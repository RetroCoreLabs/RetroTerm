using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace RetroTerm.Core.Terminal.Emulators.TDV
{
    /// <summary>
    /// Single source of truth for all TDV2200 keyboard visual metadata.
    /// Every key's geometry, rendering mode, and visual hints are defined here.
    /// Renderer code reads metadata — no hardcoded grid-position checks needed.
    /// </summary>
    public static class TDV2200KeyVisualRegistry
    {
        // Layout constants (authoritative — other classes should reference these)
        public const double StandardKeyWidth = 60.0;
        public const double StandardKeyHeight = 60.0;
        public const double KeySpacing = 5.0;
        public const double TotalWidth = 1570.0;
        public const double TotalHeight = 450.0;

        // X-position constants for the three keyboard areas
        private const double MAIN_AREA_X = 20;
        private const double NAV_AREA_X = 1074;
        private const double FUNC_AREA_X = 1314;

        // Shorthand
        private const double KW = StandardKeyWidth;
        private const double KH = StandardKeyHeight;
        private const double KS = KeySpacing;

        // Scale factor: spec defines geometry at 72px baseline, we use 60px
        private const double SPEC_SCALE = KW / 72.0;

        // Special key widths (from spec, scaled)
        private const double KW_CAPS = 105 * SPEC_SCALE;
        private const double KW_C0 = 91 * SPEC_SCALE;
        private const double KW_C12 = 86 * SPEC_SCALE;
        private const double KW_D13 = 105 * SPEC_SCALE;
        private const double KW_SHIFT = 127.5 * SPEC_SCALE;
        private const double KH_RETURN = 148 * SPEC_SCALE;

        // Main area right edge
        private const double MAIN_RIGHT_EDGE = MAIN_AREA_X + KW_CAPS + KS + 13 * (KW + KS) + KW;

        // Row Y positions
        private const double ROW_A_Y = 390;
        private const double ROW_B_Y = 325;
        private const double ROW_C_Y = 260;
        private const double ROW_D_Y = 195;
        private const double ROW_E_Y = 130;
        private const double ROW_F_Y = 65;
        private const double ROW_G_Y = 0;

        // Computed row start positions
        private static readonly double B_STD_START = MAIN_AREA_X + KW_SHIFT + KS;
        private static readonly double C0_X = MAIN_AREA_X + KW + KS;
        private static readonly double C_STD_START = C0_X + KW_C0 + KS;
        private static readonly double D0_X = MAIN_AREA_X + KW + KS;
        private static readonly double D_STD_START = D0_X + KW + KS;
        private static readonly double E_STD_START = MAIN_AREA_X + KW_CAPS + KS;

        private static readonly TDVKeyVisualMetadata[] _allKeys;
        private static readonly Dictionary<string, int> _gridIndex;

        static TDV2200KeyVisualRegistry()
        {
            var keys = new List<TDVKeyVisualMetadata>(90);
            _gridIndex = new Dictionary<string, int>(90, StringComparer.Ordinal);

            // === ROW A (bottom) ===
            // A5 - Spacebar (Ctrl+Space → NUL 0x00)
            double spaceX = B_STD_START + 2 * (KW + KS);
            double spaceW = B_STD_START + 9 * (KW + KS) + KW - spaceX;
            Add(keys, "A5", 'A', 5, spaceX, ROW_A_Y, spaceW, KH,
                TDVKeyColor.White, KeyCategory.System, KeyRenderMode.Text,
                hasLED: false, symbolId: null, symbolSizeFactor: 0.40, symbolFilled: true,
                multiLineTexts: null, overlayText: null, isBindable: true, vk: 32,
                ctrlSequence: "\x00");

            // A47-A49 Navigation (symbols)
            Add(keys, "A47", 'A', 47, NavX(0), ROW_A_Y, KW, KH,
                TDVKeyColor.Brown, KeyCategory.Navigation, KeyRenderMode.Symbol,
                hasLED: false, symbolId: "A47", symbolSizeFactor: 0.40, symbolFilled: true,
                multiLineTexts: null, overlayText: null, isBindable: true, vk: 0);
            Add(keys, "A48", 'A', 48, NavX(1), ROW_A_Y, KW, KH,
                TDVKeyColor.Brown, KeyCategory.Navigation, KeyRenderMode.Symbol,
                hasLED: false, symbolId: "A48", symbolSizeFactor: 0.40, symbolFilled: true,
                multiLineTexts: null, overlayText: null, isBindable: true, vk: 40);
            Add(keys, "A49", 'A', 49, NavX(2), ROW_A_Y, KW, KH,
                TDVKeyColor.Brown, KeyCategory.Navigation, KeyRenderMode.Symbol,
                hasLED: false, symbolId: "A49", symbolSizeFactor: 0.40, symbolFilled: true,
                multiLineTexts: null, overlayText: null, isBindable: true, vk: 0);

            // A51 - KP0 (double-wide), A53 - KP decimal
            Add(keys, "A51", 'A', 51, FuncX(0), ROW_A_Y, KW * 2 + KS, KH,
                TDVKeyColor.White, KeyCategory.NumericPad, KeyRenderMode.Text,
                hasLED: false, symbolId: null, symbolSizeFactor: 0.40, symbolFilled: true,
                multiLineTexts: null, overlayText: null, isBindable: true, vk: 96);
            Add(keys, "A53", 'A', 53, FuncX(2), ROW_A_Y, KW, KH,
                TDVKeyColor.White, KeyCategory.NumericPad, KeyRenderMode.Text,
                hasLED: false, symbolId: null, symbolSizeFactor: 0.40, symbolFilled: true,
                multiLineTexts: null, overlayText: null, isBindable: true, vk: 110);

            // === ROW B ===
            // B99 - Left Shift
            Add(keys, "B99", 'B', 99, MAIN_AREA_X, ROW_B_Y, KW_SHIFT, KH,
                TDVKeyColor.White, KeyCategory.Modifier, KeyRenderMode.Text,
                hasLED: false, symbolId: null, symbolSizeFactor: 0.40, symbolFilled: true,
                multiLineTexts: null, overlayText: null, isBindable: false, vk: 160);

            // B0 - < > key, VK_OEM_102 (226), the key left of Z on an ISO keyboard. Held Z's VK 90
            // until 27 September 2026 - the comment here said it "matched the existing registry",
            // which it did, and the registry was shifted one key to the right across the row.
            Add(keys, "B0", 'B', 0, B_STD_START, ROW_B_Y, KW, KH,
                TDVKeyColor.White, KeyCategory.Alphanumeric, KeyRenderMode.Text,
                hasLED: false, symbolId: null, symbolSizeFactor: 0.40, symbolFilled: true,
                multiLineTexts: null, overlayText: null, isBindable: true, vk: 226);

            // B1-B9 - Z X C V B N M , .
            int[] bVKs = { 90, 88, 67, 86, 66, 78, 77, 188, 190 }; // Z X C V B N M , . - the PC key with the same label
            for (int i = 0; i < 9; i++)
            {
                Add(keys, $"B{i + 1}", 'B', i + 1,
                    B_STD_START + (i + 1) * (KW + KS), ROW_B_Y, KW, KH,
                    TDVKeyColor.White, KeyCategory.Alphanumeric, KeyRenderMode.Text,
                    hasLED: false, symbolId: null, symbolSizeFactor: 0.40, symbolFilled: true,
                    multiLineTexts: null, overlayText: null, isBindable: true, vk: bVKs[i]);
            }

            // B10 - Minus/Underscore
            Add(keys, "B10", 'B', 10, B_STD_START + 10 * (KW + KS), ROW_B_Y, KW, KH,
                TDVKeyColor.White, KeyCategory.Alphanumeric, KeyRenderMode.Text,
                hasLED: false, symbolId: null, symbolSizeFactor: 0.40, symbolFilled: true,
                multiLineTexts: null, overlayText: null, isBindable: true, vk: 189);

            // B11 - Right Shift
            Add(keys, "B11", 'B', 11, B_STD_START + 11 * (KW + KS), ROW_B_Y, KW_SHIFT, KH,
                TDVKeyColor.White, KeyCategory.Modifier, KeyRenderMode.Text,
                hasLED: false, symbolId: null, symbolSizeFactor: 0.40, symbolFilled: true,
                multiLineTexts: null, overlayText: null, isBindable: false, vk: 161);

            // B47-B49 Navigation (symbols)
            Add(keys, "B47", 'B', 47, NavX(0), ROW_B_Y, KW, KH,
                TDVKeyColor.Brown, KeyCategory.Navigation, KeyRenderMode.Symbol,
                hasLED: false, symbolId: "B47", symbolSizeFactor: 0.40, symbolFilled: true,
                multiLineTexts: null, overlayText: null, isBindable: true, vk: 37);
            Add(keys, "B48", 'B', 48, NavX(1), ROW_B_Y, KW, KH,
                TDVKeyColor.Brown, KeyCategory.Navigation, KeyRenderMode.Symbol,
                hasLED: false, symbolId: "B48", symbolSizeFactor: 0.40, symbolFilled: true,
                multiLineTexts: null, overlayText: null, isBindable: true, vk: 36);
            Add(keys, "B49", 'B', 49, NavX(2), ROW_B_Y, KW, KH,
                TDVKeyColor.Brown, KeyCategory.Navigation, KeyRenderMode.Symbol,
                hasLED: false, symbolId: "B49", symbolSizeFactor: 0.40, symbolFilled: true,
                multiLineTexts: null, overlayText: null, isBindable: true, vk: 39);

            // B51-B53 Numpad 1-3
            for (int i = 0; i < 3; i++)
            {
                Add(keys, $"B{51 + i}", 'B', 51 + i, FuncX(i), ROW_B_Y, KW, KH,
                    TDVKeyColor.White, KeyCategory.NumericPad, KeyRenderMode.Text,
                    hasLED: false, symbolId: null, symbolSizeFactor: 0.40, symbolFilled: true,
                    multiLineTexts: null, overlayText: null, isBindable: true, vk: 97 + i);
            }

            // B54 - ENTER (tall key, vertical letter stack)
            Add(keys, "B54", 'B', 54, FuncX(3), ROW_B_Y, KW, KH * 2 + KS,
                TDVKeyColor.White, KeyCategory.System, KeyRenderMode.VerticalLetterStack,
                hasLED: false, symbolId: null, symbolSizeFactor: 0.40, symbolFilled: true,
                multiLineTexts: null, overlayText: null, isBindable: true, vk: 13);

            // === ROW C ===
            // C99 - MODE
            Add(keys, "C99", 'C', 99, MAIN_AREA_X, ROW_C_Y, KW, KH,
                TDVKeyColor.Orange, KeyCategory.ApplicationControl, KeyRenderMode.Text,
                hasLED: false, symbolId: null, symbolSizeFactor: 0.40, symbolFilled: true,
                multiLineTexts: null, overlayText: null, isBindable: true, vk: 0);

            // C0 - LOCK (toggle with LED)
            Add(keys, "C0", 'C', 0, C0_X, ROW_C_Y, KW_C0, KH,
                TDVKeyColor.White, KeyCategory.Toggle, KeyRenderMode.Text,
                hasLED: true, symbolId: null, symbolSizeFactor: 0.40, symbolFilled: true,
                multiLineTexts: null, overlayText: null, isBindable: false, vk: 20,
                indicator: "\u2022");

            // C1-C11 - A S D F G H J K L ; '
            int[] cVKs = { 65, 83, 68, 70, 71, 72, 74, 75, 76, 186, 222 };
            for (int i = 0; i < 11; i++)
            {
                Add(keys, $"C{i + 1}", 'C', i + 1,
                    C_STD_START + i * (KW + KS), ROW_C_Y, KW, KH,
                    TDVKeyColor.White, KeyCategory.Alphanumeric, KeyRenderMode.Text,
                    hasLED: false, symbolId: null, symbolSizeFactor: 0.40, symbolFilled: true,
                    multiLineTexts: null, overlayText: null, isBindable: true, vk: cVKs[i]);
            }

            // C12 - key before Enter (wider)
            Add(keys, "C12", 'C', 12, C_STD_START + 11 * (KW + KS), ROW_C_Y, KW_C12, KH,
                TDVKeyColor.White, KeyCategory.Alphanumeric, KeyRenderMode.Text,
                hasLED: false, symbolId: null, symbolSizeFactor: 0.40, symbolFilled: true,
                multiLineTexts: null, overlayText: null, isBindable: true, vk: 220);

            // C13 - RETURN (tall key, symbol with larger size factor)
            double c13X = MAIN_RIGHT_EDGE - KW;
            Add(keys, "C13", 'C', 13, c13X, ROW_C_Y, KW, KH_RETURN,
                TDVKeyColor.Orange, KeyCategory.System, KeyRenderMode.Symbol,
                hasLED: false, symbolId: "C13", symbolSizeFactor: 0.55, symbolFilled: true,
                multiLineTexts: null, overlayText: null, isBindable: true, vk: 13);

            // C47-C49 Navigation (symbols, stroke-only for C47/C49)
            Add(keys, "C47", 'C', 47, NavX(0), ROW_C_Y, KW, KH,
                TDVKeyColor.Brown, KeyCategory.ApplicationControl, KeyRenderMode.Symbol,
                hasLED: false, symbolId: "C47", symbolSizeFactor: 0.40, symbolFilled: false,
                multiLineTexts: null, overlayText: null, isBindable: true, vk: 0);
            Add(keys, "C48", 'C', 48, NavX(1), ROW_C_Y, KW, KH,
                TDVKeyColor.Brown, KeyCategory.Navigation, KeyRenderMode.Symbol,
                hasLED: false, symbolId: "C48", symbolSizeFactor: 0.40, symbolFilled: true,
                multiLineTexts: null, overlayText: null, isBindable: true, vk: 38);
            Add(keys, "C49", 'C', 49, NavX(2), ROW_C_Y, KW, KH,
                TDVKeyColor.Brown, KeyCategory.ApplicationControl, KeyRenderMode.Symbol,
                hasLED: false, symbolId: "C49", symbolSizeFactor: 0.40, symbolFilled: false,
                multiLineTexts: null, overlayText: null, isBindable: true, vk: 0);

            // C51-C54 Numpad 4-6, Minus
            for (int i = 0; i < 3; i++)
            {
                Add(keys, $"C{51 + i}", 'C', 51 + i, FuncX(i), ROW_C_Y, KW, KH,
                    TDVKeyColor.White, KeyCategory.NumericPad, KeyRenderMode.Text,
                    hasLED: false, symbolId: null, symbolSizeFactor: 0.40, symbolFilled: true,
                    multiLineTexts: null, overlayText: null, isBindable: true, vk: 100 + i);
            }
            Add(keys, "C54", 'C', 54, FuncX(3), ROW_C_Y, KW, KH,
                TDVKeyColor.White, KeyCategory.NumericPad, KeyRenderMode.Text,
                hasLED: false, symbolId: null, symbolSizeFactor: 0.40, symbolFilled: true,
                multiLineTexts: null, overlayText: null, isBindable: true, vk: 109);

            // === ROW D ===
            // D99 - INNS/EXPS
            Add(keys, "D99", 'D', 99, MAIN_AREA_X, ROW_D_Y, KW, KH,
                TDVKeyColor.Orange, KeyCategory.ApplicationControl, KeyRenderMode.Text,
                hasLED: false, symbolId: null, symbolSizeFactor: 0.40, symbolFilled: true,
                multiLineTexts: null, overlayText: null, isBindable: true, vk: 45);

            // D0 - CTRL
            Add(keys, "D0", 'D', 0, D0_X, ROW_D_Y, KW, KH,
                TDVKeyColor.White, KeyCategory.Modifier, KeyRenderMode.Text,
                hasLED: false, symbolId: null, symbolSizeFactor: 0.40, symbolFilled: true,
                multiLineTexts: null, overlayText: null, isBindable: false, vk: 162);

            // D1-D12 - Q W E R T Y U I O P [ ]
            int[] dVKs = { 81, 87, 69, 82, 84, 89, 85, 73, 79, 80, 219, 221 };
            for (int i = 0; i < 12; i++)
            {
                Add(keys, $"D{i + 1}", 'D', i + 1,
                    D_STD_START + i * (KW + KS), ROW_D_Y, KW, KH,
                    TDVKeyColor.White, KeyCategory.Alphanumeric, KeyRenderMode.Text,
                    hasLED: false, symbolId: null, symbolSizeFactor: 0.40, symbolFilled: true,
                    multiLineTexts: null, overlayText: null, isBindable: true, vk: dVKs[i]);
            }

            // D13 - LF (symbol, wider key)
            double d13X = D_STD_START + 12 * (KW + KS);
            Add(keys, "D13", 'D', 13, d13X, ROW_D_Y, KW_D13, KH,
                TDVKeyColor.Orange, KeyCategory.System, KeyRenderMode.Symbol,
                hasLED: false, symbolId: "D13", symbolSizeFactor: 0.40, symbolFilled: true,
                multiLineTexts: null, overlayText: null, isBindable: true, vk: 10);

            // D47 - RollUp (symbol, stroke-only, 50% size)
            Add(keys, "D47", 'D', 47, NavX(0), ROW_D_Y, KW, KH,
                TDVKeyColor.Brown, KeyCategory.ApplicationControl, KeyRenderMode.Symbol,
                hasLED: false, symbolId: "D47", symbolSizeFactor: 0.50, symbolFilled: false,
                multiLineTexts: null, overlayText: null, isBindable: true, vk: 33);

            // D48 - ANGRE/UNDO
            Add(keys, "D48", 'D', 48, NavX(1), ROW_D_Y, KW, KH,
                TDVKeyColor.Orange, KeyCategory.ApplicationControl, KeyRenderMode.Text,
                hasLED: false, symbolId: null, symbolSizeFactor: 0.40, symbolFilled: true,
                multiLineTexts: null, overlayText: null, isBindable: true, vk: 0);

            // D49 - RollDown (symbol, stroke-only, 50% size)
            Add(keys, "D49", 'D', 49, NavX(2), ROW_D_Y, KW, KH,
                TDVKeyColor.Brown, KeyCategory.ApplicationControl, KeyRenderMode.Symbol,
                hasLED: false, symbolId: "D49", symbolSizeFactor: 0.50, symbolFilled: false,
                multiLineTexts: null, overlayText: null, isBindable: true, vk: 34);

            // D51-D53 Numpad 7-9
            for (int i = 0; i < 3; i++)
            {
                Add(keys, $"D{51 + i}", 'D', 51 + i, FuncX(i), ROW_D_Y, KW, KH,
                    TDVKeyColor.White, KeyCategory.NumericPad, KeyRenderMode.Text,
                    hasLED: false, symbolId: null, symbolSizeFactor: 0.40, symbolFilled: true,
                    multiLineTexts: null, overlayText: null, isBindable: true, vk: 103 + i);
            }

            // D54 - Numpad space. VK 0: no PC key is a numpad space, and VK 32 is the A5 spacebar.
            // Held 32 until 27 September 2026, which made a physical spacebar light this key.
            Add(keys, "D54", 'D', 54, FuncX(3), ROW_D_Y, KW, KH,
                TDVKeyColor.White, KeyCategory.NumericPad, KeyRenderMode.Text,
                hasLED: false, symbolId: null, symbolSizeFactor: 0.40, symbolFilled: true,
                multiLineTexts: null, overlayText: null, isBindable: true, vk: 0);

            // === ROW E ===
            // E0 - CAPS (toggle with LED)
            Add(keys, "E0", 'E', 0, MAIN_AREA_X, ROW_E_Y, KW_CAPS, KH,
                TDVKeyColor.White, KeyCategory.Toggle, KeyRenderMode.Text,
                hasLED: true, symbolId: null, symbolSizeFactor: 0.40, symbolFilled: true,
                multiLineTexts: null, overlayText: null, isBindable: false, vk: 20,
                indicator: "\u2022");

            // E1 - 1/!
            Add(keys, "E1", 'E', 1, E_STD_START, ROW_E_Y, KW, KH,
                TDVKeyColor.White, KeyCategory.Alphanumeric, KeyRenderMode.Text,
                hasLED: false, symbolId: null, symbolSizeFactor: 0.40, symbolFilled: true,
                multiLineTexts: null, overlayText: null, isBindable: true, vk: 49);

            // E2-E10 - number keys
            int[] eVKs = { 50, 51, 52, 53, 54, 55, 56, 57, 48 };
            for (int i = 0; i < 9; i++)
            {
                Add(keys, $"E{i + 2}", 'E', i + 2,
                    E_STD_START + (i + 1) * (KW + KS), ROW_E_Y, KW, KH,
                    TDVKeyColor.White, KeyCategory.Alphanumeric, KeyRenderMode.Text,
                    hasLED: false, symbolId: null, symbolSizeFactor: 0.40, symbolFilled: true,
                    multiLineTexts: null, overlayText: null, isBindable: true, vk: eVKs[i]);
            }

            // E11 - +/?
            Add(keys, "E11", 'E', 11, E_STD_START + 10 * (KW + KS), ROW_E_Y, KW, KH,
                TDVKeyColor.White, KeyCategory.Alphanumeric, KeyRenderMode.Text,
                hasLED: false, symbolId: null, symbolSizeFactor: 0.40, symbolFilled: true,
                multiLineTexts: null, overlayText: null, isBindable: true, vk: 189);

            // E12 - |/`
            Add(keys, "E12", 'E', 12, E_STD_START + 11 * (KW + KS), ROW_E_Y, KW, KH,
                TDVKeyColor.White, KeyCategory.Alphanumeric, KeyRenderMode.Text,
                hasLED: false, symbolId: null, symbolSizeFactor: 0.40, symbolFilled: true,
                multiLineTexts: null, overlayText: null, isBindable: true, vk: 187);

            // E13 - backspace/newpara (symbol, stroke-only)
            Add(keys, "E13", 'E', 13, E_STD_START + 12 * (KW + KS), ROW_E_Y, KW, KH,
                TDVKeyColor.Orange, KeyCategory.Alphanumeric, KeyRenderMode.Symbol,
                hasLED: false, symbolId: "E13", symbolSizeFactor: 0.40, symbolFilled: false,
                multiLineTexts: null, overlayText: null, isBindable: true, vk: 192);

            // E14 - DEL (text "a" with diagonal slash overlay). Ctrl+DEL sends DEL 0x7F like the plain
            // key: spec section 4.1, row E14, Ctrl column is 0x7F. Sent NUL until 27 September 2026.
            Add(keys, "E14", 'E', 14, E_STD_START + 13 * (KW + KS), ROW_E_Y, KW, KH,
                TDVKeyColor.Orange, KeyCategory.System, KeyRenderMode.TextWithOverlay,
                hasLED: false, symbolId: "E14", symbolSizeFactor: 0.40, symbolFilled: false,
                multiLineTexts: null, overlayText: "a", isBindable: true, vk: 0,
                ctrlSequence: "\x7F");

            // E47 - chevrons >> / << (two-line bold text)
            Add(keys, "E47", 'E', 47, NavX(0), ROW_E_Y, KW, KH,
                TDVKeyColor.Orange, KeyCategory.ApplicationControl, KeyRenderMode.TwoLineText,
                hasLED: false, symbolId: null, symbolSizeFactor: 0.40, symbolFilled: true,
                multiLineTexts: new[] { ">>", "<<" }, overlayText: null, isBindable: true, vk: 0,
                multiLineBold: true);

            // E48 - JUST
            Add(keys, "E48", 'E', 48, NavX(1), ROW_E_Y, KW, KH,
                TDVKeyColor.Orange, KeyCategory.ApplicationControl, KeyRenderMode.Text,
                hasLED: false, symbolId: null, symbolSizeFactor: 0.40, symbolFilled: true,
                multiLineTexts: null, overlayText: null, isBindable: true, vk: 0);

            // E49 - single guillemets >< / <> (two-line bold text)
            Add(keys, "E49", 'E', 49, NavX(2), ROW_E_Y, KW, KH,
                TDVKeyColor.Orange, KeyCategory.ApplicationControl, KeyRenderMode.TwoLineText,
                hasLED: false, symbolId: null, symbolSizeFactor: 0.40, symbolFilled: true,
                multiLineTexts: new[] { "><", "<>" }, overlayText: null, isBindable: true, vk: 0,
                multiLineBold: true);

            // E51-E54 Function keys F5-F8
            string[] eIndicators = { "000", "00", "0", "+" };
            for (int i = 0; i < 4; i++)
            {
                Add(keys, $"E{51 + i}", 'E', 51 + i, FuncX(i), ROW_E_Y, KW, KH,
                    TDVKeyColor.Orange, KeyCategory.Function, KeyRenderMode.Text,
                    hasLED: false, symbolId: null, symbolSizeFactor: 0.40, symbolFilled: true,
                    multiLineTexts: null, overlayText: null, isBindable: true, vk: 116 + i,
                    indicator: eIndicators[i]);
            }

            // === ROW F ===
            // F47 - TAB (three-line text: -/TAB/+)
            Add(keys, "F47", 'F', 47, NavX(0), ROW_F_Y, KW, KH,
                TDVKeyColor.Orange, KeyCategory.System, KeyRenderMode.ThreeLineText,
                hasLED: false, symbolId: null, symbolSizeFactor: 0.40, symbolFilled: true,
                multiLineTexts: new[] { "-", "TAB", "+" }, overlayText: null, isBindable: true, vk: 9);

            // F48 - Search (two-line text: ...)/(...  )
            Add(keys, "F48", 'F', 48, NavX(1), ROW_F_Y, KW, KH,
                TDVKeyColor.Orange, KeyCategory.ApplicationControl, KeyRenderMode.TwoLineText,
                hasLED: false, symbolId: null, symbolSizeFactor: 0.40, symbolFilled: true,
                multiLineTexts: new[] { "...)", "(...", }, overlayText: null, isBindable: true, vk: 0);

            // F49 - Replace (two-line text: aaa/aaa with underline on second)
            Add(keys, "F49", 'F', 49, NavX(2), ROW_F_Y, KW, KH,
                TDVKeyColor.Orange, KeyCategory.ApplicationControl, KeyRenderMode.TwoLineText,
                hasLED: false, symbolId: null, symbolSizeFactor: 0.40, symbolFilled: true,
                multiLineTexts: new[] { "aaa", "aaa" }, overlayText: null, isBindable: true, vk: 0,
                secondLineUnderlined: true);

            // F51-F54 Function keys F1-F4
            // F51 (F1) has no corner indicator, hence the null entry.
            string?[] fIndicators = { null, "SI", "SO", "CLEAR" };
            for (int i = 0; i < 4; i++)
            {
                Add(keys, $"F{51 + i}", 'F', 51 + i, FuncX(i), ROW_F_Y, KW, KH,
                    TDVKeyColor.Orange, KeyCategory.Function, KeyRenderMode.Text,
                    hasLED: false, symbolId: null, symbolSizeFactor: 0.40, symbolFilled: true,
                    multiLineTexts: null, overlayText: null, isBindable: true, vk: 112 + i,
                    indicator: fIndicators[i]);
            }

            // === ROW G (top) ===
            // G0 - ESC
            Add(keys, "G0", 'G', 0, MAIN_AREA_X, ROW_G_Y, KW, KH,
                TDVKeyColor.Orange, KeyCategory.System, KeyRenderMode.Text,
                hasLED: false, symbolId: null, symbolSizeFactor: 0.40, symbolFilled: true,
                multiLineTexts: null, overlayText: null, isBindable: true, vk: 27);

            // G1-G8 - PUSH keys P1-P8
            for (int i = 1; i <= 8; i++)
            {
                Add(keys, $"G{i}", 'G', i, MAIN_AREA_X + i * (KW + KS), ROW_G_Y, KW, KH,
                    TDVKeyColor.Brown, KeyCategory.PushKey, KeyRenderMode.Text,
                    hasLED: false, symbolId: null, symbolSizeFactor: 0.40, symbolFilled: true,
                    multiLineTexts: null, overlayText: null, isBindable: true, vk: 0);
            }

            // G9-G13 Application control keys (MERK, FELT, AVSN, SETN, ORD)
            Add(keys, "G9", 'G', 9, MAIN_AREA_X + 9 * (KW + KS), ROW_G_Y, KW, KH,
                TDVKeyColor.Orange, KeyCategory.ApplicationControl, KeyRenderMode.Text,
                hasLED: false, symbolId: null, symbolSizeFactor: 0.40, symbolFilled: true,
                multiLineTexts: null, overlayText: null, isBindable: true, vk: 0);
            Add(keys, "G10", 'G', 10, MAIN_AREA_X + 10 * (KW + KS), ROW_G_Y, KW, KH,
                TDVKeyColor.Orange, KeyCategory.ApplicationControl, KeyRenderMode.Text,
                hasLED: false, symbolId: null, symbolSizeFactor: 0.40, symbolFilled: true,
                multiLineTexts: null, overlayText: null, isBindable: true, vk: 0);
            Add(keys, "G11", 'G', 11, MAIN_AREA_X + 11 * (KW + KS), ROW_G_Y, KW, KH,
                TDVKeyColor.Orange, KeyCategory.ApplicationControl, KeyRenderMode.Text,
                hasLED: false, symbolId: null, symbolSizeFactor: 0.40, symbolFilled: true,
                multiLineTexts: null, overlayText: null, isBindable: true, vk: 0);
            Add(keys, "G12", 'G', 12, MAIN_AREA_X + 12 * (KW + KS), ROW_G_Y, KW, KH,
                TDVKeyColor.Orange, KeyCategory.ApplicationControl, KeyRenderMode.Text,
                hasLED: false, symbolId: null, symbolSizeFactor: 0.40, symbolFilled: true,
                multiLineTexts: null, overlayText: null, isBindable: true, vk: 0);
            Add(keys, "G13", 'G', 13, MAIN_AREA_X + 13 * (KW + KS), ROW_G_Y, KW, KH,
                TDVKeyColor.Orange, KeyCategory.ApplicationControl, KeyRenderMode.Text,
                hasLED: false, symbolId: null, symbolSizeFactor: 0.40, symbolFilled: true,
                multiLineTexts: null, overlayText: null, isBindable: true, vk: 0);

            // G14 - LOKAL (extended width, NOT bindable)
            double g14X = MAIN_AREA_X + 14 * (KW + KS);
            Add(keys, "G14", 'G', 14, g14X, ROW_G_Y, MAIN_RIGHT_EDGE - g14X, KH,
                TDVKeyColor.Brown, KeyCategory.Local, KeyRenderMode.Text,
                hasLED: false, symbolId: null, symbolSizeFactor: 0.40, symbolFilled: true,
                multiLineTexts: null, overlayText: null, isBindable: false, vk: 0);

            // G47-G49 Application keys (STRYK, KOPI, FLYTT)
            Add(keys, "G47", 'G', 47, NavX(0), ROW_G_Y, KW, KH,
                TDVKeyColor.Orange, KeyCategory.ApplicationControl, KeyRenderMode.Text,
                hasLED: false, symbolId: null, symbolSizeFactor: 0.40, symbolFilled: true,
                multiLineTexts: null, overlayText: null, isBindable: true, vk: 46);
            Add(keys, "G48", 'G', 48, NavX(1), ROW_G_Y, KW, KH,
                TDVKeyColor.Orange, KeyCategory.ApplicationControl, KeyRenderMode.Text,
                hasLED: false, symbolId: null, symbolSizeFactor: 0.40, symbolFilled: true,
                multiLineTexts: null, overlayText: null, isBindable: true, vk: 0);
            Add(keys, "G49", 'G', 49, NavX(2), ROW_G_Y, KW, KH,
                TDVKeyColor.Orange, KeyCategory.ApplicationControl, KeyRenderMode.Text,
                hasLED: false, symbolId: null, symbolSizeFactor: 0.40, symbolFilled: true,
                multiLineTexts: null, overlayText: null, isBindable: true, vk: 0);

            // G51-G54 Function area (FUNK, SKRIV, HJELP, SLUTT)
            Add(keys, "G51", 'G', 51, FuncX(0), ROW_G_Y, KW, KH,
                TDVKeyColor.Orange, KeyCategory.ApplicationControl, KeyRenderMode.Text,
                hasLED: false, symbolId: null, symbolSizeFactor: 0.40, symbolFilled: true,
                multiLineTexts: null, overlayText: null, isBindable: true, vk: 0);
            Add(keys, "G52", 'G', 52, FuncX(1), ROW_G_Y, KW, KH,
                TDVKeyColor.Orange, KeyCategory.ApplicationControl, KeyRenderMode.Text,
                hasLED: false, symbolId: null, symbolSizeFactor: 0.40, symbolFilled: true,
                multiLineTexts: null, overlayText: null, isBindable: true, vk: 44);
            Add(keys, "G53", 'G', 53, FuncX(2), ROW_G_Y, KW, KH,
                TDVKeyColor.Orange, KeyCategory.ApplicationControl, KeyRenderMode.Text,
                hasLED: false, symbolId: null, symbolSizeFactor: 0.40, symbolFilled: true,
                multiLineTexts: null, overlayText: null, isBindable: true, vk: 0);
            Add(keys, "G54", 'G', 54, FuncX(3), ROW_G_Y, KW, KH,
                TDVKeyColor.Orange, KeyCategory.ApplicationControl, KeyRenderMode.Text,
                hasLED: false, symbolId: null, symbolSizeFactor: 0.40, symbolFilled: true,
                multiLineTexts: null, overlayText: null, isBindable: true, vk: 35);

            _allKeys = keys.ToArray();
        }

        /// <summary>
        /// Get all key metadata (read-only snapshot)
        /// </summary>
        public static TDVKeyVisualMetadata[] GetAllKeys()
        {
            var copy = new TDVKeyVisualMetadata[_allKeys.Length];
            Array.Copy(_allKeys, copy, _allKeys.Length);
            return copy;
        }

        /// <summary>
        /// Number of registered keys
        /// </summary>
        public static int KeyCount => _allKeys.Length;

        /// <summary>
        /// Try to look up a key by grid position
        /// </summary>
        public static bool TryGetKey(string gridPosition, [NotNullWhen(true)] out TDVKeyVisualMetadata? meta)
        {
            if (_gridIndex.TryGetValue(gridPosition, out int idx))
            {
                meta = _allKeys[idx];
                return true;
            }
            meta = null;
            return false;
        }

        /// <summary>
        /// Export all key metadata to JSON (for TypeScript/HTML consumers)
        /// </summary>
        public static string ExportToJson()
        {
            var sb = new StringBuilder(8192);
            sb.Append('[');
            for (int i = 0; i < _allKeys.Length; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append('\n');
                AppendKeyJson(sb, _allKeys[i]);
            }
            sb.Append("\n]");
            return sb.ToString();
        }

        // --- Private helpers ---

        private static double NavX(int col) => NAV_AREA_X + col * (KW + KS);
        private static double FuncX(int col) => FUNC_AREA_X + col * (KW + KS);

        private static void Add(
            List<TDVKeyVisualMetadata> keys,
            string gridPosition, char row, int column,
            double x, double y, double width, double height,
            TDVKeyColor color, KeyCategory category, KeyRenderMode renderMode,
            bool hasLED, string? symbolId, double symbolSizeFactor, bool symbolFilled,
            string[]? multiLineTexts, string? overlayText,
            bool isBindable, int vk,
            bool secondLineUnderlined = false, bool multiLineBold = false,
            string? indicator = null, string? ctrlSequence = null)
        {
            var meta = new TDVKeyVisualMetadata(
                gridPosition, row, column,
                x, y, width, height,
                color, category, renderMode,
                hasLED, symbolId, symbolSizeFactor, symbolFilled,
                multiLineTexts, overlayText,
                isBindable, vk,
                secondLineUnderlined, multiLineBold,
                indicator, ctrlSequence);

            _gridIndex[gridPosition] = keys.Count;
            keys.Add(meta);
        }

        private static void AppendKeyJson(StringBuilder sb, TDVKeyVisualMetadata k)
        {
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            sb.Append("  {");
            sb.AppendFormat(inv, "\"gridPosition\":\"{0}\"", k.GridPosition);
            sb.AppendFormat(inv, ",\"row\":\"{0}\",\"column\":{1}", k.Row, k.Column);
            sb.AppendFormat(inv, ",\"x\":{0:F2},\"y\":{1:F2},\"width\":{2:F2},\"height\":{3:F2}",
                k.X, k.Y, k.Width, k.Height);
            sb.AppendFormat(",\"color\":\"{0}\"", k.Color);
            sb.AppendFormat(",\"category\":\"{0}\"", k.Category);
            sb.AppendFormat(",\"renderMode\":\"{0}\"", k.RenderMode);
            sb.AppendFormat(",\"hasLED\":{0}", k.HasLED ? "true" : "false");

            if (k.SymbolId != null)
                sb.AppendFormat(",\"symbolId\":\"{0}\"", k.SymbolId);
            else
                sb.Append(",\"symbolId\":null");

            sb.AppendFormat(inv, ",\"symbolSizeFactor\":{0:F2}", k.SymbolSizeFactor);
            sb.AppendFormat(",\"symbolFilled\":{0}", k.SymbolFilled ? "true" : "false");

            if (k.MultiLineTexts != null)
            {
                sb.Append(",\"multiLineTexts\":[");
                for (int i = 0; i < k.MultiLineTexts.Length; i++)
                {
                    if (i > 0) sb.Append(',');
                    sb.AppendFormat("\"{0}\"", EscapeJson(k.MultiLineTexts[i]));
                }
                sb.Append(']');
            }
            else
            {
                sb.Append(",\"multiLineTexts\":null");
            }

            if (k.OverlayText != null)
                sb.AppendFormat(",\"overlayText\":\"{0}\"", EscapeJson(k.OverlayText));
            else
                sb.Append(",\"overlayText\":null");

            sb.AppendFormat(",\"isBindable\":{0}", k.IsBindable ? "true" : "false");
            sb.AppendFormat(",\"virtualKeyCode\":{0}", k.VirtualKeyCode);
            sb.AppendFormat(",\"secondLineUnderlined\":{0}", k.SecondLineUnderlined ? "true" : "false");
            sb.AppendFormat(",\"multiLineBold\":{0}", k.MultiLineBold ? "true" : "false");

            if (k.Indicator != null)
                sb.AppendFormat(",\"indicator\":\"{0}\"", EscapeJson(k.Indicator));
            else
                sb.Append(",\"indicator\":null");

            sb.Append('}');
        }

        private static string EscapeJson(string s)
        {
            if (s == null) return "";
            // Simple JSON string escaping for common cases
            var sb = new StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20)
                            sb.AppendFormat("\\u{0:X4}", (int)c);
                        else
                            sb.Append(c);
                        break;
                }
            }
            return sb.ToString();
        }
    }
}
