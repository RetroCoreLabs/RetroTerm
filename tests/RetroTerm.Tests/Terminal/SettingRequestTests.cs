using System.Text;
using RetroTerm.Core.Configuration;
using RetroTerm.Core.Terminal.Emulators;
using Xunit;

namespace RetroTerm.Tests.Terminal;

/// <summary>
/// DECRQSS — "what is your current setting for X?", the companion to DECRQM.
///
/// DECRQM answers whether a MODE is on. This answers what a SETTING is, and the elegant part of the
/// design is that the answer is EXECUTABLE: the terminal replies with the very sequence a host
/// would send to put it back in that state. A program can save a setting, change it, and restore it
/// without knowing anything about what it means.
/// </summary>
public class SettingRequestTests
{
    private static readonly string Esc = ((char)0x1B).ToString();

    private static TerminalEmulatorBase Build(string type = "VT420")
        => EmulatorFactory.CreateEmulator(type, 20, 10, 100);

    private static void Feed(TerminalEmulatorBase emulator, string text)
        => emulator.ProcessData(Encoding.ASCII.GetBytes(text));

    private static string Ask(TerminalEmulatorBase emulator, string setting)
    {
        var replies = new StringBuilder();
        emulator.DataToSend += bytes => replies.Append(Encoding.ASCII.GetString(bytes));

        Feed(emulator, Esc + "P$q" + setting + Esc + "\\");
        return replies.ToString();
    }

    [Fact]
    public void TheScrollingRegionIsReportedAsTheSequenceThatSetsIt()
    {
        var emulator = Build();
        Feed(emulator, Esc + "[3;8r");

        Assert.Equal(Esc + "P1$r3;8r" + Esc + "\\", Ask(emulator, "r"));
    }

    [Fact]
    public void AndTheAnswerIsSomethingTheTerminalWouldAccept()
    {
        // THE point of the design: a program can save a setting, change it, and put it back
        // without understanding it. So the answer has to be executable, and this test executes it.
        var emulator = Build();
        Feed(emulator, Esc + "[3;8r");
        var saved = Ask(emulator, "r");

        Feed(emulator, Esc + "[1;10r");                       // change it
        var payload = saved.Substring(saved.IndexOf("$r") + 2);
        payload = payload.Substring(0, payload.Length - 2);   // strip the terminator
        Feed(emulator, Esc + "[" + payload);                  // and put it back

        Assert.Equal(Esc + "P1$r3;8r" + Esc + "\\", Ask(emulator, "r"));
    }

    [Fact]
    public void TheCurrentAttributesAreReported()
    {
        var emulator = Build();
        Feed(emulator, Esc + "[1;4m");

        Assert.Equal(Esc + "P1$r0;1;4m" + Esc + "\\", Ask(emulator, "m"));
    }

    [Fact]
    public void TheAttributeAnswerAlwaysStartsFromNothing()
    {
        // Starting the list with 0 makes it a complete instruction rather than a difference from
        // whatever the host happened to have set before.
        var emulator = Build();

        Assert.Equal(Esc + "P1$r0m" + Esc + "\\", Ask(emulator, "m"));
    }

    [Fact]
    public void TheProtectionSettingIsReported()
    {
        var emulator = Build();
        Feed(emulator, Esc + "[1\"q");

        Assert.Equal(Esc + "P1$r1\"q" + Esc + "\\", Ask(emulator, "\"q"));
    }

    [Fact]
    public void MarginsAreReportedOnlyWhenTheModeIsOn()
    {
        // With the mode off the same sequence means SCOSC, and reporting margins for a terminal
        // that has none would invite a host to use them.
        var emulator = Build();
        Assert.Equal(Esc + "P0$r" + Esc + "\\", Ask(emulator, "s"));

        var withMargins = Build();
        Feed(withMargins, Esc + "[?69h" + Esc + "[4;12s");
        Assert.Equal(Esc + "P1$r4;12s" + Esc + "\\", Ask(withMargins, "s"));
    }

    [Fact]
    public void AnUnknownSettingIsAnsweredHonestlyRatherThanGuessedAt()
    {
        // 0 is a real answer, not a failure: it tells a host to stop asking and use its own
        // defaults. Replying 1 with a made-up value would be worse than useless.
        var emulator = Build();

        Assert.Equal(Esc + "P0$r" + Esc + "\\", Ask(emulator, "|"));
    }

    [Fact]
    public void ATerminalWithoutRectanglesDoesNotReportTheirExtent()
    {
        var vt100 = Build("VT100");
        Assert.Equal(Esc + "P0$r" + Esc + "\\", Ask(vt100, "*x"));

        var vt420 = Build();
        Assert.Equal(Esc + "P1$r0*x" + Esc + "\\", Ask(vt420, "*x"));
    }

    [Fact]
    public void TheConformanceLevelIsReportedAsTheHostAskedForIt()
    {
        // DECSCL is in the manual's table 12-4 of settings a host may ask about, and it was not
        // answered. The subtlety: this terminal treats 62, 63 and 64 as one level, but DECRQSS has
        // to answer with the sequence that would put the setting BACK - so the number the host
        // used is remembered separately.
        var emulator = Build();
        Feed(emulator, Esc + "[62;1\"p");

        Assert.Equal(Esc + "P1$r62;1\"p" + Esc + "\\", Ask(emulator, "\"p"));
    }

    [Fact]
    public void AndTheAnswerSaysWhichControlsAreInUse()
    {
        // The second parameter is 1 for 7-bit C1 controls and 0 for 8-bit.
        var emulator = Build();
        Feed(emulator, Esc + "[64;0\"p");

        Assert.Equal(Esc + "P1$r64;0\"p" + Esc + "\\", Ask(emulator, "\"p"));
    }

    [Fact]
    public void ALevelOneAnswerHasNoSecondParameter()
    {
        // "CSI 6 1 " p" is the whole sequence: a VT100 has no 8-bit controls to choose between.
        var emulator = Build();
        Feed(emulator, Esc + "[61\"p");

        Assert.Equal(Esc + "P1$r61\"p" + Esc + "\\", Ask(emulator, "\"p"));
    }

    [Fact]
    public void TheSettingsThisTerminalCannotPutBackAreNotReported()
    {
        // Nine of the fourteen in table 12-4. Answering would hand a host an instruction this
        // terminal would then ignore, which is worse than silence - see DescribeSetting.
        var emulator = Build();

        Assert.Equal(Esc + "P0$r" + Esc + "\\", Ask(emulator, "$|"));    // DECSCPP
        Assert.Equal(Esc + "P0$r" + Esc + "\\", Ask(emulator, "*|"));    // DECSNLS
        Assert.Equal(Esc + "P0$r" + Esc + "\\", Ask(emulator, "+q"));    // DECELF
    }

    [Fact]
    public void ASixelImageIsNotMistakenForAQuestion()
    {
        // Sixel owns the same final byte and is told apart by the '$' intermediate. A DCS q that
        // reaches the setting handler would answer a question nobody asked.
        var emulator = Build("VT340");
        var replies = new StringBuilder();
        emulator.DataToSend += bytes => replies.Append(Encoding.ASCII.GetString(bytes));

        Feed(emulator, Esc + "Pq#1~" + Esc + "\\");

        Assert.Equal("", replies.ToString());
        Assert.True(emulator.Graphics?.HasAnythingToDraw() ?? false);
    }
}
