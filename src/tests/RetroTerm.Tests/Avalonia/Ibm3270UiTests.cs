//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs/RetroTerm
// RetroTerm — Connecting today's machines to yesterday's systems
//

using System;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;

using RetroIO.Protocols.Tn3270;

using RetroTerm.Core.Protocols;
using RetroTerm.Core.Terminal.Input.Keyboards;
using RetroTerm.Desktop;
using RetroTerm.Desktop.Controls;
using RetroTerm.Desktop.Models;
using RetroTerm.Desktop.Views;
using RetroTerm.Ibm3270;
using RetroTerm.Tests.TDV;

using SkiaSharp;

using Xunit;

namespace RetroTerm.Tests.Avalonia;

/// <summary>
/// The IBM 3270 in the real windows: the connect dialog, the main window and its tabs, the one
/// keyboard window, real mouse clicks on the drawn keys, the rendered 3270 screen, and an MCP
/// open over a real socket. Every piece of 3270 behaviour that has a window has a test here.
/// </summary>
[Collection("Avalonia")]
public class Ibm3270UiTests
{
    // EW, WCC restore, SF(protected) "NAME", SF(unprotected) IC, SBA(20) SF(autoskip).
    private static readonly byte[] Screen =
    {
        Command3270.EraseWrite, 0x02,
        Order3270.StartField, 0x60, 0xD5, 0xC1, 0xD4, 0xC5,
        Order3270.StartField, 0x40,
        Order3270.InsertCursor,
        Order3270.SetBufferAddress, 0x40, 0xD4,
        Order3270.StartField, 0xF0,
    };

    // ---------------------------------------------------------------- the connect dialog

    private static void ChooseProtocol(QuickConnectWindow window, ConnectionFactory.ProtocolType protocol)
    {
        var box = window.FindControl<ComboBox>("ProtocolBox")!;
        var names = (string[])box.ItemsSource!;
        for (int i = 0; i < names.Length; i++)
        {
            if (names[i] == protocol.ToString())
            {
                box.SelectedIndex = i;
                return;
            }
        }

        Assert.Fail("protocol " + protocol + " is not offered");
    }

    [AvaloniaFact]
    public void ChoosingTn3270ShowsThe3278GreyedAndBackToTelnetRestoresTheChoice()
    {
        QuickConnectWindow.ForgetLastUsed();
        var window = new QuickConnectWindow();
        var emulatorBox = window.FindControl<ComboBox>("EmulatorBox")!;
        var portBox = window.FindControl<TextBox>("PortBox")!;
        emulatorBox.SelectedItem = "VT220";

        ChooseProtocol(window, ConnectionFactory.ProtocolType.TN3270);
        window.FindControl<TextBox>("HostBox")!.Text = "localhost";

        // The greyed box SHOWS the 3278, not the stream terminal picked before (Ronny, 22 November
        // 2026). The port is the telnet port: TN3270 is telnet (RFC 1576); TK5's 3270 is a
        // Hercules setting the person types.
        Assert.False(emulatorBox.IsEnabled);
        Assert.Equal("IBM-3278-2", emulatorBox.SelectedItem);
        Assert.Equal("23", portBox.Text);
        var parameters = window.BuildParameters(out string? error);
        Assert.Null(error);
        Assert.Equal("IBM-3278-2", parameters!.EmulatorType);
        Assert.Equal(ConnectionFactory.ProtocolType.TN3270, parameters.Protocol);

        // Back to telnet: the choice is open again and is honoured.
        ChooseProtocol(window, ConnectionFactory.ProtocolType.Telnet);
        Assert.True(emulatorBox.IsEnabled);
        Assert.Equal("VT220", emulatorBox.SelectedItem);
        Assert.Equal("VT220", window.BuildParameters(out _)!.EmulatorType);
        QuickConnectWindow.ForgetLastUsed();
    }

    // ---------------------------------------------------------------- the keyboard window

    private static async Task<(MainWindow Main, TabSession Ibm, TabSession Vt, InMemoryConnection Connection)> TwoTabs()
    {
        var main = new MainWindow();
        main.Show();
        var vt = main.AddTabForTesting("vt-host");
        var ibm = main.AddTabForTesting("tk5", "IBM-3278-2");
        var connection = new InMemoryConnection();
        await ibm.Session.ConnectAsync(connection, TestContext.Current.CancellationToken);
        connection.SimulateReceive(Screen);
        await ibm.Session.FlushAsync();
        connection.ClearSentData();
        main.SelectTabForTesting(ibm);
        return (main, ibm, vt, connection);
    }

    [AvaloniaFact]
    public async Task TheKeyboardWindowShowsTheTabsKeyboardAndStaysOpenAcrossTabChanges()
    {
        var (main, ibm, vt, _) = await TwoTabs();
        Assert.IsType<Ibm3270Emulator>(ibm.Session.Emulator);

        var keyboard = main.ToggleVirtualKeyboardForTesting();
        Assert.True(keyboard.IsVisible);
        Assert.Equal("IBM3278", keyboard.CurrentDescribedKeyboard!.Id);
        Assert.Contains("IBM 3278", keyboard.Title);

        main.SelectTabForTesting(vt);
        Assert.True(keyboard.IsVisible);
        Assert.False(keyboard.ShowsDescribedKeyboard);
        Assert.True(keyboard.FindControl<VirtualKeyboardPanel>("VirtualKeyboard")!.IsVisible);

        main.SelectTabForTesting(ibm);
        Assert.True(keyboard.IsVisible);
        Assert.Equal("IBM3278", keyboard.CurrentDescribedKeyboard!.Id);

        keyboard.AllowClose = true;
        keyboard.Close();
        main.Close();
    }

    [AvaloniaFact]
    public async Task TheWindowGrowsToFitTheWiderKeyboard()
    {
        var (main, _, _, _) = await TwoTabs();
        var keyboard = main.ToggleVirtualKeyboardForTesting();
        Dispatcher.UIThread.RunJobs();
        var view = keyboard.FindControl<DescribedKeyboardView>("DescribedKeyboard")!;

        keyboard.FindControl<ComboBox>("DescribedChoice")!.SelectedIndex = 1;
        Dispatcher.UIThread.RunJobs();

        // Every key of the 122-key keyboard is inside the window: the window is at least as
        // wide and tall as the drawn keyboard.
        Assert.Equal("KB3270PLUS", keyboard.CurrentDescribedKeyboard!.Id);
        Assert.True(keyboard.Bounds.Width >= view.DesiredSize.Width,
            "window " + keyboard.Bounds.Width + " narrower than keyboard " + view.DesiredSize.Width);
        Assert.True(keyboard.Bounds.Height >= view.DesiredSize.Height);

        keyboard.FindControl<ComboBox>("DescribedChoice")!.SelectedIndex = 0;
        keyboard.AllowClose = true;
        keyboard.Close();
        main.Close();
    }

    private static void Click(Window window, DescribedKeyboardView view, string legend)
    {
        var keys = view.Description!.Keys;
        for (int i = 0; i < keys.Count; i++)
        {
            if (keys[i].Legend == legend)
            {
                var centre = new Point((keys[i].X + keys[i].Width / 2) * DescribedKeyboardView.UnitSize,
                    (keys[i].Y + keys[i].Height / 2) * DescribedKeyboardView.UnitSize);
                var at = view.TranslatePoint(centre, window)!.Value;
                window.MouseDown(at, MouseButton.Left);
                window.MouseUp(at, MouseButton.Left);
                Dispatcher.UIThread.RunJobs();
                return;
            }
        }

        Assert.Fail("no key '" + legend + "'");
    }

    [AvaloniaFact]
    public async Task MouseClicksOnTheDrawnKeysTypeAndEnterSendsTheRecord()
    {
        var (main, ibm, _, connection) = await TwoTabs();
        var keyboard = main.ToggleVirtualKeyboardForTesting();
        Dispatcher.UIThread.RunJobs();
        var view = keyboard.FindControl<DescribedKeyboardView>("DescribedKeyboard")!;

        Click(keyboard, view, "H");
        Click(keyboard, view, "I");
        await ibm.Session.FlushAsync();
        Assert.Empty(connection.GetSentData());
        Assert.Equal('h', (char)ibm.Session.Emulator.Buffer.GetCell(0, 6).Codepoint);

        Click(keyboard, view, "ENTER");
        await ibm.Session.FlushAsync();

        var sent = connection.GetSentData();
        Assert.Single(sent);
        Assert.Equal(new byte[] { 0x7D, 0x40, 0xC8, 0x11, 0x40, 0xC6, 0x88, 0x89 }, sent[0]);

        keyboard.AllowClose = true;
        keyboard.Close();
        main.Close();
    }

    [AvaloniaFact]
    public async Task AltThenTheDigitOneClickedSendsPf1()
    {
        var (main, ibm, _, connection) = await TwoTabs();
        var keyboard = main.ToggleVirtualKeyboardForTesting();
        Dispatcher.UIThread.RunJobs();
        var view = keyboard.FindControl<DescribedKeyboardView>("DescribedKeyboard")!;

        Click(keyboard, view, "ALT");
        Assert.True(view.AltLatched);
        Click(keyboard, view, "1");
        await ibm.Session.FlushAsync();

        Assert.Equal(Aid.Pf(1), connection.GetSentData()[0][0]);
        Assert.False(view.AltLatched);

        keyboard.AllowClose = true;
        keyboard.Close();
        main.Close();
    }

    [AvaloniaFact]
    public async Task AKeyThatIsNotBuiltSaysSoInTheStatusBarAndSendsNothing()
    {
        var (main, ibm, _, connection) = await TwoTabs();
        var keyboard = main.ToggleVirtualKeyboardForTesting();
        Dispatcher.UIThread.RunJobs();
        var view = keyboard.FindControl<DescribedKeyboardView>("DescribedKeyboard")!;

        Click(keyboard, view, "DUP");
        await ibm.Session.FlushAsync();

        Assert.Empty(connection.GetSentData());
        Assert.Contains("IBM.DUP is not built yet", main.StatusTextForTesting);

        keyboard.AllowClose = true;
        keyboard.Close();
        main.Close();
    }

    [AvaloniaFact]
    public async Task TypingOnThePhysicalKeyboardInTheKeyboardWindowSendsNothingOnA3270Tab()
    {
        // The TDV mapper in the keyboard window must not turn keys into bytes on a 3270 tab.
        var (main, ibm, _, connection) = await TwoTabs();
        var keyboard = main.ToggleVirtualKeyboardForTesting();
        Dispatcher.UIThread.RunJobs();

        keyboard.KeyTextInput("x");
        keyboard.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        await ibm.Session.FlushAsync();

        Assert.Empty(connection.GetSentData());

        keyboard.AllowClose = true;
        keyboard.Close();
        main.Close();
    }

    [AvaloniaFact]
    public void ALatchedShiftIsDrawnLatched()
    {
        var view = new DescribedKeyboardView { Description = Ibm3270Keyboards.Ibm3278 };
        var window = new Window { Content = view, SizeToContent = SizeToContent.WidthAndHeight };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        KeyCap shift = null!;
        for (int i = 0; i < view.Description!.Keys.Count; i++)
        {
            if (view.Description.Keys[i].Legend == "SHIFT")
            {
                shift = view.Description.Keys[i];
                break;
            }
        }

        int x = (int)((shift.X + 0.15) * DescribedKeyboardView.UnitSize);
        int y = (int)((shift.Y + 0.85) * DescribedKeyboardView.UnitSize);

        SKColor before = PixelOf(view, x, y);
        view.Press(shift);
        SKColor after = PixelOf(view, x, y);

        // The latched colour is amber (strong red, little blue); the plain modifier is grey.
        Assert.True(after.Red > after.Blue + 60, "latched shift is not amber: " + after);
        Assert.False(before.Red > before.Blue + 60, "unlatched shift is already amber: " + before);
        window.Close();
    }

    private static SKColor PixelOf(DescribedKeyboardView view, int x, int y)
    {
        var size = view.DesiredSize;
        using var target = new global::Avalonia.Media.Imaging.RenderTargetBitmap(
            new PixelSize((int)size.Width, (int)size.Height), new Vector(96, 96));
        target.Render(view);
        using var stream = new System.IO.MemoryStream();
        target.Save(stream, global::Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
        using var bitmap = SKBitmap.Decode(stream.ToArray());
        return bitmap.GetPixel(x, y);
    }

    // ---------------------------------------------------------------- the rendered 3270 screen

    [AvaloniaFact]
    public void TheRendered3270ScreenColoursFieldsByTheirAttributes()
    {
        var emulator = new Ibm3270Emulator();
        emulator.ProcessData(Screen);
        emulator.HandleText("hi");

        using var shot = RenderedScreenshot.Capture(emulator, "ibm3270-screen");

        // Column 0 holds a field attribute: shown blank on a 3270.
        Assert.False(shot.CellHasInk(0, 0));

        // "NAME" is protected, normal intensity: blue. "hi" is unprotected, normal: green.
        SKColor name = shot.BrightestColorInCell(0, 1);
        SKColor typed = shot.BrightestColorInCell(0, 6);
        Assert.True(name.Blue > name.Red && name.Blue > name.Green - 40, "protected field is not blue: " + name);
        Assert.True(typed.Green > typed.Red + 40 && typed.Green > typed.Blue + 40, "unprotected field is not green: " + typed);
    }

    // ---------------------------------------------------------------- Manage Connections

    private static T Field<T>(ManageConnectionsWindow window, string name)
        where T : class
    {
        var field = typeof(ManageConnectionsWindow).GetField(name,
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        Assert.True(field != null, "ManageConnectionsWindow." + name + " is gone - this test's hook needs updating");
        return (T)field!.GetValue(window)!;
    }

    private static (ManageConnectionsWindow Window, string ConfigPath) ManageConnections()
    {
        // A config file of the test's own, so the person's saved connections are never touched.
        string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
            "retroterm-tn3270-ui-" + Guid.NewGuid().ToString("N") + ".json");
        var window = new ManageConnectionsWindow(new RetroTerm.Core.Configuration.ConfigurationManager(path));
        window.Show();
        return (window, path);
    }

    private static void Done(ManageConnectionsWindow window, string path)
    {
        window.Close();
        if (System.IO.File.Exists(path))
        {
            System.IO.File.Delete(path);
        }
    }

    [AvaloniaFact]
    public void ManageConnectionsOffersTn3270AndThenShowsThe3278Greyed()
    {
        var (window, path) = ManageConnections();
        var radio = Field<RadioButton>(window, "_tn3270Radio");
        var terminal = Field<ComboBox>(window, "_emulatorCombo");
        terminal.SelectedItem = "VT220";

        Assert.Equal("TN3270", radio.Content);
        radio.IsChecked = true;

        Assert.False(terminal.IsEnabled);
        Assert.Equal("IBM-3278-2", terminal.SelectedItem);
        Assert.Equal("TN3270", window.ProfileForTesting.Protocol);
        Assert.Equal("IBM-3278-2", window.ProfileForTesting.EmulatorType);
        Assert.Equal(ConnectionFactory.ProtocolType.TN3270, window.ProfileForTesting.ToConnectionParameters().Protocol);

        // Back to telnet: the stream terminals and the earlier choice come back.
        Field<RadioButton>(window, "_telnetRadio").IsChecked = true;
        Assert.True(terminal.IsEnabled);
        Assert.Equal("VT220", terminal.SelectedItem);
        Assert.Equal("VT220", window.ProfileForTesting.EmulatorType);
        Done(window, path);
    }

    [AvaloniaFact]
    public void ASavedTn3270ConnectionOpensWithTn3270ChosenAndThe3278Shown()
    {
        var (window, path) = ManageConnections();

        window.LoadProfileForTesting(new RetroTerm.Core.Configuration.HostConfiguration
        {
            Name = "TK5",
            Protocol = "TN3270",
            Host = "localhost",
            Port = 3270,
            EmulatorType = "IBM-3278-2",
        });

        Assert.True(Field<RadioButton>(window, "_tn3270Radio").IsChecked);
        var terminal = Field<ComboBox>(window, "_emulatorCombo");
        Assert.Equal("IBM-3278-2", terminal.SelectedItem);
        Assert.False(terminal.IsEnabled);
        Assert.Equal(3270, window.ProfileForTesting.Port);
        Done(window, path);
    }

    // ---------------------------------------------------------------- MCP over a real socket
    [AvaloniaFact]
    public async Task AnMcpTn3270OpenGetsThe3278AndShowsTheHostScreen()
    {
        // A host on a real socket that plays the TK5 handshake and one screen saying "He".
        byte[] host =
        {
            0xFF, 0xFD, 0x18,
            0xFF, 0xFA, 0x18, 0x01, 0xFF, 0xF0,
            0xFF, 0xFB, 0x00,
            0xFF, 0xFD, 0x00,
            0xFF, 0xFB, 0x19, 0xFF, 0xFD, 0x19,
            0xF5, 0x42, 0x11, 0x40, 0x40, 0x1D, 0x60, 0xC8, 0x85,
            0xFF, 0xEF,
        };

        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var serve = Task.Run(async () =>
        {
            using var client = await listener.AcceptTcpClientAsync();
            var stream = client.GetStream();
            await stream.WriteAsync(host);
            var sink = new byte[256];
            try
            {
                while (await stream.ReadAsync(sink) > 0)
                {
                }
            }
            catch (System.IO.IOException)
            {
                // The client closed the connection: the end of the test.
            }
        });

        var main = new MainWindow();
        main.Show();
        try
        {
            var id = await main.OpenMcpSessionAsync(new ConnectionFactory.ConnectionParameters
            {
                Protocol = ConnectionFactory.ProtocolType.TN3270,
                Host = "127.0.0.1",
                Port = port,
                EmulatorType = "VT220", // must be overruled: TN3270 always gets the 3278
            }, TestContext.Current.CancellationToken);

            var session = main.GetMcpSession(id)!;
            Assert.IsType<Ibm3270Emulator>(session.Emulator);

            // Bytes cross a real socket, so poll with a deadline (CLAUDE.md), never a fixed sleep.
            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (DateTime.UtcNow < deadline && session.Emulator.Buffer.GetCell(0, 1).Codepoint != 'H')
            {
                await session.FlushAsync();
                Dispatcher.UIThread.RunJobs();
                await Task.Delay(10, TestContext.Current.CancellationToken);
            }

            Assert.Equal('H', (char)session.Emulator.Buffer.GetCell(0, 1).Codepoint);
            Assert.Equal('e', (char)session.Emulator.Buffer.GetCell(0, 2).Codepoint);
            await main.CloseMcpSessionAsync(id);
        }
        finally
        {
            main.Close();
            listener.Stop();
        }

        await serve;
    }
}
