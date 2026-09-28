using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using RetroTerm.Core.Configuration;
using RetroTerm.Desktop;
using Xunit;

namespace RetroTerm.Tests.Avalonia;

/// <summary>
/// File → New Tab As offers every terminal, so one can be tried without saving a connection.
///
/// Before this, the only way to open a VT420 was to create and store a connection profile for it.
/// Plain New Tab still opens a VT100 - Ctrl+T should not change under anyone's fingers.
/// </summary>
[Collection("Avalonia")]
public class NewTabAsMenuTests
{
    private static MenuItem? FindNewTabAsItem(Window window)
        => window.FindControl<MenuItem>("NewTabAsMenuItem");

    private static List<string> HeadersOf(MenuItem parent)
    {
        var headers = new List<string>();
        var items = parent.ItemsSource;
        if (items == null)
        {
            return headers;
        }

        var enumerator = items.GetEnumerator();
        while (enumerator.MoveNext())
        {
            if (enumerator.Current is MenuItem item && item.Header is string header)
            {
                headers.Add(header);
            }
        }
        return headers;
    }

    [AvaloniaFact]
    public void ItListsEveryTerminalTheFactoryCanBuild()
    {
        var window = new MainWindow();

        var parent = FindNewTabAsItem(window);
        Assert.NotNull(parent);

        var headers = HeadersOf(parent!);
        var expected = EmulatorFactory.AvailableEmulators;

        Assert.Equal(expected.Length, headers.Count);
        for (int i = 0; i < expected.Length; i++)
        {
            Assert.Contains(expected[i], headers);
        }
    }

    [AvaloniaFact]
    public void ChoosingOneOpensATabRunningThatTerminal()
    {
        // The menu listing a name proves nothing on its own - clicking it has to produce that
        // terminal, which is the whole point of the entry.
        var window = new MainWindow();
        var parent = FindNewTabAsItem(window);
        Assert.NotNull(parent);

        MenuItem? vt420 = null;
        var enumerator = parent!.ItemsSource!.GetEnumerator();
        while (enumerator.MoveNext())
        {
            if (enumerator.Current is MenuItem item && (item.Header as string) == "VT420")
            {
                vt420 = item;
                break;
            }
        }

        Assert.NotNull(vt420);

        int before = window.OpenTabCount;
        vt420!.RaiseEvent(new global::Avalonia.Interactivity.RoutedEventArgs(MenuItem.ClickEvent));

        Assert.Equal(before + 1, window.OpenTabCount);
        Assert.Equal("VT420", window.ActiveTabEmulatorName);
    }
}
