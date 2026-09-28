using System.Text;
using RetroTerm.Core.Configuration;
using RetroTerm.Core.Terminal.Emulators;
using Xunit;

namespace RetroTerm.Tests.Terminal;

/// <summary>
/// DECSTBM asked for regions it cannot have.
/// </summary>
/// <remarks>
/// The VT420 Programmer Reference gives three rules beside the format: "the value of the top margin
/// (Pt) must be less than the bottom margin (Pb)", "the maximum size of the scrolling region is the
/// page size", and "DECSTBM moves the cursor to column 1, line 1 of the page".
///
/// The first rule is the one that had teeth. A request breaking it used to fall back to the full
/// screen and home the cursor, which turned a REJECTED region into a working one that happened to
/// select everything - so a program that probed with a bad region found its next scroll running
/// over the whole screen instead of over the region it had set earlier.
/// </remarks>
public class ScrollRegionEdgeCaseTests
{
    private static readonly string Esc = ((char)0x1B).ToString();

    private static TerminalEmulatorBase Build()
        => EmulatorFactory.CreateEmulator("XTERM", 10, 10, 100);

    private static void Feed(TerminalEmulatorBase emulator, string text)
        => emulator.ProcessData(Encoding.ASCII.GetBytes(text));

    private static string Row(TerminalEmulatorBase emulator, int row)
    {
        var text = new StringBuilder(emulator.Width);
        for (int col = 0; col < emulator.Width; col++)
        {
            emulator.GetBuffer().TryGetCell(row, col, out var cell);
            text.Append(cell.Codepoint == 0 ? '.' : (char)cell.Codepoint);
        }
        return text.ToString().TrimEnd('.');
    }

    /// <summary>
    /// Writes a digit on every row so a scroll is visible.
    /// </summary>
    /// <returns>An emulator showing 0 to 9, one per row.</returns>
    private static TerminalEmulatorBase BuildWithRows()
    {
        var emulator = Build();
        for (int row = 1; row <= 10; row++)
        {
            Feed(emulator, Esc + "[" + row + ";1H" + (row - 1).ToString());
        }
        return emulator;
    }

    [Fact]
    public void ATopEqualToTheBottomIsRefused()
    {
        // THE defect: refused, not reset. A one-line region is impossible, and the region that was
        // already in force stays in force.
        var emulator = BuildWithRows();
        Feed(emulator, Esc + "[3;5r");          // a real region first
        Feed(emulator, Esc + "[6;6H");

        Feed(emulator, Esc + "[4;4r");          // impossible - must change nothing

        Assert.Equal(5, emulator.GetCursor().Row);      // the cursor did not go home either
        Assert.Equal(5, emulator.GetCursor().Column);
    }

    [Fact]
    public void AndSoIsATopBelowTheBottom()
    {
        var emulator = BuildWithRows();
        Feed(emulator, Esc + "[3;5r");
        Feed(emulator, Esc + "[6;6H");

        Feed(emulator, Esc + "[8;2r");

        Assert.Equal(5, emulator.GetCursor().Row);
        Assert.Equal(5, emulator.GetCursor().Column);
    }

    [Fact]
    public void ARefusedRegionLeavesTheOneAlreadySetDoingItsWork()
    {
        // The consequence that matters: what the next scroll does.
        var emulator = BuildWithRows();
        Feed(emulator, Esc + "[1;3r");          // scrolling confined to the first three rows

        Feed(emulator, Esc + "[7;7r");          // top equals bottom - refused
        Feed(emulator, Esc + "[9;4r");          // top below bottom - refused
        Feed(emulator, Esc + "[S");

        // Rows 4 onwards must be untouched, because the region is still rows 1 to 3.
        Assert.Equal("3", Row(emulator, 3));
        Assert.Equal("9", Row(emulator, 9));
    }

    [Fact]
    public void AZeroMeansTheDefaultAtThatEnd()
    {
        // "Default: Pt = 1" and "Default: Pb = current number of lines per screen".
        var emulator = BuildWithRows();

        Feed(emulator, Esc + "[5;0r");          // bottom 0 - the last line
        Feed(emulator, Esc + "[10;1H");
        Feed(emulator, Esc + "[S");

        Assert.Equal("0", Row(emulator, 0));    // above the region, untouched
        Assert.Equal("3", Row(emulator, 3));
        Assert.Equal("5", Row(emulator, 4));    // inside it, scrolled
    }

    [Fact]
    public void AMissingTopMeansTheFirstLine()
    {
        var emulator = BuildWithRows();

        Feed(emulator, Esc + "[;4r");
        Feed(emulator, Esc + "[S");

        Assert.Equal("1", Row(emulator, 0));    // rows 1 to 4 scrolled
        Assert.Equal("4", Row(emulator, 4));    // row 5 untouched
    }

    [Fact]
    public void ABottomBeyondTheScreenIsClampedRatherThanRefused()
    {
        // "The maximum size of the scrolling region is the page size."
        var emulator = BuildWithRows();

        Feed(emulator, Esc + "[8;99r");
        Feed(emulator, Esc + "[10;1H");
        Feed(emulator, Esc + "[S");

        Assert.Equal("0", Row(emulator, 0));
        Assert.Equal("8", Row(emulator, 7));    // rows 8 to 10 scrolled
    }

    [Fact]
    public void AnExtraParameterIsIgnored()
    {
        var emulator = BuildWithRows();

        Feed(emulator, Esc + "[6;7;8r");
        Feed(emulator, Esc + "[S");

        Assert.Equal("6", Row(emulator, 5));    // the region is 6 to 7, so row 6 took row 7's text
        Assert.Equal("", Row(emulator, 6));
        Assert.Equal("7", Row(emulator, 7));    // row 8 untouched
    }

    [Fact]
    public void AValidRegionStillHomesTheCursor()
    {
        // "DECSTBM moves the cursor to column 1, line 1 of the page."
        var emulator = BuildWithRows();
        Feed(emulator, Esc + "[6;6H");

        Feed(emulator, Esc + "[3;7r");

        Assert.Equal(0, emulator.GetCursor().Row);
        Assert.Equal(0, emulator.GetCursor().Column);
    }

    [Fact]
    public void NoParametersAtAllReturnsToTheWholeScreen()
    {
        var emulator = BuildWithRows();
        Feed(emulator, Esc + "[3;5r");

        Feed(emulator, Esc + "[r");
        Feed(emulator, Esc + "[10;1H");
        Feed(emulator, Esc + "[S");

        Assert.Equal("1", Row(emulator, 0));    // the whole screen scrolled
    }

    // ── Above the region is outside it too ──────────────────────────────────────────────────

    [Fact]
    public void InsertLineAboveTheRegionDoesNothing()
    {
        // "IL has no effect outside the page margins" - chapter 8. The buffer's own guard only
        // refused a row past the BOTTOM of the region, so IL with the cursor ABOVE one shifted the
        // rows outside it downwards and dragged them in.
        var emulator = BuildWithRows();
        Feed(emulator, Esc + "[3;5r");          // region is rows 2..4
        Feed(emulator, Esc + "[1;1H");          // row 0, above it

        Feed(emulator, Esc + "[L");

        for (int row = 0; row < 10; row++)
        {
            Assert.Equal(row.ToString(), Row(emulator, row));
        }
    }

    [Fact]
    public void AndNeitherDoesDeleteLine()
    {
        // "DL has no effect outside the scrolling margins."
        var emulator = BuildWithRows();
        Feed(emulator, Esc + "[3;5r");
        Feed(emulator, Esc + "[1;1H");

        Feed(emulator, Esc + "[M");

        for (int row = 0; row < 10; row++)
        {
            Assert.Equal(row.ToString(), Row(emulator, row));
        }
    }

    [Fact]
    public void ButInsideTheRegionBothStillWork()
    {
        // The guard against fixing it by refusing everything.
        var emulator = BuildWithRows();
        Feed(emulator, Esc + "[3;5r");
        Feed(emulator, Esc + "[3;1H");          // row 2, the top of the region

        Feed(emulator, Esc + "[L");

        Assert.Equal("", Row(emulator, 2));     // a blank line came in
        Assert.Equal("2", Row(emulator, 3));    // and the region's rows moved down
        Assert.Equal("3", Row(emulator, 4));
        Assert.Equal("5", Row(emulator, 5));    // outside the region, untouched
    }
}
