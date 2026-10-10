using System;
using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input.Platform; // ClipboardExtensions.SetTextAsync (an extension method since Avalonia 12)
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using RetroTerm.Core.Protocols.WebSocket.Gateway;

namespace RetroTerm.Desktop.Views;

public partial class GatewayDebugWindow : Window
{
    private readonly GatewayListener _listener;
    private readonly ObservableCollection<string> _logItems = new();

    // Everything captured (newest 1000), with its category; _logItems is the filtered view.
    private readonly System.Collections.Generic.List<(string Category, string Line)> _allEntries = new();
    private int _messageCount;

    // Auto-scroll off: lines keep arriving, but the view neither jumps to the end nor drops
    // lines off the top (dropping them shifts what is on screen, which looks like scrolling).
    // The 1000-line limit is applied again when auto-scroll is switched back on.
    private readonly DispatcherTimer _statusTimer = new() { Interval = TimeSpan.FromSeconds(1) };

    // Color brushes for direction-based coloring (applied via text prefix)
    private static readonly IBrush TxColor = new SolidColorBrush(Color.Parse("#4EC9B0"));   // teal for TX
    private static readonly IBrush RxColor = new SolidColorBrush(Color.Parse("#DCDCAA"));   // yellow for RX
    private static readonly IBrush EvtColor = new SolidColorBrush(Color.Parse("#C586C0"));  // purple for EVT
    private static readonly IBrush DefaultColor = new SolidColorBrush(Color.Parse("#CCCCCC"));

    public GatewayDebugWindow(GatewayListener listener)
    {
        _listener = listener;
        InitializeComponent();

        var logItemsControl = this.FindControl<ItemsControl>("LogItemsControl");
        if (logItemsControl != null)
        {
            logItemsControl.ItemsSource = _logItems;
        }

        // Subscribe to debug messages
        _listener.DebugMessage += OnDebugMessage;

        _statusTimer.Tick += (_, _) => UpdateStatus();
        _statusTimer.Start();

        // Update status
        UpdateStatus();
        _listener.EmulatorConnected += OnEmulatorStateChanged;
        _listener.EmulatorDisconnected += OnEmulatorStateChanged;
        _listener.TerminalListChanged += OnEmulatorStateChanged;

        // Add initial entry
        AddLogEntry("EVT", "Debug window opened");
        AddLogEntry("EVT", $"Listener: {(_listener.IsListening ? $"port {_listener.Port}" : "stopped")}, " +
                           $"Emulator: {(_listener.IsEmulatorConnected ? "connected" : "not connected")}, " +
                           $"Terminals: {_listener.TerminalCount}");
    }

    private void OnDebugMessage(string direction, string message)
    {
        Dispatcher.UIThread.Post(() => AddLogEntry(direction, message));
    }

    private void OnEmulatorStateChanged()
    {
        Dispatcher.UIThread.Post(UpdateStatus);
    }

    /// <summary>Which filter checkbox a message belongs to, from the label the gateway gives it.</summary>
    private static string CategoryOf(string direction, string message)
    {
        if (direction == "EVT") return "Events";
        if (message.Contains("Unknown", StringComparison.OrdinalIgnoreCase)) return "Unknown";
        if (message.Contains("eth-", StringComparison.OrdinalIgnoreCase)) return "Ethernet";
        if (message.Contains("hdlc", StringComparison.OrdinalIgnoreCase)) return "HDLC";
        if (message.Contains("disk", StringComparison.OrdinalIgnoreCase)) return "Disk";
        if (message.Contains("term", StringComparison.OrdinalIgnoreCase)) return "Terminal";
        return "Other";
    }

    private bool IsShown(string category, string line)
    {
        var box = this.FindControl<Avalonia.Controls.Primitives.ToggleButton>("Filter" + category);
        if (box != null && box.IsChecked != true) return false;
        var text = this.FindControl<TextBox>("FilterText")?.Text;
        return string.IsNullOrWhiteSpace(text) || line.Contains(text.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    private void RebuildView()
    {
        _logItems.Clear();
        foreach (var (category, line) in _allEntries)
        {
            if (IsShown(category, line)) _logItems.Add(line);
        }
    }

    private void OnFilterChanged(object? sender, RoutedEventArgs e) => RebuildView();

    private void OnFilterTextChanged(object? sender, TextChangedEventArgs e) => RebuildView();


    private void AddLogEntry(string direction, string message)
    {
        _messageCount++;
        var timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
        var entry = $"[{timestamp}] [{direction}] {message}";
        string category = CategoryOf(direction, message);

        _allEntries.Add((category, entry));
        bool following = IsFollowing();
        if (following) TrimToLimit();
        else if (_allEntries.Count > 20000) TrimToLimit();   // a hard ceiling while reading

        if (!IsShown(category, entry)) return;
        _logItems.Add(entry);
        if (!following) return;

        ScrollToEnd();
    }

    /// <summary>Keep the newest 1000 captured lines, and drop from the view what was dropped.</summary>
    private void TrimToLimit()
    {
        while (_allEntries.Count > 1000)
        {
            var dropped = _allEntries[0];
            _allEntries.RemoveAt(0);
            if (_logItems.Count > 0 && ReferenceEquals(_logItems[0], dropped.Line)) _logItems.RemoveAt(0);
        }
    }

    private bool IsFollowing() => this.FindControl<CheckBox>("AutoScrollCheckBox")?.IsChecked == true;

    private void ScrollToEnd()
    {
        var scrollViewer = this.FindControl<ScrollViewer>("LogScrollViewer");
        if (scrollViewer != null)
        {
            Dispatcher.UIThread.Post(() => scrollViewer.ScrollToEnd(), DispatcherPriority.Background);
        }
    }

    private void OnAutoScrollChanged(object? sender, RoutedEventArgs e)
    {
        if (!IsFollowing()) return;

        // Back on: apply the line limit again and follow the end.
        TrimToLimit();
        ScrollToEnd();
        UpdateStatus();
    }

    private void UpdateStatus()
    {
        var statusLabel = this.FindControl<TextBlock>("StatusLabel");
        if (statusLabel == null) return;

        var parts = new System.Text.StringBuilder();
        if (_listener.IsListening)
            parts.Append($"Listening :{_listener.Port}");
        else
            parts.Append("Stopped");

        parts.Append(" | ");

        if (_listener.IsEmulatorConnected)
            parts.Append($"Emulator connected ({_listener.TerminalCount} terminals)");
        else
            parts.Append("No emulator");

        parts.Append($" | {_messageCount} messages");
        if (!IsFollowing()) parts.Append(" | not scrolling");

        statusLabel.Text = parts.ToString();
    }

    private void OnClearClick(object? sender, RoutedEventArgs e)
    {
        _logItems.Clear();
        _allEntries.Clear();
        _messageCount = 0;
        UpdateStatus();
    }

    private async void OnCopyClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < _logItems.Count; i++)
            {
                sb.AppendLine(_logItems[i]);
            }

            var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
            if (clipboard != null)
            {
                await clipboard.SetTextAsync(sb.ToString());

                var button = sender as Button;
                if (button != null)
                {
                    var originalContent = button.Content;
                    button.Content = "Copied!";
                    await System.Threading.Tasks.Task.Delay(1500);
                    button.Content = originalContent;
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to copy to clipboard: {ex.Message}");
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        _statusTimer.Stop();
        _listener.DebugMessage -= OnDebugMessage;
        _listener.EmulatorConnected -= OnEmulatorStateChanged;
        _listener.EmulatorDisconnected -= OnEmulatorStateChanged;
        _listener.TerminalListChanged -= OnEmulatorStateChanged;
        base.OnClosed(e);
    }
}
