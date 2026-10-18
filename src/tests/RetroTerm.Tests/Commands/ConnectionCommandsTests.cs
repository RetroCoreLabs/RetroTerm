using System;
using System.IO;
using System.Threading.Tasks;
using RetroTerm.Core.Commands;
using RetroTerm.Core.Commands.Builtin;
using RetroTerm.Core.Configuration;
using RetroTerm.Core.Session;
using RetroTerm.Core.Terminal.Emulators;
using RetroTerm.Tests.TDV;
using Xunit;

namespace RetroTerm.Tests.Commands;

/// <summary>
/// Tests for the connection commands: ad-hoc CONNECT, CONNECT by stored name,
/// DISCONNECT, and stored-connection CRUD (CONNLIST/CONNSHOW/CONNSAVE/CONNDEL).
/// Connections are injected in-memory; the config store uses a temp file.
/// </summary>
public class ConnectionCommandsTests : IDisposable
{
    private readonly string _configPath;
    private readonly ConfigurationManager _manager;
    private readonly CommandRegistry _registry;
    private readonly TerminalSession _session;
    private InMemoryConnection? _lastConnection;
    private RetroTerm.Core.Protocols.ConnectionFactory.ConnectionParameters? _lastParameters;

    public ConnectionCommandsTests()
    {
        _configPath = Path.Combine(Path.GetTempPath(), "RetroTermConnTests-" + Guid.NewGuid().ToString("N") + ".json");
        _manager = new ConfigurationManager(_configPath);

        _registry = new CommandRegistry();
        ConnectionCommands.RegisterAll(_registry, _manager, p =>
        {
            _lastParameters = p;
            _lastConnection = new InMemoryConnection();
            return _lastConnection;
        });

        _session = new TerminalSession(new VT100Emulator(80, 24), "ConnTest");
    }

    public void Dispose()
    {
        _session.Dispose();
        if (File.Exists(_configPath))
        {
            File.Delete(_configPath);
        }
    }

    [Fact]
    public async Task Connect_AdHoc_ConnectsTheSession()
    {
        var result = await _registry.ExecuteAsync("CONNECT", _session,
            new CommandArgs().Set("host", "10.0.0.1").Set("port", "5001"), TestContext.Current.CancellationToken);

        Assert.True(result.Success, result.Error);
        Assert.True(_session.IsConnected);
        Assert.Contains("10.0.0.1:5001", result.Output);
    }

    [Fact]
    public async Task Connect_WhenAlreadyConnected_FailsWithGuidance()
    {
        await _registry.ExecuteAsync("CONNECT", _session, new CommandArgs().Set("host", "a"), TestContext.Current.CancellationToken);
        var second = await _registry.ExecuteAsync("CONNECT", _session, new CommandArgs().Set("host", "b"), TestContext.Current.CancellationToken);

        Assert.False(second.Success);
        Assert.Contains("DISCONNECT first", second.Error);
    }

    [Fact]
    public async Task Connect_WithoutNameOrHost_Fails()
    {
        var result = await _registry.ExecuteAsync("CONNECT", _session, CommandArgs.Empty, TestContext.Current.CancellationToken);
        Assert.False(result.Success);
        Assert.Contains("host", result.Error);
    }

    [Fact]
    public async Task Disconnect_ThenReconnect_Works()
    {
        await _registry.ExecuteAsync("CONNECT", _session, new CommandArgs().Set("host", "a"), TestContext.Current.CancellationToken);
        var disconnect = await _registry.ExecuteAsync("DISCONNECT", _session, CommandArgs.Empty, TestContext.Current.CancellationToken);

        Assert.True(disconnect.Success);
        Assert.False(_session.IsConnected);

        var reconnect = await _registry.ExecuteAsync("CONNECT", _session, new CommandArgs().Set("host", "b"), TestContext.Current.CancellationToken);
        Assert.True(reconnect.Success, reconnect.Error);
        Assert.True(_session.IsConnected);
    }

    [Fact]
    public async Task ConnSave_ConnList_ConnShow_ConnDel_RoundTrip()
    {
        // Create
        var save = await _registry.ExecuteAsync("CONNSAVE", _session,
            new CommandArgs().Set("name", "ND-lab").Set("host", "192.168.1.10").Set("port", "5001")
                .Set("emulator", "TDV2200").Set("favorite", "true"), TestContext.Current.CancellationToken);
        Assert.True(save.Success, save.Error);
        Assert.Contains("created", save.Output);

        // List shows it, favorite-marked
        var list = await _registry.ExecuteAsync("CONNLIST", _session, CommandArgs.Empty, TestContext.Current.CancellationToken);
        Assert.Contains("ND-lab", list.Output);
        Assert.Contains("* ", list.Output);

        // Details
        var show = await _registry.ExecuteAsync("CONNSHOW", _session, new CommandArgs().Set("name", "nd-lab"), TestContext.Current.CancellationToken);
        Assert.True(show.Success);
        Assert.Contains("192.168.1.10:5001", show.Output);
        Assert.Contains("TDV2200", show.Output);

        // Update only the port — host must survive
        var update = await _registry.ExecuteAsync("CONNSAVE", _session,
            new CommandArgs().Set("name", "ND-lab").Set("port", "5002"), TestContext.Current.CancellationToken);
        Assert.True(update.Success, update.Error);
        Assert.Contains("updated", update.Output);

        var showAfter = await _registry.ExecuteAsync("CONNSHOW", _session, new CommandArgs().Set("name", "ND-lab"), TestContext.Current.CancellationToken);
        Assert.Contains("192.168.1.10:5002", showAfter.Output);

        // Delete
        var del = await _registry.ExecuteAsync("CONNDEL", _session, new CommandArgs().Set("name", "ND-lab"), TestContext.Current.CancellationToken);
        Assert.True(del.Success);

        var listAfter = await _registry.ExecuteAsync("CONNLIST", _session, CommandArgs.Empty, TestContext.Current.CancellationToken);
        Assert.Contains("no stored connections", listAfter.Output);
    }

    [Fact]
    public async Task ConnSave_CreateWithoutHost_Fails()
    {
        var result = await _registry.ExecuteAsync("CONNSAVE", _session, new CommandArgs().Set("name", "x"), TestContext.Current.CancellationToken);
        Assert.False(result.Success);
        Assert.Contains("host", result.Error);
    }

    [Fact]
    public async Task Connect_ByStoredName_UsesItsParameters()
    {
        await _registry.ExecuteAsync("CONNSAVE", _session,
            new CommandArgs().Set("name", "lab").Set("host", "172.16.0.5").Set("port", "2323"), TestContext.Current.CancellationToken);

        var result = await _registry.ExecuteAsync("CONNECT", _session, new CommandArgs().Set("name", "lab"), TestContext.Current.CancellationToken);

        Assert.True(result.Success, result.Error);
        Assert.True(_session.IsConnected);
        Assert.Contains("172.16.0.5:2323", result.Output);
    }

    [Fact]
    public async Task Connect_ByUnknownName_FailsPointingToConnList()
    {
        var result = await _registry.ExecuteAsync("CONNECT", _session, new CommandArgs().Set("name", "nope"), TestContext.Current.CancellationToken);
        Assert.False(result.Success);
        Assert.Contains("CONNLIST", result.Error);
    }

    [Fact]
    public async Task ConnDel_UnknownName_Fails()
    {
        var result = await _registry.ExecuteAsync("CONNDEL", _session, new CommandArgs().Set("name", "nope"), TestContext.Current.CancellationToken);
        Assert.False(result.Success);
    }

    // ─────────────────────────────────────────────────────────────
    // Serial — one command layer serves the DSL and MCP, so these
    // cover terminal_connect / terminal_connsave at the same time.
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Connect_AdHocSerial_PassesPortAndFraming()
    {
        var result = await _registry.ExecuteAsync("CONNECT", _session,
            new CommandArgs().Set("protocol", "serial").Set("port_name", "COM11")
                .Set("baud", "115200").Set("data_bits", "7").Set("parity", "even").Set("stop_bits", "1"), TestContext.Current.CancellationToken);

        Assert.True(result.Success, result.Error);
        Assert.True(_session.IsConnected);
        var p = _lastParameters!;
        Assert.Equal(RetroTerm.Core.Protocols.ConnectionFactory.ProtocolType.Serial, p.Protocol);
        Assert.Equal("COM11", p.PortName);
        Assert.Equal(115200, p.BaudRate);
        Assert.Equal(7, p.DataBits);
        Assert.Equal(2, p.ParityValue);   // even
        Assert.Equal(1, p.StopBitsValue); // one
    }

    [Fact]
    public async Task Connect_Serial_WithoutPortName_ErrorNamesTheField()
    {
        var result = await _registry.ExecuteAsync("CONNECT", _session,
            new CommandArgs().Set("protocol", "serial"), TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Contains("port_name", result.Error);
        Assert.False(_session.IsConnected);
    }

    [Fact]
    public async Task Connect_Serial_WithHostPort_IsRefusedNotReinterpreted()
    {
        var result = await _registry.ExecuteAsync("CONNECT", _session,
            new CommandArgs().Set("protocol", "serial").Set("host", "COM11").Set("port", "115200"), TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Contains("port_name", result.Error);
    }

    [Fact]
    public async Task Connect_Serial_BadParity_ErrorListsTheChoices()
    {
        var result = await _registry.ExecuteAsync("CONNECT", _session,
            new CommandArgs().Set("protocol", "serial").Set("port_name", "COM11").Set("parity", "sideways"), TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Contains("odd", result.Error);
        Assert.Contains("even", result.Error);
    }

    [Fact]
    public async Task ConnSave_Serial_RoundTrip_KeepsEveryField()
    {
        // Create — the ND-120 console shape: COM11 at 115200 7E1.
        var save = await _registry.ExecuteAsync("CONNSAVE", _session,
            new CommandArgs().Set("name", "nexys-115200").Set("protocol", "Serial")
                .Set("port_name", "COM11").Set("baud", "115200")
                .Set("data_bits", "7").Set("parity", "even").Set("stop_bits", "1"), TestContext.Current.CancellationToken);
        Assert.True(save.Success, save.Error);
        Assert.Contains("COM11 @ 115200bps", save.Output);

        // Show — port, speed and framing all readable back.
        var show = await _registry.ExecuteAsync("CONNSHOW", _session,
            new CommandArgs().Set("name", "nexys-115200"), TestContext.Current.CancellationToken);
        Assert.True(show.Success);
        Assert.Contains("COM11 @ 115200bps", show.Output);
        Assert.Contains("framing: 7E1", show.Output);

        // Update ONLY the baud — everything else must survive.
        var update = await _registry.ExecuteAsync("CONNSAVE", _session,
            new CommandArgs().Set("name", "nexys-115200").Set("baud", "9600"), TestContext.Current.CancellationToken);
        Assert.True(update.Success, update.Error);
        Assert.Contains("COM11 @ 9600bps", update.Output);

        var showAfter = await _registry.ExecuteAsync("CONNSHOW", _session,
            new CommandArgs().Set("name", "nexys-115200"), TestContext.Current.CancellationToken);
        Assert.Contains("framing: 7E1", showAfter.Output);

        // Connect by the stored name carries the serial parameters through.
        var connect = await _registry.ExecuteAsync("CONNECT", _session,
            new CommandArgs().Set("name", "nexys-115200"), TestContext.Current.CancellationToken);
        Assert.True(connect.Success, connect.Error);
        var p = _lastParameters!;
        Assert.Equal("COM11", p.PortName);
        Assert.Equal(9600, p.BaudRate);
        Assert.Equal(7, p.DataBits);
        Assert.Equal(2, p.ParityValue);
        Assert.Equal(1, p.StopBitsValue);
    }

    [Fact]
    public async Task ConnSave_Serial_WithHostPort_IsErrorNotSilentSuccess()
    {
        // The reported bug, verbatim: connsave name=nexys-115200 host=COM11 port=115200
        // protocol=Serial returned success as "? @ 9600bps" with nothing stored usable.
        var result = await _registry.ExecuteAsync("CONNSAVE", _session,
            new CommandArgs().Set("name", "nexys-115200").Set("protocol", "Serial")
                .Set("host", "COM11").Set("port", "115200"), TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Contains("port_name", result.Error);
        Assert.DoesNotContain("?", result.Output ?? "");
    }

    [Fact]
    public async Task ConnSave_SerialFields_OnTelnet_IsError()
    {
        var result = await _registry.ExecuteAsync("CONNSAVE", _session,
            new CommandArgs().Set("name", "lab").Set("host", "10.0.0.1").Set("port_name", "COM3"), TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Contains("protocol=Serial", result.Error);
    }

    [Fact]
    public async Task ConnSave_CreateSerial_WithoutPortName_Fails()
    {
        var result = await _registry.ExecuteAsync("CONNSAVE", _session,
            new CommandArgs().Set("name", "nx").Set("protocol", "Serial"), TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Contains("port_name", result.Error);
    }
}
