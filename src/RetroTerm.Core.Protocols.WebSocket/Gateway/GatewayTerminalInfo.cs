//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs/RetroTerm
// RetroTerm — Connecting today's machines to yesterday's systems
//

namespace RetroTerm.Core.Protocols.WebSocket.Gateway;

/// <summary>
/// Represents a terminal device registered by the ND-100 emulator via the Gateway protocol.
/// </summary>
public class GatewayTerminalInfo
{
    /// <summary>
    /// The unique identification code for this terminal (e.g., 43, 44, 45).
    /// Maps to a physical terminal device on the ND-100.
    /// </summary>
    public int IdentCode { get; set; }

    /// <summary>
    /// Human-readable terminal name (e.g., "TERMINAL 12").
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// The ND-100 logical device number for this terminal.
    /// </summary>
    public int LogicalDevice { get; set; }
}
