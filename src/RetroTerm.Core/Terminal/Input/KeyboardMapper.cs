using System;
using System.Collections.Generic;

namespace RetroTerm.Core.Terminal.Input;

/// <summary>
/// Keyboard mapping system for terminal emulation.
/// 
/// Key Design Principles:
/// - Integer key codes are aligned to Windows Virtual-Key (VK) values for cross-platform compatibility
/// - Non-text keys (arrows, F-keys, navigation) use VK-like numeric codes (e.g., 38=VK_UP, 13=VK_RETURN)
/// - Text/character input (letters, digits, symbols, Å/Ø/Æ) is handled via Unicode from TextInput, independent of keyboard layout
/// - International keyboards (Norwegian, etc.) work correctly as characters come through TextInput as proper Unicode
/// 
/// Configuration Recommendations:
/// - For non-text keys: store VK-like keyCode + KeyModifiers flags
/// - For text keys: store the resulting Unicode string (not a keycode)
/// </summary>

/// <summary>
/// Interface for terminal-specific keyboard mapping.
/// Each terminal emulation type has its own key mapping behavior.
/// Uses integer key codes aligned to Windows Virtual-Key (VK) values for cross-platform compatibility.
/// </summary>
public interface IKeyboardMapper
{
    /// <summary>
    /// Maps a key to its escape sequence for this terminal type.
    /// </summary>
    /// <param name="keyCode">
    /// The key code (integer value aligned to VK codes, e.g., 38=VK_UP, 13=VK_RETURN)
    /// </param>
    /// <param name="modifiers">
    /// Modifier flags (bit flags for Shift, Ctrl, Alt, Meta)
    /// </param>
    /// <param name="terminalModes">
    /// Current terminal mode flags (ApplicationCursorKeys, ApplicationKeypad, etc.)
    /// </param>
    /// <returns>
    /// The escape sequence or null if not mapped
    /// </returns>
    string? MapKey(int keyCode, KeyModifiers modifiers, TerminalModes terminalModes);
}

/// <summary>
/// Key modifier flags for efficient bitwise operations.
/// Used to track which modifier keys are pressed during key mapping.
/// </summary>
[Flags]
public enum KeyModifiers
{
    None = 0,
    Shift = 1,
    Ctrl = 2,
    Alt = 4,
    Meta = 8,  // Windows key, Command key, etc.
}

/// <summary>
/// Terminal mode flags that affect keyboard mapping behavior.
/// These modes change how keys are mapped to escape sequences.
/// </summary>
[Flags]
public enum TerminalModes
{
    None = 0,
    ApplicationCursorKeys = 1,    // DECCKM - Application Cursor Keys Mode
    ApplicationKeypad = 2,        // DECKPAM - Application Keypad Mode
    VT220Mode = 4,                // VT220 extended mode
    TDV2115Mode = 8,              // TDV 2115 compatibility mode
    TDV2215Mode = 16,             // TDV 2215 extended mode
    TDV2200Mode = 32,             // TDV 2200 mode

    /// <summary>
    /// DECBKM (private mode 67) reset - the backarrow key sends DEL rather than BS.
    /// </summary>
    /// <remarks>
    /// <para><b>Why this names DELETE and not BACKSPACE</b></para>
    /// It was written the other way round first, and it broke two tests that were right to break.
    /// <c>TerminalModes.None</c> means "nothing unusual is going on", so every flag in this enum has
    /// to be OFF in the ordinary case. The ordinary case in this emulator is BS, so the flag has to
    /// name the departure from it, which is DEL.
    ///
    /// The first version had a freshly powered terminal reporting a mode nobody had set, and a bare
    /// backspace sending DEL to every host that had said nothing at all.
    ///
    /// Set means DEL (<c>0x7F</c>), absent means BS (<c>0x08</c>). Which byte that key sends has
    /// been the most argued-about thing in terminal history, and DEC made it a mode rather than
    /// pick a side.
    /// </remarks>
    BackarrowSendsDelete = 64,

    /// <summary>
    /// DECARM (private mode 8) reset - a key held down does NOT repeat.
    /// </summary>
    /// <remarks>
    /// Named for the departure, not the norm, for the same reason as
    /// <see cref="BackarrowSendsDelete"/>: a keyboard repeats by default, so the flag has to mean
    /// "this one does not". <c>TerminalModes.None</c> must keep meaning "nothing unusual".
    ///
    /// Unlike every other flag here this one does not change what a key SENDS - it changes whether
    /// the second and later presses happen at all, so it is read by the input path rather than by
    /// the mapper.
    /// </remarks>
    AutoRepeatDisabled = 128,
}

/// <summary>
/// Base keyboard mapper with common functionality.
/// Provides shared logic for terminal-specific keyboard mapping using integer key codes.
/// </summary>
public abstract class BaseKeyboardMapper : IKeyboardMapper
{
    protected readonly Dictionary<int, string> _keyMappings;
    protected readonly Dictionary<(int keyCode, KeyModifiers modifiers), string> _modifierMappings;

    protected BaseKeyboardMapper()
    {
        _keyMappings = new Dictionary<int, string>();
        _modifierMappings = new Dictionary<(int, KeyModifiers), string>();
        InitializeMappings();
    }

    // Windows virtual-key codes for the numeric keypad. Named because "keyCode >= 96 && <= 105"
    // at a call site tells the next reader nothing.
    protected const int VkNumpad0 = 96;
    protected const int VkNumpad9 = 105;

    /// <summary>
    /// The Windows virtual-key code for the backarrow key, which DECBKM decides the byte for.
    /// </summary>
    protected const int VkBack = 8;
    protected const int VkMultiply = 106;
    protected const int VkAdd = 107;
    protected const int VkSeparator = 108;   // the keypad comma, on keyboards that have one
    protected const int VkSubtract = 109;
    protected const int VkDecimal = 110;
    protected const int VkDivide = 111;

    public virtual string? MapKey(int keyCode, KeyModifiers modifiers, TerminalModes terminalModes)
    {
        // Application keypad mode (DECKPAM) changes what the numeric keypad sends, and it does so
        // regardless of any other mapping — so it is decided here, before the tables.
        //
        // Modifiers are deliberately excluded: DEC defines no modified form for these keys, and a
        // Ctrl+keypad binding a user has configured should keep working.
        if (modifiers == KeyModifiers.None && terminalModes.HasFlag(TerminalModes.ApplicationKeypad))
        {
            var keypadSequence = MapApplicationKeypad(keyCode);
            if (keypadSequence != null)
            {
                return keypadSequence;
            }
        }

        // DECBKM (private mode 67) decides what the backarrow key sends, and like the keypad above
        // it wins over the tables - the tables hold one byte and this key has two.
        //
        // Unmodified only, on purpose. Ctrl+Backspace is mapped separately to NUL and a user's own
        // modified binding must keep working; DEC defines no modified form of this key either.
        //
        // VkBack is 8. TDV2200KeyboardMapper overrides MapKey completely and never reaches here,
        // which is right: a TDV keyboard is not a VT keyboard and its erase key is its own affair.
        if (keyCode == VkBack && modifiers == KeyModifiers.None)
        {
            return terminalModes.HasFlag(TerminalModes.BackarrowSendsDelete) ? "\x7F" : "\x08";
        }

        // Handle modifier combinations first
        if (modifiers != KeyModifiers.None)
        {
            var key = (keyCode, modifiers);
            if (_modifierMappings.TryGetValue(key, out var modifierSequence))
            {
                return ApplyTerminalModeModifications(modifierSequence, terminalModes);
            }

            // Ctrl+Space → NUL (0x00) in ALL emulations. Standard terminal behaviour
            // (xterm, VT, and the TDV2200 spacebar's Ctrl variant all send NUL).
            // Lives here in Core so every consumer of the mapper gets it, not just
            // the desktop canvas fallback.
            var control = MapUniversalControl(keyCode, modifiers);
            if (control != null)
            {
                return control;
            }

            // For arrow keys with modifiers, generate xterm-style sequences
            if ((keyCode >= 37 && keyCode <= 40) || // Arrow keys
                (keyCode >= 33 && keyCode <= 36) || // Home, End, PageUp, PageDown
                (keyCode == 45 || keyCode == 46))   // Insert, Delete
            {
                var modifierParam = GetXtermModifierParameter(modifiers);
                if (modifierParam > 0)
                {
                    // Arrow keys and Home/End use ESC[1;<modifier><char> format
                    if (keyCode >= 35 && keyCode <= 40) // Arrows, Home, End
                    {
                        var finalChar = keyCode switch
                        {
                            37 => "D", // Left
                            38 => "A", // Up
                            39 => "C", // Right
                            40 => "B", // Down
                            36 => "H", // Home
                            35 => "F", // End
                            _ => null
                        };

                        if (finalChar != null)
                        {
                            return $"\x1b[1;{modifierParam}{finalChar}";
                        }
                    }
                    // Insert, Delete, PageUp, PageDown use ESC[<num>;<modifier>~ format
                    else
                    {
                        var keyNum = keyCode switch
                        {
                            45 => 2, // Insert
                            46 => 3, // Delete
                            33 => 5, // PageUp
                            34 => 6, // PageDown
                            _ => 0
                        };

                        if (keyNum > 0)
                        {
                            return $"\x1b[{keyNum};{modifierParam}~";
                        }
                    }
                }
            }
        }

        // Handle base key
        if (_keyMappings.TryGetValue(keyCode, out var baseSequence))
        {
            return ApplyTerminalModeModifications(baseSequence, terminalModes);
        }

        return null;
    }

    /// <summary>
    /// Convert KeyModifiers to xterm modifier parameter (1-based)
    /// </summary>
    protected int GetXtermModifierParameter(KeyModifiers modifiers)
    {
        // xterm modifier parameter encoding:
        // 1 = no modifier (not used in this context)
        // 2 = Shift
        // 3 = Alt
        // 4 = Shift+Alt
        // 5 = Ctrl
        // 6 = Shift+Ctrl
        // 7 = Alt+Ctrl
        // 8 = Shift+Alt+Ctrl

        var param = 1;
        if (modifiers.HasFlag(KeyModifiers.Shift)) param += 1;
        if (modifiers.HasFlag(KeyModifiers.Alt)) param += 2;
        if (modifiers.HasFlag(KeyModifiers.Ctrl)) param += 4;

        return param > 1 ? param : 0; // Return 0 if no modifiers
    }

    protected virtual string ApplyTerminalModeModifications(string sequence, TerminalModes terminalModes)
    {
        // Apply terminal mode modifications to the sequence
        if (terminalModes.HasFlag(TerminalModes.ApplicationCursorKeys))
        {
            // Convert ESC[A/B/C/D to ESC O A/B/C/D for application cursor keys
            if (sequence == "\x1b[A") return "\x1bOA";
            if (sequence == "\x1b[B") return "\x1bOB";
            if (sequence == "\x1b[C") return "\x1bOC";
            if (sequence == "\x1b[D") return "\x1bOD";
        }

        // NOTE: application keypad mode is handled in MapKey, before the sequence tables, because
        // it depends on WHICH key was pressed rather than on the sequence a key already produced.
        // This method only sees the finished sequence, which is why the keypad branch that used to
        // sit here was an empty stub — there was nothing it could usefully do.

        return sequence;
    }

    /// <summary>
    /// The Ctrl combinations that mean the same thing on every terminal, VT and TDV alike:
    /// Ctrl+letter is the C0 control it names, Ctrl+Space is NUL, Ctrl+Backspace is NUL.
    ///
    /// These lived in the UI, in two copies — TerminalCanvas and VirtualKeyboardWindow — each
    /// written as a fallback for when the mapper returned null. Two copies of one rule is how the
    /// same physical keypress ends up encoded differently depending on which window has focus, and
    /// what Ctrl+A means is a property of the terminal, not of the control receiving keystrokes.
    ///
    /// Both mappers call this, because TDVKeyboardMapper overrides MapKey completely and would
    /// otherwise stop answering for Ctrl+letter the moment the UI fallback was deleted. That is
    /// NOT the "no VT220 fallback in TDV mode" rule being bent: a C0 code is not a VT220 escape
    /// sequence, it is what the CTRL key on a TDV keyboard has always produced.
    ///
    /// Returns null when the combination is not one of these, so the caller carries on.
    /// </summary>
    protected static string? MapUniversalControl(int keyCode, KeyModifiers modifiers)
    {
        // Bare Ctrl only. Ctrl+Shift+letter is a different combination and may be bound to
        // something; the old UI fallbacks tested for Control exactly, and that is preserved.
        if (modifiers != KeyModifiers.Ctrl)
        {
            return null;
        }

        // Ctrl+A = 0x01 ... Ctrl+Z = 0x1A
        if (keyCode >= 65 && keyCode <= 90)
        {
            return ((char)(keyCode - 64)).ToString();
        }

        switch (keyCode)
        {
            case 32: return "\x00";  // Ctrl+Space  → NUL
            case 8: return "\x00";   // Ctrl+Back   → NUL (would otherwise send BS)
            default: return null;
        }
    }

    /// <summary>
    /// The numeric keypad under DECKPAM (application keypad mode), which is how a VT sends its
    /// keypad when a host asks for it — SS3 (ESC O) plus a letter, rather than the plain digit.
    /// This is what lets a host tell the keypad 4 from the main-keyboard 4.
    ///
    /// Returns null for anything that is not a keypad key, so the caller falls through to the
    /// ordinary tables.
    /// </summary>
    protected virtual string? MapApplicationKeypad(int keyCode)
    {
        // Digits: keypad 0-9 become ESC O p .. ESC O y, in order.
        if (keyCode >= VkNumpad0 && keyCode <= VkNumpad9)
        {
            return "\x1bO" + (char)('p' + (keyCode - VkNumpad0));
        }

        switch (keyCode)
        {
            // These four are DEC's own: the keypad's operator keys.
            case VkMultiply: return "\x1bOj";
            case VkAdd: return "\x1bOk";
            case VkSeparator: return "\x1bOl";
            case VkSubtract: return "\x1bOm";
            case VkDecimal: return "\x1bOn";
            case VkDivide: return "\x1bOo";

            default: return null;
        }
    }

    /// <summary>
    /// Initialize key mappings for this terminal type.
    /// Override in derived classes
    /// </summary>
    protected abstract void InitializeMappings();
}

/// <summary>
/// VT100 keyboard mapper with basic terminal support.
/// Maps keys to VT100 escape sequences using integer key codes aligned to VK values.
/// </summary>
public class VT100KeyboardMapper : BaseKeyboardMapper
{
    protected override void InitializeMappings()
    {
        // Basic control keys (using VK-aligned key codes)
        _keyMappings[13] = "\r";      // 13 = VK_RETURN (Enter)
        _keyMappings[8] = "\x08";     // 8 = VK_BACK (Backspace)
        _keyMappings[9] = "\t";       // 9 = VK_TAB (Tab)
        _keyMappings[27] = "\x1b";    // 27 = VK_ESCAPE (Escape)

        // Navigation keys (VT100 style)
        _keyMappings[38] = "\x1b[A";  // 38 = VK_UP (Up Arrow)
        _keyMappings[40] = "\x1b[B";  // 40 = VK_DOWN (Down Arrow)
        _keyMappings[39] = "\x1b[C";  // 39 = VK_RIGHT (Right Arrow)
        _keyMappings[37] = "\x1b[D";  // 37 = VK_LEFT (Left Arrow)
        _keyMappings[36] = "\x1b[H";  // 36 = VK_HOME
        _keyMappings[35] = "\x1b[F";  // 35 = VK_END
        _keyMappings[33] = "\x1b[5~"; // 33 = VK_PRIOR (PageUp) - xterm/VT compatible
        _keyMappings[34] = "\x1b[6~"; // 34 = VK_NEXT (PageDown)
        _keyMappings[45] = "\x1b[2~"; // 45 = VK_INSERT
        _keyMappings[46] = "\x1b[3~"; // 46 = VK_DELETE

        // Function keys F1-F4 (VT100 style)
        _keyMappings[112] = "\x1bOP"; // 112 = VK_F1
        _keyMappings[113] = "\x1bOQ"; // 113 = VK_F2
        _keyMappings[114] = "\x1bOR"; // 114 = VK_F3
        _keyMappings[115] = "\x1bOS"; // 115 = VK_F4

        // Extended function keys (common xterm/VT conventions)
        _keyMappings[116] = "\x1b[15~"; // 116 = VK_F5
        _keyMappings[117] = "\x1b[17~"; // 117 = VK_F6
        _keyMappings[118] = "\x1b[18~"; // 118 = VK_F7
        _keyMappings[119] = "\x1b[19~"; // 119 = VK_F8
        _keyMappings[120] = "\x1b[20~"; // 120 = VK_F9
        _keyMappings[121] = "\x1b[21~"; // 121 = VK_F10
        _keyMappings[122] = "\x1b[23~"; // 122 = VK_F11
        _keyMappings[123] = "\x1b[24~"; // 123 = VK_F12

        // VT100 doesn't support modifier keys or extended keypad modes here;
        // base class will transform cursor keys in ApplicationCursorKeys mode.
    }
}

/// <summary>
/// VT220 keyboard mapper with extended terminal support.
/// Maps keys to VT220 escape sequences using integer key codes aligned to VK values.
/// </summary>
public class VT220KeyboardMapper : BaseKeyboardMapper
{
    protected override void InitializeMappings()
    {
        // Basic control keys (using VK-aligned key codes)
        _keyMappings[13] = "\r";      // 13 = VK_RETURN (Enter)
        _keyMappings[8] = "\x08";      // 8 = VK_BACK (Backspace)
        _keyMappings[9] = "\t";       // 9 = VK_TAB (Tab)
        _keyMappings[27] = "\x1b";     // 27 = VK_ESCAPE (Escape)

        // Navigation keys
        _keyMappings[38] = "\x1b[A";  // 38 = VK_UP (Up Arrow)
        _keyMappings[40] = "\x1b[B";  // 40 = VK_DOWN (Down Arrow)
        _keyMappings[39] = "\x1b[C";  // 39 = VK_RIGHT (Right Arrow)
        _keyMappings[37] = "\x1b[D";   // 37 = VK_LEFT (Left Arrow)
        _keyMappings[36] = "\x1b[H";   // 36 = VK_HOME
        _keyMappings[35] = "\x1b[F";   // 35 = VK_END

        // Page navigation (VT220 style)
        _keyMappings[33] = "\x1b[5~"; // 33 = VK_PRIOR (PageUp)
        _keyMappings[34] = "\x1b[6~"; // 34 = VK_NEXT (PageDown)

        // Edit keys (VT220 style)
        _keyMappings[45] = "\x1b[2~"; // 45 = VK_INSERT
        _keyMappings[46] = "\x1b[3~"; // 46 = VK_DELETE

        // Function keys F1-F4 (VT100 style)
        _keyMappings[112] = "\x1bOP"; // 112 = VK_F1
        _keyMappings[113] = "\x1bOQ"; // 113 = VK_F2
        _keyMappings[114] = "\x1bOR"; // 114 = VK_F3
        _keyMappings[115] = "\x1bOS"; // 115 = VK_F4

        // Function keys F5-F12 (VT220 style)
        _keyMappings[116] = "\x1b[15~"; // 116 = VK_F5
        _keyMappings[117] = "\x1b[17~"; // 117 = VK_F6
        _keyMappings[118] = "\x1b[18~"; // 118 = VK_F7
        _keyMappings[119] = "\x1b[19~"; // 119 = VK_F8
        _keyMappings[120] = "\x1b[20~"; // 120 = VK_F9
        _keyMappings[121] = "\x1b[21~"; // 121 = VK_F10
        _keyMappings[122] = "\x1b[23~"; // 122 = VK_F11
        _keyMappings[123] = "\x1b[24~"; // 123 = VK_F12
    }
}

/// <summary>
/// Unified TDV keyboard mapper for all TDV terminal models (TDV1200, TDV2215, TDV2200).
/// Uses TDV2200KeyRegistry as the single source of truth for TDV-native sequences.
/// NO VT220 fallback — all sequences come from the TDV spec.
///
/// Per keyboard-spec.md section 6.8:
/// - Extended Control Mode ON: function/editing/system keys send CSI nn _ sequences
/// - Fixed keys (arrows, Home, ESC, CR, LF, DEL) send C0 codes in ALL modes
/// - Extended Control Mode OFF (TDV-2115): function keys send C0 codes
///
/// Resolution order:
/// 1. User-configured key bindings → registry lookup for TDV-native sequence
/// 2. TDV2115 mode C0 codes for function keys (Extended Control Mode OFF)
/// 3. All TDV keys → registry (fixed keys=C0, function/editing=CSI nn _)
/// 4. Backspace (PC-only key, no TDV equivalent, sends BS=0x08)
/// 5. No fallback — keys without TDV equivalents return null
/// </summary>
public class TDV2200KeyboardMapper : BaseKeyboardMapper
{
    protected override void InitializeMappings()
    {
        // PC Backspace (VK 8): no physical TDV key maps to VK 8, but BS (0x08)
        // is the standard delete-backward character (same as TDV LEFT arrow C0 code)
        _keyMappings[8] = "\x08";

        // All other keys resolved via TDV2200KeyRegistry in MapKey():
        // - Arrows/Home/ESC/CR/LF/DEL → C0 codes (fixed keys, AlwaysSameCode)
        // - Tab, F1-F8, function/editing keys → CSI nn _ (Extended Control Mode)
        // - PageUp/Down/Insert/Delete → CSI nn _ (Extended Control Mode)
        // NO VT220 sequences — this is a TDV-native mapper
    }

    public override string? MapKey(int keyCode, KeyModifiers modifiers, TerminalModes terminalModes)
    {
        // 1. User-configured bindings (Alt+H → G53 HJELP, etc.)
        if (TDVKeyBindingConfiguration.Instance.TryGetTarget(keyCode, modifiers, out var target))
        {
            if (Emulators.TDV.TDV2200KeyRegistry.TryGetKey(target.GridPosition, out var kd)
                && (kd.Flags & Emulators.TDV.TDVKeyFlags.IsProgrammable) != 0)
            {
                // PUSH key — send stored programmed string
                var pushString = TDVPushKeyConfiguration.Instance
                    .GetKeyString(target.GridPosition, target.Shifted);
                return pushString; // null if not programmed
            }
            var seq = Emulators.TDV.TDV2200KeyRegistry.GetSequence(target.GridPosition,
                true, false, target.Shifted, false);
            if (seq != null) return seq;
        }

        // 1b. The universal Ctrl combinations: Ctrl+letter → C0, Ctrl+Space → NUL,
        //     Ctrl+Backspace → NUL. Shared with the VT mappers rather than repeated, and placed
        //     AFTER user bindings so a rebind still wins.
        //
        //     Ctrl+Space is the TDV2200 spacebar (A5) sending NUL with CTRL held (see
        //     TDV2200KeyVisualRegistry ctrlSequence); the registry lookup in step 3 cannot produce
        //     it because A5 has no ExtNormal sequence. Ctrl+letter matters here too: it used to be
        //     supplied by a fallback in the UI, so deleting that without this would have stopped
        //     Ctrl+C reaching a TDV host at all.
        var universalControl = MapUniversalControl(keyCode, modifiers);
        if (universalControl != null)
        {
            return universalControl;
        }

        // 2. TDV2115 mode: C0 codes for arrows/home (Extended Control Mode OFF)
        //
        // All five of these send the SAME byte in both modes (AlwaysSameCode), which is why they
        // are hardcoded here rather than read from the registry per keypress.
        //
        // HOME briefly disagreed with itself on 31 August 2026. A live test against a real ND-100
        // (terminal_localkey pressing HOME with TDV2115 mode confirmed on via a DECRQM round
        // trip) showed the app always sent GS regardless of mode, and TDV2200KeyRegistry's own
        // SimpleAscii for HOME was "\x10" at the time - so this switch was "fixed" to match it,
        // sending DLE here. That fix was itself wrong: reading spec\Keyboards\keyboard-spec.md
        // directly (section 6.8.3, citing the TDV-2200/9 User's Guide ND-30.003.04 EN) shows the
        // DLE reading was an OCR error in an early pass of section 7.2, corrected by a fresh OCR
        // of section 9.1 which marks HOME "is always" GS in both modes - the same conclusion its
        // own AlwaysSameCode flag already implied. The registry's SimpleAscii is corrected to
        // "\x1D" alongside this, so what follows is back to matching it.
        if (terminalModes.HasFlag(TerminalModes.TDV2115Mode) && modifiers == KeyModifiers.None)
        {
            // ASK THE REGISTRY, rather than the five-key list this used to be. Section 6.8.3 above
            // is a table of THIRTY-odd keys, and hardcoding the handful that happen to be
            // AlwaysSameCode meant every other one fell through to step 3 and sent its EXTENDED
            // sequence while the terminal was in 2115 mode: TAB sent ESC[16_ where the User's
            // Guide says HT 0x09, F1 sent ESC[50_ where it says RS 0x1E, and so on for twelve keys.
            //
            // Found 2 September 2026 by walking the whole registry through this mapper
            // (EveryReachableKeyAgreesWithTheRegistryTests). The registry already held the right
            // values - including the four that look wrong and are not: F5 sends "000", F6 "00",
            // F7 "0" and F8 "+", which §6.8.3 lists explicitly as multi-byte and NOT C0.
            var grid2115 = Emulators.TDV.TDV2200KeyRegistry.GetGridForVK(keyCode);
            if (grid2115 != null)
            {
                var simple = Emulators.TDV.TDV2200KeyRegistry.GetSequence(grid2115, false, false);
                if (!string.IsNullOrEmpty(simple)) return simple;
            }
        }

        // 3. All TDV keys → registry (Extended Control Mode ON)
        //    Fixed keys (arrows, Home, ESC, CR, DEL) → C0 codes (AlwaysSameCode)
        //    Function/editing keys → CSI nn _ sequences
        if (!modifiers.HasFlag(KeyModifiers.Alt))
        {
            var grid = Emulators.TDV.TDV2200KeyRegistry.GetGridForVK(keyCode);
            if (grid != null && Emulators.TDV.TDV2200KeyRegistry.TryGetKey(grid, out var def)
                && def.ExtNormal != null)
            {
                bool shift = modifiers.HasFlag(KeyModifiers.Shift);
                bool ctrl = modifiers.HasFlag(KeyModifiers.Ctrl);
                var regSeq = Emulators.TDV.TDV2200KeyRegistry.GetSequence(grid, true, false, shift, ctrl);
                if (regSeq != null) return regSeq;
            }
        }

        // 4. Backspace (PC-only key, not on TDV keyboard)
        if (_keyMappings.TryGetValue(keyCode, out var mapped))
            return mapped;

        // No VT220 fallback — keys without TDV equivalents return null
        return null;
    }
}

// Type aliases for backward compatibility — old names resolve to the unified mapper
public class TDV1200KeyboardMapper : TDV2200KeyboardMapper { }
public class TDV2215KeyboardMapper : TDV2200KeyboardMapper { }

/// <summary>
/// Factory for creating keyboard mappers based on terminal type
/// </summary>
public static class KeyboardMapperFactory
{
    /// <summary>
    /// The mapper for a terminal, chosen by its profile.
    ///
    /// This is the form to use. The string overload below keys off the emulator's CLASS NAME,
    /// which meant renaming a class silently downgraded that terminal's keyboard to VT100, and a
    /// terminal whose class name was not in the list got VT100 keys without anyone deciding so.
    /// A profile states which keyboard it types on.
    /// </summary>
    public static IKeyboardMapper CreateMapper(Profiles.TerminalProfile profile)
    {
        if (profile == null) throw new ArgumentNullException(nameof(profile));

        return CreateMapperForLayout(profile.KeyboardLayout)
            ?? throw new NotSupportedException(
                $"Terminal profile '{profile.Name}' asks for keyboard layout '{profile.KeyboardLayout}', " +
                "which has no mapper. Add one, or point the profile's KeyboardLayout at an existing layout.");
    }

    /// <summary>
    /// The mapper for a layout name, or null if there is none.
    ///
    /// Null rather than a VT100 fallback: "this terminal types on a VT100 keyboard" is a decision
    /// for a profile to state, not something for a lookup to assume when it recognises nothing.
    /// </summary>
    public static IKeyboardMapper? CreateMapperForLayout(string layout)
    {
        return layout switch
        {
            "VT100" => new VT100KeyboardMapper(),
            "VT220" => new VT220KeyboardMapper(),
            "TDV1200" => new TDV1200KeyboardMapper(),
            "TDV2215" => new TDV2215KeyboardMapper(),
            "TDV2200" => new TDV2200KeyboardMapper(),
            _ => null
        };
    }

    /// <summary>
    /// Legacy lookup by terminal-type or class name. Prefer the profile overload.
    ///
    /// Kept because tests and older call sites name terminals as strings. The VT100 fallback for an
    /// unrecognised name is retained here so those callers keep working, but it is no longer how
    /// any emulator gets its keyboard — the profile decides that now.
    /// </summary>
    public static IKeyboardMapper CreateMapper(string terminalType)
    {
        // Strip "Emulator" suffix if present (e.g., "TDV2200Emulator" -> "TDV2200")
        if (terminalType.EndsWith("Emulator"))
        {
            terminalType = terminalType.Substring(0, terminalType.Length - "Emulator".Length);
        }

        return CreateMapperForLayout(terminalType) ?? new VT100KeyboardMapper();
    }
}
