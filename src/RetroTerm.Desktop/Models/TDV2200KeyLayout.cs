using System.Collections.Generic;
using RetroTerm.Core.Terminal.Emulators.TDV;

namespace RetroTerm.Desktop.Models
{
    /// <summary>
    /// TDV2200 keyboard layout definition.
    /// Creates per-key labels from TDV2200KeyRegistry national variants and default labels.
    /// Key geometry and metadata come from TDV2200KeyVisualRegistry (Core layer).
    /// </summary>
    public static class TDV2200KeyLayout
    {
        // Default labels for keys that don't have national variants in the registry.
        // Key = grid position, Value = (primary, shifted, indicator).
        // shifted/indicator are nullable: most keys have neither.
        // Keys not in this table use national labels from TDV2200KeyRegistry.
        private static readonly Dictionary<string, (string primary, string? shifted, string? indicator)> _defaultLabels
            = new Dictionary<string, (string, string?, string?)>
            {
                // Row A
                ["A5"] = (" ", null, null),
                ["A47"] = ("\u2190|", null, null),
                ["A48"] = ("\u2193", null, null),
                ["A49"] = ("\u2192|", null, null),
                ["A51"] = ("0", null, null),
                ["A53"] = (".", null, null),

                // Row B
                ["B99"] = ("SHIFT", null, null),
                ["B10"] = ("-", "_", null),
                ["B11"] = ("", null, null),
                ["B47"] = ("\u2190", null, null),
                ["B48"] = ("HOME", null, null),
                ["B49"] = ("\u2192", null, null),
                ["B51"] = ("1", null, null),
                ["B52"] = ("2", null, null),
                ["B53"] = ("3", null, null),
                ["B54"] = ("ENTER", null, null),

                // Row C
                ["C99"] = ("MODE", null, null),
                ["C0"] = ("LOCK", null, "\u2022"),
                ["C13"] = ("RETURN", null, null),
                ["C47"] = ("\u21D0", null, null),
                ["C48"] = ("\u2191", null, null),
                ["C49"] = ("\u21D2", null, null),
                ["C51"] = ("4", null, null),
                ["C52"] = ("5", null, null),
                ["C53"] = ("6", null, null),
                ["C54"] = ("-", null, null),

                // Row D
                ["D99"] = ("INNS", "EXPS", null),
                ["D0"] = ("CTRL", null, null),
                ["D13"] = ("LF", null, null),
                ["D47"] = ("\u21D1", "\u21D0", null),
                ["D48"] = ("ANGRE", null, null),
                ["D49"] = ("\u21D3", "\u21D2", null),
                ["D51"] = ("7", null, null),
                ["D52"] = ("8", null, null),
                ["D53"] = ("9", null, null),
                ["D54"] = ("\u2334", null, null),

                // Row E
                ["E0"] = ("CAPS", null, "\u2022"),
                ["E1"] = ("1", "!", null),
                ["E3"] = ("3", "#", null),
                ["E5"] = ("5", "%", null),
                ["E14"] = ("DEL", null, null),
                ["E47"] = ("\u00AB", "\u00BB", null),
                ["E48"] = ("JUST", null, null),
                ["E49"] = ("\u2039\u203A", "\u203A\u2039", null),
                ["E51"] = ("F5", null, "000"),
                ["E52"] = ("F6", null, "00"),
                ["E53"] = ("F7", null, "0"),
                ["E54"] = ("F8", null, "+"),

                // Row F
                ["F47"] = ("TAB", "-", "+"),
                ["F48"] = ("...)", "(...", null),
                ["F49"] = ("aaa", "aaa", null),
                ["F51"] = ("F1", null, null),
                ["F52"] = ("F2", null, "SI"),
                ["F53"] = ("F3", null, "SO"),
                ["F54"] = ("F4", null, "CLEAR"),

                // Row G
                ["G0"] = ("ESC", null, null),
            };

        /// <summary>
        /// Create the complete TDV2200 keyboard layout with per-key labels.
        /// </summary>
        public static KeyboardLayout CreateLayout()
        {
            var layout = new KeyboardLayout();
            var allKeys = TDV2200KeyVisualRegistry.GetAllKeys();

            for (int i = 0; i < allKeys.Length; i++)
            {
                var meta = allKeys[i];
                layout.KeyLabels[meta.GridPosition] = CreateLabels(meta);
            }

            return layout;
        }

        /// <summary>
        /// Create per-language labels for a key.
        /// First tries national labels from TDV2200KeyRegistry, then falls back to default labels.
        /// PUSH keys (G1-G8) get their P1-P8 labels.
        /// </summary>
        private static Dictionary<string, KeyLabel> CreateLabels(TDVKeyVisualMetadata meta)
        {
            var gp = meta.GridPosition;

            // PUSH keys: P1-P8
            if (meta.Category == KeyCategory.PushKey && meta.Column >= 1 && meta.Column <= 8)
            {
                return CreateUniformLabel($"P{meta.Column}", null, null, gp);
            }

            // Try national labels from registry first
            var dict = new Dictionary<string, KeyLabel>();
            var langCodes = TDV2200KeyRegistry.LanguageCodes;
            bool anyFound = false;

            for (int i = 0; i < langCodes.Length; i++)
            {
                if (TDV2200KeyRegistry.TryGetLabel(gp, langCodes[i], out var regLabel))
                {
                    // Get indicator from visual metadata
                    string? indicator = meta.Indicator;
                    dict[langCodes[i]] = new KeyLabel(regLabel.Primary, regLabel.Shifted, null, indicator);
                    anyFound = true;
                }
            }

            if (anyFound)
            {
                // Fill any missing language codes with Norwegian fallback
                if (dict.ContainsKey("no"))
                {
                    var noLabel = dict["no"];
                    for (int i = 0; i < langCodes.Length; i++)
                    {
                        if (!dict.ContainsKey(langCodes[i]))
                        {
                            dict[langCodes[i]] = noLabel;
                        }
                    }
                }

                // Add Keyboard debug layout
                dict["Keyboard"] = new KeyLabel(gp, null, null, null);
                return dict;
            }

            // Fall back to default labels table
            if (_defaultLabels.TryGetValue(gp, out var defaults))
            {
                return CreateUniformLabel(defaults.primary, defaults.shifted, defaults.indicator, gp);
            }

            // Final fallback: use grid position as label
            return CreateUniformLabel(gp, null, null, gp);
        }

        /// <summary>
        /// Create a label that is the same across all languages.
        /// </summary>
        private static Dictionary<string, KeyLabel> CreateUniformLabel(string primary, string? shifted, string? indicator, string gridPos)
        {
            var dict = new Dictionary<string, KeyLabel>();
            var label = new KeyLabel(primary, shifted, null, indicator);
            var langCodes = TDV2200KeyRegistry.LanguageCodes;

            for (int i = 0; i < langCodes.Length; i++)
            {
                dict[langCodes[i]] = label;
            }

            if (gridPos != null)
            {
                dict["Keyboard"] = new KeyLabel(gridPos, null, null, null);
            }

            return dict;
        }
    }
}
