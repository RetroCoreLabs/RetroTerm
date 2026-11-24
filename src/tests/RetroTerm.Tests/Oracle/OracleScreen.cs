//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs/RetroTerm
// RetroTerm — Connecting today's machines to yesterday's systems
//

using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace RetroTerm.Tests.Oracle
{
    /// <summary>
    /// One 3270 screen of any size, read the same way from RetroTerm and from s3270 so the two
    /// can be compared position by position: the buffer itself (each position's EBCDIC code, or
    /// its field attribute), the text a person sees, the cursor, the keyboard lock and the
    /// highlight of every position.
    /// </summary>
    /// <remarks>
    /// Unlike the live TK5 comparison (Hercules\Tk5Screen) nothing is masked: a replayed host
    /// sends both terminals the very same bytes, times and dates included.
    /// </remarks>
    internal sealed class OracleScreen
    {
        public OracleScreen(int rows, int columns, string[] lines, int cursorAddress, bool locked, byte[] codes, bool[] fieldAttribute, bool[] graphicEscape, byte[] highlights, byte[] characterSets)
        {
            Rows = rows;
            Columns = columns;
            Lines = new string[rows];
            for (int r = 0; r < rows; r++)
            {
                string line = r < lines.Length ? lines[r] : string.Empty;
                Lines[r] = line.Length >= columns ? line.Substring(0, columns) : line.PadRight(columns);
            }

            CursorAddress = cursorAddress;
            Locked = locked;
            Codes = codes;
            FieldAttribute = fieldAttribute;
            GraphicEscape = graphicEscape;
            Highlights = highlights;
            CharacterSets = characterSets;
        }

        public int Rows { get; }

        public int Columns { get; }

        public string[] Lines { get; }

        public int CursorAddress { get; }

        public bool Locked { get; }

        /// <summary>
        /// Each position's byte: the EBCDIC code of a character, or the attribute byte of a field
        /// attribute (compared in its low six bits only: bits 0-1 only make it printable,
        /// GA23-0059-4 p. 4-13).
        /// </summary>
        public byte[] Codes { get; }

        public bool[] FieldAttribute { get; }

        /// <summary>
        /// True where the character came in after a Graphic Escape (GE order), so its code is in
        /// the graphic escape character set (code page 310: box corners, lines, APL).
        /// </summary>
        public bool[] GraphicEscape { get; }

        /// <summary>
        /// The low four bits of each position's highlight, as x3270 keeps them (see
        /// Hercules\Tk5Screen.Highlights).
        /// </summary>
        public byte[] Highlights { get; }

        /// <summary>
        /// The character set each position is shown in (type X'43', GA23-0059-4 p. 4-19): the
        /// character's own from SA, or else its field's from SFE, X'00' for the default set.
        /// X'F1' is code page 310, the same set Graphic Escape reaches.
        /// </summary>
        public byte[] CharacterSets { get; }

        public string Text()
        {
            var text = new StringBuilder();
            for (int r = 0; r < Rows; r++)
            {
                text.Append(Lines[r].TrimEnd()).Append('\n');
            }

            text.Append("[cursor ").Append(Position(CursorAddress)).Append(Locked ? ", keyboard locked]" : ", keyboard unlocked]");
            return text.ToString();
        }

        /// <summary>
        /// Every difference from another screen, at most a few of each kind with the count.
        /// </summary>
        /// <param name="other">
        /// The reference screen.
        /// </param>
        /// <param name="theirName">
        /// The reference's name in the report.
        /// </param>
        /// <returns>
        /// The differences; empty when the screens match.
        /// </returns>
        public List<string> DifferencesFrom(OracleScreen other, string theirName)
        {
            var found = new List<string>();
            if (Rows != other.Rows || Columns != other.Columns)
            {
                found.Add("screen size: RetroTerm " + Columns + "x" + Rows + ", " + theirName + " " + other.Columns + "x" + other.Rows);
                return found;
            }

            for (int r = 0; r < Rows; r++)
            {
                if (Lines[r] != other.Lines[r] && !OnlyX3270Cp310Choices(other, r))
                {
                    found.Add("text of row " + (r + 1) + ":\n    RetroTerm: [" + Lines[r].TrimEnd() + "]\n    " + theirName + ": [" + other.Lines[r].TrimEnd() + "]");
                }
            }

            if (CursorAddress != other.CursorAddress)
            {
                found.Add("cursor: RetroTerm " + Position(CursorAddress) + ", " + theirName + " " + Position(other.CursorAddress));
            }

            if (Locked != other.Locked)
            {
                found.Add("keyboard: RetroTerm " + (Locked ? "locked" : "unlocked") + ", " + theirName + " " + (other.Locked ? "locked" : "unlocked"));
            }

            int buffer = 0;
            int highlight = 0;
            int characterSet = 0;
            for (int a = 0; a < Codes.Length; a++)
            {
                if (CharacterSets[a] != other.CharacterSets[a] && characterSet++ < 6)
                {
                    found.Add("character set at " + Position(a) + ": RetroTerm X'" + CharacterSets[a].ToString("X2", CultureInfo.InvariantCulture) + "', " + theirName + " X'" +
                        other.CharacterSets[a].ToString("X2", CultureInfo.InvariantCulture) + "'");
                }

                string? mine = Cell(this, a);
                string? theirs = Cell(other, a);
                if (mine != theirs && buffer++ < 6)
                {
                    found.Add("buffer at " + Position(a) + ": RetroTerm " + mine + ", " + theirName + " " + theirs);
                }

                if (Highlights[a] != other.Highlights[a] && highlight++ < 6)
                {
                    found.Add("highlight at " + Position(a) + ": RetroTerm " + Highlights[a].ToString("X1", CultureInfo.InvariantCulture) + ", " + theirName + " " +
                        other.Highlights[a].ToString("X1", CultureInfo.InvariantCulture));
                }
            }

            if (buffer > 6)
            {
                found.Add("buffer: " + buffer + " positions differ in all");
            }

            if (highlight > 6)
            {
                found.Add("highlight: " + highlight + " positions differ in all");
            }

            if (characterSet > 6)
            {
                found.Add("character set: " + characterSet + " positions differ in all");
            }

            return found;
        }

        // The code page 310 bytes for which x3270 shows another Unicode character than RetroTerm,
        // each with x3270's character. Taken from docs\spec\IBM3270\EBCDIC.md, "Every
        // disagreement between the sources", code page 310, where each one is settled against
        // the IBM registry name and the GA27-2837-9 picture, and RetroTerm follows that answer:
        // X'C1' Left Parenthesis Superscript (x3270 superscript equals, wrong), X'C2' Plus Sign
        // Superscript and X'D2' Minus Sign Superscript (x3270 the plain signs), X'C3' Solid
        // Square (x3270 U+220E), X'95' Solid Fill Character (x3270 a small square), X'9C'
        // Lozenge (x3270 the currency sign), X'E2' and X'E3' Two and Three Subscript (x3270
        // U+0282 and U+0283, a typing error).
        private static readonly byte[] X3270Cp310Bytes = { 0xC1, 0xC2, 0xD2, 0xC3, 0x95, 0x9C, 0xE2, 0xE3 };

        private static readonly char[] X3270Cp310Chars = { '⁼', '+', '-', '∎', '■', '¤', 'ʂ', 'ʃ' };

        // True when every text difference on the row is at a position that both terminals hold
        // as the SAME code page 310 byte (Graphic Escape or character set X'F1', compared on
        // their own below) and the other terminal shows x3270's character for that byte while
        // RetroTerm shows its own from code page 310. Any other difference on the row is still
        // reported: the byte and the set are what the data stream says, and they must agree.
        private bool OnlyX3270Cp310Choices(OracleScreen other, int row)
        {
            for (int c = 0; c < Columns; c++)
            {
                char mine = Lines[row][c];
                char theirs = other.Lines[row][c];
                if (mine == theirs)
                {
                    continue;
                }

                int a = (row * Columns) + c;
                bool cp310 = !FieldAttribute[a] && !other.FieldAttribute[a] && Codes[a] == other.Codes[a]
                    && (GraphicEscape[a] || CharacterSets[a] == 0xF1) && GraphicEscape[a] == other.GraphicEscape[a]
                    && CharacterSets[a] == other.CharacterSets[a];
                if (!cp310 || mine != RetroIO.Protocols.Tn3270.CodePage.Cp310.ToUnicode(Codes[a]))
                {
                    return false;
                }

                int known = System.Array.IndexOf(X3270Cp310Bytes, Codes[a]);
                if (known < 0 || X3270Cp310Chars[known] != theirs)
                {
                    return false;
                }
            }

            return true;
        }

        private string Position(int address) => "row " + ((address / Columns) + 1) + " column " + ((address % Columns) + 1);

        private static string Cell(OracleScreen s, int a)
        {
            if (s.FieldAttribute[a])
            {
                return "field attribute X'" + (s.Codes[a] & 0x3F).ToString("X2", CultureInfo.InvariantCulture) + "' (low six bits)";
            }

            return (s.GraphicEscape[a] ? "GE " : string.Empty) + "X'" + s.Codes[a].ToString("X2", CultureInfo.InvariantCulture) + "'";
        }
    }
}
