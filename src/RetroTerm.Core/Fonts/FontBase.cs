using System;

namespace RetroTerm.Core.Fonts
{
    public class FontBase
    {
        // Size of the glyph
        public ushort Height { get; internal set; }
        public ushort HeightToUse { get; internal set; }
        public ushort Width { get; internal set; }
        public ushort stretchY { get; internal set; }
        public bool IncludeBlankSpace { get; internal set; }

        /// <summary>
        /// Glyph bitmap table. Populated by the derived font class after construction,
        /// so it is null on a bare FontBase - GetFontBits null-checks it.
        /// </summary>
        public ushort[]? glyphs { get; internal set; }

        /// <summary>
        /// Optional table with offset into the Glyph table for different font numbers.
        /// Null for fonts that have only one font number.
        /// </summary>
        public ushort[]? fontNumOffset { get; internal set; }

        /// <summary>
        /// Map SPACE (0x20) to some other character in the font table ?
        /// Default seem to use position 0 
        /// </summary>
        public ushort mapSpaceChar { get; internal set; } = 0x0;

        /// <summary>
        /// Active ISO 646 national variant of character set 1
        /// (0 = International, 1 = Norwegian/Danish, 2 = Swedish, 3 = German).
        ///
        /// Declared here, not just on the TDV fonts, so a caller holding a FontBase can ask which
        /// variant is live without type-testing for each font class. Fonts with no national
        /// variants simply keep 0. TDV2200 and TDV2215 override it — they react differently
        /// (2200 remaps into the ROM, 2215 swaps a whole charset array).
        /// </summary>
        public virtual int CharacterSetVariant { get; set; }

        /// <summary>
        /// Returns the bitmap rows for one character in one of the font's character sets.
        /// </summary>
        /// <param name="fontValue">
        /// The character's position in the ROM, which for an ordinary character is its code.
        /// </param>
        /// <param name="fontNum">
        /// Which character set to take it from - 0 standard, 2 graphics, 3 subscript, 4 control
        /// codes. See <see cref="fontNumOffset"/>, which turns this into a ROM offset.
        /// </param>
        /// <returns>
        /// The glyph bit rows, or null when the font has no glyph at that position.
        /// </returns>
        /// <remarks>
        /// <para><b>Every glyph the TDV renderer draws comes through here</b></para>
        /// The renderer selects a glyph from the RAW character plus the cell's font number rather
        /// than from a pre-mapped codepoint. That is why a cell can hold the letter j and draw a
        /// vertical line: <c>TerminalCell.FontNumber</c> is 2, and this method reads the graphics
        /// set. A caller that maps the character itself before writing the cell produces a cell
        /// with the right font number and no drawable glyph - see the note in
        /// <c>TDVInputProcessor</c> about why single shifts are left to the parser.
        ///
        /// <para><b>Overridden where the ROM is not a flat table</b></para>
        /// <c>FontTDV2200</c> remaps the ISO 646 wire positions to national glyphs stored
        /// elsewhere in the ROM; <c>FontTDV2215</c> swaps a whole character-set array per font
        /// number. Both call back here once they have chosen where to read from.
        /// </remarks>
        public virtual ushort[]? GetFontBits(ushort fontValue, int fontNum)
        {
            // Map space character to some other offset ?
            // Seem the VT fonts has some odd shit in the default position.
            if (fontValue == 0x20) // space
                fontValue = mapSpaceChar;

            int glyphOffset = 0;

            if (fontNum > 0)
            {
                if (fontNumOffset != null)
                {
                    if ((fontNumOffset.Length >= fontNum))
                    {
                        glyphOffset = fontNumOffset[fontNum];
                    }

                    fontValue = (ushort)(fontValue + glyphOffset);
                }
            }

            int startPos = fontValue * (Height / stretchY);

            if (glyphs == null) return null;
            if (startPos >= glyphs.Length) return null;

            ushort[] fb = new ushort[Height];
            for (int i = 0; i < Height; i++)
            {
                int bitrow = i / stretchY;

                if (stretchY > 1 && i % 2 == 1)
                {
                    fb[i] = 0;
                }
                else
                    fb[i] = glyphs[bitrow + startPos];
            }

            return fb;
        }
    }
}
