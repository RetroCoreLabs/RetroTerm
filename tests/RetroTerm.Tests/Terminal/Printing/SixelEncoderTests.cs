using System.Text;
using RetroTerm.Core.Terminal.Graphics;
using RetroTerm.Core.Terminal.Printing;
using Xunit;

namespace RetroTerm.Tests.Terminal.Printing;

/// <summary>
/// The sixel dump generator - re-encoding the terminal's own bitmap for the printer port.
/// </summary>
/// <remarks>
/// <para><b>The oracle is a round trip</b></para>
/// Nothing outside this repository can say whether the bytes we EMIT are right, because there is
/// no reference printer to compare against. What can be checked is that the picture survives:
/// encode a surface, decode the result with the decoder that is already checked against sixteen
/// streams from real VT340 hardware, and compare the pixels. A defect in the encoder shows up as a
/// picture that came back different.
///
/// <para><b>Where the format rules come from</b></para>
/// Chapter 5 of <c>spec\DEC\EK-PPLV2-PM.B01_Level_2_Sixel_Programming_Reference.pdf</c> for the
/// envelope and the control codes, and DEC STD 070 section 7.8 for the three print shapes.
/// </remarks>
public class SixelEncoderTests
{
    private static readonly GraphicsColor Red = new GraphicsColor(255, 0, 0);
    private static readonly GraphicsColor Green = new GraphicsColor(0, 255, 0);
    private static readonly GraphicsColor Blue = new GraphicsColor(0, 0, 255);

    private static string Encode(IGraphicsSurface surface, SixelPrintOptions options)
    {
        var sink = new MemoryPrintSink();
        SixelEncoder.Encode(surface, options, sink);
        return sink.ToText();
    }

    /// <summary>
    /// Strips the DCS envelope, leaving the picture data the decoder expects.
    /// </summary>
    private static string Payload(string dump)
    {
        int q = dump.IndexOf('q');
        int st = dump.LastIndexOf("\x1b\\", System.StringComparison.Ordinal);
        return dump.Substring(q + 1, st - q - 1);
    }

    /// <summary>
    /// Encodes then decodes, and hands back the picture that came home.
    /// </summary>
    private static InMemoryGraphicsSurface RoundTrip(IGraphicsSurface source,
        SixelPrintOptions options, int width, int height)
    {
        string dump = Encode(source, options);
        var landed = new InMemoryGraphicsSurface(width, height);
        new SixelDecoder().Decode(Payload(dump).AsSpan(), landed);
        return landed;
    }

    private static int LitPixels(IGraphicsSurface surface)
    {
        int count = 0;
        for (int y = 0; y < surface.Height; y++)
        {
            for (int x = 0; x < surface.Width; x++)
            {
                if (!surface.GetPixel(x, y).IsTransparent) count++;
            }
        }

        return count;
    }

    // ─────────────────────────────────────────────────────────────
    // The envelope
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void ALevelOneDumpIsTheBareSevenBitForm()
    {
        // DEC STD 070: a level 1 printer gets "ESC P 1 q" and no Set Raster Attributes at all.
        // The documented VT240 screen dump is exactly this.
        var surface = new InMemoryGraphicsSurface(8, 6);
        surface.SetPixel(0, 0, Red);

        string dump = Encode(surface, new SixelPrintOptions { Level = 1 });

        Assert.StartsWith("\x1bP1q", dump);
        Assert.DoesNotContain("\"", dump);
        Assert.EndsWith("\x1b\\", dump);
    }

    [Fact]
    public void ALevelTwoDumpCarriesRasterAttributes()
    {
        // The half of printing with no oracle other than the bytes we emit, so it is checked
        // literally: DECGRA is Pan;Pad;Ph;Pv and Ph;Pv are the picture's size.
        var surface = new InMemoryGraphicsSurface(8, 6);
        surface.SetPixel(0, 0, Red);

        string dump = Encode(surface, new SixelPrintOptions { Level = 2 });

        Assert.Contains("\"1;1;8;6", dump);
        Assert.EndsWith("\x1b\\", dump);
    }

    [Fact]
    public void AColourDumpSendsTheColourMapAtTheHead()
    {
        // "Colour printing sends the colour map at the head of the dump" - DEC STD 070. The 2
        // selects RGB and the components are PERCENTAGES, so pure red is 100;0;0 and not 255;0;0.
        var surface = new InMemoryGraphicsSurface(8, 6);
        surface.SetPixel(0, 0, Red);

        string dump = Encode(surface, new SixelPrintOptions { Colour = true });

        Assert.Contains("#0;2;100;0;0", dump);
    }

    [Fact]
    public void AMonochromeDumpSelectsNoColourAtAll()
    {
        // "Monochrome devices ignore DECGCI. All sixel data is printed in black."
        var surface = new InMemoryGraphicsSurface(8, 6);
        surface.SetPixel(0, 0, Red);
        surface.SetPixel(1, 1, Blue);

        string dump = Encode(surface, new SixelPrintOptions { Colour = false });

        Assert.DoesNotContain("#", dump);
    }

    [Fact]
    public void ALongRunUsesTheRepeatIntroducer()
    {
        // DECGRI. A page of white paper encoded one character per column would be enormous.
        var surface = new InMemoryGraphicsSurface(200, 6);
        surface.FillRectangle(0, 0, 200, 6, Red);

        string dump = Encode(surface, new SixelPrintOptions());

        Assert.Contains("!200~", dump);
    }

    // ─────────────────────────────────────────────────────────────
    // The round trip
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void AMonochromePictureSurvivesTheRoundTrip()
    {
        var source = new InMemoryGraphicsSurface(32, 24);
        source.DrawLine(0, 0, 31, 23, Green);
        source.FillRectangle(4, 4, 8, 8, Green);
        source.DrawCircle(20, 12, 6, Green);

        var landed = RoundTrip(source, new SixelPrintOptions(), 32, 24);

        for (int y = 0; y < 24; y++)
        {
            for (int x = 0; x < 32; x++)
            {
                bool wasInk = !source.GetPixel(x, y).IsTransparent;
                bool isInk = !landed.GetPixel(x, y).IsTransparent;
                Assert.True(wasInk == isInk, $"pixel {x},{y} changed: was {wasInk}, came back {isInk}");
            }
        }
    }

    [Fact]
    public void AColourPictureKeepsItsColoursThroughTheRoundTrip()
    {
        var source = new InMemoryGraphicsSurface(24, 12);
        source.FillRectangle(0, 0, 8, 12, Red);
        source.FillRectangle(8, 0, 8, 12, Green);
        source.FillRectangle(16, 0, 8, 12, Blue);

        var landed = RoundTrip(source, new SixelPrintOptions { Colour = true }, 24, 12);

        // Percentages, so a component survives to within one part in a hundred rather than exactly.
        AssertCloseColour(Red, landed.GetPixel(4, 6));
        AssertCloseColour(Green, landed.GetPixel(12, 6));
        AssertCloseColour(Blue, landed.GetPixel(20, 6));
    }

    private static void AssertCloseColour(GraphicsColor expected, GraphicsColor actual)
    {
        Assert.True(System.Math.Abs(expected.R - actual.R) <= 3
            && System.Math.Abs(expected.G - actual.G) <= 3
            && System.Math.Abs(expected.B - actual.B) <= 3,
            $"expected about {expected.R},{expected.G},{expected.B} but got {actual.R},{actual.G},{actual.B}");
    }

    [Fact]
    public void APictureTallerThanOneBandSurvives()
    {
        // The band boundary is where an encoder gets its bit order wrong, so the picture is
        // deliberately not a multiple of six high.
        var source = new InMemoryGraphicsSurface(8, 17);
        for (int y = 0; y < 17; y++) source.SetPixel(y % 8, y, Green);

        var landed = RoundTrip(source, new SixelPrintOptions(), 8, 17);

        for (int y = 0; y < 17; y++)
        {
            Assert.False(landed.GetPixel(y % 8, y).IsTransparent, $"row {y} lost its pixel");
        }
    }

    [Fact]
    public void TheTopPixelOfABandIsBitZero()
    {
        // Section 5.5.1: "Top pixel Bit 0 (LSB) ... Bottom pixel Bit 5 (MSB)". Getting this
        // backwards flips every band and the picture comes out as horizontal stripes, which a
        // whole-picture comparison would catch but not explain.
        var source = new InMemoryGraphicsSurface(1, 6);
        source.SetPixel(0, 0, Green);

        string dump = Encode(source, new SixelPrintOptions { Level = 1 });

        // '@' is 0x40, which is 0x3F + 1: bit 0 only, the top pixel.
        Assert.Contains("q@", dump);
    }

    // ─────────────────────────────────────────────────────────────
    // The three print shapes
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void AnExpandedPrintIsTwiceAsWide()
    {
        // DECGEPM: "each sixel sent twice in horizontal succession".
        var source = new InMemoryGraphicsSurface(10, 6);
        source.FillRectangle(0, 0, 10, 6, Green);

        var landed = RoundTrip(source, new SixelPrintOptions { Expanded = true }, 40, 6);

        Assert.False(landed.GetPixel(19, 3).IsTransparent, "the expanded picture should reach x=19");
        Assert.True(landed.GetPixel(20, 3).IsTransparent, "and should stop at twice the width");
    }

    [Fact]
    public void AnExpandedPrintDeclaresTheDoubledWidth()
    {
        var source = new InMemoryGraphicsSurface(10, 6);
        source.SetPixel(0, 0, Green);

        string dump = Encode(source, new SixelPrintOptions { Expanded = true });

        Assert.Contains("\"1;1;20;6", dump);
    }

    [Fact]
    public void ARotatedPrintSwapsWidthAndHeight()
    {
        var source = new InMemoryGraphicsSurface(12, 6);
        source.SetPixel(0, 0, Green);

        string dump = Encode(source, new SixelPrintOptions { Rotated = true });

        // Rotated is always expanded, so the printed width is 6 doubled and the height is 12.
        Assert.Contains("\"1;1;12;12", dump);
    }

    [Fact]
    public void ARotatedPrintTurnsCounterClockwise()
    {
        // DEC STD 070: "the VT240 rotates the image counter clockwise, so that the left side of
        // the paper ... corresponds to the top of the image on the terminal screen."
        //
        // So the source's TOP ROW must end up down the LEFT EDGE of the page. Clockwise would put
        // it down the right edge, and a whole-picture test would not tell the two apart.
        var source = new InMemoryGraphicsSurface(12, 12);
        source.SetPixel(11, 0, Green);          // top-RIGHT of the screen

        var landed = RoundTrip(source, new SixelPrintOptions { Rotated = true }, 24, 12);

        // Counter-clockwise sends the top-right corner to the top-left of the page. Expanded, so
        // the single source column became two printed ones.
        Assert.False(landed.GetPixel(0, 0).IsTransparent,
            "the screen's top-right corner should print at the page's top-left");
        Assert.True(landed.GetPixel(23, 11).IsTransparent,
            "and nothing should have landed in the opposite corner");
    }

    [Fact]
    public void PrintingTheBackgroundInvertsWhatGetsInk()
    {
        var source = new InMemoryGraphicsSurface(8, 6);
        source.FillRectangle(0, 0, 4, 6, Green);

        var normal = RoundTrip(source, new SixelPrintOptions(), 8, 6);
        var inverted = RoundTrip(source, new SixelPrintOptions { PrintBackground = true }, 8, 6);

        Assert.Equal(24, LitPixels(normal));
        Assert.Equal(24, LitPixels(inverted));
        Assert.False(normal.GetPixel(1, 3).IsTransparent);
        Assert.True(inverted.GetPixel(1, 3).IsTransparent);
    }

    [Fact]
    public void AnEmptySurfaceStillProducesAWellFormedDump()
    {
        // A host may ask for a hard copy of a blank screen. The answer is a valid empty picture,
        // not a truncated control string.
        var surface = new InMemoryGraphicsSurface(8, 6);

        string dump = Encode(surface, new SixelPrintOptions());

        Assert.StartsWith("\x1bP", dump);
        Assert.EndsWith("\x1b\\", dump);
    }
}
