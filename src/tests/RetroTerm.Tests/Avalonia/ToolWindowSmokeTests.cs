using System;
using System.IO;
using Avalonia.Headless.XUnit;
using RetroTerm.Core.Commands;
using RetroTerm.Core.Commands.Builtin;
using RetroTerm.Core.Scripting;
using RetroTerm.Core.Session;
using RetroTerm.Desktop.Views;
using Xunit;

namespace RetroTerm.Tests.Avalonia;

/// <summary>
/// Headless smoke tests: the tool windows must CONSTRUCT and SHOW without throwing.
/// This exists because a bad style selector (x.Nesting() at the top level of a
/// control's Styles) crashed the Script Editor the moment the menu item was clicked
/// (2026-08-05) — a class of bug the rest of the suite never executes, since these
/// windows are only built in the running app.
/// </summary>
[Collection("Avalonia")]
public class ToolWindowSmokeTests
{
    private static CommandRegistry MakeRegistry()
    {
        var registry = new CommandRegistry();
        BuiltinCommands.RegisterAll(registry);
        return registry;
    }

    private static ScriptLibrary MakeLibrary()
    {
        var dir = Path.Combine(Path.GetTempPath(), "retroterm-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return new ScriptLibrary(dir);
    }

    [AvaloniaFact]
    public void ScriptEditorWindow_ConstructsAndShows()
    {
        var window = new ScriptEditorWindow(MakeLibrary(), MakeRegistry(),
            () => null, () => Array.Empty<TerminalSession>());
        window.Show();
        window.Close();
    }

    [AvaloniaFact]
    public void ScriptConsoleWindow_ConstructsAndShows()
    {
        var window = new ScriptConsoleWindow(() => null, () => Array.Empty<TerminalSession>(),
            MakeRegistry(), MakeLibrary());
        window.Show();
        window.Close();
    }

    /// <summary>
    /// The "Run on" connection picker must be VISIBLE with real size in both windows
    /// — not just constructed. Guards the repeatedly-requested target selector.
    /// </summary>
    [AvaloniaFact]
    public void ScriptEditorWindow_RunOnPicker_IsVisibleWithItems()
    {
        var window = new ScriptEditorWindow(MakeLibrary(), MakeRegistry(),
            () => null, () => Array.Empty<TerminalSession>());
        window.Show();

        window.UpdateLayout(); // template application happens during layout

        var picker = FindDescendant<SessionTargetSelector>(window);
        Assert.NotNull(picker);
        Assert.True(picker!.ItemCount >= 1, "picker has no items — 'Active tab' must always be there");
        Assert.Equal(0, picker.SelectedIndex);
        Assert.True(picker.IsVisible, "picker is not visible");
        Assert.True(picker.Bounds.Width > 0 && picker.Bounds.Height > 0,
            $"picker has no size after layout: {picker.Bounds}");
        // A derived control does NOT get the base ComboBox template unless it
        // overrides StyleKey — without it the picker had SIZE but rendered NOTHING
        // ("Run on:" showed empty space, 2026-08-05). Template applied = visual children.
        Assert.True(HasVisualChildren(picker),
            "picker has no visual children — the ComboBox template was not applied (StyleKeyOverride missing?)");
        window.Close();
    }

    [AvaloniaFact]
    public void Diagnostic_TemplatesApplyInHeadless()
    {
        var window = new ScriptEditorWindow(MakeLibrary(), MakeRegistry(),
            () => null, () => Array.Empty<TerminalSession>());
        window.Show();
        window.UpdateLayout();
        var editorHasTemplate = HasVisualChildren(window.EditorForTests);
        var listHasTemplate = HasVisualChildren(window.ScriptListForTests);
        window.Close();
        Assert.True(editorHasTemplate, "plain TextBox has no visual children — headless applies no templates at all");
        Assert.True(listHasTemplate, "plain ListBox has no visual children");
    }

    private static bool HasVisualChildren(global::Avalonia.Visual visual)
    {
        var e = global::Avalonia.VisualTree.VisualExtensions.GetVisualChildren(visual).GetEnumerator();
        return e.MoveNext();
    }

    [AvaloniaFact]
    public void ScriptConsoleWindow_RunOnPicker_IsVisibleWithItems()
    {
        var window = new ScriptConsoleWindow(() => null, () => Array.Empty<TerminalSession>(),
            MakeRegistry(), MakeLibrary());
        window.Show();

        var picker = FindDescendant<SessionTargetSelector>(window);
        Assert.NotNull(picker);
        Assert.True(picker!.ItemCount >= 1, "picker has no items — 'Active tab' must always be there");
        Assert.True(picker.IsVisible, "picker is not visible");
        Assert.True(picker.Bounds.Width > 0 && picker.Bounds.Height > 0,
            $"picker has no size after layout: {picker.Bounds}");
        window.Close();
    }

    /// <summary>
    /// The picker's SELECTION BOX must actually render its selection — a selection
    /// made before the template exists left the box looking empty ("Run on:" showed
    /// nothing at all, 2026-08-05).
    /// </summary>
    [AvaloniaFact]
    public void RunOnPicker_SelectionBox_ShowsTheSelection()
    {
        var window = new ScriptEditorWindow(MakeLibrary(), MakeRegistry(),
            () => null, () => Array.Empty<TerminalSession>());
        window.Show();

        var picker = FindDescendant<SessionTargetSelector>(window);
        Assert.NotNull(picker);
        Assert.NotNull(picker!.SelectedItem);
        Assert.NotNull(picker.SelectionBoxItem); // what the closed box RENDERS
        window.Close();
    }

    /// <summary>
    /// Opening the dropdown must not blank it: the old refresh cleared Items while
    /// the popup opened, which killed the popup and emptied the selection.
    /// </summary>
    [AvaloniaFact]
    public void RunOnPicker_OpeningDropdown_KeepsItemsAndSelection()
    {
        var window = new ScriptEditorWindow(MakeLibrary(), MakeRegistry(),
            () => null, () => Array.Empty<TerminalSession>());
        window.Show();

        var picker = FindDescendant<SessionTargetSelector>(window);
        Assert.NotNull(picker);

        picker!.IsDropDownOpen = true; // fires DropDownOpened → refresh path
        Assert.True(picker.IsDropDownOpen, "dropdown closed itself on open");
        Assert.True(picker.ItemCount >= 1, "items vanished on open");
        Assert.NotNull(picker.SelectedItem);
        picker.IsDropDownOpen = false;
        window.Close();
    }

    /// <summary>
    /// The syntax overlay must mirror the editor text EXACTLY, line for line —
    /// with CRLF text a stray \r inside a Run can render as an extra break and the
    /// visible text drifts from the caret/status line (2026-08-05).
    /// </summary>
    [AvaloniaFact]
    public void SyntaxOverlay_CrLfText_StaysLineAligned()
    {
        var window = new ScriptEditorWindow(MakeLibrary(), MakeRegistry(),
            () => null, () => Array.Empty<TerminalSession>());
        window.Show();

        var text = "# test1\r\n\r\nSEND \"a\\r\"\r\nWAITFOR \"@\" timeout=30000";
        window.EditorForTests.Text = text;
        window.UpdateHighlightForTests();

        var sb = new System.Text.StringBuilder();
        var inlines = window.HighlightForTests.Inlines!;
        for (int i = 0; i < inlines.Count; i++)
        {
            if (inlines[i] is global::Avalonia.Controls.Documents.Run run)
            {
                sb.Append(run.Text);
            }
        }
        var rendered = sb.ToString();

        Assert.DoesNotContain('\r', rendered);                       // no stray CRs in runs
        Assert.Equal(text.Replace("\r", ""), rendered);              // same text, same breaks
        window.Close();
    }

    /// <summary>
    /// The RUN TRANSCRIPT panel must stay inside the editor column — it visually
    /// bled into the script list area (2026-08-05 screenshot).
    /// </summary>
    [AvaloniaFact]
    public void TranscriptPanel_StaysRightOfScriptList()
    {
        var window = new ScriptEditorWindow(MakeLibrary(), MakeRegistry(),
            () => null, () => Array.Empty<TerminalSession>());
        window.Show();

        window.TranscriptPanelForTests.IsVisible = true;
        window.UpdateLayout();

        var listTopRight = global::Avalonia.VisualExtensions.TranslatePoint(
            window.ScriptListForTests, new global::Avalonia.Point(window.ScriptListForTests.Bounds.Width, 0), window);
        var panelTopLeft = global::Avalonia.VisualExtensions.TranslatePoint(
            window.TranscriptPanelForTests, new global::Avalonia.Point(0, 0), window);
        Assert.NotNull(listTopRight);
        Assert.NotNull(panelTopLeft);
        Assert.True(panelTopLeft!.Value.X >= listTopRight!.Value.X,
            $"transcript panel (x={panelTopLeft.Value.X}) overlaps the script list (right edge {listTopRight.Value.X})");
        window.Close();
    }

    private static T? FindDescendant<T>(global::Avalonia.Visual root) where T : class
    {
        var children = global::Avalonia.VisualTree.VisualExtensions.GetVisualChildren(root);
        var e = children.GetEnumerator();
        while (e.MoveNext())
        {
            if (e.Current is T match)
            {
                return match;
            }
            var nested = FindDescendant<T>(e.Current);
            if (nested != null)
            {
                return nested;
            }
        }
        return null;
    }

    /// <summary>
    /// The status strip's "line N, col M" must match where the caret ACTUALLY is,
    /// including with CRLF text and after jumping back to the top — the user saw the
    /// caret on line 4 while the status claimed line 5 (2026-08-05). Line breaks are
    /// counted from the real editor text, so CRLF must not inflate the number.
    /// </summary>
    [AvaloniaFact]
    public void CaretStatus_ReportsTheLineTheCaretIsOn_WithCrLf()
    {
        var window = new ScriptEditorWindow(MakeLibrary(), MakeRegistry(),
            () => null, () => Array.Empty<TerminalSession>());
        window.Show();

        // Four lines, CRLF: "L1"(0,1) CRLF "L2"(3,4) CRLF "L3"(6,7) CRLF "L4"(9,10)
        window.EditorForTests.Text = "L1\r\nL2\r\nL3\r\nL4";

        // Caret at the very start of line 3 ("L3"): index 6 (2+2 + 2+2 = 8? recount)
        //   L=0 1=1 \r=2 \n=3 | L=4 2=5 \r=6 \n=7 | L=8 3=9 \r=10 \n=11 | L=12 4=13
        // Start of line 3 = index 8.
        window.EditorForTests.CaretIndex = 8;
        var status = window.UpdateAndReadCaretStatusForTests();
        Assert.StartsWith("line 3, col 1", status); // NOT "line 5" — CRLF must not double-count

        // Jump to the very top (Ctrl+Home equivalent): caret 0 → line 1, col 1.
        window.EditorForTests.CaretIndex = 0;
        var top = window.UpdateAndReadCaretStatusForTests();
        Assert.StartsWith("line 1, col 1", top);

        // Middle of line 4, third char position: index 12 = "L", 13 = "4".
        window.EditorForTests.CaretIndex = 13;
        var last = window.UpdateAndReadCaretStatusForTests();
        Assert.StartsWith("line 4, col 2", last);

        window.Close();
    }

    [AvaloniaFact]
    public void McpLogWindow_ConstructsAndShows()
    {
        var window = new McpLogWindow("http://127.0.0.1:5715/mcp");
        window.Show();
        window.Close();
    }
}
