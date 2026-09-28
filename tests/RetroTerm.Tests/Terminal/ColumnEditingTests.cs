using System.Text;
using RetroTerm.Core.Configuration;
using RetroTerm.Core.Terminal.Emulators;
using Xunit;

namespace RetroTerm.Tests.Terminal;

/// <summary>
/// DECIC, DECDC, SL and SR - moving whole COLUMNS of the scrolling region.
///
/// DECIC and DECDC come from chapter 8 of the VT420 Programmer Reference; SL and SR are ECMA-48,
/// written down in xterm's ctlseqs.txt. Both documents are in spec\DEC.
///
/// What separates these from ICH and DCH is that they act on EVERY ROW of the scrolling region,
/// not on the cursor's row alone. A program drawing a table uses them to open a whole column.
/// </summary>
public class ColumnEditingTests
{
    private static readonly string Esc = ((char)0x1B).ToString();

    private static TerminalEmulatorBase Build(string type = "VT420")
        => EmulatorFactory.CreateEmulator(type, 10, 4, 100);

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
        return text.ToString();
    }

    /// <summary>
    /// Fills four rows with a known pattern.
    /// </summary>
    private static TerminalEmulatorBase BuildWithGrid(string type = "VT420")
    {
        var emulator = Build(type);
        Feed(emulator, Esc + "[1;1H" + "ABCDEFGHIJ");
        Feed(emulator, Esc + "[2;1H" + "abcdefghij");
        Feed(emulator, Esc + "[3;1H" + "0123456789");
        Feed(emulator, Esc + "[4;1H" + "----------");
        return emulator;
    }

    [Fact]
    public void DeleteColumnPullsEveryRowLeft()
    {
        var emulator = BuildWithGrid();
        Feed(emulator, Esc + "[1;3H");          // cursor on column 3
        Feed(emulator, Esc + "[2'~");           // DECDC, two columns

        Assert.Equal("ABEFGHIJ..", Row(emulator, 0));
        Assert.Equal("abefghij..", Row(emulator, 1));
        Assert.Equal("01456789..", Row(emulator, 2));
        Assert.Equal("--------..", Row(emulator, 3));
    }

    [Fact]
    public void InsertColumnPushesEveryRowRight()
    {
        var emulator = BuildWithGrid();
        Feed(emulator, Esc + "[1;3H");
        Feed(emulator, Esc + "[2'}");           // DECIC, two columns

        Assert.Equal("AB..CDEFGH", Row(emulator, 0));
        Assert.Equal("ab..cdefgh", Row(emulator, 1));
        Assert.Equal("01..234567", Row(emulator, 2));
        Assert.Equal("--..------", Row(emulator, 3));
    }

    [Fact]
    public void OneColumnIsTheDefault()
    {
        var emulator = BuildWithGrid();
        Feed(emulator, Esc + "[1;1H");
        Feed(emulator, Esc + "['~");

        Assert.Equal("BCDEFGHIJ.", Row(emulator, 0));
    }

    [Fact]
    public void TheyStayInsideTheScrollingRegion()
    {
        // "DECDC has no effect outside the scrolling margins", and the region is rows here.
        var emulator = BuildWithGrid();
        Feed(emulator, Esc + "[2;3r");          // region is rows 2..3
        Feed(emulator, Esc + "[2;3H");          // cursor inside it
        Feed(emulator, Esc + "[2'~");

        Assert.Equal("ABCDEFGHIJ", Row(emulator, 0));   // untouched, outside the region
        Assert.Equal("abefghij..", Row(emulator, 1));
        Assert.Equal("01456789..", Row(emulator, 2));
        Assert.Equal("----------", Row(emulator, 3));   // untouched
    }

    [Fact]
    public void WithTheCursorOutsideTheRegionNothingHappens()
    {
        var emulator = BuildWithGrid();
        Feed(emulator, Esc + "[2;3r");
        Feed(emulator, Esc + "[1;3H");          // row 1 is above the region

        Feed(emulator, Esc + "[2'~");

        Assert.Equal("ABCDEFGHIJ", Row(emulator, 0));
        Assert.Equal("abcdefghij", Row(emulator, 1));
    }

    [Fact]
    public void DeletingMoreColumnsThanThereAreLeavesBlanks()
    {
        var emulator = BuildWithGrid();
        Feed(emulator, Esc + "[1;3H");
        Feed(emulator, Esc + "[99'~");

        Assert.Equal("AB........", Row(emulator, 0));
        Assert.Equal("ab........", Row(emulator, 1));
    }

    // ── SL and SR: the whole region, from the left margin ────────────────────────────────

    [Fact]
    public void ShiftLeftMovesTheWholeRegionAndKeepsTheCursor()
    {
        var emulator = BuildWithGrid();
        Feed(emulator, Esc + "[1;5H");          // the cursor must NOT matter
        Feed(emulator, Esc + "[2 @");           // SL by two

        Assert.Equal("CDEFGHIJ..", Row(emulator, 0));
        Assert.Equal("cdefghij..", Row(emulator, 1));
        Assert.Equal("23456789..", Row(emulator, 2));
        Assert.Equal(4, emulator.GetCursor().Column);
    }

    [Fact]
    public void ShiftRightDoesTheSameTheOtherWay()
    {
        var emulator = BuildWithGrid();
        Feed(emulator, Esc + "[1;5H");
        Feed(emulator, Esc + "[2 A");           // SR by two

        Assert.Equal("..ABCDEFGH", Row(emulator, 0));
        Assert.Equal("..abcdefgh", Row(emulator, 1));
        Assert.Equal(4, emulator.GetCursor().Column);
    }

    [Fact]
    public void ShiftingDoesNothingWithTheCursorOutsideTheRegion()
    {
        // Not something the sequence's own definition says - xterm.js's t600 fixture proves it.
        // Three SR 5 commands from three cursor positions moved the captured screen by five
        // columns, not fifteen: only the one issued from inside the region did anything.
        var emulator = BuildWithGrid();
        Feed(emulator, Esc + "[2;3r");          // region rows 2..3
        Feed(emulator, Esc + "[1;1H");          // cursor above it

        Feed(emulator, Esc + "[2 @");

        Assert.Equal("ABCDEFGHIJ", Row(emulator, 0));
        Assert.Equal("abcdefghij", Row(emulator, 1));
        Assert.Equal("0123456789", Row(emulator, 2));
    }

    [Fact]
    public void ShiftingRespectsTheScrollingRegion()
    {
        var emulator = BuildWithGrid();
        Feed(emulator, Esc + "[2;3r");
        Feed(emulator, Esc + "[2;1H");          // inside the region, so it acts
        Feed(emulator, Esc + "[1 @");

        Assert.Equal("ABCDEFGHIJ", Row(emulator, 0));
        Assert.Equal("bcdefghij.", Row(emulator, 1));
        Assert.Equal("123456789.", Row(emulator, 2));
        Assert.Equal("----------", Row(emulator, 3));
    }

    [Fact]
    public void ShiftingIsNotADecExtensionAndWorksOnAPlainAnsiTerminal()
    {
        // SL and SR are ECMA-48. Gating them behind a DEC capability would be wrong.
        var emulator = BuildWithGrid("ANSI");

        Feed(emulator, Esc + "[2 @");

        Assert.Equal("CDEFGHIJ..", Row(emulator, 0));
    }

    [Fact]
    public void ATerminalWithoutColumnEditingIgnoresDecicAndDecdc()
    {
        // "Available in: VT400 mode only" - a VT220 must leave the screen alone.
        var emulator = BuildWithGrid("VT220");

        Feed(emulator, Esc + "[1;3H" + Esc + "[2'~");

        Assert.Equal("ABCDEFGHIJ", Row(emulator, 0));
    }

    [Fact]
    public void AndLevelOneIgnoresThemToo()
    {
        // Both are on the manual's Table 4-1 of what a VT100-level terminal ignores.
        var emulator = BuildWithGrid();
        Feed(emulator, Esc + "[61\"p");         // DECSCL level 1 - this also clears the screen
        Feed(emulator, Esc + "[1;1H" + "ABCDEFGHIJ");

        Feed(emulator, Esc + "[1;3H" + Esc + "[2'~");

        Assert.Equal("ABCDEFGHIJ", Row(emulator, 0));
    }
}
