using System.Threading.Tasks;
using RetroTerm.Core.Commands;
using RetroTerm.Core.Commands.Builtin;
using RetroTerm.Core.Session;
using RetroTerm.Core.Terminal.Emulators;
using RetroTerm.Core.Terminal.Input;
using Xunit;

namespace RetroTerm.Tests.Commands;

/// <summary>
/// KEYBIND — the MCP/script gateway to the TDV keyboard bindings. Shares the
/// TDVKeyBindingConfiguration singleton, so the collection serializes these with the
/// other binding tests. Every action here passes save=false to avoid touching disk.
/// </summary>
[Collection("TDVKeyBinding")]
public class KeyboardCommandTests
{
    private readonly CommandRegistry _registry;
    private readonly TerminalSession _session;

    public KeyboardCommandTests()
    {
        TDVKeyBindingConfiguration.ResetForTesting();
        _registry = new CommandRegistry();
        BuiltinCommands.RegisterAll(_registry);
        _session = new TerminalSession(new VT100Emulator(80, 24), "KeybindCmdTest");
    }

    [Fact]
    public void Keybind_IsRegistered_WithHelp()
    {
        Assert.True(_registry.TryGet("KEYBIND", out _));
        var help = CommandHelpGenerator.GenerateFor(_registry, "KEYBIND");
        Assert.Contains("grid", help);
    }

    [Fact]
    public async Task Bind_Then_List_ShowsTheBinding()
    {
        // Alt+H (vk 72, alt) -> G53 (HJELP)
        var bind = await _registry.ExecuteAsync("KEYBIND", _session,
            new CommandArgs().Set("action", "bind").Set("vk", "72").Set("mods", "alt")
                .Set("grid", "G53").Set("save", "false"), TestContext.Current.CancellationToken);
        Assert.True(bind.Success, bind.Error);

        // The live config really holds it.
        Assert.True(TDVKeyBindingConfiguration.Instance.TryGetTarget(72, KeyModifiers.Alt, out var target));
        Assert.Equal("G53", target.GridPosition);

        var list = await _registry.ExecuteAsync("KEYBIND", _session,
            new CommandArgs().Set("action", "list"), TestContext.Current.CancellationToken);
        Assert.Contains("G53", list.Output);
        Assert.Contains("vk=72", list.Output);
    }

    [Fact]
    public async Task Unbind_RemovesTheBinding()
    {
        await _registry.ExecuteAsync("KEYBIND", _session,
            new CommandArgs().Set("action", "bind").Set("vk", "112").Set("grid", "G53").Set("save", "false"), TestContext.Current.CancellationToken);
        Assert.True(TDVKeyBindingConfiguration.Instance.TryGetTarget(112, KeyModifiers.None, out _));

        var unbind = await _registry.ExecuteAsync("KEYBIND", _session,
            new CommandArgs().Set("action", "unbind").Set("vk", "112").Set("save", "false"), TestContext.Current.CancellationToken);
        Assert.True(unbind.Success, unbind.Error);
        Assert.False(TDVKeyBindingConfiguration.Instance.TryGetTarget(112, KeyModifiers.None, out _));
    }

    [Fact]
    public async Task Bind_InvalidSource_IsError()
    {
        // A bare letter with no modifier is not a valid binding source.
        var result = await _registry.ExecuteAsync("KEYBIND", _session,
            new CommandArgs().Set("action", "bind").Set("vk", "72").Set("grid", "G53").Set("save", "false"), TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Contains("not a valid binding source", result.Error);
    }

    [Fact]
    public async Task Bind_UnknownModifier_IsError()
    {
        var result = await _registry.ExecuteAsync("KEYBIND", _session,
            new CommandArgs().Set("action", "bind").Set("vk", "72").Set("mods", "hyper")
                .Set("grid", "G53").Set("save", "false"), TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Contains("unknown modifier", result.Error);
    }

    [Fact]
    public async Task Unbind_NothingBound_IsError()
    {
        var result = await _registry.ExecuteAsync("KEYBIND", _session,
            new CommandArgs().Set("action", "unbind").Set("vk", "121").Set("mods", "ctrl").Set("save", "false"), TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Contains("nothing bound", result.Error);
    }

    [Fact]
    public async Task UnknownAction_IsError()
    {
        var result = await _registry.ExecuteAsync("KEYBIND", _session,
            new CommandArgs().Set("action", "wobble"), TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Contains("unknown action", result.Error);
    }
}
