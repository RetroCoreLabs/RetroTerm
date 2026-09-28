using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using RetroTerm.Core.Session;
using RetroTerm.Core.Terminal.Emulators.TDV;
using Xunit;

namespace RetroTerm.Tests.TDV;

/// <summary>
/// End-to-end tests for TDV query/response functionality
/// These tests simulate the full flow: query -> emulator -> response -> connection
/// </summary>
public class TDVQueryResponseE2ETests
{
    [Fact]
    public async Task TDV2215_PrimaryDA_ShouldSendResponse()
    {
        // Arrange
        var emulator = new TDV2215Emulator(80, 24);
        var session = new TerminalSession(emulator, "Test");
        var connection = new InMemoryConnection();

        // Act: Connect and send query
        await session.ConnectAsync(connection);
        await Task.Delay(100); // Give time for event wiring

        // Send Primary DA query: ESC [ c
        connection.SimulateReceive(Encoding.UTF8.GetBytes("\x1b[c"));
        await Task.Delay(100); // Give time for processing

        // Assert: Response should be sent
        var sentData = connection.GetSentData();
        Assert.True(sentData.Count > 0, "No data was sent through connection");

        var lastSent = connection.GetLastSentAsString();
        Assert.NotNull(lastSent);
        Assert.Contains("\x1b[", lastSent); // Should contain escape sequence
        Assert.Contains("c", lastSent); // Should end with 'c' for DA response

        // Cleanup
        await session.DisconnectAsync();
    }

    [Fact]
    public async Task TDV2215_SecondaryDA_ShouldSendResponse()
    {
        // Arrange
        var emulator = new TDV2215Emulator(80, 24);
        var session = new TerminalSession(emulator, "Test");
        var connection = new InMemoryConnection();

        // Act: Connect and send query
        await session.ConnectAsync(connection);
        await Task.Delay(100);

        // Send Secondary DA query: ESC [ > c
        connection.SimulateReceive(Encoding.UTF8.GetBytes("\x1b[>c"));
        await Task.Delay(100);

        // Assert: Response should be sent
        var sentData = connection.GetSentData();
        Assert.True(sentData.Count > 0, "No data was sent through connection");

        var lastSent = connection.GetLastSentAsString();
        Assert.NotNull(lastSent);
        Assert.Contains("\x1b[>", lastSent); // Should contain Secondary DA response format

        // Cleanup
        await session.DisconnectAsync();
    }

    [Fact]
    public async Task TDV2215_CPR_ShouldSendResponse()
    {
        // Arrange
        var emulator = new TDV2215Emulator(80, 24);
        var session = new TerminalSession(emulator, "Test");
        var connection = new InMemoryConnection();

        // Act: Connect and send query
        await session.ConnectAsync(connection);
        await Task.Delay(100);

        // Send CPR query: ESC [ 6 n
        connection.SimulateReceive(Encoding.UTF8.GetBytes("\x1b[6n"));
        await Task.Delay(100);

        // Assert: Response should be sent
        var sentData = connection.GetSentData();
        Assert.True(sentData.Count > 0, "No data was sent through connection");

        var lastSent = connection.GetLastSentAsString();
        Assert.NotNull(lastSent);
        Assert.Contains("\x1b[", lastSent); // Should contain escape sequence
        Assert.Contains("R", lastSent); // CPR response format: ESC [ row ; col R

        // Cleanup
        await session.DisconnectAsync();
    }

    [Fact]
    public async Task TDV2215_DSR_ShouldSendResponse()
    {
        // Arrange
        var emulator = new TDV2215Emulator(80, 24);
        var session = new TerminalSession(emulator, "Test");
        var connection = new InMemoryConnection();

        // Act: Connect and send query
        await session.ConnectAsync(connection);
        await Task.Delay(100);

        // Send DSR query: ESC [ 5 n
        connection.SimulateReceive(Encoding.UTF8.GetBytes("\x1b[5n"));
        await Task.Delay(100);

        // Assert: Response should be sent
        var sentData = connection.GetSentData();
        Assert.True(sentData.Count > 0, "No data was sent through connection");

        var lastSent = connection.GetLastSentAsString();
        Assert.NotNull(lastSent);
        Assert.Contains("\x1b[", lastSent); // Should contain escape sequence
        Assert.Contains("0n", lastSent); // DSR response format: ESC [ 0 n (ready)

        // Cleanup
        await session.DisconnectAsync();
    }

    [Fact]
    public async Task TDV2215_TerminalID_ShouldSendResponse()
    {
        // Arrange
        var emulator = new TDV2215Emulator(80, 24);
        var session = new TerminalSession(emulator, "Test");
        var connection = new InMemoryConnection();

        // Act: Connect and send query
        await session.ConnectAsync(connection);
        await Task.Delay(100);

        // Send Terminal ID query: ESC Z
        connection.SimulateReceive(Encoding.UTF8.GetBytes("\x1bZ"));
        await Task.Delay(100);

        // Assert: Response should be sent
        var sentData = connection.GetSentData();
        Assert.True(sentData.Count > 0, "No data was sent through connection");

        var lastSent = connection.GetLastSentAsString();
        Assert.NotNull(lastSent);
        // Terminal ID response should contain terminal identifier
        Assert.Contains("\x1b", lastSent);

        // Cleanup
        await session.DisconnectAsync();
    }

    [Fact]
    public async Task TDV2215_MultipleQueries_ShouldSendMultipleResponses()
    {
        // Arrange
        var emulator = new TDV2215Emulator(80, 24);
        var session = new TerminalSession(emulator, "Test");
        var connection = new InMemoryConnection();

        // Act: Connect and send multiple queries
        await session.ConnectAsync(connection);
        await Task.Delay(100);

        connection.ClearSentData();

        // Send Primary DA
        connection.SimulateReceive(Encoding.UTF8.GetBytes("\x1b[c"));
        await Task.Delay(100);

        var countAfterDA = connection.GetSentData().Count;
        Assert.True(countAfterDA > 0, "Primary DA query did not generate response");

        // Send Secondary DA
        connection.SimulateReceive(Encoding.UTF8.GetBytes("\x1b[>c"));
        await Task.Delay(100);

        var countAfterSecondaryDA = connection.GetSentData().Count;
        Assert.True(countAfterSecondaryDA > countAfterDA, "Secondary DA query did not generate response");

        // Send CPR
        connection.SimulateReceive(Encoding.UTF8.GetBytes("\x1b[6n"));
        await Task.Delay(100);

        var countAfterCPR = connection.GetSentData().Count;
        Assert.True(countAfterCPR > countAfterSecondaryDA, "CPR query did not generate response");

        // Cleanup
        await session.DisconnectAsync();
    }

    [Fact]
    public async Task TDV2215_QueryBeforeConnection_ShouldNotSendResponse()
    {
        // Arrange
        var emulator = new TDV2215Emulator(80, 24);
        var session = new TerminalSession(emulator, "Test");
        var connection = new InMemoryConnection();

        // Act: Send query BEFORE connecting
        connection.SimulateReceive(Encoding.UTF8.GetBytes("\x1b[c"));
        await Task.Delay(100);

        // Assert: No response should be sent (connection not established)
        var sentData = connection.GetSentData();
        Assert.Empty(sentData);

        // Now connect and verify it works after connection
        await session.ConnectAsync(connection);
        await Task.Delay(100);

        connection.ClearSentData();
        connection.SimulateReceive(Encoding.UTF8.GetBytes("\x1b[c"));
        await Task.Delay(100);

        Assert.True(connection.GetSentData().Count > 0, "Response should be sent after connection");

        // Cleanup
        await session.DisconnectAsync();
    }
}

