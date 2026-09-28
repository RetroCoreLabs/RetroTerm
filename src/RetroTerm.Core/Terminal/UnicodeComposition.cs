using System;
using System.Globalization;
using System.Text;

namespace RetroTerm.Core.Terminal;

/// <summary>
/// Combines a base character and a combining mark into the single precomposed codepoint Unicode
/// defines for the pair, where one exists.
///
/// WHY A TERMINAL NEEDS THIS. A cell holds one codepoint. A combining mark occupies no column of
/// its own — it modifies the character before it — so it cannot be given a cell, and it cannot be
/// thrown away either without losing the accent the host asked for. Composing "e" + U+0301 into
/// U+00E9 stores the accented letter in the one cell it belongs in.
///
/// LIMIT, STATED RATHER THAN HIDDEN: this only works where Unicode HAS a precomposed form. Latin,
/// Greek and Cyrillic accents almost always do. Devanagari, Thai and stacked multi-accent
/// sequences do not, and those marks are dropped. Carrying them properly needs a cell that can
/// hold a sequence of codepoints, which is a change to the cell model.
///
/// The normalisation call allocates, so it is reached only when a combining mark actually
/// arrives — vanishingly rare in terminal traffic, and never on the ASCII path.
/// </summary>
public static class UnicodeComposition
{
    /// <summary>
    /// Returns the precomposed form of <paramref name="baseCodepoint"/> with
    /// <paramref name="combining"/>, or <paramref name="baseCodepoint"/> unchanged when Unicode
    /// has no single character for that pair.
    /// </summary>
    public static uint Compose(uint baseCodepoint, uint combining)
    {
        if (baseCodepoint == 0)
        {
            return baseCodepoint;
        }

        // Outside the Basic Multilingual Plane the pair cannot be a precomposed Latin form, and
        // surrogate handling would only add cost for a case that never composes.
        if (baseCodepoint > 0xFFFF || combining > 0xFFFF)
        {
            return baseCodepoint;
        }

        try
        {
            Span<char> pair = stackalloc char[2];
            pair[0] = (char)baseCodepoint;
            pair[1] = (char)combining;

            string composed = new string(pair).Normalize(NormalizationForm.FormC);

            // Exactly one character back means the pair really did compose. Two means it did not,
            // and the mark has no home.
            if (composed.Length == 1)
            {
                return composed[0];
            }
        }
        catch (ArgumentException)
        {
            // Normalize throws on invalid sequences (an unpaired surrogate, for instance). A
            // character that cannot be normalised simply does not compose.
        }

        return baseCodepoint;
    }
}
