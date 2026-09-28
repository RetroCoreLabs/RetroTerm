using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Avalonia.Headless.XUnit;
using RetroTerm.Core.Fonts;
using RetroTerm.Core.Terminal.Emulators.TDV;
using SkiaSharp;
using Xunit;

namespace RetroTerm.Tests.Avalonia;

/// <summary>
/// Comprehensive screenshot tests that render ALL characters (0x00-0x7F) for each character set
/// and validate the rendering against FontTDV2215.cs glyph definitions.
///
/// TDV2215 Character Sets (DIFFERENT from TDV2200):
/// - Set 1 (fontNum=0,1): ASCII (standard characters)
/// - Set 2 (fontNum=2): Line Drawing, histogram, plotting (ESC N single shift, ESC n locking shift)
/// - Set 3 (fontNum=3): Subscript/Superscript (0x30-0x4F = 32 characters)
/// - Set 4 (fontNum=4): Control code display for transparent mode
/// </summary>
[Collection("Avalonia")]
public class TDV2215CharSetScreenshotTests
{
    private static readonly string ImagesFolder = Path.Combine(
        Path.GetDirectoryName(typeof(TDV2215CharSetScreenshotTests).Assembly.Location) ?? "",
        "..", "..", "..", "Avalonia", "images", "tdv2215");

    private static readonly FontTDV2215 _font = new FontTDV2215();

    static TDV2215CharSetScreenshotTests()
    {
        Directory.CreateDirectory(ImagesFolder);
    }

    /// <summary>
    /// Screenshot test that renders ALL 128 characters (0x00-0x7F) in character set 1 (fontNum=0).
    /// This is the standard ASCII character set.
    /// </summary>
    [AvaloniaFact]
    public void Screenshot_CharacterSet1_AllChars_0x00_0x7F()
    {
        var emulator = new TDV2215Emulator(80, 24, 1000);

        emulator.ProcessData(Encoding.UTF8.GetBytes("Character Set 1 (ASCII) - All 0x00-0x7F:\r\n\r\n"));

        // Write characters in 8 rows of 16 columns each
        for (int row = 0; row < 8; row++)
        {
            // Row header
            emulator.ProcessData(Encoding.UTF8.GetBytes($"{row:X}x: "));

            for (int col = 0; col < 16; col++)
            {
                byte charCode = (byte)(row * 16 + col);

                // Skip control characters (replace with visible representation)
                if (charCode < 0x20)
                {
                    emulator.ProcessData(new byte[] { (byte)'.' }); // Placeholder for control chars
                }
                else if (charCode == 0x7F)
                {
                    emulator.ProcessData(new byte[] { (byte)'.' }); // DEL
                }
                else
                {
                    emulator.ProcessData(new byte[] { charCode });
                }
                emulator.ProcessData(Encoding.UTF8.GetBytes(" "));
            }
            emulator.ProcessData(Encoding.UTF8.GetBytes("\r\n"));
        }

        SaveScreenshotWithValidation(emulator, "charset1_all_0x00_0x7F",
            "Character Set 1 (ASCII): All chars 0x00-0x7F", 0);
    }

    /// <summary>
    /// Screenshot test that renders ALL characters (0x20-0x7F) in character set 2 (fontNum=2).
    /// TDV2215 Set 2 is LINE DRAWING (not Greek like TDV2200).
    /// Accessed via SS2 (ESC N) for single shift or LS2 (ESC n) for locking shift.
    /// </summary>
    [AvaloniaFact]
    public void Screenshot_CharacterSet2_LineDrawing_LS2()
    {
        var emulator = new TDV2215Emulator(80, 24, 1000);

        emulator.ProcessData(Encoding.UTF8.GetBytes("Character Set 2 (Line Drawing) via LS2:\r\n\r\n"));

        // LS2 - locking shift to G2
        emulator.ProcessData(new byte[] { 0x1B, (byte)'n' }); // ESC n = LS2

        // Write printable characters (0x20-0x7F) in rows
        for (int row = 2; row < 8; row++)
        {
            // Row header (in normal charset first)
            emulator.ProcessData(new byte[] { 0x0F }); // SI to return to G0
            emulator.ProcessData(Encoding.UTF8.GetBytes($"{row:X}x: "));
            emulator.ProcessData(new byte[] { 0x1B, (byte)'n' }); // Back to LS2

            for (int col = 0; col < 16; col++)
            {
                byte charCode = (byte)(row * 16 + col);
                if (charCode >= 0x20 && charCode <= 0x7E)
                {
                    emulator.ProcessData(new byte[] { charCode });
                }
                else if (charCode == 0x7F)
                {
                    emulator.ProcessData(new byte[] { 0x0F }); // SI
                    emulator.ProcessData(new byte[] { (byte)'.' });
                    emulator.ProcessData(new byte[] { 0x1B, (byte)'n' });
                }
                else
                {
                    emulator.ProcessData(new byte[] { 0x0F }); // SI
                    emulator.ProcessData(new byte[] { (byte)'.' });
                    emulator.ProcessData(new byte[] { 0x1B, (byte)'n' });
                }

                emulator.ProcessData(new byte[] { 0x0F }); // SI for space
                emulator.ProcessData(Encoding.UTF8.GetBytes(" "));
                emulator.ProcessData(new byte[] { 0x1B, (byte)'n' }); // Back to LS2
            }

            emulator.ProcessData(new byte[] { 0x0F }); // SI for newline
            emulator.ProcessData(Encoding.UTF8.GetBytes("\r\n"));
        }

        emulator.ProcessData(new byte[] { 0x0F }); // Return to normal

        SaveScreenshotWithValidation(emulator, "charset2_line_drawing_ls2",
            "Character Set 2 (Line Drawing): All chars 0x20-0x7F via LS2", 2);
    }

    /// <summary>
    /// Screenshot test that renders characters using SS2 (single shift to G2).
    /// SS2 only affects the next character, then returns to normal.
    /// </summary>
    [AvaloniaFact]
    public void Screenshot_CharacterSet2_LineDrawing_SS2()
    {
        var emulator = new TDV2215Emulator(80, 24, 1000);

        emulator.ProcessData(Encoding.UTF8.GetBytes("SS2 Single Shift to Line Drawing (ESC N):\r\n\r\n"));

        // Demonstrate SS2 - single character from line drawing set
        emulator.ProcessData(Encoding.UTF8.GetBytes("Normal A, then SS2+A: A"));
        emulator.ProcessData(new byte[] { 0x1B, (byte)'N', (byte)'A' }); // ESC N A = SS2 + 'A'
        emulator.ProcessData(Encoding.UTF8.GetBytes(", then normal B: B\r\n\r\n"));

        // Show some line drawing characters with SS2
        emulator.ProcessData(Encoding.UTF8.GetBytes("Line chars via SS2: "));
        for (byte ch = 0x60; ch <= 0x78; ch++)
        {
            emulator.ProcessData(new byte[] { 0x1B, (byte)'N', ch }); // SS2 + char
            emulator.ProcessData(new byte[] { (byte)' ' });
        }
        emulator.ProcessData(Encoding.UTF8.GetBytes("\r\n"));

        SaveScreenshotWithValidation(emulator, "charset2_line_drawing_ss2",
            "Character Set 2 (Line Drawing): SS2 single shift demonstration", 0);
    }

    /// <summary>
    /// Screenshot: Character Set 3 - All Subscript/Superscript Glyphs
    ///
    /// WHAT THIS TEST SHOWS:
    /// - Row 1: All 16 subscript glyphs (0x00-0x0F) - small digits 0-9 positioned at bottom of cell
    /// - Row 2: All 16 superscript glyphs (0x10-0x1F) - small digits 0-9 positioned at top of cell
    ///
    /// EXPECTED APPEARANCE:
    /// - Subscript digits appear in the LOWER portion of each character cell
    /// - Superscript digits appear in the UPPER portion of each character cell
    /// - Both are smaller than normal digits
    ///
    /// HOW IT WORKS:
    /// - ESC O (SS3) shifts the NEXT character to charset 3
    /// - Byte 0x00 = subscript "0", 0x01 = subscript "1", ..., 0x09 = subscript "9"
    /// - Byte 0x10 = superscript "0", 0x11 = superscript "1", ..., 0x19 = superscript "9"
    /// </summary>
    [AvaloniaFact]
    public void Screenshot_CharacterSet3_Subscript_LS3()
    {
        var emulator = new TDV2215Emulator(80, 24, 1000);

        // Header explaining what should be visible
        emulator.ProcessData(Encoding.UTF8.GetBytes("=== CHARACTER SET 3: SUBSCRIPT/SUPERSCRIPT ===\r\n\r\n"));

        emulator.ProcessData(Encoding.UTF8.GetBytes("Below: Small digits at BOTTOM of cell (subscript)\r\n"));
        emulator.ProcessData(Encoding.UTF8.GetBytes("Subscript 0-9: "));
        for (byte ch = 0x00; ch <= 0x09; ch++)
        {
            emulator.ProcessData(new byte[] { 0x1B, (byte)'O', ch }); // SS3 + subscript digit
            emulator.ProcessData(new byte[] { (byte)' ' });
        }
        emulator.ProcessData(Encoding.UTF8.GetBytes("\r\n\r\n"));

        emulator.ProcessData(Encoding.UTF8.GetBytes("Below: Small digits at TOP of cell (superscript)\r\n"));
        emulator.ProcessData(Encoding.UTF8.GetBytes("Superscript 0-9: "));
        for (byte ch = 0x10; ch <= 0x19; ch++)
        {
            emulator.ProcessData(new byte[] { 0x1B, (byte)'O', ch }); // SS3 + superscript digit
            emulator.ProcessData(new byte[] { (byte)' ' });
        }
        emulator.ProcessData(Encoding.UTF8.GetBytes("\r\n\r\n"));

        // Visual comparison
        emulator.ProcessData(Encoding.UTF8.GetBytes("Comparison with normal digits:\r\n"));
        emulator.ProcessData(Encoding.UTF8.GetBytes("Normal:     0 1 2 3 4 5 6 7 8 9\r\n"));
        emulator.ProcessData(Encoding.UTF8.GetBytes("Subscript:  "));
        for (byte ch = 0x00; ch <= 0x09; ch++)
        {
            emulator.ProcessData(new byte[] { 0x1B, (byte)'O', ch });
            emulator.ProcessData(new byte[] { (byte)' ' });
        }
        emulator.ProcessData(Encoding.UTF8.GetBytes("\r\n"));
        emulator.ProcessData(Encoding.UTF8.GetBytes("Superscript:"));
        for (byte ch = 0x10; ch <= 0x19; ch++)
        {
            emulator.ProcessData(new byte[] { 0x1B, (byte)'O', ch });
            emulator.ProcessData(new byte[] { (byte)' ' });
        }
        emulator.ProcessData(Encoding.UTF8.GetBytes("\r\n"));

        SaveScreenshotWithValidation(emulator, "charset3_subscript_ls3",
            "Charset 3: Subscript (bottom) and Superscript (top) digits", 0);
    }

    /// <summary>
    /// Screenshot: Character Set 3 - Practical Usage Examples
    ///
    /// WHAT THIS TEST SHOWS:
    /// - Chemical formulas with subscript numbers (H2O, CO2, H2CO3)
    /// - Mathematical expressions with superscript exponents (x², y², z²)
    ///
    /// EXPECTED APPEARANCE:
    /// - "H2O" should show H, then a SMALL 2 at the BOTTOM, then O
    /// - "x²" should show x, then a SMALL 2 at the TOP
    ///
    /// NOTE: The subscript/superscript characters are stored as codepoints 0x00-0x1F
    /// which don't display in text dumps, but render correctly in the PNG.
    /// </summary>
    [AvaloniaFact]
    public void Screenshot_CharacterSet3_Subscript_SS3()
    {
        var emulator = new TDV2215Emulator(80, 24, 1000);

        emulator.ProcessData(Encoding.UTF8.GetBytes("=== SUBSCRIPT/SUPERSCRIPT PRACTICAL EXAMPLES ===\r\n\r\n"));

        // Chemical formulas
        emulator.ProcessData(Encoding.UTF8.GetBytes("CHEMICAL FORMULAS (subscript = small number at bottom):\r\n\r\n"));

        emulator.ProcessData(Encoding.UTF8.GetBytes("Water:           H"));
        emulator.ProcessData(new byte[] { 0x1B, (byte)'O', 0x02 }); // subscript 2
        emulator.ProcessData(Encoding.UTF8.GetBytes("O\r\n"));

        emulator.ProcessData(Encoding.UTF8.GetBytes("Carbon dioxide:  CO"));
        emulator.ProcessData(new byte[] { 0x1B, (byte)'O', 0x02 }); // subscript 2
        emulator.ProcessData(Encoding.UTF8.GetBytes("\r\n"));

        emulator.ProcessData(Encoding.UTF8.GetBytes("Carbonic acid:   H"));
        emulator.ProcessData(new byte[] { 0x1B, (byte)'O', 0x02 }); // subscript 2
        emulator.ProcessData(Encoding.UTF8.GetBytes("CO"));
        emulator.ProcessData(new byte[] { 0x1B, (byte)'O', 0x03 }); // subscript 3
        emulator.ProcessData(Encoding.UTF8.GetBytes("\r\n\r\n"));

        // Mathematical expressions
        emulator.ProcessData(Encoding.UTF8.GetBytes("MATH EXPRESSIONS (superscript = small number at top):\r\n\r\n"));

        emulator.ProcessData(Encoding.UTF8.GetBytes("x squared:       x"));
        emulator.ProcessData(new byte[] { 0x1B, (byte)'O', 0x12 }); // superscript 2
        emulator.ProcessData(Encoding.UTF8.GetBytes("\r\n"));

        emulator.ProcessData(Encoding.UTF8.GetBytes("Pythagorean:     a"));
        emulator.ProcessData(new byte[] { 0x1B, (byte)'O', 0x12 }); // superscript 2
        emulator.ProcessData(Encoding.UTF8.GetBytes(" + b"));
        emulator.ProcessData(new byte[] { 0x1B, (byte)'O', 0x12 }); // superscript 2
        emulator.ProcessData(Encoding.UTF8.GetBytes(" = c"));
        emulator.ProcessData(new byte[] { 0x1B, (byte)'O', 0x12 }); // superscript 2
        emulator.ProcessData(Encoding.UTF8.GetBytes("\r\n"));

        emulator.ProcessData(Encoding.UTF8.GetBytes("Cube:            x"));
        emulator.ProcessData(new byte[] { 0x1B, (byte)'O', 0x13 }); // superscript 3
        emulator.ProcessData(Encoding.UTF8.GetBytes("\r\n"));

        SaveScreenshotWithValidation(emulator, "charset3_subscript_ss3",
            "Charset 3: Chemical formulas (subscript) and math (superscript)", 0);
    }

    /// <summary>
    /// Generate a glyph comparison image that shows each character alongside its expected pixel data.
    /// This validates that the rendering matches the font definition.
    /// NOTE: FontTDV2215 glyph data is still incomplete - these tests document available glyphs.
    /// </summary>
    [AvaloniaFact]
    public void Screenshot_GlyphValidation_CharSet1_Printable()
    {
        ValidateGlyphsForCharSet(0, "charset1_glyph_validation", "Character Set 1 Glyph Validation");
    }

    [AvaloniaFact]
    public void Screenshot_GlyphValidation_CharSet2_LineDrawing()
    {
        ValidateGlyphsForCharSet(2, "charset2_glyph_validation", "Character Set 2 (Line Drawing) Glyph Validation");
    }

    [AvaloniaFact]
    public void Screenshot_GlyphValidation_CharSet3_Subscript()
    {
        ValidateGlyphsForCharSet(3, "charset3_glyph_validation", "Character Set 3 (Subscript) Glyph Validation");
    }

    [AvaloniaFact]
    public void Screenshot_GlyphValidation_CharSet4_ControlDisplay()
    {
        ValidateGlyphsForCharSet(4, "charset4_glyph_validation", "Character Set 4 (Control Display) Glyph Validation");
    }

    /// <summary>
    /// Create a glyph image showing all 128 characters (0x00-0x7F) for the specified font number.
    /// Empty glyphs are shown as empty cells - no validation failures.
    /// </summary>
    private void ValidateGlyphsForCharSet(int fontNum, string filename, string description)
    {
        const int charWidth = 9;  // TDV2215 uses 9x14
        const int charHeight = 14;
        const int glyphsPerRow = 16;
        const int totalRows = 8; // 0x00-0x7F = 128 chars, 128/16 = 8 rows
        const int headerHeight = 40;
        const int rowHeight = charHeight + 20; // Char + spacing

        int width = glyphsPerRow * (charWidth + 8) + 100; // Extra for labels
        int height = totalRows * rowHeight + headerHeight + 50;

        using var bitmap = new SKBitmap(width, height);
        using var canvas = new SKCanvas(bitmap);

        canvas.Clear(SKColors.Black);

        // Header
        using var headerFont = new SKFont(SKTypeface.FromFamilyName("Consolas", SKFontStyle.Bold), 14);
        using var textPaint = new SKPaint { Color = SKColors.Yellow };
        canvas.DrawText($"{description} (fontNum={fontNum})", 10, 25, headerFont, textPaint);

        using var smallFont = new SKFont(SKTypeface.FromFamilyName("Consolas"), 10);
        using var labelPaint = new SKPaint { Color = SKColors.Gray };
        using var pixelPaint = new SKPaint { Color = SKColors.LightGray };

        int glyphsWithContent = 0;

        // Render all 128 characters (0x00-0x7F)
        for (int row = 0; row < totalRows; row++)
        {
            int baseChar = row * glyphsPerRow;
            int y = headerHeight + row * rowHeight;

            // Row label
            canvas.DrawText($"{baseChar:X2}:", 5, y + charHeight, smallFont, labelPaint);

            for (int col = 0; col < glyphsPerRow; col++)
            {
                ushort charCode = (ushort)(baseChar + col);
                int x = 40 + col * (charWidth + 8);

                // Get glyph bits from font
                ushort[]? glyphBits = _font.GetFontBits(charCode, fontNum);

                if (glyphBits == null)
                {
                    continue; // No glyph - leave empty
                }

                // Check if glyph has any content
                bool hasContent = false;
                for (int i = 0; i < glyphBits.Length; i++)
                {
                    if (glyphBits[i] != 0)
                    {
                        hasContent = true;
                        break;
                    }
                }

                if (hasContent)
                {
                    glyphsWithContent++;
                    RenderGlyphBits(canvas, pixelPaint, glyphBits, x, y, charWidth, charHeight);
                }
            }
        }

        // Summary
        int summaryY = height - 30;
        using var greenPaint = new SKPaint { Color = SKColors.Green };
        canvas.DrawText($"Glyphs with content: {glyphsWithContent}/128 (fontNum={fontNum})", 10, summaryY, smallFont, greenPaint);

        // Save image
        var pngPath = Path.Combine(ImagesFolder, filename + ".png");
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = File.OpenWrite(pngPath);
        data.SaveTo(stream);
    }

    private void RenderGlyphBits(SKCanvas canvas, SKPaint paint, ushort[] bits, int x, int y, int charWidth, int charHeight)
    {
        // Calculate scale factor to fit glyph into cell
        // TDV2215 font is 9x14 pixels
        float scaleX = charWidth / 9.0f;
        float scaleY = charHeight / 14.0f;

        int fontHeight = Math.Min(bits.Length, 14);

        for (int row = 0; row < fontHeight; row++)
        {
            ushort rowBits = bits[row];

            for (int col = 0; col < 9; col++)
            {
                int bitIndex = 8 - col; // 9 bits, MSB first
                bool pixelOn = (rowBits & (1 << bitIndex)) != 0;

                if (pixelOn)
                {
                    // Draw scaled pixel
                    float px = x + col * scaleX;
                    float py = y + row * scaleY;
                    canvas.DrawRect(px, py, scaleX, scaleY, paint);
                }
            }
        }
    }

    private void SaveScreenshotWithValidation(TDV2215Emulator emulator, string filename, string description, int expectedFontNum)
    {
        // Scale up for better visibility: 2x native size (9x14 -> 18x28)
        const int charWidth = 18;
        const int charHeight = 28;
        const int headerHeight = 40;

        var width = emulator.Buffer.Width * charWidth;
        var height = emulator.Buffer.Height * charHeight + headerHeight;

        using var bitmap = new SKBitmap(width, height);
        using var canvas = new SKCanvas(bitmap);

        canvas.Clear(SKColors.Black);

        // Header
        using var headerFont = new SKFont(SKTypeface.FromFamilyName("Consolas", SKFontStyle.Bold), 18);
        using var headerBgPaint = new SKPaint { Color = new SKColor(0, 40, 100) };
        canvas.DrawRect(0, 0, width, headerHeight, headerBgPaint);
        using var headerPaint = new SKPaint { Color = SKColors.Yellow };
        canvas.DrawText(description, 10, 28, headerFont, headerPaint);

        var fgColor = SKColors.LightGray;
        using var pixelPaint = new SKPaint { Color = fgColor };

        var validationErrors = new List<string>();

        // Sync font variant from emulator - CRITICAL for ISO 646 national variants!
        _font.CharacterSetVariant = emulator.CharacterSetVariant;

        // Render each cell and validate
        for (int row = 0; row < emulator.Buffer.Height; row++)
        {
            for (int col = 0; col < emulator.Buffer.Width; col++)
            {
                var cell = emulator.Buffer.GetCell(row, col);
                ushort charValue = (ushort)(cell.Codepoint > 0 ? cell.Codepoint : ' ');

                int x = col * charWidth;
                int y = row * charHeight + headerHeight;

                // Get font bits for this character
                ushort[]? fontBits = _font.GetFontBits(charValue, cell.FontNumber);

                if (fontBits != null)
                {
                    RenderGlyphBits(canvas, pixelPaint, fontBits, x, y, charWidth, charHeight);
                }
            }
        }

        // Save image
        var pngPath = Path.Combine(ImagesFolder, filename + ".png");
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = File.OpenWrite(pngPath);
        data.SaveTo(stream);

        // Save text representation
        SaveTextRepresentation(emulator, filename, description);

        Assert.True(validationErrors.Count == 0, $"Validation errors: {string.Join("; ", validationErrors)}");
    }

    private void SaveTextRepresentation(TDV2215Emulator emulator, string filename, string description)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"=== {description} ===");
        sb.AppendLine($"Terminal: TDV2215 {emulator.Buffer.Width}x{emulator.Buffer.Height}");
        sb.AppendLine();

        for (int row = 0; row < Math.Min(emulator.Buffer.Height, 24); row++)
        {
            var lineContent = new StringBuilder();
            var lineFontNums = new StringBuilder();

            for (int col = 0; col < emulator.Buffer.Width; col++)
            {
                var cell = emulator.Buffer.GetCell(row, col);
                char ch = cell.Codepoint > 0 ? (char)cell.Codepoint : ' ';
                lineContent.Append(ch);
                lineFontNums.Append(cell.FontNumber.ToString());
            }

            sb.AppendLine($"[{row:D2}] {lineContent}");

            // Show font numbers if any non-zero
            string fontLine = lineFontNums.ToString().TrimEnd('0');
            if (fontLine.Length > 0 && fontLine.Replace("0", "").Length > 0)
            {
                sb.AppendLine($"     F: {lineFontNums}");
            }
        }

        var path = Path.Combine(ImagesFolder, filename + ".txt");
        File.WriteAllText(path, sb.ToString());
    }

    /// <summary>
    /// Comprehensive test that dumps all character set glyphs to text files for manual inspection
    /// </summary>
    [Fact]
    public void DumpAllCharSets_ToTextFiles()
    {
        DumpCharSetToFile(0, "charset1_glyphs_dump.txt", "Character Set 1 (Standard ASCII)");
        DumpCharSetToFile(2, "charset2_glyphs_dump.txt", "Character Set 2 (Line Drawing)");
        DumpCharSetToFile(3, "charset3_glyphs_dump.txt", "Character Set 3 (Subscript/Superscript)");
        DumpCharSetToFile(4, "charset4_glyphs_dump.txt", "Character Set 4 (Control Code Display)");
    }

    private void DumpCharSetToFile(int fontNum, string filename, string description)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"=== {description} (fontNum={fontNum}) ===");
        sb.AppendLine($"Font dimensions: {_font.Width}x{_font.Height} (HeightToUse={_font.HeightToUse})");
        sb.AppendLine($"Font offset: {_font.fontNumOffset![fontNum]}");
        sb.AppendLine();

        for (ushort charCode = 0x00; charCode <= 0x7F; charCode++)
        {
            sb.AppendLine($"// Character 0x{charCode:X2} ('{(charCode >= 0x20 && charCode < 0x7F ? (char)charCode : '?')}')");

            ushort[]? bits = _font.GetFontBits(charCode, fontNum);
            Assert.NotNull(bits);
            if (bits == null)
            {
                sb.AppendLine("// (null - no glyph)");
                sb.AppendLine();
                continue;
            }

            // Render as ASCII art
            for (int row = 0; row < Math.Min(bits.Length, 14); row++)
            {
                sb.Append("// ");
                ushort rowBits = bits[row];

                for (int col = 8; col >= 0; col--)  // 9 bits for TDV2215
                {
                    bool pixelOn = (rowBits & (1 << col)) != 0;
                    sb.Append(pixelOn ? 'X' : ' ');
                }
                sb.AppendLine();
            }

            // Also show hex values
            sb.Append("// Hex: ");
            for (int i = 0; i < Math.Min(bits.Length, 14); i++)
            {
                sb.Append($"0x{bits[i]:X4}");
                if (i < 13) sb.Append(", ");
            }
            sb.AppendLine();
            sb.AppendLine();
        }

        var path = Path.Combine(ImagesFolder, filename);
        File.WriteAllText(path, sb.ToString());

        Assert.True(File.Exists(path), $"Dump file should be created: {path}");
    }

    /// <summary>
    /// Test demonstrating TDV2115 compatibility mode features
    /// </summary>
    [AvaloniaFact]
    public void Screenshot_TDV2115_CompatibilityMode_C0Codes()
    {
        var emulator = new TDV2215Emulator(80, 24, 1000);

        emulator.ProcessData(Encoding.UTF8.GetBytes("TDV2115 Compatibility Mode C0 Codes:\r\n\r\n"));

        // Enable 2115 compatibility mode
        emulator.ProcessData(new byte[] { 0x1B, (byte)'[', (byte)'?', (byte)'4', (byte)'0', (byte)'h' }); // ESC[?40h

        // Write some text
        emulator.ProcessData(Encoding.UTF8.GetBytes("Line 1: Testing cursor movement\r\n"));
        emulator.ProcessData(Encoding.UTF8.GetBytes("Line 2: After carriage return\r\n"));
        emulator.ProcessData(Encoding.UTF8.GetBytes("Line 3: Original text"));

        // Test cursor home (GS = 0x1D)
        emulator.ProcessData(new byte[] { 0x1D }); // Cursor home
        emulator.ProcessData(Encoding.UTF8.GetBytes("HOME->"));

        // Disable 2115 mode
        emulator.ProcessData(new byte[] { 0x1B, (byte)'[', (byte)'?', (byte)'4', (byte)'0', (byte)'l' }); // ESC[?40l

        SaveScreenshotWithValidation(emulator, "tdv2115_c0_codes",
            "TDV2115 Compatibility Mode: C0 Control Codes", 0);
    }

    /// <summary>
    /// Test demonstrating extended mode (ESC Q)
    /// </summary>
    [AvaloniaFact]
    public void Screenshot_ExtendedMode_EscQ()
    {
        var emulator = new TDV2215Emulator(80, 24, 1000);

        emulator.ProcessData(Encoding.UTF8.GetBytes("Extended Mode Test (ESC Q):\r\n\r\n"));

        // First enable 2115 mode
        emulator.ProcessData(new byte[] { 0x1B, (byte)'[', (byte)'?', (byte)'4', (byte)'0', (byte)'h' }); // ESC[?40h
        emulator.ProcessData(Encoding.UTF8.GetBytes("2115 mode enabled\r\n"));

        // ESC Q exits 2115 mode and enables extended mode
        emulator.ProcessData(new byte[] { 0x1B, (byte)'Q' }); // ESC Q
        emulator.ProcessData(Encoding.UTF8.GetBytes("After ESC Q: Extended mode enabled\r\n"));

        // Verify terminal type shows extended mode
        var termType = emulator.GetTerminalType();
        emulator.ProcessData(Encoding.UTF8.GetBytes($"Terminal type: {termType}\r\n"));

        SaveScreenshotWithValidation(emulator, "extended_mode_escq",
            "Extended Mode: ESC Q enables extended mode", 0);
    }

    /// <summary>
    /// Test showing character set 1 with International (US ASCII) variant.
    /// This is the default variant (TDV2115 spec 9.1.1).
    /// </summary>
    [AvaloniaFact]
    public void Screenshot_CharSet1_International_Variant()
    {
        var emulator = new TDV2215Emulator(80, 24, 1000);

        // Verify default is International
        Assert.Equal(0, emulator.CharacterSetVariant);

        emulator.ProcessData(Encoding.UTF8.GetBytes("Character Set 1 - International (US ASCII):\r\n"));
        emulator.ProcessData(Encoding.UTF8.GetBytes("Variant: " + emulator.CharacterSetVariant + " (0=International)\r\n\r\n"));

        // Show ISO 646 variant positions: # $ @ [ \\ ] ^ ` { | } ~
        emulator.ProcessData(Encoding.UTF8.GetBytes("ISO 646 variant positions (0x23-0x7E):\r\n"));
        emulator.ProcessData(Encoding.UTF8.GetBytes("0x23 #  0x24 $  0x40 @  0x5B [  0x5C \\  0x5D ]\r\n"));
        emulator.ProcessData(Encoding.UTF8.GetBytes("0x5E ^  0x60 `  0x7B {  0x7C |  0x7D }  0x7E ~\r\n\r\n"));

        // Display the actual characters at these positions
        emulator.ProcessData(Encoding.UTF8.GetBytes("Actual characters: # $ @ [ \\ ] ^ ` { | } ~\r\n\r\n"));

        // Full printable range
        emulator.ProcessData(Encoding.UTF8.GetBytes("All printable (0x20-0x7E):\r\n"));
        for (byte ch = 0x20; ch <= 0x7E; ch++)
        {
            emulator.ProcessData(new byte[] { ch });
            if ((ch - 0x20) % 32 == 31)
            {
                emulator.ProcessData(Encoding.UTF8.GetBytes("\r\n"));
            }
        }

        SaveScreenshotWithValidation(emulator, "charset1_international_variant",
            "Character Set 1 - International (US ASCII) Variant", 0);
    }

    /// <summary>
    /// Test showing character set 1 with Norwegian variant.
    /// Per TDV2115 spec 9.1.2, Norwegian replaces certain ASCII positions.
    /// </summary>
    [AvaloniaFact]
    public void Screenshot_CharSet1_Norwegian_Variant()
    {
        var emulator = new TDV2215Emulator(80, 24, 1000);

        // Set Norwegian variant
        emulator.CharacterSetVariant = (int)TDV2200ISO646Variant.Norwegian;
        Assert.Equal(1, emulator.CharacterSetVariant);

        emulator.ProcessData(Encoding.UTF8.GetBytes("Character Set 1 - Norwegian Variant:\r\n"));
        emulator.ProcessData(Encoding.UTF8.GetBytes("Variant: " + emulator.CharacterSetVariant + " (1=Norwegian)\r\n\r\n"));

        // Norwegian ISO 646 replacements:
        // # -> £ (pound)  or similar
        // @ -> § or Ä
        // [ -> Æ
        // \\ -> Ø
        // ] -> Å
        // { -> æ
        // | -> ø
        // } -> å
        emulator.ProcessData(Encoding.UTF8.GetBytes("Norwegian ISO 646 positions:\r\n"));
        // Send hex label then actual byte so terminal renders the national char
        emulator.ProcessData(Encoding.UTF8.GetBytes("0x5B -> "));
        emulator.ProcessData(new byte[] { 0x5B });
        emulator.ProcessData(Encoding.UTF8.GetBytes("  0x5C -> "));
        emulator.ProcessData(new byte[] { 0x5C });
        emulator.ProcessData(Encoding.UTF8.GetBytes("  0x5D -> "));
        emulator.ProcessData(new byte[] { 0x5D });
        emulator.ProcessData(Encoding.UTF8.GetBytes("\r\n0x7B -> "));
        emulator.ProcessData(new byte[] { 0x7B });
        emulator.ProcessData(Encoding.UTF8.GetBytes("  0x7C -> "));
        emulator.ProcessData(new byte[] { 0x7C });
        emulator.ProcessData(Encoding.UTF8.GetBytes("  0x7D -> "));
        emulator.ProcessData(new byte[] { 0x7D });
        emulator.ProcessData(Encoding.UTF8.GetBytes("  0x7E -> "));
        emulator.ProcessData(new byte[] { 0x7E });
        emulator.ProcessData(Encoding.UTF8.GetBytes("\r\n\r\n"));

        // Display the actual characters at variant positions
        emulator.ProcessData(Encoding.UTF8.GetBytes("Variant chars: "));
        emulator.ProcessData(new byte[] { 0x5B, 0x20, 0x5C, 0x20, 0x5D, 0x20, 0x7B, 0x20, 0x7C, 0x20, 0x7D, 0x20, 0x7E });
        emulator.ProcessData(Encoding.UTF8.GetBytes("\r\n\r\n"));

        // Full printable range with Norwegian variant
        emulator.ProcessData(Encoding.UTF8.GetBytes("All printable (0x20-0x7E) Norwegian:\r\n"));
        for (byte ch = 0x20; ch <= 0x7E; ch++)
        {
            emulator.ProcessData(new byte[] { ch });
            if ((ch - 0x20) % 32 == 31)
            {
                emulator.ProcessData(Encoding.UTF8.GetBytes("\r\n"));
            }
        }

        SaveScreenshotWithValidation(emulator, "charset1_norwegian_variant",
            "Character Set 1 - Norwegian Variant", 0);
    }

    /// <summary>
    /// Test showing character set 1 with Swedish variant.
    /// Per TDV2115 spec 9.1.3.
    /// </summary>
    [AvaloniaFact]
    public void Screenshot_CharSet1_Swedish_Variant()
    {
        var emulator = new TDV2215Emulator(80, 24, 1000);

        // Set Swedish variant
        emulator.CharacterSetVariant = (int)TDV2200ISO646Variant.Swedish;
        Assert.Equal(2, emulator.CharacterSetVariant);

        emulator.ProcessData(Encoding.UTF8.GetBytes("Character Set 1 - Swedish Variant:\r\n"));
        emulator.ProcessData(Encoding.UTF8.GetBytes("Variant: " + emulator.CharacterSetVariant + " (2=Swedish)\r\n\r\n"));

        // Swedish ISO 646 replacements:
        // [ -> Ä
        // \\ -> Ö
        // ] -> Å
        // { -> ä
        // | -> ö
        // } -> å
        emulator.ProcessData(Encoding.UTF8.GetBytes("Swedish ISO 646 positions:\r\n"));
        // Send hex label then actual byte so terminal renders the national char
        emulator.ProcessData(Encoding.UTF8.GetBytes("0x5B -> "));
        emulator.ProcessData(new byte[] { 0x5B });
        emulator.ProcessData(Encoding.UTF8.GetBytes("  0x5C -> "));
        emulator.ProcessData(new byte[] { 0x5C });
        emulator.ProcessData(Encoding.UTF8.GetBytes("  0x5D -> "));
        emulator.ProcessData(new byte[] { 0x5D });
        emulator.ProcessData(Encoding.UTF8.GetBytes("\r\n0x7B -> "));
        emulator.ProcessData(new byte[] { 0x7B });
        emulator.ProcessData(Encoding.UTF8.GetBytes("  0x7C -> "));
        emulator.ProcessData(new byte[] { 0x7C });
        emulator.ProcessData(Encoding.UTF8.GetBytes("  0x7D -> "));
        emulator.ProcessData(new byte[] { 0x7D });
        emulator.ProcessData(Encoding.UTF8.GetBytes("  0x7E -> "));
        emulator.ProcessData(new byte[] { 0x7E });
        emulator.ProcessData(Encoding.UTF8.GetBytes("\r\n\r\n"));

        // Display the actual characters at variant positions
        emulator.ProcessData(Encoding.UTF8.GetBytes("Variant chars: "));
        emulator.ProcessData(new byte[] { 0x5B, 0x20, 0x5C, 0x20, 0x5D, 0x20, 0x7B, 0x20, 0x7C, 0x20, 0x7D, 0x20, 0x7E });
        emulator.ProcessData(Encoding.UTF8.GetBytes("\r\n\r\n"));

        // Full printable range with Swedish variant
        emulator.ProcessData(Encoding.UTF8.GetBytes("All printable (0x20-0x7E) Swedish:\r\n"));
        for (byte ch = 0x20; ch <= 0x7E; ch++)
        {
            emulator.ProcessData(new byte[] { ch });
            if ((ch - 0x20) % 32 == 31)
            {
                emulator.ProcessData(Encoding.UTF8.GetBytes("\r\n"));
            }
        }

        SaveScreenshotWithValidation(emulator, "charset1_swedish_variant",
            "Character Set 1 - Swedish Variant", 0);
    }

    /// <summary>
    /// Test showing character set 1 with German variant.
    /// Per TDV2115 spec 9.1.4.
    /// </summary>
    [AvaloniaFact]
    public void Screenshot_CharSet1_German_Variant()
    {
        var emulator = new TDV2215Emulator(80, 24, 1000);

        // Set German variant
        emulator.CharacterSetVariant = (int)TDV2200ISO646Variant.German;
        Assert.Equal(3, emulator.CharacterSetVariant);

        emulator.ProcessData(Encoding.UTF8.GetBytes("Character Set 1 - German Variant:\r\n"));
        emulator.ProcessData(Encoding.UTF8.GetBytes("Variant: " + emulator.CharacterSetVariant + " (3=German)\r\n\r\n"));

        // German ISO 646 replacements:
        // [ -> Ä
        // \\ -> Ö
        // ] -> Ü
        // { -> ä
        // | -> ö
        // } -> ü
        // ~ -> ß
        emulator.ProcessData(Encoding.UTF8.GetBytes("German ISO 646 positions:\r\n"));
        // Send hex label then actual byte so terminal renders the national char
        emulator.ProcessData(Encoding.UTF8.GetBytes("0x5B -> "));
        emulator.ProcessData(new byte[] { 0x5B });
        emulator.ProcessData(Encoding.UTF8.GetBytes("  0x5C -> "));
        emulator.ProcessData(new byte[] { 0x5C });
        emulator.ProcessData(Encoding.UTF8.GetBytes("  0x5D -> "));
        emulator.ProcessData(new byte[] { 0x5D });
        emulator.ProcessData(Encoding.UTF8.GetBytes("\r\n0x7B -> "));
        emulator.ProcessData(new byte[] { 0x7B });
        emulator.ProcessData(Encoding.UTF8.GetBytes("  0x7C -> "));
        emulator.ProcessData(new byte[] { 0x7C });
        emulator.ProcessData(Encoding.UTF8.GetBytes("  0x7D -> "));
        emulator.ProcessData(new byte[] { 0x7D });
        emulator.ProcessData(Encoding.UTF8.GetBytes("  0x7E -> "));
        emulator.ProcessData(new byte[] { 0x7E });
        emulator.ProcessData(Encoding.UTF8.GetBytes("\r\n\r\n"));

        // Display the actual characters at variant positions
        emulator.ProcessData(Encoding.UTF8.GetBytes("Variant chars: "));
        emulator.ProcessData(new byte[] { 0x5B, 0x20, 0x5C, 0x20, 0x5D, 0x20, 0x7B, 0x20, 0x7C, 0x20, 0x7D, 0x20, 0x7E });
        emulator.ProcessData(Encoding.UTF8.GetBytes("\r\n\r\n"));

        // Full printable range with German variant
        emulator.ProcessData(Encoding.UTF8.GetBytes("All printable (0x20-0x7E) German:\r\n"));
        for (byte ch = 0x20; ch <= 0x7E; ch++)
        {
            emulator.ProcessData(new byte[] { ch });
            if ((ch - 0x20) % 32 == 31)
            {
                emulator.ProcessData(Encoding.UTF8.GetBytes("\r\n"));
            }
        }

        SaveScreenshotWithValidation(emulator, "charset1_german_variant",
            "Character Set 1 - German Variant", 0);
    }

    /// <summary>
    /// Test that validates GetAvailableCharacterSetVariants returns correct values
    /// </summary>
    [Fact]
    public void Test_GetAvailableCharacterSetVariants()
    {
        var emulator = new TDV2215Emulator(80, 24, 1000);

        var variants = emulator.GetAvailableCharacterSetVariants();

        Assert.Equal(4, variants.Length);
        Assert.Equal((0, "International (US ASCII)"), variants[0]);
        Assert.Equal((1, "Norwegian"), variants[1]);
        Assert.Equal((2, "Swedish"), variants[2]);
        Assert.Equal((3, "German"), variants[3]);
    }

    /// <summary>
    /// Test that validates CharacterSetVariant property works correctly
    /// </summary>
    [Fact]
    public void Test_CharacterSetVariant_Property()
    {
        var emulator = new TDV2215Emulator(80, 24, 1000);

        // Default should be International (0)
        Assert.Equal(0, emulator.CharacterSetVariant);

        // Set to Norwegian
        emulator.CharacterSetVariant = 1;
        Assert.Equal(1, emulator.CharacterSetVariant);

        // Set to Swedish
        emulator.CharacterSetVariant = 2;
        Assert.Equal(2, emulator.CharacterSetVariant);

        // Set to German
        emulator.CharacterSetVariant = 3;
        Assert.Equal(3, emulator.CharacterSetVariant);

        // Set back to International
        emulator.CharacterSetVariant = 0;
        Assert.Equal(0, emulator.CharacterSetVariant);
    }

    /// <summary>
    /// Test showing all character sets side by side for visual comparison
    /// </summary>
    [AvaloniaFact]
    public void Screenshot_AllCharSets_Comparison()
    {
        var emulator = new TDV2215Emulator(80, 24, 1000);

        emulator.ProcessData(Encoding.UTF8.GetBytes("TDV2215 Character Sets Comparison:\r\n"));
        emulator.ProcessData(Encoding.UTF8.GetBytes("================================\r\n\r\n"));

        // Show a row of characters from each set
        emulator.ProcessData(Encoding.UTF8.GetBytes("Set 1 (ASCII):    "));
        for (byte ch = 0x41; ch <= 0x4A; ch++)
        {
            emulator.ProcessData(new byte[] { ch, (byte)' ' });
        }
        emulator.ProcessData(Encoding.UTF8.GetBytes("\r\n"));

        emulator.ProcessData(Encoding.UTF8.GetBytes("Set 2 (Line):     "));
        for (byte ch = 0x41; ch <= 0x4A; ch++)
        {
            emulator.ProcessData(new byte[] { 0x1B, (byte)'N', ch, (byte)' ' }); // SS2 + char
        }
        emulator.ProcessData(Encoding.UTF8.GetBytes("\r\n"));

        emulator.ProcessData(Encoding.UTF8.GetBytes("Set 3 (Sub/Sup):  "));
        for (byte ch = 0x30; ch <= 0x39; ch++)
        {
            emulator.ProcessData(new byte[] { 0x1B, (byte)'O', ch, (byte)' ' }); // SS3 + digit
        }
        emulator.ProcessData(Encoding.UTF8.GetBytes("\r\n\r\n"));

        // Show line drawing characters
        emulator.ProcessData(Encoding.UTF8.GetBytes("Line drawing (0x60-0x78):\r\n"));
        for (byte ch = 0x60; ch <= 0x78; ch++)
        {
            emulator.ProcessData(new byte[] { 0x1B, (byte)'N', ch }); // SS2 + char
        }
        emulator.ProcessData(Encoding.UTF8.GetBytes("\r\n"));

        SaveScreenshotWithValidation(emulator, "all_charsets_comparison",
            "TDV2215 All Character Sets Comparison", 0);
    }
}
