using System;
using System.IO;
using System.IO.Ports;
using System.Threading;
using System.Threading.Tasks;
using RetroTerm.Core.Logging;
using RetroTerm.Core.Protocols;

namespace RetroTerm.Core.Protocols.Net;

/// <summary>
/// Serial port connection implementation.
/// Raw byte stream — no protocol framing (unlike Telnet IAC).
/// </summary>
public class SerialConnection : IConnection, ISerialConfigurable
{
    private SerialPort? _serialPort;
    private CancellationTokenSource? _receiveCts;
    private Task? _receiveTask;

    private readonly string _portName;
    private readonly int _baudRate;
    private readonly StopBits _stopBits;
    private readonly Parity _parity;
    private readonly Handshake _handshake;

    // NOT readonly, unlike the rest: ReconfigureSerial changes the framing on a live port
    // (OPCOM switches 7E1 for the terminal and 8N1 for a binary transfer), and the receive
    // path's 7-bit mask has to follow the framing actually in force. Reading the constructor
    // value there would keep masking a stream that had just been switched to 8 bits, which
    // silently corrupts every byte of a binary download.
    private int _dataBits;

    public ConnectionStatus Status { get; private set; }
    public string ConnectionType => "Serial";
    public string Description { get; }

    public event Action<ConnectionStatus>? StatusChanged;
    public event Action<ReadOnlyMemory<byte>>? DataReceived;
    public event Action<Exception>? ErrorOccurred;

    public SerialConnection(string portName, int baudRate = 9600,
        int dataBits = 8, int stopBitsValue = 1,
        int parityValue = 0, int handshakeValue = 0)
    {
        if (string.IsNullOrWhiteSpace(portName))
            throw new ArgumentException("Port name cannot be empty", nameof(portName));

        _portName = portName;
        _baudRate = baudRate;
        _dataBits = dataBits;
        _stopBits = MapStopBits(stopBitsValue);
        _parity = MapParity(parityValue);
        _handshake = MapHandshake(handshakeValue);
        Description = $"{portName} ({baudRate}bps)";
        Status = ConnectionStatus.Disconnected;

        ApplicationLogger.Log($"[SerialConnection] Created: port={portName}, baud={baudRate}, dataBits={dataBits}, stopBitsValue={stopBitsValue}→{_stopBits}, parityValue={parityValue}→{_parity}, handshakeValue={handshakeValue}→{_handshake}");
    }

    public Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        if (Status != ConnectionStatus.Disconnected)
            throw new InvalidOperationException($"Cannot connect when status is {Status}");

        try
        {
            SetStatus(ConnectionStatus.Connecting);

            _serialPort = new SerialPort(_portName, _baudRate, _parity, _dataBits, _stopBits)
            {
                Handshake = _handshake,
                ReadTimeout = SerialPort.InfiniteTimeout,
                WriteTimeout = 5000,
                DtrEnable = true,
                RtsEnable = _handshake == Handshake.None || _handshake == Handshake.XOnXOff
            };

            StopParityErrorsBecomingQuestionMarks(_serialPort);

            // A parity error is now REPORTED rather than silently written into the text.
            _serialPort.ErrorReceived += OnSerialError;

            _serialPort.Open();

            // Start receive loop on the BaseStream
            _receiveCts = new CancellationTokenSource();
            _receiveTask = ReceiveLoopAsync(_receiveCts.Token);

            SetStatus(ConnectionStatus.Connected);
            ApplicationLogger.Log($"[SerialConnection] Connected to {_portName}: {_baudRate}bps, {_dataBits} data bits, {_stopBits} stop bits, {_parity} parity, {_handshake} flow control");
            ApplicationLogger.Log($"[SerialConnection] Actual SerialPort: {_serialPort.BaudRate}bps, {_serialPort.DataBits} data bits, {_serialPort.StopBits} stop bits, {_serialPort.Parity} parity, {_serialPort.Handshake} flow control");
        }
        catch (Exception ex)
        {
            // Never leave a half-created port object behind: Open() failing (port in use,
            // access denied) must not park an undisposed SerialPort in the field waiting
            // for someone else to remember to call Dispose.
            ClosePort();
            SetStatus(ConnectionStatus.Error);
            ErrorOccurred?.Invoke(ex);
            throw;
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Stops the serial driver replacing a byte that fails its parity check with a question mark.
    /// </summary>
    /// <param name="port">
    /// The port to configure. Must not be open yet.
    /// </param>
    /// <remarks>
    /// <para><b>The defect this exists for, measured 30 August 2026</b></para>
    /// A SINTRAN listing over a 7E1 line came through with a '?' sprinkled into it roughly every
    /// thirty characters - "SINT?RAN:DATA;1", "ND-500-M?ON-J04" - while TeraTerm on the same line
    /// showed the text clean. The question marks were INSERTED, not substituted for a missing
    /// character: the surrounding text was complete and one byte too long.
    /// <para><b>Where the byte comes from</b></para>
    /// <c>SerialPort.ParityReplace</c> defaults to 63, which is '?' - measured by constructing a
    /// SerialPort and reading the property, not taken from memory. Setting any parity other than
    /// None turns on the Windows DCB error character, so every byte the driver thinks failed its
    /// parity check is handed to the application as '?'. Nothing in this application ever set the
    /// property, so the default was in force on every serial connection ever made here.
    /// <para><b>Why zero rather than some other byte</b></para>
    /// Zero switches the replacement off, so a suspect byte arrives as it was received. A terminal
    /// must never invent a character that was not sent: a '?' in the middle of a file name is
    /// indistinguishable from one the host meant, and it sent this reader hunting a fault in the
    /// machine rather than in the cable. The errors are not swallowed - ErrorReceived logs them,
    /// which is the honest way to say "this line is misconfigured or noisy".
    /// <para><b>This does not fix the underlying line</b></para>
    /// Parity errors at that rate mean the framing or the cable is wrong. This stops the corruption
    /// reaching the screen and makes the real fault visible; it does not make a bad line good.
    /// </remarks>
    public static void StopParityErrorsBecomingQuestionMarks(SerialPort port)
    {
        port.ParityReplace = 0;
    }

    /// <summary>
    /// Logs a parity, framing or overrun error reported by the driver.
    /// </summary>
    /// <param name="sender">
    /// The port that raised it.
    /// </param>
    /// <param name="e">
    /// Which error occurred.
    /// </param>
    /// <remarks>
    /// Logged rather than raised as a connection error: a single parity error is not a reason to
    /// drop the line, but a stream of them is the explanation for text that looks wrong, and that
    /// explanation has to be findable somewhere.
    /// </remarks>
    private void OnSerialError(object sender, SerialErrorReceivedEventArgs e)
    {
        ApplicationLogger.Log($"[SerialConnection] {_portName} line error: {e.EventType} "
            + $"(framing is {_dataBits} data bits, {_parity} parity, {_stopBits} stop bits)");
    }

    public void ReconfigureSerial(int dataBits, int parity, int stopBits)
    {
        if (_serialPort == null || !_serialPort.IsOpen) return;
        _serialPort.DataBits = dataBits;
        _serialPort.Parity = MapParity(parity);
        _serialPort.StopBits = MapStopBits(stopBits);
        // Keep the receive path's 7-bit mask in step with the framing now in force.
        _dataBits = dataBits;
        ApplicationLogger.Log($"[SerialConnection] Reconfigured: {dataBits} data bits, parity={_serialPort.Parity}, stop={_serialPort.StopBits}");
    }

    public async Task SendAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
    {
        if (Status != ConnectionStatus.Connected || _serialPort == null || !_serialPort.IsOpen)
            throw new InvalidOperationException($"Cannot send when status is {Status}");

        // On a 7-bit line the UART physically cannot carry bit 7, so it drops it whatever we
        // hand over. Masking here changes NOTHING on the wire — it makes the log and the
        // protocol trace record what was actually transmitted instead of a byte that never
        // left. A Norwegian 'Æ' encoded as 0xC6 arrives at the host as 0x46 'F'; a trace that
        // still says 0xC6 sends the next person hunting a host-side fault that is not there.
        //
        // Rented rather than allocated: the caller's buffer is read-only and every keystroke
        // comes through here.
        var toSend = data;
        byte[]? rented = null;
        if (_dataBits == 7 && data.Length > 0)
        {
            rented = System.Buffers.ArrayPool<byte>.Shared.Rent(data.Length);
            data.Span.CopyTo(rented);
            StripHighBitIfSevenBit(new Span<byte>(rented, 0, data.Length), _dataBits);
            toSend = new ReadOnlyMemory<byte>(rented, 0, data.Length);
        }

        try
        {
            ApplicationLogger.Log($"[SerialConnection] TX {toSend.Length} bytes: {FormatBytes(toSend.Span)}");

            if (TransmitDelayPerCharMs > 0 || TransmitDelayPerLineMs > 0)
            {
                await SendPacedAsync(toSend, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                // The unpaced path is left exactly as it was, so a connection that asks for no
                // delay writes the whole buffer in one call as it always has.
                await _serialPort.BaseStream.WriteAsync(toSend, cancellationToken);
                await _serialPort.BaseStream.FlushAsync(cancellationToken);
            }
        }
        catch (Exception ex)
        {
            SetStatus(ConnectionStatus.Error);
            ErrorOccurred?.Invoke(ex);
            throw;
        }
        finally
        {
            if (rented != null)
            {
                System.Buffers.ArrayPool<byte>.Shared.Return(rented);
            }
        }
    }

    /// <summary>
    /// Smallest gap in milliseconds between two bytes sent. Zero sends at full speed.
    /// </summary>
    /// <remarks>
    /// Set from the connection. See <see cref="SendPacedAsync"/> for what it is for.
    /// </remarks>
    public int TransmitDelayPerCharMs { get; set; }

    /// <summary>
    /// Extra pause in milliseconds after a line ending is sent. Zero adds nothing.
    /// </summary>
    public int TransmitDelayPerLineMs { get; set; }

    /// <summary>
    /// Writes a buffer one byte at a time, pausing between bytes and after each line ending.
    /// </summary>
    /// <param name="data">
    /// The bytes to send.
    /// </param>
    /// <param name="cancellationToken">
    /// Cancels the send between bytes.
    /// </param>
    /// <returns>
    /// A task that completes when the last byte has been written.
    /// </returns>
    /// <remarks>
    /// <para><b>Why a terminal ever has to slow down</b></para>
    /// A real terminal is paced by a person's fingers. This one sends a whole line in one burst
    /// with no gap, and hardware that POLLS its UART in software rather than taking an interrupt
    /// per character can lose bytes at that rate: the next character overwrites the last before
    /// the poll comes round. The ND-120's emulated period UART showed exactly that, and it is why
    /// a scripted "li-fi" reached SINTRAN as "HF" on 31 August 2026.
    /// <para><b>Flushed per byte on purpose</b></para>
    /// Writing a byte and flushing it is what puts a real gap on the wire; buffering them and
    /// flushing once would reassemble the burst this exists to break up.
    /// <para><b>The line delay is added to the character delay, not instead of it</b></para>
    /// A host doing real work when a line arrives - echoing a prompt, opening a file - needs
    /// longer than one character time, and still needs the per-character gap for the rest.
    /// Applied after CR and after LF, so it fits whichever ending the connection uses without
    /// having to be told which that is.
    /// <para><b>These are MINIMUM gaps, and Windows rounds them up</b></para>
    /// Measured on COM11 at 115200, sending eleven bytes: 10 ms per character asked for 100 ms of
    /// gaps and really took 158, and 20 ms asked for 200 and took 306. <c>Task.Delay</c> is bounded
    /// below by the system timer tick, about 15.6 ms, so anything under that is rounded up to it.
    /// The line delay tracks much more closely because it is asked for once - 300 ms measured 306.
    /// A caller wanting a precise character rate cannot have one from here; a caller wanting "at
    /// least this much space between bytes", which is what slow hardware needs, gets it.
    /// </remarks>
    private async Task SendPacedAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken)
    {
        var stream = _serialPort!.BaseStream;

        for (int i = 0; i < data.Length; i++)
        {
            if (i > 0 && TransmitDelayPerCharMs > 0)
            {
                await Task.Delay(TransmitDelayPerCharMs, cancellationToken).ConfigureAwait(false);
            }

            await stream.WriteAsync(data.Slice(i, 1), cancellationToken).ConfigureAwait(false);
            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);

            byte b = data.Span[i];
            if (TransmitDelayPerLineMs > 0 && (b == 0x0D || b == 0x0A))
            {
                await Task.Delay(TransmitDelayPerLineMs, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// Closes the port and stops the receive loop.
    /// </summary>
    /// <remarks>
    /// <para><b>THE PORT IS CLOSED BEFORE THE RECEIVE LOOP IS WAITED FOR, AND THAT ORDER IS THE
    /// WHOLE POINT</b></para>
    /// This method used to cancel the token, await the receive task, and only then close the port.
    /// On Windows that hangs forever on a quiet line: <c>SerialPort.BaseStream.ReadAsync</c> does
    /// NOT honour a cancellation token - the read stays pending until bytes arrive or the handle is
    /// closed. A SINTRAN console is silent most of the time, so cancelling achieved nothing, the
    /// await never returned, and the close and dispose below it never ran.
    ///
    /// The port therefore stayed open for the life of the application, and the next connect
    /// attempt - by RetroTerm or by anything else - failed with "Access to the path 'COM11' is
    /// denied". Reported 31 August 2026 after several earlier disposal fixes had failed to help,
    /// which they could not: every one of them sat AFTER the await that never completed.
    ///
    /// Closing the handle first is what makes the pending read fail, which is what lets the loop
    /// finish. The wait afterwards is bounded as well, so a receive loop that is stuck for any
    /// other reason delays the disconnect briefly instead of holding the port for ever.
    /// </remarks>
    /// <returns>
    /// A task that completes once the port is closed.
    /// </returns>
    public async Task DisconnectAsync()
    {
        if (Status == ConnectionStatus.Disconnected)
            return;

        SetStatus(ConnectionStatus.Disconnecting);

        // Tell the loop to stop, for the case where it IS between reads and can see the flag.
        _receiveCts?.Cancel();

        // Then take the handle away, which is what actually ends a pending read.
        ClosePort();

        // Only now wait for the loop, and never indefinitely.
        if (_receiveTask != null)
        {
            try
            {
                await Task.WhenAny(_receiveTask, Task.Delay(ReceiveLoopShutdownTimeoutMs))
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Expected
            }
            catch (Exception ex)
            {
                // The loop's own failure must not stop a disconnect - the port is already closed.
                ApplicationLogger.Log($"[SerialConnection] receive loop ended with: {ex.Message}");
            }
            _receiveTask = null;
        }

        SetStatus(ConnectionStatus.Disconnected);
        ApplicationLogger.Log($"[SerialConnection] Disconnected from {_portName}");
    }

    /// <summary>
    /// How long a disconnect waits for the receive loop to notice the port has gone.
    /// </summary>
    /// <remarks>
    /// Generous for a loop that only has to fail one read, and short enough that a person clicking
    /// Disconnect never watches the window sit still. Exceeding it is not a failure worth
    /// reporting: the handle is already released by then, which is the thing that matters.
    /// </remarks>
    private const int ReceiveLoopShutdownTimeoutMs = 2000;

    /// <summary>
    /// How long <see cref="ClosePort"/> waits for the physical <c>port.Close()</c> call before
    /// giving up on it and returning anyway.
    /// </summary>
    /// <remarks>
    /// <c>SerialPort.Close()</c> takes no timeout of its own - unlike the pending read, which is
    /// bounded by <see cref="ReceiveLoopShutdownTimeoutMs"/>, the Win32 <c>CloseHandle</c> call
    /// underneath it can block the calling thread for as long as the driver takes to answer. On a
    /// real USB-to-UART bridge (the ND-120 Nexys board's, specifically) that can be indefinite -
    /// measured 31 August 2026 as a full application hang: popping a COM11 tab back in and closing
    /// it blocked the UI thread inside <c>TerminalSession.Dispose()</c>'s synchronous
    /// <c>DisconnectAsync().GetAwaiter().GetResult()</c>, which sits directly on top of this call,
    /// for over five minutes with no sign of returning on its own.
    /// </remarks>
    private const int PortCloseTimeoutMs = 2000;

    /// <summary>
    /// Unhooks the error handler and releases the port handle. Safe to call more than once.
    /// </summary>
    /// <remarks>
    /// <para><b>One place</b></para>
    /// Releasing the handle happens on three paths - a deliberate disconnect, a failed connect and
    /// Dispose - and a port released on only two of them is exactly the defect this class kept
    /// having.
    ///
    /// <para><b>Why the actual close runs on a background thread with a timeout</b></para>
    /// <c>port.Close()</c> and <c>port.Dispose()</c> are moved off the caller's thread and bounded
    /// to <see cref="PortCloseTimeoutMs"/>, for the reason recorded on that constant: a stuck
    /// driver can hang the call that never has a timeout of its own. If the close does not finish
    /// in time, this method returns anyway - the field is already cleared, so nothing in this class
    /// can reach the handle again - and the background task is left to finish or leak on its own
    /// rather than holding whoever called Dispose hostage to a USB driver.
    /// </remarks>
    private void ClosePort()
    {
        var port = _serialPort;
        if (port == null) return;

        _serialPort = null;

        try { port.ErrorReceived -= OnSerialError; } catch { /* best effort */ }

        var closeTask = Task.Run(() =>
        {
            try { if (port.IsOpen) port.Close(); } catch { /* the handle is going away regardless */ }
            try { port.Dispose(); } catch { /* the handle is going away regardless */ }
        });

        if (!closeTask.Wait(PortCloseTimeoutMs))
        {
            // Not a failure worth throwing over: the field is already null, so this class will
            // never touch the handle again either way. Logged because a driver that does this
            // once may keep doing it, and that is worth knowing about even though it is no longer
            // blocking anyone.
            ApplicationLogger.Log(
                $"[SerialConnection] port.Close() on {_portName} did not return within " +
                $"{PortCloseTimeoutMs}ms - continuing without waiting for it");
        }
    }

    private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
    {
        var buffer = new byte[4096];

        try
        {
            while (!cancellationToken.IsCancellationRequested && _serialPort != null && _serialPort.IsOpen)
            {
                var bytesRead = await _serialPort.BaseStream.ReadAsync(buffer, 0, buffer.Length, cancellationToken);
                if (bytesRead == 0)
                {
                    // Stream closed
                    break;
                }

                StripHighBitIfSevenBit(new Span<byte>(buffer, 0, bytesRead), _dataBits);

                ApplicationLogger.Log($"[SerialConnection] RX {bytesRead} bytes: {FormatBytes(new ReadOnlySpan<byte>(buffer, 0, bytesRead))}");
                DataReceived?.Invoke(new ReadOnlyMemory<byte>(buffer, 0, bytesRead));
            }

            // Normal exit — port was closed. The old guard also required Status == Connected,
            // which left a hole: a send failure puts the connection in Error, and if the stream
            // then ended, no Disconnected ever fired — so the session never ran its cleanup and
            // the COM port stayed open (and the device unopenable, "access denied") until the tab
            // was closed. Any un-cancelled exit of this loop means the line is gone: say so.
            if (!cancellationToken.IsCancellationRequested && Status != ConnectionStatus.Disconnected)
            {
                SetStatus(ConnectionStatus.Disconnected);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected when disconnecting
        }
        catch (Exception ex)
        {
            // A DELIBERATE disconnect closes the handle underneath this read, which is exactly how
            // the read is made to end - see DisconnectAsync. The ObjectDisposedException or
            // IOException that arrives here is then the mechanism working, not a fault, and
            // reporting it would put "Connection error" on the screen every time somebody pressed
            // Disconnect. Cancellation requested, or a status that already says we are going, is
            // how this tells the two apart.
            bool weAreClosing = cancellationToken.IsCancellationRequested
                || Status == ConnectionStatus.Disconnecting
                || Status == ConnectionStatus.Disconnected;

            SetStatus(ConnectionStatus.Disconnected);

            if (!weAreClosing)
            {
                ErrorOccurred?.Invoke(ex is IOException
                    ? new IOException("Serial port connection lost")
                    : ex);
            }
            else
            {
                ApplicationLogger.Log($"[SerialConnection] receive loop ended on close: {ex.GetType().Name}");
            }
        }
    }

    /// <summary>
    /// Clears bit 7 of every received byte when the line is running 7 data bits.
    /// </summary>
    /// <param name="data">
    /// The bytes just read from the port, edited in place.
    /// </param>
    /// <param name="dataBits">
    /// The data bits the port is running RIGHT NOW — not the value it was opened with, since
    /// ReconfigureSerial changes it on a live port.
    /// </param>
    /// <remarks>
    /// <para><b>A 7-bit line cannot carry an 8-bit byte</b></para>
    /// Bit 7 is not data on such a line: it is at best the parity bit leaking through, at worst
    /// line noise. It is stripped before the emulator ever sees it.
    /// <para><b>Why this is worse than one wrong glyph</b></para>
    /// The escape parser reads 0x80-0x9F as 8-bit C1 CONTROLS. On a 7E1 line ESC (0x1B) has odd
    /// population, so even parity sets bit 7 and makes it 0x9B — which the parser takes as CSI and
    /// then swallows the following characters as a control sequence. The screen loses whole runs of
    /// text rather than showing one bad character, and anything reading that screen over MCP sees
    /// the gap with nothing to explain it.
    /// <para><b>Gated on the framing, never applied unconditionally</b></para>
    /// A genuine 8-bit ND or TDV line needs 0xA0-0xFF delivered intact — the national character
    /// sets live up there — and masking those is a defect this project already had to fix once in
    /// the parser. Eight-bit lines pass through here untouched.
    /// <para><b>Not measured against real hardware</b></para>
    /// Whether the Windows driver already strips parity at DataBits=7 was NOT measured here; that
    /// needs a live port. This makes the outcome the same either way.
    /// </remarks>
    public static void StripHighBitIfSevenBit(Span<byte> data, int dataBits)
    {
        if (dataBits != 7) return;

        for (int i = 0; i < data.Length; i++)
        {
            data[i] &= 0x7F;
        }
    }

    /// <summary>
    /// Returns available serial port names on the system.
    /// </summary>
    public static string[] GetAvailablePorts()
    {
        return SerialPort.GetPortNames();
    }

    private void SetStatus(ConnectionStatus newStatus)
    {
        if (Status != newStatus)
        {
            Status = newStatus;
            StatusChanged?.Invoke(newStatus);
        }
    }

    private static StopBits MapStopBits(int value)
    {
        return value switch
        {
            0 => StopBits.None,
            1 => StopBits.One,
            2 => StopBits.Two,
            3 => StopBits.OnePointFive,
            _ => StopBits.One
        };
    }

    private static Parity MapParity(int value)
    {
        return value switch
        {
            0 => Parity.None,
            1 => Parity.Odd,
            2 => Parity.Even,
            3 => Parity.Mark,
            4 => Parity.Space,
            _ => Parity.None
        };
    }

    private static Handshake MapHandshake(int value)
    {
        return value switch
        {
            0 => Handshake.None,
            1 => Handshake.XOnXOff,
            2 => Handshake.RequestToSend,
            3 => Handshake.RequestToSendXOnXOff,
            _ => Handshake.None
        };
    }

    private static string FormatBytes(ReadOnlySpan<byte> data)
    {
        int len = data.Length > 32 ? 32 : data.Length;
        var sb = new System.Text.StringBuilder(len * 3);
        for (int i = 0; i < len; i++)
        {
            if (i > 0) sb.Append('-');
            sb.Append(data[i].ToString("X2"));
        }
        if (data.Length > 32) sb.Append("...");
        return sb.ToString();
    }

    public void Dispose()
    {
        try { _receiveCts?.Cancel(); } catch (ObjectDisposedException) { }

        // The same one place that DisconnectAsync and the failed-connect path use. Dispose does
        // NOT wait for the receive loop: closing the handle is what ends its pending read, and a
        // synchronous Dispose must never block on another thread to release a port.
        ClosePort();

        try { _receiveCts?.Dispose(); } catch { }

        SetStatus(ConnectionStatus.Disconnected);
    }
}
