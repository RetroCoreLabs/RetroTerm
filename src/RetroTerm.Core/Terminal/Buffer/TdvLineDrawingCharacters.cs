namespace RetroTerm.Core.Terminal.Buffer;

/// <summary>
/// What a TDV line-drawing byte LOOKS like, as Unicode, for anything that reads the screen as
/// text rather than drawing it.
/// </summary>
/// <remarks>
/// <para><b>Why this is needed at all</b></para>
/// A VT-family emulator converts its Special Graphics bytes to Unicode as they are printed, so the
/// cell already holds the box-drawing character - see
/// <c>TerminalEmulatorBase.ApplyCharacterSetMapping</c>. A TDV deliberately does NOT:
/// <c>TDVEmulatorBase</c> overrides that method to pass the byte through unchanged, because TDV
/// glyphs come from a bitmap font selected by <c>FontNumber</c> rather than from a Unicode font.
/// That is right for DRAWING and wrong for everything else, because the cell then holds the letter
/// <c>g</c> where the screen shows a corner.
///
/// <para><b>What it cost</b></para>
/// Ronny, 2 September 2026: copying a box out of a TDV session pasted as
/// <c>g``````i</c> / <c>j ... j</c> / <c>a``````c</c> instead of the box he could see.
///
/// <para><b>Where the table comes from - it was derived, not guessed</b></para>
/// Every entry was read off the REAL bitmaps in <c>FontTDV2200</c> charset 2 by rendering each
/// glyph and classifying which arms it has: ink above the horizontal stroke means an arm going up,
/// ink to the left of the vertical stroke means an arm going left, and so on. All eleven land in a
/// contiguous run, <c>0x60</c> to <c>0x6A</c>, and every one of them carries ZERO ink away from
/// its two strokes - so they are pure box drawing and nothing is being forced into a shape it does
/// not have.
/// <para>
/// The reading was then checked a second way, against the box Ronny actually pasted: <c>g</c> in
/// the top-left corner, <c>i</c> in the top-right, <c>a</c> and <c>c</c> at the bottom, <c>j</c>
/// down the sides and backtick along the top and bottom edges. Both accounts agree.
/// </para>
///
/// <para><b>What is deliberately NOT here</b></para>
/// Charset 2 holds more than boxes - blocks, arrows and other shapes, which the same classifier
/// shows carrying ink away from the strokes. None of those is claimed, because there is no
/// obviously right Unicode character for them and a wrong one would be worse than the raw byte:
/// it would look correct and be undetectably wrong. They still copy as their own byte.
/// </remarks>
public static class TdvLineDrawingCharacters
{
    /// <summary>
    /// The <c>FontNumber</c> a cell carries when its glyph comes from the line-drawing set.
    /// </summary>
    /// <remarks>
    /// Set by <c>TDVCharacterSetManager</c>, which comments it as "fontNum 2 (graphics/line
    /// drawing, offset 128)".
    /// </remarks>
    public const byte LineDrawingFontNumber = 2;

    /// <summary>
    /// Turns a TDV line-drawing byte into the box-drawing character it draws.
    /// </summary>
    /// <param name="codepoint">
    /// The byte held in the cell.
    /// </param>
    /// <param name="fontNumber">
    /// The cell's font number. Anything but the line-drawing set is left alone.
    /// </param>
    /// <param name="drawn">
    /// The character the screen shows, when this returns true.
    /// </param>
    /// <returns>
    /// True when the byte is one of the eleven box-drawing glyphs.
    /// </returns>
    public static bool TryGetDrawnCharacter(uint codepoint, byte fontNumber, out uint drawn)
    {
        if (fontNumber != LineDrawingFontNumber)
        {
            drawn = codepoint;
            return false;
        }

        // Arms named as the classifier found them: U up, D down, L left, R right.
        switch (codepoint)
        {
            case 0x60: drawn = 0x2500; return true;   // ` LR   ─
            case 0x61: drawn = 0x2514; return true;   // a UR   └
            case 0x62: drawn = 0x2534; return true;   // b ULR  ┴
            case 0x63: drawn = 0x2518; return true;   // c UL   ┘
            case 0x64: drawn = 0x251C; return true;   // d UDR  ├
            case 0x65: drawn = 0x253C; return true;   // e UDLR ┼
            case 0x66: drawn = 0x2524; return true;   // f UDL  ┤
            case 0x67: drawn = 0x250C; return true;   // g DR   ┌
            case 0x68: drawn = 0x252C; return true;   // h DLR  ┬
            case 0x69: drawn = 0x2510; return true;   // i DL   ┐
            case 0x6A: drawn = 0x2502; return true;   // j UD   │
            default:
                drawn = codepoint;
                return false;
        }
    }
}
