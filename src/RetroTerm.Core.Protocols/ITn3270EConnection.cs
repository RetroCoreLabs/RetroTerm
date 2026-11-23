//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs/RetroTerm
// RetroTerm — Connecting today's machines to yesterday's systems
//
using System;

namespace RetroTerm.Core.Protocols;

/// <summary>
/// A connection that can run TN3270E (RFC 2355), and says when it is in force.
/// </summary>
/// <remarks>
/// Some 3270 keys work differently once TN3270E is agreed: x3270 sends no PA1 and no break for
/// Attn, and Sys Req goes as a telnet command rather than an AID (Common\kybd.c, Attn_action and
/// SysReq_action). The session passes the state on to an emulator that implements
/// Terminal.Emulators.ITn3270EAware, the same way it passes the connection status.
/// </remarks>
public interface ITn3270EConnection
{
    /// <summary>
    /// Raised when TN3270E comes into force or ends; the argument is the new state. It may be
    /// raised on the connection's receive thread.
    /// </summary>
    event Action<bool>? Tn3270EChanged;

    /// <summary>
    /// Gets a value indicating whether TN3270E is in force now.
    /// </summary>
    bool InTn3270E { get; }
}
