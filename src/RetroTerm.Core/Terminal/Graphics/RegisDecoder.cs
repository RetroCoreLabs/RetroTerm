using System;
using System.Collections.Generic;

namespace RetroTerm.Core.Terminal.Graphics;

/// <summary>
/// Decodes ReGIS, the DEC vector graphics language, and draws it onto a surface.
/// </summary>
/// <remarks>
/// <para><b>What ReGIS looks like</b></para>
/// A letter names a command, brackets carry coordinates and parentheses carry options:
/// <code>
///   S(E)              erase the screen
///   P[100,100]        move to a point without drawing
///   V[200,200]        draw a line from where we are to there
///   V[+50,0][0,+50]   two more lines, each relative to the last point
///   W(I3)             draw in colour register 3 from now on
///   C[150,150]        a circle centred on the current point, through that one
/// </code>
/// Whitespace between commands is ignored, which is why real ReGIS arrives wrapped across lines.
///
/// <para><b>Y runs DOWN</b></para>
/// Unlike Tektronix, whose origin is the bottom left, ReGIS counts from the top left exactly as a
/// surface does. Nothing is flipped here, and that is worth stating because the neighbouring
/// decoder in this folder flips everything.
///
/// <para><b>What is implemented, and what is counted</b></para>
/// The commands that draw and can be checked: S, P, V, C, W. Macrographs (@) are stored and
/// replayed, because their bodies are made of exactly those commands. Everything else - text, the
/// load and report commands, filled figures - is COUNTED rather than guessed at, the same
/// instrument the Norsk Data module uses. Pointed at a real host, <see cref="UnhandledCommands"/>
/// says which commands actually matter.
/// </remarks>
public sealed class RegisDecoder
{
    /// <summary>
    /// The coordinate space a VT340 addresses in ReGIS.
    /// </summary>
    public const int DefaultWidth = 800;

    /// <summary>
    /// See <see cref="DefaultWidth"/>.
    /// </summary>
    public const int DefaultHeight = 480;

    /// <summary>
    /// How many macrograph slots a VT300 has - one per letter of the alphabet.
    /// </summary>
    /// <remarks>
    /// "The VT300 can store up to 26 macrographs. Each macrograph is identified by a letter of the
    /// alphabet. The identifying letter is not case sensitive."
    /// </remarks>
    public const int MacrographCount = 26;

    /// <summary>
    /// How deep one macrograph may call another.
    /// </summary>
    /// <remarks>
    /// "You can nest macrographs up to 16 levels deep. However, a macrograph cannot call itself."
    /// Both halves are enforced - the depth by this limit, the self-call by the busy flags.
    /// </remarks>
    public const int MacrographNestLimit = 16;

    private readonly Dictionary<char, int> _unhandled = new Dictionary<char, int>();

    /// <summary>
    /// The 26 stored macrograph definitions, indexed by call letter minus 'A'.
    /// </summary>
    /// <remarks>
    /// Allocated only when a host defines one, which most streams never do. Held as strings because
    /// a definition arrives once and is replayed many times: the replay reads
    /// <see cref="System.MemoryExtensions.AsSpan(string)"/> and allocates nothing, which is where the cost would be.
    /// </remarks>
    private string?[]? _macrographs;

    /// <summary>
    /// Which macrographs are part-way through being replayed, so one cannot call itself.
    /// </summary>
    private bool[]? _macrographBusy;

    /// <summary>
    /// The most vertices a polygon fill can use.
    /// </summary>
    /// <remarks>
    /// "You can use up to 256 vertices. ReGIS ignores additional vertices."
    /// </remarks>
    public const int MaxFillVertices = 256;

    /// <summary>
    /// How many straight sides stand in for a circle inside a polygon fill.
    /// </summary>
    /// <remarks>
    /// The fill takes vertices, and a circle has none, so a filled circle is a many-sided polygon.
    /// Sixty-four sides keeps the error under a pixel out to a radius of about 800 - the whole
    /// width of the ReGIS coordinate space - because the gap between a chord and its arc is
    /// r(1 - cos(pi/64)), about r/830. It also stays inside the manual's 256-vertex ceiling with
    /// room for the rest of the figure.
    /// </remarks>
    private const int CircleSides = 64;

    /// <summary>
    /// Vertices collected while a polygon fill command is being read, as x,y pairs.
    /// </summary>
    /// <remarks>
    /// Allocated once on the first fill and reused, because a host that fills one shape usually
    /// fills many.
    /// </remarks>
    private int[]? _fillVertices;

    private int _fillVertexCount;

    /// <summary>
    /// True while an F command's body is being read, so V and C collect vertices instead of drawing.
    /// </summary>
    private bool _collectingFill;

    private int _x;
    private int _y;

    /// <summary>
    /// The colour map. Shared with Sixel, not "in spirit" but the SAME object - a VT340 has one map
    /// of sixteen locations and both graphics languages write to it. See
    /// <see cref="GraphicsColorMap"/>.
    /// </summary>
    private readonly GraphicsColorMap _colours;

    private int _currentRegister = 3;

    /// <summary>
    /// How many bits the VT300 pattern memory holds.
    /// </summary>
    /// <remarks>
    /// "The VT300 has an 8-bit pattern memory that lets you define the appearance of lines and
    /// shaded areas in ReGIS drawings. ReGIS uses this pattern for all writing tasks. Each bit in
    /// the pattern turns one pixel on or off."
    /// </remarks>
    private const int PatternBits = 8;

    /// <summary>
    /// The pattern memory, first-drawn bit lowest. All ones at power-on, as the manual says.
    /// </summary>
    private ushort _patternMask = 0b11111111;

    /// <summary>
    /// How many pixels each bit of the pattern covers.
    /// </summary>
    /// <remarks>
    /// <para><b>Two, not one, and the manual says so twice</b></para>
    /// "The minimum value is 1, the maximum value is 16. The default value is 2." And the note under
    /// figure 3-12 spells out the whole default write state as <c>W(F3, N0, V, I0, P1(M2))</c> -
    /// which lists the multiplier as 2 alongside the other power-on values.
    ///
    /// Figures 3-2 and 3-3 are drawn at a multiplier of 1, but they say so in their own notes: they
    /// are showing the BIT PATTERNS, not the default state.
    ///
    /// This is taken from the manual and has not been confirmed against hardware - the four
    /// registest drawings all give the multiplier explicitly, so none of them can tell the
    /// difference.
    /// </remarks>
    private const int DefaultPatternMultiplier = 2;

    /// <summary>
    /// The largest multiplier the hardware accepts.
    /// </summary>
    private const int MaxPatternMultiplier = 16;

    /// <summary>
    /// How many pixels each bit of the pattern covers.
    /// </summary>
    private int _patternMultiplier = DefaultPatternMultiplier;

    /// <summary>
    /// Which of the four bitplanes drawing may change - the <c>W(F)</c> option.
    /// </summary>
    /// <remarks>
    /// "The default setting lets the terminal write to all planes", so this starts at 15.
    /// </remarks>
    private int _planeMask = 15;

    /// <summary>
    /// Whether negative pattern control is on - the <c>W(N)</c> option.
    /// </summary>
    /// <remarks>
    /// "The negative pattern control changes all 1s in pattern memory to 0s, and changes all 0s to
    /// 1s. The default setting for negative pattern control is off."
    /// </remarks>
    private bool _negatePattern;

    /// <summary>
    /// The writing style, from the <c>V</c>, <c>C</c> and <c>E</c> write control options.
    /// </summary>
    private GraphicsWritingMode _writingMode = GraphicsWritingMode.Overlay;

    /// <summary>
    /// Set when a screen command moved an output map location, so the surface can be repainted.
    /// </summary>
    private bool _colourMapChanged;

    /// <summary>
    /// The output map location that is the page background.
    /// </summary>
    private const int BackgroundMapLocation = 0;

    /// <summary>
    /// True when this payload wrote output map location 0, the page background.
    /// </summary>
    /// <remarks>
    /// <para><b>Why the caller wants to know</b></para>
    /// A pixel nothing has drawn on holds code 0, so what location 0 looks like IS what the page
    /// looks like. The emulator turns this into the terminal's default background rather than into
    /// pixels, because painting it into the surface is wrong for a reason the corpus already
    /// proved: an untouched pixel must stay untouched, or a plane-masked write over fresh screen
    /// paints an opaque bar where the hardware shows only recoloured text.
    /// <para><b>The evidence</b></para>
    /// <c>cat-original.six</c> sets locations 0 to 3 through ReGIS and then sends a Sixel image
    /// that defines no colours of its own. Its hardware capture has the dark teal-green of location
    /// 0 across the whole screen. It is the only fixture in the corpus that uses ReGIS to write the
    /// map at all, which is why nothing else can be disturbed by this.
    /// <para><b>Cleared by the reader</b></para>
    /// The caller sets it back to false once it has acted, the same way the repaint flag works.
    /// </remarks>
    public bool BackgroundMapLocationChanged { get; set; }

    /// <summary>
    /// Where shading runs to, or <see cref="GraphicsShading.None"/> when shading is off.
    /// </summary>
    private int _shadeReference = GraphicsShading.None;

    /// <summary>
    /// True when the shading reference line is a column rather than a row.
    /// </summary>
    private bool _shadeVertical;

    /// <summary>
    /// The character shading fills with, or 0 for a solid fill.
    /// </summary>
    /// <remarks>
    /// Chapter 3's shading character select. Kept as the character rather than as the bits, because
    /// which bits it means depends on the character SET, and a text command may change that between
    /// the shading control and the drawing that uses it.
    /// </remarks>
    private char _shadeCharacter;

    /// <summary>
    /// How far text sits off its baseline, in HALF display cells along the baseline's own axes.
    /// </summary>
    /// <remarks>
    /// <para><b>Chapter 7, "PV Spacing - Subscripts, Superscripts, and Overstrikes"</b></para>
    /// "In text commands, each PV value defines a movement equal to one half of the defined display
    /// cell, in the direction specified. The PV multiplication factor does not affect this
    /// movement." The values the manual names:
    ///  - 1 superscript, up and away from the previous character.
    ///  - 2 superscript, straight up.
    ///  - 4 overstrike - "A 44 value moves the character back over the previous character cell",
    ///    so one 4 is half a cell back and two make a whole one.
    ///  - 6 subscript, straight down.
    ///  - 7 subscript, down and away.
    ///  - 3 and 5 are allowed and "partially overwrite the previous character".
    ///  - 0 moves forward half a cell along the baseline, "useful for inserting visually pleasing
    ///    space between adjacent characters".
    ///
    /// <para><b>It accumulates and it persists</b></para>
    /// "ReGIS uses that PV value for all following text strings, until you change the value. You
    /// can return to the original baseline by selecting the PV value for the opposite function."
    /// So this is a running total, and 2 followed by 6 comes back to zero on its own - across
    /// SEPARATE text commands, because T2 and T6 are two of them and the example depends on it.
    ///
    /// An attempt to reset it per command on 28 August 2026, to explain a difference in the
    /// registest-grid comparison sheet, was refuted by that example and reverted. See
    /// <see cref="HandleText"/>.
    ///
    /// <para><b>Why half-cells rather than pixels</b></para>
    /// "PV spacing is relative to the baseline. If you tilt the baseline, PV spacing rotates with
    /// that baseline." Kept in baseline axes and turned into pixels at drawing time, so a tilt
    /// applied after the spacing still carries it round.
    /// </remarks>
    private int _textPvRight;

    /// <summary>
    /// See <see cref="_textPvRight"/>. Down is positive, matching surface coordinates.
    /// </summary>
    private int _textPvDown;

    /// <summary>
    /// Applies one PV spacing digit to the running text offset.
    /// </summary>
    /// <param name="direction">
    /// A pixel vector direction, 0 to 7.
    /// </param>
    private void ApplyPvSpacing(int direction)
    {
        if ((uint)direction >= (uint)PvStepX.Length) return;

        // The same eight compass points the drawing commands use, so one table serves both. Down is
        // the opposite sign to the vertical step, which is negative for north.
        _textPvRight += PvStepX[direction];
        _textPvDown += PvStepY[direction];
    }

    /// <summary>
    /// The last parse error's code, and the character blamed for it.
    /// </summary>
    /// <remarks>
    /// <para><b>Table 10-1, read off the rendered page</b></para>
    /// The report command's error condition option answers <c>"N,M"</c>, where N is the code and M
    /// is "the decimal ASCII code of the character flagged as the cause of the error or 0, as noted
    /// for each error code". The ten codes are:
    ///  - 0 no error - always M 0. "No error detected since the last resynchronization character."
    ///  - 1 ignore character - M is the ignored character. "An unexpected character was found and ignored."
    ///  - 2 extra option coordinates - always 0. More than two coordinate pairs in S(H[X,Y][X,Y]).
    ///  - 3 extra coordinate values - always 0. More than two values in [X,Y].
    ///  - 4 alphabet out of range - always 0. L(A n) outside 0 to 3.
    ///  - 5 and 6 are reserved.
    ///  - 7 begin/start overflow - M is (B) or (S). More than 16 stacked.
    ///  - 8 begin/start underflow - M is (E). An (E) with no matching (B).
    ///  - 9 text standard size error - always 0. A size below 0 or above 16.
    ///
    /// <para><b>Which are actually raised here</b></para>
    /// All eight that a terminal can raise: 1, 2, 3, 4, 7, 8 and 9, plus 0 for "no error". 5 and 6
    /// are reserved by DEC and stay unused, which is why nothing sets them.
    /// The wording above is quoted from Table 10-1 on page 176, read off the page rendered at 150
    /// dpi - the table's own text layer comes out as noise, and the meanings are too specific to
    /// paraphrase from memory. It was that rendering which showed the overflow rule is "Extra (B) or
    /// (S) options were ignored", so the seventeenth push is DROPPED rather than replacing the
    /// oldest - a detail that decides where the drawing point ends up.
    /// </remarks>
    private int _errorCode;

    /// <summary>
    /// The character blamed for <see cref="_errorCode"/>, or 0 when the code always reports 0.
    /// </summary>
    private int _errorCharacter;

    /// <summary>
    /// Records a parse error, keeping the LAST one.
    /// </summary>
    /// <remarks>
    /// "This option tells ReGIS to report the last error detected by the parser", so a later error
    /// replaces an earlier one rather than being dropped.
    /// </remarks>
    /// <param name="code">
    /// A code from Table 10-1.
    /// </param>
    /// <param name="character">
    /// The character to blame, or 0 when the code always reports 0.
    /// </param>
    private void RecordError(int code, char character)
    {
        _errorCode = code;
        _errorCharacter = character == '\0' ? 0 : character;
    }

    /// <summary>
    /// True when replace writing is selected, so the pattern's 0 bits write the background.
    /// </summary>
    private bool _replaceWriting;

    /// <summary>
    /// The output map location the background is drawn from - the <c>S(I n)</c> option.
    /// </summary>
    /// <remarks>
    /// Location 0 at power-on. hackerb9's own table of the default map names it "Black - Screen
    /// Background", and the screen erase command clears to it.
    /// </remarks>
    private int _backgroundRegister;

    /// <summary>
    /// How far through the pattern the next drawing step is.
    /// </summary>
    /// <remarks>
    /// "The VT300 starts each writing task from the first position in pattern memory. The writing
    /// cycles through the complete 8-bit pattern, unless you use a new command key letter." So this
    /// is reset when a command letter arrives and carried across the coordinate groups within one
    /// command - which is what makes a polyline one dashed line rather than a row of separately
    /// dashed segments.
    /// </remarks>
    private int _patternPhase;

    /// <summary>
    /// Commands recognised as commands but not acted on, and how often each arrived.
    /// </summary>
    /// <remarks>
    /// A count rather than a log line, for the same reason the ND module keeps one: pointed at a
    /// real host it answers "which commands does this program actually use", which is a better
    /// guide to what to build next than working down the manual in order.
    /// </remarks>
    public IReadOnlyDictionary<char, int> UnhandledCommands => _unhandled;

    /// <summary>
    /// Where the drawing point is now.
    /// </summary>
    public int CurrentX => _x;

    /// <summary>
    /// See <see cref="CurrentX"/>.
    /// </summary>
    public int CurrentY => _y;

    /// <summary>
    /// Which graphics input mode is running, if any.
    /// </summary>
    private RegisGraphicsInputMode _inputMode;

    /// <summary>
    /// Where the graphics input cursor is, in ReGIS screen coordinates.
    /// </summary>
    /// <remarks>
    /// Separate from the drawing point on purpose. Chapter 1 says the graphics cursor "indicates the
    /// active screen location", and the input cursor starts there - but moving it must NOT drag the
    /// drawing point around, or every arrow keypress would silently move where the next vector
    /// starts from.
    /// </remarks>
    private int _inputCursorX;

    /// <summary>
    /// See <see cref="_inputCursorX"/>.
    /// </summary>
    private int _inputCursorY;

    /// <summary>
    /// True once <c>R(P(I))</c> has asked for a report and no keystroke has answered it yet.
    /// </summary>
    /// <remarks>
    /// One-shot only. "In one-shot mode, the terminal cannot send a position report until the
    /// application sends a request to the terminal", and "After sending the report position
    /// interactive command, the application does not receive a report until you press an active key
    /// or locator button." So a keypress before the request is not a report - it is just a keypress.
    /// </remarks>
    private bool _positionReportRequested;

    /// <summary>
    /// How wide the surface last drawn on was, for wrapping the input cursor.
    /// </summary>
    private int _screenWidth = DefaultWidth;

    /// <summary>
    /// See <see cref="_screenWidth"/>.
    /// </summary>
    private int _screenHeight = DefaultHeight;

    /// <summary>
    /// Which shape the graphics input cursor is drawn as.
    /// </summary>
    private RegisCursorStyle _inputCursorStyle = RegisCursorStyle.Crosshair;

    /// <summary>
    /// Whether the host asked for the graphics OUTPUT cursor to be shown.
    /// </summary>
    /// <remarks>
    /// Read and remembered, never drawn. The output cursor is the mark a real VT340 shows while it
    /// waits for the host's next ReGIS command; this terminal shows no such mark. Kept so a host that
    /// sets it is answered honestly rather than told the option was not understood.
    /// </remarks>
    private bool _outputCursorVisible;

    /// <summary>
    /// The output cursor style number the host last selected with <c>S(C(H n))</c>.
    /// </summary>
    /// <remarks>
    /// Chapter 2: omitted and "0 or 1" are the diamond, 2 is the crosshair. Stored as the raw number
    /// rather than a shape, because nothing draws it and inventing a mapping for a thing that is
    /// never drawn would be inventing a fact.
    /// </remarks>
    private int _outputCursorStyleNumber;

    /// <summary>
    /// Which shape the graphics input cursor is drawn as.
    /// </summary>
    public RegisCursorStyle InputCursorStyle => _inputCursorStyle;

    /// <summary>
    /// Whether the host asked for the graphics output cursor. Nothing draws it.
    /// </summary>
    public bool OutputCursorVisible => _outputCursorVisible;

    /// <summary>
    /// The output cursor style number the host last selected. Nothing draws it.
    /// </summary>
    public int OutputCursorStyleNumber => _outputCursorStyleNumber;

    /// <summary>
    /// Which graphics input mode the terminal is in.
    /// </summary>
    public RegisGraphicsInputMode InputMode => _inputMode;

    /// <summary>
    /// True while the host's data must be held rather than processed.
    /// </summary>
    /// <remarks>
    /// "In one-shot mode, the terminal suspends processing of new data from the application until
    /// ReGIS sends a position report. The terminal buffers any data received from the application in
    /// this mode." Multiple mode does the opposite - it "lets the terminal perform graphics input and
    /// output at the same time" - so only one-shot suspends.
    /// </remarks>
    public bool IsHostDataSuspended => _inputMode == RegisGraphicsInputMode.OneShot;

    /// <summary>
    /// Where the graphics input cursor is.
    /// </summary>
    public int InputCursorX => _inputCursorX;

    /// <summary>
    /// See <see cref="InputCursorX"/>.
    /// </summary>
    public int InputCursorY => _inputCursorY;

    /// <summary>
    /// How far one arrow keypress moves the graphics input cursor.
    /// </summary>
    /// <remarks>
    /// "arrow key - The cursor moves one pixel in the direction of the arrow - up, down, left or
    /// right."
    /// </remarks>
    public const int InputCursorStep = 1;

    /// <summary>
    /// How far a shifted arrow keypress moves the graphics input cursor.
    /// </summary>
    /// <remarks>
    /// "Shift-arrow key - The cursor moves 10 pixels in the direction of the arrow."
    /// </remarks>
    public const int InputCursorShiftStep = 10;

    /// <summary>
    /// Moves the graphics input cursor, wrapping at the screen edges.
    /// </summary>
    /// <param name="dx">
    /// Pixels to move right; negative moves left.
    /// </param>
    /// <param name="dy">
    /// Pixels to move down; negative moves up.
    /// </param>
    /// <remarks>
    /// "If you move the cursor past a screen boundary, the cursor wraps to the other side of the
    /// screen." Wrapping rather than clamping, so a held arrow key never parks the cursor in a
    /// corner and stops.
    ///
    /// Does nothing outside one-shot mode. Chapter 15: "You cannot use the four arrow keys to
    /// position the input cursor as you can in ReGIS one-shot graphics input mode. If you press an
    /// arrow key in multiple mode, the terminal sends that key's escape sequence to the host."
    /// </remarks>
    public void MoveInputCursor(int dx, int dy)
    {
        if (_inputMode != RegisGraphicsInputMode.OneShot) return;

        _inputCursorX = Wrap(_inputCursorX + dx, _screenWidth);
        _inputCursorY = Wrap(_inputCursorY + dy, _screenHeight);
    }

    /// <summary>
    /// Puts the graphics input cursor at an absolute point, clamping to the screen.
    /// </summary>
    /// <param name="x">
    /// Where to put it across, in ReGIS screen coordinates.
    /// </param>
    /// <param name="y">
    /// Where to put it down, in ReGIS screen coordinates.
    /// </param>
    /// <remarks>
    /// <para><b>Clamped, where a move WRAPS</b></para>
    /// <see cref="MoveInputCursor"/> wraps because the manual says a held arrow key must not park
    /// the cursor in a corner. An absolute placement is a different gesture with a different right
    /// answer: it comes from a pointing device, the operator meant the point they indicated, and
    /// wrapping a click near the edge to the far side of the screen would be nonsense. Off-screen
    /// input is pinned to the nearest edge instead.
    ///
    /// <para><b>Not in the manual, and said so</b></para>
    /// Chapter 15 describes only the arrow keys. Placing the cursor with a pointing device is OURS,
    /// decided with Ronny on 31 August 2026 because crossing the 800-pixel plane is 800 unshifted
    /// presses or 80 shifted, and a click is one action. Whether a real VT330/VT340 accepted a
    /// mouse or tablet as a ReGIS locator is NOT known here - nothing in the specifications held in
    /// this repository covers it.
    ///
    /// Does nothing outside one-shot mode, for the same reason the arrow keys do nothing there.
    /// </remarks>
    public void SetInputCursor(int x, int y)
    {
        if (_inputMode != RegisGraphicsInputMode.OneShot) return;

        _inputCursorX = Clamp(x, _screenWidth);
        _inputCursorY = Clamp(y, _screenHeight);
    }

    /// <summary>
    /// Pins a coordinate inside the screen, for an absolute placement.
    /// </summary>
    /// <param name="value">
    /// The requested coordinate.
    /// </param>
    /// <param name="size">
    /// The screen extent on that axis.
    /// </param>
    /// <returns>
    /// The coordinate, brought inside the screen.
    /// </returns>
    private static int Clamp(int value, int size)
    {
        if (size <= 0) return 0;
        if (value < 0) return 0;
        if (value >= size) return size - 1;
        return value;
    }

    /// <summary>
    /// Brings a coordinate back inside the screen by wrapping it round.
    /// </summary>
    /// <param name="value">
    /// The coordinate after the move.
    /// </param>
    /// <param name="size">
    /// The screen extent on that axis.
    /// </param>
    /// <returns>
    /// The wrapped coordinate.
    /// </returns>
    private static int Wrap(int value, int size)
    {
        if (size <= 0) return 0;

        // C# gives a negative remainder for a negative left operand, so -1 % 800 is -1 rather than
        // 799. The second modulo after the add is what turns that back into a screen coordinate.
        int wrapped = value % size;
        if (wrapped < 0) wrapped += size;
        return wrapped;
    }

    /// <summary>
    /// Answers a pending position request with a keystroke, and leaves one-shot mode.
    /// </summary>
    /// <param name="keystroke">
    /// What the key sends to the host - one ASCII character, or the whole control function for a
    /// key that sends more than one byte.
    /// </param>
    /// <returns>
    /// True when a report was sent.
    /// </returns>
    /// <remarks>
    /// <para><b>Chapter 15, "ReGIS Locator Reports"</b></para>
    /// "Locator reports begin with the code(s) of the active non-arrow key or locator button pressed.
    /// Following this code is the current position of the input cursor. The terminal sends the input
    /// cursor position as an absolute bracketed extent in user coordinates. The report ends with the
    /// carriage return character (CR)." The worked example is <c>A[102,200]CR</c> - the user pressed
    /// A with the cursor at 102,200.
    ///
    /// Then, from chapter 10: "The input cursor disappears from the screen, and the terminal exits
    /// one-shot mode."
    /// </remarks>
    public bool SendInputReport(string keystroke)
    {
        if (_inputMode != RegisGraphicsInputMode.OneShot) return false;
        if (!_positionReportRequested) return false;

        Report((keystroke ?? string.Empty) + "[" + _inputCursorX + "," + _inputCursorY + "]");
        _positionReportRequested = false;
        _inputMode = RegisGraphicsInputMode.Off;
        return true;
    }

    /// <summary>
    /// Leaves graphics input mode without sending a report.
    /// </summary>
    /// <remarks>
    /// <para><b>Not in the manual - ours, and deliberately so</b></para>
    /// A real VT340 has no way out of one-shot mode except answering the request. Here a suspended
    /// session looks dead and the only cure would be disconnecting, so ESC cancels. Decided with
    /// Ronny on 2026-08-20 along with keys-only input. Nothing is reported to the host, so an
    /// application counting its <c>R(P(I))</c> requests will notice one went unanswered - which is
    /// the honest outcome, since the operator declined to answer it.
    /// </remarks>
    public void CancelInputMode()
    {
        _inputMode = RegisGraphicsInputMode.Off;
        _positionReportRequested = false;
    }

    /// <summary>
    /// Builds a decoder with a colour map of its own.
    /// </summary>
    public RegisDecoder()
        : this(new GraphicsColorMap())
    {
    }

    /// <summary>
    /// Builds a decoder sharing a colour map with whoever else holds it.
    /// </summary>
    /// <param name="colours">
    /// The map. A terminal passes the SAME instance here and to its Sixel decoder.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="colours"/> is null.
    /// </exception>
    public RegisDecoder(GraphicsColorMap colours)
    {
        // Register 3 - green in DEC's default map - is the colour a program gets if it never asks
        // for one.
        _colours = colours ?? throw new ArgumentNullException(nameof(colours));
    }

    /// <summary>
    /// The colour map this decoder draws from, so a terminal can hand the same one to Sixel.
    /// </summary>
    public GraphicsColorMap Colours => _colours;

    /// <summary>
    /// The colour a register holds.
    /// </summary>
    /// <param name="index">
    /// Register number.
    /// </param>
    /// <returns>
    /// The colour, or transparent when the number is outside the register file.
    /// </returns>
    public GraphicsColor Register(int index)
        => _colours.Register(index);

    /// <summary>
    /// Draws a ReGIS command string onto a surface.
    /// </summary>
    /// <param name="commands">
    /// The characters between the DCS introducer and its terminator.
    /// </param>
    /// <param name="surface">
    /// Where the pixels go. Everything clips, so a drawing off the edge is not an error.
    /// </param>
    public void Decode(ReadOnlySpan<char> commands, IGraphicsSurface surface)
    {
        if (surface == null) throw new ArgumentNullException(nameof(surface));

        Decode(commands, surface, 0);
    }

    /// <summary>
    /// The body of <see cref="Decode(ReadOnlySpan{char}, IGraphicsSurface)"/>, re-entered when a
    /// macrograph is invoked.
    /// </summary>
    /// <param name="commands">
    /// The command characters to run.
    /// </param>
    /// <param name="surface">
    /// Where the pixels go.
    /// </param>
    /// <param name="depth">
    /// How many macrographs deep this call already is.
    /// </param>
    private void Decode(ReadOnlySpan<char> commands, IGraphicsSurface surface, int depth)
    {
        // Remembered so the graphics input cursor knows where the screen edges are. It has to come
        // from the surface rather than DefaultWidth/DefaultHeight, because a host can resize the
        // addressing with S(A[...]) and the cursor must wrap at the edge the operator can see.
        if (surface.Width > 0) _screenWidth = surface.Width;
        if (surface.Height > 0) _screenHeight = surface.Height;

        int i = 0;
        while (i < commands.Length)
        {
            char c = commands[i];

            if (c == ' ' || c == '\r' || c == '\n' || c == '\t' || c == ';' || c == ',')
            {
                // ";" is the RESYNCHRONIZATION character, and chapter 10 gives it a job beyond
                // separating commands: "You can use the resynchronization character (;) to clear
                // errors." Skipping it as plain whitespace lost that, so R(E) could never go back
                // to reporting no error once anything had gone wrong.
                if (c == ';') RecordError(0, '\0');

                i++;
                continue;
            }

            // "The VT300 starts each writing task from the first position in pattern memory. The
            // writing cycles through the complete 8-bit pattern, unless you use a new command key
            // letter." So a command letter restarts the pattern, and the coordinate groups WITHIN
            // one command carry it - which is what makes a polyline one dashed line rather than a
            // row of separately dashed segments.
            if (char.IsLetter(c)) _patternPhase = 0;

            // The write state lives on the surface, because the plane mask and the writing style
            // decide what a single pixel write DOES - they cannot be applied afterwards. Pushed
            // once per command rather than once per pixel: three property sets is nothing beside
            // the drawing that follows, and it keeps one copy of the rule.
            ApplyWriteState(surface);

            switch (c)
            {
                case 'S':
                case 's':
                    i = HandleScreen(commands, i + 1, surface);

                    // Changing an output map location repaints everything already drawn in that
                    // code. On the hardware nothing is redrawn at all - pixel memory holds the code
                    // and the map is consulted as the screen is scanned - so this is that same
                    // effect, done once instead of sixty times a second.
                    if (_colourMapChanged)
                    {
                        _colourMapChanged = false;
                        ApplyWriteState(surface);
                        surface.ReapplyColourMap();
                    }

                    continue;

                case 'P':
                case 'p':
                    i = HandlePosition(commands, i + 1);
                    continue;

                case 'V':
                case 'v':
                    i = HandleVector(commands, i + 1, surface);
                    continue;

                case 'C':
                case 'c':
                    i = HandleCurve(commands, i + 1, surface);
                    continue;

                case 'W':
                case 'w':
                    i = HandleWrite(commands, i + 1);
                    continue;

                case 'F':
                case 'f':
                    i = HandleFill(commands, i + 1, surface, depth);
                    continue;

                case 'T':
                case 't':
                    i = HandleText(commands, i + 1, surface);
                    continue;

                case 'L':
                case 'l':
                    i = HandleLoad(commands, i + 1);
                    continue;

                case 'R':
                case 'r':
                    i = HandleReport(commands, i + 1);
                    continue;

                case '@':
                    i = HandleMacrograph(commands, i + 1, surface, depth);
                    continue;
            }

            // A letter this decoder does not implement. Counted, and its bracket and parenthesis
            // groups skipped so the rest of the drawing still arrives.
            if (char.IsLetter(c))
            {
                Count(char.ToUpperInvariant(c));
                i = SkipArguments(commands, i + 1);
                continue;
            }

            // Table 10-1 code 1: "An unexpected character was found and ignored", and M is the
            // ignored character itself rather than 0. This is the only place a character is dropped
            // without being understood at all.
            RecordError(1, c);
            i++;
        }
    }

    /// <summary>
    /// The text command's options and glyphs, which persist between commands.
    /// </summary>
    public RegisTextState Text { get; } = new RegisTextState();

    /// <summary>
    /// <c>T</c> - draws text, and sets the options that decide how it is drawn.
    /// </summary>
    /// <param name="commands">
    /// The whole command string.
    /// </param>
    /// <param name="i">
    /// Index just past the <c>T</c>.
    /// </param>
    /// <param name="surface">
    /// Where the glyphs go.
    /// </param>
    /// <returns>
    /// Index just past the command.
    /// </returns>
    /// <remarks>
    /// <para><b>What is implemented, from chapter 7</b></para>
    ///  - Text strings in either quote, with a doubled quote standing for one inside the string,
    ///    and a comma joining two strings.
    ///  - The four control characters ReGIS recognises inside a string: CR returns to the column
    ///    the command started at, LF drops one display cell, BS steps back one character position,
    ///    HT steps forward one.
    ///  - <c>A</c> character set, <c>S</c> standard size and display cell, <c>U</c> unit cell,
    ///    <c>H</c> height multiplier, <c>M</c> size multiplier, <c>D</c> string tilt, <c>I</c>
    ///    italics.
    ///
    /// <para><b>What is not, and is counted rather than pretended</b></para>
    /// PV spacing (subscripts and superscripts), temporary text control and temporary write
    /// control. Each is a bracketed option that is read and skipped, and the letter is counted in
    /// <see cref="UnhandledCommands"/> so a real host says which of them it actually uses.
    /// </remarks>
    private int HandleText(ReadOnlySpan<char> commands, int i, IGraphicsSurface surface)
    {
        // CR returns to "the horizontal position where the current text writing command started",
        // so it is remembered per command rather than per string.
        int homeColumn = _x;

        // A write control inside a T lasts for that command only, the same way one inside a V does.
        // Restored on the way out, including when the command runs to the end of the string.
        // PV SPACING IS NOT RESET HERE, and an attempt to reset it on 28 August 2026 was WRONG.
        // The reasoning was that hackerb9's registest.sh raises its bottom-edge labels with T22 and
        // then writes the grid labels with a plain T(B S1), and in the photograph the grid labels
        // are not raised while ours are - four half-cells, about 40 pixels, measured off
        // compare-registest-grid.png.
        //
        // The manual refutes the fix outright. Chapter 7's own worked example is "if you selected
        // superscripting (PV = 2), use subscripting (PV = 6)" to come back, and T2 followed by T6
        // is TWO text commands - so the value has to survive from one to the next or the example
        // cannot work. RegisPvSpacingTests pins both that and the plain persistence case, and both
        // went red.
        //
        // So the accumulation is right and something else in that stream returns the value to zero
        // on real hardware - most likely the T(E) that closes each label's temporary text control,
        // though nothing held here says so. NOT GUESSED AT: the divergence is written up in
        // docs\manual-tests\M6-SIXEL-AND-REGIS.md and left for a manual that answers it.

        int enteredRegister = _currentRegister;
        int enteredPlaneMask = _planeMask;
        var enteredWritingMode = _writingMode;

        while (i < commands.Length)
        {
            char c = commands[i];

            if (c == ' ' || c == '\r' || c == '\n' || c == '\t' || c == ',')
            {
                i++;
                continue;
            }

            // A NUMBER standing between the T and its string is PV SPACING, chapter 7's
            // "PV Spacing - Subscripts, Superscripts, and Overstrikes". The format is literally
            // T<PV value>, and hackerb9's registest.sh uses it: T22' 0,479 ' raises that label by
            // two half-cells.
            //
            // AN EARLIER NOTE HERE SAID THE OPPOSITE - that "nothing in chapter 7 gives a bare
            // number a meaning after T" - and skipped the digits. The manual disproves it outright,
            // so the digits are now acted on. See ApplyPvSpacing for what each value does.
            if (c >= '0' && c <= '9')
            {
                ApplyPvSpacing(c - '0');
                i++;
                continue;
            }

            if (c == '(')
            {
                i = ReadTextOptions(commands, i + 1, surface);
                continue;
            }

            if (c == '\'' || c == '"')
            {
                i = DrawTextString(commands, i, surface, homeColumn);
                continue;
            }

            // Anything else belongs to the next command.
            break;
        }

        // Write controls inside a T are temporary, exactly as they are inside an F or a V.
        _currentRegister = enteredRegister;
        _planeMask = enteredPlaneMask;
        _writingMode = enteredWritingMode;

        return i;
    }

    /// <summary>
    /// Reads one parenthesised group of text options.
    /// </summary>
    /// <param name="commands">
    /// The whole command string.
    /// </param>
    /// <param name="i">
    /// Index just past the opening parenthesis.
    /// </param>
    /// <param name="surface">
    /// The plane being drawn on. Text options can change the cell size, which the surface has to be
    /// told about before the next glyph lands.
    /// </param>
    /// <returns>
    /// Index just past the closing parenthesis.
    /// </returns>
    private int ReadTextOptions(ReadOnlySpan<char> commands, int i, IGraphicsSurface surface)
    {
        while (i < commands.Length && commands[i] != ')')
        {
            char option = commands[i];

            switch (option)
            {
                case 'A':
                case 'a':
                {
                    // T(A0..3) selects a set to DRAW from. L(A1..3) selects one to LOAD into, and
                    // that is handled in the load command - the letter is shared, the meaning is
                    // not.
                    i = ReadNumber(commands, i + 1, out int set, out bool any);
                    if (any) Text.ActiveSet = set;
                    continue;
                }

                case 'S':
                case 's':
                {
                    if (i + 1 < commands.Length && commands[i + 1] == '[')
                    {
                        i = ReadPair(commands, i + 2, out int width, out int height,
                            out bool anyWidth, out bool anyHeight);
                        Text.SetDisplayCell(anyWidth ? width : 0, anyHeight ? height : 0);
                        continue;
                    }

                    i = ReadNumber(commands, i + 1, out int size, out bool any);

                    // Table 10-1 code 9, text standard size error: "A text command selected a
                    // standard character size number of less than 0 or greater than 16."
                    if (any && (size < 0 || size > 16)) RecordError(9, (char)0);

                    if (any) Text.SelectStandardSize(size);
                    continue;
                }

                case 'U':
                case 'u':
                {
                    if (i + 1 < commands.Length && commands[i + 1] == '[')
                    {
                        i = ReadPair(commands, i + 2, out int width, out int height,
                            out bool anyWidth, out bool anyHeight);
                        Text.SetUnitCell(anyWidth ? width : 0, anyHeight ? height : 0);
                        continue;
                    }

                    i++;
                    continue;
                }

                case 'H':
                case 'h':
                {
                    i = ReadNumber(commands, i + 1, out int multiplier, out bool any);
                    if (any && multiplier > 0) Text.HeightMultiplier = multiplier;
                    continue;
                }

                case 'M':
                case 'm':
                {
                    i = ReadNumber(commands, i + 1, out int multiplier, out bool any);
                    if (any && multiplier > 0) Text.SizeMultiplier = multiplier;
                    continue;
                }

                case 'D':
                case 'd':
                {
                    i = ReadSignedNumber(commands, i + 1, out int angle, out bool any);
                    if (any) Text.StringTilt = angle;
                    continue;
                }

                case 'I':
                case 'i':
                {
                    i = ReadSignedNumber(commands, i + 1, out int angle, out bool any);
                    if (any) Text.Italics = angle;
                    continue;
                }

                case 'B':
                case 'b':
                {
                    // The temporary text control's start. Everything up to its end option applies
                    // to this command only.
                    Text.BeginTemporary();
                    i++;
                    continue;
                }

                case 'E':
                case 'e':
                {
                    Text.EndTemporary();
                    i++;
                    continue;
                }

                case 'W':
                case 'w':
                {
                    // A temporary WRITE control, which carries its own parenthesised group -
                    // registest.sh sends T(B D180 S1 W(I1V)). It is READ, not skipped, so the text
                    // that follows is drawn in the colour and the writing style the host asked for.
                    // Whatever it changes is put back when the text command ends; see the save and
                    // restore around this loop.
                    //
                    // The state has to reach the SURFACE as well. Decode pushes it once per command
                    // before dispatching, which happened before this group was read, so a write
                    // control here would otherwise be set in the decoder and never seen by a pixel.
                    i++;
                    if (i < commands.Length && commands[i] == '(')
                    {
                        i = HandleWrite(commands, i);
                        ApplyWriteState(surface);
                    }
                    continue;
                }

                default:
                {
                    if (char.IsLetter(option))
                    {
                        // PV spacing, and anything a later manual adds.
                        Count(char.ToUpperInvariant(option));
                    }

                    i++;
                    continue;
                }
            }
        }

        return i < commands.Length ? i + 1 : i;
    }

    /// <summary>
    /// Draws one quoted string, and any strings joined to it by commas.
    /// </summary>
    /// <param name="commands">
    /// The whole command string.
    /// </param>
    /// <param name="i">
    /// Index of the opening quote.
    /// </param>
    /// <param name="surface">
    /// Where the glyphs go.
    /// </param>
    /// <param name="homeColumn">
    /// The x a carriage return inside the string returns to.
    /// </param>
    /// <returns>
    /// Index just past the closing quote.
    /// </returns>
    private int DrawTextString(ReadOnlySpan<char> commands, int i, IGraphicsSurface surface,
        int homeColumn)
    {
        char quote = commands[i];
        i++;

        while (i < commands.Length)
        {
            char c = commands[i];

            if (c == quote)
            {
                // Two quote marks in a row are one quote character inside the string: "you can use
                // two quote marks in a row, so ReGIS recognizes one as a text string item, and not
                // the end of the text string".
                if (i + 1 < commands.Length && commands[i + 1] == quote)
                {
                    DrawGlyph(quote, surface);
                    i += 2;
                    continue;
                }

                return i + 1;
            }

            switch (c)
            {
                case '\r':
                    _x = homeColumn;
                    break;

                case '\n':
                    _y += Math.Max(1, Text.DisplayCellHeight);
                    break;

                case '\b':
                    _x -= Text.CharacterPositioning;
                    break;

                case '\t':
                    _x += Text.CharacterPositioning;
                    break;

                default:
                    DrawGlyph(c, surface);
                    break;
            }

            i++;
        }

        return i;
    }

    /// <summary>
    /// Draws one character at the drawing point and advances it.
    /// </summary>
    /// <param name="character">
    /// The character to draw.
    /// </param>
    /// <param name="surface">
    /// Where the pixels go.
    /// </param>
    /// <remarks>
    /// <para><b>The model, in the manual's own order</b></para>
    /// "Selects the character from a stored character set. Scales the character according to
    /// multiplication and size values. Orients the character with the tilt values. Draws the
    /// character into the bitmap."
    ///
    /// The stored cell is 8 by 10 and the drawn cell is the unit cell, so each stored pixel becomes
    /// a rectangle of unit-cell-width over eight by unit-cell-height over ten. Drawing per TARGET
    /// pixel rather than per source pixel is what keeps a large character solid: stepping the
    /// source and scaling up would leave gaps between the rectangles at any size that is not a
    /// whole multiple.
    ///
    /// "The starting cursor position is always the pixel value at the upper-left point of the
    /// stored character form. All pivoting occurs at that point."
    /// </remarks>
    private void DrawGlyph(char character, IGraphicsSurface surface)
    {
        int width = Math.Max(1, Text.UnitCellWidth * Text.SizeMultiplier);
        int height = Math.Max(1, Text.UnitCellHeight * Text.SizeMultiplier * Text.HeightMultiplier);

        var colour = _colours.Register(_currentRegister);

        // The tilt compass has eight points, 45 degrees apart. Rounding to it here means one
        // rotation for the whole glyph rather than a trigonometric transform per pixel.
        int tilt = ((Text.StringTilt % 360) + 360) % 360;
        int step = (tilt + 22) / 45 % 8;

        // How far one target pixel moves for a step right in the glyph, and for a step down. At
        // zero tilt this is the identity; each 45 degree step rotates it.
        int rightX = 1, rightY = 0, downX = 0, downY = 1;
        for (int turn = 0; turn < step; turn++)
        {
            // A 45 degree turn on an integer grid is done as two 45s making a 90, which the eight
            // points below select between - so odd steps carry a diagonal component.
            int nextRightX = rightX - rightY;
            int nextRightY = rightX + rightY;
            int nextDownX = downX - downY;
            int nextDownY = downX + downY;

            rightX = Math.Sign(nextRightX);
            rightY = Math.Sign(nextRightY);
            downX = Math.Sign(nextDownX);
            downY = Math.Sign(nextDownY);
        }

        // Italics slant the top of the cell sideways. The manual gives an angle; the shear is one
        // column per this many rows, which is the readable form of the same thing.
        int slant = Text.Italics;

        // PV spacing, turned from half-cells into pixels HERE rather than when it was set, so that
        // it rotates with the baseline exactly as chapter 7 says it must. Half of the DISPLAY cell,
        // which is the size the manual names - not the unit cell the glyph is drawn at.
        int halfWidth = Text.DisplayCellWidth / 2;
        int halfHeight = Text.DisplayCellHeight / 2;
        int pvX = (_textPvRight * halfWidth * rightX) + (_textPvDown * halfHeight * downX);
        int pvY = (_textPvRight * halfWidth * rightY) + (_textPvDown * halfHeight * downY);

        for (int targetY = 0; targetY < height; targetY++)
        {
            // The HEIGHT goes in, not a row index into a ten-row cell. Quantising through ten
            // rows first drops whichever rows the quantisation lands between, and for the built-in
            // glyphs those are the rows that tell an e from a c - see RegisTextState.RowFor.
            byte bits = Text.RowFor(character, targetY, height);
            if (bits == 0) continue;

            int shear = slant == 0 ? 0 : (height - targetY) * slant / 90;

            for (int targetX = 0; targetX < width; targetX++)
            {
                int sourceColumn = targetX * RegisTextState.StoredCellWidth / width;

                // Most significant bit is the leftmost pixel, the same order the ROM and the load
                // command's hex pairs both use.
                if ((bits & (1 << (RegisTextState.StoredCellWidth - 1 - sourceColumn))) == 0)
                {
                    continue;
                }

                int offsetX = targetX + shear;
                int x = _x + (offsetX * rightX) + (targetY * downX) + pvX;
                int y = _y + (offsetX * rightY) + (targetY * downY) + pvY;

                surface.SetPixel(x, y, colour);
            }
        }

        int advance = Math.Max(1, Text.CharacterPositioning * Text.SizeMultiplier);
        _x += advance * rightX;
        _y += advance * rightY;
    }

    /// <summary>
    /// <c>L</c> - loads a character set, from chapter 8.
    /// </summary>
    /// <param name="commands">
    /// The whole command string.
    /// </param>
    /// <param name="i">
    /// Index just past the <c>L</c>.
    /// </param>
    /// <returns>
    /// Index just past the command.
    /// </returns>
    /// <remarks>
    /// Three options, and all three are implemented:
    ///  - <c>L(A1..3)</c> selects which set the following cells go into. Set 0 is built in and
    ///    cannot be loaded, which the state object enforces.
    ///  - <c>L(A'name')</c> names that set, for the report command to answer with.
    ///  - <c>L"c"hex pairs</c> loads one cell: a call letter, then two hex digits per row for up to
    ///    ten rows, top row first, the first digit being the left four pixels.
    ///
    /// "If you use more than two hex values, ReGIS proceeds as if you used a comma after each pair
    /// of values. If you use only one hex value or end up with one, ReGIS assumes the first hex
    /// value is 0" - so an odd digit is the RIGHT half of its row.
    /// </remarks>
    private int HandleLoad(ReadOnlySpan<char> commands, int i)
    {
        while (i < commands.Length)
        {
            char c = commands[i];

            if (c == ' ' || c == '\r' || c == '\n' || c == '\t' || c == ',')
            {
                i++;
                continue;
            }

            if (c == '(')
            {
                i = ReadLoadOptions(commands, i + 1);
                continue;
            }

            if (c == '\'' || c == '"')
            {
                i = ReadLoadedCell(commands, i);
                continue;
            }

            break;
        }

        return i;
    }

    /// <summary>
    /// Reads <c>L(A...)</c>: the set number, a name, or both.
    /// </summary>
    /// <param name="commands">
    /// The whole command string.
    /// </param>
    /// <param name="i">
    /// Index just past the opening parenthesis.
    /// </param>
    /// <returns>
    /// Index just past the closing parenthesis.
    /// </returns>
    private int ReadLoadOptions(ReadOnlySpan<char> commands, int i)
    {
        while (i < commands.Length && commands[i] != ')')
        {
            char option = commands[i];

            if (option == 'A' || option == 'a')
            {
                i++;

                // The number and the name may both follow one A, and the manual is explicit about
                // the order: "Make sure you use the select character set option first".
                i = ReadNumber(commands, i, out int set, out bool any);

                // Table 10-1 code 4, alphabet out of range: "The syntax L(A<0 to 3>) contained
                // a number less than 0 or greater than 3." Recorded rather than acted on - which
                // sets this decoder actually stores is a separate question, and narrowing that
                // here would change what a stream draws rather than what it reports.
                if (any && (set < 0 || set > 3)) RecordError(4, (char)0);

                if (any && set >= 1 && set <= RegisTextState.LoadableSetCount)
                {
                    Text.LoadingSet = set;
                }

                if (i < commands.Length && (commands[i] == '\'' || commands[i] == '"'))
                {
                    i = ReadQuoted(commands, i, out string name);
                    Text.NameLoadingSet(name);
                }

                continue;
            }

            if (char.IsLetter(option))
            {
                Count(char.ToUpperInvariant(option));
            }

            i++;
        }

        return i < commands.Length ? i + 1 : i;
    }

    /// <summary>
    /// Reads <c>L"c"</c> followed by its hex pairs, and stores the cell.
    /// </summary>
    /// <param name="commands">
    /// The whole command string.
    /// </param>
    /// <param name="i">
    /// Index of the opening quote.
    /// </param>
    /// <returns>
    /// Index just past the last hex digit.
    /// </returns>
    private int ReadLoadedCell(ReadOnlySpan<char> commands, int i)
    {
        i = ReadQuoted(commands, i, out string call);
        if (call.Length == 0) return i;

        Span<byte> rows = stackalloc byte[RegisTextState.StoredCellHeight];
        int rowCount = 0;

        int value = 0;
        int digits = 0;

        while (i < commands.Length)
        {
            char c = commands[i];

            if (c == ',' || c == ' ' || c == '\r' || c == '\n' || c == '\t')
            {
                // A comma ends a row early: "ReGIS assumes the first hex value is 0", so one digit
                // becomes the right-hand four pixels.
                if (digits > 0 && rowCount < rows.Length)
                {
                    rows[rowCount++] = (byte)value;
                    value = 0;
                    digits = 0;
                }

                i++;
                continue;
            }

            int nibble = HexValue(c);
            if (nibble < 0) break;

            value = (value << 4) | nibble;
            digits++;

            if (digits == 2)
            {
                if (rowCount < rows.Length) rows[rowCount++] = (byte)value;
                value = 0;
                digits = 0;
            }

            i++;
        }

        if (digits > 0 && rowCount < rows.Length)
        {
            rows[rowCount++] = (byte)value;
        }

        Text.LoadCell(call[0], rows, rowCount);
        return i;
    }

    /// <summary>
    /// The value of one hexadecimal digit, or -1.
    /// </summary>
    /// <param name="c">
    /// The character.
    /// </param>
    /// <returns>
    /// 0 to 15, or -1 when it is not a hex digit.
    /// </returns>
    private static int HexValue(char c)
    {
        if (c >= '0' && c <= '9') return c - '0';
        if (c >= 'a' && c <= 'f') return c - 'a' + 10;
        if (c >= 'A' && c <= 'F') return c - 'A' + 10;
        return -1;
    }

    /// <summary>
    /// Reads a quoted string, honouring the doubled-quote rule.
    /// </summary>
    /// <param name="commands">
    /// The whole command string.
    /// </param>
    /// <param name="i">
    /// Index of the opening quote.
    /// </param>
    /// <param name="text">
    /// Receives the text between the quotes.
    /// </param>
    /// <returns>
    /// Index just past the closing quote.
    /// </returns>
    private static int ReadQuoted(ReadOnlySpan<char> commands, int i, out string text)
    {
        text = "";
        if (i >= commands.Length) return i;

        char quote = commands[i];
        i++;

        var builder = new System.Text.StringBuilder();

        while (i < commands.Length)
        {
            char c = commands[i];

            if (c == quote)
            {
                if (i + 1 < commands.Length && commands[i + 1] == quote)
                {
                    builder.Append(quote);
                    i += 2;
                    continue;
                }

                i++;
                break;
            }

            builder.Append(c);
            i++;
        }

        text = builder.ToString();
        return i;
    }

    /// <summary>
    /// Reads an unsigned number.
    /// </summary>
    /// <param name="commands">
    /// The whole command string.
    /// </param>
    /// <param name="i">
    /// Where to start.
    /// </param>
    /// <param name="value">
    /// Receives the number.
    /// </param>
    /// <param name="any">
    /// True when at least one digit was there.
    /// </param>
    /// <returns>
    /// Index just past the number.
    /// </returns>
    private static int ReadNumber(ReadOnlySpan<char> commands, int i, out int value, out bool any)
    {
        value = 0;
        any = false;

        while (i < commands.Length && commands[i] == ' ') i++;

        while (i < commands.Length && commands[i] >= '0' && commands[i] <= '9')
        {
            if (value < 1_000_000) value = (value * 10) + (commands[i] - '0');
            any = true;
            i++;
        }

        return i;
    }

    /// <summary>
    /// Reads a number that may carry a sign, which the tilt options accept.
    /// </summary>
    /// <param name="commands">
    /// The whole command string.
    /// </param>
    /// <param name="i">
    /// Where to start.
    /// </param>
    /// <param name="value">
    /// Receives the number.
    /// </param>
    /// <param name="any">
    /// True when at least one digit was there.
    /// </param>
    /// <returns>
    /// Index just past the number.
    /// </returns>
    private static int ReadSignedNumber(ReadOnlySpan<char> commands, int i, out int value, out bool any)
    {
        while (i < commands.Length && commands[i] == ' ') i++;

        bool negative = false;
        if (i < commands.Length && (commands[i] == '-' || commands[i] == '+'))
        {
            negative = commands[i] == '-';
            i++;
        }

        i = ReadNumber(commands, i, out value, out any);
        if (negative) value = -value;
        return i;
    }

    /// <summary>
    /// Reads a bracketed <c>[x,y]</c> pair.
    /// </summary>
    /// <param name="commands">
    /// The whole command string.
    /// </param>
    /// <param name="i">
    /// Index just past the opening bracket.
    /// </param>
    /// <param name="x">
    /// Receives the first number.
    /// </param>
    /// <param name="y">
    /// Receives the second.
    /// </param>
    /// <param name="anyX">
    /// True when the first number was given.
    /// </param>
    /// <param name="anyY">
    /// True when the second was given.
    /// </param>
    /// <returns>
    /// Index just past the closing bracket.
    /// </returns>
    private static int ReadPair(ReadOnlySpan<char> commands, int i, out int x, out int y,
        out bool anyX, out bool anyY)
    {
        i = ReadSignedNumber(commands, i, out x, out anyX);

        y = 0;
        anyY = false;

        if (i < commands.Length && commands[i] == ',')
        {
            i = ReadSignedNumber(commands, i + 1, out y, out anyY);
        }

        while (i < commands.Length && commands[i] != ']') i++;
        return i < commands.Length ? i + 1 : i;
    }

    /// <summary>
    /// <c>S(H...)</c> - the hard copy control's coordinates, read for their SYNTAX only.
    /// </summary>
    /// <remarks>
    /// <para><b>Chapter 2, Hard Copy Control</b></para>
    /// "This option lets you print a hard copy of the screen image." It takes no position, one, or
    /// two: "S(H)", "S(H[X,Y])", "S(H[X1,Y1][X2,Y2])". With one, "the terminal uses that position
    /// and the current cursor position to define the opposite corners"; with two they are the two
    /// corners themselves.
    /// <para><b>Nothing is printed, and the coordinates are not applied</b></para>
    /// There is no printer path here, so the caller counts the command as unhandled. The pairs are
    /// walked rather than used - and deliberately NOT put through ReadCoordinate, because naming a
    /// print area must not move the drawing point.
    /// What this does do is count them, because Table 10-1 code 2 is exactly this syntax: "The
    /// syntax S(H[X,Y][X,Y]) contained more than two coordinate pairs. The extra pairs were
    /// ignored."
    /// </remarks>
    /// <param name="options">
    /// The text inside the screen command's parentheses.
    /// </param>
    /// <param name="i">
    /// Just past the H.
    /// </param>
    /// <returns>
    /// The index after the last coordinate group.
    /// </returns>
    private int ReadHardCopy(ReadOnlySpan<char> options, int i)
    {
        int pairs = 0;

        while (true)
        {
            while (i < options.Length && IsRegisSpace(options[i])) i++;
            if (i >= options.Length || options[i] != '[') break;

            int close = FindClose(options, i, '[', ']');

            // Every pair still has its own values checked, so S(H[1,2,3]) reports code 3 the same
            // way [1,2,3] does anywhere else.
            CheckCoordinateValues(options.Slice(i + 1, close - i - 1));

            pairs++;
            i = close < options.Length ? close + 1 : options.Length;
        }

        // "The extra pairs were ignored" - so a third pair is an error and is dropped, not obeyed.
        if (pairs > 2) RecordError(2, '\0');

        return i;
    }

    /// <summary>
    /// <c>S(E)</c> erases the screen. Other S options are counted.
    /// </summary>
    private int HandleScreen(ReadOnlySpan<char> commands, int i, IGraphicsSurface surface)
    {
        bool erased = false;

        while (i < commands.Length && commands[i] == '(')
        {
            int close = FindClose(commands, i, '(', ')');
            var options = commands.Slice(i + 1, close - i - 1);

            bool handled = false;

            for (int o = 0; o < options.Length; o++)
            {
                char option = options[o];

                if (option == 'E' || option == 'e')
                {
                    surface.Clear(GraphicsColor.Transparent);

                    // THE CURSOR DOES NOT MOVE. Chapter 4 lists what a screen erase does, and the
                    // second bullet is "Does not change the cursor position". This used to home the
                    // drawing point to 0,0, which is a different terminal's behaviour.
                    //
                    // It never showed up because every fixture we have opens with S(E) while the
                    // point is already at 0,0 - the two only disagree when a stream erases after
                    // drawing, and then the next relative move starts from the wrong place.
                    //
                    // The same list gives the rest: it "Clears all position stacks", "Turns off any
                    // shading specified by the write control command", and "Does not change the
                    // current background color or shade".
                    ClearPositionStacks();
                    _shadeReference = GraphicsShading.None;

                    erased = true;
                    handled = true;
                    continue;
                }

                if (option == 'H' || option == 'h')
                {
                    o = ReadHardCopy(options, o + 1) - 1;

                    // NOT marked handled: there is no printer here, so the hard copy itself does not
                    // happen and the command is counted as something a real host asked for and did
                    // not get. Its SYNTAX is still checked - see ReadHardCopy - because a host that
                    // asks R(E) afterwards deserves the right answer either way.
                    continue;
                }

                if (option == 'M' || option == 'm')
                {
                    o = ReadOutputMapping(options, o + 1) - 1;
                    handled = true;
                    continue;
                }

                if (option == 'C' || option == 'c')
                {
                    o = ReadCursorControl(options, o + 1) - 1;
                    handled = true;
                    continue;
                }

                // Background intensity. "Both options have the same basic format, but start with
                // different command key letters (W for write command, S for screen command)" - so
                // S(I3) is to the background what W(I3) is to the foreground. It decides what the
                // screen erases to, and what replace writing puts in the gaps of a pattern.
                if (option == 'I' || option == 'i')
                {
                    if (ReadOptionNumber(options, o + 1, out int background)
                        && GraphicsColorMap.IsRegister(background))
                    {
                        _backgroundRegister = background;
                        handled = true;
                    }

                    continue;
                }
            }

            if (handled) erased = true;
            i = close + 1;
        }

        if (!erased) Count('S');
        return i;
    }

    /// <summary>
    /// <c>S(C...)</c> - graphics cursor control.
    /// </summary>
    /// <param name="options">
    /// The characters inside the screen command's parentheses.
    /// </param>
    /// <param name="at">
    /// Index just past the C.
    /// </param>
    /// <returns>
    /// The index just past what this consumed.
    /// </returns>
    /// <remarks>
    /// <para><b>Three forms, chapter 2</b></para>
    ///  - <c>S(C0)</c> and <c>S(C1)</c> turn the OUTPUT cursor off and on.
    ///  - <c>S(C(H n))</c> picks the output cursor's style.
    ///  - <c>S(C(I n))</c> picks the INPUT cursor's style, which is the one this terminal draws.
    ///
    /// <para><b>What is read and what is only accepted</b></para>
    /// The input styles are acted on. The output cursor is a different thing entirely - it is the
    /// mark a real VT340 shows while it sits waiting for the host's next ReGIS command, and this
    /// terminal draws no such mark - so its on/off flag and its style are read and remembered without
    /// changing anything. Remembered rather than dropped, because a host that sets it and then asks
    /// deserves the truth, and because silently skipping the digits would leave the parser standing
    /// in the middle of them.
    ///
    /// <para><b>The user-defined cursor is not read</b></para>
    /// "You can define your own input cursor by using a character mask" - <c>S(C(I[+5,+10]"XO"))</c>,
    /// two characters from the loaded set combined into a 16 by 24 shape. Not built: it needs the
    /// loaded soft font to become a cursor bitmap, and nothing in any corpus asks for it. The
    /// coordinate and the quoted mask are stepped over so the rest of the command still arrives.
    /// </remarks>
    private int ReadCursorControl(ReadOnlySpan<char> options, int at)
    {
        while (at < options.Length && options[at] == ' ') at++;

        // S(C0) / S(C1) - the output cursor's on/off flag, with no nested group.
        if (at < options.Length && char.IsDigit(options[at]))
        {
            _outputCursorVisible = options[at] != '0';
            at++;
            return at;
        }

        if (at >= options.Length || options[at] != '(') return at;

        int close = FindClose(options, at, '(', ')');
        var inner = options.Slice(at + 1, close - at - 1);

        int k = 0;
        while (k < inner.Length && inner[k] == ' ') k++;
        if (k >= inner.Length) return close + 1;

        char which = char.ToUpperInvariant(inner[k]);
        k++;
        while (k < inner.Length && inner[k] == ' ') k++;

        // A bracketed coordinate or a quoted pair means the user-defined form. Left alone on purpose
        // - see the remarks - but recognised, so a number that happens to sit after it is not read
        // as a style.
        if (k < inner.Length && (inner[k] == '[' || inner[k] == '"')) return close + 1;

        if (k >= inner.Length || !char.IsDigit(inner[k]))
        {
            // "Omitted - Crosshair" for the input cursor. The output cursor's omitted case is the
            // diamond, and nothing here draws it.
            if (which == 'I') _inputCursorStyle = RegisCursorStyle.Crosshair;
            return close + 1;
        }

        int style = inner[k] - '0';

        if (which == 'I') _inputCursorStyle = InputCursorStyleFor(style);
        else if (which == 'H') _outputCursorStyleNumber = style;

        return close + 1;
    }

    /// <summary>
    /// Turns chapter 2's input cursor style number into a shape.
    /// </summary>
    /// <param name="number">
    /// The number the host sent.
    /// </param>
    /// <returns>
    /// The shape to draw.
    /// </returns>
    /// <remarks>
    /// The table as printed: 0 "Crosshair (default)", 1 Diamond, 2 Crosshair, 3 Rubber band line,
    /// 4 Rubber band rectangle. Anything else falls back to the crosshair, which is what the same
    /// table calls the default and what an omitted number gives.
    /// </remarks>
    private static RegisCursorStyle InputCursorStyleFor(int number)
    {
        switch (number)
        {
            case 1: return RegisCursorStyle.Diamond;
            case 3: return RegisCursorStyle.RubberBandLine;
            case 4: return RegisCursorStyle.RubberBandRectangle;
            default: return RegisCursorStyle.Crosshair;
        }
    }

    /// <summary>
    /// <c>S(M</c> - output mapping. Loads the terminal's colour map.
    /// </summary>
    /// <remarks>
    /// <para><b>The form</b></para>
    /// A run of "location then a bracketed value", as many as the host cares to send. The manual's
    /// own example is <c>S(M1(AH60L80S60)3(AH150L50S60))</c> - location 1 to plum, location 3 to
    /// gold.
    ///
    /// Inside the brackets:
    ///  - <c>A</c> means "change only the colour value, not the monochrome one". On a VT340 there is
    ///    one set of values for both, so the manual says it has no effect, and it is skipped here.
    ///  - <c>H</c>, <c>L</c>, <c>S</c> each followed by a number give hue in degrees and lightness
    ///    and saturation in percent.
    ///  - <c>R</c>, <c>G</c>, <c>B</c> each followed by a number give the channels in percent.
    ///  - a letter with NO number after it names one of eight basic colours. That is the whole
    ///    disambiguation: <c>R50</c> is 50 percent red, a bare <c>R</c> is the colour red.
    ///
    /// <para><b>Why this matters beyond ReGIS</b></para>
    /// The map it writes is the same map Sixel paints from, so a host may set its palette here and
    /// then send an image that defines no colours at all. hackerb9's <c>cat-original.six</c> does
    /// exactly that, and drew in the power-on colours until this existed.
    /// </remarks>
    /// <param name="options">
    /// The characters inside the S command's brackets.
    /// </param>
    /// <param name="start">
    /// First character after the M.
    /// </param>
    /// <returns>
    /// The index just past the mapping run.
    /// </returns>
    private int ReadOutputMapping(ReadOnlySpan<char> options, int start)
    {
        int i = start;

        while (i < options.Length)
        {
            while (i < options.Length && (options[i] == ' ' || options[i] == ',')) i++;

            // A location number, then its bracketed value. Anything else ends the run.
            if (i >= options.Length || options[i] < '0' || options[i] > '9') break;

            int location = 0;
            while (i < options.Length && options[i] >= '0' && options[i] <= '9')
            {
                location = location * 10 + (options[i] - '0');
                i++;
            }

            while (i < options.Length && options[i] == ' ') i++;
            if (i >= options.Length || options[i] != '(') break;

            int close = FindClose(options, i, '(', ')');
            ApplyColourValue(options.Slice(i + 1, close - i - 1), location);
            i = close + 1;
        }

        return i;
    }

    /// <summary>
    /// Reads one bracketed colour value and stores it in the map.
    /// </summary>
    /// <param name="value">
    /// The characters between the brackets.
    /// </param>
    /// <param name="location">
    /// The output map location the value belongs to.
    /// </param>
    private void ApplyColourValue(ReadOnlySpan<char> value, int location)
    {
        // The screen command is deep in the parse and has no surface to hand, so the fact that the
        // output map moved is carried back out to the command loop, which owns the repaint.
        _colourMapChanged = true;

        // LOCATION 0 IS THE PAGE BACKGROUND, and moving it is not the same event as moving any
        // other location. Every pixel nothing has drawn on holds code 0, so changing what code 0
        // looks like changes the whole page - which on the hardware costs nothing, because the map
        // is consulted as the screen is scanned rather than written into pixels.
        //
        // Kept apart from _colourMapChanged on purpose: that one fires for ANY location, and using
        // it here would force the background to whatever location 0 happens to be every time a
        // drawing set M1 through M3 - which for a stream that never touches location 0 means
        // overriding the theme with a default black.
        if (location == BackgroundMapLocation) BackgroundMapLocationChanged = true;

        int hue = 0, lightness = 0, saturation = 0;
        int red = 0, green = 0, blue = 0;
        bool anyHls = false, anyRgb = false;

        for (int i = 0; i < value.Length; i++)
        {
            char c = value[i];
            if (c == ' ' || c == ',') continue;

            char letter = char.ToUpperInvariant(c);

            // 'A' selects the colour value rather than the monochrome one. The manual: "On the
            // VT340, there is one set of output map values for both color and monochrome modes.
            // Therefore, the A suboption has no effect."
            if (letter == 'A') continue;

            int digits = i + 1;
            while (digits < value.Length && value[digits] == ' ') digits++;

            bool hasNumber = digits < value.Length && value[digits] >= '0' && value[digits] <= '9';

            if (!hasNumber)
            {
                // A bare letter names a basic colour, so it IS the whole value.
                if (TryBasicColour(letter, out GraphicsColor basic))
                {
                    _colours.Set(location, basic);
                    return;
                }

                continue;
            }

            int number = 0;
            while (digits < value.Length && value[digits] >= '0' && value[digits] <= '9')
            {
                number = number * 10 + (value[digits] - '0');
                digits++;
            }

            switch (letter)
            {
                case 'H': hue = number; anyHls = true; break;
                case 'L': lightness = number; anyHls = true; break;
                case 'S': saturation = number; anyHls = true; break;
                case 'R': red = number; anyRgb = true; break;
                case 'G': green = number; anyRgb = true; break;
                case 'B': blue = number; anyRgb = true; break;
            }

            i = digits - 1;
        }

        // HLS wins if both appeared, which no real stream does - it is a mangled one, and the
        // manual describes HLS as the system with the larger selection.
        if (anyHls) _colours.SetFromHls(location, hue, lightness, saturation);
        else if (anyRgb) _colours.SetFromRgbPercent(location, red, green, blue);
    }

    /// <summary>
    /// The eight single-letter colour names, as the values DEC's own default map gives them.
    /// </summary>
    /// <remarks>
    /// The letters are Dark, Blue, Red, Green, Magenta, Cyan, Yellow and White. Seven of them are
    /// named rows in table 2-3 and take that row's value.
    ///
    /// ASSUMPTION, marked because it is one: the table has no row called White. The brightest entry
    /// it does have is location 15, "Gray 75%", and that is what W uses here. If a document turns up
    /// giving W its own value, this is the line to change.
    /// </remarks>
    /// <param name="letter">
    /// The upper-cased letter.
    /// </param>
    /// <param name="colour">
    /// Receives the colour when the letter names one.
    /// </param>
    /// <returns>
    /// True when the letter is one of the eight.
    /// </returns>
    private static bool TryBasicColour(char letter, out GraphicsColor colour)
    {
        int location;

        switch (letter)
        {
            case 'D': location = 0; break;   // dark
            case 'B': location = 1; break;
            case 'R': location = 2; break;
            case 'G': location = 3; break;
            case 'M': location = 4; break;
            case 'C': location = 5; break;
            case 'Y': location = 6; break;
            case 'W': location = 15; break;  // see the remarks - no row is named White
            default:
                colour = GraphicsColor.Transparent;
                return false;
        }

        colour = DefaultColours.Register(location);
        return true;
    }

    /// <summary>
    /// A never-modified map, so the basic colour letters keep meaning what DEC's table says even
    /// after a host has redefined the live map.
    /// </summary>
    private static readonly GraphicsColorMap DefaultColours = new GraphicsColorMap();

    /// <summary>
    /// <c>P[x,y]</c> moves the drawing point without drawing.
    /// </summary>
    private int HandlePosition(ReadOnlySpan<char> commands, int i)
    {
        // Chapter 3 lists where a temporary write control may appear: "you can use temporary
        // write controls in vector, curve, screen, and POSITION commands". A position draws
        // nothing itself, but it can shade - a shading reference line is named by a write
        // control, and shading fires from the point that was drawn.
        int enteredRegister = _currentRegister;
        int enteredPlaneMask = _planeMask;
        var enteredWritingMode = _writingMode;

        i = ReadOptionGroups(commands, i);

        while (true)
        {
            int next = NextArgument(commands, i);
            if (next < 0) break;
            i = next;

            // "You can control the direction of many ReGIS drawing or MOVEMENT commands by using
            // the pixel vector system." Position is a movement command, so a PV digit moves the
            // drawing point without drawing anything.
            if (commands[i] != '[')
            {
                StepPixelVector(commands[i] - '0');
                i++;
                i = ReadOptionGroups(commands, i);
                continue;
            }

            int close = FindClose(commands, i, '[', ']');
            ReadCoordinate(commands.Slice(i + 1, close - i - 1));
            i = close + 1;
            i = ReadOptionGroups(commands, i);
        }

        // Temporary, like every other command that carries one.
        _currentRegister = enteredRegister;
        _planeMask = enteredPlaneMask;
        _writingMode = enteredWritingMode;

        return i;
    }

    /// <summary>
    /// True when the character here is a pixel vector direction.
    /// </summary>
    /// <param name="commands">
    /// The command characters.
    /// </param>
    /// <param name="i">
    /// Where to look.
    /// </param>
    /// <returns>
    /// True for a digit 0 to 7.
    /// </returns>
    /// <remarks>
    /// Only 0 to 7 - there are eight directions, "each at a different 45-degree interval". An 8 or
    /// a 9 is not a PV number and must not be swallowed as one, or a malformed stream would drag
    /// the drawing point somewhere arbitrary.
    /// </remarks>
    private static bool IsPvDigit(ReadOnlySpan<char> commands, int i)
        => i < commands.Length && commands[i] >= '0' && commands[i] <= '7';

    /// <summary>
    /// Finds the next argument of a drawing command, stepping over separators.
    /// </summary>
    /// <param name="commands">
    /// The command characters.
    /// </param>
    /// <param name="i">
    /// Where to start looking.
    /// </param>
    /// <returns>
    /// The index of the next coordinate group or pixel vector digit, or -1 when the command's
    /// arguments have ended.
    /// </returns>
    /// <remarks>
    /// <para><b>Why this is not just a whitespace skip</b></para>
    /// The separator is only consumed when something this command can use follows it. Skipping
    /// blindly would swallow the space in front of the NEXT command, and the caller would return an
    /// index past characters it had no business eating.
    ///
    /// It matters in practice: hackerb9's faketextcolor.sh writes <c>F(V(W(F1)) 0642)</c>, with a
    /// space between the temporary write control and the pixel vector digits. Stopping at that
    /// space left the sixteen boxes undrawn and only the V0 separators between them on the screen -
    /// a thin line exactly sixteen steps long, which is what the plane dump showed.
    /// </remarks>
    private static int NextArgument(ReadOnlySpan<char> commands, int i)
    {
        int at = i;
        while (at < commands.Length
            && (commands[at] == ' ' || commands[at] == '\r' || commands[at] == '\n'
                || commands[at] == '\t' || commands[at] == ','))
        {
            at++;
        }

        if (at >= commands.Length) return -1;
        if (commands[at] != '[' && !IsPvDigit(commands, at)) return -1;

        return at;
    }

    /// <summary>
    /// Moves the drawing point one pixel vector step.
    /// </summary>
    /// <param name="direction">
    /// A PV number, 0 to 7.
    /// </param>
    private void StepPixelVector(int direction)
    {
        if ((uint)direction >= (uint)PvStepX.Length) return;

        _x += PvStepX[direction] * _pvMultiplier;
        _y += PvStepY[direction] * _pvMultiplier;
    }

    /// <summary>
    /// One entry of the position stack.
    /// </summary>
    private struct SavedPosition
    {
        /// <summary>
        /// The saved drawing point.
        /// </summary>
        public int X;

        /// <summary>
        /// See <see cref="X"/>.
        /// </summary>
        public int Y;

        /// <summary>
        /// True for the dummy an unbounded <c>(S)</c> pushes, which restores nothing when popped.
        /// </summary>
        public bool IsDummy;
    }

    /// <summary>
    /// "You can save up to 16 positions in a stack."
    /// </summary>
    /// <remarks>
    /// Chapter 4 is precise about the count and about who it covers: "The maximum number of unended,
    /// saved positions (including all save commands) is 16. However, for compatibility with other
    /// ReGIS products, use a maximum of eight." Sixteen is what this terminal does, so sixteen is
    /// what is built; the advice to use eight is for the host to follow, not for us to enforce.
    /// </remarks>
    private const int PositionStackLimit = 16;

    /// <summary>
    /// The position stack itself. A fixed array, never grown - the limit is part of the format.
    /// </summary>
    private readonly SavedPosition[] _positionStack = new SavedPosition[PositionStackLimit];

    private int _positionStackCount;

    /// <summary>
    /// <c>(B)</c> and <c>(S)</c> - push the drawing point, or a dummy, onto the position stack.
    /// </summary>
    /// <remarks>
    /// <para><b>Bounded and unbounded, chapter 4</b></para>
    /// "(B) saves the current active position. (Pushes the position onto the stack.)" and "(E)
    /// returns the active position to the coordinates saved by the last (B) option."
    /// The unbounded form differs only in what is pushed: "The (S) pushes a dummy, or nonexistent
    /// position onto the position stack. The (E) pops this nonexistent position off the stack,
    /// leaving the active position at the position specified before the (E) option." So the two
    /// share everything except whether the pop moves the drawing point.
    /// The manual gives the reason the unbounded form exists at all, which is worth keeping because
    /// it explains why a stack that restores nothing is not pointless: "The unbounded stack option
    /// is for symmetry with other command types (such as curve commands) that can use bounded and
    /// unbounded stacks."
    /// </remarks>
    /// <param name="dummy">
    /// True for <c>(S)</c>, false for <c>(B)</c>.
    /// </param>
    /// <param name="option">
    /// The letter to blame if the stack is full - Table 10-1 code 7 reports (B) or (S).
    /// </param>
    private void PushPosition(bool dummy, char option)
    {
        if (_positionStackCount >= PositionStackLimit)
        {
            // Table 10-1 code 7, begin/start overflow. The push is DROPPED rather than overwriting
            // the oldest entry: losing the seventeenth is recoverable, losing the first would put
            // the drawing point somewhere unrelated when the outermost (E) finally arrives.
            RecordError(7, option);
            return;
        }

        _positionStack[_positionStackCount].X = _x;
        _positionStack[_positionStackCount].Y = _y;
        _positionStack[_positionStackCount].IsDummy = dummy;
        _positionStackCount++;
    }

    /// <summary>
    /// <c>(E)</c> - pop the position stack, moving the drawing point back for a bounded stack.
    /// </summary>
    private void PopPosition()
    {
        if (_positionStackCount == 0)
        {
            // Table 10-1 code 8, begin/start underflow: an (E) with no matching (B).
            RecordError(8, 'E');
            return;
        }

        _positionStackCount--;

        if (!_positionStack[_positionStackCount].IsDummy)
        {
            _x = _positionStack[_positionStackCount].X;
            _y = _positionStack[_positionStackCount].Y;
        }
    }

    /// <summary>
    /// Throws away every saved position, which is one of the things a screen erase does.
    /// </summary>
    private void ClearPositionStacks()
    {
        _positionStackCount = 0;
    }

    /// <summary>
    /// <c>V[x,y]</c> draws a line from the current point to each coordinate in turn.
    /// </summary>
    private int HandleVector(ReadOnlySpan<char> commands, int i, IGraphicsSurface surface)
    {
        // A write control inside a vector's option group lasts for that vector only, so what it
        // changes is put back on the way out - unless this vector is collecting a fill, in which
        // case the fill has not happened yet and needs the state left where the vector put it.
        // HandleFill does the restoring in that case.
        int enteredPlaneMask = _planeMask;
        var enteredWritingMode = _writingMode;
        int enteredRegister = _currentRegister;

        i = ReadOptionGroups(commands, i);

        while (true)
        {
            int next = NextArgument(commands, i);
            if (next < 0) break;
            i = next;

            // A PV digit is a whole vector on its own - no brackets, no coordinate. This is what
            // hackerb9's faketextcolor.sh draws its boxes with: V(...)0642 goes east, south, west,
            // north at whatever the PV multiplier is.
            if (commands[i] != '[')
            {
                int fromPvX = _x;
                int fromPvY = _y;
                StepPixelVector(commands[i] - '0');

                if (_collectingFill)
                {
                    AddVertex(fromPvX, fromPvY);
                    AddVertex(_x, _y);
                }
                else
                {
                    surface.DrawLine(fromPvX, fromPvY, _x, _y, _colours.Register(_currentRegister),
                        EffectivePatternMask(), PatternBits, _patternMultiplier, ref _patternPhase);
                }

                i++;
                i = ReadOptionGroups(commands, i);
                continue;
            }

            int close = FindClose(commands, i, '[', ']');

            int fromX = _x;
            int fromY = _y;
            ReadCoordinate(commands.Slice(i + 1, close - i - 1));

            if (_collectingFill)
            {
                // "Each argument for the vector option creates one vertex." The starting point is
                // a vertex too, and AddVertex drops it if the figure already began there.
                AddVertex(fromX, fromY);
                AddVertex(_x, _y);
            }
            else
            {
                surface.DrawLine(fromX, fromY, _x, _y, _colours.Register(_currentRegister),
                    EffectivePatternMask(), PatternBits, _patternMultiplier, ref _patternPhase);
            }

            i = close + 1;

            // Options between coordinates are skipped rather than acted on - they select line
            // styles this decoder does not draw, and stopping here would lose the rest of the line.
            i = ReadOptionGroups(commands, i);
        }

        if (!_collectingFill)
        {
            _planeMask = enteredPlaneMask;
            _writingMode = enteredWritingMode;
            _currentRegister = enteredRegister;
        }

        return i;
    }

    /// <summary>
    /// <c>C[x,y]</c> draws a circle centred on the current point, passing through the given one.
    /// </summary>
    private int HandleCurve(ReadOnlySpan<char> commands, int i, IGraphicsSurface surface)
    {
        // C(A...) and C(B)/(E) are arcs and filled curves. Neither is implemented, and reading
        // their coordinates as a plain circle would draw something the host did not ask for.
        if (i < commands.Length && commands[i] == '(')
        {
            Count('C');
            return SkipArguments(commands, i);
        }

        while (i < commands.Length && commands[i] == '[')
        {
            int close = FindClose(commands, i, '[', ']');

            int centreX = _x;
            int centreY = _y;
            ReadCoordinate(commands.Slice(i + 1, close - i - 1));

            int dx = _x - centreX;
            int dy = _y - centreY;
            int radius = (int)Math.Round(Math.Sqrt((double)dx * dx + (double)dy * dy));

            if (_collectingFill)
            {
                AddCircle(centreX, centreY, radius);
            }
            else
            {
                surface.DrawCircle(centreX, centreY, radius, _colours.Register(_currentRegister));
            }

            // The drawing point goes back to the centre, which is where a real ReGIS leaves it.
            _x = centreX;
            _y = centreY;

            i = close + 1;
        }

        return i;
    }

    /// <summary>
    /// How many coordinates each PV digit moves.
    /// </summary>
    /// <remarks>
    /// Set by <c>W(M n)</c>. "Each PV number tells the terminal to move one coordinate in that
    /// direction", so the multiplier starts at 1 and a stream that never sets it is unaffected.
    /// </remarks>
    private int _pvMultiplier = 1;

    /// <summary>
    /// Horizontal step for each of the eight pixel vector directions.
    /// </summary>
    /// <remarks>
    /// <para><b>Read off figure 1-2, not guessed</b></para>
    /// The compass runs anticlockwise from east: 0 east, 1 north-east, 2 north, 3 north-west,
    /// 4 west, 5 south-west, 6 south, 7 south-east. The figure OCRs as noise, so the page was
    /// rendered and read.
    ///
    /// Figure 1-3 then gives five worked examples that pin it from the other side - "movement from
    /// center by three 6s" going DOWN settles which way 6 points, and those examples are tests.
    ///
    /// North is negative Y because surface coordinates run downwards.
    /// </remarks>
    private static readonly int[] PvStepX = { 1, 1, 0, -1, -1, -1, 0, 1 };

    /// <summary>
    /// Vertical step for each of the eight pixel vector directions.
    /// </summary>
    private static readonly int[] PvStepY = { 0, -1, -1, -1, 0, 1, 1, 1 };

    /// <summary>
    /// How much room a VT300 has for macrographs, in bytes.
    /// </summary>
    /// <remarks>
    /// "The VT300 can store at least 10,000 bytes of macrograph data." That is the only figure the
    /// manual gives, and it is what the storage report answers with. The word "at least" means a
    /// real terminal may hold more; a host uses this to decide whether its macrograph will fit, and
    /// under-promising is the safe direction to be wrong in.
    /// </remarks>
    public const int MacrographStorageBytes = 10000;

    /// <summary>
    /// Reports the terminal owes the host, not yet collected.
    /// </summary>
    private System.Text.StringBuilder? _reports;

    /// <summary>
    /// True when a report command has produced something for the host.
    /// </summary>
    public bool HasReports => _reports != null && _reports.Length > 0;

    /// <summary>
    /// Takes the pending reports and clears them.
    /// </summary>
    /// <returns>
    /// Everything the report command produced since this was last called, or an empty string.
    /// </returns>
    /// <remarks>
    /// <para><b>Collected rather than raised as an event</b></para>
    /// The decoder stays a decoder: it is handed characters and a surface, and it owns no
    /// connection. The emulator drives it and knows how to reach the host, so it asks afterwards.
    /// That also keeps the whole thing testable without wiring up a connection.
    ///
    /// "All information returned by the VT300 ends with a carriage return (CR)", so each report
    /// already carries its own terminator and several can simply follow one another.
    /// </remarks>
    public string TakeReports()
    {
        if (_reports == null || _reports.Length == 0) return string.Empty;

        string reports = _reports.ToString();
        _reports.Clear();
        return reports;
    }

    /// <summary>
    /// <c>R(...)</c> - the report command, which answers the host instead of drawing.
    /// </summary>
    /// <param name="commands">
    /// The command characters.
    /// </param>
    /// <param name="i">
    /// Index just past the R.
    /// </param>
    /// <returns>
    /// The index just past everything this command consumed.
    /// </returns>
    /// <remarks>
    /// <para><b>The five forms, from chapter 10</b></para>
    ///  - <c>R(P)</c> reports the cursor position "as an absolute, bracketed extent in screen
    ///    coordinates".
    ///  - <c>R(M(a))</c> reports one macrograph, wrapped in its indicator and terminator.
    ///  - <c>R(M(=))</c> reports macrograph storage as two integers in double quotes.
    ///  - <c>R(L)</c> reports the name of the set selected for load operations, as A'name'.
    ///  - <c>R(E)</c> reports the last parse error as two integers in double quotes.
    ///
    /// The sixth thing chapter 10 covers is graphics input mode, which needs a pointing device and
    /// is not a report. It is counted rather than answered.
    ///
    /// <para><b>A note the manual makes and this cannot enforce</b></para>
    /// "When your application requests information, make sure the system does not display the
    /// information on the screen. The data could affect your graphic images. There is no ReGIS
    /// control to prevent this action." So a host that reports while echo is on corrupts its own
    /// picture, and that is the host's problem on real hardware too.
    /// </remarks>
    private int HandleReport(ReadOnlySpan<char> commands, int i)
    {
        bool answered = false;

        while (i < commands.Length && commands[i] == '(')
        {
            int close = FindClose(commands, i, '(', ')');
            var options = commands.Slice(i + 1, close - i - 1);

            for (int o = 0; o < options.Length; o++)
            {
                char option = char.ToUpperInvariant(options[o]);

                if (option == 'P')
                {
                    // Two different commands share the letter. R(P) reports the drawing point right
                    // now; R(P(I)) is the report position INTERACTIVE option, which asks about the
                    // graphics input cursor instead and, in one-shot mode, does not answer until
                    // somebody presses a key. Telling them apart means looking for the nested group.
                    int after = ReadInteractiveRequest(options, o + 1, out bool interactive);
                    if (interactive)
                    {
                        o = after - 1;
                        answered = true;
                        continue;
                    }

                    Report("[" + _x + "," + _y + "]");
                    answered = true;
                    continue;
                }

                if (option == 'I')
                {
                    o = ReadInputMode(options, o + 1) - 1;
                    answered = true;
                    continue;
                }

                if (option == 'L')
                {
                    // "The terminal reports the name of the character set in the following format.
                    // A'<name>'"
                    Report("A'" + Text.NameOf(Text.LoadingSet) + "'");
                    answered = true;
                    continue;
                }

                if (option == 'E')
                {
                    // "The terminal reports the last error in the following format. <N>,<M>" in
                    // double quotes, where N is a code from Table 10-1 and M is the decimal ASCII
                    // code of the character blamed, or 0. Cleared by the resynchronization
                    // character - see where ';' is handled.
                    Report("\"" + _errorCode + "," + _errorCharacter + "\"");
                    answered = true;
                    continue;
                }

                if (option == 'M')
                {
                    o = ReadMacrographReport(options, o + 1) - 1;
                    answered = true;
                    continue;
                }
            }

            i = close + 1;
        }

        // Graphics input mode, and any form not listed above, leave the host waiting - so they are
        // counted, and a real host says which of them it actually uses.
        if (!answered) Count('R');
        return i;
    }

    /// <summary>
    /// Reads <c>R(I0)</c> and <c>R(I1)</c>, the graphics input mode option.
    /// </summary>
    /// <param name="options">
    /// The characters inside the report command's parentheses.
    /// </param>
    /// <param name="at">
    /// Index just past the I.
    /// </param>
    /// <returns>
    /// The index just past what this consumed.
    /// </returns>
    /// <remarks>
    /// <para><b>Chapter 10</b></para>
    /// "I identifies the input mode option. 0 identifies the input mode as one-shot", and 1 the
    /// multiple mode. Entering either "the input cursor appears on the screen".
    ///
    /// "NOTE: When the terminal receives R(I), it returns a carriage return (CR). Applications can
    /// use the CR for synchronization." That is a bare CR, which is what an empty report is here -
    /// every report already ends with one.
    ///
    /// <para><b>R(I0) both enters and leaves</b></para>
    /// From multiple mode, "the terminal stays in multiple mode until the application sends the R(I0)
    /// option. This option makes the terminal exit multiple mode and enter one-shot mode." So there
    /// is no separate "off" command: 0 always means one-shot, whatever the terminal was doing.
    /// </remarks>
    private int ReadInputMode(ReadOnlySpan<char> options, int at)
    {
        while (at < options.Length && options[at] == ' ') at++;

        int start = at;
        while (at < options.Length && char.IsDigit(options[at])) at++;

        // "When the terminal receives R(I), it returns a carriage return" - before any digit is
        // looked at, so a bare R(I) still synchronizes.
        Report(string.Empty);

        if (at == start) return at;

        bool multiple = options[start] == '1';
        _inputMode = multiple ? RegisGraphicsInputMode.Multiple : RegisGraphicsInputMode.OneShot;

        // "The graphics cursor (input or output) indicates the active screen location. This location
        // is either the screen origin [0,0] or the point most recently moved or drawn to." So the
        // input cursor starts where the drawing point already is, and moves independently after
        // that.
        _inputCursorX = _x;
        _inputCursorY = _y;

        // A request cannot outlive the mode it was made in. Re-entering leaves nothing owed.
        _positionReportRequested = false;
        return at;
    }

    /// <summary>
    /// Reads the <c>(I)</c> of <c>R(P(I))</c>, the report position interactive option.
    /// </summary>
    /// <param name="options">
    /// The characters inside the report command's parentheses.
    /// </param>
    /// <param name="at">
    /// Index just past the P.
    /// </param>
    /// <param name="interactive">
    /// Set true when this really was the interactive form.
    /// </param>
    /// <returns>
    /// The index just past what this consumed, or <paramref name="at"/> when it was not the
    /// interactive form and the caller should treat the P as a plain position report.
    /// </returns>
    /// <remarks>
    /// <para><b>Chapter 10, "Report Position Interactive"</b></para>
    /// "This option lets an application request an input cursor position report at any time. You only
    /// use this option when the terminal is in a graphics input mode (one-shot or multiple)."
    ///
    /// In one-shot mode "the terminal does not return an input cursor position report until you press
    /// an active nonarrow key or a button on the locator device", and the report then carries that
    /// keystroke ahead of the coordinates.
    ///
    /// In multiple mode "the terminal immediately returns an input cursor position report".
    ///
    /// <para><b>What prefixes the multiple-mode answer</b></para>
    /// The two chapters disagree. Chapter 10 says the multiple-mode report "contains only the cursor
    /// position". Chapter 15 prints a worked example of exactly this case - an application request in
    /// multiple mode - as <c>CSI 240 ~ [100,100] CR</c>, and explains why: "The null button sequence
    /// indicates this report is the result of an application request, not a locator button
    /// transition." The worked example is the stronger evidence, and it also matches the standing
    /// rule that "if the host requests a report when none of the buttons are in use, the terminal
    /// responds with a null-button code: CSI 240 ~". So the null button code goes in front.
    /// </remarks>
    private int ReadInteractiveRequest(ReadOnlySpan<char> options, int at, out bool interactive)
    {
        interactive = false;

        int scan = at;
        while (scan < options.Length && options[scan] == ' ') scan++;
        if (scan >= options.Length || options[scan] != '(') return at;

        int close = FindClose(options, scan, '(', ')');
        var inner = options.Slice(scan + 1, close - scan - 1);

        int k = 0;
        while (k < inner.Length && inner[k] == ' ') k++;
        if (k >= inner.Length || char.ToUpperInvariant(inner[k]) != 'I') return at;

        interactive = true;

        if (_inputMode == RegisGraphicsInputMode.Multiple)
        {
            Report(NullButtonCode + "[" + _inputCursorX + "," + _inputCursorY + "]");
        }
        else if (_inputMode == RegisGraphicsInputMode.OneShot)
        {
            // Nothing goes back yet. SendInputReport answers this when a key is pressed.
            _positionReportRequested = true;
        }

        return close + 1;
    }

    /// <summary>
    /// The sequence that stands in for a locator button when no button was pressed.
    /// </summary>
    /// <remarks>
    /// "If the host requests a report when none of the buttons are in use, the terminal responds with
    /// a null-button code: CSI 240 ~". Built from the character code rather than written as an escape
    /// inside a literal: a raw ESC byte is invisible in a diff, and the short hex escape takes UP TO
    /// four digits, so it swallows whatever follows if that happens to be a hex digit.
    ///
    /// Sent as the seven-bit CSI - ESC then a left bracket - which every host that reads ReGIS
    /// accepts. A VT340 in eight-bit mode would send the single byte instead.
    /// </remarks>
    private static readonly string NullButtonCode = ((char)0x1B) + "[240~";

    /// <summary>
    /// Reads the macrograph form of the report command, either one macrograph or the storage total.
    /// </summary>
    /// <param name="options">
    /// The characters inside the report command's parentheses.
    /// </param>
    /// <param name="at">
    /// Index just past the M.
    /// </param>
    /// <returns>
    /// The index just past what this consumed.
    /// </returns>
    private int ReadMacrographReport(ReadOnlySpan<char> options, int at)
    {
        while (at < options.Length && options[at] == ' ') at++;
        if (at >= options.Length || options[at] != '(') return at;

        int close = FindClose(options, at, '(', ')');
        var inner = options.Slice(at + 1, close - at - 1);

        int k = 0;
        while (k < inner.Length && inner[k] == ' ') k++;

        if (k < inner.Length && inner[k] == '=')
        {
            // "The terminal reports this information as two integer strings, separated by a comma
            // and enclosed in double quotes" - available first, then the total.
            int used = MacrographBytesStored();
            int available = MacrographStorageBytes - used;
            if (available < 0) available = 0;

            Report("\"" + available + "," + MacrographStorageBytes + "\"");
            return close + 1;
        }

        if (k < inner.Length && char.IsLetter(inner[k]))
        {
            char letter = inner[k];
            int slot = Slot(letter);
            string body = _macrographs != null ? _macrographs[slot] ?? "" : "";

            // "The macrograph contents report starts with a macrograph report indicator, @=<call
            // letter>... The report ends with a macrograph terminator and a carriage return, @;"
            // An undefined macrograph reports "a null macrograph (no characters) enclosed in the
            // indicator and terminator" - so the shape is the same and only the body is empty.
            Report("@=" + letter + body + "@;");
        }

        return close + 1;
    }

    /// <summary>
    /// How many bytes of macrograph body are currently stored.
    /// </summary>
    /// <returns>
    /// The total length of every defined macrograph.
    /// </returns>
    private int MacrographBytesStored()
    {
        if (_macrographs == null) return 0;

        int total = 0;
        for (int slot = 0; slot < MacrographCount; slot++)
        {
            string? body = _macrographs[slot];
            if (body != null) total += body.Length;
        }

        return total;
    }

    /// <summary>
    /// Adds one report, with the carriage return every VT300 report ends with.
    /// </summary>
    /// <param name="text">
    /// The report body.
    /// </param>
    private void Report(string text)
    {
        _reports ??= new System.Text.StringBuilder();
        _reports.Append(text).Append('\r');
    }

    /// <summary>
    /// Copies the write control state onto the surface, so a pixel write knows which planes it may
    /// change, which code it is writing, and what to do with the code already there.
    /// </summary>
    /// <param name="surface">
    /// Where the pixels go.
    /// </param>
    private void ApplyWriteState(IGraphicsSurface surface)
    {
        surface.IndexedMap = _colours;
        surface.DrawIndex = _currentRegister;
        surface.PlaneMask = _planeMask;
        surface.WritingMode = _writingMode;
        surface.ShadeToY = _shadeVertical ? GraphicsShading.None : _shadeReference;
        surface.ShadeToX = _shadeVertical ? _shadeReference : GraphicsShading.None;
        surface.BackgroundIndex = _replaceWriting ? _backgroundRegister : -1;
        surface.ShadeStencil = BuildShadeStencil();
    }

    /// <summary>
    /// The top 8 rows of the shading character's cell, or null when shading is solid.
    /// </summary>
    /// <remarks>
    /// Eight rows, not ten, and taken from the top - the manual is explicit: "No matter what type of
    /// shading character you use, the terminal only displays the top 8 x 8 matrix of the 8 x 10
    /// character cell."
    /// The rows are read at the STORED height so they are the stored rows themselves. Asking for
    /// eight rows directly would resample ten into eight and drop whichever two the arithmetic
    /// landed between, which for these glyphs are the rows that tell one character from another.
    /// A character whose top eight rows are all blank would shade nothing at all, so it falls back
    /// to a solid fill; a host that asked for shading meant to see some.
    /// </remarks>
    /// <returns>
    /// Eight bytes, or null.
    /// </returns>
    private byte[]? BuildShadeStencil()
    {
        if (_shadeCharacter == '\0') return null;

        var rows = new byte[8];
        bool anyInk = false;

        for (int row = 0; row < rows.Length; row++)
        {
            rows[row] = Text.RowFor(_shadeCharacter, row, RegisTextState.StoredCellHeight);
            if (rows[row] != 0) anyInk = true;
        }

        return anyInk ? rows : null;
    }

    /// <summary>
    /// The pattern bits as the drawing will actually read them.
    /// </summary>
    /// <returns>
    /// Pattern memory, inverted when negative pattern control is on.
    /// </returns>
    /// <remarks>
    /// Negation is applied here rather than stored inverted so that <c>W(N1)</c> followed by
    /// <c>W(N0)</c> gives the pattern back unchanged, and so a pattern loaded while negation is on
    /// means the same bits it would mean with negation off.
    /// </remarks>
    private ushort EffectivePatternMask()
        => _negatePattern ? (ushort)(~_patternMask & 0xFF) : _patternMask;

    /// <summary>
    /// <c>W(I n)</c> selects the colour register everything after it draws in.
    /// </summary>
    private int HandleWrite(ReadOnlySpan<char> commands, int i)
    {
        bool understood = false;

        while (i < commands.Length && commands[i] == '(')
        {
            int close = FindClose(commands, i, '(', ')');
            var options = commands.Slice(i + 1, close - i - 1);

            for (int o = 0; o < options.Length; o++)
            {
                char option = options[o];

                if (option == 'I' || option == 'i')
                {
                    int value = 0;
                    bool any = false;
                    for (int d = o + 1; d < options.Length; d++)
                    {
                        if (options[d] < '0' || options[d] > '9') break;
                        value = value * 10 + (options[d] - '0');
                        any = true;
                    }

                    if (any && GraphicsColorMap.IsRegister(value))
                    {
                        _currentRegister = value;
                        understood = true;
                    }

                    continue;
                }

                if (option == 'P' || option == 'p')
                {
                    // The end index matters, not just the success flag: the option scan walks every
                    // character, so leaving it to carry on through "4(M2)" would meet that M and
                    // read it as a PV multiplier. The pattern's own multiplier is a DIFFERENT
                    // setting that happens to share a letter.
                    int end = ReadPatternControl(options, o + 1);
                    if (end > o + 1) understood = true;
                    o = end - 1;
                    continue;
                }

                // PV multiplication. "The PV multiplier command lets you specify the number of
                // times to repeat each PV number. For example, suppose you use a multiplier of 10.
                // Then each PV number in later commands specifies movement for 10 coordinates."
                //
                // A bare M at option level, as in W(M20,I0). Not to be confused with the pattern
                // multiplier, which is a parenthesised group that can only follow a pattern.
                if (option == 'M' || option == 'm')
                {
                    if (ReadOptionNumber(options, o + 1, out int steps) && steps > 0)
                    {
                        _pvMultiplier = steps;
                        understood = true;
                    }

                    continue;
                }

                // Shading. W(S1) on, W(S0) off, and either may be followed by a position that
                // names the reference line - W(S1[,125]) is the manual's own example.
                if (option == 'S' || option == 's')
                {
                    // Every form goes to ReadShadingControl, INCLUDING the character form W(S'x').
                    // It used to be detected here and skipped, which meant the quoted character was
                    // recognised and then thrown away - shading simply stayed off. Reading it in one
                    // place is also what lets the manual's combined forms work, where the character
                    // and the reference line arrive together: W(S'X'(X)[400]) and W(S'X'[,125]).
                    o = ReadShadingControl(options, o + 1) - 1;
                    understood = true;
                    continue;
                }

                // Plane select. "For the VT340, you use a code number from 0 to 15... Notice that
                // the plane numbers in Tables 3-2 and 3-3 correspond to the binary numbers you use
                // to access the pixels" - so the code IS the mask, one bit per plane, and no table
                // lookup is needed.
                if (option == 'F' || option == 'f')
                {
                    if (ReadOptionNumber(options, o + 1, out int planes))
                    {
                        _planeMask = planes & 15;
                        understood = true;
                    }

                    continue;
                }

                // Negative pattern control. W(N1) turns it on, W(N0) off.
                if (option == 'N' || option == 'n')
                {
                    if (ReadOptionNumber(options, o + 1, out int negate))
                    {
                        _negatePattern = negate != 0;
                        understood = true;
                    }

                    continue;
                }

                // The writing styles. Table on printed page 56: overlay draws the foreground only
                // and is the default, complement draws the foreground and ignores the foreground
                // intensity, erase overwrites the foreground with the background.
                if (option == 'V' || option == 'v')
                {
                    _writingMode = GraphicsWritingMode.Overlay;
                    _replaceWriting = false;
                    understood = true;
                    continue;
                }

                if (option == 'C' || option == 'c')
                {
                    _writingMode = GraphicsWritingMode.Complement;
                    _replaceWriting = false;
                    understood = true;
                    continue;
                }

                if (option == 'E' || option == 'e')
                {
                    _writingMode = GraphicsWritingMode.Erase;
                    _replaceWriting = false;
                    understood = true;
                    continue;
                }

                // Replace writing is the one style that is NOT foreground-only: "Foreground and
                // background", so the pattern's 0 bits write the background intensity instead of
                // leaving the pixel alone. The foreground half behaves like overlay.
                if (option == 'R' || option == 'r')
                {
                    _writingMode = GraphicsWritingMode.Overlay;
                    _replaceWriting = true;
                    understood = true;
                    continue;
                }
            }

            i = close + 1;
        }

        // The shading CHARACTER option and the PV multiplication option are not built, so a W that
        // did nothing this decoder understood is counted rather than passing silently, and a real
        // host says which of them it needs.
        if (!understood) Count('W');
        return i;
    }

    /// <summary>
    /// Reads a shading control option and its optional reference line.
    /// </summary>
    /// <param name="options">
    /// The characters inside the write control parentheses.
    /// </param>
    /// <param name="at">
    /// Index just past the S.
    /// </param>
    /// <returns>
    /// The index just past everything this option consumed.
    /// </returns>
    /// <remarks>
    /// <para><b>The forms, from printed pages 69 and 76</b></para>
    ///  - <c>W(S0)</c> turns shading off, and <c>W(S1)</c> turns it on.
    ///  - the default line is horizontal, "defined by the Y-coordinate of the cursor position when
    ///    you turn shading on".
    ///  - <c>W(S1[,125])</c> names a horizontal line instead. "If you include an X-coordinate
    ///    ([X,Y]), ReGIS ignores the X-coordinate."
    ///  - <c>W(S(X)[400])</c> names a vertical one, and there the Y-coordinate is the ignored one.
    ///  - omitting the coordinate "is the same as using the W(S1) command".
    /// </remarks>
    private int ReadShadingControl(ReadOnlySpan<char> options, int at)
    {
        while (at < options.Length && options[at] == ' ') at++;

        // (X) ahead of everything else selects a vertical reference line.
        bool vertical = false;
        if (at < options.Length && options[at] == '(')
        {
            int close = FindClose(options, at, '(', ')');
            var inner = options.Slice(at + 1, close - at - 1);

            for (int k = 0; k < inner.Length; k++)
            {
                if (inner[k] == 'X' || inner[k] == 'x') vertical = true;
            }

            at = close + 1;
            while (at < options.Length && options[at] == ' ') at++;
        }

        // The shading CHARACTER, if one is quoted: W(S'X'). Read before the on/off digit because
        // naming a character is itself a way of turning shading on - the manual's own examples are
        // W(S'X') with no digit at all.
        bool namedCharacter = false;
        if (at < options.Length && (options[at] == '\'' || options[at] == '"'))
        {
            char quote = options[at];
            at++;

            if (at < options.Length && options[at] != quote)
            {
                _shadeCharacter = options[at];
                namedCharacter = true;
                at++;
            }

            if (at < options.Length && options[at] == quote) at++;
            while (at < options.Length && options[at] == ' ') at++;

            // The reference line may follow the character, and may be the vertical form:
            // W(S'X'(X)[400]) and W(S'X'[125]) are both in the manual.
            if (at < options.Length && options[at] == '(')
            {
                int close = FindClose(options, at, '(', ')');
                var inner = options.Slice(at + 1, close - at - 1);

                for (int k = 0; k < inner.Length; k++)
                {
                    if (inner[k] == 'X' || inner[k] == 'x') vertical = true;
                }

                at = close + 1;
                while (at < options.Length && options[at] == ' ') at++;
            }
        }

        // The on/off digit is optional - W(S[,125]) turns shading on by naming a line.
        bool on = true;
        if (at < options.Length && (options[at] == '0' || options[at] == '1'))
        {
            on = options[at] == '1';
            at++;
            while (at < options.Length && options[at] == ' ') at++;
        }

        int reference = vertical ? _x : _y;

        if (at < options.Length && options[at] == '[')
        {
            int close = FindClose(options, at, '[', ']');
            var text = options.Slice(at + 1, close - at - 1);

            int comma = -1;
            for (int k = 0; k < text.Length; k++)
            {
                if (text[k] == ',') { comma = k; break; }
            }

            var xPart = comma < 0 ? text : text.Slice(0, comma);
            var yPart = comma < 0 ? ReadOnlySpan<char>.Empty : text.Slice(comma + 1);

            // ApplyAxis rather than ReadCoordinate: naming a shading reference line must NOT move
            // the drawing point, and it still has to honour a relative coordinate like [,+40].
            reference = vertical ? ApplyAxis(xPart, _x) : ApplyAxis(yPart, _y);
            at = close + 1;
        }

        _shadeVertical = vertical;

        if (namedCharacter) on = true;
        _shadeReference = on ? reference : GraphicsShading.None;

        // Turning shading off drops the character too, so the next W(S1) is a solid fill again
        // rather than quietly inheriting a character from several commands ago.
        if (!on) _shadeCharacter = '\0';

        return at;
    }

    /// <summary>
    /// Reads the decimal number that follows a single-letter write control option.
    /// </summary>
    /// <param name="options">
    /// The characters inside the write control parentheses.
    /// </param>
    /// <param name="at">
    /// Index just past the option letter.
    /// </param>
    /// <param name="value">
    /// The number read.
    /// </param>
    /// <returns>
    /// True when there was a number to read.
    /// </returns>
    private static bool ReadOptionNumber(ReadOnlySpan<char> options, int at, out int value)
    {
        value = 0;
        bool any = false;

        while (at < options.Length && options[at] == ' ') at++;

        while (at < options.Length && options[at] >= '0' && options[at] <= '9')
        {
            value = (value * 10) + (options[at] - '0');
            any = true;
            at++;
        }

        return any;
    }

    /// <summary>
    /// Reads a pattern control option: P followed by a standard number or a binary pattern, and an
    /// optional multiplier.
    /// </summary>
    /// <param name="options">
    /// The characters inside the write control parentheses.
    /// </param>
    /// <param name="at">
    /// Index just past the P.
    /// </param>
    /// <returns>
    /// The index just past everything the pattern consumed, or <paramref name="at"/> when there was
    /// no pattern to read.
    /// </returns>
    /// <remarks>
    /// <para><b>One digit is a standard pattern; two or more are the bits themselves</b></para>
    /// Chapter 3 gives two forms the same letter. <c>W(P4)</c> selects standard pattern 4 from table
    /// 3-1, and <c>W(P1001)</c> writes those bits straight into pattern memory - "a pattern of 1 and
    /// 0 bits, from 2 to 8 bits long". The LENGTH tells them apart, which is what makes
    /// <c>W(P10(M2))</c> in hackerb9's registest.sh a two-bit binary pattern rather than a standard
    /// pattern 10 that does not exist.
    ///
    /// "If you specify less than 8 bits, the terminal repeats as much of your pattern as it can in
    /// the remaining bits of pattern memory" - so two bits fill all eight, and three repeat twice
    /// and leave two bits of a third copy, which is what figure 3-3 shows.
    ///
    /// The multiplier makes each bit cover that many pixels.
    /// </remarks>
    private int ReadPatternControl(ReadOnlySpan<char> options, int at)
    {
        int digits = 0;
        while (at + digits < options.Length
            && (options[at + digits] == '0' || options[at + digits] == '1'))
        {
            digits++;
        }

        // A standard pattern number can be any digit; only the binary form is all ones and zeros.
        // So a lone digit is taken here even when it is neither.
        if (digits <= 1)
        {
            if (at < options.Length && options[at] >= '0' && options[at] <= '9')
            {
                _patternMask = StandardPattern(options[at] - '0');
                _patternPhase = 0;
                return ReadPatternMultiplier(options, at + 1);
            }

            return at;
        }

        // "If you specify more than 8 bits, the terminal uses only the last 8 bits of your pattern."
        int first = digits > PatternBits ? at + digits - PatternBits : at;
        int used = digits > PatternBits ? PatternBits : digits;

        int pattern = 0;
        for (int b = 0; b < used; b++)
        {
            // The leftmost bit written is the first one drawn, so it goes in the LOWEST bit of the
            // mask - which is the end the line walk reads from.
            if (options[first + b] == '1') pattern |= 1 << b;
        }

        int filled = pattern;
        for (int b = used; b < PatternBits; b++)
        {
            if ((pattern & (1 << (b % used))) != 0) filled |= 1 << b;
        }

        _patternMask = (ushort)filled;
        _patternPhase = 0;
        return ReadPatternMultiplier(options, at + digits);
    }

    /// <summary>
    /// Reads the multiplier group that may follow a pattern.
    /// </summary>
    /// <param name="options">
    /// The characters inside the write control parentheses.
    /// </param>
    /// <param name="at">
    /// Where to look.
    /// </param>
    /// <returns>
    /// The index just past the multiplier group, or <paramref name="at"/> when there was none.
    /// </returns>
    private int ReadPatternMultiplier(ReadOnlySpan<char> options, int at)
    {
        _patternMultiplier = DefaultPatternMultiplier;

        int start = at;
        while (at < options.Length && options[at] == ' ') at++;
        if (at >= options.Length || options[at] != '(') return start;

        // The whole group is stepped over whether or not it turns out to hold a multiplier, so the
        // option scan cannot meet the M inside it and read it as PV multiplication.
        int close = FindClose(options, at, '(', ')');

        at++;
        while (at < options.Length && options[at] == ' ') at++;
        if (at >= options.Length || (options[at] != 'M' && options[at] != 'm')) return close + 1;

        at++;
        int value = 0;
        bool any = false;
        while (at < options.Length && options[at] >= '0' && options[at] <= '9')
        {
            value = (value * 10) + (options[at] - '0');
            any = true;
            at++;
        }

        // "The minimum value is 1, the maximum value is 16."
        if (any && value > 0)
        {
            _patternMultiplier = value > MaxPatternMultiplier ? MaxPatternMultiplier : value;
        }

        return close + 1;
    }

    /// <summary>
    /// Table 3-1, the ten standard write patterns.
    /// </summary>
    /// <param name="number">
    /// 0 to 9. Anything else gives all-on, which is what pattern memory powers on with.
    /// </param>
    /// <returns>
    /// The eight bits, first-drawn bit lowest.
    /// </returns>
    /// <remarks>
    /// Transcribed from the printed page, like every other table in this manual. The bits are
    /// reversed from the way the manual prints them because the line walk reads from the low bit.
    /// </remarks>
    private static ushort StandardPattern(int number)
    {
        switch (number)
        {
            case 0: return 0b00000000;              // all-off
            case 1: return 0b11111111;              // all-on
            case 2: return Reverse(0b11110000);     // dash
            case 3: return Reverse(0b11100100);     // dash-dot
            case 4: return Reverse(0b10101010);     // dot
            case 5: return Reverse(0b11101010);     // dash-dot-dot
            case 6: return Reverse(0b10001000);     // sparse dot
            case 7: return Reverse(0b10000100);     // asymmetrical sparse dot
            case 8: return Reverse(0b11001000);     // sparse dash-dot
            case 9: return Reverse(0b10000110);     // sparse dot-dash
            default: return 0b11111111;
        }
    }

    /// <summary>
    /// Turns a pattern written left to right into one read from the low bit up.
    /// </summary>
    /// <param name="pattern">
    /// The eight bits as the manual prints them.
    /// </param>
    /// <returns>
    /// The same pattern, first-drawn bit lowest.
    /// </returns>
    private static ushort Reverse(int pattern)
    {
        int reversed = 0;
        for (int b = 0; b < PatternBits; b++)
        {
            if ((pattern & (1 << (PatternBits - 1 - b))) != 0) reversed |= 1 << b;
        }
        return (ushort)reversed;
    }

    /// <summary>
    /// Reads one coordinate, absolute or relative.
    /// </summary>
    /// <remarks>
    /// A sign makes the number RELATIVE to the current point: <c>[+50,0]</c> is fifty to the right
    /// of wherever the drawing is, while <c>[50,0]</c> is fifty from the left edge. An empty field
    /// leaves that axis alone, which is how <c>[,100]</c> moves straight down.
    /// </remarks>
    private void ReadCoordinate(ReadOnlySpan<char> text)
    {
        CheckCoordinateValues(text);

        int comma = -1;
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] == ',') { comma = i; break; }
        }

        var xPart = comma < 0 ? text : text.Slice(0, comma);
        var yPart = comma < 0 ? ReadOnlySpan<char>.Empty : text.Slice(comma + 1);

        _x = ApplyAxis(xPart, _x);
        _y = ApplyAxis(yPart, _y);
    }

    /// <summary>
    /// Raises Table 10-1 code 3 when a coordinate holds more than an X and a Y.
    /// </summary>
    /// <remarks>
    /// "The syntax [X,Y] contained more than two coordinate values. The extra values were ignored."
    /// Ignoring them needs no code: <see cref="ReadCoordinate"/> splits on the FIRST comma and
    /// <see cref="ApplyAxis"/> stops at the next non-digit, so a third value was already dropped.
    /// All that was missing was saying so when the host asks.
    /// It lives in its own method because the hard copy control checks the same thing without
    /// applying the coordinate - naming a print area must not move the drawing point.
    /// </remarks>
    /// <param name="text">
    /// What was between the brackets.
    /// </param>
    private void CheckCoordinateValues(ReadOnlySpan<char> text)
    {
        int commas = 0;
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] != ',') continue;

            commas++;
            if (commas < 2) continue;

            RecordError(3, '\0');
            return;
        }
    }

    /// <summary>
    /// Applies one axis of a coordinate to its current value.
    /// </summary>
    private static int ApplyAxis(ReadOnlySpan<char> text, int current)
    {
        int i = 0;
        while (i < text.Length && (text[i] == ' ' || text[i] == '\r' || text[i] == '\n')) i++;

        if (i >= text.Length) return current;   // empty field: leave this axis where it is

        bool relative = text[i] == '+' || text[i] == '-';
        bool negative = text[i] == '-';
        if (relative) i++;

        int value = 0;
        bool anyDigits = false;
        for (; i < text.Length; i++)
        {
            if (text[i] < '0' || text[i] > '9') break;
            if (value < 1_000_000) value = value * 10 + (text[i] - '0');
            anyDigits = true;
        }

        if (!anyDigits) return current;
        if (negative) value = -value;

        return relative ? current + value : value;
    }

    /// <summary>
    /// Whether a character is layout rather than content - a space, a tab, or either half of a
    /// line ending. hackerb9's script puts newlines between commands, so these turn up inside
    /// option groups as well as between them.
    /// </summary>
    /// <param name="c">
    /// Character to test.
    /// </param>
    /// <returns>
    /// True when the character carries no meaning of its own.
    /// </returns>
    private static bool IsRegisSpace(char c) => c == ' ' || c == '\t' || c == '\r' || c == '\n';

    /// <summary>
    /// Walks a command's parenthesised option groups, applying a temporary WRITE control and
    /// skipping everything else.
    /// </summary>
    /// <remarks>
    /// Chapter 3 lets a drawing command carry its own write control, in force for that command
    /// alone: <c>V(W(F1))</c> draws through plane 1 and leaves the standing plane mask alone.
    /// This used to be skipped along with every other option, which is why hackerb9's sixteen boxes
    /// all came out the same. Each asks for a different plane mask, every one of them was ignored,
    /// and all sixteen filled through all four planes.
    /// Only WRITE is read. The other option groups select line styles and shading forms this
    /// decoder does not draw, and reading one of those as a write option would set a plane mask
    /// from a line style.
    /// </remarks>
    /// <param name="commands">
    /// The command string.
    /// </param>
    /// <param name="i">
    /// Index of the first character after the command letter.
    /// </param>
    /// <returns>
    /// Index of the first character after the option groups.
    /// </returns>
    private int ReadOptionGroups(ReadOnlySpan<char> commands, int i)
    {
        while (i < commands.Length && commands[i] == '(')
        {
            int close = FindClose(commands, i, '(', ')');
            var group = commands.Slice(i + 1, close - i - 1);

            int at = 0;
            while (at < group.Length && IsRegisSpace(group[at]))
            {
                at++;
            }

            if (at < group.Length && (group[at] == 'W' || group[at] == 'w'))
            {
                HandleWrite(group, at + 1);
            }
            else if (at < group.Length && (group[at] == 'B' || group[at] == 'b'))
            {
                PushPosition(dummy: false, option: 'B');
            }
            else if (at < group.Length && (group[at] == 'S' || group[at] == 's'))
            {
                PushPosition(dummy: true, option: 'S');
            }
            else if (at < group.Length && (group[at] == 'E' || group[at] == 'e'))
            {
                PopPosition();
            }

            i = close + 1;
        }

        return i;
    }

    /// <summary>
    /// Skips any parenthesised option groups at this position.
    /// </summary>
    /// <param name="commands">
    /// The command string.
    /// </param>
    /// <param name="i">
    /// Index of the opening parenthesis, or of whatever follows the groups.
    /// </param>
    /// <returns>
    /// Index of the first character after the option groups.
    /// </returns>
    private static int SkipOptionGroups(ReadOnlySpan<char> commands, int i)
    {
        while (i < commands.Length && commands[i] == '(')
        {
            i = FindClose(commands, i, '(', ')') + 1;
        }
        return i;
    }

    /// <summary>
    /// <c>F(...)</c> - polygon fill.
    /// </summary>
    /// <remarks>
    /// <para><b>F has no geometry of its own</b></para>
    /// Chapter 11 of
    /// <c>spec\DEC\EK-VT3XX-GP-002_VT330_VT340_Graphics_Programming_May88.pdf</c> says the polygon
    /// fill command "accepts all vector command options and arguments", the same for curve, the
    /// same for position, the same for write control. So the body is ordinary ReGIS and is run by
    /// the ordinary decoder - the only difference is that vectors and curves add vertices instead
    /// of putting ink down, and one fill happens at the end.
    ///
    /// <para><b>The rules the chapter states</b></para>
    ///  - "You must specify at least three different vertices, or ReGIS will not draw an image."
    ///  - "You can use up to 256 vertices. ReGIS ignores additional vertices."
    ///  - "If you map two consecutive vertices to the same pixel, they count as one vertex."
    ///  - "ReGIS saves the cursor position at the beginning of any polygon fill command. The cursor
    ///    returns to this position at the end of the command (whether or not any drawing takes
    ///    place)."
    ///  - "Only the last W option in a polygon fill command affects the graphic image, because
    ///    ReGIS does not draw the image until the end." Falls out for free: the register is read
    ///    when the fill happens, so whichever W ran last is the one that counts.
    ///  - Write controls inside an F are TEMPORARY, so the register is put back afterwards.
    ///  - An unclosed figure, or crossing closed figures, are "unpredictable" on real hardware -
    ///    there is no documented result to match, so any sane one is right.
    ///
    /// <para><b>Nesting</b></para>
    /// An F inside an F is not something the chapter defines. It is counted and skipped rather
    /// than guessed at, which also keeps the single vertex buffer honest.
    /// </remarks>
    /// <param name="commands">
    /// The whole command string.
    /// </param>
    /// <param name="i">
    /// Index of the character just after the <c>F</c>.
    /// </param>
    /// <param name="surface">
    /// Where the filled shape goes.
    /// </param>
    /// <param name="depth">
    /// How many macrographs deep the caller already is.
    /// </param>
    /// <returns>
    /// Index of the first character after the fill command.
    /// </returns>
    private int HandleFill(ReadOnlySpan<char> commands, int i, IGraphicsSurface surface, int depth)
    {
        if (i >= commands.Length || commands[i] != '(')
        {
            Count('F');
            return SkipArguments(commands, i);
        }

        int close = FindClose(commands, i, '(', ')');
        var body = commands.Slice(i + 1, close - i - 1);
        int after = close + 1;

        if (_collectingFill)
        {
            Count('F');
            return after;
        }

        int savedX = _x;
        int savedY = _y;
        int savedRegister = _currentRegister;

        // The plane mask and the writing style are write controls too, so they are temporary inside
        // an F for the same reason the register is. Only the register used to be put back, so a
        // W(F1) inside a fill leaked out and changed every drawing after it.
        int savedPlaneMask = _planeMask;
        var savedWritingMode = _writingMode;

        _fillVertices ??= new int[MaxFillVertices * 2];
        _fillVertexCount = 0;
        _collectingFill = true;

        try
        {
            Decode(body, surface, depth);
        }
        finally
        {
            _collectingFill = false;
        }

        // The first and last vertex meeting is a closed figure, not a 257th vertex.
        if (_fillVertexCount >= 2)
        {
            int lastX = _fillVertices[(_fillVertexCount - 1) * 2];
            int lastY = _fillVertices[(_fillVertexCount - 1) * 2 + 1];
            if (lastX == _fillVertices[0] && lastY == _fillVertices[1]) _fillVertexCount--;
        }

        if (_fillVertexCount >= 3)
        {
            // The write state has to reach the surface BEFORE the fill, not merely be set in the
            // decoder. Nothing else pushes it here: the body of an F collects vertices instead of
            // drawing, so a temporary W inside it never got as far as a pixel write, and the fill
            // ran with whatever mask the last real drawing had left behind - normally all four
            // planes.
            // That is what made hackerb9's sixteen boxes come out identical. Every one of them
            // asks for a different plane mask, and every one of them was filling through mask 15.
            ApplyWriteState(surface);

            surface.FillPolygon(
                new ReadOnlySpan<int>(_fillVertices, 0, _fillVertexCount * 2),
                _colours.Register(_currentRegister));
        }

        // Write controls inside an F are temporary.
        _currentRegister = savedRegister;
        _planeMask = savedPlaneMask;
        _writingMode = savedWritingMode;
        _x = savedX;
        _y = savedY;
        return after;
    }

    /// <summary>
    /// Adds one vertex to the polygon being collected.
    /// </summary>
    /// <remarks>
    /// Drops a vertex that repeats the one before it - "if you map two consecutive vertices to the
    /// same pixel, they count as one vertex" - and stops at the ceiling rather than growing, since
    /// the manual says the extras are ignored.
    /// </remarks>
    /// <param name="x">
    /// Horizontal position.
    /// </param>
    /// <param name="y">
    /// Vertical position.
    /// </param>
    private void AddVertex(int x, int y)
    {
        if (_fillVertices == null || _fillVertexCount >= MaxFillVertices) return;

        if (_fillVertexCount > 0)
        {
            int last = (_fillVertexCount - 1) * 2;
            if (_fillVertices[last] == x && _fillVertices[last + 1] == y) return;
        }

        _fillVertices[_fillVertexCount * 2] = x;
        _fillVertices[_fillVertexCount * 2 + 1] = y;
        _fillVertexCount++;
    }

    /// <summary>
    /// Adds a circle to the polygon being collected, as <see cref="CircleSides"/> straight sides.
    /// </summary>
    /// <param name="centreX">
    /// Centre, horizontal.
    /// </param>
    /// <param name="centreY">
    /// Centre, vertical.
    /// </param>
    /// <param name="radius">
    /// Radius in pixels; zero or less adds nothing.
    /// </param>
    private void AddCircle(int centreX, int centreY, int radius)
    {
        if (radius <= 0) return;

        for (int s = 0; s < CircleSides; s++)
        {
            AddVertex(
                centreX + (int)Math.Round(radius * CircleCosine[s]),
                centreY + (int)Math.Round(radius * CircleSine[s]));
        }
    }

    /// <summary>
    /// The unit circle at <see cref="CircleSides"/> steps, built once.
    /// </summary>
    /// <remarks>
    /// A table rather than a call to <see cref="Math.Cos"/> per vertex: a page of filled circles
    /// would otherwise pay for the same 128 trigonometric calls over and over.
    /// </remarks>
    private static readonly double[] CircleCosine = BuildCircle(true);

    /// <summary>
    /// See <see cref="CircleCosine"/>.
    /// </summary>
    private static readonly double[] CircleSine = BuildCircle(false);

    /// <summary>
    /// Builds one axis of the unit-circle table.
    /// </summary>
    /// <param name="cosine">
    /// True for the cosine table, false for the sine table.
    /// </param>
    /// <returns>
    /// The table.
    /// </returns>
    private static double[] BuildCircle(bool cosine)
    {
        var table = new double[CircleSides];
        for (int s = 0; s < CircleSides; s++)
        {
            double angle = 2.0 * Math.PI * s / CircleSides;
            table[s] = cosine ? Math.Cos(angle) : Math.Sin(angle);
        }

        return table;
    }

    /// <summary>
    /// The macrograph command - define, invoke, and clear.
    /// </summary>
    /// <remarks>
    /// <para><b>The three forms</b></para>
    /// From the "Macrographs" chapter of
    /// <c>spec\DEC\EK-VT3XX-GP-002_VT330_VT340_Graphics_Programming_May88.pdf</c>:
    ///  - <c>@:letter definition @;</c> defines a macrograph. An empty definition clears that one.
    ///  - <c>@letter</c> invokes one, inserting its contents into the command stream.
    ///  - <c>@.</c> clears all 26.
    ///
    /// <para><b>Why this could not stay counted-and-skipped</b></para>
    /// Every other unimplemented command is skipped by its brackets, so it costs its own shape and
    /// nothing else. The macrograph has no brackets. Left to the generic path, <c>@</c> was passed
    /// over as punctuation and a DEFINITION'S BODY WAS DRAWN THE MOMENT IT WAS DEFINED - the exact
    /// opposite of "the VT300 does not draw macrographs when you define them" - while every later
    /// invocation drew nothing. Storing the body fixes both directions at once, and the body is
    /// made of the commands this decoder already runs.
    /// </remarks>
    /// <param name="commands">
    /// The whole command string.
    /// </param>
    /// <param name="i">
    /// Index of the character just after the <c>@</c>.
    /// </param>
    /// <param name="surface">
    /// Where an invoked macrograph draws.
    /// </param>
    /// <param name="depth">
    /// How many macrographs deep the caller already is.
    /// </param>
    /// <returns>
    /// Index of the first character after this macrograph command.
    /// </returns>
    private int HandleMacrograph(ReadOnlySpan<char> commands, int i, IGraphicsSurface surface,
        int depth)
    {
        if (i >= commands.Length) return i;

        char c = commands[i];

        // @. clears all 26 slots.
        if (c == '.')
        {
            if (_macrographs != null)
            {
                for (int slot = 0; slot < MacrographCount; slot++) _macrographs[slot] = null;
            }

            return i + 1;
        }

        // @: opens a definition. The call letter follows, then the body, then @;.
        if (c == ':')
        {
            int letter = i + 1;
            while (letter < commands.Length && !char.IsLetter(commands[letter])) letter++;
            if (letter >= commands.Length) return commands.Length;

            int slot = Slot(commands[letter]);
            int bodyStart = letter + 1;
            int bodyEnd = FindDefinitionEnd(commands, bodyStart);

            // Stored even when empty: "@; clears the selected macrographs by specifying a blank
            // definition", and an empty slot invoked is explicitly not an error.
            string body = commands.Slice(bodyStart, bodyEnd - bodyStart).ToString();
            EnsureMacrographs()[slot] = body.Length != 0 ? body : null;

            // Step past the closing @; when the host sent one.
            return bodyEnd < commands.Length ? bodyEnd + 2 : commands.Length;
        }

        // @letter invokes.
        if (char.IsLetter(c))
        {
            Invoke(Slot(c), surface, depth);
            return i + 1;
        }

        // Anything else after an @ is not a macrograph command the manual defines.
        return i;
    }

    /// <summary>
    /// Replays a stored macrograph, honouring the nesting limit and the no-self-call rule.
    /// </summary>
    /// <param name="slot">
    /// Which of the 26 slots to run.
    /// </param>
    /// <param name="surface">
    /// Where it draws.
    /// </param>
    /// <param name="depth">
    /// How many macrographs deep the caller already is.
    /// </param>
    private void Invoke(int slot, IGraphicsSurface surface, int depth)
    {
        if (_macrographs == null || _macrographBusy == null) return;

        string? body = _macrographs[slot];
        if (body == null) return;                       // an empty macrograph is not an error
        if (depth >= MacrographNestLimit) return;
        if (_macrographBusy[slot]) return;              // "a macrograph cannot call itself"

        _macrographBusy[slot] = true;
        try
        {
            // AsSpan, not a substring: a replayed logo must not allocate on every frame.
            Decode(body.AsSpan(), surface, depth + 1);
        }
        finally
        {
            _macrographBusy[slot] = false;
        }
    }

    /// <summary>
    /// Finds where a macrograph definition ends.
    /// </summary>
    /// <remarks>
    /// The terminator is <c>@;</c>. Quoted strings are stepped over while looking for it, because
    /// "ReGIS does not recognize any commands in a quoted text string" - so an <c>@;</c> inside
    /// quotes is text, not the end of the definition. Definitions cannot nest, so the first
    /// terminator outside quotes is the right one.
    /// </remarks>
    /// <param name="commands">
    /// The whole command string.
    /// </param>
    /// <param name="i">
    /// Index of the first character of the body.
    /// </param>
    /// <returns>
    /// Index of the terminating <c>@</c>, or the end of the span when the host never closed it.
    /// </returns>
    private static int FindDefinitionEnd(ReadOnlySpan<char> commands, int i)
    {
        while (i < commands.Length)
        {
            char c = commands[i];

            if (c == '\'' || c == '"')
            {
                int j = i + 1;
                while (j < commands.Length && commands[j] != c) j++;
                i = j < commands.Length ? j + 1 : j;
                continue;
            }

            if (c == '@' && i + 1 < commands.Length && commands[i + 1] == ';') return i;

            i++;
        }

        return commands.Length;
    }

    /// <summary>
    /// Turns a call letter into a slot number. Case insensitive, as the manual states.
    /// </summary>
    /// <param name="letter">
    /// The call letter.
    /// </param>
    /// <returns>
    /// A slot in 0 to 25.
    /// </returns>
    private static int Slot(char letter)
    {
        // char.IsLetter is true for far more than A to Z, so the subtraction is clamped rather
        // than trusted - a stray accented letter must not index off the end of the array.
        int slot = char.ToUpperInvariant(letter) - 'A';
        if ((uint)slot >= (uint)MacrographCount) return 0;
        return slot;
    }

    /// <summary>
    /// Allocates the macrograph storage on first use.
    /// </summary>
    /// <returns>
    /// The definition array.
    /// </returns>
    private string?[] EnsureMacrographs()
    {
        if (_macrographs == null)
        {
            _macrographs = new string[MacrographCount];
            _macrographBusy = new bool[MacrographCount];
        }

        return _macrographs;
    }

    /// <summary>
    /// Skips every bracket and parenthesis group belonging to a command, and any quoted text.
    /// </summary>
    /// <remarks>
    /// This is what lets an unimplemented command be counted without losing the rest of the
    /// drawing: skip its arguments and carry on at the next command letter.
    /// </remarks>
    private static int SkipArguments(ReadOnlySpan<char> commands, int i)
    {
        while (i < commands.Length)
        {
            char c = commands[i];

            if (c == '(') { i = FindClose(commands, i, '(', ')') + 1; continue; }
            if (c == '[') { i = FindClose(commands, i, '[', ']') + 1; continue; }

            if (c == '\'' || c == '"')
            {
                // Quoted text, as the T command carries. It may contain brackets of its own.
                int j = i + 1;
                while (j < commands.Length && commands[j] != c) j++;
                i = j < commands.Length ? j + 1 : j;
                continue;
            }

            if (c == ' ' || c == '\r' || c == '\n' || c == '\t' || c == ',' || c == ';')
            {
                i++;
                continue;
            }

            break;
        }

        return i;
    }

    /// <summary>
    /// Finds the closing bracket that matches the one at <paramref name="open"/>, allowing nesting.
    /// </summary>
    /// <returns>
    /// The index of the closing bracket, or the end of the span when the host never closed it.
    /// </returns>
    private static int FindClose(ReadOnlySpan<char> commands, int open, char opener, char closer)
    {
        int depth = 0;
        for (int i = open; i < commands.Length; i++)
        {
            if (commands[i] == opener) depth++;
            else if (commands[i] == closer)
            {
                depth--;
                if (depth == 0) return i;
            }
        }

        // An unclosed group is a truncated stream, not a reason to fall over.
        return commands.Length - 1;
    }

    private void Count(char command)
    {
        _unhandled.TryGetValue(command, out int seen);
        _unhandled[command] = seen + 1;
    }
}
