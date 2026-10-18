using System;
using System.Text;
using RetroTerm.Core.Terminal;
using RetroTerm.Core.Terminal.Buffer;
using RetroTerm.Core.Terminal.Emulators;
using RetroTerm.Core.Terminal.Rendering;
using Xunit;

namespace RetroTerm.Tests.Terminal;

/// <summary>
/// Phase 2b part 2: the frame handoff between the session pump and the renderer (problem B.9).
///
/// The renderer used to walk the live TerminalCell[,] from the UI thread while the pump wrote into
/// it. Two things go wrong there and both are real: TerminalCell is a 20-byte struct, so a torn
/// read shows a character wearing another cell's colours; and a concurrent Resize swaps the whole
/// array, so the indexer can throw in the middle of a paint.
///
/// The fix is structural rather than defensive - no locks, no retries. The pump captures a frame
/// and publishes it; the renderer only ever reads a published frame, and nothing mutates a frame
/// after publication. These tests pin the properties the renderer now depends on.
/// </summary>
public class ScreenFrameHandoffTests
{
    private static void Feed(TerminalEmulatorBase emulator, string s)
    {
        emulator.ProcessData(Encoding.ASCII.GetBytes(s));
    }

    private static string RowOf(ScreenFrame frame, int row)
    {
        var sb = new StringBuilder();
        for (int col = 0; col < frame.Width; col++)
        {
            // Codepoint 0 is a cell that was never written. It renders as a space everywhere
            // else, so it must here too — appending the raw 0 put NUL characters in the string
            // and TrimEnd, which only trims whitespace, left them all in place.
            uint cp = frame[row, col].Codepoint;
            sb.Append(cp == 0 ? ' ' : (char)cp);
        }
        return sb.ToString().TrimEnd();
    }

    // ─────────────────────────────────────────────────────────────
    // Publication
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void ProcessingDataPublishesAFrame()
    {
        var emulator = new VT100Emulator(20, 5);

        Feed(emulator, "HELLO");

        Assert.NotNull(emulator.LatestFrame);
        Assert.Equal("HELLO", RowOf(emulator.LatestFrame!, 0));
    }

    [Fact]
    public void TheFrameCarriesTheScreenDimensions()
    {
        var emulator = new VT100Emulator(20, 5);
        Feed(emulator, "X");

        Assert.Equal(20, emulator.LatestFrame!.Width);
        Assert.Equal(5, emulator.LatestFrame!.Height);
    }

    [Fact]
    public void TheFrameCarriesTheCursorState()
    {
        var emulator = new VT100Emulator(20, 5);

        Feed(emulator, "\x1b[3;7H");

        var frame = emulator.LatestFrame!;
        Assert.Equal(2, frame.CursorRow);
        Assert.Equal(6, frame.CursorColumn);
        Assert.True(frame.CursorVisible);
    }

    [Fact]
    public void HidingTheCursorShowsInTheFrame()
    {
        var emulator = new VT100Emulator(20, 5);

        Feed(emulator, "\x1b[?25l");

        Assert.False(emulator.LatestFrame!.CursorVisible);
    }

    [Fact]
    public void ResizingPublishesAFrameOfTheNewSize()
    {
        var emulator = new VT100Emulator(20, 5);
        Feed(emulator, "X");

        emulator.Resize(30, 10);

        Assert.Equal(30, emulator.LatestFrame!.Width);
        Assert.Equal(10, emulator.LatestFrame!.Height);
    }

    // ─────────────────────────────────────────────────────────────
    // The property the whole design rests on
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void APublishedFrameDoesNotChangeWhenTheScreenDoes()
    {
        // This is the point. A renderer takes the reference once and draws from it; if later
        // writes could reach through it, the frame would be no better than the live buffer.
        var emulator = new VT100Emulator(20, 5);
        Feed(emulator, "FIRST");

        var held = emulator.LatestFrame!;
        Assert.Equal("FIRST", RowOf(held, 0));

        Feed(emulator, "\x1b[1;1HSECOND");

        Assert.Equal("FIRST", RowOf(held, 0));
        Assert.Equal("SECOND", RowOf(emulator.LatestFrame!, 0));
    }

    [Fact]
    public void RepeatedPublishesNeverWriteIntoTheFrameThatIsCurrentlyPublished()
    {
        // The pool must skip the published frame, or a renderer holding it would watch its own
        // picture change underneath it. Many publishes in a row is where a rotation bug shows.
        var emulator = new VT100Emulator(20, 5);

        for (int i = 0; i < 25; i++)
        {
            var before = emulator.LatestFrame;
            Feed(emulator, "\x1b[1;1HX" + i);
            Assert.NotSame(before, emulator.LatestFrame);
        }
    }

    [Fact]
    public void AFrameHeldAcrossAResizeKeepsItsOwnDimensions()
    {
        // The crash case: the buffer swapping its array must not be visible through a frame that
        // was handed out before the resize.
        var emulator = new VT100Emulator(20, 5);
        Feed(emulator, "HELLO");
        var held = emulator.LatestFrame!;

        emulator.Resize(40, 12);

        Assert.Equal(20, held.Width);
        Assert.Equal(5, held.Height);
        Assert.Equal("HELLO", RowOf(held, 0));
    }

    [Fact]
    public void ReadingOutsideAFrameReturnsBlankRatherThanThrowing()
    {
        // A picture asked about a position it does not have should draw nothing, not fall over -
        // the renderer's loop bounds and the frame can disagree for one paint after a resize.
        var emulator = new VT100Emulator(10, 3);
        Feed(emulator, "X");
        var frame = emulator.LatestFrame!;

        Assert.True(frame[-1, 0].IsEmpty);
        Assert.True(frame[0, -1].IsEmpty);
        Assert.True(frame[99, 0].IsEmpty);
        Assert.True(frame[0, 99].IsEmpty);
    }

    // ─────────────────────────────────────────────────────────────
    // Scrollback is resolved on the capturing side
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void AScrolledBackFrameShowsHistory()
    {
        // Resolving history during capture is what lets the renderer stop touching the scrollback
        // ring, which recycles its rows in place and so was racy to read from another thread.
        var emulator = new VT100Emulator(20, 3);
        Feed(emulator, "ONE\r\nTWO\r\nTHREE\r\nFOUR");

        Assert.True(emulator.Buffer.ScrollbackLineCount > 0);

        emulator.ViewScrollOffset = 1;
        emulator.PublishFrame();

        var frame = emulator.LatestFrame!;
        Assert.True(frame.IsScrolledBack);
        Assert.Equal(1, frame.ScrollOffset);
        Assert.Equal("ONE", RowOf(frame, 0));
    }

    [Fact]
    public void ALiveFrameIsNotMarkedScrolledBack()
    {
        var emulator = new VT100Emulator(20, 3);
        Feed(emulator, "X");

        Assert.False(emulator.LatestFrame!.IsScrolledBack);
        Assert.Equal(0, emulator.LatestFrame!.ScrollOffset);
    }

    [Fact]
    public void TheFrameReportsHowMuchHistoryExisted()
    {
        var emulator = new VT100Emulator(20, 3);
        Feed(emulator, "A\r\nB\r\nC\r\nD\r\nE");

        Assert.Equal(emulator.Buffer.ScrollbackLineCount, emulator.LatestFrame!.ScrollbackLineCount);
    }

    // ─────────────────────────────────────────────────────────────
    // Requesting a frame from outside the pump
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void RequestFrameWithNoSubscriberCapturesDirectly()
    {
        // A bare emulator has no pump to race with, so capturing inline is correct.
        var emulator = new VT100Emulator(20, 5);
        emulator.Buffer.SetCell(0, 0, new TerminalCell('Z'));

        emulator.RequestFrame();

        Assert.Equal('Z', (char)emulator.LatestFrame![0, 0].Codepoint);
    }

    [Fact]
    public void RequestFrameWithASubscriberDelegatesInsteadOfCapturingItself()
    {
        // With a session attached, capture must happen on the pump - never on the calling thread.
        var emulator = new VT100Emulator(20, 5);
        Feed(emulator, "BEFORE");
        var beforeRequest = emulator.LatestFrame;

        int calls = 0;
        emulator.FrameRequested += () => calls++;

        emulator.Buffer.SetCell(0, 0, new TerminalCell('Z'));
        emulator.RequestFrame();

        Assert.Equal(1, calls);
        // The subscriber decides when to capture, so nothing was published on this thread.
        Assert.Same(beforeRequest, emulator.LatestFrame);
    }

    [Fact]
    public void ScreenFrameRejectsANullBuffer()
    {
        var frame = new ScreenFrame();
        var cursor = new Cursor(5, 20);

        Assert.Throws<ArgumentNullException>(() => frame.CaptureFrom(null!, cursor, 0));
    }

    [Fact]
    public void ANegativeScrollOffsetIsTreatedAsLive()
    {
        var emulator = new VT100Emulator(20, 5);
        Feed(emulator, "HELLO");

        emulator.ViewScrollOffset = -5;
        emulator.PublishFrame();

        Assert.Equal(0, emulator.LatestFrame!.ScrollOffset);
        Assert.Equal("HELLO", RowOf(emulator.LatestFrame!, 0));
    }
}
