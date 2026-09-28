using System;
using System.Collections.Generic;
using RetroTerm.Core.Fonts;
using SkiaSharp;

namespace RetroTerm.Tests.Avalonia;

/// <summary>
/// Validates that rendered glyphs match expected font definitions by comparing pixel data.
/// </summary>
public class GlyphPixelValidator
{
    private readonly FontTDV2200 _font = new FontTDV2200();

    /// <summary>
    /// Validates that a rendered glyph in a bitmap matches the expected font bits.
    /// </summary>
    /// <param name="bitmap">
    /// The bitmap containing the rendered glyph
    /// </param>
    /// <param name="cellX">
    /// X position of the cell in pixels
    /// </param>
    /// <param name="cellY">
    /// Y position of the cell in pixels
    /// </param>
    /// <param name="cellWidth">
    /// Width of the cell in pixels
    /// </param>
    /// <param name="cellHeight">
    /// Height of the cell in pixels
    /// </param>
    /// <param name="codepoint">
    /// The character codepoint to validate
    /// </param>
    /// <param name="fontNum">
    /// The font number (character set) to use
    /// </param>
    /// <param name="foreground">
    /// Expected foreground color
    /// </param>
    /// <param name="background">
    /// Expected background color
    /// </param>
    /// <returns>
    /// Validation result with any mismatches found
    /// </returns>
    public GlyphValidationResult ValidateGlyph(SKBitmap bitmap, int cellX, int cellY,
        int cellWidth, int cellHeight, uint codepoint, int fontNum, SKColor foreground, SKColor background)
    {
        // Get expected glyph bits from font
        ushort[]? fontBits = _font.GetFontBits((ushort)codepoint, fontNum);
        if (fontBits == null)
        {
            return GlyphValidationResult.NoGlyph(codepoint, fontNum);
        }

        var mismatches = new List<PixelMismatch>();
        int heightToUse = _font.HeightToUse; // 14
        int fontWidth = _font.Width;          // 8

        double scaleX = (double)cellWidth / fontWidth;
        double scaleY = (double)cellHeight / heightToUse;

        for (int row = 0; row < heightToUse && row < fontBits.Length; row++)
        {
            ushort rowBits = fontBits[row];

            for (int col = 0; col < fontWidth; col++)
            {
                bool expectedOn = (rowBits & (1 << (fontWidth - 1 - col))) != 0;

                // Sample center of scaled pixel
                int pixelX = cellX + (int)((col + 0.5) * scaleX);
                int pixelY = cellY + (int)((row + 0.5) * scaleY);

                // Bounds check
                if (pixelX < 0 || pixelX >= bitmap.Width || pixelY < 0 || pixelY >= bitmap.Height)
                {
                    continue;
                }

                SKColor actualColor = bitmap.GetPixel(pixelX, pixelY);
                bool actualOn = IsCloserTo(actualColor, foreground, background);

                if (expectedOn != actualOn)
                {
                    mismatches.Add(new PixelMismatch(row, col, expectedOn, actualOn, pixelX, pixelY));
                }
            }
        }

        return new GlyphValidationResult(codepoint, fontNum, mismatches);
    }

    /// <summary>
    /// Validates FontNumber for a cell matches expected value.
    /// </summary>
    public static FontNumberValidationResult ValidateFontNumber(int row, int col, uint codepoint,
        int actualFontNum, int expectedFontNum)
    {
        if (actualFontNum != expectedFontNum)
        {
            return new FontNumberValidationResult(row, col, codepoint, expectedFontNum, actualFontNum, false);
        }
        return new FontNumberValidationResult(row, col, codepoint, expectedFontNum, actualFontNum, true);
    }

    private bool IsCloserTo(SKColor actual, SKColor foreground, SKColor background)
    {
        int distFg = ColorDistanceSquared(actual, foreground);
        int distBg = ColorDistanceSquared(actual, background);
        return distFg < distBg;
    }

    private int ColorDistanceSquared(SKColor a, SKColor b)
    {
        int dr = a.Red - b.Red;
        int dg = a.Green - b.Green;
        int db = a.Blue - b.Blue;
        return dr * dr + dg * dg + db * db;
    }
}

/// <summary>
/// Result of validating a single glyph.
/// </summary>
public class GlyphValidationResult
{
    public uint Codepoint { get; }
    public int FontNum { get; }
    public List<PixelMismatch> Mismatches { get; }
    public bool IsValid => Mismatches != null && Mismatches.Count == 0;
    public bool HasGlyph { get; }

    public GlyphValidationResult(uint codepoint, int fontNum, List<PixelMismatch> mismatches)
    {
        Codepoint = codepoint;
        FontNum = fontNum;
        Mismatches = mismatches ?? new List<PixelMismatch>();
        HasGlyph = true;
    }

    private GlyphValidationResult(uint codepoint, int fontNum)
    {
        Codepoint = codepoint;
        FontNum = fontNum;
        Mismatches = new List<PixelMismatch>();
        HasGlyph = false;
    }

    public static GlyphValidationResult NoGlyph(uint codepoint, int fontNum)
    {
        return new GlyphValidationResult(codepoint, fontNum);
    }

    /// <returns>
    /// The error description, or null when there is no error.
    /// </returns>
    public string? ToErrorString()
    {
        if (!HasGlyph)
        {
            return $"Glyph 0x{Codepoint:X2} fontNum={FontNum}: no glyph data";
        }

        if (IsValid)
        {
            return null;
        }

        char displayChar = Codepoint >= 0x20 && Codepoint < 0x7F ? (char)Codepoint : '?';
        return $"Glyph 0x{Codepoint:X2} '{displayChar}' fontNum={FontNum}: {Mismatches.Count} pixel mismatches";
    }
}

/// <summary>
/// Represents a single pixel mismatch in glyph validation.
/// </summary>
public class PixelMismatch
{
    public int Row { get; }
    public int Col { get; }
    public bool Expected { get; }
    public bool Actual { get; }
    public int PixelX { get; }
    public int PixelY { get; }

    public PixelMismatch(int row, int col, bool expected, bool actual, int pixelX, int pixelY)
    {
        Row = row;
        Col = col;
        Expected = expected;
        Actual = actual;
        PixelX = pixelX;
        PixelY = pixelY;
    }

    public override string ToString()
    {
        return $"[{Row},{Col}] expected={Expected}, actual={Actual} at pixel ({PixelX},{PixelY})";
    }
}

/// <summary>
/// Result of validating FontNumber for a cell.
/// </summary>
public class FontNumberValidationResult
{
    public int Row { get; }
    public int Col { get; }
    public uint Codepoint { get; }
    public int ExpectedFontNum { get; }
    public int ActualFontNum { get; }
    public bool IsValid { get; }

    public FontNumberValidationResult(int row, int col, uint codepoint, int expectedFontNum, int actualFontNum, bool isValid)
    {
        Row = row;
        Col = col;
        Codepoint = codepoint;
        ExpectedFontNum = expectedFontNum;
        ActualFontNum = actualFontNum;
        IsValid = isValid;
    }

    /// <returns>
    /// The error description, or null when there is no error.
    /// </returns>
    public string? ToErrorString()
    {
        if (IsValid)
        {
            return null;
        }

        char displayChar = Codepoint >= 0x20 && Codepoint < 0x7F ? (char)Codepoint : '?';
        return $"[{Row},{Col}] FontNumber mismatch for 0x{Codepoint:X2} '{displayChar}': expected={ExpectedFontNum}, actual={ActualFontNum}";
    }
}
