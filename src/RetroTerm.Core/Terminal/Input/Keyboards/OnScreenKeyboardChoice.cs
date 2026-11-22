//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs/RetroTerm
// RetroTerm — Connecting today's machines to yesterday's systems
//

using System.Collections.Generic;

namespace RetroTerm.Core.Terminal.Input.Keyboards
{
    /// <summary>
    /// Remembers which on-screen keyboard was chosen for each key family, so changing tabs back
    /// and forth keeps the person's choice (the 3278 or the 3270 PC keyboard, say).
    /// </summary>
    /// <remarks>
    /// Kept for the life of the program, not saved: the window starts on each terminal's usual
    /// keyboard (the first one it offers).
    /// </remarks>
    public static class OnScreenKeyboardChoice
    {
        private static readonly Dictionary<string, string> s_chosenByFamily = new Dictionary<string, string>();

        /// <summary>
        /// The keyboard to show from a terminal's list: the one chosen last for its family, or the
        /// first.
        /// </summary>
        /// <param name="keyboards">
        /// The terminal's keyboards, at least one.
        /// </param>
        /// <returns>
        /// The index into <paramref name="keyboards"/>.
        /// </returns>
        public static int IndexToShow(IReadOnlyList<KeyboardDescription> keyboards)
        {
            if (s_chosenByFamily.TryGetValue(keyboards[0].Family, out string? chosenId))
            {
                for (int i = 0; i < keyboards.Count; i++)
                {
                    if (keyboards[i].Id == chosenId)
                    {
                        return i;
                    }
                }
            }

            return 0;
        }

        /// <summary>
        /// Records a choice the person made.
        /// </summary>
        /// <param name="keyboard">
        /// The keyboard chosen.
        /// </param>
        public static void Remember(KeyboardDescription keyboard)
        {
            s_chosenByFamily[keyboard.Family] = keyboard.Id;
        }
    }
}
