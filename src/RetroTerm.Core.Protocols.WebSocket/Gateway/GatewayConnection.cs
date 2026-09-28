using System;
using System.Threading;
using System.Threading.Tasks;

namespace RetroTerm.Core.Protocols.WebSocket.Gateway;

/// <summary>
/// Per-terminal IConnection implementation for the ND-100 Gateway protocol.
/// Each instance represents a connection to a single terminal device (identCode)
/// routed through the shared GatewayListener WebSocket connection.
/// </summary>
public class GatewayConnection : IConnection
{
    private readonly GatewayListener _listener;
    private readonly int _identCode;
    private readonly string _terminalName;
    private ConnectionStatus _status = ConnectionStatus.Disconnected;
    private bool _disposed;

    public GatewayConnection(GatewayListener listener, int identCode, string terminalName)
    {
        _listener = listener ?? throw new ArgumentNullException(nameof(listener));
        _identCode = identCode;
        _terminalName = terminalName ?? string.Empty;
    }

    /// <summary>
    /// Gets the identCode this connection is bound to.
    /// </summary>
    public int IdentCode => _identCode;

    public ConnectionStatus Status => _status;

    public string ConnectionType => "ND-100 Gateway";

    public string Description => $"{_terminalName} (identCode {_identCode})";

    public event Action<ConnectionStatus>? StatusChanged;
    public event Action<ReadOnlyMemory<byte>>? DataReceived;
    public event Action<Exception>? ErrorOccurred;

    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        if (_status != ConnectionStatus.Disconnected)
            throw new InvalidOperationException($"Cannot connect: current status is {_status}");

        if (!_listener.IsEmulatorConnected)
            throw new InvalidOperationException("No emulator is connected to the gateway");

        SetStatus(ConnectionStatus.Connecting);

        // Try to register with the listener (exclusive access)
        if (!_listener.TryRegisterConnection(_identCode, this))
        {
            SetStatus(ConnectionStatus.Error);
            throw new InvalidOperationException($"Terminal identCode {_identCode} is already in use");
        }

        // Notify the emulator that a client connected to this terminal
        await _listener.SendClientConnectedAsync(_identCode);

        SetStatus(ConnectionStatus.Connected);
    }

    public async Task SendAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
    {
        if (_status != ConnectionStatus.Connected)
        {
            System.Diagnostics.Debug.WriteLine($"[GatewayConnection] SendAsync REJECTED: status={_status} (not Connected)");
            throw new InvalidOperationException("Not connected");
        }

        System.Diagnostics.Debug.WriteLine($"[GatewayConnection] SendAsync id={_identCode} {data.Length}B");
        await _listener.SendTermInputAsync(_identCode, data);
    }

    public async Task DisconnectAsync()
    {
        if (_status == ConnectionStatus.Disconnected)
            return;

        SetStatus(ConnectionStatus.Disconnecting);

        _listener.UnregisterConnection(_identCode);

        // Notify emulator if it's still connected
        if (_listener.IsEmulatorConnected)
        {
            try
            {
                await _listener.SendClientDisconnectedAsync(_identCode);
            }
            catch { /* ignore send errors during disconnect */ }
        }

        SetStatus(ConnectionStatus.Disconnected);
    }

    /// <summary>
    /// Called by GatewayListener when term-output data arrives for this identCode.
    /// </summary>
    internal void OnDataFromEmulator(byte[] data)
    {
        if (_status == ConnectionStatus.Connected)
        {
            DataReceived?.Invoke(new ReadOnlyMemory<byte>(data));
        }
    }

    /// <summary>
    /// Called by GatewayListener when the emulator WebSocket disconnects.
    /// </summary>
    internal void OnEmulatorDisconnected()
    {
        if (_status == ConnectionStatus.Connected || _status == ConnectionStatus.Connecting)
        {
            _listener.UnregisterConnection(_identCode);
            SetStatus(ConnectionStatus.Disconnected);
            ErrorOccurred?.Invoke(new InvalidOperationException("Emulator disconnected"));
        }
    }

    private void SetStatus(ConnectionStatus newStatus)
    {
        if (_status != newStatus)
        {
            _status = newStatus;
            StatusChanged?.Invoke(newStatus);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_status == ConnectionStatus.Connected || _status == ConnectionStatus.Connecting)
        {
            _listener.UnregisterConnection(_identCode);
            SetStatus(ConnectionStatus.Disconnected);
        }
    }
}
