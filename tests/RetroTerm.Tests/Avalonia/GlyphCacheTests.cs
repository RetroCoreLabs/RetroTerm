using System.Text;
using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using RetroTerm.Core.Fonts;
using RetroTerm.Core.Terminal.Buffer;
using RetroTerm.Core.Terminal.Emulators;
using RetroTerm.Desktop.Rendering;
using Xunit;

namespace RetroTerm.Tests.Avalonia;

/// <summary>
/// The glyph caches in the two font renderers — that they exist, and that they key on everything
/// that changes a glyph's shape.
///
/// WHAT WAS WRONG. Both renderers rebuilt every glyph from scratch for every cell of every frame:
///
///  * <see cref="BitmapFontRenderer"/> called <c>FontBase.GetFontBits</c>, which ALLOCATES a fresh
///    <c>ushort[]</c> per call — 1,920 arrays for one 80x24 frame, on a hot path this project's
///    rules say must not allocate — and then issued one <c>FillRectangle</c> per LIT PIXEL, up to
///    126 of them per cell.
///  * <see cref="SystemFontRenderer"/> built a <c>Typeface</c>, a <c>FormattedText</c> and a fresh
///    outline geometry per cell: full text shaping, 1,920 times a frame, for the few dozen
///    distinct characters actually on screen.
///
/// A cache is easy to claim and easy to get wrong in a way nothing notices, so these tests COUNT
/// the builds rather than trusting that it works. The other half of the check is the ~280 existing
/// headless UI tests: if the cached drawing differed from the old per-pixel drawing by so much as
/// a glyph shape, the TDV screenshot and GlyphPixelValidator tests would say so.
/// </summary>
[Collection("Avalonia")]
public class GlyphCacheTests
{
    // ─────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// A throwaway drawing surface, so a font renderer can be driven on its own.
    /// </summary>
    private static RenderTargetBitmap NewSurface() =>
        new RenderTargetBitmap(new PixelSize(64, 64), new Vector(96, 96));

    private static TerminalCell Cell(char ch, byte fontNumber = 0,
        CharacterAttributes attributes = CharacterAttributes.None)
    {
        var cell = new TerminalCell(ch);
        cell.Attributes = attributes;
        cell.FontNumber = fontNumber;
        return cell;
    }

    /// <summary>The font renderer inside a TerminalRenderer, reached the same way
    /// RenderedScreenshot reaches the renderer itself — tests stay out of production types.</summary>
    private static object? FontRendererOf(TerminalRenderer renderer)
    {
        var field = typeof(TerminalRenderer).GetField("_fontRenderer",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        return field?.GetValue(renderer);
    }

    private static void Feed(TerminalEmulatorBase emulator, string text)
        => emulator.ProcessData(Encoding.ASCII.GetBytes(text));

    // ─────────────────────────────────────────────────────────────
    // BitmapFontRenderer — the TDV ROM fonts
    // ─────────────────────────────────────────────────────────────

    [AvaloniaFact]
    public void ARepeatedCharacterIsBuiltOnce()
    {
        // THE test. Without the cache this count would be 50.
        var fontRenderer = new BitmapFontRenderer(new FontTDV2200());
        using var surface = NewSurface();

        using (var context = surface.CreateDrawingContext())
        {
            for (int i = 0; i < 50; i++)
            {
                fontRenderer.DrawCharacter(context, Cell('A'), 0, 0, Brushes.White);
            }
        }

        Assert.Equal(1, fontRenderer.GlyphsBuilt);
    }

    [AvaloniaFact]
    public void DistinctCharactersEachGetTheirOwnEntry()
    {
        // The guard against a cache that is too eager: keying on nothing would draw every
        // character as the first one ever drawn.
        var fontRenderer = new BitmapFontRenderer(new FontTDV2200());
        using var surface = NewSurface();

        using (var context = surface.CreateDrawingContext())
        {
            fontRenderer.DrawCharacter(context, Cell('A'), 0, 0, Brushes.White);
            fontRenderer.DrawCharacter(context, Cell('B'), 0, 0, Brushes.White);
            fontRenderer.DrawCharacter(context, Cell('A'), 0, 0, Brushes.White);
        }

        Assert.Equal(2, fontRenderer.GlyphsBuilt);
    }

    [AvaloniaFact]
    public void TheFontNumberIsPartOfTheKey()
    {
        // The same byte draws a different shape in a different TDV character set. A cache keyed
        // on the codepoint alone would show line-drawing characters as letters.
        var fontRenderer = new BitmapFontRenderer(new FontTDV2200());
        using var surface = NewSurface();

        using (var context = surface.CreateDrawingContext())
        {
            fontRenderer.DrawCharacter(context, Cell('A', fontNumber: 0), 0, 0, Brushes.White);
            fontRenderer.DrawCharacter(context, Cell('A', fontNumber: 2), 0, 0, Brushes.White);
        }

        Assert.Equal(2, fontRenderer.GlyphsBuilt);
    }

    [AvaloniaFact]
    public void TheIso646VariantIsPartOfTheKey()
    {
        // A TDV in Norwegian mode draws Æ where an international one draws '['. If the variant
        // were not in the key, switching the national character set would leave the OLD glyphs
        // on screen until something else evicted them — and nothing evicts them.
        var font = new FontTDV2215();
        var fontRenderer = new BitmapFontRenderer(font);
        using var surface = NewSurface();

        using (var context = surface.CreateDrawingContext())
        {
            font.CharacterSetVariant = 0;                                   // International
            fontRenderer.DrawCharacter(context, Cell('['), 0, 0, Brushes.White);

            font.CharacterSetVariant = 1;                                   // Norwegian/Danish
            fontRenderer.DrawCharacter(context, Cell('['), 0, 0, Brushes.White);
        }

        Assert.Equal(2, fontRenderer.GlyphsBuilt);
    }

    [AvaloniaFact]
    public void SwitchingBackToAVariantAlreadySeenCostsNothing()
    {
        // Keying on the variant rather than clearing the cache is what makes this free.
        var font = new FontTDV2215();
        var fontRenderer = new BitmapFontRenderer(font);
        using var surface = NewSurface();

        using (var context = surface.CreateDrawingContext())
        {
            font.CharacterSetVariant = 0;
            fontRenderer.DrawCharacter(context, Cell('['), 0, 0, Brushes.White);
            font.CharacterSetVariant = 1;
            fontRenderer.DrawCharacter(context, Cell('['), 0, 0, Brushes.White);
            font.CharacterSetVariant = 0;
            fontRenderer.DrawCharacter(context, Cell('['), 0, 0, Brushes.White);
        }

        Assert.Equal(2, fontRenderer.GlyphsBuilt);
    }

    [AvaloniaFact]
    public void ACharacterTheFontCannotDrawIsCachedToo()
    {
        // "There is no glyph here" is an answer worth remembering: rediscovering it costs the
        // whole three-step lookup — direct, national-variant remap, then fontNum 0 — every frame.
        var fontRenderer = new BitmapFontRenderer(new FontTDV2200());
        using var surface = NewSurface();

        using (var context = surface.CreateDrawingContext())
        {
            fontRenderer.DrawCharacter(context, Cell('中'), 0, 0, Brushes.White);
            fontRenderer.DrawCharacter(context, Cell('中'), 0, 0, Brushes.White);
            fontRenderer.DrawCharacter(context, Cell('中'), 0, 0, Brushes.White);
        }

        Assert.Equal(1, fontRenderer.GlyphsBuilt);
    }

    // ─────────────────────────────────────────────────────────────
    // SystemFontRenderer — the VT fonts
    // ─────────────────────────────────────────────────────────────

    [AvaloniaFact]
    public void TheSystemFontRendererAlsoBuildsARepeatedCharacterOnce()
    {
        var fontRenderer = new SystemFontRenderer();
        using var surface = NewSurface();

        using (var context = surface.CreateDrawingContext())
        {
            for (int i = 0; i < 50; i++)
            {
                fontRenderer.DrawCharacter(context, Cell('A'), 0, 0, Brushes.White);
            }
        }

        Assert.Equal(1, fontRenderer.GlyphsBuilt);
    }

    [AvaloniaFact]
    public void WeightAndSlantArePartOfTheSystemFontKey()
    {
        // Bold and italic pick a different typeface, so they are different SHAPES. Colour is not
        // in the key on purpose — the brush is applied at draw time.
        var fontRenderer = new SystemFontRenderer();
        using var surface = NewSurface();

        using (var context = surface.CreateDrawingContext())
        {
            fontRenderer.DrawCharacter(context, Cell('A'), 0, 0, Brushes.White);
            fontRenderer.DrawCharacter(context, Cell('A', attributes: CharacterAttributes.Bold), 0, 0, Brushes.White);
            fontRenderer.DrawCharacter(context, Cell('A', attributes: CharacterAttributes.Italic), 0, 0, Brushes.White);
            fontRenderer.DrawCharacter(context, Cell('A'), 0, 0, Brushes.Red);
        }

        Assert.Equal(3, fontRenderer.GlyphsBuilt);
    }

    // ─────────────────────────────────────────────────────────────
    // Through the real render path
    // ─────────────────────────────────────────────────────────────

    [AvaloniaFact]
    public void AWholeScreenOfOneCharacterBuildsExactlyOneGlyph()
    {
        // The end-to-end statement of the point: a full screen used to cost 1,920 rebuilds and
        // now costs one. Blank cells never reach the font renderer at all — TerminalRenderer
        // skips codepoint 0 and space — so the count is the number of DISTINCT characters shown.
        var emulator = new VT100Emulator(20, 4);
        var line = new string('A', 20);
        Feed(emulator, line + line + line + line);

        SystemFontRenderer? captured = null;
        using var shot = RenderedScreenshot.Capture(emulator, "glyph-cache-one-char",
            configureRenderer: r => captured = FontRendererOf(r) as SystemFontRenderer);

        Assert.NotNull(captured);
        Assert.Equal(1, captured!.GlyphsBuilt);
        Assert.True(shot.CellHasInk(0, 0), "the screen must actually have been drawn");
    }

    [AvaloniaFact]
    public void TheBuildCountFollowsTheNumberOfDistinctCharacters()
    {
        var emulator = new VT100Emulator(20, 4);
        Feed(emulator, "ABABABABABABABABABAB");

        SystemFontRenderer? captured = null;
        using var shot = RenderedScreenshot.Capture(emulator, "glyph-cache-two-chars",
            configureRenderer: r => captured = FontRendererOf(r) as SystemFontRenderer);

        Assert.NotNull(captured);
        Assert.Equal(2, captured!.GlyphsBuilt);
        Assert.True(shot.CellHasInk(0, 0), "the screen must actually have been drawn");
    }
}
