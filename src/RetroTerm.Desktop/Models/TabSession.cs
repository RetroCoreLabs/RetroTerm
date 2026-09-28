using System;
using RetroTerm.Core.Logging;
using RetroTerm.Core.Protocols;
using RetroTerm.Core.Session;
using RetroTerm.Desktop.Controls;

namespace RetroTerm.Desktop.Models;

/// <summary>
/// Groups all per-tab state into one container.
/// No business logic — just ownership of session, control, and logger.
/// </summary>
public sealed class TabSession : IDisposable
{
    public Guid Id { get; }
    public TerminalSession Session { get; }
    public TerminalControl Control { get; }
    public string? Host { get; set; }
    public string? EmulatorType { get; set; }
    public FileSessionDataLogger? Logger { get; set; }

    /// <summary>
    /// Stores the last connection parameters for reconnect support.
    /// </summary>
    /// <remarks>
    /// Setting this also applies the parameters that describe the DISPLAY rather than the
    /// connection - today just whether the window drives the terminal size.
    /// It happens here on purpose. Five different paths assign this - quick connect, a saved
    /// connection, a reconnect, a script and MCP - and applying the setting at each of them would
    /// mean five chances to forget one. That is the two-surface trap the repository already has a
    /// rule about, and the fix is the same: put the behaviour where it cannot be skipped.
    /// </remarks>
    public ConnectionFactory.ConnectionParameters? LastConnectionParameters
    {
        get => _lastConnectionParameters;
        set
        {
            _lastConnectionParameters = value;

            // Null means "whatever this terminal should do", which is what the control already
            // defaults to, so a connection that says nothing changes nothing.
            Control.FollowWindowSize = value?.FollowWindowSize;

            // The Keyboard tab's two key settings, applied here for the same reason and with the
            // same consequence if they were not: a connection opened by one of the five paths
            // would send BS where the host wanted DEL, and only on that path.
            // False when there are no parameters, which is what every key sent before the setting
            // existed.
            // Null passes through: the canvas then asks the emulator's profile, so a TDV sends DEL
            // and a VT sends BS without the connection having to say so.
            Control.BackspaceSendsDel = value?.BackspaceSendsDel;
            Control.DeleteSendsDel = value?.DeleteSendsDel ?? false;
        }
    }

    private ConnectionFactory.ConnectionParameters? _lastConnectionParameters;

    /// <summary>
    /// The currently applied color preset name (session-only, not saved to config).
    /// </summary>
    public string? ColorPreset { get; set; }

    /// <summary>
    /// When true, suppress "Connection closed" dialogs. Used during SSH host key
    /// verification where a rejected key causes a disconnect that should not show
    /// a spurious error dialog.
    /// </summary>
    public bool SuppressDisconnectDialog { get; set; }

    public event Action? TitleChanged;

    public TabSession(TerminalSession session, TerminalControl control)
    {
        Id = Guid.NewGuid();
        Session = session ?? throw new ArgumentNullException(nameof(session));
        Control = control ?? throw new ArgumentNullException(nameof(control));
    }

    public string Title
    {
        get
        {
            if (!string.IsNullOrEmpty(Host) && !string.IsNullOrEmpty(EmulatorType))
                return $"{Host} ({EmulatorType})";
            if (!string.IsNullOrEmpty(EmulatorType) && IsConnected)
                return EmulatorType;
            return "RetroTerm";
        }
    }

    public bool IsConnected => Session.IsConnected;

    /// <summary>
    /// True when scripts/console commands may target this tab. The WELCOME tab — a
    /// tab that has never been connected to anything — is NOT a script target
    /// (Ronny 2026-08-05): it only shows the welcome message, and the "Run on"
    /// picker and active-tab resolution must skip it. A tab whose connection
    /// DROPPED stays targetable (reconnect scripts).
    /// </summary>
    public bool IsScriptTargetable => IsConnected || LastConnectionParameters != null;

    public void NotifyTitleChanged()
    {
        TitleChanged?.Invoke();
    }

    public void Dispose()
    {
        if (Logger != null)
        {
            Session.DataLogger = null;
            Logger.Dispose();
            Logger = null;
        }

        Session.Dispose();
    }
}
