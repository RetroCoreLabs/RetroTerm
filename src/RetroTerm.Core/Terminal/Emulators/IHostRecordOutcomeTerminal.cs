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
    /// An emulator that takes the host's data one whole record at a time and can say how
    /// applying the last one went.
    /// </summary>
    /// <remarks>
    /// The session reads <see cref="LastRecordOutcome"/> on its pump thread right after
    /// ProcessData returns, and hands it to a connection that implements
    /// Protocols.IHostRecordResponseConnection (TN3270E RESPONSES, RFC 2355 section 10.4.1).
    /// </remarks>
    public interface IHostRecordOutcomeTerminal
    {
        /// <summary>
        /// Gets how the record given to the last ProcessData call went.
        /// </summary>
        HostRecordOutcome LastRecordOutcome { get; }
    }
}
