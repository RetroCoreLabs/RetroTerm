using System;
using System.Text;
using RetroTerm.Core.Terminal.Buffer;
using RetroTerm.Core.Terminal.Emulators;
using Xunit;

namespace RetroTerm.Tests.Terminal;

/// <summary>
/// Phase 2b, part 1: the scrollback ring and per-line metadata.
///
/// Scrollback used to be a List that Add()ed a freshly allocated row and RemoveAt(0)'d once over
/// the limit. RemoveAt(0) shifts every remaining entry, so at the default 10000 lines one screen
/// of scrolling moved a quarter of a million references, and every scrolled line allocated a row
/// the GC then had to take back. It is now a fixed ring that overwrites the oldest slot and
/// reuses its row array.
///
/// Line metadata answers a question the grid cannot: a line that filled up and wrapped onto the
/// next one looks identical to a line that ended with a newline. Without the flag, reading the
/// screen back as text breaks every wrapped line at the screen width, and reflow-on-resize is
/// impossible in principle.
///
/// See docs\ARCHITECTURE-REVIEW-TERMINAL-EMULATION-2026-08-08.md, appendix 0d.
/// </summary>
public class Phase2ScrollbackAndLineMetadataTests
{
    private static void Feed(TerminalEmulatorBase emulator, string s)
    {
        emulator.ProcessData(Encoding.ASCII.GetBytes(s));
    }

    /// <summary>
    /// Marks row 0 with a recognisable character, then scrolls it into history.
    /// </summary>
    private static void ScrollOneMarkedLine(TerminalBuffer buffer, char marker)
    {
        buffer.SetCell(0, 0, new TerminalCell(marker));
        buffer.ScrollUp();
    }

    // ─────────────────────────────────────────────────────────────
    // Scrollback ring
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void ScrollbackKeepsLinesInOrder()
    {
        var buffer = new TerminalBuffer(10, 4, maxScrollbackLines: 100);

        ScrollOneMarkedLine(buffer, 'A');
        ScrollOneMarkedLine(buffer, 'B');
        ScrollOneMarkedLine(buffer, 'C');

        Assert.Equal(3, buffer.ScrollbackLineCount);
        Assert.Equal('A', (char)buffer.GetScrollbackLine(0)![0].Codepoint);
        Assert.Equal('B', (char)buffer.GetScrollbackLine(1)![0].Codepoint);
        Assert.Equal('C', (char)buffer.GetScrollbackLine(2)![0].Codepoint);
    }

    [Fact]
    public void ScrollbackStopsGrowingAtTheLimit_AndDropsTheOldest()
    {
        var buffer = new TerminalBuffer(10, 4, maxScrollbackLines: 3);

        ScrollOneMarkedLine(buffer, 'A');
        ScrollOneMarkedLine(buffer, 'B');
        ScrollOneMarkedLine(buffer, 'C');
        ScrollOneMarkedLine(buffer, 'D');

        Assert.Equal(3, buffer.ScrollbackLineCount);
        // A fell off the front; the window slid forward and order is preserved.
        Assert.Equal('B', (char)buffer.GetScrollbackLine(0)![0].Codepoint);
        Assert.Equal('C', (char)buffer.GetScrollbackLine(1)![0].Codepoint);
        Assert.Equal('D', (char)buffer.GetScrollbackLine(2)![0].Codepoint);
    }

    [Fact]
    public void ScrollbackSurvivesManyWrapsOfTheRing()
    {
        // Wrapping the ring several times is where an off-by-one in the modular arithmetic shows.
        var buffer = new TerminalBuffer(10, 4, maxScrollbackLines: 5);

        for (int i = 0; i < 23; i++)
        {
            ScrollOneMarkedLine(buffer, (char)('a' + (i % 26)));
        }

        Assert.Equal(5, buffer.ScrollbackLineCount);
        // The last five pushed were i = 18..22 -> 's','t','u','v','w'
        Assert.Equal('s', (char)buffer.GetScrollbackLine(0)![0].Codepoint);
        Assert.Equal('t', (char)buffer.GetScrollbackLine(1)![0].Codepoint);
        Assert.Equal('u', (char)buffer.GetScrollbackLine(2)![0].Codepoint);
        Assert.Equal('v', (char)buffer.GetScrollbackLine(3)![0].Codepoint);
        Assert.Equal('w', (char)buffer.GetScrollbackLine(4)![0].Codepoint);
    }

    [Fact]
    public void GetScrollbackLine_RejectsOutOfRangeIndexes()
    {
        var buffer = new TerminalBuffer(10, 4, maxScrollbackLines: 5);
        ScrollOneMarkedLine(buffer, 'A');

        Assert.Null(buffer.GetScrollbackLine(-1));
        Assert.Null(buffer.GetScrollbackLine(1));
        Assert.NotNull(buffer.GetScrollbackLine(0));
    }

    [Fact]
    public void AZeroLineScrollbackKeepsNothing_AndDoesNotThrow()
    {
        var buffer = new TerminalBuffer(10, 4, maxScrollbackLines: 0);

        ScrollOneMarkedLine(buffer, 'A');
        ScrollOneMarkedLine(buffer, 'B');

        Assert.Equal(0, buffer.ScrollbackLineCount);
        Assert.Null(buffer.GetScrollbackLine(0));
    }

    [Fact]
    public void ClearScrollbackEmptiesIt_AndItCanBeRefilled()
    {
        var buffer = new TerminalBuffer(10, 4, maxScrollbackLines: 5);
        ScrollOneMarkedLine(buffer, 'A');
        ScrollOneMarkedLine(buffer, 'B');

        buffer.ClearScrollback();
        Assert.Equal(0, buffer.ScrollbackLineCount);

        // The row arrays are recycled rather than dropped, so a stale line must not reappear.
        ScrollOneMarkedLine(buffer, 'C');
        Assert.Equal(1, buffer.ScrollbackLineCount);
        Assert.Equal('C', (char)buffer.GetScrollbackLine(0)![0].Codepoint);
    }

    [Fact]
    public void RecycledRowsDoNotLeakTheContentOfTheLineTheyReplaced()
    {
        // The ring reuses the evicted slot's array. If a shorter write left old cells behind,
        // history would show a line that never existed.
        var buffer = new TerminalBuffer(4, 3, maxScrollbackLines: 1);

        buffer.SetCell(0, 0, new TerminalCell('A'));
        buffer.SetCell(0, 1, new TerminalCell('A'));
        buffer.SetCell(0, 2, new TerminalCell('A'));
        buffer.SetCell(0, 3, new TerminalCell('A'));
        buffer.ScrollUp();

        buffer.SetCell(0, 0, new TerminalCell('B'));
        buffer.ScrollUp();

        var line = buffer.GetScrollbackLine(0)!;
        Assert.Equal('B', (char)line[0].Codepoint);
        // Columns 1..3 came from the cleared screen row, not from the 'A' line.
        Assert.True(line[1].IsEmpty);
        Assert.True(line[2].IsEmpty);
        Assert.True(line[3].IsEmpty);
    }

    [Fact]
    public void ViewportReadsMapThroughTheRing()
    {
        var buffer = new TerminalBuffer(10, 2, maxScrollbackLines: 3);

        ScrollOneMarkedLine(buffer, 'A');
        ScrollOneMarkedLine(buffer, 'B');

        // Scrolled back by 2, the viewport's top row is the oldest scrollback line.
        Assert.True(buffer.TryGetViewportCell(0, 0, 2, out var cell));
        Assert.Equal('A', (char)cell.Codepoint);

        Assert.True(buffer.TryGetViewportCell(1, 0, 2, out cell));
        Assert.Equal('B', (char)cell.Codepoint);
    }

    [Fact]
    public void ViewportReadBeforeTheStartOfHistoryReportsNoContent()
    {
        var buffer = new TerminalBuffer(10, 2, maxScrollbackLines: 3);
        ScrollOneMarkedLine(buffer, 'A');

        Assert.False(buffer.TryGetViewportCell(0, 0, 5, out _));
    }

    // ─────────────────────────────────────────────────────────────
    // Line metadata: the wrap flag
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void AutoWrapMarksTheLineAsContinuing()
    {
        var emulator = new VT100Emulator(5, 4);

        Feed(emulator, "ABCDEFG");   // 5 fit on row 0, the rest wraps to row 1

        Assert.True(emulator.Buffer.IsLineWrapped(0));
        Assert.False(emulator.Buffer.IsLineWrapped(1));
    }

    [Fact]
    public void ALineEndedByANewlineIsNotMarkedAsWrapped()
    {
        var emulator = new VT100Emulator(10, 4);

        Feed(emulator, "AB\r\nCD");

        Assert.False(emulator.Buffer.IsLineWrapped(0));
    }

    [Fact]
    public void ExactlyFillingALine_DoesNotMarkItWrapped_BecauseTheWrapIsDeferred()
    {
        // THIS TEST USED TO ASSERT THE OPPOSITE, and said so: it documented that Cursor.Advance
        // had no pending-wrap state and moved to the next line the moment the last column was
        // filled, so a line filled exactly was indistinguishable from one that really continued.
        //
        // That is fixed. The cursor now carries the VT Last Column Flag: writing the last column
        // leaves the cursor ON it and arms the flag, and only a FURTHER character performs the
        // wrap. Filling a 5-column line with exactly 5 characters therefore continues nothing —
        // which is also why printing exactly 80 characters on an 80-column VT does not scroll.
        var emulator = new VT100Emulator(5, 4);

        Feed(emulator, "ABCDE");

        Assert.False(emulator.Buffer.IsLineWrapped(0));
        Assert.Equal(0, emulator.GetCursor().Row);
        Assert.Equal(4, emulator.GetCursor().Column);

        // The sixth character is what actually wraps, and only then does line 0 continue.
        Feed(emulator, "F");

        Assert.True(emulator.Buffer.IsLineWrapped(0));
        Assert.Equal(1, emulator.GetCursor().Row);
        Assert.Equal(1, emulator.GetCursor().Column);
    }

    [Fact]
    public void WrapFlagsMoveWithTheLinesWhenTheScreenScrolls()
    {
        var buffer = new TerminalBuffer(10, 4);
        buffer.SetLineWrapped(2, true);

        buffer.ScrollUp();

        Assert.True(buffer.IsLineWrapped(1));
        Assert.False(buffer.IsLineWrapped(2));
    }

    [Fact]
    public void WrapFlagsMoveWithTheLinesWhenTheScreenScrollsDown()
    {
        var buffer = new TerminalBuffer(10, 4);
        buffer.SetLineWrapped(1, true);

        buffer.ScrollDown(0, 3);

        Assert.True(buffer.IsLineWrapped(2));
        Assert.False(buffer.IsLineWrapped(1));
    }

    [Fact]
    public void ClearingALineClearsItsWrapFlag()
    {
        var buffer = new TerminalBuffer(10, 4);
        buffer.SetLineWrapped(1, true);

        buffer.ClearLine(1);

        Assert.False(buffer.IsLineWrapped(1));
    }

    [Fact]
    public void ClearingTheScreenClearsEveryWrapFlag()
    {
        var buffer = new TerminalBuffer(10, 4);
        buffer.SetLineWrapped(0, true);
        buffer.SetLineWrapped(2, true);

        buffer.Clear();

        Assert.False(buffer.IsLineWrapped(0));
        Assert.False(buffer.IsLineWrapped(2));
    }

    [Fact]
    public void TheWrapFlagGoesToHistoryWithItsLine()
    {
        var buffer = new TerminalBuffer(10, 4, maxScrollbackLines: 5);
        buffer.SetLineWrapped(0, true);
        buffer.ScrollUp();

        Assert.True(buffer.IsScrollbackLineWrapped(0));

        buffer.ScrollUp();   // row 0 is blank now, so not wrapped
        Assert.True(buffer.IsScrollbackLineWrapped(0));
        Assert.False(buffer.IsScrollbackLineWrapped(1));
    }

    [Fact]
    public void TheAlternateScreenHasItsOwnWrapFlags()
    {
        var buffer = new TerminalBuffer(10, 4);
        buffer.SetLineWrapped(0, true);

        buffer.SwitchToAlternateBuffer();
        Assert.False(buffer.IsLineWrapped(0));   // fresh alternate screen

        buffer.SetLineWrapped(1, true);
        buffer.SwitchToPrimaryBuffer();

        // The primary's own flag is back, and the alternate's did not leak into it.
        Assert.True(buffer.IsLineWrapped(0));
        Assert.False(buffer.IsLineWrapped(1));
    }

    [Fact]
    public void ResizeKeepsTheWrapFlagsOfTheLinesThatSurvive()
    {
        var buffer = new TerminalBuffer(10, 4);
        buffer.SetLineWrapped(1, true);

        buffer.Resize(10, 8);

        Assert.True(buffer.IsLineWrapped(1));
        Assert.False(buffer.IsLineWrapped(7));
    }

    [Fact]
    public void IsLineWrappedIsSafeForRowsOffTheScreen()
    {
        var buffer = new TerminalBuffer(10, 4);

        Assert.False(buffer.IsLineWrapped(-1));
        Assert.False(buffer.IsLineWrapped(99));
        buffer.SetLineWrapped(99, true);   // must not throw
    }

    // ─────────────────────────────────────────────────────────────
    // Screen-to-text uses the wrap flag
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void ScreenTextJoinsWrappedLinesWhenAsked()
    {
        var emulator = new VT100Emulator(5, 4);

        Feed(emulator, "ABCDEFGH");

        Assert.Equal("ABCDEFGH", ScreenReader.GetScreenText(emulator.Buffer, joinWrappedLines: true));
    }

    [Fact]
    public void ScreenTextStillBreaksAtTheScreenWidthByDefault()
    {
        // The default must not change: MCP reads and prompt matching want the screen shape.
        var emulator = new VT100Emulator(5, 4);

        Feed(emulator, "ABCDEFGH");

        Assert.Equal("ABCDE\nFGH", ScreenReader.GetScreenText(emulator.Buffer));
    }

    [Fact]
    public void JoiningDoesNotSwallowRealLineBreaks()
    {
        var emulator = new VT100Emulator(10, 4);

        Feed(emulator, "ONE\r\nTWO");

        Assert.Equal("ONE\nTWO", ScreenReader.GetScreenText(emulator.Buffer, joinWrappedLines: true));
    }
}
