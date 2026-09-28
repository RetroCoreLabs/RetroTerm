using System;
using RetroTerm.Core.Terminal.Graphics;
using Xunit;

namespace RetroTerm.Tests.Terminal.Graphics;

/// <summary>
/// The graphics surface — the first block of the graphics foundation.
///
/// Four protocols are coming (ND, Tektronix 4010/4014, Sixel, ReGIS) and they share almost no
/// command semantics. What they do share is somewhere to put pixels, and this is it. Because it
/// lives in Core with no drawing library behind it, a graphics protocol can be tested by reading
/// the pixels back with no UI at all — which is the whole reason it is built before the first
/// protocol rather than after.
///
/// Two properties get the most attention here, because both are the kind of thing that looks fine
/// until a host does something ordinary: everything CLIPS instead of throwing, and a line drawn
/// from B to A lights exactly the pixels a line drawn from A to B did.
/// </summary>
public class InMemoryGraphicsSurfaceTests
{
    private static readonly GraphicsColor White = new GraphicsColor(255, 255, 255);
    private static readonly GraphicsColor Red = new GraphicsColor(255, 0, 0);

    private static InMemoryGraphicsSurface Surface(int width = 16, int height = 10)
        => new InMemoryGraphicsSurface(width, height);

    /// <summary>
    /// Counts pixels that would show something.
    /// </summary>
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

    // ─────────────────────────────────────────────────────────────
    // Colour
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void AColourKeepsItsChannels()
    {
        var colour = new GraphicsColor(0x12, 0x34, 0x56, 0x78);

        Assert.Equal(0x12, colour.R);
        Assert.Equal(0x34, colour.G);
        Assert.Equal(0x56, colour.B);
        Assert.Equal(0x78, colour.A);
    }

    [Fact]
    public void ColoursAreOpaqueUnlessToldOtherwise()
    {
        // A protocol drawing a line means it to be visible. Defaulting alpha to zero would make
        // every drawing command silently do nothing, which is a maddening bug to chase.
        Assert.True(new GraphicsColor(1, 2, 3).IsOpaque);
        Assert.True(GraphicsColor.Transparent.IsTransparent);
    }

    // ─────────────────────────────────────────────────────────────
    // A new surface, and clearing
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void ANewSurfaceIsCompletelyTransparent()
    {
        // Not black: a graphics plane sits OVER the text, and a plane that started opaque would
        // black out the screen the moment it existed.
        var surface = Surface();

        Assert.Equal(0, LitPixels(surface));
    }

    [Fact]
    public void ClearingFillsEveryPixel()
    {
        var surface = Surface();
        surface.Clear(Red);

        Assert.Equal(surface.Width * surface.Height, LitPixels(surface));
        Assert.Equal(Red, surface.GetPixel(0, 0));
        Assert.Equal(Red, surface.GetPixel(surface.Width - 1, surface.Height - 1));
    }

    [Fact]
    public void ClearingToTransparentErasesThePlane()
    {
        var surface = Surface();
        surface.Clear(Red);
        surface.Clear(GraphicsColor.Transparent);

        Assert.Equal(0, LitPixels(surface));
    }

    [Fact]
    public void ASurfaceMustHaveRealDimensions()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new InMemoryGraphicsSurface(0, 10));
        Assert.Throws<ArgumentOutOfRangeException>(() => new InMemoryGraphicsSurface(10, -1));
    }

    // ─────────────────────────────────────────────────────────────
    // Pixels, and clipping instead of throwing
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void APixelSurvivesBeingReadBack()
    {
        var surface = Surface();
        surface.SetPixel(3, 4, Red);

        Assert.Equal(Red, surface.GetPixel(3, 4));
        Assert.True(surface.GetPixel(4, 4).IsTransparent);
    }

    [Fact]
    public void PixelsOutsideTheSurfaceAreDroppedNotThrown()
    {
        // A host is entitled to send coordinates off the screen, and a terminal that fell over
        // when it did would be broken. Every one of these is ordinary traffic.
        var surface = Surface();

        surface.SetPixel(-1, 0, Red);
        surface.SetPixel(0, -1, Red);
        surface.SetPixel(surface.Width, 0, Red);
        surface.SetPixel(0, surface.Height, Red);
        surface.SetPixel(int.MinValue, int.MinValue, Red);

        Assert.Equal(0, LitPixels(surface));
    }

    [Fact]
    public void ReadingOutsideTheSurfaceIsTransparent()
    {
        // So a caller inspecting a region never has to bounds-check first.
        var surface = Surface();
        surface.Clear(Red);

        Assert.True(surface.GetPixel(-1, 0).IsTransparent);
        Assert.True(surface.GetPixel(surface.Width, 0).IsTransparent);
    }

    // ─────────────────────────────────────────────────────────────
    // Lines
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void ALineLightsBothOfItsEnds()
    {
        // Inclusive of both ends. A vector protocol draws a polygon as a chain of lines, and an
        // exclusive endpoint would leave a hole at every corner.
        var surface = Surface();
        surface.DrawLine(2, 1, 9, 6, White);

        Assert.Equal(White, surface.GetPixel(2, 1));
        Assert.Equal(White, surface.GetPixel(9, 6));
    }

    [Fact]
    public void AHorizontalLineIsExactlyAsLongAsItShouldBe()
    {
        var surface = Surface();
        surface.DrawLine(2, 5, 11, 5, White);

        Assert.Equal(10, LitPixels(surface));           // 2..11 inclusive
        for (int x = 2; x <= 11; x++)
        {
            Assert.Equal(White, surface.GetPixel(x, 5));
        }
    }

    [Fact]
    public void AVerticalLineIsExactlyAsLongAsItShouldBe()
    {
        var surface = Surface();
        surface.DrawLine(4, 1, 4, 8, White);

        Assert.Equal(8, LitPixels(surface));            // 1..8 inclusive
    }

    [Fact]
    public void ASinglePointIsALineOfOnePixel()
    {
        // The degenerate case, and the one most likely to spin forever if the stepping is wrong.
        var surface = Surface();
        surface.DrawLine(5, 5, 5, 5, White);

        Assert.Equal(1, LitPixels(surface));
        Assert.Equal(White, surface.GetPixel(5, 5));
    }

    [Fact]
    public void ALineIsTheSameDrawnFromEitherEnd()
    {
        // THE property that separates a correct Bresenham from one that merely looks right. If
        // the two differ, a shape's outline shifts by a pixel depending on the order a protocol
        // happened to emit its vectors, and nothing about the picture would explain why.
        var forward = Surface(32, 24);
        var backward = Surface(32, 24);

        forward.DrawLine(3, 2, 29, 21, White);
        backward.DrawLine(29, 21, 3, 2, White);

        for (int y = 0; y < forward.Height; y++)
        {
            for (int x = 0; x < forward.Width; x++)
            {
                Assert.True(forward.GetPixel(x, y) == backward.GetPixel(x, y),
                    $"pixel ({x},{y}) differs depending on which end the line was drawn from");
            }
        }
    }

    [Fact]
    public void ASteepLineIsDrawnWithoutGaps()
    {
        // A steep line has to step y every pixel and x only occasionally. Getting the two axes
        // the wrong way round gives a dotted line, which looks like a rendering artefact rather
        // than the arithmetic error it is.
        var surface = Surface(16, 40);
        surface.DrawLine(2, 1, 6, 38, White);

        // Exactly one lit pixel on every row the line spans.
        for (int y = 1; y <= 38; y++)
        {
            int litOnRow = 0;
            for (int x = 0; x < surface.Width; x++)
            {
                if (!surface.GetPixel(x, y).IsTransparent) litOnRow++;
            }
            Assert.True(litOnRow >= 1, $"row {y} has a gap in the line");
        }
    }

    [Fact]
    public void ALineRunningOffTheSurfaceDrawsThePartThatFits()
    {
        // Ordinary traffic for a vector protocol: the drawing is bigger than the screen.
        var surface = Surface(16, 10);
        surface.DrawLine(-50, 5, 50, 5, White);

        Assert.Equal(16, LitPixels(surface));            // the whole row, and nothing else
        for (int x = 0; x < 16; x++)
        {
            Assert.Equal(White, surface.GetPixel(x, 5));
        }
    }

    [Fact]
    public void ALineEntirelyOffTheSurfaceDrawsNothing()
    {
        var surface = Surface();
        surface.DrawLine(-20, -20, -5, -5, White);

        Assert.Equal(0, LitPixels(surface));
    }

    // ─────────────────────────────────────────────────────────────
    // Rectangles
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void ARectangleFillsExactlyItsArea()
    {
        var surface = Surface();
        surface.FillRectangle(2, 3, 4, 2, Red);

        Assert.Equal(8, LitPixels(surface));
        Assert.Equal(Red, surface.GetPixel(2, 3));
        Assert.Equal(Red, surface.GetPixel(5, 4));
        Assert.True(surface.GetPixel(6, 3).IsTransparent, "one column too far must stay clear");
        Assert.True(surface.GetPixel(2, 5).IsTransparent, "one row too far must stay clear");
    }

    [Fact]
    public void AnEmptyRectangleDrawsNothing()
    {
        var surface = Surface();
        surface.FillRectangle(2, 2, 0, 5, Red);
        surface.FillRectangle(2, 2, 5, 0, Red);
        surface.FillRectangle(2, 2, -5, -5, Red);

        Assert.Equal(0, LitPixels(surface));
    }

    [Fact]
    public void ARectangleHangingOffTheEdgeIsClipped()
    {
        var surface = Surface(16, 10);
        surface.FillRectangle(-4, -4, 8, 8, Red);

        Assert.Equal(16, LitPixels(surface));            // the 4x4 corner that lands on the surface
        Assert.Equal(Red, surface.GetPixel(0, 0));
        Assert.Equal(Red, surface.GetPixel(3, 3));
        Assert.True(surface.GetPixel(4, 0).IsTransparent);
    }

    [Fact]
    public void ARectangleEntirelyOffTheSurfaceDrawsNothing()
    {
        var surface = Surface(16, 10);
        surface.FillRectangle(100, 100, 5, 5, Red);
        surface.FillRectangle(-100, 0, 50, 5, Red);      // ends at -50, still off

        Assert.Equal(0, LitPixels(surface));
    }

    // ─────────────────────────────────────────────────────────────
    // Bulk access, for the compositor that comes next
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void ThePixelsCanBeReadInBulkInRowMajorOrder()
    {
        // The compositor will blit whole rows rather than ask pixel by pixel, so the layout it
        // relies on is pinned here rather than assumed there.
        var surface = Surface(4, 3);
        surface.SetPixel(0, 0, Red);
        surface.SetPixel(3, 2, White);

        var pixels = surface.Pixels;

        Assert.Equal(12, pixels.Length);
        Assert.Equal(Red.Value, pixels[0]);
        Assert.Equal(White.Value, pixels[11]);
        Assert.Equal(0u, pixels[1]);
    }

    // ─────────────────────────────────────────────────────────────
    // Circles — the ND terminal defines them, and ReGIS will
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void ACircleTouchesTheFourCardinalPointsAndNotTheCentre()
    {
        var surface = Surface(21, 21);

        surface.DrawCircle(10, 10, 5, White);

        Assert.False(surface.GetPixel(15, 10).IsTransparent);   // east
        Assert.False(surface.GetPixel(5, 10).IsTransparent);    // west
        Assert.False(surface.GetPixel(10, 15).IsTransparent);   // south
        Assert.False(surface.GetPixel(10, 5).IsTransparent);    // north
        Assert.True(surface.GetPixel(10, 10).IsTransparent, "an outline is not a disc");
    }

    [Fact]
    public void ACircleIsTheSameOnBothSides()
    {
        // Symmetry is the property worth pinning: a circle a pixel fatter on one side looks wrong
        // and nothing in the picture explains why. Same reason the line draws from a canonical end.
        var surface = Surface(21, 21);

        surface.DrawCircle(10, 10, 7, White);

        for (int dy = -7; dy <= 7; dy++)
        {
            for (int dx = 0; dx <= 7; dx++)
            {
                bool right = !surface.GetPixel(10 + dx, 10 + dy).IsTransparent;
                bool left = !surface.GetPixel(10 - dx, 10 + dy).IsTransparent;
                Assert.Equal(right, left);

                bool below = !surface.GetPixel(10 + dy, 10 + dx).IsTransparent;
                bool above = !surface.GetPixel(10 + dy, 10 - dx).IsTransparent;
                Assert.Equal(below, above);
            }
        }
    }

    [Fact]
    public void ACircleOffTheEdgeIsClippedRatherThanRefused()
    {
        var surface = Surface(16, 10);

        surface.DrawCircle(0, 0, 6, White);

        Assert.True(LitPixels(surface) > 0, "the part on the surface should still be drawn");
        Assert.False(surface.GetPixel(6, 0).IsTransparent);
    }

    [Fact]
    public void ACircleWithNoRadiusDrawsNothing()
    {
        var surface = Surface();

        surface.DrawCircle(8, 5, 0, White);
        surface.DrawCircle(8, 5, -3, White);

        Assert.Equal(0, LitPixels(surface));
    }
}
