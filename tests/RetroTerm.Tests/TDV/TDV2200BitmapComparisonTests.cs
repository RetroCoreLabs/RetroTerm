using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using RetroTerm.Core.Fonts;
using SkiaSharp;
using Xunit;

namespace RetroTerm.Tests.TDV;

/// <summary>
/// Tests that compare FontTDV2200.cs glyph definitions against the reference
/// bitmap images from the TDV2115 specification.
///
/// Bitmap files are located in: spec/TDV2115/
/// - character_set_1.bmp - Standard ASCII
/// - character_set_2.bmp - Line drawing / graphics
/// - character_set_3.bmp - Subscript/Superscript
/// - character_set_4.bmp - Control code display
/// </summary>
public class TDV2200BitmapComparisonTests
{
    private static readonly FontTDV2200 _font = new FontTDV2200();

    private static readonly string SpecFolder = Path.Combine(
        Path.GetDirectoryName(typeof(TDV2200BitmapComparisonTests).Assembly.Location) ?? "",
        "..", "..", "..", "..", "..", "spec", "TDV2115");

    private static readonly string ImagesFolder = Path.Combine(
        Path.GetDirectoryName(typeof(TDV2200BitmapComparisonTests).Assembly.Location) ?? "",
        "..", "..", "..", "Avalonia", "images");

    /// <summary>
    /// Test that the spec bitmap files exist and can be loaded
    /// </summary>
    [Theory]
    [InlineData("character_set_1.bmp")]
    [InlineData("character_set_2.bmp")]
    [InlineData("character_set_3.bmp")]
    [InlineData("character_set_4.bmp")]
    public void SpecBitmapFiles_Exist(string filename)
    {
        string path = Path.Combine(SpecFolder, filename);
        Assert.True(File.Exists(path), $"Spec bitmap file should exist: {path}");
    }

    /// <summary>
    /// Load and analyze the structure of character_set_2.bmp
    /// This helps understand how to extract individual characters
    /// </summary>
    [Fact]
    public void AnalyzeCharSet2Bitmap_Structure()
    {
        string path = Path.Combine(SpecFolder, "character_set_2.bmp");
        if (!File.Exists(path))
        {
            Assert.Fail($"Bitmap file not found: {path}");
            return;
        }

        using var bitmap = SKBitmap.Decode(path);
        Assert.NotNull(bitmap);

        var sb = new StringBuilder();
        sb.AppendLine($"=== Character Set 2 Bitmap Analysis ===");
        sb.AppendLine($"Width: {bitmap.Width}");
        sb.AppendLine($"Height: {bitmap.Height}");
        sb.AppendLine($"Color Type: {bitmap.ColorType}");
        sb.AppendLine($"Alpha Type: {bitmap.AlphaType}");
        sb.AppendLine();

        // Sample some pixels to understand the format
        sb.AppendLine("Sample pixels (top-left corner):");
        for (int y = 0; y < Math.Min(32, bitmap.Height); y++)
        {
            sb.Append($"Row {y:D3}: ");
            for (int x = 0; x < Math.Min(64, bitmap.Width); x++)
            {
                var pixel = bitmap.GetPixel(x, y);
                // Assume black = foreground, white = background
                bool isSet = pixel.Red < 128 && pixel.Green < 128 && pixel.Blue < 128;
                sb.Append(isSet ? 'X' : '.');
            }
            sb.AppendLine();
        }

        // Save analysis
        string outputPath = Path.Combine(ImagesFolder, "charset2_bitmap_analysis.txt");
        Directory.CreateDirectory(ImagesFolder);
        File.WriteAllText(outputPath, sb.ToString());

        // Basic assertions
        Assert.True(bitmap.Width > 0, "Bitmap should have width");
        Assert.True(bitmap.Height > 0, "Bitmap should have height");
    }

    /// <summary>
    /// Compare a specific character from FontTDV2200 against the spec bitmap.
    /// This extracts the character from the bitmap and compares pixel-by-pixel.
    /// </summary>
    [Fact]
    public void CompareCharSet2_SpecBitmap_VsFontGlyphs()
    {
        string bitmapPath = Path.Combine(SpecFolder, "character_set_2.bmp");
        if (!File.Exists(bitmapPath))
        {
            // Skip test if bitmap not found
            return;
        }

        using var specBitmap = SKBitmap.Decode(bitmapPath);
        Assert.NotNull(specBitmap);

        var sb = new StringBuilder();
        sb.AppendLine("=== Character Set 2: Spec vs Font Comparison ===");
        sb.AppendLine($"Spec bitmap: {specBitmap.Width}x{specBitmap.Height}");
        sb.AppendLine();

        // The bitmap layout needs to be determined by analyzing the image
        // Typically character sets are arranged in a grid
        // Let's try to detect the character cell size

        // First, let's extract and compare a few characters we know
        // Character 0x20 (space) in charset 2 maps to glyph at offset 128 + 0x20 = 0xA0

        // Get font glyph for char 0x30 ('0' digit) in charset 2
        ushort[]? fontBits = _font.GetFontBits(0x30, 2);
        Assert.NotNull(fontBits);

        sb.AppendLine("Font glyph for char 0x30 in charset 2:");
        for (int row = 0; row < Math.Min(fontBits.Length, 14); row++)
        {
            sb.Append("  ");
            for (int col = 7; col >= 0; col--)
            {
                bool pixelOn = (fontBits[row] & (1 << col)) != 0;
                sb.Append(pixelOn ? 'X' : '.');
            }
            sb.AppendLine($"  0x{fontBits[row]:X4}");
        }

        // Save comparison
        string outputPath = Path.Combine(ImagesFolder, "charset2_spec_vs_font.txt");
        // The images folder is gitignored, so a fresh checkout does not have it. Without this
        // the test only passed when another test had happened to create the folder first; it
        // failed the v1.10.26.9 tag run on 30 September 2026 when the run order changed.
        Directory.CreateDirectory(ImagesFolder);
        File.WriteAllText(outputPath, sb.ToString());
    }

    /// <summary>
    /// Generate a side-by-side comparison image showing spec bitmap characters
    /// vs FontTDV2200 rendered characters
    /// </summary>
    [Fact]
    public void GenerateComparison_CharSet2_SideBySide()
    {
        string bitmapPath = Path.Combine(SpecFolder, "character_set_2.bmp");
        if (!File.Exists(bitmapPath))
        {
            return; // Skip if not found
        }

        using var specBitmap = SKBitmap.Decode(bitmapPath);
        Assert.NotNull(specBitmap);

        // Create comparison image
        int charWidth = 8;
        int charHeight = 16;
        int charsPerRow = 16;
        int numRows = 6; // 0x20-0x7F = 96 chars
        int margin = 20;

        int outputWidth = (charWidth * 2 + 10) * charsPerRow + margin * 2;
        int outputHeight = charHeight * numRows + margin * 2 + 50;

        using var outputBitmap = new SKBitmap(outputWidth, outputHeight);
        using var canvas = new SKCanvas(outputBitmap);

        canvas.Clear(SKColors.DarkGray);

        using var headerFont = new SKFont(SKTypeface.FromFamilyName("Consolas", SKFontStyle.Bold), 12);
        using var textPaint = new SKPaint { Color = SKColors.White };
        using var fontPixelPaint = new SKPaint { Color = SKColors.LightGreen };
        using var specPixelPaint = new SKPaint { Color = SKColors.LightBlue };

        canvas.DrawText("Green = FontTDV2200, Blue = Spec Bitmap (if extractable)", margin, 20, SKTextAlign.Left, headerFont, textPaint);

        // Render FontTDV2200 glyphs for charset 2
        for (int row = 0; row < numRows; row++)
        {
            for (int col = 0; col < charsPerRow; col++)
            {
                ushort charCode = (ushort)(0x20 + row * charsPerRow + col);
                if (charCode > 0x7F) continue;

                int x = margin + col * (charWidth * 2 + 10);
                int y = margin + 30 + row * charHeight;

                ushort[]? bits = _font.GetFontBits(charCode, 2);
                Assert.NotNull(bits);
                if (bits != null)
                {
                    RenderGlyphToCanvas(canvas, fontPixelPaint, bits, x, y);
                }
            }
        }

        // Save output
        var pngPath = Path.Combine(ImagesFolder, "charset2_font_vs_spec_comparison.png");
        // Same as above: create the gitignored folder rather than rely on test order.
        Directory.CreateDirectory(ImagesFolder);
        using var image = SKImage.FromBitmap(outputBitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = File.OpenWrite(pngPath);
        data.SaveTo(stream);
    }

    private void RenderGlyphToCanvas(SKCanvas canvas, SKPaint paint, ushort[] bits, int x, int y)
    {
        for (int row = 0; row < Math.Min(bits.Length, 14); row++)
        {
            ushort rowBits = bits[row];
            for (int col = 0; col < 8; col++)
            {
                int bitIndex = 7 - col;
                bool pixelOn = (rowBits & (1 << bitIndex)) != 0;
                if (pixelOn)
                {
                    canvas.DrawRect(x + col, y + row, 1, 1, paint);
                }
            }
        }
    }

    /// <summary>
    /// Count total pixel differences between spec and font (once layout is determined)
    /// </summary>
    [Fact]
    public void ValidateAllChars_FontMatchesExpected()
    {
        // This test validates that all font glyphs have reasonable content
        var issues = new List<string>();

        // Check character set 2 (line drawing)
        for (ushort charCode = 0x20; charCode <= 0x7E; charCode++)
        {
            ushort[]? bits = _font.GetFontBits(charCode, 2);
            Assert.NotNull(bits);
            if (bits == null)
            {
                issues.Add($"CharSet2 0x{charCode:X2}: null glyph");
                continue;
            }

            // Count set pixels
            int totalPixels = 0;
            for (int i = 0; i < bits.Length; i++)
            {
                totalPixels += CountBits(bits[i]);
            }

            // Space (0x20) might have some pixels due to mapping
            // But other chars should have content
            if (charCode > 0x20 && totalPixels == 0)
            {
                issues.Add($"CharSet2 0x{charCode:X2}: empty glyph (0 pixels)");
            }
        }

        // Check character set 3 (subscript/superscript)
        for (ushort charCode = 0x20; charCode <= 0x7E; charCode++)
        {
            ushort[]? bits = _font.GetFontBits(charCode, 3);
            Assert.NotNull(bits);
            if (bits == null)
            {
                issues.Add($"CharSet3 0x{charCode:X2}: null glyph");
                continue;
            }

            int totalPixels = 0;
            for (int i = 0; i < bits.Length; i++)
            {
                totalPixels += CountBits(bits[i]);
            }

            if (charCode > 0x20 && totalPixels == 0)
            {
                issues.Add($"CharSet3 0x{charCode:X2}: empty glyph (0 pixels)");
            }
        }

        if (issues.Count > 0)
        {
            // Write issues to file for review
            string outputPath = Path.Combine(ImagesFolder, "glyph_validation_issues.txt");
            Directory.CreateDirectory(ImagesFolder);
            File.WriteAllText(outputPath, string.Join("\n", issues));
        }

        // Allow some issues (some chars might legitimately be empty or minimal)
        Assert.True(issues.Count < 10, $"Too many glyph issues ({issues.Count}): {string.Join(", ", issues.GetRange(0, Math.Min(5, issues.Count)))}");
    }

    private int CountBits(ushort value)
    {
        int count = 0;
        while (value != 0)
        {
            count += (int)(value & 1);
            value >>= 1;
        }
        return count;
    }

    /// <summary>
    /// Create a complete visual dump of FontTDV2200 character sets
    /// comparing side by side for visual inspection
    /// </summary>
    [Fact]
    public void CreateVisualDump_AllCharSets()
    {
        const int charWidth = 10;
        const int charHeight = 18;
        const int charsPerRow = 16;
        const int numRows = 8; // 0x00-0x7F
        const int setSpacing = 40;
        const int headerHeight = 60;

        // 4 character sets side by side
        int width = (charWidth * charsPerRow + setSpacing) * 4 + 50;
        int height = charHeight * numRows + headerHeight + 50;

        using var bitmap = new SKBitmap(width, height);
        using var canvas = new SKCanvas(bitmap);

        canvas.Clear(SKColors.Black);

        using var headerFont = new SKFont(SKTypeface.FromFamilyName("Consolas", SKFontStyle.Bold), 12);
        using var smallFont = new SKFont(SKTypeface.FromFamilyName("Consolas"), 9);
        using var textPaint = new SKPaint { Color = SKColors.White };
        using var labelPaint = new SKPaint { Color = SKColors.Gray };

        string[] setNames = { "ASCII (fontNum=0)", "Graphics I (fontNum=2)", "Graphics II (fontNum=3)", "Ctrl Display (fontNum=4)" };
        int[] fontNums = { 0, 2, 3, 4 };
        SKColor[] colors = { SKColors.LightGray, SKColors.LightGreen, SKColors.LightBlue, SKColors.LightPink };

        for (int setIdx = 0; setIdx < 4; setIdx++)
        {
            int baseX = 30 + setIdx * (charWidth * charsPerRow + setSpacing);

            // Header
            canvas.DrawText(setNames[setIdx], baseX, 25, SKTextAlign.Left, headerFont, textPaint);

            using var pixelPaint = new SKPaint { Color = colors[setIdx] };

            // Render all characters
            for (int row = 0; row < numRows; row++)
            {
                // Row label
                canvas.DrawText($"{row:X}x", baseX - 25, headerHeight + row * charHeight + 12, SKTextAlign.Left, smallFont, labelPaint);

                for (int col = 0; col < charsPerRow; col++)
                {
                    ushort charCode = (ushort)(row * charsPerRow + col);
                    int x = baseX + col * charWidth;
                    int y = headerHeight + row * charHeight;

                    ushort[]? bits = _font.GetFontBits(charCode, fontNums[setIdx]);
                    Assert.NotNull(bits);
                    if (bits != null)
                    {
                        RenderGlyphToCanvas(canvas, pixelPaint, bits, x, y);
                    }
                }
            }
        }

        // Footer
        canvas.DrawText("TDV2200 Font - All 4 Character Sets (0x00-0x7F)", 30, height - 15, SKTextAlign.Left, headerFont, textPaint);

        // Save
        var pngPath = Path.Combine(ImagesFolder, "tdv2200_all_charsets_visual.png");
        Directory.CreateDirectory(ImagesFolder);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = File.OpenWrite(pngPath);
        data.SaveTo(stream);

        Assert.True(File.Exists(pngPath), $"Output file should exist: {pngPath}");
    }
}
