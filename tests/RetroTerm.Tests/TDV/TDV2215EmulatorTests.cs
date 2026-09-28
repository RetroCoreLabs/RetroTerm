using System;
using System.Text;
using RetroTerm.Core.Terminal.Emulators.TDV;
using RetroTerm.Tests.TDV.TestHelpers;
using Xunit;

namespace RetroTerm.Tests.TDV;

/// <summary>
/// Unit tests for TDV2215 emulator
/// Tests extended mode, transparent mode, DCS sequences, and TDV2215-specific features
/// </summary>
public class TDV2215EmulatorTests
{
    private readonly TDV2215Emulator _emulator;

    public TDV2215EmulatorTests()
    {
        _emulator = new TDV2215Emulator(80, 24, 10000);
    }

    [Fact]
    public void Constructor_ShouldInitializeCorrectly()
    {
        // Arrange & Act
        var emulator = new TDV2215Emulator(80, 24, 10000);

        // Assert
        Assert.Equal(80, emulator.Width);
        Assert.Equal(24, emulator.Height);
        Assert.Equal(10000, emulator.MaxScrollback);
        // Extended operation is "not 2115-compatible", and a fresh emulator obeys CSI, so it is
        // already extended. TDV 2215 section 3.1 and section 8.7.1 mode 66, EC.
        Assert.True(emulator.IsExtendedMode);
        Assert.False(emulator.IsTransparentMode);
    }

    /// <summary>
    /// Extended operation follows mode 66, the Extended Control switch.
    /// </summary>
    /// <remarks>
    /// RM 66 turns EC off, which is 2115-compatible operation; SM 66 turns it on. TDV 2215 section
    /// 8.7.1 lists 66 as EC with RM = OFF and SM = ON, and section 3.1 says EC off "works like a
    /// TDV 2115 from the host computer's point of view".
    ///
    /// This test used to drive <c>CSI ? 1 h</c> and <c>CSI ? 1 l</c>, which are DECCKM.
    /// </remarks>
    [Fact]
    public void ExtendedModeFollowsModeSixtySix()
    {
        Assert.True(_emulator.IsExtendedMode);

        _emulator.ProcessInput(TestTDVEmulatorBase.StringToBytes("\x1B[66l"));
        Assert.False(_emulator.IsExtendedMode);
        Assert.True(_emulator.Is2115CompatibilityMode);

        // From inside 2115 operation the ESC of a control sequence is discarded, so ESC Q is the
        // way back - sections 3.1, 7.9.2 and the note under 8.7.1.
        _emulator.ProcessInput(TestTDVEmulatorBase.StringToBytes("\x1BQ"));
        Assert.True(_emulator.IsExtendedMode);
        Assert.False(_emulator.Is2115CompatibilityMode);
    }

    /// <summary>
    /// Transparent operation is a keyboard soft-switch, so no sequence turns it on.
    /// </summary>
    /// <remarks>
    /// Section 4.3.1, the Send-Receive Mode switch. Section 8.7.1 lists every host-settable mode
    /// and SRM is not one of them. The <c>CSI ? 2</c> this used to send is, on the ND-1200 mode
    /// table, among the numbers the terminal ignores.
    /// </remarks>
    [Fact]
    public void TransparentModeIsNotHostSettable()
    {
        _emulator.ProcessInput(TestTDVEmulatorBase.StringToBytes("\x1B[?2h"));
        Assert.False(_emulator.IsTransparentMode);

        _emulator.ProcessInput(TestTDVEmulatorBase.StringToBytes("\x1B[?2l"));
        Assert.False(_emulator.IsTransparentMode);
    }

    /// <summary>
    /// <c>CSI ... |</c> is NDRAR, Remove Attribute in Rectangle - and it really removes one.
    /// </summary>
    /// <remarks>
    /// This test used to send <c>CSI 1 |</c>, call it an "extended mode specific sequence" and
    /// assert nothing. The final byte 7C is NDRAR: ND Display Terminal 1200 section 2.8, and the
    /// TDV 2200 CSI table at <c>spec\TDV2200\Testing2200_9S\nd_csi_sequences.md</c>. The handler
    /// that used to eat it on this model is gone.
    /// </remarks>
    [Fact]
    public void Ndrar_RemovesTheAttributeFromTheRectangle()
    {
        _emulator.ProcessInput(TestTDVEmulatorBase.StringToBytes("\x1B[1m"));    // bold on
        _emulator.ProcessInput(TestTDVEmulatorBase.StringToBytes("BOLD"));
        _emulator.ProcessInput(TestTDVEmulatorBase.StringToBytes("\x1B[0m"));

        Assert.True((_emulator.Buffer.GetCell(0, 0).Attributes
            & RetroTerm.Core.Terminal.Buffer.CharacterAttributes.Bold) != 0);

        // NDRAR: remove bold (1) from rows 0-0, columns 0-3. 0-indexed, as TDV rectangles are.
        _emulator.ProcessInput(TestTDVEmulatorBase.StringToBytes("\x1B[1;0;0;0;3|"));

        for (int col = 0; col <= 3; col++)
        {
            Assert.True((_emulator.Buffer.GetCell(0, col).Attributes
                & RetroTerm.Core.Terminal.Buffer.CharacterAttributes.Bold) == 0,
                "NDRAR left bold on column " + col);
        }
    }

    /// <summary>
    /// <c>CSI ... }</c> is NDFC, Fill Character(s) in Rectangle.
    /// </summary>
    /// <remarks>
    /// The 2215 had no handler for 7D at all before 11 September 2026 - the final was on the wrong
    /// mnemonic across the whole program, and this model only had the invented one. Replaces a test
    /// that put the terminal into "transparent mode" with <c>CSI ? 2 h</c> and asserted nothing.
    /// </remarks>
    [Fact]
    public void Ndfc_FillsTheRectangleWithTheCharacter()
    {
        // Fill rows 0-1, columns 0-2 with 'X' (88).
        _emulator.ProcessInput(TestTDVEmulatorBase.StringToBytes("\x1B[88;0;0;1;2}"));

        for (int row = 0; row <= 1; row++)
        {
            for (int col = 0; col <= 2; col++)
            {
                Assert.Equal((uint)'X', _emulator.Buffer.GetCell(row, col).Codepoint);
            }
        }

        // And nothing outside it.
        Assert.NotEqual((uint)'X', _emulator.Buffer.GetCell(0, 3).Codepoint);
        Assert.NotEqual((uint)'X', _emulator.Buffer.GetCell(2, 0).Codepoint);
    }

    [Fact]
    public void HandleDCSSequence_ShouldProcessCorrectly()
    {
        // Arrange
        var sequence = "\x1BP1;2;3\x1B\\"; // DCS sequence

        // Act
        _emulator.ProcessInput(TestTDVEmulatorBase.StringToBytes(sequence));

        // Assert
        // TODO: Verify that the DCS sequence was processed correctly
        // This would involve checking the terminal state
    }

    /// <summary>
    /// <c>ESC ? 1 h</c> is not a sequence, and the terminal must not act on it.
    /// </summary>
    /// <remarks>
    /// This test used to feed exactly these bytes, call them a "three-character sequence" and
    /// assert nothing at all. No TDV manual has an ESC ? form: TDV 2215 section 8.7.1 and ND-1200
    /// section 5.64 between them list every mode sequence, and they are CSI with no marker, with
    /// '?' for the DEC-compatible family, or with '&gt;' for the ND private family.
    /// </remarks>
    [Fact]
    public void TheThreeCharacterEscQuestionForm_ChangesNothing()
    {
        var before = _emulator.GetTerminalType();

        _emulator.ProcessInput(TestTDVEmulatorBase.StringToBytes("\x1B?1h"));

        Assert.Equal(before, _emulator.GetTerminalType());
        Assert.True(_emulator.IsExtendedMode);
        Assert.False(_emulator.IsTransparentMode);
    }

    [Fact]
    public void ProgramFunctionKey_ShouldProgramCorrectly()
    {
        // Arrange
        var keyNumber = 1;
        var sequence = "Hello World";

        // Act
        _emulator.ProgramFunctionKey(keyNumber, sequence);

        // Assert
        // TODO: Verify that the function key was programmed correctly
        // This would involve checking the push keys state
    }

    [Fact]
    public void GetTerminalType_ShouldReturnCorrectType()
    {
        // Act
        var terminalType = _emulator.GetTerminalType();

        // Assert - plain "TDV2215" IS extended operation; the string names the departure.
        Assert.Equal("TDV2215", terminalType);
    }

    /// <summary>
    /// Leaving extended operation is what shows up in the type string.
    /// </summary>
    [Fact]
    public void GetTerminalType_In2115Operation_SaysSo()
    {
        _emulator.ProcessInput(TestTDVEmulatorBase.StringToBytes("\x1B[66l"));   // EC off

        Assert.Equal("TDV2215+2115", _emulator.GetTerminalType());
    }

    /// <summary>
    /// A sequence the terminal ignores changes the type string not at all.
    /// </summary>
    [Fact]
    public void GetTerminalType_IsUnchangedByTheIgnoredPrivateModeTwo()
    {
        _emulator.ProcessInput(TestTDVEmulatorBase.StringToBytes("\x1B[?2h"));

        Assert.Equal("TDV2215", _emulator.GetTerminalType());
    }

    /// <summary>
    /// DECCKM and the ignored mode 2 together still leave the model string alone.
    /// </summary>
    /// <remarks>
    /// Both numbers used to be claimed by this model as "extended" and "transparent". They are
    /// DEC&#39;s, so the only thing that may change here is the cursor-key mode, which is not part
    /// of the terminal&#39;s identity.
    /// </remarks>
    [Fact]
    public void GetTerminalType_IsUnchangedByDeccKmAndModeTwo()
    {
        _emulator.ProcessInput(TestTDVEmulatorBase.StringToBytes("\x1B[?1;2h"));

        Assert.Equal("TDV2215", _emulator.GetTerminalType());
    }

    [Fact]
    public void GetTerminalCapabilities_ShouldReturnCorrectCapabilities()
    {
        // Act
        var capabilities = _emulator.GetTerminalCapabilities();

        // Assert
        Assert.Contains("TDV2215", capabilities);
        Assert.Contains("NDGRAPHICS", capabilities);
        Assert.Contains("NDWORKAREA", capabilities);
        Assert.Contains("NDPROTECTED", capabilities);
        Assert.Contains("NDLEDS", capabilities);
        Assert.Contains("NDPUSHKEYS", capabilities);
        Assert.Contains("DCS", capabilities);
        Assert.Contains("THREECHAR", capabilities);
    }

    /// <summary>
    /// 2115-compatible operation shows in the capability string, extended does not.
    /// </summary>
    [Fact]
    public void GetTerminalCapabilities_In2115Operation_SaysSo()
    {
        _emulator.ProcessInput(TestTDVEmulatorBase.StringToBytes("\x1B[66l"));

        var capabilities = _emulator.GetTerminalCapabilities();

        Assert.Contains("TDV2215", capabilities);
        Assert.Contains("+2115", capabilities);
    }

    /// <summary>
    /// The ignored private mode 2 adds nothing to the capability string.
    /// </summary>
    [Fact]
    public void GetTerminalCapabilities_AreUnchangedByTheIgnoredPrivateModeTwo()
    {
        _emulator.ProcessInput(TestTDVEmulatorBase.StringToBytes("\x1B[?2h"));

        var capabilities = _emulator.GetTerminalCapabilities();

        Assert.Contains("TDV2215", capabilities);
        Assert.DoesNotContain("+TRANSPARENT", capabilities);
    }

    /// <summary>
    /// A reset puts the terminal back into extended operation with transparent off.
    /// </summary>
    /// <remarks>
    /// The 2115 switch is the state worth checking here: a terminal left in compatibility mode
    /// after a reset would discard the ESC of every sequence that followed.
    /// </remarks>
    [Fact]
    public void ResetToInitialState_ShouldResetAllState()
    {
        _emulator.ProcessInput(TestTDVEmulatorBase.StringToBytes("\x1B[66l"));
        Assert.True(_emulator.Is2115CompatibilityMode);

        _emulator.ResetToInitialState();

        Assert.True(_emulator.IsExtendedMode);
        Assert.False(_emulator.Is2115CompatibilityMode);
        Assert.False(_emulator.IsTransparentMode);
    }

    #region SS2/SS3 Single Shift Tests

    [Fact]
    public void SS2_SingleShift2_ShouldSetPendingSingleShift()
    {
        // Arrange - ESC N is SS2 (Single Shift 2)
        var sequence = "\x1BN";

        // Act
        _emulator.ProcessInput(TestTDVEmulatorBase.StringToBytes(sequence));

        // Assert
        Assert.True(_emulator.HasPendingSingleShift);
        Assert.Equal(2, _emulator.PendingSingleShift);
    }

    [Fact]
    public void SS3_SingleShift3_ShouldSetPendingSingleShift()
    {
        // Arrange - ESC O is SS3 (Single Shift 3)
        var sequence = "\x1BO";

        // Act
        _emulator.ProcessInput(TestTDVEmulatorBase.StringToBytes(sequence));

        // Assert
        Assert.True(_emulator.HasPendingSingleShift);
        Assert.Equal(3, _emulator.PendingSingleShift);
    }

    [Fact]
    public void SS2_ShouldApplyFontNumber2ToNextCharacter()
    {
        // Arrange - ESC N followed by a character
        var sequence = "\x1BNA"; // SS2 + 'A'

        // Act
        _emulator.ProcessInput(TestTDVEmulatorBase.StringToBytes(sequence));

        // Assert - FontNumber should be 2 for the character
        var cell = _emulator.Buffer.GetCell(0, 0);
        Assert.Equal('A', (char)cell.Codepoint);
        Assert.Equal(2, cell.FontNumber);

        // Single shift should be cleared after one character
        Assert.False(_emulator.HasPendingSingleShift);
    }

    [Fact]
    public void SS3_ShouldApplyFontNumber3ToNextCharacter()
    {
        // Arrange - ESC O followed by a character
        var sequence = "\x1BO0"; // SS3 + '0' (subscript/superscript digit)

        // Act
        _emulator.ProcessInput(TestTDVEmulatorBase.StringToBytes(sequence));

        // Assert - FontNumber should be 3 for the character
        var cell = _emulator.Buffer.GetCell(0, 0);
        Assert.Equal('0', (char)cell.Codepoint);
        Assert.Equal(3, cell.FontNumber);

        // Single shift should be cleared after one character
        Assert.False(_emulator.HasPendingSingleShift);
    }

    [Fact]
    public void SS2_ShouldOnlyAffectOneCharacter()
    {
        // Arrange - ESC N followed by two characters
        var sequence = "\x1BNAB"; // SS2 + 'A' + 'B'

        // Act
        _emulator.ProcessInput(TestTDVEmulatorBase.StringToBytes(sequence));

        // Assert - First char should have FontNumber 2, second should be 0
        var cell1 = _emulator.Buffer.GetCell(0, 0);
        var cell2 = _emulator.Buffer.GetCell(0, 1);
        Assert.Equal('A', (char)cell1.Codepoint);
        Assert.Equal(2, cell1.FontNumber);
        Assert.Equal('B', (char)cell2.Codepoint);
        Assert.Equal(0, cell2.FontNumber);
    }

    #endregion

    #region LS2/LS3 Locking Shift Tests

    [Fact]
    public void LS2_LockingShift2_ShouldLockToCharacterSet2()
    {
        // Arrange - ESC n is LS2 (Locking Shift 2)
        var sequence = "\x1Bn";

        // Act
        _emulator.ProcessInput(TestTDVEmulatorBase.StringToBytes(sequence));

        // Assert
        Assert.Equal(2, _emulator.LockedCharacterSet);
    }

    [Fact]
    public void LS3_LockingShift3_ShouldLockToCharacterSet3()
    {
        // Arrange - ESC o is LS3 (Locking Shift 3)
        var sequence = "\x1Bo";

        // Act
        _emulator.ProcessInput(TestTDVEmulatorBase.StringToBytes(sequence));

        // Assert
        Assert.Equal(3, _emulator.LockedCharacterSet);
    }

    [Fact]
    public void LS2_ShouldApplyFontNumber2ToAllSubsequentCharacters()
    {
        // Arrange - ESC n followed by characters
        var sequence = "\x1BnABC"; // LS2 + 'ABC'

        // Act
        _emulator.ProcessInput(TestTDVEmulatorBase.StringToBytes(sequence));

        // Assert - All characters should have FontNumber 2
        for (int i = 0; i < 3; i++)
        {
            var cell = _emulator.Buffer.GetCell(0, i);
            Assert.Equal(2, cell.FontNumber);
        }
    }

    [Fact]
    public void LS3_ShouldApplyFontNumber3ToAllSubsequentCharacters()
    {
        // Arrange - ESC o followed by characters
        var sequence = "\x1Bo123"; // LS3 + '123'

        // Act
        _emulator.ProcessInput(TestTDVEmulatorBase.StringToBytes(sequence));

        // Assert - All characters should have FontNumber 3
        for (int i = 0; i < 3; i++)
        {
            var cell = _emulator.Buffer.GetCell(0, i);
            Assert.Equal(3, cell.FontNumber);
        }
    }

    [Fact]
    public void SS2_DuringLS3_ShouldTemporarilyOverride()
    {
        // Arrange - LS3, then SS2 + char, then more chars
        var sequence = "\x1Bo1\x1BNA2"; // LS3 + '1' + SS2 + 'A' + '2'

        // Act
        _emulator.ProcessInput(TestTDVEmulatorBase.StringToBytes(sequence));

        // Assert
        var cell0 = _emulator.Buffer.GetCell(0, 0);
        var cell1 = _emulator.Buffer.GetCell(0, 1);
        var cell2 = _emulator.Buffer.GetCell(0, 2);

        Assert.Equal('1', (char)cell0.Codepoint);
        Assert.Equal(3, cell0.FontNumber); // LS3

        Assert.Equal('A', (char)cell1.Codepoint);
        Assert.Equal(2, cell1.FontNumber); // SS2 override

        Assert.Equal('2', (char)cell2.Codepoint);
        Assert.Equal(3, cell2.FontNumber); // Back to LS3
    }

    [Fact]
    public void ResetToInitialState_ShouldResetSingleShiftAndLockingShift()
    {
        // Arrange
        _emulator.ProcessInput(TestTDVEmulatorBase.StringToBytes("\x1Bo")); // LS3
        _emulator.ProcessInput(TestTDVEmulatorBase.StringToBytes("\x1BN")); // SS2

        // Act
        _emulator.ResetToInitialState();

        // Assert
        Assert.False(_emulator.HasPendingSingleShift);
        Assert.Equal(0, _emulator.LockedCharacterSet);
    }

    #endregion

    #region TDV2115 Compatibility Mode Tests

    [Fact]
    public void TDV2115Mode_ShouldEnableViaEscapeSequence()
    {
        // Arrange - ESC[?40h enables 2115 compatibility mode
        var sequence = "\x1b[66l";

        // Act
        _emulator.ProcessInput(TestTDVEmulatorBase.StringToBytes(sequence));

        // Assert
        Assert.True(_emulator.Is2115CompatibilityMode);
    }

    [Fact]
    public void TDV2115Mode_ShouldDisableViaEscapeSequence()
    {
        // Arrange
        _emulator.ProcessInput(TestTDVEmulatorBase.StringToBytes("\x1b[66l")); // Enable

        // Act
        _emulator.ProcessInput(TestTDVEmulatorBase.StringToBytes("\x1b[66h")); // Disable

        // Assert
        Assert.False(_emulator.Is2115CompatibilityMode);
    }

    [Fact]
    public void EscQ_ShouldExitCompatibilityModeAndEnableExtendedMode()
    {
        // Arrange - Enable 2115 mode first
        _emulator.ProcessInput(TestTDVEmulatorBase.StringToBytes("\x1b[66l"));
        Assert.True(_emulator.Is2115CompatibilityMode);

        // Act - ESC Q exits compatibility mode
        _emulator.ProcessInput(TestTDVEmulatorBase.StringToBytes("\x1BQ"));

        // Assert
        Assert.False(_emulator.Is2115CompatibilityMode);
        Assert.True(_emulator.IsExtendedMode);
    }

    #endregion
}
