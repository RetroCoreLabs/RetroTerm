using System.Collections.Generic;
using System.Text;
using RetroTerm.Core.Configuration;
using RetroTerm.Core.Terminal.Emulators;
using RetroTerm.Core.Terminal.Emulators.TDV;
using Xunit;

namespace RetroTerm.Tests.TDV;

/// <summary>
/// The four ND private soft switches: roll/page, key click, and the two label modes.
/// </summary>
/// <remarks>
/// <para><b>Where they come from</b></para>
/// ND Display Terminal 1200 Functional Specifications section 5.64, the ND private table:
/// 3 roll/page (ROLL, PAGE), 4 key click (DISABLE, ENABLE), 5 PUSH-key label (ON, OFF),
/// 6 PROGRAM-key label (ON, OFF). Two of them have a second sequence in TDV 2215 section 8.7.1,
/// where the same switches appear as ANSI modes 47 and 53.
///
/// <para><b>Why they were built</b></para>
/// NDRQ report type 2 reports all eight bits of parameter 3, and six of the eight answered 0
/// whatever the terminal had been told, because nothing held the state. A report that always says
/// the same thing is worse than no report: a host cannot tell "reset" from "not implemented".
///
/// <para><b>What they do NOT do</b></para>
/// They hold the position and report it. The screen still rolls in PAGE mode, no sound is made for
/// a key click, and the virtual keyboard - a standing DO-NOT - draws its labels regardless.
/// </remarks>
public class TdvNdPrivateSoftSwitchTests
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
    /// Roll/page follows the ND private number and the 2215's ANSI number alike.
    /// </summary>
    /// <remarks>
    /// ND-1200 mode 3 and 2215 mode 47 are the same switch, and both give RESET as ROLL and SET as
    /// PAGE - so unlike the key click below, there is no polarity argument between the manuals.
    /// </remarks>
    [Fact]
    public void RollPageFollowsBothOfItsNumbers()
    {
        var emulator = new TDV2200Emulator(80, 24);
        Assert.False(emulator.PageMode);

        Feed(emulator, "[>3h");
        Assert.True(emulator.PageMode);

        Feed(emulator, "[>3l");
        Assert.False(emulator.PageMode);

        // The 2215's own number for the same switch.
        Feed(emulator, "[47h");
        Assert.True(emulator.PageMode);

        Feed(emulator, "[47l");
        Assert.False(emulator.PageMode);
    }

    /// <summary>
    /// THE KEY CLICK RUNS OPPOSITE WAYS IN THE TWO MANUALS, and both are obeyed.
    /// </summary>
    /// <remarks>
    /// ND-1200 section 5.64 mode 4: RESET is DISABLE, SET is ENABLE.
    /// TDV 2215 section 8.7.1 mode 53, KC: RM is ON, 1.SM is OFF.
    ///
    /// So <c>CSI &gt; 4 h</c> turns the click ON and <c>CSI 53 h</c> turns it OFF. Writing one
    /// polarity for both would silently break whichever host used the other manual, and this is
    /// the test that would catch it.
    /// </remarks>
    [Fact]
    public void TheKeyClickIsSetOppositeWaysByTheTwoManuals()
    {
        var emulator = new TDV2200Emulator(80, 24);
        Assert.False(emulator.KeyClickEnabled);

        // ND-1200: SET enables.
        Feed(emulator, "[>4h");
        Assert.True(emulator.KeyClickEnabled);

        // 2215: SET turns it OFF.
        Feed(emulator, "[53h");
        Assert.False(emulator.KeyClickEnabled);

        // 2215: RESET turns it back ON.
        Feed(emulator, "[53l");
        Assert.True(emulator.KeyClickEnabled);

        // ND-1200: RESET disables.
        Feed(emulator, "[>4l");
        Assert.False(emulator.KeyClickEnabled);
    }

    /// <summary>
    /// The two label modes hide labels on SET and show them on RESET.
    /// </summary>
    /// <param name="mode">
    /// The ND private mode number: 5 for PUSH keys, 6 for PROGRAM keys.
    /// </param>
    /// <param name="isPushKeys">
    /// True to read the PUSH-key flag, false for the PROGRAM-key flag.
    /// </param>
    [Theory]
    [InlineData(5, true)]
    [InlineData(6, false)]
    public void TheLabelModesHideOnSetAndShowOnReset(int mode, bool isPushKeys)
    {
        var emulator = new TDV2200Emulator(80, 24);

        Feed(emulator, "[>" + mode + "h");
        Assert.True(isPushKeys ? emulator.PushKeyLabelsHidden : emulator.ProgramKeyLabelsHidden);

        // And the other one is untouched - these are two switches, not one.
        Assert.False(isPushKeys ? emulator.ProgramKeyLabelsHidden : emulator.PushKeyLabelsHidden);

        Feed(emulator, "[>" + mode + "l");
        Assert.False(isPushKeys ? emulator.PushKeyLabelsHidden : emulator.ProgramKeyLabelsHidden);
    }

    /// <summary>
    /// A terminal reset puts all four back where they started.
    /// </summary>
    [Fact]
    public void ResetToInitialStateReturnsAllFour()
    {
        var emulator = new TDV2200Emulator(80, 24);

        Feed(emulator, "[>3h");
        Feed(emulator, "[>4h");
        Feed(emulator, "[>5h");
        Feed(emulator, "[>6h");

        Assert.True(emulator.PageMode);
        Assert.True(emulator.KeyClickEnabled);
        Assert.True(emulator.PushKeyLabelsHidden);
        Assert.True(emulator.ProgramKeyLabelsHidden);

        emulator.ResetToInitialState();

        Assert.False(emulator.PageMode);
        Assert.False(emulator.KeyClickEnabled);
        Assert.False(emulator.PushKeyLabelsHidden);
        Assert.False(emulator.ProgramKeyLabelsHidden);
    }

    /// <summary>
    /// Every one of the eight bits of NDRQ report 2, parameter 3, now moves.
    /// </summary>
    /// <remarks>
    /// Section 5.48's bit order: 0 beginning of line wrap, 1 roll/page, 2 key klick, 3 PUSH-key
    /// label, 4 PROGRAM-key label, 5 and 6 decimal separator, 7 numeric pad. Six of the eight
    /// answered 0 whatever the terminal was doing until 11 September 2026.
    ///
    /// The sequences are sent one at a time, each followed by a report, so a bit that moves the
    /// wrong neighbour is caught rather than averaged out.
    /// </remarks>
    [Fact]
    public void EveryBitOfParameterThreeMoves()
    {
        var wire = new List<string>();
        var emulator = EmulatorFactory.CreateEmulator("TDV2200", 80, 25, 100);
        emulator.DataToSend += bytes => wire.Add(Encoding.ASCII.GetString(bytes));

        Assert.Equal(0, ParameterThreeOfReportTwo(emulator, wire));

        emulator.ProcessData(Encoding.ASCII.GetBytes(Escape + "[31h"));    // bit 0
        Assert.Equal(1, ParameterThreeOfReportTwo(emulator, wire));

        emulator.ProcessData(Encoding.ASCII.GetBytes(Escape + "[>3h"));    // bit 1
        Assert.Equal(3, ParameterThreeOfReportTwo(emulator, wire));

        emulator.ProcessData(Encoding.ASCII.GetBytes(Escape + "[>4h"));    // bit 2
        Assert.Equal(7, ParameterThreeOfReportTwo(emulator, wire));

        emulator.ProcessData(Encoding.ASCII.GetBytes(Escape + "[>5h"));    // bit 3
        Assert.Equal(15, ParameterThreeOfReportTwo(emulator, wire));

        emulator.ProcessData(Encoding.ASCII.GetBytes(Escape + "[>6h"));    // bit 4
        Assert.Equal(31, ParameterThreeOfReportTwo(emulator, wire));

        emulator.ProcessData(Encoding.ASCII.GetBytes(Escape + "[>7h"));    // bits 5-6: COMMA
        Assert.Equal(63, ParameterThreeOfReportTwo(emulator, wire));

        emulator.ProcessData(Encoding.ASCII.GetBytes(Escape + "[80h"));    // bit 7
        Assert.Equal(191, ParameterThreeOfReportTwo(emulator, wire));
    }

    /// <summary>
    /// Asks for NDRQ report 2 and returns parameter 3 as a number.
    /// </summary>
    /// <param name="emulator">
    /// The emulator to ask.
    /// </param>
    /// <param name="wire">
    /// The wire collector belonging to that emulator.
    /// </param>
    /// <returns>
    /// The third parameter of the reply.
    /// </returns>
    private static int ParameterThreeOfReportTwo(TerminalEmulatorBase emulator, List<string> wire)
    {
        wire.Clear();
        emulator.ProcessData(Encoding.ASCII.GetBytes(Escape + "[2x"));

        Assert.True(wire.Count == 1, $"expected one reply to NDRQ type 2, got {wire.Count}");

        // ESC [ 2 ; p1 ; p2 ; p3 x
        var reply = wire[0];
        var parts = reply.Substring(2, reply.Length - 3).Split(';');
        Assert.True(parts.Length == 4, $"expected four parameters in '{reply}', got {parts.Length}");

        return int.Parse(parts[3]);
    }
}
