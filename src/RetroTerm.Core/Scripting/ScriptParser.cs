using System;
using System.Collections.Generic;
using System.Text;
using RetroTerm.Core.Commands;

namespace RetroTerm.Core.Scripting;

/// <summary>
/// Hand-written parser for the RetroTerm script DSL (.rts files).
///
/// The language is line-based and deliberately small:
///
///   # comment                        (also allowed after a step)
///   SEND "LIST-FILES"                — verb + positional argument
///   SEND "abc" cr=false              — named arguments as key=value
///   WAITFOR "X-C:" timeout=30000     — values may be quoted or bare
///   SENDRAW ESC
///   WAITIDLE 500
///   SLEEP 1000 optional=true         — optional: a failure does not stop the script
///
/// The parser is GENERIC: verbs resolve against the live CommandRegistry, positional
/// values map onto the command's parameters in declared order, and required-parameter
/// checks come from the command metadata. A newly registered command is a new script
/// verb with zero parser changes (PLAN-MCP-SCRIPTING.md phase 2.5).
///
/// Quoted strings support escapes: \" \\ \r \n \t \e (ESC) \xHH (hex) \NNN (octal)
/// — the same dialect as the programmable keys (EscapeSequenceFormatter). Errors
/// carry line numbers and parsing continues past them so an editor can show every
/// problem at once.
/// </summary>
public sealed class ScriptParser
{
    private readonly CommandRegistry _registry;

    public ScriptParser(CommandRegistry registry)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
    }

    /// <summary>
    /// Parses complete script text (any line-ending style).
    /// </summary>
    public ParsedScript Parse(string scriptText)
    {
        if (scriptText == null) throw new ArgumentNullException(nameof(scriptText));

        var steps = new List<ScriptStep>();
        var errors = new List<ScriptParseError>();

        var lines = scriptText.Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i].TrimEnd('\r').Trim();
            int lineNumber = i + 1;

            if (line.Length == 0 || line[0] == '#')
            {
                continue; // blank or comment line
            }

            ParseLine(line, lineNumber, steps, errors);
        }

        // Second pass: collect labels and validate every jump target. Doing this at
        // parse time means the editor flags a typo'd GOTO before anything runs.
        var labels = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < steps.Count; i++)
        {
            if (steps[i].Kind == ScriptStepKind.Label)
            {
                var name = steps[i].TargetLabel!;
                if (labels.ContainsKey(name))
                {
                    errors.Add(new ScriptParseError(steps[i].LineNumber, $"Duplicate label '{name}'"));
                }
                else
                {
                    labels.Add(name, i);
                }
            }
        }
        for (int i = 0; i < steps.Count; i++)
        {
            var step = steps[i];
            if ((step.Kind == ScriptStepKind.Goto || step.Kind == ScriptStepKind.Gosub)
                && !labels.ContainsKey(step.TargetLabel!))
            {
                errors.Add(new ScriptParseError(step.LineNumber,
                    $"{(step.Kind == ScriptStepKind.Goto ? "GOTO" : "GOSUB")} to unknown label '{step.TargetLabel}'"));
            }
            if (step.OnTimeoutLabel != null && !labels.ContainsKey(step.OnTimeoutLabel))
            {
                errors.Add(new ScriptParseError(step.LineNumber,
                    $"ontimeout target label '{step.OnTimeoutLabel}' does not exist"));
            }
        }

        // Third pass: IF/ELSE/ENDIF structure. Each IF learns where its ELSE/ENDIF is
        // (the jump target when the condition is false); each ELSE learns its ENDIF.
        var ifStack = new List<int>(); // indexes of open IFs
        var elseSeen = new List<bool>();
        for (int i = 0; i < steps.Count; i++)
        {
            var step = steps[i];
            if (step.Kind == ScriptStepKind.If)
            {
                ifStack.Add(i);
                elseSeen.Add(false);
            }
            else if (step.Kind == ScriptStepKind.Else)
            {
                if (ifStack.Count == 0)
                {
                    errors.Add(new ScriptParseError(step.LineNumber, "ELSE without a matching IF"));
                }
                else if (elseSeen[elseSeen.Count - 1])
                {
                    errors.Add(new ScriptParseError(step.LineNumber, "Second ELSE for the same IF"));
                }
                else
                {
                    steps[ifStack[ifStack.Count - 1]].JumpIndex = i;
                    elseSeen[elseSeen.Count - 1] = true;
                }
            }
            else if (step.Kind == ScriptStepKind.EndIf)
            {
                if (ifStack.Count == 0)
                {
                    errors.Add(new ScriptParseError(step.LineNumber, "ENDIF without a matching IF"));
                }
                else
                {
                    int ifIndex = ifStack[ifStack.Count - 1];
                    if (steps[ifIndex].JumpIndex < 0)
                    {
                        steps[ifIndex].JumpIndex = i; // no ELSE: false jumps straight here
                    }
                    else
                    {
                        steps[steps[ifIndex].JumpIndex].JumpIndex = i; // the ELSE jumps here
                    }
                    ifStack.RemoveAt(ifStack.Count - 1);
                    elseSeen.RemoveAt(elseSeen.Count - 1);
                }
            }
        }
        for (int i = 0; i < ifStack.Count; i++)
        {
            errors.Add(new ScriptParseError(steps[ifStack[i]].LineNumber, "IF without a matching ENDIF"));
        }

        return new ParsedScript(steps, errors, labels);
    }

    private void ParseLine(string line, int lineNumber, List<ScriptStep> steps, List<ScriptParseError> errors)
    {
        // Tokenize: verb, then positional values and key=value pairs.
        if (!TryTokenize(line, lineNumber, errors, out var verb, out var positional, out var named))
        {
            return;
        }

        // Control-flow verbs are part of the LANGUAGE, resolved before the registry —
        // the runner interprets them, they never reach a session.
        if (TryParseControlFlow(verb, positional, named, line, lineNumber, steps, errors))
        {
            return;
        }
        if (TryParseVariableFlow(verb, positional, named, line, lineNumber, steps, errors))
        {
            return;
        }

        if (!_registry.TryGet(verb, out var command))
        {
            errors.Add(new ScriptParseError(lineNumber,
                $"Unknown command '{verb}' — try HELP for the list of commands"));
            return;
        }

        var args = new CommandArgs();
        bool optional = false;
        string? onTimeoutLabel = null;
        string? intoVariable = null;

        // Named arguments: validated against the command's parameter list, except the
        // runner-level flags 'optional' and 'ontimeout' which belong to the step, not
        // the command.
        for (int i = 0; i < named.Count; i++)
        {
            var (key, value) = named[i];
            if (string.Equals(key, "optional", StringComparison.OrdinalIgnoreCase))
            {
                // A misspelled value must not silently mean "false".
                if (!IsBoolValue(value))
                {
                    errors.Add(new ScriptParseError(lineNumber,
                        $"optional= must be true/false (or 1/0, on/off, yes/no), got '{value}'"));
                    return;
                }
                optional = value == "1" || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(value, "on", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase);
                continue;
            }
            if (string.Equals(key, "ontimeout", StringComparison.OrdinalIgnoreCase))
            {
                // ontimeout on a command that can never time out is a jump that can
                // never be taken — always a script bug, flag it at edit time.
                if (!command.CanTimeOut)
                {
                    errors.Add(new ScriptParseError(lineNumber,
                        $"ontimeout= does nothing on {command.Name} — only commands that can time out (WAITFOR, WAITIDLE) support it"));
                    return;
                }
                onTimeoutLabel = value;
                continue;
            }
            if (string.Equals(key, "into", StringComparison.OrdinalIgnoreCase))
            {
                // into= on a command with nothing to store would fill the variable
                // with guaranteed-empty text.
                if (!command.ProducesCapture)
                {
                    errors.Add(new ScriptParseError(lineNumber,
                        $"into= does nothing on {command.Name} — it produces no value to store (WAITFOR, WAITIDLE, READSCREEN, READNEW, STATUS do)"));
                    return;
                }
                intoVariable = value;
                continue;
            }
            if (!HasParameter(command, key))
            {
                errors.Add(new ScriptParseError(lineNumber,
                    $"{command.Name} has no parameter '{key}' — see HELP {command.Name}"));
                continue;
            }
            args.Set(key, value);
        }

        // Positional values map onto the command's parameters in declared order.
        var parameters = command.Parameters;
        if (positional.Count > parameters.Count)
        {
            errors.Add(new ScriptParseError(lineNumber,
                $"{command.Name} takes at most {parameters.Count} value(s), got {positional.Count}"));
            return;
        }
        for (int i = 0; i < positional.Count; i++)
        {
            args.Set(parameters[i].Name, positional[i]);
        }

        // Required-parameter check at PARSE time: the editor flags the line before
        // anything runs, instead of the script dying mid-run.
        for (int i = 0; i < parameters.Count; i++)
        {
            if (parameters[i].Required && !args.Contains(parameters[i].Name))
            {
                errors.Add(new ScriptParseError(lineNumber,
                    $"{command.Name} is missing required parameter '{parameters[i].Name}' ({parameters[i].Description})"));
                return;
            }
        }

        // TYPE check at PARSE time. Without this, `timeout=300/ontimeout=wake` (a slash
        // typo) parsed as one string value and GetInt silently fell back to the default
        // — the script "ran" with a wrong timeout and no jump (2026-08-05). Values that
        // reference a variable ($...) are expanded at run time and cannot be checked here.
        for (int i = 0; i < parameters.Count; i++)
        {
            var p = parameters[i];
            var raw = args.GetString(p.Name);
            if (raw == null || raw.IndexOf('$') >= 0)
            {
                continue;
            }
            if (p.Type == CommandParameterType.Int
                && !int.TryParse(raw, System.Globalization.NumberStyles.Integer,
                       System.Globalization.CultureInfo.InvariantCulture, out _))
            {
                errors.Add(new ScriptParseError(lineNumber,
                    $"{command.Name}: '{p.Name}' must be a whole number, got '{raw}'"));
                return;
            }
            if (p.Type == CommandParameterType.Bool && !IsBoolValue(raw))
            {
                errors.Add(new ScriptParseError(lineNumber,
                    $"{command.Name}: '{p.Name}' must be true/false (or 1/0, on/off, yes/no), got '{raw}'"));
                return;
            }
        }

        steps.Add(new ScriptStep(lineNumber, line, command.Name, args, optional, onTimeoutLabel, intoVariable));
    }

    /// <summary>
    /// Parses the variable verbs: SET name value, IF left op right, ELSE, ENDIF.
    /// Returns true when the verb was one of them.
    /// </summary>
    private static bool TryParseVariableFlow(string verb, List<string> positional,
        List<(string Key, string Value)> named, string line, int lineNumber,
        List<ScriptStep> steps, List<ScriptParseError> errors)
    {
        if (string.Equals(verb, "SET", StringComparison.OrdinalIgnoreCase))
        {
            // key=value tokens would be swallowed by the tokenizer as named args —
            // rebuild them so SET user=x also works, but the documented form is
            // SET name value.
            if (positional.Count == 2 && named.Count == 0)
            {
                steps.Add(new ScriptStep(lineNumber, line, ScriptStepKind.Set,
                    positional[0], positional[1], null));
            }
            else if (positional.Count == 0 && named.Count == 1)
            {
                steps.Add(new ScriptStep(lineNumber, line, ScriptStepKind.Set,
                    named[0].Key, named[0].Value, null));
            }
            else
            {
                errors.Add(new ScriptParseError(lineNumber, "SET needs a name and a value: SET user \"GUEST\""));
            }
            return true;
        }

        if (string.Equals(verb, "IF", StringComparison.OrdinalIgnoreCase))
        {
            // Form: IF <left> <op> <right>. The tokenizer turns `IF $x == "y"` into
            // positional ["$x", "==", "y"]: the key=value split only applies when the
            // text before '=' is a plain identifier, so `==` and `!=` stay whole.
            if (positional.Count != 3)
            {
                errors.Add(new ScriptParseError(lineNumber,
                    "IF needs: IF <left> <op> <right> with op one of == != CONTAINS MATCHES"));
                return true;
            }
            var op = positional[1];
            if (op != "==" && op != "!="
                && !string.Equals(op, "CONTAINS", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(op, "MATCHES", StringComparison.OrdinalIgnoreCase))
            {
                errors.Add(new ScriptParseError(lineNumber,
                    $"IF operator '{op}' is not valid — use == != CONTAINS MATCHES"));
                return true;
            }
            steps.Add(new ScriptStep(lineNumber, line, ScriptStepKind.If,
                positional[0], positional[2], op.ToUpperInvariant()));
            return true;
        }

        if (string.Equals(verb, "ELSE", StringComparison.OrdinalIgnoreCase))
        {
            steps.Add(new ScriptStep(lineNumber, line, ScriptStepKind.Else, (string?)null));
            return true;
        }

        if (string.Equals(verb, "ENDIF", StringComparison.OrdinalIgnoreCase))
        {
            steps.Add(new ScriptStep(lineNumber, line, ScriptStepKind.EndIf, (string?)null));
            return true;
        }

        return false;
    }

    /// <summary>
    /// Parses the four control-flow verbs. Returns true when the verb was one of them
    /// (a step or an error has been recorded either way).
    /// </summary>
    private static bool TryParseControlFlow(string verb, List<string> positional,
        List<(string Key, string Value)> named, string line, int lineNumber,
        List<ScriptStep> steps, List<ScriptParseError> errors)
    {
        ScriptStepKind kind;
        bool wantsTarget;
        if (string.Equals(verb, "LABEL", StringComparison.OrdinalIgnoreCase))
        {
            kind = ScriptStepKind.Label;
            wantsTarget = true;
        }
        else if (string.Equals(verb, "GOTO", StringComparison.OrdinalIgnoreCase))
        {
            kind = ScriptStepKind.Goto;
            wantsTarget = true;
        }
        else if (string.Equals(verb, "GOSUB", StringComparison.OrdinalIgnoreCase))
        {
            kind = ScriptStepKind.Gosub;
            wantsTarget = true;
        }
        else if (string.Equals(verb, "RETURN", StringComparison.OrdinalIgnoreCase))
        {
            kind = ScriptStepKind.Return;
            wantsTarget = false;
        }
        else
        {
            return false;
        }

        if (named.Count > 0)
        {
            errors.Add(new ScriptParseError(lineNumber, $"{verb.ToUpperInvariant()} takes no key=value arguments"));
            return true;
        }

        if (wantsTarget)
        {
            if (positional.Count != 1 || positional[0].Length == 0)
            {
                errors.Add(new ScriptParseError(lineNumber, $"{verb.ToUpperInvariant()} needs exactly one label name"));
                return true;
            }
            steps.Add(new ScriptStep(lineNumber, line, kind, positional[0]));
        }
        else
        {
            if (positional.Count != 0)
            {
                errors.Add(new ScriptParseError(lineNumber, "RETURN takes no arguments"));
                return true;
            }
            steps.Add(new ScriptStep(lineNumber, line, kind, null));
        }
        return true;
    }

    private static bool HasParameter(ISessionCommand command, string name)
    {
        var parameters = command.Parameters;
        for (int i = 0; i < parameters.Count; i++)
        {
            if (string.Equals(parameters[i].Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Splits one line into verb + positional values + key=value pairs.
    /// Handles quoted strings ("..." with \" and \\ escapes), bare words, an inline
    /// trailing comment (# ...), and key="quoted value".
    /// </summary>
    private static bool TryTokenize(string line, int lineNumber, List<ScriptParseError> errors,
        out string verb, out List<string> positional, out List<(string Key, string Value)> named)
    {
        verb = string.Empty;
        positional = new List<string>();
        named = new List<(string, string)>();

        int pos = 0;
        bool first = true;

        while (pos < line.Length)
        {
            // Skip whitespace between tokens.
            while (pos < line.Length && char.IsWhiteSpace(line[pos])) pos++;
            if (pos >= line.Length) break;

            // Inline comment: rest of line ignored (only between tokens, never inside quotes).
            if (line[pos] == '#') break;

            string token;
            bool wasQuoted;
            if (!TryReadValue(line, ref pos, lineNumber, errors, out token, out wasQuoted))
            {
                return false;
            }

            if (first)
            {
                if (wasQuoted)
                {
                    errors.Add(new ScriptParseError(lineNumber, "A line must start with a command verb, not a quoted string"));
                    return false;
                }
                verb = token;
                first = false;
                continue;
            }

            // key=value? Only when the bare token contains '=' AND the part before it
            // is a plain identifier — a quoted token is always positional, and tokens
            // like `==` or `!=` (IF operators) must never be split.
            int eq = wasQuoted ? -1 : token.IndexOf('=');
            if (eq > 0 && IsIdentifier(token, eq))
            {
                var key = token.Substring(0, eq);
                var valuePart = token.Substring(eq + 1);
                if (valuePart.Length == 0 && pos < line.Length && line[pos] == '"')
                {
                    // Form key="quoted value": the tokenizer stopped at the quote.
                    if (!TryReadValue(line, ref pos, lineNumber, errors, out valuePart, out _))
                    {
                        return false;
                    }
                }
                named.Add((key, valuePart));
            }
            else
            {
                positional.Add(token);
            }
        }

        if (first)
        {
            errors.Add(new ScriptParseError(lineNumber, "Empty command line"));
            return false;
        }
        return true;
    }

    /// <summary>
    /// True when text[0..length) is letters/digits/underscore starting with a letter or underscore.
    /// </summary>
    private static bool IsIdentifier(string text, int length)
    {
        if (length <= 0) return false;
        for (int i = 0; i < length; i++)
        {
            char c = text[i];
            bool ok = (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || c == '_'
                      || (i > 0 && c >= '0' && c <= '9');
            if (!ok) return false;
        }
        return true;
    }

    /// <summary>
    /// Reads one value at pos: a quoted string (with escapes) or a bare word.
    /// A bare word stops at whitespace or at an opening quote (so key=" parses as
    /// 'key=' followed by the quoted value).
    /// </summary>
    private static bool TryReadValue(string line, ref int pos, int lineNumber,
        List<ScriptParseError> errors, out string value, out bool wasQuoted)
    {
        value = string.Empty;
        wasQuoted = false;

        if (line[pos] == '"')
        {
            wasQuoted = true;
            pos++; // consume opening quote
            var sb = new StringBuilder();
            while (pos < line.Length)
            {
                char c = line[pos];
                if (c == '\\')
                {
                    // Escapes go through the ONE shared table (ScriptStringEscapes) so
                    // the notation can never drift from the MCP surface: \" \\ \r \n \t
                    // \e (ESC) \xHH (hex byte) \NNN (octal). SEND appends NOTHING on its
                    // own — a CR is written explicitly as \r.
                    if (!ScriptStringEscapes.TryDecodeEscape(line, pos, sb, out int consumed, out var escError))
                    {
                        errors.Add(new ScriptParseError(lineNumber, escError!));
                        return false;
                    }
                    pos += consumed;
                    continue;
                }
                if (c == '"')
                {
                    pos++; // consume closing quote
                    value = sb.ToString();
                    return true;
                }
                sb.Append(c);
                pos++;
            }
            errors.Add(new ScriptParseError(lineNumber, "Unterminated quoted string"));
            return false;
        }

        int start = pos;
        while (pos < line.Length && !char.IsWhiteSpace(line[pos]) && line[pos] != '"')
        {
            pos++;
        }
        value = line.Substring(start, pos - start);
        return true;
    }

    /// <summary>
    /// The bool spellings CommandArgs.GetBool accepts. Delegates to the shared table in
    /// CommandParameterValidation so the parse-time check here and the dispatch-time
    /// check every other surface goes through cannot drift apart — this used to be a
    /// second copy of the list.
    /// </summary>
    private static bool IsBoolValue(string v) => CommandParameterValidation.IsBoolValue(v);
}
