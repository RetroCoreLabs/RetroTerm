using System;
using System.Collections.Generic;
using Avalonia.Controls;
using RetroTerm.Core.Session;

namespace RetroTerm.Desktop.Views;

/// <summary>
/// Picks which open connection (tab) script commands run against — used by the
/// Script Editor and the Script Console. The first choice, "Active tab", follows
/// whatever tab has focus in the main window; picking a specific tab PINS the
/// target so the commands keep hitting that connection no matter which tab the
/// user looks at. The item list refreshes every time the dropdown opens (tabs
/// come and go), and a pinned tab that was closed falls back to the active tab.
/// </summary>
public sealed class SessionTargetSelector : ComboBox
{
    /// <summary>
    /// One dropdown row: a specific session, or null = follow the active tab.
    /// </summary>
    private sealed class Choice
    {
        public TerminalSession? Session;
        public string Label = string.Empty;
        public override string ToString() => Label;
    }

    // A derived control styles by ITS OWN type by default — without this override
    // the Fluent ComboBox template never applies and the control renders NOTHING
    // (it still gets layout size, so only a template-applied check catches it).
    // This was why "Run on:" showed empty space (2026-08-05).
    protected override Type StyleKeyOverride => typeof(ComboBox);

    private readonly Func<TerminalSession?> _activeSession;
    private readonly Func<IReadOnlyList<TerminalSession>> _openSessions;
    private TerminalSession? _pinned;
    private bool _refreshing;

    /// <summary>
    /// Raised when the user picks a different target.
    /// </summary>
    public event Action? TargetChanged;

    public SessionTargetSelector(Func<TerminalSession?> activeSession,
        Func<IReadOnlyList<TerminalSession>> openSessions)
    {
        _activeSession = activeSession ?? throw new ArgumentNullException(nameof(activeSession));
        _openSessions = openSessions ?? throw new ArgumentNullException(nameof(openSessions));

        Width = 220;
        MinHeight = 26;
        FontSize = 12;
        VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Center;

        RefreshItems();

        // The initial selection happens before the control has a TEMPLATE — the
        // selection box can then stay empty forever ("Run on:" showed nothing,
        // 2026-08-05). Re-assert the selection once attached so the box renders it.
        AttachedToVisualTree += (_, _) =>
        {
            if (SelectedIndex >= 0)
            {
                int index = SelectedIndex;
                _refreshing = true;
                SelectedIndex = -1;
                SelectedIndex = index;
                _refreshing = false;
            }
        };

        DropDownOpened += (_, _) => RefreshItems();
        SelectionChanged += (_, _) =>
        {
            if (_refreshing) return; // Items.Clear() during refresh fires this too
            _pinned = (SelectedItem as Choice)?.Session;
            TargetChanged?.Invoke();
        };
    }

    private static string LabelFor(TerminalSession s)
        => s.Title + (s.IsConnected ? "" : " (disconnected)");

    private void RefreshItems()
    {
        // Rebuilding the items WHILE the dropdown is opening kills the popup and
        // blanks the selection (Items.Clear closes it) — the picker looked empty
        // and never dropped down (2026-08-05). Only rebuild when the tab list
        // actually changed; the unchanged case (the common one) touches nothing.
        var sessions = _openSessions();
        if (!ItemsOutOfDate(sessions))
        {
            return;
        }

        _refreshing = true;
        try
        {
            var pinned = _pinned;
            Items.Clear();
            Items.Add(new Choice { Session = null, Label = "Active tab" });

            int selectIndex = 0;
            for (int i = 0; i < sessions.Count; i++)
            {
                var s = sessions[i];
                Items.Add(new Choice { Session = s, Label = LabelFor(s) });
                if (ReferenceEquals(s, pinned))
                {
                    selectIndex = i + 1;
                }
            }
            SelectedIndex = selectIndex;
            // The pinned tab may have been closed since — selection fell back to "Active tab".
            _pinned = selectIndex > 0 ? pinned : null;
        }
        finally
        {
            _refreshing = false;
        }
    }

    /// <summary>
    /// True when the dropdown rows no longer mirror the open-session list.
    /// </summary>
    private bool ItemsOutOfDate(IReadOnlyList<TerminalSession> sessions)
    {
        if (ItemCount != sessions.Count + 1)
        {
            return true;
        }
        for (int i = 0; i < sessions.Count; i++)
        {
            if (Items[i + 1] is not Choice choice
                || !ReferenceEquals(choice.Session, sessions[i])
                || !string.Equals(choice.Label, LabelFor(sessions[i]), StringComparison.Ordinal))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// The session commands should run on RIGHT NOW: the pinned tab if it is still
    /// open, otherwise the active tab (null when there are no tabs at all).
    /// </summary>
    public TerminalSession? ResolveSession()
    {
        var pinned = _pinned;
        if (pinned != null)
        {
            var sessions = _openSessions();
            for (int i = 0; i < sessions.Count; i++)
            {
                if (ReferenceEquals(sessions[i], pinned))
                {
                    return pinned;
                }
            }
            _pinned = null; // tab was closed — stop pinning silently-dead targets
        }
        return _activeSession();
    }

    /// <summary>
    /// Human-readable name of the current target, for window titles / status strips.
    /// </summary>
    public string DescribeTarget()
    {
        var session = ResolveSession();
        if (session == null)
        {
            return "no active tab";
        }
        var name = session.Title + (session.IsConnected ? "" : " (disconnected)");
        return _pinned != null ? name : $"{name} (active tab)";
    }
}
