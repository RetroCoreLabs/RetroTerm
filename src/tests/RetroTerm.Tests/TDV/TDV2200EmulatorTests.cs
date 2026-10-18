using System;
using System.Text;
using RetroTerm.Core.Terminal.Emulators.TDV;
using RetroTerm.Tests.TDV.TestHelpers;
using Xunit;

namespace RetroTerm.Tests.TDV;

/// <summary>
/// Unit tests for TDV2200 emulator
/// Tests graphics extension, Tektronix 4010 compatibility, ISO 646 variants, and TDV2200-specific features
/// </summary>
public class TDV2200EmulatorTests
{
    private readonly TDV2200Emulator _emulator;

    public TDV2200EmulatorTests()
    {
        _emulator = new TDV2200Emulator(80, 24, 10000);
    }

    [Fact]
    public void Constructor_ShouldInitializeCorrectly()
    {
        // Arrange & Act
        var emulator = new TDV2200Emulator(80, 24, 10000);

        // Assert
        Assert.Equal(80, emulator.Width);
        Assert.Equal(24, emulator.Height);
        Assert.Equal(10000, emulator.MaxScrollback);

        // A TDV2200 draws Tektronix vectors from the moment it is switched on. It used to report
        // false here until a sequence with no source anywhere was sent to it.
        Assert.True(emulator.DrawsTektronixVectors);
        Assert.Equal(TDV2200ISO646Variant.International, emulator.CurrentISO646Variant);
    }

    /// <summary>
    /// CSI n greater-than has no source, so it is counted rather than obeyed.
    /// </summary>
    /// <remarks>
    /// <para><b>What these two tests used to assert</b></para>
    /// That <c>CSI 1 greater-than</c> enabled a graphics extension and <c>CSI 2 greater-than</c>
    /// entered Tektronix mode. Both were written from the code rather than from a manual, and on
    /// 25 August 2026 the ND graphic terminal analysis, the TDV1200 graphics library reference and
    /// the comprehensive TDV reference were all searched: none of them names this sequence.
    /// <para><b>Why counting is the honest answer</b></para>
    /// A real ND host that sends it now appears in the unrecognised counter with its parameter, so
    /// the meaning can be read off a machine. Obeying a guess is what let the capability strings
    /// claim there was no Tektronix while the terminal was drawing vectors perfectly well.
    /// </remarks>
    [Fact]
    public void TheUnsourcedModeControlSequenceIsCountedRatherThanObeyed()
    {
        _emulator.ProcessInput(TestTDVEmulatorBase.StringToBytes("\x1B[1>"));
        _emulator.ProcessInput(TestTDVEmulatorBase.StringToBytes("\x1B[2>"));

        Assert.True(_emulator.UnrecognisedSequences.ContainsKey("CSI 1 >"),
            "CSI 1 > was obeyed or silently dropped instead of being recorded for a real host to "
            + "explain");
        Assert.True(_emulator.UnrecognisedSequences.ContainsKey("CSI 2 >"),
            "CSI 2 > was obeyed or silently dropped instead of being recorded for a real host to "
            + "explain");
    }

    [Fact]
    public void TektronixDrawingDoesNotWaitToBeSwitchedOn()
    {
        // The point the old tests missed entirely. Nothing gates the vector decoder: GS is what
        // puts a 4014 into graph mode, and this terminal offers every Ground-state byte to the
        // decoder whether or not any sequence asked it to.
        Assert.True(_emulator.DrawsTektronixVectors);
    }

    [Fact]
    public void HandleTektronixSequence_ShouldProcessCorrectly()
    {
        // Arrange
        _emulator.ProcessInput(TestTDVEmulatorBase.StringToBytes("\x1B[2>")); // Enter Tektronix mode
        var sequence = "\x1B[1A"; // Cursor up in Tektronix mode

        // Act
        _emulator.ProcessInput(TestTDVEmulatorBase.StringToBytes(sequence));

        // Assert
        // TODO: Verify that the Tektronix sequence was processed correctly
        // This would involve checking the cursor position
    }

    [Fact]
    public void HandleTDV2200Sequence_ShouldProcessCorrectly()
    {
        // Arrange
        var sequence = "\x1B[1;1;1;1z"; // NDSAR - Set Attribute in Rectangle

        // Act
        _emulator.ProcessInput(TestTDVEmulatorBase.StringToBytes(sequence));

        // Assert
        // TODO: Verify that the TDV2200 sequence was processed correctly
        // This would involve checking the terminal buffer state
    }

    [Fact]
    public void HandleISO646VariantSelection_ShouldSetCorrectly()
    {
        // Arrange
        var sequence = "\x1B%N"; // Set Norwegian variant

        // Act
        _emulator.ProcessInput(TestTDVEmulatorBase.StringToBytes(sequence));

        // Assert
        Assert.Equal(TDV2200ISO646Variant.Norwegian, _emulator.CurrentISO646Variant);

        // Act - Set Swedish variant
        _emulator.ProcessInput(TestTDVEmulatorBase.StringToBytes("\x1B%S")); // Set Swedish variant

        // Assert
        Assert.Equal(TDV2200ISO646Variant.Swedish, _emulator.CurrentISO646Variant);
    }

    [Fact]
    public void CharacterSetVariant_ShouldSetCorrectly()
    {
        // Arrange
        var variant = TDV2200ISO646Variant.German;

        // Act
        _emulator.CharacterSetVariant = (int)variant;

        // Assert
        Assert.Equal(variant, _emulator.CurrentISO646Variant);
    }

    [Fact]
    public void GetAvailableCharacterSetVariants_ShouldReturnAllVariants()
    {
        // Act
        var variants = _emulator.GetAvailableCharacterSetVariants();

        // Assert - TDV2115 spec 9.1 defines 4 variants
        Assert.Equal(4, variants.Length);
        Assert.Equal((int)TDV2200ISO646Variant.International, variants[0].Value);
        Assert.Equal((int)TDV2200ISO646Variant.Norwegian, variants[1].Value);
        Assert.Equal((int)TDV2200ISO646Variant.Swedish, variants[2].Value);
        Assert.Equal((int)TDV2200ISO646Variant.German, variants[3].Value);
    }

    [Fact]
    public void HandleGraphicsExtensionOperation_ShouldProcessCorrectly()
    {
        // Arrange
        _emulator.ProcessInput(TestTDVEmulatorBase.StringToBytes("\x1B[1>")); // Enable graphics extension
        var operation = 1;
        var parameters = new int[] { 10, 20, 30, 40 };

        // Act
        _emulator.HandleGraphicsExtensionOperation(operation, parameters);

        // Assert
        // TODO: Verify that the graphics extension operation was processed correctly
        // This would involve checking the terminal state
    }

    [Fact]
    public void GetTerminalType_ShouldReturnCorrectType()
    {
        // Act
        var terminalType = _emulator.GetTerminalType();

        // Assert
        Assert.Equal("TDV2200+GRAPHICS+TEKTRONIX+ISO646_International", terminalType);
    }

    /// <summary>
    /// The reported type does not depend on anything having been switched on.
    /// </summary>
    /// <remarks>
    /// Three tests used to live here, one per combination of two flags, and all three were really
    /// asserting that an unsourced sequence toggled a string. What matters is the opposite: the
    /// abilities are permanent, so the string is too. A terminal that reports them only after being
    /// asked would tell a host it cannot draw when it can.
    /// </remarks>
    [Fact]
    public void TheReportedTypeIsTheSameBeforeAndAfterAnyModeSequence()
    {
        string before = _emulator.GetTerminalType();

        _emulator.ProcessInput(TestTDVEmulatorBase.StringToBytes("\x1B[1>"));
        _emulator.ProcessInput(TestTDVEmulatorBase.StringToBytes("\x1B[2>"));

        Assert.Equal(before, _emulator.GetTerminalType());
        Assert.Contains("+TEKTRONIX", before);
        Assert.Contains("+GRAPHICS", before);
    }

    [Fact]
    public void GetTerminalCapabilities_ShouldReturnCorrectCapabilities()
    {
        // Act
        var capabilities = _emulator.GetTerminalCapabilities();

        // Assert
        Assert.Contains("TDV2200", capabilities);
        Assert.Contains("ISO646_International", capabilities);
        Assert.Contains("NDGRAPHICS", capabilities);
        Assert.Contains("NDWORKAREA", capabilities);
        Assert.Contains("NDPROTECTED", capabilities);
        Assert.Contains("NDLEDS", capabilities);
        Assert.Contains("NDPUSHKEYS", capabilities);
    }

    [Fact]
    public void TheCapabilitiesNameBothGraphicsAndTektronixWithoutBeingAsked()
    {
        // Two tests used to sit here, each sending an unsourced sequence first and then checking
        // that the string had grown. The string should never have needed the sequence.
        var capabilities = _emulator.GetTerminalCapabilities();

        Assert.Contains("TDV2200", capabilities);
        Assert.Contains("+GRAPHICS", capabilities);
        Assert.Contains("+TEKTRONIX", capabilities);
    }

    [Fact]
    public void ResetToInitialState_ShouldResetAllState()
    {
        // Arrange
        _emulator.CharacterSetVariant = (int)TDV2200ISO646Variant.Swedish;

        // Act
        _emulator.ResetToInitialState();

        // Assert. A reset cannot take the Tektronix decoder away - it is not a mode, so there is
        // nothing for a reset to return to.
        Assert.True(_emulator.DrawsTektronixVectors);
        Assert.Equal(TDV2200ISO646Variant.International, _emulator.CurrentISO646Variant);
    }
}
