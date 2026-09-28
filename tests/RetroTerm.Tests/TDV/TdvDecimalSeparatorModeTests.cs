using System.Collections.Generic;
using System.Text;
using RetroTerm.Core.Configuration;
using RetroTerm.Core.Terminal.Emulators;
using RetroTerm.Core.Terminal.Emulators.TDV;
using Xunit;

namespace RetroTerm.Tests.TDV;

/// <summary>
/// Decimal-separator mode - ND private mode 7, the three-position one.
/// </summary>
/// <remarks>
/// <para><b>Where it comes from</b></para>
/// ND Display Terminal 1200 Functional Specifications section 5.64, the ND private table:
/// "7: Decimal-separator mode (PERIOD/COMMA/SEQUENCE)", with the manual's own drawing of the walk -
/// PERIOD, SM to COMMA, SM to SEQUENCE, and RM back to PERIOD from any of them.
///
/// <para><b>Why it was built</b></para>
/// Ronny asked on 11 September 2026 why one of his keyboards seems to have a comma where another
/// has a point. It is not a keycap: four keyboard photographs in <c>spec\Keyboards</c> all show the
/// same pad. It is this mode. See section 8 of
/// <c>docs\TDV-MODES-AND-REPORTS-FROM-THE-MANUALS.md</c>.
///
/// <para><b>What it deliberately does not do</b></para>
/// It holds the position and reports it. What the decimal key TRANSMITS lives in the key registry,
/// which is keyboard territory and a standing DO-NOT.
/// </remarks>
public class TdvDecimalSeparatorModeTests
{
    /// <summary>
    /// The escape character, built from its code so no raw control byte lands in this file.
    /// </summary>
    private const char Escape = (char)0x1B;

    /// <summary>
    /// Builds a TDV2200 and feeds it a sequence.
    /// </summary>
    /// <param name="emulator">
    /// The emulator to feed.
    /// </param>
    /// <param name="sequence">
    /// The sequence after the escape, for example "[&gt;7h".
    /// </param>
    private static void Feed(TDVEmulatorBase emulator, string sequence)
    {
        emulator.ProcessData(Encoding.ASCII.GetBytes(Escape + sequence));
    }

    /// <summary>
    /// A fresh terminal uses a full stop.
    /// </summary>
    [Fact]
    public void ItStartsOnPeriod()
    {
        var emulator = new TDV2200Emulator(80, 24);

        Assert.Equal(TDVEmulatorBase.TDVDecimalSeparator.Period, emulator.DecimalSeparatorMode);
    }

    /// <summary>
    /// Each SM advances one position, and SEQUENCE is as far as it goes.
    /// </summary>
    /// <remarks>
    /// This is the part a two-position mode would get wrong: a second <c>CSI &gt; 7 h</c> is not a
    /// repeat of the first.
    /// </remarks>
    [Fact]
    public void EachSetAdvancesOnePositionAndStopsAtSequence()
    {
        var emulator = new TDV2200Emulator(80, 24);

        Feed(emulator, "[>7h");
        Assert.Equal(TDVEmulatorBase.TDVDecimalSeparator.Comma, emulator.DecimalSeparatorMode);

        Feed(emulator, "[>7h");
        Assert.Equal(TDVEmulatorBase.TDVDecimalSeparator.Sequence, emulator.DecimalSeparatorMode);

        Feed(emulator, "[>7h");
        Assert.Equal(TDVEmulatorBase.TDVDecimalSeparator.Sequence, emulator.DecimalSeparatorMode);
    }

    /// <summary>
    /// RM goes home from wherever the switch is.
    /// </summary>
    /// <param name="setsFirst">
    /// How many times to advance before resetting.
    /// </param>
    /// <remarks>
    /// THE MIDDLE ASSERTION IS THE POINT. Measured on 11 September 2026 by breaking the mode on
    /// purpose: with the sequence no longer recognised, only two of this file&#39;s eight tests went
    /// red, and this theory was one of the six that stayed green - because PERIOD is also the
    /// state of a terminal that ignored everything it was sent. Checking that the switch actually
    /// MOVED first is what makes the reset mean something.
    /// </remarks>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void ResetReturnsToPeriodFromAnyPosition(int setsFirst)
    {
        var emulator = new TDV2200Emulator(80, 24);

        for (int i = 0; i < setsFirst; i++)
        {
            Feed(emulator, "[>7h");
        }

        Assert.NotEqual(TDVEmulatorBase.TDVDecimalSeparator.Period, emulator.DecimalSeparatorMode);

        Feed(emulator, "[>7l");

        Assert.Equal(TDVEmulatorBase.TDVDecimalSeparator.Period, emulator.DecimalSeparatorMode);
    }

    /// <summary>
    /// A terminal reset puts the switch back to PERIOD.
    /// </summary>
    [Fact]
    public void ResetToInitialStateReturnsToPeriod()
    {
        var emulator = new TDV2200Emulator(80, 24);
        Feed(emulator, "[>7h");

        // Same trap as the theory above: without this the test passes on a terminal that never
        // moved the switch at all.
        Assert.Equal(TDVEmulatorBase.TDVDecimalSeparator.Comma, emulator.DecimalSeparatorMode);

        emulator.ResetToInitialState();

        Assert.Equal(TDVEmulatorBase.TDVDecimalSeparator.Period, emulator.DecimalSeparatorMode);
    }

    /// <summary>
    /// The mode number is 7 and the marker is the ND private one, so 7 without a marker is not it.
    /// </summary>
    /// <remarks>
    /// Mode 7 with no marker is VEM, the vertical editing mode - 2215 section 8.7.1 and the ND-1200
    /// ANSI table both list it. Confusing the two families is the fault the whole mode alignment of
    /// 11 September 2026 exists to undo, so it gets a test.
    /// </remarks>
    [Fact]
    public void PlainModeSevenIsNotTheDecimalSeparator()
    {
        var emulator = new TDV2200Emulator(80, 24);

        // The marked form first, to prove this emulator IS listening to mode 7 on the ND private
        // marker - otherwise the assertion below passes on a terminal that ignores both.
        Feed(emulator, "[>7h");
        Assert.Equal(TDVEmulatorBase.TDVDecimalSeparator.Comma, emulator.DecimalSeparatorMode);

        // Now the unmarked one, which is VEM and must not touch the separator.
        Feed(emulator, "[7h");
        Assert.Equal(TDVEmulatorBase.TDVDecimalSeparator.Comma, emulator.DecimalSeparatorMode);
    }

    /// <summary>
    /// NDRQ report 2 carries the position in parameter 3, bits 5 and 6.
    /// </summary>
    /// <remarks>
    /// Section 5.48 names those two bits "Decimal separator mode, 1" and "Decimal separator mode,
    /// 2". Two bits for three positions, so the position goes in as a number: PERIOD 0, COMMA 32,
    /// SEQUENCE 64. They answered 0 whatever the terminal was doing until this mode existed.
    ///
    /// Autowrap is on at power-up, so parameter 2 reads 16 throughout.
    /// </remarks>
    [Fact]
    public void ReportTwoCarriesTheDecimalSeparatorInParameterThree()
    {
        var wire = new List<string>();
        var emulator = EmulatorFactory.CreateEmulator("TDV2200", 80, 25, 100);
        emulator.DataToSend += bytes => wire.Add(Encoding.ASCII.GetString(bytes));

        Assert.Equal("ESC[2;0;16;0x", AskForReportTwo(emulator, wire));

        emulator.ProcessData(Encoding.ASCII.GetBytes(Escape + "[>7h"));
        Assert.Equal("ESC[2;0;16;32x", AskForReportTwo(emulator, wire));

        emulator.ProcessData(Encoding.ASCII.GetBytes(Escape + "[>7h"));
        Assert.Equal("ESC[2;0;16;64x", AskForReportTwo(emulator, wire));

        emulator.ProcessData(Encoding.ASCII.GetBytes(Escape + "[>7l"));
        Assert.Equal("ESC[2;0;16;0x", AskForReportTwo(emulator, wire));
    }

    /// <summary>
    /// Asks for NDRQ report type 2 and returns the single reply.
    /// </summary>
    /// <param name="emulator">
    /// The emulator to ask.
    /// </param>
    /// <param name="wire">
    /// The wire collector belonging to that emulator.
    /// </param>
    /// <returns>
    /// The reply, with the escape rendered as "ESC" so a failure message is readable.
    /// </returns>
    private static string AskForReportTwo(TerminalEmulatorBase emulator, List<string> wire)
    {
        wire.Clear();
        emulator.ProcessData(Encoding.ASCII.GetBytes(Escape + "[2x"));

        Assert.True(wire.Count == 1, $"expected one reply to NDRQ type 2, got {wire.Count}");
        return wire[0].Replace(Escape.ToString(), "ESC");
    }
}
