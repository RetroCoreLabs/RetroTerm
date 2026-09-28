using System;
using System.Drawing;
using System.IO;
using System.Text;

/// <summary>
/// Extracts glyph data from TDV2115 character set BMP files.
/// TDV2115 uses 9x14 dot cell (per specification section 7.2).
///
/// BMP Structure (from analysis):
/// - Size: 280x304 pixels
/// - Row 0 (header): y=6-13 (column labels 0-F)
/// - Data rows: start at y=18, 36, 54, 72, 90, 108, 126, 144
/// - Each data row is ~18 pixels tall with character content ~9 pixels
/// - 16 columns starting at x=6, each ~17 pixels wide
/// </summary>
class Program
{
    const int GLYPH_WIDTH = 9;
    const int GLYPH_HEIGHT = 14;

    // Fixed grid parameters based on analysis
    const int HEADER_ROW_Y = 6;
    const int FIRST_DATA_ROW_Y = 18;
    const int ROW_HEIGHT = 18;
    const int NUM_DATA_ROWS = 8;

    const int FIRST_COL_X = 6;
    const int COL_WIDTH = 17;
    const int NUM_COLS = 16;

    static void Main(string[] args)
    {
        string specPath = @"E:\Dev\Ronny\RetroTerm\spec\TDV2115";
        string outputPath = @"E:\Dev\Ronny\RetroTerm\tools\BmpGlyphExtractor\output";

        Directory.CreateDirectory(outputPath);

        Console.WriteLine("=== TDV2115 Character Set BMP Extractor ===\n");
        Console.WriteLine($"Using fixed grid: {NUM_COLS} columns x {NUM_DATA_ROWS} rows");
        Console.WriteLine($"Column width: {COL_WIDTH}, Row height: {ROW_HEIGHT}");
        Console.WriteLine($"First column at x={FIRST_COL_X}, First data row at y={FIRST_DATA_ROW_Y}\n");

        // Show sample character locations for verification
        ShowSampleCharacters(Path.Combine(specPath, "character_set_1.bmp"));

        // Extract all character sets
        ExtractCharacterSet(
            Path.Combine(specPath, "character_set_1.bmp"),
            Path.Combine(outputPath, "charset1_final.txt"),
            "Character Set 1 - ASCII (0x00-0x7F)"
        );

        ExtractCharacterSet(
            Path.Combine(specPath, "character_set_2.bmp"),
            Path.Combine(outputPath, "charset2_final.txt"),
            "Character Set 2 - Line Drawing (0x00-0x7F)"
        );

        ExtractCharacterSet(
            Path.Combine(specPath, "character_set_3.bmp"),
            Path.Combine(outputPath, "charset3_final.txt"),
            "Character Set 3 - Subscript/Superscript (0x00-0x7F)"
        );

        ExtractCharacterSet(
            Path.Combine(specPath, "character_set_4.bmp"),
            Path.Combine(outputPath, "charset4_final.txt"),
            "Character Set 4 - Control Display (0x00-0x7F)"
        );

        // Create combined font file
        CreateFontFile(outputPath);

        Console.WriteLine("\n=== Done! ===");
        Console.WriteLine($"Output: {outputPath}");
    }

    static void ShowSampleCharacters(string bmpPath)
    {
        Console.WriteLine("=== Sample Character Verification ===\n");

        using (Bitmap bmp = new Bitmap(bmpPath))
        {
            // Show '0' at 0x30 (row 3, col 0)
            int row3Y = FIRST_DATA_ROW_Y + 3 * ROW_HEIGHT;
            int col0X = FIRST_COL_X + 0 * COL_WIDTH;
            Console.WriteLine($"Character 0x30 ('0') at ({col0X}, {row3Y}):");
            ShowCell(bmp, col0X, row3Y);

            // Scan row 4 to find where content is
            int row4Y = FIRST_DATA_ROW_Y + 4 * ROW_HEIGHT;
            Console.WriteLine($"\nScanning row 4 (y={row4Y}-{row4Y+ROW_HEIGHT}) for dark pixels:");
            for (int y = row4Y; y < row4Y + ROW_HEIGHT && y < bmp.Height; y++)
            {
                int darkCount = 0;
                int firstDarkX = -1;
                for (int x = 0; x < bmp.Width; x++)
                {
                    if (bmp.GetPixel(x, y).GetBrightness() < 0.5f)
                    {
                        darkCount++;
                        if (firstDarkX < 0) firstDarkX = x;
                    }
                }
                if (darkCount > 0)
                {
                    Console.WriteLine($"  y={y}: {darkCount} dark pixels, first at x={firstDarkX}");
                }
            }

            // Show where each expected column should be vs where content actually is
            Console.WriteLine($"\nExpected column X positions (COL_WIDTH={COL_WIDTH}):");
            for (int col = 0; col < 8; col++)
            {
                int expectedX = FIRST_COL_X + col * COL_WIDTH;
                Console.WriteLine($"  Column {col}: x={expectedX}");
            }

            // Find actual glyph locations in first few rows
            Console.WriteLine($"\nActual dark content locations in data rows:");
            for (int row = 0; row < 5; row++)
            {
                int rowY = FIRST_DATA_ROW_Y + row * ROW_HEIGHT + ROW_HEIGHT / 2;
                Console.Write($"  Row {row} (y={rowY}): ");

                int prevDark = -100;
                for (int x = 0; x < bmp.Width; x++)
                {
                    if (bmp.GetPixel(x, rowY).GetBrightness() < 0.5f)
                    {
                        if (x > prevDark + 10) // New glyph group
                        {
                            Console.Write($"x={x} ");
                        }
                        prevDark = x;
                    }
                }
                Console.WriteLine();
            }
        }
        Console.WriteLine();
    }

    static void ShowCell(Bitmap bmp, int cellX, int cellY)
    {
        for (int y = cellY; y < cellY + ROW_HEIGHT && y < bmp.Height; y++)
        {
            Console.Write($"  {y,3}: ");
            for (int x = cellX; x < cellX + COL_WIDTH + 2 && x < bmp.Width; x++)
            {
                Color c = bmp.GetPixel(x, y);
                Console.Write(c.GetBrightness() < 0.5f ? '#' : '.');
            }
            Console.WriteLine();
        }
    }

    static void ExtractCharacterSet(string bmpPath, string outputPath, string setName)
    {
        Console.WriteLine($"Extracting {setName}...");

        var sb = new StringBuilder();
        sb.AppendLine($"// {setName}");
        sb.AppendLine($"// Source: {Path.GetFileName(bmpPath)}");
        sb.AppendLine($"// Grid: {NUM_COLS}x{NUM_DATA_ROWS}, Cell: {COL_WIDTH}x{ROW_HEIGHT}");
        sb.AppendLine();

        using (Bitmap bmp = new Bitmap(bmpPath))
        {
            for (int charCode = 0; charCode < 128; charCode++)
            {
                int gridRow = charCode / 16;  // 0-7
                int gridCol = charCode % 16;  // 0-15

                int cellX = FIRST_COL_X + gridCol * COL_WIDTH;
                int cellY = FIRST_DATA_ROW_Y + gridRow * ROW_HEIGHT;

                ushort[] glyph = ExtractGlyph(bmp, cellX, cellY, COL_WIDTH, ROW_HEIGHT);

                // Output
                char displayChar = (charCode >= 0x20 && charCode < 0x7F) ? (char)charCode : '.';
                sb.AppendLine($"// Character 0x{charCode:X2} ('{displayChar}')");
                OutputGlyphVisual(sb, glyph);
                OutputGlyphHex(sb, glyph);
                sb.AppendLine();
            }
        }

        File.WriteAllText(outputPath, sb.ToString());
        Console.WriteLine($"  -> {outputPath}");
    }

    static ushort[] ExtractGlyph(Bitmap bmp, int cellX, int cellY, int cellWidth, int cellHeight)
    {
        ushort[] result = new ushort[GLYPH_HEIGHT];

        // The actual glyph in the BMP is approximately 5-6 pixels wide and 8-9 pixels tall,
        // positioned within a larger cell. We need to find and extract just the glyph area.

        // Find the actual glyph bounds within this cell
        int glyphMinX = cellX + cellWidth;
        int glyphMaxX = cellX;
        int glyphMinY = cellY + cellHeight;
        int glyphMaxY = cellY;

        for (int y = cellY; y < cellY + cellHeight && y < bmp.Height; y++)
        {
            for (int x = cellX; x < cellX + cellWidth && x < bmp.Width; x++)
            {
                if (bmp.GetPixel(x, y).GetBrightness() < 0.5f)
                {
                    if (x < glyphMinX) glyphMinX = x;
                    if (x > glyphMaxX) glyphMaxX = x;
                    if (y < glyphMinY) glyphMinY = y;
                    if (y > glyphMaxY) glyphMaxY = y;
                }
            }
        }

        // If no content found, return blank glyph
        if (glyphMaxX < glyphMinX || glyphMaxY < glyphMinY)
        {
            return result;
        }

        int glyphWidth = glyphMaxX - glyphMinX + 1;
        int glyphHeight = glyphMaxY - glyphMinY + 1;

        // Map source glyph pixels to output pixels
        // The output glyph has leading space (TDV2115 uses 7x9 character in 9x14 cell)
        // So we center the extracted content within the 9x14 output

        int outOffsetX = (GLYPH_WIDTH - glyphWidth) / 2;
        int outOffsetY = (GLYPH_HEIGHT - glyphHeight) / 2;

        // Make sure we don't go negative
        if (outOffsetX < 0) outOffsetX = 0;
        if (outOffsetY < 0) outOffsetY = 0;

        // Direct pixel copy (1:1 mapping) since source is similar size to output
        for (int srcY = glyphMinY; srcY <= glyphMaxY && srcY < bmp.Height; srcY++)
        {
            int outRow = outOffsetY + (srcY - glyphMinY);
            if (outRow >= GLYPH_HEIGHT) continue;

            for (int srcX = glyphMinX; srcX <= glyphMaxX && srcX < bmp.Width; srcX++)
            {
                int outCol = outOffsetX + (srcX - glyphMinX);
                if (outCol >= GLYPH_WIDTH) continue;

                if (bmp.GetPixel(srcX, srcY).GetBrightness() < 0.5f)
                {
                    // Bit 8 (0x100) is leftmost, bit 0 is rightmost
                    result[outRow] |= (ushort)(0x100 >> outCol);
                }
            }
        }

        return result;
    }

    static void OutputGlyphVisual(StringBuilder sb, ushort[] glyph)
    {
        for (int row = 0; row < glyph.Length; row++)
        {
            sb.Append("// ");
            for (int col = 0; col < GLYPH_WIDTH; col++)
            {
                bool isSet = (glyph[row] & (0x100 >> col)) != 0;
                sb.Append(isSet ? 'X' : ' ');
            }
            sb.AppendLine();
        }
    }

    static void OutputGlyphHex(StringBuilder sb, ushort[] glyph)
    {
        sb.Append("    ");
        for (int i = 0; i < glyph.Length; i++)
        {
            sb.Append($"0x{glyph[i]:X4}");
            if (i < glyph.Length - 1) sb.Append(", ");
        }
        sb.AppendLine(",");
    }

    static void CreateFontFile(string outputPath)
    {
        Console.WriteLine("\nCreating combined font file...");

        var sb = new StringBuilder();
        sb.AppendLine("// =============================================================================");
        sb.AppendLine("// TDV2215 Font Glyphs");
        sb.AppendLine("// =============================================================================");
        sb.AppendLine("// Extracted from TDV2115 specification BMP files");
        sb.AppendLine($"// Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine("//");
        sb.AppendLine("// Font structure:");
        sb.AppendLine("//   Height = 14 (9x14 dot cell per TDV2115 spec)");
        sb.AppendLine("//   fontNumOffset = [0, 0, 128, 256, 384]");
        sb.AppendLine("//");
        sb.AppendLine("// Character Sets:");
        sb.AppendLine("//   Set 1 (fontNum 0,1): Standard ASCII (glyphs 0-127)");
        sb.AppendLine("//   Set 2 (fontNum 2): Line Drawing (glyphs 128-255)");
        sb.AppendLine("//   Set 3 (fontNum 3): Subscript/Superscript (glyphs 256-383)");
        sb.AppendLine("//   Set 4 (fontNum 4): Control Code Display (glyphs 384-511)");
        sb.AppendLine("// =============================================================================");
        sb.AppendLine();

        string[] files = { "charset1_final.txt", "charset2_final.txt", "charset3_final.txt", "charset4_final.txt" };
        string[] labels = {
            "Character Set 1 - ASCII (glyphs 0-127)",
            "Character Set 2 - Line Drawing (glyphs 128-255)",
            "Character Set 3 - Subscript/Superscript (glyphs 256-383)",
            "Character Set 4 - Control Code Display (glyphs 384-511)"
        };

        for (int i = 0; i < files.Length; i++)
        {
            string path = Path.Combine(outputPath, files[i]);
            sb.AppendLine($"// -----------------------------------------------------------------------------");
            sb.AppendLine($"// {labels[i]}");
            sb.AppendLine($"// -----------------------------------------------------------------------------");
            sb.AppendLine();

            if (File.Exists(path))
            {
                sb.AppendLine(File.ReadAllText(path));
            }
            else
            {
                sb.AppendLine("// ERROR: Source file not found!");
            }
            sb.AppendLine();
        }

        string fontFile = Path.Combine(outputPath, "FontTDV2215_extracted.txt");
        File.WriteAllText(fontFile, sb.ToString());
        Console.WriteLine($"  -> {fontFile}");
    }
}
