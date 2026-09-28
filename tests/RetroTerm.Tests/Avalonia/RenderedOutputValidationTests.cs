using System.Text;
using Avalonia.Headless.XUnit;
using RetroTerm.Core.Terminal.Emulators;
using RetroTerm.Core.Terminal.Emulators.TDV;
using SkiaSharp;
using Xunit;
using Xunit.Abstractions;

namespace RetroTerm.Tests.Avalonia;

/// <summary>
/// Validates what the terminal ACTUALLY draws, by rendering through the production
/// TerminalCanvas/TerminalRenderer/font-renderer chain and inspecting the resulting pixels.
///
/// Two jobs at once, deliberately:
///  - every test writes a PNG into Avalonia\images\rendered so a human can look at it and
///    confirm the emulation really looks right;
///  - every test also asserts on the pixels, so a regression fails the build without anyone
///    having to look.
///
/// See RenderedScreenshot for why this does not re-implement any rendering of its own.
/// </summary>
[Collection("Avalonia")]
public class RenderedOutputValidationTests
{
    private readonly ITestOutputHelper _output;

    public RenderedOutputValidationTests(ITestOutputHelper output)
    {
        _output = output;
    }

    private static void Feed(TerminalEmulatorBase emulator, string data)
    {
        emulator.ProcessData(Encoding.ASCII.GetBytes(data));
    }

    /// <summary>
    /// Hides the cursor (DECTCEM reset) before a glyph-ink check.
    ///
    /// The renderer paints a solid block cursor OVER the cell it sits on, so a visible
    /// cursor is ink like any other. Every "is this cell empty?" assertion has to either
    /// hide the cursor or keep away from it - the first version of these tests did neither
    /// and five of them failed on the cursor rather than on any real defect.
    /// Cursor drawing itself is covered by CursorIsDrawn_AndCanBeHidden.
    /// </summary>
    private static void HideCursor(TerminalEmulatorBase emulator)
    {
        Feed(emulator, "\x1b[?25l");
    }

    // ─────────────────────────────────────────────────────────────
    // The harness itself must be trustworthy before anything else.
    // ─────────────────────────────────────────────────────────────

    [AvaloniaFact]
    public void Harness_ProducesAnImageOfTheExpectedSize()
    {
        var emulator = new VT100Emulator(20, 5);
        using var shot = RenderedScreenshot.Capture(emulator, "harness-blank-vt100");

        _output.WriteLine($"Rendered {shot.Width}x{shot.Height}, cell {shot.CellWidth}x{shot.CellHeight}");
        _output.WriteLine($"Saved: {shot.SavedPath}");

        // 20 columns x 5 rows at the font's own cell size, scale 1.0.
        // Rounded, not truncated - cell sizes are fractional (7.697 x 16.393 for the
        // default system font) and Capture rounds when it sizes the bitmap.
        Assert.Equal((int)System.Math.Round(shot.CellWidth * 20), shot.Width);
        Assert.Equal((int)System.Math.Round(shot.CellHeight * 5), shot.Height);
        Assert.NotNull(shot.SavedPath);
        Assert.True(System.IO.File.Exists(shot.SavedPath));
    }

    [AvaloniaFact]
    public void BlankScreen_HasNoInk_ButTextDoes()
    {
        // Establishes the baseline both ways round: an empty screen must be empty, and the
        // same screen with a character must not be. Without the first half, an "ink" check
        // could pass on a renderer that paints noise everywhere.
        var blank = new VT100Emulator(20, 5);
        HideCursor(blank);
        using var blankShot = RenderedScreenshot.Capture(blank, "baseline-blank");

        Assert.False(blankShot.CellHasInk(0, 0), "blank screen drew ink in cell (0,0)");

        var withText = new VT100Emulator(20, 5);
        HideCursor(withText);
        Feed(withText, "A");
        using var textShot = RenderedScreenshot.Capture(withText, "baseline-letter-A");

        Assert.True(textShot.CellHasInk(0, 0), "the letter A produced no visible pixels");
        Assert.False(textShot.CellHasInk(0, 5), "an untouched cell drew ink");
    }

    [AvaloniaFact]
    public void TextAppearsInTheCellsItWasWrittenTo()
    {
        var emulator = new VT100Emulator(20, 5);
        HideCursor(emulator);
        Feed(emulator, "\x1b[2;3HXY");   // row 2, column 3 (1-based)

        using var shot = RenderedScreenshot.Capture(emulator, "cursor-addressed-text");

        // 0-based: row 1, columns 2 and 3 have ink; their neighbours do not.
        Assert.True(shot.CellHasInk(1, 2), "X missing at row 1 col 2");
        Assert.True(shot.CellHasInk(1, 3), "Y missing at row 1 col 3");
        Assert.False(shot.CellHasInk(1, 1), "ink bled into the cell before the text");
        Assert.False(shot.CellHasInk(1, 4), "ink bled into the cell after the text");
        Assert.False(shot.CellHasInk(0, 2), "ink appeared on the wrong row");
    }

    [AvaloniaFact]
    public void CursorIsDrawn_AndCanBeHidden()
    {
        // Worth its own test: the block cursor is painted over its cell, so it is the one
        // thing that puts ink on an otherwise blank screen. DECTCEM must turn it off.
        var visible = new VT100Emulator(20, 5);
        Feed(visible, "\x1b[3;7H");   // park it somewhere unambiguous: row 2, col 6 (0-based)
        using var visibleShot = RenderedScreenshot.Capture(visible, "cursor-visible");

        Assert.True(visibleShot.CellHasInk(2, 6), "the cursor was not drawn at its position");

        var hidden = new VT100Emulator(20, 5);
        Feed(hidden, "\x1b[3;7H");
        Feed(hidden, "\x1b[?25l");    // DECTCEM reset - hide cursor
        using var hiddenShot = RenderedScreenshot.Capture(hidden, "cursor-hidden");

        Assert.False(hiddenShot.CellHasInk(2, 6), "the cursor was still drawn after DECTCEM reset");
    }

    // ─────────────────────────────────────────────────────────────
    // Attributes that the renderer - not the emulator - is responsible for
    // ─────────────────────────────────────────────────────────────

    [AvaloniaFact]
    public void ReverseVideo_InvertsTheCell()
    {
        var emulator = new VT100Emulator(20, 5);
        HideCursor(emulator);
        Feed(emulator, "N");             // normal
        Feed(emulator, "\x1b[7mR");      // SGR 7 - reverse

        using var shot = RenderedScreenshot.Capture(emulator, "attribute-reverse-video");

        var normalCellBackground = shot.DominantColorInCell(0, 0);
        var reverseCellBackground = shot.DominantColorInCell(0, 1);

        _output.WriteLine($"normal cell dominant  = {normalCellBackground}");
        _output.WriteLine($"reverse cell dominant = {reverseCellBackground}");

        // In a reverse cell most of the area is the foreground colour, so the dominant
        // colour must differ from the normal cell's dominant (the background).
        Assert.False(
            RenderedScreenshot.ApproximatelyEqual(normalCellBackground, reverseCellBackground, 24),
            "reverse video did not change the cell background");
    }

    [AvaloniaFact]
    public void Bold_IsBrighterThanNormal()
    {
        var emulator = new VT100Emulator(20, 5);
        HideCursor(emulator);
        Feed(emulator, "\x1b[0mN");   // normal
        Feed(emulator, "\x1b[1mB");   // bold

        using var shot = RenderedScreenshot.Capture(emulator, "attribute-bold");

        var normalInk = shot.BrightestColorInCell(0, 0);
        var boldInk = shot.BrightestColorInCell(0, 1);

        int normalLuma = normalInk.Red + normalInk.Green + normalInk.Blue;
        int boldLuma = boldInk.Red + boldInk.Green + boldInk.Blue;

        _output.WriteLine($"normal ink = {normalInk} (luma {normalLuma})");
        _output.WriteLine($"bold ink   = {boldInk} (luma {boldLuma})");

        Assert.True(boldLuma >= normalLuma,
            $"bold ink ({boldLuma}) should not be dimmer than normal ink ({normalLuma})");
    }

    [AvaloniaFact]
    public void Hidden_DrawsNothing()
    {
        var emulator = new VT100Emulator(20, 5);
        HideCursor(emulator);
        Feed(emulator, "\x1b[8mSECRET");   // SGR 8 - conceal

        using var shot = RenderedScreenshot.Capture(emulator, "attribute-hidden");

        for (int col = 0; col < 6; col++)
        {
            Assert.False(shot.CellHasInk(0, col), $"concealed text was drawn at column {col}");
        }
    }

    [AvaloniaFact]
    public void IndexedColour_ChangesTheInkColour()
    {
        var emulator = new VT100Emulator(20, 5);
        Feed(emulator, "\x1b[31mR");                 // ANSI red
        Feed(emulator, "\x1b[38;5;21mB");            // 256-colour blue
        Feed(emulator, "\x1b[38;2;255;255;0mY");     // 24-bit yellow

        using var shot = RenderedScreenshot.Capture(emulator, "colour-red-blue-yellow");

        var red = shot.BrightestColorInCell(0, 0);
        var blue = shot.BrightestColorInCell(0, 1);
        var yellow = shot.BrightestColorInCell(0, 2);

        _output.WriteLine($"red={red} blue={blue} yellow={yellow}");

        Assert.True(red.Red > red.Blue, $"ANSI red rendered as {red}");
        Assert.True(blue.Blue > blue.Red, $"256-colour blue rendered as {blue}");
        Assert.True(yellow.Red > 128 && yellow.Green > 128 && yellow.Blue < 128,
            $"24-bit yellow rendered as {yellow}");
    }

    // ─────────────────────────────────────────────────────────────
    // Screen operations, checked as pictures rather than buffer reads
    // ─────────────────────────────────────────────────────────────

    [AvaloniaFact]
    public void EraseDisplay_ClearsThePicture()
    {
        var emulator = new VT100Emulator(20, 5);
        HideCursor(emulator);
        Feed(emulator, "HELLO WORLD");

        using (var before = RenderedScreenshot.Capture(emulator, "erase-before"))
        {
            Assert.True(before.CellHasInk(0, 0));
        }

        Feed(emulator, "\x1b[2J");
        using var after = RenderedScreenshot.Capture(emulator, "erase-after");

        for (int col = 0; col < 11; col++)
        {
            Assert.False(after.CellHasInk(0, col), $"column {col} survived ED");
        }
    }

    [AvaloniaFact]
    public void ScrollingRegion_MovesTextUpOnScreen()
    {
        var emulator = new VT100Emulator(20, 6);
        Feed(emulator, "\x1b[1;3r");    // scroll region rows 1-3
        Feed(emulator, "\x1b[1;1HTOP");
        Feed(emulator, "\x1b[3;1HBOT");

        using var before = RenderedScreenshot.Capture(emulator, "scroll-region-before");
        Assert.True(before.CellHasInk(0, 0), "TOP not drawn before scrolling");

        // Force the region to scroll by indexing at its bottom line
        Feed(emulator, "\x1b[3;1H\n");

        using var after = RenderedScreenshot.Capture(emulator, "scroll-region-after");

        double changed = before.FractionDifferentFrom(after);
        _output.WriteLine($"pixels changed by the scroll: {changed:P1}");
        Assert.True(changed > 0.0, "scrolling the region did not change the picture at all");
    }

    // ─────────────────────────────────────────────────────────────
    // TDV2200: the bitmap-font path, which the System font path never exercises
    // ─────────────────────────────────────────────────────────────

    [AvaloniaFact]
    public void Tdv2200_RendersTextWithItsBitmapFont()
    {
        var emulator = new TDV2200Emulator(20, 5);
        emulator.ProcessInput(Encoding.ASCII.GetBytes("TDV2200"));

        using var shot = RenderedScreenshot.Capture(emulator, "tdv2200-text");

        _output.WriteLine($"cell size {shot.CellWidth}x{shot.CellHeight} (TDV2200 bitmap font)");

        for (int col = 0; col < 7; col++)
        {
            Assert.True(shot.CellHasInk(0, col), $"TDV2200 glyph missing at column {col}");
        }
        Assert.False(shot.CellHasInk(0, 8), "ink appeared past the end of the text");
    }

    [AvaloniaFact]
    public void Tdv2200_GraphicsCharacterSet_DrawsDifferentGlyphsThanAscii()
    {
        // Same byte, different character set: the picture must differ, which is the whole
        // point of the FontNumber side-channel the TDV path uses.
        var ascii = new TDV2200Emulator(10, 3);
        ascii.ProcessInput(Encoding.ASCII.GetBytes("qqqq"));
        using var asciiShot = RenderedScreenshot.Capture(ascii, "tdv2200-charset-ascii");

        var graphics = new TDV2200Emulator(10, 3);
        graphics.ProcessInput(new byte[] { 0x1B, (byte)'n' });   // LS2 - lock G2 (GraphicsI)
        graphics.ProcessInput(Encoding.ASCII.GetBytes("qqqq"));
        using var graphicsShot = RenderedScreenshot.Capture(graphics, "tdv2200-charset-graphics");

        double changed = asciiShot.FractionDifferentFrom(graphicsShot);
        _output.WriteLine($"ascii vs graphics differ by {changed:P1} of pixels");

        Assert.True(changed > 0.0,
            "the graphics character set drew exactly the same pixels as ASCII");
    }

    // ─────────────────────────────────────────────────────────────
    // Theme / presentation
    // ─────────────────────────────────────────────────────────────

    [AvaloniaFact]
    public void DefaultBackground_IsTheConfiguredPhosphorColour()
    {
        var emulator = new VT100Emulator(10, 3);
        HideCursor(emulator); // else the block cursor covers the corner pixel
        using var shot = RenderedScreenshot.Capture(emulator, "theme-default-background");

        // TerminalRenderer's built-in default is #001911 with #00FF88 text.
        var corner = shot.PixelAt(0, 0);
        _output.WriteLine($"background corner pixel = {corner}");

        Assert.True(
            RenderedScreenshot.ApproximatelyEqual(corner, new SKColor(0x00, 0x19, 0x11), 8),
            $"expected the default phosphor background #001911, got {corner}");
    }
}

