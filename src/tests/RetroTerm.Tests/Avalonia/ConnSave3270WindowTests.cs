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
