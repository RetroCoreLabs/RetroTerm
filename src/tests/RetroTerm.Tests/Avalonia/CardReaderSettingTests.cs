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

using Xunit;

namespace RetroTerm.Tests.Avalonia;

/// <summary>
/// The card reader SUBMIT sends to is a connection setting (Ronny, 22 November 2026): two boxes
/// on the Protocol tab of Manage Connections, saved with the connection and handed to the tab.
/// </summary>
[Collection("Avalonia")]
public class CardReaderSettingTests
{
    [AvaloniaFact]
    public void TheCardReaderBoxesLoadAndSaveWithTheConnection()
    {
        WithWindow(window =>
        {
            window.LoadProfileForTesting(new HostConfiguration { Name = "tk5", Protocol = "TN3270", Host = "localhost", Port = 3270, EmulatorType = "IBM-3278-2", CardReaderPort = 3505 });
            window.ShowProtocolTabForTesting();
            TextBox port = Find(window, "CardReaderPortBox");
            TextBox host = Find(window, "CardReaderHostBox");
            Assert.Equal("3505", port.Text);
            Assert.Equal("", host.Text);

            port.Text = "4505";
            host.Text = " reader.example ";
            HostConfiguration saved = window.ProfileForTesting.ToHostConfiguration();
            Assert.Equal(4505, saved.CardReaderPort);
            Assert.Equal("reader.example", saved.CardReaderHost);
            Assert.Equal(("reader.example", (int?)4505), ConnectionFactory.CardReaderAddress(saved.ToConnectionParameters()));
            Assert.Equal(("reader.example", (int?)4505), ConnectionFactory.CardReaderAddress(window.ProfileForTesting.ToConnectionParameters()));

            // Text that is not a port leaves the port as it was; an empty box means no reader.
            port.Text = "abc";
            Assert.Equal(4505, window.ProfileForTesting.CardReaderPort);
            port.Text = "70000";
            Assert.Equal(4505, window.ProfileForTesting.CardReaderPort);
            port.Text = "";
            host.Text = "";
            saved = window.ProfileForTesting.ToHostConfiguration();
            Assert.Null(saved.CardReaderPort);
            Assert.Null(saved.CardReaderHost);
        });
    }

    [AvaloniaFact]
    public void ANewConnectionHasNoCardReader()
    {
        WithWindow(window =>
        {
            window.LoadProfileForTesting(new HostConfiguration { Name = "tk5", Protocol = "TN3270", Host = "localhost", Port = 3270, EmulatorType = "IBM-3278-2" });
            window.ShowProtocolTabForTesting();
            Assert.Equal("", Find(window, "CardReaderPortBox").Text);
            Assert.Null(window.ProfileForTesting.ToHostConfiguration().CardReaderPort);
            Assert.False(window.ProfileForTesting.IsDirty);
        });
    }

    [AvaloniaFact]
    public void TheTabHandsTheCardReaderToItsSession()
    {
        // SUBMIT reads the reader from the session, so every way a tab gets its connection must
        // put it there; and a connection without a port must take an earlier one away.
        var session = new RetroTerm.Core.Session.TerminalSession(new RetroTerm.Core.Terminal.Emulators.TDV.TDV2200Emulator(80, 25), "CardReaderTest");
        var tab = new RetroTerm.Desktop.Models.TabSession(session, new RetroTerm.Desktop.Controls.TerminalControl());
        tab.LastConnectionParameters = new ConnectionFactory.ConnectionParameters { Protocol = ConnectionFactory.ProtocolType.TN3270, Host = "mvs.example", Port = 3270, EmulatorType = "IBM-3278-2", CardReaderPort = 3505 };
        Assert.Equal("mvs.example", session.CardReaderHost);
        Assert.Equal(3505, session.CardReaderPort);

        tab.LastConnectionParameters = new ConnectionFactory.ConnectionParameters { Protocol = ConnectionFactory.ProtocolType.TN3270, Host = "mvs.example", Port = 3270, EmulatorType = "IBM-3278-2" };
        Assert.Null(session.CardReaderHost);
        Assert.Null(session.CardReaderPort);
    }

    private static void WithWindow(System.Action<ManageConnectionsWindow> test)
    {
        // A configuration file of its own in %TEMP%, never the user's real one.
        string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "RetroTermCardReader-" + System.Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var window = new ManageConnectionsWindow(new ConfigurationManager(path));
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

    private static TextBox Find(ManageConnectionsWindow window, string name)
    {
        var all = new List<ILogical>(window.GetLogicalDescendants());
        for (int i = 0; i < all.Count; i++)
        {
            if (all[i] is TextBox box && box.Name == name)
            {
                return box;
            }
        }

        throw new Xunit.Sdk.XunitException("no TextBox named " + name);
    }
}
