//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs/RetroTerm
// RetroTerm — Connecting today's machines to yesterday's systems
//

using System;
using System.Collections.Generic;

using RetroTerm.Core.Terminal.Emulators;

namespace RetroTerm.Core.Terminal.Input
{
    /// <summary>
    /// The VT key names SENDKEY knows (VT.PF1, VT.UP, VT.FIND, ...), and the ONE path a VT key press
    /// takes to become text for the host - used by the terminal window for a real key press and by
    /// SENDKEY for a named one, so the two cannot send different bytes.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A name only stands for a key code. What the key sends is decided by the keyboard mapper with
    /// the emulator's live modes (DECCKM, DECKPAM/DECKPNM, S7C1T/S8C1T) and by any DECUDK definition
    /// the host loaded, exactly as for a key pressed on the PC keyboard.
    /// </para>
    /// <para>
    /// The keys and their codes come from the VT420 Programmer Reference, EK-VT420-RM-002 (held as
    /// docs\spec\DEC\VT420-Programmer-Reference-EK-VT420-RM-002.pdf), Chapter 3:
    /// Table 3-1 (editing keys), Table 3-3 (arrow keys), Table 3-4 (numeric keypad) and Table 3-6
    /// (top-row function keys). Keys no held manual covers are left out: the keypad's divide,
    /// multiply and plus keys appear only for the PC keyboard's PC key layout, and F1 to F5 are
    /// local functions (Hold, Print, Set-Up, Session, Break) on a real VT420, so on the PC keyboard
    /// here F1 to F4 are PF1 to PF4.
    /// </para>
    /// </remarks>
    public static class VtNamedKeys
    {
        // Windows virtual-key codes, named so the table below reads as keys, not numbers.
        private const int VkPrior = 33;
        private const int VkNext = 34;
        private const int VkLeft = 37;
        private const int VkUp = 38;
        private const int VkRight = 39;
        private const int VkDown = 40;
        private const int VkInsert = 45;
        private const int VkDelete = 46;
        private const int VkNumpad0 = 96;
        private const int VkSeparator = 108;
        private const int VkSubtract = 109;
        private const int VkDecimal = 110;
        private const int VkF1 = 112;
        private const int VkF6 = 117;

        private static readonly Dictionary<string, int> Codes = Build(out NamesInOrder);

        private static readonly string[] NamesInOrder;

        /// <summary>
        /// Every VT key name, upper case, without the family prefix, in a fixed order.
        /// </summary>
        public static IReadOnlyList<string> Names => NamesInOrder;

        /// <summary>
        /// The key code for a VT key name.
        /// </summary>
        /// <param name="name">
        /// The name without the family prefix, any case.
        /// </param>
        /// <param name="keyCode">
        /// The key code the keyboard mapper takes.
        /// </param>
        /// <returns>
        /// True when the name is a VT key.
        /// </returns>
        public static bool TryGetKeyCode(string name, out int keyCode)
        {
            return Codes.TryGetValue(name, out keyCode);
        }

        /// <summary>
        /// What one key press sends to the host: a DECUDK definition the host loaded for this key
        /// first, otherwise the keyboard mapper with the emulator's current modes.
        /// </summary>
        /// <param name="emulator">
        /// The terminal, whose modes decide the encoding.
        /// </param>
        /// <param name="mapper">
        /// The terminal's keyboard mapper.
        /// </param>
        /// <param name="keyCode">
        /// The key code (a Windows virtual-key code, or one of the DEC-only codes on BaseKeyboardMapper).
        /// </param>
        /// <param name="modifiers">
        /// The modifiers held.
        /// </param>
        /// <returns>
        /// The text to send, or null when the key sends nothing.
        /// </returns>
        public static string? Encode(TerminalEmulatorBase emulator, IKeyboardMapper mapper, int keyCode, KeyModifiers modifiers)
        {
            // A key the host loaded with DECUDK sends what the host asked for, not its factory
            // sequence. Asked BEFORE the mapper, because that substitution is the whole point of
            // the feature; keys with no definition fall straight through.
            if (emulator.TryGetUserDefinedKey(keyCode, out var defined))
            {
                return System.Text.Encoding.ASCII.GetString(defined);
            }

            TerminalModes modes = emulator.GetActiveModes();
            string? mapped = mapper.MapKey(keyCode, modifiers, modes);
            if (mapped != null)
            {
                return mapped;
            }

            // Numeric keypad mode: a keypad key is the character printed on it (VT420 Programmer
            // Reference Table 3-4, "Numeric" column). A real key press delivers that character
            // through the text path, not the mapper - which must not answer for it, or the digit
            // arrives twice - so the text path's answer is given here for a named press.
            if (modifiers == KeyModifiers.None && !modes.HasFlag(TerminalModes.ApplicationKeypad))
            {
                if (keyCode >= VkNumpad0 && keyCode <= VkNumpad0 + 9)
                {
                    return ((char)('0' + (keyCode - VkNumpad0))).ToString();
                }

                switch (keyCode)
                {
                    case VkSeparator: return ",";
                    case VkSubtract: return "-";
                    case VkDecimal: return ".";
                }
            }

            return null;
        }

        private static Dictionary<string, int> Build(out string[] order)
        {
            var list = new List<KeyValuePair<string, int>>(48)
            {
                // Table 3-3, arrow keys: CSI A..D, or SS3 A..D under DECCKM.
                new("UP", VkUp),
                new("DOWN", VkDown),
                new("RIGHT", VkRight),
                new("LEFT", VkLeft),

                // Table 3-4, PF1-PF4: SS3 P..S in both keypad modes. On the PC keyboard these are F1-F4.
                new("PF1", VkF1),
                new("PF2", VkF1 + 1),
                new("PF3", VkF1 + 2),
                new("PF4", VkF1 + 3),

                // Table 3-4, the rest of the numeric keypad: the printed character in numeric mode,
                // SS3 p..y / l / m / n / M in application mode.
                new("KPCOMMA", VkSeparator),
                new("KPMINUS", VkSubtract),
                new("KPPERIOD", VkDecimal),
                new("KPENTER", BaseKeyboardMapper.DecKeypadEnter),

                // Table 3-1, editing keys: CSI 1~ .. CSI 6~.
                new("FIND", BaseKeyboardMapper.DecFind),
                new("INSERT", VkInsert),
                new("REMOVE", VkDelete),
                new("SELECT", BaseKeyboardMapper.DecSelect),
                new("PREV", VkPrior),
                new("NEXT", VkNext),
            };

            for (int d = 0; d <= 9; d++)
            {
                list.Add(new("KP" + d, VkNumpad0 + d));
            }

            // Table 3-6, top-row keys F6-F20 (F15 is Help and F16 is Do on the LK401; their CSI
            // numbers are 28 and 29). Windows VK_F6..VK_F20 are consecutive.
            for (int f = 6; f <= 20; f++)
            {
                list.Add(new("F" + f, VkF6 + (f - 6)));
            }

            var codes = new Dictionary<string, int>(list.Count, StringComparer.OrdinalIgnoreCase);
            order = new string[list.Count];
            for (int i = 0; i < list.Count; i++)
            {
                codes.Add(list[i].Key, list[i].Value);
                order[i] = list[i].Key;
            }

            return codes;
        }
    }
}
