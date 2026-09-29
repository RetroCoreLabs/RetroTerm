using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using RetroTerm.Core.Session;

namespace RetroTerm.Core.Commands;

/// <summary>
/// One self-contained session operation — THE extensibility seam of the MCP/scripting
/// architecture (the "one class + one registration" rule: CommandRegistry.cs and
/// docs\MCP-AND-SCRIPTING.md section 4). A new capability is one class
/// implementing this, registered once in CommandRegistry; it then automatically
/// appears as a script DSL verb, as an MCP tool, and in the generated help.
///
/// Metadata rules (enforced at registration): Name, Summary and Example must be
/// non-empty, and every parameter must carry a description. Help is generated from
/// this metadata and never hand-written.
///
/// Threading: ExecuteAsync is called from any thread. Commands touch the emulator or
/// buffer ONLY through the session's pump primitives (RunOnSessionThreadAsync,
/// ReadScreenAsync, WaitForScreenAsync) — never directly.
/// </summary>
public interface ISessionCommand
{
    /// <summary>
    /// Command name = script verb = MCP tool suffix. Case-insensitive, no spaces.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// One-line description of what the command does.
    /// </summary>
    string Summary { get; }

    /// <summary>
    /// Parameter metadata — validation and generated help come from this.
    /// </summary>
    IReadOnlyList<CommandParameter> Parameters { get; }

    /// <summary>
    /// One usage example in script DSL syntax, e.g. <c>SEND "LIST-FILES"</c>.
    /// </summary>
    string Example { get; }

    /// <summary>
    /// True when producing output IS the command's purpose (READSCREEN, READNEW,
    /// STATUS, HELP). Script transcripts print the output of these steps even on
    /// success — READSCREEN in a script is a checkpoint: "capture the screen into
    /// the transcript here". Commands like WAITFOR keep the default false: their
    /// output (the screen) is diagnostic and only shown when the step fails.
    /// </summary>
    bool ProducesOutput => false;

    /// <summary>
    /// True when the command can fail with a TIMEOUT (sets CommandResult.TimedOut) —
    /// WAITFOR and WAITIDLE. The parser rejects <c>ontimeout=</c> on any other
    /// command: a jump that can never be taken is a script bug, not a feature.
    /// </summary>
    bool CanTimeOut => false;

    /// <summary>
    /// True when the command produces a value <c>into=var</c> can store (a capture
    /// or meaningful output). Defaults to <see cref="ProducesOutput"/>; WAITFOR and
    /// WAITIDLE override to true (match text / screen). The parser rejects
    /// <c>into=</c> elsewhere — storing guaranteed-empty text is always a mistake.
    /// </summary>
    bool ProducesCapture => ProducesOutput;

    /// <summary>
    /// Executes the command against a session.
    /// </summary>
    Task<CommandResult> ExecuteAsync(TerminalSession session, CommandArgs args, CancellationToken cancellationToken);
}
