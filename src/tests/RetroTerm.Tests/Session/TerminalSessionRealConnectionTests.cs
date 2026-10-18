using System;
using System.Collections.Generic;
using System.Linq;
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
/// REAL integration tests that test the ACTUAL flow:
/// 1. Start a TCP server (like TestServer)
/// 2. Connect TerminalSession with TelnetConnection
/// 3. Send queries from server
/// 4. Verify responses are received by server
/// 
/// These tests catch the EXACT issue happening in manual testing
/// </summary>
public class TerminalSessionRealConnectionTests : IDisposable
{
    private TcpListener? _server;
    private TcpClient? _client;
    private NetworkStream? _serverStream;
    private readonly List<byte[]> _receivedResponses = new();
    private Task? _serverTask;
    private readonly CancellationTokenSource _cts = new();

    public void Dispose()
    {
        _cts.Cancel();
        _serverTask?.Wait(TimeSpan.FromSeconds(1));
        _serverStream?.Close();
        _client?.Close();
        _server?.Stop();
        _cts.Dispose();
    }

    /// <summary>
    /// Starts a TCP server that receives responses from client
    /// </summary>
    private async Task StartResponseReceiverServerAsync(int port)
    {
        _server = new TcpListener(IPAddress.Loopback, port);
        _server.Start();

        _serverTask = Task.Run(async () =>
        {
            try
            {
                _client = await _server.AcceptTcpClientAsync();
                _serverStream = _client.GetStream();

                // Continuously read responses
                var buffer = new byte[4096];
                while (!_cts.Token.IsCancellationRequested)
                {
                    try
                    {
                        var bytesRead = await _serverStream.ReadAsync(buffer, _cts.Token);
                        if (bytesRead == 0) break;

                        var response = new byte[bytesRead];
                        Array.Copy(buffer, response, bytesRead);
                        _receivedResponses.Add(response);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                }
            }
            catch (Exception)
            {
                // Server stopped
            }
        }, _cts.Token);

        // Give server time to start
        await Task.Delay(100);
    }

    /// <summary>
    /// Sends a query from server to client
    /// </summary>
    private async Task SendQueryFromServerAsync(byte[] query)
    {
        if (_serverStream == null)
        {
            // Wait for connection
            var timeout = DateTime.Now.AddSeconds(5);
            while (_serverStream == null && DateTime.Now < timeout)
            {
                await Task.Delay(50);
            }
        }

        if (_serverStream != null)
        {
            await _serverStream.WriteAsync(query);
            await _serverStream.FlushAsync();
        }
    }

    [Fact]
    public async Task TerminalSession_ShouldSendPrimaryDAResponse_WhenQueryReceivedFromRealServer()
    {
        // Arrange - Start server on port 9997
        const int port = 9997;
        await StartResponseReceiverServerAsync(port);

        var emulator = new TDV2200Emulator(80, 24);
        var session = new TerminalSession(emulator, "Test");
        var connection = new TelnetConnection("localhost", port);

        // Act - Connect client
        await session.ConnectAsync(connection, TestContext.Current.CancellationToken);

        // Wait for connection to be established
        await Task.Delay(200, TestContext.Current.CancellationToken);

        // Send Primary DA query from server: ESC [ c
        var query = new byte[] { 0x1B, 0x5B, 0x63 };
        await SendQueryFromServerAsync(query);

        // Wait for response
        await Task.Delay(500, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(session.IsConnected, "Session should be connected");
        Assert.NotEmpty(_receivedResponses);

        var response = _receivedResponses.Last();
        var responseString = Encoding.UTF8.GetString(response);

        Assert.StartsWith("\x1b[", responseString);
        Assert.Contains("c", responseString);

        // Cleanup
        await session.DisconnectAsync();
    }

    [Fact]
    public async Task TerminalSession_ShouldSendSecondaryDAResponse_WhenQueryReceivedFromRealServer()
    {
        // Arrange
        const int port = 9996;
        await StartResponseReceiverServerAsync(port);

        var emulator = new TDV2200Emulator(80, 24);
        var session = new TerminalSession(emulator, "Test");
        var connection = new TelnetConnection("localhost", port);

        // Act
        await session.ConnectAsync(connection, TestContext.Current.CancellationToken);
        await Task.Delay(200, TestContext.Current.CancellationToken);

        // Send Secondary DA query: ESC [ > c
        var query = new byte[] { 0x1B, 0x5B, 0x3E, 0x63 };
        await SendQueryFromServerAsync(query);

        await Task.Delay(500, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(session.IsConnected);
        Assert.NotEmpty(_receivedResponses);

        var response = _receivedResponses.Last();
        var responseString = Encoding.UTF8.GetString(response);

        Assert.StartsWith("\x1b[>", responseString);
        Assert.Contains("220", responseString); // TDV2200 firmware ID
        Assert.EndsWith("c", responseString);

        await session.DisconnectAsync();
    }

    [Fact]
    public async Task TerminalSession_ShouldSendCPRResponse_WhenQueryReceivedFromRealServer()
    {
        // Arrange
        const int port = 9995;
        await StartResponseReceiverServerAsync(port);

        var emulator = new TDV2200Emulator(80, 24);
        var session = new TerminalSession(emulator, "Test");
        var connection = new TelnetConnection("localhost", port);

        // Act
        await session.ConnectAsync(connection, TestContext.Current.CancellationToken);
        await Task.Delay(200, TestContext.Current.CancellationToken);

        // Move cursor first
        var moveCursor = new byte[] { 0x1B, 0x5B, 0x35, 0x3B, 0x31, 0x30, 0x48 }; // ESC [ 5 ; 10 H
        await SendQueryFromServerAsync(moveCursor);
        await Task.Delay(100, TestContext.Current.CancellationToken);
        _receivedResponses.Clear();

        // Send CPR query: ESC [ 6 n
        var query = new byte[] { 0x1B, 0x5B, 0x36, 0x6E };
        await SendQueryFromServerAsync(query);

        await Task.Delay(500, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(session.IsConnected);
        Assert.NotEmpty(_receivedResponses);

        var response = _receivedResponses.Last();
        var responseString = Encoding.UTF8.GetString(response);

        Assert.StartsWith("\x1b[", responseString);
        Assert.EndsWith("R", responseString);
        Assert.Contains("5", responseString);
        Assert.Contains("10", responseString);

        await session.DisconnectAsync();
    }

    [Fact]
    public async Task TerminalSession_ShouldSendDSRResponse_WhenQueryReceivedFromRealServer()
    {
        // Arrange
        const int port = 9994;
        await StartResponseReceiverServerAsync(port);

        var emulator = new TDV2200Emulator(80, 24);
        var session = new TerminalSession(emulator, "Test");
        var connection = new TelnetConnection("localhost", port);

        // Act
        await session.ConnectAsync(connection, TestContext.Current.CancellationToken);
        await Task.Delay(200, TestContext.Current.CancellationToken);

        // Send DSR query: ESC [ 5 n
        var query = new byte[] { 0x1B, 0x5B, 0x35, 0x6E };
        await SendQueryFromServerAsync(query);

        await Task.Delay(500, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(session.IsConnected);
        Assert.NotEmpty(_receivedResponses);

        var response = _receivedResponses.Last();
        var responseString = Encoding.UTF8.GetString(response);

        Assert.StartsWith("\x1b[", responseString);
        Assert.EndsWith("n", responseString);

        await session.DisconnectAsync();
    }

    [Fact]
    public async Task TerminalSession_ShouldSendMultipleResponses_WhenMultipleQueriesReceived()
    {
        // Arrange
        const int port = 9993;
        await StartResponseReceiverServerAsync(port);

        var emulator = new TDV2200Emulator(80, 24);
        var session = new TerminalSession(emulator, "Test");
        var connection = new TelnetConnection("localhost", port);

        // Act
        await session.ConnectAsync(connection, TestContext.Current.CancellationToken);
        await Task.Delay(200, TestContext.Current.CancellationToken);

        // Send multiple queries
        var daQuery = new byte[] { 0x1B, 0x5B, 0x63 };
        await SendQueryFromServerAsync(daQuery);
        await Task.Delay(200, TestContext.Current.CancellationToken);

        var cprQuery = new byte[] { 0x1B, 0x5B, 0x36, 0x6E };
        await SendQueryFromServerAsync(cprQuery);
        await Task.Delay(500, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(session.IsConnected);
        Assert.True(_receivedResponses.Count >= 2, $"Expected at least 2 responses, got {_receivedResponses.Count}");

        // First response should be DA
        var firstResponse = Encoding.UTF8.GetString(_receivedResponses[_receivedResponses.Count - 2]);
        Assert.StartsWith("\x1b[", firstResponse);

        // Second response should be CPR
        var secondResponse = Encoding.UTF8.GetString(_receivedResponses[_receivedResponses.Count - 1]);
        Assert.StartsWith("\x1b[", secondResponse);
        Assert.EndsWith("R", secondResponse);

        await session.DisconnectAsync();
    }

    [Fact]
    public async Task TerminalSession_ShouldSendTDV1200SecondaryDA_WithCorrectFirmwareID()
    {
        // Arrange
        const int port = 9992;
        await StartResponseReceiverServerAsync(port);

        var emulator = new TDV1200Emulator(80, 24);
        var session = new TerminalSession(emulator, "Test");
        var connection = new TelnetConnection("localhost", port);

        // Act
        await session.ConnectAsync(connection, TestContext.Current.CancellationToken);
        await Task.Delay(200, TestContext.Current.CancellationToken);

        var query = new byte[] { 0x1B, 0x5B, 0x3E, 0x63 };
        await SendQueryFromServerAsync(query);
        await Task.Delay(500, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotEmpty(_receivedResponses);
        var response = Encoding.UTF8.GetString(_receivedResponses.Last());
        Assert.Contains("120", response); // TDV1200 firmware ID

        await session.DisconnectAsync();
    }

    [Fact]
    public async Task TerminalSession_ShouldSendTDV2215SecondaryDA_WithCorrectFirmwareID()
    {
        // Arrange
        const int port = 9991;
        await StartResponseReceiverServerAsync(port);

        var emulator = new TDV2215Emulator(80, 24);
        var session = new TerminalSession(emulator, "Test");
        var connection = new TelnetConnection("localhost", port);

        // Act
        await session.ConnectAsync(connection, TestContext.Current.CancellationToken);
        await Task.Delay(200, TestContext.Current.CancellationToken);

        var query = new byte[] { 0x1B, 0x5B, 0x3E, 0x63 };
        await SendQueryFromServerAsync(query);
        await Task.Delay(500, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotEmpty(_receivedResponses);
        var response = Encoding.UTF8.GetString(_receivedResponses.Last());
        Assert.Contains("115", response); // TDV2215 firmware ID

        await session.DisconnectAsync();
    }

    [Fact]
    public async Task TerminalSession_ShouldHandleByteByByteQuery_AndStillRespond()
    {
        // Arrange
        const int port = 9990;
        await StartResponseReceiverServerAsync(port);

        var emulator = new TDV2200Emulator(80, 24);
        var session = new TerminalSession(emulator, "Test");
        var connection = new TelnetConnection("localhost", port);

        // Act
        await session.ConnectAsync(connection, TestContext.Current.CancellationToken);
        await Task.Delay(200, TestContext.Current.CancellationToken);

        // Send query byte by byte
        var query = new byte[] { 0x1B, 0x5B, 0x63 };
        foreach (var b in query)
        {
            await SendQueryFromServerAsync(new[] { b });
            await Task.Delay(50, TestContext.Current.CancellationToken);
        }

        await Task.Delay(500, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotEmpty(_receivedResponses);
        var response = Encoding.UTF8.GetString(_receivedResponses.Last());
        Assert.StartsWith("\x1b[", response);

        await session.DisconnectAsync();
    }

    [Fact]
    public async Task TerminalSession_ShouldNotSendResponse_WhenNotConnected()
    {
        // Arrange
        const int port = 9989;
        await StartResponseReceiverServerAsync(port);

        var emulator = new TDV2200Emulator(80, 24);
        var session = new TerminalSession(emulator, "Test");
        var connection = new TelnetConnection("localhost", port);

        // Don't connect - send query directly to emulator
        var query = new byte[] { 0x1B, 0x5B, 0x63 };
        emulator.ProcessData(query);

        await Task.Delay(200, TestContext.Current.CancellationToken);

        // Assert - No connection, so no responses should be sent
        Assert.False(session.IsConnected);
        Assert.Empty(_receivedResponses);
    }
}

