using System;
using RetroTerm.Core.Terminal.Graphics;
using Xunit;

namespace RetroTerm.Tests.Terminal.Graphics;

/// <summary>
/// ReGIS polygon fill - the <c>F</c> command.
/// </summary>
/// <remarks>
/// <para><b>F has no geometry of its own</b></para>
/// Chapter 11 of <c>spec\DEC\EK-VT3XX-GP-002_VT330_VT340_Graphics_Programming_May88.pdf</c>: the
/// polygon fill command "accepts all vector command options and arguments", and the same sentence
/// again for curve, position and write control. So these tests are as much about F re-using the
/// existing V, C, P and W handlers as about the filling.
///
/// <para><b>The command strings are the manual's</b></para>
/// The square and the circle below are Figures 11-1 and 11-2, scaled down to fit a test surface.
/// </remarks>
public class RegisPolygonFillTests
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
    public void AFilledSquareIsSolidInsideAndEmptyOutside()
    {
        // Figure 11-1, FILLED SQUARE: P[..] F(V(B) [+100] [,+100] [-100] (E)).
        var decoder = new RegisDecoder();
        var surface = Surface();

        decoder.Decode("P[10,10] F(V(B)[+20][,+20][-20](E))".AsSpan(), surface);

        Assert.False(surface.GetPixel(20, 20).IsTransparent, "the middle should be filled");
        Assert.True(surface.GetPixel(40, 40).IsTransparent, "outside should be empty");
        Assert.True(LitPixels(surface) > 300, "a 20x20 filled square should be about 400 pixels");
    }

    [Fact]
    public void AnUnfilledOutlineIsHollowAndAFilledOneIsNot()
    {
        // The negative of the test above: the same shape without the F must be an outline only.
        var outline = Surface();
        var filled = Surface();

        new RegisDecoder().Decode("P[10,10] V[+20][,+20][-20][,-20]".AsSpan(), outline);
        new RegisDecoder().Decode("P[10,10] F(V[+20][,+20][-20][,-20])".AsSpan(), filled);

        Assert.True(outline.GetPixel(20, 20).IsTransparent, "the outline should be hollow");
        Assert.False(filled.GetPixel(20, 20).IsTransparent, "the fill should be solid");
        Assert.True(LitPixels(filled) > LitPixels(outline));
    }

    [Fact]
    public void AFilledCircleIsSolid()
    {
        // Figure 11-2, FILLED CIRCLE: P[500,300] F(C[+100]).
        var decoder = new RegisDecoder();
        var surface = Surface();

        decoder.Decode("P[32,32] F(C[+20])".AsSpan(), surface);

        Assert.False(surface.GetPixel(32, 32).IsTransparent, "the centre should be filled");
        Assert.False(surface.GetPixel(32, 20).IsTransparent, "and so should a point inside the rim");
        Assert.True(surface.GetPixel(2, 2).IsTransparent, "the corner is outside the circle");

        // A 64-sided polygon standing in for radius 20 should be within a few percent of pi r^2.
        int area = LitPixels(surface);
        Assert.True(area > 1100 && area < 1350, $"a filled circle of radius 20 covered {area}");
    }

    [Fact]
    public void FewerThanThreeVerticesDrawsNothing()
    {
        // "You must specify at least three different vertices, or ReGIS will not draw an image."
        var decoder = new RegisDecoder();
        var surface = Surface();

        decoder.Decode("P[10,10] F(V[+20])".AsSpan(), surface);

        Assert.Equal(0, LitPixels(surface));
    }

    [Fact]
    public void TheCursorGoesBackWhereItStarted()
    {
        // "ReGIS saves the cursor position at the beginning of any polygon fill command. The cursor
        // returns to this position at the end of the command (whether or not any drawing
        // takes place)."
        var decoder = new RegisDecoder();
        var surface = Surface();

        decoder.Decode("P[10,10] F(V[+20][,+20][-20][,-20])".AsSpan(), surface);

        Assert.Equal(10, decoder.CurrentX);
        Assert.Equal(10, decoder.CurrentY);
    }

    [Fact]
    public void TheCursorGoesBackEvenWhenNothingWasDrawn()
    {
        var decoder = new RegisDecoder();
        var surface = Surface();

        decoder.Decode("P[10,10] F(V[+20])".AsSpan(), surface);

        Assert.Equal(10, decoder.CurrentX);
        Assert.Equal(10, decoder.CurrentY);
    }

    [Fact]
    public void AWriteControlInsideAFillIsTemporary()
    {
        // Chapter 11 calls the W option inside an F a TEMPORARY write control, so the register
        // must be back to what it was once the fill is over.
        var decoder = new RegisDecoder();
        var surface = Surface();

        decoder.Decode("W(I2) P[10,10] F(W(I5) V[+20][,+20][-20][,-20])".AsSpan(), surface);

        // The fill used register 5 ...
        Assert.Equal(decoder.Register(5), surface.GetPixel(20, 20));

        // ... and the vector after it is back on register 2.
        decoder.Decode("P[0,50] V[+40]".AsSpan(), surface);
        Assert.Equal(decoder.Register(2), surface.GetPixel(20, 50));
    }

    [Fact]
    public void OnlyTheLastWriteControlInAFillCounts()
    {
        // "Only the last W option in a polygon fill command affects the graphic image ... because
        // ReGIS does not draw the image until the end of the polygon fill command."
        var decoder = new RegisDecoder();
        var surface = Surface();

        decoder.Decode("P[10,10] F(W(I2) V[+20][,+20] W(I5) V[-20][,-20])".AsSpan(), surface);

        Assert.Equal(decoder.Register(5), surface.GetPixel(20, 20));
    }

    [Fact]
    public void AMacrographCanBeFilled()
    {
        // "F (@A) Fills polygon" - the manual's own idiom for giving a filled shape a contrasting
        // outline: define the outline once, fill it, then draw it.
        var decoder = new RegisDecoder();
        var surface = Surface();

        decoder.Decode("@:X V[+20][,+20][-20][,-20] @;".AsSpan(), surface);
        decoder.Decode("P[10,10] F(@X)".AsSpan(), surface);

        Assert.False(surface.GetPixel(20, 20).IsTransparent, "the filled macrograph drew nothing");
    }

    [Fact]
    public void AFillInsideAFillIsCountedRatherThanGuessedAt()
    {
        // The chapter does not define a nested F, so it is skipped like any other command this
        // decoder does not implement - which also keeps the single vertex buffer honest.
        var decoder = new RegisDecoder();
        var surface = Surface();

        decoder.Decode("P[10,10] F(V[+20][,+20] F(V[+5]) V[-20][,-20])".AsSpan(), surface);

        Assert.True(decoder.UnhandledCommands.ContainsKey('F'));
        Assert.False(surface.GetPixel(20, 20).IsTransparent, "the outer fill should still work");
    }

    [Fact]
    public void AnFWithNoParenthesesIsCountedRatherThanDrawn()
    {
        var decoder = new RegisDecoder();
        var surface = Surface();

        decoder.Decode("P[10,10] F V[+20]".AsSpan(), surface);

        Assert.True(decoder.UnhandledCommands.ContainsKey('F'));
    }

    [Fact]
    public void PositionInsideAFillMovesWithoutAddingAnEdge()
    {
        // "Remember, position options do not draw graphic images as do the curve and vector
        // options." A P inside an F relocates the pen; the vertex it lands on only becomes part of
        // the figure when a vector or curve uses it.
        var decoder = new RegisDecoder();
        var surface = Surface();

        decoder.Decode("P[10,10] F(V[+20][,+20][-20][,-20] P[50,50])".AsSpan(), surface);

        Assert.False(surface.GetPixel(20, 20).IsTransparent, "the square should still be filled");
        Assert.True(surface.GetPixel(50, 55).IsTransparent, "the P should not have stretched it");
    }
}
