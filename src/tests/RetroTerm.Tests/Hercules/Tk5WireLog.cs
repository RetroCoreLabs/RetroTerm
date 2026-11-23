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

using RetroTerm.Tests.Oracle;

namespace RetroTerm.Tests.Hercules
{
    /// <summary>
    /// Every record in both directions, in order, as hex lines: what
    /// <see cref="RetroTermTk5Terminal"/> prints when a wait fails, so a failure says what
    /// crossed the wire instead of only what the screen ended up as.
    /// </summary>
    /// <remarks>
    /// What the client sent goes through <see cref="SentDataMask"/> before it is written down,
    /// so the logon record never shows the password (see PasswordMask). Written from the network and
    /// session threads, so it is locked.
    /// </remarks>
    internal sealed class Tk5WireLog
    {
        // 256 bytes: enough for a whole IND$FILE message record (112 bytes on TK5).
        private const int Shown = 256;

        private readonly List<string> _lines = new List<string>();
        private readonly SentDataMask _mask;

        public Tk5WireLog()
            : this(new SentDataMask())
        {
        }

        public Tk5WireLog(SentDataMask mask)
        {
            _mask = mask;
        }

        /// <summary>
        /// Notes one whole host record, telnet layer removed.
        /// </summary>
        /// <param name="record">
        /// The record.
        /// </param>
        public void Host(ReadOnlySpan<byte> record)
        {
            lock (_lines)
            {
                _mask.Host(record);
                _lines.Add(Line("host", record));
            }
        }

        /// <summary>
        /// Notes one whole record the client sent, telnet layer removed; it is masked first.
        /// </summary>
        /// <param name="record">
        /// The record.
        /// </param>
        public void Sent(ReadOnlySpan<byte> record)
        {
            lock (_lines)
            {
                byte[] masked = _mask.Client(record);
                _lines.Add(Line("sent", masked));
            }
        }

        /// <summary>
        /// Every record both ways so far, oldest first, one line each (256 bytes shown), with
        /// the known passwords masked once more in every form.
        /// </summary>
        /// <returns>
        /// The lines.
        /// </returns>
        public string Text()
        {
            lock (_lines)
            {
                return _mask.Text(string.Join("\n", _lines));
            }
        }

        private static string Line(string direction, ReadOnlySpan<byte> data)
        {
            var line = new StringBuilder(direction.Length + 2 + (data.Length * 3));
            line.Append(direction).Append(':');
            int shown = data.Length > Shown ? Shown : data.Length;
            for (int i = 0; i < shown; i++)
            {
                line.Append(' ').Append(data[i].ToString("X2", CultureInfo.InvariantCulture));
            }

            if (shown < data.Length)
            {
                line.Append(" ... (").Append(data.Length).Append(" bytes)");
            }

            return line.ToString();
        }
    }
}
