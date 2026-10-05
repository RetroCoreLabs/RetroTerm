using System;
using System.Globalization;
using System.Text;
using RetroTerm.Core.Logging;
using RetroTerm.Core.Terminal.Buffer;
using RetroTerm.Core.Terminal.Graphics;
using RetroTerm.Core.Terminal.Input;
using RetroTerm.Core.Terminal.Parsing;
using RetroTerm.Core.Terminal.Profiles;
using RetroTerm.Core.Terminal.Rendering;

namespace RetroTerm.Core.Terminal.Emulators;

/// <summary>
/// Base class for all terminal emulators providing common functionality:
/// - Buffer management
/// - Cursor control
/// - Escape sequence processing
/// - Character attributes handling
/// - Scrolling and regions
/// - Basic VT52/VT100 support
///
/// Derived classes can override specific behaviors for terminal-specific features.
/// </summary>
public abstract class TerminalEmulatorBase : ITerminalEmulator
{
    public readonly TerminalBuffer Buffer;
    public readonly Cursor Cursor;
    protected readonly EscapeSequenceParser Parser;

    /// <summary>
    /// Gets the parser instance (for testing)
    /// </summary>
    public EscapeSequenceParser GetParser() => Parser;

    /// <summary>
    /// Which terminal this is: its name, its Device Attributes replies, the DEC private modes it
    /// recognises and what it can do.
    ///
    /// Deliberately abstract rather than defaulted. A default would have meant "everything is a
    /// VT100 unless it says otherwise", which is the exact confusion this is here to end — the
    /// base class implements ECMA-48, it is not itself a terminal. Every emulator must say what
    /// it is.
    /// </summary>
    public abstract TerminalProfile Profile { get; }

    /// <summary>
    /// The modes that change how a keypress should be encoded, as the keyboard mapper wants them.
    ///
    /// This exists because the modes and the mapper had never been connected. DECCKM was stored
    /// here and DECKPAM beside it; the mapper had branches for both; and the UI, which is what
    /// actually called the mapper, built its mode set from a chain of emulator TYPE CHECKS that
    /// only ever produced the TDV flags. Nothing anywhere set TerminalModes.ApplicationCursorKeys,
    /// so the mapper's branch for it was unreachable and arrow keys never switched to their
    /// application form — which vi, less and most full-screen programs turn on and depend upon.
    ///
    /// The emulator owns the state, so the emulator answers the question. That also gets the
    /// type-check chain out of the UI layer, where it was duplicated in two places and had drifted
    /// into being business logic in a view.
    /// </summary>
    public virtual TerminalModes GetActiveModes()
    {
        var modes = TerminalModes.None;

        if (ApplicationCursorKeys) modes |= TerminalModes.ApplicationCursorKeys;
        if (ApplicationKeypad) modes |= TerminalModes.ApplicationKeypad;
        // The DEPARTURE from the ordinary case is what gets reported, never the ordinary case
        // itself - None has to keep meaning "nothing unusual is going on". Reporting it the other
        // way round made every fresh terminal claim a mode nobody had set.
        if (!BackarrowSendsBackspace) modes |= TerminalModes.BackarrowSendsDelete;
        if (!AutoRepeatKeys) modes |= TerminalModes.AutoRepeatDisabled;

        return modes;
    }

    // Current character attributes for new text
    protected CharacterAttributes CurrentAttributes;
    protected TerminalColor CurrentForeground;
    protected TerminalColor CurrentBackground;
    protected byte CurrentCharacterSet;

    // Scroll region (for region-based scrolling like DECSTBM)
    protected int ScrollTop;
    protected int ScrollBottom;

    // Terminal modes
    protected bool ApplicationCursorKeys;
    protected bool ApplicationKeypad;
    protected bool AutoWrapMode;

    /// <summary>
    /// XTREVWRAP (DECSET 45) - whether a backspace off the left edge wraps to the line above.
    /// </summary>
    /// <remarks>
    /// Off by default, and off is the only setting a DEC terminal has. xterm's ctlseqs lists the
    /// mode but says nothing about where the cursor lands, so the rule here comes from xterm's own
    /// behaviour as captured in the reverse-wrap fixture: the LAST COLUMN OF THE PREVIOUS LINE, and
    /// from the top line, round to the BOTTOM one.
    /// </remarks>
    protected bool ReverseWrapMode;

    /// <summary>
    /// DECBKM (private mode 67) - true when the backarrow key sends BS rather than DEL.
    /// </summary>
    /// <remarks>
    /// <para><b>The power-on value here is BS, and that is a deliberate deviation</b></para>
    /// A real VT220 and later powers on with DECBKM RESET, so the backarrow sends DEL. This
    /// emulator powers on with it SET, because every keyboard mapper in it has always sent
    /// <c>0x08</c> for that key and a lot of working setups depend on it. Flipping the default
    /// while adding the mode would have been two changes wearing one coat, and the second one
    /// would have shown up as "my backspace stopped working" rather than as a release note.
    ///
    /// The MODE is faithful: a host that sets 67 gets BS, a host that resets it gets DEL. Only the
    /// value before any host says anything differs, and a host that cares says so.
    /// </remarks>
    protected bool BackarrowSendsBackspace = true;

    /// <summary>
    /// DECARM (private mode 8) - true when a key held down repeats.
    /// </summary>
    /// <remarks>
    /// On by default, which is what a keyboard does and what a real VT100 powers on with.
    ///
    /// A host resets this when repeating would be actively harmful: a menu where a leaned-on arrow
    /// key would run off the end of the list, or a form where a held key would fill a field. It is
    /// the only mode in this class that is enforced by the INPUT path rather than by what a key
    /// encodes to, because the difference it makes is whether a keypress happens at all.
    /// </remarks>
    protected bool AutoRepeatKeys = true;

    /// <summary>
    /// DECNRCM (private mode 42) - whether national replacement character sets are honoured.
    /// </summary>
    /// <remarks>
    /// <para><b>What it actually changes here</b></para>
    /// One designator, because only one national set has a glyph table in this program:
    /// <c>ESC ( A</c> is British NRCS when this is set and ISO Latin-1 - which for everything this
    /// terminal can draw behaves as ASCII - when it is not. The other eleven national sets a VT300
    /// answers to are not built, and no profile's DA reply claims capability 9, so no host is being
    /// told otherwise.
    /// <para><b>The power-on value is a deliberate deviation, the same one DECBKM makes</b></para>
    /// On by default, so <c>ESC ( A</c> keeps meaning British as it always has in this program. A
    /// real VT220 powers on according to its set-up menu and xterm defaults it off. Flipping it here
    /// while adding the mode would have been two changes wearing one coat, and the second one would
    /// have arrived as "the pound sign stopped working".
    /// </remarks>
    protected bool NationalReplacementSets = true;

    /// <summary>
    /// DECSCLM (private mode 4) - whether a scroll slides or jumps.
    /// </summary>
    /// <remarks>
    /// <para><b>What smooth scrolling is, and what it is NOT</b></para>
    /// The buffer always scrolls instantly. Only the PICTURE of it is animated, by drawing the whole
    /// screen shifted down a few pixels and letting it slide up over the next several frames. The
    /// data model has no idea any of this is happening, which is the only way this could be added
    /// without every existing test about scrolling becoming timing-dependent.
    /// <para><b>Shared with the TDV's own smooth scroll on purpose</b></para>
    /// A TDV2200 has this as ND mode 67 and it used to be a separate field on
    /// <c>TDVEmulatorBase</c>. Two fields for one behaviour is the trap this codebase keeps naming:
    /// a fix lands on one and the other stays broken.
    /// </remarks>
    public bool SmoothScrollMode { get; protected set; }

    /// <summary>
    /// Raised when a whole-screen scroll happened while smooth scrolling was on.
    /// </summary>
    /// <remarks>
    /// Says only THAT a line scrolled, never how far to move anything. Pixels are the view's
    /// business - the emulator does not know how tall a character cell is on this monitor, at this
    /// zoom, and should not learn.
    ///
    /// Raised on the session pump, so a listener must marshal to its own thread.
    /// </remarks>
    public event Action? SmoothScrollLineScrolled;

    /// <summary>
    /// How many scrolled lines the picture still owes the buffer.
    /// </summary>
    private int _smoothScrollBacklog;

    /// <summary>
    /// How many lines the displayed picture is currently behind the buffer.
    /// </summary>
    /// <remarks>
    /// <para><b>Why a queue and not a drop</b></para>
    /// The first version animated one line and ignored any that arrived while it was busy, so a host
    /// faster than the animation got a mixture of sliding and jumping lines - which looks worse than
    /// jumping alone. It does not have to be that way: the lines are still in scrollback, so the
    /// picture can simply run behind and walk forward one slide at a time until it catches up.
    /// <para><b>Counted here, on the pump, and not in the view</b></para>
    /// The view learns about a scroll through an event it has to marshal to its own thread, but the
    /// frame is captured at the end of the same ProcessData that scrolled. If the count lived in the
    /// view, every frame between the scroll and the marshalled event would be captured at the wrong
    /// lag and the picture would lurch forward a line before starting to climb.
    /// </remarks>
    public int SmoothScrollBacklog => System.Threading.Volatile.Read(ref _smoothScrollBacklog);

    /// <summary>
    /// The furthest behind the picture is ever allowed to fall, in lines.
    /// </summary>
    /// <remarks>
    /// <para><b>Two separate limits, and the smaller one wins</b></para>
    /// A screenful, because a picture more than one screen behind is showing something the reader
    /// has no way to relate to what is happening; at roughly four lines a second that is already
    /// about six seconds of lag. Something has to give when a host dumps a file, and falling further
    /// and further behind for minutes is not a better answer than jumping.
    ///
    /// And never further back than there is history to read: the lagged view is resolved through
    /// scrollback, so asking for a line that was never kept draws a blank band. The plus one is what
    /// lets a screen with NO history animate at all - at a lag of one the viewport helper falls
    /// through to the live grid shifted down a row, which is the approximation this had before the
    /// queue existed. It is what the alternate screen gets, where nothing is ever kept, and it is
    /// why a pager still scrolls smoothly instead of going blank.
    /// </remarks>
    public int MaxSmoothScrollBacklog
    {
        get
        {
            int history = Buffer.ScrollbackLineCount + 1;
            return history < Height ? history : Height;
        }
    }

    /// <summary>
    /// Takes one line off the backlog, for a view about to start sliding it into place.
    /// </summary>
    /// <returns>
    /// True when a line was claimed, false when the picture is already up to date.
    /// </returns>
    /// <remarks>
    /// <para><b>Claimed at the START of a slide, never at the end</b></para>
    /// A slide draws the frame shifted DOWN by a whole cell and lets it climb to nothing, so the
    /// moment it begins it must already be showing the newer content - the shift is what makes that
    /// look like the older one. Decrementing at the end instead would leave the picture a line short
    /// for the whole slide and then snap forward as it finished, which is the jump this exists to
    /// remove.
    /// </remarks>
    public bool TryBeginSmoothScrollLine()
    {
        // Interlocked because the pump adds to this while the view takes from it.
        int current;
        do
        {
            current = System.Threading.Volatile.Read(ref _smoothScrollBacklog);
            if (current <= 0) return false;
        }
        while (System.Threading.Interlocked.CompareExchange(
            ref _smoothScrollBacklog, current - 1, current) != current);

        return true;
    }

    /// <summary>
    /// The VT100 keyboard lamps, one bit per lamp: bit 0 is L1, bit 3 is L4.
    /// </summary>
    /// <remarks>
    /// A VT100 had four numbered lamps above the keypad that a host could drive with DECLL. This
    /// emulator has no lamps, so nothing lights up - but the state is kept, because a host that
    /// sets them is entitled to have been heard, and because a sequence that is accepted and then
    /// forgotten is worse than one honestly reported as unknown.
    /// </remarks>
    public int KeyboardLeds { get; private set; }

    /// <summary>
    /// DECLL - turns the keyboard lamps on and off.
    /// </summary>
    /// <param name="parameters">
    /// The lamp numbers. Empty, or a single zero, means all of them off.
    /// </param>
    /// <remarks>
    /// From the VT100 User Guide, chapter 3: parameter 0 (or none) clears every lamp; 1 to 4 light
    /// L1 to L4; 21 to 24 clear them one at a time. Anything else is ignored rather than guessed at.
    /// </remarks>
    protected virtual void HandleLoadLeds(ReadOnlySpan<int> parameters)
    {
        if (parameters.Length == 0)
        {
            KeyboardLeds = 0;
            return;
        }

        for (int i = 0; i < parameters.Length; i++)
        {
            int lamp = parameters[i];

            if (lamp == 0)
            {
                KeyboardLeds = 0;
            }
            else if (lamp >= 1 && lamp <= 4)
            {
                KeyboardLeds |= 1 << (lamp - 1);
            }
            else if (lamp >= 21 && lamp <= 24)
            {
                KeyboardLeds &= ~(1 << (lamp - 21));
            }
        }
    }

    protected bool InsertMode;
    protected bool NewLineMode; // LF+CR together
    protected bool ReverseVideoMode;
    protected bool OriginMode; // Cursor addressing relative to scroll region

    // Character sets (G0, G1, G2, G3)
    protected readonly byte[] CharacterSets = new byte[4];
    protected int ActiveCharacterSet;

    // Tab stops, one flag per column. A real terminal keeps a settable stop per column (HTS/TBC);
    // hardcoding "every 8th column" meant HTS did nothing and any host that moved its stops - which
    // full-screen forms do - had its columns land in the wrong place.
    private bool[] _tabStops = Array.Empty<bool>();

    /// <summary>
    /// The full DECSC state. The standard saves more than the position: DECRC must also put back
    /// the graphic rendition, the character sets and origin/wrap mode. Saving only row/column/style
    /// meant a host that did "DECSC, change colour, DECRC" kept the changed colour.
    /// </summary>
    private struct SavedTerminalState
    {
        public bool IsValid;
        public CharacterAttributes Attributes;
        public TerminalColor Foreground;
        public TerminalColor Background;
        public byte CharacterSet;
        public int ActiveCharacterSet;
        public byte G0, G1, G2, G3;
        public bool OriginMode;
        public bool AutoWrapMode;

        /// <summary>
        /// DECSCA - whether what is typed next is protected from a selective erase.
        /// </summary>
        /// <remarks>
        /// On DEC's own list of what DECSC saves, and it was missing. A program that marks a form's
        /// labels protected, saves, does something and restores got back whatever the flag happened
        /// to be - so the next characters it typed were protected or not by accident, and a
        /// selective erase then kept or destroyed the wrong ones.
        /// </remarks>
        public bool ProtectedMode;
    }

    private SavedTerminalState _savedState;

    /// <summary>
    /// The DECSC slot belonging to the screen that is NOT currently shown.
    /// </summary>
    /// <remarks>
    /// Each screen owns its own saved cursor. A program that saves on the main screen, switches to
    /// the alternate one, saves again there and then restores on each screen in turn must get its
    /// own position back both times - which is what the xterm.js fixture t0092-alt_screen_DECSC
    /// captured from a real xterm. With a single shared slot the alternate screen's save destroyed
    /// the main screen's, so both restores landed on the same spot and the text that should have
    /// gone back to the top of the screen was written over the bottom of it.
    ///
    /// Held as one spare rather than as an array indexed by screen: there are exactly two screens,
    /// and the pair is exchanged whenever the buffer actually changes.
    /// </remarks>
    private SavedTerminalState _savedStateOfTheOtherScreen;

    /// <summary>
    /// The cursor half of that same slot.
    /// </summary>
    private (int Row, int Col, CursorStyle Style) _savedCursorOfTheOtherScreen;

    /// <summary>
    /// Hands the current screen's DECSC slot to the other screen and takes back its own.
    /// </summary>
    /// <remarks>
    /// Called only when the buffer REALLY changed. A host that sends the same switch twice must not
    /// exchange the slots twice, which would hand each screen the other one's saved cursor.
    /// </remarks>
    private void ExchangeSavedStateBetweenScreens()
    {
        SavedTerminalState state = _savedState;
        _savedState = _savedStateOfTheOtherScreen;
        _savedStateOfTheOtherScreen = state;

        (int Row, int Col, CursorStyle Style) cursor = Cursor.SavedPosition;
        Cursor.SavedPosition = _savedCursorOfTheOtherScreen;
        _savedCursorOfTheOtherScreen = cursor;
    }

    // ── Frame handoff to the renderer (see ScreenFrame for why) ───────────────────────────────
    //
    // Three frames, not two. The renderer holds the reference it read for the whole of its draw,
    // so the pump must not write into that one. With two, the single spare IS the one the
    // renderer may still be reading; with three the pump has to get two whole publishes ahead of
    // a draw before it could catch up with the reader. Frames are reused rather than allocated,
    // so a steady stream of output does not hand the GC a screen-sized array per update.
    private readonly ScreenFrame[] _framePool = { new ScreenFrame(), new ScreenFrame(), new ScreenFrame() };
    private int _nextFrameIndex;
    private ScreenFrame? _publishedFrame;

    /// <summary>
    /// Which part of the scrollback the next published frame should show. Written by the UI,
    /// read by the pump when it captures. An int assignment is atomic, and being one frame behind
    /// on this value is harmless - the next publish corrects it.
    /// </summary>
    public int ViewScrollOffset;

    /// <summary>
    /// The most recently published frame, or null if nothing has been published yet.
    ///
    /// This is the ONLY thing the renderer may read. Reading the buffer or the cursor from the UI
    /// thread is the race this exists to remove.
    /// </summary>
    public ScreenFrame? LatestFrame => System.Threading.Volatile.Read(ref _publishedFrame);

    /// <summary>
    /// Raised when something outside the pump needs a fresh frame - the user scrolling back on an
    /// idle terminal, for instance, where no incoming data would otherwise trigger one.
    ///
    /// TerminalSession subscribes and routes it onto the pump thread. When nothing subscribes
    /// (a bare emulator in a test), RequestFrame captures inline, which is safe precisely because
    /// there is no pump running to race with.
    /// </summary>
    public event Action? FrameRequested;

    /// <summary>
    /// Captures the current screen into a frame and publishes it. MUST run on the pump thread.
    /// Called at the end of every ProcessData, and after resize/reset.
    /// </summary>
    public void PublishFrame()
    {
        // Never write into the frame the renderer is most likely holding.
        var published = System.Threading.Volatile.Read(ref _publishedFrame);
        ScreenFrame target;
        do
        {
            target = _framePool[_nextFrameIndex];
            _nextFrameIndex = (_nextFrameIndex + 1) % _framePool.Length;
        }
        while (ReferenceEquals(target, published));

        target.CaptureFrom(Buffer, Cursor, ViewScrollOffset, ReverseVideoMode,
            ImageTextForeground, ImageTextBackground,
            captureRowAboveTop: SmoothScrollMode,
            displayLagLines: SmoothScrollMode ? SmoothScrollBacklog : 0);
        System.Threading.Volatile.Write(ref _publishedFrame, target);
    }

    /// <summary>
    /// Asks for a fresh frame from outside the pump. Safe to call from the UI thread.
    /// </summary>
    public void RequestFrame()
    {
        var handler = FrameRequested;
        if (handler != null)
        {
            handler();
        }
        else
        {
            // No session routing this: nothing else is touching the buffer, so capture directly.
            PublishFrame();
        }
    }

    // Font renderer (terminal-specific rendering strategy)
    private IFontRenderer? _fontRenderer;

    /// <summary>
    /// Gets the font renderer for this terminal type.
    /// Each terminal emulator provides its own font rendering strategy.
    /// Implementation is provided by the Desktop project via extension methods.
    /// </summary>
    public IFontRenderer GetFontRenderer()
    {
        if (_fontRenderer == null)
        {
            // This will be provided by extension methods in Desktop project
            throw new NotImplementedException("GetFontRenderer must be implemented via extension methods in Desktop project");
        }
        return _fontRenderer;
    }

    /// <summary>
    /// Sets the font renderer for this terminal.
    /// Called by Desktop project extension methods.
    /// </summary>
    internal void SetFontRenderer(IFontRenderer renderer)
    {
        _fontRenderer = renderer;
    }

    /// <summary>
    /// Gets the terminal width in columns
    /// </summary>
    public int Width => Buffer.Width;

    /// <summary>
    /// Gets the terminal height in rows
    /// </summary>
    public int Height => Buffer.Height;

    /// <summary>
    /// Gets the terminal buffer for rendering
    /// </summary>
    public TerminalBuffer GetBuffer() => Buffer;

    /// <summary>
    /// Gets the cursor for rendering
    /// </summary>
    public Cursor GetCursor() => Cursor;

    /// <summary>
    /// Gets the terminal title
    /// </summary>
    public string Title { get; protected set; } = "RetroTerm";

    /// <summary>
    /// The terminal's graphics planes, or null when this terminal has none.
    ///
    /// Null for every text-only profile, which is most of them: a VT100 has no graphics plane and
    /// paying for one would be silly. The renderer checks for null and skips the whole blit.
    ///
    /// Composited on the pump thread and read on the UI thread, which is safe because
    /// <see cref="Graphics.GraphicsCompositor.Output"/> is double buffered - the same rule
    /// ScreenFrame follows and for the same reason.
    /// </summary>
    public Graphics.GraphicsCompositor? Graphics { get; protected set; }

    /// <summary>
    /// Text foreground a Sixel image asked for, or null when no image has asked.
    /// </summary>
    /// <remarks>
    /// Only set on a terminal whose profile claims
    /// <see cref="Profiles.TerminalFeatures.ImageColoursSetTextColours"/>. It outranks the theme's
    /// own default while it is set, the way a real VT340's does, and is cleared by a hard reset -
    /// nothing smaller, because on the hardware it is a setting the picture changed rather than a
    /// mode with a sequence to turn it off.
    /// </remarks>
    public Graphics.GraphicsColor? ImageTextForeground { get; protected set; }

    /// <summary>
    /// Text background a Sixel image asked for, or null when no image has asked.
    /// </summary>
    /// <remarks>
    /// See <see cref="ImageTextForeground"/>. This is the one that shows: it repaints the whole
    /// screen, which is why the hardware capture of <c>cat-vt340.six</c> is green all over.
    /// </remarks>
    public Graphics.GraphicsColor? ImageTextBackground { get; protected set; }

    /// <summary>
    /// Event raised when the terminal needs to be repainted
    /// </summary>
    public event Action? Invalidated;

    /// <summary>
    /// How deeply nested we are inside a batch of changes. Above zero, OnInvalidated only records
    /// that the screen changed; the publish and the notification happen once, when the outermost
    /// batch ends.
    /// </summary>
    private int _invalidateBatchDepth;

    /// <summary>
    /// Whether anything asked for a repaint while a batch was open.
    /// </summary>
    private bool _invalidatePending;

    /// <summary>
    /// Triggers the Invalidated event (for use by derived classes)
    /// </summary>
    protected void OnInvalidated()
    {
        // Inside a batch, hold it. ONE chunk of host data routinely carries dozens of escape
        // sequences, and 29 places in this codebase call OnInvalidated - so a host repainting a
        // form used to copy the whole screen and post a repaint once PER SEQUENCE rather than
        // once per chunk. See BeginBatch.
        if (_invalidateBatchDepth > 0)
        {
            _invalidatePending = true;
            return;
        }

        // Publish BEFORE telling anyone the screen changed. The renderer only reads published
        // frames, so a notification that arrives ahead of its frame would draw the previous one.
        // Everything that signals a change goes through here for exactly that reason.
        PublishFrame();
        Invalidated?.Invoke();
    }

    /// <summary>
    /// Opens a batch: changes made until the matching <see cref="EndBatch"/> publish ONE frame and
    /// raise ONE Invalidated between them, instead of one per change.
    ///
    /// This is a cost question, not a correctness one. <see cref="PublishFrame"/> copies the whole
    /// screen - 1,920 cells for an 80x24 - and every publish also posts a repaint to the UI thread.
    /// A single read from the host commonly carries dozens of escape sequences, and each one that
    /// touched the screen paid for both. Nothing outside can observe an intermediate state anyway:
    /// the frame is only read by the renderer, on another thread, whenever it happens to draw.
    ///
    /// Nests, so a batch inside a batch is safe. Always pair with EndBatch in a finally.
    /// </summary>
    protected void BeginBatch() => _invalidateBatchDepth++;

    /// <summary>
    /// Closes a batch. When the outermost one closes and anything asked for a repaint inside it,
    /// the frame is published and Invalidated is raised exactly once.
    /// </summary>
    protected void EndBatch()
    {
        if (_invalidateBatchDepth > 0)
        {
            _invalidateBatchDepth--;
        }

        if (_invalidateBatchDepth == 0 && _invalidatePending)
        {
            _invalidatePending = false;
            PublishFrame();
            Invalidated?.Invoke();
        }
    }

    /// <summary>
    /// Event raised when the terminal title changes
    /// </summary>
    public event Action<string>? TitleChanged;

    /// <summary>
    /// Event raised when the terminal bell is triggered
    /// </summary>
    public event Action? Bell;

    /// <summary>
    /// Event raised when data needs to be sent back to the host (keyboard input, responses)
    /// </summary>
    public event Action<byte[]>? DataToSend;

    protected TerminalEmulatorBase(int width, int height, int maxScrollback = 10000)
    {
        Buffer = new TerminalBuffer(width, height, maxScrollback);
        Cursor = new Cursor(height, width);
        Parser = new EscapeSequenceParser();

        // Wire up parser events
        Parser.OnCharacter += HandleCharacter;
        Parser.OnExecute += HandleExecute;
        Parser.OnEscapeDispatch += HandleEscapeSequence;
        Parser.OnCsiDispatch += (p) =>
        {
            // Diagnostic only, and Trace level: the Protocol Monitor decodes the wire stream
            // independently, so this line exists purely to confirm that the EMULATOR's parser
            // actually dispatched the sequence (as opposed to it merely being on the wire).
            if (ApplicationLogger.IsEnabled(LogCategory.Parser, LogLevel.Trace))
            {
                var decoded = EscapeSequenceDecoder.DecodeCsi(
                    p.PrivateMarker, p.Intermediates, p.Parameters, p.FinalByte);

                ApplicationLogger.Log(LogCategory.Parser, LogLevel.Trace, GetType().Name,
                    $"CSI dispatch {decoded}");
            }

            HandleCsiSequence(p);
        };
        Parser.OnOscDispatch += HandleOscSequence;

        // DCS. These were declared on the parser but never subscribed, so HandleDCSSequence
        // and every DCS handler below it was unreachable code. Sixel, ReGIS and DECUDK all
        // arrive this way, so the three events are wired here in the base and overridden per
        // terminal.
        Parser.OnDcsHook += HandleDCSSequence;
        Parser.OnDcsPut += HandleDCSData;
        Parser.OnDcsUnhook += HandleDCSEnd;

        // Initialize defaults
        ResetToInitialState();
    }

    /// <summary>
    /// Whether this terminal has a graphics side that gets first refusal on incoming bytes.
    /// </summary>
    /// <remarks>
    /// False by default, and that default is what keeps the ordinary path fast: a text terminal
    /// hands the parser the whole chunk in one call, while a terminal with graphics has to look at
    /// each byte to see whether it is a coordinate. Only a terminal that really draws pays for it.
    /// </remarks>
    protected virtual bool HasGraphicsInput => TektronixMode;

    /// <summary>
    /// Offers a byte to this terminal's graphics side before it is treated as text.
    /// </summary>
    /// <remarks>
    /// Declared here rather than on one family of terminals because two now have graphics - the
    /// TDV2200 and the Tektronix 4014 - and a rule that lives at the call site gets fixed on one
    /// and stays broken on the other. Use <see cref="TryConsumeAsGraphics"/> to call it; that is
    /// where the "only in Ground" rule lives.
    /// </remarks>
    /// <param name="b">
    /// The byte arriving from the host.
    /// </param>
    /// <returns>
    /// True when the byte was part of a drawing and must not be printed.
    /// </returns>
    internal virtual bool ConsumeGraphicsByte(byte b)
    {
        // DECTEK. A VT330/VT340/VT240 asked to become a 4014 draws with the SAME plotter the
        // Tektronix terminal uses - not a second copy of it. Duplicating the vector decoder would
        // be the one-behaviour-two-places trap, and this one has bitten the project before.
        if (!TektronixMode || _tektronixPlotter == null) return false;

        bool wasDrawing = _tektronixPlotter.InGraphicsMode;

        if (!_tektronixPlotter.ConsumeVectorByte(b)) return false;

        // LEAVING GRAPH MODE PUTS THE TEXT CURSOR AT THE LAST PLOTTED POINT, exactly as on the
        // real 4014 half of this program. It is how a host labels a plot: move the beam to where
        // the text belongs, drop to alpha, print. Getting this wrong is what once put every gnuplot
        // axis label in a diagonal cascade across the picture.
        if (wasDrawing && !_tektronixPlotter.InGraphicsMode)
        {
            PlaceCursorAtLastTektronixVector();
        }

        // Only repaint when a pixel actually moved. A mode switch changes nothing, and compositing
        // per byte on a stream that arrives in thousands would cost a frame each.
        if (_tektronixPlotter.InGraphicsMode)
        {
            Graphics?.Composite();
            OnInvalidated();
        }

        return true;
    }

    // ─────────────────────────────────────────────────────────────
    // DECTEK - private mode 38, a VT pretending to be a Tektronix 4014
    //
    // Added 25 August 2026. Before it, a VT340 sent ESC[?38h and then a plot printed the coordinate
    // bytes as text across the screen, because nothing listened for the sequence and - worse -
    // nothing counted it either. See docs\manual-tests\FINDINGS-2026-08-20.md.
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// The plotter used while DECTEK is in force, built the first time it is needed.
    /// </summary>
    /// <remarks>
    /// Null until a host actually asks for Tektronix mode. A terminal that is never asked pays
    /// nothing for the feature - no plane, no surface, no per-byte check beyond one bool.
    /// </remarks>
    private Graphics.TektronixPlotter? _tektronixPlotter;

    /// <summary>
    /// The name the Tektronix plane is registered under.
    /// </summary>
    private const string TektronixPlaneId = "dectek";

    /// <summary>
    /// The 4014's own coordinate space, which is what the byte stream is written in.
    /// </summary>
    /// <remarks>
    /// 1024 by 780 is the addressable area of a 4010/4014 screen, and the viewport below is what
    /// maps it onto whatever size this terminal's graphics plane happens to be. The stream does not
    /// know or care how big the window is.
    /// </remarks>
    private const int TektronixLogicalWidth = 1024;

    /// <summary>
    /// The height half of <see cref="TektronixLogicalWidth"/>.
    /// </summary>
    private const int TektronixLogicalHeight = 780;

    /// <summary>
    /// True while DECTEK is in force and the byte stream is a drawing.
    /// </summary>
    public bool TektronixMode { get; private set; }

    /// <summary>
    /// Turns Tektronix mode on or off, building the plane the first time it is needed.
    /// </summary>
    /// <param name="enable">
    /// True to enter, false to leave.
    /// </param>
    private void SetTektronixMode(bool enable)
    {
        if (enable == TektronixMode) return;

        TektronixMode = enable;

        if (!enable)
        {
            // The DRAWING STAYS. Leaving 4014 mode on the real thing does not wipe the storage
            // tube - only an explicit erase does - and a host that plots, drops to alpha and writes
            // a caption expects to still see its plot underneath.
            _tektronixPlotter?.Vectors.Reset();
            OnInvalidated();
            return;
        }

        var compositor = Graphics;
        if (compositor == null)
        {
            compositor = new Graphics.GraphicsCompositor(GraphicsPlaneWidth, GraphicsPlaneHeight);
            Graphics = compositor;
        }

        var plane = compositor.FindPlane(TektronixPlaneId) ?? compositor.AddPlane(TektronixPlaneId);
        plane.IsVisible = true;

        if (_tektronixPlotter == null)
        {
            var viewport = new Graphics.GraphicsViewport(
                TektronixLogicalWidth, TektronixLogicalHeight,
                GraphicsPlaneWidth, GraphicsPlaneHeight);

            _tektronixPlotter = new Graphics.TektronixPlotter(plane, viewport);

            // One beam, one colour. The plot follows the theme for the same reason the text does -
            // no single-phosphor machine can show amber text beside a green graph, and this program
            // once did exactly that because the draw colour was a constant.
            var (r, g, b) = DisplayForeground;
            _tektronixPlotter.DrawColour = new Graphics.GraphicsColor(r, g, b);
        }

        OnInvalidated();
    }

    /// <summary>
    /// Puts the text cursor where the beam stopped, on the way out of graph mode.
    /// </summary>
    /// <remarks>
    /// Tektronix Y runs UPWARDS from the bottom of the screen and text rows run downwards from the
    /// top, so the row is the flipped one. Writing this the obvious way puts every label on the
    /// wrong side of the picture.
    /// </remarks>
    private void PlaceCursorAtLastTektronixVector()
    {
        if (_tektronixPlotter == null) return;

        var vectors = _tektronixPlotter.Vectors;

        int column = vectors.LastX * Width / TektronixLogicalWidth;
        int row = (TektronixLogicalHeight - 1 - vectors.LastY) * Height / TektronixLogicalHeight;

        if (column < 0) column = 0;
        if (column > Width - 1) column = Width - 1;
        if (row < 0) row = 0;
        if (row > Height - 1) row = Height - 1;

        Cursor.MoveTo(row, column);
    }

    /// <summary>
    /// Offers a byte to the graphics side, but only when no escape sequence is in flight.
    /// </summary>
    /// <remarks>
    /// The Ground check is the whole point. A Tektronix coordinate byte and an escape sequence's
    /// parameter byte look identical - both are ordinary printable bytes - so a sequence already
    /// under way wins, or it can never finish. This is one method rather than a condition repeated
    /// at each call site: the TDV input path and the base data path both come through here.
    /// </remarks>
    /// <param name="b">
    /// The byte arriving from the host.
    /// </param>
    /// <returns>
    /// True when the graphics side took the byte.
    /// </returns>
    internal bool TryConsumeAsGraphics(byte b)
        => Parser.State == ParserState.Ground && ConsumeGraphicsByte(b);

    /// <summary>
    /// Feeds a chunk byte by byte, giving the graphics side first refusal on each.
    /// </summary>
    /// <param name="data">
    /// The bytes arriving from the host.
    /// </param>
    private int ProcessDataWithGraphics(ReadOnlySpan<byte> data)
    {
        for (int i = 0; i < data.Length; i++)
        {
            if (TryConsumeAsGraphics(data[i])) continue;

            // One byte at a time, but through the same span entry point - the parser carries its
            // state across calls, so this is only a slower way in, not a different one.
            Parser.ProcessBytes(data.Slice(i, 1));

            // A handler may have changed where the rest of the chunk belongs - see
            // EscapeSequenceParser.StopRequested. Checked after the call because the one-byte
            // slice above clears the flag on its way out.
            if (_parserStoppedEarly)
            {
                _parserStoppedEarly = false;
                return i + 1;
            }
        }

        return data.Length;
    }

    /// <summary>
    /// True when a sequence handler asked the parser to stop and hand back the rest of the chunk.
    /// </summary>
    private bool _parserStoppedEarly;

    /// <summary>
    /// Host bytes held back while ReGIS one-shot graphics input mode is running.
    /// </summary>
    /// <remarks>
    /// "The terminal buffers any data received from the application in this mode." Grown by doubling
    /// rather than kept in a list: this is a cold path that only runs while an operator is aiming a
    /// crosshair, and a plain array replays through <see cref="ProcessData"/> with no copy per byte.
    /// </remarks>
    private byte[]? _suspendedHostData;

    /// <summary>
    /// How much of <see cref="_suspendedHostData"/> is in use.
    /// </summary>
    private int _suspendedHostLength;

    /// <summary>
    /// True while ReGIS one-shot graphics input mode is holding the host's data back.
    /// </summary>
    public bool IsRegisInputSuspended
        => _regisDecoder != null && _regisDecoder.IsHostDataSuspended;

    /// <summary>
    /// Which ReGIS graphics input mode is running, if any.
    /// </summary>
    public RetroTerm.Core.Terminal.Graphics.RegisGraphicsInputMode RegisInputMode
        => _regisDecoder?.InputMode ?? RetroTerm.Core.Terminal.Graphics.RegisGraphicsInputMode.Off;

    /// <summary>
    /// The mode the last <see cref="RegisInputModeChanged"/> announced.
    /// </summary>
    private RetroTerm.Core.Terminal.Graphics.RegisGraphicsInputMode _lastRaisedRegisInputMode;

    /// <summary>
    /// Raised when ReGIS graphics input mode starts or stops.
    /// </summary>
    /// <remarks>
    /// Lives on the emulator rather than on the view because the mode is entered by the HOST - a
    /// keypress only ever ends it. A view that watched its own key handling would never notice the
    /// mode starting, which is exactly the moment a paused session most needs explaining.
    /// </remarks>
    public event Action<RetroTerm.Core.Terminal.Graphics.RegisGraphicsInputMode>? RegisInputModeChanged;

    /// <summary>
    /// Holds host bytes back until graphics input mode ends.
    /// </summary>
    /// <param name="data">
    /// The bytes that arrived while the terminal was suspended.
    /// </param>
    private void BufferSuspendedHostData(ReadOnlySpan<byte> data)
    {
        if (data.Length == 0) return;

        int needed = _suspendedHostLength + data.Length;
        if (_suspendedHostData == null)
        {
            _suspendedHostData = new byte[needed < 256 ? 256 : needed];
        }
        else if (_suspendedHostData.Length < needed)
        {
            int size = _suspendedHostData.Length;
            while (size < needed) size *= 2;
            Array.Resize(ref _suspendedHostData, size);
        }

        data.CopyTo(new Span<byte>(_suspendedHostData, _suspendedHostLength, data.Length));
        _suspendedHostLength = needed;
    }

    /// <summary>
    /// Replays everything held back while the terminal was suspended.
    /// </summary>
    /// <remarks>
    /// The length is cleared BEFORE the replay, not after. The buffered bytes may contain another
    /// <c>R(I0)</c>, which suspends the terminal again part-way through - and that second suspension
    /// must start from an empty buffer, or the bytes it holds would be replayed twice.
    /// </remarks>
    private void DrainSuspendedHostData()
    {
        if (_suspendedHostData == null || _suspendedHostLength == 0) return;

        byte[] held = _suspendedHostData;
        int length = _suspendedHostLength;

        _suspendedHostData = null;
        _suspendedHostLength = 0;

        ProcessData(new ReadOnlySpan<byte>(held, 0, length));
    }

    /// <summary>
    /// Moves the ReGIS graphics input cursor, as an arrow key does.
    /// </summary>
    /// <param name="dx">
    /// Pixels to move right; negative moves left.
    /// </param>
    /// <param name="dy">
    /// Pixels to move down; negative moves up.
    /// </param>
    /// <returns>
    /// True when the keypress belonged to graphics input and must not also reach the host.
    /// </returns>
    /// <remarks>
    /// Answering false is what lets the arrow keys keep their ordinary job. Chapter 15 is explicit
    /// that they only steer the cursor in one-shot mode: "If you press an arrow key in multiple mode,
    /// the terminal sends that key's escape sequence to the host."
    /// </remarks>
    public bool MoveRegisInputCursor(int dx, int dy)
    {
        if (_regisDecoder == null) return false;
        if (_regisDecoder.InputMode != RetroTerm.Core.Terminal.Graphics.RegisGraphicsInputMode.OneShot) return false;

        _regisDecoder.MoveInputCursor(dx, dy);
        RedrawRegisInputCursor();

        // Nothing else is going to ask for one. An arrow keypress changes no text and receives no
        // host data, so without this the crosshair moves on the plane and the screen keeps showing
        // where it used to be. The same goes for the two methods below, which END the mode.
        OnInvalidated();
        return true;
    }

    /// <summary>
    /// Puts the graphics input cursor at an absolute point in the ReGIS screen.
    /// </summary>
    /// <param name="x">
    /// Where to put it across, in ReGIS screen coordinates.
    /// </param>
    /// <param name="y">
    /// Where to put it down, in ReGIS screen coordinates.
    /// </param>
    /// <returns>
    /// True when graphics input took the placement, so nothing else should act on the gesture.
    /// </returns>
    /// <remarks>
    /// The pointing half of graphics input, added 31 August 2026. A click places the cursor and the
    /// arrow keys still refine it one or ten pixels at a time - the plane is stretched into the text
    /// area, so a click can only ever round to the nearest ReGIS pixel and the last few belong to the
    /// keys. See <see cref="Graphics.RegisDecoder.SetInputCursor"/> for why this clamps where a move
    /// wraps, and for what is ours rather than the manual's.
    /// </remarks>
    public bool SetRegisInputCursor(int x, int y)
    {
        if (_regisDecoder == null) return false;
        if (_regisDecoder.InputMode != RetroTerm.Core.Terminal.Graphics.RegisGraphicsInputMode.OneShot) return false;

        _regisDecoder.SetInputCursor(x, y);
        RedrawRegisInputCursor();
        OnInvalidated();
        return true;
    }

    /// <summary>
    /// Answers a pending position request with a keystroke, and lets the host's data flow again.
    /// </summary>
    /// <param name="keystroke">
    /// What the key sends to the host, which the report carries ahead of the coordinates.
    /// </param>
    /// <returns>
    /// True when a report went out.
    /// </returns>
    public bool SendRegisInputReport(string keystroke)
    {
        if (_regisDecoder == null) return false;
        if (!_regisDecoder.SendInputReport(keystroke)) return false;

        SendRegisReports();
        RedrawRegisInputCursor();
        OnInvalidated();
        DrainSuspendedHostData();
        return true;
    }

    /// <summary>
    /// Leaves ReGIS graphics input mode without answering, and lets the host's data flow again.
    /// </summary>
    /// <returns>
    /// True when a mode was actually running and has now been left.
    /// </returns>
    /// <remarks>
    /// Ours rather than the manual's - see <c>RegisDecoder.CancelInputMode</c> for why a real VT340
    /// has no equivalent and why this one needs it.
    /// </remarks>
    public bool CancelRegisInput()
    {
        if (_regisDecoder == null) return false;
        if (_regisDecoder.InputMode == RetroTerm.Core.Terminal.Graphics.RegisGraphicsInputMode.Off) return false;

        _regisDecoder.CancelInputMode();
        RedrawRegisInputCursor();
        OnInvalidated();
        DrainSuspendedHostData();
        return true;
    }

    /// <summary>
    /// Tells the parser whether a byte at 0xA0 or above starts a UTF-8 sequence, from this
    /// terminal's own transport encoding.
    ///
    /// Called from every data entry point rather than from the constructor. Two reasons: Profile is
    /// virtual, and reading a derived member from a base constructor is the ordering trap that
    /// works right up until someone builds a profile from instance state — and the TDV emulators
    /// override ProcessData to bypass this class entirely, so a one-time hook in one path would
    /// have left them on the wrong setting. (It did, in the first draft of this change.)
    ///
    /// One bool store per chunk of data, not per byte.
    /// </summary>
    protected void ApplyTransportEncodingToParser()
    {
        Parser.DecodeUtf8 = Profile.TransportEncoding == Profiles.TransportEncoding.Utf8;

        // Recorded here because this method already runs on EVERY data entry point - that is why it
        // lives here rather than in a constructor, and the TDV emulators that bypass this class
        // still come through it. UNHANDLED needs the difference between "nothing arrived" and
        // "everything arrived and was handled": both report no counts, and only one of them means
        // the session was worth running.
        _hasReceivedData = true;
    }

    /// <summary>
    /// True once this terminal has been given host data at all.
    /// </summary>
    private bool _hasReceivedData;

    /// <summary>
    /// Whether this terminal has been given any host data yet.
    /// </summary>
    public bool HasReceivedData => _hasReceivedData;

    public virtual void ProcessData(ReadOnlySpan<byte> data)
    {
        ApplyTransportEncodingToParser();

        // NOTE: the raw payload is deliberately NOT dumped here. It is traced exactly once,
        // by TerminalSession, on its way in. Dumping it again at this layer is what made the
        // old log show the same bytes four times per packet.
        //
        // Batched: every sequence in this chunk that changes the screen used to publish a whole
        // fresh frame and post its own repaint. One chunk, one frame, one repaint.
        BeginBatch();
        try
        {
            // A LOOP, because printing can start and stop in the middle of one chunk.
            //
            // CSI 5 i means every byte AFTER it goes to the printer, and CSI 4 i hands the rest
            // back to the screen. Both arrive inside a chunk of host data, so a single pass would
            // put a whole print job on the screen or a screenful of text on the paper. The parser
            // stops at the sequence that changed things and returns how far it got; the rest goes
            // round again.
            while (data.Length != 0)
            {
                // ReGIS one-shot graphics input mode: "the terminal suspends processing of new data
                // from the application until ReGIS sends a position report. The terminal buffers any
                // data received from the application in this mode." Checked before the printer, and
                // before the parser, because it holds back EVERYTHING - the point is that the screen
                // stops changing while the operator aims the crosshair.
                if (IsRegisInputSuspended)
                {
                    BufferSuspendedHostData(data);
                    break;
                }

                if (PrinterControllerMode)
                {
                    // While it is on the terminal is a WIRE: no parsing, no characters, no escape
                    // sequences - a sixel passing through must reach the printer byte for byte.
                    bool stillPrinting = _printerFilter.Process(data, PrintSink, out int printed);
                    if (stillPrinting) break;

                    EndPrinterControllerMode();
                    data = data.Slice(printed);
                    continue;
                }

                _parserStoppedEarly = false;

                int consumed = HasGraphicsInput
                    ? ProcessDataWithGraphics(data)
                    : Parser.ProcessBytes(data);

                data = data.Slice(consumed);
            }

            // Unconditional, so a chunk that changed nothing visible still republishes - the
            // cursor may have moved, and the frame carries the cursor.
            OnInvalidated();
        }
        finally
        {
            EndBatch();
        }
    }

    /// <summary>
    /// DECSCUSR - <c>CSI Ps SP q</c>, the shape of the cursor.
    /// </summary>
    /// <param name="style">
    /// The style number the host asked for.
    /// </param>
    /// <remarks>
    /// xterm's ctlseqs gives the numbers, and note that TWO of them mean the same thing:
    ///  - 0, blinking block.
    ///  - 1, blinking block. This is the default.
    ///  - 2, steady block.
    ///  - 3, blinking underline.
    ///  - 4, steady underline.
    ///  - 5, blinking bar.
    ///  - 6, steady bar.
    ///
    /// A number outside that range is ignored rather than rounded to a neighbour: the host asked
    /// for a shape this terminal does not have, and picking one for it would be an invention.
    /// </remarks>
    protected virtual void SetCursorStyle(int style)
    {
        switch (style)
        {
            case 0:
            case 1: Cursor.Style = CursorStyle.BlinkingBlock; break;
            case 2: Cursor.Style = CursorStyle.Block; break;
            case 3: Cursor.Style = CursorStyle.BlinkingUnderline; break;
            case 4: Cursor.Style = CursorStyle.Underline; break;
            case 5: Cursor.Style = CursorStyle.BlinkingBar; break;
            case 6: Cursor.Style = CursorStyle.Bar; break;
            default: return;
        }

        OnInvalidated();
    }

    /// <summary>
    /// DECSTR - the soft terminal reset.
    /// </summary>
    /// <remarks>
    /// <para><b>What it touches, and nothing else</b></para>
    /// Table 13-1 of the VT420 Programmer Reference lists it exactly, and the note beside it is
    /// the important half: "DECSTR affects only those functions listed in Table 13-1." So this
    /// does NOT clear the screen, does not empty the scrollback, does not forget a downloaded
    /// character set or the user-defined keys, and does not touch the alternate screen. A soft
    /// reset that behaved like RIS would throw away a host's work every time a program exited.
    ///
    /// The table, in its own words:
    ///  - DECTCEM: cursor enabled.
    ///  - IRM: replace.
    ///  - DECOM: absolute.
    ///  - DECAWM: no autowrap.
    ///  - KAM: unlocked.
    ///  - DECNKM: numeric characters.
    ///  - DECCKM: normal (arrow keys).
    ///  - DECSTBM: top margin 1, bottom margin the page length.
    ///  - G0 to G3: the default sets.
    ///  - SGR: normal rendition.
    ///  - DECSCA: normal, erasable by DECSEL and DECSED.
    ///  - DECSC: home position with the default state.
    ///  - DECSASD: main display - and the VT330/VT340 manual says the same in its own words,
    ///    "Soft terminal reset (DECSTR): Exits status line."
    ///
    /// <para><b>What is deliberately left alone, and why</b></para>
    /// The left and right margins and DECLRMM are NOT in table 13-1, and the note above says the
    /// list is the whole of it - so they survive a soft reset here. xterm clears them; no document
    /// held here says to, and the manual explicitly says otherwise, so the manual wins and the
    /// disagreement is written down rather than split.
    ///
    /// DECNRCM, DECAUPSS, DECKPM and DECSMKR are in the table and are not reset because they do
    /// not exist here at all. THE CURSOR IS NOT MOVED: the table lists settings, not the display,
    /// and nothing in it says the cursor goes home.
    /// </remarks>
    protected virtual void SoftReset()
    {
        Cursor.Visible = true;                 // DECTCEM
        InsertMode = false;                    // IRM
        OriginMode = false;                    // DECOM
        AutoWrapMode = false;                  // DECAWM - "No autowrap", in the manual's words
        Cursor.AutoWrap = false;
        ApplicationKeypad = false;             // DECNKM
        ApplicationCursorKeys = false;         // DECCKM

        // DECSTBM back to the whole page. ClearScrollingRegion homes the cursor, which table 13-1
        // does not ask for, so the region is set directly instead.
        ScrollTop = 0;
        ScrollBottom = Height - 1;

        // G0 to G3 back to their defaults, and GL back to G0.
        CharacterSets[0] = 0;
        CharacterSets[1] = 0;
        CharacterSets[2] = 0;
        CharacterSets[3] = 0;
        ActiveCharacterSet = 0;
        CurrentCharacterSet = 0;

        CurrentAttributes = CharacterAttributes.None;   // SGR
        CurrentForeground = TerminalColor.Default;
        CurrentBackground = TerminalColor.Default;

        ProtectedMode = false;                 // DECSCA

        // DECSC: "Home position with VT420 defaults." Forgetting the saved state is what puts it
        // there - DECRC with nothing saved already homes the cursor with default rendition.
        _savedState.IsValid = false;
        Cursor.ResetSaved();

        ExitStatusLine();                      // DECSASD
    }

    /// <summary>
    /// Resets the terminal to its initial state
    /// </summary>
    public virtual void Reset()
    {
        // Switch back to main buffer if using alternate buffer (RIS should reset everything)
        if (Buffer.IsUsingAlternateBuffer)
        {
            Buffer.SwitchToPrimaryBuffer();
        }

        // BOTH screens: a terminal that has just been switched on has nothing on either.
        Buffer.ClearBothBuffers();
        Buffer.ClearScrollback();
        Cursor.Reset();
        Parser.Reset();

        // A hard reset is the ONLY way out of the DECUDK lock. That is the point of the lock: if a
        // host could unlock by asking, it would stop nothing.
        UserKeys.Reset();

        LeftRightMarginMode = false;
        ClearLeftRightMargins();
        AttributeChangeIsRectangular = false;

        // Text colours a Sixel image set on a VT340 go back to the theme's. There is no sequence
        // that undoes them - on the hardware a picture simply changed a setting - so a hard reset
        // is the only way back, the same as the DECUDK lock above.
        ImageTextForeground = null;
        ImageTextBackground = null;

        // "Hard terminal reset (RIS): Erases and exits the status line", and the line goes back to
        // the indicator kind it powers on as.
        StatusLineType = StatusLineKind.Indicator;
        WritingToStatusLine = false;
        ClearStatusLine();

        // THE XTERM MODES GO TOO, and this is the one that matters most. A terminal that kept
        // reporting the mouse after a reset would send bytes to a program that never asked for
        // them, and those bytes land in the middle of whatever it was reading. Bracketed paste and
        // focus reporting are the same shape: the host that asked for them is gone.
        Mouse.Mode = Input.MouseTrackingMode.Off;
        Mouse.Encoding = Input.MouseEncoding.X10;
        BracketedPasteMode = false;
        FocusReporting = false;

        // Back to ANSI. A reset leaves the terminal as it was switched on, and a VT-family
        // terminal switches on in ANSI mode.
        Vt52Mode = false;

        // A downloaded character set goes with it. The shapes came from a host, and a reset is
        // meant to leave the terminal as it was switched on.
        SoftFont.Reset();

        // And any Sixel images. The plane itself is kept - rebuilding it on the next image would
        // cost an allocation to save nothing - but nothing drawn on it survives a reset.
        var sixelPlane = Graphics?.FindPlane(SixelPlaneId);
        sixelPlane?.Clear();

        var regisPlane = Graphics?.FindPlane(RegisPlaneId);
        regisPlane?.Clear();

        // The DECODERS are reset as well as their planes. A ReGIS drawing point and a Sixel palette
        // are host state - they survive between sequences on purpose, and a hard reset is exactly
        // the event that ends them.
        _regisDecoder = null;
        _sixelDecoder?.ResetRegisters();

        if (sixelPlane != null || regisPlane != null)
        {
            Graphics?.Composite();
        }

        ResetToInitialState();
        OnInvalidated();
    }

    public virtual void ResetToInitialState()
    {
        CurrentAttributes = CharacterAttributes.None;
        CurrentForeground = TerminalColor.Default;
        CurrentBackground = TerminalColor.Default;
        CurrentCharacterSet = 0;
        ProtectedMode = false;      // DECSCA is part of the state a reset returns to defaults

        // Initialize with full screen scroll region (no active scroll region)
        ScrollTop = 0;
        ScrollBottom = Height - 1;

        ApplicationCursorKeys = false;
        ApplicationKeypad = false;
        // Back to BS, not to DEL - see the field for why this emulator's power-on value differs
        // from a real VT220's on this one mode.
        BackarrowSendsBackspace = true;

        // A hard reset takes DECTEK with it. Leaving a terminal in 4014 mode across a reset would
        // mean the next thing the host printed came out as coordinates.
        TektronixMode = false;
        _tektronixPlotter?.Vectors.Reset();

        // Scrolling jumps again. DECSCLM off is what a VT100 powers on with, and it is also the
        // safe answer: a terminal stuck in smooth scroll after a reset would feel slow with no
        // visible reason.
        SmoothScrollMode = false;
        System.Threading.Volatile.Write(ref _smoothScrollBacklog, 0);

        // National replacement sets back on, matching the power-on value described on the field.
        NationalReplacementSets = true;

        // Keys repeat again. A terminal that came out of a reset with auto-repeat still off would
        // feel broken in a way nobody would think to look for.
        AutoRepeatKeys = true;
        AutoWrapMode = true;
        ReverseWrapMode = false;
        InsertMode = false;
        NewLineMode = false;
        ReverseVideoMode = false;
        OriginMode = false;

        // Tab stops and the DECSC snapshot are part of the reset state - BOTH screens' snapshots,
        // or a reset on one screen would leave the other one's stale save waiting to come back.
        ResetTabStops();
        _savedState = default;
        _savedStateOfTheOtherScreen = default;
        _savedCursorOfTheOtherScreen = default;

        for (int i = 0; i < CharacterSets.Length; i++)
        {
            CharacterSets[i] = 0; // ASCII
        }
        ActiveCharacterSet = 0;
    }

    /// <summary>
    /// The line-size bits — DECDWL and the two halves of DECDHL.
    ///
    /// These are the one part of <see cref="CharacterAttributes"/> that belongs to the LINE rather
    /// than to the character, so writing a character must carry them across rather than replace
    /// them with the current SGR state. It did not, which meant a double-width line stopped being
    /// double-width the moment anything was typed on it — the flag survived only on the cells
    /// nobody had touched yet.
    /// </summary>
    protected const CharacterAttributes LineSizeAttributes =
        CharacterAttributes.DoubleWidth
        | CharacterAttributes.DoubleHeightTop
        | CharacterAttributes.DoubleHeightBottom;

    /// <summary>
    /// DECSCA state: whether characters written from now on are PROTECTED against selective
    /// erase (DECSED / DECSEL, the <c>CSI ? J</c> and <c>CSI ? K</c> forms).
    ///
    /// Held separately from <see cref="CurrentAttributes"/> on purpose. DECSCA is not an SGR
    /// rendition — <c>SGR 0</c> must not un-protect a field — so it is OR-ed in when a cell is
    /// written, the same way the line-size bits are.
    ///
    /// This is what lets a host draw a form once, then clear only what the user typed and leave
    /// the labels standing.
    /// </summary>
    protected bool ProtectedMode;

    /// <summary>
    /// The Protected bit to OR into a cell being written, per <see cref="ProtectedMode"/>.
    /// </summary>
    protected CharacterAttributes ProtectedAttribute
        => ProtectedMode ? CharacterAttributes.Protected : CharacterAttributes.None;

    /// <summary>
    /// The last column a character can be written to on <paramref name="row"/>.
    ///
    /// Normally the physical right edge. On a DOUBLE-WIDTH line — DECDWL, or either half of
    /// DECDHL — each character is drawn twice as wide, so an 80-column screen holds only 40 of
    /// them and the line must wrap at column 39. Treating a double-width line as full width let
    /// text run off the visible right-hand side of the screen into cells that are never drawn.
    ///
    /// Reading cell 0 is enough: the line-size sequences apply to the WHOLE line, which is why
    /// they are line operations rather than SGR attributes.
    /// </summary>
    protected int LastUsableColumn(int row)
    {
        int last = Width - 1;

        if (Buffer.TryGetCell(row, 0, out var first) && first.DoubleWidth)
        {
            last = Width / 2 - 1;
        }

        // The RIGHT MARGIN, when the host has set one. This is the single place the right-hand
        // limit is decided, so putting the margin here means printing, wrapping and the
        // double-width limit all agree without three copies of the rule.
        if (LeftRightMarginMode && RightMargin < last)
        {
            last = RightMargin;
        }

        return last;
    }

    /// <summary>
    /// Whether the host has enabled left and right margins (DECLRMM, mode 69).
    /// </summary>
    /// <remarks>
    /// The mode has to be on before <c>CSI Pl ; Pr s</c> means DECSLRM at all - with it off, that
    /// same sequence is SCOSC, save cursor. Two commands, one final byte, told apart only by this
    /// flag.
    /// </remarks>
    public bool LeftRightMarginMode { get; protected set; }

    /// <summary>
    /// Leftmost column of the margined region, counting from 0.
    /// </summary>
    public int LeftMargin { get; protected set; }

    /// <summary>
    /// Rightmost column of the margined region, counting from 0.
    /// </summary>
    public int RightMargin { get; protected set; }

    /// <summary>
    /// The column a wrap or a carriage return lands on.
    /// </summary>
    /// <remarks>
    /// The left margin inside a margined region, and the left edge otherwise.
    /// </remarks>
    protected int FirstUsableColumn => LeftRightMarginMode ? LeftMargin : 0;

    /// <summary>
    /// DECSLRM: <c>CSI Pl ; Pr s</c>, set the left and right margins.
    /// </summary>
    /// <remarks>
    /// Both parameters count from 1 and default to the edges of the screen, so a bare
    /// <c>CSI s</c> with the mode on means "the whole width" - which is how a host clears the
    /// margins without turning the mode off. A left margin at or past the right is refused rather
    /// than swapped: the host asked for something meaningless and guessing at its intent would put
    /// text somewhere it did not choose.
    /// </remarks>
    /// <param name="parameters">
    /// The numeric parameters: left, then right.
    /// </param>
    protected virtual void SetLeftRightMargins(ReadOnlySpan<int> parameters)
    {
        int left = parameters.Length > 0 && parameters[0] > 0 ? parameters[0] - 1 : 0;
        int right = parameters.Length > 1 && parameters[1] > 0 ? parameters[1] - 1 : Width - 1;

        if (right > Width - 1) right = Width - 1;
        if (left < 0) left = 0;
        if (left >= right) return;

        LeftMargin = left;
        RightMargin = right;

        // "DECSLRM moves the cursor to column 1, line 1 of the page" - the same wording DECSTBM
        // carries, and the same rule: origin mode decides where the host's column 1, line 1 is.
        // It used to land on the new LEFT MARGIN whatever origin mode said, which put the cursor
        // inside the region on a terminal that was still addressing the whole page.
        MoveCursorHomeAsTheHostSeesIt();
    }

    /// <summary>
    /// Returns the margins to the full width of the screen.
    /// </summary>
    protected void ClearLeftRightMargins()
    {
        LeftMargin = 0;
        RightMargin = Width - 1;
    }

    /// <summary>
    /// Performs the wrap that the PREVIOUS character deferred, if one is pending.
    ///
    /// Call this at the top of any HandleCharacter override, before writing anything. It lives
    /// here rather than at each call site on purpose: TDV2215 has its own HandleCharacter, and a
    /// wrap rule copied into both would be fixed in one and stay broken in the other — which is
    /// exactly how 2215 came to never scroll at the bottom of a scrolling region while 2200 did.
    ///
    /// See <see cref="Cursor.PendingWrap"/> for what the Last Column Flag is and why a real VT
    /// waits for the next character before wrapping.
    /// </summary>
    protected void ResolveDeferredWrap()
    {
        if (!Cursor.PendingWrap)
        {
            return;
        }

        if (!AutoWrapMode)
        {
            // DECAWM was switched off while the flag was armed: no wrap, and the character
            // overwrites the last column.
            Cursor.ClearPendingWrap();
            return;
        }

        int rowBeforeWrap = Cursor.Row;

        // Record that this line carries on onto the next one. Nothing else can tell afterwards:
        // in the grid, a line that filled up and continued looks exactly like one that ended with
        // a newline. Screen-to-text reads need it, and reflow on resize is impossible without it.
        Buffer.SetLineWrapped(rowBeforeWrap, true);

        Cursor.ResolvePendingWrapTo(FirstUsableColumn);

        // Only scroll when the wrap ran off the BOTTOM OF THE SCROLL REGION. Writing outside that
        // region must not scroll the screen.
        if (rowBeforeWrap == ScrollBottom)
        {
            ScrollUp();
            Cursor.Row = ScrollBottom;
        }
    }

    protected virtual void HandleCharacter(uint codepoint)
    {
        // A COMBINING MARK takes no column of its own — it modifies the character already on the
        // screen. Writing it into a cell consumed one that belonged to the next character, so
        // "e" followed by a combining acute over "0123" swallowed the "1". It must attach and
        // leave the cursor where it is.
        if (CharacterWidth.IsZeroWidth(codepoint))
        {
            ApplyCombiningMark(codepoint);
            return;
        }

        // DECSASD has pointed output at the status line, so nothing below applies: there is one
        // row, no wrapping, no scrolling and no cursor of the main display to move. The character
        // set mapping still does, because "Both the main display and status line use the same
        // character set."
        if (WritingToStatusLine)
        {
            WriteToStatusLine(ApplyCharacterSetMapping(codepoint, CharacterSets[ActiveCharacterSet]));
            return;
        }

        ResolveDeferredWrap();

        int width = CharacterWidth.Of(codepoint);

        // A WIDE character needs two cells side by side. If only one is left before the right
        // margin it cannot be drawn there at all, so it wraps to the next line rather than being
        // split in half across the edge of the screen.
        if (width == 2 && Cursor.Column >= LastUsableColumn(Cursor.Row))
        {
            if (AutoWrapMode)
            {
                Buffer.SetLineWrapped(Cursor.Row, true);
                int rowBeforeWrap = Cursor.Row;
                Cursor.ResolvePendingWrapTo(FirstUsableColumn);
                if (rowBeforeWrap == ScrollBottom)
                {
                    ScrollUp();
                    Cursor.Row = ScrollBottom;
                }
            }
            else
            {
                return;     // nowhere to put it, and no wrapping allowed
            }
        }

        // Insert mode: shift existing characters right
        if (InsertMode && !Cursor.AtLastColumn)
        {
            ShiftCharactersRight(Cursor.Row, Cursor.Column, width);
        }

        // Apply character set mapping, honouring a single shift if one is armed
        uint mappedCodepoint = ApplyCharacterSetMapping(codepoint, CharacterSets[CharacterSetForThisCharacter()]);

        // Overwriting either half of a wide character destroys the whole of it, so the other half
        // has to be cleared too — otherwise a stray lead or trail is left behind and every
        // following column reads one place out.
        ClearWidePartner(Cursor.Row, Cursor.Column);

        // Write character to buffer
        ref var cell = ref Buffer[Cursor.Row, Cursor.Column];
        cell.Codepoint = mappedCodepoint;
        cell.Attributes = CurrentAttributes | (cell.Attributes & LineSizeAttributes) | ProtectedAttribute;
        cell.Foreground = CurrentForeground;
        cell.Background = CurrentBackground;
        cell.CharacterSet = CharacterSets[CharacterSetForThisCharacter()];

        // A single shift lasts for ONE character, and this was it.
        _singleShift = 0;
        cell.IsWideLead = width == 2;
        cell.IsWideTrail = false;

        // This character is newer than anything the graphics plane has put here, so it belongs on
        // top of the picture. On a VT340 that is automatic - one bitmap, last write wins - and here
        // the renderer needs telling. See TerminalCell.TextIsNewerThanGraphics.
        cell.TextIsNewerThanGraphics = true;

        if (width == 2)
        {
            // The right-hand half. It draws nothing of its own — the lead is drawn across both —
            // and carries the same colours so a selection or a background fill looks continuous.
            ClearWidePartner(Cursor.Row, Cursor.Column + 1);

            ref var trail = ref Buffer[Cursor.Row, Cursor.Column + 1];
            trail.Codepoint = 0;
            trail.Attributes = CurrentAttributes | (trail.Attributes & LineSizeAttributes) | ProtectedAttribute;
            trail.Foreground = CurrentForeground;
            trail.Background = CurrentBackground;
            trail.CharacterSet = CharacterSets[ActiveCharacterSet];
            trail.IsWideLead = false;
            trail.IsWideTrail = true;

            Cursor.AdvanceWithin(LastUsableColumn(Cursor.Row));
        }

        // What REP would repeat. The codepoint as it ARRIVED, not the one the character set mapped
        // it to: repeating goes back through this method, so mapping it twice would send a line
        // drawing character through the graphics table a second time.
        _lastGraphicCharacter = codepoint;

        // Advance. At the last USABLE column — half the screen on a double-width line — this arms
        // the Last Column Flag instead of moving, so the wrap and any scroll happen at the top of
        // the NEXT call, not here.
        Cursor.AdvanceWithin(LastUsableColumn(Cursor.Row));
    }

    /// <summary>
    /// Stands for "nothing has been printed that REP could repeat".
    /// </summary>
    /// <remarks>
    /// Zero is safe as the empty value because a NUL never reaches
    /// <see cref="HandleCharacter"/> - it is a control, and controls clear this anyway.
    /// </remarks>
    private const uint NoCharacterToRepeat = 0;

    /// <summary>
    /// The last graphic character printed, which is the one REP repeats.
    /// </summary>
    /// <remarks>
    /// Cleared by every control and by every escape sequence, which is the part of REP that is easy
    /// to get wrong and that the xterm.js fixture settles. "abcdefg" then CSI 3 D then CSI b leaves
    /// the text alone on a real xterm, because the cursor move ended the run - a terminal that kept
    /// the character would print an eighth 'g' in the middle of the word.
    /// </remarks>
    private uint _lastGraphicCharacter = NoCharacterToRepeat;

    /// <summary>
    /// REP - repeat the last graphic character.
    /// </summary>
    /// <param name="count">
    /// How many extra copies to print. ECMA-48's default of 1 applies to an omitted parameter and
    /// to an explicit zero alike, which the fixture's CSI 0 b confirms.
    /// </param>
    /// <param name="character">
    /// The character to repeat, captured before the dispatch cleared it.
    /// </param>
    /// <remarks>
    /// Each copy goes back through <see cref="HandleCharacter"/> rather than being written into
    /// cells here, so insert mode, the deferred wrap at the right margin and any scroll off the
    /// bottom all behave exactly as they would for typed text. The fixture depends on that: a '@'
    /// in column 72 followed by CSI 20 b fills to the end of the row and carries on across the
    /// next one.
    /// </remarks>
    protected virtual void RepeatLastCharacter(int count, uint character)
    {
        if (character == NoCharacterToRepeat)
        {
            return;
        }

        if (count < 1)
        {
            count = 1;
        }

        for (int i = 0; i < count; i++)
        {
            HandleCharacter(character);
        }

        // Printing through HandleCharacter set this again, which is what makes two REPs in a row
        // keep repeating the same character. No fixture covers that case; it follows from the
        // sequence being defined as "repeat the preceding graphic character".
        _lastGraphicCharacter = character;
    }

    /// <summary>
    /// Attaches a combining mark to the character already on the screen, without consuming a cell.
    ///
    /// A cell holds ONE codepoint, so the mark is composed into the base character where Unicode
    /// has a precomposed form for the pair — "e" plus a combining acute becomes U+00E9. Where no
    /// precomposed form exists (Devanagari, Thai, stacked accents) the mark is dropped rather
    /// than stored, which is stated plainly rather than hidden: it is still strictly better than
    /// the old behaviour, where the mark both failed to attach AND ate the next character's cell.
    ///
    /// Carrying the marks properly needs a cell that can hold a sequence, which is a change to the
    /// cell model rather than to this method.
    /// </summary>
    protected virtual void ApplyCombiningMark(uint codepoint)
    {
        // The mark belongs to the character just written, which is the one BEHIND the cursor —
        // unless a wrap is pending, in which case the cursor is still sitting on it.
        int row = Cursor.Row;
        int column = Cursor.PendingWrap ? Cursor.Column : Cursor.Column - 1;

        if (column < 0)
        {
            return;     // nothing to attach to at the start of a line
        }

        // Step back over the empty right-hand half of a wide character to reach its lead.
        if (Buffer.TryGetCell(row, column, out var probe) && probe.IsWideTrail && column > 0)
        {
            column--;
        }

        if (!Buffer.TryGetCell(row, column, out var baseCell) || baseCell.Codepoint == 0)
        {
            return;
        }

        uint composed = UnicodeComposition.Compose(baseCell.Codepoint, codepoint);
        if (composed == baseCell.Codepoint)
        {
            return;     // no precomposed form; see the note above
        }

        ref var target = ref Buffer[row, column];
        target.Codepoint = composed;
        OnInvalidated();
    }

    /// <summary>
    /// Clears the OTHER half of a wide character overlapping this cell, so that overwriting half
    /// of one never leaves the other half stranded.
    /// </summary>
    private void ClearWidePartner(int row, int column)
    {
        if (!Buffer.TryGetCell(row, column, out var cell))
        {
            return;
        }

        if (cell.IsWideLead && Buffer.TryGetCell(row, column + 1, out var right) && right.IsWideTrail)
        {
            ref var partner = ref Buffer[row, column + 1];
            partner.Codepoint = 0;
            partner.IsWideTrail = false;
        }
        else if (cell.IsWideTrail && column > 0 && Buffer.TryGetCell(row, column - 1, out var left) && left.IsWideLead)
        {
            ref var partner = ref Buffer[row, column - 1];
            partner.Codepoint = 0;
            partner.IsWideLead = false;
        }
    }

    /// <summary>
    /// Applies character set mapping to convert ASCII characters to their terminal-specific equivalents.
    /// </summary>
    /// <param name="codepoint">
    /// The original character codepoint
    /// </param>
    /// <param name="characterSet">
    /// The character set index (0=ASCII, 1=UK, 2=DEC Special Graphics)
    /// </param>
    /// <returns>
    /// The mapped character codepoint
    /// </returns>
    protected virtual uint ApplyCharacterSetMapping(uint codepoint, byte characterSet)
    {
        // Only map printable ASCII characters (0x20-0x7E)
        if (codepoint < 0x20 || codepoint > 0x7E)
        {
            return codepoint;
        }

        switch (characterSet)
        {
            case 0: // US ASCII - no mapping needed
                return codepoint;

            case 1: // UK - minimal differences from US ASCII
                return codepoint;

            case 2: // DEC Special Graphics - map ASCII to line drawing characters
                return MapDecSpecialGraphics((char)codepoint);

            default:
                return codepoint;
        }
    }

    /// <summary>
    /// Maps ASCII characters to DEC Special Graphics line drawing characters.
    /// </summary>
    /// <param name="asciiChar">
    /// The ASCII character to map
    /// </param>
    /// <returns>
    /// The Unicode codepoint for the corresponding line drawing character
    /// </returns>
    private static uint MapDecSpecialGraphics(char asciiChar)
    {
        return asciiChar switch
        {
            'j' => 0x2518, // ┘ (bottom-right corner)
            'k' => 0x2510, // ┐ (top-right corner)
            'l' => 0x250C, // ┌ (top-left corner)
            'm' => 0x2514, // └ (bottom-left corner)
            'n' => 0x253C, // ┼ (crossing lines)
            'q' => 0x2500, // ─ (horizontal line)
            't' => 0x251C, // ├ (left T-junction)
            'u' => 0x2524, // ┤ (right T-junction)
            'v' => 0x2534, // ┴ (bottom T-junction)
            'w' => 0x252C, // ┬ (top T-junction)
            'x' => 0x2502, // │ (vertical line)
            _ => (uint)asciiChar // Return original character if not in mapping
        };
    }

    protected virtual void HandleExecute(byte control)
    {
        // A control ENDS the run of graphic characters, so REP has nothing left to repeat. The
        // t0040-REP fixture pins this: a CSI 3 b at the start of a line, after the CR LF that ended
        // the line before, repeats nothing at all.
        _lastGraphicCharacter = NoCharacterToRepeat;

        switch (control)
        {
            case 0x07: // BEL
                Bell?.Invoke();
                break;

            case 0x08: // BS - Backspace
                HandleBackspace();
                break;

            case 0x09: // HT - Horizontal Tab
                HandleTab();
                break;

            case 0x0A: // LF - Line Feed
            case 0x0B: // VT - Vertical Tab
            case 0x0C: // FF - Form Feed
                HandleLineFeed();
                if (NewLineMode)
                {
                    Cursor.CarriageReturn();
                }
                break;

            case 0x0D: // CR - Carriage Return
                // AND NOTHING ELSE, whatever LNM says. The VT420 manual is explicit about which
                // way that mode points: "If LNM is set, the cursor moves to the first column on
                // the next line when the terminal receives an LF, FF, or VT character. When you
                // press Return, the terminal sends both a carriage return and line feed."
                //
                // So LNM adds a carriage return to a RECEIVED LINE FEED, and adds a line feed to
                // what the RETURN KEY SENDS. It says nothing about a received carriage return,
                // and making one feed a line meant every CR LF pair - which is what a tty sends
                // for every newline - advanced TWO rows instead of one.
                Cursor.CarriageReturn();
                break;

            case 0x0E: // SO - Shift Out (G1 character set)
                ActiveCharacterSet = 1;
                break;

            case 0x0F: // SI - Shift In (G0 character set)
                ActiveCharacterSet = 0;
                break;

            default:
                // Ignore other control characters
                break;
        }
    }

    protected virtual void HandleEscapeSequence(EscapeSequenceParser parser)
    {
        // An escape sequence ends the run of graphic characters, the same way a control does, so
        // REP has nothing to repeat afterwards. See _lastGraphicCharacter.
        _lastGraphicCharacter = NoCharacterToRepeat;

        // In VT52 mode the escapes mean something else entirely - ESC A is cursor up, not an
        // unknown ANSI sequence - so they are read first and the ANSI meanings never apply.
        if (Vt52Mode && TryHandleVt52Escape(parser)) return;

        var final = (char)parser.FinalByte;
        var intermediates = parser.Intermediates;

        // Handle ESC sequences (2-character)
        if (intermediates.Length == 0)
        {
            switch (final)
            {
                case '<': // DECANM - "enter ANSI mode", the VT52 escape back to ANSI.
                          //
                          // Already handled inside the VT52 branch, which is the only place it can
                          // DO anything - but that branch is unreachable once the terminal is in
                          // ANSI mode, so a host that sends this to assert ANSI (which is exactly
                          // what it is for) got it reported as an unknown sequence.
                          //
                          // Found on 25 August 2026 by the session driving VTM on a real ND-100:
                          // the program sends ESC < first thing on every single run. Harmless, and
                          // it was filling the Protocol Monitor with noise that a reader has to
                          // learn to ignore - which is how a REAL unknown sequence gets missed.
                          //
                          // Nothing to do here beyond not complaining: ANSI mode is already on.
                    break;

                case 'D': // IND - Index (move down, scroll if needed)
                    HandleLineFeed();
                    break;

                case 'E': // NEL - Next Line
                    Cursor.CarriageReturn();
                    HandleLineFeed();
                    break;

                case 'M': // RI - Reverse Index (move up, scroll if needed)
                    HandleReverseLineFeed();
                    break;

                case 'H': // HTS - Horizontal Tab Set
                    SetTabStop();
                    break;

                case '6': // DECBI - Back Index
                    BackIndex();
                    break;

                case '7': // DECSC - Save Cursor (and the rest of the rendition state)
                    SaveTerminalState();
                    break;

                case '8': // DECRC - Restore Cursor (and the rest of the rendition state)
                    RestoreTerminalState();
                    break;

                case '9': // DECFI - Forward Index
                    ForwardIndex();
                    break;

                case 'c': // RIS - Reset to Initial State
                    Reset();
                    // Ensure cursor is at home position after RIS
                    Cursor.Home();
                    break;

                case '=': // DECKPAM - Keypad Application Mode
                    ApplicationKeypad = true;
                    break;

                case '>': // DECKPNM - Keypad Numeric Mode
                    ApplicationKeypad = false;
                    break;

                // ── The shifts ──────────────────────────────────────────────────────────────
                //
                // Found by walking xterm's ctlseqs list: none of these existed, so a host that
                // reached for G2 or G3 got whatever was already in GL. Designating a set with
                // ESC ( ) * + was implemented; INVOKING one was not, which meant G2 and G3 could
                // be loaded and never used.

                case 'N': // SS2 - single shift, G2 for exactly one character
                    _singleShift = 2;
                    break;

                case 'O': // SS3 - single shift, G3 for exactly one character
                    _singleShift = 3;
                    break;

                case 'n': // LS2 - lock G2 into GL
                    ActiveCharacterSet = 2;
                    break;

                case 'o': // LS3 - lock G3 into GL
                    ActiveCharacterSet = 3;
                    break;

                case 'Z':
                    // DECID. "Obsolete form of CSI c", says ctlseqs, and obsolete is not the same
                    // as unused - it is what a VT52-era host sends to ask what it is talking to.
                    // The answer is the same primary DA reply.
                    SendResponse(Profile.PrimaryDeviceAttributes);
                    break;

                default:
                    // Unknown ESC sequence. Counted rather than dropped in silence - see
                    // CountUnrecognisedSequence for why that difference decided a whole case.
                    CountUnrecognisedSequence("ESC " + final);
                    break;
            }
        }
        else
        {
            // ESC with intermediates (3+ character sequences)
            HandleEscapeWithIntermediates(final, intermediates);
        }
    }

    protected virtual void HandleEscapeWithIntermediates(char final, ReadOnlySpan<byte> intermediates)
    {
        // ESC # — the DEC line-size sequences. These lived in TDVEmulatorBase only, which
        // meant a VT100 or ANSI session ignored DECDWL/DECDHL entirely even though
        // CharacterAttributes has carried the flags all along. Nothing about them is
        // TDV-specific: the TDV implementation's own comment cites the VT220 spec.
        if (intermediates.Length == 1 && intermediates[0] == (byte)'#')
        {
            HandleLineSize(final);
            return;
        }

        // S7C1T and S8C1T - ESC SP F and ESC SP G. They choose whether the terminal SENDS its C1
        // controls as single 8-bit bytes or as two-character escape sequences. The state was
        // already here, set by DECSCL's second parameter; these two sequences are the direct way
        // to it and were missing, so a host that asked for 7-bit replies and then read one back
        // as 8-bit had no way to say so.
        if (intermediates.Length == 1 && intermediates[0] == (byte)' ')
        {
            switch (final)
            {
                case 'F': Send8BitControls = false; return;   // S7C1T
                case 'G': Send8BitControls = true; return;    // S8C1T
            }
        }

        // Character set designation
        if (intermediates.Length >= 1)
        {
            var inter = (char)intermediates[0];
            if (inter == '(' || inter == ')' || inter == '*' || inter == '+')
            {
                var setIndex = inter switch
                {
                    '(' => 0, // G0
                    ')' => 1, // G1
                    '*' => 2, // G2
                    '+' => 3, // G3
                    _ => 0
                };

                // A DOWNLOADED set is designated by the name it was given in the DECDLD that
                // loaded it, which may carry an intermediate of its own - " @" is the usual one.
                // So the designator here is everything after the G-set selector, and it is matched
                // against the stored name rather than against a table: the host chose the name.
                if (MatchesSoftFontDesignation(intermediates, final))
                {
                    CharacterSets[setIndex] = SoftFontCharacterSet;
                    return;
                }

                // Anything with a second intermediate that is NOT the downloaded set is a national
                // variant this terminal does not have. Falling through would designate ASCII and
                // quietly draw the wrong letters, so it is left alone instead.
                if (intermediates.Length > 1) return;

                // THREE OF THE MANUAL'S FIFTEEN. Table 5-2 of the VT330/VT340 Text Programming
                // manual lists the designators a VT300 answers to: ASCII 'B', DEC Supplemental
                // Graphic '% 5', ISO Latin-1 'A' in the 96-character position, user-preferred
                // supplemental '<', DEC Special Graphic '0', DEC Technical '>', and twelve
                // national replacement sets - British 'A', Dutch '4', Finnish '5' or 'C', French
                // 'R', French Canadian '9' or 'Q', German 'K', Italian 'Y', Norwegian/Danish
                // backtick or 'E' or '6', Portuguese '% 6', Spanish 'Z', Swedish '7' or 'H',
                // Swiss '='.
                //
                // Only ASCII, British and DEC Special Graphic are here, and NO PROFILE CLAIMS THE
                // REST: none of the DA replies carries 9 (national replacement character sets) or
                // 15 (technical character set), so no host is being told this terminal has them.
                //
                // AN UNKNOWN DESIGNATOR FALLS BACK TO ASCII ON PURPOSE. Leaving the set as it was
                // is what a real terminal does, and it is what the two-intermediate branch above
                // does - but that branch is refusing a designator that can only be a national set,
                // while this one can be reached with DEC Special Graphic already designated. A
                // host asking for Norwegian and getting line-drawing glyphs for every letter is
                // far worse than getting unaccented ones.
                // 'A' IS TWO DIFFERENT DESIGNATORS AND DECNRCM PICKS WHICH. In the 94-character
                // position it is British NRCS; with national replacement sets switched off it is
                // ISO Latin-1, which for everything this terminal can draw behaves as ASCII. That
                // is the documented meaning of private mode 42 rather than an invention here, and
                // it is the only place NRCS currently changes anything - British is the one national
                // set with a glyph table, so it is also the only one the mode can be seen through.
                var characterSet = final switch
                {
                    'A' => NationalReplacementSets ? 1 : 0,
                    'B' => 0, // ASCII
                    '0' => 2, // DEC Special Graphic
                    _ => 0
                };

                CharacterSets[setIndex] = (byte)characterSet;
                System.Diagnostics.Debug.WriteLine($"Character set designation: ESC{inter}{final} -> G{setIndex}={characterSet}");
            }
        }
    }

    /// <summary>
    /// The character-set number that means "this cell comes from the downloaded set".
    /// </summary>
    /// <remarks>
    /// A number rather than a flag because <see cref="TerminalCell.CharacterSet"/> already carries
    /// one per cell, and the renderer already reads it. 3 continues the existing run - 0 ASCII,
    /// 1 UK, 2 DEC Special Graphics.
    /// </remarks>
    public const byte SoftFontCharacterSet = 3;

    /// <summary>
    /// Which G set a single shift has armed for the next character: 2 for SS2, 3 for SS3, 0 for
    /// none.
    /// </summary>
    private int _singleShift;

    /// <summary>
    /// Which G set the character about to be printed comes from.
    /// </summary>
    /// <returns>
    /// The single-shifted set when one is armed, otherwise whatever is locked into GL.
    /// </returns>
    /// <remarks>
    /// SS2 and SS3 last for EXACTLY ONE character, which is what separates them from LS2 and LS3.
    /// A host uses them to reach one glyph out of another set without disturbing what it was
    /// using - one line-drawing corner in the middle of ordinary text.
    /// </remarks>
    private int CharacterSetForThisCharacter()
        => _singleShift == 0 ? ActiveCharacterSet : _singleShift;

    /// <summary>
    /// Whether an SCS sequence names the set a host downloaded.
    /// </summary>
    /// <param name="intermediates">
    /// The escape sequence's intermediates. The first selects G0 to G3; any others are part of the
    /// designator.
    /// </param>
    /// <param name="final">
    /// The sequence's final byte, which is the last character of the designator.
    /// </param>
    /// <returns>
    /// True when this designates the downloaded set.
    /// </returns>
    private bool MatchesSoftFontDesignation(ReadOnlySpan<byte> intermediates, char final)
    {
        if (SoftFont.Count == 0 || SoftFont.Designation.Length == 0) return false;

        var name = SoftFont.Designation;
        if (name.Length != intermediates.Length) return false;   // extra intermediates plus final

        for (int i = 1; i < intermediates.Length; i++)
        {
            if (name[i - 1] != (char)intermediates[i]) return false;
        }

        return name[name.Length - 1] == final;
    }

    /// <summary>
    /// The DEC line-size sequences, ESC # 3/4/5/6 — DECDHL top, DECDHL bottom, DECSWL, DECDWL.
    /// They apply to the WHOLE line the cursor is on, which is why they are line operations
    /// rather than an SGR-style running attribute.
    ///
    /// Lived in TDVEmulatorBase until 2026-08-09, so only the TDV models honoured them.
    ///
    /// ESC # 8 is DECALN, which shares the '#' intermediate but is not a line size at all — it is
    /// a screen-fill alignment pattern, so it gets its own branch here rather than falling through
    /// as an unknown size.
    /// </summary>
    protected virtual void HandleLineSize(char final)
    {
        switch (final)
        {
            case '8': // DECALN — Screen Alignment Pattern
                ScreenAlignmentPattern();
                break;

            case '3': // DECDHL — double-height line, top half
                SetLineHeight(Cursor.Row, LineHeight.DoubleTop);
                break;

            case '4': // DECDHL — double-height line, bottom half
                SetLineHeight(Cursor.Row, LineHeight.DoubleBottom);
                break;

            case '5': // DECSWL — single-width, single-height line
                SetLineHeight(Cursor.Row, LineHeight.Single);
                break;

            case '6': // DECDWL — double-width line
                SetLineWidth(Cursor.Row, LineWidth.Double);
                break;
        }
    }

    /// <summary>
    /// DECALN (ESC # 8) — Screen Alignment Pattern. Fills the ENTIRE screen with capital E and
    /// puts the cursor home.
    ///
    /// It exists so an engineer could see every cell at once and adjust the monitor's geometry,
    /// which is why it ignores the scrolling region and the current attributes: it must paint the
    /// whole tube in the plain default rendition. It was decoded for the logs and then dropped,
    /// so DEC's own vttest alignment screen — which starts with DECALN and then erases regions
    /// out of the E's to leave a frame — came out blank in the middle.
    /// </summary>
    protected virtual void ScreenAlignmentPattern()
    {
        for (int row = 0; row < Height; row++)
        {
            for (int col = 0; col < Width; col++)
            {
                ref var cell = ref Buffer[row, col];
                cell.Codepoint = 'E';
                cell.Attributes = CharacterAttributes.None;
                cell.Foreground = TerminalColor.Default;
                cell.Background = TerminalColor.Default;
                cell.CharacterSet = 0;
            }

            // The pattern replaces whatever was there, so nothing continues onto the next line.
            Buffer.SetLineWrapped(row, false);
        }

        Cursor.Home();
        OnInvalidated();
    }

    /// <summary>
    /// Sets the height of one line.
    /// Per the VT220 spec (and the TDV manuals, which follow it), a double-height line is
    /// ALWAYS also double-width — a 2x2 character cell.
    /// </summary>
    protected virtual void SetLineHeight(int row, LineHeight height)
    {
        if (row < 0 || row >= Height) return;

        var sizeBefore = LineSizeOf(row);

        for (int col = 0; col < Width; col++)
        {
            var cell = Buffer.GetCell(row, col);

            cell.Attributes = cell.Attributes & ~(CharacterAttributes.DoubleHeightTop
                | CharacterAttributes.DoubleHeightBottom | CharacterAttributes.DoubleWidth);

            if (height == LineHeight.DoubleTop)
            {
                cell.Attributes = cell.Attributes | CharacterAttributes.DoubleHeightTop | CharacterAttributes.DoubleWidth;
            }
            else if (height == LineHeight.DoubleBottom)
            {
                cell.Attributes = cell.Attributes | CharacterAttributes.DoubleHeightBottom | CharacterAttributes.DoubleWidth;
            }

            Buffer.SetCell(row, col, cell);
        }

        ClearGraphicsIfLineSizeChanged(row, sizeBefore);
        OnInvalidated();
    }

    /// <summary>
    /// Sets the width of one line — double-width WITHOUT double-height (DECDWL/DECSWL).
    /// Any double-height already on the line is preserved.
    /// </summary>
    protected virtual void SetLineWidth(int row, LineWidth width)
    {
        if (row < 0 || row >= Height) return;

        bool isDoubleWidth = width == LineWidth.Double;

        var sizeBefore = LineSizeOf(row);

        for (int col = 0; col < Width; col++)
        {
            var cell = Buffer.GetCell(row, col);

            if (isDoubleWidth)
            {
                cell.Attributes = cell.Attributes | CharacterAttributes.DoubleWidth;
            }
            else
            {
                cell.Attributes = cell.Attributes & ~CharacterAttributes.DoubleWidth;
            }

            Buffer.SetCell(row, col, cell);
        }

        ClearGraphicsIfLineSizeChanged(row, sizeBefore);
        OnInvalidated();
    }

    /// <summary>
    /// Puts every line on the screen back to single width and single height.
    /// </summary>
    /// <remarks>
    /// Goes through <see cref="SetLineHeight"/> so the graphics on any row that actually changes are
    /// cleared, which is the rule the ReGIS half of the same fixture establishes. A row already at
    /// single size is left completely alone - no attribute change, so no erase.
    /// </remarks>
    private void ResetAllLineAttributes()
    {
        for (int row = 0; row < Height; row++)
        {
            if (LineSizeOf(row) == CharacterAttributes.None) continue;

            SetLineHeight(row, LineHeight.Single);
        }
    }

    /// <summary>
    /// The line-size bits currently on a row.
    /// </summary>
    /// <remarks>
    /// Read from the first cell. A line size is a property of the whole LINE - both setters write
    /// every cell on the row - so any one of them answers for all of them.
    /// </remarks>
    /// <param name="row">
    /// The row to read.
    /// </param>
    /// <returns>
    /// Just the double-width and double-height bits.
    /// </returns>
    private CharacterAttributes LineSizeOf(int row)
    {
        if (row < 0 || row >= Height || Width <= 0) return CharacterAttributes.None;

        return Buffer.GetCell(row, 0).Attributes & LineSizeAttributes;
    }

    /// <summary>
    /// Wipes the graphics on a row when its line size actually changed.
    /// </summary>
    /// <remarks>
    /// <para><b>Measured on a VT340, not derived</b></para>
    /// hackerb9's <c>regis-decdwl.sh</c> exists to ask this question, and its photograph answers it:
    /// "changing the line attributes clears any graphics on that row", and "setting line attributes
    /// to the same as before has no effect". The script draws one flag across six rows that differ
    /// only in the ORDER of attributes, text and graphics, and the picture shows the flag erased on
    /// exactly the three rows whose attribute changed after the drawing.
    /// The no-change case is the half that is easy to miss and is the useful half: it is what lets a
    /// program update the text on a double-width line without erasing the picture behind it, because
    /// setting the attribute it already has costs nothing.
    /// The text is NOT touched. The same fixture says so - unlike Sixel, ReGIS "does not reset the
    /// line attribute flags to single width nor does it clear the underlying text buffer".
    /// </remarks>
    /// <param name="row">
    /// The row whose size may have changed.
    /// </param>
    /// <param name="sizeBefore">
    /// The line-size bits before the change.
    /// </param>
    private void ClearGraphicsIfLineSizeChanged(int row, CharacterAttributes sizeBefore)
    {
        if (LineSizeOf(row) == sizeBefore) return;

        var compositor = Graphics;
        if (compositor == null) return;

        int cellHeight = GraphicsCellHeight;
        int top = row * cellHeight;

        bool wipedAnything = false;
        for (int i = 0; i < compositor.PlaneCount; i++)
        {
            compositor.PlaneAt(i).Surface.ClearRows(top, cellHeight);
            wipedAnything = true;
        }

        if (wipedAnything) compositor.Composite();
    }

    protected virtual void HandleCsiSequence(EscapeSequenceParser parser)
    {
        var final = (char)parser.FinalByte;
        var parameters = parser.Parameters;
        var privateMarker = parser.PrivateMarker;

        // A control sequence ENDS the run of graphic characters. REP is the one sequence that needs
        // the character that came before it, so it is captured here and cleared for everything
        // else - including for REP itself, so a repeat cannot be repeated by accident later.
        uint repeatable = _lastGraphicCharacter;
        _lastGraphicCharacter = NoCharacterToRepeat;

        // REP - CSI Pn b. Not a cursor movement and not an edit: it prints, so it is dispatched
        // before anything else touches the cursor.
        if (final == 'b' && privateMarker == 0 && parser.Intermediates.Length == 0)
        {
            RepeatLastCharacter(parser.GetParam(0, 1), repeatable);
            return;
        }

        // DEC private mode sequences (CSI ? ...)
        if (privateMarker == (byte)'?')
        {
            // DECRQM (CSI ? Ps $ p) shares the '?' marker with DECSET/DECRST but carries a '$'
            // intermediate and asks a question rather than setting anything. Without the
            // intermediate check it would fall into the mode switch and be silently ignored.
            var intermediates = parser.Intermediates;
            if (final == 'p' && intermediates.Length == 1 && intermediates[0] == (byte)'$')
            {
                HandleRequestMode(parser.GetParam(0, 0));
                return;
            }

            // DECSED / DECSEL — SELECTIVE erase. Same shape as ED and EL but with the '?' marker,
            // and they spare characters marked protected by DECSCA. They used to fall into the
            // private-MODE switch, which knows nothing about 'J' or 'K', so a host asking to clear
            // a form while keeping its labels cleared nothing at all.
            if (final == 'J')
            {
                HandleEraseInDisplay(parser.GetParam(0, 0), selective: true);
                return;
            }

            if (final == 'K')
            {
                HandleEraseInLine(parser.GetParam(0, 0), selective: true);
                return;
            }

            // XTSMGRAPHICS - CSI ? Pi ; Pa ; Pv S. A Sixel-aware host asks this BEFORE it sends an
            // image, to find out how big the graphics area is and how many colour registers it may
            // use. Without an answer, img2sixel and its kind fall back to a guess or refuse.
            if (final == 'S')
            {
                ReportGraphicsAttribute(parameters);
                return;
            }

            // DSR, DEC-specific - CSI ? Ps n. A QUESTION, not a mode change, and it used to fall
            // through to HandleDecPrivateMode below. That reads any final byte other than 'h' as
            // "reset", so a host asking DECXCPR with CSI ? 6 n silently TURNED ORIGIN MODE OFF, and
            // CSI ? 25 n hid the cursor. Both are sequences real software sends.
            if (final == 'n')
            {
                HandleDecPrivateDeviceStatusReport(parameters);
                return;
            }

            // DECST8C - CSI ? 5 W. "Reset tab stops to start with column 9, every 8 columns"
            // (xterm's ctlseqs, VT510). Only Ps = 5 does anything; the manual gives no other
            // value, so nothing else is guessed at.
            //
            // The same trap as 'n' and 'i': without this it fell into the private-MODE switch,
            // where any final byte other than 'h' means reset - so CSI ? Ps W RESET private mode
            // Ps. With the documented parameter of 5 that happened to be harmless, because mode 5
            // is DECSCNM and this emulator does not implement it; with any other parameter it was
            // real, and CSI ? 7 W turned autowrap off. That is three separate final bytes now
            // found sitting in the same hole.
            if (final == 'W')
            {
                if (parser.GetParam(0, 0) == 5) ResetTabStops();
                return;
            }

            // XTSAVE and XTRESTORE - CSI ? Pm s and CSI ? Pm r. Same hole again, and this one was
            // the most dangerous of the four: CSI ? 7 s, "remember whether autowrap is on", would
            // have TURNED AUTOWRAP OFF. xterm's own documentation gives that exact sequence as the
            // termcap idiom - "this can be used in termcap for vi(1), for example, to turn off
            // saving of lines, but restore whatever the original state was on exit".
            if (final == 's')
            {
                SavePrivateModes(parameters);
                return;
            }

            if (final == 'r')
            {
                RestorePrivateModes(parameters);
                return;
            }

            // MC, DEC-specific - CSI ? Ps i. The same trap as 'n' above: it is a COMMAND, not a
            // mode, and the private-mode handler below reads any final byte other than 'h' as a
            // reset. So CSI ? 5 i, "turn autoprint on", would have reset private mode 5 - reverse
            // video - instead.
            if (final == 'i')
            {
                HandleMediaCopy(privateMarker, parser.GetParam(0, 0));
                return;
            }

            HandleDecPrivateMode(final, parameters);
            return;
        }

        // DECSTR - CSI ! p, the soft terminal reset. It was not implemented AT ALL, which is how
        // it went unnoticed: the sequence carries a '!' intermediate that nothing matched, so it
        // fell out of the switch and did nothing. Every curses program sends it on the way out,
        // and so does tput, so a session left by vim kept whatever modes vim had set.
        if (final == 'p' && parser.Intermediates.Length == 1 && parser.Intermediates[0] == (byte)'!')
        {
            SoftReset();
            return;
        }

        // DECSCA — CSI Ps " q. Marks what is typed NEXT as protected (Ps=1) or erasable (Ps=0,2),
        // which is what selective erase then honours. The '"' intermediate is what separates it
        // from every other 'q' final.
        if (final == 'q' && parser.Intermediates.Length == 1 && parser.Intermediates[0] == (byte)'"')
        {
            if (ConformanceLevel > 1)
            {
                ProtectedMode = parser.GetParam(0, 0) == 1;
            }
            return;
        }

        // DECSCL - CSI Ps " p, the same intermediate as DECSCA with a different final. Chooses
        // which terminal this behaves as. See SelectConformanceLevel for what the manual says.
        if (final == 'p' && parser.Intermediates.Length == 1 && parser.Intermediates[0] == (byte)'"'
            && Profile.Supports(TerminalFeatures.ConformanceLevels))
        {
            SelectConformanceLevel(parser.GetParam(0, 64), parser.GetParam(1, 0));
            return;
        }

        // The VT420 rectangle operations all carry a '$' intermediate, the same shape DECRQM uses.
        // Without this branch they fall into the switch below and land on whatever else owns their
        // final byte - 'x' is DECREQTPARM, 'v' and 'z' are nothing at all.
        if (parser.Intermediates.Length == 1 && parser.Intermediates[0] == (byte)'$'
            && Profile.Supports(TerminalFeatures.RectangleOperations) && !IsLevel1)
        {
            switch (final)
            {
                case 'x': FillRectangle(parameters); return;
                case 'z': EraseRectangle(parameters, selective: false); return;
                case '{': EraseRectangle(parameters, selective: true); return;
                case 'v': CopyRectangle(parameters); return;
                case 'r': ChangeAttributesInArea(parameters, reverse: false); return;
                case 't': ChangeAttributesInArea(parameters, reverse: true); return;
            }
        }

        // The status line - DECSASD and DECSSDT, from the VT330/VT340 Text Programming manual,
        // chapter 11. Both carry the same '$' intermediate as the rectangle family above but are
        // not gated with it: a VT320 has a status line and no rectangle operations at all.
        //
        // THE THREE DOCUMENTS DISAGREE ABOUT DECSSDT'S FINAL BYTE, and ECMA-48 settles it. xterm's
        // ctlseqs and the VT420 Programmer Reference both give "CSI Ps $ ~". The VT330/VT340
        // manual gives "CSI Ps $ -", and backs it with the character codes 2/4 2/13, which really
        // is a hyphen rather than a bad scan. But 2/13 is 0x2D, and 0x2D is in the INTERMEDIATE
        // range 0x20 to 0x2F - a control sequence cannot end there at all, so "CSI Ps $ -" would
        // leave the parser still waiting for a final byte. The tilde is 0x7E and is a proper final.
        // Two documents against one, and the standard agrees with the two: '~' it is.
        if (parser.Intermediates.Length == 1 && parser.Intermediates[0] == (byte)'$'
            && Profile.Supports(TerminalFeatures.StatusLine))
        {
            switch (final)
            {
                case '}': SelectActiveStatusDisplay(parser.GetParam(0, 0)); return;
                case '~': SelectStatusLineType(parser.GetParam(0, 1)); return;
            }
        }

        // DECRQM for ANSI modes - CSI Ps $ p. Claimed HERE rather than in the standard switch
        // below, because that switch now runs only for sequences with NO intermediate. It used to
        // sit in a case 'p' that tested the intermediate itself, which worked only as long as
        // anything carrying an intermediate still reached it.
        if (final == 'p' && parser.Intermediates.Length == 1 && parser.Intermediates[0] == (byte)'$')
        {
            HandleRequestAnsiMode(parser.GetParam(0, 0));
            return;
        }

        // DECRQCRA - CSI Pid ; Pp ; Pt ; Pl ; Pb ; Pr * y, VT400 mode only. Gated with the rest of
        // the rectangle family because it names a rectangle the same way they do, even though the
        // manual files it under Reports rather than Editing.
        if (final == 'y' && parser.Intermediates.Length == 1 && parser.Intermediates[0] == (byte)'*'
            && Profile.Supports(TerminalFeatures.RectangleOperations) && !IsLevel1)
        {
            ReportRectangleChecksum(parameters);
            return;
        }

        // DECIC and DECDC - CSI Pn ' } and CSI Pn ' ~. Whole columns of the scrolling region, which
        // is what separates them from ICH and DCH on the cursor's own row.
        if (parser.Intermediates.Length == 1 && parser.Intermediates[0] == (byte)'\''
            && Profile.Supports(TerminalFeatures.ColumnEditing) && !IsLevel1)
        {
            switch (final)
            {
                case '}': InsertColumns(parser.GetParam(0, 1)); return;
                case '~': DeleteColumns(parser.GetParam(0, 1)); return;
            }
        }

        // SL and SR - CSI Ps SP @ and CSI Ps SP A, ECMA-48. Not a DEC extension and not gated:
        // they shift the scrolling region sideways on any terminal here.
        //
        // DECSCUSR joins them: CSI Ps SP q, the cursor shape. Every modern editor sends it - vim
        // and neovim switch to a bar in insert mode and back to a block in normal mode - and it
        // was missing, so the cursor never changed shape. The style enum and the renderer that
        // reads it were both already there; only the sequence was not. The TDV2200's own dispatch
        // even carries a guard against swallowing this one, which is how it stayed hidden.
        if (parser.Intermediates.Length == 1 && parser.Intermediates[0] == (byte)' ')
        {
            switch (final)
            {
                case '@': ShiftRegionLeft(parser.GetParam(0, 1)); return;
                case 'A': ShiftRegionRight(parser.GetParam(0, 1)); return;
                case 'q': SetCursorStyle(parser.GetParam(0, 1)); return;
            }
        }

        // Page memory movement. PPA, PPB and PPR all carry a SPACE intermediate; NP and PP have
        // none and are handled in the main switch. From the VT420 Programmer Reference, chapter 6
        // ("Moving to Another Page", pages 136-139), which is held in spec\DEC.
        if (parser.Intermediates.Length == 1 && parser.Intermediates[0] == (byte)' '
            && Profile.Supports(TerminalFeatures.PageMemory))
        {
            switch (final)
            {
                case 'P': GoToPage(parser.GetParam(0, 1), keepCursor: true); return;              // PPA
                case 'R': GoToPage(CurrentPage - Math.Max(1, parser.GetParam(0, 1)), keepCursor: true); return;   // PPB
                case 'Q': GoToPage(CurrentPage + Math.Max(1, parser.GetParam(0, 1)), keepCursor: true); return;   // PPR
            }
        }

        // DECSACE - CSI Ps * x. Chooses whether the attribute commands above read their four
        // numbers as a RECTANGLE or as a STREAM. A different intermediate from the rest, because
        // it is a mode rather than an operation.
        if (final == 'x' && parser.Intermediates.Length == 1 && parser.Intermediates[0] == (byte)'*'
            && Profile.Supports(TerminalFeatures.RectangleOperations) && !IsLevel1)
        {
            SelectAttributeChangeExtent(parser.GetParam(0, 0));
            return;
        }

        // AN INTERMEDIATE IS PART OF A CONTROL'S IDENTITY, so anything still carrying one when it
        // reaches here is NOT the sequence the switch below would take it for. ECMA-48 builds a
        // control function's name out of its intermediates AND its final byte together; the final
        // byte alone does not name it.
        //
        // Every intermediate this terminal knows has already been claimed above - SPACE for SL, SR,
        // DECSCUSR and the page-memory moves, apostrophe for DECIC and DECDC, asterisk for DECSACE
        // and DECRQCRA, dollar for the rectangle operations. Falling through with one left means
        // nobody wanted it.
        //
        // Found 28 August 2026 by measuring the M6.1a comparison sheets. hackerb9's textcursor.six
        // opens with ESC [ 2 SP I. That is not CHT - CHT is CSI Ps I with no intermediate - but the
        // switch below dispatched on the final byte alone and ran it as CHT, moving the cursor two
        // tab stops. Two tab stops is column 16, a VT340 cell is 10 pixels wide, and the whole
        // picture landed 160 pixels right of where the hardware puts it. vaxrgl-lntest.six sends
        // ESC [ 7 SP I, which would be 560.
        //
        // Counted rather than obeyed, so it still shows up in UNHANDLED instead of vanishing.
        if (parser.Intermediates.Length > 0)
        {
            var shape = new StringBuilder("CSI ");
            if (privateMarker != 0)
            {
                shape.Append((char)privateMarker).Append(' ');
            }

            for (int i = 0; i < parser.Intermediates.Length; i++)
            {
                shape.Append((char)parser.Intermediates[i]).Append(' ');
            }

            shape.Append(final);
            CountUnrecognisedSequence(shape.ToString());
            return;
        }

        // Standard CSI sequences
        switch (final)
        {
            case 'A': // CUU - Cursor Up (stops at the top margin)
                MoveCursorUpWithinMargins(parser.GetParam(0, 1));
                break;

            case 'B': // CUD - Cursor Down (stops at the bottom margin)
                MoveCursorDownWithinMargins(parser.GetParam(0, 1));
                break;

            case 'C': // CUF - Cursor Forward
                Cursor.MoveForward(parser.GetParam(0, 1));
                break;

            case 'D': // CUB - Cursor Back
                Cursor.MoveBackward(parser.GetParam(0, 1));
                break;

            case 'E': // CNL - Cursor Next Line
                MoveCursorDownWithinMargins(parser.GetParam(0, 1));
                Cursor.CarriageReturn();
                break;

            case 'F': // CPL - Cursor Previous Line
                MoveCursorUpWithinMargins(parser.GetParam(0, 1));
                Cursor.CarriageReturn();
                break;

            case 'G': // CHA - Cursor Horizontal Absolute
                // NOT put through the column origin, deliberately. The VT420 manual says the
                // origin applies to CUP because it describes CUP; CHA and HPA are ECMA-48 and the
                // manual does not list them at all, so there is no document here saying either
                // way. Left alone rather than made consistent by guesswork - the same call the
                // VPR-is-not-CUD evidence taught. See SetCursorColumnFromHost.
                Cursor.Column = parser.GetParam(0, 1) - 1;
                break;

            case 'H': // CUP - Cursor Position
            case 'f': // HVP - Horizontal and Vertical Position
                // Both coordinates go through the origin, because the manual says both do: "The
                // starting point for lines and columns depends on the setting of origin mode."
                SetCursorRowFromHost(parser.GetParam(0, 1));
                SetCursorColumnFromHost(parser.GetParam(1, 1));
                break;

            case 'J': // ED - Erase in Display
                HandleEraseInDisplay(parser.GetParam(0, 0));
                break;

            case 'K': // EL - Erase in Line
                HandleEraseInLine(parser.GetParam(0, 0));
                break;

            case 'I': // CHT - Cursor Forward Tabulation. See TabulateToColumn for the wrap rule.
                TabulateToColumn(Math.Min(NextTabStop(Math.Max(1, parser.GetParam(0, 1))), Width - 1));
                break;

            case 'S':
                // SU - scroll the region up. DEC calls it Pan Down, because moving the window down
                // the page makes the text appear to move up: "Pn new lines appear at the bottom of
                // the display. Pn old lines disappear at the top." A parameter of 0 means 1.
                ScrollRegionUp(Math.Max(1, parser.GetParam(0, 1)));
                break;

            case 'T':
                // SD - scroll the region down, DEC's Pan Up. Told apart from xterm's highlight
                // mouse tracking, which is the same final byte with FIVE parameters, by there
                // being no second parameter at all.
                if (parser.GetParam(1, -1) == -1)
                {
                    ScrollRegionDown(Math.Max(1, parser.GetParam(0, 1)));
                }
                break;

            case '^':
                // SD again, and this one has a story. ECMA-48's 5th edition put SD on CSI Ps ^ by
                // mistake; the 2003 correction moved it to CSI Ps T, which DEC had been using all
                // along. xterm answers to BOTH, because software written against the erratum is
                // still out there, and this one has no highlight-mouse-tracking form to be told
                // apart from - so it needs no parameter count check.
                ScrollRegionDown(Math.Max(1, parser.GetParam(0, 1)));
                break;

            case 'L': // IL - Insert Lines (bounded by the active scrolling region)
                InsertLinesWithinMargins(Cursor.Row, parser.GetParam(0, 1));
                FinishLineEdit();
                break;

            case 'M': // DL - Delete Lines (bounded by the active scrolling region)
                DeleteLinesWithinMargins(Cursor.Row, parser.GetParam(0, 1));
                FinishLineEdit();
                break;

            case 'Z': // CBT - Cursor Backward Tabulation
                TabulateToColumn(PreviousTabStop(Math.Max(1, parser.GetParam(0, 1))));
                break;

            case 'g': // TBC - Tab Clear
                ClearTabStop(parser.GetParam(0, 0));
                break;

            case '@': // ICH - Insert Characters
                // Its opposite number DCH was implemented and this was not, so a host that
                // opened a gap to insert text simply overwrote what was there. ShiftCharactersRight
                // already did the work — it was only ever reachable through IRM insert mode.
                //
                // On the VT420's Table 4-1 of what a level 1 terminal ignores: a VT100 had no ICH,
                // and a host that asked for VT100 behaviour is entitled to be answered by one.
                if (IsLevel1) break;
                ShiftCharactersRight(Cursor.Row, Cursor.Column, parser.GetParam(0, 1));

                // ICH disarms the pending wrap, the same way IL and DL do - see FinishLineEdit.
                // t0050-ICH ends a line with a full 80 characters, so the wrap is armed at column
                // 79, then sends CSI 5 @ and "!@#". A real xterm puts the "!" AT column 79, over
                // the character the insert pushed off the end, and only then wraps for the "@".
                // Carrying the flag through the insert put all three on the next row instead.
                Cursor.ClearPendingWrap();
                break;

            case 'P': // DCH - Delete Characters
                DeleteCharacters(Cursor.Row, Cursor.Column, parser.GetParam(0, 1));
                break;

            case 'X': // ECH - Erase Characters. Also on Table 4-1: not a VT100 function.
                if (IsLevel1) break;
                EraseCharacters(Cursor.Row, Cursor.Column, parser.GetParam(0, 1));
                break;

            case 'd': // VPA - Vertical Position Absolute (honours origin mode, like CUP)
                SetCursorRowFromHost(parser.GetParam(0, 1));
                break;

            // ── The ECMA-48 position family that was missing ──────────────────────────────
            // CUP/CUU/CUD/CUF/CUB and VPA were implemented; their five siblings were not, so
            // `ESC[5\`` and friends did nothing at all and the cursor stayed where it was.
            // Found by libvterm's 11state_movecursor script. They are plain ECMA-48 — nothing
            // DEC-private about them — and cost one line each.

            case '`': // HPA - Horizontal Position Absolute. Same as CHA (ESC[G).
                Cursor.Column = parser.GetParam(0, 1) - 1;
                break;

            case 'a': // HPR - Horizontal Position Relative. Same as CUF (ESC[C).
                Cursor.MoveForward(parser.GetParam(0, 1));
                break;

            case 'j': // HPB - Horizontal Position Backward. Same as CUB (ESC[D).
                Cursor.MoveBackward(parser.GetParam(0, 1));
                break;

            case 'e': // VPR - Vertical Position Relative. NOT the same as CUD (ESC[B): the
                      // scrolling region does not stop it. See MoveCursorDownIgnoringMargins for
                      // the pair of fixtures that separate the two.
                MoveCursorDownIgnoringMargins(parser.GetParam(0, 1));
                break;

            // There is deliberately NO case 'k'. ECMA-48 defines CSI Pn k as VPB (Line Position
            // Backward), but no terminal this program emulates implements it: xterm's ctlseqs
            // lists CSI Ps e (VPR) and has no entry for k at all, and DEC's own terminals never
            // had it either. Acting on it moved the cursor up for a host that meant nothing by
            // the sequence, so a line of text ended up spread over several rows. Ignored.

            case 'm': // SGR - Select Graphic Rendition
                // Only the plain form is SGR. 'm' with a private marker is something else:
                // CSI > Pp ; Pv m is xterm's modifyOtherKeys, sent at startup by Claude Code, vim
                // and others. Read as SGR, its 4 switched underline on and its 2 switched dim on,
                // so everything drawn afterwards came out underlined. None of the private forms
                // is implemented here, so they are ignored.
                if (privateMarker == 0)
                {
                    HandleSgr(parameters);
                }
                break;

            case 'n': // DSR - Device Status Report
                HandleDeviceStatusReport(parser.GetParam(0, 0));
                break;

            case 'i': // MC - Media Copy
                HandleMediaCopy(parser.PrivateMarker, parser.GetParam(0, 0));
                break;

            case 'c': // DA - Device Attributes
                // CSI c / CSI 0 c = Primary DA, CSI > c = Secondary DA.
                // Without this the terminal stayed silent when a host probed it, which
                // is how most full-screen software decides what the terminal can do.
                HandleDeviceAttributes(parser.PrivateMarker, parser.GetParam(0, 0));
                break;

            case 'q': // DECLL - Load LEDs, the four lamps above a VT100 keyboard.
                      //
                      // Bare 'q'. With a SPACE intermediate the same final is DECSCUSR, the cursor
                      // style, and that is handled earlier - which is exactly why this one hid: the
                      // final looked taken.
                      //
                      // Found on 25 August 2026 by the session driving VTM on a real ND-100. VTM
                      // sends a parameterless CSI q on exit, which means "put all four lamps out" -
                      // a program tidying up after itself, reported as an unknown sequence every
                      // time it quit.
                      //
                      // The state is KEPT rather than swallowed. This emulator has no lamps to
                      // light, but a host is entitled to ask what they are set to, and a mode that
                      // is accepted and then forgotten is the failure this whole sweep was about.
                HandleLoadLeds(parameters);
                break;

            case 'r': // DECSTBM - Set Top and Bottom Margins (Scroll Region)
                var top = parser.GetParam(0, 1);
                var bottom = parser.GetParam(1, Height);

                // If no parameters provided (default values), clear scroll region
                if (top == 1 && bottom == Height)
                {
                    ClearScrollingRegion();
                }
                else
                {
                    SetScrollRegion(top, bottom);
                }
                break;

            case 'h': // SM - Set Mode (ANSI, no private marker)
            case 'l': // RM - Reset Mode
                HandleAnsiMode(final, parameters);
                break;


            case 'U': // NP - next page, cursor to the HOME position on it
                if (Profile.Supports(TerminalFeatures.PageMemory))
                {
                    GoToPage(CurrentPage + Math.Max(1, parser.GetParam(0, 0)), keepCursor: false);
                }
                break;

            case 'V': // PP - preceding page, cursor to the HOME position on it
                if (Profile.Supports(TerminalFeatures.PageMemory))
                {
                    GoToPage(CurrentPage - Math.Max(1, parser.GetParam(0, 0)), keepCursor: false);
                }
                break;

            case 't':
                // TWO COMMANDS, ONE FINAL BYTE, told apart by which terminal this is. On a DEC
                // terminal with page memory this is DECSLPP, which sets the lines per page and so
                // how many pages the 144 lines of memory divide into. On xterm it is window
                // manipulation. No terminal has both, so the feature flag decides.
                if (Profile.Supports(TerminalFeatures.PageMemory))
                {
                    SetLinesPerPage(parser.GetParam(0, 0));
                }
                else
                {
                    HandleWindowManipulation(parameters);
                }
                break;

            case 's':
                // TWO COMMANDS, ONE FINAL BYTE. With left and right margins enabled this is
                // DECSLRM, set margins; with the mode off it is SCOSC, save cursor. The mode flag
                // is the only thing that separates them, which is why DECLRMM has to be honoured
                // before this can be.
                if (LeftRightMarginMode)
                {
                    SetLeftRightMargins(parameters);
                }
                else
                {
                    Cursor.Save();
                }
                break;

            case 'u': // SCORC - Restore Cursor (SCO extension)
                Cursor.Restore();
                break;

            default:
                // Unknown CSI sequence. The PARAMETERS are deliberately left out of the key: a host
                // that sends CSI 1 q and CSI 2 q is using one sequence this terminal does not know,
                // not two, and a work list wants the sequence.
                CountUnrecognisedSequence(privateMarker == 0
                    ? "CSI " + final
                    : "CSI " + (char)privateMarker + " " + final);
                break;
        }
    }

    /// <summary>
    /// Sequences this terminal parsed but had no handler for, keyed by their shape.
    /// </summary>
    /// <remarks>
    /// <para><b>The gap this closes, found by running the thing</b></para>
    /// The ND graphics module and the ReGIS decoder each keep a counter, and <c>UNHANDLED</c>
    /// reported both. Pointed at real SINTRAN on 2026-08-20 it said "nothing unhandled" for a whole
    /// PED session while the protocol trace showed PED sending <c>ESC Q</c>, which nothing here
    /// understands.
    ///
    /// Both were telling the truth. Those two counters cover ND <c>ESC "</c> graphics modes and ReGIS
    /// command letters; a sequence the PARSER recognised the shape of and this class had no case for
    /// simply fell out of a <c>default:</c> in silence. That silence is where a real host's unknowns
    /// actually live, so it is counted now.
    ///
    /// A count rather than a log line, for the same reason as the other two: pointed at a real host
    /// it answers "what does this program use that we do not implement", which is a better guide to
    /// what to build next than reading a manual in order.
    /// </remarks>
    private readonly Dictionary<string, int> _unrecognisedSequences = new Dictionary<string, int>();

    /// <summary>
    /// Sequences this terminal parsed and had no handler for, with how often each arrived.
    /// </summary>
    public IReadOnlyDictionary<string, int> UnrecognisedSequences => _unrecognisedSequences;

    /// <summary>
    /// Records one sequence this terminal did not act on.
    /// </summary>
    /// <param name="shape">
    /// What arrived, written the way a person would say it - "ESC Q", "CSI ? h".
    /// </param>
    protected void CountUnrecognisedSequence(string shape)
    {
        if (string.IsNullOrEmpty(shape)) return;

        _unrecognisedSequences.TryGetValue(shape, out int count);
        _unrecognisedSequences[shape] = count + 1;
    }

    // ─────────────────────────────────────────────────────────────
    // Printing - media copy, printer controller mode and autoprint
    //
    // Designed in docs\PRINTING-SIXEL-TO-PDF-DESIGN-2026-08-17.md, from DEC STD 070 section 7.8
    // and xterm's ctlseqs. The printer hung off the TERMINAL, not the host: this is that port.
    // ─────────────────────────────────────────────────────────────

    private readonly Printing.PrinterControllerFilter _printerFilter =
        new Printing.PrinterControllerFilter();

    /// <summary>
    /// Where print jobs go, or null when no printer is attached.
    /// </summary>
    /// <remarks>
    /// With no sink the terminal still OBEYS the print commands - printer controller mode still
    /// swallows the stream rather than putting it on the screen - because that is what a terminal
    /// with an empty printer port did. Silently showing a print job on the screen would be worse
    /// than losing it: the host thinks it printed a page and the user sees garbage.
    /// </remarks>
    public Printing.IPrintSink? PrintSink { get; set; }

    /// <summary>
    /// True while <c>CSI 5 i</c> is in force and every byte is going to the printer.
    /// </summary>
    public bool PrinterControllerMode { get; private set; }

    /// <summary>
    /// True while <c>CSI ? 5 i</c> is in force, printing each line as it is completed.
    /// </summary>
    public bool AutoPrintMode { get; private set; }

    /// <summary>
    /// DECPFF, private mode 18 - send a form feed after a screen print.
    /// </summary>
    /// <remarks>
    /// Reset by default, which is what the VT420 manual gives as the power-up state.
    /// </remarks>
    public bool PrintFormFeedMode { get; private set; }

    /// <summary>
    /// DECPEX, private mode 19 - print the whole screen rather than only the scrolling region.
    /// </summary>
    /// <remarks>
    /// SET by default: the manual's power-up state is full screen, so a host that never mentions
    /// DECPEX gets the whole screen, which is what it expects.
    /// </remarks>
    public bool PrintExtentFullScreen { get; private set; } = true;

    /// <summary>
    /// MC - Media Copy. The way in to every kind of printing.
    /// </summary>
    /// <remarks>
    /// <para><b>The private marker is the whole difference</b></para>
    /// <c>CSI 5 i</c> and <c>CSI ? 5 i</c> are DIFFERENT COMMANDS. The first is printer controller
    /// mode, which makes the terminal a wire; the second is autoprint, which prints each line as
    /// it scrolls and leaves the screen working. Reading one as the other would send a host's
    /// whole session to the printer, or a print job to the screen.
    ///
    /// <para><b>Without the marker</b></para>
    ///  - <c>0</c> - print the screen, the default.
    ///  - <c>4</c> - printer controller mode off.
    ///  - <c>5</c> - printer controller mode on.
    ///  - <c>10</c>, <c>11</c> - xterm's HTML and SVG screen dumps. Not DEC, deliberately not here.
    ///
    /// <para><b>With the marker</b></para>
    ///  - <c>1</c> - print the line the cursor is on.
    ///  - <c>4</c> - autoprint off.
    ///  - <c>5</c> - autoprint on.
    ///  - <c>10</c> - print the composed display, ignoring DECPEX.
    ///  - <c>11</c> - print all pages.
    /// </remarks>
    /// <param name="privateMarker">
    /// The marker byte from the sequence, or zero when there was none.
    /// </param>
    /// <param name="parameter">
    /// The MC parameter.
    /// </param>
    protected virtual void HandleMediaCopy(byte privateMarker, int parameter)
    {
        if (privateMarker == (byte)'?')
        {
            switch (parameter)
            {
                case 1: // print the cursor's line
                    PrintRows(Cursor.Row, Cursor.Row);
                    break;

                case 4:
                    AutoPrintMode = false;
                    break;

                case 5:
                    AutoPrintMode = true;
                    break;

                case 10: // composed display - the whole screen whatever DECPEX says
                    PrintRows(0, Height - 1);
                    break;

                case 11: // all pages
                    PrintRows(0, Height - 1);
                    break;
            }

            return;
        }

        if (privateMarker != 0) return;   // some other marker is not a command this terminal has

        switch (parameter)
        {
            case 0: // print screen
                PrintScreen();
                break;

            case 4:
                if (PrinterControllerMode) EndPrinterControllerMode();
                break;

            case 5:
                PrinterControllerMode = true;
                _printerFilter.Reset();

                // Stop the parser HERE. Every byte after this sequence belongs to the printer, and
                // a parser that ran on to the end of the chunk would put the print job on screen.
                Parser.StopRequested = true;
                _parserStoppedEarly = true;
                break;
        }
    }

    /// <summary>
    /// Leaves printer controller mode and closes the job.
    /// </summary>
    private void EndPrinterControllerMode()
    {
        PrinterControllerMode = false;
        _printerFilter.Reset();
        PrintSink?.EndJob();
    }

    /// <summary>
    /// How a graphics print is encoded - the DEC-private print modes, gathered in one place.
    /// </summary>
    /// <remarks>
    /// DECGEPM (mode 43) writes <see cref="Printing.SixelPrintOptions.Expanded"/> and DECGPCM
    /// (mode 44) writes <see cref="Printing.SixelPrintOptions.Colour"/>. DECGRPM, the rotated
    /// print, is DEC's private mode 47 - which xterm uses for the alternate screen buffer, so it
    /// is NOT wired to a mode here. The option can still be set directly, and the collision is
    /// written up in the design document rather than decided by whoever wrote the code last.
    /// </remarks>
    public Printing.SixelPrintOptions GraphicsPrintOptions { get; } =
        new Printing.SixelPrintOptions();

    /// <summary>
    /// Dumps the graphics bitmap to the printer as sixel.
    /// </summary>
    /// <remarks>
    /// <para><b>One sink, three routes</b></para>
    /// This is what a VT did when asked for a graphics hard copy: it re-encoded its OWN bitmap as
    /// sixel and sent it out the printer port. Three different things ask for it - media copy,
    /// the ReGIS hardcopy command, and Tektronix mode's <c>ESC ETB</c> - and they all arrive here
    /// so there is one encoder and one set of print options rather than three.
    ///
    /// Does nothing when there is no printer or nothing has been drawn, which is what a terminal
    /// with an empty printer port did.
    /// </remarks>
    /// <returns>
    /// True when a picture was sent.
    /// </returns>
    public virtual bool PrintGraphics()
    {
        var sink = PrintSink;
        if (sink == null) return false;

        var graphics = Graphics;
        if (graphics == null || !graphics.HasAnythingToDraw()) return false;

        graphics.Composite();
        Printing.SixelEncoder.Encode(graphics.Output, GraphicsPrintOptions, sink);

        if (PrintFormFeedMode) sink.FormFeed();
        return true;
    }

    /// <summary>
    /// Prints the screen, honouring DECPEX and DECPFF.
    /// </summary>
    /// <remarks>
    /// DECPEX decides the extent: set means the whole screen, reset means the scrolling region
    /// only. DECPFF decides whether a form feed follows.
    /// </remarks>
    protected virtual void PrintScreen()
    {
        int first = PrintExtentFullScreen ? 0 : ScrollTop;
        int last = PrintExtentFullScreen ? Height - 1 : ScrollBottom;

        PrintRows(first, last);

        if (PrintFormFeedMode) PrintSink?.FormFeed();
    }

    /// <summary>
    /// Sends a range of screen rows to the printer as text.
    /// </summary>
    /// <remarks>
    /// <para><b>Text, not attributes</b></para>
    /// A screen print puts the characters on paper. Colours and bold have no meaning on the
    /// printers this models, and DEC's own screen print did not try to carry them.
    ///
    /// <para><b>Trailing blanks are trimmed</b></para>
    /// A terminal row is always <see cref="Width"/> cells whether or not anything was written to
    /// them, so printing them all would put eighty spaces after every short line.
    ///
    /// <para><b>Encoding</b></para>
    /// The rows go out as UTF-8, which is ASCII for every character these terminals could display
    /// and is at least well defined for anything a host loaded into a soft font.
    /// </remarks>
    /// <param name="firstRow">
    /// First row, zero based; clamped to the screen.
    /// </param>
    /// <param name="lastRow">
    /// Last row, zero based and inclusive; clamped to the screen.
    /// </param>
    protected void PrintRows(int firstRow, int lastRow)
    {
        var sink = PrintSink;
        if (sink == null) return;

        if (firstRow < 0) firstRow = 0;
        if (lastRow >= Height) lastRow = Height - 1;
        if (firstRow > lastRow) return;

        var line = new StringBuilder(Width + 2);

        for (int row = firstRow; row <= lastRow; row++)
        {
            line.Clear();

            int lastInk = -1;
            for (int col = 0; col < Width; col++)
            {
                if (!Buffer.TryGetCell(row, col, out var cell)) break;

                uint code = cell.Codepoint;
                char c = code == 0 ? ' ' : (char)code;
                line.Append(c);
                if (c != ' ') lastInk = col;
            }

            line.Length = lastInk + 1;      // drop the trailing blanks
            line.Append('\r');
            line.Append('\n');

            sink.Write(Encoding.UTF8.GetBytes(line.ToString()));
        }
    }

    /// <summary>
    /// SM/RM — the ANSI modes, the ones with no '?' marker (CSI Ps ... h/l).
    ///
    /// These were not handled at all. The consequence was not that the sequences were ignored in
    /// isolation: IRM (insert mode) and LNM (new-line mode) had fields, reset code and read sites
    /// in the base and in TDV2215, and NOTHING could ever set either of them to true. Insert mode
    /// was unreachable behaviour, so a host using IRM to insert text got overwrite instead.
    /// </summary>
    protected virtual void HandleAnsiMode(char final, ReadOnlySpan<int> parameters)
    {
        var enable = final == 'h';

        for (int i = 0; i < parameters.Length; i++)
        {
            SetAnsiMode(parameters[i], enable);
        }
    }

    /// <summary>
    /// The one-level cache XTSAVE writes and XTRESTORE reads.
    /// </summary>
    /// <remarks>
    /// One level, exactly as xterm documents: "Like Save Cursor (DECSC), this uses a one-level
    /// cache. Unlike Save Cursor, specific settings can be saved and restored independently."
    /// A dictionary rather than an array because the mode numbers are scattered from 1 to 2004
    /// and most of them are never saved.
    /// </remarks>
    private readonly Dictionary<int, bool> _savedPrivateModes = new Dictionary<int, bool>();

    /// <summary>
    /// XTSAVE - remembers the current value of each listed private mode.
    /// </summary>
    /// <remarks>
    /// A mode this terminal does not implement is not saved, so a later XTRESTORE cannot invent a
    /// value for it. Silently saving "false" for an unknown mode would turn a restore into a
    /// reset, which is worse than doing nothing.
    /// </remarks>
    /// <param name="modes">
    /// The mode numbers to save.
    /// </param>
    protected virtual void SavePrivateModes(ReadOnlySpan<int> modes)
    {
        for (int i = 0; i < modes.Length; i++)
        {
            bool? state = GetPrivateModeState(modes[i]);
            if (state.HasValue) _savedPrivateModes[modes[i]] = state.Value;
        }
    }

    /// <summary>
    /// XTRESTORE - puts each listed private mode back to what XTSAVE remembered.
    /// </summary>
    /// <remarks>
    /// "Only those modes listed as parameters are restored." A mode that was never saved is left
    /// alone rather than reset - the host asked to restore something it never stored, and guessing
    /// at a value would change state it did not ask about.
    /// </remarks>
    /// <param name="modes">
    /// The mode numbers to restore.
    /// </param>
    protected virtual void RestorePrivateModes(ReadOnlySpan<int> modes)
    {
        for (int i = 0; i < modes.Length; i++)
        {
            if (_savedPrivateModes.TryGetValue(modes[i], out bool state))
            {
                SetDecPrivateMode(modes[i], state);
            }
        }
    }

    /// <summary>
    /// Applies one ANSI mode.
    /// </summary>
    protected virtual void SetAnsiMode(int mode, bool enable)
    {
        switch (mode)
        {
            case 4: // IRM - Insert/Replace Mode
                InsertMode = enable;
                break;

            case 20: // LNM - Line Feed/New Line Mode
                NewLineMode = enable;
                break;

            default:
                // Not a mode this terminal implements. Counted WITH ITS NUMBER, unlike the sequence
                // counters above: for a mode the number IS the thing, and "CSI h" would tell nobody
                // anything. Real PED sends CSI 62;62 h and CSI 30;7;80 l, and those four numbers are
                // the work list.
                CountUnrecognisedSequence("mode " + mode + (enable ? " set" : " reset"));
                break;
        }
    }

    /// <summary>
    /// Reads the current state of an ANSI mode, for DECRQM. Null means not implemented.
    /// </summary>
    protected virtual bool? GetAnsiModeState(int mode)
    {
        switch (mode)
        {
            case 4: return InsertMode;
            case 20: return NewLineMode;
            default: return null;
        }
    }

    /// <summary>
    /// DECRQM for ANSI modes (CSI Ps $ p), answering CSI Ps ; Pv $ y.
    /// The private-marker form is <see cref="HandleRequestMode"/>.
    /// </summary>
    protected virtual void HandleRequestAnsiMode(int mode)
    {
        var state = GetAnsiModeState(mode);
        int value = state.HasValue ? (state.Value ? 1 : 2) : 0;
        SendResponse(Encoding.ASCII.GetBytes($"\x1b[{mode};{value}$y"));
    }

    /// <summary>
    /// DECSET/DECRST (CSI ? Ps ... h/l).
    ///
    /// A mode sequence may carry SEVERAL modes — "CSI ? 1 ; 25 h" sets both DECCKM and DECTCEM,
    /// and hosts do bundle them. Only the first parameter used to be applied, so every mode after
    /// the first in a bundle was silently dropped.
    /// </summary>
    protected virtual void HandleDecPrivateMode(char final, ReadOnlySpan<int> parameters)
    {
        var enable = final == 'h'; // 'h' = set, 'l' = reset

        // Anything that is not h or l is not a mode change at all, and this method would read it as
        // a RESET - see the note above the call site. Counted rather than acted on, so a private
        // sequence this terminal does not know shows up as itself instead of quietly resetting
        // whatever mode number happened to be in its parameters.
        if (final != 'h' && final != 'l')
        {
            CountUnrecognisedSequence("CSI ? " + final);
            return;
        }

        if (parameters.Length == 0)
        {
            SetDecPrivateMode(0, enable);
            return;
        }

        for (int i = 0; i < parameters.Length; i++)
        {
            SetDecPrivateMode(parameters[i], enable);
        }
    }

    /// <summary>
    /// Applies one DEC private mode. Split out from <see cref="HandleDecPrivateMode"/> so that
    /// bundled modes all take the same path as a single one.
    /// </summary>
    protected virtual void SetDecPrivateMode(int mode, bool enable)
    {
        switch (mode)
        {
            case 1: // DECCKM - Cursor Keys Mode
                ApplicationCursorKeys = enable;
                break;

            case 2: // DECANM - ANSI when set, VT52 when reset
                if (Profile.Supports(TerminalFeatures.Vt52Mode))
                {
                    // Only the RESET direction is a real instruction here. A host setting it is
                    // asking for ANSI, which is what this terminal already is.
                    Vt52Mode = !enable;
                }
                break;

            case 3: // DECCOLM - 132 columns when set, 80 when reset
                ApplyColumnMode(enable);
                break;

            case 43: // DECGEPM - expanded graphics print, each sixel sent twice horizontally
                GraphicsPrintOptions.Expanded = enable;
                break;

            case 44: // DECGPCM - colour graphics print when set, black and white when reset
                GraphicsPrintOptions.Colour = enable;
                break;

            case 46: // DECGPBM - graphic print background mode.
                     //
                     // Set means the background is printed too; reset means only the ink is, so a
                     // drawing comes out on white paper instead of as a solid rectangle with the
                     // picture cut out of it. Reset is the sane default and is what the encoder
                     // already assumed - this mode is what lets a host ask for the other one.
                     //
                     // Sits with 43 and 44 because all three are the same kind of thing: a switch
                     // that changes nothing on screen and only decides how PrintGraphics re-encodes
                     // the plane on its way to the printer port.
                GraphicsPrintOptions.PrintBackground = enable;
                break;

            // There is deliberately NO case 47 here, and this is SETTLED rather than pending.
            //
            // DEC's DECGRPM, the ROTATED graphics print, is private mode 47 - and so is xterm's
            // Alternate Screen Buffer, which this emulator implements. Ronny decided on 2026-08-17
            // that the xterm reading wins on EVERY profile, including the DEC graphics ones:
            // modern software sends 1047 and 1049, so honouring DEC here would buy almost nothing,
            // while a mis-read 47 would silently swap a user's screen mid-session.
            //
            // A host therefore cannot ask for a rotated print. GraphicsPrintOptions.Rotated is how
            // RetroTerm's own settings reach it. See
            // docs\PRINTING-SIXEL-TO-PDF-DESIGN-2026-08-17.md.

            case 5: // DECSCNM - light or dark screen
                // "When DECSCNM is set, the screen displays dark characters on a light background.
                // When DECSCNM is reset, the screen displays light characters on a dark
                // background." The flag rides out on the frame and the renderer swaps at draw
                // time, because the manual is explicit that "screen mode only affects how the data
                // appears on the screen. DECSCNM does not change the data in page memory."
                ReverseVideoMode = enable;
                break;

            case 18: // DECPFF - print form feed
                PrintFormFeedMode = enable;
                break;

            case 19: // DECPEX - print extent: set means the whole screen, reset means the
                     // scrolling region only.
                PrintExtentFullScreen = enable;
                break;

            case 80: // DECSDM - sixel display mode
                if (Profile.Supports(TerminalFeatures.Sixel))
                {
                    SixelScrollingDisabled = enable;
                }
                break;

            case 8: // DECARM - whether a key held down repeats. Enforced by the input path, which
                    // reads it through GetActiveModes, because what it changes is whether the second
                    // and later presses reach the host at all.
                AutoRepeatKeys = enable;
                break;

            case 4: // DECSCLM - smooth scroll. Only the picture is animated; the buffer still
                    // scrolls instantly. See the field for why that separation matters.
                SmoothScrollMode = enable;

                // Whichever way it was switched, the picture is up to date from here. Leaving a
                // count behind would make the next switch-on start already owing lines that
                // scrolled while the mode was off, and open on a screen from several seconds ago.
                System.Threading.Volatile.Write(ref _smoothScrollBacklog, 0);
                break;

            case 42: // DECNRCM - whether national replacement sets are honoured. Changes what the
                     // 'A' designator means: British NRCS when set, ISO Latin-1 when reset. See the
                     // field for why only that one designator moves.
                NationalReplacementSets = enable;
                break;

            case 38: // DECTEK - become a Tektronix 4010/4014. Set enters, reset leaves.
                     //
                     // The one this whole sweep started from: a VT340 was sent this on 25 August
                     // 2026, nothing listened, and the plot bytes printed as text across the screen.
                     // A real VT240/VT330/VT340 has 4014 emulation built in and a host is entitled
                     // to ask for it.
                SetTektronixMode(enable);
                break;

            case 66: // DECNKM - application keypad. The SAME state DECKPAM (ESC =) sets, reached by
                     // a different sequence: VT320 and later let a host set it as a mode as well as
                     // by the two-character form. One flag, because it is one mode - two would drift
                     // apart the first time only one of them was reset.
                ApplicationKeypad = enable;
                break;

            case 67: // DECBKM - the backarrow key sends BS when set, DEL when reset. Reaches the
                     // keyboard through GetActiveModes, so the mapper reads the emulator's state
                     // rather than the UI deciding for itself - the type-check chain that used to
                     // live in the view is exactly what GetActiveModes was built to end.
                BackarrowSendsBackspace = enable;
                break;

            case 69: // DECLRMM - left and right margin mode
                if (Profile.Supports(TerminalFeatures.LeftRightMargins))
                {
                    LeftRightMarginMode = enable;

                    // Turning the mode OFF discards the margins rather than remembering them. A
                    // host that turns it on again is entitled to the full width until it says
                    // otherwise, which is what the VT420 does.
                    if (!enable) ClearLeftRightMargins();
                }
                break;

            case 6: // DECOM - Origin Mode
                OriginMode = enable;
                // DECOM homes the cursor, and "home" now means the corner of whatever the cursor
                // can address: the top of the region in origin mode, the top of the screen out of
                // it. Cursor.Home() always went to the absolute corner, which put the cursor
                // OUTSIDE the region it had just been confined to.
                SetCursorRowFromHost(1);
                SetCursorColumnFromHost(1);
                break;

            case 7: // DECAWM - Auto Wrap Mode
                AutoWrapMode = enable;
                Cursor.AutoWrap = enable;
                break;

            case 45: // XTREVWRAP - reverse wraparound. See ReverseWrapMode and HandleBackspace.
                ReverseWrapMode = enable;
                break;

            case 12: // Start/Stop Blinking Cursor
                // TODO: Implement cursor blinking
                break;

            case 25: // DECTCEM - Text Cursor Enable Mode
                Cursor.Visible = enable;
                break;

            case 9: // X10 mouse compatibility - presses only
                Mouse.Mode = enable ? Input.MouseTrackingMode.PressOnly : Input.MouseTrackingMode.Off;
                break;

            case 1000: // Normal tracking - presses and releases
                Mouse.Mode = enable
                    ? Input.MouseTrackingMode.PressAndRelease
                    : Input.MouseTrackingMode.Off;
                break;

            case 1002: // Button-event tracking - adds movement while a button is held
                Mouse.Mode = enable ? Input.MouseTrackingMode.ButtonMotion : Input.MouseTrackingMode.Off;
                break;

            case 1003: // Any-event tracking - all movement
                Mouse.Mode = enable ? Input.MouseTrackingMode.AllMotion : Input.MouseTrackingMode.Off;
                break;

            case 1006: // SGR encoding. Changes the wire format, NOT which events are reported.
                Mouse.Encoding = enable ? Input.MouseEncoding.Sgr : Input.MouseEncoding.X10;
                break;

            case 1004: // Focus reporting
                FocusReporting = enable;
                break;

            case 2004: // Bracketed paste
                BracketedPasteMode = enable;
                break;

            case 47: // Alternate Screen Buffer (no clear on the way in - the oldest form)
            {
                // The saved cursor belongs to the SCREEN, so it is put aside and swapped back
                // when the screen changes. See _savedStateOfTheOtherScreen.
                bool wasAlternate = Buffer.IsUsingAlternateBuffer;
                if (enable)
                    Buffer.SwitchToAlternateBuffer(clearOnSwitch: false);
                else
                    Buffer.SwitchToPrimaryBuffer();
                if (Buffer.IsUsingAlternateBuffer != wasAlternate) ExchangeSavedStateBetweenScreens();
            }
            break;

            case 1047: // Alternate Screen Buffer, cleared when leaving it
            {
                bool wasAlternate = Buffer.IsUsingAlternateBuffer;
                if (enable)
                {
                    Buffer.SwitchToAlternateBuffer(clearOnSwitch: false);
                }
                else
                {
                    // 1047 clears the alternate screen on the way OUT, so an editor's leftovers
                    // do not reappear the next time something switches in.
                    if (Buffer.IsUsingAlternateBuffer) Buffer.Clear();
                    Buffer.SwitchToPrimaryBuffer();
                }
                if (Buffer.IsUsingAlternateBuffer != wasAlternate) ExchangeSavedStateBetweenScreens();
            }
            break;

            case 1048: // Save/Restore Cursor
                if (enable)
                    SaveTerminalState();
                else
                    RestoreTerminalState();
                break;

            case 1049: // Alternate Screen Buffer + Save/Restore Cursor
                       // The modern form, and the one full-screen programs actually use: save state, go to
                       // a freshly cleared alternate screen, and on the way back restore the primary
                       // screen AND the state, so the shell prompt is exactly where it was.
            {
                bool wasAlternate = Buffer.IsUsingAlternateBuffer;
                if (enable)
                {
                    // Saved BEFORE the exchange, so it lands in the main screen's own slot and
                    // is waiting there when the program comes back.
                    SaveTerminalState();
                    Buffer.SwitchToAlternateBuffer(clearOnSwitch: true);
                    if (Buffer.IsUsingAlternateBuffer != wasAlternate) ExchangeSavedStateBetweenScreens();
                    Cursor.Home();
                }
                else
                {
                    Buffer.SwitchToPrimaryBuffer();
                    // Exchanged BEFORE the restore, so what comes back is the main screen's own
                    // save rather than whatever the alternate screen happened to leave.
                    if (Buffer.IsUsingAlternateBuffer != wasAlternate) ExchangeSavedStateBetweenScreens();
                    RestoreTerminalState();
                }
            }
            break;

            default:
                // NOT A PRIVATE MODE THIS TERMINAL IMPLEMENTS, and until 25 August 2026 this was a
                // bare "break" with the comment "Unknown mode" - the sequence vanished without
                // trace. SetAnsiMode has counted its unknowns since the counter was built; the
                // private side never did, which left a hole exactly where DEC put nearly everything
                // interesting.
                //
                // What that cost: a VT340 was asked to enter Tektronix mode with ESC[?38h, printed
                // the plot bytes as text, and the counter said nothing at all. The whole point of
                // the counter is that a missing feature announces itself the first time a host asks
                // for it. It could not, for any private mode.
                //
                // KEYED "private mode N", not "mode N", because the two numbering spaces are
                // different and they overlap: ANSI mode 4 is IRM, private mode 4 is DECSCLM smooth
                // scroll. Sharing a key would add two unrelated things together and report a total
                // that means nothing.
                CountUnrecognisedSequence("private mode " + mode + (enable ? " set" : " reset"));
                break;
        }
    }

    protected virtual void HandleSgr(ReadOnlySpan<int> parameters)
    {
        if (parameters.Length == 0)
        {
            // SGR 0 - Reset to defaults
            CurrentAttributes = CharacterAttributes.None;
            CurrentForeground = TerminalColor.Default;
            CurrentBackground = TerminalColor.Default;
            return;
        }

        for (int i = 0; i < parameters.Length; i++)
        {
            var param = parameters[i];

            switch (param)
            {
                case 0: // Reset
                    CurrentAttributes = CharacterAttributes.None;
                    CurrentForeground = TerminalColor.Default;
                    CurrentBackground = TerminalColor.Default;
                    break;

                case 1: // Bold
                    CurrentAttributes = CurrentAttributes.SetAttribute(CharacterAttributes.Bold);
                    break;

                case 2: // Dim
                    CurrentAttributes = CurrentAttributes.SetAttribute(CharacterAttributes.Dim);
                    break;

                case 3: // Italic
                    CurrentAttributes = CurrentAttributes.SetAttribute(CharacterAttributes.Italic);
                    break;

                case 4: // Underline
                    CurrentAttributes = CurrentAttributes.SetAttribute(CharacterAttributes.Underline);
                    break;

                case 5: // Blink (slow, ECMA-48 "slowly blinking, less than 150 per minute")
                    CurrentAttributes = CurrentAttributes.SetAttribute(CharacterAttributes.Blink);
                    break;

                case 6: // Rapid blink (ECMA-48 "rapidly blinking, 150 per minute or more")
                    // The RapidBlink flag existed in CharacterAttributes from the start and
                    // nothing ever set it, because this case was simply absent — SGR 6 fell
                    // through and a host asking for rapid blink got no blink at all.
                    CurrentAttributes = CurrentAttributes.SetAttribute(CharacterAttributes.RapidBlink);
                    break;

                case 7: // Reverse
                    CurrentAttributes = CurrentAttributes.SetAttribute(CharacterAttributes.Reverse);
                    break;

                case 8: // Hidden
                    CurrentAttributes = CurrentAttributes.SetAttribute(CharacterAttributes.Hidden);
                    break;

                case 9: // Strikethrough
                    CurrentAttributes = CurrentAttributes.SetAttribute(CharacterAttributes.Strikethrough);
                    break;

                case 22: // Normal intensity (not bold/dim)
                    CurrentAttributes = CurrentAttributes.ClearAttribute(CharacterAttributes.Bold | CharacterAttributes.Dim);
                    break;

                case 23: // Not italic
                    CurrentAttributes = CurrentAttributes.ClearAttribute(CharacterAttributes.Italic);
                    break;

                case 24: // Not underlined
                    CurrentAttributes = CurrentAttributes.ClearAttribute(CharacterAttributes.Underline);
                    break;

                case 25: // Not blinking — ECMA-48 turns off BOTH rates, not just the slow one.
                    // It used to clear Blink only, so SGR 25 could not switch off a rapid
                    // blink; that mattered less while nothing set RapidBlink, and matters now.
                    CurrentAttributes = CurrentAttributes.ClearAttribute(
                        CharacterAttributes.Blink | CharacterAttributes.RapidBlink);
                    break;

                case 27: // Not reversed
                    CurrentAttributes = CurrentAttributes.ClearAttribute(CharacterAttributes.Reverse);
                    break;

                case 28: // Not hidden
                    CurrentAttributes = CurrentAttributes.ClearAttribute(CharacterAttributes.Hidden);
                    break;

                case 29: // Not strikethrough
                    CurrentAttributes = CurrentAttributes.ClearAttribute(CharacterAttributes.Strikethrough);
                    break;

                case >= 30 and <= 37: // Set foreground color (standard)
                    CurrentForeground = TerminalColor.FromIndex((byte)(param - 30));
                    break;

                case 38: // Set foreground color (extended)
                    i = HandleExtendedColor(parameters, i, true);
                    break;

                case 39: // Default foreground
                    CurrentForeground = TerminalColor.Default;
                    break;

                case >= 40 and <= 47: // Set background color (standard)
                    CurrentBackground = TerminalColor.FromIndex((byte)(param - 40));
                    break;

                case 48: // Set background color (extended)
                    i = HandleExtendedColor(parameters, i, false);
                    break;

                case 49: // Default background
                    CurrentBackground = TerminalColor.Default;
                    break;

                case >= 90 and <= 97: // Set bright foreground color
                    CurrentForeground = TerminalColor.FromIndex((byte)(param - 90 + 8));
                    break;

                case >= 100 and <= 107: // Set bright background color
                    CurrentBackground = TerminalColor.FromIndex((byte)(param - 100 + 8));
                    break;

                default:
                    // Unknown SGR parameter
                    break;
            }
        }
    }

    protected virtual int HandleExtendedColor(ReadOnlySpan<int> parameters, int index, bool isForeground)
    {
        if (index + 1 >= parameters.Length)
            return index;

        var colorType = parameters[index + 1];

        if (colorType == 5 && index + 2 < parameters.Length)
        {
            // 256-color mode: SGR 38;5;N or SGR 48;5;N
            var colorIndex = (byte)parameters[index + 2];
            if (isForeground)
                CurrentForeground = TerminalColor.FromIndex(colorIndex);
            else
                CurrentBackground = TerminalColor.FromIndex(colorIndex);
            return index + 2;
        }
        else if (colorType == 2 && index + 4 < parameters.Length)
        {
            // RGB mode: SGR 38;2;R;G;B or SGR 48;2;R;G;B
            var r = (byte)parameters[index + 2];
            var g = (byte)parameters[index + 3];
            var b = (byte)parameters[index + 4];
            if (isForeground)
                CurrentForeground = TerminalColor.FromRgb(r, g, b);
            else
                CurrentBackground = TerminalColor.FromRgb(r, g, b);
            return index + 4;
        }

        return index + 1;
    }

    protected virtual void HandleEraseInDisplay(int mode) => HandleEraseInDisplay(mode, selective: false);

    /// <summary>
    /// ED (<c>CSI J</c>) and its selective twin DECSED (<c>CSI ? J</c>).
    /// </summary>
    /// <param name="mode">
    /// Which part of the screen to erase: 0 to the end, 1 to the start, 2 the whole screen,
    /// 3 the scrollback as well.
    /// </param>
    /// <param name="selective">
    /// When true, characters marked protected by DECSCA are left standing. That is the whole
    /// point of the pair: a host draws a form once, then clears what the user typed without
    /// wiping the labels.
    /// </param>
    protected virtual void HandleEraseInDisplay(int mode, bool selective)
    {
        if (!selective)
        {
            switch (mode)
            {
                case 0: // Erase from cursor to end of display
                    Buffer.ClearToEndOfLine(Cursor.Row, Cursor.Column);
                    for (int row = Cursor.Row + 1; row < Height; row++)
                    {
                        Buffer.ClearLine(row);
                    }
                    break;

                case 1: // Erase from start of display to cursor
                    for (int row = 0; row < Cursor.Row; row++)
                    {
                        Buffer.ClearLine(row);
                    }
                    Buffer.ClearFromStartOfLine(Cursor.Row, Cursor.Column);
                    break;

                case 2: // Erase All - the visible screen, and only that
                    Buffer.Clear();
                    // The picture goes too. This is what a shell's `clear` sends, and it
                    // is the half Ronny caught live on 31 August: he ran clear after a
                    // Sixel picture and watched the picture stay put. On a real VT340 the
                    // text and the graphics are one bitmap, so erasing takes both.
                    //
                    // Only mode 2 clears the planes. Mode 0 and mode 1 erase from or to
                    // the cursor, and the planes have no notion of a cursor position to
                    // erase from; mode 3 is the scrollback, which the picture is not part
                    // of. Wiping the whole picture for any of those would destroy more
                    // than was asked for.
                    ClearGraphicsPlanes();
                    break;

                case 3:
                    // ERASE SAVED LINES, and nothing else. The saved lines are the SCROLLBACK, so
                    // the visible screen must survive - xterm's ctlseqs.txt names 2 "Erase All"
                    // and 3 "Erase Saved Lines" as separate things.
                    //
                    // This used to fall through with 2 and wipe the screen as well, which is the
                    // difference between "clear my history" and "clear my history AND everything I
                    // am currently reading". A shell that trims its scrollback would have taken the
                    // screen with it.
                    Buffer.ClearScrollback();
                    break;
            }

            return;
        }

        switch (mode)
        {
            case 0:
                EraseUnprotected(Cursor.Row, Cursor.Column, Width - 1);
                for (int row = Cursor.Row + 1; row < Height; row++)
                {
                    EraseUnprotected(row, 0, Width - 1);
                }
                break;

            case 1:
                for (int row = 0; row < Cursor.Row; row++)
                {
                    EraseUnprotected(row, 0, Width - 1);
                }
                EraseUnprotected(Cursor.Row, 0, Cursor.Column);
                break;

            case 2:
                for (int row = 0; row < Height; row++)
                {
                    EraseUnprotected(row, 0, Width - 1);
                }
                break;

            case 3:
                // Selective Erase Saved Lines. The saved lines are history: nothing in the
                // scrollback is protected, because protection is an attribute of what is on screen
                // now. So this is the same as the non-selective form - the SCREEN is left alone.
                Buffer.ClearScrollback();
                break;
        }
    }

    protected virtual void HandleEraseInLine(int mode) => HandleEraseInLine(mode, selective: false);

    /// <summary>
    /// EL (<c>CSI K</c>) and its selective twin DECSEL (<c>CSI ? K</c>).
    /// See <see cref="HandleEraseInDisplay(int, bool)"/> for what <paramref name="selective"/> means.
    /// </summary>
    /// <param name="mode">
    /// Which part of the line to erase: 0 to the end, 1 to the start, 2 the whole line.
    /// </param>
    /// <param name="selective">
    /// When true, characters marked protected by DECSCA are left standing.
    /// </param>
    protected virtual void HandleEraseInLine(int mode, bool selective)
    {
        // Erasing part of the line disarms the pending wrap. t0055-EL fills all 80 columns, which
        // arms the flag at column 79, then sends CSI K and "!". A real xterm erases that last
        // character and writes the "!" in its place, on the SAME row; carrying the flag through
        // the erase pushed the "!" onto the next row.
        //
        // The fixture only proves it for mode 0, but the reason covers all three: the character
        // that armed the flag has just been erased, so the wrap it asked for means nothing.
        Cursor.ClearPendingWrap();

        if (!selective)
        {
            switch (mode)
            {
                case 0: // Erase from cursor to end of line
                    Buffer.ClearToEndOfLine(Cursor.Row, Cursor.Column);
                    break;

                case 1: // Erase from start of line to cursor
                    Buffer.ClearFromStartOfLine(Cursor.Row, Cursor.Column);
                    break;

                case 2: // Erase entire line
                    Buffer.ClearLine(Cursor.Row);
                    break;
            }

            return;
        }

        switch (mode)
        {
            case 0:
                EraseUnprotected(Cursor.Row, Cursor.Column, Width - 1);
                break;

            case 1:
                EraseUnprotected(Cursor.Row, 0, Cursor.Column);
                break;

            case 2:
                EraseUnprotected(Cursor.Row, 0, Width - 1);
                break;
        }
    }

    /// <summary>
    /// Clears cells from <paramref name="startColumn"/> to <paramref name="endColumn"/> inclusive,
    /// skipping any marked protected by DECSCA.
    ///
    /// The protected flag itself survives — a selective erase clears the CONTENT of a field, it
    /// does not un-protect one, so a form can be cleared and refilled any number of times.
    /// </summary>
    private void EraseUnprotected(int row, int startColumn, int endColumn)
    {
        if (row < 0 || row >= Height) return;
        if (startColumn < 0) startColumn = 0;
        if (endColumn > Width - 1) endColumn = Width - 1;

        for (int col = startColumn; col <= endColumn; col++)
        {
            ref var cell = ref Buffer[row, col];
            if ((cell.Attributes & CharacterAttributes.Protected) != 0)
            {
                continue;
            }

            var lineSize = cell.Attributes & LineSizeAttributes;
            cell = TerminalCell.Empty;
            cell.Attributes = lineSize;     // the LINE's size is not the character's to erase
        }
    }

    /// <summary>
    /// Which mouse events this terminal reports, and how they are written.
    /// </summary>
    /// <remarks>
    /// Owned here rather than by the UI because it is host state: the host turns it on with a mode
    /// sequence and every surface that has a pointer has to obey the same setting.
    /// </remarks>
    public Input.MouseTracker Mouse { get; } = new Input.MouseTracker();

    /// <summary>
    /// Reports a mouse event to the host, if the host asked for that kind.
    /// </summary>
    /// <remarks>
    /// Silent when tracking is off, when the event is not one the current mode reports, and when
    /// the position cannot be written in the current encoding. That last one is not a failure to
    /// paper over: the X10 format has one byte per coordinate, so past column 223 there is no way
    /// to say where the pointer is, and a wrong cell is worse than none.
    /// </remarks>
    /// <param name="kind">
    /// What happened to the mouse.
    /// </param>
    /// <param name="button">
    /// Which button, or <see cref="Input.MouseButton.None"/> when moving with nothing held.
    /// </param>
    /// <param name="column">
    /// Column, counting from 1 at the left.
    /// </param>
    /// <param name="row">
    /// Row, counting from 1 at the top.
    /// </param>
    /// <param name="modifiers">
    /// Modifier keys held at the time.
    /// </param>
    /// <returns>
    /// True when a report was sent.
    /// </returns>
    public bool ReportMouse(Input.MouseEventKind kind, Input.MouseButton button, int column, int row,
        Input.MouseModifiers modifiers = Input.MouseModifiers.None)
    {
        Span<byte> report = stackalloc byte[Input.MouseTracker.MaximumReportLength];

        if (!Mouse.TryBuildReport(kind, button, column, row, modifiers, report, out int length))
        {
            return false;
        }

        SendResponse(report.Slice(0, length));
        return true;
    }

    /// <summary>
    /// Whether the host asked for pasted text to arrive wrapped in brackets (DEC private mode 2004).
    /// </summary>
    /// <remarks>
    /// A shell with this on can tell typed input from pasted input, which is the difference between
    /// a pasted command being shown for the user to look at and it running the moment the newline
    /// inside it arrives. Off by default: a host that has not asked for the brackets would print
    /// them.
    /// </remarks>
    public bool BracketedPasteMode { get; protected set; }

    /// <summary>
    /// Whether the host asked to be told when this terminal gains or loses focus (mode 1004).
    /// </summary>
    public bool FocusReporting { get; protected set; }

    /// <summary>
    /// Wraps text about to be pasted, so a host that asked for bracketed paste gets it.
    /// </summary>
    /// <remarks>
    /// <para><b>Why the wrapping lives here</b></para>
    /// Only the emulator knows whether the mode is on, and paste arrives from more than one place -
    /// the keyboard shortcut, the menu, and anything driving the terminal from outside. A wrap
    /// written at one of those call sites would be right there and missing everywhere else, which
    /// is the trap this repo has already been caught by once.
    ///
    /// <para><b>The end marker is removed from the text</b></para>
    /// Whatever is on the clipboard came from somewhere, and it may contain the end marker itself.
    /// Left in, it would close the bracket early and everything after it would arrive as if the
    /// user had typed it - which is the whole thing bracketed paste exists to prevent. So the
    /// marker is stripped from the payload rather than trusted.
    /// </remarks>
    /// <param name="text">
    /// The text about to be sent.
    /// </param>
    /// <returns>
    /// The text unchanged when the mode is off, or wrapped in the paste brackets when it is on.
    /// </returns>
    public string WrapForPaste(string text)
    {
        if (string.IsNullOrEmpty(text) || !BracketedPasteMode) return text;

        const string start = "\x1b[200~";
        const string end = "\x1b[201~";

        string safe = text.Contains(end) ? text.Replace(end, "") : text;
        return start + safe + end;
    }

    /// <summary>
    /// Tells the host this terminal has gained or lost focus, when it asked to be told (mode 1004).
    /// </summary>
    /// <remarks>
    /// Sends <c>CSI I</c> on focus in and <c>CSI O</c> on focus out. Silent when the mode is off:
    /// an unasked-for report arrives in the middle of whatever the host was reading, and a shell
    /// that never enabled it would print the letter.
    /// </remarks>
    /// <param name="focused">
    /// True when the terminal has just gained focus.
    /// </param>
    public void ReportFocusChange(bool focused)
    {
        if (!FocusReporting) return;

        SendResponse(focused ? "\x1b[I"u8 : "\x1b[O"u8);
    }

    /// <summary>
    /// The icon name a host set with OSC 1, or OSC 0 which sets both this and the title.
    /// </summary>
    /// <remarks>
    /// Kept separate from <see cref="Title"/> because a host can set them to different things, and
    /// a window manager showing the icon name when minimised is entitled to the one it was given.
    /// </remarks>
    public string IconName { get; protected set; } = "RetroTerm";

    /// <summary>
    /// The working directory a host reported with OSC 7, or an empty string.
    /// </summary>
    /// <remarks>
    /// Reported as a file URI by every shell that sends it. Stored verbatim rather than parsed:
    /// this terminal does nothing with it yet, and turning it into a path would be inventing a
    /// meaning for a string it has not been asked to act on.
    /// </remarks>
    public string ReportedWorkingDirectory { get; protected set; } = "";

    /// <summary>
    /// The colour this terminal actually paints text in, for answering a host's OSC 10 query.
    /// </summary>
    /// <remarks>
    /// Set by the UI from the presentation theme. It lives here rather than being read from a theme
    /// because Core has no theme - the renderer owns that - and a terminal that answered the query
    /// from a constant would be telling a host a colour it does not paint. The defaults are the
    /// renderer's own starting colours, so an emulator with no UI attached still answers honestly.
    /// </remarks>
    public (byte R, byte G, byte B) DisplayForeground
    {
        get => _displayForeground;
        set
        {
            _displayForeground = value;

            // A terminal that draws is a terminal with ONE beam. When the screen becomes amber the
            // vectors have to become amber too, or a plot stays green on an amber display - which
            // no single-phosphor machine could have done. See OnDisplayColoursChanged.
            OnDisplayColoursChanged();
        }
    }

    private (byte R, byte G, byte B) _displayForeground = (0, 255, 136);

    /// <summary>
    /// Called when the colours this terminal paints in change.
    /// </summary>
    /// <remarks>
    /// Terminals that draw vectors override this to repaint in the new colour. Sixel deliberately
    /// does NOT: an image carries its own colours and they are the host's, not the display's.
    /// </remarks>
    protected virtual void OnDisplayColoursChanged()
    {
        // A DECTEK plot follows the phosphor, because a storage tube has ONE beam. This program has
        // already shipped the other way round once - amber text beside a green graph, which no
        // single-phosphor machine can do - and that happened because a draw colour was a constant
        // that happened to match the default.
        if (_tektronixPlotter != null)
        {
            var (r, g, b) = DisplayForeground;
            _tektronixPlotter.DrawColour = new Graphics.GraphicsColor(r, g, b);
        }
    }

    /// <summary>
    /// The colour this terminal paints the background in. See <see cref="DisplayForeground"/>.
    /// </summary>
    /// <remarks>
    /// This is the one full-screen programs care about most: it is how vim and tmux decide whether
    /// they are on a dark or a light terminal, and a wrong answer gives an unreadable colour scheme.
    /// </remarks>
    public (byte R, byte G, byte B) DisplayBackground { get; set; } = (0, 25, 17);

    /// <summary>
    /// The colour the cursor is drawn in, for OSC 12. Defaults to the text colour.
    /// </summary>
    public (byte R, byte G, byte B) DisplayCursorColour { get; set; } = (0, 255, 136);

    /// <summary>
    /// Handles one OSC string: <c>OSC Ps ; Pt ST</c>.
    /// </summary>
    /// <remarks>
    /// <para><b>What is answered</b></para>
    /// The colour queries, because a host asking "what colour is your background" gets a real
    /// answer from this terminal rather than silence:
    ///  - <c>OSC 4 ; index ; ?</c> - a palette entry.
    ///  - <c>OSC 10 ; ?</c>, <c>OSC 11 ; ?</c>, <c>OSC 12 ; ?</c> - text, background, cursor.
    /// The reply format is xterm's: <c>rgb:RRRR/GGGG/BBBB</c>, four hex digits per component with
    /// the eight-bit value doubled. Terminated with ST rather than BEL - the parser hands over the
    /// payload without saying which terminator arrived, and ST is the form the standard defines.
    ///
    /// <para><b>What is stored but not acted on</b></para>
    /// OSC 1 icon name and OSC 7 working directory. Both are recorded on this object; nothing
    /// displays them yet, and inventing a use for them would be worse than keeping the fact.
    ///
    /// <para><b>What is deliberately not implemented</b></para>
    ///  - SETTING a colour, meaning OSC 4/10/11/12 with a value rather than a question mark. The
    ///    palette a session paints from is built once by the renderer, so honouring this needs the
    ///    override store and the repaint that goes with it - a change across two layers, not a
    ///    parser case. Until then the queries answer truthfully and the sets are ignored, which is
    ///    better than accepting a colour and painting something else.
    ///  - OSC 52, clipboard access. It lets the host at the other end of a connection READ what is
    ///    on this machine's clipboard. That is a decision for the person at the keyboard, not
    ///    something to switch on because the sequence exists.
    /// </remarks>
    /// <param name="data">
    /// The OSC payload, introducer and terminator already stripped.
    /// </param>
    protected virtual void HandleOscSequence(ReadOnlySpan<byte> data)
    {
        var str = Encoding.UTF8.GetString(data);

        // A parameterless OSC has no semicolon at all. Returning early on that used to drop them
        // before anything could look at the command number.
        var semicolon = str.IndexOf(';');
        var command = semicolon < 0 ? str : str.Substring(0, semicolon);
        var text = semicolon < 0 ? "" : str.Substring(semicolon + 1);

        switch (command)
        {
            case "0": // Set icon name AND window title
                Title = text;
                IconName = text;
                TitleChanged?.Invoke(Title);
                break;

            case "2": // Set window title
                Title = text;
                TitleChanged?.Invoke(Title);
                break;

            case "1": // Set icon name
                IconName = text;
                break;

            case "4": // Palette entry: "index;?" to ask, "index;spec" to set
                HandlePaletteOsc(text);
                break;

            case "7": // Working directory, as a file URI
                ReportedWorkingDirectory = text;
                break;

            case "10": // Text colour
                if (IsColourQuery(text)) SendColourReply("10", DisplayForeground);
                break;

            case "11": // Background colour
                if (IsColourQuery(text)) SendColourReply("11", DisplayBackground);
                break;

            case "12": // Cursor colour
                if (IsColourQuery(text)) SendColourReply("12", DisplayCursorColour);
                break;

            default:
                // Everything else, OSC 52 included. See the remarks: silence is the honest answer
                // where acting would mean inventing behaviour or handing over the clipboard.
                break;
        }
    }

    /// <summary>
    /// Whether an OSC colour parameter is a question rather than a value.
    /// </summary>
    /// <param name="text">
    /// The parameter text after the command number.
    /// </param>
    /// <returns>
    /// True when the host is asking what the colour is.
    /// </returns>
    private static bool IsColourQuery(string text) => text == "?";

    /// <summary>
    /// Answers <c>OSC 4 ; index ; ?</c> from the palette this terminal reports.
    /// </summary>
    /// <param name="text">
    /// The parameter text: an index, then either a colour or a question mark.
    /// </param>
    private void HandlePaletteOsc(string text)
    {
        var separator = text.IndexOf(';');
        if (separator < 0) return;

        var indexText = text.Substring(0, separator);
        var value = text.Substring(separator + 1);
        if (!IsColourQuery(value)) return;

        if (!int.TryParse(indexText, System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out int index))
        {
            return;
        }

        // Only the 256 indexed colours exist. An index outside them is not a colour this terminal
        // has, so it says nothing rather than answering for a different one.
        if (index < 0 || index > 255) return;

        // Fully qualified: this class has a Buffer PROPERTY, which shadows the namespace the
        // palette lives in.
        var (r, g, b) = RetroTerm.Core.Terminal.Buffer.TerminalPalette.GetRgb((byte)index);
        SendResponse(Encoding.ASCII.GetBytes(
            "\x1b]4;" + indexText + ";" + FormatRgb(r, g, b) + "\x1b\\"));
    }

    /// <summary>
    /// Sends one of the OSC 10/11/12 colour replies.
    /// </summary>
    /// <param name="command">
    /// The OSC command number being answered.
    /// </param>
    /// <param name="colour">
    /// The colour to report.
    /// </param>
    private void SendColourReply(string command, (byte R, byte G, byte B) colour)
    {
        SendResponse(Encoding.ASCII.GetBytes(
            "\x1b]" + command + ";" + FormatRgb(colour.R, colour.G, colour.B) + "\x1b\\"));
    }

    /// <summary>
    /// Formats a colour the way xterm answers a query: four hex digits per component.
    /// </summary>
    /// <remarks>
    /// The eight-bit value is doubled rather than shifted - 0xCD becomes "cdcd" - so that the
    /// sixteen-bit form scales across its whole range. Shifting would put every colour slightly
    /// dark, with white coming back as fefe rather than ffff.
    /// </remarks>
    /// <param name="r">
    /// Red component.
    /// </param>
    /// <param name="g">
    /// Green component.
    /// </param>
    /// <param name="b">
    /// Blue component.
    /// </param>
    /// <returns>
    /// The colour as <c>rgb:RRRR/GGGG/BBBB</c>.
    /// </returns>
    private static string FormatRgb(byte r, byte g, byte b)
    {
        return "rgb:" + Doubled(r) + "/" + Doubled(g) + "/" + Doubled(b);

        static string Doubled(byte value)
        {
            var hex = value.ToString("x2", System.Globalization.CultureInfo.InvariantCulture);
            return hex + hex;
        }
    }

    /// <summary>
    /// Answers DA (Device Attributes). The base identifies as a VT100 with the
    /// Advanced Video Option — "\x1b[?1;2c" — which is what a real VT100 returns and
    /// what the TDV emulators already report through their own query path.
    /// Derived classes override this to report their own conformance level.
    /// </summary>
    /// <param name="privateMarker">
    /// The CSI private marker: 0 for Primary DA, '>' for Secondary DA.
    /// </param>
    /// <param name="parameter">
    /// The first CSI parameter; anything other than 0 is ignored per DEC.
    /// </param>
    protected virtual void HandleDeviceAttributes(byte privateMarker, int parameter)
    {
        // DEC: a DA request carries either no parameter or an explicit 0. Any other
        // value is not a DA request and must be ignored rather than answered.
        if (parameter != 0)
        {
            return;
        }

        // The reply comes from the profile, not from a constant in the base. A hardcoded VT100
        // answer here is what made every VT-family terminal claim to be a VT100 regardless of
        // which one the user asked for.
        if (privateMarker == (byte)'>')
        {
            SendResponse(Profile.SecondaryDeviceAttributes);
            return;
        }

        if (privateMarker == 0)
        {
            SendResponse(Profile.PrimaryDeviceAttributes);
        }
    }

    /// <summary>
    /// Reads the current state of a DEC private mode, for DECRQM. Returns null when this emulator
    /// does not implement the mode, which DECRQM reports differently from "reset".
    ///
    /// The modes live as loose bools spread across the base and its subclasses; this is the one
    /// place that maps a mode number onto them. Derived terminals override it to add their own,
    /// calling the base for everything shared.
    /// </summary>
    protected virtual bool? GetPrivateModeState(int mode)
    {
        switch (mode)
        {
            case 1: return ApplicationCursorKeys;      // DECCKM
            case 2: return !Vt52Mode;                  // DECANM: set means ANSI
            case 69: return LeftRightMarginMode;       // DECLRMM
            case 5: return ReverseVideoMode;           // DECSCNM
            case 18: return PrintFormFeedMode;         // DECPFF
            case 19: return PrintExtentFullScreen;     // DECPEX
            case 43: return GraphicsPrintOptions.Expanded;   // DECGEPM
            case 44: return GraphicsPrintOptions.Colour;     // DECGPCM
            case 46: return GraphicsPrintOptions.PrintBackground; // DECGPBM
            case 80: return SixelScrollingDisabled;    // DECSDM
            // DECSCLM. It was MISSING from this list until 11 September 2026, so a host asking
            // about smooth scroll was told "not recognised" by a terminal that implements it - see
            // the DECSCLM case in SetDecPrivateMode. That is exactly the confusion DECRQM's 0 and
            // 2 exist to keep apart.
            case 4: return SmoothScrollMode;           // DECSCLM
            case 3: return Width == 132;               // DECCOLM, inferred from the actual width
            case 9: return Mouse.Mode == Input.MouseTrackingMode.PressOnly;
            case 1000: return Mouse.Mode == Input.MouseTrackingMode.PressAndRelease;
            case 1002: return Mouse.Mode == Input.MouseTrackingMode.ButtonMotion;
            case 1003: return Mouse.Mode == Input.MouseTrackingMode.AllMotion;
            case 1006: return Mouse.Encoding == Input.MouseEncoding.Sgr;
            case 1004: return FocusReporting;
            case 2004: return BracketedPasteMode;
            case 6: return OriginMode;                 // DECOM
            case 7: return AutoWrapMode;               // DECAWM
            case 45: return ReverseWrapMode;           // XTREVWRAP
            case 25: return Cursor.Visible;            // DECTCEM
            case 47:
            case 1047:
            case 1049: return Buffer.IsUsingAlternateBuffer;
            default: return null;
        }
    }

    /// <summary>
    /// DECRQM for DEC private modes (CSI ? Ps $ p). Answers with DECRPM:
    /// CSI ? Ps ; Pv $ y, where Pv is 1 set, 2 reset, 0 not recognised.
    ///
    /// This is how a host finds out what a terminal can do without guessing from the DA string,
    /// and it is why the profile carries a list of recognised modes: "I know that mode and it is
    /// off" and "I have never heard of that mode" are different answers, and collapsing them into
    /// "reset" tells a host it may safely use a mode this terminal will ignore.
    /// </summary>
    protected virtual void HandleRequestMode(int mode)
    {
        int value;
        var state = GetPrivateModeState(mode);

        if (state.HasValue)
        {
            value = state.Value ? 1 : 2;
        }
        else if (Profile.RecognisesPrivateMode(mode))
        {
            // The profile claims it, but no state is readable: report it as reset rather than
            // unknown, since the terminal does accept the sequence.
            value = 2;
        }
        else
        {
            value = 0; // not recognised
        }

        SendResponse(Encoding.ASCII.GetBytes($"\x1b[?{mode};{value}$y"));
    }

    protected virtual void HandleDeviceStatusReport(int mode)
    {
        switch (mode)
        {
            case 5: // Status Report - respond with "OK"
                SendResponse("\x1b[0n"u8);
                break;

            case 6: // CPR - Report Cursor Position
                // Reported in the same coordinates the host addresses in. With origin mode on the
                // row is relative to the top margin, so a host that did CUP then CPR used to get
                // back a different number from the one it sent.
                var reportedRow = Cursor.Row - RowAddressingOrigin + 1;
                var response = $"\x1b[{reportedRow};{Cursor.Column + 1}R";
                SendResponse(Encoding.ASCII.GetBytes(response));
                break;

            default:
                // Unknown DSR mode
                break;
        }
    }

    /// <summary>
    /// DSR with the DEC private marker - CSI ? Ps n. Every one of these is a question about the
    /// hardware rather than about the screen.
    /// </summary>
    /// <param name="parameters">
    /// The sequence's numbers. The first says which question; DSR 63 takes a request label second.
    /// </param>
    /// <remarks>
    /// <para><b>The replies come from the VT420 Programmer Reference, table 12-5</b></para>
    /// Held at spec\DEC. Where a piece of hardware is simply not here, the manual's own "there is
    /// none" answer is sent rather than silence, because a host that asked is waiting:
    ///  - Printer: "CSI ? 13 n - No printer." There is no printer support at all yet.
    ///  - Locator: no DECELR, DECSLE or DECRQLP exists here, so xterm's ctlseqs answers apply -
    ///    "CSI ? 53 n No Locator" and "CSI ? 57 ; 0 n Cannot identify".
    ///  - Macro space: no DECDMAC either, so DECMSR reports zero bytes free.
    ///  - Data integrity: "CSI ? 70 n - No communication errors."
    ///  - Multiple sessions: "CSI ? 83 n - SSU sessions not ready."
    ///
    /// <para><b>DECXCPR is where the two documents disagree</b></para>
    /// The VT420 manual gives the reply as "CSI Pl; Pc; Pp R", with no private marker - which is
    /// indistinguishable from an ordinary CPR, so a host that sent both could not tell the answers
    /// apart. xterm's ctlseqs gives "CSI ? r ; c R" and notes it "assumes the default page, i.e.,
    /// 1". We send the marker from xterm and the page from DEC: "CSI ? Pl ; Pc ; Pp R". The marker
    /// is what every host written since expects, and the page is a real number here because page
    /// memory is real here.
    /// </remarks>
    protected virtual void HandleDecPrivateDeviceStatusReport(ReadOnlySpan<int> parameters)
    {
        int request = parameters.Length > 0 ? parameters[0] : 0;

        switch (request)
        {
            case 6: // DECXCPR - extended cursor position, with the page number
                int reportedRow = Cursor.Row - RowAddressingOrigin + 1;
                SendResponse(Encoding.ASCII.GetBytes(
                    $"\x1b[?{reportedRow};{Cursor.Column + 1};{CurrentPage}R"));
                break;

            case 15: // Printer status
                // "CSI ? 10 n - printer ready", "CSI ? 11 n - printer not ready", "CSI ? 13 n -
                // no printer" (VT420 Programmer Reference, table 12-5). A sink IS a printer port,
                // so the answer stopped being a flat "none" the moment one could be attached.
                SendResponse(PrintSink != null ? "\x1b[?10n"u8 : "\x1b[?13n"u8);
                break;

            case 25: // UDK status - the lock is real, so the answer is the real one
                SendResponse(UserKeys.IsLocked ? "\x1b[?21n"u8 : "\x1b[?20n"u8);
                break;

            case 26: // Keyboard status: North American, ready, LK201/LK301
                SendResponse("\x1b[?27;1;0;0n"u8);
                break;

            case 53:
            case 55: // Locator status
                SendResponse("\x1b[?53n"u8);
                break;

            case 56: // Locator type
                SendResponse("\x1b[?57;0n"u8);
                break;

            case 62: // DECMSR - macro space, in units of 16 bytes rounded down
                SendResponse("\x1b[0*{"u8);
                break;

            case 63: // DECCKSR - checksum of the macro definitions, of which there are none
                SendChecksumReport(parameters.Length > 1 ? parameters[1] : 0, 0);
                break;

            case 75: // Data integrity
                SendResponse("\x1b[?70n"u8);
                break;

            case 85: // Multiple-session status
                SendResponse("\x1b[?83n"u8);
                break;

            default:
                // A question with no answer here. Silence is right: inventing a reply would tell
                // the host something untrue about the hardware.
                break;
        }
    }

    /// <summary>
    /// XTSMGRAPHICS - answers what the graphics area and the colour registers are.
    /// </summary>
    /// <param name="parameters">
    /// Item, action, then the value when the action is "set".
    /// </param>
    /// <remarks>
    /// <para><b>Why this one matters more than its size suggests</b></para>
    /// A Sixel-aware host asks it BEFORE sending an image: how many colour registers may I use, and
    /// how big is the graphics area. Without an answer, an encoder either guesses or refuses, so a
    /// terminal that draws Sixel perfectly can still be sent nothing at all.
    ///
    /// <para><b>The form, from xterm's ctlseqs</b></para>
    /// The request is <c>CSI ? Pi ; Pa ; Pv S</c> and the reply has the same shape,
    /// <c>CSI ? Pi ; Ps ; Pv S</c>, with Ps the status: 0 success, 1 error in Pi, 2 error in Pa,
    /// 3 failure. The items are 1 colour registers, 2 Sixel geometry, 3 ReGIS geometry; the
    /// actions are 1 read, 2 reset to default, 3 set, 4 read the maximum.
    ///
    /// <para><b>Setting is refused, exactly as xterm refuses it</b></para>
    /// "The current implementation allows reading the graphics sizes, but disallows modifying those
    /// sizes because that is done once, using resource-values." The same is true here for a
    /// different reason: the plane is a fixed 800 by 480, which is the VT340's graphics space, and
    /// a host resizing it would be resizing the hardware. Set and reset therefore answer 3,
    /// failure - a truthful "no" rather than a silent one.
    /// </remarks>
    protected virtual void ReportGraphicsAttribute(ReadOnlySpan<int> parameters)
    {
        if (!Profile.Supports(TerminalFeatures.Sixel) && !Profile.Supports(TerminalFeatures.ReGIS))
        {
            return;
        }

        int item = parameters.Length > 0 ? parameters[0] : 0;
        int action = parameters.Length > 1 ? parameters[1] : 0;

        // 1 is error in Pi, 2 is error in Pa - and both are answered rather than ignored, because
        // a host waiting for a reply would otherwise wait for ever.
        if (item < 1 || item > 3)
        {
            SendGraphicsAttributeReply(item, 1, null);
            return;
        }

        if (action < 1 || action > 4)
        {
            SendGraphicsAttributeReply(item, 2, null);
            return;
        }

        // Reading and reading-the-maximum give the same answer here: the numbers are fixed, so the
        // current value IS the maximum.
        if (action == 2 || action == 3)
        {
            SendGraphicsAttributeReply(item, 3, null);
            return;
        }

        if (item == 1)
        {
            SendGraphicsAttributeReply(item, 0,
                RetroTerm.Core.Terminal.Graphics.SixelDecoder.ColourRegisterCount
                    .ToString(CultureInfo.InvariantCulture));
            return;
        }

        SendGraphicsAttributeReply(item, 0,
            GraphicsPlaneWidth.ToString(CultureInfo.InvariantCulture) + ";"
            + GraphicsPlaneHeight.ToString(CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// Sends one XTSMGRAPHICS reply.
    /// </summary>
    /// <param name="item">
    /// The item the host asked about, echoed back.
    /// </param>
    /// <param name="status">
    /// 0 success, 1 error in the item, 2 error in the action, 3 failure.
    /// </param>
    /// <param name="value">
    /// The value on success, or null when there is none to give.
    /// </param>
    private void SendGraphicsAttributeReply(int item, int status, string? value)
    {
        string reply = "\x1b[?" + item.ToString(CultureInfo.InvariantCulture)
            + ";" + status.ToString(CultureInfo.InvariantCulture)
            + (value == null ? "" : ";" + value) + "S";

        SendResponse(Encoding.ASCII.GetBytes(reply));
    }

    /// <summary>
    /// DECRQCRA - answers with the checksum of a rectangular area of page memory.
    /// </summary>
    /// <param name="parameters">
    /// Request label, page, then top, left, bottom and right.
    /// </param>
    /// <remarks>
    /// <para><b>What the manual does and does not say</b></para>
    /// The VT420 Programmer Reference defines the request and the reply exactly - "CSI Pid; Pp; Pt;
    /// Pl; Pb; Pr * y" answered by "DCS Pid ! ~ D..D ST", four hexadecimal digits - and says
    /// NOTHING WHATEVER about how the number is arrived at. Every other terminal that implements
    /// this copied xterm, whose source is not a document we hold and must not be ported here.
    ///
    /// <para><b>So the sum follows the only description we do hold</b></para>
    /// xterm's ctlseqs documents XTCHECKSUM (CSI Ps # y) by saying what each bit CHANGES, which
    /// states the default behaviour by implication:
    ///  - "0 - do not negate the result", so by default the result is negated.
    ///  - "2 - do not omit checksum for blanks", so by default blank cells contribute nothing.
    ///  - "4 - do not mask cell value to 8 bits", so by default each character is masked to 8 bits.
    ///  - "1 - do not report the VT100 video attributes", so by default they are included.
    ///
    /// The first three are followed here. THE FOURTH IS NOT, and deliberately: no document here
    /// gives the weight each attribute adds, so including them would mean inventing numbers that
    /// would disagree with xterm anyway. The sum is over character codes only, which is exactly
    /// what a host gets from xterm when it sets XTCHECKSUM bit 1. Said here rather than silently.
    ///
    /// <para><b>The page rules ARE documented</b></para>
    /// "If Pp is 0 or omitted, the terminal ignores the following parameters and reports a checksum
    /// for all pages in page memory. If Pp is a higher number than the number of pages available,
    /// the terminal reports on the last page." Also: "The coordinates of the rectangular area are
    /// affected by the setting of origin mode (DECOM)" - which TryReadRectangle already applies.
    /// </remarks>
    protected virtual void ReportRectangleChecksum(ReadOnlySpan<int> parameters)
    {
        int id = parameters.Length > 0 ? parameters[0] : 0;
        int page = parameters.Length > 1 ? parameters[1] : 0;

        int sum = 0;

        if (page <= 0)
        {
            // Every page, whole. The rectangle parameters are ignored, as the manual says.
            for (int p = 1; p <= PageCount; p++)
            {
                sum += SumRectangle(p, 0, 0, Height - 1, Width - 1);
            }
        }
        else
        {
            if (page > PageCount) page = PageCount;

            // With the four borders omitted TryReadRectangle answers the whole screen, which is
            // the manual's "the terminal returns a checksum of page Pp".
            if (TryReadRectangle(parameters, 2, out int top, out int left, out int bottom, out int right))
            {
                sum = SumRectangle(page, top, left, bottom, right);
            }
        }

        SendChecksumReport(id, sum);
    }

    /// <summary>
    /// Adds up the character codes in a rectangle of one page.
    /// </summary>
    /// <param name="page">
    /// Page number, counting from 1. Pages other than the one on screen are read from the store
    /// that <see cref="GoToPage"/> saves them into.
    /// </param>
    /// <param name="top">
    /// First row, counting from 0.
    /// </param>
    /// <param name="left">
    /// First column, counting from 0.
    /// </param>
    /// <param name="bottom">
    /// Last row, counting from 0.
    /// </param>
    /// <param name="right">
    /// Last column, counting from 0.
    /// </param>
    /// <returns>
    /// The plain sum, before it is negated and cut to 16 bits.
    /// </returns>
    private int SumRectangle(int page, int top, int left, int bottom, int right)
    {
        // A page that has never been left is not in the store - it IS the buffer.
        bool onScreen = page == CurrentPage;
        TerminalCell[]? stored = null;
        if (!onScreen && _pageStore != null && page - 1 < _pageStore.Length)
        {
            stored = _pageStore[page - 1];
        }

        int width = Buffer.Width;
        int total = 0;

        for (int row = top; row <= bottom; row++)
        {
            for (int col = left; col <= right; col++)
            {
                uint codepoint;

                if (onScreen)
                {
                    Buffer.TryGetCell(row, col, out var cell);
                    codepoint = cell.Codepoint;
                }
                else
                {
                    int index = row * width + col;
                    codepoint = stored != null && index < stored.Length ? stored[index].Codepoint : 0;
                }

                // Blanks contribute nothing - see the XTCHECKSUM reading in ReportRectangleChecksum.
                // An untouched cell holds codepoint 0 and counts as blank.
                if (codepoint == 0 || codepoint == ' ') continue;

                total += (int)(codepoint & 0xFF);
            }
        }

        return total;
    }

    /// <summary>
    /// Sends a checksum report - DCS Pid ! ~ D..D ST - for DECRQCRA and for DSR 63.
    /// </summary>
    /// <param name="id">
    /// The request label the host sent, echoed back so it can tell several reports apart.
    /// </param>
    /// <param name="sum">
    /// The plain sum. It is negated and cut to 16 bits here.
    /// </param>
    /// <remarks>
    /// "The string consists of four hexadecimal digits that indicate the checksum. The digits are
    /// in the range of 3/0 through 3/9 and 4/1 through 4/6" - that is, 0 to 9 and A to F, so the
    /// letters are UPPER case. (An earlier table in the same manual says 4/0 through 4/6, which
    /// would include the at sign; 4/1 is the one that makes sense and matches the other table.)
    /// </remarks>
    private void SendChecksumReport(int id, int sum)
    {
        int value = (-sum) & 0xFFFF;

        SendResponse(Encoding.ASCII.GetBytes(
            "\x1bP" + id.ToString(CultureInfo.InvariantCulture) + "!~"
            + value.ToString("X4", CultureInfo.InvariantCulture) + "\x1b\\"));
    }

    /// <summary>
    /// Resets tab stops to the power-on default: one every 8 columns, starting at column 8.
    /// Also used when the screen is resized, since the stop array is per column.
    /// </summary>
    protected void ResetTabStops()
    {
        if (_tabStops.Length != Width)
        {
            _tabStops = new bool[Width];
        }

        for (int col = 0; col < _tabStops.Length; col++)
        {
            _tabStops[col] = col > 0 && (col % 8) == 0;
        }
    }

    /// <summary>
    /// DECSC - saves the cursor position together with the rest of the state the standard says
    /// belongs to it: rendition, character sets, origin mode and auto-wrap.
    /// </summary>
    protected virtual void SaveTerminalState()
    {
        Cursor.Save();

        _savedState.IsValid = true;
        _savedState.Attributes = CurrentAttributes;
        _savedState.Foreground = CurrentForeground;
        _savedState.Background = CurrentBackground;
        _savedState.CharacterSet = CurrentCharacterSet;
        _savedState.ActiveCharacterSet = ActiveCharacterSet;
        _savedState.G0 = CharacterSets[0];
        _savedState.G1 = CharacterSets[1];
        _savedState.G2 = CharacterSets[2];
        _savedState.G3 = CharacterSets[3];
        _savedState.OriginMode = OriginMode;
        _savedState.AutoWrapMode = AutoWrapMode;
        _savedState.ProtectedMode = ProtectedMode;
    }

    /// <summary>
    /// DECRC - restores what DECSC saved. With nothing saved, the standard says to go home with
    /// default rendition rather than to leave the screen as it is.
    /// </summary>
    protected virtual void RestoreTerminalState()
    {
        Cursor.Restore();

        if (!_savedState.IsValid)
        {
            Cursor.Home();
            CurrentAttributes = CharacterAttributes.None;
            CurrentForeground = TerminalColor.Default;
            CurrentBackground = TerminalColor.Default;

            // DECSCA goes back to its default too. "Nothing saved" means the power-on state, and
            // leaving text protected on the way there would spare it from a later selective erase
            // for no reason a host could see.
            ProtectedMode = false;
            return;
        }

        CurrentAttributes = _savedState.Attributes;
        CurrentForeground = _savedState.Foreground;
        CurrentBackground = _savedState.Background;
        CurrentCharacterSet = _savedState.CharacterSet;
        ActiveCharacterSet = _savedState.ActiveCharacterSet;
        CharacterSets[0] = _savedState.G0;
        CharacterSets[1] = _savedState.G1;
        CharacterSets[2] = _savedState.G2;
        CharacterSets[3] = _savedState.G3;
        OriginMode = _savedState.OriginMode;
        AutoWrapMode = _savedState.AutoWrapMode;
        Cursor.AutoWrap = _savedState.AutoWrapMode;
        ProtectedMode = _savedState.ProtectedMode;
    }

    /// <summary>
    /// Sets a tab stop at the cursor's column (HTS, ESC H / CSI 0 W).
    /// </summary>
    protected virtual void SetTabStop()
    {
        int col = Cursor.Column;
        if (col >= 0 && col < _tabStops.Length)
        {
            _tabStops[col] = true;
        }
    }

    /// <summary>
    /// Clears tab stops (TBC, CSI g). Parameter 0 clears the stop at the cursor,
    /// parameter 3 clears every stop on the line.
    /// </summary>
    protected virtual void ClearTabStop(int mode)
    {
        if (mode == 3)
        {
            for (int col = 0; col < _tabStops.Length; col++)
            {
                _tabStops[col] = false;
            }
            return;
        }

        if (mode == 0)
        {
            int col = Cursor.Column;
            if (col >= 0 && col < _tabStops.Length)
            {
                _tabStops[col] = false;
            }
        }
    }

    /// <summary>
    /// Finds the column <paramref name="count"/> tab stops forward of the cursor. With no stops
    /// left the cursor lands on the last column, which is what a real terminal does.
    /// </summary>
    protected int NextTabStop(int count)
    {
        int col = Cursor.Column;

        for (int n = 0; n < count; n++)
        {
            int found = -1;
            for (int c = col + 1; c < _tabStops.Length; c++)
            {
                if (_tabStops[c]) { found = c; break; }
            }

            if (found < 0) return Width - 1;
            col = found;
        }

        return col;
    }

    /// <summary>
    /// Finds the column <paramref name="count"/> tab stops back from the cursor. With no stops
    /// left the cursor lands on column 0.
    /// </summary>
    protected int PreviousTabStop(int count)
    {
        int col = Cursor.Column;

        for (int n = 0; n < count; n++)
        {
            int found = -1;
            int start = Math.Min(col - 1, _tabStops.Length - 1);
            for (int c = start; c > 0; c--)
            {
                if (_tabStops[c]) { found = c; break; }
            }

            if (found < 0) return 0;
            col = found;
        }

        return col;
    }

    /// <summary>
    /// BS - one column left, and off the left edge only when reverse wraparound is on.
    /// </summary>
    /// <remarks>
    /// With XTREVWRAP reset - the default, and the only thing a real DEC terminal does - a
    /// backspace at the left edge does nothing, so the cursor sits still.
    ///
    /// With it set, the cursor goes to the last column of the line above, and from the top line
    /// round to the BOTTOM line. That last part is what the reverse-wrap fixture pins down: from
    /// the home position, seven backspaces land on the bottom line, so the text printed next runs
    /// off the bottom corner and scrolls the screen twice.
    /// </remarks>
    protected virtual void HandleBackspace()
    {
        int leftEdge = FirstUsableColumn;
        if (ReverseWrapMode && Cursor.Column <= leftEdge)
        {
            Cursor.Row = Cursor.Row > 0 ? Cursor.Row - 1 : Height - 1;
            Cursor.Column = LastUsableColumn(Cursor.Row);
            return;
        }

        Cursor.MoveBackward();
    }

    protected virtual void HandleTab()
    {
        TabulateToColumn(Math.Min(NextTabStop(1), Width - 1));
    }

    /// <summary>
    /// Moves the cursor to a column reached by tabulating, leaving the pending wrap alone.
    /// </summary>
    /// <param name="target">
    /// The column the tabulation resolved to. Equal to the current column when no stop remains.
    /// </param>
    /// <remarks>
    /// TABULATION NEVER CANCELS A PENDING WRAP - not when it stays put, and not when it moves.
    /// Every other cursor movement does cancel it, which is why the Column setter clears the Last
    /// Column Flag and why this one method goes round the setter.
    ///
    /// The standing-still half came first, from tab-separated text running off the right-hand
    /// side: the tabulation after the last visible item disarmed the wrap and the NEXT item
    /// overwrote the last column instead of starting the following row. xterm put "k" in the last
    /// column and "l" at the start of the next row; we had "l" on top of "k".
    ///
    /// The moving half is t0084-CBT, and it takes the whole fixture to see. Each of its test rows
    /// prints "abcdefgh" into columns 73..80, which arms the flag on the last column, and then
    /// sends CSI Z before printing "!":
    ///
    ///  - "at end:" ends there. xterm puts the "!" ALONE AT THE START OF THE NEXT ROW, so the
    ///    backward tabulation left the wrap armed and the "!" performed it. Ours cancelled the
    ///    wrap and wrote the "!" at the stop CBT had moved to: "abcdefgh" became "!bcdefgh".
    ///  - "at end with clipping:" sends ESC M and ESC D - up a row and straight back down - in
    ///    between. Those DO cancel the wrap, and now xterm writes the "!" at column 73, exactly
    ///    where CBT had moved to. Same for the two-stop form at column 65.
    ///
    /// The second half is what separates "CBT keeps the flag" from "CBT does nothing at all": the
    /// cursor really did move back a stop, and only the flag survived. Forward tabulation cannot
    /// show this, because the flag can only be armed on the last column and there is no stop
    /// beyond it - which is why t0080-HT and t0083-CHT were already clean.
    /// </remarks>
    private void TabulateToColumn(int target)
    {
        Cursor.SetColumnKeepingPendingWrap(target);
    }

    // ── Margins and origin mode, in one place ─────────────────────────────────────────────────
    //
    // These rules used to be re-derived at each call site and disagreed with each other: origin
    // mode was applied to CUP only (so VPA addressed the wrong row whenever a host set a scrolling
    // region and turned origin mode on), CUP added the top margin without clamping to the bottom
    // one (so a too-large row escaped the region), and CUU/CUD clamped to the screen rather than
    // to the margins (so cursor-up walked out of the region entirely). Everything that positions
    // the cursor vertically now goes through the four helpers below.

    /// <summary>
    /// The row that row 1 means to the host. With origin mode on, addressing is relative to the
    /// top margin; with it off, to the top of the screen.
    /// </summary>
    protected int RowAddressingOrigin => OriginMode ? ScrollTop : 0;

    /// <summary>
    /// The lowest-numbered row the host may address right now.
    /// </summary>
    protected int LowestAddressableRow => OriginMode ? ScrollTop : 0;

    /// <summary>
    /// The highest-numbered row the host may address right now.
    /// </summary>
    protected int HighestAddressableRow => OriginMode ? ScrollBottom : Height - 1;

    /// <summary>
    /// Positions the cursor on an absolute row as the host means it (CUP, HVP, VPA): 1-based,
    /// relative to the addressing origin, and clamped so origin mode really does confine the
    /// cursor to the scrolling region.
    /// </summary>
    protected void SetCursorRowFromHost(int oneBasedRow)
    {
        int row = RowAddressingOrigin + (oneBasedRow - 1);
        Cursor.Row = Math.Clamp(row, LowestAddressableRow, HighestAddressableRow);
    }

    // The same three, sideways. THE COLUMN HAS AN ORIGIN TOO, which is easy to miss because it
    // only shows once a host has set left and right margins - and until DECSLRM existed here there
    // were none to have. The VT420 Programmer Reference says it in the description of CUP itself:
    // "The starting point for LINES AND COLUMNS depends on the setting of origin mode (DECOM)",
    // and again under DECOM: "the home cursor position is at the upper-left corner of the screen,
    // WITHIN THE MARGINS ... The cursor cannot move outside of the margins."

    /// <summary>
    /// The column that column 1 means to the host. With origin mode on and left and right margins
    /// in use, addressing is relative to the left margin; otherwise to the edge of the screen.
    /// </summary>
    protected int ColumnAddressingOrigin => OriginMode && LeftRightMarginMode ? LeftMargin : 0;

    /// <summary>
    /// The lowest-numbered column the host may address right now.
    /// </summary>
    protected int LowestAddressableColumn => ColumnAddressingOrigin;

    /// <summary>
    /// The highest-numbered column the host may address right now.
    /// </summary>
    protected int HighestAddressableColumn =>
        OriginMode && LeftRightMarginMode ? RightMargin : Width - 1;

    /// <summary>
    /// Positions the cursor on an absolute column as the host means it (CUP, HVP): 1-based,
    /// relative to the addressing origin, and clamped so origin mode confines the cursor sideways
    /// the same way it confines it vertically.
    /// </summary>
    /// <param name="oneBasedColumn">
    /// The column the host asked for, counting from 1.
    /// </param>
    protected void SetCursorColumnFromHost(int oneBasedColumn)
    {
        int column = ColumnAddressingOrigin + (oneBasedColumn - 1);
        Cursor.Column = Math.Clamp(column, LowestAddressableColumn, HighestAddressableColumn);
    }

    /// <summary>
    /// The home position as the manual means it for DECSTBM and DECSLRM: "column 1, line 1 of the
    /// page", which origin mode moves into the corner of the margins.
    /// </summary>
    protected void MoveCursorHomeAsTheHostSeesIt()
    {
        Cursor.MoveTo(LowestAddressableRow, LowestAddressableColumn);
    }

    /// <summary>
    /// CUU and friends. The cursor stops at the top margin, but only if it started at or below it
    /// — a cursor already above the region must not be dragged down into it.
    /// </summary>
    protected void MoveCursorUpWithinMargins(int count)
    {
        if (count < 1) count = 1;
        int limit = Cursor.Row >= ScrollTop ? ScrollTop : 0;
        Cursor.Row = Math.Max(limit, Cursor.Row - count);
    }

    /// <summary>
    /// CUD and friends. The cursor stops at the bottom margin, but only if it started at or above
    /// it — a cursor already below the region keeps the whole screen to move in.
    /// </summary>
    /// <remarks>
    /// A cursor ABOVE the region is still stopped by the bottom margin, and that is measured, not
    /// assumed. t0075-DECSTBM_CUU_CUD sets a region of rows 10..19, which homes the cursor to row
    /// 1, and sends CSI 25 B: a real xterm stops on row 19. Letting it out to the last row of the
    /// screen broke that fixture and t0078-DECSTBM_CPL_CNL with it.
    ///
    /// VPR does NOT behave this way - see MoveCursorDownIgnoringMargins - and t0079-DECSTBM_VPR
    /// is the same three lines with CSI 25 e in place of CSI 25 B, so the two are settled against
    /// each other rather than guessed at.
    /// </remarks>
    protected void MoveCursorDownWithinMargins(int count)
    {
        if (count < 1) count = 1;
        int limit = Cursor.Row <= ScrollBottom ? ScrollBottom : Height - 1;
        Cursor.Row = Math.Min(limit, Cursor.Row + count);
    }

    /// <summary>
    /// VPR — moves down without regard to the scrolling region, stopping at the last row the host
    /// may address.
    /// </summary>
    /// <param name="count">
    /// How many rows to move down; one or more.
    /// </param>
    /// <remarks>
    /// VPR LOOKS like CUD and is not. t0079-DECSTBM_VPR and t0075-DECSTBM_CUU_CUD are the same
    /// stream apart from the final letter - a region of rows 10..19, the cursor homed to row 1
    /// above it, then a move of 25 rows down. A real xterm answers row 19 for CSI 25 B and row 25
    /// for CSI 25 e. So the bottom margin stops CUD and does not stop VPR.
    ///
    /// That fits what the two are FOR: CUD is cursor movement inside the page the region defines,
    /// while the ECMA-48 position family VPA/VPR/HPA/HPR addresses the page itself. VPA is already
    /// bounded by HighestAddressableRow rather than by the margin, and this is its relative twin.
    /// Under origin mode HighestAddressableRow IS the bottom margin, which is how origin mode
    /// confines the host in the first place; no fixture covers that combination.
    /// </remarks>
    protected void MoveCursorDownIgnoringMargins(int count)
    {
        if (count < 1) count = 1;
        Cursor.Row = Math.Min(HighestAddressableRow, Cursor.Row + count);
    }

    /// <summary>
    /// LF / IND — move down one line, scrolling the region only when sitting on its last line.
    ///
    /// The test must be EXACTLY ON the boundary, not "at or past" it. The old condition was
    /// <c>Row is less than ScrollBottom ? MoveDown() : ScrollUp()</c>, which is right inside the region
    /// and wrong below it: with a region of rows 0..9 and the cursor parked on row 19, a linefeed
    /// scrolled a region the cursor was not in and left the cursor where it was. A full-screen
    /// program that sets a scrolling region for part of the screen and then writes below it — a
    /// status line, a pager footer — got a jumping screen and a stuck cursor.
    ///
    /// Outside the region the cursor just walks to the bottom of the SCREEN and stops there.
    /// </summary>
    protected virtual void HandleLineFeed()
    {
        // THE LAST COLUMN FLAG BELONGS TO THE LINE BEING LEFT. A character printed in the last
        // column arms the flag so the NEXT character wraps; moving to another line by any means
        // ends that, because there is no longer a full line to carry on from.
        //
        // Without this, a stream that fills the last column and then indexes gained an extra line
        // advance when the next character resolved a wrap it should never have performed - one row
        // of drift, which then repeats. Found by xterm.js's fixtures, whose expected screens came
        // from real xterm: 61 of 76 disagreed, most of them by exactly this drift.
        Cursor.ClearPendingWrap();

        // AUTOPRINT prints the line the cursor is LEAVING, and it must happen before the scroll,
        // because a scroll at the bottom of the region takes that line away. This is the whole
        // difference between autoprint and printer controller mode: the screen still works, and
        // the paper gets a copy of each line as it is finished.
        if (AutoPrintMode) PrintRows(Cursor.Row, Cursor.Row);

        if (Cursor.Row == ScrollBottom)
        {
            ScrollUp();
            return;
        }

        Cursor.MoveDown();          // clamps at the physical bottom row
    }

    /// <summary>
    /// RI — move up one line, scrolling the region only when sitting on its first line.
    ///
    /// Same correction as <see cref="HandleLineFeed"/>: the old test of Row being at or above ScrollTop
    /// scrolled the region whenever the cursor was ABOVE it as well as on its top line.
    /// </summary>
    protected virtual void HandleReverseLineFeed()
    {
        // Same reasoning as HandleLineFeed: leaving the line ends its last-column flag.
        Cursor.ClearPendingWrap();

        if (Cursor.Row == ScrollTop)
        {
            ScrollDown();
            return;
        }

        Cursor.MoveUp();            // clamps at row 0
    }

    /// <summary>
    /// Puts the cursor at the start of its line after IL or DL, and disarms any pending wrap.
    /// </summary>
    /// <remarks>
    /// THE COLUMN DOES MOVE, and the two corpora disagree about it. libvterm's 13state_edit script
    /// asserts the column is left alone and carries its author's note beside the assertion:
    /// "ECMA-48 says we should move to line home, but neither xterm nor xfce4-terminal do this".
    ///
    /// The xterm.js screens were captured from a REAL xterm, and they say the opposite in four
    /// places. In t0051-IL the cursor sits at column 2 when CSI L arrives and the "QR" printed
    /// straight afterwards starts at column 0; later in the same fixture a full line arms the wrap
    /// at column 79, CSI L follows, and the "b" lands at column 0 rather than at 79. t0052-DL is
    /// the same shape for CSI M, once at column 1 and once at column 79.
    ///
    /// So the note in libvterm is a claim about xterm that a capture of xterm contradicts, and
    /// ECMA-48 and DEC's own manuals both say to move to the line home position. Four rows of real
    /// screen beat one cursor reading, exactly as they did for VPB - see the libvterm baseline
    /// table, where 13state_edit now carries that one expected disagreement.
    ///
    /// The pending wrap is disarmed too. Inserting or deleting a line has moved the text out from
    /// under the cursor, so a wrap armed by the character that used to be in the last column no
    /// longer means anything.
    /// </remarks>
    private void FinishLineEdit()
    {
        Cursor.ClearPendingWrap();
        Cursor.Column = FirstUsableColumn;
    }

    /// <summary>
    /// SU - scrolls the scrolling region up by a number of lines, cursor unmoved.
    /// </summary>
    /// <param name="lines">How many lines to scroll; one or more.</param>
    /// <remarks>
    /// WHETHER THE DEPARTING LINES REACH THE SCROLLBACK is a judgement, not something either
    /// document here settles. The VT420 manual describes panning a window over page memory, and a
    /// VT420 has no scrollback at all, so it cannot say. This treats a full-screen SU the way a
    /// line feed at the bottom of the screen is treated - the top line becomes history - because
    /// that is what someone scrolling back to re-read output expects to find. A scroll inside a
    /// smaller region keeps nothing, exactly as an ordinary region scroll does.
    /// </remarks>
    protected virtual void ScrollRegionUp(int lines)
    {
        for (int i = 0; i < lines; i++)
        {
            ScrollUp();
        }

        OnInvalidated();
    }

    /// <summary>
    /// SD - scrolls the scrolling region down by a number of lines, cursor unmoved.
    /// </summary>
    /// <param name="lines">How many lines to scroll; one or more.</param>
    /// <remarks>
    /// "Pn new lines appear at the top of the display. Pn old lines disappear at the bottom."
    /// Nothing is taken out of the scrollback to fill the new lines: they are blank, which is what
    /// the manual says appears.
    /// </remarks>
    protected virtual void ScrollRegionDown(int lines)
    {
        for (int i = 0; i < lines; i++)
        {
            ScrollDown();
        }

        OnInvalidated();
    }

    /// <summary>
    /// Moves the graphics planes up by whole text rows, converting rows to pixels through
    /// the same cell height the rest of the graphics code uses.
    /// </summary>
    /// <param name="textRows">
    /// How many text rows the screen just scrolled by.
    /// </param>
    protected void ScrollGraphicsUp(int textRows)
    {
        if (textRows <= 0) return;
        var compositor = Graphics;
        if (compositor == null) return;

        int pixelRows = textRows * GraphicsCellHeight;
        compositor.ScrollUp(pixelRows);
        _graphicsScrollPixels += pixelRows;
    }

    /// <summary>
    /// Running total of how far the graphics planes have been scrolled up, in pixel rows.
    /// </summary>
    /// <remarks>
    /// Only ever read as a DIFFERENCE across a stretch of work, never as an absolute position.
    /// The Sixel handler takes it before and after the line feeds an image causes, because that
    /// difference is how far the picture has to be shifted to land where the hardware would put
    /// it. Nothing resets it and nothing needs to; it is allowed to wrap.
    /// </remarks>
    private int _graphicsScrollPixels;

    /// <summary>
    /// Wipes the graphics planes. Called by a full-screen erase, because on a real VT340
    /// the text and the picture share one bitmap and clearing takes both.
    /// </summary>
    protected void ClearGraphicsPlanes() => Graphics?.ClearAllPlanes();

    protected virtual void ScrollUp()
    {
        if (ScrollTop == 0 && ScrollBottom == Height - 1)
        {
            // Full screen scroll (no active scroll region)
            Buffer.ScrollUp();

            // The picture goes with the text. A real VT340 keeps text and graphics in one
            // bitmap, so a scroll moves both; ours are separate surfaces and the picture
            // used to stand still while the text slid out from under it. Ronny's call,
            // 9 September 2026: be faithful.
            //
            // Only a WHOLE-SCREEN scroll moves it. A scroll inside a region slides part of
            // the screen while the rest stands still, and the planes have no notion of a
            // region, so moving the whole picture for that would drag graphics beside rows
            // that never moved. That case is left alone deliberately.
            //
            // NOT SOLVED, and worth knowing before chasing it: under DECSCLM the buffer
            // moves at once while the PICTURE of it walks down over several frames. The
            // planes move at once too, so during that walk the graphics sit one row ahead
            // of the text they were drawn beside. A real VT340 cannot show this because
            // its text and graphics are the same bitmap. Fixing it means giving the
            // compositor the same catch-up offset the text picture uses, which is a
            // larger change than this one and has not been attempted.
            ScrollGraphicsUp(1);

            // DECSCLM. The BUFFER still scrolls instantly - the data model is never animated, only
            // the picture of it - and the view is told it owes one line of catching up.
            //
            // Whole-screen scrolls only. A scroll inside a region moves part of the screen while
            // the rest stands still, and sliding the entire picture for that would drag rows that
            // never moved.
            if (SmoothScrollMode)
            {
                // One more line the picture owes. Refusing to count past the limit is what turns a
                // host faster than the animation into plain jump scrolling instead of an ever-
                // growing lag: the content moves on without the picture being asked to walk it.
                if (System.Threading.Volatile.Read(ref _smoothScrollBacklog) < MaxSmoothScrollBacklog)
                {
                    System.Threading.Interlocked.Increment(ref _smoothScrollBacklog);
                }

                SmoothScrollLineScrolled?.Invoke();
            }
        }
        else
        {
            // Scroll within region
            Buffer.ScrollUp(ScrollTop, ScrollBottom);
        }
    }

    protected virtual void ScrollDown()
    {
        if (ScrollTop == 0 && ScrollBottom == Height - 1)
        {
            // Full screen scroll (no active scroll region)
            Buffer.ScrollDown(0, Height - 1);
        }
        else
        {
            // Scroll within region
            Buffer.ScrollDown(ScrollTop, ScrollBottom);
        }
    }

    protected virtual void SetScrollRegion(int top, int bottom)
    {
        // A parameter of 0 means "use the default", which is the page limit at that end.
        if (top <= 0) top = 1;
        if (bottom <= 0) bottom = Height;

        // Convert from 1-indexed to 0-indexed coordinates
        // DECSTBM uses 1-indexed coordinates, but our internal representation uses 0-indexed
        int wantedTop = Math.Max(0, top - 1);
        int wantedBottom = Math.Min(Height - 1, bottom - 1);

        // AN IMPOSSIBLE REGION IS IGNORED, WHOLE. The manual's note is "the value of the top
        // margin (Pt) must be less than the bottom margin (Pb)", and a request that breaks that
        // rule is not a request to reset - it is a request the terminal cannot carry out.
        //
        // This used to fall back to the full screen and home the cursor, which turned a rejected
        // DECSTBM into a working one that happened to select everything. A program probing the
        // terminal with a bad region then found its NEXT scroll running over the whole screen
        // instead of over the region it had set earlier.
        if (wantedTop >= wantedBottom)
        {
            return;
        }

        ScrollTop = wantedTop;
        ScrollBottom = wantedBottom;

        // "DECSTBM moves the cursor to column 1, line 1 of the page" - and origin mode is what
        // decides where the host's column 1, line 1 is.
        MoveCursorHomeAsTheHostSeesIt();
    }

    protected virtual void ClearScrollingRegion()
    {
        ScrollTop = 0;
        ScrollBottom = Height - 1;  // Full screen scroll region

        // Same homing rule as a region that WAS set - the region is now the whole page, so both
        // readings of "line 1" land on the same row, but the column still follows the margins.
        MoveCursorHomeAsTheHostSeesIt();
    }

    /// <summary>
    /// IL, bounded by the scrolling region and - when the host has set them - the left and right
    /// margins.
    /// </summary>
    /// <remarks>
    /// <para><b>Why margins make this a rectangle rather than a row</b></para>
    /// Without margins, inserting a line moves whole rows and the buffer does it. With margins, a
    /// real VT420 moves only the COLUMNS INSIDE THE REGION down; the text to the left and right of
    /// it does not shift, which is the entire reason a program sets margins - it is drawing a
    /// panel and does not want the rest of the screen following it around.
    /// </remarks>
    /// <param name="row">
    /// Where the blank lines appear.
    /// </param>
    /// <param name="count">
    /// How many lines to insert.
    /// </param>
    protected void InsertLinesWithinMargins(int row, int count)
    {
        // "IL has no effect outside the page margins", and DL says the same about the scrolling
        // margins. ABOVE the region counts as outside just as much as below it: the buffer's own
        // guard only refused a row past the BOTTOM, so IL with the cursor above a scrolling region
        // shifted the rows outside it downwards and dragged them into the region.
        if (row < ScrollTop || row > ScrollBottom) return;

        if (!LeftRightMarginMode)
        {
            Buffer.InsertLines(row, count, ScrollBottom);
            return;
        }

        if (count < 1) count = 1;

        for (int target = ScrollBottom; target >= row; target--)
        {
            int source = target - count;

            for (int col = LeftMargin; col <= RightMargin && col < Width; col++)
            {
                if (source >= row)
                {
                    Buffer[target, col] = Buffer[source, col];
                }
                else
                {
                    Buffer[target, col].Clear();
                }
            }
        }
    }

    /// <summary>
    /// DL, bounded the same way as <see cref="InsertLinesWithinMargins"/>.
    /// </summary>
    /// <param name="row">
    /// Where lines are removed.
    /// </param>
    /// <param name="count">
    /// How many lines to delete.
    /// </param>
    protected void DeleteLinesWithinMargins(int row, int count)
    {
        // Outside the region means outside at either end - see InsertLinesWithinMargins.
        if (row < ScrollTop || row > ScrollBottom) return;

        if (!LeftRightMarginMode)
        {
            Buffer.DeleteLines(row, count, ScrollBottom);
            return;
        }

        if (count < 1) count = 1;

        for (int target = row; target <= ScrollBottom; target++)
        {
            int source = target + count;

            for (int col = LeftMargin; col <= RightMargin && col < Width; col++)
            {
                if (source <= ScrollBottom)
                {
                    Buffer[target, col] = Buffer[source, col];
                }
                else
                {
                    Buffer[target, col].Clear();
                }
            }
        }
    }

    /// <summary>
    /// Reads the four numbers that name a rectangle, in the order DEC puts them: top, left,
    /// bottom, right.
    /// </summary>
    /// <remarks>
    /// <para><b>Every one of them defaults to the edge of the screen</b></para>
    /// An omitted or zero parameter means "as far as the screen goes in that direction", so
    /// <c>CSI $ z</c> with nothing at all erases everything. That is DEC's rule and it is why the
    /// defaults here are the four edges rather than zero.
    ///
    /// <para><b>Rows respect origin mode</b></para>
    /// The same way CUP does, so a program that set a scrolling region and turned DECOM on
    /// addresses rectangles in the coordinates it is already using.
    /// </remarks>
    /// <param name="parameters">
    /// The sequence's numbers.
    /// </param>
    /// <param name="first">
    /// Index of the first of the four.
    /// </param>
    /// <param name="top">
    /// Receives the top row, counting from 0.
    /// </param>
    /// <param name="left">
    /// Receives the left column, counting from 0.
    /// </param>
    /// <param name="bottom">
    /// Receives the bottom row, counting from 0.
    /// </param>
    /// <param name="right">
    /// Receives the right column, counting from 0.
    /// </param>
    /// <returns>
    /// False when the rectangle is empty, in which case nothing should be done.
    /// </returns>
    private bool TryReadRectangle(ReadOnlySpan<int> parameters, int first,
        out int top, out int left, out int bottom, out int right)
    {
        // Read inline rather than through a helper: the parameters are a span, and a local
        // function cannot capture one.
        int topValue = 1, leftValue = 1, bottomValue = Height, rightValue = Width;

        if (first < parameters.Length && parameters[first] > 0) topValue = parameters[first];
        if (first + 1 < parameters.Length && parameters[first + 1] > 0) leftValue = parameters[first + 1];
        if (first + 2 < parameters.Length && parameters[first + 2] > 0) bottomValue = parameters[first + 2];
        if (first + 3 < parameters.Length && parameters[first + 3] > 0) rightValue = parameters[first + 3];

        top = topValue - 1 + RowAddressingOrigin;
        left = leftValue - 1;
        bottom = bottomValue - 1 + RowAddressingOrigin;
        right = rightValue - 1;

        if (top < 0) top = 0;
        if (left < 0) left = 0;
        if (bottom > Height - 1) bottom = Height - 1;
        if (right > Width - 1) right = Width - 1;

        return top <= bottom && left <= right;
    }

    /// <summary>
    /// DECFRA: fill a rectangle with one character.
    /// </summary>
    /// <remarks>
    /// The character comes first, as a code point, and DEC restricts it to the printable ranges -
    /// 32 to 126 and 160 to 255. A control code would be filling the screen with something that has
    /// no glyph, so one outside those ranges is refused rather than drawn.
    /// </remarks>
    /// <param name="parameters">
    /// Character, then top, left, bottom, right.
    /// </param>
    protected virtual void FillRectangle(ReadOnlySpan<int> parameters)
    {
        int character = parameters.Length > 0 ? parameters[0] : 0;

        bool printable = (character >= 32 && character <= 126)
                         || (character >= 160 && character <= 255);
        if (!printable) return;

        if (!TryReadRectangle(parameters, 1, out int top, out int left, out int bottom, out int right))
        {
            return;
        }

        for (int row = top; row <= bottom; row++)
        {
            for (int col = left; col <= right; col++)
            {
                ref var cell = ref Buffer[row, col];
                cell.Codepoint = (uint)character;
                cell.Attributes = CurrentAttributes;
                cell.Foreground = CurrentForeground;
                cell.Background = CurrentBackground;
            }
        }

        OnInvalidated();
    }

    /// <summary>
    /// DECERA and DECSERA: erase a rectangle.
    /// </summary>
    /// <param name="parameters">
    /// Top, left, bottom, right.
    /// </param>
    /// <param name="selective">
    /// True for DECSERA, which spares characters marked protected by DECSCA - the same rule the
    /// selective erases already follow.
    /// </param>
    protected virtual void EraseRectangle(ReadOnlySpan<int> parameters, bool selective)
    {
        if (!TryReadRectangle(parameters, 0, out int top, out int left, out int bottom, out int right))
        {
            return;
        }

        for (int row = top; row <= bottom; row++)
        {
            for (int col = left; col <= right; col++)
            {
                if (selective && Buffer[row, col].Attributes.HasAttribute(CharacterAttributes.Protected)) continue;

                Buffer[row, col] = TerminalCell.Empty;
            }
        }

        OnInvalidated();
    }

    /// <summary>
    /// DECCRA: copy a rectangle somewhere else.
    /// </summary>
    /// <remarks>
    /// <para><b>Overlap is the whole difficulty</b></para>
    /// A host is entitled to copy a rectangle onto one that overlaps it - that is how a program
    /// scrolls a panel by one line. Copying cell by cell in place would read cells it had already
    /// overwritten and smear the source across the destination, so the source is taken first and
    /// then written.
    ///
    /// <para><b>The page parameters are ignored</b></para>
    /// DECCRA names a source and destination PAGE. This terminal has one page, so a copy between
    /// pages is a copy on the only page there is. Said here rather than silently.
    /// </remarks>
    /// <param name="parameters">
    /// Source top, left, bottom, right, page; then destination top, left, page.
    /// </param>
    protected virtual void CopyRectangle(ReadOnlySpan<int> parameters)
    {
        if (!TryReadRectangle(parameters, 0, out int top, out int left, out int bottom, out int right))
        {
            return;
        }

        int destinationTop = (parameters.Length > 5 && parameters[5] > 0 ? parameters[5] : 1)
                             - 1 + RowAddressingOrigin;
        int destinationLeft = (parameters.Length > 6 && parameters[6] > 0 ? parameters[6] : 1) - 1;

        if (destinationTop < 0) destinationTop = 0;
        if (destinationLeft < 0) destinationLeft = 0;

        int rows = bottom - top + 1;
        int columns = right - left + 1;

        // Taken first, in full. See the remarks: an overlapping copy read in place smears.
        var source = new Buffer.TerminalCell[rows * columns];
        for (int row = 0; row < rows; row++)
        {
            for (int col = 0; col < columns; col++)
            {
                source[row * columns + col] = Buffer[top + row, left + col];
            }
        }

        for (int row = 0; row < rows; row++)
        {
            int targetRow = destinationTop + row;
            if (targetRow > Height - 1) break;

            for (int col = 0; col < columns; col++)
            {
                int targetColumn = destinationLeft + col;
                if (targetColumn > Width - 1) break;

                Buffer[targetRow, targetColumn] = source[row * columns + col];
            }
        }

        OnInvalidated();
    }

    // ─────────────────────────────────────────────────────────────
    // Column editing and region shifts
    //
    // DECIC and DECDC come from chapter 8 of the VT420 Programmer Reference; SL and SR are
    // ECMA-48, documented in xterm's ctlseqs.txt as CSI Ps SP @ and CSI Ps SP A. Both documents
    // are in spec\DEC.
    //
    // All four move whole columns of the SCROLLING REGION - every row of it, not just the cursor's
    // row, which is what separates them from ICH and DCH.
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// DECIC - inserts blank columns at the cursor, pushing the rest of the region right.
    /// </summary>
    /// <param name="count">
    /// How many columns to insert; one or more.
    /// </param>
    /// <remarks>
    /// "As columns are inserted, the columns between the cursor and the right margin move to the
    /// right. DECIC inserts blank columns with no visual character attributes. DECIC has no effect
    /// outside the scrolling margins."
    /// </remarks>
    protected virtual void InsertColumns(int count)
    {
        if (count < 1) count = 1;
        if (!CursorIsInsideTheScrollingRegion())
        {
            return;         // "no effect outside the scrolling margins"
        }

        ShiftRegionColumns(Cursor.Column, count, toTheRight: true);
    }

    /// <summary>
    /// DECBI - moves the cursor back one column, scrolling the screen right at the left margin.
    /// </summary>
    /// <remarks>
    /// <para><b>The manual, quoted</b></para>
    /// VT420 Programmer Reference: "This control function moves the cursor backward one column. If
    /// the cursor is at the left margin, all screen data within the margins moves one column to
    /// the right. The column shifted past the right margin is lost... DECBI adds a new column at
    /// the left margin, with no visual attributes. If the cursor is at the left border of the page
    /// when the terminal receives DECBI, the terminal ignores DECBI."
    ///
    /// <para><b>Two different edges</b></para>
    /// The LEFT MARGIN is where the scroll happens; the LEFT BORDER OF THE PAGE is where the
    /// sequence is ignored. With no left margin set they are the same column and the distinction
    /// never shows, which is why it is worth naming: with DECSLRM in force, a cursor at the left
    /// margin scrolls, and a cursor at column 1 outside that margin does nothing at all.
    ///
    /// <para><b>VT400 mode only</b></para>
    /// "Available in: VT400 mode only", so it is gated on the same feature flag as DECIC and DECDC
    /// and on the conformance level, exactly as those are.
    /// </remarks>
    protected virtual void BackIndex()
    {
        if (!Profile.Supports(TerminalFeatures.ColumnEditing) || IsLevel1) return;

        int leftMargin = LeftRightMarginMode ? LeftMargin : 0;

        if (Cursor.Column > leftMargin)
        {
            Cursor.Column--;
            Cursor.ClearPendingWrap();
            return;
        }

        // At the left border of the page there is nowhere to go and nothing to scroll.
        if (Cursor.Column != leftMargin) return;

        // At the left margin: the WHOLE REGION moves right and the far end falls off. Not
        // InsertColumns, which acts at the cursor - at the left margin the two happen to agree,
        // but naming the region shift says what the manual says and stays right if the cursor
        // rule ever changes. Its mirror in ForwardIndex is where the difference is real.
        ShiftRegionRight(1);
    }

    /// <summary>
    /// DECFI - moves the cursor forward one column, scrolling the screen left at the right margin.
    /// </summary>
    /// <remarks>
    /// The mirror of <see cref="BackIndex"/>. VT420 Programmer Reference: "This control function
    /// moves the cursor forward one column. If the cursor is at the right margin, all screen data
    /// within the margins moves one column to the left. The column shifted past the left margin is
    /// lost... DECFI adds a new column at the right margin, with no visual attributes. If the
    /// cursor is at the right border of the page when the terminal receives DECFI, the terminal
    /// ignores DECFI."
    /// </remarks>
    protected virtual void ForwardIndex()
    {
        if (!Profile.Supports(TerminalFeatures.ColumnEditing) || IsLevel1) return;

        int rightMargin = LeftRightMarginMode ? RightMargin : Width - 1;

        if (Cursor.Column < rightMargin)
        {
            Cursor.Column++;
            Cursor.ClearPendingWrap();
            return;
        }

        if (Cursor.Column != rightMargin) return;

        // At the right margin: the WHOLE REGION moves left, losing the column past the left
        // margin, and a blank column appears here.
        //
        // NOT DeleteColumns, which acts at the CURSOR. The cursor is at the right margin, so
        // deleting there would only blank that one column and leave everything else where it was -
        // the opposite of "all screen data within the margins moves one column to the left".
        ShiftRegionLeft(1);
    }

    /// <summary>
    /// DECDC - deletes columns at the cursor, pulling the rest of the region left.
    /// </summary>
    /// <param name="count">
    /// How many columns to delete; one or more.
    /// </param>
    /// <remarks>
    /// "As columns are deleted, the remaining columns between the cursor and the right margin move
    /// to the left. The terminal adds blank columns with no visual character attributes at the
    /// right margin. DECDC has no effect outside the scrolling margins."
    /// </remarks>
    protected virtual void DeleteColumns(int count)
    {
        if (count < 1) count = 1;
        if (!CursorIsInsideTheScrollingRegion())
        {
            return;
        }

        ShiftRegionColumns(Cursor.Column, count, toTheRight: false);
    }

    /// <summary>
    /// SL - shifts the whole scrolling region left, losing what falls off the left margin.
    /// </summary>
    /// <param name="count">
    /// How many columns to shift by; one or more.
    /// </param>
    /// <remarks>
    /// Unlike DECDC this starts at the LEFT MARGIN rather than at the cursor, and the cursor does
    /// not move. ECMA-48, and it works on any terminal here - it is not a DEC extension.
    ///
    /// IT STILL DOES NOTHING WHEN THE CURSOR IS OUTSIDE THE SCROLLING REGION, which is not
    /// something the sequence's own definition says. xterm.js's t600 fixture proves it: three
    /// SR 5 commands are sent from three different cursor positions and the captured screen has
    /// moved by five columns, not fifteen - only the one command issued from inside the region
    /// did anything. The expected screen came from real xterm, so that is the behaviour.
    /// </remarks>
    protected virtual void ShiftRegionLeft(int count)
    {
        if (count < 1) count = 1;
        if (!CursorIsInsideTheScrollingRegion())
        {
            return;
        }

        ShiftRegionColumns(FirstUsableColumn, count, toTheRight: false);
    }

    /// <summary>
    /// SR - shifts the whole scrolling region right, losing what falls off the right margin.
    /// </summary>
    /// <param name="count">
    /// How many columns to shift by; one or more.
    /// </param>
    protected virtual void ShiftRegionRight(int count)
    {
        if (count < 1) count = 1;
        if (!CursorIsInsideTheScrollingRegion())
        {
            return;     // see ShiftRegionLeft
        }

        ShiftRegionColumns(FirstUsableColumn, count, toTheRight: true);
    }

    /// <summary>
    /// Whether the cursor is inside the region DECIC and DECDC are allowed to act on.
    /// </summary>
    private bool CursorIsInsideTheScrollingRegion()
        => Cursor.Row >= ScrollTop && Cursor.Row <= ScrollBottom
        && Cursor.Column >= FirstUsableColumn && Cursor.Column <= LastUsableColumn(Cursor.Row);

    /// <summary>
    /// Moves every row of the scrolling region sideways from a starting column, filling the gap
    /// with blanks that carry no attributes.
    /// </summary>
    /// <param name="from">
    /// The first column affected.
    /// </param>
    /// <param name="count">
    /// How many columns to move by.
    /// </param>
    /// <param name="toTheRight">
    /// True to push right (insert), false to pull left (delete).
    /// </param>
    private void ShiftRegionColumns(int from, int count, bool toTheRight)
    {
        for (int row = ScrollTop; row <= ScrollBottom; row++)
        {
            int last = LastUsableColumn(row);
            if (from > last)
            {
                continue;
            }

            int span = last - from + 1;
            int moved = count >= span ? 0 : span - count;

            if (toTheRight)
            {
                // Work backwards so a cell is read before the one that overwrites it.
                for (int i = moved - 1; i >= 0; i--)
                {
                    Buffer[row, from + count + i] = Buffer[row, from + i];
                }
                for (int i = 0; i < count && from + i <= last; i++)
                {
                    Buffer[row, from + i] = TerminalCell.Empty;
                }
            }
            else
            {
                for (int i = 0; i < moved; i++)
                {
                    Buffer[row, from + i] = Buffer[row, from + count + i];
                }
                for (int i = 0; i < count && last - i >= from; i++)
                {
                    Buffer[row, last - i] = TerminalCell.Empty;
                }
            }
        }

        OnInvalidated();
    }

    // ─────────────────────────────────────────────────────────────
    // Conformance levels (DECSCL)
    //
    // From chapter 4 of the VT420 Programmer Reference, "Emulating VT Series Terminals", held in
    // spec\DEC. A VT420 can be told to behave as an earlier terminal, and hosts written for a
    // VT100 use this to make sure of what they are talking to.
    //
    // There are two levels, not four: the manual says "Level 1 for VT100 operation" and "Level 4
    // for VT200, VT300, and VT400 operation", with level 4 including levels 2 and 3. So 62, 63 and
    // 64 all select the same behaviour, which is why this holds a level number of 1 or 4 rather
    // than the parameter that was sent.
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Which terminal this behaves as: 1 for VT100, 4 for the VT200/VT300/VT400 series.
    /// </summary>
    /// <remarks>
    /// The factory default is level 4, exactly as the manual states.
    /// </remarks>
    public int ConformanceLevel { get; private set; } = 4;

    /// <summary>
    /// Whether C1 controls are sent to the host as single 8-bit characters rather than as
    /// two-character escape sequences.
    /// </summary>
    /// <remarks>
    /// DECSCL's second parameter selects this, and S7C1T and S8C1T change it on their own. The
    /// manual's default is 7-bit controls, which is what every host can read.
    /// </remarks>
    public bool Send8BitControls { get; private set; }

    /// <summary>
    /// The level number the host last asked for, as it asked for it: 61, 62, 63 or 64.
    /// </summary>
    /// <remarks>
    /// <see cref="ConformanceLevel"/> collapses 62, 63 and 64 into one, because this terminal
    /// behaves the same for all three. DECRQSS cannot: its whole design is that the answer is the
    /// sequence that would put the setting back, so answering 64 to a host that asked for 62 would
    /// hand it an instruction it never gave. Kept separately for that one purpose.
    /// </remarks>
    public int ConformanceLevelAsRequested { get; private set; } = 64;

    /// <summary>
    /// DECSCL - selects the operating level.
    /// </summary>
    /// <param name="level">
    /// 61 for VT100 operation; 62, 63 or 64 for VT400 operation.
    /// </param>
    /// <param name="controls">
    /// 1 selects 7-bit C1 controls; 0, 2 and a missing parameter all select 8-bit.
    /// </param>
    /// <remarks>
    /// CHANGING THE LEVEL PERFORMS A HARD RESET. The manual is explicit about it, and it is the
    /// part an emulator is most likely to leave out: a host that drops the terminal to VT100 and
    /// then finds a scrolling region still set would be reading state from a terminal that, as far
    /// as it is concerned, has just been switched on.
    ///
    /// Selecting the level already in force still resets - the manual attaches the reset to the
    /// sequence, not to a change of value.
    /// </remarks>
    protected virtual void SelectConformanceLevel(int level, int controls)
    {
        int wanted;
        switch (level)
        {
            case 61: wanted = 1; break;
            case 62:
            case 63:
            case 64: wanted = 4; break;
            default: return;      // not a level this terminal knows; leave everything alone
        }

        // "Set conformance level (DECSCL): Exits status line." The hard reset below covers it, but
        // the rule is written down separately in the manual and is not a consequence of the reset:
        // DECSTR exits the status line too and resets far less.
        ExitStatusLine();

        Reset();

        ConformanceLevel = wanted;
        ConformanceLevelAsRequested = level;

        // Level 1 has no 8-bit controls at all: "The terminal sends all C1 control characters as
        // 7-bit escape sequences."
        Send8BitControls = wanted > 1 && controls != 1;
    }

    /// <summary>
    /// Whether the terminal is pretending to be a VT100, in which case the functions listed in the
    /// manual's Table 4-1 do nothing.
    /// </summary>
    protected bool IsLevel1 => ConformanceLevel <= 1;

    // ─────────────────────────────────────────────────────────────
    // Page memory (VT320 / VT420)
    //
    // From the VT420 Programmer Reference, chapter 6, held in spec\DEC. The terminal holds 144
    // lines of memory and divides them into equal pages: DECSLPP picks the page length and the
    // number of pages follows from it. The screen shows one page; a host writes to another by
    // moving to it first.
    //
    // WHAT IS AND IS NOT BUILT. The movement functions and the page count are real. The 144-line
    // geometry is NOT: a page here is one screenful, whatever height the screen happens to be,
    // because this emulator's height is set by the window rather than by the terminal. DECSNLS
    // (choosing how many of a page's lines to display) is not built either. Those are recorded in
    // the validation status document rather than guessed at.
    // ─────────────────────────────────────────────────────────────

    // ─────────────────────────────────────────────────────────────
    // The status line (VT320 / VT340)
    //
    // From the VT330/VT340 Text Programming manual, chapter 11, "Selecting the Indicator or
    // Host-Writable Status Line": "The twenty-fifth line at the bottom of the screen is reserved
    // for the status line. The terminal lets you use the status line in two ways - as an indicator
    // of the terminal's current state, or as a window the host can use to display
    // application-specific messages."
    //
    // WHAT IS AND IS NOT BUILT. The two control functions, the line's contents and the rules for
    // leaving it are real and tested. THE GEOMETRY IS NOT: a real VT340 shows 24 lines of main
    // display and a 25th line below them, while here the status line is a row of its own that the
    // main display never gives up, because this emulator's height comes from the window. Nothing
    // DRAWS it yet either - whether a status line looks right is a question only Ronny can answer,
    // so it is left until there is something to look at. Both recorded here rather than implied.
    //
    // The indicator status line - session number, page, cursor position, printer and modem state -
    // is not built at all. It is the terminal talking about itself, and half of what it reports
    // (modem state, dual sessions) has no counterpart here.
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Which kind of status line the host has asked for with DECSSDT.
    /// </summary>
    public enum StatusLineKind
    {
        /// <summary>
        /// No status line. "The 25th line is blank."
        /// </summary>
        None = 0,

        /// <summary>
        /// The terminal's own indicator line, which is the power-on default.
        /// </summary>
        Indicator = 1,

        /// <summary>
        /// A line the host writes into.
        /// </summary>
        HostWritable = 2,
    }

    /// <summary>
    /// The status line the host has selected. Indicator at power-on, as the manual says.
    /// </summary>
    public StatusLineKind StatusLineType { get; private set; } = StatusLineKind.Indicator;

    /// <summary>
    /// Whether DECSASD has pointed output at the status line rather than the main display.
    /// </summary>
    public bool WritingToStatusLine { get; private set; }

    /// <summary>
    /// The characters the host has written into the status line.
    /// </summary>
    private char[] _statusLineText = Array.Empty<char>();

    /// <summary>
    /// Where the next character written to the status line goes.
    /// </summary>
    private int _statusLineColumn;

    /// <summary>
    /// What the host has put on the status line, with trailing blanks removed.
    /// </summary>
    public string StatusLineText
    {
        get
        {
            int end = _statusLineText.Length;
            while (end > 0 && _statusLineText[end - 1] == ' ') end--;
            return end == 0 ? string.Empty : new string(_statusLineText, 0, end);
        }
    }

    /// <summary>
    /// DECSSDT - chooses the kind of status line.
    /// </summary>
    /// <param name="kind">
    /// 0 for none, 1 for the indicator line, 2 for a host-writable one.
    /// </param>
    /// <remarks>
    /// "If you change from an indicator to a host-writable status line, the new host-writable
    /// status line is empty" - so the change clears it rather than showing whatever the host wrote
    /// last time.
    /// </remarks>
    protected virtual void SelectStatusLineType(int kind)
    {
        if (kind < 0 || kind > 2) return;

        var wanted = (StatusLineKind)kind;
        if (wanted == StatusLineType) return;

        StatusLineType = wanted;
        ClearStatusLine();

        // Anything other than a host-writable line has nowhere for host output to go, so output
        // comes back to the main display. THIS IS AN ASSUMPTION, not something the manual states:
        // it says what DECSASD does and what DECSSDT does, but not what happens to one when the
        // other changes underneath it. Leaving output pointed at a line that no longer accepts it
        // would silently swallow everything the host printed, which is the worse of the two.
        if (wanted != StatusLineKind.HostWritable)
        {
            WritingToStatusLine = false;
        }

        OnInvalidated();
    }

    /// <summary>
    /// DECSASD - chooses whether host output goes to the main display or to the status line.
    /// </summary>
    /// <param name="display">
    /// 0 for the main display, 1 for the status line.
    /// </param>
    /// <remarks>
    /// "The main display is the first 24 lines on the screen. The status line is the twenty-fifth
    /// line." Selecting the status line while the terminal is showing its own indicator line is
    /// refused: that line belongs to the terminal, and accepting the switch would send the host's
    /// text nowhere. ALSO AN ASSUMPTION - see SelectStatusLineType for why it is made this way.
    /// </remarks>
    protected virtual void SelectActiveStatusDisplay(int display)
    {
        if (display == 0)
        {
            WritingToStatusLine = false;
            return;
        }

        if (display != 1) return;
        if (StatusLineType != StatusLineKind.HostWritable) return;

        WritingToStatusLine = true;
        _statusLineColumn = 0;
    }

    /// <summary>
    /// Empties the status line and puts its cursor back at the first column.
    /// </summary>
    protected void ClearStatusLine()
    {
        if (_statusLineText.Length != Width)
        {
            _statusLineText = new char[Width];
        }

        for (int col = 0; col < _statusLineText.Length; col++)
        {
            _statusLineText[col] = ' ';
        }

        _statusLineColumn = 0;
    }

    /// <summary>
    /// Leaves the status line without erasing it.
    /// </summary>
    /// <remarks>
    /// "Set conformance level (DECSCL): Exits status line" and "Soft terminal reset (DECSTR):
    /// Exits status line" - the manual's table of exceptions. A hard reset does more: "Hard
    /// terminal reset (RIS): Erases and exits the status line."
    /// </remarks>
    protected void ExitStatusLine()
    {
        WritingToStatusLine = false;
    }

    /// <summary>
    /// Writes one character to the status line instead of to the screen.
    /// </summary>
    /// <param name="codepoint">
    /// The character, already mapped through the active character set.
    /// </param>
    /// <remarks>
    /// "When you select the host-writable status line, most of the control functions that affect
    /// the main display also affect the status line", with a short list of exceptions - of which
    /// the one that shapes this is "Only the column parameters in cursor positioning commands
    /// operate in the status line". There is one row and no scrolling, so a character past the
    /// last column is dropped rather than wrapped.
    /// </remarks>
    private void WriteToStatusLine(uint codepoint)
    {
        if (_statusLineText.Length != Width)
        {
            ClearStatusLine();
        }

        if (_statusLineColumn < 0 || _statusLineColumn >= _statusLineText.Length) return;

        _statusLineText[_statusLineColumn] = (char)codepoint;
        _statusLineColumn++;

        OnInvalidated();
    }

    /// <summary>
    /// Contents of the pages that are not on screen. Null until a second page exists.
    /// </summary>
    private TerminalCell[][]? _pageStore;

    /// <summary>
    /// Cursor position on each stored page, so returning to one lands where it was left.
    /// </summary>
    private (int Row, int Col)[]? _pageCursors;

    /// <summary>
    /// How many pages the terminal's memory is divided into. One page means no page memory in use.
    /// </summary>
    public int PageCount { get; private set; } = 1;

    /// <summary>
    /// Which page is on screen, numbered from 1 as the manual numbers them.
    /// </summary>
    public int CurrentPage { get; private set; } = 1;

    /// <summary>
    /// DECSLPP - sets the lines per page, and with it how many pages the memory divides into.
    /// </summary>
    /// <param name="linesPerPage">
    /// One of the lengths the terminal offers. The VT420 manual's table (page 140) gives, for one
    /// session: 24 lines to 6 pages, 25 to 5, 36 to 4, 48 to 3, 72 to 2 and 144 to a single page.
    /// </param>
    /// <remarks>
    /// A length the terminal does not offer is IGNORED rather than rounded to a neighbour. The
    /// manual lists exactly six, and inventing a seventh would let a host believe in a page layout
    /// no VT420 could produce.
    /// </remarks>
    protected virtual void SetLinesPerPage(int linesPerPage)
    {
        int pages;
        switch (linesPerPage)
        {
            case 24: pages = 6; break;
            case 25: pages = 5; break;
            case 36: pages = 4; break;
            case 48: pages = 3; break;
            case 72: pages = 2; break;
            case 144: pages = 1; break;
            default: return;
        }

        SetPageCount(pages);
    }

    /// <summary>
    /// Changes how many pages exist, keeping what is on the pages that survive.
    /// </summary>
    /// <param name="pages">
    /// The new page count, one or more.
    /// </param>
    private void SetPageCount(int pages)
    {
        if (pages < 1) pages = 1;
        if (pages == PageCount) return;

        // Come back to a page that will still exist BEFORE the count changes, because the move
        // itself is refused once there is only one page - which left the screen claiming to be on
        // page four of one.
        if (pages < PageCount && CurrentPage > pages)
        {
            GoToPage(pages, keepCursor: false);
        }

        var store = new TerminalCell[pages][];
        var cursors = new (int Row, int Col)[pages];

        int carried = Math.Min(pages, PageCount);
        for (int i = 0; i < carried; i++)
        {
            if (_pageStore != null) store[i] = _pageStore[i];
            if (_pageCursors != null) cursors[i] = _pageCursors[i];
        }

        _pageStore = store;
        _pageCursors = cursors;
        PageCount = pages;

        if (CurrentPage > PageCount)
        {
            CurrentPage = PageCount;
        }
    }

    /// <summary>
    /// Moves to a page, saving the one being left and restoring the one being entered.
    /// </summary>
    /// <param name="page">
    /// Target page, numbered from 1. Out-of-range values stop at the first or last page.
    /// </param>
    /// <param name="keepCursor">
    /// True for PPA, PPB and PPR, which land on the CORRESPONDING row and column of the new page.
    /// False for NP and PP, which land on its HOME position. That difference is the whole reason
    /// there are five of these functions rather than three.
    /// </param>
    protected virtual void GoToPage(int page, bool keepCursor)
    {
        // "If there is only one page, the terminal ignores NP" - and the same for the others.
        if (PageCount <= 1) return;

        if (page < 1) page = 1;
        if (page > PageCount) page = PageCount;
        if (page == CurrentPage) return;

        EnsurePageStore();

        int width = Buffer.Width;
        int height = Buffer.Height;

        // Save the page being left, contents and cursor alike.
        var leaving = new TerminalCell[width * height];
        for (int row = 0; row < height; row++)
        {
            for (int col = 0; col < width; col++)
            {
                Buffer.TryGetCell(row, col, out leaving[row * width + col]);
            }
        }
        _pageStore![CurrentPage - 1] = leaving;
        _pageCursors![CurrentPage - 1] = (Cursor.Row, Cursor.Column);

        // Load the page being entered. A page never written to is blank, which is what a terminal
        // that has just divided its memory shows.
        var entering = _pageStore[page - 1];
        for (int row = 0; row < height; row++)
        {
            for (int col = 0; col < width; col++)
            {
                Buffer[row, col] = entering != null && row * width + col < entering.Length
                    ? entering[row * width + col]
                    : TerminalCell.Empty;
            }
        }

        if (keepCursor)
        {
            // The row and column stay; only the page changes.
            Cursor.MoveTo(Cursor.Row, Cursor.Column);
        }
        else
        {
            Cursor.MoveTo(0, FirstUsableColumn);
        }

        CurrentPage = page;
        OnInvalidated();
    }

    /// <summary>
    /// Makes sure the page arrays exist and are the right length.
    /// </summary>
    private void EnsurePageStore()
    {
        if (_pageStore == null || _pageStore.Length != PageCount)
        {
            _pageStore = new TerminalCell[PageCount][];
        }
        if (_pageCursors == null || _pageCursors.Length != PageCount)
        {
            _pageCursors = new (int Row, int Col)[PageCount];
        }
    }

    /// <summary>
    /// Whether DECCARA and DECRARA treat their four numbers as a rectangle or as a stream.
    /// </summary>
    /// <remarks>
    /// <para><b>The default is STREAM, and that is easy to get wrong</b></para>
    /// With DECSACE at its power-on value the four numbers are a START and an END position, and the
    /// change runs from one to the other the way TEXT does - to the end of the start row, across
    /// every row between, and up to the end column on the last one. Only DECSACE 2 makes it the
    /// rectangle the name suggests.
    ///
    /// Implementing the rectangle and calling it done would look right in every test whose region
    /// happens to span whole rows, and be wrong for every real use.
    /// </remarks>
    public bool AttributeChangeIsRectangular { get; protected set; }

    /// <summary>
    /// DECSACE: choose the extent the attribute commands apply to.
    /// </summary>
    /// <param name="mode">
    /// 2 for a rectangle; 0 and 1 both mean a stream.
    /// </param>
    protected virtual void SelectAttributeChangeExtent(int mode)
    {
        AttributeChangeIsRectangular = mode == 2;
    }

    /// <summary>
    /// DECCARA and DECRARA: change or reverse character attributes over an area.
    /// </summary>
    /// <remarks>
    /// <para><b>What can be changed</b></para>
    /// Only the four attributes DEC names - bold, underline, blink and reverse - plus 0 for "all
    /// off". Colour is deliberately not among them: these commands predate SGR colour, and a host
    /// asking for something outside the set gets nothing rather than a guess.
    /// </remarks>
    /// <param name="parameters">
    /// Top, left, bottom, right, then the attribute selectors.
    /// </param>
    /// <param name="reverse">
    /// True for DECRARA, which TOGGLES each named attribute rather than setting it.
    /// </param>
    protected virtual void ChangeAttributesInArea(ReadOnlySpan<int> parameters, bool reverse)
    {
        if (!TryReadRectangle(parameters, 0, out int top, out int left, out int bottom, out int right))
        {
            return;
        }

        CharacterAttributes on = CharacterAttributes.None;
        CharacterAttributes off = CharacterAttributes.None;
        bool clearEverything = false;

        for (int i = 4; i < parameters.Length; i++)
        {
            switch (parameters[i])
            {
                case 0: clearEverything = true; break;
                case 1: on |= CharacterAttributes.Bold; break;
                case 4: on |= CharacterAttributes.Underline; break;
                case 5: on |= CharacterAttributes.Blink; break;
                case 7: on |= CharacterAttributes.Reverse; break;
                case 22: off |= CharacterAttributes.Bold; break;
                case 24: off |= CharacterAttributes.Underline; break;
                case 25: off |= CharacterAttributes.Blink; break;
                case 27: off |= CharacterAttributes.Reverse; break;
            }
        }

        var toggled = reverse ? on : CharacterAttributes.None;

        for (int row = top; row <= bottom; row++)
        {
            // The stream form runs to the end of every row except the last, and starts at the left
            // edge on every row except the first. The rectangle form uses the same columns
            // throughout. See AttributeChangeIsRectangular.
            int from = AttributeChangeIsRectangular || row == top ? left : 0;
            int to = AttributeChangeIsRectangular || row == bottom ? right : Width - 1;

            for (int col = from; col <= to && col < Width; col++)
            {
                ref var cell = ref Buffer[row, col];

                if (clearEverything)
                {
                    // 0 clears the four this command owns, and leaves everything else - a
                    // protected cell must not stop being protected because a host changed its
                    // rendition.
                    cell.Attributes &= ~(CharacterAttributes.Bold | CharacterAttributes.Underline
                                         | CharacterAttributes.Blink | CharacterAttributes.Reverse);
                    continue;
                }

                if (reverse)
                {
                    cell.Attributes ^= toggled;
                    continue;
                }

                cell.Attributes |= on;
                cell.Attributes &= ~off;
            }
        }

        OnInvalidated();
    }

    /// <summary>
    /// Opens a gap, pushing what was there towards the right margin.
    /// </summary>
    /// <remarks>
    /// Bounded by the MARGINS, not by the screen. Characters pushed past the right margin fall off
    /// the region rather than spilling into the columns beside it, and a cursor outside the region
    /// does nothing at all - which is what "the margins confine editing" means in practice.
    /// </remarks>
    /// <param name="row">
    /// The row to shift.
    /// </param>
    /// <param name="col">
    /// Where the gap opens.
    /// </param>
    /// <param name="count">
    /// How many columns wide the gap is.
    /// </param>
    protected virtual void ShiftCharactersRight(int row, int col, int count)
    {
        int left = FirstUsableColumn;
        int right = LastUsableColumn(row);

        if (col < left || col > right) return;

        DropRegisColours(row);

        for (int i = right; i >= col + count; i--)
        {
            Buffer[row, i] = Buffer[row, i - count];
        }

        for (int i = col; i < col + count && i <= right; i++)
        {
            Buffer[row, i].Clear();
        }
    }

    /// <summary>
    /// Closes a gap, pulling what follows back towards the cursor.
    /// </summary>
    /// <remarks>
    /// Bounded by the margins for the same reason as <see cref="ShiftCharactersRight"/>: the blanks
    /// that appear come in at the RIGHT MARGIN, not the right edge of the screen, so text outside
    /// the region is not dragged into it.
    /// </remarks>
    /// <param name="row">
    /// The row to shift.
    /// </param>
    /// <param name="col">
    /// Where the gap closes.
    /// </param>
    /// <param name="count">
    /// How many columns are removed.
    /// </param>
    protected virtual void DeleteCharacters(int row, int col, int count)
    {
        int left = FirstUsableColumn;
        int right = LastUsableColumn(row);

        if (col < left || col > right) return;

        DropRegisColours(row);

        for (int i = col; i <= right - count; i++)
        {
            Buffer[row, i] = Buffer[row, i + count];
        }

        for (int i = Math.Max(col, right - count + 1); i <= right; i++)
        {
            Buffer[row, i].Clear();
        }
    }

    /// <summary>
    /// Takes the ReGIS pixel values off a whole row of text.
    /// </summary>
    /// <remarks>
    /// Called when characters SHIFT SIDEWAYS - delete character, or insert mode opening a gap.
    /// hackerb9 measured both on a VT340 and both lose the colour: "Colors are removed from the
    /// line when text is shifted left by Esc [ P", and the same for insert mode. Scrolling up and
    /// down keeps it, and that needs no code here because scrolling moves whole cells and the
    /// value rides along inside them.
    /// The reason the two behave differently is worth keeping: a vertical scroll moves the bitmap
    /// with the text, while a sideways shift redraws the characters in their new places - and
    /// drawing a character writes the ordinary text value over whatever was there.
    /// </remarks>
    /// <param name="row">
    /// Row to strip.
    /// </param>
    private void DropRegisColours(int row)
    {
        if (row < 0 || row >= Buffer.Height) return;

        for (int col = 0; col < Buffer.Width; col++)
        {
            ref TerminalCell cell = ref Buffer[row, col];
            if (cell.HasRegisColor) cell.RegisColorIndex = -1;
        }
    }

    protected virtual void EraseCharacters(int row, int col, int count)
    {
        for (int i = col; i < col + count && i < Width; i++)
        {
            Buffer[row, i] = TerminalCell.Empty; // Erased, not a written space
        }
    }

    protected void SendResponse(ReadOnlySpan<byte> data)
    {
        // Nothing to say means say nothing. A terminal whose profile carries no DA reply - the
        // Tektronix 4014 predates device attributes entirely - would otherwise put a zero-byte
        // write on the line every time a host probed it.
        if (data.Length == 0) return;

        DataToSend?.Invoke(data.ToArray());
    }

    /// <summary>
    /// Resizes the terminal
    /// </summary>
    public virtual void Resize(int newWidth, int newHeight)
    {
        // Reflow, so a paragraph that wrapped is re-laid out at the new width instead of being cut
        // off at the edge. The cursor travels with the text it was sitting in, not with the grid
        // position it happened to occupy — widening a window must not strand it mid-paragraph.
        // ResizeWithReflow declines to reflow the alternate buffer, where a full-screen program is
        // about to repaint anyway.
        int cursorRow = Cursor.Row;
        int cursorColumn = Cursor.Column;
        Buffer.ResizeWithReflow(newWidth, newHeight, ref cursorRow, ref cursorColumn);

        // Re-bound the cursor IN PLACE. Building a replacement and copying back Row/Column left
        // the live cursor with the old _maxRows/_maxCols, so after a shrink it stopped wrapping at
        // the right margin and the next character indexed past the buffer.
        Cursor.SetDimensions(newHeight, newWidth);
        Cursor.Row = cursorRow;
        Cursor.Column = cursorColumn;

        // Reset scroll region
        ScrollTop = 0;
        ScrollBottom = newHeight - 1;

        // Margins are columns, so a width change invalidates them the same way tab stops are
        // invalidated. Keeping a right margin of 100 on an 80-column screen would confine text to
        // a region that no longer exists.
        ClearLeftRightMargins();

        // Tab stops are one flag per column, so a width change invalidates the array.
        ResetTabStops();

        OnInvalidated();
    }

    /// <summary>
    /// The widest and narrowest screen a host is allowed to ask for.
    /// </summary>
    /// <remarks>
    /// A ceiling rather than trust: <c>CSI 8 t</c> carries whatever number the host typed, and a
    /// request for a million columns would allocate a million cells per row before anything else
    /// noticed. 512 is comfortably past every real terminal width and cheap to hold.
    /// </remarks>
    private const int MinimumHostRequestedSize = 1;

    /// <summary>
    /// See <see cref="MinimumHostRequestedSize"/>.
    /// </summary>
    private const int MaximumHostRequestedSize = 512;

    /// <summary>
    /// The two widths DECCOLM switches between.
    /// </summary>
    private const int DecColmNarrowWidth = 80;

    /// <summary>
    /// See <see cref="DecColmNarrowWidth"/>.
    /// </summary>
    private const int DecColmWideWidth = 132;

    /// <summary>
    /// DECCOLM: 132 columns when set, 80 when reset.
    /// </summary>
    /// <remarks>
    /// <para><b>Why the screen is cleared</b></para>
    /// The VT100 manual is explicit that changing DECCOLM erases the screen and homes the cursor,
    /// and it does so whether or not the width actually changed - the hardware re-timed the video
    /// and whatever was on screen did not survive it. Software written for those terminals leans on
    /// that: a host switches to 132 columns and starts drawing without clearing first. Keeping the
    /// old text would leave the previous screen showing through the new one.
    ///
    /// DECNCSM (mode 95) is the later VT500 mode that suppresses exactly this clear. It is NOT
    /// implemented here, so the clear is unconditional - stated rather than left to be discovered.
    ///
    /// <para><b>Height is not touched</b></para>
    /// DECCOLM is about columns. The row count belongs to the window, and a host wanting both
    /// asks with <c>CSI 8 t</c> instead.
    /// </remarks>
    /// <param name="wide">
    /// True for 132 columns, false for 80.
    /// </param>
    protected virtual void ApplyColumnMode(bool wide)
    {
        // Not every terminal here has a screen that can change size. See TerminalFeatures.HostResize.
        if (!Profile.Supports(TerminalFeatures.HostResize)) return;

        Resize(wide ? DecColmWideWidth : DecColmNarrowWidth, Height);

        Buffer.Clear();

        // Homes the cursor as well as resetting the margins - which is the other half of what
        // DECCOLM does, so it is not called twice here.
        ClearScrollingRegion();
        OnInvalidated();
    }

    /// <summary>
    /// The xterm window-manipulation sequence <c>CSI Ps ; Ps ; Ps t</c>, for the operations that
    /// concern the text area.
    /// </summary>
    /// <remarks>
    /// <para><b>Implemented</b></para>
    /// The three that are about the character grid:
    ///  - <c>CSI 8 ; rows ; cols t</c> resizes the text area. A zero or missing value keeps the
    ///    current one, which is how a host changes only the width.
    ///  - <c>CSI 18 t</c> reports the text area as <c>CSI 8 ; rows ; cols t</c>.
    ///  - <c>CSI 19 t</c> reports the screen size as <c>CSI 9 ; rows ; cols t</c>. There is no
    ///    desktop behind this terminal that is larger than its own screen, so the two are the same
    ///    numbers - said plainly rather than reported as a guess about a window manager.
    ///
    /// <para><b>Deliberately ignored</b></para>
    /// Everything else in the sequence - iconify, raise, lower, move the window, report the icon
    /// label - asks the window manager to do something on the host's behalf. Those are a security
    /// question as much as a feature (a host that can move and retitle a window can impersonate
    /// one), and none of them is needed to run a program. They are dropped, not queued.
    /// </remarks>
    /// <param name="parameters">
    /// The numeric parameters, in order. The first selects the operation.
    /// </param>
    protected virtual void HandleWindowManipulation(ReadOnlySpan<int> parameters)
    {
        if (!Profile.Supports(TerminalFeatures.HostResize)) return;
        if (parameters.Length == 0) return;

        switch (parameters[0])
        {
            case 8:
            {
                int rows = parameters.Length > 1 ? parameters[1] : 0;
                int columns = parameters.Length > 2 ? parameters[2] : 0;

                // 0 means "leave this one alone" - xterm's rule, and the one that lets a host set
                // the width without having to know the height it is already at.
                if (rows <= 0) rows = Height;
                if (columns <= 0) columns = Width;

                rows = ClampHostRequestedSize(rows);
                columns = ClampHostRequestedSize(columns);

                if (rows == Height && columns == Width) return;

                Resize(columns, rows);
                return;
            }

            case 18:
                SendResponse(Encoding.ASCII.GetBytes($"\x1b[8;{Height};{Width}t"));
                return;

            case 19:
                SendResponse(Encoding.ASCII.GetBytes($"\x1b[9;{Height};{Width}t"));
                return;

            default:
                // A window-manager operation. See the remarks: dropped on purpose.
                return;
        }
    }

    /// <summary>
    /// Holds a host-requested row or column count inside what this terminal will build.
    /// </summary>
    /// <param name="value">
    /// The number the host asked for.
    /// </param>
    /// <returns>
    /// The same number, brought inside <see cref="MinimumHostRequestedSize"/> and
    /// <see cref="MaximumHostRequestedSize"/>.
    /// </returns>
    private static int ClampHostRequestedSize(int value)
    {
        if (value < MinimumHostRequestedSize) return MinimumHostRequestedSize;
        if (value > MaximumHostRequestedSize) return MaximumHostRequestedSize;
        return value;
    }

    /// <summary>
    /// Gets the terminal type identifier
    /// </summary>
    public virtual string GetTerminalType()
    {
        // The profile is the single source of what this terminal calls itself. The old "Terminal"
        // placeholder meant every emulator that did not bother to override this lied about itself.
        return Profile.Name;
    }

    /// <summary>
    /// Gets the terminal capabilities
    /// </summary>
    public virtual string GetTerminalCapabilities()
    {
        return "Basic";
    }

    /// <summary>
    /// Start of a DCS (Device Control String): the introducer has been parsed, so
    /// <paramref name="parser"/> carries its parameters, intermediates and final byte.
    /// Payload then arrives through <see cref="HandleDCSData"/>, and
    /// <see cref="HandleDCSEnd"/> marks the end.
    ///
    /// The three form one stream - a terminal that supports Sixel (<c>DCS q</c>), ReGIS
    /// (<c>DCS p</c>) or DECUDK (<c>DCS |</c>) decides here whether it wants the payload,
    /// then consumes it incrementally rather than buffering the whole image.
    /// </summary>
    /// <param name="parser">
    /// The parser, positioned at the end of the DCS introducer.
    /// </param>
    protected virtual void HandleDCSSequence(EscapeSequenceParser parser)
    {
        // DECUDK is DCS Pc ; Pl | ... ST. The '|' final byte is what separates it from Sixel
        // (DCS q) and ReGIS (DCS p), which arrive through the same three hooks.
        if (parser.FinalByte == (byte)'|' && Profile.Supports(TerminalFeatures.UserDefinedKeys))
        {
            BeginUserDefinedKeys(parser.GetParam(0, 0), parser.GetParam(1, 0));
            return;
        }

        // DECDLD is DCS ... { Dscs <glyphs> ST. The '{' final byte is what marks it.
        if (parser.FinalByte == (byte)'{' && Profile.Supports(TerminalFeatures.SoftCharacterSet))
        {
            BeginSoftFont(parser);
            return;
        }

        // DECRQSS is DCS $ q <setting> ST - "what is your current X?". IT COMES BEFORE SIXEL, and
        // that order is the whole of the fix: Sixel owns the same final byte, its branch tested
        // only the final byte, and so on a terminal with Sixel - a VT340, the very terminal the
        // status line was being added for - EVERY DECRQSS was swallowed as the start of an image
        // and answered with silence. The '$' intermediate is what separates them, and the check
        // for it has to happen first.
        if (parser.FinalByte == (byte)'q'
            && parser.Intermediates.Length == 1 && parser.Intermediates[0] == (byte)'$')
        {
            _collectingSettingRequest = true;
            _settingRequest.Clear();
            return;
        }

        // Sixel is DCS ... q <image> ST, with no intermediate at all.
        if (parser.FinalByte == (byte)'q' && parser.Intermediates.Length == 0
            && Profile.Supports(TerminalFeatures.Sixel))
        {
            // Pa, the pixel aspect ratio, is the FIRST DCS parameter - "DCS Pa ; Pb ; Ph q" in
            // the xterm control sequences document held in spec\DEC\. It matters only when the
            // image carries no raster attributes, and an omitted Pa is 0, which means 2:1. Reading
            // it here rather than in the decoder keeps the decoder free of the DCS format.
            var sixelParameters = parser.Parameters;
            _sixelAspectParameter = sixelParameters.Length > 0 ? sixelParameters[0] : 0;
            _sixelBackgroundOption = sixelParameters.Length > 1 ? sixelParameters[1] : 0;
            _collectingSixel = true;
            _sixelPayload.Clear();
            return;
        }

        // ReGIS is DCS ... p <commands> ST.
        if (parser.FinalByte == (byte)'p' && Profile.Supports(TerminalFeatures.ReGIS))
        {
            _collectingRegis = true;
            _regisPayload.Clear();
            return;
        }

        // A device control string nothing above claimed. Counted, because this is a whole PAYLOAD
        // being dropped rather than a single sequence - real PED opens by sending four of them,
        // "L10" through "L40", and until this counted them they vanished without trace.
        CountUnrecognisedSequence("DCS " + (char)parser.FinalByte);

        _collectingUserDefinedKeys = false;
        _collectingSoftFont = false;
        _collectingSixel = false;
        _collectingRegis = false;
        _collectingSettingRequest = false;
    }

    /// <summary>
    /// Whether a DECUDK payload is being collected right now.
    /// </summary>
    private bool _collectingUserDefinedKeys;

    /// <summary>
    /// Whether this DECUDK should lock the keys when it finishes.
    /// </summary>
    private bool _userDefinedKeysWillLock;

    /// <summary>
    /// The payload of the DECUDK in flight.
    /// </summary>
    private readonly StringBuilder _userDefinedKeyPayload = new StringBuilder();

    /// <summary>
    /// Whether this terminal is currently answering as a VT52 rather than in ANSI.
    /// </summary>
    /// <remarks>
    /// <para><b>DECANM, and why a VT100 needs it</b></para>
    /// <c>CSI ? 2 l</c> drops a VT-family terminal into VT52 mode and ESC followed by a less-than sign brings it
    /// back. That is not a curiosity: it is how software of the period used a VT100 when it only
    /// knew how to drive the older machine, and a terminal that ignored the mode would answer ANSI
    /// to a host that had explicitly asked it not to.
    /// </remarks>
    public bool Vt52Mode { get; protected set; }

    /// <summary>
    /// Whether the VT52 graphics character set is invoked, from <c>ESC F</c> and <c>ESC G</c>.
    /// </summary>
    /// <remarks>
    /// The VT52's own set - arrows, a degree sign, fractions and scan lines - is not the DEC
    /// Special Graphics set a VT100 uses, and this emulator does not carry its glyphs. The flag is
    /// tracked so the state is right and a host turning it on and off is followed; the characters
    /// are drawn from the ordinary set until someone has the ROM.
    /// </remarks>
    protected bool Vt52GraphicsCharacterSet { get; private set; }

    /// <summary>
    /// Interprets a VT52 escape sequence: a bare ESC and one letter.
    /// </summary>
    /// <remarks>
    /// <para><b>One copy, two callers</b></para>
    /// The VT52 emulator always uses this; a VT-family terminal in VT52 mode uses it too. Two
    /// copies of ESC A through ESC Z is how one of them ends up fixed and the other does not - the
    /// trap this repository has a section about.
    ///
    /// <para><b>ESC Y reads two RAW bytes</b></para>
    /// Its coordinates may be any byte at all, including 0x1B, so they must not go through escape
    /// processing. That is what the parser's raw-byte collection is for.
    /// </remarks>
    /// <param name="parser">
    /// The parser, positioned at the end of the escape sequence.
    /// </param>
    /// <returns>
    /// True when the sequence was a VT52 command and has been acted on.
    /// </returns>
    protected bool TryHandleVt52Escape(EscapeSequenceParser parser)
    {
        // Only the two-character forms are VT52 commands. Anything with an intermediate is not
        // something a VT52 ever received.
        if (parser.Intermediates.Length != 0) return false;

        switch ((char)parser.FinalByte)
        {
            case 'A': MoveCursorUpWithinMargins(1); return true;
            case 'B': MoveCursorDownWithinMargins(1); return true;
            case 'C': Cursor.MoveForward(1); return true;
            case 'D': Cursor.MoveBackward(1); return true;
            case 'F': Vt52GraphicsCharacterSet = true; return true;
            case 'G': Vt52GraphicsCharacterSet = false; return true;
            case 'H': Cursor.Home(); return true;

            case 'I':
                // Reverse line feed: up a line, and at the top the screen scrolls DOWN. Later
                // terminals kept this as RI (ESC M); the VT52 spells it I.
                HandleReverseLineFeed();
                return true;

            case 'J': HandleEraseInDisplay(0); return true;
            case 'K': HandleEraseInLine(0); return true;

            case 'Y':
                parser.ExpectRawBytes(2, ApplyVt52CursorAddress);
                return true;

            case 'Z':
                // Identify. A VT52 answers ESC / Z, which is nothing like a DA reply - there was no
                // DA yet.
                SendResponse("\x1b/Z"u8);
                return true;

            case '=': ApplicationKeypad = true; return true;
            case '>': ApplicationKeypad = false; return true;

            case '<':
                // Leave VT52 mode. On a terminal that IS a VT52 there is nothing to leave, and the
                // sequence is swallowed rather than printed; on a VT100 in VT52 mode this is the
                // way back to ANSI.
                Vt52Mode = false;
                return true;
        }

        return false;
    }

    /// <summary>
    /// Applies the two coordinate bytes of the VT52 <c>ESC Y</c>.
    /// </summary>
    /// <remarks>
    /// Each byte is its coordinate plus 0x20, counting from 1 - so <c>ESC Y SP SP</c> is the top
    /// left corner. Out-of-range values are clamped rather than refused: a VT52 had nowhere to
    /// report an error to, and it simply put the cursor at the edge.
    /// </remarks>
    /// <param name="bytes">
    /// Exactly two bytes: row then column.
    /// </param>
    private void ApplyVt52CursorAddress(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 2) return;

        int row = bytes[0] - 0x20;
        int column = bytes[1] - 0x20;

        if (row < 0) row = 0;
        if (column < 0) column = 0;
        if (row > Height - 1) row = Height - 1;
        if (column > Width - 1) column = Width - 1;

        Cursor.Row = row;
        Cursor.Column = column;
    }

    /// <summary>
    /// The strings a host has loaded into the function keys with DECUDK.
    /// </summary>
    public Input.UserDefinedKeys UserKeys { get; } = new Input.UserDefinedKeys();

    /// <summary>
    /// The character set a host has drawn and downloaded with DECDLD.
    /// </summary>
    public Fonts.SoftFont SoftFont { get; } = new Fonts.SoftFont();

    /// <summary>
    /// The name the Sixel plane is registered under.
    /// </summary>
    public const string SixelPlaneId = "sixel";

    /// <summary>
    /// The pixel space this terminal's graphics plane covers.
    /// </summary>
    /// <remarks>
    /// <para><b>Why one fixed size for every screen</b></para>
    /// ReGIS addresses a fixed 800 by 480 whatever the text size is - that is the VT340's graphics
    /// space, and a host draws into it without knowing how many columns the screen has. Sixel has
    /// no such fixed space, so it uses the same plane and places images by cell.
    ///
    /// The renderer stretches the whole plane over the text area, exactly as it already does for
    /// the Tektronix and ND planes. Graphics land in the right place RELATIVE to the text whatever
    /// font is in use; what that cannot promise is that one graphics pixel is one screen pixel.
    /// </remarks>
    public const int GraphicsPlaneWidth = RegisDecoder.DefaultWidth;

    /// <summary>
    /// See <see cref="GraphicsPlaneWidth"/>.
    /// </summary>
    public const int GraphicsPlaneHeight = RegisDecoder.DefaultHeight;

    /// <summary>
    /// How many plane pixels one character cell is wide.
    /// </summary>
    /// <remarks>
    /// Derived from the plane and the screen rather than fixed, so a Sixel image lands at the
    /// cursor on any screen size. On the 80 by 24 this comes to exactly the VT340's 10 by 20 cell.
    /// </remarks>
    public int GraphicsCellWidth => Math.Max(1, GraphicsPlaneWidth / Math.Max(1, Width));

    /// <summary>
    /// See <see cref="GraphicsCellWidth"/>.
    /// </summary>
    public int GraphicsCellHeight => Math.Max(1, GraphicsPlaneHeight / Math.Max(1, Height));

    /// <summary>
    /// Longest Sixel payload accepted, in characters.
    /// </summary>
    /// <remarks>
    /// A full-screen image at the nominal cell size is 800 by 480 pixels, which is 80 bands of 800
    /// strips: 64,000 characters before any colour changes or repeats. This allows four times that
    /// and still refuses to grow without end.
    /// </remarks>
    private const int MaximumSixelPayload = 262144;

    private bool _collectingSixel;
    private readonly StringBuilder _sixelPayload = new StringBuilder();
    private Graphics.SixelDecoder? _sixelDecoder;

    /// <summary>
    /// The name the ReGIS plane is registered under.
    /// </summary>
    /// <remarks>
    /// A plane of its own rather than sharing the Sixel one, because the two are erased separately:
    /// ReGIS has <c>S(E)</c>, which must not take a Sixel image with it, and clearing after an
    /// image must not wipe a drawing.
    /// </remarks>
    public const string RegisPlaneId = "regis";

    /// <summary>
    /// The name the ReGIS graphics input cursor's plane is registered under.
    /// </summary>
    /// <remarks>
    /// The crosshair moves with every arrow keypress, so it cannot be painted into the drawing - it
    /// would leave a trail, and erasing it would take the picture underneath with it. A plane of its
    /// own is cleared and redrawn instead, and the compositor puts it over the top.
    /// </remarks>
    public const string RegisInputCursorPlaneId = "regis-input-cursor";

    /// <summary>
    /// Longest ReGIS payload accepted, in characters.
    /// </summary>
    private const int MaximumRegisPayload = 262144;

    private bool _collectingRegis;
    private readonly StringBuilder _regisPayload = new StringBuilder();
    private Graphics.RegisDecoder? _regisDecoder;

    /// <summary>
    /// The terminal's ONE colour map, handed to both graphics decoders.
    /// </summary>
    /// <remarks>
    /// A VT340 has sixteen output map locations and both ReGIS and Sixel write to the same sixteen.
    /// A host is entitled to set the palette with ReGIS <c>S(M...)</c> and then send a Sixel image
    /// that defines no colours of its own, and hackerb9's <c>cat-original.six</c> does exactly that.
    /// With a map per decoder that image drew in the power-on colours and every colour was wrong.
    ///
    /// Built eagerly rather than on first use: it is sixteen colours in a fixed array, and making it
    /// lazy would mean deciding which decoder gets to create it.
    /// </remarks>
    protected Graphics.GraphicsColorMap GraphicsColours { get; } = new Graphics.GraphicsColorMap();

    /// <summary>
    /// DECSDM, private mode 80: true while sixel scrolling is switched OFF.
    /// </summary>
    /// <remarks>
    /// <para><b>What the two settings do</b></para>
    /// From the Graphics Programming manual, chapter 14:
    ///  - Scrolling ENABLED, which is the default: "the sixel active position begins at the
    ///    upper-left corner of the ANSI text active position" - the cursor - and "when sixel mode is
    ///    exited, the text cursor is set to the current sixel cursor position".
    ///  - Scrolling DISABLED: "the sixel active position begins at the upper-left corner of the
    ///    active graphics page. The terminal ignores any commands that attempt to advance the active
    ///    position below the bottom margin... the text cursor does not change from the position it
    ///    was in when sixel mode was entered."
    ///
    /// <para><b>Two documents disagree about which way round the mode is, and the manual loses</b></para>
    /// The manual says "When sixel display mode is set, the Sixel Scrolling feature is enabled."
    /// xterm's ctlseqs says the opposite: mode 80 RESET "Turns on Sixel Scrolling".
    ///
    /// hackerb9's errata for this exact manual settles it against real hardware: "DECSDM reversed
    /// regarding sixel scrolling. On hackerb9's vt340: when DECSDM is set, sixel scrolling is
    /// disabled; when DECSDM is reset, sixel scrolling is enabled." A machine beats a sentence, two
    /// independent sources agree against the manual, and the manual carries several other confirmed
    /// errata. So SET means scrolling off, which is what this property is named for.
    ///
    /// <para><b>What it is for</b></para>
    /// Layered images. With scrolling off every DCS draws from the same page origin, so a host can
    /// build one picture out of many sixel strings - which is exactly what the corpus fixture
    /// <c>comment.six</c> does with fourteen of them. With scrolling on, each string landed lower
    /// than the last and the table marched off the bottom of the plane.
    /// </remarks>
    protected bool SixelScrollingDisabled { get; private set; }

    private bool _collectingSettingRequest;
    private readonly StringBuilder _settingRequest = new StringBuilder();

    /// <summary>
    /// Longest DECRQSS setting name accepted.
    /// </summary>
    /// <remarks>
    /// A setting name is a handful of characters. Anything longer is not one, and collecting it
    /// would mean growing a buffer for a host that is not asking a question.
    /// </remarks>
    private const int MaximumSettingRequest = 16;

    /// <summary>
    /// DECRQSS: answers "what is your current setting for X?".
    /// </summary>
    /// <remarks>
    /// <para><b>The companion to DECRQM</b></para>
    /// DECRQM answers whether a MODE is on. This answers what a SETTING is - the scrolling region,
    /// the margins, the current attributes - by replying with the very sequence a host would send
    /// to put the terminal back in that state. That is the elegant part of the design: the answer
    /// is executable.
    ///
    /// <para><b>Saying "I do not know that one" is a real answer</b></para>
    /// The reply carries 1 for a setting this terminal reports and 0 for one it does not, and 0 is
    /// not a failure - it tells a host to stop asking and use its own defaults. Answering 1 with a
    /// made-up value would be worse than useless, so anything not listed here gets the honest 0.
    /// </remarks>
    /// <param name="request">
    /// The setting name, which is the final part of the sequence being asked about.
    /// </param>
    protected virtual void AnswerSettingRequest(string request)
    {
        string? value = DescribeSetting(request);

        // DCS 1 $ r <value> ST for a setting this terminal knows, DCS 0 $ r ST for one it does not.
        var reply = value != null
            ? "\x1bP1$r" + value + "\x1b\\"
            : "\x1bP0$r\x1b\\";

        SendResponse(Encoding.ASCII.GetBytes(reply));
    }

    /// <summary>
    /// The current value of one setting, written as the sequence that would set it.
    /// </summary>
    /// <param name="request">
    /// The setting name.
    /// </param>
    /// <returns>
    /// The value, or null when this terminal does not report that setting.
    /// </returns>
    protected virtual string? DescribeSetting(string request)
    {
        switch (request)
        {
            case "r":   // DECSTBM - the scrolling region
                return (ScrollTop + 1).ToString(System.Globalization.CultureInfo.InvariantCulture)
                    + ";"
                    + (ScrollBottom + 1).ToString(System.Globalization.CultureInfo.InvariantCulture)
                    + "r";

            case "s":   // DECSLRM - the left and right margins
                // Only meaningful with the mode on. With it off the same sequence means SCOSC, and
                // reporting margins for a terminal that has none would invite a host to use them.
                if (!LeftRightMarginMode) return null;
                return (LeftMargin + 1).ToString(System.Globalization.CultureInfo.InvariantCulture)
                    + ";"
                    + (RightMargin + 1).ToString(System.Globalization.CultureInfo.InvariantCulture)
                    + "s";

            case "m":   // SGR - the current attributes
                return DescribeCurrentAttributes() + "m";

            case "\"q": // DECSCA - whether what is typed next is protected
                return (ProtectedMode ? "1" : "0") + "\"q";

            case "*x":  // DECSACE - the extent the attribute commands apply to
                if (!Profile.Supports(TerminalFeatures.RectangleOperations)) return null;
                return (AttributeChangeIsRectangular ? "2" : "0") + "*x";

            case "\"p": // DECSCL - the operating level
                if (!Profile.Supports(TerminalFeatures.ConformanceLevels)) return null;

                // Level 1 takes no second parameter: a VT100 has no 8-bit controls to choose
                // between, so "CSI 61 " p" is the whole of it.
                if (ConformanceLevel <= 1)
                {
                    return ConformanceLevelAsRequested.ToString(CultureInfo.InvariantCulture) + "\"p";
                }

                return ConformanceLevelAsRequested.ToString(CultureInfo.InvariantCulture)
                    + (Send8BitControls ? ";0" : ";1") + "\"p";

            case "$}": // DECSASD - whether output is going to the status line
                if (!Profile.Supports(TerminalFeatures.StatusLine)) return null;
                return (WritingToStatusLine ? "1" : "0") + "$}";

            case "$~": // DECSSDT - which kind of status line is on
                if (!Profile.Supports(TerminalFeatures.StatusLine)) return null;
                return ((int)StatusLineType).ToString(CultureInfo.InvariantCulture) + "$~";

            // Table 12-4 of the VT420 manual lists fourteen settings. The seven above are the ones
            // this terminal can answer. The rest are left unanswered ON PURPOSE, because
            // DECRQSS's whole design is that the answer is the sequence that would put the setting
            // back - so reporting a setting this terminal would then ignore, or a number it does
            // not really hold, is worse than saying nothing:
            //  - DECSCPP ($ |), columns per page: the width is real, but the SETTER is not built,
            //    so a host could not send the answer back.
            //  - DECSLPP (t), lines per page: what is stored here is a PAGE COUNT, not a page
            //    length. The manual's mapping between the two (24 lines to 6 pages, 144 to 1) only
            //    holds on a terminal with the VT420's 144 lines of memory, and that geometry is
            //    deliberately not built - see the page memory notes above.
            //  - DECSNLS (* |), lines per screen: not built either, and recorded there too.
            //  - DECELF (+ q), DECLFKC (* }) and DECSMKR (+ r): local function keys and modifier
            //    key reporting, none of which this terminal has.

            default:
                return null;
        }
    }

    /// <summary>
    /// The current character attributes as an SGR parameter list.
    /// </summary>
    /// <remarks>
    /// Deliberately only the attributes DEC's own SGR report carries. Colour is left out: this is
    /// the answer to "what rendition am I in", and a host that wants the colours asks for them
    /// another way.
    /// </remarks>
    /// <returns>
    /// The parameters, always starting with 0 so the answer is a complete instruction rather than
    /// a difference from whatever came before.
    /// </returns>
    private string DescribeCurrentAttributes()
    {
        var text = new StringBuilder("0");

        if (CurrentAttributes.HasAttribute(CharacterAttributes.Bold)) text.Append(";1");
        if (CurrentAttributes.HasAttribute(CharacterAttributes.Underline)) text.Append(";4");
        if (CurrentAttributes.HasAttribute(CharacterAttributes.Blink)) text.Append(";5");
        if (CurrentAttributes.HasAttribute(CharacterAttributes.Reverse)) text.Append(";7");

        return text.ToString();
    }

    /// <summary>
    /// The ReGIS decoder, once a host has sent a drawing.
    /// </summary>
    /// <remarks>
    /// Exposed so the counter it keeps can be read: pointed at a real host, its unhandled-command
    /// tally says which ReGIS commands actually matter.
    /// </remarks>
    public Graphics.RegisDecoder? Regis => _regisDecoder;

    /// <summary>
    /// Draws one ReGIS command string onto the graphics plane.
    /// </summary>
    /// <remarks>
    /// The decoder keeps its own drawing point and colour BETWEEN sequences, which is what a real
    /// terminal does: a host is entitled to position in one DCS and draw in the next.
    /// </remarks>
    /// <param name="commands">
    /// The characters between the DCS introducer and its terminator.
    /// </param>
    private void DrawRegis(string commands)
    {
        if (commands.Length == 0) return;

        var compositor = Graphics;
        if (compositor == null)
        {
            compositor = new Graphics.GraphicsCompositor(GraphicsPlaneWidth, GraphicsPlaneHeight);
            Graphics = compositor;
        }

        var plane = compositor.FindPlane(RegisPlaneId) ?? compositor.AddPlane(RegisPlaneId);
        plane.IsVisible = true;

        // On a VT340 characters and drawings share one four-plane bitmap, so a write recolours
        // the text it passes over. Here they are separate, so the plane is told how big a
        // character cell is and reports those writes back. See TerminalCell.RegisColorIndex for
        // what is being reproduced and what its limit is.
        plane.Surface.TextCellWidth = GraphicsCellWidth;
        plane.Surface.TextCellHeight = GraphicsCellHeight;
        plane.Surface.OnMaskedWrite = RecolourTextCell;

        _regisDecoder ??= new Graphics.RegisDecoder(GraphicsColours);
        _regisDecoder.Decode(commands, plane.Surface);

        // How much of each character a shape covered is not known until the shape is finished,
        // and the last one has nothing after it to finish it. So the drawing is closed off here.
        plane.Surface.FlushTextRecolours();

        // MOVING OUTPUT MAP LOCATION 0 MOVES THE WHOLE PAGE. Every pixel nothing has drawn holds
        // code 0, so what location 0 looks like is what the background looks like - and on the
        // hardware that costs nothing, because the map is read as the screen is scanned.
        //
        // Turned into the terminal's default background rather than painted into the surface. The
        // surface deliberately leaves untouched pixels untouched, and the corpus proved why: a
        // plane-masked write over fresh screen must recolour text without laying an opaque bar over
        // it. This is the same route a Sixel image's sixteenth colour already takes.
        //
        // Found by looking at compare-cat-original.png on 21 August 2026 - ours black where the
        // hardware is dark teal-green. cat-original.six is the only fixture in the corpus that
        // writes the map through ReGIS, so nothing else can be moved by this.
        if (_regisDecoder.BackgroundMapLocationChanged
            && Profile.Supports(Profiles.TerminalFeatures.ImageColoursSetTextColours))
        {
            _regisDecoder.BackgroundMapLocationChanged = false;
            ImageTextBackground = GraphicsColours.Register(0);
        }

        // The report command answers the host instead of drawing. The decoder collects what it owes
        // rather than raising an event, because it owns no connection - so the reply is sent from
        // here, where one is to hand.
        SendRegisReports();

        // R(I0) or R(I1) may have just turned the graphics input cursor on, or a report may have
        // turned it off. Drawn here rather than by the decoder because the crosshair lives on a
        // plane of its own and the decoder is only handed one surface.
        RedrawRegisInputCursor();

        // One-shot mode suspends the host. Every byte after this sequence has to be held rather than
        // parsed, so the parser is stopped here and ProcessData buffers the rest of the chunk - the
        // same mechanism the printer controller uses for the same reason.
        if (_regisDecoder.IsHostDataSuspended)
        {
            Parser.StopRequested = true;
            _parserStoppedEarly = true;
        }

        compositor.Composite();
        OnInvalidated();
    }

    /// <summary>
    /// Sends whatever the ReGIS decoder owes the host.
    /// </summary>
    /// <remarks>
    /// "All information returned by the VT300 ends with a carriage return (CR)", so the decoder's
    /// reports already carry their own terminators and several can go out together.
    /// </remarks>
    private void SendRegisReports()
    {
        if (_regisDecoder == null || !_regisDecoder.HasReports) return;

        string reports = _regisDecoder.TakeReports();
        int count = Encoding.ASCII.GetByteCount(reports);
        byte[] bytes = new byte[count];
        Encoding.ASCII.GetBytes(reports, 0, reports.Length, bytes, 0);
        SendResponse(bytes);
    }

    /// <summary>
    /// Redraws the ReGIS graphics input cursor, or clears it when no input mode is running.
    /// </summary>
    /// <remarks>
    /// <para><b>The crosshair, from chapter 1</b></para>
    /// "This cursor is a horizontal and a vertical line. The horizontal line is the width of the
    /// screen, and the vertical line is the height of the screen. The two lines intersect at the
    /// active position... The crosshair is the default input cursor."
    ///
    /// All four styles are drawn - the crosshair, the diamond, the rubber band line and the rubber
    /// band rectangle - selected by <c>S(C(I...))</c>. The comment here said none of them was read
    /// yet, which stopped being true when the switch below was written and was corrected on
    /// 27 August 2026 while chasing something else. A stale comment is worse than none: it was read
    /// as evidence during a live investigation and had to be checked against the code underneath it.
    /// </remarks>
    private void RedrawRegisInputCursor()
    {
        // Raised BEFORE the early return below, because a terminal that never drew anything has no
        // compositor - and a host is entitled to send R(I0) before it draws. The mode still changed,
        // and the status line still has to say so.
        var mode = RegisInputMode;
        if (mode != _lastRaisedRegisInputMode)
        {
            _lastRaisedRegisInputMode = mode;
            RegisInputModeChanged?.Invoke(mode);
        }

        var compositor = Graphics;
        if (compositor == null) return;

        var plane = compositor.FindPlane(RegisInputCursorPlaneId);

        if (_regisDecoder == null || _regisDecoder.InputMode == RetroTerm.Core.Terminal.Graphics.RegisGraphicsInputMode.Off)
        {
            // Nothing to show. The plane is kept once built - rebuilding it on the next R(I0) would
            // cost an allocation to save nothing - but it is emptied and hidden.
            if (plane != null)
            {
                plane.Clear();
                plane.IsVisible = false;
                compositor.Composite();
            }

            return;
        }

        plane ??= compositor.AddPlane(RegisInputCursorPlaneId);
        plane.IsVisible = true;
        plane.Clear();

        var surface = plane.Surface;
        int x = _regisDecoder.InputCursorX;
        int y = _regisDecoder.InputCursorY;

        // Not taken from the colour map at all. The cursor is the TERMINAL talking to the operator,
        // not part of the host's picture, so it must stay visible whatever the host left selected -
        // and register 7, the obvious candidate, is 53% grey on the VT340's default map, which
        // rendered as a crosshair you could barely see. Looking at the picture is what caught that;
        // every assertion about it passed either way.
        var colour = RegisInputCursorColour;

        switch (_regisDecoder.InputCursorStyle)
        {
            case RetroTerm.Core.Terminal.Graphics.RegisCursorStyle.Diamond:
                DrawRegisDiamond(surface, x, y, colour);
                break;

            case RetroTerm.Core.Terminal.Graphics.RegisCursorStyle.RubberBandLine:
                // "with its origin fixed at the current drawing (output) position and its endpoint
                // at the current cursor position."
                surface.DrawLine(_regisDecoder.CurrentX, _regisDecoder.CurrentY, x, y, colour);
                break;

            case RetroTerm.Core.Terminal.Graphics.RegisCursorStyle.RubberBandRectangle:
                DrawRegisRubberBandRectangle(surface, x, y, colour);
                break;

            default:
                // "The horizontal line is the width of the screen, and the vertical line is the
                // height of the screen. The two lines intersect at the active position."
                surface.DrawLine(0, y, surface.Width - 1, y, colour);
                surface.DrawLine(x, 0, x, surface.Height - 1, colour);
                break;
        }

        compositor.Composite();
    }

    /// <summary>
    /// Draws the diamond input cursor.
    /// </summary>
    /// <param name="surface">
    /// The cursor's own plane.
    /// </param>
    /// <param name="x">
    /// Where the cursor is.
    /// </param>
    /// <param name="y">
    /// See <paramref name="x"/>.
    /// </param>
    /// <param name="colour">
    /// What to draw it in.
    /// </param>
    /// <remarks>
    /// "This cursor is a 21 x 21 pixel diamond." Twenty-one is odd on purpose: it gives the shape a
    /// single centre pixel, so the diamond can sit exactly ON the point it is reporting rather than
    /// half a pixel off it. Ten each way from the centre is that 21.
    /// </remarks>
    private static void DrawRegisDiamond(Graphics.IGraphicsSurface surface, int x, int y,
        Graphics.GraphicsColor colour)
    {
        const int Reach = RegisDiamondSize / 2;

        surface.DrawLine(x, y - Reach, x + Reach, y, colour);
        surface.DrawLine(x + Reach, y, x, y + Reach, colour);
        surface.DrawLine(x, y + Reach, x - Reach, y, colour);
        surface.DrawLine(x - Reach, y, x, y - Reach, colour);
    }

    /// <summary>
    /// Draws the rubber band rectangle input cursor.
    /// </summary>
    /// <param name="surface">
    /// The cursor's own plane.
    /// </param>
    /// <param name="x">
    /// Where the cursor is - the moving corner.
    /// </param>
    /// <param name="y">
    /// See <paramref name="x"/>.
    /// </param>
    /// <param name="colour">
    /// What to draw it in.
    /// </param>
    /// <remarks>
    /// "This cursor is a rectangle, with one corner fixed at the current drawing (output) position
    /// and the opposite corner at the current cursor position." Drawn as four lines rather than a
    /// rectangle fill, because the two corners can be in either order on either axis and a rectangle
    /// with a negative width draws nothing.
    /// </remarks>
    private void DrawRegisRubberBandRectangle(Graphics.IGraphicsSurface surface, int x, int y,
        Graphics.GraphicsColor colour)
    {
        int anchorX = _regisDecoder!.CurrentX;
        int anchorY = _regisDecoder.CurrentY;

        surface.DrawLine(anchorX, anchorY, x, anchorY, colour);
        surface.DrawLine(x, anchorY, x, y, colour);
        surface.DrawLine(x, y, anchorX, y, colour);
        surface.DrawLine(anchorX, y, anchorX, anchorY, colour);
    }

    /// <summary>
    /// How wide and tall the diamond input cursor is, in pixels.
    /// </summary>
    /// <remarks>
    /// "This cursor is a 21 x 21 pixel diamond" - chapter 1, and the only measurement the manual
    /// gives for any cursor shape.
    /// </remarks>
    public const int RegisDiamondSize = 21;

    /// <summary>
    /// The colour the graphics input crosshair is drawn in.
    /// </summary>
    /// <remarks>
    /// The manual never says. It describes the crosshair's SHAPE - "a horizontal and a vertical
    /// line", full width and full height - and leaves the colour alone. So the only requirement that
    /// can be met without a VT340 in front of us is that the operator can see it over whatever the
    /// host drew, and full white is the plainest way to meet it.
    /// </remarks>
    private static readonly Graphics.GraphicsColor RegisInputCursorColour
        = new Graphics.GraphicsColor(255, 255, 255);

    /// <summary>
    /// The four-plane value an untouched character carries: <c>0111</c>, or <c>1111</c> when it is
    /// bold.
    /// </summary>
    /// <remarks>
    /// Both numbers are hackerb9's, measured on a VT340 and written down in
    /// <c>faketextcolor.md</c>: "the value of any pixel of text is 0111", and "bold text pixels
    /// are set to 1111". They are the starting point every plane operation works from.
    /// </remarks>
    /// <param name="cell">
    /// The cell whose natural value is wanted.
    /// </param>
    /// <returns>
    /// 7 for ordinary text, 15 for bold.
    /// </returns>
    private static int NaturalTextCode(in TerminalCell cell)
        => (cell.Attributes & CharacterAttributes.Bold) != 0 ? 15 : 7;

    /// <summary>
    /// The graphics colour map, so a renderer can turn a cell's
    /// <see cref="TerminalCell.RegisColorIndex"/> into a colour.
    /// </summary>
    /// <remarks>
    /// The same map the decoders draw through, deliberately - a character recoloured by a plane
    /// write and a drawing made by that same write have to come out the same colour, and they only
    /// do if there is one map. A host that then changes a register with <c>S(M)</c> moves both.
    /// </remarks>
    public Graphics.GraphicsColorMap GraphicsColorMap => GraphicsColours;

    /// <summary>
    /// Marks every cell a picture covers as being UNDER it, so the renderer stops lifting their
    /// text out on top.
    /// </summary>
    /// <param name="pixelX">
    /// Left edge of the painted area, in plane pixels.
    /// </param>
    /// <param name="pixelY">
    /// Top edge of the painted area, in plane pixels.
    /// </param>
    /// <param name="pixelWidth">
    /// Width of the painted area, in plane pixels.
    /// </param>
    /// <param name="pixelHeight">
    /// Height of the painted area, in plane pixels.
    /// </param>
    /// <remarks>
    /// Rounded OUTWARDS to whole cells on purpose. A cell is the smallest thing that can be lifted
    /// over the plane here, so a picture covering any part of one has to own the whole of it -
    /// otherwise a character sitting half under an image would be drawn complete, on top, over
    /// pixels that should have buried it.
    /// </remarks>
    private void ClearTextOverGraphics(int pixelX, int pixelY, int pixelWidth, int pixelHeight)
    {
        if (pixelWidth <= 0 || pixelHeight <= 0) return;
        if (GraphicsCellWidth <= 0 || GraphicsCellHeight <= 0) return;

        int firstColumn = pixelX / GraphicsCellWidth;
        int firstRow = pixelY / GraphicsCellHeight;
        int lastColumn = (pixelX + pixelWidth - 1) / GraphicsCellWidth;
        int lastRow = (pixelY + pixelHeight - 1) / GraphicsCellHeight;

        if (firstColumn < 0) firstColumn = 0;
        if (firstRow < 0) firstRow = 0;
        if (lastColumn >= Buffer.Width) lastColumn = Buffer.Width - 1;
        if (lastRow >= Buffer.Height) lastRow = Buffer.Height - 1;

        for (int row = firstRow; row <= lastRow; row++)
        {
            for (int column = firstColumn; column <= lastColumn; column++)
            {
                Buffer[row, column].TextIsNewerThanGraphics = false;
            }
        }
    }

    /// <summary>
    /// Applies one plane write to the characters it passed over.
    /// </summary>
    /// <remarks>
    /// The operations are chapter 3's, and the same three the bitmap itself uses: complement
    /// exclusive-ors the selected planes, erase clears them, and everything else replaces them
    /// with the drawing index. What differs is only where the value is kept.
    /// A blank cell is left alone. Recolouring a space changes nothing anyone can see, and marking
    /// every space a drawing crossed would make the whole rectangle differ from the render cache
    /// for no reason.
    /// </remarks>
    /// <param name="column">
    /// Text column the write fell in.
    /// </param>
    /// <param name="row">
    /// Text row the write fell in.
    /// </param>
    /// <param name="planeMask">
    /// Which of the four planes take part.
    /// </param>
    /// <param name="drawIndex">
    /// The code being written, before masking.
    /// </param>
    /// <param name="mode">
    /// Overlay, replace, complement or erase.
    /// </param>
    private void RecolourTextCell(int column, int row, int planeMask, int drawIndex,
        Graphics.GraphicsWritingMode mode)
    {
        if (row < 0 || row >= Buffer.Height || column < 0 || column >= Buffer.Width) return;

        ref TerminalCell cell = ref Buffer[row, column];

        // Whatever else happens below, the plane has now been written where this cell sits, so
        // the cell is underneath it. Done before the blank-cell test on purpose: a blank cell
        // still has to lose the flag, or a space typed over a picture would punch a hole in it.
        cell.TextIsNewerThanGraphics = false;

        if (cell.Codepoint == 0 || cell.Codepoint == ' ') return;

        int mask = planeMask & 15;
        int old = cell.HasRegisColor ? cell.RegisColorIndex : NaturalTextCode(in cell);
        int code;

        switch (mode)
        {
            case RetroTerm.Core.Terminal.Graphics.GraphicsWritingMode.Complement:
                code = old ^ mask;
                break;

            case RetroTerm.Core.Terminal.Graphics.GraphicsWritingMode.Erase:
                code = old & ~mask;
                break;

            default:
                code = (old & ~mask) | (drawIndex & mask);
                break;
        }

        cell.RegisColorIndex = code & 15;
    }

    /// <summary>
    /// Decodes one Sixel image and paints it where the cursor is.
    /// </summary>
    /// <remarks>
    /// <para><b>The plane is built on first use</b></para>
    /// A terminal that never sees a Sixel image never allocates one. That matters: the plane is
    /// 800 by 480 pixels for an ordinary screen, and every session paying for that so a few can
    /// show pictures would be the wrong trade.
    ///
    /// <para><b>The cursor moves below the image</b></para>
    /// Which is what a VT340 does, and what makes a sequence of images stack down the screen
    /// instead of landing on top of each other. Text after an image continues underneath it.
    /// </remarks>
    /// <param name="payload">
    /// The characters between the DCS introducer and its terminator.
    /// </param>
    private void DrawSixelImage(string payload)
    {
        if (payload.Length == 0) return;

        var compositor = Graphics;
        if (compositor == null)
        {
            compositor = new Graphics.GraphicsCompositor(GraphicsPlaneWidth, GraphicsPlaneHeight);
            Graphics = compositor;
        }

        var plane = compositor.FindPlane(SixelPlaneId) ?? compositor.AddPlane(SixelPlaneId);
        plane.IsVisible = true;

        _sixelDecoder ??= new Graphics.SixelDecoder(GraphicsColours);

        // Before the payload: the aspect the DCS asked for. Raster attributes inside the payload
        // still override it, so this only decides the images that never stated a size.
        _sixelDecoder.SetAspectFromDcs(_sixelAspectParameter);

        // Decoded into a scratch surface first, then blitted at the cursor. Painting straight onto
        // the plane would need the decoder to know where the image goes, and keeping that out of it
        // is what lets it be tested with no terminal at all.
        var scratch = new Graphics.InMemoryGraphicsSurface(
            Math.Max(1, plane.Width), Math.Max(1, plane.Height));
        _sixelDecoder.Decode(payload, scratch);

        // THE VT340'S PECULIAR ORDERING SCHEME. On that model a Sixel image's colour definitions
        // also set the TEXT colours, by the order they were defined in rather than by the register
        // each was given: the sixth defined is the foreground and the sixteenth the background. The
        // decoder counts and reports; only a profile that claims the behaviour acts on it, because
        // xterm and libsixel draw Sixel and do not do this. See TerminalFeatures for the evidence.
        if (Profile.Supports(Profiles.TerminalFeatures.ImageColoursSetTextColours))
        {
            if (_sixelDecoder.ImageTextForeground.HasValue)
            {
                ImageTextForeground = _sixelDecoder.ImageTextForeground;
            }

            if (_sixelDecoder.ImageTextBackground.HasValue)
            {
                ImageTextBackground = _sixelDecoder.ImageTextBackground;
            }
        }

        // A SIXEL IMAGE PUTS EVERY LINE BACK TO SINGLE WIDTH on a VT340. Both of hackerb9's decdwl
        // captures say so, and the second exists only to show what it causes: an image indented by
        // a double-width line suddenly reverts to single-width indentation part way down.
        //
        // The same captures say the VT340 also CLEARS THE UNDERLYING TEXT BUFFER, and that half is
        // deliberately left alone. On the hardware it costs nothing to look at, because text and
        // graphics share one bitmap and the characters remain on screen as pixels - "retained only
        // in the bitmap buffer and cannot be edited". Here text is a cell buffer, so clearing it
        // would make those characters VANISH, which is further from the photograph than leaving
        // them. Same architectural split as M6.4h.
        if (Profile.Supports(Profiles.TerminalFeatures.ImageResetsLineAttributes))
        {
            ResetAllLineAttributes();
        }

        // WHERE THE IMAGE GOES depends on DECSDM. With sixel scrolling on - the default - it starts
        // at the text cursor. With scrolling off it starts at the upper-left corner of the graphics
        // page, which is what lets a host build one picture out of many DCS strings.
        //
        // Read AFTER the line attributes are reset, on purpose: on the hardware the reset happens as
        // the image is received, so the indentation the image lands at is the single-width one. That
        // IS quirk 2.
        int originX = SixelScrollingDisabled ? 0 : Cursor.Column * GraphicsCellWidth;
        int originY = SixelScrollingDisabled ? 0 : Cursor.Row * GraphicsCellHeight;

        // P2, THE BACKGROUND COLOUR OPTION. "P2 selects how the terminal draws the background
        // color" - VT330/VT340 Graphics Programming manual, chapter 14, held in spec\DEC:
        //   0 or 2 (default)  pixel positions specified as 0 are set to the current background colour
        //   1                 pixel positions specified as 0 remain at their current colour
        //
        // The AREA erased is the one the raster attributes named: "The VT300 uses Ph and Pv to
        // erase the background when P2 is set to 0 or 2", and "Ph and Pv let you omit background
        // sixel data from the image definition and still have a color background". An image with no
        // raster attributes never says how big that area is, so nothing is erased for one.
        //
        // WHEN THE BACKGROUND IS THE TERMINAL DEFAULT, NOTHING IS PAINTED, and that is not a
        // shortcut. Leaving those pixels transparent shows the terminal's own background, which IS
        // the current background colour - painting it would only hard-code today's theme into the
        // picture. The erase matters when a host has SET a background with SGR first, and then the
        // image's untouched area has to take that colour rather than whatever is behind it.
        //
        // Read but not acted on before 28 August 2026: the parameter was not parsed at all.
        // THE LINE FEEDS COME BEFORE THE PICTURE IS PUT DOWN, and the order matters for exactly
        // one kind of image: one taller than the screen. Such an image scrolls the terminal while
        // it is being received, so what a person is left looking at is its BOTTOM end. Painting
        // first and scrolling after cannot show that, because the plane is only as tall as the
        // screen and everything past its bottom edge was clipped away before the scroll could
        // move it - which is how vaxrgl-lntest.six ended up showing nothing at all.
        //
        // So: feed the lines, measure how far the planes actually moved, and then paint the image
        // that many rows higher. For every image that does not reach the bottom of the screen
        // nothing scrolls, the shift is zero, and this behaves exactly as it did before.
        int scrollBefore = _graphicsScrollPixels;

        // WHERE THE TEXT CURSOR ENDS UP, and only when sixel scrolling is on. With it off the
        // manual is explicit: "the text cursor does not change from the position it was in when
        // sixel mode was entered". Moving it anyway is what made every image after the first land
        // lower than the one before.
        //
        // WITH scrolling on the manual is equally explicit, and says something DIFFERENT from what
        // this used to do: "when sixel mode is exited, the text cursor is set to the current sixel
        // cursor position". The SIXEL cursor - not the bottom of the pixels.
        //
        // This counted pixel rows instead, ceil(Height / cell), which gives the same answer whenever
        // every band ends with a "-" and one screen row is one pixel row. That covers nearly every
        // image ever sent, which is why the difference hid for so long.
        //
        // extremeratio.six is the fixture that tells them apart. It has NO "-" at all and paints
        // 480 screen rows out of a single band by aspect ratio, so its height is 480 while its sixel
        // cursor never moved. That file's own closing comment reads "A VT340 leaves the text cursor
        // at the top of the screen"; counting the height drove it 24 rows to the bottom instead.
        //
        // A consequence worth knowing: an image that does NOT end with a "-" now leaves the cursor
        // ON its last band rather than below it, so the next text printed overlaps the picture.
        // That is what the hardware does, and it is what the manual describes.
        if (!SixelScrollingDisabled)
        {
            // DEC's rule, or the one every other Sixel terminal follows. See
            // TerminalFeatures.SixelExitCursorFollowsTheSixelCursor for why this is a profile
            // feature rather than simply the rule: the two agree on nearly every image, and part
            // company only on one that does not end with a graphics new line.
            int rows = Profile.Supports(Profiles.TerminalFeatures.SixelExitCursorFollowsTheSixelCursor)
                ? _sixelDecoder.SixelCursorTop / GraphicsCellHeight
                : (_sixelDecoder.Height + GraphicsCellHeight - 1) / GraphicsCellHeight;
            for (int i = 0; i < rows; i++)
            {
                HandleLineFeed();
            }
            Parser.StopRequested = true;
            _parserStoppedEarly = true;
        }

        // How far the planes travelled while those line feeds ran. Zero for nearly every image.
        int scrolled = _graphicsScrollPixels - scrollBefore;

        // Where the top of the image sits on the plane now that the plane has moved under it.
        int placedY = originY - scrolled;

        // Which row of the image the scratch surface's row zero holds, as a shift. Zero normally;
        // negative when the image had to be moved up to survive the scroll, in which case the
        // scratch was painted with that shift already applied.
        int paintOffset = 0;

        if (scrolled > 0)
        {
            // Paint it again, shifted, so the rows still on screen are the rows that reach the
            // surface. The first pass is not wasted - it is what measured the height and the sixel
            // cursor that decided how far to scroll in the first place.
            paintOffset = placedY;
            scratch.Clear(RetroTerm.Core.Terminal.Graphics.GraphicsColor.Transparent);
            _sixelDecoder.PaintOffsetY = paintOffset;
            _sixelDecoder.Decode(payload, scratch);
            _sixelDecoder.PaintOffsetY = 0;
            placedY = 0;
        }

        // P2, THE BACKGROUND COLOUR OPTION. "P2 selects how the terminal draws the background
        // color" - VT330/VT340 Graphics Programming manual, chapter 14, held in spec\DEC:
        //   0 or 2 (default)  pixel positions specified as 0 are set to the current background colour
        //   1                 pixel positions specified as 0 remain at their current colour
        //
        // The AREA erased is the one the raster attributes named: "The VT300 uses Ph and Pv to
        // erase the background when P2 is set to 0 or 2", and "Ph and Pv let you omit background
        // sixel data from the image definition and still have a color background". An image with no
        // raster attributes never says how big that area is, so nothing is erased for one.
        //
        // WHEN THE BACKGROUND IS THE TERMINAL DEFAULT, NOTHING IS PAINTED, and that is not a
        // shortcut. Leaving those pixels transparent shows the terminal's own background, which IS
        // the current background colour - painting it would only hard-code today's theme into the
        // picture. The erase matters when a host has SET a background with SGR first, and then the
        // image's untouched area has to take that colour rather than whatever is behind it.
        //
        // Read but not acted on before 28 August 2026: the parameter was not parsed at all.
        if (_sixelBackgroundOption != 1 && !CurrentBackground.IsDefault
            && _sixelDecoder.DeclaredWidth > 0 && _sixelDecoder.DeclaredHeight > 0)
        {
            var (br, bg, bb) = CurrentBackground.ToRgb();
            var background = new Graphics.GraphicsColor(br, bg, bb);

            // Straight onto the plane in plane coordinates, so the shift applies here too.
            int backgroundTop = originY - scrolled;

            for (int y = 0; y < _sixelDecoder.DeclaredHeight; y++)
            {
                for (int x = 0; x < _sixelDecoder.DeclaredWidth; x++)
                {
                    plane.Surface.SetPixel(originX + x, backgroundTop + y, background);
                }
            }
        }

        // Walk the SCRATCH, not the image: once the image has been shifted the two no longer line
        // up, and the scratch is the thing that actually holds pixels.
        int scratchRows = _sixelDecoder.Height + paintOffset;
        if (scratchRows > scratch.Height) scratchRows = scratch.Height;

        for (int y = 0; y < scratchRows; y++)
        {
            for (int x = 0; x < _sixelDecoder.Width; x++)
            {
                var pixel = scratch.GetPixel(x, y);
                if (pixel.IsTransparent) continue;

                plane.Surface.SetPixel(originX + x, placedY + y, pixel);
            }
        }

        // The picture now covers these cells, so any text already in them is UNDERNEATH it -
        // on a VT340 those pixels have simply been overwritten. Text printed after this point
        // sets the flag again and comes back out on top.
        ClearTextOverGraphics(originX, placedY, _sixelDecoder.Width, scratchRows);

        compositor.Composite();
        OnInvalidated();
    }

    /// <summary>
    /// The pixel aspect ratio parameter of the Sixel DCS now being collected.
    /// </summary>
    /// <remarks>
    /// Kept from the introducer because the payload arrives afterwards, and the decoder needs it
    /// before the first band is painted. Zero when the image omitted it, which is what the
    /// specification says an omitted parameter means.
    /// </remarks>
    private int _sixelAspectParameter;

    /// <summary>
    /// The background colour option, P2, of the Sixel DCS now being collected.
    /// </summary>
    /// <remarks>
    /// "P2 selects how the terminal draws the background color", VT330/VT340 Graphics
    /// Programming manual, chapter 14. 0 or 2, the default, means "pixel positions specified as 0
    /// are set to the current background color"; 1 means they "remain at their current color".
    /// </remarks>
    private int _sixelBackgroundOption;

    /// <summary>
    /// Whether a DECDLD payload is being collected right now.
    /// </summary>
    private bool _collectingSoftFont;

    /// <summary>
    /// The payload of the DECDLD in flight.
    /// </summary>
    private readonly StringBuilder _softFontPayload = new StringBuilder();

    /// <summary>
    /// Where in the set the download starts.
    /// </summary>
    /// <remarks>
    /// Can be negative: a 94-character set asked to start at Pcn 0 is asking for position 2/0,
    /// which that set does not have, and the manual says the terminal ignores the attempt. The
    /// glyph is dropped and the one after it lands in the first real position.
    /// </remarks>
    private int _softFontStartCharacter;

    /// <summary>
    /// Whether the download in flight declared itself a 96-character set (Pcss = 1).
    /// </summary>
    private bool _softFontIs96Characters;

    /// <summary>
    /// How many positions the set in flight has: 94 unless Pcss said otherwise.
    /// </summary>
    private int SoftFontPositionCount => _softFontIs96Characters ? 96 : 94;

    /// <summary>
    /// Longest DECDLD payload accepted, in characters.
    /// </summary>
    /// <remarks>
    /// Enough for a whole 96-character set at the largest matrix this terminal keeps, several
    /// times over, and still finite.
    /// </remarks>
    private const int MaximumSoftFontPayload = 32768;

    /// <summary>
    /// Starts collecting a DECDLD download and applies the parameters that come before the glyphs.
    /// </summary>
    /// <remarks>
    /// <para><b>The parameters, in the order DEC puts them</b></para>
    ///  - Pfn, the font number. One downloaded set is kept here, so this is read and not acted on.
    ///  - Pcn, the starting character. 0 and 1 both mean the first character of the set.
    ///  - Pe, erase control: 0 erases the whole set before loading, 1 erases only the characters
    ///    this download redefines, 2 erases all renditions. 0 is the default, and destructive -
    ///    the same shape as DECUDK, where the short form is the one that clears.
    ///  - Pcmw and Pcmh, the character matrix in pixels.
    ///  - Pw, Pt and Pcss describe the screen width, text-versus-full-cell, and 94 or 96
    ///    characters. They are read but not acted on: this terminal keeps the shapes at the size
    ///    they arrive and has one screen width.
    /// </remarks>
    /// <param name="parser">
    /// The parser, positioned at the end of the DECDLD introducer.
    /// </param>
    private void BeginSoftFont(EscapeSequenceParser parser)
    {
        _softFontPayload.Clear();
        _collectingSoftFont = true;

        int startCharacter = parser.GetParam(1, 0);
        int erase = parser.GetParam(2, 0);
        int matrixWidth = parser.GetParam(3, 0);
        int matrixHeight = parser.GetParam(6, 0);

        // PCSS CHANGES WHAT PCN MEANS, and this was being ignored. The VT330/VT340 manual, after
        // the parameter table: "The value of Pcss changes the meaning of the Pcn (starting
        // character) parameter above", and then gives both readings outright.
        //
        //   Pcss 0, a 94-character set and the default: Pcn 1 is position 2/1 and Pcn 94 is 7/14.
        //   "The terminal ignores any attempt to load characters into the 2/0 or 7/15 table
        //   positions."
        //
        //   Pcss 1, a 96-character set: Pcn 0 is position 2/0 and Pcn 95 is 7/15.
        //
        // Both are the same arithmetic on the code table - position 2/0 plus Pcn - but the store
        // here counts from the FIRST CHARACTER OF THE SET, and a 94-character set starts one place
        // further along. So the 94-set subtracts one and the 96-set does not. Subtracting one from
        // both, which is what happened before, put every character of a 96-character set one
        // position to the left.
        _softFontIs96Characters = parser.GetParam(7, 0) == 1;
        _softFontStartCharacter = _softFontIs96Characters ? startCharacter : startCharacter - 1;

        if (erase == 0)
        {
            SoftFont.Clear();
        }

        // A matrix of 0 means "the terminal's default". There is no ROM cell size to fall back on
        // here, so the VT220's own 8 by 10 text cell is used and said so.
        SoftFont.Describe(SoftFont.Designation,
            matrixWidth > 0 ? matrixWidth : DefaultSoftFontWidth,
            matrixHeight > 0 ? matrixHeight : DefaultSoftFontHeight);
    }

    /// <summary>
    /// The character matrix a DECDLD download gets when it does not name one.
    /// </summary>
    private const int DefaultSoftFontWidth = 8;

    /// <summary>
    /// See <see cref="DefaultSoftFontWidth"/>.
    /// </summary>
    private const int DefaultSoftFontHeight = 10;

    /// <summary>
    /// Reads a DECDLD payload: a designation string, then the glyphs.
    /// </summary>
    /// <remarks>
    /// <para><b>How a glyph is drawn on the wire</b></para>
    /// Each printable character from '?' to '~' carries SIX PIXELS in a vertical strip: subtract
    /// 0x3F and the low bit is the top pixel. Strips run left to right across the character. A '/'
    /// ends a band of six rows and starts the next one below it, and a ';' ends the character and
    /// starts the next.
    ///
    /// That is the same idea as a sixel image, which is why a tall character is built out of bands
    /// rather than rows: the format was designed for hardware that shifted six pixels at a time.
    ///
    /// <para><b>The designation string</b></para>
    /// Everything before the first strip character is the name the host will later use to select
    /// this set. It is stored, not acted on - selecting a downloaded set is a separate sequence
    /// this terminal does not yet implement.
    /// </remarks>
    /// <param name="payload">
    /// The text between the DECDLD introducer and its terminator.
    /// </param>
    private void ApplySoftFont(string payload)
    {
        if (payload.Length == 0) return;

        // The designation is a character-set designator, not "everything up to the first glyph".
        // Its shape is fixed: zero or more intermediates in 0x20-0x2F, then exactly one final byte
        // in 0x30-0x7E. Reading it as "up to the first strip character" looks right until the
        // designator's own final byte is one - " @" ends in '@', which is a perfectly good strip -
        // and then the first column of every glyph is whatever that byte happened to mean.
        int start = 0;
        while (start < payload.Length && payload[start] >= ' ' && payload[start] <= '/')
        {
            start++;
        }

        if (start < payload.Length && payload[start] >= '0' && payload[start] <= '~')
        {
            start++;
        }

        SoftFont.Describe(payload.Substring(0, start), SoftFont.MatrixWidth, SoftFont.MatrixHeight);

        int character = _softFontStartCharacter;
        int from = start;
        while (from <= payload.Length)
        {
            int to = payload.IndexOf(';', from);
            if (to < 0) to = payload.Length;

            // A position the set does not have is skipped, and the download carries on into the
            // next one - "the terminal ignores any attempt to load characters into the 2/0 or 7/15
            // table positions". Ignoring the CHARACTER, not the rest of the string.
            if (to > from && character >= 0 && character < SoftFontPositionCount)
            {
                var rows = DecodeSoftFontGlyph(payload, from, to);
                if (rows != null)
                {
                    SoftFont.Define(character, rows);
                }
            }

            character++;
            from = to + 1;
        }
    }

    /// <summary>
    /// Turns one character's worth of DECDLD strips into pixel rows.
    /// </summary>
    /// <param name="payload">
    /// The whole payload.
    /// </param>
    /// <param name="start">
    /// First character of this glyph.
    /// </param>
    /// <param name="end">
    /// One past the last character of this glyph.
    /// </param>
    /// <returns>
    /// One value per pixel row, bit 15 leftmost, or null when nothing was drawable.
    /// </returns>
    private ushort[]? DecodeSoftFontGlyph(string payload, int start, int end)
    {
        int height = SoftFont.MatrixHeight > 0 ? SoftFont.MatrixHeight : DefaultSoftFontHeight;
        int width = SoftFont.MatrixWidth > 0 ? SoftFont.MatrixWidth : DefaultSoftFontWidth;

        var rows = new ushort[height];
        int column = 0;
        int bandTop = 0;
        bool anything = false;

        for (int i = start; i < end; i++)
        {
            char c = payload[i];

            if (c == '/')
            {
                // Next band of six rows, back at the left edge.
                bandTop += 6;
                column = 0;
                continue;
            }

            if (c < '?' || c > '~') continue;   // not a strip character; skip rather than guess

            int bits = c - '?';
            if (column < width)
            {
                for (int bit = 0; bit < 6; bit++)
                {
                    if ((bits & (1 << bit)) == 0) continue;

                    int row = bandTop + bit;
                    if (row >= height) continue;

                    // Bit 15 is the leftmost pixel, which is how FontBase stores a ROM glyph.
                    rows[row] |= (ushort)(0x8000 >> column);
                    anything = true;
                }
            }

            column++;
        }

        return anything ? rows : null;
    }

    /// <summary>
    /// The DEC key code a Windows virtual key corresponds to, for DECUDK.
    /// </summary>
    /// <remarks>
    /// <para><b>The gaps are real</b></para>
    /// The DEC numbers are not consecutive - F10 is 21 and F11 is 23, F14 is 26 and F15 is 28 -
    /// because 22, 27 and 30 belong to keys the VT220 keyboard did not have as user-definable.
    /// Filling the gaps in would put a host's definition on the wrong key.
    ///
    /// <para><b>F1 to F5 are absent, and that is correct</b></para>
    /// On a real VT220 those are Hold, Print, Set-Up, Data/Talk and Break - local functions the
    /// terminal itself acts on, never sent to the host and never definable.
    /// </remarks>
    /// <param name="virtualKey">
    /// The Windows virtual key code.
    /// </param>
    /// <returns>
    /// The DEC key code, or -1 when that key cannot be defined.
    /// </returns>
    private static int DecKeyCodeFor(int virtualKey)
    {
        switch (virtualKey)
        {
            case 117: return 17;   // F6
            case 118: return 18;   // F7
            case 119: return 19;   // F8
            case 120: return 20;   // F9
            case 121: return 21;   // F10
            case 122: return 23;   // F11
            case 123: return 24;   // F12
            case 124: return 25;   // F13
            case 125: return 26;   // F14
            case 126: return 28;   // F15, the Help key
            case 127: return 29;   // F16, the Do key
            case 128: return 31;   // F17
            case 129: return 32;   // F18
            case 130: return 33;   // F19
            case 131: return 34;   // F20
            default: return -1;
        }
    }

    /// <summary>
    /// The string a host loaded into this key, if it loaded one.
    /// </summary>
    /// <remarks>
    /// Asked BEFORE the keyboard mapper, so a defined key sends what the host asked for instead of
    /// its factory sequence - which is the whole point of DECUDK. Undefined keys fall through to
    /// the mapper unchanged, so a terminal with no definitions behaves exactly as it did.
    ///
    /// It lives here rather than in the canvas because more than one surface presses keys: the
    /// window, the virtual keyboard, and anything driving the terminal from outside. A lookup
    /// written at one of those would be missing from the others.
    /// </remarks>
    /// <param name="virtualKey">
    /// The Windows virtual key code.
    /// </param>
    /// <param name="value">
    /// The bytes the host wants this key to send.
    /// </param>
    /// <returns>
    /// True when the key has a definition.
    /// </returns>
    public bool TryGetUserDefinedKey(int virtualKey, out byte[] value)
    {
        value = System.Array.Empty<byte>();

        int decKey = DecKeyCodeFor(virtualKey);
        if (decKey < 0) return false;

        return UserKeys.TryGet(decKey, out value);
    }

    /// <summary>
    /// Starts collecting a DECUDK payload, and applies the two parameters that come before it.
    /// </summary>
    /// <remarks>
    /// Both parameters default to 0, and in both cases 0 is the DESTRUCTIVE reading - clear every
    /// key first, and lock them afterwards. That is DEC's choice, not this emulator's, and getting
    /// it backwards would mean a host that sent the shortest form got the opposite of what it asked
    /// for.
    /// </remarks>
    /// <param name="clearing">
    /// 0 to clear all keys before loading, 1 to load without clearing.
    /// </param>
    /// <param name="locking">
    /// 0 to lock the keys afterwards, 1 to leave them open.
    /// </param>
    private void BeginUserDefinedKeys(int clearing, int locking)
    {
        _userDefinedKeyPayload.Clear();

        // A locked terminal collects nothing. Refusing here rather than at the end means a host
        // that keeps sending definitions cannot make this buffer grow either.
        if (UserKeys.IsLocked)
        {
            _collectingUserDefinedKeys = false;
            return;
        }

        _collectingUserDefinedKeys = true;
        _userDefinedKeysWillLock = locking == 0;

        if (clearing == 0)
        {
            UserKeys.Clear();
        }
    }

    /// <summary>
    /// A chunk of DCS payload. Called zero or more times between
    /// <see cref="HandleDCSSequence"/> and <see cref="HandleDCSEnd"/>.
    /// </summary>
    /// <param name="data">
    /// Payload bytes; valid only for the duration of the call.
    /// </param>
    protected virtual void HandleDCSData(ReadOnlySpan<byte> data)
    {
        if (_collectingSixel)
        {
            for (int i = 0; i < data.Length; i++)
            {
                if (_sixelPayload.Length >= MaximumSixelPayload) return;
                _sixelPayload.Append((char)data[i]);
            }
            return;
        }

        if (_collectingRegis)
        {
            for (int i = 0; i < data.Length; i++)
            {
                if (_regisPayload.Length >= MaximumRegisPayload) return;
                _regisPayload.Append((char)data[i]);
            }
            return;
        }

        if (_collectingSettingRequest)
        {
            for (int i = 0; i < data.Length; i++)
            {
                // A setting name is a handful of characters. Anything longer is not one, and
                // collecting it would be growing a buffer for a host that is not asking a question.
                if (_settingRequest.Length >= MaximumSettingRequest) return;
                _settingRequest.Append((char)data[i]);
            }
            return;
        }

        if (_collectingSoftFont)
        {
            for (int i = 0; i < data.Length; i++)
            {
                if (_softFontPayload.Length >= MaximumSoftFontPayload) return;
                _softFontPayload.Append((char)data[i]);
            }
            return;
        }

        if (!_collectingUserDefinedKeys) return;

        for (int i = 0; i < data.Length; i++)
        {
            // Bounded on the way IN. A host is entitled to send a long DCS and this terminal is not
            // entitled to grow a buffer for it without end; past the ceiling the rest is dropped
            // and the definitions that did arrive still stand.
            if (_userDefinedKeyPayload.Length >= MaximumUserDefinedKeyPayload) return;

            _userDefinedKeyPayload.Append((char)data[i]);
        }
    }

    /// <summary>
    /// Longest DECUDK payload accepted, in characters.
    /// </summary>
    /// <remarks>
    /// Generous next to the store it feeds - the hex encoding takes two characters per byte, and
    /// the key numbers and separators take more - but finite, which is the point.
    /// </remarks>
    private const int MaximumUserDefinedKeyPayload = 16384;

    /// <summary>
    /// End of the current DCS, whether it was terminated properly by ST or abandoned by
    /// the host. Always follows a <see cref="HandleDCSSequence"/>.
    /// </summary>
    protected virtual void HandleDCSEnd()
    {
        if (_collectingSixel)
        {
            _collectingSixel = false;
            DrawSixelImage(_sixelPayload.ToString());
            _sixelPayload.Clear();
            return;
        }

        if (_collectingRegis)
        {
            _collectingRegis = false;
            DrawRegis(_regisPayload.ToString());
            _regisPayload.Clear();
            return;
        }

        if (_collectingSettingRequest)
        {
            _collectingSettingRequest = false;
            AnswerSettingRequest(_settingRequest.ToString());
            _settingRequest.Clear();
            return;
        }

        if (_collectingSoftFont)
        {
            _collectingSoftFont = false;
            ApplySoftFont(_softFontPayload.ToString());
            _softFontPayload.Clear();
            return;
        }

        if (!_collectingUserDefinedKeys) return;

        _collectingUserDefinedKeys = false;
        ApplyUserDefinedKeys(_userDefinedKeyPayload.ToString());
        _userDefinedKeyPayload.Clear();

        if (_userDefinedKeysWillLock)
        {
            UserKeys.Lock();
        }
    }

    /// <summary>
    /// Reads a DECUDK payload: <c>key/hex ; key/hex ; ...</c>.
    /// </summary>
    /// <remarks>
    /// <para><b>A bad definition is skipped, not fatal</b></para>
    /// An entry with no slash, a key number that is not a number, or hex that is malformed or of
    /// odd length is dropped and the rest of the payload is still read. A host that got one entry
    /// wrong should not lose the other nineteen, and this terminal should not fall over because
    /// something on the wire was mangled.
    ///
    /// <para><b>An empty string clears that key</b></para>
    /// <c>17/</c> means "F6 sends nothing", which is how a host takes a key back without clearing
    /// all of them.
    /// </remarks>
    /// <param name="payload">
    /// The text between the DECUDK introducer and its terminator.
    /// </param>
    private void ApplyUserDefinedKeys(string payload)
    {
        if (payload.Length == 0) return;

        int start = 0;
        while (start <= payload.Length)
        {
            int end = payload.IndexOf(';', start);
            if (end < 0) end = payload.Length;

            ApplyOneUserDefinedKey(payload, start, end);

            start = end + 1;
        }
    }

    /// <summary>
    /// Reads one <c>key/hex</c> entry out of a DECUDK payload.
    /// </summary>
    /// <param name="payload">
    /// The whole payload.
    /// </param>
    /// <param name="start">
    /// First character of this entry.
    /// </param>
    /// <param name="end">
    /// One past the last character of this entry.
    /// </param>
    private void ApplyOneUserDefinedKey(string payload, int start, int end)
    {
        int slash = payload.IndexOf('/', start);
        if (slash < 0 || slash >= end) return;

        if (!int.TryParse(payload.Substring(start, slash - start),
                System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out int keyCode))
        {
            return;
        }

        int hexStart = slash + 1;
        int hexLength = end - hexStart;

        // Odd length is not a sequence of bytes at all, so the entry is dropped rather than having
        // its last half-byte guessed at.
        if (hexLength < 0 || (hexLength & 1) != 0) return;

        var value = new byte[hexLength / 2];
        for (int i = 0; i < value.Length; i++)
        {
            int high = HexDigit(payload[hexStart + i * 2]);
            int low = HexDigit(payload[hexStart + i * 2 + 1]);
            if (high < 0 || low < 0) return;

            value[i] = (byte)((high << 4) | low);
        }

        UserKeys.Define(keyCode, value);
    }

    /// <summary>
    /// The value of one hexadecimal digit, or -1 when it is not one.
    /// </summary>
    /// <param name="c">
    /// The character to read.
    /// </param>
    /// <returns>
    /// 0 to 15, or -1.
    /// </returns>
    private static int HexDigit(char c)
    {
        if (c >= '0' && c <= '9') return c - '0';
        if (c >= 'A' && c <= 'F') return c - 'A' + 10;
        if (c >= 'a' && c <= 'f') return c - 'a' + 10;
        return -1;
    }

    /// <summary>
    /// Processes input data from the terminal
    /// </summary>
    public virtual void ProcessInput(ReadOnlySpan<byte> data)
    {
        Parser.ProcessBytes(data);
        OnInvalidated(); // Ensure UI is updated after processing
    }

    /// <summary>
    /// Gets the maximum scrollback buffer size
    /// </summary>
    public virtual int MaxScrollback => Buffer.MaxScrollbackLines;
}


/// <summary>
/// Line height for the DEC line-size sequences: DECDHL top (ESC # 3), DECDHL bottom
/// (ESC # 4) and DECSWL (ESC # 5).
///
/// Lived in the TDV namespace until 2026-08-09, described as "line height modes for TDV
/// terminals". It is a DEC concept every VT-family terminal shares; the TDV manuals follow
/// the VT220 spec for it.
/// </summary>
public enum LineHeight
{
    Single,
    DoubleTop,
    DoubleBottom
}

/// <summary>
/// Line width for DECDWL (ESC # 6) and DECSWL (ESC # 5). See <see cref="LineHeight"/> for
/// why this is not TDV-specific.
/// </summary>
public enum LineWidth
{
    Single,
    Double
}
