using RetroTerm.Core.Configuration;

namespace RetroTerm.Desktop.Themes;

/// <summary>
/// Where a terminal's colours came from.
/// </summary>
public enum TerminalColourSource
{
    /// <summary>The user picked them from this tab's own context menu.</summary>
    Tab,

    /// <summary>The connection saved its own colours.</summary>
    Connection,

    /// <summary>The default set in Preferences.</summary>
    PreferencesDefault,

    /// <summary>The pair that goes with the current window theme.</summary>
    WindowTheme,
}

/// <summary>
/// The colours a terminal draws with, and which rule chose them.
/// </summary>
/// <param name="Foreground">Foreground hex.</param>
/// <param name="Background">Background hex.</param>
/// <param name="SinglePhosphor">Whether the sixteen ANSI colours collapse onto the one hue.</param>
/// <param name="Source">The rule that decided.</param>
public sealed record ResolvedTerminalColours(
    string Foreground, string Background, bool SinglePhosphor, TerminalColourSource Source);

/// <summary>
/// Decides which colours a terminal gets. In order, the first that applies wins:
/// <list type="number">
/// <item>this tab's own choice from the tab menu (applied by the caller, after this),</item>
/// <item>the colours saved on the connection,</item>
/// <item>the default set in Preferences,</item>
/// <item>the pair that goes with the current window theme.</item>
/// </list>
/// A choice the user made outranks one that is merely implied, which is why the Preferences default
/// beats the theme's pair even though the theme changes more often.
/// </summary>
public static class TerminalColourResolver
{
    /// <summary>
    /// Works out the colours for one terminal.
    /// </summary>
    /// <param name="connectionForeground">
    /// The connection's saved foreground, or null/empty when it saved none.
    /// </param>
    /// <param name="connectionBackground">
    /// The connection's saved background, or null/empty when it saved none.
    /// </param>
    /// <param name="connectionSinglePhosphor">
    /// The connection's single-phosphor flag. Only used when the connection's colours are used.
    /// </param>
    /// <param name="defaultForeground">
    /// The Preferences default foreground, or null when none is set.
    /// </param>
    /// <param name="defaultBackground">
    /// The Preferences default background, or null when none is set.
    /// </param>
    /// <param name="defaultSinglePhosphor">
    /// The Preferences single-phosphor switch, applied to the default and to the theme's pair.
    /// </param>
    /// <param name="theme">
    /// The current window theme.
    /// </param>
    public static ResolvedTerminalColours Resolve(
        string? connectionForeground, string? connectionBackground, bool connectionSinglePhosphor,
        string? defaultForeground, string? defaultBackground, bool defaultSinglePhosphor,
        ThemeDefinition theme)
    {
        // Both colours must be present AND valid. Half a pair, or a hand-edited value that is not
        // a colour, is treated as "not set" so the terminal never ends up half one scheme and
        // half another, and a bad value never reaches the colour parser in the renderer.
        if (IsPair(connectionForeground, connectionBackground))
        {
            return new ResolvedTerminalColours(
                TerminalColourMath.Normalise(connectionForeground!),
                TerminalColourMath.Normalise(connectionBackground!),
                connectionSinglePhosphor,
                TerminalColourSource.Connection);
        }

        if (IsPair(defaultForeground, defaultBackground))
        {
            return new ResolvedTerminalColours(
                TerminalColourMath.Normalise(defaultForeground!),
                TerminalColourMath.Normalise(defaultBackground!),
                defaultSinglePhosphor,
                TerminalColourSource.PreferencesDefault);
        }

        return new ResolvedTerminalColours(
            TerminalColourMath.ToHex(theme.TerminalForeground.R, theme.TerminalForeground.G, theme.TerminalForeground.B),
            TerminalColourMath.ToHex(theme.TerminalBackground.R, theme.TerminalBackground.G, theme.TerminalBackground.B),
            defaultSinglePhosphor,
            TerminalColourSource.WindowTheme);
    }

    private static bool IsPair(string? foreground, string? background)
        => TerminalColourMath.TryParseHex(foreground, out _) && TerminalColourMath.TryParseHex(background, out _);
}
