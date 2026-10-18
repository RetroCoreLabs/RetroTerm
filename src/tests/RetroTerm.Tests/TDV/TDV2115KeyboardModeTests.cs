using System;
using System.Text;
using RetroTerm.Core.Terminal.Emulators.TDV;
using RetroTerm.Core.Terminal.Input;
using Xunit;

namespace RetroTerm.Tests.TDV;

/// <summary>
/// Tests for TDV 2115 compatibility mode - SENDING direction (keyboard -> host)
/// Verifies that keyboard mappers send correct sequences based on 2115 mode
/// </summary>
[Collection("TDVKeyBinding")]
public class TDV2115KeyboardModeTests
{
    // ========================================================================
    // TDV1200 SENDING TESTS
    // ========================================================================

    [Theory]
    [InlineData(38, "\x1c")]   // Up in normal mode = C0 FS
    [InlineData(40, "\x0b")]   // Down in normal mode = C0 VT
    [InlineData(39, "\x18")]   // Right in normal mode = C0 CAN
    [InlineData(37, "\x08")]   // Left in normal mode = C0 BS
    [InlineData(36, "\x1d")]   // Home in normal mode = C0 GS
    public void TDV1200_NormalMode_SendsC0Codes(int keyCode, string expected)
    {
        // Arrange
        var mapper = new TDV1200KeyboardMapper();

        // Act
        var result = mapper.MapKey(keyCode, KeyModifiers.None, TerminalModes.None);

        // Assert
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(38, "\x1c")]  // Up in 2115 mode = FS (0x1C)
    [InlineData(40, "\x0b")]  // Down in 2115 mode = VT (0x0B)
    [InlineData(39, "\x18")]  // Right in 2115 mode = CAN (0x18)
    [InlineData(37, "\x08")]  // Left in 2115 mode = BS (0x08)
    [InlineData(36, "\x1d")]  // Home in 2115 mode = GS (0x1D) - same in both modes, see keyboard-spec.md §6.8.3
    public void TDV1200_2115Mode_SendsC0ControlCodes(int keyCode, string expected)
    {
        // Arrange
        var mapper = new TDV1200KeyboardMapper();

        // Act
        var result = mapper.MapKey(keyCode, KeyModifiers.None, TerminalModes.TDV2115Mode);

        // Assert
        Assert.Equal(expected, result);
    }

    // ========================================================================
    // TDV2215 SENDING TESTS
    // ========================================================================

    [Theory]
    [InlineData(38, "\x1c")]   // Up = C0 FS
    [InlineData(40, "\x0b")]   // Down = C0 VT
    [InlineData(39, "\x18")]   // Right = C0 CAN
    [InlineData(37, "\x08")]   // Left = C0 BS
    [InlineData(36, "\x1d")]   // Home = C0 GS
    public void TDV2215_NormalMode_SendsC0Codes(int keyCode, string expected)
    {
        // Arrange
        var mapper = new TDV2215KeyboardMapper();

        // Act
        var result = mapper.MapKey(keyCode, KeyModifiers.None, TerminalModes.None);

        // Assert
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(38, "\x1c")]  // Up in 2115 mode = FS
    [InlineData(40, "\x0b")]  // Down in 2115 mode = VT
    [InlineData(39, "\x18")]  // Right in 2115 mode = CAN
    [InlineData(37, "\x08")]  // Left in 2115 mode = BS
    [InlineData(36, "\x1d")]  // Home in 2115 mode = GS (0x1D) - same in both modes
    public void TDV2215_2115Mode_SendsC0ControlCodes(int keyCode, string expected)
    {
        // Arrange
        var mapper = new TDV2215KeyboardMapper();

        // Act
        var result = mapper.MapKey(keyCode, KeyModifiers.None, TerminalModes.TDV2115Mode);

        // Assert
        Assert.Equal(expected, result);
    }

    // ========================================================================
    // TDV2200 SENDING TESTS
    // ========================================================================

    [Theory]
    [InlineData(38, "\x1c")]   // Up = C0 FS
    [InlineData(40, "\x0b")]   // Down = C0 VT
    [InlineData(39, "\x18")]   // Right = C0 CAN
    [InlineData(37, "\x08")]   // Left = C0 BS
    [InlineData(36, "\x1d")]   // Home = C0 GS
    public void TDV2200_NormalMode_SendsC0Codes(int keyCode, string expected)
    {
        // Arrange
        var mapper = new TDV2200KeyboardMapper();

        // Act
        var result = mapper.MapKey(keyCode, KeyModifiers.None, TerminalModes.None);

        // Assert
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(38, "\x1c")]  // Up in 2115 mode = FS
    [InlineData(40, "\x0b")]  // Down in 2115 mode = VT
    [InlineData(39, "\x18")]  // Right in 2115 mode = CAN
    [InlineData(37, "\x08")]  // Left in 2115 mode = BS
    [InlineData(36, "\x1d")]  // Home in 2115 mode = GS (0x1D) - same in both modes
    public void TDV2200_2115Mode_SendsC0ControlCodes(int keyCode, string expected)
    {
        // Arrange
        var mapper = new TDV2200KeyboardMapper();

        // Act
        var result = mapper.MapKey(keyCode, KeyModifiers.None, TerminalModes.TDV2115Mode);

        // Assert
        Assert.Equal(expected, result);
    }

    // ========================================================================
    // MODIFIER TESTS
    // ========================================================================

    [Fact]
    public void TDV2200_2115Mode_WithShiftModifier_StillSendsC0()
    {
        // Arrange - Shift+Right: AlwaysSameCode means same C0 regardless of modifier
        var mapper = new TDV2200KeyboardMapper();

        // Act
        var result = mapper.MapKey(39, KeyModifiers.Shift, TerminalModes.TDV2115Mode);

        // Assert - AlwaysSameCode: RIGHT always sends 0x18 (CAN)
        Assert.Equal("\x18", result);
    }

    /// <summary>
    /// A function key sends its C0 code in 2115 mode, not its extended sequence.
    /// </summary>
    /// <remarks>
    /// This test used to be called <c>FunctionKeys_NotAffectedBy2115Mode</c> and asserted that F1
    /// sent <c>ESC[50_</c> in BOTH modes. It cited nothing; it simply described what the mapper
    /// did, which was to look 2115 mode up in a hardcoded list of five keys and let everything
    /// else fall through to the extended sequence.
    /// <para>
    /// <c>spec\Keyboards\keyboard-spec.md</c> §6.8.3, citing the TDV-2200/9 User's Guide section
    /// 7.2, gives a table of some thirty keys that DO change: F51 F1 sends RS <c>0x1E</c>, F52 F2
    /// sends US <c>0x1F</c>, F53 F3 sends CAN <c>0x18</c>. Corrected 2 September 2026 after a walk
    /// of the whole registry found twelve such keys disagreeing.
    /// </para>
    /// </remarks>
    [Fact]
    public void FunctionKeysSendTheirC0CodeIn2115Mode()
    {
        var mapper = new TDV2200KeyboardMapper();

        // F1 is VK 112, grid F51.
        var normalMode = mapper.MapKey(112, KeyModifiers.None, TerminalModes.None);
        var mode2115 = mapper.MapKey(112, KeyModifiers.None, TerminalModes.TDV2115Mode);

        Assert.Equal("\x1b[50_", normalMode);   // Extended Control Mode ON: the TDV-native sequence
        Assert.Equal("\x1E", mode2115);            // Extended Control Mode OFF: RS, per §6.8.3
    }

    [Fact]
    public void AltKeys_NotAffectedBy2115Mode()
    {
        // Arrange - ensure default key binding configuration
        TDVKeyBindingConfiguration.ResetForTesting();
        var mapper = new TDV2200KeyboardMapper();

        // Act - Alt+H (HELP) in both modes
        var normalMode = mapper.MapKey(72, KeyModifiers.Alt, TerminalModes.None);
        var mode2115 = mapper.MapKey(72, KeyModifiers.Alt, TerminalModes.TDV2115Mode);

        // Assert - Alt keys should send same TDV-native sequence in both modes
        Assert.Equal("\x1b[46_", normalMode);  // HJELP key (G53)
        Assert.Equal("\x1b[46_", mode2115);    // HJELP key (G53)
    }

    // ========================================================================
    // PROCESSING TESTS (host -> terminal)
    // ========================================================================

    [Fact]
    public void TDV2200_2115Mode_ProcessesC0CodesForCursorMovement()
    {
        // Arrange
        var emulator = new TDV2200Emulator(80, 24);
        // Enable 2115 mode via CSI 40 h
        emulator.ProcessInput("\x1b[66l"u8.ToArray());
        emulator.Cursor.Row = 10;
        emulator.Cursor.Column = 10;

        // Act - Send 0x18 (CAN - Cursor Right in 2115 mode)
        emulator.ProcessInput(new byte[] { 0x18 });

        // Assert
        Assert.Equal(10, emulator.Cursor.Row);    // Row unchanged
        Assert.Equal(11, emulator.Cursor.Column); // Column moved right
        Assert.True(emulator.Is2115CompatibilityMode); // Mode is active
    }

    [Fact]
    public void TDV2200_NormalMode_MovesTheCursorOnC0CodesToo()
    {
        // This test used to assert the opposite - that CAN must NOT move the cursor outside 2115
        // mode - and carried no source for it. Two documents say otherwise and agree with each
        // other: the TDV 2215 manual (spec\TDV2115\TDV2115.md section 8.4) lists the C0 set and
        // marks only HT and ESC as affected by the extended-control switch, and the same
        // section (spec\TDV2215\TDV2215.md section 8.4, lines 2380-2396) gives BS, VT, CAN, FS and
        // GS as the cursor movements - a TDV moves its cursor by C0 code rather than by escape
        // sequence. The keyboard is the third witness: a TDV2200's arrow keys send these
        // very bytes in every mode, so a host echoing them back has to move the cursor.
        var emulator = new TDV2200Emulator(80, 24);
        emulator.Cursor.Row = 10;
        emulator.Cursor.Column = 10;

        emulator.ProcessInput(new byte[] { 0x18 });   // CAN - cursor right

        Assert.Equal(10, emulator.Cursor.Row);
        Assert.Equal(11, emulator.Cursor.Column);
        Assert.False(emulator.Is2115CompatibilityMode);
    }

    [Fact]
    public void TDV2200_NormalMode_LeavesTheGenuinelyTwoOneOneFiveOnlyCodesAlone()
    {
        // EOT erases the current line in 2115 mode. It appears in the 2215 manual's C0 table but
        // NOT in the All Models table, so it is one source short of agreement and stays off in
        // native mode rather than being enabled on a guess.
        var emulator = new TDV2200Emulator(80, 24);
        emulator.ProcessData(System.Text.Encoding.ASCII.GetBytes("HELLO"));

        emulator.ProcessInput(new byte[] { 0x04 });   // EOT - erase line, 2115 only

        emulator.GetBuffer().TryGetCell(0, 0, out var cell);
        Assert.Equal('H', (char)cell.Codepoint);
    }

    [Fact]
    public void TDV2200_GraphicsStillWinsForGroupSeparator()
    {
        // GS is cursor home in the TDV C0 table AND graph mode on a Tektronix. Nothing had to be
        // special-cased: the graphics side gets first refusal on every byte and takes GS
        // unconditionally, so a 2200 never treats it as a cursor command. A 1200 or a 2215, which
        // have no graphics, does.
        var emulator = new TDV2200Emulator(80, 24);
        emulator.Cursor.Row = 10;
        emulator.Cursor.Column = 10;

        emulator.ProcessInput(new byte[] { 0x1D });   // GS

        Assert.Equal(RetroTerm.Core.Terminal.Graphics.TektronixMode.Graph,
            emulator.GraphicsModule!.Vectors.Mode);
        Assert.Equal(10, emulator.Cursor.Row);
        Assert.Equal(10, emulator.Cursor.Column);
    }

    [Fact]
    public void TDV1200_NormalMode_HomesTheCursorOnGroupSeparator()
    {
        // The same byte on a terminal with no graphics to claim it first.
        var emulator = new TDV1200Emulator(80, 24);
        emulator.Cursor.Row = 10;
        emulator.Cursor.Column = 10;

        emulator.ProcessInput(new byte[] { 0x1D });   // GS - cursor home

        Assert.Equal(0, emulator.Cursor.Row);
        Assert.Equal(0, emulator.Cursor.Column);
    }

    [Fact]
    public void TDV2200_NormalMode_ProcessesEscapeSequences()
    {
        // Arrange
        var emulator = new TDV2200Emulator(80, 24);
        emulator.Cursor.Row = 10;
        emulator.Cursor.Column = 10;

        // Act - Send ESC[C (Cursor Right) in normal mode
        emulator.ProcessInput(Encoding.ASCII.GetBytes("\x1b[C"));

        // Assert - Cursor should move right
        Assert.Equal(10, emulator.Cursor.Row);
        Assert.Equal(11, emulator.Cursor.Column);
    }

    [Fact]
    public void RoundTrip_2115Mode_KeyboardToEmulator()
    {
        // Arrange - Set up both keyboard mapper and emulator in 2115 mode
        var mapper = new TDV2200KeyboardMapper();
        var emulator = new TDV2200Emulator(80, 24);
        emulator.ProcessInput("\x1b[66l"u8.ToArray()); // Enable 2115 mode
        emulator.Cursor.Row = 10;
        emulator.Cursor.Column = 10;

        // Act - Simulate user pressing Right Arrow key
        var keySequence = mapper.MapKey(39, KeyModifiers.None, TerminalModes.TDV2115Mode);
        emulator.ProcessInput(Encoding.ASCII.GetBytes(keySequence!));

        // Assert - Cursor should move right
        Assert.Equal("\x18", keySequence); // Verify C0 code was sent
        Assert.Equal(11, emulator.Cursor.Column); // Verify cursor moved
    }

    [Fact]
    public void RoundTrip_NormalMode_KeyboardToEmulator()
    {
        // In normal TDV mode, the keyboard sends C0 code 0x18 (CAN) for Right Arrow.
        // This goes to the HOST, which may echo back ESC[C to move the cursor.
        // The emulator does NOT interpret C0 codes as cursor movement in normal mode.
        var mapper = new TDV2200KeyboardMapper();
        var emulator = new TDV2200Emulator(80, 24);
        emulator.Cursor.Row = 10;
        emulator.Cursor.Column = 10;

        // Act - Keyboard sends C0 code to host
        var keySequence = mapper.MapKey(39, KeyModifiers.None, TerminalModes.None);
        Assert.Equal("\x18", keySequence); // C0 CAN code

        // Host echoes back ESC[C → emulator processes it
        emulator.ProcessInput(Encoding.ASCII.GetBytes("\x1b[C"));
        Assert.Equal(11, emulator.Cursor.Column); // Cursor moved via host echo
    }
}
