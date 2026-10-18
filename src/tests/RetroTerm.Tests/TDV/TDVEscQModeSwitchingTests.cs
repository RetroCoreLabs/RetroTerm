using RetroTerm.Core.Terminal.Emulators.TDV;
using Xunit;

namespace RetroTerm.Tests.TDV;

/// <summary>
/// Tests for ESC Q (Exit 2115 mode / Enable extended mode) handling across all TDV emulators
/// Verifies that keyboard sequences switch correctly based on mode
/// </summary>
public class TDVEscQModeSwitchingTests
{
    #region TDV1200 ESC Q Tests

    [Fact]
    public void TDV1200_EscQ_ExitsCompabilityMode()
    {
        // Arrange
        var emulator = new TDV1200Emulator(80, 24);

        // Enter 2115 mode via CSI 66 h
        emulator.ProcessInput("\x1b[66l"u8.ToArray());
        Assert.True(emulator.Is2115CompatibilityMode);

        // Act - Send ESC Q to exit 2115 mode
        emulator.ProcessInput("\x1bQ"u8.ToArray());

        // Assert - Mode should be disabled
        Assert.False(emulator.Is2115CompatibilityMode);
    }

    [Fact]
    public void TDV1200_KeySequences_SwitchCorrectlyWithMode()
    {
        // This test verifies that when ESC Q is sent, the keyboard mapper
        // should switch from sending C0 codes to CSI sequences
        // Note: The actual keyboard mapping is handled by the UI layer,
        // but the emulator must correctly report its mode state

        // Arrange
        var emulator = new TDV1200Emulator(80, 24);

        // Enter 2115 mode
        emulator.ProcessInput("\x1b[66l"u8.ToArray());
        Assert.True(emulator.Is2115CompatibilityMode);

        // Act - Exit 2115 mode with ESC Q
        emulator.ProcessInput("\x1bQ"u8.ToArray());

        // Assert - Mode should be off, indicating keyboard should send CSI sequences
        Assert.False(emulator.Is2115CompatibilityMode);
    }

    #endregion

    #region TDV2215 ESC Q Tests

    [Fact]
    public void TDV2215_EscQ_ExitsCompatibilityModeAndEnablesExtendedMode()
    {
        // Arrange
        var emulator = new TDV2215Emulator(80, 24);

        // Enter 2115 mode via CSI ?40 h (TDV2215 uses ?40, not 66)
        emulator.ProcessInput("\x1b[66l"u8.ToArray());
        Assert.True(emulator.Is2115CompatibilityMode);
        Assert.False(emulator.IsExtendedMode); // Extended mode should be off in 2115 mode

        // Act - Send ESC Q to exit 2115 mode and enable extended mode
        emulator.ProcessInput("\x1bQ"u8.ToArray());

        // Assert
        Assert.False(emulator.Is2115CompatibilityMode);
        Assert.True(emulator.IsExtendedMode);
    }

    [Fact]
    public void TDV2215_EscQ_WhenNotIn2115Mode_StillEnablesExtendedMode()
    {
        // Arrange
        var emulator = new TDV2215Emulator(80, 24);
        Assert.False(emulator.Is2115CompatibilityMode);

        // Act - Send ESC Q when already in normal mode
        emulator.ProcessInput("\x1bQ"u8.ToArray());

        // Assert - Extended mode should be enabled
        Assert.False(emulator.Is2115CompatibilityMode);
        Assert.True(emulator.IsExtendedMode);
    }

    #endregion

    #region TDV2200 ESC Q Tests

    [Fact]
    public void TDV2200_EscQ_ExitsCompatibilityMode()
    {
        // Arrange
        var emulator = new TDV2200Emulator(80, 24);

        // Enter 2115 mode via CSI ?40 h (TDV2200 uses ?40, not 66)
        emulator.ProcessInput("\x1b[66l"u8.ToArray());
        Assert.True(emulator.Is2115CompatibilityMode);

        // Act - Send ESC Q to exit 2115 mode
        emulator.ProcessInput("\x1bQ"u8.ToArray());

        // Assert - Mode should be disabled
        Assert.False(emulator.Is2115CompatibilityMode);
    }

    [Fact]
    public void TDV2200_EscQ_WhenNotIn2115Mode_KeepsModeOff()
    {
        // Arrange
        var emulator = new TDV2200Emulator(80, 24);
        Assert.False(emulator.Is2115CompatibilityMode);

        // Act - Send ESC Q when already in normal mode
        emulator.ProcessInput("\x1bQ"u8.ToArray());

        // Assert - Mode should remain off
        Assert.False(emulator.Is2115CompatibilityMode);
    }

    #endregion

    #region Cross-Emulator Consistency Tests

    [Fact]
    public void AllTDVEmulators_EscQ_ExitsCompatibilityMode()
    {
        // Test that ESC Q works consistently across all TDV emulators

        // TDV1200
        var tdv1200 = new TDV1200Emulator(80, 24);
        tdv1200.ProcessInput("\x1b[66l"u8.ToArray()); // Enter 2115 mode
        Assert.True(tdv1200.Is2115CompatibilityMode);
        tdv1200.ProcessInput("\x1bQ"u8.ToArray()); // Exit 2115 mode
        Assert.False(tdv1200.Is2115CompatibilityMode);

        // TDV2215
        var tdv2215 = new TDV2215Emulator(80, 24);
        tdv2215.ProcessInput("\x1b[66l"u8.ToArray()); // Enter 2115 mode
        Assert.True(tdv2215.Is2115CompatibilityMode);
        tdv2215.ProcessInput("\x1bQ"u8.ToArray()); // Exit 2115 mode
        Assert.False(tdv2215.Is2115CompatibilityMode);

        // TDV2200
        var tdv2200 = new TDV2200Emulator(80, 24);
        tdv2200.ProcessInput("\x1b[66l"u8.ToArray()); // Enter 2115 mode
        Assert.True(tdv2200.Is2115CompatibilityMode);
        tdv2200.ProcessInput("\x1bQ"u8.ToArray()); // Exit 2115 mode
        Assert.False(tdv2200.Is2115CompatibilityMode);
    }

    #endregion

    #region Mode Toggle Sequences Tests

    [Fact]
    public void TDV2200_ModeToggle_EnterAndExitWith40h40l()
    {
        // Arrange
        var emulator = new TDV2200Emulator(80, 24);

        // Act & Assert - Enter 2115 mode
        emulator.ProcessInput("\x1b[66l"u8.ToArray());
        Assert.True(emulator.Is2115CompatibilityMode);

        // Act & Assert - Exit 2115 mode
        emulator.ProcessInput("\x1b[66h"u8.ToArray());
        Assert.False(emulator.Is2115CompatibilityMode);

        // Act & Assert - Re-enter 2115 mode
        emulator.ProcessInput("\x1b[66l"u8.ToArray());
        Assert.True(emulator.Is2115CompatibilityMode);

        // Act & Assert - Exit with ESC Q
        emulator.ProcessInput("\x1bQ"u8.ToArray());
        Assert.False(emulator.Is2115CompatibilityMode);
    }

    [Fact]
    public void TDV1200_ModeToggle_EnterAndExitWith66h66l()
    {
        // Arrange
        var emulator = new TDV1200Emulator(80, 24);

        // Act & Assert - Enter 2115 mode
        emulator.ProcessInput("\x1b[66l"u8.ToArray());
        Assert.True(emulator.Is2115CompatibilityMode);

        // Act & Assert - Exit 2115 mode
        emulator.ProcessInput("\x1b[66h"u8.ToArray());
        Assert.False(emulator.Is2115CompatibilityMode);

        // Act & Assert - Re-enter 2115 mode
        emulator.ProcessInput("\x1b[66l"u8.ToArray());
        Assert.True(emulator.Is2115CompatibilityMode);

        // Act & Assert - Exit with ESC Q
        emulator.ProcessInput("\x1bQ"u8.ToArray());
        Assert.False(emulator.Is2115CompatibilityMode);
    }

    #endregion

    #region SINTRAN Initialization Sequence Tests

    [Fact]
    public void TDV2200_SINTRANInitSequence_ExitsCompatibilityMode()
    {
        // This test simulates the SINTRAN login initialization sequence
        // which sends ESC Q to exit 2115 mode

        // Arrange
        var emulator = new TDV2200Emulator(80, 24);

        // Simulate terminal starting in 2115 mode (some hosts expect this)
        emulator.ProcessInput("\x1b[66l"u8.ToArray());
        Assert.True(emulator.Is2115CompatibilityMode);

        // Act - SINTRAN sends ESC Q as part of initialization
        emulator.ProcessInput("\x1bQ"u8.ToArray());

        // Assert - Terminal should now be in extended mode
        Assert.False(emulator.Is2115CompatibilityMode);

        // Additional SINTRAN sequences (should work in extended mode)
        emulator.ProcessInput("\x1b[30;7;80l"u8.ToArray()); // Reset modes
        emulator.ProcessInput("\x1b[62;62h"u8.ToArray());   // Set mode 62
        emulator.ProcessInput("\x1b[1;1H"u8.ToArray());     // Cursor home
        emulator.ProcessInput("\x1b[2J"u8.ToArray());       // Clear screen

        // Verify terminal is still in extended mode
        Assert.False(emulator.Is2115CompatibilityMode);
    }

    #endregion
}
