namespace RetroTerm.Core.Logging;

/// <summary>
/// The human-readable identity of one escape / control sequence, as produced by
/// <see cref="EscapeSequenceDecoder"/>.
/// </summary>
/// <remarks>
/// A struct so that decoding a sequence in the trace path costs one stack value plus the
/// strings it references (which are compile-time literals for everything except the
/// argument description).
/// </remarks>
public readonly struct DecodedSequence
{
    /// <summary>
    /// Short standard mnemonic, e.g. "CUP", "SGR", "ED". Never null.
    /// </summary>
    public string Mnemonic { get; }

    /// <summary>
    /// Full name, e.g. "Cursor Position". Never null.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Decoded arguments, e.g. "row=7 col=3" or "Reset attributes". May be empty.
    /// </summary>
    public string Arguments { get; }

    /// <summary>
    /// Printable form of the sequence as it appeared on the wire, e.g. <c>ESC[7;3H</c>.
    /// </summary>
    public string Rendered { get; }

    /// <summary>
    /// False when the decoder did not recognise the sequence. Callers log these at
    /// <see cref="LogLevel.Warn"/> so gaps in emulator coverage are obvious.
    /// </summary>
    public bool IsKnown { get; }

    /// <summary>
    /// Creates a decoded sequence record.
    /// </summary>
    /// <param name="mnemonic">
    /// Short standard mnemonic.
    /// </param>
    /// <param name="name">
    /// Full name.
    /// </param>
    /// <param name="arguments">
    /// Decoded argument description.
    /// </param>
    /// <param name="rendered">
    /// Printable wire form.
    /// </param>
    /// <param name="isKnown">
    /// Whether the sequence was recognised.
    /// </param>
    public DecodedSequence(string mnemonic, string name, string arguments, string rendered, bool isKnown)
    {
        Mnemonic = mnemonic ?? string.Empty;
        Name = name ?? string.Empty;
        Arguments = arguments ?? string.Empty;
        Rendered = rendered ?? string.Empty;
        IsKnown = isKnown;
    }

    /// <summary>
    /// Renders the sequence as one aligned log line body, e.g.
    /// <c>ESC[7;3H     CUP    Cursor Position - row=7 col=3</c>.
    /// </summary>
    /// <returns>
    /// The formatted description.
    /// </returns>
    public override string ToString()
    {
        var wire = Rendered.Length >= 16 ? Rendered : Rendered.PadRight(16);
        var mn = Mnemonic.Length >= 8 ? Mnemonic : Mnemonic.PadRight(8);

        if (Arguments.Length == 0)
            return wire + mn + Name;

        return wire + mn + Name + " - " + Arguments;
    }
}
