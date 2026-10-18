using System.Text;
using Avalonia.Headless.XUnit;
using RetroTerm.Core.Configuration;
using RetroTerm.Core.Terminal.Emulators;
using Xunit;

namespace RetroTerm.Tests.Avalonia;

/// <summary>
/// What the ReGIS graphics input crosshair actually looks like, rendered through the real renderer.
/// </summary>
/// <remarks>
/// <para><b>Why a pixel test as well as the plane tests</b></para>
/// <c>RegisGraphicsInputEmulatorTests</c> checks the crosshair's own plane, which proves the decoder
/// and the plane agree. It cannot prove the crosshair reaches the SCREEN - a plane that is never
/// composited, or is drawn under the picture instead of over it, passes every one of those tests and
/// shows nothing.
///
/// The PNG each of these writes to <c>Avalonia\images\rendered\</c> is the point. Three defects in
/// this repository were found by opening one when nothing had failed.
/// </remarks>
[Collection("Avalonia")]
public class RegisGraphicsInputPixelTests
{
    /// <summary>
    /// The escape character, built from its code so no raw control byte lands in this file.
    /// </summary>
    private const char Escape = (char)0x1B;

    /// <summary>
    /// Builds a VT340 and runs some ReGIS on it.
    /// </summary>
    /// <param name="commands">
    /// The ReGIS to run.
    /// </param>
    /// <returns>
    /// The terminal, ready to be rendered.
    /// </returns>
    private static TerminalEmulatorBase Draw(string commands)
    {
        var emulator = EmulatorFactory.CreateEmulator("VT340", 80, 24, 100);
        emulator.ProcessData(Encoding.ASCII.GetBytes(Escape + "P1p" + commands + Escape + "\\"));
        return emulator;
    }

    [AvaloniaFact]
    public void TheCrosshairIsDrawnOverTheHostsPicture()
    {
        // A box with the crosshair standing in the middle of it. If the cursor plane were composited
        // UNDER the drawing, the two crosshair lines would break where they cross the box - which is
        // the sort of thing only looking at the picture catches.
        var emulator = Draw("W(I2)P[200,120]V[600,120]V[600,360]V[200,360]V[200,120]"
            + "P[400,240]R(I0)");

        var shot = RenderedScreenshot.Capture(emulator, "regis-graphics-input-crosshair");

        // The horizontal arm, far outside the box on both sides.
        Assert.True(InkNear(shot, 40, 240));
        Assert.True(InkNear(shot, 760, 240));

        // And the vertical arm, above and below it.
        Assert.True(InkNear(shot, 400, 20));
        Assert.True(InkNear(shot, 400, 460));
    }

    [AvaloniaFact]
    public void TheCrosshairSurvivesAScreenThatIsNotEightyByTwentyFour()
    {
        // The live session that showed NO crosshair was 103x29, because the tab follows the window.
        // Every other test here builds 80x24. The graphics plane is a fixed 800x480 whatever the
        // text grid is, so a crosshair that depends on the grid would pass at one size and vanish at
        // another - and only ever be seen by somebody with a big window.
        var emulator = EmulatorFactory.CreateEmulator("VT340", 103, 29, 100);
        emulator.ProcessData(Encoding.ASCII.GetBytes(Escape + "P1p"
            + "W(I2)P[200,120]V[600,120]V[600,360]V[200,360]V[200,120]"
            + "P[400,240]R(I0)" + Escape + "\\"));

        var shot = RenderedScreenshot.Capture(emulator, "regis-graphics-input-crosshair-103x29");

        Assert.True(InkNear(shot, 300, 120), "the box is missing, so nothing drew at all");

        Assert.True(InkNear(shot, 40, 240), "the crosshair's left arm is missing at 103x29");
        Assert.True(InkNear(shot, 760, 240), "the crosshair's right arm is missing at 103x29");
        Assert.True(InkNear(shot, 400, 20), "the crosshair's top arm is missing at 103x29");
        Assert.True(InkNear(shot, 400, 460), "the crosshair's bottom arm is missing at 103x29");
    }

    [AvaloniaFact]
    public void TheTestServersOwnRoundOneStreamShowsACrosshair()
    {
        // WRITTEN FROM A LIVE FAILURE, 27 August 2026. Driving the real app through the test
        // server's M6.5 round 1 and photographing it with the new SCREENSHOT command showed the box,
        // the diagonals and the circle - and NO crosshair. The test above passes, so the difference
        // between the two streams is the whole of the question.
        //
        // This is the test server's round-1 string, byte for byte, from
        // GraphicsTestSuite.RunGfx_RegisGraphicsInputAsync. It differs from the passing test by
        // exactly two things: it selects the cursor style with S(C(I0)) first, and it asks for the
        // position report R(P(I)) in the same command string.
        var emulator = Draw("S(E)W(I3)"
            + "P[100,100]V[500,100][500,400][100,400][100,100]"
            + "P[100,100]V[500,400]"
            + "P[500,100]V[100,400]"
            + "P[300,250]C[+120]"
            + "S(C(I0))"
            + "P[400,240]"
            + "R(I0)R(P(I))");

        var shot = RenderedScreenshot.Capture(emulator, "regis-graphics-input-testserver-round1");

        // The picture itself must be there, so a total blank cannot pass as "no crosshair".
        Assert.True(InkNear(shot, 300, 100), "the box's top edge is missing, so nothing drew at all");

        // Then the two arms, well outside the box on all four sides - the same four points the
        // passing test checks.
        Assert.True(InkNear(shot, 40, 240), "the crosshair's left arm is missing");
        Assert.True(InkNear(shot, 760, 240), "the crosshair's right arm is missing");
        Assert.True(InkNear(shot, 400, 20), "the crosshair's top arm is missing");
        Assert.True(InkNear(shot, 400, 460), "the crosshair's bottom arm is missing");
    }

    [AvaloniaFact]
    public void TheScreenGoesBackToTheDrawingAloneWhenTheModeEnds()
    {
        // The other half of the same question: the crosshair must LEAVE without taking the picture
        // with it. Answering the host's request is what ends one-shot mode.
        var emulator = Draw("W(I2)P[200,120]V[600,120]V[600,360]V[200,360]V[200,120]"
            + "P[400,240]R(I0)R(P(I))");

        emulator.SendRegisInputReport("A");

        var shot = RenderedScreenshot.Capture(emulator, "regis-graphics-input-crosshair-gone");

        // The crosshair's arms are gone from well outside the box.
        Assert.False(InkNear(shot, 40, 240));
        Assert.False(InkNear(shot, 400, 20));

        // The box itself survived. Without this, "clear everything" would pass the two above.
        Assert.True(InkNear(shot, 400, 120));
    }

    [AvaloniaFact]
    public void TheDiamondSitsOnThePointAndLeavesTheRestAlone()
    {
        // "This cursor is a 21 x 21 pixel diamond." Small, so this is the one style where the whole
        // question is whether it can be SEEN at all on a busy screen - which is why the PNG matters
        // more here than anywhere else in this file.
        var emulator = Draw("W(I2)P[200,120]V[600,120]V[600,360]V[200,360]V[200,120]"
            + "P[400,240]S(C(I1))R(I0)");

        var shot = RenderedScreenshot.Capture(emulator, "regis-cursor-diamond");

        Assert.True(InkNear(shot, 400, 240 - 10));
        Assert.True(InkNear(shot, 400 + 10, 240));

        // Nothing at the screen edge, which is what tells it apart from the crosshair.
        Assert.False(InkNear(shot, 40, 240));
    }

    [AvaloniaFact]
    public void TheRubberBandRectangleStretchesFromTheDrawingPoint()
    {
        // "a rectangle, with one corner fixed at the current drawing (output) position and the
        // opposite corner at the current cursor position." Drawn over a box so the PNG shows the
        // rubber band and the host's picture together, which is how it would really be used.
        //
        // THE RUBBER BAND IS ANCHORED INSIDE THE BOX ON PURPOSE, so none of its four edges lies on
        // top of one of the box's. The first draft anchored the two at the same corner, and every
        // assertion below passed against a deliberately broken anchor - because the ink it found was
        // the BOX. Two unit tests caught it; this one was measuring the wrong thing entirely.
        var emulator = Draw("W(I2)P[200,120]V[600,120]V[600,360]V[200,360]V[200,120]"
            + "P[300,180]S(C(I4))R(I0)");

        emulator.MoveRegisInputCursor(200, 100);

        var shot = RenderedScreenshot.Capture(emulator, "regis-cursor-rubber-band-rectangle");

        // The two opposite corners, and a point along the top and the right edge between them.
        Assert.True(InkNear(shot, 300, 180));
        Assert.True(InkNear(shot, 500, 280));
        Assert.True(InkNear(shot, 400, 180));
        Assert.True(InkNear(shot, 500, 230));

        // Not filled, nothing outside it, and nothing at the screen edge.
        Assert.False(InkNear(shot, 400, 230));
        Assert.False(InkNear(shot, 550, 230));
        Assert.False(InkNear(shot, 40, 280));

        // JUST PAST THE FIXED CORNER, on the line the top edge runs along. This is the assertion
        // that pins the anchor: an edge that started anywhere left of the drawing point would put
        // ink here. Everything above still passed with the anchor deliberately shifted 50 pixels.
        Assert.False(InkNear(shot, 270, 180));
        Assert.False(InkNear(shot, 300, 150));
    }

    /// <summary>
    /// Looks for ink near a point given in ReGIS coordinates.
    /// </summary>
    /// <param name="shot">
    /// The rendered screen.
    /// </param>
    /// <param name="regisX">
    /// X in the 800 by 480 ReGIS screen.
    /// </param>
    /// <param name="regisY">
    /// Y in the same.
    /// </param>
    /// <returns>
    /// True when something brighter than the background is within a few pixels.
    /// </returns>
    /// <remarks>
    /// A small window rather than a single pixel, because the graphics plane is scaled to fit the
    /// text area and a one-pixel line does not land on an exact multiple. The window is far smaller
    /// than the distance between any two points these tests ask about.
    /// </remarks>
    private static bool InkNear(RenderedScreenshot shot, int regisX, int regisY)
    {
        double scaleX = (double)shot.Width / TerminalEmulatorBase.GraphicsPlaneWidth;
        double scaleY = (double)shot.Height / TerminalEmulatorBase.GraphicsPlaneHeight;

        int centreX = (int)(regisX * scaleX);
        int centreY = (int)(regisY * scaleY);

        for (int dy = -3; dy <= 3; dy++)
        {
            for (int dx = -3; dx <= 3; dx++)
            {
                int x = centreX + dx;
                int y = centreY + dy;
                if (x < 0 || y < 0 || x >= shot.Width || y >= shot.Height) continue;

                var colour = shot.PixelAt(x, y);
                if (colour.Red > 40 || colour.Green > 60 || colour.Blue > 40) return true;
            }
        }

        return false;
    }
}
