using System.Text;
using RetroTerm.Core.Configuration;
using RetroTerm.Core.Terminal.Emulators;
using Xunit;

namespace RetroTerm.Tests.Terminal.Graphics;

/// <summary>
/// The pixel aspect ratio an image asks for in its DCS, when it carries no raster attributes.
/// </summary>
/// <remarks>
/// <para><b>What the specification says</b></para>
/// The sixel DCS is "DCS Pa ; Pb ; Ph q", and Pa is the pixel aspect ratio - xterm's control
/// sequences document, held in spec DEC, names all three positional parameters. The Level 2 Sixel
/// Programming Reference, Table 5-1, gives Pa 0 and Pa 1 as aspect 200:100, which is two screen
/// rows per sixel pixel, and an omitted parameter is 0.
///
/// <para><b>What we did before 28 August 2026</b></para>
/// Only the raster attributes were read. Any image written before raster attributes existed - which
/// is most older sixel - rendered at HALF its proper height, silently, because a squashed picture
/// still looks like a picture.
///
/// <para><b>The evidence</b></para>
/// hackerb9's photographs of a real VT340. cat-original.six and cat-vt240.six are the only two
/// fixtures with no raster attributes, and on the hardware both stand twice as tall as we drew
/// them. Every other fixture either declares raster attributes or asks for 1:1, which is exactly
/// why nothing else in the corpus moved when this was fixed.
///
/// <para><b>Red before green</b></para>
/// Remove the SetAspectFromDcs call in TerminalEmulatorBase and
/// AnImageWithNoRasterAttributesIsTwiceAsTallAsItsPixels fails at half the height. Confirmed by
/// doing it.
/// </remarks>
public class SixelDcsAspectTests
{
    /// <summary>
    /// The escape character, built from its code so no raw control byte lands in this file.
    /// </summary>
    private const char Escape = (char)0x1B;

    /// <summary>
    /// A VT340 on its own 80 by 24 geometry, cursor hidden so the only ink is the image.
    /// </summary>
    /// <returns>
    /// The emulator.
    /// </returns>
    private static TerminalEmulatorBase NewVt340()
    {
        var emulator = EmulatorFactory.CreateEmulator("VT340", 80, 24, 100);
        emulator.ProcessData(Encoding.ASCII.GetBytes(Escape + "[?25l"));
        return emulator;
    }

    /// <summary>
    /// The lowest plane row holding ink, or -1 when nothing was drawn.
    /// </summary>
    /// <param name="emulator">
    /// The emulator whose sixel plane is measured.
    /// </param>
    /// <returns>
    /// The row index.
    /// </returns>
    private static int LowestInkRow(TerminalEmulatorBase emulator)
    {
        var plane = emulator.Graphics!.FindPlane("sixel");
        Assert.NotNull(plane);

        for (int y = plane!.Surface.Height - 1; y >= 0; y--)
        {
            for (int x = 0; x < plane.Surface.Width; x++)
            {
                if (!plane.Surface.GetPixel(x, y).IsTransparent) return y;
            }
        }

        return -1;
    }

    [Fact]
    public void AnImageWithNoRasterAttributesIsTwiceAsTallAsItsPixels()
    {
        // One band, six sixel pixels tall, and no raster attributes anywhere. Pa is omitted, which
        // the specification says is 0, which is 2:1 - so six sixel pixels are twelve screen rows and
        // the lowest inked row is 11.
        var emulator = NewVt340();

        emulator.ProcessData(Encoding.ASCII.GetBytes(Escape + "Pq" + "#1~" + Escape + "\\"));

        Assert.Equal(11, LowestInkRow(emulator));
    }

    [Fact]
    public void RasterAttributesStillOverrideTheDcsParameter()
    {
        // The same image, now declaring 1:1 in its raster attributes. The attributes are the more
        // specific statement and win, so six sixel pixels are six screen rows.
        var emulator = NewVt340();

        emulator.ProcessData(Encoding.ASCII.GetBytes(
            Escape + "Pq" + "\"1;1;8;6" + "#1~" + Escape + "\\"));

        Assert.Equal(5, LowestInkRow(emulator));
    }

    [Fact]
    public void AnAspectParameterOfNineIsOneToOne()
    {
        // comment.six and vaxrgl-lntest.six both send Pa 9 with no raster attributes. Only 0 and 1
        // are verified as 2:1, so everything else stays 1:1 - which is what those two fixtures need.
        var emulator = NewVt340();

        emulator.ProcessData(Encoding.ASCII.GetBytes(Escape + "P9q" + "#1~" + Escape + "\\"));

        Assert.Equal(5, LowestInkRow(emulator));
    }

    [Fact]
    public void TheBackgroundAndGridParametersDoNotChangeTheAspect()
    {
        // "DCS Pa ; Pb ; Ph q" - only the FIRST parameter is the aspect. cat-vt240.six sends
        // ESC P ; 1 q, an omitted Pa with Pb set, and reading the wrong position would make that
        // image 1:1 and leave it squashed.
        var emulator = NewVt340();

        emulator.ProcessData(Encoding.ASCII.GetBytes(Escape + "P;1;6q" + "#1~" + Escape + "\\"));

        Assert.Equal(11, LowestInkRow(emulator));
    }
}
