using System;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using RetroTerm.Core.Logging;
using RetroTerm.Core.Protocols;

namespace RetroTerm.Core.Protocols.Net;

/// <summary>
/// Telnet protocol implementation (RFC 854)
/// Supports basic Telnet commands and option negotiation
/// </summary>
public class TelnetConnection : IConnection
{
    private TcpClient? _client;
    private NetworkStream? _stream;
    private CancellationTokenSource? _receiveCts;
    private Task? _receiveTask;

    private readonly string _host;
    private readonly int _port;

    // Telnet protocol constants (RFC 854)
    private const byte IAC = 255;  // Interpret As Command
    private const byte DONT = 254; // Don't do option
    private const byte DO = 253;   // Do option
    private const byte WONT = 252; // Won't do option
    private const byte WILL = 251; // Will do option
    private const byte SB = 250;   // Subnegotiation begin
    private const byte SE = 240;   // Subnegotiation end

    // Telnet options (RFC 854, 1073, 1091, 1096)
    private const byte ECHO = 1;           // RFC 857
    private const byte SUPPRESS_GO_AHEAD = 3; // RFC 858
    private const byte TERMINAL_TYPE = 24; // RFC 1091
    private const byte NAWS = 31;          // RFC 1073 - Negotiate About Window Size
    private const byte TERMINAL_SPEED = 32; // RFC 1079
    private const byte LINEMODE = 34;      // RFC 1184

    public ConnectionStatus Status { get; private set; }
    public string ConnectionType => "Telnet";
    public string Description { get; }

    public event Action<ConnectionStatus>? StatusChanged;
    public event Action<ReadOnlyMemory<byte>>? DataReceived;
    public event Action<Exception>? ErrorOccurred;

    /// <summary>
    /// Gets or sets the terminal type to report (default: "VT100")
    /// </summary>
    public string TerminalType { get; set; } = "VT100";

    /// <summary>
    /// Gets or sets the terminal window size (width, height)
    /// </summary>
    public (int Width, int Height) WindowSize { get; set; } = (80, 24);

    public TelnetConnection(string host, int port = 23)
    {
        if (string.IsNullOrWhiteSpace(host))
            throw new ArgumentException("Host cannot be empty", nameof(host));
        if (port <= 0 || port > 65535)
            throw new ArgumentException("Port must be between 1 and 65535", nameof(port));

        _host = host;
        _port = port;
        Description = $"{host}:{port}";
        Status = ConnectionStatus.Disconnected;
    }

    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        if (Status != ConnectionStatus.Disconnected)
            throw new InvalidOperationException($"Cannot connect when status is {Status}");

        try
        {
            SetStatus(ConnectionStatus.Connecting);

            _client = new TcpClient();
            await _client.ConnectAsync(_host, _port, cancellationToken);
            _stream = _client.GetStream();

            // Start receive loop
            _receiveCts = new CancellationTokenSource();
            _receiveTask = ReceiveLoopAsync(_receiveCts.Token);

            SetStatus(ConnectionStatus.Connected);
        }
        catch (Exception ex)
        {
            SetStatus(ConnectionStatus.Error);
            ErrorOccurred?.Invoke(ex);
            throw;
        }
    }

    public async Task SendAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
    {
        if (Status != ConnectionStatus.Connected || _stream == null)
            throw new InvalidOperationException($"Cannot send when status is {Status}");

        try
        {
            // Escape IAC bytes (send IAC IAC for literal IAC)
            var escaped = EscapeIAC(data.Span);

            // One line per write, at Trace. The decoded view of these bytes comes from the
            // Protocol Monitor; this only records that they reached the socket. The IAC-escaped
            // length is included because a differing length is the one interesting fact here.
            if (ApplicationLogger.IsEnabled(LogCategory.Network, LogLevel.Trace))
            {
                ApplicationLogger.Log(LogCategory.Network, LogLevel.Trace, "TelnetConnection",
                    escaped.Length == data.Length
                        ? $"write {data.Length} bytes"
                        : $"write {data.Length} bytes ({escaped.Length} after IAC escaping)",
                    escaped);
            }

            await _stream.WriteAsync(escaped, cancellationToken);
            await _stream.FlushAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            ApplicationLogger.Log(LogCategory.Network, LogLevel.Error, "TelnetConnection",
                $"SendAsync failed: {ex.Message}");
            SetStatus(ConnectionStatus.Error);
            ErrorOccurred?.Invoke(ex);
            throw;
        }
    }

    public async Task DisconnectAsync()
    {
        if (Status == ConnectionStatus.Disconnected)
            return;

        SetStatus(ConnectionStatus.Disconnecting);

        // Stop receive loop
        _receiveCts?.Cancel();

        if (_receiveTask != null)
        {
            try
            {
                await _receiveTask;
            }
            catch (OperationCanceledException)
            {
                // Expected
            }
        }

        // Close connection
        _stream?.Close();
        _client?.Close();

        _stream = null;
        _client = null;

        SetStatus(ConnectionStatus.Disconnected);
    }

    private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
    {
        var buffer = new byte[8192];
        var dataBuffer = new byte[8192];
        int dataLength = 0;

        ApplicationLogger.Log(LogCategory.Network, LogLevel.Info, "TelnetConnection", "Receive loop started");

        try
        {
            while (!cancellationToken.IsCancellationRequested && _stream != null)
            {
                // NOTE: the old "Waiting for data from stream..." line was emitted once per
                // iteration and carried no information - it just doubled the log volume.
                var bytesRead = await _stream.ReadAsync(buffer, cancellationToken);
                if (bytesRead == 0)
                {
                    // Connection closed by remote host
                    ApplicationLogger.Log(LogCategory.Network, LogLevel.Info, "TelnetConnection",
                        "Read 0 bytes - connection closed by remote host");
                    break;
                }

                // Raw socket read, at Trace. This is the ONLY place the on-the-wire bytes
                // (still IAC-escaped) are dumped.
                if (ApplicationLogger.IsEnabled(LogCategory.Network, LogLevel.Trace))
                {
                    var raw = new byte[bytesRead];
                    Array.Copy(buffer, raw, bytesRead);
                    ApplicationLogger.Log(LogCategory.Network, LogLevel.Trace, "TelnetConnection",
                        $"read {bytesRead} bytes", raw);
                }

                // Process telnet protocol and extract data
                for (int i = 0; i < bytesRead; i++)
                {
                    var b = buffer[i];

                    if (b == IAC && i + 1 < bytesRead)
                    {
                        // Telnet command sequence
                        i++; // Move to next byte
                        var cmd = buffer[i];

                        if (cmd == IAC)
                        {
                            // Escaped IAC (literal 255)
                            dataBuffer[dataLength++] = IAC;
                        }
                        else if (cmd == WILL || cmd == WONT || cmd == DO || cmd == DONT)
                        {
                            // Option negotiation (3-byte sequence)
                            if (i + 1 < bytesRead)
                            {
                                i++; // Move to option byte
                                var option = buffer[i];
                                await HandleOptionNegotiation(cmd, option, cancellationToken);
                            }
                        }
                        else if (cmd == SB)
                        {
                            // Subnegotiation (IAC SB option ... IAC SE)
                            var sbStart = i + 1;
                            var sbEnd = FindSubnegotiationEnd(buffer, sbStart, bytesRead);
                            if (sbEnd > 0)
                            {
                                i = sbEnd; // Skip to end of subnegotiation
                            }
                        }
                        // Other commands (SE, etc.) are ignored or handled above
                    }
                    else
                    {
                        // Regular data
                        dataBuffer[dataLength++] = b;
                    }

                    // Flush data buffer when full or at end of received data
                    if (dataLength >= dataBuffer.Length - 1 || i == bytesRead - 1)
                    {
                        if (dataLength > 0)
                        {
                            // The de-escaped payload is traced by TerminalSession, which is the
                            // single RX feed point. Re-dumping it here (and again after the
                            // event fired) is what produced the duplicated log.
                            DataReceived?.Invoke(new ReadOnlyMemory<byte>(dataBuffer, 0, dataLength));
                            dataLength = 0;
                        }
                    }
                }
            }

            // Normal exit from receive loop - connection was closed by remote host
            if (!cancellationToken.IsCancellationRequested && Status == ConnectionStatus.Connected)
            {
                SetStatus(ConnectionStatus.Disconnected);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected when disconnecting
        }
        catch (IOException)
        {
            // Connection lost
            SetStatus(ConnectionStatus.Disconnected);
            ErrorOccurred?.Invoke(new IOException("Connection lost"));
        }
        catch (Exception ex)
        {
            SetStatus(ConnectionStatus.Disconnected);
            ErrorOccurred?.Invoke(ex);
        }
    }

    private async Task HandleOptionNegotiation(byte command, byte option, CancellationToken cancellationToken)
    {
        // Respond to option negotiations
        byte[] response;

        switch (command)
        {
            case WILL:
                // Server wants to enable an option
                if (option == ECHO || option == SUPPRESS_GO_AHEAD)
                {
                    // Accept ECHO and SUPPRESS_GO_AHEAD
                    response = new byte[] { IAC, DO, option };
                }
                else
                {
                    // Reject other options
                    response = new byte[] { IAC, DONT, option };
                }
                break;

            case DO:
                // Server wants us to enable an option
                if (option == TERMINAL_TYPE || option == NAWS)
                {
                    // Accept TERMINAL_TYPE and NAWS
                    response = new byte[] { IAC, WILL, option };

                    // Send subnegotiation if needed
                    if (option == TERMINAL_TYPE)
                    {
                        await SendTerminalType(cancellationToken);
                    }
                    else if (option == NAWS)
                    {
                        await SendWindowSize(cancellationToken);
                    }
                }
                else
                {
                    // Reject other options
                    response = new byte[] { IAC, WONT, option };
                }
                break;

            case WONT:
            case DONT:
                // Server is refusing an option (no response needed typically)
                return;

            default:
                return;
        }

        if (_stream != null)
        {
            await _stream.WriteAsync(response, cancellationToken);
            await _stream.FlushAsync(cancellationToken);
        }
    }

    private async Task SendTerminalType(CancellationToken cancellationToken)
    {
        // RFC 1091: IAC SB TERMINAL_TYPE IS "terminal" IAC SE
        var termBytes = Encoding.ASCII.GetBytes(TerminalType);
        var buffer = new byte[6 + termBytes.Length];
        buffer[0] = IAC;
        buffer[1] = SB;
        buffer[2] = TERMINAL_TYPE;
        buffer[3] = 0; // IS
        Array.Copy(termBytes, 0, buffer, 4, termBytes.Length);
        buffer[4 + termBytes.Length] = IAC;
        buffer[5 + termBytes.Length] = SE;

        if (_stream != null)
        {
            await _stream.WriteAsync(buffer, cancellationToken);
            await _stream.FlushAsync(cancellationToken);
        }
    }

    private async Task SendWindowSize(CancellationToken cancellationToken)
    {
        // RFC 1073: IAC SB NAWS WIDTH[2] HEIGHT[2] IAC SE
        var buffer = new byte[9];
        buffer[0] = IAC;
        buffer[1] = SB;
        buffer[2] = NAWS;
        buffer[3] = (byte)(WindowSize.Width >> 8);
        buffer[4] = (byte)(WindowSize.Width & 0xFF);
        buffer[5] = (byte)(WindowSize.Height >> 8);
        buffer[6] = (byte)(WindowSize.Height & 0xFF);
        buffer[7] = IAC;
        buffer[8] = SE;

        if (_stream != null)
        {
            await _stream.WriteAsync(buffer, cancellationToken);
            await _stream.FlushAsync(cancellationToken);
        }
    }

    /// <summary>
    /// Updates the window size and notifies the server (if connected)
    /// </summary>
    public async Task UpdateWindowSizeAsync(int width, int height)
    {
        WindowSize = (width, height);

        if (Status == ConnectionStatus.Connected)
        {
            await SendWindowSize(CancellationToken.None);
        }
    }

    /// <summary>
    /// Tells the server the new size with RFC 1073 NAWS.
    /// </summary>
    /// <remarks>
    /// The interface's wording, forwarding to the Telnet-shaped one above. Both names are kept:
    /// this is what callers use, and <see cref="UpdateWindowSizeAsync"/> is what the Telnet code
    /// and its tests already say.
    /// </remarks>
    /// <param name="columns">
    /// New width in character cells.
    /// </param>
    /// <param name="rows">
    /// New height in character cells.
    /// </param>
    /// <param name="cancellationToken">
    /// Cancels the send.
    /// </param>
    /// <returns>
    /// A task that completes once NAWS has gone out, or immediately when not connected.
    /// </returns>
    public Task ResizeTerminalAsync(int columns, int rows, CancellationToken cancellationToken = default)
        => UpdateWindowSizeAsync(columns, rows);

    private static int FindSubnegotiationEnd(byte[] buffer, int start, int length)
    {
        for (int i = start; i < length - 1; i++)
        {
            if (buffer[i] == IAC && buffer[i + 1] == SE)
            {
                return i + 1; // Return index of SE
            }
        }
        return -1; // Not found
    }

    private static byte[] EscapeIAC(ReadOnlySpan<byte> data)
    {
        // Count IAC bytes to determine buffer size
        int iacCount = 0;
        for (int i = 0; i < data.Length; i++)
        {
            if (data[i] == IAC)
                iacCount++;
        }

        if (iacCount == 0)
        {
            // No IAC bytes, return as-is
            return data.ToArray();
        }

        // Create buffer with space for escaped IACs
        var result = new byte[data.Length + iacCount];
        int writePos = 0;

        for (int i = 0; i < data.Length; i++)
        {
            result[writePos++] = data[i];
            if (data[i] == IAC)
            {
                result[writePos++] = IAC; // Escape IAC with another IAC
            }
        }

        return result;
    }

    private void SetStatus(ConnectionStatus newStatus)
    {
        if (Status != newStatus)
        {
            Status = newStatus;
            StatusChanged?.Invoke(newStatus);
        }
    }

    public void Dispose()
    {
        // Cancel receive loop first (don't wait - it can deadlock)
        // Wrap in try-catch because Cancel throws if already disposed
        try { _receiveCts?.Cancel(); } catch (ObjectDisposedException) { }

        // Close stream and client immediately (this unblocks any pending reads)
        try { _stream?.Close(); } catch { }
        try { _client?.Close(); } catch { }

        // Dispose resources
        try { _receiveCts?.Dispose(); } catch { }
        _stream?.Dispose();
        _client?.Dispose();

        SetStatus(ConnectionStatus.Disconnected);
    }
}

