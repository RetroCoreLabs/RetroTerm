using System.Linq;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using RetroTerm.Desktop;
using Xunit;

namespace RetroTerm.Tests.Avalonia;

/// <summary>
/// A popped-out window's session must be exactly as reachable through MCP as a docked tab's.
/// </summary>
/// <remarks>
/// <para><b>Ronny's report, 1 September 2026</b></para>
/// "Popped out windows must still be listed in the MCP listing of tab/windows." Traced to
/// <c>MainWindow.Mcp.cs</c>: <c>FindSessionById</c>, <c>ListSessionsOnUiThread</c> and
/// <c>CloseMcpSessionAsync</c> all scanned only <c>_tabs</c> — a session moved to
/// <c>_popoutWindows</c> by <c>PopOutTab</c> was invisible to <c>terminal_list</c>, unreachable
/// by sessionId (every other MCP tool routes through <c>GetMcpSession</c>), and could not be
/// closed through MCP at all. Fixed by scanning both lists in all three methods.
/// </remarks>
[Collection("Avalonia")]
public class PopoutSessionsAreVisibleToMcpTests
{
    [AvaloniaFact]
    public void ListMcpSessionsIncludesAPoppedOutTab()
    {
        var window = new MainWindow();
        window.Show();

        var tab = window.AddTabForTesting("localhost:23");
        window.PopOutTabForTesting(tab);

        var sessions = window.ListMcpSessions();

        Assert.Contains(sessions, s => s.Id == tab.Id);

        window.Close();
    }

    [AvaloniaFact]
    public void GetMcpSessionFindsAPoppedOutTabBySessionId()
    {
        var window = new MainWindow();
        window.Show();

        var tab = window.AddTabForTesting("localhost:23");
        window.PopOutTabForTesting(tab);

        var found = window.GetMcpSession(tab.Id);

        Assert.NotNull(found);
        Assert.Same(tab.Session, found);

        window.Close();
    }

    [AvaloniaFact]
    public async Task CloseMcpSessionAsyncClosesAPoppedOutWindow()
    {
        var window = new MainWindow();
        window.Show();

        var tab = window.AddTabForTesting("localhost:23");
        window.PopOutTabForTesting(tab);
        Assert.Contains(window.ListMcpSessions(), s => s.Id == tab.Id);

        await window.CloseMcpSessionAsync(tab.Id);

        Assert.DoesNotContain(window.ListMcpSessions(), s => s.Id == tab.Id);

        window.Close();
    }
}
