//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs/RetroTerm
// RetroTerm — Connecting today's machines to yesterday's systems
//
using System;

using RetroTerm.Core.Protocols;

namespace RetroTerm.Core.Terminal.Emulators
{
    /// <summary>
    /// Raised by an emulator for a key that goes to a TN3270E host as a telnet command rather
    /// than as data.
    /// </summary>
    /// <param name="key">
    /// Which key.
    /// </param>
    public delegate void Tn3270EKeyHandler(Tn3270EKey key);

    /// <summary>
    /// The 3270 keys that a TN3270E connection sends as telnet commands.
    /// </summary>
    public enum Tn3270EKey
    {
        /// <summary>
        /// Sys Req: telnet Abort Output, with the SYSREQ function (RFC 2355 section 10.5.2).
        /// </summary>
        SysReq,

        /// <summary>
        /// Attn: telnet Interrupt Process (RFC 2355 section 11).
        /// </summary>
        Attention,

        /// <summary>
        /// Not a key: a printer that answered Intervention Required is ready again, and the
        /// connection sends REQUEST ERR-COND-CLEARED (RFC 2355 section 10.4.1). It goes the same
        /// way as the keys because it is the same thing, a message from the emulator that only
        /// the TN3270E connection can put on the wire.
        /// </summary>
        ErrorConditionCleared,
    }

    /// <summary>
    /// An emulator whose keys depend on whether the connection runs TN3270E.
    /// </summary>
    /// <remarks>
    /// The session calls <see cref="Tn3270EChanged"/> on its pump thread (SessionPump.cs, the
    /// thread rule) when a connection that implements Protocols.ITn3270EConnection changes state,
    /// and once with the current state when the connection is attached. It calls
    /// <see cref="Tn3270EHostMessageReceived"/> on the same thread, in order with the 3270
    /// records, and sends what <see cref="Tn3270EKeyPressed"/> asks for to the connection.
    /// </remarks>
    public interface ITn3270EAware
    {
        /// <summary>
        /// Raised on the pump thread for a key the connection must send as a telnet command.
        /// </summary>
        event Tn3270EKeyHandler? Tn3270EKeyPressed;

        /// <summary>
        /// TN3270E came into force or ended.
        /// </summary>
        /// <param name="on">
        /// True when TN3270E is in force now.
        /// </param>
        /// <param name="luName">
        /// The LU name the host gave, or null; shown on the 3270's line 25 as wc3270 shows it.
        /// </param>
        /// <param name="bindImage">
        /// True when the BIND-IMAGE function was agreed (RFC 2355 section 10.3).
        /// </param>
        /// <param name="sysReq">
        /// True when the SYSREQ function was agreed (RFC 2355 section 10.5).
        /// </param>
        /// <param name="responses">
        /// True when the RESPONSES function was agreed: the host hears how each record went (RFC
        /// 2355 section 10.4).
        /// </param>
        void Tn3270EChanged(bool on, string? luName, bool bindImage, bool sysReq, bool responses);

        /// <summary>
        /// A TN3270E message that is not 3270 data arrived (BIND image, UNBIND, SSCP-LU text,
        /// NVT text).
        /// </summary>
        /// <param name="kind">
        /// What it is.
        /// </param>
        /// <param name="data">
        /// Its data part, without the 5-byte header.
        /// </param>
        void Tn3270EHostMessageReceived(Tn3270EHostMessage kind, ReadOnlySpan<byte> data);
    }
}
