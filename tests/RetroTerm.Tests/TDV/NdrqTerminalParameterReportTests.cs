using System.Collections.Generic;
using System.Text;
using RetroTerm.Core.Configuration;
using RetroTerm.Core.Terminal.Emulators;
using RetroTerm.Core.Terminal.Emulators.TDV;
using Xunit;

namespace RetroTerm.Tests.TDV;

/// <summary>
/// NDRQ - Request and Report Terminal Parameters, the report a TDV really has.
/// </summary>
/// <remarks>
/// <para><b>Where this comes from</b></para>
/// ND Display Terminal 1200 Functional Specifications, ND-12054-1 EN, section 5.48. Transcribed at
/// <c>spec\TDV1200\ND-12054-1-EN_combined.md</c>. The host sends <c>CSI Ps x</c>, the terminal
/// answers <c>CSI Ps ; n1 ; ... ; nn x</c>, and the first parameter is the report type.
///
/// <para><b>Why it exists at all</b></para>
/// This program used to answer DECRQM on a TDV, with five mode numbers that each named a different
/// switch in the real manuals. DECRQM appears in no TDV manual: TDV 2215 Functional Specifications
/// section 8.7 lists every CSI sequence the terminal accepts and none carries a <c>$</c>
/// intermediate, and section 8.3.2 lists everything it sends, which is CPR alone. NDRQ is what the
/// ND documentation actually describes. The whole story is in
/// <c>docs\TDV-MODES-AND-REPORTS-FROM-THE-MANUALS.md</c>.
///
/// <para><b>What is deliberately NOT claimed</b></para>
/// Report type 3 answers 0, "requested report type not available", which the manual defines for
/// exactly this - it reports front-panel convenience switches this program does not model, so all
/// seven bits would be guesses. Type 4 the manual itself leaves "to be defined later", so 0 is the
/// complete answer there rather than a gap.
///
/// Type 1, the emulator level, answered 0 too until Ronny decided on 11 September 2026 that it
/// should claim 93. That is a considered choice and NOT a measurement - see the remarks on
/// <c>EmulatorLevel</c> for exactly what is and is not known behind it.
/// </remarks>
public class NdrqTerminalParameterReportTests
{
    /// <summary>
    /// The escape character, built from its code so no raw control byte lands in this file.
    /// </summary>
    private const char Escape = (char)0x1B;

    /// <summary>
    /// Builds a TDV2200 and collects everything it puts on the wire.
    /// </summary>
    /// <param name="wire">
    /// Receives the emulator's outbound bytes, decoded as ASCII.
    /// </param>
    /// <returns>
    /// The emulator.
    /// </returns>
    private static TerminalEmulatorBase TdvWatchingTheWire(List<string> wire)
    {
        var emulator = EmulatorFactory.CreateEmulator("TDV2200", 80, 25, 100);
        emulator.DataToSend += bytes => wire.Add(Encoding.ASCII.GetString(bytes));
        return emulator;
    }

    /// <summary>
    /// Feeds the emulator a byte string.
    /// </summary>
    /// <param name="emulator">
    /// The emulator to feed.
    /// </param>
    /// <param name="text">
    /// The bytes, as a string.
    /// </param>
    private static void Send(TerminalEmulatorBase emulator, string text)
    {
        emulator.ProcessData(Encoding.ASCII.GetBytes(text));
    }

    /// <summary>
    /// Asks for one report and returns the single reply.
    /// </summary>
    /// <param name="emulator">
    /// The emulator to ask.
    /// </param>
    /// <param name="wire">
    /// The wire collector belonging to that emulator.
    /// </param>
    /// <param name="reportType">
    /// The report type to request.
    /// </param>
    /// <returns>
    /// The reply, with the escape rendered as "ESC" so a failure message is readable.
    /// </returns>
    private static string AskFor(TerminalEmulatorBase emulator, List<string> wire, int reportType)
    {
        wire.Clear();
        Send(emulator, Escape + "[" + reportType + "x");

        Assert.True(wire.Count == 1, $"expected one reply to NDRQ type {reportType}, got {wire.Count}");
        return wire[0].Replace(Escape.ToString(), "ESC");
    }

    /// <summary>
    /// Report type 2 carries the three mode bitmasks, and every bit follows its mode.
    /// </summary>
    [Fact]
    public void ReportTwoCarriesTheThreeModeBitmasks()
    {
        var wire = new List<string>();
        var emulator = TdvWatchingTheWire(wire);

        // Autowrap is on at power-up, so p2 already has bit 4 set: 16.
        Assert.Equal("ESC[2;0;16;0x", AskFor(emulator, wire, 2));

        // p1 bit 1, IRM. CSI 4 h.
        Send(emulator, Escape + "[4h");
        Assert.Equal("ESC[2;2;16;0x", AskFor(emulator, wire, 2));

        // p2 bit 1, smooth scroll. The TDV number is 60, RT Roll Type.
        Send(emulator, Escape + "[60h");
        Assert.Equal("ESC[2;2;18;0x", AskFor(emulator, wire, 2));

        // p3 bit 0, beginning of line wrap. The TDV number is 31.
        Send(emulator, Escape + "[31h");
        Assert.Equal("ESC[2;2;18;1x", AskFor(emulator, wire, 2));

        // p3 bit 7, numeric pad to Function mode. TDV 2200/9 S User's Guide section 11.2.
        Send(emulator, Escape + "[80h");
        Assert.Equal("ESC[2;2;18;129x", AskFor(emulator, wire, 2));

        // And back down again - a bit that never clears is a bit nobody is reading.
        Send(emulator, Escape + "[4l" + Escape + "[60l" + Escape + "[31l" + Escape + "[80l");
        Assert.Equal("ESC[2;0;16;0x", AskFor(emulator, wire, 2));
    }

    /// <summary>
    /// Report type 6 says which auxiliary devices are fitted.
    /// </summary>
    /// <remarks>
    /// Bit 1 graphics and bit 2 mouse, so 6. Graphics because the vector and sixel decoders are
    /// offered every byte and nothing gates them - the same reasoning as the standing call that a
    /// TDV2200 reports GRAPHICS and TEKTRONIX unconditionally. No card reader is modelled and
    /// printer support is designed but not built, so bits 0 and 3 stay clear.
    /// </remarks>
    [Fact]
    public void ReportSixListsGraphicsAndMouseAndNothingElse()
    {
        var wire = new List<string>();
        var emulator = TdvWatchingTheWire(wire);

        Assert.Equal("ESC[6;6x", AskFor(emulator, wire, 6));
    }

    /// <summary>
    /// Report type 5 latches no error conditions, because this program latches none.
    /// </summary>
    [Fact]
    public void ReportFiveCarriesNoLatchedErrors()
    {
        var wire = new List<string>();
        var emulator = TdvWatchingTheWire(wire);

        Assert.Equal("ESC[5;0x", AskFor(emulator, wire, 5));
    }

    /// <summary>
    /// The types this program cannot fill answer 0, which the manual defines as "not available".
    /// </summary>
    /// <param name="reportType">
    /// The report type to ask for.
    /// </param>
    [Theory]
    [InlineData(3)]   // convenience options - not modelled as terminal state
    [InlineData(4)]   // graphic switches - the manual says "to be defined later"
    [InlineData(7)]   // not a report type at all
    public void TheTypesThisTerminalCannotFillAnswerNotAvailable(int reportType)
    {
        var wire = new List<string>();
        var emulator = TdvWatchingTheWire(wire);

        Assert.Equal("ESC[0x", AskFor(emulator, wire, reportType));
    }

    /// <summary>
    /// Report type 1 carries the emulator level, three digits, zero padded.
    /// </summary>
    /// <remarks>
    /// 93 is Ronny's call of 11 September 2026, not a measurement. Section 5.48 gives the range as
    /// 050 to 100 and names no machine; 93 is the terminal type every D100 session here hands
    /// SINTRAN, and whether that is the same registry as the ND emulator level is not established.
    /// See the remarks on <c>EmulatorLevel</c>.
    ///
    /// The zero padding is the manual's: "3 digits in the range 050 - 100". A bare "93" would be
    /// two.
    /// </remarks>
    [Fact]
    public void ReportOneCarriesTheEmulatorLevelAsThreeDigits()
    {
        var wire = new List<string>();
        var emulator = TdvWatchingTheWire(wire);

        Assert.Equal("ESC[1;093x", AskFor(emulator, wire, 1));
    }

    /// <summary>
    /// With no parameter at all the report type defaults to 1.
    /// </summary>
    /// <remarks>
    /// Section 5.48 gives the default as "Request report type 1".
    /// </remarks>
    [Fact]
    public void NoParameterMeansReportTypeOne()
    {
        var wire = new List<string>();
        var emulator = TdvWatchingTheWire(wire);

        wire.Clear();
        Send(emulator, Escape + "[x");

        Assert.Single(wire);
        Assert.Equal("ESC[1;093x", wire[0].Replace(Escape.ToString(), "ESC"));
    }

    /// <summary>
    /// The <c>$</c> form of the same final is DECFRA and must NOT be taken for NDRQ.
    /// </summary>
    /// <remarks>
    /// <c>CSI Pch;Pt;Pl;Pb;Pr $ x</c> fills a rectangle. NDRQ carries no intermediate and the
    /// handler checks for that, so a rectangle fill must never draw a report out of the terminal.
    ///
    /// The check runs on a TDV2200 AND a VT420, because neither on its own would catch a dropped
    /// guard. A TDV2200 has no rectangle operations at all - only the VT420 profile carries
    /// <c>TerminalFeatures.RectangleOperations</c> - so on the TDV the sequence draws nothing
    /// either way, and only the absence of a reply means anything. The VT420 is where the fill
    /// actually happens, and it is the emulator that would lose a working DECFRA if the two ever
    /// collided.
    /// </remarks>
    [Fact]
    public void TheDollarFormIsStillDecfraAndNeverAReport()
    {
        // Fill rows 1-2, columns 1-4 with 'X' (code 88).
        const string Decfra = "[88;1;1;2;4$x";

        // On a TDV2200: no report. Nothing is painted, because this model has no DECFRA.
        var tdvWire = new List<string>();
        var tdv = TdvWatchingTheWire(tdvWire);
        Send(tdv, Escape + Decfra);
        Assert.True(tdvWire.Count == 0,
            $"DECFRA drew {tdvWire.Count} reply/replies out of a TDV2200 and should draw none");

        // On a VT420: no report, and the rectangle IS filled.
        var vtWire = new List<string>();
        var vt = EmulatorFactory.CreateEmulator("VT420", 80, 24, 100);
        vt.DataToSend += bytes => vtWire.Add(Encoding.ASCII.GetString(bytes));
        Send(vt, Escape + Decfra);

        Assert.True(vtWire.Count == 0,
            $"DECFRA drew {vtWire.Count} reply/replies out of a VT420 and should draw none");
        Assert.Equal((uint)'X', vt.Buffer.GetCell(0, 0).Codepoint);
    }

    /// <summary>
    /// A VT100 must not answer NDRQ - it is an ND sequence, not a DEC one.
    /// </summary>
    /// <remarks>
    /// DEC's DECREQTPARM happens to be the same shape, <c>CSI Ps x</c>, and this program does not
    /// implement it. If it is ever built, the two must stay apart: their parameters mean completely
    /// different things. This test is the tripwire for that.
    /// </remarks>
    [Fact]
    public void AVt100DoesNotAnswerNdrq()
    {
        var wire = new List<string>();
        var emulator = EmulatorFactory.CreateEmulator("VT100", 80, 24, 100);
        emulator.DataToSend += bytes => wire.Add(Encoding.ASCII.GetString(bytes));

        Send(emulator, Escape + "[2x");

        Assert.True(wire.Count == 0, $"a VT100 answered NDRQ with {wire.Count} reply/replies");
    }
}
