using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using RetroTerm.Core.Protocols.WebSocket.Gateway;

namespace RetroTerm.Desktop.Views;

/// <summary>
/// Settings dialog for the ND-100 Gateway WebSocket listener.
/// Allows enabling/disabling the listener, configuring the port,
/// and showing connection status.
/// </summary>
public class GatewaySettingsDialog
{
    private static readonly SolidColorBrush BgDark = new(Color.Parse("#1E1E1E"));
    private static readonly SolidColorBrush BgPanel = new(Color.Parse("#252526"));
    private static readonly SolidColorBrush BorderDark = new(Color.Parse("#3C3C3C"));
    private static readonly SolidColorBrush TextPrimary = new(Color.Parse("#CCCCCC"));
    private static readonly SolidColorBrush TextSecondary = new(Color.Parse("#999999"));
    private static readonly SolidColorBrush GreenActive = new(Color.Parse("#4EC9B0"));
    private static readonly SolidColorBrush RedStopped = new(Color.Parse("#D83B01"));

    private readonly Window _parentWindow;
    private readonly GatewayListener _listener;
    private readonly GatewaySettings _settings;

    private CheckBox? _enabledCheckBox;
    private TextBox? _portBox;
    private TextBlock? _statusText;
    private Window? _dialog;

    public GatewaySettingsDialog(Window parentWindow, GatewayListener listener, GatewaySettings settings)
    {
        _parentWindow = parentWindow;
        _listener = listener;
        _settings = settings;
    }

    /// <summary>
    /// Returns true if settings were changed and applied.
    /// </summary>
    public async Task<bool> ShowAsync()
    {
        _dialog = new Window
        {
            Title = "Gateway Settings",
            Width = 420,
            Height = 320,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
            Background = BgDark
        };

        var mainPanel = new StackPanel
        {
            Margin = new Thickness(24),
            Spacing = 16
        };

        // Header
        mainPanel.Children.Add(new TextBlock
        {
            Text = "ND-100 Gateway WebSocket Listener",
            FontSize = 16,
            FontWeight = FontWeight.Bold,
            Foreground = TextPrimary
        });

        mainPanel.Children.Add(new TextBlock
        {
            Text = "Allows the ND-100 emulator to connect directly to RetroTerm via WebSocket.",
            FontSize = 12,
            Foreground = TextSecondary,
            TextWrapping = TextWrapping.Wrap
        });

        // Enable checkbox
        _enabledCheckBox = new CheckBox
        {
            Content = "Enable Gateway Listener",
            IsChecked = _settings.Enabled,
            Foreground = TextPrimary,
            Margin = new Thickness(0, 8, 0, 0)
        };
        mainPanel.Children.Add(_enabledCheckBox);

        // Port setting
        var portRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8
        };
        portRow.Children.Add(new TextBlock
        {
            Text = "Port:",
            Width = 60,
            Foreground = TextPrimary,
            VerticalAlignment = VerticalAlignment.Center
        });

        _portBox = new TextBox
        {
            Width = 100,
            Text = _settings.Port.ToString(),
            Background = BgDark,
            Foreground = TextPrimary,
            BorderBrush = BorderDark,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(8, 6)
        };
        portRow.Children.Add(_portBox);
        mainPanel.Children.Add(portRow);

        // Status display
        var statusPanel = new Border
        {
            Background = BgPanel,
            BorderBrush = BorderDark,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(12, 8),
            Margin = new Thickness(0, 8, 0, 0)
        };

        _statusText = new TextBlock
        {
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap
        };
        UpdateStatusDisplay();
        statusPanel.Child = _statusText;
        mainPanel.Children.Add(statusPanel);

        // Buttons
        var buttonPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 12,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 16, 0, 0)
        };

        // The shared dialog button classes, not hand brushes: a brush set on the Button itself is
        // lost under the mouse (see the note above the Button styles in DarkTheme.axaml).
        var cancelBtn = new Button
        {
            Content = "Cancel",
            Classes = { "dialog-secondary" }
        };
        cancelBtn.Click += (_, _) => _dialog.Close(false);

        var applyBtn = new Button
        {
            Content = "Apply",
            Classes = { "dialog-primary" }
        };
        applyBtn.Click += async (_, _) => await OnApplyClick();

        buttonPanel.Children.Add(cancelBtn);
        buttonPanel.Children.Add(applyBtn);
        mainPanel.Children.Add(buttonPanel);

        _dialog.Content = mainPanel;

        // Subscribe to listener events for live status updates
        _listener.EmulatorConnected += OnListenerStatusChanged;
        _listener.EmulatorDisconnected += OnListenerStatusChanged;
        _listener.TerminalListChanged += OnListenerStatusChanged;

        try
        {
            var result = await _dialog.ShowDialog<bool?>(_parentWindow);
            return result == true;
        }
        finally
        {
            _listener.EmulatorConnected -= OnListenerStatusChanged;
            _listener.EmulatorDisconnected -= OnListenerStatusChanged;
            _listener.TerminalListChanged -= OnListenerStatusChanged;
        }
    }

    private void OnListenerStatusChanged()
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(UpdateStatusDisplay);
    }

    private void UpdateStatusDisplay()
    {
        if (_statusText == null) return;

        if (!_listener.IsListening)
        {
            _statusText.Text = "Status: Stopped";
            _statusText.Foreground = RedStopped;
        }
        else if (_listener.IsEmulatorConnected)
        {
            int count = _listener.TerminalCount;
            var termWord = count == 1 ? "terminal" : "terminals";
            _statusText.Text = $"Status: Emulator connected ({count} {termWord})";
            _statusText.Foreground = GreenActive;
        }
        else
        {
            _statusText.Text = $"Status: Listening on port {_listener.Port}";
            _statusText.Foreground = TextPrimary;
        }
    }

    private async Task OnApplyClick()
    {
        if (_enabledCheckBox == null || _portBox == null || _dialog == null)
            return;

        bool enabled = _enabledCheckBox.IsChecked == true;
        if (!int.TryParse(_portBox.Text?.Trim(), out var port) || port < 1 || port > 65535)
        {
            _portBox.BorderBrush = RedStopped;
            return;
        }

        bool configChanged = enabled != _settings.Enabled || port != _settings.Port;

        _settings.Enabled = enabled;
        _settings.Port = port;

        _settings.Save();

        if (configChanged)
        {
            // Restart listener with new settings
            if (_listener.IsListening)
                await _listener.StopAsync();

            if (enabled)
                await _listener.StartAsync(port);
        }

        _dialog.Close(true);
    }
}
