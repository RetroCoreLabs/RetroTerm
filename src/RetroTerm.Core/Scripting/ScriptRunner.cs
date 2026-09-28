using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using RetroTerm.Core.Commands;
using RetroTerm.Core.Session;

namespace RetroTerm.Core.Scripting;

/// <summary>
/// Outcome of one executed script step, for the transcript.
/// </summary>
public sealed class ScriptStepResult
{
    public ScriptStep Step { get; }
    public CommandResult Result { get; }

    /// <summary>
    /// True when this step's command exists to produce output (READSCREEN, READNEW,
    /// STATUS, HELP — ISessionCommand.ProducesOutput): transcript renderers print
    /// the output of these steps even on success.
    /// </summary>
    public bool ShowOutput { get; }

    public ScriptStepResult(ScriptStep step, CommandResult result, bool showOutput = false)
    {
        Step = step;
        Result = result;
        ShowOutput = showOutput;
    }
}

/// <summary>
/// Outcome of a script run: per-step transcript with timings, and — when it stopped
/// early — which step failed and why. The session is ALWAYS left live: when a step
/// misbehaves, the caller pokes at the same session one command at a time
/// (HANDOVER-MCP-TERMINAL-CONTROL.md §4: "a script that stops on the failing step
/// and hands the live session back is the ideal").
/// </summary>
public sealed class ScriptRunResult
{
    /// <summary>
    /// True when every non-optional step succeeded.
    /// </summary>
    public bool Success { get; }

    /// <summary>
    /// Everything that ran, in order, with per-step output and timing.
    /// </summary>
    public IReadOnlyList<ScriptStepResult> Transcript { get; }

    /// <summary>
    /// The step that stopped the script, or null on success.
    /// </summary>
    public ScriptStep? FailedStep { get; }

    /// <summary>
    /// Why the run stopped early, or null on success.
    /// </summary>
    public string? Error { get; }

    /// <summary>
    /// True when the run stopped because the connection dropped (rule 6: say so).
    /// </summary>
    public bool ConnectionLost { get; }

    /// <summary>
    /// Total run time.
    /// </summary>
    public TimeSpan Elapsed { get; }

    public ScriptRunResult(bool success, IReadOnlyList<ScriptStepResult> transcript,
        ScriptStep? failedStep, string? error, bool connectionLost, TimeSpan elapsed)
    {
        Success = success;
        Transcript = transcript;
        FailedStep = failedStep;
        Error = error;
        ConnectionLost = connectionLost;
        Elapsed = elapsed;
    }
}

/// <summary>
/// Executes a parsed script against a session, one step at a time, through the
/// command registry — the same dispatch path MCP tools use.
///
/// Behavior contract (from the handover's hard-won rules):
///  - Steps sequence via WAITFOR, never guessed delays — that is the DSL's job.
///  - STOPS at the first failing non-optional step and returns; the session stays
///    live and untouched for interactive poking.
///  - A dropped connection aborts the run immediately and the result says which
///    step it happened at, with the transcript so far.
///  - Every step's elapsed time is in the transcript.
/// </summary>
public sealed class ScriptRunner
{
    private readonly CommandRegistry _registry;

    public ScriptRunner(CommandRegistry registry)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
    }

    /// <summary>
    /// Runs the script. Throws ArgumentException for a script with parse errors —
    /// validation belongs to the editor/caller, running a broken script is a bug.
    /// Progress (when given) receives each step result as it completes.
    /// </summary>
    public async Task<ScriptRunResult> RunAsync(ParsedScript script, TerminalSession session,
        CancellationToken cancellationToken = default, IProgress<ScriptStepResult>? progress = null,
        ScriptContext? context = null)
    {
        if (script == null) throw new ArgumentNullException(nameof(script));
        if (session == null) throw new ArgumentNullException(nameof(session));

        // The variable store. Callers can pass one in (the console shares its context
        // across commands and RUNs); scripts started fresh get an empty one.
        context ??= new ScriptContext();
        if (!script.IsValid)
        {
            throw new ArgumentException(
                $"Script has {script.Errors.Count} parse error(s) — first: {script.Errors[0]}", nameof(script));
        }

        var transcript = new List<ScriptStepResult>(script.Steps.Count);
        var stopwatch = Stopwatch.StartNew();

        // A dropped connection must abort the run even mid-step: the flag fails the
        // step evaluation below, and the linked token cancels a step blocked in a
        // long WAITFOR so the abort is prompt (rule 6: say so, don't hang).
        bool connectionLost = false;
        using var lostCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        void OnLost(string reason)
        {
            Volatile.Write(ref connectionLost, true);
            try { lostCts.Cancel(); } catch (ObjectDisposedException) { /* run already over */ }
        }

        session.ConnectionLost += OnLost;
        try
        {
            var steps = script.Steps;

            // Program counter + GOSUB return stack. Control flow (LABEL/GOTO/GOSUB/
            // RETURN and ontimeout jumps) is interpreted here; the parser already
            // validated that every target label exists.
            int pc = 0;
            var returnStack = new Stack<int>();
            int executed = 0;

            while (pc < steps.Count)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // Backstop against a tight GOTO loop with no waits: a legitimate retry
                // loop always contains a WAITFOR/WAITIDLE/SLEEP, so real scripts never
                // get anywhere near this.
                executed++;
                if (executed > MaxExecutedSteps)
                {
                    return new ScriptRunResult(false, transcript, steps[pc],
                        $"Script aborted after executing {MaxExecutedSteps} steps — runaway GOTO loop?",
                        connectionLost: false, stopwatch.Elapsed);
                }

                var step = steps[pc];

                // Control-flow steps never touch the session.
                switch (step.Kind)
                {
                    case ScriptStepKind.Label:
                        pc++;
                        continue;

                    case ScriptStepKind.Goto:
                        pc = script.Labels[step.TargetLabel!];
                        continue;

                    case ScriptStepKind.Gosub:
                        returnStack.Push(pc + 1);
                        pc = script.Labels[step.TargetLabel!];
                        continue;

                    case ScriptStepKind.Return:
                        if (returnStack.Count == 0)
                        {
                            return new ScriptRunResult(false, transcript, step,
                                $"RETURN at line {step.LineNumber} without a matching GOSUB",
                                connectionLost: false, stopwatch.Elapsed);
                        }
                        pc = returnStack.Pop();
                        continue;

                    case ScriptStepKind.Set:
                        // Both name and value may reference other variables.
                        context.Set(context.Expand(step.SetName!), context.Expand(step.SetValue!));
                        pc++;
                        continue;

                    case ScriptStepKind.If:
                        // Condition false → jump PAST the matching ELSE/ENDIF marker.
                        pc = EvaluateCondition(context, step) ? pc + 1 : step.JumpIndex + 1;
                        continue;

                    case ScriptStepKind.Else:
                        // Reached from the true branch: skip over the else block.
                        pc = step.JumpIndex + 1;
                        continue;

                    case ScriptStepKind.EndIf:
                        pc++;
                        continue;
                }

                if (Volatile.Read(ref connectionLost))
                {
                    return new ScriptRunResult(false, transcript, step,
                        $"Connection lost before step at line {step.LineNumber}: {step.SourceText}",
                        connectionLost: true, stopwatch.Elapsed);
                }

                CommandResult result;
                try
                {
                    // Variable expansion happens at EXECUTION time so values captured
                    // earlier in the run are visible: SEND "LOGIN $user".
                    var expandedArgs = step.Args.ExpandWith(context.Expand);
                    result = await _registry.ExecuteAsync(step.CommandName, session, expandedArgs, lostCts.Token)
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (Volatile.Read(ref connectionLost))
                {
                    // The step was interrupted by the drop, not by the caller.
                    return new ScriptRunResult(false, transcript, step,
                        $"Connection lost during step at line {step.LineNumber}: {step.SourceText}",
                        connectionLost: true, stopwatch.Elapsed);
                }

                bool showOutput = _registry.TryGet(step.CommandName, out var command) && command.ProducesOutput;
                var stepResult = new ScriptStepResult(step, result, showOutput);
                transcript.Add(stepResult);
                progress?.Report(stepResult);

                // into=var: store the step's capture — WAITFOR's matched text (regex
                // group 1 when present), otherwise the command output.
                if (result.Success && step.IntoVariable != null)
                {
                    context.Set(step.IntoVariable, result.CaptureValue ?? result.Output ?? string.Empty);
                }

                if (!result.Success)
                {
                    // A timeout with an ontimeout target is a PLANNED branch (retry
                    // loops), not a failure — jump and keep going.
                    if (result.TimedOut && step.OnTimeoutLabel != null)
                    {
                        pc = script.Labels[step.OnTimeoutLabel];
                        continue;
                    }

                    if (!step.Optional)
                    {
                        // Two ways to learn about a drop: the session's ConnectionLost
                        // event (our flag) OR the command's own detection (WAITFOR sees
                        // the status change directly). The command can finish BEFORE the
                        // event handler runs — check both or the run mislabels the drop
                        // as a generic step failure.
                        if (Volatile.Read(ref connectionLost) || result.ConnectionLost)
                        {
                            return new ScriptRunResult(false, transcript, step,
                                $"Connection lost during step at line {step.LineNumber}: {step.SourceText}",
                                connectionLost: true, stopwatch.Elapsed);
                        }
                        // Stop here, hand the live session back. The transcript has the
                        // partial output; the failing step and its screen are in Result.
                        return new ScriptRunResult(false, transcript, step,
                            $"Step at line {step.LineNumber} failed: {result.Error}",
                            connectionLost: false, stopwatch.Elapsed);
                    }
                }

                pc++;
            }

            return new ScriptRunResult(true, transcript, null, null, false, stopwatch.Elapsed);
        }
        finally
        {
            session.ConnectionLost -= OnLost;
        }
    }

    /// <summary>
    /// Hard ceiling on executed steps per run — a backstop against `LABEL a` /
    /// `GOTO a` spinning forever, far above anything a real script does.
    /// </summary>
    private const int MaxExecutedSteps = 100_000;

    /// <summary>
    /// Evaluates an IF condition. Both operands are variable-expanded first.
    /// == and != are ordinal (case-sensitive — SINTRAN talks uppercase and the
    /// difference matters); CONTAINS is ordinal too; MATCHES is a .NET regex.
    /// </summary>
    private static bool EvaluateCondition(ScriptContext context, ScriptStep step)
    {
        var left = context.Expand(step.SetName!);
        var right = context.Expand(step.SetValue!);
        return step.IfOperator switch
        {
            "==" => string.Equals(left, right, StringComparison.Ordinal),
            "!=" => !string.Equals(left, right, StringComparison.Ordinal),
            "CONTAINS" => left.IndexOf(right, StringComparison.Ordinal) >= 0,
            "MATCHES" => System.Text.RegularExpressions.Regex.IsMatch(left, right),
            _ => false // parser guarantees the operator; belt and braces
        };
    }
}
