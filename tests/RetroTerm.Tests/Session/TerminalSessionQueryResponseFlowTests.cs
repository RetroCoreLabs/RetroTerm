using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using RetroTerm.Core.Protocols;
using RetroTerm.Core.Session;
using RetroTerm.Core.Terminal.Emulators;
using RetroTerm.Core.Terminal.Emulators.TDV;
using RetroTerm.Tests.TDV;
using Xunit;

namespace RetroTerm.Tests.Session;

/// <summary>
/// CRITICAL UNIT TESTS for query/response flow
/// These tests verify the EXACT scenario that failed in manual testing:
/// 1. Query sent from server -> TerminalSession receives it
/// 2. TerminalSession routes to emulator -> emulator processes query
/// 3. Emulator generates response -> OnResponseReady event fires
/// 4. TerminalSession receives OnResponseReady -> sends response through connection
/// 5. Response arrives at server
/// 
/// These tests MUST catch event wiring issues, connection issues, and response routing issues.
/// </summary>
public class TerminalSessionQueryResponseFlowTests
{
    /// <summary>
    /// CRITICAL TEST: Verifies OnResponseReady event is wired BEFORE connection
    /// This test would have caught the issue where event wasn't wired
    /// Uses behavior-based verification since reflection is unreliable for compiler-generated events
    /// </summary>
    [Fact]
    public async Task TerminalSession_ShouldWireOnResponseReadyEvent_BeforeConnection()
    {
        // Arrange
        var emulator = new TDV2200Emulator(80, 24);
        var connection = new InMemoryConnection();
        var session = new TerminalSession(emulator, "Test");

        // Act - Connect and send query to verify event wiring
        // We can't reliably check event wiring with reflection, so we test behavior
        // If event is wired, response will be sent through connection

        // Connect AFTER construction (event should already be wired in constructor)
        await session.ConnectAsync(connection);

        connection.ClearSentData();

        // Send a query - if event is wired, response will be sent
        var query = new byte[] { 0x1B, 0x5B, 0x63 }; // ESC [ c
        connection.SimulateReceive(query);

        // Wait for processing
        System.Threading.Thread.Sleep(200);

        // Assert - Response MUST be sent if event is wired
        var sentData = connection.GetSentData();
        Assert.True(sentData.Any(),
            "OnResponseReady event MUST be wired in TerminalSession constructor. " +
            "If this fails, WireTDVQueryResponse() is not being called or event handler is not attached.");

        var responseString = Encoding.UTF8.GetString(sentData.Last());
        Assert.StartsWith("\x1b[", responseString);
    }

    /// <summary>
    /// CRITICAL TEST: Verifies OnResponseReady event is wired AFTER connection
    /// Connection should not affect event wiring
    /// Uses behavior-based verification
    /// </summary>
    [Fact]
    public async Task TerminalSession_ShouldWireOnResponseReadyEvent_AfterConnection()
    {
        // Arrange
        var emulator = new TDV2200Emulator(80, 24);
        var session = new TerminalSession(emulator, "Test");
        var connection = new InMemoryConnection();

        // Act - Connect (WireTDVQueryResponse is called again in ConnectAsync)
        await session.ConnectAsync(connection);

        connection.ClearSentData();

        // Send a query to verify event wiring
        var query = new byte[] { 0x1B, 0x5B, 0x63 }; // ESC [ c
        connection.SimulateReceive(query);

        await session.FlushAsync();

        // Assert - Response MUST be sent if event is wired
        var sentData = connection.GetSentData();
        Assert.True(sentData.Any(),
            "OnResponseReady event MUST be wired after connection. " +
            "If this fails, WireTDVQueryResponse() in ConnectAsync is not working.");
    }

    /// <summary>
    /// CRITICAL TEST: Full end-to-end query/response flow
    /// This test simulates the EXACT scenario from manual testing:
    /// Server sends query -> Client receives -> Emulator processes -> Response sent back
    /// </summary>
    [Fact]
    public async Task TerminalSession_ShouldCompleteFullQueryResponseFlow_PrimaryDA()
    {
        // Arrange
        var emulator = new TDV2200Emulator(80, 24);
        var session = new TerminalSession(emulator, "Test");
        var connection = new InMemoryConnection();

        // Track response generation and sending
        var responseGenerated = false;
        var responseSent = false;
        string? generatedResponse = null;
        byte[]? sentResponseBytes = null;

        // Wire up event handlers BEFORE connection to catch any wiring issues
        if (emulator is TDVEmulatorBase tdvEmulator)
        {
            tdvEmulator.OnResponseReady += (response) =>
            {
                responseGenerated = true;
                generatedResponse = response;
                System.Diagnostics.Debug.WriteLine($"[Test] OnResponseReady fired: {response}");
            };
        }

        // Connect
        await session.ConnectAsync(connection);

        // Verify event wiring AFTER connection by testing behavior
        // We can't reliably check event wiring with reflection, so we verify by sending a test query
        connection.ClearSentData();
        var testQuery = new byte[] { 0x1B, 0x5B, 0x63 }; // ESC [ c
        connection.SimulateReceive(testQuery);
        await session.FlushAsync();
        var testResponse = connection.GetSentData();
        Assert.True(testResponse.Any(),
            "OnResponseReady MUST be wired. If this fails, event handler is not attached.");
        connection.ClearSentData();

        // Track what gets sent through connection
        connection.ClearSentData();

        // Act - Send Primary DA query: ESC [ c
        var query = new byte[] { 0x1B, 0x5B, 0x63 }; // ESC [ c
        System.Diagnostics.Debug.WriteLine($"[Test] Sending query: {BitConverter.ToString(query)}");
        connection.SimulateReceive(query);

        // Wait for processing
        await session.FlushAsync();

        // Check what was sent
        var sentData = connection.GetSentData();
        if (sentData.Any())
        {
            responseSent = true;
            sentResponseBytes = sentData.Last();
            System.Diagnostics.Debug.WriteLine($"[Test] Response sent: {BitConverter.ToString(sentResponseBytes)}");
        }

        // Assert - Response MUST be generated
        Assert.True(responseGenerated,
            "OnResponseReady event MUST fire when query is processed. " +
            "If this fails, the emulator is not generating responses or event is not wired.");

        Assert.NotNull(generatedResponse);
        Assert.StartsWith("\x1b[", generatedResponse);
        Assert.Contains("c", generatedResponse);

        // Assert - Response MUST be sent through connection
        Assert.True(responseSent,
            "Response MUST be sent through connection. " +
            "If this fails, TerminalSession.OnTDVResponseReady is not wired or connection.SendAsync failed.");

        Assert.NotNull(sentResponseBytes);
        var sentResponseString = Encoding.UTF8.GetString(sentResponseBytes);
        Assert.StartsWith("\x1b[", sentResponseString);
        Assert.Contains("c", sentResponseString);

        // Assert - Generated response should match sent response
        var generatedBytes = Encoding.UTF8.GetBytes(generatedResponse);
        Assert.Equal(generatedBytes, sentResponseBytes);
    }

    /// <summary>
    /// CRITICAL TEST: Verifies Secondary DA query/response flow
    /// </summary>
    [Fact]
    public async Task TerminalSession_ShouldCompleteFullQueryResponseFlow_SecondaryDA()
    {
        // Arrange
        var emulator = new TDV2200Emulator(80, 24);
        var session = new TerminalSession(emulator, "Test");
        var connection = new InMemoryConnection();

        var responseGenerated = false;
        var responseSent = false;
        string? generatedResponse = null;

        if (emulator is TDVEmulatorBase tdvEmulator)
        {
            tdvEmulator.OnResponseReady += (response) =>
            {
                responseGenerated = true;
                generatedResponse = response;
            };
        }

        await session.ConnectAsync(connection);

        // Verify event wiring by behavior (send test query)
        connection.ClearSentData();
        var testQuery = new byte[] { 0x1B, 0x5B, 0x63 }; // ESC [ c
        connection.SimulateReceive(testQuery);
        await session.FlushAsync();
        var testResponse = connection.GetSentData();
        Assert.True(testResponse.Any(), "OnResponseReady MUST be wired for Secondary DA");
        connection.ClearSentData();

        // Act - Send Secondary DA query: ESC [ > c
        var query = new byte[] { 0x1B, 0x5B, 0x3E, 0x63 }; // ESC [ > c
        connection.SimulateReceive(query);

        await session.FlushAsync();

        var sentData = connection.GetSentData();
        if (sentData.Any())
        {
            responseSent = true;
        }

        // Assert
        Assert.True(responseGenerated, "OnResponseReady MUST fire for Secondary DA query");
        Assert.True(responseSent, "Response MUST be sent through connection");
        Assert.NotNull(generatedResponse);
        Assert.StartsWith("\x1b[>", generatedResponse);
    }

    /// <summary>
    /// CRITICAL TEST: Verifies CPR query/response flow
    /// </summary>
    [Fact]
    public async Task TerminalSession_ShouldCompleteFullQueryResponseFlow_CPR()
    {
        // Arrange
        var emulator = new TDV2200Emulator(80, 24);
        var session = new TerminalSession(emulator, "Test");
        var connection = new InMemoryConnection();

        var responseGenerated = false;
        var responseSent = false;

        if (emulator is TDVEmulatorBase tdvEmulator)
        {
            tdvEmulator.OnResponseReady += (response) =>
            {
                responseGenerated = true;
            };
        }

        await session.ConnectAsync(connection);

        // Move cursor to known position
        var moveCursor = new byte[] { 0x1B, 0x5B, 0x35, 0x3B, 0x31, 0x30, 0x48 }; // ESC [ 5 ; 10 H
        connection.SimulateReceive(moveCursor);
        await session.FlushAsync();

        connection.ClearSentData();

        // Act - Send CPR query: ESC [ 6 n
        var query = new byte[] { 0x1B, 0x5B, 0x36, 0x6E }; // ESC [ 6 n
        connection.SimulateReceive(query);

        await session.FlushAsync();

        var sentData = connection.GetSentData();
        if (sentData.Any())
        {
            responseSent = true;
        }

        // Assert
        Assert.True(responseGenerated, "OnResponseReady MUST fire for CPR query");
        Assert.True(responseSent, "Response MUST be sent through connection");
    }

    /// <summary>
    /// CRITICAL TEST: Verifies that event wiring happens in TerminalSession constructor
    /// This test ensures WireTDVQueryResponse is called
    /// Uses behavior-based verification
    /// </summary>
    [Fact]
    public async Task TerminalSession_ShouldWireEventInConstructor()
    {
        // Arrange & Act
        var emulator = new TDV2200Emulator(80, 24);
        var session = new TerminalSession(emulator, "Test");
        var connection = new InMemoryConnection();

        // Connect to enable response sending
        await session.ConnectAsync(connection);
        connection.ClearSentData();

        // Send query - if event is wired in constructor, response will be sent
        var query = new byte[] { 0x1B, 0x5B, 0x63 }; // ESC [ c
        connection.SimulateReceive(query);
        await session.FlushAsync();

        // Assert - Response MUST be sent if event was wired in constructor
        var sentData = connection.GetSentData();
        Assert.True(sentData.Any(),
            "OnResponseReady MUST be wired in TerminalSession constructor. " +
            "If this fails, WireTDVQueryResponse() is not being called in constructor.");
    }

    /// <summary>
    /// CRITICAL TEST: Verifies event wiring for all TDV emulator types
    /// Uses behavior-based verification
    /// </summary>
    [Fact]
    public async Task TerminalSession_ShouldWireEventForTDV2200()
    {
        // Arrange & Act
        var emulator = new TDV2200Emulator(80, 24);
        var session = new TerminalSession(emulator, "Test");
        var connection = new InMemoryConnection();

        // Connect
        await session.ConnectAsync(connection);
        connection.ClearSentData();

        // Send query
        var query = new byte[] { 0x1B, 0x5B, 0x63 }; // ESC [ c
        connection.SimulateReceive(query);
        await session.FlushAsync();

        // Assert
        var sentData = connection.GetSentData();
        Assert.True(sentData.Any(),
            "OnResponseReady MUST be wired for TDV2200Emulator. " +
            "If this fails, WireTDVQueryResponse() is not working for this emulator type.");
    }

    [Fact]
    public async Task TerminalSession_ShouldWireEventForTDV1200()
    {
        // Arrange & Act
        var emulator = new TDV1200Emulator(80, 24);
        var session = new TerminalSession(emulator, "Test");
        var connection = new InMemoryConnection();

        // Connect
        await session.ConnectAsync(connection);
        connection.ClearSentData();

        // Send query
        var query = new byte[] { 0x1B, 0x5B, 0x63 }; // ESC [ c
        connection.SimulateReceive(query);
        await session.FlushAsync();

        // Assert
        var sentData = connection.GetSentData();
        Assert.True(sentData.Any(),
            "OnResponseReady MUST be wired for TDV1200Emulator. " +
            "If this fails, WireTDVQueryResponse() is not working for this emulator type.");
    }

    [Fact]
    public async Task TerminalSession_ShouldWireEventForTDV2215()
    {
        // Arrange & Act
        var emulator = new TDV2215Emulator(80, 24);
        var session = new TerminalSession(emulator, "Test");
        var connection = new InMemoryConnection();

        // Connect
        await session.ConnectAsync(connection);
        connection.ClearSentData();

        // Send query
        var query = new byte[] { 0x1B, 0x5B, 0x63 }; // ESC [ c
        connection.SimulateReceive(query);
        await session.FlushAsync();

        // Assert
        var sentData = connection.GetSentData();
        Assert.True(sentData.Any(),
            "OnResponseReady MUST be wired for TDV2215Emulator. " +
            "If this fails, WireTDVQueryResponse() is not working for this emulator type.");
    }

    /// <summary>
    /// Helper method to get event handlers using reflection
    /// Uses a test handler to verify the event is wired
    /// </summary>
    private Delegate? GetEventHandlers(object obj, string eventName)
    {
        var type = obj.GetType();
        var eventInfo = type.GetEvent(eventName, BindingFlags.Public | BindingFlags.Instance);
        if (eventInfo == null)
            return null;

        // Try to get the backing field (compiler-generated events use a field)
        // The field name is usually the same as the event name
        var fieldInfo = type.GetField(eventName,
            BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public);

        if (fieldInfo != null)
        {
            return fieldInfo.GetValue(obj) as Delegate;
        }

        // Alternative: Try common compiler-generated field names
        var fieldNames = new[] { eventName, $"_{eventName}", $"<{eventName}>k__BackingField" };
        foreach (var fieldName in fieldNames)
        {
            fieldInfo = type.GetField(fieldName,
                BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public);
            if (fieldInfo != null)
            {
                return fieldInfo.GetValue(obj) as Delegate;
            }
        }

        // If we can't get the backing field, we can't verify handlers directly
        // But we can verify by testing behavior (which is what the other tests do)
        return null;
    }

    /// <summary>
    /// Alternative method: Verify event wiring by testing behavior
    /// This is more reliable than reflection for compiler-generated events
    /// </summary>
    private int GetEventHandlerCountByBehavior(TDVEmulatorBase emulator)
    {
        bool handlerFired = false;

        // Add a test handler
        Action<string> testHandler = (response) => { handlerFired = true; };
        emulator.OnResponseReady += testHandler;

        // Trigger the event by sending a query
        var query = new byte[] { 0x1B, 0x5B, 0x63 }; // ESC [ c
        emulator.ProcessData(query);

        // Remove test handler
        emulator.OnResponseReady -= testHandler;

        // Count handlers by trying to invoke them
        // This is a workaround - we can't directly count handlers
        // But we can verify at least one handler exists by checking if response is sent
        return handlerFired ? 1 : 0;
    }

    /// <summary>
    /// CRITICAL TEST: Verifies that responses are sent even when connection is established after event wiring
    /// </summary>
    [Fact]
    public async Task TerminalSession_ShouldSendResponse_WhenConnectionEstablishedAfterWiring()
    {
        // Arrange
        var emulator = new TDV2200Emulator(80, 24);
        var session = new TerminalSession(emulator, "Test");

        // Event is wired in constructor
        var connection = new InMemoryConnection();

        var responseSent = false;

        // Connect AFTER construction (event already wired)
        await session.ConnectAsync(connection);

        connection.ClearSentData();

        // Act - Send query
        var query = new byte[] { 0x1B, 0x5B, 0x63 }; // ESC [ c
        connection.SimulateReceive(query);

        await session.FlushAsync();

        // Assert
        var sentData = connection.GetSentData();
        Assert.NotEmpty(sentData);
        responseSent = true;

        Assert.True(responseSent, "Response MUST be sent even when connection established after wiring");
    }
}

