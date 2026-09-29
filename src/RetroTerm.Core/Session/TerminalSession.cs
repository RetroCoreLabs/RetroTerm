using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using RetroTerm.Core.Logging;
using RetroTerm.Core.Opcom;
using RetroTerm.Core.Protocols;
using RetroTerm.Core.Terminal.Emulators;
using RetroTerm.Core.Terminal.Emulators.TDV;
using RetroTerm.Core.Transfer;

namespace RetroTerm.Core.Session;

/// <summary>
/// Represents a terminal session that coordinates between a connection and an emulator.
/// This is the business logic layer that sits between the UI and the protocol/emulation layers.
/// </summary>
public partial class TerminalSession : IDisposable
{
    private IConnection? _connection;
    private readonly SessionPump _pump;
    private ISessionDataLogger? _dataLogger;

    // Traffic counters for diagnosis ("time since last byte" tells busy from broken —
    // see docs\MCP-AND-SCRIPTING.md, "Rules the design enforces"). Interlocked because receive counting
    // happens on the network thread while status readers are on UI/MCP threads.
    private long _bytesReceived;
    private long _bytesSent;
    private long _lastReceiveStopwatchTicks; // Stopwatch.GetTimestamp() at last receive; 0 = never
    private IFileTransferHandler? _transferHandler;
    private volatile bool _isTransferActive;
    private IOpcomHandler? _opcomHandler;
    private bool _disposed;

    /// <summary>
    /// Gets the current connection, or null if not connected
    /// </summary>
    public IConnection? Connection => _connection;

    /// <summary>
    /// Gets the terminal emulator for this session.
    /// </summary>
    /// <remarks>
    /// Replaceable while connected - see <see cref="ChangeEmulatorAsync"/>. Anything holding on to
    /// this reference across that call is holding the OLD terminal, so subscribe to
    /// <see cref="EmulatorChanged"/> rather than caching it.
    /// </remarks>
    public TerminalEmulatorBase Emulator { get; private set; }

    /// <summary>
    /// Gets whether the session is currently connected
    /// </summary>
    public bool IsConnected => _connection?.IsConnected ?? false;

    /// <summary>
    /// Gets whether a file transfer is currently active
    /// </summary>
    public bool IsTransferActive => _isTransferActive;

    /// <summary>
    /// Gets the active file transfer handler, or null if no transfer is in progress.
    /// </summary>
    public IFileTransferHandler? ActiveTransferHandler => _transferHandler;

    /// <summary>
    /// Gets the active OPCOM handler, or null if OPCOM mode is not active.
    /// </summary>
    public IOpcomHandler? ActiveOpcomHandler => _opcomHandler;

    /// <summary>
    /// Gets whether OPCOM mode is currently active.
    /// </summary>
    public bool IsOpcomActive => _opcomHandler?.IsActive ?? false;

    /// <summary>
    /// Gets the current connection status
    /// </summary>
    public ConnectionStatus Status => _connection?.Status ?? ConnectionStatus.Disconnected;

    /// <summary>
    /// Gets or sets the session data logger for recording raw bytes on the wire
    /// </summary>
    public ISessionDataLogger? DataLogger
    {
        get => Volatile.Read(ref _dataLogger);
        set => Volatile.Write(ref _dataLogger, value);
    }

    /// <summary>
    /// Total bytes received from the host over this session's lifetime (across reconnects).
    /// </summary>
    public long BytesReceived => Interlocked.Read(ref _bytesReceived);

    /// <summary>
    /// Total bytes sent to the host over this session's lifetime (across reconnects).
    /// </summary>
    public long BytesSent => Interlocked.Read(ref _bytesSent);

    /// <summary>
    /// Time since the last byte arrived from the host, or null if nothing was ever
    /// received. The single most useful diagnostic for "is the machine busy or broken".
    /// </summary>
    public TimeSpan? TimeSinceLastReceive
    {
        get
        {
            long last = Interlocked.Read(ref _lastReceiveStopwatchTicks);
            if (last == 0)
            {
                return null;
            }
            long delta = System.Diagnostics.Stopwatch.GetTimestamp() - last;
            return TimeSpan.FromSeconds((double)delta / System.Diagnostics.Stopwatch.Frequency);
        }
    }

    /// <summary>
    /// Event raised when the connection status changes
    /// </summary>
    public event Action<ConnectionStatus>? StatusChanged;

    /// <summary>
    /// Raw bytes received from the host, raised on the SESSION PUMP thread after data
    /// logging but before any routing (OPCOM/transfer/emulator). This is the seam the
    /// MCP layer uses to forward host output to a client. The array is a copy owned by
    /// the subscriber. Only fires (and only allocates) when someone is subscribed.
    /// Handlers must return quickly — they run in line with emulation.
    /// </summary>
    public event Action<byte[]>? DataReceived;

    /// <summary>
    /// Event raised when a connection error occurs
    /// </summary>
    public event Action<Exception>? ErrorOccurred;

    /// <summary>
    /// Raised when the connection drops WITHOUT a user-initiated disconnect (remote
    /// close, network failure). User-initiated DisconnectAsync never fires this —
    /// it unsubscribes from the connection first, same as StatusChanged.
    /// This is the seam the script runner and MCP layer subscribe to: stop the
    /// running script, tell the client, update status — no modal dialogs anywhere.
    /// </summary>
    public event Action<string>? ConnectionLost;

    /// <summary>
    /// Event raised when the terminal display needs to be updated
    /// </summary>
    public event Action? DisplayInvalidated;

    /// <summary>
    /// Event raised when a file transfer starts or stops (true=active, false=inactive)
    /// </summary>
    public event Action<bool>? TransferStateChanged;

    /// <summary>
    /// Event raised when file transfer progress updates
    /// </summary>
    public event Action<TransferProgress>? TransferProgressChanged;

    /// <summary>
    /// Gets or sets whether Kermit auto-detect receive is enabled.
    /// When enabled, incoming data is scanned for Kermit Send-Init packets.
    /// </summary>
    public bool KermitAutoDetectEnabled { get; set; }

    /// <summary>
    /// Event raised when a Kermit Send-Init packet is detected in the incoming data stream.
    /// The UI should prompt the user for a save directory and start a receive operation.
    /// </summary>
    public event Action? KermitSendInitDetected;

    /// <summary>
    /// Gets or sets the session title (for display in UI)
    /// </summary>
    public string Title { get; set; }

    /// <summary>
    /// Creates a new terminal session with the specified emulator
    /// </summary>
    public TerminalSession(TerminalEmulatorBase emulator, string title = "Terminal")
    {
        Emulator = emulator ?? throw new ArgumentNullException(nameof(emulator));
        Title = title;

        // THE THREADING RULE: the network thread produces, this pump's task is the only
        // code that mutates the emulator/buffer, UI and MCP consume events/snapshots.
        // See SessionPump for the full contract.
        _pump = new SessionPump(ProcessReceivedData, ex => ErrorOccurred?.Invoke(ex));

        // Wire up emulator events
        Emulator.Invalidated += OnEmulatorInvalidated;

        // Wire up TDV-specific query/response events
        // NOTE: This is called before connection, but the event handler checks _connection when it fires
        WireTDVQueryResponse();

        // Wire the generic (non-TDV) terminal-to-host channel. Every emulator has this;
        // TDV additionally has its own string channel above. Without this subscription a
        // VT100 session generated DA/DSR/CPR replies and silently dropped them.
        WireEmulatorResponse();

        // Route "I need a fresh frame" requests from the UI onto the pump. Capturing a frame reads
        // the live buffer, so it may only happen on the single writer thread — that is the whole
        // point of the frame handoff. Without this the UI would have to touch the buffer itself.
        WireFrameRequests();
    }

    /// <summary>
    /// Makes the emulator's frame requests run on the session pump. Safe to call again after the
    /// emulator changes — the handler is removed before it is re-added.
    /// </summary>
    public void WireFrameRequests()
    {
        Emulator.FrameRequested -= OnFrameRequested;
        Emulator.FrameRequested += OnFrameRequested;
    }

    /// <summary>
    /// Captures a frame on the pump thread. Fire-and-forget: the UI asked for a repaint, and the
    /// frame it gets is whichever one the pump produces next.
    /// </summary>
    private void OnFrameRequested()
    {
        _ = _pump.RunAsync(() => Emulator.PublishFrame());
    }

    /// <summary>
    /// Wires the emulator's generic byte-oriented reply channel
    /// (<see cref="TerminalEmulatorBase.DataToSend"/>) to the connection. Safe to call
    /// again after the emulator changes — the handler is removed before it is re-added.
    /// </summary>
    public void WireEmulatorResponse()
    {
        Emulator.DataToSend -= OnEmulatorDataToSend;
        Emulator.DataToSend += OnEmulatorDataToSend;
    }

    /// <summary>
    /// Sends an emulator-generated reply (DA, DSR, CPR, ...) back to the host.
    /// This is now the ONLY path from an emulator to the host. It takes bytes, so nothing is lost
    /// to text encoding — a reply may legitimately contain bytes above 0x7F, and the TDV path that
    /// used to encode replies as UTF-8 would have turned any such byte into two on the wire.
    /// </summary>
    private async void OnEmulatorDataToSend(byte[] data)
    {
        if (data == null || data.Length == 0)
        {
            return;
        }

        if (_connection?.IsConnected != true)
        {
            ApplicationLogger.Log(LogCategory.Session, LogLevel.Warn, "TerminalSession",
                $"Emulator reply ready but connection is not connected (status: {_connection?.Status})");
            return;
        }

        try
        {
            Volatile.Read(ref _dataLogger)?.LogOutgoing(data);

            // THE single TX feed point for the protocol trace (emulator reply path).
            ProtocolTracer.TraceOutgoing(data);

            ApplicationLogger.Log(LogCategory.Session, LogLevel.Debug, "TerminalSession",
                $"Emulator reply, {data.Length} bytes", data);

            Interlocked.Add(ref _bytesSent, data.Length);

            await _connection.SendAsync(data).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            ApplicationLogger.Log(LogCategory.Session, LogLevel.Error, "TerminalSession",
                $"Failed to send emulator reply: {ex}");
        }
    }

    /// <summary>
    /// Wires up TDV query/response handling.
    ///
    /// There is no longer a separate TDV send path: TDV replies travel down the same byte channel
    /// as every other emulator's, so this delegates to <see cref="WireEmulatorResponse"/>. It is
    /// kept because callers ask for it by name, and because "have TDV replies been wired up?" is
    /// still a reasonable question to ask a session.
    ///
    /// The old arrangement had the session subscribe to TDVEmulatorBase.OnResponseReady and encode
    /// the string as UTF-8. Two send paths meant two near-identical handlers to keep in step, and
    /// UTF-8 would have mangled any reply byte above 0x7F. OnResponseReady still fires — it is an
    /// observation point now, not a wire.
    /// </summary>
    public void WireTDVQueryResponse()
    {
        WireEmulatorResponse();

        ApplicationLogger.Log(LogCategory.Session, LogLevel.Debug, "TerminalSession",
            $"Query/response wired for {Emulator.GetType().Name} (unified byte channel)");
    }

    /// <summary>
    /// Connects the session using the specified connection
    /// </summary>
    public async Task ConnectAsync(IConnection connection, CancellationToken cancellationToken = default)
    {
        if (_connection != null)
        {
            throw new InvalidOperationException("Session is already connected. Disconnect first.");
        }

        if (connection == null)
        {
            throw new ArgumentNullException(nameof(connection));
        }

        // Store connection and wire up events
        _connection = connection;
        _connection.StatusChanged += OnConnectionStatusChanged;
        _connection.DataReceived += OnConnectionDataReceived;
        _connection.ErrorOccurred += OnConnectionError;

        try
        {
            // Tell the connection how big the screen is BEFORE it opens, so the size the host is
            // given at login is the emulator's. Until 27 September 2026 nothing did this: a Telnet
            // connection answered DO NAWS with its own 80 by 24 default and an SSH pty was opened
            // at 80 by 24, whatever the emulator was, until a window resize or an EMULATION change
            // happened to send the real value. Before ConnectAsync nothing goes on the wire - the
            // Telnet and SSH connections only record the value - and a transport with no notion
            // of size (serial, gateway) ignores it.
            await _connection.ResizeTerminalAsync(Emulator.Width, Emulator.Height, cancellationToken);

            // Attempt to connect
            await _connection.ConnectAsync(cancellationToken);

            // Update title with connection info
            Title = $"Terminal - {_connection.Description}";

            // CRITICAL: Re-wire query/response after connection is established
            // This ensures the event handlers have access to _connection
            WireTDVQueryResponse();
            WireEmulatorResponse();
            WireFrameRequests();
        }
        catch
        {
            // Clean up on connection failure
            CleanupConnection();
            throw;
        }
    }

    /// <summary>
    /// Raised after the session's emulator has been replaced, carrying the new one.
    /// </summary>
    /// <remarks>
    /// The UI rewires its canvas from here rather than the caller doing it, so every way of changing
    /// the terminal - the menu, a script, an MCP call - ends up wired the same. That is the
    /// two-surface rule this repository already has: put the behaviour where it cannot be skipped.
    /// </remarks>
    public event Action<TerminalEmulatorBase>? EmulatorChanged;

    /// <summary>
    /// Swaps in a different terminal emulator without disturbing the connection.
    /// </summary>
    /// <remarks>
    /// <para><b>The connection is untouched on purpose</b></para>
    /// Picking the wrong terminal type is usually discovered AFTER logging in, and dropping the line
    /// to correct it means logging in again. Nothing about the socket depends on which terminal is
    /// decoding the bytes, so nothing about the socket needs to change.
    /// <para><b>What carries over, and what cannot</b></para>
    /// The screen, the cursor and the scrollback come across, so the reader keeps looking at what
    /// they were looking at, now interpreted by the new rules. Graphics do NOT: the new terminal may
    /// have no bitmap at all, and there is no honest way to reinterpret a ReGIS plane as a Tektronix
    /// storage tube. A caller with a status bar should say so rather than leaving a picture to
    /// vanish unexplained.
    /// <para><b>It all happens on the pump</b></para>
    /// Copying a buffer and swapping the object the receive loop reads are both writes, and the pump
    /// is the single writer. Doing it from the UI thread while bytes were arriving would tear the
    /// buffer in a way no test would reproduce twice.
    /// </remarks>
    /// <param name="replacement">
    /// The new emulator, already built at the size it should be.
    /// </param>
    /// <returns>
    /// True when the screen was carried over; false when there was nothing to carry.
    /// </returns>
    public async Task<bool> ChangeEmulatorAsync(TerminalEmulatorBase replacement)
    {
        if (replacement == null) throw new ArgumentNullException(nameof(replacement));
        if (ReferenceEquals(replacement, Emulator)) return false;

        bool carried = false;

        await _pump.RunAsync(() =>
        {
            var outgoing = Emulator;

            // Unwire FIRST. A handler left on the old emulator would keep this session alive in its
            // event list and would keep answering for a terminal nobody is looking at any more.
            outgoing.Invalidated -= OnEmulatorInvalidated;
            outgoing.DataToSend -= OnEmulatorDataToSend;
            outgoing.FrameRequested -= OnFrameRequested;

            replacement.GetBuffer().CopyFrom(outgoing.GetBuffer());

            var oldCursor = outgoing.GetCursor();
            var newCursor = replacement.GetCursor();
            newCursor.Row = Math.Min(oldCursor.Row, replacement.Height - 1);
            newCursor.Column = Math.Min(oldCursor.Column, replacement.Width - 1);
            carried = true;

            Emulator = replacement;

            Emulator.Invalidated += OnEmulatorInvalidated;
            WireEmulatorResponse();
            WireFrameRequests();

            // Publish at once, or the screen stays as the old terminal drew it until the host
            // happens to send something - which on an idle login prompt can be a long time.
            Emulator.PublishFrame();
        }).ConfigureAwait(false);

        ApplicationLogger.Log(LogCategory.Session, LogLevel.Info, "TerminalSession",
            $"Terminal changed to {replacement.GetType().Name} ({replacement.Width}x{replacement.Height})");

        EmulatorChanged?.Invoke(replacement);
        DisplayInvalidated?.Invoke();

        // The host is told the new geometry, because a fixed-geometry terminal may be a different
        // size from the one it was talking to a moment ago.
        var connection = _connection;
        if (connection != null)
        {
            try
            {
                await connection.ResizeTerminalAsync(replacement.Width, replacement.Height)
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke(ex);
            }
        }

        return carried;
    }

    /// <summary>
    /// Changes the terminal's size and tells the host about it.
    /// </summary>
    /// <remarks>
    /// <para><b>Why this lives on the session</b></para>
    /// A resize is two things that must happen together: the emulator re-lays out its buffer, and
    /// whatever is on the other end is told the new size so a full-screen program repaints at it.
    /// The session is the only object that holds both, so doing it anywhere else means doing half
    /// of it.
    /// The emulator half runs ON THE PUMP. The pump is the single writer to the buffer, and a
    /// resize is the largest write there is - it rebuilds the grid and reflows every wrapped
    /// paragraph. Doing that from the UI thread while bytes were arriving would corrupt the buffer
    /// in a way no test would reproduce twice.
    /// A failure to tell the host is reported and swallowed. The terminal has already resized, and
    /// throwing here would leave the two halves disagreeing.
    /// </remarks>
    /// <param name="columns">
    /// New width in character cells.
    /// </param>
    /// <param name="rows">
    /// New height in character cells.
    /// </param>
    /// <returns>
    /// A task that completes once the emulator has resized and the host has been told.
    /// </returns>
    public async Task ResizeAsync(int columns, int rows)
    {
        if (columns <= 0 || rows <= 0) return;
        if (columns == Emulator.Width && rows == Emulator.Height) return;

        await _pump.RunAsync(() => Emulator.Resize(columns, rows)).ConfigureAwait(false);

        var connection = _connection;
        if (connection == null) return;

        try
        {
            await connection.ResizeTerminalAsync(columns, rows).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(ex);
        }
    }

    /// <summary>
    /// Disconnects the current session.
    /// This is a user-initiated disconnect — events are unsubscribed before
    /// disconnecting so StatusChanged does NOT fire. The caller handles its
    /// own UI updates. Remote disconnects still fire StatusChanged through
    /// OnConnectionStatusChanged.
    /// </summary>
    public async Task DisconnectAsync()
    {
        if (_connection == null)
        {
            return;
        }

        // Unsubscribe BEFORE disconnecting so the connection's StatusChanged
        // event doesn't propagate through to the UI. The caller (ConnectToHost,
        // OnDisconnectClick, etc.) already handles UI updates explicitly.
        var conn = _connection;
        conn.StatusChanged -= OnConnectionStatusChanged;
        conn.DataReceived -= OnConnectionDataReceived;
        conn.ErrorOccurred -= OnConnectionError;
        _connection = null;
        Title = "Terminal - Disconnected";

        try
        {
            await conn.DisconnectAsync();
        }
        finally
        {
            conn.Dispose();
        }
    }

    /// <summary>
    /// Sends user input to the remote host
    /// </summary>
    public Task SendInputAsync(string input, CancellationToken cancellationToken = default)
    {
        // The TERMINAL decides how text becomes bytes, not this method. A TDV line is 8-bit and a
        // modern host behind an ANSI session speaks UTF-8; sending UTF-8 to both — which is what
        // this used to do — puts two bytes on an ND line for every character above 0x7F.
        // Still one send pipeline: encode, then go through the byte path.
        return SendBytesAsync(Emulator.Profile.EncodeForTransport(input), cancellationToken);
    }

    /// <summary>
    /// Sends raw bytes to the remote host. This is the first-class way to send control
    /// bytes: ESC (0x1B) wakes a SINTRAN line and recovers a wedged one, so scripts and
    /// MCP need bare-byte sending, not just text (rule 2 of the MCP terminal-control rules
    /// in docs\MCP-AND-SCRIPTING.md).
    /// </summary>
    public async Task SendBytesAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
    {
        if (_connection == null || !_connection.IsConnected)
        {
            throw new InvalidOperationException("Cannot send input when not connected");
        }

        Volatile.Read(ref _dataLogger)?.LogOutgoing(data.Span);

        // THE single TX feed point for the protocol trace (user input path).
        ProtocolTracer.TraceOutgoing(data.Span);

        Interlocked.Add(ref _bytesSent, data.Length);

        await _connection.SendAsync(data, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Writes text directly to the terminal display (for local messages).
    /// Callers are typically on the UI thread; the actual emulator mutation happens on
    /// the session pump, serialized with network data — the UI must never touch the
    /// emulator directly. Fire-and-forget: the display updates via Invalidated as usual.
    /// </summary>
    public void WriteToTerminal(string text)
    {
        // Verbatim string literals (@"...") embed whatever line endings the source file has on
        // disk — LF on Linux/macOS, CRLF on Windows. The emulator needs CRLF to render correctly,
        // so normalize bare LFs here on the local-write path. The network path is untouched.
        if (text.IndexOf('\n') >= 0)
        {
            text = text.Replace("\r\n", "\n").Replace("\n", "\r\n");
        }
        // Same encoding question as the send path: ProcessData takes WIRE bytes, so a local message
        // must be encoded the way this terminal's wire encodes text, not always as UTF-8.
        var bytes = Emulator.Profile.EncodeForTransport(text);
        _ = _pump.RunAsync(() => Emulator.ProcessData(bytes));
    }

    /// <summary>
    /// Completes when every data chunk received before this call has been processed by
    /// the emulator. Deterministic "the screen is now up to date" barrier for tests,
    /// screen reads and the script/MCP wait primitives — instead of Task.Delay guessing.
    /// </summary>
    public Task FlushAsync() => _pump.FlushAsync();

    /// <summary>
    /// Runs a job on the session pump thread, serialized with data processing.
    /// This is THE safe way to read or mutate the emulator/buffer from any other
    /// thread (UI snapshots, MCP screen reads, script wait checks).
    /// </summary>
    public Task RunOnSessionThreadAsync(Action job) => _pump.RunAsync(job);

    /// <summary>
    /// Value-returning variant of <see cref="RunOnSessionThreadAsync(Action)"/>.
    /// </summary>
    /// <remarks>
    /// <para><b>Overload trap, found 4 August 2026</b></para>
    /// An expression lambda whose body returns a value binds to THIS generic overload, not to the
    /// Action one, even when the caller only wanted the side effect. The case that found it was a
    /// one-line lambda calling <c>tcs.TrySetResult(x)</c> passed to <c>RunAsync</c>: TrySetResult
    /// returns bool, so the call bound to the generic overload again and the stack overflowed.
    /// When the job is meant to run for its effect, write it as a statement lambda with braces so
    /// it is an Action.
    /// </remarks>
    public Task<T> RunOnSessionThreadAsync<T>(Func<T> job) => _pump.RunAsync(job);

    /// <summary>
    /// Takes a consistent snapshot of the rendered screen (text + cursor), executed on
    /// the session pump thread so it can never race the emulator. All bytes received
    /// before this call are processed before the snapshot is taken.
    /// </summary>
    public Task<ScreenSnapshot> ReadScreenAsync(bool stripTrailingBlanks = true)
    {
        return _pump.RunAsync(() =>
        {
            var buffer = Emulator.GetBuffer();
            var cursor = Emulator.GetCursor();
            return new ScreenSnapshot(
                Terminal.Buffer.ScreenReader.GetScreenText(buffer, stripTrailingBlanks),
                cursor.Row,
                cursor.Column,
                buffer.Width,
                buffer.Height);
        });
    }

    private void OnConnectionStatusChanged(ConnectionStatus status)
    {
        // Raise event for UI
        StatusChanged?.Invoke(status);

        // Handle automatic cleanup on disconnection
        if (status == ConnectionStatus.Disconnected)
        {
            CleanupConnection();

            // Only remote/unexpected drops reach here (user-initiated disconnects
            // unsubscribed before disconnecting) — notify scripts and MCP clients.
            ConnectionLost?.Invoke("Connection closed by remote host");
        }
    }

    /// <summary>
    /// Connection DataReceived handler — runs on the network receive thread.
    /// Does NOTHING except hand the bytes to the session pump (copied into a pooled
    /// buffer, because the connection reuses its read buffer as soon as we return).
    /// All routing and emulation happens on the pump thread in ProcessReceivedData.
    /// </summary>
    private void OnConnectionDataReceived(ReadOnlyMemory<byte> data)
    {
        // Count arrival on the network thread — this timestamps when bytes actually
        // came in, independent of how far behind the emulator might be.
        Interlocked.Add(ref _bytesReceived, data.Length);
        Interlocked.Exchange(ref _lastReceiveStopwatchTicks, System.Diagnostics.Stopwatch.GetTimestamp());

        _pump.PostData(data.Span);
    }

    /// <summary>
    /// Processes one received chunk — runs on the session pump thread, the single
    /// writer for the emulator/buffer. This is the old OnConnectionDataReceived body,
    /// unchanged in logic, just moved off the network thread.
    /// </summary>
    private void ProcessReceivedData(ReadOnlySpan<byte> data)
    {
        // Log raw incoming data before processing
        Volatile.Read(ref _dataLogger)?.LogIncoming(data);

        // Forward the raw stream to observers (MCP client). Copy only when someone
        // listens — the span points into a pooled buffer that is recycled after this
        // method returns, so subscribers get their own array.
        var observers = DataReceived;
        if (observers != null)
        {
            observers(data.ToArray());
        }

        // Route to OPCOM handler if active (highest priority)
        var opcom = _opcomHandler;
        if (opcom != null && opcom.IsActive)
        {
            opcom.ProcessIncomingData(data);
            // If pass-through enabled, also forward to emulator
            if (opcom.PassThrough)
            {
                Emulator.ProcessData(data);
            }
            return;
        }

        // Route to file transfer handler if transfer is active
        if (_isTransferActive && _transferHandler != null)
        {
            _transferHandler.ProcessIncomingData(data);
            return;
        }

        // Kermit auto-detect: scan for Send-Init packet when not transferring
        if (KermitAutoDetectEnabled)
        {
            ScanForKermitSendInit(data);
        }

        // THE single RX feed point for the protocol trace. Bytes arrive here already
        // de-escaped by the transport, and have not yet been consumed by the emulator, so this
        // is the one place that sees the true host-to-terminal stream exactly once.
        ProtocolTracer.TraceIncoming(data);

        // Business logic: route incoming data to the emulator
        Emulator.ProcessData(data);
    }

    /// <summary>
    /// Scans for a Kermit Send-Init (S) packet in the data stream.
    /// Zero-allocation scan using Span.
    /// Packet format: SOH LEN SEQ TYPE DATA CHECK CR
    /// Type-1 checksum: sum of bytes from LEN to end-of-DATA, mod 64, + 32.
    /// </summary>
    internal void ScanForKermitSendInit(ReadOnlySpan<byte> data)
    {
        const byte SOH = 0x01;

        for (int i = 0; i < data.Length; i++)
        {
            if (data[i] != SOH) continue;

            // Need at least SOH + LEN + SEQ + TYPE = 4 bytes
            if (i + 3 >= data.Length) break;

            int lenField = data[i + 1] - 32; // unchar
            if (lenField < 3) continue; // minimum: SEQ + TYPE + CHECK

            int totalPacketLen = 1 + 1 + lenField; // SOH + LEN + (lenField bytes: SEQ+TYPE+DATA+CHECK)
            if (i + totalPacketLen > data.Length) continue;

            byte typeChar = data[i + 3]; // TYPE field
            if (typeChar != (byte)'S') continue; // Not a Send-Init

            // Verify type-1 checksum: sum bytes from LEN through last DATA byte (not CHECK)
            // LEN field tells how many bytes follow it (SEQ+TYPE+DATA+CHECK).
            // Checksum covers: LEN byte (1) + SEQ + TYPE + DATA = 1 + (lenField - 1) = lenField bytes
            int checksumBytes = lenField; // LEN + (lenField-1) bytes after LEN, excluding CHECK
            int sum = 0;
            for (int j = 0; j < checksumBytes; j++)
            {
                sum += data[i + 1 + j]; // start at LEN byte
            }
            int expected = (sum + ((sum & 192) >> 6)) & 63;
            int actual = data[i + 1 + checksumBytes] - 32; // CHECK byte, unchar'd

            if (expected == actual)
            {
                KermitSendInitDetected?.Invoke();
                return; // Only fire once per data chunk
            }
        }
    }

    private void OnConnectionError(Exception ex)
    {
        // Raise event for UI
        ErrorOccurred?.Invoke(ex);
    }

    private void OnEmulatorInvalidated()
    {
        // Raise event for UI to refresh display
        DisplayInvalidated?.Invoke();
    }

    private void CleanupConnection()
    {
        if (_connection == null)
        {
            return;
        }

        // Unsubscribe from events
        _connection.StatusChanged -= OnConnectionStatusChanged;
        _connection.DataReceived -= OnConnectionDataReceived;
        _connection.ErrorOccurred -= OnConnectionError;

        // Dispose connection
        _connection.Dispose();
        _connection = null;

        // Update title
        Title = "Terminal - Disconnected";
    }

    /// <summary>
    /// Starts a file transfer using the specified handler
    /// </summary>
    public async Task StartFileTransferAsync(
        IFileTransferHandler handler,
        TransferDirection direction,
        string[] paths,
        CancellationToken ct)
    {
        if (_connection == null || !_connection.IsConnected)
            throw new InvalidOperationException("Cannot start file transfer when not connected");

        if (_isTransferActive)
            throw new InvalidOperationException("A file transfer is already in progress");

        _transferHandler = handler;

        // Subscribe to handler events
        _transferHandler.ProgressChanged += OnTransferProgressChanged;
        _transferHandler.TransferCompleted += OnTransferCompleted;

        _isTransferActive = true;
        TransferStateChanged?.Invoke(true);

        // Create the send delegate that routes through the connection
        var connection = _connection;
        // Fully qualified: the Transfer.SendBytesAsync DELEGATE TYPE would otherwise
        // collide with this class's SendBytesAsync METHOD.
        Transfer.SendBytesAsync sendDelegate = async (data, token) =>
        {
            if (connection.IsConnected)
            {
                Volatile.Read(ref _dataLogger)?.LogOutgoing(data.Span);
                Interlocked.Add(ref _bytesSent, data.Length);
                await connection.SendAsync(data.ToArray(), token).ConfigureAwait(false);
            }
        };

        try
        {
            if (direction == TransferDirection.Send)
                await handler.StartSendAsync(paths, sendDelegate, ct).ConfigureAwait(false);
            else
                await handler.StartReceiveAsync(paths[0], sendDelegate, ct).ConfigureAwait(false);
        }
        catch (Exception)
        {
            CleanupTransfer();
            throw;
        }
    }

    /// <summary>
    /// Cancels the active file transfer
    /// </summary>
    public void CancelFileTransfer()
    {
        _transferHandler?.Cancel();
    }

    private void OnTransferProgressChanged(TransferProgress progress)
    {
        TransferProgressChanged?.Invoke(progress);
    }

    private void OnTransferCompleted(TransferProgress progress)
    {
        CleanupTransfer();
        TransferProgressChanged?.Invoke(progress);
    }

    private void CleanupTransfer()
    {
        if (_transferHandler != null)
        {
            _transferHandler.ProgressChanged -= OnTransferProgressChanged;
            _transferHandler.TransferCompleted -= OnTransferCompleted;
            _transferHandler = null;
        }

        _isTransferActive = false;
        TransferStateChanged?.Invoke(false);
    }

    // ==================== OPCOM Handler ====================

    /// <summary>
    /// Attaches an OPCOM handler to this session. While attached, the handler
    /// intercepts all incoming serial data (with optional pass-through to emulator).
    /// </summary>
    public void AttachOpcomHandler(IOpcomHandler handler)
    {
        if (handler == null) throw new ArgumentNullException(nameof(handler));
        if (_isTransferActive)
            throw new InvalidOperationException("Cannot attach OPCOM handler during file transfer");

        _opcomHandler = handler;

        // Create a send delegate that routes through the connection
        var connection = _connection;
        // Fully qualified: the Transfer.SendBytesAsync DELEGATE TYPE would otherwise
        // collide with this class's SendBytesAsync METHOD.
        Transfer.SendBytesAsync sendDelegate = async (data, token) =>
        {
            if (connection?.IsConnected == true)
            {
                Volatile.Read(ref _dataLogger)?.LogOutgoing(data.Span);
                Interlocked.Add(ref _bytesSent, data.Length);
                await connection.SendAsync(data.ToArray(), token).ConfigureAwait(false);
            }
        };

        handler.Activate(sendDelegate);
    }

    /// <summary>
    /// Detaches the OPCOM handler from this session.
    /// </summary>
    public void DetachOpcomHandler()
    {
        var handler = _opcomHandler;
        _opcomHandler = null;
        handler?.Deactivate();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        // Cancel any active transfer
        if (_isTransferActive)
        {
            try { CancelFileTransfer(); }
            catch { /* ignore */ }
            CleanupTransfer();
        }

        // Unsubscribe from emulator events FIRST to prevent async void handler crashes
        if (Emulator != null)
        {
            Emulator.Invalidated -= OnEmulatorInvalidated;
            Emulator.DataToSend -= OnEmulatorDataToSend;
            Emulator.FrameRequested -= OnFrameRequested;

            // Nothing TDV-specific to unsubscribe any more: TDV replies come through DataToSend
            // like everything else, and that handler is removed just above.
        }

        // Disconnect if still connected
        if (_connection != null)
        {
            try
            {
                DisconnectAsync().GetAwaiter().GetResult();
            }
            catch
            {
                // Ignore disconnect errors during disposal
            }
        }

        // Stop the pump AFTER disconnecting so no more data can arrive; it drains
        // what is queued (returning pooled buffers) and exits on its own.
        _pump.Dispose();

        _disposed = true;
    }
}

