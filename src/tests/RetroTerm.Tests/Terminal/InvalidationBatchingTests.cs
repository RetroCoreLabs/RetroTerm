using System.Text;
using RetroTerm.Core.Terminal.Emulators;
using Xunit;

namespace RetroTerm.Tests.Terminal;

/// <summary>
/// One chunk of host data costs ONE published frame and ONE repaint, however many escape
/// sequences it carries.
///
/// WHAT WAS WRONG. Twenty-nine places in this codebase call <c>OnInvalidated</c>, and each one
/// published a whole fresh frame — <c>PublishFrame</c> copies every cell on screen, 1,920 of them
/// for an 80x24 — and raised Invalidated, which the canvas answers by posting a repaint to the UI
/// thread. A single read from a host commonly carries dozens of sequences. A TDV host repainting a
/// form is the worst case: the 2200 raises Invalidated from ten different handlers, so one form
/// could copy the screen and queue a repaint dozens of times to show one final picture.
///
/// Nothing outside could ever see those intermediate frames anyway. The frame is read only by the
/// renderer, on another thread, whenever it happens to draw — so publishing the in-between states
/// was pure cost with no observer.
///
/// This is about cost, so these tests COUNT rather than look at the screen. What the screen ends
/// up showing is the rest of the suite's job, and it has to keep passing unchanged.
/// </summary>
public class InvalidationBatchingTests
{
    private static void Feed(TerminalEmulatorBase emulator, string text)
        => emulator.ProcessData(Encoding.ASCII.GetBytes(text));

    /// <summary>
    /// Twenty DECDWL sequences. Each one lands in SetLineWidth, which is one of the handlers that
    /// asks for a repaint, so this chunk used to cost twenty-one frames.
    /// </summary>
    private const string TwentyRepaintingSequences =
        "\u001b#6\u001b#6\u001b#6\u001b#6\u001b#6\u001b#6\u001b#6\u001b#6\u001b#6\u001b#6" +
        "\u001b#6\u001b#6\u001b#6\u001b#6\u001b#6\u001b#6\u001b#6\u001b#6\u001b#6\u001b#6";

    [Fact]
    public void OneChunkRaisesOneInvalidation()
    {
        // THE test. Twenty-one before the change, one after.
        var emulator = new VT100Emulator(20, 4);
        int count = 0;
        emulator.Invalidated += () => count++;

        Feed(emulator, TwentyRepaintingSequences);

        Assert.Equal(1, count);
    }

    [Fact]
    public void EachChunkStillGetsItsOwnInvalidation()
    {
        // The guard against overreach: batching must not swallow anything ACROSS chunks, or a
        // terminal would stop repainting between reads and the screen would freeze mid-output.
        var emulator = new VT100Emulator(20, 4);
        int count = 0;
        emulator.Invalidated += () => count++;

        Feed(emulator, "A");
        Feed(emulator, "B");
        Feed(emulator, "C");

        Assert.Equal(3, count);
    }

    [Fact]
    public void AChunkThatDrawsNothingStillRepaints()
    {
        // The cursor lives in the frame too, so a chunk that only moved it has to republish.
        var emulator = new VT100Emulator(20, 4);
        int count = 0;
        emulator.Invalidated += () => count++;

        Feed(emulator, "\u001b[2;5H");

        Assert.Equal(1, count);
    }

    [Fact]
    public void ThePublishedFrameShowsTheFinalStateOfTheChunk()
    {
        // Batching decides WHEN the frame is published, never WHAT it holds. If the single frame
        // were captured early it would show a half-drawn screen and stay that way until the next
        // read from the host.
        var emulator = new VT100Emulator(20, 4);

        Feed(emulator, "first\r\u001b[Ksecond");

        var frame = emulator.LatestFrame;
        Assert.NotNull(frame);

        var text = new StringBuilder();
        for (int col = 0; col < 6; col++)
        {
            uint cp = frame![0, col].Codepoint;
            text.Append(cp == 0 ? ' ' : (char)cp);
        }

        Assert.Equal("second", text.ToString());
    }

    [Fact]
    public void TheTdvPathIsBatchedToo()
    {
        // TDVEmulatorBase overrides ProcessData to bypass the base class entirely, so it needs its
        // own batch - a fix applied only to the base would have left the emulators that repaint
        // hardest still doing it per sequence. The two-surface trap, one layer down.
        var emulator = new RetroTerm.Core.Terminal.Emulators.TDV.TDV2200Emulator(20, 4);
        int count = 0;
        emulator.Invalidated += () => count++;

        Feed(emulator, TwentyRepaintingSequences);

        Assert.Equal(1, count);
    }
}
