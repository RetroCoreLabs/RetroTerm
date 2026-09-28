using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using RetroTerm.Core.Protocols;

namespace RetroTerm.Core.Session;

/// <summary>
/// Summary of one hosted session, for listing (MCP terminal_list).
/// </summary>
public sealed class SessionInfo
{
    public Guid Id { get; }
    public string Title { get; }
    public bool IsConnected { get; }
    public string EmulatorType { get; }

    public SessionInfo(Guid id, string title, bool isConnected, string emulatorType)
    {
        Id = id;
        Title = title;
        IsConnected = isConnected;
        EmulatorType = emulatorType;
    }
}

/// <summary>
/// The seam between the MCP layer and whoever owns the sessions.
///
/// Two implementations are planned:
///  - the DESKTOP host: sessions are tabs in the running RetroTerm window, so a human
///    watches the LLM drive the same screen (creation marshalled to the UI thread);
///  - a HEADLESS host (InProcessSessionHost) for tests and a future console MCP server.
///
/// Sessions opened through this interface are PERSISTENT: they survive across many
/// MCP calls and LLM turns, and are closed only by an explicit CloseSessionAsync —
/// reconnecting to a RetroCore terminal port mid-program wedges the line
/// (HANDOVER-MCP-TERMINAL-CONTROL.md rule 1).
/// </summary>
public interface ITerminalSessionHost
{
    /// <summary>
    /// Opens a new session: creates the emulator, connects, returns the session id
    /// used by every subsequent call. Throws on connection failure.
    /// </summary>
    Task<Guid> OpenSessionAsync(ConnectionFactory.ConnectionParameters parameters, CancellationToken cancellationToken = default);

    /// <summary>
    /// The session for an id, or null when unknown/closed.
    /// </summary>
    TerminalSession? GetSession(Guid id);

    /// <summary>
    /// All open sessions — lets a new LLM turn find a session it opened earlier.
    /// </summary>
    IReadOnlyList<SessionInfo> ListSessions();

    /// <summary>
    /// Disconnects and disposes the session. Unknown ids are a no-op.
    /// </summary>
    Task CloseSessionAsync(Guid id);
}
