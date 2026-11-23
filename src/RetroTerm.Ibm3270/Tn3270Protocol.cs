//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs/RetroTerm
// RetroTerm — Connecting today's machines to yesterday's systems
//

using System;
using System.Buffers;
using System.Text;

using RetroIO.Protocols.Telnet;
using RetroIO.Protocols.Tn3270;

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
    /// TN3270E (RFC 2355) is "basic TN3270E" (section 9): the function list asked for is empty, so
    /// there are no responses, no BIND image and no SYSREQ, and every record carries a 5-byte header
    /// that is all zeros on the way out. Built 23 November 2026 from the RFC and checked against
    /// x3270 (Common\telnet.c, tn3270e_negotiate, process_eor, net_output) and tnz (tnz\tnz.py); TK5
    /// never offers TN3270E, so no live host has tested it. Where the two disagree this follows
    /// x3270: TN3270E counts as agreed only when the function lists match (RFC 2355 section 7.2,
    /// "once this command is received by the other side, all necessary negotiation has been
    /// completed"), whereas tnz takes it as agreed at DEVICE-TYPE IS; and a refused device type
    /// backs off to plain TN3270 with WONT TN3270E (x3270 backoff_tn3270e), whereas tnz stops with
    /// an error.
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

        // True once FUNCTIONS IS has settled the (empty) function list: TN3270E is in force.
        private bool _tn3270eNegotiated;

        // The TN3270E subnegotiation command codes (RFC 2355 section 3; x3270 include\tn3270e.h).
        private const byte OpConnect = 0x01;
        private const byte OpDeviceType = 0x02;
        private const byte OpFunctions = 0x03;
        private const byte OpIs = 0x04;
        private const byte OpReason = 0x05;
        private const byte OpReject = 0x06;
        private const byte OpRequest = 0x07;
        private const byte OpSend = 0x08;
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
            _out.Clear();
            if (InTn3270E)
            {
                // 3270-DATA, no request, no response, sequence 0: RESPONSES is not agreed, so the
                // number stays 0 (RFC 2355 8.1.4; x3270 net_output counts only with RESPONSES).
                Span<byte> header = stackalloc byte[Tn3270EHeader.Length];
                new Tn3270EHeader(Tn3270EDataType.Data3270, 0, 0, 0).Write(header);
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

            RecordReceived?.Invoke(_record.WrittenMemory);
            _record.Clear();
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

            if (header.DataType != Tn3270EDataType.Data3270)
            {
                // Basic TN3270E uses 3270-DATA and NVT-DATA only (RFC 2355 section 9). NVT mode is
                // not shown anywhere yet, as in plain TN3270.
                ProtocolError?.Invoke("TN3270E data type " + header.DataType + " is not handled; the record was not shown.");
                return;
            }

            if (header.ResponseFlag != Tn3270EResponseFlag.NoResponse)
            {
                // RESPONSES was not agreed, so the host must not ask (RFC 2355 section 9). It is
                // said, and no answer is made up: an answer needs the outcome of the record.
                ProtocolError?.Invoke("The host asked for a TN3270E response (flag " + header.ResponseFlag + ") although RESPONSES was not agreed; none was sent.");
            }

            ReadOnlySpan<byte> data = record.Slice(Tn3270EHeader.Length);
            RecordReceived?.Invoke(_record.WrittenMemory.Slice(Tn3270EHeader.Length, data.Length));
        }

        // IAC SB TN3270E ... IAC SE (RFC 2355 section 7; x3270 tn3270e_negotiate).
        private void ReceiveTn3270ESubnegotiation(ReadOnlySpan<byte> sb)
        {
            if (sb.Length >= 2 && sb[0] == OpSend && sb[1] == OpDeviceType)
            {
                // DEVICE-TYPE REQUEST type [CONNECT lu] (RFC 2355 7.1.2).
                byte[] request = new byte[2 + _deviceType.Length + (_luName == null ? 0 : 1 + _luName.Length)];
                request[0] = OpDeviceType;
                request[1] = OpRequest;
                _deviceType.CopyTo(request, 2);
                if (_luName != null)
                {
                    request[2 + _deviceType.Length] = OpConnect;
                    _luName.CopyTo(request, 3 + _deviceType.Length);
                }

                SendTn3270E(request);
                return;
            }

            if (sb.Length >= 2 && sb[0] == OpDeviceType && sb[1] == OpIs)
            {
                // DEVICE-TYPE IS type [CONNECT lu]: keep what was granted, then ask for no
                // functions, which is basic TN3270E (RFC 2355 sections 7.1.4, 7.2, 9).
                ReadOnlySpan<byte> rest = sb.Slice(2);
                int connect = rest.IndexOf(OpConnect);
                GrantedDeviceType = Encoding.ASCII.GetString(connect < 0 ? rest : rest.Slice(0, connect));
                GrantedLuName = connect < 0 ? null : Encoding.ASCII.GetString(rest.Slice(connect + 1));
                SendTn3270E(stackalloc byte[] { OpFunctions, OpRequest });
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
                // We want no functions. An empty list is accepted as it is; a longer one is
                // answered with what both can do, which is nothing (RFC 2355 7.2.1).
                if (sb.Length == 2)
                {
                    SendTn3270E(stackalloc byte[] { OpFunctions, OpIs });
                    SetNegotiated(true);
                }
                else
                {
                    SendTn3270E(stackalloc byte[] { OpFunctions, OpRequest });
                }

                return;
            }

            if (sb.Length >= 2 && sb[0] == OpFunctions && sb[1] == OpIs)
            {
                if (sb.Length == 2)
                {
                    SetNegotiated(true);
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
    }
}
