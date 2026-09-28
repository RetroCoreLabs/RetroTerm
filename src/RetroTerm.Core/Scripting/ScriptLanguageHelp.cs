using System;
using System.Collections.Generic;

namespace RetroTerm.Core.Scripting;

/// <summary>
/// One language-level help entry: the verb/flag, what it does, an example line.
/// </summary>
public sealed class ScriptLanguageHelpEntry
{
    public string Verb { get; }
    public string Help { get; }
    public string Example { get; }

    public ScriptLanguageHelpEntry(string verb, string help, string example)
    {
        Verb = verb;
        Help = help;
        Example = example;
    }
}

/// <summary>
/// Help for the LANGUAGE level of the .rts DSL — the things the parser/runner
/// interpret that are NOT registry commands: labels and jumps, variables, IF blocks,
/// comments, step flags and string escapes. THE single source: the HELP command,
/// the MCP terminal_help tool and the script editor's help panel all read this list,
/// so the console and the editor can never disagree about what the language has.
/// </summary>
public static class ScriptLanguageHelp
{
    public static readonly IReadOnlyList<ScriptLanguageHelpEntry> Entries = new[]
    {
        new ScriptLanguageHelpEntry("LABEL",
            "LABEL name — declares a jump target; executing it does nothing.",
            "LABEL retry"),
        new ScriptLanguageHelpEntry("GOTO",
            "GOTO name — jump to a label, no return.",
            "GOTO retry"),
        new ScriptLanguageHelpEntry("GOSUB",
            "GOSUB name — jump to a label; RETURN comes back to the next step. Nests.",
            "GOSUB login"),
        new ScriptLanguageHelpEntry("RETURN",
            "RETURN — back to the step after the most recent GOSUB.",
            "RETURN"),
        new ScriptLanguageHelpEntry("SET",
            "SET name value — assign a variable. Use $name / ${name} in any argument; $$ is a literal dollar.",
            "SET user \"SYSTEM\""),
        new ScriptLanguageHelpEntry("IF",
            "IF left op right — run the block when true. Operators: == != CONTAINS MATCHES (regex). Nests; close with ENDIF.",
            "IF $mode == \"PROD\""),
        new ScriptLanguageHelpEntry("ELSE",
            "ELSE — the alternative block of the innermost IF.",
            "ELSE"),
        new ScriptLanguageHelpEntry("ENDIF",
            "ENDIF — closes an IF block.",
            "ENDIF"),
        new ScriptLanguageHelpEntry("# comment",
            "Lines starting with # are comments; # after a step starts an inline comment.",
            "# log in first"),
        new ScriptLanguageHelpEntry("\\ escapes",
            "Quoted strings decode: \\\" \\\\ \\r (CR) \\n (LF) \\t (TAB) \\e (ESC) \\xHH (hex) \\NNN (octal). SEND appends NOTHING — write the CR yourself.",
            "SEND \"LIST-FILES\\r\""),
        new ScriptLanguageHelpEntry("optional=",
            "optional=true on any step: its failure does not stop the script.",
            "WAITFOR \"maybe\" timeout=500 optional=true"),
        new ScriptLanguageHelpEntry("ontimeout=",
            "ontimeout=label on WAITFOR/WAITIDLE: a timeout BRANCHES to the label instead of failing (retry loops).",
            "WAITFOR \"ENTER\" timeout=2000 ontimeout=retry"),
        new ScriptLanguageHelpEntry("into=",
            "into=var on any step stores its capture: WAITFOR stores the match (regex group 1), READ*/STATUS store their output.",
            "WAITFOR \"VERSION ([A-Z])\" regex=true into=ver")
    };

    /// <summary>
    /// Finds an entry by name, forgiving about how the user typed it: case-insensitive,
    /// with or without a trailing '=' (HELP ontimeout finds "ontimeout="), and the
    /// display names' first token ("#" finds "# comment", "\\" finds "\\ escapes").
    /// </summary>
    public static ScriptLanguageHelpEntry? Find(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return null;
        }
        for (int i = 0; i < Entries.Count; i++)
        {
            var verb = Entries[i].Verb;
            if (string.Equals(verb, name, StringComparison.OrdinalIgnoreCase))
            {
                return Entries[i];
            }
            // "ontimeout" matches "ontimeout=", "#" matches "# comment", "\" matches "\ escapes"
            if (verb.Length > name.Length
                && string.Compare(verb, 0, name, 0, name.Length, StringComparison.OrdinalIgnoreCase) == 0
                && (verb[name.Length] == '=' || verb[name.Length] == ' '))
            {
                return Entries[i];
            }
        }
        return null;
    }
}
