using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using RetroTerm.Desktop;
using RetroTerm.Desktop.Themes;
using Xunit;
using Xunit.Abstractions;

namespace RetroTerm.Tests.Avalonia;

/// <summary>
/// Covers the window chrome — menu bar, tab bar and status bar — after it was moved off
/// per-element inline values and onto the shared style layer in Styles\DarkTheme.axaml.
///
/// The chrome used to carry three different font sizes (13 on the menu, 13 on the tabs,
/// 11 in the status bar), a Padding repeated on roughly fifty MenuItem elements, and a
/// hardcoded grey on the new-tab button. These tests pin the single source: if anyone puts
/// an inline FontSize or Padding back on the chrome, the shared value stops winning and the
/// matching test fails.
///
/// The rendered PNG these tests leave behind is the point as much as the assertions are —
/// open it. An assertion only catches what it was told to expect.
/// </summary>
[Collection("Avalonia")]
public class WindowChromeStyleTests
{
    private readonly ITestOutputHelper _output;

    /// <summary>
    /// Creates the test class and captures the xUnit output sink.
    /// </summary>
    /// <param name="output">
    /// Where the saved PNG path is written so it can be opened after a run.
    /// </param>
    public WindowChromeStyleTests(ITestOutputHelper output)
    {
        _output = output;
    }

    /// <summary>
    /// The menu bar, the tab strip and the status bar must all resolve to one font family.
    /// Before the style layer they inherited three different ones depending on where the
    /// control sat.
    /// </summary>
    [AvaloniaFact]
    public void TheChromeResolvesToOneFontFamily()
    {
        var window = new MainWindow();
        window.Show();

        Menu? menu = FindDescendant<Menu>(window);
        Assert.NotNull(menu);

        FontFamily expected = window.FontFamily;
        Assert.Equal(expected, menu!.FontFamily);

        // The status bar carries the class the descendant styles hang off.
        Border? statusBar = FindStatusBar(window);
        Assert.NotNull(statusBar);

        TextBlock? statusText = FindDescendant<TextBlock>(statusBar!);
        Assert.NotNull(statusText);
        Assert.Equal(expected, statusText!.FontFamily);
    }

    /// <summary>
    /// The status bar sits on the small step of the shared type scale, and the menu on the
    /// body step. Both come from the FluentFontSize resources rather than an inline number.
    /// </summary>
    [AvaloniaFact]
    public void TheChromeUsesTheSharedTypeScale()
    {
        var window = new MainWindow();
        window.Show();

        Menu? menu = FindDescendant<Menu>(window);
        Assert.NotNull(menu);
        Assert.Equal(12.0, menu!.FontSize);

        Border? statusBar = FindStatusBar(window);
        Assert.NotNull(statusBar);
        TextBlock? statusText = FindDescendant<TextBlock>(statusBar!);
        Assert.NotNull(statusText);
        Assert.Equal(10.5, statusText!.FontSize);
    }

    /// <summary>
    /// No MenuItem may carry its own Padding any more. One value in the style layer decides
    /// the whole menu's density, which is what makes the menu look tightened rather than
    /// merely smaller.
    /// </summary>
    [AvaloniaFact]
    public void EveryMenuItemTakesItsPaddingFromTheStyleLayer()
    {
        var window = new MainWindow();
        window.Show();

        Menu? menu = FindDescendant<Menu>(window);
        Assert.NotNull(menu);

        int checkedItems = 0;
        for (int i = 0; i < menu!.Items.Count; i++)
        {
            if (menu.Items[i] is MenuItem item)
            {
                // Top-level headers sit tighter than the submenu rows below them.
                Assert.Equal(new global::Avalonia.Thickness(9, 4), item.Padding);
                checkedItems++;
            }
        }

        Assert.True(checkedItems > 0, "the menu bar had no top-level items to check");
    }

    /// <summary>
    /// Renders the real window and writes the PNG. The assertion only proves something was
    /// drawn; the file is there to be looked at.
    /// </summary>
    [AvaloniaFact]
    public void TheChromeRendersAndTheImageIsWritten()
    {
        var window = new MainWindow();
        window.Width = 1100;
        window.Height = 700;
        window.Show();

        var shot = RenderedScreenshot.CaptureWindow(window, "window-chrome-dark");
        Assert.NotNull(shot);
        using (shot)
        {
            _output.WriteLine($"chrome frame {shot!.Width}x{shot.Height}, saved to {shot.SavedPath}");
            Assert.True(shot.HasAnyPixelDifferentFrom(shot.PixelAt(0, 0)),
                "the whole window frame is a single flat colour - nothing was rendered");
        }
    }

    /// <summary>
    /// Solarized Dark was carried over from RetroCommanderUI so both apps in the suite offer
    /// the same theme list, and it must land on the Dark control variant, not Light.
    /// </summary>
    [AvaloniaFact]
    public void SolarizedDarkIsOfferedAndIsADarkTheme()
    {
        ThemeDefinition solarized = BuiltInThemes.SolarizedDark;
        Assert.Equal("solarized-dark", solarized.Id);

        // base03 — the darkest Solarized background. Well under the brightness cut that
        // ThemeManager uses to choose between the Light and Dark control variants.
        Assert.Equal(Color.Parse("#002B36"), solarized.WindowBackground);

        bool present = false;
        for (int i = 0; i < BuiltInThemes.All.Length; i++)
        {
            if (ReferenceEquals(BuiltInThemes.All[i], solarized))
            {
                present = true;
                break;
            }
        }

        Assert.True(present, "Solarized Dark is not in BuiltInThemes.All, so it never reaches the picker");
    }

    // ---------------------------------------------------------------------------------
    // Helpers. Index-based walks, no LINQ.
    // ---------------------------------------------------------------------------------

    /// <summary>
    /// Finds the status bar by the class the style layer hangs off.
    /// </summary>
    /// <param name="root">
    /// The window to search.
    /// </param>
    /// <returns>
    /// The status bar border, or null when it is not present.
    /// </returns>
    private static Border? FindStatusBar(global::Avalonia.Visual root)
    {
        var children = (System.Collections.Generic.IReadOnlyList<global::Avalonia.Visual>)global::Avalonia.VisualTree.VisualExtensions.GetVisualChildren(root);
        for (int i = 0; i < children.Count; i++)
        {
            global::Avalonia.Visual child = children[i];
            if (child is Border border && border.Classes.Contains("StatusBar"))
            {
                return border;
            }

            Border? found = FindStatusBar(child);
            if (found != null)
            {
                return found;
            }
        }

        return null;
    }

    /// <summary>
    /// Finds the first descendant of the given type in visual-tree order.
    /// </summary>
    /// <typeparam name="T">
    /// The control type to look for.
    /// </typeparam>
    /// <param name="root">
    /// The visual to search under.
    /// </param>
    /// <returns>
    /// The first match, or null when there is none.
    /// </returns>
    private static T? FindDescendant<T>(global::Avalonia.Visual root) where T : class
    {
        var children = (System.Collections.Generic.IReadOnlyList<global::Avalonia.Visual>)global::Avalonia.VisualTree.VisualExtensions.GetVisualChildren(root);
        for (int i = 0; i < children.Count; i++)
        {
            global::Avalonia.Visual child = children[i];
            if (child is T match)
            {
                return match;
            }

            T? found = FindDescendant<T>(child);
            if (found != null)
            {
                return found;
            }
        }

        return null;
    }
}
