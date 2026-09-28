using System;
using RetroTerm.Core.Terminal.Graphics;
using RetroTerm.Core.Terminal.Parsing;
using RetroTerm.Core.Terminal.Profiles;

namespace RetroTerm.Core.Terminal.Emulators.Tektronix;

/// <summary>
/// The Tektronix 4014 storage-tube graphics terminal.
/// </summary>
/// <remarks>
/// <para><b>Why it is its own terminal now</b></para>
/// The vector decoder, the graphics surface, the coordinate transform and the GIN reports were all
/// built and proven, but they could only be reached by opening a TDV2200 - a Norsk Data terminal
/// that happens to contain a 4014. Anyone wanting to look at plain Tektronix output had to pick a
/// Norwegian terminal from 1984 to get it, and had to send an ND sequence (<c>ESC "17h</c>) first
/// to make the picture visible.
///
/// <para><b>What a 4014 is</b></para>
/// A screen that remembers. There is no refresh and no frame buffer being scanned - the beam
/// writes on a storage tube and the image stays until the whole screen is erased. That single fact
/// explains most of the differences from every other terminal here:
///  - Nothing is ever "cleared" a piece at a time. There is one erase, <c>ESC FF</c>, and it takes
///    the whole screen with it.
///  - The graphics plane is visible from the moment the terminal is switched on. There is no
///    enable-graphics command, because there is nothing to enable.
///  - Scrolling does not exist. Text past the bottom of the screen wraps round to the top in a
///    second column, which is NOT implemented here - see the note on alpha mode below.
///
/// <para><b>What is implemented</b></para>
/// The four commands the spec in this repo (<c>spec\Tektronix\nd-graphic-terminal-analysis.md</c>)
/// names as standard 4014, plus the coordinate stream:
///  - GS, FS, RS, US - graph mode, point plot, incremental plot, back to alpha.
///  - <c>ESC FF</c> erases the screen and homes the cursor.
///  - <c>ESC ENQ</c> reports status and beam position in five bytes.
///  - <c>ESC SUB</c> raises the crosshair and arms GIN.
///  - <c>ESC CAN</c> sets the bypass condition, and alpha mode and <c>ESC ETB</c> clear it.
///
/// <para><b>What is not</b></para>
/// The four character sizes (<c>ESC 8</c> through <c>ESC ;</c>), the vector patterns
/// (<c>ESC `</c> through <c>ESC g</c>), the raster writing modes, the alternate-column text wrap,
/// and hard copy itself. None of them is guessed at: this terminal draws, and says nothing it
/// cannot back up. The full list, read against DEC's own 4010/4014 chapter, is in
/// <c>docs\TEKTRONIX-4014-GAP-2026-08-17.md</c>.
/// </remarks>
public sealed class Tek4014Emulator : TerminalEmulatorBase
{
    /// <summary>
    /// Addressable width of the 4014 screen in display points.
    /// </summary>
    public const int GraphicsWidth = 1024;

    /// <summary>
    /// Addressable height of the 4014 screen in display points.
    /// </summary>
    /// <remarks>
    /// The tube is wider than it is tall and the coordinate space is square, so a host may address
    /// up to 1023 vertically but only the bottom 780 are on the glass.
    /// </remarks>
    public const int GraphicsHeight = 780;

    /// <summary>
    /// Columns of text at the default character size.
    /// </summary>
    public const int DefaultColumns = 74;

    /// <summary>
    /// Lines of text at the default character size.
    /// </summary>
    public const int DefaultRows = 35;

    /// <summary>
    /// The status byte that leads an identification reply.
    /// </summary>
    /// <remarks>
    /// 0x68 is the value the ND spec works through, and it satisfies the two bits a host actually
    /// checks: bit 7 clear and bit 6 set. The rest describe a hard-copy unit and an auxiliary
    /// device this emulator does not have.
    /// </remarks>
    private const byte IdentificationStatusByte = 0x68;

    private readonly GraphicsPlane _drawing;
    private readonly GraphicsPlane _crosshair;
    private readonly TektronixPlotter _plotter;

    /// <summary>
    /// Builds a 4014 with its screen and its graphics surface.
    /// </summary>
    /// <param name="width">
    /// Text columns. Defaults to the 74 of the real machine at its default character size.
    /// </param>
    /// <param name="height">
    /// Text lines.
    /// </param>
    /// <param name="maxScrollback">
    /// Lines of history to keep. A real 4014 has none - the tube is the memory - but the window
    /// this runs in does, and throwing it away would be a worse terminal, not a truer one.
    /// </param>
    public Tek4014Emulator(int width = DefaultColumns, int height = DefaultRows, int maxScrollback = 1000)
        : base(width, height, maxScrollback)
    {
        var compositor = new GraphicsCompositor(GraphicsWidth, GraphicsHeight);

        // Y runs UPWARD in Tektronix space: (0,0) is the bottom left corner of the screen. The
        // aspect ratio is deliberately not preserved - the addressable space is square and the
        // screen is not, and the renderer stretches the composite over the text area.
        var viewport = new GraphicsViewport(
            GraphicsWidth, GraphicsHeight,
            GraphicsWidth, GraphicsHeight,
            logicalYIncreasesUpward: true,
            preserveAspectRatio: false);

        _drawing = compositor.AddPlane("tek-drawing");
        _crosshair = compositor.AddPlane("tek-gin");

        // VISIBLE FROM THE START, unlike the ND terminal's plane. A storage tube has no
        // enable-graphics command: whatever the beam wrote is on the glass.
        _drawing.IsVisible = true;
        _crosshair.IsVisible = false;

        _plotter = new TektronixPlotter(_drawing, viewport);

        // A plain 4014, so five bytes and no model identification. The two extra bytes are the
        // Norsk Data extension and sending them here would make this terminal claim to be one.
        Gin = new GinRouter(viewport) { ReportAsNorskData = false };

        Graphics = compositor;
    }

    /// <inheritdoc/>
    public override TerminalProfile Profile => TerminalProfile.Tek4014;

    /// <summary>
    /// Where a pick is reported from. Shared with the terminal's input path.
    /// </summary>
    public GinRouter Gin { get; }

    /// <summary>
    /// The coordinate stream and the ink it makes.
    /// </summary>
    public TektronixPlotter Plotter => _plotter;

    /// <summary>
    /// Whether the crosshair is up.
    /// </summary>
    public bool CrosshairVisible => _crosshair.IsVisible;

    /// <inheritdoc/>
    protected override bool HasGraphicsInput => true;

    /// <inheritdoc/>
    protected override void OnDisplayColoursChanged()
    {
        // A storage tube has one beam. When the screen becomes amber, so does everything drawn on
        // it - the plot, and the crosshair with it.
        var (r, g, b) = DisplayForeground;
        _plotter.DrawColour = new GraphicsColor(r, g, b);
    }

    /// <inheritdoc/>
    internal override bool ConsumeGraphicsByte(byte b)
    {
        bool wasDrawing = _plotter.InGraphicsMode;

        if (!_plotter.ConsumeVectorByte(b)) return false;

        // LEAVING GRAPH MODE PUTS THE TEXT CURSOR AT THE LAST PLOTTED POINT. This is how a host
        // writes a label anywhere on the screen: move the beam to where the text belongs, drop back
        // to alpha, print. Without it a plot's axis numbers come out in a diagonal cascade across
        // the picture instead of beside their tick marks.
        if (wasDrawing && !_plotter.InGraphicsMode)
        {
            PlaceCursorAtLastVector();
        }

        // Only repaint when something was actually drawn. A mode switch changes no pixel, and
        // compositing the whole screen for it would cost a frame per byte on a stream that arrives
        // in thousands.
        if (_plotter.InGraphicsMode)
        {
            Graphics?.Composite();
            OnInvalidated();
        }

        return true;
    }

    /// <summary>
    /// Handles the control codes that mean something different on a 4014 from everywhere else.
    /// </summary>
    /// <remarks>
    /// All three are told apart from their plain C0 selves by whether an ESC came first, which is
    /// readable here only because a C0 inside an escape sequence executes without abandoning the
    /// sequence. A bare FF is a form feed; <c>ESC FF</c> erases the tube.
    /// </remarks>
    /// <param name="control">
    /// The C0 code.
    /// </param>
    protected override void HandleExecute(byte control)
    {
        bool afterEscape = Parser.State == ParserState.Escape;

        switch (control)
        {
            case 0x05: // ENQ - report status and beam position
                if (afterEscape)
                {
                    SendIdentificationReply();
                    return;
                }
                break;

            case 0x0C: // FF - erase the screen
                if (afterEscape)
                {
                    EraseScreen();
                    return;
                }
                break;

            case 0x1A: // SUB - raise the crosshair and arm GIN
                if (afterEscape)
                {
                    EnterGinMode();
                    return;
                }
                break;

            case 0x17: // ETB - print a hard copy of the bitmap
                if (afterEscape)
                {
                    // The manual: "This sequence prints a hard copy of the terminal's bitmap by
                    // using the sixel protocol (Chapter 16). The sequence also clears the bypass
                    // condition. The sequence only works when a printer is connected to the
                    // terminal's printer port."
                    //
                    // All three clauses are honoured. PrintGraphics re-encodes our own bitmap as
                    // sixel and sends it to the print sink - the same sink media copy uses, which
                    // is the whole reason this was held back until there was one. With no sink
                    // attached it does nothing, which is the manual's third clause exactly.
                    PrintGraphics();
                    IsBypassed = false;
                    return;
                }
                break;

            case 0x18: // CAN - set the bypass condition
                if (afterEscape)
                {
                    IsBypassed = true;
                    return;
                }
                break;
        }

        base.HandleExecute(control);
    }

    /// <summary>
    /// The four character sizes a 4014 offers, in the order the manual lists them.
    /// </summary>
    /// <remarks>
    /// The names are the manual's own. Each is a text grid over the same fixed tube: the glass does
    /// not change, the cell does.
    /// </remarks>
    public enum CharacterSize
    {
        /// <summary>
        /// 35 lines of 74 characters. The power-on size.
        /// </summary>
        Size1 = 0,

        /// <summary>
        /// 38 lines of 81 characters.
        /// </summary>
        Size2 = 1,

        /// <summary>
        /// 58 lines of 121 characters.
        /// </summary>
        Size3 = 2,

        /// <summary>
        /// 64 lines of 133 characters.
        /// </summary>
        Size4 = 3,
    }

    /// <summary>
    /// The character size in force.
    /// </summary>
    public CharacterSize TextSize { get; private set; } = CharacterSize.Size1;

    /// <summary>
    /// Selects one of the four character sizes and re-lays the text grid to match.
    /// </summary>
    /// <remarks>
    /// <para><b>The sizes</b></para>
    /// From the 4010/4014 chapter, aligned mode:
    ///  - <c>ESC 8</c> - 35 lines of 74 characters, the default.
    ///  - <c>ESC 9</c> - 38 lines of 81 characters.
    ///  - <c>ESC :</c> - 58 lines of 121 characters.
    ///  - <c>ESC ;</c> - 64 lines of 133 characters.
    ///
    /// <para><b>Enlarged mode is not host-selectable</b></para>
    /// The same four sequences select 24 lines of 69 and 47 lines of 125 when the terminal is in
    /// enlarged mode - but that is chosen in Graphics Set-Up, by the person sitting at the
    /// terminal, not by the host. There is no set-up screen here, so this is the aligned table.
    /// Nothing is guessed: the enlarged numbers are in the manual and would be a set-up option if
    /// one is ever added.
    ///
    /// <para><b>What a real tube does that this cannot</b></para>
    /// A storage tube keeps what is already drawn. Changing the character size on real hardware
    /// changes the cell for text written AFTERWARDS and leaves everything already on the glass
    /// alone, so one screen can carry two sizes at once. This buffer has one cell size for the
    /// whole grid, so text already written is re-laid out instead of staying put. In practice a
    /// host sends the size right after an erase, when there is nothing to disturb.
    /// </remarks>
    /// <param name="size">
    /// The size to select.
    /// </param>
    public void SelectCharacterSize(CharacterSize size)
    {
        TextSize = size;

        int columns;
        int rows;

        switch (size)
        {
            case CharacterSize.Size2: columns = 81; rows = 38; break;
            case CharacterSize.Size3: columns = 121; rows = 58; break;
            case CharacterSize.Size4: columns = 133; rows = 64; break;
            default: columns = DefaultColumns; rows = DefaultRows; break;
        }

        if (columns == Width && rows == Height) return;

        Resize(columns, rows);
        OnInvalidated();
    }

    /// <inheritdoc/>
    protected override void HandleEscapeSequence(EscapeSequenceParser parser)
    {
        if (parser.Intermediates.Length == 0)
        {
            switch ((char)parser.FinalByte)
            {
                // The four Digital recommends.
                case '8': SelectCharacterSize(CharacterSize.Size1); return;
                case '9': SelectCharacterSize(CharacterSize.Size2); return;
                case ':': SelectCharacterSize(CharacterSize.Size3); return;
                case ';': SelectCharacterSize(CharacterSize.Size4); return;

                // And the four it does not: "Digital does not recommend using ESC 0, ESC 1, ESC 2,
                // and ESC 3. These sequences are not standard Tektronix sequences, and may not be
                // supported in future terminals." They are honoured anyway, because a terminal that
                // ignores a documented sequence is worse than one that discourages it, and the
                // manual prints what each of them does.
                case '0': SelectCharacterSize(CharacterSize.Size4); return;
                case '1':
                case '2':
                case '3': SelectCharacterSize(CharacterSize.Size1); return;

                // Select Vector Patterns. The manual lists eight sequences and five distinct
                // patterns - e, f and g are solid again, which is not a mistake in the table.
                case '`': _plotter.Pattern = LinePattern.Solid; return;
                case 'a': _plotter.Pattern = LinePattern.Dotted; return;
                case 'b': _plotter.Pattern = LinePattern.DotDash; return;
                case 'c': _plotter.Pattern = LinePattern.ShortDash; return;
                case 'd': _plotter.Pattern = LinePattern.LongDash; return;
                case 'e':
                case 'f':
                case 'g': _plotter.Pattern = LinePattern.Solid; return;
            }
        }

        // Select Raster Writing Mode Features - ESC / 0 d, ESC / 1 d, ESC / 2 d.
        //
        // THE PARSER SPLITS THESE, AND IT IS RIGHT TO. DEC prints them as four bytes
        // (1/11 2/15 3/0 6/4) but that is not a conformant escape sequence: '/' is an intermediate
        // and '0' is already a valid FINAL, so ECMA-48 ends the sequence at the digit and leaves
        // the 'd' as an ordinary character. The parser recognises structure and never interprets,
        // so the oddity is handled here, in the one terminal that has it, rather than by teaching
        // the parser a vendor's mistake.
        //
        // The manual flags the sequences itself: "NOTE: These sequences are not part of the
        // 4010/4014 protocol." They are DEC's own, and the restrictions section says why they
        // exist - a real 4014 could draw WITHOUT storing, and "the VT300 can simulate
        // write-through functions by using raster writing modes".
        if (parser.Intermediates.Length == 1 && parser.Intermediates[0] == (byte)'/')
        {
            switch ((char)parser.FinalByte)
            {
                case '0': SelectWritingMode(GraphicsWritingMode.Overlay); return;
                case '1': SelectWritingMode(GraphicsWritingMode.Erase); return;
                case '2': SelectWritingMode(GraphicsWritingMode.Complement); return;
            }
        }

        base.HandleEscapeSequence(parser);
    }

    /// <summary>
    /// True while the terminal is ignoring everything the host sends.
    /// </summary>
    /// <remarks>
    /// <para><b>The bypass condition</b></para>
    /// From the 4010/4014 chapter: "This sequence selects the bypass condition. In the bypass
    /// condition, the VT300 ignores any data received from the host." It is set by <c>ESC CAN</c>
    /// and cleared by selecting alpha mode or by <c>ESC ETB</c>.
    ///
    /// <para><b>Not the same as a bare CAN</b></para>
    /// A bare CAN leaves graph mode, which <see cref="TektronixVectorDecoder"/> handles and which
    /// is a different thing entirely. The ESC is the whole difference, exactly as it is for FF and
    /// SUB.
    ///
    /// <para><b>Why a terminal would want this</b></para>
    /// It exists for graphics input. A host arms GIN, the operator picks a point, and the terminal
    /// reports it; bypass stops the host's echo of that report coming back and being drawn as if it
    /// were a command.
    /// </remarks>
    public bool IsBypassed { get; private set; }

    /// <summary>
    /// Drops displayable data while the bypass condition is set.
    /// </summary>
    /// <remarks>
    /// <para><b>An interpretation, and it is marked as one</b></para>
    /// The manual says bypass means the terminal "ignores any data received from the host", and
    /// that is not literally possible: it also says the condition is cleared by selecting alpha
    /// mode and by <c>ESC ETB</c>, and both of those ARE data received from the host. A terminal
    /// that ignored everything could never be got out of bypass.
    ///
    /// So what is dropped here is DISPLAYABLE data - the characters that would be written to the
    /// screen. Escape sequences keep running, which is what makes <c>ESC FF</c> and <c>ESC ETB</c>
    /// able to clear the condition, and it is the only reading of the chapter that is
    /// self-consistent. If a document turns up drawing the line somewhere else, this is the method
    /// to change.
    /// </remarks>
    /// <param name="codepoint">
    /// The character the parser decoded.
    /// </param>
    protected override void HandleCharacter(uint codepoint)
    {
        if (IsBypassed) return;

        // The 'd' that DEC hangs off the end of a raster writing mode sequence. The parser ended
        // the escape at the digit, correctly, so the byte arrives here as a printable character
        // and would put a stray "d" on the screen. Swallowed only when it is the VERY NEXT
        // character, so a host that sent ESC / 0 on its own cannot lose a real 'd' later on.
        if (_expectingRasterWritingSuffix)
        {
            _expectingRasterWritingSuffix = false;
            if (codepoint == 'd') return;
        }

        // THE DEFERRED WRAP HAS TO BE RESOLVED FIRST, or the snap below reads the cursor as it was
        // before the wrap and does nothing. A character that fills the last column leaves the
        // cursor sitting there with the Last Column Flag armed; only resolving it moves the cursor
        // to the next row, and only then is there a column 0 to snap away from.
        ResolveDeferredWrap();

        // At margin 2 the text runs from the CENTRE of the row, not the left edge. Snapping here
        // rather than overriding the wrap covers both ways the cursor can arrive at column 0 - a
        // wrap off the end of the row above, and a carriage return - with one rule.
        if (ActiveMargin == 2 && Cursor.Column < MarginColumn)
        {
            Cursor.Column = MarginColumn;
        }

        bool wasOnTheLastCell = Cursor.Row >= Height - 1 && Cursor.Column >= Width - 1;

        base.HandleCharacter(codepoint);

        // The manual: the active margin switches when "the terminal fills the last row for the
        // currently active margin", and then "the terminal wraps characters around to the top row
        // of the display, at the new margin". Checked on the cell that WAS the last one, because
        // the switch is caused by filling it.
        if (wasOnTheLastCell)
        {
            SwitchMargin();
        }
    }

    /// <summary>
    /// A line feed on the last row is the other thing that switches margin.
    /// </summary>
    /// <remarks>
    /// The manual lists two triggers: filling the last row for the active margin, and "the
    /// terminal receives a line feed on the last row of the display". A storage tube cannot
    /// scroll, so a line feed at the bottom has nowhere to go and starts the other column instead.
    /// </remarks>
    protected override void HandleLineFeed()
    {
        if (Cursor.Row >= Height - 1)
        {
            SwitchMargin();
            return;
        }

        base.HandleLineFeed();

        if (ActiveMargin == 2 && Cursor.Column < MarginColumn)
        {
            Cursor.Column = MarginColumn;
        }
    }

    /// <summary>
    /// True when the next character is the trailing <c>d</c> of a raster writing mode sequence.
    /// </summary>
    private bool _expectingRasterWritingSuffix;

    /// <summary>
    /// Which of the two alpha-mode margins new text starts from.
    /// </summary>
    /// <remarks>
    /// <para><b>Two-column writing, from the manual</b></para>
    /// "In alpha mode, you can use two-column writing. This form of writing uses two margins.
    /// Margin 1 is at the left edge of the display area. Margin 2 is at the center of each row in
    /// the display area."
    ///
    /// A storage tube cannot scroll, so when the screen fills the terminal starts again at the top
    /// - in the other column, "overstriking any characters already displayed". When the second
    /// column fills too it goes back to margin 1 and the whole thing repeats.
    ///
    /// One-column writing is not a separate mode: "If you want one-column writing, then you must
    /// clear the screen before characters wrap around to margin 2." So there is nothing to switch
    /// off, and a host that erases often never sees margin 2 at all.
    /// </remarks>
    public int ActiveMargin { get; private set; } = 1;

    /// <summary>
    /// The column that new rows of text start at, from the active margin.
    /// </summary>
    public int MarginColumn => ActiveMargin == 1 ? 0 : Width / 2;

    /// <summary>
    /// Moves to the other margin and back to the top of the screen.
    /// </summary>
    private void SwitchMargin()
    {
        ActiveMargin = ActiveMargin == 1 ? 2 : 1;
        Cursor.Row = 0;
        Cursor.Column = MarginColumn;
        Cursor.ClearPendingWrap();
    }

    /// <summary>
    /// Selects what drawing does to pixels that are already lit.
    /// </summary>
    /// <param name="mode">
    /// The writing mode.
    /// </param>
    private void SelectWritingMode(GraphicsWritingMode mode)
    {
        _expectingRasterWritingSuffix = true;

        var surface = Graphics?.Output;
        if (surface != null) surface.WritingMode = mode;

        WritingMode = mode;
    }

    /// <summary>
    /// The raster writing mode in force.
    /// </summary>
    public GraphicsWritingMode WritingMode { get; private set; } = GraphicsWritingMode.Overlay;

    /// <summary>
    /// Erases everything - the stored drawing and the text - and homes the cursor.
    /// </summary>
    /// <remarks>
    /// One erase for the whole screen, because that is what a storage tube can do. Every gnuplot
    /// stream in the test corpus opens with this.
    /// </remarks>
    public void EraseScreen()
    {
        _drawing.Clear();
        _crosshair.Clear();

        Buffer.Clear();
        Cursor.Home();

        // Erasing returns the terminal to alpha mode: the beam is back at the top of the screen
        // and the host starts again.
        _plotter.Reset();

        // "Selecting alpha mode erases the screen, moves the current position to the upper-left
        // corner, activates margin 1, and clears the bypass condition. Selecting alpha mode also
        // resets the pattern register and intensity."
        //
        // ALL FIVE, now that margins exist. The erase and the home are above, _plotter.Reset puts
        // the pattern register back to solid, and these two finish it. The gap document warned
        // that margins would have to be reset here when they arrived, so that ESC FF could not
        // quietly leave state behind - this is that.
        IsBypassed = false;
        ActiveMargin = 1;
        SelectWritingMode(GraphicsWritingMode.Overlay);
        _expectingRasterWritingSuffix = false;   // nothing follows an erase

        Graphics?.Composite();
        OnInvalidated();
    }

    /// <summary>
    /// Raises the crosshair and arms GIN, so the next pick is reported.
    /// </summary>
    public void EnterGinMode()
    {
        _crosshair.IsVisible = true;
        Gin.Arm();
        OnInvalidated();
    }

    /// <summary>
    /// Moves the crosshair and redraws it on its own plane.
    /// </summary>
    /// <param name="surfaceX">
    /// Pointer position in surface pixels.
    /// </param>
    /// <param name="surfaceY">
    /// Pointer position in surface pixels.
    /// </param>
    public void MoveCrosshair(int surfaceX, int surfaceY)
    {
        Gin.MoveCrosshair(surfaceX, surfaceY);

        // Redrawn on its OWN plane, so erasing it cannot touch the drawing underneath. That
        // separation is the whole reason the plane model exists.
        _crosshair.Clear();
        if (!_crosshair.IsVisible) return;

        _crosshair.Surface.DrawLine(0, surfaceY, _crosshair.Width - 1, surfaceY, _plotter.DrawColour);
        _crosshair.Surface.DrawLine(surfaceX, 0, surfaceX, _crosshair.Height - 1, _plotter.DrawColour);

        Graphics?.Composite();
        OnInvalidated();
    }

    /// <summary>
    /// Reports status and beam position: five bytes, and the only way a host identifies a 4014.
    /// </summary>
    /// <remarks>
    /// The report carries the crosshair position whether or not a crosshair is up, so GIN is armed
    /// for the length of this reply regardless of what the host asked for earlier.
    /// </remarks>
    private void SendIdentificationReply()
    {
        Span<byte> reply = stackalloc byte[TektronixGinEncoder.NorskDataReportLength];

        Gin.Arm();
        if (!Gin.TryBuildReport(IdentificationStatusByte, reply, out int length)) return;

        SendResponse(reply.Slice(0, length));
    }

    /// <summary>
    /// Moves the text cursor to the character cell holding the last drawn point.
    /// </summary>
    private void PlaceCursorAtLastVector()
    {
        var vectors = _plotter.Vectors;

        // Graphics space is 1024 x 780 regardless of how many character cells are on screen, so the
        // cell is worked out by proportion rather than by any fixed ratio.
        int column = vectors.LastX * Width / GraphicsWidth;

        // Y runs UP in the terminal's own space and DOWN the screen, so the row flips.
        int row = (GraphicsHeight - 1 - vectors.LastY) * Height / GraphicsHeight;

        if (column < 0) column = 0;
        if (column > Width - 1) column = Width - 1;
        if (row < 0) row = 0;
        if (row > Height - 1) row = Height - 1;

        Cursor.Column = column;
        Cursor.Row = row;
    }

    /// <inheritdoc/>
    public override void Reset()
    {
        // The graphics side has its own state - a drawing, the crosshair, the decoder's mode - and
        // a reset clears all of it.
        _drawing.Clear();
        _crosshair.Clear();
        _crosshair.IsVisible = false;
        _plotter.Reset();
        Gin.Disarm();
        Graphics?.Composite();

        base.Reset();
    }

    /// <inheritdoc/>
    public override string ToString() => "TEK4014";
}
