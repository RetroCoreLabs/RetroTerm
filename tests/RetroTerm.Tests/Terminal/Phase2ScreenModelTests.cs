using System;
using System.Text;
using RetroTerm.Core.Terminal.Buffer;
using RetroTerm.Core.Terminal.Emulators;
using Xunit;

namespace RetroTerm.Tests.Terminal;

/// <summary>
/// Phase 2 screen-model consolidation.
///
/// Five defects, each of which a real host program would hit:
///  1. IL/DL ignored the scrolling region and always worked to the bottom of the screen,
///     so a full-screen program that reserved a status line smeared it.
///  2. DL at row 0 pushed the deleted lines into scrollback, corrupting the history the
///     user scrolls back through with lines the application had removed.
///  3. Tab stops did not exist - HTS was a TODO and HT was hardcoded to every 8 columns,
///     so a host that moved its stops (forms do) landed in the wrong columns. TBC, CHT
///     and CBT were not implemented at all.
///  4. DECSC saved only row/column/style, so DECRC did not put back the graphic rendition,
///     the character sets, origin mode or auto-wrap.
///  5. The alternate screen buffer existed in TerminalBuffer but DECSET 47/1047/1049 were
///     all "TODO", and Resize left the inactive buffer at the old size - a hard crash on
///     any resize taller than the old screen.
///
/// See docs\ARCHITECTURE-REVIEW-TERMINAL-EMULATION-2026-08-08.md, migration plan Phase 2.
/// </summary>
public class Phase2ScreenModelTests
{
    // NOTE on the escapes below: C#'s "\x" escape is VARIABLE length, so backslash-x-1-b-7 is the single character U+01B7 --- not ESC followed by '7'. Any ESC sequence whose next character is a hex
    // digit (7, 8, c, ...) must use the four-digit backslash-u form instead. See the calls below.
    // (This exact trap already bit once, in CtrlSpaceNulTests.)
    private static void Feed(TerminalEmulatorBase emulator, string s)
    {
        emulator.ProcessData(Encoding.ASCII.GetBytes(s));
    }

    /// <summary>
    /// Writes "L0".."Ln" one per row, so a scroll is visible by name.
    /// </summary>
    private static void FillRows(TerminalEmulatorBase emulator, int rows)
    {
        for (int row = 0; row < rows; row++)
        {
            Feed(emulator, "\x1b[" + (row + 1) + ";1H" + "L" + row);
        }
    }

    private static string Row(TerminalEmulatorBase emulator, int row)
    {
        return ScreenReader.GetRowText(emulator.Buffer, row);
    }

    // ─────────────────────────────────────────────────────────────
    // 1. IL / DL are bounded by the scrolling region
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void DeleteLines_StopsAtTheBottomOfTheScrollingRegion()
    {
        // Region rows 1-4 (1-based 2..5). Row 6 (index 5) is outside it and must not move.
        var emulator = new VT100Emulator(20, 8);
        FillRows(emulator, 8);
        Feed(emulator, "\x1b[2;5r");   // DECSTBM 2..5 -> indices 1..4

        Feed(emulator, "\x1b[2;1H");   // cursor to row index 1
        Feed(emulator, "\x1b[M");      // DL 1

        Assert.Equal("L0", Row(emulator, 0));
        Assert.Equal("L2", Row(emulator, 1));
        Assert.Equal("L3", Row(emulator, 2));
        Assert.Equal("L4", Row(emulator, 3));
        Assert.Equal("", Row(emulator, 4));    // blank pulled in at the region's bottom
        Assert.Equal("L5", Row(emulator, 5));  // outside the region - untouched
        Assert.Equal("L6", Row(emulator, 6));
        Assert.Equal("L7", Row(emulator, 7));
    }

    [Fact]
    public void InsertLines_StopsAtTheBottomOfTheScrollingRegion()
    {
        var emulator = new VT100Emulator(20, 8);
        FillRows(emulator, 8);
        Feed(emulator, "\x1b[2;5r");   // region indices 1..4

        Feed(emulator, "\x1b[2;1H");
        Feed(emulator, "\x1b[L");      // IL 1

        Assert.Equal("L0", Row(emulator, 0));
        Assert.Equal("", Row(emulator, 1));    // the inserted blank
        Assert.Equal("L1", Row(emulator, 2));
        Assert.Equal("L2", Row(emulator, 3));
        Assert.Equal("L3", Row(emulator, 4));
        // L4 fell off the bottom of the REGION, it did not push L5 down.
        Assert.Equal("L5", Row(emulator, 5));
        Assert.Equal("L6", Row(emulator, 6));
        Assert.Equal("L7", Row(emulator, 7));
    }

    [Fact]
    public void InsertLines_OutsideTheScrollingRegion_DoesNothing()
    {
        var emulator = new VT100Emulator(20, 8);
        FillRows(emulator, 8);
        Feed(emulator, "\x1b[1;3r");   // region indices 0..2

        Feed(emulator, "\x1b[6;1H");   // cursor at index 5, below the region
        Feed(emulator, "\x1b[L");

        for (int row = 0; row < 8; row++)
        {
            Assert.Equal("L" + row, Row(emulator, row));
        }
    }

    [Fact]
    public void WithNoScrollingRegionSet_InsertAndDeleteStillWorkToTheBottomOfTheScreen()
    {
        // Guard: the region-aware change must not alter the ordinary full-screen case.
        var emulator = new VT100Emulator(20, 5);
        FillRows(emulator, 5);

        Feed(emulator, "\x1b[1;1H\x1b[M");   // DL 1 at the top

        Assert.Equal("L1", Row(emulator, 0));
        Assert.Equal("L2", Row(emulator, 1));
        Assert.Equal("L3", Row(emulator, 2));
        Assert.Equal("L4", Row(emulator, 3));
        Assert.Equal("", Row(emulator, 4));
    }

    // ─────────────────────────────────────────────────────────────
    // 2. Deleted lines are not history
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void DeleteLines_DoesNotPushTheDeletedLineIntoScrollback()
    {
        var buffer = new TerminalBuffer(10, 4);
        Assert.Equal(0, buffer.ScrollbackLineCount);

        buffer.DeleteLines(0, 2);

        Assert.Equal(0, buffer.ScrollbackLineCount);
    }

    [Fact]
    public void ScrollingOffTheTopStillGoesToScrollback()
    {
        // The counterpart: an index/line-feed off the top IS history and must be kept.
        var buffer = new TerminalBuffer(10, 4);

        buffer.ScrollUp();

        Assert.Equal(1, buffer.ScrollbackLineCount);
    }

    [Fact]
    public void ScrollingARegionThatDoesNotStartAtRowZero_AddsNothingToScrollback()
    {
        var buffer = new TerminalBuffer(10, 6);

        buffer.ScrollUp(2, 4);

        Assert.Equal(0, buffer.ScrollbackLineCount);
    }

    // ─────────────────────────────────────────────────────────────
    // 3. Tab stops
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void DefaultTabStops_AreEveryEightColumns()
    {
        var emulator = new VT100Emulator(40, 5);

        Feed(emulator, "\t");
        Assert.Equal(8, emulator.Cursor.Column);
        Feed(emulator, "\t");
        Assert.Equal(16, emulator.Cursor.Column);
    }

    [Fact]
    public void HorizontalTabSet_CreatesAStopTheTabHonours()
    {
        var emulator = new VT100Emulator(40, 5);

        Feed(emulator, "\x1b[1;4H");   // column index 3
        Feed(emulator, "\x1bH");       // HTS - set a stop here
        Feed(emulator, "\x1b[1;1H");   // back to column 0
        Feed(emulator, "\t");

        Assert.Equal(3, emulator.Cursor.Column);
    }

    [Fact]
    public void TabClear_ClearsTheStopAtTheCursor()
    {
        var emulator = new VT100Emulator(40, 5);

        Feed(emulator, "\x1b[1;9H");   // column index 8, a default stop
        Feed(emulator, "\x1b[g");      // TBC 0 - clear it
        Feed(emulator, "\x1b[1;1H");
        Feed(emulator, "\t");

        Assert.Equal(16, emulator.Cursor.Column); // skipped the cleared stop
    }

    [Fact]
    public void TabClearAll_LeavesTabGoingToTheLastColumn()
    {
        var emulator = new VT100Emulator(40, 5);

        Feed(emulator, "\x1b[3g");     // TBC 3 - clear every stop
        Feed(emulator, "\x1b[1;1H");
        Feed(emulator, "\t");

        Assert.Equal(39, emulator.Cursor.Column);
    }

    [Fact]
    public void CursorForwardTabulation_MovesSeveralStops()
    {
        var emulator = new VT100Emulator(40, 5);

        Feed(emulator, "\x1b[1;1H");
        Feed(emulator, "\x1b[3I");     // CHT 3

        Assert.Equal(24, emulator.Cursor.Column);
    }

    [Fact]
    public void CursorBackwardTabulation_MovesBackSeveralStops()
    {
        var emulator = new VT100Emulator(40, 5);

        Feed(emulator, "\x1b[1;31H");  // column index 30
        Feed(emulator, "\x1b[2Z");     // CBT 2 -> 24 then 16

        Assert.Equal(16, emulator.Cursor.Column);
    }

    [Fact]
    public void CursorBackwardTabulation_StopsAtColumnZero()
    {
        var emulator = new VT100Emulator(40, 5);

        Feed(emulator, "\x1b[1;5H");
        Feed(emulator, "\x1b[9Z");

        Assert.Equal(0, emulator.Cursor.Column);
    }

    [Fact]
    public void ResetRestoresTheDefaultTabStops()
    {
        var emulator = new VT100Emulator(40, 5);

        Feed(emulator, "\x1b[3g");     // clear every stop
        Feed(emulator, "\u001bc");       // RIS
        Feed(emulator, "\t");

        Assert.Equal(8, emulator.Cursor.Column);
    }

    // ─────────────────────────────────────────────────────────────
    // 4. DECSC / DECRC save the whole state
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void DecrcRestoresTheGraphicRendition_NotJustThePosition()
    {
        var emulator = new VT100Emulator(20, 5);

        Feed(emulator, "\x1b[31m");    // red foreground
        Feed(emulator, "\u001b7");       // DECSC
        Feed(emulator, "\x1b[32m");    // green
        Feed(emulator, "\u001b8");       // DECRC - must put red back
        Feed(emulator, "\x1b[1;1HX");

        var cell = emulator.Buffer.GetCell(0, 0);
        var afterRestore = cell.Foreground;

        // Compare against what a plain red write produces, rather than hardcoding the
        // palette's representation of red.
        var reference = new VT100Emulator(20, 5);
        Feed(reference, "\x1b[31m\x1b[1;1HX");

        Assert.Equal(reference.Buffer.GetCell(0, 0).Foreground, afterRestore);
    }

    [Fact]
    public void DecrcRestoresThePosition()
    {
        var emulator = new VT100Emulator(20, 5);

        Feed(emulator, "\x1b[3;7H");
        Feed(emulator, "\u001b7");
        Feed(emulator, "\x1b[1;1H");
        Feed(emulator, "\u001b8");

        Assert.Equal(2, emulator.Cursor.Row);
        Assert.Equal(6, emulator.Cursor.Column);
    }

    [Fact]
    public void DecrcWithNothingSaved_GoesHomeWithDefaultRendition()
    {
        // The standard's rule for a restore that was never preceded by a save.
        var emulator = new VT100Emulator(20, 5);

        Feed(emulator, "\x1b[3;7H");
        Feed(emulator, "\u001b8");

        Assert.Equal(0, emulator.Cursor.Row);
        Assert.Equal(0, emulator.Cursor.Column);
    }

    // ─────────────────────────────────────────────────────────────
    // 5. Alternate screen
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void Decset1049_SwitchesToACleanAlternateScreen()
    {
        var emulator = new VT100Emulator(20, 5);
        Feed(emulator, "\x1b[1;1HPRIMARY");

        Feed(emulator, "\x1b[?1049h");

        Assert.True(emulator.Buffer.IsUsingAlternateBuffer);
        Assert.Equal("", Row(emulator, 0));
    }

    [Fact]
    public void Decrst1049_BringsBackThePrimaryScreenAndTheSavedState()
    {
        var emulator = new VT100Emulator(20, 5);
        Feed(emulator, "\x1b[3;5HPRIMARY");

        Feed(emulator, "\x1b[?1049h");
        Feed(emulator, "\x1b[1;1HALTERNATE");
        Feed(emulator, "\x1b[?1049l");

        Assert.False(emulator.Buffer.IsUsingAlternateBuffer);
        Assert.Equal("    PRIMARY", Row(emulator, 2));
        // The cursor is back where it was when the program switched in - after "PRIMARY".
        Assert.Equal(2, emulator.Cursor.Row);
        Assert.Equal(11, emulator.Cursor.Column);
    }

    [Fact]
    public void Decset47_SwitchesBuffersWithoutClearing()
    {
        // The oldest form does not clear on the way in.
        var emulator = new VT100Emulator(20, 5);

        Feed(emulator, "\x1b[?47h");
        Feed(emulator, "\x1b[1;1HALT");
        Feed(emulator, "\x1b[?47l");
        Feed(emulator, "\x1b[?47h");

        Assert.Equal("ALT", Row(emulator, 0));
    }

    [Fact]
    public void Decrst1047_ClearsTheAlternateScreenOnTheWayOut()
    {
        var emulator = new VT100Emulator(20, 5);

        Feed(emulator, "\x1b[?1047h");
        Feed(emulator, "\x1b[1;1HALT");
        Feed(emulator, "\x1b[?1047l");
        Feed(emulator, "\x1b[?1047h");

        Assert.Equal("", Row(emulator, 0));
    }

    [Fact]
    public void PrimaryContentSurvivesATripThroughTheAlternateScreen()
    {
        var emulator = new VT100Emulator(20, 5);
        Feed(emulator, "\x1b[1;1HKEEP ME");

        Feed(emulator, "\x1b[?1049h");
        Feed(emulator, "\x1b[1;1HOVERWRITTEN");
        Feed(emulator, "\x1b[?1049l");

        Assert.Equal("KEEP ME", Row(emulator, 0));
    }

    [Fact]
    public void ResizingWhileTheAlternateBufferExists_DoesNotCrash()
    {
        // Clear() used to walk the alternate buffer using the NEW height, so any resize
        // taller than the old screen threw IndexOutOfRangeException.
        var buffer = new TerminalBuffer(20, 5);
        buffer.SwitchToAlternateBuffer();
        buffer.SwitchToPrimaryBuffer();

        buffer.Resize(20, 30);
        buffer.Clear();

        Assert.Equal(30, buffer.Height);
    }

    [Fact]
    public void ResizingWhileOnTheAlternateScreen_KeepsThePrimaryTheRightSize()
    {
        var buffer = new TerminalBuffer(20, 5);
        buffer.SetCell(0, 0, new TerminalCell('P'));

        buffer.SwitchToAlternateBuffer();
        buffer.Resize(20, 12);
        buffer.SwitchToPrimaryBuffer();

        // The primary is now 12 rows too, and its content is still there.
        Assert.Equal(12, buffer.Height);
        Assert.Equal('P', (char)buffer.GetCell(0, 0).Codepoint);
        // A cell only the resized primary has must be reachable.
        buffer.SetCell(11, 0, new TerminalCell('Z'));
        Assert.Equal('Z', (char)buffer.GetCell(11, 0).Codepoint);
    }
}
