using System;
using System.Collections.Generic;

namespace RetroTerm.Core.Terminal.Emulators.TDV
{
    /// <summary>
    /// TDV keyboard mapping for function keys and special keys
    /// Maps SDL keys to TDV escape sequences
    /// </summary>
    public class TDVKeyboardMapper
    {
        private readonly Dictionary<string, string> _keyMappings;
        private readonly bool _is2115Mode;
        private readonly bool _tdvArrowsIsEscCode;

        public TDVKeyboardMapper(bool is2115Mode = false, bool tdvArrowsIsEscCode = false)
        {
            _is2115Mode = is2115Mode;
            _tdvArrowsIsEscCode = tdvArrowsIsEscCode;
            _keyMappings = InitializeKeyMappings();
        }

        /// <summary>
        /// Map a key to TDV escape sequence
        /// </summary>
        public string? MapKey(string keyName, bool shift = false, bool ctrl = false, bool alt = false)
        {
            var key = keyName.ToUpper();
            var modifier = GetModifierString(shift, ctrl, alt);

            // TDV 2115 mode has limited key support
            if (_is2115Mode)
            {
                return Map2115Key(key, modifier);
            }

            // TDV 2200/2215 extended key support
            return MapExtendedKey(key, modifier);
        }

        private string? Map2115Key(string key, string modifier)
        {
            // TDV 2115 has basic function keys only
            switch (key)
            {
                case "F1": return "\x1B[11~";
                case "F2": return "\x1B[12~";
                case "F3": return "\x1B[13~";
                case "F4": return "\x1B[14~";
                case "F5": return "\x1B[15~";
                case "F6": return "\x1B[17~";
                case "F7": return "\x1B[18~";
                case "F8": return "\x1B[19~";
                case "F9": return "\x1B[20~";
                case "F10": return "\x1B[21~";
                case "F11": return "\x1B[23~";
                case "F12": return "\x1B[24~";

                // Arrow keys - TDV sends C0 control codes by default (hardware spec)
                // ESC sequences available as fallback if _tdvArrowsIsEscCode is true
                case "UP": return _tdvArrowsIsEscCode ? "\x1B[A" : "\x1C";      // FS (0x1C) or ESC[A
                case "DOWN": return _tdvArrowsIsEscCode ? "\x1B[B" : "\x0B";    // VT (0x0B) or ESC[B
                case "RIGHT": return _tdvArrowsIsEscCode ? "\x1B[C" : "\x18";   // CAN (0x18) or ESC[C
                case "LEFT": return _tdvArrowsIsEscCode ? "\x1B[D" : "\x08";    // BS (0x08) or ESC[D

                // Home/End - Home sends C0 code by default
                case "HOME": return _tdvArrowsIsEscCode ? "\x1B[H" : "\x1D";    // GS (0x1D) or ESC[H
                case "END": return "\x1B[F";

                // Page Up/Down
                case "PAGEUP": return "\x1B[5~";
                case "PAGEDOWN": return "\x1B[6~";

                // Insert/Delete
                case "INSERT": return "\x1B[2~";
                case "DELETE": return "\x1B[3~";

                default:
                    return null; // Not supported in 2115 mode
            }
        }

        private string? MapExtendedKey(string key, string modifier)
        {
            // Check modified arrow/navigation keys FIRST (before switch catches unmodified versions)
            if (modifier != "")
            {
                var modifiedResult = MapModifierKey(key, modifier);
                if (modifiedResult != null)
                {
                    return modifiedResult;
                }
            }

            // TDV 2200/2215 extended key support
            switch (key)
            {
                // Function keys F1-F12
                case "F1": return "\x1B[11~";
                case "F2": return "\x1B[12~";
                case "F3": return "\x1B[13~";
                case "F4": return "\x1B[14~";
                case "F5": return "\x1B[15~";
                case "F6": return "\x1B[17~";
                case "F7": return "\x1B[18~";
                case "F8": return "\x1B[19~";
                case "F9": return "\x1B[20~";
                case "F10": return "\x1B[21~";
                case "F11": return "\x1B[23~";
                case "F12": return "\x1B[24~";

                // Extended function keys F13-F20
                case "F13": return "\x1B[25~";
                case "F14": return "\x1B[26~";
                case "F15": return "\x1B[28~";
                case "F16": return "\x1B[29~";
                case "F17": return "\x1B[31~";
                case "F18": return "\x1B[32~";
                case "F19": return "\x1B[33~";
                case "F20": return "\x1B[34~";

                // TDV-specific PUSH keys (8 programmable keys with shift variants)
                case "PUSH1": return "\x1B[?1~";  // PUSH Key 1
                case "PUSH2": return "\x1B[?2~";  // PUSH Key 2
                case "PUSH3": return "\x1B[?3~";  // PUSH Key 3
                case "PUSH4": return "\x1B[?4~";  // PUSH Key 4
                case "PUSH5": return "\x1B[?5~";  // PUSH Key 5
                case "PUSH6": return "\x1B[?6~";  // PUSH Key 6
                case "PUSH7": return "\x1B[?7~";  // PUSH Key 7
                case "PUSH8": return "\x1B[?8~";  // PUSH Key 8

                // TDV-specific soft keys
                case "SOFT1": return "\x1B[?A";   // Soft Key 1
                case "SOFT2": return "\x1B[?B";   // Soft Key 2
                case "SOFT3": return "\x1B[?C";   // Soft Key 3
                case "SOFT4": return "\x1B[?D";   // Soft Key 4
                case "SOFT5": return "\x1B[?E";   // Soft Key 5
                case "SOFT6": return "\x1B[?F";   // Soft Key 6
                case "SOFT7": return "\x1B[?G";   // Soft Key 7
                case "SOFT8": return "\x1B[?H";   // Soft Key 8

                // TDV-specific application keys
                case "HELP": return "\x1B[28~";     // HELP (HJÄLP)
                case "DO": return "\x1B[29~";       // DO (Execute)
                case "FUNC": return "\x1B[@";       // FUNC (FUNK)
                case "PRINT": return "\x1B[A";      // PRINT (SKRIV)
                case "EXIT": return "\x1B[C";       // EXIT (SLUT)
                case "CANCEL": return "\x1B[27~";   // CANCEL
                case "COMMAND": return "\x1B[26~";  // COMMAND
                case "FIND": return "\x1B[1;2R";    // FIND
                case "INSERT_HERE": return "\x1B[2;2~"; // INSERT HERE
                case "REMOVE": return "\x1B[3;2~";  // REMOVE
                case "SELECT": return "\x1B[4;2~";  // SELECT
                case "PREV": return "\x1B[5;2~";    // PREV
                case "NEXT": return "\x1B[6;2~";    // NEXT

                // TDV 1200-specific editing keys
                case "COPY": return "\x1B[M";       // COPY (KOPI)
                case "MOVE": return "\x1B[N";       // MOVE (FLYTT)
                case "JUST": return "\x1B[ H";      // JUST
                case "MARK": return "\x1B[X";       // MARK
                case "FIELD": return "\x1B[Y";      // FIELD
                case "PARA": return "\x1B[Z";       // PARA (Paragraph)
                case "SENT": return "\x1B[[";       // SENT (Sentence)
                case "WORD": return "\x1B[\\";      // WORD

                // TDV-specific control keys
                case "TAB_RIGHT": return "\x09";   // Tab right
                case "TAB_LEFT": return "\x1B[Z"; // Tab left (CSI Z)
                case "ROLL_UP": return "\x0C";    // Roll up (FF)
                case "ROLL_DOWN": return "\x17";  // Roll down (ETB)
                case "ERASE_PAGE": return "\x19"; // Erase page (EM)
                case "ERASE_LINE": return "\x04"; // Erase line (EOT)
                case "DEL_LINE": return "\x1B[M"; // Delete line (CSI M)
                case "INS_LINE": return "\x1B[L"; // Insert line (CSI L)
                case "DEL_CHAR": return "\x1B[P"; // Delete character (CSI P)
                case "INS_CHAR": return "\x1B[@"; // Insert character (CSI @)

                // TDV-specific cursor keys
                case "CURSOR_UP": return "\x1C";   // Cursor up (FS)
                case "CURSOR_DOWN": return "\x0B"; // Cursor down (VT)
                case "CURSOR_RIGHT": return "\x18"; // Cursor right (CAN)
                case "CURSOR_LEFT": return "\x08";  // Cursor left (BS)
                case "CURSOR_HOME": return "\x1D"; // Cursor home (GS)
                case "CURSOR_RETURN": return "\x0D"; // Cursor return (CR)

                // TDV-specific video control
                case "VIDEO_ON": return "\x03";    // Video on (ETX)
                case "VIDEO_OFF": return "\x02";   // Video off (STX)

                // TDV-specific LED control
                case "LIGHT1_ON": return "\x05";    // Light 1 on (ENQ)
                case "LIGHT2_ON": return "\x06";   // Light 2 on (ACK)
                case "LIGHT3_ON": return "\x15";   // Light 3 on (NAK)
                case "LIGHTS_OFF": return "\x16";  // All lights off (SYN)

                // TDV-specific attributes
                case "UNDERLINE": return "\x0E";    // Underline (SO)
                case "NORMAL": return "\x0F";       // Normal (SI)

                // TDV-specific line feed
                case "LINE_FEED": return "\x0A";   // Line feed (LF)

                // Arrow keys - TDV sends C0 control codes by default (hardware spec)
                // ESC sequences available as fallback if _tdvArrowsIsEscCode is true
                case "UP": return _tdvArrowsIsEscCode ? "\x1B[A" : "\x1C";      // FS (0x1C) or ESC[A
                case "DOWN": return _tdvArrowsIsEscCode ? "\x1B[B" : "\x0B";    // VT (0x0B) or ESC[B
                case "RIGHT": return _tdvArrowsIsEscCode ? "\x1B[C" : "\x18";   // CAN (0x18) or ESC[C
                case "LEFT": return _tdvArrowsIsEscCode ? "\x1B[D" : "\x08";    // BS (0x08) or ESC[D

                // Home/End - Home sends C0 code by default
                case "HOME": return _tdvArrowsIsEscCode ? "\x1B[H" : "\x1D";    // GS (0x1D) or ESC[H
                case "END": return "\x1B[F";

                // Page Up/Down
                case "PAGEUP": return "\x1B[5~";
                case "PAGEDOWN": return "\x1B[6~";

                // Insert/Delete
                case "INSERT": return "\x1B[2~";
                case "DELETE": return "\x1B[3~";

                // Keypad keys - mapped to TDV special keys
                case "KP_0": return "\x1B[M";       // COPY (KOPI)
                case "KP_1": return "\x1B[28~";     // HELP (HJÄLP)
                case "KP_2": return "\x1B[@";       // FUNC (FUNK)
                case "KP_3": return "\x1B[A";       // PRINT (SKRIV)
                case "KP_4": return "\x1B[C";       // EXIT (SLUT)
                case "KP_5": return "\x1B[29~";     // DO
                case "KP_6": return "\x1B[27~";     // CANCEL
                case "KP_7": return "\x1B[1;2R";    // FIND
                case "KP_8": return "\x1B[4;2~";    // SELECT
                case "KP_9": return "\x1B[26~";     // COMMAND
                case "KP_ENTER": return "\r";       // Enter
                case "KP_PLUS": return "\x1B[N";    // MOVE (FLYTT)
                case "KP_MINUS": return "\x1B[ H";  // JUST
                case "KP_MULTIPLY": return "*";
                case "KP_DIVIDE": return "/";
                case "KP_PERIOD": return ".";

                // TDV-specific keys
                case "PRINTSCREEN": return "\x1B[?1i"; // Print screen
                case "SCROLLLOCK": return "\x1B[?2i"; // Scroll lock
                case "PAUSE": return "\x1B[?3i"; // Pause

                // Fall back to basic key mappings (RETURN, TAB, BACKSPACE, etc.)
                default:
                    if (_keyMappings.TryGetValue(key, out var mapping))
                    {
                        return mapping;
                    }
                    return null;
            }
        }

        private string? MapModifierKey(string key, string modifier)
        {
            // Handle modifier combinations
            switch (key)
            {
                case "UP":
                    if (modifier == "SHIFT") return "\x1B[1;2A";
                    if (modifier == "CTRL") return "\x1B[1;5A";
                    if (modifier == "ALT") return "\x1B[1;3A";
                    break;
                case "DOWN":
                    if (modifier == "SHIFT") return "\x1B[1;2B";
                    if (modifier == "CTRL") return "\x1B[1;5B";
                    if (modifier == "ALT") return "\x1B[1;3B";
                    break;
                case "RIGHT":
                    if (modifier == "SHIFT") return "\x1B[1;2C";
                    if (modifier == "CTRL") return "\x1B[1;5C";
                    if (modifier == "ALT") return "\x1B[1;3C";
                    break;
                case "LEFT":
                    if (modifier == "SHIFT") return "\x1B[1;2D";
                    if (modifier == "CTRL") return "\x1B[1;5D";
                    if (modifier == "ALT") return "\x1B[1;3D";
                    break;
            }
            return null;
        }

        private string GetModifierString(bool shift, bool ctrl, bool alt)
        {
            if (shift && ctrl && alt) return "SHIFT_CTRL_ALT";
            if (shift && ctrl) return "SHIFT_CTRL";
            if (shift && alt) return "SHIFT_ALT";
            if (ctrl && alt) return "CTRL_ALT";
            if (shift) return "SHIFT";
            if (ctrl) return "CTRL";
            if (alt) return "ALT";
            return "";
        }

        private Dictionary<string, string> InitializeKeyMappings()
        {
            return new Dictionary<string, string>
            {
                // Basic character mappings for TDV
                {"SPACE", " "},
                {"TAB", "\t"},
                {"RETURN", "\r"},
                {"ENTER", "\r"},
                {"BACKSPACE", "\b"},
                {"ESCAPE", "\x1B"},
            };
        }

        /// <summary>
        /// Check if a key is supported in current mode
        /// </summary>
        public bool IsKeySupported(string keyName)
        {
            var key = keyName.ToUpper();

            if (_is2115Mode)
            {
                // TDV 2115 supports basic function keys and arrows
                return key.StartsWith("F") && int.TryParse(key.Substring(1), out int fnum) && fnum >= 1 && fnum <= 12 ||
                       key == "UP" || key == "DOWN" || key == "LEFT" || key == "RIGHT" ||
                       key == "HOME" || key == "END" || key == "PAGEUP" || key == "PAGEDOWN" ||
                       key == "INSERT" || key == "DELETE";
            }
            else
            {
                // TDV 2200/2215 supports extended keys
                return true; // Most keys supported
            }
        }

        /// <summary>
        /// Get TDV-specific key sequences
        /// </summary>
        public string? GetTDVKeySequence(string keyName, TDVKeyType keyType = TDVKeyType.Function)
        {
            switch (keyType)
            {
                case TDVKeyType.Function:
                    return MapKey(keyName);
                case TDVKeyType.PushKey:
                    return $"\x1BP{keyName}\x1B\\"; // DCS sequence for push keys
                case TDVKeyType.SoftKey:
                    return $"\x1B[?{keyName}~"; // Soft key sequence
                default:
                    return MapKey(keyName);
            }
        }

        /// <summary>
        /// Get TDV 2115 C0 control code sequence
        /// </summary>
        public string? GetTDV2115ControlCode(string keyName)
        {
            return keyName switch
            {
                "VIDEO_OFF" => "\x02",    // STX
                "VIDEO_ON" => "\x03",     // ETX
                "ERASE_LINE" => "\x04",   // EOT
                "LIGHT1_ON" => "\x05",    // ENQ
                "LIGHT2_ON" => "\x06",    // ACK
                "BELL" => "\x07",         // BEL
                "CURSOR_LEFT" => "\x08",  // BS
                "TAB" => "\x09",          // HT
                "LINE_FEED" => "\x0A",    // LF
                "CURSOR_DOWN" => "\x0B",  // VT
                "ROLL_UP" => "\x0C",      // FF
                "CURSOR_RETURN" => "\x0D", // CR
                "UNDERLINE" => "\x0E",    // SO
                "NORMAL" => "\x0F",       // SI
                "CURSOR_LOAD" => "\x10",  // DLE (start binary positioning)
                "ROLL_DOWN" => "\x17",    // ETB
                "CURSOR_RIGHT" => "\x18", // CAN
                "ERASE_PAGE" => "\x19",   // EM
                "LIGHT3_ON" => "\x15",   // NAK
                "LIGHTS_OFF" => "\x16",  // SYN
                "CURSOR_UP" => "\x1C",   // FS
                "CURSOR_HOME" => "\x1D", // GS
                _ => null
            };
        }

        /// <summary>
        /// Check if a key is a TDV 2115 C0 control code
        /// </summary>
        public bool IsTDV2115ControlCode(string keyName)
        {
            return keyName switch
            {
                "VIDEO_OFF" or "VIDEO_ON" or "ERASE_LINE" or "LIGHT1_ON" or "LIGHT2_ON" or
                "BELL" or "CURSOR_LEFT" or "TAB" or "LINE_FEED" or "CURSOR_DOWN" or
                "ROLL_UP" or "CURSOR_RETURN" or "UNDERLINE" or "NORMAL" or "CURSOR_LOAD" or
                "ROLL_DOWN" or "CURSOR_RIGHT" or "ERASE_PAGE" or "LIGHT3_ON" or "LIGHTS_OFF" or
                "CURSOR_UP" or "CURSOR_HOME" => true,
                _ => false
            };
        }

        /// <summary>
        /// Get TDV PUSH key sequence (programmable keys)
        /// </summary>
        public string? GetTDVPushKeySequence(int pushKeyNumber, bool shifted = false)
        {
            if (pushKeyNumber < 1 || pushKeyNumber > 8)
                return null;

            // TDV PUSH keys use DCS sequences
            var shiftFlag = shifted ? "S" : "N";
            return $"\x1BP{shiftFlag}{pushKeyNumber}\x1B\\";
        }

        /// <summary>
        /// Get TDV soft key sequence
        /// </summary>
        public string? GetTDVSoftKeySequence(int softKeyNumber)
        {
            if (softKeyNumber < 1 || softKeyNumber > 8)
                return null;

            // TDV soft keys use CSI sequences
            return $"\x1B[?{(char)('A' + softKeyNumber - 1)}";
        }
    }

    /// <summary>
    /// TDV key types for different key categories
    /// </summary>
    public enum TDVKeyType
    {
        Function,
        PushKey,
        SoftKey,
        Special
    }
}
