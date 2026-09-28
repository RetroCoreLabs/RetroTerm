using System.Text;
using RetroTerm.Core.Configuration;
using RetroTerm.Core.Terminal.Emulators;
using Xunit;

namespace RetroTerm.Tests.Terminal;

/// <summary>
/// The saved cursor belongs to the SCREEN, not to the terminal.
/// </summary>
/// <remarks>
/// There are two screens - the main one and the alternate one a full-screen program switches to -
/// and each keeps its own DECSC slot. A program can save on one, switch, save on the other, and get
/// its own position back on each.
///
/// With one shared slot the second save destroyed the first, so both restores landed on the same
/// spot. The xterm.js fixture t0092-alt_screen_DECSC is exactly that shape and was captured from a
/// real xterm: text meant for the top of the main screen was written over the middle of it, and
/// everything printed afterwards sat three rows too low.
///
/// Nothing in ctlseqs states the rule outright. It follows from what mode 1049 has to do - save the
/// shell's cursor, hand the whole screen to a program that may save and restore as much as it
/// likes, and still put the prompt back exactly where it was.
/// </remarks>
public class SavedCursorPerScreenTests
{
    private const string Esc = "";

    private static TerminalEmulatorBase Build(int width = 20, int height = 10)
        => EmulatorFactory.CreateEmulator("XTERM", width, height, 100);

    private static void Feed(TerminalEmulatorBase emulator, string text)
        => emulator.ProcessData(Encoding.ASCII.GetBytes(text));

    private static string Row(TerminalEmulatorBase emulator, int row)
    {
        var buffer = emulator.GetBuffer();
        var sb = new StringBuilder(buffer.Width);
        for (int column = 0; column < buffer.Width; column++)
        {
            uint codepoint = buffer[row, column].Codepoint;
            sb.Append(codepoint == 0 || codepoint == ' ' ? '.' : (char)codepoint);
        }
        return sb.ToString();
    }

    [Fact]
    public void EachScreenRestoresItsOwnSavedCursor()
    {
        var emulator = Build();

        Feed(emulator, Esc + "[2;3H" + Esc + "7");      // save (1,2) on the MAIN screen
        Feed(emulator, Esc + "[?47h");                   // to the alternate screen
        Feed(emulator, Esc + "[6;9H" + Esc + "7");      // save (5,8) on the ALTERNATE screen
        Feed(emulator, Esc + "[1;1H" + Esc + "8");      // restore on the alternate screen

        Assert.Equal(5, emulator.GetCursor().Row);
        Assert.Equal(8, emulator.GetCursor().Column);

        Feed(emulator, Esc + "[?47l");                   // back to the main screen
        Feed(emulator, Esc + "8");                       // restore on the main screen

        Assert.Equal(1, emulator.GetCursor().Row);
        Assert.Equal(2, emulator.GetCursor().Column);
    }

    [Fact]
    public void ASaveOnTheAlternateScreenDoesNotReachTheMainOne()
    {
        // The defect, stated as a screen rather than as a cursor: the letter meant for the saved
        // position on the main screen used to land where the ALTERNATE screen had saved.
        var emulator = Build();

        Feed(emulator, Esc + "[2;3H" + Esc + "7");
        Feed(emulator, Esc + "[?47h");
        Feed(emulator, Esc + "[6;9H" + Esc + "7");
        Feed(emulator, Esc + "[?47l");
        Feed(emulator, Esc + "8" + "X");

        Assert.Equal("..X.................", Row(emulator, 1));
        Assert.Equal("....................", Row(emulator, 5));
    }

    [Fact]
    public void SwitchingScreensWithoutSavingLeavesTheCursorWhereItWas()
    {
        // Mode 47 is the oldest form: it changes the screen and NOTHING else. No save, no restore,
        // no clear. A terminal that homed the cursor here would break the fixture's third line.
        var emulator = Build();

        Feed(emulator, Esc + "[4;7H");
        Feed(emulator, Esc + "[?47h");

        Assert.Equal(3, emulator.GetCursor().Row);
        Assert.Equal(6, emulator.GetCursor().Column);

        Feed(emulator, Esc + "[?47l");

        Assert.Equal(3, emulator.GetCursor().Row);
        Assert.Equal(6, emulator.GetCursor().Column);
    }

    [Fact]
    public void TheSameSwitchTwiceDoesNotShuffleTheSlots()
    {
        // The slots are exchanged when the screen CHANGES. A host that sends the same switch twice
        // must not exchange them twice, which would hand each screen the other one's save.
        var emulator = Build();

        Feed(emulator, Esc + "[2;3H" + Esc + "7");
        Feed(emulator, Esc + "[?47h");
        Feed(emulator, Esc + "[?47h");                   // again, already there
        Feed(emulator, Esc + "[?47l");
        Feed(emulator, Esc + "[?47l");                   // again, already back
        Feed(emulator, Esc + "8");

        Assert.Equal(1, emulator.GetCursor().Row);
        Assert.Equal(2, emulator.GetCursor().Column);
    }

    [Fact]
    public void ModeTenFortyNinePutsThePromptBackWhereItWas()
    {
        // What a full-screen program actually uses. The alternate screen saves and restores as much
        // as it likes in between, and none of it may disturb the shell's saved position.
        var emulator = Build();

        Feed(emulator, Esc + "[3;5Hprompt");
        Feed(emulator, Esc + "[?1049h");                 // save, switch, clear, home
        Feed(emulator, Esc + "[8;2H" + Esc + "7");      // the program saves on its own screen
        Feed(emulator, Esc + "[1;1H" + Esc + "8");      // ...and restores, twice for good measure
        Feed(emulator, Esc + "[2;2H" + Esc + "8");
        Feed(emulator, Esc + "[?1049l");                 // switch back and restore

        Assert.Equal(2, emulator.GetCursor().Row);
        Assert.Equal(4 + 6, emulator.GetCursor().Column);   // after "prompt", where it was saved
        Assert.Equal("....prompt..........", Row(emulator, 2));
    }

    [Fact]
    public void TheAttributesSavedOnOneScreenDoNotLeakToTheOther()
    {
        // DECSC saves more than a position - colour, character sets, origin and wrap mode go with
        // it. All of that belongs to the screen too.
        var emulator = Build();

        Feed(emulator, Esc + "[31m" + Esc + "7");        // red, saved on the main screen
        Feed(emulator, Esc + "[?47h");
        Feed(emulator, Esc + "[32m" + Esc + "7");        // green, saved on the alternate screen
        Feed(emulator, Esc + "[?47l");
        Feed(emulator, Esc + "[0m" + Esc + "8" + "M");  // restore on the main screen and print

        var buffer = emulator.GetBuffer();
        var cell = buffer[0, 0];

        // Whatever red is on this profile, the cell must not be wearing the alternate screen's
        // green. Comparing against a freshly printed red is stronger than naming a colour value.
        var reference = Build();
        Feed(reference, Esc + "[31mM");
        Assert.Equal(reference.GetBuffer()[0, 0].Foreground, cell.Foreground);
    }

    [Fact]
    public void AResetClearsBothScreensSaves()
    {
        // RIS means the power-on state. A save left waiting on the screen that is not showing would
        // come back the next time something switched, long after the reset.
        var emulator = Build();

        Feed(emulator, Esc + "[?47h");
        Feed(emulator, Esc + "[7;7H" + Esc + "7");
        Feed(emulator, Esc + "[?47l");
        Feed(emulator, Esc + "c");                       // RIS

        Feed(emulator, Esc + "[?47h");
        Feed(emulator, Esc + "[5;5H" + Esc + "8");      // restore with nothing saved: go home

        Assert.Equal(0, emulator.GetCursor().Row);
        Assert.Equal(0, emulator.GetCursor().Column);
    }
}
