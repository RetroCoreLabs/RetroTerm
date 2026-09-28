using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using RetroTerm.Core.Terminal.Emulators.TDV;

namespace RetroTerm.Core.Terminal.Input
{
    /// <summary>
    /// Configuration for generalized key bindings to TDV special keys.
    /// Maps any key combination (Alt+H, F1, Shift+F1, Ctrl+A, Tab, etc.)
    /// to TDV keyboard grid positions with optional shifted target state.
    /// Replaces AltKeyConfiguration with a superset of functionality.
    /// </summary>
    public class TDVKeyBindingConfiguration
    {
        private static TDVKeyBindingConfiguration? _instance;
        private static readonly object _lock = new object();

        /// <summary>
        /// Primary lookup: source key combination → target TDV key
        /// </summary>
        private readonly Dictionary<KeyBindingSource, KeyBindingTarget> _bindings =
            new Dictionary<KeyBindingSource, KeyBindingTarget>();

        /// <summary>
        /// Reverse lookup: grid position → list of source key combinations bound to it
        /// </summary>
        private readonly Dictionary<string, List<KeyBindingSource>> _gridToSources =
            new Dictionary<string, List<KeyBindingSource>>();

        /// <summary>
        /// Singleton instance
        /// </summary>
        public static TDVKeyBindingConfiguration Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_lock)
                    {
                        if (_instance == null)
                        {
                            _instance = new TDVKeyBindingConfiguration();
                            _instance.LoadOrCreateDefaults();
                        }
                    }
                }
                return _instance;
            }
        }

        /// <summary>
        /// Event raised when configuration changes
        /// </summary>
        public event EventHandler? ConfigurationChanged;

        private TDVKeyBindingConfiguration()
        {
        }

        /// <summary>
        /// Number of active bindings
        /// </summary>
        public int Count => _bindings.Count;

        /// <summary>
        /// Every binding as (source, target) pairs — for enumeration by the keyboard
        /// config UI and the MCP/script KEYBIND command. A fresh snapshot, safe to
        /// iterate while the live config changes.
        /// </summary>
        public IReadOnlyList<KeyValuePair<KeyBindingSource, KeyBindingTarget>> GetAllBindings()
        {
            var result = new List<KeyValuePair<KeyBindingSource, KeyBindingTarget>>(_bindings.Count);
            var e = _bindings.GetEnumerator();
            while (e.MoveNext())
            {
                result.Add(e.Current);
            }
            return result;
        }

        // ─── Lookup API ────────────────────────────────────────────

        /// <summary>
        /// Fast lookup: get the target TDV key for a source key combination.
        /// </summary>
        public bool TryGetTarget(KeyBindingSource source, out KeyBindingTarget target)
        {
            return _bindings.TryGetValue(source, out target);
        }

        /// <summary>
        /// Convenience overload: look up by VK code and modifiers directly.
        /// </summary>
        public bool TryGetTarget(int vkCode, KeyModifiers modifiers, out KeyBindingTarget target)
        {
            var source = new KeyBindingSource(vkCode, modifiers);
            return _bindings.TryGetValue(source, out target);
        }

        /// <summary>
        /// Get all source key combinations bound to a grid position.
        /// </summary>
        public IReadOnlyList<KeyBindingSource> GetSourcesForGrid(string gridPos)
        {
            if (_gridToSources.TryGetValue(gridPos, out var sources))
                return sources;
            return Array.Empty<KeyBindingSource>();
        }

        // ─── CRUD API ──────────────────────────────────────────────

        /// <summary>
        /// Add or replace a binding. Validates the source first.
        /// Returns true on success, false if the source is invalid.
        /// If the source is already bound to a different target, it is re-bound (overwritten).
        /// </summary>
        public bool SetBinding(KeyBindingSource source, KeyBindingTarget target)
        {
            if (!source.IsValid())
                return false;

            if (string.IsNullOrEmpty(target.GridPosition))
                return false;

            // Remove any existing binding for this source (if re-mapping)
            RemoveBindingInternal(source, raiseEvent: false);

            // Add the new binding
            _bindings[source] = target;

            // Update reverse lookup
            if (!_gridToSources.TryGetValue(target.GridPosition, out var sources))
            {
                sources = new List<KeyBindingSource>();
                _gridToSources[target.GridPosition] = sources;
            }
            sources.Add(source);

            ConfigurationChanged?.Invoke(this, EventArgs.Empty);
            return true;
        }

        /// <summary>
        /// Remove a binding by its source key combination.
        /// Returns true if a binding was found and removed.
        /// </summary>
        public bool RemoveBinding(KeyBindingSource source)
        {
            return RemoveBindingInternal(source, raiseEvent: true);
        }

        /// <summary>
        /// Remove all bindings that target a grid position.
        /// </summary>
        public void RemoveBindingsForGrid(string gridPos)
        {
            if (!_gridToSources.TryGetValue(gridPos, out var sources))
                return;

            // Copy the list to avoid mutation during iteration
            var sourceCopy = new KeyBindingSource[sources.Count];
            sources.CopyTo(sourceCopy, 0);

            for (int i = 0; i < sourceCopy.Length; i++)
            {
                _bindings.Remove(sourceCopy[i]);
            }

            _gridToSources.Remove(gridPos);
            ConfigurationChanged?.Invoke(this, EventArgs.Empty);
        }

        private bool RemoveBindingInternal(KeyBindingSource source, bool raiseEvent)
        {
            if (!_bindings.TryGetValue(source, out var existingTarget))
                return false;

            _bindings.Remove(source);

            // Update reverse lookup
            if (_gridToSources.TryGetValue(existingTarget.GridPosition, out var sources))
            {
                for (int i = sources.Count - 1; i >= 0; i--)
                {
                    if (sources[i] == source)
                    {
                        sources.RemoveAt(i);
                        break;
                    }
                }
                if (sources.Count == 0)
                {
                    _gridToSources.Remove(existingTarget.GridPosition);
                }
            }

            if (raiseEvent)
            {
                ConfigurationChanged?.Invoke(this, EventArgs.Empty);
            }
            return true;
        }

        // ─── Label API ─────────────────────────────────────────────

        /// <summary>
        /// Get human-readable label for a binding source (e.g., "Alt+H", "F1", "Shift+F1", "Ctrl+A").
        /// </summary>
        public static string GetBindingLabel(KeyBindingSource source)
        {
            return GetBindingLabel(source.VKCode, source.Modifiers);
        }

        /// <summary>
        /// Get human-readable label for a VK code + modifiers combination.
        /// </summary>
        public static string GetBindingLabel(int vkCode, KeyModifiers modifiers)
        {
            var prefix = "";
            if (modifiers.HasFlag(KeyModifiers.Ctrl)) prefix += "Ctrl+";
            if (modifiers.HasFlag(KeyModifiers.Alt)) prefix += "Alt+";
            if (modifiers.HasFlag(KeyModifiers.Shift)) prefix += "Shift+";
            if (modifiers.HasFlag(KeyModifiers.Meta)) prefix += "Win+";

            var keyName = GetVKCodeName(vkCode);
            return prefix + keyName;
        }

        /// <summary>
        /// Get comma-separated labels for all bindings targeting a grid position.
        /// Returns null if no bindings exist.
        /// </summary>
        public string? GetBindingLabelForGrid(string gridPos)
        {
            if (!_gridToSources.TryGetValue(gridPos, out var sources) || sources.Count == 0)
                return null;

            if (sources.Count == 1)
                return GetBindingLabel(sources[0]);

            // Multiple bindings — join with comma
            var result = GetBindingLabel(sources[0]);
            for (int i = 1; i < sources.Count; i++)
            {
                result += ", " + GetBindingLabel(sources[i]);
            }
            return result;
        }

        /// <summary>
        /// Returns the display name for a Windows virtual-key code, without modifiers.
        /// </summary>
        /// <remarks>
        /// Public since 27 September 2026 so the Keyboard Reference window names keys the same
        /// way the binding popup does, rather than keeping a second list of names.
        /// </remarks>
        /// <param name="vkCode">
        /// The Windows virtual-key code, for example 33 for Page Up.
        /// </param>
        /// <returns>
        /// A short name such as "PgUp", "F1" or "Num5", or the code in hex when it has no name.
        /// </returns>
        public static string GetVKCodeName(int vkCode)
        {
            // Letters A-Z (VK 65-90)
            if (vkCode >= 65 && vkCode <= 90)
                return ((char)vkCode).ToString();

            // Numbers 0-9 (VK 48-57)
            if (vkCode >= 48 && vkCode <= 57)
                return (vkCode - 48).ToString();

            // Function keys F1-F24 (VK 112-135)
            if (vkCode >= 112 && vkCode <= 135)
                return "F" + (vkCode - 111);

            // NumPad 0-9 (VK 96-105)
            if (vkCode >= 96 && vkCode <= 105)
                return "Num" + (vkCode - 96);

            return vkCode switch
            {
                8 => "Backspace",
                9 => "Tab",
                13 => "Enter",
                27 => "Esc",
                32 => "Space",
                33 => "PgUp",
                34 => "PgDn",
                35 => "End",
                36 => "Home",
                37 => "Left",
                38 => "Up",
                39 => "Right",
                40 => "Down",
                45 => "Ins",
                46 => "Del",
                // Numpad operators
                106 => "Num*",
                107 => "Num+",
                108 => "NumSep",
                109 => "Num-",
                110 => "Num.",
                111 => "Num/",
                // OEM/punctuation keys
                186 => ";",
                187 => "=",
                188 => ",",
                189 => "-",
                190 => ".",
                191 => "/",
                192 => "`",
                219 => "[",
                220 => "\\",
                221 => "]",
                222 => "'",
                226 => "OEM102",
                _ => $"0x{vkCode:X2}"
            };
        }

        // ─── Validation ────────────────────────────────────────────

        /// <summary>
        /// Check whether a source key combination is a valid binding source.
        /// Delegates to KeyBindingSource.IsValidSource.
        /// </summary>
        public static bool IsValidSource(KeyBindingSource source)
        {
            return source.IsValid();
        }

        // ─── Defaults ──────────────────────────────────────────────

        /// <summary>
        /// Reset all bindings to defaults (Alt+key mappings from TDV2200KeyRegistry).
        /// </summary>
        public void SetDefaults()
        {
            _bindings.Clear();
            _gridToSources.Clear();

            // Alt+key defaults (unshifted)
            var defaults = TDV2200KeyRegistry.DefaultAltMappings;
            var enumerator = defaults.GetEnumerator();
            while (enumerator.MoveNext())
            {
                var vkCode = enumerator.Current.Key;
                var gridPos = enumerator.Current.Value;
                var source = new KeyBindingSource(vkCode, KeyModifiers.Alt);
                var target = new KeyBindingTarget(gridPos, false);
                _bindings[source] = target;
                AddToReverse(gridPos, source);
            }

            // Alt+Shift defaults (shifted)
            var shiftDefaults = TDV2200KeyRegistry.DefaultAltShiftMappings;
            var shiftEnum = shiftDefaults.GetEnumerator();
            while (shiftEnum.MoveNext())
            {
                var vkCode = shiftEnum.Current.Key;
                var gridPos = shiftEnum.Current.Value;
                var source = new KeyBindingSource(vkCode, KeyModifiers.Alt | KeyModifiers.Shift);
                var target = new KeyBindingTarget(gridPos, true);
                _bindings[source] = target;
                AddToReverse(gridPos, source);
            }
        }

        private void AddToReverse(string gridPos, KeyBindingSource source)
        {
            if (!_gridToSources.TryGetValue(gridPos, out var sources))
            {
                sources = new List<KeyBindingSource>();
                _gridToSources[gridPos] = sources;
            }
            sources.Add(source);
        }

        // ─── Persistence ───────────────────────────────────────────

        /// <summary>
        /// JSON binding entry for serialization.
        /// </summary>
        private class BindingEntry
        {
            public int vk { get; set; }
            public int mod { get; set; }
            public string grid { get; set; } = "";
            public bool shifted { get; set; }
        }

        private class BindingFile
        {
            public int version { get; set; }
            public List<BindingEntry> bindings { get; set; } = new List<BindingEntry>();
        }

        /// <summary>
        /// Save configuration to JSON file.
        /// </summary>
        public void Save()
        {
            try
            {
                var configPath = GetConfigFilePath();
                var directory = Path.GetDirectoryName(configPath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var file = new BindingFile { version = 2 };
                var enumerator = _bindings.GetEnumerator();
                while (enumerator.MoveNext())
                {
                    var entry = new BindingEntry
                    {
                        vk = enumerator.Current.Key.VKCode,
                        mod = (int)enumerator.Current.Key.Modifiers,
                        grid = enumerator.Current.Value.GridPosition,
                        shifted = enumerator.Current.Value.Shifted
                    };
                    file.bindings.Add(entry);
                }

                var json = JsonSerializer.Serialize(file, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(configPath, json);
            }
            catch
            {
                // Silently fail - not critical
            }
        }

        /// <summary>
        /// Load configuration from file, migrating from old format if needed.
        /// </summary>
        public void Load()
        {
            LoadOrCreateDefaults();
        }

        private void LoadOrCreateDefaults()
        {
            var configPath = GetConfigFilePath();
            if (File.Exists(configPath))
            {
                try
                {
                    var json = File.ReadAllText(configPath);
                    if (TryLoadV2(json))
                        return;
                }
                catch
                {
                    // Fall through to migration or defaults
                }
            }

            // Try migrating from old alt-key-config.json
            var oldConfigPath = GetOldConfigFilePath();
            if (File.Exists(oldConfigPath))
            {
                try
                {
                    var json = File.ReadAllText(oldConfigPath);
                    if (TryMigrateFromOldFormat(json))
                        return;
                }
                catch
                {
                    // Fall through to defaults
                }
            }

            SetDefaults();
        }

        private bool TryLoadV2(string json)
        {
            var file = JsonSerializer.Deserialize<BindingFile>(json);
            if (file == null || file.version != 2 || file.bindings == null)
                return false;

            _bindings.Clear();
            _gridToSources.Clear();

            for (int i = 0; i < file.bindings.Count; i++)
            {
                var entry = file.bindings[i];
                var source = new KeyBindingSource(entry.vk, (KeyModifiers)entry.mod);
                var target = new KeyBindingTarget(entry.grid, entry.shifted);
                _bindings[source] = target;

                if (!_gridToSources.TryGetValue(entry.grid, out var sources))
                {
                    sources = new List<KeyBindingSource>();
                    _gridToSources[entry.grid] = sources;
                }
                sources.Add(source);
            }

            return true;
        }

        /// <summary>
        /// Migrate from old AltKeyConfiguration format: a dictionary of int to string (VK → grid).
        /// Each old entry becomes (VK, Alt) → (grid, shifted=false).
        /// </summary>
        internal bool TryMigrateFromOldFormat(string json)
        {
            var oldMap = JsonSerializer.Deserialize<Dictionary<int, string>>(json);
            if (oldMap == null || oldMap.Count == 0)
                return false;

            _bindings.Clear();
            _gridToSources.Clear();

            var enumerator = oldMap.GetEnumerator();
            while (enumerator.MoveNext())
            {
                var vkCode = enumerator.Current.Key;
                var gridPos = enumerator.Current.Value;
                var source = new KeyBindingSource(vkCode, KeyModifiers.Alt);
                var target = new KeyBindingTarget(gridPos, false);
                _bindings[source] = target;

                if (!_gridToSources.TryGetValue(gridPos, out var sources))
                {
                    sources = new List<KeyBindingSource>();
                    _gridToSources[gridPos] = sources;
                }
                sources.Add(source);
            }

            return true;
        }

        private static string GetConfigFilePath()
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            return Path.Combine(appData, "RetroTerm", "tdv-key-bindings.json");
        }

        private static string GetOldConfigFilePath()
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            return Path.Combine(appData, "RetroTerm", "alt-key-config.json");
        }

        // ─── Avalonia Key Conversion ─────────────────────────────────

        // Avalonia Key enum integer values (from Avalonia.Input.Key)
        private const int AvKey_Back = 2;
        private const int AvKey_Tab = 3;
        private const int AvKey_Enter = 6;
        private const int AvKey_Escape = 13;
        private const int AvKey_Space = 18;
        private const int AvKey_PageUp = 19;
        private const int AvKey_PageDown = 20;
        private const int AvKey_End = 21;
        private const int AvKey_Home = 22;
        private const int AvKey_Left = 23;
        private const int AvKey_Up = 24;
        private const int AvKey_Right = 25;
        private const int AvKey_Down = 26;
        private const int AvKey_Insert = 31;
        private const int AvKey_Delete = 32;
        private const int AvKey_D0 = 34;
        private const int AvKey_D9 = 43;
        private const int AvKey_A = 44;
        private const int AvKey_Z = 69;
        private const int AvKey_NumPad0 = 74;
        private const int AvKey_NumPad9 = 83;
        private const int AvKey_Multiply = 84;
        private const int AvKey_Add = 85;
        private const int AvKey_Separator = 86;
        private const int AvKey_Subtract = 87;
        private const int AvKey_Decimal = 88;
        private const int AvKey_Divide = 89;
        private const int AvKey_F1 = 90;
        private const int AvKey_F24 = 113;
        private const int AvKey_NumLock = 114;
        private const int AvKey_Scroll = 115;
        private const int AvKey_LeftShift = 116;
        private const int AvKey_RightShift = 117;
        private const int AvKey_LeftCtrl = 118;
        private const int AvKey_RightCtrl = 119;
        private const int AvKey_LeftAlt = 120;
        private const int AvKey_RightAlt = 121;
        private const int AvKey_OemSemicolon = 140;
        private const int AvKey_OemPlus = 141;
        private const int AvKey_OemComma = 142;
        private const int AvKey_OemMinus = 143;
        private const int AvKey_OemPeriod = 144;
        private const int AvKey_OemQuestion = 145;
        private const int AvKey_OemTilde = 146;
        private const int AvKey_OemOpenBrackets = 149;
        private const int AvKey_OemPipe = 150;
        private const int AvKey_OemCloseBrackets = 151;
        private const int AvKey_OemQuotes = 152;
        private const int AvKey_Oem8 = 153;
        private const int AvKey_OemBackslash = 154;

        /// <summary>
        /// Convert Avalonia Key enum value to Windows Virtual Key code.
        /// Avalonia Key.A=44→VK_A=65, Key.D0=34→VK_0=48, Key.F1=90→VK_F1=112, etc.
        /// </summary>
        public static int AvaloniaKeyToVK(int avaloniaKey)
        {
            // Letters A-Z: Avalonia 44-69 → VK 65-90 (offset +21)
            if (avaloniaKey >= AvKey_A && avaloniaKey <= AvKey_Z)
                return avaloniaKey + 21;

            // Digits D0-D9: Avalonia 34-43 → VK 48-57 (offset +14)
            if (avaloniaKey >= AvKey_D0 && avaloniaKey <= AvKey_D9)
                return avaloniaKey + 14;

            // Function keys F1-F24: Avalonia 90-113 → VK 112-135 (offset +22)
            if (avaloniaKey >= AvKey_F1 && avaloniaKey <= AvKey_F24)
                return avaloniaKey + 22;

            // NumPad 0-9: Avalonia 74-83 → VK 96-105 (offset +22)
            if (avaloniaKey >= AvKey_NumPad0 && avaloniaKey <= AvKey_NumPad9)
                return avaloniaKey + 22;

            // All other keys: explicit mapping
            return avaloniaKey switch
            {
                // Control keys
                AvKey_Back => 8,          // VK_BACK
                AvKey_Tab => 9,           // VK_TAB
                AvKey_Enter => 13,        // VK_RETURN
                AvKey_Escape => 27,       // VK_ESCAPE
                AvKey_Space => 32,        // VK_SPACE

                // Navigation keys
                AvKey_PageUp => 33,       // VK_PRIOR
                AvKey_PageDown => 34,     // VK_NEXT
                AvKey_End => 35,          // VK_END
                AvKey_Home => 36,         // VK_HOME
                AvKey_Left => 37,         // VK_LEFT
                AvKey_Up => 38,           // VK_UP
                AvKey_Right => 39,        // VK_RIGHT
                AvKey_Down => 40,         // VK_DOWN
                AvKey_Insert => 45,       // VK_INSERT
                AvKey_Delete => 46,       // VK_DELETE

                // Modifier keys
                AvKey_LeftShift => 160,   // VK_LSHIFT
                AvKey_RightShift => 161,  // VK_RSHIFT
                AvKey_LeftCtrl => 162,    // VK_LCONTROL
                AvKey_RightCtrl => 163,   // VK_RCONTROL
                AvKey_LeftAlt => 164,     // VK_LMENU
                AvKey_RightAlt => 165,    // VK_RMENU
                AvKey_NumLock => 144,     // VK_NUMLOCK
                AvKey_Scroll => 145,      // VK_SCROLL

                // Numpad operators
                AvKey_Multiply => 106,    // VK_MULTIPLY
                AvKey_Add => 107,         // VK_ADD
                AvKey_Separator => 108,   // VK_SEPARATOR
                AvKey_Subtract => 109,    // VK_SUBTRACT
                AvKey_Decimal => 110,     // VK_DECIMAL
                AvKey_Divide => 111,      // VK_DIVIDE

                // OEM/punctuation keys
                AvKey_OemSemicolon => 186,     // VK_OEM_1 (;:)
                AvKey_OemPlus => 187,          // VK_OEM_PLUS (=+)
                AvKey_OemComma => 188,         // VK_OEM_COMMA (,<)
                AvKey_OemMinus => 189,         // VK_OEM_MINUS (-_)
                AvKey_OemPeriod => 190,        // VK_OEM_PERIOD (.>)
                AvKey_OemQuestion => 191,      // VK_OEM_2 (/?)
                AvKey_OemTilde => 192,         // VK_OEM_3 (`~)
                AvKey_OemOpenBrackets => 219,  // VK_OEM_4 ([{)
                AvKey_OemPipe => 220,          // VK_OEM_5 (\|)
                AvKey_OemCloseBrackets => 221, // VK_OEM_6 (]})
                AvKey_OemQuotes => 222,        // VK_OEM_7 ('")
                AvKey_Oem8 => 223,             // VK_OEM_8
                AvKey_OemBackslash => 226,     // VK_OEM_102

                _ => avaloniaKey
            };
        }

        /// <summary>
        /// Get the Avalonia Key enum name for a given integer value.
        /// Used for diagnostic display in binding popup.
        /// </summary>
        public static string GetAvaloniaKeyName(int avaloniaKey)
        {
            if (avaloniaKey >= AvKey_A && avaloniaKey <= AvKey_Z)
                return "Key." + (char)('A' + avaloniaKey - AvKey_A);
            if (avaloniaKey >= AvKey_D0 && avaloniaKey <= AvKey_D9)
                return "Key.D" + (avaloniaKey - AvKey_D0);
            if (avaloniaKey >= AvKey_F1 && avaloniaKey <= AvKey_F24)
                return "Key.F" + (avaloniaKey - AvKey_F1 + 1);
            if (avaloniaKey >= AvKey_NumPad0 && avaloniaKey <= AvKey_NumPad9)
                return "Key.NumPad" + (avaloniaKey - AvKey_NumPad0);

            return avaloniaKey switch
            {
                AvKey_Back => "Key.Back",
                AvKey_Tab => "Key.Tab",
                AvKey_Enter => "Key.Enter",
                AvKey_Escape => "Key.Escape",
                AvKey_Space => "Key.Space",
                AvKey_PageUp => "Key.PageUp",
                AvKey_PageDown => "Key.PageDown",
                AvKey_End => "Key.End",
                AvKey_Home => "Key.Home",
                AvKey_Left => "Key.Left",
                AvKey_Up => "Key.Up",
                AvKey_Right => "Key.Right",
                AvKey_Down => "Key.Down",
                AvKey_Insert => "Key.Insert",
                AvKey_Delete => "Key.Delete",
                AvKey_Multiply => "Key.Multiply",
                AvKey_Add => "Key.Add",
                AvKey_Separator => "Key.Separator",
                AvKey_Subtract => "Key.Subtract",
                AvKey_Decimal => "Key.Decimal",
                AvKey_Divide => "Key.Divide",
                AvKey_OemSemicolon => "Key.OemSemicolon",
                AvKey_OemPlus => "Key.OemPlus",
                AvKey_OemComma => "Key.OemComma",
                AvKey_OemMinus => "Key.OemMinus",
                AvKey_OemPeriod => "Key.OemPeriod",
                AvKey_OemQuestion => "Key.OemQuestion",
                AvKey_OemTilde => "Key.OemTilde",
                AvKey_OemOpenBrackets => "Key.OemOpenBrackets",
                AvKey_OemPipe => "Key.OemPipe",
                AvKey_OemCloseBrackets => "Key.OemCloseBrackets",
                AvKey_OemQuotes => "Key.OemQuotes",
                AvKey_Oem8 => "Key.Oem8",
                AvKey_OemBackslash => "Key.OemBackslash",
                AvKey_LeftShift => "Key.LeftShift",
                AvKey_RightShift => "Key.RightShift",
                AvKey_LeftCtrl => "Key.LeftCtrl",
                AvKey_RightCtrl => "Key.RightCtrl",
                AvKey_LeftAlt => "Key.LeftAlt",
                AvKey_RightAlt => "Key.RightAlt",
                AvKey_NumLock => "Key.NumLock",
                AvKey_Scroll => "Key.Scroll",
                _ => $"Key({avaloniaKey})"
            };
        }

        // ─── Testing Support ───────────────────────────────────────

        /// <summary>
        /// Reset singleton for test isolation.
        /// </summary>
        internal static void ResetForTesting()
        {
            lock (_lock)
            {
                _instance = new TDVKeyBindingConfiguration();
                _instance.SetDefaults();
            }
        }

        // ─── Legacy Compatibility (for migration period) ───────────

        /// <summary>
        /// Get the grid position for an Alt+VK combination (legacy compatibility).
        /// </summary>
        public string? GetGridPosition(int vkCode)
        {
            var source = new KeyBindingSource(vkCode, KeyModifiers.Alt);
            if (_bindings.TryGetValue(source, out var target))
                return target.GridPosition;
            return null;
        }

        /// <summary>
        /// Check if an Alt+VK combination is available (not bound).
        /// </summary>
        public bool IsVKCodeAvailable(int vkCode)
        {
            var source = new KeyBindingSource(vkCode, KeyModifiers.Alt);
            return !_bindings.ContainsKey(source);
        }
    }
}
