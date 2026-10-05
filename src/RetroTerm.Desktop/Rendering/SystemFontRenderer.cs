using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using RetroTerm.Core.Terminal.Buffer;
using RetroTerm.Core.Terminal.Rendering;
using FontStyle = Avalonia.Media.FontStyle;

namespace RetroTerm.Desktop.Rendering;

/// <summary>
/// Default system font renderer for VT100/VT220 and other standard terminals.
/// Uses Avalonia system fonts (TrueType/OpenType).
///
/// Each glyph's outline is built ONCE and cached. What it replaced built a <c>Typeface</c>, a
/// <c>FormattedText</c> and a fresh geometry for EVERY CELL of EVERY FRAME — text shaping and
/// outline extraction, 1,920 times a frame for an 80x24 screen, to draw the same few dozen
/// distinct characters over and over.
///
/// Not thread-safe, and does not need to be: a renderer belongs to one canvas and only ever draws
/// on the UI thread.
/// </summary>
public class SystemFontRenderer : IFontRenderer
{
    private readonly Typeface _typeface;
    private readonly double _fontSize;
    private readonly double _charWidth;
    private readonly double _charHeight;

    /// <summary>
    /// Cached glyph outlines at the CELL ORIGIN, so one geometry serves every position the
    /// character appears at. Weight and slant are part of the key because they change the shape;
    /// colour is not, because it is applied by the brush at draw time.
    ///
    /// A null value is cached too — some codepoints produce no outline at all, and rediscovering
    /// that costs a full text-shaping pass.
    /// </summary>
    private readonly Dictionary<long, Geometry?> _glyphCache = new Dictionary<long, Geometry?>();

    /// <summary>
    /// How many glyphs have been built. Read by tests to prove the cache is used.
    /// </summary>
    internal int GlyphsBuilt { get; private set; }

    /// <summary>
    /// Consolas is Windows-only. Without a fallback list Avalonia substitutes a proportional UI font on
    /// Linux and macOS, and the fixed cell grid then shows letter-spaced text ("Ter mi nal").
    /// </summary>
    public const string DefaultFontFamily =
        "Consolas, Cascadia Mono, Menlo, DejaVu Sans Mono, Liberation Mono, Noto Sans Mono, Courier New";

    public SystemFontRenderer(string fontFamily = DefaultFontFamily, double fontSize = 14)
    {
        _typeface = new Typeface(fontFamily, FontStyle.Normal, FontWeight.Normal);
        _fontSize = fontSize;

        // Calculate character metrics
        var testText = new FormattedText(
            "M",
            System.Globalization.CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            _typeface,
            _fontSize,
            Brushes.White);

        _charWidth = testText.Width;
        _charHeight = testText.Height;
    }

    /// <summary>
    /// The character shapes a host downloaded, when this terminal has any.
    /// </summary>
    /// <remarks>
    /// A reference to the emulator's own store rather than a copy, so a set downloaded mid-session
    /// is drawn without anything having to notice and re-hand it over.
    /// </remarks>
    public RetroTerm.Core.Fonts.SoftFont? SoftFont { get; set; }

    /// <summary>
    /// Cached shapes for downloaded characters, built the same way the ROM fonts are.
    /// </summary>
    private readonly Dictionary<int, Geometry?> _softGlyphCache = new Dictionary<int, Geometry?>();

    /// <summary>
    /// How many downloaded characters the host has redefined, so the cache can be dropped when the
    /// set changes rather than drawing a stale shape.
    /// </summary>
    private int _softFontGeneration = -1;

    public double GetCharWidth() => _charWidth;
    public double GetCharHeight() => _charHeight;

    public void DrawCharacter(object context, TerminalCell cell, double x, double y, object foreground)
    {
        var drawingContext = (DrawingContext)context;
        var brush = (IBrush)foreground;

        // Determine font weight and style based on attributes
        bool bold = cell.Attributes.HasAttribute(CharacterAttributes.Bold);
        bool italic = cell.Attributes.HasAttribute(CharacterAttributes.Italic);

        // A cell from the downloaded set is drawn from the shape the HOST sent, not from the system
        // font. That is the whole point of DECDLD: the host is showing a character this machine has
        // no glyph for.
        var geometry = cell.CharacterSet == SoftFontCharacterSet
            ? GetSoftGlyph(cell.Codepoint)
            : GetGlyph(cell.Codepoint, bold, italic);

        if (geometry != null)
        {
            // The outline is built at the origin, so the cell's position is a translation. This is
            // also what lets the double-size line transform in TerminalRenderer compose with it.
            using (drawingContext.PushTransform(Matrix.CreateTranslation(x, y)))
            {
                // DrawGeometry with an explicit brush, so the colour is applied rather than
                // inherited from the FormattedText the outline came out of.
                drawingContext.DrawGeometry(brush, null, geometry);
            }
        }

        // Underline and strikethrough are cell ATTRIBUTES, not part of the character's shape, so
        // they stay outside the cache — folding them in would multiply every glyph by four.
        if (cell.Attributes.HasAttribute(CharacterAttributes.Underline))
        {
            var pen = new Pen(brush, 1);
            var underlineY = y + _charHeight - 2;
            drawingContext.DrawLine(pen, new Point(x, underlineY), new Point(x + _charWidth, underlineY));
        }

        if (cell.Attributes.HasAttribute(CharacterAttributes.Strikethrough))
        {
            var pen = new Pen(brush, 1);
            var strikeY = y + _charHeight / 2;
            drawingContext.DrawLine(pen, new Point(x, strikeY), new Point(x + _charWidth, strikeY));
        }
    }

    /// <summary>
    /// The character-set number that marks a cell as coming from the downloaded set.
    /// </summary>
    private const byte SoftFontCharacterSet =
        RetroTerm.Core.Terminal.Emulators.TerminalEmulatorBase.SoftFontCharacterSet;

    /// <summary>
    /// The shape for a downloaded character, built from the bits the host sent.
    /// </summary>
    /// <remarks>
    /// <para><b>Where the character sits in the set</b></para>
    /// A downloaded set covers the printable range, so the first shape the host sent is the
    /// character at 0x20. ASSUMPTION, flagged: DECDLD can describe a 94-character set starting at
    /// 0x21 instead, and its size parameter says which. That parameter is read and not acted on,
    /// so a 94-character set drawn here would be one place out. No host has been available to check
    /// which form is actually sent.
    ///
    /// <para><b>Built the same way the ROM fonts are</b></para>
    /// Consecutive lit pixels in a row become ONE rectangle rather than one each, and the finished
    /// shape is cached - the same two tricks that took the bitmap renderer off a quarter of a
    /// million draw calls per frame.
    /// </remarks>
    /// <param name="codepoint">
    /// The character in the cell.
    /// </param>
    /// <returns>
    /// The shape at the cell origin, or null when the host has not defined that character.
    /// </returns>
    private Geometry? GetSoftGlyph(uint codepoint)
    {
        var font = SoftFont;
        if (font == null) return null;

        // The host redefining the set invalidates every shape built from the old one.
        if (_softFontGeneration != font.Count)
        {
            _softGlyphCache.Clear();
            _softFontGeneration = font.Count;
        }

        int index = (int)codepoint - 0x20;
        if (index < 0) return null;

        if (_softGlyphCache.TryGetValue(index, out var cached)) return cached;

        Geometry? geometry = null;
        if (font.TryGetGlyph(index, out var rows) && font.MatrixWidth > 0 && font.MatrixHeight > 0)
        {
            geometry = BuildSoftGlyph(rows, font.MatrixWidth, font.MatrixHeight);
            GlyphsBuilt++;
        }

        _softGlyphCache[index] = geometry;
        return geometry;
    }

    /// <summary>
    /// Turns a downloaded character's bit rows into a shape that fills the cell.
    /// </summary>
    /// <param name="rows">
    /// One value per pixel row, bit 15 leftmost.
    /// </param>
    /// <param name="matrixWidth">
    /// Width of the host's matrix in pixels.
    /// </param>
    /// <param name="matrixHeight">
    /// Height of the host's matrix in pixels.
    /// </param>
    /// <returns>
    /// The shape, or null when nothing is lit.
    /// </returns>
    private Geometry? BuildSoftGlyph(ushort[] rows, int matrixWidth, int matrixHeight)
    {
        // The host's matrix is scaled to whatever cell this font actually draws in, so a
        // downloaded character is the same size as the text around it rather than the size the
        // host's hardware happened to use.
        double pixelWidth = _charWidth / matrixWidth;
        double pixelHeight = _charHeight / matrixHeight;

        var group = new GeometryGroup();

        for (int row = 0; row < matrixHeight && row < rows.Length; row++)
        {
            ushort bits = rows[row];
            if (bits == 0) continue;

            int column = 0;
            while (column < matrixWidth)
            {
                if ((bits & (0x8000 >> column)) == 0)
                {
                    column++;
                    continue;
                }

                // One rectangle per RUN of lit pixels, not one per pixel.
                int runStart = column;
                while (column < matrixWidth && (bits & (0x8000 >> column)) != 0) column++;

                group.Children.Add(new RectangleGeometry(new Rect(
                    runStart * pixelWidth,
                    row * pixelHeight,
                    (column - runStart) * pixelWidth,
                    pixelHeight)));
            }
        }

        return group.Children.Count > 0 ? group : null;
    }

    /// <summary>
    /// The cached outline for a character, building it on first use.
    /// </summary>
    /// <returns>
    /// The glyph's geometry at the origin, or null when the font produces no outline.
    /// </returns>
    private Geometry? GetGlyph(uint codepoint, bool bold, bool italic)
    {
        long key = codepoint | (bold ? 1L << 32 : 0L) | (italic ? 1L << 33 : 0L);

        if (_glyphCache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var built = BuildGlyph(codepoint, bold, italic);
        _glyphCache[key] = built;
        GlyphsBuilt++;
        return built;
    }

    /// <summary>
    /// Shapes one character and extracts its outline at the cell origin.
    /// </summary>
    private Geometry? BuildGlyph(uint codepoint, bool bold, bool italic)
    {
        var text = char.ConvertFromUtf32((int)codepoint);

        var weight = bold ? FontWeight.Bold : FontWeight.Normal;
        var style = italic ? FontStyle.Italic : FontStyle.Normal;
        var typeface = new Typeface(_typeface.FontFamily, style, weight);

        var formattedText = new FormattedText(
            text,
            System.Globalization.CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            typeface,
            _fontSize,
            Brushes.White);

        // Built at (0,0): the draw translates it into place, which is exactly what passing the
        // cell's position here used to do.
        return formattedText.BuildGeometry(new Point(0, 0));
    }
}
