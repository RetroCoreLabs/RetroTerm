using System.Threading.Tasks;
using RetroTerm.Core.Commands;
using RetroTerm.Core.Commands.Builtin;
using RetroTerm.Core.Session;
using RetroTerm.Core.Terminal.Emulators;
using RetroTerm.Tests.TDV;
using Xunit;

namespace RetroTerm.Tests.Commands;

/// <summary>
/// OPCOM debug command — the MCP/script gateway to the OPCOM Debug view. These cover
/// the argument/plumbing contract (registration, connection requirement, octal
/// validation, local status/passthrough). Full ND round-trips are covered by the
/// OpcomProtocol tests; here we prove the command reaches the protocol correctly.
/// </summary>
public class OpcomDebugCommandTests
{
    private readonly CommandRegistry _registry;
    private readonly TerminalSession _session;

    public OpcomDebugCommandTests()
    {
        _registry = new CommandRegistry();
        BuiltinCommands.RegisterAll(_registry);
        _session = new TerminalSession(new VT100Emulator(80, 24), "OpcomCmdTest");
    }

    [Fact]
    public void Opcom_IsRegistered_WithHelp()
    {
        Assert.True(_registry.TryGet("OPCOM", out _));
        var help = CommandHelpGenerator.GenerateFor(_registry, "OPCOM");
        Assert.Contains("readreg", help);
        Assert.Contains("OCTAL", help.ToUpperInvariant());
    }

    [Fact]
    public async Task Opcom_NotConnected_IsError()
    {
        var result = await _registry.ExecuteAsync("OPCOM", _session,
            new CommandArgs().Set("action", "stop"));

        Assert.False(result.Success);
        Assert.Contains("not connected", result.Error);
    }

    [Fact]
    public async Task Opcom_Status_AfterConnect_AttachesHandler()
    {
        await _session.ConnectAsync(new InMemoryConnection());

        var result = await _registry.ExecuteAsync("OPCOM", _session,
            new CommandArgs().Set("action", "status"));

        Assert.True(result.Success);
        Assert.Contains("opcom active:", result.Output);
        Assert.NotNull(_session.ActiveOpcomHandler); // command attached the protocol
    }

    [Fact]
    public async Task Opcom_Passthrough_TogglesTheFlag()
    {
        await _session.ConnectAsync(new InMemoryConnection());

        var on = await _registry.ExecuteAsync("OPCOM", _session,
            new CommandArgs().Set("action", "passthrough").Set("pass", "true"));
        Assert.Contains("on", on.Output);

        var off = await _registry.ExecuteAsync("OPCOM", _session,
            new CommandArgs().Set("action", "passthrough").Set("pass", "false"));
        Assert.Contains("off", off.Output);
    }

    [Fact]
    public async Task Opcom_UnknownAction_IsError()
    {
        await _session.ConnectAsync(new InMemoryConnection());

        var result = await _registry.ExecuteAsync("OPCOM", _session,
            new CommandArgs().Set("action", "frobnicate"));

        Assert.False(result.Success);
        Assert.Contains("unknown action", result.Error);
    }

    [Fact]
    public async Task Opcom_ReadMem_BadOctalAddress_IsRejectedBeforeAnyRoundTrip()
    {
        await _session.ConnectAsync(new InMemoryConnection());

        // '9' is not an octal digit — must fail fast with a clear message, not hang
        // waiting for an ND response.
        var result = await _registry.ExecuteAsync("OPCOM", _session,
            new CommandArgs().Set("action", "readmem").Set("addr", "999").Set("timeout", "500"));

        Assert.False(result.Success);
        Assert.Contains("octal", result.Error);
    }

    [Fact]
    public async Task Opcom_ReadReg_MissingReg_IsError()
    {
        await _session.ConnectAsync(new InMemoryConnection());

        var result = await _registry.ExecuteAsync("OPCOM", _session,
            new CommandArgs().Set("action", "readreg").Set("level", "0").Set("timeout", "500"));

        Assert.False(result.Success);
        Assert.Contains("'reg='", result.Error);
    }
}
