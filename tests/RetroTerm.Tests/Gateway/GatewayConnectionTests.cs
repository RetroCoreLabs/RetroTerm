using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using RetroTerm.Core.Protocols;
using RetroTerm.Core.Protocols.WebSocket.Gateway;
using Xunit;

namespace RetroTerm.Tests.Gateway;

public class GatewayConnectionTests : IDisposable
{
    private readonly GatewayListener _listener;
    private readonly int _port;

    public GatewayConnectionTests()
    {
        _listener = new GatewayListener();
        _port = GetFreePort();
    }

    public void Dispose()
    {
        _listener.Dispose();
    }

    [Fact]
    public void ConnectionType_ReturnsGateway()
    {
        var connection = new GatewayConnection(_listener, 43, "TERMINAL 12");
        Assert.Equal("ND-100 Gateway", connection.ConnectionType);
        connection.Dispose();
    }

    [Fact]
    public void Description_ContainsTerminalNameAndIdentCode()
    {
        var connection = new GatewayConnection(_listener, 43, "TERMINAL 12");
        Assert.Contains("TERMINAL 12", connection.Description);
        Assert.Contains("43", connection.Description);
        connection.Dispose();
    }

    [Fact]
    public void IdentCode_ReturnsConfiguredValue()
    {
        var connection = new GatewayConnection(_listener, 45, "TEST");
        Assert.Equal(45, connection.IdentCode);
        connection.Dispose();
    }

    [Fact]
    public void Status_IsDisconnectedInitially()
    {
        var connection = new GatewayConnection(_listener, 43, "TEST");
        Assert.Equal(ConnectionStatus.Disconnected, connection.Status);
        connection.Dispose();
    }

    [Fact]
    public async Task ConnectAsync_ThrowsWhenNoEmulator()
    {
        var connection = new GatewayConnection(_listener, 43, "TEST");
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => connection.ConnectAsync());
        connection.Dispose();
    }

    [Fact]
    public async Task ConnectAsync_SucceedsWithEmulator()
    {
        await _listener.StartAsync(_port);
        var connectedEvent = new TaskCompletionSource<bool>();
        _listener.EmulatorConnected += () => connectedEvent.TrySetResult(true);

        using var client = new ClientWebSocket();
        await client.ConnectAsync(new Uri($"ws://127.0.0.1:{_port}/"), CancellationToken.None);
        await connectedEvent.Task;

        var connection = new GatewayConnection(_listener, 43, "TERMINAL 12");
        await connection.ConnectAsync();

        Assert.Equal(ConnectionStatus.Connected, connection.Status);
        Assert.True(_listener.IsIdentCodeInUse(43));

        await connection.DisconnectAsync();
        await client.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None);
        await _listener.StopAsync();
    }

    [Fact]
    public async Task ConnectAsync_SendsClientConnectedMessage()
    {
        await _listener.StartAsync(_port);
        var connectedEvent = new TaskCompletionSource<bool>();
        _listener.EmulatorConnected += () => connectedEvent.TrySetResult(true);

        using var client = new ClientWebSocket();
        await client.ConnectAsync(new Uri($"ws://127.0.0.1:{_port}/"), CancellationToken.None);
        await connectedEvent.Task;

        var connection = new GatewayConnection(_listener, 43, "TERMINAL 12");
        await connection.ConnectAsync();

        // Read the client-connected message
        var buffer = new byte[4096];
        var result = await client.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
        Assert.Equal(WebSocketMessageType.Text, result.MessageType);

        var json = Encoding.UTF8.GetString(buffer, 0, result.Count);
        using var doc = JsonDocument.Parse(json);
        Assert.Equal("client-connected", doc.RootElement.GetProperty("type").GetString());
        Assert.Equal(43, doc.RootElement.GetProperty("identCode").GetInt32());

        await connection.DisconnectAsync();
        await client.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None);
        await _listener.StopAsync();
    }

    [Fact]
    public async Task DisconnectAsync_SendsClientDisconnectedMessage()
    {
        await _listener.StartAsync(_port);
        var connectedEvent = new TaskCompletionSource<bool>();
        _listener.EmulatorConnected += () => connectedEvent.TrySetResult(true);

        using var client = new ClientWebSocket();
        await client.ConnectAsync(new Uri($"ws://127.0.0.1:{_port}/"), CancellationToken.None);
        await connectedEvent.Task;

        var connection = new GatewayConnection(_listener, 43, "TERMINAL 12");
        await connection.ConnectAsync();

        // Read the client-connected message first
        var buffer = new byte[4096];
        await client.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);

        // Now disconnect
        await connection.DisconnectAsync();

        Assert.Equal(ConnectionStatus.Disconnected, connection.Status);
        Assert.False(_listener.IsIdentCodeInUse(43));

        // Read the client-disconnected message
        var result = await client.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
        var json = Encoding.UTF8.GetString(buffer, 0, result.Count);
        using var doc = JsonDocument.Parse(json);
        Assert.Equal("client-disconnected", doc.RootElement.GetProperty("type").GetString());
        Assert.Equal(43, doc.RootElement.GetProperty("identCode").GetInt32());

        await client.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None);
        await _listener.StopAsync();
    }

    [Fact]
    public async Task ExclusiveAccess_SecondConnectionToSameIdentCodeFails()
    {
        await _listener.StartAsync(_port);
        var connectedEvent = new TaskCompletionSource<bool>();
        _listener.EmulatorConnected += () => connectedEvent.TrySetResult(true);

        using var client = new ClientWebSocket();
        await client.ConnectAsync(new Uri($"ws://127.0.0.1:{_port}/"), CancellationToken.None);
        await connectedEvent.Task;

        var conn1 = new GatewayConnection(_listener, 43, "TERMINAL 12");
        await conn1.ConnectAsync();

        var conn2 = new GatewayConnection(_listener, 43, "TERMINAL 12");
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => conn2.ConnectAsync());

        Assert.Equal(ConnectionStatus.Error, conn2.Status);

        await conn1.DisconnectAsync();
        conn2.Dispose();

        await client.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None);
        await _listener.StopAsync();
    }

    [Fact]
    public async Task SendAsync_SendsTermInputToEmulator()
    {
        await _listener.StartAsync(_port);
        var connectedEvent = new TaskCompletionSource<bool>();
        _listener.EmulatorConnected += () => connectedEvent.TrySetResult(true);

        using var client = new ClientWebSocket();
        await client.ConnectAsync(new Uri($"ws://127.0.0.1:{_port}/"), CancellationToken.None);
        await connectedEvent.Task;

        var connection = new GatewayConnection(_listener, 43, "TERMINAL 12");
        await connection.ConnectAsync();

        // Read the client-connected message
        var buffer = new byte[4096];
        await client.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);

        // Send data from terminal
        var testData = new byte[] { 0x41, 0x42, 0x43 }; // "ABC"
        await connection.SendAsync(new ReadOnlyMemory<byte>(testData));

        // Read the term-input binary frame: [0x01][identCode][data...]
        var result = await client.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
        Assert.Equal(WebSocketMessageType.Binary, result.MessageType);
        Assert.Equal(2 + testData.Length, result.Count); // 2-byte header + data

        Assert.Equal(0x01, buffer[0]); // type = term-input
        Assert.Equal(43, buffer[1]);   // identCode

        for (int i = 0; i < testData.Length; i++)
        {
            Assert.Equal(testData[i], buffer[2 + i]);
        }

        await connection.DisconnectAsync();
        await client.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None);
        await _listener.StopAsync();
    }

    [Fact]
    public async Task EmulatorDisconnect_NotifiesActiveConnections()
    {
        await _listener.StartAsync(_port);
        var connectedEvent = new TaskCompletionSource<bool>();
        _listener.EmulatorConnected += () => connectedEvent.TrySetResult(true);

        using var client = new ClientWebSocket();
        await client.ConnectAsync(new Uri($"ws://127.0.0.1:{_port}/"), CancellationToken.None);
        await connectedEvent.Task;

        var connection = new GatewayConnection(_listener, 43, "TERMINAL 12");
        var statusChanges = new List<ConnectionStatus>();
        connection.StatusChanged += (status) => statusChanges.Add(status);

        var errorFired = new TaskCompletionSource<bool>();
        connection.ErrorOccurred += (ex) => errorFired.TrySetResult(true);

        await connection.ConnectAsync();

        // Emulator disconnects
        await client.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None);

        // Wait for error event
        var waitResult = await Task.WhenAny(errorFired.Task, Task.Delay(5000));
        Assert.True(errorFired.Task.IsCompleted, "ErrorOccurred should have fired");

        Assert.Equal(ConnectionStatus.Disconnected, connection.Status);
        Assert.False(_listener.IsIdentCodeInUse(43));

        connection.Dispose();
        await _listener.StopAsync();
    }

    [Fact]
    public async Task Dispose_UnregistersFromListener()
    {
        await _listener.StartAsync(_port);
        var connectedEvent = new TaskCompletionSource<bool>();
        _listener.EmulatorConnected += () => connectedEvent.TrySetResult(true);

        using var client = new ClientWebSocket();
        await client.ConnectAsync(new Uri($"ws://127.0.0.1:{_port}/"), CancellationToken.None);
        await connectedEvent.Task;

        var connection = new GatewayConnection(_listener, 43, "TERMINAL 12");
        await connection.ConnectAsync();
        Assert.True(_listener.IsIdentCodeInUse(43));

        connection.Dispose();
        Assert.False(_listener.IsIdentCodeInUse(43));

        await client.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None);
        await _listener.StopAsync();
    }

    [Fact]
    public async Task SendAsync_ThrowsWhenNotConnected()
    {
        var connection = new GatewayConnection(_listener, 43, "TEST");
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => connection.SendAsync(new ReadOnlyMemory<byte>(new byte[] { 0x41 })));
        connection.Dispose();
    }

    [Fact]
    public async Task DisconnectAsync_IdempotentWhenAlreadyDisconnected()
    {
        var connection = new GatewayConnection(_listener, 43, "TEST");
        // Should not throw
        await connection.DisconnectAsync();
        Assert.Equal(ConnectionStatus.Disconnected, connection.Status);
        connection.Dispose();
    }

    [Fact]
    public async Task DifferentIdentCodes_CanConnectSimultaneously()
    {
        await _listener.StartAsync(_port);
        var connectedEvent = new TaskCompletionSource<bool>();
        _listener.EmulatorConnected += () => connectedEvent.TrySetResult(true);

        using var client = new ClientWebSocket();
        await client.ConnectAsync(new Uri($"ws://127.0.0.1:{_port}/"), CancellationToken.None);
        await connectedEvent.Task;

        var conn1 = new GatewayConnection(_listener, 43, "TERMINAL 12");
        var conn2 = new GatewayConnection(_listener, 44, "TERMINAL 13");

        await conn1.ConnectAsync();
        await conn2.ConnectAsync();

        Assert.True(_listener.IsIdentCodeInUse(43));
        Assert.True(_listener.IsIdentCodeInUse(44));
        Assert.Equal(ConnectionStatus.Connected, conn1.Status);
        Assert.Equal(ConnectionStatus.Connected, conn2.Status);

        await conn1.DisconnectAsync();
        await conn2.DisconnectAsync();

        Assert.False(_listener.IsIdentCodeInUse(43));
        Assert.False(_listener.IsIdentCodeInUse(44));

        await client.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None);
        await _listener.StopAsync();
    }

    private static int GetFreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}
