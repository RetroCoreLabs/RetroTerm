using System.Text;
using RetroTerm.Core.Configuration;
using RetroTerm.Core.Terminal.Emulators;
using Xunit;

namespace RetroTerm.Tests.Terminal;

/// <summary>
/// DECSCL - telling the terminal to behave as an earlier one.
///
/// From chapter 4 of the VT420 Programmer Reference in spec\DEC. A host written for a VT100 can ask
/// for VT100 behaviour and be answered by one, which is worth more than it sounds: the alternative
/// is a host discovering the difference by having a sequence it does not expect acted upon.
///
/// The manual gives two levels, not four - "Level 1 for VT100 operation", "Level 4 for VT200,
/// VT300, and VT400 operation" - and says level 4 includes levels 2 and 3.
/// </summary>
public class ConformanceLevelTests
{
    private static readonly string Esc = ((char)0x1B).ToString();

    private static TerminalEmulatorBase Build(string type = "VT420")
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
    public void TheFactoryDefaultIsLevelFour()
    {
        Assert.Equal(4, Build().ConformanceLevel);
    }

    [Fact]
    public void SixtyOneSelectsVt100Operation()
    {
        var emulator = Build();

        Feed(emulator, Esc + "[61\"p");

        Assert.Equal(1, emulator.ConformanceLevel);
    }

    [Theory]
    [InlineData(62)]
    [InlineData(63)]
    [InlineData(64)]
    public void SixtyTwoThreeAndFourAllSelectTheSameThing(int parameter)
    {
        // "Level 4 includes all the characteristics of levels 2 and 3", so there is nothing for
        // this terminal to do differently between them.
        var emulator = Build();
        Feed(emulator, Esc + "[61\"p");

        Feed(emulator, Esc + "[" + parameter + "\"p");

        Assert.Equal(4, emulator.ConformanceLevel);
    }

    [Fact]
    public void ChangingTheLevelPerformsAHardReset()
    {
        // The manual says so in a note, and it is the part most likely to be left out. A host that
        // drops the terminal to VT100 and finds a scrolling region still set is reading state from
        // a terminal that should have just been switched on.
        var emulator = Build();
        Feed(emulator, Esc + "[3;5r");          // a scrolling region
        Feed(emulator, Esc + "[1;1H" + "TEXT");

        Feed(emulator, Esc + "[61\"p");

        Assert.Equal("", Row(emulator, 0));
        Assert.Equal(0, emulator.GetCursor().Row);
        Assert.Equal(0, emulator.GetCursor().Column);
    }

    [Fact]
    public void TheSecondParameterChoosesHowC1ControlsAreSent()
    {
        // 1 means 7-bit; 0, 2 and a missing parameter all mean 8-bit.
        var emulator = Build();

        Feed(emulator, Esc + "[64;1\"p");
        Assert.False(emulator.Send8BitControls);

        Feed(emulator, Esc + "[64;2\"p");
        Assert.True(emulator.Send8BitControls);

        Feed(emulator, Esc + "[64;0\"p");
        Assert.True(emulator.Send8BitControls);

        Feed(emulator, Esc + "[64\"p");
        Assert.True(emulator.Send8BitControls);
    }

    [Fact]
    public void LevelOneNeverSendsEightBitControls()
    {
        // "The terminal sends all C1 control characters as 7-bit escape sequences (ESC Fe)."
        var emulator = Build();
        Feed(emulator, Esc + "[64;2\"p");
        Assert.True(emulator.Send8BitControls);

        Feed(emulator, Esc + "[61\"p");

        Assert.False(emulator.Send8BitControls);
    }

    [Fact]
    public void AParameterThatIsNotALevelChangesNothing()
    {
        // Including no reset. A terminal that wiped the screen for a sequence it did not understand
        // would be worse than one that ignored it.
        var emulator = Build();
        Feed(emulator, Esc + "[1;1H" + "KEEP");

        Feed(emulator, Esc + "[99\"p");

        Assert.Equal(4, emulator.ConformanceLevel);
        Assert.Equal("KEEP", Row(emulator, 0));
    }

    [Fact]
    public void ATerminalThatDoesNotClaimDecsclIgnoresIt()
    {
        // Only the VT420 claims this, because the VT420 manual is the only source here that
        // documents it. A VT220 must not silently reset itself.
        var emulator = Build("VT220");
        Feed(emulator, Esc + "[1;1H" + "KEEP");

        Feed(emulator, Esc + "[61\"p");

        Assert.Equal(4, emulator.ConformanceLevel);
        Assert.Equal("KEEP", Row(emulator, 0));
    }

    // ── What level 1 stops doing (the manual's Table 4-1) ────────────────────────────────

    [Fact]
    public void LevelOneIgnoresEraseCharacter()
    {
        var emulator = Build();
        Feed(emulator, Esc + "[61\"p");
        Feed(emulator, Esc + "[1;1H" + "ABCDEF");

        Feed(emulator, Esc + "[1;2H" + Esc + "[3X");

        Assert.Equal("ABCDEF", Row(emulator, 0));
    }

    [Fact]
    public void LevelOneIgnoresInsertCharacter()
    {
        var emulator = Build();
        Feed(emulator, Esc + "[61\"p");
        Feed(emulator, Esc + "[1;1H" + "ABCDEF");

        Feed(emulator, Esc + "[1;2H" + Esc + "[3@");

        Assert.Equal("ABCDEF", Row(emulator, 0));
    }

    [Fact]
    public void LevelOneIgnoresTheProtectionAttribute()
    {
        // DECSCA is on Table 4-1, and with it off the selective erase has nothing to spare.
        var emulator = Build();
        Feed(emulator, Esc + "[61\"p");

        Feed(emulator, Esc + "[1\"q");
        Feed(emulator, Esc + "[1;1H" + "GONE");
        Feed(emulator, Esc + "[1;1H" + Esc + "[?2J");

        Assert.Equal("", Row(emulator, 0));
    }

    [Fact]
    public void LevelOneIgnoresTheRectangleOperations()
    {
        var emulator = Build();
        Feed(emulator, Esc + "[61\"p");

        Feed(emulator, Esc + "[88;1;1;3;3$x");      // DECFRA, fill with 'X'

        Assert.Equal("", Row(emulator, 0));
    }

    [Fact]
    public void AndGoingBackToLevelFourRestoresThemAll()
    {
        // The guard that level 1 disables rather than deletes.
        var emulator = Build();
        Feed(emulator, Esc + "[61\"p");
        Feed(emulator, Esc + "[64\"p");

        Feed(emulator, Esc + "[1;1H" + "ABCDEF");
        Feed(emulator, Esc + "[1;2H" + Esc + "[3X");

        Assert.Equal("A", Row(emulator, 0).Substring(0, 1));
        Assert.Equal("A...EF", Row(emulator, 0).Replace(' ', '.'));
    }
}
