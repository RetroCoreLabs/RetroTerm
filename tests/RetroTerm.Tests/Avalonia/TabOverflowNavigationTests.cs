using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using RetroTerm.Desktop;
using Xunit;

namespace RetroTerm.Tests.Avalonia;

/// <summary>
/// Reaching a session when there are more tabs than the strip can show.
/// </summary>
/// <remarks>
/// <para><b>Ronny's report and decision, 1 September 2026</b></para>
/// "if we open many connection, there are too many TAB's to be rendered correct horizontally."
/// Given the choice, he picked scroll buttons PLUS a dropdown listing every open session, with
/// popped-out windows in the same list as docked tabs rather than a section of their own.
///
/// <para><b>What is and is not tested here</b></para>
/// The RULE for showing the scroll buttons, the contents and behaviour of the list, and the
/// clamping of the scroll are all tested. Whether the buttons look right, and whether a real
/// window actually takes focus when its row is picked, are not things a headless test can answer.
/// </remarks>
[Collection("Avalonia")]
public class TabOverflowNavigationTests
{
    [AvaloniaFact]
    public void TheScrollButtonsAppearOnlyWhenTheStripDoesNotFit()
    {
        // A strip narrower than its viewport, or exactly as wide, has nothing to scroll to.
        Assert.False(MainWindow.ShouldShowTabScrollButtons(200, 800));
        Assert.False(MainWindow.ShouldShowTabScrollButtons(800, 800));

        // The half-pixel margin: a rounding wobble must not flicker the buttons on.
        Assert.False(MainWindow.ShouldShowTabScrollButtons(800.4, 800));

        Assert.True(MainWindow.ShouldShowTabScrollButtons(801, 800));
        Assert.True(MainWindow.ShouldShowTabScrollButtons(4000, 800));
    }

    [AvaloniaFact]
    public void EveryOpenSessionIsListed_DockedTabsAndPoppedOutWindowsAlike()
    {
        var window = new MainWindow();
        window.Show();

        // The window builds itself a welcome tab, so count from where it actually starts.
        int initial = window.EnumerateOpenSessions().Count;

        var docked = window.AddTabForTesting("docked-host:23");
        var toPopOut = window.AddTabForTesting("popped-host:23");
        window.PopOutTabForTesting(toPopOut);

        var sessions = window.EnumerateOpenSessions();
        Assert.Equal(initial + 2, sessions.Count);

        bool sawDocked = false;
        bool sawPopped = false;
        for (int i = 0; i < sessions.Count; i++)
        {
            if (sessions[i].Tab.Id == docked.Id)
            {
                sawDocked = true;
                Assert.False(sessions[i].IsPoppedOut);
            }

            if (sessions[i].Tab.Id == toPopOut.Id)
            {
                sawPopped = true;
                Assert.True(sessions[i].IsPoppedOut);
                Assert.NotNull(sessions[i].Popout);
            }
        }

        Assert.True(sawDocked, "the docked tab must be listed");
        Assert.True(sawPopped, "the popped-out window must be listed");

        window.Close();
    }

    [AvaloniaFact]
    public void ThePoppedOutRowSaysSoAndTheActiveRowIsMarked()
    {
        var window = new MainWindow();
        window.Show();

        var docked = window.AddTabForTesting("docked-host:23");
        var toPopOut = window.AddTabForTesting("popped-host:23");
        window.PopOutTabForTesting(toPopOut);

        window.SelectTabForTesting(docked);

        var items = window.BuildOpenSessionMenuItems();
        var sessions = window.EnumerateOpenSessions();
        Assert.Equal(sessions.Count, items.Count);

        MenuItem? dockedRow = null;
        MenuItem? poppedRow = null;
        for (int i = 0; i < items.Count; i++)
        {
            if (Equals(items[i].Tag, docked.Id)) dockedRow = items[i];
            if (Equals(items[i].Tag, toPopOut.Id)) poppedRow = items[i];
        }

        Assert.NotNull(dockedRow);
        Assert.NotNull(poppedRow);

        // Each row must NAME its session. The whole point of the list is telling many
        // connections apart, so a row that just says "RetroTerm" would be useless in exactly the
        // case Ronny opened it for. This reads the live Title, not the tab header's TextBlock.
        Assert.Contains("docked-host:23", dockedRow!.Header?.ToString() ?? string.Empty);
        Assert.Contains("popped-host:23", poppedRow!.Header?.ToString() ?? string.Empty);

        // The suffix is what tells the reader the row will focus another window rather than
        // switch tabs here.
        Assert.Contains("separate window", poppedRow.Header?.ToString() ?? string.Empty);
        Assert.DoesNotContain("separate window", dockedRow.Header?.ToString() ?? string.Empty);

        // The dot marks the session you are already looking at.
        Assert.NotNull(dockedRow.Icon);
        Assert.Null(poppedRow.Icon);

        window.Close();
    }

    [AvaloniaFact]
    public void PickingADockedRowSelectsThatTab()
    {
        var window = new MainWindow();
        window.Show();

        var first = window.AddTabForTesting("first-host:23");
        var second = window.AddTabForTesting("second-host:23");

        window.SelectTabForTesting(first);
        Assert.Same(first, window.ActiveTabForTesting);

        var items = window.BuildOpenSessionMenuItems();
        MenuItem? secondRow = null;
        for (int i = 0; i < items.Count; i++)
        {
            if (Equals(items[i].Tag, second.Id)) secondRow = items[i];
        }

        Assert.NotNull(secondRow);
        secondRow!.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));

        Assert.Same(second, window.ActiveTabForTesting);

        window.Close();
    }

    [AvaloniaFact]
    public void PickingAPoppedOutRowDoesNotChangeWhichTabIsActive()
    {
        // Its session is not a tab here any more, so switching the tab strip to it would be
        // wrong - the row's whole job is to raise the other window.
        var window = new MainWindow();
        window.Show();

        var stays = window.AddTabForTesting("stays-host:23");
        var toPopOut = window.AddTabForTesting("popped-host:23");
        window.PopOutTabForTesting(toPopOut);

        window.SelectTabForTesting(stays);
        Assert.Same(stays, window.ActiveTabForTesting);

        var items = window.BuildOpenSessionMenuItems();
        MenuItem? poppedRow = null;
        for (int i = 0; i < items.Count; i++)
        {
            if (Equals(items[i].Tag, toPopOut.Id)) poppedRow = items[i];
        }

        Assert.NotNull(poppedRow);
        poppedRow!.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));

        Assert.Same(stays, window.ActiveTabForTesting);

        window.Close();
    }

    [AvaloniaFact]
    public void EnoughTabsInANarrowWindowActuallyRevealTheScrollButtons()
    {
        // The rule is unit-tested above; this drives the REAL layout, which is the only thing
        // that can catch the wiring being wrong - a scroller never found, an event never hooked,
        // a button left permanently hidden. It also writes a PNG of the tab bar to look at.
        var window = new MainWindow { Width = 420, Height = 400 };
        window.Show();

        var left = window.FindControlForTesting<Button>("ScrollTabsLeftButton");
        var right = window.FindControlForTesting<Button>("ScrollTabsRightButton");
        var list = window.FindControlForTesting<Button>("TabListButton");

        Assert.NotNull(left);
        Assert.NotNull(right);
        Assert.NotNull(list);

        // The all-sessions list is always available - it is the only route to a popped-out window.
        Assert.True(list!.IsVisible, "the session list button must always be available");

        for (int i = 0; i < 12; i++)
        {
            window.AddTabForTesting("a-fairly-long-hostname-" + i + ":23");
        }

        Dispatcher.UIThread.RunJobs();
        window.UpdateTabScrollButtonsForTesting();

        // The strip must NAME its tabs. A strip of identical "RetroTerm" labels would make the
        // overflow problem worse, not better - you could not tell which one to scroll to.
        Assert.Equal("a-fairly-long-hostname-0:23 (VT100)", window.TabHeaderTextForTesting(1));

        Assert.True(left!.IsVisible, "twelve tabs in a 420px window must overflow the strip");
        Assert.True(right!.IsVisible, "twelve tabs in a 420px window must overflow the strip");

        // Capture twice and keep the second. The first frame comes back rendered from BEFORE the
        // queued title updates landed - the "green while the screen still says LINE 0" trap - so
        // the first call is only there to force a fresh render.
        RenderedScreenshot.CaptureWindow(window)?.Dispose();
        Dispatcher.UIThread.RunJobs();

        var shot = RenderedScreenshot.CaptureWindow(window, "tab-overflow-scroll-buttons");
        Assert.NotNull(shot);
        shot!.Dispose();

        window.Close();
    }

    [AvaloniaFact]
    public void ScrollingIsClampedAndNeverGoesNegative()
    {
        // A strip that fits has nothing to scroll: both directions must leave it at zero rather
        // than parking the tabs off the left edge where nothing can bring them back.
        var window = new MainWindow();
        window.Show();

        window.ScrollTabStrip(-500);
        Assert.Equal(0, window.TabStripScrollOffsetForTesting);

        window.ScrollTabStrip(500);
        Assert.True(window.TabStripScrollOffsetForTesting >= 0);

        window.Close();
    }
}
