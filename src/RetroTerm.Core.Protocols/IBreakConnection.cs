//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs/RetroTerm
// RetroTerm — Connecting today's machines to yesterday's systems
//

namespace RetroTerm.Core.Protocols;

/// <summary>
/// A connection that can send a break: a signal to the host outside the data.
/// </summary>
/// <remarks>
/// On telnet that is IAC BRK (RFC 854). The TN3270 connection sends it for the 3270 Attn key,
/// as x3270 does in plain TN3270 mode. The session calls <see cref="SendBreak"/> when an
/// emulator asks for one (Terminal.Emulators.IBreakSource).
/// </remarks>
public interface IBreakConnection
{
    /// <summary>
    /// Sends a break to the host.
    /// </summary>
    void SendBreak();
}
