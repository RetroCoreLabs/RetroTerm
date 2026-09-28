using System.Text;
using Avalonia.Headless.XUnit;
using RetroTerm.Core.Terminal.Buffer;
using RetroTerm.Core.Terminal.Emulators;
using Xunit;

namespace RetroTerm.Tests.Avalonia;

/// <summary>
/// Double-size lines actually being DRAWN double size.
///
/// WHAT WAS WRONG. The emulator has stored the line-size bits correctly for a while —
/// <c>ESC # 6</c> (DECDWL) marks the row double width, <c>ESC # 3</c> / <c>ESC # 4</c> (DECDHL)
/// mark its top and bottom halves — and the buffer even refuses to let the cursor past the
/// halfway column of such a line. TerminalRenderer read NONE of it. A host sending those
/// sequences changed the buffer and produced nothing visible at all: the banner it wanted twice
/// the size came out ordinary, and on a DECDHL pair it came out printed twice.
///
/// These assertions run on real pixels through the production canvas and renderer, because
/// "is the glyph twice as wide" is a question about what was drawn, not about what a flag says.
///
/// NOT COVERED, deliberately: that the renderer stops at the halfway column of a double-width
/// line. The columns past it would be drawn beyond the right edge of the canvas, so skipping
/// them saves work but changes no pixel — there is nothing an image can assert. It is a
/// correctness point about the model, and the model's own tests own it
/// (DoubleWidthLineLayoutTests).
/// </summary>
[Collection("Avalonia")]
public class DoubleSizeRenderingTests
{
    private static void Feed(TerminalEmulatorBase emulator, string text)
        => emulator.ProcessData(Encoding.ASCII.GetBytes(text));

    /// <summary>
    /// Parks the cursor on row 3, out of the way of everything these tests look at.
    ///
    /// Not optional: the cursor is drawn as a filled block, so leaving it where the text ended
    /// puts a cell's worth of ink in the cell AFTER the character and every "did it spill into
    /// the next cell" assertion measures the cursor instead of the glyph.
    /// </summary>
    private static void ParkCursor(TerminalEmulatorBase emulator) => Feed(emulator, "\u001b[3;1H");

    /// <summary>
    /// Counts lit pixels across a block of screen cells, measured against the screen's own
    /// background colour taken from a cell nothing ever writes to.
    ///
    /// <c>InkPixelsInCell</c> cannot be used for the size comparisons below. It takes each
    /// cell's MOST COMMON colour as the background, and half of a double-size glyph covers so
    /// much of its cell that the glyph itself becomes the most common colour — so the count
    /// inverts and a bigger character measures as LESS ink. Reading the background once, from a
    /// known-empty cell, avoids that completely.
    /// </summary>
    private static int InkInCells(RenderedScreenshot shot, int row, int col, int rowSpan, int colSpan)
    {
        var background = shot.DominantColorInCell(3, 19);   // bottom-right cell; always empty here

        int x0 = (int)(col * shot.CellWidth);
        int y0 = (int)(row * shot.CellHeight);
        int x1 = System.Math.Min(shot.Width, (int)((col + colSpan) * shot.CellWidth));
        int y1 = System.Math.Min(shot.Height, (int)((row + rowSpan) * shot.CellHeight));

        int count = 0;
        for (int y = y0; y < y1; y++)
        {
            for (int x = x0; x < x1; x++)
            {
                if (!RenderedScreenshot.ApproximatelyEqual(shot.PixelAt(x, y), background, 24))
                {
                    count++;
                }
            }
        }
        return count;
    }

    /// <summary>
    /// Ink in the two screen cells a single double-width character covers.
    /// </summary>
    private static int InkOverPair(RenderedScreenshot shot, int row, int leftCol)
        => InkInCells(shot, row, leftCol, 1, 2);

    // ─────────────────────────────────────────────────────────────
    // The control — an ordinary line still draws one cell per character
    // ─────────────────────────────────────────────────────────────

    [AvaloniaFact]
    public void AnOrdinaryLineDrawsOneCellPerCharacter()
    {
        // The guard against overreach: if this ever fails, the scaling leaked onto normal text.
        var emulator = new VT100Emulator(20, 4);
        Feed(emulator, "X");
        ParkCursor(emulator);

        using var shot = RenderedScreenshot.Capture(emulator, "dsize-normal-line");

        Assert.True(shot.CellHasInk(0, 0), "the character must be drawn");
        Assert.False(shot.CellHasInk(0, 1), "an ordinary character must not spill into the next cell");
    }

    // ─────────────────────────────────────────────────────────────
    // DECDWL — ESC # 6
    // ─────────────────────────────────────────────────────────────

    [AvaloniaFact]
    public void ADoubleWidthCharacterCoversTwoScreenCells()
    {
        // THE test. Before the fix this drew exactly like the control above.
        var emulator = new VT100Emulator(20, 4);
        Feed(emulator, "\u001b#6X");
        ParkCursor(emulator);

        using var shot = RenderedScreenshot.Capture(emulator, "dsize-decdwl");

        Assert.True(shot.CellHasInk(0, 0), "the left half of the doubled glyph must be drawn");
        Assert.True(shot.CellHasInk(0, 1), "the right half of the doubled glyph must be drawn");
    }

    [AvaloniaFact]
    public void ADoubleWidthGlyphPutsDownAboutTwiceTheInk()
    {
        // Covering two cells is not enough on its own — two ordinary glyphs side by side would
        // do that too. Doubling the width doubles each ink pixel horizontally and nothing else,
        // so the total lands near 2x, and well below the 4x that a full 2x2 scale would give.
        var normalEmulator = new VT100Emulator(20, 4);
        Feed(normalEmulator, "M");
        ParkCursor(normalEmulator);
        using var normalShot = RenderedScreenshot.Capture(normalEmulator, null);
        int normalInk = InkInCells(normalShot, 0, 0, 1, 1);

        var wideEmulator = new VT100Emulator(20, 4);
        Feed(wideEmulator, "\u001b#6M");
        ParkCursor(wideEmulator);
        using var wideShot = RenderedScreenshot.Capture(wideEmulator, "dsize-decdwl-ink");
        int wideInk = InkOverPair(wideShot, 0, 0);

        Assert.True(normalInk > 0, "the control glyph must have drawn something");
        Assert.True(wideInk > normalInk * 3 / 2,
            $"a double-width glyph should carry roughly twice the ink; normal={normalInk} wide={wideInk}");
        Assert.True(wideInk < normalInk * 3,
            $"double WIDTH must not scale the height as well; normal={normalInk} wide={wideInk}");
    }

    [AvaloniaFact]
    public void TheSecondCharacterOfADoubleWidthLineStartsTwoCellsIn()
    {
        // Buffer column 1 lands at screen column 2. Getting the width right but leaving the
        // ORIGIN at col * charWidth would overlap every character with its neighbour.
        var emulator = new VT100Emulator(20, 4);
        Feed(emulator, "\u001b#6\u001b[2GX");
        ParkCursor(emulator);

        using var shot = RenderedScreenshot.Capture(emulator, "dsize-decdwl-column");

        Assert.False(shot.CellHasInk(0, 0), "nothing was written in buffer column 0");
        Assert.False(shot.CellHasInk(0, 1), "nothing was written in buffer column 0");
        Assert.True(InkOverPair(shot, 0, 2) > 0, "buffer column 1 must be drawn at screen column 2");
    }

    // ─────────────────────────────────────────────────────────────
    // DECDHL — ESC # 3 over ESC # 4
    // ─────────────────────────────────────────────────────────────

    [AvaloniaFact]
    public void ADoubleHeightPairDrawsTheTopHalfAboveTheBottomHalf()
    {
        // A DECDHL banner is the SAME text sent twice, with the top row marked ESC # 3 and the
        // row below it ESC # 4. Each row shows one half of a 2x2 glyph.
        //
        // 'T' is chosen because its halves are unmistakably different: a heavy bar across the
        // top, a thin stem below. If the two rows drew the same thing — which is exactly what
        // happened before the fix — these counts would match.
        var emulator = new VT100Emulator(20, 4);
        Feed(emulator, "\u001b#3T\r\n\u001b#4T");
        ParkCursor(emulator);

        using var shot = RenderedScreenshot.Capture(emulator, "dsize-decdhl");

        int topInk = InkOverPair(shot, 0, 0);
        int bottomInk = InkOverPair(shot, 1, 0);

        Assert.True(topInk > 0, "the top half must be drawn");
        Assert.True(bottomInk > 0, "the bottom half must be drawn");
        Assert.True(topInk > bottomInk,
            $"the top half of a 'T' carries its bar and must hold more ink than the stem below; top={topInk} bottom={bottomInk}");
    }

    [AvaloniaFact]
    public void ADoubleHeightPairCarriesAboutFourTimesTheInk()
    {
        // Scaled in both directions, the two halves together hold roughly 4x an ordinary glyph.
        // This is what separates a real 2x2 scale from drawing the glyph twice at double width.
        var normalEmulator = new VT100Emulator(20, 4);
        Feed(normalEmulator, "M");
        ParkCursor(normalEmulator);
        using var normalShot = RenderedScreenshot.Capture(normalEmulator, null);
        int normalInk = InkInCells(normalShot, 0, 0, 1, 1);

        var tallEmulator = new VT100Emulator(20, 4);
        Feed(tallEmulator, "\u001b#3M\r\n\u001b#4M");
        ParkCursor(tallEmulator);
        using var tallShot = RenderedScreenshot.Capture(tallEmulator, "dsize-decdhl-ink");
        int tallInk = InkOverPair(tallShot, 0, 0) + InkOverPair(tallShot, 1, 0);

        Assert.True(normalInk > 0, "the control glyph must have drawn something");
        Assert.True(tallInk > normalInk * 5 / 2,
            $"a double-height pair should carry roughly four times the ink; normal={normalInk} tall={tallInk}");
    }

    [AvaloniaFact]
    public void TheHalvesOfADoubleHeightPairAreNotTheSamePicture()
    {
        // The seam check, stated as a flag rather than as pixels: the top row's cells must be
        // marked as the top half and the bottom row's as the bottom half, or the renderer has
        // no way to know which half to clip to. This is the emulator side of the same feature
        // and it is what the renderer reads.
        var emulator = new VT100Emulator(20, 4);
        Feed(emulator, "\u001b#3T\r\n\u001b#4T");

        Assert.True(emulator.GetBuffer().TryGetCell(0, 0, out var top));
        Assert.True(emulator.GetBuffer().TryGetCell(1, 0, out var bottom));

        Assert.True(top.Attributes.HasAttribute(CharacterAttributes.DoubleHeightTop));
        Assert.True(bottom.Attributes.HasAttribute(CharacterAttributes.DoubleHeightBottom));
        Assert.True(top.DoubleWidth, "DECDHL doubles the width as well as the height");
        Assert.True(bottom.DoubleWidth, "DECDHL doubles the width as well as the height");
    }
}
