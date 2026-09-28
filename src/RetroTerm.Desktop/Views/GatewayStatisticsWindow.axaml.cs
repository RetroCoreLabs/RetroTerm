using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using RetroTerm.Core.Protocols.WebSocket.Gateway;

namespace RetroTerm.Desktop.Views;

public partial class GatewayStatisticsWindow : Window
{
    private readonly GatewayListener _listener;
    private readonly DispatcherTimer _refreshTimer;

    public GatewayStatisticsWindow(GatewayListener listener)
    {
        _listener = listener;
        InitializeComponent();

        // Subscribe to statistics updates for immediate disk I/O refresh
        _listener.StatisticsUpdated += OnStatisticsUpdated;
        _listener.EmulatorConnected += OnConnectionStateChanged;
        _listener.EmulatorDisconnected += OnConnectionStateChanged;
        _listener.DiskWorkerConnected += OnConnectionStateChanged;
        _listener.DiskWorkerDisconnected += OnConnectionStateChanged;

        // Timer for uptime counter refresh (every 1 second)
        _refreshTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _refreshTimer.Tick += OnRefreshTimerTick;
        _refreshTimer.Start();

        // Initial update
        RefreshAll();
    }

    private void OnStatisticsUpdated()
    {
        Dispatcher.UIThread.Post(RefreshCounters);
    }

    private void OnConnectionStateChanged()
    {
        Dispatcher.UIThread.Post(RefreshAll);
    }

    private void OnRefreshTimerTick(object? sender, EventArgs e)
    {
        RefreshUptime();
    }

    private void RefreshAll()
    {
        RefreshConnectionStatus();
        RefreshCounters();
        RefreshUptime();
    }

    private void RefreshConnectionStatus()
    {
        var emulatorStatus = this.FindControl<TextBlock>("EmulatorStatus");
        var diskWorkerStatus = this.FindControl<TextBlock>("DiskWorkerStatus");

        if (emulatorStatus != null)
        {
            emulatorStatus.Text = _listener.IsEmulatorConnected ? "Connected" : "Not connected";
            emulatorStatus.Foreground = _listener.IsEmulatorConnected
                ? Avalonia.Media.Brushes.LightGreen
                : Avalonia.Media.Brushes.Gray;
        }

        if (diskWorkerStatus != null)
        {
            diskWorkerStatus.Text = _listener.IsDiskWorkerConnected ? "Connected" : "Not connected";
            diskWorkerStatus.Foreground = _listener.IsDiskWorkerConnected
                ? Avalonia.Media.Brushes.LightGreen
                : Avalonia.Media.Brushes.Gray;
        }
    }

    private void RefreshCounters()
    {
        var stats = _listener.Statistics;

        var termInput = this.FindControl<TextBlock>("TermInputLabel");
        var termOutput = this.FindControl<TextBlock>("TermOutputLabel");
        var diskRead = this.FindControl<TextBlock>("DiskReadLabel");
        var diskWrite = this.FindControl<TextBlock>("DiskWriteLabel");
        var diskError = this.FindControl<TextBlock>("DiskErrorLabel");
        var clientConnects = this.FindControl<TextBlock>("ClientConnectsLabel");
        var clientDisconnects = this.FindControl<TextBlock>("ClientDisconnectsLabel");

        if (termInput != null)
            termInput.Text = $"{stats.TermInputFrames:N0} frames / {FormatBytes(stats.TermInputBytes)}";
        if (termOutput != null)
            termOutput.Text = $"{stats.TermOutputFrames:N0} frames / {FormatBytes(stats.TermOutputBytes)}";
        if (diskRead != null)
            diskRead.Text = $"{stats.DiskReadOps:N0} ops / {FormatBytes(stats.DiskReadBytes)}";
        if (diskWrite != null)
            diskWrite.Text = $"{stats.DiskWriteOps:N0} ops / {FormatBytes(stats.DiskWriteBytes)}";
        if (diskError != null)
            diskError.Text = stats.DiskErrors.ToString("N0");
        if (clientConnects != null)
            clientConnects.Text = stats.ClientConnects.ToString("N0");
        if (clientDisconnects != null)
            clientDisconnects.Text = stats.ClientDisconnects.ToString("N0");
    }

    private void RefreshUptime()
    {
        var uptimeLabel = this.FindControl<TextBlock>("UptimeLabel");
        if (uptimeLabel == null) return;

        var connectedSince = _listener.Statistics.EmulatorConnectedSince;
        if (connectedSince.HasValue)
        {
            var elapsed = DateTime.UtcNow - connectedSince.Value;
            uptimeLabel.Text = $"{(int)elapsed.TotalHours:D2}:{elapsed.Minutes:D2}:{elapsed.Seconds:D2}";
        }
        else
        {
            uptimeLabel.Text = "--:--:--";
        }
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes < 1024)
            return $"{bytes} B";
        if (bytes < 1024 * 1024)
            return $"{bytes / 1024.0:F1} KB";
        if (bytes < 1024L * 1024 * 1024)
            return $"{bytes / (1024.0 * 1024):F1} MB";
        return $"{bytes / (1024.0 * 1024 * 1024):F2} GB";
    }

    private void OnResetClick(object? sender, RoutedEventArgs e)
    {
        _listener.Statistics.Reset();

        // Restore connection times if still connected
        if (_listener.IsEmulatorConnected)
            _listener.Statistics.EmulatorConnectedSince = DateTime.UtcNow;
        if (_listener.IsDiskWorkerConnected)
            _listener.Statistics.DiskWorkerConnectedSince = DateTime.UtcNow;

        RefreshAll();
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    protected override void OnClosed(EventArgs e)
    {
        _refreshTimer.Stop();
        _listener.StatisticsUpdated -= OnStatisticsUpdated;
        _listener.EmulatorConnected -= OnConnectionStateChanged;
        _listener.EmulatorDisconnected -= OnConnectionStateChanged;
        _listener.DiskWorkerConnected -= OnConnectionStateChanged;
        _listener.DiskWorkerDisconnected -= OnConnectionStateChanged;
        base.OnClosed(e);
    }
}
