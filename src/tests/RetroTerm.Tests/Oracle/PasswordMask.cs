//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs/RetroTerm
// RetroTerm — Connecting today's machines to yesterday's systems
//

using System;
using System.Collections.Generic;
using System.Text;

using RetroIO.Protocols.Tn3270;

namespace RetroTerm.Tests.Oracle
{
    /// <summary>
    /// The one place that keeps the TK5 passwords out of everything the tests write: recording
    /// files (<see cref="HostRecording"/>), failure messages and test output (once BUGS.md B17, fixed
    /// 23 November 2026).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Two ways, used together on what a client sent. The first needs no knowledge of the
    /// password: a password is typed into a non-display input field (GA23-0059-4 field
    /// attribute bits 2-3 both set, bit 5 clear), so once the host has written such a field the
    /// field data of the client's next records is replaced (<see cref="SentDataMask"/>).
    /// Measured in Oracle\Recordings\tso-ready.txt: the TSO password prompt record holds
    /// SF X'4C' and the client's answer is AID, cursor, SBA, then the password.
    /// </para>
    /// <para>
    /// The second searches for the known passwords (from tk5-tests.json) in every form one
    /// could take in text: as typed in any case, ASCII hex and EBCDIC (code page 037) hex, as
    /// typed and upper case - the same forms <see cref="HostRecording.AssertHolds"/> checks.
    /// </para>
    /// </remarks>
    internal static class PasswordMask
    {
        /// <summary>
        /// What a password is replaced with.
        /// </summary>
        public const string Placeholder = "PASSWORD";

        private static string[]? _known;

        /// <summary>
        /// The passwords in tk5-tests.json, read once; none when the file is not there.
        /// </summary>
        /// <returns>
        /// The passwords, empty ones left out.
        /// </returns>
        public static string[] KnownPasswords()
        {
            string[]? known = _known;
            if (known == null)
            {
                known = Hercules.Tk5Settings.Passwords();
                _known = known;
            }

            return known;
        }

        /// <summary>
        /// Every form a password could take in text, upper case: the text itself, ASCII hex as
        /// typed and upper case, EBCDIC hex as typed and upper case. Hex is two digits a byte,
        /// separated by spaces, as <see cref="TelnetUnit.Hex"/> writes it.
        /// </summary>
        /// <param name="secret">
        /// The password.
        /// </param>
        /// <returns>
        /// The five forms.
        /// </returns>
        public static string[] Forms(string secret)
        {
            return new string[]
            {
                secret.ToUpperInvariant(),
                TelnetUnit.Hex(Encoding.ASCII.GetBytes(secret)),
                TelnetUnit.Hex(Encoding.ASCII.GetBytes(secret.ToUpperInvariant())),
                TelnetUnit.Hex(Ebcdic(secret)),
                TelnetUnit.Hex(Ebcdic(secret.ToUpperInvariant())),
            };
        }

        /// <summary>
        /// The text with every form of every known password replaced (see <see cref="Forms"/>).
        /// </summary>
        /// <param name="text">
        /// The text.
        /// </param>
        /// <returns>
        /// The masked text.
        /// </returns>
        public static string Text(string text) => Text(text, KnownPasswords());

        /// <summary>
        /// The text with every form of every given password replaced (see <see cref="Forms"/>).
        /// The text form becomes <see cref="Placeholder"/>, an ASCII hex form the placeholder's
        /// ASCII hex and an EBCDIC hex form its EBCDIC hex, so a decoded dump still lines up.
        /// </summary>
        /// <param name="text">
        /// The text.
        /// </param>
        /// <param name="secrets">
        /// The passwords.
        /// </param>
        /// <returns>
        /// The masked text.
        /// </returns>
        public static string Text(string text, string[] secrets)
        {
            string asciiPlaceholder = TelnetUnit.Hex(Encoding.ASCII.GetBytes(Placeholder));
            string ebcdicPlaceholder = TelnetUnit.Hex(Ebcdic(Placeholder));
            for (int s = 0; s < secrets.Length; s++)
            {
                if (secrets[s].Length == 0)
                {
                    continue;
                }

                string[] forms = Forms(secrets[s]);
                for (int f = 0; f < forms.Length; f++)
                {
                    if (forms[f].Length == 0)
                    {
                        continue;
                    }

                    string with = f == 0 ? Placeholder : f < 3 ? asciiPlaceholder : ebcdicPlaceholder;
                    text = ReplaceIgnoringCase(text, forms[f], with);
                }
            }

            return text;
        }

        /// <summary>
        /// The bytes with each given password's EBCDIC bytes, as typed and in upper case,
        /// replaced by the placeholder's. EBCDIC letters and digits are never X'FF', so IAC
        /// doubling in wire bytes is not disturbed.
        /// </summary>
        /// <param name="bytes">
        /// 3270 bytes, with or without the telnet layer.
        /// </param>
        /// <param name="secrets">
        /// The passwords.
        /// </param>
        /// <returns>
        /// The masked bytes, a new array.
        /// </returns>
        public static byte[] ReplaceKnown(byte[] bytes, string[] secrets)
        {
            for (int s = 0; s < secrets.Length; s++)
            {
                if (secrets[s].Length == 0)
                {
                    continue;
                }

                bytes = Replace(bytes, Ebcdic(secrets[s]));
                bytes = Replace(bytes, Ebcdic(secrets[s].ToUpperInvariant()));
            }

            return bytes;
        }

        /// <summary>
        /// Whether a host record defines an input field that does not show what is typed: a
        /// field attribute (SF, or the X'C0' pair of SFE or MF) with bits 2-3 both set
        /// (non-display) and bit 5 clear (unprotected), GA23-0059-4 chapter 3.
        /// </summary>
        /// <remarks>
        /// The orders are walked from the first byte, so a command, WCC or TN3270E header in
        /// front is passed over as data: none of those bytes is an order code that takes an
        /// attribute. A wrong guess here only masks more than it needs to.
        /// </remarks>
        /// <param name="record">
        /// The host record, telnet layer removed.
        /// </param>
        /// <returns>
        /// True when the record has such a field.
        /// </returns>
        public static bool HasNonDisplayInputField(ReadOnlySpan<byte> record)
        {
            int i = 0;
            while (i < record.Length)
            {
                byte b = record[i];
                if (b == Order3270.StartField)
                {
                    if (i + 1 < record.Length && IsNonDisplayInput(record[i + 1]))
                    {
                        return true;
                    }

                    i += 2;
                }
                else if (b == Order3270.StartFieldExtended || b == Order3270.ModifyField)
                {
                    int pairs = i + 1 < record.Length ? record[i + 1] : 0;
                    for (int p = 0; p < pairs; p++)
                    {
                        int at = i + 2 + (p * 2);
                        if (at + 1 < record.Length && record[at] == 0xC0 && IsNonDisplayInput(record[at + 1]))
                        {
                            return true;
                        }
                    }

                    i += 2 + (pairs * 2);
                }
                else if (b == Order3270.SetBufferAddress || b == Order3270.EraseUnprotectedToAddress || b == Order3270.SetAttribute)
                {
                    i += 3;
                }
                else if (b == Order3270.RepeatToAddress)
                {
                    // Address, then the character (GE and its character when it is a GE).
                    i += i + 3 < record.Length && record[i + 3] == Order3270.GraphicEscape ? 5 : 4;
                }
                else if (b == Order3270.GraphicEscape)
                {
                    i += 2;
                }
                else
                {
                    i++;
                }
            }

            return false;
        }

        /// <summary>
        /// A client record with all its field data replaced: the AID and the cursor address are
        /// kept, each SBA and its address are kept, and each run of data between them becomes
        /// the placeholder in EBCDIC.
        /// </summary>
        /// <param name="record">
        /// The client's record, telnet layer removed.
        /// </param>
        /// <returns>
        /// The masked record, a new array.
        /// </returns>
        public static byte[] FieldDataReplaced(ReadOnlySpan<byte> record)
        {
            if (record.Length <= 3)
            {
                return record.ToArray();
            }

            byte[] placeholder = Ebcdic(Placeholder);
            var result = new List<byte>(record.Length);
            for (int k = 0; k < 3; k++)
            {
                result.Add(record[k]);
            }

            int i = 3;
            while (i < record.Length)
            {
                if (record[i] == Order3270.SetBufferAddress)
                {
                    int end = Math.Min(i + 3, record.Length);
                    for (int k = i; k < end; k++)
                    {
                        result.Add(record[k]);
                    }

                    i = end;
                    continue;
                }

                while (i < record.Length && record[i] != Order3270.SetBufferAddress)
                {
                    i++;
                }

                result.AddRange(placeholder);
            }

            return result.ToArray();
        }

        /// <summary>
        /// The text in EBCDIC code page 037, the code page the TK5 sessions run in. A character
        /// the code page does not have cannot have been sent, so it is left out.
        /// </summary>
        /// <param name="text">
        /// The text.
        /// </param>
        /// <returns>
        /// The EBCDIC bytes.
        /// </returns>
        public static byte[] Ebcdic(string text)
        {
            var bytes = new List<byte>(text.Length);
            for (int i = 0; i < text.Length; i++)
            {
                if (CodePage.Cp037.TryFromUnicode(text[i], out byte b))
                {
                    bytes.Add(b);
                }
            }

            return bytes.ToArray();
        }

        private static bool IsNonDisplayInput(byte attribute) => (attribute & 0x0C) == 0x0C && (attribute & 0x20) == 0;

        private static byte[] Replace(byte[] bytes, byte[] secret)
        {
            if (secret.Length == 0)
            {
                return bytes;
            }

            byte[] placeholder = Ebcdic(Placeholder);
            var result = new List<byte>(bytes.Length);
            int i = 0;
            while (i < bytes.Length)
            {
                if (i + secret.Length <= bytes.Length && bytes.AsSpan(i, secret.Length).SequenceEqual(secret))
                {
                    result.AddRange(placeholder);
                    i += secret.Length;
                }
                else
                {
                    result.Add(bytes[i]);
                    i++;
                }
            }

            return result.ToArray();
        }

        // ToUpperInvariant maps one char to one char, so an index in the upper-case copy is the
        // same index in the text.
        private static string ReplaceIgnoringCase(string text, string upperForm, string with)
        {
            string upper = text.ToUpperInvariant();
            int at = upper.IndexOf(upperForm, StringComparison.Ordinal);
            if (at < 0)
            {
                return text;
            }

            var result = new StringBuilder(text.Length);
            int from = 0;
            while (at >= 0)
            {
                result.Append(text, from, at - from).Append(with);
                from = at + upperForm.Length;
                at = upper.IndexOf(upperForm, from, StringComparison.Ordinal);
            }

            result.Append(text, from, text.Length - from);
            return result.ToString();
        }
    }

    /// <summary>
    /// Masks what a client sent, record by record, for printing: told every host record in
    /// order, it replaces the field data of each client record that answers a screen with a
    /// non-display input field, and replaces the known passwords everywhere
    /// (<see cref="PasswordMask"/>). Not thread safe; the caller locks.
    /// </summary>
    internal sealed class SentDataMask
    {
        private readonly string[] _secrets;
        private readonly List<byte> _hostRecord = new List<byte>();

        // The screen the host last wrote has a non-display input field. Erase/Write and
        // Erase/Write Alternate start it again from that record; any other write can only add
        // fields, so it only ever sets it.
        private bool _nonDisplayOnScreen;

        // Telnet state for HostWire: 0 data, 1 after IAC, 2 inside SB, 3 IAC inside SB,
        // 4 waiting for the option after WILL/WONT/DO/DONT.
        private int _telnet;

        public SentDataMask()
            : this(PasswordMask.KnownPasswords())
        {
        }

        public SentDataMask(string[] secrets)
        {
            _secrets = secrets;
        }

        /// <summary>
        /// Takes one whole host record, telnet layer removed.
        /// </summary>
        /// <param name="record">
        /// The record.
        /// </param>
        public void Host(ReadOnlySpan<byte> record)
        {
            bool has = PasswordMask.HasNonDisplayInputField(record);
            byte command = record.Length > 0 ? record[0] : (byte)0;
            bool erases = command == 0xF5 || command == 0x7E || command == 0x05 || command == 0x0D;
            _nonDisplayOnScreen = erases ? has : _nonDisplayOnScreen || has;
        }

        /// <summary>
        /// Takes host bytes as they came off the socket, telnet layer and all, in any pieces;
        /// each record is handed to <see cref="Host"/> when its IAC EOR arrives.
        /// </summary>
        /// <param name="bytes">
        /// The bytes.
        /// </param>
        public void HostWire(ReadOnlySpan<byte> bytes)
        {
            for (int i = 0; i < bytes.Length; i++)
            {
                byte b = bytes[i];
                switch (_telnet)
                {
                    case 0:
                        if (b == TelnetUnit.Iac)
                        {
                            _telnet = 1;
                        }
                        else
                        {
                            _hostRecord.Add(b);
                        }

                        break;
                    case 1:
                        _telnet = 0;
                        if (b == TelnetUnit.Iac)
                        {
                            _hostRecord.Add(b);
                        }
                        else if (b == TelnetUnit.Eor)
                        {
                            Host(_hostRecord.ToArray());
                            _hostRecord.Clear();
                        }
                        else if (b == TelnetUnit.Sb)
                        {
                            _telnet = 2;
                        }
                        else if (b >= TelnetUnit.Will && b <= TelnetUnit.Dont)
                        {
                            _telnet = 4;
                        }

                        break;
                    case 2:
                        _telnet = b == TelnetUnit.Iac ? 3 : 2;
                        break;
                    case 3:
                        _telnet = b == TelnetUnit.Se ? 0 : 2;
                        break;
                    default:
                        _telnet = 0;
                        break;
                }
            }
        }

        /// <summary>
        /// Masks one client record, telnet layer removed, or any piece of client bytes.
        /// </summary>
        /// <param name="record">
        /// The record.
        /// </param>
        /// <returns>
        /// The bytes to print, a new array.
        /// </returns>
        public byte[] Client(ReadOnlySpan<byte> record)
        {
            byte[] shown = _nonDisplayOnScreen ? PasswordMask.FieldDataReplaced(record) : record.ToArray();
            return PasswordMask.ReplaceKnown(shown, _secrets);
        }

        /// <summary>
        /// Masks text about to be printed (<see cref="PasswordMask.Text(string, string[])"/>).
        /// </summary>
        /// <param name="text">
        /// The text.
        /// </param>
        /// <returns>
        /// The masked text.
        /// </returns>
        public string Text(string text) => PasswordMask.Text(text, _secrets);
    }
}
