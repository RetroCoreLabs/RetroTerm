using System.Text;
using RetroTerm.Core.Configuration;
using RetroTerm.Core.Terminal.Emulators;
using RetroTerm.Core.Terminal.Input;
using Xunit;

namespace RetroTerm.Tests.Terminal;

/// <summary>
/// The two DEC private modes that change what the keyboard sends: DECNKM and DECBKM.
/// </summary>
/// <remarks>
/// <para><b>Why these two together</b></para>
/// Both were found missing by <see cref="DecPrivateModeCoverageTests"/> on 25 August 2026, and both
/// are only real if they reach the KEYBOARD. A flag on the emulator that no mapper reads would be
/// the same dead code as <c>TDV2200Emulator._isTektronixMode</c>, which is set, cleared, and then
/// used for nothing but a capability string. So every test here goes through the mapper.
/// <para><b>The route</b></para>
/// The emulator owns the state and answers <c>GetActiveModes()</c>; the mapper reads that. The UI
/// does not decide - it used to, through a chain of emulator type checks duplicated in two views,
/// and ending that is what <c>GetActiveModes</c> exists for.
/// </remarks>
public class DecKeyboardModeTests
{
    /// <summary>
    /// The escape character, built from its code so no raw control byte lands in this file.
    /// </summary>
    private const char Escape = (char)0x1B;

    /// <summary>
    /// The Windows virtual-key code for the backarrow key.
    /// </summary>
    private const int VkBack = 8;

    /// <summary>
    /// A fresh VT340.
    /// </summary>
    /// <returns>
    /// The emulator.
    /// </returns>
    private static TerminalEmulatorBase NewVt340()
    {
        return EmulatorFactory.CreateEmulator("VT340", 80, 24, 100);
    }

    /// <summary>
    /// Sends a private mode to a fresh terminal and reports the modes the keyboard would see.
    /// </summary>
    /// <param name="mode">
    /// The private mode number.
    /// </param>
    /// <param name="enable">
    /// True to set it, false to reset it.
    /// </param>
    /// <returns>
    /// The active modes afterwards.
    /// </returns>
    private static TerminalModes ModesAfter(int mode, bool enable)
    {
        var emulator = NewVt340();
        emulator.ProcessData(Encoding.ASCII.GetBytes(Escape + "[?" + mode + (enable ? "h" : "l")));
        return emulator.GetActiveModes();
    }

    /// <summary>
    /// What a VT220 keyboard sends for one key under the given modes.
    /// </summary>
    /// <param name="keyCode">
    /// The virtual-key code.
    /// </param>
    /// <param name="modes">
    /// The terminal modes in force.
    /// </param>
    /// <returns>
    /// The bytes, or null when the key maps to nothing.
    /// </returns>
    private static string? KeySends(int keyCode, TerminalModes modes)
    {
        var mapper = new VT220KeyboardMapper();
        return mapper.MapKey(keyCode, KeyModifiers.None, modes);
    }

    #region DECNKM - private mode 66

    [Fact]
    public void ModeSixtySixTurnsOnTheApplicationKeypad()
    {
        // VT320 and later let a host set the application keypad as a MODE as well as with the older
        // two-character ESC = form. Both must reach the same flag - two flags would drift apart the
        // first time only one of them was reset.
        Assert.True(ModesAfter(66, true).HasFlag(TerminalModes.ApplicationKeypad));
    }

    [Fact]
    public void ResettingModeSixtySixTurnsItOffAgain()
    {
        var emulator = NewVt340();

        emulator.ProcessData(Encoding.ASCII.GetBytes(Escape + "[?66h"));
        Assert.True(emulator.GetActiveModes().HasFlag(TerminalModes.ApplicationKeypad));

        emulator.ProcessData(Encoding.ASCII.GetBytes(Escape + "[?66l"));
        Assert.False(emulator.GetActiveModes().HasFlag(TerminalModes.ApplicationKeypad));
    }

    [Fact]
    public void TheModeFormAndTheEscapeEqualsFormReachTheSameFlag()
    {
        // The point of the previous test stated as its own case: if these ever stop agreeing, a host
        // that sets one and resets the other leaves the keypad in a state neither asked for.
        var byMode = NewVt340();
        byMode.ProcessData(Encoding.ASCII.GetBytes(Escape + "[?66h"));

        var bySequence = NewVt340();
        bySequence.ProcessData(Encoding.ASCII.GetBytes(Escape + "="));

        Assert.Equal(bySequence.GetActiveModes().HasFlag(TerminalModes.ApplicationKeypad),
                     byMode.GetActiveModes().HasFlag(TerminalModes.ApplicationKeypad));
    }

    #endregion

    #region DECBKM - private mode 67

    [Fact]
    public void TheBackarrowSendsBackspaceBeforeAnyHostSaysOtherwise()
    {
        // The power-on value, and a DELIBERATE deviation from a real VT220, which powers on sending
        // DEL. Every mapper in this emulator has always sent 0x08 and working setups depend on it;
        // flipping that while adding the mode would have arrived as "my backspace broke" rather
        // than as a change anybody chose. The MODE itself is faithful - see the two tests below.
        Assert.Equal("\x08", KeySends(VkBack, NewVt340().GetActiveModes()));
    }

    [Fact]
    public void ResettingModeSixtySevenMakesTheBackarrowSendDelete()
    {
        // The faithful half. A host that resets DECBKM is asking for DEL and gets DEL.
        Assert.Equal("\x7F", KeySends(VkBack, ModesAfter(67, false)));
    }

    [Fact]
    public void SettingModeSixtySevenMakesTheBackarrowSendBackspace()
    {
        Assert.Equal("\x08", KeySends(VkBack, ModesAfter(67, true)));
    }

    [Fact]
    public void TheModeSurvivesTheRoundTrip()
    {
        // Set, reset, set again on ONE terminal. A mode that only works from its power-on value is
        // half a mode, and this is the shape that catches a flag being assigned rather than toggled.
        var emulator = NewVt340();

        emulator.ProcessData(Encoding.ASCII.GetBytes(Escape + "[?67l"));
        Assert.Equal("\x7F", KeySends(VkBack, emulator.GetActiveModes()));

        emulator.ProcessData(Encoding.ASCII.GetBytes(Escape + "[?67h"));
        Assert.Equal("\x08", KeySends(VkBack, emulator.GetActiveModes()));

        emulator.ProcessData(Encoding.ASCII.GetBytes(Escape + "[?67l"));
        Assert.Equal("\x7F", KeySends(VkBack, emulator.GetActiveModes()));
    }

    [Fact]
    public void ControlBackarrowIsNotTouchedByTheMode()
    {
        // Ctrl+Backspace sends NUL and has its own mapping. DEC defines no modified form of this
        // key, and a user's own Ctrl binding must keep working - so DECBKM is deliberately checked
        // only for the UNMODIFIED key. Without that guard the new branch would swallow every
        // modified press as well.
        var mapper = new VT220KeyboardMapper();

        string? withMode = mapper.MapKey(VkBack, KeyModifiers.Ctrl, ModesAfter(67, false));
        string? withoutMode = mapper.MapKey(VkBack, KeyModifiers.Ctrl, ModesAfter(67, true));

        Assert.Equal(withoutMode, withMode);
    }

    #endregion

    [Fact]
    public void NeitherModeIsCountedAsUnhandledAnyMore()
    {
        // The thing the sweep test was built to notice. Until today both of these fell into a bare
        // "break" and were not even counted, so nothing anywhere could have told you they were
        // missing.
        var emulator = NewVt340();

        emulator.ProcessData(Encoding.ASCII.GetBytes(Escape + "[?66h"));
        emulator.ProcessData(Encoding.ASCII.GetBytes(Escape + "[?67h"));

        Assert.False(emulator.UnrecognisedSequences.ContainsKey("private mode 66 set"));
        Assert.False(emulator.UnrecognisedSequences.ContainsKey("private mode 67 set"));
    }
}
