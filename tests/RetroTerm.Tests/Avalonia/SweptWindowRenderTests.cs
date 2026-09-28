using System;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using RetroTerm.Core.Configuration;
using RetroTerm.Core.Protocols.WebSocket.Gateway;
using RetroTerm.Core.Session;
using RetroTerm.Core.Terminal.Emulators;
using RetroTerm.Core.Transfer;
using RetroTerm.Desktop.Controls;
using RetroTerm.Desktop.Views;
using Xunit;
using Xunit.Abstractions;

namespace RetroTerm.Tests.Avalonia;

/// <summary>
/// Renders the windows whose buttons moved onto the theme classes on 9 September 2026, so the
/// result can be LOOKED AT rather than only asserted about.
///
/// The classes carry a border, a padding and a hover colour the hand-set brushes did not, so a
/// button can change size when it takes one. A test that checks class names cannot see a toolbar
/// that has wrapped, a Cancel button that has grown into its neighbour, or a border that now
/// shows where there was none. The PNG can.
///
/// Only the six windows that build with no arguments are here. The rest need a live session, a
/// gateway listener or a transfer in flight, and building fakes for them would prove less than
/// looking at the six that share the same button rows.
///
/// The pictures land in tests\RetroTerm.Tests\Avalonia\images\rendered\.
/// </summary>
[Collection("Avalonia")]
public class SweptWindowRenderTests
{
    private readonly ITestOutputHelper _output;

    public SweptWindowRenderTests(ITestOutputHelper output)
    {
        _output = output;
    }

    /// <summary>
    /// Shows a window, captures it, and checks the frame is a real layout rather than a stub.
    /// </summary>
    /// <param name="window">
    /// The window to render. It is shown here, not by the caller.
    /// </param>
    /// <param name="saveAs">
    /// File name for the PNG.
    /// </param>
    private void RenderAndSave(Window window, string saveAs)
    {
        // CLOSE THE WINDOW AFTERWARDS, in a finally so a failed assertion still closes it.
        //
        // This class and WindowContentFitsTests together open about 26 windows and used to close
        // none of them. Window teardown is what releases the render and font resources a headless
        // window holds, so leaving them open piles that up across the run.
        //
        // Why it matters here: on 10 September 2026 the suite began losing the WHOLE headless UI
        // collection at once, roughly one run in three, on a KeyNotFoundException for
        // 'fonts:SystemFonts' inside Avalonia's font manager. With these two classes filtered out
        // the suite ran green five times out of five, with everything else still in. That is a
        // lead rather than a proof - five clean runs happen about one time in eight by luck at
        // that rate - but leaking windows in a test is wrong on its own terms.
        try
        {
            window.Show();

            var shot = RenderedScreenshot.CaptureWindow(window, saveAs);
            Assert.NotNull(shot);
            using (shot!)
            {
                _output.WriteLine($"{saveAs} {shot.Width}x{shot.Height} saved to {shot.SavedPath}");
                Assert.True(shot.Width > 300 && shot.Height > 200,
                    $"{saveAs} rendered at {shot.Width}x{shot.Height}, too small to be the real layout");
            }
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void TheKermitDebugWindowRenders()
        => RenderAndSave(new KermitDebugWindow(), "swept-kermit-debug");

    [AvaloniaFact]
    public void TheLogViewerWindowRenders()
        => RenderAndSave(new LogViewerWindow(), "swept-log-viewer");

    [AvaloniaFact]
    public void TheManageConnectionsWindowRenders()
        => RenderAndSave(new ManageConnectionsWindow(), "swept-manage-connections");

    [AvaloniaFact]
    public void ThePreferencesWindowRenders()
        => RenderAndSave(new PreferencesWindow(), "swept-preferences");

    [AvaloniaFact]
    public void TheProtocolMonitorWindowRenders()
        => RenderAndSave(new ProtocolMonitorWindow(), "swept-protocol-monitor");

    [AvaloniaFact]
    public void TheQuickConnectWindowRenders()
        => RenderAndSave(new QuickConnectWindow(), "swept-quick-connect");

    // The five below need something handed to them. None of it starts anything: a
    // GatewayListener opens its socket in Start, not in its constructor, and a
    // TerminalSession only runs its pump once data arrives. They are here because their
    // buttons were changed too, and a button nobody has looked at is a button nobody has
    // checked - which is exactly the gap this file exists to close.

    [AvaloniaFact]
    public void TheGatewayDebugWindowRenders()
    {
        using var listener = new GatewayListener();
        RenderAndSave(new GatewayDebugWindow(listener), "swept-gateway-debug");
    }

    [AvaloniaFact]
    public void TheGatewayStatisticsWindowRenders()
    {
        using var listener = new GatewayListener();
        RenderAndSave(new GatewayStatisticsWindow(listener), "swept-gateway-statistics");
    }

    /// <summary>
    /// The Gateway Statistics window must be tall enough for its own content.
    ///
    /// It declared 480 by 420 and its four sections need more, so the Clients section was cut in
    /// half and the Reset Counters and Close buttons sat off the bottom edge. The window cannot be
    /// resized and has no scroll bar, so there was no way to reach them at all. Found 9 September
    /// 2026 by rendering it and looking; it had been that way since the window was written.
    ///
    /// WHAT THIS COMPARES, and the first version of it got this wrong. It asks the CONTENT how tall
    /// it wants to be, and compares that against the height the WINDOW actually is. The first
    /// version re-arranged the window to the content's own desired height first, so the two could
    /// never disagree and it passed with the defect still in place - an assertion that passed for
    /// the wrong reason, which is worse than no assertion.
    /// </summary>
    [AvaloniaFact]
    public void TheGatewayStatisticsWindowIsTallEnoughForItsContent()
    {
        using var listener = new GatewayListener();
        var window = new GatewayStatisticsWindow(listener);
        window.Show();

        var content = window.Content as Control;
        Assert.NotNull(content);

        // How tall the content wants to be at the window's width, with no limit imposed.
        content!.Measure(new global::Avalonia.Size(window.Bounds.Width, double.PositiveInfinity));
        double wanted = content.DesiredSize.Height;
        double have = window.Bounds.Height;

        _output.WriteLine($"content wants {wanted:F0}, window is {have:F0}");

        // Closed before the assertion, so a failure still releases it.
        window.Close();

        Assert.True(wanted <= have,
            $"the content needs {wanted:F0} and the window is only {have:F0} tall, so the bottom "
            + "of it is cut off - and this window cannot be resized and has no scroll bar");
    }

    /// <summary>
    /// The window holding the two green "+ Add" buttons, which are the only users of the
    /// AddAction class. Both tabs are rendered, because each carries one of them.
    /// </summary>
    [AvaloniaFact]
    public void TheGatewayDiskConfigWindowRenders()
    {
        using var listener = new GatewayListener();
        var settings = new GatewayDiskSettings();
        var window = new GatewayDiskConfigWindow(listener, settings);

        // Shown and captured by hand rather than through RenderAndSave, because that closes the
        // window when it is done and this test needs the SAME window a second time for its other
        // tab.
        window.Show();
        using (var smd = RenderedScreenshot.CaptureWindow(window, "swept-gateway-disk-smd"))
        {
            Assert.NotNull(smd);
        }

        // The floppy tab carries the second AddAction button, and a tab that is never
        // selected is never drawn.
        // The TabControl carries no Name, so it is found by walking the tree rather than
        // by adding a name to the markup purely for a test.
        TabControl? tabs = null;
        foreach (var child in window.GetLogicalDescendants())
        {
            if (child is TabControl found) { tabs = found; break; }
        }

        Assert.NotNull(tabs);
        if (tabs!.ItemCount > 1)
        {
            tabs.SelectedIndex = 1;
            var shot = RenderedScreenshot.CaptureWindow(window, "swept-gateway-disk-floppy");
            Assert.NotNull(shot);
            shot!.Dispose();
        }

        window.Close();
    }

    [AvaloniaFact]
    public void TheFileTransferProgressWindowRenders()
    {
        var emulator = EmulatorFactory.CreateEmulator("VT100", 80, 24, 100);
        var session = new TerminalSession(emulator, "render check");
        RenderAndSave(
            new FileTransferProgressWindow(session, TransferDirection.Send),
            "swept-file-transfer");
    }

    /// <summary>
    /// A control rather than a window, so it is hosted in one the size it is used at.
    /// </summary>
    [AvaloniaFact]
    public void TheVirtualKeyboardPanelRenders()
    {
        var panel = new VirtualKeyboardPanel();
        var host = new Window { Width = 1100, Height = 420, Content = panel };
        RenderAndSave(host, "swept-virtual-keyboard");
    }
}
