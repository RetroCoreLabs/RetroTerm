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
    private int _messageCount;

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

    private void AddLogEntry(string direction, string message)
    {
        _messageCount++;
        var timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
        var entry = $"[{timestamp}] [{direction}] {message}";
        _logItems.Add(entry);

        // Keep max 1000 entries
        while (_logItems.Count > 1000)
        {
            _logItems.RemoveAt(0);
        }

        // Auto-scroll
        var autoScrollCheckBox = this.FindControl<CheckBox>("AutoScrollCheckBox");
        if (autoScrollCheckBox?.IsChecked == true)
        {
            var scrollViewer = this.FindControl<ScrollViewer>("LogScrollViewer");
            if (scrollViewer != null)
            {
                Dispatcher.UIThread.Post(() => scrollViewer.ScrollToEnd(), DispatcherPriority.Background);
            }
        }
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

        statusLabel.Text = parts.ToString();
    }

    private void OnClearClick(object? sender, RoutedEventArgs e)
    {
        _logItems.Clear();
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
        _listener.DebugMessage -= OnDebugMessage;
        _listener.EmulatorConnected -= OnEmulatorStateChanged;
        _listener.EmulatorDisconnected -= OnEmulatorStateChanged;
        _listener.TerminalListChanged -= OnEmulatorStateChanged;
        base.OnClosed(e);
    }
}
