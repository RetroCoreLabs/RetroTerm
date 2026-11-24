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

    // Every field of the profile that differs from its snapshot, as "name: now vs snapshot", so a
    // failing IsDirty says WHICH field the window changed rather than only that one did.
    private static string ChangedFields(RetroTerm.Desktop.ViewModels.ConnectionProfileViewModel profile)
    {
        var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        var fields = typeof(RetroTerm.Desktop.ViewModels.ConnectionProfileViewModel).GetFields(flags);
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < fields.Length; i++)
        {
            string name = fields[i].Name;
            if (!name.StartsWith("_snapshot", System.StringComparison.Ordinal)) continue;
            string live = "_" + char.ToLowerInvariant(name[9]) + name.Substring(10);
            var liveField = typeof(RetroTerm.Desktop.ViewModels.ConnectionProfileViewModel).GetField(live, flags);
            if (liveField == null) continue;
            object? now = liveField.GetValue(profile);
            object? then = fields[i].GetValue(profile);
            if (!Equals(now, then)) sb.Append(live).Append(": ").Append(now).Append(" vs ").Append(then).Append("; ");
        }
        return sb.ToString();
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
            Assert.False(window.ProfileForTesting.IsDirty);

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
            Assert.False(window.ProfileForTesting.IsDirty);

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
    public async Task ThePartnerDisplayRoundTripsThroughCommandWindowAndFile()
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
                .Set("name", "prt").Set("protocol", "tn3270").Set("host", "localhost").Set("port", "3270")
                .Set("emulator", "IBM-3287-1").Set("printer_associate", "TERM0001"));
            Assert.True(saved.Success, saved.Error);

            // Read back by the window: the box shows it, because this is a 3287 on TN3270.
            var reread = new ConfigurationManager(path);
            await reread.LoadAsync();
            var window = new ManageConnectionsWindow(reread);
            window.LoadProfileForTesting(ConnectionCommands.FindByName(reread, "prt")!);
            window.ShowPrinterTabForTesting();
            window.Show();
            TextBox box = FindNamed<TextBox>(window, "PrinterAssociateBox");
            TextBlock problem = FindNamed<TextBlock>(window, "PrinterAssociateProblem");
            Assert.True(FindNamed<StackPanel>(window, "PrinterAssociatePanel").IsVisible);
            Assert.Equal("TERM0001", box.Text);
            Assert.False(problem.IsVisible);

            // Opened and not touched, so not changed: Save stays dark (BUGS.md B25, where the
            // command's "tn3270" against the window's "TN3270" read as a change).
            Assert.False(window.ProfileForTesting.IsDirty);

            // A name CONNSAVE would refuse is refused here with the same words, and the stored
            // name stays as it was.
            box.Text = "TERM00001";
            Assert.True(problem.IsVisible);
            Assert.Contains("at most 8 characters", problem.Text, System.StringComparison.Ordinal);
            Assert.Equal("TERM0001", window.ProfileForTesting.PrinterAssociate);
            box.Text = "A B";
            Assert.Contains("letters, digits, @, # and $", problem.Text, System.StringComparison.Ordinal);

            // A good name is taken, the message goes, and the file carries it.
            box.Text = " LU#2 ";
            Assert.False(problem.IsVisible);
            Assert.Equal("LU#2", window.ProfileForTesting.PrinterAssociate);
            await reread.UpdateAsync(window.ProfileForTesting.ToHostConfiguration());

            // A picture of the Printer tab, to look at (images\rendered), taken with a refusal showing.
            box.Text = "TOO-LONG-NAME";
            using (var frame = global::Avalonia.Headless.HeadlessWindowExtensions.CaptureRenderedFrame(window))
            {
                string folder = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(typeof(ConnSave3270WindowTests).Assembly.Location)!,
                    "..", "..", "..", "Avalonia", "images", "rendered");
                System.IO.Directory.CreateDirectory(folder);
                using var file = System.IO.File.Create(System.IO.Path.Combine(folder, "manage-connections-3287-partner-display.png"));
                frame!.Save(file, global::Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
            }

            // A display terminal sends no ASSOCIATE, so the box goes away.
            window.ProfileForTesting.EmulatorType = "IBM-3278-2";
            Assert.False(FindNamed<StackPanel>(window, "PrinterAssociatePanel").IsVisible);
            window.Close();

            var again = new ConfigurationManager(path);
            await again.LoadAsync();
            Assert.Equal("LU#2", ConnectionCommands.FindByName(again, "prt")!.PrinterAssociate);
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
            Assert.False(window.ProfileForTesting.IsDirty);

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
            Assert.False(window.ProfileForTesting.IsDirty);
        }
        finally
        {
            if (System.IO.File.Exists(path))
            {
                System.IO.File.Delete(path);
            }
        }
    }

    /// <summary>
    /// Every protocol and a terminal, typed in lower case to CONNSAVE (as a script or an MCP call
    /// may type them), are stored in the one spelling the window writes, so the connection opens
    /// in Manage Connections as not changed (BUGS.md B25).
    /// </summary>
    [AvaloniaTheory]
    [InlineData("telnet", "vt220", "Telnet", "VT220")]
    [InlineData("ssh", "xterm", "SSH", "XTERM")]
    [InlineData("tn3270", "ibm-3278-4", "TN3270", "IBM-3278-4")]
    [InlineData("tn3270", "ibm-3287-1", "TN3270", "IBM-3287-1")]
    [InlineData("serial", "tdv2200", "Serial", "TDV2200")]
    [InlineData("gateway", "tdv2215", "Gateway", "TDV2215")]
    [InlineData("opcomsimulator", "vt100", "OpcomSimulator", "VT100")]
    public async Task ALowerCaseConnSaveIsStoredInTheWindowsSpellingAndOpensUnchanged(
        string protocol, string emulator, string storedProtocol, string storedEmulator)
    {
        string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "RetroTermConnSave3270-" + System.Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var manager = new ConfigurationManager(path);
            var registry = new CommandRegistry();
            ConnectionCommands.RegisterAll(registry, manager);
            using var session = new TerminalSession(new RetroTerm.Core.Terminal.Emulators.VT100Emulator(80, 24), "test");
            var args = new CommandArgs().Set("name", "c").Set("protocol", protocol).Set("emulator", emulator);
            if (protocol == "serial")
            {
                args.Set("port_name", "COM11");
            }
            else
            {
                args.Set("host", "localhost").Set("port", "3270");
            }
            var saved = await registry.ExecuteAsync("CONNSAVE", session, args);
            Assert.True(saved.Success, saved.Error);

            var reread = new ConfigurationManager(path);
            await reread.LoadAsync();
            var stored = ConnectionCommands.FindByName(reread, "c")!;
            Assert.Equal(storedProtocol, stored.Protocol);
            Assert.Equal(storedEmulator, stored.EmulatorType);

            var window = new ManageConnectionsWindow(reread);
            window.LoadProfileForTesting(stored);
            Assert.Equal(storedProtocol, window.ProfileForTesting.Protocol);
            Assert.Equal(storedEmulator, window.ProfileForTesting.EmulatorType);
            // Opening it does not move the port to the protocol's default: port 3270 is a default
            // (TN3270's), and the protocol-change rule used to rewrite it to 22 on an SSH one.
            Assert.Equal(stored.Port, window.ProfileForTesting.Port);
            Assert.True(!window.ProfileForTesting.IsDirty, ChangedFields(window.ProfileForTesting));
        }
        finally
        {
            if (System.IO.File.Exists(path))
            {
                System.IO.File.Delete(path);
            }
        }
    }

    /// <summary>
    /// A file written before B25 was fixed holds the protocol and terminal as they were typed.
    /// It still loads, as that connection, and opens as not changed.
    /// </summary>
    [AvaloniaFact]
    public async Task AnOldFileWithLowerCaseNamesStillOpensUnchanged()
    {
        string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "RetroTermConnSave3270-" + System.Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var manager = new ConfigurationManager(path);
            await manager.AddAsync(new HostConfiguration
            {
                Name = "old",
                Protocol = "tn3270",
                EmulatorType = "ibm-3278-3",
                Host = "localhost",
                Port = 3270,
                Width = 80,
                Height = 32
            });

            var reread = new ConfigurationManager(path);
            await reread.LoadAsync();
            var window = new ManageConnectionsWindow(reread);
            window.LoadProfileForTesting(ConnectionCommands.FindByName(reread, "old")!);
            Assert.Equal("TN3270", window.ProfileForTesting.Protocol);
            Assert.Equal("IBM-3278-3", window.ProfileForTesting.EmulatorType);
            Assert.False(window.ProfileForTesting.IsDirty);
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
