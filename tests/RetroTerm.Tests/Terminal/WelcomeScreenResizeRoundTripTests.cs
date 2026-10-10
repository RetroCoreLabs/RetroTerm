using System.Text;
using RetroTerm.Core.Configuration;
using RetroTerm.Core.Terminal.Emulators;
using RetroTerm.Desktop;
using Xunit;

namespace RetroTerm.Tests.Terminal;

/// <summary>
/// Shrinking the window very small and growing it again must give the screen back, history
/// included.
/// </summary>
/// <remarks>
/// <para><b>Ronny, 10 October 2026</b></para>
/// After dragging the window very small and then large again, the welcome screen showed its text
/// wrapped at about 27 columns in a wide window, the logo cut to pieces, and a status bar saying
/// "Scrollback: 42 lines back".
/// <para></para>
/// <b>Cause.</b> <c>TerminalBuffer.ResizeWithReflow</c> re-laid out only the SCREEN. Narrowing the
/// window pushed the overflowing rows into scrollback at the narrow width, and growing it again
/// never reached them: they stayed wrapped at that width in the history, and the screen showed only
/// the tail of what had been there. Reflow now takes the history and the screen as one document.
/// </remarks>
public class WelcomeScreenResizeRoundTripTests
{
    private static string Screen(TerminalEmulatorBase emulator)
    {
        var sb = new StringBuilder();
        for (int row = 0; row < emulator.Height; row++)
        {
            var line = new StringBuilder();
            for (int col = 0; col < emulator.Width; col++)
            {
                uint cp = emulator.Buffer.GetCell(row, col).Codepoint;
                line.Append(cp == 0 ? ' ' : char.ConvertFromUtf32((int)cp));
            }

            sb.Append(line.ToString().TrimEnd()).Append('\n');
        }

        return sb.ToString();
    }

    /// <summary>
    /// The whole text, scrollback first and then the screen, one logical line per hard line break.
    /// </summary>
    private static string Document(TerminalEmulatorBase emulator)
    {
        var sb = new StringBuilder();
        var buffer = emulator.Buffer;

        for (int i = 0; i < buffer.ScrollbackLineCount; i++)
        {
            var line = buffer.GetScrollbackLine(i);
            var text = new StringBuilder();
            if (line != null)
            {
                for (int col = 0; col < line.Length; col++)
                {
                    text.Append(line[col].Codepoint == 0 ? ' ' : char.ConvertFromUtf32((int)line[col].Codepoint));
                }
            }

            bool continues = buffer.IsScrollbackLineWrapped(i);
            sb.Append(continues ? text.ToString() : text.ToString().TrimEnd() + "\n");
        }

        for (int row = 0; row < emulator.Height; row++)
        {
            var text = new StringBuilder();
            for (int col = 0; col < emulator.Width; col++)
            {
                text.Append(buffer.GetCell(row, col).Codepoint == 0 ? ' ' : char.ConvertFromUtf32((int)buffer.GetCell(row, col).Codepoint));
            }

            bool continues = row < emulator.Height - 1 && buffer.IsLineWrapped(row);
            sb.Append(continues ? text.ToString() : text.ToString().TrimEnd() + "\n");
        }

        return sb.ToString().TrimEnd('\n');
    }

    private static TerminalEmulatorBase NewWelcomeEmulator()
    {
        var emulator = EmulatorFactory.CreateEmulator("VT100", 110, 40);
        emulator.ProcessData(Encoding.UTF8.GetBytes(MainWindow.WelcomeText().Replace("\r\n", "\n").Replace("\n", "\r\n")));
        return emulator;
    }

    [Fact]
    public void ShrinkingToTheFloorAndGrowingBackGivesTheWelcomeScreenBack()
    {
        var emulator = NewWelcomeEmulator();
        string before = Screen(emulator);

        emulator.Resize(20, 24);
        emulator.Resize(110, 40);

        Assert.Equal(before, Screen(emulator));
        Assert.Equal(0, emulator.Buffer.ScrollbackLineCount);
    }

    [Fact]
    public void TheHistoryIsRewrappedToo_NotLeftAtTheNarrowWidth()
    {
        var emulator = NewWelcomeEmulator();
        string documentBefore = Document(emulator);

        emulator.Resize(20, 24);
        emulator.Resize(40, 24);
        emulator.Resize(100, 30);

        // Nothing lost, nothing joined that was not joined, wherever it now sits.
        Assert.Equal(documentBefore, Document(emulator));
    }

    [Fact]
    public void ResizingNarrowerAndWiderRepeatedlyLosesNothing()
    {
        var emulator = NewWelcomeEmulator();
        string documentBefore = Document(emulator);

        int[] widths = { 60, 20, 33, 110, 25, 80, 20, 140, 90 };
        for (int i = 0; i < widths.Length; i++)
        {
            emulator.Resize(widths[i], 30);
        }

        Assert.Equal(documentBefore, Document(emulator));
    }

    [Fact]
    public void OldLongHistoryLinesAreNotWrappedByAnIntermediateWidth()
    {
        // A line written at 40 columns and met again at 20 on the way back to 40 must not be
        // wrapped into two rows: the ring holds a fixed number of ROWS, so wrapping every old line
        // at every width the window passes through deletes the oldest history of a full ring. Only
        // what a shrink wrapped is rejoined.
        var emulator = EmulatorFactory.CreateEmulator("VT100", 40, 6, 1000);
        string longLine = "0123456789ABCDEFGHIJ0123456789ABCDEF"; // 36 characters
        for (int i = 0; i < 12; i++)
        {
            emulator.ProcessData(Encoding.ASCII.GetBytes(longLine + "\r\n"));
        }

        int historyBefore = emulator.Buffer.ScrollbackLineCount;
        var oldest = emulator.Buffer.GetScrollbackLine(0);
        Assert.NotNull(oldest);

        emulator.Resize(10, 6);   // narrow: the screen's rows wrap into history
        emulator.Resize(20, 6);   // wider, still narrower than the old lines
        var oldestNow = emulator.Buffer.GetScrollbackLine(0);

        Assert.NotNull(oldestNow);
        Assert.Equal(oldest!.Length, oldestNow!.Length);
        Assert.True(historyBefore > 0);

        emulator.Resize(40, 6);   // back where it started

        Assert.Equal(longLine, Screen(emulator).Split('\n')[4]);
    }

    [Fact]
    public void TheViewPositionDoesNotPointPastTheHistoryAfterAResize()
    {
        // "Scrollback: 42 lines back" while only a few lines of history remain would show nothing.
        var emulator = NewWelcomeEmulator();
        emulator.Resize(20, 24);
        emulator.ViewScrollOffset = emulator.Buffer.ScrollbackLineCount;

        emulator.Resize(110, 40);

        Assert.True(emulator.ViewScrollOffset <= emulator.Buffer.ScrollbackLineCount);
    }
}
