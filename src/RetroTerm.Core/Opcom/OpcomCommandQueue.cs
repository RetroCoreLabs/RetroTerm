using System;
using System.Collections.Generic;
using System.Threading;

namespace RetroTerm.Core.Opcom;

/// <summary>
/// Thread-safe sequential command queue for OPCOM operations.
/// Only one command executes at a time.
/// </summary>
public sealed class OpcomCommandQueue
{
    private readonly object _lock = new();
    private readonly Queue<OpcomCommand> _queue = new();
    private OpcomCommand? _current;

    /// <summary>
    /// Gets whether a command is currently being executed.
    /// </summary>
    public bool IsBusy
    {
        get { lock (_lock) return _current != null; }
    }

    /// <summary>
    /// Gets the number of pending commands.
    /// </summary>
    public int Count
    {
        get { lock (_lock) return _queue.Count; }
    }

    /// <summary>
    /// Enqueues a command for execution.
    /// </summary>
    public void Enqueue(OpcomCommand command)
    {
        lock (_lock)
        {
            _queue.Enqueue(command);
        }
        CommandEnqueued?.Invoke();
    }

    /// <summary>
    /// Dequeues the next command. Returns null if queue is empty or a command is already in progress.
    /// </summary>
    public OpcomCommand? TryDequeue()
    {
        lock (_lock)
        {
            if (_current != null) return null;
            if (_queue.Count == 0) return null;
            _current = _queue.Dequeue();
            return _current;
        }
    }

    /// <summary>
    /// Marks the current command as complete with the given result.
    /// </summary>
    public void Complete(OpcomResult result)
    {
        OpcomCommand? cmd;
        lock (_lock)
        {
            cmd = _current;
            _current = null;
        }
        cmd?.Completion.TrySetResult(result);
    }

    /// <summary>
    /// Cancels all pending commands and the current command.
    /// </summary>
    public void CancelAll(string reason = "Cancelled")
    {
        OpcomCommand? current;
        OpcomCommand[] pending;
        lock (_lock)
        {
            current = _current;
            _current = null;
            pending = new OpcomCommand[_queue.Count];
            int idx = 0;
            while (_queue.Count > 0)
            {
                pending[idx++] = _queue.Dequeue();
            }
        }

        var failResult = OpcomResult.Fail(reason);
        current?.Completion.TrySetResult(failResult);
        for (int i = 0; i < pending.Length; i++)
        {
            pending[i].Completion.TrySetResult(failResult);
        }
    }

    /// <summary>
    /// Event raised when a command is enqueued (to wake up the protocol processor).
    /// </summary>
    public event Action? CommandEnqueued;
}
