using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using RetroTerm.Desktop;
using RetroTerm.Tests.TDV;
using Xunit;

namespace RetroTerm.Tests.Avalonia;

/// <summary>
/// Closing the main window with an active connection must show its confirmation dialog, not
/// crash on the way there.
/// </summary>
/// <remarks>
/// <para><b>Ronny's report, 1 September 2026</b></para>
/// Closing the running app crashed with "Cannot show a window with a closed owner"
/// (<c>System.InvalidOperationException</c>), logged to <c>crash.log</c> with a stack trace
/// through <c>MainWindow.ShowCloseConfirmationDialog</c> → <c>Window.ShowDialog(Window
/// owner)</c>.
///
/// <para><b>Why it happened</b></para>
/// <c>MainWindow.OnClosing</c> is <c>async void</c>. Avalonia reads <c>e.Cancel</c> the moment
/// the method call returns control to it — for an <c>async void</c> method that is at the first
/// incomplete <c>await</c>, not when the method is logically finished. The old code left
/// <c>Cancel</c> at its default (false) through <c>await ShutdownMcpServerAsync()</c> and the
/// gateway shutdown, so Avalonia went ahead and actually closed the window while the method was
/// still awaiting. By the time execution reached <c>e.Cancel = true</c> and tried
/// <c>ShowDialog(this)</c>, the owner was already closed.
///
/// <para><b>The fix</b></para>
/// <c>e.Cancel = true</c> is now the first statement in <c>OnClosing</c>, unconditionally, before
/// any <c>await</c> — the window can never finish closing while this method still has async work
/// left. A guard flag lets the method's own later <c>Close()</c> call pass straight through
/// instead of re-entering teardown.
/// </remarks>
[Collection("Avalonia")]
public class ClosingTheMainWindowWithActiveConnectionsDoesNotCrashTests
{
    private static async Task<Window?> WaitForOwnedWindowAsync(Window owner)
    {
        for (int i = 0; i < 100; i++)
        {
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
            var owned = owner.OwnedWindows;
            for (int j = 0; j < owned.Count; j++)
            {
                if (owned[j] != null)
                {
                    return owned[j];
                }
            }

            await Task.Delay(20);
        }

        return null;
    }

    /// <summary>
    /// The close-confirmation dialog is built entirely in code, with no XAML root to register a
    /// <c>NameScope</c> — so <c>Window.FindControl</c> cannot see its buttons. Walking the visual
    /// tree by hand finds them by their <see cref="StyledElement.Name"/> instead.
    /// </summary>
    private static Button? FindButtonByName(Visual root, string name)
    {
        var stack = new Stack<Visual>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var current = stack.Pop();
            if (current is Button button && button.Name == name)
            {
                return button;
            }

            foreach (var child in current.GetVisualChildren())
            {
                stack.Push(child);
            }
        }

        return null;
    }

    [AvaloniaFact]
    public async Task ClosingWithAnActiveConnectionShowsTheDialogInsteadOfCrashing()
    {
        var window = new MainWindow();
        window.Show();

        var tab = window.AddTabForTesting("localhost:23");
        await tab.Session.ConnectAsync(new InMemoryConnection());
        Assert.True(tab.IsConnected);

        window.Close();

        var dialog = await WaitForOwnedWindowAsync(window);

        Assert.NotNull(dialog);
        Assert.True(window.IsVisible, "the owner must still be open while its confirmation dialog is up");

        var cancelButton = FindButtonByName(dialog!, "CloseConfirmationCancelButton");
        Assert.NotNull(cancelButton);
        cancelButton!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        for (int i = 0; i < 50; i++)
        {
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
            await Task.Delay(20);
        }

        Assert.True(window.IsVisible, "Cancel must leave the window open");

        await tab.Session.DisconnectAsync();
        window.Close();
    }

    [AvaloniaFact]
    public async Task ClosingAllFromTheDialogActuallyClosesTheWindow()
    {
        var window = new MainWindow();
        window.Show();

        var tab = window.AddTabForTesting("localhost:23");
        await tab.Session.ConnectAsync(new InMemoryConnection());

        var closed = false;
        window.Closed += (_, _) => closed = true;

        window.Close();

        var dialog = await WaitForOwnedWindowAsync(window);
        Assert.NotNull(dialog);

        var closeAllButton = FindButtonByName(dialog!, "CloseConfirmationCloseAllButton");
        Assert.NotNull(closeAllButton);
        closeAllButton!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!closed && DateTime.UtcNow < deadline)
        {
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
            await Task.Delay(20);
        }

        Assert.True(closed, "Close All must actually close the window");
    }

    /// <summary>
    /// Reads the rendered fill of a button: a point at mid-height, six pixels in from its left
    /// edge, which is inside the button's padding and clear of the text.
    /// </summary>
    private static SkiaSharp.SKColor ColourAtCentreOf(Button button, Window dialog, RenderedScreenshot shot)
    {
        var point = button.TranslatePoint(new Point(6, button.Bounds.Height / 2), dialog);
        Assert.True(point.HasValue, "the button must be laid out inside the dialog");
        return shot.PixelAt((int)point!.Value.X, (int)point.Value.Y);
    }

    private static bool LooksRed(SkiaSharp.SKColor c) => c.Red > c.Green + 40 && c.Red > c.Blue + 40;

    private static bool LooksDark(SkiaSharp.SKColor c) => c.Red + c.Green + c.Blue < 300;

    [AvaloniaFact]
    public async Task TheDialogIsDarkAndItsButtonsSurviveTheMouse()
    {
        // Ronny, 27 September 2026: "colours very odd (gray and red) but worse, when I hover
        // over them with the mouse the button disappears." The dialog was a white window with
        // hex brushes set straight on its Buttons; the Fluent Button theme swaps the content
        // presenter's background on :pointerover, which such a brush cannot reach, so the fill
        // went. This pins both halves: the window is the theme's dark, and the Close All button
        // is still red with the pointer over it.
        var window = new MainWindow();
        window.Show();

        var tab = window.AddTabForTesting("localhost:23");
        await tab.Session.ConnectAsync(new InMemoryConnection());

        window.Close();
        var dialog = await WaitForOwnedWindowAsync(window);
        Assert.NotNull(dialog);
        for (int i = 0; i < 10; i++)
        {
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
            await Task.Delay(20);
        }

        var closeAll = FindButtonByName(dialog!, "CloseConfirmationCloseAllButton");
        var cancel = FindButtonByName(dialog!, "CloseConfirmationCancelButton");
        Assert.NotNull(closeAll);
        Assert.NotNull(cancel);

        var before = RenderedScreenshot.CaptureWindow(dialog!, "close-dialog");
        Assert.NotNull(before);
        Assert.True(LooksDark(before!.PixelAt(4, 4)), $"the dialog background must be the theme's dark, was {before.PixelAt(4, 4)}");
        var restRed = ColourAtCentreOf(closeAll!, dialog!, before);
        Assert.True(LooksRed(restRed), $"Close All must be the theme's error red, was {restRed}");
        var restCancel = ColourAtCentreOf(cancel!, dialog!, before);
        Assert.False(LooksRed(restCancel), $"Cancel must not be red, was {restCancel}");

        // Now the mouse is over Close All.
        ((global::Avalonia.Controls.IPseudoClasses)closeAll!.Classes).Set(":pointerover", true);
        for (int i = 0; i < 10; i++)
        {
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
            await Task.Delay(20);
        }
        var hovered = RenderedScreenshot.CaptureWindow(dialog!, "close-dialog-hover");
        Assert.NotNull(hovered);
        var hoverRed = ColourAtCentreOf(closeAll, dialog!, hovered!);
        Assert.True(LooksRed(hoverRed), $"Close All must stay red under the pointer, was {hoverRed}");

        ((global::Avalonia.Controls.IPseudoClasses)closeAll.Classes).Set(":pointerover", false);
        cancel!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        for (int i = 0; i < 20; i++)
        {
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
            await Task.Delay(20);
        }
        await tab.Session.DisconnectAsync();
        window.Close();
    }
}
