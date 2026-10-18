using System.Text;
using Avalonia.Headless.XUnit;
using RetroTerm.Core.Configuration;
using RetroTerm.Core.Terminal.Buffer;
using RetroTerm.Core.Terminal.Emulators;
using RetroTerm.Core.Terminal.Emulators.Tektronix;
using RetroTerm.Desktop.Controls;
using Xunit;

namespace RetroTerm.Tests.Avalonia;

/// <summary>
/// A terminal that draws has ONE beam, so its vectors are the colour of its text.
///
/// Until now the plot colour was a constant in the graphics code that happened to match the
/// renderer's default green. Pick an amber theme and the text went amber while the plot stayed
/// green — which no single-phosphor machine could have done, and which was invisible in every test
/// because nothing compared the two.
///
/// Sixel is deliberately exempt: an image carries its own colours and they belong to the host.
/// </summary>
[Collection("Avalonia")]
public class GraphicsFollowTheThemeTests
{
    private static readonly (byte R, byte G, byte B) Amber = (255, 176, 0);
    private static readonly (byte R, byte G, byte B) AmberBackground = (10, 8, 0);

    private static void Feed(TerminalEmulatorBase emulator, string data)
        => emulator.ProcessData(Encoding.ASCII.GetBytes(data));

    [AvaloniaFact]
    public void ATektronixPlotIsDrawnInThePhosphorTheScreenUses()
    {
        var canvas = new TerminalCanvas();
        var emulator = new Tek4014Emulator(20, 6);
        canvas.SetEmulator(emulator);

        canvas.SetTheme(TerminalTheme.Monochrome("Amber", Amber, AmberBackground));

        // GS, then a line across the middle of the space.
        emulator.ProcessData(new byte[] { 0x1D, 0x30, 0x60, 0x30, 0x40, 0x38, 0x68, 0x38, 0x48 });

        var colour = emulator.Plotter.DrawColour;
        Assert.Equal(255, colour.R);
        Assert.Equal(176, colour.G);
        Assert.Equal(0, colour.B);
    }

    [AvaloniaFact]
    public void AndSoIsAnNdDrawing()
    {
        var canvas = new TerminalCanvas();
        var emulator = EmulatorFactory.CreateEmulator("TDV2200", 20, 6, 100);
        canvas.SetEmulator(emulator);

        canvas.SetTheme(TerminalTheme.Monochrome("Amber", Amber, AmberBackground));

        var module = ((RetroTerm.Core.Terminal.Emulators.TDV.TDV2200Emulator)emulator).GraphicsModule;
        Assert.NotNull(module);
        Assert.Equal(255, module!.DrawColour.R);
        Assert.Equal(176, module.DrawColour.G);
        Assert.Equal(0, module.DrawColour.B);
    }

    [AvaloniaFact]
    public void ASixelImageKeepsItsColoursInTheDATA_ButNotOnAOneGunSCREEN()
    {
        // THIS TEST USED TO ASSERT THE OPPOSITE, and the reasoning was wrong. It said an image is
        // data from the host rather than something the terminal's beam drew, so the phosphor should
        // leave it alone. But the sixteen TEXT colours are equally the host's, and a single-phosphor
        // screen collapses those - because a one-gun display cannot show blue whatever anyone asks
        // for. Two rules for the same question is one rule too many.
        //
        // The consistent one: the HOST decides what colour it asks for, the SCREEN decides how that
        // is displayed. The image keeps its own colours in the plane, so switching back to a colour
        // theme shows them again; only the display collapses.
        var canvas = new TerminalCanvas();
        var emulator = EmulatorFactory.CreateEmulator("VT340", 20, 6, 100);
        canvas.SetEmulator(emulator);

        Feed(emulator, "\x1b[?25l\x1b[1;1H");
        Feed(emulator, "\x1bPq#1;2;0;0;100!8~\x1b\\");

        // The data still says blue.
        emulator.Graphics!.Composite();
        var stored = emulator.Graphics.Output.GetPixel(0, 0);
        Assert.True(stored.B > stored.R, "the plane should hold the colour the host sent");

        // The one-gun screen shows amber.
        using var mono = RenderedScreenshot.Capture(emulator, "sixel-on-one-gun-screen",
            configureRenderer: r => r.SetTheme(TerminalTheme.Monochrome("Amber", Amber, AmberBackground)));
        var monoInk = mono.BrightestColorInCell(0, 0);
        Assert.True(monoInk.Red >= monoInk.Blue, $"an amber gun cannot draw blue; got {monoInk}");

        // And a colour screen shows the blue again, from the same unchanged data.
        using var colour = RenderedScreenshot.Capture(emulator, "sixel-on-colour-screen",
            configureRenderer: r => r.SetTheme(TerminalTheme.Colour("Amber", Amber, AmberBackground)));
        var colourInk = colour.BrightestColorInCell(0, 0);
        Assert.True(colourInk.Blue > colourInk.Red, $"a colour screen shows what was sent; got {colourInk}");
    }
}
