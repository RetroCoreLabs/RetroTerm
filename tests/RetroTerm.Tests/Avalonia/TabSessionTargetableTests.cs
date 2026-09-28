using Avalonia.Headless.XUnit;
using RetroTerm.Core.Protocols;
using RetroTerm.Core.Session;
using RetroTerm.Core.Terminal.Emulators;
using RetroTerm.Desktop.Controls;
using RetroTerm.Desktop.Models;
using Xunit;

namespace RetroTerm.Tests.Avalonia;

/// <summary>
/// Scripts and the "Run on" picker must NEVER target the WELCOME tab — a tab that
/// has never been connected only shows the welcome message. A dropped-but-once-
/// connected tab stays targetable (reconnect scripts need it).
/// </summary>
[Collection("Avalonia")]
public class TabSessionTargetableTests
{
    private static TabSession MakeTab()
    {
        var session = new TerminalSession(new VT100Emulator(80, 24), "TargetableTest");
        return new TabSession(session, new TerminalControl());
    }

    [AvaloniaFact]
    public void WelcomeTab_NeverConnected_IsNotScriptTargetable()
    {
        var tab = MakeTab();

        Assert.False(tab.IsConnected);
        Assert.False(tab.IsScriptTargetable); // the welcome tab: no scripts, ever
    }

    [AvaloniaFact]
    public void Tab_WithConnectionParameters_IsTargetable_EvenWhenDisconnected()
    {
        var tab = MakeTab();
        // A tab that HAS connected before carries its parameters — a dropped line
        // must stay targetable so a reconnect script can bring it back.
        tab.LastConnectionParameters = new ConnectionFactory.ConnectionParameters
        {
            Protocol = ConnectionFactory.ProtocolType.Telnet,
            Host = "localhost",
            Port = 23
        };

        Assert.False(tab.IsConnected);
        Assert.True(tab.IsScriptTargetable);
    }
}
