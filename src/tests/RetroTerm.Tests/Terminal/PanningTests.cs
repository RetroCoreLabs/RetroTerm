using System.Text;
using RetroTerm.Core.Configuration;
using RetroTerm.Core.Terminal.Emulators;
using Xunit;

namespace RetroTerm.Tests.Terminal;

/// <summary>
/// SU and SD - scrolling the region without moving the cursor.
/// </summary>
/// <remarks>
/// Chapter 10 of the VT420 Programmer Reference calls them Pan Down and Pan Up, because the window
/// moves over page memory rather than the text moving under it: "To a user viewing the screen, data
/// appears to scroll in the opposite direction."
///
/// The manual's own wording is what these tests check. SU: "Pn new lines appear at the bottom of
/// the display. Pn old lines disappear at the top." SD is the reverse.
///
/// Neither existed at all until now, which is why five of xterm.js's fixtures disagreed - not only
/// the two named after these sequences, but the save-and-restore pair that happen to use them.
/// </remarks>
public class PanningTests
{
    private static readonly string Esc = ((char)0x1B).ToString();

    private static TerminalEmulatorBase Build(string type = "XTERM")
        => EmulatorFactory.CreateEmulator(type, 10, 5, 100);

    private static void Feed(TerminalEmulatorBase emulator, string text)
        => emulator.ProcessData(Encoding.ASCII.GetBytes(text));

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

    /// <summary>
    /// Fills the five rows with one letter each.
    /// </summary>
    /// <returns>An emulator showing A to E, one per row.</returns>
    private static TerminalEmulatorBase BuildWithRows()
    {
        var emulator = Build();
        Feed(emulator, Esc + "[1;1HA" + Esc + "[2;1HB" + Esc + "[3;1HC");
        Feed(emulator, Esc + "[4;1HD" + Esc + "[5;1HE");
        return emulator;
    }

    [Fact]
    public void ScrollUpTakesLinesOffTheTopAndAddsBlanksAtTheBottom()
    {
        var emulator = BuildWithRows();

        Feed(emulator, Esc + "[2S");

        Assert.Equal("C", Row(emulator, 0));
        Assert.Equal("D", Row(emulator, 1));
        Assert.Equal("E", Row(emulator, 2));
        Assert.Equal("", Row(emulator, 3));
        Assert.Equal("", Row(emulator, 4));
    }

    [Fact]
    public void ScrollDownDoesTheOpposite()
    {
        var emulator = BuildWithRows();

        Feed(emulator, Esc + "[2T");

        Assert.Equal("", Row(emulator, 0));
        Assert.Equal("", Row(emulator, 1));
        Assert.Equal("A", Row(emulator, 2));
        Assert.Equal("B", Row(emulator, 3));
        Assert.Equal("C", Row(emulator, 4));
    }

    [Fact]
    public void OneLineIsTheDefault()
    {
        var emulator = BuildWithRows();

        Feed(emulator, Esc + "[S");

        Assert.Equal("B", Row(emulator, 0));
    }

    [Fact]
    public void AndSoIsAParameterOfZero()
    {
        // "If you omit Pn or use a value of 0, the terminal uses a default value of 1."
        var emulator = BuildWithRows();

        Feed(emulator, Esc + "[0S");

        Assert.Equal("B", Row(emulator, 0));
    }

    [Fact]
    public void TheCursorDoesNotMove()
    {
        // This is what separates panning from an index: the window moves, the cursor stays.
        var emulator = BuildWithRows();
        Feed(emulator, Esc + "[3;4H");

        Feed(emulator, Esc + "[2S");

        Assert.Equal(2, emulator.GetCursor().Row);
        Assert.Equal(3, emulator.GetCursor().Column);
    }

    [Fact]
    public void ScrollingMoreThanTheScreenHoldsLeavesItBlank()
    {
        var emulator = BuildWithRows();

        Feed(emulator, Esc + "[30S");

        for (int row = 0; row < 5; row++)
        {
            Assert.Equal("", Row(emulator, row));
        }
    }

    [Fact]
    public void BothStayInsideTheScrollingRegion()
    {
        var emulator = BuildWithRows();
        Feed(emulator, Esc + "[2;4r");          // region is rows 2..4

        Feed(emulator, Esc + "[1S");

        Assert.Equal("A", Row(emulator, 0));    // outside, untouched
        Assert.Equal("C", Row(emulator, 1));
        Assert.Equal("D", Row(emulator, 2));
        Assert.Equal("", Row(emulator, 3));
        Assert.Equal("E", Row(emulator, 4));    // outside, untouched
    }

    [Fact]
    public void AFullScreenScrollUpKeepsTheDepartingLine()
    {
        // A judgement rather than a quotation: a VT420 has no scrollback, so its manual cannot say.
        // Treating a full-screen SU like a line feed at the bottom is what someone scrolling back
        // to re-read output expects.
        var emulator = BuildWithRows();
        int before = emulator.GetBuffer().ScrollbackLineCount;

        Feed(emulator, Esc + "[1S");

        Assert.Equal(before + 1, emulator.GetBuffer().ScrollbackLineCount);
    }

    [Fact]
    public void AFiveParameterFormIsMouseTrackingRatherThanScrollDown()
    {
        // xterm's highlight mouse tracking shares the final byte. Acting on it as SD would scroll
        // the screen when a program asked to track the pointer.
        var emulator = BuildWithRows();

        Feed(emulator, Esc + "[1;2;3;4;5T");

        Assert.Equal("A", Row(emulator, 0));
        Assert.Equal("E", Row(emulator, 4));
    }

    // ── IL and DL and the pending wrap ──────────────────────────────────────────────────

    [Fact]
    public void InsertLineMovesToTheLineHomePosition()
    {
        // ECMA-48 says the active position moves to the line home position, and so do DEC's own
        // manuals. libvterm's 13state_edit script asserts the opposite and carries its author's
        // note - "ECMA-48 says we should move to line home, but neither xterm nor xfce4-terminal
        // do this" - but that is a claim about xterm, and the xterm.js screens, captured FROM a
        // real xterm, contradict it in four rows across t0051-IL and t0052-DL. The screens win.
        var emulator = BuildWithRows();
        Feed(emulator, Esc + "[2;5H");

        Feed(emulator, Esc + "[L");

        Assert.Equal(0, emulator.GetCursor().Column);
        Assert.Equal(1, emulator.GetCursor().Row);
    }

    [Fact]
    public void AndItDisarmsAPendingWrap()
    {
        // The character that armed the wrap has been moved out from under the cursor, so the wrap
        // no longer means anything. Without this, the next character wrapped onto the line that
        // the insert had just pushed down and overwrote its first column.
        var emulator = Build();
        Feed(emulator, Esc + "[1;1H" + "ABCDEFGHIJ");   // fills the row, arming the wrap
        Feed(emulator, Esc + "[L");
        Feed(emulator, "X");

        // X lands in the FIRST column of the inserted blank line, and the line that was pushed
        // down is untouched - which is what the disarm buys on top of the move home.
        Assert.Equal("X", Row(emulator, 0));
        Assert.Equal("ABCDEFGHIJ", Row(emulator, 1));
    }

    [Fact]
    public void DeleteLineDoesBothOfThoseToo()
    {
        var emulator = Build();
        Feed(emulator, Esc + "[1;1H" + "ABCDEFGHIJ");
        Feed(emulator, Esc + "[2;1H" + "second");
        Feed(emulator, Esc + "[1;1H" + "ABCDEFGHIJ");   // back to row 1, wrap armed again

        Feed(emulator, Esc + "[M");
        Feed(emulator, "X");

        // "second" moved up into row 0, and X overwrote its first column rather than wrapping.
        Assert.Equal("Xecond", Row(emulator, 0));
    }
}
