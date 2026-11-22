//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs/RetroTerm
// RetroTerm — Connecting today's machines to yesterday's systems
//

using System.Threading.Tasks;

using Avalonia.Controls;
using Avalonia.Headless.XUnit;

using RetroIO.Protocols.Tn3270;

using RetroTerm.Core.Session;
using RetroTerm.Core.Terminal.Emulators.TDV;
using RetroTerm.Core.Terminal.Input.Keyboards;
using RetroTerm.Desktop.Controls;
using RetroTerm.Desktop.Views;
using RetroTerm.Ibm3270;
using RetroTerm.Tests.TDV;

using Xunit;

namespace RetroTerm.Tests.Avalonia;

/// <summary>
/// One keyboard window for every terminal: it shows the keyboard of the active tab's terminal,
/// stays open across tab changes, and remembers the IBM keyboard chosen (Ronny, 22 November 2026).
/// </summary>
[Collection("Avalonia")]
public class KeyboardWindowFollowsTheTabTests
{
    [AvaloniaFact]
    public void A3270TabShowsTheIbmKeyboardAndATdvTabTheTdvPanel()
    {
        var window = new VirtualKeyboardWindow();
        var tdvPanel = window.FindControl<VirtualKeyboardPanel>("VirtualKeyboard")!;

        window.ShowKeyboardsFor(new Ibm3270Emulator().OnScreenKeyboards);
        Assert.True(window.ShowsDescribedKeyboard);
        Assert.False(tdvPanel.IsVisible);
        Assert.Equal("IBM3278", window.CurrentDescribedKeyboard!.Id);
        Assert.Equal(SizeToContent.WidthAndHeight, window.SizeToContent);

        window.ShowKeyboardsFor(null);
        Assert.False(window.ShowsDescribedKeyboard);
        Assert.True(tdvPanel.IsVisible);
        Assert.Null(window.CurrentDescribedKeyboard);
    }

    [AvaloniaFact]
    public void TheChosenIbmKeyboardIsKeptAcrossTabChanges()
    {
        var window = new VirtualKeyboardWindow();
        var choice = window.FindControl<ComboBox>("DescribedChoice")!;
        var keyboards = new Ibm3270Emulator().OnScreenKeyboards;

        window.ShowKeyboardsFor(keyboards);
        choice.SelectedIndex = 1;
        Assert.Equal("KB3270PLUS", window.CurrentDescribedKeyboard!.Id);

        // To a TDV tab and back to a (different) 3270 tab.
        window.ShowKeyboardsFor(null);
        window.ShowKeyboardsFor(new Ibm3270Emulator().OnScreenKeyboards);
        Assert.Equal("KB3270PLUS", window.CurrentDescribedKeyboard!.Id);

        // Put it back so other tests start from the 3278.
        choice.SelectedIndex = 0;
    }

    [AvaloniaFact]
    public async Task AnOnScreenEnterReachesTheHostAsARecord()
    {
        var emulator = new Ibm3270Emulator();
        var session = new TerminalSession(emulator, "Test");
        var connection = new InMemoryConnection();
        await session.ConnectAsync(connection, TestContext.Current.CancellationToken);
        connection.SimulateReceive(new byte[]
        {
            Command3270.EraseWrite, 0x02, Order3270.StartField, 0x40, Order3270.InsertCursor,
        });
        await session.FlushAsync();
        connection.ClearSentData();

        Assert.Null(OnScreenKeyPress.Deliver(emulator, "IBM", null, "x"));
        Assert.Null(OnScreenKeyPress.Deliver(emulator, "IBM", "ENTER", null));
        await session.FlushAsync();

        var sent = connection.GetSentData();
        Assert.Single(sent);
        Assert.Equal(Aid.Enter, sent[0][0]);
    }

    [AvaloniaFact]
    public void APressForAnotherFamilyOrAnUnbuiltKeyIsRefusedWithAReason()
    {
        string? wrongFamily = OnScreenKeyPress.Deliver(new TDV2200Emulator(80, 24), "IBM", "ENTER", null);
        string? notBuilt = OnScreenKeyPress.Deliver(new Ibm3270Emulator(), "IBM", "DUP", null);

        Assert.Contains("not a key of this tab", wrongFamily);
        Assert.Contains("not built yet", notBuilt);
    }
}
