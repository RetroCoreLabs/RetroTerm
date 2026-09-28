using System;
using System.Collections.Generic;
using System.Globalization;

namespace RetroTerm.Core.Commands;

/// <summary>
/// Named arguments for a command call. All values travel as text — the script DSL is
/// text, MCP JSON params arrive as text-convertible values — and commands read them
/// through the typed getters. Names are case-insensitive.
/// </summary>
public sealed class CommandArgs
{
    public static readonly CommandArgs Empty = new CommandArgs();

    private readonly Dictionary<string, string> _values;

    public CommandArgs()
    {
        _values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }

    public CommandArgs(IReadOnlyDictionary<string, string> values) : this()
    {
        if (values == null) throw new ArgumentNullException(nameof(values));
        // Manual copy (no LINQ). Enumerator use is unavoidable for a dictionary source;
        // this is command dispatch, not a hot path.
        var e = values.GetEnumerator();
        while (e.MoveNext())
        {
            _values[e.Current.Key] = e.Current.Value;
        }
    }

    /// <summary>
    /// Sets one argument; returns this for chaining when building calls in code.
    /// </summary>
    public CommandArgs Set(string name, string value)
    {
        _values[name] = value;
        return this;
    }

    /// <summary>
    /// Returns a copy with every VALUE run through the expander — how script variables
    /// ($name) get substituted into command arguments at execution time. Keys are
    /// never expanded.
    /// </summary>
    public CommandArgs ExpandWith(Func<string, string> expander)
    {
        if (expander == null) throw new ArgumentNullException(nameof(expander));
        var expanded = new CommandArgs();
        var e = _values.GetEnumerator();
        while (e.MoveNext())
        {
            expanded._values[e.Current.Key] = expander(e.Current.Value);
        }
        return expanded;
    }

    public bool Contains(string name) => _values.ContainsKey(name);

    public bool TryGet(string name, out string value) => _values.TryGetValue(name, out value!);

    public string? GetString(string name, string? defaultValue = null)
    {
        return _values.TryGetValue(name, out var v) ? v : defaultValue;
    }

    public int GetInt(string name, int defaultValue)
    {
        if (_values.TryGetValue(name, out var v)
            && int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
        {
            return parsed;
        }
        return defaultValue;
    }

    public bool GetBool(string name, bool defaultValue)
    {
        if (_values.TryGetValue(name, out var v))
        {
            if (bool.TryParse(v, out var parsed))
            {
                return parsed;
            }
            // Script-friendly forms: 1/0, on/off, yes/no.
            if (v == "1" || string.Equals(v, "on", StringComparison.OrdinalIgnoreCase)
                || string.Equals(v, "yes", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
            if (v == "0" || string.Equals(v, "off", StringComparison.OrdinalIgnoreCase)
                || string.Equals(v, "no", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }
        return defaultValue;
    }
}
