using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using RetroTerm.Core.Protocols;
using RetroTerm.Core.Terminal.Emulators.TDV;
using RetroTerm.Core.Terminal.Input;
using RetroTerm.Desktop.Controls;
using RetroTerm.Desktop.Helpers;

namespace RetroTerm.Desktop.Views
{
    public partial class VirtualKeyboardWindow : Window
    {
        private VirtualKeyboardPanel? _virtualKeyboard;

        /// <summary>
        /// The keyboard panel inside this window. Test seam.
        /// </summary>
        internal VirtualKeyboardPanel? PanelForTesting => _virtualKeyboard;
        private IKeyboardMapper? _keyboardMapper;

        public TerminalModes CurrentTerminalModes { get; set; } = TerminalModes.None;

        public event EventHandler<string>? InputReceived;

        /// <summary>
        /// Raised when the keyboard layout changes. Carries the ISO 646 language code.
        /// </summary>
        public event EventHandler<string>? LayoutChanged;

        public VirtualKeyboardWindow()
        {
            InitializeComponent();
#if DEBUG
            this.AttachDevTools();
#endif
            // Handle physical keyboard input when this window has focus
            KeyDown += OnKeyDown;
            KeyUp += OnKeyUp;
            TextInput += OnTextInput;
        }

        /// <summary>
        /// Set the keyboard mapper for handling physical keyboard input
        /// </summary>
        public void SetKeyboardMapper(IKeyboardMapper? mapper)
        {
            _keyboardMapper = mapper;
        }

        /// <summary>
        /// Mirror a physical key event from another window (e.g. TerminalCanvas)
        /// so the virtual keyboard highlights the pressed key.
        /// </summary>
        public void MirrorPhysicalKey(int vkCode, Core.Terminal.Input.KeyModifiers modifiers, bool pressed)
        {
            _virtualKeyboard?.MirrorKeyEvent(vkCode, modifiers, pressed);
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);

            // Get reference to virtual keyboard panel
            _virtualKeyboard = this.FindControl<VirtualKeyboardPanel>("VirtualKeyboard");

            if (_virtualKeyboard != null)
            {
                _virtualKeyboard.InputReceived += OnVirtualKeyboardInput;
                _virtualKeyboard.PushKeyRightClicked += OnPushKeyRightClicked;
                _virtualKeyboard.LayoutChanged += (s, lang) => LayoutChanged?.Invoke(this, lang);
            }

            // Handle window closing to unsubscribe
            this.Closing += OnWindowClosing;
        }

        /// <summary>
        /// Handle physical keyboard input - routes ALL keys through the mapper
        /// and supports key capture mode for binding assignment.
        /// </summary>
        private void OnKeyDown(object? sender, KeyEventArgs e)
        {
            // 1. If in key capture mode (binding popup), intercept for binding assignment
            if (_virtualKeyboard != null && _virtualKeyboard.IsCapturingKey)
            {
                _virtualKeyboard.HandleCapturedKey(e);
                e.Handled = true;
                return;
            }

            // 1b. Escape with the binding popup up and no capture running closes the popup.
            //     Before 27 September 2026 nothing did, and Escape went on to the host as ESC
            //     while the popup stayed - "the right click got stuck in a popup".
            if (e.Key == Key.Escape && _virtualKeyboard != null && _virtualKeyboard.IsBindingPopupOpen)
            {
                _virtualKeyboard.CloseBindingPopup();
                e.Handled = true;
                return;
            }

            if (_keyboardMapper == null) return;

            // 2. Mirror bare modifier presses on virtual keyboard for visual feedback
            if (e.Key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
                or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin)
            {
                var modVkCode = AvaloniaKeyHelper.ToVKCode(e.Key);
                _virtualKeyboard?.MirrorKeyEvent(modVkCode,
                    Core.Terminal.Input.KeyModifiers.None, true);
                return;
            }

            // NOTE: Ctrl+Backspace → NUL used to be intercepted here as well as in TerminalCanvas.
            // The mapper resolves it now (MapUniversalControl), so both windows get it from one
            // place instead of from two copies that could drift.

            // 3. Convert Avalonia key to VK code and modifiers
            var vkCode = AvaloniaKeyHelper.ToVKCode(e.Key);
            var modifiers = AvaloniaKeyHelper.ConvertModifiers(e.KeyModifiers);

            // 4. Route through mapper — handles Alt+key, Ctrl+key, F-keys, arrows, etc.
            bool hasAlt = e.KeyModifiers.HasFlag(Avalonia.Input.KeyModifiers.Alt);
            bool hasCtrl = e.KeyModifiers.HasFlag(Avalonia.Input.KeyModifiers.Control);
            bool isSpecial = AvaloniaKeyHelper.IsSpecialKey(e.Key);

            if (hasAlt || hasCtrl || isSpecial)
            {
                var text = _keyboardMapper.MapKey(vkCode, modifiers, CurrentTerminalModes);
                if (text != null)
                {
                    InputReceived?.Invoke(this, text);
                    _virtualKeyboard?.MirrorKeyEvent(vkCode, modifiers, true);
                    e.Handled = true;
                    return;
                }
            }

            // NOTE: the Ctrl+letter → C0 and Ctrl+Space → NUL fallbacks used to be repeated here,
            // duplicating TerminalCanvas. Both now come out of the Core mapper above, so this
            // window and the terminal canvas cannot encode the same keypress differently.

            if (e.KeyModifiers == Avalonia.Input.KeyModifiers.Control && e.Key == Key.OemBackslash)
            {
                InputReceived?.Invoke(this, "\x1c");
                e.Handled = true;
                return;
            }

            if (e.KeyModifiers == Avalonia.Input.KeyModifiers.Control && e.Key == Key.OemCloseBrackets)
            {
                InputReceived?.Invoke(this, "\x1d");
                e.Handled = true;
                return;
            }

            if (e.KeyModifiers.HasFlag(Avalonia.Input.KeyModifiers.Control) && e.Key == Key.D6)
            {
                InputReceived?.Invoke(this, "\x1e");
                e.Handled = true;
                return;
            }

            if (e.KeyModifiers.HasFlag(Avalonia.Input.KeyModifiers.Control) && e.Key == Key.OemMinus)
            {
                InputReceived?.Invoke(this, "\x1f");
                e.Handled = true;
                return;
            }

            // 6. Mirror key press on virtual keyboard for visual feedback
            _virtualKeyboard?.MirrorKeyEvent(vkCode, modifiers, true);

            // 7. Regular characters fall through to TextInput handler
            //    (VirtualKeyboardPanel.Focusable = true ensures TextInput fires)
        }

        /// <summary>
        /// Handle text input for regular characters (letters, numbers, symbols).
        /// This fires after OnKeyDown for keys that produce text output.
        /// </summary>
        private void OnTextInput(object? sender, TextInputEventArgs e)
        {
            // Don't send text when in key capture mode
            if (_virtualKeyboard != null && _virtualKeyboard.IsCapturingKey)
                return;

            if (!string.IsNullOrEmpty(e.Text))
            {
                // Avoid duplicate Enter/LF — already handled by mapper in OnKeyDown
                if (e.Text == "\r" || e.Text == "\n")
                    return;

                // ISO 646 conversion: national characters (ÆØÅæøå etc.) → ASCII wire bytes
                // The TDV emulator renders bytes through the active NRC, so we must send
                // the ASCII position byte (e.g. ø → '|', Æ → '[', Å → ']')
                // Conversion lives in Core now. This site used to have its own loop that only
                // handled input exactly one character long, so a multi-character paste or IME
                // commit went out unconverted while the same text typed into the terminal canvas
                // was converted correctly.
                string text = Core.Terminal.Emulators.TDV.TDVCharacterSets
                    .ConvertToWireBytes(e.Text, _virtualKeyboard?.CurrentLanguageCode);

                InputReceived?.Invoke(this, text);

                // Mirror the character on virtual keyboard
                if (text.Length == 1)
                {
                    var ch = char.ToUpper(text[0]);
                    if ((ch >= 'A' && ch <= 'Z') || (ch >= '0' && ch <= '9'))
                    {
                        var charVk = (int)ch;
                        _virtualKeyboard?.MirrorKeyEvent(charVk, RetroTerm.Core.Terminal.Input.KeyModifiers.None, true);
                    }
                }

                e.Handled = true;
            }
        }

        /// <summary>
        /// Handle key release for visual feedback on virtual keyboard.
        /// During capture mode, notifies the panel so modifier release can reset status text.
        /// </summary>
        private void OnKeyUp(object? sender, KeyEventArgs e)
        {
            // During capture mode, notify panel of key release for modifier tracking
            if (_virtualKeyboard != null && _virtualKeyboard.IsCapturingKey)
            {
                _virtualKeyboard.HandleCapturedKeyUp(e);
                e.Handled = true;
                return;
            }

            var vkCode = AvaloniaKeyHelper.ToVKCode(e.Key);
            var modifiers = AvaloniaKeyHelper.ConvertModifiers(e.KeyModifiers);
            _virtualKeyboard?.MirrorKeyEvent(vkCode, modifiers, false);
        }

        private void OnVirtualKeyboardInput(object? sender, string input)
        {
            // Forward input event to parent
            InputReceived?.Invoke(this, input);
        }

        private void OnPushKeyRightClicked(object? sender, int gridNum)
        {
            // Open programming dialog pre-selected to the clicked key
            // gridNum is 1-8 (unshifted P1-P8), same as key number
            var dialog = new ProgrammableKeysDialog();
            dialog.TestKeyRequested += (s, byteString) =>
            {
                InputReceived?.Invoke(this, byteString);
            };
            dialog.Show();
            dialog.SelectKey(gridNum);
        }

        /// <summary>
        /// Set to true when the application is shutting down so the window actually closes.
        /// </summary>
        public bool AllowClose { get; set; }

        private void OnWindowClosing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            if (AllowClose)
                return; // Let the window close for real

            // Normal user close → just hide the window
            e.Cancel = true;
            this.Hide();
        }

        /// <summary>
        /// Show the virtual keyboard window
        /// </summary>
        public new void Show()
        {
            base.Show();
            this.Activate();
        }

        /// <summary>
        /// Programmatically set the keyboard layout to match the active terminal's language.
        /// </summary>
        public void SetLayout(string languageName)
        {
            _virtualKeyboard?.SetLayout(languageName);
        }

        /// <summary>
        /// Set LED state on the virtual keyboard
        /// </summary>
        public void SetLEDState(string ledName, bool state)
        {
            _virtualKeyboard?.SetLEDState(ledName, state);
        }

        #region Indicator lamps

        // The emulator whose lamps this window mirrors, or null when the active tab is not a
        // TDV terminal (VT100 etc. have no such lamps).
        private TDVEmulatorBase? _ledSource;

        // Drives the L4 message lamp when the host has requested BLINK (NDBLED).
        // Only runs while something is actually blinking.
        private DispatcherTimer? _blinkTimer;
        private bool _blinkPhase;

        /// <summary>
        /// The four message lamps, in parameter order: EXPAND, APPEND, BUSY, MESSAGE.
        /// </summary>
        private readonly TDVMessageLampState[] _lampStates = new TDVMessageLampState[4];

        /// <summary>
        /// Lamps held on by 2115-compatible operation's C0 codes, whatever the message state says.
        /// </summary>
        private readonly bool[] _lampForcedOn = new bool[4];

        /// <summary>
        /// Mirrors the indicator lamps of the given TDV emulator on the keyboard panel.
        /// </summary>
        /// <param name="emulator">
        /// The emulator to follow, or null to stop mirroring (non-TDV terminal, or no tab).
        /// </param>
        public void AttachLedSource(TDVEmulatorBase? emulator)
        {
            if (ReferenceEquals(_ledSource, emulator))
            {
                RefreshLamps();
                return;
            }

            if (_ledSource != null)
                _ledSource.LedStateChanged -= OnEmulatorLedStateChanged;

            _ledSource = emulator;

            if (_ledSource != null)
                _ledSource.LedStateChanged += OnEmulatorLedStateChanged;

            RefreshLamps();
        }

        /// <summary>
        /// Updates the modem-style status lamps from the connection state.
        /// </summary>
        /// <param name="status">
        /// Current connection status of the active session.
        /// </param>
        /// <remarks>
        /// LINE/CAR/WAIT/ERROR are terminal-local hardware lamps on real TDV hardware - they
        /// reflect the line, not anything the host sends - so they are driven from the
        /// connection rather than from the data stream. ON is a power lamp and stays lit.
        /// </remarks>
        public void UpdateConnectionLamps(ConnectionStatus status)
        {
            bool connected = status == ConnectionStatus.Connected;
            bool connecting = status == ConnectionStatus.Connecting;
            bool error = status == ConnectionStatus.Error;

            SetLEDState("LINE", connected);
            SetLEDState("CAR", connected);
            SetLEDState("WAIT", connecting);
            SetLEDState("ERROR", error);
        }

        // Lamp changes are raised on whatever thread processed the host data, which is a
        // network callback thread - marshal before touching Avalonia controls.
        private void OnEmulatorLedStateChanged()
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(RefreshLamps);
        }

        private void RefreshLamps()
        {
            if (_virtualKeyboard == null)
                return;

            if (_ledSource == null)
            {
                SetLEDState("L1", false);
                SetLEDState("L2", false);
                SetLEDState("L3", false);
                SetLEDState("L4", false);
                StopBlink();
                return;
            }

            // FOUR lamps, from TWO sources, and a lamp is on if either says so.
            //
            // The message lamps are the host's: NDSLED, NDBLED and NDCLED, ND-1200 sections 5.52,
            // 5.37 and 5.38, numbered 1 EXPAND, 2 APPEND, 3 BUSY, 4 MESSAGE. Each is off, lit or
            // blinking.
            //
            // The keyboard lamps are 2115-compatible operation's: ENQ and ACK light lights 1 and 2
            // (TDV 2215 section 3.1), NAK lights the third and SYN clears them. They almost
            // certainly ARE the same physical lamps 1 to 3, but no manual held here says so
            // outright, so the two states are kept apart and merged only here, for drawing.
            //
            // Until 11 September 2026 the message side was three flags named after the three
            // OPERATIONS, and this method had to guess what a host meant: "Blink wins over Set, and
            // an explicit Clear with nothing else asserted means off". There is nothing to guess
            // now.
            var lights = _ledSource.KeyboardLights;
            var message = _ledSource.MessageLEDState;

            _lampStates[0] = message.Expand;
            _lampStates[1] = message.Append;
            _lampStates[2] = message.Busy;
            _lampStates[3] = message.Message;

            _lampForcedOn[0] = lights.L1;
            _lampForcedOn[1] = lights.L2;
            _lampForcedOn[2] = lights.L3;
            _lampForcedOn[3] = false;

            bool anyBlinking = false;
            for (int i = 0; i < _lampStates.Length; i++)
            {
                if (_lampStates[i] == TDVMessageLampState.Blinking)
                {
                    anyBlinking = true;
                    break;
                }
            }

            if (anyBlinking)
            {
                StartBlink();
            }
            else
            {
                StopBlink();
            }

            PaintLamps();
        }

        /// <summary>
        /// Draws the four lamps from the merged state and the current blink phase.
        /// </summary>
        private void PaintLamps()
        {
            for (int i = 0; i < _lampStates.Length; i++)
            {
                bool on = _lampForcedOn[i]
                    || _lampStates[i] == TDVMessageLampState.Lit
                    || (_lampStates[i] == TDVMessageLampState.Blinking && _blinkPhase);

                SetLEDState("L" + (i + 1).ToString(), on);
            }
        }

        private void StartBlink()
        {
            if (_blinkTimer != null)
                return;

            // ~1.5 Hz: on for 333 ms, off for 333 ms.
            _blinkTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(333)
            };

            // Any of the four lamps can blink, so the tick repaints all of them rather than one.
            _blinkTimer.Tick += (_, _) =>
            {
                _blinkPhase = !_blinkPhase;
                PaintLamps();
            };

            _blinkPhase = true;
            _blinkTimer.Start();
        }

        private void StopBlink()
        {
            if (_blinkTimer == null)
                return;

            _blinkTimer.Stop();
            _blinkTimer = null;
            _blinkPhase = false;
        }

        /// <inheritdoc/>
        protected override void OnClosed(EventArgs e)
        {
            // Leaving this subscribed would keep the window alive for the emulator's lifetime.
            if (_ledSource != null)
            {
                _ledSource.LedStateChanged -= OnEmulatorLedStateChanged;
                _ledSource = null;
            }

            StopBlink();
            base.OnClosed(e);
        }

        #endregion

        /// <summary>
        /// Mirror physical keyboard input
        /// </summary>
        public void MirrorPhysicalKey(int virtualKeyCode, bool pressed)
        {
            _virtualKeyboard?.MirrorPhysicalKey(virtualKeyCode, pressed);
        }
    }
}
