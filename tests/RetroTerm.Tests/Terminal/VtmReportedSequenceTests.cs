using System.Text;
using RetroTerm.Core.Configuration;
using RetroTerm.Core.Terminal.Emulators;
using RetroTerm.Core.Terminal.Emulators.TDV;
using Xunit;

namespace RetroTerm.Tests.Terminal;

/// <summary>
/// Two documented DEC sequences that a real ND program sends and this decoder called unknown.
/// </summary>
/// <remarks>
/// <para><b>Where these came from</b></para>
/// Reported on 25 August 2026 by the session driving VTM - the ND terminal-independence layer - on
/// a real ND-100 over a VT100 session. VTM sends both on EVERY run: <c>ESC &lt;</c> first thing, and
/// a parameterless <c>CSI q</c> on exit.
/// <para><b>Why "harmless" was not good enough</b></para>
/// Neither one broke anything. One asserts a mode that is already true and the other drives lamps
/// this emulator has not got. The cost was that both showed as UNKNOWN in the Protocol Monitor on
/// every single run, and a reader who learns to ignore the noise is a reader who will miss a real
/// unknown sequence sitting in the middle of it. That is the whole reason the counter exists.
/// <para><b>Why they hid</b></para>
/// Both were HALF handled, which is worse than not at all for finding them.
/// <c>ESC &lt;</c> had a case, but only inside the VT52 branch - unreachable once the terminal is in
/// ANSI mode, which is exactly when a host sends it to assert ANSI. And <c>CSI q</c> had a case, but
/// only with a SPACE intermediate, where the same final byte means DECSCUSR. The final looked taken.
/// </remarks>
public class VtmReportedSequenceTests
{
    /// <summary>
    /// The escape character, built from its code so no raw control byte lands in this file.
    /// </summary>
    private const char Escape = (char)0x1B;

    /// <summary>
    /// A fresh VT100.
    /// </summary>
    /// <returns>
    /// The emulator.
    /// </returns>
    private static TerminalEmulatorBase NewVt100()
    {
        return EmulatorFactory.CreateEmulator("VT100", 80, 24, 100);
    }

    #region ESC < - DECANM, back to ANSI

    [Fact]
    public void TheEscapeToAnsiIsNotReportedAsUnknownWhenAlreadyInAnsi()
    {
        // The exact thing VTM does on every run.
        var emulator = NewVt100();

        emulator.ProcessData(Encoding.ASCII.GetBytes(Escape + "<"));

        Assert.False(emulator.UnrecognisedSequences.ContainsKey("ESC <"),
            "ESC < is a documented DEC sequence and a host is entitled to send it to assert ANSI " +
            "mode. Reporting it as unknown fills the trace with noise a reader has to learn to skip.");
    }

    [Fact]
    public void TheEscapeToAnsiPrintsNothing()
    {
        // Swallowed, not printed. A sequence that is "handled" by putting a less-than sign on the
        // screen would be a worse defect than the one being fixed.
        var emulator = NewVt100();

        emulator.ProcessData(Encoding.ASCII.GetBytes(Escape + "<"));

        // An untouched cell holds codepoint zero, not a space - so "nothing was printed" is stated
        // as "no less-than sign landed here" rather than as an equality against a blank, which is
        // what the first draft of this assertion got wrong.
        Assert.NotEqual((uint)'<', emulator.GetBuffer().GetCell(0, 0).Codepoint);
        Assert.Equal(0, emulator.Cursor.Column);
    }

    #endregion

    #region CSI q - DECLL, the keyboard lamps

    [Fact]
    public void LoadLedsIsNotReportedAsUnknown()
    {
        // What VTM sends on exit: parameterless, meaning "put all four lamps out".
        var emulator = NewVt100();

        emulator.ProcessData(Encoding.ASCII.GetBytes(Escape + "[q"));

        Assert.False(emulator.UnrecognisedSequences.ContainsKey("CSI q"));
    }

    [Fact]
    public void ALampCanBeLitAndPutOut()
    {
        // The state is KEPT rather than swallowed. There are no lamps to light here, but a sequence
        // that is accepted and then forgotten is the exact failure this sweep was about - the
        // counter goes quiet while the behaviour stays missing.
        var emulator = NewVt100();

        emulator.ProcessData(Encoding.ASCII.GetBytes(Escape + "[2q"));
        Assert.Equal(0b0010, emulator.KeyboardLeds);

        emulator.ProcessData(Encoding.ASCII.GetBytes(Escape + "[22q"));
        Assert.Equal(0, emulator.KeyboardLeds);
    }

    [Fact]
    public void AParameterlessLoadLedsPutsThemAllOut()
    {
        var emulator = NewVt100();

        emulator.ProcessData(Encoding.ASCII.GetBytes(Escape + "[1q"));
        emulator.ProcessData(Encoding.ASCII.GetBytes(Escape + "[3q"));
        Assert.Equal(0b0101, emulator.KeyboardLeds);

        emulator.ProcessData(Encoding.ASCII.GetBytes(Escape + "[q"));

        Assert.Equal(0, emulator.KeyboardLeds);
    }

    [Fact]
    public void TheCursorStyleSequenceStillWorks()
    {
        // CSI q with a SPACE intermediate is DECSCUSR, a completely different sequence that happens
        // to share its final byte. Adding DECLL must not have stolen it - and this is the test that
        // would have gone red if the new case had been put in the wrong switch.
        var emulator = NewVt100();

        emulator.ProcessData(Encoding.ASCII.GetBytes(Escape + "[4 q"));

        Assert.Equal(RetroTerm.Core.Terminal.CursorStyle.Underline, emulator.Cursor.Style);
        Assert.Equal(0, emulator.KeyboardLeds);
    }

    #endregion

    #region The key names that looked like duplicates and are not

    [Fact]
    public void AKeyLegendAndItsGridPositionNameTheSameKey()
    {
        // The third thing that session reported: SENDKEY F1 and SENDKEY F51 produce byte-identical
        // output, which looked like a half-filled table. It is not.
        //
        // F51 is a GRID POSITION and F1 is the LEGEND PRINTED ON THAT KEY. They are one key with two
        // names, which is what the command's own error message has always said. The registry is
        // right; the tool's parameter description was wrong, saying "F1..F52" and so mixing the two
        // naming systems into one range that matches neither.
        //
        // Pinned here so nobody else spends an afternoon deciding it is a bug.
        string? gridForLegend = TDV2200KeyRegistry.GetGridForName("F1");

        Assert.Equal("F51", gridForLegend);
        Assert.True(TDV2200KeyRegistry.TryGetKey("F51", out _),
            "F51 is a real grid position, so SENDKEY accepts it directly as well as by legend");
    }

    [Fact]
    public void TheLegendsFiveToEightSitOnADifferentRowAndThatIsWhyFiftyFiveDoesNotExist()
    {
        // The other half of the same confusion. F5..F8 are legends on grid row E, not row F, so
        // there is no F55 to find and its absence is not a gap.
        string? gridForF5 = TDV2200KeyRegistry.GetGridForName("F5");

        Assert.NotNull(gridForF5);
        Assert.StartsWith("E", gridForF5);
        Assert.False(TDV2200KeyRegistry.TryGetKey("F55", out _),
            "there is no F55 key, and its absence is not a gap - the F5 legend lives at grid E51");
    }

    #endregion
}
