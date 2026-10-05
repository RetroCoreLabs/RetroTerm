using Avalonia.Media;

namespace RetroTerm.Desktop.Themes;

/// <summary>
/// Defines the complete color palette for a UI theme.
/// Each theme provides values for all keys; the ThemeManager
/// builds a ResourceDictionary from these and swaps it at runtime.
/// </summary>
public sealed class ThemeDefinition
{
    public string Id { get; init; } = "";
    public string DisplayName { get; init; } = "";

    // Backgrounds
    public Color WindowBackground { get; init; }
    public Color PanelBackground { get; init; }
    public Color SectionBackground { get; init; }
    public Color HoverBackground { get; init; }
    public Color SelectedBackground { get; init; }

    /// <summary>
    /// Background for TEXT AREAS (script editor text, help text, console/log output).
    /// The darkest surface of the theme — the "focused text box" look, kept in EVERY
    /// state (Ronny 2026-08-05: text boxes must not change color on focus/unfocus,
    /// and they keep the black/editor look, not the panel gray).
    /// </summary>
    public Color TextBoxBackground { get; init; }

    /// <summary>
    /// The terminal screen colours this theme goes with: the default text and the screen behind it,
    /// used for every terminal that has not been given colours of its own. The order that decides
    /// which colours a terminal gets is: this tab's own choice, then the connection's saved
    /// colours, then the default set in Preferences, then this pair. The default value is the
    /// green phosphor pair the terminal has always started with.
    /// </summary>
    public Color TerminalForeground { get; init; } = Color.FromRgb(0x00, 0xFF, 0x88);

    /// <summary>
    /// The terminal screen background that goes with <see cref="TerminalForeground"/>.
    /// </summary>
    public Color TerminalBackground { get; init; } = Color.FromRgb(0x00, 0x19, 0x11);

    // Borders
    public Color Border { get; init; }
    public Color FocusBorder { get; init; }

    // Text
    public Color PrimaryText { get; init; }
    public Color SecondaryText { get; init; }
    public Color DisabledText { get; init; }

    // Accent / Buttons
    public Color Accent { get; init; }
    public Color AccentHover { get; init; }
    public Color SecondaryButton { get; init; }
    public Color SecondaryButtonBorder { get; init; }
    public Color Error { get; init; }
    public Color Success { get; init; }
    public Color Warning { get; init; }

    // Section headers (info panels)
    public Color SectionHeader1 { get; init; }
    public Color SectionHeader2 { get; init; }
    public Color SectionHeader3 { get; init; }
    public Color SectionHeader4 { get; init; }

    // Status indicators
    public Color TransferIndicator { get; init; }
    public Color RecordIndicator { get; init; }

    // Error panels
    public Color ErrorPanelBackground { get; init; }
    public Color ErrorPanelBorder { get; init; }
    public Color ErrorPanelText { get; init; }

    // Code/syntax (for ProgrammableKeys, etc.)
    public Color CodeKeyword { get; init; }
    public Color CodeString { get; init; }
}
