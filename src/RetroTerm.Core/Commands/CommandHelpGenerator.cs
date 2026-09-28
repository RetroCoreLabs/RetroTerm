using System;
using System.Text;

namespace RetroTerm.Core.Commands;

/// <summary>
/// Generates command help from the live registry metadata. This is the ONLY source of
/// command documentation — the HELP script verb, the MCP terminal_help tool and the
/// script editor's help panel all render what this produces. Because CommandRegistry
/// refuses commands without docs, everything listed here is guaranteed documented.
/// </summary>
public static class CommandHelpGenerator
{
    /// <summary>
    /// One-line-per-command overview: NAME — summary, followed by the script
    /// LANGUAGE level (labels, variables, IF, flags, escapes) so a HELP in the
    /// console lists the same things the editor's command panel shows.
    /// </summary>
    public static string GenerateOverview(CommandRegistry registry)
    {
        if (registry == null) throw new ArgumentNullException(nameof(registry));

        var sb = new StringBuilder();
        var commands = registry.Commands;
        for (int i = 0; i < commands.Count; i++)
        {
            var c = commands[i];
            sb.Append(c.Name).Append(" — ").Append(c.Summary).Append('\n');
        }

        sb.Append("\nScript language (parser/runner level, not commands):\n");
        var language = Scripting.ScriptLanguageHelp.Entries;
        for (int i = 0; i < language.Count; i++)
        {
            sb.Append(language[i].Verb).Append(" — ").Append(language[i].Help).Append('\n');
        }
        sb.Append("\nHELP <name> shows parameters and an example for any of these.\n");
        return sb.ToString();
    }

    /// <summary>
    /// Full help for one command: summary, every parameter with type/required/default,
    /// and the usage example.
    /// </summary>
    public static string GenerateFor(ISessionCommand command)
    {
        if (command == null) throw new ArgumentNullException(nameof(command));

        var sb = new StringBuilder();
        sb.Append(command.Name).Append(" — ").Append(command.Summary).Append('\n');

        var parameters = command.Parameters;
        if (parameters.Count > 0)
        {
            sb.Append("Parameters:\n");
            for (int i = 0; i < parameters.Count; i++)
            {
                var p = parameters[i];
                sb.Append("  ").Append(p.Name)
                  .Append(" (").Append(TypeName(p.Type));
                if (p.Required)
                {
                    sb.Append(", required");
                }
                else if (p.DefaultValue != null)
                {
                    sb.Append(", default ").Append(p.DefaultValue);
                }
                sb.Append(") — ").Append(p.Description).Append('\n');
            }
        }
        else
        {
            sb.Append("No parameters.\n");
        }

        sb.Append("Example: ").Append(command.Example).Append('\n');
        return sb.ToString();
    }

    /// <summary>
    /// Help for one command by name, or the overview plus an error line when the name
    /// is unknown — a HELP call should never come back empty-handed.
    /// </summary>
    public static string GenerateFor(CommandRegistry registry, string name)
    {
        if (registry == null) throw new ArgumentNullException(nameof(registry));

        if (registry.TryGet(name, out var command))
        {
            return GenerateFor(command);
        }
        // Language-level words (LABEL, IF, ontimeout=, escapes...) are not registry
        // commands but HELP must know them too.
        var entry = Scripting.ScriptLanguageHelp.Find(name);
        if (entry != null)
        {
            return $"{entry.Help}\nExample: {entry.Example}\n";
        }
        return $"Unknown command '{name}'. Available commands:\n" + GenerateOverview(registry);
    }

    private static string TypeName(CommandParameterType type)
    {
        return type switch
        {
            CommandParameterType.Int => "int",
            CommandParameterType.Bool => "bool",
            _ => "string"
        };
    }
}
