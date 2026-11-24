//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs/RetroTerm
// RetroTerm — Connecting today's machines to yesterday's systems
//

using System;
using System.Collections.Generic;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
using RetroTerm.Desktop.Themes;
using RetroTerm.Desktop.Views;
using Xunit;

namespace RetroTerm.Tests.Avalonia;

/// <summary>
/// Every one-line text box in the Preferences window has room for a whole line of its text:
/// the descenders of y, g, p and q are not cut off.
/// </summary>
/// <remarks>
/// <para><b>What cut them off (24 November 2026)</b></para>
/// Ten controls had a fixed Height of 30. For a TextBox, Fluent's MinHeight of 32 won over it, and
/// DarkTheme.axaml's Padding of 8 on all four sides plus the border of 1 left 32 - 16 - 2 = 14
/// pixels for the text; one line of 12 pixel text asks for 16 (measured here), so the text area
/// clipped the bottom two pixels: the tail of the y in a path and the foot of every backslash.
/// The TextBox inside each NumericUpDown has less padding and measured a text area of 19, so the
/// numbers were never cut, and the NumericUpDowns and ComboBoxes rendered the same with and
/// without their Height of 30.
/// <para><b>What is measured</b></para>
/// The height the box's text area was given (the ScrollContentPresenter inside the template,
/// which is what clips) against the height the text presenter asked for (one line of the box's
/// font). Asked-for taller than given is the cut-off. The rendered picture of each tab is saved
/// to images\rendered\preferences-tab-*.png to be looked at.
/// </remarks>
[Collection("Avalonia")]
public sealed class TextBoxTextIsNotCutOffTests : IDisposable
{
    private readonly string _prefsFile;

    public TextBoxTextIsNotCutOffTests()
    {
        _prefsFile = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
            "retroterm-prefs-" + Guid.NewGuid().ToString("N") + ".txt");
        ThemeManager.PreferencesPathForTesting = _prefsFile;
    }

    public void Dispose()
    {
        ThemeManager.PreferencesPathForTesting = null;
        if (System.IO.File.Exists(_prefsFile)) System.IO.File.Delete(_prefsFile);
    }

    private static TabControl Tabs(Window window)
    {
        var descendants = window.GetLogicalDescendants();
        var e = descendants.GetEnumerator();
        while (e.MoveNext())
        {
            if (e.Current is TabControl tabs) return tabs;
        }
        throw new InvalidOperationException("the Preferences window has no TabControl");
    }

    /// <summary>
    /// The first visual of a type inside another.
    /// </summary>
    private static T? Find<T>(Visual root) where T : Visual
    {
        var e = root.GetVisualDescendants().GetEnumerator();
        while (e.MoveNext())
        {
            if (e.Current is T found) return found;
        }
        return null;
    }

    /// <summary>
    /// Every shown one-line TextBox in the window, including those inside a NumericUpDown.
    /// </summary>
    private static List<TextBox> ShownSingleLineBoxes(Window window)
    {
        var boxes = new List<TextBox>();
        var e = window.GetVisualDescendants().GetEnumerator();
        while (e.MoveNext())
        {
            if (e.Current is TextBox box && box.IsEffectivelyVisible && !box.AcceptsReturn
                && box.TextWrapping == global::Avalonia.Media.TextWrapping.NoWrap)
            {
                boxes.Add(box);
            }
        }
        return boxes;
    }

    private static string NameOf(TextBox box)
    {
        if (!string.IsNullOrEmpty(box.Name) && box.Name != "PART_TextBox") return box.Name!;
        var parent = box.FindAncestorOfType<NumericUpDown>();
        return parent?.Name != null ? parent.Name + " (its text box)" : "(unnamed TextBox)";
    }

    [AvaloniaFact]
    public void EveryPreferencesTextBoxHasRoomForAWholeLine()
    {
        var window = new PreferencesWindow();
        var cut = new StringBuilder();
        int measured = 0;
        try
        {
            window.Show();
            var tabs = Tabs(window);
            for (int t = 0; t < tabs.ItemCount; t++)
            {
                tabs.SelectedIndex = t;
                Dispatcher.UIThread.RunJobs();

                var header = (tabs.Items[t] as TabItem)?.Header as string ?? ("tab" + t);
                var shot = RenderedScreenshot.CaptureWindow(window, "preferences-tab-" + header.ToLowerInvariant().Replace(' ', '-'));
                shot?.Dispose();

                var boxes = ShownSingleLineBoxes(window);
                for (int i = 0; i < boxes.Count; i++)
                {
                    var box = boxes[i];
                    var textArea = Find<ScrollContentPresenter>(box);
                    var text = Find<TextPresenter>(box);
                    Assert.True(textArea != null && text != null, NameOf(box) + " has no text area in its template");

                    measured++;
                    double given = textArea!.Bounds.Height;
                    double asked = text!.DesiredSize.Height;
                    if (asked > given + 0.01)
                    {
                        cut.Append($"\n  {header}: {NameOf(box)} - box {box.Bounds.Height:0.##} high, text area {given:0.##}, "
                            + $"one line of its text needs {asked:0.##}");
                    }
                }
            }
        }
        finally
        {
            window.Close();
        }

        Assert.True(measured > 0, "no text box was measured");
        Assert.True(cut.Length == 0, "these boxes cut the bottom off their text:" + cut);
    }
}
