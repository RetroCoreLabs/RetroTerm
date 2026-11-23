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

namespace RetroTerm.Tests.Avalonia
{
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
        public void QuickConnectRemembersThe3278ModelUsedLast()
        {
            QuickConnectWindow.ForgetLastUsed();
            var first = new QuickConnectWindow();
            ChooseProtocol(first, ConnectionFactory.ProtocolType.TN3270);
            first.FindControl<TextBox>("HostBox")!.Text = "localhost";
            first.FindControl<ComboBox>("EmulatorBox")!.SelectedItem = "IBM-3278-4";
            Assert.NotNull(first.BuildParameters(out _));

            var second = new QuickConnectWindow();

            Assert.Equal("IBM-3278-4", second.FindControl<ComboBox>("EmulatorBox")!.SelectedItem);
            QuickConnectWindow.ForgetLastUsed();
        }

        [AvaloniaFact]
        public void ChoosingTn3270OffersThe3278ModelsAndBackToTelnetRestoresTheChoice()
        {
            QuickConnectWindow.ForgetLastUsed();
            var window = new QuickConnectWindow();
            var emulatorBox = window.FindControl<ComboBox>("EmulatorBox")!;
            var portBox = window.FindControl<TextBox>("PortBox")!;
            emulatorBox.SelectedItem = "VT220";

            ChooseProtocol(window, ConnectionFactory.ProtocolType.TN3270);
            window.FindControl<TextBox>("HostBox")!.Text = "localhost";

            // The box SHOWS the 3278, not the stream terminal picked before (Ronny, 22 November
            // 2026), and offers the four models. The port is 3270, the port 3270 hosts such as
            // Hercules and TK5 listen on (Ronny, 22 November 2026), not the RFC 1576 telnet port 23.
            Assert.True(emulatorBox.IsEnabled);
            Assert.Equal("IBM-3278-2", emulatorBox.SelectedItem);
            Assert.Equal("3270", portBox.Text);
            emulatorBox.SelectedItem = "IBM-3278-4";
            var parameters = window.BuildParameters(out string? error);
            Assert.Null(error);
            Assert.Equal("IBM-3278-4", parameters!.EmulatorType);
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
            ClickWhere(window, view, legend, byPress: false);
        }

        // Two caps can carry the same legend (Backspace and cursor left are both an arrow), so a
        // key can also be found by the 3270 key it presses.
        private static void ClickKeyThatPresses(Window window, DescribedKeyboardView view, string press)
        {
            ClickWhere(window, view, press, byPress: true);
        }

        private static void ClickWhere(Window window, DescribedKeyboardView view, string name, bool byPress)
        {
            var keys = view.Description!.Keys;
            for (int i = 0; i < keys.Count; i++)
            {
                if ((byPress ? keys[i].Press : keys[i].Legend) == name)
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

            Assert.Fail("no key '" + name + "'");
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
        public async Task BackspaceOnTheKeyboardWindowErasesLocallyAndSendsNothing()
        {
            var (main, ibm, _, connection) = await TwoTabs();
            var keyboard = main.ToggleVirtualKeyboardForTesting();
            Dispatcher.UIThread.RunJobs();
            var view = keyboard.FindControl<DescribedKeyboardView>("DescribedKeyboard")!;

            // Backspace erases left, a local key (Ronny, 23 November 2026; KEYBOARD.md question 3).
            ClickKeyThatPresses(keyboard, view, "BACKSPACE");
            await ibm.Session.FlushAsync();

            Assert.Empty(connection.GetSentData());
            Assert.DoesNotContain("not built", main.StatusTextForTesting);

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
        public void AModel5DrawsItsAlternateScreenAndTheStatusLineUnderIt()
        {
            // EWA switches a model 5 to 27 x 132 (GA23-0059-4 p. 3-5). An A at the last address
            // (3563 = 55 x 64 + 43, SBA X'F7' X'6B' in 12-bit code), the status line on row 28.
            var emulator = new Ibm3270Emulator(5);
            emulator.ProcessData(new byte[]
            {
                RetroIO.Protocols.Tn3270.Command3270.EraseWriteAlternate, 0x02,
                RetroIO.Protocols.Tn3270.Order3270.StartField, 0x60, 0xD5, 0xC1, 0xD4, 0xC5,
                RetroIO.Protocols.Tn3270.Order3270.SetBufferAddress, 0xF7, 0x6B, 0xC1,
            });

            using var shot = RenderedScreenshot.Capture(emulator, "ibm3270-model5");

            Assert.True(shot.CellHasInk(0, 1));
            Assert.True(shot.CellHasInk(26, 131));
            Assert.True(shot.CellHasInk(27, 0));

            // BUGS.md B8: the empty middle of the screen is ONE colour, x3270's neutral black,
            // across many cell edges both ways. One rectangle per cell left a darker line (0x0B
            // against 0x10) at every edge, because cells sit at fractional pixel positions.
            var black = new SkiaSharp.SKColor(0x10, 0x10, 0x10);
            int y = (int)(10.5 * shot.CellHeight);
            for (int x = (int)(20 * shot.CellWidth); x < (int)(40 * shot.CellWidth); x++)
            {
                Assert.Equal(black, shot.PixelAt(x, y));
            }

            int x0 = (int)(30.5 * shot.CellWidth);
            for (int yy = (int)(5 * shot.CellHeight); yy < (int)(15 * shot.CellHeight); yy++)
            {
                Assert.Equal(black, shot.PixelAt(x0, yy));
            }
        }

        [AvaloniaFact]
        public void AThinRuleSetsLine25OffFromTheHostScreen()
        {
            // Ronny, 22 November 2026: a thin rule inside the picture (wc3270 spends a blank,
            // underlined row on it). Column 45 is blank on row 24 and on line 25, so any ink
            // there is the rule; one row higher there must be none.
            var emulator = new Ibm3270Emulator();
            emulator.ProcessData(Screen);

            using var shot = RenderedScreenshot.Capture(emulator, "ibm3270-line25-rule");

            int x = (int)(45.5 * shot.CellWidth);
            var background = shot.PixelAt(x, (int)(22.5 * shot.CellHeight));
            bool ruled = false;
            int top = (int)(24 * shot.CellHeight);
            for (int y = top - 1; y <= top + 1; y++)
            {
                ruled |= !RenderedScreenshot.ApproximatelyEqual(shot.PixelAt(x, y), background, 24);
            }

            Assert.True(ruled, "no rule at the top of line 25");
            Assert.True(RenderedScreenshot.ApproximatelyEqual(shot.PixelAt(x, (int)(23 * shot.CellHeight)), background, 24), "a rule above row 24 too");
        }

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

            // Line 25 (row index 24), not connected: "4" and "?" in reverse video, so the main
            // colour of their cells is the lit foreground, not the background of the plain "X" of
            // "X Not Connected" at column 7; the gap at 3..6 is empty (wc3270\screen.c, draw_oia).
            // Looked at 23 November 2026.
            SKColor background = shot.DominantColorInCell(24, 7);
            Assert.False(RenderedScreenshot.ApproximatelyEqual(shot.DominantColorInCell(24, 0), background, 40), "the 4 is not in reverse video");
            Assert.False(RenderedScreenshot.ApproximatelyEqual(shot.DominantColorInCell(24, 2), background, 40), "the box is not in reverse video");
            Assert.True(RenderedScreenshot.ApproximatelyEqual(shot.DominantColorInCell(24, 1), background, 40), "the A should not be in reverse video");
            Assert.True(shot.CellHasInk(24, 7));

            // The gap holds no character. Its top edge carries the thin rule above line 25, so the
            // cell is checked from two pixels below its top: every pixel there is background.
            int left = (int)(4 * shot.CellWidth) + 1;
            int right = (int)(5 * shot.CellWidth) - 1;
            int below = (int)(24 * shot.CellHeight) + 2;
            int bottom = (int)(25 * shot.CellHeight) - 1;
            SKColor gapBackground = shot.PixelAt(left, bottom);
            for (int y = below; y < bottom; y++)
            {
                for (int x = left; x < right; x++)
                {
                    Assert.True(RenderedScreenshot.ApproximatelyEqual(shot.PixelAt(x, y), gapBackground, 24), "ink in the gap at " + x + "," + y);
                }
            }
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
        public void ManageConnectionsOffersTn3270AndThenThe3278Models()
        {
            var (window, path) = ManageConnections();
            var radio = Field<RadioButton>(window, "_tn3270Radio");
            var terminal = Field<ComboBox>(window, "_emulatorCombo");
            terminal.SelectedItem = "VT220";

            Assert.Equal("TN3270", radio.Content);
            radio.IsChecked = true;

            Assert.True(terminal.IsEnabled);
            // The box shows the family; the model is in the size box (23 November 2026).
            Assert.Equal("IBM-3278", terminal.SelectedItem);
            Assert.Equal("80x24 (model 2)", Field<ComboBox>(window, "_sizeCombo").SelectedItem);
            Assert.Equal("TN3270", window.ProfileForTesting.Protocol);
            Assert.Equal("IBM-3278-2", window.ProfileForTesting.EmulatorType);
            Assert.Equal(ConnectionFactory.ProtocolType.TN3270, window.ProfileForTesting.ToConnectionParameters().Protocol);
            Assert.Equal("3270", Field<TextBox>(window, "_portBox").Text);
            Assert.Equal(3270, window.ProfileForTesting.Port);

            // Back to telnet: the stream terminals, the earlier choice and port 23 come back.
            Field<RadioButton>(window, "_telnetRadio").IsChecked = true;
            Assert.Equal("23", Field<TextBox>(window, "_portBox").Text);
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
                EmulatorType = "IBM-3278-5",
            });

            Assert.True(Field<RadioButton>(window, "_tn3270Radio").IsChecked);
            var terminal = Field<ComboBox>(window, "_emulatorCombo");
            Assert.Equal("IBM-3278", terminal.SelectedItem);
            Assert.Equal("132x27 (model 5)", Field<ComboBox>(window, "_sizeCombo").SelectedItem);
            Assert.Equal("IBM-3278-5", window.ProfileForTesting.EmulatorType);
            Assert.True(terminal.IsEnabled);
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
}
