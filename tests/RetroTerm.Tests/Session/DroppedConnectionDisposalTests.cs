using System;
using System.Threading;
using System.Threading.Tasks;
using RetroTerm.Core.Protocols;
using RetroTerm.Core.Session;
using RetroTerm.Core.Terminal.Emulators;
using RetroTerm.Tests.TDV;
using Xunit;

namespace RetroTerm.Tests.Session;

/// <summary>
/// A connection that leaves a session — dropped by the remote side, deliberately
/// disconnected, or failed while connecting — must be DISPOSED, not just forgotten.
///
/// Why this matters enough for its own file: for a serial connection, Dispose is what
/// closes the COM port. A dropped serial line whose connection object is merely
/// dereferenced keeps the OS handle open, and every later attempt to open that port —
/// by this app or any other — fails with "Access to the path 'COM11' is denied"
/// (seen live 30 August 2026). The session cannot know which connection type it holds,
/// so the rule is universal: out of the session means disposed.
/// </summary>
public class DroppedConnectionDisposalTests : IDisposable
{
    private readonly TerminalSession _session;
    private readonly InMemoryConnection _connection;

    public DroppedConnectionDisposalTests()
    {
        _session = new TerminalSession(new VT100Emulator(80, 24), "DisposalTest");
        _connection = new InMemoryConnection();
    }

    public void Dispose()
    {
        _session.Dispose();
    }

    [Fact]
    public async Task ARemoteDrop_DisposesTheConnection()
    {
        await _session.ConnectAsync(_connection);
        Assert.True(_session.IsConnected);

        string? lostReason = null;
        _session.ConnectionLost += reason => lostReason = reason;

        // The remote side goes away — the status change a real receive loop raises.
        _connection.SimulateRemoteDrop();

        Assert.True(_connection.IsDisposed);
        Assert.False(_session.IsConnected);
        Assert.NotNull(lostReason); // scripts and MCP clients were told
    }

    [Fact]
    public async Task ADeliberateDisconnect_DisposesTheConnection()
    {
        await _session.ConnectAsync(_connection);

        await _session.DisconnectAsync();

        Assert.True(_connection.IsDisposed);
        Assert.False(_session.IsConnected);
    }

    [Fact]
    public async Task AFailedConnect_DisposesTheConnection()
    {
        var failing = new FailingConnection();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _session.ConnectAsync(failing));

        Assert.True(failing.IsDisposed);
        Assert.False(_session.IsConnected);
    }

    /// <summary>
    /// A connection whose ConnectAsync always throws — the shape of a serial open
    /// against a port another process already holds.
    /// </summary>
    private sealed class FailingConnection : IConnection
    {
        public ConnectionStatus Status => ConnectionStatus.Error;
        public string ConnectionType => "Failing";
        public string Description => "Always fails";
        public bool IsConnected => false;

        public event Action<ConnectionStatus>? StatusChanged;
        public event Action<ReadOnlyMemory<byte>>? DataReceived;
        public event Action<Exception>? ErrorOccurred;

        public bool IsDisposed { get; private set; }

        public Task ConnectAsync(CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("Access to the path 'COM11' is denied.");

        public Task DisconnectAsync() => Task.CompletedTask;

        public Task SendAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public void Dispose()
        {
            IsDisposed = true;
            // The three events are declared by IConnection and never raised here —
            // reference them so warnings-as-errors does not reject the test double.
            _ = StatusChanged; _ = DataReceived; _ = ErrorOccurred;
        }
    }
}
