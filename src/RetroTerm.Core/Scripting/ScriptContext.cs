using System;
using System.Collections.Generic;
using System.Text;

namespace RetroTerm.Core.Scripting;

/// <summary>
/// Variables for a script run (or a console session — the console keeps one context
/// alive across commands, so SET there persists).
///
/// Expansion syntax in argument values: <c>$name</c> or <c>${name}</c>; <c>$$</c> is a
/// literal dollar. Names are letters, digits and underscore, case-insensitive.
/// An unknown variable expands to the empty string (shell-style).
/// </summary>
public sealed class ScriptContext
{
    private readonly Dictionary<string, string> _variables =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Sets a variable (creates or overwrites).
    /// </summary>
    public void Set(string name, string value)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Variable name must be set", nameof(name));
        _variables[name] = value ?? string.Empty;
    }

    /// <summary>
    /// The variable's value, or empty string when not set.
    /// </summary>
    public string Get(string name)
    {
        return _variables.TryGetValue(name, out var value) ? value : string.Empty;
    }

    public bool Contains(string name) => _variables.ContainsKey(name);

    /// <summary>
    /// Variable names currently set (for console diagnostics).
    /// </summary>
    public IReadOnlyCollection<string> Names => _variables.Keys;

    /// <summary>
    /// Expands $name / ${name} / $$ in a text. Runs at step EXECUTION time, not parse
    /// time — variables set earlier in the run are visible.
    /// </summary>
    public string Expand(string text)
    {
        if (text == null || text.IndexOf('$') < 0)
        {
            return text ?? string.Empty;
        }

        var sb = new StringBuilder(text.Length + 16);
        int i = 0;
        while (i < text.Length)
        {
            char c = text[i];
            if (c != '$')
            {
                sb.Append(c);
                i++;
                continue;
            }

            // '$' at end of text: literal.
            if (i + 1 >= text.Length)
            {
                sb.Append('$');
                break;
            }

            char next = text[i + 1];
            if (next == '$')
            {
                sb.Append('$'); // $$ → literal $
                i += 2;
                continue;
            }

            if (next == '{')
            {
                int close = text.IndexOf('}', i + 2);
                if (close > i + 2)
                {
                    sb.Append(Get(text.Substring(i + 2, close - i - 2)));
                    i = close + 1;
                    continue;
                }
                sb.Append(c); // unterminated ${ — literal
                i++;
                continue;
            }

            if (IsNameChar(next))
            {
                int start = i + 1;
                int end = start;
                while (end < text.Length && IsNameChar(text[end]))
                {
                    end++;
                }
                sb.Append(Get(text.Substring(start, end - start)));
                i = end;
                continue;
            }

            sb.Append('$'); // $ followed by something else — literal
            i++;
        }
        return sb.ToString();
    }

    private static bool IsNameChar(char c)
        => (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '_';
}
