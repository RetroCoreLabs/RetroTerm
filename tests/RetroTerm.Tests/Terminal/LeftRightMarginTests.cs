using System.Text;
using RetroTerm.Core.Configuration;
using RetroTerm.Core.Terminal.Emulators;
using Xunit;

namespace RetroTerm.Tests.Terminal;

/// <summary>
/// Left and right margins — DECLRMM and DECSLRM, the VT400 series' one real addition here.
///
/// The subtlety worth the most care is that <c>CSI s</c> is TWO COMMANDS. With margins enabled it
/// is DECSLRM, set margins; with the mode off it is SCOSC, save cursor. One final byte, told apart
/// only by the mode — so a terminal that accepted DECSLRM unconditionally would stop being able to
/// save the cursor.
/// </summary>
public class LeftRightMarginTests
{
    private static readonly string Esc = ((char)0x1B).ToString();

    private static TerminalEmulatorBase Build(string type = "VT420")
        => EmulatorFactory.CreateEmulator(type, 20, 6, 100);

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

    [Fact]
    public void WithoutTheModeCsiSStillSavesTheCursor()
    {
        // The guard that matters most: this behaviour is older and far more widely used than
        // margins, and breaking it would be a poor trade for a VT420 feature.
        var emulator = Build();
        Feed(emulator, Esc + "[3;7H");

        Feed(emulator, Esc + "[s");          // SCOSC
        Feed(emulator, Esc + "[1;1H");
        Feed(emulator, Esc + "[u");          // SCORC

        Assert.Equal(2, emulator.GetCursor().Row);
        Assert.Equal(6, emulator.GetCursor().Column);
    }

    [Fact]
    public void WithTheModeOnTheSameSequenceSetsMargins()
    {
        var emulator = Build();

        Feed(emulator, Esc + "[?69h");
        Feed(emulator, Esc + "[5;15s");

        Assert.True(emulator.LeftRightMarginMode);
        Assert.Equal(4, emulator.LeftMargin);
        Assert.Equal(14, emulator.RightMargin);
    }

    [Fact]
    public void SettingMarginsHomesTheCursorToColumnOneOfThePage()
    {
        // "DECSLRM moves the cursor to column 1, line 1 of the page" - the VT420 Programmer
        // Reference, Notes on DECSLRM. OF THE PAGE, so with origin mode off it lands OUTSIDE the
        // region it just set. This test used to assert column 4, the new left margin, which was
        // written from the shape of DECSTBM rather than from the manual.
        var emulator = Build();
        Feed(emulator, Esc + "[4;18H");

        Feed(emulator, Esc + "[?69h" + Esc + "[5;15s");

        Assert.Equal(0, emulator.GetCursor().Row);
        Assert.Equal(0, emulator.GetCursor().Column);
    }

    [Fact]
    public void ButUnderOriginModeColumnOneIsTheLeftMargin()
    {
        // The other half of the same rule. DECOM: "the home cursor position is at the upper-left
        // corner of the screen, within the margins."
        var emulator = Build();
        Feed(emulator, Esc + "[?69h");
        Feed(emulator, Esc + "[2;5r");                  // a top margin to show as well
        Feed(emulator, Esc + "[?6h");

        Feed(emulator, Esc + "[5;15s");

        Assert.Equal(1, emulator.GetCursor().Row);
        Assert.Equal(4, emulator.GetCursor().Column);
    }

    [Fact]
    public void AndCursorAddressingCountsColumnsFromTheLeftMarginToo()
    {
        // CUP, in the manual's own words: "The starting point for lines and columns depends on the
        // setting of origin mode (DECOM)." Columns, not just lines - which is the half that was
        // missing. Column 3 of a region starting at column 5 is column 7 of the page.
        var emulator = Build();
        Feed(emulator, Esc + "[?69h" + Esc + "[5;15s");
        Feed(emulator, Esc + "[?6h");

        Feed(emulator, Esc + "[1;3H");

        Assert.Equal(6, emulator.GetCursor().Column);
    }

    [Fact]
    public void AndItCannotBeAddressedPastTheRightMargin()
    {
        // "The cursor cannot move outside of the margins" - DECOM.
        var emulator = Build();
        Feed(emulator, Esc + "[?69h" + Esc + "[5;15s");
        Feed(emulator, Esc + "[?6h");

        Feed(emulator, Esc + "[1;99H");

        Assert.Equal(14, emulator.GetCursor().Column);   // the right margin, column 15
    }

    [Fact]
    public void WithOriginModeOffTheColumnIsStillCountedFromTheScreen()
    {
        // The guard the other way. Without DECOM the margins do not move the origin at all, so a
        // host addressing column 3 means the third column of the page.
        var emulator = Build();
        Feed(emulator, Esc + "[?69h" + Esc + "[5;15s");

        Feed(emulator, Esc + "[1;3H");

        Assert.Equal(2, emulator.GetCursor().Column);
    }

    [Fact]
    public void TextWrapsAtTheRightMarginBackToTheLeftMargin()
    {
        // THE test. Wrapping to column 0 instead would spill every wrapped line out of the region
        // it belongs to, which is the whole point of having one.
        var emulator = Build();
        Feed(emulator, Esc + "[?69h" + Esc + "[5;10s");
        Feed(emulator, Esc + "[1;5H");       // into the region: DECSLRM homes to the PAGE corner

        Feed(emulator, "ABCDEFGH");

        // Six columns of region, 4 through 9: ABCDEF on the first row, GH on the next, both
        // starting at column 4.
        Assert.Equal("....ABCDEF..........", Row(emulator, 0));
        Assert.Equal("....GH..............", Row(emulator, 1));
    }

    [Fact]
    public void TurningTheModeOffGivesTheWholeWidthBack()
    {
        var emulator = Build();
        Feed(emulator, Esc + "[?69h" + Esc + "[5;10s");

        Feed(emulator, Esc + "[?69l");
        Feed(emulator, Esc + "[1;1H" + "ABCDEFGH");

        Assert.False(emulator.LeftRightMarginMode);
        Assert.Equal("ABCDEFGH............", Row(emulator, 0));
    }

    [Fact]
    public void AMarginPastTheRightEdgeIsBroughtInside()
    {
        var emulator = Build();

        Feed(emulator, Esc + "[?69h" + Esc + "[5;99s");

        Assert.Equal(19, emulator.RightMargin);
    }

    [Fact]
    public void AMeaninglessPairIsRefusedRatherThanSwapped()
    {
        // The host asked for something that is not a region. Guessing at its intent would put text
        // somewhere it did not choose.
        var emulator = Build();
        Feed(emulator, Esc + "[?69h" + Esc + "[5;10s");

        Feed(emulator, Esc + "[15;3s");

        Assert.Equal(4, emulator.LeftMargin);
        Assert.Equal(9, emulator.RightMargin);
    }

    [Fact]
    public void ABareSequenceClearsTheMarginsWithoutLeavingTheMode()
    {
        // Both parameters default to the edges, so this is how a host says "the whole width" while
        // keeping the mode on.
        var emulator = Build();
        Feed(emulator, Esc + "[?69h" + Esc + "[5;10s");

        Feed(emulator, Esc + "[s");

        Assert.True(emulator.LeftRightMarginMode);
        Assert.Equal(0, emulator.LeftMargin);
        Assert.Equal(19, emulator.RightMargin);
    }

    [Fact]
    public void AResizeTakesTheMarginsAway()
    {
        // A right margin of 15 on a screen that is now 10 wide would confine text to a region that
        // no longer exists.
        var emulator = Build();
        Feed(emulator, Esc + "[?69h" + Esc + "[5;15s");

        emulator.Resize(10, 6);

        Assert.Equal(0, emulator.LeftMargin);
        Assert.Equal(9, emulator.RightMargin);
    }

    [Fact]
    public void AHardResetTurnsTheModeOff()
    {
        var emulator = Build();
        Feed(emulator, Esc + "[?69h" + Esc + "[5;15s");

        emulator.Reset();

        Assert.False(emulator.LeftRightMarginMode);
    }

    [Fact]
    public void ATerminalWithoutTheCapabilityKeepsSaveCursor()
    {
        // A VT100 has no margins. The mode does nothing, and CSI s stays SCOSC - which is exactly
        // why the capability gates the mode rather than the sequence.
        var emulator = Build("VT100");
        Feed(emulator, Esc + "[3;7H");

        Feed(emulator, Esc + "[?69h");
        Feed(emulator, Esc + "[s");
        Feed(emulator, Esc + "[1;1H");
        Feed(emulator, Esc + "[u");

        Assert.False(emulator.LeftRightMarginMode);
        Assert.Equal(2, emulator.GetCursor().Row);
        Assert.Equal(6, emulator.GetCursor().Column);
    }

    [Fact]
    public void DecrqmAnswersForTheMode()
    {
        var emulator = Build();
        var replies = new StringBuilder();
        emulator.DataToSend += bytes => replies.Append(Encoding.ASCII.GetString(bytes));

        Feed(emulator, Esc + "[?69$p");
        Feed(emulator, Esc + "[?69h");
        Feed(emulator, Esc + "[?69$p");

        Assert.Equal(Esc + "[?69;2$y" + Esc + "[?69;1$y", replies.ToString());
    }

    [Fact]
    public void InsertCharacterStopsAtTheRightMargin()
    {
        // What was pushed off the region falls away rather than spilling into the columns beside
        // it. The 'Z' outside the margin must not move.
        var emulator = Build();
        Feed(emulator, Esc + "[1;1H" + "....ABCDEF....Z");
        Feed(emulator, Esc + "[?69h" + Esc + "[5;10s");

        Feed(emulator, Esc + "[1;5H" + Esc + "[2@");

        Assert.Equal("......ABCD....Z.....", Row(emulator, 0));
    }

    [Fact]
    public void DeleteCharacterPullsInBlanksFromTheRightMargin()
    {
        // And not from the right edge - text outside the region is not dragged into it.
        var emulator = Build();
        Feed(emulator, Esc + "[1;1H" + "....ABCDEF....Z");
        Feed(emulator, Esc + "[?69h" + Esc + "[5;10s");

        Feed(emulator, Esc + "[1;5H" + Esc + "[2P");

        Assert.Equal("....CDEF......Z.....", Row(emulator, 0));
    }

    [Fact]
    public void EditingOutsideTheRegionDoesNothing()
    {
        var emulator = Build();
        Feed(emulator, Esc + "[1;1H" + "ABCDEFGHIJKLMNOPQRST");
        Feed(emulator, Esc + "[?69h" + Esc + "[5;10s");

        Feed(emulator, Esc + "[1;1H" + Esc + "[3@");

        Assert.Equal("ABCDEFGHIJKLMNOPQRST", Row(emulator, 0));
    }

    [Fact]
    public void InsertLineMovesOnlyTheColumnsInsideTheRegion()
    {
        // THE reason a program sets margins: it is drawing a panel and does not want the rest of
        // the screen following it around.
        var emulator = Build();
        Feed(emulator, Esc + "[1;1H" + "AAAAAAAAAAAAAAAAAAAA");
        Feed(emulator, Esc + "[2;1H" + "BBBBBBBBBBBBBBBBBBBB");
        Feed(emulator, Esc + "[?69h" + Esc + "[5;10s");

        Feed(emulator, Esc + "[1;5H" + Esc + "[L");

        // Inside columns 4..9 the A row moved down and left blanks; outside it, nothing moved.
        Assert.Equal("AAAA......AAAAAAAAAA", Row(emulator, 0));
        Assert.Equal("BBBBAAAAAABBBBBBBBBB", Row(emulator, 1));
    }

    [Fact]
    public void DeleteLineAlsoMovesOnlyTheRegion()
    {
        var emulator = Build();
        Feed(emulator, Esc + "[1;1H" + "AAAAAAAAAAAAAAAAAAAA");
        Feed(emulator, Esc + "[2;1H" + "BBBBBBBBBBBBBBBBBBBB");
        Feed(emulator, Esc + "[?69h" + Esc + "[5;10s");

        Feed(emulator, Esc + "[1;5H" + Esc + "[M");

        Assert.Equal("AAAABBBBBBAAAAAAAAAA", Row(emulator, 0));
        Assert.Equal("BBBB......BBBBBBBBBB", Row(emulator, 1));
    }

    [Fact]
    public void WithoutMarginsEditingStillSpansTheWholeWidth()
    {
        // The guard against overreach: everything above must leave the ordinary behaviour alone.
        var emulator = Build();
        Feed(emulator, Esc + "[1;1H" + "ABCDEFGHIJ");

        Feed(emulator, Esc + "[1;1H" + Esc + "[2@");

        Assert.Equal("..ABCDEFGHIJ........", Row(emulator, 0));
    }
}
