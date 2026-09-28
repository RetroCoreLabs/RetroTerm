using System.Text;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using RetroTerm.Core.Terminal.Emulators;
using RetroTerm.Desktop.Controls;
using Xunit;
using Xunit.Abstractions;

namespace RetroTerm.Tests.Avalonia;

/// <summary>
/// UI validation through Avalonia's OWN headless test API: a real Window is shown, driven
/// with real simulated input, and its rendered frame is captured with
/// HeadlessWindowExtensions.CaptureRenderedFrame.
///
/// This complements RenderedOutputValidationTests rather than replacing it:
///
///   RenderedScreenshot.Capture       - control pinned to its natural size, so one terminal
///                                      cell is a known block of pixels. Use for "is this
///                                      glyph/attribute/colour drawn correctly".
///   RenderedScreenshot.CaptureWindow - the assembled UI at whatever size the window is,
///                                      including layout and letterboxing. Use for "does
///                                      the app look right" and for input-driven tests.
///
/// Both capture pixels the production renderer actually produced; neither re-implements
/// any drawing.
/// </summary>
[Collection("Avalonia")]
public class WindowRenderValidationTests
{
    private readonly ITestOutputHelper _output;

    public WindowRenderValidationTests(ITestOutputHelper output)
    {
        _output = output;
    }

    /// <summary>
    /// Shows a window containing a real TerminalControl wired to an emulator.
    /// </summary>
    private static (Window window, TerminalControl control, TerminalEmulatorBase emulator) CreateTerminalWindow(
        int cols = 40, int rows = 12, int width = 640, int height = 320)
    {
        var window = new Window { Width = width, Height = height };
        var control = new TerminalControl();
        window.Content = control;
        window.Show();

        var emulator = new VT100Emulator(cols, rows);
        control.SetEmulator(emulator);
        control.Focus();

        return (window, control, emulator);
    }

    [AvaloniaFact]
    public void Window_CaptureRenderedFrame_ProducesAPicture()
    {
        var (window, _, emulator) = CreateTerminalWindow();
        emulator.ProcessData(Encoding.ASCII.GetBytes("RETROTERM WINDOW CAPTURE"));

        var shot = RenderedScreenshot.CaptureWindow(window, "window-capture-basic");

        Assert.NotNull(shot);
        using (shot)
        {
            _output.WriteLine($"window frame {shot!.Width}x{shot.Height}, saved to {shot.SavedPath}");

            Assert.True(shot.Width > 0 && shot.Height > 0, "captured frame had no size");

            // Something other than a flat fill must have been drawn.
            Assert.True(shot.HasAnyPixelDifferentFrom(shot.PixelAt(0, 0)),
                "the whole window frame is a single flat colour - nothing was rendered");
        }
    }

    [AvaloniaFact]
    public void Window_ShowsTextThatWasWrittenToTheEmulator()
    {
        var (blankWindow, _, _) = CreateTerminalWindow();
        var blankShot = RenderedScreenshot.CaptureWindow(blankWindow, "window-blank");
        Assert.NotNull(blankShot);

        var (textWindow, _, textEmulator) = CreateTerminalWindow();
        textEmulator.ProcessData(Encoding.ASCII.GetBytes("HELLO FROM THE HOST"));
        var textShot = RenderedScreenshot.CaptureWindow(textWindow, "window-with-text");
        Assert.NotNull(textShot);

        using (blankShot)
        using (textShot)
        {
            double changed = blankShot!.FractionDifferentFrom(textShot!);
            _output.WriteLine($"text changed {changed:P2} of the window pixels");

            Assert.True(changed > 0.0,
                "writing text to the emulator changed nothing in the rendered window");
        }
    }

    [AvaloniaFact]
    public void TypingIntoTheWindow_ReachesTheHost_AndIsEchoedOnScreen()
    {
        // The full loop through the real UI: simulated keystroke -> TerminalCanvas.OnKeyDown
        // -> keyboard mapper -> InputReceived. Then the "host" echoes it back into the
        // emulator and the window is captured to prove it actually appears on screen.
        var (window, control, emulator) = CreateTerminalWindow();

        var sentToHost = new StringBuilder();
        control.InputReceived += text => sentToHost.Append(text);

        window.KeyTextInput("HI");

        _output.WriteLine($"bytes the terminal sent to the host: \"{sentToHost}\"");
        Assert.Equal("HI", sentToHost.ToString());

        // Echo, the way a real host would
        emulator.ProcessData(Encoding.ASCII.GetBytes(sentToHost.ToString()));

        var shot = RenderedScreenshot.CaptureWindow(window, "window-typed-echo");
        Assert.NotNull(shot);
        using (shot)
        {
            Assert.True(shot!.HasAnyPixelDifferentFrom(shot.PixelAt(0, 0)),
                "the echoed keystrokes did not appear in the window");
        }
    }

    [AvaloniaFact]
    public void CtrlKeyChord_SendsAControlCodeToTheHost()
    {
        // Ctrl+C must reach the host as ETX (0x03), through the real key pipeline.
        var (window, control, _) = CreateTerminalWindow();

        var sentToHost = new StringBuilder();
        control.InputReceived += text => sentToHost.Append(text);

        window.KeyPressQwerty(PhysicalKey.C, RawInputModifiers.Control);

        var sent = sentToHost.ToString();
        _output.WriteLine($"Ctrl+C produced: {(sent.Length == 0 ? "(nothing)" : $"0x{(int)sent[0]:X2}")}");

        Assert.Equal("\x03", sent);
    }

    [AvaloniaFact]
    public void ResizingTheWindow_ChangesTheRenderedFrameSize()
    {
        // The terminal keeps its cols/rows and is scaled to fit, so the frame must follow
        // the window while the content stays present.
        var (window, _, emulator) = CreateTerminalWindow(width: 640, height: 320);
        emulator.ProcessData(Encoding.ASCII.GetBytes("SIZE TEST"));

        var small = RenderedScreenshot.CaptureWindow(window, "window-size-small");
        Assert.NotNull(small);

        window.Width = 900;
        window.Height = 500;

        var large = RenderedScreenshot.CaptureWindow(window, "window-size-large");
        Assert.NotNull(large);

        using (small)
        using (large)
        {
            _output.WriteLine($"small {small!.Width}x{small.Height}, large {large!.Width}x{large.Height}");
            Assert.True(large.Width > small.Width || large.Height > small.Height,
                $"resizing did not change the captured frame ({small.Width}x{small.Height} vs {large.Width}x{large.Height})");
        }
    }
}
