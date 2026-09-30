using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using RetroTerm.Core.Protocols.TelnetServer.Telnet;
using RetroTerm.Core.Protocols.TelnetServer.Utilities;
using RetroTerm.Core.Session;
using RetroTerm.Core.Terminal.Emulators;
using RetroTerm.Core.Terminal.Emulators.TDV;
using RetroTerm.TestServer.App;
using Xunit;

namespace RetroTerm.Tests.Session;

/// <summary>
/// Unit tests that connect TerminalSession to TestServer logic using in-memory bidirectional connections
/// Tests the ACTUAL flow without TCP overhead
/// </summary>
public class TerminalSessionTestServerIntegrationTests : IDisposable
{
    private InMemoryBidirectionalConnection? _clientConnection;
    private InMemoryBidirectionalConnection? _serverConnection;
    private TerminalSession? _session;
    private TestServerApp? _testServerApp;
    private MockTelnetSession? _telnetSession;

    public void Dispose()
    {
        _session?.DisconnectAsync().GetAwaiter().GetResult();
        _session?.Dispose();
        _clientConnection?.Dispose();
        _serverConnection?.Dispose();
    }

    /// <summary>
    /// Sets up a connected TerminalSession and TestServerApp using in-memory connections
    /// </summary>
    private async Task SetupConnectionAsync(TerminalEmulatorBase emulator)
    {
        // Create bidirectional connection pair
        (_clientConnection, _serverConnection) = InMemoryBidirectionalConnection.CreatePair();

        // Connect both connections
        await _clientConnection.ConnectAsync();
        await _serverConnection.ConnectAsync();

        // Create TerminalSession with client connection
        _session = new TerminalSession(emulator, "Test");
        await _session.ConnectAsync(_clientConnection);

        // Create TestServerApp with server connection
        _testServerApp = new TestServerApp();

        // Create a mock TelnetSession that uses our in-memory connection
        var mockSession = new MockTelnetSession(_serverConnection);
        _telnetSession = mockSession;

        // Initialize TestServerApp connection in background (it has a blocking while loop)
        // We don't await it - it runs in background and we can send queries directly
        _ = Task.Run(async () =>
        {
            try
            {
                await _testServerApp.OnConnectedAsync(mockSession);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SetupConnectionAsync] TestServerApp.OnConnectedAsync error: {ex}");
            }
        });

        // Give connections time to stabilize and TestServerApp to initialize
        await Task.Delay(300);

        // Verify event wiring
        VerifyEventWiring(emulator);
    }

    /// <summary>
    /// Verifies that event wiring is correct
    /// </summary>
    private void VerifyEventWiring(TerminalEmulatorBase emulator)
    {
        // Verify TerminalSession has connection set
        Assert.NotNull(_session?.Connection);
        Assert.True(_session.IsConnected);

        // Verify the session is wired to carry emulator replies to the host.
        //
        // This used to check TDVEmulatorBase.OnResponseReady for subscribers. That was checking
        // WHICH handler happened to be attached rather than whether replies can reach the host,
        // so it broke when the two reply channels were unified even though replies still flowed.
        // DataToSend is the wire now — for TDV and everything else alike. OnResponseReady is an
        // observation point and may legitimately have no subscribers at all.
        var dataToSendField = typeof(TerminalEmulatorBase).GetField("DataToSend",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        if (dataToSendField != null)
        {
            var handlers = dataToSendField.GetValue(emulator) as Delegate;
            Assert.NotNull(handlers);
        }

        // Verify InMemoryBidirectionalConnection DataReceived event is wired
        Assert.NotNull(_clientConnection);
    }

    /// <summary>
    /// Logs the current state of both connections for debugging
    /// </summary>
    private void LogConnectionState(string context = "")
    {
        System.Diagnostics.Debug.WriteLine($"=== Connection State Log [{context}] ===");

        if (_clientConnection != null)
        {
            var clientStats = _clientConnection.GetStatistics();
            System.Diagnostics.Debug.WriteLine($"Client Connection:");
            System.Diagnostics.Debug.WriteLine($"  Status: {_clientConnection.Status}");
            System.Diagnostics.Debug.WriteLine($"  IsConnected: {_clientConnection.IsConnected}");
            System.Diagnostics.Debug.WriteLine($"  Data Sent: {clientStats.DataSentCount}");
            System.Diagnostics.Debug.WriteLine($"  Data Received: {clientStats.DataReceivedCount}");

            var clientSent = _clientConnection.GetAllSentData();
            System.Diagnostics.Debug.WriteLine($"  Sent Queue Count: {clientSent.Count}");
            for (int i = 0; i < clientSent.Count; i++)
            {
                System.Diagnostics.Debug.WriteLine($"    [{i}] {BitConverter.ToString(clientSent[i])} ({clientSent[i].Length} bytes)");
            }

            var clientReceived = _clientConnection.GetAllReceivedData();
            System.Diagnostics.Debug.WriteLine($"  Received Queue Count: {clientReceived.Count}");
            for (int i = 0; i < clientReceived.Count; i++)
            {
                System.Diagnostics.Debug.WriteLine($"    [{i}] {BitConverter.ToString(clientReceived[i])} ({clientReceived[i].Length} bytes)");
            }
        }

        if (_serverConnection != null)
        {
            var serverStats = _serverConnection.GetStatistics();
            System.Diagnostics.Debug.WriteLine($"Server Connection:");
            System.Diagnostics.Debug.WriteLine($"  Status: {_serverConnection.Status}");
            System.Diagnostics.Debug.WriteLine($"  IsConnected: {_serverConnection.IsConnected}");
            System.Diagnostics.Debug.WriteLine($"  Data Sent: {serverStats.DataSentCount}");
            System.Diagnostics.Debug.WriteLine($"  Data Received: {serverStats.DataReceivedCount}");

            var serverSent = _serverConnection.GetAllSentData();
            System.Diagnostics.Debug.WriteLine($"  Sent Queue Count: {serverSent.Count}");
            for (int i = 0; i < serverSent.Count; i++)
            {
                System.Diagnostics.Debug.WriteLine($"    [{i}] {BitConverter.ToString(serverSent[i])} ({serverSent[i].Length} bytes)");
            }

            var serverReceived = _serverConnection.GetAllReceivedData();
            System.Diagnostics.Debug.WriteLine($"  Received Queue Count: {serverReceived.Count}");
            for (int i = 0; i < serverReceived.Count; i++)
            {
                System.Diagnostics.Debug.WriteLine($"    [{i}] {BitConverter.ToString(serverReceived[i])} ({serverReceived[i].Length} bytes)");
            }
        }

        System.Diagnostics.Debug.WriteLine($"=== End Connection State Log ===");
    }

    /// <summary>
    /// Waits for a response to be sent by the client connection with timeout and polling
    /// </summary>
    private async Task<List<byte[]>> WaitForResponseAsync(int timeoutMs = 2000, int minCount = 1)
    {
        var startTime = DateTime.UtcNow;

        // Poll until data is available in server's receive queue (what client sent)
        // Note: We check server's receive queue because that's where client responses arrive
        // The TestServerApp's background loop may consume them, so we need to check quickly
        while ((DateTime.UtcNow - startTime).TotalMilliseconds < timeoutMs)
        {
            // Check client's sent data history (what client sent as responses)
            // This uses a separate history queue that doesn't get consumed by receive loop
            var clientSentHistoryCount = _clientConnection!.GetSentDataHistoryCount();

            System.Diagnostics.Debug.WriteLine($"[WaitForResponseAsync] Polling: clientSentHistory={clientSentHistoryCount}, elapsed={(DateTime.UtcNow - startTime).TotalMilliseconds}ms");

            if (clientSentHistoryCount >= minCount)
            {
                System.Diagnostics.Debug.WriteLine($"[WaitForResponseAsync] Response detected after {(DateTime.UtcNow - startTime).TotalMilliseconds}ms (count: {clientSentHistoryCount})");
                // Get all data that client sent (responses)
                var responses = _clientConnection!.GetAllSentData();
                System.Diagnostics.Debug.WriteLine($"[WaitForResponseAsync] Retrieved {responses.Count} responses from client sent history");
                return responses;
            }

            await Task.Delay(10); // Poll at 10ms intervals
        }

        System.Diagnostics.Debug.WriteLine($"[WaitForResponseAsync] Timeout after {timeoutMs}ms - no response received");
        LogConnectionState("Timeout");
        return new List<byte[]>();
    }

    [Fact]
    public async Task TerminalSession_ShouldSendPrimaryDAResponse_WhenTestServerSendsQuery()
    {
        // Arrange
        var emulator = new TDV2200Emulator(80, 24);
        await SetupConnectionAsync(emulator);

        // Act - TestServer sends Primary DA query
        var query = TDVSequenceBuilder.BuildDAQuery();
        System.Diagnostics.Debug.WriteLine($"[Test] Sending Primary DA query: {BitConverter.ToString(query)}");
        await _telnetSession!.WriteBytesAsync(query);

        // Wait for response with polling
        var responses = await WaitForResponseAsync(2000);

        LogConnectionState("After query");

        // Assert - Response should be sent by client
        // Client sends response -> clientConnection._sendQueue (which is serverToClient)
        // The receive loop consumes it, so we check what client SENT instead
        Assert.NotEmpty(responses);

        // Check response bytes directly (not UTF-8 string conversion)
        var responseBytes = responses[responses.Count - 1];
        System.Diagnostics.Debug.WriteLine($"[Test] Response received: {BitConverter.ToString(responseBytes)} ({responseBytes.Length} bytes)");

        // Verify response format: should start with ESC [
        Assert.True(responseBytes.Length >= 2, $"Response too short: {responseBytes.Length} bytes");
        Assert.Equal(0x1B, responseBytes[0]); // ESC
        Assert.Equal(0x5B, responseBytes[1]); // [

        // Verify response contains 'c' (DA response terminator)
        var responseString = Encoding.UTF8.GetString(responseBytes);
        Assert.Contains("c", responseString);
    }

    [Fact]
    public async Task VerifyQueryRouting_ShouldDeliverQueryToEmulator()
    {
        // Arrange
        var emulator = new TDV2200Emulator(80, 24);
        bool dataReceivedFired = false;
        byte[]? receivedBytes = null;

        await SetupConnectionAsync(emulator);

        // Wire up event handler to track data received
        _clientConnection!.DataReceived += (data) =>
        {
            dataReceivedFired = true;
            receivedBytes = data.ToArray();
            System.Diagnostics.Debug.WriteLine($"[VerifyQueryRouting] DataReceived fired: {BitConverter.ToString(receivedBytes)}");
        };

        // Act - Send query via MockTelnetSession
        var query = TDVSequenceBuilder.BuildDAQuery();
        System.Diagnostics.Debug.WriteLine($"[VerifyQueryRouting] Sending query: {BitConverter.ToString(query)}");
        await _telnetSession!.WriteBytesAsync(query);

        // Wait for data to be received
        await Task.Delay(200, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(dataReceivedFired, "DataReceived event should have fired");
        Assert.NotNull(receivedBytes);
        Assert.Equal(query, receivedBytes);
    }

    [Fact]
    public async Task VerifyResponseGeneration_ShouldGenerateResponse()
    {
        // Arrange
        var emulator = new TDV2200Emulator(80, 24);
        string? capturedResponse = null;

        await SetupConnectionAsync(emulator);

        // Wire up event handler to capture response
        if (emulator is TDVEmulatorBase tdvEmulator)
        {
            tdvEmulator.OnResponseReady += (response) =>
            {
                capturedResponse = response;
                System.Diagnostics.Debug.WriteLine($"[VerifyResponseGeneration] OnResponseReady fired: {response} ({BitConverter.ToString(Encoding.UTF8.GetBytes(response))})");
            };
        }

        // Act - Directly call ProcessData with DA query bytes
        var query = TDVSequenceBuilder.BuildDAQuery();
        System.Diagnostics.Debug.WriteLine($"[VerifyResponseGeneration] Processing query: {BitConverter.ToString(query)}");
        emulator.ProcessData(query);

        // Wait for response to be generated
        await Task.Delay(200, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(capturedResponse);
        Assert.StartsWith("\x1b[", capturedResponse);
        Assert.Contains("c", capturedResponse);
    }

    [Fact]
    public async Task TerminalSession_ShouldSendSecondaryDAResponse_WhenTestServerSendsQuery()
    {
        // Arrange
        var emulator = new TDV2200Emulator(80, 24);
        await SetupConnectionAsync(emulator);

        // Act - TestServer sends Secondary DA query
        var query = TDVSequenceBuilder.BuildSecondaryDAQuery();
        await _telnetSession!.WriteBytesAsync(query);

        // Wait for the reply itself, not for a number of milliseconds: on the hosted GitHub
        // runner 300 ms was not always enough and the tag build went red.
        var responses = await WaitForResponseAsync(2000);

        // Assert - Check what client sent (response)
        Assert.NotEmpty(responses);

        var response = Encoding.UTF8.GetString(responses[responses.Count - 1]);
        Assert.StartsWith("\x1b[>", response);
        Assert.Contains("220", response); // TDV2200 firmware ID
        Assert.EndsWith("c", response);
    }

    [Fact]
    public async Task TerminalSession_ShouldSendCPRResponse_WhenTestServerSendsQuery()
    {
        // Arrange
        var emulator = new TDV2200Emulator(80, 24);
        await SetupConnectionAsync(emulator);

        // Move cursor first
        var moveCursor = new byte[] { 0x1B, 0x5B, 0x35, 0x3B, 0x31, 0x30, 0x48 };
        await _telnetSession!.WriteBytesAsync(moveCursor);
        await Task.Delay(100, TestContext.Current.CancellationToken);
        _clientConnection!.GetAllSentData(); // Clear sent data

        // Act - TestServer sends CPR query
        var query = TDVSequenceBuilder.BuildCPRQuery();
        await _telnetSession!.WriteBytesAsync(query);

        // Wait for response with polling
        var responses = await WaitForResponseAsync(2000);

        // Assert - Check what client sent (response)
        Assert.NotEmpty(responses);

        var response = Encoding.UTF8.GetString(responses[responses.Count - 1]);
        Assert.StartsWith("\x1b[", response);
        Assert.EndsWith("R", response);
        Assert.Contains("5", response);
        Assert.Contains("10", response);
    }

    [Fact]
    public async Task TerminalSession_ShouldSendDSRResponse_WhenTestServerSendsQuery()
    {
        // Arrange
        var emulator = new TDV2200Emulator(80, 24);
        await SetupConnectionAsync(emulator);

        // Act - TestServer sends DSR query
        var query = TDVSequenceBuilder.BuildDSRQuery();
        await _telnetSession!.WriteBytesAsync(query);

        // Wait for response with polling
        var responses = await WaitForResponseAsync(2000);

        // Assert - Check what client sent (response)
        Assert.NotEmpty(responses);

        var response = Encoding.UTF8.GetString(responses[responses.Count - 1]);
        Assert.StartsWith("\x1b[", response);
        Assert.EndsWith("n", response);
    }

    [Fact]
    public async Task TerminalSession_ShouldSendTerminalIDResponse_WhenTestServerSendsQuery()
    {
        // Arrange
        var emulator = new TDV2200Emulator(80, 24);
        await SetupConnectionAsync(emulator);

        // Act - TestServer sends Terminal ID query
        var query = TDVSequenceBuilder.BuildTerminalIDQuery();
        await _telnetSession!.WriteBytesAsync(query);

        // Wait for response with polling
        var responses = await WaitForResponseAsync(2000);

        // Assert - Check what client sent (response)
        Assert.NotEmpty(responses);

        var response = Encoding.UTF8.GetString(responses[responses.Count - 1]);
        Assert.NotEmpty(response);
    }

    [Fact]
    public async Task TerminalSession_ShouldSendMultipleResponses_WhenTestServerSendsMultipleQueries()
    {
        // Arrange
        var emulator = new TDV2200Emulator(80, 24);
        await SetupConnectionAsync(emulator);

        // Act - Send multiple queries
        var daQuery = TDVSequenceBuilder.BuildDAQuery();
        await _telnetSession!.WriteBytesAsync(daQuery);

        var cprQuery = TDVSequenceBuilder.BuildCPRQuery();
        await _telnetSession!.WriteBytesAsync(cprQuery);

        // Both queries went down one TCP stream in order, so the replies come back in order;
        // wait until both are there instead of hoping the clock was generous enough.
        var responses = await WaitForResponseAsync(2000, 2);

        // Assert - Check what client sent (responses)
        Assert.True(responses.Count >= 2, $"Expected at least 2 responses, got {responses.Count}");

        // First response should be DA
        var firstResponse = Encoding.UTF8.GetString(responses[responses.Count - 2]);
        Assert.StartsWith("\x1b[", firstResponse);

        // Second response should be CPR
        var secondResponse = Encoding.UTF8.GetString(responses[responses.Count - 1]);
        Assert.StartsWith("\x1b[", secondResponse);
        Assert.EndsWith("R", secondResponse);
    }

    [Fact]
    public async Task TerminalSession_ShouldSendTDV1200SecondaryDA_WithCorrectFirmwareID()
    {
        // Arrange
        var emulator = new TDV1200Emulator(80, 24);
        await SetupConnectionAsync(emulator);

        // Act
        var query = TDVSequenceBuilder.BuildSecondaryDAQuery();
        await _telnetSession!.WriteBytesAsync(query);

        // Wait for response with polling
        var responses = await WaitForResponseAsync(2000);

        // Assert - Check what client sent (response)
        Assert.NotEmpty(responses);

        var response = Encoding.UTF8.GetString(responses[responses.Count - 1]);
        Assert.Contains("120", response); // TDV1200 firmware ID
    }

    [Fact]
    public async Task TerminalSession_ShouldSendTDV2215SecondaryDA_WithCorrectFirmwareID()
    {
        // Arrange
        var emulator = new TDV2215Emulator(80, 24);
        await SetupConnectionAsync(emulator);

        // Act
        var query = TDVSequenceBuilder.BuildSecondaryDAQuery();
        await _telnetSession!.WriteBytesAsync(query);

        // Wait for response with polling
        var responses = await WaitForResponseAsync(2000);

        // Assert - Check what client sent (response)
        Assert.NotEmpty(responses);

        var response = Encoding.UTF8.GetString(responses[responses.Count - 1]);
        Assert.Contains("115", response); // TDV2215 firmware ID
    }
}

/// <summary>
/// Mock TelnetSession that uses in-memory connection instead of TCP
/// Extends TelnetSession but overrides Stream to use in-memory connection
/// </summary>
internal class MockTelnetSession : TelnetSession
{
    private readonly InMemoryBidirectionalConnection _connection;
    private readonly MockNetworkStream _mockStream;
    private readonly TelnetNegotiator _negotiator;
    private static readonly System.Collections.Generic.List<System.Net.Sockets.TcpClient> _keepAliveServers = new();

    public override System.Net.Sockets.NetworkStream Stream => _mockStream;
    public override TelnetNegotiator Negotiator => _negotiator;

    public MockTelnetSession(InMemoryBidirectionalConnection connection)
        : base(CreateConnectedTcpClient())
    {
        _connection = connection;
        _mockStream = new MockNetworkStream(connection);
        _negotiator = new TelnetNegotiator(_mockStream);
    }

    private static System.Net.Sockets.TcpClient CreateConnectedTcpClient()
    {
        var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
            var client = new System.Net.Sockets.TcpClient();
            var acceptTask = Task.Run(() => listener.AcceptTcpClient());
            client.Connect(System.Net.IPAddress.Loopback, port);
            var server = acceptTask.GetAwaiter().GetResult();
            _keepAliveServers.Add(server);
            return client;
        }
        finally
        {
            // Keep listener alive for the duration of tests
        }
    }

    public new async Task WriteBytesAsync(byte[] bytes)
    {
        await _connection.SendAsync(bytes);
    }

    public new async Task WriteBytesAsync(ReadOnlyMemory<byte> bytes)
    {
        await _connection.SendAsync(bytes);
    }

    public new async Task WriteAsync(string text)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(text);
        await _connection.SendAsync(bytes);
    }

    protected internal override async Task<int> ReadRawAsync(byte[] buffer, int timeoutMs = 200)
    {
        // Pre-pump reads: block until data or timeout — don't return 0 on empty
        var startTime = DateTime.UtcNow;
        while ((DateTime.UtcNow - startTime).TotalMilliseconds < timeoutMs)
        {
            if (_connection.GetReceivedDataCount() > 0)
            {
                var data = _connection.DequeueReceivedData();
                if (data != null)
                {
                    int len = Math.Min(data.Length, buffer.Length);
                    Array.Copy(data, 0, buffer, 0, len);
                    return len;
                }
            }
            await Task.Delay(10);
        }
        return 0; // Timeout
    }
}

/// <summary>
/// Mock NetworkStream that uses in-memory connection.
/// Used by the pump loop — must block until data available or cancellation.
/// </summary>
internal class MockNetworkStream : System.Net.Sockets.NetworkStream
{
    private readonly InMemoryBidirectionalConnection _connection;
    private static System.Net.Sockets.Socket? _dummySocket;

    public MockNetworkStream(InMemoryBidirectionalConnection connection)
        : base(CreateDummySocket(), false)
    {
        _connection = connection;
    }

    private static System.Net.Sockets.Socket CreateDummySocket()
    {
        if (_dummySocket != null && _dummySocket.Connected) return _dummySocket;

        var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
            var client = new System.Net.Sockets.TcpClient();
            var acceptTask = Task.Run(() => listener.AcceptTcpClient());
            client.Connect(System.Net.IPAddress.Loopback, port);
            var server = acceptTask.GetAwaiter().GetResult();
            _ = Task.Run(async () =>
            {
                try { await Task.Delay(TimeSpan.FromHours(1)); }
                catch { }
                finally { server?.Close(); }
            });
            _dummySocket = client.Client;
            return _dummySocket;
        }
        finally
        {
            listener.Stop();
        }
    }

    public override bool DataAvailable => _connection.GetReceivedDataCount() > 0;

    public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        // Block until data available or cancellation — never return 0 on timeout
        // (pump interprets 0 as connection closed)
        while (!cancellationToken.IsCancellationRequested)
        {
            if (_connection.GetReceivedDataCount() > 0)
            {
                var data = _connection.DequeueReceivedData();
                if (data != null)
                {
                    int len = Math.Min(data.Length, count);
                    Array.Copy(data, 0, buffer, offset, len);
                    return len;
                }
            }
            await Task.Delay(10, cancellationToken);
        }
        throw new OperationCanceledException(cancellationToken);
    }

    public override async Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        var data = new byte[count];
        Array.Copy(buffer, offset, data, 0, count);
        await _connection.SendAsync(data, cancellationToken);
    }

    public override async Task FlushAsync(CancellationToken cancellationToken)
    {
        await Task.CompletedTask;
    }
}

