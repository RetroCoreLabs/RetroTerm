//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs/RetroTerm
// RetroTerm — Connecting today's machines to yesterday's systems
//

using System.Collections.Generic;

using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;

using RetroTerm.Desktop.Views;
using RetroTerm.Ibm3270;

using Xunit;

namespace RetroTerm.Tests.Avalonia;

/// <summary>
/// Keyboard > Key Reference on a 3270 tab: the PC keys that press 3270 keys, read from the
/// emulator's own mapping, on one tab, with the TDV tabs hidden.
/// </summary>
[Collection("Avalonia")]
public class Ibm3270KeyReferenceTests
{
    private static string? Find(List<(string PcKey, string Key3270, string Description)> rows, string pcKey)
    {
        for (int i = 0; i < rows.Count; i++)
        {
            if (rows[i].PcKey == pcKey)
            {
                return rows[i].Key3270;
            }
        }

        return null;
    }

    [AvaloniaFact]
    public void ThePageListsTheKeysThePcKeyboardReallyPresses()
    {
        var rows = KeyboardReferenceWindow.Ibm3270KeyRows();

        Assert.Equal("ENTER", Find(rows, "Enter"));
        Assert.Equal("PF1", Find(rows, "F1"));
        Assert.Equal("PF13", Find(rows, "Shift+F1"));
        Assert.Equal("PF24", Find(rows, "Shift+F12"));
        Assert.Equal("PA1", Find(rows, "Alt+1"));
        Assert.Equal("CLEAR", Find(rows, "Alt+C"));
        Assert.Equal("BACKTAB", Find(rows, "Shift+Tab"));
        Assert.Equal("PF7", Find(rows, "PageUp"));

        // Every row is what the emulator does with that key, and the page says whether it goes to
        // the host.
        for (int i = 0; i < rows.Count; i++)
        {

            Assert.Equal(Ibm3270Emulator.IsAttentionKey(rows[i].Key3270), rows[i].Description == "Sends to the host");
        }

        // Enter first, as in the emulator's key list; one row per PC key.
        Assert.Equal("ENTER", rows[0].Key3270);
        var seen = new HashSet<string>();
        for (int i = 0; i < rows.Count; i++)
        {
            Assert.True(seen.Add(rows[i].PcKey), "listed twice: " + rows[i].PcKey);
        }
    }

    [AvaloniaFact]
    public void OnA3270TabOnlyThe3270PageShows()
    {
        var window = new KeyboardReferenceWindow("IBM-3278-4");
        window.Show();

        var tabs = new List<ILogical>(window.GetLogicalDescendants());
        int shown = 0;
        string? header = null;
        for (int i = 0; i < tabs.Count; i++)
        {
            if (tabs[i] is TabItem tab && tab.IsVisible)
            {
                shown++;
                header = tab.Header as string;
            }
        }

        Assert.Equal(1, shown);
        Assert.Equal("IBM 3270", header);

        // A picture of the page, to look at (images\rendered, as the other rendered tests do).
        using (var frame = global::Avalonia.Headless.HeadlessWindowExtensions.CaptureRenderedFrame(window))
        {
            string folder = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(typeof(Ibm3270KeyReferenceTests).Assembly.Location)!,
                "..", "..", "..", "Avalonia", "images", "rendered");
            System.IO.Directory.CreateDirectory(folder);
            using var file = System.IO.File.Create(System.IO.Path.Combine(folder, "keyreference-ibm3270.png"));
            frame!.Save(file, global::Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
        }

        window.Close();
    }
}
