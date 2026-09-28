using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using RetroTerm.Desktop.Models;
using RetroTerm.Desktop.Views;

namespace RetroTerm.Desktop;

/// <summary>
/// Reaching a session when there are more of them than the tab strip can show.
/// </summary>
/// <remarks>
/// <para><b>Why this exists</b></para>
/// Ronny, 1 September 2026: "if we open many connection, there are too many TAB's to be rendered
/// correct horizontally." The strip has always been inside a <c>ScrollViewer</c> whose scrollbar
/// is hidden, so the tabs did scroll - with no visible way to do it, and nothing at all that could
/// reach a popped-out window.
///
/// <para><b>What was decided</b></para>
/// His call, same day, given the choice between scroll buttons alone, a menu alone, or both:
/// scroll buttons for nudging along the strip, PLUS a dropdown listing every open session by name
/// to jump straight to one. Popped-out windows appear in the SAME list as docked tabs rather than
/// in a section of their own, and picking one focuses that window.
/// </remarks>
public partial class MainWindow
{
    /// <summary>
    /// How far one press of a scroll button moves the tab strip, in pixels.
    /// </summary>
    /// <remarks>
    /// A fixed step rather than a viewport-sized page: a page jump past several tabs at once loses
    /// the reader's place, and the button is cheap to press twice.
    /// </remarks>
    private const double TabScrollStepPixels = 120;

    private ScrollViewer? _tabStripScroller;
    private Button? _scrollTabsLeftButton;
    private Button? _scrollTabsRightButton;

    /// <summary>
    /// One open session, whether it sits in the tab strip or in a window of its own.
    /// </summary>
    /// <remarks>
    /// The tab strip and the MCP session list had drifted into asking the question two different
    /// ways, and the MCP one forgot popped-out windows entirely (fixed the same day). This is the
    /// single answer to "what is open", so a third caller cannot invent a fourth answer.
    /// </remarks>
    internal readonly struct OpenSessionEntry
    {
        /// <summary>
        /// Creates an entry.
        /// </summary>
        /// <param name="tab">
        /// The session's tab. Popped-out sessions keep theirs; it is simply not in the strip.
        /// </param>
        /// <param name="popout">
        /// The window holding it, or null when the session is a docked tab.
        /// </param>
        public OpenSessionEntry(TabSession tab, TerminalPopoutWindow? popout)
        {
            Tab = tab;
            Popout = popout;
        }

        /// <summary>
        /// The session's tab.
        /// </summary>
        public TabSession Tab { get; }

        /// <summary>
        /// The window holding this session, or null when it is a docked tab.
        /// </summary>
        public TerminalPopoutWindow? Popout { get; }

        /// <summary>
        /// Whether this session lives in a window of its own rather than in the tab strip.
        /// </summary>
        public bool IsPoppedOut => Popout != null;
    }

    /// <summary>
    /// Every open session, docked tabs first and then popped-out windows.
    /// </summary>
    /// <returns>
    /// The sessions, in the order a reader sees them: the tab strip left to right, then the
    /// separate windows in the order they were popped out.
    /// </returns>
    internal List<OpenSessionEntry> EnumerateOpenSessions()
    {
        var result = new List<OpenSessionEntry>(_tabs.Count + _popoutWindows.Count);

        for (int i = 0; i < _tabs.Count; i++)
        {
            result.Add(new OpenSessionEntry(_tabs[i], null));
        }

        for (int i = 0; i < _popoutWindows.Count; i++)
        {
            result.Add(new OpenSessionEntry(_popoutWindows[i].TabSession, _popoutWindows[i]));
        }

        return result;
    }

    /// <summary>
    /// How far the tab strip is currently scrolled, for the test that pins the clamping.
    /// </summary>
    internal double TabStripScrollOffsetForTesting => _tabStripScroller?.Offset.X ?? 0;

    /// <summary>
    /// The tab in front, for tests that check which session a navigation action landed on.
    /// </summary>
    internal TabSession? ActiveTabForTesting => _activeTab;

    /// <summary>
    /// Recomputes the scroll buttons' visibility, for the test that drives the real layout.
    /// </summary>
    /// <remarks>
    /// A layout pass in a headless test does not reliably raise the scroller's own
    /// <c>ScrollChanged</c>, so the test asks directly rather than waiting for an event that may
    /// never come.
    /// </remarks>
    internal void UpdateTabScrollButtonsForTesting() => UpdateTabScrollButtons();

    /// <summary>
    /// Finds a named control in this window, for tests that need to see one the window does not
    /// otherwise expose.
    /// </summary>
    /// <typeparam name="T">
    /// The control's type.
    /// </typeparam>
    /// <param name="name">
    /// The name given in the XAML.
    /// </param>
    /// <returns>
    /// The control, or null when there is none by that name.
    /// </returns>
    internal T? FindControlForTesting<T>(string name) where T : Control
        => this.FindControl<T>(name);

    /// <summary>
    /// The text actually showing on a tab header in the strip.
    /// </summary>
    /// <param name="index">
    /// The tab's position in the strip, counting from zero.
    /// </param>
    /// <returns>
    /// The header's text, or null when there is no such tab.
    /// </returns>
    /// <remarks>
    /// Reads the rendered TextBlock rather than <c>TabSession.Title</c> on purpose: the two are
    /// updated by different mechanisms and a test that reads the property cannot see the header
    /// going stale.
    /// </remarks>
    internal string? TabHeaderTextForTesting(int index)
    {
        if (index < 0 || index >= _tabs.Count)
        {
            return null;
        }

        return _tabTitleTexts.TryGetValue(_tabs[index].Id, out var text) ? text.Text : null;
    }

    private void InitializeTabOverflow()
    {
        _tabStripScroller = this.FindControl<ScrollViewer>("TabStripScroller");
        _scrollTabsLeftButton = this.FindControl<Button>("ScrollTabsLeftButton");
        _scrollTabsRightButton = this.FindControl<Button>("ScrollTabsRightButton");

        if (_tabStripScroller != null)
        {
            // The strip's width changes for reasons this class does not see - a tab renamed by the
            // host, the window resized. Asking the scroller itself is the only account that is
            // always current.
            _tabStripScroller.ScrollChanged += (_, _) => UpdateTabScrollButtons();
        }

        UpdateTabScrollButtons();
    }

    /// <summary>
    /// Whether the tab strip has more in it than it can show, and so needs its scroll buttons.
    /// </summary>
    /// <param name="extentWidth">
    /// The full width of the tab strip's contents.
    /// </param>
    /// <param name="viewportWidth">
    /// The width actually on screen.
    /// </param>
    /// <returns>
    /// True when the contents do not fit.
    /// </returns>
    /// <remarks>
    /// Split out as a plain function so the RULE can be tested without a real layout pass, which
    /// a headless test cannot be relied on to produce for a strip that has never been measured.
    /// The half-pixel margin keeps a strip that fits exactly from flickering its buttons on and
    /// off as rounding moves the last pixel about.
    /// </remarks>
    internal static bool ShouldShowTabScrollButtons(double extentWidth, double viewportWidth)
    {
        return extentWidth - viewportWidth > 0.5;
    }

    private void UpdateTabScrollButtons()
    {
        if (_tabStripScroller == null)
        {
            return;
        }

        bool show = ShouldShowTabScrollButtons(
            _tabStripScroller.Extent.Width,
            _tabStripScroller.Viewport.Width);

        if (_scrollTabsLeftButton != null)
        {
            _scrollTabsLeftButton.IsVisible = show;
        }

        if (_scrollTabsRightButton != null)
        {
            _scrollTabsRightButton.IsVisible = show;
        }
    }

    /// <summary>
    /// Moves the tab strip by one step, clamped to its ends.
    /// </summary>
    /// <param name="deltaPixels">
    /// How far to move. Negative goes left.
    /// </param>
    internal void ScrollTabStrip(double deltaPixels)
    {
        if (_tabStripScroller == null)
        {
            return;
        }

        double maximum = _tabStripScroller.Extent.Width - _tabStripScroller.Viewport.Width;
        if (maximum < 0)
        {
            maximum = 0;
        }

        double target = _tabStripScroller.Offset.X + deltaPixels;
        if (target < 0)
        {
            target = 0;
        }
        else if (target > maximum)
        {
            target = maximum;
        }

        _tabStripScroller.Offset = new Vector(target, _tabStripScroller.Offset.Y);
    }

    private void OnScrollTabsLeftClick(object? sender, RoutedEventArgs e)
        => ScrollTabStrip(-TabScrollStepPixels);

    private void OnScrollTabsRightClick(object? sender, RoutedEventArgs e)
        => ScrollTabStrip(TabScrollStepPixels);

    /// <summary>
    /// Brings the active tab's header into view, so selecting a tab from the list does not leave
    /// the strip parked somewhere else.
    /// </summary>
    internal void ScrollActiveTabIntoView()
    {
        if (_activeTab == null)
        {
            return;
        }

        if (_tabHeaders.TryGetValue(_activeTab.Id, out var header))
        {
            header.BringIntoView();
        }
    }

    /// <summary>
    /// Builds one menu row per open session.
    /// </summary>
    /// <returns>
    /// The rows, in <see cref="EnumerateOpenSessions"/> order.
    /// </returns>
    /// <remarks>
    /// Separate from showing the flyout so a test can read the rows, and press one, without a
    /// popup window being involved.
    /// </remarks>
    internal List<MenuItem> BuildOpenSessionMenuItems()
    {
        var sessions = EnumerateOpenSessions();
        var items = new List<MenuItem>(sessions.Count);

        for (int i = 0; i < sessions.Count; i++)
        {
            var entry = sessions[i];

            // The suffix says what pressing the row will DO - focus another window rather than
            // switch tabs here. That is information, not the separate section Ronny turned down.
            string header = entry.IsPoppedOut
                ? entry.Tab.Title + "   (separate window)"
                : entry.Tab.Title;

            var item = new MenuItem
            {
                Header = header,
                Tag = entry.Tab.Id,
            };

            // A dot on the session you are already looking at. A popped-out window is never the
            // active TAB, so it never gets one.
            if (!entry.IsPoppedOut && ReferenceEquals(entry.Tab, _activeTab))
            {
                item.Icon = new TextBlock
                {
                    Text = "●",
                    FontSize = 9,
                    VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                };
            }

            var captured = entry;
            item.Click += (_, _) => ActivateOpenSession(captured);
            items.Add(item);
        }

        return items;
    }

    /// <summary>
    /// Shows the session, wherever it lives.
    /// </summary>
    /// <param name="entry">
    /// The session to show.
    /// </param>
    internal void ActivateOpenSession(OpenSessionEntry entry)
    {
        if (entry.IsPoppedOut)
        {
            var popout = entry.Popout!;
            if (!popout.IsVisible)
            {
                popout.Show();
            }

            popout.Activate();
            return;
        }

        SelectTab(entry.Tab);
        ScrollActiveTabIntoView();
    }

    private void OnTabListClick(object? sender, RoutedEventArgs e)
    {
        var button = sender as Button ?? this.FindControl<Button>("TabListButton");
        if (button == null)
        {
            return;
        }

        var flyout = new MenuFlyout();
        var items = BuildOpenSessionMenuItems();
        for (int i = 0; i < items.Count; i++)
        {
            flyout.Items.Add(items[i]);
        }

        flyout.ShowAt(button);
    }
}
