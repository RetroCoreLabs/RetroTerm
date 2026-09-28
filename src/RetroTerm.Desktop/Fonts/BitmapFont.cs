using System;
using System.Collections.Generic;
using System.IO;

namespace RetroTerm.Desktop.Fonts;

/// <summary>
/// ROM-based bitmap font support for TDV terminals
/// Handles font loading, caching, and rendering
/// </summary>
public class BitmapFont
{
    private readonly Dictionary<int, byte[]> _characterCache;
    private readonly TDVFontData _fontData;
    private readonly int _bytesPerCharacter;

    public string Name => _fontData.Name;
    public TDVFontType Type => _fontData.Type;
    public int Width => _fontData.Width;
    public int Height => _fontData.Height;
    public int CharacterCount => _fontData.CharacterCount;

    public BitmapFont(TDVFontData fontData)
    {
        _fontData = fontData;
        _characterCache = new Dictionary<int, byte[]>();
        _bytesPerCharacter = fontData.Width * fontData.Height / 8; // 8 pixels per byte
    }

    /// <summary>
    /// Gets the bitmap data for a character
    /// </summary>
    public byte[] GetCharacterBitmap(int characterCode)
    {
        if (characterCode < 0 || characterCode >= _fontData.CharacterCount)
        {
            return GetDefaultCharacter();
        }

        if (_characterCache.TryGetValue(characterCode, out var cached))
        {
            return cached;
        }

        var bitmap = ExtractCharacterBitmap(characterCode);
        _characterCache[characterCode] = bitmap;
        return bitmap;
    }

    /// <summary>
    /// Extracts bitmap data for a specific character
    /// </summary>
    private byte[] ExtractCharacterBitmap(int characterCode)
    {
        var offset = characterCode * _bytesPerCharacter;

        if (offset + _bytesPerCharacter > _fontData.Data.Length)
        {
            return GetDefaultCharacter();
        }

        var bitmap = new byte[_bytesPerCharacter];
        Array.Copy(_fontData.Data, offset, bitmap, 0, _bytesPerCharacter);

        return bitmap;
    }

    /// <summary>
    /// Gets a default character bitmap (space or block)
    /// </summary>
    private byte[] GetDefaultCharacter()
    {
        var bitmap = new byte[_bytesPerCharacter];

        // Create a simple block character
        for (int i = 0; i < _bytesPerCharacter; i++)
        {
            bitmap[i] = 0xFF; // All pixels on
        }

        return bitmap;
    }

    /// <summary>
    /// Renders a character to a bitmap
    /// </summary>
    public void RenderCharacter(int characterCode, byte[] outputBuffer, int x, int y, int outputWidth, int outputHeight)
    {
        var characterBitmap = GetCharacterBitmap(characterCode);

        for (int row = 0; row < _fontData.Height; row++)
        {
            for (int col = 0; col < _fontData.Width; col++)
            {
                var pixelIndex = row * _fontData.Width + col;
                var byteIndex = pixelIndex / 8;
                var bitIndex = 7 - (pixelIndex % 8);

                if (byteIndex < characterBitmap.Length)
                {
                    var pixel = (characterBitmap[byteIndex] & (1 << bitIndex)) != 0;

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
    /// Gets character width (may vary for proportional fonts)
    /// </summary>
    public int GetCharacterWidth(int characterCode)
    {
        // For now, all characters have the same width
        // TODO: Implement proportional font support
        return _fontData.Width;
    }

    /// <summary>
    /// Gets character width (overloaded version for compatibility)
    /// </summary>
    public int GetCharacterWidth()
    {
        return _fontData.Width;
    }

    /// <summary>
    /// Gets character height
    /// </summary>
    public int GetCharacterHeight()
    {
        return _fontData.Height;
    }

    /// <summary>
    /// Checks if a character is available in this font
    /// </summary>
    public bool HasCharacter(int characterCode)
    {
        return characterCode >= 0 && characterCode < _fontData.CharacterCount;
    }

    /// <summary>
    /// Gets font metrics
    /// </summary>
    public FontMetrics GetFontMetrics()
    {
        return new FontMetrics
        {
            Width = _fontData.Width,
            Height = _fontData.Height,
            CharacterCount = _fontData.CharacterCount,
            BytesPerCharacter = _bytesPerCharacter
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
        var memoryUsage = _characterCache.Count * _bytesPerCharacter;
        return (_characterCache.Count, memoryUsage);
    }
}

/// <summary>
/// Font metrics for bitmap fonts
/// </summary>
public class FontMetrics
{
    public int Width { get; set; }
    public int Height { get; set; }
    public int CharacterCount { get; set; }
    public int BytesPerCharacter { get; set; }
}
