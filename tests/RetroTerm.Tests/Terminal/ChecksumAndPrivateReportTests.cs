using System.Text;
using RetroTerm.Core.Configuration;
using RetroTerm.Core.Terminal.Emulators;
using Xunit;

namespace RetroTerm.Tests.Terminal;

/// <summary>
/// DECRQCRA and the DEC-private device status reports - CSI ? Ps n.
/// </summary>
/// <remarks>
/// <para><b>The defect these were written for</b></para>
/// CSI ? Ps n was not recognised as a question at all. It fell through to the private-MODE handler,
/// which reads any final byte other than "h" as a reset - so a host asking for the cursor position
/// with CSI ? 6 n silently TURNED ORIGIN MODE OFF, and CSI ? 25 n asking whether the function keys
/// were locked HID THE CURSOR instead. Both are sequences real software sends.
///
/// <para><b>Where the expected answers come from</b></para>
/// Table 12-5 of the VT420 Programmer Reference, at spec\DEC, and xterm's ctlseqs.txt beside it for
/// the two questions DEC does not answer (locator status and type). Each test quotes the one it
/// checks. The checksum ALGORITHM is documented nowhere - see ReportRectangleChecksum for what is
/// followed instead and what is deliberately left out.
/// </remarks>
public class ChecksumAndPrivateReportTests
{
    private static readonly string Esc = ((char)0x1B).ToString();

    private static TerminalEmulatorBase Build(string type = "VT420")
        => EmulatorFactory.CreateEmulator(type, 20, 10, 100);

    private static void Feed(TerminalEmulatorBase emulator, string text)
        => emulator.ProcessData(Encoding.ASCII.GetBytes(text));

    /// <summary>
    /// Sends one sequence and collects everything the terminal answers with.
    /// </summary>
    /// <param name="emulator">
    /// The terminal to ask.
    /// </param>
    /// <param name="sequence">
    /// The sequence, without the leading escape.
    /// </param>
    /// <returns>
    /// The reply as text, empty when the terminal said nothing.
    /// </returns>
    private static string Ask(TerminalEmulatorBase emulator, string sequence)
    {
        var replies = new StringBuilder();
        emulator.DataToSend += bytes => replies.Append(Encoding.ASCII.GetString(bytes));

        Feed(emulator, Esc + sequence);
        return replies.ToString();
    }

    // ── The defect ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void AskingForTheCursorPositionDoesNotTurnOriginModeOff()
    {
        // CSI ? 6 n is DECXCPR. Mode 6 is DECOM. Reading the question as "reset mode 6" is how one
        // became the other.
        var emulator = Build();
        Feed(emulator, Esc + "[3;8r");
        Feed(emulator, Esc + "[?6h");           // origin mode ON
        Feed(emulator, Esc + "[?6n");           // ask the question

        // Row 1 under origin mode is row 3 of the screen. If the question had reset the mode, the
        // cursor would go to the top of the SCREEN instead.
        Feed(emulator, Esc + "[1;1H");
        Assert.Equal(2, emulator.GetCursor().Row);
    }

    [Fact]
    public void AndAskingAboutTheFunctionKeysDoesNotHideTheCursor()
    {
        // CSI ? 25 n is the UDK status question; mode 25 is DECTCEM, the cursor's visibility.
        var emulator = Build();
        Feed(emulator, Esc + "[?25n");

        Assert.True(emulator.GetCursor().Visible);
    }

    // ── The answers ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void TheExtendedCursorPositionCarriesThePageNumber()
    {
        // "Report DECXCPR CSI Pl; Pc; Pp R" - the manual - with xterm's private marker in front,
        // because without it the answer cannot be told from an ordinary CPR. See
        // HandleDecPrivateDeviceStatusReport for that choice.
        var emulator = Build();
        Feed(emulator, Esc + "[4;7H");

        Assert.Equal(Esc + "[?4;7;1R", Ask(emulator, "[?6n"));
    }

    [Fact]
    public void ThePrinterIsReportedAsAbsent()
    {
        // "CSI ? 13 n - No printer." There is no printer support here at all, so this is the true
        // answer rather than a placeholder.
        var emulator = Build();

        Assert.Equal(Esc + "[?13n", Ask(emulator, "[?15n"));
    }

    [Fact]
    public void TheFunctionKeysReportWhetherAHostLockedThem()
    {
        // "CSI ? 20 n UDKs unlocked" and "CSI ? 21 n UDKs locked". The lock is real - DECUDK with
        // Pl = 1 sets it and only a hard reset clears it - so the answer is read from it.
        var emulator = Build();
        Assert.Equal(Esc + "[?20n", Ask(emulator, "[?25n"));

        var locked = Build();
        Feed(locked, Esc + "P0;0|17/41" + Esc + "\\");        // DECUDK - Pl 0 is the one that LOCKS
        Assert.Equal(Esc + "[?21n", Ask(locked, "[?25n"));
    }

    [Fact]
    public void TheKeyboardAnswersNorthAmericanAndReady()
    {
        // "CSI ? 27 ; 1 ; 0 ; 0 n (North American)" - ctlseqs. The manual's table 12-5 gives the
        // same three numbers: Pn 1 North American, Pst 0 keyboard ready, Ptyp 0 LK201/LK301.
        var emulator = Build();

        Assert.Equal(Esc + "[?27;1;0;0n", Ask(emulator, "[?26n"));
    }

    [Theory]
    [InlineData("[?53n", "[?53n")]       // locator status - "No Locator, if not [compiled-in]"
    [InlineData("[?55n", "[?53n")]
    [InlineData("[?56n", "[?57;0n")]     // locator type - "Cannot identify, if not"
    [InlineData("[?75n", "[?70n")]       // data integrity - "No communication errors"
    [InlineData("[?85n", "[?83n")]       // multiple sessions - "SSU sessions not ready"
    [InlineData("[?62n", "[0*{")]        // DECMSR - no macro memory, so no bytes free
    public void TheHardwareThatIsNotHereSaysSoRatherThanSayingNothing(string question, string answer)
    {
        // A host that asked is waiting. Silence would hang it; a made-up "yes" would be a lie.
        var emulator = Build();

        Assert.Equal(Esc + answer, Ask(emulator, question));
    }

    [Fact]
    public void AQuestionWithNoAnswerHereIsAnsweredWithSilence()
    {
        var emulator = Build();

        Assert.Equal("", Ask(emulator, "[?9999n"));
    }

    // ── DECRQCRA ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void TheChecksumIsTheNegatedSumOfTheCharactersInTheRectangle()
    {
        // "A" is 65 and "B" is 66. Negated and cut to 16 bits, 131 is 0xFF7D.
        var emulator = Build();
        Feed(emulator, Esc + "[1;1HAB");

        Assert.Equal(Esc + "P1!~FF7D" + Esc + "\\", Ask(emulator, "[1;1;1;1;1;2*y"));
    }

    [Fact]
    public void TheRequestLabelComesBackWithIt()
    {
        // "Pid is a numeric label you can provide to identify the checksum request. The checksum
        // report returns this number."
        var emulator = Build();
        Feed(emulator, Esc + "[1;1HA");

        Assert.StartsWith(Esc + "P42!~", Ask(emulator, "[42;1;1;1;1;1*y"));
    }

    [Fact]
    public void BlanksAddNothing()
    {
        // ctlseqs documents XTCHECKSUM bit 2 as "do not omit checksum for blanks", so by default
        // blanks ARE omitted. An untouched cell counts as blank too.
        var emulator = Build();
        Feed(emulator, Esc + "[1;1HA B");

        // 65 + 66 with nothing for the space between them, so the same 0xFF7D as "AB" side by
        // side. Counting the blank as 32 would have given 0xFF5D.
        Assert.Equal(Esc + "P1!~FF7D" + Esc + "\\", Ask(emulator, "[1;1;1;1;1;3*y"));
    }

    [Fact]
    public void AnEmptyRectangleChecksumsToZero()
    {
        var emulator = Build();

        Assert.Equal(Esc + "P1!~0000" + Esc + "\\", Ask(emulator, "[1;1;2;1;2;20*y"));
    }

    [Fact]
    public void OmittingTheBordersChecksumsTheWholePage()
    {
        // "If these parameters are omitted, the terminal returns a checksum of page Pp."
        var emulator = Build();
        Feed(emulator, Esc + "[1;1HA" + Esc + "[9;20HB");

        // 65 + 66 = 131 again, and both are inside the page but not inside any small rectangle.
        Assert.Equal(Esc + "P1!~FF7D" + Esc + "\\", Ask(emulator, "[1;1*y"));
    }

    [Fact]
    public void TheRectangleFollowsOriginMode()
    {
        // "The coordinates of the rectangular area are affected by the setting of origin mode
        // (DECOM)" - Notes on DECRQCRA.
        var emulator = Build();
        Feed(emulator, Esc + "[3;8r");
        Feed(emulator, Esc + "[?6h");
        Feed(emulator, Esc + "[1;1HZ");         // row 1 of the region is row 3 of the screen

        // Row 1 to the host is row 3 of the screen, so this rectangle contains the "Z" - 90
        // negated is 0xFFA6.
        Assert.Equal(Esc + "P1!~FFA6" + Esc + "\\", Ask(emulator, "[1;1;1;1;1;1*y"));
    }

    [Fact]
    public void ALevelOneTerminalDoesNotAnswerAtAll()
    {
        // DECRQCRA is VT400 mode only, and it is gated with the rest of the rectangle family.
        var emulator = EmulatorFactory.CreateEmulator("VT100", 20, 10, 100);
        Feed(emulator, Esc + "[1;1HA");

        Assert.Equal("", Ask(emulator, "[1;1;1;1;1;1*y"));
    }

    [Fact]
    public void TheMacroChecksumIsReportedInTheSameShape()
    {
        // DSR 63 asks for a checksum "of current text macro definitions". There is no macro
        // facility here, so the sum is over nothing - but the REPORT still has to arrive, because
        // it is the same DECCKSR string and the host is waiting for it.
        var emulator = Build();

        Assert.Equal(Esc + "P7!~0000" + Esc + "\\", Ask(emulator, "[?63;7n"));
    }
}
