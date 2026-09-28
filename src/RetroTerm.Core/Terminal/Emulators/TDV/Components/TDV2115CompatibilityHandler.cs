using RetroTerm.Core.Terminal.Buffer;
using RetroTerm.Core.Terminal.Rendering;

namespace RetroTerm.Core.Terminal.Emulators.TDV.Components;

/// <summary>
/// Handles TDV 2115 compatibility mode features:
/// - C0 control codes (21 codes for basic terminal operations)
/// - Video control (on/off)
/// - LED control (3 LEDs)
/// - Screen operations (erase line/page)
/// - Cursor movement
/// - DLE (Direct Line Entry) binary cursor positioning
/// </summary>
public class TDV2115CompatibilityHandler
{
    private readonly TDVEmulatorBase _emulator;
    private bool _is2115CompatibilityMode = false;
    private bool _videoOn = true;
    private bool[] _leds = new bool[3]; // LEDs 1-3
    private bool _isDLEMode = false;
    private int _dleByteCount = 0;
    private int _dleLine = 0;
    private int _dleColumn = 0;

    public TDV2115CompatibilityHandler(TDVEmulatorBase emulator)
    {
        _emulator = emulator ?? throw new ArgumentNullException(nameof(emulator));
    }

    /// <summary>
    /// Gets whether 2115 compatibility mode is active
    /// </summary>
    public bool Is2115CompatibilityMode => _is2115CompatibilityMode;

    /// <summary>
    /// Gets whether video is on
    /// </summary>
    public bool VideoOn => _videoOn;

    /// <summary>
    /// Gets the LED states (3 LEDs)
    /// </summary>
    public bool[] Leds => _leds;

    /// <summary>
    /// Gets whether DLE mode is active
    /// </summary>
    public bool IsDLEMode => _isDLEMode;

    /// <summary>
    /// Current state of the three keyboard indicator lamps (L1, L2, L3).
    /// </summary>
    public (bool L1, bool L2, bool L3) Lamps => (_leds[0], _leds[1], _leds[2]);

    /// <summary>
    /// Sets one lamp and notifies the emulator only when the state actually changes,
    /// so a host that re-asserts lamps on every refresh does not spam the UI thread.
    /// </summary>
    /// <param name="index">
    /// Lamp index, 0-2.
    /// </param>
    /// <param name="state">
    /// True to light the lamp.
    /// </param>
    private void SetLamp(int index, bool state)
    {
        if (_leds[index] == state)
            return;

        _leds[index] = state;
        _emulator.RaiseLedStateChanged();
    }

    /// <summary>
    /// Turns all three keyboard lamps off (SYN).
    /// </summary>
    private void ClearLamps()
    {
        if (!_leds[0] && !_leds[1] && !_leds[2])
            return;

        _leds[0] = false;
        _leds[1] = false;
        _leds[2] = false;
        _emulator.RaiseLedStateChanged();
    }

    /// <summary>
    /// Enable or disable 2115 compatibility mode
    /// </summary>
    public void Set2115CompatibilityMode(bool enable)
    {
        _is2115CompatibilityMode = enable;
    }

    /// <summary>
    /// Whether a C0 code carries its TDV meaning in NATIVE mode (not just 2115 mode).
    /// </summary>
    /// <param name="b">
    /// The control byte.
    /// </param>
    /// <returns>
    /// True if the code should be handled even when 2115 mode is off.
    /// </returns>
    /// <remarks>
    /// <para><b>Where this comes from</b></para>
    /// Two sources, and they agree:
    ///  - The TDV 2215 manual, <c>spec\TDV2115\TDV2115.md</c> section 8.4 "Accepted Codes in the
    ///    C0-set". It lists the whole set and marks exactly two entries "Affected by the EC
    ///    switch" - HT and ESC. Everything else is accepted whether extended control is on or off.
    ///    Compare section 3.1, which lists what a terminal accepts with EC OFF: the same set minus
    ///    those two. So turning extended control on ADDS escape sequences; it does not take the C0
    ///    meanings away.
    ///  - <c>docs\TDV-COMPREHENSIVE-REFERENCE.md</c>, the table headed "Standard ISO 6429 C0
    ///    Codes - All Models", which says the same thing and adds "Cursor Movement: uses C0 codes
    ///    (0x08, 0x0B, 0x18, 0x1C, 0x1D), NOT ESC sequences".
    ///
    /// <para><b>What changed, and what it replaced</b></para>
    /// Only the four lamp codes used to be live in native mode. A comment here recorded that the
    /// documentation and a test disagreed and left it alone. Going back to the manual settled it:
    /// the test asserting that CAN must not move the cursor outside 2115 mode carried no citation,
    /// and both documents say it does. The keyboard is the third witness - a TDV2200's arrow keys
    /// send these very bytes in every mode, so a host echoing them back has to move the cursor.
    ///
    /// <para><b>Still excluded, on purpose</b></para>
    ///  - 0x0E SO / 0x0F SI. Here they mean Underline / Normal, a 2115 attribute feature. Outside
    ///    2115 mode they are Shift Out / Shift In - G1/G0 invocation - which the emulator already
    ///    handles. Routing them here would silently break character-set switching.
    ///  - 0x02 STX / 0x03 ETX (video off/on) and 0x04 EOT (erase line). The 2215 manual lists them
    ///    without an EC gate, but the All Models table does not carry them at all. One source
    ///    short of agreement, so they stay 2115-only rather than being enabled on a guess.
    ///  - 0x0C FF. Documented as ROLL UP with the cursor left where it is, which is not what a
    ///    form feed does in the base emulator. Changing it is a separate question from cursor
    ///    movement and is not smuggled in with it.
    ///  - 0x08 BS, 0x0A LF, 0x0D CR - already correct in the base emulator with the same meaning.
    ///    Handling them here would add a video-on gate the base layer does not have.
    ///
    /// <para><b>The TDV2200 keeps its graphics</b></para>
    /// GS and FS are on this list as cursor home and cursor up, and on a TDV2200 they are also the
    /// Tektronix graph-mode and point-plot codes. Nothing had to be special-cased for that: the
    /// graphics side gets first refusal on every byte and takes those two unconditionally, so they
    /// never reach here on a 2200. They reach here on a 1200 or a 2215, which have no graphics.
    /// </remarks>
    private static bool IsNativeModeControlCode(byte b)
    {
        switch (b)
        {
            // Keyboard indicator lamps.
            case 0x05: // ENQ - Light 1 on
            case 0x06: // ACK - Light 2 on
            case 0x15: // NAK - Light 3 on
            case 0x16: // SYN - Clear all lamps
                return true;

            // Cursor movement. This is how a TDV moves the cursor - by C0 code, not by escape
            // sequence - and it is what its own arrow keys send in every mode.
            case 0x0B: // VT  - cursor down
            case 0x18: // CAN - cursor right
            case 0x1C: // FS  - cursor up
            case 0x1D: // GS  - cursor home
                return true;

            // Whole-screen operations from the same table.
            case 0x17: // ETB - roll down
            case 0x19: // EM  - erase page
                return true;

            default:
                return false;
        }
    }

    /// <summary>
    /// Process a TDV 2115 C0 control character
    /// Returns true if the character was handled
    /// </summary>
    public bool ProcessTDV2115ControlCharacter(char c)
    {
        // In 2115 compatibility mode the whole set is active. In TDV-native mode only the
        // codes documented as "All Models" are - see IsNativeModeControlCode.
        if (!_is2115CompatibilityMode && !IsNativeModeControlCode((byte)c))
            return false;

        switch ((byte)c)
        {
            // Video Control
            case 0x02: // STX - Video off
                _videoOn = false;
                return true;
            case 0x03: // ETX - Video on
                _videoOn = true;
                return true;

            // Screen Operations
            case 0x04: // EOT - Erase line
                EraseCurrentLine();
                return true;
            case 0x19: // EM - Erase page
                ErasePage();
                return true;

            // LED Control
            case 0x05: // ENQ - Light 1 on
                SetLamp(0, true);
                return true;
            case 0x06: // ACK - Light 2 on
                SetLamp(1, true);
                return true;
            case 0x15: // NAK - Light 3 on
                SetLamp(2, true);
                return true;
            case 0x16: // SYN - Keyboard lights off
                ClearLamps();
                return true;

            // Attributes
            case 0x0E: // SO - Underline
                if (_videoOn)
                {
                    var currentAttrs = _emulator.Buffer.GetCell(_emulator.Cursor.Row, _emulator.Cursor.Column).Attributes;
                    currentAttrs |= CharacterAttributes.Underline;
                    var cell = _emulator.Buffer.GetCell(_emulator.Cursor.Row, _emulator.Cursor.Column);
                    cell.Attributes = currentAttrs;
                    _emulator.Buffer.SetCell(_emulator.Cursor.Row, _emulator.Cursor.Column, cell);
                }
                return true;
            case 0x0F: // SI - Normal (clear underline)
                if (_videoOn)
                {
                    var currentAttrs = _emulator.Buffer.GetCell(_emulator.Cursor.Row, _emulator.Cursor.Column).Attributes;
                    currentAttrs &= ~CharacterAttributes.Underline;
                    var cell = _emulator.Buffer.GetCell(_emulator.Cursor.Row, _emulator.Cursor.Column);
                    cell.Attributes = currentAttrs;
                    _emulator.Buffer.SetCell(_emulator.Cursor.Row, _emulator.Cursor.Column, cell);
                }
                return true;

            // Cursor Movement
            case 0x08: // BS - Backspace (cursor left)
                if (_videoOn && _emulator.Cursor.Column > 0)
                {
                    _emulator.Cursor.Column--;
                }
                return true;
            case 0x0A: // LF - Line feed (cursor down)
                if (_videoOn)
                {
                    if (_emulator.Cursor.Row < _emulator.Height - 1)
                    {
                        _emulator.Cursor.Row++;
                    }
                    else
                    {
                        RollUp();
                    }
                }
                return true;
            case 0x0B: // VT - Cursor down
                if (_videoOn)
                {
                    if (_emulator.Cursor.Row < _emulator.Height - 1)
                    {
                        _emulator.Cursor.Row++;
                    }
                    else
                    {
                        RollUp();
                    }
                }
                return true;
            case 0x0C: // FF - Roll up (page up/scroll up)
                RollUp();
                return true;
            case 0x0D: // CR - Cursor return (cursor to start of line)
                CursorToStartOfLine();
                return true;
            case 0x18: // CAN - Cursor right
                if (_videoOn && _emulator.Cursor.Column < _emulator.Width - 1)
                {
                    _emulator.Cursor.Column++;
                }
                return true;
            case 0x1C: // FS - Cursor up
                if (_videoOn && _emulator.Cursor.Row > 0)
                {
                    _emulator.Cursor.Row--;
                }
                return true;
            case 0x1D: // GS - Cursor home
                CursorHome();
                return true;

            // Roll Operations
            case 0x17: // ETB - Roll down (page down/scroll down)
                RollDown();
                return true;

            // DLE - Direct Line Entry (binary cursor positioning)
            case 0x10: // DLE - Cursor load (start)
                _isDLEMode = true;
                _dleByteCount = 0;
                return true;

            // Standard codes - let base class handle
            case 0x07: // BEL - Bell
            case 0x09: // HT - Tab
                return false; // Let base class handle
        }

        // If in DLE mode, handle position bytes
        if (_isDLEMode)
        {
            HandleDLEByte((byte)c);
            return true;
        }

        return false; // Let base class handle
    }

    /// <summary>
    /// Handle DLE binary cursor positioning
    /// DLE uses a two-byte binary positioning system per TDV spec:
    /// 1. First byte: Line number (0-24, 0-based) with 5-bit mask (0b11111)
    /// 2. Second byte: Column number (0-79, 0-based) with 7-bit mask (0b1111111)
    /// Per spec: "Line number 0-24 and column number 0-79"
    /// </summary>
    /// <remarks>
    /// <para>
    /// The masks make this tolerant of BOTH observed encodings, which is why no arithmetic
    /// change was needed when native-mode DLE was enabled:
    /// </para>
    ///   Unbiased: coordinates sent as raw 0-based values, e.g. <c>DLE 0x05 0x0A</c> = row 5, col 10.
    ///   Biased: coordinates sent as <c>0x7F + n</c> (1-based), as SINTRAN terminal type 53 does,
    ///   e.g. <c>DLE 0x83 0x82</c> = row 4, col 3 (1-based) = row 3, col 2 (0-based).
    ///   Masking off the 0x80 bias yields the same 0-based value.
    /// <para>
    /// NOTE: docs/TDV-COMPLETE-ESCAPE-SEQUENCE-REFERENCE.md states a 5-bit mask for the COLUMN.
    /// That is wrong on its face - 5 bits cannot express the documented column range 0-79 - so
    /// the 7-bit mask used here is retained. Row genuinely is 5 bits (0-24 fits in 0-31).
    /// </para>
    /// </remarks>
    public void HandleDLEByte(byte b)
    {
        if (_dleByteCount == 0)
        {
            // Row: 5-bit mask (0-31, but typically 0-24)
            _dleLine = b & 0b11111;
            _dleByteCount++;
        }
        else
        {
            // Column: 7-bit mask (0-127, but typically 0-79)
            _dleColumn = b & 0b1111111;

            // Validate and set cursor position
            if (_dleLine < _emulator.Height)
                _emulator.Cursor.Row = _dleLine;
            if (_dleColumn < _emulator.Width)
                _emulator.Cursor.Column = _dleColumn;

            _isDLEMode = false;
            _dleByteCount = 0;
        }
    }

    /// <summary>
    /// Check if DLE mode is active and handle the byte if so
    /// Returns true if the byte was handled
    /// </summary>
    public bool HandleDLE(byte b)
    {
        if (!_isDLEMode)
            return false;

        HandleDLEByte(b);
        return true;
    }

    /// <summary>
    /// Begins DLE (Direct Line Entry) cursor addressing: the next two bytes are the
    /// row and column coordinates.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Deliberately NOT gated on <see cref="Is2115CompatibilityMode"/>. DLE is part of the
    /// TDV native C0 set - docs/TDV-COMPREHENSIVE-REFERENCE.md lists
    /// "0x10 DLE Direct Line Entry (cursor load)" in the table headed
    /// "Standard ISO 6429 C0 Codes - All Models".
    /// </para>
    /// <para>
    /// Before this method existed, 0x10 was only recognised inside
    /// <see cref="ProcessTDV2115ControlCharacter"/>, which returns early when 2115
    /// compatibility mode is off. SINTRAN terminal type 53 (TDV-2200/9) drives the terminal
    /// in native mode with 2115 mode OFF, so cursor addressing was silently dropped and the
    /// two coordinate bytes were rendered as high-bit garbage text.
    /// </para>
    /// </remarks>
    public void BeginDLE()
    {
        _isDLEMode = true;
        _dleByteCount = 0;
    }

    /// <summary>
    /// Erase current line from cursor to end (with video check)
    /// </summary>
    private void EraseCurrentLine()
    {
        if (!_videoOn) return;
        _emulator.EraseCurrentLine();
    }

    /// <summary>
    /// Erase entire page/screen (with video check)
    /// </summary>
    private void ErasePage()
    {
        if (!_videoOn) return;
        _emulator.ErasePage();
    }

    /// <summary>
    /// Roll up (scroll up/page up) (with video check)
    /// </summary>
    private void RollUp()
    {
        if (!_videoOn) return;
        _emulator.RollUp();
    }

    /// <summary>
    /// Roll down (scroll down/page down) (with video check)
    /// </summary>
    private void RollDown()
    {
        if (!_videoOn) return;
        _emulator.RollDown();
    }

    /// <summary>
    /// Move cursor to start of current line (with video check)
    /// </summary>
    private void CursorToStartOfLine()
    {
        if (!_videoOn) return;
        _emulator.CursorToStartOfLine();
    }

    /// <summary>
    /// Move cursor to home position (0,0) (with video check)
    /// </summary>
    private void CursorHome()
    {
        if (!_videoOn) return;
        _emulator.CursorHome();
    }

    /// <summary>
    /// Reset handler to initial state
    /// </summary>
    public void Reset()
    {
        _is2115CompatibilityMode = false;
        _videoOn = true;
        _leds[0] = _leds[1] = _leds[2] = false;
        _isDLEMode = false;
        _dleByteCount = 0;
        _dleLine = 0;
        _dleColumn = 0;
    }
}

