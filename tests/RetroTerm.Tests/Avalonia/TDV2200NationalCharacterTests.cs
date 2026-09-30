using System;
using System.IO;
using System.Text;
using Avalonia.Headless.XUnit;
using RetroTerm.Core.Fonts;
using RetroTerm.Core.Terminal.Emulators.TDV;
using SkiaSharp;
using Xunit;

namespace RetroTerm.Tests.Avalonia;

/// <summary>
/// Screenshot tests that verify TDV2200 bitmap font renders characters correctly.
///
/// The TDV2200 bitmap font contains national characters at positions 0x00-0x1F (control code range).
/// These are NOT directly displayable via normal character output - they're accessed through
/// special modes like "transparent mode" or "control code display mode".
///
/// For normal text display, the TDV2200 uses:
/// - Standard ASCII characters at their standard positions
/// - NO ISO646 mapping - brackets, braces, and @ render as their standard ASCII glyphs
/// - The bitmap font itself determines the appearance of each character code
/// </summary>
[Collection("Avalonia")]
public class TDV2200NationalCharacterTests
{
    private static readonly string ImagesFolder = Path.Combine(
        Path.GetDirectoryName(typeof(TDV2200NationalCharacterTests).Assembly.Location) ?? "",
        "..", "..", "..", "Avalonia", "images", "tdv2200");

    private static readonly FontTDV2200 _tdvFont = new FontTDV2200();

    static TDV2200NationalCharacterTests()
    {
        Directory.CreateDirectory(ImagesFolder);
    }

    #region Font Glyph Direct Verification Tests

    /// <summary>
    /// Verify the font has the Ä glyph at position 0x01.
    /// The glyph should have two dots above the A character.
    /// </summary>
    [AvaloniaFact]
    public void FontTDV2200_Position01_HasAUmlautGlyph()
    {
        // Verify the font has this glyph with the expected pattern (two dots above A)
        var glyphBits = _tdvFont.GetFontBits(0x01, 0);
        Assert.NotNull(glyphBits);

        // Row 1 should have the two dots pattern: 0x0044 = binary 01000100 = "  X   X  "
        Assert.Equal(0x0044, glyphBits[1]);

        // Save a visual representation of the glyph
        SaveGlyphScreenshot(0x01, "glyph_01_A_umlaut", "Font position 0x01 = Ä (A with umlaut)");
    }

    /// <summary>
    /// Verify the font has the Ü glyph at position 0x04.
    /// </summary>
    [AvaloniaFact]
    public void FontTDV2200_Position04_HasUUmlautGlyph()
    {
        var glyphBits = _tdvFont.GetFontBits(0x04, 0);
        Assert.NotNull(glyphBits);

        // Row 1 should have the two dots pattern: 0x0044 = "  X   X  "
        Assert.Equal(0x0044, glyphBits[1]);

        SaveGlyphScreenshot(0x04, "glyph_04_U_umlaut", "Font position 0x04 = Ü (U with umlaut)");
    }

    /// <summary>
    /// Verify the font has the Ø glyph at position 0x06.
    /// </summary>
    [AvaloniaFact]
    public void FontTDV2200_Position06_HasOSlashGlyph()
    {
        var glyphBits = _tdvFont.GetFontBits(0x06, 0);
        Assert.NotNull(glyphBits);

        // Should be a non-empty glyph
        bool hasPixels = false;
        for (int i = 0; i < glyphBits.Length && i < 14; i++)
        {
            if (glyphBits[i] != 0) hasPixels = true;
        }
        Assert.True(hasPixels, "Glyph at 0x06 should have visible pixels");

        SaveGlyphScreenshot(0x06, "glyph_06_O_slash", "Font position 0x06 = Ø (O with slash)");
    }

    /// <summary>
    /// Verify the font has the ä glyph at position 0x11.
    /// </summary>
    [AvaloniaFact]
    public void FontTDV2200_Position11_HasLowerAUmlautGlyph()
    {
        var glyphBits = _tdvFont.GetFontBits(0x11, 0);
        Assert.NotNull(glyphBits);

        // Row 1 should have the two dots pattern
        Assert.Equal(0x0044, glyphBits[1]);

        SaveGlyphScreenshot(0x11, "glyph_11_a_umlaut", "Font position 0x11 = ä (lowercase a with umlaut)");
    }

    /// <summary>
    /// Verify the font has the å glyph at position 0x16.
    /// </summary>
    [AvaloniaFact]
    public void FontTDV2200_Position16_HasARingGlyph()
    {
        var glyphBits = _tdvFont.GetFontBits(0x16, 0);
        Assert.NotNull(glyphBits);

        // Row 0-2 should have the ring pattern (small circle above the a)
        Assert.Equal(0x0030, glyphBits[0]); // Top of ring
        Assert.Equal(0x0048, glyphBits[1]); // Middle of ring
        Assert.Equal(0x0030, glyphBits[2]); // Bottom of ring

        SaveGlyphScreenshot(0x16, "glyph_16_a_ring", "Font position 0x16 = å (lowercase a with ring)");
    }

    /// <summary>
    /// Verify the font has the ü glyph at position 0x0A.
    /// </summary>
    [AvaloniaFact]
    public void FontTDV2200_Position0A_HasLowerUUmlautGlyph()
    {
        var glyphBits = _tdvFont.GetFontBits(0x0A, 0);
        Assert.NotNull(glyphBits);

        // Row 1 should have the two dots pattern: 0x0048 = " X  X "
        Assert.Equal(0x0048, glyphBits[1]);

        SaveGlyphScreenshot(0x0A, "glyph_0A_u_umlaut", "Font position 0x0A = ü (lowercase u with umlaut)");
    }

    /// <summary>
    /// Verify the font has the ö glyph at position 0x1C.
    /// </summary>
    [AvaloniaFact]
    public void FontTDV2200_Position1C_HasLowerOUmlautGlyph()
    {
        var glyphBits = _tdvFont.GetFontBits(0x1C, 0);
        Assert.NotNull(glyphBits);

        // Row 1 should have the two dots pattern: 0x0048 = " X  X "
        Assert.Equal(0x0048, glyphBits[1]);

        SaveGlyphScreenshot(0x1C, "glyph_1C_o_umlaut", "Font position 0x1C = ö (lowercase o with umlaut)");
    }

    /// <summary>
    /// Verify the font has the ø glyph at position 0x1D.
    /// </summary>
    [AvaloniaFact]
    public void FontTDV2200_Position1D_HasLowerOSlashGlyph()
    {
        var glyphBits = _tdvFont.GetFontBits(0x1D, 0);
        Assert.NotNull(glyphBits);

        // Should be a non-empty glyph
        bool hasPixels = false;
        for (int i = 0; i < glyphBits.Length && i < 14; i++)
        {
            if (glyphBits[i] != 0) hasPixels = true;
        }
        Assert.True(hasPixels, "Glyph at 0x1D should have visible pixels");

        SaveGlyphScreenshot(0x1D, "glyph_1D_o_slash", "Font position 0x1D = ø (lowercase o with slash)");
    }

    /// <summary>
    /// Verify the font has the æ glyph at position 0x10.
    /// </summary>
    [AvaloniaFact]
    public void FontTDV2200_Position10_HasAELigatureGlyph()
    {
        var glyphBits = _tdvFont.GetFontBits(0x10, 0);
        Assert.NotNull(glyphBits);

        // Should be a non-empty glyph
        bool hasPixels = false;
        for (int i = 0; i < glyphBits.Length && i < 14; i++)
        {
            if (glyphBits[i] != 0) hasPixels = true;
        }
        Assert.True(hasPixels, "Glyph at 0x10 should have visible pixels");

        SaveGlyphScreenshot(0x10, "glyph_10_ae_ligature", "Font position 0x10 = æ (ae ligature)");
    }

    #endregion

    #region ASCII Character Rendering Tests

    /// <summary>
    /// Test that the @ character (0x40) renders correctly as @ symbol.
    /// This was the original issue - @ was being mapped incorrectly to Ä via ISO646.
    /// After removing ISO646 mapping, @ should render as @.
    /// </summary>
    [AvaloniaFact]
    public void Emulator_AtSign_ShouldRenderAsAtSign()
    {
        // Arrange
        var emulator = new TDV2200Emulator(80, 24);

        // Act - Write @ character (0x40 = 64)
        emulator.ProcessData(new byte[] { 0x40 }); // @

        // Assert - Verify the character is stored as @ (not mapped to anything else)
        var cell = emulator.Buffer.GetCell(0, 0);
        Assert.Equal('@', (char)cell.Codepoint);

        // Verify the font has the @ glyph (not Ä)
        var glyphBits = _tdvFont.GetFontBits(0x40, 0);
        Assert.NotNull(glyphBits);

        // @ glyph should NOT have two dots on row 1 (that would be Ä)
        Assert.NotEqual(0x0044, glyphBits[1]); // Should NOT be the Ä pattern

        SaveEmulatorScreenshot(emulator, "char_40_at_sign", "@ (0x40) renders as @ - NOT mapped to Ä");
    }

    /// <summary>
    /// Test that standard ASCII brackets are rendered correctly (not mapped to national chars).
    /// </summary>
    [AvaloniaFact]
    public void Emulator_Brackets_ShouldRenderAsBrackets()
    {
        // Arrange
        var emulator = new TDV2200Emulator(80, 24);

        // Act - Write brackets
        emulator.ProcessData(new byte[] { 0x5B, 0x5C, 0x5D }); // [ \ ]

        // Assert - Verify brackets are stored unchanged
        Assert.Equal('[', (char)emulator.Buffer.GetCell(0, 0).Codepoint);
        Assert.Equal('\\', (char)emulator.Buffer.GetCell(0, 1).Codepoint);
        Assert.Equal(']', (char)emulator.Buffer.GetCell(0, 2).Codepoint);

        SaveEmulatorScreenshot(emulator, "brackets_unchanged",
            "Brackets [ \\ ] render as brackets - NOT mapped to Æ Ø Å");
    }

    /// <summary>
    /// Test that standard ASCII braces are rendered correctly (not mapped to national chars).
    /// </summary>
    [AvaloniaFact]
    public void Emulator_Braces_ShouldRenderAsBraces()
    {
        // Arrange
        var emulator = new TDV2200Emulator(80, 24);

        // Act - Write braces
        emulator.ProcessData(new byte[] { 0x7B, 0x7C, 0x7D }); // { | }

        // Assert - Verify braces are stored unchanged
        Assert.Equal('{', (char)emulator.Buffer.GetCell(0, 0).Codepoint);
        Assert.Equal('|', (char)emulator.Buffer.GetCell(0, 1).Codepoint);
        Assert.Equal('}', (char)emulator.Buffer.GetCell(0, 2).Codepoint);

        SaveEmulatorScreenshot(emulator, "braces_unchanged",
            "Braces { | } render as braces - NOT mapped to æ ø å");
    }

    /// <summary>
    /// Test that the tilde character renders correctly.
    /// </summary>
    [AvaloniaFact]
    public void Emulator_Tilde_ShouldRenderAsTilde()
    {
        // Arrange
        var emulator = new TDV2200Emulator(80, 24);

        // Act - Write tilde
        emulator.ProcessData(new byte[] { 0x7E }); // ~

        // Assert
        Assert.Equal('~', (char)emulator.Buffer.GetCell(0, 0).Codepoint);

        SaveEmulatorScreenshot(emulator, "tilde_unchanged",
            "Tilde ~ renders as tilde - NOT mapped to ß or other");
    }

    #endregion

    #region Font Glyph Comparison Screenshots

    /// <summary>
    /// Screenshot showing all national character glyphs in the TDV2200 font (0x00-0x1F range).
    /// These glyphs exist in the font but are not normally displayable (control code range).
    /// </summary>
    [AvaloniaFact]
    public void Screenshot_AllNationalGlyphs_ControlCodeRange()
    {
        const int charWidth = 8;
        const int charHeight = 14;
        const int scale = 4; // 4x scale for visibility
        const int gridPadding = 4;
        const int cols = 8;
        const int rows = 4;

        int cellWidth = charWidth * scale + gridPadding;
        int cellHeight = charHeight * scale + gridPadding + 20; // Extra for label

        var width = cols * cellWidth + gridPadding;
        var height = rows * cellHeight + gridPadding + 40; // Header

        using var bitmap = new SKBitmap(width, height);
        using var canvas = new SKCanvas(bitmap);

        canvas.Clear(SKColors.Black);

        // Draw header
        using var headerFont = new SKFont(SKTypeface.FromFamilyName("Consolas", SKFontStyle.Bold), 14);
        using var labelFont = new SKFont(SKTypeface.FromFamilyName("Consolas", SKFontStyle.Normal), 10);
        using var headerPaint = new SKPaint { Color = SKColors.Yellow };
        canvas.DrawText("TDV2200 Font - National Characters (0x00-0x1F)", 5, 20, SKTextAlign.Left, headerFont, headerPaint);

        var fgColor = SKColors.LightGreen;

        // Draw each character glyph
        for (int charCode = 0; charCode < 32; charCode++)
        {
            int col = charCode % cols;
            int row = charCode / cols;

            int x = col * cellWidth + gridPadding;
            int y = row * cellHeight + gridPadding + 40;

            // Draw background box
            using var boxPaint = new SKPaint { Color = new SKColor(20, 40, 20) };
            canvas.DrawRect(x, y, charWidth * scale, charHeight * scale, boxPaint);

            // Draw label
            using var labelPaint = new SKPaint { Color = SKColors.Gray };
            canvas.DrawText($"{charCode:X2}", x, y + charHeight * scale + 12, SKTextAlign.Left, labelFont, labelPaint);

            // Get and draw glyph
            var glyphBits = _tdvFont.GetFontBits((ushort)charCode, 0);
            if (glyphBits != null)
            {
                using var glyphPaint = new SKPaint { Color = fgColor };
                RenderGlyphScaled(canvas, glyphPaint, glyphBits, x, y, charWidth, charHeight, scale);
            }
        }

        var pngPath = Path.Combine(ImagesFolder, "all_national_glyphs_0x00_0x1F.png");
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = File.OpenWrite(pngPath);
        data.SaveTo(stream);
    }

    /// <summary>
    /// Screenshot comparing ASCII characters that would be mapped in ISO646 variants.
    /// Shows that [ \ ] { | } @ ~ all render as their standard ASCII glyphs.
    /// </summary>
    [AvaloniaFact]
    public void Screenshot_ISO646PositionCharacters_RenderAsASCII()
    {
        var emulator = new TDV2200Emulator(80, 24);

        emulator.ProcessData(Encoding.UTF8.GetBytes("TDV2200 - ISO646 Position Characters:\r\n"));
        emulator.ProcessData(Encoding.UTF8.GetBytes("(No mapping - renders as standard ASCII)\r\n\r\n"));

        emulator.ProcessData(Encoding.UTF8.GetBytes("@ (0x40): @\r\n"));
        emulator.ProcessData(Encoding.UTF8.GetBytes("[ (0x5B): [\r\n"));
        emulator.ProcessData(Encoding.UTF8.GetBytes("\\ (0x5C): \\\r\n"));
        emulator.ProcessData(Encoding.UTF8.GetBytes("] (0x5D): ]\r\n"));
        emulator.ProcessData(Encoding.UTF8.GetBytes("^ (0x5E): ^\r\n"));
        emulator.ProcessData(Encoding.UTF8.GetBytes("` (0x60): `\r\n"));
        emulator.ProcessData(Encoding.UTF8.GetBytes("{ (0x7B): {\r\n"));
        emulator.ProcessData(Encoding.UTF8.GetBytes("| (0x7C): |\r\n"));
        emulator.ProcessData(Encoding.UTF8.GetBytes("} (0x7D): }\r\n"));
        emulator.ProcessData(Encoding.UTF8.GetBytes("~ (0x7E): ~\r\n"));

        SaveEmulatorScreenshot(emulator, "iso646_positions_as_ascii",
            "ISO646 position chars render as standard ASCII");
    }

    #endregion

    #region ISO 646 National Variant Font Tests

    private static bool GlyphsEqual(ushort[]? a, ushort[]? b)
    {
        if (a == null || b == null || a.Length != b.Length) return false;
        for (int i = 0; i < a.Length; i++)
            if (a[i] != b[i]) return false;
        return true;
    }

    [AvaloniaFact]
    public void FontVariant_International_RendersAsciiAtIso646Positions()
    {
        var font = new FontTDV2200 { CharacterSetVariant = 0 };

        // International must keep the standard ASCII glyphs at the ISO 646 positions.
        Assert.True(GlyphsEqual(font.GetFontBits(0x5B, 0), new FontTDV2200().GetFontBits(0x5B, 0)));
        Assert.True(GlyphsEqual(font.GetFontBits(0x7C, 0), new FontTDV2200().GetFontBits(0x7C, 0)));
    }

    [AvaloniaFact]
    public void FontVariant_Norwegian_RemapsToNationalGlyphs()
    {
        var font = new FontTDV2200 { CharacterSetVariant = 1 };

        // Wire positions must now resolve to the national ROM glyphs.
        Assert.True(GlyphsEqual(font.GetFontBits(0x5B, 0), font.GetFontBits(0x05, 0))); // [ -> Æ
        Assert.True(GlyphsEqual(font.GetFontBits(0x5C, 0), font.GetFontBits(0x06, 0))); // \ -> Ø
        Assert.True(GlyphsEqual(font.GetFontBits(0x5D, 0), font.GetFontBits(0x02, 0))); // ] -> Å
        Assert.True(GlyphsEqual(font.GetFontBits(0x7B, 0), font.GetFontBits(0x10, 0))); // { -> æ
        Assert.True(GlyphsEqual(font.GetFontBits(0x7C, 0), font.GetFontBits(0x1D, 0))); // | -> ø
        Assert.True(GlyphsEqual(font.GetFontBits(0x7D, 0), font.GetFontBits(0x16, 0))); // } -> å

        // And must differ from the International (ASCII) glyphs.
        var intl = new FontTDV2200 { CharacterSetVariant = 0 };
        Assert.False(GlyphsEqual(font.GetFontBits(0x5B, 0), intl.GetFontBits(0x5B, 0)));
    }

    [AvaloniaFact]
    public void FontVariant_Swedish_RemapsToNationalGlyphs()
    {
        var font = new FontTDV2200 { CharacterSetVariant = 2 };

        Assert.True(GlyphsEqual(font.GetFontBits(0x5B, 0), font.GetFontBits(0x01, 0))); // [ -> Ä
        Assert.True(GlyphsEqual(font.GetFontBits(0x5D, 0), font.GetFontBits(0x02, 0))); // ] -> Å
        Assert.True(GlyphsEqual(font.GetFontBits(0x7B, 0), font.GetFontBits(0x11, 0))); // { -> ä
        Assert.True(GlyphsEqual(font.GetFontBits(0x7C, 0), font.GetFontBits(0x1C, 0))); // | -> ö
        Assert.True(GlyphsEqual(font.GetFontBits(0x7D, 0), font.GetFontBits(0x16, 0))); // } -> å

        // Ö is synthesized (no ROM glyph); it must be non-empty and differ from ASCII '\'.
        var oUmlaut = font.GetFontBits(0x5C, 0);
        Assert.NotNull(oUmlaut);
        Assert.False(GlyphsEqual(oUmlaut, new FontTDV2200().GetFontBits(0x5C, 0)));
    }

    [AvaloniaFact]
    public void FontVariant_German_RemapsToNationalGlyphs()
    {
        var font = new FontTDV2200 { CharacterSetVariant = 3 };

        Assert.True(GlyphsEqual(font.GetFontBits(0x5B, 0), font.GetFontBits(0x01, 0))); // [ -> Ä
        Assert.True(GlyphsEqual(font.GetFontBits(0x5D, 0), font.GetFontBits(0x04, 0))); // ] -> Ü
        Assert.True(GlyphsEqual(font.GetFontBits(0x7B, 0), font.GetFontBits(0x11, 0))); // { -> ä
        Assert.True(GlyphsEqual(font.GetFontBits(0x7C, 0), font.GetFontBits(0x1C, 0))); // | -> ö
        Assert.True(GlyphsEqual(font.GetFontBits(0x7D, 0), font.GetFontBits(0x0A, 0))); // } -> ü

        // Ö (0x5C) and ß (0x7E) are synthesized; must be non-empty and differ from ASCII.
        Assert.False(GlyphsEqual(font.GetFontBits(0x5C, 0), new FontTDV2200().GetFontBits(0x5C, 0)));
        Assert.False(GlyphsEqual(font.GetFontBits(0x7E, 0), new FontTDV2200().GetFontBits(0x7E, 0)));
    }

    #endregion

    #region Helper Methods

    private void SaveGlyphScreenshot(ushort charCode, string filename, string description)
    {
        const int charWidth = 8;
        const int charHeight = 14;
        const int scale = 8; // 8x scale for single glyph visibility
        const int headerHeight = 28;
        const int padding = 10;

        var width = charWidth * scale + padding * 2;
        var height = charHeight * scale + padding * 2 + headerHeight;

        using var bitmap = new SKBitmap(width, height);
        using var canvas = new SKCanvas(bitmap);

        canvas.Clear(SKColors.Black);

        // Draw header
        using var headerFont = new SKFont(SKTypeface.FromFamilyName("Consolas", SKFontStyle.Bold), 12);
        using var headerBgPaint = new SKPaint { Color = new SKColor(0, 60, 0) };
        canvas.DrawRect(0, 0, width, headerHeight, headerBgPaint);
        using var headerPaint = new SKPaint { Color = SKColors.White };
        canvas.DrawText(description, 5, 18, SKTextAlign.Left, headerFont, headerPaint);

        // Draw glyph background
        using var boxPaint = new SKPaint { Color = new SKColor(20, 40, 20) };
        canvas.DrawRect(padding, headerHeight + padding, charWidth * scale, charHeight * scale, boxPaint);

        // Get and draw glyph
        var glyphBits = _tdvFont.GetFontBits(charCode, 0);
        if (glyphBits != null)
        {
            using var glyphPaint = new SKPaint { Color = SKColors.LightGreen };
            RenderGlyphScaled(canvas, glyphPaint, glyphBits, padding, headerHeight + padding, charWidth, charHeight, scale);
        }

        var pngPath = Path.Combine(ImagesFolder, filename + ".png");
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = File.OpenWrite(pngPath);
        data.SaveTo(stream);
    }

    private void SaveEmulatorScreenshot(TDV2200Emulator emulator, string filename, string description)
    {
        const int charWidth = 8;
        const int charHeight = 14;
        const int headerHeight = 28;
        const int scale = 2;

        var width = emulator.Buffer.Width * charWidth * scale;
        var height = emulator.Buffer.Height * charHeight * scale + headerHeight;

        using var bitmap = new SKBitmap(width, height);
        using var canvas = new SKCanvas(bitmap);

        canvas.Clear(SKColors.Black);

        // Draw header
        using var headerFont = new SKFont(SKTypeface.FromFamilyName("Consolas", SKFontStyle.Bold), 14);
        using var headerBgPaint = new SKPaint { Color = new SKColor(0, 60, 0) };
        canvas.DrawRect(0, 0, width, headerHeight, headerBgPaint);
        using var headerPaint = new SKPaint { Color = SKColors.White };
        canvas.DrawText(description, 5, 19, SKTextAlign.Left, headerFont, headerPaint);

        var fgColor = SKColors.LightGreen;

        // Render each cell
        for (int row = 0; row < emulator.Buffer.Height; row++)
        {
            for (int col = 0; col < emulator.Buffer.Width; col++)
            {
                var cell = emulator.Buffer.GetCell(row, col);
                ushort charValue = (ushort)(cell.Codepoint > 0 ? cell.Codepoint : ' ');

                int x = col * charWidth * scale;
                int y = row * charHeight * scale + headerHeight;

                var glyphBits = _tdvFont.GetFontBits(charValue, cell.FontNumber);
                if (glyphBits == null && cell.FontNumber != 0)
                {
                    glyphBits = _tdvFont.GetFontBits(charValue, 0);
                }

                if (glyphBits != null)
                {
                    using var paint = new SKPaint { Color = fgColor };
                    RenderGlyphScaled(canvas, paint, glyphBits, x, y, charWidth, charHeight, scale);
                }
            }
        }

        var pngPath = Path.Combine(ImagesFolder, filename + ".png");
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = File.OpenWrite(pngPath);
        data.SaveTo(stream);
    }

    private void RenderGlyphScaled(SKCanvas canvas, SKPaint paint, ushort[] fontBits,
        int x, int y, int charWidth, int charHeight, int scale)
    {
        int fontHeight = _tdvFont.HeightToUse > 0 ? _tdvFont.HeightToUse : _tdvFont.Height;
        int fontWidth = _tdvFont.Width;

        for (int row = 0; row < fontHeight && row < fontBits.Length; row++)
        {
            ushort rowBits = fontBits[row];
            int pixelY = y + (row * scale);

            for (int col = 0; col < fontWidth; col++)
            {
                int bitIndex = fontWidth - 1 - col;
                bool pixelOn = (rowBits & (1 << bitIndex)) != 0;

                if (pixelOn)
                {
                    int pixelX = x + (col * scale);
                    canvas.DrawRect(pixelX, pixelY, scale, scale, paint);
                }
            }
        }
    }

    #endregion
}
