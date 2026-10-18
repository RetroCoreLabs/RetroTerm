using System.Collections.Generic;
using System.Text;
using Avalonia;
using Avalonia.Headless.XUnit;
using RetroTerm.Core.Configuration;
using RetroTerm.Core.Terminal.Emulators;
using RetroTerm.Desktop.Controls;
using Xunit;

namespace RetroTerm.Tests.Avalonia;

/// <summary>
/// A host that changes the grid size has to make the layout run again.
/// </summary>
/// <remarks>
/// <para><b>The defect this was written for</b></para>
/// Reported by Ronny on 21 August 2026, on a VT340 against a real host: running <c>reset</c> at a
/// Linux shell left the text about twenty columns in from the left edge and it STAYED there.
/// Switching to another tab and back put it right, and a second <c>reset</c> broke it again.
/// <para><b>Why it happened</b></para>
/// <c>reset</c> sends <c>ESC c</c> and then <c>ESC [ ? 3 l</c> - DECCOLM, eighty columns - and
/// <c>ApplyColumnMode</c> really does resize the grid. On a terminal whose size comes from the
/// window that is the wrong width, and <c>RequestTerminalSizeFor</c> already knew how to correct
/// it, but that runs only from <c>ArrangeOverride</c>. Nothing invalidated the layout when the
/// host changed the size, so no arrange happened. The grid stayed narrow, the fitted picture no
/// longer spanned the control, and <c>OnRender</c> centres whatever fits.
/// <para><b>What made it look haunted rather than broken</b></para>
/// Changing tab forced a layout pass, so the correction that should have happened immediately
/// happened later and for an unrelated reason. A defect that fixes itself when you look away is
/// the kind worth a test.
/// </remarks>
[Collection("Avalonia")]
public class HostResizeRelayoutTests
{
    /// <summary>
    /// The escape character, built from its code so no raw control byte lands in this file.
    /// </summary>
    private const char Escape = (char)0x1B;

    /// <summary>
    /// A window comfortably wider than eighty columns, so DECCOLM's eighty cannot fill it.
    /// </summary>
    private const double WideWindow = 1400;

    /// <summary>
    /// Window height, chosen so the fit is limited by height and the narrowed grid really does
    /// leave space at the sides - which is the condition that produces the indent.
    /// </summary>
    private const double WindowHeight = 600;

    /// <summary>
    /// Builds a VT340 canvas and lays it out once, the way a real window would.
    /// </summary>
    /// <param name="canvas">
    /// Receives the canvas.
    /// </param>
    /// <returns>
    /// The emulator behind it.
    /// </returns>
    private static TerminalEmulatorBase LaidOutVt340(out TerminalCanvas canvas)
    {
        var emulator = (TerminalEmulatorBase)EmulatorFactory.CreateEmulator("VT340", 80, 24, 100);

        canvas = new TerminalCanvas();
        canvas.SetEmulator(emulator);

        // The canvas only ASKS; the session does the resizing, and MainWindow wires the two
        // together with tab.Session.ResizeAsync. Standing in for that here is what makes the
        // window actually drive the grid - without it the emulator would sit at its starting
        // eighty columns and the test would prove nothing.
        var target = emulator;
        canvas.TerminalResizeRequested += (columns, rows) => target.Resize(columns, rows);

        // The first pass settles the grid to the window, exactly as opening a tab does.
        canvas.Measure(new Size(WideWindow, WindowHeight));
        canvas.Arrange(new Rect(0, 0, WideWindow, WindowHeight));

        return emulator;
    }

    [AvaloniaFact]
    public void AWideWindowGivesAVt340MoreThanEightyColumns()
    {
        // The premise. If the window did not drive the grid past eighty in the first place, the
        // test below would be asserting nothing - DECCOLM's eighty would already be the right
        // answer and the indent could never appear.
        var emulator = LaidOutVt340(out _);

        Assert.True(emulator.Width > 80,
            "a 1400 pixel window should hold more than eighty columns, got " + emulator.Width);
    }

    [AvaloniaFact]
    public void DecColmAsksForTheLayoutToRunAgain()
    {
        // THE DEFECT ITSELF. After the host narrows the grid, the control must ask for the window's
        // size again. Without the fix nothing invalidates the layout, no arrange runs, and the
        // request never happens - which is the state Ronny was left looking at.
        var emulator = LaidOutVt340(out var canvas);

        var asked = new List<(int Columns, int Rows)>();
        canvas.TerminalResizeRequested += (columns, rows) => asked.Add((columns, rows));

        // What "reset" sends: a hard reset, then DECCOLM to eighty columns.
        emulator.ProcessData(Encoding.ASCII.GetBytes(Escape + "c" + Escape + "[?3l"));

        // The invalidation is posted to the UI thread, so the queue has to be run before the
        // layout will treat itself as dirty. Avalonia skips Measure entirely on a control that
        // still thinks it is valid, which is exactly why calling Measure by hand is not enough -
        // and is the same reason the defect needed a tab switch to clear.
        global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        // The layout pass the invalidation asks for. A real window runs this itself; here it is
        // driven directly rather than waiting on a timer, so the test never sleeps.
        canvas.Measure(new Size(WideWindow, WindowHeight));
        canvas.Arrange(new Rect(0, 0, WideWindow, WindowHeight));

        Assert.NotEmpty(asked);
        Assert.True(asked[asked.Count - 1].Columns > 80,
            "the window should have won and asked for its own width back, got "
            + asked[asked.Count - 1].Columns);
    }

    [AvaloniaFact]
    public void ASettledGridIsNotAskedToResizeAgain()
    {
        // The boundary. Noticing a size change must not turn into asking on every screen update -
        // that would put a resize request behind every character the host prints.
        var emulator = LaidOutVt340(out var canvas);

        var asked = new List<(int Columns, int Rows)>();
        canvas.TerminalResizeRequested += (columns, rows) => asked.Add((columns, rows));

        emulator.ProcessData(Encoding.ASCII.GetBytes("hello"));

        global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        canvas.Measure(new Size(WideWindow, WindowHeight));
        canvas.Arrange(new Rect(0, 0, WideWindow, WindowHeight));

        Assert.Empty(asked);
    }
}
