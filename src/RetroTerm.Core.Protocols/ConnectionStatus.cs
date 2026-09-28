namespace RetroTerm.Core.Protocols;

/// <summary>
/// Represents the current status of a connection
/// </summary>
public enum ConnectionStatus
{
    /// <summary>
    /// Not connected
    /// </summary>
    Disconnected,

    /// <summary>
    /// Attempting to establish connection
    /// </summary>
    Connecting,

    /// <summary>
    /// Successfully connected and ready for data transfer
    /// </summary>
    Connected,

    /// <summary>
    /// Connection is being closed
    /// </summary>
    Disconnecting,

    /// <summary>
    /// Connection failed or encountered an error
    /// </summary>
    Error
}

