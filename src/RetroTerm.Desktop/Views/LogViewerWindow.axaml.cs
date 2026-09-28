using System;
using System.Collections.ObjectModel;
using System.Text;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using RetroTerm.Core.Logging;

namespace RetroTerm.Desktop.Views;

/// <summary>
/// Displays the structured application log with level / category / text filtering, and
/// supports copying either every visible line or just the selected lines.
/// </summary>
public partial class LogViewerWindow : Window
{
    private const int MaxDisplayedRows = 5000;

    private readonly ObservableCollection<LogLineRow> _rows = new();

    private ListBox? _list;
    private TextBox? _searchBox;
    private TextBlock? _statusText;
    private CheckBox? _autoScroll;
    private CheckBox? _showRawBytes;
    private Button? _fileLogButton;

    private string _searchFilter = string.Empty;

    public LogViewerWindow()
    {
        InitializeComponent();

        _list = this.FindControl<ListBox>("LogListBox");
        _searchBox = this.FindControl<TextBox>("SearchBox");
        _statusText = this.FindControl<TextBlock>("StatusText");
        _autoScroll = this.FindControl<CheckBox>("AutoScrollCheck");
        _showRawBytes = this.FindControl<CheckBox>("ShowRawBytesCheck");
        _fileLogButton = this.FindControl<Button>("FileLogButton");

        if (_list != null)
        {
            _list.ItemsSource = _rows;
            // Ctrl+C copies the selection, which is what every other log viewer does.
            _list.KeyDown += OnListKeyDown;
        }

        InitializeLevelCombo();
        SyncCategoryChecksFromLogger();
        UpdateFileLogButton();

        ApplicationLogger.LogAdded += OnLogAdded;

        RefreshLogs();
    }

    private void InitializeLevelCombo()
    {
        var combo = this.FindControl<ComboBox>("LevelCombo");
        if (combo == null)
            return;

        // Order matches the LogLevel enum so the index maps directly onto the value.
        var items = new ObservableCollection<string>
        {
            "Trace", "Debug", "Info", "Warn", "Error"
        };

        combo.ItemsSource = items;
        combo.SelectedIndex = (int)ApplicationLogger.MinimumLevel;
    }

    private void OnLevelChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is not ComboBox combo || combo.SelectedIndex < 0)
            return;

        ApplicationLogger.MinimumLevel = (LogLevel)combo.SelectedIndex;
        UpdateStatus();
    }

    private void OnCategoryChanged(object? sender, RoutedEventArgs e)
    {
        LogCategory enabled = LogCategory.None;

        if (IsChecked("CatNetworkCheck")) enabled |= LogCategory.Network;
        if (IsChecked("CatTelnetCheck")) enabled |= LogCategory.Telnet;
        if (IsChecked("CatSessionCheck")) enabled |= LogCategory.Session;
        if (IsChecked("CatParserCheck")) enabled |= LogCategory.Parser;
        if (IsChecked("CatEmulatorCheck")) enabled |= LogCategory.Emulator;
        if (IsChecked("CatKeyboardCheck")) enabled |= LogCategory.Keyboard;
        if (IsChecked("CatUiCheck")) enabled |= LogCategory.UI;
        if (IsChecked("CatGeneralCheck")) enabled |= LogCategory.General;

        ApplicationLogger.EnabledCategories = enabled;

        // Categories gate recording, not just display, so re-render what is still buffered.
        RefreshLogs();
    }

    private void SyncCategoryChecksFromLogger()
    {
        var enabled = ApplicationLogger.EnabledCategories;

        SetChecked("CatNetworkCheck", (enabled & LogCategory.Network) != 0);
        SetChecked("CatTelnetCheck", (enabled & LogCategory.Telnet) != 0);
        SetChecked("CatSessionCheck", (enabled & LogCategory.Session) != 0);
        SetChecked("CatParserCheck", (enabled & LogCategory.Parser) != 0);
        SetChecked("CatEmulatorCheck", (enabled & LogCategory.Emulator) != 0);
        SetChecked("CatKeyboardCheck", (enabled & LogCategory.Keyboard) != 0);
        SetChecked("CatUiCheck", (enabled & LogCategory.UI) != 0);
        SetChecked("CatGeneralCheck", (enabled & LogCategory.General) != 0);
    }

    private bool IsChecked(string name)
    {
        var box = this.FindControl<CheckBox>(name);
        return box?.IsChecked == true;
    }

    private void SetChecked(string name, bool value)
    {
        var box = this.FindControl<CheckBox>(name);
        if (box != null)
            box.IsChecked = value;
    }

    private void OnSearchChanged(object? sender, TextChangedEventArgs e)
    {
        _searchFilter = _searchBox?.Text ?? string.Empty;
        RefreshLogs();
    }

    private void OnFilterChanged(object? sender, RoutedEventArgs e)
    {
        RefreshLogs();
    }

    private void OnLogAdded(LogEntry entry)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            if (!PassesSearch(entry))
                return;

            AppendRow(entry);
            TrimRows();
            ScrollToEndIfFollowing();
            UpdateStatus();
        });
    }

    private bool PassesSearch(LogEntry entry)
    {
        if (_searchFilter.Length == 0)
            return true;

        // Search the rendered line so the user can match on level tags and mnemonics too.
        return entry.Format(_showRawBytes?.IsChecked == true)
            .IndexOf(_searchFilter, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private void AppendRow(LogEntry entry)
    {
        _rows.Add(new LogLineRow
        {
            Text = entry.Format(_showRawBytes?.IsChecked == true),
            Brush = BrushForLevel(entry.Level)
        });
    }

    private void TrimRows()
    {
        while (_rows.Count > MaxDisplayedRows)
        {
            _rows.RemoveAt(0);
        }
    }

    private static IBrush BrushForLevel(LogLevel level)
    {
        switch (level)
        {
            case LogLevel.Trace: return Brushes.Gray;
            case LogLevel.Debug: return Brushes.DarkGray;
            case LogLevel.Info: return Brushes.Gainsboro;
            case LogLevel.Warn: return Brushes.Gold;
            case LogLevel.Error: return Brushes.OrangeRed;
            default: return Brushes.Gainsboro;
        }
    }

    private void RefreshLogs()
    {
        _rows.Clear();

        var entries = ApplicationLogger.GetLogEntries();

        // Chronological order (oldest first) so causality reads top to bottom. The old viewer
        // inserted newest-first, which made a request/response pair read backwards.
        for (int i = 0; i < entries.Length; i++)
        {
            if (PassesSearch(entries[i]))
                AppendRow(entries[i]);
        }

        TrimRows();
        ScrollToEndIfFollowing();
        UpdateStatus();
    }

    private void ScrollToEndIfFollowing()
    {
        if (_autoScroll?.IsChecked != true || _list == null || _rows.Count == 0)
            return;

        _list.ScrollIntoView(_rows[_rows.Count - 1]);
    }

    private void UpdateStatus()
    {
        if (_statusText == null)
            return;

        var sb = new StringBuilder(96);
        sb.Append(_rows.Count).Append(" lines shown");

        int selected = _list?.SelectedItems?.Count ?? 0;
        if (selected > 0)
            sb.Append("  |  ").Append(selected).Append(" selected");

        sb.Append("  |  min level ").Append(ApplicationLogger.MinimumLevel);

        if (ApplicationLogger.IsFileLoggingEnabled)
            sb.Append("  |  writing to ").Append(ApplicationLogger.FileLogPath);

        _statusText.Text = sb.ToString();
    }

    private void OnClearClick(object? sender, RoutedEventArgs e)
    {
        ApplicationLogger.Clear();
        _rows.Clear();
        UpdateStatus();
    }

    private void OnRefreshClick(object? sender, RoutedEventArgs e)
    {
        RefreshLogs();
    }

    private void OnFileLogClick(object? sender, RoutedEventArgs e)
    {
        if (ApplicationLogger.IsFileLoggingEnabled)
        {
            ApplicationLogger.StopFileLog();
        }
        else
        {
            try
            {
                ApplicationLogger.StartFileLog();
            }
            catch (Exception ex)
            {
                ApplicationLogger.Log(LogCategory.UI, LogLevel.Error, "LogViewer",
                    $"Could not start file log: {ex.Message}");
            }
        }

        UpdateFileLogButton();
        UpdateStatus();
    }

    private void UpdateFileLogButton()
    {
        if (_fileLogButton == null)
            return;

        _fileLogButton.Content = ApplicationLogger.IsFileLoggingEnabled
            ? "Stop File Log"
            : "Start File Log";
    }

    private void OnListKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.C && (e.KeyModifiers & KeyModifiers.Control) != 0)
        {
            CopySelected();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.A && (e.KeyModifiers & KeyModifiers.Control) != 0)
        {
            _list?.SelectAll();
            UpdateStatus();
            e.Handled = true;
        }
    }

    private async void OnCopyAllClick(object? sender, RoutedEventArgs e)
    {
        var sb = new StringBuilder(_rows.Count * 80);
        for (int i = 0; i < _rows.Count; i++)
        {
            sb.AppendLine(_rows[i].Text);
        }

        await CopyToClipboard(sb.ToString(), sender as Button, _rows.Count);
    }

    private async void OnCopySelectedClick(object? sender, RoutedEventArgs e)
    {
        await CopySelectedCore(sender as Button);
    }

    private async void CopySelected()
    {
        await CopySelectedCore(null);
    }

    private async System.Threading.Tasks.Task CopySelectedCore(Button? button)
    {
        var selected = _list?.SelectedItems;
        if (selected == null || selected.Count == 0)
            return;

        var sb = new StringBuilder(selected.Count * 80);

        // SelectedItems is not index-ordered in every case, so walk the source collection and
        // pick out the selected rows. That keeps the copied text in display order.
        for (int i = 0; i < _rows.Count; i++)
        {
            if (selected.Contains(_rows[i]))
                sb.AppendLine(_rows[i].Text);
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
            ApplicationLogger.Log(LogCategory.UI, LogLevel.Error, "LogViewer",
                $"Clipboard copy failed: {ex.Message}");
        }
    }

    /// <inheritdoc/>
    protected override void OnClosed(EventArgs e)
    {
        ApplicationLogger.LogAdded -= OnLogAdded;

        if (_list != null)
            _list.KeyDown -= OnListKeyDown;

        base.OnClosed(e);
    }
}
