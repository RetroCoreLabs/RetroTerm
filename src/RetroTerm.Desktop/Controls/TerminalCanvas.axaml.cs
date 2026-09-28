using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using RetroTerm.Core.Clipboard;
using RetroTerm.Core.Selection;
using RetroTerm.Core.Terminal.Emulators;
using RetroTerm.Core.Terminal.Input;
using RetroTerm.Desktop.Helpers;
using RetroTerm.Desktop.Rendering;
using RetroTerm.Desktop.Services;

// Avalonia has a MouseButton of its own, so the bare name is ambiguous in this file. The reports
// this canvas builds are the terminal's, so that is the one the short name means here; the two
// places that need Avalonia's say so in full.
using MouseButton = RetroTerm.Core.Terminal.Input.MouseButton;

namespace RetroTerm.Desktop.Controls;

public partial class TerminalCanvas : Control
{
    private TerminalEmulatorBase? _emulator;
    private TerminalRenderer? _renderer;
    private IKeyboardMapper? _keyboardMapper;
    private SelectionManager? _selectionManager;
    private ClipboardManager? _clipboardManager;
    private AvaloniaClipboardService? _clipboardService;

    // Selection state
    private bool _isSelecting;
    private Point? _selectionStart;
    private int _clickCount;
    private System.DateTime _lastClickTime;

    // Scrollback viewport state
    private int _scrollOffset; // 0 = live view, >0 = lines scrolled back
    private const int ScrollLinesPerNotch = 3;

    // Auto-scaling state
    private double _scale = 1.0;
    private double _offsetX;
    private double _offsetY;

    // Where the view sits when the zoomed picture is bigger than the window. Both are zero or
    // negative: zero shows the terminal's own top-left corner, and more negative slides the picture
    // further up and left. Meaningless while the picture fits, and reset to zero there.
    private double _panX;
    private double _panY;

    // The cursor position the pan was last aimed at, so a cursor that has NOT moved does not keep
    // dragging the view back while the reader is looking somewhere else on purpose.
    private int _lastCursorRow = -1;
    private int _lastCursorColumn = -1;

    // Set when the zoom changes, so the first frame at a new magnification aims at the cursor even
    // though the cursor itself did not move.
    private bool _aimAtCursorNextFrame;

    // Middle-button drag state.
    private bool _isPanning;
    private Point _panPointerStart;
    private double _panStartX;
    private double _panStartY;
    private static readonly ImmutableSolidColorBrush LetterboxBrush =
        new(Color.FromRgb(0, 25, 17)); // #001911 — matches terminal background

    // Tracks physically held keys to suppress repeat-press mirror events
    private readonly HashSet<Key> _heldKeys = new();

    /// <summary>
    /// Fired on every physical key down/up so the virtual keyboard can mirror the visual state.
    /// Parameters: (vkCode, modifiers, pressed)
    /// </summary>
    public event Action<int, Core.Terminal.Input.KeyModifiers, bool>? KeyMirrored;

    /// <summary>
    /// Fired when the scroll offset changes. Parameter is the new offset (0 = live).
    /// </summary>
    public event Action<int>? ScrollOffsetChanged;

    /// <summary>
    /// Raised when ReGIS graphics input mode starts or stops, so the status line can say so.
    /// </summary>
    /// <remarks>
    /// One-shot mode stops the screen changing while the operator aims the crosshair. Without a note
    /// saying why, a paused session is indistinguishable from a dead one - which is the whole reason
    /// this event exists. It carries the mode so the status line can word the two cases differently.
    /// </remarks>
    public event Action<RetroTerm.Core.Terminal.Graphics.RegisGraphicsInputMode>? RegisGraphicsInputChanged;

    /// <summary>
    /// Whether the viewport is scrolled back into history (not showing live buffer)
    /// </summary>
    public bool IsScrolledBack => _scrollOffset > 0;

    /// <summary>
    /// Snaps the viewport back to the live buffer
    /// </summary>
    public void ScrollToBottom()
    {
        SetScrollOffset(0);
    }

    /// <summary>
    /// Brings a search match into view.
    /// </summary>
    /// <param name="searchRow">
    /// The match's row in the SEARCH's coordinates: 0 is the top of the live screen and minus one
    /// is the line immediately above it, which is how ScrollbackSearch numbers a match in history.
    /// </param>
    /// <remarks>
    /// Without this, finding a match in the scrollback moved the counter to "3 of 5" and left the
    /// screen where it was, so the user was told about a line they could not see. A match on the
    /// live screen snaps the view back to the bottom for the same reason - it is on the screen the
    /// user is not currently looking at.
    /// </remarks>
    public void ScrollToSearchRow(int searchRow)
    {
        if (_emulator == null) return;

        // A history match goes to the TOP row of the window, so the lines after it are visible -
        // a match is nearly always read forwards from where it was found.
        int offset = searchRow < 0 ? -searchRow : 0;

        var frame = _emulator.LatestFrame;
        int scrollbackCount = frame != null ? frame.ScrollbackLineCount : _emulator.GetBuffer().ScrollbackLineCount;
        if (offset > scrollbackCount) offset = scrollbackCount;

        SetScrollOffset(offset);
    }

    /// <summary>
    /// Moves the viewport and asks for a frame that shows it.
    ///
    /// The offset has to reach the emulator because the frame is captured on the session pump —
    /// resolving scrollback means reading the ring, which only the pump may do. Simply
    /// invalidating would redraw the frame we already have, i.e. the old position, and on an idle
    /// terminal (no incoming data) nothing would ever produce a new one.
    /// </summary>
    private void SetScrollOffset(int newOffset)
    {
        if (newOffset == _scrollOffset) return;

        _scrollOffset = newOffset;

        if (_emulator != null)
        {
            _emulator.ViewScrollOffset = newOffset;
            _emulator.RequestFrame();
        }

        ScrollOffsetChanged?.Invoke(newOffset);
        InvalidateVisual();
    }

    public TerminalCanvas()
    {
        // Load minimal XAML
        AvaloniaXamlLoader.Load(this);

        // Set properties that Control needs
        Focusable = true;
        ClipToBounds = true;
        // Background is drawn in Render() method

        // Handle keyboard input
        KeyDown += OnKeyDown;
        KeyUp += OnKeyUp;
        TextInput += OnTextInput;
        PointerPressed += OnPointerPressed;
        PointerMoved += OnPointerMoved;
        PointerReleased += OnPointerReleased;
        AttachedToVisualTree += (s, e) => Focus(); // Ensure focus on load

        // Focus reporting (mode 1004). A host that asked to be told wants to know when the user
        // looked away - it is how an editor stops blinking a cursor in a window nobody is watching.
        // The emulator stays silent unless the mode is on, so this costs nothing otherwise.
        GotFocus += (s, e) => _emulator?.ReportFocusChange(true);
        LostFocus += (s, e) => _emulator?.ReportFocusChange(false);
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);

        if (_emulator == null) return;

        // Control plus the wheel zooms, before anything else looks at it - including the host.
        // Every browser and editor does this, so it is what a reader will try, and a program
        // tracking the mouse must not swallow it or the gesture would work everywhere except
        // inside the program where the text is hardest to read.
        if (e.KeyModifiers.HasFlag(Avalonia.Input.KeyModifiers.Control))
        {
            if (e.Delta.Y != 0)
            {
                StepZoom(e.Delta.Y > 0 ? 1 : -1);
                e.Handled = true;
            }

            return;
        }

        // A host tracking the mouse gets the wheel too - that is how a full-screen program scrolls
        // its own view. Shift still forces the local behaviour, so the scrollback is never out of
        // reach.
        if (TryReportMouseToHost(e.GetPosition(this), e.KeyModifiers, MouseEventKind.Press,
                e.Delta.Y > 0 ? MouseButton.WheelUp : MouseButton.WheelDown))
        {
            e.Handled = true;
            return;
        }

        // Disable scrollback when alternate buffer is active (vim, less, etc.)
        var buffer = _emulator.GetBuffer();
        if (buffer.IsUsingAlternateBuffer) return;

        // How much history there is comes from the last published frame when there is one: it was
        // read on the pump, which owns the buffer. Falling back to the live count is a plain int
        // read, so it cannot tear — it is only ever a frame out of date.
        var frame = _emulator.LatestFrame;
        int scrollbackCount = frame != null ? frame.ScrollbackLineCount : buffer.ScrollbackLineCount;
        if (scrollbackCount == 0 && _scrollOffset == 0) return;

        // Wheel up (positive delta) = scroll back into history
        // Wheel down (negative delta) = scroll toward live view
        int delta = e.Delta.Y > 0 ? ScrollLinesPerNotch : -ScrollLinesPerNotch;
        int newOffset = _scrollOffset + delta;

        // Clamp to valid range
        if (newOffset < 0) newOffset = 0;
        if (newOffset > scrollbackCount) newOffset = scrollbackCount;

        SetScrollOffset(newOffset);

        e.Handled = true;
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        // Ensure focus when clicked
        Focus();

        if (_emulator == null || _renderer == null) return;

        // Handle right-click: copy selected text if clicking within selection
        var properties = e.GetCurrentPoint(this).Properties;

        // ReGIS graphics input comes FIRST, ahead of even the host, and only for the left button.
        // While one-shot mode is up the host is suspended by definition, so there is nothing for
        // TryReportMouseToHost to report to and nothing arriving to select - the click can only
        // sensibly mean "put the cursor there". Middle-button panning and right-button copy are
        // below and still reachable.
        if (properties.IsLeftButtonPressed && HandleRegisGraphicsInputPointer(e.GetPosition(this)))
        {
            e.Handled = true;
            return;
        }

        // The host gets first refusal on the pointer, ahead of selection AND of the right-click
        // copy - a program tracking the mouse expects its own context menu on button 3.
        if (TryReportMouseToHost(e.GetPosition(this), e.KeyModifiers, MouseEventKind.Press,
                ToMouseButton(properties)))
        {
            e.Handled = true;
            return;
        }
        // Middle button drags the view when the magnified picture is bigger than the window. The
        // cursor-following above covers typing; this covers reading something the cursor is not on.
        // Nothing happens when the whole picture already fits, so the button stays free there.
        if (properties.IsMiddleButtonPressed && IsPictureLargerThanWindow())
        {
            _isPanning = true;
            _panPointerStart = e.GetPosition(this);
            _panStartX = _panX;
            _panStartY = _panY;
            e.Handled = true;
            return;
        }

        if (properties.IsRightButtonPressed)
        {
            if (_selectionManager != null && _selectionManager.HasSelection)
            {
                // Check if the click is within the selected area
                var rightClickPoint = e.GetPosition(this);
                var (clickRow, clickCol) = PixelToCell(rightClickPoint);
                var selectedCells = _selectionManager.GetSelectedCells().ToHashSet();

                if (selectedCells.Contains((clickRow, clickCol)))
                {
                    HandleCopy();
                    // Clear selection after copying to indicate action completed
                    _selectionManager.ClearSelection();
                    InvalidateVisual();
                    e.Handled = true;
                }
            }
            // If no selection or click outside selection, don't do anything (could add paste here later)
            return;
        }

        // Left-click: handle selection
        var point = e.GetPosition(this);
        var (row, col) = PixelToCell(point);

        // Handle click counting for word/line selection
        var now = System.DateTime.Now;
        if ((now - _lastClickTime).TotalMilliseconds < 500)
        {
            _clickCount++;
        }
        else
        {
            _clickCount = 1;
        }
        _lastClickTime = now;

        // Determine selection mode
        var mode = RetroTerm.Core.Selection.SelectionMode.Character;
        if (_clickCount == 2)
        {
            mode = RetroTerm.Core.Selection.SelectionMode.Word;
        }
        else if (_clickCount >= 3)
        {
            mode = RetroTerm.Core.Selection.SelectionMode.Line;
        }

        // Check for Alt+drag (rectangular selection)
        if (e.KeyModifiers.HasFlag(Avalonia.Input.KeyModifiers.Alt))
        {
            mode = RetroTerm.Core.Selection.SelectionMode.Rectangular;
        }

        // Start selection, in the SELECTION's coordinates rather than the window's. Row 0 there is
        // the top of the live screen, so a window scrolled back three lines is pointing at row
        // minus three - see TerminalCanvas.ScrollToSearchRow for the same numbering. Handing the
        // window row over unchanged highlighted the line under the pointer and copied whatever the
        // live screen happened to have at that row.
        _selectionManager?.StartSelection(row - _scrollOffset, col, mode);
        _isSelecting = true;
        _selectionStart = point;

        InvalidateVisual();
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        // Movement goes to the host whether or not a selection is in progress. The tracker itself
        // decides whether this particular movement is reported: mode 1002 wants it only while a
        // button is held, 1003 wants all of it, and the modes below them want none.
        var moved = e.GetCurrentPoint(this);
        if (TryReportMouseToHost(e.GetPosition(this), e.KeyModifiers, MouseEventKind.Motion,
                ToMouseButton(moved.Properties)))
        {
            e.Handled = true;
            return;
        }

        if (_isPanning)
        {
            // One pixel of pointer travel moves the view one pixel, which is what makes a drag feel
            // like dragging the paper rather than a proxy for it. The clamp is in PanToShowCursor.
            var now = e.GetPosition(this);
            _panX = _panStartX + (now.X - _panPointerStart.X);
            _panY = _panStartY + (now.Y - _panPointerStart.Y);
            InvalidateVisual();
            e.Handled = true;
            return;
        }

        if (!_isSelecting || _emulator == null || _renderer == null || _selectionManager == null) return;

        var point = e.GetPosition(this);
        var (row, col) = PixelToCell(point);

        _selectionManager.ExtendSelection(row - _scrollOffset, col);
        InvalidateVisual();
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        // Which button came UP is not in the current button state - by now it is no longer held -
        // so it comes from the event's own record of what changed.
        var released = e.InitialPressMouseButton switch
        {
            global::Avalonia.Input.MouseButton.Left => MouseButton.Left,
            global::Avalonia.Input.MouseButton.Middle => MouseButton.Middle,
            global::Avalonia.Input.MouseButton.Right => MouseButton.Right,
            _ => MouseButton.None,
        };

        if (TryReportMouseToHost(e.GetPosition(this), e.KeyModifiers, MouseEventKind.Release, released))
        {
            e.Handled = true;
            _isPanning = false;
            _isSelecting = false;
            _selectionStart = null;
            return;
        }

        _isPanning = false;
        _isSelecting = false;
        _selectionStart = null;
    }

    /// <summary>
    /// Offers a pointer event to the host, when the host asked for the mouse.
    /// </summary>
    /// <remarks>
    /// <para><b>Shift is the way out</b></para>
    /// A program that is tracking the mouse wants the same gesture the user drags a selection with,
    /// and both cannot have it. The host wins and holding SHIFT forces the local behaviour - the
    /// convention xterm, PuTTY and gnome-terminal all follow, so the muscle memory is already
    /// there. Without an override there would be no way to copy text out of a full-screen program
    /// at all.
    ///
    /// <para><b>Coordinates are one-based</b></para>
    /// The wire format counts from 1 at the top left; the buffer counts from 0. The conversion
    /// happens here, once, rather than in each of the three handlers.
    /// </remarks>
    /// <param name="point">
    /// Where the pointer is, in control coordinates.
    /// </param>
    /// <param name="modifiers">
    /// Modifier keys held.
    /// </param>
    /// <param name="kind">
    /// What happened.
    /// </param>
    /// <param name="button">
    /// Which button.
    /// </param>
    /// <returns>
    /// True when the host took the event and the canvas should do nothing else with it.
    /// </returns>
    private bool TryReportMouseToHost(Point point, Avalonia.Input.KeyModifiers modifiers,
        MouseEventKind kind, MouseButton button)
    {
        if (_emulator == null || !_emulator.Mouse.IsTracking) return false;
        if (modifiers.HasFlag(Avalonia.Input.KeyModifiers.Shift)) return false;

        var (row, col) = PixelToCell(point);
        return _emulator.ReportMouse(kind, button, col + 1, row + 1, ToMouseModifiers(modifiers));
    }

    /// <summary>
    /// Converts Avalonia's modifier flags to the ones a mouse report carries.
    /// </summary>
    /// <remarks>
    /// Shift is deliberately absent: it never reaches the host, because holding it is what tells
    /// this terminal to keep the event for itself.
    /// </remarks>
    /// <param name="modifiers">
    /// Modifier keys held.
    /// </param>
    /// <returns>
    /// The same modifiers in the wire format's numbering.
    /// </returns>
    private static MouseModifiers ToMouseModifiers(Avalonia.Input.KeyModifiers modifiers)
    {
        var result = MouseModifiers.None;
        if (modifiers.HasFlag(Avalonia.Input.KeyModifiers.Control)) result |= MouseModifiers.Control;
        if (modifiers.HasFlag(Avalonia.Input.KeyModifiers.Alt)) result |= MouseModifiers.Meta;
        return result;
    }

    /// <summary>
    /// Which button a pointer event is about.
    /// </summary>
    /// <param name="properties">
    /// The pointer's current button state.
    /// </param>
    /// <returns>
    /// The button, or <see cref="MouseButton.None"/> when nothing is held - which is what movement
    /// with no button down reports.
    /// </returns>
    private static MouseButton ToMouseButton(PointerPointProperties properties)
    {
        if (properties.IsLeftButtonPressed) return MouseButton.Left;
        if (properties.IsMiddleButtonPressed) return MouseButton.Middle;
        if (properties.IsRightButtonPressed) return MouseButton.Right;
        return MouseButton.None;
    }

    private (int Row, int Col) PixelToCell(Point point)
    {
        if (_renderer == null || _emulator == null)
            return (0, 0);

        var buffer = _emulator.GetBuffer();
        var charWidth = _renderer.GetCharWidth();
        var charHeight = _renderer.GetCharHeight();

        // Reverse the scale transform: subtract offset, divide by scale
        double x = (point.X - _offsetX) / _scale;
        double y = (point.Y - _offsetY) / _scale;

        var col = Math.Max(0, Math.Min(buffer.Width - 1, (int)(x / charWidth)));
        var row = Math.Max(0, Math.Min(buffer.Height - 1, (int)(y / charHeight)));

        return (row, col);
    }

    public void SetEmulator(TerminalEmulatorBase emulator)
    {
        if (emulator == null) throw new ArgumentNullException(nameof(emulator));

        // TEAR DOWN THE OLD ONE FIRST. This method runs again on every terminal-type change, and
        // it used to just overwrite _renderer — leaving the previous renderer's 500 ms blink timer
        // running for the life of the process, holding that renderer, its emulator, its palette
        // and its selection manager alive. One leaked timer per type change.
        if (_renderer != null)
        {
            _renderer.BlinkStateChanged -= OnBlinkStateChanged;
            _renderer.Dispose();
        }

        // Same story for Invalidated: it was subscribed with += and never removed, so calling
        // SetEmulator twice for the same emulator stacked handlers and repainted N times per
        // change. The Bell subscription below already guarded against exactly this.
        if (_emulator != null)
        {
            _emulator.Invalidated -= OnTerminalInvalidated;

            // Same reason as Invalidated above: a replaced emulator that kept its subscription would
            // go on announcing graphics input modes to a status line that is no longer showing it.
            _emulator.RegisInputModeChanged -= OnRegisInputModeChanged;

            // And the smooth-scroll signal, for the same reason: a replaced emulator that kept its
            // subscription would go on driving an animation for a screen nobody is looking at.
            _emulator.SmoothScrollLineScrolled -= OnSmoothScrollLineScrolled;

            // And any slide already in flight ends here. Carrying the offset across would show the
            // NEW emulator's first screen permanently shifted down by part of a line, and carrying
            // a running timer across would repaint forever for a scroll that is now history.
            StopSmoothScroll();
        }

        _emulator = emulator;
        _emulator.RegisInputModeChanged += OnRegisInputModeChanged;
        _emulator.SmoothScrollLineScrolled += OnSmoothScrollLineScrolled;
        _renderer = new TerminalRenderer(_emulator);
        _renderer.BlinkStateChanged += OnBlinkStateChanged;

        // Initialize selection manager
        _selectionManager = new SelectionManager(_emulator.GetBuffer());
        _renderer.SetSelectionManager(_selectionManager);

        // Initialize clipboard manager
        _clipboardService = new AvaloniaClipboardService();
        _clipboardManager = new ClipboardManager(_clipboardService);

        // Create terminal-specific keyboard mapper
        // Keyed on the emulator's PROFILE, not its class name. The old form meant renaming a class
        // silently downgraded that terminal's keyboard to VT100.
        _keyboardMapper = KeyboardMapperFactory.CreateMapper(emulator.Profile);

        // Subscribe to invalidation events. The matching -= is at the top of this method, next to
        // the renderer teardown, so a repeat call cannot stack handlers.
        _emulator.Invalidated += OnTerminalInvalidated;

        // Subscribe to BEL (0x07) so the host machine actually beeps.
        // -= first: SetEmulator may be called again for the same emulator instance
        // (e.g. on terminal-type change) and we must not stack duplicate handlers.
        _emulator.Bell -= OnTerminalBell;
        _emulator.Bell += OnTerminalBell;

        // Open the audio device now, in the background, so the FIRST BEL is as fast as
        // every later one instead of paying device-open latency at the worst moment.
        RetroTerm.Desktop.Services.BellService.Prime();

        // Force initial render
        InvalidateVisual();
    }

    /// <summary>
    /// The renderer this canvas draws through, or null before an emulator has been set.
    /// </summary>
    /// <remarks>
    /// Exposed alongside the other accessors below so a caller can ask what one character cell
    /// measures - which is what decides how many of them fit in a window.
    /// </remarks>
    /// <returns>
    /// The renderer, or null.
    /// </returns>
    public TerminalRenderer? GetRenderer() => _renderer;

    public SelectionManager? GetSelectionManager() => _selectionManager;
    public ClipboardManager? GetClipboardManager() => _clipboardManager;
    public IKeyboardMapper? GetKeyboardMapper() => _keyboardMapper;

    /// <summary>
    /// Applies a presentation theme - default colours, and on a monochrome theme the sixteen ANSI
    /// colours collapsed onto one phosphor.
    /// </summary>
    /// <remarks>
    /// Redraws immediately, so a theme change is visible without waiting for the next frame.
    /// </remarks>
    public void SetTheme(RetroTerm.Core.Terminal.Buffer.TerminalTheme theme)
    {
        _renderer?.SetTheme(theme);

        // The emulator is told the same colours, because a host may ASK what they are - OSC 10 and
        // OSC 11 are how vim and tmux decide whether they are on a dark or a light terminal. Core
        // has no theme of its own, so without this the answer would be a constant that stopped
        // being true the moment anyone changed the colours.
        if (_emulator != null)
        {
            _emulator.DisplayForeground = theme.DefaultForeground;
            _emulator.DisplayBackground = theme.DefaultBackground;
            _emulator.DisplayCursorColour = theme.DefaultForeground;
        }

        InvalidateVisual();
    }

    public void SetDefaultColors(Avalonia.Media.Color fg, Avalonia.Media.Color bg)
    {
        _renderer?.SetDefaultColors(fg, bg);
        InvalidateVisual();
    }

    /// <summary>
    /// Gets the current default foreground color, or null if no renderer.
    /// </summary>
    public Avalonia.Media.Color? GetDefaultForeground() => _renderer?.DefaultForegroundColor;

    /// <summary>
    /// Gets the current default background color, or null if no renderer.
    /// </summary>
    public Avalonia.Media.Color? GetDefaultBackground() => _renderer?.DefaultBackgroundColor;

    /// <summary>
    /// Sets search matches for highlighting
    /// </summary>
    public void SetSearchMatches(List<RetroTerm.Core.Search.SearchMatch>? matches, int currentMatchIndex = -1)
    {
        _renderer?.SetSearchMatches(matches, currentMatchIndex);
        InvalidateVisual();
    }

    private void OnTerminalInvalidated()
    {
        // A HOST CAN CHANGE THE GRID SIZE, and when it does the layout has to run again.
        //
        // DECCOLM is the one that bites. "reset" at a Linux shell sends ESC c then ESC[?3l, and
        // ApplyColumnMode resizes the grid to 80 columns. On a terminal whose size comes from the
        // window that is the wrong answer, and RequestTerminalSizeFor already knows it - but it
        // only runs from ArrangeOverride, and nothing here invalidated the layout, so no arrange
        // happened. The grid stayed 80 in a wider window, the fitted picture no longer spanned the
        // control, and OnRender centres anything that fits: the text sat about twenty columns in
        // from the left and STAYED there. Switching tab and back forced a layout pass, the size
        // request fired, and it silently corrected itself - which is what made it look haunted.
        //
        // Reported by Ronny on 21 August 2026, on a VT340 against a real host.
        var emulator = _emulator;
        if (emulator != null
            && (emulator.Width != _lastKnownGridWidth || emulator.Height != _lastKnownGridHeight))
        {
            _lastKnownGridWidth = emulator.Width;
            _lastKnownGridHeight = emulator.Height;

            Avalonia.Threading.Dispatcher.UIThread.Post(InvalidateMeasure);
        }

        // Request redraw on UI thread
        Avalonia.Threading.Dispatcher.UIThread.Post(InvalidateVisual);
    }

    /// <summary>
    /// The grid size this control last saw, so a host-driven change can be noticed.
    /// </summary>
    /// <remarks>
    /// Starts at zero so the first screen after an emulator is attached counts as a change and the
    /// layout runs once. See <see cref="OnTerminalInvalidated"/> for what this is guarding against.
    /// </remarks>
    private int _lastKnownGridWidth;

    /// <summary>
    /// See <see cref="_lastKnownGridWidth"/>.
    /// </summary>
    private int _lastKnownGridHeight;

    /// <summary>
    /// The cursor blink flag flipped, so the screen has to be redrawn for anyone to see it.
    ///
    /// Raised on the blink timer's thread — same marshalling as OnTerminalInvalidated. Without
    /// this the blink timer flipped a flag nobody looked at again until the host happened to send
    /// something, so on an idle session the cursor simply did not blink.
    /// </summary>
    private void OnBlinkStateChanged()
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(InvalidateVisual);
    }

    // ─────────────────────────────────────────────────────────────
    // DECSCLM - smooth scrolling
    //
    // The buffer scrolls instantly; only the PICTURE of it lags. Each frame the whole screen is
    // drawn a few pixels lower and the offset counts down to zero, so the picture appears to climb
    // into place. Nothing about the content changes while it slides, so the row cache stays valid
    // and the cached screen is blitted at an offset - one transform per frame, no glyph re-drawn.
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// How many steps one line of smooth scrolling is broken into.
    /// </summary>
    /// <remarks>
    /// <para><b>Why a DURATION and not a step count</b></para>
    /// Smooth motion is a question of step SIZE, not of how quickly the slide is over. A character
    /// cell here is around nineteen pixels; moving it in three steps means three jumps of six
    /// pixels, which is what the second attempt did and what Ronny called "still jagged". Moving it
    /// in nineteen steps of one pixel is smooth, and at sixty frames a second that takes about three
    /// hundred milliseconds whether anybody likes it or not.
    ///
    /// <para><b>Which caps how fast the PICTURE can move, but not how fast the host may send</b></para>
    /// A screen refresh is the smallest unit of motion there is. One line cannot be moved in fewer
    /// frames than it has pixels without the steps becoming visible, so the picture can only travel
    /// at about four lines a second. A real VT100 ran six and held the host back with XOFF to make
    /// it possible - it did not keep up, it refused to.
    ///
    /// This emulator cannot throttle a host, so it does the other thing: the lines are still in
    /// scrollback, so the picture runs BEHIND and walks forward one slide at a time until it catches
    /// up. Nothing is dropped and nothing jumps. Ronny's words were that a fast host "should render
    /// on lines that hasnt been shown yet, and will be shown AFTER the smooth scroll has catched up"
    /// - which is exactly right, and is what SmoothScrollBacklog on the emulator counts.
    ///
    /// The one thing that still jumps is a picture already a full screen behind. Past that the lag
    /// stops growing, because watching something minutes old is not a better answer than jumping.
    /// </remarks>
    private const int SmoothScrollDurationMilliseconds = 260;

    /// <summary>
    /// Pixels the screen is currently shifted down by, counting towards zero.
    /// </summary>
    private double _smoothScrollOffset;

    /// <summary>
    /// How far the slide had to travel, in pixels.
    /// </summary>
    private double _smoothScrollDistance;

    /// <summary>
    /// The frame timestamp the slide began at, or null before the first frame arrives.
    /// </summary>
    private TimeSpan? _smoothScrollStartedAt;

    /// <summary>
    /// A slide is running and the frame callback should keep going.
    /// </summary>
    /// <remarks>
    /// A SEPARATE flag rather than reading the offset or the start time, because a frame callback
    /// that has already been handed to the compositor cannot be cancelled - it WILL arrive. Without
    /// a flag to check, that stale callback finds a null start time, takes its own frame as the
    /// beginning, and quietly starts a whole new slide after the control was told to stop.
    /// </remarks>
    private bool _smoothScrollRunning;

    /// <summary>
    /// A whole-screen scroll happened while smooth scrolling was on.
    /// </summary>
    /// <remarks>
    /// Raised on the session pump, so everything it touches is posted to the UI thread.
    /// </remarks>
    private void OnSmoothScrollLineScrolled()
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(BeginSmoothScrollStep);
    }

    /// <summary>
    /// Starts a slide for one scrolled line, if one is not already running.
    /// </summary>
    /// <remarks>
    /// <para><b>Driven by the display's own frames, not by a timer</b></para>
    /// This used a DispatcherTimer and that was the whole problem. A timer is not synchronised to
    /// anything the screen does, so its ticks land between frames: some frames show no movement and
    /// the next shows two steps at once. The motion is uneven whatever the step size, and uneven
    /// motion is exactly what the eye reads as jagged. Ronny's words after the second attempt were
    /// "this is NOT how i remember smooth from the old days" - and he was right, because a real
    /// VT100 shifted the raster ONE SCAN LINE PER VIDEO FRAME, locked to the display.
    ///
    /// <see cref="TopLevel.RequestAnimationFrame"/> is that lock. One callback per frame, with the
    /// frame's own timestamp.
    ///
    /// <para><b>Position comes from elapsed TIME, never from counting steps</b></para>
    /// A step counter drifts the moment a frame is late or dropped, and then the slide arrives early
    /// or late by however much was lost. Reading the clock each frame means a dropped frame costs one
    /// missing picture rather than a permanent skew, which is the difference between motion that
    /// stutters once and motion that never looks right.
    ///
    /// <para><b>A slide already running is left alone, and the line is not lost</b></para>
    /// Re-arming part way through would reset the offset to a full cell and yank the picture back
    /// DOWN before it had finished climbing, and nothing in a real scroll ever moves backwards. The
    /// extra line is not dropped either: the emulator is counting how many the picture owes, so this
    /// returns and the running slide starts the next one the moment it lands.
    /// <para><b>Claiming the line and republishing happen together</b></para>
    /// Taking a line off the backlog moves the frame one line NEWER, and starting the slide shifts
    /// the picture one whole cell DOWN. Those cancel exactly, which is the whole trick - the viewer
    /// sees no movement at the instant a slide begins, only during it. Doing one without the other,
    /// or in two separate paints, is a visible jerk in whichever direction was applied first.
    /// </remarks>
    private void BeginSmoothScrollStep()
    {
        double cellHeight = _renderer?.GetCharHeight() ?? 0.0;
        if (cellHeight <= 0.0) return;
        if (_smoothScrollRunning) return;
        if (_emulator == null) return;

        var top = TopLevel.GetTopLevel(this);
        if (top == null) return;

        // Nothing owed means the picture is already up to date and there is nothing to animate.
        if (!_emulator.TryBeginSmoothScrollLine()) return;

        _smoothScrollDistance = System.Math.Round(cellHeight);
        _smoothScrollOffset = _smoothScrollDistance;
        _smoothScrollStartedAt = null;
        _smoothScrollRunning = true;

        // The frame on hand was captured at the old lag, so it is one line behind where the slide is
        // about to start from. Asking for a fresh one now is what makes the two halves cancel.
        _emulator.RequestFrame();

        top.RequestAnimationFrame(AdvanceSmoothScroll);
        InvalidateVisual();
    }

    /// <summary>
    /// Moves the slide to where the clock says it should be, once per displayed frame.
    /// </summary>
    /// <param name="frameTime">
    /// The frame's timestamp, supplied by the compositor.
    /// </param>
    /// <remarks>
    /// Asks for the next frame only while there is still travel left. A callback that re-registered
    /// unconditionally would repaint the screen at the display's refresh rate forever, for a scroll
    /// that finished a minute ago - the shape of leak this project already has a test for on the
    /// cursor blink.
    /// </remarks>
    private void AdvanceSmoothScroll(TimeSpan frameTime)
    {
        // A callback already queued with the compositor still arrives after StopSmoothScroll, so
        // the flag is checked before anything else is touched.
        if (!_smoothScrollRunning) return;

        _smoothScrollStartedAt ??= frameTime;

        double elapsed = (frameTime - _smoothScrollStartedAt.Value).TotalMilliseconds;
        double progress = elapsed / SmoothScrollDurationMilliseconds;

        if (progress >= 1.0)
        {
            _smoothScrollOffset = 0.0;
            _smoothScrollStartedAt = null;
            _smoothScrollRunning = false;
            InvalidateVisual();

            // STRAIGHT INTO THE NEXT ONE while the picture still owes lines. Waiting for another
            // scroll event to arrive would stall the moment the host stopped sending, leaving the
            // picture permanently short of the buffer with nothing left to nudge it forward.
            BeginSmoothScrollStep();
            return;
        }

        // ROUNDED, so the cached screen is never blitted at a fractional Y. Half a pixel of offset
        // resamples every glyph on the page and the whole screen goes soft for the length of the
        // slide, which reads as blur rather than as movement.
        _smoothScrollOffset = System.Math.Round(_smoothScrollDistance * (1.0 - progress));

        InvalidateVisual();

        var top = TopLevel.GetTopLevel(this);
        top?.RequestAnimationFrame(AdvanceSmoothScroll);
    }

    /// <summary>
    /// Stops any slide in progress and puts the screen back where it belongs.
    /// </summary>
    /// <remarks>
    /// Called when the control goes away or the emulator is replaced. Leaving the offset set would
    /// show the next screen permanently shifted down by part of a line.
    /// </remarks>
    private void StopSmoothScroll()
    {
        // Clearing the flag is what ends it: the frame callback asks for another frame only while
        // the flag is set, so there is no timer to stop.
        _smoothScrollRunning = false;
        _smoothScrollStartedAt = null;
        _smoothScrollOffset = 0.0;
    }

    /// <summary>
    /// Handles BEL (0x07) from the emulator by playing an audible bell.
    /// BellService is thread-safe, rate-limited and non-blocking, so this can be
    /// called straight from the network/parse thread without marshalling.
    /// </summary>
    private void OnTerminalBell()
    {
        // Straight through to the bell - no logging on this path. The audio device is
        // already open and the tone already synthesized, so Play() just hands the driver
        // a pointer. BellService logs on its own when playback actually fails.
        RetroTerm.Desktop.Services.BellService.Play();
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        var bounds = Bounds.Size;

        if (_renderer == null || _emulator == null)
        {
            // No terminal yet — just fill with background
            context.FillRectangle(LetterboxBrush, new Rect(0, 0, bounds.Width, bounds.Height));
            return;
        }

        // Size the drawing from the SAME frame the renderer will paint. Taking the dimensions from
        // the live buffer instead let the two disagree for one paint after a resize — the content
        // would be scaled as if it were the new size while still being the old one.
        var frame = _emulator.LatestFrame;
        var naturalSize = frame != null
            ? _renderer.CalculateSize(frame.Width, frame.Height)
            : _renderer.CalculateSize(_emulator.Width, _emulator.Height);

        if (naturalSize.Width <= 0 || naturalSize.Height <= 0)
        {
            context.FillRectangle(LetterboxBrush, new Rect(0, 0, bounds.Width, bounds.Height));
            return;
        }

        // THE ZOOM IS MEASURED FROM THE FITTED PICTURE, NOT FROM THE FONT'S NATURAL SIZE.
        //
        // Fit first: one uniform scale, aspect ratio preserved. That is the picture at 100 percent,
        // and every other percentage is a multiple of it - so 200 really is twice what you were just
        // looking at, and 75 is slightly smaller than it.
        //
        // It used to be a multiple of the FONT's natural size, and that made the whole ladder read
        // as broken. A fixed 80 by 24 grid fits a 1200 by 700 window at about 177 percent, so
        // choosing "100" shrank the picture by half and "75" left a postage stamp floating in a
        // black window. Every step below 175 was smaller than the default view. The arithmetic was
        // right and the baseline was wrong, which is worse, because each number looked defensible
        // on its own.
        double fitScale = Math.Min(bounds.Width / naturalSize.Width, bounds.Height / naturalSize.Height);

        // Not clamped back to fit above 100. Someone asking for 300 percent wants three times the
        // size and accepts that the edges go past the window; quietly giving them 100 would look
        // like the setting did nothing. What is off the edge is reachable - see PanToShowCursor.
        _scale = fitScale * (_zoomPercent / 100.0);

        // CENTRE ONLY WHAT FITS. An axis with room to spare is centred, which is the letterboxing
        // that has always been here. An axis that does NOT fit is panned instead, so the part you
        // are working in stays on screen and the rest is clipped off the far edge.
        //
        // Centring an over-sized picture is what made the zoom look dead. Half the excess was
        // subtracted from the offset, so at 300 percent in an ordinary window the terminal's origin
        // sat hundreds of pixels off the left edge and above the top - and a terminal writes AT the
        // origin. The prompt, the banner and the cursor were all outside the window and the screen
        // came up blank. Ronny hit it at 300 percent and again on the way back from full screen.
        // The sixteen zoom tests were all assertions about the ZoomPercent integer, so not one of
        // them could see it.
        double scaledWidth = naturalSize.Width * _scale;
        double scaledHeight = naturalSize.Height * _scale;

        PanToShowCursor(frame, bounds, scaledWidth, scaledHeight);

        _offsetX = scaledWidth <= bounds.Width ? (bounds.Width - scaledWidth) / 2.0 : _panX;
        _offsetY = scaledHeight <= bounds.Height ? (bounds.Height - scaledHeight) / 2.0 : _panY;

        // Fill letterbox areas with background
        context.FillRectangle(LetterboxBrush, new Rect(0, 0, bounds.Width, bounds.Height));

        // Draw terminal content at natural coordinates, scaled & centered.
        //
        // The SCALE is handed to the renderer rather than pushed here: it needs to know the device
        // pixel size it is really drawing at, so a cached screen can be held at that resolution
        // instead of being magnified on the way out and coming out blurred.
        using (context.PushTransform(Matrix.CreateTranslation(_offsetX, _offsetY)))
        {
            // The slide, handed over each frame. Zero for an ordinary frame, and the renderer's
            // ordinary path is then completely untouched.
            _renderer.SmoothScrollPixelOffset = _smoothScrollOffset;

            _renderer.Render(context, naturalSize, _scrollOffset, _scale);
        }
    }

    /// <summary>
    /// Whether the picture currently overflows the control, so panning means something.
    /// </summary>
    /// <returns>
    /// True when either axis is larger than the window.
    /// </returns>
    private bool IsPictureLargerThanWindow()
    {
        if (_renderer == null || _emulator == null) return false;

        var frame = _emulator.LatestFrame;
        var natural = frame != null
            ? _renderer.CalculateSize(frame.Width, frame.Height)
            : _renderer.CalculateSize(_emulator.Width, _emulator.Height);

        return natural.Width * _scale > Bounds.Width
            || natural.Height * _scale > Bounds.Height;
    }

    /// <summary>
    /// Keeps the cursor on screen when the magnified picture is bigger than the window, and holds
    /// the view inside the picture's own edges.
    /// </summary>
    /// <remarks>
    /// <para><b>Following the cursor, not the corner</b></para>
    /// Anchoring at the top-left corner is enough to stop the screen coming up blank, but at 300
    /// percent it means only ever seeing the top-left of the terminal - and the line being typed is
    /// wherever the cursor is. So the view moves to keep the cursor in it, which needs no controls
    /// at all and costs nothing when the picture fits.
    /// It only aims when the cursor has actually MOVED (or the zoom just changed). Otherwise a
    /// reader who dragged the view somewhere to read it would be pulled straight back on the next
    /// repaint - the blink of the cursor alone would do it, sixty times a minute.
    /// </remarks>
    /// <param name="frame">
    /// The frame being drawn, or null when nothing has been published yet.
    /// </param>
    /// <param name="bounds">
    /// The control's size.
    /// </param>
    /// <param name="scaledWidth">
    /// Width of the magnified picture.
    /// </param>
    /// <param name="scaledHeight">
    /// Height of the magnified picture.
    /// </param>
    private void PanToShowCursor(RetroTerm.Core.Terminal.Rendering.ScreenFrame? frame, Size bounds,
        double scaledWidth, double scaledHeight)
    {
        bool overflowsX = scaledWidth > bounds.Width;
        bool overflowsY = scaledHeight > bounds.Height;

        // An axis that fits is centred by the caller, and its pan means nothing. Zeroing it here is
        // what makes zooming back out and in again start at the corner rather than at whatever the
        // view happened to be left at.
        if (!overflowsX) _panX = 0;
        if (!overflowsY) _panY = 0;

        bool cursorMoved = frame != null
            && (frame.CursorRow != _lastCursorRow || frame.CursorColumn != _lastCursorColumn);

        if (frame != null)
        {
            _lastCursorRow = frame.CursorRow;
            _lastCursorColumn = frame.CursorColumn;
        }

        if (!overflowsX && !overflowsY)
        {
            _aimAtCursorNextFrame = false;
            return;
        }

        // Not while a drag is in progress: the pointer is the thing moving the view then, and
        // fighting it would make the drag stick.
        if ((cursorMoved || _aimAtCursorNextFrame) && !_isPanning && frame != null && _renderer != null)
        {
            double cellWidth = _renderer.GetCharWidth() * _scale;
            double cellHeight = _renderer.GetCharHeight() * _scale;

            double cursorLeft = frame.CursorColumn * cellWidth;
            double cursorTop = frame.CursorRow * cellHeight;

            if (overflowsX)
            {
                if (_panX + cursorLeft < 0)
                {
                    _panX = -cursorLeft;
                }
                else if (_panX + cursorLeft + cellWidth > bounds.Width)
                {
                    _panX = bounds.Width - cursorLeft - cellWidth;
                }
            }

            if (overflowsY)
            {
                if (_panY + cursorTop < 0)
                {
                    _panY = -cursorTop;
                }
                else if (_panY + cursorTop + cellHeight > bounds.Height)
                {
                    _panY = bounds.Height - cursorTop - cellHeight;
                }
            }
        }

        _aimAtCursorNextFrame = false;

        // Hold the view inside the picture. Without this a drag could pull the terminal entirely off
        // the window, which is the blank screen this whole change is about, reintroduced by hand.
        if (overflowsX) _panX = Math.Min(0, Math.Max(bounds.Width - scaledWidth, _panX));
        if (overflowsY) _panY = Math.Min(0, Math.Max(bounds.Height - scaledHeight, _panY));
    }

    /// <summary>
    /// The zoom steps the keyboard shortcuts and the status bar both walk, as percentages.
    /// </summary>
    /// <remarks>
    /// ONE ladder, shared, so Ctrl+plus and the dropdown cannot end up offering different numbers.
    /// That is the same trap the script DSL and the MCP provider fell into, and the fix is the
    /// same: put the rule in one place both surfaces read.
    /// </remarks>
    public static readonly int[] ZoomSteps = { 50, 75, 100, 125, 150, 175, 200, 250, 300, 400 };

    /// <summary>
    /// The zoom as a percentage of the fitted picture. 100 is the normal view.
    /// </summary>
    /// <remarks>
    /// <para><b>100 is what the window shows, not what the font measures</b></para>
    /// The percentage multiplies the FITTED size - the picture the window would show on its own - so
    /// 100 is exactly the normal view, 200 is twice it and 75 is a little smaller. It used to
    /// multiply the font's natural size, which meant a fixed 80 by 24 grid filled an ordinary window
    /// at about 177 percent, "100" made everything abruptly smaller, and every step below 175 was
    /// smaller than the default. See the scale calculation in Render for the whole story.
    /// <para><b>100 also hands the size back to the window</b></para>
    /// At 100 the window decides how many columns and rows there are, for a terminal whose profile
    /// allows it. At any other value the GRID IS PINNED and the picture is magnified instead - which
    /// is what makes zoom useful on a terminal whose geometry is part of what it is. A TDV2200 is 80
    /// by 25 because the hardware was; growing it to 137 columns because the window is wide is not a
    /// bigger TDV2200, it is a different machine.
    /// The two must not both be active, or dragging a window would fight the zoom: a bigger window
    /// would add columns, which would make the fitted size larger, which would change what the zoom
    /// is a multiple of.
    /// Zero is accepted and means 100, because "fit" was what zero meant before the two became the
    /// same thing, and a saved setting or a script may still say it.
    /// </remarks>
    public int ZoomPercent
    {
        get => _zoomPercent;
        set
        {
            int wanted = value <= 0 ? 100 : value;
            if (wanted == _zoomPercent) return;

            _zoomPercent = wanted;
            ZoomChanged?.Invoke(wanted);

            // The new magnification aims at the cursor even though the cursor has not moved -
            // otherwise zooming in on a full screen would always land on the top-left corner,
            // whatever the reader was looking at.
            _aimAtCursorNextFrame = true;

            // A change either way redraws, and leaving 100 has to stop the size requests while
            // returning to it has to start them again - both of which happen on the next arrange.
            InvalidateMeasure();
            InvalidateVisual();
        }
    }

    /// <summary>
    /// The DEL character, 0x7F, as the text an input handler sends.
    /// </summary>
    /// <remarks>
    /// Written as a four-digit \u escape on purpose. A raw 0x7F typed into the source is invisible
    /// in every diff and editor - this constant was committed that way once and had to be dug out
    /// by dumping the file's bytes. The C# \x escape is no good either: it is greedy, so "\x7f"
    /// followed by a hex letter silently becomes one different character.
    /// </remarks>
    private const string DelCharacter = "\u007F";

    /// <summary>
    /// Whether the Backspace key sends DEL (0x7F) instead of BS (0x08), or null when the
    /// connection has not chosen.
    /// </summary>
    /// <remarks>
    /// Set from the connection - see the Keyboard tab of the connection editor. Null defers to
    /// the emulator's profile (see <see cref="EffectiveBackspaceSendsDel"/>), which is how every
    /// TDV sends DEL by default since 27 September 2026 while a VT keeps sending BS.
    /// </remarks>
    public bool? BackspaceSendsDel { get; set; }

    /// <summary>
    /// What the Backspace key will actually send: the connection's choice if it made one,
    /// otherwise the terminal's own default from its profile. False with no emulator.
    /// </summary>
    public bool EffectiveBackspaceSendsDel
        => BackspaceSendsDel ?? (_emulator?.Profile.BackspaceSendsDel ?? false);

    /// <summary>
    /// Whether the Delete key sends DEL (0x7F) instead of this terminal's own sequence.
    /// </summary>
    public bool DeleteSendsDel { get; set; }

    /// <summary>
    /// The zoom that means "the normal view": the picture the window fits on its own.
    /// </summary>
    public const int NaturalZoomPercent = 100;

    private int _zoomPercent = NaturalZoomPercent;

    /// <summary>
    /// The zoom changed, as a percentage, or 0 for "fit the window".
    /// </summary>
    public event Action<int>? ZoomChanged;

    /// <summary>
    /// Moves one step up or down the shared ladder.
    /// </summary>
    /// <param name="direction">
    /// Positive to enlarge, negative to shrink.
    /// </param>
    public void StepZoom(int direction)
    {
        if (direction == 0) return;

        int at = -1;
        for (int i = 0; i < ZoomSteps.Length; i++)
        {
            if (ZoomSteps[i] == _zoomPercent) { at = i; break; }
        }

        if (at < 0)
        {
            // Not on the ladder - a value set from somewhere else. Move to the nearest step in the
            // direction asked for rather than snapping backwards.
            for (int i = 0; i < ZoomSteps.Length; i++)
            {
                if (direction > 0 && ZoomSteps[i] > _zoomPercent) { ZoomPercent = ZoomSteps[i]; return; }
            }

            for (int i = ZoomSteps.Length - 1; i >= 0; i--)
            {
                if (direction < 0 && ZoomSteps[i] < _zoomPercent) { ZoomPercent = ZoomSteps[i]; return; }
            }

            return;
        }

        int next = at + (direction > 0 ? 1 : -1);
        if (next < 0 || next >= ZoomSteps.Length) return;   // already at an end; stay there

        ZoomPercent = ZoomSteps[next];
    }

    /// <summary>
    /// Whether the window decides how many columns and rows there are, or null to let the
    /// terminal's own profile decide.
    /// </summary>
    /// <remarks>
    /// <para><b>The default comes from the profile, not from a list kept here</b></para>
    /// <see cref="RetroTerm.Core.Terminal.Profiles.TerminalFeatures.HostResize"/> already draws
    /// exactly this line, and already withholds itself from the TDV profiles, with the reason
    /// written beside it: a TDV2200 has one screen, 80 by 25, wired that way, and growing it because
    /// a window is wide would be an emulator inventing a machine that never existed. A VT or an
    /// xterm has no such constraint.
    /// So this is read from the emulator rather than decided again, and a second list that could
    /// drift never exists. A connection may still override it either way - some people want a VT100
    /// held at 80 by 24, and Ronny's TDVs can be configured for 132 columns in SINTRAN.
    /// A terminal that does NOT follow the window behaves as everything used to: the grid stays put
    /// and the picture is scaled to fit, which is what <see cref="ZoomPercent"/> 0 already draws.
    /// </remarks>
    public bool? FollowWindowSize
    {
        get => _followWindowSize;
        set
        {
            if (_followWindowSize == value) return;

            _followWindowSize = value;

            // Turning it on has to start the size requests, which happens on the next arrange.
            InvalidateMeasure();
            InvalidateVisual();
        }
    }

    private bool? _followWindowSize;

    /// <summary>
    /// Whether the window is actually driving the size right now, profile default included.
    /// </summary>
    /// <returns>
    /// True when a size change should be asked for.
    /// </returns>
    public bool FollowsWindowSize()
    {
        if (_followWindowSize.HasValue) return _followWindowSize.Value;

        // No emulator yet means nothing to resize, and true is the harmless answer - the first
        // arrange after one arrives asks again.
        return _emulator?.Profile.Supports(
            RetroTerm.Core.Terminal.Profiles.TerminalFeatures.HostResize) ?? true;
    }

    /// <summary>
    /// Asks for the terminal to become this many columns and rows, because the control changed
    /// size. Nothing here resizes anything - the session owns that.
    /// </summary>
    /// <remarks>
    /// A control raising a request rather than doing the work is what keeps the business logic out
    /// of the UI layer. It also keeps the canvas testable without a connection.
    /// </remarks>
    public event Action<int, int>? TerminalResizeRequested;

    /// <summary>
    /// The smallest terminal the window is allowed to ask for.
    /// </summary>
    /// <remarks>
    /// A window dragged down to nothing would otherwise ask for a one-column terminal, and every
    /// line of history would be reflowed into a vertical ribbon on the way down and be unrecoverable
    /// on the way back. The floor costs nothing and makes the drag reversible.
    /// </remarks>
    private const int MinimumColumns = 20;

    /// <summary>
    /// See <see cref="MinimumColumns"/>.
    /// </summary>
    private const int MinimumRows = 4;

    /// <summary>
    /// Works out how many whole character cells fit, and asks for that size if it differs from what
    /// the terminal already is.
    /// </summary>
    /// <remarks>
    /// Called from <see cref="ArrangeOverride"/>, which is where the control learns the size it
    /// actually got - measure only says what it asked for.
    /// The division truncates on purpose. A partial column cannot hold a character, and rounding up
    /// would put one off the edge.
    /// </remarks>
    /// <param name="available">
    /// The size the control has been given.
    /// </param>
    private void RequestTerminalSizeFor(Size available)
    {
        if (_renderer == null || _emulator == null) return;

        // Zoomed means the grid is pinned. See ZoomPercent for why the two modes cannot both run.
        if (_zoomPercent != NaturalZoomPercent) return;

        // A terminal whose geometry is part of what it is does not grow with the window. See
        // FollowWindowSize; the default is the profile's own answer.
        if (!FollowsWindowSize()) return;

        double cellWidth = _renderer.GetCharWidth();
        double cellHeight = _renderer.GetCharHeight();
        if (cellWidth <= 0 || cellHeight <= 0) return;

        int columns = (int)(available.Width / cellWidth);
        int rows = (int)(available.Height / cellHeight);

        if (columns < MinimumColumns) columns = MinimumColumns;
        if (rows < MinimumRows) rows = MinimumRows;

        if (columns == _emulator.Width && rows == _emulator.Height) return;

        TerminalResizeRequested?.Invoke(columns, rows);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var arranged = base.ArrangeOverride(finalSize);
        RequestTerminalSizeFor(finalSize);
        return arranged;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        // Claim all available space so the canvas fills its container
        if (_renderer != null && _emulator != null)
        {
            // If given infinite space (shouldn't happen without ScrollViewer, but be safe),
            // fall back to natural size
            if (double.IsInfinity(availableSize.Width) || double.IsInfinity(availableSize.Height))
            {
                var buffer = _emulator.GetBuffer();
                return _renderer.CalculateSize(buffer.Width, buffer.Height);
            }

            return availableSize;
        }

        return base.MeasureOverride(availableSize);
    }

    public event Action<string>? InputReceived;

    private static string ToVisibleSequence(string s)
    {
        var sb = new System.Text.StringBuilder(s.Length * 4);
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            if (c == '\x1b') sb.Append("<ESC>");
            else if (c < 0x20) sb.Append($"<0x{(int)c:X2}>");
            else sb.Append(c);
        }
        return sb.ToString();
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        System.Diagnostics.Debug.WriteLine(
            $"[Keyboard] KeyDown: Key={e.Key} ({(int)e.Key}), Modifiers={e.KeyModifiers}, VK={AvaloniaKeyHelper.ToVKCodeForMapper(e.Key)}");

        // DECARM. A host that has reset private mode 8 is asking for a key held down NOT to repeat,
        // and the only place that can be honoured is here - by the time the mapper sees a keypress
        // it is already a keypress. Avalonia raises KeyDown again and again while a key is held and
        // gives no "this is a repeat" flag, so the held keys are tracked and the second and later
        // arrivals are dropped.
        //
        // Placed above everything, zoom included: while auto-repeat is off, a leaned-on key must
        // produce exactly one of whatever it produces.
        //
        // A host resets this when repeating would do damage - a menu where a held arrow runs off
        // the end of the list, a form where a held key fills a field.
        if (!TrackKeyHeldAndAllowRepeat(e.Key))
        {
            e.Handled = true;
            return;
        }

        // Snap to live view on any keypress while scrolled back
        if (_scrollOffset > 0)
        {
            ScrollToBottom();
        }

        // Mirror key press on virtual keyboard (before any routing)
        FireKeyMirror(e.Key, e.KeyModifiers, true);

        // 0. Zoom, before anything else claims the key.
        //
        // Ctrl+minus and Ctrl+plus are what every other application uses, so they are what a reader
        // will try. Ctrl+0 goes back to fitting the window, which is also how every other
        // application spells "undo my zooming".
        //
        // BOTH SPELLINGS OF EACH KEY are accepted: the main row reports OemPlus and OemMinus while
        // the numeric keypad reports Add and Subtract, and a reader who uses the keypad is not
        // making a mistake. Shift is ignored on the plus, because on most layouts the unshifted key
        // is '=' and typing a literal plus means holding shift.
        if (e.KeyModifiers.HasFlag(Avalonia.Input.KeyModifiers.Control))
        {
            if (e.Key == Key.OemPlus || e.Key == Key.Add)
            {
                StepZoom(1);
                e.Handled = true;
                return;
            }

            if (e.Key == Key.OemMinus || e.Key == Key.Subtract)
            {
                StepZoom(-1);
                e.Handled = true;
                return;
            }

            if (e.Key == Key.D0 || e.Key == Key.NumPad0)
            {
                ZoomPercent = NaturalZoomPercent;
                e.Handled = true;
                return;
            }
        }

        // 0b. ReGIS graphics input mode, before anything else reads the arrow keys.
        //
        // While one-shot mode is running the arrow keys steer the graphics input cursor and must NOT
        // reach the host, and any other key answers the host's position request. Placed here so it
        // beats the mapper, which would otherwise send an arrow key's escape sequence.
        if (HandleRegisGraphicsInputKey(e))
        {
            e.Handled = true;
            return;
        }

        // 1. Handle Ctrl+Shift shortcuts (clipboard, select-all, find)
        //    These must be intercepted before the mapper/C0 fallback to avoid
        //    being consumed as terminal control codes.
        if (e.KeyModifiers.HasFlag(Avalonia.Input.KeyModifiers.Control) && e.KeyModifiers.HasFlag(Avalonia.Input.KeyModifiers.Shift))
        {
            if (e.Key == Key.C)
            {
                HandleCopy();
                e.Handled = true;
                return;
            }
            else if (e.Key == Key.V)
            {
                HandlePaste();
                e.Handled = true;
                return;
            }
            else if (e.Key == Key.A || e.Key == Key.F || e.Key == Key.N)
            {
                // Let these bubble to MainWindow (Select All / Find / Connect)
                return;
            }
        }

        // 2. Escape clears selection (only if there IS a selection)
        if (e.Key == Key.Escape && _selectionManager != null && _selectionManager.HasSelection)
        {
            _selectionManager.ClearSelection();
            InvalidateVisual();
            e.Handled = true;
            return;
        }

        if (_emulator == null || _keyboardMapper == null) return;

        // The connection's "Transmit DEL by" choice, asked BEFORE the mapper because it overrides
        // what the terminal would otherwise send for these two keys. Which of BS and DEL a host
        // wants is a property of its line discipline, not of the terminal type, so the mapper - which
        // only knows the terminal - is the wrong place to decide it.
        //
        // Plain modifiers only: Ctrl+Backspace is a universal control combination the mapper
        // resolves to NUL, and taking it over here would silently break that.
        if (e.KeyModifiers == Avalonia.Input.KeyModifiers.None)
        {
            if (e.Key == Key.Back && EffectiveBackspaceSendsDel)
            {
                InputReceived?.Invoke(DelCharacter);
                e.Handled = true;
                return;
            }
            if (e.Key == Key.Delete && DeleteSendsDel)
            {
                InputReceived?.Invoke(DelCharacter);
                e.Handled = true;
                return;
            }
        }

        // NOTE: Ctrl+Backspace → NUL used to be intercepted here, ahead of the mapper. It is now
        // one of the universal Ctrl combinations the mapper resolves (MapUniversalControl), so it
        // is no longer a special case in the view.

        // 3. Unified mapper check — route any key with modifiers or special keys through the mapper.
        //    This covers: Alt+letter, Alt+number, bare F-keys, Shift+F-keys, Ctrl+letter bindings,
        //    bare Tab/Esc/arrows, and any other configurable key binding.
        if (ShouldRouteToMapper(e.Key, e.KeyModifiers))
        {
            var modifiers = AvaloniaKeyHelper.ConvertModifiers(e.KeyModifiers);
            var terminalModes = GetTerminalModes();
            var keyCode = AvaloniaKeyHelper.ToVKCodeForMapper(e.Key);

            // A key the host loaded with DECUDK sends what the host asked for, not its factory
            // sequence. Asked BEFORE the mapper, because that substitution is the whole point of
            // the feature; keys with no definition fall straight through.
            if (_emulator != null && _emulator.TryGetUserDefinedKey(keyCode, out var defined))
            {
                InputReceived?.Invoke(System.Text.Encoding.ASCII.GetString(defined));
                e.Handled = true;
                return;
            }

            var text = _keyboardMapper.MapKey(keyCode, modifiers, terminalModes);
            if (text != null)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[Keyboard] Mapper → VK={keyCode}, Mod={modifiers}, Modes={terminalModes} → \"{ToVisibleSequence(text)}\"");
                InputReceived?.Invoke(text);
                e.Handled = true;
                return;
            }
            else
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[Keyboard] Mapper → VK={keyCode}, Mod={modifiers}, Modes={terminalModes} → null (no mapping)");
            }
        }

        // NOTE: the Ctrl+letter → C0 and Ctrl+Space → NUL fallbacks used to live here, and in a
        // second copy in VirtualKeyboardWindow. Both are now decided once in the Core mapper
        // (BaseKeyboardMapper.MapKey), so they cannot disagree with each other and the rule is
        // where the rest of the terminal's key encoding is, rather than in a view.

        if (e.KeyModifiers == Avalonia.Input.KeyModifiers.Control && e.Key == Key.OemBackslash)
        {
            InputReceived?.Invoke("\x1c");
            e.Handled = true;
            return;
        }

        if (e.KeyModifiers == Avalonia.Input.KeyModifiers.Control && e.Key == Key.OemCloseBrackets)
        {
            InputReceived?.Invoke("\x1d");
            e.Handled = true;
            return;
        }

        if (e.KeyModifiers.HasFlag(Avalonia.Input.KeyModifiers.Control) && e.Key == Key.D6)
        {
            InputReceived?.Invoke("\x1e");
            e.Handled = true;
            return;
        }

        if (e.KeyModifiers.HasFlag(Avalonia.Input.KeyModifiers.Control) && e.Key == Key.OemMinus)
        {
            InputReceived?.Invoke("\x1f");
            e.Handled = true;
            return;
        }
    }

    private void OnKeyUp(object? sender, KeyEventArgs e)
    {
        _heldKeys.Remove(e.Key);
        _autoRepeatHeldKeys.Remove(e.Key);
        FireKeyMirror(e.Key, e.KeyModifiers, false);
    }

    /// <summary>
    /// Which keys are physically down, for DECARM.
    /// </summary>
    /// <remarks>
    /// SEPARATE from <c>_heldKeys</c> on purpose, even though the two hold nearly the same thing.
    /// <c>_heldKeys</c> is filled by <c>FireKeyMirror</c>, which returns early when nothing is
    /// listening to the virtual keyboard - so with no mirror attached it stays empty, and auto-repeat
    /// suppression built on it would silently do nothing in the ordinary case. Sharing the set would
    /// also mean this check and the mirror's own first-press suppression fighting over who gets to
    /// add the key.
    /// </remarks>
    private readonly HashSet<Key> _autoRepeatHeldKeys = new HashSet<Key>();

    /// <summary>
    /// Records that a key is down, and says whether this press should be acted on.
    /// </summary>
    /// <param name="key">
    /// The key that just went down.
    /// </param>
    /// <returns>
    /// True to act on the press; false when DECARM is off and this is a repeat.
    /// </returns>
    /// <remarks>
    /// The key is recorded either way, so that turning auto-repeat off part way through a held key
    /// still suppresses the rest of that key's repeats rather than waiting for the next press.
    /// </remarks>
    private bool TrackKeyHeldAndAllowRepeat(Key key)
    {
        bool wasAlreadyDown = !_autoRepeatHeldKeys.Add(key);

        var emulator = _emulator;
        if (emulator == null) return true;

        if (!emulator.GetActiveModes().HasFlag(RetroTerm.Core.Terminal.Input.TerminalModes.AutoRepeatDisabled))
        {
            return true;
        }

        return !wasAlreadyDown;
    }

    private void FireKeyMirror(Key key, Avalonia.Input.KeyModifiers avModifiers, bool pressed)
    {
        if (KeyMirrored == null) return;

        // On press: suppress repeats — only fire once per physical key-down
        if (pressed && !_heldKeys.Add(key))
            return;

        var vkCode = AvaloniaKeyHelper.ToVKCode(key);
        var modifiers = AvaloniaKeyHelper.ConvertModifiers(avModifiers);
        KeyMirrored.Invoke(vkCode, modifiers, pressed);
    }

    /// <summary>
    /// Determines whether a key event should be routed through the keyboard mapper.
    /// Returns true for: special keys (F-keys, arrows, etc.), any key with Alt/Ctrl modifier,
    /// and Shift+special keys. Bare modifier keys (Ctrl, Alt, Shift alone) are excluded.
    /// </summary>
    private bool ShouldRouteToMapper(Key key, Avalonia.Input.KeyModifiers modifiers)
    {
        // Never route bare modifier key presses — they have no terminal meaning
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
            or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin)
            return false;

        // Any key with Alt or Ctrl modifier should go through the mapper
        // (handles Alt+letter, Alt+number, Ctrl+letter bindings, etc.)
        if (modifiers.HasFlag(Avalonia.Input.KeyModifiers.Alt))
            return true;
        if (modifiers.HasFlag(Avalonia.Input.KeyModifiers.Control))
            return true;

        // Keypad keys go to the mapper ONLY while application keypad mode is on, because that is
        // the only time they mean something other than the character printed on them. In numeric
        // mode they must keep travelling the ordinary text path — routing them here as well would
        // deliver every keypad digit twice.
        if (AvaloniaKeyHelper.IsKeypadKey(key))
        {
            return GetTerminalModes().HasFlag(TerminalModes.ApplicationKeypad);
        }

        // Special keys always go through the mapper
        return AvaloniaKeyHelper.IsSpecialKey(key);
    }


    /// <summary>
    /// Copies the current selection to the clipboard
    /// </summary>
    public void Copy() => HandleCopy();

    /// <summary>
    /// Pastes text from the clipboard
    /// </summary>
    public void Paste() => HandlePaste();

    /// <summary>
    /// Selects all buffer content
    /// </summary>
    public void SelectAll()
    {
        _selectionManager?.SelectAll();
        InvalidateVisual();
    }

    private void HandleCopy()
    {
        if (_selectionManager == null || _clipboardManager == null) return;

        if (_selectionManager.HasSelection)
        {
            var text = _selectionManager.GetSelectedText();
            _clipboardManager.Copy(text);
        }
    }

    private async void HandlePaste()
    {
        if (_clipboardManager == null || _clipboardService == null) return;

        // Refresh clipboard cache (async)
        await _clipboardService.GetTextAsync();

        // Now paste (will use cached value)
        var pasteText = _clipboardManager.Paste();
        PasteText(pasteText);
    }

    /// <summary>
    /// Pastes a known piece of text, exactly as if it had come off the clipboard.
    /// </summary>
    /// <param name="text">
    /// The text to paste. Null or empty does nothing.
    /// </param>
    /// <remarks>
    /// <para><b>Why the tail of HandlePaste lives here</b></para>
    /// This IS the paste - the national character conversion and the bracketed paste wrapping - and
    /// <see cref="HandlePaste"/> is now only the part that fetches the clipboard. A script or the
    /// MCP <c>PASTE</c> command names the text instead, which makes the case repeatable and leaves
    /// the reader's own clipboard untouched; both then travel this one path, so a fix here cannot
    /// reach one caller and miss the other.
    /// </remarks>
    public void PasteText(string? text)
    {
        if (string.IsNullOrEmpty(text)) return;

        var converted = ApplyIso646Conversion(text!);

        // Bracketed paste, when the host asked for it. The wrapping is the emulator's - it is
        // the only thing that knows the mode, and paste arrives from more than one place.
        InputReceived?.Invoke(_emulator?.WrapForPaste(converted) ?? converted);
    }

    /// <summary>
    /// Presses a key AT THE TERMINAL, through the canvas's real key path.
    /// </summary>
    /// <param name="key">
    /// The key to press.
    /// </param>
    /// <param name="modifiers">
    /// The modifiers held while pressing it.
    /// </param>
    /// <remarks>
    /// <para><b>It raises the real event, and does not copy the routing</b></para>
    /// Everything the canvas decides about a keypress - auto-repeat suppression, the zoom
    /// shortcuts, ReGIS graphics input, and only then the keyboard mapper - is decided in
    /// <c>OnKeyDown</c>. Reproducing any of that here would give a second answer that agrees with
    /// itself and with nothing else, which is the shape of three defects this repository has
    /// already had. So the event is raised and the ordinary handler runs.
    /// <para><b>What it is for</b></para>
    /// The behaviour that never reaches the wire: the ReGIS input cursor, where an arrow moves the
    /// crosshair and any other key answers the host, and the zoom shortcuts.
    /// <para><b>The modifiers are spelled out in full on purpose</b></para>
    /// Core has a <c>KeyModifiers</c> of its own, so the bare name is ambiguous in this file - the
    /// same trap as <c>MouseButton</c> at the top. These are the WINDOW's modifiers, so they are
    /// Avalonia's.
    /// </remarks>
    public void DeliverLocalKey(Key key, Avalonia.Input.KeyModifiers modifiers)
    {
        RaiseEvent(new KeyEventArgs
        {
            RoutedEvent = InputElement.KeyDownEvent,
            Key = key,
            KeyModifiers = modifiers,
            Source = this
        });
    }

    /// <summary>
    /// Types one piece of text AT THE TERMINAL, through the canvas's real text path.
    /// </summary>
    /// <param name="text">
    /// The text produced by the keyboard, as the layout would produce it.
    /// </param>
    /// <remarks>
    /// A printable key reaches the terminal as text, not as a key code, because only the keyboard
    /// layout knows what a key produces. ReGIS graphics input needs exactly that: a position report
    /// carries the character the operator pressed, so it is answered in <c>OnTextInput</c> and not
    /// in the key handler.
    /// </remarks>
    public void DeliverLocalText(string text)
    {
        if (string.IsNullOrEmpty(text)) return;

        RaiseEvent(new TextInputEventArgs
        {
            RoutedEvent = InputElement.TextInputEvent,
            Text = text,
            Source = this
        });
    }

    /// <summary>
    /// Converts typed text to the bytes the terminal expects under its active national variant.
    /// The conversion itself lives in Core (TDVCharacterSets.ConvertToWireBytes); this only
    /// supplies the variant, which is the emulator's.
    /// </summary>
    /// <remarks>
    /// A character with no mapping passes through unchanged, and so does all input when the variant
    /// is International.
    /// </remarks>
    private string ApplyIso646Conversion(string input)
    {
        if (_emulator is not Core.Terminal.Emulators.TDV.TDVEmulatorBase tdv)
            return input;

        return Core.Terminal.Emulators.TDV.TDVCharacterSets
            .ConvertToWireBytes(input, tdv.GetISO646LanguageCode());
    }


    /// <summary>
    /// The terminal modes that affect key encoding.
    ///
    /// This used to be a chain of emulator type checks, duplicated here and in MainWindow. Two
    /// problems: it was business logic sitting in a view, and because it only ever produced the
    /// TDV flags, DECCKM and DECKPAM never reached the mapper at all — the mapper's branches for
    /// them could not run. The emulator owns the mode state, so it answers the question now.
    /// </summary>
    private TerminalModes GetTerminalModes()
    {
        return _emulator?.GetActiveModes() ?? TerminalModes.None;
    }

    /// <summary>
    /// Gives a keypress to ReGIS graphics input mode, when one is running.
    /// </summary>
    /// <param name="e">
    /// The key that was pressed.
    /// </param>
    /// <returns>
    /// True when graphics input took the key and nothing else should see it.
    /// </returns>
    /// <remarks>
    /// <para><b>Only one-shot mode takes keys</b></para>
    /// Chapter 15: "You cannot use the four arrow keys to position the input cursor as you can in
    /// ReGIS one-shot graphics input mode. If you press an arrow key in multiple mode, the terminal
    /// sends that key's escape sequence to the host." So in multiple mode this answers false to
    /// everything and every key travels its ordinary path.
    ///
    /// <para><b>Escape is ours</b></para>
    /// A real VT340 has no way out of one-shot mode except answering the request. Here a suspended
    /// session looks dead, so Escape cancels. Decided with Ronny on 2026-08-20.
    /// </remarks>
    private bool HandleRegisGraphicsInputKey(KeyEventArgs e)
    {
        if (_emulator == null) return false;
        if (_emulator.RegisInputMode != RetroTerm.Core.Terminal.Graphics.RegisGraphicsInputMode.OneShot)
            return false;

        // A modifier held on its own is not a keystroke. Reporting on it would answer the host the
        // instant the operator reached for shift.
        if (e.Key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
            or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin)
        {
            return true;
        }

        if (e.Key == Key.Escape)
        {
            _emulator.CancelRegisInput();
            return true;
        }

        // "arrow key - The cursor moves one pixel in the direction of the arrow... Shift-arrow key -
        // The cursor moves 10 pixels in the direction of the arrow."
        int step = e.KeyModifiers.HasFlag(Avalonia.Input.KeyModifiers.Shift)
            ? RetroTerm.Core.Terminal.Graphics.RegisDecoder.InputCursorShiftStep
            : RetroTerm.Core.Terminal.Graphics.RegisDecoder.InputCursorStep;

        switch (e.Key)
        {
            case Key.Left: _emulator.MoveRegisInputCursor(-step, 0); return true;
            case Key.Right: _emulator.MoveRegisInputCursor(step, 0); return true;
            case Key.Up: _emulator.MoveRegisInputCursor(0, -step); return true;
            case Key.Down: _emulator.MoveRegisInputCursor(0, step); return true;
        }

        // "The terminal sends a position report when you press any non-arrow key that is not dead."
        // Return is taken here rather than left to the text path, because OnTextInput deliberately
        // drops carriage returns to avoid sending Enter twice.
        if (e.Key == Key.Enter)
        {
            _emulator.SendRegisInputReport("\r");
            return true;
        }

        // Everything else is a printable character, and the character itself is what the report has
        // to carry. Only OnTextInput knows what the layout produced, so the key is swallowed here
        // and answered there.
        return false;
    }

    /// <summary>
    /// Gives a pointer press to ReGIS graphics input mode, when one is running, so a click places
    /// the input cursor.
    /// </summary>
    /// <param name="point">
    /// Where the pointer was pressed, in this control's coordinates.
    /// </param>
    /// <returns>
    /// True when graphics input took the click and nothing else should see it.
    /// </returns>
    /// <remarks>
    /// <para><b>Why the mouse is allowed here when it is not allowed elsewhere</b></para>
    /// The keyboard-only rule of 2026-08-20 existed because the ReGIS manual describes only the
    /// arrow keys and nothing said who wins when a program is ALSO asking for xterm mouse reports.
    /// That cannot arise in this window: one-shot graphics input SUSPENDS the host, so no program is
    /// listening for mouse reports while the cursor is up, and no new text is arriving to select.
    /// Ronny overturned the rule on 31 August 2026 - a click places, the arrows still refine.
    ///
    /// <para><b>The transform, and why it rounds</b></para>
    /// The renderer stretches the whole graphics composite across the whole terminal surface
    /// (TerminalRenderer.DrawGraphicsPlanes blits source rect to destination rect), so the inverse
    /// is a plain ratio. Undo the canvas offset and zoom first, exactly as PixelToCell does, then
    /// scale into the plane. One screen pixel is not one ReGIS pixel except by accident, so this can
    /// only land on the nearest plane pixel - which is precisely why the arrow keys keep their job.
    ///
    /// <para><b>Only the left button</b></para>
    /// Middle still pans a magnified picture and right still copies, so neither gesture is lost
    /// while the cursor is up.
    /// </remarks>
    private bool HandleRegisGraphicsInputPointer(Point point)
    {
        if (_emulator == null || _renderer == null) return false;
        if (_emulator.RegisInputMode != RetroTerm.Core.Terminal.Graphics.RegisGraphicsInputMode.OneShot)
            return false;

        var graphics = _emulator.Graphics;
        if (graphics == null) return false;

        var composite = graphics.Output;
        if (composite == null || composite.Width <= 0 || composite.Height <= 0) return false;

        var buffer = _emulator.GetBuffer();
        double surfaceWidth = buffer.Width * _renderer.GetCharWidth();
        double surfaceHeight = buffer.Height * _renderer.GetCharHeight();
        if (surfaceWidth <= 0 || surfaceHeight <= 0) return false;

        // Reverse the scale transform, the same way PixelToCell does.
        double x = (point.X - _offsetX) / _scale;
        double y = (point.Y - _offsetY) / _scale;

        int graphicsX = (int)(x / surfaceWidth * composite.Width);
        int graphicsY = (int)(y / surfaceHeight * composite.Height);

        // SetRegisInputCursor clamps, so a click in the letterbox margin pins to the nearest edge
        // rather than being thrown away or wrapping to the far side.
        return _emulator.SetRegisInputCursor(graphicsX, graphicsY);
    }

    /// <summary>
    /// Passes the emulator's graphics input mode change on to the window.
    /// </summary>
    /// <param name="mode">
    /// The mode now running.
    /// </param>
    private void OnRegisInputModeChanged(RetroTerm.Core.Terminal.Graphics.RegisGraphicsInputMode mode)
    {
        RegisGraphicsInputChanged?.Invoke(mode);
        InvalidateVisual();
    }

    private void OnTextInput(object? sender, TextInputEventArgs e)
    {
        // A keystroke in ReGIS one-shot graphics input mode answers the host's position request and
        // goes no further. "Locator reports begin with the code(s) of the active non-arrow key or
        // locator button pressed" - so the character travels inside the report rather than beside it.
        if (_emulator != null && !string.IsNullOrEmpty(e.Text)
            && _emulator.RegisInputMode == RetroTerm.Core.Terminal.Graphics.RegisGraphicsInputMode.OneShot)
        {
            _emulator.SendRegisInputReport(e.Text);
            e.Handled = true;
            return;
        }

        if (!string.IsNullOrEmpty(e.Text))
        {
            // Avoid duplicate Enter when mapper already handled VK_RETURN
            if (e.Text == "\r" || e.Text == "\n")
            {
                return;
            }

            // ISO 646 conversion: national characters (ÆØÅæøå etc.) → ASCII wire bytes
            // The TDV emulator renders bytes through the active NRC, so we must send
            // the ASCII position byte (e.g. ø → '|', Æ → '[', Å → ']')
            InputReceived?.Invoke(ApplyIso646Conversion(e.Text));
            e.Handled = true;
        }
    }
}

