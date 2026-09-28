using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using RetroTerm.Core.Terminal.Emulators.TDV;
using RetroTerm.Desktop.Models;

namespace RetroTerm.Desktop.ViewModels
{
    /// <summary>
    /// ViewModel for the virtual keyboard panel
    /// </summary>
    public class VirtualKeyboardViewModel : INotifyPropertyChanged
    {
        private KeyboardLayout _layout;
        private NationalKeyboardLayout _selectedLayout = NationalKeyboardLayout.Norwegian;
        private HashSet<string> _pressedKeys = new HashSet<string>();
        private HashSet<string> _modifierStates = new HashSet<string>();
        private HashSet<string> _stickyModifiers = new HashSet<string>();
        private Dictionary<string, bool> _ledStates = new Dictionary<string, bool>();
        private bool _capsLockActive;
        private bool _lockActive;

        public event PropertyChangedEventHandler? PropertyChanged;
        public event EventHandler<KeyPressedEventArgs>? KeyPressed;
        public event EventHandler<KeyReleasedEventArgs>? KeyReleased;

        /// <summary>
        /// Fired when sticky modifiers are auto-released after a non-modifier key is pressed.
        /// The Panel uses this to reset the visual state of the released modifier keys.
        /// </summary>
        public event EventHandler<StickyModifiersReleasedEventArgs>? StickyModifiersReleased;

        public VirtualKeyboardViewModel()
        {
            // Initialize layout
            _layout = TDV2200KeyLayout.CreateLayout();

            // Initialize LED states
            InitializeLEDStates();
        }

        /// <summary>
        /// Current keyboard layout
        /// </summary>
        public KeyboardLayout Layout
        {
            get => _layout;
            set
            {
                if (_layout != value)
                {
                    _layout = value;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        /// Selected national keyboard layout
        /// </summary>
        public NationalKeyboardLayout SelectedLayout
        {
            get => _selectedLayout;
            set
            {
                if (_selectedLayout != value)
                {
                    _selectedLayout = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(CurrentLanguageCode));
                }
            }
        }

        /// <summary>
        /// Current language code based on selected layout
        /// </summary>
        public string CurrentLanguageCode => KeyboardLayout.GetLanguageCode(_selectedLayout);

        /// <summary>
        /// Check if a key is currently pressed
        /// </summary>
        public bool IsKeyPressed(string gridPosition)
        {
            return _pressedKeys.Contains(gridPosition);
        }

        /// <summary>
        /// Check if a modifier is active (held or sticky)
        /// </summary>
        public bool IsModifierActive(string modifierName)
        {
            return _modifierStates.Contains(modifierName);
        }

        /// <summary>
        /// Check if a modifier is in sticky (latched) state
        /// </summary>
        public bool IsStickyModifier(string gridPosition)
        {
            return _stickyModifiers.Contains(gridPosition);
        }

        /// <summary>
        /// Release a sticky modifier (e.g. when the physical key is pressed while virtual is sticky)
        /// </summary>
        public void ReleaseStickyModifier(string gridPosition)
        {
            _stickyModifiers.Remove(gridPosition);
            _modifierStates.Remove(gridPosition);
            _pressedKeys.Remove(gridPosition);
            OnPropertyChanged(nameof(IsKeyPressed));
            OnPropertyChanged(nameof(IsModifierActive));
        }

        /// <summary>
        /// Whether the CAPS lock toggle is active
        /// </summary>
        public bool IsCapsLockActive
        {
            get => _capsLockActive;
            private set
            {
                if (_capsLockActive != value)
                {
                    _capsLockActive = value;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        /// Whether the LOCK (shift lock) toggle is active.
        /// When active, function/editing/navigation keys behave as if Shift is held.
        /// Does NOT affect normal character keys or numpad.
        /// </summary>
        public bool IsLockActive
        {
            get => _lockActive;
            private set
            {
                if (_lockActive != value)
                {
                    _lockActive = value;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        /// Get LED state
        /// </summary>
        public bool GetLEDState(string ledName)
        {
            return _ledStates.TryGetValue(ledName, out bool state) && state;
        }

        /// <summary>
        /// Set LED state
        /// </summary>
        public void SetLEDState(string ledName, bool state)
        {
            if (_ledStates.ContainsKey(ledName))
            {
                _ledStates[ledName] = state;
                OnPropertyChanged(nameof(LEDStates));
            }
        }

        /// <summary>
        /// All LED states for binding
        /// </summary>
        public Dictionary<string, bool> LEDStates => _ledStates;

        /// <summary>
        /// Handle key press from UI (mouse click on virtual keyboard)
        /// </summary>
        public void HandleKeyDown(TDVKeyVisualMetadata? key)
        {
            if (key == null) return;

            bool isModifier = key.Category == KeyCategory.Modifier;
            bool isToggle = key.Category == KeyCategory.Toggle;

            // Handle modifier keys: toggle sticky state
            if (isModifier)
            {
                if (_stickyModifiers.Contains(key.GridPosition))
                {
                    // Already sticky — un-stick
                    _stickyModifiers.Remove(key.GridPosition);
                    _modifierStates.Remove(key.GridPosition);
                    _pressedKeys.Remove(key.GridPosition);
                }
                else
                {
                    // Make sticky
                    _stickyModifiers.Add(key.GridPosition);
                    _modifierStates.Add(key.GridPosition);
                    _pressedKeys.Add(key.GridPosition);
                }

                OnPropertyChanged(nameof(IsKeyPressed));
                OnPropertyChanged(nameof(IsModifierActive));
                return;
            }

            // Handle toggle keys (CAPS E0, LOCK C0)
            if (isToggle)
            {
                _pressedKeys.Add(key.GridPosition);

                if (key.GridPosition == "E0") // CAPS
                    IsCapsLockActive = !IsCapsLockActive;
                else if (key.GridPosition == "C0") // LOCK (shift lock)
                    IsLockActive = !IsLockActive;

                // Toggle modifier state for visual tracking
                if (_modifierStates.Contains(key.GridPosition))
                    _modifierStates.Remove(key.GridPosition);
                else
                    _modifierStates.Add(key.GridPosition);

                OnPropertyChanged(nameof(IsKeyPressed));
                OnPropertyChanged(nameof(IsModifierActive));
                return;
            }

            // Non-modifier key: fire KeyPressed with current modifiers (including sticky)
            _pressedKeys.Add(key.GridPosition);
            KeyPressed?.Invoke(this, new KeyPressedEventArgs(key, GetCurrentModifiers()));

            // Auto-release sticky modifiers after the key combo is sent
            if (_stickyModifiers.Count > 0)
            {
                var released = new string[_stickyModifiers.Count];
                _stickyModifiers.CopyTo(released);
                _stickyModifiers.Clear();
                for (int i = 0; i < released.Length; i++)
                {
                    _modifierStates.Remove(released[i]);
                    _pressedKeys.Remove(released[i]);
                }
                StickyModifiersReleased?.Invoke(this, new StickyModifiersReleasedEventArgs(released));
            }

            OnPropertyChanged(nameof(IsKeyPressed));
            OnPropertyChanged(nameof(IsModifierActive));
        }

        /// <summary>
        /// Handle key release from UI (mouse button released)
        /// </summary>
        public void HandleKeyUp(TDVKeyVisualMetadata? key)
        {
            if (key == null) return;

            bool isModifier = key.Category == KeyCategory.Modifier;
            bool isToggle = key.Category == KeyCategory.Toggle;

            // Sticky modifiers stay pressed — don't release on mouse-up
            if (isModifier && _stickyModifiers.Contains(key.GridPosition))
                return;

            // Update pressed state
            _pressedKeys.Remove(key.GridPosition);

            // Handle non-toggle modifiers (non-sticky path — e.g. physical keyboard release)
            if (isModifier)
            {
                _modifierStates.Remove(key.GridPosition);
            }

            // Raise event
            KeyReleased?.Invoke(this, new KeyReleasedEventArgs(key));

            OnPropertyChanged(nameof(IsKeyPressed));
            OnPropertyChanged(nameof(IsModifierActive));
        }

        /// <summary>
        /// Mirror physical keyboard input (highlight key when typing)
        /// </summary>
        public void MirrorPhysicalKeyPress(int virtualKeyCode)
        {
            // Find key with matching VK code from visual registry
            var allKeys = TDV2200KeyVisualRegistry.GetAllKeys();
            for (int i = 0; i < allKeys.Length; i++)
            {
                if (allKeys[i].VirtualKeyCode == virtualKeyCode)
                {
                    _pressedKeys.Add(allKeys[i].GridPosition);
                    OnPropertyChanged(nameof(IsKeyPressed));
                    return;
                }
            }
        }

        /// <summary>
        /// Mirror physical key release
        /// </summary>
        public void MirrorPhysicalKeyRelease(int virtualKeyCode)
        {
            var allKeys = TDV2200KeyVisualRegistry.GetAllKeys();
            for (int i = 0; i < allKeys.Length; i++)
            {
                if (allKeys[i].VirtualKeyCode == virtualKeyCode)
                {
                    _pressedKeys.Remove(allKeys[i].GridPosition);
                    OnPropertyChanged(nameof(IsKeyPressed));
                    return;
                }
            }
        }

        /// <summary>
        /// Physical keyboard modifier state (set from pointer events before HandleKeyDown).
        /// Combined with virtual sticky modifiers in GetCurrentModifiers().
        /// </summary>
        public RetroTerm.Core.Terminal.Input.KeyModifiers PhysicalModifiers { get; set; }

        /// <summary>
        /// Get current modifier state flags (physical + virtual sticky combined)
        /// </summary>
        private RetroTerm.Core.Terminal.Input.KeyModifiers GetCurrentModifiers()
        {
            var modifiers = PhysicalModifiers;

            // OR in virtual sticky modifiers
            // Shift: B99 = Left Shift, B11 = Right Shift
            if (_modifierStates.Contains("B99") || _modifierStates.Contains("B11"))
                modifiers |= RetroTerm.Core.Terminal.Input.KeyModifiers.Shift;

            // Ctrl: D0 = CTRL key
            if (_modifierStates.Contains("D0"))
                modifiers |= RetroTerm.Core.Terminal.Input.KeyModifiers.Ctrl;

            return modifiers;
        }

        /// <summary>
        /// Initialize LED states
        /// </summary>
        private void InitializeLEDStates()
        {
            _ledStates["CAPS"] = false;
            _ledStates["LINE"] = false;
            _ledStates["WAIT"] = false;
            _ledStates["ERROR"] = false;
            _ledStates["ON"] = true;    // Power indicator, usually always on
            _ledStates["APP"] = false;  // Application mode
            _ledStates["BUSY"] = false;
            _ledStates["MSG"] = false;
            _ledStates["CAR"] = false;  // Carriage return
        }

        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    /// <summary>
    /// Event args for key pressed event
    /// </summary>
    public class KeyPressedEventArgs : EventArgs
    {
        public TDVKeyVisualMetadata Key { get; }
        public RetroTerm.Core.Terminal.Input.KeyModifiers Modifiers { get; }

        public KeyPressedEventArgs(TDVKeyVisualMetadata key, RetroTerm.Core.Terminal.Input.KeyModifiers modifiers)
        {
            Key = key;
            Modifiers = modifiers;
        }
    }

    /// <summary>
    /// Event args for key released event
    /// </summary>
    public class KeyReleasedEventArgs : EventArgs
    {
        public TDVKeyVisualMetadata Key { get; }

        public KeyReleasedEventArgs(TDVKeyVisualMetadata key)
        {
            Key = key;
        }
    }

    /// <summary>
    /// Event args when sticky modifiers auto-release after a key combo is sent
    /// </summary>
    public class StickyModifiersReleasedEventArgs : EventArgs
    {
        public string[] ReleasedGridPositions { get; }

        public StickyModifiersReleasedEventArgs(string[] releasedGridPositions)
        {
            ReleasedGridPositions = releasedGridPositions;
        }
    }
}
