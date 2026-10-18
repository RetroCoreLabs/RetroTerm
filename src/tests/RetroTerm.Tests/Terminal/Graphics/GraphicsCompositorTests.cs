using System;
using RetroTerm.Core.Terminal.Graphics;
using Xunit;

namespace RetroTerm.Tests.Terminal.Graphics;

/// <summary>
/// The plane model and compositor — the third block of the graphics foundation.
///
/// Planes exist because a terminal's graphics are not one picture. An ND terminal can hold a
/// drawing and hide it, draw into a second graphics memory while the first is displayed, and put a
/// crosshair over the top. That last one is the trap a single shared surface walks straight into: a
/// GIN cursor scribbled onto the drawing plane is still sitting there after the crosshair moves,
/// and it comes back when the host reads the plane.
///
/// What the compositor deliberately does NOT own is the text. Core has no glyph rasteriser and is
/// not getting one, so this flattens the graphics planes and the renderer blits the result over the
/// text it drew. The text showing through is what the transparent pixels are for.
/// </summary>
public class GraphicsCompositorTests
{
    private static readonly GraphicsColor Red = new GraphicsColor(255, 0, 0);
    private static readonly GraphicsColor Blue = new GraphicsColor(0, 0, 255);
    private static readonly GraphicsColor HalfWhite = new GraphicsColor(255, 255, 255, 128);

    private static GraphicsCompositor Compositor(int width = 8, int height = 4)
        => new GraphicsCompositor(width, height);

    // ─────────────────────────────────────────────────────────────
    // Planes
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void AFreshCompositorHasNoPlanesAndAnEmptyPicture()
    {
        var compositor = Compositor();
        compositor.Composite();

        Assert.Equal(0, compositor.PlaneCount);
        Assert.False(compositor.HasAnythingToDraw());
        Assert.True(compositor.Output.GetPixel(0, 0).IsTransparent);
    }

    [Fact]
    public void APlaneCanBeFoundByName()
    {
        // By name rather than index, so a module can reach its own plane without holding a number
        // that another module adding a plane could shift.
        var compositor = Compositor();
        var drawing = compositor.AddPlane("nd-graphics");
        var crosshair = compositor.AddPlane("gin");

        Assert.Same(drawing, compositor.FindPlane("nd-graphics"));
        Assert.Same(crosshair, compositor.FindPlane("gin"));
        Assert.Null(compositor.FindPlane("sixel"));
    }

    [Fact]
    public void TwoPlanesCannotShareAName()
    {
        var compositor = Compositor();
        compositor.AddPlane("nd-graphics");

        Assert.Throws<ArgumentException>(() => compositor.AddPlane("nd-graphics"));
    }

    [Fact]
    public void APlaneIsTheSameSizeAsTheCompositor()
    {
        var compositor = Compositor(64, 32);
        var plane = compositor.AddPlane("nd-graphics");

        Assert.Equal(64, plane.Width);
        Assert.Equal(32, plane.Height);
    }

    // ─────────────────────────────────────────────────────────────
    // Stacking order
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void OnePlaneShowsThrough()
    {
        var compositor = Compositor();
        compositor.AddPlane("nd-graphics").Surface.SetPixel(2, 1, Red);

        compositor.Composite();

        Assert.Equal(Red, compositor.Output.GetPixel(2, 1));
        Assert.True(compositor.Output.GetPixel(3, 1).IsTransparent);
    }

    [Fact]
    public void AnUpperPlaneCoversALowerOne()
    {
        // Added bottom first. The crosshair goes on last precisely so it wins.
        var compositor = Compositor();
        compositor.AddPlane("drawing").Surface.SetPixel(2, 1, Red);
        compositor.AddPlane("gin").Surface.SetPixel(2, 1, Blue);

        compositor.Composite();

        Assert.Equal(Blue, compositor.Output.GetPixel(2, 1));
    }

    [Fact]
    public void AGapInAnUpperPlaneLetsTheLowerOneThrough()
    {
        // The whole reason a new plane starts transparent rather than black. A crosshair plane is
        // one thin cross on an otherwise empty layer; if empty meant black it would black out the
        // drawing underneath.
        var compositor = Compositor();
        compositor.AddPlane("drawing").Surface.SetPixel(2, 1, Red);
        compositor.AddPlane("gin").Surface.SetPixel(5, 3, Blue);

        compositor.Composite();

        Assert.Equal(Red, compositor.Output.GetPixel(2, 1));
        Assert.Equal(Blue, compositor.Output.GetPixel(5, 3));
    }

    [Fact]
    public void PlanesStackInTheOrderTheyWereAdded()
    {
        var compositor = Compositor();
        var bottom = compositor.AddPlane("bottom");
        var top = compositor.AddPlane("top");

        Assert.Same(bottom, compositor.PlaneAt(0));
        Assert.Same(top, compositor.PlaneAt(1));
    }

    // ─────────────────────────────────────────────────────────────
    // Hiding
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void AHiddenPlaneIsNotDrawn()
    {
        var compositor = Compositor();
        var drawing = compositor.AddPlane("drawing");
        drawing.Surface.SetPixel(2, 1, Red);

        drawing.IsVisible = false;
        compositor.Composite();

        Assert.True(compositor.Output.GetPixel(2, 1).IsTransparent);
        Assert.False(compositor.HasAnythingToDraw());
    }

    [Fact]
    public void HidingAPlaneKeepsItsPixels()
    {
        // Hiding is not erasing. An ND host can hide a drawing and show it again unchanged, and a
        // compositor that cleared on hide would lose it.
        var compositor = Compositor();
        var drawing = compositor.AddPlane("drawing");
        drawing.Surface.SetPixel(2, 1, Red);

        drawing.IsVisible = false;
        compositor.Composite();
        drawing.IsVisible = true;
        compositor.Composite();

        Assert.Equal(Red, compositor.Output.GetPixel(2, 1));
    }

    [Fact]
    public void ClearingAPlaneRemovesOnlyThatPlane()
    {
        // The crosshair case: erase the GIN plane between moves and the drawing under it survives.
        var compositor = Compositor();
        compositor.AddPlane("drawing").Surface.SetPixel(2, 1, Red);
        var gin = compositor.AddPlane("gin");
        gin.Surface.SetPixel(2, 1, Blue);

        gin.Clear();
        compositor.Composite();

        Assert.Equal(Red, compositor.Output.GetPixel(2, 1));
    }

    // ─────────────────────────────────────────────────────────────
    // Blending
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void AHalfTransparentPixelBlendsWithWhatIsBelow()
    {
        // Most terminal graphics are fully on or fully off, but Sixel carries real colours. Getting
        // the general case right here costs a few lines; getting it wrong costs a special case in
        // every protocol that ever needs it.
        var compositor = Compositor();
        compositor.AddPlane("drawing").Surface.SetPixel(1, 1, Red);
        compositor.AddPlane("overlay").Surface.SetPixel(1, 1, HalfWhite);

        compositor.Composite();

        var blended = compositor.Output.GetPixel(1, 1);

        // Half white over opaque red: red stays at the top, green and blue come up to about half.
        Assert.True(blended.IsOpaque, $"blending over an opaque pixel stays opaque; got {blended}");
        Assert.InRange(blended.R, 250, 255);
        Assert.InRange(blended.G, 120, 136);
        Assert.InRange(blended.B, 120, 136);
    }

    [Fact]
    public void AHalfTransparentPixelOverNothingKeepsItsOwnAlpha()
    {
        // It must NOT become opaque just because there was nothing underneath - the text below
        // still has to show through it when the renderer blits.
        var compositor = Compositor();
        compositor.AddPlane("overlay").Surface.SetPixel(1, 1, HalfWhite);

        compositor.Composite();

        var result = compositor.Output.GetPixel(1, 1);

        Assert.InRange(result.A, 126, 130);
        Assert.False(result.IsOpaque, "a half-transparent pixel over nothing must stay half transparent");
    }

    [Fact]
    public void AnOpaquePixelReplacesWhatIsBelowExactly()
    {
        // The common case has to be exact, not merely close: rounding an opaque colour through the
        // blend would shift every drawn pixel by a value or two and nothing would look wrong
        // enough to investigate.
        var compositor = Compositor();
        compositor.AddPlane("drawing").Surface.SetPixel(1, 1, Blue);
        compositor.AddPlane("overlay").Surface.SetPixel(1, 1, Red);

        compositor.Composite();

        Assert.Equal(Red, compositor.Output.GetPixel(1, 1));
    }

    // ─────────────────────────────────────────────────────────────
    // Recomposing
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void CompositingAgainDoesNotAccumulate()
    {
        // The output is rebuilt, not added to. Without the clear at the top of Composite, a
        // half-transparent overlay would get more solid every frame - the sort of drift that looks
        // like a rendering bug on a slow machine only.
        var compositor = Compositor();
        compositor.AddPlane("overlay").Surface.SetPixel(1, 1, HalfWhite);

        compositor.Composite();
        var once = compositor.Output.GetPixel(1, 1);

        compositor.Composite();
        compositor.Composite();
        var thrice = compositor.Output.GetPixel(1, 1);

        Assert.Equal(once, thrice);
    }

    [Fact]
    public void ErasingEverythingLeavesAnEmptyPicture()
    {
        var compositor = Compositor();
        var drawing = compositor.AddPlane("drawing");
        drawing.Surface.SetPixel(2, 1, Red);
        compositor.Composite();

        drawing.Clear();
        compositor.Composite();

        Assert.False(compositor.HasAnythingToDraw());
        Assert.True(compositor.Output.GetPixel(2, 1).IsTransparent);
    }

    [Fact]
    public void HasAnythingToDrawIgnoresHiddenPlanes()
    {
        // The renderer uses this to skip the blit entirely, so a hidden plane counting as content
        // would cost a full-screen composite and blit every frame for nothing.
        var compositor = Compositor();
        var drawing = compositor.AddPlane("drawing");
        drawing.Surface.SetPixel(2, 1, Red);

        Assert.True(compositor.HasAnythingToDraw());

        drawing.IsVisible = false;
        Assert.False(compositor.HasAnythingToDraw());
    }

    // ─────────────────────────────────────────────────────────────
    // With the rest of the foundation
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void AVectorDrawnInLogicalSpaceEndsUpInTheComposite()
    {
        // All three blocks at once, which is the shape every graphics module will use: the protocol
        // speaks its own bottom-left space, the viewport converts, the plane's surface draws and
        // clips, the compositor flattens.
        var compositor = new GraphicsCompositor(1024, 780);
        var viewport = new GraphicsViewport(1024, 780, 1024, 780,
            logicalYIncreasesUpward: true, preserveAspectRatio: false);

        var drawing = compositor.AddPlane("nd-graphics");
        viewport.ToSurface(0, 0, out int x0, out int y0);          // Tek origin: bottom left
        viewport.ToSurface(1023, 0, out int x1, out int y1);
        drawing.Surface.DrawLine(x0, y0, x1, y1, Red);

        var gin = compositor.AddPlane("gin");
        viewport.ToSurface(512, 390, out int cx, out int cy);      // crosshair in the middle
        gin.Surface.DrawLine(cx - 10, cy, cx + 10, cy, Blue);

        compositor.Composite();

        Assert.Equal(Red, compositor.Output.GetPixel(500, 779));
        Assert.Equal(Blue, compositor.Output.GetPixel(cx, cy));

        // And the crosshair is not part of the drawing.
        gin.Clear();
        compositor.Composite();
        Assert.True(compositor.Output.GetPixel(cx, cy).IsTransparent);
        Assert.Equal(Red, compositor.Output.GetPixel(500, 779));
    }
}
