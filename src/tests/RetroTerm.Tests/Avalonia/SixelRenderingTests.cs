using System.Text;
using Avalonia.Headless.XUnit;
using RetroTerm.Core.Configuration;
using RetroTerm.Core.Terminal.Emulators;
using Xunit;

namespace RetroTerm.Tests.Avalonia;

/// <summary>
/// A Sixel image drawn through the real render chain.
///
/// The decoder is proven against a bare surface and the wiring is proven against the compositor.
/// This is the last link: that the plane actually reaches the screen, on a terminal whose renderer
/// was built for text. It writes a PNG so the picture can be looked at rather than only asserted on.
/// </summary>
[Collection("Avalonia")]
public class SixelRenderingTests
{
    private static void Feed(TerminalEmulatorBase emulator, string data)
        => emulator.ProcessData(Encoding.ASCII.GetBytes(data));

    [AvaloniaFact]
    public void AnImageIsDrawnOnTheScreen()
    {
        var emulator = EmulatorFactory.CreateEmulator("VT340", 20, 6, 100);
        Feed(emulator, "\x1b[?25l");        // park the cursor - a visible one is ink like any other
        Feed(emulator, "\x1b[1;1H");

        // Three colours side by side, each a solid block six pixels tall and eight wide:
        // red, green, blue. Repeats keep it short.
        Feed(emulator,
            "\x1bPq" +
            "#1;2;100;0;0!8~" +
            "#2;2;0;100;0!8~" +
            "#3;2;0;0;100!8~" +
            "\x1b\\");

        using var shot = RenderedScreenshot.Capture(emulator, "sixel-three-colours");

        Assert.True(shot.CellHasInk(0, 0), "the image should have reached the screen");

        // AND IT MUST BE THE RIGHT WAY ROUND. The blit used to swap red and blue, so this picture
        // came out blue-green-red. Nothing caught it because the only graphics before Sixel drew a
        // single green, which swaps to another green. Asserting on the actual hues is what makes
        // that impossible to reintroduce.
        var first = shot.BrightestColorInCell(0, 0);
        var last = shot.BrightestColorInCell(0, 2);

        Assert.True(first.Red > first.Blue, $"the first block was sent red; got {first}");
        Assert.True(last.Blue > last.Red, $"the last block was sent blue; got {last}");
    }

    [AvaloniaFact]
    public void TextAroundTheImageStillDraws()
    {
        // The guard against the plane covering the terminal: an image is an overlay, not a
        // replacement for the screen.
        var emulator = EmulatorFactory.CreateEmulator("VT340", 20, 6, 100);
        Feed(emulator, "\x1b[?25l");
        Feed(emulator, "\x1b[1;1H");
        Feed(emulator, "\x1bPq#2;2;0;100;0!8~\x1b\\");
        Feed(emulator, "\x1b[3;1HTEXT");

        using var shot = RenderedScreenshot.Capture(emulator, "sixel-with-text");

        Assert.True(shot.CellHasInk(2, 0), "the T of TEXT should be drawn");
        Assert.True(shot.CellHasInk(2, 3), "and the last letter too");
    }
}
