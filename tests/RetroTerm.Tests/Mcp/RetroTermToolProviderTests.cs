using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Protocol;
using RetroTerm.Core.Commands;
using RetroTerm.Core.Commands.Builtin;
using RetroTerm.Core.Configuration;
using RetroTerm.Core.Protocols;
using RetroTerm.Core.Scripting;
using RetroTerm.Core.Session;
using RetroTerm.Mcp;
using RetroTerm.Tests.TDV;
using Xunit;

namespace RetroTerm.Tests.Mcp;

/// <summary>
/// Tests for the MCP tool provider (P4.3/P4.4) over a headless session host with
/// in-memory connections — the full open → send → wait → read → close flow an LLM
/// client drives, minus the HTTP transport.
/// </summary>
public class RetroTermToolProviderTests : IDisposable
{
    private readonly InProcessSessionHost _host;
    private readonly RetroTermToolProvider _provider;
    private readonly string _tempScripts;
    private readonly string _tempConfig;
    private readonly ConfigurationManager _configManager;
    private readonly List<InMemoryConnection> _connections = new();
    private readonly List<ConnectionFactory.ConnectionParameters> _openedWith = new();

    public RetroTermToolProviderTests()
    {
        // Every opened session gets an InMemoryConnection we can drive from the test,
        // and the parameters terminal_open resolved to are recorded — that is the
        // MCP-handler boundary the serial and open-by-name tests assert on.
        _host = new InProcessSessionHost(p =>
        {
            _openedWith.Add(p);
            var c = new InMemoryConnection();
            _connections.Add(c);
            return c;
        });

        var registry = new CommandRegistry();
        BuiltinCommands.RegisterAll(registry);

        _tempScripts = Path.Combine(Path.GetTempPath(), "RetroTermMcpTests-" + Guid.NewGuid().ToString("N"));
        _tempConfig = Path.Combine(Path.GetTempPath(), "RetroTermMcpTests-" + Guid.NewGuid().ToString("N") + ".json");
        _configManager = new ConfigurationManager(_tempConfig);
        _provider = new RetroTermToolProvider(_host, registry, new ScriptLibrary(_tempScripts), _configManager);
    }

    public void Dispose()
    {
        _host.Dispose();
        if (Directory.Exists(_tempScripts))
        {
            Directory.Delete(_tempScripts, recursive: true);
        }
        if (File.Exists(_tempConfig))
        {
            File.Delete(_tempConfig);
        }
    }

    private static IDictionary<string, JsonElement> Args(params (string Key, object Value)[] pairs)
    {
        var dict = new Dictionary<string, JsonElement>();
        for (int i = 0; i < pairs.Length; i++)
        {
            dict[pairs[i].Key] = JsonSerializer.SerializeToElement(pairs[i].Value);
        }
        return dict;
    }

    private static string Text(CallToolResult result)
    {
        Assert.NotEmpty(result.Content);
        var block = Assert.IsType<TextContentBlock>(result.Content[0]);
        return block.Text;
    }

    private async Task<string> OpenSessionAsync()
    {
        var result = await _provider.CallToolAsync("terminal_open",
            Args(("host", "test-nd"), ("port", 5001)), CancellationToken.None);

        Assert.NotEqual(true, result.IsError);
        var text = Text(result);
        // "sessionId: <guid>" on the first line
        var id = text.Substring(text.IndexOf(':') + 1);
        id = id.Substring(0, id.IndexOf('\n')).Trim();
        return id;
    }

    // ─────────────────────────────────────────────────────────────
    // Tool list
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void ToolList_ContainsSessionToolsAndOneToolPerCommand()
    {
        var tools = _provider.BuildToolList();

        var names = new List<string>();
        for (int i = 0; i < tools.Count; i++)
        {
            names.Add(tools[i].Name);
        }

        Assert.Contains("terminal_open", names);
        Assert.Contains("terminal_close", names);
        Assert.Contains("terminal_list", names);
        Assert.Contains("terminal_run_script", names);
        Assert.Contains("terminal_scripts", names);
        // Generated from the registry:
        Assert.Contains("terminal_send", names);
        Assert.Contains("terminal_sendraw", names);
        Assert.Contains("terminal_waitfor", names);
        Assert.Contains("terminal_waitidle", names);
        Assert.Contains("terminal_readscreen", names);
        Assert.Contains("terminal_status", names);
        Assert.Contains("terminal_help", names);
        // The capability commands must ALSO surface as MCP tools — the whole point
        // of "MCP must have access" to the monitor, OPCOM and the keyboard.
        Assert.Contains("terminal_sendkey", names);
        Assert.Contains("terminal_tracestart", names);
        Assert.Contains("terminal_traceread", names);
        Assert.Contains("terminal_opcom", names);
        Assert.Contains("terminal_keybind", names);
        // GATEWAY is registered by the desktop (it owns the listener), so it is not
        // in the builtin registry this test uses — covered by GatewayCommandTests.
    }

    [Fact]
    public void ToolList_CapabilityTools_HaveWellFormedSchemas()
    {
        var tools = _provider.BuildToolList();

        // Each capability tool's schema must at least carry sessionId and its own
        // required 'action' — a malformed schema would make the tool uncallable.
        string[] withAction = { "terminal_opcom", "terminal_keybind" };
        for (int t = 0; t < withAction.Length; t++)
        {
            Tool? tool = null;
            for (int i = 0; i < tools.Count; i++)
            {
                if (tools[i].Name == withAction[t]) { tool = tools[i]; break; }
            }
            Assert.NotNull(tool);
            var props = tool!.InputSchema.GetProperty("properties");
            Assert.True(props.TryGetProperty("sessionId", out _), $"{withAction[t]} missing sessionId");
            Assert.True(props.TryGetProperty("action", out _), $"{withAction[t]} missing action");
            Assert.Contains("action", tool.InputSchema.GetProperty("required").GetRawText());
        }
    }

    [Fact]
    public void ToolList_CommandTools_CarryGeneratedSchemas()
    {
        var tools = _provider.BuildToolList();

        Tool? waitfor = null;
        for (int i = 0; i < tools.Count; i++)
        {
            if (tools[i].Name == "terminal_waitfor") { waitfor = tools[i]; break; }
        }
        Assert.NotNull(waitfor);

        var schema = waitfor!.InputSchema;
        var properties = schema.GetProperty("properties");
        Assert.True(properties.TryGetProperty("sessionId", out _));
        Assert.True(properties.TryGetProperty("pattern", out _));
        Assert.True(properties.TryGetProperty("timeout", out _));
        // pattern and sessionId are required
        var required = schema.GetProperty("required").GetRawText();
        Assert.Contains("pattern", required);
        Assert.Contains("sessionId", required);
    }

    // ─────────────────────────────────────────────────────────────
    // Session lifetime
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task OpenListClose_RoundTrip()
    {
        var id = await OpenSessionAsync();

        var list = await _provider.CallToolAsync("terminal_list", null, CancellationToken.None);
        Assert.Contains(id, Text(list));
        Assert.Contains("connected", Text(list));

        var close = await _provider.CallToolAsync("terminal_close",
            Args(("sessionId", id)), CancellationToken.None);
        Assert.NotEqual(true, close.IsError);

        var listAfter = await _provider.CallToolAsync("terminal_list", null, CancellationToken.None);
        Assert.Contains("no open sessions", Text(listAfter));
    }

    [Fact]
    public async Task Open_WithoutHost_IsError()
    {
        var result = await _provider.CallToolAsync("terminal_open",
            Args(("port", 23)), CancellationToken.None);

        Assert.Equal(true, result.IsError);
        Assert.Contains("host", Text(result));
    }

    // ─────────────────────────────────────────────────────────────
    // terminal_open by stored name, and ad-hoc serial — the fix for
    // the 30 August 2026 bounce workaround (a throwaway telnet session
    // to a foreign host just to reach a stored serial connection).
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Open_ByStoredName_Telnet_UsesTheStoredParameters()
    {
        await _configManager.AddAsync(new HostConfiguration
        {
            Name = "d100",
            Host = "10.0.0.7",
            Port = 9010,
            Protocol = "Telnet",
            EmulatorType = "TDV2200"
        });

        var result = await _provider.CallToolAsync("terminal_open",
            Args(("name", "d100")), CancellationToken.None);

        Assert.NotEqual(true, result.IsError);
        var p = _openedWith[0];
        Assert.Equal(ConnectionFactory.ProtocolType.Telnet, p.Protocol);
        Assert.Equal("10.0.0.7", p.Host);
        Assert.Equal(9010, p.Port);
        Assert.Equal("TDV2200", p.EmulatorType);
    }

    [Fact]
    public async Task Open_ByStoredName_Serial_UsesTheStoredSerialParameters()
    {
        // The ND-120 console shape: 115200 7E1 on COM11.
        await _configManager.AddAsync(new HostConfiguration
        {
            Name = "nexys-115200",
            Protocol = "Serial",
            PortName = "COM11",
            BaudRate = 115200,
            DataBits = 7,
            ParityValue = 2,     // even
            StopBitsValue = 1,   // one
            EmulatorType = "VT100"
        });

        var result = await _provider.CallToolAsync("terminal_open",
            Args(("name", "nexys-115200")), CancellationToken.None);

        Assert.NotEqual(true, result.IsError);
        Assert.Contains("COM11", Text(result));
        var p = _openedWith[0];
        Assert.Equal(ConnectionFactory.ProtocolType.Serial, p.Protocol);
        Assert.Equal("COM11", p.PortName);
        Assert.Equal(115200, p.BaudRate);
        Assert.Equal(7, p.DataBits);
        Assert.Equal(2, p.ParityValue);
        Assert.Equal(1, p.StopBitsValue);
    }

    [Fact]
    public async Task Open_ByUnknownName_IsErrorPointingToConnList()
    {
        var result = await _provider.CallToolAsync("terminal_open",
            Args(("name", "nope")), CancellationToken.None);

        Assert.Equal(true, result.IsError);
        Assert.Contains("terminal_connlist", Text(result));
    }

    [Fact]
    public async Task Open_AdHocSerial_PassesPortAndFraming()
    {
        var result = await _provider.CallToolAsync("terminal_open",
            Args(("protocol", "serial"), ("port_name", "COM11"), ("baud", 115200),
                 ("data_bits", 7), ("parity", "even"), ("stop_bits", "1")),
            CancellationToken.None);

        Assert.NotEqual(true, result.IsError);
        var p = _openedWith[0];
        Assert.Equal(ConnectionFactory.ProtocolType.Serial, p.Protocol);
        Assert.Equal("COM11", p.PortName);
        Assert.Equal(115200, p.BaudRate);
        Assert.Equal(7, p.DataBits);
        Assert.Equal(2, p.ParityValue);
        Assert.Equal(1, p.StopBitsValue);
    }

    [Fact]
    public async Task Open_AdHocSerial_DefaultsAre8N1At9600()
    {
        var result = await _provider.CallToolAsync("terminal_open",
            Args(("protocol", "serial"), ("port_name", "COM3")), CancellationToken.None);

        Assert.NotEqual(true, result.IsError);
        var p = _openedWith[0];
        Assert.Equal(9600, p.BaudRate);
        Assert.Equal(8, p.DataBits);
        Assert.Equal(0, p.ParityValue);   // none
        Assert.Equal(1, p.StopBitsValue); // one
    }

    [Fact]
    public async Task Open_Serial_WithoutPortName_ErrorNamesTheMcpField()
    {
        // The reported failure: "PortName cannot be empty for serial connections
        // (Parameter 'parameters')" — an internal name an MCP caller cannot act on.
        // The error must name port_name, the field the caller can actually send.
        var result = await _provider.CallToolAsync("terminal_open",
            Args(("protocol", "serial")), CancellationToken.None);

        Assert.Equal(true, result.IsError);
        Assert.Contains("port_name", Text(result));
        Assert.DoesNotContain("Parameter 'parameters'", Text(result));
        Assert.Empty(_openedWith); // nothing was opened
    }

    [Fact]
    public async Task Open_Serial_WithHostPort_IsErrorNotSilentReinterpretation()
    {
        // The 30 August call shape: host=COM11 port=115200 protocol=serial.
        var result = await _provider.CallToolAsync("terminal_open",
            Args(("protocol", "serial"), ("host", "COM11"), ("port", 115200)), CancellationToken.None);

        Assert.Equal(true, result.IsError);
        Assert.Contains("port_name", Text(result));
        Assert.Empty(_openedWith);
    }

    [Fact]
    public async Task UnknownSessionId_IsErrorWithGuidance()
    {
        var result = await _provider.CallToolAsync("terminal_send",
            Args(("sessionId", Guid.NewGuid().ToString()), ("text", "x")), CancellationToken.None);

        Assert.Equal(true, result.IsError);
        Assert.Contains("terminal_list", Text(result));
    }

    // ─────────────────────────────────────────────────────────────
    // Command dispatch through MCP
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Send_WaitFor_ReadScreen_FullFlow()
    {
        var id = await OpenSessionAsync();
        var connection = _connections[0];

        // LLM sends a command — SEND is verbatim, the CR is in the text itself.
        var send = await _provider.CallToolAsync("terminal_send",
            Args(("sessionId", id), ("text", "LIST-FILES\r")), CancellationToken.None);
        Assert.NotEqual(true, send.IsError);
        Assert.Equal("LIST-FILES\r", Encoding.UTF8.GetString(connection.GetSentData()[0]));

        // ...the host answers...
        connection.SimulateReceive(Encoding.UTF8.GetBytes("FILE-1\r\nFILE-2\r\nX-C:"));

        // ...the LLM waits on the rendered screen...
        var wait = await _provider.CallToolAsync("terminal_waitfor",
            Args(("sessionId", id), ("pattern", "X-C:"), ("timeout", 5000)), CancellationToken.None);
        Assert.NotEqual(true, wait.IsError);
        Assert.Contains("took", Text(wait)); // rule 5: wait time reported

        // ...and reads the screen.
        var read = await _provider.CallToolAsync("terminal_readscreen",
            Args(("sessionId", id)), CancellationToken.None);
        Assert.Contains("FILE-1", Text(read));
        Assert.Contains("FILE-2", Text(read));
    }

    [Fact]
    public async Task Send_LiteralBackslashR_DecodesToCarriageReturn()
    {
        // The reported bug: an MCP client, told by the tool docs that text supports
        // "\r", sends the TWO characters backslash+r (JSON "SYSTEM\\r"). Before the fix
        // that reached the wire verbatim; now it must decode to a real CR — exactly what
        // the script surface produces for SEND "SYSTEM\r".
        var id = await OpenSessionAsync();
        var connection = _connections[0];

        // @"SYSTEM\r" is a C# verbatim string: literal backslash then r, no CR.
        var send = await _provider.CallToolAsync("terminal_send",
            Args(("sessionId", id), ("text", @"SYSTEM\r")), CancellationToken.None);

        Assert.NotEqual(true, send.IsError);
        Assert.Equal("SYSTEM\r", Encoding.UTF8.GetString(connection.GetSentData()[0]));
        // Prove the OLD behaviour is gone: no stray backslash on the wire.
        Assert.DoesNotContain('\\', Encoding.UTF8.GetString(connection.GetSentData()[0]));
    }

    [Fact]
    public async Task Send_AllEscapeForms_DecodeOverMcp()
    {
        var id = await OpenSessionAsync();
        var connection = _connections[0];

        // \n \t \e \xHH \NNN and \\ all decode, same table as the script parser.
        var send = await _provider.CallToolAsync("terminal_send",
            Args(("sessionId", id), ("text", @"A\nB\tC\e\x1B\033\\Z")), CancellationToken.None);

        Assert.NotEqual(true, send.IsError);
        // \e and \x1B and \033 are all ESC (0x1B); \\ is one backslash.
        Assert.Equal("A\nB\tC\x1B\x1B\x1B\\Z",
            Encoding.UTF8.GetString(connection.GetSentData()[0]));
    }

    [Fact]
    public async Task Send_BadEscape_IsErrorNotSentToWire()
    {
        var id = await OpenSessionAsync();
        var connection = _connections[0];

        // \q is not a valid escape — the MCP call must fail cleanly, sending nothing.
        var send = await _provider.CallToolAsync("terminal_send",
            Args(("sessionId", id), ("text", @"OOPS\q")), CancellationToken.None);

        Assert.Equal(true, send.IsError);
        Assert.Contains("bad escape", Text(send));
        Assert.Empty(connection.GetSentData()); // nothing hit the wire
    }

    [Fact]
    public async Task Send_DanglingBackslash_IsError()
    {
        var id = await OpenSessionAsync();
        var connection = _connections[0];

        // A lone trailing backslash (JSON "X\\") is ambiguous — reject it, send nothing.
        var send = await _provider.CallToolAsync("terminal_send",
            Args(("sessionId", id), ("text", @"X\")), CancellationToken.None);

        Assert.Equal(true, send.IsError);
        Assert.Contains("bad escape", Text(send));
        Assert.Empty(connection.GetSentData());
    }

    [Fact]
    public async Task WaitFor_Pattern_IsNotEscapeDecoded_RegexBackslashesSurvive()
    {
        // WAITFOR's pattern is deliberately NOT flagged DecodeEscapes: a regex like
        // \d+ must reach the matcher untouched. If the MCP boundary wrongly decoded it,
        // \d would blow up as an unknown escape before the pattern ever ran.
        var id = await OpenSessionAsync();
        _connections[0].SimulateReceive(Encoding.UTF8.GetBytes("value 12345 done"));

        var result = await _provider.CallToolAsync("terminal_waitfor",
            Args(("sessionId", id), ("pattern", @"\d+"), ("regex", true), ("timeout", 5000)),
            CancellationToken.None);

        // It matched the digits — not a "bad escape" error.
        Assert.NotEqual(true, result.IsError);
        Assert.DoesNotContain("bad escape", Text(result));
    }

    [Fact]
    public async Task WaitFor_Timeout_IsErrorButCarriesScreen()
    {
        var id = await OpenSessionAsync();
        _connections[0].SimulateReceive(Encoding.UTF8.GetBytes("partial"));

        var result = await _provider.CallToolAsync("terminal_waitfor",
            Args(("sessionId", id), ("pattern", "NEVER"), ("timeout", 150)), CancellationToken.None);

        Assert.Equal(true, result.IsError);
        Assert.Contains("Timeout", Text(result));
        Assert.Contains("partial", Text(result)); // rule 4
    }

    [Fact]
    public async Task Help_ToolWorks_GeneratedFromRegistry()
    {
        var id = await OpenSessionAsync();

        var result = await _provider.CallToolAsync("terminal_help",
            Args(("sessionId", id), ("command", "WAITFOR")), CancellationToken.None);

        Assert.NotEqual(true, result.IsError);
        Assert.Contains("pattern", Text(result));
        Assert.Contains("Example:", Text(result));
    }

    // ─────────────────────────────────────────────────────────────
    // Scripts over MCP
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task RunScript_Inline_TranscriptWithTimings()
    {
        var id = await OpenSessionAsync();
        var connection = _connections[0];
        connection.SimulateReceive(Encoding.UTF8.GetBytes("READY"));

        var result = await _provider.CallToolAsync("terminal_run_script",
            Args(("sessionId", id),
                 ("script", "WAITFOR \"READY\" timeout=5000\nSEND \"GO\\r\"\n")),
            CancellationToken.None);

        Assert.NotEqual(true, result.IsError);
        var text = Text(result);
        Assert.Contains("SCRIPT OK", text);
        Assert.Contains("2 step(s)", text);
        Assert.Contains("ms]", text); // per-step timing
        // The \r in the DSL string decoded to a real CR — SEND itself appends nothing.
        Assert.Equal("GO\r", connection.GetLastSentAsString());
    }

    [Fact]
    public async Task RunScript_FailingStep_ReportsStepAndScreen_SessionStaysOpen()
    {
        var id = await OpenSessionAsync();
        _connections[0].SimulateReceive(Encoding.UTF8.GetBytes("the machine said this"));

        var result = await _provider.CallToolAsync("terminal_run_script",
            Args(("sessionId", id),
                 ("script", "WAITFOR \"NEVER\" timeout=150\nSEND \"NOT-REACHED\"\n")),
            CancellationToken.None);

        Assert.Equal(true, result.IsError);
        var text = Text(result);
        Assert.Contains("SCRIPT FAILED", text);
        Assert.Contains("line 1", text);
        Assert.Contains("the machine said this", text); // screen at failure

        // Session is still live for interactive poking.
        var status = await _provider.CallToolAsync("terminal_status",
            Args(("sessionId", id)), CancellationToken.None);
        Assert.Contains("connected: yes", Text(status));
    }

    [Fact]
    public async Task Scripts_SaveValidatesAndLists()
    {
        var bad = await _provider.CallToolAsync("terminal_scripts",
            Args(("save", "broken"), ("script", "NOSUCHVERB")), CancellationToken.None);
        Assert.Equal(true, bad.IsError);
        Assert.Contains("not saved", Text(bad));

        var good = await _provider.CallToolAsync("terminal_scripts",
            Args(("save", "login"), ("script", "SENDRAW ESC\nWAITFOR \"ENTER\"\n")), CancellationToken.None);
        Assert.NotEqual(true, good.IsError);

        var list = await _provider.CallToolAsync("terminal_scripts", null, CancellationToken.None);
        Assert.Contains("login", Text(list));
    }

    [Fact]
    public async Task RunScript_ByStoredName()
    {
        await _provider.CallToolAsync("terminal_scripts",
            Args(("save", "greet"), ("script", "SEND \"HELLO\\r\"\n")), CancellationToken.None);

        var id = await OpenSessionAsync();
        var result = await _provider.CallToolAsync("terminal_run_script",
            Args(("sessionId", id), ("name", "greet")), CancellationToken.None);

        Assert.NotEqual(true, result.IsError);
        Assert.Equal("HELLO\r", _connections[0].GetLastSentAsString());
    }
}
