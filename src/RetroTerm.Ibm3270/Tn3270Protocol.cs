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
    /// with "IBM-3278-2"; BINARY and END-OF-RECORD in both directions. Everything else is refused,
    /// TN3270E included (RFC 2355 is not built; TK5 never asks for it).
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

        /// <summary>
        /// Creates the protocol.
        /// </summary>
        /// <param name="write">
        /// Called with bytes to put on the wire. The memory is only valid during the call.
        /// </param>
        /// <param name="terminalType">
        /// The TERMINAL-TYPE name, e.g. "IBM-3278-2". TK5 also takes "IBM-3278-2@00C2" to pick a device.
        /// </param>
        public Tn3270Protocol(Action<ReadOnlyMemory<byte>> write, string terminalType = "IBM-3278-2")
        {
            ArgumentNullException.ThrowIfNull(write);
            ArgumentException.ThrowIfNullOrEmpty(terminalType);
            _write = write;
            _terminalType = Encoding.ASCII.GetBytes(terminalType);
            _reader = new TelnetReader(this);
            _negotiator = new TelnetOptionNegotiator(this, SendNegotiation);
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
        /// Gets a value indicating whether BINARY and EOR are on in both directions (RFC 1576 section 3).
        /// </summary>
        public bool In3270Mode =>
            _negotiator.IsLocalEnabled(TelnetOption.Binary) && _negotiator.IsRemoteEnabled(TelnetOption.Binary)
            && _negotiator.IsLocalEnabled(TelnetOption.EndOfRecord) && _negotiator.IsRemoteEnabled(TelnetOption.EndOfRecord);

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
            option == TelnetOption.TerminalType || option == TelnetOption.Binary || option == TelnetOption.EndOfRecord;

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

        private void SendNegotiation(byte verb, byte option)
        {
            _out.Clear();
            TelnetEncoder.WriteNegotiation(_out, verb, option);
            _write(_out.WrittenMemory);
        }
    }
}
