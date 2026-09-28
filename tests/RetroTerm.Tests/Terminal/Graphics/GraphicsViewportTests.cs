using System;
using RetroTerm.Core.Terminal.Graphics;
using Xunit;

namespace RetroTerm.Tests.Terminal.Graphics;

/// <summary>
/// The coordinate transform — the second block of the graphics foundation, and the one most likely
/// to be quietly wrong.
///
/// ND graphics and Tektronix 4010/4014 both put the ORIGIN AT THE BOTTOM LEFT with Y increasing
/// UPWARD, over 1024 x 780 addressable points. That is the opposite of every surface, window and
/// bitmap. The numbers here are taken from <c>spec\Tektronix\nd-graphic-terminal-analysis.md</c>
/// rather than assumed — the architecture review says "0..4095-style" loosely, and the real ND
/// crosshair space is 1024 x 780.
///
/// The reason this has one owner: two copies of a Y flip in two modules is how a crosshair ends up
/// half a screen from the line it is pointing at, with each module looking correct on its own.
/// </summary>
public class GraphicsViewportTests
{
    // The real ND / Tek 4014 addressable space.
    private const int TekWidth = 1024;
    private const int TekHeight = 780;

    private static GraphicsViewport Tek(int surfaceWidth, int surfaceHeight, bool preserveAspect = true)
        => new GraphicsViewport(TekWidth, TekHeight, surfaceWidth, surfaceHeight,
            logicalYIncreasesUpward: true, preserveAspectRatio: preserveAspect);

    // ─────────────────────────────────────────────────────────────
    // The Y flip
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void LogicalOriginIsTheBOTTOMLeftOfTheSurface()
    {
        // THE test. Tek 0,0 is bottom left; surface 0,0 is top left. Getting this backwards draws
        // every picture upside down, and a surprising amount of vector art looks plausible that
        // way until you find a piece with text in it.
        var viewport = Tek(1024, 780, preserveAspect: false);

        viewport.ToSurface(0, 0, out int x, out int y);

        Assert.Equal(0, x);
        Assert.Equal(779, y);          // bottom row of the surface
    }

    [Fact]
    public void LogicalTopLeftIsTheSurfaceTopLeft()
    {
        var viewport = Tek(1024, 780, preserveAspect: false);

        viewport.ToSurface(0, TekHeight - 1, out int x, out int y);

        Assert.Equal(0, x);
        Assert.Equal(0, y);
    }

    [Fact]
    public void AllFourCornersLandOnTheirSurfaceCorners()
    {
        var viewport = Tek(1024, 780, preserveAspect: false);

        viewport.ToSurface(0, 0, out int blX, out int blY);
        viewport.ToSurface(TekWidth - 1, 0, out int brX, out int brY);
        viewport.ToSurface(0, TekHeight - 1, out int tlX, out int tlY);
        viewport.ToSurface(TekWidth - 1, TekHeight - 1, out int trX, out int trY);

        Assert.Equal((0, 779), (blX, blY));
        Assert.Equal((1023, 779), (brX, brY));
        Assert.Equal((0, 0), (tlX, tlY));
        Assert.Equal((1023, 0), (trX, trY));
    }

    [Fact]
    public void AYUpFlagOfFalseLeavesTheAxisAlone()
    {
        // Sixel and ReGIS place rasters in a top-left space. The flip has to be optional or those
        // protocols would each need their own compensating flip, which is the duplication this
        // class exists to prevent.
        var viewport = new GraphicsViewport(100, 100, 100, 100,
            logicalYIncreasesUpward: false, preserveAspectRatio: false);

        viewport.ToSurface(0, 0, out int x, out int y);

        Assert.Equal(0, x);
        Assert.Equal(0, y);
    }

    // ─────────────────────────────────────────────────────────────
    // Scaling
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void TheFarEdgeReachesTheFarEdge()
    {
        // The off-by-one that leaves a sliver of unpainted screen down the right and bottom. A
        // logical space of 1024 columns has its outermost points at 0 and 1023, so the distance
        // across is 1023 units - dividing by 1024 puts the last column one pixel short.
        var viewport = Tek(2048, 1560, preserveAspect: false);

        viewport.ToSurface(TekWidth - 1, 0, out int x, out int y);

        Assert.Equal(2047, x);
        Assert.Equal(1559, y);
    }

    [Fact]
    public void AMagnifiedSpaceSpreadsPointsOut()
    {
        var viewport = Tek(2048, 1560, preserveAspect: false);

        viewport.ToSurface(512, 0, out int x, out _);

        // 512 of 1023 units across 2047 pixels.
        Assert.InRange(x, 1023, 1025);
    }

    [Fact]
    public void ASingleLogicalColumnDoesNotDivideByZero()
    {
        // The degenerate space. Nothing should send it, and it must not crash the terminal if
        // something does.
        var viewport = new GraphicsViewport(1, 1, 100, 100);

        viewport.ToSurface(0, 0, out int x, out int y);

        Assert.InRange(x, 0, 99);
        Assert.InRange(y, 0, 99);
    }

    [Fact]
    public void ASpaceMustHaveRealDimensions()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new GraphicsViewport(0, 10, 10, 10));
        Assert.Throws<ArgumentOutOfRangeException>(() => new GraphicsViewport(10, 10, 10, 0));
    }

    // ─────────────────────────────────────────────────────────────
    // Aspect ratio and letterboxing
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void PreservingAspectLetterboxesAWideSurface()
    {
        // A Tek screen is roughly 4:3. Stretched across a wide window, circles become ellipses -
        // so the drawing keeps its shape and the spare width becomes margin.
        var viewport = Tek(2000, 780);

        Assert.Equal(viewport.ScaleX, viewport.ScaleY, 6);
        Assert.True(viewport.OffsetX > 0, "a wide surface should letterbox left and right");
        Assert.Equal(0.0, viewport.OffsetY, 6);
    }

    [Fact]
    public void PreservingAspectLetterboxesATallSurface()
    {
        var viewport = Tek(1024, 1600);

        Assert.Equal(viewport.ScaleX, viewport.ScaleY, 6);
        Assert.True(viewport.OffsetY > 0, "a tall surface should letterbox top and bottom");
        Assert.Equal(0.0, viewport.OffsetX, 6);
    }

    [Fact]
    public void ALetterboxedDrawingIsCentred()
    {
        var viewport = Tek(2000, 780);

        viewport.ToSurface(0, 0, out int leftX, out _);
        viewport.ToSurface(TekWidth - 1, 0, out int rightX, out _);

        int leftMargin = leftX;
        int rightMargin = 1999 - rightX;

        Assert.InRange(Math.Abs(leftMargin - rightMargin), 0, 1);
        Assert.True(leftMargin > 0, "there should actually be a margin to centre");
    }

    [Fact]
    public void NotPreservingAspectFillsTheSurface()
    {
        var viewport = Tek(2000, 780, preserveAspect: false);

        Assert.Equal(0.0, viewport.OffsetX, 6);
        Assert.Equal(0.0, viewport.OffsetY, 6);
        Assert.NotEqual(viewport.ScaleX, viewport.ScaleY, 6);
    }

    // ─────────────────────────────────────────────────────────────
    // Off-space points are mapped, not refused
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void APointOutsideTheLogicalSpaceStillMaps()
    {
        // A protocol is entitled to draw off the edge, and the SURFACE clips. Refusing here would
        // push the special case into every caller and lose the part of a line that does land.
        var viewport = Tek(1024, 780, preserveAspect: false);

        viewport.ToSurface(-100, 0, out int x, out _);

        Assert.True(x < 0, "an off-space point should map off-surface, not be clamped");
    }

    [Fact]
    public void ContainsLogicalAnswersWhatIsAddressable()
    {
        var viewport = Tek(1024, 780);

        Assert.True(viewport.ContainsLogical(0, 0));
        Assert.True(viewport.ContainsLogical(1023, 779));
        Assert.False(viewport.ContainsLogical(1024, 0));
        Assert.False(viewport.ContainsLogical(0, 780));
        Assert.False(viewport.ContainsLogical(-1, 0));
    }

    // ─────────────────────────────────────────────────────────────
    // Back the other way — what GIN needs
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void APointerAtTheBottomLeftReportsTheLogicalOrigin()
    {
        // The GIN direction. A crosshair at the bottom-left of the screen is 0,0 to the host.
        var viewport = Tek(1024, 780, preserveAspect: false);

        viewport.ToLogical(0, 779, out int lx, out int ly);

        Assert.Equal(0, lx);
        Assert.Equal(0, ly);
    }

    [Fact]
    public void APointerAtTheTopRightReportsTheFarCorner()
    {
        var viewport = Tek(1024, 780, preserveAspect: false);

        viewport.ToLogical(1023, 0, out int lx, out int ly);

        Assert.Equal(1023, lx);
        Assert.Equal(779, ly);
    }

    [Fact]
    public void APointSurvivesTheRoundTripWhenMagnified()
    {
        // Only when magnified. Shrinking the space puts several logical points on one pixel, and
        // no transform can undo that - so this asserts the case where the round trip IS exact
        // rather than pretending it always is.
        var viewport = Tek(2048, 1560, preserveAspect: false);

        for (int logicalX = 0; logicalX < TekWidth; logicalX += 101)
        {
            for (int logicalY = 0; logicalY < TekHeight; logicalY += 77)
            {
                viewport.ToSurface(logicalX, logicalY, out int sx, out int sy);
                viewport.ToLogical(sx, sy, out int backX, out int backY);

                Assert.Equal(logicalX, backX);
                Assert.Equal(logicalY, backY);
            }
        }
    }

    [Fact]
    public void TheRoundTripSurvivesLetterboxingToo()
    {
        // The margin must be subtracted on the way back, or every reported crosshair is shifted by
        // the width of the letterbox - which looks like a correct transform with a mysterious
        // constant error.
        var viewport = Tek(2400, 1560);

        viewport.ToSurface(500, 400, out int sx, out int sy);
        viewport.ToLogical(sx, sy, out int backX, out int backY);

        Assert.Equal(500, backX);
        Assert.Equal(400, backY);
    }

    [Fact]
    public void APointerInTheLetterboxIsClampedToTheSpace()
    {
        // A pointer can sit in the margin. A crosshair report of "-3" is not something a host can
        // do anything sensible with, so it is clamped to the nearest addressable point.
        var viewport = Tek(2000, 780);

        viewport.ToLogical(0, 0, out int lx, out int ly);

        Assert.InRange(lx, 0, TekWidth - 1);
        Assert.InRange(ly, 0, TekHeight - 1);
    }

    // ─────────────────────────────────────────────────────────────
    // Drawing through it, end to end
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void ALineDrawnInLogicalSpaceLandsOnTheSurface()
    {
        // The two blocks together: a protocol speaks its own space, the viewport converts, the
        // surface draws and clips. This is the shape every graphics module will use.
        var surface = new InMemoryGraphicsSurface(1024, 780);
        var viewport = Tek(1024, 780, preserveAspect: false);
        var white = new GraphicsColor(255, 255, 255);

        // A horizontal line along the BOTTOM of the logical space.
        viewport.ToSurface(100, 0, out int x0, out int y0);
        viewport.ToSurface(900, 0, out int x1, out int y1);
        surface.DrawLine(x0, y0, x1, y1, white);

        Assert.Equal(white, surface.GetPixel(100, 779));
        Assert.Equal(white, surface.GetPixel(900, 779));
        Assert.True(surface.GetPixel(500, 0).IsTransparent,
            "a line along the bottom of Tek space must not appear at the top of the surface");
    }
}
