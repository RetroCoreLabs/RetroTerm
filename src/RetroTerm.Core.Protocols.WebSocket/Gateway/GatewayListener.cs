using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace RetroTerm.Core.Protocols.WebSocket.Gateway;

/// <summary>
/// Singleton WebSocket server that listens for connections from the ND-100 emulator.
/// Routes terminal I/O by identCode to per-terminal GatewayConnection instances.
/// Handles disk I/O from a second WebSocket connection (disk sub-worker).
/// </summary>
public class GatewayListener : IDisposable
{
    private TcpListener? _tcpListener;
    private CancellationTokenSource? _listenerCts;
    private Task? _listenerTask;
    private System.Net.WebSockets.WebSocket? _emulatorSocket;
    private Task? _receiveTask;
    private CancellationTokenSource? _receiveCts;
    private readonly object _lock = new();
    private Channel<(byte[] Data, string DebugLabel, bool IsBinary)>? _sendChannel;
    private Task? _sendTask;
    private bool _disposed;

    // Disk worker (second WebSocket connection)
    private System.Net.WebSockets.WebSocket? _diskSocket;
    private Channel<byte[]>? _diskSendChannel;
    private Task? _diskSendTask;
    private Task? _diskReceiveTask;
    private CancellationTokenSource? _diskReceiveCts;
    private readonly Dictionary<int, FileStream> _smdFiles = new();
    private readonly Dictionary<int, FileStream> _floppyFiles = new();
    private GatewayDiskSettings? _diskSettings;

    // Ethernet (0x30/0x31/0x32). The bridge owns the host mapping; this class only routes
    // frames to and from it - see GatewayEthernetBridge for why they are separate.
    private Ethernet.GatewayEthernetBridge? _ethernet;

    /// <summary>
    /// Registered terminals from the emulator's most recent 'register' message.
    /// </summary>
    private readonly List<GatewayTerminalInfo> _terminals = new();

    /// <summary>
    /// Active per-terminal connections, keyed by identCode.
    /// </summary>
    private readonly ConcurrentDictionary<int, GatewayConnection> _activeConnections = new();

    /// <summary>
    /// Gateway I/O statistics.
    /// </summary>
    public GatewayStatistics Statistics { get; } = new();

    /// <summary>
    /// The Ethernet mapping, or null while none has been configured. Read by the settings and
    /// statistics windows to show what the guest's wire is actually attached to.
    /// </summary>
    public Ethernet.GatewayEthernetBridge? Ethernet => _ethernet;

    /// <summary>
    /// Point the emulated machine's Ethernet at a host mapping - a real adapter, a RETH
    /// segment, a multicast group, or nothing. Safe to call while running: the previous
    /// mapping is stopped first, so this is also how the mapping is changed.
    /// </summary>
    /// <param name="spec">A mapping spec; see <see cref="Ethernet.EthernetBackendFactory"/>.</param>
    /// <param name="segment">The emulated segment number these frames belong to.</param>
    public void ConfigureEthernet(string? spec, int segment)
    {
        _ethernet ??= new Ethernet.GatewayEthernetBridge(
            (frame, label) => EnqueueSend(frame, label, true));

        _ethernet.Configure(spec, segment);
    }

    /// <summary>
    /// The port this listener is bound to.
    /// </summary>
    public int Port { get; private set; }

    /// <summary>
    /// Whether the listener is currently accepting connections.
    /// </summary>
    public bool IsListening { get; private set; }

    /// <summary>
    /// Whether an emulator is currently connected.
    /// </summary>
    public bool IsEmulatorConnected
    {
        get
        {
            lock (_lock)
            {
                return _emulatorSocket != null &&
                       _emulatorSocket.State == WebSocketState.Open;
            }
        }
    }

    /// <summary>
    /// Whether the disk worker WebSocket is currently connected.
    /// </summary>
    public bool IsDiskWorkerConnected
    {
        get
        {
            lock (_lock)
            {
                return _diskSocket != null &&
                       _diskSocket.State == WebSocketState.Open;
            }
        }
    }

    /// <summary>
    /// Gets a snapshot of the currently registered terminals.
    /// </summary>
    public List<GatewayTerminalInfo> GetTerminals()
    {
        lock (_lock)
        {
            var result = new List<GatewayTerminalInfo>(_terminals.Count);
            for (int i = 0; i < _terminals.Count; i++)
            {
                result.Add(_terminals[i]);
            }
            return result;
        }
    }

    /// <summary>
    /// Returns the number of registered terminals.
    /// </summary>
    public int TerminalCount
    {
        get
        {
            lock (_lock)
            {
                return _terminals.Count;
            }
        }
    }

    /// <summary>
    /// Checks if a specific identCode is currently connected (has an active GatewayConnection).
    /// </summary>
    public bool IsIdentCodeInUse(int identCode)
    {
        return _activeConnections.ContainsKey(identCode);
    }

    // ───────────────────────────────────────────────────────────────────
    // Events
    // ───────────────────────────────────────────────────────────────────

    /// <summary>
    /// Fired when the terminal list changes (emulator registers or re-registers).
    /// </summary>
    public event Action? TerminalListChanged;

    /// <summary>
    /// Fired when the emulator WebSocket connects.
    /// </summary>
    public event Action? EmulatorConnected;

    /// <summary>
    /// Fired when the emulator WebSocket disconnects.
    /// </summary>
    public event Action? EmulatorDisconnected;

    /// <summary>
    /// Fired when the disk worker WebSocket connects.
    /// </summary>
    public event Action? DiskWorkerConnected;

    /// <summary>
    /// Fired when the disk worker WebSocket disconnects.
    /// </summary>
    public event Action? DiskWorkerDisconnected;

    /// <summary>
    /// Fired when statistics are updated (after each disk op, or periodically for term I/O).
    /// </summary>
    public event Action? StatisticsUpdated;

    /// <summary>
    /// Raised whenever the listener starts or stops.
    ///
    /// Without this, nothing tells a status bar that the gateway was switched OFF: the other
    /// events all describe an emulator or disk worker, and stopping the listener produces none
    /// of them. The indicator kept its last text and stayed on screen advertising a port that
    /// was no longer open.
    /// </summary>
    public event Action? ListeningChanged;

    /// <summary>
    /// Fired for debug/diagnostic messages showing all WebSocket traffic.
    /// Direction: "TX" = sent to emulator, "RX" = received from emulator, "EVT" = lifecycle event.
    /// </summary>
    public event Action<string, string>? DebugMessage;

    private void LogDebug(string direction, string message)
    {
        DebugMessage?.Invoke(direction, message);
    }

    // ───────────────────────────────────────────────────────────────────
    // Lifecycle
    // ───────────────────────────────────────────────────────────────────

    /// <summary>
    /// Start listening on the specified port.
    /// </summary>
    public async Task StartAsync(int port)
    {
        if (IsListening)
            await StopAsync();

        Port = port;
        _listenerCts = new CancellationTokenSource();
        _tcpListener = new TcpListener(IPAddress.Any, port);
        _tcpListener.Start();
        IsListening = true;
        ListeningChanged?.Invoke();

        LogDebug("EVT", $"Listening on port {port}");
        _listenerTask = AcceptLoopAsync(_listenerCts.Token);
    }

    /// <summary>
    /// Stop the listener and disconnect any active emulator connection.
    /// </summary>
    public async Task StopAsync()
    {
        LogDebug("EVT", "Stopping listener...");
        IsListening = false;
        ListeningChanged?.Invoke();

        _listenerCts?.Cancel();

        try { _tcpListener?.Stop(); }
        catch { /* ignore */ }

        // Disconnect disk worker first, then emulator
        await DisconnectDiskWorkerAsync();
        await DisconnectEmulatorAsync();

        if (_listenerTask != null)
        {
            try { await _listenerTask; }
            catch (OperationCanceledException) { }
            catch { /* ignore */ }
        }

        _listenerCts?.Dispose();
        _listenerCts = null;
        _tcpListener = null;
        _listenerTask = null;
    }

    // ───────────────────────────────────────────────────────────────────
    // TCP Accept Loop
    // ───────────────────────────────────────────────────────────────────

    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            TcpClient? client = null;
            try
            {
                client = await _tcpListener!.AcceptTcpClientAsync(ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (SocketException)
            {
                break;
            }

            if (client != null)
            {
                _ = HandleTcpClientAsync(client, ct);
            }
        }
    }

    private async Task HandleTcpClientAsync(TcpClient client, CancellationToken ct)
    {
        NetworkStream? stream = null;
        try
        {
            stream = client.GetStream();

            // Read HTTP upgrade request
            var buffer = ArrayPool<byte>.Shared.Rent(4096);
            try
            {
                int totalRead = 0;
                while (totalRead < buffer.Length)
                {
                    var bytesRead = await stream.ReadAsync(buffer, totalRead, buffer.Length - totalRead, ct);
                    if (bytesRead == 0) break;
                    totalRead += bytesRead;

                    // Check if we have the full HTTP request (ends with \r\n\r\n)
                    if (totalRead >= 4)
                    {
                        bool foundEnd = false;
                        for (int i = totalRead - 4; i >= 0; i--)
                        {
                            if (buffer[i] == '\r' && buffer[i + 1] == '\n' &&
                                buffer[i + 2] == '\r' && buffer[i + 3] == '\n')
                            {
                                foundEnd = true;
                                break;
                            }
                        }
                        if (foundEnd) break;
                    }
                }

                var requestText = Encoding.UTF8.GetString(buffer, 0, totalRead);

                // Validate this is a WebSocket upgrade request
                if (!requestText.Contains("Upgrade: websocket", StringComparison.OrdinalIgnoreCase))
                {
                    var badResponse = "HTTP/1.1 400 Bad Request\r\nContent-Length: 0\r\n\r\n"u8;
                    await stream.WriteAsync(badResponse.ToArray(), ct);
                    client.Close();
                    return;
                }

                // Check current connection state
                bool emulatorConnected;
                bool diskWorkerConnected;
                lock (_lock)
                {
                    emulatorConnected = _emulatorSocket != null &&
                                        _emulatorSocket.State == WebSocketState.Open;
                    diskWorkerConnected = _diskSocket != null &&
                                          _diskSocket.State == WebSocketState.Open;
                }

                // Extract the Sec-WebSocket-Key
                var wsKey = ExtractWebSocketKey(requestText);
                if (wsKey == null)
                {
                    var badResponse = "HTTP/1.1 400 Bad Request\r\nContent-Length: 0\r\n\r\n"u8;
                    await stream.WriteAsync(badResponse.ToArray(), ct);
                    client.Close();
                    return;
                }

                // Send upgrade response
                var acceptKey = ComputeWebSocketAcceptKey(wsKey);
                var response = $"HTTP/1.1 101 Switching Protocols\r\n" +
                               $"Upgrade: websocket\r\n" +
                               $"Connection: Upgrade\r\n" +
                               $"Sec-WebSocket-Accept: {acceptKey}\r\n\r\n";
                var responseBytes = Encoding.UTF8.GetBytes(response);
                await stream.WriteAsync(responseBytes, ct);

                // Create WebSocket from upgraded stream
                var ws = System.Net.WebSockets.WebSocket.CreateFromStream(
                    stream, new WebSocketCreationOptions
                    {
                        IsServer = true,
                        KeepAliveInterval = TimeSpan.FromSeconds(30)
                    });

                if (!emulatorConnected)
                {
                    // First connection = emulator main worker
                    AcceptEmulatorConnection(ws, client, stream, ct);
                }
                else if (!diskWorkerConnected)
                {
                    // Second connection = disk sub-worker
                    AcceptDiskWorkerConnection(ws, client, stream, ct);
                }
                else
                {
                    // Third+ connection = reject
                    LogDebug("EVT", $"Rejecting connection from {client.Client.RemoteEndPoint} (emulator + disk worker already connected)");
                    await ws.CloseAsync(
                        (WebSocketCloseStatus)4000,
                        "Emulator and disk worker already connected",
                        CancellationToken.None);
                    client.Close();
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }
        catch (Exception)
        {
            try { stream?.Close(); } catch { }
            try { client.Close(); } catch { }
        }
    }

    private void AcceptEmulatorConnection(System.Net.WebSockets.WebSocket ws, TcpClient client, NetworkStream stream, CancellationToken ct)
    {
        _sendChannel = Channel.CreateUnbounded<(byte[] Data, string DebugLabel, bool IsBinary)>(
            new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });

        lock (_lock)
        {
            _emulatorSocket = ws;
        }

        _receiveCts = new CancellationTokenSource();
        var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, _receiveCts.Token);

        LogDebug("EVT", $"Emulator connected from {client.Client.RemoteEndPoint}");
        Statistics.EmulatorConnectedSince = DateTime.UtcNow;

        // Start the dedicated sender loop (single writer to WebSocket)
        _sendTask = SendLoopAsync(ws, linkedCts.Token);

        // Send current disk-list if any images are loaded
        if (_diskSettings != null && _diskSettings.Images.Count > 0)
        {
            SendDiskListJson();
        }

        EmulatorConnected?.Invoke();
        StatisticsUpdated?.Invoke();

        _receiveTask = ReceiveLoopAsync(ws, client, stream, linkedCts.Token);
    }

    private void AcceptDiskWorkerConnection(System.Net.WebSockets.WebSocket ws, TcpClient client, NetworkStream stream, CancellationToken ct)
    {
        _diskSendChannel = Channel.CreateUnbounded<byte[]>(
            new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });

        lock (_lock)
        {
            _diskSocket = ws;
        }

        _diskReceiveCts = new CancellationTokenSource();
        var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, _diskReceiveCts.Token);

        LogDebug("EVT", $"Disk worker connected from {client.Client.RemoteEndPoint}");
        Statistics.DiskWorkerConnectedSince = DateTime.UtcNow;

        // Send disk-list JSON to the disk worker
        SendDiskListJson();

        // Start disk worker send/receive loops
        _diskSendTask = DiskSendLoopAsync(ws, linkedCts.Token);
        _diskReceiveTask = DiskReceiveLoopAsync(ws, client, stream, linkedCts.Token);

        DiskWorkerConnected?.Invoke();
        StatisticsUpdated?.Invoke();
    }

    // ───────────────────────────────────────────────────────────────────
    // WebSocket Receive Loop (emulator main worker)
    // ───────────────────────────────────────────────────────────────────

    private async Task ReceiveLoopAsync(System.Net.WebSockets.WebSocket ws, TcpClient client, NetworkStream stream, CancellationToken ct)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(16384);
        MemoryStream? messageBuffer = null; // only allocated for multi-fragment messages

        try
        {
            while (!ct.IsCancellationRequested && ws.State == WebSocketState.Open)
            {
                WebSocketReceiveResult result;
                try
                {
                    result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), ct);
                }
                catch (OperationCanceledException) { break; }
                catch (WebSocketException) { break; }

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    try
                    {
                        if (ws.State == WebSocketState.CloseReceived)
                        {
                            await ws.CloseOutputAsync(
                                WebSocketCloseStatus.NormalClosure,
                                "",
                                CancellationToken.None);
                        }
                    }
                    catch { /* ignore close errors */ }
                    break;
                }

                if (result.EndOfMessage && messageBuffer == null)
                {
                    // Fast path: single-fragment message — process directly from buffer, no copy
                    if (result.MessageType == WebSocketMessageType.Binary)
                    {
                        HandleBinaryFrame(buffer, result.Count);
                    }
                    else if (result.MessageType == WebSocketMessageType.Text)
                    {
                        ProcessMessage(buffer, result.Count);
                    }
                }
                else
                {
                    // Multi-fragment: accumulate
                    messageBuffer ??= new MemoryStream();
                    messageBuffer.Write(buffer, 0, result.Count);

                    if (result.EndOfMessage)
                    {
                        if (result.MessageType == WebSocketMessageType.Binary)
                        {
                            HandleBinaryFrame(messageBuffer.GetBuffer(), (int)messageBuffer.Length);
                        }
                        else if (result.MessageType == WebSocketMessageType.Text)
                        {
                            ProcessMessage(messageBuffer.GetBuffer(), (int)messageBuffer.Length);
                        }
                        messageBuffer.SetLength(0);
                    }
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            LogDebug("EVT", $"Receive loop error: {ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            // Stop the send loop
            _sendChannel?.Writer.TryComplete();
            if (_sendTask != null)
            {
                try { await _sendTask; } catch { }
            }

            // Emulator disconnected — notify all active connections
            await OnEmulatorDisconnectedAsync();

            lock (_lock)
            {
                _emulatorSocket = null;
                _terminals.Clear();
            }

            Statistics.EmulatorConnectedSince = null;

            ArrayPool<byte>.Shared.Return(buffer);
            messageBuffer?.Dispose();

            try { ws.Dispose(); } catch { }
            try { stream.Close(); } catch { }
            try { client.Close(); } catch { }

            EmulatorDisconnected?.Invoke();
            TerminalListChanged?.Invoke();
            StatisticsUpdated?.Invoke();
        }
    }

    // ───────────────────────────────────────────────────────────────────
    // Disk Worker Receive/Send Loops
    // ───────────────────────────────────────────────────────────────────

    private async Task DiskReceiveLoopAsync(System.Net.WebSockets.WebSocket ws, TcpClient client, NetworkStream stream, CancellationToken ct)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(65536); // larger buffer for disk data
        MemoryStream? messageBuffer = null;

        try
        {
            while (!ct.IsCancellationRequested && ws.State == WebSocketState.Open)
            {
                WebSocketReceiveResult result;
                try
                {
                    result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), ct);
                }
                catch (OperationCanceledException) { break; }
                catch (WebSocketException) { break; }

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    try
                    {
                        if (ws.State == WebSocketState.CloseReceived)
                        {
                            await ws.CloseOutputAsync(
                                WebSocketCloseStatus.NormalClosure,
                                "",
                                CancellationToken.None);
                        }
                    }
                    catch { /* ignore close errors */ }
                    break;
                }

                if (result.EndOfMessage && messageBuffer == null)
                {
                    if (result.MessageType == WebSocketMessageType.Binary)
                    {
                        HandleDiskBinaryFrame(buffer, result.Count);
                    }
                }
                else
                {
                    messageBuffer ??= new MemoryStream();
                    messageBuffer.Write(buffer, 0, result.Count);

                    if (result.EndOfMessage)
                    {
                        if (result.MessageType == WebSocketMessageType.Binary)
                        {
                            HandleDiskBinaryFrame(messageBuffer.GetBuffer(), (int)messageBuffer.Length);
                        }
                        messageBuffer.SetLength(0);
                    }
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            LogDebug("EVT", $"Disk receive loop error: {ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            // Stop the disk send loop
            _diskSendChannel?.Writer.TryComplete();
            if (_diskSendTask != null)
            {
                try { await _diskSendTask; } catch { }
            }

            lock (_lock)
            {
                _diskSocket = null;
            }

            Statistics.DiskWorkerConnectedSince = null;

            ArrayPool<byte>.Shared.Return(buffer);
            messageBuffer?.Dispose();

            try { ws.Dispose(); } catch { }
            try { stream.Close(); } catch { }
            try { client.Close(); } catch { }

            LogDebug("EVT", "Disk worker disconnected");
            DiskWorkerDisconnected?.Invoke();
            StatisticsUpdated?.Invoke();
        }
    }

    private async Task DiskSendLoopAsync(System.Net.WebSockets.WebSocket ws, CancellationToken ct)
    {
        var channel = _diskSendChannel;
        if (channel == null) return;

        LogDebug("EVT", "Disk send loop started");

        try
        {
            while (await channel.Reader.WaitToReadAsync(ct))
            {
                while (channel.Reader.TryRead(out var frame))
                {
                    if (ct.IsCancellationRequested) break;

                    try
                    {
                        if (ws.State != WebSocketState.Open)
                            continue;

                        await ws.SendAsync(
                            new ArraySegment<byte>(frame),
                            WebSocketMessageType.Binary,
                            true,
                            ct);
                    }
                    catch (WebSocketException ex)
                    {
                        LogDebug("TX", $"DISK SEND ERROR: {ex.Message}");
                    }
                    catch (ObjectDisposedException)
                    {
                        return;
                    }
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            LogDebug("EVT", $"Disk send loop error: {ex.Message}");
        }

        LogDebug("EVT", "Disk send loop stopped");
    }

    // ───────────────────────────────────────────────────────────────────
    // Disk Binary Frame Processing
    // 0x20 = block-read:  [0x20][driveType][unit][offset:4BE][size:2BE]
    // 0x22 = block-write: [0x22][driveType][unit][offset:4BE][size:2BE][data...]
    // ───────────────────────────────────────────────────────────────────

    private void HandleDiskBinaryFrame(byte[] buffer, int length)
    {
        if (length < 1)
            return;

        byte frameType = buffer[0];

        if (frameType == 0x20) // block-read
        {
            HandleDiskRead(buffer, length);
        }
        else if (frameType == 0x22) // block-write
        {
            HandleDiskWrite(buffer, length);
        }
        else
        {
            if (DebugMessage != null)
                LogDebug("RX", $"Unknown disk frame type 0x{frameType:X2} {length}B");
        }
    }

    private void HandleDiskRead(byte[] buffer, int length)
    {
        // [0x20][driveType:1][unit:1][offset:4BE][size:2BE] = 9 bytes minimum
        if (length < 9)
        {
            LogDebug("RX", $"Disk read frame too short: {length}B");
            return;
        }

        byte driveType = buffer[1];
        byte unit = buffer[2];
        uint offset = (uint)((buffer[3] << 24) | (buffer[4] << 16) | (buffer[5] << 8) | buffer[6]);
        ushort size = (ushort)((buffer[7] << 8) | buffer[8]);

        string driveTypeStr = driveType == 0 ? "SMD" : "Floppy";
        if (DebugMessage != null)
            LogDebug("RX", $"disk-read {driveTypeStr} unit={unit} offset={offset} size={size}");

        var fileStream = GetDiskFileStream(driveType, unit);
        if (fileStream == null)
        {
            // No image mounted — send error response
            LogDebug("TX", $"disk-read ERROR: no image for {driveTypeStr} unit={unit}");
            SendDiskReadError(driveType, unit);
            Statistics.RecordDiskError();
            StatisticsUpdated?.Invoke();
            return;
        }

        try
        {
            // Read data from file
            var data = new byte[size];
            fileStream.Seek(offset, SeekOrigin.Begin);
            int totalRead = 0;
            while (totalRead < size)
            {
                int bytesRead = fileStream.Read(data, totalRead, size - totalRead);
                if (bytesRead == 0) break; // EOF
                totalRead += bytesRead;
            }

            // Build response: [0x21][driveType][unit][data...]
            var response = new byte[3 + totalRead];
            response[0] = 0x21; // read-response
            response[1] = driveType;
            response[2] = unit;
            Buffer.BlockCopy(data, 0, response, 3, totalRead);

            EnqueueDiskSend(response);
            Statistics.RecordDiskRead(totalRead);

            if (DebugMessage != null)
                LogDebug("TX", $"disk-read-response {driveTypeStr} unit={unit} {totalRead}B");
        }
        catch (Exception ex)
        {
            LogDebug("TX", $"disk-read ERROR: {ex.Message}");
            SendDiskReadError(driveType, unit);
            Statistics.RecordDiskError();
        }

        StatisticsUpdated?.Invoke();
    }

    private void HandleDiskWrite(byte[] buffer, int length)
    {
        // [0x22][driveType:1][unit:1][offset:4BE][size:2BE][data...] = 9 + data bytes
        if (length < 9)
        {
            LogDebug("RX", $"Disk write frame too short: {length}B");
            return;
        }

        byte driveType = buffer[1];
        byte unit = buffer[2];
        uint offset = (uint)((buffer[3] << 24) | (buffer[4] << 16) | (buffer[5] << 8) | buffer[6]);
        ushort size = (ushort)((buffer[7] << 8) | buffer[8]);
        int dataLen = length - 9;

        string driveTypeStr = driveType == 0 ? "SMD" : "Floppy";
        if (DebugMessage != null)
            LogDebug("RX", $"disk-write {driveTypeStr} unit={unit} offset={offset} size={size} dataLen={dataLen}");

        var fileStream = GetDiskFileStream(driveType, unit);
        if (fileStream == null)
        {
            LogDebug("TX", $"disk-write ERROR: no image for {driveTypeStr} unit={unit}");
            SendDiskWriteAck(driveType, unit, 0xFF);
            Statistics.RecordDiskError();
            StatisticsUpdated?.Invoke();
            return;
        }

        try
        {
            int writeLen = Math.Min(dataLen, size);
            fileStream.Seek(offset, SeekOrigin.Begin);
            fileStream.Write(buffer, 9, writeLen);
            fileStream.Flush();

            SendDiskWriteAck(driveType, unit, 0x00); // success
            Statistics.RecordDiskWrite(writeLen);

            if (DebugMessage != null)
                LogDebug("TX", $"disk-write-ack {driveTypeStr} unit={unit} OK {writeLen}B");
        }
        catch (Exception ex)
        {
            LogDebug("TX", $"disk-write ERROR: {ex.Message}");
            SendDiskWriteAck(driveType, unit, 0xFF);
            Statistics.RecordDiskError();
        }

        StatisticsUpdated?.Invoke();
    }

    private FileStream? GetDiskFileStream(byte driveType, byte unit)
    {
        if (driveType == 0) // SMD
        {
            _smdFiles.TryGetValue(unit, out var fs);
            return fs;
        }
        else if (driveType == 1) // Floppy
        {
            _floppyFiles.TryGetValue(unit, out var fs);
            return fs;
        }
        return null;
    }

    private void SendDiskReadError(byte driveType, byte unit)
    {
        // Error response: [0x21][driveType][unit][0xFF]
        var response = new byte[] { 0x21, driveType, unit, 0xFF };
        EnqueueDiskSend(response);
    }

    private void SendDiskWriteAck(byte driveType, byte unit, byte status)
    {
        // Write ack: [0x23][driveType][unit][status]
        var response = new byte[] { 0x23, driveType, unit, status };
        EnqueueDiskSend(response);
    }

    private void EnqueueDiskSend(byte[] frame)
    {
        var channel = _diskSendChannel;
        if (channel == null) return;
        channel.Writer.TryWrite(frame);
    }

    // ───────────────────────────────────────────────────────────────────
    // Disk Image Management
    // ───────────────────────────────────────────────────────────────────

    /// <summary>
    /// Open disk images based on settings. Closes any previously open images first.
    /// If the disk worker is already connected, sends an updated disk-list.
    /// </summary>
    public void OpenDiskImages(GatewayDiskSettings settings)
    {
        CloseDiskImages();
        _diskSettings = settings;

        // Unit numbers are assigned by position: first SMD image = unit 0, second = unit 1, etc.
        int smdUnit = 0;
        int floppyUnit = 0;

        for (int i = 0; i < settings.Images.Count; i++)
        {
            var entry = settings.Images[i];
            if (string.IsNullOrEmpty(entry.Path) || !File.Exists(entry.Path))
                continue;

            string typeStr;
            Dictionary<int, FileStream> dict;
            int unit;

            if (entry.DriveType == 0)
            {
                typeStr = "SMD";
                dict = _smdFiles;
                unit = smdUnit++;
            }
            else
            {
                typeStr = "Floppy";
                dict = _floppyFiles;
                unit = floppyUnit++;
            }

            try
            {
                var fs = new FileStream(entry.Path, FileMode.Open, FileAccess.ReadWrite, FileShare.Read);
                dict[unit] = fs;
                LogDebug("EVT", $"Opened {typeStr} unit {unit}: {entry.Path}");
            }
            catch (Exception ex)
            {
                LogDebug("EVT", $"Failed to open {typeStr} unit {unit}: {ex.Message}");
            }
        }

        // Send updated disk-list to whoever is connected
        SendDiskListJson();
    }

    /// <summary>
    /// Close all open disk image file streams.
    /// </summary>
    public void CloseDiskImages()
    {
        var smdEnumerator = _smdFiles.GetEnumerator();
        while (smdEnumerator.MoveNext())
        {
            var kvp = (KeyValuePair<int, FileStream>)smdEnumerator.Current;
            try { kvp.Value.Close(); } catch { }
        }
        _smdFiles.Clear();

        var floppyEnumerator = _floppyFiles.GetEnumerator();
        while (floppyEnumerator.MoveNext())
        {
            var kvp = (KeyValuePair<int, FileStream>)floppyEnumerator.Current;
            try { kvp.Value.Close(); } catch { }
        }
        _floppyFiles.Clear();
    }

    /// <summary>
    /// Build and send the disk-list JSON.
    /// Always sends on the main emulator socket (so the emulator knows about changes).
    /// Also sends on the disk worker socket if connected.
    /// </summary>
    public void SendDiskListJson()
    {
        var json = BuildDiskListJson();
        var bytes = Encoding.UTF8.GetBytes(json);

        // Send on main emulator socket via the send channel
        if (IsEmulatorConnected)
        {
            var label = DebugMessage != null ? $"disk-list (emulator): {Truncate(json, 200)}" : string.Empty;
            EnqueueSend(bytes, label);
        }

        // Also send directly on disk worker socket if connected
        System.Net.WebSockets.WebSocket? diskWs;
        lock (_lock)
        {
            diskWs = _diskSocket;
        }

        if (diskWs != null && diskWs.State == WebSocketState.Open)
        {
            var diskBytes = Encoding.UTF8.GetBytes(json);
            _ = Task.Run(async () =>
            {
                try
                {
                    await diskWs.SendAsync(
                        new ArraySegment<byte>(diskBytes),
                        WebSocketMessageType.Text,
                        true,
                        CancellationToken.None);

                    LogDebug("TX", $"disk-list (disk worker): {Truncate(json, 200)}");
                }
                catch (Exception ex)
                {
                    LogDebug("TX", $"Failed to send disk-list to disk worker: {ex.Message}");
                }
            });
        }
    }

    private string BuildDiskListJson()
    {
        var sb = new StringBuilder();
        sb.Append("{\"type\":\"disk-list\",\"smd\":[");

        bool first = true;
        var smdEnumerator = _smdFiles.GetEnumerator();
        while (smdEnumerator.MoveNext())
        {
            var kvp = (KeyValuePair<int, FileStream>)smdEnumerator.Current;
            if (!first) sb.Append(',');
            first = false;

            string name = FindImageName(0, kvp.Key);
            long size = 0;
            try { size = kvp.Value.Length; } catch { }

            sb.Append("{\"unit\":").Append(kvp.Key);
            sb.Append(",\"name\":\"").Append(EscapeJsonString(name)).Append('"');
            sb.Append(",\"size\":").Append(size);
            sb.Append('}');
        }

        sb.Append("],\"floppy\":[");

        first = true;
        var floppyEnumerator = _floppyFiles.GetEnumerator();
        while (floppyEnumerator.MoveNext())
        {
            var kvp = (KeyValuePair<int, FileStream>)floppyEnumerator.Current;
            if (!first) sb.Append(',');
            first = false;

            string name = FindImageName(1, kvp.Key);
            long size = 0;
            try { size = kvp.Value.Length; } catch { }

            sb.Append("{\"unit\":").Append(kvp.Key);
            sb.Append(",\"name\":\"").Append(EscapeJsonString(name)).Append('"');
            sb.Append(",\"size\":").Append(size);
            sb.Append('}');
        }

        sb.Append("]}");
        return sb.ToString();
    }

    private string FindImageName(int driveType, int unit)
    {
        if (_diskSettings == null) return string.Empty;

        // Unit number = position among images of the same driveType
        int index = 0;
        for (int i = 0; i < _diskSettings.Images.Count; i++)
        {
            var img = _diskSettings.Images[i];
            if (img.DriveType != driveType) continue;
            if (string.IsNullOrEmpty(img.Path)) continue;
            if (index == unit) return img.Name;
            index++;
        }
        return string.Empty;
    }

    private static string EscapeJsonString(string s)
    {
        if (string.IsNullOrEmpty(s)) return s;

        var sb = new StringBuilder(s.Length);
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            if (c == '"') sb.Append("\\\"");
            else if (c == '\\') sb.Append("\\\\");
            else if (c == '\n') sb.Append("\\n");
            else if (c == '\r') sb.Append("\\r");
            else if (c == '\t') sb.Append("\\t");
            else sb.Append(c);
        }
        return sb.ToString();
    }

    // ───────────────────────────────────────────────────────────────────
    // Message Processing (emulator main worker)
    // ───────────────────────────────────────────────────────────────────

    /// <summary>
    /// Fast-path message type detection using Utf8JsonReader (no DOM allocation).
    /// Returns the "type" string value without building a full JsonDocument.
    /// </summary>
    private static string? DetectMessageType(ReadOnlySpan<byte> json)
    {
        var reader = new Utf8JsonReader(json);
        if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
            return null;

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.PropertyName && reader.ValueTextEquals("type"u8))
            {
                if (reader.Read() && reader.TokenType == JsonTokenType.String)
                    return reader.GetString();
                return null;
            }
            // Skip value if not "type"
            if (reader.TokenType == JsonTokenType.PropertyName)
            {
                reader.Skip();
            }
        }
        return null;
    }

    private void ProcessMessage(byte[] messageBytes, int length)
    {
        try
        {
            var span = new ReadOnlySpan<byte>(messageBytes, 0, length);
            var type = DetectMessageType(span);

            if (type == null)
            {
                if (DebugMessage != null)
                    LogDebug("RX", $"Message without 'type': {Truncate(Encoding.UTF8.GetString(messageBytes, 0, length), 200)}");
                return;
            }

            if (string.Equals(type, "register", StringComparison.Ordinal))
            {
                if (DebugMessage != null)
                    LogDebug("RX", $"register: {Truncate(Encoding.UTF8.GetString(messageBytes, 0, length), 500)}");
                HandleRegisterMessage(messageBytes, length);
            }
            else
            {
                if (DebugMessage != null)
                    LogDebug("RX", $"Unknown type '{type}': {Truncate(Encoding.UTF8.GetString(messageBytes, 0, length), 200)}");
            }
        }
        catch (Exception ex)
        {
            LogDebug("RX", $"Message processing error: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static string Truncate(string s, int maxLen)
    {
        if (s.Length <= maxLen) return s;
        return s.Substring(0, maxLen) + "...";
    }

    private void HandleRegisterMessage(byte[] messageBytes, int length)
    {
        using var doc = JsonDocument.Parse(new ReadOnlyMemory<byte>(messageBytes, 0, length));
        var root = doc.RootElement;

        if (!root.TryGetProperty("terminals", out var terminalsArray))
            return;

        lock (_lock)
        {
            _terminals.Clear();

            if (terminalsArray.ValueKind == JsonValueKind.Array)
            {
                var enumerator = terminalsArray.EnumerateArray();
                while (enumerator.MoveNext())
                {
                    var item = enumerator.Current;
                    var info = new GatewayTerminalInfo();

                    if (item.TryGetProperty("identCode", out var idProp))
                        info.IdentCode = idProp.GetInt32();

                    if (item.TryGetProperty("name", out var nameProp))
                        info.Name = nameProp.GetString() ?? string.Empty;

                    if (item.TryGetProperty("logicalDevice", out var ldProp))
                        info.LogicalDevice = ldProp.GetInt32();

                    _terminals.Add(info);
                }
            }
        }

        TerminalListChanged?.Invoke();
    }

    // ───────────────────────────────────────────────────────────────────
    // Binary Frame Processing (emulator main worker)
    // Format: [type:1][identCode:1][data:N]
    // Type 0x02 = term-output (emulator → RetroTerm)
    // ───────────────────────────────────────────────────────────────────

    private void HandleBinaryFrame(byte[] buffer, int length)
    {
        if (length < 2)
            return;

        byte type = buffer[0];
        int identCode = buffer[1];

        // Ethernet TX from the guest. Handled first and returned on, because byte 1 is a
        // SEGMENT number here, not an identCode - reading it as a terminal would look up a
        // connection that has nothing to do with this frame.
        if (type == 0x31)
        {
            _ethernet?.TryHandleEmulatorFrame(buffer, length);
            return;
        }

        if (type == 0x02) // term-output
        {
            int dataLen = length - 2;

            if (!_activeConnections.TryGetValue(identCode, out var connection))
            {
                if (DebugMessage != null)
                    LogDebug("RX", $"term-output id={identCode} — NO connection, {dataLen}B dropped");
                return;
            }

            if (DebugMessage != null)
                LogDebug("RX", $"term-output id={identCode} {dataLen}B");

            if (dataLen > 0)
            {
                // Deliver data directly from buffer — no JSON parsing, no allocation for small payloads
                var data = new byte[dataLen];
                Buffer.BlockCopy(buffer, 2, data, 0, dataLen);
                connection.OnDataFromEmulator(data);
            }

            Statistics.RecordTermOutput(dataLen);
        }
        else
        {
            if (DebugMessage != null)
                LogDebug("RX", $"Unknown binary type 0x{type:X2} id={identCode} {length}B");
        }
    }

    // ───────────────────────────────────────────────────────────────────
    // Send Queue + Sender Loop (thread-safe, single-writer to WebSocket)
    // ───────────────────────────────────────────────────────────────────

    private void EnqueueSend(byte[] data, string debugLabel, bool isBinary = false)
    {
        var channel = _sendChannel;
        if (channel == null)
        {
            LogDebug("TX", $"DROPPED {debugLabel} (no send channel)");
            return;
        }

        if (!channel.Writer.TryWrite((data, debugLabel, isBinary)))
        {
            LogDebug("TX", $"DROPPED {debugLabel} (channel write failed)");
        }
    }

    private async Task SendLoopAsync(System.Net.WebSockets.WebSocket ws, CancellationToken ct)
    {
        var channel = _sendChannel;
        if (channel == null) return;

        LogDebug("EVT", "Send loop started");

        try
        {
            while (await channel.Reader.WaitToReadAsync(ct))
            {
                while (channel.Reader.TryRead(out var item))
                {
                    if (ct.IsCancellationRequested) break;

                    try
                    {
                        if (ws.State != WebSocketState.Open)
                        {
                            if (DebugMessage != null)
                                LogDebug("TX", $"DROPPED {item.DebugLabel} (ws={ws.State})");
                            continue;
                        }

                        await ws.SendAsync(
                            new ArraySegment<byte>(item.Data),
                            item.IsBinary ? WebSocketMessageType.Binary : WebSocketMessageType.Text,
                            true,
                            ct);

                        if (DebugMessage != null && item.DebugLabel.Length > 0)
                            LogDebug("TX", $"SENT {item.DebugLabel}");
                    }
                    catch (WebSocketException ex)
                    {
                        LogDebug("TX", $"SEND ERROR {item.DebugLabel}: {ex.Message}");
                    }
                    catch (ObjectDisposedException ex)
                    {
                        LogDebug("TX", $"SEND ERROR {item.DebugLabel}: {ex.Message}");
                        return;
                    }
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            LogDebug("EVT", $"Send loop error: {ex.Message}");
        }

        LogDebug("EVT", "Send loop stopped");
    }

    /// <summary>
    /// Send terminal input data to the emulator for a specific identCode.
    /// Binary format: [0x01][identCode][data bytes...]
    /// </summary>
    internal Task SendTermInputAsync(int identCode, ReadOnlyMemory<byte> data)
    {
        var span = data.Span;

        // Binary frame: [type:1][identCode:1][data:N]
        var frame = new byte[2 + span.Length];
        frame[0] = 0x01; // term-input
        frame[1] = (byte)(identCode & 0xFF);
        span.CopyTo(frame.AsSpan(2));

        Statistics.RecordTermInput(data.Length);

        if (DebugMessage != null)
        {
            var label = $"term-input id={identCode} {data.Length}B hex={FormatHex(span, 32)}";
            LogDebug("TX", $"QUEUE {label}");
            EnqueueSend(frame, label, true);
        }
        else
        {
            EnqueueSend(frame, string.Empty, true);
        }

        return Task.CompletedTask;
    }

    private static string FormatHex(ReadOnlySpan<byte> data, int maxBytes)
    {
        var sb = new StringBuilder();
        int limit = Math.Min(data.Length, maxBytes);
        for (int i = 0; i < limit; i++)
        {
            if (i > 0) sb.Append(' ');
            sb.Append(data[i].ToString("X2"));
        }
        if (data.Length > maxBytes)
            sb.Append("...");
        return sb.ToString();
    }

    /// <summary>
    /// Send client-connected notification to the emulator.
    /// </summary>
    internal Task SendClientConnectedAsync(int identCode)
    {
        var json = $"{{\"type\":\"client-connected\",\"identCode\":{identCode}}}";
        var bytes = Encoding.UTF8.GetBytes(json);
        var label = DebugMessage != null ? $"client-connected id={identCode}" : string.Empty;
        if (label.Length > 0) LogDebug("TX", $"QUEUE {label}");
        EnqueueSend(bytes, label);
        Statistics.RecordClientConnect();
        return Task.CompletedTask;
    }

    /// <summary>
    /// Send client-disconnected notification to the emulator.
    /// </summary>
    internal Task SendClientDisconnectedAsync(int identCode)
    {
        var json = $"{{\"type\":\"client-disconnected\",\"identCode\":{identCode}}}";
        var bytes = Encoding.UTF8.GetBytes(json);
        var label = DebugMessage != null ? $"client-disconnected id={identCode}" : string.Empty;
        if (label.Length > 0) LogDebug("TX", $"QUEUE {label}");
        EnqueueSend(bytes, label);
        Statistics.RecordClientDisconnect();
        return Task.CompletedTask;
    }

    // ───────────────────────────────────────────────────────────────────
    // Connection Registration (called by GatewayConnection)
    // ───────────────────────────────────────────────────────────────────

    /// <summary>
    /// Register a GatewayConnection for a specific identCode.
    /// Returns false if identCode is already in use.
    /// </summary>
    internal bool TryRegisterConnection(int identCode, GatewayConnection connection)
    {
        bool added = _activeConnections.TryAdd(identCode, connection);
        LogDebug("EVT", added
            ? $"Connection registered id={identCode} (total={_activeConnections.Count})"
            : $"Connection registration REJECTED id={identCode} (already in use)");
        return added;
    }

    /// <summary>
    /// Unregister a GatewayConnection for a specific identCode.
    /// </summary>
    internal void UnregisterConnection(int identCode)
    {
        bool removed = _activeConnections.TryRemove(identCode, out _);
        LogDebug("EVT", removed
            ? $"Connection unregistered id={identCode} (remaining={_activeConnections.Count})"
            : $"Connection unregister NOOP id={identCode} (was not registered)");
    }

    // ───────────────────────────────────────────────────────────────────
    // Emulator / Disk Worker Disconnect
    // ───────────────────────────────────────────────────────────────────

    // Not async: every step here is synchronous, and an async method with no await runs
    // synchronously anyway while costing a state machine and a CS1998 warning.
    private Task OnEmulatorDisconnectedAsync()
    {
        // Snapshot all active connections and notify them
        var connections = new List<KeyValuePair<int, GatewayConnection>>();
        var enumerator = _activeConnections.GetEnumerator();
        while (enumerator.MoveNext())
        {
            connections.Add(enumerator.Current);
        }

        LogDebug("EVT", $"Emulator disconnected — notifying {connections.Count} active connection(s)");

        for (int i = 0; i < connections.Count; i++)
        {
            LogDebug("EVT", $"Notifying connection id={connections[i].Key}");
            connections[i].Value.OnEmulatorDisconnected();
        }

        return Task.CompletedTask;
    }

    private async Task DisconnectEmulatorAsync()
    {
        // Stop the send loop first
        _sendChannel?.Writer.TryComplete();
        if (_sendTask != null)
        {
            try { await _sendTask; } catch { }
            _sendTask = null;
        }

        System.Net.WebSockets.WebSocket? ws;
        lock (_lock)
        {
            ws = _emulatorSocket;
            _emulatorSocket = null;
        }

        if (ws != null)
        {
            try
            {
                if (ws.State == WebSocketState.Open)
                {
                    await ws.CloseAsync(
                        WebSocketCloseStatus.NormalClosure,
                        "Server shutting down",
                        CancellationToken.None);
                }
            }
            catch { }

            try { ws.Dispose(); } catch { }
        }

        _receiveCts?.Cancel();

        if (_receiveTask != null)
        {
            try { await _receiveTask; }
            catch { }
        }

        _receiveCts?.Dispose();
        _receiveCts = null;
        _receiveTask = null;

        await OnEmulatorDisconnectedAsync();

        lock (_lock)
        {
            _terminals.Clear();
        }

        Statistics.EmulatorConnectedSince = null;
    }

    private async Task DisconnectDiskWorkerAsync()
    {
        _diskSendChannel?.Writer.TryComplete();
        if (_diskSendTask != null)
        {
            try { await _diskSendTask; } catch { }
            _diskSendTask = null;
        }

        System.Net.WebSockets.WebSocket? diskWs;
        lock (_lock)
        {
            diskWs = _diskSocket;
            _diskSocket = null;
        }

        if (diskWs != null)
        {
            try
            {
                if (diskWs.State == WebSocketState.Open)
                {
                    await diskWs.CloseAsync(
                        WebSocketCloseStatus.NormalClosure,
                        "Server shutting down",
                        CancellationToken.None);
                }
            }
            catch { }

            try { diskWs.Dispose(); } catch { }
        }

        _diskReceiveCts?.Cancel();

        if (_diskReceiveTask != null)
        {
            try { await _diskReceiveTask; } catch { }
        }

        _diskReceiveCts?.Dispose();
        _diskReceiveCts = null;
        _diskReceiveTask = null;

        Statistics.DiskWorkerConnectedSince = null;
    }

    // ───────────────────────────────────────────────────────────────────
    // HTTP / WebSocket Handshake Helpers
    // ───────────────────────────────────────────────────────────────────

    private static string? ExtractWebSocketKey(string request)
    {
        // Parse line by line looking for "Sec-WebSocket-Key: <value>"
        int pos = 0;
        while (pos < request.Length)
        {
            int lineEnd = request.IndexOf('\n', pos);
            if (lineEnd < 0) lineEnd = request.Length;

            int lineLen = lineEnd - pos;
            if (lineLen > 0 && request[lineEnd - 1] == '\r')
                lineLen--;

            var line = request.Substring(pos, lineLen);

            if (line.StartsWith("Sec-WebSocket-Key:", StringComparison.OrdinalIgnoreCase))
            {
                return line.Substring("Sec-WebSocket-Key:".Length).Trim();
            }

            pos = lineEnd + 1;
        }

        return null;
    }

    private static string ComputeWebSocketAcceptKey(string key)
    {
        var combined = key + "258EAFA5-E914-47DA-95CA-C5AB0DC85B11";
        var hash = SHA1.HashData(Encoding.UTF8.GetBytes(combined));
        return Convert.ToBase64String(hash);
    }

    // ───────────────────────────────────────────────────────────────────
    // Dispose
    // ───────────────────────────────────────────────────────────────────

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _listenerCts?.Cancel();
        _sendChannel?.Writer.TryComplete();
        _diskSendChannel?.Writer.TryComplete();

        CloseDiskImages();

        try { _tcpListener?.Stop(); } catch { }
        try { _emulatorSocket?.Dispose(); } catch { }
        try { _diskSocket?.Dispose(); } catch { }
        try { _listenerCts?.Dispose(); } catch { }
        try { _receiveCts?.Cancel(); } catch { }
        try { _receiveCts?.Dispose(); } catch { }
        try { _diskReceiveCts?.Cancel(); } catch { }
        try { _diskReceiveCts?.Dispose(); } catch { }

        IsListening = false;
        // Not raised here: Dispose runs at shutdown, when there is no status bar left to tell.
    }
}
