using System;
using RetroTerm.Core.Terminal.Graphics;
using Xunit;

namespace RetroTerm.Tests.Terminal.Graphics;

/// <summary>
/// The ND graphics module — the first consumer of the graphics foundation, and the first place all
/// four blocks are used together.
///
/// The spec (<c>spec\Tektronix\nd-graphic-terminal-analysis.md</c>) documents thirty <c>ESC "</c>
/// modes recovered from a Ghidra analysis of a 1986 ND test program. Some are pinned down exactly:
/// mode 9 clears graphic memory, mode 8 fills a rectangle from four coordinates, mode 17 shows the
/// graphics plane. Others are named without their parameter meanings — "set polygon/shape drawing
/// mode" with an unexplained mode number cannot be implemented without inventing the semantics.
///
/// So the unambiguous ones are implemented and everything else is COUNTED. These tests cover both,
/// because the counting is not a placeholder: pointed at a real host it says which of the thirty
/// modes that program actually uses, which beats working down the table in order.
/// </summary>
public class NorskDataGraphicsModuleTests
{
    private const int TekWidth = 1024;
    private const int TekHeight = 780;

    private static (NorskDataGraphicsModule Module, GraphicsCompositor Compositor) Build()
    {
        var compositor = new GraphicsCompositor(TekWidth, TekHeight);
        var viewport = new GraphicsViewport(TekWidth, TekHeight, TekWidth, TekHeight,
            logicalYIncreasesUpward: true, preserveAspectRatio: false);
        return (new NorskDataGraphicsModule(compositor, viewport), compositor);
    }

    private static void Send(NorskDataGraphicsModule module, char final, params int[] parameters)
        => module.HandleSequence(parameters, final);

    // ─────────────────────────────────────────────────────────────
    // The plane starts out of the way
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void TheGraphicsPlaneStartsHidden()
    {
        // A terminal shows text until a host asks for graphics. A plane that started visible would
        // put an empty layer over every session that never wanted one.
        var (module, compositor) = Build();

        Assert.False(module.GraphicsDisplayed);
        Assert.False(module.CrosshairVisible);
        Assert.False(compositor.HasAnythingToDraw());
    }

    [Fact]
    public void ModeSeventeenShowsTheGraphicsPlane()
    {
        var (module, _) = Build();

        Send(module, 'h', 17);

        Assert.True(module.GraphicsDisplayed);
    }

    [Fact]
    public void ResettingModeSeventeenHidesItAgain()
    {
        var (module, _) = Build();
        Send(module, 'h', 17);

        Send(module, 'l', 17);

        Assert.False(module.GraphicsDisplayed);
    }

    // ─────────────────────────────────────────────────────────────
    // Drawing
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void ModeEightFillsARectangle()
    {
        // ESC "8;x1;y1;x2;y2h. The Y flip goes through the viewport, so a rectangle along the
        // BOTTOM of the terminal's space lands at the bottom of the surface.
        var (module, compositor) = Build();
        Send(module, 'h', 17);

        Send(module, 'h', 8, 100, 0, 200, 50);
        compositor.Composite();

        Assert.False(compositor.Output.GetPixel(150, 779).IsTransparent);
        Assert.False(compositor.Output.GetPixel(150, 760).IsTransparent);
        Assert.True(compositor.Output.GetPixel(150, 700).IsTransparent,
            "the rectangle must not reach above the coordinates it was given");
        Assert.True(compositor.Output.GetPixel(50, 779).IsTransparent,
            "nor left of them");
    }

    [Fact]
    public void ARectangleCanBeGivenEitherDiagonal()
    {
        // A host may name the corners in any order, and the surface draws nothing for a negative
        // width - so the corners are ordered here rather than trusted.
        var (moduleA, compositorA) = Build();
        Send(moduleA, 'h', 17);
        Send(moduleA, 'h', 8, 100, 0, 200, 50);
        compositorA.Composite();

        var (moduleB, compositorB) = Build();
        Send(moduleB, 'h', 17);
        Send(moduleB, 'h', 8, 200, 50, 100, 0);
        compositorB.Composite();

        for (int x = 90; x <= 210; x += 10)
        {
            for (int y = 720; y <= 779; y += 10)
            {
                Assert.True(compositorA.Output.GetPixel(x, y) == compositorB.Output.GetPixel(x, y),
                    $"pixel ({x},{y}) differs depending on which diagonal was given");
            }
        }
    }

    [Fact]
    public void ASingleCoordinatePairFillsOnePixel()
    {
        // Inclusive of both corners: asking for the rectangle from a point to itself means one
        // pixel, not none.
        var (module, compositor) = Build();
        Send(module, 'h', 17);

        Send(module, 'h', 8, 500, 400, 500, 400);
        compositor.Composite();

        Assert.True(compositor.HasAnythingToDraw());
    }

    [Fact]
    public void ModeNineClearsTheDrawing()
    {
        var (module, compositor) = Build();
        Send(module, 'h', 17);
        Send(module, 'h', 8, 100, 0, 200, 50);

        Send(module, 'h', 9);
        compositor.Composite();

        Assert.False(compositor.HasAnythingToDraw());
    }

    [Fact]
    public void HidingThePlaneKeepsTheDrawing()
    {
        // Hiding is not clearing. An ND host hides a drawing and shows it again unchanged, which
        // is exactly why planes carry a visible flag instead of being erased.
        var (module, compositor) = Build();
        Send(module, 'h', 17);
        Send(module, 'h', 8, 100, 0, 200, 50);

        Send(module, 'l', 17);
        compositor.Composite();
        Assert.False(compositor.HasAnythingToDraw());

        Send(module, 'h', 17);
        compositor.Composite();
        Assert.True(compositor.HasAnythingToDraw());
    }

    // ─────────────────────────────────────────────────────────────
    // The crosshair, and GIN
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void ModeSixRaisesTheCrosshairAndArmsGin()
    {
        // Mode 6 is what puts the terminal into GIN: a crosshair appears and the next thing the
        // user does is reported to the host.
        var (module, _) = Build();

        Send(module, 'h', 6);

        Assert.True(module.CrosshairVisible);
        Assert.True(module.Gin.IsArmed);
    }

    [Fact]
    public void LoweringTheCrosshairCancelsThePick()
    {
        // A cancelled pick must not report later.
        var (module, _) = Build();
        Send(module, 'h', 6);

        Send(module, 'l', 6);

        Assert.False(module.CrosshairVisible);
        Assert.False(module.Gin.IsArmed);
    }

    [Fact]
    public void TheCrosshairIsDrawnOnItsOwnPlane()
    {
        // THE reason the plane model exists, and it has to be tested by what DISAPPEARS.
        //
        // A first version of this filled the whole drawing plane and then checked the drawing
        // survived a crosshair move. It passed even with the crosshair deliberately drawn onto the
        // drawing plane - because the crosshair and the fill are the same colour, so scribbling on
        // the wrong plane was invisible. It proved nothing. Red-before-green caught it.
        //
        // What actually discriminates: after the crosshair moves, its OLD position must be empty.
        // On a shared surface the old cross stays there for good.
        var (module, compositor) = Build();
        Send(module, 'h', 17);
        Send(module, 'h', 8, 10, 10, 40, 40);       // a small drawing, well away from both crosshairs
        Send(module, 'h', 6);

        module.MoveCrosshair(500, 400);
        compositor.Composite();
        Assert.False(compositor.Output.GetPixel(500, 400).IsTransparent,
            "the crosshair should be visible where it was put");

        module.MoveCrosshair(600, 300);
        compositor.Composite();

        Assert.True(compositor.Output.GetPixel(500, 250).IsTransparent,
            "the old crosshair's vertical arm must be gone, not left on the drawing");
        Assert.True(compositor.Output.GetPixel(200, 400).IsTransparent,
            "the old crosshair's horizontal arm must be gone too");
        Assert.False(compositor.Output.GetPixel(600, 300).IsTransparent,
            "and the new one should be where it was moved to");

        // And the drawing underneath is untouched by any of it.
        viewportSurvives(compositor);

        static void viewportSurvives(GraphicsCompositor compositor)
        {
            Assert.False(compositor.Output.GetPixel(20, 750).IsTransparent,
                "the drawing must survive the crosshair moving over the screen");
        }
    }

    [Fact]
    public void APickIsReportedInTheTerminalsOwnSpaceAsAnNd()
    {
        var (module, _) = Build();
        Send(module, 'h', 6);
        module.MoveCrosshair(512, 389);

        module.Gin.NorskDataModelByte1 = 0x41;
        module.Gin.NorskDataModelByte2 = 0x31;

        Span<byte> report = stackalloc byte[module.Gin.MaximumReportLength];
        Assert.True(module.Gin.TryBuildReport(0x41, report, out int length));

        // Seven bytes, not five: this is an ND, and that is how a host tells.
        Assert.Equal(7, length);
        Assert.True(TektronixGinEncoder.TryDecode(report, out int x, out int y));
        Assert.Equal(512, x);
        Assert.Equal(390, y);                       // 779 - 389, the Y flip
    }

    // ─────────────────────────────────────────────────────────────
    // What is NOT implemented, and how we will find out what matters
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void AnUnimplementedModeIsCountedRatherThanGuessedAt()
    {
        // Mode 20 is "set circle drawing mode" with an unexplained mode number. Inventing the
        // semantics would be worse than not having them: a wrong implementation looks like a
        // working one until something draws an ellipse where a circle belongs.
        var (module, _) = Build();

        Send(module, 'h', 20, 1);
        Send(module, 'h', 20, 1);
        Send(module, 'h', 21, 3);

        Assert.Equal(2, module.UnhandledSequences["20:h"]);
        Assert.Equal(1, module.UnhandledSequences["21:h"]);
    }

    [Fact]
    public void QueriesAreCountedToo()
    {
        // 'd', 'n', 'a', 'C' and '.' each need a reply or a state model the spec does not pin
        // down. ESC "5d is the detection query, so this one will be the first to be implemented -
        // and the counter is how we will know a host is asking for it.
        var (module, _) = Build();

        Send(module, 'd', 5);

        Assert.Equal(1, module.UnhandledSequences["5:d"]);
    }

    // ─────────────────────────────────────────────────────────────
    // Circles, and the cursor switch — two more modes the spec pins down
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void ModeTwentyFourDrawsACircleAtTheGivenCentre()
    {
        // ESC "24;cx;cy;rh. The spec gives all three numbers a meaning, which is why this one is
        // implemented while its neighbours - "set circle drawing mode" with an unexplained mode
        // number - are counted.
        var (module, compositor) = Build();
        Send(module, 'h', 17);

        Send(module, 'h', 24, 512, 390, 100);

        compositor.Composite();
        var surface = compositor.Output;

        // Logical Y runs UP and surface Y runs DOWN, so the centre row is the middle either way.
        int centreRow = TekHeight - 1 - 390;
        Assert.False(surface.GetPixel(512 + 100, centreRow).IsTransparent);
        Assert.False(surface.GetPixel(512 - 100, centreRow).IsTransparent);
        Assert.True(surface.GetPixel(512, centreRow).IsTransparent, "an outline is not a disc");
    }

    [Fact]
    public void ACircleWithNoRadiusDrawsNothing()
    {
        var (module, compositor) = Build();
        Send(module, 'h', 17);

        Send(module, 'h', 24, 512, 390, 0);

        Assert.False(compositor.HasAnythingToDraw());
    }

    [Fact]
    public void ModeTenTurnsTheCursorOffAndOnAgain()
    {
        // ESC "10;0h hides, ESC "10;1h shows. One of the few modes the spec gives BOTH values for.
        var (module, _) = Build();
        Send(module, 'h', 6);          // crosshair up
        Assert.True(module.CrosshairVisible);

        Send(module, 'h', 10, 0);
        Assert.False(module.CrosshairVisible);

        Send(module, 'h', 10, 1);
        Assert.True(module.CrosshairVisible);
    }

    [Fact]
    public void ExecuteDrawIsStillCountedBecauseNothingIsPending()
    {
        // Mode 30 renders a pending polygon, and polygons (mode 27) are still counted rather than
        // collected. An empty implementation would look finished and swallow the evidence, so it
        // stays on the counter until there is a shape for it to draw.
        var (module, _) = Build();

        Send(module, 'h', 30);

        Assert.Equal(1, module.UnhandledSequences["30:h"]);
    }

    [Fact]
    public void AnImplementedModeIsNotCounted()
    {
        // The guard that keeps the counter meaningful: if handled modes leaked into it, the
        // instrument would report noise instead of gaps.
        var (module, _) = Build();

        Send(module, 'h', 17);
        Send(module, 'h', 9);
        Send(module, 'h', 6);
        Send(module, 'l', 17);
        Send(module, 'h', 10, 1);
        Send(module, 'h', 24, 512, 390, 50);

        Assert.Empty(module.UnhandledSequences);
    }
}
