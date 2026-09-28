using Avalonia;
using Avalonia.Headless;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Skia;

// WITHOUT this attribute the AvaloniaFact runner never uses our TestApp — the tests
// ran against a bare default Application with NO styles, so no control ever had a
// template and template-level bugs (invisible derived controls, bad selectors) were
// invisible to the suite (found 2026-08-05).
[assembly: AvaloniaTestApplication(typeof(RetroTerm.Tests.Avalonia.AvaloniaTestAppBuilder))]

namespace RetroTerm.Tests.Avalonia;

/// <summary>
/// Provides Avalonia application builder for headless testing
/// </summary>
public class AvaloniaTestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp()
    {
        return AppBuilder.Configure<TestApp>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions
            {
                UseHeadlessDrawing = false // Required for screenshot capture
            })
            .LogToTrace();
    }
}

/// <summary>
/// Minimal Avalonia application for headless testing.
///
/// Loads FluentTheme so controls get REAL templates — without it, template-part
/// style selectors are never evaluated and window smoke tests can't catch
/// selector bugs (a bad selector crashed the script editor on open, 2026-08-05,
/// while the un-themed headless test passed).
///
/// It ALSO loads RetroTerm's own DarkTheme.axaml and applies a theme through
/// ThemeManager, in the same order App.axaml does. Until 2026-08-28 it did neither,
/// so every headless test rendered the app WITHOUT its own stylesheet and without
/// any of the ThemeManager brushes: no RetroTerm style and no theme colour had ever
/// been exercised by the suite, and a style that silently failed to apply looked
/// exactly like one that worked. Found while moving the window chrome onto the
/// shared style layer — the chrome tests reported Avalonia's stock 14px font
/// because the stylesheet setting 12px was not loaded at all.
/// </summary>
public class TestApp : Application
{
    /// <summary>
    /// Builds the same style stack the real application uses: Fluent first for the
    /// control templates, then RetroTerm's own styles on top, then a theme so every
    /// DynamicResource brush resolves.
    /// </summary>
    public override void Initialize()
    {
        Styles.Add(new global::Avalonia.Themes.Fluent.FluentTheme());
        // BOTH of RetroTerm's stylesheets, in App.axaml's order. Until 27 September 2026 only
        // DarkTheme.axaml was loaded here, so every class defined in the app's own
        // FluentTheme.axaml - dialog-primary, dialog-secondary, dialog-danger, fluent-button,
        // fkey, the dialog footer - was invisible to every headless test: a button carrying
        // one of them rendered as a stock grey Fluent button and nothing went red. Found when a
        // test asserting the Close RetroTerm dialog's Close All button is red read back grey.
        Styles.Add(new StyleInclude(new System.Uri("avares://RetroTerm.Desktop/Styles/"))
        {
            Source = new System.Uri("avares://RetroTerm.Desktop/Styles/FluentTheme.axaml")
        });
        Styles.Add(new StyleInclude(new System.Uri("avares://RetroTerm.Desktop/Styles/"))
        {
            Source = new System.Uri("avares://RetroTerm.Desktop/Styles/DarkTheme.axaml")
        });
    }

    /// <summary>
    /// Applies the default theme once the framework is up, which is what fills
    /// Application.Resources with the WindowBackgroundBrush / PrimaryTextBrush family.
    /// </summary>
    public override void OnFrameworkInitializationCompleted()
    {
        // persist:false — Initialize() and a plain ApplyTheme() both write the user's real
        // preferences file, and a test run must never touch it.
        RetroTerm.Desktop.Themes.ThemeManager.Instance.ApplyTheme(
            RetroTerm.Desktop.Themes.BuiltInThemes.Dark, persist: false);
        base.OnFrameworkInitializationCompleted();
    }
}
