//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs/RetroTerm
// RetroTerm — Connecting today's machines to yesterday's systems
//
namespace RetroTerm.Core.Terminal.Emulators
{
    /// <summary>
    /// An emulator whose keys depend on whether the connection runs TN3270E.
    /// </summary>
    /// <remarks>
    /// The session calls <see cref="Tn3270EChanged"/> on its pump thread (SessionPump.cs, the
    /// thread rule) when a connection that implements Protocols.ITn3270EConnection changes state,
    /// and once with the current state when the connection is attached.
    /// </remarks>
    public interface ITn3270EAware
    {
        /// <summary>
        /// TN3270E came into force or ended.
        /// </summary>
        /// <param name="on">
        /// True when TN3270E is in force now.
        /// </param>
        /// <param name="luName">
        /// The LU name the host gave, or null; shown on the 3270's line 25 as wc3270 shows it.
        /// </param>
        void Tn3270EChanged(bool on, string? luName);
    }
}
