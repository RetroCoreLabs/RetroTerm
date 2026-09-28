using System;
using System.Collections.Concurrent;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace RetroTerm.Core.Protocols.Mock;

/// <summary>
/// Mock connection for testing - uses in-memory ringbuffers to simulate data flow
/// </summary>
public class MockConnection : IConnection
{
    private readonly ConcurrentQueue<byte[]> _sendQueue = new();
    private readonly ConcurrentQueue<byte[]> _receiveQueue = new();
    private ConnectionStatus _status;
    private CancellationTokenSource? _cts;
    private Task? _simulationTask;
    private readonly bool _autoRespond;

    public ConnectionStatus Status
    {
        get => _status;
        private set
        {
            if (_status != value)
            {
                _status = value;
                StatusChanged?.Invoke(value);
            }
        }
    }

    public bool IsConnected => Status == ConnectionStatus.Connected;
    public string ConnectionType => "Mock (In-Memory)";
    public string Description => "Mock connection for testing";

    public event Action<ReadOnlyMemory<byte>>? DataReceived;
    public event Action<ConnectionStatus>? StatusChanged;
    public event Action<Exception>? ErrorOccurred;

    public MockConnection(bool autoRespond = true)
    {
        _autoRespond = autoRespond;
        _status = ConnectionStatus.Disconnected;
    }

    public Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        if (Status != ConnectionStatus.Disconnected)
            throw new InvalidOperationException($"Cannot connect in state: {Status}");

        Status = ConnectionStatus.Connecting;
        Status = ConnectionStatus.Connected;

        _cts = new CancellationTokenSource();
        _simulationTask = SimulateDataFlowAsync(_cts.Token);

        return Task.CompletedTask;
    }

    public Task SendAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
    {
        if (!IsConnected)
            throw new InvalidOperationException("Not connected");

        var bytes = data.ToArray();
        _sendQueue.Enqueue(bytes);

        return Task.CompletedTask;
    }

    public async Task DisconnectAsync()
    {
        if (Status == ConnectionStatus.Disconnected)
            return;

        Status = ConnectionStatus.Disconnecting;

        _cts?.Cancel();
        if (_simulationTask != null)
        {
            try
            {
                await _simulationTask;
            }
            catch (OperationCanceledException)
            {
                // Expected
            }
        }

        Status = ConnectionStatus.Disconnected;
        _cts?.Dispose();
        _cts = null;
        _simulationTask = null;
    }

    /// <summary>
    /// Manually inject data (simulates receiving from remote)
    /// </summary>
    public void SimulateReceive(string data)
    {
        if (IsConnected)
        {
            var bytes = Encoding.UTF8.GetBytes(data);
            DataReceived?.Invoke(bytes);
        }
    }

    /// <summary>
    /// Manually inject raw bytes
    /// </summary>
    public void SimulateReceive(byte[] data)
    {
        if (IsConnected)
        {
            DataReceived?.Invoke(data);
        }
    }

    private async Task SimulateDataFlowAsync(CancellationToken cancellationToken)
    {
        try
        {
            // Send welcome message
            if (_autoRespond)
            {
                await Task.Delay(100, cancellationToken);
                SimulateReceive("Welcome to Mock Connection!\r\n");
                SimulateReceive("Type 'test' to see colored output.\r\n");
                SimulateReceive("\r\nPrompt> ");
            }

            while (!cancellationToken.IsCancellationRequested)
            {
                // Check for sent data (user typed something)
                if (_sendQueue.TryDequeue(out var sentData))
                {
                    if (_autoRespond)
                    {
                        // Echo back what was sent
                        var text = Encoding.UTF8.GetString(sentData);

                        if (text.Contains("test"))
                        {
                            // Send colored response
                            SimulateReceive("\r\nColors test:\r\n");
                            SimulateReceive("\x1b[31mRED\x1b[0m ");
                            SimulateReceive("\x1b[32mGREEN\x1b[0m ");
                            SimulateReceive("\x1b[34mBLUE\x1b[0m ");
                            SimulateReceive("\x1b[33mYELLOW\x1b[0m\r\n");
                            SimulateReceive("\r\nPrompt> ");
                        }
                        else if (text.Contains("exit") || text.Contains("quit"))
                        {
                            SimulateReceive("\r\nGoodbye!\r\n");
                            await DisconnectAsync();
                            return;
                        }
                        else
                        {
                            // Echo
                            SimulateReceive($"\r\nYou typed: {text}");
                            SimulateReceive("\r\nPrompt> ");
                        }
                    }
                }

                await Task.Delay(50, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected on disconnect
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(ex);
        }
    }

    public void Dispose()
    {
        DisconnectAsync().Wait(1000); // Timeout after 1 second
    }
}


