using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using RetroTerm.Core.Session;
using RetroTerm.Core.Transfer;
using RetroTerm.Desktop.Themes;

namespace RetroTerm.Desktop.Views;

public partial class FileTransferProgressWindow : Window
{
    private readonly TerminalSession _session;
    private bool _transferFinished;

    public FileTransferProgressWindow(TerminalSession session, TransferDirection direction)
    {
        _session = session;
        InitializeComponent();

        var dirLabel = this.FindControl<TextBlock>("DirectionLabel");
        if (dirLabel != null)
            dirLabel.Text = direction == TransferDirection.Send ? "Sending File(s)" : "Receiving File(s)";

        _session.TransferProgressChanged += OnProgressChanged;
        _session.TransferStateChanged += OnTransferStateChanged;
    }

    private void OnProgressChanged(TransferProgress progress)
    {
        Dispatcher.UIThread.Post(() => UpdateUI(progress));
    }

    private void OnTransferStateChanged(bool active)
    {
        if (!active)
        {
            Dispatcher.UIThread.Post(() =>
            {
                _transferFinished = true;
                var cancelBtn = this.FindControl<Button>("CancelButton");
                var closeBtn = this.FindControl<Button>("CloseButton");
                if (cancelBtn != null) cancelBtn.IsEnabled = false;
                if (closeBtn != null) closeBtn.IsEnabled = true;

                // Auto-close if preference is set
                if (ThemeManager.Instance.AutoCloseTransferWindow)
                    Close();
            });
        }
    }

    private void UpdateUI(TransferProgress progress)
    {
        var stateLabel = this.FindControl<TextBlock>("StateLabel");
        var fileNameLabel = this.FindControl<TextBlock>("FileNameLabel");
        var bytesLabel = this.FindControl<TextBlock>("BytesLabel");
        var fileCountLabel = this.FindControl<TextBlock>("FileCountLabel");
        var fileCountHeader = this.FindControl<TextBlock>("FileCountLabelHeader");
        var progressBar = this.FindControl<ProgressBar>("TransferProgressBar");
        var errorBorder = this.FindControl<Border>("ErrorBorder");
        var errorLabel = this.FindControl<TextBlock>("ErrorLabel");

        if (stateLabel != null)
        {
            stateLabel.Text = progress.State.ToString();
            stateLabel.Foreground = progress.State switch
            {
                TransferState.Completed => Avalonia.Media.Brushes.LightGreen,
                TransferState.Failed => Avalonia.Media.Brushes.Salmon,
                TransferState.Cancelled => Avalonia.Media.Brushes.Orange,
                _ => Avalonia.Media.Brushes.MediumTurquoise
            };
        }

        if (fileNameLabel != null && progress.FileName != null)
            fileNameLabel.Text = progress.FileName;

        if (bytesLabel != null)
        {
            if (progress.TotalBytes > 0)
                bytesLabel.Text = $"{FormatBytes(progress.BytesTransferred)} / {FormatBytes(progress.TotalBytes)}";
            else if (progress.BytesTransferred > 0)
                bytesLabel.Text = FormatBytes(progress.BytesTransferred);
            else
                bytesLabel.Text = "-";
        }

        if (fileCountLabel != null && fileCountHeader != null)
        {
            bool showCount = progress.TotalFiles > 1 || progress.FileNumber > 1;
            fileCountHeader.IsVisible = showCount;
            fileCountLabel.IsVisible = showCount;
            if (showCount)
            {
                fileCountLabel.Text = progress.TotalFiles > 0
                    ? $"File {progress.FileNumber} of {progress.TotalFiles}"
                    : $"File {progress.FileNumber}";
            }
        }

        if (progressBar != null)
        {
            if (progress.TotalBytes > 0)
            {
                progressBar.IsIndeterminate = false;
                progressBar.Value = (double)progress.BytesTransferred / progress.TotalBytes * 100.0;
            }
            else if (progress.State == TransferState.Transferring)
            {
                progressBar.IsIndeterminate = true;
            }
            else if (progress.State == TransferState.Completed)
            {
                progressBar.IsIndeterminate = false;
                progressBar.Value = 100;
            }
        }

        if (errorBorder != null && errorLabel != null)
        {
            if (!string.IsNullOrEmpty(progress.ErrorMessage))
            {
                errorBorder.IsVisible = true;
                errorLabel.Text = progress.ErrorMessage;
            }
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

    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        _session.CancelFileTransfer();
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    protected override void OnClosed(EventArgs e)
    {
        _session.TransferProgressChanged -= OnProgressChanged;
        _session.TransferStateChanged -= OnTransferStateChanged;

        // If transfer is still active when window is closed, cancel it
        if (!_transferFinished && _session.IsTransferActive)
        {
            _session.CancelFileTransfer();
        }

        base.OnClosed(e);
    }
}
