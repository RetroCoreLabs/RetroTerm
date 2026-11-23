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
using Avalonia.VisualTree;
using RetroIO.Protocols.Tn3270;
using RetroTerm.Core.Configuration;
using RetroTerm.Desktop.ViewModels;
using RetroTerm.Desktop.Views;
using Xunit;

namespace RetroTerm.Tests.Avalonia;

/// <summary>
/// The 3270 code page on the Keyboard tab of Manage Connections: loaded from the saved
/// connection, marks it changed, and saved back.
/// </summary>
[Collection("Avalonia")]
public class CodePageConnectionSettingTests
{
    [AvaloniaFact]
    public void ASavedCodePageLoadsAndIsSavedBack()
    {
        var profile = new ConnectionProfileViewModel();
        profile.LoadFromConfiguration(new HostConfiguration { Name = "tk5", Protocol = "TN3270", Host = "localhost", Port = 3270, CodePage = 277 });
        profile.MarkClean();

        Assert.Equal(277, profile.CodePage);
        Assert.False(profile.IsDirty);

        profile.CodePage = 37;

        Assert.True(profile.IsDirty);
        Assert.Equal(37, profile.ToHostConfiguration().CodePage);
    }

    [AvaloniaFact]
    public void NoCodePageStaysNone()
    {
        var profile = new ConnectionProfileViewModel();
        profile.LoadFromConfiguration(new HostConfiguration { Name = "tk5", Protocol = "TN3270", Host = "localhost", Port = 3270 });

        Assert.Null(profile.CodePage);
        Assert.Null(profile.ToHostConfiguration().CodePage);
    }

    [AvaloniaFact]
    public void TheBoxOffersTheDefaultAndEveryBasePage()
    {
        // A configuration file of its own in %TEMP%, never the user's real one.
        string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "RetroTermCodePage-" + System.Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var window = new ManageConnectionsWindow(new ConfigurationManager(path));
            window.ShowKeyboardTabForTesting();
            ComboBox? box = null;
            var all = new List<ILogical>(window.GetLogicalDescendants());
            for (int i = 0; i < all.Count; i++)
            {
                if (all[i] is ComboBox combo && combo.Name == "CodePageCombo")
                {
                    box = combo;
                }
            }

            Assert.NotNull(box);
            Assert.Equal(CodePage.All.Count + 1, box!.Items.Count);
            Assert.Equal("Terminal's default (037 USA, Canada)", box.Items[0]);
            Assert.Contains(box.Items, item => item as string == "277 Denmark, Norway");

            window.Show();
            using var shot = RenderedScreenshot.CaptureWindow(window, "manage-connections-keyboard-tab");
            Assert.NotNull(shot);

            // The box must be reachable: scrolled into view, it is inside the visible part.
            box.BringIntoView();
            using var scrolled = RenderedScreenshot.CaptureWindow(window, "manage-connections-keyboard-tab-scrolled");
            Assert.NotNull(scrolled);
            global::Avalonia.Point? corner = global::Avalonia.VisualExtensions.TranslatePoint(box, new global::Avalonia.Point(0, box.Bounds.Height), window);
            Assert.NotNull(corner);
            Assert.True(corner!.Value.Y <= window.ClientSize.Height, "the code page box ends at " + corner.Value.Y + ", below the window's " + window.ClientSize.Height);
        }
        finally
        {
            if (System.IO.File.Exists(path))
            {
                System.IO.File.Delete(path);
            }
        }
    }
}
