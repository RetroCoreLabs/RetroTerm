using System;
using System.Text;
using System.Threading.Tasks;
using RetroTerm.Core.Commands;
using RetroTerm.Core.Commands.Builtin;
using RetroTerm.Core.Logging;
using RetroTerm.Core.Session;
using RetroTerm.Core.Terminal.Emulators;
using Xunit;

namespace RetroTerm.Tests.Commands;

/// <summary>
/// TRACESTART/TRACESTOP/TRACEREAD/TRACECLEAR — the MCP window into the protocol
/// monitor. The contract that matters: every entry has a UNIQUE increasing id,
/// TRACEREAD sinceid= is an exact incremental poll, and ids survive TRACECLEAR.
/// ProtocolTracer is a static singleton — serialized via the collection.
/// </summary>
[Collection("ProtocolTracer")]
public class TraceCommandsTests : IDisposable
{
    private readonly CommandRegistry _registry;
    private readonly TerminalSession _session;

    public TraceCommandsTests()
    {
        ProtocolTracer.ResetForTesting();
        _registry = new CommandRegistry();
        BuiltinCommands.RegisterAll(_registry);
        _session = new TerminalSession(new VT100Emulator(80, 24), "TraceTest");
    }

    public void Dispose()
    {
        ProtocolTracer.ResetForTesting();
        _session.Dispose();
    }

    private static void Feed(string text)
    {
        ProtocolTracer.TraceIncoming(Encoding.ASCII.GetBytes(text));
    }

    [Fact]
    public async Task TraceStart_EnablesTracer_AndSaysWhereToPollFrom()
    {
        var result = await _registry.ExecuteAsync("TRACESTART", _session, CommandArgs.Empty);

        Assert.True(result.Success);
        Assert.True(ProtocolTracer.Enabled);
        Assert.Contains("sinceid=", result.Output);
    }

    [Fact]
    public async Task Entries_GetUniqueIncreasingIds()
    {
        await _registry.ExecuteAsync("TRACESTART", _session, CommandArgs.Empty);
        Feed("AB");
        Feed("CD");

        var entries = ProtocolTracer.GetEntries();
        Assert.True(entries.Length >= 2);
        for (int i = 1; i < entries.Length; i++)
        {
            Assert.True(entries[i].Id > entries[i - 1].Id,
                $"ids not strictly increasing: {entries[i - 1].Id} then {entries[i].Id}");
        }
    }

    [Fact]
    public async Task TraceRead_SinceId_IsAnExactIncrementalPoll()
    {
        await _registry.ExecuteAsync("TRACESTART", _session,
            new CommandArgs().Set("raw", "false"));
        Feed("FIRST");

        // First poll from 0 sees FIRST and hands back the next poll cursor as the capture.
        var first = await _registry.ExecuteAsync("TRACEREAD", _session,
            new CommandArgs().Set("sinceid", "0"));
        Assert.True(first.Success);
        Assert.Contains("FIRST", first.Output);
        Assert.NotNull(first.CaptureValue);
        var cursor = first.CaptureValue!;

        // Nothing new: polling from the cursor returns no entries — and NOT "FIRST" again.
        var empty = await _registry.ExecuteAsync("TRACEREAD", _session,
            new CommandArgs().Set("sinceid", cursor));
        Assert.True(empty.Success);
        Assert.DoesNotContain("FIRST", empty.Output);

        // New traffic appears exactly once from the same cursor.
        Feed("SECOND");
        var second = await _registry.ExecuteAsync("TRACEREAD", _session,
            new CommandArgs().Set("sinceid", cursor));
        Assert.True(second.Success);
        Assert.Contains("SECOND", second.Output);
        Assert.DoesNotContain("FIRST", second.Output);
    }

    [Fact]
    public async Task TraceRead_LinesCarryTheirId()
    {
        await _registry.ExecuteAsync("TRACESTART", _session,
            new CommandArgs().Set("raw", "false"));
        Feed("HELLO");

        var result = await _registry.ExecuteAsync("TRACEREAD", _session, CommandArgs.Empty);

        var entries = ProtocolTracer.GetEntries();
        Assert.True(entries.Length > 0);
        Assert.Contains($"[{entries[0].Id}]", result.Output);
    }

    [Fact]
    public async Task TraceClear_KeepsIdsIncreasing()
    {
        await _registry.ExecuteAsync("TRACESTART", _session, CommandArgs.Empty);
        Feed("BEFORE");
        var beforeMax = ProtocolTracer.NextId;

        await _registry.ExecuteAsync("TRACECLEAR", _session, CommandArgs.Empty);
        Feed("AFTER");

        var entries = ProtocolTracer.GetEntries();
        Assert.True(entries.Length > 0);
        // Every post-clear id is >= the pre-clear high-water mark: sinceid= cursors
        // from before the clear never see a duplicate id.
        for (int i = 0; i < entries.Length; i++)
        {
            Assert.True(entries[i].Id >= beforeMax);
        }
    }

    [Fact]
    public async Task TraceStop_DisablesTracer()
    {
        await _registry.ExecuteAsync("TRACESTART", _session, CommandArgs.Empty);
        var result = await _registry.ExecuteAsync("TRACESTOP", _session, CommandArgs.Empty);

        Assert.True(result.Success);
        Assert.False(ProtocolTracer.Enabled);
    }
}

[CollectionDefinition("ProtocolTracer")]
public class ProtocolTracerCollection
{
    // Serializes tests that touch the ProtocolTracer static singleton.
}
