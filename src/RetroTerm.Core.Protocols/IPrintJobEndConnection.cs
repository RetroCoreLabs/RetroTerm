//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs/RetroTerm
// RetroTerm — Connecting today's machines to yesterday's systems
//
using System;

namespace RetroTerm.Core.Protocols;

/// <summary>
/// A connection on which the host can say "this print job is finished" in the protocol itself.
/// </summary>
/// <remarks>
/// In plain TN3270 the signal is telnet Abort Output (IAC AO): x3270's pr3287 ends the print job
/// when it arrives in 3270 mode without TN3270E (Common\pr3287\telnet.c, telnet_fsm, case AO).
/// The session passes it on, in order with the data before it, to an emulator that implements
/// Terminal.Emulators.IPrintJobEndAware.
/// </remarks>
public interface IPrintJobEndConnection
{
    /// <summary>
    /// Raised when the host marked the end of a print job. It may be raised on the connection's
    /// receive thread.
    /// </summary>
    event Action? EndOfJobReceived;
}
