using System;
using System.Text;
using RetroTerm.Core.Configuration;
using RetroTerm.Core.Terminal.Buffer;
using RetroTerm.Core.Terminal.Emulators.TDV.Components;
using RetroTerm.Core.Terminal.Parsing;
using RetroTerm.Core.Terminal.Rendering;

namespace RetroTerm.Core.Terminal.Emulators.TDV
{
    /// <summary>
    /// TDV 2200 Terminal Emulator - Complete TDV implementation with built-in 2115 compatibility
    /// 
    /// The TDV 2200 includes:
    /// - Built-in TDV 2115 compatibility mode (CSI ? 40 h/l)
    /// - TDV 2115 C0 control codes (21 codes for basic terminal operations)
    /// - DLE (Direct Line Entry) binary cursor positioning
    /// - Full CSI (Control Sequence Introducer) sequences
    /// - Character set switching (ESC 1-9, ESC N/O, ESC n/o)
    /// - Three-character escape sequences (ESC # 3/4/5/6)
    /// - ND private CSI sequences (0x70-0x7F)
    /// - DCS sequences (PUSH keys, soft keys, UDC)
    /// - Rectangle operations
    /// - Work area operations
    /// - Extended attributes and modes
    /// </summary>
    public class TDV2200Emulator : TDVEmulatorBase
    {
        /// <inheritdoc/>
        public override Profiles.TerminalProfile Profile => TdvProfile;

        /// <inheritdoc/>
        public override Input.TerminalModes GetActiveModes()
        {
            var modes = base.GetActiveModes();
            modes |= Is2115CompatibilityMode ? Input.TerminalModes.TDV2115Mode : Input.TerminalModes.TDV2200Mode;
            return modes;
        }

        /// <summary>
        /// The TDV2200's identity. DA reply matches HandleDeviceAttributesQuery below.
        /// </summary>
        public static readonly Profiles.TerminalProfile TdvProfile =
            Profiles.TerminalProfile.ForTdv("TDV2200", "\x1b[?220;0c",
                Profiles.TerminalFeatures.UserDefinedKeys);

        // Component-based handlers

        // Graphics and Tektronix modes



        // Keyboard mapping
        private TDVKeyboardMapper _keyboardMapper;

        // Test support: Track if Reset() was called (used by unit tests)
        public bool ResetWasCalled { get; set; }

        // Test support: Track if HandleEscapeSequence was called and what final byte it received (used by unit tests)
        public char? LastEscapeFinalByte { get; set; }

        // Test support: Track if OnEscapeDispatch was invoked (used by unit tests)
        public bool OnEscapeDispatchInvoked { get; set; }

        public TDV2200Emulator(int width = 80, int height = 24, int maxScrollback = 1000) : base(width, height, maxScrollback)
        {
            // Initialize character sets in base class per TDV spec
            // G0/G1: US ASCII by default (no character mapping, fontNum stays at 0)
            // G2/G3: GraphicsI/II for line drawing when explicitly invoked
            _g0CharacterSet = TDVCharacterSets.TDVCharacterSetType.USASCII;
            _g1CharacterSet = TDVCharacterSets.TDVCharacterSetType.USASCII;
            _g2CharacterSet = TDVCharacterSets.TDVCharacterSetType.GraphicsI;
            _g3CharacterSet = TDVCharacterSets.TDVCharacterSetType.GraphicsII;

            // Initialize component handlers

            // Initialize keyboard mapper
            _keyboardMapper = new TDVKeyboardMapper(CompatibilityHandler.Is2115CompatibilityMode);

            // Test support: Wire up handler to track OnEscapeDispatch (used by unit tests)
            Parser.OnEscapeDispatch += (parser) =>
            {
                OnEscapeDispatchInvoked = true;
            };

            InitialiseGraphics();
        }

        /// <summary>
        /// Builds the graphics planes and switches on the ESC " ground mode.
        ///
        /// A TDV2200 IS the Norsk Data graphic terminal, so this is where the parser is told that
        /// ESC " starts an ND sequence rather than an ordinary escape with '"' as an intermediate.
        /// That switch is per-profile on purpose: on a VT the default reading is the correct one.
        ///
        /// The surface is the ND/Tek addressable space, 1024 x 780, taken from
        /// spec\Tektronix\nd-graphic-terminal-analysis.md. It is deliberately NOT tied to the text
        /// grid: the graphics resolution of these terminals has nothing to do with how many
        /// character cells are on screen, and the renderer scales the composite to fit.
        /// </summary>
        private void InitialiseGraphics()
        {
            var compositor = new Graphics.GraphicsCompositor(NdGraphicsWidth, NdGraphicsHeight);
            var viewport = new Graphics.GraphicsViewport(
                NdGraphicsWidth, NdGraphicsHeight,
                NdGraphicsWidth, NdGraphicsHeight,
                logicalYIncreasesUpward: true,
                preserveAspectRatio: false);

            _graphicsModule = new Graphics.NorskDataGraphicsModule(compositor, viewport);
            Graphics = compositor;

            // The two model bytes. 0x40 0x40 is what the spec's own worked example carries, and it
            // is the only concrete pair recorded anywhere in it.
            //
            // WHAT IS NOT KNOWN: which pair means which machine. The spec says these two bytes
            // identify the model - ND-324/Notis, ND-325/Net, ND-246, ND-285, ND-320, ND-322 - and
            // never lists the values. So this answers "an ND graphic terminal" correctly and
            // "which one" only by accident. They are settable so a host that cares can be given
            // the right pair once someone reads them off real hardware.
            _graphicsModule.Gin.NorskDataModelByte1 = 0x40;
            _graphicsModule.Gin.NorskDataModelByte2 = 0x40;

            Parser.NorskDataGraphicsSequences = true;
            Parser.OnNorskDataDispatch += OnNorskDataSequence;
        }

        /// <summary>
        /// Addressable width of the ND graphics space.
        /// </summary>
        internal const int NdGraphicsWidth = 1024;

        /// <summary>
        /// Addressable height of the ND graphics space.
        /// </summary>
        internal const int NdGraphicsHeight = 780;

        private Graphics.NorskDataGraphicsModule? _graphicsModule;

        /// <summary>
        /// The ND graphics protocol handler. Never null on a 2200.
        /// </summary>
        public Graphics.NorskDataGraphicsModule? GraphicsModule => _graphicsModule;

        /// <inheritdoc/>
        protected override void OnDisplayColoursChanged()
        {
            // One beam, one colour. An amber screen draws amber vectors, which is what the
            // hardware had no choice about.
            var module = _graphicsModule;
            if (module == null) return;

            var (r, g, b) = DisplayForeground;
            module.DrawColour = new Graphics.GraphicsColor(r, g, b);
        }

        /// <summary>
        /// A TDV2200 is a Tektronix 4014 as well as a text terminal, so bytes go to the vector
        /// decoder first. Most real drawing traffic arrives this way rather than as ESC "
        /// sequences: vectors are what a plotting program sends, and the ND sequences are what it
        /// sends to set things up beforehand.
        /// </summary>
        internal override bool ConsumeGraphicsByte(byte b)
        {
            var module = _graphicsModule;
            if (module == null) return false;

            bool wasDrawing = module.InGraphicsMode;

            if (!module.ConsumeVectorByte(b)) return false;

            // LEAVING GRAPH MODE PUTS THE TEXT CURSOR AT THE LAST PLOTTED POINT.
            //
            // This is how a Tektronix host writes a label anywhere on the screen: move the beam to
            // where the text belongs, drop back to alpha, print. Without it every label lands
            // wherever the previous one happened to end, and a plot's axis numbers come out in a
            // diagonal cascade across the picture instead of beside their tick marks.
            //
            // Found by rendering real gnuplot output and LOOKING at it - the curve, the axes and
            // the border were all correct, and every label was in the wrong place. No assertion in
            // the corpus tests could see it; they check that labels are printed as text, which they
            // were.
            //
            // DERIVED, not quoted: the ND spec does not mention it. It is standard Tek 4010/4014
            // behaviour, and gnuplot's output plainly depends on it.
            if (wasDrawing && !module.InGraphicsMode)
            {
                PlaceCursorAtLastVector(module);
            }

            // Only repaint when something was actually drawn. A mode switch changes no pixel, and
            // compositing the whole screen for it would cost a frame per byte on a stream that
            // arrives in thousands.
            if (module.InGraphicsMode)
            {
                Graphics?.Composite();
                OnInvalidated();
            }

            return true;
        }


        /// <summary>
        /// Moves the text cursor to the character cell holding the last drawn point.
        /// </summary>
        private void PlaceCursorAtLastVector(Graphics.NorskDataGraphicsModule module)
        {
            var vectors = module.Vectors;

            // Graphics space is 1024 x 780 regardless of how many character cells are on screen,
            // so the cell is worked out by proportion rather than by any fixed ratio.
            int column = vectors.LastX * Width / NdGraphicsWidth;

            // Y runs UP in the terminal's own space and DOWN the screen, so the row flips.
            int row = (NdGraphicsHeight - 1 - vectors.LastY) * Height / NdGraphicsHeight;

            if (column < 0) column = 0;
            if (column > Width - 1) column = Width - 1;
            if (row < 0) row = 0;
            if (row > Height - 1) row = Height - 1;

            Cursor.Column = column;
            Cursor.Row = row;
        }

        private void OnNorskDataSequence(Parsing.EscapeSequenceParser parser)
        {
            _graphicsModule?.HandleSequence(parser.Parameters, (char)parser.FinalByte);

            // Flatten immediately, on the pump thread, so the renderer only ever reads a finished
            // picture. Composite double buffers, so this cannot tear what the UI thread is holding.
            Graphics?.Composite();
            OnInvalidated();
        }

        // Note: TDV 2115 C0 control code processing is now handled by TDV2115CompatibilityHandler component
        // Note: Extended control character processing is now handled by TDVInputProcessor component
        // Note: Extended control character handling (SS2/SS3) is now handled by TDVCharacterSetManager component

        /// <summary>
        /// Override HandleCharacter to set FontNumber for TDV2200 bitmap fonts.
        /// TDV2200 uses bitmap fonts - character codes go directly to the buffer,
        /// NO Unicode/ISO646 mapping. The bitmap font handles all character rendering.
        /// </summary>
        protected override void HandleCharacter(uint codepoint)
        {
            // Store current cursor position before base call (this is where the character will be written)
            int row = Cursor.Row;
            int col = Cursor.Column;

            // Determine fontNum based on SS2/SS3 or locking shift state
            byte fontNum = CharacterSetManager.NextFontNumber;

            if (fontNum == 0)
            {
                var activeSet = GetActiveCharacterSetType();
                if (activeSet == TDVCharacterSets.TDVCharacterSetType.GraphicsI)
                {
                    fontNum = 2;
                }
                else if (activeSet == TDVCharacterSets.TDVCharacterSetType.GraphicsII)
                {
                    fontNum = 3;
                }
            }

            // TDV2200 bitmap font: pass codepoint directly, NO mapping
            base.HandleCharacter(codepoint);

            // Set FontNumber for the character we just wrote
            ref var cell = ref Buffer[row, col];
            cell.FontNumber = fontNum;

            // Clear SS2/SS3 flags after use
            if (CharacterSetManager.IsSS2Active || CharacterSetManager.IsSS3Active)
            {
                CharacterSetManager.ClearSingleShiftFlags();
            }
        }

        #region Overrides

        /// <summary>
        /// TDV 2200 has scrollback
        /// </summary>
        public override int MaxScrollback => 10000;

        /// <summary>
        /// Exposes the keyboard indicator lamps driven by ENQ/ACK/NAK/SYN so the virtual
        /// keyboard can mirror them.
        /// </summary>

        /// <summary>
        /// Override HandleExecute to intercept SI/SO control codes and properly manage
        /// character set invocation. SI (0x0F) and SO (0x0E) need to update _invokedCharacterSet
        /// to ensure correct FontNumber is used after LS2/LS3 locking shifts.
        /// </summary>
        protected override void HandleExecute(byte control)
        {
            switch (control)
            {
                case 0x0E: // SO - Shift Out to G1
                    InvokeCharacterSet(1);
                    return;
                case 0x0F: // SI - Shift In to G0
                    InvokeCharacterSet(0);
                    return;

                case 0x05: // ENQ
                    // ESC ENQ is the standard Tektronix identification request; a BARE ENQ is
                    // something else entirely and must not be answered with a screen report.
                    //
                    // Telling them apart is only possible because a C0 arriving inside an escape
                    // sequence now executes WITHOUT abandoning the sequence, so the parser is still
                    // in the Escape state here. Before that, these two were the same byte with no
                    // context, and this reply could not have been written at all.
                    if (Parser.State == Parsing.ParserState.Escape)
                    {
                        SendIdentificationReply();
                        return;
                    }
                    break;
            }
            base.HandleExecute(control);
        }

        /// <summary>
        /// Answers <c>ESC ENQ</c> with the Norsk Data form of the Tektronix identification reply.
        ///
        /// A REAL Tektronix 4014 sends five bytes: a status byte and the crosshair position. An ND
        /// graphic terminal sends SEVEN - the same five, plus two 7-bit bytes that say which ND
        /// model it is. That length difference is how a host tells the two apart, and the ND test
        /// program's detection fails outright if it reads back fewer than seven
        /// (<c>spec\Tektronix\nd-graphic-terminal-analysis.md</c>, validation at ram:c6c3-c701).
        /// Without this reply a real ND program concludes there is no graphics terminal and never
        /// sends any graphics at all.
        /// </summary>
        private void SendIdentificationReply()
        {
            var module = _graphicsModule;
            if (module == null) return;

            // Fully qualified: this class has a Graphics PROPERTY, which shadows the namespace.
            Span<byte> reply = stackalloc byte[
                RetroTerm.Core.Terminal.Graphics.TektronixGinEncoder.NorskDataReportLength];

            // The reply carries the crosshair position whether or not a crosshair is up, so the
            // router is asked for one report regardless of whether the host armed GIN.
            module.Gin.Arm();
            if (!module.Gin.TryBuildReport(IdentificationStatusByte, reply, out int length))
            {
                return;
            }

            SendResponse(reply.Slice(0, length));
        }

        /// <summary>
        /// The status byte that leads an identification reply.
        ///
        /// 0x68 is the example the spec works through, and it satisfies the two bits a host
        /// actually checks: bit 7 clear and bit 6 set. The rest describe a hard-copy unit and an
        /// auxiliary device this emulator does not have.
        /// </summary>
        private const byte IdentificationStatusByte = 0x68;

        /// <summary>
        /// Reset TDV 2200 to initial state
        /// </summary>
        public override void Reset()
        {
            ResetWasCalled = true; // Test support: Track that Reset() was called

            // The graphics side has its own state - a drawing, plane visibility, the vector
            // decoder's mode - and RIS clears all of it.
            _graphicsModule?.Reset();
            Graphics?.Composite();

            // Call base Reset() first to clear buffer and reset cursor
            base.Reset();

            // TDV2200-specific reset is handled by ResetToInitialState()
            // which is called by base.Reset()
        }

        /// <summary>
        /// Reset TDV 2200 to initial state
        /// </summary>
        public override void ResetToInitialState()
        {
            base.ResetToInitialState();

            // G0/G1 default to US ASCII per TDV spec (base.ResetToInitialState already sets this)
            // G2/G3 for special graphics/math characters when explicitly invoked
            _g0CharacterSet = TDVCharacterSets.TDVCharacterSetType.USASCII;
            _g1CharacterSet = TDVCharacterSets.TDVCharacterSetType.USASCII;
            _g2CharacterSet = TDVCharacterSets.TDVCharacterSetType.GraphicsI;
            _g3CharacterSet = TDVCharacterSets.TDVCharacterSetType.GraphicsII;
            _invokedCharacterSet = 0;

            // Reset component handlers (may be null during base constructor call)
            CompatibilityHandler?.Reset();
            CharacterSetManager?.Reset();
            Iso646Handler?.Reset();
            InputProcessor?.Reset();

            // Reset graphics and Tektronix modes

            // ISO646 variant reset is handled by component
        }

        /// <summary>
        /// True while the terminal is in Tektronix graph mode, so a 4014 escape means what it means
        /// on a 4014 rather than what it means to the text terminal.
        /// </summary>
        private bool IsPlottingVectors => _graphicsModule != null && _graphicsModule.InGraphicsMode;

        /// <summary>
        /// Applies a Tektronix line type escape.
        /// </summary>
        /// <param name="final">
        /// The character after the escape.
        /// </param>
        /// <returns>
        /// True when it named a line type and the plotter was set.
        /// </returns>
        /// <remarks>
        /// The five the ND analysis names, and no others. Character size selection is NOT here: the
        /// analysis lists what the ND terminal takes from the 4014 and character sizes are not on
        /// that list, so adding them would be an invention rather than a reading.
        /// </remarks>
        private bool TrySelectVectorPattern(char final)
        {
            var module = _graphicsModule;
            if (module == null) return false;

            switch (final)
            {
                case '`': module.Pattern = RetroTerm.Core.Terminal.Graphics.LinePattern.Solid; return true;
                case 'a': module.Pattern = RetroTerm.Core.Terminal.Graphics.LinePattern.Dotted; return true;
                case 'b': module.Pattern = RetroTerm.Core.Terminal.Graphics.LinePattern.DotDash; return true;
                case 'c': module.Pattern = RetroTerm.Core.Terminal.Graphics.LinePattern.ShortDash; return true;
                case 'd': module.Pattern = RetroTerm.Core.Terminal.Graphics.LinePattern.LongDash; return true;
                default: return false;
            }
        }

        /// <summary>
        /// Handles an escape sequence, overriding the base to add SS2 and SS3.
        /// </summary>
        /// <param name="parser">
        /// The parser holding the sequence just collected.
        /// </param>
        protected override void HandleEscapeSequence(EscapeSequenceParser parser)
        {
            var final = (char)parser.FinalByte;
            var intermediates = parser.Intermediates;

            // Test support: Track what final byte we received
            LastEscapeFinalByte = final;

            // TEKTRONIX LINE TYPES, AND ONLY WHILE THE BEAM IS ON.
            //
            // The ND analysis lists these under "Compatible (standard Tek 4014)": "Line types:
            // Dotted (ESC a), dot-dashed (ESC b), short-dashed (ESC c), long-dashed (ESC d),
            // normal (ESC `)". A TDV2200 is a 4014 as well as a text terminal, so a host plotting
            // a graph selects its line style this way and expects a dashed line.
            //
            // ESC c CLASHES: in alpha mode it is the terminal reset that TDVEmulatorBase already
            // handles, and ND software uses it. Both must work on one machine, so the graph mode
            // decides which meaning applies - a rule the firmware would need too.
            //
            // DERIVED, not quoted: the analysis lists the line types and says nothing about the
            // clash. Ronny's call, 2026-08-18. If a real ND machine ever says otherwise, THIS is
            // the comment to correct.
            if (intermediates.Length == 0 && IsPlottingVectors && TrySelectVectorPattern(final))
            {
                return;
            }

            // Handle ESC Q - Exit 2115 compatibility mode / Enable extended mode
            if (intermediates.Length == 0 && final == 'Q')
            {
                CompatibilityHandler.Set2115CompatibilityMode(false);
                return;
            }

            // Handle ISO646 variant selection (ESC % X where X = N, S, D, F, G, U)
            if (intermediates.Length == 1 && intermediates[0] == '%')
            {
                // Directly set the variant based on the final character
                // Per TDV2115 spec section 9.1
                TDV2200ISO646Variant variant = final switch
                {
                    'I' => TDV2200ISO646Variant.International,
                    'N' => TDV2200ISO646Variant.Norwegian,
                    'S' => TDV2200ISO646Variant.Swedish,
                    'G' => TDV2200ISO646Variant.German,
                    _ => Iso646Handler.CurrentISO646Variant // Keep current if unknown
                };
                Iso646Handler.SetVariant(variant);
                return;
            }

            // Handle SS2/SS3 (C1 control characters via ESC sequences) and LS2/LS3 (locking shifts)
            if (intermediates.Length == 0)
            {
                switch (final)
                {
                    case 'N': // SS2 - Single shift to G2
                        CharacterSetManager.ActivateSS2();
                        return;
                    case 'O': // SS3 - Single shift to G3
                        CharacterSetManager.ActivateSS3();
                        return;
                    case 'n': // LS2 - Locking shift to G2
                        InvokeCharacterSet(2);
                        return;
                    case 'o': // LS3 - Locking shift to G3
                        InvokeCharacterSet(3);
                        return;
                }
            }

            // Call base class for other ESC sequences (including RIS 'c')
            base.HandleEscapeSequence(parser);
        }

        /// <summary>
        /// Handle CSI sequences - override to handle rectangle operations
        /// </summary>
        protected override void HandleCsiSequence(EscapeSequenceParser parser)
        {
            var final = (char)parser.FinalByte;
            var parameters = parser.Parameters;
            var privateMarker = parser.PrivateMarker;

            // Handle query sequences FIRST (before other sequences)
            // This ensures DA, CPR, DSR, and mode queries are processed
            if (HandleQuerySequence(final, parameters, privateMarker, parser))
            {
                return;
            }

            // Handle TDV2200-specific sequences (rectangle operations, etc.).
            //
            // ONLY when the sequence carries no intermediate byte. Every TDV2200-specific final
            // below is intermediate-free, while several STANDARD sequences share those finals and
            // are told apart precisely by an intermediate: DECSCA is CSI Ps " q and DECSCUSR is
            // CSI Ps SP q, both of which NDDLWA ('q', Delete Lines in Work Area) was swallowing
            // whole. Without this guard a 2200 silently ate them and did a work-area delete
            // instead — a wrong action, not merely a missing one.
            if (parser.Intermediates.Length == 0 && HandleTDV2200Sequence(final, parameters, privateMarker))
            {
                return;
            }

            // Fall back to base TDV handling
            base.HandleCsiSequence(parser);
        }

        /// <summary>
        /// Handles TDV2200-specific CSI sequences (rectangle operations, work areas, etc.)
        /// </summary>
        private bool HandleTDV2200Sequence(char final, ReadOnlySpan<int> parameters, byte privateMarker)
        {
            switch (final)
            {
                case 'z': // NDSAR - Set Attribute in Rectangle
                    HandleSetAttributeInRectangle(parameters);
                    return true;

                case '{': // NDAAR - Add Attribute in Rectangle
                    HandleAddAttributeInRectangle(parameters);
                    return true;

                case '|': // NDRAR - Remove Attribute in Rectangle, hex 7C
                    // These two were SWAPPED until 11 September 2026: NDRAR sat on '}' and NDFC on
                    // '|'. ND Display Terminal 1200 section 2.8 and the TDV 2200 CSI table at
                    // spec\TDV2200\Testing2200_9S\nd_csi_sequences.md both give 7C NDRAR, 7D NDFC.
                    HandleRemoveAttributeInRectangle(parameters);
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

                case '}': // NDFC - Fill Character(s) in Rectangle, hex 7D
                    HandleFillCharacter(parameters);
                    return true;

                case '>': // TDV2200 mode control
                    HandleModeControl(parameters);
                    return true;

                case 'p': // NDLIWA - Insert Lines in Work Area
                    HandleInsertLinesInWorkArea(parameters);
                    return true;

                case 'q': // NDDLWA - Delete Lines in Work Area
                    HandleDeleteLinesInWorkArea(parameters);
                    return true;

                case 's': // NDICHE - Insert Characters with Extent
                    HandleInsertCharactersWithExtent(parameters);
                    return true;

                case 't': // NDDCHE - Delete Characters with Extent
                    HandleDeleteCharactersWithExtent(parameters);
                    return true;

                default:
                    return false;
            }
        }




        /// <summary>
        /// Handles NDLIWA - Insert Lines in Work Area
        /// Format: ESC[n p
        /// Inserts n blank lines at the current cursor position within the work area
        /// </summary>
        private void HandleInsertLinesInWorkArea(ReadOnlySpan<int> parameters)
        {
            var count = parameters.Length > 0 ? Math.Max(1, parameters[0]) : 1;
            var (left, top, right, bottom) = WorkAreas.GetCurrentWorkArea();

            // Only insert lines if cursor is within work area
            if (Cursor.Row >= top && Cursor.Row <= bottom)
            {
                // Insert count lines at cursor position, scrolling down within work area
                for (int i = 0; i < count && Cursor.Row + i <= bottom; i++)
                {
                    Buffer.ScrollDown(Cursor.Row, bottom);
                }
                OnInvalidated();
            }
        }

        /// <summary>
        /// Handles NDDLWA - Delete Lines in Work Area
        /// Format: ESC[n q
        /// Deletes n lines at the current cursor position within the work area
        /// </summary>
        private void HandleDeleteLinesInWorkArea(ReadOnlySpan<int> parameters)
        {
            var count = parameters.Length > 0 ? Math.Max(1, parameters[0]) : 1;
            var (left, top, right, bottom) = WorkAreas.GetCurrentWorkArea();

            // Only delete lines if cursor is within work area
            if (Cursor.Row >= top && Cursor.Row <= bottom)
            {
                // Delete count lines at cursor position, scrolling up within work area
                for (int i = 0; i < count && Cursor.Row <= bottom; i++)
                {
                    Buffer.ScrollUp(Cursor.Row, bottom);
                }
                OnInvalidated();
            }
        }

        /// <summary>
        /// Handles NDICHE - Insert Characters with Extent
        /// Format: ESC[n s
        /// Inserts n blank characters at cursor position, shifting existing characters right
        /// </summary>
        private void HandleInsertCharactersWithExtent(ReadOnlySpan<int> parameters)
        {
            var count = parameters.Length > 0 ? Math.Max(1, parameters[0]) : 1;
            var row = Cursor.Row;
            var col = Cursor.Column;

            // Shift characters to the right
            for (int c = Width - 1; c >= col + count; c--)
            {
                Buffer[row, c] = Buffer[row, c - count];
            }

            // Clear the inserted positions
            for (int c = col; c < col + count && c < Width; c++)
            {
                ref var cell = ref Buffer[row, c];
                cell.Codepoint = ' ';
                cell.Attributes = CharacterAttributes.None;
            }

            OnInvalidated();
        }

        /// <summary>
        /// Handles NDDCHE - Delete Characters with Extent
        /// Format: ESC[n t
        /// Deletes n characters at cursor position, shifting remaining characters left
        /// </summary>
        private void HandleDeleteCharactersWithExtent(ReadOnlySpan<int> parameters)
        {
            var count = parameters.Length > 0 ? Math.Max(1, parameters[0]) : 1;
            var row = Cursor.Row;
            var col = Cursor.Column;

            // Shift characters to the left
            for (int c = col; c < Width - count; c++)
            {
                Buffer[row, c] = Buffer[row, c + count];
            }

            // Clear the positions at the end
            for (int c = Width - count; c < Width; c++)
            {
                ref var cell = ref Buffer[row, c];
                cell.Codepoint = ' ';
                cell.Attributes = CharacterAttributes.None;
            }

            OnInvalidated();
        }

        /// <summary>
        /// Records a CSI n greater-than sequence, which this terminal does not implement.
        /// </summary>
        /// <param name="parameters">
        /// The sequence's numeric parameters.
        /// </param>
        /// <remarks>
        /// <para><b>It used to act on four modes, and none of them has a source</b></para>
        /// Until 25 August 2026 this switch read 0 and 1 as enabling and disabling a graphics
        /// extension, and 2 and 3 as entering and leaving Tektronix mode. Searched for on that day:
        /// the ND graphic terminal analysis in the Tektronix spec folder, the TDV1200 graphics
        /// library reference, and the comprehensive TDV reference in docs. None of the three names
        /// such a sequence at all. It is our own invention, the same way the <c>?3 DECCOLM</c> entry
        /// in the TDV reference turned out to be.
        /// <para><b>And the numbering looks borrowed from a different sequence family</b></para>
        /// The ND analysis DOES have a mode 2 and a mode 3 - under <c>ESC "</c>, where 2 sets the
        /// WRITE MODE (replace, OR, XOR, complement) and 3 sets the LINE STYLE. Neither has anything
        /// to do with Tektronix. The likeliest story is that those numbers were carried over to the
        /// wrong introducer, which is a good reason not to guess again.
        /// <para><b>Why counting beats acting</b></para>
        /// A real ND host that sends this now shows up in <c>UNHANDLED</c> with its parameters, and
        /// the true meaning can be read off a machine instead of invented. Acting on a guess is what
        /// made the capability strings lie for as long as they did.
        /// </remarks>
        private void HandleModeControl(ReadOnlySpan<int> parameters)
        {
            CountUnrecognisedSequence(parameters.Length == 0
                ? "CSI >"
                : "CSI " + parameters[0] + " >");
        }

        // ESC # 3/4/5/6 (DECDHL/DECSWL/DECDWL) is TerminalEmulatorBase.HandleLineSize now.
        // The override here was the same switch as the shared one; its only difference was
        // calling SetLineWidth(Single) after SetLineHeight(Single) on ESC # 5, which clears
        // DoubleWidth a second time — SetLineHeight already clears all three double-size
        // attributes. TDV1200 carried a third copy behind four one-line private helpers.

        /// <summary>
        /// Handle device attributes query - respond with TDV 2200 identification
        /// </summary>
        protected override string HandleDeviceAttributesQuery()
        {
            if (CompatibilityHandler.Is2115CompatibilityMode)
            {
                return "\x1b[?115;0c"; // TDV 2115/2215 firmware ID
            }
            else
            {
                return "\x1b[?220;0c"; // TDV 2200 firmware ID
            }
        }

        /// <summary>
        /// Handle secondary device attributes query
        /// </summary>
        protected override string HandleSecondaryDeviceAttributesQuery()
        {
            if (CompatibilityHandler.Is2115CompatibilityMode)
            {
                return "\x1b[>115;0;0c"; // TDV 2115 secondary attributes
            }
            else
            {
                return "\x1b[>220;0;0c"; // TDV 2200 secondary attributes
            }
        }

        /// <summary>
        /// Handle terminal identification
        /// </summary>
        protected override string HandleTerminalIdentification()
        {
            if (CompatibilityHandler.Is2115CompatibilityMode)
            {
                return "\x1b[?115;0c"; // TDV 2115 identification
            }
            else
            {
                return "\x1b[?220;0c"; // TDV 2200 identification
            }
        }

        // TWO OVERRIDES USED TO SIT HERE AND BOTH ARE GONE, 11 September 2026.
        //
        // HandleNDPrivateSequence claimed mode 40 as "TDV 2115 compatibility mode" and passed
        // everything else on. Mode 40 is PCF, the printer code format, in TDV 2215 Functional
        // Specifications section 8.7.1, and it does not exist at all on the ND Display Terminal
        // 1200. It reached this file from a document in docs\ that cites no source. The 2115
        // switch is mode 66, it carries NO private marker, and RESET is the 2115 side of it - the
        // base class handles it and quotes the manuals for why.
        //
        // GetModeState answered DECRQM, which no TDV manual describes. See
        // docs\TDV-MODES-AND-REPORTS-FROM-THE-MANUALS.md. TerminalEmulatorBase still answers
        // DECRQM for this emulator, with DEC's numbers and DEC's values, as our own extension.

        #endregion

        #region Properties

        /// <summary>
        /// Gets whether TDV 2115 compatibility mode is active
        /// </summary>

        /// <summary>
        /// Handles 2115 compatibility mode for TDV2200
        /// </summary>
        protected override void Handle2115CompatibilityMode(bool enable)
        {
            CompatibilityHandler.Set2115CompatibilityMode(enable);
        }

        /// <summary>
        /// Gets TDV 2115 compatibility mode state (override from base)
        /// </summary>
        protected override bool Get2115CompatibilityMode()
        {
            return CompatibilityHandler.Is2115CompatibilityMode;
        }

        /// <summary>
        /// Video display state
        /// </summary>
        public bool VideoOn => CompatibilityHandler.VideoOn;

        /// <summary>
        /// LED states (3 LEDs)
        /// </summary>
        public bool[] Leds => CompatibilityHandler.Leds;

        /// <summary>
        /// Whether this terminal draws Tektronix vectors. Always true.
        /// </summary>
        /// <remarks>
        /// It is not a mode and never was. <see cref="ConsumeGraphicsByte"/> offers every byte to
        /// the vector decoder in Ground state, unconditionally, so a plot draws whether or not any
        /// sequence asked for it - which is correct, because GS is what puts a 4014 into graph mode
        /// and there is nothing else to switch on.
        ///
        /// Kept as a property rather than deleted so the answer is stated somewhere rather than
        /// merely being true by accident.
        /// </remarks>
        public bool DrawsTektronixVectors => true;

        /// <summary>
        /// Current ISO646 variant
        /// </summary>
        public TDV2200ISO646Variant CurrentISO646Variant => Iso646Handler.CurrentISO646Variant;

        // Test compatibility properties
        public new object CharacterSets => new TDVCharacterSetsWrapper();

        #endregion

        #region Character Set Variant (ISO 646)

        #endregion

        #region Graphics Extension Operations

        /// <summary>
        /// Handle graphics extension operation
        /// </summary>
        public void HandleGraphicsExtensionOperation(string operation)
        {
            // TODO: Implement graphics extension operations
        }

        /// <summary>
        /// Handle graphics extension operation with parameters
        /// </summary>
        public void HandleGraphicsExtensionOperation(string operation, string parameters)
        {
            // TODO: Implement graphics extension operations with parameters
        }

        /// <summary>
        /// Handle graphics extension operation with int parameters
        /// </summary>
        public void HandleGraphicsExtensionOperation(int operation, int[] parameters)
        {
            // TODO: Implement graphics extension operations with int parameters
        }

        /// <summary>
        /// Get terminal type string
        /// </summary>
        public override string GetTerminalType()
        {
            // BOTH UNCONDITIONAL, because both are unconditionally true. These used to be gated on
            // two flags set by CSI n greater-than, a sequence with no source anywhere - so the
            // terminal reported no Tektronix until an invented sequence arrived, while happily
            // drawing vectors the whole time.
            var type = "TDV2200+GRAPHICS+TEKTRONIX";

            type += $"+ISO646_{Iso646Handler.CurrentISO646Variant}";

            return type;
        }

        /// <summary>
        /// Get terminal capabilities
        /// </summary>
        public override string GetTerminalCapabilities()
        {
            var capabilities = "TDV2200+GRAPHICS+TEKTRONIX";

            capabilities += $"+ISO646_{Iso646Handler.CurrentISO646Variant}";
            capabilities += "+NDGRAPHICS+NDWORKAREA+NDPROTECTED+NDLEDS+NDPUSHKEYS";

            return capabilities;
        }

        /// <summary>
        /// Handle keyboard input with TDV key mapping
        /// </summary>
        public void HandleKeyPress(string keyName, bool shift = false, bool ctrl = false, bool alt = false)
        {
            // Handle regular ASCII keys directly (A-Z, 0-9, etc.)
            if (keyName.Length == 1 && char.IsLetterOrDigit(keyName[0]))
            {
                // For regular ASCII keys, send them directly
                var ch = keyName[0];
                if (shift && char.IsLetter(ch))
                {
                    ch = char.ToUpper(ch);
                }
                else if (!shift && char.IsLetter(ch))
                {
                    ch = char.ToLower(ch);
                }
                // Process the character directly - this will trigger HandleCharacter
                ProcessInput(System.Text.Encoding.UTF8.GetBytes(new[] { ch }));
                OnInvalidated(); // Ensure UI is updated
                return;
            }

            string? sequence = null;

            // Check if it's a TDV 2115 control code (when in 2115 compatibility mode)
            if (CompatibilityHandler.Is2115CompatibilityMode && _keyboardMapper.IsTDV2115ControlCode(keyName))
            {
                sequence = _keyboardMapper.GetTDV2115ControlCode(keyName);
            }
            else
            {
                // Use standard TDV key mapping
                sequence = _keyboardMapper.MapKey(keyName, shift, ctrl, alt);
            }

            if (sequence != null)
            {
                var bytes = System.Text.Encoding.UTF8.GetBytes(sequence);
                ProcessInput(bytes);
                OnInvalidated(); // Ensure UI is updated
            }
        }

        /// <summary>
        /// Handle TDV PUSH key press (programmable keys)
        /// </summary>
        public void HandlePushKeyPress(int pushKeyNumber, bool shifted = false)
        {
            var sequence = _keyboardMapper.GetTDVPushKeySequence(pushKeyNumber, shifted);
            if (sequence != null)
            {
                var bytes = System.Text.Encoding.UTF8.GetBytes(sequence);
                ProcessInput(bytes);
            }
        }

        /// <summary>
        /// Handle TDV soft key press
        /// </summary>
        public void HandleSoftKeyPress(int softKeyNumber)
        {
            var sequence = _keyboardMapper.GetTDVSoftKeySequence(softKeyNumber);
            if (sequence != null)
            {
                var bytes = System.Text.Encoding.UTF8.GetBytes(sequence);
                ProcessInput(bytes);
            }
        }

        /// <summary>
        /// Check if a key is supported
        /// </summary>
        public bool IsKeySupported(string keyName)
        {
            return _keyboardMapper.IsKeySupported(keyName);
        }

        /// <summary>
        /// Check if a key is a TDV 2115 control code
        /// </summary>
        public bool IsTDV2115ControlCode(string keyName)
        {
            return _keyboardMapper.IsTDV2115ControlCode(keyName);
        }

        #endregion
    }

    /// <summary>
    /// Wrapper class for TDVCharacterSets to provide test compatibility
    /// </summary>
    public class TDVCharacterSetsWrapper
    {
        public void SetCharacterSet(int set, TDVCharacterSets.TDVCharacterSetType type)
        {
            // TODO: Implement character set switching
        }

        public void SetCurrentSet(int set)
        {
            // TODO: Implement current set switching
        }

        public int GetCurrentSet()
        {
            return 0; // TODO: Return actual current set
        }
    }
}
