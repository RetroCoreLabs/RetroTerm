//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs/RetroTerm
// RetroTerm — Connecting today's machines to yesterday's systems
//
namespace RetroTerm.Core.Protocols;

/// <summary>
/// What happened when the terminal applied one record from the host.
/// </summary>
/// <remarks>
/// <para>
/// The kinds are the ones a TN3270E negative response can carry (RFC 2355 section 10.4.1):
/// Command Reject and Operation Check for any terminal, and Intervention Required ("Printer is not
/// ready") for a printer. Component Disconnected ("Printer is powered off or not connected") has
/// no kind here: a printer that answers the host is connected by definition.
/// </para>
/// <para>
/// Which error is which comes from GA23-0059-4 Appendix A: "For non-SNA environments, all 3270
/// data stream errors are rejected with an Op-Check (OC) except for invalid commands.
/// Unsupported commands are rejected with Command Reject (CR)." RFC 2355 gives the SNA sense code
/// a server makes of each: X'1003' (function not supported) for Command Reject, X'1005'
/// (parameter error) for Operation Check.
/// </para>
/// </remarks>
public enum HostRecordOutcome
{
    /// <summary>
    /// The record was applied without an error.
    /// </summary>
    Applied,

    /// <summary>
    /// The record's command code is not a 3270 command this terminal has (Command Reject).
    /// </summary>
    CommandReject,

    /// <summary>
    /// The command is known, but something in the record was refused: an order, a buffer
    /// address, a structured field (Operation Check).
    /// </summary>
    OperationCheck,

    /// <summary>
    /// A printer that is not ready refused the record without printing it (Intervention
    /// Required; RFC 2355 section 10.4.1 gives SNA sense X'08020000'). GA23-0061-2 p. 4-26, the
    /// Intervention Required sense bit: "A command attemped [sic] to start a printer but found it
    /// not ready. The printout is suppressed."
    /// </summary>
    InterventionRequired,
}

/// <summary>
/// A connection that answers the host for each record, once the terminal has applied it.
/// </summary>
/// <remarks>
/// <para>
/// TN3270E with the RESPONSES function (RFC 2355 section 10.4.1): "the receiver must attempt to
/// process the data in the data portion of the message, then determine whether or not it should
/// send a data message with a DATA-TYPE of RESPONSE." The connection cannot see the outcome; the
/// emulator can, on the session thread. So the session tells the connection when it starts
/// applying a record and how it went.
/// </para>
/// <para>
/// The connection raises DataReceived exactly once per host record, in order, and the session
/// calls <see cref="HostRecordStarting"/> and then <see cref="HostRecordProcessed"/> once for
/// each of those raises, in the same order, on its pump thread (SessionPump.cs, the thread
/// rule). Anything the emulator sends between the two calls is a reply to that record.
/// </para>
/// </remarks>
public interface IHostRecordResponseConnection
{
    /// <summary>
    /// The session is about to apply the next host record.
    /// </summary>
    void HostRecordStarting();

    /// <summary>
    /// The session has applied the record that <see cref="HostRecordStarting"/> began.
    /// </summary>
    /// <param name="outcome">
    /// How it went, or null when nothing could say (the emulator does not report outcomes, or it
    /// threw). With null the connection sends no response and says so.
    /// </param>
    void HostRecordProcessed(HostRecordOutcome? outcome);
}
