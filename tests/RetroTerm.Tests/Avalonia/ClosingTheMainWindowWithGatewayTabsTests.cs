using System;
using System.Collections.Generic;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using RetroTerm.Core.Protocols.WebSocket.Gateway;
using RetroTerm.Desktop;
using Xunit;

namespace RetroTerm.Tests.Avalonia;

/// <summary>
/// Closing the main window while tabs are connected through the ND-100 gateway.
/// </summary>
/// <remarks>
/// Ronny, 9 October 2026: two tabs open, the window's X pressed, "Close All" answered yes, the
/// confirmation dialog went away, and the main window and the connections stayed.
/// <c>ClosingTheMainWindowWithActiveConnectionsDoesNotCrashTests</c> covers the same close with
/// in-memory connections and passes; this one puts a live gateway under the tabs, because
/// <c>MainWindow.OnClosing</c> stops the gateway listener BEFORE it asks.
/// </remarks>
[Collection("Avalonia")]
public class ClosingTheMainWindowWithGatewayTabsTests
{
    private static Button? FindButtonByName(Visual root, string name)
    {
        var stack = new Stack<Visual>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var current = stack.Pop();
            if (current is Button button && button.Name == name)
            {
                return button;
            }

            foreach (var child in current.GetVisualChildren())
            {
                stack.Push(child);
            }
        }

        return null;
    }

    private static async Task<Window?> WaitForOwnedWindowAsync(Window owner)
    {
        for (int i = 0; i < 100; i++)
        {
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
            var owned = owner.OwnedWindows;
            if (owned.Count > 0 && owned[0] != null)
            {
                return owned[0];
            }

            await Task.Delay(20);
        }

        return null;
    }

    private static int FreePort()
    {
        var probe = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        probe.Start();
        int port = ((System.Net.IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        return port;
    }

    private static async Task PumpAsync(int milliseconds)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(milliseconds);
        while (DateTime.UtcNow < deadline)
        {
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
            await Task.Delay(20);
        }
    }

    [AvaloniaFact]
    public async Task CancelLeavesTheGatewayAloneAndCloseAllClosesTheWindow()
    {
        var window = new MainWindow();
        window.Show();

        // A listener of the test's own, on a free port. The window's own one sits on the saved
        // port, which a real emulator in a browser reconnects to.
        var port = FreePort();
        var listener = new GatewayListener();
        await listener.StartAsync(port);
        await window.UseGatewayListenerForTesting(listener);

        // A fake emulator registers two terminals.
        using var emulator = new ClientWebSocket();
        await emulator.ConnectAsync(new Uri("ws://127.0.0.1:" + port + "/"), CancellationToken.None);
        var register = "{\"type\":\"register\",\"terminals\":["
            + "{\"identCode\":43,\"name\":\"TERMINAL 12\",\"logicalDevice\":51},"
            + "{\"identCode\":44,\"name\":\"TERMINAL 13\",\"logicalDevice\":52}]}";
        await emulator.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes(register)),
            WebSocketMessageType.Text, true, CancellationToken.None);
        for (int i = 0; i < 100 && listener.GetTerminals().Count < 2; i++)
        {
            await PumpAsync(40);
        }

        Assert.Equal(2, listener.GetTerminals().Count);

        var first = window.AddTabForTesting("GW:TERMINAL 12");
        await first.Session.ConnectAsync(new GatewayConnection(listener, 43, "TERMINAL 12"));
        var second = window.AddTabForTesting("GW:TERMINAL 13");
        await second.Session.ConnectAsync(new GatewayConnection(listener, 44, "TERMINAL 13"));
        Assert.True(first.IsConnected);
        Assert.True(second.IsConnected);

        var closed = false;
        window.Closed += (_, _) => closed = true;

        // The first press of X, answered Cancel. The tabs are gateway tabs, so they are counted
        // (the dialog appears), and NOTHING has been stopped: the listener still listens, the
        // emulator is still on it and both tabs are still connected.
        window.Close();
        var dialog = await WaitForOwnedWindowAsync(window);
        Assert.NotNull(dialog);
        FindButtonByName(dialog!, "CloseConfirmationCancelButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await PumpAsync(400);

        Assert.True(window.IsVisible, "Cancel must leave the window open");
        Assert.True(listener.IsListening, "Cancel must not have stopped the gateway listener");
        Assert.True(listener.IsEmulatorConnected, "Cancel must not have dropped the emulator");
        Assert.True(first.IsConnected);
        Assert.True(second.IsConnected);

        // The second press, answered Close All: everything goes, including the window.
        window.Close();
        dialog = await WaitForOwnedWindowAsync(window);
        Assert.NotNull(dialog);
        FindButtonByName(dialog!, "CloseConfirmationCloseAllButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        var deadline = DateTime.UtcNow.AddSeconds(8);
        while (!closed && DateTime.UtcNow < deadline)
        {
            await PumpAsync(40);
        }

        Assert.True(closed, "Close All must close the window when the tabs are gateway tabs");
        Assert.False(first.IsConnected);
        Assert.False(second.IsConnected);
    }
}
