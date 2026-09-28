using RetroTerm.Core.Terminal.Graphics;
using Xunit;

namespace RetroTerm.Tests.Terminal.Graphics;

/// <summary>
/// ReGIS pattern memory - the <c>W(P...)</c> write control, from chapter 3.
/// </summary>
/// <remarks>
/// <para><b>This one has a hardware capture behind it</b></para>
/// hackerb9's <c>registest.sh</c> draws its grid with <c>W(P10(M2))</c> and <c>W(P1(M10))</c>, and
/// the photograph of a real VT340 beside it shows the result. Measured on the bottom rule of that
/// grid, the hardware lights 400 pixels of 800 and this decoder used to light all 800: every line
/// was solid where the hardware's was dashed.
///
/// The bit patterns are DEC's own, table 3-1, so unlike the Tektronix dash masks these can be
/// asserted exactly rather than judged by eye.
/// </remarks>
public class RegisPatternTests
{
    /// <summary>
    /// Draws one horizontal line the width of the surface and reports which pixels were lit.
    /// </summary>
    /// <param name="commands">
    /// ReGIS to run before the line.
    /// </param>
    /// <param name="length">
    /// How long to draw the line.
    /// </param>
    /// <returns>
    /// One flag per pixel along the line.
    /// </returns>
    private static bool[] LinePixels(string commands, int length = 64)
    {
        var surface = new InMemoryGraphicsSurface(length + 10, 20);
        var decoder = new RegisDecoder();

        decoder.Decode(commands + "P[0,5]V[" + (length - 1) + ",5]", surface);

        var lit = new bool[length];
        for (int x = 0; x < length; x++)
        {
            lit[x] = !surface.GetPixel(x, 5).IsTransparent;
        }

        return lit;
    }

    /// <summary>
    /// How many of the pixels are lit.
    /// </summary>
    private static int Count(bool[] lit)
    {
        int n = 0;
        for (int i = 0; i < lit.Length; i++)
        {
            if (lit[i]) n++;
        }
        return n;
    }

    [Fact]
    public void ThePowerOnPatternDrawsASolidLine()
    {
        // "The default for pattern memory is all 1s."
        var lit = LinePixels("");

        Assert.Equal(lit.Length, Count(lit));
    }

    [Fact]
    public void TheDefaultMultiplierIsTwo()
    {
        // "The minimum value is 1, the maximum value is 16. The default value is 2." The note under
        // figure 3-12 says the same thing from the other direction, listing the default write state
        // as W(F3, N0, V, I0, P1(M2)).
        //
        // So a pattern given with no multiplier covers two pixels a bit: standard pattern 4 is
        // 10101010, which at M2 is two lit, two dark. TAKEN FROM THE MANUAL, NOT FROM HARDWARE -
        // every registest drawing gives its multiplier explicitly, so none of them can tell.
        var lit = LinePixels("W(P4)");

        Assert.True(lit[0]);
        Assert.True(lit[1]);
        Assert.False(lit[2]);
        Assert.False(lit[3]);
    }

    [Fact]
    public void AMultiplierAboveSixteenIsClampedToSixteen()
    {
        // "the maximum value is 16".
        var lit = LinePixels("W(P4(M99))", 96);

        for (int i = 0; i < 16; i++)
        {
            Assert.True(lit[i], $"pixel {i} should be lit - the first bit covers sixteen pixels");
        }

        Assert.False(lit[16], "the second bit of 10101010 is off");
    }

    [Fact]
    public void StandardPatternZeroDrawsNothing()
    {
        // Table 3-1: pattern 0 is 00000000, the all-off write pattern.
        var lit = LinePixels("W(P0)");

        Assert.Equal(0, Count(lit));
    }

    [Fact]
    public void StandardPatternFourIsEveryOtherPixel()
    {
        // Table 3-1: pattern 4 is 10101010, the dot pattern. The leftmost bit is drawn first, so
        // the line starts lit and alternates.
        var lit = LinePixels("W(P4(M1))");

        Assert.True(lit[0]);
        Assert.False(lit[1]);
        Assert.True(lit[2]);
        Assert.False(lit[3]);
        Assert.Equal(lit.Length / 2, Count(lit));
    }

    [Fact]
    public void StandardPatternTwoIsFourOnAndFourOff()
    {
        // Table 3-1: pattern 2 is 11110000, the dash pattern.
        var lit = LinePixels("W(P2(M1))");

        for (int i = 0; i < 4; i++)
        {
            Assert.True(lit[i], $"pixel {i} should be lit");
        }

        for (int i = 4; i < 8; i++)
        {
            Assert.False(lit[i], $"pixel {i} should be dark");
        }

        Assert.True(lit[8], "the pattern should repeat at the ninth pixel");
    }

    [Fact]
    public void ABinaryPatternIsRepeatedToFillTheMemory()
    {
        // "a pattern of 1 and 0 bits, from 2 to 8 bits long ... If you specify less than 8 bits, the
        // terminal repeats as much of your pattern as it can". W(P01) is the manual's own first
        // example, and its note says it gives the same shape as P4 with the values reversed.
        var lit = LinePixels("W(P01(M1))");

        Assert.False(lit[0]);
        Assert.True(lit[1]);
        Assert.False(lit[2]);
        Assert.True(lit[3]);
        Assert.Equal(lit.Length / 2, Count(lit));
    }

    [Fact]
    public void TheMultiplierStretchesEachBit()
    {
        // W(P10(M2)) - what registest.sh draws its outer rules with. Two bits, "10", each covering
        // two pixels: two lit, two dark, and half the line in total. The hardware capture measures
        // 400 lit pixels of 800 on exactly this.
        var lit = LinePixels("W(P10(M2))");

        Assert.True(lit[0]);
        Assert.True(lit[1]);
        Assert.False(lit[2]);
        Assert.False(lit[3]);
        Assert.Equal(lit.Length / 2, Count(lit));
    }

    [Fact]
    public void AMultipliedAllOnPatternIsStillSolid()
    {
        // W(P1(M10)) - the other one registest.sh uses, for its interior grid lines. Standard
        // pattern 1 is all-on, so stretching it changes nothing and the line stays solid.
        var lit = LinePixels("W(P1(M10))");

        Assert.Equal(lit.Length, Count(lit));
    }

    [Fact]
    public void ThePatternCarriesAcrossTheCoordinatesOfOneCommand()
    {
        // A polyline is ONE dashed line. Restarting the pattern at every vertex would put a dash at
        // the start of each segment, so a curve sent as many short vectors would come out solid -
        // the same rule the Tektronix patterns needed.
        var surface = new InMemoryGraphicsSurface(40, 20);
        var decoder = new RegisDecoder();

        decoder.Decode("W(P2(M1))P[0,5]V[9,5][19,5]", surface);

        // With the pattern restarting per segment, pixel 10 would begin a fresh run of four lit
        // pixels. Carried, the second segment continues wherever the first left off.
        bool anyDark = false;
        for (int x = 10; x < 14; x++)
        {
            if (surface.GetPixel(x, 5).IsTransparent) anyDark = true;
        }

        Assert.True(anyDark, "the pattern restarted at the vertex instead of carrying across it");
    }

    [Fact]
    public void ANewCommandLetterRestartsThePattern()
    {
        // "The VT300 starts each writing task from the first position in pattern memory... unless
        // you use a new command key letter."
        var surface = new InMemoryGraphicsSurface(40, 20);
        var decoder = new RegisDecoder();

        decoder.Decode("W(P2(M1))P[0,5]V[5,5]P[10,5]V[19,5]", surface);

        // The second V starts a new writing task, so its first four pixels are lit.
        for (int x = 10; x < 14; x++)
        {
            Assert.False(surface.GetPixel(x, 5).IsTransparent,
                $"pixel {x} should be lit - a new command letter restarts the pattern");
        }
    }

    [Fact]
    public void AnUnknownWriteOptionIsStillCounted()
    {
        // A W that did nothing must say so, or the counter stops being a guide to what a real host
        // actually needs. 'Q' is not a write control option in chapter 3.
        var surface = new InMemoryGraphicsSurface(40, 20);
        var decoder = new RegisDecoder();

        decoder.Decode("W(Q7)", surface);

        Assert.Equal(1, decoder.UnhandledCommands['W']);
    }

    [Fact]
    public void ShadingWithACharacterIsNoLongerCountedAsUnhandled()
    {
        // This case used to assert the OPPOSITE: shading with a text character was recognised and
        // thrown away, so the W was counted as unhandled. It is built now - see
        // RegisShadingCharacterTests - so being counted would send a reader looking for a gap that
        // is closed.
        var surface = new InMemoryGraphicsSurface(40, 20);
        var decoder = new RegisDecoder();

        decoder.Decode("W(S1'x')", surface);

        Assert.False(decoder.UnhandledCommands.ContainsKey('W'));
    }

    [Fact]
    public void APatternedWriteIsNotCountedAsUnhandled()
    {
        var surface = new InMemoryGraphicsSurface(40, 20);
        var decoder = new RegisDecoder();

        decoder.Decode("W(P4)", surface);

        Assert.False(decoder.UnhandledCommands.ContainsKey('W'));
    }
}
