using System;
using System.Threading;
using System.Threading.Tasks;

namespace RetroTerm.Core.Protocols;

/// <summary>
/// Interface for all connection types (Telnet, SSH, Serial, WebSocket, Memory-based)
/// Provides a unified API for connecting, sending, receiving, and disconnecting
/// </summary>
public interface IConnection : IDisposable
{
    /// <summary>
    /// Gets the current connection status
    /// </summary>
    ConnectionStatus Status { get; }

    /// <summary>
    /// Gets whether the connection is currently active
    /// </summary>
    bool IsConnected => Status == ConnectionStatus.Connected;

    /// <summary>
    /// Gets the connection type name (e.g., "Telnet", "SSH", "Serial")
    /// </summary>
    string ConnectionType { get; }

    /// <summary>
    /// Gets a human-readable description of the connection
    /// </summary>
    string Description { get; }

    /// <summary>
    /// Event raised when the connection status changes
    /// </summary>
    event Action<ConnectionStatus>? StatusChanged;

    /// <summary>
    /// Event raised when data is received from the remote host
    /// </summary>
    event Action<ReadOnlyMemory<byte>>? DataReceived;

    /// <summary>
    /// Event raised when an error occurs
    /// </summary>
    event Action<Exception>? ErrorOccurred;

    /// <summary>
    /// Establishes the connection asynchronously
    /// </summary>
    Task ConnectAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends data to the remote host asynchronously
    /// </summary>
    Task SendAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default);

    /// <summary>
    /// Disconnects from the remote host asynchronously
    /// </summary>
    Task DisconnectAsync();

    /// <summary>
    /// Tells the host the terminal is now this many columns and rows.
    /// </summary>
    /// <remarks>
    /// Declared HERE rather than on each protocol, because it is one behaviour and it had already
    /// grown two shapes: Telnet called it <c>UpdateWindowSizeAsync(width, height)</c> and SSH called
    /// it <c>ResizeTerminal(columns, rows)</c>, and neither had a caller. A window-size change has
    /// to reach whatever is on the other end, whichever protocol that is, so the question is asked
    /// in one place.
    /// The default does nothing, which is the truth for a connection with no way to say it - a
    /// serial line has no window-size message, and neither does an in-memory test connection.
    /// </remarks>
    /// <param name="columns">
    /// New width in character cells.
    /// </param>
    /// <param name="rows">
    /// New height in character cells.
    /// </param>
    /// <param name="cancellationToken">
    /// Cancels the send.
    /// </param>
    /// <returns>
    /// A task that completes once the host has been told, or immediately when there is nothing to
    /// tell it.
    /// </returns>
    Task ResizeTerminalAsync(int columns, int rows, CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}

/// <summary>
/// Interface for connections that support runtime serial port reconfiguration.
/// Used by OPCOM binary transfer to switch between 7E1 (terminal) and 8N1 (binary data).
/// </summary>
public interface ISerialConfigurable
{
    /// <summary>
    /// Reconfigures the serial port parameters at runtime.
    /// dataBits: 7 or 8. parity: 0=None, 2=Even. stopBits: 1 or 2.
    /// </summary>
    void ReconfigureSerial(int dataBits, int parity, int stopBits);
}

