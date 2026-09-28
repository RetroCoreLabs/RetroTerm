using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using RetroTerm.Core.Protocols.TelnetServer.Parsing;

namespace RetroTerm.Core.Protocols.TelnetServer.Telnet;

public class TelnetSession
{
    private readonly TcpClient _client;
    private readonly InputParser _inputParser = new InputParser();

    // Channel-based input pump
    private Channel<ParsedInput>? _inputChannel;
    private Task? _pumpTask;
    private CancellationTokenSource? _pumpCts;

    public virtual NetworkStream Stream { get; }
    public virtual TelnetNegotiator Negotiator { get; }
    public InputParser Parser => _inputParser;

    /// <summary>
    /// Remote endpoint for logging purposes
    /// </summary>
    public string RemoteEndPoint => _client.Client?.RemoteEndPoint?.ToString() ?? "unknown";

    public TelnetSession(TcpClient client)
    {
        _client = client;
        Stream = client.GetStream();
        Negotiator = new TelnetNegotiator(Stream);
    }

    public async Task WriteAsync(string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        await Stream.WriteAsync(bytes);
        await Stream.FlushAsync();
    }

    /// <summary>
    /// Writes raw bytes directly to the stream (for escape sequences)
    /// </summary>
    public async Task WriteBytesAsync(byte[] bytes)
    {
        await Stream.WriteAsync(bytes);
        await Stream.FlushAsync();
    }

    /// <summary>
    /// Writes raw bytes directly to the stream (for escape sequences)
    /// </summary>
    public async Task WriteBytesAsync(ReadOnlyMemory<byte> bytes)
    {
        await Stream.WriteAsync(bytes);
        await Stream.FlushAsync();
    }

    /// <summary>
    /// Flushes the underlying stream
    /// </summary>
    public async Task FlushAsync()
    {
        await Stream.FlushAsync();
    }

    #region Input Pump

    /// <summary>
    /// Starts the channel-based input pump. All input from this point
    /// goes through ReadInputAsync.
    /// </summary>
    public void StartInputPump()
    {
        if (_pumpTask != null)
            throw new InvalidOperationException("Input pump is already running.");

        _inputChannel = Channel.CreateUnbounded<ParsedInput>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = true
        });
        _pumpCts = new CancellationTokenSource();
        _pumpTask = Task.Run(() => InputPumpLoopAsync(_pumpCts.Token));
    }

    /// <summary>
    /// Whether the input pump is running and <see cref="ReadInputAsync"/> can be called.
    /// </summary>
    /// <remarks>
    /// <para><b>So a caller can WAIT for the pump instead of guessing how long it takes</b></para>
    /// The pump starts partway through a session's setup, after the welcome text and the terminal
    /// detection, and detection talks to the far end. A test that starts a session in the
    /// background and then sleeps for a fixed 500 ms is betting that all of that finished, and
    /// sixteen of them lost that bet every run: the failure they reported was "input pump is not
    /// running", which describes the symptom and hides the race.
    ///
    /// Reading this in a loop turns the guess into a wait.
    /// </remarks>
    public bool IsInputPumpRunning => _inputChannel != null;

    /// <summary>
    /// Stops the input pump and cleans up resources.
    /// </summary>
    public async Task StopInputPumpAsync()
    {
        if (_pumpCts != null)
        {
            _pumpCts.Cancel();
        }

        if (_pumpTask != null)
        {
            try
            {
                await _pumpTask;
            }
            catch (OperationCanceledException)
            {
                // Expected
            }
        }

        _inputChannel?.Writer.TryComplete();
        _pumpTask = null;
        _pumpCts?.Dispose();
        _pumpCts = null;
        _inputChannel = null;
    }

    /// <summary>
    /// Blocking read from the input channel. Returns the next parsed input.
    /// Throws ChannelClosedException if connection closed.
    /// </summary>
    public async Task<ParsedInput> ReadInputAsync(CancellationToken ct = default)
    {
        if (_inputChannel == null)
            throw new InvalidOperationException("Input pump is not running. Call StartInputPump() first.");

        return await _inputChannel.Reader.ReadAsync(ct);
    }

    /// <summary>
    /// Reads input from the channel with a timeout. Returns null if timeout expires.
    /// Throws ChannelClosedException if connection closed.
    /// </summary>
    public async Task<ParsedInput?> TryReadInputAsync(int timeoutMs, CancellationToken ct = default)
    {
        if (_inputChannel == null)
            throw new InvalidOperationException("Input pump is not running. Call StartInputPump() first.");

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeoutMs);
        try
        {
            return await _inputChannel.Reader.ReadAsync(cts.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return null; // Timeout — not caller cancellation
        }
    }

    /// <summary>
    /// Reads input until Enter is received. Discards all non-Enter input.
    /// </summary>
    public async Task WaitForEnterAsync(CancellationToken ct = default)
    {
        while (true)
        {
            var input = await ReadInputAsync(ct);
            if (input.Type == InputType.Enter)
                return;
        }
    }

    /// <summary>
    /// Drains all pending input from the channel, parser queue, and raw stream.
    /// </summary>
    public async Task DrainInputAsync()
    {
        // Clear parser queue
        while (_inputParser.TryGetNext(out _)) { }

        // Clear channel
        if (_inputChannel != null)
        {
            while (_inputChannel.Reader.TryRead(out _)) { }
        }

        // Drain raw stream
        var buffer = new byte[256];
        while (Stream.DataAvailable)
        {
            var n = await Stream.ReadAsync(buffer);
            if (n <= 0) break;
        }
    }

    private async Task InputPumpLoopAsync(CancellationToken ct)
    {
        var buf = new byte[1024];
        try
        {
            while (!ct.IsCancellationRequested)
            {
                // Check timeouts on incomplete escape sequences
                _inputParser.CheckTimeout();

                // Drain any queued items from parser
                while (_inputParser.TryGetNext(out var p))
                {
                    await _inputChannel!.Writer.WriteAsync(p, ct);
                }

                if (Stream.DataAvailable)
                {
                    int n = await Stream.ReadAsync(buf, 0, buf.Length, ct);
                    if (n == 0)
                    {
                        // Connection closed
                        _inputChannel!.Writer.TryComplete();
                        return;
                    }

                    var stripped = TelnetCodec.StripIac(buf.AsSpan(0, n));
                    if (!string.IsNullOrEmpty(stripped))
                    {
                        _inputParser.Feed(stripped);
                    }

                    // Drain parsed items
                    while (_inputParser.TryGetNext(out var p2))
                    {
                        await _inputChannel!.Writer.WriteAsync(p2, ct);
                    }
                }
                else
                {
                    await Task.Delay(10, ct);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Normal cancellation
        }
        catch (Exception ex)
        {
            System.Console.WriteLine($"[TelnetSession] Input pump error: {ex.GetType().Name}: {ex.Message}");
            _inputChannel?.Writer.TryComplete(ex);
        }
    }

    #endregion

    #region Pre-pump methods (used before StartInputPump)

    /// <summary>
    /// Reads raw bytes from the stream with optional timeout.
    /// Used internally for Telnet-level negotiation and response capture.
    /// Must be called BEFORE StartInputPump().
    /// </summary>
    protected internal virtual async Task<int> ReadRawAsync(byte[] buffer, int timeoutMs = 200)
    {
        // If data is available, read immediately
        if (Stream.DataAvailable)
        {
            var bytesRead = await Stream.ReadAsync(buffer);
            if (bytesRead > 0)
            {
                System.Console.WriteLine($"[TelnetSession] ReadRawAsync: Read {bytesRead} bytes: {BitConverter.ToString(buffer, 0, Math.Min(bytesRead, 20))}");
            }
            return bytesRead;
        }

        // No data available - do a blocking read with timeout using CancellationToken
        using var cts = new CancellationTokenSource(timeoutMs);
        try
        {
            var bytesRead = await Stream.ReadAsync(buffer, cts.Token);
            if (bytesRead > 0)
            {
                System.Console.WriteLine($"[TelnetSession] ReadRawAsync: Read {bytesRead} bytes after wait: {BitConverter.ToString(buffer, 0, Math.Min(bytesRead, 50))}");
            }
            return bytesRead;
        }
        catch (OperationCanceledException)
        {
            // Timeout - no data arrived
            return 0;
        }
    }

    /// <summary>
    /// Attempts to parse TERMINAL-TYPE subnegotiation from received data
    /// </summary>
    public bool TryParseTerminalType(ReadOnlySpan<byte> data, out string terminalType)
    {
        terminalType = "unknown";

        // Look for TERMINAL-TYPE subnegotiation in the buffer
        for (int i = 0; i < data.Length - 4; i++)
        {
            if (Negotiator.ParseTerminalTypeSubnegotiation(data, i, out terminalType, out _))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Detects terminal type via TERMINAL-TYPE Telnet subnegotiation.
    /// Reads raw bytes (needs IAC parsing) and extracts TERMINAL-TYPE value.
    /// Must be called BEFORE StartInputPump().
    /// </summary>
    public async Task<string?> DetectTerminalTypeAsync(int timeoutMs = 1000)
    {
        var buffer = new byte[256];
        var timeout = DateTime.Now.AddMilliseconds(timeoutMs);

        while (DateTime.Now < timeout)
        {
            if (Stream.DataAvailable)
            {
                var bytesRead = await ReadRawAsync(buffer);
                if (bytesRead > 0)
                {
                    if (TryParseTerminalType(buffer.AsSpan(0, bytesRead), out var terminalType))
                    {
                        return terminalType;
                    }
                }
            }
            await Task.Delay(50);
        }

        return null;
    }

    /// <summary>
    /// Drains all pending input from the stream and parser queue (pre-pump version).
    /// </summary>
    public async Task DrainAsync()
    {
        // Clear parser queue
        while (_inputParser.TryGetNext(out _)) { }

        // Drain raw stream
        var buffer = new byte[256];
        while (Stream.DataAvailable)
        {
            var n = await Stream.ReadAsync(buffer);
            if (n <= 0) break;
        }
    }

    #endregion

}
