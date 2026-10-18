using RetroTerm.Core.Selection;
using RetroTerm.Core.Terminal.Buffer;
using Xunit;

namespace RetroTerm.Tests.Selection;

/// <summary>
/// Selecting text that has scrolled off the screen.
///
/// Selection rows are numbered the way search matches are: 0 is the top of the live screen and
/// minus one is the line immediately above it. Before this, every read went through the screen
/// grid, so a drag made while the view was scrolled back highlighted the line the user was
/// pointing at and copied whatever happened to be on the live screen at that row instead.
/// </summary>
public class SelectionAcrossHistoryTests
{
    private static TerminalBuffer BuildWithHistory()
    {
        var buffer = new TerminalBuffer(20, 3, 100);

        WriteLine(buffer, 0, "FIRST");
        WriteLine(buffer, 1, "SECOND");
        WriteLine(buffer, 2, "THIRD");

        // Two scrolls put FIRST and SECOND into history.
        buffer.ScrollUp();
        buffer.ScrollUp();

        WriteLine(buffer, 0, "THIRD");
        WriteLine(buffer, 1, "FOURTH");
        WriteLine(buffer, 2, "FIFTH");
        return buffer;
    }

    private static void WriteLine(TerminalBuffer buffer, int row, string text)
    {
        for (int col = 0; col < buffer.Width; col++)
        {
            buffer[row, col] = col < text.Length
                ? new TerminalCell { Codepoint = text[col] }
                : TerminalCell.Empty;
        }
    }

    [Fact]
    public void ALineInHistoryCanBeSelectedAndCopied()
    {
        // THE defect. Minus two is the older of the two history lines.
        var buffer = BuildWithHistory();
        Assert.Equal(2, buffer.ScrollbackLineCount);

        var selection = new SelectionManager(buffer);
        selection.StartSelection(-2, 0);
        selection.ExtendSelection(-2, 4);

        Assert.Equal("FIRST", selection.GetSelectedText());
    }

    [Fact]
    public void TheOtherHistoryLineIsTheOtherOne()
    {
        // The guard against reading the right ring slot by accident.
        var buffer = BuildWithHistory();
        var selection = new SelectionManager(buffer);

        selection.StartSelection(-1, 0);
        selection.ExtendSelection(-1, 5);

        Assert.Equal("SECOND", selection.GetSelectedText());
    }

    [Fact]
    public void ASelectionCanRunFromHistoryOntoTheLiveScreen()
    {
        // The case that made this worth doing: a drag that starts above the screen and ends on it.
        var buffer = BuildWithHistory();
        var selection = new SelectionManager(buffer);

        selection.StartSelection(-1, 0);
        selection.ExtendSelection(0, 4);

        // This assertion used to read "SECOND              \nTHIRD", with the row that is left
        // behind taken to its full width, padding included - and it said so, with a note that the
        // trimming lived in the highlight rather than in the copy and was being STATED rather than
        // quietly fixed inside a different repair.
        //
        // This is that repair, made on purpose: writing the by-hand pass for M8.3 turned the
        // mismatch into a case a person would have to check, and the honest answer is that the
        // text a user gets should be the text they saw highlighted. Both now read their row range
        // from TryGetSelectedColumnRange. See docs\manual-tests\M8-WHOLE-TERMINAL.md, case M8.3a.
        Assert.Equal("SECOND\nTHIRD", selection.GetSelectedText());
    }

    [Fact]
    public void TheLiveScreenStillReadsTheSameAsItAlwaysDid()
    {
        // The guard that nothing about the ordinary case moved.
        var buffer = BuildWithHistory();
        var selection = new SelectionManager(buffer);

        selection.StartSelection(1, 0);
        selection.ExtendSelection(1, 5);

        Assert.Equal("FOURTH", selection.GetSelectedText());
    }

    [Fact]
    public void ALineSelectionInHistoryTakesTheWholeLine()
    {
        var buffer = BuildWithHistory();
        var selection = new SelectionManager(buffer);

        selection.StartSelection(-2, 3, SelectionMode.Line);
        selection.ExtendSelection(-2, 3);

        Assert.Equal("FIRST", selection.GetSelectedText().TrimEnd());
    }

    [Fact]
    public void AWordSelectionInHistoryFindsTheWordsEdges()
    {
        var buffer = new TerminalBuffer(20, 2, 100);
        WriteLine(buffer, 0, "alpha beta gamma");
        buffer.ScrollUp();

        var selection = new SelectionManager(buffer);
        selection.StartSelection(-1, 7, SelectionMode.Word);
        selection.ExtendSelection(-1, 7);

        Assert.Equal("beta", selection.GetSelectedText());
    }

    [Fact]
    public void ARectangleCanStraddleTheBoundaryToo()
    {
        var buffer = BuildWithHistory();
        var selection = new SelectionManager(buffer);

        selection.StartSelection(-1, 0, SelectionMode.Rectangular);
        selection.ExtendSelection(0, 2);

        Assert.Equal("SEC\nTHI", selection.GetSelectedText());
    }

    [Fact]
    public void HighlightingKnowsAboutHistoryRowsAsWell()
    {
        // The renderer asks per row, and it must get the same answer for a history row that the
        // copy does - a highlight on one line and text from another is how this was found.
        var buffer = BuildWithHistory();
        var selection = new SelectionManager(buffer);
        selection.StartSelection(-2, 0);
        selection.ExtendSelection(-2, 4);

        Assert.True(selection.TryGetSelectedColumnRange(-2, out int start, out int end));
        Assert.Equal(0, start);
        Assert.Equal(4, end);
    }

    [Fact]
    public void AskingForHistoryThatIsNoLongerThereIsNotACrash()
    {
        // A selection outlives the lines it points at: the ring recycles, and the oldest line goes.
        var buffer = BuildWithHistory();
        var selection = new SelectionManager(buffer);

        selection.StartSelection(-500, 0);
        selection.ExtendSelection(-500, 4);

        Assert.Equal("", selection.GetSelectedText());
    }
}
