//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs/RetroTerm
// RetroTerm — Connecting today's machines to yesterday's systems
//

using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using RetroTerm.Core.Commands.Builtin;
using RetroTerm.Core.Session;

namespace RetroTerm.Desktop.Views;

/// <summary>
/// IBM Host Files: one window on a 3270 tab to view, download or upload a host data set by its
/// name, the file moving through the terminal with IND$FILE.
/// </summary>
/// <remarks>
/// <para>
/// The window only collects the fields, picks the PC file and shows the outcome. Everything
/// else - the READY check, the host command, the transfer itself, the Clear afterwards - is
/// HostFileTransfer in RetroTerm.Core, the same code HOSTSEND and HOSTRECEIVE run (no business
/// logic in the UI layer, CLAUDE.md).
/// </para>
/// <para>
/// One transfer at a time: the three action buttons are disabled while one runs, and Cancel
/// stops the wait through the cancellation token RunAsync already takes. A transfer the host has
/// opened still ends with the host's message (IND-FILE-TRANSFER.md section 3.13).
/// </para>
/// </remarks>
public sealed class HostFilesWindow : Window
{
    private readonly TerminalSession _session;
    private readonly TextBox _dataSet;
    private readonly ComboBox _mode;
    private readonly TextBox _recfm;
    private readonly TextBox _lrecl;
    private readonly TextBox _blksize;
    private readonly Button _view;
    private readonly Button _download;
    private readonly Button _upload;
    private readonly Button _cancel;
    private readonly TextBlock _status;
    private CancellationTokenSource? _running;

    /// <summary>
    /// Builds the window for one 3270 tab.
    /// </summary>
    /// <param name="session">
    /// The tab's session; every transfer goes through its terminal.
    /// </param>
    /// <param name="tabName">
    /// The tab's name, for the title.
    /// </param>
    public HostFilesWindow(TerminalSession session, string tabName)
    {
        _session = session;
        Title = "IBM Host Files - " + tabName;
        Width = 560;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        this[!BackgroundProperty] = new DynamicResourceExtension("WindowBackgroundBrush");

        var panel = new StackPanel { Spacing = 8, Margin = new Thickness(20) };

        panel.Children.Add(Label("Host data set, for example HERC01.DEMO.CNTL(HELLO) or 'SYS2.JCLLIB(X)' (without quotes TSO puts your user id in front)"));
        _dataSet = new TextBox { Name = "DataSetBox" };
        panel.Children.Add(_dataSet);

        panel.Children.Add(Label("Mode"));
        _mode = new ComboBox { Name = "ModeBox", HorizontalAlignment = HorizontalAlignment.Stretch };
        _mode.Items.Add("Text (translated, records end at CR LF)");
        _mode.Items.Add("Binary (bytes unchanged)");
        _mode.SelectedIndex = 0;
        panel.Children.Add(_mode);

        panel.Children.Add(Label("For Upload of a new data set only; empty means the host decides. A member takes none of them."));
        var record = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,*,Auto,*") };
        _recfm = new TextBox { Name = "RecfmBox", PlaceholderText = "F, V or U" };
        _lrecl = new TextBox { Name = "LreclBox", PlaceholderText = "80" };
        _blksize = new TextBox { Name = "BlksizeBox", PlaceholderText = "3120" };
        AddField(record, 0, "RECFM", _recfm);
        AddField(record, 2, "LRECL", _lrecl);
        AddField(record, 4, "BLKSIZE", _blksize);
        panel.Children.Add(record);

        _status = new TextBlock { Name = "StatusText", Text = "TSO must be at the READY prompt.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) };
        _status[!TextBlock.ForegroundProperty] = new DynamicResourceExtension("PrimaryTextBrush");
        panel.Children.Add(_status);

        _view = new Button { Content = "View", Name = "ViewButton", Classes = { "dialog-primary" } };
        _view.Click += async (_, _) => await OnViewClickAsync().ConfigureAwait(true);
        _download = new Button { Content = "Download...", Name = "DownloadButton", Classes = { "dialog-secondary" } };
        _download.Click += async (_, _) => await OnDownloadClickAsync().ConfigureAwait(true);
        _upload = new Button { Content = "Upload...", Name = "UploadButton", Classes = { "dialog-secondary" } };
        _upload.Click += async (_, _) => await OnUploadClickAsync().ConfigureAwait(true);
        _cancel = new Button { Content = "Cancel", Name = "CancelButton", Classes = { "dialog-secondary" }, IsEnabled = false };
        _cancel.Click += (_, _) => _running?.Cancel();
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 8, 0, 0) };
        buttons.Children.Add(_cancel);
        buttons.Children.Add(_upload);
        buttons.Children.Add(_download);
        buttons.Children.Add(_view);
        panel.Children.Add(buttons);

        Content = panel;

        // Closing the window stops the wait; a transfer the host has started still ends with
        // the host's message and closes its file then (HostFileTransfer.RunAsync).
        Closed += (_, _) => _running?.Cancel();
    }

    /// <summary>
    /// Gets the status line's text: the outcome of the last action.
    /// </summary>
    public string StatusText => _status.Text ?? string.Empty;

    /// <summary>
    /// Gets whether a transfer is running.
    /// </summary>
    public bool IsBusy => _running != null;

    /// <summary>
    /// Gets the window that showed the last View, or null.
    /// </summary>
    public HostFileViewWindow? LastViewWindow { get; private set; }

    /// <summary>
    /// Fills the fields, for a test or for a caller that knows them already.
    /// </summary>
    /// <param name="dataSet">
    /// The host data set.
    /// </param>
    /// <param name="binary">
    /// True for Binary, false for Text.
    /// </param>
    /// <param name="recfm">
    /// RECFM, or empty.
    /// </param>
    /// <param name="lrecl">
    /// LRECL, or empty.
    /// </param>
    /// <param name="blksize">
    /// BLKSIZE, or empty.
    /// </param>
    public void Fill(string dataSet, bool binary, string recfm, string lrecl, string blksize)
    {
        _dataSet.Text = dataSet;
        _mode.SelectedIndex = binary ? 1 : 0;
        _recfm.Text = recfm;
        _lrecl.Text = lrecl;
        _blksize.Text = blksize;
    }

    /// <summary>
    /// View: receives the data set as text and shows it in a new read-only window.
    /// </summary>
    /// <returns>
    /// A task that ends with the action.
    /// </returns>
    public async Task ViewAsync()
    {
        HostFilesOutcome? outcome = await RunAsync(token => HostFileTransfer.ViewAsync(_session, _dataSet.Text, HostFileTransfer.DefaultTimeoutMs, token)).ConfigureAwait(true);
        if (outcome != null && outcome.Success && outcome.Text != null)
        {
            LastViewWindow = new HostFileViewWindow((_dataSet.Text ?? string.Empty).Trim(), outcome.Text);
            LastViewWindow.Show();
        }
    }

    /// <summary>
    /// Download: receives the data set into a PC file.
    /// </summary>
    /// <param name="path">
    /// The PC file, replaced only when the transfer is complete.
    /// </param>
    /// <returns>
    /// A task that ends with the action.
    /// </returns>
    public Task DownloadAsync(string path)
    {
        bool binary = _mode.SelectedIndex == 1;
        return RunAsync(token => HostFileTransfer.DownloadAsync(_session, _dataSet.Text, binary, path, HostFileTransfer.DefaultTimeoutMs, token));
    }

    /// <summary>
    /// Upload: sends a PC file to the data set.
    /// </summary>
    /// <param name="path">
    /// The PC file to send.
    /// </param>
    /// <returns>
    /// A task that ends with the action.
    /// </returns>
    public Task UploadAsync(string path)
    {
        bool binary = _mode.SelectedIndex == 1;
        string? recfm = _recfm.Text;
        string? lrecl = _lrecl.Text;
        string? blksize = _blksize.Text;
        return RunAsync(token => HostFileTransfer.UploadAsync(_session, _dataSet.Text, binary, path, recfm, lrecl, blksize, HostFileTransfer.DefaultTimeoutMs, token));
    }

    private static TextBlock Label(string text)
    {
        var block = new TextBlock { Text = text, Margin = new Thickness(0, 4, 0, 0), TextWrapping = TextWrapping.Wrap };
        block[!TextBlock.ForegroundProperty] = new DynamicResourceExtension("PrimaryTextBrush");
        return block;
    }

    private static void AddField(Grid grid, int column, string name, TextBox box)
    {
        TextBlock label = Label(name);
        label.Margin = new Thickness(column == 0 ? 0 : 12, 0, 6, 0);
        label.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(label, column);
        Grid.SetColumn(box, column + 1);
        grid.Children.Add(label);
        grid.Children.Add(box);
    }

    // The one way an action runs: one at a time, buttons off while it runs, the outcome on the
    // status line. Null when nothing ran because another action was running.
    private async Task<HostFilesOutcome?> RunAsync(Func<CancellationToken, Task<HostFilesOutcome>> action)
    {
        if (_running != null)
        {
            return null;
        }

        _running = new CancellationTokenSource();
        SetBusy(true);
        _status.Text = "Working ...";
        try
        {
            HostFilesOutcome outcome = await action(_running.Token).ConfigureAwait(true);
            _status.Text = outcome.Message;
            return outcome;
        }
        finally
        {
            _running.Dispose();
            _running = null;
            SetBusy(false);
        }
    }

    private void SetBusy(bool busy)
    {
        _view.IsEnabled = !busy;
        _download.IsEnabled = !busy;
        _upload.IsEnabled = !busy;
        _cancel.IsEnabled = busy;
    }

    // The READY check comes before the file picker too, so nobody picks a file only to be told
    // to go to READY. The action checks again: the screen can change while the picker is open.
    private async Task<bool> ReadyBeforePickingAsync()
    {
        if (await HostFileTransfer.IsAtReadyAsync(_session).ConfigureAwait(true))
        {
            return true;
        }

        _status.Text = HostFileTransfer.NotAtReadyMessage;
        return false;
    }

    private Task OnViewClickAsync()
    {
        return ViewAsync();
    }

    private async Task OnDownloadClickAsync()
    {
        if (_running != null || !await ReadyBeforePickingAsync().ConfigureAwait(true))
        {
            return;
        }

        IStorageFile? target = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions { Title = "PC file to write" }).ConfigureAwait(true);
        if (target != null)
        {
            await DownloadAsync(target.Path.LocalPath).ConfigureAwait(true);
        }
    }

    private async Task OnUploadClickAsync()
    {
        if (_running != null || !await ReadyBeforePickingAsync().ConfigureAwait(true))
        {
            return;
        }

        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "PC file to send", AllowMultiple = false }).ConfigureAwait(true);
        if (files != null && files.Count > 0)
        {
            await UploadAsync(files[0].Path.LocalPath).ConfigureAwait(true);
        }
    }
}

/// <summary>
/// The read-only text of a host data set, from View in the IBM Host Files window.
/// </summary>
public sealed class HostFileViewWindow : Window
{
    /// <summary>
    /// Builds the window.
    /// </summary>
    /// <param name="dataSet">
    /// The data set name, for the title.
    /// </param>
    /// <param name="text">
    /// The data set's text.
    /// </param>
    public HostFileViewWindow(string dataSet, string text)
    {
        Title = dataSet;
        Width = 760;
        Height = 520;
        this[!BackgroundProperty] = new DynamicResourceExtension("WindowBackgroundBrush");
        Content = new TextBox
        {
            Name = "ContentBox",
            Text = text,
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.NoWrap,
            FontFamily = new FontFamily("Cascadia Mono,Consolas,monospace"),
            Margin = new Thickness(8),
        };
    }

    /// <summary>
    /// Gets the text shown.
    /// </summary>
    public string Text => ((TextBox)Content!).Text ?? string.Empty;
}
