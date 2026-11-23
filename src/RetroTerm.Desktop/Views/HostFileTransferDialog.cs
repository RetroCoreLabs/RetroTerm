//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs/RetroTerm
// RetroTerm — Connecting today's machines to yesterday's systems
//

using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Platform.Storage;
using RetroTerm.Core.Commands;

namespace RetroTerm.Desktop.Views;

/// <summary>
/// The IND$FILE dialog of a 3270 tab: which PC file, which host data set, text or binary, and
/// any further IND$FILE options, for one upload (HOSTSEND) or one download (HOSTRECEIVE).
/// </summary>
/// <remarks>
/// The dialog only collects the fields. What happens with them is the registered HOSTSEND or
/// HOSTRECEIVE command, run by the caller through the command registry, so the menu, the .rts
/// script and MCP are one behaviour (the two-surface trap in CLAUDE.md). That is also why the
/// dialog checks only that the fields are filled in: whether the file exists, the mode is valid
/// and the screen is ready is decided by the command, in one place.
/// </remarks>
public sealed class HostFileTransferDialog : Window
{
    private readonly bool _upload;
    private readonly TextBox _path;
    private readonly TextBox _dataSet;
    private readonly ComboBox _mode;
    private readonly TextBox _options;
    private readonly TextBlock _problem;
    private CommandArgs? _result;

    /// <summary>
    /// Builds the dialog for one direction.
    /// </summary>
    /// <param name="upload">
    /// True to send a PC file to the host (IND$FILE PUT), false to receive a data set (GET).
    /// </param>
    public HostFileTransferDialog(bool upload)
    {
        _upload = upload;
        Title = upload ? "Send File to Host (IND$FILE PUT)" : "Receive File from Host (IND$FILE GET)";
        Width = 560;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        this[!BackgroundProperty] = new DynamicResourceExtension("WindowBackgroundBrush");

        var panel = new StackPanel { Spacing = 8, Margin = new Thickness(20) };

        panel.Children.Add(Label(upload ? "PC file to send" : "PC file to write (replaced only when the transfer is complete)"));
        _path = new TextBox { Name = "PathBox" };
        var browse = new Button { Content = "Browse...", Name = "BrowseButton", Classes = { "dialog-secondary" } };
        browse.Click += async (_, _) => await BrowseAsync().ConfigureAwait(true);
        var pathRow = new DockPanel();
        DockPanel.SetDock(browse, Dock.Right);
        browse.Margin = new Thickness(8, 0, 0, 0);
        pathRow.Children.Add(browse);
        pathRow.Children.Add(_path);
        panel.Children.Add(pathRow);

        panel.Children.Add(Label("Host data set (without quotes TSO puts your user id in front)"));
        _dataSet = new TextBox { Name = "DataSetBox" };
        panel.Children.Add(_dataSet);

        panel.Children.Add(Label("Mode"));
        _mode = new ComboBox { Name = "ModeBox", HorizontalAlignment = HorizontalAlignment.Stretch };
        _mode.Items.Add("ascii (text: translated, records end at CR LF)");
        _mode.Items.Add("binary (bytes unchanged)");
        _mode.SelectedIndex = 0;
        panel.Children.Add(_mode);

        panel.Children.Add(Label(upload ? "More IND$FILE options, for example RECFM(F) LRECL(80)" : "More IND$FILE options, for example APPEND"));
        _options = new TextBox { Name = "OptionsBox" };
        panel.Children.Add(_options);

        _problem = new TextBlock { Name = "ProblemText", IsVisible = false, TextWrapping = Avalonia.Media.TextWrapping.Wrap };
        _problem[!TextBlock.ForegroundProperty] = new DynamicResourceExtension("ErrorBrush");
        panel.Children.Add(_problem);

        var cancel = new Button { Content = "Cancel", Name = "CancelButton", Classes = { "dialog-secondary" } };
        cancel.Click += (_, _) => Close();
        var start = new Button { Content = upload ? "Send" : "Receive", Name = "StartButton", Classes = { "dialog-primary" } };
        start.Click += (_, _) => Accept();
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 8, 0, 0) };
        buttons.Children.Add(cancel);
        buttons.Children.Add(start);
        panel.Children.Add(buttons);

        Content = panel;
    }

    /// <summary>
    /// The registered command the fields are for: HOSTSEND or HOSTRECEIVE.
    /// </summary>
    public string CommandName => _upload ? "HOSTSEND" : "HOSTRECEIVE";

    /// <summary>
    /// Fills the fields, for a test or for a caller that knows them already.
    /// </summary>
    /// <param name="path">
    /// The PC file.
    /// </param>
    /// <param name="dataSet">
    /// The host data set.
    /// </param>
    /// <param name="binary">
    /// True for binary mode, false for ascii.
    /// </param>
    /// <param name="options">
    /// Further IND$FILE options.
    /// </param>
    public void Fill(string path, string dataSet, bool binary, string options)
    {
        _path.Text = path;
        _dataSet.Text = dataSet;
        _mode.SelectedIndex = binary ? 1 : 0;
        _options.Text = options;
    }

    /// <summary>
    /// The arguments for <see cref="CommandName"/> from the fields as they stand, or null with
    /// the reason shown in the dialog when a required field is empty.
    /// </summary>
    /// <returns>
    /// The command arguments, or null.
    /// </returns>
    public CommandArgs? ArgsFromFields()
    {
        string path = (_path.Text ?? string.Empty).Trim();
        string dataSet = (_dataSet.Text ?? string.Empty).Trim();
        string? missing = path.Length == 0 ? "Give the PC file." : dataSet.Length == 0 ? "Give the host data set." : null;
        _problem.Text = missing ?? string.Empty;
        _problem.IsVisible = missing != null;
        if (missing != null)
        {
            return null;
        }

        var args = new CommandArgs();
        args.Set("path", path);
        args.Set("dataset", dataSet);
        args.Set("mode", _mode.SelectedIndex == 1 ? "binary" : "ascii");
        args.Set("options", (_options.Text ?? string.Empty).Trim());
        return args;
    }

    /// <summary>
    /// Shows the dialog and waits for it.
    /// </summary>
    /// <param name="owner">
    /// The main window.
    /// </param>
    /// <returns>
    /// The arguments for <see cref="CommandName"/>, or null when the dialog was cancelled.
    /// </returns>
    public async Task<CommandArgs?> AskAsync(Window owner)
    {
        await ShowDialog(owner).ConfigureAwait(true);
        return _result;
    }

    private static TextBlock Label(string text)
    {
        var block = new TextBlock { Text = text, Margin = new Thickness(0, 4, 0, 0) };
        block[!TextBlock.ForegroundProperty] = new DynamicResourceExtension("PrimaryTextBrush");
        return block;
    }

    private void Accept()
    {
        _result = ArgsFromFields();
        if (_result != null)
        {
            Close();
        }
    }

    private async Task BrowseAsync()
    {
        if (_upload)
        {
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "PC file to send", AllowMultiple = false }).ConfigureAwait(true);
            if (files != null && files.Count > 0)
            {
                _path.Text = files[0].Path.LocalPath;
            }

            return;
        }

        var target = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions { Title = "PC file to write" }).ConfigureAwait(true);
        if (target != null)
        {
            _path.Text = target.Path.LocalPath;
        }
    }
}
