using System.Text;
using RetroTerm.Core.Configuration;
using RetroTerm.Core.Terminal.Emulators;
using Xunit;

namespace RetroTerm.Tests.Terminal;

/// <summary>
/// DECSTR - the soft terminal reset, <c>CSI ! p</c>.
/// </summary>
/// <remarks>
/// <para><b>It was not implemented at all</b></para>
/// The sequence carries a "!" intermediate that nothing matched, so it fell out of the switch and
/// did nothing. That is easy to miss and expensive to have: every curses program sends DECSTR on
/// the way out, and so does tput, so a session left by vim kept whatever modes vim had set -
/// application cursor keys, origin mode, a scrolling region, a hidden cursor.
///
/// <para><b>The rules are table 13-1 of the VT420 Programmer Reference</b></para>
/// Held at spec\DEC. The note beside the table is the half that matters most: "DECSTR affects only
/// those functions listed in Table 13-1." So a soft reset must NOT behave like a hard one.
/// </remarks>
public class SoftTerminalResetTests
{
    private static readonly string Esc = ((char)0x1B).ToString();

    private static TerminalEmulatorBase Build(string type = "VT420")
        => EmulatorFactory.CreateEmulator(type, 20, 10, 100);

    private static void Feed(TerminalEmulatorBase emulator, string text)
        => emulator.ProcessData(Encoding.ASCII.GetBytes(text));

    /// <summary>
    /// Asks the terminal a question and collects the answer, so the tests read the state the way a
    /// host would rather than reaching into the emulator.
    /// </summary>
    /// <param name="emulator">
    /// The terminal to ask.
    /// </param>
    /// <param name="sequence">
    /// The request, without the leading escape.
    /// </param>
    /// <returns>
    /// The reply as text.
    /// </returns>
    private static string Ask(TerminalEmulatorBase emulator, string sequence)
    {
        var replies = new StringBuilder();
        emulator.DataToSend += bytes => replies.Append(Encoding.ASCII.GetString(bytes));

        Feed(emulator, Esc + sequence);
        return replies.ToString();
    }

    private static string Row(TerminalEmulatorBase emulator, int row)
    {
        var text = new StringBuilder(emulator.Width);
        for (int col = 0; col < emulator.Width; col++)
        {
            emulator.GetBuffer().TryGetCell(row, col, out var cell);
            text.Append(cell.Codepoint == 0 ? '.' : (char)cell.Codepoint);
        }
        return text.ToString().TrimEnd('.');
    }

    [Fact]
    public void TheCursorComesBack()
    {
        // "Text cursor enable DECTCEM: Cursor enabled."
        var emulator = Build();
        Feed(emulator, Esc + "[?25l");

        Feed(emulator, Esc + "[!p");

        Assert.True(emulator.GetCursor().Visible);
    }

    [Fact]
    public void TheScrollingRegionGoesBackToTheWholePage()
    {
        // "Set top and bottom margins DECSTBM: Top margin = 1. Bottom margin = page length."
        var emulator = Build();
        Feed(emulator, Esc + "[3;6r");

        Feed(emulator, Esc + "[!p");

        // Read back the way a host would, with DECRQSS.
        Assert.Equal(Esc + "P1$r1;10r" + Esc + "\\", Ask(emulator, "P$qr" + Esc + "\\"));
    }

    [Fact]
    public void OriginModeGoesOff()
    {
        // "Origin DECOM: Absolute (cursor origin at upper-left of screen)."
        var emulator = Build();
        Feed(emulator, Esc + "[?6h");

        Feed(emulator, Esc + "[!p");

        Assert.Equal(Esc + "[?6;2$y", Ask(emulator, "[?6$p"));
    }

    [Fact]
    public void AndSoDoesAutowrap()
    {
        // "Autowrap DECAWM: No autowrap." Note the direction: DECSTR turns it OFF, it does not
        // put it back on. That is the manual's word, not an inference.
        var emulator = Build();
        Feed(emulator, Esc + "[?7h");

        Feed(emulator, Esc + "[!p");

        Assert.Equal(Esc + "[?7;2$y", Ask(emulator, "[?7$p"));
    }

    [Fact]
    public void TheCursorKeysGoBackToNormal()
    {
        // "Cursor keys DECCKM: Normal (arrow keys)."
        var emulator = Build();
        Feed(emulator, Esc + "[?1h");

        Feed(emulator, Esc + "[!p");

        Assert.Equal(Esc + "[?1;2$y", Ask(emulator, "[?1$p"));
    }

    [Fact]
    public void TheRenditionGoesBackToNormal()
    {
        // "Select graphic rendition SGR: Normal rendition."
        var emulator = Build();
        Feed(emulator, Esc + "[1;4;7m");

        Feed(emulator, Esc + "[!p");

        Assert.Equal(Esc + "P1$r0m" + Esc + "\\", Ask(emulator, "P$qm" + Esc + "\\"));
    }

    [Fact]
    public void InsertModeGoesBackToReplace()
    {
        // "Insert/replace IRM: Replace."
        var emulator = Build();
        Feed(emulator, Esc + "[1;1HABC");
        Feed(emulator, Esc + "[4h");            // IRM on

        Feed(emulator, Esc + "[!p");
        Feed(emulator, Esc + "[1;1HZ");

        Assert.Equal("ZBC", Row(emulator, 0));
    }

    [Fact]
    public void TheSavedCursorGoesHome()
    {
        // "Save cursor state DECSC: Home position with VT420 defaults." So a DECRC after a soft
        // reset lands at the top left rather than wherever DECSC last was.
        var emulator = Build();
        Feed(emulator, Esc + "[5;9H" + Esc + "7");

        Feed(emulator, Esc + "[!p");
        Feed(emulator, Esc + "[3;3H" + Esc + "8");

        Assert.Equal(0, emulator.GetCursor().Row);
        Assert.Equal(0, emulator.GetCursor().Column);
    }

    [Fact]
    public void ItExitsTheStatusLine()
    {
        // The VT330/VT340 manual's own table of exceptions: "Soft terminal reset (DECSTR): Exits
        // status line."
        var emulator = Build();
        Feed(emulator, Esc + "[2$~" + Esc + "[1$}");

        Feed(emulator, Esc + "[!p");
        Feed(emulator, "BACK");

        Assert.False(emulator.WritingToStatusLine);
        Assert.Equal("BACK", Row(emulator, 0));
    }

    // ── And what it must NOT do ─────────────────────────────────────────────────────────────

    [Fact]
    public void ItDoesNotClearTheScreen()
    {
        // THE test that separates a soft reset from a hard one. "DECSTR affects only those
        // functions listed in Table 13-1", and the screen is not one of them - a program that
        // reset the terminal on the way out would otherwise wipe the user's scrollback and
        // whatever was on screen.
        var emulator = Build();
        Feed(emulator, Esc + "[1;1HKEEP ME");

        Feed(emulator, Esc + "[!p");

        Assert.Equal("KEEP ME", Row(emulator, 0));
    }

    [Fact]
    public void AndItDoesNotForgetTheDownloadedCharacterSetOrTheKeys()
    {
        // Neither is in table 13-1. Only RIS throws these away.
        var emulator = Build();
        Feed(emulator, "\x1bP1;1;1;8;0;0;12;0{ @~\x1b\\");     // DECDLD
        Feed(emulator, "\x1bP1;1|17/6c73\x1b\\");              // DECUDK

        Feed(emulator, Esc + "[!p");

        Assert.Equal(1, emulator.SoftFont.Count);
        Assert.Equal(1, emulator.UserKeys.Count);
    }

    [Fact]
    public void AndItDoesNotMoveTheCursor()
    {
        // Table 13-1 lists settings, not the display, and nothing in it says the cursor goes home.
        var emulator = Build();
        Feed(emulator, Esc + "[4;7H");

        Feed(emulator, Esc + "[!p");

        Assert.Equal(3, emulator.GetCursor().Row);
        Assert.Equal(6, emulator.GetCursor().Column);
    }

    [Fact]
    public void AndItLeavesTheLeftAndRightMarginsAlone()
    {
        // NOT in table 13-1, and the note says the list is the whole of it. xterm clears them; no
        // document held here says to, so the manual wins and the disagreement is written down in
        // SoftReset rather than split down the middle.
        var emulator = Build();
        Feed(emulator, Esc + "[?69h" + Esc + "[5;15s");

        Feed(emulator, Esc + "[!p");

        Assert.Equal(Esc + "P1$r5;15s" + Esc + "\\", Ask(emulator, "P$qs" + Esc + "\\"));
    }
}
