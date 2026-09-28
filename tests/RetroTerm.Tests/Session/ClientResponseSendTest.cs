using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using RetroTerm.Core.Protocols;
using RetroTerm.Core.Protocols.Net;
using RetroTerm.Core.Protocols.TelnetServer.Utilities;
using RetroTerm.Core.Session;
using RetroTerm.Core.Terminal.Emulators.TDV;
using Xunit;

namespace RetroTerm.Tests.Session;

/// <summary>
/// Test to verify that client actually sends responses over TCP
/// This simulates the exact Desktop app scenario
/// </summary>
public class ClientResponseSendTest : IDisposable
{
    private TcpListener? _server;
    private TcpClient? _serverClient;
    private TerminalSession? _session;
    private IConnection? _connection;
    private TDV2200Emulator? _emulator;
    private NetworkStream? _serverStream;
    private int _serverPort = 0;
    private byte[]? _receivedResponse;

    public void Dispose()
    {
        _session?.DisconnectAsync().GetAwaiter().GetResult();
        _session?.Dispose();
        _connection?.DisconnectAsync().GetAwaiter().GetResult();
        _connection?.Dispose();
        _serverStream?.Close();
        _serverClient?.Close();
        _server?.Stop();
    }

    private async Task SetupAsync()
    {
        // Start TCP server
        _server = new TcpListener(IPAddress.Loopback, 0);
        _server.Start();
        _serverPort = ((IPEndPoint)_server.LocalEndpoint).Port;

        // Create emulator and session EXACTLY like Desktop app
        _emulator = new TDV2200Emulator(80, 24);
        _session = new TerminalSession(_emulator, "RetroTerm");

        // Create connection EXACTLY like Desktop app
        var parameters = new ConnectionFactory.ConnectionParameters
        {
            Protocol = ConnectionFactory.ProtocolType.Telnet,
            Host = "127.0.0.1",
            Port = _serverPort,
            EmulatorType = "TDV2200"
        };

        _connection = ConnectionFactory.CreateConnection(parameters);

        // Connect EXACTLY like Desktop app
        await _session.ConnectAsync(_connection);

        // Accept server connection
        _serverClient = await _server.AcceptTcpClientAsync();
        _serverStream = _serverClient.GetStream();

        // Send Telnet negotiation
        var negotiator = new RetroTerm.Core.Protocols.TelnetServer.Telnet.TelnetNegotiator(_serverStream);
        await negotiator.SendInitialAsync();

        // Wait for negotiation
        await Task.Delay(500);
    }

    [Fact]
    public async Task Client_ShouldSendPrimaryDAResponse_WhenQueryReceived()
    {
        // Arrange
        await SetupAsync();
        var query = TDVSequenceBuilder.BuildDAQuery();

        // Act - Send query from server
        await _serverStream!.WriteAsync(query);
        await _serverStream.FlushAsync();

        // Wait for response
        await Task.Delay(200);

        // Read response from server stream
        var buffer = new byte[256];
        var bytesRead = 0;
        if (_serverStream.DataAvailable)
        {
            bytesRead = await _serverStream.ReadAsync(buffer);
            _receivedResponse = new byte[bytesRead];
            Array.Copy(buffer, _receivedResponse, bytesRead);
        }

        // Also try blocking read with timeout
        if (bytesRead == 0)
        {
            var readTask = _serverStream.ReadAsync(buffer).AsTask();
            var timeoutTask = Task.Delay(500);
            var completedTask = await Task.WhenAny(readTask, timeoutTask);

            if (completedTask == readTask && readTask.IsCompletedSuccessfully)
            {
                bytesRead = await readTask;
                if (bytesRead > 0)
                {
                    _receivedResponse = new byte[bytesRead];
                    Array.Copy(buffer, _receivedResponse, bytesRead);
                }
            }
        }

        // Assert
        System.Console.WriteLine($"[ClientResponseSendTest] Query sent: {BitConverter.ToString(query)}");
        System.Console.WriteLine($"[ClientResponseSendTest] Response received: {bytesRead} bytes");
        if (_receivedResponse != null)
        {
            System.Console.WriteLine($"[ClientResponseSendTest] Response bytes: {BitConverter.ToString(_receivedResponse)}");

            // Strip Telnet protocol bytes (like TestServer does)
            var stripped = RetroTerm.Core.Protocols.TelnetServer.Telnet.TelnetCodec.StripIac(_receivedResponse);
            System.Console.WriteLine($"[ClientResponseSendTest] After StripIac: {stripped.Length} chars");

            // Extract response (escape sequence) from mixed data
            var escIndex = stripped.IndexOf('\x1B');
            if (escIndex >= 0)
            {
                var extractedResponse = stripped.Substring(escIndex);
                System.Console.WriteLine($"[ClientResponseSendTest] Extracted response: {BitConverter.ToString(Encoding.UTF8.GetBytes(extractedResponse))}");

                // Validate it's a valid DA response
                var daResponse = RetroTerm.Core.Protocols.TelnetServer.Utilities.TDVResponseValidator.ParseDAResponse(extractedResponse);
                Assert.True(daResponse.IsValid, $"Response must be valid DA response. Got: {extractedResponse}");
                System.Console.WriteLine($"[ClientResponseSendTest] ✓ Valid DA response detected!");
            }
            else
            {
                Assert.Fail($"CRITICAL: No ESC found in stripped response! Stripped data: {BitConverter.ToString(Encoding.UTF8.GetBytes(stripped))}");
            }
        }
        else
        {
            Assert.Fail($"CRITICAL: No response received! Client must send response!");
        }
    }
}

