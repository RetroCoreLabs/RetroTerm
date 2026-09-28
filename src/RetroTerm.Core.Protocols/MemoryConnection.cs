using System;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace RetroTerm.Core.Protocols;

/// <summary>
/// Memory-based connection implementation for unit testing.
/// Uses channels for bidirectional communication without network I/O.
/// </summary>
public class MemoryConnection : IConnection
{
    private readonly Channel<ReadOnlyMemory<byte>> _receiveChannel;
    private readonly Channel<ReadOnlyMemory<byte>> _sendChannel;
    private CancellationTokenSource? _receiveCts;
    private Task? _receiveTask;

    public ConnectionStatus Status { get; private set; }
    public string ConnectionType => "Memory";
    public string Description { get; }

    public event Action<ConnectionStatus>? StatusChanged;
    public event Action<ReadOnlyMemory<byte>>? DataReceived;
    public event Action<Exception>? ErrorOccurred;

    /// <summary>
    /// Gets the send channel for simulating data from the "remote" side
    /// </summary>
    public ChannelWriter<ReadOnlyMemory<byte>> RemoteSend => _receiveChannel.Writer;

    /// <summary>
    /// Gets the receive channel for capturing data sent by the terminal
    /// </summary>
    public ChannelReader<ReadOnlyMemory<byte>> RemoteReceive => _sendChannel.Reader;

    public MemoryConnection(string description = "Memory Connection")
    {
        Description = description;
        Status = ConnectionStatus.Disconnected;

        _receiveChannel = Channel.CreateUnbounded<ReadOnlyMemory<byte>>();
        _sendChannel = Channel.CreateUnbounded<ReadOnlyMemory<byte>>();
    }

    public Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        if (Status != ConnectionStatus.Disconnected)
            throw new InvalidOperationException($"Cannot connect when status is {Status}");

        SetStatus(ConnectionStatus.Connecting);

        // Start receive loop
        _receiveCts = new CancellationTokenSource();
        _receiveTask = ReceiveLoopAsync(_receiveCts.Token);

        SetStatus(ConnectionStatus.Connected);

        return Task.CompletedTask;
    }

    public async Task SendAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
    {
        if (Status != ConnectionStatus.Connected)
            throw new InvalidOperationException($"Cannot send when status is {Status}");

        await _sendChannel.Writer.WriteAsync(data, cancellationToken);
    }

    public async Task DisconnectAsync()
    {
        if (Status == ConnectionStatus.Disconnected)
            return;

        SetStatus(ConnectionStatus.Disconnecting);

        // Stop receive loop
        _receiveCts?.Cancel();

        if (_receiveTask != null)
        {
            try
            {
                await _receiveTask;
            }
            catch (OperationCanceledException)
            {
                // Expected
            }
        }

        SetStatus(ConnectionStatus.Disconnected);
    }

    private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var data = await _receiveChannel.Reader.ReadAsync(cancellationToken);
                DataReceived?.Invoke(data);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected when disconnecting
        }
        catch (Exception ex)
        {
            SetStatus(ConnectionStatus.Error);
            ErrorOccurred?.Invoke(ex);
        }
    }

    private void SetStatus(ConnectionStatus newStatus)
    {
        if (Status != newStatus)
        {
            Status = newStatus;
            StatusChanged?.Invoke(newStatus);
        }
    }

    public void Dispose()
    {
        DisconnectAsync().GetAwaiter().GetResult();
        _receiveCts?.Dispose();
    }
}

