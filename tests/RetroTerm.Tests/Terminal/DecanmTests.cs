using System.Text;
using RetroTerm.Core.Configuration;
using RetroTerm.Core.Terminal.Emulators;
using Xunit;

namespace RetroTerm.Tests.Terminal;

/// <summary>
/// DECANM — a VT100 pretending to be a VT52, and finding its way back.
///
/// <c>CSI ? 2 l</c> drops a VT-family terminal into VT52 mode; ESC followed by a less-than sign is the way out. That
/// is how software of the period drove a VT100 when it only knew the older machine, and a terminal
/// that ignored the mode would answer ANSI to a host that had explicitly asked it not to.
///
/// The interpretation is shared with the VT52 emulator rather than copied. Two copies of ESC A
/// through ESC Z is how one gets fixed and the other does not.
/// </summary>
public class DecanmTests
{
    /// <summary>
    /// ESC as its own string — C#'s hex escape is variable length, so an ESC literal followed by a
    /// hex-digit letter is one character rather than two. See the note in Vt52Tests.
    /// </summary>
    private static readonly string Esc = ((char)0x1B).ToString();

    private static TerminalEmulatorBase Build(string type = "VT100")
        => EmulatorFactory.CreateEmulator(type, 20, 6, 100);

    private static void Feed(TerminalEmulatorBase emulator, string text)
        => emulator.ProcessData(Encoding.ASCII.GetBytes(text));

    [Fact]
    public void AVt100StartsInAnsiMode()
    {
        Assert.False(Build().Vt52Mode);
    }

    [Fact]
    public void TheHostCanDropItIntoVt52Mode()
    {
        var emulator = Build();

        Feed(emulator, Esc + "[?2l");

        Assert.True(emulator.Vt52Mode);
    }

    [Fact]
    public void AndThenTheOldEscapesWork()
    {
        var emulator = Build();
        Feed(emulator, Esc + "[3;3H");
        Feed(emulator, Esc + "[?2l");

        Feed(emulator, Esc + "A");     // VT52 cursor up

        Assert.Equal(1, emulator.GetCursor().Row);
    }

    [Fact]
    public void DirectAddressingWorksThereToo()
    {
        // The raw-byte path, reached through a VT100 rather than a VT52.
        var emulator = Build();
        Feed(emulator, Esc + "[?2l");

        Feed(emulator, Esc + "Y$(X");

        emulator.GetBuffer().TryGetCell(4, 8, out var cell);
        Assert.Equal('X', (char)cell.Codepoint);
    }

    [Fact]
    public void EscapeLessThanComesBack()
    {
        var emulator = Build();
        Feed(emulator, Esc + "[?2l");
        Assert.True(emulator.Vt52Mode);

        Feed(emulator, Esc + "<");

        Assert.False(emulator.Vt52Mode);
    }

    [Fact]
    public void AndThenAnsiWorksAgain()
    {
        var emulator = Build();
        Feed(emulator, Esc + "[?2l");
        Feed(emulator, Esc + "<");

        Feed(emulator, Esc + "[4;7H");

        Assert.Equal(3, emulator.GetCursor().Row);
        Assert.Equal(6, emulator.GetCursor().Column);
    }

    [Fact]
    public void AHardResetReturnsToAnsi()
    {
        // A VT-family terminal switches on in ANSI mode, and a reset leaves it as switched on.
        var emulator = Build();
        Feed(emulator, Esc + "[?2l");

        emulator.Reset();

        Assert.False(emulator.Vt52Mode);
    }

    [Fact]
    public void DecrqmAnswersForTheMode()
    {
        var emulator = Build();
        var replies = new StringBuilder();
        emulator.DataToSend += bytes => replies.Append(Encoding.ASCII.GetString(bytes));

        Feed(emulator, Esc + "[?2$p");          // set: this terminal is in ANSI
        Feed(emulator, Esc + "[?2l");
        Feed(emulator, Esc + "[?2$p");          // and now reset: it is a VT52

        // 1 is set, 2 is reset. In VT52 mode the terminal still answers, because the query arrived
        // before the mode changed the second time.
        Assert.Equal(Esc + "[?2;1$y" + Esc + "[?2;2$y", replies.ToString());
    }

    [Fact]
    public void ATdvIsNotDroppedIntoVt52Mode()
    {
        // DECANM is a VT-family capability. A TDV2200 never had it, and obeying would put the
        // terminal into a mode its own host has no idea about.
        var emulator = Build("TDV2200");

        Feed(emulator, Esc + "[?2l");

        Assert.False(emulator.Vt52Mode);
    }

    [Fact]
    public void AVt52HasNothingToDropInto()
    {
        // It is already one. The profile does not carry the capability, so the mode never turns on
        // and ESC < is swallowed rather than printed.
        var emulator = Build("VT52");

        Feed(emulator, Esc + "[?2l");

        Assert.False(emulator.Vt52Mode);
    }
}
