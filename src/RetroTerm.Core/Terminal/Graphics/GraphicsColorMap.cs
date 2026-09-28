using System;

namespace RetroTerm.Core.Terminal.Graphics;

/// <summary>
/// The terminal's colour map - the one both Sixel and ReGIS write to.
/// </summary>
/// <remarks>
/// <para><b>Why this is one object and not two</b></para>
/// A VT340 has SIXTEEN output map locations, and both graphics languages address the same sixteen.
/// That is not an implementation detail, it is the feature: hackerb9's <c>cat-original.six</c> sets
/// its entire palette with ReGIS
/// <c>S(M0(H280L35S60))</c> and then sends a Sixel image that defines no colours of its own. With
/// a register file per decoder that file draws in the power-on palette and every colour is wrong.
///
/// It also keeps the HLS conversion in ONE place. It used to exist only on the Sixel side, so
/// teaching ReGIS its own <c>S(M...)</c> would have meant a second copy of the same arithmetic -
/// and a fix to one of them would silently miss the other.
///
/// <para><b>The default map is DEC's, not a guess</b></para>
/// Table 2-3, "VT340 Default Color Map", in the VT330/VT340 Programmer Reference Manual Volume 2:
/// Graphics Programming, held at
/// <c>spec\DEC\EK-VT3XX-GP-002_VT330_VT340_Graphics_Programming_May88.pdf</c>. The table gives each
/// entry in both HLS and RGB percentages; the percentages are transcribed below and scaled here, so
/// the numbers in the source can be checked against the page.
///
/// The two palettes this replaced were both wrong, and wrong differently. The Sixel one had
/// entries 9 to 14 BRIGHTER than 1 to 6, where the manual's footnote says "These colors are less
/// saturated than colors 1 through 6" and gives them lower values - 60 percent, not 89. The ReGIS
/// one used fully saturated primaries throughout, so its blue was 0,0,255 where a VT340's is
/// 20,20,80 percent.
///
/// <para><b>Sixteen entries, 256 registers</b></para>
/// The map is sixteen locations because the hardware is. The register file is 256 because that is
/// what modern Sixel encoders assume, and refusing them would refuse real images. Registers 16 and
/// above start as a repeat of the sixteen, so an undefined high register draws something visible
/// rather than nothing.
/// </remarks>
public sealed class GraphicsColorMap
{
    /// <summary>
    /// How many colour registers exist. A VT340 has 16; the rest are for modern encoders.
    /// </summary>
    public const int RegisterCount = 256;

    /// <summary>
    /// How many of them the hardware actually had, and therefore how long the default map is.
    /// </summary>
    public const int HardwareMapSize = 16;

    /// <summary>
    /// Table 2-3 as printed, three RGB percentages per entry, map location 0 first.
    /// </summary>
    /// <remarks>
    /// Kept as the manual's PERCENTAGES rather than as scaled bytes so the table can be read
    /// against the page without arithmetic. The colour names DEC gives are, in order: black, blue,
    /// red, green, magenta, cyan, yellow, gray 50 percent, gray 25 percent, then blue, red, green,
    /// magenta, cyan and yellow again less saturated, then gray 75 percent.
    /// </remarks>
    private static readonly byte[] DefaultMapPercent =
    {
         0,  0,  0,   // 0  black
        20, 20, 80,   // 1  blue
        80, 13, 13,   // 2  red
        20, 80, 20,   // 3  green
        80, 20, 80,   // 4  magenta
        20, 80, 80,   // 5  cyan
        80, 80, 20,   // 6  yellow
        53, 53, 53,   // 7  gray 50%
        26, 26, 26,   // 8  gray 25%
        33, 33, 60,   // 9  blue, less saturated
        60, 26, 26,   // 10 red, less saturated
        33, 60, 33,   // 11 green, less saturated
        60, 33, 60,   // 12 magenta, less saturated
        33, 60, 60,   // 13 cyan, less saturated
        60, 60, 33,   // 14 yellow, less saturated
        80, 80, 80,   // 15 gray 75%
    };

    private readonly GraphicsColor[] _registers = new GraphicsColor[RegisterCount];

    /// <summary>
    /// Builds a map holding the power-on colours.
    /// </summary>
    public GraphicsColorMap()
    {
        Reset();
    }

    /// <summary>
    /// Restores every register to the VT340 default map.
    /// </summary>
    public void Reset()
    {
        for (int i = 0; i < _registers.Length; i++)
        {
            int entry = (i % HardwareMapSize) * 3;

            _registers[i] = new GraphicsColor(
                ScalePercent(DefaultMapPercent[entry]),
                ScalePercent(DefaultMapPercent[entry + 1]),
                ScalePercent(DefaultMapPercent[entry + 2]));
        }
    }

    /// <summary>
    /// Reads one register.
    /// </summary>
    /// <param name="index">
    /// Register number.
    /// </param>
    /// <returns>
    /// The colour, or transparent when the number is outside the register file.
    /// </returns>
    public GraphicsColor Register(int index)
        => index >= 0 && index < _registers.Length ? _registers[index] : GraphicsColor.Transparent;

    /// <summary>
    /// True when the number names a register that exists.
    /// </summary>
    /// <param name="index">
    /// Register number.
    /// </param>
    public static bool IsRegister(int index)
        => index >= 0 && index < RegisterCount;

    /// <summary>
    /// Sets a register from red, green and blue percentages, which is how both graphics languages
    /// express an RGB colour.
    /// </summary>
    /// <param name="index">
    /// Register number. Out-of-range numbers are ignored rather than throwing: a mangled stream is
    /// not a reason to lose the rest of a picture.
    /// </param>
    /// <param name="red">
    /// Red, 0 to 100.
    /// </param>
    /// <param name="green">
    /// Green, 0 to 100.
    /// </param>
    /// <param name="blue">
    /// Blue, 0 to 100.
    /// </param>
    public void SetFromRgbPercent(int index, int red, int green, int blue)
    {
        if (!IsRegister(index)) return;

        _registers[index] = new GraphicsColor(
            ScalePercent(red), ScalePercent(green), ScalePercent(blue));
    }

    /// <summary>
    /// Sets a register from hue, lightness and saturation.
    /// </summary>
    /// <param name="index">
    /// Register number; out-of-range numbers are ignored.
    /// </param>
    /// <param name="hue">
    /// Hue in degrees, 0 to 360.
    /// </param>
    /// <param name="lightness">
    /// Lightness, 0 to 100.
    /// </param>
    /// <param name="saturation">
    /// Saturation, 0 to 100.
    /// </param>
    public void SetFromHls(int index, int hue, int lightness, int saturation)
    {
        if (!IsRegister(index)) return;

        _registers[index] = FromHls(hue, lightness, saturation);
    }

    /// <summary>
    /// Sets a register to a colour directly.
    /// </summary>
    /// <param name="index">
    /// Register number; out-of-range numbers are ignored.
    /// </param>
    /// <param name="colour">
    /// The colour to store.
    /// </param>
    public void Set(int index, GraphicsColor colour)
    {
        if (!IsRegister(index)) return;

        _registers[index] = colour;
    }

    /// <summary>
    /// Converts a hue, lightness and saturation colour to RGB.
    /// </summary>
    /// <remarks>
    /// DEC's hue is offset from the usual one: 0 degrees is BLUE, not red. That is not a mistake in
    /// the format, it is what DEC chose, and getting it wrong rotates every colour in an image by a
    /// third of the wheel - which looks like a plausible picture in the wrong colours rather than
    /// like a fault.
    ///
    /// Table 2-3 confirms it from both directions: hue 0 is listed as blue, 120 as red and 240 as
    /// green, with the matching RGB percentages beside them.
    /// </remarks>
    /// <param name="hue">
    /// Hue in degrees, 0 to 360.
    /// </param>
    /// <param name="lightness">
    /// Lightness, 0 to 100.
    /// </param>
    /// <param name="saturation">
    /// Saturation, 0 to 100.
    /// </param>
    /// <returns>
    /// The colour.
    /// </returns>
    public static GraphicsColor FromHls(int hue, int lightness, int saturation)
    {
        double l = Clamp01(lightness / 100.0);
        double s = Clamp01(saturation / 100.0);

        // Rotate so that the usual "0 is red" arithmetic below gets DEC's "0 is blue".
        double h = ((hue % 360) + 360) % 360;
        h = (h + 240.0) % 360.0;

        if (s <= 0.0)
        {
            byte grey = (byte)Math.Round(l * 255.0);
            return new GraphicsColor(grey, grey, grey);
        }

        double c = (1.0 - Math.Abs(2.0 * l - 1.0)) * s;
        double x = c * (1.0 - Math.Abs((h / 60.0) % 2.0 - 1.0));
        double m = l - c / 2.0;

        double r, g, b;
        if (h < 60) { r = c; g = x; b = 0; }
        else if (h < 120) { r = x; g = c; b = 0; }
        else if (h < 180) { r = 0; g = c; b = x; }
        else if (h < 240) { r = 0; g = x; b = c; }
        else if (h < 300) { r = x; g = 0; b = c; }
        else { r = c; g = 0; b = x; }

        return new GraphicsColor(
            (byte)Math.Round(Clamp01(r + m) * 255.0),
            (byte)Math.Round(Clamp01(g + m) * 255.0),
            (byte)Math.Round(Clamp01(b + m) * 255.0));
    }

    /// <summary>
    /// Scales one 0 to 100 channel onto 0 to 255, rounding rather than truncating.
    /// </summary>
    /// <param name="value">
    /// The percentage.
    /// </param>
    /// <returns>
    /// The channel value.
    /// </returns>
    public static byte ScalePercent(int value)
    {
        if (value <= 0) return 0;
        if (value >= 100) return 255;
        return (byte)((value * 255 + 50) / 100);
    }

    private static double Clamp01(double value)
    {
        if (value < 0.0) return 0.0;
        if (value > 1.0) return 1.0;
        return value;
    }
}
