namespace RetroTerm.Core.Terminal;

/// <summary>
/// How many terminal columns a Unicode codepoint occupies.
///
/// A terminal grid assumes one character per cell, and for Latin text that holds. It does not
/// hold for the two cases below, and getting them wrong corrupts the rest of the line — every
/// character after the mistake sits one column out of place:
///
/// - Width 2 — East Asian Wide and Fullwidth characters. A CJK ideograph is drawn
///   twice as wide and occupies TWO cells. Writing it into one leaves the character clipped and
///   the following text overlapping it.
/// - Width 0 — combining marks. A combining acute does not occupy a cell of its own;
///   it modifies the character before it. Treating it as width 1 consumes a cell that belongs to
///   the next character, so "e" + acute over "0123" wrongly swallowed the "1".
///
/// The ranges are from Unicode's East Asian Width property (UAX #11) and the combining-mark
/// general categories (Mn, Me, and the Cf zero-width formatting characters). They are checked
/// with a binary search over sorted range tables rather than a dictionary, so the hot path costs
/// a handful of comparisons and allocates nothing.
///
/// NOT a full UAX #11 implementation: it covers the blocks that appear in practice. A codepoint
/// outside every table is treated as width 1, which is the safe default — the character draws in
/// one cell and nothing after it shifts.
/// </summary>
public static class CharacterWidth
{
    /// <summary>
    /// Zero-width codepoints: combining marks (Mn/Me) and the zero-width format characters.
    /// Sorted, inclusive [low, high] pairs.
    /// </summary>
    private static readonly uint[] ZeroWidthRanges =
    {
        0x0300, 0x036F,   // Combining Diacritical Marks
        0x0483, 0x0489,   // Cyrillic combining
        0x0591, 0x05BD,   // Hebrew points
        0x05BF, 0x05BF,
        0x05C1, 0x05C2,
        0x05C4, 0x05C5,
        0x05C7, 0x05C7,
        0x0610, 0x061A,   // Arabic
        0x064B, 0x065F,
        0x0670, 0x0670,
        0x06D6, 0x06DC,
        0x06DF, 0x06E4,
        0x06E7, 0x06E8,
        0x06EA, 0x06ED,
        0x0711, 0x0711,   // Syriac
        0x0730, 0x074A,
        0x07A6, 0x07B0,   // Thaana
        0x07EB, 0x07F3,
        0x0816, 0x0819,
        0x081B, 0x0823,
        0x0825, 0x0827,
        0x0829, 0x082D,
        0x0859, 0x085B,
        0x08E3, 0x0902,
        0x093A, 0x093A,   // Devanagari
        0x093C, 0x093C,
        0x0941, 0x0948,
        0x094D, 0x094D,
        0x0951, 0x0957,
        0x0962, 0x0963,
        0x0981, 0x0981,   // Bengali
        0x09BC, 0x09BC,
        0x09C1, 0x09C4,
        0x09CD, 0x09CD,
        0x09E2, 0x09E3,
        0x0A01, 0x0A02,   // Gurmukhi
        0x0A3C, 0x0A3C,
        0x0A41, 0x0A42,
        0x0A47, 0x0A48,
        0x0A4B, 0x0A4D,
        0x0A70, 0x0A71,
        0x0A81, 0x0A82,   // Gujarati
        0x0ABC, 0x0ABC,
        0x0AC1, 0x0AC5,
        0x0AC7, 0x0AC8,
        0x0ACD, 0x0ACD,
        0x0B01, 0x0B01,   // Oriya
        0x0B3C, 0x0B3C,
        0x0B3F, 0x0B3F,
        0x0B41, 0x0B44,
        0x0B4D, 0x0B4D,
        0x0B82, 0x0B82,   // Tamil
        0x0BC0, 0x0BC0,
        0x0BCD, 0x0BCD,
        0x0C00, 0x0C00,   // Telugu
        0x0C3E, 0x0C40,
        0x0C46, 0x0C48,
        0x0C4A, 0x0C4D,
        0x0C81, 0x0C81,   // Kannada
        0x0CBC, 0x0CBC,
        0x0CCC, 0x0CCD,
        0x0D01, 0x0D01,   // Malayalam
        0x0D41, 0x0D44,
        0x0D4D, 0x0D4D,
        0x0DCA, 0x0DCA,   // Sinhala
        0x0DD2, 0x0DD4,
        0x0E31, 0x0E31,   // Thai
        0x0E34, 0x0E3A,
        0x0E47, 0x0E4E,
        0x0EB1, 0x0EB1,   // Lao
        0x0EB4, 0x0EB9,
        0x0EC8, 0x0ECD,
        0x0F18, 0x0F19,   // Tibetan
        0x0F35, 0x0F35,
        0x0F37, 0x0F37,
        0x0F39, 0x0F39,
        0x0F71, 0x0F7E,
        0x0F80, 0x0F84,
        0x0F86, 0x0F87,
        0x102D, 0x1030,   // Myanmar
        0x1032, 0x1037,
        0x1039, 0x103A,
        0x1058, 0x1059,
        0x135D, 0x135F,   // Ethiopic
        0x1712, 0x1714,
        0x1732, 0x1734,
        0x1752, 0x1753,
        0x1772, 0x1773,
        0x17B4, 0x17B5,   // Khmer
        0x17B7, 0x17BD,
        0x17C6, 0x17C6,
        0x17C9, 0x17D3,
        0x180B, 0x180E,   // Mongolian free variation selectors
        0x18A9, 0x18A9,
        0x1A17, 0x1A18,
        0x1AB0, 0x1ABE,
        0x1B00, 0x1B03,
        0x1B34, 0x1B34,
        0x1B6B, 0x1B73,
        0x1DC0, 0x1DFF,   // Combining Diacritical Marks Supplement
        0x200B, 0x200F,   // Zero-width space / joiners / directional marks
        0x202A, 0x202E,   // Directional formatting
        0x2060, 0x2064,   // Word joiner, invisible operators
        0x206A, 0x206F,
        0x20D0, 0x20F0,   // Combining Diacritical Marks for Symbols
        0xFE00, 0xFE0F,   // Variation selectors
        0xFE20, 0xFE2F,   // Combining Half Marks
        0xFEFF, 0xFEFF,   // Zero-width no-break space (BOM)
        0xFFF9, 0xFFFB,   // Interlinear annotation
        0x1D167, 0x1D169, // Musical symbols
        0x1D17B, 0x1D182,
        0x1D185, 0x1D18B,
        0x1D1AA, 0x1D1AD,
        0xE0100, 0xE01EF  // Variation Selectors Supplement
    };

    /// <summary>
    /// Double-width codepoints: East Asian Wide (W) and Fullwidth (F).
    /// Sorted, inclusive [low, high] pairs.
    /// </summary>
    private static readonly uint[] WideRanges =
    {
        0x1100, 0x115F,   // Hangul Jamo initial consonants
        0x2E80, 0x2EFF,   // CJK Radicals Supplement
        0x2F00, 0x2FDF,   // Kangxi Radicals
        0x2FF0, 0x2FFF,   // Ideographic Description Characters
        0x3000, 0x303E,   // CJK Symbols and Punctuation (incl. ideographic space)
        0x3041, 0x33FF,   // Hiragana, Katakana, Bopomofo, Hangul Compatibility Jamo, CJK compat
        0x3400, 0x4DBF,   // CJK Unified Ideographs Extension A
        0x4E00, 0x9FFF,   // CJK Unified Ideographs
        0xA000, 0xA4CF,   // Yi Syllables and Radicals
        0xA960, 0xA97F,   // Hangul Jamo Extended-A
        0xAC00, 0xD7A3,   // Hangul Syllables
        0xF900, 0xFAFF,   // CJK Compatibility Ideographs
        0xFE10, 0xFE19,   // Vertical forms
        0xFE30, 0xFE6F,   // CJK Compatibility Forms, Small Form Variants
        0xFF00, 0xFF60,   // Fullwidth ASCII variants — includes U+FF10 FULLWIDTH DIGIT ZERO
        0xFFE0, 0xFFE6,   // Fullwidth signs
        0x16FE0, 0x16FE4,
        0x17000, 0x187F7, // Tangut
        0x18800, 0x18AFF,
        0x1B000, 0x1B12F, // Kana supplement
        0x1F004, 0x1F004,
        0x1F0CF, 0x1F0CF,
        0x1F18E, 0x1F18E,
        0x1F191, 0x1F19A,
        0x1F200, 0x1F320, // Emoji: enclosed ideographic, weather, and friends
        0x1F32D, 0x1F335,
        0x1F337, 0x1F37C,
        0x1F37E, 0x1F393,
        0x1F3A0, 0x1F3CA,
        0x1F3CF, 0x1F3D3,
        0x1F3E0, 0x1F3F0,
        0x1F3F4, 0x1F3F4,
        0x1F3F8, 0x1F43E,
        0x1F440, 0x1F440,
        0x1F442, 0x1F4FC,
        0x1F4FF, 0x1F53D,
        0x1F54B, 0x1F54E,
        0x1F550, 0x1F567,
        0x1F57A, 0x1F57A,
        0x1F595, 0x1F596,
        0x1F5A4, 0x1F5A4,
        0x1F5FB, 0x1F64F,
        0x1F680, 0x1F6C5,
        0x1F6CC, 0x1F6CC,
        0x1F6D0, 0x1F6D2,
        0x1F6EB, 0x1F6EC,
        0x1F6F4, 0x1F6F9,
        0x1F910, 0x1F93E,
        0x1F940, 0x1F970,
        0x1F973, 0x1F976,
        0x1F97A, 0x1F9A2,
        0x1F9B0, 0x1F9B9,
        0x1F9C0, 0x1F9C2,
        0x1F9D0, 0x1F9FF,
        0x20000, 0x2FFFD, // CJK Unified Ideographs Extension B and beyond
        0x30000, 0x3FFFD
    };

    /// <summary>
    /// Columns this codepoint occupies: 0 for a combining mark, 2 for a wide character, 1 for
    /// everything else.
    /// </summary>
    public static int Of(uint codepoint)
    {
        // Fast path: ASCII is the overwhelming majority of terminal traffic and is always width 1.
        if (codepoint < 0x0300)
        {
            return 1;
        }

        if (InRanges(ZeroWidthRanges, codepoint))
        {
            return 0;
        }

        if (InRanges(WideRanges, codepoint))
        {
            return 2;
        }

        return 1;
    }

    /// <summary>
    /// True when the codepoint is a combining mark or otherwise takes no column.
    /// </summary>
    public static bool IsZeroWidth(uint codepoint) => Of(codepoint) == 0;

    /// <summary>
    /// True when the codepoint needs two cells.
    /// </summary>
    public static bool IsWide(uint codepoint) => Of(codepoint) == 2;

    /// <summary>
    /// Binary search over a sorted flat array of inclusive [low, high] pairs.
    /// </summary>
    private static bool InRanges(uint[] ranges, uint codepoint)
    {
        int low = 0;
        int high = ranges.Length / 2 - 1;

        while (low <= high)
        {
            int mid = (low + high) / 2;
            uint rangeLow = ranges[mid * 2];
            uint rangeHigh = ranges[mid * 2 + 1];

            if (codepoint < rangeLow)
            {
                high = mid - 1;
            }
            else if (codepoint > rangeHigh)
            {
                low = mid + 1;
            }
            else
            {
                return true;
            }
        }

        return false;
    }
}
