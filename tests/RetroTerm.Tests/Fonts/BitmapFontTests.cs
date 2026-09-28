using System;
using RetroTerm.Desktop.Fonts;
using Xunit;

namespace RetroTerm.Tests.Fonts;

/// <summary>
/// Tests for BitmapFont class
/// </summary>
public class BitmapFontTests
{
    [Fact]
    public void BitmapFont_Constructor_ShouldInitializeCorrectly()
    {
        // Arrange
        var fontData = new TDVFontData
        {
            Name = "TestFont",
            Type = TDVFontType.ASCII,
            Width = 8,
            Height = 8,
            Data = new byte[256 * 8], // 256 characters * 8 bytes each
            CharacterCount = 256
        };

        // Act
        var font = new BitmapFont(fontData);

        // Assert
        Assert.Equal("TestFont", font.Name);
        Assert.Equal(TDVFontType.ASCII, font.Type);
        Assert.Equal(8, font.Width);
        Assert.Equal(8, font.Height);
        Assert.Equal(256, font.CharacterCount);
    }

    [Fact]
    public void GetCharacterBitmap_ValidCharacter_ShouldReturnBitmap()
    {
        // Arrange
        var fontData = new TDVFontData
        {
            Name = "TestFont",
            Type = TDVFontType.ASCII,
            Width = 8,
            Height = 8,
            Data = new byte[256 * 8],
            CharacterCount = 256
        };
        var font = new BitmapFont(fontData);

        // Act
        var bitmap = font.GetCharacterBitmap(65); // 'A'

        // Assert
        Assert.NotNull(bitmap);
        Assert.Equal(8, bitmap.Length); // 8 bytes for 8x8 bitmap
    }

    [Fact]
    public void GetCharacterBitmap_InvalidCharacter_ShouldReturnDefault()
    {
        // Arrange
        var fontData = new TDVFontData
        {
            Name = "TestFont",
            Type = TDVFontType.ASCII,
            Width = 8,
            Height = 8,
            Data = new byte[256 * 8],
            CharacterCount = 256
        };
        var font = new BitmapFont(fontData);

        // Act
        var bitmap = font.GetCharacterBitmap(300); // Invalid character

        // Assert
        Assert.NotNull(bitmap);
        Assert.Equal(8, bitmap.Length);
    }

    [Fact]
    public void HasCharacter_ValidCharacter_ShouldReturnTrue()
    {
        // Arrange
        var fontData = new TDVFontData
        {
            Name = "TestFont",
            Type = TDVFontType.ASCII,
            Width = 8,
            Height = 8,
            Data = new byte[256 * 8],
            CharacterCount = 256
        };
        var font = new BitmapFont(fontData);

        // Act & Assert
        Assert.True(font.HasCharacter(65)); // 'A'
        Assert.True(font.HasCharacter(0));
        Assert.True(font.HasCharacter(255));
        Assert.False(font.HasCharacter(256));
        Assert.False(font.HasCharacter(-1));
    }

    [Fact]
    public void GetCharacterWidth_ShouldReturnCorrectWidth()
    {
        // Arrange
        var fontData = new TDVFontData
        {
            Name = "TestFont",
            Type = TDVFontType.ASCII,
            Width = 8,
            Height = 8,
            Data = new byte[256 * 8],
            CharacterCount = 256
        };
        var font = new BitmapFont(fontData);

        // Act & Assert
        Assert.Equal(8, font.GetCharacterWidth());
        Assert.Equal(8, font.GetCharacterWidth(65));
    }

    [Fact]
    public void GetCharacterHeight_ShouldReturnCorrectHeight()
    {
        // Arrange
        var fontData = new TDVFontData
        {
            Name = "TestFont",
            Type = TDVFontType.ASCII,
            Width = 8,
            Height = 8,
            Data = new byte[256 * 8],
            CharacterCount = 256
        };
        var font = new BitmapFont(fontData);

        // Act & Assert
        Assert.Equal(8, font.GetCharacterHeight());
    }
}
