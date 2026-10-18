using System;
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using RetroTerm.Core.Configuration;
using RetroTerm.Core.Protocols.WebSocket.Gateway;
using RetroTerm.Core.Session;
using RetroTerm.Core.Transfer;
using RetroTerm.Desktop.Views;
using Xunit;

namespace RetroTerm.Tests.Avalonia;

/// <summary>
/// No window may open too small for what is inside it, unless it can be resized or something in
/// it scrolls.
///
/// WHY THIS EXISTS. On 9 September 2026 the Gateway Statistics window was found cutting off its
/// own buttons: it declared 480 by 420, its content needs 486, and it can neither be resized nor
/// scrolled - so Reset Counters and Close could not be reached at all. It had been that way since
/// the window was written and nothing went red to say so. It was found by rendering the window and
/// LOOKING at the picture.
///
/// Ronny's standing rule is that a fix gets measured against every case, not only the one that
/// prompted it. This is that measurement, kept as a test so the next window cannot arrive with the
/// same fault.
///
/// THE RULE, and it is deliberately narrow. Content taller or wider than its window is only a
/// defect when the person cannot do anything about it. A resizable window can be dragged bigger,
/// and a ScrollViewer can be scrolled, so both are left alone - a log window whose list is longer
/// than the screen is working exactly as intended. What fails is the combination the person is
/// stuck with: too small, cannot resize, nothing scrolls.
/// </summary>
[Collection("Avalonia")]
public class WindowContentFitsTests
{
    private readonly ITestOutputHelper _output;

    public WindowContentFitsTests(ITestOutputHelper output)
    {
        _output = output;
    }

    /// <summary>
    /// What one window's measurement came to.
    /// </summary>
    private readonly struct Measurement
    {
        public Measurement(string name, double wantedWidth, double wantedHeight,
            double haveWidth, double haveHeight, bool canResize, bool hasScroll,
            bool widthIsAutomatic, bool heightIsAutomatic)
        {
            Name = name;
            WantedWidth = wantedWidth;
            WantedHeight = wantedHeight;
            HaveWidth = haveWidth;
            HaveHeight = haveHeight;
            CanResize = canResize;
            HasScroll = hasScroll;
            WidthIsAutomatic = widthIsAutomatic;
            HeightIsAutomatic = heightIsAutomatic;
        }

        /// <summary>
        /// The window sizes its own width to the content, so it cannot be too narrow.
        /// </summary>
        public bool WidthIsAutomatic { get; }

        /// <summary>
        /// The window sizes its own height to the content, so it cannot be too short.
        /// </summary>
        public bool HeightIsAutomatic { get; }

        public string Name { get; }

        public double WantedWidth { get; }

        public double WantedHeight { get; }

        public double HaveWidth { get; }

        public double HaveHeight { get; }

        public bool CanResize { get; }

        public bool HasScroll { get; }

        /// <summary>
        /// Content bigger than the window, by more than a rounding pixel.
        /// </summary>
        public bool Overflows
            => (!HeightIsAutomatic && WantedHeight > HaveHeight + 1)
               || (!WidthIsAutomatic && WantedWidth > HaveWidth + 1);

        /// <summary>
        /// Overflowing with no way out: cannot be made bigger and nothing scrolls.
        /// </summary>
        public bool IsTrapped => Overflows && !CanResize && !HasScroll;

        public override string ToString()
            => $"{Name}: wants {WantedWidth:F0}x{WantedHeight:F0}, is "
               + (WidthIsAutomatic ? "auto" : HaveWidth.ToString("F0")) + "x"
               + (HeightIsAutomatic ? "auto" : HaveHeight.ToString("F0"))
               + (CanResize ? ", resizable" : ", FIXED SIZE")
               + (HasScroll ? ", scrolls" : ", no scroll");
    }

    /// <summary>
    /// Shows a window and asks its content how big it wants to be.
    /// </summary>
    /// <param name="window">
    /// The window to measure. It is shown here.
    /// </param>
    /// <param name="name">
    /// Name for the report.
    /// </param>
    /// <returns>
    /// The measurement.
    /// </returns>
    private static Measurement Measure(Window window, string name)
    {
        // CLOSED IN THE FINALLY BELOW. This method is called for fifteen windows in one test and
        // used to close none of them. Window teardown is what releases the render and font
        // resources a headless window holds. See SweptWindowRenderTests.RenderAndSave for the
        // measurement that made this worth fixing.
        try
        {
            window.Show();

            // COMPARE AGAINST WHAT THE WINDOW DECLARES, not against Bounds.
            //
            // The headless platform hands a window that has not fixed its own height the screen's
            // 768, so reading Bounds made three windows look 768 tall when their markup says
            // otherwise - a table of numbers that were not the app's. Height and Width carry the
            // declared value, or NaN when the markup sets none, and only then is Bounds the answer.
            var sizing = window.SizeToContent;
            bool heightIsAutomatic = sizing == SizeToContent.Height || sizing == SizeToContent.WidthAndHeight;
            bool widthIsAutomatic = sizing == SizeToContent.Width || sizing == SizeToContent.WidthAndHeight;

            double haveWidth = double.IsNaN(window.Width) ? window.Bounds.Width : window.Width;
            double haveHeight = double.IsNaN(window.Height) ? window.Bounds.Height : window.Height;

            double wantedWidth = haveWidth;
            double wantedHeight = haveHeight;

            if (window.Content is Control content)
            {
                // Height first, at the width the window actually is: nearly every layout here is a
                // column, so the height it needs depends on the width it is given.
                content.Measure(new global::Avalonia.Size(haveWidth, double.PositiveInfinity));
                wantedHeight = content.DesiredSize.Height;

                // Then width, unconstrained, so a row that cannot fit is caught as well.
                content.Measure(new global::Avalonia.Size(double.PositiveInfinity, haveHeight));
                wantedWidth = content.DesiredSize.Width;
            }

            bool hasScroll = false;
            foreach (var child in window.GetLogicalDescendants())
            {
                if (child is ScrollViewer) { hasScroll = true; break; }
            }

            return new Measurement(name, wantedWidth, wantedHeight, haveWidth, haveHeight,
                window.CanResize, hasScroll, widthIsAutomatic, heightIsAutomatic);
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>
    /// Every window that can be built here, measured. The ones needing a gateway listener, a
    /// session or a settings object get one; none of that starts anything.
    /// </summary>
    /// <returns>
    /// One measurement per window.
    /// </returns>
    private List<Measurement> MeasureEveryWindow()
    {
        var results = new List<Measurement>();

        results.Add(Measure(new KermitDebugWindow(), "Kermit Debug"));
        results.Add(Measure(new LogViewerWindow(), "Log Viewer"));
        results.Add(Measure(new ManageConnectionsWindow(), "Manage Connections"));
        results.Add(Measure(new PreferencesWindow(), "Preferences"));
        results.Add(Measure(new ProtocolMonitorWindow(), "Protocol Monitor"));
        results.Add(Measure(new QuickConnectWindow(), "Quick Connect"));
        results.Add(Measure(new OpcomDebugWindow(), "OPCOM Debug"));
        results.Add(Measure(new KeyboardReferenceWindow(), "Keyboard Reference"));
        results.Add(Measure(new ProgrammableKeysDialog(), "Programmable Keys"));

        using (var listener = new GatewayListener())
        {
            results.Add(Measure(new GatewayDebugWindow(listener), "Gateway Debug"));
        }

        using (var listener = new GatewayListener())
        {
            results.Add(Measure(new GatewayStatisticsWindow(listener), "Gateway Statistics"));
        }

        using (var listener = new GatewayListener())
        {
            results.Add(Measure(
                new GatewayDiskConfigWindow(listener, new GatewayDiskSettings()),
                "Gateway Disk Config"));
        }

        results.Add(Measure(new TerminalPopoutWindow(), "Terminal Popout"));
        results.Add(Measure(new VirtualKeyboardWindow(), "Virtual Keyboard"));

        var emulator = EmulatorFactory.CreateEmulator("VT100", 80, 24, 100);
        var session = new TerminalSession(emulator, "fit check");
        results.Add(Measure(
            new FileTransferProgressWindow(session, TransferDirection.Send),
            "File Transfer Progress"));

        return results;
    }

    /// <summary>
    /// Saves the Virtual Keyboard at its own declared 900 by 350.
    ///
    /// It is here because the measurement above says the keyboard wants 1062 across in a window
    /// that declares 900, which reads like the right-hand end being cut off every time it opens.
    /// IT IS NOT. The panel puts its keys inside a Viewbox with uniform stretch, so the whole
    /// keyboard is scaled down to whatever width it is given, and the picture shows every key
    /// present at the declared size - numeric pad, function keys and all.
    ///
    /// So the number is real and the defect is not, which is the whole reason the picture is
    /// taken. Believing the number would have "fixed" a window that was never broken.
    /// </summary>
    [AvaloniaFact]
    public void TheVirtualKeyboardIsSavedAtItsDeclaredSize()
    {
        var window = new VirtualKeyboardWindow();
        try
        {
            window.Show();

            var shot = RenderedScreenshot.CaptureWindow(window, "virtual-keyboard-at-declared-size");
            Assert.NotNull(shot);
            using (shot!)
            {
                _output.WriteLine($"virtual keyboard {shot.Width}x{shot.Height} saved to {shot.SavedPath}");
            }
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void NoFixedSizeWindowOpensTooSmallForItsOwnContent()
    {
        var measured = MeasureEveryWindow();

        // Print all of them, not only the failures. A window sitting one pixel inside its limit is
        // the next one to break, and only the whole table shows that.
        var trapped = new List<string>();
        for (int i = 0; i < measured.Count; i++)
        {
            _output.WriteLine(measured[i].ToString());
            if (measured[i].IsTrapped) trapped.Add(measured[i].ToString());
        }

        Assert.True(trapped.Count == 0,
            "these windows open smaller than their content and can be neither resized nor "
            + "scrolled, so part of them cannot be reached: "
            + string.Join(" | ", trapped));
    }
}
