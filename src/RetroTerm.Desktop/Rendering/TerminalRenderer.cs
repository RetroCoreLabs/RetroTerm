using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Media.Immutable;
using Avalonia.Platform;
using RetroTerm.Core.Fonts;
using RetroTerm.Core.Search;
using RetroTerm.Core.Selection;
using RetroTerm.Core.Terminal;
using RetroTerm.Core.Terminal.Buffer;
using RetroTerm.Core.Terminal.Emulators;
using RetroTerm.Core.Terminal.Emulators.TDV;
using RetroTerm.Core.Terminal.Rendering;

namespace RetroTerm.Desktop.Rendering;

/// <summary>
/// High-performance terminal renderer using Avalonia DrawingContext.
/// Renders terminal buffer with per-character colors, attributes, and cursor.
/// </summary>
public class TerminalRenderer
{
    private readonly TerminalEmulatorBase _emulator;

    // Color palette (xterm 256-color)
    private ImmutableSolidColorBrush[] _palette = null!; // Initialized in constructor via InitializeColorPalette()
    private ImmutableSolidColorBrush _defaultForeground = null!; // Initialized in constructor via InitializeColorPalette()
    private ImmutableSolidColorBrush _defaultBackground = null!; // Initialized in constructor via InitializeColorPalette()

    /// <summary>
    /// Which way round the cached rows were drawn, so DECSCNM invalidates them.
    /// </summary>
    private bool _cachedReverseScreen;

    /// <summary>
    /// The theme's own defaults, kept apart because a Sixel image may override them per frame.
    /// </summary>
    private ImmutableSolidColorBrush? _themeForeground;
    private ImmutableSolidColorBrush? _themeBackground;

    private RetroTerm.Core.Terminal.Graphics.GraphicsColor? _cachedImageForeground;
    private RetroTerm.Core.Terminal.Graphics.GraphicsColor? _cachedImageBackground;

    // Cursor state
    private bool _cursorVisible = true;

    /// <summary>
    /// Whether the last drawn frame used a blinking cursor style. Set by Render, read by the blink
    /// timer so it only forces repaints when a blink is actually wanted.
    /// </summary>
    private bool _cursorIsBlinking;
    private readonly System.Timers.Timer _cursorBlinkTimer;

    /// <summary>
    /// Blink phase for TEXT — the SGR 5 / SGR 6 cell attribute, which is a different thing from
    /// the cursor blinking and must not share its phase. A cursor blinking in step with the text
    /// under it is how a real terminal never looked.
    ///
    /// One timer drives both: it ticks at 500 ms, the rapid phase flips on every tick and the slow
    /// phase on every second tick, giving 120 and 60 flashes per minute. ECMA-48 puts the
    /// boundary at 150 per minute — slow is "less than", rapid is "at least" — so the rapid rate
    /// here is at the slow end of what SGR 6 permits, chosen because a faster flash on a modern
    /// display is unpleasant rather than authentic.
    /// </summary>
    private bool _slowBlinkOn = true;

    private bool _rapidBlinkOn = true;
    private int _blinkTickCount;

    /// <summary>
    /// Whether the last drawn frame actually contained a blinking cell. Same role as
    /// <see cref="_cursorIsBlinking"/>: the phases flip regardless, but a repaint is only worth
    /// asking for when something on screen would change.
    /// </summary>
    private bool _frameHasBlinkingText;

    // Highlight brushes. Constant colours, so they are built ONCE for the process rather
    // than inside the per-cell loop — they used to be `new ImmutableSolidColorBrush(...)`
    // on the hot path, which allocated a brush per highlighted cell per frame.
    private static readonly ImmutableSolidColorBrush SelectionBrush =
        new ImmutableSolidColorBrush(Color.FromRgb(68, 68, 136));   // blue
    private static readonly ImmutableSolidColorBrush CurrentMatchBrush =
        new ImmutableSolidColorBrush(Color.FromRgb(255, 255, 0));   // bright yellow
    private static readonly ImmutableSolidColorBrush SearchMatchBrush =
        new ImmutableSolidColorBrush(Color.FromRgb(200, 200, 0));   // darker yellow

    // Selection support
    private SelectionManager? _selectionManager;

    // Search match highlighting
    private List<SearchMatch>? _searchMatches;
    private int _currentMatchIndex = -1;

    // Font renderer (provided by emulator - NO TERMINAL TYPE CHECKS!)
    // Each terminal emulator provides its own font rendering strategy via IFontRenderer
    private readonly IFontRenderer _fontRenderer;
    private readonly double _charWidth;
    private readonly double _charHeight;

    // Cached font variant to avoid syncing on every render
    private int _lastSyncedVariant = -1;

    public TerminalRenderer(TerminalEmulatorBase emulator)
    {
        _emulator = emulator ?? throw new ArgumentNullException(nameof(emulator));

        // Get font renderer from emulator (each terminal provides its own strategy via extension methods)
        // NO TERMINAL TYPE CHECKS - polymorphism handles it!
        _fontRenderer = emulator.CreateFontRenderer();
        _charWidth = _fontRenderer.GetCharWidth();
        _charHeight = _fontRenderer.GetCharHeight();

        // Initialize color palette
        InitializeColorPalette();

        // Setup cursor blink timer (500ms).
        //
        // The timer used to flip the flag and stop there, with nothing asking for a repaint — so a
        // blinking cursor only ever changed state when something ELSE happened to redraw the
        // screen. On an idle session that is never, which is exactly when a user looks at the
        // cursor. BlinkStateChanged is how the owner learns it needs to repaint.
        _cursorBlinkTimer = new System.Timers.Timer(500);
        _cursorBlinkTimer.Elapsed += OnCursorBlinkElapsed;
        _cursorBlinkTimer.Start();
    }

    /// <summary>
    /// Raised when the cursor blink flag flips, so the owner can ask for a repaint.
    ///
    /// Raised on the TIMER thread, not the UI thread — a subscriber that touches UI state must
    /// marshal. TerminalCanvas does.
    /// </summary>
    public event Action? BlinkStateChanged;

    /// <summary>
    /// Whether this cell falls inside any search match.
    ///
    /// A linear scan of the match list, deliberately: a search produces a handful of
    /// ranges, and scanning them is cheaper than the HashSet of every matched CELL this
    /// replaced — which was rebuilt on every frame whether or not the matches changed.
    /// </summary>
    private bool IsInAnySearchMatch(int row, int col)
    {
        var matches = _searchMatches;
        if (matches == null)
        {
            return false;
        }
        for (int i = 0; i < matches.Count; i++)
        {
            var match = matches[i];
            if (match.Row == row && col >= match.StartCol && col <= match.EndCol)
            {
                return true;
            }
        }
        return false;
    }

    private void OnCursorBlinkElapsed(object? sender, System.Timers.ElapsedEventArgs e)
    {
        // Volatile: written here on the timer thread, read by Render on the UI thread. Without it
        // the read is free to be hoisted out of the render loop and the cursor stops blinking for
        // reasons that never show up in a debugger.
        System.Threading.Volatile.Write(ref _cursorVisible, !System.Threading.Volatile.Read(ref _cursorVisible));

        // Text blink runs on the same timer but its OWN phases, so the cursor and the text under
        // it never flash in lockstep. Rapid flips every tick (500 ms), slow every second tick.
        _blinkTickCount++;
        System.Threading.Volatile.Write(ref _rapidBlinkOn, !System.Threading.Volatile.Read(ref _rapidBlinkOn));
        if ((_blinkTickCount & 1) == 0)
        {
            System.Threading.Volatile.Write(ref _slowBlinkOn, !System.Threading.Volatile.Read(ref _slowBlinkOn));
        }

        // Only ask for a repaint when the screen is actually showing something that blinks. The
        // flags keep flipping either way, so switching TO a blinking style or attribute needs no
        // special handling — the repaint that draws it sets these, and the next tick starts
        // blinking. A screen with neither costs no repaints at all.
        if (System.Threading.Volatile.Read(ref _cursorIsBlinking)
            || System.Threading.Volatile.Read(ref _frameHasBlinkingText))
        {
            BlinkStateChanged?.Invoke();
        }
    }

    /// <summary>
    /// Whether a cell's character should be drawn this frame, given its blink attributes.
    /// A blinked-off cell keeps its background and loses only its glyph — that is what a
    /// terminal does, and it is why this is a draw-time question rather than a colour one.
    /// </summary>
    private bool IsBlinkedVisible(CharacterAttributes attributes)
    {
        if (attributes.HasAttribute(CharacterAttributes.RapidBlink))
        {
            return System.Threading.Volatile.Read(ref _rapidBlinkOn);
        }
        if (attributes.HasAttribute(CharacterAttributes.Blink))
        {
            return System.Threading.Volatile.Read(ref _slowBlinkOn);
        }
        return true;
    }

    public double GetCharWidth() => _charWidth;
    public double GetCharHeight() => _charHeight;

    private void InitializeColorPalette()
    {
        // Brushes built FROM Core's palette, not from a second copy of the numbers. The renderer
        // used to carry its own table; it agreed with Core on the 16 base colours and the greys and
        // disagreed on 214 of the 216 cube colours, so what was painted and what Core reported
        // could never be reconciled. See TerminalPalette.
        _palette = new ImmutableSolidColorBrush[256];
        for (int i = 0; i < 256; i++)
        {
            // The theme gets first refusal on each index. A colour theme declines every one and
            // the canonical value is used, so this is the same table as before themes existed.
            if (!_theme.TryGetBaseColour((byte)i, out var themed))
            {
                themed = TerminalPalette.GetRgb((byte)i);
            }
            _palette[i] = new ImmutableSolidColorBrush(Color.FromRgb(themed.R, themed.G, themed.B));
        }

        // Default colors come from the theme; its own default is the CRT phosphor green this
        // renderer has always started with (#00FF88 on #001911).
        var fg = _theme.DefaultForeground;
        var bg = _theme.DefaultBackground;
        _defaultForeground = new ImmutableSolidColorBrush(Color.FromRgb(fg.R, fg.G, fg.B));
        _defaultBackground = new ImmutableSolidColorBrush(Color.FromRgb(bg.R, bg.G, bg.B));
    }

    /// <summary>
    /// Lets a Sixel image's colours stand in for the theme's defaults, for this frame.
    /// </summary>
    /// <param name="frame">
    /// The frame about to be drawn.
    /// </param>
    /// <remarks>
    /// <para><b>Why the theme's own pair is kept separately</b></para>
    /// The default brushes are read from fifteen places in this file - the page fill, the cache
    /// band fill, reverse video, the cursor, the selection. Swapping the two fields for the frame
    /// keeps every one of those correct with no further change, but it would destroy the theme's
    /// values if they were not held somewhere else first. So they are.
    ///
    /// Null image colours put the theme back, which is what happens after a hard reset.
    /// </remarks>
    private void ApplyImageColours(ScreenFrame frame)
    {
        _themeForeground ??= _defaultForeground;
        _themeBackground ??= _defaultBackground;

        var foreground = frame.ImageForeground;
        _defaultForeground = foreground.HasValue
            ? new ImmutableSolidColorBrush(Color.FromRgb(foreground.Value.R, foreground.Value.G, foreground.Value.B))
            : _themeForeground;

        var background = frame.ImageBackground;
        _defaultBackground = background.HasValue
            ? new ImmutableSolidColorBrush(Color.FromRgb(background.Value.R, background.Value.G, background.Value.B))
            : _themeBackground;
    }

    /// <summary>
    /// Replaces the default foreground and background colors.
    /// Caller must trigger a visual invalidation after calling this.
    /// </summary>
    public void SetDefaultColors(Color fg, Color bg)
        => SetTheme(TerminalTheme.Colour("", (fg.R, fg.G, fg.B), (bg.R, bg.G, bg.B)));

    /// <summary>
    /// Applies a presentation theme: the default colours, and on a monochrome theme the sixteen
    /// base colours collapsed onto this screen's one phosphor.
    ///
    /// The canonical palette in Core is untouched — a screen read back over MCP still reports the
    /// colour the host asked for, not the shade a phosphor theme painted. See TerminalTheme.
    ///
    /// Caller must trigger a visual invalidation after calling this.
    /// </summary>
    public void SetTheme(TerminalTheme theme)
    {
        if (theme == null) throw new ArgumentNullException(nameof(theme));

        _theme = theme;

        var fg = theme.DefaultForeground;
        var bg = theme.DefaultBackground;
        _defaultForeground = new ImmutableSolidColorBrush(Color.FromRgb(fg.R, fg.G, fg.B));
        _defaultBackground = new ImmutableSolidColorBrush(Color.FromRgb(bg.R, bg.G, bg.B));

        // The new theme's pair becomes the one an image override falls back to. Without this a
        // theme changed while a Sixel image's colours were in force would be forgotten the moment
        // the image released them.
        _themeForeground = _defaultForeground;
        _themeBackground = _defaultBackground;

        InitializeColorPalette();

        // Every cached pixel was painted in the OLD colours, and the cell data has not changed, so
        // the row diff would find nothing dirty and the screen would keep the previous theme until
        // something else happened to touch it. Throw the cache away.
        _cacheValid = false;
    }

    private TerminalTheme _theme = TerminalTheme.Colour("Default", (0, 255, 136), (0, 25, 17));

    /// <summary>
    /// Gets the current default foreground color.
    /// </summary>
    public Color DefaultForegroundColor => ((ImmutableSolidColorBrush)_defaultForeground).Color;

    /// <summary>
    /// Gets the current default background color.
    /// </summary>
    public Color DefaultBackgroundColor => ((ImmutableSolidColorBrush)_defaultBackground).Color;

    /// <summary>
    /// Sets the selection manager for rendering selections
    /// </summary>
    public void SetSelectionManager(SelectionManager? selectionManager)
    {
        _selectionManager = selectionManager;
    }

    /// <summary>
    /// Sets search matches for highlighting
    /// </summary>
    public void SetSearchMatches(List<SearchMatch>? matches, int currentMatchIndex = -1)
    {
        _searchMatches = matches;
        _currentMatchIndex = currentMatchIndex;
    }

    /// <summary>
    /// How far DOWN to shift the whole screen this frame, for DECSCLM smooth scrolling.
    /// </summary>
    /// <remarks>
    /// <para><b>The whole feature, on this side</b></para>
    /// Zero for an ordinary frame, and then nothing below costs anything. While a scroll is sliding
    /// it counts down from one character cell to zero, and the screen is drawn that many pixels
    /// lower each frame - so the picture appears to climb into place. The buffer scrolled instantly;
    /// only the drawing of it lags.
    /// <para><b>Why a shift and not a re-render</b></para>
    /// Nothing about the content changes during the slide, so the row cache stays valid and the
    /// cached screen is simply blitted at an offset. That is one transform per frame, no reflowing
    /// and no re-rasterising of a single glyph, which is what makes this affordable at all.
    /// </remarks>
    public double SmoothScrollPixelOffset { get; set; }

    /// <summary>
    /// Renders the terminal buffer to the DrawingContext.
    /// </summary>
    /// <param name="context">
    /// Where to draw. The caller has already translated for letterboxing.
    /// </param>
    /// <param name="availableSize">
    /// The terminal's NATURAL size, in cell-grid pixels.
    /// </param>
    /// <param name="scrollOffset">
    /// Lines scrolled back; 0 for the live screen.
    /// </param>
    /// <param name="scale">
    /// How much the terminal is magnified to fit its window.
    ///
    /// TerminalCanvas used to push this transform itself and hand the renderer a context that was
    /// already scaled. It is passed in instead because the renderer is about to need the DEVICE
    /// pixel size it is really drawing at: a screen cached at natural size and then magnified on
    /// the way out would be a blurred copy of glyphs that are currently rasterised at their final
    /// size, and nobody would notice until they looked at the screen.
    ///
    /// Applying it here rather than at the call site is the same transform in the same order, so
    /// this step on its own changes no pixel - which is exactly what the headless UI suite is for.
    /// </param>
    public void Render(DrawingContext context, Size availableSize, int scrollOffset = 0, double scale = 1.0)
    {
        double smooth = SmoothScrollPixelOffset;

        if (smooth <= 0.0)
        {
            // The ordinary path, untouched. A terminal that never smooth-scrolls pays one comparison
            // per frame for the feature and nothing else.
            if (TryRenderFromCache(context, availableSize, scrollOffset, scale))
            {
                return;
            }

            using (context.PushTransform(Matrix.CreateScale(scale, scale)))
            {
                RenderAtNaturalSize(context, availableSize, scrollOffset);
            }

            return;
        }

        // CLIP FIRST. The screen is about to be drawn lower than it belongs and the row above the
        // top is about to be drawn higher, so without this both would paint over whatever the
        // control has around it.
        using (context.PushClip(new Rect(0, 0, availableSize.Width, availableSize.Height)))
        using (context.PushTransform(Matrix.CreateTranslation(0, smooth)))
        {
            if (!TryRenderFromCache(context, availableSize, scrollOffset, scale))
            {
                using (context.PushTransform(Matrix.CreateScale(scale, scale)))
                {
                    RenderAtNaturalSize(context, availableSize, scrollOffset);
                }
            }

            DrawRowAboveTop(context, scale);
        }
    }

    /// <summary>
    /// Draws the line that has just scrolled off, in the gap a sliding screen leaves at the top.
    /// </summary>
    /// <param name="context">
    /// Where to draw.
    /// </param>
    /// <param name="scale">
    /// The magnification the rest of the screen is drawn at.
    /// </param>
    /// <remarks>
    /// Without this the animation is a blank band climbing the screen rather than a line leaving it,
    /// which reads as a flicker rather than as scrolling. It is one row, drawn only while a slide is
    /// in progress, and only when the frame actually carries the row - the very first scroll on a
    /// fresh screen has nothing above it and honestly shows nothing.
    /// </remarks>
    private void DrawRowAboveTop(DrawingContext context, double scale)
    {
        var frame = _emulator.LatestFrame;
        if (frame == null || !frame.HasRowAboveTop) return;

        using (context.PushTransform(Matrix.CreateScale(scale, scale)))
        {
            DrawRow(context, frame, -1, frame.ScrollOffset > 0, hasSelection: false);
        }
    }

    // ─────────────────────────────────────────────────────────────
    // The row cache
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// The screen as last drawn, at DEVICE resolution. Null until the first frame.
    /// </summary>
    private RenderTargetBitmap? _cache;

    private int _cachePixelWidth;
    private int _cachePixelHeight;
    private double _cacheScale;

    /// <summary>The cells the cache currently shows, row-major. Compared against the new frame to
    /// find which rows actually changed.</summary>
    private TerminalCell[]? _cachedCells;

    private int _cachedWidth;
    private int _cachedHeight;

    /// <summary>
    /// Everything OTHER than cell content that decides how a row looks.
    /// </summary>
    private RowAppearance[]? _cachedRows;

    /// <summary>
    /// False until the cache holds a complete screen; forces the next draw to do all rows.
    /// </summary>
    private bool _cacheValid;

    /// <summary>
    /// The parts of a row's appearance that do not live in its cells.
    ///
    /// A row can look different with identical text: it may be selected, highlighted as a search
    /// hit, holding the cursor, or holding blinking characters that are currently in their off
    /// phase. Comparing only the cells would leave every one of those stale on screen, which is
    /// the whole failure mode a row cache invites.
    /// </summary>
    private struct RowAppearance
    {
        public int SelectionStart, SelectionEnd;
        public int CurrentMatchStart, CurrentMatchEnd;
        public bool HasOtherSearchMatch;
        public bool HasBlinkingText;
        public bool SlowBlinkOn, RapidBlinkOn;
        public bool HasCursor;
        public int CursorColumn;
        public CursorStyle CursorStyle;
        public bool CursorOn;

        public bool Matches(in RowAppearance other)
            => SelectionStart == other.SelectionStart
            && SelectionEnd == other.SelectionEnd
            && CurrentMatchStart == other.CurrentMatchStart
            && CurrentMatchEnd == other.CurrentMatchEnd
            && HasOtherSearchMatch == other.HasOtherSearchMatch
            && HasBlinkingText == other.HasBlinkingText
            && SlowBlinkOn == other.SlowBlinkOn
            && RapidBlinkOn == other.RapidBlinkOn
            && HasCursor == other.HasCursor
            && CursorColumn == other.CursorColumn
            && CursorStyle == other.CursorStyle
            && CursorOn == other.CursorOn;
    }

    /// <summary>
    /// Draws the screen from a cached bitmap, repainting only the rows whose appearance changed.
    ///
    /// A full 80x24 screen costs around forty times what blitting a cached one costs, and terminal
    /// frames overwhelmingly change one or two rows — a line of output, a keystroke, the cursor
    /// blinking. See tests\RetroTerm.Tests\Avalonia\RenderCostBenchmarkTests.cs for the numbers
    /// this is built on.
    ///
    /// The cache holds DEVICE pixels, not cell-grid pixels. Holding it at natural size and
    /// magnifying it on the way out would be a blurred copy of glyphs that are rasterised at their
    /// final size today, and that is a change only a human eye would catch.
    /// </summary>
    /// <returns>
    /// False when caching cannot be used this frame, and the caller should draw directly.
    /// </returns>
    private bool TryRenderFromCache(DrawingContext context, Size availableSize, int scrollOffset, double scale)
    {
        SyncFontVariant();

        var frame = _emulator.LatestFrame;
        if (frame == null || frame.Width <= 0 || frame.Height <= 0)
        {
            // Nothing published yet. The direct path asks for a frame and paints the background.
            return false;
        }

        int pixelWidth = (int)Math.Ceiling(availableSize.Width * scale);
        int pixelHeight = (int)Math.Ceiling(availableSize.Height * scale);
        if (pixelWidth <= 0 || pixelHeight <= 0)
        {
            return false;
        }

        // A new size, a new magnification or a new cell grid invalidates everything: the cached
        // pixels describe a screen that no longer exists.
        // DECSCNM belongs in this list. It changes every pixel on the screen while changing NO
        // cell - the manual is explicit that it "does not change the data in page memory" - so
        // RowCellsChanged sees nothing and every cached row would be reused inverted the wrong
        // way. Nothing else in the dirty check can notice it.
        // A VT340 lets a Sixel image set the TEXT colours, so the frame may carry a pair that
        // outranks the theme's. Resolved before anything is measured against the defaults, and it
        // is in the cache-invalidation list below for exactly the DECSCNM reason: it repaints every
        // pixel while changing no cell, so the row diff cannot notice it.
        ApplyImageColours(frame);

        if (_cache == null
            || _cachePixelWidth != pixelWidth
            || _cachePixelHeight != pixelHeight
            || _cacheScale != scale
            || _cachedWidth != frame.Width
            || _cachedHeight != frame.Height
            || _cachedReverseScreen != frame.ReverseScreen
            || _cachedImageForeground != frame.ImageForeground
            || _cachedImageBackground != frame.ImageBackground)
        {
            _cache?.Dispose();
            _cache = new RenderTargetBitmap(new PixelSize(pixelWidth, pixelHeight), new Vector(96, 96));
            _cachePixelWidth = pixelWidth;
            _cachePixelHeight = pixelHeight;
            _cacheScale = scale;
            _cachedWidth = frame.Width;
            _cachedHeight = frame.Height;
            _cachedCells = new TerminalCell[frame.Width * frame.Height];
            _cachedRows = new RowAppearance[frame.Height];
            _cachedReverseScreen = frame.ReverseScreen;
            _cachedImageForeground = frame.ImageForeground;
            _cachedImageBackground = frame.ImageBackground;
            _cacheValid = false;
        }

        bool isScrolledBack = frame.IsScrolledBack;
        bool hasSelection = _selectionManager?.HasSelection == true;

        // Work out what each row should look like now, and which rows differ from the cache.
        var dirty = new bool[frame.Height];
        bool anyDirty = !_cacheValid;
        bool sawBlinkingText = false;

        for (int row = 0; row < frame.Height; row++)
        {
            var appearance = DescribeRow(frame, row, isScrolledBack, hasSelection);
            if (appearance.HasBlinkingText)
            {
                sawBlinkingText = true;
            }

            if (!_cacheValid
                || !appearance.Matches(_cachedRows![row])
                || RowCellsChanged(frame, row))
            {
                dirty[row] = true;
                anyDirty = true;
            }

            _cachedRows![row] = appearance;
        }

        if (anyDirty)
        {
            // A row's drawing is not guaranteed to stay inside its own band - a descender or a
            // tall accent reaches across the boundary - so a row that CHANGED has stale spill
            // sitting in the bands above and below it, drawn from its previous content. Those
            // bands have to be repainted as well or the leftovers stay there for good.
            //
            // Found by DirtyRowCacheTests, which compares an incrementally drawn screen against a
            // fully drawn one: without this, six of its eight cases showed a few hundred stale
            // pixels each.
            ExpandDirtyToNeighbours(dirty);

            RepaintDirtyRows(frame, dirty, scale, isScrolledBack, hasSelection);
            StoreCells(frame);
            _cacheValid = true;
        }

        System.Threading.Volatile.Write(ref _frameHasBlinkingText, sawBlinkingText);

        // Blit at device resolution, one to one. The caller has translated for letterboxing and
        // has NOT scaled, so these coordinates are already device pixels.
        var whole = new Rect(0, 0, pixelWidth, pixelHeight);
        context.DrawImage(_cache!, whole, whole);

        DrawGraphicsPlanes(context, whole, frame);

        // The scrollback badge is an overlay, not part of the screen, so it stays outside the
        // cache - otherwise it would be baked into rows and have to be scrubbed off again.
        if (isScrolledBack)
        {
            using (context.PushTransform(Matrix.CreateScale(scale, scale)))
            {
                DrawScrollIndicator(context, availableSize, frame.ScrollOffset);
            }
        }

        return true;
    }

    /// <summary>
    /// Marks the row above and below each dirty row dirty too, so stale spill across a band
    /// boundary is repainted. Reads from a copy, or the expansion would cascade down the screen
    /// and mark everything dirty.
    /// </summary>
    private static void ExpandDirtyToNeighbours(bool[] dirty)
    {
        var original = new bool[dirty.Length];
        Array.Copy(dirty, original, dirty.Length);

        for (int row = 0; row < original.Length; row++)
        {
            if (!original[row]) continue;
            if (row > 0) dirty[row - 1] = true;
            if (row < dirty.Length - 1) dirty[row + 1] = true;
        }
    }

    /// <summary>
    /// Puts the terminal's graphics planes over the text that has just been drawn.
    ///
    /// Text first, graphics on top, which is the order the hardware worked in: a graphics plane
    /// overlays the character display, and the transparent pixels are how the text shows through.
    ///
    /// The composite is stretched to the terminal's whole area rather than drawn pixel for pixel.
    /// The ND graphics space is 1024 x 780 and has nothing to do with how many character cells are
    /// on screen, so the two only coincide by accident; scaling here is what lets the same drawing
    /// look right at any window size.
    ///
    /// Nothing happens at all when the emulator has no graphics, which is every text-only profile.
    /// </summary>
    private void DrawGraphicsPlanes(DrawingContext context, Rect destination,
        RetroTerm.Core.Terminal.Rendering.ScreenFrame frame)
    {
        var graphics = _emulator.Graphics;
        if (graphics == null) return;

        // Reading the published surface once: it is double buffered, so this reference stays valid
        // even if the pump composites again while the blit is in flight.
        var composite = graphics.Output;

        if (!graphics.HasAnythingToDraw())
        {
            // Nothing drawn, or every plane hidden. Dropping the bitmap when it is not needed keeps
            // an idle session from carrying a megabyte of transparent pixels through every frame.
            _graphicsBitmap?.Dispose();
            _graphicsBitmap = null;
            return;
        }

        var bitmap = EnsureGraphicsBitmap(composite.Width, composite.Height);
        WritePixels(bitmap, composite, frame);

        context.DrawImage(bitmap, new Rect(0, 0, composite.Width, composite.Height), destination);
    }

    /// <summary>
    /// The composite, as an Avalonia bitmap. Kept between frames rather than rebuilt.
    /// </summary>
    private WriteableBitmap? _graphicsBitmap;

    private WriteableBitmap EnsureGraphicsBitmap(int width, int height)
    {
        if (_graphicsBitmap != null
            && _graphicsBitmap.PixelSize.Width == width
            && _graphicsBitmap.PixelSize.Height == height)
        {
            return _graphicsBitmap;
        }

        _graphicsBitmap?.Dispose();
        _graphicsBitmap = new WriteableBitmap(
            new PixelSize(width, height),
            new Vector(96, 96),
            PixelFormat.Bgra8888,
            AlphaFormat.Unpremul);

        return _graphicsBitmap;
    }

    /// <summary>
    /// Copies the composite's pixels into the bitmap, swapping channel order on the way.
    ///
    /// GraphicsColor is ARGB because that is how a colour reads to a human; Avalonia's buffer here
    /// is BGRA. The swap has to happen somewhere, and doing it once on the way out is cheaper than
    /// making every protocol module think in BGRA.
    /// </summary>
    private void WritePixels(WriteableBitmap bitmap,
        RetroTerm.Core.Terminal.Graphics.InMemoryGraphicsSurface composite,
        RetroTerm.Core.Terminal.Rendering.ScreenFrame frame)
    {
        int width = composite.Width;
        int height = composite.Height;
        int needed = width * height;

        // One scratch buffer, kept between frames. Allocating a megabyte per repaint on the render
        // path is exactly what this project's rules forbid.
        if (_graphicsScratch == null || _graphicsScratch.Length < needed)
        {
            _graphicsScratch = new int[needed];
        }

        var source = composite.Pixels;
        var scratch = _graphicsScratch;

        // A single-phosphor screen has ONE GUN. It could not show a blue line whatever the host
        // asked for - it showed a dim one - so on a monochrome theme the whole composite collapses
        // to the phosphor, exactly as the sixteen text colours do. Collapsing the text and leaving
        // the drawings in colour would put a picture on screen the hardware could not produce.
        //
        // Asked once per frame, not per pixel: the flag cannot change inside a blit.
        bool monochrome = _theme.IsMonochrome;

        for (int i = 0; i < needed; i++)
        {
            if (monochrome)
            {
                uint value = source[i];
                uint alpha = value & 0xFF000000u;

                // A transparent pixel has no colour to collapse, and running it through the
                // phosphor would paint the background over the text underneath.
                if (alpha != 0
                    && _theme.TryGetPhosphorColour(
                        (byte)((value >> 16) & 0xFF), (byte)((value >> 8) & 0xFF), (byte)(value & 0xFF),
                        out var phosphor))
                {
                    scratch[i] = unchecked((int)(alpha
                        | ((uint)phosphor.R << 16) | ((uint)phosphor.G << 8) | phosphor.B));
                    continue;
                }
            }

            // STRAIGHT THROUGH, and the arithmetic is the whole point of the comment above.
            //
            // Bgra8888 describes the order of BYTES IN MEMORY: B, G, R, A. An int written to that
            // buffer little-endian puts its low byte first, so the value that lands as B,G,R,A is
            // (A << 24) | (R << 16) | (G << 8) | B - which is ARGB, exactly what GraphicsColor
            // already holds. No swap is needed, and the swap that used to be here crossed red and
            // blue in every graphic this terminal has ever drawn.
            //
            // It went unnoticed because the only thing drawing until now was the ND/Tektronix
            // plane, which uses ONE colour: (0,255,136) came out as (136,255,0), a yellow-green
            // instead of a spring green, and both look green. A three-colour Sixel image made it
            // obvious the moment anyone looked at the picture - red, green, blue arrived on screen
            // as blue, green, red.
            scratch[i] = unchecked((int)source[i]);
        }

        PunchOutCellsWhoseTextIsOnTop(scratch, width, height, frame);

        using var locked = bitmap.Lock();

        // Row by row rather than one copy: a locked bitmap's rows can be padded, and assuming they
        // are not is the kind of thing that works on one machine and skews the picture on another.
        int rowInts = width;
        for (int y = 0; y < height; y++)
        {
            var rowStart = locked.Address + y * locked.RowBytes;
            System.Runtime.InteropServices.Marshal.Copy(scratch, y * rowInts, rowStart, rowInts);
        }
    }

    /// <summary>
    /// Makes the plane transparent over every cell whose text was written after the picture, so
    /// the text underneath shows through instead of being buried.
    /// </summary>
    /// <param name="scratch">
    /// The composite pixels, ARGB, about to be copied into the bitmap.
    /// </param>
    /// <param name="width">
    /// The composite's width in pixels.
    /// </param>
    /// <param name="height">
    /// The composite's height in pixels.
    /// </param>
    /// <param name="frame">
    /// The published frame, which carries the cells and therefore the flags.
    /// </param>
    /// <remarks>
    /// <para><b>Why a hole in the plane rather than redrawing the text on top</b></para>
    /// The text is already in the cache that was blitted before this plane, correct in every
    /// respect. Punching the plane lets that finished drawing show through, so nothing has to
    /// reproduce the font, the attributes, the selection or the ReGIS recolouring a second time -
    /// and there is no second copy to drift out of step.
    ///
    /// <para><b>A printed SPACE is lifted too, and that is not an oversight</b></para>
    /// On the hardware a space is not nothing: it paints background pixels into the shared bitmap
    /// and rubs the picture out underneath itself. So the black bar behind a line of text runs
    /// unbroken through the gaps between its words, which is exactly what the hardware capture of
    /// <c>extremeratio</c> shows.
    /// <para>
    /// This is safe only because the flag is raised in ONE place, the print path, so a space can
    /// carry it only if a space was genuinely printed. A cell that was merely erased, scrolled
    /// into place or never touched holds codepoint 0, never a space, and keeps the flag clear -
    /// which is why a screen full of trailing blanks does not cut a hole in the picture.
    /// </para>
    /// </remarks>
    private void PunchOutCellsWhoseTextIsOnTop(int[] scratch, int width, int height,
        RetroTerm.Core.Terminal.Rendering.ScreenFrame frame)
    {
        int cellWidth = _emulator.GraphicsCellWidth;
        int cellHeight = _emulator.GraphicsCellHeight;
        if (cellWidth <= 0 || cellHeight <= 0) return;

        for (int row = 0; row < frame.Height; row++)
        {
            int top = row * cellHeight;
            if (top >= height) break;

            int bottom = top + cellHeight;
            if (bottom > height) bottom = height;

            for (int column = 0; column < frame.Width; column++)
            {
                var cell = frame[row, column];
                if (!cell.TextIsNewerThanGraphics) continue;

                // Codepoint 0 is a cell nothing was ever printed into. A printed space IS lifted -
                // see the remarks above.
                if (cell.Codepoint == 0) continue;

                int left = column * cellWidth;
                if (left >= width) break;

                int right = left + cellWidth;
                if (right > width) right = width;

                for (int y = top; y < bottom; y++)
                {
                    int rowStart = y * width;
                    for (int x = left; x < right; x++)
                    {
                        // Alpha to zero, colour left alone: the blit reads the alpha only.
                        scratch[rowStart + x] = 0;
                    }
                }
            }
        }
    }

    private int[]? _graphicsScratch;

    /// <summary>
    /// Collects everything about a row's appearance that its cells do not carry.
    /// </summary>
    private RowAppearance DescribeRow(ScreenFrame frame, int row, bool isScrolledBack, bool hasSelection)
    {
        var appearance = new RowAppearance
        {
            SelectionStart = 0,
            SelectionEnd = -1,
            CurrentMatchStart = 0,
            CurrentMatchEnd = -1,
            SlowBlinkOn = System.Threading.Volatile.Read(ref _slowBlinkOn),
            RapidBlinkOn = System.Threading.Volatile.Read(ref _rapidBlinkOn),
            CursorOn = System.Threading.Volatile.Read(ref _cursorVisible),
            CursorColumn = -1,
        };

        if (hasSelection && _selectionManager!.TryGetSelectedColumnRange(row - frame.ScrollOffset, out var s, out var e))
        {
            appearance.SelectionStart = s;
            appearance.SelectionEnd = e;
        }

        // Search coordinates, not window coordinates - see the same conversion in DrawRow. The
        // cache decides which rows to repaint from this, so getting it wrong here would leave a
        // highlight painted on a row that no longer holds the match.
        int matchRow = row - frame.ScrollOffset;

        if (_searchMatches != null && _currentMatchIndex >= 0 && _currentMatchIndex < _searchMatches.Count)
        {
            var currentMatch = _searchMatches[_currentMatchIndex];
            if (currentMatch.Row == matchRow)
            {
                appearance.CurrentMatchStart = currentMatch.StartCol;
                appearance.CurrentMatchEnd = currentMatch.EndCol;
            }
        }

        var matches = _searchMatches;
        if (matches != null)
        {
            for (int i = 0; i < matches.Count; i++)
            {
                if (matches[i].Row == matchRow)
                {
                    appearance.HasOtherSearchMatch = true;
                    break;
                }
            }
        }

        for (int col = 0; col < frame.Width; col++)
        {
            var attributes = frame[row, col].Attributes;
            if (attributes.HasAttribute(CharacterAttributes.Blink)
                || attributes.HasAttribute(CharacterAttributes.RapidBlink))
            {
                appearance.HasBlinkingText = true;
                break;
            }
        }

        if (!isScrolledBack && frame.CursorVisible && frame.CursorRow == row)
        {
            appearance.HasCursor = true;
            appearance.CursorColumn = frame.CursorColumn;
            appearance.CursorStyle = frame.CursorStyle;
        }

        return appearance;
    }

    /// <summary>
    /// Whether any cell on this row differs from what the cache was drawn with.
    /// </summary>
    private bool RowCellsChanged(ScreenFrame frame, int row)
    {
        var cached = _cachedCells!;
        int start = row * frame.Width;
        for (int col = 0; col < frame.Width; col++)
        {
            if (!cached[start + col].Equals(frame[row, col]))
            {
                return true;
            }
        }
        return false;
    }

    private void StoreCells(ScreenFrame frame)
    {
        var cached = _cachedCells!;
        for (int row = 0; row < frame.Height; row++)
        {
            int start = row * frame.Width;
            for (int col = 0; col < frame.Width; col++)
            {
                cached[start + col] = frame[row, col];
            }
        }
    }

    /// <summary>
    /// Repaints the dirty rows into the cache, leaving every other pixel of it alone.
    /// </summary>
    private void RepaintDirtyRows(ScreenFrame frame, bool[] dirty, double scale,
        bool isScrolledBack, bool hasSelection)
    {
        // clear:false is what makes this incremental at all - the default overload wipes the
        // bitmap to transparent, which would throw away every row that did not change.
        using var cacheContext = _cache!.CreateDrawingContext(false);

        for (int row = 0; row < frame.Height; row++)
        {
            if (!dirty[row]) continue;

            // The band is computed and clipped in DEVICE pixels, snapped to whole ones.
            //
            // This is not tidiness. A row is _charHeight tall and _charHeight is fractional for a
            // system font, so a band expressed in cell-grid coordinates has fuzzy edges - and an
            // antialiased edge blends with whatever is ALREADY in the bitmap. On a fresh cache
            // that is transparent; on a reused one it is the previous frame. The same screen then
            // comes out different depending on whether the cache had been drawn before, which is
            // exactly what DirtyRowCacheTests caught: even a whole-screen scroll, where every row
            // is repainted, differed from a cold draw by several hundred pixels along the row
            // boundaries. Whole-pixel bands tile exactly and blend with nothing.
            int top = (int)Math.Round(row * _charHeight * scale);
            int bottom = (int)Math.Round((row + 1) * _charHeight * scale);
            if (row == frame.Height - 1) bottom = _cachePixelHeight;   // no sliver left at the foot
            if (bottom <= top) continue;

            var band = new Rect(0, top, _cachePixelWidth, bottom - top);
            using (cacheContext.PushClip(band))
            {
                // The GROUND each cached row is drawn onto. Under DECSCNM it has to be the
                // foreground colour, for the same reason the page fill does: the per-cell
                // rectangles land on fractional coordinates and do not tile exactly, so whatever
                // is underneath shows through as a three-pixel seam at every cell boundary. With
                // the old dark ground the inverted screen came out covered in a visible grid -
                // caught by looking at the PNG, not by any assertion.
                cacheContext.FillRectangle(
                    frame.ReverseScreen ? _defaultForeground : _defaultBackground, band);

                using (cacheContext.PushTransform(Matrix.CreateScale(scale, scale)))
                {
                    // The row above and below are drawn too, clipped to THIS row's band. A glyph
                    // is not guaranteed to stay inside its own row - a descender or a tall accent
                    // can reach across the boundary - and in a full repaint those pixels are
                    // there. Draw only the one row and the spill from its neighbours would be
                    // missing, which shows up as a smudge rather than as a failing test.
                    for (int neighbour = row - 1; neighbour <= row + 1; neighbour++)
                    {
                        if (neighbour < 0 || neighbour >= frame.Height) continue;
                        DrawRow(cacheContext, frame, neighbour, isScrolledBack, hasSelection);
                    }
                }
            }
        }
    }

    /// <summary>
    /// Draws the screen in cell-grid coordinates, with any magnification already applied by the
    /// caller. Split out from <see cref="Render"/> so the scale lives in exactly one place.
    /// </summary>
    private void RenderAtNaturalSize(DrawingContext context, Size availableSize, int scrollOffset)
    {
        // Sync font variant settings from emulator (e.g., ISO 646 national variants)
        SyncFontVariant();

        // Read the published frame, NOT the live buffer. The buffer is written by the session
        // pump; walking it from the UI thread tore 20-byte cells and could throw outright when a
        // concurrent resize swapped the array. See ScreenFrame.
        //
        // The reference is taken once and used for the whole draw, so the picture is internally
        // consistent even if the pump publishes again mid-frame.
        var frame = _emulator.LatestFrame;
        if (frame == null)
        {
            // Nothing has been published yet (a brand-new emulator that has processed no data).
            // Ask for a frame and draw the background this time round; the request repaints us.
            context.FillRectangle(_defaultBackground, new Rect(0, 0, availableSize.Width, availableSize.Height));
            _emulator.RequestFrame();
            return;
        }

        bool isScrolledBack = frame.IsScrolledBack;

        // Fill background. DECSCNM inverts the PAGE as well as the cells: "the screen displays
        // dark characters on a light background". Leaving this one brush alone would put inverted
        // text on the old dark ground and the screen would come out as a patchwork.
        context.FillRectangle(
            frame.ReverseScreen ? _defaultForeground : _defaultBackground,
            new Rect(0, 0, availableSize.Width, availableSize.Height));

        // Selection and search highlighting are answered PER ROW, not per cell.
        //
        // This used to build three HashSets every frame: GetSelectedCells().ToHashSet()
        // (one tuple per selected cell — 1,920 of them for a full-screen selection, plus a
        // buffer rescan per row for the whitespace trim) and one set per search-match
        // group. Sixty times a second, to answer questions that are integer comparisons.
        // It also put LINQ and foreach on the render hot path, which this project forbids.
        //
        // Now each row asks once for its selected column range and its match ranges, and
        // the column loop compares integers. No allocation, no LINQ, no foreach.
        bool hasSelection = _selectionManager?.HasSelection == true;

        // Recomputed from scratch each frame: a cell that stopped blinking must stop costing
        // repaints, and the only way to know is to look at the frame being drawn.
        bool sawBlinkingText = false;

        // Render each character cell
        for (int row = 0; row < frame.Height; row++)
        {
            if (DrawRow(context, frame, row, isScrolledBack, hasSelection))
            {
                sawBlinkingText = true;
            }
        }

        // Publish what this frame saw, so the timer knows whether a repaint would change
        // anything. Written after the sweep rather than during it: a frame with no blinking
        // cells left must clear the flag, and only the completed sweep knows that.
        System.Threading.Volatile.Write(ref _frameHasBlinkingText, sawBlinkingText);

        // Draw scrollback indicator when scrolled back
        if (isScrolledBack)
        {
            DrawScrollIndicator(context, availableSize, frame.ScrollOffset);
        }
    }

    /// <summary>
    /// Draws one row of cells: highlights, backgrounds, glyphs and the cursor if it is here.
    ///
    /// Split out of the render sweep so a cached screen can repaint a single row without a second
    /// copy of the drawing rules living somewhere else. It draws in cell-grid coordinates and
    /// clips nothing, so a caller that wants only one row's band must clip around it.
    /// </summary>
    /// <returns>
    /// Whether this row holds any blinking text.
    /// </returns>
    private bool DrawRow(DrawingContext context, ScreenFrame frame, int row, bool isScrolledBack, bool hasSelection)
    {
        bool sawBlinkingText = false;

        {
            // Selected column range on this row (inclusive), or no selection here.
            // Selection rows are numbered from the live screen, exactly as search matches are, so
            // the same conversion applies - see matchRow below.
            int selStart = 0, selEnd = -1;
            if (hasSelection && _selectionManager!.TryGetSelectedColumnRange(row - frame.ScrollOffset, out var s, out var e))
            {
                selStart = s;
                selEnd = e;
            }

            // The current search match on this row, and whether any other match is here.
            // Matches are few (a search produces a handful of ranges), so a linear scan of
            // the list per row costs less than building a set of cells per frame.
            //
            // A match is numbered in the SEARCH's coordinates, where 0 is the top of the live
            // screen and minus one is the line just above it. This row is a position in the
            // window, which shows history when the view is scrolled back - so scrolling back
            // three lines makes window row 0 show search row minus three. Comparing the two
            // numbers directly highlighted the wrong line the moment the user scrolled, and
            // could never highlight a match in history at all.
            int matchRow = row - frame.ScrollOffset;

            int currentStart = 0, currentEnd = -1;
            if (_searchMatches != null && _currentMatchIndex >= 0 && _currentMatchIndex < _searchMatches.Count)
            {
                var currentMatch = _searchMatches[_currentMatchIndex];
                if (currentMatch.Row == matchRow)
                {
                    currentStart = currentMatch.StartCol;
                    currentEnd = currentMatch.EndCol;
                }
            }

            // Double-size lines. DECDWL and DECDHL make every cell on the line twice as wide, so
            // buffer column c lands at screen column 2c and only the LEFT HALF of the line is on
            // screen — the emulator already refuses to move the cursor past that half
            // (TerminalEmulatorBase.LastUsableColumn), so anything beyond it is stale and must not
            // be drawn. The line-size bits sit on every cell of the row; cell 0 is read for the
            // answer, which is what LastUsableColumn does too.
            // ROW -1 IS THE LINE THAT HAS JUST SCROLLED OFF, drawn only while a smooth scroll is
            // sliding. It lives above the top of the screen and comes from the frame's own extra
            // row rather than from the grid, which has no such row to give.
            //
            // Everything else below degrades correctly on its own: y is row * _charHeight, which is
            // negative and therefore off the top exactly as it should be; the selection and search
            // lookups take a row number that matches nothing; and the cursor is never on it.
            bool aboveTop = row < 0;

            bool doubleWidthRow = !aboveTop && frame.Width > 0 && frame[row, 0].DoubleWidth;
            double cellWidth = doubleWidthRow ? _charWidth * 2 : _charWidth;
            int lastVisibleColumn = doubleWidthRow ? frame.Width / 2 - 1 : frame.Width - 1;

            for (int col = 0; col <= lastVisibleColumn; col++)
            {
                // The frame already resolved any scrollback offset on the pump thread, so the
                // live and scrolled-back cases read exactly the same way here.
                TerminalCell cell = aboveTop ? frame.GetCellAboveTop(col) : frame[row, col];

                var x = col * cellWidth;
                var y = row * _charHeight;

                // Selection and search state for this cell — integer comparisons against
                // the ranges resolved once for this row.
                bool isSelected = col >= selStart && col <= selEnd;
                bool isCurrentMatch = col >= currentStart && col <= currentEnd;
                bool isSearchMatch = isCurrentMatch || IsInAnySearchMatch(matchRow, col);

                // Get colors (with reverse video support)
                var fg = GetCellForeground(cell);
                var bg = GetCellBackground(cell);

                // TWO swaps that cancel, not one that wins. SGR reverse marks a single cell;
                // DECSCNM inverts the whole screen. A cell already marked reverse on a reversed
                // screen must come out looking NORMAL, which is what keeps a highlighted menu bar
                // standing out after a host turns the screen inverse. An exclusive-or says that
                // without needing a rule for the overlap.
                if (cell.Attributes.HasAttribute(CharacterAttributes.Reverse) != frame.ReverseScreen)
                {
                    (fg, bg) = (bg, fg); // Swap colors
                }

                // Draw highlights in priority order: selection > current match > other matches
                if (isSelected)
                {
                    // Selection highlight (highest priority)
                    context.FillRectangle(SelectionBrush, new Rect(x, y, cellWidth, _charHeight));
                }
                else if (isCurrentMatch)
                {
                    // Current search match highlight (bright yellow)
                    context.FillRectangle(CurrentMatchBrush, new Rect(x, y, cellWidth, _charHeight));
                }
                else if (isSearchMatch)
                {
                    // Other search match highlight (darker yellow)
                    context.FillRectangle(SearchMatchBrush, new Rect(x, y, cellWidth, _charHeight));
                }
                else
                {
                    // Skip the fill only when the cell's background is ALREADY WHAT IS UNDERNEATH.
                    // That is the default background normally, but under DECSCNM the ground is
                    // the foreground colour - so a cell that resolves back to the default
                    // background (SGR reverse on a reversed screen, where the two swaps cancel)
                    // must really be painted, or the inverted ground shows through and the cell
                    // comes out the wrong way round. Comparing against _defaultBackground
                    // unconditionally is what made that cell invisible.
                    var ground = frame.ReverseScreen ? _defaultForeground : _defaultBackground;
                    if (!ReferenceEquals(bg, ground))
                    {
                        context.FillRectangle(bg, new Rect(x, y, cellWidth, _charHeight));
                    }
                }

                // Blink is a CELL attribute (SGR 5 / SGR 6) and is separate from the cursor
                // blinking. Both were set by the emulators and ignored here, so text a host
                // marked as blinking simply rendered steady.
                bool blinks = cell.Attributes.HasAttribute(CharacterAttributes.Blink)
                              || cell.Attributes.HasAttribute(CharacterAttributes.RapidBlink);
                if (blinks)
                {
                    sawBlinkingText = true;
                }

                // Draw character if not space, not hidden, and not currently blinked off.
                // A blinked-off cell keeps the background drawn above and loses only its glyph.
                //
                // A CELL FROM A DOWNLOADED SET IS NEVER SKIPPED AS A SPACE. Its code may well be
                // 0x20 - a 96-character DRCS set starts there - and the shape the host drew for
                // that position is a character like any other. Skipping it would silently drop the
                // first character of every downloaded set, which is exactly what happened the first
                // time this was tried.
                bool fromDownloadedSet =
                    cell.CharacterSet == RetroTerm.Core.Terminal.Emulators.TerminalEmulatorBase.SoftFontCharacterSet;

                if (cell.Codepoint != 0 && (cell.Codepoint != ' ' || fromDownloadedSet)
                    && !cell.Attributes.HasAttribute(CharacterAttributes.Hidden)
                    && (!blinks || IsBlinkedVisible(cell.Attributes)))
                {
                    DrawCellGlyph(context, cell, x, y, fg);
                }

                // Draw cursor only when viewing live buffer (not scrolled back)
                if (!isScrolledBack && frame.CursorRow == row && frame.CursorColumn == col && frame.CursorVisible)
                {
                    // Record whether this frame actually wants a blinking cursor, so the timer can
                    // stay silent otherwise. A repaint twice a second per tab is not free, and a
                    // steady block cursor gains nothing from it.
                    var style = frame.CursorStyle;
                    System.Threading.Volatile.Write(ref _cursorIsBlinking,
                        style == CursorStyle.BlinkingBlock || style == CursorStyle.BlinkingUnderline || style == CursorStyle.BlinkingBar);

                    // The cursor is as wide as the cell it sits in, so it doubles with the line.
                    DrawCursor(context, x, y, cellWidth, style);
                }
            }
        }

        return sawBlinkingText;
    }

    /// <summary>
    /// Draws a scrollback indicator overlay at the top-right corner
    /// </summary>
    private void DrawScrollIndicator(DrawingContext context, Size availableSize, int scrollOffset)
    {
        // "1 lines back" reads as a bug in the program to anyone who sees it, and one line back is
        // the commonest case of all - it is what a single wheel notch or one press of Find gives.
        var text = scrollOffset == 1
            ? "Scrollback: 1 line back"
            : $"Scrollback: {scrollOffset} lines back";
        var formattedText = new FormattedText(
            text,
            System.Globalization.CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            new Typeface("Consolas"),
            12,
            new ImmutableSolidColorBrush(Color.FromRgb(255, 255, 200)));

        // Background pill behind the text
        var textWidth = formattedText.Width;
        var textHeight = formattedText.Height;
        var padding = 6.0;
        var pillX = availableSize.Width - textWidth - padding * 2 - 8;
        var pillY = 4.0;

        var pillBg = new ImmutableSolidColorBrush(Color.FromArgb(200, 40, 40, 40));
        context.FillRectangle(pillBg, new Rect(pillX, pillY, textWidth + padding * 2, textHeight + padding));

        context.DrawText(formattedText, new Point(pillX + padding, pillY + padding / 2));
    }

    /// <summary>
    /// Syncs the font's character set variant with the emulator's setting.
    /// Only updates when variant changes to avoid overhead on every render.
    /// </summary>
    private void SyncFontVariant()
    {
        // Only applicable for TDV emulators with a BitmapFontRenderer (TDV2215 / TDV2200)
        if (_fontRenderer is BitmapFontRenderer bitmapRenderer &&
            _emulator is TDVEmulatorBase tdvEmulator)
        {
            int currentVariant = tdvEmulator.CharacterSetVariant;
            if (currentVariant != _lastSyncedVariant)
            {
                if (bitmapRenderer.Font is FontTDV2215 font2215)
                    font2215.CharacterSetVariant = currentVariant;
                else if (bitmapRenderer.Font is FontTDV2200 font2200)
                    font2200.CharacterSetVariant = currentVariant;

                _lastSyncedVariant = currentVariant;
            }
        }
    }

    /// <summary>
    /// Draws a cell's glyph, applying double-size line scaling when the cell asks for it.
    ///
    /// DECDWL (<c>ESC # 6</c>) doubles the width. DECDHL (<c>ESC # 3</c> / <c>ESC # 4</c>) doubles
    /// both and splits ONE glyph across two rows: the top row shows its upper half, the row below
    /// shows the lower half. Both halves come out of the same 2x-scaled glyph, so they line up the
    /// way a real VT's character generator made them line up — drawing two independently scaled
    /// halves would leave a seam.
    ///
    /// THE DEFECT THIS FIXES. The emulator has stored the line-size bits correctly for a while, and
    /// the renderer ignored them completely, so a host sending ESC # 3/4/6 changed the buffer and
    /// produced nothing visible at all.
    ///
    /// The scaling is a transform wrapped around the ordinary draw rather than a second copy of the
    /// glyph code, so BOTH font renderers — bitmap (TDV) and system font (VT) — get it without
    /// knowing it exists.
    /// </summary>
    private void DrawCellGlyph(DrawingContext context, TerminalCell cell, double x, double y, IBrush foreground)
    {
        if (!cell.DoubleWidth)
        {
            DrawCharacter(context, cell, x, y, foreground);
            return;
        }

        // Where the FULL two-cells-tall glyph starts. For the bottom half of a double-height pair
        // the glyph began one row higher, so it is pulled up by a cell height and clipped back to
        // this row — that clip is what leaves only the lower half showing.
        double scaleY = cell.DoubleHeight ? 2.0 : 1.0;
        double glyphTop = cell.DoubleHeightBottom ? y - _charHeight : y;

        // Scale about the origin, then translate so the point the font renderer draws at, (x, y),
        // lands at (x, glyphTop). Solving p' = p * scale + t for that one point gives t directly.
        var matrix = Matrix.CreateScale(2.0, scaleY)
                     * Matrix.CreateTranslation(x - x * 2.0, glyphTop - y * scaleY);

        // Clip FIRST, while the context is still in device coordinates, so the rectangle means the
        // one cell being drawn.
        using (context.PushClip(new Rect(x, y, _charWidth * 2.0, _charHeight)))
        using (context.PushTransform(matrix))
        {
            DrawCharacter(context, cell, x, y, foreground);
        }
    }

    private void DrawCharacter(DrawingContext context, TerminalCell cell, double x, double y, IBrush foreground)
    {
        // Check for Bold attribute - synthetic bold for bitmap fonts
        // CRT terminals achieved bold by printing the same character twice at the same position,
        // which reinforced the phosphor and created a thicker/brighter appearance.
        // We emulate this by drawing the glyph twice: once at (x,y) and once at (x+1,y)
        bool isBold = cell.Attributes.HasAttribute(CharacterAttributes.Bold);

        if (isBold && _fontRenderer is BitmapFontRenderer)
        {
            // Draw first pass at normal position
            _fontRenderer.DrawCharacter(context, cell, x, y, foreground);

            // Draw second pass offset by 1 pixel to the right (synthetic bold)
            _fontRenderer.DrawCharacter(context, cell, x + 1, y, foreground);
        }
        else
        {
            // Standard rendering (SystemFontRenderer has native bold via FontWeight)
            _fontRenderer.DrawCharacter(context, cell, x, y, foreground);
        }
    }

    /// <summary>
    /// Draws the text cursor in the style the host asked for.
    /// </summary>
    /// <param name="context">
    /// Where to draw.
    /// </param>
    /// <param name="x">
    /// Left edge of the cell, in pixels.
    /// </param>
    /// <param name="y">
    /// Top edge of the cell, in pixels.
    /// </param>
    /// <param name="width">
    /// Width of the cell the cursor sits in. Not always <see cref="_charWidth"/>: on a
    /// double-size line every cell is twice as wide, and a half-width cursor there would point
    /// at the left half of a character.
    /// </param>
    /// <param name="style">
    /// Block, underline or bar, and whether it is hollow.
    /// </param>
    private void DrawCursor(DrawingContext context, double x, double y, double width, CursorStyle style)
    {
        var cursorBrush = _defaultForeground;

        switch (style)
        {
            case CursorStyle.Block:
                // Fill entire cell
                context.FillRectangle(cursorBrush, new Rect(x, y, width, _charHeight));
                break;

            case CursorStyle.Underline:
                // Draw line at bottom of cell
                var underlinePen = new Pen(cursorBrush, 2);
                context.DrawLine(underlinePen,
                    new Point(x, y + _charHeight - 1),
                    new Point(x + width, y + _charHeight - 1));
                break;

            case CursorStyle.Bar:
                // Draw vertical line at left of cell
                var barPen = new Pen(cursorBrush, 2);
                context.DrawLine(barPen,
                    new Point(x, y),
                    new Point(x, y + _charHeight));
                break;

            case CursorStyle.BlinkingBlock:
            case CursorStyle.BlinkingUnderline:
            case CursorStyle.BlinkingBar:
                // Handle blinking variants (use cursor blink timer visibility)
                if (System.Threading.Volatile.Read(ref _cursorVisible))
                {
                    // Draw same as non-blinking version
                    DrawCursor(context, x, y, width, style == CursorStyle.BlinkingBlock ? CursorStyle.Block :
                                             style == CursorStyle.BlinkingUnderline ? CursorStyle.Underline : CursorStyle.Bar);
                }
                break;
        }
    }

    private IBrush GetCellForeground(TerminalCell cell)
    {
        IBrush brush;

        // A character ReGIS has written a plane value onto takes its colour from the graphics map
        // and nothing else - not the theme, not SGR. On a VT340 that is not a rule anyone wrote:
        // text pixels ARE bitmap pixels, so once a plane write has changed their value the colour
        // map is the only thing that decides what they look like. See TerminalCell.RegisColorIndex.
        //
        // Ahead of the Dim handling below on purpose. Dim is an SGR rendition, and a pixel value is
        // not - dimming a colour the map chose would be inventing a shade the hardware has no way
        // to show.
        if (cell.HasRegisColor)
        {
            var mapped = _emulator.GraphicsColorMap.Register(cell.RegisColorIndex);
            return new ImmutableSolidColorBrush(Color.FromArgb(255, mapped.R, mapped.G, mapped.B));
        }

        if (cell.Foreground.IsDefault)
            brush = _defaultForeground;
        else if (cell.Foreground.IsIndexed && cell.Foreground.Index < _palette.Length)
            brush = _palette[cell.Foreground.Index];
        else if (cell.Foreground.IsRgb)
        {
            var (r, g, b) = cell.Foreground.ToRgb();
            brush = new ImmutableSolidColorBrush(Color.FromRgb(r, g, b));
        }
        else
            brush = _defaultForeground;

        // Apply Dim attribute by darkening the color (50% brightness)
        if (cell.Attributes.HasAttribute(CharacterAttributes.Dim))
        {
            if (brush is ImmutableSolidColorBrush solidBrush)
            {
                var color = solidBrush.Color;
                // Reduce brightness by 50% for authentic dim effect
                var dimmed = Color.FromArgb(
                    color.A,
                    (byte)(color.R * 0.5),
                    (byte)(color.G * 0.5),
                    (byte)(color.B * 0.5)
                );
                return new ImmutableSolidColorBrush(dimmed);
            }
        }

        // Apply Bold attribute by brightening the color
        // CRT terminals achieved bold via phosphor reinforcement (double-printing),
        // which produced both thicker strokes AND increased luminance.
        // We increase brightness by ~40% to simulate the phosphor glow effect.
        if (cell.Attributes.HasAttribute(CharacterAttributes.Bold))
        {
            if (brush is ImmutableSolidColorBrush solidBrush)
            {
                var color = solidBrush.Color;
                // Increase brightness by 40% (clamped to 255)
                var brightened = Color.FromArgb(
                    color.A,
                    (byte)Math.Min(255, color.R + (255 - color.R) * 0.4),
                    (byte)Math.Min(255, color.G + (255 - color.G) * 0.4),
                    (byte)Math.Min(255, color.B + (255 - color.B) * 0.4)
                );
                return new ImmutableSolidColorBrush(brightened);
            }
        }

        return brush;
    }

    private IBrush GetCellBackground(TerminalCell cell)
    {
        if (cell.Background.IsDefault)
            return _defaultBackground;

        if (cell.Background.IsIndexed && cell.Background.Index < _palette.Length)
            return _palette[cell.Background.Index];

        if (cell.Background.IsRgb)
        {
            var (r, g, b) = cell.Background.ToRgb();
            return new ImmutableSolidColorBrush(Color.FromRgb(r, g, b));
        }

        return _defaultBackground;
    }

    /// <summary>
    /// Calculates the required size for the terminal display
    /// </summary>
    public Size CalculateSize(int cols, int rows)
    {
        return new Size(cols * _charWidth, rows * _charHeight);
    }

    /// <summary>
    /// Stops the cursor blink timer and drops its subscribers.
    ///
    /// This EXISTED and nobody called it: TerminalCanvas.SetEmulator replaced _renderer on every
    /// terminal-type change and left the old one's 500 ms timer running forever, holding the
    /// renderer — and through it the emulator, palette and selection manager — alive. A Dispose
    /// that is never called is not a resource policy.
    /// </summary>
    public void Dispose()
    {
        _cursorBlinkTimer?.Stop();
        _cursorBlinkTimer?.Dispose();

        // The cached screen is a real GPU/native bitmap, not just managed memory.
        _cache?.Dispose();
        _cache = null;
        _cacheValid = false;

        // Drop subscribers too: a stopped timer cannot fire, but a live event keeps whoever
        // subscribed reachable from this renderer for as long as the renderer is.
        BlinkStateChanged = null;
    }
}

