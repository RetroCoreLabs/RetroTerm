using System;
using RetroTerm.Desktop.Fonts;
using Xunit;

namespace RetroTerm.Tests.Fonts;

/// <summary>
/// Tests for SystemFont class
/// </summary>
public class SystemFontTests
{
    [Fact]
    public void SystemFont_Constructor_ShouldInitializeCorrectly()
    {
        // Arrange & Act
        var font = new SystemFont("Consolas", 14, true, false);

        // Assert
        Assert.Equal("Consolas", font.FontFamily);
        Assert.Equal(14, font.FontSize);
        Assert.True(font.IsBold);
        Assert.False(font.IsItalic);
    }

    [Fact]
    public void SystemFont_DefaultConstructor_ShouldUseDefaults()
    {
        // Arrange & Act
        var font = new SystemFont();

        // Assert
        Assert.Equal("Consolas", font.FontFamily);
        Assert.Equal(14, font.FontSize);
        Assert.False(font.IsBold);
        Assert.False(font.IsItalic);
    }

    [Fact]
    public void GetCharacterBitmap_ValidCharacter_ShouldReturnBitmap()
    {
        // Arrange
        var font = new SystemFont("Consolas", 14);

        // Act
        var bitmap = font.GetCharacterBitmap(65); // 'A'

        // Assert
        Assert.NotNull(bitmap);
        Assert.Equal(14 * 14, bitmap.Length); // fontSize * fontSize
    }

    [Fact]
    public void HasCharacter_ShouldReturnTrueForValidCharacters()
    {
        // Arrange
        var font = new SystemFont("Consolas", 14);

        // Act & Assert
        Assert.True(font.HasCharacter(65)); // 'A'
        Assert.True(font.HasCharacter(0));
        Assert.True(font.HasCharacter(255));
    }

    [Fact]
    public void GetCharacterWidth_ShouldReturnFontSize()
    {
        // Arrange
        var font = new SystemFont("Consolas", 16);

        // Act & Assert
        Assert.Equal(16, font.GetCharacterWidth());
        Assert.Equal(8, font.GetCharacterWidth(65)); // Fixed width method
    }

    [Fact]
    public void GetCharacterHeight_ShouldReturnFontSize()
    {
        // Arrange
        var font = new SystemFont("Consolas", 16);

        // Act & Assert
        Assert.Equal(16, font.GetCharacterHeight());
    }

    [Fact]
    public void GetFontMetrics_ShouldReturnCorrectMetrics()
    {
        // Arrange
        var font = new SystemFont("Consolas", 14, true, true);

        // Act
        var metrics = font.GetFontMetrics();

        // Assert
        Assert.Equal("Consolas", metrics.FontFamily);
        Assert.Equal(14, metrics.FontSize);
        Assert.True(metrics.IsBold);
        Assert.True(metrics.IsItalic);
        Assert.Equal(14, metrics.Width);
        Assert.Equal(14, metrics.Height);
    }

    [Fact]
    public void ClearCache_ShouldClearCharacterCache()
    {
        // Arrange
        var font = new SystemFont("Consolas", 14);
        font.GetCharacterBitmap(65); // Cache a character
        var (cachedBefore, _) = font.GetCacheStats();

        // Act
        font.ClearCache();
        var (cachedAfter, _) = font.GetCacheStats();

        // Assert
        Assert.True(cachedBefore > 0);
        Assert.Equal(0, cachedAfter);
    }

    [Fact]
    public void GetCacheStats_ShouldReturnCorrectStats()
    {
        // Arrange
        var font = new SystemFont("Consolas", 14);

        // Act
        var (cachedCharacters, memoryUsage) = font.GetCacheStats();

        // Assert
        Assert.Equal(0, cachedCharacters);
        Assert.Equal(0, memoryUsage);

        // Cache some characters
        font.GetCharacterBitmap(65);
        font.GetCharacterBitmap(66);

        var (cachedAfter, memoryAfter) = font.GetCacheStats();
        Assert.Equal(2, cachedAfter);
        Assert.True(memoryAfter > 0);
    }

    [Fact]
    public void GetAvailableFonts_ShouldReturnFontList()
    {
        // Act
        var fonts = SystemFont.GetAvailableFonts();

        // Assert
        Assert.NotNull(fonts);
        Assert.True(fonts.Length > 0);
        Assert.Contains("Consolas", fonts);
    }

    [Fact]
    public void CreateBestMonospaceFont_ShouldCreateFont()
    {
        // Act
        var font = SystemFont.CreateBestMonospaceFont(16);

        // Assert
        Assert.NotNull(font);
        Assert.Equal(16, font.FontSize);
        Assert.False(font.IsBold);
        Assert.False(font.IsItalic);
    }
}
