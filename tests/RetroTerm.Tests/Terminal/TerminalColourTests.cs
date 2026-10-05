using System;
using System.IO;
using System.Linq;
using Avalonia.Media;
using RetroTerm.Core.Configuration;
using RetroTerm.Desktop.Themes;
using Xunit;

namespace RetroTerm.Tests.Terminal;

/// <summary>
/// The terminal colour presets, the user's saved ones, the contrast warning, and the rule that
/// decides which colours a terminal gets.
/// </summary>
public class TerminalColourTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "retroterm-colour-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }

    private UserTerminalColourStore NewStore() => new(Path.Combine(_dir, "terminal-colours.json"));

    // ── built-in presets ────────────────────────────────────────────

    [Fact]
    public void WhiteIsWhiteTextOnPureBlack()
    {
        // It used to sit on #0A0E1C, a dark blue. Pure black is what "White" should mean.
        var white = TerminalColourPresets.BuiltIn.Single(p => p.Name == "White");

        Assert.Equal("#000000", white.Background);
        Assert.True(TerminalColourMath.TryParseHex(white.Background, out var bg));
        Assert.Equal((0, 0, 0), (bg.R, bg.G, bg.B));
    }

    [Fact]
    public void EveryBuiltInPresetIsReadable()
    {
        foreach (var preset in TerminalColourPresets.BuiltIn)
        {
            Assert.Null(TerminalColourMath.ContrastWarning(preset.Foreground, preset.Background));
        }
    }

    [Fact]
    public void BuiltInNamesAreUniqueAndColoursAreValid()
    {
        var names = TerminalColourPresets.BuiltIn.Select(p => p.Name).ToList();
        Assert.Equal(names.Count, names.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        foreach (var preset in TerminalColourPresets.BuiltIn)
        {
            Assert.True(TerminalColourMath.TryParseHex(preset.Foreground, out _), preset.Name);
            Assert.True(TerminalColourMath.TryParseHex(preset.Background, out _), preset.Name);
        }
    }

    // ── hex and contrast ────────────────────────────────────────────

    [Theory]
    [InlineData("#00FF88", 0x00, 0xFF, 0x88)]
    [InlineData("00ff88", 0x00, 0xFF, 0x88)]
    [InlineData("  #0f8 ", 0x00, 0xFF, 0x88)]
    [InlineData("#000000", 0, 0, 0)]
    public void ValidHexParses(string text, int r, int g, int b)
    {
        Assert.True(TerminalColourMath.TryParseHex(text, out var c));
        Assert.Equal((r, g, b), (c.R, c.G, c.B));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("#12")]
    [InlineData("#GGGGGG")]
    [InlineData("#1234567")]
    [InlineData("green")]
    public void InvalidHexIsRejected(string? text)
    {
        Assert.False(TerminalColourMath.TryParseHex(text, out _));
    }

    [Fact]
    public void NormaliseWritesCapitalsWithAHash()
    {
        Assert.Equal("#00FF88", TerminalColourMath.Normalise("00ff88"));
        Assert.Equal("#00FF88", TerminalColourMath.Normalise("#0f8"));
        Assert.Equal("nonsense", TerminalColourMath.Normalise("nonsense"));
    }

    [Fact]
    public void ContrastIsTwentyOneForBlackOnWhiteAndOneForIdentical()
    {
        Assert.Equal(21.0, TerminalColourMath.ContrastRatio((0, 0, 0), (255, 255, 255)), 3);
        Assert.Equal(1.0, TerminalColourMath.ContrastRatio((90, 90, 90), (90, 90, 90)), 3);
    }

    [Fact]
    public void AClearPairGivesNoWarning()
    {
        Assert.Null(TerminalColourMath.ContrastWarning("#FFFFFF", "#000000"));
    }

    [Fact]
    public void ACloseToUnreadablePairGivesTheStrongWarning()
    {
        var warning = TerminalColourMath.ContrastWarning("#101010", "#000000");
        Assert.NotNull(warning);
        Assert.StartsWith("Very low contrast", warning);
    }

    [Fact]
    public void AMarginalPairGivesTheMildWarning()
    {
        // #777777 on white is 4.48:1, just under the 4.5 line.
        var warning = TerminalColourMath.ContrastWarning("#777777", "#FFFFFF");
        Assert.NotNull(warning);
        Assert.StartsWith("Low contrast", warning);
    }

    [Fact]
    public void NoWarningIsGivenForAPairThatIsNotAColourYet()
    {
        Assert.Null(TerminalColourMath.ContrastWarning("#12", "#000000"));
    }

    // ── the user's saved presets ────────────────────────────────────

    [Fact]
    public void ASavedPresetComesBackFromADifferentStoreOnTheSameFile()
    {
        Assert.True(NewStore().TrySave(new TerminalColourPreset("My blue", "00bfff", "#000a14"), out var error), error);

        var again = NewStore().Presets;

        Assert.Single(again);
        Assert.Equal("My blue", again[0].Name);
        Assert.Equal("#00BFFF", again[0].Foreground);   // stored normalised
        Assert.Equal("#000A14", again[0].Background);
    }

    [Fact]
    public void SavingTheSameNameReplacesIt()
    {
        var store = NewStore();
        store.TrySave(new TerminalColourPreset("Mine", "#111111", "#EEEEEE"), out _);
        store.TrySave(new TerminalColourPreset("MINE", "#222222", "#FFFFFF"), out _);

        Assert.Single(store.Presets);
        Assert.Equal("#222222", store.Presets[0].Foreground);
    }

    [Theory]
    [InlineData("White")]
    [InlineData("amber")]
    public void ABuiltInNameIsRefused(string name)
    {
        var store = NewStore();

        Assert.False(store.TrySave(new TerminalColourPreset(name, "#111111", "#EEEEEE"), out var error));
        Assert.Contains("built-in", error);
        Assert.Empty(store.Presets);
    }

    [Fact]
    public void AnEmptyNameOrABadColourIsRefused()
    {
        var store = NewStore();

        Assert.False(store.TrySave(new TerminalColourPreset("  ", "#111111", "#EEEEEE"), out var nameError));
        Assert.Contains("name", nameError);
        Assert.False(store.TrySave(new TerminalColourPreset("X", "nope", "#EEEEEE"), out var colourError));
        Assert.Contains("hex", colourError);
        Assert.Empty(store.Presets);
    }

    [Fact]
    public void ARemovedPresetIsGoneAndTheListIsRaisedAsChanged()
    {
        var store = NewStore();
        store.TrySave(new TerminalColourPreset("Mine", "#111111", "#EEEEEE"), out _);
        int changes = 0;
        store.Changed += () => changes++;

        Assert.True(store.Remove("mine"));
        Assert.False(store.Remove("mine"));

        Assert.Empty(NewStore().Presets);
        Assert.Equal(1, changes);
    }

    [Fact]
    public void ADamagedFileMeansNoPresetsNotACrash()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "terminal-colours.json"), "{ this is not json");

        Assert.Empty(NewStore().Presets);
    }

    [Fact]
    public void ADamagedEntryIsSkippedAndTheGoodOnesSurvive()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "terminal-colours.json"),
            "[{\"Name\":\"Good\",\"Foreground\":\"#111111\",\"Background\":\"#EEEEEE\"}," +
            "{\"Name\":\"Bad\",\"Foreground\":\"zzz\",\"Background\":\"#EEEEEE\"}," +
            "{\"Name\":\"\",\"Foreground\":\"#111111\",\"Background\":\"#EEEEEE\"}]");

        var presets = NewStore().Presets;

        Assert.Single(presets);
        Assert.Equal("Good", presets[0].Name);
    }

    // ── which colours a terminal gets ───────────────────────────────

    private static ThemeDefinition Theme(string fg, string bg) => new()
    {
        Id = "test",
        TerminalForeground = Color.Parse(fg),
        TerminalBackground = Color.Parse(bg),
    };

    [Fact]
    public void AConnectionsOwnColoursWinOverEverything()
    {
        var r = TerminalColourResolver.Resolve("#111111", "#EEEEEE", true, "#222222", "#DDDDDD", false, Theme("#333333", "#CCCCCC"));

        Assert.Equal(("#111111", "#EEEEEE", true, TerminalColourSource.Connection),
            (r.Foreground, r.Background, r.SinglePhosphor, r.Source));
    }

    [Fact]
    public void ThePreferencesDefaultBeatsTheWindowThemesPair()
    {
        // A choice the user made outranks one that is only implied.
        var r = TerminalColourResolver.Resolve(null, null, false, "#222222", "#DDDDDD", true, Theme("#333333", "#CCCCCC"));

        Assert.Equal(("#222222", "#DDDDDD", true, TerminalColourSource.PreferencesDefault),
            (r.Foreground, r.Background, r.SinglePhosphor, r.Source));
    }

    [Fact]
    public void WithNothingSetTheWindowThemesPairIsUsed()
    {
        var r = TerminalColourResolver.Resolve("", "", false, null, null, false, Theme("#333333", "#CCCCCC"));

        Assert.Equal(("#333333", "#CCCCCC", false, TerminalColourSource.WindowTheme),
            (r.Foreground, r.Background, r.SinglePhosphor, r.Source));
    }

    [Fact]
    public void ThePreferencesSinglePhosphorSwitchAppliesToTheThemesPairToo()
    {
        var r = TerminalColourResolver.Resolve(null, null, false, null, null, true, Theme("#333333", "#CCCCCC"));

        Assert.True(r.SinglePhosphor);
        Assert.Equal(TerminalColourSource.WindowTheme, r.Source);
    }

    [Theory]
    [InlineData("#111111", null)]
    [InlineData(null, "#EEEEEE")]
    [InlineData("#111111", "not a colour")]
    public void HalfAPairOrAnInvalidColourFallsThrough(string? fg, string? bg)
    {
        // Never half one scheme and half another, and never a bad value into the colour parser.
        var r = TerminalColourResolver.Resolve(fg, bg, true, null, null, false, Theme("#333333", "#CCCCCC"));

        Assert.Equal(TerminalColourSource.WindowTheme, r.Source);
        Assert.False(r.SinglePhosphor);
    }

    [Fact]
    public void ADefaultOfHexInAnyFormIsWrittenNormalised()
    {
        var r = TerminalColourResolver.Resolve("0f8", "#000", false, null, null, false, Theme("#333333", "#CCCCCC"));

        Assert.Equal("#00FF88", r.Foreground);
        Assert.Equal("#000000", r.Background);
    }

    // ── the window themes' terminal pairs ───────────────────────────

    [Fact]
    public void TheDarkThemeKeepsTheGreenPhosphorTheTerminalAlwaysStartedWith()
    {
        // So nobody on the default theme sees their terminal change colour.
        var r = TerminalColourResolver.Resolve(null, null, false, null, null, false, BuiltInThemes.Dark);

        Assert.Equal("#00FF88", r.Foreground);
        Assert.Equal("#001911", r.Background);
    }

    [Fact]
    public void EveryWindowThemesTerminalPairIsReadable()
    {
        foreach (var theme in BuiltInThemes.All)
        {
            var r = TerminalColourResolver.Resolve(null, null, false, null, null, false, theme);
            Assert.True(TerminalColourMath.TryParseHex(r.Foreground, out _), theme.DisplayName);
            Assert.True(TerminalColourMath.TryParseHex(r.Background, out _), theme.DisplayName);
            Assert.Null(TerminalColourMath.ContrastWarning(r.Foreground, r.Background));
        }
    }
}
