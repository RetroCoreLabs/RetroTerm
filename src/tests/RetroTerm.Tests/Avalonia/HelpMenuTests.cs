//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs/RetroTerm
// RetroTerm — Connecting today's machines to yesterday's systems
//

using System;
using System.IO;
using System.Threading.Tasks;

using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;

using RetroTerm.Desktop;

using Xunit;

namespace RetroTerm.Tests.Avalonia;

/// <summary>
/// Help menu: the 3270 introduction opens on the docs site (Ronny, 23 November 2026).
/// </summary>
[Collection("Avalonia")]
public class HelpMenuTests
{
    [AvaloniaFact]
    public async Task TheIntroductionItemOpensTheDocsSitePage()
    {
        var window = new MainWindow();
        Uri? opened = null;
        window.OpenWebPageForTesting = uri =>
        {
            opened = uri;
            return Task.CompletedTask;
        };

        MenuItem? item = window.FindControl<MenuItem>("Ibm3270IntroductionMenuItem");
        Assert.NotNull(item);
        item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        await Task.Yield();

        Assert.Equal(new Uri(MainWindow.Ibm3270IntroductionUrl), opened);
        window.Close();
    }

    [AvaloniaFact]
    public void TheAddressIsTheIntroductionDocumentAsDocfxPublishesIt()
    {
        // DocFX writes docs\X.md to docs/X.html under the site root; the document must exist
        // and be in the site's file list, or the link goes nowhere.
        string root = RepoRoot();
        Assert.True(File.Exists(Path.Combine(root, "docs", "IBM3270-INTRODUCTION.md")));
        string docfx = File.ReadAllText(Path.Combine(root, "src", "docfx", "docfx.json"));
        Assert.Contains("\"docs/IBM3270-INTRODUCTION.md\"", docfx, StringComparison.Ordinal);
        Assert.EndsWith("/RetroTerm/docs/IBM3270-INTRODUCTION.html", MainWindow.Ibm3270IntroductionUrl, StringComparison.Ordinal);
    }

    private static string RepoRoot()
    {
        string? dir = AppContext.BaseDirectory;
        while (dir != null && !File.Exists(Path.Combine(dir, "src", "RetroTerm.slnx")))
        {
            dir = Path.GetDirectoryName(dir);
        }

        Assert.NotNull(dir);
        return dir;
    }
}
