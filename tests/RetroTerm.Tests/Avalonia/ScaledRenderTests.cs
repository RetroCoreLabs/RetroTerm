using System.Text;
using Avalonia.Headless.XUnit;
using RetroTerm.Core.Terminal.Emulators;
using Xunit;

namespace RetroTerm.Tests.Avalonia;

/// <summary>
/// Drawing at a magnification other than 1.0 — the case that is nearly always what is on screen,
/// and had no test at all.
///
/// The terminal is scaled to fit its window, so the scale is only exactly 1.0 when the window
/// happens to be precisely the cell grid's natural size. Every existing screenshot test arranges
/// the canvas at exactly that size on purpose, because per-cell glyph assertions need it — so the
/// scaled path, the one users actually look at, was never covered.
///
/// It matters now because the scale moved out of <c>TerminalCanvas</c> and into
/// <c>TerminalRenderer</c>. The renderer has to know the device resolution it is really drawing
/// at: a screen cached at natural size and magnified on the way out would be a blurred copy of
/// glyphs that are currently rasterised at their final size, and nothing would have caught it.
/// </summary>
[Collection("Avalonia")]
public class ScaledRenderTests
{
    private static void Feed(TerminalEmulatorBase emulator, string text)
        => emulator.ProcessData(Encoding.ASCII.GetBytes(text));

    /// <summary>
    /// Text in row 0 and a cursor parked well away from it.
    /// </summary>
    private static VT100Emulator BuildScreen()
    {
        var emulator = new VT100Emulator(20, 4);
        Feed(emulator, "MMMM");
        Feed(emulator, "\u001b[4;20H");
        return emulator;
    }

    [AvaloniaFact]
    public void AMagnifiedScreenIsStillDrawnCellByCell()
    {
        // The scale reaches the renderer and the grid still lines up: the same cells carry ink and
        // the same cells stay empty, just bigger. If the scale were dropped on the way through,
        // the picture would occupy a quarter of the bitmap and cell 3 would be blank.
        using var shot = RenderedScreenshot.Capture(BuildScreen(), "scaled-2x", sizeMultiplier: 2);

        Assert.True(shot.CellHasInk(0, 0), "first character must be drawn");
        Assert.True(shot.CellHasInk(0, 3), "fourth character must be drawn");
        Assert.False(shot.CellHasInk(0, 5), "nothing was written past the fourth column");
        Assert.False(shot.CellHasInk(2, 0), "nothing was written on row 2");
    }

    [AvaloniaFact]
    public void MagnifyingFillsTheWholeBitmap()
    {
        // The guard against the scale silently becoming 1.0: at 2x the bitmap is twice the size in
        // each direction, and the drawing has to fill it rather than sitting in the top-left
        // quarter with three quarters of letterbox around it.
        using var natural = RenderedScreenshot.Capture(BuildScreen(), null);
        using var magnified = RenderedScreenshot.Capture(BuildScreen(), null, sizeMultiplier: 2);

        // Within a pixel, not exactly double: the system font's cell width is fractional, so
        // rounding twenty cells and doubling is not the same as doubling and then rounding. That
        // is arithmetic, not a rendering question.
        Assert.InRange(magnified.Width, natural.Width * 2 - 2, natural.Width * 2 + 2);
        Assert.InRange(magnified.Height, natural.Height * 2 - 2, natural.Height * 2 + 2);

        // A cell near the far corner must still be terminal background, not letterbox. Comparing
        // against the natural-size render's background is what makes this a real check.
        var naturalBackground = natural.DominantColorInCell(3, 18);
        var magnifiedBackground = magnified.DominantColorInCell(3, 18);
        Assert.True(
            RenderedScreenshot.ApproximatelyEqual(naturalBackground, magnifiedBackground, 8),
            "the magnified drawing must reach the far corner, not leave letterbox there");
    }

    [AvaloniaFact]
    public void AMagnifiedGlyphCarriesMoreInk()
    {
        // Scaled by two in each direction, a glyph covers about four times the pixels. This is the
        // assertion that would fail if the renderer drew at natural size into a magnified bitmap.
        using var natural = RenderedScreenshot.Capture(BuildScreen(), null);
        using var magnified = RenderedScreenshot.Capture(BuildScreen(), "scaled-ink", sizeMultiplier: 2);

        int naturalInk = natural.InkPixelsInCell(0, 0);
        int magnifiedInk = magnified.InkPixelsInCell(0, 0);

        Assert.True(naturalInk > 0, "the control glyph must have drawn something");
        Assert.True(magnifiedInk > naturalInk * 5 / 2,
            $"a 2x glyph should carry roughly four times the ink; natural={naturalInk} magnified={magnifiedInk}");
    }
}
