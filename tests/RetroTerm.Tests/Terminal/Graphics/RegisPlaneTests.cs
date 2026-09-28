using RetroTerm.Core.Terminal.Graphics;
using Xunit;

namespace RetroTerm.Tests.Terminal.Graphics;

/// <summary>
/// ReGIS plane select, negative pattern control and the writing styles - chapter 3's remaining
/// write controls.
/// </summary>
/// <remarks>
/// <para><b>Why these needed pixel memory to be rebuilt first</b></para>
/// A VT340 does not store a colour per pixel. It stores a four-bit CODE, one bit in each of four
/// planes, and the output map turns that code into a colour as the screen is scanned. Every option
/// tested here is an operation on the code:
///  - the plane mask decides which bits of the code a drawing may change.
///  - negative pattern control inverts pattern memory.
///  - complement writing exclusive-ors the selected planes.
///  - changing the output map repaints what is already drawn, without redrawing it.
///
/// A surface that only remembered the resolved colour could express none of them, which is why the
/// codes are now kept alongside the pixels.
///
/// hackerb9's <c>registest-bitplane.regis</c> uses all of these together, and a photograph of a real
/// VT340 running it sits beside it in the corpus.
/// </remarks>
public class RegisPlaneTests
{
    /// <summary>
    /// Runs some ReGIS on a small surface and hands the surface back.
    /// </summary>
    /// <param name="commands">
    /// The ReGIS to run.
    /// </param>
    /// <returns>
    /// The surface it drew on.
    /// </returns>
    private static InMemoryGraphicsSurface Draw(string commands)
    {
        var surface = new InMemoryGraphicsSurface(40, 20);
        new RegisDecoder().Decode(commands, surface);
        return surface;
    }

    /// <summary>
    /// The colour the default map holds in one location.
    /// </summary>
    /// <param name="index">
    /// Output map location.
    /// </param>
    /// <returns>
    /// The colour.
    /// </returns>
    private static GraphicsColor MapColour(int index) => new GraphicsColorMap().Register(index);

    [Fact]
    public void WritingToOnePlaneStoresOnlyThatPlanesBit()
    {
        // W(F1) allows plane 0 only. Drawing code 15 through it leaves code 1, because the other
        // three bits keep the background value they already had.
        var surface = Draw("W(F1I15)P[0,5]V[9,5]");

        Assert.Equal(MapColour(1).Value, surface.GetPixel(4, 5).Value);
    }

    [Fact]
    public void WritingToOnePlaneIsNotTheColourItAskedFor()
    {
        // The point of the test above, stated the other way round. Before pixel memory existed this
        // drew map location 15 - light grey - because the mask had nothing to act on.
        var surface = Draw("W(F1I15)P[0,5]V[9,5]");

        Assert.NotEqual(MapColour(15).Value, surface.GetPixel(4, 5).Value);
    }

    [Fact]
    public void TwoPlanesOverlapIntoTheCodeThatIsBothOfThem()
    {
        // A line in plane 0, then a line in plane 1 crossing it. Where they cross the code is 3,
        // which is what makes the bitplane drawing's overlaps a third colour rather than the last
        // one drawn. This is the whole reason that picture exists.
        var surface = Draw("W(F1I15)P[0,5]V[19,5]W(F2I15)P[10,0]V[10,15]");

        Assert.Equal(MapColour(1).Value, surface.GetPixel(4, 5).Value);
        Assert.Equal(MapColour(2).Value, surface.GetPixel(10, 2).Value);
        Assert.Equal(MapColour(3).Value, surface.GetPixel(10, 5).Value);
    }

    [Fact]
    public void RestoringAllPlanesWritesTheWholeCodeAgain()
    {
        // "Remember to restore writing to all planes after using 1-plane or no-plane writing."
        var surface = Draw("W(F1I15)P[0,5]V[9,5]W(F15I15)P[0,5]V[9,5]");

        Assert.Equal(MapColour(15).Value, surface.GetPixel(4, 5).Value);
    }

    [Fact]
    public void NoPlaneWritingDrawsNothing()
    {
        // W(F0) selects no planes, so the code cannot change and the pixel stays as it was.
        var surface = Draw("W(F0I15)P[0,5]V[9,5]");

        Assert.True(surface.GetPixel(4, 5).IsTransparent);
    }

    [Fact]
    public void NegativePatternControlInvertsThePattern()
    {
        // "The negative pattern control changes all 1s in pattern memory to 0s, and changes all 0s
        // to 1s." Standard pattern 4 is 10101010, so with negation on the line starts dark.
        var surface = Draw("W(P4(M1)N1)P[0,5]V[19,5]");

        Assert.True(surface.GetPixel(0, 5).IsTransparent);
        Assert.False(surface.GetPixel(1, 5).IsTransparent);
        Assert.True(surface.GetPixel(2, 5).IsTransparent);
    }

    [Fact]
    public void NegativePatternControlCanBeTurnedBackOff()
    {
        // W(N0) restores it, and the pattern means the same bits it did before.
        var surface = Draw("W(P4(M1)N1)W(N0)P[0,5]V[19,5]");

        Assert.False(surface.GetPixel(0, 5).IsTransparent);
        Assert.True(surface.GetPixel(1, 5).IsTransparent);
    }

    [Fact]
    public void ComplementWritingFlipsTheSelectedPlanes()
    {
        // Table 3-4: complement writing changes the bitmap value to its opposite, and only within
        // the planes the mask allows. Drawing code 15, then complementing plane 0, turns 15 into 14.
        var surface = Draw("W(I15)P[0,5]V[19,5]W(F1C)P[0,5]V[9,5]");

        Assert.Equal(MapColour(14).Value, surface.GetPixel(4, 5).Value);
        Assert.Equal(MapColour(15).Value, surface.GetPixel(15, 5).Value);
    }

    [Fact]
    public void ComplementWritingIgnoresTheForegroundIntensity()
    {
        // The table on printed page 56 says so: "Complement ... Ignores the foreground intensity."
        // So the same complement gives the same answer whatever colour was selected with it.
        var withOne = Draw("W(I15)P[0,5]V[19,5]W(F1C I1)P[0,5]V[9,5]");
        var withSeven = Draw("W(I15)P[0,5]V[19,5]W(F1C I7)P[0,5]V[9,5]");

        Assert.Equal(withOne.GetPixel(4, 5).Value, withSeven.GetPixel(4, 5).Value);
    }

    [Fact]
    public void ChangingTheOutputMapRepaintsWhatIsAlreadyDrawn()
    {
        // The heart of it: nothing is redrawn. A line drawn in code 3 changes colour because the
        // map location it points at changed, exactly as a real terminal's scan-out would.
        var surface = Draw("W(I3)P[0,5]V[19,5]S(M3(R100G0B0))");

        var pixel = surface.GetPixel(4, 5);
        Assert.Equal(255, pixel.R);
        Assert.Equal(0, pixel.G);
        Assert.Equal(0, pixel.B);
    }

    [Fact]
    public void ChangingAnUnusedMapLocationLeavesTheDrawingAlone()
    {
        // Only the code that moved repaints. Location 9 is not on the screen, so nothing changes.
        var surface = Draw("W(I3)P[0,5]V[19,5]S(M9(R100G0B0))");

        Assert.Equal(MapColour(3).Value, surface.GetPixel(4, 5).Value);
    }

    [Fact]
    public void PlaneSelectAndNegateAreNotCountedAsUnhandled()
    {
        var surface = new InMemoryGraphicsSurface(40, 20);
        var decoder = new RegisDecoder();

        decoder.Decode("W(F5)W(N1)W(V)W(C)W(E)", surface);

        Assert.False(decoder.UnhandledCommands.ContainsKey('W'));
    }
}
