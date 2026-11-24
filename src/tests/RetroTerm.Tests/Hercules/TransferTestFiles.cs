//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs/RetroTerm
// RetroTerm — Connecting today's machines to yesterday's systems
//

using System;
using System.Globalization;
using System.Text;

namespace RetroTerm.Tests.Hercules
{
    /// <summary>
    /// The PC files the large IND$FILE transfers send: the same bytes on every run, so a
    /// recording made from them can be made again and compared.
    /// </summary>
    internal static class TransferTestFiles
    {
        /// <summary>
        /// A Windows text file of about the given size: lines of 20 to 79 printable characters,
        /// each ended by CR LF, each starting with its own line number. Every line fits the
        /// LRECL 255 that TK5's IND$FILE gives a new data set (IND-FILE-TRANSFER.md, open
        /// question 4), so nothing is cut or folded on the host.
        /// </summary>
        /// <param name="bytes">
        /// The size wanted; the file stops at the first whole line that reaches it.
        /// </param>
        /// <returns>
        /// The file's bytes, ASCII.
        /// </returns>
        public static byte[] Text(int bytes)
        {
            var text = new StringBuilder(bytes + 100);
            int line = 1;
            while (text.Length < bytes)
            {
                string head = "LINE " + line.ToString("D5", CultureInfo.InvariantCulture) + " ";
                text.Append(head);
                int length = 20 + ((line * 37) % 60);
                for (int c = head.Length; c < length; c++)
                {
                    // Letters, digits and the punctuation that is in both code page 37 and ASCII
                    // the same way under IND$FILE's own table, never a character that table maps
                    // differently (open question 8): A-Z, 0-9, blank, period, comma.
                    int k = (line + c) % 39;
                    char ch = k < 26 ? (char)('A' + k) : k < 36 ? (char)('0' + (k - 26)) : k == 36 ? ' ' : k == 37 ? '.' : ',';
                    text.Append(ch);
                }

                // No blank at the end of a line: the host may drop trailing blanks of a record.
                if (text[text.Length - 1] == ' ')
                {
                    text[text.Length - 1] = 'X';
                }

                text.Append("\r\n");
                line++;
            }

            return Encoding.ASCII.GetBytes(text.ToString());
        }

        /// <summary>
        /// A binary file of the given size: every byte value 0 to 255, in an order that is not a
        /// simple count, so a block sent twice or out of order shows.
        /// </summary>
        /// <param name="bytes">
        /// The size.
        /// </param>
        /// <returns>
        /// The file's bytes.
        /// </returns>
        public static byte[] Binary(int bytes)
        {
            var data = new byte[bytes];
            uint x = 0x12345678;
            for (int i = 0; i < data.Length; i++)
            {
                // xorshift32, a fixed sequence (Marsaglia 2003).
                x ^= x << 13;
                x ^= x >> 17;
                x ^= x << 5;
                data[i] = (byte)x;
            }

            return data;
        }
    }
}
