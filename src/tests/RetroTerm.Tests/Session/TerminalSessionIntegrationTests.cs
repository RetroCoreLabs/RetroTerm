using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using RetroTerm.Core.Protocols.Net;
using RetroTerm.Core.Session;
using RetroTerm.Core.Terminal.Emulators.TDV;
using Xunit;

namespace RetroTerm.Tests.Session;

/// <summary>
/// Integration tests that actually connect to TestServer to verify end-to-end query/response flow
/// These tests require TestServer to be running on localhost:8080
/// </summary>
public class TerminalSessionIntegrationTests : IDisposable
{
    private TcpListener? _testServer;
    private TcpClient? _serverClient;
    private NetworkStream? _serverStream;
    private readonly ManualResetEventSlim _clientConnected = new();

    public void Dispose()
    {
        _serverStream?.Close();
        _serverClient?.Close();
        _testServer?.Stop();
        _clientConnected.Dispose();
    }

    /// <summary>
    /// Sets up a simple test server that sends queries and receives responses
    /// </summary>
    private async Task StartTestServerAsync(int port)
    {
        _testServer = new TcpListener(IPAddress.Loopback, port);
        _testServer.Start();

        _ = Task.Run(async () =>
        {
            _serverClient = await _testServer.AcceptTcpClientAsync();
            _serverStream = _serverClient.GetStream();
            _clientConnected.Set();
        });
    }

    [Fact(Skip = "Requires manual TestServer - run manually when debugging")]
    public async Task TerminalSession_ShouldSendResponse_WhenConnectedToTestServer()
    {
        // This test requires TestServer to be running manually
        // Start TestServer: dotnet run --project tests/RetroTerm.TestServer

        // Arrange
        var emulator = new TDV2200Emulator(80, 24);
        var session = new TerminalSession(emulator, "Test");
        var connection = new TelnetConnection("localhost", 8080);

        // Monitor responses received by server (would need to be implemented in TestServer)
        // For now, we'll just verify connection works - no response counters yet.

        // Act
        await session.ConnectAsync(connection, TestContext.Current.CancellationToken);

        // Wait a bit for connection to stabilize
        await Task.Delay(500, TestContext.Current.CancellationToken);

        // Send a query manually through the connection to test response
        // In real scenario, TestServer sends the query
        var query = new byte[] { 0x1B, 0x5B, 0x63 }; // ESC [ c (Primary DA)
        await connection.SendAsync(query, TestContext.Current.CancellationToken);

        // Wait for response
        await Task.Delay(1000, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(session.IsConnected);

        // Cleanup
        await session.DisconnectAsync();
    }

    [Fact]
    public async Task TerminalSession_ShouldReceiveQueryAndSendResponse_ThroughMockServer()
    {
        // Arrange - Create a simple mock server
        const int port = 9999;
        await StartTestServerAsync(port);

        var emulator = new TDV2200Emulator(80, 24);
        var session = new TerminalSession(emulator, "Test");
        var connection = new TelnetConnection("localhost", port);

        // Wire TDV query/response handling
        session.WireTDVQueryResponse();

        var responseReceived = false;
        var responseBytes = Array.Empty<byte>();

        // Wait for server to accept connection
        await Task.Delay(100, TestContext.Current.CancellationToken);

        // Connect client
        await session.ConnectAsync(connection, TestContext.Current.CancellationToken);

        // Wait for connection to be established
        _clientConnected.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        await Task.Delay(200, TestContext.Current.CancellationToken);

        // Act - Server sends Primary DA query: ESC [ c
        if (_serverStream != null)
        {
            var query = new byte[] { 0x1B, 0x5B, 0x63 }; // ESC [ c
            await _serverStream.WriteAsync(query, TestContext.Current.CancellationToken);
            await _serverStream.FlushAsync(TestContext.Current.CancellationToken);

            // Wait for response
            await Task.Delay(500, TestContext.Current.CancellationToken);

            // Read response
            var buffer = new byte[256];
            if (_serverStream.DataAvailable)
            {
                var bytesRead = await _serverStream.ReadAsync(buffer, TestContext.Current.CancellationToken);
                if (bytesRead > 0)
                {
                    responseReceived = true;
                    responseBytes = new byte[bytesRead];
                    Array.Copy(buffer, responseBytes, bytesRead);
                }
            }
        }

        // Assert
        Assert.True(session.IsConnected);
        Assert.True(responseReceived, "Response should have been received by server");
        Assert.NotEmpty(responseBytes);

        var responseString = Encoding.UTF8.GetString(responseBytes);
        Assert.StartsWith("\x1b[", responseString);

        // Cleanup
        await session.DisconnectAsync();
    }

    [Fact]
    public async Task TerminalSession_ShouldSendSecondaryDAResponse_WhenQueryReceived()
    {
        // Arrange
        const int port = 9998;
        await StartTestServerAsync(port);

        var emulator = new TDV2200Emulator(80, 24);
        var session = new TerminalSession(emulator, "Test");
        var connection = new TelnetConnection("localhost", port);

        // Wire TDV query/response handling
        session.WireTDVQueryResponse();

        var responseReceived = false;
        var responseBytes = Array.Empty<byte>();

        await Task.Delay(100, TestContext.Current.CancellationToken);
        await session.ConnectAsync(connection, TestContext.Current.CancellationToken);
        _clientConnected.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        await Task.Delay(200, TestContext.Current.CancellationToken);

        // Act - Server sends Secondary DA query: ESC [ > c
        if (_serverStream != null)
        {
            var query = new byte[] { 0x1B, 0x5B, 0x3E, 0x63 }; // ESC [ > c
            await _serverStream.WriteAsync(query, TestContext.Current.CancellationToken);
            await _serverStream.FlushAsync(TestContext.Current.CancellationToken);

            await Task.Delay(500, TestContext.Current.CancellationToken);

            var buffer = new byte[256];
            if (_serverStream.DataAvailable)
            {
                var bytesRead = await _serverStream.ReadAsync(buffer, TestContext.Current.CancellationToken);
                if (bytesRead > 0)
                {
                    responseReceived = true;
                    responseBytes = new byte[bytesRead];
                    Array.Copy(buffer, responseBytes, bytesRead);
                }
            }
        }

        // Assert
        Assert.True(responseReceived);
        var responseString = Encoding.UTF8.GetString(responseBytes);
        Assert.StartsWith("\x1b[>", responseString);
        Assert.Contains("220", responseString); // TDV2200 firmware ID

        await session.DisconnectAsync();
    }
}

