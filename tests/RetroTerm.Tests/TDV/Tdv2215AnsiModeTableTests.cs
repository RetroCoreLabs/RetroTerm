using System.Text;
using RetroTerm.Core.Terminal;
using RetroTerm.Core.Terminal.Emulators.TDV;
using RetroTerm.Core.Terminal.Input;
using Xunit;

namespace RetroTerm.Tests.TDV;

/// <summary>
/// The 2215's own ANSI mode numbers, section 8.7.1, read the other way round.
/// </summary>
/// <remarks>
/// <para><b>Where this came from</b></para>
/// The mode work of 11 September 2026 started from the sequences this program SENT and worked
/// towards the manual. This file is the reverse pass: take the nineteen numbers TDV 2215 section
/// 8.7.1 lists and ask which of them this emulator does anything with.
///
/// <para><b>What the pass found</b></para>
/// Three of the nineteen named a switch this program already keeps, under a different number, and
/// was ignoring on the 2215's number: 32 CR is LNM, 55 AR is DECARM read backwards, and 68 CT is
/// the cursor shape. Those three are wired now and tested here.
///
/// The rest are either already handled - 31, 36, 47, 53, 60, 66 - or name hardware this program
/// does not model: 40, 42, 43 and 69 are the printer, 67 is the serial handshake, 54 is the margin
/// bell, 7 is vertical editing extent, 56 is the underline representation and 61 is the lamp-clear
/// policy. Those reach <c>CountUnrecognisedSequence</c> with their number, so a host using one
/// shows up in UNHANDLED rather than vanishing.
/// </remarks>
public class Tdv2215AnsiModeTableTests
{
    /// <summary>
    /// The escape character, built from its code so no raw control byte lands in this file.
    /// </summary>
    private const char Escape = (char)0x1B;

    /// <summary>
    /// Feeds a sequence, supplying the escape.
    /// </summary>
    /// <param name="emulator">
    /// The emulator to feed.
    /// </param>
    /// <param name="sequence">
    /// The sequence after the escape.
    /// </param>
    private static void Feed(TDVEmulatorBase emulator, string sequence)
    {
        emulator.ProcessData(Encoding.ASCII.GetBytes(Escape + sequence));
    }

    /// <summary>
    /// Mode 32 is the 2215's number for what DEC calls LNM, and both reach one flag.
    /// </summary>
    /// <remarks>
    /// Section 8.7.1: "32 CR, Cursor Return: CR / CRLF" - whether a carriage return also moves down
    /// a line. DEC's number for the same switch is 20. Two numbers, one state, so they cannot
    /// disagree with each other.
    /// </remarks>
    [Fact]
    public void ModeThirtyTwoIsTheSameSwitchAsLnm()
    {
        var emulator = new TDV2200Emulator(80, 24);

        // Observed through what a line feed DOES, which is the whole meaning of the switch: with
        // it set, LF also returns the carriage.
        emulator.ProcessData(Encoding.ASCII.GetBytes("abc"));
        Feed(emulator, "[32h");
        emulator.ProcessData(new byte[] { 0x0A });
        Assert.Equal(0, emulator.Cursor.Column);

        emulator.ProcessData(Encoding.ASCII.GetBytes("abc"));
        Feed(emulator, "[32l");
        emulator.ProcessData(new byte[] { 0x0A });
        Assert.Equal(3, emulator.Cursor.Column);

        // DEC's number for the same switch, on the same emulator, reaching the same state.
        emulator.ProcessData(Encoding.ASCII.GetBytes("de"));
        Feed(emulator, "[20h");
        emulator.ProcessData(new byte[] { 0x0A });
        Assert.Equal(0, emulator.Cursor.Column);
    }

    /// <summary>
    /// Mode 55 turns auto repeat OFF when SET - the opposite of DEC's private mode 8.
    /// </summary>
    /// <remarks>
    /// Section 8.7.1 gives 55 AR as RM = ON, 1.SM = OFF. DECARM, private mode 8, has SET meaning
    /// repeat. Both are obeyed, each with its own manual's polarity - the same trap the key click
    /// carries, and the reason both have a test.
    /// </remarks>
    [Fact]
    public void ModeFiftyFiveTurnsAutoRepeatOffWhenSet()
    {
        var emulator = new TDV2200Emulator(80, 24);

        // A fresh terminal repeats, so nothing unusual is reported.
        Assert.False(emulator.GetActiveModes().HasFlag(TerminalModes.AutoRepeatDisabled));

        Feed(emulator, "[55h");
        Assert.True(emulator.GetActiveModes().HasFlag(TerminalModes.AutoRepeatDisabled));

        Feed(emulator, "[55l");
        Assert.False(emulator.GetActiveModes().HasFlag(TerminalModes.AutoRepeatDisabled));

        // DEC's number, running the other way: RESET stops the repeat.
        Feed(emulator, "[?8l");
        Assert.True(emulator.GetActiveModes().HasFlag(TerminalModes.AutoRepeatDisabled));

        Feed(emulator, "[?8h");
        Assert.False(emulator.GetActiveModes().HasFlag(TerminalModes.AutoRepeatDisabled));
    }

    /// <summary>
    /// Mode 68 picks the cursor shape, and leaves the blink alone.
    /// </summary>
    /// <remarks>
    /// Section 8.7.1: "68 CT, Cursor Type: LINE / BLOCK". The blink is a separate switch, among the
    /// convenience options NDRQ report type 3 covers, so changing the type must not start or stop
    /// it.
    /// </remarks>
    [Fact]
    public void ModeSixtyEightPicksTheCursorShapeAndKeepsTheBlink()
    {
        var emulator = new TDV2200Emulator(80, 24);

        Feed(emulator, "[68l");                       // LINE
        Assert.Equal(CursorStyle.Underline, emulator.Cursor.Style);

        Feed(emulator, "[68h");                       // BLOCK
        Assert.Equal(CursorStyle.Block, emulator.Cursor.Style);

        // Now with a blinking cursor, set through DECSCUSR: the blink has to survive both moves.
        Feed(emulator, "[1 q");                       // blinking block
        Assert.Equal(CursorStyle.BlinkingBlock, emulator.Cursor.Style);

        Feed(emulator, "[68l");
        Assert.Equal(CursorStyle.BlinkingUnderline, emulator.Cursor.Style);

        Feed(emulator, "[68h");
        Assert.Equal(CursorStyle.BlinkingBlock, emulator.Cursor.Style);
    }

    /// <summary>
    /// A number from the table that names hardware we do not model is counted, not swallowed.
    /// </summary>
    /// <param name="mode">
    /// The mode number from section 8.7.1.
    /// </param>
    /// <remarks>
    /// 40, 42, 43 and 69 are the printer; 67 is the serial handshake. None of them is something
    /// this program can act on, and the point of the test is that they still reach the unhandled
    /// counter with their number attached, which is what makes a real host's use of one visible.
    /// </remarks>
    [Theory]
    [InlineData(40)]
    [InlineData(42)]
    [InlineData(43)]
    [InlineData(67)]
    [InlineData(69)]
    public void AModeThisTerminalCannotActOnIsStillCounted(int mode)
    {
        var emulator = new TDV2200Emulator(80, 24);

        Feed(emulator, "[" + mode + "h");

        Assert.True(emulator.UnrecognisedSequences.ContainsKey("mode " + mode + " set"),
            "mode " + mode + " was swallowed in silence rather than counted with its number");
    }
}
