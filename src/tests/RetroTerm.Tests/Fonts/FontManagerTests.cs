using System;
using System.IO;
using RetroTerm.Desktop.Fonts;
using Xunit;

namespace RetroTerm.Tests.Fonts;

/// <summary>
/// Tests for FontManager class
/// </summary>
public class FontManagerTests
{
    [Fact]
    public void FontManager_Constructor_ShouldInitializeCorrectly()
    {
        // Arrange
        var tempDir = Path.GetTempPath();
        var fontDir = Path.Combine(tempDir, "test_fonts");

        try
        {
            // Act
            var fontManager = new FontManager(fontDir);

            // Assert
            Assert.NotNull(fontManager);
        }
        finally
        {
            // Cleanup
            if (Directory.Exists(fontDir))
            {
                Directory.Delete(fontDir, true);
            }
        }
    }

    [Fact]
    public void GetAvailableFonts_ShouldReturnFontList()
    {
        // Arrange
        var tempDir = Path.GetTempPath();
        var fontDir = Path.Combine(tempDir, "test_fonts");

        try
        {
            var fontManager = new FontManager(fontDir);

            // Act
            var fonts = fontManager.GetAvailableFonts();

            // Assert
            Assert.NotNull(fonts);
            Assert.True(fonts.Length > 0);
        }
        finally
        {
            // Cleanup
            if (Directory.Exists(fontDir))
            {
                Directory.Delete(fontDir, true);
            }
        }
    }

    [Fact]
    public void GetBitmapFonts_ShouldReturnBitmapFontList()
    {
        // Arrange
        var tempDir = Path.GetTempPath();
        var fontDir = Path.Combine(tempDir, "test_fonts");

        try
        {
            var fontManager = new FontManager(fontDir);

            // Act
            var bitmapFonts = fontManager.GetBitmapFonts();

            // Assert
            Assert.NotNull(bitmapFonts);
            // May be empty if no ROM files are available
        }
        finally
        {
            // Cleanup
            if (Directory.Exists(fontDir))
            {
                Directory.Delete(fontDir, true);
            }
        }
    }

    [Fact]
    public void GetSystemFonts_ShouldReturnSystemFontList()
    {
        // Arrange
        var tempDir = Path.GetTempPath();
        var fontDir = Path.Combine(tempDir, "test_fonts");

        try
        {
            var fontManager = new FontManager(fontDir);

            // Act
            var systemFonts = fontManager.GetSystemFonts();

            // Assert
            Assert.NotNull(systemFonts);
            Assert.True(systemFonts.Length > 0); // Should have at least fallback fonts
        }
        finally
        {
            // Cleanup
            if (Directory.Exists(fontDir))
            {
                Directory.Delete(fontDir, true);
            }
        }
    }

    [Fact]
    public void SetCurrentFont_WithSystemFont_ShouldWork()
    {
        // Arrange
        var tempDir = Path.GetTempPath();
        var fontDir = Path.Combine(tempDir, "test_fonts");

        try
        {
            var fontManager = new FontManager(fontDir);
            var systemFonts = fontManager.GetSystemFonts();

            if (systemFonts.Length > 0)
            {
                // Act
                fontManager.SetCurrentFont(systemFonts[0]);

                // Assert
                Assert.Equal(systemFonts[0], fontManager.GetCurrentFont());
            }
        }
        finally
        {
            // Cleanup
            if (Directory.Exists(fontDir))
            {
                Directory.Delete(fontDir, true);
            }
        }
    }

    [Fact]
    public void SetCurrentFont_WithInvalidFont_ShouldThrow()
    {
        // Arrange
        var tempDir = Path.GetTempPath();
        var fontDir = Path.Combine(tempDir, "test_fonts");

        try
        {
            var fontManager = new FontManager(fontDir);

            // Act & Assert
            Assert.Throws<ArgumentException>(() => fontManager.SetCurrentFont("NonExistentFont"));
        }
        finally
        {
            // Cleanup
            if (Directory.Exists(fontDir))
            {
                Directory.Delete(fontDir, true);
            }
        }
    }

    [Fact]
    public void GetCurrentFont_DefaultState_ShouldReturnDefault()
    {
        // Arrange
        var tempDir = Path.GetTempPath();
        var fontDir = Path.Combine(tempDir, "test_fonts");

        try
        {
            var fontManager = new FontManager(fontDir);

            // Act
            var currentFont = fontManager.GetCurrentFont();

            // Assert
            Assert.Equal("Default", currentFont);
        }
        finally
        {
            // Cleanup
            if (Directory.Exists(fontDir))
            {
                Directory.Delete(fontDir, true);
            }
        }
    }
}
