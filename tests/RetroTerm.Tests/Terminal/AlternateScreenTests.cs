using System.Text;
using RetroTerm.Core.Configuration;
using RetroTerm.Core.Terminal.Emulators;
using Xunit;

namespace RetroTerm.Tests.Terminal;

/// <summary>
/// The alternate screen, audited the way DECSC and RIS were.
///
/// This is the path every full-screen program takes — an editor, a pager, anything that switches to
/// mode 1049, draws, and switches back expecting the shell exactly as it left it. The interesting
/// question is not whether the switch works but what LEAKS ACROSS IT.
/// </summary>
public class AlternateScreenTests
{
    private static readonly string Esc = ((char)0x1B).ToString();

    private static TerminalEmulatorBase Build()
        => EmulatorFactory.CreateEmulator("VT220", 20, 6, 100);

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
    public void ClearingTheAlternateScreenDoesNotWipeThePrimary()
    {
        // THE defect this audit found. A full-screen program erases the screen it just switched to;
        // that erase used to clear the buffer BEHIND it as well, so when the program exited the
        // shell's screen was blank. The classic "quitting the editor cleared my terminal".
        var emulator = Build();
        Feed(emulator, Esc + "[1;1H" + "SHELL OUTPUT");

        Feed(emulator, Esc + "[?1049h");          // a program starts
        Feed(emulator, Esc + "[2J");              // and clears its screen
        Feed(emulator, Esc + "[1;1H" + "EDITOR");
        Feed(emulator, Esc + "[?1049l");          // and exits

        Assert.Equal("SHELL OUTPUT", Row(emulator, 0));
    }

    [Fact]
    public void AndTheProgramStillGetsACleanScreenToWorkOn()
    {
        // The guard against fixing it the lazy way by not clearing at all.
        var emulator = Build();
        Feed(emulator, Esc + "[1;1H" + "SHELL OUTPUT");

        Feed(emulator, Esc + "[?1049h");
        Feed(emulator, Esc + "[2J");

        Assert.Equal("", Row(emulator, 0));
    }

    [Fact]
    public void AHardResetStillClearsBothScreens()
    {
        // A reset means "just switched on", and a terminal that has just been switched on has
        // nothing on either screen. This is the one caller that genuinely wants both.
        var emulator = Build();
        Feed(emulator, Esc + "[1;1H" + "PRIMARY");
        Feed(emulator, Esc + "[?1049h");
        Feed(emulator, Esc + "[1;1H" + "ALTERNATE");

        emulator.Reset();

        Assert.Equal("", Row(emulator, 0));
        Feed(emulator, Esc + "[?1049l");
        Assert.Equal("", Row(emulator, 0));
    }

    [Fact]
    public void TheCursorComesBackWhereTheProgramFoundIt()
    {
        var emulator = Build();
        Feed(emulator, Esc + "[4;7H");

        Feed(emulator, Esc + "[?1049h");
        Feed(emulator, Esc + "[1;1H");
        Feed(emulator, Esc + "[?1049l");

        Assert.Equal(3, emulator.GetCursor().Row);
        Assert.Equal(6, emulator.GetCursor().Column);
    }

    [Fact]
    public void TheRenditionComesBackToo()
    {
        // Mode 1049 saves and restores through the same code DECSC uses, so everything on that
        // list travels with it.
        var emulator = Build();
        Feed(emulator, Esc + "[1;4m");

        Feed(emulator, Esc + "[?1049h");
        Feed(emulator, Esc + "[0m");
        Feed(emulator, Esc + "[?1049l");

        var replies = new StringBuilder();
        emulator.DataToSend += bytes => replies.Append(Encoding.ASCII.GetString(bytes));
        Feed(emulator, Esc + "P$qm" + Esc + "\\");

        Assert.Equal(Esc + "P1$r0;1;4m" + Esc + "\\", replies.ToString());
    }

    [Fact]
    public void TheScrollbackIsNotTouchedByTheSwitch()
    {
        // Scrollback belongs to the primary screen. A program switching away and back must not
        // cost the user their history.
        var emulator = Build();
        for (int i = 1; i <= 10; i++)
        {
            Feed(emulator, "line " + i + "\r\n");
        }
        int before = emulator.GetBuffer().ScrollbackLineCount;
        Assert.True(before > 0);

        Feed(emulator, Esc + "[?1049h");
        Feed(emulator, Esc + "[2J");
        Feed(emulator, Esc + "[?1049l");

        Assert.Equal(before, emulator.GetBuffer().ScrollbackLineCount);
    }

    [Fact]
    public void AResizeWhileOnTheAlternateScreenLeavesThePrimaryUsable()
    {
        // Both grids are resized, not just the active one. If the inactive array kept the old
        // shape, switching back would read a screen whose dimensions disagree with the buffer's.
        var emulator = Build();
        Feed(emulator, Esc + "[1;1H" + "PRIMARY");

        Feed(emulator, Esc + "[?1049h");
        emulator.Resize(40, 12);
        Feed(emulator, Esc + "[?1049l");

        Assert.Equal(40, emulator.Width);
        Assert.Equal("PRIMARY", Row(emulator, 0));
    }
}
