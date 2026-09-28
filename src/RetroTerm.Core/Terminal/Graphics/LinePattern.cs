namespace RetroTerm.Core.Terminal.Graphics;

/// <summary>
/// The dash patterns a vector can be drawn with.
/// </summary>
/// <remarks>
/// <para><b>Where the names come from</b></para>
/// The "4010/4014 Mode" chapter of the VT330/VT340 Graphics Programming manual, "Select Vector
/// Patterns": <c>ESC `</c> solid, <c>ESC a</c> dotted, <c>ESC b</c> dot-dash, <c>ESC c</c> short
/// dash, <c>ESC d</c> long dash, and <c>ESC e</c> through <c>ESC g</c> solid again. The names here
/// are the manual's own words.
///
/// <para><b>The BIT PATTERNS are chosen, and that is marked because it is a choice</b></para>
/// The manual names each pattern and does not print its dash lengths, so the sixteen-bit masks in
/// <see cref="LinePatternMask"/> are this emulator's reading of those names rather than DEC's
/// numbers. They are written in binary so the shape can be read straight off the source, and each
/// tiles evenly at sixteen pixels. If a document ever turns up giving the real lengths, that one
/// table is the only thing to change.
/// </remarks>
public enum LinePattern
{
    /// <summary>
    /// An unbroken line. The power-on pattern.
    /// </summary>
    Solid = 0,

    /// <summary>
    /// Single pixels alternating with single gaps.
    /// </summary>
    Dotted = 1,

    /// <summary>
    /// A long dash, a gap, a dot, a gap.
    /// </summary>
    DotDash = 2,

    /// <summary>
    /// Two on, two off.
    /// </summary>
    ShortDash = 3,

    /// <summary>
    /// Four on, four off.
    /// </summary>
    LongDash = 4,
}

/// <summary>
/// Turns a <see cref="LinePattern"/> into the sixteen-bit mask a line walk steps through.
/// </summary>
public static class LinePatternMask
{
    /// <summary>
    /// How many pixels one turn of a mask covers.
    /// </summary>
    public const int Period = 16;

    /// <summary>
    /// The mask for a pattern, bit 0 first.
    /// </summary>
    /// <param name="pattern">
    /// The pattern to look up. Anything unrecognised draws solid, because a line nobody can see is
    /// a worse answer than a line in the wrong style.
    /// </param>
    /// <returns>
    /// Sixteen bits; a set bit lights that pixel.
    /// </returns>
    public static ushort For(LinePattern pattern)
    {
        switch (pattern)
        {
            case LinePattern.Dotted: return 0b1010101010101010;
            case LinePattern.DotDash: return 0b1111000010000000;
            case LinePattern.ShortDash: return 0b1100110011001100;
            case LinePattern.LongDash: return 0b1111000011110000;
            default: return 0xFFFF;
        }
    }
}
