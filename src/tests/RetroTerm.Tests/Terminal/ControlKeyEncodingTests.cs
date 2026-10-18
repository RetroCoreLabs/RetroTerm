using System;
using RetroTerm.Core.Terminal.Input;
using Xunit;

namespace RetroTerm.Tests.Terminal;

/// <summary>
/// Phase 3 part 6: Ctrl+letter → C0 decided once, in Core (problem B.10).
///
/// The rule lived in the UI, in two copies — TerminalCanvas and VirtualKeyboardWindow — each
/// written as "a fallback if the mapper returned null". Two copies of the same rule is how the
/// same physical keypress ends up encoded differently depending on which window has focus, and it
/// is terminal behaviour rather than view behaviour: what Ctrl+A means is a property of the
/// terminal, not of the control that happens to be receiving keystrokes.
///
/// Both copies are gone. The mapper answers, so every consumer gets the same answer.
/// </summary>
public class ControlKeyEncodingTests
{
    private static IKeyboardMapper Vt100() => KeyboardMapperFactory.CreateMapper("VT100");

    // VK codes for letters are the ASCII uppercase values.
    private const int VkA = 65;
    private const int VkZ = 90;

    [Theory]
    [InlineData(VkA, '\x01')]     // Ctrl+A = SOH
    [InlineData(67, '\x03')]      // Ctrl+C = ETX
    [InlineData(68, '\x04')]      // Ctrl+D = EOT
    [InlineData(71, '\x07')]      // Ctrl+G = BEL
    [InlineData(73, '\x09')]      // Ctrl+I = HT
    [InlineData(77, '\x0D')]      // Ctrl+M = CR
    [InlineData(91 - 1, '\x1A')]  // Ctrl+Z = SUB
    public void CtrlLetterProducesItsControlCode(int keyCode, char expected)
    {
        var mapper = Vt100();

        Assert.Equal(expected.ToString(), mapper.MapKey(keyCode, KeyModifiers.Ctrl, TerminalModes.None));
    }

    [Fact]
    public void EveryLetterFromAToZIsCovered()
    {
        // The whole range in one sweep, rather than trusting the handful sampled above.
        var mapper = Vt100();

        for (int keyCode = VkA; keyCode <= VkZ; keyCode++)
        {
            var expected = ((char)(keyCode - 64)).ToString();
            Assert.Equal(expected, mapper.MapKey(keyCode, KeyModifiers.Ctrl, TerminalModes.None));
        }
    }

    [Fact]
    public void TheResultIsAlwaysASingleByteInTheC0Range()
    {
        var mapper = Vt100();

        for (int keyCode = VkA; keyCode <= VkZ; keyCode++)
        {
            var sequence = mapper.MapKey(keyCode, KeyModifiers.Ctrl, TerminalModes.None);

            Assert.NotNull(sequence);
            Assert.Single(sequence!);
            Assert.InRange(sequence![0], (char)0x01, (char)0x1A);
        }
    }

    [Fact]
    public void CtrlSpaceStillGivesNul()
    {
        // The sibling rule, already in Core, kept beside this one so the pair stay together.
        var mapper = Vt100();

        Assert.Equal("\x00", mapper.MapKey(32, KeyModifiers.Ctrl, TerminalModes.None));
    }

    [Fact]
    public void CtrlShiftLetterIsNotClaimed()
    {
        // A different combination, and one a user may have bound. The old UI fallbacks tested for
        // Control EXACTLY, and that behaviour is preserved deliberately rather than by accident.
        var mapper = Vt100();

        Assert.Null(mapper.MapKey(VkA, KeyModifiers.Ctrl | KeyModifiers.Shift, TerminalModes.None));
    }

    [Fact]
    public void AltLetterIsNotClaimedByTheControlRule()
    {
        var mapper = Vt100();

        var result = mapper.MapKey(VkA, KeyModifiers.Alt, TerminalModes.None);

        // Whatever Alt+A maps to, it must not be the Ctrl+A control code.
        Assert.NotEqual("\x01", result);
    }

    [Fact]
    public void ABareLetterIsNotClaimed()
    {
        // Ordinary typing must keep travelling the text path; claiming it here would double it up.
        var mapper = Vt100();

        Assert.Null(mapper.MapKey(VkA, KeyModifiers.None, TerminalModes.None));
    }

    [Fact]
    public void ATableEntryStillWinsOverTheControlRule()
    {
        // The rule runs after the modifier table, so a terminal that defines its own meaning for
        // a Ctrl combination keeps it. Ctrl+Up is an xterm-style modified arrow, not a C0 code.
        var mapper = Vt100();
        const int vkUp = 38;

        var result = mapper.MapKey(vkUp, KeyModifiers.Ctrl, TerminalModes.None);

        Assert.NotNull(result);
        Assert.StartsWith("\x1b[", result);
    }

    [Fact]
    public void TheControlRuleDoesNotDisturbApplicationCursorKeys()
    {
        // Guard against the new rule being reached before the mode handling.
        var mapper = Vt100();

        Assert.Equal("\x1bOA", mapper.MapKey(38, KeyModifiers.None, TerminalModes.ApplicationCursorKeys));
    }

    [Fact]
    public void CtrlBackspaceGivesNul()
    {
        // VK_BACK = 8. Without this the mapper would send BS, which is why the UI used to
        // intercept it ahead of the mapper in both windows.
        var mapper = Vt100();

        Assert.Equal("\x00", mapper.MapKey(8, KeyModifiers.Ctrl, TerminalModes.None));
    }

    [Fact]
    public void BareBackspaceIsStillBackspace()
    {
        // This mapper sends BS (0x08), not DEL (0x7F). Both are in use in the wild — a real VT100
        // sends DEL, xterm is configurable — so this records the choice this codebase already
        // made rather than asserting a preference. The point of the test is only that adding the
        // Ctrl+Backspace rule did not disturb the unmodified key.
        var mapper = Vt100();

        Assert.Equal("\x08", mapper.MapKey(8, KeyModifiers.None, TerminalModes.None));
    }

    // ─────────────────────────────────────────────────────────────
    // The TDV mapper answers these too
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void TheTdvMapperAlsoGivesTheControlCodes()
    {
        // TDVKeyboardMapper overrides MapKey completely, so deleting the UI fallback without
        // giving TDV the same rule would have stopped Ctrl+C reaching a TDV host entirely.
        //
        // This is not the "no VT220 fallback in TDV mode" rule being bent — a C0 code is not a
        // VT220 escape sequence, it is what CTRL on a TDV keyboard has always produced.
        var mapper = KeyboardMapperFactory.CreateMapper("TDV2200");

        Assert.Equal("\x03", mapper.MapKey(67, KeyModifiers.Ctrl, TerminalModes.None));   // Ctrl+C
        Assert.Equal("\x00", mapper.MapKey(32, KeyModifiers.Ctrl, TerminalModes.None));   // Ctrl+Space
        Assert.Equal("\x00", mapper.MapKey(8, KeyModifiers.Ctrl, TerminalModes.None));    // Ctrl+Backspace
    }

    [Fact]
    public void BothMapperFamiliesAgreeOnEveryControlLetter()
    {
        // The property that the whole change is for: one rule, so two mappers cannot disagree.
        var vt = KeyboardMapperFactory.CreateMapper("VT100");
        var tdv = KeyboardMapperFactory.CreateMapper("TDV2200");

        for (int keyCode = VkA; keyCode <= VkZ; keyCode++)
        {
            Assert.Equal(
                vt.MapKey(keyCode, KeyModifiers.Ctrl, TerminalModes.None),
                tdv.MapKey(keyCode, KeyModifiers.Ctrl, TerminalModes.None));
        }
    }
}
