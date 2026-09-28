using System;
using System.Text;
using RetroTerm.Core.Terminal.Emulators;
using RetroTerm.Core.Terminal.Input;
using Xunit;

namespace RetroTerm.Tests.Terminal;

/// <summary>
/// Phase 3 part 5: the numeric keypad under DECKPAM.
///
/// The mode arrived at the mapper as of the previous change, but the mapper's keypad branch was an
/// empty stub with the comment "this would need specific keypad key handling" — so DECKPAM was
/// carried faithfully and then ignored.
///
/// It sat in ApplyTerminalModeModifications, which only ever sees the FINISHED sequence a key
/// produced. Application keypad mode depends on WHICH key was pressed, not on what it produced, so
/// there was nothing that method could usefully do; the stub could not have been filled in where
/// it stood. The handling now happens in MapKey, ahead of the tables.
///
/// What the mode is for: it is how a host tells the keypad 4 from the main-keyboard 4. Both send
/// '4' normally; under DECKPAM the keypad one sends ESC O t.
/// </summary>
public class ApplicationKeypadTests
{
    private const int VkNumpad0 = 96;
    private const int VkMultiply = 106;
    private const int VkAdd = 107;
    private const int VkSeparator = 108;
    private const int VkSubtract = 109;
    private const int VkDecimal = 110;
    private const int VkDivide = 111;

    private static void Feed(TerminalEmulatorBase emulator, string s)
    {
        emulator.ProcessData(Encoding.ASCII.GetBytes(s));
    }

    /// <summary>
    /// An emulator with DECKPAM on, and the mapper that goes with it.
    /// </summary>
    private static (IKeyboardMapper Mapper, TerminalModes Modes) WithApplicationKeypad()
    {
        var emulator = new VT100Emulator(20, 5);
        Feed(emulator, new string(new[] { (char)0x1B, '=' }));   // DECKPAM
        return (KeyboardMapperFactory.CreateMapper("VT100"), emulator.GetActiveModes());
    }

    // ─────────────────────────────────────────────────────────────
    // The digits
    // ─────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(0, "\x1bOp")]
    [InlineData(1, "\x1bOq")]
    [InlineData(2, "\x1bOr")]
    [InlineData(3, "\x1bOs")]
    [InlineData(4, "\x1bOt")]
    [InlineData(5, "\x1bOu")]
    [InlineData(6, "\x1bOv")]
    [InlineData(7, "\x1bOw")]
    [InlineData(8, "\x1bOx")]
    [InlineData(9, "\x1bOy")]
    public void EachKeypadDigitHasItsOwnSequence(int digit, string expected)
    {
        var (mapper, modes) = WithApplicationKeypad();

        Assert.Equal(expected, mapper.MapKey(VkNumpad0 + digit, KeyModifiers.None, modes));
    }

    [Fact]
    public void TheDigitsAreDistinct()
    {
        // A transcription slip in a ten-entry table is easy and would be invisible in a
        // per-digit test that happened to be wrong the same way.
        var (mapper, modes) = WithApplicationKeypad();
        var seen = new System.Collections.Generic.HashSet<string>();

        for (int digit = 0; digit <= 9; digit++)
        {
            var sequence = mapper.MapKey(VkNumpad0 + digit, KeyModifiers.None, modes);
            Assert.NotNull(sequence);
            Assert.True(seen.Add(sequence!), $"keypad {digit} produced a duplicate sequence");
        }
    }

    // ─────────────────────────────────────────────────────────────
    // The operator keys
    // ─────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(VkMultiply, "\x1bOj")]
    [InlineData(VkAdd, "\x1bOk")]
    [InlineData(VkSeparator, "\x1bOl")]
    [InlineData(VkSubtract, "\x1bOm")]
    [InlineData(VkDecimal, "\x1bOn")]
    [InlineData(VkDivide, "\x1bOo")]
    public void TheOperatorKeysMapToo(int keyCode, string expected)
    {
        var (mapper, modes) = WithApplicationKeypad();

        Assert.Equal(expected, mapper.MapKey(keyCode, KeyModifiers.None, modes));
    }

    // ─────────────────────────────────────────────────────────────
    // Only when the mode is on
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void WithoutTheModeAKeypadKeyIsNotClaimedByTheMapper()
    {
        // In numeric mode the keypad is ordinary text and must keep travelling the text path.
        // A non-null answer here would mean the digit got delivered twice.
        var mapper = KeyboardMapperFactory.CreateMapper("VT100");

        Assert.Null(mapper.MapKey(VkNumpad0 + 4, KeyModifiers.None, TerminalModes.None));
    }

    [Fact]
    public void TurningTheModeOffAgainReleasesTheKeypad()
    {
        var emulator = new VT100Emulator(20, 5);
        var mapper = KeyboardMapperFactory.CreateMapper("VT100");

        Feed(emulator, new string(new[] { (char)0x1B, '=' }));   // DECKPAM
        Assert.Equal("\x1bOt", mapper.MapKey(VkNumpad0 + 4, KeyModifiers.None, emulator.GetActiveModes()));

        Feed(emulator, new string(new[] { (char)0x1B, '>' }));   // DECKPNM
        Assert.Null(mapper.MapKey(VkNumpad0 + 4, KeyModifiers.None, emulator.GetActiveModes()));
    }

    [Fact]
    public void ResetReleasesTheKeypad()
    {
        var emulator = new VT100Emulator(20, 5);
        var mapper = KeyboardMapperFactory.CreateMapper("VT100");

        Feed(emulator, new string(new[] { (char)0x1B, '=' }));
        Feed(emulator, new string(new[] { (char)0x1B, 'c' }));   // RIS

        Assert.Null(mapper.MapKey(VkNumpad0 + 4, KeyModifiers.None, emulator.GetActiveModes()));
    }

    // ─────────────────────────────────────────────────────────────
    // Not at the expense of anything else
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void ModifiedKeypadPressesAreLeftAlone()
    {
        // DEC defines no modified form for these keys, and a user may have bound Ctrl+keypad to
        // something. Claiming them here would take that binding away.
        var (mapper, modes) = WithApplicationKeypad();

        Assert.Null(mapper.MapKey(VkNumpad0 + 4, KeyModifiers.Ctrl, modes));
    }

    [Fact]
    public void TheMainKeyboardDigitsAreUnaffected()
    {
        // The whole point of the mode is telling the two 4s apart, so the main-keyboard one must
        // not change. VK 52 is the main '4'.
        var (mapper, modes) = WithApplicationKeypad();

        Assert.Null(mapper.MapKey(52, KeyModifiers.None, modes));
    }

    [Fact]
    public void ArrowKeysStillFollowCursorKeyMode_NotKeypadMode()
    {
        // The two application modes are independent; turning the keypad one on must not change
        // the arrows, and the arrows' own mode must still work while it is on.
        var emulator = new VT100Emulator(20, 5);
        var mapper = KeyboardMapperFactory.CreateMapper("VT100");
        const int vkUp = 38;

        Feed(emulator, new string(new[] { (char)0x1B, '=' }));   // DECKPAM only
        Assert.Equal("\x1b[A", mapper.MapKey(vkUp, KeyModifiers.None, emulator.GetActiveModes()));

        Feed(emulator, "\x1b[?1h");                              // now DECCKM as well
        Assert.Equal("\x1bOA", mapper.MapKey(vkUp, KeyModifiers.None, emulator.GetActiveModes()));
    }

    [Fact]
    public void KeypadAndCursorModesCoexist()
    {
        var emulator = new VT100Emulator(20, 5);
        var mapper = KeyboardMapperFactory.CreateMapper("VT100");

        Feed(emulator, new string(new[] { (char)0x1B, '=' }));
        Feed(emulator, "\x1b[?1h");
        var modes = emulator.GetActiveModes();

        Assert.Equal("\x1bOt", mapper.MapKey(VkNumpad0 + 4, KeyModifiers.None, modes));
        Assert.Equal("\x1bOA", mapper.MapKey(38, KeyModifiers.None, modes));
    }
}
