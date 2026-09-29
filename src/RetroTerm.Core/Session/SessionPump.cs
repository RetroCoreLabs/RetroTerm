using System;
using System.Buffers;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using RetroTerm.Core.Logging;

namespace RetroTerm.Core.Session;

/// <summary>
/// Delegate for processing a chunk of received data on the pump thread.
/// Span-based so the pump can hand out pooled buffers without exposing them.
/// </summary>
internal delegate void PumpDataHandler(ReadOnlySpan<byte> data);

/// <summary>
/// Single-writer processing pump for a terminal session.
///
/// THE THREADING RULE (this header is where it lives; docs\MCP-AND-SCRIPTING.md section 4 points here):
///   - The network receive thread PRODUCES: it only copies bytes into this pump and returns.
///   - The pump task is the ONLY code allowed to mutate the emulator/buffer.
///   - UI, MCP and scripts CONSUME: they get events/snapshots, or run a job on the pump
///     via <see cref="RunAsync(Action)"/> for serialized access to the emulator state.
///
/// Because there is exactly one consumer, emulator/buffer access needs no locks in the
/// hot path. Data buffers are rented from <see cref="ArrayPool{T}"/> and returned after
/// processing (zero steady-state allocation). The channel is bounded: if the emulator
/// falls behind, the producing network thread blocks (backpressure) instead of the
/// queue growing without limit.
/// </summary>
internal sealed class SessionPump : IDisposable
{
    /// <summary>
    /// Bounded queue depth. 256 chunks of typical telnet reads (up to a few KB each)
    /// is far more than a terminal ever needs; the bound exists to create backpressure,
    /// not to be reached in normal operation.
    /// </summary>
    private const int QueueCapacity = 256;

    /// <summary>
    /// One work item: either a data chunk (Buffer != null, pooled) or a job (Job != null).
    /// Struct so queueing a data chunk allocates nothing beyond the pooled buffer.
    /// </summary>
    private struct PumpItem
    {
        public byte[]? Buffer;   // rented from ArrayPool<byte>.Shared; returned by the pump
        public int Length;       // valid bytes in Buffer
        public Action? Job;      // serialized job (reads/commands); allocation here is fine, jobs are not the hot path
        public TaskCompletionSource<object?>? Completion; // completes when Job has run
    }

    private readonly Channel<PumpItem> _channel;
    private readonly PumpDataHandler _processData;
    private readonly Action<Exception>? _onError;
    private readonly Task _pumpTask;
    private volatile bool _disposed;

    /// <summary>
    /// Creates the pump and starts its processing task immediately.
    /// The pump idles (no CPU) while the queue is empty and lives until Dispose.
    /// </summary>
    /// <param name="processData">
    /// Called on the pump thread for every received data chunk.
    /// </param>
    /// <param name="onError">
    /// Called on the pump thread when processData throws. The pump itself survives.
    /// </param>
    public SessionPump(PumpDataHandler processData, Action<Exception>? onError)
    {
        _processData = processData ?? throw new ArgumentNullException(nameof(processData));
        _onError = onError;

        _channel = Channel.CreateBounded<PumpItem>(new BoundedChannelOptions(QueueCapacity)
        {
            SingleReader = true,   // exactly one pump task — this is what makes it single-writer for the emulator
            SingleWriter = false,  // network thread, UI thread (local writes), MCP threads may all enqueue
            FullMode = BoundedChannelFullMode.Wait
        });

        // Long-running: the pump is one dedicated logical thread per session.
        _pumpTask = Task.Factory.StartNew(
            PumpLoopAsync,
            CancellationToken.None,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default).Unwrap();
    }

    /// <summary>
    /// Hands a received data chunk to the pump. Called on the network receive thread.
    /// Copies the data into a pooled buffer — the caller's memory may be reused as soon
    /// as this returns (connection implementations recycle their read buffers).
    /// If the queue is full the calling thread blocks until space frees: that block IS
    /// the backpressure that stops a fast sender from outrunning the emulator.
    /// </summary>
    public void PostData(ReadOnlySpan<byte> data)
    {
        if (_disposed || data.Length == 0)
        {
            return;
        }

        var rented = ArrayPool<byte>.Shared.Rent(data.Length);
        data.CopyTo(rented);
        var item = new PumpItem { Buffer = rented, Length = data.Length };

        if (_channel.Writer.TryWrite(item))
        {
            return;
        }

        // Queue full (backpressure) or completed. WriteAsync waits for space; if the
        // channel is completed it throws and we must return the rented buffer ourselves.
        try
        {
            var write = _channel.Writer.WriteAsync(item);
            if (!write.IsCompletedSuccessfully)
            {
                write.AsTask().GetAwaiter().GetResult();
            }
        }
        catch (ChannelClosedException)
        {
            ArrayPool<byte>.Shared.Return(rented);
        }
    }

    /// <summary>
    /// Runs a job on the pump thread, serialized with data processing.
    /// This is THE way to touch the emulator/buffer from any other thread
    /// (screen reads, wait-for checks, commands). The returned task completes
    /// after all previously queued data has been processed and the job has run.
    /// </summary>
    public Task RunAsync(Action job)
    {
        if (job == null) throw new ArgumentNullException(nameof(job));
        if (_disposed)
        {
            return Task.FromException(new ObjectDisposedException(nameof(SessionPump)));
        }

        var tcs = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var item = new PumpItem { Job = job, Completion = tcs };

        if (!_channel.Writer.TryWrite(item))
        {
            try
            {
                var write = _channel.Writer.WriteAsync(item);
                if (!write.IsCompletedSuccessfully)
                {
                    write.AsTask().GetAwaiter().GetResult();
                }
            }
            catch (ChannelClosedException)
            {
                tcs.TrySetException(new ObjectDisposedException(nameof(SessionPump)));
            }
        }

        return tcs.Task;
    }

    /// <summary>
    /// Runs a value-returning job on the pump thread, serialized with data processing.
    /// Used for screen reads: the job executes race-free against the emulator because
    /// the pump is the buffer's single writer.
    /// </summary>
    public Task<T> RunAsync<T>(Func<T> job)
    {
        if (job == null) throw new ArgumentNullException(nameof(job));

        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        // Statement-bodied lambda ON PURPOSE: `() => tcs.TrySetResult(job())` returns
        // bool and would bind to THIS generic overload again — infinite recursion.
        var enqueued = RunAsync(() => { tcs.TrySetResult(job()); });

        // If the wrapper job faulted (job() threw) or the pump is disposed, the
        // enqueued task carries the exception — forward it to the typed task.
        enqueued.ContinueWith(t =>
        {
            if (t.IsFaulted)
            {
                tcs.TrySetException(t.Exception!.InnerExceptions);
            }
            else if (t.IsCanceled)
            {
                tcs.TrySetCanceled();
            }
        }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);

        return tcs.Task;
    }

    /// <summary>
    /// Completes when everything queued before this call has been processed.
    /// Deterministic flush for tests and for callers that need "the screen now
    /// reflects all bytes received so far" (replaces Task.Delay guessing).
    /// </summary>
    public Task FlushAsync() => RunAsync(static () => { });

    /// <summary>
    /// The pump loop — the single consumer. Runs until the channel is completed by Dispose.
    /// A throwing processData or job must never kill the pump: the connection keeps
    /// delivering and a dead pump would silently freeze the terminal.
    /// </summary>
    private async Task PumpLoopAsync()
    {
        var reader = _channel.Reader;

        while (await reader.WaitToReadAsync().ConfigureAwait(false))
        {
            while (reader.TryRead(out var item))
            {
                ProcessItem(ref item);
            }
        }

        // Channel completed: drain anything still queued so pooled buffers are returned
        // and pending jobs are not left hanging forever.
        while (reader.TryRead(out var leftover))
        {
            if (leftover.Buffer != null)
            {
                ArrayPool<byte>.Shared.Return(leftover.Buffer);
            }
            leftover.Completion?.TrySetException(new ObjectDisposedException(nameof(SessionPump)));
        }
    }

    private void ProcessItem(ref PumpItem item)
    {
        if (item.Job != null)
        {
            try
            {
                item.Job();
                item.Completion?.TrySetResult(null);
            }
            catch (Exception ex)
            {
                // The job's exception belongs to the caller awaiting it, not the pump.
                item.Completion?.TrySetException(ex);
            }
            return;
        }

        try
        {
            _processData(new ReadOnlySpan<byte>(item.Buffer, 0, item.Length));
        }
        catch (Exception ex)
        {
            ApplicationLogger.Log(LogCategory.Session, LogLevel.Error, "SessionPump",
                $"Data processing threw: {ex}");
            _onError?.Invoke(ex);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(item.Buffer!);
        }
    }

    /// <summary>
    /// Completes the channel; the pump drains remaining items (returning pooled buffers)
    /// and exits. Does NOT block waiting for the pump task — Dispose may be called from
    /// a UI thread or even from a pump job, and blocking there could deadlock.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        _channel.Writer.TryComplete();
    }
}
