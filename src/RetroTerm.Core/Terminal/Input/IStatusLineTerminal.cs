//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs/RetroTerm
// RetroTerm — Connecting today's machines to yesterday's systems
//

namespace RetroTerm.Core.Terminal.Input
{
    /// <summary>
    /// A block-mode terminal with a status line and a keyboard the host locks: the IBM 3270.
    /// </summary>
    /// <remarks>
    /// Read by the STATUSLINE and WAITUNLOCK commands (scripts and MCP alike). Both members are
    /// read on the session's pump thread, the one thread allowed to touch an emulator
    /// (SessionPump.cs, the thread rule).
    /// </remarks>
    public interface IStatusLineTerminal
    {
        /// <summary>
        /// Gets why the keyboard is locked, in words ("waiting for the host", "System Lock",
        /// "operator error"), or null when it is not locked.
        /// </summary>
        string? KeyboardLockReason { get; }

        /// <summary>
        /// Gets the status line as drawn (the 3270's line 25).
        /// </summary>
        string DrawnStatusLine { get; }
    }
}
