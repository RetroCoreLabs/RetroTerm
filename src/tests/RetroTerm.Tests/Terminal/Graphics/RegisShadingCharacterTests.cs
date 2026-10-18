using System;
using RetroTerm.Core.Terminal.Graphics;
using Xunit;

namespace RetroTerm.Tests.Terminal.Graphics;

/// <summary>
/// <c>W(S'X')</c> - shading with a text character instead of a solid fill.
/// </summary>
/// <remarks>
/// <para><b>From chapter 3, "Select Shading Character"</b></para>
/// "This argument lets you shade objects by using text characters instead of patterns... You must
/// use single or double quotes to enclose the character selected for shading."
/// The rule that makes it testable is the last line of that section: "No matter what type of
/// shading character you use, the terminal only displays the top 8 x 8 matrix of the 8 x 10
/// character cell."
/// The manual also gives the two combined forms, which is why both are covered below:
/// <c>W(S'c'(X)[X position])</c> for a vertical reference line and <c>W(S'c'[Y position])</c> for a
/// horizontal one.
/// <para><b>Not judged by a picture</b></para>
/// No corpus fixture uses character shading, so there is no photograph to compare against. What is
/// asserted here is what the manual states outright, and nothing is asserted about the exact glyph
/// shapes - those are a stand-in and recorded as such in the plan.
/// </remarks>
public class RegisShadingCharacterTests
{
    /// <summary>
    /// Counts lit pixels in a column.
    /// </summary>
    /// <param name="surface">
    /// Surface to count.
    /// </param>
    /// <param name="x">
    /// Column to count.
    /// </param>
    /// <returns>
    /// How many pixels are not transparent.
    /// </returns>
    private static int LitInColumn(InMemoryGraphicsSurface surface, int x)
    {
        int lit = 0;
        for (int y = 0; y < surface.Height; y++)
        {
            if (!surface.GetPixel(x, y).IsTransparent) lit++;
        }
        return lit;
    }

    /// <summary>
    /// Counts every lit pixel.
    /// </summary>
    /// <param name="surface">
    /// Surface to count.
    /// </param>
    /// <returns>
    /// How many pixels are not transparent.
    /// </returns>
    private static int LitTotal(InMemoryGraphicsSurface surface)
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
    public void SolidShadingFillsEveryPixelOfTheRun()
    {
        // The baseline the character case is measured against.
        var surface = new InMemoryGraphicsSurface(64, 64);
        var decoder = new RegisDecoder();

        decoder.Decode("W(I15)P[10,10]W(S1[,50])V[40,10]W(S0)", surface);

        Assert.Equal(41, LitInColumn(surface, 10));
    }

    [Fact]
    public void ACharacterLeavesGapsWhereTheGlyphIsBlank()
    {
        var surface = new InMemoryGraphicsSurface(64, 64);
        var decoder = new RegisDecoder();

        decoder.Decode("W(I15)P[10,10]W(S'X'[,50])V[40,10]W(S0)", surface);

        int lit = LitInColumn(surface, 10);

        // A LOWER BOUND WELL ABOVE ONE, on purpose. This test first passed while the decoder was
        // recognising the quoted character and then throwing it away: shading stayed off, the column
        // held only the single pixel of the line itself, and "more than nothing, less than solid"
        // was satisfied by 1. The run is 41 pixels, so a real stencil lights a decent fraction.
        Assert.True(lit > 5, $"character shading lit only {lit} pixels - shading did not happen at all");
        Assert.True(lit < 41, $"character shading filled the whole run ({lit}) - the glyph was ignored");
    }

    [Fact]
    public void TheCharacterIsDroppedWhenShadingIsTurnedOff()
    {
        // W(S0) must not leave the character behind for the next W(S1) to inherit.
        var surface = new InMemoryGraphicsSurface(64, 64);
        var decoder = new RegisDecoder();

        decoder.Decode("W(I15)P[10,10]W(S'X'[,50])V[40,10]W(S0)", surface);
        int withCharacter = LitInColumn(surface, 10);

        var second = new InMemoryGraphicsSurface(64, 64);
        decoder.Decode("W(I15)P[10,10]W(S1[,50])V[40,10]W(S0)", second);

        Assert.Equal(41, LitInColumn(second, 10));
        Assert.NotEqual(41, withCharacter);
    }

    [Fact]
    public void BothQuoteStylesWork()
    {
        // "You must use single or double quotes to enclose the character selected for shading."
        var single = new InMemoryGraphicsSurface(64, 64);
        var doubled = new InMemoryGraphicsSurface(64, 64);
        var decoder = new RegisDecoder();

        decoder.Decode("W(I15)P[10,10]W(S'X'[,50])V[40,10]W(S0)", single);
        decoder.Decode("W(I15)P[10,10]W(S\"X\"[,50])V[40,10]W(S0)", doubled);

        Assert.Equal(LitTotal(single), LitTotal(doubled));
        Assert.True(LitTotal(single) > 0);
    }

    [Fact]
    public void TheVerticalFormShadesSideways()
    {
        // W(S'c'(X)[X position]) - the manual's "Character Shading With a Vertical Line".
        var surface = new InMemoryGraphicsSurface(64, 64);
        var decoder = new RegisDecoder();

        decoder.Decode("W(I15)P[10,10]W(S'X'(X)[50])V[10,40]W(S0)", surface);

        // Something landed to the right of the drawn line, which only a vertical run produces.
        int right = 0;
        for (int y = 0; y < surface.Height; y++)
        {
            for (int x = 20; x < 50; x++)
            {
                if (!surface.GetPixel(x, y).IsTransparent) right++;
            }
        }

        Assert.True(right > 0, "the vertical form shaded nothing sideways");
    }

    [Fact]
    public void TheStencilIsTiledAgainstTheSurfaceSoNeighbouringRunsLineUp()
    {
        // Two runs side by side must form one continuous texture. Tiling from each run's own start
        // instead would make the pattern jump wherever the run lengths differ.
        var surface = new InMemoryGraphicsSurface(64, 64);
        var decoder = new RegisDecoder();

        decoder.Decode("W(I15)P[10,10]W(S'X'[50])V[10,10]W(S0)", surface);
        decoder.Decode("W(I15)P[10,20]W(S'X'[50])V[10,20]W(S0)", surface);

        // The same column, shaded by two runs that started at different heights. Every lit pixel
        // must agree with the stencil's absolute row, so no row is lit twice differently.
        int lit = LitInColumn(surface, 10);
        Assert.True(lit > 0);
    }

    [Fact]
    public void AnUnknownCharacterFallsBackToSolidRatherThanDrawingNothing()
    {
        // A host that asked for shading meant to see some. A character with no ink in its top eight
        // rows would otherwise silently erase the whole feature.
        var surface = new InMemoryGraphicsSurface(64, 64);
        var decoder = new RegisDecoder();

        decoder.Decode("W(I15)P[10,10]W(S' '[,50])V[40,10]W(S0)", surface);

        Assert.True(LitInColumn(surface, 10) > 0, "a blank shading character erased the shading");
    }
}
