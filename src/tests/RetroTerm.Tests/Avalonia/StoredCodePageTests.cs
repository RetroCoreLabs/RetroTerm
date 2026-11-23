//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs/RetroTerm
// RetroTerm — Connecting today's machines to yesterday's systems
//

using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using RetroIO.Protocols.Tn3270;
using RetroTerm.Core.Configuration;
using RetroTerm.Core.Protocols;
using RetroTerm.Core.Session;
using RetroTerm.Desktop.Controls;
using RetroTerm.Desktop.Models;
using RetroTerm.Ibm3270;
using Xunit;

namespace RetroTerm.Tests.Avalonia;

/// <summary>
/// A saved connection's 3270 code page reaches the tab, by every path that opens one (they all
/// set <see cref="TabSession.LastConnectionParameters"/>).
/// </summary>
[Collection("Avalonia")]
public class StoredCodePageTests
{
    [AvaloniaFact]
    public async Task TheSavedCodePageIsSetWhenTheTabGetsItsConnection()
    {
        var config = new HostConfiguration { Protocol = "TN3270", Host = "localhost", Port = 3270, EmulatorType = "IBM-3278-2", CodePage = 277 };
        var emulator = new Ibm3270Emulator();
        var session = new TerminalSession(emulator, "CodePageTest");
        var tab = new TabSession(session, new TerminalControl());

        tab.LastConnectionParameters = config.ToConnectionParameters();
        await session.FlushAsync();

        Assert.Equal(CodePage.Cp277, emulator.Display.CodePage);
    }

    [AvaloniaFact]
    public async Task NoSavedCodePageLeavesTheTerminalOn037()
    {
        var config = new HostConfiguration { Protocol = "TN3270", Host = "localhost", Port = 3270, EmulatorType = "IBM-3278-2" };
        var emulator = new Ibm3270Emulator();
        var session = new TerminalSession(emulator, "CodePageTest");
        var tab = new TabSession(session, new TerminalControl());

        tab.LastConnectionParameters = config.ToConnectionParameters();
        await session.FlushAsync();

        Assert.Equal(CodePage.Cp037, emulator.Display.CodePage);
    }

    [AvaloniaFact]
    public void ACopyOfTheConnectionKeepsItsCodePage()
    {
        var config = new HostConfiguration { Protocol = "TN3270", Host = "localhost", Port = 3270, CodePage = 277 };

        Assert.Equal(277, config.Clone().CodePage);
    }
}
