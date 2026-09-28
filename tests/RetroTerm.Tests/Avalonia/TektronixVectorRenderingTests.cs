using System;
using System.Collections.Generic;
using System.Text;
using Avalonia.Headless.XUnit;
using RetroTerm.Core.Terminal.Emulators.TDV;
using Xunit;

namespace RetroTerm.Tests.Avalonia;

/// <summary>
/// Tektronix vector drawing, end to end: a host enters graph mode, sends coordinates, and a shape
/// appears on screen.
///
/// This is the traffic a plotting program actually sends. The <c>ESC "</c> sequences set things up
/// - show the plane, pick a line style, clear the memory - and then the drawing itself arrives as
/// GS followed by a stream of coordinate bytes.
/// </summary>
[Collection("Avalonia")]
public class TektronixVectorRenderingTests
{
    private const byte Gs = 0x1D;
    private const byte Us = 0x1F;

    private static void Feed(TDV2200Emulator emulator, params byte[] bytes)
        => emulator.ProcessData(bytes);

    private static void Feed(TDV2200Emulator emulator, string text)
        => emulator.ProcessData(Encoding.ASCII.GetBytes(text));

    /// <summary>
    /// The four bytes for a point, in the order a host sends them.
    /// </summary>
    private static byte[] Point(int x, int y) => new[]
    {
        (byte)(0x20 | ((y >> 5) & 0x1F)),
        (byte)(0x60 | (y & 0x1F)),
        (byte)(0x20 | ((x >> 5) & 0x1F)),
        (byte)(0x40 | (x & 0x1F)),
    };

    private static void ShowPlane(TDV2200Emulator emulator) => Feed(emulator, "\u001b\"17h");

    [Fact]
    public void CoordinateBytesAreNotPrintedAsText()
    {
        // THE thing that goes wrong first. Coordinate bytes are ordinary printable ASCII - a point
        // is bytes like ' ', '`', '0', '@' - so a terminal that does not know it is in graph mode
        // fills the screen with punctuation instead of drawing.
        var emulator = new TDV2200Emulator(20, 4);
        ShowPlane(emulator);

        Feed(emulator, Gs);
        Feed(emulator, Point(100, 100));
        Feed(emulator, Point(900, 700));

        var buffer = emulator.GetBuffer();
        for (int col = 0; col < buffer.Width; col++)
        {
            Assert.True(buffer.GetCell(0, col).IsEmpty,
                $"column {col} should be empty; coordinates must not be printed");
        }
    }

    [Fact]
    public void TextStillPrintsAfterLeavingGraphMode()
    {
        // The guard against the opposite failure: a terminal stuck in graph mode swallows the
        // host's text and looks like a hung session.
        //
        // The text lands AT THE LAST PLOTTED POINT, not at home. That is how a Tektronix host
        // writes a label anywhere on screen, and this test originally asserted cell (0,0) - which
        // was the behaviour before that was implemented, and wrong.
        //
        // Logical (100,100) over a 20x4 grid on a 1024x780 space: column 1, and row 3 because Y
        // runs up in Tek space and down the screen.
        var emulator = new TDV2200Emulator(20, 4);

        Feed(emulator, Gs);
        Feed(emulator, Point(100, 100));
        Feed(emulator, Us);
        Feed(emulator, "HELLO");

        Assert.Equal('H', (char)emulator.GetBuffer().GetCell(3, 1).Codepoint);
    }

    [AvaloniaFact]
    public void AVectorReachesTheScreen()
    {
        var emulator = new TDV2200Emulator(20, 4);
        ShowPlane(emulator);

        Feed(emulator, Gs);
        Feed(emulator, Point(0, 0));            // move to the bottom left
        Feed(emulator, Point(1023, 779));       // draw to the top right

        Assert.True(emulator.Graphics!.HasAnythingToDraw());

        using var shot = RenderedScreenshot.Capture(emulator, "tek-vector-diagonal");
        Assert.True(shot.Width > 0);
    }

    [AvaloniaFact]
    public void AClosedShapeIsDrawn()
    {
        // A box, the way a plotting program draws one: move to a corner, then four draws round it.
        // Saved so a human can see whether it actually looks like a box.
        var emulator = new TDV2200Emulator(40, 12);
        ShowPlane(emulator);

        Feed(emulator, Gs);
        Feed(emulator, Point(200, 150));
        Feed(emulator, Point(820, 150));
        Feed(emulator, Point(820, 620));
        Feed(emulator, Point(200, 620));
        Feed(emulator, Point(200, 150));
        Feed(emulator, Us);

        using var shot = RenderedScreenshot.Capture(emulator, "tek-vector-box");

        Assert.True(emulator.Graphics!.HasAnythingToDraw());
        Assert.True(shot.Width > 0);
    }

    [AvaloniaFact]
    public void AShapeAndTextShareTheScreen()
    {
        // The whole point of a plane model: a plot with a caption under it.
        var emulator = new TDV2200Emulator(40, 12);
        Feed(emulator, "PLOT OF SOMETHING");
        ShowPlane(emulator);

        Feed(emulator, Gs);
        Feed(emulator, Point(150, 100));
        Feed(emulator, Point(400, 500));
        Feed(emulator, Point(650, 200));
        Feed(emulator, Point(900, 600));
        Feed(emulator, Us);

        using var shot = RenderedScreenshot.Capture(emulator, "tek-vector-with-caption");

        Assert.True(shot.CellHasInk(0, 0), "the caption must still be readable under the plot");
        Assert.True(emulator.Graphics!.HasAnythingToDraw());
    }
}
