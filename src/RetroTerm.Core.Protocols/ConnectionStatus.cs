//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs/RetroTerm
// RetroTerm — Connecting today's machines to yesterday's systems
//

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

