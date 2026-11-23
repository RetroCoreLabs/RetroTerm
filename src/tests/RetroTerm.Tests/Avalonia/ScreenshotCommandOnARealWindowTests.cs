//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs/RetroTerm
// RetroTerm — Connecting today's machines to yesterday's systems
//

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using RetroTerm.Core.Commands;
using RetroTerm.Core.Commands.Builtin;
using RetroTerm.Desktop;
using RetroTerm.Desktop.Models;
using RetroTerm.Tests.TDV;
using SkiaSharp;
using Xunit;

namespace RetroTerm.Tests.Avalonia;

/// <summary>
/// SCREENSHOT run through the desktop's own command registry on a real, shown main window, for
/// both of its areas.
/// </summary>
/// <remarks>
/// <para><b>What is checked</b></para>
/// area=terminal gives a PNG exactly the size of the tab's terminal control, with real glyph ink
/// where text was written and none on a blank row. area=window gives a PNG exactly the size of
/// the window, and the terminal picture appears inside it at the place the control sits - which
/// proves the window picture holds the same terminal and not a stale or different one.
/// <para><b>What is not</b></para>
/// Both pictures are drawn by the program into an offscreen bitmap. Neither is a capture of the
/// real screen, and nothing here can say that the glass shows the same thing.
/// The PNGs are also copied to the images\rendered folder to be looked at.
/// </remarks>
[Collection("Avalonia")]
public class ScreenshotCommandOnARealWindowTests
{
    /// <summary>
    /// The text written to row one; long enough that its ink is far above any noise.
    /// </summary>
    private const string Text = "HELLO SCREENSHOT 0123456789";

    /// <summary>
    /// Opens a main window with one VT100 tab, connects it to an in-memory line, writes a line of
    /// text and selects the tab.
    /// </summary>
    /// <returns>
    /// The window and the tab.
    /// </returns>
    private static async Task<(MainWindow Main, TabSession Tab)> WindowWithText()
    {
        var main = new MainWindow();
        main.Show();

        var tab = main.AddTabForTesting("shot-host", "VT100");
        var connection = new InMemoryConnection();
        await tab.Session.ConnectAsync(connection, TestContext.Current.CancellationToken);

        // Clear, home, then the text on row one. Row ten stays blank for the no-ink check.
        connection.SimulateReceive(Encoding.ASCII.GetBytes("\u001b[2J\u001b[H" + Text));
        await tab.Session.FlushAsync();

        main.SelectTabForTesting(tab);
        Dispatcher.UIThread.RunJobs();
        return (main, tab);
    }

    /// <summary>
    /// Runs SCREENSHOT through the window's own registry - the one MCP and scripts use.
    /// </summary>
    /// <param name="main">
    /// The window.
    /// </param>
    /// <param name="tab">
    /// The tab whose session the command runs on.
    /// </param>
    /// <param name="area">
    /// The area word, or null to leave it out.
    /// </param>
    /// <param name="path">
    /// Where the PNG goes.
    /// </param>
    /// <returns>
    /// What the command reported.
    /// </returns>
    private static Task<CommandResult> Shoot(MainWindow main, TabSession tab, string? area, string path)
    {
        var values = new Dictionary<string, string> { ["file"] = path, ["settle"] = "0" };
        if (area != null) values["area"] = area;

        return main.GetCommandRegistryForTesting()
            .ExecuteAsync("SCREENSHOT", tab.Session, new CommandArgs(values), CancellationToken.None);
    }

    /// <summary>
    /// A fresh file name in the temp folder.
    /// </summary>
    /// <param name="name">
    /// What the picture is.
    /// </param>
    /// <returns>
    /// The full path.
    /// </returns>
    private static string TempPng(string name)
        => Path.Combine(Path.GetTempPath(), "retroterm-" + name + "-" + Guid.NewGuid().ToString("N") + ".png");

    /// <summary>
    /// Copies a written PNG into the test images folder so it can be looked at.
    /// </summary>
    /// <param name="path">
    /// The PNG.
    /// </param>
    /// <param name="name">
    /// The name to give it there.
    /// </param>
    private static void KeepForLooking(string path, string name)
    {
        Directory.CreateDirectory(RenderedScreenshot.ImagesFolder);
        File.Copy(path, Path.Combine(RenderedScreenshot.ImagesFolder, name + ".png"), overwrite: true);
    }

    /// <summary>
    /// Counts pixels in a horizontal band that differ from the colour at the picture's bottom
    /// right corner, which on a cleared VT100 screen is the background.
    /// </summary>
    /// <param name="bitmap">
    /// The picture.
    /// </param>
    /// <param name="y0">
    /// First row of the band.
    /// </param>
    /// <param name="y1">
    /// Row after the last.
    /// </param>
    /// <returns>
    /// How many pixels carry ink.
    /// </returns>
    private static int InkInBand(SKBitmap bitmap, int y0, int y1)
    {
        var background = bitmap.GetPixel(bitmap.Width - 2, bitmap.Height - 2);
        int ink = 0;
        for (int y = Math.Max(0, y0); y < Math.Min(bitmap.Height, y1); y++)
        {
            for (int x = 0; x < bitmap.Width; x++)
            {
                if (bitmap.GetPixel(x, y) != background) ink++;
            }
        }
        return ink;
    }

    [AvaloniaFact]
    public async Task AreaTerminalSavesTheTerminalControlAtItsOwnSizeWithRealInk()
    {
        var (main, tab) = await WindowWithText();
        var path = TempPng("terminal");

        try
        {
            var result = await Shoot(main, tab, "terminal", path);
            Assert.True(result.Success, result.Error);

            using var bitmap = SKBitmap.Decode(path);
            Assert.NotNull(bitmap);
            KeepForLooking(path, "screenshot-command-terminal");

            // The size of the terminal control as laid out - nothing of the window around it.
            Assert.Equal((int)Math.Round(tab.Control.Bounds.Width), bitmap.Width);
            Assert.Equal((int)Math.Round(tab.Control.Bounds.Height), bitmap.Height);
            Assert.True(bitmap.Width < (int)Math.Round(main.Bounds.Width) || bitmap.Height < (int)Math.Round(main.Bounds.Height),
                "the terminal picture is as big as the whole window, so it is not the terminal alone");

            // Row one carries the text, so the top band of one row's height has glyph ink. A row
            // is the screen height over 24; the band is taken a little generously for padding.
            int rowHeight = bitmap.Height / 24;
            int textInk = InkInBand(bitmap, 0, rowHeight * 2);
            Assert.True(textInk > Text.Length * 4,
                "only " + textInk + " ink pixels where '" + Text + "' was written - nothing was drawn");

            // A blank row in the middle of the screen carries none.
            int blankInk = InkInBand(bitmap, rowHeight * 11, rowHeight * 12);
            Assert.Equal(0, blankInk);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
            main.Close();
        }
    }

    [AvaloniaFact]
    public async Task AreaWindowSavesTheWholeWindowWithTheTerminalInsideIt()
    {
        var (main, tab) = await WindowWithText();
        var windowPath = TempPng("window");
        var terminalPath = TempPng("terminal-for-window");

        try
        {
            var result = await Shoot(main, tab, "window", windowPath);
            Assert.True(result.Success, result.Error);
            Assert.StartsWith("window saved", result.Output!);

            var terminalResult = await Shoot(main, tab, null, terminalPath);
            Assert.True(terminalResult.Success, terminalResult.Error);

            using var window = SKBitmap.Decode(windowPath);
            using var terminal = SKBitmap.Decode(terminalPath);
            KeepForLooking(windowPath, "screenshot-command-window");

            Assert.Equal((int)Math.Round(main.Bounds.Width), window.Width);
            Assert.Equal((int)Math.Round(main.Bounds.Height), window.Height);

            // Where the control sits in the window.
            var origin = tab.Control.TranslatePoint(new Point(0, 0), main);
            Assert.NotNull(origin);
            int ox = (int)Math.Round(origin!.Value.X);
            int oy = (int)Math.Round(origin.Value.Y);
            Assert.True(ox + terminal.Width <= window.Width && oy + terminal.Height <= window.Height,
                "the terminal control does not fit inside the window picture");

            // The terminal picture appears inside the window picture at that place. Compared
            // pixel by pixel; a different tab, a stale frame or the wrong visual cannot match.
            int same = 0;
            int total = terminal.Width * terminal.Height;
            for (int y = 0; y < terminal.Height; y++)
            {
                for (int x = 0; x < terminal.Width; x++)
                {
                    if (terminal.GetPixel(x, y) == window.GetPixel(ox + x, oy + y)) same++;
                }
            }
            Assert.True(same * 100L >= total * 99L,
                "only " + same + " of " + total + " terminal pixels match the window picture at (" + ox + "," + oy + ")");

            // And the window holds more than the terminal: something is drawn outside it.
            Assert.True(window.Width * window.Height > total,
                "the window picture is no bigger than the terminal");
        }
        finally
        {
            if (File.Exists(windowPath)) File.Delete(windowPath);
            if (File.Exists(terminalPath)) File.Delete(terminalPath);
            main.Close();
        }
    }

    [AvaloniaFact]
    public async Task AreaWindowIsRefusedWhenTheSessionsTabIsNotTheSelectedOne()
    {
        // A picture of the window with another tab selected would show the other tab's screen
        // under this session's name. The command refuses, and does not switch tabs on its own.
        var (main, tab) = await WindowWithText();
        var other = main.AddTabForTesting("other-host", "VT100");
        main.SelectTabForTesting(other);
        Dispatcher.UIThread.RunJobs();
        var path = TempPng("not-selected");

        try
        {
            var result = await Shoot(main, tab, "window", path);

            Assert.False(result.Success);
            Assert.Contains("not the selected tab", result.Error!);
            Assert.False(File.Exists(path), "a file was written for a refused capture");
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
            main.Close();
        }
    }
}
