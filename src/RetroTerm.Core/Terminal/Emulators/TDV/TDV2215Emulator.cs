using System;
using System.Text;
using RetroTerm.Core.Terminal.Buffer;
using RetroTerm.Core.Terminal.Emulators.TDV.Components;
using RetroTerm.Core.Terminal.Parsing;

namespace RetroTerm.Core.Terminal.Emulators.TDV;

/// <summary>
/// TDV2215 emulator with extended mode features
/// Implements dual-mode: 2115 compatibility + extended mode
/// Supports extended C0/C1 controls, three-character ESC sequences, and DCS sequences
/// Uses composition to reuse TDV2200 functionality and add TDV2215-specific features
///
/// Character Sets (TDV2215/TDV2115 - different from TDV2200!):
/// - Set 1 (fontNum=0,1): ASCII standard characters
/// - Set 2 (fontNum=2): Line drawing, histogram, plotting (accessed via SS2/LS2)
/// - Set 3 (fontNum=3): Subscript/superscript (accessed via SS3/LS3)
/// - Set 4 (fontNum=4): Control code display for transparent mode
/// </summary>
public class TDV2215Emulator : TDVEmulatorBase
{
    /// <inheritdoc/>
    public override Profiles.TerminalProfile Profile => TdvProfile;

    /// <inheritdoc/>
    public override Input.TerminalModes GetActiveModes()
    {
        var modes = base.GetActiveModes();
        modes |= Is2115CompatibilityMode ? Input.TerminalModes.TDV2115Mode : Input.TerminalModes.TDV2215Mode;
        return modes;
    }

    /// <summary>
    /// The TDV2215's identity. DA reply matches HandleDeviceAttributesQuery below.
    /// </summary>
    public static readonly Profiles.TerminalProfile TdvProfile =
        Profiles.TerminalProfile.ForTdv("TDV2215", "\x1b[?1;2c");

    // Component-based handlers (reusing TDV2200 functionality)

    // TDV2215-specific features
    private readonly TDV2215Features _features;

    // Single shift state: 0=none, 2=SS2 (next char from G2/Set2), 3=SS3 (next char from G3/Set3)
    private byte _pendingSingleShift;

    // Locking shift state: tracks which character set is currently locked
    // 0=G0, 1=G1, 2=G2 (via LS2), 3=G3 (via LS3)
    private byte _lockedCharacterSet;

    /// <summary>
    /// Whether the terminal is in extended operation - the Extended Control switch on.
    /// </summary>
    /// <remarks>
    /// This is ONE switch with two names, so it is now one flag with two readings. TDV 2215
    /// Functional Specifications section 3.1: "Whether the TDV 2215 should be limited to the TDV
    /// 2115 compatible mode, or take advantage of its full capability, is controlled by a
    /// Soft-switch called the Extended Control switch (4.2.12). When this switch is set to OFF, the
    /// terminal works like a TDV 2115 from the host computer&#39;s point of view."
    ///
    /// Extended operation is therefore exactly "not 2115-compatible", and section 8.7.1 gives the
    /// host control of it as mode 66, EC, with RM = OFF and SM = ON. TDVEmulatorBase already
    /// handles that number.
    ///
    /// Until 11 September 2026 this was a separate flag switched by <c>CSI ? 1 h</c> and
    /// <c>CSI ? 1 l</c>, which are DECCKM - application cursor keys. A host turning application
    /// cursor keys on put this emulator into "extended mode" and changed nothing about the cursor
    /// keys, which is two defects in one sequence.
    /// </remarks>
    public bool IsExtendedMode => !Is2115CompatibilityMode;

    /// <summary>
    /// Whether the terminal is in transparent operation.
    /// </summary>
    /// <remarks>
    /// NO HOST SEQUENCE SETS THIS, and none should be added. Transparent operation is the
    /// TRANSPARENT setting of the Send-Receive Mode soft-switch, section 4.3.1, reached from the
    /// keyboard - and the manual is explicit that it is also the only way out: "The only exit
    /// possible from this mode is obtained by depressing the MODE key twice, which gives access to
    /// the Soft-switch menu." Section 8.7.1 lists every mode the host may set and SRM is not one.
    ///
    /// It used to answer <c>CSI ? 2 h</c>, a DEC private number the ND-1200 mode table lists among
    /// those the terminal IGNORES.
    /// </remarks>
    public bool IsTransparentMode => _features.IsTransparentMode;

    /// <summary>
    /// Gets whether 2115 compatibility mode is active
    /// </summary>

    public TDV2215Emulator(int width = 80, int height = 24, int maxScrollback = 10000)
        : base(width, height, maxScrollback)
    {
        // Initialize character sets in base class
        _g0CharacterSet = TDVCharacterSets.TDVCharacterSetType.GraphicsI;
        _g1CharacterSet = TDVCharacterSets.TDVCharacterSetType.GraphicsI;
        _g2CharacterSet = TDVCharacterSets.TDVCharacterSetType.GraphicsII;
        _g3CharacterSet = TDVCharacterSets.TDVCharacterSetType.Math;

        // Initialize component handlers (reusing TDV2200 functionality)
        // Default to International (US) per TDV2115 spec 9.1.1

        // Initialize TDV2215-specific features
        _features = new TDV2215Features();
    }

    /// <summary>
    /// Gets the maximum scrollback lines for TDV2215 (10000 lines)
    /// </summary>
    public override int MaxScrollback => 10000;

    public override void ResetToInitialState()
    {
        base.ResetToInitialState();

        // Reset single shift and locking shift state
        _pendingSingleShift = 0;
        _lockedCharacterSet = 0;

        // Reset TDV2200 component handlers (may be null during base constructor call)
        CompatibilityHandler?.Reset();
        CharacterSetManager?.Reset();
        Iso646Handler?.Reset();
        InputProcessor?.Reset();

        // Reset TDV2215 features (may be null during base constructor call)
        _features?.Reset();
    }

    protected override void HandleCsiSequence(EscapeSequenceParser parser)
    {
        var final = (char)parser.FinalByte;
        var parameters = parser.Parameters;
        var privateMarker = parser.PrivateMarker;

        // Handle query sequences FIRST (before mode sequences)
        // This ensures DA, CPR, DSR, and mode queries are processed
        if (HandleQuerySequence(final, parameters, privateMarker, parser))
        {
            return;
        }

        // THE PRIVATE-MODE BLOCK THAT USED TO BE HERE IS GONE, 11 September 2026.
        //
        // It claimed three DEC private numbers for this model and every one of them was invented:
        //
        //   CSI ? 1 h   "extended mode"     - is DECCKM, application cursor keys
        //   CSI ? 2 h   "transparent mode"  - ND-1200 section 5.64 lists 2 among the numbers the
        //                                     terminal IGNORES, and transparent operation is a
        //                                     keyboard soft-switch the host cannot reach at all
        //   CSI ? 40 h  "2115 compatibility" - 40 is PCF, the printer code format, and the real
        //                                     2115 switch is ANSI mode 66 with no marker, which
        //                                     TDVEmulatorBase has handled since the mode work
        //
        // So a host asking a TDV2215 for application cursor keys got extended mode and no cursor
        // key change. With the block gone all three fall through to the ordinary DEC private path,
        // where they mean what DEC says they mean. Extended mode now reads off the EC switch - see
        // the remarks on IsExtendedMode.

        // Handle extended mode sequences
        if (IsExtendedMode && _features.ExtendedMode.HandleCsiSequence(final, parameters, privateMarker, this))
        {
            return;
        }

        // Handle transparent mode sequences
        if (IsTransparentMode && _features.TransparentMode.HandleCsiSequence(final, parameters, privateMarker, this))
        {
            return;
        }

        // Handle TDV2215-specific sequences
        if (HandleTDV2215Sequence(final, parameters, privateMarker))
        {
            return;
        }

        // Fall back to base TDV handling
        base.HandleCsiSequence(parser);
    }

    // Note: Extended mode and transparent mode sequences are now handled by TDV2215Features components

    /// <summary>
    /// Handles TDV2215-specific sequences
    /// </summary>
    private bool HandleTDV2215Sequence(char final, ReadOnlySpan<int> parameters, byte privateMarker)
    {
        switch (final)
        {
            case 'z': // NDSAR - Set Attribute in Rectangle
                HandleSetAttributeInRectangle(parameters);
                return true;

            case 'u': // NDSREC - Save Rectangle
                HandleSaveRectangle(parameters);
                return true;

            case 'v': // NDRREC - Restore Rectangle
                HandleRestoreRectangle(parameters);
                return true;

            case '~': // NDDWA - Define Work Area
                HandleDefineWorkArea(parameters);
                return true;

            case '<': // NDVIDEO - Alpha/Graphics toggle
                HandleVideoToggle(parameters);
                return true;

            case '|': // NDRAR - Remove Attribute in Rectangle
                // <7C> is NDRAR. ND Display Terminal 1200 section 2.8 and the TDV 2200 CSI table
                // both give it, and this case used to hold an invented "extended mode specific"
                // handler that swallowed the sequence instead.
                HandleRemoveAttributeInRectangle(parameters);
                return true;

            case '}': // NDFC - Fill Character(s) in Rectangle
                // <7D> is NDFC, not NDRAR. The two were swapped here and in TDVSequenceBuilder.
                HandleFillCharacter(parameters);
                return true;

            default:
                return false;
        }
    }


    /// <summary>
    /// Start of a DCS sequence (PUSH-key, PROGRAM-key loading).
    /// </summary>
    /// <param name="parser">
    /// The parser, positioned at the end of the DCS introducer.
    /// </param>
    protected override void HandleDCSSequence(EscapeSequenceParser parser)
    {
        _features.DCSHandler.StartDCS();
    }

    /// <summary>
    /// A chunk of DCS payload.
    /// </summary>
    /// <param name="data">
    /// Payload bytes; valid only for this call.
    /// </param>
    protected override void HandleDCSData(ReadOnlySpan<byte> data)
    {
        _features.DCSHandler.AppendData(data);
    }

    /// <summary>
    /// End of the DCS sequence - apply whatever was programmed.
    /// </summary>
    protected override void HandleDCSEnd()
    {
        _features.DCSHandler.EndDCS(this);
    }

    /// <summary>
    /// Handles extended C0/C1 controls, including TDV2115 compatibility mode controls
    /// </summary>
    protected override void HandleExecute(byte control)
    {
        // CRITICAL: If single shift is pending, treat control codes as display characters
        // from the shifted character set (charset 3 for subscript/superscript).
        // The parser calls HandleExecute for C0 codes (0x00-0x1F), but when SS3 is active,
        // these codes map to subscript digits (0x00-0x09) and superscript digits (0x10-0x19).
        if (_pendingSingleShift != 0)
        {
            HandleCharacter(control);
            return;
        }

        // First check for DLE mode (binary cursor positioning) - this takes priority
        if (CompatibilityHandler.IsDLEMode)
        {
            if (CompatibilityHandler.HandleDLE(control))
            {
                return;
            }
        }

        // Handle extended mode controls
        if (IsExtendedMode)
        {
            if (_features.ExtendedMode.HandleControl(control, this))
            {
                return;
            }
        }

        // Handle transparent mode controls
        if (IsTransparentMode)
        {
            if (_features.TransparentMode.HandleControl(control, this))
            {
                return;
            }
        }

        // In TDV2115 compatibility mode, handle the special C0 codes
        // These codes are specific to TDV2115 mode and differ from standard VT100 handling
        if (CompatibilityHandler.Is2115CompatibilityMode)
        {
            if (CompatibilityHandler.ProcessTDV2115ControlCharacter((char)control))
            {
                OnInvalidated();
                return;
            }
        }

        base.HandleExecute(control);
    }

    /// <summary>
    /// Handles ESC sequences including SS2/SS3/LS2/LS3 for character set switching
    /// </summary>
    protected override void HandleEscapeSequence(EscapeSequenceParser parser)
    {
        var final = (char)parser.FinalByte;
        var intermediates = parser.Intermediates;

        // Handle single-character ESC sequences (no intermediates)
        if (intermediates.Length == 0)
        {
            switch (final)
            {
                case 'Q': // ESC Q - Exit 2115 compatibility mode, which IS enabling extended mode
                    // One call, not two: IsExtendedMode is now "not 2115-compatible", so turning
                    // the EC switch on is the whole action. 2215 sections 3.1 and 7.9.2.
                    CompatibilityHandler.Set2115CompatibilityMode(false);
                    return;

                case 'N': // ESC N - SS2 (Single Shift 2)
                    // Next character from G2 (character set 2 - line drawing)
                    _pendingSingleShift = 2;
                    return;

                case 'O': // ESC O - SS3 (Single Shift 3)
                    // Next character from G3 (character set 3 - subscript/superscript)
                    _pendingSingleShift = 3;
                    return;

                case 'n': // ESC n - LS2 (Locking Shift 2)
                    // Lock to G2 (character set 2 - line drawing)
                    _lockedCharacterSet = 2;
                    InvokeCharacterSet(2);
                    return;

                case 'o': // ESC o - LS3 (Locking Shift 3)
                    // Lock to G3 (character set 3 - subscript/superscript)
                    _lockedCharacterSet = 3;
                    InvokeCharacterSet(3);
                    return;

                case '~': // ESC ~ - LS1R (Locking Shift 1 Right) - not commonly used
                case '}': // ESC } - LS2R (Locking Shift 2 Right) - not commonly used
                case '|': // ESC | - LS3R (Locking Shift 3 Right) - not commonly used
                    // These are 8-bit mode locking shifts, rarely used
                    return;
            }
        }

        // Handle three-character sequences using the feature component
        if (intermediates.Length == 2)
        {
            if (_features.ThreeCharacterSequences.HandleThreeCharacterSequence(final, intermediates, this))
            {
                return;
            }
        }

        // Handle SI (0x0F) / SO (0x0E) via escape sequence for explicit G0/G1 switch
        // These are typically handled as control codes but can also appear after ESC
        if (intermediates.Length == 0)
        {
            switch (final)
            {
                case '\x0F': // SI - Shift In (back to G0)
                    _lockedCharacterSet = 0;
                    InvokeCharacterSet(0);
                    return;

                case '\x0E': // SO - Shift Out (to G1)
                    _lockedCharacterSet = 1;
                    InvokeCharacterSet(1);
                    return;
            }
        }

        base.HandleEscapeSequence(parser);
    }

    /// <summary>
    /// Handles function key programming
    /// </summary>
    public void ProgramFunctionKey(int keyNumber, string sequence)
    {
        if (keyNumber >= 1 && keyNumber <= 24)
        {
            ProgramPushKey(keyNumber, sequence);
        }
    }

    // HandleExtendedModeInput and HandleTransparentModeInput were deleted on 11 September 2026.
    // Nothing called either of them - they were a second, hand-rolled byte scanner looking for the
    // invented ESC [ ? 1 h and ESC [ ? 2 h, sitting beside the real parser and never reached.

    /// <summary>
    /// Gets the terminal type identifier
    /// </summary>
    public override string GetTerminalType()
    {
        var type = "TDV2215";

        // The string names the DEPARTURE from the ordinary state, not the ordinary state itself.
        // Extended operation IS the ordinary state of this emulator - it accepts CSI from the first
        // byte, which is only true with the Extended Control switch on - so a plain "TDV2215" means
        // extended, and 2115-compatible operation is the thing worth saying. Before 11 September
        // 2026 extended mode was a separate flag that started off, so a terminal happily obeying
        // CSI reported itself as not extended.
        if (!IsExtendedMode)
        {
            type += "+2115";
        }

        if (IsTransparentMode)
        {
            type += "+TRANSPARENT";
        }

        return type;
    }

    /// <summary>
    /// Gets the terminal capabilities
    /// </summary>
    public override string GetTerminalCapabilities()
    {
        var capabilities = new StringBuilder();
        capabilities.Append("TDV2215");

        // See GetTerminalType: the departure is what gets named.
        if (!IsExtendedMode)
        {
            capabilities.Append("+2115");
        }

        if (IsTransparentMode)
        {
            capabilities.Append("+TRANSPARENT");
        }

        capabilities.Append("+NDGRAPHICS");
        capabilities.Append("+NDWORKAREA");
        capabilities.Append("+NDPROTECTED");
        capabilities.Append("+NDLEDS");
        capabilities.Append("+NDPUSHKEYS");
        capabilities.Append("+DCS");
        capabilities.Append("+THREECHAR");

        return capabilities.ToString();
    }

    // Maximum scrollback: TDV2215 takes it from the constructor parameter, so there is no
    // member here to document - this used to be an XML comment on nothing at all.
    // TDV2215 uses the constructor parameter for MaxScrollback



    #region Character Set Switching (SS2/SS3/LS2/LS3)

    /// <summary>
    /// Override HandleCharacter to apply single shift and set FontNumber for TDV2215 bitmap fonts.
    /// </summary>
    protected override void HandleCharacter(uint codepoint)
    {
        // Determine which font/character set to use
        byte fontNumber = 0;
        bool isShifted = false;

        if (_pendingSingleShift != 0)
        {
            // Single shift: use the shifted character set for this one character only
            fontNumber = _pendingSingleShift;
            _pendingSingleShift = 0; // Clear after one character
            isShifted = true;
        }
        else if (_lockedCharacterSet > 0)
        {
            // Locking shift: use the locked character set
            fontNumber = _lockedCharacterSet;
            isShifted = true;
        }

        // When shifted to charset 3 (subscript) or charset 4 (control display),
        // characters 0x00-0x1F are valid glyph positions - don't treat as control codes
        if (!isShifted && codepoint < 0x20)
        {
            // Not shifted - treat as control code
            base.HandleCharacter(codepoint);
            return;
        }

        // For transparent mode, display control codes using character set 4
        if (IsTransparentMode && codepoint < 0x20)
        {
            fontNumber = 4;
        }

        // Perform the wrap the previous character deferred, before anything is written.
        ResolveDeferredWrap();

        // Insert mode handling
        if (InsertMode && !Cursor.AtLastColumn)
        {
            ShiftCharactersRight(Cursor.Row, Cursor.Column, 1);
        }

        // Write character to buffer with correct font number
        ref var cell = ref Buffer[Cursor.Row, Cursor.Column];
        cell.Codepoint = codepoint;
        // Carry the line-size bits across — they belong to the line, not to this character.
        cell.Attributes = CurrentAttributes | (cell.Attributes & LineSizeAttributes) | ProtectedAttribute;
        cell.Foreground = CurrentForeground;
        cell.Background = CurrentBackground;
        cell.FontNumber = fontNumber;

        // At the last USABLE column — half the screen on a double-width line — this arms the Last
        // Column Flag instead of moving. The wrap and any scroll then happen in
        // ResolveDeferredWrap at the top of the NEXT character — one implementation shared with
        // the base class, because this method having its own copy of the wrap rule is exactly how
        // 2215 came to never scroll at the bottom of a scrolling region while 2200 did.
        Cursor.AdvanceWithin(LastUsableColumn(Cursor.Row));

        OnInvalidated();
    }

    /// <summary>
    /// Gets whether a single shift is pending
    /// </summary>
    public bool HasPendingSingleShift => _pendingSingleShift != 0;

    /// <summary>
    /// Gets the pending single shift value (2=SS2, 3=SS3, 0=none)
    /// </summary>
    public byte PendingSingleShift => _pendingSingleShift;

    /// <summary>
    /// Gets the current locked character set (0=G0, 1=G1, 2=G2/LS2, 3=G3/LS3)
    /// </summary>
    public byte LockedCharacterSet => _lockedCharacterSet;

    #endregion

    #region TDV2215-Specific Query Responses

    /// <summary>
    /// Handles device attributes query for TDV2215
    /// </summary>
    protected override string HandleDeviceAttributesQuery()
    {
        if (CompatibilityHandler.Is2115CompatibilityMode)
        {
            return "\x1b[?1;0c"; // TDV2115 compatible response
        }
        return "\x1b[?1;2c"; // TDV2215 response
    }

    /// <summary>
    /// Handles secondary device attributes query for TDV2215
    /// Format: ESC [ > Ps ; Pv ; Pc c where Ps is firmware ID (115 for TDV2215)
    /// </summary>
    protected override string HandleSecondaryDeviceAttributesQuery()
    {
        if (CompatibilityHandler.Is2115CompatibilityMode)
        {
            return "\x1b[>115;0;0c"; // TDV2115 firmware ID
        }
        return "\x1b[>115;0;0c"; // TDV2215 firmware ID (same as 2115)
    }

    /// <summary>
    /// Handles terminal identification query for TDV2215
    /// </summary>
    protected override string HandleTerminalIdentification()
    {
        if (CompatibilityHandler.Is2115CompatibilityMode)
        {
            return "\x1b[?1;0c"; // TDV2115 identification
        }
        return "\x1b[?1;2c"; // TDV2215 identification
    }

    /// <summary>
    /// Handles 2115 compatibility mode for TDV2215
    /// </summary>
    protected override void Handle2115CompatibilityMode(bool enable)
    {
        CompatibilityHandler.Set2115CompatibilityMode(enable);
    }

    /// <summary>
    /// Gets 2115 compatibility mode state
    /// </summary>
    protected override bool Get2115CompatibilityMode()
    {
        return CompatibilityHandler.Is2115CompatibilityMode;
    }

    // GetModeState used to sit here, answering DECRQM about "mode 1 = extended mode" and
    // "mode 2 = transparent mode". It is gone, 11 September 2026.
    //
    // No TDV manual has DECRQM at all - TDV 2215 Functional Specifications section 8.7 is a
    // complete list of the CSI sequences this terminal accepts and there is no '$' intermediate
    // anywhere in it, while section 8.3.2 lists everything it ever sends and that is one sequence,
    // CPR. Mode numbers 1 and 2 came from a document in docs\ that cites no source; on the real
    // machine, extended operation is the EC switch, mode 66, and RESET is the 2115 side of it
    // (sections 3.1 and 8.7.1). Transparent mode is section 3.3 and is not a mode number.
    //
    // See docs\TDV-MODES-AND-REPORTS-FROM-THE-MANUALS.md.

    #endregion

    #region Character Set Variant (TDV2115 Spec Section 9.1)

    #endregion
}
