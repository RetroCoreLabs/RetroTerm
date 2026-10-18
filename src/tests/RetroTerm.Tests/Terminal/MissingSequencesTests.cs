using System.Text;
using RetroTerm.Core.Configuration;
using RetroTerm.Core.Terminal.Emulators;
using Xunit;

namespace RetroTerm.Tests.Terminal;

/// <summary>
/// The last three sequences the ctlseqs sweep found missing - DECST8C, XTSAVE/XTRESTORE, and
/// DECBI/DECFI.
/// </summary>
/// <remarks>
/// <para><b>They were not merely absent - they did the wrong thing</b></para>
/// All three carry the <c>?</c> private marker, and the private-mode handler reads any final byte
/// other than <c>h</c> as a RESET. So before this:
///  - <c>CSI ? 7 s</c>, "remember whether autowrap is on", turned AUTOWRAP OFF. That sequence is
///    xterm's own documented termcap idiom for vi.
///  - <c>CSI ? Ps W</c> reset private mode Ps. With the real DECST8C parameter of 5 that was
///    harmless here only by accident - mode 5 is DECSCNM, which this emulator does not implement -
///    but any other parameter did real damage.
///
/// That makes four final bytes now found in the same hole - <c>n</c> and <c>i</c> were the first
/// two. Each one was a question or a command being read as a mode change.
///
/// <para><b>Where the definitions come from</b></para>
/// <c>spec\DEC\xterm-ctlseqs.txt</c> for DECST8C, XTSAVE and XTRESTORE; the VT420 Programmer
/// Reference at <c>spec\DEC\</c> for DECBI and DECFI, quoted in the source beside each.
/// </remarks>
public class MissingSequencesTests
{
    private static readonly string Esc = ((char)0x1B).ToString();

    private static TerminalEmulatorBase Build(string type = "VT420", int width = 20, int height = 10)
        => EmulatorFactory.CreateEmulator(type, width, height, 100);

    private static void Feed(TerminalEmulatorBase emulator, string text)
        => emulator.ProcessData(Encoding.ASCII.GetBytes(text));

    private static string RowText(TerminalEmulatorBase emulator, int row)
    {
        var text = new StringBuilder();
        for (int col = 0; col < emulator.Width; col++)
        {
            emulator.GetBuffer().TryGetCell(row, col, out var cell);
            text.Append(cell.Codepoint == 0 ? ' ' : (char)cell.Codepoint);
        }

        return text.ToString();
    }

    private static string Ask(TerminalEmulatorBase emulator, string sequence)
    {
        var replies = new StringBuilder();
        emulator.DataToSend += bytes => replies.Append(Encoding.ASCII.GetString(bytes));

        Feed(emulator, Esc + sequence);
        return replies.ToString();
    }

    // ─────────────────────────────────────────────────────────────
    // DECST8C - CSI ? 5 W
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void Decst8CPutsTheTabStopsBackEveryEightColumns()
    {
        // "Reset tab stops to start with column 9, every 8 columns."
        var emulator = Build(width: 40);
        Feed(emulator, Esc + "[3g");        // TBC 3 - clear every tab stop

        Feed(emulator, Esc + "[?5W");

        // From column 1, one tab must land on column 9 and the next on 17.
        Feed(emulator, Esc + "[H\tX");
        Assert.Equal('X', RowText(emulator, 0)[8]);
    }

    [Fact]
    public void TheTabSequenceDoesNotResetAPrivateMode()
    {
        // THE DEFECT, shown with a parameter whose damage can be SEEN. CSI ? Ps W fell into the
        // private-mode switch, where any final byte that is not 'h' means reset - so CSI ? 7 W
        // turned AUTOWRAP OFF.
        //
        // Deliberately not demonstrated with Ps = 5, the real DECST8C parameter: mode 5 is
        // DECSCNM, reverse video, which this emulator DOES NOT IMPLEMENT - ReverseVideoMode is a
        // field nothing writes and nothing reads. So for Ps = 5 the misrouting was latent rather
        // than harmful, and saying otherwise would be a story rather than a fact.
        var emulator = Build();
        Assert.Equal("\x1b[?7;1$y", Ask(emulator, "[?7$p"));

        Feed(emulator, Esc + "[?7W");

        Assert.Equal("\x1b[?7;1$y", Ask(emulator, "[?7$p"));
    }

    [Fact]
    public void AnyOtherParameterToTheTabSequenceIsIgnored()
    {
        // The manual gives one value. Acting on others would be inventing behaviour.
        var emulator = Build(width: 40);
        Feed(emulator, Esc + "[3g");

        Feed(emulator, Esc + "[?2W");

        Feed(emulator, Esc + "[H\tX");
        Assert.Equal('X', RowText(emulator, 0)[emulator.Width - 1]);   // no stop, so tab ran to the end
    }

    // ─────────────────────────────────────────────────────────────
    // XTSAVE and XTRESTORE - CSI ? Pm s and CSI ? Pm r
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void SavingAModeDoesNotChangeIt()
    {
        // THE DEFECT, and the worst of the four: CSI ? 7 s turned autowrap OFF. xterm's own
        // documentation gives that sequence as the termcap idiom for vi.
        var emulator = Build();
        Assert.Equal("\x1b[?7;1$y", Ask(emulator, "[?7$p"));   // autowrap on to begin with

        Feed(emulator, Esc + "[?7s");

        Assert.Equal("\x1b[?7;1$y", Ask(emulator, "[?7$p"));
    }

    [Fact]
    public void RestoringPutsAModeBackToWhatWasSaved()
    {
        var emulator = Build();

        Feed(emulator, Esc + "[?7s");       // save autowrap, which is on
        Feed(emulator, Esc + "[?7l");       // turn it off
        Assert.Equal("\x1b[?7;2$y", Ask(emulator, "[?7$p"));

        Feed(emulator, Esc + "[?7r");

        Assert.Equal("\x1b[?7;1$y", Ask(emulator, "[?7$p"));
    }

    [Fact]
    public void SeveralModesAreSavedAndRestoredTogether()
    {
        var emulator = Build();

        Feed(emulator, Esc + "[?7h" + Esc + "[?25h");
        Feed(emulator, Esc + "[?7;25s");
        Feed(emulator, Esc + "[?7l" + Esc + "[?25l");

        Feed(emulator, Esc + "[?7;25r");

        Assert.Equal("\x1b[?7;1$y", Ask(emulator, "[?7$p"));
        Assert.Equal("\x1b[?25;1$y", Ask(emulator, "[?25$p"));
    }

    [Fact]
    public void OnlyTheModesListedAreRestored()
    {
        // "Only those modes listed as parameters are restored."
        var emulator = Build();

        Feed(emulator, Esc + "[?7;25s");    // save both, both on
        Feed(emulator, Esc + "[?7l" + Esc + "[?25l");

        Feed(emulator, Esc + "[?7r");       // restore only autowrap

        Assert.Equal("\x1b[?7;1$y", Ask(emulator, "[?7$p"));
        Assert.Equal("\x1b[?25;2$y", Ask(emulator, "[?25$p"));
    }

    [Fact]
    public void RestoringAModeThatWasNeverSavedLeavesItAlone()
    {
        // Guessing at a value would change state the host did not ask about.
        var emulator = Build();
        Feed(emulator, Esc + "[?7l");

        Feed(emulator, Esc + "[?7r");

        Assert.Equal("\x1b[?7;2$y", Ask(emulator, "[?7$p"));
    }

    [Fact]
    public void AModeThisTerminalDoesNotImplementIsNotInvented()
    {
        // Saving "false" for an unknown mode would turn a later restore into a reset.
        var emulator = Build();

        Feed(emulator, Esc + "[?12345s");
        Feed(emulator, Esc + "[?12345r");

        // Nothing to assert but the absence of a crash and of a changed screen; the point is that
        // the unknown mode never enters the cache.
        Assert.Equal("\x1b[?7;1$y", Ask(emulator, "[?7$p"));
    }

    // ─────────────────────────────────────────────────────────────
    // DECBI and DECFI - ESC 6 and ESC 9
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void BackIndexMovesTheCursorLeftWhenThereIsRoom()
    {
        var emulator = Build();
        Feed(emulator, Esc + "[1;5H");      // row 1, column 5

        Feed(emulator, Esc + "6");

        // Asked with CPR rather than read off a field: the terminal's own answer is what a host
        // would see, and it is one-based like the sequence that set it.
        Assert.Equal("\x1b[1;4R", Ask(emulator, "[6n"));
    }

    [Fact]
    public void ForwardIndexMovesTheCursorRightWhenThereIsRoom()
    {
        var emulator = Build();
        Feed(emulator, Esc + "[1;5H");

        Feed(emulator, Esc + "9");

        Assert.Equal("\x1b[1;6R", Ask(emulator, "[6n"));
    }

    [Fact]
    public void BackIndexAtTheLeftMarginScrollsTheScreenRight()
    {
        // "If the cursor is at the left margin, all screen data within the margins moves one
        // column to the right. The column shifted past the right margin is lost."
        var emulator = Build(width: 10);
        Feed(emulator, "ABCDEFGHIJ");
        Feed(emulator, Esc + "[1;1H");

        Feed(emulator, Esc + "6");

        Assert.Equal(" ABCDEFGHI", RowText(emulator, 0));
    }

    [Fact]
    public void ForwardIndexAtTheRightMarginScrollsTheScreenLeft()
    {
        // "If the cursor is at the right margin, all screen data within the margins moves one
        // column to the left. The column shifted past the left margin is lost."
        //
        // This is the one that separates a region shift from a column delete at the cursor. Done
        // with DeleteColumns the row would come out "ABCDEFGHI " - everything left of the cursor
        // untouched - instead of losing the A.
        var emulator = Build(width: 10);
        Feed(emulator, "ABCDEFGHIJ");
        Feed(emulator, Esc + "[1;10H");

        Feed(emulator, Esc + "9");

        Assert.Equal("BCDEFGHIJ ", RowText(emulator, 0));
    }

    [Fact]
    public void TheCursorDoesNotMoveWhenTheScreenScrolls()
    {
        var emulator = Build(width: 10);
        Feed(emulator, "ABCDEFGHIJ");
        Feed(emulator, Esc + "[1;1H");

        Feed(emulator, Esc + "6");

        Assert.Equal("\x1b[1;1R", Ask(emulator, "[6n"));
    }

    [Fact]
    public void NeitherIndexIsAvailableOnAVt100()
    {
        // "Available in: VT400 mode only." A VT100 must leave the screen alone.
        var emulator = Build("VT100", width: 10);
        Feed(emulator, "ABCDEFGHIJ");
        Feed(emulator, Esc + "[1;1H");

        Feed(emulator, Esc + "6");

        Assert.Equal("ABCDEFGHIJ", RowText(emulator, 0));
    }
}
