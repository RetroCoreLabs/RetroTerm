using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
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
/// Comprehensive tests that exercise ALL TestServerApp test methods through in-memory connection
/// These tests verify the FULL host query/response flow:
/// 1. TestServerApp sends queries via its test methods
/// 2. TerminalSession receives queries and routes to emulator
/// 3. Emulator generates responses
/// 4. TerminalSession sends responses back
/// 5. TestServerApp captures and validates responses
/// </summary>
public class TestServerAppFullIntegrationTests : IDisposable
{
    private InMemoryBidirectionalConnection? _clientConnection;
    private InMemoryBidirectionalConnection? _serverConnection;
    private TerminalSession? _session;
    private TestServerApp? _testServerApp;
    private MockTelnetSessionForQueryTests? _telnetSession;

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

        // THE SERVER SIDE MUST NOT RUN A RECEIVE LOOP, and nothing was telling it so.
        //
        // DisableReceiveLoop has said in its own comment all along that server-side connections
        // have to call it, because the mock session reads with ReadRawAsync instead. No caller
        // ever did. So the loop ran, dequeued everything the client sent, and raised it as an
        // event nobody was listening to - and the mock's ReadRawAsync then found an empty queue.
        //
        // That is why the server never saw the terminal-type answer, sat in its 30-second manual
        // prompt, and never reached StartInputPump. All sixteen tests reported "input pump is not
        // running", which is three steps downstream of this line.
        _serverConnection.DisableReceiveLoop();

        // Connect both connections
        await _clientConnection.ConnectAsync();
        await _serverConnection.ConnectAsync();

        // Create TerminalSession with client connection
        _session = new TerminalSession(emulator, "Test");
        await _session.ConnectAsync(_clientConnection);

        // Create TestServerApp with server connection
        _testServerApp = new TestServerApp();

        // Create a mock TelnetSession that uses our in-memory connection
        var mockSession = new MockTelnetSessionForQueryTests(_serverConnection);
        _telnetSession = mockSession;

        // START THE PUMP AND NOTHING ELSE. Do NOT run OnConnectedAsync here.
        //
        // This class exists to drive TestServerApp's own test routines one at a time, and each of
        // those captures the terminal's reply with CaptureResponseWithPollingAsync - which reads
        // the input pump and says in its own comment that it must be the SOLE reader.
        // OnConnectedAsync runs the menu loop, and the menu loop reads the same pump, so having it
        // running here put two readers on a channel built with SingleReader = true. The tests died
        // on a closed channel, and before that they never got as far as the pump at all, because
        // OnConnectedAsync spends its first seconds on a welcome banner and a terminal detection
        // that cannot succeed over an in-memory pair - ending in a THIRTY-SECOND prompt for a
        // terminal type nobody was there to answer.
        //
        // Starting the pump directly is both simpler and truer to what is being tested: the
        // routine under test is the only thing talking. The terminal type it needs is set by
        // reflection just below, which is what OnConnectedAsync's detection would have worked out.
        mockSession.StartInputPump();

        Assert.True(mockSession.IsInputPumpRunning,
            "the mock session's input pump did not start");

        // Manually set terminal type to avoid waiting for detection
        SetTerminalTypeViaReflection(emulator);
    }

    /// <summary>
    /// Sets the terminal type in TestServerApp via reflection to avoid detection delay.
    /// </summary>
    /// <param name="emulator">
    /// The emulator whose type the server should believe it is talking to.
    /// </param>
    private void SetTerminalTypeViaReflection(TerminalEmulatorBase emulator)
    {
        var terminalType = emulator switch
        {
            TDV2200Emulator => TerminalType.TDV2200,
            TDV1200Emulator => TerminalType.TDV1200,
            TDV2215Emulator => TerminalType.TDV2215,
            _ => TerminalType.Unknown
        };

        var fieldInfo = typeof(TestServerApp).GetField("_detectedTerminalType",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        if (fieldInfo != null && _testServerApp != null)
        {
            fieldInfo.SetValue(_testServerApp, terminalType);

            // Also initialize capability checker
            var capabilityCheckerField = typeof(TestServerApp).GetField("_capabilityChecker",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (capabilityCheckerField != null)
            {
                capabilityCheckerField.SetValue(_testServerApp, new TDVCapabilityChecker(terminalType));
            }
        }
    }

    /// <summary>
    /// Calls a TestServerApp test method via reflection and verifies responses are received
    /// </summary>
    private async Task<bool> RunTestServerAppMethodAsync(string methodName, int timeoutMs = 10000)
    {
        var methodInfo = typeof(TestServerApp).GetMethod(methodName,
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        if (methodInfo == null)
        {
            System.Diagnostics.Debug.WriteLine($"[RunTestServerAppMethodAsync] Method {methodName} not found");
            return false;
        }

        // Run the test method
        var task = (Task)methodInfo.Invoke(_testServerApp, new object[] { _telnetSession! })!;

        // KEEP PRESSING ENTER. Every one of these routines ends with "Press Enter to continue"
        // and then WaitForEnterAsync, so with nobody at the keyboard they never return - which is
        // what the fifteen-second timeout here was actually measuring. One of the sixteen skip
        // notes even said so: "Interactive test - calls WaitForEnterAsync which blocks waiting for
        // user input". It was true of all of them.
        //
        // An Enter that arrives early is harmless: the capture loop reads and discards anything
        // that is not an escape sequence.
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (!task.IsCompleted && DateTime.UtcNow < deadline)
        {
            await _session!.SendInputAsync("\r");
            await Task.WhenAny(task, Task.Delay(250));
        }

        if (task.IsCompleted)
        {
            await task; // Re-await to get exceptions
            return true;
        }

        System.Console.WriteLine($"[RunTestServerAppMethodAsync] Method {methodName} timed out after {timeoutMs}ms");
        return false;
    }

    /// <summary>
    /// Verifies that at least one response was received during test execution
    /// </summary>
    private void VerifyResponsesReceived()
    {
        var responses = _clientConnection!.GetAllSentData();
        Assert.True(responses.Count > 0, "At least one response should have been sent by client during test");
    }

    [Fact]
    public async Task TestServerApp_RunTDV_QueryResponseTestsAsync_ShouldReceiveAllResponses()
    {
        // Arrange
        var emulator = new TDV2200Emulator(80, 24);
        await SetupConnectionAsync(emulator);

        // Track responses as they're sent
        var responseCount = 0;
        if (emulator is TDVEmulatorBase tdvEmulator)
        {
            tdvEmulator.OnResponseReady += (response) =>
            {
                responseCount++;
                System.Diagnostics.Debug.WriteLine($"[Test] Response #{responseCount} generated: {BitConverter.ToString(Encoding.UTF8.GetBytes(response))}");
            };
        }

        // Clear any initial responses
        _clientConnection!.GetAllSentData();

        // Act - Run TestServerApp's query/response test method
        var success = await RunTestServerAppMethodAsync("RunTDV_QueryResponseTestsAsync", timeoutMs: 15000);

        // Assert
        Assert.True(success, "RunTDV_QueryResponseTestsAsync should complete successfully");

        // Log connection state for debugging
        System.Diagnostics.Debug.WriteLine($"[Test] Response count from OnResponseReady: {responseCount}");
        System.Diagnostics.Debug.WriteLine($"[Test] Server connection received data count: {_serverConnection!.GetReceivedDataCount()}");
        System.Diagnostics.Debug.WriteLine($"[Test] Client connection sent data count: {_clientConnection.GetSentDataHistoryCount()}");

        // FETCH ONCE. GetAllSentData DEQUEUES, so calling it twice empties the history and the
        // second caller sees nothing - which is exactly what happened here: VerifyResponsesReceived
        // took the whole list and the assertion below then failed with "got 0" on a run where
        // every single query had in fact been answered.
        var responses = _clientConnection.GetAllSentData();

        Assert.True(responses.Count > 0,
            "At least one response should have been sent by the client during the test");

        // Primary DA, Secondary DA, CPR, DSR, Terminal ID, DECRQM.
        Assert.True(responses.Count >= 6, $"Expected at least 6 responses (got {responses.Count})");
    }

    [Fact]
    public async Task TestServerApp_RunTDV_CharacterSetsTestsAsync_ShouldWork()
    {
        // Arrange
        var emulator = new TDV2200Emulator(80, 24);
        await SetupConnectionAsync(emulator);

        _clientConnection!.GetAllSentData();

        // Act
        var success = await RunTestServerAppMethodAsync("RunTDV_CharacterSetsTestsAsync", timeoutMs: 10000);

        // Assert
        Assert.True(success, "RunTDV_CharacterSetsTestsAsync should complete successfully");
    }

    [Fact]
    public async Task TestServerApp_RunTDV_DrawingOperationsTestsAsync_ShouldWork()
    {
        // Arrange
        var emulator = new TDV2200Emulator(80, 24);
        await SetupConnectionAsync(emulator);

        _clientConnection!.GetAllSentData();

        // Act
        var success = await RunTestServerAppMethodAsync("RunTDV_DrawingOperationsTestsAsync", timeoutMs: 10000);

        // Assert
        Assert.True(success, "RunTDV_DrawingOperationsTestsAsync should complete successfully");
    }

    [Fact]
    public async Task TestServerApp_RunTDV_FunctionKeysTestsAsync_ShouldWork()
    {
        // Arrange
        var emulator = new TDV2200Emulator(80, 24);
        await SetupConnectionAsync(emulator);

        _clientConnection!.GetAllSentData();

        // Act
        var success = await RunTestServerAppMethodAsync("RunTDV_FunctionKeysTestsAsync", timeoutMs: 10000);

        // Assert
        Assert.True(success, "RunTDV_FunctionKeysTestsAsync should complete successfully");
    }

    [Fact]
    public async Task TestServerApp_RunTDV_ModesFeaturesTestsAsync_ShouldWork()
    {
        // Arrange
        var emulator = new TDV2200Emulator(80, 24);
        await SetupConnectionAsync(emulator);

        _clientConnection!.GetAllSentData();

        // Act
        var success = await RunTestServerAppMethodAsync("RunTDV_ModesFeaturesTestsAsync", timeoutMs: 10000);

        // Assert
        Assert.True(success, "RunTDV_ModesFeaturesTestsAsync should complete successfully");
    }

    [Fact]
    public async Task TestServerApp_RunTDV_ComprehensiveDemoAsync_ShouldWork()
    {
        // Arrange
        var emulator = new TDV2200Emulator(80, 24);
        await SetupConnectionAsync(emulator);

        _clientConnection!.GetAllSentData();

        // Act
        var success = await RunTestServerAppMethodAsync("RunTDV_ComprehensiveDemoAsync", timeoutMs: 20000);

        // Assert
        Assert.True(success, "RunTDV_ComprehensiveDemoAsync should complete successfully");
    }

    [Fact]
    public async Task TestServerApp_RunTDV1200_2115CompatibilityAsync_ShouldWork()
    {
        // Arrange
        var emulator = new TDV1200Emulator(80, 24);
        await SetupConnectionAsync(emulator);

        _clientConnection!.GetAllSentData();

        // Act
        var success = await RunTestServerAppMethodAsync("RunTDV1200_2115CompatibilityAsync", timeoutMs: 10000);

        // Assert
        Assert.True(success, "RunTDV1200_2115CompatibilityAsync should complete successfully");
    }

    [Fact]
    public async Task TestServerApp_RunTDV1200_NDGraphicsAsync_ShouldWork()
    {
        // Arrange
        var emulator = new TDV1200Emulator(80, 24);
        await SetupConnectionAsync(emulator);

        _clientConnection!.GetAllSentData();

        // Act
        var success = await RunTestServerAppMethodAsync("RunTDV1200_NDGraphicsAsync", timeoutMs: 10000);

        // Assert
        Assert.True(success, "RunTDV1200_NDGraphicsAsync should complete successfully");
    }

    [Fact]
    public async Task TestServerApp_RunTDV1200_ProtectedAreasAsync_ShouldWork()
    {
        // Arrange
        var emulator = new TDV1200Emulator(80, 24);
        await SetupConnectionAsync(emulator);

        _clientConnection!.GetAllSentData();

        // Act
        var success = await RunTestServerAppMethodAsync("RunTDV1200_ProtectedAreasAsync", timeoutMs: 10000);

        // Assert
        Assert.True(success, "RunTDV1200_ProtectedAreasAsync should complete successfully");
    }

    [Fact]
    public async Task TestServerApp_RunTDV1200_CharacterSetsAsync_ShouldWork()
    {
        // Arrange
        var emulator = new TDV1200Emulator(80, 24);
        await SetupConnectionAsync(emulator);

        _clientConnection!.GetAllSentData();

        // Act
        var success = await RunTestServerAppMethodAsync("RunTDV1200_CharacterSetsAsync", timeoutMs: 10000);

        // Assert
        Assert.True(success, "RunTDV1200_CharacterSetsAsync should complete successfully");
    }

    [Fact]
    public async Task TestServerApp_RunTDV2215_ExtendedModeAsync_ShouldWork()
    {
        // Arrange
        var emulator = new TDV2215Emulator(80, 24);
        await SetupConnectionAsync(emulator);

        _clientConnection!.GetAllSentData();

        // Act
        var success = await RunTestServerAppMethodAsync("RunTDV2215_ExtendedModeAsync", timeoutMs: 10000);

        // Assert
        Assert.True(success, "RunTDV2215_ExtendedModeAsync should complete successfully");
    }

    [Fact]
    public async Task TestServerApp_RunTDV2215_TransparentModeAsync_ShouldWork()
    {
        // Arrange
        var emulator = new TDV2215Emulator(80, 24);
        await SetupConnectionAsync(emulator);

        _clientConnection!.GetAllSentData();

        // Act
        var success = await RunTestServerAppMethodAsync("RunTDV2215_TransparentModeAsync", timeoutMs: 10000);

        // Assert
        Assert.True(success, "RunTDV2215_TransparentModeAsync should complete successfully");
    }

    [Fact]
    public async Task TestServerApp_RunTDV2215_DCSSequencesAsync_ShouldWork()
    {
        // Arrange
        var emulator = new TDV2215Emulator(80, 24);
        await SetupConnectionAsync(emulator);

        _clientConnection!.GetAllSentData();

        // Act
        var success = await RunTestServerAppMethodAsync("RunTDV2215_DCSSequencesAsync", timeoutMs: 10000);

        // Assert
        Assert.True(success, "RunTDV2215_DCSSequencesAsync should complete successfully");
    }

    [Fact]
    public async Task TestServerApp_RunTDV2200_GraphicsExtensionAsync_ShouldWork()
    {
        // Arrange
        var emulator = new TDV2200Emulator(80, 24);
        await SetupConnectionAsync(emulator);

        _clientConnection!.GetAllSentData();

        // Act
        var success = await RunTestServerAppMethodAsync("RunTDV2200_GraphicsExtensionAsync", timeoutMs: 10000);

        // Assert
        Assert.True(success, "RunTDV2200_GraphicsExtensionAsync should complete successfully");
    }

    [Fact]
    public async Task TestServerApp_RunTDV2200_TektronixModeAsync_ShouldWork()
    {
        // Arrange
        var emulator = new TDV2200Emulator(80, 24);
        await SetupConnectionAsync(emulator);

        _clientConnection!.GetAllSentData();

        // Act
        var success = await RunTestServerAppMethodAsync("RunTDV2200_TektronixModeAsync", timeoutMs: 10000);

        // Assert
        Assert.True(success, "RunTDV2200_TektronixModeAsync should complete successfully");
    }

    [Fact]
    public async Task TestServerApp_RunTDV2200_ISO646VariantsAsync_ShouldWork()
    {
        // Arrange
        var emulator = new TDV2200Emulator(80, 24);
        await SetupConnectionAsync(emulator);

        _clientConnection!.GetAllSentData();

        // Act
        var success = await RunTestServerAppMethodAsync("RunTDV2200_ISO646VariantsAsync", timeoutMs: 10000);

        // Assert
        Assert.True(success, "RunTDV2200_ISO646VariantsAsync should complete successfully");
    }
}

/// <summary>
/// Mock TelnetSession that uses in-memory connection for TestServerApp query/response tests
/// </summary>
internal class MockTelnetSessionForQueryTests : TelnetSession
{
    private readonly InMemoryBidirectionalConnection _connection;
    private readonly MockNetworkStreamForQueryTests _mockStream;
    private readonly TelnetNegotiator _negotiator;
    private static readonly System.Collections.Generic.List<System.Net.Sockets.TcpClient> _keepAliveServers = new();

    public override System.Net.Sockets.NetworkStream Stream => _mockStream;
    public override TelnetNegotiator Negotiator => _negotiator;

    public MockTelnetSessionForQueryTests(InMemoryBidirectionalConnection connection)
        : base(CreateConnectedTcpClient())
    {
        _connection = connection;
        _mockStream = new MockNetworkStreamForQueryTests(connection);
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
            // Keep listener alive
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
        // Pre-pump reads: block until data or timeout
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
/// Mock NetworkStream for TestServerApp query/response tests.
/// Used by the pump loop — must block until data available or cancellation.
/// </summary>
internal class MockNetworkStreamForQueryTests : System.Net.Sockets.NetworkStream
{
    private readonly InMemoryBidirectionalConnection _connection;
    private static System.Net.Sockets.Socket? _dummySocket;

    public MockNetworkStreamForQueryTests(InMemoryBidirectionalConnection connection)
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

    public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, System.Threading.CancellationToken cancellationToken)
    {
        // Block until data available or cancellation — never return 0 on timeout
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

    public override async Task WriteAsync(byte[] buffer, int offset, int count, System.Threading.CancellationToken cancellationToken)
    {
        var data = new byte[count];
        Array.Copy(buffer, offset, data, 0, count);
        await _connection.SendAsync(data, cancellationToken);
    }

    /// <summary>
    /// The memory-based write, and WITHOUT THIS the mock delivers nothing.
    /// </summary>
    /// <remarks>
    /// <para><b>The overload above was never called</b></para>
    /// <c>TelnetSession.WriteAsync</c> does <c>Stream.WriteAsync(bytes)</c> with a byte array.
    /// That does NOT bind to <c>WriteAsync(byte[], int, int, CancellationToken)</c>, which needs
    /// four arguments - it binds to <c>WriteAsync(ReadOnlyMemory&lt;byte&gt;, CancellationToken)</c>,
    /// which needs one, because a byte array converts to a read-only memory implicitly.
    ///
    /// So every byte the server wrote went to the base NetworkStream and out of the DUMMY socket
    /// this mock is constructed over, and the client saw nothing at all. Sixteen tests failed on
    /// it, reporting "input pump is not running" - which is true, but is three steps downstream:
    /// with no server output there is nothing to auto-detect from, so the server sat in its manual
    /// terminal-type prompt, and the pump is started after that.
    ///
    /// The read side was fine only by luck: the pump happens to call the four-argument read.
    ///
    /// Measured 10 September 2026 by dumping the client's screen, which was empty.
    /// </remarks>
    /// <param name="buffer">
    /// What to write.
    /// </param>
    /// <param name="cancellationToken">
    /// Cancellation for the send.
    /// </param>
    /// <returns>
    /// A task that completes when the bytes have been handed to the connection.
    /// </returns>
    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, System.Threading.CancellationToken cancellationToken = default)
    {
        await _connection.SendAsync(buffer.ToArray(), cancellationToken);
    }

    public override async Task FlushAsync(System.Threading.CancellationToken cancellationToken)
    {
        await Task.CompletedTask;
    }
}

