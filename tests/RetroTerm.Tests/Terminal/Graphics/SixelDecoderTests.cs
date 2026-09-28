using RetroTerm.Core.Terminal.Graphics;
using Xunit;

namespace RetroTerm.Tests.Terminal.Graphics;

/// <summary>
/// The Sixel decoder — the second graphics protocol to ride on the surface built for the first.
///
/// One character carries six pixels in a vertical strip, low bit at the top; strips advance right;
/// '-' drops a band and returns to the left; '$' returns to the left WITHOUT dropping, so the same
/// six rows can be painted again in another colour. Everything else is registers.
///
/// Nothing here needs a terminal, a window or a host: the decoder paints onto a surface and the
/// test reads the pixels back.
/// </summary>
public class SixelDecoderTests
{
    private static InMemoryGraphicsSurface Surface(int width = 32, int height = 24)
        => new InMemoryGraphicsSurface(width, height);

    private static int LitPixels(InMemoryGraphicsSurface surface)
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

    [Fact]
    public void OneStripLightsSixPixelsDownAColumn()
    {
        // '~' is 0x7E: all six bits.
        var surface = Surface();
        var decoder = new SixelDecoder();

        decoder.Decode("#1~", surface);

        for (int y = 0; y < 6; y++)
        {
            Assert.False(surface.GetPixel(0, y).IsTransparent);
        }
        Assert.True(surface.GetPixel(0, 6).IsTransparent);
        Assert.Equal(6, LitPixels(surface));
    }

    [Fact]
    public void TheLowBitIsTheTopPixel()
    {
        // '@' is one more than '?': bit 0 alone.
        var surface = Surface();
        var decoder = new SixelDecoder();

        decoder.Decode("#1@", surface);

        Assert.False(surface.GetPixel(0, 0).IsTransparent);
        Assert.True(surface.GetPixel(0, 1).IsTransparent);
    }

    [Fact]
    public void StripsAdvanceToTheRight()
    {
        var surface = Surface();
        var decoder = new SixelDecoder();

        decoder.Decode("#1@?@", surface);

        Assert.False(surface.GetPixel(0, 0).IsTransparent);
        Assert.True(surface.GetPixel(1, 0).IsTransparent);
        Assert.False(surface.GetPixel(2, 0).IsTransparent);
        Assert.Equal(3, decoder.Width);
    }

    [Fact]
    public void AHyphenDropsToTheNextBandOfSixRows()
    {
        var surface = Surface();
        var decoder = new SixelDecoder();

        decoder.Decode("#1@-@", surface);

        Assert.False(surface.GetPixel(0, 0).IsTransparent);
        Assert.False(surface.GetPixel(0, 6).IsTransparent);
        Assert.Equal(7, decoder.Height);
    }

    [Fact]
    public void ADollarReturnsToTheLeftWithoutDropping()
    {
        // How a multi-coloured image is built: paint the band, go back, paint it again in another
        // colour. Dropping a band here instead would smear every image down the screen.
        var surface = Surface();
        var decoder = new SixelDecoder();

        // Two columns in register 1, then back to the left and the FIRST column again in register
        // 2. Both strips are '@' - bit 0, the top row - so the second really does land on the
        // first one rather than under it.
        decoder.Decode("#1@@$#2@", surface);

        Assert.Equal(decoder.Register(2).Value, surface.GetPixel(0, 0).Value);
        Assert.Equal(decoder.Register(1).Value, surface.GetPixel(1, 0).Value);

        // And nothing dropped a band: the image is one row tall, not seven.
        Assert.Equal(1, decoder.Height);
    }

    [Fact]
    public void ARepeatPaintsTheSameStripSeveralTimes()
    {
        var surface = Surface();
        var decoder = new SixelDecoder();

        decoder.Decode("#1!5@", surface);

        for (int x = 0; x < 5; x++)
        {
            Assert.False(surface.GetPixel(x, 0).IsTransparent);
        }
        Assert.True(surface.GetPixel(5, 0).IsTransparent);
    }

    [Fact]
    public void TheRepeatAppliesToOneStripOnly()
    {
        // Leaving the count set is how a whole image ends up drawn at the width of its first run.
        var surface = Surface();
        var decoder = new SixelDecoder();

        decoder.Decode("#1!3@@", surface);

        Assert.Equal(4, decoder.Width);
    }

    [Fact]
    public void ARepeatOfZeroIsTreatedAsOne()
    {
        // Painting nothing would silently swallow the strip that follows it.
        var surface = Surface();
        var decoder = new SixelDecoder();

        decoder.Decode("#1!0@", surface);

        Assert.False(surface.GetPixel(0, 0).IsTransparent);
    }

    [Fact]
    public void AColourCanBeDefinedInRgb()
    {
        // Components are 0-100, not 0-255, and 100 has to reach 255 rather than 254.
        var surface = Surface();
        var decoder = new SixelDecoder();

        decoder.Decode("#9;2;100;0;0~", surface);

        var pixel = surface.GetPixel(0, 0);
        Assert.Equal(255, pixel.R);
        Assert.Equal(0, pixel.G);
        Assert.Equal(0, pixel.B);
    }

    [Fact]
    public void SelectingARegisterDoesNotRedefineIt()
    {
        // "#9" with nothing after it means "use register 9". Treating it as a definition would
        // repaint it from whatever happened to be in the array.
        var surface = Surface();
        var decoder = new SixelDecoder();

        decoder.Decode("#9;2;100;0;0~$#0?#9~", surface);

        var pixel = surface.GetPixel(0, 0);
        Assert.Equal(255, pixel.R);
        Assert.Equal(0, pixel.G);
    }

    [Fact]
    public void HueZeroIsBlueNotRed()
    {
        // Sixel's hue is offset from the usual wheel: 0 degrees is BLUE. Getting it wrong rotates
        // every colour by a third and produces a plausible picture in the wrong colours.
        var surface = Surface();
        var decoder = new SixelDecoder();

        decoder.Decode("#1;1;0;50;100~", surface);

        var pixel = surface.GetPixel(0, 0);
        Assert.True(pixel.B > pixel.R, $"hue 0 should be blue, got {pixel.R},{pixel.G},{pixel.B}");
        Assert.True(pixel.B > pixel.G);
    }

    [Fact]
    public void NoSaturationIsGrey()
    {
        var surface = Surface();
        var decoder = new SixelDecoder();

        decoder.Decode("#1;1;0;50;0~", surface);

        var pixel = surface.GetPixel(0, 0);
        Assert.Equal(pixel.R, pixel.G);
        Assert.Equal(pixel.G, pixel.B);
    }

    [Fact]
    public void RasterAttributesGiveTheSizeTheImageMeantToBe()
    {
        var surface = Surface();
        var decoder = new SixelDecoder();

        decoder.Decode("\"1;1;20;12#1~", surface);

        Assert.Equal(20, decoder.DeclaredWidth);
        Assert.Equal(12, decoder.DeclaredHeight);
    }

    [Fact]
    public void AnImageLargerThanTheSurfaceIsClippedRatherThanRefused()
    {
        // A host is entitled to send a big picture, and a terminal that fell over would be broken.
        var surface = Surface(4, 4);
        var decoder = new SixelDecoder();

        decoder.Decode("#1!100~", surface);

        Assert.Equal(100, decoder.Width);
        Assert.Equal(16, LitPixels(surface));   // the 4x4 that fits
    }

    [Fact]
    public void RubbishInTheStreamIsSkippedRatherThanGuessedAt()
    {
        // Encoders wrap long images across lines, so a stray newline in the payload is ordinary.
        var surface = Surface();
        var decoder = new SixelDecoder();

        decoder.Decode("#1@\n\r@", surface);

        Assert.False(surface.GetPixel(0, 0).IsTransparent);
        Assert.False(surface.GetPixel(1, 0).IsTransparent);
    }

    [Fact]
    public void AnUndefinedRegisterStillDrawsSomething()
    {
        // An image that never defines a colour has to draw something visible; leaving the
        // registers black would make it invisible on a black screen.
        var surface = Surface();
        var decoder = new SixelDecoder();

        decoder.Decode("#3~", surface);

        Assert.False(surface.GetPixel(0, 0).IsTransparent);
    }

    [Fact]
    public void ASecondImageStartsAtTheTopLeftAgain()
    {
        // The registers survive between images - encoders define a palette once and use it again -
        // but the pen does not.
        var surface = Surface();
        var decoder = new SixelDecoder();
        decoder.Decode("#1@--@", surface);

        decoder.Decode("#1@", surface);

        Assert.Equal(1, decoder.Width);
        Assert.Equal(1, decoder.Height);
    }

    // ─────────────────────────────────────────────────────────────
    // Raster attributes: the aspect ratio is a pixel replication count
    //
    // Found by rendering hackerb9's vt340test corpus and LOOKING at it. multisize.six draws the
    // United States flag by changing the ratio three times inside one image; ours drew a twelve
    // pixel sliver along the top edge, because the ratio was read and thrown away.
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void ATwoToOneRatioMakesEverySixelPixelTwoRowsTall()
    {
        // "Pan;Pad is 2;1" - the ratio DEC's own encoders emit. Six sixel pixels become twelve
        // screen rows, and each one is a solid pair.
        var surface = Surface();
        var decoder = new SixelDecoder();

        decoder.Decode("\"2;1;1;12#1~", surface);

        for (int y = 0; y < 12; y++)
        {
            Assert.False(surface.GetPixel(0, y).IsTransparent, $"row {y} should be painted");
        }
        Assert.True(surface.GetPixel(0, 12).IsTransparent);
        Assert.Equal(12, decoder.Height);
    }

    [Fact]
    public void AndTheNextBandStartsBelowTheStretchedOne()
    {
        // The '-' has to advance by six STRETCHED pixels, not six. Advancing by six would lay the
        // second band on top of the first and the image would come out a sixth of its height with
        // every band overlapping.
        var surface = Surface();
        var decoder = new SixelDecoder();

        decoder.Decode("\"2;1;1;24#1~-#1@", surface);

        // Band two's first pixel: row 12, two rows tall.
        Assert.False(surface.GetPixel(0, 12).IsTransparent);
        Assert.False(surface.GetPixel(0, 13).IsTransparent);
        Assert.True(surface.GetPixel(0, 14).IsTransparent);
    }

    [Fact]
    public void TheRatioCanChangeInTheMiddleOfAnImage()
    {
        // multisize.six is built on this: three raster attributes in one DCS string, each one
        // changing the scale of the pixels that follow it.
        var surface = Surface(32, 24);
        var decoder = new SixelDecoder();

        decoder.Decode("\"1;1;1;24#1@-\"3;1;1;24#1@", surface);

        // First band at 1:1 - one row. Then '-' advances six.
        Assert.False(surface.GetPixel(0, 0).IsTransparent);
        Assert.True(surface.GetPixel(0, 1).IsTransparent);

        // Second band at 3:1 - three rows, starting at row 6.
        Assert.False(surface.GetPixel(0, 6).IsTransparent);
        Assert.False(surface.GetPixel(0, 8).IsTransparent);
        Assert.True(surface.GetPixel(0, 9).IsTransparent);
    }

    [Fact]
    public void ARatioWithNoSenseInItFallsBackToSquarePixels()
    {
        // A zero denominator is a mangled stream, not a request to divide by zero. Painting the
        // image square is more use than dropping it.
        var surface = Surface();
        var decoder = new SixelDecoder();

        decoder.Decode("\"2;0;1;6#1~", surface);

        Assert.Equal(6, decoder.Height);
    }

    // ─────────────────────────────────────────────────────────────
    // Whitespace inside the parameters
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void SpacesBetweenTheNumbersDoNotLoseTheColour()
    {
        // Sixel data runs from '?' to '~', so a space can never be data and a hand-written stream
        // is entitled to lay its numbers out readably. Stopping at the space left the register at
        // its power-on default instead - which is how the navy canton of a flag came out bright
        // blue, matching nothing the host had asked for.
        var surface = Surface();
        var defined = new SixelDecoder();
        var laidOut = new SixelDecoder();

        defined.Decode("#5;2;0;13;28~", surface);
        var wanted = defined.Register(5);

        laidOut.Decode("#5 ;2; 0; 13; 28 ~", surface);

        Assert.Equal(wanted.R, laidOut.Register(5).R);
        Assert.Equal(wanted.G, laidOut.Register(5).G);
        Assert.Equal(wanted.B, laidOut.Register(5).B);
    }

    [Fact]
    public void AndALineBreakInTheMiddleOfThemDoesNotEither()
    {
        // The corpus files wrap their palettes over several lines.
        var surface = Surface();
        var decoder = new SixelDecoder();

        decoder.Decode("#5;2;\n0;13;\n28~", surface);

        var colour = decoder.Register(5);

        Assert.Equal(0, colour.R);
        Assert.True(colour.B > colour.G);
    }
}
