using Avalonia.Headless.XUnit;
using RetroTerm.Core.Protocols;
using RetroTerm.Core.Session;
using RetroTerm.Core.Terminal.Emulators;
using RetroTerm.Desktop;
using RetroTerm.Desktop.Controls;
using RetroTerm.Desktop.Models;
using Xunit;

namespace RetroTerm.Tests.Avalonia;

/// <summary>
/// Which tab a new connection is allowed to take over.
/// </summary>
/// <remarks>
/// <para><b>What this was written for, 27 August 2026</b></para>
/// One <c>terminal_open</c> aimed at a local test server took over a tab that was holding a SINTRAN
/// session on D100. The tab happened to be disconnected at that moment, the old rule reused any
/// disconnected active tab, and the caller had no way to know what it was overwriting. Ronny had
/// said not to touch those sessions.
/// <para><b>The rule, and why it is not two rules</b></para>
/// The UI and the MCP surface do NOT get different behaviour - that is the trap this repository
/// keeps falling into. They get the same rule, fed one honest fact: whether the caller can SEE the
/// tab it is about to reuse. A person clicking Connect is pointing at the tab and means it. A remote
/// caller is not pointing at anything, so it may only take a tab that has never held a connection.
/// <para><b>Why LastConnectionParameters is the discriminator</b></para>
/// It is already what tells a welcome tab from a dropped-but-once-connected one; the script "Run on"
/// picker uses the same fact, pinned by <see cref="TabSessionTargetableTests"/>. Adding a second way
/// to ask the same question is how the two surfaces drift apart.
/// </remarks>
[Collection("Avalonia")]
public class McpTabReuseTests
{
    /// <summary>
    /// A tab with nothing on it but the welcome message.
    /// </summary>
    /// <returns>
    /// The tab.
    /// </returns>
    private static TabSession NewWelcomeTab()
    {
        var session = new TerminalSession(new VT100Emulator(80, 24), "TabReuseTest");
        return new TabSession(session, new TerminalControl());
    }

    /// <summary>
    /// A tab that has held a connection and has since dropped.
    /// </summary>
    /// <returns>
    /// The tab.
    /// </returns>
    private static TabSession NewDroppedTab()
    {
        var tab = NewWelcomeTab();

        // Exactly what one of the D100 tabs looked like: not connected any more, but carrying the
        // connection it used to have - and a screenful of SINTRAN output nobody can see from the
        // other end of the MCP socket.
        tab.LastConnectionParameters = new ConnectionFactory.ConnectionParameters
        {
            Protocol = ConnectionFactory.ProtocolType.Telnet,
            Host = "localhost",
            Port = 9010
        };

        return tab;
    }

    [AvaloniaFact]
    public void ARemoteCallerMayNotTakeOverATabThatHeldAConnection()
    {
        // THE ONE THAT COST A TAB. Before 27 August 2026 this returned true and D100's session went.
        var dropped = NewDroppedTab();

        Assert.False(dropped.IsConnected);
        Assert.False(MainWindow.CanReuseTabForNewConnection(dropped, callerCanSeeTheTab: false));
    }

    [AvaloniaFact]
    public void ARemoteCallerMayTakeOverAWelcomeTab()
    {
        // The behaviour worth keeping: opening from MCP on a fresh app uses the welcome tab rather
        // than leaving it sitting beside the new one.
        var welcome = NewWelcomeTab();

        Assert.True(MainWindow.CanReuseTabForNewConnection(welcome, callerCanSeeTheTab: false));
    }

    [AvaloniaFact]
    public void APersonClickingConnectStillReusesTheTabTheyAreLookingAt()
    {
        // The UI must NOT change. Somebody looking at a dropped tab and pressing Connect means that
        // tab - opening a second one beside it would be the wrong answer.
        var dropped = NewDroppedTab();

        Assert.True(MainWindow.CanReuseTabForNewConnection(dropped, callerCanSeeTheTab: true));
    }

    [AvaloniaFact]
    public void NobodyTakesOverATabThatIsStillConnected()
    {
        // Neither surface may cut a live line. There is no tab here to stand in for a connected one
        // without a real connection, so this pins the null case instead - the other half of the
        // guard, and the one that decides what happens on an empty window.
        Assert.False(MainWindow.CanReuseTabForNewConnection(null, callerCanSeeTheTab: true));
        Assert.False(MainWindow.CanReuseTabForNewConnection(null, callerCanSeeTheTab: false));
    }
}
