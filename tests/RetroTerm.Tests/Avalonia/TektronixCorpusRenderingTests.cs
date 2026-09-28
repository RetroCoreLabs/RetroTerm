using System;
using System.IO;
using Avalonia.Headless.XUnit;
using RetroTerm.Core.Terminal.Emulators.TDV;
using Xunit;

namespace RetroTerm.Tests.Avalonia;

/// <summary>
/// The gnuplot fixtures drawn on the real renderer, saved so a human can look at them.
///
/// The conformance tests beside these check that a plot draws SOMETHING and that the axis labels
/// are not printed as punctuation. Neither can answer the only question that matters in the end —
/// does a sin curve come out looking like a sin curve. That is a judgement, and the point of these
/// is to produce the picture it is made against.
///
/// PNGs land in <c>tests\RetroTerm.Tests\Avalonia\images\rendered\</c>.
/// </summary>
[Collection("Avalonia")]
public class TektronixCorpusRenderingTests
{
    private static string CorpusFolder => Path.GetFullPath(Path.Combine(
        Path.GetDirectoryName(typeof(TektronixCorpusRenderingTests).Assembly.Location) ?? "",
        "..", "..", "..", "Conformance", "tektronix"));

    /// <summary>
    /// Builds a terminal with the graphics plane shown and the fixture played into it.
    /// </summary>
    private static TDV2200Emulator Plot(string fixture)
    {
        var emulator = new TDV2200Emulator(80, 24);

        // ESC "17h - show the graphics plane. A real host sends this during setup; the fixtures
        // start straight in at the drawing because gnuplot assumes a terminal that is already a
        // Tektronix rather than one that has to be switched into being one.
        emulator.ProcessData(new byte[] { 0x1B, (byte)'"', (byte)'1', (byte)'7', (byte)'h' });
        emulator.ProcessData(File.ReadAllBytes(Path.Combine(CorpusFolder, fixture)));

        return emulator;
    }

    /// <summary>
    /// A real Tektronix 4014's alpha screen is 74 columns by 35 lines. Our default TDV geometry is
    /// 80x24, which is why a plot's axis labels crowd: 780 graphics units over 24 rows puts the
    /// x-axis labels on the last row, where the LF after each one scrolls the screen.
    /// </summary>
    private static TDV2200Emulator PlotAtTekGeometry(string fixture)
    {
        var emulator = new TDV2200Emulator(74, 35);
        emulator.ProcessData(new byte[] { 0x1B, (byte)'"', (byte)'1', (byte)'7', (byte)'h' });
        emulator.ProcessData(File.ReadAllBytes(Path.Combine(CorpusFolder, fixture)));
        return emulator;
    }

    [AvaloniaFact]
    public void ASinCurveAtTheRealTerminalGeometry()
    {
        // The same plot on a screen the shape the host was written for. Saved to be looked at:
        // whether the labels sit beside their tick marks is a judgement, not an assertion.
        var emulator = PlotAtTekGeometry("sin.tek40xx");

        using var shot = RenderedScreenshot.Capture(emulator, "tek-corpus-sin-74x35", saveZoom: 1);

        Assert.True(emulator.Graphics!.HasAnythingToDraw());
    }

    [AvaloniaFact]
    public void ASinCurve()
    {
        var emulator = Plot("sin.tek40xx");

        using var shot = RenderedScreenshot.Capture(emulator, "tek-corpus-sin", saveZoom: 1);

        Assert.True(emulator.Graphics!.HasAnythingToDraw());
    }

    [AvaloniaFact]
    public void ABoxDrawnParametrically()
    {
        var emulator = Plot("box.tek40xx");

        using var shot = RenderedScreenshot.Capture(emulator, "tek-corpus-box", saveZoom: 1);

        Assert.True(emulator.Graphics!.HasAnythingToDraw());
    }

    [AvaloniaFact]
    public void ACosineDrawnAsPoints()
    {
        // gnuplot draws "with points" as a move and a tiny mark per sample rather than by entering
        // point-plot mode, which is why this fixture has 260 GS in it against the sin curve's 60.
        var emulator = Plot("points.tek40xx");

        using var shot = RenderedScreenshot.Capture(emulator, "tek-corpus-points", saveZoom: 1);

        Assert.True(emulator.Graphics!.HasAnythingToDraw());
    }
}
