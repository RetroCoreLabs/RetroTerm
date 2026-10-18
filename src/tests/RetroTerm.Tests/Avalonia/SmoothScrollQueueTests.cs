using System.Text;
using RetroTerm.Core.Configuration;
using RetroTerm.Core.Terminal.Emulators;
using RetroTerm.Core.Terminal.Rendering;
using Xunit;

namespace RetroTerm.Tests.Avalonia;

/// <summary>
/// The smooth scroll backlog - what happens when the host outruns the animation.
/// </summary>
/// <remarks>
/// <para><b>The problem these tests pin</b></para>
/// A slide takes about as many frames as a character cell has pixels, which puts smooth scrolling at
/// roughly four lines a second. Hosts are faster than that. The first version animated one line and
/// ignored every line that arrived while it was busy, so a fast host produced a MIXTURE of sliding
/// and jumping lines - which reads worse than jumping alone, because the eye keeps being promised
/// motion that then does not happen.
/// <para><b>The answer, and why it is possible at all</b></para>
/// The lines are not gone: they are in scrollback. So the picture is allowed to run behind the
/// buffer by a count of lines, and walks forward one slide at a time until it catches up. Nothing is
/// dropped and nothing jumps, as long as the backlog stays inside its limit.
/// <para><b>Why none of this needs the UI</b></para>
/// The count and the lagged frame both live in the core, so these are plain facts about a frame and
/// a counter. What the pixels do with it is covered by SmoothScrollTests.
/// </remarks>
public class SmoothScrollQueueTests
{
    /// <summary>
    /// The escape character, built from its code so no raw control byte lands in this file.
    /// </summary>
    private const char Escape = (char)0x1B;

    /// <summary>
    /// A VT340 with smooth scrolling switched on.
    /// </summary>
    /// <returns>
    /// The terminal.
    /// </returns>
    private static TerminalEmulatorBase SmoothVt340()
    {
        var emulator = EmulatorFactory.CreateEmulator("VT340", 80, 24, 100);
        emulator.ProcessData(Encoding.ASCII.GetBytes(Escape + "[?4h"));
        return emulator;
    }

    /// <summary>
    /// Scrolls the screen without ever letting the view catch up.
    /// </summary>
    /// <param name="emulator">
    /// The terminal to scroll.
    /// </param>
    /// <param name="lines">
    /// How many lines to push past the bottom of the screen.
    /// </param>
    /// <remarks>
    /// Stands in for a host faster than the animation. Nothing here claims a line, so the backlog
    /// only grows - which is the state a fast host leaves the terminal in.
    /// </remarks>
    private static void ScrollWithoutCatchingUp(TerminalEmulatorBase emulator, int lines)
    {
        // TWENTY-THREE, not twenty-four. Writing 24 lines already costs one scroll: the last one
        // lands on the bottom row and its own newline is what pushes the screen up. Filling with 24
        // and then counting from there reports one scroll more than was asked for.
        for (int i = 0; i < 23 + lines; i++)
        {
            emulator.ProcessData(Encoding.ASCII.GetBytes("LINE " + i + "\r\n"));
        }
    }

    /// <summary>
    /// Reads the line number printed at the start of a frame row.
    /// </summary>
    /// <param name="frame">
    /// The frame to read.
    /// </param>
    /// <param name="row">
    /// The row to read.
    /// </param>
    /// <returns>
    /// The number after the word LINE.
    /// </returns>
    private static int LineNumberAt(ScreenFrame frame, int row)
    {
        var text = new StringBuilder();
        for (int col = 0; col < 12; col++)
        {
            uint codepoint = frame[row, col].Codepoint;
            if (codepoint != 0) text.Append((char)codepoint);
        }

        return int.Parse(text.ToString().Trim().Substring(5));
    }

    /// <summary>
    /// Reads the line number at the top of the live buffer.
    /// </summary>
    /// <param name="emulator">
    /// The terminal to read.
    /// </param>
    /// <returns>
    /// The number after the word LINE.
    /// </returns>
    private static int LiveTopLineNumber(TerminalEmulatorBase emulator)
    {
        var buffer = emulator.GetBuffer();
        var text = new StringBuilder();
        for (int col = 0; col < 12; col++)
        {
            uint codepoint = buffer.GetCell(0, col).Codepoint;
            if (codepoint != 0) text.Append((char)codepoint);
        }

        return int.Parse(text.ToString().Trim().Substring(5));
    }

    [Fact]
    public void LinesArrivingFasterThanTheAnimationAreQueuedRatherThanDropped()
    {
        // THE POINT OF THE WHOLE THING. Every line the host scrolls has to be owed to the picture,
        // or the fast case degrades to a mixture of sliding and jumping.
        var emulator = SmoothVt340();

        ScrollWithoutCatchingUp(emulator, 5);

        Assert.Equal(5, emulator.SmoothScrollBacklog);
    }

    [Fact]
    public void NothingIsQueuedWhileSmoothScrollingIsOff()
    {
        // A terminal nobody asked to smooth-scroll must never start running its picture behind.
        var emulator = EmulatorFactory.CreateEmulator("VT340", 80, 24, 100);

        ScrollWithoutCatchingUp(emulator, 5);

        Assert.Equal(0, emulator.SmoothScrollBacklog);
    }

    [Fact]
    public void AQueuedPictureShowsTheOlderScreen()
    {
        // What being behind actually MEANS. The buffer has moved on; the frame handed to the
        // renderer must still show the screen as it was, or there is nothing left to animate.
        var emulator = SmoothVt340();
        ScrollWithoutCatchingUp(emulator, 5);

        emulator.RequestFrame();

        Assert.Equal(LiveTopLineNumber(emulator) - 5, LineNumberAt(emulator.LatestFrame!, 0));
    }

    [Fact]
    public void ClaimingALineMovesThePictureExactlyOneLineForward()
    {
        // One claimed line must be one line of travel. More would skip content; less would mean the
        // picture never catches up however long it animates.
        var emulator = SmoothVt340();
        ScrollWithoutCatchingUp(emulator, 5);

        emulator.RequestFrame();
        int before = LineNumberAt(emulator.LatestFrame!, 0);

        Assert.True(emulator.TryBeginSmoothScrollLine());
        emulator.RequestFrame();
        int after = LineNumberAt(emulator.LatestFrame!, 0);

        Assert.Equal(4, emulator.SmoothScrollBacklog);
        Assert.Equal(before + 1, after);
    }

    [Fact]
    public void AnUpToDatePictureHasNoLineToClaim()
    {
        // The chain that runs slide after slide stops on this. If it ever returned true with an
        // empty backlog the animation would repaint at the display's refresh rate forever.
        var emulator = SmoothVt340();

        Assert.False(emulator.TryBeginSmoothScrollLine());
    }

    [Fact]
    public void ThePictureIsNeverAllowedToFallMoreThanAScreenBehind()
    {
        // Something has to give when a host dumps a file. Falling further and further behind for
        // minutes is not a better answer than jumping, so past the limit the content moves on
        // without the picture being asked to walk it.
        var emulator = SmoothVt340();

        ScrollWithoutCatchingUp(emulator, 500);

        Assert.True(emulator.SmoothScrollBacklog <= 24,
            "the picture is " + emulator.SmoothScrollBacklog + " lines behind, more than a screenful"
            + " - a dumped file would leave the viewer watching something minutes old");
    }

    [Fact]
    public void AScreenWithNoHistoryStillAnimatesOneLine()
    {
        // The alternate screen keeps nothing, so there is no older screen to show. Rather than
        // refusing to animate there, one line of lag falls through to the live grid shifted down a
        // row - the approximation this had before the queue existed, and enough for a pager.
        var emulator = SmoothVt340();
        emulator.ProcessData(Encoding.ASCII.GetBytes(Escape + "[?1049h"));

        ScrollWithoutCatchingUp(emulator, 10);

        Assert.Equal(1, emulator.SmoothScrollBacklog);
    }

    [Fact]
    public void BeingBehindIsNotReportedAsTheUserHavingScrolledBack()
    {
        // THE TRAP THIS DESIGN EXISTS TO AVOID. Both push the view back through the same history, so
        // folding the lag into the scroll offset looks like a free simplification. It is not: the
        // scrollback badge appears whenever that number is non-zero, the cursor is suppressed, and
        // selection and search map view rows to buffer rows by subtracting it. Reusing it would
        // flash the badge and misplace every highlight the moment output scrolled.
        var emulator = SmoothVt340();
        ScrollWithoutCatchingUp(emulator, 5);

        emulator.RequestFrame();
        var frame = emulator.LatestFrame!;

        Assert.Equal(5, frame.DisplayLagLines);
        Assert.Equal(0, frame.ScrollOffset);
        Assert.False(frame.IsScrolledBack);
    }

    [Fact]
    public void SwitchingTheModeClearsWhatThePictureOwed()
    {
        // Lines that scrolled while the mode was off were never going to be animated. Carrying the
        // count across would open the next switch-on on a screen several seconds old.
        var emulator = SmoothVt340();
        ScrollWithoutCatchingUp(emulator, 5);
        Assert.Equal(5, emulator.SmoothScrollBacklog);

        emulator.ProcessData(Encoding.ASCII.GetBytes(Escape + "[?4l"));

        Assert.Equal(0, emulator.SmoothScrollBacklog);
    }

    [Fact]
    public void TheCursorTravelsWithThePictureRatherThanTheBuffer()
    {
        // The line the cursor is on has not been revealed yet, so drawing it at its live row would
        // put it several lines above where its text is about to appear. Walking it back by the lag
        // either lands it where it belongs or pushes it off the bottom of the screen, and off the
        // bottom is the honest answer - that line is not visible yet.
        var emulator = SmoothVt340();
        ScrollWithoutCatchingUp(emulator, 5);

        emulator.RequestFrame();

        Assert.Equal(emulator.Cursor.Row + 5, emulator.LatestFrame!.CursorRow);
    }

    /// <summary>
    /// Scrolls a run of lines, letting the picture keep up with every one.
    /// </summary>
    /// <param name="emulator">
    /// The terminal to scroll.
    /// </param>
    /// <param name="lines">
    /// How many lines to push past the bottom of the screen.
    /// </param>
    /// <remarks>
    /// Builds history without building backlog, for tests that need to look further back than the
    /// lag they are about to create.
    /// </remarks>
    private static void ScrollAndCatchUp(TerminalEmulatorBase emulator, int lines)
    {
        // CONTINUES THE NUMBERING rather than calling the other helper, which always starts at
        // LINE 0. Restarting it would put two lines called LINE 3 in one history, and a test that
        // reads a line number to work out how far back it is looking would quietly compare the
        // wrong pair.
        for (int i = 0; i < 23 + lines; i++)
        {
            emulator.ProcessData(Encoding.ASCII.GetBytes("LINE " + i + "\r\n"));
        }

        while (emulator.TryBeginSmoothScrollLine())
        {
        }
    }

    [Fact]
    public void TheRowAboveTheTopFollowsThePictureBack()
    {
        // The gap a sliding screen leaves at the top is filled with the row above it. While the
        // picture is behind, that has to be the row above the LAGGED view - reading the row above
        // the live screen instead would splice a line from several seconds later into the slide.
        var emulator = SmoothVt340();

        // History FIRST, so there is something a line further back than the lag to read. With only
        // five lines ever scrolled, a five-line lag sits on the oldest line there is and the row
        // above it genuinely does not exist.
        ScrollAndCatchUp(emulator, 20);

        // The numbering carries on from where the history left off, so the row above the top and
        // the top row are still consecutive.
        for (int i = 43; i < 48; i++)
        {
            emulator.ProcessData(Encoding.ASCII.GetBytes("LINE " + i + "\r\n"));
        }

        emulator.RequestFrame();
        var frame = emulator.LatestFrame!;

        var text = new StringBuilder();
        for (int col = 0; col < 12; col++)
        {
            uint codepoint = frame.GetCellAboveTop(col).Codepoint;
            if (codepoint != 0) text.Append((char)codepoint);
        }

        Assert.True(frame.HasRowAboveTop);
        Assert.Equal(LineNumberAt(frame, 0) - 1, int.Parse(text.ToString().Trim().Substring(5)));
    }
}
