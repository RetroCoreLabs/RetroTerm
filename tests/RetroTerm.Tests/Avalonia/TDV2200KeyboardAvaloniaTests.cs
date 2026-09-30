using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using RetroTerm.Core.Protocols;
using RetroTerm.Core.Protocols.Net;
using RetroTerm.Core.Protocols.TelnetServer.Telnet;
using RetroTerm.Core.Protocols.TelnetServer.Utilities;
using RetroTerm.Core.Session;
using RetroTerm.Core.Terminal.Emulators.TDV;
using Xunit;

namespace RetroTerm.Tests.Avalonia;

/// <summary>
/// Tests TDV2200 keyboard input through the full stack.
/// Validates that keyboard mapper produces correct sequences
/// and that they are properly transmitted through the connection.
/// </summary>
[Collection("Avalonia")]
public class TDV2200KeyboardAvaloniaTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private TcpListener? _server;
    private TcpClient? _serverClient;
    private TerminalSession? _session;
    private IConnection? _connection;
    private TDV2200Emulator? _emulator;
    private TelnetSession? _serverSession;
    private TDVKeyboardMapper? _keyboardMapper;
    private int _serverPort;

    public TDV2200KeyboardAvaloniaTests(ITestOutputHelper output)
    {
        _output = output;
    }

    public void Dispose()
    {
        // Close TCP resources first to unblock pending reads
        try { _serverClient?.Close(); } catch { }
        try { _server?.Stop(); } catch { }

        // Then dispose managed resources
        try { _connection?.Dispose(); } catch { }
        try { _session?.Dispose(); } catch { }
    }

    /// <summary>
    /// Sets up connection and keyboard mapper
    /// </summary>
    private async Task SetupConnectionAsync()
    {
        // Start TCP server
        _server = new TcpListener(IPAddress.Loopback, 0);
        _server.Start();
        _serverPort = ((IPEndPoint)_server.LocalEndpoint).Port;

        // Create emulator and session
        _emulator = new TDV2200Emulator(80, 24);
        _session = new TerminalSession(_emulator, "TDV2200 Keyboard Test");

        // Create keyboard mapper (TDV2200 mode, no 2115 mode, use ESC codes for arrows)
        _keyboardMapper = new TDVKeyboardMapper(is2115Mode: false, tdvArrowsIsEscCode: true);

        // Create connection
        var parameters = new ConnectionFactory.ConnectionParameters
        {
            Protocol = ConnectionFactory.ProtocolType.Telnet,
            Host = "127.0.0.1",
            Port = _serverPort,
            EmulatorType = "TDV2200"
        };

        _connection = ConnectionFactory.CreateConnection(parameters);

        // Connect
        var connectTask = _session.ConnectAsync(_connection);
        _serverClient = await _server.AcceptTcpClientAsync();
        _serverSession = new TelnetSession(_serverClient);
        await connectTask;

        // Telnet negotiation
        await _serverSession.Negotiator.SendInitialAsync();
        await Task.Delay(200);

        _output.WriteLine("Connection and keyboard mapper ready");
    }

    /// <summary>
    /// Sends a key sequence through the session and captures what the server receives
    /// </summary>
    private async Task<string?> SendKeyAndCaptureAsync(string keyName, bool shift = false, bool ctrl = false, bool alt = false, int timeoutMs = 500)
    {
        // Clear any pending data
        while (_serverClient!.Available > 0)
        {
            var buffer = new byte[_serverClient.Available];
            await _serverSession!.Stream.ReadExactlyAsync(buffer);
        }

        // Map key to sequence
        var sequence = _keyboardMapper!.MapKey(keyName, shift, ctrl, alt);
        if (string.IsNullOrEmpty(sequence))
        {
            _output.WriteLine($"Key {keyName} (shift={shift}, ctrl={ctrl}, alt={alt}) produced no sequence");
            return null;
        }

        _output.WriteLine($"Key {keyName} (shift={shift}, ctrl={ctrl}, alt={alt}) -> Sequence: {TDVResponseValidator.ToVisibleString(sequence)}");

        // Send through session
        await _session!.SendInputAsync(sequence);

        // Capture what server receives
        var responseBuilder = new List<byte>();
        var cts = new CancellationTokenSource(timeoutMs);

        try
        {
            while (!cts.Token.IsCancellationRequested)
            {
                if (_serverClient.Available > 0)
                {
                    var buffer = new byte[_serverClient.Available];
                    int bytesRead = await _serverSession!.Stream.ReadAsync(buffer, 0, buffer.Length, cts.Token);
                    for (int i = 0; i < bytesRead; i++)
                    {
                        responseBuilder.Add(buffer[i]);
                    }
                    break; // Got data, stop waiting
                }
                await Task.Delay(10, cts.Token);
            }
        }
        catch (OperationCanceledException)
        {
            // Timeout
        }

        if (responseBuilder.Count == 0)
        {
            return null;
        }

        return Encoding.UTF8.GetString(responseBuilder.ToArray());
    }

    #region Function Key Tests

    [AvaloniaFact(Timeout = 10000)]
    public async Task TDV2200_F1Key_ShouldSendCorrectSequence()
    {
        await SetupConnectionAsync();

        var received = await SendKeyAndCaptureAsync("F1");

        Assert.NotNull(received);
        Assert.Equal("\x1b[11~", received); // TDV F1 = ESC [ 11 ~
        _output.WriteLine($"F1 received by server: {TDVResponseValidator.ToVisibleString(received)}");
    }

    [AvaloniaFact(Timeout = 10000)]
    public async Task TDV2200_F5Key_ShouldSendCorrectSequence()
    {
        await SetupConnectionAsync();

        var received = await SendKeyAndCaptureAsync("F5");

        Assert.NotNull(received);
        Assert.Equal("\x1b[15~", received); // TDV F5 = ESC [ 15 ~
        _output.WriteLine($"F5 received by server: {TDVResponseValidator.ToVisibleString(received)}");
    }

    [AvaloniaFact(Timeout = 10000)]
    public async Task TDV2200_F10Key_ShouldSendCorrectSequence()
    {
        await SetupConnectionAsync();

        var received = await SendKeyAndCaptureAsync("F10");

        Assert.NotNull(received);
        Assert.Equal("\x1b[21~", received); // TDV F10 = ESC [ 21 ~
        _output.WriteLine($"F10 received by server: {TDVResponseValidator.ToVisibleString(received)}");
    }

    [AvaloniaFact(Timeout = 10000)]
    public async Task TDV2200_F12Key_ShouldSendCorrectSequence()
    {
        await SetupConnectionAsync();

        var received = await SendKeyAndCaptureAsync("F12");

        Assert.NotNull(received);
        Assert.Equal("\x1b[24~", received); // TDV F12 = ESC [ 24 ~
        _output.WriteLine($"F12 received by server: {TDVResponseValidator.ToVisibleString(received)}");
    }

    #endregion

    #region Navigation Key Tests

    [AvaloniaFact(Timeout = 10000)]
    public async Task TDV2200_ArrowUp_ShouldSendCorrectSequence()
    {
        await SetupConnectionAsync();

        var received = await SendKeyAndCaptureAsync("UP");

        Assert.NotNull(received);
        Assert.Equal("\x1b[A", received); // ESC [ A (with ESC mode enabled)
        _output.WriteLine($"Arrow Up: {TDVResponseValidator.ToVisibleString(received)}");
    }

    [AvaloniaFact(Timeout = 10000)]
    public async Task TDV2200_ArrowDown_ShouldSendCorrectSequence()
    {
        await SetupConnectionAsync();

        var received = await SendKeyAndCaptureAsync("DOWN");

        Assert.NotNull(received);
        Assert.Equal("\x1b[B", received); // ESC [ B
        _output.WriteLine($"Arrow Down: {TDVResponseValidator.ToVisibleString(received)}");
    }

    [AvaloniaFact(Timeout = 10000)]
    public async Task TDV2200_ArrowLeft_ShouldSendCorrectSequence()
    {
        await SetupConnectionAsync();

        var received = await SendKeyAndCaptureAsync("LEFT");

        Assert.NotNull(received);
        Assert.Equal("\x1b[D", received); // ESC [ D
        _output.WriteLine($"Arrow Left: {TDVResponseValidator.ToVisibleString(received)}");
    }

    [AvaloniaFact(Timeout = 10000)]
    public async Task TDV2200_ArrowRight_ShouldSendCorrectSequence()
    {
        await SetupConnectionAsync();

        var received = await SendKeyAndCaptureAsync("RIGHT");

        Assert.NotNull(received);
        Assert.Equal("\x1b[C", received); // ESC [ C
        _output.WriteLine($"Arrow Right: {TDVResponseValidator.ToVisibleString(received)}");
    }

    [AvaloniaFact(Timeout = 10000)]
    public async Task TDV2200_Home_ShouldSendCorrectSequence()
    {
        await SetupConnectionAsync();

        var received = await SendKeyAndCaptureAsync("HOME");

        Assert.NotNull(received);
        Assert.Equal("\x1b[H", received); // ESC [ H (with ESC mode enabled)
        _output.WriteLine($"Home: {TDVResponseValidator.ToVisibleString(received)}");
    }

    [AvaloniaFact(Timeout = 10000)]
    public async Task TDV2200_End_ShouldSendCorrectSequence()
    {
        await SetupConnectionAsync();

        var received = await SendKeyAndCaptureAsync("END");

        Assert.NotNull(received);
        Assert.Equal("\x1b[F", received); // ESC [ F
        _output.WriteLine($"End: {TDVResponseValidator.ToVisibleString(received)}");
    }

    [AvaloniaFact(Timeout = 10000)]
    public async Task TDV2200_PageUp_ShouldSendCorrectSequence()
    {
        await SetupConnectionAsync();

        var received = await SendKeyAndCaptureAsync("PAGEUP");

        Assert.NotNull(received);
        Assert.Equal("\x1b[5~", received); // ESC [ 5 ~
        _output.WriteLine($"PageUp: {TDVResponseValidator.ToVisibleString(received)}");
    }

    [AvaloniaFact(Timeout = 10000)]
    public async Task TDV2200_PageDown_ShouldSendCorrectSequence()
    {
        await SetupConnectionAsync();

        var received = await SendKeyAndCaptureAsync("PAGEDOWN");

        Assert.NotNull(received);
        Assert.Equal("\x1b[6~", received); // ESC [ 6 ~
        _output.WriteLine($"PageDown: {TDVResponseValidator.ToVisibleString(received)}");
    }

    [AvaloniaFact(Timeout = 10000)]
    public async Task TDV2200_Insert_ShouldSendCorrectSequence()
    {
        await SetupConnectionAsync();

        var received = await SendKeyAndCaptureAsync("INSERT");

        Assert.NotNull(received);
        Assert.Equal("\x1b[2~", received); // ESC [ 2 ~
        _output.WriteLine($"Insert: {TDVResponseValidator.ToVisibleString(received)}");
    }

    [AvaloniaFact(Timeout = 10000)]
    public async Task TDV2200_Delete_ShouldSendCorrectSequence()
    {
        await SetupConnectionAsync();

        var received = await SendKeyAndCaptureAsync("DELETE");

        Assert.NotNull(received);
        Assert.Equal("\x1b[3~", received); // ESC [ 3 ~
        _output.WriteLine($"Delete: {TDVResponseValidator.ToVisibleString(received)}");
    }

    #endregion

    #region Control Key Tests

    [AvaloniaFact(Timeout = 10000)]
    public async Task TDV2200_Enter_ShouldSendCR()
    {
        await SetupConnectionAsync();

        var received = await SendKeyAndCaptureAsync("ENTER");

        Assert.NotNull(received);
        Assert.Equal("\r", received); // CR
        _output.WriteLine($"Enter: {TDVResponseValidator.ToVisibleString(received)}");
    }

    [AvaloniaFact(Timeout = 10000)]
    public async Task TDV2200_Tab_ShouldSendTab()
    {
        await SetupConnectionAsync();

        var received = await SendKeyAndCaptureAsync("TAB");

        Assert.NotNull(received);
        Assert.Equal("\t", received); // Tab
        _output.WriteLine($"Tab: {TDVResponseValidator.ToVisibleString(received)}");
    }

    [AvaloniaFact(Timeout = 10000)]
    public async Task TDV2200_Escape_ShouldSendESC()
    {
        await SetupConnectionAsync();

        var received = await SendKeyAndCaptureAsync("ESCAPE");

        Assert.NotNull(received);
        Assert.Equal("\x1b", received); // ESC
        _output.WriteLine($"Escape: {TDVResponseValidator.ToVisibleString(received)}");
    }

    [AvaloniaFact(Timeout = 10000)]
    public async Task TDV2200_Backspace_ShouldSendBS()
    {
        await SetupConnectionAsync();

        var received = await SendKeyAndCaptureAsync("BACKSPACE");

        Assert.NotNull(received);
        Assert.Equal("\x08", received); // BS
        _output.WriteLine($"Backspace: {TDVResponseValidator.ToVisibleString(received)}");
    }

    [AvaloniaFact(Timeout = 10000)]
    public async Task TDV2200_Space_ShouldSendSpace()
    {
        await SetupConnectionAsync();

        var received = await SendKeyAndCaptureAsync("SPACE");

        Assert.NotNull(received);
        Assert.Equal(" ", received); // Space
        _output.WriteLine($"Space: {TDVResponseValidator.ToVisibleString(received)}");
    }

    #endregion

    #region TDV-Specific Key Tests

    [AvaloniaFact(Timeout = 10000)]
    public async Task TDV2200_Help_ShouldSendCorrectSequence()
    {
        await SetupConnectionAsync();

        var received = await SendKeyAndCaptureAsync("HELP");

        Assert.NotNull(received);
        Assert.Equal("\x1b[28~", received); // HELP key
        _output.WriteLine($"HELP: {TDVResponseValidator.ToVisibleString(received)}");
    }

    [AvaloniaFact(Timeout = 10000)]
    public async Task TDV2200_Do_ShouldSendCorrectSequence()
    {
        await SetupConnectionAsync();

        var received = await SendKeyAndCaptureAsync("DO");

        Assert.NotNull(received);
        Assert.Equal("\x1b[29~", received); // DO key
        _output.WriteLine($"DO: {TDVResponseValidator.ToVisibleString(received)}");
    }

    [AvaloniaFact(Timeout = 10000)]
    public async Task TDV2200_Func_ShouldSendCorrectSequence()
    {
        await SetupConnectionAsync();

        var received = await SendKeyAndCaptureAsync("FUNC");

        Assert.NotNull(received);
        Assert.Equal("\x1b[@", received); // FUNC key
        _output.WriteLine($"FUNC: {TDVResponseValidator.ToVisibleString(received)}");
    }

    [AvaloniaFact(Timeout = 10000)]
    public async Task TDV2200_Print_ShouldSendCorrectSequence()
    {
        await SetupConnectionAsync();

        var received = await SendKeyAndCaptureAsync("PRINT");

        Assert.NotNull(received);
        Assert.Equal("\x1b[A", received); // PRINT key
        _output.WriteLine($"PRINT: {TDVResponseValidator.ToVisibleString(received)}");
    }

    [AvaloniaFact(Timeout = 10000)]
    public async Task TDV2200_Exit_ShouldSendCorrectSequence()
    {
        await SetupConnectionAsync();

        var received = await SendKeyAndCaptureAsync("EXIT");

        Assert.NotNull(received);
        Assert.Equal("\x1b[C", received); // EXIT key
        _output.WriteLine($"EXIT: {TDVResponseValidator.ToVisibleString(received)}");
    }

    [AvaloniaFact(Timeout = 10000)]
    public async Task TDV2200_Copy_ShouldSendCorrectSequence()
    {
        await SetupConnectionAsync();

        var received = await SendKeyAndCaptureAsync("COPY");

        Assert.NotNull(received);
        Assert.Equal("\x1b[M", received); // COPY key
        _output.WriteLine($"COPY: {TDVResponseValidator.ToVisibleString(received)}");
    }

    [AvaloniaFact(Timeout = 10000)]
    public async Task TDV2200_Move_ShouldSendCorrectSequence()
    {
        await SetupConnectionAsync();

        var received = await SendKeyAndCaptureAsync("MOVE");

        Assert.NotNull(received);
        Assert.Equal("\x1b[N", received); // MOVE key
        _output.WriteLine($"MOVE: {TDVResponseValidator.ToVisibleString(received)}");
    }

    #endregion

    #region PUSH Key Tests

    [AvaloniaFact(Timeout = 10000)]
    public async Task TDV2200_Push1_ShouldSendCorrectSequence()
    {
        await SetupConnectionAsync();

        var received = await SendKeyAndCaptureAsync("PUSH1");

        Assert.NotNull(received);
        Assert.Equal("\x1b[?1~", received); // PUSH1 key
        _output.WriteLine($"PUSH1: {TDVResponseValidator.ToVisibleString(received)}");
    }

    [AvaloniaFact(Timeout = 10000)]
    public async Task TDV2200_Push8_ShouldSendCorrectSequence()
    {
        await SetupConnectionAsync();

        var received = await SendKeyAndCaptureAsync("PUSH8");

        Assert.NotNull(received);
        Assert.Equal("\x1b[?8~", received); // PUSH8 key
        _output.WriteLine($"PUSH8: {TDVResponseValidator.ToVisibleString(received)}");
    }

    #endregion

    #region Modifier Key Tests

    [AvaloniaFact(Timeout = 10000)]
    public async Task TDV2200_ShiftArrowUp_ShouldSendModifiedSequence()
    {
        await SetupConnectionAsync();

        var received = await SendKeyAndCaptureAsync("UP", shift: true);

        Assert.NotNull(received);
        Assert.Equal("\x1b[1;2A", received); // Shift+Up
        _output.WriteLine($"Shift+Up: {TDVResponseValidator.ToVisibleString(received)}");
    }

    [AvaloniaFact(Timeout = 10000)]
    public async Task TDV2200_CtrlArrowRight_ShouldSendModifiedSequence()
    {
        await SetupConnectionAsync();

        var received = await SendKeyAndCaptureAsync("RIGHT", ctrl: true);

        Assert.NotNull(received);
        Assert.Equal("\x1b[1;5C", received); // Ctrl+Right
        _output.WriteLine($"Ctrl+Right: {TDVResponseValidator.ToVisibleString(received)}");
    }

    [AvaloniaFact(Timeout = 10000)]
    public async Task TDV2200_AltArrowDown_ShouldSendModifiedSequence()
    {
        await SetupConnectionAsync();

        var received = await SendKeyAndCaptureAsync("DOWN", alt: true);

        Assert.NotNull(received);
        Assert.Equal("\x1b[1;3B", received); // Alt+Down
        _output.WriteLine($"Alt+Down: {TDVResponseValidator.ToVisibleString(received)}");
    }

    #endregion

    #region Extended Function Key Tests

    [AvaloniaFact(Timeout = 10000)]
    public async Task TDV2200_F13_ShouldSendCorrectSequence()
    {
        await SetupConnectionAsync();

        var received = await SendKeyAndCaptureAsync("F13");

        Assert.NotNull(received);
        Assert.Equal("\x1b[25~", received); // F13
        _output.WriteLine($"F13: {TDVResponseValidator.ToVisibleString(received)}");
    }

    [AvaloniaFact(Timeout = 10000)]
    public async Task TDV2200_F20_ShouldSendCorrectSequence()
    {
        await SetupConnectionAsync();

        var received = await SendKeyAndCaptureAsync("F20");

        Assert.NotNull(received);
        Assert.Equal("\x1b[34~", received); // F20
        _output.WriteLine($"F20: {TDVResponseValidator.ToVisibleString(received)}");
    }

    #endregion

    #region TDV Control Key Tests

    [AvaloniaFact(Timeout = 10000)]
    public async Task TDV2200_TabLeft_ShouldSendCorrectSequence()
    {
        await SetupConnectionAsync();

        var received = await SendKeyAndCaptureAsync("TAB_LEFT");

        Assert.NotNull(received);
        Assert.Equal("\x1b[Z", received); // CSI Z
        _output.WriteLine($"Tab Left: {TDVResponseValidator.ToVisibleString(received)}");
    }

    [AvaloniaFact(Timeout = 10000)]
    public async Task TDV2200_RollUp_ShouldSendCorrectSequence()
    {
        await SetupConnectionAsync();

        var received = await SendKeyAndCaptureAsync("ROLL_UP");

        Assert.NotNull(received);
        Assert.Equal("\x0c", received); // FF (Form Feed)
        _output.WriteLine($"Roll Up: {TDVResponseValidator.ToVisibleString(received)}");
    }

    [AvaloniaFact(Timeout = 10000)]
    public async Task TDV2200_ErasePage_ShouldSendCorrectSequence()
    {
        await SetupConnectionAsync();

        var received = await SendKeyAndCaptureAsync("ERASE_PAGE");

        Assert.NotNull(received);
        Assert.Equal("\x19", received); // EM (End of Medium)
        _output.WriteLine($"Erase Page: {TDVResponseValidator.ToVisibleString(received)}");
    }

    [AvaloniaFact(Timeout = 10000)]
    public async Task TDV2200_DelLine_ShouldSendCorrectSequence()
    {
        await SetupConnectionAsync();

        var received = await SendKeyAndCaptureAsync("DEL_LINE");

        Assert.NotNull(received);
        Assert.Equal("\x1b[M", received); // CSI M
        _output.WriteLine($"Del Line: {TDVResponseValidator.ToVisibleString(received)}");
    }

    [AvaloniaFact(Timeout = 10000)]
    public async Task TDV2200_InsLine_ShouldSendCorrectSequence()
    {
        await SetupConnectionAsync();

        var received = await SendKeyAndCaptureAsync("INS_LINE");

        Assert.NotNull(received);
        Assert.Equal("\x1b[L", received); // CSI L
        _output.WriteLine($"Ins Line: {TDVResponseValidator.ToVisibleString(received)}");
    }

    #endregion

    #region Multiple Keys Test

    [AvaloniaFact(Timeout = 10000)]
    public async Task TDV2200_MultipleKeys_ShouldAllTransmit()
    {
        await SetupConnectionAsync();

        // Test a sequence of keys
        var keys = new[] { "F1", "UP", "DOWN", "ENTER", "TAB", "ESCAPE" };
        var expectedSequences = new[] { "\x1b[11~", "\x1b[A", "\x1b[B", "\r", "\t", "\x1b" };

        for (int i = 0; i < keys.Length; i++)
        {
            var received = await SendKeyAndCaptureAsync(keys[i]);
            Assert.NotNull(received);
            Assert.Equal(expectedSequences[i], received);
            _output.WriteLine($"Key {i + 1} ({keys[i]}): OK");
        }

        _output.WriteLine("All keys transmitted correctly!");
    }

    #endregion
}
