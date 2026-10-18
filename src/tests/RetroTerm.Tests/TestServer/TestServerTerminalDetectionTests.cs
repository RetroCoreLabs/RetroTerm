using System;
using System.Reflection;
using RetroTerm.Core.Protocols.TelnetServer.Utilities;
using RetroTerm.TestServer.App;
using Xunit;

namespace RetroTerm.Tests.TestServer;

/// <summary>
/// Unit tests for TestServer terminal detection functionality.
/// Validates terminal type detection via DA queries and manual selection.
/// </summary>
public class TestServerTerminalDetectionTests
{
    [Fact]
    public void DetectTerminalTypeAsync_ShouldExist()
    {
        // Arrange
        var testServerType = typeof(TestServerApp);

        // Assert - Method should exist
        var method = testServerType.GetMethod("DetectTerminalTypeAsync",
            BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(method);
        Assert.Equal(typeof(System.Threading.Tasks.Task), method!.ReturnType);
        var parameters = method.GetParameters();
        Assert.Single(parameters);
        Assert.Equal(typeof(RetroTerm.Core.Protocols.TelnetServer.Telnet.TelnetSession), parameters[0].ParameterType);
    }

    [Fact]
    public void TryDetectViaTerminalTypeAsync_ShouldExist()
    {
        // Arrange
        var testServerType = typeof(TestServerApp);

        // Assert - Method should exist
        var method = testServerType.GetMethod("TryDetectViaTerminalTypeAsync",
            BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(method);
        Assert.Equal(typeof(System.Threading.Tasks.Task<TerminalType>), method!.ReturnType);
    }

    [Fact]
    public void TryDetectViaDeviceAttributesAsync_ShouldExist()
    {
        // Arrange
        var testServerType = typeof(TestServerApp);

        // Assert - Method should exist
        var method = testServerType.GetMethod("TryDetectViaDeviceAttributesAsync",
            BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(method);
        Assert.Equal(typeof(System.Threading.Tasks.Task<TerminalType>), method!.ReturnType);
    }

    [Fact]
    public void ParseTerminalTypeString_ShouldExist()
    {
        // Arrange
        var testServerType = typeof(TestServerApp);

        // Assert - Method should exist
        var method = testServerType.GetMethod("ParseTerminalTypeString",
            BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(method);
        Assert.Equal(typeof(TerminalType), method!.ReturnType);
        var parameters = method.GetParameters();
        Assert.Single(parameters);
        Assert.Equal(typeof(string), parameters[0].ParameterType);
    }

    [Fact]
    public void ManualTerminalTypeSelectionAsync_ShouldExist()
    {
        // Arrange
        var testServerType = typeof(TestServerApp);

        // Assert - Method should exist
        var method = testServerType.GetMethod("ManualTerminalTypeSelectionAsync",
            BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(method);
        Assert.Equal(typeof(System.Threading.Tasks.Task), method!.ReturnType);
        var parameters = method.GetParameters();
        Assert.Single(parameters);
        Assert.Equal(typeof(RetroTerm.Core.Protocols.TelnetServer.Telnet.TelnetSession), parameters[0].ParameterType);
    }

    [Fact]
    public void TDVResponseValidator_ParseDAResponse_ShouldDetectTDV1200()
    {
        // Arrange
        var response = "\x1b[>120;0;0c"; // TDV1200 Secondary DA response

        // Act
        var parsed = TDVResponseValidator.ParseDAResponse(response);

        // Assert
        Assert.True(parsed.IsValid);
        Assert.True(parsed.IsSecondary);
        Assert.Equal(120, parsed.FirmwareId);
        Assert.True(TDVResponseValidator.ValidateDATerminalType(parsed, TerminalType.TDV1200));
    }

    [Fact]
    public void TDVResponseValidator_ParseDAResponse_ShouldDetectTDV2200()
    {
        // Arrange
        var response = "\x1b[>220;0;0c"; // TDV2200 Secondary DA response

        // Act
        var parsed = TDVResponseValidator.ParseDAResponse(response);

        // Assert
        Assert.True(parsed.IsValid);
        Assert.True(parsed.IsSecondary);
        Assert.Equal(220, parsed.FirmwareId);
        Assert.True(TDVResponseValidator.ValidateDATerminalType(parsed, TerminalType.TDV2200));
    }

    [Fact]
    public void TDVResponseValidator_ParseDAResponse_ShouldDetectTDV2215()
    {
        // Arrange
        var response = "\x1b[>115;0;0c"; // TDV2215 Secondary DA response

        // Act
        var parsed = TDVResponseValidator.ParseDAResponse(response);

        // Assert
        Assert.True(parsed.IsValid);
        Assert.True(parsed.IsSecondary);
        Assert.Equal(115, parsed.FirmwareId);
        Assert.True(TDVResponseValidator.ValidateDATerminalType(parsed, TerminalType.TDV2215));
    }

    [Fact]
    public void TDVResponseValidator_ParseTerminalIDResponse_ShouldDetectTDV1200()
    {
        // Arrange
        var response = "\x1b[?120;0c"; // TDV1200 Terminal ID response

        // Act
        var parsed = TDVResponseValidator.ParseTerminalIDResponse(response);

        // Assert
        Assert.True(parsed.IsValid);
        Assert.Equal(120, parsed.FirmwareId);
        Assert.Equal("TDV1200", parsed.TerminalType);
        Assert.True(TDVResponseValidator.ValidateTerminalIDType(parsed, TerminalType.TDV1200));
    }

    [Fact]
    public void TDVResponseValidator_ParseTerminalIDResponse_ShouldDetectTDV2200()
    {
        // Arrange
        var response = "\x1b[?220;0c"; // TDV2200 Terminal ID response

        // Act
        var parsed = TDVResponseValidator.ParseTerminalIDResponse(response);

        // Assert
        Assert.True(parsed.IsValid);
        Assert.Equal(220, parsed.FirmwareId);
        Assert.Equal("TDV2200", parsed.TerminalType);
        Assert.True(TDVResponseValidator.ValidateTerminalIDType(parsed, TerminalType.TDV2200));
    }

    [Fact]
    public void TDVResponseValidator_ParseTerminalIDResponse_ShouldDetectTDV2215()
    {
        // Arrange
        var response = "\x1b[?115;0c"; // TDV2215 Terminal ID response

        // Act
        var parsed = TDVResponseValidator.ParseTerminalIDResponse(response);

        // Assert
        Assert.True(parsed.IsValid);
        Assert.Equal(115, parsed.FirmwareId);
        Assert.Equal("TDV2215", parsed.TerminalType);
        Assert.True(TDVResponseValidator.ValidateTerminalIDType(parsed, TerminalType.TDV2215));
    }

    [Fact]
    public void TDVCapabilityChecker_ShouldIdentifyTDV1200Capabilities()
    {
        // Arrange
        var checker = new TDVCapabilityChecker(TerminalType.TDV1200);

        // Assert
        Assert.True(checker.IsTDV1200());
        Assert.True(checker.IsTDVTerminal());
        Assert.True(checker.Supports("2115Compatibility"));
        Assert.True(checker.Supports("NDGraphics"));
        Assert.True(checker.Supports("CharacterSets"));
        // ND-1200 section 5.64 lists mode 66, the 2115/extended switch, and chapter 3 of the
        // set-up functions lists Transparent mode. Both read false here until 11 September 2026.
        Assert.True(checker.Supports("ExtendedMode"));
        Assert.True(checker.Supports("TransparentMode"));
        Assert.False(checker.Supports("ISO646Variants"));
    }

    [Fact]
    public void TDVCapabilityChecker_ShouldIdentifyTDV2200Capabilities()
    {
        // Arrange
        var checker = new TDVCapabilityChecker(TerminalType.TDV2200);

        // Assert
        Assert.True(checker.IsTDV2200());
        Assert.True(checker.IsTDVTerminal());
        Assert.True(checker.Supports("2115Compatibility"));
        Assert.True(checker.Supports("NDGraphics"));
        Assert.True(checker.Supports("ISO646Variants"));
        Assert.True(checker.Supports("TektronixMode"));
        // TDV 2200/9 S User's Guide soft-switch table: Extended Control Mode On/Off, and Send
        // Receive Mode Simultaneous/Transparent. The 2200 has both.
        Assert.True(checker.Supports("ExtendedMode"));
        Assert.True(checker.Supports("TransparentMode"));
    }

    [Fact]
    public void TDVCapabilityChecker_ShouldIdentifyTDV2215Capabilities()
    {
        // Arrange
        var checker = new TDVCapabilityChecker(TerminalType.TDV2215);

        // Assert
        Assert.True(checker.IsTDV2215());
        Assert.True(checker.IsTDVTerminal());
        Assert.True(checker.Supports("2115Compatibility"));
        Assert.True(checker.Supports("NDGraphics"));
        Assert.True(checker.Supports("ExtendedMode"));
        Assert.True(checker.Supports("TransparentMode"));
        Assert.True(checker.Supports("DCSSequences"));
        Assert.False(checker.Supports("ISO646Variants"));
    }

    [Fact]
    public void TDVCapabilityChecker_ShouldValidateTestRequirements()
    {
        // Arrange
        var checker = new TDVCapabilityChecker(TerminalType.TDV2215);

        // Assert
        Assert.True(checker.ValidateTestRequirements("ExtendedMode", "DCSSequences"));
        Assert.False(checker.ValidateTestRequirements("ExtendedMode", "ISO646Variants"));
    }
}

