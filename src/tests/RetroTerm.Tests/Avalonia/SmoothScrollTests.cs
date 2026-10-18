using System.Text;
using Avalonia.Headless.XUnit;
using RetroTerm.Core.Configuration;
using RetroTerm.Core.Terminal.Emulators;
using RetroTerm.Desktop.Controls;
using Xunit;

namespace RetroTerm.Tests.Avalonia;

/// <summary>
/// DECSCLM - private mode 4, smooth scrolling.
/// </summary>
/// <remarks>
/// <para><b>The shape of the feature, because it decides what can be tested</b></para>
/// The buffer scrolls INSTANTLY. Only the picture of it lags: each frame the whole screen is drawn a
/// few pixels lower and the offset counts down to zero, so the picture appears to climb into place.
/// That separation is what keeps every existing test about scrolling free of timing - the data model
/// has no idea any of this is happening.
/// <para><b>What is tested here</b></para>
/// That the buffer is untouched by the animation, that the row above the top is available to fill
/// the gap a sliding screen leaves, and that the pixels really do move. Whether the SPEED feels
/// right is not something a test can answer and is not claimed here.
/// <para><b>What is not tested, and why</b></para>
/// The timer is not driven by waiting. Sleeping a test into passing is forbidden in this project and
/// would prove nothing anyway; the animation step is exercised by rendering at a known offset
/// instead, which is what the timer would have produced.
/// </remarks>
[Collection("Avalonia")]
public class SmoothScrollTests
{
    /// <summary>
    /// The escape character, built from its code so no raw control byte lands in this file.
    /// </summary>
    private const char Escape = (char)0x1B;

    private readonly ITestOutputHelper _output;

    public SmoothScrollTests(ITestOutputHelper output)
    {
        _output = output;
    }

    /// <summary>
    /// A VT340 with smooth scrolling on and enough text to have scrolled.
    /// </summary>
    /// <param name="rows">
    /// How many lines to print. More than the screen holds, so the top ones scroll off.
    /// </param>
    /// <returns>
    /// The emulator.
    /// </returns>
    private static TerminalEmulatorBase ScrolledVt340(int rows)
    {
        var emulator = EmulatorFactory.CreateEmulator("VT340", 80, 24, 100);
        emulator.ProcessData(Encoding.ASCII.GetBytes(Escape + "[?25l"));
        emulator.ProcessData(Encoding.ASCII.GetBytes(Escape + "[?4h"));

        for (int i = 0; i < rows; i++)
        {
            emulator.ProcessData(Encoding.ASCII.GetBytes("LINE " + i + "\r\n"));
        }

        // LET THE PICTURE CATCH UP, which is what a running view does between slides. Without this
        // the frame is still resolved several lines back - the emulator counts every scrolled line
        // as one the view owes, and nothing here has been animating to work them off. These tests
        // are about the settled screen, so they start from a settled one.
        while (emulator.TryBeginSmoothScrollLine())
        {
        }

        // AND REPUBLISH. Claiming the lines only moves the counter; the frame everyone reads was
        // captured during the last ProcessData, when the picture was still seventeen lines behind.
        // Leaving it there showed a screen still sitting on its very first line, which is what the
        // rendered PNG showed when this was missed.
        emulator.RequestFrame();

        return emulator;
    }

    [Fact]
    public void TheModeIsAnsweredRatherThanCounted()
    {
        var emulator = EmulatorFactory.CreateEmulator("VT340", 80, 24, 100);

        emulator.ProcessData(Encoding.ASCII.GetBytes(Escape + "[?4h"));

        Assert.True(emulator.SmoothScrollMode);
        Assert.False(emulator.UnrecognisedSequences.ContainsKey("private mode 4 set"));
    }

    [Fact]
    public void TheBufferStillScrollsInstantly()
    {
        // THE MOST IMPORTANT TEST IN THIS FILE. If smooth scrolling ever delayed the buffer, every
        // existing test about scrolling would become timing-dependent and the emulator would start
        // lying to the host about where the cursor is.
        var smooth = ScrolledVt340(40);
        var jump = EmulatorFactory.CreateEmulator("VT340", 80, 24, 100);

        jump.ProcessData(Encoding.ASCII.GetBytes(Escape + "[?25l"));
        for (int i = 0; i < 40; i++)
        {
            jump.ProcessData(Encoding.ASCII.GetBytes("LINE " + i + "\r\n"));
        }

        Assert.Equal(jump.Cursor.Row, smooth.Cursor.Row);

        var smoothBuffer = smooth.GetBuffer();
        var jumpBuffer = jump.GetBuffer();
        for (int col = 0; col < 8; col++)
        {
            Assert.Equal(jumpBuffer.GetCell(0, col).Codepoint, smoothBuffer.GetCell(0, col).Codepoint);
        }
    }

    [Fact]
    public void TheFrameCarriesTheLineThatJustScrolledOff()
    {
        // The gap a sliding screen leaves at the top has to be filled with something, and what
        // belongs there is the line that just left. Without it the animation is a blank band
        // climbing the screen, which reads as a flicker rather than as scrolling.
        var emulator = ScrolledVt340(40);

        emulator.RequestFrame();
        var frame = emulator.LatestFrame;

        Assert.NotNull(frame);
        Assert.True(frame!.HasRowAboveTop,
            "smooth scrolling is on and lines have scrolled, so the frame should carry the row above the top");

        var text = new StringBuilder();
        for (int col = 0; col < 8; col++)
        {
            uint codepoint = frame.GetCellAboveTop(col).Codepoint;
            if (codepoint != 0) text.Append((char)codepoint);
        }

        Assert.StartsWith("LINE", text.ToString());
    }

    [Fact]
    public void AnOrdinaryFrameDoesNotCarryIt()
    {
        // A terminal nobody asked to smooth-scroll must not pay for the extra row.
        var emulator = EmulatorFactory.CreateEmulator("VT340", 80, 24, 100);
        for (int i = 0; i < 40; i++)
        {
            emulator.ProcessData(Encoding.ASCII.GetBytes("LINE " + i + "\r\n"));
        }

        emulator.RequestFrame();

        Assert.False(emulator.LatestFrame!.HasRowAboveTop);
    }

    [Fact]
    public void TheRowAboveTheTopIsTheLineBeforeTheTopLine()
    {
        // Off-by-one is the obvious way to get this wrong, and it would be invisible during a
        // hundred-millisecond slide - the wrong line would flash past and nobody would catch it.
        var emulator = ScrolledVt340(40);
        emulator.RequestFrame();
        var frame = emulator.LatestFrame!;

        var above = new StringBuilder();
        var top = new StringBuilder();
        for (int col = 0; col < 10; col++)
        {
            uint a = frame.GetCellAboveTop(col).Codepoint;
            uint t = frame[0, col].Codepoint;
            if (a != 0 && a != ' ') above.Append((char)a);
            if (t != 0 && t != ' ') top.Append((char)t);
        }

        // The lines are numbered, so the one above the top must be numbered one lower.
        int aboveNumber = int.Parse(above.ToString().Substring(4));
        int topNumber = int.Parse(top.ToString().Substring(4));

        Assert.Equal(topNumber - 1, aboveNumber);
    }

    /// <summary>
    /// Puts a slide part-way through, by writing the canvas's own offset.
    /// </summary>
    /// <param name="canvas">
    /// The canvas to set up.
    /// </param>
    /// <param name="fractionOfACell">
    /// How far through the slide to sit, 0 being finished and 1 being just started.
    /// </param>
    /// <remarks>
    /// Reflection, deliberately, and for the same reason <see cref="RenderedScreenshot"/> reaches
    /// the renderer that way: adding a test-only setter to a production control is worse than a
    /// documented reach-in, and TerminalCanvas should stay unaware that tests exist.
    ///
    /// The CANVAS field and not the renderer property, because the canvas assigns that property
    /// from this field on every frame. Setting the renderer's copy looks like it works and is
    /// silently overwritten - which is exactly what the first version of this test did.
    /// </remarks>
    private static void PutSlidePartWayThrough(TerminalCanvas canvas, double fractionOfACell)
    {
        var rendererField = typeof(TerminalCanvas).GetField("_renderer",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        var renderer = rendererField?.GetValue(canvas) as RetroTerm.Desktop.Rendering.TerminalRenderer;
        Assert.NotNull(renderer);

        var offsetField = typeof(TerminalCanvas).GetField("_smoothScrollOffset",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(offsetField);

        offsetField!.SetValue(canvas, renderer!.GetCharHeight() * fractionOfACell);
    }

    [AvaloniaFact]
    public void ShiftingTheScreenActuallyMovesThePixels()
    {
        // The animation, exercised at a known offset rather than by waiting for a timer. Sleeping a
        // test into passing is forbidden here and would prove nothing anyway.
        var emulator = ScrolledVt340(40);

        var settled = RenderedScreenshot.Capture(emulator, "smooth-scroll-settled");
        var shifted = RenderedScreenshot.Capture(emulator, "smooth-scroll-mid-slide",
            configureCanvas: canvas => PutSlidePartWayThrough(canvas, 0.5));

        _output.WriteLine("settled: " + settled.SavedPath);
        _output.WriteLine("mid-slide: " + shifted.SavedPath);

        // COMPARED AGAINST THE SETTLED SCREEN, not against "is anything drawn". The first version of
        // this test asked whether the shifted image contained any pixel unlike its own top-left
        // corner, which is true of every screen with text on it - so it passed while the screen had
        // not moved at all, and only opening the PNG showed that.
        int differing = 0;
        for (int y = 0; y < settled.Height && differing < 100; y++)
        {
            for (int x = 0; x < settled.Width; x++)
            {
                if (!RenderedScreenshot.ApproximatelyEqual(settled.PixelAt(x, y), shifted.PixelAt(x, y), 8))
                {
                    differing++;
                    if (differing >= 100) break;
                }
            }
        }

        Assert.True(differing >= 100,
            "shifting the screen by half a character cell changed almost nothing - only " + differing +
            " pixels differ from the settled screen, so the slide is not reaching the picture.");
    }

    [AvaloniaFact]
    public void TheLineThatScrolledOffFillsTheGapAtTheTop()
    {
        // Without this the slide is a blank band climbing the screen. The row above the top is drawn
        // at a negative Y, so at half a cell of shift its BOTTOM half is on screen above the first
        // full line - and that is what makes it read as a line leaving rather than as a flicker.
        var emulator = ScrolledVt340(40);

        var shifted = RenderedScreenshot.Capture(emulator, "smooth-scroll-gap-filled",
            configureCanvas: canvas => PutSlidePartWayThrough(canvas, 0.5));

        _output.WriteLine("gap: " + shifted.SavedPath);

        // The very top band of the picture must have ink in it. With no row above the top it would
        // be empty background, because the real first line has been pushed half a cell down.
        int inkInTopBand = 0;
        for (int y = 0; y < 3; y++)
        {
            for (int x = 0; x < shifted.Width; x++)
            {
                if (!RenderedScreenshot.ApproximatelyEqual(shifted.PixelAt(x, y), shifted.PixelAt(0, 0), 8))
                {
                    inkInTopBand++;
                }
            }
        }

        Assert.True(inkInTopBand > 0,
            "the top band of a mid-slide screen is empty, so the line that scrolled off is not " +
            "being drawn and the animation is a blank band climbing the screen");
    }
}
