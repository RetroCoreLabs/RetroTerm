//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs/RetroTerm
// RetroTerm — Connecting today's machines to yesterday's systems
//

using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using RetroTerm.Core.Commands;
using RetroTerm.Core.Commands.Builtin;
using RetroTerm.Core.Configuration;
using RetroTerm.Core.Session;
using RetroTerm.Desktop.Views;
using Xunit;

namespace RetroTerm.Tests.Avalonia;

/// <summary>
/// A 3270 connection saved with CONNSAVE model=... codepage=... (as a script or an MCP call
/// saves it) shows the same model and code page in Manage Connections: the size box and the
/// code page box read what the command wrote.
/// </summary>
[Collection("Avalonia")]
public class ConnSave3270WindowTests
{
    private static ComboBox Find(Window window, string name)
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

    private static T FindNamed<T>(Window window, string name) where T : Control
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

    [AvaloniaFact]
    public async Task ConnSaveDoubleClickAndCardReaderShowInTheWindow()
    {
        string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "RetroTermConnSave3270-" + System.Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var manager = new ConfigurationManager(path);
            var registry = new CommandRegistry();
            ConnectionCommands.RegisterAll(registry, manager);
            using var session = new TerminalSession(new RetroTerm.Core.Terminal.Emulators.VT100Emulator(80, 24), "test");
            var saved = await registry.ExecuteAsync("CONNSAVE", session, new CommandArgs()
                .Set("name", "tk5").Set("protocol", "tn3270").Set("host", "localhost").Set("port", "3270")
                .Set("doubleclick", "select").Set("reader_port", "3505").Set("reader_host", "reader.lab"));
            Assert.True(saved.Success, saved.Error);

            var reread = new ConfigurationManager(path);
            await reread.LoadAsync();
            var window = new ManageConnectionsWindow(reread);
            window.LoadProfileForTesting(ConnectionCommands.FindByName(reread, "tk5")!);

            window.ShowKeyboardTabForTesting();
            Assert.Equal(ManageConnectionsWindow.DoubleClickSelectsWordText, Find(window, "DoubleClickCombo").SelectedItem);

            window.ShowProtocolTabForTesting();
            Assert.Equal("3505", FindNamed<TextBox>(window, "CardReaderPortBox").Text);
            Assert.Equal("reader.lab", FindNamed<TextBox>(window, "CardReaderHostBox").Text);
        }
        finally
        {
            if (System.IO.File.Exists(path))
            {
                System.IO.File.Delete(path);
            }
        }
    }

    [AvaloniaFact]
    public async Task ConnSavePrinterSettingsShowInTheWindow()
    {
        string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "RetroTermConnSave3270-" + System.Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var manager = new ConfigurationManager(path);
            var registry = new CommandRegistry();
            ConnectionCommands.RegisterAll(registry, manager);
            using var session = new TerminalSession(new RetroTerm.Core.Terminal.Emulators.VT100Emulator(80, 24), "test");
            var saved = await registry.ExecuteAsync("CONNSAVE", session, new CommandArgs()
                .Set("name", "prt").Set("protocol", "tn3270").Set("host", "localhost").Set("port", "3270")
                .Set("emulator", "IBM-3287-1")
                .Set("printer_output", "Printer").Set("printer_folder", "jobs").Set("printer_delete_pdf", "true")
                .Set("printer_quiet_seconds", "7").Set("printer_lines_per_page", "56")
                .Set("printer_tractor", "true").Set("printer_band_colour", "Green"));
            Assert.True(saved.Success, saved.Error);

            var reread = new ConfigurationManager(path);
            await reread.LoadAsync();
            var window = new ManageConnectionsWindow(reread);
            window.LoadProfileForTesting(ConnectionCommands.FindByName(reread, "prt")!);
            window.ShowPrinterTabForTesting();

            Assert.Equal(ManageConnectionsWindow.PrinterOutputPrinterText, Find(window, "PrinterOutputCombo").SelectedItem);
            Assert.Equal("jobs", FindNamed<TextBox>(window, "PrinterFolderBox").Text);
            Assert.True(FindNamed<CheckBox>(window, "PrinterDeletePdfCheck").IsChecked);
            Assert.Equal("7", FindNamed<TextBox>(window, "PrinterQuietBox").Text);
            Assert.Equal("56", FindNamed<TextBox>(window, "PrinterLinesPerPageBox").Text);
            Assert.True(FindNamed<CheckBox>(window, "PrinterTractorCheck").IsChecked);
            Assert.Equal(ManageConnectionsWindow.PrinterTractorGreenText, Find(window, "PrinterTractorColourCombo").SelectedItem);
        }
        finally
        {
            if (System.IO.File.Exists(path))
            {
                System.IO.File.Delete(path);
            }
        }
    }

    [AvaloniaFact]
    public async Task ALargerScreenRoundTripsThroughCommandWindowFileAndTerminal()
    {
        string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "RetroTermConnSave3270-" + System.Guid.NewGuid().ToString("N") + ".json");
        try
        {
            // Saved by the command, as a script or an MCP call saves it.
            var manager = new ConfigurationManager(path);
            var registry = new CommandRegistry();
            ConnectionCommands.RegisterAll(registry, manager);
            using var session = new TerminalSession(new RetroTerm.Core.Terminal.Emulators.VT100Emulator(80, 24), "test");
            var saved = await registry.ExecuteAsync("CONNSAVE", session, new CommandArgs()
                .Set("name", "big").Set("protocol", "tn3270").Set("host", "localhost").Set("port", "3270")
                .Set("screen_size", "100x50"));
            Assert.True(saved.Success, saved.Error);

            // Read back by the window: the 3278 family, model 2, and the larger screen in the box.
            var reread = new ConfigurationManager(path);
            await reread.LoadAsync();
            var window = new ManageConnectionsWindow(reread);
            window.LoadProfileForTesting(ConnectionCommands.FindByName(reread, "big")!);
            window.ShowTerminalTabForTesting();
            window.Show();
            ComboBox size = Find(window, "SizeCombo");
            Assert.Equal("IBM-3278", Find(window, "EmulatorCombo").SelectedItem);
            Assert.True(size.IsEditable);
            Assert.Equal("100x50", size.Text);
            Assert.Equal("IBM-3278-2", window.ProfileForTesting.EmulatorType);
            Assert.Equal("100x50", window.ProfileForTesting.Ibm3270ScreenSize);

            // Changed in the window and saved to the file.
            size.Text = "120x60";
            Assert.True(window.ProfileForTesting.IsDirty);
            await reread.UpdateAsync(window.ProfileForTesting.ToHostConfiguration());

            // A picture of the Terminal tab, to look at (images\rendered).
            using (var frame = global::Avalonia.Headless.HeadlessWindowExtensions.CaptureRenderedFrame(window))
            {
                string folder = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(typeof(ConnSave3270WindowTests).Assembly.Location)!,
                    "..", "..", "..", "Avalonia", "images", "rendered");
                System.IO.Directory.CreateDirectory(folder);
                using var file = System.IO.File.Create(System.IO.Path.Combine(folder, "manage-connections-3278-larger-screen.png"));
                frame!.Save(file, global::Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
            }
            window.Close();

            // The file carries it, and the terminal built from it is that screen.
            var again = new ConfigurationManager(path);
            await again.LoadAsync();
            var stored = ConnectionCommands.FindByName(again, "big")!;
            Assert.Equal("120x60", stored.Ibm3270ScreenSize);
            var emulator = Assert.IsType<RetroTerm.Ibm3270.Ibm3270Emulator>(EmulatorFactory.CreateForConnection(stored.ToConnectionParameters()));
            Assert.Equal((120, 60), (emulator.AlternateScreenColumns, emulator.AlternateScreenRows));
            Assert.Equal(RetroTerm.Ibm3270.Ibm3270Emulator.DynamicTerminalType, emulator.TerminalType);
        }
        finally
        {
            if (System.IO.File.Exists(path))
            {
                System.IO.File.Delete(path);
            }
        }
    }

    [AvaloniaFact]
    public async Task ConnSaveModelAndCodePageShowInTheWindow()
    {
        // A configuration file of its own in %TEMP%, never the user's real one.
        string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "RetroTermConnSave3270-" + System.Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var manager = new ConfigurationManager(path);
            var registry = new CommandRegistry();
            ConnectionCommands.RegisterAll(registry, manager);
            using var session = new TerminalSession(new RetroTerm.Core.Terminal.Emulators.VT100Emulator(80, 24), "test");
            var saved = await registry.ExecuteAsync("CONNSAVE", session, new CommandArgs()
                .Set("name", "tk5").Set("protocol", "tn3270").Set("host", "localhost").Set("port", "3270")
                .Set("model", "IBM-3278-5").Set("codepage", "277"));
            Assert.True(saved.Success, saved.Error);

            // A fresh manager reads the file back, as the window does when it opens.
            var reread = new ConfigurationManager(path);
            await reread.LoadAsync();
            var window = new ManageConnectionsWindow(reread);
            window.LoadProfileForTesting(ConnectionCommands.FindByName(reread, "tk5")!);
            window.ShowTerminalTabForTesting();

            Assert.Equal("IBM-3278", Find(window, "EmulatorCombo").SelectedItem);
            Assert.Equal("132x27 (model 5)", Find(window, "SizeCombo").SelectedItem);
            Assert.Equal("IBM-3278-5", window.ProfileForTesting.EmulatorType);

            window.ShowKeyboardTabForTesting();
            Assert.Equal("277 Denmark, Norway", Find(window, "CodePageCombo").SelectedItem);
            Assert.Equal(277, window.ProfileForTesting.CodePage);
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
