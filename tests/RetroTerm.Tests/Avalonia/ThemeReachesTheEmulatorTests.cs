using System.Text;
using Avalonia.Headless.XUnit;
using RetroTerm.Core.Terminal.Buffer;
using RetroTerm.Core.Terminal.Emulators;
using RetroTerm.Desktop.Controls;
using Xunit;

namespace RetroTerm.Tests.Avalonia;

/// <summary>
/// A host can ASK what colours this terminal paints - OSC 10 and OSC 11 - and the answer has to be
/// the colours actually on screen.
///
/// Core has no theme of its own; the renderer owns that. So the emulator can only answer honestly
/// if the canvas tells it what the theme is, and this is the test that the wire exists. Without it
/// the emulator would answer from its built-in defaults, which stop being true the moment anyone
/// picks a different colour preset - and it would go on answering confidently.
/// </summary>
[Collection("Avalonia")]
public class ThemeReachesTheEmulatorTests
{
    [AvaloniaFact]
    public void ApplyingAThemeTellsTheEmulatorWhatItPaints()
    {
        var canvas = new TerminalCanvas();
        var emulator = new VT100Emulator(80, 24);
        canvas.SetEmulator(emulator);

        canvas.SetTheme(TerminalTheme.Colour("Amber", (255, 176, 0), (10, 8, 0)));

        Assert.Equal(((byte)255, (byte)176, (byte)0), emulator.DisplayForeground);
        Assert.Equal(((byte)10, (byte)8, (byte)0), emulator.DisplayBackground);
    }

    [AvaloniaFact]
    public void AndTheHostGetsThatColourBackWhenItAsks()
    {
        // End to end: pick a theme, let a host ask what the background is, read the reply.
        var canvas = new TerminalCanvas();
        var emulator = new VT100Emulator(80, 24);
        canvas.SetEmulator(emulator);

        var replies = new StringBuilder();
        emulator.DataToSend += bytes => replies.Append(Encoding.ASCII.GetString(bytes));

        canvas.SetTheme(TerminalTheme.Monochrome("Amber", (255, 176, 0), (10, 8, 0)));
        emulator.ProcessData(Encoding.ASCII.GetBytes("\x1b]11;?\x1b\\"));

        Assert.Equal("\x1b]11;rgb:0a0a/0808/0000\x1b\\", replies.ToString());
    }
}
