using System.Text;
using RetroTerm.Core.Configuration;
using RetroTerm.Core.Terminal.Emulators;
using Xunit;

namespace RetroTerm.Tests.Terminal;

/// <summary>
/// ED 3 - Erase Saved Lines, which erases the history and NOT the screen.
/// </summary>
/// <remarks>
/// xterm's ctlseqs.txt lists them as separate things: Ps 2 is "Erase All" and Ps 3 is "Erase Saved
/// Lines". Ours ran the two together, so a host asking to trim its scrollback also lost everything
/// the user was reading. Found by xterm.js's t0057 fixture, where xterm keeps a full screen of text
/// and we showed twenty blank rows.
/// </remarks>
public class EraseSavedLinesTests
{
    private static readonly string Esc = ((char)0x1B).ToString();

    private static TerminalEmulatorBase Build()
        => EmulatorFactory.CreateEmulator("XTERM", 20, 4, 100);

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
    /// Writes enough lines to push some into the scrollback.
    /// </summary>
    /// <returns>An emulator with history behind it and four lines on screen.</returns>
    private static TerminalEmulatorBase BuildWithHistory()
    {
        var emulator = Build();
        for (int i = 1; i <= 8; i++)
        {
            Feed(emulator, "line " + i + "\r\n");
        }
        Feed(emulator, "ON SCREEN");
        return emulator;
    }

    [Fact]
    public void ItErasesTheHistory()
    {
        var emulator = BuildWithHistory();
        Assert.True(emulator.GetBuffer().ScrollbackLineCount > 0);

        Feed(emulator, Esc + "[3J");

        Assert.Equal(0, emulator.GetBuffer().ScrollbackLineCount);
    }

    [Fact]
    public void AndLeavesTheScreenAlone()
    {
        // THE defect. "Erase Saved Lines" and "Erase All" are different commands, and running them
        // together means a host trimming its history also wipes what the user is reading.
        var emulator = BuildWithHistory();

        Feed(emulator, Esc + "[3J");

        Assert.Equal("ON SCREEN", Row(emulator, 3));
        Assert.Equal("line 6", Row(emulator, 0));
    }

    [Fact]
    public void EraseAllStillClearsTheScreen()
    {
        // The guard on the other side: Ps 2 must keep doing what it always did.
        var emulator = BuildWithHistory();

        Feed(emulator, Esc + "[2J");

        Assert.Equal("", Row(emulator, 0));
        Assert.Equal("", Row(emulator, 3));
    }

    [Fact]
    public void AndEraseAllLeavesTheHistoryWhereItIs()
    {
        // The mirror of the defect: clearing the screen is not clearing the history either.
        var emulator = BuildWithHistory();
        int before = emulator.GetBuffer().ScrollbackLineCount;

        Feed(emulator, Esc + "[2J");

        Assert.Equal(before, emulator.GetBuffer().ScrollbackLineCount);
    }

    [Fact]
    public void TheSelectiveFormErasesTheHistoryToo()
    {
        // CSI ? 3 J - "Selective Erase Saved Lines". Nothing in the history is protected, because
        // protection describes what is on screen now, so it behaves as the plain form.
        var emulator = BuildWithHistory();

        Feed(emulator, Esc + "[?3J");

        Assert.Equal(0, emulator.GetBuffer().ScrollbackLineCount);
        Assert.Equal("ON SCREEN", Row(emulator, 3));
    }

    [Fact]
    public void TheSelectiveEraseAllStillSparesProtectedText()
    {
        var emulator = Build();
        Feed(emulator, Esc + "[1\"q" + Esc + "[1;1H" + "KEEP");
        Feed(emulator, Esc + "[0\"q" + Esc + "[2;1H" + "GONE");

        Feed(emulator, Esc + "[?2J");

        Assert.Equal("KEEP", Row(emulator, 0));
        Assert.Equal("", Row(emulator, 1));
    }
}
