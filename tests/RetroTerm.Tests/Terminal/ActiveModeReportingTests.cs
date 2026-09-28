using System;
using System.Text;
using RetroTerm.Core.Terminal.Emulators;
using RetroTerm.Core.Terminal.Emulators.TDV;
using RetroTerm.Core.Terminal.Input;
using Xunit;

namespace RetroTerm.Tests.Terminal;

/// <summary>
/// Phase 3 part 4: connecting the terminal modes to the keyboard (problem B.10).
///
/// DECCKM was stored by the emulator, and the keyboard mapper had a branch to turn arrow keys into
/// their application form. The two had never been connected. What actually called the mapper was
/// the UI, and it built its mode set from a chain of emulator TYPE CHECKS that only ever produced
/// the TDV flags — so nothing anywhere set TerminalModes.ApplicationCursorKeys and the mapper's
/// branch for it was unreachable.
///
/// The visible effect: vi, less, and most full-screen programs enable DECCKM and expect
/// "ESC O A" from the up arrow. They got "ESC [ A" instead, forever.
///
/// The type-check chain was also duplicated in TerminalCanvas and MainWindow, and was business
/// logic living in a view. The emulator owns the mode state, so the emulator answers now.
/// </summary>
public class ActiveModeReportingTests
{
    private static void Feed(TerminalEmulatorBase emulator, string s)
    {
        emulator.ProcessData(Encoding.ASCII.GetBytes(s));
    }

    // ─────────────────────────────────────────────────────────────
    // The emulator reports its own modes
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void AFreshTerminalReportsNoModes()
    {
        var emulator = new VT100Emulator(20, 5);

        Assert.Equal(TerminalModes.None, emulator.GetActiveModes());
    }

    [Fact]
    public void DeccKmShowsUpInTheReportedModes()
    {
        var emulator = new VT100Emulator(20, 5);

        Feed(emulator, "\x1b[?1h");

        Assert.True(emulator.GetActiveModes().HasFlag(TerminalModes.ApplicationCursorKeys));
    }

    [Fact]
    public void ResettingDeccKmTakesItOutAgain()
    {
        var emulator = new VT100Emulator(20, 5);

        Feed(emulator, "\x1b[?1h");
        Feed(emulator, "\x1b[?1l");

        Assert.False(emulator.GetActiveModes().HasFlag(TerminalModes.ApplicationCursorKeys));
    }

    [Fact]
    public void DeckPamShowsUpInTheReportedModes()
    {
        // DECKPAM is ESC =, built from chars because "\x" is variable length and '=' is not hex —
        // safe either way here, but the explicit form is the house style now.
        var emulator = new VT100Emulator(20, 5);

        Feed(emulator, new string(new[] { (char)0x1B, '=' }));

        Assert.True(emulator.GetActiveModes().HasFlag(TerminalModes.ApplicationKeypad));

        Feed(emulator, new string(new[] { (char)0x1B, '>' }));   // DECKPNM

        Assert.False(emulator.GetActiveModes().HasFlag(TerminalModes.ApplicationKeypad));
    }

    [Fact]
    public void BothKeyboardModesCanBeOnAtOnce()
    {
        var emulator = new VT100Emulator(20, 5);

        Feed(emulator, "\x1b[?1h");
        Feed(emulator, new string(new[] { (char)0x1B, '=' }));

        var modes = emulator.GetActiveModes();
        Assert.True(modes.HasFlag(TerminalModes.ApplicationCursorKeys));
        Assert.True(modes.HasFlag(TerminalModes.ApplicationKeypad));
    }

    [Fact]
    public void ResetClearsTheReportedModes()
    {
        var emulator = new VT100Emulator(20, 5);

        Feed(emulator, "\x1b[?1h");
        Feed(emulator, new string(new[] { (char)0x1B, 'c' }));   // RIS

        Assert.Equal(TerminalModes.None, emulator.GetActiveModes());
    }

    // ─────────────────────────────────────────────────────────────
    // The TDV models report their own flag, without anyone type-checking them
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void Tdv2215ReportsItsOwnModelFlag()
    {
        var emulator = new TDV2215Emulator(80, 24);

        Assert.True(emulator.GetActiveModes().HasFlag(TerminalModes.TDV2215Mode));
    }

    [Fact]
    public void Tdv2200ReportsItsOwnModelFlag()
    {
        var emulator = new TDV2200Emulator(80, 24);

        Assert.True(emulator.GetActiveModes().HasFlag(TerminalModes.TDV2200Mode));
    }

    [Fact]
    public void ATdvInCompatibilityModeReportsTheCompatibilityFlagInstead()
    {
        var emulator = new TDV2215Emulator(80, 24);

        // CSI 66 l, no private marker. Mode 66 is EC, the Extended Control switch, and its RESET is
        // the 2115 side - TDV 2215 section 3.1, ND-1200 section 8.1. This test used to send
        // CSI ? 40 h, which is the printer code format with a marker no TDV mode table carries.
        Feed(emulator, "\x1b[66l");   // 2115 compatibility mode

        var modes = emulator.GetActiveModes();
        Assert.True(modes.HasFlag(TerminalModes.TDV2115Mode));
        Assert.False(modes.HasFlag(TerminalModes.TDV2215Mode));
    }

    [Fact]
    public void ATdvAlsoReportsTheKeyboardModes()
    {
        // The model flag and the keyboard modes are independent; the old UI chain returned the
        // model flag ALONE, which is how the keyboard modes went missing on TDV sessions too.
        //
        // DECKPAM is used here rather than DECCKM because of the mode-number collision below.
        var emulator = new TDV2215Emulator(80, 24);

        Feed(emulator, new string(new[] { (char)0x1B, '=' }));   // DECKPAM

        var modes = emulator.GetActiveModes();
        Assert.True(modes.HasFlag(TerminalModes.ApplicationKeypad));
        Assert.True(modes.HasFlag(TerminalModes.TDV2215Mode));
    }

    /// <summary>
    /// On a TDV2215, private mode 1 is DECCKM, exactly as it is on a VT100.
    /// </summary>
    /// <remarks>
    /// This test used to assert the OPPOSITE, and said so plainly: it documented that
    /// TDV2215Emulator claimed private mode 1 for "extended mode" and swallowed it before the base
    /// class could treat it as DECCKM, and it left the question open - "whether that is right
    /// depends on the TDV2215 specification, which I have not verified".
    ///
    /// The manuals settled it on 11 September 2026. TDV 2215 section 8.7.1 lists every mode the
    /// host may set and extended control is 66, an ANSI mode with no marker. ND-1200 section 5.64
    /// gives the DEC-compatible '?' family, where 1 is cursor key mode - DEC's own number meaning
    /// DEC's own thing. So the emulator changed, as that test asked it to.
    /// </remarks>
    [Fact]
    public void OnTheTdv2215PrivateModeOneIsDeccKm_LikeEverywhereElse()
    {
        var emulator = new TDV2215Emulator(80, 24);

        Feed(emulator, "\x1b[?1h");

        Assert.True(emulator.GetActiveModes().HasFlag(TerminalModes.ApplicationCursorKeys));

        Feed(emulator, "\x1b[?1l");

        Assert.False(emulator.GetActiveModes().HasFlag(TerminalModes.ApplicationCursorKeys));
    }

    // ─────────────────────────────────────────────────────────────
    // End to end: the mode reaches the mapper and changes the bytes
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void ArrowKeysSwitchToTheApplicationFormWhenDeccKmIsOn()
    {
        // The whole point, stated as one test: a host turns DECCKM on, and the bytes the terminal
        // sends for the up arrow change.
        var emulator = new VT100Emulator(20, 5);
        var mapper = KeyboardMapperFactory.CreateMapper("VT100");

        const int vkUp = 38;
        var normal = mapper.MapKey(vkUp, KeyModifiers.None, emulator.GetActiveModes());

        Feed(emulator, "\x1b[?1h");
        var application = mapper.MapKey(vkUp, KeyModifiers.None, emulator.GetActiveModes());

        Assert.Equal("\x1b[A", normal);
        Assert.Equal("\x1bOA", application);
    }

    [Theory]
    [InlineData(38, "\x1b[A", "\x1bOA")]   // Up
    [InlineData(40, "\x1b[B", "\x1bOB")]   // Down
    [InlineData(39, "\x1b[C", "\x1bOC")]   // Right
    [InlineData(37, "\x1b[D", "\x1bOD")]   // Left
    public void EveryArrowKeyHasBothForms(int vkCode, string normal, string application)
    {
        var emulator = new VT100Emulator(20, 5);
        var mapper = KeyboardMapperFactory.CreateMapper("VT100");

        Assert.Equal(normal, mapper.MapKey(vkCode, KeyModifiers.None, emulator.GetActiveModes()));

        Feed(emulator, "\x1b[?1h");
        Assert.Equal(application, mapper.MapKey(vkCode, KeyModifiers.None, emulator.GetActiveModes()));
    }

    [Fact]
    public void TurningDeccKmBackOffRestoresTheNormalForm()
    {
        var emulator = new VT100Emulator(20, 5);
        var mapper = KeyboardMapperFactory.CreateMapper("VT100");

        Feed(emulator, "\x1b[?1h");
        Feed(emulator, "\x1b[?1l");

        Assert.Equal("\x1b[A", mapper.MapKey(38, KeyModifiers.None, emulator.GetActiveModes()));
    }
}
