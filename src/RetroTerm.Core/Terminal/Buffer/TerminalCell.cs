using System;
using System.Runtime.InteropServices;

namespace RetroTerm.Core.Terminal.Buffer;

/// <summary>
/// Represents a single character cell in the terminal buffer.
/// Optimized for memory efficiency and high-performance access.
/// </summary>
/// <remarks>
/// <para><b>Size</b></para>
/// 20 bytes. It was 16 and exactly full - 4 + 2 + 1 + 1 + 4 + 4 - until
/// <see cref="RegisColorIndex"/> was added, and one more byte rounds the struct up to the next
/// multiple of its 4-byte alignment. Bits were looked for first and there were not enough: the
/// packed flags are full, and <see cref="CharacterAttributes"/> has only three spare where five
/// are needed.
/// <see cref="TextIsNewerThanGraphics"/> then took a second byte and the struct did NOT grow: 18
/// rounds up to the same 20 that 17 did, so it rides along in padding that was already being
/// paid for. There is room for one more byte on the same terms, and no more after that.
/// The byte earns its place by riding along with the cell. Scrolling, insert line, delete line,
/// the alternate screen and scrollback all move cells with <c>Array.Copy</c>, so a colour stored
/// HERE survives all of them with no extra code - which is what the hardware does. Held in a
/// parallel plane it would be a second copy of the same truth to keep in step.
/// </remarks>
[StructLayout(LayoutKind.Sequential)]
public struct TerminalCell : IEquatable<TerminalCell>
{
    /// <summary>
    /// The Unicode codepoint of the character.
    /// For most characters, this is sufficient. For combining characters,
    /// additional logic may be needed.
    /// </summary>
    public uint Codepoint;

    /// <summary>
    /// Character attributes (bold, underline, etc.)
    /// </summary>
    public CharacterAttributes Attributes;

    /// <summary>
    /// Character set index (for terminals that support multiple character sets)
    /// 0 = ASCII/Default, 1-15 = alternate character sets (G0-G3, etc.)
    /// </summary>
    public byte CharacterSet;

    /// <summary>
    /// Packed flags byte:
    /// Bit 0: WideLead — this cell holds a character that occupies TWO columns
    /// Bit 1: WideTrail — this cell is the second half of the one to its left
    /// Bits 2-4: FontNumber (for TDV2200 bitmap fonts: 0=standard, 2=SS2/graphics, 3=SS3/subscript, 4=control codes)
    /// Bits 5-7: Field attributes (for IBM 3270 compatibility)
    /// </summary>
    private byte _flags;

    private const byte WideLeadBit = 0x01;
    private const byte WideTrailBit = 0x02;

    /// <summary>
    /// This cell holds a character that is drawn across TWO columns — a CJK ideograph, a
    /// fullwidth form, most emoji. The cell to its right is its <see cref="IsWideTrail"/> and
    /// holds nothing of its own.
    ///
    /// Stored in the packed flags rather than derived, because unlike double-width LINES this is
    /// a property of the character itself and cannot be recovered from the attributes. The two
    /// bits used here are the ones freed when double size stopped being recorded twice per cell.
    /// </summary>
    public bool IsWideLead
    {
        get => (_flags & WideLeadBit) != 0;
        set => _flags = (byte)(value ? (_flags | WideLeadBit) : (_flags & ~WideLeadBit));
    }

    /// <summary>
    /// This cell is the right-hand half of the wide character in the cell to its left. It draws
    /// nothing itself, and the cursor steps over it rather than landing on it.
    /// </summary>
    public bool IsWideTrail
    {
        get => (_flags & WideTrailBit) != 0;
        set => _flags = (byte)(value ? (_flags | WideTrailBit) : (_flags & ~WideTrailBit));
    }

    /// <summary>
    /// The four-plane pixel value this cell's text carries, plus one, or 0 when it carries none.
    ///
    /// Never read directly - go through <see cref="RegisColorIndex"/>, which does the plus-one.
    /// The offset is what lets 0 mean "untouched" while still allowing a real code of 0, which is
    /// a genuine outcome: clearing every plane under a character leaves it the background colour,
    /// and on the hardware that character goes invisible.
    /// </summary>
    private byte _regisCodePlusOne;

    /// <summary>
    /// The four-plane pixel value this cell's text carries, 0 to 15, or -1 when ReGIS has not
    /// touched it and the ordinary foreground colour applies.
    /// </summary>
    /// <remarks>
    /// On a VT340 there is one picture memory four bits deep, and every pixel of text holds the
    /// same value - <c>0111</c> normally, <c>1111</c> for bold. That is why the terminal cannot
    /// show multicoloured text: recolouring one text pixel recolours all of them.
    /// ReGIS gets round it by writing through a PLANE MASK, which changes only some of the four
    /// bits. hackerb9's <c>faketextcolor.sh</c> writes index 0 through mask <c>i</c> for i in 0..15,
    /// so a text pixel becomes <c>0111 AND NOT i</c> while the background, already <c>0000</c>,
    /// does not move at all. Sixteen different values, sixteen different colours, one line of text.
    /// Here text is a cell buffer rather than a bitmap, so the same idea is kept per CELL: this
    /// holds the value the character's pixels would have, and the renderer resolves it through the
    /// ReGIS colour map instead of using the ordinary foreground.
    /// The limit that follows from that, stated plainly: a plane write covering only PART of a
    /// character recolours the whole character. On the 80 by 24 the boxes are cell-aligned - the
    /// plane is 800 by 480, a cell is exactly 10 by 20, and hackerb9's boxes are 20 wide - so the
    /// fixture matches the photograph. On other geometries it is an approximation.
    /// </remarks>
    public int RegisColorIndex
    {
        readonly get => _regisCodePlusOne == 0 ? -1 : _regisCodePlusOne - 1;
        set => _regisCodePlusOne = value < 0 ? (byte)0 : (byte)((value & 15) + 1);
    }

    /// <summary>
    /// Whether ReGIS has given this cell a pixel value of its own.
    /// </summary>
    public readonly bool HasRegisColor => _regisCodePlusOne != 0;

    /// <summary>
    /// Packed graphics-ordering flags. Its own byte because all eight bits of
    /// <see cref="_flags"/> are already spoken for.
    /// </summary>
    private byte _graphicsFlags;

    private const byte TextOverGraphicsBit = 0x01;

    /// <summary>
    /// This cell's character was written AFTER the last graphics write that covered it, so it
    /// belongs on top of the picture rather than underneath it.
    /// </summary>
    /// <remarks>
    /// <para><b>Why the cell has to remember this at all</b></para>
    /// A real VT340 has ONE picture memory holding both text and graphics, so the question never
    /// arises there: whichever wrote a pixel last owns it. Here text is a cell buffer and graphics
    /// is a separate plane composited over it, which loses the ordering completely - the plane
    /// always won, whatever arrived first.
    /// <para>
    /// Reported by Ronny on 1 September 2026 against the M6.1a <c>extremeratio</c> sheet, whose
    /// trailing line of text lands on row 0 under a full-screen image and vanished. The emulator
    /// was already right - <c>DrawSixelImage</c> deliberately leaves the cursor on the image's
    /// last band, and its own comment says the next text printed overlaps the picture, which is
    /// what the hardware does - so the ordering only ever went missing at the renderer.
    /// </para>
    /// <para>
    /// Set when a character is printed into the cell, cleared when graphics paints over it. The
    /// renderer punches the plane transparent over cells that carry it, which is as close to one
    /// shared bitmap as a cell buffer gets.
    /// </para>
    /// </remarks>
    public bool TextIsNewerThanGraphics
    {
        readonly get => (_graphicsFlags & TextOverGraphicsBit) != 0;
        set => _graphicsFlags = (byte)(value
            ? (_graphicsFlags | TextOverGraphicsBit)
            : (_graphicsFlags & ~TextOverGraphicsBit));
    }

    /// <summary>
    /// Foreground color
    /// </summary>
    public TerminalColor Foreground;

    /// <summary>
    /// Background color
    /// </summary>
    public TerminalColor Background;

    /// <summary>
    /// Whether this cell is part of a double-WIDTH line (DECDWL, or DECDHL which implies it).
    ///
    /// DERIVED from <see cref="Attributes"/>, not stored. Double size used to live in TWO
    /// places on every cell: bits 0-1 of <see cref="_flags"/>, and the
    /// <see cref="CharacterAttributes"/> DoubleWidth / DoubleHeightTop / DoubleHeightBottom
    /// flags. One writer set both and nothing in production read the bits — only tests did —
    /// so the pair could drift apart without a single test noticing.
    ///
    /// The attributes win because they carry strictly more: a bit cannot say whether a
    /// double-height line is showing its TOP or BOTTOM half, and the renderer has to know.
    /// </summary>
    public bool DoubleWidth => (Attributes & CharacterAttributes.DoubleWidth) != 0;

    /// <summary>
    /// Whether this cell is part of a double-HEIGHT line — either half. Derived from
    /// <see cref="Attributes"/>; see <see cref="DoubleWidth"/> for why.
    /// Use <see cref="DoubleHeightTop"/> / <see cref="DoubleHeightBottom"/> when the half matters.
    /// </summary>
    public bool DoubleHeight =>
        (Attributes & (CharacterAttributes.DoubleHeightTop | CharacterAttributes.DoubleHeightBottom)) != 0;

    /// <summary>
    /// Whether this cell shows the TOP half of a double-height line (DECDHL, ESC # 3).
    /// </summary>
    public bool DoubleHeightTop => (Attributes & CharacterAttributes.DoubleHeightTop) != 0;

    /// <summary>
    /// Whether this cell shows the BOTTOM half of a double-height line (DECDHL, ESC # 4).
    /// </summary>
    public bool DoubleHeightBottom => (Attributes & CharacterAttributes.DoubleHeightBottom) != 0;

    /// <summary>
    /// Gets or sets the font number for TDV2200 bitmap fonts (bits 2-4 of _flags)
    /// 0 = Standard characters, 2 = SS2/Graphics, 3 = SS3/Subscript, 4 = Control codes
    /// </summary>
    public byte FontNumber
    {
        // readonly on the getter so reading it from a readonly member does not silently copy the
        // whole struct - the compiler flags that as CS8656, and this cell is read on hot paths.
        readonly get => (byte)((_flags >> 2) & 0x07);
        set => _flags = (byte)((_flags & 0xE3) | ((value & 0x07) << 2));
    }

    /// <summary>
    /// Gets or sets the IBM 3270 field attribute (bits 5-7 of _flags)
    /// </summary>
    public byte FieldAttribute
    {
        get => (byte)((_flags >> 5) & 0x07);
        set => _flags = (byte)((_flags & 0x1F) | ((value & 0x07) << 5));
    }

    /// <summary>
    /// Creates a new terminal cell with the specified character
    /// </summary>
    public TerminalCell(char ch)
    {
        Codepoint = ch;
        Attributes = CharacterAttributes.None;
        CharacterSet = 0;
        _flags = 0;
        _regisCodePlusOne = 0;
        _graphicsFlags = 0;
        Foreground = TerminalColor.Default;
        Background = TerminalColor.Default;
    }

    /// <summary>
    /// Creates a new terminal cell with the specified codepoint
    /// </summary>
    public TerminalCell(uint codepoint)
    {
        Codepoint = codepoint;
        Attributes = CharacterAttributes.None;
        CharacterSet = 0;
        _flags = 0;
        _regisCodePlusOne = 0;
        _graphicsFlags = 0;
        Foreground = TerminalColor.Default;
        Background = TerminalColor.Default;
    }

    /// <summary>
    /// Creates a new terminal cell with full specification
    /// </summary>
    public TerminalCell(uint codepoint, CharacterAttributes attributes,
                       TerminalColor foreground, TerminalColor background,
                       byte characterSet = 0)
    {
        Codepoint = codepoint;
        Attributes = attributes;
        CharacterSet = characterSet;
        _flags = 0;
        _regisCodePlusOne = 0;
        _graphicsFlags = 0;
        Foreground = foreground;
        Background = background;
    }

    /// <summary>
    /// Gets the character as a string (handles Unicode properly)
    /// </summary>
    public readonly string GetString()
    {
        if (Codepoint == 0)
            return " ";

        // What the SCREEN shows, not the byte behind it. A TDV keeps its line-drawing bytes
        // unmapped because its glyphs come from a bitmap font, so without this a copied box
        // pastes as "g``````i" instead of the box. See TdvLineDrawingCharacters.
        if (TdvLineDrawingCharacters.TryGetDrawnCharacter(Codepoint, FontNumber, out uint drawn))
            return ((char)drawn).ToString();

        if (Codepoint <= 0xFFFF)
            return ((char)Codepoint).ToString();

        // Handle Unicode supplementary planes (codepoints > U+FFFF)
        return char.ConvertFromUtf32((int)Codepoint);
    }

    /// <summary>
    /// Checks if this cell is empty (space with no attributes)
    /// </summary>
    public readonly bool IsEmpty =>
        (Codepoint == 0 || Codepoint == ' ') &&
        Attributes == CharacterAttributes.None &&
        Foreground.IsDefault &&
        Background.IsDefault;

    /// <summary>
    /// Checks if this cell has any visual attributes
    /// </summary>
    public readonly bool HasAttributes => Attributes != CharacterAttributes.None;

    /// <summary>
    /// Creates an empty cell (space with default attributes)
    /// </summary>
    public static TerminalCell Empty => new(0);

    /// <summary>
    /// Clears this cell to empty state
    /// </summary>
    public void Clear()
    {
        Codepoint = 0;
        Attributes = CharacterAttributes.None;
        CharacterSet = 0;
        FontNumber = 0;
        _regisCodePlusOne = 0;
        _graphicsFlags = 0;
        Foreground = TerminalColor.Default;
        Background = TerminalColor.Default;
    }

    /// <summary>
    /// Copies attributes and colors from another cell
    /// </summary>
    public void CopyAttributesFrom(in TerminalCell other)
    {
        Attributes = other.Attributes;
        CharacterSet = other.CharacterSet;
        FontNumber = other.FontNumber;
        Foreground = other.Foreground;
        Background = other.Background;
    }

    public readonly bool Equals(TerminalCell other)
    {
        return Codepoint == other.Codepoint &&
               Attributes == other.Attributes &&
               CharacterSet == other.CharacterSet &&
               _flags == other._flags &&
               _regisCodePlusOne == other._regisCodePlusOne &&
               _graphicsFlags == other._graphicsFlags &&
               Foreground == other.Foreground &&
               Background == other.Background;
    }

    public override readonly bool Equals(object? obj) => obj is TerminalCell cell && Equals(cell);

    public override readonly int GetHashCode()
    {
        return HashCode.Combine(Codepoint, Attributes, CharacterSet, _flags, _regisCodePlusOne,
            _graphicsFlags, Foreground, Background);
    }

    public static bool operator ==(TerminalCell left, TerminalCell right) => left.Equals(right);
    public static bool operator !=(TerminalCell left, TerminalCell right) => !left.Equals(right);

    public override readonly string ToString()
    {
        var ch = GetString();
        if (Attributes == CharacterAttributes.None && Foreground.IsDefault && Background.IsDefault)
            return $"'{ch}'";
        return $"'{ch}' [{Attributes}] FG:{Foreground} BG:{Background}";
    }
}

