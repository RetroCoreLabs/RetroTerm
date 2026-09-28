using System;

namespace RetroTerm.Core.Terminal.Input
{
    /// <summary>
    /// Source key combination for a TDV key binding (e.g., Alt+H, F1, Shift+F1, Ctrl+A).
    /// Used as dictionary key — implements IEquatable for efficient lookup.
    /// </summary>
    public readonly struct KeyBindingSource : IEquatable<KeyBindingSource>
    {
        /// <summary>
        /// Windows Virtual Key code (e.g., 72 for 'H', 112 for F1, 9 for Tab)
        /// </summary>
        public readonly int VKCode;

        /// <summary>
        /// Modifier key flags (None, Shift, Ctrl, Alt, or combinations)
        /// </summary>
        public readonly KeyModifiers Modifiers;

        public KeyBindingSource(int vkCode, KeyModifiers modifiers)
        {
            VKCode = vkCode;
            Modifiers = modifiers;
        }

        public bool Equals(KeyBindingSource other)
        {
            return VKCode == other.VKCode && Modifiers == other.Modifiers;
        }

        public override bool Equals(object obj)
        {
            return obj is KeyBindingSource other && Equals(other);
        }

        public override int GetHashCode()
        {
            return (VKCode * 397) ^ (int)Modifiers;
        }

        public static bool operator ==(KeyBindingSource left, KeyBindingSource right) => left.Equals(right);
        public static bool operator !=(KeyBindingSource left, KeyBindingSource right) => !left.Equals(right);

        /// <summary>
        /// Validates whether this source is a legal binding source.
        /// Bare text-producing keys (letters, digits, punctuation without modifiers) are rejected.
        /// Modifier-only keys (Shift, Ctrl, Alt alone) are rejected.
        /// </summary>
        public bool IsValid()
        {
            return IsValidSource(VKCode, Modifiers);
        }

        /// <summary>
        /// Static validation: checks whether a VK code + modifier combination is a valid binding source.
        /// </summary>
        public static bool IsValidSource(int vkCode, KeyModifiers modifiers)
        {
            // Reject modifier-only keys:
            // Generic: Shift=16, Ctrl=17, Alt=18, LWin=91, RWin=92
            // Left/Right: LShift=160, RShift=161, LCtrl=162, RCtrl=163, LAlt=164, RAlt=165
            if (vkCode == 16 || vkCode == 17 || vkCode == 18 || vkCode == 91 || vkCode == 92)
                return false;
            if (vkCode >= 160 && vkCode <= 165)
                return false;

            // If any modifier is held, the combination is valid
            // (Shift+A, Ctrl+A, Alt+A, Ctrl+Shift+F1, etc.)
            if (modifiers != KeyModifiers.None)
                return true;

            // No modifiers — only special (non-text-producing) keys are valid
            return IsSpecialKey(vkCode);
        }

        /// <summary>
        /// Returns true if the VK code represents a bare modifier key (Shift, Ctrl, Alt, Win).
        /// These should never be treated as binding sources.
        /// </summary>
        public static bool IsModifierOnlyKey(int vkCode)
        {
            // Generic: Shift=16, Ctrl=17, Alt=18, LWin=91, RWin=92
            if (vkCode == 16 || vkCode == 17 || vkCode == 18 || vkCode == 91 || vkCode == 92)
                return true;
            // Left/Right: LShift=160, RShift=161, LCtrl=162, RCtrl=163, LAlt=164, RAlt=165
            if (vkCode >= 160 && vkCode <= 165)
                return true;
            return false;
        }

        /// <summary>
        /// Returns true for VK codes that don't produce text by themselves:
        /// F1-F24, arrows, Home/End, PageUp/PageDown, Insert, Delete, Tab, Escape.
        /// </summary>
        private static bool IsSpecialKey(int vkCode)
        {
            // F1-F24 (VK 112-135)
            if (vkCode >= 112 && vkCode <= 135)
                return true;

            // Arrow keys (37-40)
            if (vkCode >= 37 && vkCode <= 40)
                return true;

            // Navigation: PageUp(33), PageDown(34), End(35), Home(36)
            if (vkCode >= 33 && vkCode <= 36)
                return true;

            // Insert(45), Delete(46)
            if (vkCode == 45 || vkCode == 46)
                return true;

            // Backspace(8), Tab(9), Enter(13), Escape(27), Space(32)
            if (vkCode == 8 || vkCode == 9 || vkCode == 13 || vkCode == 27 || vkCode == 32)
                return true;

            return false;
        }
    }

    /// <summary>
    /// Target TDV key for a key binding.
    /// </summary>
    public readonly struct KeyBindingTarget
    {
        /// <summary>
        /// TDV keyboard grid position (e.g., "G53" for HJELP, "F51" for F1)
        /// </summary>
        public readonly string GridPosition;

        /// <summary>
        /// Whether to send the shifted variant of the target key
        /// </summary>
        public readonly bool Shifted;

        public KeyBindingTarget(string gridPosition, bool shifted = false)
        {
            GridPosition = gridPosition;
            Shifted = shifted;
        }
    }
}
