using System.Collections.Generic;
using RetroTerm.Core.Terminal.Emulators.TDV;

namespace RetroTerm.Desktop.Models
{
    /// <summary>
    /// Label information for a key in a specific language
    /// </summary>
    public class KeyLabel
    {
        /// <summary>
        /// Primary label (shown in center of key)
        /// </summary>
        public string Primary { get; set; }

        /// <summary>
        /// Shifted label (shown above primary, smaller). Null when the key has none.
        /// </summary>
        public string? Shifted { get; set; }

        /// <summary>
        /// Alternative label (shown below, for dual-labeled keys). Null when the key has none.
        /// </summary>
        public string? Alternative { get; set; }

        /// <summary>
        /// Special indicator text (for function keys, shown in corner). Null when the key has none.
        /// </summary>
        public string? Indicator { get; set; }

        public KeyLabel(string primary, string? shifted = null, string? alternative = null, string? indicator = null)
        {
            Primary = primary;
            Shifted = shifted;
            Alternative = alternative;
            Indicator = indicator;
        }
    }

    /// <summary>
    /// National keyboard layout variants supported by TDV2200
    /// </summary>
    public enum NationalKeyboardLayout
    {
        Norwegian,   // no
        Danish,      // dk
        Swedish,     // sv
        German,      // de
        USASCII,     // us
        French,      // fr
        SDS,         // sds (Norsk Data Standard)
        FAO,         // fao
        English,     // en (UK)
        Swiss,       // ch
        Finnish,     // fi
        Icelandic,   // is
        Keyboard     // Grid position debug mode
    }

    /// <summary>
    /// Complete keyboard layout definition.
    /// Holds per-key labels indexed by grid position and language code.
    /// Key geometry and metadata come from TDV2200KeyVisualRegistry.
    /// </summary>
    public class KeyboardLayout
    {
        /// <summary>
        /// Per-key labels: KeyLabels[gridPosition][languageCode] = KeyLabel
        /// </summary>
        public Dictionary<string, Dictionary<string, KeyLabel>> KeyLabels { get; set; }

        /// <summary>
        /// Standard key width in layout units
        /// </summary>
        public const double StandardKeyWidth = TDV2200KeyVisualRegistry.StandardKeyWidth;

        /// <summary>
        /// Standard key height in layout units
        /// </summary>
        public const double StandardKeyHeight = TDV2200KeyVisualRegistry.StandardKeyHeight;

        /// <summary>
        /// Spacing between keys in layout units
        /// </summary>
        public const double KeySpacing = TDV2200KeyVisualRegistry.KeySpacing;

        /// <summary>
        /// Total keyboard width in layout units
        /// </summary>
        public const double TotalWidth = TDV2200KeyVisualRegistry.TotalWidth;

        /// <summary>
        /// Total keyboard height in layout units
        /// </summary>
        public const double TotalHeight = TDV2200KeyVisualRegistry.TotalHeight;

        public KeyboardLayout()
        {
            KeyLabels = new Dictionary<string, Dictionary<string, KeyLabel>>();
        }

        /// <summary>
        /// Get language code for a national layout
        /// </summary>
        public static string GetLanguageCode(NationalKeyboardLayout layout)
        {
            return layout switch
            {
                NationalKeyboardLayout.Norwegian => "no",
                NationalKeyboardLayout.Danish => "dk",
                NationalKeyboardLayout.Swedish => "sv",
                NationalKeyboardLayout.German => "de",
                NationalKeyboardLayout.USASCII => "us",
                NationalKeyboardLayout.French => "fr",
                NationalKeyboardLayout.SDS => "sds",
                NationalKeyboardLayout.FAO => "fao",
                NationalKeyboardLayout.English => "en",
                NationalKeyboardLayout.Swiss => "ch",
                NationalKeyboardLayout.Finnish => "fi",
                NationalKeyboardLayout.Icelandic => "is",
                NationalKeyboardLayout.Keyboard => "Keyboard",
                _ => "no"
            };
        }

        /// <summary>
        /// Get display name for a national layout
        /// </summary>
        public static string GetDisplayName(NationalKeyboardLayout layout)
        {
            return layout switch
            {
                NationalKeyboardLayout.Norwegian => "Norwegian",
                NationalKeyboardLayout.Danish => "Danish",
                NationalKeyboardLayout.Swedish => "Swedish",
                NationalKeyboardLayout.German => "German",
                NationalKeyboardLayout.USASCII => "US ASCII",
                NationalKeyboardLayout.French => "French",
                NationalKeyboardLayout.SDS => "SDS (Norsk Data)",
                NationalKeyboardLayout.FAO => "FAO",
                NationalKeyboardLayout.English => "English (UK)",
                NationalKeyboardLayout.Swiss => "Swiss",
                NationalKeyboardLayout.Finnish => "Finnish",
                NationalKeyboardLayout.Icelandic => "Icelandic",
                NationalKeyboardLayout.Keyboard => "Keyboard",
                _ => "Norwegian"
            };
        }
    }
}
