namespace RetroTerm.Core.Terminal.Buffer;

/// <summary>
/// How a terminal's colours are PRESENTED, as distinct from what they mean.
///
/// <see cref="TerminalPalette"/> is canonical: it says what index 2 IS — green, the xterm value,
/// the number a host asking for <c>SGR 32</c> is entitled to assume. A theme sits on top and says
/// what actually gets drawn on this screen. The two are kept apart deliberately, because a screen
/// read back over MCP or a script must report the colour the host asked for, not the colour a
/// phosphor theme happened to paint.
///
/// The default theme changes nothing at all: <see cref="TryGetBaseColour"/> declines every index
/// and the renderer falls through to the canonical palette, exactly as it did before themes
/// existed.
///
/// A MONOCHROME theme is the interesting case. A real amber or green terminal had one phosphor and
/// no way to show a second hue, so a host's sixteen colours arrived as sixteen BRIGHTNESSES. That
/// is what <see cref="Monochrome"/> reproduces: each canonical colour is measured for how bright it
/// is and redrawn at that brightness in the screen's own colour. Red and green stop being different
/// hues and become different intensities, which is what the hardware did.
/// </summary>
public sealed class TerminalTheme
{
    /// <summary>
    /// Name shown to the user. Not used for anything but display.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Colour of text a host has not coloured.
    /// </summary>
    public (byte R, byte G, byte B) DefaultForeground { get; }

    /// <summary>
    /// Colour of the screen behind it.
    /// </summary>
    public (byte R, byte G, byte B) DefaultBackground { get; }

    /// <summary>
    /// Whether the sixteen base colours are collapsed onto this screen's single phosphor.
    /// False for every colour theme, so they draw the canonical palette untouched.
    /// </summary>
    public bool IsMonochrome { get; }

    private TerminalTheme(string name, (byte, byte, byte) foreground, (byte, byte, byte) background,
        bool isMonochrome)
    {
        Name = name;
        DefaultForeground = foreground;
        DefaultBackground = background;
        IsMonochrome = isMonochrome;
    }

    /// <summary>
    /// A theme that sets the default colours and leaves the sixteen base colours alone — what
    /// every existing colour preset does today.
    /// </summary>
    public static TerminalTheme Colour(string name, (byte R, byte G, byte B) foreground,
        (byte R, byte G, byte B) background)
        => new TerminalTheme(name, foreground, background, isMonochrome: false);

    /// <summary>
    /// A theme that collapses the sixteen base colours onto one phosphor, the way a single-gun CRT
    /// had no choice but to.
    /// </summary>
    public static TerminalTheme Monochrome(string name, (byte R, byte G, byte B) phosphor,
        (byte R, byte G, byte B) background)
        => new TerminalTheme(name, phosphor, background, isMonochrome: true);

    /// <summary>
    /// The colour this theme draws for an arbitrary RGB value, or false to draw it unchanged.
    /// </summary>
    /// <remarks>
    /// <para><b>For pixels rather than palette entries</b></para>
    /// <see cref="TryGetBaseColour"/> answers for the sixteen text colours, which are indexes. A
    /// graphics protocol has no indexes to look up - Sixel and ReGIS both carry real RGB - so this
    /// is the same question asked about a colour instead of a number.
    ///
    /// <para><b>Why graphics collapse too</b></para>
    /// A single-phosphor screen has one gun. It could not show a blue line whatever the host asked
    /// for; it showed a dim one. Collapsing the text and leaving the drawings in colour would put
    /// a picture on that screen the hardware could not have produced.
    /// </remarks>
    /// <param name="r">
    /// Red component of the colour the host asked for.
    /// </param>
    /// <param name="g">
    /// Green component.
    /// </param>
    /// <param name="b">
    /// Blue component.
    /// </param>
    /// <param name="colour">
    /// Receives the colour this theme draws, or an undefined value when the method returns false.
    /// </param>
    /// <returns>
    /// True when this theme replaces the colour; false to draw it as the host sent it.
    /// </returns>
    public bool TryGetPhosphorColour(byte r, byte g, byte b, out (byte R, byte G, byte B) colour)
    {
        if (!IsMonochrome)
        {
            colour = default;
            return false;
        }

        colour = ScaleToPhosphor(Brightness(r, g, b));
        return true;
    }

    /// <summary>
    /// The colour this theme draws for a palette index, or false to use the canonical palette.
    /// </summary>
    /// <param name="index">
    /// Palette index. Only 0-15 are ever themed; the 6x6x6 cube and the grey ramp are addressed by
    /// their exact RGB, and remapping those would be a lie about what the host asked for.
    /// </param>
    /// <param name="colour">
    /// Receives the colour this theme draws for <paramref name="index"/>, or an undefined value
    /// when the method returns false.
    /// </param>
    /// <returns>
    /// True when this theme replaces the canonical colour; false to use the palette unchanged.
    /// </returns>
    public bool TryGetBaseColour(byte index, out (byte R, byte G, byte B) colour)
    {
        if (!IsMonochrome || index > 15)
        {
            colour = default;
            return false;
        }

        var (r, g, b) = TerminalPalette.GetRgb(index);
        colour = ScaleToPhosphor(Brightness(r, g, b));
        return true;
    }

    /// <summary>
    /// How bright a colour is, 0 to 1, by the ITU-R BT.601 luma weights.
    ///
    /// Those weights, not a flat average: the eye is far more sensitive to green than to blue, so
    /// an average would make blue text on an amber screen almost as bright as green text, when on
    /// real hardware blue was the dimmest thing on the display.
    /// </summary>
    private static double Brightness(byte r, byte g, byte b)
        => (0.299 * r + 0.587 * g + 0.114 * b) / 255.0;

    /// <summary>
    /// Paints a brightness in this theme's phosphor, blending from the background rather than
    /// simply scaling the phosphor towards black.
    ///
    /// Blending from the background is what keeps a dim colour readable: an amber screen's
    /// background is not black but a very dark brown, and scaling towards black would put dim text
    /// BELOW the background it sits on.
    /// </summary>
    private (byte R, byte G, byte B) ScaleToPhosphor(double brightness)
    {
        if (brightness < 0.0) brightness = 0.0;
        if (brightness > 1.0) brightness = 1.0;

        var (br, bg, bb) = DefaultBackground;
        var (fr, fg, fb) = DefaultForeground;

        return (
            (byte)(br + (fr - br) * brightness),
            (byte)(bg + (fg - bg) * brightness),
            (byte)(bb + (fb - bb) * brightness));
    }
}
