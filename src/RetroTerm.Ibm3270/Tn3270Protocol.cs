//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs/RetroTerm
// RetroTerm — Connecting today's machines to yesterday's systems
//

using System;
using System.Buffers;
using System.Collections.Generic;
using System.Text;

using RetroIO.Protocols.Telnet;
using RetroIO.Protocols.Tn3270;
using RetroTerm.Core.Protocols;

namespace RetroTerm.Ibm3270
{
    /// <summary>
    /// Plain TN3270 (RFC 1576) on top of the RetroIO telnet library, with no socket: bytes from the
    /// host go in through <see cref="Feed"/>, bytes for the host come out through the write callback.
    /// </summary>
    /// <remarks>
    /// <para>
    /// What it agrees to, and why (RetroTerm docs\spec\IBM3270\TN3270-NEGOTIATION.md section 3, the
    /// bytes measured against Hercules TK5 on 22 November 2026): TERMINAL-TYPE on our side, answered
    /// with <see cref="DefaultTerminalType"/>; BINARY and END-OF-RECORD in both directions; and
    /// TN3270E on our side. Everything else is refused.
    /// </para>
    /// <para>
    /// TN3270E (RFC 2355) asks for BIND-IMAGE, RESPONSES and SYSREQ (sections 10.3, 10.4, 10.5),
    /// the three x3270 asks for (Common\telnet.c, tn3270e_request), takes whatever subset the host
    /// grants, and basic TN3270E (section 9, no functions) when it grants none. BIND-IMAGE, UNBIND,
    /// SSCP-LU-DATA and NVT-DATA messages are raised through <see cref="HostMessageReceived"/>, and
    /// our own records carry the data type of the mode the connection is in (3270, SSCP-LU or
    /// NVT). Built 24 November 2026; no host on this machine offers TN3270E (TK5 measured again
    /// that day: FF FD 18 first, never FF FD 28), so the bytes come from the RFC. Every record
    /// carries a 5-byte header. With RESPONSES, each host record's
    /// RESPONSE-FLAG and SEQ-NUMBER are kept until the session has applied the record
    /// (<see cref="HostRecordStarting"/>, <see cref="HostRecordProcessed"/>), and then a positive
    /// or negative response is sent as section 10.4.1 says; our own 3270-DATA messages carry a
    /// counting sequence number. RESPONSES built 23 November 2026 from the RFC and GA23-0059-4
    /// Appendix A, checked against x3270 Common\telnet.c (process_eor, net_output, tn3270e_ack,
    /// tn3270e_nak). Basic TN3270E built 23 November 2026 from the RFC and checked against
    /// x3270 (Common\telnet.c, tn3270e_negotiate, process_eor, net_output) and tnz (tnz\tnz.py); TK5
    /// never offers TN3270E, so no live host has tested it. Where the two disagree this follows
    /// x3270: TN3270E counts as agreed only when the function lists match (RFC 2355 section 7.2,
    /// "once this command is received by the other side, all necessary negotiation has been
    /// completed"), whereas tnz takes it as agreed at DEVICE-TYPE IS; and a refused device type
    /// backs off to plain TN3270 with WONT TN3270E (x3270 backoff_tn3270e), whereas tnz stops with
    /// an error.
    /// </para>
    /// <para>
    /// A printer (device type IBM-3287-1) asks for SCS-CTL-CODES, DATA-STREAM-CTL and RESPONSES
    /// instead (RFC 2355 sections 10.1, 10.2, 10.4), may ask for the partner printer of a display
    /// with ASSOCIATE (7.1.3), takes SCS-DATA (raised through HostMessageReceived as ScsData and
    /// answered like 3270-DATA, 10.4.1) and PRINT-EOJ (PrintEndOfJobReceived), and gives up
    /// TN3270E with WONT when neither printer function is left (7.2.1). Built 24 November 2026
    /// from the RFC alone and tested against an in-process host that follows it; no real TN3270E
    /// host has seen it.
    /// </para>
    /// <para>
    /// 3270 mode starts when BINARY and EOR are on in both directions (RFC 1576 section 3). From then
    /// on every record that ends with IAC EOR is raised as <see cref="RecordReceived"/>. Host data
    /// that arrives outside 3270 mode (NVT text) is not shown anywhere yet, so it is reported through
    /// <see cref="ProtocolError"/> instead of being dropped without a word.
    /// </para>
    /// </remarks>
    public sealed class Tn3270Protocol : ITelnetReaderHandler, ITelnetOptionPolicy
    {
        private readonly Action<ReadOnlyMemory<byte>> _write;
        private readonly byte[] _terminalType;
        private readonly TelnetReader _reader;
        private readonly TelnetOptionNegotiator _negotiator;
        private readonly ArrayBufferWriter<byte> _record = new ArrayBufferWriter<byte>(4096);
        private readonly ArrayBufferWriter<byte> _out = new ArrayBufferWriter<byte>(256);

        // The TN3270E DEVICE-TYPE asked for (the terminal type up to any "@"), and the LU name
        // asked for with CONNECT (the part after "@"), or null for any free one (RFC 2355 7.1.2).
        private readonly byte[] _deviceType;
        private readonly byte[]? _luName;

        // The display LU whose partner printer this printer session asks for with ASSOCIATE
        // (RFC 2355 7.1.3), or null. Only for a printer device type.
        private readonly byte[]? _associateLu;

        // True when the device type is the printer, IBM-3287-1 (RFC 2355 7.1): the functions asked
        // for and the data types accepted are the printer's.
        private readonly bool _isPrinter;

        // The functions this terminal asks for: FunctionsWeAskFor for a display,
        // PrinterFunctionsWeAskFor for a printer.
        private readonly int _functionsWeAskFor;

        // True once FUNCTIONS IS has settled the function list: TN3270E is in force.
        private bool _tn3270eNegotiated;

        // The TN3270E functions still wanted, one bit per function code (bit 2 = RESPONSES).
        // Starts as RESPONSES alone and only ever shrinks during one negotiation, which is the
        // RFC 2355 7.2.1 rule against loops: never add back a function the other side removed
        // (x3270 tn3270e_negotiate narrows e_funcs the same way).
        private int _wantedFunctions;

        // The functions agreed by FUNCTIONS IS; only meaningful while _tn3270eNegotiated.
        private int _agreedFunctions;

        // Our next outbound 3270-DATA sequence number. RFC 2355 10.4: counted only with RESPONSES,
        // from zero, one per 3270-DATA message, back to zero after 32767.
        private ushort _sendSequence;

        // One entry per record handed up through RecordReceived, in order, so the session's
        // HostRecordStarting/HostRecordProcessed calls (one pair per record) find the header of
        // the record they are about. Plain TN3270 records get an entry too, asking for nothing,
        // so the two can never fall out of step.
        private readonly Queue<PendingRecord> _pending = new Queue<PendingRecord>();

        // The record the session is applying now, between HostRecordStarting and
        // HostRecordProcessed, and whether a response to it has gone already.
        private PendingRecord _current;
        private bool _applying;
        private bool _answered;

        // The TN3270E subnegotiation command codes (RFC 2355 section 3; x3270 include\tn3270e.h).
        private const byte OpAssociate = 0x00;
        private const byte OpConnect = 0x01;
        private const byte OpDeviceType = 0x02;
        private const byte OpFunctions = 0x03;
        private const byte OpIs = 0x04;
        private const byte OpReason = 0x05;
        private const byte OpReject = 0x06;
        private const byte OpRequest = 0x07;
        private const byte OpSend = 0x08;

        // TN3270E function codes (RFC 2355 section 3; RetroIO Tn3270EFunction).
        private const byte FunctionBindImage = Tn3270EFunction.BindImage;
        private const byte FunctionDataStreamControl = Tn3270EFunction.DataStreamControl;
        private const byte FunctionResponses = Tn3270EFunction.Responses;
        private const byte FunctionScsControlCodes = Tn3270EFunction.ScsControlCodes;
        private const byte FunctionSysReq = Tn3270EFunction.SysReq;

        // What a display asks for: BIND-IMAGE, RESPONSES and SYSREQ, as x3270 does for a
        // display.
        private const int FunctionsWeAskFor = (1 << FunctionBindImage) | (1 << FunctionResponses) | (1 << FunctionSysReq);

        // What a printer asks for: SCS-CTL-CODES and DATA-STREAM-CTL, "either ... or both, must
        // be negotiated" for a printer (RFC 2355 10.1, 10.2), both so the server may bind it to an
        // application of either kind, and RESPONSES, as the RFC's own printer examples ask
        // (section 13.4, "FUNCTIONS REQUEST SCS-CTL-CODES RESPONSES"). Not BIND-IMAGE: nothing in
        // a printout depends on the BIND, and with it agreed no data may come before a BIND-IMAGE
        // (10.3). Not SYSREQ: it is a key, and a printer has no keyboard (10.5).
        private const int PrinterFunctionsWeAskFor = (1 << FunctionScsControlCodes) | (1 << FunctionDataStreamControl) | (1 << FunctionResponses);

        // A printer session needs one of these two (RFC 2355 7.2.1, the impasse rule).
        private const int PrinterDataFunctions = (1 << FunctionScsControlCodes) | (1 << FunctionDataStreamControl);

        /// <summary>
        /// The TN3270E device type of a printer (RFC 2355 7.1).
        /// </summary>
        public const string PrinterDeviceType = "IBM-3287-1";

        // Which data the connection carries now (RFC 2355 sections 9.1, 10.3, 10.5; x3270 keeps
        // the same four states, Common\telnet.c tn3270e_submode).
        private Tn3270ESubmode _submode;

        // True between a BIND-IMAGE and the next UNBIND (RFC 2355 10.3).
        private bool _bound;

        // RFC 2355 10.4: sequence numbers run 0 to 32767.
        private const int MaxSequence = 32767;
        /// <summary>
        /// The TERMINAL-TYPE this terminal announces: a 3278 model 2 that understands structured
        /// fields (the "-E").
        /// </summary>
        /// <remarks>
        /// Measured on TK5, 22 November 2026: with plain "IBM-3278-2", TSO answers the first Enter
        /// at the logon panel with IKT00405I SCREEN ERASURE CAUSED BY ERROR RECOVERY PROCEDURE, and
        /// s3270 does the same when made to send that name (-tn IBM-3278-2). With the -E, TSO sends
        /// a Read Partition Query, which RetroIO answers, and the logon goes on.
        /// </remarks>
        public const string DefaultTerminalType = "IBM-3278-2-E";

        /// <summary>
        /// Creates the protocol.
        /// </summary>
        /// <param name="write">
        /// Called with bytes to put on the wire. The memory is only valid during the call.
        /// </param>
        /// <param name="terminalType">
        /// The TERMINAL-TYPE name, e.g. "IBM-3278-2-E". TK5 also takes a device suffix such as "@00C2" to pick a terminal.
        /// </param>
        public Tn3270Protocol(Action<ReadOnlyMemory<byte>> write, string terminalType = DefaultTerminalType)
            : this(write, terminalType, associateLu: null)
        {
        }

        /// <summary>
        /// Creates the protocol for a printer that asks TN3270E for the partner printer of a
        /// display LU (RFC 2355 7.1.3, ASSOCIATE).
        /// </summary>
        /// <param name="write">
        /// Called with bytes to put on the wire. The memory is only valid during the call.
        /// </param>
        /// <param name="terminalType">
        /// The TERMINAL-TYPE name; must be the printer, IBM-3287-1, when <paramref name="associateLu"/> is given.
        /// </param>
        /// <param name="associateLu">
        /// The display's LU name, as the server gave it to that display in DEVICE-TYPE IS ...
        /// CONNECT, or null to ask for any printer.
        /// </param>
        /// <exception cref="ArgumentException">
        /// Thrown when an LU is given for a device type that is not a printer ("ASSOCIATE can be
        /// used by the client only when requesting a DEVICE-TYPE that represents a printer", RFC
        /// 2355 7.1.3), or together with an "@" CONNECT name: a request carries CONNECT or
        /// ASSOCIATE, not both (7.1.1, "either the CONNECT or the ASSOCIATE command").
        /// </exception>
        public Tn3270Protocol(Action<ReadOnlyMemory<byte>> write, string terminalType, string? associateLu)
        {
            ArgumentNullException.ThrowIfNull(write);
            ArgumentException.ThrowIfNullOrEmpty(terminalType);
            _write = write;
            _terminalType = Encoding.ASCII.GetBytes(terminalType);

            // "IBM-3278-2-E@TSO1" asks TN3270E for the device type IBM-3278-2-E and the LU TSO1,
            // the same name the "@" form asks TK5 for in plain TN3270 (TN3270-NEGOTIATION.md
            // section 9; tnz sends its LU the same two ways).
            int at = terminalType.IndexOf('@');
            _deviceType = Encoding.ASCII.GetBytes(at < 0 ? terminalType : terminalType.Substring(0, at));
            _luName = at < 0 || at == terminalType.Length - 1 ? null : Encoding.ASCII.GetBytes(terminalType.Substring(at + 1));
            _isPrinter = string.Equals(Encoding.ASCII.GetString(_deviceType), PrinterDeviceType, StringComparison.OrdinalIgnoreCase);
            if (!string.IsNullOrEmpty(associateLu))
            {
                if (!_isPrinter)
                {
                    throw new ArgumentException("ASSOCIATE asks for a printer; the device type " + terminalType + " is not one (RFC 2355 section 7.1.3).", nameof(associateLu));
                }

                if (_luName != null)
                {
                    throw new ArgumentException("A TN3270E request names a device with CONNECT or ASSOCIATE, not both (RFC 2355 section 7.1.1).", nameof(associateLu));
                }

                _associateLu = Encoding.ASCII.GetBytes(associateLu);
            }

            _functionsWeAskFor = _isPrinter ? PrinterFunctionsWeAskFor : FunctionsWeAskFor;
            _wantedFunctions = _functionsWeAskFor;

            _reader = new TelnetReader(this);
            _negotiator = new TelnetOptionNegotiator(this, SendNegotiation);
            _negotiator.OptionChanged += OnOptionChanged;
        }

        /// <summary>
        /// One complete 3270 record from the host, IAC EOR removed and IAC IAC undoubled. The memory
        /// is only valid during the call.
        /// </summary>
        public event Action<ReadOnlyMemory<byte>>? RecordReceived;

        /// <summary>
        /// Something the host sent that this client does not handle. The text says what.
        /// </summary>
        public event Action<string>? ProtocolError;

        /// <summary>
        /// Raised when TN3270E comes into force or ends; the argument is the new state.
        /// </summary>
        public event Action<bool>? Tn3270EChanged;

        /// <summary>
        /// Raised when the host sends telnet Abort Output (IAC AO) in plain TN3270: the end of a
        /// print job for a 3287 (x3270 Common\pr3287\telnet.c, telnet_fsm, case AO: print_eoj when
        /// in 3270 mode and not in TN3270E).
        /// </summary>
        public event Action? AbortOutputReceived;

        /// <summary>
        /// Gets a value indicating whether 3270 records flow: in TN3270E once the function list is
        /// agreed, otherwise when BINARY and EOR are on in both directions (RFC 1576 section 3).
        /// </summary>
        /// <remarks>
        /// With TN3270E on, BINARY and EOR do not decide it (x3270 Common\telnet.c, check_in3270).
        /// </remarks>
        public bool In3270Mode => _negotiator.IsLocalEnabled(TelnetOption.Tn3270E)
            ? _tn3270eNegotiated
            : _negotiator.IsLocalEnabled(TelnetOption.Binary) && _negotiator.IsRemoteEnabled(TelnetOption.Binary)
              && _negotiator.IsLocalEnabled(TelnetOption.EndOfRecord) && _negotiator.IsRemoteEnabled(TelnetOption.EndOfRecord);

        /// <summary>
        /// Gets a value indicating whether TN3270E is in force (the function list is agreed).
        /// </summary>
        public bool InTn3270E => _tn3270eNegotiated && _negotiator.IsLocalEnabled(TelnetOption.Tn3270E);

        /// <summary>
        /// Gets the device type the host granted in TN3270E (DEVICE-TYPE IS), or null.
        /// </summary>
        public string? GrantedDeviceType { get; private set; }

        /// <summary>
        /// Gets the LU name the host gave in TN3270E (DEVICE-TYPE IS ... CONNECT), or null.
        /// </summary>
        /// <remarks>
        /// The name of the terminal this connection is, as the host knows it (RFC 2355 7.1.4).
        /// </remarks>
        public string? GrantedLuName { get; private set; }

        /// <summary>
        /// Gets a value indicating whether TN3270E is in force with the RESPONSES function: the
        /// host may ask for a response to each record, and the terminal must give one (RFC 2355
        /// section 10.4).
        /// </summary>
        public bool ResponsesAgreed => InTn3270E && (_agreedFunctions & (1 << FunctionResponses)) != 0;

        /// <summary>
        /// Gets a value indicating whether TN3270E is in force with the BIND-IMAGE function: the
        /// host says when an SNA session with an application starts and ends (RFC 2355 10.3).
        /// </summary>
        public bool BindImageAgreed => InTn3270E && (_agreedFunctions & (1 << FunctionBindImage)) != 0;

        /// <summary>
        /// Gets a value indicating whether TN3270E is in force with the SYSREQ function: the Sys
        /// Req key is telnet Abort Output (RFC 2355 10.5.2).
        /// </summary>
        public bool SysReqAgreed => InTn3270E && (_agreedFunctions & (1 << FunctionSysReq)) != 0;

        /// <summary>
        /// Gets a value indicating whether an SNA session with a host application is bound: a
        /// BIND-IMAGE came and no UNBIND after it (RFC 2355 10.3).
        /// </summary>
        public bool Bound => InTn3270E && _bound;

        /// <summary>
        /// Gets a value indicating whether the connection carries SSCP-LU data now (RFC 2355
        /// 10.5.2: "While the connection is suspended, any data messages ... SSCP-LU-DATA").
        /// </summary>
        public bool InSscpLuMode => InTn3270E && _submode == Tn3270ESubmode.SscpLu;

        /// <summary>
        /// Gets a value indicating whether the connection carries NVT data now (RFC 2355 9.1).
        /// </summary>
        public bool InNvtMode => InTn3270E && _submode == Tn3270ESubmode.Nvt;

        /// <summary>
        /// A TN3270E message that is not 3270 data: BIND image, UNBIND, SSCP-LU text or NVT text.
        /// The memory is the data part, without the header, and is only valid during the call.
        /// </summary>
        public event Action<Tn3270EHostMessage, ReadOnlyMemory<byte>>? HostMessageReceived;

        /// <summary>
        /// Raised when the server sends PRINT-EOJ on a printer session: the print job has ended
        /// (RFC 2355 10.1 and 10.2, "it may now take whatever action is appropriate for its
        /// environment (e.g., close a disk or spool file, etc.)").
        /// </summary>
        public event Action? PrintEndOfJobReceived;

        /// <summary>
        /// Gets a value indicating whether this connection asks for a printer (IBM-3287-1).
        /// </summary>
        public bool IsPrinter => _isPrinter;

        /// <summary>
        /// Gets a value indicating whether TN3270E is in force with SCS-CTL-CODES: the server may
        /// send SCS-DATA (RFC 2355 10.1).
        /// </summary>
        public bool ScsControlCodesAgreed => InTn3270E && (_agreedFunctions & (1 << FunctionScsControlCodes)) != 0;

        /// <summary>
        /// Gets a value indicating whether TN3270E is in force with DATA-STREAM-CTL: the server
        /// may send 3270-DATA to the printer (RFC 2355 10.2).
        /// </summary>
        public bool DataStreamControlAgreed => InTn3270E && (_agreedFunctions & (1 << FunctionDataStreamControl)) != 0;

        /// <summary>
        /// Bytes received from the host, any amount, split anywhere.
        /// </summary>
        /// <param name="data">
        /// The bytes.
        /// </param>
        public void Feed(ReadOnlySpan<byte> data) => _reader.Feed(data);

        /// <summary>
        /// Sends one inbound 3270 record: IAC bytes doubled, then IAC EOR.
        /// </summary>
        /// <param name="record">
        /// The record from the 3270 screen model.
        /// </param>
        public void SendRecord(ReadOnlySpan<byte> record)
        {
            if (InTn3270E && _submode == Tn3270ESubmode.Unbound)
            {
                // RFC 2355 10.3: with BIND-IMAGE agreed, no 3270-DATA before the first BIND-IMAGE
                // or after an UNBIND; only SSCP-LU-DATA or NVT-DATA, and the host has sent
                // neither. x3270 sends nothing then either (net_add_dummy_tn3270e returns false
                // in E_UNBOUND).
                ProtocolError?.Invoke("A record from the terminal was not sent: no SNA session is bound (RFC 2355 section 10.3, no 3270-DATA before a BIND-IMAGE).");
                return;
            }

            if (InTn3270E && _applying && !_answered && _current.ResponseFlag == Tn3270EResponseFlag.AlwaysResponse)
            {
                // This record is the reply to the host record being applied (a Read Buffer, a
                // Read Modified, a query reply). The positive response that record asked for goes
                // first: x3270 net_output sends tn3270e_ack before the output whenever
                // response_required is ALWAYS-RESPONSE.
                SendResponse(positive: true, Tn3270EResponseStatus.Successful);
            }

            _out.Clear();
            if (InTn3270E)
            {
                // 3270-DATA, no request, no response. With RESPONSES the sequence number counts
                // every 3270-DATA message we send, "regardless of the value of the RESPONSE-FLAG
                // field", from 0 to 32767 and round again (RFC 2355 10.4). Without it the number
                // stays 0 (RFC 2355 8.1.4; x3270 net_output counts only with RESPONSES). Written
                // through WriteData, so a 0xFF in it is doubled (RFC 2355 8.1.4).
                //
                // In SSCP-LU mode the record is SSCP-LU-DATA (RFC 2355 10.5.2), in NVT mode
                // NVT-DATA (9.1). RFC 2355 10.4 counts "3270-DATA" messages, so those two carry
                // 0; x3270 counts every message it sends (net_output, e_xmit_seq), which the
                // RFC does not say.
                byte dataType = _submode switch
                {
                    Tn3270ESubmode.SscpLu => Tn3270EDataType.SscpLuData,
                    Tn3270ESubmode.Nvt => Tn3270EDataType.NvtData,
                    _ => Tn3270EDataType.Data3270,
                };
                ushort sequence = 0;
                if (ResponsesAgreed && dataType == Tn3270EDataType.Data3270)
                {
                    sequence = _sendSequence;
                    _sendSequence = _sendSequence == MaxSequence ? (ushort)0 : (ushort)(_sendSequence + 1);
                }

                Span<byte> header = stackalloc byte[Tn3270EHeader.Length];
                new Tn3270EHeader(dataType, 0, 0, sequence).Write(header);
                TelnetEncoder.WriteData(_out, header);
            }

            TelnetEncoder.WriteData(_out, record);
            TelnetEncoder.WriteCommand(_out, TelnetCommand.EndOfRecord);
            _write(_out.WrittenMemory);
        }

        /// <summary>
        /// Sends IAC BRK. x3270 sends this for the Attn key in plain TN3270 (KEYBOARD.md section 4.9).
        /// </summary>
        public void SendBreak()
        {
            _out.Clear();
            TelnetEncoder.WriteCommand(_out, TelnetCommand.Break);
            _write(_out.WrittenMemory);
        }

        /// <summary>
        /// Sends the Sys Req key: IAC AO, when the SYSREQ function is agreed and the connection is
        /// in 3270 or SSCP-LU mode (RFC 2355 10.5.2; x3270 Common\telnet.c, net_abort, sends it
        /// in those two modes only).
        /// </summary>
        /// <remarks>
        /// The first AO asks the server to suspend the host application's output and pass data
        /// on the SSCP-LU session; the second resumes it. The mode here changes when the server's
        /// SSCP-LU-DATA or 3270-DATA arrives, not when AO is sent, as in x3270 (the copy read
        /// 24 November 2026 changes tn3270e_submode only in process_eor, on received data).
        /// </remarks>
        /// <returns>
        /// False when nothing was sent, because SYSREQ is not agreed or the mode does not allow
        /// it.
        /// </returns>
        public bool SendSysReq()
        {
            if (!SysReqAgreed || (_submode != Tn3270ESubmode.Data3270 && _submode != Tn3270ESubmode.SscpLu))
            {
                return false;
            }

            _out.Clear();
            TelnetEncoder.WriteCommand(_out, TelnetCommand.AbortOutput);
            _write(_out.WrittenMemory);
            return true;
        }

        /// <summary>
        /// Sends the Attn key in TN3270E: IAC IP (RFC 2355 section 11: "the client should transmit
        /// a Telnet IP command to the server").
        /// </summary>
        public void SendInterruptProcess()
        {
            _out.Clear();
            TelnetEncoder.WriteCommand(_out, TelnetCommand.InterruptProcess);
            _write(_out.WrittenMemory);
        }

        /// <summary>
        /// Collects record bytes until the end-of-record mark.
        /// </summary>
        /// <param name="data">
        /// Bytes of the record, telnet framing removed.
        /// </param>
        void ITelnetReaderHandler.OnData(ReadOnlySpan<byte> data)
        {
            Span<byte> target = _record.GetSpan(data.Length);
            data.CopyTo(target);
            _record.Advance(data.Length);
        }

        /// <summary>
        /// Hands the collected record on: one 3270 record ends at IAC EOR.
        /// </summary>
        void ITelnetReaderHandler.OnEndOfRecord()
        {
            if (!In3270Mode)
            {
                ProtocolError?.Invoke("The host sent END-OF-RECORD before 3270 mode was agreed (RFC 1576 section 3); the record was not shown.");
                _record.Clear();
                return;
            }

            if (InTn3270E)
            {
                ReceiveTn3270ERecord(_record.WrittenSpan);
                _record.Clear();
                return;
            }

            // Plain TN3270: nothing to answer, but the entry keeps the queue in step with the
            // records handed up.
            _pending.Enqueue(new PendingRecord(Tn3270EResponseFlag.NoResponse, 0));
            RecordReceived?.Invoke(_record.WrittenMemory);
            _record.Clear();
        }

        /// <summary>
        /// The session starts applying the next record raised through <see cref="RecordReceived"/>.
        /// </summary>
        /// <remarks>
        /// Called once per raised record, in order (Core.Protocols.IHostRecordResponseConnection).
        /// Anything sent with <see cref="SendRecord"/> until <see cref="HostRecordProcessed"/> is
        /// that record's reply.
        /// </remarks>
        public void HostRecordStarting()
        {
            if (_pending.Count == 0)
            {
                ProtocolError?.Invoke("The session started a host record that was never received; no TN3270E response can be matched to it.");
                _applying = false;
                return;
            }

            _current = _pending.Dequeue();
            _applying = true;
            _answered = false;
        }

        /// <summary>
        /// The session has applied the record <see cref="HostRecordStarting"/> began: sends the
        /// TN3270E response the record asked for, if any (RFC 2355 section 10.4.1).
        /// </summary>
        /// <param name="outcome">
        /// How it went, or null when nothing could say.
        /// </param>
        public void HostRecordProcessed(HostRecordOutcome? outcome)
        {
            if (!_applying)
            {
                return;
            }

            _applying = false;
            byte flag = _current.ResponseFlag;
            if (flag == Tn3270EResponseFlag.NoResponse)
            {
                // "If the data message it has just processed had a RESPONSE-FLAG value of
                // NO-RESPONSE ... then no RESPONSE type message should be sent."
                return;
            }

            if (outcome == null)
            {
                ProtocolError?.Invoke("The host asked for a TN3270E response to record " + _current.Sequence + ", but the outcome of applying it is not known; no response was sent.");
                return;
            }

            if (outcome.Value == HostRecordOutcome.Applied)
            {
                // ERROR-RESPONSE without an error: nothing. ALWAYS-RESPONSE: positive, "Device
                // End" (X'00'), unless it went ahead of the record's own reply already.
                if (flag == Tn3270EResponseFlag.AlwaysResponse && !_answered)
                {
                    SendResponse(positive: true, Tn3270EResponseStatus.Successful);
                }

                return;
            }

            // An error, and the host asked for ERROR-RESPONSE or ALWAYS-RESPONSE: negative. This
            // goes even when a positive one went ahead of a reply the record produced before the
            // error was found, because the host must hear that the record failed; x3270 does
            // the same (process_eor: tn3270e_nak whenever rv is negative).
            byte status = outcome.Value == HostRecordOutcome.CommandReject
                ? Tn3270EResponseStatus.CommandReject
                : Tn3270EResponseStatus.OperationCheck;
            SendResponse(positive: false, status);
        }

        // A RESPONSE message: header with DATA-TYPE RESPONSE, the host record's own sequence
        // number, then the one status byte, then IAC EOR (RFC 2355 10.4.1). WriteData doubles a
        // 0xFF in the sequence number (RFC 2355 8.1.4; x3270 tn3270e_ack doubles it by hand).
        private void SendResponse(bool positive, byte status)
        {
            Span<byte> message = stackalloc byte[Tn3270EHeader.Length + 1];
            int length = Tn3270EHeader.WriteResponse(_current.Sequence, positive, status, message);
            _out.Clear();
            TelnetEncoder.WriteData(_out, message.Slice(0, length));
            TelnetEncoder.WriteCommand(_out, TelnetCommand.EndOfRecord);
            _write(_out.WrittenMemory);
            _answered = true;
        }

        /// <summary>
        /// Passes a telnet option request to the negotiator.
        /// </summary>
        /// <param name="verb">
        /// DO, DONT, WILL or WONT.
        /// </param>
        /// <param name="option">
        /// The telnet option.
        /// </param>
        void ITelnetReaderHandler.OnNegotiation(byte verb, byte option) => _negotiator.Receive(verb, option);

        /// <summary>
        /// Answers a subnegotiation: the terminal type request.
        /// </summary>
        /// <param name="option">
        /// The telnet option.
        /// </param>
        /// <param name="parameters">
        /// The bytes between IAC SB option and IAC SE.
        /// </param>
        void ITelnetReaderHandler.OnSubnegotiation(byte option, ReadOnlySpan<byte> parameters)
        {
            // RFC 1091: IAC SB TERMINAL-TYPE SEND IAC SE asks for our name.
            if (option == TelnetOption.TerminalType && parameters.Length == 1 && parameters[0] == TelnetOption.TerminalTypeSend)
            {
                byte[] reply = new byte[_terminalType.Length + 1];
                reply[0] = TelnetOption.TerminalTypeIs;
                _terminalType.CopyTo(reply, 1);
                _out.Clear();
                TelnetEncoder.WriteSubnegotiation(_out, TelnetOption.TerminalType, reply);
                _write(_out.WrittenMemory);
                return;
            }

            if (option == TelnetOption.Tn3270E)
            {
                ReceiveTn3270ESubnegotiation(parameters);
                return;
            }

            ProtocolError?.Invoke("Subnegotiation for option " + option + " is not handled.");
        }

        /// <summary>
        /// Handles a telnet command that is not an option request.
        /// </summary>
        /// <param name="command">
        /// The command byte after IAC.
        /// </param>
        void ITelnetReaderHandler.OnCommand(byte command)
        {
            // Abort Output ends a print job on a plain TN3270 printer session (pr3287 telnet.c, case
            // AO). In TN3270E the job end has its own data types (PRINT-EOJ, UNBIND), so AO there
            // is not a job end and falls through to the report below.
            if (command == TelnetCommand.AbortOutput && In3270Mode && !InTn3270E)
            {
                AbortOutputReceived?.Invoke();
                return;
            }

            // NOP and the like carry nothing for a 3270. Anything else is said out loud.
            if (command != TelnetCommand.NoOperation)
            {
                ProtocolError?.Invoke("Telnet command " + command + " from the host is not handled.");
            }
        }

        /// <summary>
        /// Says which options this side agrees to turn on: binary, end of record and terminal type.
        /// </summary>
        /// <param name="option">
        /// The telnet option.
        /// </param>
        /// <returns>
        /// True to agree.
        /// </returns>
        bool ITelnetOptionPolicy.AcceptLocal(byte option) =>
            option == TelnetOption.TerminalType || option == TelnetOption.Binary || option == TelnetOption.EndOfRecord
            || option == TelnetOption.Tn3270E;

        /// <summary>
        /// Says which options the host may turn on: binary and end of record.
        /// </summary>
        /// <param name="option">
        /// The telnet option.
        /// </param>
        /// <returns>
        /// True to agree.
        /// </returns>
        bool ITelnetOptionPolicy.AcceptRemote(byte option) =>
            option == TelnetOption.Binary || option == TelnetOption.EndOfRecord;

        // One TN3270E record: the 5-byte header, then the data (RFC 2355 section 8).
        private void ReceiveTn3270ERecord(ReadOnlySpan<byte> record)
        {
            if (!Tn3270EHeader.TryRead(record, out Tn3270EHeader header))
            {
                ProtocolError?.Invoke("A TN3270E record shorter than its 5-byte header (RFC 2355 section 8) was not shown.");
                return;
            }

            if (header.DataType == Tn3270EDataType.ScsData)
            {
                ReceiveScsData(header, record.Length - Tn3270EHeader.Length);
                return;
            }

            if (header.DataType != Tn3270EDataType.Data3270)
            {
                ReceiveOtherDataType(header.DataType, record.Length - Tn3270EHeader.Length);
                return;
            }

            if (_isPrinter && (_agreedFunctions & (1 << FunctionDataStreamControl)) == 0)
            {
                // RFC 2355 10.2: 3270-DATA to a printer belongs to DATA-STREAM-CTL, and section 10
                // calls using a header value of a function not agreed "non-conforming". Said, and
                // not printed.
                ProtocolError?.Invoke("A 3270-DATA record came to the printer although DATA-STREAM-CTL was not agreed (RFC 2355 section 10.2); it was not printed.");
                return;
            }

            if ((_agreedFunctions & (1 << FunctionBindImage)) != 0 && !_bound)
            {
                // RFC 2355 10.3: "data messages with DATA-TYPE set to 3270-DATA ... are not
                // allowed before the first BIND-IMAGE is received by the client". x3270 drops
                // such a record without a word (process_eor); here it is said.
                ProtocolError?.Invoke("A 3270-DATA record came before any BIND-IMAGE, which RFC 2355 section 10.3 does not allow; the record was not shown.");
                return;
            }

            _submode = Tn3270ESubmode.Data3270;
            QueueResponse(header, "3270-DATA");
            ReadOnlySpan<byte> data = record.Slice(Tn3270EHeader.Length);
            RecordReceived?.Invoke(_record.WrittenMemory.Slice(Tn3270EHeader.Length, data.Length));
        }

        // SCS-DATA (RFC 2355 8.1.1, 10.1): only on a printer session with SCS-CTL-CODES. It is
        // answered like 3270-DATA (10.4.1, "Whenever a data message with a DATA-TYPE of either
        // SCS-DATA or 3270-DATA is received"), and "For the purposes of NVT mode versus 3270
        // mode, SCS-DATA must be treated exactly like 3270-DATA" (10.1).
        private void ReceiveScsData(Tn3270EHeader header, int dataLength)
        {
            if (!_isPrinter || (_agreedFunctions & (1 << FunctionScsControlCodes)) == 0)
            {
                ProtocolError?.Invoke("An SCS-DATA record came although SCS-CTL-CODES was not agreed on a printer session (RFC 2355 section 10.1); it was not printed.");
                return;
            }

            _submode = Tn3270ESubmode.Data3270;
            QueueResponse(header, "SCS-DATA");
            HostMessageReceived?.Invoke(Tn3270EHostMessage.ScsData, _record.WrittenMemory.Slice(Tn3270EHeader.Length, dataLength));
        }

        // Keeps the RESPONSE-FLAG and SEQ-NUMBER of a 3270-DATA or SCS-DATA record until the
        // session has applied it (HostRecordStarting / HostRecordProcessed). A flag that cannot
        // be honoured is said and replaced by NO-RESPONSE; the record still goes on.
        private void QueueResponse(Tn3270EHeader header, string dataTypeName)
        {
            byte responseFlag = header.ResponseFlag;
            if (responseFlag != Tn3270EResponseFlag.NoResponse && !ResponsesAgreed)
            {
                // RESPONSES was not agreed, so the host must not ask (RFC 2355 section 9). It is
                // said, and no answer is made up.
                ProtocolError?.Invoke("The host asked for a TN3270E response (flag " + responseFlag + ") although RESPONSES was not agreed; none was sent.");
                responseFlag = Tn3270EResponseFlag.NoResponse;
            }
            else if (responseFlag > Tn3270EResponseFlag.AlwaysResponse)
            {
                // RFC 2355 8.1.3 defines 0, 1 and 2 for 3270-DATA. Anything else is not answered,
                // and said.
                ProtocolError?.Invoke("TN3270E response flag " + responseFlag + " is not defined for " + dataTypeName + " (RFC 2355 section 8.1.3); no response will be sent.");
                responseFlag = Tn3270EResponseFlag.NoResponse;
            }

            // The header is kept until the session has applied the record and says how it went
            // (HostRecordStarting / HostRecordProcessed).
            _pending.Enqueue(new PendingRecord(responseFlag, header.SequenceNumber));
        }

        // A TN3270E message that is not 3270-DATA (RFC 2355 8.1.1). The RESPONSE-FLAG of these is
        // not answered: 8.1.3 gives it a meaning for 3270-DATA and SCS-DATA only, and "For any
        // other values of the DATA-TYPE field, the RESPONSE-FLAG field must be ignored by the
        // receiver". x3270 acknowledges an NVT-DATA message that asks (process_eor), which the
        // RFC does not say.
        private void ReceiveOtherDataType(byte dataType, int dataLength)
        {
            bool bindImage = (_agreedFunctions & (1 << FunctionBindImage)) != 0;
            bool sysReq = (_agreedFunctions & (1 << FunctionSysReq)) != 0;
            Tn3270EHostMessage kind;
            switch (dataType)
            {
                case Tn3270EDataType.BindImage:
                    if (!bindImage)
                    {
                        ProtocolError?.Invoke("A BIND-IMAGE message came although the BIND-IMAGE function was not agreed (RFC 2355 section 10.3); it was not used.");
                        return;
                    }

                    // RFC 2355 10.3: after it, 3270-DATA is allowed; x3270 process_bind: "A BIND
                    // implicitly puts us in 3270 mode".
                    _bound = true;
                    _submode = Tn3270ESubmode.Data3270;
                    kind = Tn3270EHostMessage.BindImage;
                    break;

                case Tn3270EDataType.Unbind:
                    if (!bindImage)
                    {
                        ProtocolError?.Invoke("An UNBIND message came although the BIND-IMAGE function was not agreed (RFC 2355 section 10.3); it was not used.");
                        return;
                    }

                    _bound = false;
                    _submode = Tn3270ESubmode.Unbound;
                    kind = Tn3270EHostMessage.Unbind;
                    break;

                case Tn3270EDataType.SscpLuData:
                    // Part of BIND-IMAGE and of SYSREQ (RFC 2355 10.3, 10.5); not of basic
                    // TN3270E (section 9).
                    if (!bindImage && !sysReq)
                    {
                        ProtocolError?.Invoke("An SSCP-LU-DATA message came although neither BIND-IMAGE nor SYSREQ was agreed (RFC 2355 sections 9, 10.3, 10.5); it was not shown.");
                        return;
                    }

                    _submode = Tn3270ESubmode.SscpLu;
                    kind = Tn3270EHostMessage.SscpLuData;
                    break;

                case Tn3270EDataType.NvtData:
                    // Allowed in every TN3270E, basic included (RFC 2355 section 9).
                    _submode = Tn3270ESubmode.Nvt;
                    kind = Tn3270EHostMessage.NvtData;
                    break;

                case Tn3270EDataType.PrintEndOfJob:
                    // RFC 2355 8.1.1: "sent only by the server, and only on a printer session";
                    // part of SCS-CTL-CODES and of DATA-STREAM-CTL (10.1, 10.2). It has no data.
                    if (!_isPrinter || (_agreedFunctions & PrinterDataFunctions) == 0)
                    {
                        ProtocolError?.Invoke("A PRINT-EOJ message came although this is not a printer session with SCS-CTL-CODES or DATA-STREAM-CTL (RFC 2355 sections 8.1.1, 10.1, 10.2); it was not used.");
                        return;
                    }

                    PrintEndOfJobReceived?.Invoke();
                    return;

                default:
                    ProtocolError?.Invoke("TN3270E data type " + dataType + " is not handled" + (_isPrinter ? " on a printer" : " on a display") + "; the record was not used.");
                    return;
            }

            HostMessageReceived?.Invoke(kind, _record.WrittenMemory.Slice(Tn3270EHeader.Length, dataLength));
        }

        // IAC SB TN3270E ... IAC SE (RFC 2355 section 7; x3270 tn3270e_negotiate).
        private void ReceiveTn3270ESubnegotiation(ReadOnlySpan<byte> sb)
        {
            if (sb.Length >= 2 && sb[0] == OpSend && sb[1] == OpDeviceType)
            {
                // DEVICE-TYPE REQUEST type [CONNECT lu] (RFC 2355 7.1.2). A new negotiation
                // starts with the full wish list again.
                _wantedFunctions = _functionsWeAskFor;

                // CONNECT asks for a named device or pool, ASSOCIATE for the partner printer of a
                // display (RFC 2355 7.1.2, 7.1.3; the last two examples of 13.4 send
                // "DEVICE-TYPE REQUEST IBM-3287-1 ASSOCIATE termxyz").
                byte[]? name = _luName ?? _associateLu;
                byte nameCommand = _luName != null ? OpConnect : OpAssociate;
                byte[] request = new byte[2 + _deviceType.Length + (name == null ? 0 : 1 + name.Length)];
                request[0] = OpDeviceType;
                request[1] = OpRequest;
                _deviceType.CopyTo(request, 2);
                if (name != null)
                {
                    request[2 + _deviceType.Length] = nameCommand;
                    name.CopyTo(request, 3 + _deviceType.Length);
                }

                SendTn3270E(request);
                return;
            }

            if (sb.Length >= 2 && sb[0] == OpDeviceType && sb[1] == OpIs)
            {
                // DEVICE-TYPE IS type [CONNECT lu]: keep what was granted, then ask for the
                // functions wanted, RESPONSES (RFC 2355 sections 7.1.4, 7.2, 10.4; x3270 asks for
                // its whole list at this point too, tn3270e_negotiate).
                ReadOnlySpan<byte> rest = sb.Slice(2);
                int connect = rest.IndexOf(OpConnect);
                GrantedDeviceType = Encoding.ASCII.GetString(connect < 0 ? rest : rest.Slice(0, connect));
                GrantedLuName = connect < 0 ? null : Encoding.ASCII.GetString(rest.Slice(connect + 1));
                SendFunctions(OpRequest, _wantedFunctions);
                return;
            }

            if (sb.Length >= 2 && sb[0] == OpDeviceType && sb[1] == OpReject)
            {
                string reason = sb.Length >= 4 && sb[2] == OpReason ? ReasonName(sb[3]) : "no reason given";
                BackOff("the host refused the device type (" + reason + ")");
                return;
            }

            if (sb.Length >= 2 && sb[0] == OpFunctions && sb[1] == OpRequest)
            {
                // RFC 2355 7.2.1: a list holding only functions we want is accepted by sending it
                // back unchanged in FUNCTIONS IS. A list with anything else in it, including a
                // code we do not know ("simply remove that function code from the list"), is
                // answered with FUNCTIONS REQUEST of what both want (x3270: b8_none_added, then
                // b8_and).
                int received = FunctionMask(sb.Slice(2), out bool onlyWanted);
                if (_isPrinter && (received & PrinterDataFunctions) == 0)
                {
                    // Neither SCS-CTL-CODES nor DATA-STREAM-CTL is left that both sides want: the
                    // impasse RFC 2355 7.2.1 names, "the 'offended' party should terminate the
                    // negotiation by sending an IAC DON'T (or WON'T) TN3270E".
                    BackOff("the host's function list keeps neither SCS-CTL-CODES nor DATA-STREAM-CTL, which a printer needs (RFC 2355 section 7.2.1)");
                    return;
                }

                if (onlyWanted)
                {
                    _wantedFunctions = received;
                    byte[] reply = sb.ToArray();
                    reply[1] = OpIs;
                    SendTn3270E(reply);
                    Agree(received);
                }
                else
                {
                    _wantedFunctions &= received;
                    SendFunctions(OpRequest, _wantedFunctions);
                }

                return;
            }

            if (sb.Length >= 2 && sb[0] == OpFunctions && sb[1] == OpIs)
            {
                int agreed = FunctionMask(sb.Slice(2), out bool onlyWanted);
                if (onlyWanted && _isPrinter && (agreed & PrinterDataFunctions) == 0)
                {
                    BackOff("the host agreed to neither SCS-CTL-CODES nor DATA-STREAM-CTL, which a printer needs (RFC 2355 sections 7.2.1, 10.1)");
                    return;
                }

                if (onlyWanted)
                {
                    // What we asked for, or less (RFC 2355 7.2.1; x3270 takes a subset too).
                    _wantedFunctions = agreed;
                    Agree(agreed);
                }
                else
                {
                    // The host added functions that were not asked for (x3270: "Host illegally
                    // added function(s)").
                    BackOff("the host agreed to functions that were not asked for");
                }

                return;
            }

            ProtocolError?.Invoke("TN3270E subnegotiation " + Convert.ToHexString(sb) + " is not handled.");
        }

        // TN3270E is in force with these functions. The send counter starts from zero (RFC 2355
        // 10.4: "This counter should start at zero for the first such message").
        private void Agree(int functions)
        {
            _agreedFunctions = functions;
            _sendSequence = 0;

            // x3270 (tn3270e_negotiate, FUNCTIONS IS): "If the host does not support BIND_IMAGE,
            // then we must go straight to 3270 mode"; with it, nothing is bound yet.
            _bound = false;
            _submode = (functions & (1 << FunctionBindImage)) != 0 ? Tn3270ESubmode.Unbound : Tn3270ESubmode.Data3270;
            SetNegotiated(true);
        }

        // The bit mask of a function list. onlyWanted is false when the list holds a function we
        // do not want now, or a code we do not know at all.
        private int FunctionMask(ReadOnlySpan<byte> list, out bool onlyWanted)
        {
            int mask = 0;
            onlyWanted = true;
            for (int i = 0; i < list.Length; i++)
            {
                byte code = list[i];
                int bit = code < 31 ? 1 << code : 0;
                if (bit == 0 || (_wantedFunctions & bit) == 0)
                {
                    onlyWanted = false;
                    continue;
                }

                mask |= bit;
            }

            return mask;
        }

        // FUNCTIONS REQUEST or IS with the functions in a mask, lowest code first.
        private void SendFunctions(byte verb, int functions)
        {
            Span<byte> sb = stackalloc byte[2 + 31];
            sb[0] = OpFunctions;
            sb[1] = verb;
            int n = 2;
            for (int code = 0; code < 31; code++)
            {
                if ((functions & (1 << code)) != 0)
                {
                    sb[n++] = (byte)code;
                }
            }

            SendTn3270E(sb.Slice(0, n));
        }

        // Gives up TN3270E and goes on as plain TN3270 (x3270 backoff_tn3270e).
        private void BackOff(string why)
        {
            ProtocolError?.Invoke("TN3270E given up, going on as plain TN3270: " + why + ".");
            GrantedDeviceType = null;
            GrantedLuName = null;
            SetNegotiated(false);
            _negotiator.RequestLocalDisable(TelnetOption.Tn3270E);
        }

        private void OnOptionChanged(byte option, bool local, bool on)
        {
            if (option == TelnetOption.Tn3270E && local && !on)
            {
                SetNegotiated(false);
            }
        }

        private void SetNegotiated(bool negotiated)
        {
            if (_tn3270eNegotiated == negotiated)
            {
                return;
            }

            _tn3270eNegotiated = negotiated;
            Tn3270EChanged?.Invoke(negotiated);
        }

        private void SendTn3270E(ReadOnlySpan<byte> parameters)
        {
            _out.Clear();
            TelnetEncoder.WriteSubnegotiation(_out, TelnetOption.Tn3270E, parameters);
            _write(_out.WrittenMemory);
        }

        // RFC 2355 section 3; the same names tnz prints.
        private static string ReasonName(byte reason) => reason switch
        {
            0x00 => "CONN-PARTNER",
            0x01 => "DEVICE-IN-USE",
            0x02 => "INV-ASSOCIATE",
            0x03 => "INV-NAME",
            0x04 => "INV-DEVICE-TYPE",
            0x05 => "TYPE-NAME-ERROR",
            0x06 => "UNKNOWN-ERROR",
            0x07 => "UNSUPPORTED-REQ",
            _ => "reason " + reason,
        };

        private void SendNegotiation(byte verb, byte option)
        {
            _out.Clear();
            TelnetEncoder.WriteNegotiation(_out, verb, option);
            _write(_out.WrittenMemory);
        }

        // The four modes of a TN3270E connection.
        private enum Tn3270ESubmode
        {
            // BIND-IMAGE agreed and no SNA session bound: SSCP-LU or NVT data only.
            Unbound,

            // 3270-DATA.
            Data3270,

            // NVT-DATA (RFC 2355 9.1).
            Nvt,

            // SSCP-LU-DATA (RFC 2355 10.5.2).
            SscpLu,
        }

        // What a host record's header asked for: its RESPONSE-FLAG (NO-RESPONSE when nothing is
        // to be sent) and its SEQ-NUMBER, to be echoed in the response.
        private readonly struct PendingRecord
        {
            public PendingRecord(byte responseFlag, ushort sequence)
            {
                ResponseFlag = responseFlag;
                Sequence = sequence;
            }

            public byte ResponseFlag { get; }

            public ushort Sequence { get; }
        }
    }
}
