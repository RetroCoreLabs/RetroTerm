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

    /// <summary>
    /// Gets the LU name the host gave in TN3270E (DEVICE-TYPE IS ... CONNECT), or null.
    /// </summary>
    /// <remarks>
    /// Set before <see cref="Tn3270EChanged"/> is raised with true, and cleared before it is
    /// raised with false.
    /// </remarks>
    string? GrantedLuName { get; }

    /// <summary>
    /// Gets a value indicating whether the BIND-IMAGE function was agreed: the host says when an
    /// SNA session with an application starts and ends (RFC 2355 section 10.3).
    /// </summary>
    /// <remarks>
    /// Set before <see cref="Tn3270EChanged"/> is raised with true.
    /// </remarks>
    bool BindImageAgreed { get; }

    /// <summary>
    /// Gets a value indicating whether the SYSREQ function was agreed: the Sys Req key goes to
    /// the host as telnet Abort Output (RFC 2355 section 10.5.2).
    /// </summary>
    /// <remarks>
    /// Set before <see cref="Tn3270EChanged"/> is raised with true.
    /// </remarks>
    bool SysReqAgreed { get; }

    /// <summary>
    /// Raised for a TN3270E message that is not 3270 data: a BIND image, an UNBIND, SSCP-LU text
    /// or NVT text (RFC 2355 section 8.1.1). The memory is the message's data part, without the
    /// 5-byte header, and stays valid after the call. Raised on the receive thread, in order
    /// with the 3270 records of <see cref="IConnection.DataReceived"/>.
    /// </summary>
    event Action<Tn3270EHostMessage, ReadOnlyMemory<byte>>? HostMessageReceived;

    /// <summary>
    /// Sends the Sys Req key: telnet Abort Output, when the SYSREQ function was agreed (RFC 2355
    /// section 10.5.2). Does nothing otherwise, and says so in the log.
    /// </summary>
    void SendSysReq();

    /// <summary>
    /// Sends the Attn key in TN3270E: telnet Interrupt Process (RFC 2355 section 11).
    /// </summary>
    void SendAttention();
}

/// <summary>
/// The kinds of TN3270E host message that carry something other than 3270 data (RFC 2355
/// section 8.1.1, the DATA-TYPE codes).
/// </summary>
public enum Tn3270EHostMessage
{
    /// <summary>
    /// BIND-IMAGE (X'03'): an SNA session with a host application has started; the data is the
    /// BIND (RFC 2355 section 10.3).
    /// </summary>
    BindImage,

    /// <summary>
    /// UNBIND (X'04'): that session has ended; the data is the reason code, X'01' for a normal
    /// end (RFC 2355 section 10.3), or nothing.
    /// </summary>
    Unbind,

    /// <summary>
    /// SSCP-LU-DATA (X'07'): text from the host's control program on the SSCP-LU session (RFC
    /// 2355 section 10.5).
    /// </summary>
    SscpLuData,

    /// <summary>
    /// NVT-DATA (X'05'): plain line-mode text (RFC 2355 section 9.1).
    /// </summary>
    NvtData,
}
