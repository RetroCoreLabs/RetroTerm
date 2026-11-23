//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs/RetroTerm
// RetroTerm — Connecting today's machines to yesterday's systems
//
namespace RetroTerm.Core.Terminal.Emulators
{
    /// <summary>
    /// An emulator that is a printer and wants to hear when the host ends a print job.
    /// </summary>
    /// <remarks>
    /// The session calls <see cref="EndOfJobReceived"/> on its pump thread (SessionPump.cs, the
    /// thread rule), after every record that arrived before the signal, when a connection that
    /// implements Protocols.IPrintJobEndConnection raises it.
    /// </remarks>
    public interface IPrintJobEndAware
    {
        /// <summary>
        /// The host marked the end of the current print job.
        /// </summary>
        void EndOfJobReceived();
    }
}
