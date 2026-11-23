//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs/RetroTerm
// RetroTerm — Connecting today's machines to yesterday's systems
//

using System;
using System.Collections.Generic;
using System.Text;

namespace RetroTerm.Ibm3270
{
    /// <summary>
    /// One finished print job from a 3287: its pages, each a list of printed lines. A page ends where
    /// the host sent a Form Feed.
    /// </summary>
    /// <remarks>
    /// Lines carry no trailing spaces. The paper does not show them, and pr3287 drops them as well
    /// (x3270 Common\pr3287\ctlr.c, dump_formatted: "trailing spaces and nulls suppressed").
    /// </remarks>
    public sealed class PrintJob
    {
        private readonly List<List<string>> _pages;

        /// <summary>
        /// Creates a job from its pages.
        /// </summary>
        /// <param name="pages">
        /// The pages, in print order. The list is taken over, not copied.
        /// </param>
        /// <param name="bytesReceived">
        /// How many record bytes the host sent for this job.
        /// </param>
        internal PrintJob(List<List<string>> pages, long bytesReceived)
        {
            _pages = pages;
            BytesReceived = bytesReceived;
        }

        /// <summary>
        /// Gets the number of pages.
        /// </summary>
        public int PageCount => _pages.Count;

        /// <summary>
        /// Gets how many 3270 record bytes the host sent while this job was being built.
        /// </summary>
        public long BytesReceived { get; }

        /// <summary>
        /// Gets the lines of one page.
        /// </summary>
        /// <param name="page">
        /// The page, from 0.
        /// </param>
        /// <returns>
        /// The page's lines, top to bottom.
        /// </returns>
        public IReadOnlyList<string> Page(int page) => _pages[page];

        /// <summary>
        /// The job as plain text: every line ends with <paramref name="newLine"/>, and a Form Feed
        /// character (X'0C') stands between two pages, the way pr3287 writes a job to a file.
        /// </summary>
        /// <param name="newLine">
        /// The line end to write, for example "\r\n" on Windows.
        /// </param>
        /// <returns>
        /// The text of the whole job.
        /// </returns>
        public string ToText(string newLine)
        {
            ArgumentNullException.ThrowIfNull(newLine);
            var text = new StringBuilder();
            for (int page = 0; page < _pages.Count; page++)
            {
                if (page > 0)
                {
                    text.Append('\f');
                }

                List<string> lines = _pages[page];
                for (int line = 0; line < lines.Count; line++)
                {
                    text.Append(lines[line]).Append(newLine);
                }
            }

            return text.ToString();
        }
    }
}
