using System;
using System.Collections.Generic;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using RetroTerm.Core.Commands;
using RetroTerm.Core.Scripting;

namespace RetroTerm.Desktop.Views;

/// <summary>
/// The script editor (IDE-style, "Option B" of the UX review):
///
///   ┌ toolbar: New/Rename/Delete │ Save/Run/Stop ──────────────┐
///   ├ SCRIPTS │ gutter+editor                      │ COMMANDS  ┤
///   │         ├ PROBLEMS / TRANSCRIPT (collapsible)│ help      │
///   ├ status strip: state · steps · line/col · run target ─────┤
///
/// Everything is themed through the app's DynamicResource brushes (bound with
/// GetResourceObservable so live theme switches restyle the window), validation and
/// help come from Core (parser + registry), and unsaved changes are tracked — no
/// silent data loss when switching scripts.
/// </summary>
public sealed class ScriptEditorWindow : Window
{
    private static readonly FontFamily MonoFont = new("Cascadia Mono,Consolas,monospace");

    private readonly ScriptLibrary _library;
    private readonly CommandRegistry _registry;
    private readonly ScriptParser _parser;
    private readonly ScriptRunner _runner;
    private readonly SessionTargetSelector _target;

    // ── controls ─────────────────────────────────────────────
    private readonly ListBox _scriptList;
    private readonly TextBox _editor;
    private readonly TextBlock _gutter;
    private readonly ScrollViewer _gutterScroll;
    // Syntax highlighting overlay: the TextBox's own text is TRANSPARENT (caret and
    // selection stay visible); this TextBlock sits on top (hit-test invisible) and
    // renders the same text as colored Runs, scroll-synced to the editor.
    private readonly TextBlock _highlight;
    private readonly ScrollViewer _highlightScroll;
    private readonly ListBox _problemList;
    private readonly Border _problemPanel;
    private readonly TextBox _transcript;
    private readonly Border _transcriptPanel;
    private readonly ListBox _verbList;
    private readonly TextBox _helpText;
    private readonly TextBlock _statusState;
    private readonly TextBlock _statusInfo;
    private readonly TextBlock _statusTarget;
    private readonly Button _runButton;
    private readonly Button _stopButton;

    private readonly DispatcherTimer _validateTimer;
    private string? _currentName;
    private bool _suppressTextEvents;
    private bool _dirty;
    // The text as loaded/saved. Dirty = current text differs from THIS — never trust
    // TextChanged event ordering for dirty state (the event can arrive after the
    // suppress flag is cleared, which made every freshly loaded script "dirty" and
    // the editor nag about saving unchanged scripts, 2026-08-05).
    private string _cleanText = string.Empty;
    private CancellationTokenSource? _runCts;
    private ParsedScript? _lastParse;

    // Cached syntax colors — updated by resource observers so a live theme switch
    // re-colors the highlight (regenerating runs is cheap, scripts are small).
    private IBrush? _brushKeyword;   // CodeKeywordBrush — known verbs
    private IBrush? _brushString;    // CodeStringBrush  — "quoted strings"
    private IBrush? _brushComment;   // DisabledTextBrush — # comments
    private IBrush? _brushParam;     // SecondaryTextBrush — key= argument names
    private IBrush? _brushVariable;  // AccentBrush — $var / ${var}
    private IBrush? _brushPlain;     // PrimaryTextBrush — everything else

    // Registry command names + language keywords, for keyword coloring.
    private readonly HashSet<string> _knownVerbs = new(StringComparer.OrdinalIgnoreCase);

    private const string ControlFlowMarker = "── language ──";

    public ScriptEditorWindow(ScriptLibrary library, CommandRegistry registry,
        Func<RetroTerm.Core.Session.TerminalSession?>? activeSession = null,
        Func<IReadOnlyList<RetroTerm.Core.Session.TerminalSession>>? openSessions = null)
    {
        _library = library ?? throw new ArgumentNullException(nameof(library));
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _parser = new ScriptParser(registry);
        _runner = new ScriptRunner(registry);

        // "Run on" picker: follow the active tab (default) or pin a specific one.
        _target = new SessionTargetSelector(
            activeSession ?? (() => null),
            openSessions ?? (() => Array.Empty<RetroTerm.Core.Session.TerminalSession>()));
        _target.TargetChanged += UpdateTargetStatus;

        // Keyword set for the highlighter: every registered command + the language level.
        var registeredCommands = registry.Commands;
        for (int i = 0; i < registeredCommands.Count; i++)
        {
            _knownVerbs.Add(registeredCommands[i].Name);
        }
        _knownVerbs.Add("LABEL");
        _knownVerbs.Add("GOTO");
        _knownVerbs.Add("GOSUB");
        _knownVerbs.Add("RETURN");
        _knownVerbs.Add("SET");
        _knownVerbs.Add("IF");
        _knownVerbs.Add("ELSE");
        _knownVerbs.Add("ENDIF");

        Title = "Script Editor";
        Width = 1080;
        Height = 700;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        BindTheme(this, BackgroundProperty, "WindowBackgroundBrush");

        _validateTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        _validateTimer.Tick += (_, _) => { _validateTimer.Stop(); Validate(); };

        // ── toolbar ──────────────────────────────────────────
        var newButton = ToolButton("New", primary: false);
        newButton.Click += async (_, _) => await NewScriptAsync();
        var renameButton = ToolButton("Rename", primary: false);
        renameButton.Click += async (_, _) => await RenameScriptAsync();
        var deleteButton = ToolButton("Delete", primary: false);
        deleteButton.Click += async (_, _) => await DeleteSelectedAsync();

        var saveButton = ToolButton("Save", primary: true);
        saveButton.Click += (_, _) => SaveCurrent();
        _runButton = ToolButton("Run ▶", primary: true);
        _runButton.Click += async (_, _) => await RunCurrentAsync();
        _stopButton = ToolButton("Stop ■", primary: false);
        _stopButton.IsEnabled = false;
        _stopButton.Click += (_, _) => _runCts?.Cancel();

        var toolbar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            Margin = new Thickness(10, 8, 10, 8)
        };
        toolbar.Children.Add(newButton);
        toolbar.Children.Add(renameButton);
        toolbar.Children.Add(deleteButton);
        toolbar.Children.Add(ToolbarSeparator());
        toolbar.Children.Add(saveButton);
        toolbar.Children.Add(_runButton);
        toolbar.Children.Add(_stopButton);
        toolbar.Children.Add(ToolbarSeparator());
        var runOnLabel = new TextBlock
        {
            Text = "Run on:",
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(2, 0, 6, 0)
        };
        BindTheme(runOnLabel, TextBlock.ForegroundProperty, "SecondaryTextBrush");
        toolbar.Children.Add(runOnLabel);
        toolbar.Children.Add(_target);

        var toolbarBorder = new Border { Child = toolbar };
        BindTheme(toolbarBorder, Border.BackgroundProperty, "PanelBackgroundBrush");
        BindTheme(toolbarBorder, Border.BorderBrushProperty, "BorderBrush");
        toolbarBorder.BorderThickness = new Thickness(0, 0, 0, 1);

        // ── left: script library ─────────────────────────────
        _scriptList = new ListBox();
        BindTheme(_scriptList, BackgroundProperty, "PanelBackgroundBrush");
        _scriptList.SelectionChanged += async (_, _) => await LoadSelectedAsync();

        var leftPanel = SidePanel("SCRIPTS", _scriptList);

        // ── center: gutter + editor ──────────────────────────
        _gutter = new TextBlock
        {
            FontFamily = MonoFont,
            FontSize = 13,
            TextAlignment = TextAlignment.Right,
            Padding = new Thickness(8, 2, 6, 2)
        };
        BindTheme(_gutter, TextBlock.ForegroundProperty, "DisabledTextBrush");
        _gutterScroll = new ScrollViewer
        {
            Content = _gutter,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
            Width = 44
        };

        _editor = new TextBox
        {
            AcceptsReturn = true,
            AcceptsTab = false,
            TextWrapping = TextWrapping.NoWrap,
            FontFamily = MonoFont,
            FontSize = 13,
            IsEnabled = false,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(6, 2, 2, 2)
        };
        // Text areas use the theme's TEXT BOX background (the dark editor look, the
        // color a focused text box has) — in EVERY state, focus or not.
        BindTheme(_editor, BackgroundProperty, "TextBoxBackgroundBrush");
        TextBoxChrome.Freeze(_editor, this, "TextBoxBackgroundBrush");
        // The TextBox's own glyphs are transparent — the colored overlay renders the
        // text instead. Local values beat the theme's pseudo-class styles, so hover /
        // focus can't flip the foreground back. Caret and selection remain the TextBox's.
        _editor.Foreground = Brushes.Transparent;
        BindTheme(_editor, TextBox.CaretBrushProperty, "PrimaryTextBrush");
        _editor.TextChanged += (_, _) =>
        {
            if (_suppressTextEvents) return;
            // Compare against the clean snapshot: typing back to the original also
            // clears the dirty flag, and a load that raises a late TextChanged does
            // not mark an unchanged script dirty.
            SetDirty(!string.Equals(_editor.Text ?? string.Empty, _cleanText, StringComparison.Ordinal));
            _validateTimer.Stop();
            _validateTimer.Start();
            UpdateGutter();
            // Highlighting regenerates on EVERY keystroke, not on the 300 ms validate
            // debounce — with a transparent TextBox foreground, a delayed overlay would
            // mean invisible characters while typing.
            UpdateHighlight();
        };
        _editor.PropertyChanged += (_, e) =>
        {
            if (e.Property == TextBox.CaretIndexProperty)
            {
                UpdateCaretStatus();
            }
        };
        _editor.AttachedToVisualTree += (_, _) => HookEditorScroll();

        var gutterBorder = new Border { Child = _gutterScroll, BorderThickness = new Thickness(0, 0, 1, 0) };
        BindTheme(gutterBorder, Border.BorderBrushProperty, "BorderBrush");
        BindTheme(gutterBorder, Border.BackgroundProperty, "PanelBackgroundBrush");

        // The overlay: same font/size/padding as the TextBox so glyphs line up exactly,
        // hit-test invisible so all mouse work (caret, selection) goes to the TextBox.
        _highlight = new TextBlock
        {
            FontFamily = MonoFont,
            FontSize = 13,
            TextWrapping = TextWrapping.NoWrap,
            Padding = new Thickness(6, 2, 2, 2),
            IsHitTestVisible = false
        };
        _highlightScroll = new ScrollViewer
        {
            Content = _highlight,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden,
            VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
            IsHitTestVisible = false
        };

        // Layering: TextBox at the bottom (background, selection, caret, input),
        // colored text on top. The overlay glyphs cover only their own pixels, so the
        // selection rectangle below stays visible around them.
        var editorLayers = new Panel();
        editorLayers.Children.Add(_editor);
        editorLayers.Children.Add(_highlightScroll);

        var editorRow = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        Grid.SetColumn(gutterBorder, 0);
        Grid.SetColumn(editorLayers, 1);
        editorRow.Children.Add(gutterBorder);
        editorRow.Children.Add(editorLayers);

        // Cached-brush observers: fire immediately with the current theme value and
        // again on every theme switch — each change re-colors the overlay.
        HookSyntaxBrush("CodeKeywordBrush", b => _brushKeyword = b);
        HookSyntaxBrush("CodeStringBrush", b => _brushString = b);
        HookSyntaxBrush("DisabledTextBrush", b => _brushComment = b);
        HookSyntaxBrush("SecondaryTextBrush", b => _brushParam = b);
        HookSyntaxBrush("AccentBrush", b => _brushVariable = b);
        HookSyntaxBrush("PrimaryTextBrush", b => _brushPlain = b);

        // ── bottom: problems + transcript (each collapsible) ─
        _problemList = new ListBox { MaxHeight = 130 };
        BindTheme(_problemList, BackgroundProperty, "ErrorPanelBackgroundBrush");
        BindTheme(_problemList, ForegroundProperty, "ErrorPanelTextBrush");
        _problemList.SelectionChanged += (_, _) => JumpToSelectedError();
        _problemPanel = CaptionedPanel("PROBLEMS", _problemList, "ErrorPanelBorderBrush");
        _problemPanel.IsVisible = false;

        _transcript = new TextBox
        {
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            FontFamily = MonoFont,
            FontSize = 12,
            MaxHeight = 170,
            BorderThickness = new Thickness(0)
        };
        // Transcript stays on the PANEL color — only the script text and the help
        // panel use the dark text-box background (Ronny 2026-08-05).
        BindTheme(_transcript, BackgroundProperty, "PanelBackgroundBrush");
        BindTheme(_transcript, ForegroundProperty, "PrimaryTextBrush");
        TextBoxChrome.Freeze(_transcript, this, "PanelBackgroundBrush");
        _transcriptPanel = CaptionedPanel("RUN TRANSCRIPT", _transcript, "BorderBrush");
        _transcriptPanel.IsVisible = false;

        var centerPanel = new DockPanel();
        DockPanel.SetDock(_problemPanel, Dock.Bottom);
        DockPanel.SetDock(_transcriptPanel, Dock.Bottom);
        centerPanel.Children.Add(_problemPanel);
        centerPanel.Children.Add(_transcriptPanel);
        centerPanel.Children.Add(editorRow);

        // ── right: commands + generated help ─────────────────
        // List fills the top, help fills the bottom, a GridSplitter between them —
        // each area has its own caption so the split is obvious at a glance.
        _verbList = new ListBox();
        BindTheme(_verbList, BackgroundProperty, "SectionBackgroundBrush");
        var commands = _registry.Commands;
        for (int i = 0; i < commands.Count; i++)
        {
            _verbList.Items.Add(commands[i].Name);
        }
        _verbList.Items.Add(ControlFlowMarker);
        // Language-level entries come from Core's ScriptLanguageHelp — the SAME table
        // the HELP command and terminal_help render, so console and editor never differ.
        var language = ScriptLanguageHelp.Entries;
        for (int i = 0; i < language.Count; i++)
        {
            _verbList.Items.Add(language[i].Verb);
        }
        _verbList.SelectionChanged += (_, _) => ShowHelpForSelectedVerb();
        _verbList.DoubleTapped += (_, _) => InsertExampleForSelectedVerb();

        _helpText = new TextBox
        {
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            FontFamily = MonoFont,
            FontSize = 12,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(8, 6)
        };
        BindTheme(_helpText, BackgroundProperty, "TextBoxBackgroundBrush");
        BindTheme(_helpText, ForegroundProperty, "PrimaryTextBrush");
        // No color flip when the help text is clicked/focused (user rule: keep the
        // colors, don't change on select/unselect).
        TextBoxChrome.Freeze(_helpText, this, "TextBoxBackgroundBrush");

        var helpHeader = new TextBlock
        {
            Text = "HELP",
            FontSize = 11,
            FontWeight = FontWeight.SemiBold,
            Margin = new Thickness(2, 8, 0, 6)
        };
        BindTheme(helpHeader, TextBlock.ForegroundProperty, "SectionHeader1Brush");

        var helpHint = new TextBlock
        {
            Text = "Double-click a command to insert its example",
            FontSize = 11,
            Margin = new Thickness(0, 6, 0, 0)
        };
        BindTheme(helpHint, TextBlock.ForegroundProperty, "DisabledTextBrush");

        // Each area gets a themed border so the two sections read as distinct boxes.
        var listBorder = new Border { Child = _verbList, BorderThickness = new Thickness(1) };
        BindTheme(listBorder, Border.BorderBrushProperty, "BorderBrush");
        BindTheme(listBorder, Border.BackgroundProperty, "SectionBackgroundBrush");

        var helpBorder = new Border { Child = _helpText, BorderThickness = new Thickness(1) };
        BindTheme(helpBorder, Border.BorderBrushProperty, "BorderBrush");
        BindTheme(helpBorder, Border.BackgroundProperty, "TextBoxBackgroundBrush");

        var rightGrid = new Grid { RowDefinitions = new RowDefinitions("*,4,Auto,*,Auto") };
        var rightSplitter = new GridSplitter { Height = 4, HorizontalAlignment = HorizontalAlignment.Stretch };
        BindTheme(rightSplitter, BackgroundProperty, "BorderBrush");
        Grid.SetRow(listBorder, 0);
        Grid.SetRow(rightSplitter, 1);
        Grid.SetRow(helpHeader, 2);
        Grid.SetRow(helpBorder, 3);
        Grid.SetRow(helpHint, 4);
        rightGrid.Children.Add(listBorder);
        rightGrid.Children.Add(rightSplitter);
        rightGrid.Children.Add(helpHeader);
        rightGrid.Children.Add(helpBorder);
        rightGrid.Children.Add(helpHint);
        var rightPanel = SidePanel("COMMANDS", rightGrid);

        // ── status strip ─────────────────────────────────────
        _statusState = new TextBlock { FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
        BindTheme(_statusState, TextBlock.ForegroundProperty, "SecondaryTextBrush");
        _statusInfo = new TextBlock { FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(16, 0, 0, 0) };
        BindTheme(_statusInfo, TextBlock.ForegroundProperty, "SecondaryTextBrush");
        _statusTarget = new TextBlock { FontSize = 12, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right };
        BindTheme(_statusTarget, TextBlock.ForegroundProperty, "SecondaryTextBrush");

        var statusGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*"), Margin = new Thickness(10, 4, 10, 4) };
        Grid.SetColumn(_statusState, 0);
        Grid.SetColumn(_statusInfo, 1);
        Grid.SetColumn(_statusTarget, 2);
        statusGrid.Children.Add(_statusState);
        statusGrid.Children.Add(_statusInfo);
        statusGrid.Children.Add(_statusTarget);

        var statusBorder = new Border { Child = statusGrid, BorderThickness = new Thickness(0, 1, 0, 0) };
        BindTheme(statusBorder, Border.BackgroundProperty, "PanelBackgroundBrush");
        BindTheme(statusBorder, Border.BorderBrushProperty, "BorderBrush");

        // ── layout ───────────────────────────────────────────
        var columns = new Grid { ColumnDefinitions = new ColumnDefinitions("200,4,*,4,300") };
        var mainSplitterL = new GridSplitter { Width = 4 };
        var mainSplitterR = new GridSplitter { Width = 4 };
        Grid.SetColumn(leftPanel, 0);
        Grid.SetColumn(mainSplitterL, 1);
        Grid.SetColumn(centerPanel, 2);
        Grid.SetColumn(mainSplitterR, 3);
        Grid.SetColumn(rightPanel, 4);
        columns.Children.Add(leftPanel);
        columns.Children.Add(mainSplitterL);
        columns.Children.Add(centerPanel);
        columns.Children.Add(mainSplitterR);
        columns.Children.Add(rightPanel);

        var root = new DockPanel();
        DockPanel.SetDock(toolbarBorder, Dock.Top);
        DockPanel.SetDock(statusBorder, Dock.Bottom);
        root.Children.Add(toolbarBorder);
        root.Children.Add(statusBorder);
        root.Children.Add(columns);
        Content = root;

        // Keyboard shortcuts: the two actions people reach for constantly.
        KeyBindings.Add(new KeyBinding { Gesture = new KeyGesture(Key.S, KeyModifiers.Control), Command = new SimpleCommand(SaveCurrent) });
        KeyBindings.Add(new KeyBinding { Gesture = new KeyGesture(Key.F5), Command = new SimpleCommand(() => _ = RunCurrentAsync()) });

        Closing += async (_, e) =>
        {
            if (_dirty)
            {
                e.Cancel = true;
                if (await ConfirmDiscardAsync())
                {
                    _dirty = false;
                    Close();
                }
            }
        };

        RefreshScriptList(selectName: null);
        UpdateWindowTitle();
        UpdateTargetStatus();
        _statusState.Text = "Select or create a script";
    }

    // ── test hooks (InternalsVisibleTo RetroTerm.Tests) — UI facts the unit tests
    // assert instead of trusting a human to eyeball every build ─────────────────
    internal TextBox EditorForTests => _editor;
    internal TextBlock HighlightForTests => _highlight;
    internal Border TranscriptPanelForTests => _transcriptPanel;
    internal ListBox ScriptListForTests => _scriptList;
    internal void UpdateHighlightForTests() => UpdateHighlight();
    // Recomputes the "line N, col M" status from the current editor text + caret and
    // returns exactly what the status strip shows — lets a headless test prove the
    // line/col the user sees matches where the caret actually is (the "I'm on line 4,
    // status says 5" drift, 2026-08-05).
    internal string UpdateAndReadCaretStatusForTests()
    {
        UpdateCaretStatus();
        return _statusInfo.Text ?? string.Empty;
    }

    /// <summary>
    /// Binds a property to a theme resource — live theme switches restyle the window.
    /// </summary>
    private void BindTheme(AvaloniaObject target, AvaloniaProperty property, string key)
    {
        target.Bind(property, this.GetResourceObservable(key));
    }

    private Button ToolButton(string text, bool primary)
    {
        // The theme's Primary and Secondary classes. Until 27 September 2026 the same brushes
        // were bound onto the Button itself, which the Fluent hover style does not read - so
        // every toolbar button here lost its fill under the mouse (see the note above the
        // Button styles in DarkTheme.axaml). The toolbar's tighter padding is kept.
        var button = new Button
        {
            Content = text,
            Classes = { primary ? "Primary" : "Secondary" },
            Padding = new Thickness(14, 5, 14, 5),
            FontSize = 12
        };
        return button;
    }

    private Control ToolbarSeparator()
    {
        var sep = new Border { Width = 1, Margin = new Thickness(4, 2, 4, 2) };
        BindTheme(sep, Border.BackgroundProperty, "BorderBrush");
        return sep;
    }

    /// <summary>
    /// A side panel with a themed section header.
    /// </summary>
    private Border SidePanel(string caption, Control content)
    {
        var header = new TextBlock
        {
            Text = caption,
            FontSize = 11,
            FontWeight = FontWeight.SemiBold,
            Margin = new Thickness(2, 0, 0, 6)
        };
        BindTheme(header, TextBlock.ForegroundProperty, "SectionHeader1Brush");

        var panel = new DockPanel { Margin = new Thickness(8) };
        DockPanel.SetDock(header, Dock.Top);
        panel.Children.Add(header);
        panel.Children.Add(content);

        var border = new Border { Child = panel };
        BindTheme(border, Border.BackgroundProperty, "PanelBackgroundBrush");
        return border;
    }

    /// <summary>
    /// A collapsible bottom panel with a caption and a colored border.
    /// </summary>
    private Border CaptionedPanel(string caption, Control content, string borderKey)
    {
        var header = new TextBlock
        {
            Text = caption,
            FontSize = 11,
            FontWeight = FontWeight.SemiBold,
            Margin = new Thickness(8, 4, 0, 2)
        };
        BindTheme(header, TextBlock.ForegroundProperty, "SectionHeader2Brush");

        var panel = new DockPanel();
        DockPanel.SetDock(header, Dock.Top);
        panel.Children.Add(header);
        panel.Children.Add(content);

        // Full border + margins: the panel reads as a BOX inside the editor column,
        // not a stripe that visually merges with the script list across the splitter.
        var border = new Border
        {
            Child = panel,
            BorderThickness = new Thickness(1),
            Margin = new Thickness(8, 6, 8, 6),
            Padding = new Thickness(0, 0, 0, 4)
        };
        BindTheme(border, Border.BorderBrushProperty, borderKey);
        BindTheme(border, Border.BackgroundProperty, "PanelBackgroundBrush");
        return border;
    }

    // ─────────────────────────────────────────────────────────
    // Gutter + caret status
    // ─────────────────────────────────────────────────────────

    private void UpdateGutter()
    {
        var text = _editor.Text ?? string.Empty;
        int lines = 1;
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] == '\n') lines++;
        }
        var sb = new System.Text.StringBuilder(lines * 4);
        for (int i = 1; i <= lines; i++)
        {
            sb.Append(i).Append('\n');
        }
        _gutter.Text = sb.ToString();
    }

    // ─────────────────────────────────────────────────────────
    // Syntax highlighting overlay
    // ─────────────────────────────────────────────────────────

    /// <summary>
    /// Subscribes to a theme brush resource: fires immediately with the current value
    /// and again on every live theme switch, re-coloring the overlay each time.
    /// </summary>
    private void HookSyntaxBrush(string key, Action<IBrush?> assign)
    {
        this.GetResourceObservable(key).Subscribe(new Avalonia.Reactive.AnonymousObserver<object?>(value =>
        {
            assign(value as IBrush);
            UpdateHighlight();
        }));
    }

    /// <summary>
    /// Regenerates the colored Runs from the editor text. Runs on every keystroke —
    /// scripts are small (KBs) so a full re-lex is cheaper than incremental tracking.
    /// The substring/Run allocations here are UI-side editing work, not a hot path.
    /// </summary>
    private void UpdateHighlight()
    {
        // Brush observers fire during construction, before the overlay exists.
        if (_highlight == null)
        {
            return;
        }
        var inlines = _highlight.Inlines;
        if (inlines == null)
        {
            return;
        }
        inlines.Clear();

        var text = _editor?.Text ?? string.Empty;
        int i = 0;
        while (i <= text.Length)
        {
            int lineEnd = text.IndexOf('\n', Math.Min(i, text.Length));
            if (lineEnd < 0) lineEnd = text.Length;
            // CRLF: the TextBox inserts \r\n on Windows. The \r must NEVER reach a
            // Run — TextBlock can render it as its own line break, which shifts the
            // overlay against the real text and the caret/status lie about the line
            // (2026-08-05). One break per line, always the single '\n' Run below.
            int contentEnd = lineEnd;
            if (contentEnd > i && text[contentEnd - 1] == '\r')
            {
                contentEnd--;
            }
            HighlightLine(text, i, contentEnd, inlines);
            if (lineEnd < text.Length)
            {
                inlines.Add(new Run("\n"));
            }
            i = lineEnd + 1;
            if (lineEnd == text.Length)
            {
                break;
            }
        }
    }

    /// <summary>
    /// Lexes one line into colored Runs. Rules match the DSL: '#' starts a comment
    /// (outside strings), "quoted" is a string (\" and \\ escapes), $var/${var}/$$
    /// is a variable, the FIRST word is a keyword if it is a registered verb or a
    /// language word, and identifier= is an argument name.
    /// </summary>
    private void HighlightLine(string text, int start, int end, InlineCollection inlines)
    {
        int i = start;
        int plainStart = start;   // start of the pending uncolored stretch
        bool seenFirstWord = false;

        void FlushPlain(int upTo)
        {
            if (upTo > plainStart)
            {
                inlines.Add(MakeRun(text, plainStart, upTo, _brushPlain));
            }
        }

        while (i < end)
        {
            char c = text[i];

            if (c == '#')
            {
                // Comment to end of line (we are never inside a string here — the
                // string branch below consumes whole strings including any '#').
                FlushPlain(i);
                inlines.Add(MakeRun(text, i, end, _brushComment));
                plainStart = end;
                return;
            }

            if (c == '"')
            {
                FlushPlain(i);
                int j = i + 1;
                while (j < end)
                {
                    if (text[j] == '\\' && j + 1 < end) { j += 2; continue; }
                    if (text[j] == '"') { j++; break; }
                    j++;
                }
                inlines.Add(MakeRun(text, i, j, _brushString));
                i = j;
                plainStart = i;
                continue;
            }

            if (c == '$')
            {
                FlushPlain(i);
                int j = i + 1;
                if (j < end && text[j] == '$')
                {
                    j++;   // $$ = literal dollar
                }
                else if (j < end && text[j] == '{')
                {
                    while (j < end && text[j] != '}') j++;
                    if (j < end) j++;   // include the closing brace
                }
                else
                {
                    while (j < end && (char.IsLetterOrDigit(text[j]) || text[j] == '_')) j++;
                }
                inlines.Add(MakeRun(text, i, j, _brushVariable));
                i = j;
                plainStart = i;
                continue;
            }

            if (char.IsLetter(c) || c == '_')
            {
                int j = i;
                while (j < end && (char.IsLetterOrDigit(text[j]) || text[j] == '_' || text[j] == '-')) j++;

                if (!seenFirstWord)
                {
                    seenFirstWord = true;
                    // Substring allocation is fine here — editing UI, small scripts.
                    if (_knownVerbs.Contains(text.Substring(i, j - i)))
                    {
                        FlushPlain(i);
                        inlines.Add(MakeRun(text, i, j, _brushKeyword));
                        i = j;
                        plainStart = i;
                        continue;
                    }
                    i = j;   // unknown first word stays plain
                    continue;
                }

                if (j < end && text[j] == '=')
                {
                    // key= argument name, '=' included in the colored run
                    FlushPlain(i);
                    inlines.Add(MakeRun(text, i, j + 1, _brushParam));
                    i = j + 1;
                    plainStart = i;
                    continue;
                }

                i = j;
                continue;
            }

            i++;
        }
        FlushPlain(end);
    }

    private static Run MakeRun(string text, int start, int end, IBrush? brush)
    {
        var run = new Run(text.Substring(start, end - start));
        if (brush != null)
        {
            run.Foreground = brush;
        }
        return run;
    }

    /// <summary>
    /// Keeps the gutter scrolled in step with the editor's internal ScrollViewer.
    /// </summary>
    private void HookEditorScroll()
    {
        var viewer = FindScrollViewer(_editor);
        if (viewer == null)
        {
            return;
        }
        viewer.ScrollChanged += (_, _) =>
        {
            _gutterScroll.Offset = new Vector(0, viewer.Offset.Y);
            // The highlight overlay follows BOTH axes — the editor scrolls horizontally too.
            _highlightScroll.Offset = viewer.Offset;
        };
    }

    private static ScrollViewer? FindScrollViewer(Visual root)
    {
        var children = root.GetVisualChildren();
        var e = children.GetEnumerator();
        while (e.MoveNext())
        {
            if (e.Current is ScrollViewer viewer)
            {
                return viewer;
            }
            var nested = FindScrollViewer(e.Current);
            if (nested != null)
            {
                return nested;
            }
        }
        return null;
    }

    private void UpdateCaretStatus()
    {
        var text = _editor.Text ?? string.Empty;
        int caret = Math.Min(_editor.CaretIndex, text.Length);
        int line = 1;
        int column = 1;
        for (int i = 0; i < caret; i++)
        {
            if (text[i] == '\n') { line++; column = 1; }
            else column++;
        }
        int steps = _lastParse?.Steps.Count ?? 0;
        _statusInfo.Text = $"line {line}, col {column} · {steps} step(s)";
    }

    private void UpdateTargetStatus()
    {
        _statusTarget.Text = $"run target: {_target.DescribeTarget()}";
    }

    private void UpdateWindowTitle()
    {
        Title = _currentName == null
            ? "Script Editor"
            : $"Script Editor — {_currentName}{(_dirty ? " *" : "")}";
    }

    private void SetDirty(bool dirty)
    {
        if (_dirty != dirty)
        {
            _dirty = dirty;
            UpdateWindowTitle();
        }
    }

    // ─────────────────────────────────────────────────────────
    // Library operations
    // ─────────────────────────────────────────────────────────

    private void RefreshScriptList(string? selectName)
    {
        _scriptList.Items.Clear();
        var names = _library.List();
        int selectIndex = -1;
        for (int i = 0; i < names.Count; i++)
        {
            _scriptList.Items.Add(names[i]);
            if (string.Equals(names[i], selectName, StringComparison.OrdinalIgnoreCase))
            {
                selectIndex = i;
            }
        }
        if (selectIndex >= 0)
        {
            _scriptList.SelectedIndex = selectIndex;
        }
    }

    private async System.Threading.Tasks.Task LoadSelectedAsync()
    {
        var name = _scriptList.SelectedItem as string;
        if (name == null || string.Equals(name, _currentName, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        // The dirty check that prevents the silent-data-loss bug.
        if (_dirty && !await ConfirmDiscardAsync())
        {
            // Put the selection back on the script being edited.
            RefreshScriptList(selectName: _currentName);
            return;
        }

        _currentName = name;
        _suppressTextEvents = true;
        _editor.Text = _library.Exists(name) ? _library.Load(name) : string.Empty;
        _suppressTextEvents = false;
        _cleanText = _editor.Text ?? string.Empty;
        _editor.IsEnabled = true;
        SetDirty(false);
        UpdateWindowTitle();
        UpdateGutter();
        UpdateHighlight();   // text was set with events suppressed — repaint the overlay
        UpdateTargetStatus();
        Validate();
    }

    private async System.Threading.Tasks.Task<bool> ConfirmDiscardAsync()
    {
        return await Helpers.ErrorDialogHelper.ShowConfirmDialogAsync(this,
            "Unsaved Changes", $"'{_currentName}' has unsaved changes. Discard them?",
            "Discard", "Keep editing", isDanger: true);
    }

    private async System.Threading.Tasks.Task NewScriptAsync()
    {
        if (_dirty && !await ConfirmDiscardAsync())
        {
            return;
        }
        var name = await PromptForNameAsync("New Script", null);
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }
        try
        {
            _library.Save(name!, "# " + name + "\n");
        }
        catch (ArgumentException ex)
        {
            SetStateError(ex.Message);
            return;
        }
        SetDirty(false);
        RefreshScriptList(selectName: name);
    }

    private async System.Threading.Tasks.Task RenameScriptAsync()
    {
        if (_currentName == null)
        {
            SetStateError("No script selected");
            return;
        }
        var newName = await PromptForNameAsync("Rename Script", _currentName);
        if (string.IsNullOrWhiteSpace(newName)
            || string.Equals(newName, _currentName, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }
        if (_library.Exists(newName!))
        {
            SetStateError($"'{newName}' already exists");
            return;
        }
        try
        {
            // Save current content (even if dirty) under the new name, drop the old file.
            _library.Save(newName!, _editor.Text ?? string.Empty);
            _library.Delete(_currentName);
        }
        catch (ArgumentException ex)
        {
            SetStateError(ex.Message);
            return;
        }
        _currentName = newName;
        _cleanText = _editor.Text ?? string.Empty; // rename saved the current text
        SetDirty(false);
        RefreshScriptList(selectName: newName);
        SetStateOk($"Renamed to '{newName}'");
    }

    private async System.Threading.Tasks.Task DeleteSelectedAsync()
    {
        var name = _scriptList.SelectedItem as string;
        if (name == null)
        {
            return;
        }
        var confirmed = await Helpers.ErrorDialogHelper.ShowConfirmDialogAsync(this,
            "Delete Script", $"Delete script '{name}'?", "Delete", "Cancel", isDanger: true);
        if (!confirmed)
        {
            return;
        }
        _library.Delete(name);
        _currentName = null;
        _suppressTextEvents = true;
        _editor.Text = string.Empty;
        _suppressTextEvents = false;
        _cleanText = string.Empty;
        _editor.IsEnabled = false;
        SetDirty(false);
        UpdateGutter();
        UpdateHighlight();   // text was cleared with events suppressed — repaint the overlay
        _problemList.Items.Clear();
        _problemPanel.IsVisible = false;
        RefreshScriptList(selectName: null);
        UpdateWindowTitle();
        _statusState.Text = "Select or create a script";
    }

    private void SaveCurrent()
    {
        if (_currentName == null)
        {
            SetStateError("No script selected — use New first");
            return;
        }
        _library.Save(_currentName, _editor.Text ?? string.Empty);
        _cleanText = _editor.Text ?? string.Empty;
        SetDirty(false);
        var parsed = _parser.Parse(_editor.Text ?? string.Empty);
        if (parsed.IsValid)
        {
            SetStateOk($"Saved · {parsed.Steps.Count} step(s)");
        }
        else
        {
            SetStateWarning($"Saved WITH {parsed.Errors.Count} error(s) — it will not run until fixed");
        }
    }

    // ─────────────────────────────────────────────────────────
    // Validation / problems
    // ─────────────────────────────────────────────────────────

    private void Validate()
    {
        _problemList.Items.Clear();
        var text = _editor.Text ?? string.Empty;
        _lastParse = _parser.Parse(text);

        for (int i = 0; i < _lastParse.Errors.Count; i++)
        {
            // The ScriptParseError object rides along as the item's tag — no string
            // re-parsing when jumping to the line.
            _problemList.Items.Add(new ListBoxItem
            {
                Content = $"⛔ {_lastParse.Errors[i]}",
                Tag = _lastParse.Errors[i],
                FontSize = 12
            });
        }

        // The problems panel exists only while there ARE problems.
        _problemPanel.IsVisible = _lastParse.Errors.Count > 0;

        if (_lastParse.IsValid)
        {
            SetStateOk(_dirty ? $"✓ OK · {_lastParse.Steps.Count} step(s) · unsaved" : $"✓ OK · {_lastParse.Steps.Count} step(s)");
        }
        else
        {
            SetStateError($"{_lastParse.Errors.Count} problem(s)");
        }
        UpdateCaretStatus();
    }

    private void JumpToSelectedError()
    {
        var item = _problemList.SelectedItem as ListBoxItem;
        if (item?.Tag is not ScriptParseError error)
        {
            return;
        }

        var text = _editor.Text ?? string.Empty;
        int offset = 0;
        int current = 1;
        while (current < error.LineNumber && offset < text.Length)
        {
            int nl = text.IndexOf('\n', offset);
            if (nl < 0) break;
            offset = nl + 1;
            current++;
        }
        _editor.CaretIndex = offset;
        _editor.Focus();
    }

    // ─────────────────────────────────────────────────────────
    // Run / Stop
    // ─────────────────────────────────────────────────────────

    private async System.Threading.Tasks.Task RunCurrentAsync()
    {
        if (_runCts != null)
        {
            return; // already running
        }

        UpdateTargetStatus();
        var session = _target.ResolveSession();
        if (session == null)
        {
            SetStateError("No connection to run against — open a tab or pick one in Run on");
            return;
        }

        var parsed = _parser.Parse(_editor.Text ?? string.Empty);
        if (!parsed.IsValid)
        {
            Validate();
            SetStateError($"Cannot run — {parsed.Errors.Count} problem(s)");
            return;
        }
        if (parsed.Steps.Count == 0)
        {
            SetStateWarning("Nothing to run");
            return;
        }

        _runCts = new CancellationTokenSource();
        _runButton.IsEnabled = false;
        _stopButton.IsEnabled = true;
        _transcript.Text = string.Empty;
        _transcriptPanel.IsVisible = true;
        SetStateOk("Running…");

        void AppendLine(string line)
        {
            _transcript.Text += line + "\n";
            _transcript.CaretIndex = _transcript.Text?.Length ?? 0;
        }

        try
        {
            var progress = new Progress<ScriptStepResult>(stepResult =>
            {
                AppendLine($"{(stepResult.Result.Success ? "ok  " : "FAIL")} line {stepResult.Step.LineNumber} " +
                           $"[{(long)stepResult.Result.Elapsed.TotalMilliseconds} ms] {stepResult.Step.SourceText}");
                if (stepResult.ShowOutput && !string.IsNullOrEmpty(stepResult.Result.Output))
                {
                    AppendLine(stepResult.Result.Output!);
                }
            });

            var result = await _runner.RunAsync(parsed, session, _runCts.Token, progress);

            if (result.Success)
            {
                AppendLine($"\nOK — {result.Transcript.Count} step(s), {(long)result.Elapsed.TotalMilliseconds} ms total");
                SetStateOk($"Run OK · {(long)result.Elapsed.TotalMilliseconds} ms");
            }
            else
            {
                AppendLine($"\nFAILED: {result.Error}");
                if (result.Transcript.Count > 0)
                {
                    var last = result.Transcript[result.Transcript.Count - 1].Result;
                    if (!string.IsNullOrEmpty(last.Output))
                    {
                        AppendLine("screen at failure:");
                        AppendLine(last.Output!);
                    }
                }
                // Wording matters: this must read as "the FAILED script did not kill
                // your session", never as "scripts can't run on a live connection".
                AppendLine("the script STOPPED at the failing step — the connection is untouched and stays open. " +
                           "Fix the step and Run again, or try single commands in the Script Console.");
                SetStateError("Run FAILED — see transcript");
            }
        }
        catch (OperationCanceledException)
        {
            AppendLine("\nSTOPPED by user — the connection is untouched and stays open");
            SetStateWarning("Run stopped");
        }
        catch (Exception ex)
        {
            AppendLine("error: " + ex.Message);
            SetStateError("Run error: " + ex.Message);
        }
        finally
        {
            _runCts.Dispose();
            _runCts = null;
            _runButton.IsEnabled = true;
            _stopButton.IsEnabled = false;
        }
    }

    // ─────────────────────────────────────────────────────────
    // Help panel
    // ─────────────────────────────────────────────────────────

    private void ShowHelpForSelectedVerb()
    {
        var name = _verbList.SelectedItem as string;
        if (name == null || name == ControlFlowMarker)
        {
            return;
        }
        if (_registry.TryGet(name, out var command))
        {
            _helpText.Text = CommandHelpGenerator.GenerateFor(command);
            return;
        }
        // Language-level entries live in Core (ScriptLanguageHelp) — shared with the
        // HELP command and terminal_help so every surface shows the same list.
        var entry = ScriptLanguageHelp.Find(name);
        if (entry != null)
        {
            _helpText.Text = $"{entry.Help}\nExample: {entry.Example}";
        }
    }

    private void InsertExampleForSelectedVerb()
    {
        var name = _verbList.SelectedItem as string;
        if (name == null || name == ControlFlowMarker || !_editor.IsEnabled)
        {
            return;
        }

        string? example = null;
        if (_registry.TryGet(name, out var command))
        {
            example = command.Example;
        }
        else
        {
            example = ScriptLanguageHelp.Find(name)?.Example;
        }
        if (example == null)
        {
            return;
        }

        var text = _editor.Text ?? string.Empty;
        int caret = Math.Min(_editor.CaretIndex, text.Length);
        var insert = example + "\n";
        _editor.Text = text.Insert(caret, insert);
        _editor.CaretIndex = caret + insert.Length;
        _editor.Focus();
    }

    // ─────────────────────────────────────────────────────────
    // Status strip helpers
    // ─────────────────────────────────────────────────────────

    private void SetStateOk(string text)
    {
        _statusState.Text = text;
        BindTheme(_statusState, TextBlock.ForegroundProperty, "SuccessBrush");
    }

    private void SetStateWarning(string text)
    {
        _statusState.Text = text;
        BindTheme(_statusState, TextBlock.ForegroundProperty, "WarningBrush");
    }

    private void SetStateError(string text)
    {
        _statusState.Text = text;
        BindTheme(_statusState, TextBlock.ForegroundProperty, "ErrorBrush");
    }

    // ─────────────────────────────────────────────────────────
    // Name prompt
    // ─────────────────────────────────────────────────────────

    private async System.Threading.Tasks.Task<string?> PromptForNameAsync(string title, string? initial)
    {
        string? result = null;

        var dialog = new Window
        {
            Title = title,
            Width = 380,
            Height = 150,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false
        };
        BindTheme(dialog, BackgroundProperty, "WindowBackgroundBrush");

        var input = new TextBox
        {
            Watermark = "script-name (plain file name, no path)",
            Text = initial ?? string.Empty
        };
        BindTheme(input, BackgroundProperty, "SectionBackgroundBrush");
        BindTheme(input, ForegroundProperty, "PrimaryTextBrush");

        var okButton = ToolButton("OK", primary: true);
        okButton.Click += (_, _) => { result = input.Text; dialog.Close(); };
        var cancelButton = ToolButton("Cancel", primary: false);
        cancelButton.Click += (_, _) => dialog.Close();

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        buttons.Children.Add(cancelButton);
        buttons.Children.Add(okButton);

        var panel = new StackPanel { Margin = new Thickness(16), Spacing = 12 };
        panel.Children.Add(input);
        panel.Children.Add(buttons);
        dialog.Content = panel;

        input.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) { result = input.Text; dialog.Close(); }
        };

        await dialog.ShowDialog(this);
        return result;
    }

    /// <summary>
    /// Minimal ICommand for KeyBindings (Avalonia has no built-in delegate command).
    /// </summary>
    private sealed class SimpleCommand : System.Windows.Input.ICommand
    {
        private readonly Action _action;
        public SimpleCommand(Action action) => _action = action;
        public bool CanExecute(object? parameter) => true;
        public void Execute(object? parameter) => _action();
        public event EventHandler? CanExecuteChanged { add { } remove { } }
    }
}
