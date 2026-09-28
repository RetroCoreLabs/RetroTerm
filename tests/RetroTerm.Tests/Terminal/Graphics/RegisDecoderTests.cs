using RetroTerm.Core.Terminal.Graphics;
using Xunit;

namespace RetroTerm.Tests.Terminal.Graphics;

/// <summary>
/// ReGIS — the third graphics protocol on the surface built for the first.
///
/// A letter names a command, brackets carry coordinates, parentheses carry options. The parts that
/// bite are the coordinate rules: a sign makes a number RELATIVE to the current point, and an empty
/// field leaves that axis alone. Both are easy to read past and produce a drawing that is subtly in
/// the wrong place.
///
/// Y runs DOWN, unlike the Tektronix decoder next to it, which flips everything.
/// </summary>
public class RegisDecoderTests
{
    private static InMemoryGraphicsSurface Surface(int width = 64, int height = 64)
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
    public void AVectorDrawsFromWhereTheDrawingPointIs()
    {
        var surface = Surface();
        var decoder = new RegisDecoder();

        decoder.Decode("P[10,10]V[10,20]", surface);

        Assert.False(surface.GetPixel(10, 10).IsTransparent);
        Assert.False(surface.GetPixel(10, 15).IsTransparent);
        Assert.False(surface.GetPixel(10, 20).IsTransparent);
        Assert.True(surface.GetPixel(10, 21).IsTransparent);
    }

    [Fact]
    public void PositionMovesWithoutDrawing()
    {
        var surface = Surface();
        var decoder = new RegisDecoder();

        decoder.Decode("P[10,10]P[30,30]", surface);

        Assert.Equal(0, LitPixels(surface));
        Assert.Equal(30, decoder.CurrentX);
        Assert.Equal(30, decoder.CurrentY);
    }

    [Fact]
    public void ASignMakesTheNumberRelative()
    {
        // [+20,0] is twenty to the RIGHT of where the drawing is; [20,0] is twenty from the left
        // edge. Reading past the sign gives a picture that is subtly in the wrong place.
        var surface = Surface();
        var decoder = new RegisDecoder();

        decoder.Decode("P[10,10]P[+20,+5]", surface);

        Assert.Equal(30, decoder.CurrentX);
        Assert.Equal(15, decoder.CurrentY);
    }

    [Fact]
    public void AMinusGoesTheOtherWay()
    {
        var surface = Surface();
        var decoder = new RegisDecoder();

        decoder.Decode("P[30,30]P[-10,-5]", surface);

        Assert.Equal(20, decoder.CurrentX);
        Assert.Equal(25, decoder.CurrentY);
    }

    [Fact]
    public void AnEmptyFieldLeavesThatAxisAlone()
    {
        // How a host moves straight down: [,100].
        var surface = Surface();
        var decoder = new RegisDecoder();

        decoder.Decode("P[10,10]P[,40]", surface);

        Assert.Equal(10, decoder.CurrentX);
        Assert.Equal(40, decoder.CurrentY);
    }

    [Fact]
    public void SeveralCoordinatesAfterOneVectorDrawAChain()
    {
        var surface = Surface();
        var decoder = new RegisDecoder();

        decoder.Decode("P[0,0]V[10,0][10,10]", surface);

        Assert.False(surface.GetPixel(5, 0).IsTransparent);    // the first leg
        Assert.False(surface.GetPixel(10, 5).IsTransparent);   // and the second
        Assert.Equal(10, decoder.CurrentX);
        Assert.Equal(10, decoder.CurrentY);
    }

    [Fact]
    public void YRunsDown()
    {
        // The neighbouring Tektronix decoder flips Y; this one must not. A drawing at y=5 belongs
        // near the TOP of the surface.
        var surface = Surface();
        var decoder = new RegisDecoder();

        decoder.Decode("P[0,5]V[20,5]", surface);

        Assert.False(surface.GetPixel(10, 5).IsTransparent);
        Assert.True(surface.GetPixel(10, 58).IsTransparent);
    }

    /// <summary>
    /// A screen erase clears every pixel - and leaves the drawing point exactly where it was.
    /// </summary>
    /// <remarks>
    /// <para><b>This test used to assert the opposite, and the manual settled it</b></para>
    /// It checked that the drawing point had gone home to 0,0 after the erase, because that is what
    /// the decoder did. Chapter 4 lists what the screen erase command does and the second bullet is
    /// "Does not change the cursor position" - so the assertion had been written from the code
    /// rather than from the source, and it pinned the defect in place.
    /// Corrected on 2026-08-20 along with the decoder. The pixel half of this test was always right
    /// and is untouched.
    /// </remarks>
    [Fact]
    public void ScreenEraseClearsEveryPixelAndLeavesTheDrawingPointAlone()
    {
        var surface = Surface();
        var decoder = new RegisDecoder();
        decoder.Decode("P[0,0]V[30,30]", surface);
        Assert.True(LitPixels(surface) > 0);

        decoder.Decode("S(E)", surface);

        Assert.Equal(0, LitPixels(surface));

        // The vector ended at 30,30 and the erase does not move it.
        Assert.Equal(30, decoder.CurrentX);
        Assert.Equal(30, decoder.CurrentY);
    }

    [Fact]
    public void TheWriteCommandChoosesTheColour()
    {
        var surface = Surface();
        var decoder = new RegisDecoder();

        decoder.Decode("W(I2)P[0,0]V[10,0]", surface);

        Assert.Equal(decoder.Register(2).Value, surface.GetPixel(5, 0).Value);
    }

    [Fact]
    public void ACircleIsCentredOnTheCurrentPointAndPassesThroughTheGivenOne()
    {
        var surface = Surface();
        var decoder = new RegisDecoder();

        decoder.Decode("P[30,30]C[40,30]", surface);

        Assert.False(surface.GetPixel(40, 30).IsTransparent);   // east
        Assert.False(surface.GetPixel(20, 30).IsTransparent);   // west
        Assert.False(surface.GetPixel(30, 40).IsTransparent);   // south
        Assert.True(surface.GetPixel(30, 30).IsTransparent, "an outline is not a disc");

        // And the drawing point is back at the centre, which is where a real ReGIS leaves it.
        Assert.Equal(30, decoder.CurrentX);
        Assert.Equal(30, decoder.CurrentY);
    }

    [Fact]
    public void WhitespaceBetweenCommandsIsIgnored()
    {
        // Real ReGIS arrives wrapped across lines.
        var surface = Surface();
        var decoder = new RegisDecoder();

        decoder.Decode("P[0,0]\r\n  V[10,0]\n", surface);

        Assert.False(surface.GetPixel(5, 0).IsTransparent);
    }

    [Fact]
    public void AnUnimplementedCommandIsCountedAndItsArgumentsSkipped()
    {
        // Q is not a ReGIS command at all, which is now the only way to write this test: all TEN of
        // the real ones are implemented as of 2026-08-18. It stands for whatever a mangled stream or
        // a later extension might put in front of us.
        //
        // The drawing after it must still arrive, which is the whole reason the arguments are
        // skipped rather than parsed - and the position inside them must NOT move the cursor, or
        // the vector would start somewhere else.
        //
        // This example has moved twice as the decoder grew: it was T until T was built, then R
        // until R was built. The test still says what it always said.
        var surface = Surface();
        var decoder = new RegisDecoder();

        decoder.Decode("P[0,0]Q(9)[99,99]V[10,0]", surface);

        Assert.Equal(1, decoder.UnhandledCommands['Q']);
        Assert.False(surface.GetPixel(5, 0).IsTransparent);
    }

    [Fact]
    public void QuotedTextCannotDeraiTheParserWithBracketsInside()
    {
        var surface = Surface();
        var decoder = new RegisDecoder();

        // Brackets inside a quoted string are TEXT, not coordinates. Now that T draws, the string
        // itself puts ink on the surface - so the check is that the vector after it still arrives,
        // somewhere the glyphs cannot reach. The text is drawn at the top, ten pixels tall at
        // this size, and the line runs along y=40, well below it.
        decoder.Decode("P[0,0]T'a[b]c'P[0,40]V[10,40]", surface);

        Assert.False(surface.GetPixel(5, 40).IsTransparent);
    }

    [Fact]
    public void AnArcIsCountedRatherThanDrawnAsACircle()
    {
        // C(A...) is an arc. Reading its coordinates as a plain circle would draw something the
        // host did not ask for, which is worse than drawing nothing.
        var surface = Surface();
        var decoder = new RegisDecoder();

        decoder.Decode("P[30,30]C(A+90)[40,30]", surface);

        Assert.Equal(1, decoder.UnhandledCommands['C']);
        Assert.Equal(0, LitPixels(surface));
    }

    [Fact]
    public void AnUnclosedBracketDoesNotHang()
    {
        // A truncated stream is a stream, not a reason to fall over or loop.
        var surface = Surface();
        var decoder = new RegisDecoder();

        decoder.Decode("P[0,0]V[10,", surface);

        Assert.True(true, "reaching here at all is the assertion");
    }

    [Fact]
    public void ADrawingOffTheEdgeIsClipped()
    {
        var surface = Surface(16, 16);
        var decoder = new RegisDecoder();

        decoder.Decode("P[0,0]V[100,0]", surface);

        Assert.Equal(16, LitPixels(surface));
    }

    // ─────────────────────────────────────────────────────────────
    // S(M...) - output mapping, the command that loads the colour map
    //
    // The manual's own example: S(M1(AH60L80S60)3(AH150L50S60)) - location 1 to plum, location 3
    // to gold. It was counted rather than implemented, so a host that set its palette this way and
    // then sent a Sixel got the power-on colours.
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void OutputMappingSetsAColourFromHls()
    {
        var surface = Surface();
        var decoder = new RegisDecoder();

        decoder.Decode("S(M2(H120L50S100))", surface);

        // DEC hue 120 is RED, and lightness 50 with full saturation is the pure form of it.
        var colour = decoder.Register(2);
        Assert.Equal(255, colour.R);
        Assert.Equal(0, colour.G);
        Assert.Equal(0, colour.B);
    }

    [Fact]
    public void AndFromRgbPercentages()
    {
        var surface = Surface();
        var decoder = new RegisDecoder();

        decoder.Decode("S(M4(R80G20B80))", surface);

        // The same numbers table 2-3 prints for magenta.
        Assert.Equal(new GraphicsColor(204, 51, 204), decoder.Register(4));
    }

    [Fact]
    public void SeveralLocationsInOneCommand()
    {
        // The manual's example verbatim, A suboption and all. On a VT340 the A has no effect,
        // because there is one set of values for both colour and monochrome modes.
        var surface = Surface();
        var decoder = new RegisDecoder();

        decoder.Decode("S(M1(AH60L80S60)3(AH150L50S60))", surface);

        Assert.Equal(GraphicsColorMap.FromHls(60, 80, 60), decoder.Register(1));
        Assert.Equal(GraphicsColorMap.FromHls(150, 50, 60), decoder.Register(3));
    }

    [Fact]
    public void ABareLetterNamesABasicColour()
    {
        // "S(M1(R)) - Set color 1 to red". A letter with a number after it is a channel; a letter
        // without one is a colour name. That is the whole disambiguation.
        var surface = Surface();
        var decoder = new RegisDecoder();

        decoder.Decode("S(M1(R))", surface);

        var wanted = new GraphicsColorMap().Register(2);   // table 2-3's red
        Assert.Equal(wanted, decoder.Register(1));
    }

    [Fact]
    public void AndTheColourItSetsIsWhatGetsDrawn()
    {
        // The map is not an ornament: W(I n) picks a location and the vector uses it.
        var surface = Surface();
        var decoder = new RegisDecoder();

        decoder.Decode("S(M7(R0G0B100))W(I7)P[0,0]V[10,0]", surface);

        var pixel = surface.GetPixel(5, 0);
        Assert.False(pixel.IsTransparent);
        Assert.True(pixel.B > pixel.R && pixel.B > pixel.G, $"drew {pixel}");
    }

    [Fact]
    public void EraseStillWorksBesideAMapping()
    {
        // S(E) and S(M...) are options of the same command and a host may send both.
        var surface = Surface();
        var decoder = new RegisDecoder();
        decoder.Decode("W(I3)P[0,0]V[10,0]", surface);

        decoder.Decode("S(E M2(R100G0B0))", surface);

        Assert.True(surface.GetPixel(5, 0).IsTransparent, "S(E) should have erased the line");
        Assert.Equal(new GraphicsColor(255, 0, 0), decoder.Register(2));
    }

    [Fact]
    public void AnOutputMappingIsNoLongerCountedAsUnhandled()
    {
        // It used to fall through to the counter. A command that works must stop reporting itself
        // as one that does not, or the counter stops being a guide to what to build next.
        var surface = Surface();
        var decoder = new RegisDecoder();

        decoder.Decode("S(M0(H280L35S60))", surface);

        Assert.False(decoder.UnhandledCommands.ContainsKey('S'));
    }
}
