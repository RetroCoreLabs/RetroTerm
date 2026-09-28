using System;
using System.Collections.Generic;
using System.IO;

namespace RetroTerm.Desktop.Fonts;

/// <summary>
/// System font fallback for TDV terminals
/// Provides TrueType/OpenType font support when ROM fonts are unavailable
/// </summary>
public class SystemFont
{
    private readonly string _fontFamily;
    private readonly int _fontSize;
    private readonly bool _isBold;
    private readonly bool _isItalic;
    private readonly Dictionary<int, byte[]> _characterCache;

    public string FontFamily => _fontFamily;
    public int FontSize => _fontSize;
    public bool IsBold => _isBold;
    public bool IsItalic => _isItalic;

    public SystemFont(string fontFamily = "Consolas", int fontSize = 14, bool isBold = false, bool isItalic = false)
    {
        _fontFamily = fontFamily;
        _fontSize = fontSize;
        _isBold = isBold;
        _isItalic = isItalic;
        _characterCache = new Dictionary<int, byte[]>();
    }

    /// <summary>
    /// Gets the bitmap data for a character
    /// </summary>
    public byte[] GetCharacterBitmap(int characterCode)
    {
        if (_characterCache.TryGetValue(characterCode, out var cached))
        {
            return cached;
        }

        var bitmap = GenerateCharacterBitmap(characterCode);
        _characterCache[characterCode] = bitmap;
        return bitmap;
    }

    /// <summary>
    /// Generates bitmap data for a character using system font
    /// </summary>
    private byte[] GenerateCharacterBitmap(int characterCode)
    {
        try
        {
            // Try to use SkiaSharp for actual font rendering if available
            return GenerateCharacterBitmapWithSkia(characterCode);
        }
        catch
        {
            // Fallback to simple pattern generation
            return GenerateSimplePattern(characterCode);
        }
    }

    /// <summary>
    /// Generates character bitmap using SkiaSharp
    /// </summary>
    private byte[] GenerateCharacterBitmapWithSkia(int characterCode)
    {
        // For now, return the simple pattern until SkiaSharp is properly integrated
        // TODO: Implement actual SkiaSharp font rendering
        return GenerateSimplePattern(characterCode);
    }

    /// <summary>
    /// Generates a simple pattern bitmap as fallback
    /// </summary>
    private byte[] GenerateSimplePattern(int characterCode)
    {
        var width = GetCharacterWidth();
        var height = GetCharacterHeight();
        var bitmap = new byte[width * height];

        // Generate a simple pattern based on character code
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                var index = y * width + x;
                var pattern = (characterCode + x + y) % 8;
                bitmap[index] = (byte)(pattern > 4 ? 0xFF : 0x00);
            }
        }

        return bitmap;
    }

    /// <summary>
    /// Renders a character to a bitmap
    /// </summary>
    public void RenderCharacter(int characterCode, byte[] outputBuffer, int x, int y, int outputWidth, int outputHeight)
    {
        var characterBitmap = GetCharacterBitmap(characterCode);
        var width = GetCharacterWidth();
        var height = GetCharacterHeight();

        for (int row = 0; row < height; row++)
        {
            for (int col = 0; col < width; col++)
            {
                var pixelIndex = row * width + col;

                if (pixelIndex < characterBitmap.Length)
                {
                    var pixel = characterBitmap[pixelIndex] > 0;

                    var outputX = x + col;
                    var outputY = y + row;

                    if (outputX >= 0 && outputX < outputWidth && outputY >= 0 && outputY < outputHeight)
                    {
                        var outputIndex = outputY * outputWidth + outputX;
                        if (outputIndex < outputBuffer.Length)
                        {
                            outputBuffer[outputIndex] = pixel ? (byte)0xFF : (byte)0x00;
                        }
                    }
                }
            }
        }
    }

    /// <summary>
    /// Gets character width
    /// </summary>
    public int GetCharacterWidth(int characterCode)
    {
        // TODO: Implement actual character width calculation
        return 8; // Fixed width for now
    }

    /// <summary>
    /// Gets character height
    /// </summary>
    public int GetCharacterHeight()
    {
        // TODO: Implement actual character height calculation
        return _fontSize; // Use font size as height
    }

    /// <summary>
    /// Gets character width (overloaded version for compatibility)
    /// </summary>
    public int GetCharacterWidth()
    {
        return _fontSize; // Use font size as width for monospace
    }

    /// <summary>
    /// Checks if a character is available in this font
    /// </summary>
    public bool HasCharacter(int characterCode)
    {
        // TODO: Implement actual character availability check
        return characterCode >= 0 && characterCode < 256;
    }

    /// <summary>
    /// Gets font metrics
    /// </summary>
    public SystemFontMetrics GetFontMetrics()
    {
        return new SystemFontMetrics
        {
            FontFamily = _fontFamily,
            FontSize = _fontSize,
            IsBold = _isBold,
            IsItalic = _isItalic,
            Width = GetCharacterWidth(),
            Height = GetCharacterHeight()
        };
    }

    /// <summary>
    /// Clears the character cache
    /// </summary>
    public void ClearCache()
    {
        _characterCache.Clear();
    }

    /// <summary>
    /// Gets cache statistics
    /// </summary>
    public (int CachedCharacters, long MemoryUsage) GetCacheStats()
    {
        var memoryUsage = _characterCache.Count * 8 * 16; // Approximate memory usage
        return (_characterCache.Count, memoryUsage);
    }

    /// <summary>
    /// Enumerates available system fonts
    /// </summary>
    public static string[] GetAvailableFonts()
    {
        try
        {
            // Try to enumerate actual system fonts
            return EnumerateSystemFonts();
        }
        catch
        {
            // Fallback to hardcoded list
            return GetFallbackFonts();
        }
    }

    /// <summary>
    /// Enumerates actual system fonts
    /// </summary>
    private static string[] EnumerateSystemFonts()
    {
        // TODO: Implement actual system font enumeration
        // This would require platform-specific code or a font enumeration library
        return GetFallbackFonts();
    }

    /// <summary>
    /// Gets fallback font list
    /// </summary>
    private static string[] GetFallbackFonts()
    {
        return new[]
        {
            "Consolas",
            "Courier New",
            "Monaco",
            "Menlo",
            "Liberation Mono",
            "DejaVu Sans Mono",
            "Source Code Pro",
            "Fira Code",
            "JetBrains Mono"
        };
    }

    /// <summary>
    /// Creates a system font with the best available monospace font
    /// </summary>
    public static SystemFont CreateBestMonospaceFont(int fontSize = 14)
    {
        var availableFonts = GetAvailableFonts();

        foreach (var font in availableFonts)
        {
            // TODO: Check if font is actually available on the system
            return new SystemFont(font, fontSize);
        }

        // Fallback to default
        return new SystemFont("Consolas", fontSize);
    }
}

/// <summary>
/// System font metrics
/// </summary>
public class SystemFontMetrics
{
    public string FontFamily { get; set; } = string.Empty;
    public int FontSize { get; set; }
    public bool IsBold { get; set; }
    public bool IsItalic { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
}
