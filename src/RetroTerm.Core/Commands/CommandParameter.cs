//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs/RetroTerm
// RetroTerm — Connecting today's machines to yesterday's systems
//

using System;
using System.Collections.Generic;

namespace RetroTerm.Core.Commands;

/// <summary>
/// Value type of a command parameter — used for validation and for generated help.
/// </summary>
public enum CommandParameterType
{
    String,
    Int,
    Bool
}

/// <summary>
/// Self-describing metadata for one command parameter. Every parameter carries its own
/// documentation: the help system is GENERATED from this, never hand-written, so a
/// command cannot exist without docs (CommandRegistry validates on registration).
/// </summary>
public sealed class CommandParameter
{
    /// <summary>
    /// Parameter name, case-insensitive at lookup time.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Value type, for validation and help.
    /// </summary>
    public CommandParameterType Type { get; }

    /// <summary>
    /// Whether the caller must supply this parameter.
    /// </summary>
    public bool Required { get; }

    /// <summary>
    /// Default value as text when not required (shown in help). Null = no default.
    /// </summary>
    public string? DefaultValue { get; }

    /// <summary>
    /// One-line description — mandatory, enforced by CommandRegistry.
    /// </summary>
    public string Description { get; }

    /// <summary>
    /// True when this string parameter carries the backslash-escape notation
    /// (<c>\r \n \t \e \xHH \NNN \\ \"</c>) — SEND's <c>text</c> is the one that does.
    /// The script parser already decodes escapes inside a quoted string literal, but
    /// surfaces WITHOUT a quoting layer (MCP tool arguments) hand the value straight
    /// to the command; those surfaces must run flagged values through
    /// <see cref="Scripting.ScriptStringEscapes.TryDecode"/> first, or <c>\r</c> would
    /// reach the wire as two literal characters instead of a carriage return.
    /// Regex-bearing parameters (WAITFOR's pattern) are deliberately NOT flagged, so a
    /// pattern like <c>\d+</c> is never mangled.
    /// </summary>
    public bool DecodeEscapes { get; }

    /// <summary>
    /// The only words this string parameter accepts, or null when any text is accepted.
    /// </summary>
    /// <remarks>
    /// Declared HERE, on the metadata, so the script parser (at parse time), the dispatch-time
    /// check in <see cref="CommandRegistry.ExecuteAsync"/> and the MCP tool list (as a JSON
    /// Schema enum) all read the same list - the two-surface rule. Compared without regard to
    /// case. A word outside the list is refused by name, never mapped to the default: a
    /// SCREENSHOT with <c>area=windwo</c> must not quietly save the wrong picture.
    /// </remarks>
    public IReadOnlyList<string>? AllowedValues { get; }

    public CommandParameter(string name, CommandParameterType type, bool required,
        string? defaultValue, string description, bool decodeEscapes = false,
        IReadOnlyList<string>? allowedValues = null)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        Type = type;
        Required = required;
        DefaultValue = defaultValue;
        Description = description ?? throw new ArgumentNullException(nameof(description));
        DecodeEscapes = decodeEscapes;
        AllowedValues = allowedValues;
    }

    /// <summary>
    /// Whether a value is one of <see cref="AllowedValues"/>, ignoring case. Always true when
    /// the parameter declares no list.
    /// </summary>
    /// <param name="value">
    /// The value as supplied.
    /// </param>
    /// <returns>
    /// True when the value is acceptable.
    /// </returns>
    public bool IsAllowedValue(string value)
    {
        var allowed = AllowedValues;
        if (allowed == null) return true;
        for (int i = 0; i < allowed.Count; i++)
        {
            if (string.Equals(allowed[i], value, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }
}
