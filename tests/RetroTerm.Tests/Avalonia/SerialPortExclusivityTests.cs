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
/// A serial port may only be opened once, and BOTH connect surfaces must say so.
/// </summary>
/// <remarks>
/// <para><b>What this was written for, 30 August 2026</b></para>
/// Ronny had COM11 open and was talking to the machine on it when a tab printed
/// "Access to the path 'COM11' is denied". Windows gives a COM port to one owner, so a
/// second open of a port this same process already holds fails that way — and the message
/// reads like a permissions problem on the device, which it is not.
/// <para><b>The two-surface trap, again</b></para>
/// The exclusivity check lived inside ConnectWithParameters, the UI path. MCP's
/// terminal_open goes through OpenMcpSessionAsync straight to ConnectToHostCore and never
/// saw it. That was harmless only while MCP could not open a serial port at all; the moment
/// it could, one remote call could take the console port from under a live session. The
/// check now lives in one place that both paths ask.
/// </remarks>
[Collection("Avalonia")]
public class SerialPortExclusivityTests
{
    private static ConnectionFactory.ConnectionParameters Serial(string portName)
    {
        return new ConnectionFactory.ConnectionParameters
        {
            Protocol = ConnectionFactory.ProtocolType.Serial,
            PortName = portName,
            BaudRate = 115200,
            DataBits = 7,
            ParityValue = 2,
            StopBitsValue = 1
        };
    }

    [AvaloniaFact]
    public async Task APortAnotherTabIsHolding_IsRefusedByName()
    {
        var window = new MainWindow();
        var tab = window.AddTabForTesting("COM11 (115200bps)");
        await tab.Session.ConnectAsync(new InMemoryConnection());
        Assert.True(tab.IsConnected);

        var clash = window.SerialPortAlreadyInUse(Serial("COM11"));

        Assert.NotNull(clash);
        Assert.Contains("COM11", clash!);
        Assert.Contains("already in use", clash!);
    }

    [AvaloniaFact]
    public async Task ADifferentPort_IsAllowed()
    {
        var window = new MainWindow();
        var tab = window.AddTabForTesting("COM11 (115200bps)");
        await tab.Session.ConnectAsync(new InMemoryConnection());

        Assert.Null(window.SerialPortAlreadyInUse(Serial("COM3")));
    }

    [AvaloniaFact]
    public void APortNobodyHolds_IsAllowed()
    {
        var window = new MainWindow();

        Assert.Null(window.SerialPortAlreadyInUse(Serial("COM11")));
    }

    [AvaloniaFact]
    public async Task ATelnetConnection_IsNeverBlocked()
    {
        // Only serial is exclusive. Two telnet sessions to the same host are ordinary.
        var window = new MainWindow();
        var tab = window.AddTabForTesting("localhost:9010");
        await tab.Session.ConnectAsync(new InMemoryConnection());

        var telnet = new ConnectionFactory.ConnectionParameters
        {
            Protocol = ConnectionFactory.ProtocolType.Telnet,
            Host = "localhost",
            Port = 9010
        };

        Assert.Null(window.SerialPortAlreadyInUse(telnet));
    }

    [AvaloniaFact]
    public void ADisconnectedTabDoesNotHoldItsPort()
    {
        // A tab that dropped has released the port — the whole reason the connection is
        // disposed on a drop. It must not go on blocking a reconnect.
        var window = new MainWindow();
        window.AddTabForTesting("COM11 (115200bps)");

        Assert.Null(window.SerialPortAlreadyInUse(Serial("COM11")));
    }

    [AvaloniaFact]
    public async Task TheDisconnectionNotice_NamesAMenuItemThatExists()
    {
        // It said "File - Connect" until 30 August 2026 and there has never been such an item.
        //
        // Updated 31 August 2026: the advice is no longer one fixed sentence. This tab never held
        // a connection - which is the shape of a FAILED connect, the case that produced the
        // "access denied" message - so Reconnect would be greyed out and pointing at it would be
        // the same fault in a new form. The notice must send the reader somewhere that works
        // instead. DisconnectionNoticeAdviceTests covers both branches; this one keeps the
        // original guarantee that no menu item is named which does not exist.
        var window = new MainWindow();
        var tab = window.AddTabForTesting("COM11 (115200bps)");

        window.ShowDisconnectionNoticeForTesting(tab, "Connection error: Access to the path 'COM11' is denied.");

        var snapshot = await tab.Session.ReadScreenAsync();
        var screen = snapshot.Text;
        Assert.Contains("Connection", screen);      // the menu that does exist
        Assert.Contains("Quick Connect", screen);   // and an item on it that is not greyed out
        Assert.DoesNotContain("File →", screen);    // the menu path that never existed
    }
}
