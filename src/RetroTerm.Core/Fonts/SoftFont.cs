using System;
using System.Collections.Generic;

namespace RetroTerm.Core.Fonts;

/// <summary>
/// A character set a host has drawn and downloaded, with DECDLD.
/// </summary>
/// <remarks>
/// <para><b>What a soft font is</b></para>
/// The host sends the SHAPES: for each character, a grid of lit and unlit pixels. The terminal
/// keeps them and draws them wherever that character code appears in the set. It is how a VT220
/// showed a currency symbol, a line-drawing corner or a whole alphabet its ROM had never heard of.
///
/// <para><b>Stored as bit rows, which is what the renderer already speaks</b></para>
/// One <c>ushort</c> per pixel row, bit 15 leftmost - the same layout <see cref="FontBase"/> keeps
/// its ROM glyphs in. That is deliberate: a downloaded glyph should reach the screen through the
/// same path a built-in one does, rather than growing a second way to draw a character.
///
/// <para><b>Bounded, because a host fills it</b></para>
/// The matrix size is clamped to something a terminal could really have had, and the set holds at
/// most the 96 characters DECDLD can address. Nothing a host sends may make this grow without end.
/// </remarks>
public sealed class SoftFont
{
    /// <summary>
    /// Widest character matrix accepted, in pixels.
    /// </summary>
    /// <remarks>
    /// A VT220 cell is 10 wide at most. 16 is the ceiling here because a row is one ushort, and
    /// anything wider would need a different store rather than a bigger number.
    /// </remarks>
    public const int MaximumWidth = 16;

    /// <summary>
    /// Tallest character matrix accepted, in pixels.
    /// </summary>
    public const int MaximumHeight = 24;

    /// <summary>
    /// Most characters one downloaded set can hold.
    /// </summary>
    /// <remarks>
    /// DECDLD addresses either 94 or 96 characters depending on its size parameter, so 96 covers
    /// both.
    /// </remarks>
    public const int MaximumCharacters = 96;

    private readonly Dictionary<int, ushort[]> _glyphs = new Dictionary<int, ushort[]>();

    /// <summary>
    /// The designation string a host uses to select this set later, e.g. "@" or "%5".
    /// </summary>
    public string Designation { get; private set; } = "";

    /// <summary>
    /// Width of the character matrix in pixels.
    /// </summary>
    public int MatrixWidth { get; private set; }

    /// <summary>
    /// Height of the character matrix in pixels.
    /// </summary>
    public int MatrixHeight { get; private set; }

    /// <summary>
    /// How many characters this set currently defines.
    /// </summary>
    public int Count => _glyphs.Count;

    /// <summary>
    /// Describes the set a download is about to fill.
    /// </summary>
    /// <param name="designation">
    /// The designation string that selects this set.
    /// </param>
    /// <param name="matrixWidth">
    /// Character matrix width in pixels. Clamped to <see cref="MaximumWidth"/>.
    /// </param>
    /// <param name="matrixHeight">
    /// Character matrix height in pixels. Clamped to <see cref="MaximumHeight"/>.
    /// </param>
    public void Describe(string designation, int matrixWidth, int matrixHeight)
    {
        Designation = designation ?? "";
        MatrixWidth = Clamp(matrixWidth, 1, MaximumWidth);
        MatrixHeight = Clamp(matrixHeight, 1, MaximumHeight);
    }

    /// <summary>
    /// Stores one character's shape.
    /// </summary>
    /// <param name="characterCode">
    /// Position in the set, counting from 0 at the first character.
    /// </param>
    /// <param name="rows">
    /// One value per pixel row, bit 15 leftmost.
    /// </param>
    /// <returns>
    /// False when the position is outside the set, or the set is full.
    /// </returns>
    public bool Define(int characterCode, ushort[] rows)
    {
        if (rows == null) throw new ArgumentNullException(nameof(rows));
        if (characterCode < 0 || characterCode >= MaximumCharacters) return false;

        // Replacing an existing character is always allowed; only NEW ones can hit the ceiling.
        if (!_glyphs.ContainsKey(characterCode) && _glyphs.Count >= MaximumCharacters) return false;

        _glyphs[characterCode] = rows;
        return true;
    }

    /// <summary>
    /// Reads one character's shape.
    /// </summary>
    /// <param name="characterCode">
    /// Position in the set.
    /// </param>
    /// <param name="rows">
    /// The pixel rows, when that character is defined.
    /// </param>
    /// <returns>
    /// True when the character has a shape.
    /// </returns>
    public bool TryGetGlyph(int characterCode, out ushort[] rows)
        => _glyphs.TryGetValue(characterCode, out rows!);

    /// <summary>
    /// Forgets every character. The matrix size and designation stay.
    /// </summary>
    public void Clear() => _glyphs.Clear();

    /// <summary>
    /// Forgets everything, including what the set was called.
    /// </summary>
    public void Reset()
    {
        _glyphs.Clear();
        Designation = "";
        MatrixWidth = 0;
        MatrixHeight = 0;
    }

    private static int Clamp(int value, int low, int high)
    {
        if (value < low) return low;
        if (value > high) return high;
        return value;
    }
}
