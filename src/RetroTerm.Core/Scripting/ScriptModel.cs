using System;
using System.Collections.Generic;
using RetroTerm.Core.Commands;

namespace RetroTerm.Core.Scripting;

/// <summary>
/// What kind of step this is. Control-flow verbs (LABEL/GOTO/GOSUB/RETURN) are part
/// of the script LANGUAGE, not session commands — the runner interprets them itself.
/// </summary>
public enum ScriptStepKind
{
    /// <summary>
    /// A registered session command (SEND, WAITFOR, ...).
    /// </summary>
    Command,

    /// <summary>
    /// LABEL name — a jump target; executing it does nothing.
    /// </summary>
    Label,

    /// <summary>
    /// GOTO name — jump, no return.
    /// </summary>
    Goto,

    /// <summary>
    /// GOSUB name — jump, RETURN comes back to the next step.
    /// </summary>
    Gosub,

    /// <summary>
    /// RETURN — back to the step after the most recent GOSUB.
    /// </summary>
    Return,

    /// <summary>
    /// SET name value — assign a script variable.
    /// </summary>
    Set,

    /// <summary>
    /// IF left op right — run the following block only when the condition holds.
    /// </summary>
    If,

    /// <summary>
    /// ELSE — the alternative block of the innermost IF.
    /// </summary>
    Else,

    /// <summary>
    /// ENDIF — closes an IF.
    /// </summary>
    EndIf
}

/// <summary>
/// One executable step of a parsed script: a command name (validated against the
/// registry at parse time) plus its arguments, tagged with the source line for
/// error reporting and transcripts — or a control-flow step (label/jump).
/// </summary>
public sealed class ScriptStep
{
    /// <summary>
    /// 1-based line number in the source script.
    /// </summary>
    public int LineNumber { get; }

    /// <summary>
    /// The source line as written (trimmed) — shown in transcripts.
    /// </summary>
    public string SourceText { get; }

    /// <summary>
    /// What kind of step this is.
    /// </summary>
    public ScriptStepKind Kind { get; }

    /// <summary>
    /// Registered command name this step calls (Kind == Command only).
    /// </summary>
    public string CommandName { get; }

    /// <summary>
    /// Arguments for the command (Kind == Command only).
    /// </summary>
    public CommandArgs Args { get; }

    /// <summary>
    /// When true (written as <c>optional=true</c> on the line), a failure of this step
    /// does not stop the script. For probing steps like "dismiss a message if present".
    /// </summary>
    public bool Optional { get; }

    /// <summary>
    /// Label to jump to when this step's command TIMES OUT (written as
    /// <c>ontimeout=label</c> on WAITFOR/WAITIDLE lines). A timeout with a target is a
    /// planned branch, not a failure — retry loops are built from this plus GOTO.
    /// </summary>
    public string? OnTimeoutLabel { get; }

    /// <summary>
    /// The label name this step declares (Label) or targets (Goto/Gosub).
    /// </summary>
    public string? TargetLabel { get; }

    /// <summary>
    /// Variable name the step's capture is stored into (written as <c>into=name</c>).
    /// WAITFOR stores the matched text (regex group 1 when present); READ*/STATUS
    /// store their output.
    /// </summary>
    public string? IntoVariable { get; }

    /// <summary>
    /// SET: the variable name. IF: the left operand (expanded at run time).
    /// </summary>
    public string? SetName { get; }

    /// <summary>
    /// SET: the value expression. IF: the right operand (expanded at run time).
    /// </summary>
    public string? SetValue { get; }

    /// <summary>
    /// IF only: the operator — ==, !=, CONTAINS or MATCHES.
    /// </summary>
    public string? IfOperator { get; }

    /// <summary>
    /// IF: index of the matching ELSE/ENDIF (jump target when false).
    /// ELSE: index of the matching ENDIF. Set by the parser's structure pass.
    /// </summary>
    public int JumpIndex { get; internal set; } = -1;

    /// <summary>
    /// A command step.
    /// </summary>
    public ScriptStep(int lineNumber, string sourceText, string commandName, CommandArgs args,
        bool optional, string? onTimeoutLabel = null, string? intoVariable = null)
    {
        LineNumber = lineNumber;
        SourceText = sourceText;
        Kind = ScriptStepKind.Command;
        CommandName = commandName;
        Args = args;
        Optional = optional;
        OnTimeoutLabel = onTimeoutLabel;
        IntoVariable = intoVariable;
    }

    /// <summary>
    /// A control-flow step (Label/Goto/Gosub/Return/Else/EndIf).
    /// </summary>
    public ScriptStep(int lineNumber, string sourceText, ScriptStepKind kind, string? targetLabel)
    {
        if (kind == ScriptStepKind.Command)
            throw new ArgumentException("Use the command constructor for command steps", nameof(kind));
        LineNumber = lineNumber;
        SourceText = sourceText;
        Kind = kind;
        CommandName = string.Empty;
        Args = CommandArgs.Empty;
        TargetLabel = targetLabel;
    }

    /// <summary>
    /// A SET step (kind = Set) or an IF step (kind = If, with operator).
    /// </summary>
    public ScriptStep(int lineNumber, string sourceText, ScriptStepKind kind,
        string name, string value, string? ifOperator)
    {
        LineNumber = lineNumber;
        SourceText = sourceText;
        Kind = kind;
        CommandName = string.Empty;
        Args = CommandArgs.Empty;
        SetName = name;
        SetValue = value;
        IfOperator = ifOperator;
    }
}

/// <summary>
/// One parse problem, anchored to its line — the script editor lists these and jumps
/// to the line on click.
/// </summary>
public sealed class ScriptParseError
{
    /// <summary>
    /// 1-based line number the error is on.
    /// </summary>
    public int LineNumber { get; }

    /// <summary>
    /// What is wrong, in plain words.
    /// </summary>
    public string Message { get; }

    public ScriptParseError(int lineNumber, string message)
    {
        LineNumber = lineNumber;
        Message = message;
    }

    public override string ToString() => $"line {LineNumber}: {Message}";
}

/// <summary>
/// Result of parsing a script: the runnable steps and every problem found. The parser
/// does not stop at the first error — the editor wants all of them at once.
/// A script is runnable only when Errors is empty.
/// </summary>
public sealed class ParsedScript
{
    public IReadOnlyList<ScriptStep> Steps { get; }
    public IReadOnlyList<ScriptParseError> Errors { get; }
    public bool IsValid => Errors.Count == 0;

    /// <summary>
    /// Label name → index into Steps. Built and validated by the parser.
    /// </summary>
    public IReadOnlyDictionary<string, int> Labels { get; }

    public ParsedScript(IReadOnlyList<ScriptStep> steps, IReadOnlyList<ScriptParseError> errors,
        IReadOnlyDictionary<string, int>? labels = null)
    {
        Steps = steps;
        Errors = errors;
        Labels = labels ?? new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
    }
}
