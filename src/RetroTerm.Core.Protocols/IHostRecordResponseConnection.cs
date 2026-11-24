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
/// ready") and Component Disconnected ("Printer is powered off or not connected") for a printer.
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

    /// <summary>
    /// The printer could not be reached at all, so the record was not printed (Component
    /// Disconnected; RFC 2355 section 10.4.1, status X'03', "Printer is powered off or not
    /// connected", SNA sense X'08310000'). GA23-0061-2 p. 5-66, sense X'31' LU Component
    /// Disconnected: "This response is returned if the device attached to the 3274 cannot be
    /// contacted by a device poll. This is due to device power off, cable detached from the
    /// controller port, or connecting cable broken."
    /// </summary>
    /// <remarks>
    /// RetroTerm's own 3287 never reports this. The 3274 and the printer there are two boxes and a
    /// cable; here the TN3270E client IS the printer, so while it can answer the host at all it
    /// has been "contacted", and every failure it has (a job that cannot be written) is one the
    /// printer itself took and holds, which is Intervention Required. Implemented on the
    /// connection side because the RFC defines its bytes and its ERR-COND-CLEARED follow-up.
    /// </remarks>
    ComponentDisconnected,
}

/// <summary>
/// What the host asked to hear about one 3270-DATA or SCS-DATA record: its TN3270E RESPONSE-FLAG
/// (RFC 2355 section 8.1.3).
/// </summary>
/// <remarks>
/// A record that cannot be answered is <see cref="NoResponse"/>: a plain TN3270 record, a flag
/// asked for while RESPONSES was not agreed, or a flag the RFC does not define. The connection
/// says why in each of those cases.
/// </remarks>
public enum HostRecordResponseRequest
{
    /// <summary>
    /// NO-RESPONSE (X'00'): "The receiver must therefore not send any response to this
    /// data-message." The host will never hear how this record went.
    /// </summary>
    NoResponse,

    /// <summary>
    /// ERROR-RESPONSE (X'01'): a negative response only if the record failed.
    /// </summary>
    ErrorResponse,

    /// <summary>
    /// ALWAYS-RESPONSE (X'02'): a negative response on an error, a positive one otherwise.
    /// </summary>
    AlwaysResponse,
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
    /// <returns>
    /// What the host asked to hear about this record. The session hands it to the emulator before
    /// the record, because what a not-ready printer should do with a record depends on whether
    /// the host can be told (Core.Terminal.Emulators.IHostRecordOutcomeTerminal).
    /// </returns>
    HostRecordResponseRequest HostRecordStarting();

    /// <summary>
    /// The session has applied the record that <see cref="HostRecordStarting"/> began.
    /// </summary>
    /// <param name="outcome">
    /// How it went, or null when nothing could say (the emulator does not report outcomes, or it
    /// threw). With null the connection sends no response and says so.
    /// </param>
    void HostRecordProcessed(HostRecordOutcome? outcome);
}
