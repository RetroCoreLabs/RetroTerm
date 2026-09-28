using System;

namespace RetroTerm.Core.Commands;

/// <summary>
/// Outcome of one command execution. Follows the handover rules: a failing command
/// still carries whatever output the machine produced (rule 4), and the elapsed time
/// is always reported (rule 5) — knowing a step took 23 s instead of 200 ms is often
/// the whole diagnosis.
/// </summary>
public sealed class CommandResult
{
    /// <summary>
    /// True when the command achieved what it was asked to do.
    /// </summary>
    public bool Success { get; }

    /// <summary>
    /// Command output (screen text, status report...). Present on failure too when
    /// there is anything to show — partial output is the most valuable thing on a
    /// misbehaving step.
    /// </summary>
    public string? Output { get; }

    /// <summary>
    /// What went wrong, in plain words. Null on success.
    /// </summary>
    public string? Error { get; }

    /// <summary>
    /// How long the command took. Set by the command or by the dispatcher.
    /// </summary>
    public TimeSpan Elapsed { get; set; }

    /// <summary>
    /// True when the failure was a TIMEOUT (WAITFOR/WAITIDLE giving up), as opposed to
    /// any other error. The script runner uses this for ontimeout=label jumps —
    /// a timeout can be a planned branch, other failures never are.
    /// </summary>
    public bool TimedOut { get; set; }

    /// <summary>
    /// True when the failure was the CONNECTION DROPPING mid-command (WAITFOR/WAITIDLE
    /// observe this directly via the connection status). The script runner uses this
    /// in addition to the session's ConnectionLost event — the command's own detection
    /// can complete before the event handler has run, and the run must still report
    /// "connection lost" (rule 6), never a generic step failure.
    /// </summary>
    public bool ConnectionLost { get; set; }

    /// <summary>
    /// The value an <c>into=variable</c> step stores: WAITFOR sets the matched text
    /// (regex capture group 1 when the pattern has one), READ*/STATUS leave it null and
    /// the runner falls back to Output. This is how scripts pull answers off the
    /// screen into variables.
    /// </summary>
    public string? CaptureValue { get; set; }

    private CommandResult(bool success, string? output, string? error)
    {
        Success = success;
        Output = output;
        Error = error;
    }

    public static CommandResult Ok(string? output = null) => new CommandResult(true, output, null);

    public static CommandResult Fail(string error, string? output = null) => new CommandResult(false, output, error);
}
