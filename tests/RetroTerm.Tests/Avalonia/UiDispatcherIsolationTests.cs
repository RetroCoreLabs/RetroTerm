using Avalonia.Headless.XUnit;
using Avalonia.Threading;

namespace RetroTerm.Tests.Avalonia;

/// <summary>
/// Pins the headless test isolation model: every UI test shares ONE Application and ONE UI
/// dispatcher, as a running RetroTerm does.
/// </summary>
/// <remarks>
/// <para>
/// Avalonia 12 recreates the dispatcher for every test by default (PerTest isolation), and that
/// mode has a race: the process-wide UI dispatcher is reset to nothing before each test, and
/// whichever thread reads <c>Dispatcher.UIThread</c> first becomes the UI thread. A background
/// thread from a test running in parallel won that race once on GitHub and failed the release.
/// The assembly attribute in AvaloniaTestAppBuilder.cs sets PerAssembly instead.
/// </para>
/// <para>
/// The race itself is too rare to reproduce on demand, so these two tests check the thing that
/// makes it impossible: the UI dispatcher a test sees is the same object the other test saw.
/// Under PerTest isolation the second of the two to run fails every time, whatever the order.
/// </para>
/// </remarks>
[Collection("Avalonia")]
public class UiDispatcherIsolationTests
{
    // Whichever test runs first records the dispatcher it saw; the other compares against it.
    private static Dispatcher? s_seen;

    [AvaloniaFact]
    public void TheFirstTestSeesTheSharedUiDispatcher() => CheckTheDispatcherIsShared();

    [AvaloniaFact]
    public void TheSecondTestSeesTheSameUiDispatcher() => CheckTheDispatcherIsShared();

    private static void CheckTheDispatcherIsShared()
    {
        var current = Dispatcher.UIThread;
        Assert.True(current.CheckAccess(), "the test body is not running on the UI dispatcher's thread");

        var first = Interlocked.CompareExchange(ref s_seen, current, null);
        if (first != null)
        {
            Assert.Same(first, current);
        }
    }
}
