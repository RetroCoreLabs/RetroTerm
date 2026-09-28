using System;
using System.Threading.Tasks;
using RetroTerm.Core.Commands;
using RetroTerm.Core.Commands.Builtin;
using RetroTerm.Core.Session;
using RetroTerm.Core.Terminal.Emulators;
using RetroTerm.Core.Terminal.Emulators.TDV;
using RetroTerm.Tests.TDV;
using Xunit;

namespace RetroTerm.Tests.Commands;

/// <summary>
/// Tests for SENDKEY: named TDV keys resolve through the key registry and send the
/// mode-correct escape sequence.
/// </summary>
public class SendKeyCommandTests : IDisposable
{
    private readonly CommandRegistry _registry;
    private TerminalSession? _session;
    private readonly InMemoryConnection _connection = new();

    public SendKeyCommandTests()
    {
        _registry = new CommandRegistry();
        _registry.Register(new SendKeyCommand());
    }

    public void Dispose()
    {
        _session?.Dispose();
    }

    private async Task<TerminalSession> ConnectAsync(Core.Terminal.Emulators.TerminalEmulatorBase emulator)
    {
        _session = new TerminalSession(emulator, "SendKeyTest");
        await _session.ConnectAsync(_connection);
        return _session;
    }

    [Fact]
    public async Task SendKey_Hjelp_SendsExtendedSequence()
    {
        var session = await ConnectAsync(new TDV2200Emulator(80, 24));

        var result = await _registry.ExecuteAsync("SENDKEY", session,
            new CommandArgs().Set("key", "HJELP"));

        Assert.True(result.Success, result.Error);
        // HJELP is grid G53, extended sequence ESC [ 46 _
        Assert.Equal("\x1b[46_", _connection.GetLastSentAsString());
        Assert.Contains("ESC", result.Output); // escaped rendering in the transcript
    }

    [Fact]
    public async Task SendKey_ByGridPosition_Works()
    {
        var session = await ConnectAsync(new TDV2200Emulator(80, 24));

        var result = await _registry.ExecuteAsync("SENDKEY", session,
            new CommandArgs().Set("key", "G53"));

        Assert.True(result.Success, result.Error);
        Assert.Equal("\x1b[46_", _connection.GetLastSentAsString());
    }

    [Fact]
    public async Task SendKey_UnknownKey_FailsWithGuidance()
    {
        var session = await ConnectAsync(new TDV2200Emulator(80, 24));

        var result = await _registry.ExecuteAsync("SENDKEY", session,
            new CommandArgs().Set("key", "NOSUCHKEY"));

        Assert.False(result.Success);
        Assert.Contains("unknown TDV key", result.Error);
        Assert.Empty(_connection.GetSentData());
    }

    [Fact]
    public async Task SendKey_OnVt100_FailsPointingToSendraw()
    {
        var session = await ConnectAsync(new VT100Emulator(80, 24));

        var result = await _registry.ExecuteAsync("SENDKEY", session,
            new CommandArgs().Set("key", "HJELP"));

        Assert.False(result.Success);
        Assert.Contains("SENDRAW", result.Error);
    }
}
