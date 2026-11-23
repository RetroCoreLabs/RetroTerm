//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs/RetroTerm
// RetroTerm — Connecting today's machines to yesterday's systems
//

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace RetroTerm.Tests.Oracle
{
    /// <summary>
    /// What one telnet unit is: a negotiation (IAC WILL/WONT/DO/DONT option), a subnegotiation
    /// (IAC SB ... IAC SE), a 3270 record (data ended by IAC EOR), or another telnet command.
    /// </summary>
    internal enum TelnetUnitKind
    {
        Negotiation,
        Subnegotiation,
        Record,
        Command,
    }

    /// <summary>
    /// One telnet unit as it crossed the wire.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="Wire"/> holds the bytes exactly as sent, IAC doubling and the closing IAC EOR
    /// included, so a replay sends the same bytes the host did. <see cref="Payload"/> is the
    /// meaning: for a negotiation the verb and the option, for a subnegotiation the bytes between
    /// IAC SB and IAC SE, for a record the 3270 record with IAC IAC made one X'FF' again (RFC 1576
    /// and RFC 885), for a command the command byte.
    /// </para>
    /// </remarks>
    internal sealed class TelnetUnit
    {
        public const byte Iac = 0xFF;
        public const byte Eor = 0xEF;
        public const byte Se = 0xF0;
        public const byte Sb = 0xFA;
        public const byte Will = 0xFB;
        public const byte Wont = 0xFC;
        public const byte Do = 0xFD;
        public const byte Dont = 0xFE;

        /// <summary>
        /// TIMING-MARK, RFC 860. The replay host sends IAC DO TIMING-MARK after a record; the
        /// client's answer (WILL or WONT) comes back only after it has read everything before it.
        /// </summary>
        public const byte TimingMark = 0x06;

        public TelnetUnit(TelnetUnitKind kind, byte[] wire, byte[] payload)
        {
            Kind = kind;
            Wire = wire;
            Payload = payload;
        }

        public TelnetUnitKind Kind { get; }

        public byte[] Wire { get; }

        public byte[] Payload { get; }

        /// <summary>
        /// Splits a byte stream into telnet units. Bytes after the last complete unit are left in
        /// <paramref name="rest"/> for the next call.
        /// </summary>
        /// <param name="bytes">
        /// The bytes, starting at a unit boundary.
        /// </param>
        /// <param name="units">
        /// Where the complete units go, in order.
        /// </param>
        /// <param name="rest">
        /// The bytes of a unit not yet complete.
        /// </param>
        public static void Split(ReadOnlySpan<byte> bytes, List<TelnetUnit> units, out byte[] rest)
        {
            int start = 0;
            int i = 0;
            var payload = new List<byte>();
            while (i < bytes.Length)
            {
                byte b = bytes[i];
                if (b != Iac)
                {
                    payload.Add(b);
                    i++;
                    continue;
                }

                if (i + 1 >= bytes.Length)
                {
                    break;
                }

                byte c = bytes[i + 1];
                if (c == Iac)
                {
                    payload.Add(Iac);
                    i += 2;
                    continue;
                }

                if (c == Eor)
                {
                    i += 2;
                    units.Add(new TelnetUnit(TelnetUnitKind.Record, bytes.Slice(start, i - start).ToArray(), payload.ToArray()));
                    payload.Clear();
                    start = i;
                    continue;
                }

                if (c >= Will && c <= Dont)
                {
                    if (i + 2 >= bytes.Length)
                    {
                        break;
                    }

                    FlushBefore(bytes, ref start, i, payload, units);
                    units.Add(new TelnetUnit(TelnetUnitKind.Negotiation, bytes.Slice(i, 3).ToArray(), new[] { c, bytes[i + 2] }));
                    i += 3;
                    start = i;
                    continue;
                }

                if (c == Sb)
                {
                    int end = -1;
                    for (int j = i + 2; j + 1 < bytes.Length; j++)
                    {
                        if (bytes[j] == Iac && bytes[j + 1] == Se)
                        {
                            end = j;
                            break;
                        }

                        if (bytes[j] == Iac && bytes[j + 1] == Iac)
                        {
                            j++;
                        }
                    }

                    if (end < 0)
                    {
                        break;
                    }

                    FlushBefore(bytes, ref start, i, payload, units);
                    var sub = new List<byte>();
                    for (int j = i + 2; j < end; j++)
                    {
                        sub.Add(bytes[j]);
                        if (bytes[j] == Iac)
                        {
                            j++;
                        }
                    }

                    units.Add(new TelnetUnit(TelnetUnitKind.Subnegotiation, bytes.Slice(i, end + 2 - i).ToArray(), sub.ToArray()));
                    i = end + 2;
                    start = i;
                    continue;
                }

                FlushBefore(bytes, ref start, i, payload, units);
                units.Add(new TelnetUnit(TelnetUnitKind.Command, bytes.Slice(i, 2).ToArray(), new[] { c }));
                i += 2;
                start = i;
            }

            rest = bytes.Slice(start).ToArray();
        }

        /// <summary>
        /// The unit's wire bytes in hex, as the recording stores a host unit.
        /// </summary>
        /// <returns>
        /// The hex text, bytes separated by spaces.
        /// </returns>
        public string ToHex() => Hex(Wire);

        public static string Hex(byte[] bytes)
        {
            var text = new StringBuilder(bytes.Length * 3);
            for (int i = 0; i < bytes.Length; i++)
            {
                if (i > 0)
                {
                    text.Append(' ');
                }

                text.Append(bytes[i].ToString("X2", CultureInfo.InvariantCulture));
            }

            return text.ToString();
        }

        public static byte[] ParseHex(string text)
        {
            string[] parts = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            byte[] bytes = new byte[parts.Length];
            for (int i = 0; i < parts.Length; i++)
            {
                bytes[i] = byte.Parse(parts[i], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            }

            return bytes;
        }

        // Bytes before a telnet command that were not ended by IAC EOR. Neither TK5 nor a client
        // here is known to send those; if one does, this fails loudly rather than guess where the
        // record would have ended.
        private static void FlushBefore(ReadOnlySpan<byte> bytes, ref int start, int at, List<byte> payload, List<TelnetUnit> units)
        {
            if (at > start)
            {
                throw new InvalidOperationException("3270 data before a telnet command without IAC EOR: " + Hex(bytes.Slice(start, at - start).ToArray()));
            }

            payload.Clear();
        }
    }
}
