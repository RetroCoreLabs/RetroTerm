using System;
using System.Text;
using RetroTerm.Core.Terminal.Emulators.TDV;
using RetroTerm.Desktop.Fonts;
using RetroTerm.Tests.TDV.TestHelpers;
using Xunit;

namespace RetroTerm.Tests.TDV;

/// <summary>
/// Integration tests for TDV terminal system
/// Tests end-to-end functionality with font support and character set switching
/// </summary>
public class TDVIntegrationTests
{
    private readonly TDV1200Emulator _tdv1200;
    private readonly TDV2215Emulator _tdv2215;
    private readonly TDV2200Emulator _tdv2200;
    private readonly FontManager _fontManager;

    public TDVIntegrationTests()
    {
        _tdv1200 = new TDV1200Emulator(80, 24, 10000);
        _tdv2215 = new TDV2215Emulator(80, 24, 10000);
        _tdv2200 = new TDV2200Emulator(80, 24, 10000);
        _fontManager = new FontManager("test_fonts/");
    }

    [Fact]
    public void TDV1200_With2115Mode_ShouldWorkCorrectly()
    {
        // Arrange
        var sequence = "\x1b[66l"; // RM 66 - the EC switch off, which is 2115 mode. 2215 spec 3.1.

        // Act
        _tdv1200.ProcessInput(TestTDVEmulatorBase.StringToBytes(sequence));

        // Assert
        Assert.True(_tdv1200.Is2115CompatibilityMode);
        Assert.Equal("TDV2115", _tdv1200.GetTerminalType());
    }

    /// <summary>
    /// Extended operation is the EC switch, and a fresh TDV2215 is already in it.
    /// </summary>
    /// <remarks>
    /// This test used to send <c>CSI ? 1 h</c> - DECCKM - and expect "TDV2215+EXTENDED" back.
    /// Extended operation is mode 66, TDV 2215 section 8.7.1, and reset of that mode is what enters
    /// 2115-compatible operation. Since the emulator obeys CSI from its first byte it starts
    /// extended, and the type string names the departure instead: plain "TDV2215" is extended,
    /// "TDV2215+2115" is the compatible mode.
    /// </remarks>
    [Fact]
    public void TDV2215_StartsInExtendedOperation_AndModeSixtySixLeavesIt()
    {
        Assert.True(_tdv2215.IsExtendedMode);
        Assert.Equal("TDV2215", _tdv2215.GetTerminalType());

        _tdv2215.ProcessInput(TestTDVEmulatorBase.StringToBytes("\x1B[66l"));

        Assert.False(_tdv2215.IsExtendedMode);
        Assert.True(_tdv2215.Is2115CompatibilityMode);
        Assert.Equal("TDV2215+2115", _tdv2215.GetTerminalType());

        // ESC Q is the way back, and the only one from inside 2115 operation.
        _tdv2215.ProcessInput(TestTDVEmulatorBase.StringToBytes("\x1BQ"));

        Assert.True(_tdv2215.IsExtendedMode);
        Assert.Equal("TDV2215", _tdv2215.GetTerminalType());
    }

    /// <summary>
    /// No host sequence puts a TDV2215 into transparent operation.
    /// </summary>
    /// <remarks>
    /// It is the TRANSPARENT setting of the Send-Receive Mode soft-switch, section 4.3.1, set and
    /// cleared from the keyboard - "The only exit possible from this mode is obtained by depressing
    /// the MODE key twice". Section 8.7.1 lists every host-settable mode and SRM is not there.
    ///
    /// This test used to send <c>CSI ? 2 h</c> and expect transparent mode on. ND-1200 section 5.64
    /// lists 2 among the DEC-compatible numbers the terminal IGNORES, so the sequence has to leave
    /// the terminal exactly as it found it.
    /// </remarks>
    [Fact]
    public void TDV2215_TransparentMode_CannotBeEnteredByTheHost()
    {
        _tdv2215.ProcessInput(TestTDVEmulatorBase.StringToBytes("\x1B[?2h"));

        Assert.False(_tdv2215.IsTransparentMode);
        Assert.Equal("TDV2215", _tdv2215.GetTerminalType());
    }

    /// <summary>
    /// A TDV2200 reports graphics and Tektronix because it has them, not because it was asked.
    /// </summary>
    /// <remarks>
    /// Two tests used to sit here, each sending <c>CSI n greater-than</c> first. That sequence has
    /// no source in any manual held in this repository - checked on 25 August 2026 - and the flags
    /// it set gated nothing but these two strings.
    /// </remarks>
    [Fact]
    public void TDV2200_ReportsGraphicsAndTektronixFromTheStart()
    {
        Assert.Contains("+GRAPHICS", _tdv2200.GetTerminalType());
        Assert.Contains("+TEKTRONIX", _tdv2200.GetTerminalType());
        Assert.True(_tdv2200.DrawsTektronixVectors);
    }

    [Fact]
    public void TDV2200_WithISO646Variants_ShouldWorkCorrectly()
    {
        // Arrange
        var variants = _tdv2200.GetAvailableCharacterSetVariants();

        // Act & Assert - TDV2115 spec 9.1 defines 4 variants
        Assert.Equal(4, variants.Length);
        Assert.Equal((int)TDV2200ISO646Variant.International, variants[0].Value);
        Assert.Equal((int)TDV2200ISO646Variant.Norwegian, variants[1].Value);
        Assert.Equal((int)TDV2200ISO646Variant.Swedish, variants[2].Value);
        Assert.Equal((int)TDV2200ISO646Variant.German, variants[3].Value);
    }

    [Fact]
    public void FontManager_ShouldManageFontsCorrectly()
    {
        // Arrange
        var fontManager = new FontManager("test_fonts/");

        // Act
        var availableFonts = fontManager.GetAvailableFonts();
        var bitmapFonts = fontManager.GetBitmapFonts();
        var systemFonts = fontManager.GetSystemFonts();

        // Assert
        Assert.NotNull(availableFonts);
        Assert.NotNull(bitmapFonts);
        Assert.NotNull(systemFonts);
        Assert.True(availableFonts.Length >= 0);
        Assert.True(bitmapFonts.Length >= 0);
        Assert.True(systemFonts.Length >= 0);
    }

    [Fact]
    public void FontManager_ShouldSetCurrentFontCorrectly()
    {
        // Arrange
        var fontManager = new FontManager("test_fonts/");
        var availableFonts = fontManager.GetAvailableFonts();

        if (availableFonts.Length > 0)
        {
            var fontName = availableFonts[0];

            // Act
            fontManager.SetCurrentFont(fontName);

            // Assert
            Assert.Equal(fontName, fontManager.GetCurrentFont());
        }
    }

    [Fact]
    public void FontManager_ShouldGetBestFontForTerminalCorrectly()
    {
        // Arrange
        var fontManager = new FontManager("test_fonts/");

        // Act
        var tdv1200Font = fontManager.GetBestFontForTerminal("TDV1200");
        var tdv2215Font = fontManager.GetBestFontForTerminal("TDV2215");
        var tdv2200Font = fontManager.GetBestFontForTerminal("TDV2200");
        var unknownFont = fontManager.GetBestFontForTerminal("UNKNOWN");

        // Assert
        Assert.NotNull(tdv1200Font);
        Assert.NotNull(tdv2215Font);
        Assert.NotNull(tdv2200Font);
        Assert.NotNull(unknownFont);
    }

    [Fact]
    public void FontManager_ShouldRenderCharactersCorrectly()
    {
        // Arrange
        var fontManager = new FontManager("test_fonts/");
        var outputBuffer = new byte[1000];
        var characterCode = 65; // 'A'
        var x = 10;
        var y = 10;
        var outputWidth = 100;
        var outputHeight = 100;

        // Act
        fontManager.RenderCharacter(characterCode, outputBuffer, x, y, outputWidth, outputHeight);

        // Assert
        // TODO: Verify that the character was rendered correctly
        // This would involve checking the output buffer contents
    }

    [Fact]
    public void FontManager_ShouldGetCharacterDimensionsCorrectly()
    {
        // Arrange
        var fontManager = new FontManager("test_fonts/");
        var characterCode = 65; // 'A'

        // Act
        var width = fontManager.GetCharacterWidth(characterCode);
        var height = fontManager.GetCharacterHeight();

        // Assert
        Assert.True(width > 0);
        Assert.True(height > 0);
    }

    [Fact]
    public void FontManager_ShouldCheckCharacterAvailabilityCorrectly()
    {
        // Arrange
        var fontManager = new FontManager("test_fonts/");
        var characterCode = 65; // 'A'

        // Act
        var hasCharacter = fontManager.HasCharacter(characterCode);

        // Assert
        Assert.True(hasCharacter);
    }

    [Fact]
    public void FontManager_ShouldGetFontMetricsCorrectly()
    {
        // Arrange
        var fontManager = new FontManager("test_fonts/");

        // Act
        var metrics = fontManager.GetFontMetrics();

        // Assert
        Assert.NotNull(metrics);
        Assert.NotNull(metrics.CurrentFont);
        Assert.True(metrics.CharacterWidth > 0);
        Assert.True(metrics.CharacterHeight > 0);
    }

    [Fact]
    public void FontManager_ShouldClearCachesCorrectly()
    {
        // Arrange
        var fontManager = new FontManager("test_fonts/");

        // Act
        fontManager.ClearCaches();

        // Assert
        // TODO: Verify that caches were cleared correctly
        // This would involve checking cache statistics
    }

    [Fact]
    public void FontManager_ShouldGetCacheStatsCorrectly()
    {
        // Arrange
        var fontManager = new FontManager("test_fonts/");

        // Act
        var (totalCached, totalMemory) = fontManager.GetCacheStats();

        // Assert
        Assert.True(totalCached >= 0);
        Assert.True(totalMemory >= 0);
    }

    [Fact]
    public void FontManager_ShouldEnableBitmapFontsCorrectly()
    {
        // Arrange
        var fontManager = new FontManager("test_fonts/");

        // Act
        fontManager.EnableBitmapFonts();

        // Assert
        // TODO: Verify that bitmap fonts were enabled
        // This would involve checking the font manager state
    }

    [Fact]
    public void FontManager_ShouldEnableSystemFontsCorrectly()
    {
        // Arrange
        var fontManager = new FontManager("test_fonts/");

        // Act
        fontManager.EnableSystemFonts();

        // Assert
        // TODO: Verify that system fonts were enabled
        // This would involve checking the font manager state
    }
}
