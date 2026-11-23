//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs/RetroTerm
// RetroTerm — Connecting today's machines to yesterday's systems
//

using RetroTerm.Core.Protocols;

namespace RetroTerm.Core.Terminal.Emulators
{
    /// <summary>
    /// An emulator that shows the state of the connection on its own screen.
    /// </summary>
    /// <remarks>
    /// The IBM 3270 does: line 25, the operator information area, says "X Not Connected" or shows
    /// the ready symbols (GA27-2890-4 p. 2-19, 2-20). The session calls
    /// <see cref="ConnectionStatusChanged"/> on its pump thread, the only thread allowed to change
    /// an emulator (SessionPump.cs, the thread rule), for every change: connect, a drop by the
    /// host, and a disconnect asked for by the user.
    /// </remarks>
    public interface IConnectionStatusAware
    {
        /// <summary>
        /// The connection changed state.
        /// </summary>
        /// <param name="status">
        /// The new state.
        /// </param>
        void ConnectionStatusChanged(ConnectionStatus status);
    }
}
