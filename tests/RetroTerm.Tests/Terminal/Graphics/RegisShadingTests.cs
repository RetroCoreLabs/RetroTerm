using RetroTerm.Core.Terminal.Graphics;
using Xunit;

namespace RetroTerm.Tests.Terminal.Graphics;

/// <summary>
/// ReGIS shading - the <c>W(S)</c> write control, from printed pages 67 to 82.
/// </summary>
/// <remarks>
/// <para><b>Shading is not a fill command</b></para>
/// "During shading commands, vector and curve commands operate as usual. However, as each point in
/// a vector or curve is drawn, shading occurs from that point to a point on a shading reference
/// line. The shading includes the point being drawn, as well as the point on the reference line."
///
/// So a shaded circle is not computed as a disc: the top and bottom arcs each shade down to the
/// same row, and the inside is covered because the two runs meet. That is why this lives on the
/// pixel write rather than in the decoder - every primitive gets it, and no primitive needed its
/// point walk written out a second time.
///
/// hackerb9's <c>registest-raf.regis</c> draws three shaded circles, and its capture of a real
/// VT340 shows them as solid roundels. Unshaded they would be three outlines.
/// </remarks>
public class RegisShadingTests
{
    /// <summary>
    /// Runs some ReGIS on a surface of the given size.
    /// </summary>
    /// <param name="commands">
    /// The ReGIS to run.
    /// </param>
    /// <param name="width">
    /// Surface width.
    /// </param>
    /// <param name="height">
    /// Surface height.
    /// </param>
    /// <returns>
    /// The surface it drew on.
    /// </returns>
    private static InMemoryGraphicsSurface Draw(string commands, int width = 40, int height = 30)
    {
        var surface = new InMemoryGraphicsSurface(width, height);
        new RegisDecoder().Decode(commands, surface);
        return surface;
    }

    /// <summary>
    /// Counts the lit pixels on the surface.
    /// </summary>
    /// <param name="surface">
    /// The surface to measure.
    /// </param>
    /// <returns>
    /// How many pixels carry ink.
    /// </returns>
    private static int LitPixels(InMemoryGraphicsSurface surface)
    {
        int lit = 0;
        for (int y = 0; y < surface.Height; y++)
        {
            for (int x = 0; x < surface.Width; x++)
            {
                if (!surface.GetPixel(x, y).IsTransparent) lit++;
            }
        }

        return lit;
    }

    [Fact]
    public void ShadingFillsFromTheLineDownToTheReferenceRow()
    {
        // The reference line is the cursor's row when shading was turned on - row 20 here. A line
        // drawn along row 5 therefore fills the whole band between the two.
        var surface = Draw("P[0,20]W(S1)P[0,5]V[9,5]");

        for (int y = 5; y <= 20; y++)
        {
            Assert.False(surface.GetPixel(4, y).IsTransparent, $"row {y} should be shaded");
        }

        Assert.True(surface.GetPixel(4, 21).IsTransparent, "shading should stop at the reference");
        Assert.True(surface.GetPixel(4, 4).IsTransparent, "nothing above the line should be shaded");
    }

    [Fact]
    public void ShadingIncludesTheReferenceLineItself()
    {
        // "shading always includes the shading reference line".
        var surface = Draw("P[0,20]W(S1)P[0,5]V[9,5]");

        Assert.False(surface.GetPixel(4, 20).IsTransparent);
    }

    [Fact]
    public void ShadingCanRunUpwardsAsWellAsDown()
    {
        // Nothing says the drawing has to be above the reference line, and a graph with negative
        // values puts it below.
        var surface = Draw("P[0,5]W(S1)P[0,20]V[9,20]");

        for (int y = 5; y <= 20; y++)
        {
            Assert.False(surface.GetPixel(4, y).IsTransparent, $"row {y} should be shaded");
        }
    }

    [Fact]
    public void ShadingOffLeavesAPlainLine()
    {
        var surface = Draw("P[0,20]W(S1)W(S0)P[0,5]V[9,5]");

        Assert.False(surface.GetPixel(4, 5).IsTransparent);
        Assert.True(surface.GetPixel(4, 6).IsTransparent);
    }

    [Fact]
    public void AReferenceLineCanBeNamedInsteadOfTakenFromTheCursor()
    {
        // W(S1[,125]) is the manual's own example, on printed page 76.
        var surface = Draw("P[0,0]W(S1[,25])P[0,5]V[9,5]");

        Assert.False(surface.GetPixel(4, 25).IsTransparent);
        Assert.True(surface.GetPixel(4, 4).IsTransparent);
    }

    [Fact]
    public void NamingAReferenceLineDoesNotMoveTheDrawingPoint()
    {
        // The reference line is a parameter, not a position. If reading it moved the cursor, the
        // vector that follows would start somewhere else entirely.
        var named = Draw("P[0,5]W(S1[,25])V[9,5]");
        var plain = Draw("P[0,25]W(S1)P[0,5]V[9,5]");

        Assert.Equal(LitPixels(plain), LitPixels(named));
    }

    [Fact]
    public void AVerticalReferenceLineShadesSideways()
    {
        // W(S(X)[x]) selects "a vertical (X-axis) shading reference line".
        var surface = Draw("W(S(X)[30])P[5,0]V[5,9]");

        for (int x = 5; x <= 30; x++)
        {
            Assert.False(surface.GetPixel(x, 4).IsTransparent, $"column {x} should be shaded");
        }

        Assert.True(surface.GetPixel(31, 4).IsTransparent);
    }

    [Fact]
    public void AShadedCircleComesOutSolid()
    {
        // Figure 3-11: the arcs shade to the same line from both sides, so the inside is covered
        // without anyone computing a disc. The centre is the test - an unshaded circle is hollow.
        var surface = Draw("P[20,15]W(S1)C[30,15]", 60, 40);

        Assert.False(surface.GetPixel(20, 15).IsTransparent, "the centre should be filled");
        Assert.False(surface.GetPixel(20, 10).IsTransparent);
        Assert.False(surface.GetPixel(20, 20).IsTransparent);
    }

    [Fact]
    public void AnUnshadedCircleIsStillHollow()
    {
        // The other half of the test above - proof the fill came from shading and not from the
        // circle command having quietly started filling.
        var surface = Draw("P[20,15]C[30,15]", 60, 40);

        Assert.True(surface.GetPixel(20, 15).IsTransparent, "the centre should be empty");
    }

    [Fact]
    public void ShadingHonoursThePlaneMask()
    {
        // The table on printed page 67 lists plane select as applying to pattern shading. So a
        // shaded run written to one plane carries that plane's code, not the full drawing code.
        var surface = Draw("P[0,20]W(S1)W(F1I15)P[0,5]V[9,5]");
        var expected = new GraphicsColorMap().Register(1);

        Assert.Equal(expected.Value, surface.GetPixel(4, 12).Value);
    }

    [Fact]
    public void ShadingIsNotCountedAsUnhandled()
    {
        var surface = new InMemoryGraphicsSurface(40, 30);
        var decoder = new RegisDecoder();

        decoder.Decode("W(S1)W(S0)W(S1[,10])W(S(X)[10])", surface);

        Assert.False(decoder.UnhandledCommands.ContainsKey('W'));
    }
}
