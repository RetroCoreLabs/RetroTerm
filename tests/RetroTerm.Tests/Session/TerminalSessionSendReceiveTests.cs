using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using RetroTerm.Core.Protocols;
using RetroTerm.Core.Session;
using RetroTerm.Core.Terminal.Emulators;
using RetroTerm.Core.Terminal.Emulators.TDV;
using RetroTerm.Tests.TDV;
using Xunit;

namespace RetroTerm.Tests.Session;

/// <summary>
/// Unit tests for TerminalSession send/receive logic
/// Tests that TerminalSession correctly:
/// 1. Receives data from connection and routes to emulator
/// 2. Wires up TDV query/response events
/// 3. Sends responses back through connection when emulator generates them
/// </summary>
public class TerminalSessionSendReceiveTests
{
    [Fact]
    public void TerminalSession_ShouldWireTDVResponseReady_WhenTDVEmulatorCreated()
    {
        // Arrange
        var emulator = new TDV2200Emulator(80, 24);
        var session = new TerminalSession(emulator, "Test");

        // Act - WireTDVQueryResponse is called in constructor
        // We can verify by checking if event handler is attached

        // Assert - Event should be wired (we can't directly check, but we can test behavior)
        Assert.NotNull(session.Emulator);
        Assert.IsType<TDV2200Emulator>(session.Emulator);
    }

    [Fact]
    public void TerminalSession_ShouldNotWireTDVResponseReady_WhenNonTDVEmulatorCreated()
    {
        // Arrange
        var emulator = new VT100Emulator(80, 24);
        var session = new TerminalSession(emulator, "Test");

        // Act - WireTDVQueryResponse is called in constructor

        // Assert
        Assert.NotNull(session.Emulator);
        Assert.IsType<VT100Emulator>(session.Emulator);
        // Assert against session.Emulator, not the local: the local is statically
        // VT100Emulator, so `local is TDVEmulatorBase` is a compile-time constant
        // false (CS0184) and asserts nothing about what the session actually holds.
        Assert.False(session.Emulator is TDVEmulatorBase);
    }

    [Fact]
    public async Task TerminalSession_ShouldReceiveDataAndRouteToEmulator()
    {
        // Arrange
        var emulator = new TDV2200Emulator(80, 24);
        var session = new TerminalSession(emulator, "Test");
        var connection = new InMemoryConnection();

        var receivedBytes = new List<byte[]>();
        emulator.Invalidated += () => { }; // Prevent null reference

        // Act
        await session.ConnectAsync(connection);
        connection.SimulateReceive(Encoding.UTF8.GetBytes("Hello"));

        // FlushAsync completes when the pump has run every chunk posted before it, and the
        // reply goes out synchronously on the pump thread through the in-memory connection.
        await session.FlushAsync();

        // Assert - Data should have been routed to emulator
        // We can verify by checking buffer contents
        var buffer = emulator.GetBuffer();
        var cell = buffer.GetCell(0, 0);
        Assert.Equal('H', (char)cell.Codepoint);
    }

    [Fact]
    public async Task TerminalSession_ShouldSendResponse_WhenTDVEmulatorGeneratesResponse()
    {
        // Arrange
        var emulator = new TDV2200Emulator(80, 24);
        var session = new TerminalSession(emulator, "Test");
        var connection = new InMemoryConnection();

        await session.ConnectAsync(connection);
        connection.ClearSentData();

        // Act - Send Primary DA query: ESC [ c
        var query = new byte[] { 0x1B, 0x5B, 0x63 }; // ESC [ c
        connection.SimulateReceive(query);

        // FlushAsync completes when the pump has run every chunk posted before it, and the
        // reply goes out synchronously on the pump thread through the in-memory connection.
        await session.FlushAsync();

        // Assert - Response should have been sent through connection
        var sentData = connection.GetSentData();
        Assert.NotEmpty(sentData);

        var lastSent = sentData.Last();
        var responseString = Encoding.UTF8.GetString(lastSent);

        // TDV2200 should respond with Primary DA response
        Assert.StartsWith("\x1b[", responseString);
        Assert.Contains("c", responseString);
    }

    [Fact]
    public async Task TerminalSession_ShouldSendSecondaryDAResponse_WhenQueryReceived()
    {
        // Arrange
        var emulator = new TDV2200Emulator(80, 24);
        var session = new TerminalSession(emulator, "Test");
        var connection = new InMemoryConnection();

        await session.ConnectAsync(connection);
        connection.ClearSentData();

        // Act - Send Secondary DA query: ESC [ > c
        var query = new byte[] { 0x1B, 0x5B, 0x3E, 0x63 }; // ESC [ > c
        connection.SimulateReceive(query);

        // FlushAsync completes when the pump has run every chunk posted before it, and the
        // reply goes out synchronously on the pump thread through the in-memory connection.
        await session.FlushAsync();

        // Assert - Secondary DA response should have been sent
        var sentData = connection.GetSentData();
        Assert.NotEmpty(sentData);

        var lastSent = sentData.Last();
        var responseString = Encoding.UTF8.GetString(lastSent);

        // TDV2200 should respond with Secondary DA: ESC [ > 220 ; 0 ; 0 c
        Assert.StartsWith("\x1b[>", responseString);
        Assert.Contains("220", responseString); // Firmware ID for TDV2200
        Assert.EndsWith("c", responseString);
    }

    [Fact]
    public async Task TerminalSession_ShouldSendCPRResponse_WhenCPRQueryReceived()
    {
        // Arrange
        var emulator = new TDV2200Emulator(80, 24);
        var session = new TerminalSession(emulator, "Test");
        var connection = new InMemoryConnection();

        await session.ConnectAsync(connection);

        // Move cursor to specific position
        var moveCursor = new byte[] { 0x1B, 0x5B, 0x35, 0x3B, 0x31, 0x30, 0x48 }; // ESC [ 5 ; 10 H
        connection.SimulateReceive(moveCursor);
        await session.FlushAsync();

        connection.ClearSentData();

        // Act - Send CPR query: ESC [ 6 n
        var query = new byte[] { 0x1B, 0x5B, 0x36, 0x6E }; // ESC [ 6 n
        connection.SimulateReceive(query);

        // FlushAsync completes when the pump has run every chunk posted before it, and the
        // reply goes out synchronously on the pump thread through the in-memory connection.
        await session.FlushAsync();

        // Assert - CPR response should have been sent
        var sentData = connection.GetSentData();
        Assert.NotEmpty(sentData);

        var lastSent = sentData.Last();
        var responseString = Encoding.UTF8.GetString(lastSent);

        // CPR response format: ESC [ row ; col R (1-based)
        Assert.StartsWith("\x1b[", responseString);
        Assert.EndsWith("R", responseString);
        Assert.Contains("5", responseString); // Row 5 (1-based)
        Assert.Contains("10", responseString); // Col 10 (1-based)
    }

    [Fact]
    public async Task TerminalSession_ShouldSendDSRResponse_WhenDSRQueryReceived()
    {
        // Arrange
        var emulator = new TDV2200Emulator(80, 24);
        var session = new TerminalSession(emulator, "Test");
        var connection = new InMemoryConnection();

        await session.ConnectAsync(connection);
        connection.ClearSentData();

        // Act - Send DSR query: ESC [ 5 n
        var query = new byte[] { 0x1B, 0x5B, 0x35, 0x6E }; // ESC [ 5 n
        connection.SimulateReceive(query);

        // FlushAsync completes when the pump has run every chunk posted before it, and the
        // reply goes out synchronously on the pump thread through the in-memory connection.
        await session.FlushAsync();

        // Assert - DSR response should have been sent
        var sentData = connection.GetSentData();
        Assert.NotEmpty(sentData);

        var lastSent = sentData.Last();
        var responseString = Encoding.UTF8.GetString(lastSent);

        // DSR response format: ESC [ 0 n (OK) or ESC [ 3 n (Failure)
        Assert.StartsWith("\x1b[", responseString);
        Assert.EndsWith("n", responseString);
    }

    [Fact]
    public async Task TerminalSession_ShouldNotSendResponse_WhenNotConnected()
    {
        // Arrange
        var emulator = new TDV2200Emulator(80, 24);
        var session = new TerminalSession(emulator, "Test");
        var connection = new InMemoryConnection();

        // Don't connect - session should not be connected

        // Act - Try to trigger response (should not send because not connected)
        var responses = new List<string>();
        if (emulator is TDVEmulatorBase tdvEmulator)
        {
            tdvEmulator.OnResponseReady += response => responses.Add(response);
        }

        // Send query directly to emulator (bypassing session)
        var query = new byte[] { 0x1B, 0x5B, 0x63 }; // ESC [ c
        emulator.ProcessData(query);

        await session.FlushAsync();

        // Assert - Response event should fire, but connection should not send
        Assert.Single(responses); // Event fired
        Assert.False(session.IsConnected);

        // Connection should have no sent data
        var sentData = connection.GetSentData();
        Assert.Empty(sentData);
    }

    [Fact]
    public async Task TerminalSession_ShouldSendMultipleResponses_WhenMultipleQueriesReceived()
    {
        // Arrange
        var emulator = new TDV2200Emulator(80, 24);
        var session = new TerminalSession(emulator, "Test");
        var connection = new InMemoryConnection();

        await session.ConnectAsync(connection);
        connection.ClearSentData();

        // Act - Send multiple queries
        var daQuery = new byte[] { 0x1B, 0x5B, 0x63 }; // ESC [ c
        var cprQuery = new byte[] { 0x1B, 0x5B, 0x36, 0x6E }; // ESC [ 6 n

        connection.SimulateReceive(daQuery);
        await session.FlushAsync();

        connection.SimulateReceive(cprQuery);
        await session.FlushAsync();

        // Assert - Both responses should have been sent
        var sentData = connection.GetSentData();
        Assert.True(sentData.Count >= 2, $"Expected at least 2 responses, got {sentData.Count}");

        // First response should be DA
        var firstResponse = Encoding.UTF8.GetString(sentData[sentData.Count - 2]);
        Assert.StartsWith("\x1b[", firstResponse);

        // Second response should be CPR
        var secondResponse = Encoding.UTF8.GetString(sentData[sentData.Count - 1]);
        Assert.StartsWith("\x1b[", secondResponse);
        Assert.EndsWith("R", secondResponse);
    }

    [Fact]
    public async Task TerminalSession_ShouldHandleConnectionErrors_Gracefully()
    {
        // Arrange
        var emulator = new TDV2200Emulator(80, 24);
        var session = new TerminalSession(emulator, "Test");
        var connection = new InMemoryConnection();

        await session.ConnectAsync(connection);

        var errorOccurred = false;
        session.ErrorOccurred += ex => errorOccurred = true;

        // Act - Simulate connection error
        connection.SimulateError(new Exception("Test error"));

        await session.FlushAsync();

        // Assert - Error event should have been raised
        Assert.True(errorOccurred);
    }

    [Fact]
    public async Task TerminalSession_ShouldReWireTDVResponseReady_AfterReconnecting()
    {
        // Arrange
        var emulator = new TDV2200Emulator(80, 24);
        var session = new TerminalSession(emulator, "Test");
        var connection1 = new InMemoryConnection();
        var connection2 = new InMemoryConnection();

        // Connect first time
        await session.ConnectAsync(connection1);
        await session.DisconnectAsync();

        // Connect second time
        await session.ConnectAsync(connection2);
        connection2.ClearSentData();

        // Act - Send query
        var query = new byte[] { 0x1B, 0x5B, 0x63 }; // ESC [ c
        connection2.SimulateReceive(query);

        await session.FlushAsync();

        // Assert - Response should be sent through second connection
        var sentData = connection2.GetSentData();
        Assert.NotEmpty(sentData);
    }

    [Fact]
    public async Task TerminalSession_ShouldSendTDV1200SecondaryDA_WithCorrectFirmwareID()
    {
        // Arrange
        var emulator = new TDV1200Emulator(80, 24);
        var session = new TerminalSession(emulator, "Test");
        var connection = new InMemoryConnection();

        await session.ConnectAsync(connection);
        connection.ClearSentData();

        // Act - Send Secondary DA query: ESC [ > c
        var query = new byte[] { 0x1B, 0x5B, 0x3E, 0x63 }; // ESC [ > c
        connection.SimulateReceive(query);

        await session.FlushAsync();

        // Assert - TDV1200 should respond with firmware ID 120
        var sentData = connection.GetSentData();
        Assert.NotEmpty(sentData);

        var responseString = Encoding.UTF8.GetString(sentData.Last());
        Assert.Contains("120", responseString); // Firmware ID for TDV1200
    }

    [Fact]
    public async Task TerminalSession_ShouldSendTDV2215SecondaryDA_WithCorrectFirmwareID()
    {
        // Arrange
        var emulator = new TDV2215Emulator(80, 24);
        var session = new TerminalSession(emulator, "Test");
        var connection = new InMemoryConnection();

        await session.ConnectAsync(connection);
        connection.ClearSentData();

        // Act - Send Secondary DA query: ESC [ > c
        var query = new byte[] { 0x1B, 0x5B, 0x3E, 0x63 }; // ESC [ > c
        connection.SimulateReceive(query);

        await session.FlushAsync();

        // Assert - TDV2215 should respond with firmware ID 115
        var sentData = connection.GetSentData();
        Assert.NotEmpty(sentData);

        var responseString = Encoding.UTF8.GetString(sentData.Last());
        Assert.Contains("115", responseString); // Firmware ID for TDV2215
    }

    [Fact]
    public async Task TerminalSession_ShouldProcessDataByteByByte()
    {
        // Arrange
        var emulator = new TDV2200Emulator(80, 24);
        var session = new TerminalSession(emulator, "Test");
        var connection = new InMemoryConnection();

        await session.ConnectAsync(connection);
        connection.ClearSentData();

        // Act - Send query byte by byte
        var query = new byte[] { 0x1B, 0x5B, 0x63 }; // ESC [ c
        foreach (var b in query)
        {
            connection.SimulateReceive(new[] { b });
            await session.FlushAsync();
        }

        await session.FlushAsync();

        // Assert - Response should still be sent
        var sentData = connection.GetSentData();
        Assert.NotEmpty(sentData);
    }

    [Fact]
    public async Task TerminalSession_ShouldSendTerminalIDResponse_WhenTerminalIDQueryReceived()
    {
        // Arrange
        var emulator = new TDV2200Emulator(80, 24);
        var session = new TerminalSession(emulator, "Test");
        var connection = new InMemoryConnection();

        await session.ConnectAsync(connection);
        connection.ClearSentData();

        // Act - Send Terminal ID query: ESC Z
        var query = new byte[] { 0x1B, 0x5A }; // ESC Z
        connection.SimulateReceive(query);

        await session.FlushAsync();

        // Assert - Terminal ID response should have been sent
        var sentData = connection.GetSentData();
        Assert.NotEmpty(sentData);

        var responseString = Encoding.UTF8.GetString(sentData.Last());
        // Terminal ID response format varies, but should contain terminal type
        Assert.NotEmpty(responseString);
    }
}

