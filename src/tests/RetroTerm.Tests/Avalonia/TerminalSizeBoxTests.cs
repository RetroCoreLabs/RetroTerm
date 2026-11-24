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

using RetroTerm.Core.Configuration;
using RetroTerm.Core.Protocols;
using RetroTerm.Desktop.Views;
using RetroTerm.Ibm3270;

using Xunit;

namespace RetroTerm.Tests.Avalonia;

/// <summary>
/// The Terminal Size box in Manage Connections follows the chosen terminal (Ronny, 23 November
/// 2026): a 3278 lists its four models, a terminal that follows the window takes any size typed
/// in, and every other terminal offers its one screen.
/// </summary>
[Collection("Avalonia")]
public class TerminalSizeBoxTests
{
    [AvaloniaFact]
    public void EachKindOfTerminalGetsItsOwnSizes()
    {
        Assert.Equal(new[] { "80x25" }, TerminalSizeChoices.For("TDV2200"));
        Assert.False(TerminalSizeChoices.AllowsAnySize("TDV2200"));

        string[] xterm = TerminalSizeChoices.For("XTERM");
        Assert.True(TerminalSizeChoices.AllowsAnySize("XTERM"));
        Assert.Equal("80x24", xterm[0]);
        Assert.Contains("132x25", xterm);

        Assert.Equal(new[] { "80x24 (model 2)", "80x32 (model 3)", "80x43 (model 4)", "132x27 (model 5)" }, TerminalSizeChoices.For("IBM-3278-4"));
        Assert.False(TerminalSizeChoices.AllowsAnySize("IBM-3278-2"));
        Assert.Equal("IBM-3278-5", TerminalSizeChoices.ModelFor("132x27 (model 5)"));
        Assert.Equal((132, 27), EmulatorFactory.GetRecommendedSize("IBM-3278-5"));
    }

    [AvaloniaFact]
    public void ATypedSizeIsHeldToTheLimitsAHostResizeHas()
    {
        Assert.True(TerminalSizeChoices.TryParse(" 100 X 40 ", out int columns, out int rows));
        Assert.Equal((100, 40), (columns, rows));
        Assert.True(TerminalSizeChoices.TryParse("512x512", out _, out _));
        Assert.False(TerminalSizeChoices.TryParse("513x24", out _, out _));
        Assert.False(TerminalSizeChoices.TryParse("0x24", out _, out _));
        Assert.False(TerminalSizeChoices.TryParse("-80x24", out _, out _));
        Assert.False(TerminalSizeChoices.TryParse("80", out _, out _));
        Assert.False(TerminalSizeChoices.TryParse("80xabc", out _, out _));
    }

    [AvaloniaFact]
    public void TheModelTableMatchesTheScreenTheEmulatorBuilds()
    {
        for (int i = 0; i < ConnectionFactory.Ibm3278Models.Length; i++)
        {
            int model = ConnectionFactory.Ibm3278Models[i][^1] - '0';
            // The model's size is its alternate screen; every model starts at 24 x 80.
            Assert.Equal(ConnectionFactory.Ibm3278ModelSizes[i], Ibm3270Emulator.AlternateScreenSize(model));
        }
    }

    [AvaloniaFact]
    public void ASaved3270ShowsTheFamilyAndItsModelAndPickingASizeChangesTheModel()
    {
        WithWindow(window =>
        {
            window.LoadProfileForTesting(new HostConfiguration { Name = "tk5", Protocol = "TN3270", Host = "localhost", Port = 3270, EmulatorType = "IBM-3278-5" });
            ComboBox terminal = Find(window, "EmulatorCombo");
            ComboBox size = Find(window, "SizeCombo");

            Assert.Equal("IBM-3278", terminal.SelectedItem);
            Assert.Equal("132x27 (model 5)", size.SelectedItem);
            // Editable since 24 November 2026: the list is still the four models, and a screen
            // larger than the model's may be typed in (ATypedSizeMakesA3278Larger... below).
            Assert.True(size.IsEditable);
            Assert.Equal("IBM-3278-5", window.ProfileForTesting.EmulatorType);

            size.SelectedItem = "80x43 (model 4)";

            Assert.Equal("IBM-3278-4", window.ProfileForTesting.EmulatorType);
            Assert.Equal("IBM-3278-4", window.ProfileForTesting.ToHostConfiguration().EmulatorType);
            Assert.Equal("80x43", window.ProfileForTesting.Size);
        });
    }

    [AvaloniaFact]
    public void AnXtermTakesATypedSizeAndATdvOffersOnlyItsOwn()
    {
        WithWindow(window =>
        {
            window.LoadProfileForTesting(new HostConfiguration { Name = "x", Protocol = "Telnet", Host = "localhost", Port = 23, EmulatorType = "XTERM", Width = 100, Height = 40 });
            ComboBox terminal = Find(window, "EmulatorCombo");
            ComboBox size = Find(window, "SizeCombo");

            Assert.True(size.IsEditable);
            Assert.Equal("100x40", size.Text);

            size.Text = "120x50";
            Assert.Equal("120x50", window.ProfileForTesting.Size);
            size.Text = "9999x50";
            Assert.Equal("120x50", window.ProfileForTesting.Size);

            terminal.SelectedItem = "TDV2200";

            Assert.False(size.IsEditable);
            Assert.Equal(new[] { "80x25" }, size.ItemsSource);
            Assert.Equal("80x25", size.SelectedItem);
            Assert.Equal("80x25", window.ProfileForTesting.Size);
        });
    }

    [AvaloniaFact]
    public void TheDoubleClickBoxSavesWithTheConnectionAndReachesTheTab()
    {
        WithWindow(window =>
        {
            window.LoadProfileForTesting(new HostConfiguration { Name = "tk5", Protocol = "TN3270", Host = "localhost", Port = 3270, EmulatorType = "IBM-3278-2", DoubleClickPressesEnter = false });
            window.ShowKeyboardTabForTesting();
            ComboBox box = Find(window, "DoubleClickCombo");
            Assert.Equal(ManageConnectionsWindow.DoubleClickSelectsWordText, box.SelectedItem);

            box.SelectedIndex = 0;
            Assert.Null(window.ProfileForTesting.ToHostConfiguration().DoubleClickPressesEnter);

            box.SelectedIndex = 1;
            HostConfiguration saved = window.ProfileForTesting.ToHostConfiguration();
            Assert.False(saved.DoubleClickPressesEnter);
            Assert.False(saved.ToConnectionParameters().DoubleClickPressesEnter);
        });
    }

    [AvaloniaFact]
    public void ATypedSizeMakesA3278LargerAndARefusalIsShownUnderTheBox()
    {
        WithWindow(window =>
        {
            window.LoadProfileForTesting(new HostConfiguration { Name = "big", Protocol = "TN3270", Host = "localhost", Port = 3270, EmulatorType = "IBM-3278-2" });
            ComboBox size = Find(window, "SizeCombo");
            TextBlock problem = FindNamed<TextBlock>(window, "SizeProblem");
            Assert.False(problem.IsVisible);

            // A larger screen keeps the model and is stored as typed.
            size.Text = "100x50";
            Assert.Equal("IBM-3278-2", window.ProfileForTesting.EmulatorType);
            Assert.Equal("100x50", window.ProfileForTesting.Ibm3270ScreenSize);
            Assert.False(problem.IsVisible);

            // Past the emulator's own limit: said under the box, and the stored screen stays.
            size.Text = "128x128";
            Assert.True(problem.IsVisible);
            Assert.Contains("at most 16383", problem.Text, System.StringComparison.Ordinal);
            Assert.Equal("100x50", window.ProfileForTesting.Ibm3270ScreenSize);

            // A picture of the refusal under the box, to look at (images\rendered).
            using (var frame = global::Avalonia.Headless.HeadlessWindowExtensions.CaptureRenderedFrame(window))
            {
                string folder = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(typeof(TerminalSizeBoxTests).Assembly.Location)!,
                    "..", "..", "..", "Avalonia", "images", "rendered");
                System.IO.Directory.CreateDirectory(folder);
                using var file = System.IO.File.Create(System.IO.Path.Combine(folder, "manage-connections-3278-screen-refused.png"));
                frame!.Save(file, global::Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
            }

            // Narrower than the model: refused by the same rule.
            size.Text = "70x50";
            Assert.Contains("at least 80 columns", problem.Text, System.StringComparison.Ordinal);
            Assert.Equal("100x50", window.ProfileForTesting.Ibm3270ScreenSize);

            // A model's own size picks that model, with no larger screen.
            size.Text = "132x27";
            Assert.Equal("IBM-3278-5", window.ProfileForTesting.EmulatorType);
            Assert.Null(window.ProfileForTesting.Ibm3270ScreenSize);
            Assert.False(problem.IsVisible);

            // And picking a model from the list goes back to that model's own screen.
            size.Text = "140x40";
            Assert.Equal("140x40", window.ProfileForTesting.Ibm3270ScreenSize);
            size.SelectedItem = "80x43 (model 4)";
            Assert.Equal("IBM-3278-4", window.ProfileForTesting.EmulatorType);
            Assert.Null(window.ProfileForTesting.Ibm3270ScreenSize);
        });
    }

    private static T FindNamed<T>(ManageConnectionsWindow window, string name) where T : Control
    {
        var all = new List<ILogical>(window.GetLogicalDescendants());
        for (int i = 0; i < all.Count; i++)
        {
            if (all[i] is T control && control.Name == name)
            {
                return control;
            }
        }

        throw new Xunit.Sdk.XunitException("no " + typeof(T).Name + " named " + name);
    }

    private static void WithWindow(System.Action<ManageConnectionsWindow> test)
    {
        // A configuration file of its own in %TEMP%, never the user's real one.
        string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "RetroTermSizeBox-" + System.Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var window = new ManageConnectionsWindow(new ConfigurationManager(path));
            window.ShowTerminalTabForTesting();
            window.Show();
            test(window);
            window.Close();
        }
        finally
        {
            if (System.IO.File.Exists(path))
            {
                System.IO.File.Delete(path);
            }
        }
    }

    private static ComboBox Find(ManageConnectionsWindow window, string name)
    {
        var all = new List<ILogical>(window.GetLogicalDescendants());
        for (int i = 0; i < all.Count; i++)
        {
            if (all[i] is ComboBox combo && combo.Name == name)
            {
                return combo;
            }
        }

        throw new Xunit.Sdk.XunitException("no ComboBox named " + name);
    }
}
