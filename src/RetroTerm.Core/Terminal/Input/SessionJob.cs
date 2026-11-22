//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs/RetroTerm
// RetroTerm — Connecting today's machines to yesterday's systems
//

namespace RetroTerm.Core.Terminal.Input
{
    /// <summary>
    /// A change to an emulator that must run on the session thread (SessionPump.cs, the thread
    /// rule): for example, a key typing into a 3270 field.
    /// </summary>
    public delegate void SessionJob();

    /// <summary>
    /// Runs a <see cref="SessionJob"/> on the session thread. Set by TerminalSession on an
    /// emulator that handles its own keys (<see cref="ILocalKeyboard.SessionThread"/>).
    /// </summary>
    /// <param name="job">
    /// The change to run.
    /// </param>
    public delegate void SessionThreadRunner(SessionJob job);
}
