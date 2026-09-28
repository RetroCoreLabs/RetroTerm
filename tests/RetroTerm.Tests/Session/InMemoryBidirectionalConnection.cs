using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using RetroTerm.Core.Protocols;

namespace RetroTerm.Tests.Session;

/// <summary>
/// Bidirectional in-memory connection pair for testing
/// Simulates a full-duplex connection without TCP overhead
/// </summary>
public class InMemoryBidirectionalConnection : IConnection
{
    private readonly ConcurrentQueue<byte[]> _receiveQueue;
    private readonly ConcurrentQueue<byte[]> _sendQueue;
    private readonly ConcurrentQueue<byte[]> _sentDataHistory; // Track all sent data for testing
    private InMemoryBidirectionalConnection? _peer;
    private ConnectionStatus _status = ConnectionStatus.Disconnected;
    private int _dataSentCount = 0;
    private int _dataReceivedCount = 0;
    /// <summary>
    /// 1 once a receive loop has been started, so a second ConnectAsync cannot start another.
    /// </summary>
    private int _receiveLoopStarted;

    private bool _disableReceiveLoop = false; // Set to true to disable receive loop (for server-side connections that use ReadRawAsync directly)

    public ConnectionStatus Status => _status;
    public string ConnectionType => "InMemoryBidirectional";
    public string Description => "In-Memory Bidirectional Test Connection";
    public bool IsConnected => _status == ConnectionStatus.Connected;

    public event Action<ConnectionStatus>? StatusChanged;
    public event Action<ReadOnlyMemory<byte>>? DataReceived;
    // Required by IConnection; this in-memory test double has no transport that
    // can fail, so it never raises the event.
#pragma warning disable CS0067
    public event Action<Exception>? ErrorOccurred;
#pragma warning restore CS0067

    private InMemoryBidirectionalConnection(ConcurrentQueue<byte[]> receiveQueue, ConcurrentQueue<byte[]> sendQueue)
    {
        _receiveQueue = receiveQueue;
        _sendQueue = sendQueue;
        _sentDataHistory = new ConcurrentQueue<byte[]>();
    }

    /// <summary>
    /// Creates a pair of connected bidirectional connections
    /// </summary>
    public static (InMemoryBidirectionalConnection client, InMemoryBidirectionalConnection server) CreatePair()
    {
        var clientToServer = new ConcurrentQueue<byte[]>();
        var serverToClient = new ConcurrentQueue<byte[]>();

        var client = new InMemoryBidirectionalConnection(clientToServer, serverToClient);
        var server = new InMemoryBidirectionalConnection(serverToClient, clientToServer);

        client._peer = server;
        server._peer = client;

        return (client, server);
    }

    public Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        _status = ConnectionStatus.Connected;
        StatusChanged?.Invoke(_status);

        // ONE RECEIVE LOOP, EVER. Starting a second one reorders the stream.
        //
        // Callers routinely connect this twice: a test does `await connection.ConnectAsync()` and
        // then hands the same connection to `TerminalSession.ConnectAsync`, which connects it
        // again. That used to start a second loop on the same queue, and two loops dequeue
        // concurrently - so loop B could take the second write and raise it BEFORE loop A raised
        // the first. The host's lines then landed on the screen in the wrong order, or looked
        // dropped because the rest had shifted up.
        //
        // Measured 10 September 2026: six lines written one at a time came out wrong on two runs
        // in five before this guard and on NONE in eight after it. Always the same shape - an
        // early line missing and everything below it moved up.
        //
        // It had been seen by eye earlier the same day, as two items of a six-line menu swapped on
        // a client screen, and written off as the probe that read the screen mid-write. It was not
        // the probe.
        //
        // Interlocked, not a plain bool: ConnectAsync is called from more than one thread here.
        //
        // Server-side connections disable the loop entirely and read with ReadRawAsync instead.
        if (!_disableReceiveLoop && System.Threading.Interlocked.Exchange(ref _receiveLoopStarted, 1) == 0)
        {
            _ = Task.Run(() => ReceiveLoopAsync(cancellationToken), cancellationToken);
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Disables the receive loop for this connection
    /// Use this for server-side connections that use ReadRawAsync directly
    /// </summary>
    public void DisableReceiveLoop()
    {
        _disableReceiveLoop = true;
    }

    public Task DisconnectAsync()
    {
        _status = ConnectionStatus.Disconnected;
        StatusChanged?.Invoke(_status);
        return Task.CompletedTask;
    }

    public Task SendAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
    {
        if (_status != ConnectionStatus.Connected)
            throw new InvalidOperationException("Not connected");

        var bytes = data.ToArray();
        // When we send, we enqueue to our send queue, which is the peer's receive queue
        _sendQueue.Enqueue(bytes);
        _sentDataHistory.Enqueue(bytes); // Track for testing
        _dataSentCount++;

        System.Console.WriteLine($"[InMemoryBidirectionalConnection] SendAsync: {bytes.Length} bytes sent (total sent: {_dataSentCount}), enqueued to sendQueue (peer's receiveQueue)");
        System.Console.WriteLine($"[InMemoryBidirectionalConnection] SendAsync bytes: {BitConverter.ToString(bytes)}");
        System.Diagnostics.Debug.WriteLine($"[InMemoryBidirectionalConnection] SendAsync: {bytes.Length} bytes sent, total sent: {_dataSentCount}");
        System.Diagnostics.Debug.WriteLine($"[InMemoryBidirectionalConnection] SendAsync bytes: {BitConverter.ToString(bytes)}");

        // The peer's receive loop will pick it up automatically
        // No need to notify synchronously - the receive loop polls

        return Task.CompletedTask;
    }

    private void NotifyDataAvailable()
    {
        // This is called when data is manually enqueued
        // The receive loop will handle it automatically
    }

    private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested && _status == ConnectionStatus.Connected)
        {
            if (_receiveQueue.TryDequeue(out var data))
            {
                _dataReceivedCount++;
                System.Console.WriteLine($"[InMemoryBidirectionalConnection] ReceiveLoop: Dequeued {data.Length} bytes from receiveQueue (total received: {_dataReceivedCount})");
                System.Console.WriteLine($"[InMemoryBidirectionalConnection] ReceiveLoop bytes: {BitConverter.ToString(data)}");
                System.Console.WriteLine($"[InMemoryBidirectionalConnection] ReceiveLoop: Firing DataReceived event (subscribers: {(DataReceived?.GetInvocationList().Length ?? 0)})");
                System.Diagnostics.Debug.WriteLine($"[InMemoryBidirectionalConnection] ReceiveLoop: {data.Length} bytes received, total received: {_dataReceivedCount}");
                System.Diagnostics.Debug.WriteLine($"[InMemoryBidirectionalConnection] ReceiveLoop bytes: {BitConverter.ToString(data)}");
                DataReceived?.Invoke(data);
                System.Console.WriteLine($"[InMemoryBidirectionalConnection] ReceiveLoop: DataReceived event fired");
            }
            else
            {
                // Poll every 5ms for new data (faster for tests)
                try
                {
                    await Task.Delay(5, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
    }

    /// <summary>
    /// Manually enqueue data to simulate receiving from peer
    /// </summary>
    public void EnqueueReceivedData(byte[] data)
    {
        _receiveQueue.Enqueue(data);
        NotifyDataAvailable();
    }

    /// <summary>
    /// Get all sent data (for verification)
    /// </summary>
    public byte[]? DequeueSentData()
    {
        return _sendQueue.TryDequeue(out var data) ? data : null;
    }

    /// <summary>
    /// Dequeue a single received data item (for verification)
    /// </summary>
    public byte[]? DequeueReceivedData()
    {
        return _receiveQueue.TryDequeue(out var data) ? data : null;
    }

    /// <summary>
    /// Get count of items in send queue without dequeuing
    /// </summary>
    public int GetSentDataCount()
    {
        return _sendQueue.Count;
    }

    /// <summary>
    /// Get all sent data as a list (for verification)
    /// Uses history queue so it doesn't interfere with receive loop
    /// </summary>
    public System.Collections.Generic.List<byte[]> GetAllSentData()
    {
        var result = new System.Collections.Generic.List<byte[]>();
        while (_sentDataHistory.TryDequeue(out var data))
        {
            result.Add(data);
        }
        return result;
    }

    /// <summary>
    /// Get count of items in sent data history without dequeuing
    /// </summary>
    public int GetSentDataHistoryCount()
    {
        return _sentDataHistory.Count;
    }

    /// <summary>
    /// Get count of items in receive queue without dequeuing
    /// </summary>
    public int GetReceivedDataCount()
    {
        return _receiveQueue.Count;
    }

    /// <summary>
    /// Peek at the first item in receive queue without dequeuing it
    /// </summary>
    public byte[]? PeekReceivedData()
    {
        return _receiveQueue.TryPeek(out var data) ? data : null;
    }

    /// <summary>
    /// Get all received data as a list (for verification)
    /// This is what the peer sent to us
    /// </summary>
    public System.Collections.Generic.List<byte[]> GetAllReceivedData()
    {
        var result = new System.Collections.Generic.List<byte[]>();
        while (_receiveQueue.TryDequeue(out var data))
        {
            result.Add(data);
        }
        return result;
    }

    /// <summary>
    /// Gets statistics about data sent and received
    /// </summary>
    public ConnectionStatistics GetStatistics()
    {
        return new ConnectionStatistics
        {
            DataSentCount = _dataSentCount,
            DataReceivedCount = _dataReceivedCount,
            SendQueueCount = _sendQueue.Count,
            ReceiveQueueCount = _receiveQueue.Count
        };
    }

    public void Dispose()
    {
        DisconnectAsync().GetAwaiter().GetResult();
    }
}

/// <summary>
/// Statistics for InMemoryBidirectionalConnection
/// </summary>
public class ConnectionStatistics
{
    public int DataSentCount { get; set; }
    public int DataReceivedCount { get; set; }
    public int SendQueueCount { get; set; }
    public int ReceiveQueueCount { get; set; }
}

