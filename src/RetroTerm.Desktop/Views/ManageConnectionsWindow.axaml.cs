using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using RetroTerm.Core.Configuration;
using RetroTerm.Core.Protocols;
using RetroTerm.Core.Protocols.WebSocket.Gateway;
using RetroTerm.Desktop.Helpers;
using RetroTerm.Desktop.ViewModels;

namespace RetroTerm.Desktop.Views;

public partial class ManageConnectionsWindow : Window
{
    // Dark theme colors
    private static readonly SolidColorBrush BgDark = new(Color.Parse("#1E1E1E"));
    private static readonly SolidColorBrush BgPanel = new(Color.Parse("#252526"));
    private static readonly SolidColorBrush BorderDark = new(Color.Parse("#3C3C3C"));
    private static readonly SolidColorBrush TextPrimary = new(Color.Parse("#CCCCCC"));
    private static readonly SolidColorBrush TextSecondary = new(Color.Parse("#999999"));
    private static readonly SolidColorBrush TextDisabled = new(Color.Parse("#656565"));
    private static readonly SolidColorBrush AccentBlue = new(Color.Parse("#0E639C"));
    private static readonly SolidColorBrush StarGold = new(Color.Parse("#FFD700"));
    private static readonly SolidColorBrush BadgeTelnet = new(Color.Parse("#4EC9B0"));
    private static readonly SolidColorBrush BadgeSSH = new(Color.Parse("#569CD6"));
    private static readonly SolidColorBrush BadgeSerial = new(Color.Parse("#FF8C00"));
    private static readonly SolidColorBrush BadgeGateway = new(Color.Parse("#C586C0"));

    private readonly ConnectionsManagerViewModel _viewModel;
    private readonly GatewayListener? _gatewayListener;

    // Tab content panels (pre-built, swapped on tab change)
    private StackPanel? _generalPanel;
    private StackPanel? _protocolPanel;
    private StackPanel? _terminalPanel;
    private StackPanel? _keyboardPanel;
    private StackPanel? _scriptsPanel;

    // Protocol-specific sub-panels within Protocol tab
    private StackPanel? _telnetProtocolPanel;
    private StackPanel? _sshProtocolPanel;
    private StackPanel? _serialProtocolPanel;
    private StackPanel? _gatewayProtocolPanel;

    // Form controls
    private TextBox? _nameBox;
    private RadioButton? _telnetRadio;
    private RadioButton? _sshRadio;
    private RadioButton? _serialRadio;
    private RadioButton? _gatewayRadio;
    private RadioButton? _opcomSimRadio;
    private TextBox? _hostBox;
    private TextBox? _portBox;
    private TextBox? _usernameBox;
    private TextBox? _passwordBox;
    private ComboBox? _emulatorCombo;
    private ComboBox? _sizeCombo;

    /// <summary>
    /// Whether this connection follows the window or keeps the size above.
    /// </summary>
    private ComboBox? _screenSizeModeCombo;
    private ComboBox? _languageCombo;
    private ComboBox? _colorPresetCombo;

    // Scripts tab: connection event hooks — "(none)" + the script library names.
    private ComboBox? _onConnectCombo;
    private ComboBox? _onDisconnectCombo;
    private const string NoScript = "(none)";

    // Transmit pacing, for hardware that drops characters when a line arrives with no gap.
    private TextBox? _txDelayCharBox;
    private TextBox? _txDelayLineBox;

    // Serial controls
    private ComboBox? _serialPortCombo;
    private ComboBox? _baudRateCombo;
    private ComboBox? _dataBitsCombo;
    private ComboBox? _stopBitsCombo;
    private ComboBox? _parityCombo;
    private ComboBox? _flowControlCombo;

    // Gateway controls
    private TextBlock? _gatewayStatusLabel;
    private CheckBox? _gatewaySelectFirstFreeCheck;

    // Keyboard tab: what the keys send, how line endings are handled, and local echo.
    private CheckBox? _backspaceSendsDelCheck;
    private CheckBox? _deleteSendsDelCheck;
    private ComboBox? _newLineReceiveCombo;
    private ComboBox? _newLineTransmitCombo;
    private CheckBox? _localEchoCheck;

    // Active tab index
    private int _activeTab;

    // Suppress selection change events during programmatic updates
    private bool _suppressSelectionChange;

    public ManageConnectionsWindow()
    {
        InitializeComponent();
        _viewModel = null!;
    }

    public ManageConnectionsWindow(ConfigurationManager configManager, GatewayListener? gatewayListener = null)
    {
        InitializeComponent();

        _viewModel = new ConnectionsManagerViewModel(configManager);
        _gatewayListener = gatewayListener;

        BuildTabPanels();
        WireEvents();
        SelectTab(0); // General tab
        _viewModel.LoadConnections();
        RebuildConnectionList();
    }

    private void WireEvents()
    {
        var newBtn = this.FindControl<Button>("NewConnectionBtn");
        var deleteBtn = this.FindControl<Button>("DeleteBtn");
        var cancelBtn = this.FindControl<Button>("CancelBtn");
        var saveBtn = this.FindControl<Button>("SaveBtn");
        var connectBtn = this.FindControl<Button>("ConnectBtn");
        var searchBox = this.FindControl<TextBox>("SearchBox");
        var listBox = this.FindControl<ListBox>("ConnectionsListBox");

        var generalTabBtn = this.FindControl<Button>("GeneralTabBtn");
        var protocolTabBtn = this.FindControl<Button>("ProtocolTabBtn");
        var terminalTabBtn = this.FindControl<Button>("TerminalTabBtn");
        var keyboardTabBtn = this.FindControl<Button>("KeyboardTabBtn");
        var scriptsTabBtn = this.FindControl<Button>("ScriptsTabBtn");

        if (newBtn != null) newBtn.Click += (_, _) => OnNewConnection();
        if (deleteBtn != null) deleteBtn.Click += async (_, _) => await OnDeleteClick();
        if (cancelBtn != null) cancelBtn.Click += (_, _) => Close(null);
        if (saveBtn != null) saveBtn.Click += async (_, _) => await OnSaveClick();
        if (connectBtn != null) connectBtn.Click += async (_, _) => await OnConnectClick();

        if (searchBox != null)
        {
            searchBox.TextChanged += (_, _) =>
            {
                _viewModel.SearchText = searchBox.Text ?? "";
                RebuildConnectionList();
            };
        }

        if (listBox != null)
        {
            listBox.SelectionChanged += OnListSelectionChanged;
            listBox.DoubleTapped += async (_, _) => await OnConnectClick();
            listBox.ItemTemplate = CreateConnectionItemTemplate();
        }

        if (generalTabBtn != null) generalTabBtn.Click += (_, _) => SelectTab(0);
        if (protocolTabBtn != null) protocolTabBtn.Click += (_, _) => SelectTab(1);
        if (terminalTabBtn != null) terminalTabBtn.Click += (_, _) => SelectTab(2);
        if (keyboardTabBtn != null) keyboardTabBtn.Click += (_, _) => SelectTab(3);
        if (scriptsTabBtn != null) scriptsTabBtn.Click += (_, _) => SelectTab(4);

        // Dirty tracking => update save button
        _viewModel.CurrentProfile.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(ConnectionProfileViewModel.CanSave) ||
                args.PropertyName == nameof(ConnectionProfileViewModel.IsDirty))
            {
                var btn = this.FindControl<Button>("SaveBtn");
                if (btn != null) btn.IsEnabled = _viewModel.CurrentProfile.CanSave;
            }
        };
    }

    #region Tab Panel Building

    private void BuildTabPanels()
    {
        // General tab
        _generalPanel = new StackPanel { Spacing = 16 };
        _generalPanel.Children.Add(CreateLabeledTextBox("Connection Name", out _nameBox, "Enter connection name..."));

        // Protocol radio buttons
        var protocolLabel = new TextBlock
        {
            Text = "Protocol",
            Foreground = TextPrimary,
            FontSize = 13,
            FontWeight = FontWeight.SemiBold
        };
        var protocolPanel = new StackPanel { Spacing = 4 };
        protocolPanel.Children.Add(protocolLabel);

        var radioPanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 16 };
        _telnetRadio = new RadioButton { Content = "Telnet", GroupName = "Protocol", Foreground = TextPrimary, FontSize = 13, IsChecked = true };
        _sshRadio = new RadioButton { Content = "SSH", GroupName = "Protocol", Foreground = TextPrimary, FontSize = 13 };
        _serialRadio = new RadioButton { Content = "Serial", GroupName = "Protocol", Foreground = TextPrimary, FontSize = 13 };
        _gatewayRadio = new RadioButton { Content = "Gateway", GroupName = "Protocol", Foreground = TextPrimary, FontSize = 13 };
        _opcomSimRadio = new RadioButton { Content = "OPCOM Sim", GroupName = "Protocol", Foreground = TextPrimary, FontSize = 13 };
        radioPanel.Children.Add(_telnetRadio);
        radioPanel.Children.Add(_sshRadio);
        radioPanel.Children.Add(_serialRadio);
        radioPanel.Children.Add(_gatewayRadio);
        radioPanel.Children.Add(_opcomSimRadio);
        protocolPanel.Children.Add(radioPanel);
        _generalPanel.Children.Add(protocolPanel);

        if (_nameBox != null)
        {
            _nameBox.TextChanged += (_, _) => _viewModel.CurrentProfile.Name = _nameBox.Text ?? "";
        }

        _telnetRadio.IsCheckedChanged += (_, _) => { if (_telnetRadio.IsChecked == true) OnProtocolRadioChanged("Telnet"); };
        _sshRadio.IsCheckedChanged += (_, _) => { if (_sshRadio.IsChecked == true) OnProtocolRadioChanged("SSH"); };
        _serialRadio.IsCheckedChanged += (_, _) => { if (_serialRadio.IsChecked == true) OnProtocolRadioChanged("Serial"); };
        _gatewayRadio.IsCheckedChanged += (_, _) => { if (_gatewayRadio.IsChecked == true) OnProtocolRadioChanged("Gateway"); };
        _opcomSimRadio.IsCheckedChanged += (_, _) => { if (_opcomSimRadio.IsChecked == true) OnProtocolRadioChanged("OpcomSimulator"); };

        // Protocol tab
        _protocolPanel = new StackPanel { Spacing = 16 };
        BuildProtocolSubPanels();

        // Terminal tab
        _terminalPanel = new StackPanel { Spacing = 16 };
        // The list comes from the FACTORY, which is the thing that has to build whatever is picked.
        // It used to be three names typed in here, so every terminal added since - the VT52, VT102,
        // VT220, VT240, VT340, VT420, both xterms, the TDV1200 and the TEK4014 - existed, passed
        // its tests, and could not be chosen for a connection by anybody. A hardcoded copy of a
        // list that lives somewhere else is a promise to update two places forever.
        _terminalPanel.Children.Add(CreateLabeledComboBox("Terminal Type", out _emulatorCombo,
            RetroTerm.Core.Configuration.EmulatorFactory.AvailableEmulators));
        _terminalPanel.Children.Add(CreateLabeledComboBox("Terminal Size", out _sizeCombo,
            new[] { "80x24", "80x25", "132x24", "132x25", "80x43", "80x50" }));
        // Straight after the size, because it says what that size MEANS: a fixed connection keeps
        // it, and one following the window overrides it on the first arrange. The choices come from
        // the view model rather than being listed again here.
        _terminalPanel.Children.Add(CreateLabeledComboBox("Screen Size Mode", out _screenSizeModeCombo,
            ViewModels.ConnectionProfileViewModel.ScreenSizeModes));
        _terminalPanel.Children.Add(CreateLabeledComboBox("Language", out _languageCombo,
            RetroTerm.Core.Terminal.Emulators.TDV.TDVLanguageOptions.DisplayNames()));

        // Color preset dropdown (built from MainWindow.TerminalColorPresets)
        {
            var presetNames = new string[MainWindow.TerminalColorPresets.Length];
            for (int pi = 0; pi < MainWindow.TerminalColorPresets.Length; pi++)
                presetNames[pi] = MainWindow.TerminalColorPresets[pi].Name;
            _terminalPanel.Children.Add(CreateLabeledComboBox("Terminal Color", out _colorPresetCombo, presetNames));
        }

        if (_emulatorCombo != null)
        {
            _emulatorCombo.SelectionChanged += (_, _) =>
            {
                if (_emulatorCombo.SelectedItem is string emu)
                    _viewModel.CurrentProfile.EmulatorType = emu;
            };
        }
        if (_sizeCombo != null)
        {
            _sizeCombo.SelectionChanged += (_, _) =>
            {
                if (_sizeCombo.SelectedItem is string size)
                    _viewModel.CurrentProfile.Size = size;
            };
        }
        if (_screenSizeModeCombo != null)
        {
            _screenSizeModeCombo.SelectionChanged += (_, _) =>
            {
                if (_screenSizeModeCombo.SelectedItem is string mode)
                    _viewModel.CurrentProfile.ScreenSizeMode = mode;
            };
        }
        if (_languageCombo != null)
        {
            _languageCombo.SelectionChanged += (_, _) =>
            {
                if (_languageCombo.SelectedItem is string lang)
                    _viewModel.CurrentProfile.Language = lang;
            };
        }
        if (_colorPresetCombo != null)
        {
            _colorPresetCombo.SelectionChanged += (_, _) =>
            {
                if (_colorPresetCombo.SelectedItem is string presetName)
                {
                    for (int pi = 0; pi < MainWindow.TerminalColorPresets.Length; pi++)
                    {
                        if (MainWindow.TerminalColorPresets[pi].Name == presetName)
                        {
                            _viewModel.CurrentProfile.ForegroundColor = MainWindow.TerminalColorPresets[pi].Fg;
                            _viewModel.CurrentProfile.BackgroundColor = MainWindow.TerminalColorPresets[pi].Bg;

                            // Carried explicitly: the two Amber presets have identical hex, so
                            // saving only the colours would lose which of them was chosen.
                            _viewModel.CurrentProfile.SinglePhosphor = MainWindow.TerminalColorPresets[pi].Mono;
                            break;
                        }
                    }
                }
            };
        }

        // Keyboard tab: what the keys send, line endings, local echo
        BuildKeyboardPanel();

        // Scripts tab: connection event hooks
        BuildScriptsPanel();
    }

    /// <summary>
    /// Keyboard tab: what Backspace and Delete send, how line endings are handled in each
    /// direction, and whether this terminal echoes what is typed.
    /// </summary>
    /// <remarks>
    /// <para><b>Per connection rather than in Preferences</b></para>
    /// Every one of these is a property of the HOST, not of the keyboard or of the person. A
    /// SINTRAN line and a Unix login want opposite answers to the DEL question and to local echo,
    /// and both are commonly open in this window at once. A single global setting would be wrong
    /// for one of them at all times.
    /// <para><b>The defaults change nothing</b></para>
    /// Both DEL boxes start clear, transmit starts at CR and receive at AUTO, which is what every
    /// connection did before this tab existed - so opening an old connection and pressing Save
    /// cannot alter how it behaves.
    /// </remarks>
    private void BuildKeyboardPanel()
    {
        _keyboardPanel = new StackPanel { Spacing = 16 };

        _keyboardPanel.Children.Add(new TextBlock
        {
            Text = "What the keys send and how line endings are handled. These belong to the host, "
                + "not to your keyboard, so they are saved with this connection.",
            Foreground = TextSecondary,
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap
        });

        // ── Transmit DEL by ────────────────────────────────────────
        _keyboardPanel.Children.Add(new TextBlock
        {
            Text = "Transmit DEL (0x7F) by:",
            Foreground = TextPrimary,
            FontSize = 13,
            FontWeight = FontWeight.SemiBold,
            Margin = new Thickness(0, 8, 0, 0)
        });

        _backspaceSendsDelCheck = new CheckBox
        {
            Content = "Backspace key   (half-set = the terminal's own default: DEL on a TDV, BS on a VT)",
            Foreground = TextPrimary,
            FontSize = 13,
            // Three states, because "not chosen" is a real answer: it means the terminal decides.
            // That is the state every connection is in unless somebody clicked this box.
            IsThreeState = true
        };
        _backspaceSendsDelCheck.IsCheckedChanged += (_, _) =>
        {
            if (_suppressSelectionChange) return;
            _viewModel.CurrentProfile.BackspaceSendsDel = _backspaceSendsDelCheck.IsChecked;
        };
        _keyboardPanel.Children.Add(_backspaceSendsDelCheck);

        _deleteSendsDelCheck = new CheckBox
        {
            Content = "Delete key   (otherwise it sends this terminal's own sequence)",
            Foreground = TextPrimary,
            FontSize = 13
        };
        _deleteSendsDelCheck.IsCheckedChanged += (_, _) =>
        {
            if (_suppressSelectionChange) return;
            _viewModel.CurrentProfile.DeleteSendsDel = _deleteSendsDelCheck.IsChecked == true;
        };
        _keyboardPanel.Children.Add(_deleteSendsDelCheck);

        // ── New-line ───────────────────────────────────────────────
        _keyboardPanel.Children.Add(new TextBlock
        {
            Text = "New-line",
            Foreground = TextPrimary,
            FontSize = 13,
            FontWeight = FontWeight.SemiBold,
            Margin = new Thickness(0, 12, 0, 0)
        });

        _keyboardPanel.Children.Add(CreateLabeledComboBox("Receive — how an incoming line ending is read",
            out _newLineReceiveCombo, ConnectionFactory.NewLineModes.ReceiveChoices));
        _keyboardPanel.Children.Add(CreateLabeledComboBox("Transmit — what the Enter key sends",
            out _newLineTransmitCombo, ConnectionFactory.NewLineModes.TransmitChoices));

        if (_newLineReceiveCombo != null)
        {
            _newLineReceiveCombo.SelectionChanged += (_, _) =>
            {
                if (_suppressSelectionChange) return;
                if (_newLineReceiveCombo.SelectedItem is string mode)
                    _viewModel.CurrentProfile.NewLineReceive = mode;
            };
        }
        if (_newLineTransmitCombo != null)
        {
            _newLineTransmitCombo.SelectionChanged += (_, _) =>
            {
                if (_suppressSelectionChange) return;
                if (_newLineTransmitCombo.SelectedItem is string mode)
                    _viewModel.CurrentProfile.NewLineTransmit = mode;
            };
        }

        // ── Local echo ─────────────────────────────────────────────
        _keyboardPanel.Children.Add(new TextBlock
        {
            Text = "Local echo",
            Foreground = TextPrimary,
            FontSize = 13,
            FontWeight = FontWeight.SemiBold,
            Margin = new Thickness(0, 12, 0, 0)
        });

        _localEchoCheck = new CheckBox
        {
            Content = "Echo what I type on this screen   (for a host that does not echo)",
            Foreground = TextPrimary,
            FontSize = 13
        };
        _localEchoCheck.IsCheckedChanged += (_, _) =>
        {
            if (_suppressSelectionChange) return;
            _viewModel.CurrentProfile.LocalEcho = _localEchoCheck.IsChecked == true;
        };
        _keyboardPanel.Children.Add(_localEchoCheck);
    }

    /// <summary>
    /// Scripts tab: pick a library script to run on connect (auto-login) and one to
    /// run when the line DROPS (auto-reconnect). Same settings as CONNSAVE's
    /// onconnect=/ondisconnect= — this is just the clickable way to set them.
    /// </summary>
    private void BuildScriptsPanel()
    {
        _scriptsPanel = new StackPanel { Spacing = 16 };

        var scriptNames = LoadScriptNamesWithNone();

        _scriptsPanel.Children.Add(new TextBlock
        {
            Text = $"Scripts from the library ({System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RetroTerm", "scripts")}) can run automatically " +
                   "on connection events. Manage them in View → Script Editor.",
            Foreground = TextSecondary,
            FontSize = 13,
            TextWrapping = TextWrapping.Wrap
        });

        _scriptsPanel.Children.Add(CreateLabeledComboBox("On connect — runs right after the connection is established (auto-login)",
            out _onConnectCombo, scriptNames));
        _scriptsPanel.Children.Add(CreateLabeledComboBox("On disconnect — runs when the line DROPS (never on a deliberate disconnect)",
            out _onDisconnectCombo, scriptNames));

        if (_onConnectCombo != null)
        {
            _onConnectCombo.Width = 280;
            _onConnectCombo.SelectionChanged += (_, _) =>
            {
                var name = _onConnectCombo.SelectedItem as string;
                _viewModel.CurrentProfile.OnConnectScript = name == null || name == NoScript ? null : name;
            };
        }
        if (_onDisconnectCombo != null)
        {
            _onDisconnectCombo.Width = 280;
            _onDisconnectCombo.SelectionChanged += (_, _) =>
            {
                var name = _onDisconnectCombo.SelectedItem as string;
                _viewModel.CurrentProfile.OnDisconnectScript = name == null || name == NoScript ? null : name;
            };
        }
    }

    /// <summary>
    /// "(none)" followed by every script in the library, in list order.
    /// </summary>
    private static string[] LoadScriptNamesWithNone()
    {
        var library = RetroTerm.Core.Scripting.ScriptLibrary.CreateDefault();
        var names = library.List();
        var items = new string[names.Count + 1];
        items[0] = NoScript;
        for (int i = 0; i < names.Count; i++)
        {
            items[i + 1] = names[i];
        }
        return items;
    }

    /// <summary>
    /// Sets a hook combo to the profile's script name. A saved name that no longer
    /// exists in the library is APPENDED (marked missing) rather than silently
    /// dropped — selecting "(none)" is the user's decision, not ours.
    /// </summary>
    private static void SelectScriptInCombo(ComboBox? combo, string? scriptName)
    {
        if (combo == null) return;
        if (string.IsNullOrEmpty(scriptName))
        {
            combo.SelectedIndex = 0; // "(none)"
            return;
        }
        var items = combo.ItemsSource as string[] ?? Array.Empty<string>();
        for (int i = 0; i < items.Length; i++)
        {
            if (string.Equals(items[i], scriptName, StringComparison.OrdinalIgnoreCase))
            {
                combo.SelectedIndex = i;
                return;
            }
        }
        var extended = new string[items.Length + 1];
        for (int i = 0; i < items.Length; i++) extended[i] = items[i];
        extended[items.Length] = scriptName;
        combo.ItemsSource = extended;
        combo.SelectedIndex = extended.Length - 1;
    }

    private void BuildProtocolSubPanels()
    {
        // Telnet panel
        _telnetProtocolPanel = new StackPanel { Spacing = 12 };
        _telnetProtocolPanel.Children.Add(CreateLabeledTextBox("Host", out _hostBox, "localhost"));
        _telnetProtocolPanel.Children.Add(CreateLabeledTextBox("Port", out _portBox, "23"));

        if (_hostBox != null)
            _hostBox.TextChanged += (_, _) => _viewModel.CurrentProfile.Host = _hostBox.Text ?? "";
        if (_portBox != null)
        {
            _portBox.TextChanged += (_, _) =>
            {
                if (int.TryParse(_portBox.Text, out var port))
                    _viewModel.CurrentProfile.Port = port;
            };
        }

        // SSH panel (reuses host/port controls, adds username/password)
        _sshProtocolPanel = new StackPanel { Spacing = 12 };
        // Host and Port are shared with telnet — we rebuild them as needed

        // Serial panel
        _serialProtocolPanel = new StackPanel { Spacing = 12 };
        BuildSerialPanel();

        // Gateway panel
        _gatewayProtocolPanel = new StackPanel { Spacing = 12 };
        BuildGatewayPanel();
    }

    private void BuildSerialPanel()
    {
        if (_serialProtocolPanel == null) return;

        var portRow = new StackPanel { Spacing = 4 };
        portRow.Children.Add(new TextBlock { Text = "Serial Port", Foreground = TextPrimary, FontSize = 13 });
        var portPanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        _serialPortCombo = new ComboBox
        {
            Width = 150,
            Background = BgDark,
            Foreground = TextPrimary,
            BorderBrush = BorderDark,
            BorderThickness = new Thickness(1)
        };
        // Themed through the Secondary class rather than hand brushes: a brush set on the Button
        // itself is lost under the mouse (see the note above the Button styles in DarkTheme.axaml).
        var refreshBtn = new Button
        {
            Content = "Refresh",
            Width = 70,
            Height = 30,
            Classes = { "Secondary" },
            Padding = new Thickness(0),
            FontSize = 12
        };
        refreshBtn.Click += (_, _) => RefreshSerialPorts();
        portPanel.Children.Add(_serialPortCombo);
        portPanel.Children.Add(refreshBtn);
        portRow.Children.Add(portPanel);
        _serialProtocolPanel.Children.Add(portRow);

        _serialProtocolPanel.Children.Add(CreateLabeledComboBox("Speed (Baud)", out _baudRateCombo,
            new[] { "300", "1200", "2400", "4800", "9600", "19200", "38400", "57600", "115200" }));
        _serialProtocolPanel.Children.Add(CreateLabeledComboBox("Data Bits", out _dataBitsCombo,
            new[] { "5", "6", "7", "8" }));
        _serialProtocolPanel.Children.Add(CreateLabeledComboBox("Stop Bits", out _stopBitsCombo,
            new[] { "1", "1.5", "2" }));
        _serialProtocolPanel.Children.Add(CreateLabeledComboBox("Parity", out _parityCombo,
            new[] { "None", "Odd", "Even", "Mark", "Space" }));
        _serialProtocolPanel.Children.Add(CreateLabeledComboBox("Flow Control", out _flowControlCombo,
            new[] { "None", "XOn/XOff", "RTS/CTS", "Both" }));

        // ── Transmit delay ────────────────────────────────────────
        // Here rather than in Preferences because it belongs to the far end, exactly like the
        // baud rate above it. One machine needing 20 ms a character is no reason to throttle a
        // telnet session in the next tab, and both are normally open at once.
        _serialProtocolPanel.Children.Add(new TextBlock
        {
            Text = "Transmit delay",
            Foreground = TextPrimary,
            FontSize = 13,
            FontWeight = FontWeight.SemiBold,
            Margin = new Thickness(0, 12, 0, 0)
        });
        _serialProtocolPanel.Children.Add(new TextBlock
        {
            Text = "For hardware that polls its UART in software and drops characters when a whole "
                + "line arrives with no gap. 0 = send at full speed.",
            Foreground = TextSecondary,
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap
        });
        _serialProtocolPanel.Children.Add(CreateLabeledTextBox("msec/char — between each byte",
            out _txDelayCharBox, "0"));
        _serialProtocolPanel.Children.Add(CreateLabeledTextBox("msec/line — extra, after CR or LF",
            out _txDelayLineBox, "0"));

        // Wire serial events
        if (_serialPortCombo != null) _serialPortCombo.SelectionChanged += (_, _) =>
            _viewModel.CurrentProfile.PortName = _serialPortCombo.SelectedItem?.ToString() ?? "";
        if (_baudRateCombo != null) _baudRateCombo.SelectionChanged += (_, _) =>
        {
            if (int.TryParse(_baudRateCombo.SelectedItem?.ToString(), out var baud))
                _viewModel.CurrentProfile.BaudRate = baud;
        };
        if (_dataBitsCombo != null) _dataBitsCombo.SelectionChanged += (_, _) =>
        {
            if (int.TryParse(_dataBitsCombo.SelectedItem?.ToString(), out var db))
                _viewModel.CurrentProfile.DataBits = db;
        };
        if (_stopBitsCombo != null) _stopBitsCombo.SelectionChanged += (_, _) =>
        {
            var text = _stopBitsCombo.SelectedItem?.ToString() ?? "1";
            _viewModel.CurrentProfile.StopBitsValue = text switch { "1" => 1, "1.5" => 3, "2" => 2, _ => 1 };
        };
        if (_parityCombo != null) _parityCombo.SelectionChanged += (_, _) =>
            _viewModel.CurrentProfile.ParityValue = _parityCombo.SelectedIndex;
        if (_flowControlCombo != null) _flowControlCombo.SelectionChanged += (_, _) =>
            _viewModel.CurrentProfile.HandshakeValue = _flowControlCombo.SelectedIndex;

        // Blank or nonsense reads as 0, which is "no delay" - the same thing an empty box looks
        // like it should mean. The guard stops loading a profile from marking it dirty.
        if (_txDelayCharBox != null) _txDelayCharBox.TextChanged += (_, _) =>
        {
            if (_suppressSelectionChange) return;
            _viewModel.CurrentProfile.TransmitDelayPerCharMs = ParseDelay(_txDelayCharBox.Text);
        };
        if (_txDelayLineBox != null) _txDelayLineBox.TextChanged += (_, _) =>
        {
            if (_suppressSelectionChange) return;
            _viewModel.CurrentProfile.TransmitDelayPerLineMs = ParseDelay(_txDelayLineBox.Text);
        };

        // Initial serial port load
        RefreshSerialPorts();

        // Default selections
        if (_baudRateCombo != null) _baudRateCombo.SelectedItem = "9600";
        if (_dataBitsCombo != null) _dataBitsCombo.SelectedItem = "8";
        if (_stopBitsCombo != null) _stopBitsCombo.SelectedIndex = 0;
        if (_parityCombo != null) _parityCombo.SelectedIndex = 0;
        if (_flowControlCombo != null) _flowControlCombo.SelectedIndex = 0;
    }

    /// <summary>
    /// Reads a delay box: a non-negative whole number of milliseconds, or 0.
    /// </summary>
    /// <param name="text">
    /// What is typed in the box.
    /// </param>
    /// <returns>
    /// The delay, or 0 for anything empty, negative or not a number.
    /// </returns>
    /// <remarks>
    /// Deliberately forgiving. The box is being typed INTO, so it passes through "", "-" and "2x"
    /// on the way to a real value, and none of those should throw or clear what is already stored
    /// elsewhere - they simply mean "no delay yet".
    /// </remarks>
    private static int ParseDelay(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return 0;
        if (!int.TryParse(text.Trim(), System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out int value))
        {
            return 0;
        }
        return value < 0 ? 0 : value;
    }

    /// <summary>
    /// <see cref="ParseDelay"/>, for tests. The rule is worth pinning; the dialog is not worth
    /// constructing to do it.
    /// </summary>
    /// <param name="text">
    /// What is typed in the box.
    /// </param>
    /// <returns>
    /// The delay in milliseconds.
    /// </returns>
    internal static int ParseDelayForTesting(string? text) => ParseDelay(text);

    private void BuildGatewayPanel()
    {
        if (_gatewayProtocolPanel == null) return;

        _gatewayStatusLabel = new TextBlock
        {
            Text = "Terminal selection happens in the terminal after connecting.",
            Foreground = TextSecondary,
            FontSize = 13,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 8)
        };
        _gatewayProtocolPanel.Children.Add(_gatewayStatusLabel);

        _gatewaySelectFirstFreeCheck = new CheckBox
        {
            Content = "Auto-connect to first free terminal",
            Foreground = TextPrimary,
            FontSize = 13,
            Margin = new Thickness(0, 0, 0, 8)
        };
        _gatewaySelectFirstFreeCheck.IsCheckedChanged += (_, _) =>
        {
            _viewModel.CurrentProfile.GatewaySelectFirstFree = _gatewaySelectFirstFreeCheck.IsChecked == true;
        };
        _gatewayProtocolPanel.Children.Add(_gatewaySelectFirstFreeCheck);
    }

    #endregion

    #region Connection List

    // Marker class for group headers in the ListBox
    private sealed class GroupHeader
    {
        public string Title { get; }
        public GroupHeader(string title) { Title = title; }
    }

    private FuncDataTemplate<object> CreateConnectionItemTemplate()
    {
        return new FuncDataTemplate<object>((item, _) =>
        {
            if (item is GroupHeader header)
            {
                return new TextBlock
                {
                    Text = header.Title,
                    FontSize = 11,
                    FontWeight = FontWeight.SemiBold,
                    Foreground = TextDisabled,
                    Padding = new Thickness(4, 8, 4, 4),
                    IsHitTestVisible = false
                };
            }

            if (item is HostConfiguration config)
            {
                var outerPanel = new DockPanel { Margin = new Thickness(0, 1) };

                // Star button
                // The StarToggle class carries the size, the transparent fill and the hover; it
                // existed in DarkTheme.axaml and was used nowhere until 27 September 2026, while
                // this button set the same things by hand and turned into a grey block under
                // the mouse. Only the "is a favourite" gold is decided here.
                var starBtn = new Button
                {
                    Content = config.IsFavorite ? "\u2605" : "\u2606",
                    Classes = { "StarToggle" }
                };
                if (config.IsFavorite)
                {
                    starBtn.Foreground = StarGold;
                }
                starBtn.Click += async (s, e) =>
                {
                    e.Handled = true;
                    await _viewModel.ToggleFavoriteAsync(config);
                    RebuildConnectionList();
                };
                DockPanel.SetDock(starBtn, Dock.Left);
                outerPanel.Children.Add(starBtn);

                // Protocol badge
                var badgeColorHex = ConnectionDisplayHelper.GetProtocolBadgeColorHex(config.Protocol);
                var badgeColor = new SolidColorBrush(Color.Parse(badgeColorHex));
                var badge = new Border
                {
                    Background = Brushes.Transparent,
                    BorderBrush = badgeColor,
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(3),
                    Padding = new Thickness(4, 1),
                    Margin = new Thickness(4, 0, 8, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                    Child = new TextBlock
                    {
                        Text = ConnectionDisplayHelper.GetProtocolBadge(config.Protocol),
                        FontSize = 10,
                        FontWeight = FontWeight.Bold,
                        Foreground = badgeColor
                    }
                };
                DockPanel.SetDock(badge, Dock.Left);
                outerPanel.Children.Add(badge);

                // Text
                var textPanel = new StackPanel { Spacing = 2 };
                textPanel.Children.Add(new TextBlock
                {
                    Text = config.Name,
                    FontSize = 13,
                    FontWeight = FontWeight.SemiBold,
                    Foreground = TextPrimary
                });
                textPanel.Children.Add(new TextBlock
                {
                    Text = ConnectionDisplayHelper.GetConnectionDetail(config),
                    FontSize = 11,
                    Foreground = TextSecondary
                });
                outerPanel.Children.Add(textPanel);

                return outerPanel;
            }

            return new TextBlock { Text = "?" };
        });
    }

    private void RebuildConnectionList()
    {
        var listBox = this.FindControl<ListBox>("ConnectionsListBox");
        if (listBox == null) return;

        var items = new List<object>();

        if (_viewModel.FavoriteConnections.Count > 0)
        {
            items.Add(new GroupHeader("FAVORITES"));
            for (int i = 0; i < _viewModel.FavoriteConnections.Count; i++)
                items.Add(_viewModel.FavoriteConnections[i]);
        }

        if (_viewModel.AllConnections.Count > 0)
        {
            items.Add(new GroupHeader("ALL CONNECTIONS"));
            for (int i = 0; i < _viewModel.AllConnections.Count; i++)
                items.Add(_viewModel.AllConnections[i]);
        }

        _suppressSelectionChange = true;
        listBox.ItemsSource = null;
        listBox.ItemsSource = items;
        _suppressSelectionChange = false;

        // Re-select if we have a selected config
        if (_viewModel.SelectedConfiguration != null)
        {
            for (int i = 0; i < items.Count; i++)
            {
                if (items[i] is HostConfiguration cfg && cfg.Id == _viewModel.SelectedConfiguration.Id)
                {
                    listBox.SelectedIndex = i;
                    break;
                }
            }
        }
    }

    private async void OnListSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_suppressSelectionChange) return;

        var listBox = this.FindControl<ListBox>("ConnectionsListBox");
        if (listBox == null) return;

        // Ignore GroupHeader selection
        if (listBox.SelectedItem is GroupHeader)
        {
            // Skip to next non-header item
            int idx = listBox.SelectedIndex;
            var items = listBox.ItemsSource as IList<object>;
            if (items != null && idx + 1 < items.Count && items[idx + 1] is HostConfiguration)
            {
                listBox.SelectedIndex = idx + 1;
            }
            else
            {
                _suppressSelectionChange = true;
                listBox.SelectedIndex = -1;
                _suppressSelectionChange = false;
            }
            return;
        }

        if (listBox.SelectedItem is HostConfiguration selectedConfig)
        {
            // Check dirty state
            if (_viewModel.CurrentProfile.IsDirty)
            {
                var result = await ErrorDialogHelper.ShowConfirmDialogAsync(this,
                    "Unsaved Changes",
                    $"Discard unsaved changes to '{_viewModel.CurrentProfile.Name}'?",
                    "Discard", "Cancel");

                if (!result)
                {
                    // Revert selection
                    _suppressSelectionChange = true;
                    if (_viewModel.SelectedConfiguration != null)
                    {
                        var items = listBox.ItemsSource as IList<object>;
                        if (items != null)
                        {
                            for (int i = 0; i < items.Count; i++)
                            {
                                if (items[i] is HostConfiguration cfg && cfg.Id == _viewModel.SelectedConfiguration.Id)
                                {
                                    listBox.SelectedIndex = i;
                                    break;
                                }
                            }
                        }
                    }
                    else
                    {
                        listBox.SelectedIndex = -1;
                    }
                    _suppressSelectionChange = false;
                    return;
                }
            }

            _viewModel.SelectProfile(selectedConfig);
            LoadProfileIntoForm();

            var deleteBtn = this.FindControl<Button>("DeleteBtn");
            if (deleteBtn != null) deleteBtn.IsEnabled = true;
        }
    }

    #endregion

    #region Tab Switching

    private void SelectTab(int tabIndex)
    {
        _activeTab = tabIndex;

        var generalTabBtn = this.FindControl<Button>("GeneralTabBtn");
        var protocolTabBtn = this.FindControl<Button>("ProtocolTabBtn");
        var terminalTabBtn = this.FindControl<Button>("TerminalTabBtn");
        var keyboardTabBtn = this.FindControl<Button>("KeyboardTabBtn");
        var scriptsTabBtn = this.FindControl<Button>("ScriptsTabBtn");
        var tabContent = this.FindControl<ContentControl>("TabContent");

        Button?[] buttons = { generalTabBtn, protocolTabBtn, terminalTabBtn, keyboardTabBtn, scriptsTabBtn };
        StackPanel?[] panels = { _generalPanel, _protocolPanel, _terminalPanel, _keyboardPanel, _scriptsPanel };

        for (int i = 0; i < buttons.Length; i++)
        {
            if (buttons[i] != null)
            {
                if (i == tabIndex)
                    buttons[i]!.Classes.Add("active");
                else
                    buttons[i]!.Classes.Remove("active");
            }
        }

        if (tabContent != null && tabIndex >= 0 && tabIndex < panels.Length)
        {
            tabContent.Content = panels[tabIndex];
            if (tabIndex == 1) UpdateProtocolPanel();
            // Scripts moved from 3 to 4 when the Keyboard tab went in between. Getting this
            // wrong is silent: the wrong panel simply never refreshes.
            if (tabIndex == 4) RefreshScriptCombos();
        }
    }

    /// <summary>
    /// Re-reads the script library (it may have changed while this dialog is open)
    /// and re-selects the profile's hooks.
    /// </summary>
    private void RefreshScriptCombos()
    {
        var scriptNames = LoadScriptNamesWithNone();
        if (_onConnectCombo != null) _onConnectCombo.ItemsSource = scriptNames;
        if (_onDisconnectCombo != null) _onDisconnectCombo.ItemsSource = scriptNames;
        SelectScriptInCombo(_onConnectCombo, _viewModel.CurrentProfile.OnConnectScript);
        SelectScriptInCombo(_onDisconnectCombo, _viewModel.CurrentProfile.OnDisconnectScript);
    }

    private void UpdateProtocolPanel()
    {
        if (_protocolPanel == null) return;
        _protocolPanel.Children.Clear();

        var protocol = _viewModel.CurrentProfile.Protocol;

        if (string.Equals(protocol, "Serial", StringComparison.OrdinalIgnoreCase))
        {
            _protocolPanel.Children.Add(_serialProtocolPanel!);
        }
        else if (string.Equals(protocol, "Gateway", StringComparison.OrdinalIgnoreCase))
        {
            _protocolPanel.Children.Add(_gatewayProtocolPanel!);
            RefreshGatewayTerminalList(_viewModel.CurrentProfile.GatewayIdentCode);
        }
        else if (string.Equals(protocol, "SSH", StringComparison.OrdinalIgnoreCase))
        {
            // SSH: host, port, username, password
            _protocolPanel.Children.Add(CreateLabeledTextBox("Host", out var sshHost, "localhost"));
            _protocolPanel.Children.Add(CreateLabeledTextBox("Port", out var sshPort, "22"));
            _protocolPanel.Children.Add(CreateLabeledTextBox("Username", out _usernameBox, ""));
            var pwPanel = CreateLabeledPasswordBox("Password", out _passwordBox);
            _protocolPanel.Children.Add(pwPanel);

            // Sync from ViewModel
            if (sshHost != null) { sshHost.Text = _viewModel.CurrentProfile.Host; _hostBox = sshHost; }
            if (sshPort != null) { sshPort.Text = _viewModel.CurrentProfile.Port.ToString(); _portBox = sshPort; }
            if (_usernameBox != null) _usernameBox.Text = _viewModel.CurrentProfile.Username;
            if (_passwordBox != null) _passwordBox.Text = _viewModel.CurrentProfile.Password;

            // Wire events
            if (sshHost != null) sshHost.TextChanged += (_, _) => _viewModel.CurrentProfile.Host = sshHost.Text ?? "";
            if (sshPort != null) sshPort.TextChanged += (_, _) =>
            {
                if (int.TryParse(sshPort.Text, out var p)) _viewModel.CurrentProfile.Port = p;
            };
            if (_usernameBox != null) _usernameBox.TextChanged += (_, _) =>
                _viewModel.CurrentProfile.Username = _usernameBox.Text ?? "";
            if (_passwordBox != null) _passwordBox.TextChanged += (_, _) =>
                _viewModel.CurrentProfile.Password = _passwordBox.Text ?? "";
        }
        else if (string.Equals(protocol, "OpcomSimulator", StringComparison.OrdinalIgnoreCase))
        {
            // OPCOM Simulator: no configuration needed
            var infoText = new Avalonia.Controls.TextBlock
            {
                Text = "ND-100 OPCOM Simulator\n\nSimulates the ND-100 OPCOM microprogram for testing.\nNo additional configuration required.\n\nSimulated: 64KW memory, 16 program levels,\n14 internal registers, all OPCOM commands.",
                Foreground = TextSecondary,
                FontSize = 13,
                TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                Margin = new Avalonia.Thickness(0, 8)
            };
            _protocolPanel.Children.Add(infoText);
        }
        else
        {
            // Telnet: host, port
            _protocolPanel.Children.Add(CreateLabeledTextBox("Host", out var telHost, "localhost"));
            _protocolPanel.Children.Add(CreateLabeledTextBox("Port", out var telPort, "23"));

            if (telHost != null) { telHost.Text = _viewModel.CurrentProfile.Host; _hostBox = telHost; }
            if (telPort != null) { telPort.Text = _viewModel.CurrentProfile.Port.ToString(); _portBox = telPort; }

            if (telHost != null) telHost.TextChanged += (_, _) => _viewModel.CurrentProfile.Host = telHost.Text ?? "";
            if (telPort != null) telPort.TextChanged += (_, _) =>
            {
                if (int.TryParse(telPort.Text, out var p)) _viewModel.CurrentProfile.Port = p;
            };
        }
    }

    #endregion

    #region Form Loading / Extracting

    private void LoadProfileIntoForm()
    {
        var profile = _viewModel.CurrentProfile;

        if (_nameBox != null) _nameBox.Text = profile.Name;

        // Set the correct protocol radio button
        SetProtocolRadio(profile.Protocol);

        if (_emulatorCombo != null) _emulatorCombo.SelectedItem = profile.EmulatorType;
        if (_sizeCombo != null) _sizeCombo.SelectedItem = profile.Size;
        if (_screenSizeModeCombo != null) _screenSizeModeCombo.SelectedItem = profile.ScreenSizeMode;
        if (_languageCombo != null) _languageCombo.SelectedItem = profile.Language;
        // Match saved colors to a preset name
        if (_colorPresetCombo != null)
        {
            string matchedPreset = "Green Phosphor"; // default
            for (int pi = 0; pi < MainWindow.TerminalColorPresets.Length; pi++)
            {
                var p = MainWindow.TerminalColorPresets[pi];
                if (string.Equals(p.Fg, profile.ForegroundColor, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(p.Bg, profile.BackgroundColor, StringComparison.OrdinalIgnoreCase))
                {
                    matchedPreset = p.Name;
                    break;
                }
            }
            _colorPresetCombo.SelectedItem = matchedPreset;
        }
        if (_gatewaySelectFirstFreeCheck != null) _gatewaySelectFirstFreeCheck.IsChecked = profile.GatewaySelectFirstFree;

        // Keyboard tab: the four settings follow the profile. Loading these fires the change
        // handlers, which would mark a freshly loaded profile dirty and light up Save on a
        // connection nobody has touched - so the handlers are suppressed while they are set.
        _suppressSelectionChange = true;
        if (_backspaceSendsDelCheck != null) _backspaceSendsDelCheck.IsChecked = profile.BackspaceSendsDel;
        if (_deleteSendsDelCheck != null) _deleteSendsDelCheck.IsChecked = profile.DeleteSendsDel;
        if (_newLineReceiveCombo != null) _newLineReceiveCombo.SelectedItem = profile.NewLineReceive;
        if (_newLineTransmitCombo != null) _newLineTransmitCombo.SelectedItem = profile.NewLineTransmit;
        if (_localEchoCheck != null) _localEchoCheck.IsChecked = profile.LocalEcho;
        _suppressSelectionChange = false;

        // Scripts tab: hook selections follow the profile
        SelectScriptInCombo(_onConnectCombo, profile.OnConnectScript);
        SelectScriptInCombo(_onDisconnectCombo, profile.OnDisconnectScript);

        // Protocol tab will update when selected — gateway gets identCode passed through
        UpdateProtocolPanel();

        // Serial-specific
        if (string.Equals(profile.Protocol, "Serial", StringComparison.OrdinalIgnoreCase))
        {
            if (_serialPortCombo != null && !string.IsNullOrEmpty(profile.PortName))
                _serialPortCombo.SelectedItem = profile.PortName;
            if (_baudRateCombo != null) _baudRateCombo.SelectedItem = profile.BaudRate.ToString();
            if (_dataBitsCombo != null) _dataBitsCombo.SelectedItem = profile.DataBits.ToString();
            if (_stopBitsCombo != null)
            {
                var stopText = profile.StopBitsValue switch { 1 => "1", 2 => "2", 3 => "1.5", _ => "1" };
                _stopBitsCombo.SelectedItem = stopText;
            }
            if (_parityCombo != null) _parityCombo.SelectedIndex = profile.ParityValue;
            if (_flowControlCombo != null) _flowControlCombo.SelectedIndex = profile.HandshakeValue;

            // Suppressed while loading: writing .Text fires TextChanged, which would mark a
            // freshly opened profile dirty and light up Save on a connection nobody has touched.
            _suppressSelectionChange = true;
            if (_txDelayCharBox != null)
                _txDelayCharBox.Text = profile.TransmitDelayPerCharMs.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (_txDelayLineBox != null)
                _txDelayLineBox.Text = profile.TransmitDelayPerLineMs.ToString(System.Globalization.CultureInfo.InvariantCulture);
            _suppressSelectionChange = false;
        }
    }

    private void AutoUpdatePort(string protocol)
    {
        if (_portBox == null) return;
        var currentPort = _portBox.Text?.Trim();

        if (string.Equals(protocol, "SSH", StringComparison.OrdinalIgnoreCase))
        {
            if (currentPort == "23" || currentPort == "2323") _portBox.Text = "22";
        }
        else if (string.Equals(protocol, "Telnet", StringComparison.OrdinalIgnoreCase))
        {
            if (currentPort == "22") _portBox.Text = "23";
        }
    }

    private void OnProtocolRadioChanged(string protocol)
    {
        _viewModel.CurrentProfile.Protocol = protocol;
        UpdateProtocolPanel();
        AutoUpdatePort(protocol);
    }

    private void SetProtocolRadio(string protocol)
    {
        if (_telnetRadio == null) return;

        // Suppress events while setting programmatically by just setting IsChecked directly
        if (string.Equals(protocol, "SSH", StringComparison.OrdinalIgnoreCase))
            _sshRadio!.IsChecked = true;
        else if (string.Equals(protocol, "Serial", StringComparison.OrdinalIgnoreCase))
            _serialRadio!.IsChecked = true;
        else if (string.Equals(protocol, "Gateway", StringComparison.OrdinalIgnoreCase))
            _gatewayRadio!.IsChecked = true;
        else if (string.Equals(protocol, "OpcomSimulator", StringComparison.OrdinalIgnoreCase))
            _opcomSimRadio!.IsChecked = true;
        else
            _telnetRadio.IsChecked = true;
    }

    #endregion

    #region Button Handlers

    private void OnNewConnection()
    {
        _viewModel.NewProfile();
        LoadProfileIntoForm();

        var listBox = this.FindControl<ListBox>("ConnectionsListBox");
        if (listBox != null)
        {
            _suppressSelectionChange = true;
            listBox.SelectedIndex = -1;
            _suppressSelectionChange = false;
        }

        var deleteBtn = this.FindControl<Button>("DeleteBtn");
        if (deleteBtn != null) deleteBtn.IsEnabled = false;
    }

    private async Task OnSaveClick()
    {
        try
        {
            await _viewModel.SaveCurrentProfileAsync();

            _suppressSelectionChange = true;
            RebuildConnectionList();
            _suppressSelectionChange = false;

            LoadProfileIntoForm();
        }
        catch (Exception ex)
        {
            await ErrorDialogHelper.ShowErrorDialogAsync(this, $"Failed to save: {ex.Message}");
        }
    }

    private async Task OnConnectClick()
    {
        // If there are unsaved changes, prompt
        if (_viewModel.CurrentProfile.IsDirty && !string.IsNullOrWhiteSpace(_viewModel.CurrentProfile.Name))
        {
            var result = await ErrorDialogHelper.ShowSavePromptAsync(this,
                "Save changes before connecting?");

            if (result == "cancel") return;
            if (result == "save")
            {
                try
                {
                    await _viewModel.SaveCurrentProfileAsync();
                    RebuildConnectionList();
                }
                catch (Exception ex)
                {
                    await ErrorDialogHelper.ShowErrorDialogAsync(this, $"Failed to save: {ex.Message}");
                    return;
                }
            }
        }

        var parameters = _viewModel.CurrentProfile.ToConnectionParameters();

        // Mark as used if this is an existing profile
        if (!_viewModel.CurrentProfile.IsNew && _viewModel.SelectedConfiguration != null)
        {
            _viewModel.SelectedConfiguration.MarkAsUsed();
            try
            {
                var configManager = GetConfigManager();
                if (configManager != null)
                    await configManager.UpdateAsync(_viewModel.SelectedConfiguration);
            }
            catch { /* best effort */ }
        }

        Close(parameters);
    }

    private ConfigurationManager? GetConfigManager()
    {
        // Access via reflection since we only store the ViewModel
        var field = typeof(ConnectionsManagerViewModel).GetField("_configManager",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        return field?.GetValue(_viewModel) as ConfigurationManager;
    }

    private async Task OnDeleteClick()
    {
        if (_viewModel.SelectedConfiguration == null) return;

        var confirmed = await ErrorDialogHelper.ShowConfirmDialogAsync(this,
            "Confirm Delete",
            $"Delete '{_viewModel.SelectedConfiguration.Name}'?",
            "Delete", "Cancel", isDanger: true);

        if (confirmed)
        {
            try
            {
                await _viewModel.DeleteSelectedAsync();
                RebuildConnectionList();

                var deleteBtn = this.FindControl<Button>("DeleteBtn");
                if (deleteBtn != null) deleteBtn.IsEnabled = false;

                // Load defaults
                LoadProfileIntoForm();
            }
            catch (Exception ex)
            {
                await ErrorDialogHelper.ShowErrorDialogAsync(this, $"Failed to delete: {ex.Message}");
            }
        }
    }

    #endregion

    #region Gateway / Serial Helpers

    private void RefreshGatewayTerminalList(int? selectIdentCode = null)
    {
        if (_gatewayStatusLabel == null) return;

        if (_gatewayListener == null || !_gatewayListener.IsListening)
        {
            _gatewayStatusLabel.Text = "Gateway listener is not active. Enable it in Connection > Gateway Settings.";
            return;
        }

        if (!_gatewayListener.IsEmulatorConnected)
        {
            _gatewayStatusLabel.Text = $"Listening on port {_gatewayListener.Port} \u2014 no emulator connected.";
            return;
        }

        var terminals = _gatewayListener.GetTerminals();
        _gatewayStatusLabel.Text = $"Emulator connected \u2014 {terminals.Count} terminal(s). Terminal selection happens in the terminal after connecting.";
    }

    private void RefreshSerialPorts()
    {
        if (_serialPortCombo == null) return;

        try
        {
            var serialType = Type.GetType("RetroTerm.Core.Protocols.Net.SerialConnection, RetroTerm.Core.Protocols.Net");
            string[] ports;
            if (serialType != null)
            {
                var method = serialType.GetMethod("GetAvailablePorts",
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                ports = method?.Invoke(null, null) as string[] ?? Array.Empty<string>();
            }
            else
            {
                ports = Array.Empty<string>();
            }

            var previousSelection = _serialPortCombo.SelectedItem?.ToString();
            _serialPortCombo.ItemsSource = ports;

            if (previousSelection != null && ports.Length > 0)
            {
                for (int i = 0; i < ports.Length; i++)
                {
                    if (ports[i] == previousSelection)
                    {
                        _serialPortCombo.SelectedIndex = i;
                        return;
                    }
                }
            }

            if (ports.Length > 0) _serialPortCombo.SelectedIndex = 0;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to enumerate serial ports: {ex.Message}");
        }
    }

    #endregion

    #region Control Factory Helpers

    private static StackPanel CreateLabeledTextBox(string label, out TextBox? textBox, string watermark = "")
    {
        var panel = new StackPanel { Spacing = 4 };
        panel.Children.Add(new TextBlock
        {
            Text = label,
            Foreground = TextPrimary,
            FontSize = 13,
            FontWeight = FontWeight.SemiBold
        });
        textBox = new TextBox
        {
            PlaceholderText = watermark,
            Background = BgDark,
            Foreground = TextPrimary,
            BorderBrush = BorderDark,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(8, 6),
            FontSize = 13
        };
        panel.Children.Add(textBox);
        return panel;
    }

    private static StackPanel CreateLabeledPasswordBox(string label, out TextBox? textBox)
    {
        var panel = new StackPanel { Spacing = 4 };
        panel.Children.Add(new TextBlock
        {
            Text = label,
            Foreground = TextPrimary,
            FontSize = 13,
            FontWeight = FontWeight.SemiBold
        });
        textBox = new TextBox
        {
            PasswordChar = '\u25CF',
            Background = BgDark,
            Foreground = TextPrimary,
            BorderBrush = BorderDark,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(8, 6),
            FontSize = 13
        };
        panel.Children.Add(textBox);
        return panel;
    }

    private static StackPanel CreateLabeledComboBox(string label, out ComboBox? comboBox, string[] items)
    {
        var panel = new StackPanel { Spacing = 4 };
        panel.Children.Add(new TextBlock
        {
            Text = label,
            Foreground = TextPrimary,
            FontSize = 13,
            FontWeight = FontWeight.SemiBold
        });
        comboBox = new ComboBox
        {
            Width = 200,
            Background = BgDark,
            Foreground = TextPrimary,
            BorderBrush = BorderDark,
            BorderThickness = new Thickness(1),
            SelectedIndex = 0
        };
        comboBox.ItemsSource = items;
        panel.Children.Add(comboBox);
        return panel;
    }

    #endregion
}
