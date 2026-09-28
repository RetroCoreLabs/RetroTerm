using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;

namespace RetroTerm.Core.Terminal.Input
{
    /// <summary>
    /// Persistent configuration for TDV PUSH keys (P1-P16).
    /// Singleton with JSON storage at %AppData%\RetroTerm\push-keys.json.
    /// P1-P8 (unshifted G1-G8), P9-P16 (shifted G1-G8).
    /// </summary>
    public class TDVPushKeyConfiguration
    {
        private static TDVPushKeyConfiguration? _instance;
        private static readonly object _lock = new object();

        private readonly Dictionary<int, string> _keys = new Dictionary<int, string>();

        /// <summary>
        /// Event raised when configuration changes
        /// </summary>
        public event EventHandler? ConfigurationChanged;

        /// <summary>
        /// Singleton instance
        /// </summary>
        public static TDVPushKeyConfiguration Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_lock)
                    {
                        if (_instance == null)
                        {
                            _instance = new TDVPushKeyConfiguration();
                            _instance.Load();
                        }
                    }
                }
                return _instance;
            }
        }

        private TDVPushKeyConfiguration()
        {
        }

        /// <summary>
        /// Get the maximum string length for a PUSH key number.
        /// P1-P8 = 12 chars, P9-P12 = 32 chars, P13-P16 = 48 chars.
        /// </summary>
        public static int GetMaxLength(int keyNumber)
        {
            if (keyNumber >= 1 && keyNumber <= 8) return 12;
            if (keyNumber >= 9 && keyNumber <= 12) return 32;
            if (keyNumber >= 13 && keyNumber <= 16) return 48;
            return 0;
        }

        /// <summary>
        /// Convert grid position (G1-G8) + shift flag to PUSH key number (1-16).
        /// Returns 0 if the grid position is not a PUSH key.
        /// </summary>
        public static int GridToKeyNumber(string gridPosition, bool shifted)
        {
            if (string.IsNullOrEmpty(gridPosition) || gridPosition.Length < 2
                || gridPosition[0] != 'G')
                return 0;

            if (!int.TryParse(gridPosition.Substring(1), out int gridNum))
                return 0;

            if (gridNum < 1 || gridNum > 8)
                return 0;

            return shifted ? gridNum + 8 : gridNum;
        }

        /// <summary>
        /// Get the stored string for a PUSH key number (1-16).
        /// Returns null if not programmed.
        /// </summary>
        public string? GetKeyString(int keyNumber)
        {
            if (keyNumber < 1 || keyNumber > 16)
                return null;
            return _keys.TryGetValue(keyNumber, out var value) ? value : null;
        }

        /// <summary>
        /// Get the stored string for a grid position + shift combination.
        /// Returns null if not programmed or not a PUSH key.
        /// </summary>
        public string? GetKeyString(string gridPosition, bool shifted)
        {
            int keyNum = GridToKeyNumber(gridPosition, shifted);
            if (keyNum == 0) return null;
            return GetKeyString(keyNum);
        }

        /// <summary>
        /// Check if a PUSH key is programmed.
        /// </summary>
        public bool IsKeyProgrammed(int keyNumber)
        {
            return _keys.ContainsKey(keyNumber);
        }

        /// <summary>
        /// Program a PUSH key with a string. Enforces max length.
        /// Pass null or empty to clear.
        /// </summary>
        public bool ProgramKey(int keyNumber, string? sequence)
        {
            if (keyNumber < 1 || keyNumber > 16)
                return false;

            if (string.IsNullOrEmpty(sequence))
            {
                _keys.Remove(keyNumber);
                ConfigurationChanged?.Invoke(this, EventArgs.Empty);
                return true;
            }

            int maxLen = GetMaxLength(keyNumber);
            if (sequence.Length > maxLen)
                sequence = sequence.Substring(0, maxLen);

            _keys[keyNumber] = sequence;
            ConfigurationChanged?.Invoke(this, EventArgs.Empty);
            return true;
        }

        /// <summary>
        /// Clear a specific key.
        /// </summary>
        public void ClearKey(int keyNumber)
        {
            if (_keys.Remove(keyNumber))
                ConfigurationChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// Clear all programmed keys.
        /// </summary>
        public void ClearAll()
        {
            _keys.Clear();
            ConfigurationChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// Get all programmed keys as a snapshot.
        /// </summary>
        public Dictionary<int, string> GetAllKeys()
        {
            return new Dictionary<int, string>(_keys);
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
                    Directory.CreateDirectory(directory);

                var data = new PushKeyFileData { Version = 1, Keys = new Dictionary<string, string>() };
                // Store keys with string key names for JSON compatibility
                foreach (var kvp in _keys)
                {
                    // Store the string as hex-encoded bytes so control chars survive JSON
                    data.Keys[kvp.Key.ToString()] = EncodeToHex(kvp.Value);
                }

                var json = JsonSerializer.Serialize(data, new JsonSerializerOptions
                {
                    WriteIndented = true
                });
                File.WriteAllText(configPath, json);
            }
            catch
            {
                // Silently fail on save errors (read-only filesystem, etc.)
            }
        }

        /// <summary>
        /// Load configuration from JSON file.
        /// </summary>
        public void Load()
        {
            _keys.Clear();
            var configPath = GetConfigFilePath();
            if (!File.Exists(configPath))
                return;

            try
            {
                var json = File.ReadAllText(configPath);
                var data = JsonSerializer.Deserialize<PushKeyFileData>(json);
                if (data?.Keys == null)
                    return;

                foreach (var kvp in data.Keys)
                {
                    if (int.TryParse(kvp.Key, out int keyNum) && keyNum >= 1 && keyNum <= 16)
                    {
                        var decoded = DecodeFromHex(kvp.Value);
                        if (!string.IsNullOrEmpty(decoded))
                        {
                            int maxLen = GetMaxLength(keyNum);
                            if (decoded.Length > maxLen)
                                decoded = decoded.Substring(0, maxLen);
                            _keys[keyNum] = decoded;
                        }
                    }
                }
            }
            catch
            {
                // Silently fail on load errors — start with empty config
            }
        }

        /// <summary>
        /// Encode a string to hex pairs (preserves control characters in JSON).
        /// "AB\x1B" → "41421B"
        /// </summary>
        private static string EncodeToHex(string value)
        {
            var sb = new StringBuilder(value.Length * 2);
            for (int i = 0; i < value.Length; i++)
            {
                sb.Append(((byte)value[i]).ToString("X2"));
            }
            return sb.ToString();
        }

        /// <summary>
        /// Decode hex pairs back to string.
        /// "41421B" → "AB\x1B"
        /// </summary>
        private static string DecodeFromHex(string hex)
        {
            if (string.IsNullOrEmpty(hex) || hex.Length % 2 != 0)
                return string.Empty;

            var sb = new StringBuilder(hex.Length / 2);
            for (int i = 0; i < hex.Length; i += 2)
            {
                if (i + 1 < hex.Length)
                {
                    int high = HexCharToNibble(hex[i]);
                    int low = HexCharToNibble(hex[i + 1]);
                    if (high < 0 || low < 0)
                        return string.Empty;
                    sb.Append((char)((high << 4) | low));
                }
            }
            return sb.ToString();
        }

        private static int HexCharToNibble(char c)
        {
            if (c >= '0' && c <= '9') return c - '0';
            if (c >= 'A' && c <= 'F') return c - 'A' + 10;
            if (c >= 'a' && c <= 'f') return c - 'a' + 10;
            return -1;
        }

        private static string GetConfigFilePath()
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            return Path.Combine(appData, "RetroTerm", "push-keys.json");
        }

        /// <summary>
        /// Reset singleton for testing.
        /// </summary>
        public static void ResetForTesting()
        {
            lock (_lock)
            {
                _instance = null;
            }
        }

        private class PushKeyFileData
        {
            public int Version { get; set; }
            public Dictionary<string, string>? Keys { get; set; }
        }
    }
}
