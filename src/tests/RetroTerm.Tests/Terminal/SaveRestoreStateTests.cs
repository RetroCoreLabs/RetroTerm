using System.Text;
using RetroTerm.Core.Configuration;
using RetroTerm.Core.Terminal.Emulators;
using Xunit;

namespace RetroTerm.Tests.Terminal;

/// <summary>
/// What DECSC saves and DECRC puts back.
///
/// DEC's own list is cursor position, graphic rendition, the character set shift state, autowrap,
/// origin mode — and the SELECTIVE ERASE attribute. That last one was missing, which is the kind of
/// gap that only shows up when a program uses save and restore around a form: it marks its labels
/// protected, saves, does something, restores, and then types characters that are protected or not
/// by accident.
/// </summary>
public class SaveRestoreStateTests
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
    public void TheProtectionSettingSurvivesASaveAndRestore()
    {
        // THE gap this test was written for. Protection on, save, protection off, restore - and the
        // text typed afterwards must be protected again, because that is what was saved.
        var emulator = Build();

        Feed(emulator, Esc + "[1\"q");        // DECSCA: protect what follows
        Feed(emulator, Esc + "7");            // DECSC
        Feed(emulator, Esc + "[0\"q");        // and now do not
        Feed(emulator, Esc + "8");            // DECRC

        Feed(emulator, Esc + "[1;1H" + "KEEP");
        Feed(emulator, Esc + "[1;1H" + Esc + "[?2J");   // selective erase

        Assert.Equal("KEEP", Row(emulator, 0));
    }

    [Fact]
    public void AndTheOtherDirectionToo()
    {
        // Not protected, saved, then protected, then restored: what follows must be erasable.
        var emulator = Build();

        Feed(emulator, Esc + "[0\"q");
        Feed(emulator, Esc + "7");
        Feed(emulator, Esc + "[1\"q");
        Feed(emulator, Esc + "8");

        Feed(emulator, Esc + "[1;1H" + "GONE");
        Feed(emulator, Esc + "[1;1H" + Esc + "[?2J");

        Assert.Equal("", Row(emulator, 0));
    }

    [Fact]
    public void RestoringWithNothingSavedReturnsToTheDefaults()
    {
        // "Nothing saved" means the power-on state, and leaving text protected on the way there
        // would spare it from a later selective erase for no reason a host could see.
        var emulator = Build();
        Feed(emulator, Esc + "[1\"q");

        Feed(emulator, Esc + "8");            // DECRC with no DECSC before it

        Feed(emulator, Esc + "[1;1H" + "GONE");
        Feed(emulator, Esc + "[1;1H" + Esc + "[?2J");

        Assert.Equal("", Row(emulator, 0));
    }

    [Fact]
    public void TheRestOfDecsListIsStillSaved()
    {
        // The guard against a narrow fix: everything DECSC already carried must keep working.
        var emulator = Build();

        Feed(emulator, Esc + "[4;7H");        // position
        Feed(emulator, Esc + "[1;4m");        // rendition
        Feed(emulator, Esc + "[?7l");         // autowrap off
        Feed(emulator, Esc + "7");

        Feed(emulator, Esc + "[1;1H" + Esc + "[0m" + Esc + "[?7h");
        Feed(emulator, Esc + "8");

        Assert.Equal(3, emulator.GetCursor().Row);
        Assert.Equal(6, emulator.GetCursor().Column);
        Assert.False(emulator.GetCursor().AutoWrap);

        // The rendition came back too, which DECRQSS can be asked about directly.
        var replies = new StringBuilder();
        emulator.DataToSend += bytes => replies.Append(Encoding.ASCII.GetString(bytes));
        Feed(emulator, Esc + "P$qm" + Esc + "\\");
        Assert.Equal(Esc + "P1$r0;1;4m" + Esc + "\\", replies.ToString());
    }

    [Fact]
    public void TheAlternateScreenPathSavesItAsWell()
    {
        // Mode 1049 saves and restores through the same code, and it is the path full-screen
        // programs actually use.
        var emulator = Build();
        Feed(emulator, Esc + "[1\"q");

        Feed(emulator, Esc + "[?1049h");
        Feed(emulator, Esc + "[0\"q");
        Feed(emulator, Esc + "[?1049l");

        Feed(emulator, Esc + "[1;1H" + "KEEP");
        Feed(emulator, Esc + "[1;1H" + Esc + "[?2J");

        Assert.Equal("KEEP", Row(emulator, 0));
    }
}
