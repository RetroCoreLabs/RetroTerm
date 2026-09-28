using System.Text;
using Avalonia.Headless.XUnit;
using RetroTerm.Core.Configuration;
using RetroTerm.Core.Terminal.Emulators;
using Xunit;

namespace RetroTerm.Tests.Avalonia;

/// <summary>
/// Drawing the character shapes a host downloaded with DECDLD, through the real render chain.
///
/// The Core tests say the shapes are stored and that the right cells are marked as coming from the
/// downloaded set. This says the pixels actually appear - which is the half that makes the feature
/// worth anything, and the half no amount of parser testing can see.
/// </summary>
[Collection("Avalonia")]
public class SoftFontRenderingTests
{
    private static void Feed(TerminalEmulatorBase emulator, string data)
        => emulator.ProcessData(Encoding.ASCII.GetBytes(data));

    /// <summary>
    /// A terminal with one downloaded character: a solid block, 8 by 12, at the first position of
    /// the set - which is where 0x20 lands, so it is what a SPACE draws once the set is selected.
    /// </summary>
    private static TerminalEmulatorBase WithASolidBlockDownloaded()
    {
        var emulator = EmulatorFactory.CreateEmulator("VT220", 20, 4, 100);

        // Eight full-height columns: two bands of six rows, every strip '~' (all six bits).
        Feed(emulator, "\x1bP1;1;1;8;0;0;12;0{ @~~~~~~~~/~~~~~~~~\x1b\\");

        // Park the cursor out of the way - the renderer paints a solid block over the cell it sits
        // on, so a visible cursor is ink like any other.
        Feed(emulator, "\x1b[?25l");
        return emulator;
    }

    [AvaloniaFact]
    public void ADownloadedCharacterIsDrawnFromTheShapeTheHostSent()
    {
        var emulator = WithASolidBlockDownloaded();

        // Select the downloaded set into G0, then print the first character of it.
        Feed(emulator, "\x1b( @");
        Feed(emulator, " ");

        using var shot = RenderedScreenshot.Capture(emulator, "softfont-block");

        // A space normally draws nothing at all. This one is a solid block, because the host said
        // so - and that difference is the whole feature.
        Assert.True(shot.CellHasInk(0, 0), "the downloaded shape should have been drawn");
    }

    [AvaloniaFact]
    public void WithoutSelectingTheSetASpaceIsStillASpace()
    {
        // The guard against overreach: downloading shapes must not change what ordinary text draws.
        var emulator = WithASolidBlockDownloaded();

        Feed(emulator, " ");

        using var shot = RenderedScreenshot.Capture(emulator, "softfont-not-selected");

        Assert.False(shot.CellHasInk(0, 0), "an unselected set must not affect ordinary text");
    }

    [AvaloniaFact]
    public void SelectingTheSetLeavesUndefinedCharactersBlank()
    {
        // Only the first character was downloaded. The rest of the set has no shape, and drawing
        // the system font's letter there would be worse than drawing nothing - the host is showing
        // characters this machine has no glyph for, so a wrong glyph is a wrong answer.
        var emulator = WithASolidBlockDownloaded();

        Feed(emulator, "\x1b( @");
        Feed(emulator, "X");

        using var shot = RenderedScreenshot.Capture(emulator, "softfont-undefined");

        Assert.False(shot.CellHasInk(0, 0));
    }

    [AvaloniaFact]
    public void OrdinaryTextIsUnaffectedAfterTheSetIsSelectedBack()
    {
        var emulator = WithASolidBlockDownloaded();

        Feed(emulator, "\x1b( @");
        Feed(emulator, " ");
        Feed(emulator, "\x1b(B");     // back to US ASCII
        Feed(emulator, "X");

        using var shot = RenderedScreenshot.Capture(emulator, "softfont-back-to-ascii");

        Assert.True(shot.CellHasInk(0, 0), "the downloaded block should still be there");
        Assert.True(shot.CellHasInk(0, 1), "and the X after it should be an ordinary X");
    }

    [AvaloniaFact]
    public void TheDownloadedShapeIsDrawnTheRightWayUp()
    {
        // <para><b>Why a block was not enough</b></para>
        // Every other test here downloads a solid 8 by 12 block, and a block is symmetric: it looks
        // identical upside down, mirrored, or with its two sixel bands swapped. So all of them would
        // still pass if the renderer read the host's rows in the wrong order.
        //
        // This one downloads a shape that is lit in its TOP half only - the first sixel band all
        // six bits set, the second band none - and checks the ink landed in the top half of the
        // cell. DECDLD sends a character as vertical strips of six pixels, top bit first, so
        // getting this backwards is a real and easy mistake.
        var emulator = EmulatorFactory.CreateEmulator("VT220", 20, 4, 100);

        Feed(emulator, "\x1bP1;1;1;8;0;0;12;0{ @~~~~~~~~/????????\x1b\\");
        Feed(emulator, "\x1b[?25l");
        Feed(emulator, "\x1b( @");
        Feed(emulator, " ");

        using var shot = RenderedScreenshot.Capture(emulator, "softfont-top-half");

        int width = (int)shot.CellWidth;
        int cellHeight = (int)shot.CellHeight;

        // The background is taken from an EMPTY cell, not from this one. A glyph that covers more
        // than half its cell makes itself the cell's dominant colour, and then "differs from the
        // dominant colour" counts the background instead of the ink - which is exactly backwards,
        // and read as an upside-down glyph when this test was first written.
        var background = shot.DominantColorInCell(3, 19);

        int topInk = 0;
        int bottomInk = 0;

        for (int y = 0; y < cellHeight; y++)
        {
            for (int x = 0; x < width; x++)
            {
                if (RenderedScreenshot.ApproximatelyEqual(shot.PixelAt(x, y), background, 24))
                {
                    continue;
                }

                if (y < cellHeight / 2) topInk++;
                else bottomInk++;
            }
        }

        Assert.True(topInk > 0, "the top half of the downloaded shape should be lit");
        Assert.True(bottomInk * 4 < topInk,
            $"the bottom half should be nearly empty - top {topInk}, bottom {bottomInk}");
    }
}
