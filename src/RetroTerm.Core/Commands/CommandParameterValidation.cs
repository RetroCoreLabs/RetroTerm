using System;
using System.Globalization;

namespace RetroTerm.Core.Commands;

/// <summary>
/// The one place that decides whether a set of arguments is acceptable for a command.
///
/// WHY THIS IS SHARED. Commands are reachable through more than one front end — the
/// script DSL and the MCP tool provider today, more later. A rule implemented at one
/// call site holds on that surface only, which is how SEND ended up decoding <c>\r</c>
/// in scripts while shipping it as two literal characters over MCP for weeks.
///
/// The script parser calls this at PARSE time, so the editor can underline the line
/// before anything runs. <see cref="CommandRegistry.ExecuteAsync"/> calls it at DISPATCH
/// time, which catches every other surface — including ones that do not exist yet — and
/// catches script values that only become wrong after variable expansion.
///
/// Running it twice on the script path is deliberate and free: the checks are a handful
/// of comparisons over a parameter list that is never longer than a few entries.
/// </summary>
public static class CommandParameterValidation
{
    /// <summary>
    /// Checks that every required parameter is present and that every supplied value
    /// matches its declared type.
    ///
    /// A value containing '$' is skipped: in a script that is a variable reference which
    /// is substituted at run time, so its final form is unknowable here. After expansion
    /// the dispatch-time call sees the real value and checks it properly.
    /// </summary>
    /// <param name="command">
    /// The command whose parameter metadata defines the rules.
    /// </param>
    /// <param name="args">
    /// The arguments as supplied by whichever surface built them.
    /// </param>
    /// <param name="error">
    /// Human-readable explanation naming the offending parameter.
    /// </param>
    /// <returns>
    /// True when the arguments are acceptable.
    /// </returns>
    public static bool TryValidate(ISessionCommand command, CommandArgs args, out string? error)
    {
        if (command == null) throw new ArgumentNullException(nameof(command));
        args ??= CommandArgs.Empty;
        error = null;

        var parameters = command.Parameters;

        // Required parameters first: a missing one makes any type complaint about the
        // others less useful, and "you forgot X" is the more actionable message.
        for (int i = 0; i < parameters.Count; i++)
        {
            var p = parameters[i];
            if (p.Required && !args.Contains(p.Name))
            {
                error = $"Command '{command.Name}' is missing required parameter '{p.Name}' ({p.Description})";
                return false;
            }
        }

        // Type checks. Without these, CommandArgs.GetInt/GetBool fall back to their
        // default on an unparseable value and the caller is never told: a script written
        // as `timeout=300/ontimeout=wake` (a slash typo) ran with the default timeout and
        // no jump, and an MCP client passing timeout="soon" got the same silence.
        for (int i = 0; i < parameters.Count; i++)
        {
            var p = parameters[i];
            var raw = args.GetString(p.Name);
            if (raw == null || raw.IndexOf('$') >= 0)
            {
                continue;
            }

            if (p.Type == CommandParameterType.Int
                && !int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
            {
                error = $"{command.Name}: '{p.Name}' must be a whole number, got '{raw}'";
                return false;
            }

            if (p.Type == CommandParameterType.Bool && !IsBoolValue(raw))
            {
                error = $"{command.Name}: '{p.Name}' must be true/false (or 1/0, on/off, yes/no), got '{raw}'";
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// The bool spellings <see cref="CommandArgs.GetBool"/> accepts. Kept here so the
    /// validator and the reader cannot drift apart — a value this rejects must never be
    /// one GetBool would have understood.
    /// </summary>
    public static bool IsBoolValue(string value)
    {
        if (value == null) return false;
        return bool.TryParse(value, out _)
            || value == "1" || value == "0"
            || string.Equals(value, "on", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "off", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "no", StringComparison.OrdinalIgnoreCase);
    }
}
