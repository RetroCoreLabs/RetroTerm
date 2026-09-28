using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using RetroTerm.Mcp;

namespace RetroTerm.Desktop.Views;

/// <summary>
/// Live view of MCP traffic (View → MCP Log): every tool call an LLM makes and every
/// result going back, with timing. Fed by McpTrafficLog; entries arrive on Kestrel
/// threads and are marshalled to the UI thread here.
///
/// All colors come from the live theme resources (GetResourceObservable) so a theme
/// switch restyles the open window — same pattern as ScriptEditorWindow.BindTheme.
/// </summary>
public sealed class McpLogWindow : Window
{
    private readonly TextBox _output;
    private readonly TextBlock _header;
    private readonly CheckBox _tailToggle;
    private readonly Action<McpLogEntry> _onEntry;

    // Shown in the header so the user can see at a glance where the server listens
    // (null = MCP disabled or failed to bind).
    private readonly string? _serverUrl;
    private int _entryCount;

    public McpLogWindow(string? serverUrl)
    {
        _serverUrl = serverUrl;

        Title = "MCP Log";
        Width = 900;
        Height = 500;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        BindTheme(this, BackgroundProperty, "WindowBackgroundBrush");

        // ── header strip: endpoint + entry count ─────────────────────────────
        _header = new TextBlock
        {
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center
        };
        BindTheme(_header, TextBlock.ForegroundProperty, "SecondaryTextBrush");

        var headerBorder = new Border
        {
            Child = _header,
            Padding = new Thickness(10, 6, 10, 6),
            BorderThickness = new Thickness(0, 0, 0, 1)
        };
        BindTheme(headerBorder, Border.BackgroundProperty, "PanelBackgroundBrush");
        BindTheme(headerBorder, Border.BorderBrushProperty, "BorderBrush");

        // ── the log itself ───────────────────────────────────────────────────
        _output = new TextBox
        {
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.NoWrap,
            FontFamily = new FontFamily("Cascadia Mono,Consolas,monospace"),
            FontSize = 12
        };
        BindTheme(_output, BackgroundProperty, "SectionBackgroundBrush");
        BindTheme(_output, ForegroundProperty, "PrimaryTextBrush");
        // No color flip when the log is clicked/focused.
        TextBoxChrome.Freeze(_output, this, "SectionBackgroundBrush");

        // ── bottom strip: tail toggle, word wrap, Clear (secondary button) ───
        // Tail is a TOGGLE because a forced caret-to-end fights the user's manual
        // scrolling while reading old entries (UX review).
        _tailToggle = new CheckBox
        {
            Content = "Tail (jump to newest)",
            IsChecked = true,
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center
        };
        BindTheme(_tailToggle, ForegroundProperty, "PrimaryTextBrush");

        var wrapToggle = new CheckBox
        {
            Content = "Word wrap",
            IsChecked = false,
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(16, 0, 0, 0)
        };
        BindTheme(wrapToggle, ForegroundProperty, "PrimaryTextBrush");
        wrapToggle.IsCheckedChanged += (_, _) =>
        {
            _output.TextWrapping = wrapToggle.IsChecked == true ? TextWrapping.Wrap : TextWrapping.NoWrap;
        };

        // Clear is a secondary action, not the accent color — accent implies "primary
        // thing you came here to do", which for a log viewer is reading, not clearing.
        // The Secondary class, not brushes bound onto the Button: those are lost under the mouse
        // (see the note above the Button styles in DarkTheme.axaml).
        var clearButton = new Button
        {
            Content = "Clear",
            Classes = { "Secondary" },
            Padding = new Thickness(14, 5, 14, 5),
            FontSize = 12,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        clearButton.Click += (_, _) =>
        {
            McpTrafficLog.Instance.Clear();
            _output.Text = string.Empty;
            _entryCount = 0;
            UpdateHeader();
        };

        var bottomStrip = new DockPanel { Margin = new Thickness(0, 8, 0, 0) };
        DockPanel.SetDock(clearButton, Dock.Right);
        bottomStrip.Children.Add(clearButton);
        var toggles = new StackPanel { Orientation = Orientation.Horizontal };
        toggles.Children.Add(_tailToggle);
        toggles.Children.Add(wrapToggle);
        bottomStrip.Children.Add(toggles);

        var panel = new DockPanel { Margin = new Thickness(12) };
        DockPanel.SetDock(headerBorder, Dock.Top);
        DockPanel.SetDock(bottomStrip, Dock.Bottom);
        panel.Children.Add(headerBorder);
        panel.Children.Add(bottomStrip);
        panel.Children.Add(_output);
        Content = panel;

        // Show what already happened, then tail new entries.
        var snapshot = McpTrafficLog.Instance.GetSnapshot();
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < snapshot.Count; i++)
        {
            sb.Append(snapshot[i]).Append('\n');
        }
        _output.Text = sb.ToString();
        _entryCount = snapshot.Count;
        UpdateHeader();

        _onEntry = entry => Dispatcher.UIThread.Post(() => AppendEntry(entry));
        McpTrafficLog.Instance.EntryAdded += _onEntry;
        Closed += (_, _) => McpTrafficLog.Instance.EntryAdded -= _onEntry;
    }

    private void UpdateHeader()
    {
        var endpoint = _serverUrl ?? "server not running";
        _header.Text = $"MCP traffic — {endpoint} — {_entryCount} entries";
    }

    private void AppendEntry(McpLogEntry entry)
    {
        _output.Text += entry + "\n";
        _entryCount++;
        UpdateHeader();
        if (_tailToggle.IsChecked == true)
        {
            _output.CaretIndex = _output.Text?.Length ?? 0; // keep tailing
        }
    }

    /// <summary>
    /// Live theme binding — restyles on theme switch (same as ScriptEditorWindow).
    /// </summary>
    private void BindTheme(AvaloniaObject target, AvaloniaProperty property, string key)
    {
        target.Bind(property, this.GetResourceObservable(key));
    }
}
