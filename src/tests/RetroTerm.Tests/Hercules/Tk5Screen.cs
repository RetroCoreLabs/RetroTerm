//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs/RetroTerm
// RetroTerm — Connecting today's machines to yesterday's systems
//

using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace RetroTerm.Tests.Hercules
{
    /// <summary>
    /// One 3270 screen as a person sees it: 24 rows of 80 characters, the cursor, and where each
    /// field starts with its attribute byte. Built the same way from RetroTerm and from s3270 so
    /// the two can be compared.
    /// </summary>
    internal sealed class Tk5Screen
    {
        public const int Rows = 24;
        public const int Columns = 80;

        // Text that changes from run to run and from terminal to terminal, masked before two
        // screens are compared: times, dates, the terminal address (CUU0C1) and the logon time line.
        private static readonly Regex s_changing = new Regex(
            @"\d\d:\d\d(:\d\d)?|\d\d\.\d\d\.\d\d|CUU0C\w|ON [A-Z]+ \d\d?, \d{4}|TERMINAL : \w+",
            RegexOptions.Compiled);

        public Tk5Screen(string[] lines, int cursorRow, int cursorColumn, List<int> fieldAddresses, List<byte> fieldAttributes)
        {
            Lines = new string[Rows];
            for (int r = 0; r < Rows; r++)
            {
                string line = r < lines.Length ? lines[r] : string.Empty;
                Lines[r] = line.Length >= Columns ? line.Substring(0, Columns) : line.PadRight(Columns);
            }

            CursorRow = cursorRow;
            CursorColumn = cursorColumn;
            FieldAddresses = fieldAddresses;
            FieldAttributes = fieldAttributes;
        }

        public string[] Lines { get; }

        public int CursorRow { get; }

        public int CursorColumn { get; }

        public List<int> FieldAddresses { get; }

        public List<byte> FieldAttributes { get; }

        public bool Contains(string text)
        {
            for (int r = 0; r < Rows; r++)
            {
                if (Lines[r].Contains(text, System.StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        public string Text()
        {
            var sb = new StringBuilder(Rows * (Columns + 1));
            for (int r = 0; r < Rows; r++)
            {
                sb.Append(Lines[r].TrimEnd()).Append('\n');
            }

            return sb.ToString();
        }

        /// <summary>
        /// Lists every difference between this screen and another, after masking the text that
        /// changes between runs. Empty when they match.
        /// </summary>
        public List<string> DifferencesFrom(Tk5Screen other, string theirName)
        {
            var found = new List<string>();
            for (int r = 0; r < Rows; r++)
            {
                string mine = s_changing.Replace(Lines[r], "#");
                string theirs = s_changing.Replace(other.Lines[r], "#");
                if (mine != theirs)
                {
                    found.Add("row " + (r + 1) + ":\n  RetroTerm: [" + mine.TrimEnd() + "]\n  " + theirName + ": [" + theirs.TrimEnd() + "]");
                }
            }

            if (CursorRow != other.CursorRow || CursorColumn != other.CursorColumn)
            {
                found.Add("cursor: RetroTerm " + (CursorRow + 1) + "," + (CursorColumn + 1) + ", " + theirName + " " + (other.CursorRow + 1) + "," + (other.CursorColumn + 1));
            }

            if (FieldAddresses.Count != other.FieldAddresses.Count)
            {
                found.Add("field count: RetroTerm " + FieldAddresses.Count + ", " + theirName + " " + other.FieldAddresses.Count);
                return found;
            }

            for (int i = 0; i < FieldAddresses.Count; i++)
            {
                // Bits 0-1 of an attribute byte only make it a printable EBCDIC character
                // (GA23-0059-4 p. 4-13), so compare the six bits that mean something.
                if (FieldAddresses[i] != other.FieldAddresses[i] || (FieldAttributes[i] & 0x3F) != (other.FieldAttributes[i] & 0x3F))
                {
                    found.Add("field " + i + ": RetroTerm at " + FieldAddresses[i] + " attribute " + FieldAttributes[i].ToString("X2") +
                        ", " + theirName + " at " + other.FieldAddresses[i] + " attribute " + other.FieldAttributes[i].ToString("X2"));
                }
            }

            return found;
        }
    }
}
