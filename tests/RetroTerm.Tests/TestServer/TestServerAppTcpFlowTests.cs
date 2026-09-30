using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using RetroTerm.Core.Protocols;
using RetroTerm.Core.Protocols.Net;
using RetroTerm.Core.Protocols.TelnetServer.Telnet;
using RetroTerm.Core.Protocols.TelnetServer.Utilities;
using RetroTerm.Core.Session;
using RetroTerm.Core.Terminal.Emulators.TDV;
using RetroTerm.TestServer.App;
using Xunit;

namespace RetroTerm.Tests.TestServer;

/// <summary>
/// Tests that simulate the real TCP flow with main loop active
/// These tests catch issues that only appear when the TestServer's main loop is running
/// </summary>
public class TestServerAppTcpFlowTests : IDisposable
{
    private TcpListener? _server;
    private TcpClient? _serverClient;
    private TerminalSession? _session;
    private IConnection? _connection;
    private TDV2200Emulator? _emulator;
    private TelnetSession? _serverSession;
    private TestServerApp? _testServerApp;
    private Task? _mainLoopTask;
    private int _serverPort = 0;

    /// <summary>
    /// Closes the client end first, which is what lets the server's menu loop finish.
    /// </summary>
    /// <remarks>
    /// <para><b>The task is not disposed here, and that is deliberate</b></para>
    /// This used to start with <c>_mainLoopTask?.Dispose()</c> on a task that was still running,
    /// which throws "a task may only be disposed if it is in a completion state" - the second
    /// error inside the aggregate these tests used to fail with. A Task holds nothing that needs
    /// disposing; letting it be collected is the normal thing to do.
    ///
    /// The order matters too. Closing the client's side of the connection ends the server's read,
    /// so the loop can leave on its own rather than being cancelled out of one.
    /// </remarks>
    public void Dispose()
    {
        _session?.Dispose();
        _connection?.Dispose();
        _serverClient?.Close();
        _server?.Stop();
    }

    /// <summary>
    /// Sets up a real TCP connection with TestServerApp main loop running
    /// </summary>
    private async Task SetupTcpFlowAsync()
    {
        // Start TCP server (like TestServer)
        _server = new TcpListener(IPAddress.Loopback, 0);
        _server.Start();
        _serverPort = ((IPEndPoint)_server.LocalEndpoint).Port;

        // Create emulator
        _emulator = new TDV2200Emulator(80, 24);

        // Create TerminalSession
        _session = new TerminalSession(_emulator, "RetroTerm");

        // Use ConnectionFactory to create connection
        var parameters = new ConnectionFactory.ConnectionParameters
        {
            Protocol = ConnectionFactory.ProtocolType.Telnet,
            Host = "127.0.0.1",
            Port = _serverPort,
            EmulatorType = "TDV2200"
        };

        _connection = ConnectionFactory.CreateConnection(parameters);

        // Connect session to connection
        await _session.ConnectAsync(_connection);

        // Accept server connection
        _serverClient = await _server.AcceptTcpClientAsync();
        _serverSession = new TelnetSession(_serverClient);

        // Send Telnet negotiation
        await _serverSession.Negotiator.SendInitialAsync();

        // Wait for negotiation
        await Task.Delay(500);

        // Create TestServerApp and start main loop in background
        _testServerApp = new TestServerApp();
        _mainLoopTask = Task.Run(async () =>
        {
            try
            {
                await _testServerApp.OnConnectedAsync(_serverSession);
            }
            catch (Exception ex)
            {
                System.Console.WriteLine($"[TestServerAppTcpFlowTests] Main loop exception: {ex}");
            }
        });

        // Wait for main loop to initialize
        await Task.Delay(300);
    }

    /// <summary>
    /// Helper to send a query and capture response using TestServerApp's capture method
    /// </summary>
    private async Task<string?> SendQueryAndCaptureResponseAsync(byte[] query)
    {
        await _serverSession!.WriteBytesAsync(query);
        await _serverSession.FlushAsync();
        await Task.Delay(50); // Allow TCP to deliver query

        // ASK THE MENU LOOP WHAT IT SAW, rather than reading the pump behind its back.
        //
        // This used to call CaptureResponseWithPollingAsync on the same session while the loop was
        // running. The pump has one reader: the loop read the terminal's answer first and threw it
        // away, the capture then found nothing, and its timeout cancelled the read the LOOP was
        // sitting in - which ended the session and left every later test with a closed channel.
        // All seven tests in this class failed that way, and the skip note blamed timing.
        return await _testServerApp!.WaitForEscapeSequenceAsync(1000);
    }

    [Fact]
    public async Task TcpFlow_PrimaryDA_ShouldReceiveResponse()
    {
        // Arrange
        await SetupTcpFlowAsync();
        var query = TDVSequenceBuilder.BuildDAQuery();

        // Act
        var response = await SendQueryAndCaptureResponseAsync(query);

        // Assert
        Assert.NotNull(response);
        var daResponse = TDVResponseValidator.ParseDAResponse(response);
        Assert.True(daResponse.IsValid, $"Invalid DA response: {TDVResponseValidator.ToVisibleString(response)}");
    }

    [Fact]
    public async Task TcpFlow_SecondaryDA_ShouldReceiveResponse()
    {
        // Arrange
        await SetupTcpFlowAsync();
        var query = TDVSequenceBuilder.BuildSecondaryDAQuery();

        // Act
        var response = await SendQueryAndCaptureResponseAsync(query);

        // Assert
        Assert.NotNull(response);
        var daResponse = TDVResponseValidator.ParseDAResponse(response);
        Assert.True(daResponse.IsValid && daResponse.IsSecondary,
            $"Invalid Secondary DA response: {TDVResponseValidator.ToVisibleString(response)}");
    }

    [Fact]
    public async Task TcpFlow_CPR_ShouldReceiveResponse()
    {
        // Arrange
        await SetupTcpFlowAsync();
        var query = TDVSequenceBuilder.BuildCPRQuery();

        // Act
        var response = await SendQueryAndCaptureResponseAsync(query);

        // Assert
        Assert.NotNull(response);
        var cprResponse = TDVResponseValidator.ParseCPRResponse(response);
        Assert.True(cprResponse.IsValid, $"Invalid CPR response: {TDVResponseValidator.ToVisibleString(response)}");
    }

    [Fact]
    public async Task TcpFlow_DSR_ShouldReceiveResponse()
    {
        // Arrange
        await SetupTcpFlowAsync();
        var query = TDVSequenceBuilder.BuildDSRQuery();

        // Act
        var response = await SendQueryAndCaptureResponseAsync(query);

        // Assert
        Assert.NotNull(response);
        var dsrResponse = TDVResponseValidator.ParseDSRResponse(response);
        Assert.True(dsrResponse.IsValid, $"Invalid DSR response: {TDVResponseValidator.ToVisibleString(response)}");
    }

    [Fact]
    public async Task TcpFlow_TerminalID_ShouldReceiveResponse()
    {
        // Arrange
        await SetupTcpFlowAsync();
        var query = TDVSequenceBuilder.BuildTerminalIDQuery();

        // Act
        var response = await SendQueryAndCaptureResponseAsync(query);

        // Assert
        Assert.NotNull(response);
        var terminalIDResponse = TDVResponseValidator.ParseTerminalIDResponse(response);
        Assert.True(terminalIDResponse.IsValid,
            $"Invalid Terminal ID response: {TDVResponseValidator.ToVisibleString(response)}");
    }

    [Fact]
    public async Task TcpFlow_MultipleQueries_ShouldReceiveAllResponses()
    {
        // Arrange
        await SetupTcpFlowAsync();

        // Act & Assert - Send multiple queries sequentially
        var daQuery = TDVSequenceBuilder.BuildDAQuery();
        var response1 = await SendQueryAndCaptureResponseAsync(daQuery);
        Assert.NotNull(response1);

        await Task.Delay(100, TestContext.Current.CancellationToken); // Small delay between queries

        var cprQuery = TDVSequenceBuilder.BuildCPRQuery();
        var response2 = await SendQueryAndCaptureResponseAsync(cprQuery);
        Assert.NotNull(response2);

        await Task.Delay(100, TestContext.Current.CancellationToken);

        var dsrQuery = TDVSequenceBuilder.BuildDSRQuery();
        var response3 = await SendQueryAndCaptureResponseAsync(dsrQuery);
        Assert.NotNull(response3);
    }

    [Fact]
    public async Task TcpFlow_ResponseCapture_WithMainLoopActive_ShouldWork()
    {
        // Arrange
        await SetupTcpFlowAsync();

        // Verify main loop is running
        Assert.NotNull(_serverSession);

        // Act - Send query and capture response
        var query = TDVSequenceBuilder.BuildDAQuery();
        var response = await SendQueryAndCaptureResponseAsync(query);

        // Assert
        Assert.NotNull(response);
        var daResponse = TDVResponseValidator.ParseDAResponse(response);
        Assert.True(daResponse.IsValid);
    }
}

