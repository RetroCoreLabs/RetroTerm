using System.Threading.Tasks;
using RetroTerm.Core.Commands;
using RetroTerm.Core.Protocols.WebSocket.Gateway;
using RetroTerm.Core.Session;
using RetroTerm.Core.Terminal.Emulators;
using Xunit;

namespace RetroTerm.Tests.Commands;

/// <summary>
/// GATEWAY — the MCP/script window into the ND-100 gateway listener. A real
/// (unstarted) GatewayListener reports the not-listening/no-terminals state, which
/// is exactly what the command must format; the null-provider path is the
/// "gateway not available" case.
/// </summary>
public class GatewayCommandTests
{
    private readonly TerminalSession _session;

    public GatewayCommandTests()
    {
        _session = new TerminalSession(new VT100Emulator(80, 24), "GatewayCmdTest");
    }

    private static CommandRegistry MakeRegistry(GatewayListener? listener)
    {
        var registry = new CommandRegistry();
        GatewayCommand.RegisterAll(registry, () => listener);
        return registry;
    }

    [Fact]
    public void Gateway_IsRegistered_WithHelp()
    {
        var registry = MakeRegistry(new GatewayListener());
        Assert.True(registry.TryGet("GATEWAY", out _));
        var help = CommandHelpGenerator.GenerateFor(registry, "GATEWAY");
        Assert.Contains("terminals", help);
    }

    [Fact]
    public async Task Gateway_NoListener_IsError()
    {
        var registry = MakeRegistry(null);

        var result = await registry.ExecuteAsync("GATEWAY", _session, CommandArgs.Empty);

        Assert.False(result.Success);
        Assert.Contains("not available", result.Error);
    }

    [Fact]
    public async Task Gateway_Status_ReportsListenerState()
    {
        using var listener = new GatewayListener(); // not started
        var registry = MakeRegistry(listener);

        var result = await registry.ExecuteAsync("GATEWAY", _session, CommandArgs.Empty);

        Assert.True(result.Success);
        Assert.Contains("listening: no", result.Output);
        Assert.Contains("emulator connected: no", result.Output);
        Assert.Contains("terminals: 0", result.Output);
        Assert.Equal("0", result.CaptureValue); // into=var gets the terminal count
    }

    [Fact]
    public async Task Gateway_Terminals_WhenEmpty_SaysSo()
    {
        using var listener = new GatewayListener();
        var registry = MakeRegistry(listener);

        var result = await registry.ExecuteAsync("GATEWAY", _session,
            new CommandArgs().Set("action", "terminals"));

        Assert.True(result.Success);
        Assert.Contains("no terminals registered", result.Output);
    }
}
