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
/// The pointer wired to the host, driven through the real control.
///
/// The Core tests say what bytes a mouse event turns into. These say that a real click on a real
/// canvas produces one at all, and - the part that is a decision rather than a fact - that holding
/// SHIFT keeps the gesture for the terminal so text can still be selected out of a full-screen
/// program.
/// </summary>
[Collection("Avalonia")]
public class MousePointerWiringTests
{
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
    private static void TrackWithSgr(VT100Emulator emulator)
    {
        emulator.ProcessData(Encoding.ASCII.GetBytes("\x1b[?1000h"));
        emulator.ProcessData(Encoding.ASCII.GetBytes("\x1b[?1006h"));
    }

    [AvaloniaFact]
    public void AClickReachesTheHostWhenItIsTracking()
    {
        var (window, canvas, emulator, replies) = BuildTerminal();
        TrackWithSgr(emulator);

        window.MouseDown(new Point(0, 0), global::Avalonia.Input.MouseButton.Left);

        // Top-left cell is column 1, row 1 - the wire format counts from one.
        Assert.Equal("\x1b[<0;1;1M", replies.ToString());
        window.Close();
    }

    [AvaloniaFact]
    public void ReleasingReportsTheButtonThatCameUp()
    {
        var (window, canvas, emulator, replies) = BuildTerminal();
        TrackWithSgr(emulator);

        window.MouseDown(new Point(0, 0), global::Avalonia.Input.MouseButton.Right);
        window.MouseUp(new Point(0, 0), global::Avalonia.Input.MouseButton.Right);

        // Button 2 both times, and the release is the lower-case final byte. The button that came
        // up is not in the pointer's current state by then - it comes from the event's own record.
        Assert.Equal("\x1b[<2;1;1M\x1b[<2;1;1m", replies.ToString());
        window.Close();
    }

    [AvaloniaFact]
    public void ShiftKeepsTheGestureForSelection()
    {
        // THE decision, made the way xterm, PuTTY and gnome-terminal all make it. Without an
        // override there would be no way to copy text out of a program that tracks the mouse.
        var (window, canvas, emulator, replies) = BuildTerminal();
        TrackWithSgr(emulator);

        window.MouseDown(new Point(0, 0), global::Avalonia.Input.MouseButton.Left,
            RawInputModifiers.Shift);

        Assert.Equal("", replies.ToString());
        window.Close();
    }

    [AvaloniaFact]
    public void NothingIsSentWhenTheHostIsNotTracking()
    {
        // The default for every session that never asked. A terminal that sent mouse bytes unasked
        // would drop control codes into whatever the host was reading.
        var (window, canvas, _, replies) = BuildTerminal();

        window.MouseDown(new Point(0, 0), global::Avalonia.Input.MouseButton.Left);
        window.MouseUp(new Point(0, 0), global::Avalonia.Input.MouseButton.Left);

        Assert.Equal("", replies.ToString());
        window.Close();
    }

    [AvaloniaFact]
    public void MovementIsReportedOnlyInTheModesThatAskForIt()
    {
        var (window, canvas, emulator, replies) = BuildTerminal();
        TrackWithSgr(emulator);

        // Mode 1000 wants presses and releases, not movement.
        window.MouseMove(new Point(20, 20));
        Assert.Equal("", replies.ToString());

        // 1003 wants everything, including a wander with no button held.
        emulator.ProcessData(Encoding.ASCII.GetBytes("\x1b[?1003h"));
        window.MouseMove(new Point(0, 0));

        Assert.Equal("\x1b[<35;1;1M", replies.ToString());
        window.Close();
    }
}
