using System;
using System.Threading.Tasks;
using RetroTerm.Core.Protocols.Net;
using Xunit;

namespace RetroTerm.Tests.Protocols;

/// <summary>
/// The order in which a receive loop is stopped: release what it is blocked on first, then wait,
/// and never wait for ever. See ReceiveLoopShutdown for the SSH hang this came from.
/// </summary>
public class ReceiveLoopShutdownTests
{
    [Fact]
    public async Task ALoopThatOnlyEndsWhenTheStreamIsClosedEndsBecauseTheStreamIsClosedFirst()
    {
        // Stands in for ShellStream.ReadAsync: ignores cancellation, returns only once the stream
        // is closed under it.
        var streamClosed = new TaskCompletionSource();
        Task loop = streamClosed.Task;

        // A generous timeout: if the order were wrong this would run all of it and return false.
        bool ended = await ReceiveLoopShutdown.StopAsync(loop, () => streamClosed.SetResult(), 30000)
            .WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        Assert.True(ended, "the loop ends as soon as its stream is closed, which has to happen first");
    }

    [Fact]
    public async Task ALoopThatNeverEndsDoesNotHoldTheDisconnectUp()
    {
        // The stream was closed and the loop is still stuck: wait the timeout, then give up.
        var neverEnds = new TaskCompletionSource();

        bool ended = await ReceiveLoopShutdown.StopAsync(neverEnds.Task, () => { }, 100)
            .WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        Assert.False(ended);
    }

    [Fact]
    public async Task TheStreamIsClosedEvenWhenThereIsNoLoop()
    {
        bool closed = false;

        bool ended = await ReceiveLoopShutdown.StopAsync(null, () => closed = true, 100);

        Assert.True(ended);
        Assert.True(closed);
    }

    [Fact]
    public async Task ClosingTheStreamMayThrowAndTheWaitStillHappens()
    {
        var done = new TaskCompletionSource();
        done.SetResult();

        bool ended = await ReceiveLoopShutdown.StopAsync(
            done.Task, () => throw new InvalidOperationException("already closed"), 1000);

        Assert.True(ended);
    }

    [Fact]
    public async Task ALoopThatFailsOnTheWayOutStillCountsAsEnded()
    {
        // A read on a stream that was just disposed throws; that is how the loop ends.
        var failed = new TaskCompletionSource();
        failed.SetException(new ObjectDisposedException("stream"));

        bool ended = await ReceiveLoopShutdown.StopAsync(failed.Task, () => { }, 1000);

        Assert.True(ended);
    }
}
