using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using RetroTerm.Core;
using RetroTerm.Core.Configuration;
using RetroTerm.Core.Logging;
using RetroTerm.Core.Protocols;
using RetroTerm.Core.Protocols.Kermit;
using RetroTerm.Core.Protocols.WebSocket.Gateway;
using RetroTerm.Core.Search;
using RetroTerm.Core.Session;
using RetroTerm.Core.Terminal.Emulators;
using RetroTerm.Core.Terminal.Emulators.TDV;
using RetroTerm.Core.Transfer;
using RetroTerm.Desktop.Controls;
using RetroTerm.Desktop.Helpers;
using RetroTerm.Desktop.Models;
using RetroTerm.Desktop.Views;

namespace RetroTerm.Desktop;

public partial class MainWindow : Window
{
    // Tab management
    private readonly List<TabSession> _tabs = new();
    private TabSession? _activeTab;
    private readonly List<TerminalPopoutWindow> _popoutWindows = new();
    private bool _isClosingConfirmed;

    // Custom tab bar controls
    private StackPanel? _tabStrip;

    /// <summary>
    /// The status bar's display-size dropdown.
    /// </summary>
    private ComboBox? _zoomSelector;

    /// <summary>
    /// Set while the dropdown is being written to from code, so its selection-changed handler
    /// does not turn round and set the zoom it was only being told about.
    /// </summary>
    private bool _updatingZoomSelector;
    private ContentControl? _tabContent;

    // Per-tab header UI elements (for fast updates)
    private readonly Dictionary<Guid, Ellipse> _tabStatusDots = new();
    private readonly Dictionary<Guid, TextBlock> _tabTitleTexts = new();
    private readonly Dictionary<Guid, Button> _tabCloseButtons = new();
    private readonly Dictionary<Guid, Border> _tabHeaders = new();

    // Status bar controls
    private TextBlock? _statusText;
    private TextBlock? _modeIndicator;
    private TextBlock? _connectionInfo;
    private string? _savedStatusText;
    private readonly ConfigurationManager _configManager;

    // Search UI
    private Border? _searchBar;
    private TextBox? _searchTextBox;
    private TextBlock? _searchMatchCount;
    private ScrollbackSearch? _search;
    private List<SearchMatch>? _searchMatches;
    private int _currentMatchIndex = -1;
    private bool _searchBarVisible = false;

    // Virtual keyboard window
    private VirtualKeyboardWindow? _virtualKeyboardWindow;

    // Gateway
    private GatewayListener? _gatewayListener;
    private GatewaySettings? _gatewaySettings;
    private GatewayDiskSettings? _gatewayDiskSettings;
    private TextBlock? _gatewayStatus;

    // File transfer
    private FileTransferProgressWindow? _transferProgressWindow;
    private KermitDebugWindow? _kermitDebugWindow;
    private OpcomDebugWindow? _opcomDebugWindow;

    // Gateway terminal menu state (tabs in menu-selection mode)
    private readonly Dictionary<Guid, GatewayMenuState> _gatewayMenuTabs = new();

    // SSH login state (tabs in username/password prompt mode)
    private readonly Dictionary<Guid, SSHLoginState> _sshLoginTabs = new();

    // Terminal color presets (shared between tab context menu and connection config)
    // Sourced from nd100x glass template + RetroTerm additions. Sorted alphabetically.
    // Mono=true collapses the sixteen ANSI colours onto the screen's single phosphor, the way a
    // one-gun CRT had no choice but to: a host's colours arrive as BRIGHTNESSES, not hues. The
    // existing seven presets keep Mono=false and are byte for byte what they always were - picking
    // a colour preset must not silently change how a coloured host program looks.
    internal static readonly (string Name, string Fg, string Bg, bool Mono)[] TerminalColorPresets =
    {
        ("Amber", "#FFBF00", "#0A0800", false),
        ("Amber (single phosphor)", "#FFBF00", "#0A0800", true),
        ("Blue", "#00BFFF", "#000A14", false),
        ("Cyan", "#00FFFF", "#001919", false),
        ("Green", "#00FF00", "#000E00", false),
        ("Green Phosphor", "#00FF88", "#001911", false),
        ("Green Phosphor (single phosphor)", "#00FF88", "#001911", true),
        ("Paper White", "#222222", "#F0F0F5", false),
        ("White", "#F8F8F8", "#0A0E1C", false),
    };

    /// <summary>
    /// Turns a SAVED CONNECTION's colours into the theme the renderer draws with.
    ///
    /// The single-phosphor flag has to be carried explicitly rather than worked out from the
    /// colours, because it cannot be worked out from them: "Amber" and "Amber (single phosphor)"
    /// store byte-for-byte identical foreground and background hex, and the flag is the only thing
    /// that tells them apart.
    /// </summary>
    internal static RetroTerm.Core.Terminal.Buffer.TerminalTheme BuildSavedConnectionTheme(
        RetroTerm.Core.Protocols.ConnectionFactory.ConnectionParameters parameters)
    {
        var fg = Color.Parse(parameters.ForegroundColor!);
        var bg = Color.Parse(parameters.BackgroundColor!);
        var foreground = (fg.R, fg.G, fg.B);
        var background = (bg.R, bg.G, bg.B);

        return parameters.SinglePhosphor
            ? RetroTerm.Core.Terminal.Buffer.TerminalTheme.Monochrome(parameters.DisplayName ?? "", foreground, background)
            : RetroTerm.Core.Terminal.Buffer.TerminalTheme.Colour(parameters.DisplayName ?? "", foreground, background);
    }

    /// <summary>
    /// Turns a colour preset into the theme the renderer draws with.
    /// </summary>
    internal static RetroTerm.Core.Terminal.Buffer.TerminalTheme BuildTerminalTheme(
        (string Name, string Fg, string Bg, bool Mono) preset)
    {
        var fg = Color.Parse(preset.Fg);
        var bg = Color.Parse(preset.Bg);
        var foreground = (fg.R, fg.G, fg.B);
        var background = (bg.R, bg.G, bg.B);

        return preset.Mono
            ? RetroTerm.Core.Terminal.Buffer.TerminalTheme.Monochrome(preset.Name, foreground, background)
            : RetroTerm.Core.Terminal.Buffer.TerminalTheme.Colour(preset.Name, foreground, background);
    }

    // Cached brushes for tab headers (avoid per-tab allocations)
    private static readonly SolidColorBrush s_tabActiveBg = new(Color.Parse("#1E1E1E"));
    private static readonly SolidColorBrush s_tabInactiveBg = new(Color.Parse("#252526"));
    private static readonly SolidColorBrush s_tabHoverBg = new(Color.Parse("#2D2D30"));
    private static readonly SolidColorBrush s_tabAccent = new(Color.Parse("#007ACC"));
    private static readonly SolidColorBrush s_tabActiveText = new(Color.Parse("#FFFFFF"));
    private static readonly SolidColorBrush s_tabInactiveText = new(Color.Parse("#969696"));
    private static readonly SolidColorBrush s_connectedDot = new(Color.Parse("#4EC9B0"));
    private static readonly SolidColorBrush s_transparent = new(Colors.Transparent);

    public MainWindow()
    {
        InitializeComponent();
        _configManager = new ConfigurationManager();
        InitializeTabSystem();
        InitializeSearch();
        InitializeGateway();
        InitializeFavorites();

        KeyDown += OnWindowKeyDown;

        // TUNNEL routing, not the bubble-routed OnWindowKeyDown above. TerminalCanvas.OnKeyDown
        // claims a plain Enter unconditionally - it maps it through the keyboard mapper and marks
        // it Handled without ever asking whether a session is connected, because that is
        // TerminalSession.SendBytesAsync's job further downstream. A bubble-routed handler on the
        // window never sees the event once the focused canvas has already handled it. Tunnel
        // routing runs top-down, before the canvas gets a turn, so this is the only place able to
        // intercept Enter ahead of it. Found 31 August 2026 the first time this was written as a
        // bubble handler and a test proved it dead code - never once observed to run.
        AddHandler(KeyDownEvent, OnEnterReconnectsTunnel, RoutingStrategies.Tunnel);
    }

    /// <summary>
    /// Enter reconnects a disconnected tab, alongside the existing Connection -> Reconnect menu
    /// path - not instead of it.
    /// </summary>
    /// <remarks>
    /// The disconnected notice (<see cref="ShowDisconnectionNotice"/>) already tells the reader
    /// "Reconnect with Connection -> Reconnect", naming the menu as the only way; Ronny asked
    /// for Enter to do the same thing. Guarded by <see cref="CanReconnect"/>, the SAME check the
    /// menu item itself uses, so this can only ever fire in exactly the state where that menu item
    /// is enabled - an ordinary Enter keystroke to a connected session, or one with no
    /// <c>LastConnectionParameters</c> to go back to, is left alone and reaches the canvas exactly
    /// as it always did.
    /// </remarks>
    private void OnEnterReconnectsTunnel(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && e.KeyModifiers == KeyModifiers.None && EnterShouldReconnect(_activeTab))
        {
            OnReconnectClick(null, null!);
            e.Handled = true;
        }
    }

    /// <summary>
    /// Whether an unmodified Enter on this tab means "reconnect", rather than being a keystroke
    /// the tab is waiting for.
    /// </summary>
    /// <remarks>
    /// <para><b>The SSH password that "did not work", 27 September 2026</b></para>
    /// A tab showing the in-terminal SSH login prompt, or the gateway terminal menu, is NOT
    /// connected and DOES have parameters to go back to - so <see cref="CanReconnect"/> was true
    /// while the user typed a password, and the Enter that ended the line went to this tunnel
    /// handler instead of the prompt. That restarted the connect from the saved parameters, which
    /// had no password, cleared the typed one and printed "Password:" again. Saving both the user
    /// name and the password never went through the prompt, which is why only that case worked.
    /// A tab waiting on one of those prompts owns its Enter key.
    /// </remarks>
    /// <param name="tab">
    /// The active tab, or null when there is none.
    /// </param>
    /// <returns>
    /// True when Enter should start a reconnect.
    /// </returns>
    private bool EnterShouldReconnect(TabSession? tab)
    {
        return CanReconnect(tab)
            && !_sshLoginTabs.ContainsKey(tab!.Id)
            && !_gatewayMenuTabs.ContainsKey(tab.Id);
    }

    private async void InitializeFavorites()
    {
        try
        {
            await _configManager.LoadAsync();
            BuildFavoritesSubmenu();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to load favorites: {ex.Message}");
        }

        _configManager.ConfigurationsChanged += () =>
        {
            Dispatcher.UIThread.Post(BuildFavoritesSubmenu);
        };
    }

    private void InitializeSearch()
    {
        // Search bar will be created when needed
    }

    private async void InitializeGateway()
    {
        _gatewayStatus = this.FindControl<TextBlock>("GatewayStatus");
        _gatewayListener = new GatewayListener();
        _gatewaySettings = GatewaySettings.Load();
        _gatewayDiskSettings = GatewayDiskSettings.Load();

        _gatewayListener.EmulatorConnected += OnGatewayStatusChanged;
        _gatewayListener.EmulatorDisconnected += OnGatewayStatusChanged;
        _gatewayListener.TerminalListChanged += OnGatewayStatusChanged;
        _gatewayListener.DiskWorkerConnected += OnGatewayStatusChanged;
        _gatewayListener.DiskWorkerDisconnected += OnGatewayStatusChanged;
        // So the indicator disappears the moment the gateway is switched off in Preferences,
        // instead of sitting there naming a port that is no longer open.
        _gatewayListener.ListeningChanged += OnGatewayStatusChanged;

        // Open any configured disk images
        _gatewayListener.OpenDiskImages(_gatewayDiskSettings);

        // Let the Preferences window reach the gateway. A provider rather than a constructor
        // argument, because Preferences is opened from several places and none of them should
        // have to know the gateway exists.
        Views.PreferencesWindow.GatewayListenerProvider = () => _gatewayListener;
        Views.PreferencesWindow.GatewaySettingsProvider = () => _gatewaySettings;

        // The Ethernet mapping is an APPLICATION setting, so it is applied at start rather than
        // waiting for someone to open Preferences. "none" is the default and costs nothing.
        _gatewayListener.ConfigureEthernet(
            Themes.ThemeManager.Instance.BuildEthernetSpec(),
            Themes.ThemeManager.Instance.EthernetSegment);

        if (_gatewaySettings.Enabled)
        {
            try
            {
                await _gatewayListener.StartAsync(_gatewaySettings.Port);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to start Gateway listener: {ex.Message}");
            }
        }

        UpdateGatewayStatus();
    }

    private void OnGatewayStatusChanged()
    {
        Dispatcher.UIThread.Post(UpdateGatewayStatus);
    }

    private void UpdateGatewayStatus()
    {
        if (_gatewayStatus == null || _gatewayListener == null) return;

        if (!_gatewayListener.IsListening)
        {
            _gatewayStatus.IsVisible = false;
            return;
        }

        _gatewayStatus.IsVisible = true;

        if (_gatewayListener.IsEmulatorConnected)
        {
            int count = _gatewayListener.TerminalCount;
            var termWord = count == 1 ? "terminal" : "terminals";
            _gatewayStatus.Text = $"GW: {count} {termWord}";
            _gatewayStatus.Foreground = s_connectedDot; // green
        }
        else
        {
            _gatewayStatus.Text = $"GW: :{_gatewayListener.Port}";
            _gatewayStatus.Foreground = new SolidColorBrush(Color.Parse("#999999"));
        }
    }

    // ───────────────────────────────────────────────────────────────────
    // Tab System (custom tab bar — no TabControl)
    // ───────────────────────────────────────────────────────────────────

    private void InitializeTabSystem()
    {
        _tabStrip = this.FindControl<StackPanel>("TabStrip");
        _tabContent = this.FindControl<ContentControl>("TabContent");
        _statusText = this.FindControl<TextBlock>("StatusText");
        _zoomSelector = this.FindControl<ComboBox>("ZoomSelector");
        BuildZoomSelector();
        _modeIndicator = this.FindControl<TextBlock>("ModeIndicator");
        _connectionInfo = this.FindControl<TextBlock>("ConnectionInfo");

        // Apply style class to "+" button
        var newTabButton = this.FindControl<Button>("NewTabButton");
        if (newTabButton != null)
        {
            newTabButton.Classes.Add("NewTabButton");
        }

        // The scroll buttons and the all-sessions list beside it.
        InitializeTabOverflow();

        // Every terminal the factory can build, openable without saving a connection first.
        BuildNewTabAsSubmenu();

        // And the same list again, this time changing the tab you are already looking at.
        BuildEmulationSubmenu();

        // Create initial tab
        // Sized by the terminal, not by a literal: the profile knows its own screen.
        var (startWidth, startHeight) = EmulatorFactory.GetRecommendedSize("VT100");
        var tab = CreateTab("VT100", startWidth, startHeight);
        ShowWelcomeMessage(tab);
        UpdateStatusDisplay();

        // Start the localhost MCP server so an LLM can drive these tabs
        // (fire-and-forget: a failure is logged, never blocks startup).
        _ = InitializeMcpServerAsync();
    }

    private TabSession CreateTab(string emulatorType, int width, int height)
    {
        // 1. Create emulator
        var emulator = EmulatorFactory.CreateEmulator(emulatorType, width, height);
        if (emulator is not TerminalEmulatorBase emulatorBase)
            throw new InvalidOperationException($"Failed to create emulator: {emulatorType}");

        // 2. Create session
        var session = new TerminalSession(emulatorBase, "RetroTerm");

        // 3. Create terminal control
        var control = new TerminalControl();
        control.SetEmulator(emulatorBase);

        // 4. Create TabSession
        var tab = new TabSession(session, control)
        {
            EmulatorType = emulatorType
        };

        // 5. Wire session events with active-tab guards
        session.StatusChanged += (status) => OnTabStatusChanged(tab, status);
        session.ErrorOccurred += (ex) => OnTabError(tab, ex);
        session.DisplayInvalidated += () => OnTabDisplayInvalidated(tab);
        // Connection-drop hook: reads the tab's CURRENT parameters at fire time, so
        // one subscription covers every reconnect (see MainWindow.Mcp.cs).
        session.ConnectionLost += (reason) => OnTabConnectionLost(tab, reason);

        // The terminal type changed underneath a live connection. ONE subscription covers the View
        // menu, a .rts script and an MCP call alike, because all three go through the session - so
        // the canvas cannot end up rewired by one route and stale by another.
        session.EmulatorChanged += replacement => OnTabEmulatorChanged(tab, replacement);

        // 6. Wire control events
        control.InputReceived += (input) => OnTabInput(tab, input);
        control.KeyMirrored += (vk, mods, pressed) => OnTabKeyMirrored(tab, vk, mods, pressed);
        control.ScrollOffsetChanged += (offset) => OnTabScrollOffsetChanged(tab, offset);
        control.RegisGraphicsInputChanged += (mode) => OnTabRegisGraphicsInputChanged(tab, mode);

        // The window drives the terminal size. Handed straight to the session, which resizes the
        // emulator on its pump and tells the host - neither of which is the UI's business.
        // Fire-and-forget on purpose: a resize must not block laying the window out, and the
        // session reports its own failures through ErrorOccurred, which is already wired above.
        control.TerminalResizeRequested += (columns, rows) => _ = tab.Session.ResizeAsync(columns, rows);

        // Ctrl+plus, Ctrl+minus and Ctrl+0 change the zoom from the keyboard. The dropdown has
        // to follow, or the status bar would claim a size the terminal is not at.
        control.ZoomChanged += percent =>
        {
            if (ReferenceEquals(tab, _activeTab)) SyncZoomSelector(percent);
        };

        // 7. Create tab header + add to strip
        var header = CreateTabHeader(tab);
        _tabs.Add(tab);
        _tabStrip?.Children.Add(header);

        // One more tab may be the one that no longer fits.
        UpdateTabScrollButtons();

        // 8. Select the new tab
        SelectTab(tab);

        return tab;
    }

    private Border CreateTabHeader(TabSession tab)
    {
        // Status dot (connected indicator)
        var dot = new Ellipse
        {
            Width = 7,
            Height = 7,
            Fill = s_transparent,
            Margin = new Thickness(0, 0, 6, 0),
            VerticalAlignment = VerticalAlignment.Center
        };

        // Title text
        var titleText = new TextBlock
        {
            Text = tab.Title,
            FontSize = 12,
            Foreground = s_tabInactiveText,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth = 180
        };

        // Close button (invisible by default, shown on hover/active)
        var closeButton = new Button
        {
            Content = "\u00d7",
            Classes = { "TabClose" },
            Margin = new Thickness(6, 0, 0, 0)
        };
        closeButton.Click += (_, _) => CloseTab(tab);

        // Layout: dot + title fill, close button on right
        var innerPanel = new DockPanel
        {
            LastChildFill = true
        };
        DockPanel.SetDock(closeButton, Dock.Right);
        innerPanel.Children.Add(closeButton);

        var leftPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center
        };
        leftPanel.Children.Add(dot);
        leftPanel.Children.Add(titleText);
        innerPanel.Children.Add(leftPanel);

        // Tab header border
        var header = new Border
        {
            Child = innerPanel,
            Background = s_tabInactiveBg,
            BorderThickness = new Thickness(0, 2, 0, 0),
            BorderBrush = s_transparent,
            Padding = new Thickness(12, 7, 8, 7),
            Margin = new Thickness(0, 0, 1, 0),
            Cursor = new Cursor(StandardCursorType.Hand),
            Tag = tab.Id
        };

        // Click to select
        header.PointerPressed += (_, e) =>
        {
            if (e.GetCurrentPoint(header).Properties.IsLeftButtonPressed)
            {
                SelectTab(tab);
            }
        };

        // Hover effects
        header.PointerEntered += (_, _) =>
        {
            if (tab != _activeTab)
            {
                header.Background = s_tabHoverBg;
            }
            closeButton.Foreground = s_tabInactiveText; // show close button
        };
        header.PointerExited += (_, _) =>
        {
            if (tab != _activeTab)
            {
                header.Background = s_tabInactiveBg;
            }
            if (tab != _activeTab)
            {
                closeButton.Foreground = s_transparent; // hide close button
            }
        };

        // Context menu
        var popOutItem = new MenuItem { Header = "Pop Out" };
        popOutItem.Click += (_, _) => PopOutTab(tab);

        // Terminal Color submenu
        var colorMenu = new MenuItem { Header = "Terminal Color" };
        for (int ci = 0; ci < TerminalColorPresets.Length; ci++)
        {
            var preset = TerminalColorPresets[ci];
            var colorItem = new MenuItem();

            var itemPanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            // Show fg swatch on bg swatch
            itemPanel.Children.Add(new Border
            {
                Width = 20,
                Height = 16,
                CornerRadius = new CornerRadius(2),
                Background = new SolidColorBrush(Color.Parse(preset.Bg)),
                BorderBrush = new SolidColorBrush(Color.Parse("#555555")),
                BorderThickness = new Thickness(1),
                Child = new TextBlock
                {
                    Text = "A",
                    FontSize = 10,
                    Foreground = new SolidColorBrush(Color.Parse(preset.Fg)),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            });
            itemPanel.Children.Add(new TextBlock { Text = preset.Name, VerticalAlignment = VerticalAlignment.Center });
            colorItem.Header = itemPanel;

            colorItem.Click += (_, _) =>
            {
                tab.Control.SetTheme(BuildTerminalTheme(preset));
                tab.ColorPreset = preset.Name;
            };
            colorMenu.Items.Add(colorItem);
        }

        var closeMenuItem = new MenuItem { Header = "Close" };
        closeMenuItem.Click += (_, _) => CloseTab(tab);
        var closeAllMenuItem = new MenuItem { Header = "Close All Tabs" };
        closeAllMenuItem.Click += (_, _) => CloseAllTabs();
        header.ContextMenu = new ContextMenu
        {
            Items = { popOutItem, colorMenu, new Separator(), closeMenuItem, closeAllMenuItem }
        };

        // Wire title changes
        tab.TitleChanged += () =>
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (_tabTitleTexts.ContainsKey(tab.Id))
                {
                    _tabTitleTexts[tab.Id].Text = tab.Title;
                }
            });
        };

        // Store references for fast lookup
        _tabHeaders[tab.Id] = header;
        _tabStatusDots[tab.Id] = dot;
        _tabTitleTexts[tab.Id] = titleText;
        _tabCloseButtons[tab.Id] = closeButton;

        return header;
    }

    /// <summary>
    /// Fills the display-size dropdown from the canvas's own ladder and wires its selection.
    /// </summary>
    /// <remarks>
    /// The percentages come from <see cref="Controls.TerminalCanvas.ZoomSteps"/> rather than being
    /// listed again in the markup. Two lists would drift, and a dropdown offering a size the
    /// keyboard shortcuts cannot reach is worse than either on its own.
    /// </remarks>
    private void BuildZoomSelector()
    {
        if (_zoomSelector == null) return;

        // No separate "Fit" entry any more: 100% IS the fitted picture, so a second name for it
        // would be two entries doing the same thing - and the old pair actively misled, because
        // "Fit" and "100%" then showed noticeably different sizes.
        var items = new List<string>();
        for (int i = 0; i < Controls.TerminalCanvas.ZoomSteps.Length; i++)
        {
            items.Add(Controls.TerminalCanvas.ZoomSteps[i].ToString(CultureInfo.InvariantCulture) + "%");
        }

        _zoomSelector.ItemsSource = items;
        _zoomSelector.SelectionChanged += OnZoomSelectionChanged;

        SyncZoomSelector(_activeTab?.Control.ZoomPercent ?? Controls.TerminalCanvas.NaturalZoomPercent);
    }

    /// <summary>
    /// Points the dropdown at a zoom without treating that as the user choosing it.
    /// </summary>
    /// <param name="percent">
    /// Zoom as a percentage; zero is read as the normal view, which is what it used to mean.
    /// </param>
    private void SyncZoomSelector(int percent)
    {
        if (_zoomSelector?.ItemsSource == null) return;

        if (percent <= 0) percent = Controls.TerminalCanvas.NaturalZoomPercent;
        string wanted = percent.ToString(CultureInfo.InvariantCulture) + "%";

        int index = 0;
        var items = (List<string>)_zoomSelector.ItemsSource;
        for (int i = 0; i < items.Count; i++)
        {
            if (string.Equals(items[i], wanted, StringComparison.Ordinal)) { index = i; break; }
        }

        _updatingZoomSelector = true;
        try
        {
            _zoomSelector.SelectedIndex = index;
        }
        finally
        {
            _updatingZoomSelector = false;
        }
    }

    /// <summary>
    /// Applies a zoom the user picked from the dropdown to the active tab.
    /// </summary>
    /// <param name="sender">
    /// The dropdown.
    /// </param>
    /// <param name="e">
    /// Selection details, unused.
    /// </param>
    private void OnZoomSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_updatingZoomSelector) return;
        if (_zoomSelector?.SelectedItem is not string chosen) return;
        if (_activeTab == null) return;

        // "150%" - everything before the sign.
        string number = chosen.Substring(0, chosen.Length - 1);
        if (int.TryParse(number, NumberStyles.Integer, CultureInfo.InvariantCulture, out int percent))
        {
            _activeTab.Control.ZoomPercent = percent;
        }
    }

    /// <summary>
    /// View menu: one step up the zoom ladder.
    /// </summary>
    /// <param name="sender">
    /// The menu item.
    /// </param>
    /// <param name="e">
    /// Click details, unused.
    /// </param>
    private void OnZoomInClick(object? sender, RoutedEventArgs e)
        => _activeTab?.Control.StepZoom(1);

    /// <summary>
    /// View menu: one step down the zoom ladder.
    /// </summary>
    /// <param name="sender">
    /// The menu item.
    /// </param>
    /// <param name="e">
    /// Click details, unused.
    /// </param>
    private void OnZoomOutClick(object? sender, RoutedEventArgs e)
        => _activeTab?.Control.StepZoom(-1);

    /// <summary>
    /// View menu: back to letting the window decide the size.
    /// </summary>
    /// <param name="sender">
    /// The menu item.
    /// </param>
    /// <param name="e">
    /// Click details, unused.
    /// </param>
    private void OnZoomResetClick(object? sender, RoutedEventArgs e)
    {
        if (_activeTab != null) _activeTab.Control.ZoomPercent = Controls.TerminalCanvas.NaturalZoomPercent;
    }

    private void SelectTab(TabSession tab)
    {
        if (tab == _activeTab) return;

        // Deactivate old tab header
        if (_activeTab != null)
        {
            ApplyTabHeaderStyle(_activeTab, false);
        }

        // Swap content
        if (_tabContent != null)
        {
            _tabContent.Content = null;
            _tabContent.Content = tab.Control;
        }

        _activeTab = tab;

        // Activate new tab header
        ApplyTabHeaderStyle(tab, true);

        // Update all UI state
        SyncZoomSelector(tab.Control.ZoomPercent);
        UpdateStatusDisplay();
        UpdateMenuState();
        UpdateRecIndicator();

        // Update virtual keyboard
        if (_virtualKeyboardWindow != null && _virtualKeyboardWindow.IsVisible)
        {
            _virtualKeyboardWindow.SetKeyboardMapper(_activeTab?.Control.GetKeyboardMapper());
            _virtualKeyboardWindow.CurrentTerminalModes = GetCurrentTerminalModes();

            // Re-point the lamps at the newly active tab's emulator and connection.
            SyncVirtualKeyboardLamps();

            // Sync layout dropdown to match the active terminal's language
            var lang = _activeTab?.LastConnectionParameters?.Language;
            if (!string.IsNullOrEmpty(lang))
                _virtualKeyboardWindow.SetLayout(lang);
        }

        // Focus the terminal
        tab.Control.Focus();

        // Recreate search if search bar is open
        if (_searchBarVisible)
        {
            _search = new ScrollbackSearch(tab.Session.Emulator.GetBuffer());
            OnSearchTextChanged(null, null!);
        }
    }

    private void ApplyTabHeaderStyle(TabSession tab, bool active)
    {
        if (!_tabHeaders.TryGetValue(tab.Id, out var header)) return;

        if (active)
        {
            header.Background = s_tabActiveBg;
            header.BorderBrush = s_tabAccent;
            if (_tabTitleTexts.TryGetValue(tab.Id, out var title))
            {
                title.Foreground = s_tabActiveText;
                title.FontWeight = FontWeight.SemiBold;
            }
            if (_tabCloseButtons.TryGetValue(tab.Id, out var close))
            {
                close.Foreground = s_tabInactiveText; // visible on active tab
            }
        }
        else
        {
            header.Background = s_tabInactiveBg;
            header.BorderBrush = s_transparent;
            if (_tabTitleTexts.TryGetValue(tab.Id, out var title))
            {
                title.Foreground = s_tabInactiveText;
                title.FontWeight = FontWeight.Normal;
            }
            if (_tabCloseButtons.TryGetValue(tab.Id, out var close))
            {
                close.Foreground = s_transparent; // hidden on inactive tab
            }
        }
    }

    private void UpdateTabStatusDot(TabSession tab)
    {
        if (_tabStatusDots.TryGetValue(tab.Id, out var dot))
        {
            dot.Fill = tab.IsConnected ? s_connectedDot : s_transparent;
        }
    }

    // ───────────────────────────────────────────────────────────────────
    // Tab Event Handlers (with active-tab guards)
    // ───────────────────────────────────────────────────────────────────

    private void OnTabStatusChanged(TabSession tab, ConnectionStatus status)
    {
        Dispatcher.UIThread.Post(() =>
        {
            // Guard: skip if tab was removed (user-initiated close/pop-out)
            if (!_tabs.Contains(tab)) return;

            // Update status dot in header
            UpdateTabStatusDot(tab);

            if (tab == _activeTab)
            {
                // Keep the keyboard's LINE/CAR/WAIT/ERROR lamps in step with the line.
                SyncVirtualKeyboardLamps();

                UpdateStatus($"Connection: {status}");
                UpdateMenuState();
            }

            if (status == ConnectionStatus.Disconnected)
            {
                StopSessionLogging(tab);
                tab.Host = null;
                tab.NotifyTitleChanged();

                // Non-blocking: no modal dialog on a dropped connection. The tab gets
                // the notice on its own screen; scripts/MCP are told via the session's
                // ConnectionLost event; the status bar shows Disconnected.
                if (!tab.SuppressDisconnectDialog)
                {
                    ShowDisconnectionNotice(tab, "Connection closed by remote host");
                }

                if (tab == _activeTab)
                {
                    UpdateStatusDisplay();
                    UpdateStatus("Disconnected");
                }
            }
            else if (status == ConnectionStatus.Connected)
            {
                if (tab == _activeTab)
                {
                    UpdateStatusDisplay();
                }
            }
        });
    }

    private void OnTabDisplayInvalidated(TabSession tab)
    {
        if (tab == _activeTab)
        {
            Dispatcher.UIThread.Post(() => UpdateStatusDisplay());
        }
    }

    private void OnTabError(TabSession tab, Exception ex)
    {
        Dispatcher.UIThread.Post(() =>
        {
            // Guard: skip if tab was removed
            if (!_tabs.Contains(tab)) return;
            if (tab.SuppressDisconnectDialog) return;

            // Non-blocking: the error lands on the tab's screen and in the status
            // bar, never in a modal dialog.
            ShowDisconnectionNotice(tab, $"Connection error: {ex.Message}");
        });
    }

    private async void OnTabInput(TabSession tab, string input)
    {
        // Gateway menu mode: intercept input for terminal selection
        if (_gatewayMenuTabs.ContainsKey(tab.Id))
        {
            HandleGatewayMenuInput(tab, input);
            return;
        }

        // SSH login mode: intercept input for username/password
        if (_sshLoginTabs.ContainsKey(tab.Id))
        {
            HandleSSHLoginInput(tab, input);
            return;
        }

        if (tab.Session.IsConnected)
        {
            // The Keyboard tab's two typing settings live HERE and nowhere else, because this is
            // the path a person's keystrokes take. A script's SEND and the MCP tool deliberately
            // do not pass through: those say exactly what bytes they want and must stay verbatim,
            // which is the rule the SEND-swallowing-\r defect established.
            var settings = tab.LastConnectionParameters;

            string toSend = ApplyTransmitNewLine(input, settings?.NewLineTransmit);

            try
            {
                await tab.Session.SendInputAsync(toSend).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error sending input: {ex.Message}");
            }

            // Local echo AFTER the send, so what is on the glass is in the order it went out.
            // Echoing what was TYPED rather than what was transmitted: a person who pressed Enter
            // wants a new line, not to see the CR+LF pair they configured.
            if (settings != null && settings.LocalEcho)
            {
                tab.Session.WriteToTerminal(input);
            }
        }
    }

    /// <summary>
    /// Rewrites the carriage return the Enter key produces into what this connection asked for.
    /// </summary>
    /// <param name="input">
    /// The characters just typed.
    /// </param>
    /// <param name="mode">
    /// The connection's transmit choice, or null for a connection that has none.
    /// </param>
    /// <returns>
    /// The text to send.
    /// </returns>
    /// <remarks>
    /// CR is both the default and what this program has always sent, so a connection with no
    /// setting - every connection saved before the Keyboard tab existed - comes through here
    /// unchanged. Only a deliberate CR+LF or LF rewrites anything.
    /// </remarks>
    internal static string ApplyTransmitNewLine(string input, string? mode)
    {
        if (string.IsNullOrEmpty(input)) return input;
        if (input.IndexOf('\r') < 0) return input;

        if (string.Equals(mode, ConnectionFactory.NewLineModes.CrLf, StringComparison.OrdinalIgnoreCase))
        {
            return input.Replace("\r", "\r\n");
        }
        if (string.Equals(mode, ConnectionFactory.NewLineModes.Lf, StringComparison.OrdinalIgnoreCase))
        {
            return input.Replace("\r", "\n");
        }

        return input;
    }

    private void OnTabKeyMirrored(TabSession tab, int vkCode, RetroTerm.Core.Terminal.Input.KeyModifiers modifiers, bool pressed)
    {
        if (tab == _activeTab && _virtualKeyboardWindow != null && _virtualKeyboardWindow.IsVisible)
        {
            _virtualKeyboardWindow.MirrorPhysicalKey(vkCode, modifiers, pressed);
        }
    }

    /// <summary>
    /// Says in the status line that ReGIS graphics input mode is running, and how to get out of it.
    /// </summary>
    /// <param name="tab">
    /// Which tab changed.
    /// </param>
    /// <param name="mode">
    /// The graphics input mode now running.
    /// </param>
    /// <remarks>
    /// One-shot mode suspends the host's data, so the screen stops changing entirely. A reader with
    /// no note would see a dead session. The old status text is put back afterwards, the same way the
    /// scrollback note does it.
    /// </remarks>
    private void OnTabRegisGraphicsInputChanged(TabSession tab,
        RetroTerm.Core.Terminal.Graphics.RegisGraphicsInputMode mode)
    {
        if (tab != _activeTab) return;

        Dispatcher.UIThread.Post(() =>
        {
            if (mode != RetroTerm.Core.Terminal.Graphics.RegisGraphicsInputMode.Off)
            {
                if (_savedStatusText == null && _statusText != null)
                {
                    _savedStatusText = _statusText.Text;
                }

                UpdateStatus(mode == RetroTerm.Core.Terminal.Graphics.RegisGraphicsInputMode.OneShot
                    ? "Graphics input: arrow keys move the crosshair, shift moves 10, any other key answers. Esc cancels."
                    : "Graphics input: the host is reading the crosshair position.");
            }
            else if (_savedStatusText != null)
            {
                UpdateStatus(_savedStatusText);
                _savedStatusText = null;
            }
        });
    }

    private void OnTabScrollOffsetChanged(TabSession tab, int offset)
    {
        if (tab != _activeTab) return;

        Dispatcher.UIThread.Post(() =>
        {
            if (offset > 0)
            {
                if (_savedStatusText == null && _statusText != null)
                {
                    _savedStatusText = _statusText.Text;
                }
                UpdateStatus(offset == 1
                    ? "Scrollback: 1 line back"
                    : $"Scrollback: {offset} lines back");
            }
            else
            {
                if (_savedStatusText != null)
                {
                    UpdateStatus(_savedStatusText);
                    _savedStatusText = null;
                }
            }
        });
    }

    // ───────────────────────────────────────────────────────────────────
    // Close / Pop-out Tab (async to avoid deadlock)
    // ───────────────────────────────────────────────────────────────────

    private async void CloseTab(TabSession tab)
    {
        StopSessionLogging(tab);

        // Remove from tab list FIRST — prevents event handlers from showing dialogs
        _tabs.Remove(tab);

        // Remove header from tab strip
        RemoveTabHeader(tab);

        // If the closed tab was active, select an adjacent tab
        if (tab == _activeTab)
        {
            _activeTab = null;
            if (_tabContent != null)
            {
                _tabContent.Content = null;
            }

            if (_tabs.Count > 0)
            {
                SelectTab(_tabs[_tabs.Count - 1]);
            }
        }

        // If no tabs remain, create a fresh welcome tab
        if (_tabs.Count == 0)
        {
            var newTab = CreateTab("VT100", 80, 24);
            ShowWelcomeMessage(newTab);
        }

        // Disconnect ASYNC before dispose (avoids UI thread deadlock)
        if (tab.Session.IsConnected)
        {
            try { await tab.Session.DisconnectAsync(); }
            catch { /* ignore disconnect errors during close */ }
        }

        // Now safe to dispose (session already disconnected)
        tab.Dispose();
    }

    private async void CloseAllTabs()
    {
        // Snapshot the list — CloseTab modifies _tabs
        var tabsToClose = new TabSession[_tabs.Count];
        for (int i = 0; i < _tabs.Count; i++)
        {
            tabsToClose[i] = _tabs[i];
        }

        // Clear UI immediately
        _activeTab = null;
        if (_tabContent != null)
        {
            _tabContent.Content = null;
        }

        _tabs.Clear();
        if (_tabStrip != null)
        {
            _tabStrip.Children.Clear();
        }
        _tabHeaders.Clear();
        _tabStatusDots.Clear();
        _tabTitleTexts.Clear();
        _tabCloseButtons.Clear();

        // Disconnect and dispose all
        for (int i = 0; i < tabsToClose.Length; i++)
        {
            StopSessionLogging(tabsToClose[i]);
            if (tabsToClose[i].Session.IsConnected)
            {
                try { await tabsToClose[i].Session.DisconnectAsync(); }
                catch { /* ignore */ }
            }
            tabsToClose[i].Dispose();
        }

        // Create a fresh welcome tab
        var newTab = CreateTab("VT100", 80, 24);
        ShowWelcomeMessage(newTab);
    }

    private void RemoveTabHeader(TabSession tab)
    {
        if (_tabStrip != null && _tabHeaders.TryGetValue(tab.Id, out var header))
        {
            _tabStrip.Children.Remove(header);
        }

        // Clean up dictionaries
        _tabHeaders.Remove(tab.Id);
        _tabStatusDots.Remove(tab.Id);
        _tabTitleTexts.Remove(tab.Id);
        _tabCloseButtons.Remove(tab.Id);

        // One fewer tab may be the one that made the strip fit again.
        UpdateTabScrollButtons();
    }

    private void PopOutTab(TabSession tab)
    {
        // Remove from tab list FIRST
        _tabs.Remove(tab);
        RemoveTabHeader(tab);

        // Detach control from content area
        if (tab == _activeTab)
        {
            _activeTab = null;
            if (_tabContent != null)
            {
                _tabContent.Content = null;
            }
        }

        // Create popout window (reparents the control)
        var popout = new TerminalPopoutWindow(tab);
        _popoutWindows.Add(popout);
        popout.PopoutClosed += OnPopoutWindowClosed;
        popout.Show();

        // Select another tab or create welcome tab
        if (_tabs.Count > 0)
        {
            SelectTab(_tabs[_tabs.Count - 1]);
        }
        else
        {
            var newTab = CreateTab("VT100", 80, 24);
            ShowWelcomeMessage(newTab);
        }
    }

    private void OnPopoutWindowClosed(TerminalPopoutWindow window, TabSession tab)
    {
        _popoutWindows.Remove(window);
    }

    // ───────────────────────────────────────────────────────────────────
    // Keyboard Shortcuts
    // ───────────────────────────────────────────────────────────────────

    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        // Ctrl+Shift+Tab = Previous Tab (must check BEFORE Ctrl+Tab)
        if (e.Key == Key.Tab && e.KeyModifiers == (KeyModifiers.Control | KeyModifiers.Shift))
        {
            SwitchToPreviousTab();
            e.Handled = true;
            return;
        }

        // Ctrl+Tab = Next Tab
        if (e.Key == Key.Tab && e.KeyModifiers == KeyModifiers.Control)
        {
            SwitchToNextTab();
            e.Handled = true;
            return;
        }

        // Ctrl+T = New Tab
        if (e.KeyModifiers == KeyModifiers.Control && e.Key == Key.T)
        {
            OnNewTabClick(null, null!);
            e.Handled = true;
            return;
        }

        // Ctrl+W = Close Tab
        if (e.KeyModifiers == KeyModifiers.Control && e.Key == Key.W)
        {
            if (_activeTab != null)
            {
                CloseTab(_activeTab);
            }
            e.Handled = true;
            return;
        }


        // Handle Ctrl+Shift shortcuts
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control) && e.KeyModifiers.HasFlag(KeyModifiers.Shift))
        {
            if (e.Key == Key.A)
            {
                _activeTab?.Control.SelectAll();
                e.Handled = true;
                return;
            }
            else if (e.Key == Key.F)
            {
                ToggleSearchBar();
                e.Handled = true;
                return;
            }
            else if (e.Key == Key.N)
            {
                OnQuickConnectClick(null, null!);
                e.Handled = true;
                return;
            }
        }

        // Handle F3 for next match
        if (e.Key == Key.F3)
        {
            if (_searchBarVisible)
            {
                if (e.KeyModifiers.HasFlag(KeyModifiers.Shift))
                    FindPrevious();
                else
                    FindNext();
                e.Handled = true;
            }
        }
        // Handle Escape to close search
        else if (e.Key == Key.Escape && _searchBarVisible)
        {
            HideSearchBar();
            e.Handled = true;
        }
    }

    private void SwitchToNextTab()
    {
        if (_tabs.Count <= 1 || _activeTab == null) return;
        int idx = _tabs.IndexOf(_activeTab);
        SelectTab(_tabs[(idx + 1) % _tabs.Count]);
    }

    private void SwitchToPreviousTab()
    {
        if (_tabs.Count <= 1 || _activeTab == null) return;
        int idx = _tabs.IndexOf(_activeTab);
        SelectTab(_tabs[idx <= 0 ? _tabs.Count - 1 : idx - 1]);
    }

    private void OnNewTabClick(object? sender, RoutedEventArgs e)
    {
        // Sized by the terminal, not by a literal: the profile knows its own screen.
        var (startWidth, startHeight) = EmulatorFactory.GetRecommendedSize("VT100");
        var tab = CreateTab("VT100", startWidth, startHeight);
        ShowWelcomeMessage(tab);
    }

    /// <summary>
    /// How many tabs are open. For tests, which should not reach into private fields.
    /// </summary>
    internal int OpenTabCount => _tabs.Count;

    /// <summary>
    /// Adds a tab carrying a given host string, for tests that need the tab list populated.
    /// </summary>
    /// <param name="host">
    /// The host the tab reports — a serial tab carries something like "COM11 (115200bps)",
    /// which is what the port-exclusivity check matches against.
    /// </param>
    /// <returns>
    /// The new tab, so the test can connect its session to an in-memory connection.
    /// </returns>
    /// <remarks>
    /// A seam rather than a public setter on the tab list: tests must be able to build the
    /// state without reaching into private fields, and nothing in the app needs to add a tab
    /// with a host and no connection.
    /// </remarks>
    internal TabSession AddTabForTesting(string host)
    {
        // Sized by the terminal, not by a literal: the profile knows its own screen.
        var (startWidth, startHeight) = EmulatorFactory.GetRecommendedSize("VT100");
        var tab = CreateTab("VT100", startWidth, startHeight);
        tab.Host = host;

        // The header's text is only ever refreshed by this event - CreateTab built it while Host
        // was still null, so without this the strip keeps saying "RetroTerm" while Title says the
        // hostname. The real connect path raises it for the same reason; a test seam that skips it
        // renders a strip no user would ever see.
        tab.NotifyTitleChanged();
        return tab;
    }

    /// <summary>
    /// Writes the disconnection notice onto a tab, for the test that pins its wording.
    /// </summary>
    /// <param name="tab">
    /// The tab to write on.
    /// </param>
    /// <param name="message">
    /// The reason to show.
    /// </param>
    internal void ShowDisconnectionNoticeForTesting(TabSession tab, string message)
        => ShowDisconnectionNotice(tab, message);

    /// <summary>
    /// Makes a tab the active one, for a test that needs <c>OnWindowKeyDown</c> to see it as
    /// <c>_activeTab</c> - the Enter-reconnects-a-disconnected-tab case, specifically.
    /// </summary>
    /// <param name="tab">
    /// The tab to select.
    /// </param>
    internal void SelectTabForTesting(TabSession tab) => SelectTab(tab);

    /// <summary>
    /// Starts the SSH connect path for a tab, which with no saved password shows the in-terminal
    /// login prompt and touches no network until a password line is entered. Test seam for the
    /// prompt, which had no test at all until 27 September 2026.
    /// </summary>
    /// <param name="tab">
    /// The tab to prompt in.
    /// </param>
    /// <param name="parameters">
    /// SSH parameters; leave the password null to get the prompt.
    /// </param>
    /// <returns>
    /// A task that completes once the prompt is on screen.
    /// </returns>
    internal Task BeginSshConnectForTesting(TabSession tab, ConnectionFactory.ConnectionParameters parameters)
        => ConnectSSHAsync(tab, parameters);

    /// <summary>
    /// Whether the tab is currently showing the in-terminal SSH login prompt.
    /// </summary>
    internal bool IsInSshLoginPromptForTesting(TabSession tab) => _sshLoginTabs.ContainsKey(tab.Id);

    /// <summary>
    /// The status bar's current text, for a test that needs to see the result of an attempted
    /// reconnect without waiting on real network or serial I/O to settle.
    /// </summary>
    internal string? StatusTextForTesting => _statusText?.Text;

    /// <summary>
    /// Pops a tab out the real way, for a test that needs the resulting window without going
    /// through <see cref="TerminalPopoutWindow"/>'s constructor directly — that constructor
    /// reparents <c>tab.Control</c>, which throws if the control still has a visual parent in
    /// this window's own tab content area.
    /// </summary>
    /// <param name="tab">
    /// The tab to pop out. Must be attached to this window (e.g. from
    /// <see cref="AddTabForTesting"/>).
    /// </param>
    /// <returns>
    /// The popout window <see cref="PopOutTab"/> created and showed.
    /// </returns>
    internal TerminalPopoutWindow PopOutTabForTesting(TabSession tab)
    {
        PopOutTab(tab);
        return _popoutWindows[_popoutWindows.Count - 1];
    }

    /// <summary>
    /// The terminal type running in the active tab, or null when there is no tab.
    /// </summary>
    /// <remarks>
    /// The PROFILE's name rather than the string that was asked for, so this answers what the tab
    /// actually got - which is the question worth asking after the connection dialog spent months
    /// offering three terminals out of thirteen.
    /// </remarks>
    internal string? ActiveTabEmulatorName
        => (_activeTab?.Session?.Emulator as TerminalEmulatorBase)?.Profile.Name;

    /// <summary>
    /// Fills File → New Tab As with every terminal the program can build.
    /// </summary>
    /// <remarks>
    /// Built from <see cref="EmulatorFactory.AvailableEmulators"/> rather than written out, for the
    /// same reason the connection dialog is: a typed-in copy of that list is how ten terminals came
    /// to exist with no way to reach them. Plain New Tab still opens a VT100, because that is what
    /// Ctrl+T has always done and muscle memory should not change under anyone.
    ///
    /// This is the way to try a terminal WITHOUT saving a connection - before it, choosing a VT420
    /// meant creating and storing a connection profile first.
    /// </remarks>
    private void BuildNewTabAsSubmenu()
    {
        var parent = this.FindControl<MenuItem>("NewTabAsMenuItem");
        if (parent == null)
        {
            return;
        }

        var items = new List<MenuItem>();
        var names = EmulatorFactory.AvailableEmulators;
        for (int i = 0; i < names.Length; i++)
        {
            var name = names[i];      // captured per iteration, not shared across the loop
            var item = new MenuItem { Header = name, Padding = new Thickness(8, 6) };
            item.Click += (_, _) =>
            {
                // The terminal's own screen size - a TDV picked here used to open at 80 by 24 and
                // lose its 25th row, because this was the one path that never asked the factory.
                var (width, height) = EmulatorFactory.GetRecommendedSize(name);
                var tab = CreateTab(name, width, height);
                ShowWelcomeMessage(tab);
            };
            items.Add(item);
        }

        parent.ItemsSource = items;
    }

    /// <summary>
    /// Points a tab's control at its session's new emulator after a terminal-type change.
    /// </summary>
    /// <remarks>
    /// The canvas holds a renderer, a keyboard mapper and a selection manager built FROM the
    /// emulator, so all of them have to be rebuilt against the new one - which is exactly what
    /// SetEmulator does, and why it tears the old renderer down before replacing it.
    /// </remarks>
    /// <param name="tab">
    /// The tab whose session changed.
    /// </param>
    /// <param name="replacement">
    /// The emulator now in the session.
    /// </param>
    private void OnTabEmulatorChanged(TabSession tab, TerminalEmulatorBase replacement)
    {
        // The event comes off the session pump, and everything below touches controls.
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            tab.Control.SetEmulator(replacement);
            tab.EmulatorType = replacement.Profile.Name;
            tab.NotifyTitleChanged();

            if (ReferenceEquals(tab, _activeTab))
            {
                UpdateStatusDisplay();
            }
        });
    }

    /// <summary>
    /// Fills Terminal → Emulation with every terminal type, changing the ACTIVE tab in place.
    /// </summary>
    /// <remarks>
    /// The type a host really wants is usually discovered after logging in, and until now correcting
    /// it meant disconnecting: the connect path replaced the whole tab when the type differed. This
    /// changes the terminal underneath a live connection instead.
    /// Built from the same list as New Tab As, and it goes through the same command as the script and
    /// MCP surfaces, so all three agree about what a change does.
    /// </remarks>
    private void BuildEmulationSubmenu()
    {
        var parent = this.FindControl<MenuItem>("EmulationMenuItem");
        if (parent == null)
        {
            return;
        }

        var items = new List<MenuItem>();
        var names = EmulatorFactory.AvailableEmulators;
        for (int i = 0; i < names.Length; i++)
        {
            var name = names[i];      // captured per iteration, not shared across the loop
            var item = new MenuItem { Header = name, Padding = new Thickness(8, 6) };
            item.Click += (_, _) => _ = ChangeActiveTabEmulationAsync(name);
            items.Add(item);
        }

        parent.ItemsSource = items;
    }

    /// <summary>
    /// Changes the active tab's terminal type, keeping its connection and its screen.
    /// </summary>
    /// <param name="emulatorType">
    /// The terminal to become.
    /// </param>
    /// <returns>
    /// A task that completes once the change is done and the status bar has been told.
    /// </returns>
    private async Task ChangeActiveTabEmulationAsync(string emulatorType)
    {
        var tab = _activeTab;
        if (tab == null) return;

        try
        {
            // Through the command registry, not around it. The menu, the .rts script command and the
            // MCP tool are three ways into one behaviour, and a rule implemented at the call site is
            // the two-surface trap this repository keeps a note about.
            var args = new Core.Commands.CommandArgs();
            args.Set("type", emulatorType);

            var result = await GetOrCreateCommandRegistry()
                .ExecuteAsync("EMULATION", tab.Session, args)
                .ConfigureAwait(true);

            UpdateStatus(result.Success
                ? result.Output ?? $"terminal is now {emulatorType}"
                : $"Could not change terminal: {result.Error}");
        }
        catch (Exception ex)
        {
            UpdateStatus($"Could not change terminal: {ex.Message}");
        }
    }

    private void OnCloseTabClick(object? sender, RoutedEventArgs e)
    {
        if (_activeTab != null)
        {
            CloseTab(_activeTab);
        }
    }

    private void OnCloseAllTabsClick(object? sender, RoutedEventArgs e)
    {
        CloseAllTabs();
    }

    // ───────────────────────────────────────────────────────────────────
    // Search
    // ───────────────────────────────────────────────────────────────────

    private void ToggleSearchBar()
    {
        if (_searchBarVisible)
            HideSearchBar();
        else
            ShowSearchBar();
    }

    private void ShowSearchBar()
    {
        if (_searchBar != null)
        {
            _searchBar.IsVisible = true;
            _searchTextBox?.Focus();
            _searchBarVisible = true;
            return;
        }

        var dockPanel = this.FindControl<DockPanel>("MainDockPanel");
        if (dockPanel == null) return;

        _searchBar = new Border
        {
            Background = new SolidColorBrush(Color.Parse("#2D2D30")),
            BorderBrush = new SolidColorBrush(Color.Parse("#3C3C3C")),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(12, 8)
        };
        DockPanel.SetDock(_searchBar, Dock.Top);

        var searchPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8
        };

        searchPanel.Children.Add(new TextBlock
        {
            Text = "Find:",
            Foreground = new SolidColorBrush(Color.Parse("#CCCCCC")),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0)
        });

        _searchTextBox = new TextBox
        {
            Width = 300,
            Background = new SolidColorBrush(Color.Parse("#1E1E1E")),
            Foreground = new SolidColorBrush(Color.Parse("#CCCCCC")),
            BorderBrush = new SolidColorBrush(Color.Parse("#3C3C3C")),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(8, 4)
        };
        _searchTextBox.TextChanged += OnSearchTextChanged;
        _searchTextBox.KeyDown += (s, e) =>
        {
            if (e.Key == Key.Enter) { FindNext(); e.Handled = true; }
            else if (e.Key == Key.Escape) { HideSearchBar(); e.Handled = true; }
        };
        searchPanel.Children.Add(_searchTextBox);

        _searchMatchCount = new TextBlock
        {
            Foreground = new SolidColorBrush(Color.Parse("#999999")),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 0, 0)
        };
        searchPanel.Children.Add(_searchMatchCount);

        var buttonPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 4,
            HorizontalAlignment = HorizontalAlignment.Right
        };

        // The Secondary class: these three were bare Fluent buttons until 27 September 2026.
        var prevButton = new Button { Content = "\u2191 Prev", Classes = { "Secondary" }, Padding = new Thickness(0), Width = 70, Height = 28, Margin = new Thickness(8, 0, 0, 0) };
        prevButton.Click += (s, e) => FindPrevious();
        buttonPanel.Children.Add(prevButton);

        var nextButton = new Button { Content = "Next \u2193", Classes = { "Secondary" }, Padding = new Thickness(0), Width = 70, Height = 28 };
        nextButton.Click += (s, e) => FindNext();
        buttonPanel.Children.Add(nextButton);

        var closeButton = new Button { Content = "\u2715", Classes = { "Secondary" }, Padding = new Thickness(0), Width = 28, Height = 28, Margin = new Thickness(8, 0, 0, 0) };
        closeButton.Click += (s, e) => HideSearchBar();
        buttonPanel.Children.Add(closeButton);

        searchPanel.Children.Add(buttonPanel);
        _searchBar.Child = searchPanel;

        dockPanel.Children.Insert(0, _searchBar);
        _searchBarVisible = true;
        _searchTextBox.Focus();

        if (_activeTab != null)
        {
            _search = new ScrollbackSearch(_activeTab.Session.Emulator.GetBuffer());
        }
    }

    private void HideSearchBar()
    {
        if (_searchBar != null)
        {
            _searchBar.IsVisible = false;
            _searchBarVisible = false;
            _searchMatches = null;
            _currentMatchIndex = -1;
            _activeTab?.Control.SetSearchMatches(null, -1);
            _activeTab?.Control.Focus();
        }
    }

    private void OnSearchTextChanged(object? sender, TextChangedEventArgs e)
    {
        if (_search == null || _searchTextBox == null) return;

        var pattern = _searchTextBox.Text ?? "";
        if (string.IsNullOrEmpty(pattern))
        {
            _searchMatches = new List<SearchMatch>();
            _currentMatchIndex = -1;
            UpdateSearchMatchCount();
            _activeTab?.Control.SetSearchMatches(null, -1);
            return;
        }

        var options = new SearchOptions { CaseSensitive = false, WholeWord = false, UseRegex = false, SearchUp = false };
        _searchMatches = _search.Search(pattern, options);
        _currentMatchIndex = _searchMatches.Count > 0 ? 0 : -1;
        UpdateSearchMatchCount();
        _activeTab?.Control.SetSearchMatches(_searchMatches, _currentMatchIndex);
    }

    private void FindNext()
    {
        if (_searchMatches == null || _searchMatches.Count == 0) return;
        _currentMatchIndex = (_currentMatchIndex + 1) % _searchMatches.Count;
        UpdateSearchMatchCount();
        // Scroll BEFORE handing the matches over: the scroll asks for a new frame, and the
        // highlight is resolved against the frame the renderer is given.
        _activeTab?.Control.ScrollToSearchRow(_searchMatches[_currentMatchIndex].Row);
        _activeTab?.Control.SetSearchMatches(_searchMatches, _currentMatchIndex);
    }

    private void FindPrevious()
    {
        if (_searchMatches == null || _searchMatches.Count == 0) return;
        _currentMatchIndex = _currentMatchIndex <= 0 ? _searchMatches.Count - 1 : _currentMatchIndex - 1;
        UpdateSearchMatchCount();
        _activeTab?.Control.ScrollToSearchRow(_searchMatches[_currentMatchIndex].Row);
        _activeTab?.Control.SetSearchMatches(_searchMatches, _currentMatchIndex);
    }

    private void UpdateSearchMatchCount()
    {
        if (_searchMatchCount == null) return;
        _searchMatchCount.Text = (_searchMatches == null || _searchMatches.Count == 0)
            ? ""
            : $"{_currentMatchIndex + 1} of {_searchMatches.Count}";
    }

    // ───────────────────────────────────────────────────────────────────
    // Welcome Message
    // ───────────────────────────────────────────────────────────────────

    private void ShowWelcomeMessage(TabSession tab)
    {
        tab.Session.WriteToTerminal(WelcomeText());
    }

    /// <summary>
    /// The welcome screen a new tab shows: the logo, what this program emulates and speaks, and
    /// which build this is, read from the assembly stamp so it cannot drift from Help, About.
    /// </summary>
    /// <remarks>
    /// Until 28 September 2026 this named four terminals of the fourteen, promised TN3270 "coming
    /// soon" (it is not started, and the README says why), and printed a hand-typed
    /// "1.0.0-alpha (Phase 4 - Canvas Rendering)" beside a hand-typed copyright year. Pinned by
    /// WelcomeScreenTests.
    /// </remarks>
    /// <returns>
    /// The whole screen as one string, carriage returns and line feeds included.
    /// </returns>
    internal static string WelcomeText()
    {
        return @"

 ██████╗ ███████╗████████╗██████╗  ██████╗ ████████╗███████╗██████╗ ███╗   ███╗
 ██╔══██╗██╔════╝╚══██╔══╝██╔══██╗██╔═══██╗╚══██╔══╝██╔════╝██╔══██╗████╗ ████║
 ██████╔╝█████╗     ██║   ██████╔╝██║   ██║   ██║   █████╗  ██████╔╝██╔████╔██║
 ██╔══██╗██╔══╝     ██║   ██╔══██╗██║   ██║   ██║   ██╔══╝  ██╔══██╗██║╚██╔╝██║
 ██║  ██║███████╗   ██║   ██║  ██║╚██████╔╝   ██║   ███████╗██║  ██║██║ ╚═╝ ██║
 ╚═╝  ╚═╝╚══════╝   ╚═╝   ╚═╝  ╚═╝ ╚═════╝    ╚═╝   ╚══════╝╚═╝  ╚═╝╚═╝     ╚═╝

 A modern terminal emulator with vintage soul
 ──────────────────────────────────────────────────────────────────────────────

 Terminal Emulation:  TDV2200, TDV2215, TDV1200, VT52, VT100, VT102, VT220, VT240,
                      VT320, VT340, VT420, xterm, xterm-256color, Tektronix 4014
 Protocols:           Telnet, SSH, serial, ND-100 gateway
 Graphics:            Sixel, ReGIS, Tektronix vectors, TDV2200 graphics planes
 Features:            Virtual TDV keyboard, scripts, an MCP server for an LLM,
                      Kermit file transfer, scrollback, authentic CRT colours

 RetroTerm " + BuildInfo.Version + ", commit " + BuildIdentity.Commit + (BuildIdentity.IsDirty ? " (uncommitted changes)" : "") + @"
 Build: " + BuildInfo.BuildDateTimeString + @"
 " + BuildInfo.Copyright + @"


";
    }

    // ───────────────────────────────────────────────────────────────────
    // Connect / Disconnect
    // ───────────────────────────────────────────────────────────────────

    private async void OnQuickConnectClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            await _configManager.LoadAsync();

            var dialog = new QuickConnectWindow(_configManager);
            var parameters = await dialog.ShowDialog<ConnectionFactory.ConnectionParameters?>(this);

            if (parameters != null)
            {
                await ConnectWithParameters(parameters);
            }
        }
        catch (Exception ex)
        {
            UpdateStatus($"Connection Error: {ex.Message}");
            System.Diagnostics.Debug.WriteLine($"Quick connect error: {ex}");
        }
    }

    private async void OnManageConnectionsClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            await _configManager.LoadAsync();

            var dialog = new ManageConnectionsWindow(_configManager, _gatewayListener);
            var parameters = await dialog.ShowDialog<ConnectionFactory.ConnectionParameters?>(this);

            // Rebuild favorites whenever the manager closes (may have changed)
            BuildFavoritesSubmenu();

            if (parameters != null)
            {
                await ConnectWithParameters(parameters);
            }
        }
        catch (Exception ex)
        {
            UpdateStatus($"Connection Error: {ex.Message}");
            System.Diagnostics.Debug.WriteLine($"Manage connections error: {ex}");
        }
    }

    private async void OnReconnectClick(object? sender, RoutedEventArgs e)
    {
        if (_activeTab == null || _activeTab.LastConnectionParameters == null)
            return;

        if (_activeTab.IsConnected)
            return;

        try
        {
            await ConnectWithParameters(_activeTab.LastConnectionParameters);
        }
        catch (Exception ex)
        {
            UpdateStatus($"Reconnect Error: {ex.Message}");
            System.Diagnostics.Debug.WriteLine($"Reconnect error: {ex}");
        }
    }

    /// <summary>
    /// Whether a serial port this application is ALREADY holding would be opened twice.
    /// </summary>
    /// <param name="parameters">
    /// The connection about to be made. Anything that is not serial always passes.
    /// </param>
    /// <returns>
    /// Null when the connection may go ahead, or a sentence naming the port and saying it
    /// is already open here.
    /// </returns>
    /// <remarks>
    /// <para><b>A serial port is exclusive, and the second open fails ugly</b></para>
    /// Windows hands a COM port to one owner. A second <c>SerialPort.Open</c> on a port this
    /// same process already holds throws "Access to the path 'COM11' is denied", which reads
    /// like a permissions problem on the device and is nothing of the kind.
    /// <para><b>Why this is its own method: the two-surface trap</b></para>
    /// This check used to live inside <c>ConnectWithParameters</c>, which is the UI path only.
    /// MCP's <c>terminal_open</c> goes through <c>OpenMcpSessionAsync</c> straight to
    /// <c>ConnectToHostCore</c> and never saw it. That was harmless only for as long as MCP
    /// could not open a serial port at all; the moment it could (30 August 2026), one MCP call
    /// could take the console port out from under a tab that was talking to a live machine, and
    /// the person at the keyboard got the denial message with no idea what had caused it.
    /// So the rule lives here and BOTH surfaces ask it, which is the same lesson this
    /// repository already wrote down about SEND and about connection creation.
    /// <para><b>UI thread only</b></para>
    /// It reads the tab and pop-out lists, which are UI-thread state.
    /// </remarks>
    internal string? SerialPortAlreadyInUse(ConnectionFactory.ConnectionParameters parameters)
    {
        if (parameters.Protocol != ConnectionFactory.ProtocolType.Serial)
        {
            return null;
        }

        string? portName = parameters.PortName;

        for (int i = 0; i < _tabs.Count; i++)
        {
            if (_tabs[i].IsConnected && IsSerialPort(_tabs[i].Host, portName))
            {
                return $"Serial port {portName} is already in use by another tab";
            }
        }

        for (int i = 0; i < _popoutWindows.Count; i++)
        {
            if (_popoutWindows[i].TabSession.IsConnected &&
                IsSerialPort(_popoutWindows[i].TabSession.Host, portName))
            {
                return $"Serial port {portName} is already in use by another window";
            }
        }

        return null;
    }

    private async Task ConnectWithParameters(ConnectionFactory.ConnectionParameters parameters)
    {
        // Serial port exclusivity — shared with the MCP open path, see SerialPortAlreadyInUse.
        var portClash = SerialPortAlreadyInUse(parameters);
        if (portClash != null)
        {
            // No modal dialogs for connection problems — status bar only.
            UpdateStatus(portClash);
            return;
        }

        // Gateway: no pre-selection needed — ConnectGatewayAsync shows in-terminal menu
        // (or auto-connects if GatewaySelectFirstFree is set)

        // Determine which tab to use — same rule for UI and MCP connects.
        var (tab, _) = GetOrCreateTabForNewConnection(parameters.EmulatorType, parameters.Width, parameters.Height);

        if (parameters.Protocol == ConnectionFactory.ProtocolType.Gateway)
            await ConnectGatewayAsync(tab, parameters);
        else if (parameters.Protocol == ConnectionFactory.ProtocolType.SSH)
            await ConnectSSHAsync(tab, parameters);
        else
            await ConnectToHost(tab, parameters);

        UpdateStatusDisplay();
        UpdateTabStatusDot(tab);
    }

    private void BuildFavoritesSubmenu()
    {
        var favMenuItem = this.FindControl<MenuItem>("FavoritesMenuItem");
        if (favMenuItem == null) return;

        favMenuItem.Items.Clear();

        var configs = _configManager.Configurations;
        bool hasFavorites = false;

        for (int i = 0; i < configs.Count; i++)
        {
            if (!configs[i].IsFavorite) continue;

            hasFavorites = true;
            var config = configs[i];
            var menuItem = new MenuItem
            {
                Header = config.Name,
                Padding = new Thickness(8, 6),
                Tag = config.Id
            };
            menuItem.Click += OnFavoriteMenuItemClick;
            favMenuItem.Items.Add(menuItem);
        }

        if (!hasFavorites)
        {
            favMenuItem.Items.Add(new MenuItem
            {
                Header = "(No favorites)",
                IsEnabled = false,
                Padding = new Thickness(8, 6)
            });
        }
    }

    private async void OnFavoriteMenuItemClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem menuItem || menuItem.Tag is not string configId)
            return;

        var configs = _configManager.Configurations;
        HostConfiguration? config = null;
        for (int i = 0; i < configs.Count; i++)
        {
            if (configs[i].Id == configId)
            {
                config = configs[i];
                break;
            }
        }

        if (config == null) return;

        try
        {
            var parameters = config.ToConnectionParameters();
            config.MarkAsUsed();
            _ = _configManager.UpdateAsync(config);

            await ConnectWithParameters(parameters);
        }
        catch (Exception ex)
        {
            UpdateStatus($"Connection Error: {ex.Message}");
            System.Diagnostics.Debug.WriteLine($"Favorite connect error: {ex}");
        }
    }

    /// <summary>
    /// Whether a new connection may take over the active tab instead of opening another one.
    /// </summary>
    /// <param name="activeTab">
    /// The tab currently on screen, or null when there is none.
    /// </param>
    /// <param name="callerCanSeeTheTab">
    /// True when a person clicked Connect and is looking at the tab; false for a remote caller such
    /// as the MCP tools or a script, which cannot see which tab is active.
    /// </param>
    /// <returns>
    /// True to reuse <paramref name="activeTab"/>, false to build a new one.
    /// </returns>
    /// <remarks>
    /// <para><b>Why the two callers differ, when one behaviour in one place is the rule here</b></para>
    /// They do not differ in the RULE. They differ in one fact: whether the caller can see what it is
    /// about to overwrite. A person clicking Connect on a disconnected tab is pointing at it and means
    /// that tab - reusing it is the whole point, and piling a second tab beside the welcome screen
    /// would be wrong. A remote caller has no idea which tab is active or what is on it.
    /// <para><b>What this cost, 27 August 2026</b></para>
    /// One <c>terminal_open</c> aimed at a test server took over a tab that had been holding a
    /// SINTRAN session on D100. The tab was disconnected at that moment, so the old rule reused it,
    /// and the caller had no way to know. Ronny had said not to touch those sessions.
    /// So: a remote caller reuses only a tab that has NEVER held a connection - a true welcome tab,
    /// which is exactly what <c>LastConnectionParameters</c> being null means, and is the same
    /// discriminator the script "Run on" picker already uses.
    /// </remarks>
    internal static bool CanReuseTabForNewConnection(TabSession? activeTab, bool callerCanSeeTheTab)
    {
        if (activeTab == null) return false;
        if (activeTab.IsConnected) return false;

        if (callerCanSeeTheTab) return true;

        // Never connected means nothing on it but the welcome message, so nothing to lose.
        return activeTab.LastConnectionParameters == null;
    }

    /// <summary>
    /// The tab a NEW connection should use — ONE rule shared by the UI connect paths
    /// and MCP terminal_open: reuse the active tab when it is NOT connected (that is
    /// the welcome tab, or a tab whose session died) instead of leaving blank tabs
    /// behind; otherwise create a fresh tab. UI thread only.
    /// Returns whether the tab was freshly created — a failed MCP connect must only
    /// close a tab it created itself, never the reused welcome tab.
    /// </summary>
    private (TabSession Tab, bool Created) GetOrCreateTabForNewConnection(string emulatorType, int width, int height,
        bool callerCanSeeTheTab = true)
    {
        if (CanReuseTabForNewConnection(_activeTab, callerCanSeeTheTab))
        {
            return (_activeTab!, false);
        }
        return (CreateTab(emulatorType, width, height), true);
    }

    private static bool IsSerialPort(string? tabHost, string? portName)
    {
        // A tab with no host, or a connection with no port name, can never be a
        // match - guard both before comparing.
        if (string.IsNullOrEmpty(tabHost) || string.IsNullOrEmpty(portName)) return false;
        return tabHost.StartsWith(portName, StringComparison.OrdinalIgnoreCase);
    }

    private async Task ConnectToHost(TabSession tab, ConnectionFactory.ConnectionParameters parameters)
    {
        try
        {
            await ConnectToHostCore(tab, parameters);
        }
        catch (Exception ex)
        {
            // No modal dialogs for connection problems — the status bar carries the
            // reason (single line; inner exception appended when present).
            var errorMessage = ex.InnerException != null
                ? $"{ex.Message} — {ex.InnerException.Message}"
                : ex.Message;
            UpdateStatus($"Connection failed: {errorMessage}");
        }
    }

    /// <summary>
    /// The one connect pipeline (emulator swap, language variant, colors, Kermit
    /// auto-detect, on-connect hook). THROWS on failure — the UI wrapper above turns
    /// that into a status-bar line, the MCP path turns it into a tool error.
    /// Returns the tab that ended up connected: an emulator mismatch REPLACES the
    /// tab, so the caller must not keep using the one it passed in.
    /// </summary>
    private async Task<TabSession> ConnectToHostCore(TabSession tab, ConnectionFactory.ConnectionParameters parameters)
    {
        {
            UpdateStatus($"Connecting to {parameters.DisplayName}...");

            if (tab.Session.IsConnected)
            {
                await tab.Session.DisconnectAsync();
            }

            // Replace emulator if needed
            var newEmulator = CreateEmulatorFromParameters(parameters);

            if (newEmulator != null && newEmulator is TerminalEmulatorBase newEmulatorBase &&
                newEmulatorBase.GetType() != tab.Session.Emulator.GetType())
            {
                ApplicationLogger.Log($"[MainWindow.ConnectToHost] Replacing emulator from {tab.Session.Emulator.GetType().Name} to {newEmulatorBase.GetType().Name}");

                // Dispose the old tab properly
                bool wasActive = (tab == _activeTab);
                _tabs.Remove(tab);
                RemoveTabHeader(tab);
                if (wasActive && _tabContent != null)
                {
                    _tabContent.Content = null;
                    _activeTab = null;
                }

                if (tab.Session.IsConnected)
                {
                    try { await tab.Session.DisconnectAsync(); }
                    catch { /* ignore */ }
                }
                tab.Dispose();

                // Create fresh tab with the right emulator
                tab = CreateTab(parameters.EmulatorType, parameters.Width, parameters.Height);
            }
            else
            {
                tab.Session.Emulator.Reset();
            }

            var connection = ConnectionFactory.CreateConnection(parameters);
            await tab.Session.ConnectAsync(connection);

            tab.Host = parameters.DisplayName;
            tab.EmulatorType = parameters.EmulatorType;
            tab.LastConnectionParameters = parameters;

            // Drive the emulator's ISO 646 national variant from the selected connection
            // language. This is the single source of truth for both the display font and the
            // keyboard's national-character conversion. A host ESC % command can still override it.
            ApplyLanguageVariant(tab, parameters.Language);

            tab.NotifyTitleChanged();
            UpdateTabStatusDot(tab);

            // Wire Kermit auto-detect if enabled
            tab.Session.KermitAutoDetectEnabled = Themes.ThemeManager.Instance.KermitAutoDetectReceive;
            tab.Session.KermitSendInitDetected -= OnKermitSendInitDetected;
            tab.Session.KermitSendInitDetected += OnKermitSendInitDetected;

            // Apply per-connection terminal colors if configured
            if (parameters.ForegroundColor != null && parameters.BackgroundColor != null)
            {
                try
                {


                    tab.Control.SetTheme(BuildSavedConnectionTheme(parameters));
                }
                catch
                {
                    // Invalid color strings — ignore
                }
            }

            UpdateStatus($"Connected to {tab.Host}");
            UpdateStatusDisplay();
            tab.Control.Focus();

            // Stored-connection OnConnect hook (auto-login scripts and the like).
            FireOnConnectHook(tab);
        }
        return tab;
    }

    // ── Gateway In-Terminal Menu ──────────────────────────────────────

    /// <summary>
    /// Tracks state for a tab that is in gateway terminal menu mode.
    /// </summary>
    private sealed class GatewayMenuState
    {
        public ConnectionFactory.ConnectionParameters Parameters;
        public List<GatewayTerminalInfo> Terminals;
        public string InputBuffer = "";

        public GatewayMenuState(ConnectionFactory.ConnectionParameters p, List<GatewayTerminalInfo> t)
        {
            Parameters = p;
            Terminals = t;
        }
    }

    private string BuildGatewayMenuText(List<GatewayTerminalInfo> terminals)
    {
        var sb = new StringBuilder();
        sb.Append("\r\n");
        sb.Append("  ND-100/CX Terminal Server\r\n");
        sb.Append("  =========================\r\n");
        sb.Append("\r\n");

        if (terminals.Count == 0)
        {
            sb.Append("  No terminals available. Is the emulator connected?\r\n\r\n");
            sb.Append("  Press Enter to refresh, or 0 to disconnect.\r\n");
            return sb.ToString();
        }

        sb.Append("  Available terminals:\r\n\r\n");
        for (int i = 0; i < terminals.Count; i++)
        {
            var t = terminals[i];
            bool inUse = _gatewayListener != null && _gatewayListener.IsIdentCodeInUse(t.IdentCode);
            string num = (i + 1).ToString().PadLeft(2);
            string status = inUse ? " [in use]" : "";
            sb.Append($"  {num}. {t.Name}{status}\r\n");
        }
        sb.Append("\r\n   0. Disconnect\r\n");
        sb.Append("\r\n  Select terminal (1-");
        sb.Append(terminals.Count);
        sb.Append(", Enter=first free, 0=quit): ");
        return sb.ToString();
    }

    private async Task ConnectGatewayAsync(TabSession tab, ConnectionFactory.ConnectionParameters parameters)
    {
        if (_gatewayListener == null)
            return;

        try
        {
            if (tab.Session.IsConnected)
                await tab.Session.DisconnectAsync();

            // Replace emulator if needed
            var newEmulator = CreateEmulatorFromParameters(parameters);
            if (newEmulator != null && newEmulator is TerminalEmulatorBase newEmulatorBase &&
                newEmulatorBase.GetType() != tab.Session.Emulator.GetType())
            {
                bool wasActive = (tab == _activeTab);
                _tabs.Remove(tab);
                RemoveTabHeader(tab);
                if (wasActive && _tabContent != null)
                {
                    _tabContent.Content = null;
                    _activeTab = null;
                }

                if (tab.Session.IsConnected)
                {
                    try { await tab.Session.DisconnectAsync(); }
                    catch { /* ignore */ }
                }
                tab.Dispose();

                tab = CreateTab(parameters.EmulatorType, parameters.Width, parameters.Height);
            }
            else
            {
                tab.Session.Emulator.Reset();
            }

            tab.EmulatorType = parameters.EmulatorType;
            tab.LastConnectionParameters = parameters;

            var terminals = _gatewayListener.GetTerminals();

            // "Select first free" mode: show the menu, then auto-connect
            if (parameters.GatewaySelectFirstFree)
            {
                tab.Session.WriteToTerminal(BuildGatewayMenuText(terminals));

                // Find first free
                int freeIdentCode = -1;
                string freeName = "";
                for (int i = 0; i < terminals.Count; i++)
                {
                    if (!_gatewayListener.IsIdentCodeInUse(terminals[i].IdentCode))
                    {
                        freeIdentCode = terminals[i].IdentCode;
                        freeName = terminals[i].Name;
                        break;
                    }
                }

                if (freeIdentCode < 0)
                {
                    tab.Session.WriteToTerminal("\r\n\r\n  No free terminals available.\r\n");
                    UpdateStatus("Gateway: no free terminals");
                    return;
                }

                tab.Session.WriteToTerminal($"\r\n\r\n  Auto-connecting to {freeName}...\r\n\r\n");
                await FinishGatewayConnect(tab, parameters, freeIdentCode, freeName);
                return;
            }

            // Interactive menu mode: show menu and wait for input
            tab.Session.WriteToTerminal(BuildGatewayMenuText(terminals));
            _gatewayMenuTabs[tab.Id] = new GatewayMenuState(parameters, terminals);

            UpdateStatus("Gateway: select a terminal");
            tab.Control.Focus();
        }
        catch (Exception ex)
        {
            // No modal dialogs for connection problems — status bar only.
            var errorMessage = ex.InnerException != null
                ? $"{ex.Message} — {ex.InnerException.Message}"
                : ex.Message;
            UpdateStatus($"Gateway connection failed: {errorMessage}");
        }
    }

    private async Task FinishGatewayConnect(TabSession tab, ConnectionFactory.ConnectionParameters parameters,
        int identCode, string termName)
    {
        if (_gatewayListener == null) return;

        if (_gatewayListener.IsIdentCodeInUse(identCode))
        {
            tab.Session.WriteToTerminal($"\r\n  {termName} is already in use.\r\n");
            // Re-show menu
            var terminals = _gatewayListener.GetTerminals();
            tab.Session.WriteToTerminal(BuildGatewayMenuText(terminals));
            _gatewayMenuTabs[tab.Id] = new GatewayMenuState(parameters, terminals);
            return;
        }

        tab.Session.WriteToTerminal($"  Connected to {termName}\r\n\r\n");

        var connection = new GatewayConnection(_gatewayListener, identCode, termName);
        await tab.Session.ConnectAsync(connection);

        tab.Host = $"GW:{termName}";
        tab.LastConnectionParameters = parameters;
        tab.NotifyTitleChanged();
        UpdateTabStatusDot(tab);

        // Wire Kermit auto-detect if enabled
        tab.Session.KermitAutoDetectEnabled = Themes.ThemeManager.Instance.KermitAutoDetectReceive;
        tab.Session.KermitSendInitDetected -= OnKermitSendInitDetected;
        tab.Session.KermitSendInitDetected += OnKermitSendInitDetected;

        // Apply per-connection terminal colors if configured
        if (parameters.ForegroundColor != null && parameters.BackgroundColor != null)
        {
            try
            {


                tab.Control.SetTheme(BuildSavedConnectionTheme(parameters));
            }
            catch { }
        }

        UpdateStatus($"Connected to {termName} via Gateway");
        UpdateStatusDisplay();
        tab.Control.Focus();
    }

    private async void HandleGatewayMenuInput(TabSession tab, string input)
    {
        if (!_gatewayMenuTabs.TryGetValue(tab.Id, out var menuState))
            return;

        for (int ci = 0; ci < input.Length; ci++)
        {
            char c = input[ci];

            if (c == '\r' || c == '\n')
            {
                // Echo newline
                tab.Session.WriteToTerminal("\r\n");

                string line = menuState.InputBuffer.Trim();
                menuState.InputBuffer = "";

                if (line.Length == 0)
                {
                    // Enter alone = first free
                    _gatewayMenuTabs.Remove(tab.Id);

                    int freeIdentCode = -1;
                    string freeName = "";
                    for (int i = 0; i < menuState.Terminals.Count; i++)
                    {
                        if (_gatewayListener != null && !_gatewayListener.IsIdentCodeInUse(menuState.Terminals[i].IdentCode))
                        {
                            freeIdentCode = menuState.Terminals[i].IdentCode;
                            freeName = menuState.Terminals[i].Name;
                            break;
                        }
                    }

                    if (freeIdentCode < 0)
                    {
                        tab.Session.WriteToTerminal("\r\n  No free terminals available.\r\n");
                        // Re-show menu
                        var terminals2 = _gatewayListener?.GetTerminals() ?? menuState.Terminals;
                        tab.Session.WriteToTerminal(BuildGatewayMenuText(terminals2));
                        _gatewayMenuTabs[tab.Id] = new GatewayMenuState(menuState.Parameters, terminals2);
                        return;
                    }

                    try
                    {
                        await FinishGatewayConnect(tab, menuState.Parameters, freeIdentCode, freeName);
                    }
                    catch (Exception ex)
                    {
                        tab.Session.WriteToTerminal($"\r\n  Connection failed: {ex.Message}\r\n");
                        UpdateStatus($"Gateway connection failed: {ex.Message}");
                    }
                    return;
                }

                if (!int.TryParse(line, out int choice))
                {
                    tab.Session.WriteToTerminal("\r\n  Invalid selection. Try again.\r\n");
                    var terminals3 = _gatewayListener?.GetTerminals() ?? menuState.Terminals;
                    tab.Session.WriteToTerminal(BuildGatewayMenuText(terminals3));
                    menuState.Terminals = terminals3;
                    return;
                }

                if (choice == 0)
                {
                    // Disconnect
                    _gatewayMenuTabs.Remove(tab.Id);
                    tab.Session.WriteToTerminal("\r\n  Goodbye.\r\n");
                    UpdateStatus("Disconnected");
                    return;
                }

                if (choice < 1 || choice > menuState.Terminals.Count)
                {
                    tab.Session.WriteToTerminal("\r\n  Invalid selection. Try again.\r\n");
                    var terminals4 = _gatewayListener?.GetTerminals() ?? menuState.Terminals;
                    tab.Session.WriteToTerminal(BuildGatewayMenuText(terminals4));
                    menuState.Terminals = terminals4;
                    return;
                }

                // Valid choice
                _gatewayMenuTabs.Remove(tab.Id);
                var terminal = menuState.Terminals[choice - 1];

                try
                {
                    await FinishGatewayConnect(tab, menuState.Parameters, terminal.IdentCode, terminal.Name);
                }
                catch (Exception ex)
                {
                    tab.Session.WriteToTerminal($"\r\n  Connection failed: {ex.Message}\r\n");
                    UpdateStatus($"Gateway connection failed: {ex.Message}");
                }
                return;
            }
            else if (c == '\b' || c == 0x7F)
            {
                // Backspace
                if (menuState.InputBuffer.Length > 0)
                {
                    menuState.InputBuffer = menuState.InputBuffer.Substring(0, menuState.InputBuffer.Length - 1);
                    tab.Session.WriteToTerminal("\b \b");
                }
            }
            else if (c >= '0' && c <= '9')
            {
                menuState.InputBuffer += c;
                tab.Session.WriteToTerminal(c.ToString());
            }
            // Ignore other characters
        }
    }

    // ── SSH In-Terminal Login ──────────────────────────────────────────

    private sealed class SSHLoginState
    {
        public ConnectionFactory.ConnectionParameters Parameters;
        public string InputBuffer = "";
        public bool IsPasswordPhase;
        public string? Username;

        public SSHLoginState(ConnectionFactory.ConnectionParameters p) { Parameters = p; }
    }

    private async Task ConnectSSHAsync(TabSession tab, ConnectionFactory.ConnectionParameters parameters)
    {
        try
        {
            if (tab.Session.IsConnected)
                await tab.Session.DisconnectAsync();

            // Replace emulator if needed
            var newEmulator = CreateEmulatorFromParameters(parameters);
            if (newEmulator != null && newEmulator is TerminalEmulatorBase newEmulatorBase &&
                newEmulatorBase.GetType() != tab.Session.Emulator.GetType())
            {
                bool wasActive = (tab == _activeTab);
                _tabs.Remove(tab);
                RemoveTabHeader(tab);
                if (wasActive && _tabContent != null)
                {
                    _tabContent.Content = null;
                    _activeTab = null;
                }

                if (tab.Session.IsConnected)
                {
                    try { await tab.Session.DisconnectAsync(); }
                    catch { /* ignore */ }
                }
                tab.Dispose();
                tab = CreateTab(parameters.EmulatorType, parameters.Width, parameters.Height);
            }
            else
            {
                tab.Session.Emulator.Reset();
            }

            tab.EmulatorType = parameters.EmulatorType;
            tab.LastConnectionParameters = parameters;

            // If credentials are already provided, connect directly
            if (!string.IsNullOrEmpty(parameters.Username) && !string.IsNullOrEmpty(parameters.Password))
            {
                await FinishSSHConnectAsync(tab, parameters, parameters.Username, parameters.Password);
                return;
            }

            // In-terminal login prompt
            tab.Session.WriteToTerminal($"\r\n  SSH Login to {parameters.Host}:{parameters.Port}\r\n");
            tab.Session.WriteToTerminal("  ─────────────────────────────\r\n\r\n");

            var loginState = new SSHLoginState(parameters);

            // If username is already set, skip to password
            if (!string.IsNullOrEmpty(parameters.Username))
            {
                loginState.Username = parameters.Username;
                loginState.IsPasswordPhase = true;
                tab.Session.WriteToTerminal($"  Username: {parameters.Username}\r\n");
                tab.Session.WriteToTerminal("  Password: ");
            }
            else
            {
                tab.Session.WriteToTerminal("  Username: ");
            }

            _sshLoginTabs[tab.Id] = loginState;
            UpdateStatus($"SSH: enter credentials for {parameters.Host}");
            tab.Control.Focus();
        }
        catch (Exception ex)
        {
            UpdateStatus($"SSH connection failed: {ex.Message}");
        }
    }

    private async void HandleSSHLoginInput(TabSession tab, string input)
    {
        if (!_sshLoginTabs.TryGetValue(tab.Id, out var loginState))
            return;

        for (int ci = 0; ci < input.Length; ci++)
        {
            char c = input[ci];

            // Ctrl+C or Escape: cancel
            if (c == 0x03 || c == 0x1B)
            {
                _sshLoginTabs.Remove(tab.Id);
                tab.SuppressDisconnectDialog = false;
                tab.Session.WriteToTerminal("\r\n\r\n  Cancelled.\r\n");
                UpdateStatus("SSH login cancelled");
                return;
            }

            if (c == '\r' || c == '\n')
            {
                tab.Session.WriteToTerminal("\r\n");
                string line = loginState.InputBuffer;
                loginState.InputBuffer = "";

                if (!loginState.IsPasswordPhase)
                {
                    // Username entered
                    if (string.IsNullOrWhiteSpace(line))
                    {
                        tab.Session.WriteToTerminal("  Username: ");
                        return;
                    }
                    loginState.Username = line;
                    loginState.IsPasswordPhase = true;
                    tab.Session.WriteToTerminal("  Password: ");
                }
                else
                {
                    // Password entered — connect
                    _sshLoginTabs.Remove(tab.Id);
                    tab.Session.WriteToTerminal("\r\n");

                    try
                    {
                        await FinishSSHConnectAsync(tab, loginState.Parameters,
                            loginState.Username!, line);
                    }
                    catch (Exception ex)
                    {
                        string msg = ex.InnerException != null ? ex.InnerException.Message : ex.Message;

                        // Authentication failure: let user retry
                        if (msg.Contains("Authentication", StringComparison.OrdinalIgnoreCase)
                            || msg.Contains("denied", StringComparison.OrdinalIgnoreCase)
                            || msg.Contains("password", StringComparison.OrdinalIgnoreCase))
                        {
                            tab.Session.WriteToTerminal($"  Authentication failed.\r\n\r\n");
                            loginState.InputBuffer = "";
                            loginState.IsPasswordPhase = false;
                            loginState.Username = null;
                            tab.Session.WriteToTerminal("  Username: ");
                            _sshLoginTabs[tab.Id] = loginState;
                            UpdateStatus("SSH: authentication failed, try again");
                        }
                        else
                        {
                            tab.SuppressDisconnectDialog = false;
                            tab.Session.WriteToTerminal($"  Connection failed: {msg}\r\n");
                            UpdateStatus($"SSH connection failed: {msg}");
                        }
                    }
                }
                return;
            }
            else if (c == '\b' || c == 0x7F)
            {
                if (loginState.InputBuffer.Length > 0)
                {
                    loginState.InputBuffer = loginState.InputBuffer.Substring(
                        0, loginState.InputBuffer.Length - 1);
                    if (loginState.IsPasswordPhase)
                        tab.Session.WriteToTerminal("\b \b");
                    else
                        tab.Session.WriteToTerminal("\b \b");
                }
            }
            else if (c >= 0x20) // printable
            {
                loginState.InputBuffer += c;
                if (loginState.IsPasswordPhase)
                    tab.Session.WriteToTerminal("*"); // mask password
                else
                    tab.Session.WriteToTerminal(c.ToString()); // echo username
            }
        }
    }

    private async Task FinishSSHConnectAsync(TabSession tab,
        ConnectionFactory.ConnectionParameters parameters, string username, string password)
    {
        tab.Session.WriteToTerminal($"  Connecting to {parameters.Host}:{parameters.Port}...\r\n");
        UpdateStatus($"Connecting to {parameters.Host}:{parameters.Port} via SSH...");

        // Set credentials on parameters for ConnectionFactory
        parameters.Username = username;
        parameters.Password = password;

        var connection = ConnectionFactory.CreateConnection(parameters);
        if (connection is not RetroTerm.Core.Protocols.Net.SSHConnection sshConn)
            throw new InvalidOperationException("Failed to create SSH connection");

        // Set up host key validation with TCS to pause SSH thread while UI dialog runs
        var hostKeyManager = new RetroTerm.Core.Protocols.Net.SSHHostKeyManager();
        sshConn.HostKeyValidation = (host, hostKey) =>
        {
            var result = hostKeyManager.ValidateHostKey(host, parameters.Port, hostKey);
            if (result.IsValid) return true;

            // Unknown or changed key — must ask user on UI thread
            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            Dispatcher.UIThread.Post(async () =>
            {
                try
                {
                    var accepted = await SSHHostKeyDialog.ShowConfirmationAsync(
                        this, host, parameters.Port,
                        result.Fingerprint, result.FingerprintMD5,
                        result.KnownFingerprint);
                    if (accepted)
                        hostKeyManager.TrustHostKey(host, parameters.Port, hostKey);
                    tcs.SetResult(accepted);
                }
                catch
                {
                    tcs.SetResult(false);
                }
            });

            // Block SSH.NET's background thread until dialog completes
            // Safe: SSH connect runs on Task.Run thread, not UI thread
            return tcs.Task.GetAwaiter().GetResult();
        };

        // Suppress spurious disconnect dialogs during entire SSH login flow.
        // Only cleared on success — retries keep the flag true.
        tab.SuppressDisconnectDialog = true;
        try
        {
            await tab.Session.ConnectAsync(connection);

            tab.Host = $"{parameters.Host}:{parameters.Port}";
            tab.LastConnectionParameters = parameters;
            tab.NotifyTitleChanged();
            UpdateTabStatusDot(tab);

            // Wire Kermit auto-detect if enabled
            tab.Session.KermitAutoDetectEnabled = Themes.ThemeManager.Instance.KermitAutoDetectReceive;
            tab.Session.KermitSendInitDetected -= OnKermitSendInitDetected;
            tab.Session.KermitSendInitDetected += OnKermitSendInitDetected;

            // Apply per-connection terminal colors if configured
            if (parameters.ForegroundColor != null && parameters.BackgroundColor != null)
            {
                try
                {


                    tab.Control.SetTheme(BuildSavedConnectionTheme(parameters));
                }
                catch { }
            }

            tab.SuppressDisconnectDialog = false;
            UpdateStatus($"Connected to {tab.Host} (SSH)");
            UpdateStatusDisplay();
            tab.Control.Focus();
        }
        catch
        {
            // Do NOT clear SuppressDisconnectDialog here — the caller
            // (HandleSSHLoginInput) may retry, and async StatusChanged events
            // can arrive after this catch runs.
            // Clean up the session if it partially connected
            if (tab.Session.IsConnected)
            {
                try { await tab.Session.DisconnectAsync(); }
                catch { /* ignore */ }
            }
            throw; // re-throw for caller to handle
        }
    }

    private async void OnDisconnectClick(object? sender, RoutedEventArgs e)
    {
        if (_activeTab != null && _activeTab.Session.IsConnected)
        {
            try
            {
                StopSessionLogging(_activeTab);
                await _activeTab.Session.DisconnectAsync();
                _activeTab.Host = null;
                _activeTab.NotifyTitleChanged();
                UpdateTabStatusDot(_activeTab);
                UpdateStatus("Disconnected");
                UpdateStatusDisplay();
                UpdateMenuState();
            }
            catch (Exception ex)
            {
                UpdateStatus($"Disconnect error: {ex.Message}");
            }
        }
    }

    // ───────────────────────────────────────────────────────────────────
    // File Transfer
    // ───────────────────────────────────────────────────────────────────

    private KermitOptions BuildKermitOptionsFromPreferences()
    {
        var tm = Themes.ThemeManager.Instance;
        return new KermitOptions
        {
            Parity = (ParityMode)tm.KermitParity,
            Force8BitQuoting = tm.KermitForce8BitQuoting,
            Delay = tm.KermitDelay,
            Timeout = tm.KermitTimeout,
            MaxRetries = tm.KermitMaxRetries,
            BlockCheckType = Math.Max(1, tm.KermitBlockCheckType),
            MaxReceivePacketSize = tm.KermitPacketSize > 0 ? tm.KermitPacketSize : 80
        };
    }

    private FileCollisionMode GetFileCollisionFromPreferences()
    {
        return (FileCollisionMode)Themes.ThemeManager.Instance.KermitFileCollision;
    }

    private async void OnSendFileClick(object? sender, RoutedEventArgs e)
    {
        if (_activeTab == null || !_activeTab.Session.IsConnected) return;

        try
        {
            // Pick files
            var storageProvider = StorageProvider;
            if (storageProvider == null) return;

            var files = await storageProvider.OpenFilePickerAsync(new Avalonia.Platform.Storage.FilePickerOpenOptions
            {
                Title = "Select Files to Send",
                AllowMultiple = true
            });

            if (files == null || files.Count == 0) return;

            var paths = new string[files.Count];
            for (int i = 0; i < files.Count; i++)
            {
                paths[i] = files[i].Path.LocalPath;
            }

            var options = BuildKermitOptionsFromPreferences();
            var handler = new KermitFileTransfer(options);
            handler.FileCollision = GetFileCollisionFromPreferences();

            _transferProgressWindow = new FileTransferProgressWindow(_activeTab.Session, TransferDirection.Send);
            _transferProgressWindow.Show(this);

            _activeTab.Session.TransferStateChanged -= OnActiveTabTransferStateChanged;
            _activeTab.Session.TransferStateChanged += OnActiveTabTransferStateChanged;

            await _activeTab.Session.StartFileTransferAsync(
                handler, TransferDirection.Send, paths, CancellationToken.None);
        }
        catch (Exception ex)
        {
            UpdateStatus($"Send failed: {ex.Message}");
        }
    }

    private async void OnReceiveFileClick(object? sender, RoutedEventArgs e)
    {
        if (_activeTab == null || !_activeTab.Session.IsConnected) return;

        try
        {
            // Pick destination folder
            var storageProvider = StorageProvider;
            if (storageProvider == null) return;

            var folders = await storageProvider.OpenFolderPickerAsync(new Avalonia.Platform.Storage.FolderPickerOpenOptions
            {
                Title = "Select Save Directory",
                AllowMultiple = false
            });

            if (folders == null || folders.Count == 0) return;

            var folderPath = folders[0].Path.LocalPath;

            var options = BuildKermitOptionsFromPreferences();
            var handler = new KermitFileTransfer(options);
            handler.FileCollision = GetFileCollisionFromPreferences();

            _transferProgressWindow = new FileTransferProgressWindow(_activeTab.Session, TransferDirection.Receive);
            _transferProgressWindow.Show(this);

            _activeTab.Session.TransferStateChanged -= OnActiveTabTransferStateChanged;
            _activeTab.Session.TransferStateChanged += OnActiveTabTransferStateChanged;

            await _activeTab.Session.StartFileTransferAsync(
                handler, TransferDirection.Receive, new[] { folderPath }, CancellationToken.None);
        }
        catch (Exception ex)
        {
            UpdateStatus($"Receive failed: {ex.Message}");
        }
    }

    private void OnCancelTransferClick(object? sender, RoutedEventArgs e)
    {
        _activeTab?.Session.CancelFileTransfer();
    }

    private void OnKermitSendInitDetected()
    {
        Dispatcher.UIThread.Post(async () =>
        {
            if (_activeTab == null || !_activeTab.Session.IsConnected || _activeTab.Session.IsTransferActive)
                return;

            try
            {
                var storageProvider = StorageProvider;
                if (storageProvider == null) return;

                var folders = await storageProvider.OpenFolderPickerAsync(new Avalonia.Platform.Storage.FolderPickerOpenOptions
                {
                    Title = "Kermit auto-detect: Select Save Directory",
                    AllowMultiple = false
                });

                if (folders == null || folders.Count == 0) return;

                var folderPath = folders[0].Path.LocalPath;
                var options = BuildKermitOptionsFromPreferences();
                var handler = new KermitFileTransfer(options);
                handler.FileCollision = GetFileCollisionFromPreferences();

                _transferProgressWindow = new FileTransferProgressWindow(_activeTab.Session, TransferDirection.Receive);
                _transferProgressWindow.Show(this);

                _activeTab.Session.TransferStateChanged -= OnActiveTabTransferStateChanged;
                _activeTab.Session.TransferStateChanged += OnActiveTabTransferStateChanged;

                await _activeTab.Session.StartFileTransferAsync(
                    handler, TransferDirection.Receive, new[] { folderPath }, CancellationToken.None);
            }
            catch (Exception ex)
            {
                UpdateStatus($"Auto-detect receive failed: {ex.Message}");
            }
        });
    }

    private void OnActiveTabTransferStateChanged(bool active)
    {
        Dispatcher.UIThread.Post(() =>
        {
            UpdateTransferMenuState();
            UpdateTransferIndicator();
            UpdateKermitDebugStatistics();
        });
    }

    private void UpdateTransferMenuState()
    {
        bool isConnected = _activeTab != null && _activeTab.Session.IsConnected;
        bool isTransferring = _activeTab != null && _activeTab.Session.IsTransferActive;

        var sendItem = this.FindControl<MenuItem>("SendFileMenuItem");
        var receiveItem = this.FindControl<MenuItem>("ReceiveFileMenuItem");
        var cancelItem = this.FindControl<MenuItem>("CancelTransferMenuItem");

        if (sendItem != null) sendItem.IsEnabled = isConnected && !isTransferring;
        if (receiveItem != null) receiveItem.IsEnabled = isConnected && !isTransferring;
        if (cancelItem != null) cancelItem.IsEnabled = isTransferring;
    }

    private void UpdateTransferIndicator()
    {
        var indicator = this.FindControl<TextBlock>("TransferIndicator");
        if (indicator != null)
            indicator.IsVisible = _activeTab != null && _activeTab.Session.IsTransferActive;
    }

    // ───────────────────────────────────────────────────────────────────
    // Disconnection Dialog
    // ───────────────────────────────────────────────────────────────────

    /// <summary>
    /// Whether Connection - Reconnect can do anything for a tab.
    /// </summary>
    /// <param name="tab">
    /// The tab to ask about, or null when there is none.
    /// </param>
    /// <returns>
    /// True when the tab is disconnected and still remembers what it was connected to.
    /// </returns>
    /// <remarks>
    /// One rule, read by the menu AND by the message that points at the menu. They were two copies
    /// of the same condition until 31 August 2026, and they disagreed: a connect that FAILED never
    /// reached the line that stores the parameters, so Reconnect was greyed out while the notice on
    /// screen told the reader to use it. Telling somebody to click a disabled item is worse than
    /// telling them nothing.
    /// </remarks>
    internal static bool CanReconnect(TabSession? tab)
    {
        return tab != null
            && !tab.Session.IsConnected
            && tab.LastConnectionParameters != null;
    }

    /// <summary>
    /// Non-blocking replacement for the old "Connection Lost" modal dialog:
    /// the reason is written into the tab's own terminal and shown in the status
    /// bar when the tab is active. Nothing blocks — MCP clients and scripts learn
    /// about the drop through the session's ConnectionLost event, and a human sees
    /// it on the screen it belongs to.
    /// </summary>
    /// <param name="tab">
    /// The tab whose screen the notice is written on.
    /// </param>
    /// <param name="message">
    /// The reason the connection ended.
    /// </param>
    private void ShowDisconnectionNotice(TabSession tab, string message)
    {
        // The advice has to match what the menu will actually let the reader do. A dropped
        // connection can be reconnected; a connect that never succeeded has nothing to go back to,
        // and must be sent somewhere that works instead.
        string advice = CanReconnect(tab)
            ? "Reconnect with Connection \u2192 Reconnect."
            : "This tab has nothing to reconnect to \u2014 open the connection again from "
              + "Connection \u2192 Manage Connections, or Connection \u2192 Quick Connect.";

        tab.Session.WriteToTerminal($"\r\n\r\n  [{message}]\r\n  {advice}\r\n");
        if (tab == _activeTab)
        {
            UpdateStatus(message);
        }

        // The menu is re-evaluated here too, so what is greyed out agrees with what was just
        // printed rather than with whatever state the menu was last updated for.
        UpdateMenuState();
    }

    // ───────────────────────────────────────────────────────────────────
    // Status Display
    // ───────────────────────────────────────────────────────────────────

    private void UpdateStatus(string message)
    {
        if (_statusText != null) _statusText.Text = message;
    }

    private void UpdateStatusDisplay()
    {
        if (_connectionInfo != null)
        {
            var host = _activeTab?.Host;
            var emType = _activeTab?.EmulatorType;
            if (!string.IsNullOrEmpty(host) && !string.IsNullOrEmpty(emType))
            {
                _connectionInfo.Text = $"{host} | {emType}";
                _connectionInfo.IsVisible = true;
            }
            else
            {
                _connectionInfo.IsVisible = false;
            }
        }

        if (_modeIndicator != null && _activeTab != null)
        {
            bool is2115Mode = false;
            if (_activeTab.Session.Emulator is TDV1200Emulator tdv1200)
                is2115Mode = tdv1200.Is2115CompatibilityMode;
            else if (_activeTab.Session.Emulator is TDV2215Emulator tdv2215)
                is2115Mode = tdv2215.Is2115CompatibilityMode;
            else if (_activeTab.Session.Emulator is TDV2200Emulator tdv2200)
                is2115Mode = tdv2200.Is2115CompatibilityMode;

            _modeIndicator.Text = is2115Mode ? "\u26a0 2115 MODE" : "";
            _modeIndicator.IsVisible = is2115Mode;
        }
        else if (_modeIndicator != null)
        {
            _modeIndicator.IsVisible = false;
        }
    }

    private void UpdateMenuState()
    {
        var disconnectItem = this.FindControl<MenuItem>("DisconnectMenuItem");
        if (disconnectItem != null)
            disconnectItem.IsEnabled = _activeTab != null && _activeTab.Session.IsConnected;

        var reconnectItem = this.FindControl<MenuItem>("ReconnectMenuItem");
        if (reconnectItem != null)
            reconnectItem.IsEnabled = CanReconnect(_activeTab);

        UpdateTransferMenuState();
        UpdateTransferIndicator();
    }

    private void UpdateRecIndicator()
    {
        var recIndicator = this.FindControl<TextBlock>("RecIndicator");
        if (recIndicator != null)
            recIndicator.IsVisible = _activeTab?.Logger != null && _activeTab.Logger.IsLogging;

        var startItem = this.FindControl<MenuItem>("StartSessionLogMenuItem");
        var stopItem = this.FindControl<MenuItem>("StopSessionLogMenuItem");
        if (startItem != null && stopItem != null)
        {
            bool isLogging = _activeTab?.Logger != null && _activeTab.Logger.IsLogging;
            startItem.IsEnabled = !isLogging;
            stopItem.IsEnabled = isLogging;
        }
    }

    // ───────────────────────────────────────────────────────────────────
    // Session Logging (per-tab)
    // ───────────────────────────────────────────────────────────────────

    private async void OnStartSessionLogClick(object? sender, RoutedEventArgs e)
    {
        if (_activeTab == null) return;
        try
        {
            var storageProvider = StorageProvider;
            if (storageProvider == null) return;

            var file = await storageProvider.SaveFilePickerAsync(new Avalonia.Platform.Storage.FilePickerSaveOptions
            {
                Title = "Save Session Log",
                SuggestedFileName = "session.log",
                FileTypeChoices = new[]
                {
                    new Avalonia.Platform.Storage.FilePickerFileType("Hex Dump Log") { Patterns = new[] { "*.log" } },
                    new Avalonia.Platform.Storage.FilePickerFileType("Decoded Text") { Patterns = new[] { "*.txt" } },
                    new Avalonia.Platform.Storage.FilePickerFileType("Raw Binary") { Patterns = new[] { "*.bin" } },
                    new Avalonia.Platform.Storage.FilePickerFileType("All files") { Patterns = new[] { "*" } }
                }
            });
            if (file == null) return;

            var filePath = file.Path.LocalPath;
            SessionLogFormat format;
            if (filePath.EndsWith(".bin", StringComparison.OrdinalIgnoreCase))
                format = SessionLogFormat.RawBinary;
            else if (filePath.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
                format = SessionLogFormat.DecodedText;
            else
                format = SessionLogFormat.HexDump;

            var logger = new FileSessionDataLogger();
            logger.Start(filePath, format);
            _activeTab.Logger = logger;
            _activeTab.Session.DataLogger = logger;
            UpdateRecIndicator();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error starting session log: {ex.Message}");
        }
    }

    private void OnStopSessionLogClick(object? sender, RoutedEventArgs e)
    {
        if (_activeTab != null) StopSessionLogging(_activeTab);
    }

    private void StopSessionLogging(TabSession tab)
    {
        if (tab.Logger == null) return;
        tab.Session.DataLogger = null;
        tab.Logger.Dispose();
        tab.Logger = null;
        if (tab == _activeTab) UpdateRecIndicator();
    }

    // ───────────────────────────────────────────────────────────────────
    // Menu Handlers
    // ───────────────────────────────────────────────────────────────────

    private void OnPreferencesClick(object? sender, RoutedEventArgs e)
    {
        var prefsWindow = new PreferencesWindow();
        prefsWindow.ShowDialog(this);
    }

    private void OnExitClick(object? sender, RoutedEventArgs e) => Close();

    private static string GetAssemblyMetadata(System.Reflection.Assembly assembly, string key)
    {
        var attributes = assembly.GetCustomAttributes(typeof(System.Reflection.AssemblyMetadataAttribute), false);
        for (int i = 0; i < attributes.Length; i++)
        {
            var attr = (System.Reflection.AssemblyMetadataAttribute)attributes[i];
            if (string.Equals(attr.Key, key, StringComparison.Ordinal))
                return attr.Value ?? string.Empty;
        }
        return string.Empty;
    }

    private async void OnAboutClick(object? sender, RoutedEventArgs e)
    {
        var assembly = System.Reflection.Assembly.GetExecutingAssembly();
        var version = assembly.GetName().Version;
        string appVersion = version != null ? version.ToString() : "unknown";

        string buildDate = GetAssemblyMetadata(assembly, "BuildDate");
        string buildTime = GetAssemblyMetadata(assembly, "BuildTime");
        string buildInfo = string.Empty;
        if (buildDate.Length > 0 || buildTime.Length > 0)
            buildInfo = $" ({buildDate} {buildTime})".TrimEnd();

        var aboutDialog = new Window
        {
            Title = "About RetroTerm",
            Width = 450,
            Height = 300,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false
        };
        // Theme brushes throughout, and the OK button on the shared dialog class - until
        // 27 September 2026 this dialog was hex-coded and its OK button lost its fill under the
        // mouse (see the note above the Button styles in DarkTheme.axaml).
        aboutDialog[!Window.BackgroundProperty] = new DynamicResourceExtension("WindowBackgroundBrush");

        var border = new Border
        {
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(24)
        };
        border[!Border.BackgroundProperty] = new DynamicResourceExtension("PanelBackgroundBrush");
        border[!Border.BorderBrushProperty] = new DynamicResourceExtension("BorderBrush");

        var panel = new StackPanel { Spacing = 12 };
        panel.Children.Add(ThemedText("RetroTerm", "PrimaryTextBrush", 24, FontWeight.SemiBold, 0));
        panel.Children.Add(ThemedText($"Version {appVersion}{buildInfo}", "SecondaryTextBrush", 13, FontWeight.Normal, 0));
        panel.Children.Add(ThemedText("A modern terminal emulator with vintage soul", "PrimaryTextBrush", 13, FontWeight.Normal, 8));
        panel.Children.Add(ThemedText("Supports: VT100, VT220, TDV1200, TDV2215, TDV2200, ANSI\nProtocols: Telnet, SSH, ND-100 Gateway", "SecondaryTextBrush", 13, FontWeight.Normal, 4));
        panel.Children.Add(ThemedText("Copyright Ronny Hansen 2024-2026", "DisabledTextBrush", 12, FontWeight.Normal, 4));

        var aboutOkButton = new Button
        {
            Content = "OK",
            Classes = { "dialog-primary" },
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 16, 0, 0)
        };
        aboutOkButton.Click += (_, _) => aboutDialog.Close();
        panel.Children.Add(aboutOkButton);

        border.Child = panel;
        aboutDialog.Content = border;
        await aboutDialog.ShowDialog(this);
    }

    /// <summary>
    /// A text block for the About dialog, coloured by a theme brush rather than a hex value.
    /// </summary>
    /// <param name="text">
    /// The text to show.
    /// </param>
    /// <param name="brushKey">
    /// The theme brush resource for the text colour, for example PrimaryTextBrush.
    /// </param>
    /// <param name="fontSize">
    /// Font size in points.
    /// </param>
    /// <param name="weight">
    /// Font weight.
    /// </param>
    /// <param name="topMargin">
    /// Space above the block.
    /// </param>
    /// <returns>
    /// A wrapping text block bound to the brush.
    /// </returns>
    private static TextBlock ThemedText(string text, string brushKey, double fontSize, FontWeight weight, double topMargin)
    {
        var block = new TextBlock
        {
            Text = text,
            TextWrapping = TextWrapping.Wrap,
            FontSize = fontSize,
            FontWeight = weight,
            Margin = new Thickness(0, topMargin, 0, 0)
        };
        block[!TextBlock.ForegroundProperty] = new DynamicResourceExtension(brushKey);
        return block;
    }

    private void OnLogClick(object? sender, RoutedEventArgs e)
    {
        var logWindow = new LogViewerWindow();
        logWindow.Show(this);
    }

    /// <summary>
    /// Opens the two-pane protocol trace. Opening the window enables capture in
    /// <see cref="ProtocolTracer"/>; closing it disables capture again.
    /// </summary>
    private void OnProtocolMonitorClick(object? sender, RoutedEventArgs e)
    {
        var monitorWindow = new ProtocolMonitorWindow();
        monitorWindow.Show(this);
    }

    private void OnKeyboardReferenceClick(object? sender, RoutedEventArgs e)
    {
        var emulatorType = _activeTab?.EmulatorType ?? "TDV2200";
        var keyboardWindow = new KeyboardReferenceWindow(emulatorType);
        keyboardWindow.Show(this);
    }

    private void OnConfigurePushKeysClick(object? sender, RoutedEventArgs e)
    {
        var pushKeysDialog = new ProgrammableKeysDialog();
        pushKeysDialog.TestKeyRequested += async (s, byteString) =>
        {
            if (_activeTab != null && _activeTab.Session.IsConnected)
                await _activeTab.Session.SendInputAsync(byteString);
        };
        pushKeysDialog.ShowDialog(this);
    }

    private async void OnGatewaySettingsClick(object? sender, RoutedEventArgs e)
    {
        if (_gatewayListener == null || _gatewaySettings == null) return;

        var dialog = new GatewaySettingsDialog(this, _gatewayListener, _gatewaySettings);
        await dialog.ShowAsync();
        UpdateGatewayStatus();
    }

    private void OnGatewayDebugClick(object? sender, RoutedEventArgs e)
    {
        if (_gatewayListener == null) return;

        var debugWindow = new GatewayDebugWindow(_gatewayListener);
        debugWindow.Show(this);
    }

    private void OnKermitDebugClick(object? sender, RoutedEventArgs e)
    {
        // Reuse existing window if open
        if (_kermitDebugWindow != null && _kermitDebugWindow.IsVisible)
        {
            _kermitDebugWindow.Activate();
            return;
        }

        _kermitDebugWindow = new KermitDebugWindow();

        // If there's an active transfer, wire up the statistics immediately
        if (_activeTab?.Session.ActiveTransferHandler is KermitFileTransfer kermit)
        {
            _kermitDebugWindow.SetStatistics(kermit.Statistics);
        }

        _kermitDebugWindow.Closed += (_, _) => { _kermitDebugWindow = null; };
        _kermitDebugWindow.Show(this);
    }

    private void OnOpcomDebugClick(object? sender, RoutedEventArgs e)
    {
        // Reuse existing window if open
        if (_opcomDebugWindow != null && _opcomDebugWindow.IsVisible)
        {
            _opcomDebugWindow.Activate();
            return;
        }

        if (_activeTab?.Session == null) return;

        _opcomDebugWindow = new OpcomDebugWindow();
        _opcomDebugWindow.Initialize(_activeTab.Session);
        _opcomDebugWindow.Closed += (_, _) => { _opcomDebugWindow = null; };
        _opcomDebugWindow.Show(this);
    }

    private void UpdateKermitDebugStatistics()
    {
        if (_kermitDebugWindow == null || !_kermitDebugWindow.IsVisible) return;

        // Only push new statistics when a transfer starts — don't null them out
        // on completion so the debug window keeps showing the final counters
        if (_activeTab?.Session.ActiveTransferHandler is KermitFileTransfer kermit)
            _kermitDebugWindow.SetStatistics(kermit.Statistics);
    }

    private void OnGatewayStatisticsClick(object? sender, RoutedEventArgs e)
    {
        if (_gatewayListener == null) return;

        var statsWindow = new GatewayStatisticsWindow(_gatewayListener);
        statsWindow.Show(this);
    }

    private async void OnGatewayDiskImagesClick(object? sender, RoutedEventArgs e)
    {
        if (_gatewayListener == null || _gatewayDiskSettings == null) return;

        var diskWindow = new GatewayDiskConfigWindow(_gatewayListener, _gatewayDiskSettings);
        await diskWindow.ShowDialog(this);

        // Reload settings after dialog closes (Apply may have saved new settings)
        _gatewayDiskSettings = GatewayDiskSettings.Load();
    }

    private void OnVirtualKeyboardClick(object? sender, RoutedEventArgs e)
    {
        var menuItem = this.FindControl<MenuItem>("VirtualKeyboardMenuItem");

        if (_virtualKeyboardWindow == null)
        {
            _virtualKeyboardWindow = new VirtualKeyboardWindow();
            _virtualKeyboardWindow.InputReceived += OnVirtualKeyboardInput;
            _virtualKeyboardWindow.LayoutChanged += OnVirtualKeyboardLayoutChanged;
        }

        _virtualKeyboardWindow.SetKeyboardMapper(_activeTab?.Control.GetKeyboardMapper());
        _virtualKeyboardWindow.CurrentTerminalModes = GetCurrentTerminalModes();

        // Mirror the active terminal's indicator lamps on the keyboard panel.
        SyncVirtualKeyboardLamps();

        // Sync layout dropdown to active terminal's language
        var lang = _activeTab?.LastConnectionParameters?.Language;
        if (!string.IsNullOrEmpty(lang))
            _virtualKeyboardWindow.SetLayout(lang);

        if (_virtualKeyboardWindow.IsVisible)
        {
            _virtualKeyboardWindow.Hide();
            if (menuItem != null) menuItem.Header = "Virtual Keyboard";
        }
        else
        {
            _virtualKeyboardWindow.Show();
            if (menuItem != null) menuItem.Header = "\u2713 Virtual Keyboard";
        }
    }

    private void OnCopyClick(object? sender, RoutedEventArgs e) => _activeTab?.Control.Copy();
    private void OnPasteClick(object? sender, RoutedEventArgs e) => _activeTab?.Control.Paste();
    private void OnSelectAllClick(object? sender, RoutedEventArgs e) => _activeTab?.Control.SelectAll();
    private void OnFindClick(object? sender, RoutedEventArgs e) => ToggleSearchBar();

    // ───────────────────────────────────────────────────────────────────
    // Virtual Keyboard
    // ───────────────────────────────────────────────────────────────────

    /// <summary>
    /// Points the virtual keyboard's indicator lamps at the active tab's emulator and
    /// connection state. Safe to call when the keyboard window is closed or the active
    /// terminal is not a TDV model.
    /// </summary>
    private void SyncVirtualKeyboardLamps()
    {
        if (_virtualKeyboardWindow == null)
            return;

        // Non-TDV emulators (VT100 etc.) have no indicator lamps; passing null blanks them.
        _virtualKeyboardWindow.AttachLedSource(_activeTab?.Session?.Emulator as TDVEmulatorBase);
        _virtualKeyboardWindow.UpdateConnectionLamps(
            _activeTab?.Session?.Status ?? ConnectionStatus.Disconnected);
    }

    /// <summary>
    /// The terminal modes that affect key encoding, for the virtual keyboard.
    ///
    /// Was a copy of the same emulator type-check chain that lived in TerminalCanvas. Both are now
    /// one call to the emulator, which is where the mode state actually is — and which is what
    /// finally lets DECCKM and DECKPAM reach the keyboard mapper.
    /// </summary>
    private RetroTerm.Core.Terminal.Input.TerminalModes GetCurrentTerminalModes()
    {
        return _activeTab?.Session?.Emulator?.GetActiveModes()
            ?? RetroTerm.Core.Terminal.Input.TerminalModes.None;
    }

    private async void OnVirtualKeyboardInput(object? sender, string input)
    {
        if (_activeTab == null || !_activeTab.Session.IsConnected || string.IsNullOrEmpty(input))
            return;
        await _activeTab.Session.SendInputAsync(input);
    }

    /// <summary>
    /// Keeps the active terminal's national variant in sync when the user changes the virtual
    /// keyboard layout dropdown, so the display font and keyboard conversion follow the selection.
    /// </summary>
    private void OnVirtualKeyboardLayoutChanged(object? sender, string languageCode)
    {
        if (_activeTab == null) return;

        ApplyLanguageVariant(_activeTab, languageCode);

        // Persist so the choice survives tab switches (which re-sync the dropdown from this value).
        if (_activeTab.LastConnectionParameters != null)
            _activeTab.LastConnectionParameters.Language = languageCode;

        _activeTab.Control.InvalidateVisual();
    }

    /// <summary>
    /// Sets a tab's emulator ISO 646 national variant from a connection/keyboard language string.
    /// Accepts both display names ("Norwegian") and codes ("no").
    /// </summary>
    /// <remarks>
    /// The mapping itself lives in <see cref="TDVLanguageOptions"/>, next to the list of languages
    /// the dialog offers, because the two used to be written separately and disagreed: Finnish was
    /// offered and had no case here, so it silently produced US ASCII.
    /// </remarks>
    private void ApplyLanguageVariant(TabSession? tab, string? language)
    {
        if (tab?.Session?.Emulator is not TDVEmulatorBase tdv)
            return;
        tdv.CharacterSetVariant = (int)LanguageToVariant(language);
    }

    private static TDV2200ISO646Variant LanguageToVariant(string? language)
        => TDVLanguageOptions.ToVariant(language);

    // ───────────────────────────────────────────────────────────────────
    // Emulator Factory
    // ───────────────────────────────────────────────────────────────────

    private ITerminalEmulator? CreateEmulatorFromParameters(ConnectionFactory.ConnectionParameters parameters)
    {
        try
        {
            var emulator = EmulatorFactory.CreateEmulator(parameters.EmulatorType, parameters.Width, parameters.Height);
            return emulator as ITerminalEmulator;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[MainWindow] Failed to create emulator {parameters.EmulatorType}: {ex.Message}");
            ApplicationLogger.Log($"[MainWindow] Failed to create emulator {parameters.EmulatorType}: {ex.Message}");
            return null;
        }
    }

    // ───────────────────────────────────────────────────────────────────
    // Window Closing (with confirmation)
    // ───────────────────────────────────────────────────────────────────

    /// <remarks>
    /// <para><b>Why <c>e.Cancel = true</c> is the first statement, unconditionally</b></para>
    /// This handler is <c>async void</c>. Avalonia reads <c>e.Cancel</c> the moment the method
    /// call returns control to it - which for an <c>async void</c> method is at the first
    /// incomplete <c>await</c>, not when the method is logically done. The old version left
    /// <c>Cancel</c> at its default (false) through <c>await ShutdownMcpServerAsync()</c> and
    /// the gateway shutdown, so Avalonia went ahead and actually closed the window while this
    /// method was still awaiting. By the time execution reached <c>e.Cancel = true</c> and
    /// <c>ShowCloseConfirmationDialog</c> tried <c>ShowDialog(this)</c>, the owner was already
    /// closed. Reported live, 1 September 2026 — the app crashed on exit with "Cannot show a
    /// window with a closed owner" whenever a confirmation dialog needed to be shown.
    /// <para>
    /// <see cref="_isClosingConfirmed"/> lets the self-issued <c>Close()</c> at the bottom pass
    /// straight through on the second pass instead of re-running teardown and asking again.
    /// </para>
    /// </remarks>
    protected override async void OnClosing(WindowClosingEventArgs e)
    {
        if (_isClosingConfirmed)
        {
            base.OnClosing(e);
            return;
        }

        e.Cancel = true;

        // Stop the MCP server first so no new LLM calls arrive during teardown
        await ShutdownMcpServerAsync();

        // Close virtual keyboard window
        if (_virtualKeyboardWindow != null)
        {
            _virtualKeyboardWindow.InputReceived -= OnVirtualKeyboardInput;
            _virtualKeyboardWindow.AllowClose = true;
            _virtualKeyboardWindow.Close();
            _virtualKeyboardWindow = null;
        }

        // Stop gateway listener
        if (_gatewayListener != null)
        {
            _gatewayListener.EmulatorConnected -= OnGatewayStatusChanged;
            _gatewayListener.EmulatorDisconnected -= OnGatewayStatusChanged;
            _gatewayListener.TerminalListChanged -= OnGatewayStatusChanged;
            _gatewayListener.DiskWorkerConnected -= OnGatewayStatusChanged;
            _gatewayListener.DiskWorkerDisconnected -= OnGatewayStatusChanged;

            try { await _gatewayListener.StopAsync(); }
            catch { /* ignore */ }
            _gatewayListener.Dispose();
            _gatewayListener = null;
        }

        // Count active connections
        int activeCount = 0;
        for (int i = 0; i < _tabs.Count; i++)
        {
            if (_tabs[i].IsConnected) activeCount++;
        }
        for (int i = 0; i < _popoutWindows.Count; i++)
        {
            if (_popoutWindows[i].TabSession.IsConnected) activeCount++;
        }

        if (activeCount > 0)
        {
            var confirmed = await ShowCloseConfirmationDialog(activeCount);
            if (!confirmed)
            {
                // e.Cancel is already true — the window stays open.
                return;
            }
        }

        _isClosingConfirmed = true;
        await CleanUpAllTabsAndPopouts();
        Close();
    }

    private async Task CleanUpAllTabsAndPopouts()
    {
        // Close popout windows
        var popouts = _popoutWindows.ToArray();
        for (int i = 0; i < popouts.Length; i++)
        {
            await popouts[i].ForceCloseAsync();
        }
        _popoutWindows.Clear();

        // Disconnect and dispose all tabs (async to avoid deadlock)
        for (int i = 0; i < _tabs.Count; i++)
        {
            StopSessionLogging(_tabs[i]);
            if (_tabs[i].Session.IsConnected)
            {
                try { await _tabs[i].Session.DisconnectAsync(); }
                catch { /* ignore */ }
            }
            _tabs[i].Dispose();
        }
        _tabs.Clear();

        _tabHeaders.Clear();
        _tabStatusDots.Clear();
        _tabTitleTexts.Clear();
        _tabCloseButtons.Clear();
    }

    private async Task<bool> ShowCloseConfirmationDialog(int activeCount)
    {
        // Themed like every other dialog, through the shared dialog button classes.
        //
        // Until 27 September 2026 this window was a hand-built LIGHT dialog inside a dark app:
        // white background, hex-coded grey and red buttons with their brushes set on the Button
        // itself. Ronny's report: "colours very odd (gray and red) but worse, when I hover over
        // them with the mouse the button disappears." The hover was the Fluent Button theme doing
        // what FluentTheme.axaml says it does at the dialog-primary style: on :pointerover it
        // swaps the ContentPresenter's background for its own resource, which the hand-set brush
        // on the Button cannot reach - so the fill vanished under the pointer. The dialog-* classes
        // exist precisely to pin the fill through hover and press; this dialog just never used
        // them. Nothing here sets a colour by hand any more.
        var dialog = new Window
        {
            Title = "Close RetroTerm",
            Width = 420,
            Height = 180,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false
        };
        dialog[!Window.BackgroundProperty] = new DynamicResourceExtension("WindowBackgroundBrush");

        bool result = false;

        var border = new Border
        {
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(24),
            Margin = new Thickness(16)
        };
        border[!Border.BackgroundProperty] = new DynamicResourceExtension("FluentSurfaceAltBrush");
        border[!Border.BorderBrushProperty] = new DynamicResourceExtension("FluentBorderLightBrush");

        var panel = new StackPanel { Spacing = 16 };
        var connectionWord = activeCount == 1 ? "connection" : "connections";
        var heading = new TextBlock
        {
            Text = $"Close all {activeCount} active {connectionWord}?",
            FontSize = 14,
            FontWeight = FontWeight.SemiBold,
            TextWrapping = TextWrapping.Wrap
        };
        heading[!TextBlock.ForegroundProperty] = new DynamicResourceExtension("FluentTextPrimaryBrush");
        panel.Children.Add(heading);
        var detail = new TextBlock
        {
            Text = "All active sessions will be disconnected.",
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap
        };
        detail[!TextBlock.ForegroundProperty] = new DynamicResourceExtension("FluentTextSecondaryBrush");
        panel.Children.Add(detail);

        var buttonPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8
        };

        var cancelButton = new Button
        {
            Name = "CloseConfirmationCancelButton",
            Content = "Cancel",
            Classes = { "dialog-secondary" }
        };
        cancelButton.Click += (_, _) => { result = false; dialog.Close(); };

        var closeAllButton = new Button
        {
            Name = "CloseConfirmationCloseAllButton",
            Content = "Close All",
            Classes = { "dialog-danger" }
        };
        closeAllButton.Click += (_, _) => { result = true; dialog.Close(); };

        buttonPanel.Children.Add(cancelButton);
        buttonPanel.Children.Add(closeAllButton);
        panel.Children.Add(buttonPanel);

        border.Child = panel;
        dialog.Content = border;
        await dialog.ShowDialog(this);
        return result;
    }
}
