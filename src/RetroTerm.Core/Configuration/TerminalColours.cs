using System.Globalization;
using System.Text.Json;

namespace RetroTerm.Core.Configuration;

/// <summary>
/// A named foreground and background for a terminal screen, as hex strings ("#RRGGBB").
/// </summary>
public sealed class TerminalColourPreset
{
    /// <summary>
    /// A preset with the given name and colours.
    /// </summary>
    /// <param name="name">
    /// Name shown in the colour menus.
    /// </param>
    /// <param name="foreground">
    /// Colour of text a host has not coloured.
    /// </param>
    /// <param name="background">
    /// Colour of the screen behind it.
    /// </param>
    public TerminalColourPreset(string name, string foreground, string background)
    {
        Name = name;
        Foreground = foreground;
        Background = background;
    }

    /// <summary>Name shown in the colour menus.</summary>
    public string Name { get; }

    /// <summary>Colour of text a host has not coloured, as hex.</summary>
    public string Foreground { get; }

    /// <summary>Colour of the screen behind the text, as hex.</summary>
    public string Background { get; }
}

/// <summary>
/// The colour presets RetroTerm ships with, and the ones the user has saved.
///
/// "Single phosphor" is deliberately NOT a preset. It does not change the two colours, it changes
/// how the sixteen ANSI colours are drawn (as brightnesses of one hue), so it is a separate switch
/// that applies to any pair - built-in, saved or custom. It used to be two extra presets that
/// repeated Amber and Green Phosphor byte for byte, which made the list look duplicated and made
/// a saved single-phosphor connection show up as the plain preset when it was loaded again.
/// </summary>
public static class TerminalColourPresets
{
    /// <summary>
    /// The built-in presets, alphabetical. The backgrounds of the coloured ones carry a trace of
    /// the phosphor so black does not look dead next to bright text; White is true black.
    /// </summary>
    public static IReadOnlyList<TerminalColourPreset> BuiltIn { get; } = new[]
    {
        new TerminalColourPreset("Amber", "#FFBF00", "#0A0800"),
        new TerminalColourPreset("Blue", "#00BFFF", "#000A14"),
        new TerminalColourPreset("Cyan", "#00FFFF", "#001919"),
        new TerminalColourPreset("Green", "#00FF00", "#000E00"),
        new TerminalColourPreset("Green Phosphor", "#00FF88", "#001911"),
        new TerminalColourPreset("Paper White", "#222222", "#F0F0F5"),
        new TerminalColourPreset("White", "#F8F8F8", "#000000"),
    };

    /// <summary>
    /// The built-in presets followed by the user's saved ones, in menu order.
    /// </summary>
    public static IReadOnlyList<TerminalColourPreset> All
    {
        get
        {
            var all = new List<TerminalColourPreset>(BuiltIn);
            all.AddRange(UserTerminalColourStore.Default.Presets);
            return all;
        }
    }

    /// <summary>
    /// The first preset (built-in first) whose two colours are these, ignoring case, or null.
    /// </summary>
    /// <param name="foreground">
    /// Foreground hex.
    /// </param>
    /// <param name="background">
    /// Background hex.
    /// </param>
    public static TerminalColourPreset? FindByColours(string? foreground, string? background)
    {
        foreach (var preset in All)
        {
            if (string.Equals(preset.Foreground, foreground, StringComparison.OrdinalIgnoreCase)
                && string.Equals(preset.Background, background, StringComparison.OrdinalIgnoreCase))
            {
                return preset;
            }
        }

        return null;
    }
}

/// <summary>
/// The presets the user saved from the colour picker, kept in a small JSON file next to the other
/// settings so they follow the user from one run to the next.
/// </summary>
public sealed class UserTerminalColourStore
{
    private readonly object _lock = new();
    private readonly string _path;
    private List<TerminalColourPreset>? _presets;

    /// <summary>
    /// Raised after the saved list changed, so open menus can rebuild.
    /// </summary>
    public event Action? Changed;

    /// <summary>
    /// A store reading and writing the given file. Tests pass a temporary path.
    /// </summary>
    /// <param name="path">
    /// The JSON file. It does not have to exist yet.
    /// </param>
    public UserTerminalColourStore(string path)
    {
        _path = path;
    }

    /// <summary>
    /// The store the application uses: terminal-colours.json in the application-data folder.
    /// </summary>
    public static UserTerminalColourStore Default { get; } = new(DefaultPath);

    /// <summary>
    /// terminal-colours.json under RetroTerm in the platform's application-data folder.
    /// </summary>
    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "RetroTerm",
        "terminal-colours.json");

    /// <summary>
    /// The saved presets, in the order they were saved.
    /// </summary>
    public IReadOnlyList<TerminalColourPreset> Presets
    {
        get
        {
            lock (_lock)
            {
                return EnsureLoaded().ToArray();
            }
        }
    }

    /// <summary>
    /// Saves a preset, replacing one with the same name (names are compared ignoring case).
    /// A built-in name is refused, because it would hide the built-in one in the menus.
    /// </summary>
    /// <param name="preset">
    /// The preset to save. Both colours must be valid hex.
    /// </param>
    /// <param name="error">
    /// Why it was refused, or null.
    /// </param>
    /// <returns>
    /// True when it was saved.
    /// </returns>
    public bool TrySave(TerminalColourPreset preset, out string? error)
    {
        var name = preset.Name?.Trim() ?? "";
        if (name.Length == 0)
        {
            error = "Give the preset a name.";
            return false;
        }

        if (!TerminalColourMath.TryParseHex(preset.Foreground, out _) ||
            !TerminalColourMath.TryParseHex(preset.Background, out _))
        {
            error = "Both colours must be hex values like #00FF88.";
            return false;
        }

        foreach (var builtIn in TerminalColourPresets.BuiltIn)
        {
            if (string.Equals(builtIn.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                error = $"\"{builtIn.Name}\" is a built-in preset. Choose another name.";
                return false;
            }
        }

        var normalised = new TerminalColourPreset(
            name,
            TerminalColourMath.Normalise(preset.Foreground),
            TerminalColourMath.Normalise(preset.Background));

        lock (_lock)
        {
            var list = EnsureLoaded();
            var existing = list.FindIndex(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
            if (existing >= 0) list[existing] = normalised; else list.Add(normalised);
            Write(list);
        }

        error = null;
        Changed?.Invoke();
        return true;
    }

    /// <summary>
    /// Removes a saved preset. Does nothing when there is none of that name.
    /// </summary>
    /// <param name="name">
    /// The preset name, compared ignoring case.
    /// </param>
    /// <returns>
    /// True when something was removed.
    /// </returns>
    public bool Remove(string name)
    {
        bool removed;
        lock (_lock)
        {
            var list = EnsureLoaded();
            removed = list.RemoveAll(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)) > 0;
            if (removed) Write(list);
        }

        if (removed) Changed?.Invoke();
        return removed;
    }

    private List<TerminalColourPreset> EnsureLoaded()
    {
        if (_presets != null) return _presets;

        _presets = new List<TerminalColourPreset>();
        try
        {
            if (File.Exists(_path))
            {
                var stored = JsonSerializer.Deserialize<List<StoredPreset>>(File.ReadAllText(_path));
                if (stored != null)
                {
                    foreach (var item in stored)
                    {
                        // A hand-edited or damaged entry is skipped, not allowed to break the menu.
                        if (!string.IsNullOrWhiteSpace(item.Name)
                            && TerminalColourMath.TryParseHex(item.Foreground, out _)
                            && TerminalColourMath.TryParseHex(item.Background, out _))
                        {
                            _presets.Add(new TerminalColourPreset(
                                item.Name!.Trim(),
                                TerminalColourMath.Normalise(item.Foreground!),
                                TerminalColourMath.Normalise(item.Background!)));
                        }
                    }
                }
            }
        }
        catch
        {
            // An unreadable file means "no saved presets", never a crash at start-up.
        }

        return _presets;
    }

    private void Write(List<TerminalColourPreset> list)
    {
        try
        {
            var dir = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            var stored = list.Select(p => new StoredPreset
            {
                Name = p.Name,
                Foreground = p.Foreground,
                Background = p.Background,
            }).ToList();
            File.WriteAllText(_path, JsonSerializer.Serialize(stored, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
            // Not critical: the preset still works this session.
        }
    }

    private sealed class StoredPreset
    {
        public string? Name { get; set; }
        public string? Foreground { get; set; }
        public string? Background { get; set; }
    }
}

/// <summary>
/// Hex parsing and the contrast check behind the colour picker's warning.
/// </summary>
public static class TerminalColourMath
{
    /// <summary>
    /// Below this ratio text is hard to read for most people. 4.5 is the WCAG AA level for normal
    /// text, which is what a terminal's body text is.
    /// </summary>
    public const double ReadableContrast = 4.5;

    /// <summary>
    /// Below this ratio the text is close to invisible.
    /// </summary>
    public const double UnreadableContrast = 3.0;

    /// <summary>
    /// Parses "#RGB", "#RRGGBB" or the same without the '#'.
    /// </summary>
    /// <param name="text">
    /// The text to parse.
    /// </param>
    /// <param name="colour">
    /// Receives the colour on success.
    /// </param>
    /// <returns>
    /// True when the text is a valid colour.
    /// </returns>
    public static bool TryParseHex(string? text, out (byte R, byte G, byte B) colour)
    {
        colour = default;
        if (string.IsNullOrWhiteSpace(text)) return false;

        var hex = text.Trim();
        if (hex.StartsWith('#')) hex = hex.Substring(1);

        if (hex.Length == 3)
        {
            hex = new string(new[] { hex[0], hex[0], hex[1], hex[1], hex[2], hex[2] });
        }

        if (hex.Length != 6) return false;

        if (!byte.TryParse(hex.AsSpan(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var r) ||
            !byte.TryParse(hex.AsSpan(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var g) ||
            !byte.TryParse(hex.AsSpan(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var b))
        {
            return false;
        }

        colour = (r, g, b);
        return true;
    }

    /// <summary>
    /// "#RRGGBB" in capitals.
    /// </summary>
    public static string ToHex(byte r, byte g, byte b)
        => "#" + r.ToString("X2", CultureInfo.InvariantCulture)
               + g.ToString("X2", CultureInfo.InvariantCulture)
               + b.ToString("X2", CultureInfo.InvariantCulture);

    /// <summary>
    /// A valid colour text written as "#RRGGBB" in capitals. The text is returned unchanged
    /// when it is not a valid colour.
    /// </summary>
    public static string Normalise(string text)
        => TryParseHex(text, out var c) ? ToHex(c.R, c.G, c.B) : text;

    /// <summary>
    /// The WCAG contrast ratio between two colours: 1 for identical, 21 for black on white.
    /// </summary>
    public static double ContrastRatio((byte R, byte G, byte B) a, (byte R, byte G, byte B) b)
    {
        var la = RelativeLuminance(a);
        var lb = RelativeLuminance(b);
        var lighter = Math.Max(la, lb);
        var darker = Math.Min(la, lb);
        return (lighter + 0.05) / (darker + 0.05);
    }

    /// <summary>
    /// The warning to show for a pair of colours, or null when they read well.
    /// </summary>
    /// <param name="foreground">
    /// Foreground colour text.
    /// </param>
    /// <param name="background">
    /// Background colour text.
    /// </param>
    public static string? ContrastWarning(string? foreground, string? background)
    {
        if (!TryParseHex(foreground, out var fg) || !TryParseHex(background, out var bg)) return null;

        var ratio = ContrastRatio(fg, bg);
        var shown = ratio.ToString("0.0", CultureInfo.InvariantCulture);

        if (ratio < UnreadableContrast)
            return $"Very low contrast ({shown}:1). Text will be almost impossible to read.";
        if (ratio < ReadableContrast)
            return $"Low contrast ({shown}:1). Text may be hard to read. Aim for at least 4.5:1.";
        return null;
    }

    private static double RelativeLuminance((byte R, byte G, byte B) c)
        => 0.2126 * Linear(c.R) + 0.7152 * Linear(c.G) + 0.0722 * Linear(c.B);

    private static double Linear(byte channel)
    {
        var s = channel / 255.0;
        return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
    }
}
