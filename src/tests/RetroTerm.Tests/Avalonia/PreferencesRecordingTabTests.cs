//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs/RetroTerm
// RetroTerm — Connecting today's machines to yesterday's systems
//

using System;
using System.IO;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using RetroTerm.Desktop.Themes;
using RetroTerm.Desktop.Views;
using Xunit;

namespace RetroTerm.Tests.Avalonia;

/// <summary>
/// Preferences, Recording tab: the ffmpeg path RECORDSTOP makes its MP4 with.
/// </summary>
/// <remarks>
/// Every test here points ThemeManager at a preferences file of its own
/// (<see cref="ThemeManager.PreferencesPathForTesting"/>), so typing into the box never writes
/// the user's real preferences.txt. The rendered tab is saved as
/// images\rendered\preferences-recording.png to be looked at.
/// </remarks>
[Collection("Avalonia")]
public sealed class PreferencesRecordingTabTests : IDisposable
{
    private readonly string _prefsFile;

    // Example paths only; nothing is run. Built from the temp folder so no machine's own path is
    // written into the repository.
    private static readonly string SavedPath = Path.Combine(Path.GetTempPath(), "tools", "ffmpeg.exe");
    private static readonly string TypedPath = Path.Combine(Path.GetTempPath(), "video tools", "ffmpeg.exe");
    private readonly string _ffmpegBefore;

    public PreferencesRecordingTabTests()
    {
        _prefsFile = Path.Combine(Path.GetTempPath(), "retroterm-prefs-" + Guid.NewGuid().ToString("N") + ".txt");
        ThemeManager.PreferencesPathForTesting = _prefsFile;
        _ffmpegBefore = ThemeManager.Instance.FfmpegPath;
    }

    public void Dispose()
    {
        ThemeManager.Instance.FfmpegPath = _ffmpegBefore;
        ThemeManager.PreferencesPathForTesting = null;
        if (File.Exists(_prefsFile)) File.Delete(_prefsFile);
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

    private static int RecordingTabIndex(TabControl tabs)
    {
        for (int i = 0; i < tabs.ItemCount; i++)
        {
            if (tabs.Items[i] is TabItem item && (item.Header as string) == "Recording") return i;
        }
        throw new InvalidOperationException("the Preferences window has no Recording tab");
    }

    [AvaloniaFact]
    public void TheBoxShowsTheSavedPathAndATypedOneIsSavedAndReadBack()
    {
        ThemeManager.Instance.FfmpegPath = SavedPath;
        var window = new PreferencesWindow();
        try
        {
            window.Show();
            var tabs = Tabs(window);
            tabs.SelectedIndex = RecordingTabIndex(tabs);
            Dispatcher.UIThread.RunJobs();

            var box = window.FindControl<TextBox>("FfmpegPathBox")!;
            Assert.Equal(SavedPath, box.Text);

            // A programmatic set, as Browse does - TextChanged would not see it.
            box.Text = "  " + TypedPath + " ";
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(TypedPath, ThemeManager.Instance.FfmpegPath);
            Assert.Contains("ffmpeg-path=" + TypedPath, File.ReadAllText(_prefsFile), StringComparison.Ordinal);

            ThemeManager.Instance.FfmpegPath = "something else";
            ThemeManager.Instance.LoadPreferencesForTesting();
            Assert.Equal(TypedPath, ThemeManager.Instance.FfmpegPath);

            // Emptied: the PATH's ffmpeg, saved as empty.
            box.Text = "";
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(string.Empty, ThemeManager.Instance.FfmpegPath);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void TheRecordingTabRendersWithItsBoxAndHint()
    {
        ThemeManager.Instance.FfmpegPath = TypedPath;
        var window = new PreferencesWindow();
        try
        {
            window.Show();
            var tabs = Tabs(window);
            tabs.SelectedIndex = RecordingTabIndex(tabs);
            Dispatcher.UIThread.RunJobs();

            var box = window.FindControl<TextBox>("FfmpegPathBox")!;
            Assert.True(box.IsEffectivelyVisible);
            Assert.True(box.Bounds.Width > 150, $"the ffmpeg box is only {box.Bounds.Width} wide");
            // The cut-off bottom of y and g that a fixed Height of 30 caused is pinned for every
            // Preferences box by TextBoxTextIsNotCutOffTests: it compares the height the text area
            // was given with the height one line of text asks for. Look at the picture as well.
            var hint = window.FindControl<TextBlock>("FfmpegInfoText")!;
            Assert.True(hint.IsEffectivelyVisible);
            Assert.Contains("PATH", hint.Text, StringComparison.Ordinal);

            var shot = RenderedScreenshot.CaptureWindow(window, "preferences-recording");
            Assert.NotNull(shot);
            using (shot!)
            {
                Assert.True(shot.Width > 300 && shot.Height > 200);
            }
        }
        finally
        {
            window.Close();
        }
    }
}
