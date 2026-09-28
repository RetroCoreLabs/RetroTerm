using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using RetroTerm.Core.Protocols;
using RetroTerm.Core.Session;
using RetroTerm.Core.Terminal.Emulators;
using RetroTerm.Desktop;
using RetroTerm.Desktop.Controls;
using RetroTerm.Desktop.Models;
using RetroTerm.Tests.TDV;
using Xunit;

namespace RetroTerm.Tests.Avalonia;

/// <summary>
/// The notice printed when a connection ends must not tell the reader to click something that
/// is greyed out.
/// </summary>
/// <remarks>
/// <para><b>What was wrong, 31 August 2026</b></para>
/// The notice always said "Reconnect with Connection - Reconnect", but that menu item is only
/// enabled when the tab still remembers what it was connected to. A connect that FAILED never
/// reaches the line that stores those parameters - it is assigned after ConnectAsync returns - so
/// on the failure that matters most, the advice pointed at a disabled item. Ronny hit exactly
/// that: "even this message is shown the Connect -&gt; reconnect is disabled".
/// <para><b>The fix is one rule, not two</b></para>
/// The menu and the message now both call MainWindow.CanReconnect, so they cannot drift apart
/// again. These tests pin the rule and the two sentences it chooses between.
/// </remarks>
[Collection("Avalonia")]
public class DisconnectionNoticeAdviceTests
{
    [AvaloniaFact]
    public async Task ADroppedConnectionIsOfferedReconnect()
    {
        // The tab held a connection and lost it: Reconnect can genuinely put it back.
        var tab = NewTab();
        var connection = new InMemoryConnection();
        await tab.Session.ConnectAsync(connection);
        tab.LastConnectionParameters = new ConnectionFactory.ConnectionParameters
        {
            Protocol = ConnectionFactory.ProtocolType.Serial,
            PortName = "COM11"
        };

        connection.SimulateRemoteDrop();

        Assert.False(tab.Session.IsConnected);
        Assert.True(MainWindow.CanReconnect(tab));
    }

    [AvaloniaFact]
    public void AConnectThatNeverSucceededIsNotOfferedReconnect()
    {
        // THE ONE THAT WAS WRONG. The connect threw - "Access to the path 'COM11' is denied" -
        // so LastConnectionParameters was never assigned and Reconnect has nothing to use.
        var tab = NewTab();

        Assert.False(tab.Session.IsConnected);
        Assert.Null(tab.LastConnectionParameters);
        Assert.False(MainWindow.CanReconnect(tab));
    }

    [AvaloniaFact]
    public async Task AStillConnectedTabIsNotOfferedReconnect()
    {
        var tab = NewTab();
        await tab.Session.ConnectAsync(new InMemoryConnection());
        tab.LastConnectionParameters = new ConnectionFactory.ConnectionParameters
        {
            Protocol = ConnectionFactory.ProtocolType.Telnet,
            Host = "localhost",
            Port = 23
        };

        Assert.True(tab.Session.IsConnected);
        Assert.False(MainWindow.CanReconnect(tab));
    }

    [AvaloniaFact]
    public void NoTabAtAllIsNotOfferedReconnect()
    {
        Assert.False(MainWindow.CanReconnect(null));
    }

    [AvaloniaFact]
    public async Task TheNoticeNamesReconnectOnlyWhenItWouldWork()
    {
        var window = new MainWindow();

        // A tab that never connected: the advice must send the reader somewhere that works.
        var failed = window.AddTabForTesting("COM11 (115200bps)");
        window.ShowDisconnectionNoticeForTesting(failed,
            "Connection error: Access to the path 'COM11' is denied.");

        var failedScreen = (await failed.Session.ReadScreenAsync()).Text;
        Assert.Contains("nothing to reconnect to", failedScreen);
        Assert.Contains("Quick Connect", failedScreen);

        // A tab that held a connection and dropped: Reconnect is the right advice.
        var dropped = window.AddTabForTesting("COM11 (115200bps)");
        dropped.LastConnectionParameters = new ConnectionFactory.ConnectionParameters
        {
            Protocol = ConnectionFactory.ProtocolType.Serial,
            PortName = "COM11"
        };
        window.ShowDisconnectionNoticeForTesting(dropped, "Connection closed by remote host");

        var droppedScreen = (await dropped.Session.ReadScreenAsync()).Text;
        Assert.Contains("Reconnect", droppedScreen);
        Assert.DoesNotContain("nothing to reconnect to", droppedScreen);
    }

    private static TabSession NewTab()
    {
        var session = new TerminalSession(new VT100Emulator(80, 24), "NoticeAdviceTest");
        return new TabSession(session, new TerminalControl());
    }
}
