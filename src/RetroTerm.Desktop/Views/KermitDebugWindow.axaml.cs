using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using RetroTerm.Core.Protocols.Kermit;

namespace RetroTerm.Desktop.Views;

public partial class KermitDebugWindow : Window
{
    private KermitTransferStatistics? _statistics;
    private readonly DispatcherTimer _refreshTimer;

    public KermitDebugWindow()
    {
        InitializeComponent();

        _refreshTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(500)
        };
        _refreshTimer.Tick += OnRefreshTick;
        _refreshTimer.Start();
    }

    public void SetStatistics(KermitTransferStatistics? statistics)
    {
        if (_statistics != null)
            _statistics.Updated -= OnStatisticsUpdated;

        _statistics = statistics;

        if (_statistics != null)
            _statistics.Updated += OnStatisticsUpdated;

        Dispatcher.UIThread.Post(RefreshAll);
    }

    private void OnStatisticsUpdated()
    {
        Dispatcher.UIThread.Post(RefreshCounters);
    }

    private void OnRefreshTick(object? sender, EventArgs e)
    {
        RefreshAll();
    }

    private void RefreshAll()
    {
        RefreshCounters();
        RefreshElapsed();
    }

    private void RefreshCounters()
    {
        var stateLabel = this.FindControl<TextBlock>("StateLabel");
        var currentFileLabel = this.FindControl<TextBlock>("CurrentFileLabel");
        var fileBytesLabel = this.FindControl<TextBlock>("FileBytesLabel");
        var charsSentLabel = this.FindControl<TextBlock>("CharsSentLabel");
        var charsReceivedLabel = this.FindControl<TextBlock>("CharsReceivedLabel");
        var baudRateLabel = this.FindControl<TextBlock>("BaudRateLabel");
        var packetsSentLabel = this.FindControl<TextBlock>("PacketsSentLabel");
        var packetsReceivedLabel = this.FindControl<TextBlock>("PacketsReceivedLabel");
        var retriesLabel = this.FindControl<TextBlock>("RetriesLabel");
        var timeoutsLabel = this.FindControl<TextBlock>("TimeoutsLabel");
        var ebqLabel = this.FindControl<TextBlock>("EbqLabel");
        var maxSendLabel = this.FindControl<TextBlock>("MaxSendLabel");
        var blockCheckLabel = this.FindControl<TextBlock>("BlockCheckLabel");
        var errorBorder = this.FindControl<Border>("ErrorBorder");
        var errorLabel = this.FindControl<TextBlock>("ErrorLabel");

        if (_statistics == null)
        {
            if (stateLabel != null) stateLabel.Text = "No transfer active";
            if (currentFileLabel != null) currentFileLabel.Text = "-";
            if (fileBytesLabel != null) fileBytesLabel.Text = "0";
            if (charsSentLabel != null) charsSentLabel.Text = "0";
            if (charsReceivedLabel != null) charsReceivedLabel.Text = "0";
            if (baudRateLabel != null) baudRateLabel.Text = "0 baud";
            if (packetsSentLabel != null) packetsSentLabel.Text = "0";
            if (packetsReceivedLabel != null) packetsReceivedLabel.Text = "0";
            if (retriesLabel != null) retriesLabel.Text = "0";
            if (timeoutsLabel != null) timeoutsLabel.Text = "0";
            if (ebqLabel != null) ebqLabel.Text = "-";
            if (maxSendLabel != null) maxSendLabel.Text = "-";
            if (blockCheckLabel != null) blockCheckLabel.Text = "-";
            if (errorBorder != null) errorBorder.IsVisible = false;
            return;
        }

        if (stateLabel != null) stateLabel.Text = _statistics.StateName;
        if (currentFileLabel != null) currentFileLabel.Text = string.IsNullOrEmpty(_statistics.CurrentFile) ? "-" : _statistics.CurrentFile;
        if (fileBytesLabel != null) fileBytesLabel.Text = _statistics.FileBytes.ToString("N0");
        if (charsSentLabel != null) charsSentLabel.Text = _statistics.CharsSent.ToString("N0");
        if (charsReceivedLabel != null) charsReceivedLabel.Text = _statistics.CharsReceived.ToString("N0");
        if (baudRateLabel != null) baudRateLabel.Text = $"{_statistics.EffectiveBaud:N0} baud";
        if (packetsSentLabel != null) packetsSentLabel.Text = _statistics.PacketsSent.ToString("N0");
        if (packetsReceivedLabel != null) packetsReceivedLabel.Text = _statistics.PacketsReceived.ToString("N0");
        if (retriesLabel != null) retriesLabel.Text = _statistics.Retries.ToString("N0");
        if (timeoutsLabel != null) timeoutsLabel.Text = _statistics.Timeouts.ToString("N0");

        if (ebqLabel != null)
            ebqLabel.Text = _statistics.Use8BitQuoting ? "ON" : "OFF";
        if (maxSendLabel != null)
            maxSendLabel.Text = _statistics.MaxSendDataLength > 0 ? _statistics.MaxSendDataLength.ToString() : "-";
        if (blockCheckLabel != null)
            blockCheckLabel.Text = _statistics.BlockCheckType > 0 ? _statistics.BlockCheckType.ToString() : "-";

        if (errorBorder != null && errorLabel != null)
        {
            if (!string.IsNullOrEmpty(_statistics.LastError))
            {
                errorBorder.IsVisible = true;
                errorLabel.Text = _statistics.LastError;
            }
            else
            {
                errorBorder.IsVisible = false;
            }
        }
    }

    private void RefreshElapsed()
    {
        var elapsedLabel = this.FindControl<TextBlock>("ElapsedLabel");
        if (elapsedLabel == null) return;

        if (_statistics == null)
        {
            elapsedLabel.Text = "00:00:00";
            return;
        }

        var elapsed = _statistics.Elapsed;
        elapsedLabel.Text = $"{(int)elapsed.TotalHours:D2}:{elapsed.Minutes:D2}:{elapsed.Seconds:D2}";
    }

    private void OnResetClick(object? sender, RoutedEventArgs e)
    {
        _statistics?.Reset();
        RefreshAll();
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    protected override void OnClosed(EventArgs e)
    {
        _refreshTimer.Stop();
        if (_statistics != null)
            _statistics.Updated -= OnStatisticsUpdated;
        base.OnClosed(e);
    }
}
