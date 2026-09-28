using System;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using RetroTerm.Core.Protocols.WebSocket.Gateway;

namespace RetroTerm.Desktop.Themes;

/// <summary>
/// Manages application themes. Builds ResourceDictionaries from ThemeDefinition
/// objects and swaps them at runtime via Application.Resources.MergedDictionaries.
/// Persists the selected theme ID to disk.
/// </summary>
public sealed class ThemeManager
{
    private static ThemeManager? s_instance;
    public static ThemeManager Instance => s_instance ??= new ThemeManager();

    private ResourceDictionary? _currentDictionary;
    private ThemeDefinition _currentTheme;

    /// <summary>
    /// Auto-close transfer progress window after completion.
    /// </summary>
    public bool AutoCloseTransferWindow { get; set; }

    // ── Kermit Settings ─────────────────────────────────────────────
    public int KermitParity { get; set; }
    public bool KermitForce8BitQuoting { get; set; }
    public int KermitDelay { get; set; }
    public int KermitTimeout { get; set; } = 8;
    public int KermitMaxRetries { get; set; } = 10;
    public int KermitFileCollision { get; set; } // 0=Rename, 1=Overwrite, 2=Skip
    public int KermitBlockCheckType { get; set; } = 1;
    public int KermitPacketSize { get; set; } = 80;
    public bool KermitAutoDetectReceive { get; set; }

    // ── MCP Settings ────────────────────────────────────────────────
    /// <summary>
    /// Start the localhost MCP server with the app. Takes effect on next start.
    /// </summary>
    public bool McpEnabled { get; set; } = true;

    /// <summary>
    /// MCP server port (localhost only). This setting is the ONLY source - nothing overrides it.
    /// </summary>
    public int McpPort { get; set; } = 5715;

    // ── Gateway Ethernet ────────────────────────────────────────────
    // An APPLICATION setting, not a per-connection one. The mapping describes how THIS
    // machine's network is shared with an emulated guest - which adapter, which segment -
    // and that is a property of the installation, not of any one saved host.

    /// <summary>
    /// Whether the gateway carries an emulated machine's Ethernet frames at all. Off by
    /// default: a guest's frames should not reach a real LAN because the gateway happened
    /// to be switched on for terminals.
    /// </summary>
    public bool EthernetEnabled { get; set; }

    /// <summary>Where those frames go. See <see cref="EthernetMappingMode"/>.</summary>
    public EthernetMappingMode EthernetMapping { get; set; } = EthernetMappingMode.None;

    /// <summary>
    /// The parameter for the chosen mapping, whose meaning depends on it: an adapter for
    /// <see cref="EthernetMappingMode.HostAdapter"/>, <c>host:port</c> for
    /// <see cref="EthernetMappingMode.JoinSegment"/>, a port for
    /// <see cref="EthernetMappingMode.HostSegment"/>, and <c>group:port</c> for
    /// <see cref="EthernetMappingMode.Multicast"/>. Empty means "the defaults for that mode".
    /// </summary>
    public string EthernetTarget { get; set; } = string.Empty;

    /// <summary>Which emulated segment number these frames belong to. A machine with two
    /// cards would use two segments.</summary>
    public int EthernetSegment { get; set; }

    /// <summary>
    /// The backend spec built from the mapping and its target, in the same grammar RetroCore
    /// uses so a spec is portable between the two products.
    /// </summary>
    public string BuildEthernetSpec()
        => EthernetMappingSpec.Build(EthernetEnabled, EthernetMapping, EthernetTarget);

    public ThemeDefinition CurrentTheme => _currentTheme;

    public event Action? ThemeChanged;

    private ThemeManager()
    {
        _currentTheme = BuiltInThemes.Dark;
    }

    /// <summary>
    /// Loads saved preferences from disk and applies the saved theme.
    /// Call once during App.OnFrameworkInitializationCompleted.
    /// </summary>
    public void Initialize()
    {
        LoadPreferences();
        ApplyTheme(_currentTheme);
    }

    /// <summary>
    /// Switches to the specified theme and persists the choice.
    /// </summary>
    /// <param name="theme">
    /// The theme to apply.
    /// </param>
    /// <param name="persist">
    /// True to write the choice to the preferences file, which is what the app wants.
    /// Pass false to re-colour without touching disk — the headless test app does this,
    /// because a test run must never overwrite the user's real preferences. Mirrors
    /// RetroCommanderUI.Themes.ThemeManager.Apply(themeId, persist).
    /// </param>
    public void ApplyTheme(ThemeDefinition theme, bool persist = true)
    {
        _currentTheme = theme;

        var app = Application.Current;
        if (app == null) return;

        // Remove previous theme dictionary
        if (_currentDictionary != null)
        {
            app.Resources.MergedDictionaries.Remove(_currentDictionary);
        }

        // Build new dictionary from theme definition
        _currentDictionary = BuildDictionary(theme);
        app.Resources.MergedDictionaries.Add(_currentDictionary);

        // Set Avalonia theme variant for FluentTheme base
        app.RequestedThemeVariant = IsLightTheme(theme) ? ThemeVariant.Light : ThemeVariant.Dark;

        if (persist)
        {
            SavePreferences();
        }

        ThemeChanged?.Invoke();
    }

    /// <summary>
    /// Finds a theme by ID. Returns Dark if not found.
    /// </summary>
    public ThemeDefinition GetThemeById(string id)
    {
        for (int i = 0; i < BuiltInThemes.All.Length; i++)
        {
            if (string.Equals(BuiltInThemes.All[i].Id, id, StringComparison.OrdinalIgnoreCase))
                return BuiltInThemes.All[i];
        }
        return BuiltInThemes.Dark;
    }

    private static bool IsLightTheme(ThemeDefinition theme)
    {
        // Simple heuristic: if the window background is bright, it's a light theme
        return theme.WindowBackground.R + theme.WindowBackground.G + theme.WindowBackground.B > 384;
    }

    private static ResourceDictionary BuildDictionary(ThemeDefinition t)
    {
        var dict = new ResourceDictionary();

        void Add(string key, Color color) => dict[key] = new SolidColorBrush(color);

        // Backgrounds
        Add("WindowBackgroundBrush", t.WindowBackground);
        Add("PanelBackgroundBrush", t.PanelBackground);
        Add("ControlBackgroundBrush", t.WindowBackground);
        Add("SectionBackgroundBrush", t.SectionBackground);
        Add("TextBoxBackgroundBrush", t.TextBoxBackground);
        Add("HoverBackgroundBrush", t.HoverBackground);
        Add("SelectedBackgroundBrush", t.SelectedBackground);

        // Borders
        Add("BorderBrush", t.Border);
        Add("FocusBorderBrush", t.FocusBorder);

        // Text
        Add("PrimaryTextBrush", t.PrimaryText);
        Add("SecondaryTextBrush", t.SecondaryText);
        Add("DisabledTextBrush", t.DisabledText);

        // Accent / Buttons
        Add("AccentBrush", t.Accent);
        Add("AccentHoverBrush", t.AccentHover);
        Add("SecondaryButtonBrush", t.SecondaryButton);
        Add("SecondaryButtonBorderBrush", t.SecondaryButtonBorder);
        Add("ErrorBrush", t.Error);
        Add("SuccessBrush", t.Success);
        Add("WarningBrush", t.Warning);

        // Section headers
        Add("SectionHeader1Brush", t.SectionHeader1);
        Add("SectionHeader2Brush", t.SectionHeader2);
        Add("SectionHeader3Brush", t.SectionHeader3);
        Add("SectionHeader4Brush", t.SectionHeader4);

        // Status indicators
        Add("TransferIndicatorBrush", t.TransferIndicator);
        Add("RecordIndicatorBrush", t.RecordIndicator);

        // Error panels
        Add("ErrorPanelBackgroundBrush", t.ErrorPanelBackground);
        Add("ErrorPanelBorderBrush", t.ErrorPanelBorder);
        Add("ErrorPanelTextBrush", t.ErrorPanelText);

        // Code/syntax
        Add("CodeKeywordBrush", t.CodeKeyword);
        Add("CodeStringBrush", t.CodeString);

        // The same colours published a second time under RetroCommander's key names, so this
        // app can include the suite's shared Styles\FluentTheme.axaml unchanged and get the
        // identical control vocabulary rather than a second set of styles that drifts. Only
        // nineteen of RetroTerm's twenty-nine map across; the code-syntax, section-header and
        // indicator colours have no counterpart there and stay RetroTerm's own.
        Add("FluentBackgroundPrimaryBrush", t.WindowBackground);
        Add("FluentBackgroundSecondaryBrush", t.SectionBackground);
        Add("FluentSurfaceAltBrush", t.PanelBackground);
        Add("FluentBorderLightBrush", t.Border);
        Add("FluentBorderDarkBrush", t.SecondaryButtonBorder);
        Add("FluentAccentBlueBrush", t.FocusBorder);
        Add("FluentTextPrimaryBrush", t.PrimaryText);
        Add("FluentTextSecondaryBrush", t.SecondaryText);
        Add("FluentSelectionHighlightBrush", t.SelectedBackground);
        Add("FluentHoverHighlightBrush", t.HoverBackground);
        Add("FluentSidebarBackgroundBrush", t.SectionBackground);
        Add("FluentSidebarSeparatorBrush", t.Border);
        Add("FluentWarningBackgroundBrush", t.SectionBackground);
        Add("FluentWarningBorderBrush", t.Warning);
        Add("FluentSuccessBackgroundBrush", t.SectionBackground);
        Add("FluentSuccessBorderBrush", t.Success);
        Add("FluentErrorBackgroundBrush", t.ErrorPanelBackground);
        Add("FluentErrorBorderBrush", t.ErrorPanelBorder);
        Add("FluentErrorTextBrush", t.ErrorPanelText);

        return dict;
    }

    // ── Persistence ──────────────────────────────────────────────────

    private static string PreferencesPath
    {
        get
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            return Path.Combine(appData, "RetroTerm", "preferences.txt");
        }
    }

    /// <summary>
    /// Persists all preferences to disk. Called automatically on theme change,
    /// but can also be called explicitly when other settings change.
    /// </summary>
    public void SavePreferences()
    {
        try
        {
            string dir = Path.GetDirectoryName(PreferencesPath)!;
            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            // Simple key=value format
            using var writer = new StreamWriter(PreferencesPath);
            writer.WriteLine($"theme={_currentTheme.Id}");
            writer.WriteLine($"autoclose-transfer={AutoCloseTransferWindow}");
            writer.WriteLine($"kermit-parity={KermitParity}");
            writer.WriteLine($"kermit-force8bit={KermitForce8BitQuoting}");
            writer.WriteLine($"kermit-delay={KermitDelay}");
            writer.WriteLine($"kermit-timeout={KermitTimeout}");
            writer.WriteLine($"kermit-retries={KermitMaxRetries}");
            writer.WriteLine($"kermit-collision={KermitFileCollision}");
            writer.WriteLine($"kermit-blockcheck={KermitBlockCheckType}");
            writer.WriteLine($"kermit-packetsize={KermitPacketSize}");
            writer.WriteLine($"kermit-autodetect={KermitAutoDetectReceive}");
            writer.WriteLine($"mcp-enabled={McpEnabled}");
            writer.WriteLine($"mcp-port={McpPort}");
            writer.WriteLine($"eth-enabled={EthernetEnabled}");
            writer.WriteLine($"eth-mapping={(int)EthernetMapping}");
            writer.WriteLine($"eth-target={EthernetTarget}");
            writer.WriteLine($"eth-segment={EthernetSegment}");
        }
        catch
        {
            // Non-critical — ignore write failures
        }
    }

    private void LoadPreferences()
    {
        try
        {
            if (!File.Exists(PreferencesPath)) return;

            string[] lines = File.ReadAllLines(PreferencesPath);
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                int eq = line.IndexOf('=');
                if (eq < 0) continue;

                string key = line.Substring(0, eq).Trim();
                string value = line.Substring(eq + 1).Trim();

                if (key == "theme")
                    _currentTheme = GetThemeById(value);
                else if (key == "autoclose-transfer")
                    AutoCloseTransferWindow = string.Equals(value, "True", StringComparison.OrdinalIgnoreCase);
                else if (key == "kermit-parity" && int.TryParse(value, out var kp))
                    KermitParity = kp;
                else if (key == "kermit-force8bit")
                    KermitForce8BitQuoting = string.Equals(value, "True", StringComparison.OrdinalIgnoreCase);
                else if (key == "kermit-delay" && int.TryParse(value, out var kd))
                    KermitDelay = kd;
                else if (key == "kermit-timeout" && int.TryParse(value, out var kt))
                    KermitTimeout = kt;
                else if (key == "kermit-retries" && int.TryParse(value, out var kr))
                    KermitMaxRetries = kr;
                else if (key == "kermit-collision" && int.TryParse(value, out var kc))
                    KermitFileCollision = kc;
                else if (key == "kermit-blockcheck" && int.TryParse(value, out var kb))
                    KermitBlockCheckType = kb;
                else if (key == "kermit-packetsize" && int.TryParse(value, out var kps))
                    KermitPacketSize = kps;
                else if (key == "kermit-autodetect")
                    KermitAutoDetectReceive = string.Equals(value, "True", StringComparison.OrdinalIgnoreCase);
                else if (key == "mcp-enabled")
                    McpEnabled = string.Equals(value, "True", StringComparison.OrdinalIgnoreCase);
                else if (key == "mcp-port" && int.TryParse(value, out var mp) && mp > 0 && mp <= 65535)
                    McpPort = mp;
                else if (key == "eth-enabled")
                    EthernetEnabled = string.Equals(value, "True", StringComparison.OrdinalIgnoreCase);
                else if (key == "eth-mapping" && int.TryParse(value, out var em) &&
                         Enum.IsDefined(typeof(EthernetMappingMode), em))
                    EthernetMapping = (EthernetMappingMode)em;
                else if (key == "eth-target")
                    EthernetTarget = value;
                else if (key == "eth-segment" && int.TryParse(value, out var es) && es >= 0 && es <= 255)
                    EthernetSegment = es;
            }
        }
        catch
        {
            // Non-critical — use defaults
        }
    }
}
