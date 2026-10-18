using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using RetroTerm.Core.Terminal.Emulators;
using RetroTerm.Desktop.Controls;
using Xunit;

namespace RetroTerm.Tests.Avalonia;

/// <summary>
/// Who gets the wheel - the host, the scrollback, or the zoom.
/// </summary>
/// <remarks>
/// <para><b>Why this file exists</b></para>
/// <c>TerminalCanvas.OnPointerWheelChanged</c> arbitrates between three claimants in a fixed order,
/// and until now nothing tested any of it. <c>MousePointerWiringTests</c> covers clicks, releases
/// and movement; the wheel had no cover at all, while carrying the most rules of the three.
///
/// M2.2 names the wheel as part of the case that needs a real host and real hardware - "the wheel
/// scrolls vim's view rather than our scrollback". What vim makes of the bytes still needs vim.
/// Which of the three claimants gets the gesture does not, and that is the part that can regress
/// silently: all three failures look like "the wheel did nothing" or "the wheel did the wrong
/// thing", with no error anywhere.
///
/// <para><b>The order, and why it is that order</b></para>
///  - Control plus the wheel zooms, before anything else looks at it - including a tracking host.
///    Every browser and editor does this, so it is what a reader will try, and a program tracking
///    the mouse must not be able to swallow it.
///  - Then a tracking host gets the wheel, which is how a full-screen program scrolls its own view.
///  - Shift forces the local behaviour whatever the host asked for, so the scrollback is never out
///    of reach. This is the same decision as M2.3's shift-drag, applied to the wheel.
/// </remarks>
[Collection("Avalonia")]
public class MouseWheelWiringTests
{
    /// <summary>
    /// Builds a window with a real canvas and a real emulator, and records what reaches the host.
    /// </summary>
    /// <returns>
    /// The window, the canvas, the emulator, and everything sent to the host so far.
    /// </returns>
    private static (Window Window, TerminalCanvas Canvas, VT100Emulator Emulator, StringBuilder Replies)
        BuildTerminal()
    {
        var window = new Window { Width = 800, Height = 600 };
        var canvas = new TerminalCanvas();
        window.Content = canvas;
        window.Show();

        var emulator = new VT100Emulator(80, 24);
        canvas.SetEmulator(emulator);

        var replies = new StringBuilder();
        emulator.DataToSend += bytes => replies.Append(Encoding.Latin1.GetString(bytes));

        return (window, canvas, emulator, replies);
    }

    /// <summary>
    /// Turns on tracking with the SGR encoding, which is the readable one to assert on.
    /// </summary>
    /// <param name="emulator">
    /// The emulator to put into tracking mode.
    /// </param>
    private static void TrackWithSgr(VT100Emulator emulator)
    {
        emulator.ProcessData(Encoding.ASCII.GetBytes("\x1b[?1000h"));
        emulator.ProcessData(Encoding.ASCII.GetBytes("\x1b[?1006h"));
    }

    [AvaloniaFact]
    public void ATrackingHostGetsTheWheel()
    {
        // How a full-screen program scrolls its own view. Without this the wheel would move OUR
        // scrollback while vim sat still, which is the complaint M2.2 is written to catch.
        var (window, _, emulator, replies) = BuildTerminal();
        TrackWithSgr(emulator);

        window.MouseWheel(new Point(0, 0), new Vector(0, 1));

        // Button 64 is wheel up in the SGR encoding, at the top-left cell, and it is reported as a
        // press - a wheel notch has no release.
        Assert.Equal("\x1b[<64;1;1M", replies.ToString());
        window.Close();
    }

    [AvaloniaFact]
    public void WheelDownIsADifferentButtonFromWheelUp()
    {
        // The two directions must not collapse into one code, which is the sort of thing that
        // makes a program scroll one way only.
        var (window, _, emulator, replies) = BuildTerminal();
        TrackWithSgr(emulator);

        window.MouseWheel(new Point(0, 0), new Vector(0, -1));

        Assert.Equal("\x1b[<65;1;1M", replies.ToString());
        window.Close();
    }

    [AvaloniaFact]
    public void ShiftKeepsTheWheelForOurOwnScrollback()
    {
        // The same decision as M2.3's shift-drag. Without it, a program that tracks the mouse would
        // put the scrollback out of reach entirely, and there would be no way to look back at what
        // scrolled past.
        var (window, _, emulator, replies) = BuildTerminal();
        TrackWithSgr(emulator);

        window.MouseWheel(new Point(0, 0), new Vector(0, 1), RawInputModifiers.Shift);

        Assert.Equal("", replies.ToString());
        window.Close();
    }

    [AvaloniaFact]
    public void ControlPlusTheWheelZoomsEvenWhileTheHostIsTracking()
    {
        // Zoom is decided BEFORE the host is offered the gesture, on purpose: Control plus wheel is
        // what a reader will try without thinking, and it must not stop working inside the one
        // program where the text is hardest to read.
        var (window, canvas, emulator, replies) = BuildTerminal();
        TrackWithSgr(emulator);

        int before = canvas.ZoomPercent;

        window.MouseWheel(new Point(0, 0), new Vector(0, 1), RawInputModifiers.Control);

        Assert.True(canvas.ZoomPercent > before,
            "Control plus the wheel did not zoom in: still at " + canvas.ZoomPercent);

        // And the host saw none of it. A tracking program receiving a wheel report here would
        // scroll its view at the same time as the zoom.
        Assert.Equal("", replies.ToString());
        window.Close();
    }

    [AvaloniaFact]
    public void NothingReachesAHostThatNeverAskedForTheMouse()
    {
        // The default for every session that never turned tracking on. A terminal that sent mouse
        // bytes unasked would drop control codes into whatever the host was reading.
        var (window, _, _, replies) = BuildTerminal();

        window.MouseWheel(new Point(0, 0), new Vector(0, 1));

        Assert.Equal("", replies.ToString());
        window.Close();
    }
}
