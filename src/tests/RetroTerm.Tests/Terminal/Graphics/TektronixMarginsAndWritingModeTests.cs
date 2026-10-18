using System.Text;
using RetroTerm.Core.Terminal.Emulators.Tektronix;
using RetroTerm.Core.Terminal.Graphics;
using Xunit;

namespace RetroTerm.Tests.Terminal.Graphics;

/// <summary>
/// The last two items of the 4010/4014 chapter - two-column writing, and the raster writing modes.
/// </summary>
/// <remarks>
/// <para><b>Both come from the manual</b></para>
/// The "4010/4014 Mode" chapter of
/// <c>spec\DEC\EK-VT3XX-GP-002_VT330_VT340_Graphics_Programming_May88.pdf</c>. The margins are
/// real 4014 behaviour; the writing modes are flagged by the manual itself as "not part of the
/// 4010/4014 protocol" - they are DEC's way of standing in for a storage tube's write-through,
/// which a raster screen cannot do.
/// </remarks>
public class TektronixMarginsAndWritingModeTests
{
    private static readonly string Esc = ((char)0x1B).ToString();

    private static Tek4014Emulator Build() => new Tek4014Emulator();

    private static void Feed(Tek4014Emulator emulator, string data)
        => emulator.ProcessData(Encoding.ASCII.GetBytes(data));

    private static string RowText(Tek4014Emulator emulator, int row)
    {
        var text = new StringBuilder();
        for (int col = 0; col < emulator.Width; col++)
        {
            emulator.GetBuffer().TryGetCell(row, col, out var cell);
            text.Append(cell.Codepoint == 0 ? ' ' : (char)cell.Codepoint);
        }

        return text.ToString();
    }

    // ─────────────────────────────────────────────────────────────
    // Two-column writing
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void ATerminalStartsAtMarginOne()
    {
        var emulator = Build();

        Assert.Equal(1, emulator.ActiveMargin);
        Assert.Equal(0, emulator.MarginColumn);
    }

    [Fact]
    public void MarginTwoIsTheCentreOfTheRow()
    {
        // "Margin 1 is at the left edge of the display area. Margin 2 is at the center of each
        // row in the display area."
        var emulator = Build();

        FillTheScreen(emulator);

        Assert.Equal(2, emulator.ActiveMargin);
        Assert.Equal(emulator.Width / 2, emulator.MarginColumn);
    }

    [Fact]
    public void FillingTheLastRowWrapsToTheTopAtTheOtherMargin()
    {
        // "The terminal then wraps characters around to the top row of the display, at the new
        // margin." A storage tube cannot scroll, so this is what it does instead.
        var emulator = Build();
        FillTheScreen(emulator);

        Feed(emulator, "X");

        Assert.Equal('X', RowText(emulator, 0)[emulator.Width / 2]);
    }

    [Fact]
    public void TextAtMarginTwoRunsFromTheCentreToTheRightEdge()
    {
        // "The terminal now writes characters from the middle of the screen to the right edge,
        // overstriking any characters already displayed. As each row fills, the next character
        // wraps to the middle of the next row."
        var emulator = Build();
        FillTheScreen(emulator);

        int half = emulator.Width - emulator.Width / 2;
        Feed(emulator, new string('X', half + 1));

        // The first row's right half is Xs, and the next one lands at the centre of row 1.
        Assert.Equal('X', RowText(emulator, 0)[emulator.Width - 1]);
        Assert.Equal('X', RowText(emulator, 1)[emulator.Width / 2]);

        // The LEFT half of row 0 still holds what the first column wrote - margin 2 overstrikes
        // the right half only.
        Assert.NotEqual(' ', RowText(emulator, 0)[0]);
    }

    [Fact]
    public void ALineFeedOnTheLastRowSwitchesMarginToo()
    {
        // The manual's second trigger: "The terminal receives a line feed on the last row of the
        // display." There is nowhere to scroll to, so it starts the other column.
        var emulator = Build();
        Feed(emulator, "\x1b\x0c");                       // ESC FF - erase, margin 1, home
        Feed(emulator, new string('\n', emulator.Height));

        Assert.Equal(2, emulator.ActiveMargin);
    }

    [Fact]
    public void FillingBothColumnsComesBackToMarginOne()
    {
        // "When the last row is full, the next character wraps around to the top row at the left
        // margin. Then the process starts again."
        var emulator = Build();
        FillTheScreen(emulator);
        Assert.Equal(2, emulator.ActiveMargin);

        FillTheScreen(emulator);

        Assert.Equal(1, emulator.ActiveMargin);
    }

    [Fact]
    public void AnEraseGoesBackToMarginOne()
    {
        // "Selecting alpha mode erases the screen, moves the current position to the upper-left
        // corner, ACTIVATES MARGIN 1, and clears the bypass condition."
        var emulator = Build();
        FillTheScreen(emulator);
        Assert.Equal(2, emulator.ActiveMargin);

        Feed(emulator, Esc + "\f");

        Assert.Equal(1, emulator.ActiveMargin);
        Assert.Equal(0, emulator.MarginColumn);
    }

    /// <summary>
    /// Writes enough characters to fill every row of the screen once.
    /// </summary>
    private static void FillTheScreen(Tek4014Emulator emulator)
    {
        // From wherever the cursor is, one screenful of a character that is not a space, so the
        // rows can be told apart from blanks afterwards.
        Feed(emulator, new string('#', emulator.Width * emulator.Height));
    }

    // ─────────────────────────────────────────────────────────────
    // Raster writing modes
    // ─────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("0", GraphicsWritingMode.Overlay)]
    [InlineData("1", GraphicsWritingMode.Erase)]
    [InlineData("2", GraphicsWritingMode.Complement)]
    public void EachSequenceSelectsTheModeTheManualNames(string digit, GraphicsWritingMode expected)
    {
        var emulator = Build();

        Feed(emulator, Esc + "/" + digit + "d");

        Assert.Equal(expected, emulator.WritingMode);
        Assert.Equal(expected, emulator.Graphics!.Output.WritingMode);
    }

    [Fact]
    public void TheTrailingLetterIsNotPrinted()
    {
        // DEC writes the sequence as four bytes but that is not a conformant escape: '/' is an
        // intermediate and '0' is already a valid final, so the parser correctly ends the sequence
        // at the digit and the 'd' arrives as an ordinary character. It must not land on screen.
        var emulator = Build();

        Feed(emulator, Esc + "/0d");

        Assert.Equal(' ', RowText(emulator, 0)[0]);
    }

    [Fact]
    public void ARealLetterDAfterwardsIsStillPrinted()
    {
        // Only the very next character is swallowed, so a host that sent the sequence without its
        // trailing byte cannot lose a real 'd' later on.
        var emulator = Build();

        Feed(emulator, Esc + "/0d");
        Feed(emulator, "dog");

        Assert.StartsWith("dog", RowText(emulator, 0));
    }

    [Fact]
    public void EraseModeRubsOutInsteadOfDrawing()
    {
        var emulator = Build();
        var surface = emulator.Graphics!.Output;

        surface.DrawLine(0, 0, 20, 0, new GraphicsColor(255, 255, 255));
        Assert.False(surface.GetPixel(10, 0).IsTransparent);

        Feed(emulator, Esc + "/1d");
        surface.DrawLine(0, 0, 20, 0, new GraphicsColor(255, 255, 255));

        Assert.True(surface.GetPixel(10, 0).IsTransparent, "erase mode should have rubbed the line out");
    }

    [Fact]
    public void ComplementModeDrawnTwiceLeavesNothingBehind()
    {
        // What makes a rubber-band line possible: draw to show, draw again to take away.
        var emulator = Build();
        var surface = emulator.Graphics!.Output;
        var ink = new GraphicsColor(255, 255, 255);

        Feed(emulator, Esc + "/2d");

        surface.DrawLine(5, 5, 40, 20, ink);
        Assert.False(surface.GetPixel(5, 5).IsTransparent);

        surface.DrawLine(5, 5, 40, 20, ink);

        Assert.True(surface.GetPixel(5, 5).IsTransparent,
            "complementing the same line twice should leave the surface as it was");
    }

    [Fact]
    public void AnEraseGoesBackToOverlay()
    {
        var emulator = Build();
        Feed(emulator, Esc + "/2d");
        Assert.Equal(GraphicsWritingMode.Complement, emulator.WritingMode);

        Feed(emulator, Esc + "\f");

        Assert.Equal(GraphicsWritingMode.Overlay, emulator.WritingMode);
        Assert.Equal(GraphicsWritingMode.Overlay, emulator.Graphics!.Output.WritingMode);
    }

    [Fact]
    public void AValueTheManualDoesNotDefineChangesNothing()
    {
        var emulator = Build();
        Feed(emulator, Esc + "/2d");

        Feed(emulator, Esc + "/7d");

        Assert.Equal(GraphicsWritingMode.Complement, emulator.WritingMode);
    }
}
