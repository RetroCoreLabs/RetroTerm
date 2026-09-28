using System.Collections.Generic;
using System.Text;
using Avalonia.Headless.XUnit;
using RetroTerm.Core.Search;
using RetroTerm.Core.Terminal.Emulators;
using SkiaSharp;
using Xunit;

namespace RetroTerm.Tests.Avalonia;

/// <summary>
/// Where a search match is painted once the view is scrolled back.
///
/// A match is numbered in the SEARCH's coordinates - 0 is the top of the live screen, minus one is
/// the line just above it - while the renderer draws rows of a WINDOW that may be showing history.
/// The two were compared as if they were the same number, so the highlight sat on the wrong line
/// as soon as the user scrolled, and a match in history could never be highlighted at all.
/// </summary>
[Collection("Avalonia")]
public class SearchHighlightTests
{
    private static VT100Emulator BuildWithHistory()
    {
        // Four lines through a three-row screen leaves the first line one row up in history.
        var emulator = new VT100Emulator(20, 3);
        emulator.ProcessData(Encoding.ASCII.GetBytes("NEEDLE\r\nTWO\r\nTHREE\r\nFOUR"));
        Assert.True(emulator.Buffer.ScrollbackLineCount > 0);
        return emulator;
    }

    private static List<SearchMatch> OneMatch(int row, int startCol, int endCol)
        => new List<SearchMatch> { new SearchMatch { Row = row, StartCol = startCol, EndCol = endCol } };

    /// <summary>
    /// Whether a cell is painted the bright yellow of the current search match.
    /// </summary>
    private static bool LooksHighlighted(SKColor colour)
        => colour.Red > 180 && colour.Green > 180 && colour.Blue < 80;

    [AvaloniaFact]
    public void AMatchInHistoryIsHighlightedOnceTheViewScrollsToIt()
    {
        // THE defect. "NEEDLE" is one line back, so it is search row minus one; scrolling back one
        // line puts it on window row 0, where the highlight belongs.
        var emulator = BuildWithHistory();
        emulator.ViewScrollOffset = 1;
        emulator.PublishFrame();

        var shot = RenderedScreenshot.Capture(emulator, "search-history-highlight",
            configureRenderer: r => r.SetSearchMatches(OneMatch(-1, 0, 5), 0));

        Assert.True(LooksHighlighted(shot.DominantColorInCell(0, 0)),
            "the history match should be highlighted on the top window row");
    }

    [AvaloniaFact]
    public void AndTheRowBelowItIsLeftAlone()
    {
        // The guard against highlighting everything: only the match's own row changes.
        var emulator = BuildWithHistory();
        emulator.ViewScrollOffset = 1;
        emulator.PublishFrame();

        var shot = RenderedScreenshot.Capture(emulator, "search-history-highlight-neighbour",
            configureRenderer: r => r.SetSearchMatches(OneMatch(-1, 0, 5), 0));

        Assert.False(LooksHighlighted(shot.DominantColorInCell(1, 0)));
    }

    [AvaloniaFact]
    public void AMatchOnTheLiveScreenMovesDownWhenTheViewScrollsBack()
    {
        // The other half of the same conversion. A match on live row 0 is one row FURTHER DOWN the
        // window once a line of history is shown above it.
        var emulator = BuildWithHistory();
        emulator.ViewScrollOffset = 1;
        emulator.PublishFrame();

        var shot = RenderedScreenshot.Capture(emulator, "search-live-match-while-scrolled",
            configureRenderer: r => r.SetSearchMatches(OneMatch(0, 0, 2), 0));

        Assert.False(LooksHighlighted(shot.DominantColorInCell(0, 0)));
        Assert.True(LooksHighlighted(shot.DominantColorInCell(1, 0)),
            "a live-screen match should move down the window by the scroll offset");
    }

    [AvaloniaFact]
    public void FindingAMatchInHistoryScrollsTheViewToIt()
    {
        // The counter used to say "1 of 2" while the screen stayed where it was, so the user was
        // told about a line they could not see. The match goes to the top row, because a found line
        // is nearly always read forwards.
        var emulator = BuildWithHistory();
        var canvas = new global::RetroTerm.Desktop.Controls.TerminalCanvas();
        canvas.SetEmulator(emulator);

        canvas.ScrollToSearchRow(-1);

        Assert.True(canvas.IsScrolledBack);
        Assert.Equal(1, emulator.ViewScrollOffset);
    }

    [AvaloniaFact]
    public void AndAMatchOnTheLiveScreenSnapsTheViewBack()
    {
        var emulator = BuildWithHistory();
        var canvas = new global::RetroTerm.Desktop.Controls.TerminalCanvas();
        canvas.SetEmulator(emulator);
        canvas.ScrollToSearchRow(-1);

        canvas.ScrollToSearchRow(0);

        Assert.False(canvas.IsScrolledBack);
        Assert.Equal(0, emulator.ViewScrollOffset);
    }

    [AvaloniaFact]
    public void ScrollingNeverGoesPastTheOldestLineThereIs()
    {
        // A match row can only come from a search of this buffer, but clamping is what keeps a
        // stale match list from asking for history that has since been trimmed away.
        var emulator = BuildWithHistory();
        var canvas = new global::RetroTerm.Desktop.Controls.TerminalCanvas();
        canvas.SetEmulator(emulator);

        canvas.ScrollToSearchRow(-500);

        Assert.Equal(emulator.Buffer.ScrollbackLineCount, emulator.ViewScrollOffset);
    }

    [AvaloniaFact]
    public void OnTheLiveViewNothingMoves()
    {
        // The guard that the conversion is a no-op when the offset is zero, which is the case
        // every existing test covers.
        var emulator = BuildWithHistory();

        var shot = RenderedScreenshot.Capture(emulator, "search-live-highlight",
            configureRenderer: r => r.SetSearchMatches(OneMatch(0, 0, 2), 0));

        Assert.True(LooksHighlighted(shot.DominantColorInCell(0, 0)));
    }
}
