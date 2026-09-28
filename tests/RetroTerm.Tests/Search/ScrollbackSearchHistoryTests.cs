using System.Collections.Generic;
using RetroTerm.Core.Search;
using RetroTerm.Core.Terminal.Buffer;
using Xunit;

namespace RetroTerm.Tests.Search;

/// <summary>
/// Whether the scrollback search actually searches the scrollback.
///
/// The existing tests all put their text on the screen, so nothing ever asked the one question the
/// feature is named after: can the user find a line that has already scrolled past? Matches in
/// history are reported with a NEGATIVE row - minus one is the line just above the top of the
/// screen - so the caller can tell the two apart and scroll to it.
/// </summary>
public class ScrollbackSearchHistoryTests
{
    /// <summary>
    /// Fills a buffer with lines and scrolls them until the first ones are in history.
    /// </summary>
    private static TerminalBuffer BuildWithHistory()
    {
        var buffer = new TerminalBuffer(20, 4, 100);

        WriteLine(buffer, 0, "needle in the past");
        WriteLine(buffer, 1, "second line");
        WriteLine(buffer, 2, "third line");
        WriteLine(buffer, 3, "fourth line");

        // Three scrolls put the first three lines into history and leave the fourth on top.
        buffer.ScrollUp();
        buffer.ScrollUp();
        buffer.ScrollUp();

        WriteLine(buffer, 1, "needle on screen");
        return buffer;
    }

    private static void WriteLine(TerminalBuffer buffer, int row, string text)
    {
        for (int col = 0; col < text.Length && col < buffer.Width; col++)
        {
            buffer[row, col] = new TerminalCell { Codepoint = text[col] };
        }
    }

    [Fact]
    public void TextThatHasScrolledOffIsStillFound()
    {
        // THE question the feature is named after.
        var buffer = BuildWithHistory();
        Assert.Equal(3, buffer.ScrollbackLineCount);

        var search = new ScrollbackSearch(buffer);
        var matches = search.Search("needle in the past", new SearchOptions());

        Assert.Single(matches);
        Assert.True(matches[0].Row < 0, "a match in history must report a negative row");
    }

    [Fact]
    public void TheRowSaysHowFarBackItIs()
    {
        // Minus one is the line immediately above the screen, so the oldest of three is minus three.
        var buffer = BuildWithHistory();
        var search = new ScrollbackSearch(buffer);

        var matches = search.Search("needle in the past", new SearchOptions());

        Assert.Equal(-3, matches[0].Row);
        Assert.Equal(0, matches[0].StartCol);
    }

    [Fact]
    public void HistoryAndScreenAreBothSearchedAndHistoryComesFirst()
    {
        var buffer = BuildWithHistory();
        var search = new ScrollbackSearch(buffer);

        var matches = search.Search("needle", new SearchOptions());

        Assert.Equal(2, matches.Count);
        Assert.Equal(-3, matches[0].Row);
        Assert.Equal(1, matches[1].Row);
    }

    [Fact]
    public void CaseFoldingReachesHistoryToo()
    {
        var buffer = BuildWithHistory();
        var search = new ScrollbackSearch(buffer);

        var matches = search.Search("NEEDLE IN THE PAST", new SearchOptions { CaseSensitive = false });

        Assert.Single(matches);
        Assert.Equal(-3, matches[0].Row);
    }

    [Fact]
    public void SoDoesARegex()
    {
        var buffer = BuildWithHistory();
        var search = new ScrollbackSearch(buffer);

        var matches = search.Search("n..dle in", new SearchOptions { UseRegex = true });

        Assert.Single(matches);
        Assert.Equal(-3, matches[0].Row);
    }

    [Fact]
    public void AWholeWordRegexIsStillARegex()
    {
        // Asking for whole words used to run the pattern through Regex.Escape, which turned a
        // regex into a search for its own punctuation - "sec.nd" stopped matching "second" and
        // started looking for a literal full stop.
        var buffer = BuildWithHistory();
        var search = new ScrollbackSearch(buffer);

        var matches = search.Search("sec.nd", new SearchOptions { UseRegex = true, WholeWord = true });

        Assert.Single(matches);
        Assert.Equal(-2, matches[0].Row);
    }

    [Fact]
    public void AndAWholeWordPlainSearchStillMeansWholeWords()
    {
        var buffer = new TerminalBuffer(20, 2, 100);
        WriteLine(buffer, 0, "line lines");

        var search = new ScrollbackSearch(buffer);
        var matches = search.Search("line", new SearchOptions { WholeWord = true });

        Assert.Single(matches);
        Assert.Equal(0, matches[0].StartCol);
    }

    [Fact]
    public void FindNextWalksFromHistoryOntoTheScreen()
    {
        var buffer = BuildWithHistory();
        var search = new ScrollbackSearch(buffer);
        var matches = search.Search("needle", new SearchOptions());

        var next = search.FindNext(matches, -3, 0);

        Assert.NotNull(next);
        Assert.Equal(1, next!.Row);
    }

    [Fact]
    public void FindPreviousWalksBackOffTheScreenIntoHistory()
    {
        var buffer = BuildWithHistory();
        var search = new ScrollbackSearch(buffer);
        var matches = search.Search("needle", new SearchOptions());

        var previous = search.FindPrevious(matches, 1, 0);

        Assert.NotNull(previous);
        Assert.Equal(-3, previous!.Row);
    }

    [Fact]
    public void AnEmptyHistoryIsNotAnError()
    {
        var buffer = new TerminalBuffer(20, 4, 100);
        WriteLine(buffer, 0, "only on screen");

        var search = new ScrollbackSearch(buffer);
        var matches = search.Search("only", new SearchOptions());

        Assert.Single(matches);
        Assert.Equal(0, matches[0].Row);
    }

    [Fact]
    public void ABufferWithNoHistoryAtAllIsSafeToSearch()
    {
        // Scrollback can be turned off entirely, and the ring is then a zero-length array.
        var buffer = new TerminalBuffer(20, 4, 0);
        WriteLine(buffer, 0, "no history here");
        buffer.ScrollUp();

        var search = new ScrollbackSearch(buffer);
        var matches = search.Search("history", new SearchOptions());

        Assert.Empty(matches);
    }
}
