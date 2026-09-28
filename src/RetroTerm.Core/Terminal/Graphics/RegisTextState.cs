using System;

namespace RetroTerm.Core.Terminal.Graphics;

/// <summary>
/// Everything the ReGIS text command remembers between one string and the next, and the glyphs it
/// draws from.
/// </summary>
/// <remarks>
/// <para><b>Why this is its own type</b></para>
/// Chapter 7 of the VT330/VT340 Graphics Programming manual gives the text command ten options and
/// a scaling model that has nothing to do with the rest of ReGIS. Keeping it here leaves
/// <c>RegisDecoder</c> reading as the command dispatcher it is, and lets the sizes be tested
/// without drawing anything.
///
/// <para><b>Where the numbers come from, and where the shapes come from</b></para>
/// The SIZES are the manual's, table 7-3, transcribed below. The stored cell is "80 pixels
/// (8 pixels wide x 10 pixels high)" and a character is drawn with "the upper-left pixel of the
/// 8 x 10 cell at the cursor position".
///
/// The SHAPES are not the manual's, because it prints six example glyphs and no font. They come
/// from the TDV2200 character ROM, which is the only bitmap font in Core and was traced from real
/// hardware - a stand-in, chosen because the alternative was ninety-six glyphs invented here. It is
/// 8 pixels wide, which is the width the manual asks for, and its rows are scaled into whatever
/// unit cell is in force. A reader who wants DEC's own shapes should replace
/// <see cref="BuiltInGlyph"/> and nothing else.
/// </remarks>
public sealed class RegisTextState
{
    /// <summary>
    /// Width of a stored character cell, in pixels.
    /// </summary>
    public const int StoredCellWidth = 8;

    /// <summary>
    /// Height of a stored character cell, in pixels.
    /// </summary>
    public const int StoredCellHeight = 10;

    /// <summary>
    /// The standard cell size a terminal entering ReGIS starts with.
    /// </summary>
    /// <remarks>
    /// <para><b>One, not zero, and it was zero here until the corpus said otherwise</b></para>
    /// Table 7-4 gives the S option a default of 1, and its footnote says so twice over: "Default
    /// value is based on standard S1 character cell", beside a display cell of [9,20].
    ///
    /// Starting at S0 made every label half height, which is exactly how it showed up - the grid
    /// drawn by hackerb9's registest.sh came out with visibly smaller text than the capture taken
    /// from the real VT340 beside it.
    /// </remarks>
    public const int DefaultStandardSize = 1;

    /// <summary>
    /// How many character sets a host may load: sets 1, 2 and 3. Set 0 is built in and cannot be
    /// loaded - "Set 0 is reserved for one of the terminal's built-in sets".
    /// </summary>
    public const int LoadableSetCount = 3;

    /// <summary>
    /// How many cells one loadable set holds. The manual: "This set can have up to 96 characters,
    /// but can only include 7-bit characters."
    /// </summary>
    public const int CellsPerLoadableSet = 96;

    /// <summary>
    /// The lowest call letter a loadable cell can have, so the store is indexed from it.
    /// </summary>
    private const int FirstCallLetter = 0x20;

    /// <summary>
    /// Table 7-3, the seventeen standard character cell sizes, as display width, display height,
    /// unit width, unit height, character positioning.
    /// </summary>
    /// <remarks>
    /// Transcribed from the printed page rather than from the OCR, which renders every table in
    /// this manual as noise. From S2 upwards the pattern is display <c>[9n, 15n]</c>, unit
    /// <c>[8n, 15n]</c> and positioning <c>9n</c>; S0 and S1 do not follow it, which is exactly why
    /// the table is written out instead of computed.
    /// </remarks>
    private static readonly int[,] StandardSizes =
    {
        //  display w, display h, unit w, unit h, positioning
        {   9,  10,   8,  10,   9 },   // S0
        {   9,  20,   8,  20,   9 },   // S1
        {  18,  30,  16,  30,  18 },   // S2
        {  27,  45,  24,  45,  27 },   // S3
        {  36,  60,  32,  60,  36 },   // S4
        {  45,  75,  40,  75,  45 },   // S5
        {  54,  90,  48,  90,  54 },   // S6
        {  63, 105,  56, 105,  63 },   // S7
        {  72, 120,  64, 120,  72 },   // S8
        {  81, 135,  72, 135,  81 },   // S9
        {  90, 150,  80, 150,  90 },   // S10
        {  99, 165,  88, 165,  99 },   // S11
        { 108, 180,  96, 180, 108 },   // S12
        { 117, 195, 104, 195, 117 },   // S13
        { 126, 210, 112, 210, 126 },   // S14
        { 135, 225, 120, 225, 135 },   // S15
        { 144, 240, 128, 240, 144 },   // S16
    };

    /// <summary>
    /// The built-in glyphs, built on first use.
    /// </summary>
    private static Fonts.FontTDV2200? _builtIn;

    /// <summary>
    /// Loadable sets 1 to 3. Null until a host loads into one.
    /// </summary>
    /// <remarks>
    /// Indexed set, then call letter minus 0x20, then row: ten rows of eight bits, one byte each,
    /// most significant bit on the left, which is the order the manual's hex pairs are written in.
    /// </remarks>
    private byte[][]? _loadable;

    /// <summary>
    /// Names given to the loadable sets, for the report command to answer with.
    /// </summary>
    private string[]? _loadableNames;

    /// <summary>
    /// Which set text is drawn from: 0 for the built-in set, 1 to 3 for a loaded one.
    /// </summary>
    public int ActiveSet { get; set; }

    /// <summary>
    /// Which set the load command is filling.
    /// </summary>
    public int LoadingSet { get; set; } = 1;

    /// <summary>
    /// Width of the area one character occupies, in screen coordinates.
    /// </summary>
    public int DisplayCellWidth { get; private set; } = 9;

    /// <summary>
    /// Height of the area one character occupies.
    /// </summary>
    public int DisplayCellHeight { get; private set; } = 20;

    /// <summary>
    /// Width the character itself is drawn at, inside the display cell.
    /// </summary>
    public int UnitCellWidth { get; private set; } = StoredCellWidth;

    /// <summary>
    /// Height the character itself is drawn at.
    /// </summary>
    public int UnitCellHeight { get; private set; } = 20;

    /// <summary>
    /// How far the cursor moves after each character.
    /// </summary>
    public int CharacterPositioning { get; private set; } = 9;

    /// <summary>
    /// Height multiplier, the H option. Multiplies the unit cell's height only.
    /// </summary>
    public int HeightMultiplier { get; set; } = 1;

    /// <summary>
    /// Size multiplier, the M option. Multiplies both directions.
    /// </summary>
    public int SizeMultiplier { get; set; } = 1;

    /// <summary>
    /// String tilt in degrees, the D option: the direction the string runs in.
    /// </summary>
    /// <remarks>
    /// "The following options let you tilt individual characters or text strings at any 45 degree
    /// increment, for a full 360 degrees." Values that are not multiples of 45 are rounded to one,
    /// because the compass in figure 7-5 has eight points and nothing in the chapter offers a
    /// meaning for the angles between them.
    /// </remarks>
    public int StringTilt { get; set; }

    /// <summary>
    /// Italic slant in degrees, the I option.
    /// </summary>
    public int Italics { get; set; }

    /// <summary>
    /// Options saved by a temporary text control, to be put back at its end option.
    /// </summary>
    /// <remarks>
    /// A single slot rather than a stack. The manual gives the temporary control a start and an end
    /// and never speaks of nesting one inside another, and a second start before an end is a stream
    /// that has lost track of itself - taking the newer values and forgetting the older ones is the
    /// behaviour that keeps drawing.
    /// </remarks>
    private int[]? _saved;

    /// <summary>
    /// Starts a temporary text control: <c>T(B ...)</c>.
    /// </summary>
    /// <remarks>
    /// "Temporary text controls have specific start and end options. ReGIS processes all values
    /// between the start and end options as part of the temporary text control", and the chapter's
    /// opening list says their values do NOT remain in effect afterwards - which is what separates
    /// them from every other option here.
    ///
    /// hackerb9's <c>registest.sh</c> uses them on every line it labels: <c>T(B D180 S1 W(I1V))</c>
    /// then <c>T(E)</c>, so the tilt and size it needs for one label do not leak into the next.
    /// </remarks>
    public void BeginTemporary()
    {
        _saved ??= new int[9];

        _saved[0] = ActiveSet;
        _saved[1] = DisplayCellWidth;
        _saved[2] = DisplayCellHeight;
        _saved[3] = UnitCellWidth;
        _saved[4] = UnitCellHeight;
        _saved[5] = CharacterPositioning;
        _saved[6] = HeightMultiplier;
        _saved[7] = SizeMultiplier;
        _saved[8] = StringTilt;
    }

    /// <summary>
    /// Ends a temporary text control: <c>T(E)</c>, putting back what was in force before it.
    /// </summary>
    /// <remarks>
    /// An end with no start does nothing rather than resetting to the power-on values. A stream
    /// that sends one is confused, and wiping the host's carefully set size because of it would
    /// turn a small confusion into a blank screen.
    /// </remarks>
    public void EndTemporary()
    {
        if (_saved == null) return;

        ActiveSet = _saved[0];
        DisplayCellWidth = _saved[1];
        DisplayCellHeight = _saved[2];
        UnitCellWidth = _saved[3];
        UnitCellHeight = _saved[4];
        CharacterPositioning = _saved[5];
        HeightMultiplier = _saved[6];
        SizeMultiplier = _saved[7];
        StringTilt = _saved[8];

        _saved = null;
    }

    /// <summary>
    /// Puts every option back to what a terminal entering ReGIS starts with.
    /// </summary>
    public void Reset()
    {
        ActiveSet = 0;
        LoadingSet = 1;
        SelectStandardSize(DefaultStandardSize);
        HeightMultiplier = 1;
        SizeMultiplier = 1;
        StringTilt = 0;
        Italics = 0;
    }

    /// <summary>
    /// Applies one of the seventeen standard character cell sizes.
    /// </summary>
    /// <param name="size">
    /// 0 to 16. Anything outside is clamped, which is what a terminal with a fixed table does.
    /// </param>
    public void SelectStandardSize(int size)
    {
        if (size < 0) size = 0;
        if (size > 16) size = 16;

        DisplayCellWidth = StandardSizes[size, 0];
        DisplayCellHeight = StandardSizes[size, 1];
        UnitCellWidth = StandardSizes[size, 2];
        UnitCellHeight = StandardSizes[size, 3];
        CharacterPositioning = StandardSizes[size, 4];
    }

    /// <summary>
    /// Sets the display cell - the screen area one character occupies - from <c>T(S[w,h])</c>.
    /// </summary>
    /// <param name="width">
    /// Width in screen coordinates.
    /// </param>
    /// <param name="height">
    /// Height in screen coordinates.
    /// </param>
    /// <remarks>
    /// "This option does not change the size of characters", so the unit cell is left alone. The
    /// spacing follows the display cell, since the manual defines character positioning as the
    /// spacing between characters and a wider cell that kept its old spacing would overlap.
    /// </remarks>
    public void SetDisplayCell(int width, int height)
    {
        if (width > 0)
        {
            DisplayCellWidth = width;
            CharacterPositioning = width;
        }

        if (height > 0)
        {
            DisplayCellHeight = height;
        }
    }

    /// <summary>
    /// Sets the unit cell - the size of the character itself - from <c>T(U[w,h])</c>.
    /// </summary>
    /// <param name="width">
    /// Width in screen coordinates.
    /// </param>
    /// <param name="height">
    /// Height in screen coordinates.
    /// </param>
    /// <remarks>
    /// "The width value must be a positive multiple of 8, the height value must be a positive
    /// multiple of 5. ... If you do not use a multiple, ReGIS uses the next smaller size." So both
    /// are rounded DOWN to the multiple, and a value below one multiple lands on it rather than on
    /// zero - a character of no height is not a size a terminal can draw.
    /// </remarks>
    public void SetUnitCell(int width, int height)
    {
        if (width > 0)
        {
            UnitCellWidth = Math.Max(8, width - (width % 8));
        }

        if (height > 0)
        {
            UnitCellHeight = Math.Max(5, height - (height % 5));
        }
    }

    /// <summary>
    /// Names the set currently being loaded.
    /// </summary>
    /// <param name="name">
    /// Up to ten characters; longer names are cut, which is what a fixed field does.
    /// </param>
    public void NameLoadingSet(string name)
    {
        if (name == null) return;

        _loadableNames ??= new string[LoadableSetCount + 1];

        int set = LoadingSet;
        if (set < 1 || set > LoadableSetCount) return;

        _loadableNames[set] = name.Length > 10 ? name.Substring(0, 10) : name;
    }

    /// <summary>
    /// The name a loadable set was given, or an empty string.
    /// </summary>
    /// <param name="set">
    /// 1 to 3.
    /// </param>
    /// <returns>
    /// The name, or an empty string when the set has none.
    /// </returns>
    public string NameOf(int set)
    {
        if (_loadableNames == null || set < 1 || set > LoadableSetCount) return "";
        return _loadableNames[set] ?? "";
    }

    /// <summary>
    /// Stores one loaded character cell.
    /// </summary>
    /// <param name="callLetter">
    /// The character that will select this cell in a text string. "You can use any single ASCII
    /// character for the call letter, including a number or a space."
    /// </param>
    /// <param name="rows">
    /// Up to ten rows, most significant bit on the left.
    /// </param>
    /// <param name="rowCount">
    /// How many of <paramref name="rows"/> were given. Rows not given stay blank.
    /// </param>
    public void LoadCell(char callLetter, ReadOnlySpan<byte> rows, int rowCount)
    {
        int set = LoadingSet;
        if (set < 1 || set > LoadableSetCount) return;

        int index = callLetter - FirstCallLetter;
        if (index < 0 || index >= CellsPerLoadableSet) return;

        _loadable ??= new byte[LoadableSetCount + 1][];
        _loadable[set] ??= new byte[CellsPerLoadableSet * StoredCellHeight];

        var store = _loadable[set];
        int at = index * StoredCellHeight;

        for (int row = 0; row < StoredCellHeight; row++)
        {
            store[at + row] = row < rowCount && row < rows.Length ? rows[row] : (byte)0;
        }
    }

    /// <summary>
    /// The eight-bit row pattern for one row of one character, in the active set.
    /// </summary>
    /// <param name="character">
    /// The character to draw.
    /// </param>
    /// <param name="row">
    /// Which row of the stored cell, from the top.
    /// </param>
    /// <returns>
    /// The bits, most significant on the left; zero when there is nothing to draw.
    /// </returns>
    /// <remarks>
    /// A loadable set that has not been loaded draws nothing rather than falling back to the
    /// built-in glyphs. A host that selected set 2 and drew blanks has a bug worth seeing; one that
    /// silently got ASCII would never find it.
    /// </remarks>
    public byte RowOf(char character, int row)
        => RowFor(character, row, StoredCellHeight);

    /// <summary>
    /// The eight-bit row pattern for one row of a character being drawn at a given height.
    /// </summary>
    /// <param name="character">
    /// The character to draw.
    /// </param>
    /// <param name="row">
    /// Which row of the DRAWN character, from the top.
    /// </param>
    /// <param name="rowsBeingDrawn">
    /// How many rows the character is being drawn in.
    /// </param>
    /// <returns>
    /// The bits, most significant on the left; zero when there is nothing to draw.
    /// </returns>
    /// <remarks>
    /// <para><b>Why the caller's height reaches this far in</b></para>
    /// It looked unnecessary and it was not. Quantising the built-in glyphs to ten rows first and
    /// scaling that to the target loses whichever rows the quantisation drops - and for this ROM
    /// those rows are the ones that tell letters apart. The first sample sheet drawn this way read
    /// "Thc quick brown fox" and "S1zcs from tablc 7-3": every <c>e</c> had become a <c>c</c> and
    /// every <c>i</c> a <c>1</c>, because the middle bar of the e and the dot of the i sat in the
    /// dropped rows. Every assertion still passed - only looking at the picture found it.
    ///
    /// So a built-in glyph is sampled straight from the ROM at whatever height it is being drawn
    /// at, and only a LOADED cell is sampled through ten rows, because a loaded cell really is
    /// eight by ten - the host said so, one hex pair per row.
    /// </remarks>
    public byte RowFor(char character, int row, int rowsBeingDrawn)
    {
        if (row < 0 || rowsBeingDrawn <= 0) return 0;

        int set = ActiveSet;

        if (set >= 1 && set <= LoadableSetCount)
        {
            if (_loadable == null || _loadable[set] == null) return 0;

            int index = character - FirstCallLetter;
            if (index < 0 || index >= CellsPerLoadableSet) return 0;

            int storedRow = row * StoredCellHeight / rowsBeingDrawn;
            if (storedRow >= StoredCellHeight) return 0;

            return _loadable[set][(index * StoredCellHeight) + storedRow];
        }

        return BuiltInGlyph(character, row, rowsBeingDrawn);
    }

    /// <summary>
    /// One row of a built-in glyph, scaled from the character ROM into the ten-row stored cell.
    /// </summary>
    /// <param name="character">
    /// The character to draw.
    /// </param>
    /// <param name="row">
    /// Which row of the drawn character.
    /// </param>
    /// <param name="rowsBeingDrawn">
    /// How many rows the character is being drawn in.
    /// </param>
    /// <returns>
    /// Eight bits, most significant on the left.
    /// </returns>
    /// <remarks>
    /// Sampled straight from the ROM's own fourteen rows at the height being drawn, never through
    /// ten - see <see cref="RowFor"/> for the letters that cost.
    /// </remarks>
    private static byte BuiltInGlyph(char character, int row, int rowsBeingDrawn)
    {
        _builtIn ??= new Fonts.FontTDV2200();

        if (character > 0xFF) return 0;

        var bits = _builtIn.GetFontBits(character, 0);
        if (bits == null) return 0;

        int used = _builtIn.HeightToUse > 0 ? _builtIn.HeightToUse : _builtIn.Height;
        if (used <= 0) return 0;

        int source = row * used / rowsBeingDrawn;
        if (source >= bits.Length || source >= used) return 0;

        // The ROM is eight pixels wide with the leftmost pixel in the highest bit, which is the
        // same order the stored cell uses, so the low byte is the pattern.
        return (byte)(bits[source] & 0xFF);
    }
}
