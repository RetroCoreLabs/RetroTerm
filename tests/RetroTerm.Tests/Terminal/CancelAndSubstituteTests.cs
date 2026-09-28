using System.Text;
using RetroTerm.Core.Configuration;
using RetroTerm.Core.Terminal.Emulators;
using Xunit;

namespace RetroTerm.Tests.Terminal;

/// <summary>
/// CAN and SUB, which abandon a sequence half way through, and the ignore state a ruined sequence
/// falls into.
/// </summary>
/// <remarks>
/// ECMA-48 6.2.1 gives CAN (0x18) as "the information preceding it is in error" and SUB (0x1A) as
/// a character to be replaced. Neither statement says what a terminal does with the escape sequence
/// that was in flight, and that is the part that matters.
///
/// The xterm.js fixtures t0014-CAN and t0015-SUB settle it. Eight lines each, putting the control
/// in eight different places - in text, after ESC, inside an escape with intermediates, inside a
/// CSI with and without parameters - and a real xterm printed "abcdDefgh" for every one. The
/// sequence is DROPPED and the letter after the control is ordinary text.
///
/// Two defects were behind those eight rows. Neither control was recognised anywhere, so inside a
/// CSI they were executed as ordinary controls and the sequence carried on: "ESC [ CAN D" finished
/// as CSI D and moved the cursor back over a letter. And a CSI that had already gone wrong dropped
/// straight back to Ground, so its remaining bytes were printed - "ESC [ * 2 ; CAN D" left a
/// semicolon on the screen.
/// </remarks>
public class CancelAndSubstituteTests
{
    private const string Esc = "";
    private const string Can = "";
    private const string Sub = "";

    private static TerminalEmulatorBase Build(int width = 20, int height = 4)
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

    /// <summary>
    /// The eight places the fixture puts the control, as the text between "abcd" and "Defgh".
    /// </summary>
    public static TheoryData<string> EveryPlaceASequenceCanBeAbandoned() => new()
    {
        "",                 // in plain text, with no sequence running at all
        Esc,                // straight after ESC
        Esc + "!",          // inside an escape sequence with an intermediate
        Esc + "!*",         // ...with two of them
        Esc + "[",          // straight after CSI
        Esc + "[!",         // after a CSI private marker
        Esc + "[2",         // after a CSI parameter
        Esc + "[*2;",       // inside a CSI that is already malformed
    };

    [Theory]
    [MemberData(nameof(EveryPlaceASequenceCanBeAbandoned))]
    public void CancelDropsTheSequenceAndPrintsNothing(string prefix)
    {
        var emulator = Build();
        Feed(emulator, "abcd" + prefix + Can + "Defgh");

        Assert.Equal("abcdDefgh...........", Row(emulator, 0));
    }

    [Theory]
    [MemberData(nameof(EveryPlaceASequenceCanBeAbandoned))]
    public void SubstituteDoesExactlyTheSame(string prefix)
    {
        // Some DEC hardware shows a reverse question mark where a SUB arrived. A real xterm shows
        // nothing, and the two fixtures' expected screens are identical, so nothing is what we do.
        var emulator = Build();
        Feed(emulator, "abcd" + prefix + Sub + "Defgh");

        Assert.Equal("abcdDefgh...........", Row(emulator, 0));
    }

    [Fact]
    public void TheLetterAfterACancelIsTextRatherThanAFinalByte()
    {
        // The heart of the defect: without CAN handling this finished as CSI D - cursor back one -
        // and the following text overwrote the 'd'.
        var emulator = Build();
        Feed(emulator, "abcd" + Esc + "[" + Can + "Defgh");

        Assert.Equal("abcdDefgh...........", Row(emulator, 0));
        Assert.Equal(9, emulator.GetCursor().Column);
    }

    [Fact]
    public void ARuinedSequenceIsSwallowedWholeRatherThanPrinted()
    {
        // A digit after an intermediate is not legal CSI. Everything up to the final byte goes,
        // and nothing is dispatched.
        var emulator = Build();
        Feed(emulator, "ab" + Esc + "[*2;5H" + "cd");

        Assert.Equal("abcd................", Row(emulator, 0));
    }

    [Fact]
    public void ARuinedSequenceDoesNotActWhenItsFinalByteArrives()
    {
        // CSI 5 H would home the cursor to row 5. Ruined, it must do nothing at all - so the text
        // after it carries on where it was.
        var emulator = Build();
        Feed(emulator, "ab" + Esc + "[*2;5H" + "cd");

        Assert.Equal(0, emulator.GetCursor().Row);
        Assert.Equal(4, emulator.GetCursor().Column);
    }

    [Fact]
    public void AControlInsideARuinedSequenceStillActs()
    {
        // The DEC state diagram executes C0 controls even while throwing a sequence away, so a line
        // feed in the middle of one still feeds a line.
        var emulator = Build();
        Feed(emulator, "ab" + Esc + "[*2;" + "\r\n" + "5Hcd");

        Assert.Equal("ab..................", Row(emulator, 0));
        Assert.Equal("cd..................", Row(emulator, 1));
    }

    [Fact]
    public void AnEscapeStartsAFreshSequenceEvenInsideARuinedOne()
    {
        var emulator = Build();
        Feed(emulator, "ab" + Esc + "[*2;" + Esc + "[3;1Hxy");

        Assert.Equal("ab..................", Row(emulator, 0));
        Assert.Equal("xy..................", Row(emulator, 2));
    }

    [Fact]
    public void CancelInPlainTextIsSimplyIgnored()
    {
        // With no sequence running there is nothing to abandon, and CAN prints nothing of its own.
        var emulator = Build();
        Feed(emulator, "ab" + Can + "cd");

        Assert.Equal("abcd................", Row(emulator, 0));
    }

    [Fact]
    public void ACancelledSequenceLeavesNoParametersBehind()
    {
        // The parameters of the abandoned sequence must not leak into the next one. If they did,
        // this CSI m would arrive carrying the 31 from the sequence that was thrown away.
        var emulator = Build();
        Feed(emulator, Esc + "[31" + Can + Esc + "[mZ");

        var buffer = emulator.GetBuffer();
        Assert.Equal(buffer[0, 1].Foreground, buffer[0, 0].Foreground);
    }
}
