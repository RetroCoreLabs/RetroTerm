using System.Threading.Tasks;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using RetroTerm.Core.Protocols;
using RetroTerm.Core.Session;
using RetroTerm.Desktop;
using RetroTerm.Tests.TDV;
using Xunit;

namespace RetroTerm.Tests.Avalonia;

/// <summary>
/// Enter reconnects a disconnected tab, alongside Connection -&gt; Reconnect - not instead of it.
/// </summary>
/// <remarks>
/// <para><b>Ronny's request, 31 August 2026</b></para>
/// The disconnected notice already reads "Reconnect with Connection -&gt; Reconnect.", naming the
/// menu item as the only way. Ronny wants Enter to do the same thing while the tab is in that
/// state.
///
/// <para><b>Why the port-exclusivity message, and not a real connection attempt</b></para>
/// Proving Enter genuinely reached <c>OnReconnectClick</c> -&gt; <c>ConnectWithParameters</c>,
/// rather than just being swallowed as ordinary key input, needs an observable side effect. A real
/// serial or network connect would be slow and flaky in a test. Reusing the same trick as
/// <see cref="SerialPortExclusivityTests"/> - a serial port another tab already holds - makes the
/// attempt fail synchronously, in-process, with an exact known message
/// (<c>MainWindow.SerialPortAlreadyInUse</c>), which is the earliest observable point in
/// <c>ConnectWithParameters</c> and proves the whole chain ran.
/// </remarks>
[Collection("Avalonia")]
public class EnterReconnectsADisconnectedTabTests
{
    private static ConnectionFactory.ConnectionParameters Serial(string portName)
    {
        return new ConnectionFactory.ConnectionParameters
        {
            Protocol = ConnectionFactory.ProtocolType.Serial,
            PortName = portName,
            BaudRate = 115200,
        };
    }

    /// <summary>
    /// Lets the window's own fire-and-forget MCP startup attempt (<c>MainWindow.axaml.cs</c>,
    /// <c>_ = InitializeMcpServerAsync()</c>) finish before the test touches the status bar.
    /// </summary>
    /// <param name="window">
    /// The window just constructed and shown.
    /// </param>
    /// <remarks>
    /// It races the same <c>UpdateStatus</c> sink this test class reads: MCP has nowhere real to
    /// bind under a headless test runner, so it fails and posts its own status message
    /// (<c>Dispatcher.UIThread.Post</c>) on its own schedule. It runs exactly once per window, so
    /// letting it land here - before the test's own action - means nothing races in afterward.
    /// </remarks>
    private static async Task LetMcpStartupNoiseSettle(MainWindow window)
    {
        // Polled rather than a fixed delay: the real startup attempt is a socket bind with its
        // own OS-level timing, not something this test controls. Times out at 5 seconds so a
        // genuine regression fails loudly instead of hanging.
        for (int i = 0; i < 50; i++)
        {
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
            string? status = window.StatusTextForTesting;
            if (status != null && status.Contains("MCP server"))
            {
                return;
            }

            await Task.Delay(100);
        }
    }

    [AvaloniaFact]
    public async System.Threading.Tasks.Task EnterReconnectsWhenTheActiveTabCanReconnect()
    {
        var window = new MainWindow();
        window.Show();
        await LetMcpStartupNoiseSettle(window);

        // holder keeps COM11 open, so the reconnect attempt below is guaranteed to collide with
        // it rather than depending on any real hardware being present.
        var holder = window.AddTabForTesting("COM11 (115200bps)");
        await holder.Session.ConnectAsync(new InMemoryConnection());
        Assert.True(holder.IsConnected);

        var dropped = window.AddTabForTesting("COM11 (115200bps)");
        dropped.LastConnectionParameters = Serial("COM11");
        Assert.True(MainWindow.CanReconnect(dropped));

        window.SelectTabForTesting(dropped);
        dropped.Control.Focus();

        window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        window.KeyReleaseQwerty(PhysicalKey.Enter, RawInputModifiers.None);

        Assert.NotNull(window.StatusTextForTesting);
        Assert.Contains("COM11", window.StatusTextForTesting);
        Assert.Contains("already in use", window.StatusTextForTesting);

        window.Close();
    }

    [AvaloniaFact]
    public async System.Threading.Tasks.Task EnterDoesNothingSpecialWhenTheActiveTabIsAlreadyConnected()
    {
        // An ordinary Enter keystroke to a connected session must be untouched - the guard is
        // CanReconnect, which is false here because the tab never disconnected.
        var window = new MainWindow();
        window.Show();
        await LetMcpStartupNoiseSettle(window);

        var tab = window.AddTabForTesting("localhost:23");
        await tab.Session.ConnectAsync(new InMemoryConnection());
        tab.LastConnectionParameters = new ConnectionFactory.ConnectionParameters
        {
            Protocol = ConnectionFactory.ProtocolType.Telnet,
            Host = "localhost",
            Port = 23,
        };
        Assert.False(MainWindow.CanReconnect(tab));

        window.SelectTabForTesting(tab);
        tab.Control.Focus();

        window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        window.KeyReleaseQwerty(PhysicalKey.Enter, RawInputModifiers.None);

        // No reconnect attempt was made - the port-exclusivity/status-bar path this test class
        // otherwise proves Enter can reach never fired.
        Assert.DoesNotContain("already in use", window.StatusTextForTesting ?? string.Empty);

        window.Close();
    }

    [AvaloniaFact]
    public void EnterDoesNothingWhenNoTabCanReconnect()
    {
        // No tab at all is the same "nothing to do" case CanReconnect(null) already covers -
        // Enter must not throw or do anything observable.
        var window = new MainWindow();
        window.Show();

        window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        window.KeyReleaseQwerty(PhysicalKey.Enter, RawInputModifiers.None);

        window.Close();
    }

    [AvaloniaFact]
    public async System.Threading.Tasks.Task EnterAtTheSshPasswordPromptSubmitsThePasswordInsteadOfReconnecting()
    {
        // Ronny, 27 September 2026: "if I save username only I am asked for password but it
        // doesn't work." The tab at the prompt is disconnected and has parameters, so the
        // Enter-reconnects rule fired first, restarted the connect from the saved parameters (no
        // password) and showed the prompt again. The typed password was never sent.
        var window = new MainWindow();
        window.Show();
        await LetMcpStartupNoiseSettle(window);

        var tab = window.AddTabForTesting("127.0.0.1:1");
        var parameters = new ConnectionFactory.ConnectionParameters
        {
            Protocol = ConnectionFactory.ProtocolType.SSH,
            Host = "127.0.0.1",
            Port = 1, // nothing listens here, so the connect after Enter fails at once
            Username = "ronny",
            Password = null
        };
        window.SelectTabForTesting(tab);
        await window.BeginSshConnectForTesting(tab, parameters);
        Assert.True(window.IsInSshLoginPromptForTesting(tab), "a saved user name and no password must show the prompt");
        Assert.True(MainWindow.CanReconnect(tab), "the prompt state is exactly the one the reconnect rule matched");
        tab.Control.Focus();

        window.KeyTextInput("secret");
        window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        window.KeyReleaseQwerty(PhysicalKey.Enter, RawInputModifiers.None);

        for (int i = 0; i < 50 && window.IsInSshLoginPromptForTesting(tab); i++)
        {
            await global::Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => { }, global::Avalonia.Threading.DispatcherPriority.Background);
            await System.Threading.Tasks.Task.Delay(20);
        }

        // The prompt is over and the connect was attempted WITH the typed password: the SSH
        // path writes it into the parameters before it builds the connection. Before the fix the
        // prompt was still up and the password still null.
        Assert.False(window.IsInSshLoginPromptForTesting(tab), "Enter must end the prompt, not restart it");
        Assert.Equal("secret", tab.LastConnectionParameters?.Password);

        window.Close();
    }
}
