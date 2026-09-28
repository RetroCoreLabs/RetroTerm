using RetroTerm.Core.Terminal.Graphics;
using Xunit;

namespace RetroTerm.Tests.Terminal.Graphics;

/// <summary>
/// The ReGIS pixel vector system - bare digits that move and draw in eight fixed directions.
/// </summary>
/// <remarks>
/// <para><b>Where the numbering comes from</b></para>
/// Figure 1-2, "Pixel Vector Directions", which OCRs as noise - the page was rendered and read.
/// The compass runs anticlockwise from east: 0 east, 1 north-east, 2 north, 3 north-west, 4 west,
/// 5 south-west, 6 south, 7 south-east.
///
/// Figure 1-3 pins it from the other side with five worked examples, and those are the tests below.
/// They are the manual's own arithmetic, not mine, which is why they are worth having even though
/// the direction table is only eight entries.
///
/// <para><b>Why this turned up now</b></para>
/// hackerb9's <c>faketextcolor.sh</c> draws its boxes with <c>V(...)0642</c> and sets the step
/// length with <c>W(M20)</c>. Neither form was implemented, and neither was in the plan - the
/// fixture found them.
/// </remarks>
public class RegisPixelVectorTests
{
    /// <summary>
    /// Runs some ReGIS from the middle of a surface and reports where the drawing point ended.
    /// </summary>
    /// <param name="commands">
    /// The ReGIS to run, after a move to (50,50).
    /// </param>
    /// <returns>
    /// The final drawing point.
    /// </returns>
    private static (int X, int Y) EndPoint(string commands)
    {
        var surface = new InMemoryGraphicsSurface(100, 100);
        var decoder = new RegisDecoder();

        decoder.Decode("P[50,50]" + commands + "R(P)", surface);

        // The report command is the decoder's own answer for where the cursor is, so the position
        // is read the way a host would read it rather than through a test-only accessor.
        string report = decoder.TakeReports().Trim('\r');
        string inner = report.Substring(1, report.Length - 2);
        string[] parts = inner.Split(',');

        return (int.Parse(parts[0]), int.Parse(parts[1]));
    }

    [Fact]
    public void ZeroGoesEastAndFourGoesWest()
    {
        // "you use a PV string of 444 to move three pixels to the left. You use a 000 string to
        // move three pixels to the right."
        Assert.Equal((53, 50), EndPoint("P000"));
        Assert.Equal((47, 50), EndPoint("P444"));
    }

    [Fact]
    public void FigureOneThreeExampleA()
    {
        // "MOVEMENT FROM CENTER BY SIX 2s AND THREE 4s" - up six, left three.
        Assert.Equal((47, 44), EndPoint("P222222444"));
    }

    [Fact]
    public void FigureOneThreeExampleB()
    {
        // "MOVEMENT FROM CENTER BY TWO 2s AND FIVE 4s" - up two, left five.
        Assert.Equal((45, 48), EndPoint("P2244444"));
    }

    [Fact]
    public void FigureOneThreeExampleC()
    {
        // "MOVEMENT FROM CENTER BY THREE 6s" - straight down. This is the one that settles which
        // way 6 points, and with it the whole compass.
        Assert.Equal((50, 53), EndPoint("P666"));
    }

    [Fact]
    public void FigureOneThreeExampleD()
    {
        // "MOVEMENT FROM CENTER BY SIX 6s AND TWO 0s" - down six, right two.
        Assert.Equal((52, 56), EndPoint("P66666600"));
    }

    [Fact]
    public void FigureOneThreeExampleE()
    {
        // "MOVEMENT FROM CENTER BY FIVE 1s" - north-east five, so up five and right five.
        Assert.Equal((55, 45), EndPoint("P11111"));
    }

    [Fact]
    public void TheMultiplierRepeatsEachStep()
    {
        // "suppose you use a multiplier of 10. Then each PV number in later commands specifies
        // movement for 10 coordinates, not just 1."
        Assert.Equal((70, 50), EndPoint("W(M10)P00"));
    }

    [Fact]
    public void TheMultiplierIsNotThePatternMultiplier()
    {
        // W(P4(M2)) sets the PATTERN multiplier and must leave the PV multiplier alone. They share
        // a letter and nothing else.
        //
        // The PV multiplier stays 10, so two steps move twenty and the point lands at 70. If the
        // option scan walked into the pattern's parenthesised group and read that M as PV
        // multiplication, the multiplier would drop to 2 and the answer would be 54.
        Assert.Equal((70, 50), EndPoint("W(M10)W(P4(M2))P00"));
    }

    [Fact]
    public void EightAndNineAreNotDirections()
    {
        // There are eight directions. A 9 is not one of them, and swallowing it would drag the
        // drawing point somewhere the host never asked for.
        Assert.Equal((50, 50), EndPoint("P9"));
    }

    [Fact]
    public void AVectorDigitDrawsAsWellAsMoves()
    {
        // The difference between P and V: both move, only V leaves ink.
        var surface = new InMemoryGraphicsSurface(100, 100);
        var decoder = new RegisDecoder();

        decoder.Decode("P[50,50]W(M10)V0", surface);

        Assert.False(surface.GetPixel(55, 50).IsTransparent, "the vector should have drawn");
        Assert.False(surface.GetPixel(60, 50).IsTransparent);
    }

    [Fact]
    public void APositionDigitMovesWithoutDrawing()
    {
        var surface = new InMemoryGraphicsSurface(100, 100);
        var decoder = new RegisDecoder();

        decoder.Decode("P[50,50]W(M10)P0", surface);

        Assert.True(surface.GetPixel(55, 50).IsTransparent, "position must not draw");
    }

    [Fact]
    public void TheBoxFromTheFixtureClosesOnItself()
    {
        // 0642 - "goes east, south, west, and then north, making a 20x20 square", which is exactly
        // what faketextcolor.sh draws sixteen of. If the compass were wrong in any one direction
        // the shape would not return to where it started.
        Assert.Equal((50, 50), EndPoint("W(M20)V0642"));
    }

    [Fact]
    public void ThePixelVectorFormsAreNotCountedAsUnhandled()
    {
        var surface = new InMemoryGraphicsSurface(100, 100);
        var decoder = new RegisDecoder();

        decoder.Decode("W(M20,I0)P[10,10]V0642", surface);

        Assert.False(decoder.UnhandledCommands.ContainsKey('W'));
        Assert.False(decoder.UnhandledCommands.ContainsKey('V'));
    }
}
