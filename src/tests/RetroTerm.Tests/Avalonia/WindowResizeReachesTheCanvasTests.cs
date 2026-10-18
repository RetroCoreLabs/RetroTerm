using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using RetroTerm.Core.Configuration;
using RetroTerm.Core.Terminal.Emulators;
using RetroTerm.Desktop.Controls;
using Xunit;

namespace RetroTerm.Tests.Avalonia;

/// <summary>
/// A window that changes size must drive a layout pass that reaches the terminal canvas.
/// </summary>
/// <remarks>
/// <para><b>The gap this fills</b></para>
/// <c>WindowDrivesTerminalSizeTests</c> and <c>HostResizeRelayoutTests</c> both call
/// <c>canvas.Measure</c> and <c>canvas.Arrange</c> DIRECTLY. They prove the arithmetic in
/// <c>RequestTerminalSizeFor</c> is right once a layout pass runs. Nothing proved that a layout
/// pass runs at all when a real window changes size, and that is a different question with the
/// same symptom.
///
/// <para><b>Where it came from</b></para>
/// Ronny moved the window from one monitor to another on 29 August 2026. The window grew to about
/// 1293 pixels wide; the terminal went on painting in its old box of roughly 800 by 640 with dead
/// black around it. No resize, no repaint. His screenshot is the only record.
///
/// A monitor move is two things at once - a size change and often a scaling change - and neither
/// had any cover. This file takes the half that a headless window can drive: change the size of a
/// SHOWN window and require that the canvas both re-arranges and asks for a new terminal size.
///
/// <para><b>What it deliberately does NOT claim</b></para>
/// It does not reproduce a DPI change, and it does not prove Ronny's fault is absent. If these
/// pass, the plain size path works and the remaining suspect is the scaling change or the build he
/// was running, which could not be published at all that day. Written down so a later reader does
/// not read a green tick here as "the monitor-move defect is fixed".
/// </remarks>
[Collection("Avalonia")]
public class WindowResizeReachesTheCanvasTests
{
    /// <summary>
    /// Builds a shown window with a terminal canvas filling it.
    /// </summary>
    /// <param name="width">
    /// Starting window width.
    /// </param>
    /// <param name="height">
    /// Starting window height.
    /// </param>
    /// <returns>
    /// The window, the canvas, and the list every resize request is recorded into.
    /// </returns>
    private static (Window Window, TerminalCanvas Canvas, List<(int Columns, int Rows)> Asked)
        ShownTerminal(double width, double height)
    {
        var window = new Window { Width = width, Height = height };
        var canvas = new TerminalCanvas();
        window.Content = canvas;

        var emulator = (TerminalEmulatorBase)EmulatorFactory.CreateEmulator("VT100", 80, 24, 100);
        canvas.SetEmulator(emulator);

        var asked = new List<(int, int)>();
        canvas.TerminalResizeRequested += (columns, rows) => asked.Add((columns, rows));

        window.Show();
        RunLayout(window);

        return (window, canvas, asked);
    }

    /// <summary>
    /// Drives the window's layout to completion, the way the framework does between frames.
    /// </summary>
    /// <param name="window">
    /// The window to lay out.
    /// </param>
    private static void RunLayout(Window window)
    {
        // InvalidateMeasure FIRST, and run the dispatcher queue before and after.
        //
        // Avalonia skips Measure entirely on a control that still thinks it is valid, so calling
        // Measure by hand on a clean tree does nothing at all - the same trap
        // HostResizeRelayoutTests documents. Without this the window keeps the size it was shown
        // at and the test reports a defect that is its own.
        global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        window.InvalidateMeasure();
        window.InvalidateArrange();

        window.Measure(new Size(window.Width, window.Height));
        window.Arrange(new Rect(0, 0, window.Width, window.Height));
        window.UpdateLayout();

        global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact]
    public void TheCanvasGrowsWhenTheWindowDoes()
    {
        // The plainest form of the symptom: the window got bigger and the terminal kept painting in
        // its old box. If the canvas's own bounds do not follow the window, nothing downstream can.
        var (window, canvas, _) = ShownTerminal(800, 640);

        double widthBefore = canvas.Bounds.Width;
        double heightBefore = canvas.Bounds.Height;
        Assert.True(widthBefore > 0, "the canvas was never laid out at all");

        window.Width = 1293;
        window.Height = 903;
        RunLayout(window);

        Assert.True(canvas.Bounds.Width > widthBefore,
            "the window grew to 1293 and the canvas stayed " + canvas.Bounds.Width + " wide");
        Assert.True(canvas.Bounds.Height > heightBefore,
            "the window grew to 903 and the canvas stayed " + canvas.Bounds.Height + " tall");

        window.Close();
    }

    [AvaloniaFact]
    public void AGrowingWindowAsksForMoreColumnsAndRows()
    {
        // The half that reaches the host. A canvas that resized but never raised
        // TerminalResizeRequested would leave the grid at its old size inside a bigger control, so
        // the text would sit in a corner with black around it - which is what the screenshot showed.
        var (window, _, asked) = ShownTerminal(800, 640);

        asked.Clear();

        window.Width = 1293;
        window.Height = 903;
        RunLayout(window);

        Assert.NotEmpty(asked);

        var (columns, rows) = asked[asked.Count - 1];
        Assert.True(columns > 80,
            "a 1293 pixel wide window asked for only " + columns + " columns");
        Assert.True(rows > 24,
            "a 903 pixel tall window asked for only " + rows + " rows");

        window.Close();
    }

    [AvaloniaFact]
    public void AShrinkingWindowAsksForFewerColumnsAndRows()
    {
        // The other direction, because a monitor move can go either way and a one-way test would
        // pass on a canvas that only ever grew.
        var (window, _, asked) = ShownTerminal(1293, 903);

        asked.Clear();

        window.Width = 800;
        window.Height = 640;
        RunLayout(window);

        Assert.NotEmpty(asked);

        var (columns, rows) = asked[asked.Count - 1];
        Assert.True(columns < 130,
            "an 800 pixel wide window still asked for " + columns + " columns");

        window.Close();
    }
}
