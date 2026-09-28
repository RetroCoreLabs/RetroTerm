using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Interactivity;
using Avalonia.Threading;
using RetroTerm.Core.Commands;
using RetroTerm.Core.Logging;
using RetroTerm.Core.Protocols;
using RetroTerm.Core.Scripting;
using RetroTerm.Core.Session;
using RetroTerm.Desktop.Models;
using RetroTerm.Mcp;

namespace RetroTerm.Desktop;

/// <summary>
/// MCP hosting inside the desktop app: an LLM drives the SAME tabs a human watches.
/// The server listens on localhost only; each terminal_open becomes a visible tab,
/// terminal_close closes it, and everything in between runs through the session's
/// pump — the human sees the LLM type.
///
/// Port: Preferences, MCP tab. Default 5715. Nothing else sets it - no environment variable.
/// Claude Code: claude mcp add --transport http retroterm http://127.0.0.1:5715/mcp
/// </summary>
public partial class MainWindow
{
    private const int DefaultMcpPort = 5715;

    private McpServerHost? _mcpServer;

    // One registry + library shared by the MCP server and the script editor, so both
    // always see the same command set and the same scripts.
    private CommandRegistry? _commandRegistry;
    private ScriptLibrary? _scriptLibrary;

    private CommandRegistry GetOrCreateCommandRegistry()
    {
        if (_commandRegistry == null)
        {
            _commandRegistry = new CommandRegistry();
            Core.Commands.Builtin.BuiltinCommands.RegisterAll(_commandRegistry);

            // Connection commands share the same stored-connection list as the
            // Quick Connect / Manage Connections windows.
            Core.Commands.Builtin.ConnectionCommands.RegisterAll(_commandRegistry, _configManager);

            // Kermit transfers use the user's Kermit preferences (Preferences → Kermit).
            RetroTerm.Core.Protocols.Kermit.KermitCommands.RegisterAll(_commandRegistry,
                BuildKermitOptionsFromPreferences, GetFileCollisionFromPreferences);

            // GATEWAY reports the live ND-100 gateway listener state.
            RetroTerm.Core.Protocols.WebSocket.Gateway.GatewayCommand.RegisterAll(
                _commandRegistry, () => _gatewayListener);

            // SCREENSHOT renders the screen WITH its graphics. Core owns the command and cannot
            // draw, so the renderer is handed in from here - the same shape as GATEWAY above.
            Core.Commands.Builtin.ScreenshotCommand.RegisterAll(
                _commandRegistry, CaptureSessionScreenshot);

            // LOCALKEY, ZOOM and PASTE act on the WINDOW rather than the connection. Everything
            // else here ends at TerminalSession.SendInputAsync, which cannot reach what the
            // terminal decides for itself - the ReGIS graphics input cursor, the magnification and
            // the paste path. Same shape again: Core declares them, the desktop supplies the doing.
            Core.Commands.Builtin.LocalInputCommands.RegisterAll(
                _commandRegistry, DeliverLocalKey, ChangeSessionZoom, PasteIntoSession);
        }
        return _commandRegistry;
    }

    /// <summary>
    /// Finds the tab that owns a session.
    /// </summary>
    /// <param name="session">
    /// The session to look for.
    /// </param>
    /// <returns>
    /// The tab, or null when this window has none for it.
    /// </returns>
    /// <remarks>
    /// An indexed loop over a handful of tabs. The list is the window's own state, so every caller
    /// below runs this on the UI thread.
    /// </remarks>
    private TabSession? FindTabForSession(TerminalSession session)
    {
        for (int i = 0; i < _tabs.Count; i++)
        {
            if (ReferenceEquals(_tabs[i].Session, session))
            {
                return _tabs[i];
            }
        }

        return null;
    }

    /// <summary>
    /// Presses one key at a session's terminal, rather than sending it to the host.
    /// </summary>
    /// <param name="session">
    /// The session whose terminal receives the key.
    /// </param>
    /// <param name="key">
    /// A key name, or a single character to type.
    /// </param>
    /// <param name="shift">
    /// Whether shift is held.
    /// </param>
    /// <returns>
    /// Null when the key was delivered, or a sentence saying why it was not.
    /// </returns>
    /// <remarks>
    /// <para><b>One character is typed, a longer name is a key</b></para>
    /// A printable key reaches the terminal as TEXT, because only the keyboard layout knows what a
    /// key produces - and ReGIS graphics input answers the host with the character itself, so it
    /// has to be the character and not a key code. Anything longer than one character is looked up
    /// as an Avalonia key name, which is what gives Left, Right, Up, Down, Enter, Escape and the
    /// function keys.
    /// <para><b>On the UI thread, always</b></para>
    /// Raising a routed event walks the visual tree, which is UI-thread state, and this is called
    /// from the MCP server's thread.
    /// </remarks>
    private string? DeliverLocalKey(TerminalSession session, string key, bool shift)
    {
        if (session == null) return "no session";
        if (string.IsNullOrEmpty(key)) return "no key";

        return Dispatcher.UIThread.Invoke(() =>
        {
            var tab = FindTabForSession(session);
            if (tab == null) return "that session has no tab in this window";

            var modifiers = shift
                ? global::Avalonia.Input.KeyModifiers.Shift
                : global::Avalonia.Input.KeyModifiers.None;

            // A single character is typed rather than pressed. Note this is the CHARACTER path even
            // when shift was asked for: the caller spells a capital by giving a capital, the same
            // way the keyboard layout would produce one.
            if (key.Length == 1)
            {
                tab.Control.DeliverLocalText(key);
                return null;
            }

            if (!Enum.TryParse<global::Avalonia.Input.Key>(key, ignoreCase: true, out var parsed))
            {
                return "unknown key name '" + key + "' - use an Avalonia key name (Left, Right, Up, "
                    + "Down, Enter, Escape, Tab, F1, Add, Subtract, D0) or a single character";
            }

            tab.Control.DeliverLocalKey(parsed, modifiers);
            return null;
        });
    }

    /// <summary>
    /// Reads, sets or steps the display zoom of a session's view.
    /// </summary>
    /// <param name="session">
    /// The session whose view to act on.
    /// </param>
    /// <param name="percent">
    /// The percentage to set, or null to leave it alone.
    /// </param>
    /// <param name="step">
    /// Places to move up or down the shared ladder; zero to leave it alone.
    /// </param>
    /// <param name="nowPercent">
    /// The zoom in force afterwards.
    /// </param>
    /// <returns>
    /// Null when the zoom was read or changed, or a sentence saying why it was not.
    /// </returns>
    /// <remarks>
    /// The range check is against the ends of <c>TerminalCanvas.ZoomSteps</c> rather than a pair of
    /// numbers written here, so the command and the dropdown can never disagree about what is
    /// allowed. Values between the steps are accepted - the ladder is what the SHORTCUTS walk, not
    /// a list of the only legal magnifications.
    /// </remarks>
    private string? ChangeSessionZoom(TerminalSession session, int? percent, int step, out int nowPercent)
    {
        nowPercent = 0;
        if (session == null) return "no session";

        var steps = Controls.TerminalCanvas.ZoomSteps;
        int lowest = steps[0];
        int highest = steps[steps.Length - 1];

        if (percent != null && (percent.Value < lowest || percent.Value > highest))
        {
            return "zoom must be between " + lowest + " and " + highest + " percent";
        }

        int result = 0;
        var problem = Dispatcher.UIThread.Invoke(() =>
        {
            var tab = FindTabForSession(session);
            if (tab == null) return "that session has no tab in this window";

            if (percent != null)
            {
                tab.Control.ZoomPercent = percent.Value;
            }
            else if (step != 0)
            {
                // Each place on the ladder is one call, so step=2 really is two moves and stops at
                // the end rather than running past it.
                int moves = step > 0 ? step : -step;
                for (int i = 0; i < moves; i++)
                {
                    tab.Control.StepZoom(step > 0 ? 1 : -1);
                }
            }

            result = tab.Control.ZoomPercent;
            return null;
        });

        nowPercent = result;
        return problem;
    }

    /// <summary>
    /// Pastes into a session the way the Paste menu item does.
    /// </summary>
    /// <param name="session">
    /// The session to paste into.
    /// </param>
    /// <param name="text">
    /// The text to paste, or null to use the system clipboard.
    /// </param>
    /// <returns>
    /// Null when the paste happened, or a sentence saying why it did not.
    /// </returns>
    /// <remarks>
    /// Naming the text is the ordinary case for anything driving the terminal: it is repeatable and
    /// it leaves the reader's own clipboard alone. The clipboard route is kept because that is what
    /// a person does, and the two must not be allowed to behave differently.
    /// </remarks>
    private string? PasteIntoSession(TerminalSession session, string? text)
    {
        if (session == null) return "no session";

        return Dispatcher.UIThread.Invoke(() =>
        {
            var tab = FindTabForSession(session);
            if (tab == null) return "that session has no tab in this window";

            if (text == null)
            {
                tab.Control.Paste();
            }
            else
            {
                tab.Control.PasteText(text);
            }

            return null;
        });
    }

    /// <summary>
    /// Draws one session's tab - text and every graphics plane - into a PNG file.
    /// </summary>
    /// <param name="session">
    /// The session whose tab to draw.
    /// </param>
    /// <param name="path">
    /// Full path of the file to write.
    /// </param>
    /// <returns>
    /// Null when the file was written, or a sentence saying why it was not.
    /// </returns>
    /// <remarks>
    /// <para><b>It renders the REAL control, not a copy of the drawing logic</b></para>
    /// <c>RenderTargetBitmap.Render</c> calls the control's own <c>Render</c>, so what lands in the
    /// file is what is on the glass - the same route the headless tests use. Drawing a second time
    /// from the buffer would produce a picture that agrees with itself and with nothing else, which
    /// is how three graphics defects survived a green suite.
    /// <para><b>On the UI thread, always</b></para>
    /// The control and its visual tree are UI-thread state, and this is called from the MCP server's
    /// thread. The whole render is marshalled as one unit.
    /// </remarks>
    private string? CaptureSessionScreenshot(TerminalSession session, string path)
    {
        if (session == null) return "no session";

        return Dispatcher.UIThread.Invoke(() =>
        {
            var tab = FindTabForSession(session);
            if (tab == null) return "that session has no tab in this window";

            var control = tab.Control;

            // Bounds are zero for a tab that has never been laid out - a background tab on a window
            // that has not been shown. Saying so beats writing a 0 by 0 PNG and calling it a pass.
            int width = (int)Math.Round(control.Bounds.Width);
            int height = (int)Math.Round(control.Bounds.Height);
            if (width <= 0 || height <= 0)
            {
                return "that tab has not been laid out yet, so it has no pixels to capture - "
                    + "select it in the window first";
            }

            try
            {
                using var target = new global::Avalonia.Media.Imaging.RenderTargetBitmap(
                    new global::Avalonia.PixelSize(width, height),
                    new global::Avalonia.Vector(96, 96));

                target.Render(control);
                target.Save(path);
                return null;
            }
            catch (Exception ex)
            {
                return "render failed: " + ex.Message;
            }
        });
    }

    private ScriptLibrary GetOrCreateScriptLibrary()
    {
        if (_scriptLibrary == null)
        {
            _scriptLibrary = ScriptLibrary.CreateDefault();
            // First run (empty library): drop in the commented sample scripts.
            try { ScriptSamples.SeedIfEmpty(_scriptLibrary); }
            catch { /* a read-only scripts folder must never break the app */ }
        }
        return _scriptLibrary;
    }

    // ─────────────────────────────────────────────────────────────
    // Connection event hooks (OnConnect / OnDisconnect scripts)
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Runs a stored connection's OnConnectScript. Called after ConnectToHost
    /// succeeds. Fire-and-forget: hook failures land in the terminal + status bar,
    /// never block the connect.
    /// </summary>
    internal void FireOnConnectHook(TabSession tab)
    {
        var scriptName = tab.LastConnectionParameters?.OnConnectScript;
        if (!string.IsNullOrEmpty(scriptName))
        {
            _ = RunHookScriptAsync(tab, scriptName!, "on-connect");
        }
    }

    /// <summary>
    /// Runs the OnDisconnectScript when the tab's connection DROPS. Wired once per
    /// tab (CreateTab) and reads the CURRENT parameters at fire time, so reconnects
    /// never stack handlers. Deliberate disconnects don't raise ConnectionLost, so
    /// they never fire this.
    /// </summary>
    internal void OnTabConnectionLost(TabSession tab, string reason)
    {
        var scriptName = tab.LastConnectionParameters?.OnDisconnectScript;
        if (!string.IsNullOrEmpty(scriptName))
        {
            _ = RunHookScriptAsync(tab, scriptName!, "on-disconnect");
        }
    }

    private async Task RunHookScriptAsync(TabSession tab, string scriptName, string hookName)
    {
        try
        {
            var library = GetOrCreateScriptLibrary();
            if (!library.Exists(scriptName))
            {
                ReportHook(tab, $"{hookName} script '{scriptName}' not found in the library");
                return;
            }

            var parser = new RetroTerm.Core.Scripting.ScriptParser(GetOrCreateCommandRegistry());
            var parsed = parser.Parse(library.Load(scriptName));
            if (!parsed.IsValid)
            {
                ReportHook(tab, $"{hookName} script '{scriptName}' has {parsed.Errors.Count} error(s): {parsed.Errors[0]}");
                return;
            }

            ReportHook(tab, $"{hookName}: running '{scriptName}' ({parsed.Steps.Count} steps)");
            var runner = new RetroTerm.Core.Scripting.ScriptRunner(GetOrCreateCommandRegistry());
            var result = await runner.RunAsync(parsed, tab.Session).ConfigureAwait(false);

            ReportHook(tab, result.Success
                ? $"{hookName}: '{scriptName}' OK ({(long)result.Elapsed.TotalMilliseconds} ms)"
                : $"{hookName}: '{scriptName}' FAILED — {result.Error}");
        }
        catch (Exception ex)
        {
            ReportHook(tab, $"{hookName} script '{scriptName}' threw: {ex.Message}");
        }
    }

    /// <summary>
    /// Hook progress goes onto the tab's screen and (when active) the status bar.
    /// </summary>
    private void ReportHook(TabSession tab, string message)
    {
        ApplicationLogger.Log(LogCategory.Session, LogLevel.Info, "MainWindow", message);
        tab.Session.WriteToTerminal($"\r\n  [{message}]\r\n");
        Dispatcher.UIThread.Post(() =>
        {
            if (tab == _activeTab)
            {
                UpdateStatus(message);
            }
        });
    }

    /// <summary>
    /// View → Script Editor.
    /// </summary>
    private void OnScriptEditorClick(object? sender, RoutedEventArgs e)
    {
        var editor = new Views.ScriptEditorWindow(GetOrCreateScriptLibrary(), GetOrCreateCommandRegistry(),
            GetScriptTargetSession, GetScriptTargetSessions);
        editor.Show(this);
    }

    /// <summary>
    /// View → Script Console: type DSL commands against the active tab; HELP prints here.
    /// </summary>
    private void OnScriptConsoleClick(object? sender, RoutedEventArgs e)
    {
        var console = new Views.ScriptConsoleWindow(GetScriptTargetSession, GetScriptTargetSessions,
            GetOrCreateCommandRegistry(), GetOrCreateScriptLibrary());
        console.Show(this);
    }

    /// <summary>
    /// The sessions scripts may target, in tab order — feeds the "Run on" picker in
    /// the script editor and console. The WELCOME tab (never connected) is excluded:
    /// scripts must not interact with the welcome message.
    /// </summary>
    private System.Collections.Generic.IReadOnlyList<RetroTerm.Core.Session.TerminalSession> GetScriptTargetSessions()
    {
        var tabs = _tabs;
        var sessions = new System.Collections.Generic.List<RetroTerm.Core.Session.TerminalSession>(tabs.Count);
        for (int i = 0; i < tabs.Count; i++)
        {
            if (tabs[i].IsScriptTargetable)
            {
                sessions.Add(tabs[i].Session);
            }
        }
        return sessions;
    }

    /// <summary>
    /// The session "Active tab" resolves to — null when the active tab is the
    /// welcome tab (or there is none), so scripts never touch the welcome message.
    /// </summary>
    private RetroTerm.Core.Session.TerminalSession? GetScriptTargetSession()
    {
        var tab = _activeTab;
        return tab != null && tab.IsScriptTargetable ? tab.Session : null;
    }

    /// <summary>
    /// View → MCP Log: live MCP traffic (tool calls + results).
    /// </summary>
    private void OnMcpLogClick(object? sender, RoutedEventArgs e)
    {
        new Views.McpLogWindow(_mcpServer?.Url).Show(this);
    }

    /// <summary>
    /// Starts the MCP server. Called once at startup; a failure (port in use, ...)
    /// is logged and shown in the status bar but never blocks the app.
    /// </summary>
    /// <remarks>
    /// <para><b>Preferences is the ONLY place the port comes from</b></para>
    /// The MCP tab of Preferences carries enable and port, and nothing else does. An environment
    /// variable <c>RETROTERM_MCP_PORT</c> used to override it and was removed on 27 August 2026 at
    /// Ronny's instruction: "dont use environment variables for anything inside the app. if you need
    /// configuration, we have a standard configuration window."
    /// The reason it is a bad idea here in particular: Preferences would show one port while the
    /// server listened on another, with nothing on screen saying why - and the person who set the
    /// variable is usually not the person reading the dialog.
    /// </remarks>
    private async Task InitializeMcpServerAsync()
    {
        try
        {
            if (!Themes.ThemeManager.Instance.McpEnabled)
            {
                ApplicationLogger.Log(LogCategory.Session, LogLevel.Info, "MainWindow",
                    "MCP server disabled in Preferences — not starting");
                return;
            }

            int port = Themes.ThemeManager.Instance.McpPort;
            if (port <= 0 || port > 65535)
            {
                port = DefaultMcpPort;
            }
            // The configuration manager is what lets terminal_open name= reach the SAME
            // stored connections the UI shows — serial ones included, with no telnet bounce.
            var provider = new RetroTermToolProvider(new TabSessionHost(this),
                GetOrCreateCommandRegistry(), GetOrCreateScriptLibrary(), _configManager);
            _mcpServer = await McpServerHost.StartAsync(provider, port);

            ApplicationLogger.Log(LogCategory.Session, LogLevel.Info, "MainWindow",
                $"MCP server listening at {_mcpServer.Url}");
        }
        catch (Exception ex)
        {
            ApplicationLogger.Log(LogCategory.Session, LogLevel.Error, "MainWindow",
                $"MCP server failed to start: {ex.Message}");
            Dispatcher.UIThread.Post(() => UpdateStatus($"MCP server failed to start: {ex.Message}"));
        }
    }

    private async Task ShutdownMcpServerAsync()
    {
        var server = _mcpServer;
        _mcpServer = null;
        if (server != null)
        {
            try { await server.DisposeAsync(); }
            catch { /* best effort on shutdown */ }
        }
    }

    // ─────────────────────────────────────────────────────────────
    // Tab-backed session host operations (called from MCP threads)
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// The title of a dialog currently open over this window, or null when there is none.
    /// </summary>
    /// <returns>
    /// The dialog's title, so a caller can say WHICH window is in the way.
    /// </returns>
    /// <remarks>
    /// Found by asking the window list who our children are, rather than by keeping a counter that
    /// every <c>ShowDialog</c> call site would have to remember to increment. A counter is one
    /// forgotten call site away from being wrong, and the forgotten one is always the dialog nobody
    /// thought about.
    ///
    /// UI thread only - the window list is UI-thread state.
    /// </remarks>
    internal string? OpenModalDialogTitle()
    {
        // OwnedWindows, not the application's whole window list. The first attempt asked
        // Application.Current.ApplicationLifetime for its Windows collection and returned null every
        // time under test, because the headless lifetime is not the classic desktop one - so the
        // guard would have been dead in the only place it could be proven. Asking the window for its
        // own children needs no lifetime at all and is a shorter question besides.
        var owned = OwnedWindows;

        for (int i = 0; i < owned.Count; i++)
        {
            var window = owned[i];
            if (window == null) continue;

            return string.IsNullOrEmpty(window.Title) ? window.GetType().Name : window.Title;
        }

        return null;
    }

    internal async Task<Guid> OpenMcpSessionAsync(ConnectionFactory.ConnectionParameters parameters,
        CancellationToken cancellationToken)
    {
        // SAME code path as a UI connect: the welcome tab (any disconnected active
        // tab) is reused instead of piling up next to a new tab, and the full
        // connect pipeline runs (emulator swap, language variant, colors, Kermit
        // auto-detect, on-connect hook). Marshalled to the UI thread as one unit —
        // tab bookkeeping is UI-thread state.
        // NOT WHILE A DIALOG IS OPEN. Manage Connections and friends are shown with ShowDialog(this),
        // which is a true modal - the main window is DISABLED for as long as one is up. Building a
        // tab underneath it puts a new TerminalCanvas into that disabled window, and a canvas takes
        // the focus the moment it is attached.
        //
        // This is not theoretical. On 25 August 2026 a connection was opened from the MCP side while
        // Ronny was working in Manage Connections; the UI broke and he had to kill the terminal,
        // losing what he had been editing.
        //
        // Refusing, rather than queueing, is the deliberate choice: the caller is told exactly what
        // is in the way, and the person at the keyboard is not interrupted by something they did not
        // ask for. CLAUDE.md already names connection creation as a past offender in the
        // two-surface trap, and this is the same lesson arriving from the other direction.
        string? blockingDialog = await Dispatcher.UIThread.InvokeAsync(() => OpenModalDialogTitle());
        if (blockingDialog != null)
        {
            throw new InvalidOperationException(
                "A dialog is open in RetroTerm (" + blockingDialog + "), so a new connection cannot " +
                "be opened right now - it would build a tab underneath a modal window and break the " +
                "interface. Close the dialog and try again.");
        }

        // A serial port is exclusive. Refusing here, by name, is the whole point: without it an
        // MCP open of COM11 would take the port from under a tab that is talking to a live
        // machine, and the person watching would get "Access to the path 'COM11' is denied" with
        // nothing on screen tying it to a remote call. Same rule the UI connect path uses —
        // see MainWindow.SerialPortAlreadyInUse for why it is shared rather than copied.
        string? portClash = await Dispatcher.UIThread.InvokeAsync(() => SerialPortAlreadyInUse(parameters));
        if (portClash != null)
        {
            throw new InvalidOperationException(
                portClash + " in RetroTerm. Disconnect that tab first, or use the session that "
                + "already has the port (terminal_list shows the open sessions).");
        }

        var connectedTab = await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            // callerCanSeeTheTab: FALSE. A remote caller cannot see which tab is active, so it may
            // only take over a tab that has never held a connection. See
            // CanReuseTabForNewConnection for the session this rule was written to protect.
            var (tab, created) = GetOrCreateTabForNewConnection(
                parameters.EmulatorType, parameters.Width, parameters.Height,
                callerCanSeeTheTab: false);
            try
            {
                // Core THROWS on failure (the MCP tool reports it) and may return a
                // DIFFERENT tab (emulator mismatch replaces the tab).
                return await ConnectToHostCore(tab, parameters);
            }
            catch
            {
                // Only take down a tab this call created — never the welcome tab.
                if (created)
                {
                    CloseTab(tab);
                }
                throw;
            }
        });

        return connectedTab.Id;
    }

    internal TerminalSession? GetMcpSession(Guid id)
    {
        // _tabs is only touched on the UI thread — marshal the lookup.
        return Dispatcher.UIThread.CheckAccess()
            ? FindSessionById(id)
            : Dispatcher.UIThread.Invoke(() => FindSessionById(id));
    }

    /// <remarks>
    /// A popped-out window's <c>TabSession</c> is not in <see cref="_tabs"/> — it moved to
    /// <see cref="_popoutWindows"/> when <see cref="PopOutTab"/> ran. Skipping it here made a
    /// popped-out session invisible to MCP: not listed, not reachable by sessionId, not
    /// closeable. Ronny's report, 1 September 2026.
    /// </remarks>
    private TerminalSession? FindSessionById(Guid id)
    {
        for (int i = 0; i < _tabs.Count; i++)
        {
            if (_tabs[i].Id == id)
            {
                return _tabs[i].Session;
            }
        }

        for (int i = 0; i < _popoutWindows.Count; i++)
        {
            if (_popoutWindows[i].TabSession.Id == id)
            {
                return _popoutWindows[i].TabSession.Session;
            }
        }

        return null;
    }

    internal IReadOnlyList<SessionInfo> ListMcpSessions()
    {
        return Dispatcher.UIThread.CheckAccess()
            ? ListSessionsOnUiThread()
            : Dispatcher.UIThread.Invoke(ListSessionsOnUiThread);
    }

    /// <remarks>
    /// Built from <see cref="EnumerateOpenSessions"/>, the one place that answers "what is open",
    /// so this list and the tab strip's own session dropdown cannot disagree about it. They did
    /// once: this method forgot popped-out windows entirely.
    /// </remarks>
    private IReadOnlyList<SessionInfo> ListSessionsOnUiThread()
    {
        var sessions = EnumerateOpenSessions();
        var result = new List<SessionInfo>(sessions.Count);

        for (int i = 0; i < sessions.Count; i++)
        {
            var tab = sessions[i].Tab;
            result.Add(new SessionInfo(tab.Id, tab.Title, tab.IsConnected,
                tab.EmulatorType ?? tab.Session.Emulator.GetType().Name));
        }

        return result;
    }

    internal async Task CloseMcpSessionAsync(Guid id)
    {
        await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            for (int i = 0; i < _tabs.Count; i++)
            {
                if (_tabs[i].Id == id)
                {
                    CloseTab(_tabs[i]);
                    return;
                }
            }

            for (int i = 0; i < _popoutWindows.Count; i++)
            {
                if (_popoutWindows[i].TabSession.Id == id)
                {
                    await _popoutWindows[i].ForceCloseAsync();
                    return;
                }
            }
        });
    }

    /// <summary>
    /// <see cref="ITerminalSessionHost"/> over the main window's tabs — the adapter
    /// the MCP tool provider talks to. Thin by design: every call forwards to the
    /// internal MainWindow methods above, which own the UI-thread marshalling.
    /// </summary>
    private sealed class TabSessionHost : ITerminalSessionHost
    {
        private readonly MainWindow _window;

        public TabSessionHost(MainWindow window)
        {
            _window = window;
        }

        public Task<Guid> OpenSessionAsync(ConnectionFactory.ConnectionParameters parameters,
            CancellationToken cancellationToken = default)
            => _window.OpenMcpSessionAsync(parameters, cancellationToken);

        public TerminalSession? GetSession(Guid id) => _window.GetMcpSession(id);

        public IReadOnlyList<SessionInfo> ListSessions() => _window.ListMcpSessions();

        public Task CloseSessionAsync(Guid id) => _window.CloseMcpSessionAsync(id);
    }
}
