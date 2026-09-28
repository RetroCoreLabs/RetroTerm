using System.Text;
using RetroTerm.Core.Configuration;
using RetroTerm.Core.Terminal.Emulators;
using Xunit;

namespace RetroTerm.Tests.Terminal.Graphics;

/// <summary>
/// The background colour option, P2, of the Sixel device control string.
/// </summary>
/// <remarks>
/// <para><b>What the manual says, quoted</b></para>
/// VT330/VT340 Graphics Programming manual, chapter 14, held in spec DEC. "P2 selects how the
/// terminal draws the background color":
///  - 0 or 2, the default - "Pixel positions specified as 0 are set to the current background color."
///  - 1 - "Pixel positions specified as 0 remain at their current color."
///
/// The area is the one the raster attributes name: "The VT300 uses Ph and Pv to erase the background
/// when P2 is set to 0 or 2", and "Ph and Pv let you omit background sixel data from the image
/// definition and still have a color background."
///
/// <para><b>What we did before 28 August 2026</b></para>
/// The parameter was never parsed. Every image behaved as though P2 were 1, so a host that set a
/// background and then sent an image got the terminal's background showing through the parts the
/// image left alone, instead of the colour it had asked for.
///
/// <para><b>Why the default case paints nothing</b></para>
/// When the background is the terminal default, leaving those pixels transparent shows the
/// terminal's own background - which IS the current background colour. Painting it would bake
/// today's theme into the picture for no gain.
/// </remarks>
public class SixelBackgroundOptionTests
{
    /// <summary>
    /// The escape character, built from its code so no raw control byte lands in this file.
    /// </summary>
    private const char Escape = (char)0x1B;

    /// <summary>
    /// A VT340 with the cursor hidden.
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
    /// The colour of one pixel on the sixel plane.
    /// </summary>
    /// <param name="emulator">
    /// The emulator to read.
    /// </param>
    /// <param name="x">
    /// Plane column.
    /// </param>
    /// <param name="y">
    /// Plane row.
    /// </param>
    /// <returns>
    /// The pixel.
    /// </returns>
    private static RetroTerm.Core.Terminal.Graphics.GraphicsColor PixelAt(
        TerminalEmulatorBase emulator, int x, int y)
    {
        var plane = emulator.Graphics!.FindPlane("sixel");
        Assert.NotNull(plane);
        return plane!.Surface.GetPixel(x, y);
    }

    [Fact]
    public void AnUntouchedPixelTakesTheBackgroundTheHostAskedFor()
    {
        // SGR 41 is a red background. The image declares 40 by 20 and paints one column, so the far
        // corner of the declared area is a pixel the image never touched.
        var emulator = NewVt340();
        emulator.ProcessData(Encoding.ASCII.GetBytes(Escape + "[41m"));

        emulator.ProcessData(Encoding.ASCII.GetBytes(
            Escape + "Pq" + "\"1;1;40;20" + "#1~" + Escape + "\\"));

        var corner = PixelAt(emulator, 30, 15);
        Assert.False(corner.IsTransparent, "the declared area was not erased at all");
        Assert.True(corner.R > corner.G && corner.R > corner.B,
            "expected the red background, got " + corner.R + "," + corner.G + "," + corner.B);
    }

    [Fact]
    public void P2OfOneLeavesUntouchedPixelsAlone()
    {
        // The transparent case, stated in the manual as "remain at their current color".
        var emulator = NewVt340();
        emulator.ProcessData(Encoding.ASCII.GetBytes(Escape + "[41m"));

        emulator.ProcessData(Encoding.ASCII.GetBytes(
            Escape + "P;1q" + "\"1;1;40;20" + "#1~" + Escape + "\\"));

        Assert.True(PixelAt(emulator, 30, 15).IsTransparent,
            "P2=1 must not paint the background");
    }

    [Fact]
    public void WithNoRasterAttributesNothingIsErased()
    {
        // "The VT300 uses Ph and Pv to erase the background" - an image that never declared them has
        // not said how big the area is, so there is nothing to erase.
        var emulator = NewVt340();
        emulator.ProcessData(Encoding.ASCII.GetBytes(Escape + "[41m"));

        emulator.ProcessData(Encoding.ASCII.GetBytes(Escape + "Pq" + "#1~" + Escape + "\\"));

        Assert.True(PixelAt(emulator, 30, 15).IsTransparent,
            "an image with no raster attributes erased something");
    }

    [Fact]
    public void TheTerminalDefaultBackgroundPaintsNothing()
    {
        // No SGR background set. Transparent already shows the terminal's own background, so the
        // erase would only hard-code the current theme into the plane.
        var emulator = NewVt340();

        emulator.ProcessData(Encoding.ASCII.GetBytes(
            Escape + "Pq" + "\"1;1;40;20" + "#1~" + Escape + "\\"));

        Assert.True(PixelAt(emulator, 30, 15).IsTransparent,
            "the default background should be left showing, not painted");
    }
}
