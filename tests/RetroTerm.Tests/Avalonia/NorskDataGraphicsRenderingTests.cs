using System.Text;
using Avalonia.Headless.XUnit;
using RetroTerm.Core.Terminal.Emulators.TDV;
using Xunit;

namespace RetroTerm.Tests.Avalonia;

/// <summary>
/// ND graphics end to end: bytes from a host reach real pixels on the real renderer.
///
/// Everything before this was headless Core work — surfaces, a transform, planes, a compositor, a
/// protocol module — none of which put anything on a screen. This is the first test that drives the
/// whole path: <c>ESC "</c> arrives, the parser's ND ground mode tokenises it, the module draws on
/// its plane, the compositor flattens, and TerminalRenderer blits the result over the text it drew.
///
/// It is also the first place the wiring can be wrong in ways Core tests cannot see: a profile that
/// never switched the ground mode on, an emulator that never composited, a renderer that never
/// looked.
/// </summary>
[Collection("Avalonia")]
public class NorskDataGraphicsRenderingTests
{
    private static void Feed(TDV2200Emulator emulator, string text)
        => emulator.ProcessData(Encoding.ASCII.GetBytes(text));

    [AvaloniaFact]
    public void ATdv2200HasGraphicsAndAVt100DoesNot()
    {
        // Null for every text-only profile, which is most of them. The renderer checks and skips
        // the whole blit, so a VT100 pays nothing for a plane it will never use.
        var nd = new TDV2200Emulator(20, 4);
        var vt = new RetroTerm.Core.Terminal.Emulators.VT100Emulator(20, 4);

        Assert.NotNull(nd.Graphics);
        Assert.NotNull(nd.GraphicsModule);
        Assert.Null(vt.Graphics);
    }

    [AvaloniaFact]
    public void TheGroundModeIsOnForATdv2200()
    {
        // A TDV2200 IS the ND graphic terminal, so ESC " must tokenise as an ND sequence here even
        // though the parser's default - correctly - reads it as an ordinary escape.
        var emulator = new TDV2200Emulator(20, 4);

        Feed(emulator, "\u001b\"17h");

        Assert.True(emulator.GraphicsModule!.GraphicsDisplayed);
    }

    [AvaloniaFact]
    public void NothingIsDrawnUntilAHostAsksForIt()
    {
        // The guard that matters for every existing session: a terminal that has never seen a
        // graphics command must look exactly as it did before graphics existed.
        var emulator = new TDV2200Emulator(20, 4);
        Feed(emulator, "TEXT");

        Assert.False(emulator.Graphics!.HasAnythingToDraw());

        using var shot = RenderedScreenshot.Capture(emulator, "nd-graphics-text-only");
        Assert.True(shot.CellHasInk(0, 0), "the text must still be drawn");
    }

    [AvaloniaFact]
    public void ARectangleFromTheHostReachesTheScreen()
    {
        // THE end-to-end test. ESC "17h shows the plane, ESC "8;...h fills a rectangle across the
        // bottom-left of the terminal's own space, and it has to appear on screen.
        var emulator = new TDV2200Emulator(20, 4);
        Feed(emulator, "\u001b\"17h");
        Feed(emulator, "\u001b\"8;0;0;511;389h");

        Assert.True(emulator.Graphics!.HasAnythingToDraw());

        using var shot = RenderedScreenshot.Capture(emulator, "nd-graphics-rectangle");

        // The rectangle covers the left half and bottom half of the ND space. The Y flip means
        // "bottom" of that space is the BOTTOM of the screen.
        int width = shot.Width;
        int height = shot.Height;

        var insideRectangle = shot.PixelAt(width / 4, height * 3 / 4);
        var outsideRectangle = shot.PixelAt(width * 3 / 4, height / 4);

        Assert.False(insideRectangle == outsideRectangle,
            "the filled rectangle must not look the same as the empty part of the screen");
    }

    [AvaloniaFact]
    public void HidingThePlaneTakesTheDrawingOffTheScreen()
    {
        var emulator = new TDV2200Emulator(20, 4);
        Feed(emulator, "\u001b\"17h");
        Feed(emulator, "\u001b\"8;0;0;511;389h");

        using var visible = RenderedScreenshot.Capture(emulator, null);

        Feed(emulator, "\u001b\"17l");
        using var hidden = RenderedScreenshot.Capture(emulator, "nd-graphics-hidden");

        Assert.True(hidden.FractionDifferentFrom(visible) > 0.0,
            "hiding the graphics plane must change what is on screen");
    }

    [AvaloniaFact]
    public void TextUnderTheGraphicsStillShowsThrough()
    {
        // The transparent pixels earning their keep. A graphics plane overlays the character
        // display; it does not replace it, and a plane that blacked out the text would make every
        // ND session unusable the moment a host drew one line.
        var emulator = new TDV2200Emulator(20, 4);
        Feed(emulator, "TEXT");
        Feed(emulator, "\u001b\"17h");

        // A rectangle in the TOP-RIGHT of the ND space, which is the top right of the screen -
        // nowhere near the text in the top-left cell.
        Feed(emulator, "\u001b\"8;600;500;1023;779h");

        using var shot = RenderedScreenshot.Capture(emulator, "nd-graphics-over-text");

        Assert.True(shot.CellHasInk(0, 0),
            "the text must still be readable with a graphics plane shown over it");
    }

    [AvaloniaFact]
    public void ClearingGraphicMemoryLeavesTheTextAlone()
    {
        var emulator = new TDV2200Emulator(20, 4);
        Feed(emulator, "TEXT");
        Feed(emulator, "\u001b\"17h");
        Feed(emulator, "\u001b\"8;0;0;1023;779h");
        Feed(emulator, "\u001b\"9h");

        Assert.False(emulator.Graphics!.HasAnythingToDraw());

        using var shot = RenderedScreenshot.Capture(emulator, "nd-graphics-cleared");
        Assert.True(shot.CellHasInk(0, 0), "clearing graphic memory must not erase the text");
    }
}
