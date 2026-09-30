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
using RetroTerm.Core.Terminal.Input;
using Xunit;

namespace RetroTerm.Tests.Avalonia;

/// <summary>
/// Tests Alt+key combinations as alternative input method for TDV special keys.
/// Verifies that Alt+letter/number combinations correctly map to TDV-specific keys
/// that are not available on a standard PC keyboard.
/// Sequences are TDV-native CSI nn _ format from TDV2200KeyRegistry.
/// </summary>
[Collection("Avalonia")]
public class TDV2200AltKeyTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private TcpListener? _server;
    private TcpClient? _serverClient;
    private TerminalSession? _session;
    private IConnection? _connection;
    private TDV2200Emulator? _emulator;
    private TelnetSession? _serverSession;
    private TDV2200KeyboardMapper? _keyboardMapper;
    private int _serverPort;

    public TDV2200AltKeyTests(ITestOutputHelper output)
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

    private async Task SetupConnectionAsync()
    {
        // Ensure default key binding configuration for consistent test behavior
        TDVKeyBindingConfiguration.ResetForTesting();

        _server = new TcpListener(IPAddress.Loopback, 0);
        _server.Start();
        _serverPort = ((IPEndPoint)_server.LocalEndpoint).Port;

        _emulator = new TDV2200Emulator(80, 24);
        _session = new TerminalSession(_emulator, "TDV2200 Alt Key Test");

        // Use the VK-code-based keyboard mapper (same as UI uses)
        _keyboardMapper = new TDV2200KeyboardMapper();

        var parameters = new ConnectionFactory.ConnectionParameters
        {
            Protocol = ConnectionFactory.ProtocolType.Telnet,
            Host = "127.0.0.1",
            Port = _serverPort,
            EmulatorType = "TDV2200"
        };

        _connection = ConnectionFactory.CreateConnection(parameters);

        var connectTask = _session.ConnectAsync(_connection);
        _serverClient = await _server.AcceptTcpClientAsync();
        _serverSession = new TelnetSession(_serverClient);
        await connectTask;

        await _serverSession.Negotiator.SendInitialAsync();
        await Task.Delay(200);

        _output.WriteLine("Connection ready for Alt+key testing");
    }

    /// <summary>
    /// Sends a key using VK code through the session and captures server response
    /// </summary>
    private async Task<string?> SendVKKeyAndCaptureAsync(int vkCode, KeyModifiers modifiers, int timeoutMs = 500)
    {
        // Clear pending data
        while (_serverClient!.Available > 0)
        {
            var buffer = new byte[_serverClient.Available];
            await _serverSession!.Stream.ReadExactlyAsync(buffer);
        }

        // Map key using VK-code-based mapper
        var sequence = _keyboardMapper!.MapKey(vkCode, modifiers, TerminalModes.None);
        if (string.IsNullOrEmpty(sequence))
        {
            _output.WriteLine($"VK {vkCode} + {modifiers} produced no sequence");
            return null;
        }

        _output.WriteLine($"VK {vkCode} + {modifiers} -> Sequence: {TDVResponseValidator.ToVisibleString(sequence)}");

        // Send through session
        await _session!.SendInputAsync(sequence);

        // Capture server response
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
                    break;
                }
                await Task.Delay(10, cts.Token);
            }
        }
        catch (OperationCanceledException)
        {
        }

        if (responseBuilder.Count == 0)
        {
            return null;
        }

        return Encoding.UTF8.GetString(responseBuilder.ToArray());
    }

    #region Alt+Letter Application Key Tests

    [AvaloniaFact(Timeout = 10000)]
    public async Task AltH_ShouldSendHELP()
    {
        await SetupConnectionAsync();
        var received = await SendVKKeyAndCaptureAsync(72, KeyModifiers.Alt); // VK_H = 72
        Assert.NotNull(received);
        Assert.Equal("\x1b[46_", received);
        _output.WriteLine($"Alt+H = HJELP: PASS");
    }

    [AvaloniaFact(Timeout = 10000)]
    public async Task AltD_ShouldSendDO()
    {
        await SetupConnectionAsync();
        var received = await SendVKKeyAndCaptureAsync(68, KeyModifiers.Alt); // VK_D = 68
        Assert.NotNull(received);
        Assert.Equal("\x1b[20_", received);
        _output.WriteLine($"Alt+D = REPLACE: PASS");
    }

    [AvaloniaFact(Timeout = 10000)]
    public async Task AltU_ShouldSendFUNC()
    {
        await SetupConnectionAsync();
        var received = await SendVKKeyAndCaptureAsync(85, KeyModifiers.Alt); // VK_U = 85
        Assert.NotNull(received);
        Assert.Equal("\x1b[42_", received);
        _output.WriteLine($"Alt+U = FUNK: PASS");
    }

    [AvaloniaFact(Timeout = 10000)]
    public async Task AltP_ShouldSendPRINT()
    {
        await SetupConnectionAsync();
        var received = await SendVKKeyAndCaptureAsync(80, KeyModifiers.Alt); // VK_P = 80
        Assert.NotNull(received);
        Assert.Equal("\x1b[44_", received);
        _output.WriteLine($"Alt+P = SKRIV: PASS");
    }

    [AvaloniaFact(Timeout = 10000)]
    public async Task AltX_ShouldSendGUILLEMETS()
    {
        await SetupConnectionAsync();
        var received = await SendVKKeyAndCaptureAsync(88, KeyModifiers.Alt); // VK_X = 88
        Assert.NotNull(received);
        Assert.Equal("\x1b[22_", received);
        _output.WriteLine($"Alt+X = GUILLEMETS: PASS");
    }

    [AvaloniaFact(Timeout = 10000)]
    public async Task AltBackspace_ShouldSendANGRE()
    {
        await SetupConnectionAsync();
        var received = await SendVKKeyAndCaptureAsync(8, KeyModifiers.Alt); // VK_BACK = 8
        Assert.NotNull(received);
        Assert.Equal("\x1b[30_", received);
        _output.WriteLine($"Alt+Backspace = ANGRE: PASS");
    }

    [AvaloniaFact(Timeout = 10000)]
    public async Task AltM_ShouldSendCOMMAND()
    {
        await SetupConnectionAsync();
        var received = await SendVKKeyAndCaptureAsync(77, KeyModifiers.Alt); // VK_M = 77
        Assert.NotNull(received);
        Assert.Equal("\x1b[84_", received);
        _output.WriteLine($"Alt+M = MODE: PASS");
    }

    [AvaloniaFact(Timeout = 10000)]
    public async Task AltF_ShouldSendFIND()
    {
        await SetupConnectionAsync();
        var received = await SendVKKeyAndCaptureAsync(70, KeyModifiers.Alt); // VK_F = 70
        Assert.NotNull(received);
        Assert.Equal("\x1b[18_", received);
        _output.WriteLine($"Alt+F = SEARCH: PASS");
    }

    [AvaloniaFact(Timeout = 10000)]
    public async Task AltS_ShouldSendSLUTT()
    {
        await SetupConnectionAsync();
        var received = await SendVKKeyAndCaptureAsync(83, KeyModifiers.Alt); // VK_S = 83
        Assert.NotNull(received);
        Assert.Equal("\x1b[48_", received);
        _output.WriteLine($"Alt+S = SLUTT: PASS");
    }

    #endregion

    #region Alt+Letter Editing Key Tests

    [AvaloniaFact(Timeout = 10000)]
    public async Task AltK_ShouldSendCOPY()
    {
        await SetupConnectionAsync();
        var received = await SendVKKeyAndCaptureAsync(75, KeyModifiers.Alt); // VK_K = 75
        Assert.NotNull(received);
        Assert.Equal("\x1b[12_", received);
        _output.WriteLine($"Alt+K = KOPI: PASS");
    }

    [AvaloniaFact(Timeout = 10000)]
    public async Task AltV_ShouldSendMOVE()
    {
        await SetupConnectionAsync();
        var received = await SendVKKeyAndCaptureAsync(86, KeyModifiers.Alt); // VK_V = 86
        Assert.NotNull(received);
        Assert.Equal("\x1b[14_", received);
        _output.WriteLine($"Alt+V = FLYTT: PASS");
    }

    [AvaloniaFact(Timeout = 10000)]
    public async Task AltJ_ShouldSendJUST()
    {
        await SetupConnectionAsync();
        var received = await SendVKKeyAndCaptureAsync(74, KeyModifiers.Alt); // VK_J = 74
        Assert.NotNull(received);
        Assert.Equal("\x1b[24_", received);
        _output.WriteLine($"Alt+J = JUST: PASS");
    }

    [AvaloniaFact(Timeout = 10000)]
    public async Task AltA_ShouldSendMARK()
    {
        await SetupConnectionAsync();
        var received = await SendVKKeyAndCaptureAsync(65, KeyModifiers.Alt); // VK_A = 65
        Assert.NotNull(received);
        Assert.Equal("\x1b[00_", received);
        _output.WriteLine($"Alt+A = MERK: PASS");
    }

    [AvaloniaFact(Timeout = 10000)]
    public async Task AltL_ShouldSendFIELD()
    {
        await SetupConnectionAsync();
        var received = await SendVKKeyAndCaptureAsync(76, KeyModifiers.Alt); // VK_L = 76
        Assert.NotNull(received);
        Assert.Equal("\x1b[02_", received);
        _output.WriteLine($"Alt+L = FELT: PASS");
    }

    [AvaloniaFact(Timeout = 10000)]
    public async Task AltR_ShouldSendPARA()
    {
        await SetupConnectionAsync();
        var received = await SendVKKeyAndCaptureAsync(82, KeyModifiers.Alt); // VK_R = 82
        Assert.NotNull(received);
        Assert.Equal("\x1b[04_", received);
        _output.WriteLine($"Alt+R = AVSN: PASS");
    }

    [AvaloniaFact(Timeout = 10000)]
    public async Task AltE_ShouldSendSENT()
    {
        await SetupConnectionAsync();
        var received = await SendVKKeyAndCaptureAsync(69, KeyModifiers.Alt); // VK_E = 69
        Assert.NotNull(received);
        Assert.Equal("\x1b[06_", received);
        _output.WriteLine($"Alt+E = SETN: PASS");
    }

    [AvaloniaFact(Timeout = 10000)]
    public async Task AltW_ShouldSendWORD()
    {
        await SetupConnectionAsync();
        var received = await SendVKKeyAndCaptureAsync(87, KeyModifiers.Alt); // VK_W = 87
        Assert.NotNull(received);
        Assert.Equal("\x1b[08_", received);
        _output.WriteLine($"Alt+W = ORD: PASS");
    }

    [AvaloniaFact(Timeout = 10000)]
    public async Task AltI_ShouldSendINSERT_HERE()
    {
        await SetupConnectionAsync();
        var received = await SendVKKeyAndCaptureAsync(73, KeyModifiers.Alt); // VK_I = 73
        Assert.NotNull(received);
        Assert.Equal("\x1b[26_", received);
        _output.WriteLine($"Alt+I = SINGLEGUILLEMETS: PASS");
    }

    #endregion

    #region Alt+Number PUSH Key Tests

    [AvaloniaFact(Timeout = 10000)]
    public async Task Alt1_ShouldSendPUSH1()
    {
        TDVPushKeyConfiguration.ResetForTesting();
        TDVPushKeyConfiguration.Instance.ProgramKey(1, "push1");
        await SetupConnectionAsync();
        var received = await SendVKKeyAndCaptureAsync(49, KeyModifiers.Alt); // VK_1 = 49
        Assert.NotNull(received);
        Assert.Equal("push1", received);
        _output.WriteLine($"Alt+1 = PUSH1: PASS");
    }

    [AvaloniaFact(Timeout = 10000)]
    public async Task Alt2_ShouldSendPUSH2()
    {
        TDVPushKeyConfiguration.ResetForTesting();
        TDVPushKeyConfiguration.Instance.ProgramKey(2, "push2");
        await SetupConnectionAsync();
        var received = await SendVKKeyAndCaptureAsync(50, KeyModifiers.Alt); // VK_2 = 50
        Assert.NotNull(received);
        Assert.Equal("push2", received);
        _output.WriteLine($"Alt+2 = PUSH2: PASS");
    }

    [AvaloniaFact(Timeout = 10000)]
    public async Task Alt3_ShouldSendPUSH3()
    {
        TDVPushKeyConfiguration.ResetForTesting();
        TDVPushKeyConfiguration.Instance.ProgramKey(3, "push3");
        await SetupConnectionAsync();
        var received = await SendVKKeyAndCaptureAsync(51, KeyModifiers.Alt); // VK_3 = 51
        Assert.NotNull(received);
        Assert.Equal("push3", received);
        _output.WriteLine($"Alt+3 = PUSH3: PASS");
    }

    [AvaloniaFact(Timeout = 10000)]
    public async Task Alt4_ShouldSendPUSH4()
    {
        TDVPushKeyConfiguration.ResetForTesting();
        TDVPushKeyConfiguration.Instance.ProgramKey(4, "push4");
        await SetupConnectionAsync();
        var received = await SendVKKeyAndCaptureAsync(52, KeyModifiers.Alt); // VK_4 = 52
        Assert.NotNull(received);
        Assert.Equal("push4", received);
        _output.WriteLine($"Alt+4 = PUSH4: PASS");
    }

    [AvaloniaFact(Timeout = 10000)]
    public async Task Alt5_ShouldSendPUSH5()
    {
        TDVPushKeyConfiguration.ResetForTesting();
        TDVPushKeyConfiguration.Instance.ProgramKey(5, "push5");
        await SetupConnectionAsync();
        var received = await SendVKKeyAndCaptureAsync(53, KeyModifiers.Alt); // VK_5 = 53
        Assert.NotNull(received);
        Assert.Equal("push5", received);
        _output.WriteLine($"Alt+5 = PUSH5: PASS");
    }

    [AvaloniaFact(Timeout = 10000)]
    public async Task Alt6_ShouldSendPUSH6()
    {
        TDVPushKeyConfiguration.ResetForTesting();
        TDVPushKeyConfiguration.Instance.ProgramKey(6, "push6");
        await SetupConnectionAsync();
        var received = await SendVKKeyAndCaptureAsync(54, KeyModifiers.Alt); // VK_6 = 54
        Assert.NotNull(received);
        Assert.Equal("push6", received);
        _output.WriteLine($"Alt+6 = PUSH6: PASS");
    }

    [AvaloniaFact(Timeout = 10000)]
    public async Task Alt7_ShouldSendPUSH7()
    {
        TDVPushKeyConfiguration.ResetForTesting();
        TDVPushKeyConfiguration.Instance.ProgramKey(7, "push7");
        await SetupConnectionAsync();
        var received = await SendVKKeyAndCaptureAsync(55, KeyModifiers.Alt); // VK_7 = 55
        Assert.NotNull(received);
        Assert.Equal("push7", received);
        _output.WriteLine($"Alt+7 = PUSH7: PASS");
    }

    [AvaloniaFact(Timeout = 10000)]
    public async Task Alt8_ShouldSendPUSH8()
    {
        TDVPushKeyConfiguration.ResetForTesting();
        TDVPushKeyConfiguration.Instance.ProgramKey(8, "push8");
        await SetupConnectionAsync();
        var received = await SendVKKeyAndCaptureAsync(56, KeyModifiers.Alt); // VK_8 = 56
        Assert.NotNull(received);
        Assert.Equal("push8", received);
        _output.WriteLine($"Alt+8 = PUSH8: PASS");
    }

    #endregion

    #region Alt+Navigation Key Tests

    [AvaloniaFact(Timeout = 10000)]
    public async Task AltDelete_ShouldSendREMOVE()
    {
        await SetupConnectionAsync();
        var received = await SendVKKeyAndCaptureAsync(46, KeyModifiers.Alt); // VK_DELETE = 46
        Assert.NotNull(received);
        Assert.Equal("\x1b[10_", received);
        _output.WriteLine($"Alt+Delete = STRYK: PASS");
    }

    // keyboard-spec.md section 6.3: RollUp (D47, ESC[28_) is Page Up and RollDown (D49, ESC[32_)
    // is Page Down. These two pinned the reverse until 27 September 2026.
    [AvaloniaFact(Timeout = 10000)]
    public async Task AltPageUp_ShouldSendRollUp()
    {
        await SetupConnectionAsync();
        var received = await SendVKKeyAndCaptureAsync(33, KeyModifiers.Alt); // VK_PRIOR = 33
        Assert.NotNull(received);
        Assert.Equal("\x1b[28_", received);
        _output.WriteLine($"Alt+PageUp = ROLLUP: PASS");
    }

    [AvaloniaFact(Timeout = 10000)]
    public async Task AltPageDown_ShouldSendRollDown()
    {
        await SetupConnectionAsync();
        var received = await SendVKKeyAndCaptureAsync(34, KeyModifiers.Alt); // VK_NEXT = 34
        Assert.NotNull(received);
        Assert.Equal("\x1b[32_", received);
        _output.WriteLine($"Alt+PageDown = ROLLDN: PASS");
    }

    #endregion

    #region All Alt+Key Combinations Test

    [AvaloniaFact(Timeout = 10000)]
    public async Task AllAltLetterKeys_ShouldMapCorrectly()
    {
        await SetupConnectionAsync();

        var testCases = new (int vkCode, string letter, string expected, string name)[]
        {
            (72, "H", "\x1b[46_", "HJELP"),
            (68, "D", "\x1b[20_", "REPLACE"),
            (85, "U", "\x1b[42_", "FUNK"),
            (80, "P", "\x1b[44_", "SKRIV"),
            (88, "X", "\x1b[22_", "GUILLEMETS"),
            (8, "Backspace", "\x1b[30_", "ANGRE"),
            (77, "M", "\x1b[84_", "MODE"),
            (70, "F", "\x1b[18_", "SEARCH"),
            (83, "S", "\x1b[48_", "SLUTT"),
            (75, "K", "\x1b[12_", "KOPI"),
            (86, "V", "\x1b[14_", "FLYTT"),
            (74, "J", "\x1b[24_", "JUST"),
            (65, "A", "\x1b[00_", "MERK"),
            (76, "L", "\x1b[02_", "FELT"),
            (82, "R", "\x1b[04_", "AVSN"),
            (69, "E", "\x1b[06_", "SETN"),
            (87, "W", "\x1b[08_", "ORD"),
            (73, "I", "\x1b[26_", "SINGLEGUILLEMETS"),
        };

        int passed = 0;
        int failed = 0;

        for (int i = 0; i < testCases.Length; i++)
        {
            var (vkCode, letter, expected, name) = testCases[i];
            var received = await SendVKKeyAndCaptureAsync(vkCode, KeyModifiers.Alt);

            if (received == expected)
            {
                _output.WriteLine($"  Alt+{letter} = {name}: PASS");
                passed++;
            }
            else
            {
                _output.WriteLine($"  Alt+{letter} = {name}: FAIL (expected {TDVResponseValidator.ToVisibleString(expected)}, got {TDVResponseValidator.ToVisibleString(received ?? "null")})");
                failed++;
            }
        }

        _output.WriteLine($"\nResults: {passed} passed, {failed} failed");
        Assert.Equal(0, failed);
    }

    [AvaloniaFact(Timeout = 10000)]
    public async Task AllAltNumberKeys_ShouldMapToPUSH()
    {
        await SetupConnectionAsync();

        // Program all PUSH keys before testing
        TDVPushKeyConfiguration.ResetForTesting();
        for (int num = 1; num <= 8; num++)
        {
            TDVPushKeyConfiguration.Instance.ProgramKey(num, $"push{num}");
        }

        int passed = 0;
        int failed = 0;

        for (int num = 1; num <= 8; num++)
        {
            int vkCode = 48 + num; // VK_1 = 49, VK_2 = 50, etc.
            var expected = $"push{num}";
            var received = await SendVKKeyAndCaptureAsync(vkCode, KeyModifiers.Alt);

            if (received == expected)
            {
                _output.WriteLine($"  Alt+{num} = PUSH{num}: PASS");
                passed++;
            }
            else
            {
                _output.WriteLine($"  Alt+{num} = PUSH{num}: FAIL (expected {TDVResponseValidator.ToVisibleString(expected)}, got {TDVResponseValidator.ToVisibleString(received ?? "null")})");
                failed++;
            }
        }

        _output.WriteLine($"\nResults: {passed} passed, {failed} failed");
        Assert.Equal(0, failed);
    }

    #endregion
}
