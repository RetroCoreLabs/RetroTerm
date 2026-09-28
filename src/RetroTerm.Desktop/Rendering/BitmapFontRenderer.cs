using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Media;
using RetroTerm.Core.Fonts;
using RetroTerm.Core.Terminal.Buffer;
using RetroTerm.Core.Terminal.Emulators.TDV;
using RetroTerm.Core.Terminal.Rendering;

namespace RetroTerm.Desktop.Rendering;

/// <summary>
/// Bitmap font renderer for TDV terminals (TDV2200, TDV2215).
/// Uses ROM bitmap fonts with fontNum-based glyph selection.
///
/// Each glyph's shape is built ONCE and cached as a geometry. What it replaced ran, per cell and
/// per frame:
///
///  * <c>FontBase.GetFontBits</c>, which allocates a fresh <c>ushort[]</c> every call — 1,920 of
///    them for one 80x24 frame, on a hot path this project's rules say must not allocate;
///  * one <c>FillRectangle</c> per LIT PIXEL — up to 126 for a 9x14 cell, so up to a quarter of a
///    million draw calls for a full frame of dense text.
///
/// The cache also merges each row's consecutive lit pixels into ONE rectangle, so even the first
/// build of a glyph issues far fewer figures than the old per-pixel loop did every frame.
///
/// Not thread-safe, and does not need to be: a renderer belongs to one canvas and only ever draws
/// on the UI thread.
/// </summary>
public class BitmapFontRenderer : IFontRenderer
{
    private readonly FontBase _font;
    private readonly double _charWidth;
    private readonly double _charHeight;

    /// <summary>
    /// Cached glyph shapes in CELL-LOCAL coordinates, so one geometry serves every position the
    /// character appears at. A null value is a real answer — "this font has no glyph here" — and
    /// is cached too, or a missing character would redo the whole three-step lookup below on
    /// every frame.
    ///
    /// The ISO 646 variant is part of the key rather than a reason to clear the cache: the TDV
    /// fonts change what a byte draws when the variant changes, and keying on it means switching
    /// variant back and forth costs nothing after the first pass.
    /// </summary>
    private readonly Dictionary<int, Geometry?> _glyphCache = new Dictionary<int, Geometry?>();

    /// <summary>
    /// Gets the underlying font for configuration (e.g., setting CharacterSetVariant on FontTDV2215)
    /// </summary>
    public FontBase Font => _font;

    /// <summary>
    /// How many glyphs have been built. Read by tests to prove the cache is used.
    /// </summary>
    internal int GlyphsBuilt { get; private set; }

    public BitmapFontRenderer() : this(new FontTDV2200())
    {
    }

    public BitmapFontRenderer(FontBase font)
    {
        _font = font;
        _charWidth = _font.Width;
        _charHeight = _font.HeightToUse > 0 ? _font.HeightToUse : _font.Height;
    }

    public double GetCharWidth() => _charWidth;
    public double GetCharHeight() => _charHeight;

    public void DrawCharacter(object context, TerminalCell cell, double x, double y, object foreground)
    {
        var drawingContext = (DrawingContext)context;
        var brush = (IBrush)foreground;

        ushort charValue = (ushort)(cell.Codepoint <= 0xFFFF ? cell.Codepoint : 0);
        var geometry = GetGlyph(charValue, cell.FontNumber);

        if (geometry != null)
        {
            // The geometry is cell-local, so the cell's position is a translation. This is also
            // what lets the double-size line transform in TerminalRenderer compose with it.
            using (drawingContext.PushTransform(Matrix.CreateTranslation(x, y)))
            {
                drawingContext.DrawGeometry(brush, null, geometry);
            }
        }

        // Draw underline if needed (for bitmap fonts too).
        // Deliberately OUTSIDE the cached glyph: it is a cell attribute, not part of the
        // character's shape, and folding it in would double every glyph in the cache.
        if (cell.Attributes.HasAttribute(CharacterAttributes.Underline))
        {
            var pen = new Pen(brush, 1);
            var underlineY = y + _charHeight - 2;
            drawingContext.DrawLine(pen, new Point(x, underlineY), new Point(x + _charWidth, underlineY));
        }
    }

    /// <summary>
    /// The cached shape for a character, building it on first use.
    /// </summary>
    /// <returns>
    /// The glyph's geometry, or null when the font draws nothing for this character.
    /// </returns>
    private Geometry? GetGlyph(ushort charValue, int fontNumber)
    {
        // charValue is 16 bits, fontNumber is a handful of character sets, variant is 0..3.
        int key = charValue | (fontNumber << 16) | (_font.CharacterSetVariant << 20);

        if (_glyphCache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var built = BuildGlyph(charValue, fontNumber);
        _glyphCache[key] = built;
        GlyphsBuilt++;
        return built;
    }

    /// <summary>
    /// Turns a character's ROM bits into a geometry in cell-local coordinates.
    /// </summary>
    private Geometry? BuildGlyph(ushort charValue, int fontNumber)
    {
        // Try to get font bits with the current font number
        ushort[]? fontBits = _font.GetFontBits(charValue, fontNumber);

        // If the font has no glyph and the cell holds a Unicode national character (like Ä=U+00C4),
        // resolve it to the ASCII position the ROM draws it at under the font's ACTIVE variant.
        //
        // The renderer used to carry its own inline table for this. It was variant-blind and wrong
        // in four places (Ø and ø both resolved to 0x60), so the mapping now lives in Core next to
        // the ISO 646 tables it has to agree with. The renderer only draws bits.
        if (fontBits == null && charValue > 127)
        {
            var variant = (TDV2200ISO646Variant)_font.CharacterSetVariant;
            if (TDVCharacterSets.TryMapUnicodeToRomPosition((char)charValue, variant, out char romPosition))
            {
                fontBits = _font.GetFontBits(romPosition, fontNumber);
            }
        }

        // If still null, try with fontNum 0 (standard font) as fallback
        if (fontBits == null && fontNumber != 0)
        {
            fontBits = _font.GetFontBits(charValue, 0);
        }

        if (fontBits == null) return null;

        // Font dimensions
        int fontWidth = _font.Width;
        int fontHeight = _font.Height;
        int heightToUse = _font.HeightToUse > 0 ? _font.HeightToUse : fontHeight;

        // Calculate pixel size (scale bitmap to character cell size)
        double pixelWidth = _charWidth / fontWidth;
        double pixelHeight = _charHeight / heightToUse;

        var geometry = new StreamGeometry();
        bool anyInk = false;

        using (var stream = geometry.Open())
        {
            for (int row = 0; row < heightToUse && row < fontBits.Length; row++)
            {
                ushort rowBits = fontBits[row];
                if (rowBits == 0) continue;

                double pixelY = row * pixelHeight;

                // Walk the row and emit ONE rectangle per unbroken run of lit pixels. The old code
                // emitted one per pixel; a horizontal bar in a 9-wide font is 9 rectangles that
                // way and 1 this way.
                int runStart = -1;
                for (int col = 0; col <= fontWidth; col++)
                {
                    // Bits are MSB first, so column 0 is the highest bit. The extra iteration at
                    // col == fontWidth closes a run that reaches the right edge.
                    bool pixelOn = col < fontWidth && (rowBits & (1 << (fontWidth - 1 - col))) != 0;

                    if (pixelOn)
                    {
                        if (runStart < 0) runStart = col;
                        continue;
                    }

                    if (runStart >= 0)
                    {
                        AddRectangle(stream,
                            runStart * pixelWidth, pixelY,
                            col * pixelWidth, pixelY + pixelHeight);
                        anyInk = true;
                        runStart = -1;
                    }
                }
            }
        }

        return anyInk ? geometry : null;
    }

    /// <summary>
    /// Adds one axis-aligned rectangle as a closed figure.
    /// </summary>
    private static void AddRectangle(StreamGeometryContext stream, double left, double top, double right, double bottom)
    {
        stream.BeginFigure(new Point(left, top), true);
        stream.LineTo(new Point(right, top));
        stream.LineTo(new Point(right, bottom));
        stream.LineTo(new Point(left, bottom));
        stream.EndFigure(true);
    }
}
