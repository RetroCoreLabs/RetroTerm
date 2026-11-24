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

        public Tk5Screen(string[] lines, int cursorRow, int cursorColumn, List<int> fieldAddresses, List<byte> fieldAttributes, string status = "", string line25 = "", byte[]? highlights = null, byte[]? colours = null)
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
            Status = status;
            Line25 = line25;
            Highlights = highlights;
            Colours = colours;
        }

        public string[] Lines { get; }

        /// <summary>
        /// A row with the text that changes between runs (times, dates, terminal address) masked,
        /// as <see cref="DifferencesFrom(Tk5Screen, string, int[])"/> compares it.
        /// </summary>
        /// <param name="row">
        /// The row, counted from 0.
        /// </param>
        /// <returns>
        /// The masked text.
        /// </returns>
        public string MaskedLine(int row) => s_changing.Replace(Lines[row], "#");

        public int CursorRow { get; }

        public int CursorColumn { get; }

        public List<int> FieldAddresses { get; }

        public List<byte> FieldAttributes { get; }

        /// <summary>
        /// Line 25, the status line, as the first eight fields of s3270's status: keyboard (U
        /// unlocked, L locked for any reason), screen formatted (F) or
        /// not (U), field at the cursor protected (P) or not (U), connection C(host), mode (I =
        /// 3270), model, rows, columns. Empty when the terminal gave none.
        /// </summary>
        /// <remarks>
        /// s3270 has no drawn status line; these fields are the same state the 3278's line 25
        /// shows (GA27-2890-4 p. 2-19 to 2-23: readiness and connection, Do Not Enter and its
        /// reason). That manual gives the meaning of each symbol but not its column, so the
        /// state is compared here, not the drawing.
        /// </remarks>
        public string Status { get; }

        /// <summary>
        /// Line 25 as drawn: RetroTerm's real row 25, or for s3270 (which draws nothing) the text
        /// wc3270 draws for the state s3270 reports. Empty when there is none.
        /// </summary>
        public string Line25 { get; }

        /// <summary>
        /// The highlight of every buffer position, in s3270's terms: a field attribute's own
        /// highlight at its position, a character's own (SA) highlight elsewhere, as the low four
        /// bits (0 default or normal, 1 blink, 2 reverse, 4 underscore). Null when not read.
        /// </summary>
        /// <remarks>
        /// x3270 keeps only the low four bits (ctlr.c, ctlr_add_gr with value AND 0x0F) and prints
        /// them with F0 added (task.c, the ReadBuffer loop), so "default" X'00' and "normal" X'F0'
        /// are one value there and are made one here. Colour is NOT compared: s3270 runs as a
        /// 3278, and x3270 drops every colour a 3278 is sent (ctlr.c, each colour store is inside
        /// "if (mode3279)").
        /// </remarks>
        public byte[]? Highlights { get; }

        /// <summary>
        /// The foreground colour of every buffer position, as the host set it: a field
        /// attribute's own colour at its position, a character's own (SA) colour elsewhere, X'00'
        /// for "the default" and X'F1' to X'F7' for the seven colours (GA23-0061-2 p. 2-10). Null
        /// when not read, which is always for s3270 started as a 3278 (it drops colours, see
        /// <see cref="Highlights"/>).
        /// </summary>
        public byte[]? Colours { get; }

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

            sb.Append("[line 25: ").Append(Status).Append("]\n");
            if (Line25.Length > 0)
            {
                sb.Append('|').Append(Line25).Append("|\n");
            }


            return sb.ToString();
        }

        /// <summary>
        /// Lists every difference between this screen and another, after masking the text that
        /// changes between runs. Empty when they match.
        /// </summary>
        public List<string> DifferencesFrom(Tk5Screen other, string theirName) => DifferencesFrom(other, theirName, null);

        /// <summary>
        /// As <see cref="DifferencesFrom(Tk5Screen, string)"/>, with the text of the rows given
        /// left out (everything else, fields, colours, highlights and cursor, is still compared).
        /// </summary>
        /// <param name="other">
        /// The other terminal's screen.
        /// </param>
        /// <param name="theirName">
        /// Its name, for the report.
        /// </param>
        /// <param name="rowsNotCompared">
        /// Rows counted from 0 whose text is not compared, or null.
        /// </param>
        /// <returns>
        /// The differences.
        /// </returns>
        public List<string> DifferencesFrom(Tk5Screen other, string theirName, int[]? rowsNotCompared)
        {
            var found = new List<string>();
            for (int r = 0; r < Rows; r++)
            {
                if (rowsNotCompared != null && System.Array.IndexOf(rowsNotCompared, r) >= 0)
                {
                    continue;
                }

                string mine = s_changing.Replace(Lines[r], "#");
                string theirs = s_changing.Replace(other.Lines[r], "#");
                if (mine != theirs)
                {
                    found.Add("row " + (r + 1) + ":\n  RetroTerm: [" + mine.TrimEnd() + "]\n  " + theirName + ": [" + theirs.TrimEnd() + "]");
                }
            }

            if (Status.Length > 0 && other.Status.Length > 0 && Status != other.Status)
            {
                found.Add("line 25: RetroTerm [" + Status + "], " + theirName + " [" + other.Status + "]");
            }

            if (Line25.Length > 0 && other.Line25.Length > 0 && Line25 != other.Line25)
            {
                found.Add("line 25 as drawn:\n  RetroTerm: |" + Line25 + "|\n  " + theirName + ": |" + other.Line25 + "|");
            }

            if (CursorRow != other.CursorRow || CursorColumn != other.CursorColumn)
            {
                found.Add("cursor: RetroTerm " + (CursorRow + 1) + "," + (CursorColumn + 1) + ", " + theirName + " " + (other.CursorRow + 1) + "," + (other.CursorColumn + 1));
            }

            if (Highlights != null && other.Highlights != null)
            {
                int shown = 0;
                for (int a = 0; a < Highlights.Length && a < other.Highlights.Length; a++)
                {
                    if (Highlights[a] != other.Highlights[a] && shown++ < 5)
                    {
                        found.Add("highlight at row " + ((a / Columns) + 1) + " column " + ((a % Columns) + 1) + ": RetroTerm " +
                            Highlights[a].ToString("X1") + ", " + theirName + " " + other.Highlights[a].ToString("X1"));
                    }
                }

                if (shown > 5)
                {
                    found.Add("highlight: " + shown + " positions differ in all");
                }
            }

            if (Colours != null && other.Colours != null)
            {
                int shown = 0;
                for (int a = 0; a < Colours.Length && a < other.Colours.Length; a++)
                {
                    if (Colours[a] != other.Colours[a] && shown++ < 5)
                    {
                        found.Add("colour at row " + ((a / Columns) + 1) + " column " + ((a % Columns) + 1) + ": RetroTerm " +
                            Colours[a].ToString("X2") + ", " + theirName + " " + other.Colours[a].ToString("X2"));
                    }
                }

                if (shown > 5)
                {
                    found.Add("colour: " + shown + " positions differ in all");
                }
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
