using System;
using System.Threading;
using System.Threading.Tasks;
using RetroTerm.Core.Protocols;
using RetroTerm.Core.Session;
using RetroTerm.Core.Terminal.Emulators;
using RetroTerm.Core.Transfer;
using Xunit;

namespace RetroTerm.Tests.Desktop;

/// <summary>
/// Tests for TerminalSession file transfer mode:
/// - Data routing (transfer intercept vs emulator)
/// - Transfer lifecycle (start, cancel, complete, cleanup)
/// - Event firing (TransferStateChanged, TransferProgressChanged)
/// - Error conditions (not connected, already transferring)
/// </summary>
public class TransferSessionTests
{
    [Fact]
    public void IsTransferActive_DefaultsFalse()
    {
        var emulator = new VT100Emulator(80, 24);
        var session = new TerminalSession(emulator, "Test");

        Assert.False(session.IsTransferActive);
        Assert.Null(session.ActiveTransferHandler);
    }

    [Fact]
    public async Task StartFileTransferAsync_WhenNotConnected_ThrowsInvalidOperation()
    {
        var emulator = new VT100Emulator(80, 24);
        var session = new TerminalSession(emulator, "Test");
        var handler = new FakeTransferHandler();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            session.StartFileTransferAsync(handler, TransferDirection.Send, new[] { "test.txt" }, CancellationToken.None));
    }

    [Fact]
    public async Task StartFileTransferAsync_SetsTransferActive()
    {
        var emulator = new VT100Emulator(80, 24);
        var session = new TerminalSession(emulator, "Test");
        var connection = new FakeConnection();
        await session.ConnectAsync(connection);

        bool stateChangedFired = false;
        session.TransferStateChanged += (active) => { if (active) stateChangedFired = true; };

        var handler = new FakeTransferHandler();
        _ = session.StartFileTransferAsync(handler, TransferDirection.Send, new[] { "test.txt" }, CancellationToken.None);

        Assert.True(session.IsTransferActive);
        Assert.Same(handler, session.ActiveTransferHandler);
        Assert.True(stateChangedFired);
    }

    [Fact]
    public async Task StartFileTransferAsync_WhenAlreadyTransferring_ThrowsInvalidOperation()
    {
        var emulator = new VT100Emulator(80, 24);
        var session = new TerminalSession(emulator, "Test");
        var connection = new FakeConnection();
        await session.ConnectAsync(connection);

        var handler1 = new FakeTransferHandler();
        _ = session.StartFileTransferAsync(handler1, TransferDirection.Send, new[] { "test.txt" }, CancellationToken.None);

        var handler2 = new FakeTransferHandler();
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            session.StartFileTransferAsync(handler2, TransferDirection.Send, new[] { "test2.txt" }, CancellationToken.None));
    }

    [Fact]
    public async Task CancelFileTransfer_SetsTransferInactive()
    {
        var emulator = new VT100Emulator(80, 24);
        var session = new TerminalSession(emulator, "Test");
        var connection = new FakeConnection();
        await session.ConnectAsync(connection);

        var handler = new FakeTransferHandler();
        _ = session.StartFileTransferAsync(handler, TransferDirection.Send, new[] { "test.txt" }, CancellationToken.None);

        Assert.True(session.IsTransferActive);

        session.CancelFileTransfer();
        Assert.True(handler.CancelCalled);
    }

    [Fact]
    public async Task TransferProgressChanged_ForwardsFromHandler()
    {
        var emulator = new VT100Emulator(80, 24);
        var session = new TerminalSession(emulator, "Test");
        var connection = new FakeConnection();
        await session.ConnectAsync(connection);

        TransferProgress? lastProgress = null;
        session.TransferProgressChanged += (p) => lastProgress = p;

        var handler = new FakeTransferHandler();
        _ = session.StartFileTransferAsync(handler, TransferDirection.Send, new[] { "test.txt" }, CancellationToken.None);

        var progress = new TransferProgress("test.txt", 500, 1000, 1, 1, TransferState.Transferring, TransferDirection.Send);
        handler.FireProgressChanged(progress);

        Assert.NotNull(lastProgress);
        Assert.Equal("test.txt", lastProgress.Value.FileName);
        Assert.Equal(500, lastProgress.Value.BytesTransferred);
    }

    [Fact]
    public async Task TransferCompleted_CleansUpHandler()
    {
        var emulator = new VT100Emulator(80, 24);
        var session = new TerminalSession(emulator, "Test");
        var connection = new FakeConnection();
        await session.ConnectAsync(connection);

        bool stateInactive = false;
        session.TransferStateChanged += (active) => { if (!active) stateInactive = true; };

        var handler = new FakeTransferHandler();
        _ = session.StartFileTransferAsync(handler, TransferDirection.Send, new[] { "test.txt" }, CancellationToken.None);

        // Simulate transfer completing
        var completedProgress = new TransferProgress("test.txt", 1000, 1000, 1, 1, TransferState.Completed, TransferDirection.Send);
        handler.FireTransferCompleted(completedProgress);

        Assert.False(session.IsTransferActive);
        Assert.Null(session.ActiveTransferHandler);
        Assert.True(stateInactive);
    }

    [Fact]
    public void Dispose_CleansUpTransfer()
    {
        var emulator = new VT100Emulator(80, 24);
        var session = new TerminalSession(emulator, "Test");

        // Should not throw even with no transfer active
        session.Dispose();
    }

    // ── Fake implementations ────────────────────────────────────────

    private class FakeTransferHandler : IFileTransferHandler
    {
        public TransferState State { get; private set; } = TransferState.Idle;
        public bool CancelCalled { get; private set; }

        public event Action<TransferProgress>? ProgressChanged;
        public event Action<TransferProgress>? TransferCompleted;

        public void ProcessIncomingData(ReadOnlySpan<byte> data) { }

        public Task StartSendAsync(string[] filePaths, SendBytesAsync sendDelegate, CancellationToken ct)
        {
            State = TransferState.Transferring;
            return Task.CompletedTask;
        }

        public Task StartReceiveAsync(string saveDirectory, SendBytesAsync sendDelegate, CancellationToken ct)
        {
            State = TransferState.Transferring;
            return Task.CompletedTask;
        }

        public void Cancel()
        {
            CancelCalled = true;
            State = TransferState.Cancelled;
        }

        public void FireProgressChanged(TransferProgress progress) => ProgressChanged?.Invoke(progress);
        public void FireTransferCompleted(TransferProgress progress) => TransferCompleted?.Invoke(progress);
    }

    private class FakeConnection : IConnection
    {
        public bool IsConnected { get; private set; }
        public ConnectionStatus Status { get; private set; }
        public string Description => "Fake";
        public string ConnectionType => "Fake";

        public event Action<ConnectionStatus>? StatusChanged;
        public event Action<ReadOnlyMemory<byte>>? DataReceived;
        // Required by IConnection; this in-memory test double has no transport that
        // can fail, so it never raises the event.
#pragma warning disable CS0067
        public event Action<Exception>? ErrorOccurred;
#pragma warning restore CS0067

        public Task ConnectAsync(CancellationToken ct = default)
        {
            IsConnected = true;
            Status = ConnectionStatus.Connected;
            StatusChanged?.Invoke(ConnectionStatus.Connected);
            return Task.CompletedTask;
        }

        public Task DisconnectAsync()
        {
            IsConnected = false;
            Status = ConnectionStatus.Disconnected;
            return Task.CompletedTask;
        }

        public Task SendAsync(ReadOnlyMemory<byte> data, CancellationToken ct = default) => Task.CompletedTask;

        public void SimulateDataReceived(byte[] data) => DataReceived?.Invoke(data);

        public void Dispose() { }
    }
}
