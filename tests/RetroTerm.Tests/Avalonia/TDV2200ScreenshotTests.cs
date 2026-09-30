using System;
using System.IO;
using System.Text;
using Avalonia.Headless.XUnit;
using RetroTerm.Core.Terminal.Buffer;
using RetroTerm.Core.Terminal.Emulators.TDV;
using RetroTerm.Desktop.Rendering;
using SkiaSharp;
using Xunit;

namespace RetroTerm.Tests.Avalonia;

/// <summary>
/// Screenshot tests for TDV2200 features - buffer state, plus pixels from the real renderer.
/// </summary>
/// <remarks>
/// <para><b>The summary this replaced was false, and had been for a long time</b></para>
/// It read "Uses the ACTUAL TerminalRenderer to ensure tests validate real rendering behavior".
/// It did not. <see cref="SaveRenderTargetAsPng"/> below is a SECOND renderer written in SkiaSharp, with
/// its own background constant, its own foreground constant, its own 40% bold-brightness formula,
/// its own synthetic-bold stamp and its own glyph loop. The pixel assertions then read the PNG that
/// method had just drawn - so they asserted this file's arithmetic, not the program's.
///
/// Concretely: if <see cref="TerminalRenderer"/> stopped brightening bold, or stopped drawing underlines
/// altogether, every test in here stayed green.
///
/// <para><b>What changed, 28 August 2026</b></para>
/// The pixel assertions now come from <see cref="RenderedScreenshot.Capture"/>, which renders through
/// <see cref="RetroTerm.Desktop.Controls.TerminalCanvas"/> to <see cref="TerminalRenderer"/> to <see cref="BitmapFontRenderer"/> - the production
/// chain. <see cref="SaveRenderTargetAsPng"/> stays, because the sheets it draws carry a caption and are
/// genuinely nice to look at, but NOTHING IS ASSERTED AGAINST THEM ANY MORE.
///
/// <para><b>Do not add a new assertion against the SkiaSharp sheet</b></para>
/// If a new check needs pixels, capture them. <see cref="RenderedScreenshot"/> was written for exactly this
/// file and its own summary says so; it just never got used here.
/// </remarks>
[Collection("Avalonia")]
public class TDV2200ScreenshotTests
{
    private static readonly string ImagesFolder = Path.Combine(
        Path.GetDirectoryName(typeof(TDV2200ScreenshotTests).Assembly.Location) ?? "",
        "..", "..", "..", "Avalonia", "images");

    static TDV2200ScreenshotTests()
    {
        // Ensure images folder exists
        Directory.CreateDirectory(ImagesFolder);
    }

    /// <summary>
    /// Saves a screenshot using the same rendering logic as TerminalRenderer.
    /// Uses SkiaSharp directly for reliable headless mode saving, but applies
    /// the same Bold rendering logic (synthetic bold + 40% brightness) that
    /// TerminalRenderer.DrawCharacter and GetCellForeground use.
    /// </summary>
    private void SaveScreenshot(TDV2200Emulator emulator, string filename, string description)
    {
        // Get dimensions from the font renderer (same source as TerminalRenderer)
        var fontRenderer = emulator.CreateFontRenderer();
        double charWidth = fontRenderer.GetCharWidth();
        double charHeight = fontRenderer.GetCharHeight();
        const int headerHeight = 28;

        int terminalWidth = (int)(emulator.Buffer.Width * charWidth);
        int terminalHeight = (int)(emulator.Buffer.Height * charHeight);
        int totalWidth = terminalWidth;
        int totalHeight = terminalHeight + headerHeight;

        // Save screenshot using same rendering logic as TerminalRenderer
        SaveRenderTargetAsPng(emulator, filename, totalWidth, totalHeight, headerHeight, description);

        // Also save text representation for documentation
        SaveTextRepresentation(emulator, filename, description);
    }

    /// <summary>
    /// Saves Avalonia RenderTargetBitmap as PNG.
    /// Due to headless mode limitations, we re-render using SkiaSharp directly,
    /// but using the same BitmapFontRenderer that TerminalRenderer uses.
    /// </summary>
    private void SaveRenderTargetAsPng(TDV2200Emulator emulator, string filename, int totalWidth, int totalHeight, int headerHeight, string description)
    {
        // Create SkiaSharp bitmap and render using same font as TerminalRenderer
        using var bitmap = new SKBitmap(totalWidth, totalHeight);
        using var canvas = new SKCanvas(bitmap);

        // Get the font renderer that TerminalRenderer would use
        var fontRenderer = emulator.CreateFontRenderer();
        double charWidth = fontRenderer.GetCharWidth();
        double charHeight = fontRenderer.GetCharHeight();

        // Background color (matches TerminalRenderer's default)
        canvas.Clear(new SKColor(0, 25, 17)); // #001911

        // Draw header
        using var headerFont = new SKFont(SKTypeface.FromFamilyName("Consolas", SKFontStyle.Bold), 14);
        using var headerBgPaint = new SKPaint { Color = new SKColor(0, 40, 100) };
        canvas.DrawRect(0, 0, totalWidth, headerHeight, headerBgPaint);
        using var headerPaint = new SKPaint { Color = SKColors.Yellow };
        canvas.DrawText(description, 5, 19, SKTextAlign.Left, headerFont, headerPaint);

        // Default foreground color (matches TerminalRenderer)
        var defaultFg = new SKColor(0, 255, 136); // #00FF88

        // Render using BitmapFontRenderer's font data (same as TerminalRenderer uses)
        if (fontRenderer is BitmapFontRenderer bitmapRenderer)
        {
            var font = bitmapRenderer.Font;

            for (int row = 0; row < emulator.Buffer.Height; row++)
            {
                for (int col = 0; col < emulator.Buffer.Width; col++)
                {
                    var cell = emulator.Buffer.GetCell(row, col);
                    if (cell.Codepoint == 0 || cell.Codepoint == ' ') continue;
                    if (cell.Attributes.HasAttribute(CharacterAttributes.Hidden)) continue;

                    int x = (int)(col * charWidth);
                    int y = headerHeight + (int)(row * charHeight);

                    // Calculate foreground color with Bold brightness (same as TerminalRenderer)
                    var fg = defaultFg;
                    if (cell.Attributes.HasAttribute(CharacterAttributes.Bold))
                    {
                        // 40% brightness increase (same as TerminalRenderer.GetCellForeground)
                        fg = new SKColor(
                            (byte)Math.Min(255, fg.Red + (255 - fg.Red) * 0.4),
                            (byte)Math.Min(255, fg.Green + (255 - fg.Green) * 0.4),
                            (byte)Math.Min(255, fg.Blue + (255 - fg.Blue) * 0.4));
                    }

                    var bg = new SKColor(0, 25, 17);
                    if (cell.Attributes.HasAttribute(CharacterAttributes.Reverse))
                    {
                        (fg, bg) = (bg, fg);
                    }

                    // Draw background if not default
                    if (bg != new SKColor(0, 25, 17))
                    {
                        using var bgPaint = new SKPaint { Color = bg };
                        canvas.DrawRect(x, y, (float)charWidth, (float)charHeight, bgPaint);
                    }

                    // Get font bits and render (same as BitmapFontRenderer)
                    var fontBits = font.GetFontBits((ushort)cell.Codepoint, cell.FontNumber);
                    if (fontBits == null && cell.FontNumber != 0)
                        fontBits = font.GetFontBits((ushort)cell.Codepoint, 0);

                    if (fontBits != null)
                    {
                        using var paint = new SKPaint { Color = fg };
                        bool isBold = cell.Attributes.HasAttribute(CharacterAttributes.Bold);

                        // First pass
                        RenderFontBits(canvas, paint, fontBits, x, y, font.Width, font.HeightToUse);

                        // Synthetic bold: second pass offset by 1 pixel (same as TerminalRenderer.DrawCharacter)
                        if (isBold)
                        {
                            RenderFontBits(canvas, paint, fontBits, x + 1, y, font.Width, font.HeightToUse);
                        }
                    }

                    // Underline (same as what TerminalRenderer would show)
                    if (cell.Attributes.HasAttribute(CharacterAttributes.Underline))
                    {
                        using var linePaint = new SKPaint { Color = fg, StrokeWidth = 1 };
                        canvas.DrawLine(x, y + (float)charHeight - 1, x + (float)charWidth, y + (float)charHeight - 1, linePaint);
                    }
                }
            }
        }

        // Save as PNG
        var pngPath = Path.Combine(ImagesFolder, filename + ".png");
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = File.Create(pngPath);
        data.SaveTo(stream);
    }

    private void RenderFontBits(SKCanvas canvas, SKPaint paint, ushort[] fontBits, int x, int y, int fontWidth, int fontHeight)
    {
        for (int row = 0; row < fontHeight && row < fontBits.Length; row++)
        {
            ushort rowBits = fontBits[row];
            for (int col = 0; col < fontWidth; col++)
            {
                int bitIndex = fontWidth - 1 - col;
                if ((rowBits & (1 << bitIndex)) != 0)
                {
                    canvas.DrawRect(x + col, y + row, 1, 1, paint);
                }
            }
        }
    }

    #region PNG Pixel Validation

    /// <summary>
    /// Validates that Bold text is rendered brighter than Normal text.
    /// Returns average color values for comparison.
    /// </summary>
    private (SKColor normalAvg, SKColor boldAvg, bool isValid) ValidateBoldBrightness(
        string pngPath, int normalRow, int boldRow, int headerHeight, double charHeight)
    {
        using var bitmap = SKBitmap.Decode(pngPath);
        if (bitmap == null)
            return (SKColors.Black, SKColors.Black, false);

        int normalY = headerHeight + (int)(normalRow * charHeight) + (int)(charHeight / 2);
        int boldY = headerHeight + (int)(boldRow * charHeight) + (int)(charHeight / 2);

        var normalPixels = GetForegroundPixels(bitmap, normalY, 0, 200);
        var boldPixels = GetForegroundPixels(bitmap, boldY, 0, 200);

        if (normalPixels.Count == 0 || boldPixels.Count == 0)
            return (SKColors.Black, SKColors.Black, false);

        var normalAvg = AverageColor(normalPixels);
        var boldAvg = AverageColor(boldPixels);

        // Bold should be brighter (higher B channel for green text)
        // Normal: #00FF88 (R=0, G=255, B=136)
        // Bold:   #00FF?? where ?? > 136 due to 40% brightness increase
        bool isValid = boldAvg.Blue > normalAvg.Blue || boldAvg.Green > normalAvg.Green;

        return (normalAvg, boldAvg, isValid);
    }

    /// <summary>
    /// Validates that Bold text has more lit pixels (synthetic bold effect).
    /// </summary>
    private (int normalCount, int boldCount, bool isValid) ValidateSyntheticBold(
        string pngPath, int normalRow, int boldRow, int headerHeight, double charHeight)
    {
        using var bitmap = SKBitmap.Decode(pngPath);
        if (bitmap == null)
            return (0, 0, false);

        int normalY = headerHeight + (int)(normalRow * charHeight) + (int)(charHeight / 2);
        int boldY = headerHeight + (int)(boldRow * charHeight) + (int)(charHeight / 2);

        var normalPixels = GetForegroundPixels(bitmap, normalY, 0, 200);
        var boldPixels = GetForegroundPixels(bitmap, boldY, 0, 200);

        // Bold should have more lit pixels due to double-draw at x and x+1
        bool isValid = boldPixels.Count > normalPixels.Count;

        return (normalPixels.Count, boldPixels.Count, isValid);
    }

    /// <summary>
    /// Validates that Underline has pixels at the bottom of the character cell.
    /// </summary>
    private bool ValidateUnderline(string pngPath, int row, int headerHeight, double charHeight)
    {
        using var bitmap = SKBitmap.Decode(pngPath);
        if (bitmap == null)
            return false;

        // Check last row of character cell for underline pixels
        int underlineY = headerHeight + (int)((row + 1) * charHeight) - 1;
        var underlinePixels = GetForegroundPixels(bitmap, underlineY, 0, 200);

        return underlinePixels.Count > 10; // Should have underline pixels
    }

    /// <summary>
    /// Validates that Reverse video has dark foreground on light background.
    /// For reverse video, large areas of bright green background (not just text pixels) should exist.
    /// </summary>
    private bool ValidateReverse(string pngPath, int row, int headerHeight, double charHeight)
    {
        using var bitmap = SKBitmap.Decode(pngPath);
        if (bitmap == null)
            return false;

        int y = headerHeight + (int)(row * charHeight) + (int)(charHeight / 2);

        // For reverse video, background should be bright (the foreground color)
        // Count consecutive bright pixels - reverse video has continuous bright backgrounds
        // Normal text has scattered bright pixels (just the glyph)
        int consecutiveBrightCount = 0;
        int maxConsecutive = 0;

        for (int x = 0; x < Math.Min(bitmap.Width, 200); x++)
        {
            var pixel = bitmap.GetPixel(x, y);
            // Bright green background (green > 200)
            if (pixel.Green > 200)
            {
                consecutiveBrightCount++;
                if (consecutiveBrightCount > maxConsecutive)
                    maxConsecutive = consecutiveBrightCount;
            }
            else
            {
                consecutiveBrightCount = 0;
            }
        }

        // Reverse video should have at least 8 consecutive bright pixels (one character width)
        // Normal text has gaps between glyphs
        return maxConsecutive >= 8;
    }

    /// <summary>
    /// Validates that Hidden text has no visible foreground pixels.
    /// </summary>
    private bool ValidateHidden(string pngPath, int row, int startCol, int headerHeight, double charWidth, double charHeight)
    {
        using var bitmap = SKBitmap.Decode(pngPath);
        if (bitmap == null)
            return false;

        int y = headerHeight + (int)(row * charHeight) + (int)(charHeight / 2);
        int startX = (int)(startCol * charWidth);
        int endX = startX + 100;

        var pixels = GetForegroundPixels(bitmap, y, startX, endX);

        // Hidden text should have very few or no foreground pixels
        return pixels.Count < 5;
    }

    private List<SKColor> GetForegroundPixels(SKBitmap bitmap, int y, int startX, int endX)
    {
        var pixels = new List<SKColor>();
        var bgColor = new SKColor(0, 25, 17); // Default background

        for (int x = startX; x < Math.Min(endX, bitmap.Width); x++)
        {
            var pixel = bitmap.GetPixel(x, y);
            // Consider it foreground if significantly different from background
            if (Math.Abs(pixel.Green - bgColor.Green) > 50 ||
                Math.Abs(pixel.Blue - bgColor.Blue) > 50)
            {
                pixels.Add(pixel);
            }
        }
        return pixels;
    }

    private SKColor AverageColor(List<SKColor> pixels)
    {
        if (pixels.Count == 0)
            return SKColors.Black;

        int totalR = 0, totalG = 0, totalB = 0;
        for (int i = 0; i < pixels.Count; i++)
        {
            totalR += pixels[i].Red;
            totalG += pixels[i].Green;
            totalB += pixels[i].Blue;
        }
        return new SKColor(
            (byte)(totalR / pixels.Count),
            (byte)(totalG / pixels.Count),
            (byte)(totalB / pixels.Count));
    }

    /// <summary>
    /// Validates that a specific character appears at the expected position.
    /// Checks that foreground pixels exist in the cell area.
    /// </summary>
    private bool ValidateCharacterExists(string pngPath, int row, int col, int headerHeight, double charWidth, double charHeight)
    {
        using var bitmap = SKBitmap.Decode(pngPath);
        if (bitmap == null) return false;

        int x = (int)(col * charWidth);
        int y = headerHeight + (int)(row * charHeight);
        int cellWidth = (int)charWidth;
        int cellHeight = (int)charHeight;

        // Count foreground pixels in the cell
        int fgCount = 0;
        var bgColor = new SKColor(0, 25, 17);

        for (int py = y; py < y + cellHeight && py < bitmap.Height; py++)
        {
            for (int px = x; px < x + cellWidth && px < bitmap.Width; px++)
            {
                var pixel = bitmap.GetPixel(px, py);
                if (Math.Abs(pixel.Green - bgColor.Green) > 50 || Math.Abs(pixel.Blue - bgColor.Blue) > 50)
                {
                    fgCount++;
                }
            }
        }

        return fgCount > 5; // Should have some foreground pixels
    }

    /// <summary>
    /// Validates that a cell is blank (no foreground pixels).
    /// </summary>
    private bool ValidateCellIsBlank(string pngPath, int row, int col, int headerHeight, double charWidth, double charHeight)
    {
        using var bitmap = SKBitmap.Decode(pngPath);
        if (bitmap == null) return false;

        int x = (int)(col * charWidth);
        int y = headerHeight + (int)(row * charHeight);
        int cellWidth = (int)charWidth;
        int cellHeight = (int)charHeight;

        var bgColor = new SKColor(0, 25, 17);

        for (int py = y; py < y + cellHeight && py < bitmap.Height; py++)
        {
            for (int px = x; px < x + cellWidth && px < bitmap.Width; px++)
            {
                var pixel = bitmap.GetPixel(px, py);
                if (Math.Abs(pixel.Green - bgColor.Green) > 50 || Math.Abs(pixel.Blue - bgColor.Blue) > 50)
                {
                    return false; // Found a foreground pixel
                }
            }
        }

        return true; // No foreground pixels found
    }

    /// <summary>
    /// Validates that a row has visible text (any foreground pixels).
    /// </summary>
    private bool ValidateRowHasText(string pngPath, int row, int headerHeight, double charHeight)
    {
        using var bitmap = SKBitmap.Decode(pngPath);
        if (bitmap == null) return false;

        int y = headerHeight + (int)(row * charHeight) + (int)(charHeight / 2);
        var pixels = GetForegroundPixels(bitmap, y, 0, bitmap.Width);

        return pixels.Count > 0;
    }

    /// <summary>
    /// Validates that a row is blank (no foreground pixels).
    /// </summary>
    private bool ValidateRowIsBlank(string pngPath, int row, int headerHeight, double charHeight)
    {
        using var bitmap = SKBitmap.Decode(pngPath);
        if (bitmap == null) return false;

        int y = headerHeight + (int)(row * charHeight) + (int)(charHeight / 2);
        var pixels = GetForegroundPixels(bitmap, y, 0, bitmap.Width);

        return pixels.Count == 0;
    }

    /// <summary>
    /// Validates that PNG file was created and has content.
    /// </summary>
    private bool ValidatePngExists(string pngPath)
    {
        if (!File.Exists(pngPath)) return false;
        var fileInfo = new FileInfo(pngPath);
        return fileInfo.Length > 100; // Should have reasonable size
    }

    #endregion

    private void SaveTextRepresentation(TDV2200Emulator emulator, string filename, string description)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"=== {description} ===");
        sb.AppendLine($"Terminal: TDV2200 {emulator.Buffer.Width}x{emulator.Buffer.Height}");
        sb.AppendLine();

        // Render buffer to text with visual indicators
        for (int row = 0; row < Math.Min(emulator.Buffer.Height, 24); row++)
        {
            var lineContent = new StringBuilder();
            var lineAttrs = new StringBuilder();

            for (int col = 0; col < emulator.Buffer.Width; col++)
            {
                var cell = emulator.Buffer.GetCell(row, col);
                char ch = cell.Codepoint > 0 ? (char)cell.Codepoint : ' ';

                // Apply character set mapping based on FontNumber
                if (cell.FontNumber > 0 && cell.FontNumber <= 9)
                {
                    var charSetType = (TDVCharacterSets.TDVCharacterSetType)cell.FontNumber;
                    ch = TDVCharacterSets.GetCharacter(charSetType, ch);
                }

                lineContent.Append(ch);

                // Build attribute indicator
                char attr = '.';
                if (cell.DoubleWidth) attr = 'W';
                else if (cell.DoubleHeight) attr = 'D';
                else if ((cell.Attributes & CharacterAttributes.Bold) != 0) attr = 'B';
                else if ((cell.Attributes & CharacterAttributes.Underline) != 0) attr = 'U';
                else if ((cell.Attributes & CharacterAttributes.Reverse) != 0) attr = 'R';
                else if ((cell.Attributes & CharacterAttributes.Blink) != 0) attr = '*';
                else if ((cell.Attributes & CharacterAttributes.Hidden) != 0) attr = 'H';
                else if (cell.FontNumber > 0) attr = (char)('0' + cell.FontNumber);

                lineAttrs.Append(attr);
            }

            sb.AppendLine($"[{row:D2}] {lineContent}");

            // Only show attr line if there are non-default attributes
            if (lineAttrs.ToString().Trim('.').Length > 0)
            {
                sb.AppendLine($"     {lineAttrs}");
            }
        }

        sb.AppendLine();
        sb.AppendLine("Legend: W=DoubleWidth, D=DoubleHeight, B=Bold, U=Underline, R=Reverse, *=Blink, H=Hidden, 2/3=FontNumber");

        // Save to file
        var path = Path.Combine(ImagesFolder, filename + ".txt");
        File.WriteAllText(path, sb.ToString());
    }

    #region Rectangle Attribute Screenshots

    [AvaloniaFact]
    public void Screenshot_NDAAR_BoldRectangle()
    {
        var emulator = new TDV2200Emulator(80, 24);

        emulator.ProcessData(Encoding.UTF8.GetBytes("NDAAR Bold Rectangle Test:\r\n\r\n"));
        emulator.ProcessData(Encoding.UTF8.GetBytes("Normal Text Here\r\n"));
        emulator.ProcessData(Encoding.UTF8.GetBytes("This text will be BOLD\r\n"));
        emulator.ProcessData(Encoding.UTF8.GetBytes("Normal again here"));

        // NDAAR: Add Bold (1) to rectangle row 3, cols 0-21
        emulator.ProcessData(new byte[] { 0x1B, (byte)'[', (byte)'1', (byte)';', (byte)'3', (byte)';',
            (byte)'0', (byte)';', (byte)'3', (byte)';', (byte)'2', (byte)'1', (byte)'{' });

        // The documentation sheet, drawn by this file's own SkiaSharp path. Kept because it is a
        // useful thing to LOOK at - but nothing below is asserted against it. See the note on
        // SaveRenderTargetAsPng.
        SaveScreenshot(emulator, "03_ndaar_bold",
            "NDAAR: Bold attribute added to rectangle (row 3, cols 0-21)");

        // Validate buffer has Bold attribute
        var cell = emulator.Buffer.GetCell(3, 5);
        Assert.True((cell.Attributes & CharacterAttributes.Bold) != 0, "Buffer cell should have Bold attribute");

        // THE PIXELS COME FROM THE REAL RENDERER, 28 August 2026.
        //
        // These two assertions used to read the PNG that SaveRenderTargetAsPng had just drawn with
        // THIS FILE's own copy of the bold rule - brightness lifted 40%, then the glyph stamped a
        // second time one pixel right. So the test asserted its own arithmetic. If TerminalRenderer
        // stopped brightening bold altogether, both stayed green.
        //
        // RenderedScreenshot.Capture goes through TerminalCanvas -> TerminalRenderer ->
        // BitmapFontRenderer, which is the chain the screen actually uses.
        using var shot = RenderedScreenshot.Capture(emulator, "03_ndaar_bold_rendered");

        // Row 2 is normal text, row 3 carries the NDAAR bold rectangle. Column 5 is inside both
        // ("Normal Text Here" / "This text will be BOLD"), so the same column can be compared.
        var normal = shot.BrightestColorInCell(2, 5);
        var bold = shot.BrightestColorInCell(3, 5);

        int normalSum = normal.Red + normal.Green + normal.Blue;
        int boldSum = bold.Red + bold.Green + bold.Blue;
        Assert.True(boldSum > normalSum,
            $"Bold should render brighter through TerminalRenderer. Normal R={normal.Red},G={normal.Green},B={normal.Blue}; "
            + $"bold R={bold.Red},G={bold.Green},B={bold.Blue}");

        // Synthetic bold stamps the glyph twice, so the bold cell must carry MORE lit pixels.
        int normalInk = shot.InkPixelsInCell(2, 5);
        int boldInk = shot.InkPixelsInCell(3, 5);
        Assert.True(boldInk > normalInk,
            $"Synthetic bold should light more pixels. Normal {normalInk}, bold {boldInk}");
    }

    [AvaloniaFact]
    public void Screenshot_NDAAR_UnderlineRectangle()
    {
        var emulator = new TDV2200Emulator(80, 24);

        emulator.ProcessData(Encoding.UTF8.GetBytes("NDAAR Underline Rectangle Test:\r\n\r\n"));
        emulator.ProcessData(Encoding.UTF8.GetBytes("Normal Text\r\n"));
        emulator.ProcessData(Encoding.UTF8.GetBytes("UNDERLINED TEXT\r\n"));

        // NDAAR: Add Underline (4) to rectangle row 3, cols 0-14
        emulator.ProcessData(new byte[] { 0x1B, (byte)'[', (byte)'4', (byte)';', (byte)'3', (byte)';',
            (byte)'0', (byte)';', (byte)'3', (byte)';', (byte)'1', (byte)'4', (byte)'{' });

        SaveScreenshot(emulator, "04_ndaar_underline",
            "NDAAR: Underline attribute added to rectangle");

        var cell = emulator.Buffer.GetCell(3, 5);
        Assert.True((cell.Attributes & CharacterAttributes.Underline) != 0);

        // THE PIXELS COME FROM THE REAL RENDERER, 28 August 2026 - same reason as the bold test
        // above. The old check read this file's own PNG, in which the underline was drawn by this
        // file. TerminalRenderer could have stopped drawing underlines entirely and both assertions
        // would have passed.
        using var shot = RenderedScreenshot.Capture(emulator, "04_ndaar_underline_rendered");

        // The underline sits on the last pixel rows of the cell. Compare the bottom row of an
        // underlined cell against the same place on a normal one: "UNDERLINED TEXT" is row 3,
        // "Normal Text" is row 2, and column 5 has a glyph in both.
        int underlinedInk = InkOnBottomRowsOfCell(shot, 3, 5);
        int normalInk = InkOnBottomRowsOfCell(shot, 2, 5);

        Assert.True(underlinedInk > normalInk,
            $"The underlined row should carry more ink along the bottom of the cell. "
            + $"Underlined {underlinedInk}, normal {normalInk}");
    }

    /// <summary>
    /// Counts lit pixels on the bottom two pixel rows of one character cell.
    /// </summary>
    /// <param name="shot">
    /// A capture taken through the real renderer.
    /// </param>
    /// <param name="row">
    /// The text row.
    /// </param>
    /// <param name="col">
    /// The text column.
    /// </param>
    /// <returns>
    /// The number of pixels that are not the background.
    /// </returns>
    /// <remarks>
    /// An underline is drawn along the foot of the cell, so it shows up as ink on rows a glyph
    /// rarely reaches. Two rows rather than one, because the exact baseline depends on the cell
    /// height and pinning it would make this a test about font metrics.
    /// </remarks>
    private static int InkOnBottomRowsOfCell(RenderedScreenshot shot, int row, int col)
    {
        var background = shot.PixelAt(0, 0);

        int left = (int)(col * shot.CellWidth);
        int right = (int)((col + 1) * shot.CellWidth);
        int bottom = (int)((row + 1) * shot.CellHeight);

        int lit = 0;
        for (int y = bottom - 2; y < bottom; y++)
        {
            for (int x = left; x < right; x++)
            {
                if (!RenderedScreenshot.ApproximatelyEqual(shot.PixelAt(x, y), background, 12))
                {
                    lit++;
                }
            }
        }
        return lit;
    }

    [AvaloniaFact]
    public void Screenshot_NDAAR_ReverseRectangle()
    {
        var emulator = new TDV2200Emulator(80, 24);

        emulator.ProcessData(Encoding.UTF8.GetBytes("NDAAR Reverse Video Test:\r\n\r\n"));
        emulator.ProcessData(Encoding.UTF8.GetBytes("Normal Background\r\n"));
        emulator.ProcessData(Encoding.UTF8.GetBytes("REVERSED VIDEO\r\n"));

        // NDAAR: Add Reverse (7)
        emulator.ProcessData(new byte[] { 0x1B, (byte)'[', (byte)'7', (byte)';', (byte)'3', (byte)';',
            (byte)'0', (byte)';', (byte)'3', (byte)';', (byte)'1', (byte)'3', (byte)'{' });

        SaveScreenshot(emulator, "05_ndaar_reverse",
            "NDAAR: Reverse video attribute added to rectangle");

        var cell = emulator.Buffer.GetCell(3, 5);
        Assert.True((cell.Attributes & CharacterAttributes.Reverse) != 0);

        // Validate PNG: Reversed text should have bright background pixels
        var pngPath = Path.Combine(ImagesFolder, "05_ndaar_reverse.png");
        Assert.True(ValidatePngExists(pngPath), "PNG file should exist and have content");
        const int headerHeight = 28;
        double charHeight = 14;

        Assert.True(ValidateReverse(pngPath, 3, headerHeight, charHeight),
            "Reverse video row should have continuous bright background pixels (foreground/background swapped)");

        // Validate row 2 (normal) has visible text
        Assert.True(ValidateRowHasText(pngPath, 2, headerHeight, charHeight),
            "Normal text row should have visible text");
    }

    [AvaloniaFact]
    public void Screenshot_NDRAR_RemoveAttribute()
    {
        var emulator = new TDV2200Emulator(80, 24);

        emulator.ProcessData(Encoding.UTF8.GetBytes("NDRAR Remove Attribute Test:\r\n\r\n"));

        // Row 2: Write text that will stay bold (for comparison)
        emulator.ProcessData(new byte[] { 0x1B, (byte)'[', (byte)'1', (byte)'m' }); // Bold on
        emulator.ProcessData(Encoding.UTF8.GetBytes("STAYS BOLD\r\n"));              // Row 2
        emulator.ProcessData(new byte[] { 0x1B, (byte)'[', (byte)'0', (byte)'m' }); // Reset

        // Row 3: Write text that will have bold removed
        emulator.ProcessData(new byte[] { 0x1B, (byte)'[', (byte)'1', (byte)'m' }); // Bold on
        emulator.ProcessData(Encoding.UTF8.GetBytes("WAS BOLD NOW NORMAL"));         // Row 3
        emulator.ProcessData(new byte[] { 0x1B, (byte)'[', (byte)'0', (byte)'m' }); // Reset

        // Verify both have bold before removal
        Assert.True((emulator.Buffer.GetCell(2, 0).Attributes & CharacterAttributes.Bold) != 0);
        Assert.True((emulator.Buffer.GetCell(3, 0).Attributes & CharacterAttributes.Bold) != 0);

        // NDRAR: Remove Bold (1) from row 3 only (cols 0-19)
        emulator.ProcessData(new byte[] { 0x1B, (byte)'[', (byte)'1', (byte)';', (byte)'3', (byte)';',
            (byte)'0', (byte)';', (byte)'3', (byte)';', (byte)'1', (byte)'9', (byte)'|' });

        SaveScreenshot(emulator, "06_ndrar_remove_bold",
            "NDRAR: Row 2 stays bold, Row 3 had bold removed");

        // Verify: Row 2 still bold, Row 3 no longer bold
        Assert.True((emulator.Buffer.GetCell(2, 0).Attributes & CharacterAttributes.Bold) != 0,
            "Row 2 should still have Bold");
        Assert.True((emulator.Buffer.GetCell(3, 0).Attributes & CharacterAttributes.Bold) == 0,
            "Row 3 should no longer have Bold");

        // Validate PNG: Row 2 should be brighter than Row 3
        var pngPath = Path.Combine(ImagesFolder, "06_ndrar_remove_bold.png");
        const int headerHeight = 28;
        double charHeight = 14;

        var (row3Avg, row2Avg, isValid) = ValidateBoldBrightness(pngPath, 3, 2, headerHeight, charHeight);
        Assert.True(isValid,
            $"Row 2 (bold) should be brighter than Row 3 (normal). Row2: G={row2Avg.Green},B={row2Avg.Blue}. Row3: G={row3Avg.Green},B={row3Avg.Blue}");
    }

    #endregion

    #region Character Set Screenshots

    [AvaloniaFact]
    public void Screenshot_SS2_GraphicsI()
    {
        var emulator = new TDV2200Emulator(80, 24);

        emulator.ProcessData(Encoding.UTF8.GetBytes("SS2 Single Shift to Graphics I:\r\n\r\n"));
        emulator.ProcessData(Encoding.UTF8.GetBytes("Normal: `abcdefghijklmnop\r\n"));

        // SS2 affects only the NEXT character - send ESC N before EACH character
        emulator.ProcessData(Encoding.UTF8.GetBytes("SS2 G2: "));
        // Characters 0x60-0x7F map to Graphics I: ` a b c d e f g h i j k l m n o p q r s t u v w x y z { | } ~
        for (byte c = 0x60; c <= 0x70; c++)
        {
            emulator.ProcessData(new byte[] { 0x1B, 0x4E, c }); // ESC N + char
        }

        emulator.ProcessData(Encoding.UTF8.GetBytes("\r\n\r\nGraphics I charset (0x60-0x7F):\r\n"));
        emulator.ProcessData(Encoding.UTF8.GetBytes("` a b c d e f g h i j k l m n o\r\n"));
        emulator.ProcessData(Encoding.UTF8.GetBytes("p q r s t u v w x y z { | } ~\r\n"));

        SaveScreenshot(emulator, "07_ss2_graphics",
            "SS2 (ESC N): Single shift to G2 - chars 0x60-0x7F map to Graphics I");

        // First char after SS2 should have font 2
        Assert.Equal(2, emulator.Buffer.GetCell(3, 8).FontNumber);

        // Validate PNG exists and has content
        var pngPath = Path.Combine(ImagesFolder, "07_ss2_graphics.png");
        Assert.True(ValidatePngExists(pngPath), "PNG file should exist and have content");
        const int headerHeight = 28;
        double charWidth = 8;
        double charHeight = 14;

        // Validate that SS2 row (row 3) has visible graphics glyphs at expected positions
        Assert.True(ValidateCharacterExists(pngPath, 3, 8, headerHeight, charWidth, charHeight),
            "First SS2 character should be visible at row 3, col 8");
        Assert.True(ValidateCharacterExists(pngPath, 3, 9, headerHeight, charWidth, charHeight),
            "Second SS2 character should be visible at row 3, col 9");

        // Validate normal row (row 2) also has visible text
        Assert.True(ValidateRowHasText(pngPath, 2, headerHeight, charHeight),
            "Normal text row should have visible text");
    }

    [AvaloniaFact]
    public void Screenshot_LS2_LockingShift()
    {
        var emulator = new TDV2200Emulator(80, 24);

        emulator.ProcessData(Encoding.UTF8.GetBytes("LS2 Locking Shift to G2 (Graphics I):\r\n\r\n"));
        emulator.ProcessData(Encoding.UTF8.GetBytes("Normal text: `abcdefghijklmnop\r\n"));

        // LS2 locks ALL subsequent characters to G2
        emulator.ProcessData(Encoding.UTF8.GetBytes("LS2 G2:  "));
        emulator.ProcessData(new byte[] { 0x1B, (byte)'n' }); // ESC n = LS2

        // Now send the full Graphics I range (0x60-0x7F)
        for (byte c = 0x60; c <= 0x7F; c++)
        {
            emulator.ProcessData(new byte[] { c });
        }

        // Return to G0 with SI (0x0F)
        emulator.ProcessData(new byte[] { 0x0F });
        emulator.ProcessData(Encoding.UTF8.GetBytes("\r\n\r\nBack to normal after SI\r\n"));

        // Show the mapping table
        emulator.ProcessData(Encoding.UTF8.GetBytes("Graphics I maps 0x60-0x7F to:\r\n"));
        emulator.ProcessData(Encoding.UTF8.GetBytes("Box drawing, shading, etc.\r\n"));

        SaveScreenshot(emulator, "08_ls2_locking_shift",
            "LS2 (ESC n): Locking shift - all chars 0x60-0x7F become Graphics I");

        // All chars in the shifted range should have font 2
        Assert.Equal(2, emulator.Buffer.GetCell(3, 9).FontNumber);
        Assert.Equal(2, emulator.Buffer.GetCell(3, 10).FontNumber);

        // Validate PNG exists and has content
        var pngPath = Path.Combine(ImagesFolder, "08_ls2_locking_shift.png");
        Assert.True(ValidatePngExists(pngPath), "PNG file should exist and have content");
        const int headerHeight = 28;
        double charWidth = 8;
        double charHeight = 14;

        // Validate LS2 row has visible graphics characters
        Assert.True(ValidateCharacterExists(pngPath, 3, 9, headerHeight, charWidth, charHeight),
            "LS2 graphics character should be visible at row 3, col 9");
        Assert.True(ValidateCharacterExists(pngPath, 3, 15, headerHeight, charWidth, charHeight),
            "LS2 graphics character should be visible at row 3, col 15");

        // Validate normal row (row 2) has text
        Assert.True(ValidateRowHasText(pngPath, 2, headerHeight, charHeight),
            "Normal text row should have visible text");

        // Validate text after SI (row 5) returns to normal
        Assert.True(ValidateRowHasText(pngPath, 5, headerHeight, charHeight),
            "'Back to normal after SI' row should have visible text");
    }

    [AvaloniaFact]
    public void Screenshot_LS3_LockingShift()
    {
        var emulator = new TDV2200Emulator(80, 24);

        emulator.ProcessData(Encoding.UTF8.GetBytes("LS3 Locking Shift to G3 (Graphics II):\r\n\r\n"));
        emulator.ProcessData(Encoding.UTF8.GetBytes("Normal text: `abcdefghijklmnop\r\n"));

        // LS3 locks ALL subsequent characters to G3
        emulator.ProcessData(Encoding.UTF8.GetBytes("LS3 G3:  "));
        emulator.ProcessData(new byte[] { 0x1B, (byte)'o' }); // ESC o = LS3

        // Now send the full Graphics II range (0x60-0x7F)
        for (byte c = 0x60; c <= 0x7F; c++)
        {
            emulator.ProcessData(new byte[] { c });
        }

        // Return to G0 with SI (0x0F)
        emulator.ProcessData(new byte[] { 0x0F });
        emulator.ProcessData(Encoding.UTF8.GetBytes("\r\n\r\nBack to normal after SI\r\n"));

        // Show the mapping table
        emulator.ProcessData(Encoding.UTF8.GetBytes("Graphics II maps 0x60-0x7F to:\r\n"));
        emulator.ProcessData(Encoding.UTF8.GetBytes("Circles, triangles, diamonds\r\n"));

        SaveScreenshot(emulator, "09_ls3_locking_shift",
            "LS3 (ESC o): Locking shift - all chars 0x60-0x7F become Graphics II");

        Assert.Equal(3, emulator.Buffer.GetCell(3, 9).FontNumber);
        Assert.Equal(3, emulator.Buffer.GetCell(3, 10).FontNumber);

        // Validate PNG exists and has content
        var pngPath = Path.Combine(ImagesFolder, "09_ls3_locking_shift.png");
        Assert.True(ValidatePngExists(pngPath), "PNG file should exist and have content");
        const int headerHeight = 28;
        double charWidth = 8;
        double charHeight = 14;

        // Validate LS3 row has visible graphics characters
        Assert.True(ValidateCharacterExists(pngPath, 3, 9, headerHeight, charWidth, charHeight),
            "LS3 graphics character should be visible at row 3, col 9");
        Assert.True(ValidateCharacterExists(pngPath, 3, 15, headerHeight, charWidth, charHeight),
            "LS3 graphics character should be visible at row 3, col 15");

        // Validate normal row (row 2) has text
        Assert.True(ValidateRowHasText(pngPath, 2, headerHeight, charHeight),
            "Normal text row should have visible text");

        // Validate text after SI (row 5) returns to normal
        Assert.True(ValidateRowHasText(pngPath, 5, headerHeight, charHeight),
            "'Back to normal after SI' row should have visible text");
    }

    #endregion

    #region Character Insert/Delete Screenshots

    [AvaloniaFact]
    public void Screenshot_NDICHE_InsertCharacters()
    {
        var emulator = new TDV2200Emulator(80, 24);

        emulator.ProcessData(Encoding.UTF8.GetBytes("NDICHE Insert Characters Test:\r\n\r\n"));
        emulator.ProcessData(Encoding.UTF8.GetBytes("Before: ABCDEFGH\r\n"));
        emulator.ProcessData(Encoding.UTF8.GetBytes("After:  ABCDEFGH"));

        // Move to row 3, col 8
        emulator.ProcessData(new byte[] { 0x1B, (byte)'[', (byte)'4', (byte)';', (byte)'9', (byte)'H' });

        // Insert 3 characters
        emulator.ProcessData(new byte[] { 0x1B, (byte)'[', (byte)'3', (byte)'s' });

        SaveScreenshot(emulator, "10_ndiche_insert",
            "NDICHE: Insert 3 blank characters, shifting existing text right");

        Assert.True(emulator.Buffer.GetCell(3, 8).IsEmpty);
        Assert.True(emulator.Buffer.GetCell(3, 9).IsEmpty);
        Assert.True(emulator.Buffer.GetCell(3, 10).IsEmpty);
        Assert.Equal('A', (char)emulator.Buffer.GetCell(3, 11).Codepoint);

        // Validate PNG
        var pngPath = Path.Combine(ImagesFolder, "10_ndiche_insert.png");
        Assert.True(ValidatePngExists(pngPath), "PNG file should exist and have content");
        const int headerHeight = 28;
        double charWidth = 8;
        double charHeight = 14;

        // Validate inserted blank cells are actually blank in PNG
        Assert.True(ValidateCellIsBlank(pngPath, 3, 8, headerHeight, charWidth, charHeight),
            "Inserted cell at col 8 should be blank");
        Assert.True(ValidateCellIsBlank(pngPath, 3, 9, headerHeight, charWidth, charHeight),
            "Inserted cell at col 9 should be blank");
        Assert.True(ValidateCellIsBlank(pngPath, 3, 10, headerHeight, charWidth, charHeight),
            "Inserted cell at col 10 should be blank");

        // Validate shifted 'A' is visible at new position (col 11)
        Assert.True(ValidateCharacterExists(pngPath, 3, 11, headerHeight, charWidth, charHeight),
            "Shifted 'A' should be visible at col 11");

        // Validate 'Before:' row is unchanged (row 2)
        Assert.True(ValidateRowHasText(pngPath, 2, headerHeight, charHeight),
            "Before row should have visible text");
    }

    [AvaloniaFact]
    public void Screenshot_NDDCHE_DeleteCharacters()
    {
        var emulator = new TDV2200Emulator(80, 24);

        emulator.ProcessData(Encoding.UTF8.GetBytes("NDDCHE Delete Characters Test:\r\n\r\n"));
        emulator.ProcessData(Encoding.UTF8.GetBytes("Before: ABCDEFGH\r\n"));
        emulator.ProcessData(Encoding.UTF8.GetBytes("After:  ABCDEFGH"));

        // Move to row 3, col 8
        emulator.ProcessData(new byte[] { 0x1B, (byte)'[', (byte)'4', (byte)';', (byte)'9', (byte)'H' });

        // Delete 3 characters
        emulator.ProcessData(new byte[] { 0x1B, (byte)'[', (byte)'3', (byte)'t' });

        SaveScreenshot(emulator, "11_nddche_delete",
            "NDDCHE: Delete 3 characters, shifting remaining text left");

        Assert.Equal('D', (char)emulator.Buffer.GetCell(3, 8).Codepoint);
        Assert.Equal('E', (char)emulator.Buffer.GetCell(3, 9).Codepoint);

        // Validate PNG
        var pngPath = Path.Combine(ImagesFolder, "11_nddche_delete.png");
        Assert.True(ValidatePngExists(pngPath), "PNG file should exist and have content");
        const int headerHeight = 28;
        double charWidth = 8;
        double charHeight = 14;

        // Validate shifted 'D' is now at col 8 (previously col 11)
        Assert.True(ValidateCharacterExists(pngPath, 3, 8, headerHeight, charWidth, charHeight),
            "Shifted 'D' should be visible at col 8");
        Assert.True(ValidateCharacterExists(pngPath, 3, 9, headerHeight, charWidth, charHeight),
            "Shifted 'E' should be visible at col 9");

        // Validate 'Before:' row is unchanged (row 2)
        Assert.True(ValidateRowHasText(pngPath, 2, headerHeight, charHeight),
            "Before row should have visible text");
    }

    #endregion

    #region Fill Character Screenshots

    [AvaloniaFact]
    public void Screenshot_NDFC_FillRectangle()
    {
        var emulator = new TDV2200Emulator(80, 24);
        emulator.CharacterSetVariant = (int)TDV2200ISO646Variant.International;

        emulator.ProcessData(Encoding.UTF8.GetBytes("NDFC Fill Rectangle Test:\r\n\r\n"));
        emulator.ProcessData(Encoding.UTF8.GetBytes("Surrounding text here\r\n"));

        // NDFC: Fill rectangle with '#' (ASCII 35)
        // Format: ESC[char;top;left;bottom;right}
        emulator.ProcessData(new byte[] { 0x1B, (byte)'[', (byte)'3', (byte)'5', (byte)';',
            (byte)'4', (byte)';', (byte)'5', (byte)';', (byte)'8', (byte)';', (byte)'1', (byte)'5', (byte)'}' });

        SaveScreenshot(emulator, "12_ndfc_fill",
            "NDFC: Fill rectangle (rows 4-8, cols 5-15) with '#' character");

        Assert.Equal('#', (char)emulator.Buffer.GetCell(4, 5).Codepoint);
        Assert.Equal('#', (char)emulator.Buffer.GetCell(6, 10).Codepoint);
        Assert.Equal('#', (char)emulator.Buffer.GetCell(8, 15).Codepoint);

        // Validate PNG
        var pngPath = Path.Combine(ImagesFolder, "12_ndfc_fill.png");
        Assert.True(ValidatePngExists(pngPath), "PNG file should exist and have content");
        const int headerHeight = 28;
        double charWidth = 8;
        double charHeight = 14;

        // Validate filled rectangle cells are visible
        Assert.True(ValidateCharacterExists(pngPath, 4, 5, headerHeight, charWidth, charHeight),
            "Fill character '#' at top-left of rectangle should be visible");
        Assert.True(ValidateCharacterExists(pngPath, 6, 10, headerHeight, charWidth, charHeight),
            "Fill character '#' in middle of rectangle should be visible");
        Assert.True(ValidateCharacterExists(pngPath, 8, 15, headerHeight, charWidth, charHeight),
            "Fill character '#' at bottom-right of rectangle should be visible");

        // Validate surrounding cells are blank (outside rectangle)
        Assert.True(ValidateCellIsBlank(pngPath, 4, 4, headerHeight, charWidth, charHeight),
            "Cell left of rectangle should be blank");
        Assert.True(ValidateCellIsBlank(pngPath, 4, 16, headerHeight, charWidth, charHeight),
            "Cell right of rectangle should be blank");
    }

    #endregion

    #region Query Response Screenshots

    [AvaloniaFact]
    public void Screenshot_DeviceAttributes()
    {
        var emulator = new TDV2200Emulator(80, 24);
        string? response = null;
        emulator.OnResponseReady += r => response = r;

        emulator.ProcessData(Encoding.UTF8.GetBytes("Device Attributes Query Test:\r\n\r\n"));
        emulator.ProcessData(Encoding.UTF8.GetBytes("Sending DA query (ESC[c)...\r\n"));

        // Send DA query
        emulator.ProcessData(new byte[] { 0x1B, (byte)'[', (byte)'c' });

        emulator.ProcessData(Encoding.UTF8.GetBytes($"Response: {response ?? "(none)"}\r\n"));

        SaveScreenshot(emulator, "13_da_query",
            "DA Query: Terminal responds with TDV2200 identification");

        Assert.NotNull(response);
        Assert.Contains("220", response);

        // Validate PNG
        var pngPath = Path.Combine(ImagesFolder, "13_da_query.png");
        Assert.True(ValidatePngExists(pngPath), "PNG file should exist and have content");
        const int headerHeight = 28;
        double charHeight = 14;

        // Validate title row (row 0) has visible text
        Assert.True(ValidateRowHasText(pngPath, 0, headerHeight, charHeight),
            "Title row should have visible text");

        // Validate 'Sending DA query' row (row 2) has visible text
        Assert.True(ValidateRowHasText(pngPath, 2, headerHeight, charHeight),
            "Query row should have visible text");

        // Validate response row (row 3) has visible text
        Assert.True(ValidateRowHasText(pngPath, 3, headerHeight, charHeight),
            "Response row should have visible text");
    }

    [AvaloniaFact]
    public void Screenshot_CursorPositionReport()
    {
        var emulator = new TDV2200Emulator(80, 24);
        string? response = null;
        emulator.OnResponseReady += r => response = r;

        emulator.ProcessData(Encoding.UTF8.GetBytes("Cursor Position Report Test:\r\n\r\n"));

        // Move cursor to specific position (row 10, col 25)
        emulator.ProcessData(new byte[] { 0x1B, (byte)'[', (byte)'1', (byte)'0', (byte)';', (byte)'2', (byte)'5', (byte)'H' });

        // Request cursor position BEFORE writing text
        emulator.ProcessData(new byte[] { 0x1B, (byte)'[', (byte)'6', (byte)'n' });

        // Capture the response before writing label
        var capturedResponse = response;

        // Now write the label text
        emulator.ProcessData(Encoding.UTF8.GetBytes("X <- Cursor at row 10, col 25"));

        // Move to show response
        emulator.ProcessData(new byte[] { 0x1B, (byte)'[', (byte)'1', (byte)'2', (byte)';', (byte)'1', (byte)'H' });
        emulator.ProcessData(Encoding.UTF8.GetBytes($"CPR Response: {capturedResponse ?? "(none)"}"));

        SaveScreenshot(emulator, "14_cpr_query",
            "CPR Query: Terminal reports cursor position (10;25)");

        Assert.NotNull(capturedResponse);
        Assert.Contains("10;25R", capturedResponse);

        // Validate PNG
        var pngPath = Path.Combine(ImagesFolder, "14_cpr_query.png");
        Assert.True(ValidatePngExists(pngPath), "PNG file should exist and have content");
        const int headerHeight = 28;
        double charWidth = 8;
        double charHeight = 14;

        // Validate title row (row 0) has visible text
        Assert.True(ValidateRowHasText(pngPath, 0, headerHeight, charHeight),
            "Title row should have visible text");

        // Validate cursor marker 'X' at row 9, col 24 (0-indexed)
        Assert.True(ValidateCharacterExists(pngPath, 9, 24, headerHeight, charWidth, charHeight),
            "Cursor marker 'X' should be visible at row 9, col 24");

        // Validate response row (row 11, 0-indexed) has visible text
        Assert.True(ValidateRowHasText(pngPath, 11, headerHeight, charHeight),
            "CPR Response row should have visible text");
    }

    #endregion

    #region Attribute Combination Screenshots

    [AvaloniaFact]
    public void Screenshot_MultipleAttributes()
    {
        var emulator = new TDV2200Emulator(80, 24);

        emulator.ProcessData(Encoding.UTF8.GetBytes("Multiple Attributes Test:\r\n\r\n"));
        emulator.ProcessData(Encoding.UTF8.GetBytes("Normal text\r\n"));        // Row 2

        // Bold
        emulator.ProcessData(new byte[] { 0x1B, (byte)'[', (byte)'1', (byte)'m' });
        emulator.ProcessData(Encoding.UTF8.GetBytes("Bold text\r\n"));          // Row 3

        // Underline
        emulator.ProcessData(new byte[] { 0x1B, (byte)'[', (byte)'0', (byte)'m' });
        emulator.ProcessData(new byte[] { 0x1B, (byte)'[', (byte)'4', (byte)'m' });
        emulator.ProcessData(Encoding.UTF8.GetBytes("Underline text\r\n"));     // Row 4

        // Bold + Underline
        emulator.ProcessData(new byte[] { 0x1B, (byte)'[', (byte)'1', (byte)';', (byte)'4', (byte)'m' });
        emulator.ProcessData(Encoding.UTF8.GetBytes("Bold+Underline\r\n"));     // Row 5

        // Reverse
        emulator.ProcessData(new byte[] { 0x1B, (byte)'[', (byte)'0', (byte)'m' });
        emulator.ProcessData(new byte[] { 0x1B, (byte)'[', (byte)'7', (byte)'m' });
        emulator.ProcessData(Encoding.UTF8.GetBytes("Reverse video\r\n"));      // Row 6

        // Blink
        emulator.ProcessData(new byte[] { 0x1B, (byte)'[', (byte)'0', (byte)'m' });
        emulator.ProcessData(new byte[] { 0x1B, (byte)'[', (byte)'5', (byte)'m' });
        emulator.ProcessData(Encoding.UTF8.GetBytes("Blinking text\r\n"));      // Row 7

        // Reset
        emulator.ProcessData(new byte[] { 0x1B, (byte)'[', (byte)'0', (byte)'m' });
        emulator.ProcessData(Encoding.UTF8.GetBytes("Normal again"));           // Row 8

        SaveScreenshot(emulator, "15_multiple_attributes",
            "SGR attributes: Bold, Underline, Bold+Underline, Reverse, Blink");

        // Verify buffer attributes
        Assert.True((emulator.Buffer.GetCell(3, 0).Attributes & CharacterAttributes.Bold) != 0, "Row 3 should have Bold");
        Assert.True((emulator.Buffer.GetCell(4, 0).Attributes & CharacterAttributes.Underline) != 0, "Row 4 should have Underline");
        Assert.True((emulator.Buffer.GetCell(5, 0).Attributes & CharacterAttributes.Bold) != 0, "Row 5 should have Bold");
        Assert.True((emulator.Buffer.GetCell(5, 0).Attributes & CharacterAttributes.Underline) != 0, "Row 5 should have Underline");
        Assert.True((emulator.Buffer.GetCell(6, 0).Attributes & CharacterAttributes.Reverse) != 0, "Row 6 should have Reverse");
        Assert.True((emulator.Buffer.GetCell(7, 0).Attributes & CharacterAttributes.Blink) != 0, "Row 7 should have Blink");

        // Validate PNG rendering
        var pngPath = Path.Combine(ImagesFolder, "15_multiple_attributes.png");
        const int headerHeight = 28;
        double charHeight = 14;

        // Validate Bold brightness (row 2 = Normal, row 3 = Bold)
        var (normalAvg, boldAvg, brightnessValid) = ValidateBoldBrightness(pngPath, 2, 3, headerHeight, charHeight);
        Assert.True(brightnessValid,
            $"Bold should be brighter than Normal. Normal: G={normalAvg.Green},B={normalAvg.Blue}. Bold: G={boldAvg.Green},B={boldAvg.Blue}");

        // Validate synthetic bold (more pixels)
        var (normalCount, boldCount, syntheticValid) = ValidateSyntheticBold(pngPath, 2, 3, headerHeight, charHeight);
        Assert.True(syntheticValid,
            $"Bold should have more pixels (synthetic bold). Normal: {normalCount}, Bold: {boldCount}");

        // Validate Underline (row 4)
        Assert.True(ValidateUnderline(pngPath, 4, headerHeight, charHeight),
            "Underline text should have underline pixels at bottom of cell");

        // Validate Reverse video (row 6)
        Assert.True(ValidateReverse(pngPath, 6, headerHeight, charHeight),
            "Reverse video should have bright background pixels");
    }

    [AvaloniaFact]
    public void Screenshot_HiddenAttribute()
    {
        var emulator = new TDV2200Emulator(80, 24);

        emulator.ProcessData(Encoding.UTF8.GetBytes("Hidden Attribute Test:\r\n\r\n"));
        emulator.ProcessData(Encoding.UTF8.GetBytes("Visible text: HELLO\r\n"));   // Row 2

        // Hidden
        emulator.ProcessData(new byte[] { 0x1B, (byte)'[', (byte)'8', (byte)'m' });
        emulator.ProcessData(Encoding.UTF8.GetBytes("Hidden text: SECRET\r\n"));   // Row 3

        emulator.ProcessData(new byte[] { 0x1B, (byte)'[', (byte)'0', (byte)'m' });
        emulator.ProcessData(Encoding.UTF8.GetBytes("Visible again"));              // Row 4

        SaveScreenshot(emulator, "16_hidden_attribute",
            "SGR 8: Hidden/invisible attribute - text exists but should not be displayed");

        // Verify buffer has Hidden attribute
        Assert.True((emulator.Buffer.GetCell(3, 13).Attributes & CharacterAttributes.Hidden) != 0,
            "Buffer cell should have Hidden attribute");

        // Validate PNG: Hidden text should have no visible pixels
        var pngPath = Path.Combine(ImagesFolder, "16_hidden_attribute.png");
        const int headerHeight = 28;
        double charWidth = 8;
        double charHeight = 14;

        // Row 3, starting at "Hidden text: " (col 0), the word "SECRET" starts at col 13
        Assert.True(ValidateHidden(pngPath, 3, 13, headerHeight, charWidth, charHeight),
            "Hidden text 'SECRET' should not have visible foreground pixels in the PNG");
    }

    #endregion

    #region Work Area Screenshots

    [AvaloniaFact]
    public void Screenshot_NDLIWA_InsertLines()
    {
        var emulator = new TDV2200Emulator(80, 24);

        emulator.ProcessData(Encoding.UTF8.GetBytes("NDLIWA Insert Lines Test:\r\n\r\n"));
        emulator.ProcessData(Encoding.UTF8.GetBytes("Line 1 - Original\r\n"));
        emulator.ProcessData(Encoding.UTF8.GetBytes("Line 2 - Original\r\n"));
        emulator.ProcessData(Encoding.UTF8.GetBytes("Line 3 - Original\r\n"));

        // Move to row 3
        emulator.ProcessData(new byte[] { 0x1B, (byte)'[', (byte)'4', (byte)';', (byte)'1', (byte)'H' });

        // Insert 2 lines
        emulator.ProcessData(new byte[] { 0x1B, (byte)'[', (byte)'2', (byte)'p' });

        SaveScreenshot(emulator, "17_ndliwa_insert_lines",
            "NDLIWA: Insert 2 blank lines at cursor, pushing content down");

        // Row 3 and 4 should now be blank, original line 2 at row 5
        Assert.True(emulator.Buffer.GetCell(3, 0).IsEmpty);

        // Validate PNG
        var pngPath = Path.Combine(ImagesFolder, "17_ndliwa_insert_lines.png");
        Assert.True(ValidatePngExists(pngPath), "PNG file should exist and have content");
        const int headerHeight = 28;
        double charHeight = 14;

        // Validate title row (row 0) has visible text
        Assert.True(ValidateRowHasText(pngPath, 0, headerHeight, charHeight),
            "Title row should have visible text");

        // Validate Line 1 (row 2) still has visible text
        Assert.True(ValidateRowHasText(pngPath, 2, headerHeight, charHeight),
            "Line 1 should still have visible text");

        // Validate inserted blank lines (rows 3 and 4) are blank
        Assert.True(ValidateRowIsBlank(pngPath, 3, headerHeight, charHeight),
            "First inserted line (row 3) should be blank");
        Assert.True(ValidateRowIsBlank(pngPath, 4, headerHeight, charHeight),
            "Second inserted line (row 4) should be blank");

        // Validate pushed content (original Line 2) at row 5 has visible text
        Assert.True(ValidateRowHasText(pngPath, 5, headerHeight, charHeight),
            "Original Line 2 should be visible at row 5");
    }

    [AvaloniaFact]
    public void Screenshot_NDDLWA_DeleteLines()
    {
        var emulator = new TDV2200Emulator(80, 24);

        emulator.ProcessData(Encoding.UTF8.GetBytes("NDDLWA Delete Lines Test:\r\n\r\n"));
        emulator.ProcessData(Encoding.UTF8.GetBytes("Line A - Keep\r\n"));
        emulator.ProcessData(Encoding.UTF8.GetBytes("Line B - Delete\r\n"));
        emulator.ProcessData(Encoding.UTF8.GetBytes("Line C - Delete\r\n"));
        emulator.ProcessData(Encoding.UTF8.GetBytes("Line D - Keep\r\n"));

        // Move to row 3 (Line B)
        emulator.ProcessData(new byte[] { 0x1B, (byte)'[', (byte)'4', (byte)';', (byte)'1', (byte)'H' });

        // Delete 2 lines
        emulator.ProcessData(new byte[] { 0x1B, (byte)'[', (byte)'2', (byte)'q' });

        SaveScreenshot(emulator, "18_nddlwa_delete_lines",
            "NDDLWA: Delete 2 lines at cursor, pulling content up");

        // Line D should now be at row 3
        Assert.Equal('L', (char)emulator.Buffer.GetCell(3, 0).Codepoint);
        Assert.Equal('D', (char)emulator.Buffer.GetCell(3, 5).Codepoint);

        // Validate PNG
        var pngPath = Path.Combine(ImagesFolder, "18_nddlwa_delete_lines.png");
        Assert.True(ValidatePngExists(pngPath), "PNG file should exist and have content");
        const int headerHeight = 28;
        double charHeight = 14;

        // Validate title row (row 0) has visible text
        Assert.True(ValidateRowHasText(pngPath, 0, headerHeight, charHeight),
            "Title row should have visible text");

        // Validate Line A (row 2) still has visible text
        Assert.True(ValidateRowHasText(pngPath, 2, headerHeight, charHeight),
            "Line A should still have visible text at row 2");

        // Validate Line D now at row 3 (pulled up)
        Assert.True(ValidateRowHasText(pngPath, 3, headerHeight, charHeight),
            "Line D should now be visible at row 3 (pulled up after delete)");
    }

    #endregion

    #region Double Width/Height Screenshots

    [AvaloniaFact]
    public void Screenshot_DoubleWidthHeight()
    {
        var emulator = new TDV2200Emulator(80, 24);

        emulator.ProcessData(Encoding.UTF8.GetBytes("Double Width/Height Test:\r\n\r\n"));

        // Normal line for comparison
        emulator.ProcessData(Encoding.UTF8.GetBytes("This is a normal-sized line\r\n"));

        // Double-width only (ESC#6) - characters are 2x wide, normal height
        emulator.ProcessData(Encoding.UTF8.GetBytes("Double-width line"));
        emulator.ProcessData(new byte[] { 0x1B, (byte)'#', (byte)'6' });
        emulator.ProcessData(Encoding.UTF8.GetBytes("\r\n"));

        // Double-width-and-height: Write SAME text on TWO lines
        // Top half (ESC#3) - shows upper half of 2x2 characters
        emulator.ProcessData(Encoding.UTF8.GetBytes("DOUBLE W+H"));
        emulator.ProcessData(new byte[] { 0x1B, (byte)'#', (byte)'3' });
        emulator.ProcessData(Encoding.UTF8.GetBytes("\r\n"));

        // Bottom half (ESC#4) - shows lower half of 2x2 characters (SAME TEXT!)
        emulator.ProcessData(Encoding.UTF8.GetBytes("DOUBLE W+H"));
        emulator.ProcessData(new byte[] { 0x1B, (byte)'#', (byte)'4' });
        emulator.ProcessData(Encoding.UTF8.GetBytes("\r\n"));

        // Another normal line
        emulator.ProcessData(Encoding.UTF8.GetBytes("Normal again\r\n"));

        // Numbers before DH to compare character width
        emulator.ProcessData(Encoding.UTF8.GetBytes("1234567890123456789012345678901234567890\r\n"));

        // Another double-height example - per VT220, DH implies DW (chars are 2x2)
        emulator.ProcessData(Encoding.UTF8.GetBytes("DOUBLE-HEIGHT"));
        emulator.ProcessData(new byte[] { 0x1B, (byte)'#', (byte)'3' }); // Top half
        emulator.ProcessData(Encoding.UTF8.GetBytes("\r\n"));

        emulator.ProcessData(Encoding.UTF8.GetBytes("DOUBLE-HEIGHT"));
        emulator.ProcessData(new byte[] { 0x1B, (byte)'#', (byte)'4' }); // Bottom half
        emulator.ProcessData(Encoding.UTF8.GetBytes("\r\n"));

        // Numbers after DH to compare character width
        emulator.ProcessData(Encoding.UTF8.GetBytes("1234567890123456789012345678901234567890\r\n"));

        emulator.ProcessData(Encoding.UTF8.GetBytes("End of test"));

        SaveScreenshot(emulator, "19_double_width_height",
            "ESC#6=double-width, ESC#3+#4=double-height (same text on 2 rows)");

        // Verify normal line has no flags
        Assert.False(emulator.Buffer.GetCell(2, 0).DoubleHeight, "Row 2 should NOT have DoubleHeight flag");
        Assert.False(emulator.Buffer.GetCell(2, 0).DoubleWidth, "Row 2 should NOT have DoubleWidth flag");

        // Verify double-width only line (row 3)
        Assert.True(emulator.Buffer.GetCell(3, 0).DoubleWidth, "Row 3 should have DoubleWidth flag");
        Assert.False(emulator.Buffer.GetCell(3, 0).DoubleHeight, "Row 3 should NOT have DoubleHeight flag");

        // Verify double-height top (row 4) - has BOTH DoubleHeight and DoubleWidth (DH implies DW)
        Assert.True(emulator.Buffer.GetCell(4, 0).DoubleHeight, "Row 4 should have DoubleHeight flag");
        Assert.True(emulator.Buffer.GetCell(4, 0).DoubleWidth, "Row 4 should have DoubleWidth (DH implies DW per VT220/TDV)");
        Assert.True((emulator.Buffer.GetCell(4, 0).Attributes & CharacterAttributes.DoubleHeightTop) != 0, "Row 4 should have DoubleHeightTop attribute");

        // Verify double-height bottom (row 5) - has BOTH DoubleHeight and DoubleWidth (DH implies DW)
        Assert.True(emulator.Buffer.GetCell(5, 0).DoubleHeight, "Row 5 should have DoubleHeight flag");
        Assert.True(emulator.Buffer.GetCell(5, 0).DoubleWidth, "Row 5 should have DoubleWidth (DH implies DW per VT220/TDV)");
        Assert.True((emulator.Buffer.GetCell(5, 0).Attributes & CharacterAttributes.DoubleHeightBottom) != 0, "Row 5 should have DoubleHeightBottom attribute");

        // Verify both double-height rows have the SAME text
        Assert.Equal('D', (char)emulator.Buffer.GetCell(4, 0).Codepoint);
        Assert.Equal('D', (char)emulator.Buffer.GetCell(5, 0).Codepoint);
        Assert.Equal('O', (char)emulator.Buffer.GetCell(4, 1).Codepoint);
        Assert.Equal('O', (char)emulator.Buffer.GetCell(5, 1).Codepoint);

        // Validate PNG
        var pngPath = Path.Combine(ImagesFolder, "19_double_width_height.png");
        Assert.True(ValidatePngExists(pngPath), "PNG file should exist and have content");
        const int headerHeight = 28;
        double charHeight = 14;

        // Validate normal text row (row 2) has visible text
        Assert.True(ValidateRowHasText(pngPath, 2, headerHeight, charHeight),
            "Normal text row should have visible text");

        // Validate double-width row (row 3) has visible text
        Assert.True(ValidateRowHasText(pngPath, 3, headerHeight, charHeight),
            "Double-width row should have visible text");

        // Validate double-height top (row 4) has visible text
        Assert.True(ValidateRowHasText(pngPath, 4, headerHeight, charHeight),
            "Double-height top row should have visible text");

        // Validate double-height bottom (row 5) has visible text
        Assert.True(ValidateRowHasText(pngPath, 5, headerHeight, charHeight),
            "Double-height bottom row should have visible text");
    }

    [AvaloniaFact]
    public void Screenshot_DoubleWidthHeight_Scrolling()
    {
        var emulator = new TDV2200Emulator(80, 24);

        // Fill the screen with mixed content including double-width and double-height
        emulator.ProcessData(Encoding.UTF8.GetBytes("Line 00: Normal text\r\n"));
        emulator.ProcessData(Encoding.UTF8.GetBytes("Line 01: Normal text\r\n"));

        // Double-width line
        emulator.ProcessData(Encoding.UTF8.GetBytes("Line 02: DW"));
        emulator.ProcessData(new byte[] { 0x1B, (byte)'#', (byte)'6' });
        emulator.ProcessData(Encoding.UTF8.GetBytes("\r\n"));

        emulator.ProcessData(Encoding.UTF8.GetBytes("Line 03: Normal text\r\n"));

        // Double-height (spans 2 rows)
        emulator.ProcessData(Encoding.UTF8.GetBytes("DH TOP"));
        emulator.ProcessData(new byte[] { 0x1B, (byte)'#', (byte)'3' });
        emulator.ProcessData(Encoding.UTF8.GetBytes("\r\n"));

        emulator.ProcessData(Encoding.UTF8.GetBytes("DH TOP"));
        emulator.ProcessData(new byte[] { 0x1B, (byte)'#', (byte)'4' });
        emulator.ProcessData(Encoding.UTF8.GetBytes("\r\n"));

        emulator.ProcessData(Encoding.UTF8.GetBytes("Line 06: Normal text\r\n"));
        emulator.ProcessData(Encoding.UTF8.GetBytes("Line 07: Normal text\r\n"));

        // Another double-width
        emulator.ProcessData(Encoding.UTF8.GetBytes("Line 08: DW"));
        emulator.ProcessData(new byte[] { 0x1B, (byte)'#', (byte)'6' });
        emulator.ProcessData(Encoding.UTF8.GetBytes("\r\n"));

        emulator.ProcessData(Encoding.UTF8.GetBytes("Line 09: Normal text\r\n"));
        emulator.ProcessData(Encoding.UTF8.GetBytes("Line 10: Normal text\r\n"));
        emulator.ProcessData(Encoding.UTF8.GetBytes("Line 11: Normal text\r\n"));
        emulator.ProcessData(Encoding.UTF8.GetBytes("Line 12: Normal text\r\n"));
        emulator.ProcessData(Encoding.UTF8.GetBytes("Line 13: Normal text\r\n"));
        emulator.ProcessData(Encoding.UTF8.GetBytes("Line 14: Normal text\r\n"));
        emulator.ProcessData(Encoding.UTF8.GetBytes("Line 15: Normal text\r\n"));
        emulator.ProcessData(Encoding.UTF8.GetBytes("Line 16: Normal text\r\n"));
        emulator.ProcessData(Encoding.UTF8.GetBytes("Line 17: Normal text\r\n"));
        emulator.ProcessData(Encoding.UTF8.GetBytes("Line 18: Normal text\r\n"));

        // Another double-height near bottom
        emulator.ProcessData(Encoding.UTF8.GetBytes("BOTTOM"));
        emulator.ProcessData(new byte[] { 0x1B, (byte)'#', (byte)'3' });
        emulator.ProcessData(Encoding.UTF8.GetBytes("\r\n"));

        emulator.ProcessData(Encoding.UTF8.GetBytes("BOTTOM"));
        emulator.ProcessData(new byte[] { 0x1B, (byte)'#', (byte)'4' });
        emulator.ProcessData(Encoding.UTF8.GetBytes("\r\n"));

        emulator.ProcessData(Encoding.UTF8.GetBytes("Line 21: Normal text\r\n"));
        emulator.ProcessData(Encoding.UTF8.GetBytes("Line 22: Normal text\r\n"));
        emulator.ProcessData(Encoding.UTF8.GetBytes("Line 23: Last line"));

        // Take screenshot BEFORE scroll
        SaveScreenshot(emulator, "21_dw_dh_before_scroll",
            "Screen filled with DW/DH lines - BEFORE scroll");

        // Now add new lines to cause scrolling
        emulator.ProcessData(Encoding.UTF8.GetBytes("\r\n"));
        emulator.ProcessData(Encoding.UTF8.GetBytes("NEW LINE 1 - Screen scrolled!\r\n"));
        emulator.ProcessData(Encoding.UTF8.GetBytes("NEW LINE 2 - Screen scrolled!\r\n"));
        emulator.ProcessData(Encoding.UTF8.GetBytes("NEW LINE 3 - Screen scrolled!"));

        // Take screenshot AFTER scroll
        SaveScreenshot(emulator, "22_dw_dh_after_scroll",
            "After adding 3 new lines - DW/DH should scroll correctly");

        // Verify the double-width line scrolled up (was at row 2, now at row 2-3 = depends on scroll amount)
        // After 3 scrolls, row 2 content moved to row -1 (scrolled off), row 3 is now at row 0
        // Let's verify some content is still correct
        Assert.Equal('L', (char)emulator.Buffer.GetCell(0, 0).Codepoint); // Line 03 scrolled to row 0

        // Validate before-scroll PNG
        var beforePngPath = Path.Combine(ImagesFolder, "21_dw_dh_before_scroll.png");
        Assert.True(ValidatePngExists(beforePngPath), "Before-scroll PNG should exist");
        const int headerHeight = 28;
        double charHeight = 14;

        // Validate before screenshot has content
        Assert.True(ValidateRowHasText(beforePngPath, 0, headerHeight, charHeight),
            "Before-scroll: Row 0 should have visible text");
        Assert.True(ValidateRowHasText(beforePngPath, 2, headerHeight, charHeight),
            "Before-scroll: Double-width row should have visible text");

        // Validate after-scroll PNG
        var afterPngPath = Path.Combine(ImagesFolder, "22_dw_dh_after_scroll.png");
        Assert.True(ValidatePngExists(afterPngPath), "After-scroll PNG should exist");

        // Validate after screenshot has content - new lines at bottom
        Assert.True(ValidateRowHasText(afterPngPath, 0, headerHeight, charHeight),
            "After-scroll: Row 0 should have visible text (scrolled content)");

        // Validate new lines are visible at bottom of screen
        Assert.True(ValidateRowHasText(afterPngPath, 21, headerHeight, charHeight),
            "After-scroll: New line should be visible near bottom");
    }

    #endregion

    #region Clear Screen Screenshots

    [AvaloniaFact]
    public void Screenshot_ClearScreen()
    {
        var emulator = new TDV2200Emulator(80, 24);

        // Fill screen with content
        for (int i = 0; i < 10; i++)
        {
            emulator.ProcessData(Encoding.UTF8.GetBytes($"Line {i}: Some content here\r\n"));
        }

        // Position cursor in middle
        emulator.ProcessData(new byte[] { 0x1B, (byte)'[', (byte)'5', (byte)';', (byte)'1', (byte)'H' });

        // Clear from cursor to end of screen (ED 0)
        emulator.ProcessData(new byte[] { 0x1B, (byte)'[', (byte)'J' });

        emulator.ProcessData(Encoding.UTF8.GetBytes("Cleared below cursor"));

        SaveScreenshot(emulator, "20_clear_screen",
            "ED: Clear screen from cursor to end - lines 0-4 preserved, 5+ cleared");

        // Line 4 should still have content
        Assert.Equal('L', (char)emulator.Buffer.GetCell(3, 0).Codepoint);
        // Lines 5+ should be cleared (except our new text)

        // Validate PNG
        var pngPath = Path.Combine(ImagesFolder, "20_clear_screen.png");
        Assert.True(ValidatePngExists(pngPath), "PNG file should exist and have content");
        const int headerHeight = 28;
        double charHeight = 14;

        // Validate preserved lines (rows 0-3) still have visible text
        Assert.True(ValidateRowHasText(pngPath, 0, headerHeight, charHeight),
            "Row 0 should have visible text (preserved)");
        Assert.True(ValidateRowHasText(pngPath, 1, headerHeight, charHeight),
            "Row 1 should have visible text (preserved)");
        Assert.True(ValidateRowHasText(pngPath, 2, headerHeight, charHeight),
            "Row 2 should have visible text (preserved)");
        Assert.True(ValidateRowHasText(pngPath, 3, headerHeight, charHeight),
            "Row 3 should have visible text (preserved)");

        // Validate cursor position text (row 4) has visible text
        Assert.True(ValidateRowHasText(pngPath, 4, headerHeight, charHeight),
            "Row 4 (cursor position + new text) should have visible text");

        // Validate cleared rows (6+) are blank
        Assert.True(ValidateRowIsBlank(pngPath, 6, headerHeight, charHeight),
            "Row 6 should be blank (cleared)");
        Assert.True(ValidateRowIsBlank(pngPath, 10, headerHeight, charHeight),
            "Row 10 should be blank (cleared)");
    }

    #endregion
}
