using System;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using RetroTerm.Core.Terminal.Emulators;
using RetroTerm.Core.Terminal.Input;

namespace RetroTerm.Desktop.Controls;

public partial class TerminalControl : UserControl
{
    private TerminalCanvas? _canvas;

    public TerminalControl()
    {
        InitializeComponent();

        // Find the canvas control
        _canvas = this.FindControl<TerminalCanvas>("TerminalCanvas");

        // Wire up input events from canvas
        if (_canvas != null)
        {
            _canvas.InputReceived += OnCanvasInputReceived;
            _canvas.KeyMirrored += OnCanvasKeyMirrored;
            _canvas.ScrollOffsetChanged += offset => ScrollOffsetChanged?.Invoke(offset);
            _canvas.TerminalResizeRequested += (columns, rows) =>
                TerminalResizeRequested?.Invoke(columns, rows);
            _canvas.ZoomChanged += percent => ZoomChanged?.Invoke(percent);
            _canvas.RegisGraphicsInputChanged += mode => RegisGraphicsInputChanged?.Invoke(mode);
        }
    }

    /// <summary>
    /// Raised when ReGIS graphics input mode starts or stops on this terminal.
    /// </summary>
    /// <remarks>
    /// Forwarded from the canvas so the window can put a note in the status line. One-shot mode stops
    /// the screen changing, and an unexplained pause reads as a dead session.
    /// </remarks>
    public event Action<RetroTerm.Core.Terminal.Graphics.RegisGraphicsInputMode>? RegisGraphicsInputChanged;

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    /// <summary>
    /// Fired when the scroll offset changes. Parameter is the new offset (0 = live).
    /// </summary>
    public event Action<int>? ScrollOffsetChanged;

    /// <summary>
    /// The canvas has been given a new size and this many whole character cells now fit.
    /// </summary>
    /// <remarks>
    /// Forwarded from the canvas untouched. Nothing in the UI layer acts on it - the window hands
    /// it to the session, which owns both the emulator and the connection.
    /// </remarks>
    public event Action<int, int>? TerminalResizeRequested;

    /// <summary>
    /// The display zoom changed, as a percentage, or 0 for "fit the window".
    /// </summary>
    public event Action<int>? ZoomChanged;

    /// <summary>
    /// The display zoom as a percentage, or 0 for "fit the window".
    /// </summary>
    /// <remarks>
    /// Forwarded to the canvas, which is where the two modes are explained.
    /// </remarks>
    public int ZoomPercent
    {
        get => _canvas?.ZoomPercent ?? 0;
        set { if (_canvas != null) _canvas.ZoomPercent = value; }
    }

    /// <summary>
    /// Whether the Backspace key sends DEL (0x7F) instead of BS (0x08).
    /// </summary>
    /// <remarks>
    /// Forwarded to the canvas, which is where the key is read. Set from the connection's
    /// Keyboard tab.
    /// </remarks>
    public bool? BackspaceSendsDel
    {
        get => _canvas?.BackspaceSendsDel;
        set { if (_canvas != null) _canvas.BackspaceSendsDel = value; }
    }

    /// <summary>
    /// What Backspace will actually send: the connection's choice, or the terminal's default.
    /// See TerminalCanvas.EffectiveBackspaceSendsDel.
    /// </summary>
    public bool EffectiveBackspaceSendsDel => _canvas?.EffectiveBackspaceSendsDel ?? false;

    /// <summary>
    /// Whether the Delete key sends DEL (0x7F) instead of this terminal's own sequence.
    /// </summary>
    public bool DeleteSendsDel
    {
        get => _canvas?.DeleteSendsDel ?? false;
        set { if (_canvas != null) _canvas.DeleteSendsDel = value; }
    }

    /// <summary>
    /// Moves one step along the shared zoom ladder.
    /// </summary>
    /// <param name="direction">
    /// Positive to enlarge, negative to shrink.
    /// </param>
    public void StepZoom(int direction) => _canvas?.StepZoom(direction);

    /// <summary>
    /// Whether the window decides how many columns and rows there are, or null to let the
    /// terminal's own profile decide.
    /// </summary>
    /// <remarks>
    /// Forwarded to the canvas, where the profile default is explained.
    /// </remarks>
    public bool? FollowWindowSize
    {
        get => _canvas?.FollowWindowSize;
        set { if (_canvas != null) _canvas.FollowWindowSize = value; }
    }

    /// <summary>
    /// Whether the window is actually driving the size right now, profile default included.
    /// </summary>
    /// <returns>
    /// True when the terminal grows with the window.
    /// </returns>
    public bool FollowsWindowSize() => _canvas?.FollowsWindowSize() ?? true;

    /// <summary>
    /// Whether the viewport is scrolled back into history
    /// </summary>
    public bool IsScrolledBack => _canvas?.IsScrolledBack ?? false;

    /// <summary>
    /// Snaps the viewport back to the live buffer
    /// </summary>
    public void ScrollToBottom() => _canvas?.ScrollToBottom();

    /// <summary>
    /// Brings a search match into view. See <see cref="TerminalCanvas.ScrollToSearchRow"/> for what
    /// the row number means - it is the SEARCH's numbering, where a negative row is history.
    /// </summary>
    /// <param name="searchRow">
    /// The match's row: 0 is the top of the live screen, minus one the line just above it.
    /// </param>
    public void ScrollToSearchRow(int searchRow) => _canvas?.ScrollToSearchRow(searchRow);

    public void SetEmulator(TerminalEmulatorBase emulator)
    {
        _canvas?.SetEmulator(emulator);
    }

    // No `new`: the base Control.Focus() overload set differs, so this does not hide it (CS0109).
    public void Focus()
    {
        _canvas?.Focus();
    }

    public void SetSearchMatches(List<RetroTerm.Core.Search.SearchMatch>? matches, int currentMatchIndex = -1)
    {
        _canvas?.SetSearchMatches(matches, currentMatchIndex);
    }

    /// <summary>
    /// Get the keyboard mapper for the current terminal type
    /// </summary>
    public IKeyboardMapper? GetKeyboardMapper()
    {
        return _canvas?.GetKeyboardMapper();
    }

    /// <summary>
    /// Copies the current selection to the clipboard
    /// </summary>
    public void Copy() => _canvas?.Copy();

    /// <summary>
    /// Pastes text from the clipboard
    /// </summary>
    public void Paste() => _canvas?.Paste();

    /// <summary>
    /// Pastes a known piece of text, leaving the system clipboard alone.
    /// </summary>
    /// <param name="text">
    /// The text to paste.
    /// </param>
    /// <remarks>
    /// See <see cref="TerminalCanvas.PasteText"/> - it takes the same route the clipboard does, so
    /// national characters and bracketed paste are applied either way.
    /// </remarks>
    public void PasteText(string? text) => _canvas?.PasteText(text);

    /// <summary>
    /// Presses a key at the terminal rather than sending it to the host.
    /// </summary>
    /// <param name="key">
    /// The key to press.
    /// </param>
    /// <param name="modifiers">
    /// The modifiers held while pressing it.
    /// </param>
    public void DeliverLocalKey(Avalonia.Input.Key key, Avalonia.Input.KeyModifiers modifiers)
        => _canvas?.DeliverLocalKey(key, modifiers);

    /// <summary>
    /// Types text at the terminal rather than sending it to the host.
    /// </summary>
    /// <param name="text">
    /// The text the keyboard produced.
    /// </param>
    public void DeliverLocalText(string text) => _canvas?.DeliverLocalText(text);

    /// <summary>
    /// Selects all buffer content
    /// </summary>
    public void SelectAll() => _canvas?.SelectAll();

    /// <summary>
    /// Replaces the default terminal colors and redraws.
    /// </summary>
    public void SetDefaultColors(Avalonia.Media.Color fg, Avalonia.Media.Color bg) => _canvas?.SetDefaultColors(fg, bg);

    /// <summary>
    /// Applies a presentation theme to this terminal's screen.
    /// </summary>
    public void SetTheme(RetroTerm.Core.Terminal.Buffer.TerminalTheme theme) => _canvas?.SetTheme(theme);

    /// <summary>
    /// Gets the current default foreground color, or null if no renderer.
    /// </summary>
    public Avalonia.Media.Color? GetDefaultForeground() => _canvas?.GetDefaultForeground();

    /// <summary>
    /// Gets the current default background color, or null if no renderer.
    /// </summary>
    public Avalonia.Media.Color? GetDefaultBackground() => _canvas?.GetDefaultBackground();

    public event Action<string>? InputReceived;

    /// <summary>
    /// Fired on every physical key down/up so the virtual keyboard can mirror the visual state.
    /// Parameters: (vkCode, modifiers, pressed)
    /// </summary>
    public event Action<int, KeyModifiers, bool>? KeyMirrored;

    private void OnCanvasInputReceived(string input)
    {
        // Forward input from canvas to session
        InputReceived?.Invoke(input);
    }

    private void OnCanvasKeyMirrored(int vkCode, KeyModifiers modifiers, bool pressed)
    {
        KeyMirrored?.Invoke(vkCode, modifiers, pressed);
    }
}
