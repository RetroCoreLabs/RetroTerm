using System;

namespace RetroTerm.Core.Terminal.Emulators.TDV;

/// <summary>
/// The languages a connection can be set to, and the ISO 646 variant each one selects.
/// </summary>
/// <remarks>
/// <para><b>Why this exists</b></para>
/// The connection dialog used to hold a typed-in list of six languages while the mapping that
/// turns a language into a character set lived somewhere else entirely. The two disagreed: the
/// dialog offered Finnish, and the mapping had no case for it, so choosing Finnish silently gave
/// US ASCII and the user lost every one of the letters they picked it for.
///
/// The list and the mapping are the same data here, so a language cannot be offered without an
/// answer for what it does.
///
/// <para><b>What the terminal actually has</b></para>
/// A TDV has four ISO 646 variants: International, Norwegian, Swedish and German. Languages
/// outside that set are mapped to the closest one the hardware can produce, and each such mapping
/// is stated below rather than left to a default.
/// </remarks>
public static class TDVLanguageOptions
{
    /// <summary>
    /// One language a user can choose, and what it does.
    /// </summary>
    public readonly struct LanguageOption
    {
        /// <summary>
        /// The name shown in the dialog.
        /// </summary>
        public readonly string DisplayName;

        /// <summary>
        /// The two-letter code accepted as an alternative spelling.
        /// </summary>
        public readonly string Code;

        /// <summary>
        /// The character set variant this language selects.
        /// </summary>
        public readonly TDV2200ISO646Variant Variant;

        /// <summary>
        /// Why this language maps where it does, when that is not obvious.
        /// </summary>
        public readonly string Note;

        /// <summary>
        /// Creates a language option.
        /// </summary>
        /// <param name="displayName">
        /// Name shown to the user.
        /// </param>
        /// <param name="code">
        /// Two-letter alternative spelling.
        /// </param>
        /// <param name="variant">
        /// The variant selected.
        /// </param>
        /// <param name="note">
        /// Why, when the mapping is not one-to-one.
        /// </param>
        public LanguageOption(string displayName, string code, TDV2200ISO646Variant variant, string note)
        {
            DisplayName = displayName;
            Code = code;
            Variant = variant;
            Note = note;
        }
    }

    /// <summary>
    /// Every language offered, in the order the dialog lists them.
    /// </summary>
    /// <remarks>
    /// Finnish and Swedish share ISO-IR-10 - the Finnish standard SFS 4017 and the Swedish
    /// SEN 850200 register the same set - so Finnish selects the Swedish variant and gets the
    /// letters it needs. It used to fall through to International and get none of them.
    ///
    /// Danish is the approximation in this list. DS 2089 is a REGISTRATION OF ITS OWN and is not
    /// the same set as Norway's NS 4551, but a TDV has no Danish variant, and the Norwegian one is
    /// the closest the hardware can produce. Said plainly here rather than described as identical,
    /// which is what the old comment claimed.
    /// </remarks>
    public static readonly LanguageOption[] All =
    {
        new LanguageOption("Norwegian", "no", TDV2200ISO646Variant.Norwegian, "NS 4551."),
        new LanguageOption("Swedish", "sv", TDV2200ISO646Variant.Swedish, "SEN 850200, ISO-IR-10."),
        new LanguageOption("Finnish", "fi", TDV2200ISO646Variant.Swedish,
            "SFS 4017 registers the same set as Swedish, ISO-IR-10."),
        new LanguageOption("Danish", "da", TDV2200ISO646Variant.Norwegian,
            "DS 2089 is its own set; a TDV has no Danish variant, so the Norwegian one is the closest available."),
        new LanguageOption("German", "de", TDV2200ISO646Variant.German, "DIN 66003."),
        new LanguageOption("English", "en", TDV2200ISO646Variant.International, "US ASCII, no substitutions."),
    };

    /// <summary>
    /// The display names, for a dropdown.
    /// </summary>
    /// <returns>
    /// The name of every language offered, in list order.
    /// </returns>
    public static string[] DisplayNames()
    {
        var names = new string[All.Length];
        for (int i = 0; i < All.Length; i++)
        {
            names[i] = All[i].DisplayName;
        }
        return names;
    }

    /// <summary>
    /// Turns a language into the character set variant it selects.
    /// </summary>
    /// <param name="language">
    /// A display name or a two-letter code; matching ignores case.
    /// </param>
    /// <returns>
    /// The variant for that language, or International when the language is empty or unrecognised -
    /// International being plain US ASCII, which substitutes nothing and so is the safe answer.
    /// </returns>
    public static TDV2200ISO646Variant ToVariant(string? language)
    {
        if (string.IsNullOrWhiteSpace(language))
        {
            return TDV2200ISO646Variant.International;
        }

        var wanted = language.Trim();
        for (int i = 0; i < All.Length; i++)
        {
            if (string.Equals(All[i].DisplayName, wanted, StringComparison.OrdinalIgnoreCase)
                || string.Equals(All[i].Code, wanted, StringComparison.OrdinalIgnoreCase))
            {
                return All[i].Variant;
            }
        }

        // "dk" was the code this accepted for Danish before the list existed, and connections saved
        // with it are still on disk.
        if (string.Equals(wanted, "dk", StringComparison.OrdinalIgnoreCase))
        {
            return TDV2200ISO646Variant.Norwegian;
        }

        return TDV2200ISO646Variant.International;
    }
}
