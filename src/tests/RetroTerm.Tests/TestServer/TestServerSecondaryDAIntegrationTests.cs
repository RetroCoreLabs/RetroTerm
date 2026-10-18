using System;
using System.Text;
using System.Threading.Tasks;
using RetroTerm.Core.Protocols.TelnetServer.Utilities;
using RetroTerm.Core.Terminal.Emulators.TDV;
using RetroTerm.Tests.TDV.TestHelpers;
using Xunit;

namespace RetroTerm.Tests.TestServer;

/// <summary>
/// Integration tests for Secondary DA query/response flow.
/// Tests the complete flow: Send query -> Emulator responds -> Capture response -> Parse response
/// </summary>
public class TestServerSecondaryDAIntegrationTests
{
    [Fact]
    public void TDV2200_SecondaryDA_EndToEnd_ShouldWork()
    {
        // Arrange
        var emulator = new TDV2200Emulator(80, 24);
        var responses = new System.Collections.Generic.List<string>();
        emulator.OnResponseReady += response => responses.Add(response);

        // Act - Send Secondary DA query: ESC [ > c
        var query = TDVSequenceBuilder.BuildSecondaryDAQuery();
        emulator.ProcessInput(query);

        // Assert - Response should be generated
        Assert.Single(responses);
        var response = responses[0];

        // Verify response format
        Assert.StartsWith("\x1b[>", response);
        Assert.EndsWith("c", response);
        Assert.Contains("220", response); // TDV2200 firmware ID

        // Verify parser can parse it
        var parsed = TDVResponseValidator.ParseDAResponse(response);
        Assert.True(parsed.IsValid, $"Response should be valid. Raw: {TDVResponseValidator.ToVisibleString(response)}");
        Assert.True(parsed.IsSecondary, "Should be identified as Secondary DA");
        Assert.Equal(220, parsed.FirmwareId);
    }

    [Fact]
    public void TDV1200_SecondaryDA_EndToEnd_ShouldWork()
    {
        // Arrange
        var emulator = new TDV1200Emulator(80, 24);
        var responses = new System.Collections.Generic.List<string>();
        emulator.OnResponseReady += response => responses.Add(response);

        // Act - Send Secondary DA query: ESC [ > c
        var query = TDVSequenceBuilder.BuildSecondaryDAQuery();
        emulator.ProcessInput(query);

        // Assert - Response should be generated
        Assert.Single(responses);
        var response = responses[0];

        // Verify response format
        Assert.StartsWith("\x1b[>", response);
        Assert.EndsWith("c", response);
        Assert.Contains("120", response); // TDV1200 firmware ID

        // Verify parser can parse it
        var parsed = TDVResponseValidator.ParseDAResponse(response);
        Assert.True(parsed.IsValid, $"Response should be valid. Raw: {TDVResponseValidator.ToVisibleString(response)}");
        Assert.True(parsed.IsSecondary, "Should be identified as Secondary DA");
        Assert.Equal(120, parsed.FirmwareId);
    }

    [Fact]
    public void TDV2215_SecondaryDA_EndToEnd_ShouldWork()
    {
        // Arrange
        var emulator = new TDV2215Emulator(80, 24);
        var responses = new System.Collections.Generic.List<string>();
        emulator.OnResponseReady += response => responses.Add(response);

        // Act - Send Secondary DA query: ESC [ > c
        var query = TDVSequenceBuilder.BuildSecondaryDAQuery();
        emulator.ProcessInput(query);

        // Assert - Response should be generated
        Assert.Single(responses);
        var response = responses[0];

        // Verify response format
        Assert.StartsWith("\x1b[>", response);
        Assert.EndsWith("c", response);
        Assert.Contains("115", response); // TDV2215 firmware ID

        // Verify parser can parse it
        var parsed = TDVResponseValidator.ParseDAResponse(response);
        Assert.True(parsed.IsValid, $"Response should be valid. Raw: {TDVResponseValidator.ToVisibleString(response)}");
        Assert.True(parsed.IsSecondary, "Should be identified as Secondary DA");
        Assert.Equal(115, parsed.FirmwareId);
    }

    [Fact]
    public void BuildSecondaryDAQuery_ShouldGenerateCorrectSequence()
    {
        // Act
        var bytes = TDVSequenceBuilder.BuildSecondaryDAQuery();

        // Assert - Should be ESC [ > c
        Assert.Equal(4, bytes.Length);
        Assert.Equal(0x1B, bytes[0]); // ESC
        Assert.Equal(0x5B, bytes[1]); // '['
        Assert.Equal(0x3E, bytes[2]); // '>'
        Assert.Equal(0x63, bytes[3]); // 'c'

        // Verify it matches expected string format
        var asString = Encoding.UTF8.GetString(bytes);
        Assert.Equal("\x1b[>c", asString);
    }
}

