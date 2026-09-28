using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using RetroTerm.Core.Logging;
using RetroTerm.Core.Session;

namespace RetroTerm.Core.Commands.Builtin;

/// <summary>
/// Protocol-monitor access for scripts, the console and MCP — the same decoded
/// stream the Protocol Monitor window shows, driven by <see cref="ProtocolTracer"/>.
///
/// Every entry carries a UNIQUE, strictly increasing id (never reused, survives
/// TRACECLEAR), so an MCP client polls incrementally:
///   TRACEREAD              → newest entries, note the last id
///   TRACEREAD sinceid=1234 → only entries with id >= 1234
///
/// The tracer is GLOBAL (one wire trace for the whole app, like the monitor
/// window) — the session argument is unused.
/// </summary>
public static class TraceCommands
{
    public static void RegisterAll(CommandRegistry registry)
    {
        if (registry == null) throw new ArgumentNullException(nameof(registry));
        registry.Register(new TraceStartCommand());
        registry.Register(new TraceStopCommand());
        registry.Register(new TraceReadCommand());
        registry.Register(new TraceClearCommand());
    }
}

/// <summary>
/// TRACESTART — turn the protocol trace on.
/// </summary>
public sealed class TraceStartCommand : ISessionCommand
{
    public string Name => "TRACESTART";
    public string Summary => "Start the protocol trace (same decoded stream as the Protocol Monitor window)";
    public string Example => "TRACESTART";

    public IReadOnlyList<CommandParameter> Parameters { get; } = new[]
    {
        new CommandParameter("raw", CommandParameterType.Bool, required: false, defaultValue: "true",
            "Also record whole network blocks as RAW entries (the monitor's raw pane)")
    };

    public Task<CommandResult> ExecuteAsync(TerminalSession session, CommandArgs args, CancellationToken cancellationToken)
    {
        ProtocolTracer.CaptureRawBlocks = args.GetBool("raw", true);
        ProtocolTracer.Enabled = true;
        return Task.FromResult(CommandResult.Ok(
            $"protocol trace ON — next entry id is {ProtocolTracer.NextId}; read with TRACEREAD sinceid={ProtocolTracer.NextId}"));
    }
}

/// <summary>
/// TRACESTOP — turn the protocol trace off. The buffer keeps its entries.
/// </summary>
public sealed class TraceStopCommand : ISessionCommand
{
    public string Name => "TRACESTOP";
    public string Summary => "Stop the protocol trace — buffered entries stay readable with TRACEREAD";
    public string Example => "TRACESTOP";

    public IReadOnlyList<CommandParameter> Parameters { get; } = Array.Empty<CommandParameter>();

    public Task<CommandResult> ExecuteAsync(TerminalSession session, CommandArgs args, CancellationToken cancellationToken)
    {
        ProtocolTracer.Enabled = false;
        return Task.FromResult(CommandResult.Ok("protocol trace OFF"));
    }
}

/// <summary>
/// TRACEREAD — read decoded trace entries, incrementally by id.
/// </summary>
public sealed class TraceReadCommand : ISessionCommand
{
    public string Name => "TRACEREAD";
    public string Summary => "Read protocol trace entries — each line starts with its unique id; poll with sinceid=<last id + 1>";
    public string Example => "TRACEREAD sinceid=0 max=200";
    public bool ProducesOutput => true;

    public IReadOnlyList<CommandParameter> Parameters { get; } = new[]
    {
        new CommandParameter("sinceid", CommandParameterType.Int, required: false, defaultValue: "0",
            "Only entries with id >= this value (ids are unique and strictly increasing, they survive TRACECLEAR)"),
        new CommandParameter("max", CommandParameterType.Int, required: false, defaultValue: "200",
            "At most this many entries, oldest first"),
        new CommandParameter("raw", CommandParameterType.Bool, required: false, defaultValue: "false",
            "Include RAW block entries (duplicates of the decoded stream, hex only)"),
        new CommandParameter("hex", CommandParameterType.Bool, required: false, defaultValue: "false",
            "Include the hex column in each line")
    };

    public Task<CommandResult> ExecuteAsync(TerminalSession session, CommandArgs args, CancellationToken cancellationToken)
    {
        long sinceId = args.GetInt("sinceid", 0);
        int max = args.GetInt("max", 200);
        bool includeRaw = args.GetBool("raw", false);
        bool includeHex = args.GetBool("hex", false);

        var entries = ProtocolTracer.GetEntriesSince(sinceId, max);

        var sb = new StringBuilder(256 + entries.Length * 96);
        long lastId = 0;
        int written = 0;
        for (int i = 0; i < entries.Length; i++)
        {
            var e = entries[i];
            if (!includeRaw && e.Kind == TraceKind.RawBlock)
            {
                lastId = e.Id; // still counts for the poll cursor
                continue;
            }
            sb.Append('[').Append(e.Id).Append("] ").Append(e.Format(includeHex)).Append('\n');
            lastId = e.Id;
            written++;
        }

        // Header line FIRST in the final output: the poll cursor without parsing lines.
        var header = $"trace {(ProtocolTracer.Enabled ? "ON" : "OFF")} — {written} entr{(written == 1 ? "y" : "ies")}"
                     + (lastId > 0 ? $", last id {lastId}, poll next with sinceid={lastId + 1}" : $", nothing at id >= {sinceId}")
                     + "\n";
        var outcome = CommandResult.Ok(header + sb);
        outcome.CaptureValue = lastId > 0 ? (lastId + 1).ToString() : sinceId.ToString();
        return Task.FromResult(outcome);
    }
}

/// <summary>
/// TRACECLEAR — empty the trace buffer. Ids keep counting up.
/// </summary>
public sealed class TraceClearCommand : ISessionCommand
{
    public string Name => "TRACECLEAR";
    public string Summary => "Discard buffered trace entries — ids keep increasing, sinceid= polling stays valid";
    public string Example => "TRACECLEAR";

    public IReadOnlyList<CommandParameter> Parameters { get; } = Array.Empty<CommandParameter>();

    public Task<CommandResult> ExecuteAsync(TerminalSession session, CommandArgs args, CancellationToken cancellationToken)
    {
        ProtocolTracer.Clear();
        return Task.FromResult(CommandResult.Ok("trace buffer cleared — ids continue from " + ProtocolTracer.NextId));
    }
}
