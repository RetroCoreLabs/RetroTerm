using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using RetroTerm.Core.Protocols.WebSocket.Gateway;
using Xunit;

namespace RetroTerm.Tests.Gateway;

public class GatewayListenerTests : IDisposable
{
    private readonly GatewayListener _listener;

    public GatewayListenerTests()
    {
        _listener = new GatewayListener();
    }

    public void Dispose()
    {
        _listener.Dispose();
    }

    [Fact]
    public async Task StartAsync_SetsIsListening()
    {
        int port = GetFreePort();
        await _listener.StartAsync(port);

        Assert.True(_listener.IsListening);
        Assert.Equal(port, _listener.Port);

        await _listener.StopAsync();
    }

    [Fact]
    public async Task StopAsync_ClearsIsListening()
    {
        int port = GetFreePort();
        await _listener.StartAsync(port);
        await _listener.StopAsync();

        Assert.False(_listener.IsListening);
    }

    [Fact]
    public async Task EmulatorConnect_FiresEvent()
    {
        int port = GetFreePort();
        await _listener.StartAsync(port);

        var connectedEvent = new TaskCompletionSource<bool>();
        _listener.EmulatorConnected += () => connectedEvent.TrySetResult(true);

        using var client = new ClientWebSocket();
        await client.ConnectAsync(new Uri($"ws://127.0.0.1:{port}/"), CancellationToken.None);

        var connected = await Task.WhenAny(connectedEvent.Task, Task.Delay(5000, TestContext.Current.CancellationToken));
        Assert.True(connectedEvent.Task.IsCompleted, "EmulatorConnected event should have fired");

        await client.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None);
        await _listener.StopAsync();
    }

    [Fact]
    public async Task EmulatorDisconnect_FiresEvent()
    {
        int port = GetFreePort();
        await _listener.StartAsync(port);

        var connectedEvent = new TaskCompletionSource<bool>();
        var disconnectedEvent = new TaskCompletionSource<bool>();
        _listener.EmulatorConnected += () => connectedEvent.TrySetResult(true);
        _listener.EmulatorDisconnected += () => disconnectedEvent.TrySetResult(true);

        using var client = new ClientWebSocket();
        await client.ConnectAsync(new Uri($"ws://127.0.0.1:{port}/"), CancellationToken.None);
        await connectedEvent.Task;

        // Close the client
        await client.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None);

        var result = await Task.WhenAny(disconnectedEvent.Task, Task.Delay(5000, TestContext.Current.CancellationToken));
        Assert.True(disconnectedEvent.Task.IsCompleted, "EmulatorDisconnected event should have fired");

        await _listener.StopAsync();
    }

    [Fact]
    public async Task RegisterMessage_PopulatesTerminalList()
    {
        int port = GetFreePort();
        await _listener.StartAsync(port);

        var connectedEvent = new TaskCompletionSource<bool>();
        var terminalListEvent = new TaskCompletionSource<bool>();
        _listener.EmulatorConnected += () => connectedEvent.TrySetResult(true);
        _listener.TerminalListChanged += () => terminalListEvent.TrySetResult(true);

        using var client = new ClientWebSocket();
        await client.ConnectAsync(new Uri($"ws://127.0.0.1:{port}/"), CancellationToken.None);
        await connectedEvent.Task;

        // Send register message
        var registerMsg = "{\"type\":\"register\",\"terminals\":[" +
                          "{\"identCode\":43,\"name\":\"TERMINAL 12\",\"logicalDevice\":1}," +
                          "{\"identCode\":44,\"name\":\"TERMINAL 13\",\"logicalDevice\":2}" +
                          "]}";
        var bytes = Encoding.UTF8.GetBytes(registerMsg);
        await client.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None);

        await Task.WhenAny(terminalListEvent.Task, Task.Delay(5000, TestContext.Current.CancellationToken));
        Assert.True(terminalListEvent.Task.IsCompleted, "TerminalListChanged event should have fired");

        var terminals = _listener.GetTerminals();
        Assert.Equal(2, terminals.Count);
        Assert.Equal(43, terminals[0].IdentCode);
        Assert.Equal("TERMINAL 12", terminals[0].Name);
        Assert.Equal(1, terminals[0].LogicalDevice);
        Assert.Equal(44, terminals[1].IdentCode);
        Assert.Equal("TERMINAL 13", terminals[1].Name);
        Assert.Equal(2, terminals[1].LogicalDevice);

        await client.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None);
        await _listener.StopAsync();
    }

    [Fact]
    public async Task SecondConnection_AcceptedAsDiskWorker()
    {
        int port = GetFreePort();
        await _listener.StartAsync(port);

        var connectedEvent = new TaskCompletionSource<bool>();
        var diskConnectedEvent = new TaskCompletionSource<bool>();
        _listener.EmulatorConnected += () => connectedEvent.TrySetResult(true);
        _listener.DiskWorkerConnected += () => diskConnectedEvent.TrySetResult(true);

        using var client1 = new ClientWebSocket();
        await client1.ConnectAsync(new Uri($"ws://127.0.0.1:{port}/"), CancellationToken.None);
        await connectedEvent.Task;

        // Second connection should be accepted as disk worker
        using var client2 = new ClientWebSocket();
        await client2.ConnectAsync(new Uri($"ws://127.0.0.1:{port}/"), CancellationToken.None);

        var result = await Task.WhenAny(diskConnectedEvent.Task, Task.Delay(5000, TestContext.Current.CancellationToken));
        Assert.True(diskConnectedEvent.Task.IsCompleted, "DiskWorkerConnected event should have fired");
        Assert.True(_listener.IsDiskWorkerConnected);

        await client2.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None);
        await client1.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None);
        await _listener.StopAsync();
    }

    [Fact]
    public async Task ThirdConnection_IsRejected()
    {
        int port = GetFreePort();
        await _listener.StartAsync(port);

        var connectedEvent = new TaskCompletionSource<bool>();
        var diskConnectedEvent = new TaskCompletionSource<bool>();
        _listener.EmulatorConnected += () => connectedEvent.TrySetResult(true);
        _listener.DiskWorkerConnected += () => diskConnectedEvent.TrySetResult(true);

        using var client1 = new ClientWebSocket();
        await client1.ConnectAsync(new Uri($"ws://127.0.0.1:{port}/"), CancellationToken.None);
        await connectedEvent.Task;

        using var client2 = new ClientWebSocket();
        await client2.ConnectAsync(new Uri($"ws://127.0.0.1:{port}/"), CancellationToken.None);
        await diskConnectedEvent.Task;

        // Third connection should get closed with code 4000
        using var client3 = new ClientWebSocket();
        await client3.ConnectAsync(new Uri($"ws://127.0.0.1:{port}/"), CancellationToken.None);

        // Read the close frame
        var buffer = new byte[1024];
        var receiveResult = await client3.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
        Assert.Equal(WebSocketMessageType.Close, receiveResult.MessageType);
        Assert.Equal((WebSocketCloseStatus)4000, client3.CloseStatus);

        await client2.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None);
        await client1.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None);
        await _listener.StopAsync();
    }

    [Fact]
    public async Task TermOutput_RoutesToRegisteredConnection()
    {
        int port = GetFreePort();
        await _listener.StartAsync(port);

        var connectedEvent = new TaskCompletionSource<bool>();
        _listener.EmulatorConnected += () => connectedEvent.TrySetResult(true);

        using var client = new ClientWebSocket();
        await client.ConnectAsync(new Uri($"ws://127.0.0.1:{port}/"), CancellationToken.None);
        await connectedEvent.Task;

        // Register terminals
        var registerMsg = "{\"type\":\"register\",\"terminals\":[{\"identCode\":43,\"name\":\"TERMINAL 12\",\"logicalDevice\":1}]}";
        await client.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes(registerMsg)),
            WebSocketMessageType.Text, true, CancellationToken.None);
        await Task.Delay(100, TestContext.Current.CancellationToken);

        // Create and connect a GatewayConnection
        var connection = new GatewayConnection(_listener, 43, "TERMINAL 12");
        var dataReceived = new TaskCompletionSource<byte[]>();
        connection.DataReceived += (data) => dataReceived.TrySetResult(data.ToArray());
        await connection.ConnectAsync(TestContext.Current.CancellationToken);

        // Send term-output from emulator as binary: [0x02][identCode][data...]
        var testData = new byte[] { 0x48, 0x65, 0x6C, 0x6C, 0x6F }; // "Hello"
        var frame = new byte[2 + testData.Length];
        frame[0] = 0x02; // term-output
        frame[1] = 43;   // identCode
        Buffer.BlockCopy(testData, 0, frame, 2, testData.Length);
        await client.SendAsync(new ArraySegment<byte>(frame),
            WebSocketMessageType.Binary, true, CancellationToken.None);

        var result = await Task.WhenAny(dataReceived.Task, Task.Delay(5000, TestContext.Current.CancellationToken));
        Assert.True(dataReceived.Task.IsCompleted, "DataReceived should have fired");

        var received = await dataReceived.Task;
        Assert.Equal(testData.Length, received.Length);
        for (int i = 0; i < testData.Length; i++)
        {
            Assert.Equal(testData[i], received[i]);
        }

        await connection.DisconnectAsync();
        await client.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None);
        await _listener.StopAsync();
    }

    [Fact]
    public async Task TermOutput_IgnoredIfNoConnectionRegistered()
    {
        int port = GetFreePort();
        await _listener.StartAsync(port);

        var connectedEvent = new TaskCompletionSource<bool>();
        _listener.EmulatorConnected += () => connectedEvent.TrySetResult(true);

        using var client = new ClientWebSocket();
        await client.ConnectAsync(new Uri($"ws://127.0.0.1:{port}/"), CancellationToken.None);
        await connectedEvent.Task;

        // Send term-output for identCode with no registered connection — should not throw
        var testData = new byte[] { 0x48, 0x65 };
        var frame = new byte[2 + testData.Length];
        frame[0] = 0x02; // term-output
        frame[1] = 99;   // identCode (no connection)
        Buffer.BlockCopy(testData, 0, frame, 2, testData.Length);
        await client.SendAsync(new ArraySegment<byte>(frame),
            WebSocketMessageType.Binary, true, CancellationToken.None);

        // Give it a moment to process — nothing should crash
        await Task.Delay(200, TestContext.Current.CancellationToken);

        await client.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None);
        await _listener.StopAsync();
    }

    [Fact]
    public void IsIdentCodeInUse_ReturnsFalseWhenNotRegistered()
    {
        Assert.False(_listener.IsIdentCodeInUse(43));
    }

    [Fact]
    public void GetTerminals_ReturnsEmptyByDefault()
    {
        var terminals = _listener.GetTerminals();
        Assert.Empty(terminals);
    }

    [Fact]
    public void TerminalCount_ReturnsZeroByDefault()
    {
        Assert.Equal(0, _listener.TerminalCount);
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
