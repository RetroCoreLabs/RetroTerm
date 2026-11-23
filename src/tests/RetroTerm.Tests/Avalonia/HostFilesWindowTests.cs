//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs/RetroTerm
// RetroTerm — Connecting today's machines to yesterday's systems
//

using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;

using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;

using RetroTerm.Core.Commands.Builtin;
using RetroTerm.Desktop.Views;
using RetroTerm.Tests.Ibm3270;

using Xunit;

namespace RetroTerm.Tests.Avalonia
{
    /// <summary>
    /// The IBM Host Files window (Transfer, IBM Host Files...) of a 3270 tab: it opens, it draws,
    /// and it refuses every action with the plain READY message when TSO is not at READY,
    /// sending nothing to the host.
    /// </summary>
    [Collection("Avalonia")]
    public class HostFilesWindowTests
    {
        private readonly ITestOutputHelper _output;

        public HostFilesWindowTests(ITestOutputHelper output)
        {
            _output = output;
        }

        [AvaloniaFact]
        public async Task TheWindowOpensWithEveryControl()
        {
            var (session, _, _) = await HostFileTransferCommandTests.Open(HostFilesTests.Ready);
            var window = new HostFilesWindow(session, "TK5");
            window.Show();

            string[] names = { "DataSetBox", "ModeBox", "RecfmBox", "LreclBox", "BlksizeBox", "ViewButton", "DownloadButton", "UploadButton", "CancelButton", "StatusText" };
            for (int i = 0; i < names.Length; i++)
            {
                Assert.True(Find<Control>(window, names[i]) != null, names[i] + " is missing");
            }

            Assert.Equal("IBM Host Files - TK5", window.Title);
            Assert.Equal(0, Find<ComboBox>(window, "ModeBox")!.SelectedIndex);
            Assert.True(Find<Button>(window, "ViewButton")!.IsEnabled);
            Assert.False(Find<Button>(window, "CancelButton")!.IsEnabled);
            window.Close();
        }

        [AvaloniaFact]
        public async Task ViewDownloadAndUploadAreRefusedAwayFromReady()
        {
            var (session, connection, _) = await HostFileTransferCommandTests.Open(HostFileTransferCommandTests.Screen);
            string path = Path.Combine(Path.GetTempPath(), "rtt-hostfileswindow-" + Guid.NewGuid().ToString("N") + ".txt");
            File.WriteAllText(path, "X", Encoding.ASCII);
            var window = new HostFilesWindow(session, "TK5");
            window.Fill("HERC01.DEMO.CNTL(HELLO)", false, string.Empty, string.Empty, string.Empty);
            window.Show();
            try
            {
                await window.ViewAsync();
                Assert.Equal(HostFileTransfer.NotAtReadyMessage, window.StatusText);
                Assert.Null(window.LastViewWindow);

                await window.DownloadAsync(path + ".down");
                Assert.Equal(HostFileTransfer.NotAtReadyMessage, window.StatusText);
                Assert.False(File.Exists(path + ".down"));

                await window.UploadAsync(path);
                Assert.Equal(HostFileTransfer.NotAtReadyMessage, window.StatusText);

                Assert.Equal("Go to the READY prompt first (press F3 until you see READY), then try again.", Find<TextBlock>(window, "StatusText")!.Text);
                Assert.Empty(connection.GetSentData());
                Assert.False(window.IsBusy);
                Assert.True(Find<Button>(window, "UploadButton")!.IsEnabled);
            }
            finally
            {
                window.Close();
                File.Delete(path);
            }
        }

        [AvaloniaFact]
        public async Task TheButtonsAreOffWhileATransferRunsAndCancelEndsIt()
        {
            var (session, connection, _) = await HostFileTransferCommandTests.Open(HostFilesTests.Ready);
            var window = new HostFilesWindow(session, "TK5");
            window.Fill("RTTFT.TEXT", false, string.Empty, string.Empty, string.Empty);
            window.Show();
            try
            {
                Task view = window.ViewAsync();

                Assert.True(window.IsBusy);
                Assert.False(Find<Button>(window, "ViewButton")!.IsEnabled);
                Assert.False(Find<Button>(window, "DownloadButton")!.IsEnabled);
                Assert.False(Find<Button>(window, "UploadButton")!.IsEnabled);
                Assert.True(Find<Button>(window, "CancelButton")!.IsEnabled);

                // A second action while one runs does nothing.
                await window.UploadAsync(Path.Combine(Path.GetTempPath(), "does-not-matter.txt"));
                Assert.True(window.IsBusy);

                // Wait until the IND$FILE command has really gone out (an Enter, AID 7D), so the
                // Cancel below lands after it. Without this the test raced the window: on a warm
                // run the READY below arrived before the command, the command then locked the
                // keyboard, and nothing unlocked it (23 November 2026).
                var sentClock = System.Diagnostics.Stopwatch.StartNew();
                while (!Sent(connection).StartsWith("[7D", StringComparison.Ordinal))
                {
                    Assert.True(sentClock.ElapsedMilliseconds < 10000, "the IND$FILE command was never sent: " + Sent(connection));
                    await Task.Delay(10, TestContext.Current.CancellationToken);
                }

                // Cancel before the host has opened the transfer gives it up. TSO then shows
                // READY, Clear goes out, and the host's answer to it unlocks the keyboard.
                Find<Button>(window, "CancelButton")!.RaiseEvent(new global::Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                connection.SimulateReceive(HostFilesTests.Ready);
                await session.FlushAsync();
                var clock = System.Diagnostics.Stopwatch.StartNew();
                while (!LastSentIsClear(connection))
                {
                    Assert.True(clock.ElapsedMilliseconds < 10000, "Clear was not pressed after Cancel: " + Sent(connection) + " status " + window.StatusText + " busy " + window.IsBusy);
                    await Task.Delay(10, TestContext.Current.CancellationToken);
                }

                connection.SimulateReceive(HostFileTransferCommandTests.Hex("F5 C3 13"));
                await session.FlushAsync();
                await view;

                Assert.False(window.IsBusy);
                Assert.True(Find<Button>(window, "ViewButton")!.IsEnabled);
                Assert.False(Find<Button>(window, "CancelButton")!.IsEnabled);
                Assert.StartsWith("Cancelled.", window.StatusText, StringComparison.Ordinal);
                Assert.Null(window.LastViewWindow);
            }
            finally
            {
                window.Close();
            }
        }

        [AvaloniaFact]
        public void TheViewWindowShowsTheTextReadOnly()
        {
            var view = new HostFileViewWindow("RTTFT.TEXT", "HELLO\r\nWORLD\r\n");
            view.Show();

            TextBox? box = Find<TextBox>(view, "ContentBox");
            Assert.NotNull(box);
            Assert.True(box!.IsReadOnly);
            Assert.Equal("HELLO\r\nWORLD\r\n", view.Text);
            Assert.Equal("RTTFT.TEXT", view.Title);

            var shot = RenderedScreenshot.CaptureWindow(view, "host-file-view-window");
            Assert.NotNull(shot);
            using (shot)
            {
                _output.WriteLine("saved to " + shot!.SavedPath);
                Assert.True(shot.HasAnyPixelDifferentFrom(shot.PixelAt(0, 0)), "the view window is one flat colour");
            }

            view.Close();
        }

        [AvaloniaFact]
        public async Task TheWindowDraws()
        {
            var (session, _, _) = await HostFileTransferCommandTests.Open(HostFileTransferCommandTests.Screen);
            var window = new HostFilesWindow(session, "TK5");
            window.Fill("HERC01.DEMO.CNTL(HELLO)", false, "F", "80", string.Empty);
            window.Show();
            await window.ViewAsync();

            var shot = RenderedScreenshot.CaptureWindow(window, "host-files-window");
            Assert.NotNull(shot);
            using (shot)
            {
                _output.WriteLine("saved to " + shot!.SavedPath);
                Assert.True(shot.HasAnyPixelDifferentFrom(shot.PixelAt(0, 0)), "the window is one flat colour");
            }

            window.Close();
        }

        private static string Sent(RetroTerm.Tests.TDV.InMemoryConnection connection)
        {
            var sb = new StringBuilder();
            var sent = connection.GetSentData();
            for (int i = 0; i < sent.Count; i++)
            {
                sb.Append('[').Append(BitConverter.ToString(sent[i], 0, Math.Min(6, sent[i].Length))).Append(']');
            }

            return sb.ToString();
        }

        private static bool LastSentIsClear(RetroTerm.Tests.TDV.InMemoryConnection connection)
        {
            var sent = connection.GetSentData();
            return sent.Count > 0 && sent[sent.Count - 1].Length > 0 && sent[sent.Count - 1][0] == 0x6D;
        }

        private static T? Find<T>(Window window, string name) where T : Control
        {
            var all = new System.Collections.Generic.List<global::Avalonia.Visual>(window.GetVisualDescendants());
            for (int i = 0; i < all.Count; i++)
            {
                if (all[i] is T control && control.Name == name)
                {
                    return control;
                }
            }

            return null;
        }
    }
}
