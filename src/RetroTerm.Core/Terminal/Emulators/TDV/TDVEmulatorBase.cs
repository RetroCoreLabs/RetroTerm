using System;
using System.Text;
using RetroTerm.Core.Logging;
using RetroTerm.Core.Terminal.Buffer;
using RetroTerm.Core.Terminal.Parsing;

namespace RetroTerm.Core.Terminal.Emulators.TDV;

/// <summary>
/// Base class for all TDV (Tandberg Data Video) terminal emulators providing:
/// - VT100 compatibility layer
/// - ND-specific CSI sequences
/// - Protected areas (SPA/EPA) management
/// - Work areas (NDDWA) support
/// - Message LEDs (NDCLED, NDSLED, NDBLED)
/// - Smooth scroll mode (NDSSM)
/// - Line wrap modes (NDBLWM, NDELWM - beginning and end of line wrap, NOT blink)
/// - Rectangle operations (NDILWA, NDDLWA, NDICHE, NDDCHE)
/// 
/// Derived classes implement specific TDV models (TDV1200, TDV2215, TDV2200).
/// </summary>
public abstract class TDVEmulatorBase : TerminalEmulatorBase
{
    // TDV-specific modes and state
    // SmoothScrollMode used to be declared here. It moved up to TerminalEmulatorBase when DECSCLM
    // was built, because a TDV's smooth scroll and a VT's private mode 4 are the SAME behaviour and
    // two fields for one behaviour is the trap this codebase keeps naming - a fix lands on one and
    // the other stays broken. The TDV mode sets the shared flag, and the animation the base class
    // drives works on a TDV for free.
    //
    // The TDV number for it is 60, RT "Roll Type", STEP or SMOOTH - TDV 2215 Functional
    // Specifications section 8.7.1. It was 67 here until 11 September 2026, which is HAN, the
    // XON/XOFF handshake. On the ND Display Terminal 1200 it is DEC's own CSI ? 4 h, which the base
    // class already answered, so that model needed nothing added.
    // BlinkMode and EnhancedBlinkMode used to be declared here, set by "CSI ? 68 h" and
    // "CSI ? 69 h", and read by nothing at all. They were named for ND-1200's NDBLWM and NDELWM,
    // which are the Beginning and End of Line WRAP modes (sections 4.10 and 4.11) - the manual's
    // eighteen modes include no blink mode of any kind. Both now route into the wrap flags the base
    // class already keeps. See docs\TDV-MODES-AND-REPORTS-FROM-THE-MANUALS.md.

    /// <summary>
    /// True when the numeric pad is in "Function" mode rather than "Numeric".
    /// </summary>
    /// <remarks>
    /// TDV 2200/9 S User's Guide section 11.2: <c>CSI 80 h</c> selects Function, <c>CSI 80 l</c>
    /// selects Numeric. It is an ANSI mode with no private marker, so it does NOT collide with
    /// DEC private mode 80, DECSDM. Captured from a real host: PED's exit ritual on the D100 sends
    /// <c>CSI 66;62;80 l</c>.
    /// </remarks>
    protected bool NumericPadFunctionMode;

    // Character set management (G0-G3 designation and invocation)
    // Initialized to defaults per TDV spec: G0/G1=US ASCII, G2/G3=Graphics
    protected TDVCharacterSets.TDVCharacterSetType _g0CharacterSet = TDVCharacterSets.TDVCharacterSetType.USASCII;
    protected TDVCharacterSets.TDVCharacterSetType _g1CharacterSet = TDVCharacterSets.TDVCharacterSetType.USASCII;
    protected TDVCharacterSets.TDVCharacterSetType _g2CharacterSet = TDVCharacterSets.TDVCharacterSetType.GraphicsI;
    protected TDVCharacterSets.TDVCharacterSetType _g3CharacterSet = TDVCharacterSets.TDVCharacterSetType.GraphicsII;
    protected byte _invokedCharacterSet = 0; // 0=G0, 1=G1, 2=G2, 3=G3

    // Protected areas (SPA/EPA) management
    public readonly TDVProtectedAreas ProtectedAreas;

    // Work areas (NDDWA) support
    public readonly TDVWorkAreas WorkAreas;

    // Message LEDs
    public readonly TDVMessageLEDs MessageLEDs;

    // Rectangle operations
    public readonly TDVRectangleOperations RectangleOperations;

    // PUSH keys for programmable functionality
    public readonly TDVPushKeys PushKeys;

    // TDV character sets (10 different sets) - accessed via static methods

    /// <summary>
    /// Gets the current work area bounds
    /// </summary>
    public (int Left, int Top, int Right, int Bottom) CurrentWorkArea => WorkAreas.GetCurrentWorkArea();

    /// <summary>
    /// Gets the maximum scrollback lines for TDV terminals (2000 lines default)
    /// </summary>
    public override int MaxScrollback => 2000;

    /// <summary>
    /// Gets the current protected area state
    /// </summary>
    public bool IsProtectedArea => ProtectedAreas.IsProtected(Cursor.Row, Cursor.Column);

    /// <summary>
    /// The four message lamps, in parameter order: EXPAND, APPEND, BUSY, MESSAGE.
    /// </summary>
    /// <remarks>
    /// Each is off, lit or blinking. This used to be three booleans named after the three
    /// OPERATIONS - see the remarks on <see cref="TDVMessageLEDs"/>.
    /// </remarks>
    public (TDVMessageLampState Expand, TDVMessageLampState Append,
            TDVMessageLampState Busy, TDVMessageLampState Message) MessageLEDState
        => (MessageLEDs.GetState(1), MessageLEDs.GetState(2),
            MessageLEDs.GetState(3), MessageLEDs.GetState(4));

    /// <summary>
    /// Gets the three keyboard indicator lamps driven by the host C0 codes
    /// ENQ (L1 on), ACK (L2 on), NAK (L3 on) and SYN (all off).
    /// </summary>
    /// <remarks>
    /// Base returns all-off. Derived emulators that own a
    /// <c>TDV2115CompatibilityHandler</c> override this to expose its real lamp state.
    /// Declared here so the UI can read lamp state from any TDV emulator without
    /// downcasting to a specific model.
    /// </remarks>
    public virtual (bool L1, bool L2, bool L3) KeyboardLights => CompatibilityHandler.Lamps;

    /// <summary>
    /// Raised whenever any indicator lamp changes - message LEDs or keyboard lamps.
    /// The virtual keyboard subscribes to this to mirror the terminal's lamps.
    /// </summary>
    /// <remarks>
    /// Carries no payload deliberately: the subscriber re-reads
    /// <see cref="MessageLEDState"/> and <see cref="KeyboardLights"/>, which keeps the UI
    /// correct even if several lamps change in one host write.
    /// </remarks>
    public event Action? LedStateChanged;

    /// <summary>
    /// Raises <see cref="LedStateChanged"/>. Called by the components that own lamp state.
    /// </summary>
    public void RaiseLedStateChanged()
    {
        LedStateChanged?.Invoke();
    }

    protected TDVEmulatorBase(int width, int height, int maxScrollback = 10000)
        : base(width, height, maxScrollback)
    {
        ProtectedAreas = new TDVProtectedAreas(width, height);
        WorkAreas = new TDVWorkAreas(width, height);
        MessageLEDs = new TDVMessageLEDs();

        // Message lamp changes are lamp changes: fold them into the single UI-facing event.
        MessageLEDs.StateChanged += RaiseLedStateChanged;
        RectangleOperations = new TDVRectangleOperations();
        PushKeys = new TDVPushKeys();

        // The four shared components. Every TDV model built these itself, with byte-identical
        // code in all three leaf constructors, and then re-implemented the same five members on
        // top of them. Built once here instead — see the shared members further down.
        //
        // Constructing them with `this` from the base constructor is safe: each one only stores
        // the reference (verified in Components\), so none of them touches derived state that has
        // not been initialised yet.
        Iso646Handler = new Components.TDVISO646VariantHandler(this);
        CharacterSetManager = new Components.TDVCharacterSetManager(this);
        CompatibilityHandler = new Components.TDV2115CompatibilityHandler(this);
        InputProcessor = new Components.TDVInputProcessor(this, Iso646Handler, CharacterSetManager, CompatibilityHandler);

        // Initialize TDV-specific modes
        SmoothScrollMode = false;
        NumericPadFunctionMode = false;
    }

    // ── Shared TDV components and the members built on them ───────────────────────────────────
    //
    // These were triplicated across TDV1200, TDV2215 and TDV2200: identical component fields,
    // identical construction, and five identical members reading them. They had not yet diverged
    // — checked line by line before this change — but three copies of a thing is three places a
    // future edit can land in only one of.

    /// <summary>
    /// ISO 646 national variant state (Norwegian, Swedish, German, International).
    /// </summary>
    protected readonly Components.TDVISO646VariantHandler Iso646Handler;

    /// <summary>
    /// Character set selection and single-shift state.
    /// </summary>
    protected readonly Components.TDVCharacterSetManager CharacterSetManager;

    /// <summary>
    /// TDV2115 compatibility mode and its keyboard lamps.
    /// </summary>
    protected readonly Components.TDV2115CompatibilityHandler CompatibilityHandler;

    /// <summary>
    /// The pre-parser byte filter (DLE coordinates, ESC % variants, 2115 C0 codes).
    /// </summary>
    protected readonly Components.TDVInputProcessor InputProcessor;

    /// <summary>
    /// Whether TDV2115 compatibility mode is active.
    /// </summary>
    public bool Is2115CompatibilityMode => CompatibilityHandler.Is2115CompatibilityMode;


    public override void ResetToInitialState()
    {
        base.ResetToInitialState();

        // Reset TDV-specific state
        SmoothScrollMode = false;
        NumericPadFunctionMode = false;
        DecimalSeparatorMode = TDVDecimalSeparator.Period;   // ND private mode 7, RM position
        GraphicRenditionMode = TDVGraphicRenditionMode.Sgr;  // what this program actually does
        PageMode = false;                                    // ND private 3 / ANSI 47: ROLL
        KeyClickEnabled = false;                             // ND private 4 RESET: DISABLE
        PushKeyLabelsHidden = false;                         // ND private 5 RESET: labels ON
        ProgramKeyLabelsHidden = false;                      // ND private 6 RESET: labels ON

        // Reset character sets to defaults per TDV specification
        // G0/G1: US ASCII by default (no character mapping, fontNum stays at 0)
        // G2/G3: Special character sets for when explicitly invoked via SS2/SS3
        _g0CharacterSet = TDVCharacterSets.TDVCharacterSetType.USASCII;
        _g1CharacterSet = TDVCharacterSets.TDVCharacterSetType.USASCII;
        _g2CharacterSet = TDVCharacterSets.TDVCharacterSetType.GraphicsI;
        _g3CharacterSet = TDVCharacterSets.TDVCharacterSetType.GraphicsII;
        _invokedCharacterSet = 0;

        // Only reset TDV components if they are initialized
        ProtectedAreas?.Clear();
        WorkAreas?.Clear();
        MessageLEDs?.Clear();
        RectangleOperations?.Clear();
        PushKeys?.Clear();
    }


    /// <summary>
    /// Handles ND-specific CSI sequences
    /// </summary>
    protected virtual bool HandleNDSpecificSequence(char final, ReadOnlySpan<int> parameters, byte privateMarker)
    {
        // SM and RM carry a LIST of mode numbers, and the list may mix ND modes with ordinary
        // ones. That case is real: PED's exit ritual, captured from D100 on 28 August 2026, is
        // CSI 66;62;80 l - modes 66 and 80 are TDV modes, 62 is not. See PedExitCaptureTests.
        if (final == 'h' || final == 'l')
        {
            return HandleModeList(final, parameters, privateMarker);
        }

        // ND-specific sequences use private marker '?' or specific final characters
        if (privateMarker == (byte)'?' || IsNDSpecificFinal(final))
        {
            return HandleNDPrivateSequence(final, parameters, privateMarker);
        }

        return false;
    }

    /// <summary>
    /// Routes each mode number of an SM or RM sequence to the handler that owns it.
    /// </summary>
    /// <param name="final">
    /// The final byte - 'h' to set, 'l' to reset.
    /// </param>
    /// <param name="parameters">
    /// The mode numbers, in the order the host sent them.
    /// </param>
    /// <param name="privateMarker">
    /// The private marker byte, or zero when the sequence carries none.
    /// </param>
    /// <returns>
    /// True when this method dealt with the sequence; false to leave it to the ordinary path.
    /// </returns>
    /// <remarks>
    /// <para><b>The defect this replaces</b></para>
    /// The previous code tested <c>parameters[0]</c> alone, and on a match handed the WHOLE
    /// sequence to <see cref="HandleNDPrivateSequence"/>, which also reads only the first
    /// parameter. Every mode after the first was therefore neither acted on nor counted as
    /// unhandled - it vanished. Measured on <c>CSI 66;62;80 l</c>: mode 66 was handled, 62 and 80
    /// disappeared, while the same two numbers sent one at a time were counted correctly.
    ///
    /// That matters twice over. The modes are lost, and so is the EVIDENCE that they were lost -
    /// the unhandled counter is the instrument the whole ND work list is read off, and it was
    /// under-reporting exactly where a real host mixes the two kinds.
    ///
    /// <para><b>Why it returns false when no ND mode is present</b></para>
    /// So that an ordinary SM or RM behaves exactly as it did before. Only a list that actually
    /// contains an ND mode is taken over here, and then every OTHER number in it is passed to the
    /// handler it would have reached on its own.
    /// </remarks>
    protected bool HandleModeList(char final, ReadOnlySpan<int> parameters, byte privateMarker)
    {
        if (parameters.Length == 0)
        {
            return false;
        }

        // The unmarked form "CSI <mode> h/l" IS the ND form - it is not a tolerance.
        // TDV 2215 section 8.7.1 lists these as plain SM/RM parameter values, and the '>' form is
        // ND-1200's own private table. Restricted to the known ND mode numbers so that real ANSI
        // SM/RM modes (e.g. CSI 4 h = IRM) are NOT swallowed here.
        bool carriesAnNdMode = false;
        for (int i = 0; i < parameters.Length; i++)
        {
            if (IsNDPrivateMode(parameters[i], privateMarker))
            {
                carriesAnNdMode = true;
                break;
            }
        }

        if (!carriesAnNdMode)
        {
            return false;
        }

        bool enable = final == 'h';

        for (int i = 0; i < parameters.Length; i++)
        {
            if (IsNDPrivateMode(parameters[i], privateMarker))
            {
                // A one-element slice, so the handler's read of parameters[0] sees THIS mode.
                // Slicing a span allocates nothing.
                HandleNDPrivateSequence(final, parameters.Slice(i, 1), privateMarker);
            }
            else if (privateMarker == (byte)'?')
            {
                SetDecPrivateMode(parameters[i], enable);
            }
            else
            {
                SetAnsiMode(parameters[i], enable);
            }
        }

        return true;
    }

    /// <summary>
    /// Whether a mode number is one of the ND/TDV private modes handled by
    /// <see cref="HandleNDPrivateSequence"/>.
    /// </summary>
    /// <returns>
    /// True for the ND private modes; false for everything else, including ANSI modes.
    /// </returns>
    /// <remarks>
    /// <para><b>These numbers come from the manuals now, and every one of them used to be wrong</b></para>
    /// Until 11 September 2026 this listed 40, 66, 67, 68 and 69 as PRIVATE modes carrying a '?'
    /// marker, on the authority of two documents in <c>docs\</c> that cite no source. All five were
    /// a different switch in the real manuals - 40 is the printer code format, 67 the XON/XOFF
    /// handshake, 68 the cursor type, 69 the printer mode - and 66, the only right number, had its
    /// polarity and its marker wrong. The whole story, with sections quoted, is in
    /// <c>docs\TDV-MODES-AND-REPORTS-FROM-THE-MANUALS.md</c>.
    ///
    /// <para><b>The ND modes carry no '?'. They are ANSI modes, or they carry '>'</b></para>
    /// TDV 2215 Functional Specifications section 8.7.1 lists them as plain SM/RM parameter values;
    /// ND Display Terminal 1200 section 5.64 splits them into an ANSI table, a DEC-compatible '?'
    /// table that uses DEC's own numbers, and an ND private '>' table. Nothing here is claimed for
    /// the '?' marker any more: the DEC-compatible modes are DEC's and the base class already owns
    /// them, which is the "one behaviour, one place" rule this file keeps being caught by.
    ///
    /// <para><b>Why these numbers cannot collide with real ANSI modes</b></para>
    /// ECMA-48's own mode numbers stop in the low twenties. 31, 36, 60, 66 and 80 are all above
    /// that, so claiming them here takes nothing away from <see cref="TerminalEmulatorBase"/>.
    /// </remarks>
    /// <param name="mode">
    /// The mode number from the SM or RM parameter list.
    /// </param>
    /// <param name="privateMarker">
    /// The private marker byte the sequence carried, or zero when it carried none.
    /// </param>
    /// <returns>
    /// True when this emulator owns the mode and <see cref="HandleNDPrivateSequence"/> should have it.
    /// </returns>
    protected static bool IsNDPrivateMode(int mode, byte privateMarker)
    {
        // ND private modes, ND-1200 section 5.64, third table: CSI > n h / CSI > n l.
        if (privateMarker == (byte)'>')
        {
            return mode switch
            {
                1 => true, // NDBLWM - Beginning of Line Wrap Mode
                2 => true, // NDELWM - End of Line Wrap Mode
                3 => true, // Roll/page mode
                4 => true, // Key click mode
                5 => true, // PUSH-key label mode
                6 => true, // PROGRAM-key label mode
                7 => true, // Decimal separator mode - PERIOD, COMMA, SEQUENCE
                _ => false
            };
        }

        // Anything carrying '?' is DEC's, and the base class answers for it.
        if (privateMarker != 0)
        {
            return false;
        }

        // ANSI modes, 2215 section 8.7.1 and ND-1200 section 5.64 first table.
        return mode switch
        {
            31 => true, // BOL - Beginning of Line Wrap, 2215 8.7.1
            32 => true, // CR  - Cursor Return, 2215 8.7.1. RESET is CR, SET is CR+LF.
            36 => true, // EOL - End of Line Wrap, 2215 8.7.1. A real TDV2200 termcap sets this.
            47 => true, // RPM - Roll/Page, 2215 8.7.1. Same switch as ND private mode 3.
            53 => true, // KC  - Key Click, 2215 8.7.1. RM is ON here and SET is ON in the ND
                        // numbering - the polarity really is opposite. See the handler.
            55 => true, // AR  - Auto Repeat, 2215 8.7.1. RM is ON, SET is OFF - inverted like KC.
            68 => true, // CT  - Cursor Type, 2215 8.7.1. RESET is LINE, SET is BLOCK.
            60 => true, // RT  - Roll Type, STEP or SMOOTH, 2215 8.7.1
            62 => true, // GRM - Graphic Rendition Mode, 2215 8.7.1. THREE positions, and a real
                        // PED initialisation string sends it: ESC [ 62;36;66 l.
            66 => true, // EC  - Extended Control. RESET is the 2115 side. See Handle2115CompatibilityMode.
            80 => true, // Numeric pad Function/Numeric, TDV 2200/9 S User's Guide 11.2
            _ => false
        };
    }

    /// <summary>
    /// Determines if a final character is ND-specific
    /// </summary>
    protected virtual bool IsNDSpecificFinal(char final)
    {
        return final switch
        {
            'z' => true, // NDSAR - Set Attribute in Rectangle
            'u' => true, // NDSREC - Save Rectangle
            'v' => true, // NDRREC - Restore Rectangle
            '~' => true, // NDDWA - Define Work Area
            '<' => true, // NDVIDEO - Alpha/Graphics toggle
            '{' => true, // NDAAR - Add Attribute in Rectangle, hex 7B
            '|' => true, // NDRAR - Remove Attribute in Rectangle, hex 7C
            '}' => true, // NDFC  - Fill Character(s) in Rectangle, hex 7D
            _ => false
        };
    }

    /// <summary>
    /// Acts on one ND or TDV mode from an SM or RM sequence.
    /// </summary>
    /// <param name="final">
    /// The final byte - 'h' to set, 'l' to reset.
    /// </param>
    /// <param name="parameters">
    /// A one-element span holding the mode number.
    /// </param>
    /// <param name="privateMarker">
    /// The private marker byte, or zero when the sequence carried none.
    /// </param>
    /// <returns>
    /// True when the mode was acted on.
    /// </returns>
    /// <remarks>
    /// <para><b>Every number here is quoted from a manual. None of them used to be</b></para>
    /// See <c>docs\TDV-MODES-AND-REPORTS-FROM-THE-MANUALS.md</c>. The previous version used
    /// <c>CSI ? 40/66/67/68/69 h</c> on the authority of two uncited documents; four of those five
    /// numbers name a completely different switch in the manuals and the fifth was inverted.
    ///
    /// <para><b>One behaviour, one place</b></para>
    /// The wrap modes and the smooth scroll route into the flags
    /// <see cref="TerminalEmulatorBase"/> already keeps, rather than growing a second copy on the
    /// TDV side. That is the whole reason "NDBLWM" and "NDELWM" had drifted into meaning "blink"
    /// and "enhanced blink" - two dead fields nothing ever read, sitting beside working flags that
    /// did the real job.
    /// </remarks>
    protected virtual bool HandleNDPrivateSequence(char final, ReadOnlySpan<int> parameters, byte privateMarker)
    {
        // The message lamps come first, because they are not modes at all - they are three
        // sequences of their own that happen to share the '?' marker.
        //
        //     CSI ? n1 ; n2 ... A   NDCLED   clear   ND-1200 section 5.38
        //     CSI ? n1 ; n2 ... B   NDSLED   light   ND-1200 section 5.52
        //     CSI ? n1 ; n2 ... C   NDBLED   blink   ND-1200 section 5.37
        //
        // The parameters name the lamp - 0 all, 1 EXPAND, 2 APPEND, 3 BUSY, 4 MESSAGE - and the
        // default is 0. Nothing decoded these until 11 September 2026: the lamp state existed and
        // the virtual keyboard drew it, but no host could reach it.
        if (privateMarker == (byte)'?' && (final == 'A' || final == 'B' || final == 'C'))
        {
            return HandleMessageLampSequence(final, parameters);
        }

        var mode = parameters.Length > 0 ? parameters[0] : 0;
        var enable = final == 'h'; // 'h' = set, 'l' = reset

        // ND private modes, ND-1200 section 5.64 third table: CSI > n h / CSI > n l.
        if (privateMarker == (byte)'>')
        {
            switch (mode)
            {
                case 1: // NDBLWM - Beginning of Line Wrap. SET wraps to the preceding line.
                    ReverseWrapMode = enable;
                    return true;

                case 2: // NDELWM - End of Line Wrap. SET wraps, RESET stops at the margin.
                    SetEndOfLineWrap(enable);
                    return true;

                case 3: // Roll/page mode. RESET is ROLL, SET is PAGE.
                    PageMode = enable;
                    return true;

                case 4: // Key click. RESET is DISABLE, SET is ENABLE - and the 2215's own number
                        // for this switch runs the OTHER WAY. See the remarks on KeyClickEnabled.
                    KeyClickEnabled = enable;
                    return true;

                case 5: // PUSH-key label mode. RESET is labels ON, SET is labels OFF.
                    PushKeyLabelsHidden = enable;
                    return true;

                case 6: // PROGRAM-key label mode. RESET is labels ON, SET is labels OFF.
                    ProgramKeyLabelsHidden = enable;
                    return true;

                case 7: // Decimal separator mode. THREE positions, not two - see the remarks.
                    StepDecimalSeparatorMode(enable);
                    return true;

                default:
                    return false;
            }
        }

        switch (mode)
        {
            case 31: // BOL - Beginning of Line Wrap, 2215 section 8.7.1. The 2215's number for NDBLWM.
                ReverseWrapMode = enable;
                return true;

            case 36: // EOL - End of Line Wrap, 2215 section 8.7.1. A real TDV2200 termcap sets this.
                SetEndOfLineWrap(enable);
                return true;

            case 32: // CR - Cursor Return, 2215 section 8.7.1. RESET is CR, SET is CRLF.
                     //
                     // That is LNM's meaning under the 2215's own number: whether a carriage
                     // return also moves down a line. DEC calls it mode 20 and this program
                     // already keeps the flag, so the two numbers meet on one state rather than
                     // this model growing a second copy that could disagree with the first.
                NewLineMode = enable;
                return true;

            case 47: // RPM - Roll/Page Mode, 2215 section 8.7.1. RESET is ROLL, SET is PAGE.
                     // Same switch as ND private mode 3, and the same way round.
                PageMode = enable;
                return true;

            case 53: // KC - Key Click, 2215 section 8.7.1. RESET is ON, SET is OFF.
                     //
                     // THE POLARITY IS INVERTED HERE, and it is not a slip. Section 8.7.1's table
                     // gives 53 as RM = ON, 1.SM = OFF, while ND-1200 section 5.64 gives its own
                     // mode 4 as RESET = DISABLE, SET = ENABLE. Two manuals, one switch, opposite
                     // directions, so each sequence is written with ITS OWN manual's polarity and
                     // they meet in one flag.
                KeyClickEnabled = !enable;
                return true;

            case 55: // AR - Auto Repeat, 2215 section 8.7.1. RM is ON, 1.SM is OFF.
                     //
                     // Inverted, like the key click above, and for the same reason: the 2215's
                     // table names the ordinary state first. DEC's own number for this switch is
                     // private mode 8, DECARM, where SET means repeat - so the two run opposite
                     // ways and both are obeyed.
                AutoRepeatKeys = !enable;
                return true;

            case 60: // RT - Roll Type, 2215 section 8.7.1. RESET is STEP, SET is SMOOTH.
                SmoothScrollMode = enable;
                return true;

            // EC - Extended Control, 2215 section 8.7.1 and section 3.1; the same switch is
            // ND-1200 section 5.64's "66: 2115 mode".
            //
            // THE POLARITY IS THE OPPOSITE OF WHAT THIS PROGRAM USED TO ASSUME, and it is not an
            // inference. 2215 section 3.1: "When this switch is set to OFF, the terminal works like
            // a TDV 2115 from the host computer's point of view." ND-1200's table gives RESET as
            // "2115 codes" and SET as "N/A", and its section 8.1 spells it out: enter with
            // CSI 66 l, leave with ESC Q.
            //
            // A real TDV2200 termcap proves it independently. Its init string is
            //     ESC [ 62;36;66 l   ESC Q   ESC [ 36;62;62 h   ESC [ 0 m
            // - reset 66, then IMMEDIATELY ESC Q, which is the manual's documented way OUT of 2115
            // mode and is meaningless unless the byte before it went in. A second termcap does the
            // mirror image: ESC Q then ESC [ 66 h.
            case 66:
                Handle2115CompatibilityMode(!enable);
                return true;

            case 62: // GRM - Graphic Rendition Mode. RESET is ATTR, 1.SM is UNDERLINE, 2.SM is SGR.
                StepGraphicRenditionMode(enable);
                return true;

            case 68: // CT - Cursor Type, 2215 section 8.7.1. RESET is LINE, SET is BLOCK.
                     //
                     // Whether the cursor BLINKS is a different switch - the manual puts it among
                     // the convenience options that NDRQ report type 3 covers - so the blink of
                     // whatever style is current is carried across rather than reset here.
                SetCursorTypeFromModeSixtyEight(enable);
                return true;

            case 80: // Numeric pad: SET is "Function" mode, RESET is "Numeric".
                     // TDV 2200/9 S User's Guide section 11.2. PED's exit ritual resets it.
                NumericPadFunctionMode = enable;
                return true;

            default:
                return false;
        }
    }

    /// <summary>
    /// NDCLED, NDSLED and NDBLED - the three operations on the four message lamps.
    /// </summary>
    /// <param name="final">
    /// 'A' to clear, 'B' to light, 'C' to blink.
    /// </param>
    /// <param name="parameters">
    /// The lamps: 1 EXPAND, 2 APPEND, 3 BUSY, 4 MESSAGE, or 0 for all. Empty means 0.
    /// </param>
    /// <returns>
    /// True - the sequence is always this terminal's.
    /// </returns>
    /// <remarks>
    /// EVERY parameter is acted on, not just the first. The manual's own shape is
    /// <c>CSI ? n1 ; n2 ... B</c>, a list, so "light lamps 3 and 4" is one sequence - and acting on
    /// the first number alone is the exact fault HandleModeList was written to fix elsewhere.
    ///
    /// A number outside 0 to 4 is ignored by TDVMessageLEDs. Section 5.52 says the whole sequence
    /// should be ignored and a parameter error raised; this program latches no error conditions
    /// anywhere yet, so the lamps that were named still change. That is a smaller lie than
    /// pretending the error was recorded.
    /// </remarks>
    private bool HandleMessageLampSequence(char final, ReadOnlySpan<int> parameters)
    {
        if (parameters.Length == 0)
        {
            ApplyLampOperation(final, 0);
            return true;
        }

        for (int i = 0; i < parameters.Length; i++)
        {
            ApplyLampOperation(final, parameters[i]);
        }

        return true;
    }

    /// <summary>
    /// Applies one lamp operation to one lamp number.
    /// </summary>
    /// <param name="final">
    /// 'A' to clear, 'B' to light, 'C' to blink.
    /// </param>
    /// <param name="lamp">
    /// The lamp number, 1 to 4, or 0 for all.
    /// </param>
    private void ApplyLampOperation(char final, int lamp)
    {
        switch (final)
        {
            case 'A':
                MessageLEDs.ClearLamp(lamp);
                break;

            case 'B':
                MessageLEDs.Light(lamp);
                break;

            case 'C':
                MessageLEDs.Blink(lamp);
                break;
        }
    }

    /// <summary>
    /// PAGE rather than ROLL: what the screen does when the cursor leaves the last line.
    /// </summary>
    /// <remarks>
    /// ND Display Terminal 1200 section 5.64 ND private mode 3, and the same switch again as 2215
    /// section 8.7.1's ANSI mode 47, RPM. Both give RESET as ROLL and SET as PAGE, so both
    /// sequences meet here without any polarity argument.
    ///
    /// <para><b>State and report only, for now</b></para>
    /// Nothing reads this yet - the screen still rolls. What PAGE operation does to scrolling on a
    /// real TDV is a behaviour change worth its own piece of work, and inventing it would be
    /// exactly the fault the mode alignment of 11 September 2026 exists to undo. Setting it and
    /// reading it back through NDRQ report 2 works today, which is what a host asks for first.
    /// </remarks>
    public bool PageMode { get; protected set; }

    /// <summary>
    /// Whether the keyboard clicks when a key is pressed.
    /// </summary>
    /// <remarks>
    /// <para><b>Two manuals, one switch, opposite directions</b></para>
    /// ND-1200 section 5.64 gives ND private mode 4 as RESET = DISABLE, SET = ENABLE. TDV 2215
    /// section 8.7.1 gives ANSI mode 53, KC, as RM = ON, 1.SM = OFF. So <c>CSI > 4 h</c> turns
    /// the click ON and <c>CSI 53 h</c> turns it OFF, and both are correct for the terminal that
    /// documents them. Each handler is written with its own manual's polarity.
    ///
    /// <para><b>Nothing makes a sound</b></para>
    /// A key click is a local noise the keyboard makes; nothing here plays one, and whether a beep
    /// is even audible is on the list of things this program cannot verify without Ronny. The flag
    /// is held and reported.
    /// </remarks>
    public bool KeyClickEnabled { get; protected set; }

    /// <summary>
    /// Whether the PUSH-key labels are hidden.
    /// </summary>
    /// <remarks>
    /// ND-1200 section 5.64, ND private mode 5. Named for the DEPARTURE, like the keyboard mode
    /// flags: the manual's RESET is labels ON, which is the ordinary state, and SET hides them.
    /// A flag called "PushKeyLabels" would have to start true and read backwards in the report.
    ///
    /// Held and reported. The virtual keyboard draws the labels and is a standing DO-NOT, so
    /// nothing here reaches into it.
    /// </remarks>
    public bool PushKeyLabelsHidden { get; protected set; }

    /// <summary>
    /// Whether the PROGRAM-key labels are hidden.
    /// </summary>
    /// <remarks>
    /// ND-1200 section 5.64, ND private mode 6. Same shape as
    /// <see cref="PushKeyLabelsHidden"/> - RESET shows them, SET hides them.
    /// </remarks>
    public bool ProgramKeyLabelsHidden { get; protected set; }

    /// <summary>
    /// Which character the numeric pad's decimal key stands for.
    /// </summary>
    /// <remarks>
    /// ND Display Terminal 1200 section 5.64, ND private mode 7. It is one of the few TDV modes
    /// with THREE positions rather than two, and the manual draws the walk:
    /// <para>
    /// PERIOD - SM -> COMMA - SM -> SEQUENCE, and RM from any of them returns to PERIOD.
    /// </para>
    /// So a second <c>CSI > 7 h</c> does not repeat the first - it advances again. The 2215
    /// describes the same shape for its multi-position soft switches in section 8.7.1: "each SM
    /// increments the setting until it reaches its highest value, where it stays until RM".
    ///
    /// <para><b>What it does NOT do here</b></para>
    /// It holds the state and reports it, and nothing more. What the numeric pad's decimal key
    /// TRANSMITS lives in TDV2200KeyRegistry, which is keyboard territory - see
    /// <c>docs\TDV-MODES-AND-REPORTS-FROM-THE-MANUALS.md</c> section 8, where the photographs of
    /// four real keyboards are read off. A host can set this mode and read it back, which is what
    /// NDRQ report 2 needs.
    /// </remarks>
    public enum TDVDecimalSeparator
    {
        /// <summary>
        /// A full stop. The power-up setting.
        /// </summary>
        Period = 0,

        /// <summary>
        /// A comma, which is what a Nordic machine wants.
        /// </summary>
        Comma = 1,

        /// <summary>
        /// The manual's third position, "SEQUENCE".
        /// </summary>
        Sequence = 2
    }

    /// <summary>
    /// The decimal separator the numeric pad stands for. ND private mode 7.
    /// </summary>
    public TDVDecimalSeparator DecimalSeparatorMode { get; protected set; }

    /// <summary>
    /// Walks the decimal-separator switch, or sends it home.
    /// </summary>
    /// <param name="set">
    /// True for SM, which advances one position and stops at SEQUENCE; false for RM, which
    /// returns to PERIOD from wherever it is.
    /// </param>
    private void StepDecimalSeparatorMode(bool set)
    {
        if (!set)
        {
            DecimalSeparatorMode = TDVDecimalSeparator.Period;
            return;
        }

        if (DecimalSeparatorMode == TDVDecimalSeparator.Period)
        {
            DecimalSeparatorMode = TDVDecimalSeparator.Comma;
        }
        else
        {
            // COMMA advances to SEQUENCE; SEQUENCE is the highest and stays there until RM.
            DecimalSeparatorMode = TDVDecimalSeparator.Sequence;
        }
    }

    /// <summary>
    /// How graphic rendition reaches the screen - the GRM switch.
    /// </summary>
    /// <remarks>
    /// TDV 2215 section 4.2.3 and the mode table in 8.7.1, where 62 has three positions: RM gives
    /// ATTR, the first SM gives UNDERLINE and the second gives SGR. ND-1200 section 5.64 lists the
    /// same number with only TWO, ATTRIBUTE and UNDERLINE - the models genuinely differ, and the
    /// three-position walk is kept because the 2215 and 2200 are what this program emulates.
    /// </remarks>
    public enum TDVGraphicRenditionMode
    {
        /// <summary>
        /// ATTR. Rendition changes are invisible attribute cells written with SO Y SI, each taking
        /// a screen position and governing everything up to the next one. SGR is IGNORED.
        /// </summary>
        Attribute = 0,

        /// <summary>
        /// UNDERLINE. The 2115's underline mode: SO underlines, SI returns to normal, and the
        /// Underline Representation switch picks what "underline" is drawn as.
        /// </summary>
        Underline = 1,

        /// <summary>
        /// SGR. Rendition is set the ECMA-48 way, with CSI Ps m.
        /// </summary>
        Sgr = 2
    }

    /// <summary>
    /// Which of the three ways graphic rendition is selected. See <see cref="TDVGraphicRenditionMode"/>.
    /// </summary>
    /// <remarks>
    /// <para><b>It starts on SGR, which is NOT the 2215's factory setting</b></para>
    /// The 2215's own switch listing has Graphic Rendition Mode set to ATTR. from the factory. This
    /// program starts on SGR because SGR is what it actually does: it honours CSI Ps m and treats
    /// SO and SI as character-set shifts. Starting on ATTR. would make the switch report a position
    /// the terminal does not behave like, which is the fault this whole piece of work exists to
    /// undo.
    ///
    /// <para><b>The switch is held, and the semantics are NOT implemented</b></para>
    /// On a real 2215, ATTR. makes SGR do nothing and turns SO Y SI into an attribute cell that
    /// occupies a position on the screen; UNDERLINE turns SO and SI into underline on and off; and
    /// changing the switch at all ERASES THE SCREEN (section 4.2.3). None of that happens here.
    /// Doing it properly means deciding what SO and SI mean in each position - they already have
    /// two meanings, character-set shift in native operation and underline in 2115 operation - and
    /// that is a decision with a visible effect on every TDV session, so it is Ronny's rather than
    /// mine.
    /// </remarks>
    public TDVGraphicRenditionMode GraphicRenditionMode { get; protected set; }
        = TDVGraphicRenditionMode.Sgr;

    /// <summary>
    /// Walks the GRM switch, or sends it home.
    /// </summary>
    /// <param name="set">
    /// True for SM, which advances one position and stops at SGR; false for RM, which returns to
    /// ATTR from wherever it is.
    /// </param>
    /// <remarks>
    /// Section 8.7.1: "for switches with more than two settings, each SM increments the setting
    /// until it reaches its highest value, where it stays until RM". Same shape as the decimal
    /// separator.
    /// </remarks>
    private void StepGraphicRenditionMode(bool set)
    {
        if (!set)
        {
            GraphicRenditionMode = TDVGraphicRenditionMode.Attribute;
            return;
        }

        if (GraphicRenditionMode == TDVGraphicRenditionMode.Attribute)
        {
            GraphicRenditionMode = TDVGraphicRenditionMode.Underline;
        }
        else
        {
            GraphicRenditionMode = TDVGraphicRenditionMode.Sgr;
        }
    }

    /// <summary>
    /// Applies the 2215's Cursor Type switch, keeping the cursor's blink as it was.
    /// </summary>
    /// <param name="block">
    /// True for BLOCK, the SET position; false for LINE, the RESET position.
    /// </param>
    /// <remarks>
    /// TDV 2215 section 8.7.1 mode 68, CT: RM is LINE and 1.SM is BLOCK. LINE is drawn here as the
    /// underline cursor, which is the shape DEC's own DECSCUSR calls 4.
    ///
    /// Blink is deliberately untouched. The 2215 keeps the cursor blink among the convenience
    /// switches - the ones NDRQ report type 3 covers - so a host changing the TYPE should not
    /// silently stop or start the blinking.
    /// </remarks>
    private void SetCursorTypeFromModeSixtyEight(bool block)
    {
        bool blinking = Cursor.Style == CursorStyle.BlinkingBlock
            || Cursor.Style == CursorStyle.BlinkingUnderline
            || Cursor.Style == CursorStyle.BlinkingBar;

        if (block)
        {
            Cursor.Style = blinking ? CursorStyle.BlinkingBlock : CursorStyle.Block;
        }
        else
        {
            Cursor.Style = blinking ? CursorStyle.BlinkingUnderline : CursorStyle.Underline;
        }

        OnInvalidated();
    }

    /// <summary>
    /// Sets or clears end-of-line wrap, keeping the cursor's own copy of the flag in step.
    /// </summary>
    /// <param name="wrap">
    /// True to wrap at the right margin, false to stop there.
    /// </param>
    private void SetEndOfLineWrap(bool wrap)
    {
        AutoWrapMode = wrap;
        Cursor.AutoWrap = wrap;
    }

    /// <summary>
    /// Handles 2115 compatibility mode
    /// </summary>
    protected virtual void Handle2115CompatibilityMode(bool enable)
    {
        // Override in derived classes for specific 2115 compatibility behavior
    }

    /// <summary>
    /// Handles NDAAR - Add Attribute in Rectangle, <c>CSI attr;top;left;bottom;right {</c>.
    /// </summary>
    /// <param name="parameters">
    /// The attribute followed by the four 0-indexed corners.
    /// </param>
    /// <remarks>
    /// Adds the attribute to the cells without clearing the ones already there. Lifted out of
    /// TDV2200Emulator on 11 September 2026 so the 2215 gets it too - ND Display Terminal 1200
    /// section 2.8 lists NDAAR, NDRAR and NDFC for the family, not for one model.
    /// </remarks>
    protected virtual void HandleAddAttributeInRectangle(ReadOnlySpan<int> parameters)
    {
        if (parameters.Length < 5) return;

        var attr = parameters[0];
        var top = Math.Max(0, parameters[1]);
        var left = Math.Max(0, parameters[2]);
        var bottom = Math.Max(0, parameters[3]);
        var right = Math.Max(0, parameters[4]);

        RectangleOperations.AddAttributeInRectangle(Buffer, attr, left, top, right, bottom);
        OnInvalidated();
    }

    /// <summary>
    /// Handles NDRAR - Remove Attribute in Rectangle, <c>CSI attr;top;left;bottom;right |</c>.
    /// </summary>
    /// <param name="parameters">
    /// The attribute followed by the four 0-indexed corners.
    /// </param>
    /// <remarks>
    /// The final byte is <c>|</c>, hex 7C. It was <c>}</c> here until 11 September 2026, swapped
    /// with NDFC - both the ND-1200 table in section 2.8 and the TDV 2200 CSI table give 7C as
    /// NDRAR and 7D as NDFC.
    /// </remarks>
    protected virtual void HandleRemoveAttributeInRectangle(ReadOnlySpan<int> parameters)
    {
        if (parameters.Length < 5) return;

        var attr = parameters[0];
        var top = Math.Max(0, parameters[1]);
        var left = Math.Max(0, parameters[2]);
        var bottom = Math.Max(0, parameters[3]);
        var right = Math.Max(0, parameters[4]);

        RectangleOperations.RemoveAttributeInRectangle(Buffer, attr, left, top, right, bottom);
        OnInvalidated();
    }

    /// <summary>
    /// Handles NDFC - Fill Character in Rectangle, <c>CSI char;top;left;bottom;right }</c>.
    /// </summary>
    /// <param name="parameters">
    /// The character code followed by the four 0-indexed corners.
    /// </param>
    /// <remarks>
    /// The final byte is <c>}</c>, hex 7D. See the remarks on HandleRemoveAttributeInRectangle for
    /// the swap this corrects.
    /// </remarks>
    protected virtual void HandleFillCharacter(ReadOnlySpan<int> parameters)
    {
        if (parameters.Length < 5) return;

        var charCode = parameters[0];
        var top = Math.Max(0, parameters[1]);
        var left = Math.Max(0, parameters[2]);
        var bottom = Math.Max(0, parameters[3]);
        var right = Math.Max(0, parameters[4]);

        for (int row = top; row <= bottom && row < Height; row++)
        {
            for (int col = left; col <= right && col < Width; col++)
            {
                ref var cell = ref Buffer[row, col];
                cell.Codepoint = (uint)charCode;
            }
        }

        OnInvalidated();
    }

    /// <summary>
    /// Handles NDSAR - Set Attribute in Rectangle
    /// TDV rectangle operations use 0-indexed coordinates
    /// </summary>
    protected virtual void HandleSetAttributeInRectangle(ReadOnlySpan<int> parameters)
    {
        if (parameters.Length < 5) return;

        var attr = parameters[0];
        // TDV rectangle operations use 0-indexed coordinates (unlike standard CSI)
        var row1 = Math.Max(0, parameters[1]);
        var col1 = Math.Max(0, parameters[2]);
        var row2 = Math.Max(0, parameters[3]);
        var col2 = Math.Max(0, parameters[4]);

        // SetAttributeInRectangle expects (attr, x1, y1, x2, y2) where x=column, y=row
        RectangleOperations.SetAttributeInRectangle(Buffer, attr, col1, row1, col2, row2);
        OnInvalidated();
    }

    /// <summary>
    /// Handles NDSREC - Save Rectangle
    /// CSI parameters are 1-indexed, converted to 0-indexed internally
    /// </summary>
    protected virtual void HandleSaveRectangle(ReadOnlySpan<int> parameters)
    {
        if (parameters.Length < 4) return;

        // CSI parameters are 1-indexed, convert to 0-indexed
        var row1 = Math.Max(0, parameters[0] - 1);
        var col1 = Math.Max(0, parameters[1] - 1);
        var row2 = Math.Max(0, parameters[2] - 1);
        var col2 = Math.Max(0, parameters[3] - 1);

        // SaveRectangle expects (x1, y1, x2, y2) where x=column, y=row
        RectangleOperations.SaveRectangle(Buffer, col1, row1, col2, row2);
    }

    /// <summary>
    /// Handles NDRREC - Restore Rectangle
    /// CSI parameters are 1-indexed, converted to 0-indexed internally
    /// </summary>
    protected virtual void HandleRestoreRectangle(ReadOnlySpan<int> parameters)
    {
        if (parameters.Length < 2) return;

        // CSI parameters are 1-indexed, convert to 0-indexed
        var row = Math.Max(0, parameters[0] - 1);
        var col = Math.Max(0, parameters[1] - 1);

        // RestoreRectangle expects (x, y) where x=column, y=row
        RectangleOperations.RestoreRectangle(Buffer, col, row);
        OnInvalidated();
    }

    /// <summary>
    /// Handles NDDWA - Define Work Area
    /// TDV rectangle operations use 0-indexed coordinates
    /// </summary>
    protected virtual void HandleDefineWorkArea(ReadOnlySpan<int> parameters)
    {
        if (parameters.Length < 4) return;

        // TDV rectangle operations use 0-indexed coordinates (unlike standard CSI)
        var row1 = Math.Max(0, parameters[0]);
        var col1 = Math.Max(0, parameters[1]);
        var row2 = Math.Max(0, parameters[2]);
        var col2 = Math.Max(0, parameters[3]);

        // DefineWorkArea expects (x1, y1, x2, y2) where x=column, y=row
        WorkAreas.DefineWorkArea(col1, row1, col2, row2);
    }

    /// <summary>
    /// Handles NDVIDEO - Alpha/Graphics toggle
    /// </summary>
    protected virtual void HandleVideoToggle(ReadOnlySpan<int> parameters)
    {
        if (parameters.Length < 1) return;

        var mode = parameters[0];
        // Override in derived classes for graphics support
    }

    /// <summary>
    /// Handles escape sequences with intermediates (ESC # for double-width, ESC ( for character sets)
    /// </summary>
    protected override void HandleEscapeWithIntermediates(char final, ReadOnlySpan<byte> intermediates)
    {
        // ESC # (DECDHL/DECDWL/DECSWL) falls through to the base, which owns it for every
        // terminal now rather than only the TDV models.
        if (intermediates.Length == 1 && intermediates[0] == (byte)'#')
        {
            base.HandleEscapeWithIntermediates(final, intermediates);
            return;
        }

        // Handle TDV character set switching: ESC ( 0-9
        // This designates character sets to G0 (the primary character set)
        if (intermediates.Length == 1 && intermediates[0] == (byte)'(')
        {
            if (final >= '0' && final <= '9')
            {
                var setNumber = final - '0';
                var characterSetType = (TDVCharacterSets.TDVCharacterSetType)setNumber;
                SetCharacterSet(0, characterSetType); // Designate to G0

                if (ApplicationLogger.IsEnabled(LogCategory.General, LogLevel.Info))
                {
                    var logMsg = $"[TDV Emulator] Character set switched: ESC({final} -> G0={characterSetType}";
                    System.Diagnostics.Debug.WriteLine(logMsg);
                    ApplicationLogger.Log(logMsg);
                }
                return;
            }
        }

        // Handle ESC ) 0-9 for G1 character set
        if (intermediates.Length == 1 && intermediates[0] == (byte)')')
        {
            if (final >= '0' && final <= '9')
            {
                var setNumber = final - '0';
                var characterSetType = (TDVCharacterSets.TDVCharacterSetType)setNumber;
                SetCharacterSet(1, characterSetType); // Designate to G1

                if (ApplicationLogger.IsEnabled(LogCategory.General, LogLevel.Info))
                {
                    var logMsg = $"[TDV Emulator] Character set switched: ESC){final} -> G1={characterSetType}";
                    System.Diagnostics.Debug.WriteLine(logMsg);
                    ApplicationLogger.Log(logMsg);
                }
                return;
            }
        }

        base.HandleEscapeWithIntermediates(final, intermediates);
    }

    /// <summary>
    /// Formats intermediate bytes as "0xNN,0xNN" for a log line. Diagnostics only —
    /// call it INSIDE an <see cref="ApplicationLogger.IsEnabled"/> guard, never before one.
    /// Written as a plain loop because the project forbids LINQ.
    /// </summary>
    private static string FormatIntermediates(ReadOnlySpan<byte> intermediates)
    {
        if (intermediates.Length == 0)
        {
            return string.Empty;
        }

        // At most MaxIntermediates (2) entries, so a small StringBuilder is plenty.
        var sb = new StringBuilder(intermediates.Length * 5);
        for (int i = 0; i < intermediates.Length; i++)
        {
            if (i > 0)
            {
                sb.Append(',');
            }
            sb.Append("0x").Append(intermediates[i].ToString("X2"));
        }
        return sb.ToString();
    }

    /// <summary>
    /// Handles escape sequences (2-character ESC sequences)
    /// </summary>
    protected override void HandleEscapeSequence(EscapeSequenceParser parser)
    {
        var final = (char)parser.FinalByte;
        var intermediates = parser.Intermediates;

        // GUARD BEFORE BUILDING. This runs on EVERY escape sequence, i.e. squarely in the
        // receive hot path. It used to build the message unconditionally — a ToArray(), a
        // LINQ Select with a closure, a string.Join and two interpolations per ESC, even
        // with logging switched off. ApplicationLogger's own contract requires IsEnabled
        // to be checked before the message is composed.
        if (ApplicationLogger.IsEnabled(LogCategory.General, LogLevel.Info))
        {
            var logMsg = $"[TDV Emulator] HandleEscapeSequence called: final='{final}' (0x{(int)final:X2}), intermediates=[{FormatIntermediates(intermediates)}]";
            System.Diagnostics.Debug.WriteLine(logMsg);
            ApplicationLogger.Log(logMsg);
        }

        // Handle ESC Z (Terminal Identification)
        if (intermediates.Length == 0 && final == 'Z')
        {
            var response = HandleTerminalIdentification();

            if (ApplicationLogger.IsEnabled(LogCategory.General, LogLevel.Info))
            {
                var logMsg2 = "[TDV Emulator] ESC Z (Terminal Identification) query detected";
                System.Diagnostics.Debug.WriteLine(logMsg2);
                ApplicationLogger.Log(logMsg2);

                var logMsg3 = $"[TDV Emulator] Terminal ID response: {BitConverter.ToString(Encoding.UTF8.GetBytes(response))} ({response})";
                System.Diagnostics.Debug.WriteLine(logMsg3);
                ApplicationLogger.Log(logMsg3);
            }

            SendResponse(response);
            return;
        }

        // Handle ESC Q - leave 2115 compatibility mode. Shared by every TDV model because
        // 2115 mode behaves identically on all of them; TDV2215 additionally turns on
        // extended mode, which it does in its own override before delegating here.
        if (intermediates.Length == 0 && final == 'Q')
        {
            Handle2115CompatibilityMode(false);
            return;
        }

        // Handle SS2/SS3 - these are handled by derived classes
        // Call base class for other ESC sequences (including RIS)
        base.HandleEscapeSequence(parser);
    }

    // ESC # (DECDHL/DECDWL/DECSWL) is handled by TerminalEmulatorBase.HandleLineSize now.
    // HandleDoubleWidthHeight, SetLineHeight and SetLineWidth used to live here, which meant
    // only the TDV models honoured the DEC line-size sequences even though nothing about them
    // is TDV-specific - the old implementation cited the VT220 spec in its own comment.

    /// <summary>
    /// TDV terminals use bitmap fonts - NO Unicode mapping.
    /// Character code goes directly to buffer, fontNum selects glyph set.
    /// </summary>
    protected override uint ApplyCharacterSetMapping(uint codepoint, byte characterSet)
    {
        // TDV uses bitmap fonts with fontNum-based glyph selection
        // No Unicode/ISO646 mapping - pass through unchanged
        return codepoint;
    }

    /// <summary>
    /// Handles smooth scrolling when enabled
    /// </summary>
    protected override void ScrollUp()
    {
        if (SmoothScrollMode)
        {
            // For smooth scrolling, we need to trigger a gradual animation
            // This is handled by the renderer, which will animate the scroll
            // For now, we just mark that a smooth scroll is needed
            TriggerSmoothScroll(true);
        }

        base.ScrollUp();
    }

    /// <summary>
    /// Handles smooth scrolling when enabled
    /// </summary>
    protected override void ScrollDown()
    {
        if (SmoothScrollMode)
        {
            // For smooth scrolling, we need to trigger a gradual animation
            // This is handled by the renderer, which will animate the scroll
            // For now, we just mark that a smooth scroll is needed
            TriggerSmoothScroll(false);
        }

        base.ScrollDown();
    }

    /// <summary>
    /// Triggers smooth scroll animation (to be handled by renderer)
    /// </summary>
    protected virtual void TriggerSmoothScroll(bool scrollUp)
    {
        // This would typically raise an event that the renderer listens to
        // For now, we just invalidate the display
        OnInvalidated();
    }

    /// <summary>
    /// Checks if cursor is in a protected area
    /// </summary>
    protected virtual bool IsCursorInProtectedArea()
    {
        return ProtectedAreas.IsProtected(Cursor.Row, Cursor.Column);
    }

    /// <summary>
    /// Checks if cursor is within current work area
    /// </summary>
    protected virtual bool IsCursorInWorkArea()
    {
        var (left, top, right, bottom) = WorkAreas.GetCurrentWorkArea();
        return Cursor.Column >= left && Cursor.Column <= right &&
               Cursor.Row >= top && Cursor.Row <= bottom;
    }

    /// <summary>
    /// Handles PUSH key programming
    /// </summary>
    public virtual void ProgramPushKey(int keyNumber, string sequence)
    {
        PushKeys.ProgramKey(keyNumber, sequence);
    }

    /// <summary>
    /// Executes PUSH key sequence
    /// </summary>
    public virtual void ExecutePushKey(int keyNumber)
    {
        var sequence = PushKeys.GetKeySequence(keyNumber);
        if (!string.IsNullOrEmpty(sequence))
        {
            var data = Encoding.UTF8.GetBytes(sequence);
            ProcessData(data);
        }
    }

    /// <summary>
    /// Sets one message lamp, the way NDSLED, NDBLED and NDCLED do.
    /// </summary>
    /// <param name="lamp">
    /// The lamp: 1 EXPAND, 2 APPEND, 3 BUSY, 4 MESSAGE, or 0 for all of them.
    /// </param>
    /// <param name="state">
    /// Off, lit or blinking.
    /// </param>
    /// <remarks>
    /// The old signature took a TDVMessageLEDType of Clear, Set or Blink and a bool, which named
    /// the three OPERATIONS as if they were the lamps. ND-1200 section 5.52 has four lamps.
    /// </remarks>
    public virtual void SetMessageLamp(int lamp, TDVMessageLampState state)
    {
        switch (state)
        {
            case TDVMessageLampState.Lit:
                MessageLEDs.Light(lamp);
                break;

            case TDVMessageLampState.Blinking:
                MessageLEDs.Blink(lamp);
                break;

            default:
                MessageLEDs.ClearLamp(lamp);
                break;
        }
    }

    /// <summary>
    /// Gets current character set
    /// </summary>
    public virtual byte GetCurrentCharacterSet()
    {
        return _invokedCharacterSet;
    }

    /// <summary>
    /// Sets character set designation (G0-G3)
    /// </summary>
    public virtual void SetCharacterSet(byte setIndex, TDVCharacterSets.TDVCharacterSetType setType)
    {
        switch (setIndex)
        {
            case 0:
                _g0CharacterSet = setType;
                break;
            case 1:
                _g1CharacterSet = setType;
                break;
            case 2:
                _g2CharacterSet = setType;
                break;
            case 3:
                _g3CharacterSet = setType;
                break;
        }
    }

    /// <summary>
    /// Invokes a character set (locking shift)
    /// </summary>
    public virtual void InvokeCharacterSet(byte setIndex)
    {
        if (setIndex >= 0 && setIndex <= 3)
        {
            _invokedCharacterSet = setIndex;
        }
    }

    /// <summary>
    /// Gets the currently active character set type
    /// </summary>
    public virtual TDVCharacterSets.TDVCharacterSetType GetActiveCharacterSetType()
    {
        return _invokedCharacterSet switch
        {
            0 => _g0CharacterSet,
            1 => _g1CharacterSet,
            2 => _g2CharacterSet,
            3 => _g3CharacterSet,
            _ => TDVCharacterSets.TDVCharacterSetType.GraphicsI
        };
    }

    /// <summary>
    /// Gets the G0 character set type
    /// </summary>
    public TDVCharacterSets.TDVCharacterSetType GetG0CharacterSet() => _g0CharacterSet;

    /// <summary>
    /// Gets the G1 character set type
    /// </summary>
    public TDVCharacterSets.TDVCharacterSetType GetG1CharacterSet() => _g1CharacterSet;

    /// <summary>
    /// Gets the G2 character set type
    /// </summary>
    public TDVCharacterSets.TDVCharacterSetType GetG2CharacterSet() => _g2CharacterSet;

    /// <summary>
    /// Gets the G3 character set type
    /// </summary>
    public TDVCharacterSets.TDVCharacterSetType GetG3CharacterSet() => _g3CharacterSet;

    #region Helper Methods (Shared across TDV models)

    /// <summary>
    /// Erase current line from cursor to end
    /// </summary>
    public void EraseCurrentLine()
    {
        var currentLine = Cursor.Row;
        var currentCol = Cursor.Column;

        // Erase from cursor to end of line
        for (int col = currentCol; col < Width; col++)
        {
            var cell = Buffer.GetCell(currentLine, col);
            cell.Codepoint = ' ';
            cell.Attributes = CharacterAttributes.None;
            Buffer.SetCell(currentLine, col, cell);
        }
        OnInvalidated();
    }

    /// <summary>
    /// Erase entire page/screen
    /// </summary>
    public void ErasePage()
    {
        // Clear entire buffer
        Buffer.Clear();
        OnInvalidated();
    }

    /// <summary>
    /// Roll up (scroll up/page up)
    /// </summary>
    public void RollUp()
    {
        // Scroll up one line
        Buffer.ScrollUp();
        OnInvalidated();
    }

    /// <summary>
    /// Roll down (scroll down/page down)
    /// </summary>
    public void RollDown()
    {
        // Scroll down one line
        Buffer.ScrollDown(0, Height - 1);
        OnInvalidated();
    }

    /// <summary>
    /// Move cursor to start of current line
    /// </summary>
    public void CursorToStartOfLine()
    {
        Cursor.Column = 0;
    }

    /// <summary>
    /// Move cursor to home position (0,0)
    /// </summary>
    public void CursorHome()
    {
        Cursor.Row = 0;
        Cursor.Column = 0;
    }

    #endregion

    /// <summary>
    /// Routes host data through the TDV-specific input path.
    /// </summary>
    /// <param name="data">
    /// Bytes received from the host.
    /// </param>
    /// <remarks>
    /// <para>
    /// CRITICAL: without this override, <see cref="TerminalEmulatorBase.ProcessData"/> feeds the
    /// escape-sequence parser directly and the entire TDV input pipeline
    /// (TDVInputProcessor - ISO 646 variant selection, SS2/SS3 single shifts, DLE cursor
    /// addressing, 2115 control codes) is never reached on the live network path.
    /// TerminalSession calls ProcessData, not ProcessInput, so all of that TDV-specific
    /// handling was effectively dead code for real sessions.
    /// </para>
    /// <para>
    /// This surfaced as the TDV-native (SINTRAN terminal type 53) DLE cursor-addressing bug:
    /// <c>DLE row col</c> was dropped and its two coordinate bytes were painted on screen as
    /// high-bit garbage.
    /// </para>
    /// <para>
    /// ProcessInput raises Invalidated itself, so this does not need to.
    /// </para>
    /// </remarks>
    public override void ProcessData(ReadOnlySpan<byte> data)
    {
        ProcessInput(data);
    }

    /// <summary>
    /// Processes input data with TDV-specific handling
    /// Note: Derived classes should override this to use component-based input processors
    /// </summary>
    // ConsumeGraphicsByte used to be declared here. It moved up to TerminalEmulatorBase when the
    // Tektronix 4014 became a terminal in its own right: two families with graphics means the hook
    // cannot belong to one of them. A 1200 or a 2215 still inherits the "no graphics" default and
    // goes on treating every byte as text.

    public override void ProcessInput(ReadOnlySpan<byte> data)
    {
        // This class overrides ProcessData to come straight here, so the base never gets a chance
        // to set this — a TDV line has to be told here that 0xA0+ is data, not a UTF-8 lead byte.
        ApplyTransportEncodingToParser();

        // Batched for the same reason the base class batches (see below): one read from the host carries many
        // sequences, and a TDV host repainting a form sends a LOT of them - the 2200 alone raises
        // Invalidated from ten places. Without this, each one copied the whole screen and posted
        // its own repaint. See TerminalEmulatorBase.BeginBatch.
        BeginBatch();
        try
        {
            InputProcessor.ProcessInput(data);
            OnInvalidated();
        }
        finally
        {
            EndBatch();
        }
    }

    #region Query/Response Support

    /// <summary>
    /// Handles device attributes query (Primary DA)
    /// </summary>
    protected virtual string HandleDeviceAttributesQuery()
    {
        // Return VT100 compatible response
        return "\x1b[?1;2c";
    }

    /// <summary>
    /// Handles secondary device attributes query
    /// </summary>
    protected virtual string HandleSecondaryDeviceAttributesQuery()
    {
        // Return model-specific version info
        return "\x1b[?1;0;0c";
    }

    /// <summary>
    /// Handles cursor position report query
    /// </summary>
    protected virtual string HandleCursorPositionReport()
    {
        return $"\x1b[{Cursor.Row + 1};{Cursor.Column + 1}R";
    }

    /// <summary>
    /// Handles device status report query
    /// </summary>
    protected virtual string HandleDeviceStatusReport()
    {
        // Always return ready status
        return "\x1b[0n";
    }

    /// <summary>
    /// Handles terminal identification query
    /// </summary>
    protected virtual string HandleTerminalIdentification()
    {
        return "\x1b[?1;0c";
    }

    // HandleModeQuery and GetModeState used to sit here, answering DECRQM with a table of five
    // TDV mode numbers.
    //
    // BOTH ARE GONE, 11 September 2026, because no TDV manual has DECRQM and because every one of
    // those five numbers named a different switch in the manuals that do exist. The sequence
    // CSI ? Ps $ p appears nowhere in the TDV 2215 Functional Specifications - section 8.7's table
    // of accepted CSI sequences is complete and has no '$' intermediate at all - nor in the TDV
    // 2200/9 S delta list, nor in the ND Display Terminal 1200 specification. It reaches this
    // repository only through spec\DEC\xterm-ctlseqs.txt.
    //
    // What a TDV really answers, and what this program does NOT yet implement, is NDRQ:
    // ND-1200 section 5.48, "CSI Ps x" requesting and "CSI Ps ; n1 ; ... x" reporting, with report
    // type 2 giving all the mode switches as three bitmasks. It is on the plan.
    //
    // DECRQM still WORKS on a TDV here, because TerminalEmulatorBase answers it for every emulator
    // and nothing suppresses it. That is deliberate and it is OUR extension, not Tandberg's: the
    // numbers it answers about are DEC's own DECSET numbers and the values are DEC's DECRPM table -
    // 0 not recognised, 1 set, 2 reset. It is not a claim about what a real Tandberg would say.
    // See docs\TDV-MODES-AND-REPORTS-FROM-THE-MANUALS.md.

    /// <summary>
    /// Gets whether 2115 compatibility mode is active (override in derived classes)
    /// </summary>
    protected virtual bool Get2115CompatibilityMode()
    {
        return false; // Base implementation - override in TDV1200
    }

    /// <summary>
    /// Handles work area query
    /// </summary>
    protected virtual string HandleWorkAreaQuery()
    {
        var (left, top, right, bottom) = WorkAreas.GetCurrentWorkArea();
        return $"\x1b[?1;{left};{top};{right};{bottom}$y";
    }

    // HandleLEDStatusQuery and HandleProtectedAreaQuery were deleted on 11 September 2026.
    //
    // Nothing called either of them. Both answered with a "CSI ? ... $ y" reply shape, which comes
    // from the same uncited documents that gave this program DECRQM on a TDV - no TDV manual has a
    // dollar intermediate anywhere. If a real lamp or protected-area report is ever needed, NDRQ is
    // the sequence a TDV actually has: see BuildTerminalParameterReport.

    /// <summary>
    /// Handles PUSH key query
    /// </summary>
    protected virtual string HandlePUSHKeyQuery(int keyNumber)
    {
        var sequence = PushKeys.GetKeySequence(keyNumber);
        if (string.IsNullOrEmpty(sequence))
        {
            return $"\x1b[?0;{keyNumber}$y";
        }

        // Return key definition (encoded)
        var encoded = EncodeSequence(sequence);
        return $"\x1b[?1;{keyNumber};{encoded}$y";
    }

    /// <summary>
    /// Encodes a sequence for transmission
    /// </summary>
    protected virtual string EncodeSequence(string sequence)
    {
        // Simple encoding - replace special characters
        return sequence.Replace("\x1b", "\\e")
                      .Replace("\r", "\\r")
                      .Replace("\n", "\\n")
                      .Replace("\t", "\\t");
    }

    /// <summary>
    /// Processes query sequences and sends responses
    /// </summary>
    protected override void HandleCsiSequence(EscapeSequenceParser parser)
    {
        var final = (char)parser.FinalByte;
        var parameters = parser.Parameters;
        var privateMarker = parser.PrivateMarker;

        // Handle query sequences first
        if (HandleQuerySequence(final, parameters, privateMarker, parser))
        {
            return;
        }

        // Handle ND-specific sequences
        if (HandleNDSpecificSequence(final, parameters, privateMarker))
        {
            return;
        }

        // Fall back to base class handling
        base.HandleCsiSequence(parser);
    }

    /// <summary>
    /// Handles query sequences
    /// </summary>
    protected virtual bool HandleQuerySequence(char final, ReadOnlySpan<int> parameters, byte privateMarker, EscapeSequenceParser parser)
    {
        // REMOVED VERBOSE LOGGING - only log actual query/response exchanges below

        switch (final)
        {
            case 'c': // Device Attributes
                if (privateMarker == 0)
                {
                    // Primary DA
                    var response = HandleDeviceAttributesQuery();
                    SendResponse(response);
                    return true;
                }
                else if (privateMarker == (byte)'>')
                {
                    // Secondary DA
                    var response = HandleSecondaryDeviceAttributesQuery();
                    SendResponse(response);
                    return true;
                }
                break;

            case 'n': // Device Status Report
                if (parameters.Length > 0 && parameters[0] == 6)
                {
                    // Cursor Position Report
                    var response = HandleCursorPositionReport();
                    SendResponse(response);
                    return true;
                }
                else if (parameters.Length > 0 && parameters[0] == 5)
                {
                    // Device Status Report
                    var response = HandleDeviceStatusReport();
                    SendResponse(response);
                    return true;
                }
                break;

            // DECRQM used to be claimed here, with a table of TDV mode numbers that were all wrong.
            // It is NOT claimed any more: TerminalEmulatorBase answers CSI ? Ps $ p for every
            // emulator, using DEC's own mode numbers and DEC's DECRPM values, and that is a better
            // answer than an invented one. Nothing in any TDV manual describes the sequence.
            //
            // NDRQ is what a TDV really answers, and it is claimed here.
            case 'x':
                // No private marker and no intermediate. The '$' form of 'x' is DECFRA, the VT420
                // rectangle fill, which TerminalEmulatorBase owns - hence both guards.
                if (privateMarker == 0 && parser.Intermediates.Length == 0)
                {
                    int reportType = parameters.Length > 0 ? parameters[0] : 1;
                    SendResponse(BuildTerminalParameterReport(reportType));
                    return true;
                }
                break;
        }

        // REMOVED VERBOSE LOGGING - only log when we actually find and process a query
        return false;
    }

    /// <summary>
    /// Builds the NDRQ reply for one report type.
    /// </summary>
    /// <param name="reportType">
    /// The report type the host asked for, from the first parameter of <c>CSI Ps x</c>.
    /// </param>
    /// <returns>
    /// The reply byte string, always ending in <c>x</c>.
    /// </returns>
    /// <remarks>
    /// <para><b>NDRQ - Request and Report Terminal Parameters</b></para>
    /// ND Display Terminal 1200 Functional Specifications section 5.48. The host sends
    /// <c>CSI Ps x</c> and the terminal answers <c>CSI Ps ; n1 ; ... ; nn x</c>. The default
    /// report type is 1.
    ///
    /// <para><b>This is the report a TDV actually has</b></para>
    /// Until 11 September 2026 this program answered DECRQM on a TDV instead, with five mode
    /// numbers that each named a different switch in the manuals. DECRQM appears in no TDV manual
    /// at all. NDRQ is the sequence the ND documentation describes, and this is it.
    ///
    /// <para><b>NDRQ and DEC's DECREQTPARM are the SAME sequence shape - watch for this</b></para>
    /// DEC's Request Terminal Parameters is also <c>CSI Ps x</c> with no intermediate, answering
    /// <c>CSI Ps ; ... x</c>. The two are told apart by nothing at all on the wire; only the
    /// terminal's identity decides. This program does not implement DECREQTPARM anywhere, so
    /// claiming the final byte on TDV models takes nothing away - but if DECREQTPARM is ever built
    /// for the VT emulators, the two must stay on their own sides of the family and NOT be folded
    /// into one handler. Their parameters mean completely different things.
    ///
    /// The <c>$</c> form of the same final is DECFRA, the VT420 rectangle fill, which
    /// <see cref="TerminalEmulatorBase"/> owns. That is why the caller checks for no intermediate
    /// as well as no private marker.
    ///
    /// <para><b>Report 0 means "requested report type not available", and it is used honestly</b></para>
    /// The manual defines report 0 for exactly that, so it is the right answer for a type this
    /// program cannot fill rather than a shrug.
    /// <para>
    /// Type 1, the emulator level, wants three digits in the range 050 to 100 and NOTHING held here
    /// says what our models should claim. Inventing a number would put a made-up identity on the
    /// wire, which is the fault this whole piece of work exists to undo. It answers 0 until that is
    /// decided.
    /// </para>
    /// <para>
    /// Type 3, the convenience options, reports cursor type, cursor blink, key rollover, margin
    /// bell, caps lock at power on, shift lock key action and menu language. This program models
    /// none of them as terminal state, so every bit would be a guess. It answers 0.
    /// </para>
    /// <para>
    /// Type 4, the graphic switches, the MANUAL itself leaves as "to be defined later". So 0 is not
    /// a gap here - it is the correct and complete answer.
    /// </para>
    /// </remarks>
    protected virtual string BuildTerminalParameterReport(int reportType)
    {
        switch (reportType)
        {
            case 1:
                return "\x1b[1;" + EmulatorLevel().ToString("D3") + "x";

            case 2:
                return "\x1b[2;" + IsoModeBits() + ";" + DecCompatibleModeBits() + ";"
                     + NdPrivateModeBits() + "x";

            case 5:
                return "\x1b[5;" + ErrorConditionBits() + "x";

            case 6:
                return "\x1b[6;" + AuxiliaryDeviceBits() + "x";

            default:
                // 0 is "requested report type not available" - section 5.48. Types 3 and 4 land
                // here, each for a reason given above, and so does any number that is not a report
                // type at all.
                return "\x1b[0x";
        }
    }

    /// <summary>
    /// Report 1: the emulator level, three digits in the range 050 to 100.
    /// </summary>
    /// <returns>
    /// The level this model claims.
    /// </returns>
    /// <remarks>
    /// <para><b>93 is RONNY'S CALL, 11 September 2026, and the reasoning is NOT a measurement</b></para>
    /// ND Display Terminal 1200 Functional Specifications section 5.48 says report type 1 carries
    /// "emulator type (3 digits in the range 050 - 100)" and says nothing whatever about which
    /// number belongs to which machine. Nothing else held here does either.
    ///
    /// What is known is that every D100 session in this repository tells SINTRAN
    /// <c>set-term-type,,93</c>, and 93 sits inside that range. **Whether SINTRAN's terminal-type
    /// number and the ND emulator level are the same registry is NOT established** - they may be
    /// two different numbering schemes that happen to overlap. Ronny weighed that and chose 93 over
    /// answering "not available", which is what this did until he decided.
    ///
    /// So: a considered choice, not a fact. If a real ND terminal is ever asked <c>CSI 1 x</c> and
    /// answers something else, that answer wins and this changes - it is one number in one method.
    ///
    /// <para><b>All three models claim the same level, for now</b></para>
    /// The TDV1200, TDV2215 and TDV2200 are different machines and a real one would presumably
    /// answer differently. Nothing here says how, so one number is claimed rather than three
    /// invented. Override this in a model class the moment a real figure turns up for it.
    /// </remarks>
    protected virtual int EmulatorLevel()
    {
        return 93;
    }

    /// <summary>
    /// Report 2 parameter 1: the ISO modes.
    /// </summary>
    /// <returns>
    /// The bitmask.
    /// </returns>
    /// <remarks>
    /// Bit order from ND-1200 section 5.48: 0 keyboard action, 1 insert/replace, 2 vertical
    /// editing, 3 horizontal editing, 4 send/receive, 5 line feed - new line, 6 SGR combination.
    /// Bit 7 is unused.
    ///
    /// Only the modes this program actually keeps are reported set. One it does not model reads as
    /// 0, which is what the bit means anyway - reset.
    /// </remarks>
    protected virtual int IsoModeBits()
    {
        int bits = 0;
        if (InsertMode) { bits |= 1 << 1; }             // IRM
        if (NewLineMode) { bits |= 1 << 5; }            // LNM
        return bits;
    }

    /// <summary>
    /// Report 2 parameter 2: the DEC-compatible modes.
    /// </summary>
    /// <returns>
    /// The bitmask.
    /// </returns>
    /// <remarks>
    /// Bit order from section 5.48: 0 cursor key, 1 smooth scroll, 2 screen background, 3 origin,
    /// 4 autowrap, 5 and 6 auto repeat, 7 unused. These are DEC modes and this program keeps every
    /// one of them, so this parameter is complete.
    ///
    /// Bit 2 is "screen background mode", whose RESET is NEGATIVE and SET is POSITIVE in the
    /// manual table - the same switch DEC calls DECSCNM, reverse video.
    /// </remarks>
    protected virtual int DecCompatibleModeBits()
    {
        int bits = 0;
        if (ApplicationCursorKeys) { bits |= 1 << 0; }   // DECCKM
        if (SmoothScrollMode) { bits |= 1 << 1; }        // DECSCLM
        if (ReverseVideoMode) { bits |= 1 << 2; }        // DECSCNM
        if (OriginMode) { bits |= 1 << 3; }              // DECOM
        if (AutoWrapMode) { bits |= 1 << 4; }            // DECAWM
        return bits;
    }

    /// <summary>
    /// Report 2 parameter 3: the ND private modes.
    /// </summary>
    /// <returns>
    /// The bitmask.
    /// </returns>
    /// <remarks>
    /// Bit order from section 5.48: 0 beginning of line wrap, 1 roll/page, 2 key klick, 3 PUSH-key
    /// label, 4 PROGRAM-key label, 5 and 6 decimal separator, 7 numeric pad.
    ///
    /// ALL EIGHT are modelled now, which they were not until 11 September 2026 - it started as two,
    /// beginning of line wrap and the numeric pad switch, with the other six answering 0 whatever
    /// the terminal had been told. A report that always says the same thing is worse than no
    /// report, because a host cannot tell the difference between "reset" and "not implemented".
    ///
    /// Every one of the six is held as state and set by its own sequence; none of them changes
    /// what the screen or the keyboard does yet, and the remarks on each property say so.
    /// </remarks>
    protected virtual int NdPrivateModeBits()
    {
        int bits = 0;
        if (ReverseWrapMode) { bits |= 1 << 0; }          // NDBLWM
        if (PageMode) { bits |= 1 << 1; }                 // roll/page
        if (KeyClickEnabled) { bits |= 1 << 2; }          // key klick
        if (PushKeyLabelsHidden) { bits |= 1 << 3; }      // PUSH-key label
        if (ProgramKeyLabelsHidden) { bits |= 1 << 4; }   // PROGRAM-key label

        // Bits 5 and 6 are "Decimal separator mode, 1" and "Decimal separator mode, 2" - two bits
        // for a three-position switch, so the position goes in as a number: 0 PERIOD, 1 COMMA,
        // 2 SEQUENCE. Both readings of the manual's two bit names give the same answer for all
        // three positions, so there is nothing guessed here.
        bits |= ((int)DecimalSeparatorMode & 0x03) << 5;

        if (NumericPadFunctionMode) { bits |= 1 << 7; }   // numeric pad in Function mode
        return bits;
    }

    /// <summary>
    /// Report 5 parameter 1: the error conditions.
    /// </summary>
    /// <returns>
    /// The bitmask.
    /// </returns>
    /// <remarks>
    /// Bit order from section 5.48: 0 framing error, 1 parity error, 2 line buffer overflow,
    /// 6 cursor addressing error, 7 CSI parameter error. Bits 3, 4 and 5 are unused.
    ///
    /// Zero for now, and deliberately. These are conditions a terminal LATCHES and clears when it
    /// reports them, and this program keeps no such latches - the serial framing and parity errors
    /// it does see are raised as events by the connection rather than held on the emulator. Wiring
    /// them through is real work and is not done here. 0 says "nothing latched", which is honest
    /// rather than a guess.
    /// </remarks>
    protected virtual int ErrorConditionBits()
    {
        return 0;
    }

    /// <summary>
    /// Report 6 parameter 1: the auxiliary devices fitted.
    /// </summary>
    /// <returns>
    /// The bitmask.
    /// </returns>
    /// <remarks>
    /// Bit order from section 5.48: 0 magnetic card reader, 1 graphics, 2 mouse, 3 printer
    /// connected. Bits 4 to 7 are unused.
    ///
    /// Graphics is set because it is unconditionally true of every emulator here - the vector and
    /// sixel decoders are offered every byte and nothing gates them, which is the reasoning behind
    /// the standing call that a TDV2200 reports GRAPHICS and TEKTRONIX unconditionally. Mouse is
    /// set because this program does deliver mouse input. No card reader is modelled, and printer
    /// support is designed but not built, so both read 0.
    /// </remarks>
    protected virtual int AuxiliaryDeviceBits()
    {
        int bits = 0;
        bits |= 1 << 1;   // graphics
        bits |= 1 << 2;   // mouse
        return bits;
    }

    /// <summary>
    /// Sends a response back to the host
    /// </summary>
    protected virtual void SendResponse(string response)
    {
        if (string.IsNullOrEmpty(response))
        {
            return;
        }

        // Kept for observers - tests, the capability checker and the protocol tooling all watch
        // this. It is now a notification, NOT the way the reply reaches the host.
        OnResponseReady?.Invoke(response);

        // The reply goes to the host down the SAME byte channel every other emulator uses.
        //
        // It used to have a channel of its own that TerminalSession encoded as UTF-8. That was
        // wrong in two ways. Any byte above 0x7F in a reply would have been encoded as two bytes
        // on the wire - latent rather than live, since no current TDV reply contains one, but a
        // reply is a byte string and nothing was stopping it. And having two ways for a terminal
        // to answer meant two near-identical send paths in the session, and a caller watching the
        // wrong one saw silence rather than an error.
        var bytes = new byte[response.Length];
        for (int i = 0; i < response.Length; i++)
        {
            // Char-to-byte, one for one: these strings are byte strings that happen to be typed
            // as string. Masking is deliberate - a reply must never grow or shrink in transit.
            bytes[i] = (byte)(response[i] & 0xFF);
        }

        SendResponse(bytes);
    }

    /// <summary>
    /// Raised when a response has been produced. This is an OBSERVATION point - the reply is sent
    /// to the host through <see cref="TerminalEmulatorBase.DataToSend"/> like every other
    /// emulator's. Subscribing here does not put anything on the wire.
    /// </summary>
    public event Action<string>? OnResponseReady;

    #endregion

    #region Character Set Variant

    /// <summary>
    /// Gets or sets the current character set variant as an integer.
    /// Each emulator maps this to their specific enum values.
    /// Default is 0 (typically International/US ASCII).
    /// </summary>
    public virtual int CharacterSetVariant
    {
        get => (int)Iso646Handler.CurrentISO646Variant;
        set => Iso646Handler.SetVariant((TDV2200ISO646Variant)value);
    }

    /// <summary>
    /// Gets the available character set variants with descriptions.
    /// Returns an array of (Value, Description) tuples where Value is the integer
    /// that can be assigned to CharacterSetVariant.
    /// </summary>
    public virtual (int Value, string Description)[] GetAvailableCharacterSetVariants()
    {
        return new (int, string)[]
        {
            ((int)TDV2200ISO646Variant.International, "International (US ASCII)"),
            ((int)TDV2200ISO646Variant.Norwegian, "Norwegian"),
            ((int)TDV2200ISO646Variant.Swedish, "Swedish"),
            ((int)TDV2200ISO646Variant.German, "German")
        };
    }

    /// <summary>
    /// Gets the ISO 646 language code for the current character set variant.
    /// Returns null for International/US ASCII (no conversion needed).
    /// Used by keyboard input handlers to convert national characters to wire bytes.
    /// </summary>
    public virtual string? GetISO646LanguageCode()
    {
        // One variant→language answer, shared with the ROM-position mapping.
        return TDVCharacterSets.GetLanguageCodeForVariant(Iso646Handler.CurrentISO646Variant);
    }

    #endregion
}
