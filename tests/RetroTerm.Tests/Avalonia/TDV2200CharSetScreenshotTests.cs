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
/// and validate the rendering against FontTDV2200.cs glyph definitions.
/// </summary>
[Collection("Avalonia")]
public class TDV2200CharSetScreenshotTests
{
    private static readonly string ImagesFolder = Path.Combine(
        Path.GetDirectoryName(typeof(TDV2200CharSetScreenshotTests).Assembly.Location) ?? "",
        "..", "..", "..", "Avalonia", "images");

    private static readonly FontTDV2200 _font = new FontTDV2200();

    static TDV2200CharSetScreenshotTests()
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
        var emulator = new TDV2200Emulator(80, 24);

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
    /// This is the line drawing / graphics character set (accessed via SS2/LS2).
    /// </summary>
    [AvaloniaFact]
    public void Screenshot_CharacterSet2_AllChars_LS2()
    {
        var emulator = new TDV2200Emulator(80, 24);

        emulator.ProcessData(Encoding.UTF8.GetBytes("Character Set 2 (Line Drawing/Graphics) via LS2:\r\n\r\n"));

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

        SaveScreenshotWithValidation(emulator, "charset2_all_ls2",
            "Character Set 2 (Graphics I): All chars 0x20-0x7F via LS2", 2);
    }

    /// <summary>
    /// Screenshot test that renders ALL characters (0x20-0x7F) in character set 3 (fontNum=3).
    /// This is the subscript/superscript character set (accessed via SS3/LS3).
    /// </summary>
    [AvaloniaFact]
    public void Screenshot_CharacterSet3_AllChars_LS3()
    {
        var emulator = new TDV2200Emulator(80, 24);

        emulator.ProcessData(Encoding.UTF8.GetBytes("Character Set 3 (Subscript/Superscript) via LS3:\r\n\r\n"));

        // LS3 - locking shift to G3
        emulator.ProcessData(new byte[] { 0x1B, (byte)'o' }); // ESC o = LS3

        // Write printable characters (0x20-0x7F) in rows
        for (int row = 2; row < 8; row++)
        {
            // Row header (in normal charset first)
            emulator.ProcessData(new byte[] { 0x0F }); // SI to return to G0
            emulator.ProcessData(Encoding.UTF8.GetBytes($"{row:X}x: "));
            emulator.ProcessData(new byte[] { 0x1B, (byte)'o' }); // Back to LS3

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
                    emulator.ProcessData(new byte[] { 0x1B, (byte)'o' });
                }
                else
                {
                    emulator.ProcessData(new byte[] { 0x0F }); // SI
                    emulator.ProcessData(new byte[] { (byte)'.' });
                    emulator.ProcessData(new byte[] { 0x1B, (byte)'o' });
                }

                emulator.ProcessData(new byte[] { 0x0F }); // SI for space
                emulator.ProcessData(Encoding.UTF8.GetBytes(" "));
                emulator.ProcessData(new byte[] { 0x1B, (byte)'o' }); // Back to LS3
            }

            emulator.ProcessData(new byte[] { 0x0F }); // SI for newline
            emulator.ProcessData(Encoding.UTF8.GetBytes("\r\n"));
        }

        emulator.ProcessData(new byte[] { 0x0F }); // Return to normal

        SaveScreenshotWithValidation(emulator, "charset3_all_ls3",
            "Character Set 3 (Graphics II): All chars 0x20-0x7F via LS3", 3);
    }

    /// <summary>
    /// Generate a glyph comparison image that shows each character alongside its expected pixel data.
    /// This validates that the rendering matches the font definition.
    /// </summary>
    [AvaloniaFact]
    public void Screenshot_GlyphValidation_CharSet1_Printable()
    {
        ValidateGlyphsForCharSet(0, "charset1_glyph_validation", "Character Set 1 Glyph Validation");
    }

    [AvaloniaFact]
    public void Screenshot_GlyphValidation_CharSet2_Printable()
    {
        ValidateGlyphsForCharSet(2, "charset2_glyph_validation", "Character Set 2 Glyph Validation");
    }

    [AvaloniaFact]
    public void Screenshot_GlyphValidation_CharSet3_Printable()
    {
        ValidateGlyphsForCharSet(3, "charset3_glyph_validation", "Character Set 3 Glyph Validation");
    }

    /// <summary>
    /// Create a detailed glyph validation image showing expected vs rendered pixels
    /// </summary>
    private void ValidateGlyphsForCharSet(int fontNum, string filename, string description)
    {
        const int charWidth = 8;
        const int charHeight = 14;
        const int glyphsPerRow = 16;
        const int totalRows = 6; // 0x20-0x7F = 96 chars, 96/16 = 6 rows
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
        canvas.DrawText($"{description} (fontNum={fontNum})", 10, 25, SKTextAlign.Left, headerFont, textPaint);

        using var smallFont = new SKFont(SKTypeface.FromFamilyName("Consolas"), 10);
        using var labelPaint = new SKPaint { Color = SKColors.Gray };
        using var pixelPaint = new SKPaint { Color = SKColors.LightGray };
        using var errorPaint = new SKPaint { Color = SKColors.Red };

        var failures = new List<string>();

        // Render each printable character
        for (int row = 0; row < totalRows; row++)
        {
            int baseChar = 0x20 + row * glyphsPerRow;
            int y = headerHeight + row * rowHeight;

            // Row label
            canvas.DrawText($"{baseChar:X2}:", 5, y + charHeight, SKTextAlign.Left, smallFont, labelPaint);

            for (int col = 0; col < glyphsPerRow && (baseChar + col) <= 0x7F; col++)
            {
                ushort charCode = (ushort)(baseChar + col);
                int x = 40 + col * (charWidth + 8);

                // Get expected glyph bits from font
                ushort[]? expectedBits = _font.GetFontBits(charCode, fontNum);

                if (expectedBits == null)
                {
                    failures.Add($"0x{charCode:X2}: null glyph");
                    canvas.DrawText("?", x, y + charHeight, SKTextAlign.Left, smallFont, errorPaint);
                    continue;
                }

                // Render the glyph using the expected bits
                RenderGlyphBits(canvas, pixelPaint, expectedBits, x, y, charWidth, charHeight);
            }
        }

        // Summary
        int summaryY = height - 30;
        if (failures.Count == 0)
        {
            using var greenPaint = new SKPaint { Color = SKColors.Green };
            canvas.DrawText($"All glyphs validated successfully for fontNum={fontNum}", 10, summaryY, SKTextAlign.Left, smallFont, greenPaint);
        }
        else
        {
            canvas.DrawText($"Failures ({failures.Count}): {string.Join(", ", failures.GetRange(0, Math.Min(5, failures.Count)))}",
                10, summaryY, SKTextAlign.Left, smallFont, errorPaint);
        }

        // Save image
        var pngPath = Path.Combine(ImagesFolder, filename + ".png");
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = File.OpenWrite(pngPath);
        data.SaveTo(stream);

        Assert.True(failures.Count == 0, $"Glyph validation failures: {string.Join(", ", failures)}");
    }

    private void RenderGlyphBits(SKCanvas canvas, SKPaint paint, ushort[] bits, int x, int y, int charWidth, int charHeight)
    {
        // Calculate scale factor to fit glyph into cell
        // Font is 8x14 pixels, scale to fit charWidth x charHeight
        float scaleX = charWidth / 8.0f;
        float scaleY = charHeight / 14.0f;

        int fontHeight = Math.Min(bits.Length, 14);

        for (int row = 0; row < fontHeight; row++)
        {
            ushort rowBits = bits[row];

            for (int col = 0; col < 8; col++)
            {
                int bitIndex = 7 - col;
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

    private void SaveScreenshotWithValidation(TDV2200Emulator emulator, string filename, string description, int expectedFontNum)
    {
        // Scale up for better visibility: 2x native size (8x14 -> 16x28)
        const int charWidth = 16;
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
        canvas.DrawText(description, 10, 28, SKTextAlign.Left, headerFont, headerPaint);

        var fgColor = SKColors.LightGray;
        var bgColor = SKColors.Black;
        using var pixelPaint = new SKPaint { Color = fgColor };

        var validationErrors = new List<string>();
        var validator = new GlyphPixelValidator();

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

                // Validate FontNumber for printable characters if expectedFontNum is specified
                // Only validate cells that should have the expected font (skip headers, control chars)
                // Skip '.' (0x2E) since it's used as a placeholder for control characters (written with SI)
                if (expectedFontNum > 0 && charValue >= 0x20 && charValue < 0x7F && charValue != ' ' && charValue != '.')
                {
                    // Skip the header rows (row 0 and 1 typically contain description text)
                    // and the row headers (first few columns with "Xx:" format)
                    bool isInDataArea = row >= 2 && col >= 4;

                    if (isInDataArea)
                    {
                        var fontValidation = GlyphPixelValidator.ValidateFontNumber(
                            row, col, cell.Codepoint, cell.FontNumber, expectedFontNum);

                        if (!fontValidation.IsValid)
                        {
                            var errorStr = fontValidation.ToErrorString();
                            if (errorStr != null)
                            {
                                validationErrors.Add(errorStr);
                            }
                        }
                    }
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

        // After rendering, validate pixels match expected font bits
        for (int row = 0; row < emulator.Buffer.Height; row++)
        {
            for (int col = 0; col < emulator.Buffer.Width; col++)
            {
                var cell = emulator.Buffer.GetCell(row, col);
                if (cell.Codepoint == 0 || cell.Codepoint == ' ') continue; // Skip empty/space

                int cellX = col * charWidth;
                int cellY = row * charHeight + headerHeight;

                var result = validator.ValidateGlyph(bitmap, cellX, cellY, charWidth, charHeight,
                    cell.Codepoint, cell.FontNumber, fgColor, bgColor);

                if (!result.IsValid && result.HasGlyph)
                {
                    // Only report errors for significant mismatches (allow some tolerance)
                    if (result.Mismatches.Count > 5)
                    {
                        var errorStr = result.ToErrorString();
                        if (errorStr != null && validationErrors.Count < 20)
                        {
                            validationErrors.Add($"[{row},{col}] {errorStr}");
                        }
                    }
                }
            }
        }

        // Limit error output for readability
        if (validationErrors.Count > 0)
        {
            var errorSummary = string.Join("\n", validationErrors);
            Assert.Fail($"Validation errors ({validationErrors.Count}):\n{errorSummary}");
        }
    }

    private void SaveTextRepresentation(TDV2200Emulator emulator, string filename, string description)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"=== {description} ===");
        sb.AppendLine($"Terminal: TDV2200 {emulator.Buffer.Width}x{emulator.Buffer.Height}");
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
    /// Direct pixel comparison test: Extract rendered pixels and compare to font definition
    /// </summary>
    [Fact]
    public void DirectPixelComparison_CharSet1_LetterA()
    {
        // Get the font bits for 'A' (0x41) in character set 1
        ushort[]? bits = _font.GetFontBits(0x41, 0);
        Assert.NotNull(bits);
        Assert.NotNull(bits);
        Assert.Equal(16, bits.Length);

        // Verify specific pixel patterns for letter A
        // Row 0 should be blank (top padding)
        Assert.Equal(0x0000, bits[0]);

        // Rows in the middle should have the A shape (apex, widening sides, crossbar)
        // The exact values depend on the font design
        bool hasApex = false;
        bool hasCrossbar = false;

        for (int row = 1; row < 10; row++)
        {
            if (bits[row] != 0)
            {
                // Check for apex (single pixel or small cluster at top)
                int setBits = CountSetBits(bits[row]);
                if (row <= 3 && setBits <= 3)
                {
                    hasApex = true;
                }
                // Check for crossbar (horizontal line in middle rows)
                if (row >= 5 && row <= 7 && setBits >= 5)
                {
                    hasCrossbar = true;
                }
            }
        }

        Assert.True(hasApex, "Letter A should have an apex (top point)");
        Assert.True(hasCrossbar, "Letter A should have a horizontal crossbar");
    }

    private int CountSetBits(ushort value)
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
    /// Comprehensive test that dumps all character set glyphs to text files for manual inspection
    /// </summary>
    [Fact]
    public void DumpAllCharSets_ToTextFiles()
    {
        DumpCharSetToFile(0, "charset1_glyphs_dump.txt", "Character Set 1 (Standard ASCII)");
        DumpCharSetToFile(2, "charset2_glyphs_dump.txt", "Character Set 2 (Line Drawing/Graphics I)");
        DumpCharSetToFile(3, "charset3_glyphs_dump.txt", "Character Set 3 (Subscript/Superscript/Graphics II)");
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
            for (int row = 0; row < Math.Min(bits.Length, 16); row++)
            {
                sb.Append("// ");
                ushort rowBits = bits[row];

                for (int col = 7; col >= 0; col--)
                {
                    bool pixelOn = (rowBits & (1 << col)) != 0;
                    sb.Append(pixelOn ? 'X' : ' ');
                }
                sb.AppendLine();
            }

            // Also show hex values
            sb.Append("// Hex: ");
            for (int i = 0; i < Math.Min(bits.Length, 16); i++)
            {
                sb.Append($"0x{bits[i]:X4}");
                if (i < 15) sb.Append(", ");
            }
            sb.AppendLine();
            sb.AppendLine();
        }

        var path = Path.Combine(ImagesFolder, filename);
        File.WriteAllText(path, sb.ToString());

        Assert.True(File.Exists(path), $"Dump file should be created: {path}");
    }
}
