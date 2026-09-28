using System;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace RetroTerm.Core.Protocols.TelnetServer.Telnet;

public class TelnetServer
{
    private readonly int _port;
    private readonly ITelnetApp _app;

    public TelnetServer(int port, ITelnetApp app)
    {
        _port = port;
        _app = app;
    }

    public async Task RunAsync()
    {
        var listener = new TcpListener(IPAddress.Any, _port);

        try
        {
            listener.Start();
        }
        catch (SocketException ex) when (ex.SocketErrorCode == SocketError.AccessDenied)
        {
            throw new InvalidOperationException(
                $"Cannot bind to port {_port}. Error: {ex.Message}\n\n" +
                "Possible solutions:\n" +
                $"1. Port {_port} may be in use by another process\n" +
                "2. Try a different port (e.g., 2324, 8080, 9999)\n" +
                "3. Run as Administrator if port requires elevated privileges\n" +
                "4. Check Windows Firewall settings\n" +
                "\n" +
                $"To use a different port, run: RetroTerm.TestServer.exe <port>",
                ex);
        }
        catch (SocketException ex)
        {
            throw new InvalidOperationException(
                $"Cannot bind to port {_port}. Error: {ex.Message}\n\n" +
                $"Try a different port: RetroTerm.TestServer.exe <port>",
                ex);
        }

        Console.WriteLine($"Server started successfully on port {_port}");
        Console.WriteLine("Press Ctrl+C to stop the server\n");

        while (true)
        {
            var client = await listener.AcceptTcpClientAsync();
            _ = HandleClientAsync(client);
        }
    }

    private async Task HandleClientAsync(TcpClient client)
    {
        var clientEndpoint = client.Client.RemoteEndPoint?.ToString() ?? "unknown";
        System.Console.WriteLine($"[TelnetServer] ========================================");
        System.Console.WriteLine($"[TelnetServer] New client connection accepted");
        System.Console.WriteLine($"[TelnetServer] Client: {clientEndpoint}");
        System.Console.WriteLine($"[TelnetServer] ========================================");

        try
        {
            using var session = client;
            var telnet = new TelnetSession(client);
            await telnet.Negotiator.SendInitialAsync();
            await _app.OnConnectedAsync(telnet);
        }
        catch (Exception ex)
        {
            System.Console.WriteLine($"[TelnetServer] ========================================");
            System.Console.WriteLine($"[TelnetServer] Error handling client connection");
            System.Console.WriteLine($"[TelnetServer] Client: {clientEndpoint}");
            System.Console.WriteLine($"[TelnetServer] Exception: {ex.GetType().Name}: {ex.Message}");
            System.Console.WriteLine($"[TelnetServer] Stack trace: {ex.StackTrace}");
            System.Console.WriteLine($"[TelnetServer] ========================================");
        }
        finally
        {
            System.Console.WriteLine($"[TelnetServer] ========================================");
            System.Console.WriteLine($"[TelnetServer] Client connection closed");
            System.Console.WriteLine($"[TelnetServer] Client: {clientEndpoint}");
            System.Console.WriteLine($"[TelnetServer] ========================================");
        }
    }
}
