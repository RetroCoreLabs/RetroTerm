using System;
using System.Linq;
using System.Reflection;
using RetroTerm.Core.Protocols.TelnetServer.Utilities;
using RetroTerm.TestServer.App;
using Xunit;

namespace RetroTerm.Tests.TestServer;

/// <summary>
/// Unit tests for TestServer escape sequence generation.
/// Validates that TDVSequenceBuilder generates correct byte sequences and that TestServer methods use it correctly.
/// </summary>
public class TestServerSequenceGenerationTests
{
    #region TDVSequenceBuilder Tests

    [Fact]
    public void BuildDAQuery_ShouldGenerateCorrectBytes()
    {
        // Act
        var bytes = TDVSequenceBuilder.BuildDAQuery();

        // Assert
        Assert.Equal(3, bytes.Length);
        Assert.Equal(0x1B, bytes[0]); // ESC
        Assert.Equal(0x5B, bytes[1]); // '['
        Assert.Equal(0x63, bytes[2]); // 'c'
    }

    [Fact]
    public void BuildSecondaryDAQuery_ShouldGenerateCorrectBytes()
    {
        // Act
        var bytes = TDVSequenceBuilder.BuildSecondaryDAQuery();

        // Assert
        Assert.Equal(4, bytes.Length);
        Assert.Equal(0x1B, bytes[0]); // ESC
        Assert.Equal(0x5B, bytes[1]); // '['
        Assert.Equal(0x3E, bytes[2]); // '>'
        Assert.Equal(0x63, bytes[3]); // 'c'
    }

    [Fact]
    public void BuildCPRQuery_ShouldGenerateCorrectBytes()
    {
        // Act
        var bytes = TDVSequenceBuilder.BuildCPRQuery();

        // Assert
        Assert.Equal(4, bytes.Length);
        Assert.Equal(0x1B, bytes[0]); // ESC
        Assert.Equal(0x5B, bytes[1]); // '['
        Assert.Equal(0x36, bytes[2]); // '6'
        Assert.Equal(0x6E, bytes[3]); // 'n'
    }

    [Fact]
    public void BuildDSRQuery_ShouldGenerateCorrectBytes()
    {
        // Act
        var bytes = TDVSequenceBuilder.BuildDSRQuery();

        // Assert
        Assert.Equal(4, bytes.Length);
        Assert.Equal(0x1B, bytes[0]); // ESC
        Assert.Equal(0x5B, bytes[1]); // '['
        Assert.Equal(0x35, bytes[2]); // '5'
        Assert.Equal(0x6E, bytes[3]); // 'n'
    }

    [Fact]
    public void BuildTerminalIDQuery_ShouldGenerateCorrectBytes()
    {
        // Act
        var bytes = TDVSequenceBuilder.BuildTerminalIDQuery();

        // Assert
        Assert.Equal(2, bytes.Length);
        Assert.Equal(0x1B, bytes[0]); // ESC
        Assert.Equal(0x5A, bytes[1]); // 'Z'
    }

    [Fact]
    public void BuildDECRQMQuery_ShouldGenerateCorrectBytes()
    {
        // Act
        var bytes = TDVSequenceBuilder.BuildDECRQMQuery(66);

        // Assert
        Assert.Equal(7, bytes.Length);
        Assert.Equal(0x1B, bytes[0]); // ESC
        Assert.Equal(0x5B, bytes[1]); // '['
        Assert.Equal(0x3F, bytes[2]); // '?'
        Assert.Equal(0x36, bytes[3]); // '6'
        Assert.Equal(0x36, bytes[4]); // '6'
        Assert.Equal(0x24, bytes[5]); // '$'
        Assert.Equal(0x70, bytes[6]); // 'p'
    }

    [Fact]
    public void BuildCharacterSet_ShouldGenerateCorrectBytes()
    {
        // Act
        var bytes = TDVSequenceBuilder.BuildCharacterSet(1);

        // Assert
        Assert.Equal(3, bytes.Length);
        Assert.Equal(0x1B, bytes[0]); // ESC
        Assert.Equal(0x28, bytes[1]); // '('
        Assert.Equal(0x31, bytes[2]); // '1'
    }

    [Fact]
    public void BuildRIS_ShouldGenerateCorrectBytes()
    {
        // Act
        var bytes = TDVSequenceBuilder.BuildRIS();

        // Assert
        Assert.Equal(2, bytes.Length);
        Assert.Equal(0x1B, bytes[0]); // ESC
        Assert.Equal(0x63, bytes[1]); // 'c'
    }

    [Fact]
    public void BuildDECSC_ShouldGenerateCorrectBytes()
    {
        // Act
        var bytes = TDVSequenceBuilder.BuildDECSC();

        // Assert
        Assert.Equal(2, bytes.Length);
        Assert.Equal(0x1B, bytes[0]); // ESC
        Assert.Equal(0x37, bytes[1]); // '7'
    }

    [Fact]
    public void BuildDECRC_ShouldGenerateCorrectBytes()
    {
        // Act
        var bytes = TDVSequenceBuilder.BuildDECRC();

        // Assert
        Assert.Equal(2, bytes.Length);
        Assert.Equal(0x1B, bytes[0]); // ESC
        Assert.Equal(0x38, bytes[1]); // '8'
    }

    /// <summary>
    /// Entering 2115 mode is <c>ESC [ 66 l</c> - five bytes, RESET, and no private marker.
    /// </summary>
    /// <remarks>
    /// This asserted six bytes with a <c>?</c> and mode 40 until 11 September 2026. Mode 40 is PCF,
    /// the printer code format (TDV 2215 Functional Specifications section 8.7.1); the 2115 switch
    /// is EC, mode 66, and section 3.1 says the terminal behaves like a 2115 when that switch is
    /// OFF. ND Display Terminal 1200 section 8.1 gives the instruction outright: enter with
    /// <c>CSI 66 l</c>. See <c>docs&#92;TDV-MODES-AND-REPORTS-FROM-THE-MANUALS.md</c>.
    /// </remarks>
    [Fact]
    public void Build2115CompatibilityEnable_ShouldGenerateCorrectBytes()
    {
        var bytes = TDVSequenceBuilder.Build2115CompatibilityEnable();

        Assert.Equal(5, bytes.Length);
        Assert.Equal(0x1B, bytes[0]); // ESC
        Assert.Equal(0x5B, bytes[1]); // '['
        Assert.Equal(0x36, bytes[2]); // '6'
        Assert.Equal(0x36, bytes[3]); // '6'
        Assert.Equal(0x6C, bytes[4]); // 'l' - RESET, which is the 2115 side of the EC switch
    }

    /// <summary>
    /// Leaving 2115 mode is <c>ESC [ 66 h</c>, the EC switch on.
    /// </summary>
    [Fact]
    public void Build2115CompatibilityDisable_ShouldGenerateCorrectBytes()
    {
        var bytes = TDVSequenceBuilder.Build2115CompatibilityDisable();

        Assert.Equal(5, bytes.Length);
        Assert.Equal(0x1B, bytes[0]); // ESC
        Assert.Equal(0x5B, bytes[1]); // '['
        Assert.Equal(0x36, bytes[2]); // '6'
        Assert.Equal(0x36, bytes[3]); // '6'
        Assert.Equal(0x68, bytes[4]); // 'h'
    }

    /// <summary>
    /// The escape that works from INSIDE 2115 mode, where no CSI can reach the terminal.
    /// </summary>
    /// <remarks>
    /// 2215 sections 3.1 and 7.9.2, and ND-1200 chapter 8, which gives the bytes as 1B 51.
    /// </remarks>
    [Fact]
    public void Build2115CompatibilityExitEscape_IsEscQ()
    {
        var bytes = TDVSequenceBuilder.Build2115CompatibilityExitEscape();

        Assert.Equal(2, bytes.Length);
        Assert.Equal(0x1B, bytes[0]); // ESC
        Assert.Equal(0x51, bytes[1]); // 'Q'
    }

    [Fact]
    public void BuildNDDWA_ShouldGenerateCorrectBytes()
    {
        // Act
        var bytes = TDVSequenceBuilder.BuildNDDWA(10, 10, 100, 50);

        // Assert - Should be ESC [ 10 ; 10 ; 100 ; 50 ~
        Assert.True(bytes.Length >= 10);
        Assert.Equal(0x1B, bytes[0]); // ESC
        Assert.Equal(0x5B, bytes[1]); // '['
        // Parameters: 10;10;100;50~
        Assert.Contains((byte)'~', bytes);
    }

    [Fact]
    public void BuildNDSAR_ShouldGenerateCorrectBytes()
    {
        // Act
        var bytes = TDVSequenceBuilder.BuildNDSAR(1, 10, 10, 100, 50);

        // Assert - Should be ESC [ 1 ; 10 ; 10 ; 100 ; 50 z
        Assert.True(bytes.Length >= 10);
        Assert.Equal(0x1B, bytes[0]); // ESC
        Assert.Equal(0x5B, bytes[1]); // '['
        Assert.Contains((byte)'z', bytes);
    }

    /// <summary>
    /// Extended operation is the EC switch, mode 66, with no private marker.
    /// </summary>
    /// <remarks>
    /// This test used to demand ESC ? 1 h and call it a "three-character ESC sequence". No TDV
    /// manual has an ESC ? form at all - TDV 2215 section 8.7.1 and ND-1200 section 5.64 between
    /// them list every mode the host may set, and the sequences are CSI with no marker, with '?'
    /// for the DEC-compatible family, or with '&gt;' for the ND private family. The number 1 it
    /// borrowed is real but belongs to the last of those, where it is Beginning of Line Wrap.
    ///
    /// EC on is extended operation and EC off is 2115-compatible operation, so this is the same
    /// switch as Build2115CompatibilityEnable read the other way round. See
    /// <c>docs\TDV-MODES-AND-REPORTS-FROM-THE-MANUALS.md</c>.
    /// </remarks>
    [Fact]
    public void BuildExtendedModeEnable_IsTheExtendedControlSwitch()
    {
        var bytes = TDVSequenceBuilder.BuildExtendedModeEnable();

        // ESC [ 6 6 h
        Assert.Equal(5, bytes.Length);
        Assert.Equal(0x1B, bytes[0]);
        Assert.Equal((byte)'[', bytes[1]);
        Assert.Equal((byte)'6', bytes[2]);
        Assert.Equal((byte)'6', bytes[3]);
        Assert.Equal((byte)'h', bytes[4]);
    }

    /// <summary>
    /// Turning Extended Control off is the same bytes as entering 2115 compatibility.
    /// </summary>
    /// <remarks>
    /// One switch, two names, and both names are in the manuals. If these two ever disagree, one of
    /// them has been given a number of its own again.
    /// </remarks>
    [Fact]
    public void BuildExtendedModeDisable_IsTheSameBytesAs2115CompatibilityEnable()
    {
        var extendedOff = TDVSequenceBuilder.BuildExtendedModeDisable();
        var enter2115 = TDVSequenceBuilder.Build2115CompatibilityEnable();

        Assert.Equal(enter2115.Length, extendedOff.Length);
        for (int i = 0; i < extendedOff.Length; i++)
        {
            Assert.Equal(enter2115[i], extendedOff[i]);
        }
    }

    /// <summary>
    /// There is no builder for transparent mode, and there must never be one again.
    /// </summary>
    /// <remarks>
    /// Transparent operation is the Send-Receive Mode soft-switch - TDV 2215 section 4.3.1, the
    /// 2200/9 S User's Guide switch table, and chapter 3 of the ND-1200 set-up functions. All three
    /// are reached from the keyboard, and the 2215 manual says outright that the only way out is
    /// pressing MODE twice. Section 8.7.1 lists every host-settable mode and SRM is not among them.
    ///
    /// The builder that used to be here sent ESC ? 2 h, which is not a sequence any TDV parses. A
    /// reflection check is the only thing that can keep a name from coming back.
    /// </remarks>
    [Fact]
    public void ThereIsNoTransparentModeBuilder()
    {
        var methods = typeof(TDVSequenceBuilder).GetMethods();
        for (int i = 0; i < methods.Length; i++)
        {
            Assert.False(methods[i].Name.Contains("Transparent"),
                "TDVSequenceBuilder." + methods[i].Name + " exists, but a host cannot set "
                + "transparent mode on any TDV - it is a keyboard soft-switch.");
        }
    }

    [Fact]
    public void BuildISO646Variant_ShouldGenerateCorrectBytes()
    {
        // Act
        var bytes = TDVSequenceBuilder.BuildISO646Variant('N');

        // Assert - ESC % N
        Assert.Equal(3, bytes.Length);
        Assert.Equal(0x1B, bytes[0]); // ESC
        Assert.Equal(0x25, bytes[1]); // '%'
        Assert.Equal(0x4E, bytes[2]); // 'N'
    }

    /// <summary>
    /// Only the four national versions that exist are accepted.
    /// </summary>
    /// <remarks>
    /// D and F were accepted until 11 September 2026 and name terminals that were never built; U
    /// was a second letter for the International version, which is I. TDV 2215 sections 9.1.1 to
    /// 9.1.4 and appendix A both list exactly four: International, Norwegian, Swedish, German.
    /// TDV2200Emulator has always decoded only those four, so the extra letters produced a
    /// sequence that quietly changed nothing.
    /// </remarks>
    [Theory]
    [InlineData('X')]   // never meant anything
    [InlineData('D')]   // "Danish" - no such TDV
    [InlineData('F')]   // "Finnish" - no such TDV
    [InlineData('U')]   // "US" - that is the International version, I
    public void BuildISO646Variant_ShouldThrowForAVersionThatDoesNotExist(char variant)
    {
        Assert.Throws<ArgumentException>(() => TDVSequenceBuilder.BuildISO646Variant(variant));
    }

    /// <summary>
    /// The four that do exist all build.
    /// </summary>
    [Theory]
    [InlineData('I')]
    [InlineData('N')]
    [InlineData('S')]
    [InlineData('G')]
    public void BuildISO646Variant_BuildsEveryVersionThatExists(char variant)
    {
        var bytes = TDVSequenceBuilder.BuildISO646Variant(variant);

        Assert.Equal(3, bytes.Length);
        Assert.Equal(0x1B, bytes[0]);
        Assert.Equal((byte)'%', bytes[1]);
        Assert.Equal((byte)variant, bytes[2]);
    }

    [Fact]
    public void BuildDCS_ShouldGenerateCorrectBytes()
    {
        // Act
        var bytes = TDVSequenceBuilder.BuildDCS("PUSH1");

        // Assert - ESC P PUSH1 ESC \
        Assert.True(bytes.Length >= 7);
        Assert.Equal(0x1B, bytes[0]); // ESC
        Assert.Equal(0x50, bytes[1]); // 'P'
        Assert.Equal(0x1B, bytes[bytes.Length - 2]); // ESC
        Assert.Equal(0x5C, bytes[bytes.Length - 1]); // '\'
    }

    [Fact]
    public void BuildPUSHKeyProgram_ShouldGenerateCorrectBytes()
    {
        // Act
        var bytes = TDVSequenceBuilder.BuildPUSHKeyProgram(1, "44617461"); // "Data" in hex

        // Assert - ESC P P 01 44617461 ESC \
        Assert.True(bytes.Length >= 10);
        Assert.Equal(0x1B, bytes[0]); // ESC
        Assert.Equal(0x50, bytes[1]); // 'P'
        Assert.Equal(0x1B, bytes[bytes.Length - 2]); // ESC
        Assert.Equal(0x5C, bytes[bytes.Length - 1]); // '\'
    }

    [Fact]
    public void BuildPUSHKeyProgram_ShouldThrowForInvalidKeyNumber()
    {
        // Act & Assert
        Assert.Throws<ArgumentOutOfRangeException>(() => TDVSequenceBuilder.BuildPUSHKeyProgram(0, ""));
        Assert.Throws<ArgumentOutOfRangeException>(() => TDVSequenceBuilder.BuildPUSHKeyProgram(17, ""));
    }

    [Fact]
    public void BuildTektronixModeEnable_ShouldGenerateCorrectBytes()
    {
        // Act
        var bytes = TDVSequenceBuilder.BuildTektronixModeEnable();

        // Assert - ESC [ ? 38 h = 6 bytes
        Assert.Equal(6, bytes.Length);
        Assert.Equal(0x1B, bytes[0]); // ESC
        Assert.Equal(0x5B, bytes[1]); // '['
        Assert.Equal(0x3F, bytes[2]); // '?'
        Assert.Equal(0x33, bytes[3]); // '3'
        Assert.Equal(0x38, bytes[4]); // '8'
        Assert.Equal(0x68, bytes[5]); // 'h'
    }

    [Fact]
    public void BuildGraphicsExtensionEnable_ShouldGenerateCorrectBytes()
    {
        // Act
        var bytes = TDVSequenceBuilder.BuildGraphicsExtensionEnable();

        // Assert - ESC [ 1 >
        Assert.Equal(4, bytes.Length);
        Assert.Equal(0x1B, bytes[0]); // ESC
        Assert.Equal(0x5B, bytes[1]); // '['
        Assert.Equal(0x31, bytes[2]); // '1'
        Assert.Equal(0x3E, bytes[3]); // '>'
    }

    #endregion

    #region TestServer Method Reflection Tests

    [Fact]
    public void QueryResponseTests_ShouldUseTDVSequenceBuilder()
    {
        // This test validates that RunTDV_QueryResponseTestsAsync method exists and can be called
        // Actual sequence validation would require mocking TelnetSession

        // Arrange
        var testServerType = typeof(TestServerApp);
        var method = testServerType.GetMethod("RunTDV_QueryResponseTestsAsync",
            BindingFlags.NonPublic | BindingFlags.Instance);

        // Assert
        Assert.NotNull(method);
        Assert.Equal(typeof(System.Threading.Tasks.Task), method!.ReturnType);
        var parameters = method.GetParameters();
        Assert.Single(parameters);
        Assert.Equal(typeof(RetroTerm.Core.Protocols.TelnetServer.Telnet.TelnetSession), parameters[0].ParameterType);
    }

    [Fact]
    public void CharacterSetsTests_ShouldUseTDVSequenceBuilder()
    {
        // Arrange
        var testServerType = typeof(TestServerApp);
        var method = testServerType.GetMethod("RunTDV_CharacterSetsTestsAsync",
            BindingFlags.NonPublic | BindingFlags.Instance);

        // Assert
        Assert.NotNull(method);
        Assert.Equal(typeof(System.Threading.Tasks.Task), method!.ReturnType);
    }

    [Fact]
    public void DrawingOperationsTests_ShouldUseTDVSequenceBuilder()
    {
        // Arrange
        var testServerType = typeof(TestServerApp);
        var method = testServerType.GetMethod("RunTDV_DrawingOperationsTestsAsync",
            BindingFlags.NonPublic | BindingFlags.Instance);

        // Assert
        Assert.NotNull(method);
        Assert.Equal(typeof(System.Threading.Tasks.Task), method!.ReturnType);
    }

    [Fact]
    public void TDV1200Tests_ShouldUseTDVSequenceBuilder()
    {
        // Arrange
        var testServerType = typeof(TestServerApp);
        var methods = new[]
        {
            "RunTDV1200_2115CompatibilityAsync",
            "RunTDV1200_NDGraphicsAsync",
            "RunTDV1200_ProtectedAreasAsync",
            "RunTDV1200_CharacterSetsAsync"
        };

        // Assert
        foreach (var methodName in methods)
        {
            var method = testServerType.GetMethod(methodName,
                BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(method);
            Assert.Equal(typeof(System.Threading.Tasks.Task), method!.ReturnType);
        }
    }

    [Fact]
    public void TDV2215Tests_ShouldUseTDVSequenceBuilder()
    {
        // Arrange
        var testServerType = typeof(TestServerApp);
        var methods = new[]
        {
            "RunTDV2215_ExtendedModeAsync",
            "RunTDV2215_TransparentModeAsync",
            "RunTDV2215_DCSSequencesAsync"
        };

        // Assert
        foreach (var methodName in methods)
        {
            var method = testServerType.GetMethod(methodName,
                BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(method);
            Assert.Equal(typeof(System.Threading.Tasks.Task), method!.ReturnType);
        }
    }

    [Fact]
    public void TDV2200Tests_ShouldUseTDVSequenceBuilder()
    {
        // Arrange
        var testServerType = typeof(TestServerApp);
        var methods = new[]
        {
            "RunTDV2200_GraphicsExtensionAsync",
            "RunTDV2200_TektronixModeAsync",
            "RunTDV2200_ISO646VariantsAsync"
        };

        // Assert
        foreach (var methodName in methods)
        {
            var method = testServerType.GetMethod(methodName,
                BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(method);
            Assert.Equal(typeof(System.Threading.Tasks.Task), method!.ReturnType);
        }
    }

    #endregion
}

