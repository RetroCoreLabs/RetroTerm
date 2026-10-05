using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Text;
using Avalonia.Controls;
using Avalonia.Input.Platform; // ClipboardExtensions.SetTextAsync (an extension method since Avalonia 12)
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using RetroTerm.Core.Logging;

namespace RetroTerm.Desktop.Views;

/// <summary>
/// Two-pane protocol trace: decoded escape sequences / control codes / text runs on the left,
/// raw network blocks on the right.
/// </summary>
/// <remarks>
/// Capture is driven by <see cref="ProtocolTracer"/>, which is enabled while this window is
/// open and disabled again when it closes, so a normal session pays nothing for it.
/// </remarks>
public partial class ProtocolMonitorWindow : Window
{
    private const int MaxDisplayedRows = 8000;

    private readonly ObservableCollection<LogLineRow> _decodedRows = new();
    private readonly ObservableCollection<LogLineRow> _rawRows = new();

    private ListBox? _decodedList;
    private ListBox? _rawList;
    private TextBox? _searchBox;
    private TextBlock? _statusText;
    private CheckBox? _followCheck;
    private CheckBox? _showHexCheck;

    private string _searchFilter = string.Empty;

    // Remembers whether the tracer was already on, so closing this window does not switch off
    // a capture that something else started.
    private readonly bool _tracerWasEnabled;

    public ProtocolMonitorWindow()
    {
        InitializeComponent();
        if (this.FindControl<Button>("SaveButton") is { } saveButton)
            ToolTip.SetTip(saveButton, $"Write the whole trace to {ApplicationLogger.LogDirectory}");

        _decodedList = this.FindControl<ListBox>("DecodedListBox");
        _rawList = this.FindControl<ListBox>("RawListBox");
        _searchBox = this.FindControl<TextBox>("SearchBox");
        _statusText = this.FindControl<TextBlock>("StatusText");
        _followCheck = this.FindControl<CheckBox>("FollowCheck");
        _showHexCheck = this.FindControl<CheckBox>("ShowHexCheck");

        if (_decodedList != null)
        {
            _decodedList.ItemsSource = _decodedRows;
            _decodedList.KeyDown += OnListKeyDown;
        }

        if (_rawList != null)
        {
            _rawList.ItemsSource = _rawRows;
            _rawList.KeyDown += OnListKeyDown;
        }

        _tracerWasEnabled = ProtocolTracer.Enabled;
        ProtocolTracer.Enabled = true;
        ProtocolTracer.TraceAdded += OnTraceAdded;

        Refresh();
    }

    private void OnTraceAdded(ProtocolTraceEntry entry)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            AddEntry(entry);
            Trim();
            ScrollIfFollowing();
            UpdateStatus();
        });
    }

    private void AddEntry(ProtocolTraceEntry entry)
    {
        if (entry.Kind == TraceKind.RawBlock)
        {
            if (!PassesDirection(entry))
                return;

            _rawRows.Add(new LogLineRow
            {
                Text = $"{entry.Timestamp:HH:mm:ss.fff} {entry.DirectionTag} {entry.Bytes.Length,5}  {entry.ToHex(48)}",
                Brush = BrushForDirection(entry.Direction)
            });
            return;
        }

        if (!PassesFilters(entry))
            return;

        _decodedRows.Add(new LogLineRow
        {
            Text = entry.Format(_showHexCheck?.IsChecked == true),
            Brush = BrushForEntry(entry)
        });
    }

    private bool PassesDirection(ProtocolTraceEntry entry)
    {
        if (entry.Direction == TraceDirection.Rx && !IsChecked("ShowRxCheck"))
            return false;

        if (entry.Direction == TraceDirection.Tx && !IsChecked("ShowTxCheck"))
            return false;

        return true;
    }

    private bool PassesFilters(ProtocolTraceEntry entry)
    {
        if (!PassesDirection(entry))
            return false;

        if (IsChecked("UnknownOnlyCheck") && entry.IsKnown)
            return false;

        switch (entry.Kind)
        {
            case TraceKind.Text:
                if (!IsChecked("ShowTextCheck")) return false;
                break;
            case TraceKind.Control:
                if (!IsChecked("ShowControlCheck")) return false;
                break;
            case TraceKind.Sequence:
            case TraceKind.StringCommand:
                if (!IsChecked("ShowSequenceCheck")) return false;
                break;
        }

        if (_searchFilter.Length != 0)
        {
            if (entry.Format(true).IndexOf(_searchFilter, StringComparison.OrdinalIgnoreCase) < 0)
                return false;
        }

        return true;
    }

    private static IBrush BrushForEntry(ProtocolTraceEntry entry)
    {
        // Unrecognised sequences are the point of the exercise, so they win over direction.
        if (!entry.IsKnown)
            return Brushes.Gold;

        if (entry.Kind == TraceKind.Text)
            return entry.Direction == TraceDirection.Rx ? Brushes.Gainsboro : Brushes.LightSkyBlue;

        return BrushForDirection(entry.Direction);
    }

    private static IBrush BrushForDirection(TraceDirection direction)
    {
        return direction == TraceDirection.Rx ? Brushes.MediumSpringGreen : Brushes.DeepSkyBlue;
    }

    private bool IsChecked(string name)
    {
        var box = this.FindControl<CheckBox>(name);
        return box?.IsChecked == true;
    }

    private void Trim()
    {
        while (_decodedRows.Count > MaxDisplayedRows)
            _decodedRows.RemoveAt(0);

        while (_rawRows.Count > MaxDisplayedRows)
            _rawRows.RemoveAt(0);
    }

    private void ScrollIfFollowing()
    {
        if (_followCheck?.IsChecked != true)
            return;

        if (_decodedList != null && _decodedRows.Count != 0)
            _decodedList.ScrollIntoView(_decodedRows[_decodedRows.Count - 1]);

        if (_rawList != null && _rawRows.Count != 0)
            _rawList.ScrollIntoView(_rawRows[_rawRows.Count - 1]);
    }

    private void Refresh()
    {
        _decodedRows.Clear();
        _rawRows.Clear();

        var entries = ProtocolTracer.GetEntries();
        for (int i = 0; i < entries.Length; i++)
        {
            AddEntry(entries[i]);
        }

        Trim();
        ScrollIfFollowing();
        UpdateStatus();
    }

    private void UpdateStatus()
    {
        if (_statusText == null)
            return;

        int unknown = 0;
        for (int i = 0; i < _decodedRows.Count; i++)
        {
            // Gold is the unknown-sequence colour; counting rows avoids a second pass over
            // the whole tracer buffer on every update.
            if (ReferenceEquals(_decodedRows[i].Brush, Brushes.Gold))
                unknown++;
        }

        var sb = new StringBuilder(120);
        sb.Append(_decodedRows.Count).Append(" decoded  |  ");
        sb.Append(_rawRows.Count).Append(" raw blocks");

        if (unknown != 0)
            sb.Append("  |  ").Append(unknown).Append(" UNKNOWN");

        int selected = _decodedList?.SelectedItems?.Count ?? 0;
        if (selected > 0)
            sb.Append("  |  ").Append(selected).Append(" selected");

        sb.Append(ProtocolTracer.Enabled ? "  |  capturing" : "  |  paused");

        _statusText.Text = sb.ToString();
    }

    private void OnCaptureToggled(object? sender, RoutedEventArgs e)
    {
        ProtocolTracer.Enabled = (sender as ToggleButton)?.IsChecked == true;

        if (sender is ToggleButton toggle)
            toggle.Content = ProtocolTracer.Enabled ? "Capturing" : "Paused";

        UpdateStatus();
    }

    private void OnFilterChanged(object? sender, RoutedEventArgs e)
    {
        Refresh();
    }

    private void OnSearchChanged(object? sender, TextChangedEventArgs e)
    {
        _searchFilter = _searchBox?.Text ?? string.Empty;
        Refresh();
    }

    private void OnClearClick(object? sender, RoutedEventArgs e)
    {
        ProtocolTracer.Clear();
        _decodedRows.Clear();
        _rawRows.Clear();
        UpdateStatus();
    }

    private async void OnSaveClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            var dir = ApplicationLogger.LogDirectory;
            Directory.CreateDirectory(dir);

            var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
            var path = Path.Combine(dir, $"trace-{stamp}.log");

            var entries = ProtocolTracer.GetEntries();
            var sb = new StringBuilder(entries.Length * 90);
            for (int i = 0; i < entries.Length; i++)
            {
                sb.AppendLine(entries[i].Format(includeHex: true));
            }

            File.WriteAllText(path, sb.ToString(), Encoding.UTF8);

            if (sender is Button button)
            {
                var original = button.Content;
                button.Content = "Saved";
                await System.Threading.Tasks.Task.Delay(1500);
                button.Content = original;
            }

            ApplicationLogger.Log(LogCategory.UI, LogLevel.Info, "ProtocolMonitor",
                $"Trace saved to {path}");
        }
        catch (Exception ex)
        {
            ApplicationLogger.Log(LogCategory.UI, LogLevel.Error, "ProtocolMonitor",
                $"Failed to save trace: {ex.Message}");
        }
    }

    private void OnListKeyDown(object? sender, KeyEventArgs e)
    {
        if (sender is not ListBox list)
            return;

        if (e.Key == Key.C && (e.KeyModifiers & KeyModifiers.Control) != 0)
        {
            CopySelectedFrom(list, null);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.A && (e.KeyModifiers & KeyModifiers.Control) != 0)
        {
            list.SelectAll();
            UpdateStatus();
            e.Handled = true;
        }
    }

    private async void OnCopyAllClick(object? sender, RoutedEventArgs e)
    {
        var sb = new StringBuilder(_decodedRows.Count * 90);
        for (int i = 0; i < _decodedRows.Count; i++)
        {
            sb.AppendLine(_decodedRows[i].Text);
        }

        await CopyToClipboard(sb.ToString(), sender as Button, _decodedRows.Count);
    }

    private async void OnCopySelectedClick(object? sender, RoutedEventArgs e)
    {
        // Copy whichever pane has a selection, preferring the decoded pane.
        if (_decodedList?.SelectedItems?.Count > 0)
        {
            await CopySelectedFromCore(_decodedList, _decodedRows, sender as Button);
            return;
        }

        if (_rawList?.SelectedItems?.Count > 0)
            await CopySelectedFromCore(_rawList, _rawRows, sender as Button);
    }

    private async void CopySelectedFrom(ListBox list, Button? button)
    {
        var source = ReferenceEquals(list, _rawList) ? _rawRows : _decodedRows;
        await CopySelectedFromCore(list, source, button);
    }

    private async System.Threading.Tasks.Task CopySelectedFromCore(
        ListBox list,
        ObservableCollection<LogLineRow> source,
        Button? button)
    {
        var selected = list.SelectedItems;
        if (selected == null || selected.Count == 0)
            return;

        // Walk the source collection so copied lines keep display order.
        var sb = new StringBuilder(selected.Count * 90);
        for (int i = 0; i < source.Count; i++)
        {
            if (selected.Contains(source[i]))
                sb.AppendLine(source[i].Text);
        }

        await CopyToClipboard(sb.ToString(), button, selected.Count);
    }

    private async System.Threading.Tasks.Task CopyToClipboard(string text, Button? button, int lineCount)
    {
        try
        {
            var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
            if (clipboard == null)
                return;

            await clipboard.SetTextAsync(text);

            if (button != null)
            {
                var original = button.Content;
                button.Content = $"Copied {lineCount}";
                await System.Threading.Tasks.Task.Delay(1200);
                button.Content = original;
            }
        }
        catch (Exception ex)
        {
            ApplicationLogger.Log(LogCategory.UI, LogLevel.Error, "ProtocolMonitor",
                $"Clipboard copy failed: {ex.Message}");
        }
    }

    /// <inheritdoc/>
    protected override void OnClosed(EventArgs e)
    {
        ProtocolTracer.TraceAdded -= OnTraceAdded;

        // Only switch capture off if this window is what turned it on.
        if (!_tracerWasEnabled)
            ProtocolTracer.Enabled = false;

        if (_decodedList != null)
            _decodedList.KeyDown -= OnListKeyDown;

        if (_rawList != null)
            _rawList.KeyDown -= OnListKeyDown;

        base.OnClosed(e);
    }
}
