using System;
using RetroTerm.Core.Terminal.Graphics;
using Xunit;

namespace RetroTerm.Tests.Terminal.Graphics;

/// <summary>
/// The scanline polygon fill - the surface half of ReGIS polygon fill.
/// </summary>
/// <remarks>
/// <para><b>Even-odd, and why the shapes here are the shapes they are</b></para>
/// A pixel is inside when a ray from it crosses the outline an odd number of times. The square and
/// triangle pin the plain cases; the frame pins that a hole comes out hollow, which is the whole
/// point of the rule and the one thing a naive "fill the bounding box" would get wrong.
///
/// <para><b>What is deliberately NOT pinned</b></para>
/// Exact edge pixels. Chapter 11 of the VT330/VT340 Graphics Programming manual says of real
/// hardware: "In some cases, the outline of the filled polygon may not line up exactly with the
/// vectors that connect the same vertices." There is no exact answer to match, so these tests
/// assert the interior, the exterior and the area - never a single boundary pixel.
/// </remarks>
public class FillPolygonTests
{
    private static InMemoryGraphicsSurface Surface(int width = 64, int height = 64)
        => new InMemoryGraphicsSurface(width, height);

    private static readonly GraphicsColor Ink = new GraphicsColor(255, 255, 255);

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
    public void ASquareFillsItsInsideAndNothingOutside()
    {
        var surface = Surface();
        int[] square = { 10, 10, 30, 10, 30, 30, 10, 30 };

        surface.FillPolygon(new ReadOnlySpan<int>(square), Ink);

        Assert.False(surface.GetPixel(20, 20).IsTransparent);   // middle
        Assert.False(surface.GetPixel(11, 11).IsTransparent);   // just inside a corner
        Assert.True(surface.GetPixel(9, 20).IsTransparent);     // just outside the left edge
        Assert.True(surface.GetPixel(20, 40).IsTransparent);    // well below
    }

    [Fact]
    public void ASquaresAreaIsAboutWidthTimesHeight()
    {
        // Not exact: the half-open scanline rule includes one edge of each pair and not the other,
        // which is what stops adjacent polygons overlapping. So the area is checked to within a
        // border's worth rather than to the pixel.
        var surface = Surface();
        int[] square = { 10, 10, 30, 10, 30, 30, 10, 30 };

        surface.FillPolygon(new ReadOnlySpan<int>(square), Ink);

        int area = LitPixels(surface);
        Assert.True(area >= 380 && area <= 441, $"a 20x20 square filled {area} pixels");
    }

    [Fact]
    public void ATriangleFillsAWedgeAndLeavesTheCornersEmpty()
    {
        var surface = Surface();
        int[] triangle = { 32, 8, 56, 56, 8, 56 };

        surface.FillPolygon(new ReadOnlySpan<int>(triangle), Ink);

        Assert.False(surface.GetPixel(32, 50).IsTransparent);   // low centre, well inside
        Assert.True(surface.GetPixel(10, 12).IsTransparent);    // top-left, outside the slope
        Assert.True(surface.GetPixel(54, 12).IsTransparent);    // top-right, outside the slope
    }

    [Fact]
    public void AFrameLeavesItsHoleHollow()
    {
        // The even-odd rule in one picture: an outer ring and an inner ring in the same polygon,
        // joined by a seam, so the inside of the inner ring is crossed twice and stays empty.
        var surface = Surface();
        int[] frame =
        {
            10, 10, 50, 10, 50, 50, 10, 50, 10, 10,   // outer, closed back to the start
            20, 20, 20, 40, 40, 40, 40, 20, 20, 20,   // inner, wound the other way
        };

        surface.FillPolygon(new ReadOnlySpan<int>(frame), Ink);

        Assert.False(surface.GetPixel(15, 30).IsTransparent, "the border should be filled");
        Assert.True(surface.GetPixel(30, 30).IsTransparent, "the hole should be hollow");
    }

    [Fact]
    public void FewerThanThreeVerticesDrawsNothing()
    {
        var surface = Surface();
        int[] line = { 10, 10, 30, 30 };

        surface.FillPolygon(new ReadOnlySpan<int>(line), Ink);

        Assert.Equal(0, LitPixels(surface));
    }

    [Fact]
    public void AnOddTrailingValueIsIgnoredRatherThanRead()
    {
        // Three vertices and a stray x with no y. The stray must not be read as half a vertex.
        var surface = Surface();
        int[] points = { 10, 10, 30, 10, 20, 30, 99 };

        surface.FillPolygon(new ReadOnlySpan<int>(points), Ink);

        Assert.True(LitPixels(surface) > 0);
        Assert.True(surface.GetPixel(60, 60).IsTransparent, "the stray value leaked into the shape");
    }

    [Fact]
    public void AShapeHangingOffTheEdgeIsClippedRatherThanThrowing()
    {
        // Everything on this surface clips. A host may send a shape half off the screen.
        var surface = Surface();
        int[] square = { -20, -20, 20, -20, 20, 20, -20, 20 };

        surface.FillPolygon(new ReadOnlySpan<int>(square), Ink);

        Assert.False(surface.GetPixel(0, 0).IsTransparent);
        Assert.True(surface.GetPixel(40, 40).IsTransparent);
    }

    [Fact]
    public void AShapeEntirelyOffTheSurfaceDrawsNothing()
    {
        var surface = Surface();
        int[] square = { 100, 100, 140, 100, 140, 140, 100, 140 };

        surface.FillPolygon(new ReadOnlySpan<int>(square), Ink);

        Assert.Equal(0, LitPixels(surface));
    }

    [Fact]
    public void ClosingTheFigureExplicitlyChangesNothing()
    {
        // The last vertex joins the first whether or not the caller repeated it.
        var open = Surface();
        var closed = Surface();

        int[] withoutRepeat = { 10, 10, 30, 10, 30, 30, 10, 30 };
        int[] withRepeat = { 10, 10, 30, 10, 30, 30, 10, 30, 10, 10 };

        open.FillPolygon(new ReadOnlySpan<int>(withoutRepeat), Ink);
        closed.FillPolygon(new ReadOnlySpan<int>(withRepeat), Ink);

        for (int y = 0; y < open.Height; y++)
        {
            for (int x = 0; x < open.Width; x++)
            {
                Assert.Equal(open.GetPixel(x, y), closed.GetPixel(x, y));
            }
        }
    }
}
