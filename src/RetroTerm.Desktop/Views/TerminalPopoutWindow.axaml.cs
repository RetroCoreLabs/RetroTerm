using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;
using RetroTerm.Core.Protocols;
using RetroTerm.Desktop.Models;

namespace RetroTerm.Desktop.Views;

public partial class TerminalPopoutWindow : Window
{
    private TabSession _tab = null!;
    private TextBlock? _statusText;
    private TextBlock? _recIndicator;
    private TextBlock? _connectionInfo;

    public event Action<TerminalPopoutWindow, TabSession>? PopoutClosed;

    public TabSession TabSession => _tab;

    // Required by Avalonia XAML loader
    public TerminalPopoutWindow()
    {
        InitializeComponent();
    }

    public TerminalPopoutWindow(TabSession tab)
    {
        _tab = tab ?? throw new ArgumentNullException(nameof(tab));
        InitializeComponent();

        _statusText = this.FindControl<TextBlock>("PopoutStatusText");
        _recIndicator = this.FindControl<TextBlock>("PopoutRecIndicator");
        _connectionInfo = this.FindControl<TextBlock>("PopoutConnectionInfo");

        // Reparent the terminal control into this window
        var content = this.FindControl<ContentControl>("PopoutContent");
        if (content != null)
        {
            content.Content = _tab.Control;
        }

        // Set window title
        Title = $"RetroTerm - {_tab.Title}";

        // Wire session events for status bar
        _tab.Session.StatusChanged += OnPopoutStatusChanged;
        _tab.TitleChanged += OnPopoutTitleChanged;

        // Initial status update
        UpdatePopoutStatus();
    }

    private void OnPopoutStatusChanged(ConnectionStatus status)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (status == ConnectionStatus.Disconnected)
            {
                UpdateStatusText("Disconnected");
            }
            else if (status == ConnectionStatus.Connected)
            {
                UpdateStatusText($"Connected to {_tab.Host}");
            }
            else
            {
                UpdateStatusText($"Connection: {status}");
            }
            UpdatePopoutStatus();
        });
    }

    private void OnPopoutTitleChanged()
    {
        Dispatcher.UIThread.Post(() =>
        {
            Title = $"RetroTerm - {_tab.Title}";
        });
    }

    private void UpdatePopoutStatus()
    {
        if (_connectionInfo != null)
        {
            if (!string.IsNullOrEmpty(_tab.Host) && !string.IsNullOrEmpty(_tab.EmulatorType))
            {
                _connectionInfo.Text = $"{_tab.Host} | {_tab.EmulatorType}";
                _connectionInfo.IsVisible = true;
            }
            else
            {
                _connectionInfo.IsVisible = false;
            }
        }

        if (_recIndicator != null)
        {
            _recIndicator.IsVisible = _tab.Logger != null && _tab.Logger.IsLogging;
        }
    }

    private void UpdateStatusText(string message)
    {
        if (_statusText != null)
        {
            _statusText.Text = message;
        }
    }

    private bool _teardownDone;

    /// <summary>
    /// Force-close this window during app shutdown. Skips confirmation, but still
    /// disconnects and disposes the session asynchronously — see <see cref="TeardownAsync"/>.
    /// </summary>
    public async Task ForceCloseAsync()
    {
        await TeardownAsync();
        Close();
    }

    /// <remarks>
    /// <para><b>Why this is one Close() call, not two racing paths</b></para>
    /// The old version fired disconnect on a background <c>Task.Run</c> and, in the same
    /// breath, called <c>_tab.Dispose()</c> synchronously on the UI thread. <c>Dispose()</c>
    /// itself blocks with <c>DisconnectAsync().GetAwaiter().GetResult()</c> when the session
    /// is still connected, so the UI thread sat there waiting while a second, concurrent
    /// disconnect touched the same connection from the background thread — the exact
    /// UI-thread deadlock <c>MainWindow.CloseTab</c> already had a comment warning about.
    /// Reported live, 1 September 2026: closing a popped-out window hung the whole app.
    /// <para>
    /// The fix mirrors <c>CloseTab</c>: cancel the close, await the disconnect off the UI
    /// thread's blocking path, dispose, THEN let the close proceed. <see cref="_teardownDone"/>
    /// makes the second, self-issued <c>Close()</c> pass straight through instead of
    /// re-entering the same teardown.
    /// </para>
    /// </remarks>
    protected override async void OnClosing(WindowClosingEventArgs e)
    {
        if (_teardownDone)
        {
            base.OnClosing(e);
            return;
        }

        e.Cancel = true;
        await TeardownAsync();
        Close();
    }

    private async Task TeardownAsync()
    {
        if (_teardownDone)
        {
            return;
        }

        _teardownDone = true;

        // NO TAB MEANS NOTHING TO TEAR DOWN, and this window can exist without one.
        //
        // The parameterless constructor above is there for the Avalonia XAML loader and leaves
        // _tab null. Everything below dereferences it, so closing such a window threw a
        // NullReferenceException - and because OnClosing is `async void`, the throw surfaced on a
        // task continuation rather than at the call, which is the hardest kind to trace back.
        //
        // Found 10 September 2026 by a test that constructs every window and closes it again.
        // Whether the real app can reach it depends on the loader ever building one, which is
        // exactly the sort of thing that changes without anyone noticing; a null check costs
        // nothing and removes the question.
        if (_tab == null)
        {
            return;
        }

        _tab.Session.StatusChanged -= OnPopoutStatusChanged;
        _tab.TitleChanged -= OnPopoutTitleChanged;

        // Detach terminal control before closing
        var content = this.FindControl<ContentControl>("PopoutContent");
        if (content != null)
        {
            content.Content = null;
        }

        if (_tab.IsConnected)
        {
            try
            {
                await _tab.Session.DisconnectAsync();
            }
            catch
            {
                // Ignore errors during close
            }
        }

        _tab.Dispose();

        // Notify MainWindow
        PopoutClosed?.Invoke(this, _tab);
    }
}
