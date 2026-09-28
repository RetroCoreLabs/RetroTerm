using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using RetroTerm.Core.Configuration;
using RetroTerm.Core.Protocols;

namespace RetroTerm.Core.Session;

/// <summary>
/// Headless <see cref="ITerminalSessionHost"/>: sessions live in this process with no
/// UI attached. Used by tests today and by a future console-hosted MCP server.
/// The desktop app has its own implementation that creates tabs instead.
///
/// Thread-safe: MCP calls arrive on arbitrary threads.
/// </summary>
public sealed class InProcessSessionHost : ITerminalSessionHost, IDisposable
{
    private readonly object _lock = new object();
    private readonly Dictionary<Guid, TerminalSession> _sessions = new Dictionary<Guid, TerminalSession>();

    // Connection creation is pluggable so tests can hand out InMemoryConnection.
    private readonly Func<ConnectionFactory.ConnectionParameters, IConnection> _connectionFactory;

    public InProcessSessionHost() : this(ConnectionFactory.CreateConnection)
    {
    }

    public InProcessSessionHost(Func<ConnectionFactory.ConnectionParameters, IConnection> connectionFactory)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
    }

    public async Task<Guid> OpenSessionAsync(ConnectionFactory.ConnectionParameters parameters, CancellationToken cancellationToken = default)
    {
        if (parameters == null) throw new ArgumentNullException(nameof(parameters));

        var emulator = EmulatorFactory.CreateEmulator(parameters.EmulatorType, parameters.Width, parameters.Height);
        var session = new TerminalSession(emulator, parameters.DisplayName);
        var connection = _connectionFactory(parameters);

        try
        {
            await session.ConnectAsync(connection, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            session.Dispose();
            throw;
        }

        var id = Guid.NewGuid();
        lock (_lock)
        {
            _sessions.Add(id, session);
        }
        return id;
    }

    public TerminalSession? GetSession(Guid id)
    {
        lock (_lock)
        {
            return _sessions.TryGetValue(id, out var session) ? session : null;
        }
    }

    public IReadOnlyList<SessionInfo> ListSessions()
    {
        var result = new List<SessionInfo>();
        lock (_lock)
        {
            // Dictionary enumeration under the lock; session list is tiny.
            var e = _sessions.GetEnumerator();
            while (e.MoveNext())
            {
                var session = e.Current.Value;
                result.Add(new SessionInfo(e.Current.Key, session.Title, session.IsConnected,
                    session.Emulator.GetType().Name));
            }
        }
        return result;
    }

    public async Task CloseSessionAsync(Guid id)
    {
        TerminalSession? session;
        lock (_lock)
        {
            if (!_sessions.TryGetValue(id, out session))
            {
                return;
            }
            _sessions.Remove(id);
        }

        try
        {
            await session!.DisconnectAsync().ConfigureAwait(false);
        }
        finally
        {
            session!.Dispose();
        }
    }

    public void Dispose()
    {
        List<TerminalSession> toDispose;
        lock (_lock)
        {
            toDispose = new List<TerminalSession>(_sessions.Count);
            var e = _sessions.GetEnumerator();
            while (e.MoveNext())
            {
                toDispose.Add(e.Current.Value);
            }
            _sessions.Clear();
        }
        for (int i = 0; i < toDispose.Count; i++)
        {
            try { toDispose[i].Dispose(); } catch { /* best effort on shutdown */ }
        }
    }
}
