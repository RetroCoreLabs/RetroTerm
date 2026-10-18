using System.Text;
using RetroTerm.Core.Configuration;
using RetroTerm.Core.Terminal.Emulators;
using Xunit;

namespace RetroTerm.Tests.Terminal;

/// <summary>
/// The twenty-fifth line - DECSSDT and DECSASD.
/// </summary>
/// <remarks>
/// <para><b>Where the rules come from</b></para>
/// The VT330/VT340 Text Programming manual (EK-VT3XX-TP-001), chapter 11: "The twenty-fifth line at
/// the bottom of the screen is reserved for the status line. The terminal lets you use the status
/// line in two ways - as an indicator of the terminal's current state, or as a window the host can
/// use to display application-specific messages."
///
/// <para><b>What is deliberately NOT tested here, because it is not built</b></para>
/// The geometry. A real VT340 shows 24 lines of main display with the status line as a 25th below
/// them; here the status line is held separately and the main display never gives up a row, because
/// this emulator's height comes from the window. Nothing draws it yet either - whether a status
/// line LOOKS right is not a question any test in this repository can answer.
///
/// The indicator line is not built at all: it is the terminal talking about itself, and half of
/// what a VT340 puts there - modem state, dual sessions - has no counterpart here.
/// </remarks>
public class StatusLineTests
{
    private static readonly string Esc = ((char)0x1B).ToString();

    private static TerminalEmulatorBase Build(string type = "VT340")
        => EmulatorFactory.CreateEmulator(type, 20, 6, 100);

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

    [Fact]
    public void TheIndicatorLineIsWhatTheTerminalPowersOnWith()
    {
        // "This status line is enabled by default."
        var emulator = Build();

        Assert.Equal(TerminalEmulatorBase.StatusLineKind.Indicator, emulator.StatusLineType);
        Assert.False(emulator.WritingToStatusLine);
    }

    [Fact]
    public void TheHostCanAskForAWritableOneAndWriteToIt()
    {
        var emulator = Build();

        Feed(emulator, Esc + "[2$~");        // DECSSDT 2 - host-writable
        Feed(emulator, Esc + "[1$}");        // DECSASD 1 - send data there
        Feed(emulator, "READY");

        Assert.Equal("READY", emulator.StatusLineText);
    }

    [Fact]
    public void AndNoneOfThatReachesTheScreen()
    {
        // THE point of the feature: the main display is untouched while the status line is active.
        var emulator = Build();
        Feed(emulator, Esc + "[1;1HMAIN");

        Feed(emulator, Esc + "[2$~" + Esc + "[1$}");
        Feed(emulator, "STATUS");

        Assert.Equal("MAIN", Row(emulator, 0));
        for (int row = 1; row < 6; row++)
        {
            Assert.Equal("", Row(emulator, row));
        }
    }

    [Fact]
    public void GoingBackToTheMainDisplayResumesWhereItLeftOff()
    {
        // The main display's cursor is not touched while the status line has the output, so text
        // carries on from where it was.
        var emulator = Build();
        Feed(emulator, Esc + "[1;1HAB");

        Feed(emulator, Esc + "[2$~" + Esc + "[1$}");
        Feed(emulator, "xx");
        Feed(emulator, Esc + "[0$}");        // DECSASD 0 - back to the main display
        Feed(emulator, "CD");

        Assert.Equal("ABCD", Row(emulator, 0));
        Assert.Equal("xx", emulator.StatusLineText);
    }

    [Fact]
    public void ChangingTheKindOfLineEmptiesIt()
    {
        // "If you change from an indicator to a host-writable status line, the new host-writable
        // status line is empty."
        var emulator = Build();
        Feed(emulator, Esc + "[2$~" + Esc + "[1$}");
        Feed(emulator, "OLD");

        Feed(emulator, Esc + "[1$~");        // back to the indicator line
        Feed(emulator, Esc + "[2$~");        // and to a host-writable one again

        Assert.Equal("", emulator.StatusLineText);
    }

    [Fact]
    public void AndTakesTheOutputBackWithIt()
    {
        // An assumption, marked as one in SelectStatusLineType: the manual says what each of the
        // two functions does but not what happens to one when the other changes underneath it.
        // Leaving output pointed at a line that no longer accepts it would swallow it silently.
        var emulator = Build();
        Feed(emulator, Esc + "[2$~" + Esc + "[1$}");
        Assert.True(emulator.WritingToStatusLine);

        Feed(emulator, Esc + "[0$~");        // no status line at all now
        Feed(emulator, "BACK");

        Assert.False(emulator.WritingToStatusLine);
        Assert.Equal("BACK", Row(emulator, 0));
    }

    [Fact]
    public void TheIndicatorLineCannotBeWrittenTo()
    {
        // It belongs to the terminal. Accepting the switch would send the host's text nowhere at
        // all, which is worse than refusing it. Also an assumption - see SelectActiveStatusDisplay.
        var emulator = Build();

        Feed(emulator, Esc + "[1$}");
        Feed(emulator, "HELLO");

        Assert.False(emulator.WritingToStatusLine);
        Assert.Equal("HELLO", Row(emulator, 0));
    }

    [Fact]
    public void AHardResetErasesAndExitsIt()
    {
        // "Hard terminal reset (RIS): Erases and exits the status line."
        var emulator = Build();
        Feed(emulator, Esc + "[2$~" + Esc + "[1$}");
        Feed(emulator, "GONE");

        Feed(emulator, Esc + "c");

        Assert.Equal("", emulator.StatusLineText);
        Assert.False(emulator.WritingToStatusLine);
        Assert.Equal(TerminalEmulatorBase.StatusLineKind.Indicator, emulator.StatusLineType);
    }

    [Fact]
    public void ATerminalWithoutOneIgnoresBothSequences()
    {
        // "Available in: VT300 mode only." A VT220 has no twenty-fifth line, and acting on the
        // sequence would be inventing a machine that did not exist.
        var emulator = Build("VT220");

        Feed(emulator, Esc + "[2$~" + Esc + "[1$}");
        Feed(emulator, "TEXT");

        Assert.False(emulator.WritingToStatusLine);
        Assert.Equal("TEXT", Row(emulator, 0));
    }

    [Fact]
    public void BothSettingsCanBeAskedFor()
    {
        // DECRQSS lists them in table 12-4, and both are answerable because both are real state.
        var emulator = Build();
        Feed(emulator, Esc + "[2$~" + Esc + "[1$}");

        var replies = new StringBuilder();
        emulator.DataToSend += bytes => replies.Append(Encoding.ASCII.GetString(bytes));

        Feed(emulator, Esc + "P$q$~" + Esc + "\\");
        Assert.Equal(Esc + "P1$r2$~" + Esc + "\\", replies.ToString());

        replies.Clear();
        Feed(emulator, Esc + "P$q$}" + Esc + "\\");
        Assert.Equal(Esc + "P1$r1$}" + Esc + "\\", replies.ToString());
    }

    [Fact]
    public void TextTooLongForTheLineIsDroppedRatherThanWrapped()
    {
        // One row, no scrolling: there is nowhere for a twenty-first character to go on a
        // twenty-column screen.
        var emulator = Build();
        Feed(emulator, Esc + "[2$~" + Esc + "[1$}");

        Feed(emulator, "123456789012345678901234567890");

        Assert.Equal("12345678901234567890", emulator.StatusLineText);
        for (int row = 0; row < 6; row++)
        {
            Assert.Equal("", Row(emulator, row));
        }
    }
}
