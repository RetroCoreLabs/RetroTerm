using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using RetroTerm.Core.Protocols;
using RetroTerm.Desktop;
using RetroTerm.Desktop.Views;
using Xunit;

namespace RetroTerm.Tests.Avalonia;

/// <summary>
/// Closing a popped-out window must not hang the app.
/// </summary>
/// <remarks>
/// <para><b>Ronny's report, 1 September 2026</b></para>
/// "Popping out a window and trying to close it or the app hangs the app very hard." Traced to
/// <c>TerminalPopoutWindow.OnClosing</c>: it fired a background <c>Task.Run(() =&gt;
/// DisconnectAsync())</c> and, on the same call stack, called <c>_tab.Dispose()</c> synchronously
/// on the UI thread — which itself blocks with <c>DisconnectAsync().GetAwaiter().GetResult()</c>
/// when the session is still connected. Two concurrent disconnects touching the same connection,
/// one of them holding the UI thread hostage.
///
/// <para><b>Why these tests use a delay and a call counter, not a real deadlock</b></para>
/// Reproducing the exact deadlock needs a connection whose disconnect wants to resume on the
/// captured UI <c>SynchronizationContext</c> while that thread is blocked — genuinely flaky and
/// slow to force in a unit test, and if it ever DOES hang, an unbounded test hangs the whole
/// suite along with it. Instead: <see cref="RaceDetectingConnection"/> fails loudly the moment it
/// sees a second concurrent call to <c>DisconnectAsync</c>, which is the actual defect regardless
/// of whether it happens to deadlock on a given machine, and every close is wrapped in a bounded
/// timeout so a regression fails fast instead of hanging the run.
/// </remarks>
[Collection("Avalonia")]
public class ClosingAPoppedOutWindowDoesNotHangTests
{
    private static async Task PumpDispatcherAsync()
    {
        for (int i = 0; i < 50; i++)
        {
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
            await Task.Delay(20);
        }
    }

    [AvaloniaFact]
    public async Task ClosingAConnectedPopoutCompletesWithoutHanging()
    {
        var window = new MainWindow();
        window.Show();

        var tab = window.AddTabForTesting("localhost:23");
        var connection = new RaceDetectingConnection();
        await tab.Session.ConnectAsync(connection);
        Assert.True(tab.IsConnected);

        var popout = window.PopOutTabForTesting(tab);

        var closeTask = Task.Run(() =>
            Dispatcher.UIThread.Invoke(() => popout.Close()));

        var completed = await Task.WhenAny(closeTask, Task.Delay(TimeSpan.FromSeconds(5)));
        Assert.Same(closeTask, completed);

        await PumpDispatcherAsync();

        Assert.Equal(1, connection.DisconnectCallCount);
        Assert.Equal(0, connection.ConcurrentDisconnectCallsObserved);
        Assert.True(connection.IsDisposed);
        Assert.False(tab.IsConnected);

        window.Close();
    }

    [AvaloniaFact]
    public async Task ClosingAnAlreadyDisconnectedPopoutCompletesWithoutHanging()
    {
        var window = new MainWindow();
        window.Show();

        var tab = window.AddTabForTesting("localhost:23");
        var popout = window.PopOutTabForTesting(tab);

        var closeTask = Task.Run(() =>
            Dispatcher.UIThread.Invoke(() => popout.Close()));

        var completed = await Task.WhenAny(closeTask, Task.Delay(TimeSpan.FromSeconds(5)));
        Assert.Same(closeTask, completed);

        window.Close();
    }

    [AvaloniaFact]
    public async Task ForceCloseAsyncDisconnectsBeforeTheWindowActuallyCloses()
    {
        var window = new MainWindow();
        window.Show();

        var tab = window.AddTabForTesting("localhost:23");
        var connection = new RaceDetectingConnection();
        await tab.Session.ConnectAsync(connection);

        var popout = window.PopOutTabForTesting(tab);

        var forceCloseTask = popout.ForceCloseAsync();
        var completed = await Task.WhenAny(forceCloseTask, Task.Delay(TimeSpan.FromSeconds(5)));
        Assert.Same(forceCloseTask, completed);

        Assert.Equal(1, connection.DisconnectCallCount);
        Assert.Equal(0, connection.ConcurrentDisconnectCallsObserved);

        window.Close();
    }

    /// <summary>
    /// A minimal <see cref="IConnection"/> that takes a moment to disconnect and fails loudly if
    /// a second disconnect overlaps the first — the shape of the actual defect, independent of
    /// whether a given machine happens to deadlock on it.
    /// </summary>
    private sealed class RaceDetectingConnection : IConnection
    {
        private ConnectionStatus _status = ConnectionStatus.Disconnected;
        private int _inFlight;

        public ConnectionStatus Status => _status;
        public string ConnectionType => "RaceDetecting";
        public string Description => "Race-detecting test connection";
        public bool IsConnected => _status == ConnectionStatus.Connected;
        public bool IsDisposed { get; private set; }

        public int DisconnectCallCount { get; private set; }

        public int ConcurrentDisconnectCallsObserved { get; private set; }

        public event Action<ConnectionStatus>? StatusChanged;
        public event Action<ReadOnlyMemory<byte>>? DataReceived;
        public event Action<Exception>? ErrorOccurred;

        public Task ConnectAsync(CancellationToken cancellationToken = default)
        {
            _status = ConnectionStatus.Connected;
            StatusChanged?.Invoke(_status);
            return Task.CompletedTask;
        }

        public async Task DisconnectAsync()
        {
            if (Interlocked.Increment(ref _inFlight) > 1)
            {
                ConcurrentDisconnectCallsObserved++;
            }

            DisconnectCallCount++;
            try
            {
                await Task.Delay(50);
                _status = ConnectionStatus.Disconnected;
                StatusChanged?.Invoke(_status);
            }
            finally
            {
                Interlocked.Decrement(ref _inFlight);
            }
        }

        public Task SendAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        /// <summary>
        /// Never called by these tests — exists only so <see cref="DataReceived"/> and
        /// <see cref="ErrorOccurred"/> are not flagged as unused, since <c>IConnection</c>
        /// requires both events regardless of whether a given test exercises them.
        /// </summary>
        public void NeverCalled()
        {
            DataReceived?.Invoke(ReadOnlyMemory<byte>.Empty);
            ErrorOccurred?.Invoke(new InvalidOperationException());
        }

        public void Dispose()
        {
            IsDisposed = true;
        }
    }
}
