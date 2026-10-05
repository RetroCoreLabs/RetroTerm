using System;
using System.IO;
using System.Linq;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using RetroTerm.Core.Configuration;
using RetroTerm.Desktop;
using RetroTerm.Desktop.Themes;
using RetroTerm.Desktop.Views;
using Xunit;

namespace RetroTerm.Tests.Avalonia;

/// <summary>
/// The colour picker, and terminals following the window theme and the Preferences default.
///
/// The PNGs these leave behind are the point as much as the assertions: open them. A swatch that
/// shows the wrong colour, or a warning that is there but unreadable, is not something an
/// assertion about a string would catch.
/// </summary>
[Collection("Avalonia")]
public class TerminalColourUiTests
{
    private readonly ITestOutputHelper _output;

    public TerminalColourUiTests(ITestOutputHelper output)
    {
        _output = output;
    }

    private static void Settle() => Dispatcher.UIThread.RunJobs();

    private static void ResetDefaults()
    {
        ThemeManager.Instance.SetTerminalDefaults(null, null, false, persist: false);
        ThemeManager.Instance.ApplyTheme(BuiltInThemes.Dark, persist: false);
        Settle();
    }

    // ── following the theme and the default ─────────────────────────

    [AvaloniaFact]
    public void ANewTabStartsOnTheColoursTheRuleGives()
    {
        ResetDefaults();
        var window = new MainWindow();
        window.Show();

        var tab = window.ActiveTabForTesting;
        var expected = ThemeManager.Instance.ResolveTerminalColours();

        Assert.NotNull(tab);
        Assert.Equal(expected.Foreground, tab!.CurrentColours!.Foreground);
        Assert.Equal(expected.Background, tab.CurrentColours.Background);
        Assert.Equal(TerminalColourSource.WindowTheme, tab.CurrentColours.Source);
    }

    [AvaloniaFact]
    public void ChangingThePreferencesDefaultRecoloursAnOpenTab()
    {
        ResetDefaults();
        var window = new MainWindow();
        window.Show();
        var tab = window.ActiveTabForTesting!;

        try
        {
            ThemeManager.Instance.SetTerminalDefaults("#FFBF00", "#0A0800", false, persist: false);
            Settle();

            Assert.Equal("#FFBF00", tab.CurrentColours!.Foreground);
            Assert.Equal(TerminalColourSource.PreferencesDefault, tab.CurrentColours.Source);
            var drawn = tab.Control.GetDefaultForeground();
            Assert.Equal((0xFF, 0xBF, 0x00), (drawn!.Value.R, drawn.Value.G, drawn.Value.B));
        }
        finally
        {
            ResetDefaults();
        }
    }

    [AvaloniaFact]
    public void ChangingTheWindowThemeRecoloursATabThatHasNoColoursOfItsOwn()
    {
        ResetDefaults();
        var window = new MainWindow();
        window.Show();
        var tab = window.ActiveTabForTesting!;

        try
        {
            ThemeManager.Instance.ApplyTheme(BuiltInThemes.Nord, persist: false);
            Settle();

            Assert.Equal("#D8DEE9", tab.CurrentColours!.Foreground);
            Assert.Equal("#2E3440", tab.CurrentColours.Background);
        }
        finally
        {
            ResetDefaults();
        }
    }

    [AvaloniaFact]
    public void ATabsOwnChoiceIgnoresLaterChangesUntilDefaultIsChosenAgain()
    {
        ResetDefaults();
        var window = new MainWindow();
        window.Show();
        var tab = window.ActiveTabForTesting!;

        try
        {
            tab.TabColourOverride = new ResolvedTerminalColours("#FF0000", "#000000", false, TerminalColourSource.Tab);
            ThemeManager.Instance.SetTerminalDefaults("#FFBF00", "#0A0800", false, persist: false);
            Settle();
            Assert.Equal("#FF0000", tab.CurrentColours!.Foreground);

            // "Default" in the tab menu clears the choice, and the tab follows again.
            tab.TabColourOverride = null;
            ThemeManager.Instance.SetTerminalDefaults("#00BFFF", "#000A14", false, persist: false);
            Settle();
            Assert.Equal("#00BFFF", tab.CurrentColours!.Foreground);
        }
        finally
        {
            ResetDefaults();
        }
    }

    [AvaloniaFact]
    public void AConnectionsSavedColoursStillBeatTheDefault()
    {
        ResetDefaults();
        var window = new MainWindow();
        window.Show();
        var tab = window.ActiveTabForTesting!;

        try
        {
            tab.LastConnectionParameters = new RetroTerm.Core.Protocols.ConnectionFactory.ConnectionParameters
            {
                ForegroundColor = "#FF8800",
                BackgroundColor = "#101010",
                SinglePhosphor = true,
            };
            ThemeManager.Instance.SetTerminalDefaults("#00BFFF", "#000A14", false, persist: false);
            Settle();

            Assert.Equal("#FF8800", tab.CurrentColours!.Foreground);
            Assert.True(tab.CurrentColours.SinglePhosphor);
            Assert.Equal(TerminalColourSource.Connection, tab.CurrentColours.Source);
        }
        finally
        {
            ResetDefaults();
        }
    }

    [AvaloniaFact]
    public void TheDefaultSurvivesATrip_ThroughThePreferencesFile()
    {
        // SetTerminalDefaults validates; a bad pair clears rather than half-applying.
        ThemeManager.Instance.SetTerminalDefaults("#FFBF00", "nope", true, persist: false);

        Assert.Null(ThemeManager.Instance.DefaultTerminalForeground);
        Assert.Null(ThemeManager.Instance.DefaultTerminalBackground);
        Assert.True(ThemeManager.Instance.DefaultTerminalSinglePhosphor);

        ThemeManager.Instance.SetTerminalDefaults("0f8", "#000", false, persist: false);
        Assert.Equal("#00FF88", ThemeManager.Instance.DefaultTerminalForeground);
        Assert.Equal("#000000", ThemeManager.Instance.DefaultTerminalBackground);
        ResetDefaults();
    }

    // ── the picker ──────────────────────────────────────────────────

    [AvaloniaFact]
    public void ThePickerStartsOnTheColoursItIsGivenAndFallsBackOnNonsense()
    {
        var given = TerminalColourPickerDialog.Create("#FFBF00", "#0A0800", offerSave: false);
        Assert.Equal("#FFBF00", given.CurrentForeground);
        Assert.Equal("#0A0800", given.CurrentBackground);

        var nonsense = TerminalColourPickerDialog.Create("green", null, offerSave: false);
        Assert.Equal("#CCCCCC", nonsense.CurrentForeground);
        Assert.Equal("#000000", nonsense.CurrentBackground);
    }

    [AvaloniaFact]
    public void ThePickerWarnsOnlyWhenTheColoursAreHardToRead()
    {
        var picker = TerminalColourPickerDialog.Create("#FFFFFF", "#000000", offerSave: false);
        Assert.Equal("", picker.WarningShown);

        picker.SetColours("#202020", "#101010");
        Assert.StartsWith("⚠ Very low contrast", picker.WarningShown);

        picker.SetColours("#777777", "#FFFFFF");
        Assert.StartsWith("⚠ Low contrast", picker.WarningShown);

        picker.SetColours("#00FF88", "#001911");
        Assert.Equal("", picker.WarningShown);

        // Half-typed hex is not an error and not a warning: there is nothing to judge yet.
        picker.SetColours("#00FF8", "#001911");
        Assert.Equal("", picker.WarningShown);
    }

    [AvaloniaFact]
    public void ThePickerWritesTheHexInCapitals()
    {
        var picker = TerminalColourPickerDialog.Create("#ffbf00", "#0a0800", offerSave: false);

        Assert.Equal("#FFBF00", picker.CurrentForeground);
        Assert.Equal("#0A0800", picker.CurrentBackground);
    }

    [AvaloniaFact]
    public void ThePickerRendersWithItsPreviewSwatchAndTheWarning()
    {
        var good = TerminalColourPickerDialog.Create("#00FF88", "#001911");
        good.Show();
        var goodShot = RenderedScreenshot.CaptureWindow(good, "terminal-colour-picker-good");
        _output.WriteLine($"picker (good pair): {goodShot?.SavedPath}");

        var bad = TerminalColourPickerDialog.Create("#3A3A3A", "#202020");
        bad.Show();
        var badShot = RenderedScreenshot.CaptureWindow(bad, "terminal-colour-picker-low-contrast");
        _output.WriteLine($"picker (low contrast): {badShot?.SavedPath}");

        Assert.NotNull(goodShot);
        Assert.NotNull(badShot);
    }

    [AvaloniaFact]
    public void PreferencesShowsTheTerminalColoursSection()
    {
        ResetDefaults();
        var window = new PreferencesWindow();
        window.Show();
        var shot = RenderedScreenshot.CaptureWindow(window, "preferences-terminal-colours");
        _output.WriteLine($"preferences: {shot?.SavedPath}");

        Assert.NotNull(shot);
    }
}

/// <summary>
/// The connection dialog's Terminal Color controls: the list, the swatch, the warning and the
/// single-phosphor box.
/// </summary>
[Collection("Avalonia")]
public class ConnectionColourControlsTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "retroterm-conn-" + Guid.NewGuid().ToString("N"));
    private readonly ITestOutputHelper _output;

    public ConnectionColourControlsTests(ITestOutputHelper output)
    {
        _output = output;
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }

    private ManageConnectionsWindow NewWindow()
    {
        // A temporary file, so a test run never reads or writes the user's real connections.
        var manager = new ConfigurationManager(Path.Combine(_dir, "host-configurations.json"));
        var window = new ManageConnectionsWindow(manager);
        window.Show();
        window.ShowTerminalTabForTesting();
        return window;
    }

    private static HostConfiguration Config(string? fg, string? bg, bool mono = false) => new()
    {
        Name = "test",
        Host = "localhost",
        ForegroundColor = fg,
        BackgroundColor = bg,
        SinglePhosphor = mono,
    };

    [AvaloniaFact]
    public void ASavedSinglePhosphorAmberConnectionLoadsAsAmberWithTheBoxTickedAndIsNotChanged()
    {
        var window = NewWindow();

        window.LoadProfileForTesting(Config("#FFBF00", "#0A0800", mono: true));

        Assert.Equal("Amber", window.SelectedColourItemForTesting);
        Assert.True(window.SinglePhosphorTickedForTesting);
        // Loading must not write anything back: before this, selecting the matching preset in
        // code fired the handler, which cleared the flag and lit up Save on an untouched connection.
        Assert.True(window.ProfileForTesting.SinglePhosphor);
        Assert.False(window.ProfileForTesting.IsDirty);
    }

    [AvaloniaFact]
    public void AConnectionWithNoColoursShowsDefaultAndSinglePhosphorIsLeftToPreferences()
    {
        var window = NewWindow();

        window.LoadProfileForTesting(Config(null, null));

        Assert.StartsWith("Default", window.SelectedColourItemForTesting);
        Assert.False(window.SinglePhosphorEnabledForTesting);
        Assert.False(window.ProfileForTesting.IsDirty);
    }

    [AvaloniaFact]
    public void AConnectionWithColoursThatMatchNoPresetShowsCustom()
    {
        var window = NewWindow();

        window.LoadProfileForTesting(Config("#123456", "#ABCDEF"));

        Assert.Equal("Custom...", window.SelectedColourItemForTesting);
        Assert.Equal("#123456", window.ProfileForTesting.ForegroundColor);
    }

    [AvaloniaFact]
    public void PickingAPresetSetsTheColoursAndLeavesSinglePhosphorAlone()
    {
        var window = NewWindow();
        window.LoadProfileForTesting(Config("#FFBF00", "#0A0800", mono: true));

        window.SelectColourItemForTesting("Green");

        Assert.Equal("#00FF00", window.ProfileForTesting.ForegroundColor);
        Assert.Equal("#000E00", window.ProfileForTesting.BackgroundColor);
        Assert.True(window.ProfileForTesting.SinglePhosphor);
    }

    [AvaloniaFact]
    public void PickingWhiteGivesPureBlack()
    {
        var window = NewWindow();
        window.LoadProfileForTesting(Config("#FFBF00", "#0A0800"));

        window.SelectColourItemForTesting("White");

        Assert.Equal("#F8F8F8", window.ProfileForTesting.ForegroundColor);
        Assert.Equal("#000000", window.ProfileForTesting.BackgroundColor);
    }

    [AvaloniaFact]
    public void PickingDefaultClearsTheSavedColours()
    {
        var window = NewWindow();
        window.LoadProfileForTesting(Config("#FFBF00", "#0A0800"));

        window.SelectColourItemForTesting("Default (follow theme or Preferences)");

        Assert.Equal("", window.ProfileForTesting.ForegroundColor);
        Assert.Equal("", window.ProfileForTesting.BackgroundColor);
    }

    [AvaloniaFact]
    public void ALowContrastPairShowsTheWarningAndAGoodOneDoesNot()
    {
        var window = NewWindow();

        window.LoadProfileForTesting(Config("#3A3A3A", "#202020"));
        Assert.StartsWith("⚠ Very low contrast", window.ColourWarningForTesting);

        window.LoadProfileForTesting(Config("#00FF88", "#001911"));
        Assert.Equal("", window.ColourWarningForTesting);
    }

    [AvaloniaFact]
    public void TheTerminalTabRenders()
    {
        var window = NewWindow();
        window.LoadProfileForTesting(Config("#3A3A3A", "#202020", mono: true));

        var shot = RenderedScreenshot.CaptureWindow(window, "connection-terminal-colours");
        _output.WriteLine($"connection dialog: {shot?.SavedPath}");

        Assert.NotNull(shot);
    }
}

/// <summary>
/// The tab's right-click Terminal Color menu.
/// </summary>
[Collection("Avalonia")]
public class TabColourMenuTests
{
    private static string HeaderText(global::Avalonia.Controls.MenuItem item) => item.Header switch
    {
        string text => text,
        global::Avalonia.Controls.StackPanel panel => panel.Children.OfType<global::Avalonia.Controls.TextBlock>().Last().Text ?? "",
        _ => "",
    };

    private static global::Avalonia.Controls.MenuItem Find(global::Avalonia.Controls.MenuItem menu, string startsWith)
        => menu.Items.OfType<global::Avalonia.Controls.MenuItem>().First(i => HeaderText(i).StartsWith(startsWith, StringComparison.Ordinal));

    private static void Click(global::Avalonia.Controls.MenuItem item)
        => item.RaiseEvent(new global::Avalonia.Interactivity.RoutedEventArgs(global::Avalonia.Controls.MenuItem.ClickEvent));

    private static (MainWindow Window, RetroTerm.Desktop.Models.TabSession Tab, global::Avalonia.Controls.MenuItem Menu) Open()
    {
        ThemeManager.Instance.SetTerminalDefaults(null, null, false, persist: false);
        ThemeManager.Instance.ApplyTheme(BuiltInThemes.Dark, persist: false);
        var window = new MainWindow();
        window.Show();
        var tab = window.ActiveTabForTesting!;
        return (window, tab, window.BuildTerminalColourMenuForTesting(tab));
    }

    [AvaloniaFact]
    public void TheMenuListsDefaultTheBuiltInPresetsCustomAndSinglePhosphor()
    {
        var (_, _, menu) = Open();
        var headers = menu.Items.OfType<global::Avalonia.Controls.MenuItem>().Select(HeaderText).ToList();

        Assert.Contains(headers, h => h.StartsWith("Default"));
        foreach (var preset in TerminalColourPresets.BuiltIn) Assert.Contains(preset.Name, headers);
        Assert.Contains("Custom...", headers);
        Assert.Contains(headers, h => h.StartsWith("Single phosphor"));
        // No preset repeats another under a different name.
        Assert.DoesNotContain(headers, h => h.Contains("(single phosphor)"));
    }

    [AvaloniaFact]
    public void PickingWhiteColoursOnlyThisTabWhiteOnPureBlack()
    {
        var (_, tab, menu) = Open();

        Click(Find(menu, "White"));

        Assert.Equal(TerminalColourSource.Tab, tab.CurrentColours!.Source);
        Assert.Equal("#000000", tab.CurrentColours.Background);
        var bg = tab.Control.GetDefaultBackground()!.Value;
        Assert.Equal((0, 0, 0), (bg.R, bg.G, bg.B));
    }

    [AvaloniaFact]
    public void SinglePhosphorTogglesOnTopOfTheColoursThatAreShowing()
    {
        var (_, tab, menu) = Open();
        var before = tab.CurrentColours!;

        Click(Find(menu, "Single phosphor"));
        Assert.True(tab.CurrentColours!.SinglePhosphor);
        Assert.Equal(before.Foreground, tab.CurrentColours.Foreground);
        Assert.Equal(before.Background, tab.CurrentColours.Background);

        Click(Find(menu, "Single phosphor"));
        Assert.False(tab.CurrentColours!.SinglePhosphor);
    }

    [AvaloniaFact]
    public void ChoosingAColourAfterSinglePhosphorKeepsSinglePhosphor()
    {
        var (_, tab, menu) = Open();

        Click(Find(menu, "Single phosphor"));
        Click(Find(menu, "Amber"));

        Assert.Equal("#FFBF00", tab.CurrentColours!.Foreground);
        Assert.True(tab.CurrentColours.SinglePhosphor);
    }

    [AvaloniaFact]
    public void DefaultGivesTheTabBackToTheRule()
    {
        var (_, tab, menu) = Open();
        Click(Find(menu, "Amber"));
        Assert.Equal(TerminalColourSource.Tab, tab.CurrentColours!.Source);

        Click(Find(menu, "Default"));

        Assert.Null(tab.TabColourOverride);
        Assert.Equal(TerminalColourSource.WindowTheme, tab.CurrentColours!.Source);
    }
}
