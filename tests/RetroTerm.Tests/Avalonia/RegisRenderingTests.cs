using System.Text;
using Avalonia.Headless.XUnit;
using RetroTerm.Core.Configuration;
using RetroTerm.Core.Terminal.Emulators;
using Xunit;

namespace RetroTerm.Tests.Avalonia;

/// <summary>
/// A ReGIS drawing through the real render chain, written out as a PNG to be looked at.
///
/// Every colour defect this project has had was found by opening one of these rather than by an
/// assertion — see the note in CLAUDE.md. So the drawing here is deliberately something a human can
/// judge at a glance: a box, a diagonal across it, and a circle, each in a different colour.
/// </summary>
[Collection("Avalonia")]
public class RegisRenderingTests
{
    private static void Feed(TerminalEmulatorBase emulator, string data)
        => emulator.ProcessData(Encoding.ASCII.GetBytes(data));

    [AvaloniaFact]
    public void ABoxADiagonalAndACircleAreDrawn()
    {
        var emulator = EmulatorFactory.CreateEmulator("VT340", 80, 24, 100);
        Feed(emulator, "\x1b[?25l");

        Feed(emulator,
            "\x1bPp" +
            "W(I3)P[100,100]V[600,100][600,380][100,380][100,100]" +   // a green box
            "W(I2)P[100,100]V[600,380]" +                              // a red diagonal
            "W(I1)P[350,240]C[450,240]" +                              // a blue circle
            "\x1b\\");

        using var shot = RenderedScreenshot.Capture(emulator, "regis-box-diagonal-circle");

        Assert.True(emulator.Graphics!.HasAnythingToDraw());

        // The box's top edge runs along y=100 of an 800x480 plane, which is near the top of the
        // screen; the assertion is on presence, and the PNG is for judging the shape.
        Assert.True(shot.CellHasInk(5, 20), "the drawing should have reached the screen");
    }

    [AvaloniaFact]
    public void EachColourIsTheOneTheHostAskedFor()
    {
        // Pinning the hues, because the last graphics protocol added here shipped with red and
        // blue crossed and no assertion could see it.
        var emulator = EmulatorFactory.CreateEmulator("VT340", 80, 24, 100);
        Feed(emulator, "\x1b[?25l");

        // Three thick horizontal bars, well apart: red, green, blue.
        Feed(emulator,
            "\x1bPp" +
            "W(I2)P[100,100]V[700,100]P[100,101]V[700,101]P[100,102]V[700,102]" +
            "W(I3)P[100,200]V[700,200]P[100,201]V[700,201]P[100,202]V[700,202]" +
            "W(I1)P[100,300]V[700,300]P[100,301]V[700,301]P[100,302]V[700,302]" +
            "\x1b\\");

        using var shot = RenderedScreenshot.Capture(emulator, "regis-three-colours");

        emulator.Graphics!.Composite();
        var output = emulator.Graphics.Output;

        var red = output.GetPixel(400, 101);
        var green = output.GetPixel(400, 201);
        var blue = output.GetPixel(400, 301);

        Assert.True(red.R > red.B, $"the first bar was drawn in register 2, red; got {red.R},{red.G},{red.B}");
        Assert.True(green.G > green.R, $"the second was register 3, green; got {green.R},{green.G},{green.B}");
        Assert.True(blue.B > blue.R, $"the third was register 1, blue; got {blue.R},{blue.G},{blue.B}");
    }

    /// <summary>
    /// Polygon fill, drawn as the manual's own figures so the picture can be judged against them.
    /// </summary>
    /// <remarks>
    /// Figures 11-1 and 11-2 of the VT330/VT340 Graphics Programming manual: a filled square, a
    /// filled diamond and a filled circle. Each is drawn twice - filled in one colour, then the
    /// same outline stroked over it in another - which is the manual's own advice, "You should draw
    /// a border after the filled area", and also makes a fill that missed its outline obvious to
    /// look at rather than only to assert.
    /// </remarks>
    [AvaloniaFact]
    public void FilledShapesAreDrawnAndOutlined()
    {
        var emulator = EmulatorFactory.CreateEmulator("VT340", 80, 24, 100);
        Feed(emulator, "\x1b[?25l");

        Feed(emulator,
            "\x1bPp" +
            // A filled square in green with a red border, from Figure 11-1.
            "W(I3)P[50,100]F(V[+150][,+150][-150][,-150])" +
            "W(I2)P[50,100]V[+150][,+150][-150][,-150]" +
            // A filled diamond in cyan, also Figure 11-1.
            "W(I5)P[400,100]F(V[+100,+75][-100,+75][-100,-75][+100,-75])" +
            // A filled circle in magenta with a yellow border, from Figure 11-2.
            "W(I4)P[600,300]F(C[+80])" +
            "W(I6)P[600,300]C[+80]" +
            "\x1b\\");

        using var shot = RenderedScreenshot.Capture(emulator, "regis-polygon-fill");

        emulator.Graphics!.Composite();
        var output = emulator.Graphics.Output;

        // Inside each shape, in the colour it was filled with.
        var square = output.GetPixel(125, 175);
        var diamond = output.GetPixel(400, 175);
        var circle = output.GetPixel(600, 300);

        Assert.True(square.G > square.R && square.G > square.B,
            $"the square should be filled green; got {square.R},{square.G},{square.B}");
        Assert.True(diamond.G > diamond.R && diamond.B > diamond.R,
            $"the diamond should be filled cyan; got {diamond.R},{diamond.G},{diamond.B}");
        Assert.True(circle.R > circle.G && circle.B > circle.G,
            $"the circle should be filled magenta; got {circle.R},{circle.G},{circle.B}");

        // And a point outside every shape is still empty, so the fill did not flood the plane.
        Assert.True(output.GetPixel(300, 420).IsTransparent, "the fill escaped its outline");

        // NOT CellHasInk, which cannot see a solid fill. It counts pixels that differ from the
        // cell's own dominant colour, so it measures CONTRAST inside a cell - a cell entirely
        // inside a filled shape is uniform and reads as blank. DominantColorInCell asks the right
        // question of a fill: what colour did this cell actually come out?
        //
        // Row 10, column 8 of an 80x24 screen over the 800x480 plane is plane (80..90, 200..220),
        // which is inside the green square.
        var cell = shot.DominantColorInCell(10, 8);
        Assert.True(cell.Green > cell.Red && cell.Green > cell.Blue,
            $"the square should have reached the screen green; got {cell.Red},{cell.Green},{cell.Blue}");
    }
}
