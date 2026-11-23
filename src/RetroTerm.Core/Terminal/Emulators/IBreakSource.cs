//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs/RetroTerm
// RetroTerm — Connecting today's machines to yesterday's systems
//

namespace RetroTerm.Core.Terminal.Emulators
{
    /// <summary>
    /// An emulator asks the connection to send a break.
    /// </summary>
    public delegate void BreakRequestedHandler();

    /// <summary>
    /// An emulator with a key that sends a break to the host rather than data.
    /// </summary>
    /// <remarks>
    /// The IBM 3270 Attn key: the PA1 short read, then a telnet break (x3270, Attn_action). The
    /// session hands the request to its connection when that connection is a
    /// <see cref="RetroTerm.Core.Protocols.IBreakConnection"/>, and logs it as dropped otherwise.
    /// The event is raised on the session's pump thread, after any data the same key sent, so the
    /// host gets the data first.
    /// </remarks>
    public interface IBreakSource
    {
        /// <summary>
        /// Raised when the emulator wants a break sent.
        /// </summary>
        event BreakRequestedHandler? BreakRequested;
    }
}
