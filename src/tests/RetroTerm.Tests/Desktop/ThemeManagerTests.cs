using System;
using RetroTerm.Desktop.Themes;
using Xunit;

namespace RetroTerm.Tests.Desktop;

public class ThemeManagerTests
{
    [Fact]
    public void BuiltInThemes_All_ContainsEight()
    {
        // Eight since Solarized Dark was carried over from RetroCommanderUI so the two apps
        // in the suite offer the same theme list.
        Assert.Equal(8, BuiltInThemes.All.Length);
    }

    [Fact]
    public void BuiltInThemes_All_HaveUniqueIds()
    {
        var ids = new System.Collections.Generic.HashSet<string>();
        for (int i = 0; i < BuiltInThemes.All.Length; i++)
        {
            Assert.True(ids.Add(BuiltInThemes.All[i].Id), $"Duplicate theme Id: {BuiltInThemes.All[i].Id}");
        }
    }

    [Fact]
    public void BuiltInThemes_All_HaveNonEmptyDisplayNames()
    {
        for (int i = 0; i < BuiltInThemes.All.Length; i++)
        {
            Assert.False(string.IsNullOrWhiteSpace(BuiltInThemes.All[i].DisplayName),
                $"Theme at index {i} has empty DisplayName");
        }
    }

    [Fact]
    public void BuiltInThemes_All_HaveNonEmptyIds()
    {
        for (int i = 0; i < BuiltInThemes.All.Length; i++)
        {
            Assert.False(string.IsNullOrWhiteSpace(BuiltInThemes.All[i].Id),
                $"Theme at index {i} has empty Id");
        }
    }

    [Fact]
    public void BuiltInThemes_Dark_HasExpectedColors()
    {
        var dark = BuiltInThemes.Dark;
        Assert.Equal("dark", dark.Id);
        Assert.Equal("Dark", dark.DisplayName);
        // These three are the exact values RetroCommanderUI's "Dark" palette uses
        // (RetroCommanderUI\Themes\ThemeManager.cs: BackgroundPrimary, TextPrimary, AccentBlue).
        // The suite shares one dark palette; if it changes there, change it here too.
        Assert.Equal(global::Avalonia.Media.Color.Parse("#1E1E1E"), dark.WindowBackground);
        Assert.Equal(global::Avalonia.Media.Color.Parse("#E8E8E8"), dark.PrimaryText);
        Assert.Equal(global::Avalonia.Media.Color.Parse("#0A84FF"), dark.Accent);
    }

    [Fact]
    public void BuiltInThemes_Light_HasBrightBackground()
    {
        var light = BuiltInThemes.Light;
        Assert.Equal("light", light.Id);
        // Light theme should have a bright window background
        Assert.True(light.WindowBackground.R > 200);
        Assert.True(light.WindowBackground.G > 200);
        Assert.True(light.WindowBackground.B > 200);
    }

    [Fact]
    public void BuiltInThemes_HighContrast_HasBlackBackground()
    {
        var hc = BuiltInThemes.HighContrast;
        Assert.Equal("high-contrast", hc.Id);
        Assert.Equal(global::Avalonia.Media.Color.Parse("#000000"), hc.WindowBackground);
        Assert.Equal(global::Avalonia.Media.Color.Parse("#FFFFFF"), hc.PrimaryText);
    }

    [Fact]
    public void BuiltInThemes_RetroGreen_HasGreenText()
    {
        var green = BuiltInThemes.RetroGreen;
        Assert.Equal("retro-green", green.Id);
        Assert.Equal(0, green.PrimaryText.R);
        Assert.Equal(255, green.PrimaryText.G);
        Assert.Equal(0, green.PrimaryText.B);
    }

    [Fact]
    public void BuiltInThemes_AmberCrt_HasAmberText()
    {
        var amber = BuiltInThemes.AmberCrt;
        Assert.Equal("amber-crt", amber.Id);
        // Amber is warm (R > G >> B)
        Assert.True(amber.PrimaryText.R > amber.PrimaryText.G);
        Assert.True(amber.PrimaryText.G > amber.PrimaryText.B);
    }

    [Fact]
    public void BuiltInThemes_Nord_HasExpectedBackground()
    {
        var nord = BuiltInThemes.Nord;
        Assert.Equal("nord", nord.Id);
        Assert.Equal(global::Avalonia.Media.Color.Parse("#2E3440"), nord.WindowBackground);
    }

    [Fact]
    public void BuiltInThemes_Synthwave_HasExpectedId()
    {
        var sw = BuiltInThemes.Synthwave;
        Assert.Equal("synthwave", sw.Id);
        Assert.Equal("Synthwave '84", sw.DisplayName);
    }

    [Fact]
    public void ThemeManager_GetThemeById_ReturnsCorrectTheme()
    {
        var manager = ThemeManager.Instance;
        for (int i = 0; i < BuiltInThemes.All.Length; i++)
        {
            var theme = manager.GetThemeById(BuiltInThemes.All[i].Id);
            Assert.Equal(BuiltInThemes.All[i].Id, theme.Id);
        }
    }

    [Fact]
    public void ThemeManager_GetThemeById_UnknownId_ReturnsDark()
    {
        var manager = ThemeManager.Instance;
        var theme = manager.GetThemeById("nonexistent-theme-id");
        Assert.Equal("dark", theme.Id);
    }

    [Fact]
    public void ThemeManager_GetThemeById_CaseInsensitive()
    {
        var manager = ThemeManager.Instance;
        var theme = manager.GetThemeById("DARK");
        Assert.Equal("dark", theme.Id);

        var nord = manager.GetThemeById("Nord");
        Assert.Equal("nord", nord.Id);
    }

    [Fact]
    public void ThemeDefinition_AllColorFieldsNonDefault()
    {
        // Verify that no theme has accidentally left a Color field at default(Color) = transparent black
        var defaultColor = default(global::Avalonia.Media.Color);

        for (int i = 0; i < BuiltInThemes.All.Length; i++)
        {
            var t = BuiltInThemes.All[i];
            Assert.NotEqual(defaultColor, t.WindowBackground);
            Assert.NotEqual(defaultColor, t.PanelBackground);
            Assert.NotEqual(defaultColor, t.SectionBackground);
            Assert.NotEqual(defaultColor, t.PrimaryText);
            Assert.NotEqual(defaultColor, t.SecondaryText);
            Assert.NotEqual(defaultColor, t.Accent);
            Assert.NotEqual(defaultColor, t.AccentHover);
            Assert.NotEqual(defaultColor, t.Border);
            Assert.NotEqual(defaultColor, t.Error);
            Assert.NotEqual(defaultColor, t.Success);
            Assert.NotEqual(defaultColor, t.Warning);
            Assert.NotEqual(defaultColor, t.SectionHeader1);
            Assert.NotEqual(defaultColor, t.SectionHeader2);
            Assert.NotEqual(defaultColor, t.SectionHeader3);
            Assert.NotEqual(defaultColor, t.SectionHeader4);
        }
    }

    [Fact]
    public void ThemeDefinition_TextContrastsWithBackground()
    {
        // Basic check: primary text should be visually distinct from window background
        for (int i = 0; i < BuiltInThemes.All.Length; i++)
        {
            var t = BuiltInThemes.All[i];
            int bgLuma = t.WindowBackground.R + t.WindowBackground.G + t.WindowBackground.B;
            int fgLuma = t.PrimaryText.R + t.PrimaryText.G + t.PrimaryText.B;
            int contrast = Math.Abs(bgLuma - fgLuma);
            Assert.True(contrast > 100,
                $"Theme '{t.Id}' has insufficient text/background contrast: bg={bgLuma}, fg={fgLuma}, diff={contrast}");
        }
    }
}
