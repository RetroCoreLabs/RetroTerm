using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using RetroTerm.Core.Session;
using RetroTerm.Core.Terminal.Buffer;

namespace RetroTerm.Core.Commands.Builtin;

/// <summary>
/// READNEW — everything that scrolled into the scrollback since the last READNEW on
/// the same session, plus the current visible screen. This is how a caller captures a
/// long listing that scrolled past without re-reading the whole scrollback each time.
///
/// The read cursor is per (this command instance, session): one registry serves one
/// MCP server / script engine, so the cursor tracks that consumer's position. The
/// table holds sessions weakly — a closed session's cursor just disappears.
///
/// Note: the visible screen is included on every call (it has not scrolled out yet,
/// so it cannot be tracked by the scrollback cursor). Expect the tail of one call to
/// overlap the next call's scrollback lines.
/// </summary>
public sealed class ReadNewCommand : ISessionCommand
{
    private sealed class Cursor
    {
        public int ScrollbackConsumed;
    }

    private readonly ConditionalWeakTable<TerminalSession, Cursor> _cursors =
        new ConditionalWeakTable<TerminalSession, Cursor>();

    public string Name => "READNEW";
    public string Summary => "Read scrollback lines added since the last READNEW, plus the current screen";
    public string Example => "READNEW";
    public bool ProducesOutput => true; // in a script: captures the new output into the transcript

    public IReadOnlyList<CommandParameter> Parameters { get; } = Array.Empty<CommandParameter>();

    public async Task<CommandResult> ExecuteAsync(TerminalSession session, CommandArgs args, CancellationToken cancellationToken)
    {
        var cursor = _cursors.GetOrCreateValue(session);

        var text = await session.RunOnSessionThreadAsync(() =>
        {
            var buffer = session.Emulator.GetBuffer();
            var sb = new StringBuilder();

            int total = buffer.ScrollbackLineCount;
            int from = cursor.ScrollbackConsumed;
            if (from > total)
            {
                // Scrollback was cleared since the last read — start over.
                from = 0;
            }

            for (int i = from; i < total; i++)
            {
                var line = ScreenReader.GetScrollbackLineText(buffer, i);
                if (line != null)
                {
                    sb.Append(line).Append('\n');
                }
            }
            cursor.ScrollbackConsumed = total;

            sb.Append("--- current screen ---\n");
            sb.Append(ScreenReader.GetScreenText(buffer, stripTrailingBlanks: true));
            return sb.ToString();
        }).ConfigureAwait(false);

        return CommandResult.Ok(text);
    }
}
