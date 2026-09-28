using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace RetroTerm.Core.Terminal.Emulators.TDV
{
    /// <summary>
    /// Key color on the physical ND-246 keyboard
    /// </summary>
    public enum TDVKeyColor : byte
    {
        White = 0,
        Orange = 1,
        Brown = 2
    }

    /// <summary>
    /// Flags describing key behavior
    /// </summary>
    [Flags]
    public enum TDVKeyFlags : byte
    {
        None = 0,
        IsModifier = 1,
        IsToggle = 2,
        IsProgrammable = 4,
        AlwaysSameCode = 8,
        IsNumericPad = 16
    }

    /// <summary>
    /// Immutable definition of a single TDV2200 key.
    /// Owns all data: identity, escape sequences, color, VK code.
    /// </summary>
    public sealed class TDVKeyDefinition
    {
        public string Id { get; }
        public string Name { get; }
        public TDVKeyColor Color { get; }
        public TDVKeyFlags Flags { get; }
        public int VirtualKeyCode { get; }

        // Extended Control Mode sequences (CSI nn _ format).
        // Nullable: most keys have no shifted/ctrl variant, and programmable keys
        // (PUSH keys G1-G8) have no fixed sequence at all. Null means "not defined
        // for this key", which callers already test for.
        public string? ExtNormal { get; }
        public string? ExtShift { get; }
        public string? ExtCtrl { get; }

        // Simple ASCII Mode sequence (C0 control code), null when the key has none.
        public string? SimpleAscii { get; }

        // Numeric pad function mode sequence, null when the key has none.
        public string? NumPadFunc { get; }

        public TDVKeyDefinition(
            string id, string name, TDVKeyColor color, TDVKeyFlags flags,
            int virtualKeyCode,
            string? extNormal, string? extShift, string? extCtrl,
            string? simpleAscii, string? numPadFunc)
        {
            Id = id;
            Name = name;
            Color = color;
            Flags = flags;
            VirtualKeyCode = virtualKeyCode;
            ExtNormal = extNormal;
            ExtShift = extShift;
            ExtCtrl = extCtrl;
            SimpleAscii = simpleAscii;
            NumPadFunc = numPadFunc;
        }
    }

    /// <summary>
    /// Label text for a key in a specific language
    /// </summary>
    public readonly struct TDVKeyLabel
    {
        /// <summary>
        /// Main legend on the keycap. Always present.
        /// </summary>
        public readonly string Primary;

        /// <summary>
        /// Shifted legend, or null when the key has no separate shifted legend.
        /// </summary>
        public readonly string? Shifted;

        /// <summary>
        /// Alternative/third legend, or null when the key has none.
        /// </summary>
        public readonly string? Alternative;

        public TDVKeyLabel(string primary, string? shifted, string? alternative)
        {
            Primary = primary;
            Shifted = shifted;
            Alternative = alternative;
        }
    }

    /// <summary>
    /// Single source of truth for all TDV2200 ND-246 keyboard data.
    /// All key identity, escape sequences, colors, VK codes, labels, and Alt mappings
    /// are defined here. Other classes delegate to this registry.
    /// </summary>
    public static class TDV2200KeyRegistry
    {
        private static readonly Dictionary<string, TDVKeyDefinition> _keys;
        private static readonly Dictionary<string, TDVKeyLabel> _labels;
        private static readonly Dictionary<int, string> _vkToGrid;
        private static readonly Dictionary<string, string> _nameToGrid;
        private static readonly Dictionary<int, string> _defaultAltMap;
        private static readonly Dictionary<int, string> _defaultAltShiftMap;

        public static readonly string[] LanguageCodes =
            { "no", "dk", "sv", "de", "us", "fr", "sds", "fao", "en", "ch", "fi", "is" };

        static TDV2200KeyRegistry()
        {
            _keys = new Dictionary<string, TDVKeyDefinition>(128);
            _labels = new Dictionary<string, TDVKeyLabel>(1024);
            _vkToGrid = new Dictionary<int, string>(64);
            _nameToGrid = new Dictionary<string, string>(128, StringComparer.OrdinalIgnoreCase);
            _defaultAltMap = new Dictionary<int, string>(40);
            _defaultAltShiftMap = new Dictionary<int, string>(8);

            InitializeKeys();
            InitializeLabels();
            InitializeAltMappings();
        }

        // ─── Public API ────────────────────────────────────────────

        public static bool TryGetKey(string gridPosition, [NotNullWhen(true)] out TDVKeyDefinition? key)
        {
            return _keys.TryGetValue(gridPosition, out key);
        }

        /// <summary>
        /// Returns the key definition, or null when the grid position is unknown.
        /// </summary>
        public static TDVKeyDefinition? GetKey(string gridPosition)
        {
            _keys.TryGetValue(gridPosition, out var key);
            return key;
        }

        public static bool TryGetLabel(string gridPosition, string langCode, out TDVKeyLabel label)
        {
            return _labels.TryGetValue(gridPosition + "_" + langCode, out label);
        }

        /// <summary>
        /// Returns the grid position for a VK code, or null when the key has no TDV equivalent.
        /// </summary>
        public static string? GetGridForVK(int vkCode)
        {
            _vkToGrid.TryGetValue(vkCode, out var grid);
            return grid;
        }

        /// <summary>
        /// Returns the grid position for a key name, or null when the name is unknown.
        /// </summary>
        public static string? GetGridForName(string? name)
        {
            if (name == null) return null;
            _nameToGrid.TryGetValue(name, out var grid);
            return grid;
        }

        /// <summary>
        /// Returns the default Alt target grid position, or null when the VK has no default.
        /// </summary>
        public static string? GetDefaultAltTarget(int vkCode)
        {
            _defaultAltMap.TryGetValue(vkCode, out var grid);
            return grid;
        }

        /// <summary>
        /// Get the English label for a key.
        /// Returns the primary label from the "en" language, or the key's Name if no English label exists.
        /// </summary>
        public static string? GetEnglishName(string gridPosition)
        {
            if (_labels.TryGetValue(gridPosition + "_en", out var label) && label.Primary != null)
                return label.Primary;
            if (_keys.TryGetValue(gridPosition, out var key))
                return key.Name;
            return null;
        }


        /// <summary>
        /// THE single method for resolving a key press to its escape sequence.
        /// </summary>
        /// <returns>
        /// The escape sequence, or null when the key has none in this mode.
        /// </returns>
        public static string? GetSequence(string gridPosition,
            bool extendedMode, bool numPadFuncMode,
            bool shift = false, bool ctrl = false)
        {
            if (!_keys.TryGetValue(gridPosition, out var key))
                return null;

            // Programmable keys have no fixed sequence
            if ((key.Flags & TDVKeyFlags.IsProgrammable) != 0)
                return null;

            if (extendedMode)
            {
                // Numeric pad function mode. Checked BEFORE the AlwaysSameCode shortcut because
                // B54 KPENTER is both: CR in every other case, CSI 81 _ in pad function mode
                // (spec 6.8.4). Only keys with a NumPadFunc column are affected.
                if (numPadFuncMode && key.NumPadFunc != null)
                    return key.NumPadFunc;

                // AlwaysSameCode keys: same in both modes, ignore shift
                if ((key.Flags & TDVKeyFlags.AlwaysSameCode) != 0)
                    return key.ExtNormal;

                // Ctrl variant (only F2, F3 have this)
                if (ctrl && key.ExtCtrl != null)
                    return key.ExtCtrl;

                // Shift variant
                if (shift && key.ExtShift != null)
                    return key.ExtShift;

                // Normal
                return key.ExtNormal;
            }
            else
            {
                // Simple ASCII mode
                // AlwaysSameCode keys send same code in simple mode too
                if ((key.Flags & TDVKeyFlags.AlwaysSameCode) != 0)
                    return key.SimpleAscii ?? key.ExtNormal;

                return key.SimpleAscii;
            }
        }

        /// <summary>
        /// Get all key definitions (for iteration by UI code)
        /// </summary>
        public static IReadOnlyDictionary<string, TDVKeyDefinition> AllKeys => _keys;

        /// <summary>
        /// Get the default Alt mappings (VK code -> grid position)
        /// </summary>
        public static IReadOnlyDictionary<int, string> DefaultAltMappings => _defaultAltMap;

        /// <summary>
        /// Default Alt+Shift key bindings: VK code → grid position (shifted=true).
        /// Used by TDVKeyBindingConfiguration.SetDefaults() for shifted push key variants.
        /// </summary>
        public static IReadOnlyDictionary<int, string> DefaultAltShiftMappings => _defaultAltShiftMap;

        // ─── Initialization ────────────────────────────────────────

        private static void Reg(
            string id, string name, TDVKeyColor color, TDVKeyFlags flags,
            int vk,
            string? extNormal, string? extShift, string? extCtrl,
            string? simpleAscii, string? numPadFunc)
        {
            var def = new TDVKeyDefinition(id, name, color, flags, vk,
                extNormal, extShift, extCtrl, simpleAscii, numPadFunc);
            _keys[id] = def;

            // Register name → grid (add both primary and aliases)
            _nameToGrid[name] = id;

            // Register VK → grid (only for non-zero VK that isn't already mapped)
            if (vk > 0 && !_vkToGrid.ContainsKey(vk))
                _vkToGrid[vk] = id;
        }

        private static void Alias(string name, string gridPosition)
        {
            _nameToGrid[name] = gridPosition;
        }

        private static void InitializeKeys()
        {
            // ── G-row (top row) ──────────────────────────────
            Reg("G0", "ESC", TDVKeyColor.Orange, TDVKeyFlags.AlwaysSameCode,
                27, "\x1B", null, null, null, null);

            // PUSH keys G1-G8 (programmable, no fixed sequence)
            for (int i = 1; i <= 8; i++)
            {
                Reg($"G{i}", $"P{i}", TDVKeyColor.Brown, TDVKeyFlags.IsProgrammable,
                    0, null, null, null, null, null);
            }

            Reg("G9", "MERK", TDVKeyColor.Orange, TDVKeyFlags.None,
                0, "\x1B[00_", "\x1B[01_", null, null, null);
            Reg("G10", "FELT", TDVKeyColor.Orange, TDVKeyFlags.None,
                0, "\x1B[02_", "\x1B[03_", null, "\x02", null);
            // AVSN, short for "avsnitt" (paragraph). The keycap photo spec\Keyboards\keys\G11.png
            // reads AVSN; this was registered as "AVSN" until 27 September 2026.
            Reg("G11", "AVSN", TDVKeyColor.Orange, TDVKeyFlags.None,
                0, "\x1B[04_", "\x1B[05_", null, "\x01", null);
            Reg("G12", "SETN", TDVKeyColor.Orange, TDVKeyFlags.None,
                0, "\x1B[06_", "\x1B[07_", null, "\x03", null);
            Reg("G13", "ORD", TDVKeyColor.Orange, TDVKeyFlags.None,
                0, "\x1B[08_", "\x1B[09_", null, null, null);
            Reg("G14", "LOKAL", TDVKeyColor.Brown, TDVKeyFlags.None,
                0, null, null, null, null, null);

            // Navigation area
            //
            // The SimpleAscii (Extended Control Mode OFF, "2115 mode") bytes for STRYK, KOPI,
            // FLYTT, FUNK and the three E-row keys below were missing until 27 September 2026, so
            // in 2115 mode a physical Delete sent ESC[10_ instead of EOT. Values are the 6.8.3
            // table in spec\Keyboards\keyboard-spec.md, which cites the TDV-2200/9 User's Guide.
            Reg("G47", "STRYK", TDVKeyColor.Orange, TDVKeyFlags.None,
                46, "\x1B[10_", "\x1B[11_", null, "\x04", null);
            Reg("G48", "KOPI", TDVKeyColor.Orange, TDVKeyFlags.None,
                0, "\x1B[12_", "\x1B[13_", null, "\x10", null);
            Reg("G49", "FLYTT", TDVKeyColor.Orange, TDVKeyFlags.None,
                0, "\x1B[14_", "\x1B[15_", null, "\x19", null);

            // Function area
            Reg("G51", "FUNK", TDVKeyColor.Orange, TDVKeyFlags.None,
                0, "\x1B[42_", "\x1B[43_", null, "\x16", null);
            Reg("G52", "SKRIV", TDVKeyColor.Orange, TDVKeyFlags.None,
                44, "\x1B[44_", "\x1B[45_", null, null, null);
            Reg("G53", "HJELP", TDVKeyColor.Orange, TDVKeyFlags.None,
                0, "\x1B[46_", "\x1B[47_", null, null, null);
            Reg("G54", "SLUTT", TDVKeyColor.Orange, TDVKeyFlags.None,
                35, "\x1B[48_", "\x1B[49_", null, null, null);

            // ── F-row ────────────────────────────────────────
            Reg("F47", "TAB", TDVKeyColor.Orange, TDVKeyFlags.None,
                9, "\x1B[16_", "\x1B[17_", null, "\x09", null);
            Reg("F48", "SEARCH", TDVKeyColor.Orange, TDVKeyFlags.None,
                0, "\x1B[18_", "\x1B[19_", null, "\x11", null);
            Reg("F49", "REPLACE", TDVKeyColor.Orange, TDVKeyFlags.None,
                0, "\x1B[20_", "\x1B[21_", null, "\x14", null);
            Reg("F51", "F1", TDVKeyColor.Orange, TDVKeyFlags.None,
                112, "\x1B[50_", "\x1B[51_", null, "\x1E", null);
            Reg("F52", "F2", TDVKeyColor.Orange, TDVKeyFlags.None,
                113, "\x1B[52_", "\x1B[53_", "\x1B[54_", "\x1F", null);
            Reg("F53", "F3", TDVKeyColor.Orange, TDVKeyFlags.None,
                114, "\x1B[55_", "\x1B[56_", "\x1B[57_", "\x18", null);
            Reg("F54", "F4", TDVKeyColor.Orange, TDVKeyFlags.None,
                115, "\x1B[58_", "\x1B[59_", null, null, null);

            // ── E-row (number row) ───────────────────────────
            Reg("E0", "CAPS", TDVKeyColor.White, TDVKeyFlags.IsToggle,
                20, null, null, null, null, null);

            // E1-E12: normal character keys (no special sequences)
            Reg("E1", "1", TDVKeyColor.White, TDVKeyFlags.None, 49, null, null, null, null, null);
            Reg("E2", "2", TDVKeyColor.White, TDVKeyFlags.None, 50, null, null, null, null, null);
            Reg("E3", "3", TDVKeyColor.White, TDVKeyFlags.None, 51, null, null, null, null, null);
            Reg("E4", "4", TDVKeyColor.White, TDVKeyFlags.None, 52, null, null, null, null, null);
            Reg("E5", "5", TDVKeyColor.White, TDVKeyFlags.None, 53, null, null, null, null, null);
            Reg("E6", "6", TDVKeyColor.White, TDVKeyFlags.None, 54, null, null, null, null, null);
            Reg("E7", "7", TDVKeyColor.White, TDVKeyFlags.None, 55, null, null, null, null, null);
            Reg("E8", "8", TDVKeyColor.White, TDVKeyFlags.None, 56, null, null, null, null, null);
            Reg("E9", "9", TDVKeyColor.White, TDVKeyFlags.None, 57, null, null, null, null, null);
            Reg("E10", "0", TDVKeyColor.White, TDVKeyFlags.None, 48, null, null, null, null, null);
            // E11 is the + / ? key - spec\Keyboards\keys\E11.png photographs it, and this
            // registry's own labels give it as + / ? in every language but us. So VK_OEM_PLUS
            // (187) is what should reach it, NOT the 189 (VK_OEM_MINUS) below, which is B10 MINUS
            // and leaves one of the two unreachable.
            //
            // NOT CHANGED, 2 September 2026, because fixing it here only moves the clash: E12
            // already holds 187, and E12 is the @ / backslash key, which is not VK_OEM_PLUS on a
            // Norwegian layout either. What E12 should be is not settled by anything in spec\, and
            // TDV2200KeyVisualRegistry carries its own copy of these codes that has to move in
            // step. Written up in docs\PLAN.md for Ronny rather than guessed at.
            Reg("E11", "PLUS", TDVKeyColor.White, TDVKeyFlags.None, 189, null, null, null, null, null);
            Reg("E12", "PIPE", TDVKeyColor.White, TDVKeyFlags.None, 187, null, null, null, null, null);

            Reg("E13", "NEWPARA", TDVKeyColor.Orange, TDVKeyFlags.None,
                192, "\x1B[86_", "\x1B[87_", null, "\x08", null);
            Reg("E14", "DEL", TDVKeyColor.Orange, TDVKeyFlags.AlwaysSameCode,
                0, "\x7F", null, null, "\x7F", null);

            // Navigation area
            // SimpleAscii bytes from spec section 6.8.3 (SUB, DC2, DC3), added 27 September 2026.
            Reg("E47", "GUILLEMETS", TDVKeyColor.Orange, TDVKeyFlags.None,
                0, "\x1B[22_", "\x1B[23_", null, "\x1A", null);
            Reg("E48", "JUST", TDVKeyColor.Orange, TDVKeyFlags.None,
                0, "\x1B[24_", "\x1B[25_", null, "\x12", null);
            Reg("E49", "SINGLEGUILLEMETS", TDVKeyColor.Orange, TDVKeyFlags.None,
                0, "\x1B[26_", "\x1B[27_", null, "\x13", null);

            // Function area
            Reg("E51", "F5", TDVKeyColor.Orange, TDVKeyFlags.None,
                116, "\x1B[60_", "\x1B[61_", null, "000", null);
            Reg("E52", "F6", TDVKeyColor.Orange, TDVKeyFlags.None,
                117, "\x1B[62_", "\x1B[63_", null, "00", null);
            Reg("E53", "F7", TDVKeyColor.Orange, TDVKeyFlags.None,
                118, "\x1B[64_", "\x1B[65_", null, "0", null);
            Reg("E54", "F8", TDVKeyColor.Orange, TDVKeyFlags.None,
                119, "\x1B[66_", "\x1B[67_", null, "+", null);

            // ── D-row (QWERTY) ───────────────────────────────
            Reg("D99", "INNS", TDVKeyColor.Orange, TDVKeyFlags.None,
                45, "\x1B[82_", "\x1B[83_", null, "\x07", null);
            Reg("D0", "CTRL", TDVKeyColor.White, TDVKeyFlags.IsModifier,
                162, null, null, null, null, null);

            // D1-D12: normal character keys
            Reg("D1", "Q", TDVKeyColor.White, TDVKeyFlags.None, 81, null, null, null, null, null);
            Reg("D2", "W", TDVKeyColor.White, TDVKeyFlags.None, 87, null, null, null, null, null);
            Reg("D3", "E", TDVKeyColor.White, TDVKeyFlags.None, 69, null, null, null, null, null);
            Reg("D4", "R", TDVKeyColor.White, TDVKeyFlags.None, 82, null, null, null, null, null);
            Reg("D5", "T", TDVKeyColor.White, TDVKeyFlags.None, 84, null, null, null, null, null);
            Reg("D6", "Y", TDVKeyColor.White, TDVKeyFlags.None, 89, null, null, null, null, null);
            Reg("D7", "U", TDVKeyColor.White, TDVKeyFlags.None, 85, null, null, null, null, null);
            Reg("D8", "I", TDVKeyColor.White, TDVKeyFlags.None, 73, null, null, null, null, null);
            Reg("D9", "O", TDVKeyColor.White, TDVKeyFlags.None, 79, null, null, null, null, null);
            Reg("D10", "P", TDVKeyColor.White, TDVKeyFlags.None, 80, null, null, null, null, null);
            Reg("D11", "LBRACKET", TDVKeyColor.White, TDVKeyFlags.None, 219, null, null, null, null, null);
            Reg("D12", "RBRACKET", TDVKeyColor.White, TDVKeyFlags.None, 221, null, null, null, null, null);

            Reg("D13", "LF", TDVKeyColor.Orange, TDVKeyFlags.AlwaysSameCode,
                10, "\x0A", null, null, "\x0A", null);

            // Navigation area
            //
            // ROLLUP (the key marked with an UP arrow) is the PC's Page Up, VK_PRIOR 33, and
            // ROLLDN (DOWN arrow) is Page Down, VK_NEXT 34. keyboard-spec.md section 6.3 pairs
            // RollUp with "ESC [ 5 ~ (Page Up)" and RollDown with "ESC [ 6 ~ (Page Down)". From
            // 8 February to 27 September 2026 these two VK codes were crossed, so a physical Page
            // Up sent the ROLLDN sequence and lit the ROLLDN key on the virtual keyboard, while a
            // click on the virtual key (which goes by grid position, not VK) was right. Found by
            // Ronny in an editor: the two keyboards scrolled opposite ways. The same two numbers
            // live in TDV2200KeyVisualRegistry and must agree with these.
            Reg("D47", "ROLLUP", TDVKeyColor.Brown, TDVKeyFlags.None,
                33, "\x1B[28_", "\x1B[29_", null, "\x06", null);
            Reg("D48", "ANGRE", TDVKeyColor.Orange, TDVKeyFlags.None,
                0, "\x1B[30_", "\x1B[31_", null, "\x15", null);
            Reg("D49", "ROLLDN", TDVKeyColor.Brown, TDVKeyFlags.None,
                34, "\x1B[32_", "\x1B[33_", null, "\x05", null);

            // Numeric pad
            Reg("D51", "KP7", TDVKeyColor.White, TDVKeyFlags.IsNumericPad,
                103, null, null, null, null, "\x1B[75_");
            Reg("D52", "KP8", TDVKeyColor.White, TDVKeyFlags.IsNumericPad,
                104, null, null, null, null, "\x1B[76_");
            Reg("D53", "KP9", TDVKeyColor.White, TDVKeyFlags.IsNumericPad,
                105, null, null, null, null, "\x1B[77_");
            // VK 0: a PC keyboard has no numeric-pad space. Until 27 September 2026 this row held
            // VK 32, the same as the A5 spacebar, and because the D row registers before the A
            // row, GetGridForVK(32) answered D54 - so a physical spacebar lit the numpad space on
            // the virtual keyboard instead of the spacebar. Registration is first-wins, see Reg().
            Reg("D54", "KPSPACE", TDVKeyColor.White, TDVKeyFlags.IsNumericPad,
                0, null, null, null, null, "\x1B[80_");

            // ── C-row (ASDF) ─────────────────────────────────
            Reg("C99", "MODE", TDVKeyColor.Orange, TDVKeyFlags.None,
                0, "\x1B[84_", "\x1B[85_", null, "\x05", null);
            Reg("C0", "LOCK", TDVKeyColor.White, TDVKeyFlags.IsToggle,
                20, null, null, null, null, null);

            // C1-C12: normal character keys
            Reg("C1", "A", TDVKeyColor.White, TDVKeyFlags.None, 65, null, null, null, null, null);
            Reg("C2", "S", TDVKeyColor.White, TDVKeyFlags.None, 83, null, null, null, null, null);
            Reg("C3", "D", TDVKeyColor.White, TDVKeyFlags.None, 68, null, null, null, null, null);
            Reg("C4", "F", TDVKeyColor.White, TDVKeyFlags.None, 70, null, null, null, null, null);
            Reg("C5", "G", TDVKeyColor.White, TDVKeyFlags.None, 71, null, null, null, null, null);
            Reg("C6", "H", TDVKeyColor.White, TDVKeyFlags.None, 72, null, null, null, null, null);
            Reg("C7", "J", TDVKeyColor.White, TDVKeyFlags.None, 74, null, null, null, null, null);
            Reg("C8", "K", TDVKeyColor.White, TDVKeyFlags.None, 75, null, null, null, null, null);
            Reg("C9", "L", TDVKeyColor.White, TDVKeyFlags.None, 76, null, null, null, null, null);
            Reg("C10", "SEMICOLON", TDVKeyColor.White, TDVKeyFlags.None, 186, null, null, null, null, null);
            Reg("C11", "QUOTE", TDVKeyColor.White, TDVKeyFlags.None, 222, null, null, null, null, null);
            Reg("C12", "BACKSLASH", TDVKeyColor.White, TDVKeyFlags.None, 220, null, null, null, null, null);

            Reg("C13", "RETURN", TDVKeyColor.Orange, TDVKeyFlags.AlwaysSameCode,
                13, "\x0D", null, null, "\x0D", null);

            // Navigation area
            Reg("C47", "FIELDLEFT", TDVKeyColor.Brown, TDVKeyFlags.None,
                0, "\x1B[34_", "\x1B[35_", null, "\x0C", null);
            Reg("C48", "UP", TDVKeyColor.Brown, TDVKeyFlags.AlwaysSameCode,
                38, "\x1C", "\x1C", null, "\x1C", null);
            Reg("C49", "FIELDRIGHT", TDVKeyColor.Brown, TDVKeyFlags.None,
                0, "\x1B[36_", "\x1B[37_", null, "\x17", null);

            // Numeric pad
            Reg("C51", "KP4", TDVKeyColor.White, TDVKeyFlags.IsNumericPad,
                100, null, null, null, null, "\x1B[72_");
            Reg("C52", "KP5", TDVKeyColor.White, TDVKeyFlags.IsNumericPad,
                101, null, null, null, null, "\x1B[73_");
            Reg("C53", "KP6", TDVKeyColor.White, TDVKeyFlags.IsNumericPad,
                102, null, null, null, null, "\x1B[74_");
            Reg("C54", "KPMINUS", TDVKeyColor.White, TDVKeyFlags.IsNumericPad,
                109, null, null, null, null, "\x1B[79_");

            // ── B-row (ZXCV) ─────────────────────────────────
            Reg("B99", "LSHIFT", TDVKeyColor.White, TDVKeyFlags.IsModifier,
                160, null, null, null, null, null);

            // B0-B10: normal character keys
            //
            // Each VK is the PC key with the SAME label: Z is VK 90, X is 88, and so on, and B0
            // (the < > key left of Z on an ISO keyboard, spec Appendix B) is VK_OEM_102, 226.
            // Until 27 September 2026 the whole row was shifted one key to the right - B0 held
            // Z's VK, B1 "Z" held X's, through B9 "PERIOD" holding the slash - so pressing X lit
            // the Z key on the virtual keyboard. Typing was unaffected because these rows have no
            // sequences and the text falls through to the OS character. B10 MINUS keeps VK 189,
            // shared with E11 PLUS; Ronny decided on 2 September 2026 to leave that pair alone.
            Reg("B0", "LTGT", TDVKeyColor.White, TDVKeyFlags.None, 226, null, null, null, null, null);
            Reg("B1", "Z", TDVKeyColor.White, TDVKeyFlags.None, 90, null, null, null, null, null);
            Reg("B2", "X", TDVKeyColor.White, TDVKeyFlags.None, 88, null, null, null, null, null);
            Reg("B3", "C", TDVKeyColor.White, TDVKeyFlags.None, 67, null, null, null, null, null);
            Reg("B4", "V", TDVKeyColor.White, TDVKeyFlags.None, 86, null, null, null, null, null);
            Reg("B5", "B", TDVKeyColor.White, TDVKeyFlags.None, 66, null, null, null, null, null);
            Reg("B6", "N", TDVKeyColor.White, TDVKeyFlags.None, 78, null, null, null, null, null);
            Reg("B7", "M", TDVKeyColor.White, TDVKeyFlags.None, 77, null, null, null, null, null);
            Reg("B8", "COMMA", TDVKeyColor.White, TDVKeyFlags.None, 188, null, null, null, null, null);
            Reg("B9", "PERIOD", TDVKeyColor.White, TDVKeyFlags.None, 190, null, null, null, null, null);
            Reg("B10", "MINUS", TDVKeyColor.White, TDVKeyFlags.None, 189, null, null, null, null, null);

            Reg("B11", "RSHIFT", TDVKeyColor.White, TDVKeyFlags.IsModifier,
                161, null, null, null, null, null);

            // Navigation area
            Reg("B47", "LEFT", TDVKeyColor.Brown, TDVKeyFlags.AlwaysSameCode,
                37, "\x08", "\x08", null, "\x08", null);
            // HOME's SimpleAscii used to be "\x10" (DLE). Corrected 31 August 2026: found by
            // reading spec\Keyboards\keyboard-spec.md section 6.8.3 directly, which cites the
            // TDV-2200/9 User's Guide (ND-30.003.04 EN) and explains the DLE reading was an OCR
            // error in an early, uncorrected pass of section 7.2 - a fresh OCR of section 9.1
            // marks B47/B48/B49 as "is always" keys and gives GS 0x1D for HOME in BOTH modes,
            // superseding the DLE reading outright. AlwaysSameCode on this entry already agreed
            // with that: HOME sat beside LEFT and RIGHT, whose SimpleAscii is identical to their
            // ExtNormal, while HOME's alone differed - the one inconsistency in an otherwise
            // uniform group.
            Reg("B48", "HOME", TDVKeyColor.Brown, TDVKeyFlags.AlwaysSameCode,
                36, "\x1D", "\x1D", null, "\x1D", null);
            Reg("B49", "RIGHT", TDVKeyColor.Brown, TDVKeyFlags.AlwaysSameCode,
                39, "\x18", "\x18", null, "\x18", null);

            // Numeric pad
            Reg("B51", "KP1", TDVKeyColor.White, TDVKeyFlags.IsNumericPad,
                97, null, null, null, null, "\x1B[69_");
            Reg("B52", "KP2", TDVKeyColor.White, TDVKeyFlags.IsNumericPad,
                98, null, null, null, null, "\x1B[70_");
            Reg("B53", "KP3", TDVKeyColor.White, TDVKeyFlags.IsNumericPad,
                99, null, null, null, null, "\x1B[71_");
            // Numeric-pad ENTER: CR in both modes (spec 6.8.3 "fixed keys", B54), but in numeric
            // pad FUNCTION mode it sends CSI 81 _ like the other pad keys (spec 6.8.4, line
            // "B54 | ENTER | CSI 81 _"). The NumPadFunc column was empty until 27 September 2026,
            // and a test had pinned CR as a "special case" with no citation. GetSequence checks
            // the pad-function column before the AlwaysSameCode shortcut for this reason.
            Reg("B54", "KPENTER", TDVKeyColor.White, TDVKeyFlags.AlwaysSameCode,
                13, "\x0D", null, null, "\x0D", "\x1B[81_");

            // ── A-row (bottom) ───────────────────────────────
            Reg("A5", "SPACE", TDVKeyColor.White, TDVKeyFlags.None,
                32, null, null, null, null, null);

            // Navigation area
            Reg("A47", "TABLEFT", TDVKeyColor.Brown, TDVKeyFlags.None,
                0, "\x1B[38_", "\x1B[39_", null, "\x15", null);
            Reg("A48", "DOWN", TDVKeyColor.Brown, TDVKeyFlags.AlwaysSameCode,
                40, "\x0B", "\x0B", null, "\x0B", null);
            Reg("A49", "TABRIGHT", TDVKeyColor.Brown, TDVKeyFlags.None,
                0, "\x1B[40_", "\x1B[41_", null, "\x09", null);

            // Numeric pad
            Reg("A51", "KP0", TDVKeyColor.White, TDVKeyFlags.IsNumericPad,
                96, null, null, null, null, "\x1B[68_");
            Reg("A53", "KPDOT", TDVKeyColor.White, TDVKeyFlags.IsNumericPad,
                110, null, null, null, null, "\x1B[78_");

            // ── Name aliases ─────────────────────────────────
            // Norwegian → English aliases for GetGridForName
            Alias("ESCAPE", "G0");
            Alias("MARK", "G9");
            Alias("FIELD", "G10");
            Alias("PARA", "G11");
            Alias("SENT", "G12");
            Alias("WORD", "G13");
            Alias("LOCAL", "G14");
            Alias("DELETE_KEY", "G47");
            Alias("COPY", "G48");
            Alias("MOVE", "G49");
            Alias("FUNC", "G51");
            Alias("PRINT", "G52");
            Alias("HELP", "G53");
            Alias("EXIT", "G54");
            Alias("TAB_FUNC", "F47");
            Alias("F48_KEY", "F48");
            Alias("F49_KEY", "F49");
            Alias("BACKSPACE", "E13");
            Alias("DELETE", "E14");
            Alias("DEL", "E14");
            Alias("E47_KEY", "E47");
            Alias("JUST", "E48");
            Alias("JUSTIFY", "E48");
            Alias("E49_KEY", "E49");
            Alias("INSERT_MODE", "D99");
            Alias("INNS", "D99");
            Alias("LINEFEED", "D13");
            Alias("PAGEUP", "D47");     // RollUp is Page Up (spec section 6.3)
            Alias("PGUP", "D47");
            Alias("CANCEL", "D48");
            Alias("PAGEDOWN", "D49");   // RollDown is Page Down
            Alias("PGDN", "D49");
            Alias("MODE", "C99");
            Alias("ENTER", "C13");
            Alias("ERASE_PAGE", "C47");
            Alias("ARROWUP", "C48");
            Alias("INSERT", "C49");
            Alias("INS", "C49");
            Alias("ARROWLEFT", "B47");
            Alias("ARROWRIGHT", "B49");
            Alias("ARROWDOWN", "A48");
            Alias("TAB_RIGHT", "A49");
            Alias("TAB", "A49");
            Alias("KP_7", "D51"); Alias("NUMPAD7", "D51");
            Alias("KP_8", "D52"); Alias("NUMPAD8", "D52");
            Alias("KP_9", "D53"); Alias("NUMPAD9", "D53");
            Alias("KP_MINUS", "D54"); Alias("NUMPADMINUS", "D54");
            Alias("KP_4", "C51"); Alias("NUMPAD4", "C51");
            Alias("KP_5", "C52"); Alias("NUMPAD5", "C52");
            Alias("KP_6", "C53"); Alias("NUMPAD6", "C53");
            Alias("KP_PLUS", "C54"); Alias("NUMPADPLUS", "C54");
            Alias("KP_1", "B51"); Alias("NUMPAD1", "B51");
            Alias("KP_2", "B52"); Alias("NUMPAD2", "B52");
            Alias("KP_3", "B53"); Alias("NUMPAD3", "B53");
            Alias("KP_ENTER", "B54"); Alias("NUMPADENTER", "B54");
            Alias("KP_0", "A51"); Alias("NUMPAD0", "A51");
            Alias("KP_PERIOD", "A53"); Alias("NUMPADDECIMAL", "A53");

            // Also register the PUSH key aliases
            for (int i = 1; i <= 8; i++)
            {
                Alias($"PUSH{i}", $"G{i}");
            }
        }

        private static void RegLabel(string gridPos, string langCode, string primary, string? shifted, string? alternative)
        {
            _labels[gridPos + "_" + langCode] = new TDVKeyLabel(primary, shifted, alternative);
        }

        private static void RegLabelAllLangs(string gridPos, string primary, string? shifted, string? alternative)
        {
            for (int i = 0; i < LanguageCodes.Length; i++)
            {
                _labels[gridPos + "_" + LanguageCodes[i]] = new TDVKeyLabel(primary, shifted, alternative);
            }
        }

        private static void InitializeLabels()
        {
            // ── G-row labels ─────────────────────────────────
            RegLabelAllLangs("G0", "ESC", null, null);

            for (int i = 1; i <= 8; i++)
                RegLabelAllLangs($"G{i}", $"P{i}", null, null);

            // G9 - MERK/MARK (national variants)
            RegLabel("G9", "no", "MERK", null, null);
            RegLabel("G9", "dk", "MRK", null, null);
            RegLabel("G9", "sv", "MERK", null, null);
            RegLabel("G9", "de", "MERK", null, null);
            RegLabel("G9", "us", "MARK", null, null);
            RegLabel("G9", "fr", "MARQ", null, null);
            RegLabel("G9", "sds", "MERK", null, null);
            RegLabel("G9", "fao", "MERK", null, null);
            RegLabel("G9", "en", "MARK", null, null);
            RegLabel("G9", "ch", "MARK", null, null);
            RegLabel("G9", "fi", "MERK", null, null);
            RegLabel("G9", "is", "MERK", null, null);

            // G10 - FELT/FIELD
            RegLabel("G10", "no", "FELT", null, null);
            RegLabel("G10", "dk", "FELT", null, null);
            RegLabel("G10", "sv", "FÄLT", null, null);
            RegLabel("G10", "de", "FELT", null, null);
            RegLabel("G10", "us", "FIELD", null, null);
            RegLabel("G10", "fr", "CHAMP", null, null);
            RegLabel("G10", "sds", "FELT", null, null);
            RegLabel("G10", "fao", "FELT", null, null);
            RegLabel("G10", "en", "FIELD", null, null);
            RegLabel("G10", "ch", "FIELD", null, null);
            RegLabel("G10", "fi", "KENTTÄ", null, null);
            RegLabel("G10", "is", "FELT", null, null);

            // G11 - AVSN/PARA
            RegLabel("G11", "no", "AVSN", null, null);
            RegLabel("G11", "dk", "AVSN", null, null);
            RegLabel("G11", "sv", "AVSN", null, null);
            RegLabel("G11", "de", "AVSN", null, null);
            RegLabel("G11", "us", "PARA", null, null);
            RegLabel("G11", "fr", "PARA", null, null);
            RegLabel("G11", "sds", "AVSN", null, null);
            RegLabel("G11", "fao", "AVSN", null, null);
            RegLabel("G11", "en", "PARA", null, null);
            RegLabel("G11", "ch", "PARA", null, null);
            RegLabel("G11", "fi", "KAPPALE", null, null);
            RegLabel("G11", "is", "AVSN", null, null);

            // G12 - SETN/SENT
            RegLabel("G12", "no", "SETN", null, null);
            RegLabel("G12", "dk", "SETN", null, null);
            RegLabel("G12", "sv", "SETN", null, null);
            RegLabel("G12", "de", "SETN", null, null);
            RegLabel("G12", "us", "SENT", null, null);
            RegLabel("G12", "fr", "SENT", null, null);
            RegLabel("G12", "sds", "SETN", null, null);
            RegLabel("G12", "fao", "SETN", null, null);
            RegLabel("G12", "en", "SENT", null, null);
            RegLabel("G12", "ch", "SENT", null, null);
            RegLabel("G12", "fi", "LAUSE", null, null);
            RegLabel("G12", "is", "SETN", null, null);

            // G13 - ORD/WORD
            RegLabel("G13", "no", "ORD", null, null);
            RegLabel("G13", "dk", "ORD", null, null);
            RegLabel("G13", "sv", "ORD", null, null);
            RegLabel("G13", "de", "ORD", null, null);
            RegLabel("G13", "us", "WORD", null, null);
            RegLabel("G13", "fr", "MOT", null, null);
            RegLabel("G13", "sds", "ORD", null, null);
            RegLabel("G13", "fao", "ORD", null, null);
            RegLabel("G13", "en", "WORD", null, null);
            RegLabel("G13", "ch", "WORD", null, null);
            RegLabel("G13", "fi", "SANA", null, null);
            RegLabel("G13", "is", "ORD", null, null);

            // G14 - LOKAL/LOCAL
            RegLabel("G14", "no", "LOKAL", null, null);
            RegLabel("G14", "dk", "LOKAL", null, null);
            RegLabel("G14", "sv", "LOKAL", null, null);
            RegLabel("G14", "de", "LOKAL", null, null);
            RegLabel("G14", "us", "LOCAL", null, null);
            RegLabel("G14", "fr", "LOCAL", null, null);
            RegLabel("G14", "sds", "LOKAL", null, null);
            RegLabel("G14", "fao", "LOKAL", null, null);
            RegLabel("G14", "en", "LOCAL", null, null);
            RegLabel("G14", "ch", "LOCAL", null, null);
            RegLabel("G14", "fi", "LOKAL", null, null);
            RegLabel("G14", "is", "LOKAL", null, null);

            // G47 - STRYK/DELETE
            RegLabel("G47", "no", "STRYK", null, null);
            RegLabel("G47", "dk", "STRYK", null, null);
            RegLabel("G47", "sv", "STRYK", null, null);
            RegLabel("G47", "de", "STRYK", null, null);
            RegLabel("G47", "us", "DELETE", null, null);
            RegLabel("G47", "fr", "SUPPR", null, null);
            RegLabel("G47", "sds", "STRYK", null, null);
            RegLabel("G47", "fao", "STRYK", null, null);
            RegLabel("G47", "en", "DELETE", null, null);
            RegLabel("G47", "ch", "DELETE", null, null);
            RegLabel("G47", "fi", "POISTA", null, null);
            RegLabel("G47", "is", "STRYK", null, null);

            // G48 - KOPI/COPY
            RegLabel("G48", "no", "KOPI", null, null);
            RegLabel("G48", "dk", "KOPI", null, null);
            RegLabel("G48", "sv", "KOPI", null, null);
            RegLabel("G48", "de", "KOPI", null, null);
            RegLabel("G48", "us", "COPY", null, null);
            RegLabel("G48", "fr", "COPIE", null, null);
            RegLabel("G48", "sds", "KOPI", null, null);
            RegLabel("G48", "fao", "KOPI", null, null);
            RegLabel("G48", "en", "COPY", null, null);
            RegLabel("G48", "ch", "COPY", null, null);
            RegLabel("G48", "fi", "KOPIOI", null, null);
            RegLabel("G48", "is", "KOPI", null, null);

            // G49 - FLYTT/MOVE
            RegLabel("G49", "no", "FLYTT", null, null);
            RegLabel("G49", "dk", "FLYTT", null, null);
            RegLabel("G49", "sv", "FLYTT", null, null);
            RegLabel("G49", "de", "FLYTT", null, null);
            RegLabel("G49", "us", "MOVE", null, null);
            RegLabel("G49", "fr", "DEPL", null, null);
            RegLabel("G49", "sds", "FLYTT", null, null);
            RegLabel("G49", "fao", "FLYTT", null, null);
            RegLabel("G49", "en", "MOVE", null, null);
            RegLabel("G49", "ch", "MOVE", null, null);
            RegLabel("G49", "fi", "SIIRRÄ", null, null);
            RegLabel("G49", "is", "FLYTT", null, null);

            // G51 - FUNK/FUNC
            RegLabel("G51", "no", "FUNK", null, null);
            RegLabel("G51", "dk", "FUNK", null, null);
            RegLabel("G51", "sv", "FUNK", null, null);
            RegLabel("G51", "de", "FUNK", null, null);
            RegLabel("G51", "us", "FUNC", null, null);
            RegLabel("G51", "fr", "FONC", null, null);
            RegLabel("G51", "sds", "FUNK", null, null);
            RegLabel("G51", "fao", "FUNK", null, null);
            RegLabel("G51", "en", "FUNC", null, null);
            RegLabel("G51", "ch", "FUNC", null, null);
            RegLabel("G51", "fi", "FUNK", null, null);
            RegLabel("G51", "is", "FUNK", null, null);

            // G52 - SKRIV/PRINT
            RegLabel("G52", "no", "SKRIV", null, null);
            RegLabel("G52", "dk", "SKRIV", null, null);
            RegLabel("G52", "sv", "SKRIV", null, null);
            RegLabel("G52", "de", "SKRIV", null, null);
            RegLabel("G52", "us", "PRINT", null, null);
            RegLabel("G52", "fr", "IMPRI", null, null);
            RegLabel("G52", "sds", "SKRIV", null, null);
            RegLabel("G52", "fao", "SKRIV", null, null);
            RegLabel("G52", "en", "PRINT", null, null);
            RegLabel("G52", "ch", "PRINT", null, null);
            RegLabel("G52", "fi", "KIRJ", null, null);
            RegLabel("G52", "is", "SKRIV", null, null);

            // G53 - HJELP/HELP
            RegLabel("G53", "no", "HJELP", null, null);
            RegLabel("G53", "dk", "HJLP", null, null);
            RegLabel("G53", "sv", "HJÄLP", null, null);
            RegLabel("G53", "de", "HJELP", null, null);
            RegLabel("G53", "us", "HELP", null, null);
            RegLabel("G53", "fr", "AIDE", null, null);
            RegLabel("G53", "sds", "HJELP", null, null);
            RegLabel("G53", "fao", "HJELP", null, null);
            RegLabel("G53", "en", "HELP", null, null);
            RegLabel("G53", "ch", "HELP", null, null);
            RegLabel("G53", "fi", "AUTA", null, null);
            RegLabel("G53", "is", "HJÁLP", null, null);

            // G54 - SLUTT/EXIT
            RegLabel("G54", "no", "SLUTT", null, null);
            RegLabel("G54", "dk", "SLUT", null, null);
            RegLabel("G54", "sv", "SLUTT", null, null);
            RegLabel("G54", "de", "SLUTT", null, null);
            RegLabel("G54", "us", "EXIT", null, null);
            RegLabel("G54", "fr", "FIN", null, null);
            RegLabel("G54", "sds", "SLUTT", null, null);
            RegLabel("G54", "fao", "SLUTT", null, null);
            RegLabel("G54", "en", "EXIT", null, null);
            RegLabel("G54", "ch", "EXIT", null, null);
            RegLabel("G54", "fi", "LOPPU", null, null);
            RegLabel("G54", "is", "HÆTTA", null, null);

            // ── F-row labels ─────────────────────────────────
            RegLabelAllLangs("F47", "TAB", "-", "+");
            RegLabelAllLangs("F48", "\u00B7\u00B7\u00B7", null, null);
            RegLabelAllLangs("F49", "/aaa", null, "aaa");
            RegLabelAllLangs("F51", "F1", null, null);
            RegLabelAllLangs("F52", "F2", null, "SI");
            RegLabelAllLangs("F53", "F3", null, "SO");
            RegLabelAllLangs("F54", "F4", null, "CLEAR");

            // ── E-row labels ─────────────────────────────────
            RegLabelAllLangs("E0", "CAPS", null, null);
            RegLabelAllLangs("E1", "1", "!", null);
            // E2 has national variants
            RegLabel("E2", "no", "2", "\"", null);
            RegLabel("E2", "dk", "2", "\"", null);
            RegLabel("E2", "sv", "2", "\"", null);
            RegLabel("E2", "de", "2", "\"", null);
            RegLabel("E2", "us", "2", "@", null);
            RegLabel("E2", "fr", "2", "\"", null);
            RegLabel("E2", "sds", "2", "\"", null);
            RegLabel("E2", "fao", "2", "\"", null);
            RegLabel("E2", "en", "2", "\"", null);
            RegLabel("E2", "ch", "2", "\"", null);
            RegLabel("E2", "fi", "2", "\"", null);
            RegLabel("E2", "is", "2", "\"", null);

            RegLabelAllLangs("E3", "3", "#", null);

            // E4 has national variants
            RegLabel("E4", "no", "4", "$", null);
            RegLabel("E4", "dk", "4", "$", null);
            RegLabel("E4", "sv", "4", "\u00A4", null);
            RegLabel("E4", "de", "4", "$", null);
            RegLabel("E4", "us", "4", "$", null);
            RegLabel("E4", "fr", "4", "$", null);
            RegLabel("E4", "sds", "4", "$", null);
            RegLabel("E4", "fao", "4", "\u00A7", null);
            RegLabel("E4", "en", "4", "$", null);
            RegLabel("E4", "ch", "4", "$", null);
            RegLabel("E4", "fi", "4", "$", null);
            RegLabel("E4", "is", "4", "$", null);

            RegLabelAllLangs("E5", "5", "%", null);

            // E6 has national variants
            RegLabel("E6", "no", "6", "&", null);
            RegLabel("E6", "dk", "6", "&", null);
            RegLabel("E6", "sv", "6", "&", null);
            RegLabel("E6", "de", "6", "&", null);
            RegLabel("E6", "us", "6", "^", null);
            RegLabel("E6", "fr", "6", "&", null);
            RegLabel("E6", "sds", "6", "&", null);
            RegLabel("E6", "fao", "6", "&", null);
            RegLabel("E6", "en", "6", "&", null);
            RegLabel("E6", "ch", "6", "&", null);
            RegLabel("E6", "fi", "6", "&", null);
            RegLabel("E6", "is", "6", "&", null);

            // E7 has national variants
            RegLabel("E7", "no", "7", "/", null);
            RegLabel("E7", "dk", "7", "/", null);
            RegLabel("E7", "sv", "7", "/", null);
            RegLabel("E7", "de", "7", "/", null);
            RegLabel("E7", "us", "7", "&", null);
            RegLabel("E7", "fr", "7", "/", null);
            RegLabel("E7", "sds", "7", "/", null);
            RegLabel("E7", "fao", "7", "/", null);
            RegLabel("E7", "en", "7", "/", null);
            RegLabel("E7", "ch", "7", "/", null);
            RegLabel("E7", "fi", "7", "/", null);
            RegLabel("E7", "is", "7", "/", null);

            // E8 has national variants
            RegLabel("E8", "no", "8", "(", null);
            RegLabel("E8", "dk", "8", "(", null);
            RegLabel("E8", "sv", "8", "(", null);
            RegLabel("E8", "de", "8", "(", null);
            RegLabel("E8", "us", "8", "*", null);
            RegLabel("E8", "fr", "8", "(", null);
            RegLabel("E8", "sds", "8", "(", null);
            RegLabel("E8", "fao", "8", "(", null);
            RegLabel("E8", "en", "8", "(", null);
            RegLabel("E8", "ch", "8", "(", null);
            RegLabel("E8", "fi", "8", "(", null);
            RegLabel("E8", "is", "8", "(", null);

            // E9 has national variants
            RegLabel("E9", "no", "9", ")", null);
            RegLabel("E9", "dk", "9", ")", null);
            RegLabel("E9", "sv", "9", ")", null);
            RegLabel("E9", "de", "9", ")", null);
            RegLabel("E9", "us", "9", "(", null);
            RegLabel("E9", "fr", "9", ")", null);
            RegLabel("E9", "sds", "9", ")", null);
            RegLabel("E9", "fao", "9", ")", null);
            RegLabel("E9", "en", "9", ")", null);
            RegLabel("E9", "ch", "9", ")", null);
            RegLabel("E9", "fi", "9", ")", null);
            RegLabel("E9", "is", "9", ")", null);

            // E10 has national variants
            RegLabel("E10", "no", "0", "=", null);
            RegLabel("E10", "dk", "0", "=", null);
            RegLabel("E10", "sv", "0", "=", null);
            RegLabel("E10", "de", "0", "=", null);
            RegLabel("E10", "us", "0", ")", null);
            RegLabel("E10", "fr", "0", "=", null);
            RegLabel("E10", "sds", "0", "=", null);
            RegLabel("E10", "fao", "0", "=", null);
            RegLabel("E10", "en", "0", "=", null);
            RegLabel("E10", "ch", "0", "=", null);
            RegLabel("E10", "fi", "0", "=", null);
            RegLabel("E10", "is", "0", "=", null);

            // E11 has national variants
            RegLabel("E11", "no", "+", "?", null);
            RegLabel("E11", "dk", "+", "?", null);
            RegLabel("E11", "sv", "+", "?", null);
            RegLabel("E11", "de", "+", "?", null);
            RegLabel("E11", "us", "-", "_", null);
            RegLabel("E11", "fr", "+", "?", null);
            RegLabel("E11", "sds", "+", "?", null);
            RegLabel("E11", "fao", "+", "?", null);
            RegLabel("E11", "en", "+", "?", null);
            RegLabel("E11", "ch", "+", "?", null);
            RegLabel("E11", "fi", "+", "?", null);
            RegLabel("E11", "is", "+", "?", null);

            // E12 has national variants
            RegLabel("E12", "no", "@", "`", null);
            RegLabel("E12", "dk", "@", "`", null);
            RegLabel("E12", "sv", "@", "`", null);
            RegLabel("E12", "de", "@", "`", null);
            RegLabel("E12", "us", "=", "+", null);
            RegLabel("E12", "fr", "@", "`", null);
            RegLabel("E12", "sds", "@", "`", null);
            RegLabel("E12", "fao", "@", "`", null);
            RegLabel("E12", "en", "@", "`", null);
            RegLabel("E12", "ch", "@", "`", null);
            RegLabel("E12", "fi", "@", "`", null);
            RegLabel("E12", "is", "@", "`", null);

            // E13 has national variants
            RegLabel("E13", "no", "\u00B4", "\\", null);
            RegLabel("E13", "dk", "\u00B4", "\\", null);
            RegLabel("E13", "sv", "\u00B4", "\\", null);
            RegLabel("E13", "de", "\u00B4", "\\", null);
            RegLabel("E13", "us", "`", "~", null);
            RegLabel("E13", "fr", "\u00B4", "\\", null);
            RegLabel("E13", "sds", "\u00B4", "\\", null);
            RegLabel("E13", "fao", "\u00B4", "\\", null);
            RegLabel("E13", "en", "\u00B4", "\\", null);
            RegLabel("E13", "ch", "\u00B4", "\\", null);
            RegLabel("E13", "fi", "\u00B4", "\\", null);
            RegLabel("E13", "is", "\u00B4", "\\", null);

            RegLabelAllLangs("E14", "DEL", null, null);

            // Navigation area
            RegLabelAllLangs("E47", "\u00AB", "\u00BB", null);
            RegLabelAllLangs("E48", "JUST", null, null);
            RegLabelAllLangs("E49", "\u2039\u203A", "\u203A\u2039", null);

            // Function area
            RegLabelAllLangs("E51", "F5", null, "000");
            RegLabelAllLangs("E52", "F6", null, "00");
            RegLabelAllLangs("E53", "F7", null, "0");
            RegLabelAllLangs("E54", "F8", null, "+");

            // ── D-row labels ─────────────────────────────────
            RegLabelAllLangs("D99", "INNS", "EXPS", null);
            RegLabelAllLangs("D0", "CTRL", null, null);

            // D1-D10 are standard QWERTY (same for all layouts except some national variants)
            RegLabelAllLangs("D1", "Q", "q", null);
            RegLabelAllLangs("D2", "W", "w", null);
            RegLabelAllLangs("D3", "E", "e", null);
            RegLabelAllLangs("D4", "R", "r", null);
            RegLabelAllLangs("D5", "T", "t", null);
            RegLabelAllLangs("D6", "Y", "y", null);
            RegLabelAllLangs("D7", "U", "u", null);
            RegLabelAllLangs("D8", "I", "i", null);
            RegLabelAllLangs("D9", "O", "o", null);
            RegLabelAllLangs("D10", "P", "p", null);

            // D11 has national variants
            RegLabel("D11", "no", "\u00C5", "\u00E5", null);
            RegLabel("D11", "dk", "\u00C5", "\u00E5", null);
            RegLabel("D11", "sv", "\u00C5", "\u00E5", null);
            RegLabel("D11", "de", "\u00DC", "\u00FC", null);
            RegLabel("D11", "us", "[", "{", null);
            RegLabel("D11", "fr", "^", "\u00A8", null);
            RegLabel("D11", "sds", "\u00C5", "\u00E5", null);
            RegLabel("D11", "fao", "[", "{", null);
            RegLabel("D11", "en", "[", "{", null);
            RegLabel("D11", "ch", "\u00DC", "\u00FC", null);
            RegLabel("D11", "fi", "\u00C5", "\u00E5", null);
            RegLabel("D11", "is", "\u00D0", "\u00F0", null);

            // D12 has national variants
            RegLabel("D12", "no", "^", "~", null);
            RegLabel("D12", "dk", "\u00A8", "^", null);
            RegLabel("D12", "sv", "\u00A8", "^", null);
            RegLabel("D12", "de", "+", "*", null);
            RegLabel("D12", "us", "]", "}", null);
            RegLabel("D12", "fr", "$", "\u00A3", null);
            RegLabel("D12", "sds", "\u00A8", "^", null);
            RegLabel("D12", "fao", "]", "}", null);
            RegLabel("D12", "en", "]", "}", null);
            RegLabel("D12", "ch", "+", "*", null);
            RegLabel("D12", "fi", "\u00A8", "^", null);
            RegLabel("D12", "is", "\u00DE", "\u00FE", null);

            RegLabelAllLangs("D13", "LF", null, null);

            // Navigation area
            RegLabelAllLangs("D47", "\u21D1", "\u21D0", null);
            RegLabelAllLangs("D48", "ANGRE", null, null);
            RegLabelAllLangs("D49", "\u21D3", "\u21D2", null);

            // Numeric pad
            RegLabelAllLangs("D51", "7", null, null);
            RegLabelAllLangs("D52", "8", null, null);
            RegLabelAllLangs("D53", "9", null, null);
            RegLabelAllLangs("D54", "\u2334", null, null);

            // ── C-row labels ─────────────────────────────────
            RegLabelAllLangs("C99", "MODE", null, null);
            RegLabelAllLangs("C0", "LOCK", null, null);

            // C1-C9 are standard ASDF (same for all layouts)
            RegLabelAllLangs("C1", "A", "a", null);
            RegLabelAllLangs("C2", "S", "s", null);
            RegLabelAllLangs("C3", "D", "d", null);
            RegLabelAllLangs("C4", "F", "f", null);
            RegLabelAllLangs("C5", "G", "g", null);
            RegLabelAllLangs("C6", "H", "h", null);
            RegLabelAllLangs("C7", "J", "j", null);
            RegLabelAllLangs("C8", "K", "k", null);
            RegLabelAllLangs("C9", "L", "l", null);

            // C10 has national variants
            RegLabel("C10", "no", "\u00D8", "\u00F8", null);
            RegLabel("C10", "dk", "\u00D8", "\u00F8", null);
            RegLabel("C10", "sv", "\u00D6", "\u00F6", null);
            RegLabel("C10", "de", "\u00D6", "\u00F6", null);
            RegLabel("C10", "us", ";", ":", null);
            RegLabel("C10", "fr", "\u00D6", "\u00F6", null);
            RegLabel("C10", "sds", "\u00D8", "\u00F8", null);
            RegLabel("C10", "fao", ";", ":", null);
            RegLabel("C10", "en", ";", ":", null);
            RegLabel("C10", "ch", "\u00D6", "\u00F6", null);
            RegLabel("C10", "fi", "\u00D6", "\u00F6", null);
            RegLabel("C10", "is", ";", ":", null);

            // C11 has national variants
            RegLabel("C11", "no", "\u00C6", "\u00E6", null);
            RegLabel("C11", "dk", "\u00C6", "\u00E6", null);
            RegLabel("C11", "sv", "\u00C4", "\u00E4", null);
            RegLabel("C11", "de", "\u00C4", "\u00E4", null);
            RegLabel("C11", "us", "'", "\"", null);
            RegLabel("C11", "fr", "\u00C4", "\u00E4", null);
            RegLabel("C11", "sds", "\u00C6", "\u00E6", null);
            RegLabel("C11", "fao", "'", "\"", null);
            RegLabel("C11", "en", "'", "\"", null);
            RegLabel("C11", "ch", "\u00C4", "\u00E4", null);
            RegLabel("C11", "fi", "\u00C4", "\u00E4", null);
            RegLabel("C11", "is", "'", "\"", null);

            // C12 has national variants
            RegLabel("C12", "no", "'", "*", null);
            RegLabel("C12", "dk", "'", "*", null);
            RegLabel("C12", "sv", "'", "*", null);
            RegLabel("C12", "de", "#", "'", null);
            RegLabel("C12", "us", "\\", "|", null);
            RegLabel("C12", "fr", "'", "*", null);
            RegLabel("C12", "sds", "'", "*", null);
            RegLabel("C12", "fao", "#", "~", null);
            RegLabel("C12", "en", "#", "~", null);
            RegLabel("C12", "ch", "#", "'", null);
            RegLabel("C12", "fi", "'", "*", null);
            RegLabel("C12", "is", "'", "*", null);

            RegLabelAllLangs("C13", "RETURN", null, null);

            // Navigation area
            RegLabelAllLangs("C47", "\u21D0", null, null);
            RegLabelAllLangs("C48", "\u2191", null, null);
            RegLabelAllLangs("C49", "\u21D2", null, null);

            // Numeric pad
            RegLabelAllLangs("C51", "4", null, null);
            RegLabelAllLangs("C52", "5", null, null);
            RegLabelAllLangs("C53", "6", null, null);
            RegLabelAllLangs("C54", "-", null, null);

            // ── B-row labels ─────────────────────────────────
            RegLabelAllLangs("B99", "SHIFT", null, null);

            // B0 has national variants
            RegLabel("B0", "no", ">", "<", null);
            RegLabel("B0", "dk", ">", "<", null);
            RegLabel("B0", "sv", ">", "<", null);
            RegLabel("B0", "de", "<", ">", null);
            RegLabel("B0", "us", "Z", "z", null);
            RegLabel("B0", "fr", "<", ">", null);
            RegLabel("B0", "sds", ">", "<", null);
            RegLabel("B0", "fao", "Z", "z", null);
            RegLabel("B0", "en", "Z", "z", null);
            RegLabel("B0", "ch", "<", ">", null);
            RegLabel("B0", "fi", ">", "<", null);
            RegLabel("B0", "is", ">", "<", null);

            // B1 has national variants
            RegLabel("B1", "no", "Z", "z", null);
            RegLabel("B1", "dk", "Z", "z", null);
            RegLabel("B1", "sv", "Z", "z", null);
            RegLabel("B1", "de", "Y", "y", null);
            RegLabel("B1", "us", "X", "x", null);
            RegLabel("B1", "fr", "W", "w", null);
            RegLabel("B1", "sds", "Z", "z", null);
            RegLabel("B1", "fao", "X", "x", null);
            RegLabel("B1", "en", "X", "x", null);
            RegLabel("B1", "ch", "Y", "y", null);
            RegLabel("B1", "fi", "Z", "z", null);
            RegLabel("B1", "is", "Z", "z", null);

            // B2-B9 have national variants (copied from TDV2200KeyLayout)
            RegLabel("B2", "no", "X", "x", null);
            RegLabel("B2", "dk", "X", "x", null);
            RegLabel("B2", "sv", "X", "x", null);
            RegLabel("B2", "de", "X", "x", null);
            RegLabel("B2", "us", "C", "c", null);
            RegLabel("B2", "fr", "X", "x", null);
            RegLabel("B2", "sds", "X", "x", null);
            RegLabel("B2", "fao", "C", "c", null);
            RegLabel("B2", "en", "C", "c", null);
            RegLabel("B2", "ch", "X", "x", null);
            RegLabel("B2", "fi", "X", "x", null);
            RegLabel("B2", "is", "X", "x", null);

            RegLabel("B3", "no", "C", "c", null);
            RegLabel("B3", "dk", "C", "c", null);
            RegLabel("B3", "sv", "C", "c", null);
            RegLabel("B3", "de", "C", "c", null);
            RegLabel("B3", "us", "V", "v", null);
            RegLabel("B3", "fr", "C", "c", null);
            RegLabel("B3", "sds", "C", "c", null);
            RegLabel("B3", "fao", "V", "v", null);
            RegLabel("B3", "en", "V", "v", null);
            RegLabel("B3", "ch", "C", "c", null);
            RegLabel("B3", "fi", "C", "c", null);
            RegLabel("B3", "is", "C", "c", null);

            RegLabel("B4", "no", "V", "v", null);
            RegLabel("B4", "dk", "V", "v", null);
            RegLabel("B4", "sv", "V", "v", null);
            RegLabel("B4", "de", "V", "v", null);
            RegLabel("B4", "us", "B", "b", null);
            RegLabel("B4", "fr", "V", "v", null);
            RegLabel("B4", "sds", "V", "v", null);
            RegLabel("B4", "fao", "B", "b", null);
            RegLabel("B4", "en", "B", "b", null);
            RegLabel("B4", "ch", "V", "v", null);
            RegLabel("B4", "fi", "V", "v", null);
            RegLabel("B4", "is", "V", "v", null);

            RegLabel("B5", "no", "B", "b", null);
            RegLabel("B5", "dk", "B", "b", null);
            RegLabel("B5", "sv", "B", "b", null);
            RegLabel("B5", "de", "B", "b", null);
            RegLabel("B5", "us", "N", "n", null);
            RegLabel("B5", "fr", "B", "b", null);
            RegLabel("B5", "sds", "B", "b", null);
            RegLabel("B5", "fao", "N", "n", null);
            RegLabel("B5", "en", "N", "n", null);
            RegLabel("B5", "ch", "B", "b", null);
            RegLabel("B5", "fi", "B", "b", null);
            RegLabel("B5", "is", "B", "b", null);

            RegLabel("B6", "no", "N", "n", null);
            RegLabel("B6", "dk", "N", "n", null);
            RegLabel("B6", "sv", "N", "n", null);
            RegLabel("B6", "de", "N", "n", null);
            RegLabel("B6", "us", "M", "m", null);
            RegLabel("B6", "fr", "N", "n", null);
            RegLabel("B6", "sds", "N", "n", null);
            RegLabel("B6", "fao", "M", "m", null);
            RegLabel("B6", "en", "M", "m", null);
            RegLabel("B6", "ch", "N", "n", null);
            RegLabel("B6", "fi", "N", "n", null);
            RegLabel("B6", "is", "N", "n", null);

            RegLabel("B7", "no", "M", "m", null);
            RegLabel("B7", "dk", "M", "m", null);
            RegLabel("B7", "sv", "M", "m", null);
            RegLabel("B7", "de", "M", "m", null);
            RegLabel("B7", "us", ",", "<", null);
            RegLabel("B7", "fr", ",", ";", null);
            RegLabel("B7", "sds", "M", "m", null);
            RegLabel("B7", "fao", ",", "<", null);
            RegLabel("B7", "en", ",", "<", null);
            RegLabel("B7", "ch", "M", "m", null);
            RegLabel("B7", "fi", "M", "m", null);
            RegLabel("B7", "is", "M", "m", null);

            RegLabel("B8", "no", ",", ";", null);
            RegLabel("B8", "dk", ",", ";", null);
            RegLabel("B8", "sv", ",", ";", null);
            RegLabel("B8", "de", ",", ";", null);
            RegLabel("B8", "us", ".", ">", null);
            RegLabel("B8", "fr", ".", ":", null);
            RegLabel("B8", "sds", ",", ";", null);
            RegLabel("B8", "fao", ".", ">", null);
            RegLabel("B8", "en", ".", ">", null);
            RegLabel("B8", "ch", ",", ";", null);
            RegLabel("B8", "fi", ",", ";", null);
            RegLabel("B8", "is", ",", ";", null);

            RegLabel("B9", "no", ".", ":", null);
            RegLabel("B9", "dk", ".", ":", null);
            RegLabel("B9", "sv", ".", ":", null);
            RegLabel("B9", "de", ".", ":", null);
            RegLabel("B9", "us", "/", "?", null);
            RegLabel("B9", "fr", "/", "!", null);
            RegLabel("B9", "sds", ".", ":", null);
            RegLabel("B9", "fao", "/", "?", null);
            RegLabel("B9", "en", "/", "?", null);
            RegLabel("B9", "ch", ".", ":", null);
            RegLabel("B9", "fi", ".", ":", null);
            RegLabel("B9", "is", ".", ":", null);

            RegLabelAllLangs("B10", "-", "_", null);
            RegLabelAllLangs("B11", "", null, null);

            // Navigation area
            RegLabelAllLangs("B47", "\u2190", null, null);
            RegLabelAllLangs("B48", "HOME", null, null);
            RegLabelAllLangs("B49", "\u2192", null, null);

            // Numeric pad
            RegLabelAllLangs("B51", "1", null, null);
            RegLabelAllLangs("B52", "2", null, null);
            RegLabelAllLangs("B53", "3", null, null);
            RegLabelAllLangs("B54", "ENTER", null, null);

            // ── A-row labels ─────────────────────────────────
            RegLabelAllLangs("A5", " ", null, null);
            RegLabelAllLangs("A47", "\u2190|", null, null);
            RegLabelAllLangs("A48", "\u2193", null, null);
            RegLabelAllLangs("A49", "\u2192|", null, null);
            RegLabelAllLangs("A51", "0", null, null);
            RegLabelAllLangs("A53", ".", null, null);
        }

        private static void InitializeAltMappings()
        {
            // ── Default VK → grid position mappings ──

            // Application control keys
            _defaultAltMap[72] = "G53";   // Alt+H → HJELP
            _defaultAltMap[68] = "F49";   // Alt+D → REPLACE (DO)
            _defaultAltMap[85] = "G51";   // Alt+U → FUNK
            _defaultAltMap[80] = "G52";   // Alt+P → SKRIV
            _defaultAltMap[83] = "G54";   // Alt+S → SLUTT
            _defaultAltMap[8] = "D48";    // Alt+Backspace → ANGRE
            _defaultAltMap[77] = "C99";   // Alt+M → MODE (COMMAND)
            _defaultAltMap[70] = "F48";   // Alt+F → SEARCH (FIND)
            _defaultAltMap[88] = "E47";   // Alt+X → GUILLEMETS

            // Editing keys
            _defaultAltMap[65] = "G9";    // Alt+A → MERK
            _defaultAltMap[76] = "G10";   // Alt+L → FELT
            _defaultAltMap[82] = "G11";   // Alt+R → AVSN
            _defaultAltMap[69] = "G12";   // Alt+E → SETN
            _defaultAltMap[87] = "G13";   // Alt+W → ORD
            _defaultAltMap[75] = "G48";   // Alt+K → KOPI
            _defaultAltMap[86] = "G49";   // Alt+V → FLYTT
            _defaultAltMap[74] = "E48";   // Alt+J → JUST
            _defaultAltMap[73] = "E49";   // Alt+I → SINGLEGUILLEMETS (INSERT_HERE)

            // PUSH keys (Alt+1-8)
            for (int i = 1; i <= 8; i++)
            {
                _defaultAltMap[48 + i] = $"G{i}"; // VK 49-56 → G1-G8
            }

            // PUSH keys (Alt+F1-F8) — same targets as Alt+1-8
            for (int i = 0; i < 8; i++)
            {
                _defaultAltMap[112 + i] = $"G{i + 1}"; // VK_F1=112..VK_F8=119 → G1-G8
            }

            // PUSH keys shifted (Alt+Shift+F1-F8) — shifted=true targets
            for (int i = 0; i < 8; i++)
            {
                _defaultAltShiftMap[112 + i] = $"G{i + 1}"; // VK_F1=112..VK_F8=119 → G1-G8 (shifted)
            }

            // Navigation
            _defaultAltMap[46] = "G47";   // Alt+Delete → STRYK
            _defaultAltMap[33] = "D47";   // Alt+PageUp → ROLLUP (spec section 6.3: RollUp is Page Up)
            _defaultAltMap[34] = "D49";   // Alt+PageDown → ROLLDN

        }
    }
}
