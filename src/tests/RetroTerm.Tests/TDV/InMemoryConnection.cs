using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using RetroTerm.Core.Protocols;

namespace RetroTerm.Tests.TDV;

/// <summary>
/// In-memory connection for testing - simulates a bidirectional connection
/// </summary>
public class InMemoryConnection : IConnection, IDisposable
{
    private readonly Queue<byte[]> _receivedData = new();
    private readonly List<byte[]> _sentData = new();
    private ConnectionStatus _status = ConnectionStatus.Disconnected;

    public ConnectionStatus Status => _status;
    public string ConnectionType => "InMemory";
    public string Description => "In-Memory Test Connection";
    public bool IsConnected => _status == ConnectionStatus.Connected;

    public event Action<ConnectionStatus>? StatusChanged;
    public event Action<ReadOnlyMemory<byte>>? DataReceived;
    public event Action<Exception>? ErrorOccurred;

    public Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        _status = ConnectionStatus.Connected;
        StatusChanged?.Invoke(_status);
        return Task.CompletedTask;
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
        _sentData.Add(bytes);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Simulates receiving data from the "server" side
    /// </summary>
    public void SimulateReceive(byte[] data)
    {
        if (_status == ConnectionStatus.Connected)
        {
            DataReceived?.Invoke(data);
        }
    }

    /// <summary>
    /// Gets all data that was sent through this connection
    /// </summary>
    public IReadOnlyList<byte[]> GetSentData() => _sentData;

    /// <summary>
    /// Gets the last sent data as a string
    /// </summary>
    public string? GetLastSentAsString()
    {
        if (_sentData.Count == 0) return null;
        return Encoding.UTF8.GetString(_sentData[_sentData.Count - 1]);
    }

    /// <summary>
    /// Clears all sent data history
    /// </summary>
    public void ClearSentData()
    {
        _sentData.Clear();
    }

    /// <summary>
    /// Simulates an error occurring on the connection
    /// </summary>
    public void SimulateError(Exception error)
    {
        ErrorOccurred?.Invoke(error);
    }

    /// <summary>
    /// Simulates the REMOTE side dropping the line — the status change a real connection
    /// raises from its receive loop, as opposed to <see cref="DisconnectAsync"/> which is
    /// the deliberate local path (the session unsubscribes before calling that one).
    /// </summary>
    public void SimulateRemoteDrop()
    {
        _status = ConnectionStatus.Disconnected;
        StatusChanged?.Invoke(_status);
    }

    /// <summary>
    /// Whether Dispose has run — what the connection-lifetime tests assert on, because a
    /// dropped SERIAL connection that is never disposed keeps the COM port open and every
    /// later open fails with access denied.
    /// </summary>
    public bool IsDisposed { get; private set; }

    public void Dispose()
    {
        IsDisposed = true;
    }
}

