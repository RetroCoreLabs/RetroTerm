using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using RetroTerm.Core.Configuration;
using RetroTerm.Desktop;
using Xunit;

namespace RetroTerm.Tests.Avalonia;

/// <summary>
/// View → Emulation changes the terminal on the tab in front of you, keeping its screen.
/// </summary>
/// <remarks>
/// The session half is covered by EmulationChangeTests. This is the half that only exists in the
/// window: the menu is built from the factory's list, and choosing an entry has to rewire the CANVAS
/// as well as the session - a change that left the canvas pointing at the replaced emulator would
/// keep drawing the old terminal's screen forever, and the session tests cannot see that.
/// </remarks>
[Collection("Avalonia")]
public class EmulationMenuTests
{
    private static MenuItem? FindEmulationItem(Window window)
        => window.FindControl<MenuItem>("EmulationMenuItem");

    /// <summary>
    /// Finds one entry of the submenu by its name.
    /// </summary>
    /// <param name="parent">
    /// The submenu.
    /// </param>
    /// <param name="header">
    /// The terminal name to look for.
    /// </param>
    /// <returns>
    /// The entry, or null.
    /// </returns>
    private static MenuItem? EntryNamed(MenuItem parent, string header)
    {
        var items = parent.ItemsSource;
        if (items == null) return null;

        var enumerator = items.GetEnumerator();
        while (enumerator.MoveNext())
        {
            if (enumerator.Current is MenuItem item && (item.Header as string) == header)
            {
                return item;
            }
        }

        return null;
    }

    [AvaloniaFact]
    public void ItListsEveryTerminalTheFactoryCanBuild()
    {
        var window = new MainWindow();

        var parent = FindEmulationItem(window);
        Assert.NotNull(parent);

        var headers = new List<string>();
        var enumerator = parent!.ItemsSource!.GetEnumerator();
        while (enumerator.MoveNext())
        {
            if (enumerator.Current is MenuItem item && item.Header is string header)
            {
                headers.Add(header);
            }
        }

        var expected = EmulatorFactory.AvailableEmulators;
        Assert.Equal(expected.Length, headers.Count);
        for (int i = 0; i < expected.Length; i++)
        {
            Assert.Contains(expected[i], headers);
        }
    }

    [AvaloniaFact]
    public async Task ChoosingOneChangesTheTabInPlaceRatherThanOpeningAnother()
    {
        var window = new MainWindow();
        var parent = FindEmulationItem(window);
        Assert.NotNull(parent);

        int tabsBefore = window.OpenTabCount;
        Assert.Equal("VT100", window.ActiveTabEmulatorName);

        var tdv = EntryNamed(parent!, "TDV2200");
        Assert.NotNull(tdv);

        tdv!.RaiseEvent(new global::Avalonia.Interactivity.RoutedEventArgs(MenuItem.ClickEvent));

        // The click starts the change and returns; letting the dispatcher run is what delivers the
        // session's EmulatorChanged back onto the UI thread.
        await Task.Delay(50);
        Dispatcher.UIThread.RunJobs();

        // Changed IN PLACE. Opening a second tab would lose whatever was on this one, which is the
        // behaviour this replaced: the connect path used to throw the tab away on a type mismatch.
        Assert.Equal(tabsBefore, window.OpenTabCount);
        Assert.Equal("TDV2200", window.ActiveTabEmulatorName);
    }

    [AvaloniaFact]
    public async Task TheCanvasIsRewiredToTheNewTerminal()
    {
        // THE ONE THE SESSION TESTS CANNOT SEE. The canvas builds its renderer, keyboard mapper and
        // selection manager FROM the emulator, so a change that did not reach it would leave all
        // three bound to a terminal the session has already let go of.
        var window = new MainWindow();
        var parent = FindEmulationItem(window);
        Assert.NotNull(parent);

        var tek = EntryNamed(parent!, "TEK4014");
        Assert.NotNull(tek);

        tek!.RaiseEvent(new global::Avalonia.Interactivity.RoutedEventArgs(MenuItem.ClickEvent));

        await Task.Delay(50);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("TEK4014", window.ActiveTabEmulatorName);

        // The canvas draws through its own emulator reference, and it has to be the session's.
        Assert.Same(SessionEmulatorOf(window), CanvasEmulatorOf(window));
    }

    /// <summary>
    /// Reads a private field, so the assertions can reach the canvas's own emulator reference.
    /// </summary>
    /// <remarks>
    /// Reflection on purpose, and for the reason RenderedScreenshot already gives for doing the
    /// same: adding an accessor to a production control that exists only so a test can look at it
    /// is worse than reaching in from the test. The controls stay unaware that tests exist.
    /// </remarks>
    /// <param name="target">
    /// Object to read from.
    /// </param>
    /// <param name="name">
    /// Field name.
    /// </param>
    /// <returns>
    /// The value, or null when the field is gone - which fails the assertion loudly rather than
    /// quietly passing.
    /// </returns>
    private static object? PrivateField(object target, string name)
    {
        var field = target.GetType().GetField(name,
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

        Assert.True(field != null,
            $"{target.GetType().Name}.{name} is gone - this test's reflection needs updating");

        return field!.GetValue(target);
    }

    /// <summary>
    /// The emulator the active tab's SESSION is running.
    /// </summary>
    /// <param name="window">
    /// The window.
    /// </param>
    /// <returns>
    /// The emulator.
    /// </returns>
    private static object? SessionEmulatorOf(MainWindow window)
    {
        var tab = PrivateField(window, "_activeTab");
        Assert.NotNull(tab);
        return ((RetroTerm.Desktop.Models.TabSession)tab!).Session.Emulator;
    }

    /// <summary>
    /// The emulator the active tab's CANVAS is drawing.
    /// </summary>
    /// <param name="window">
    /// The window.
    /// </param>
    /// <returns>
    /// The emulator.
    /// </returns>
    private static object? CanvasEmulatorOf(MainWindow window)
    {
        var tab = PrivateField(window, "_activeTab");
        Assert.NotNull(tab);

        var control = ((RetroTerm.Desktop.Models.TabSession)tab!).Control;
        var canvas = PrivateField(control, "_canvas");
        Assert.NotNull(canvas);

        return PrivateField(canvas!, "_emulator");
    }
}
