using System.Text;
using Avalonia.Headless.XUnit;
using RetroTerm.Core.Configuration;
using RetroTerm.Core.Terminal.Emulators;
using RetroTerm.Core.Terminal.Printing;
using Xunit;

namespace RetroTerm.Tests.Avalonia;

/// <summary>
/// A graphics print, sent back through the terminal so the page can be LOOKED at.
/// </summary>
/// <remarks>
/// <para><b>Why this exists as well as the round-trip tests</b></para>
/// <c>SixelEncoderTests</c> compares pixels and proves the picture survives. It cannot tell whether
/// the page is the right way up, or whether a rotation went the wrong way round and happened to
/// preserve every pixel. That is a thing to see, and every colour and orientation defect this
/// project has had was found by opening a PNG rather than by an assertion - see the note in
/// CLAUDE.md.
///
/// <para><b>What the pipeline is</b></para>
/// Draw with ReGIS on one terminal, print it as sixel through the real encoder, then feed that dump
/// to a SECOND terminal as an ordinary sixel image and capture the screen. Every step is a real
/// code path; nothing here reimplements anything.
/// </remarks>
[Collection("Avalonia")]
public class GraphicsPrintRenderingTests
{
    private static void Feed(TerminalEmulatorBase emulator, string data)
        => emulator.ProcessData(Encoding.ASCII.GetBytes(data));

    /// <summary>
    /// Draws a picture with an obvious top and an obvious right, so a rotation is visible.
    /// </summary>
    /// <remarks>
    /// <para><b>Why it is drawn in the TOP-RIGHT corner</b></para>
    /// The first version of this drew in the top-left and the rotated page came out BLANK. That was
    /// the test's fault, not the encoder's, and it is worth writing down because the arithmetic is
    /// easy to get backwards. A counter-clockwise turn sends source (sx, sy) to page (sy, W-1-sx),
    /// so the source's LEFT edge lands at the BOTTOM of the page - past the bottom of an 800 by 480
    /// screen plane, and therefore clipped away entirely. Only the source's RIGHT edge lands
    /// anywhere a screen-sized playback can show.
    ///
    /// So: a red bar along the top edge and a blue bar down the right edge, both in the right-hand
    /// end of the picture. After rotation the red bar must run down the LEFT of the page and the
    /// blue bar ACROSS ITS TOP - the two swap roles, which is a thing that can be seen at a glance
    /// and cannot happen if the turn went the other way.
    /// </remarks>
    private static void DrawAFlag(TerminalEmulatorBase emulator)
    {
        Feed(emulator, "\x1bPp" +
            "W(I2)P[500,0]F(V[+299][,+40][-299][,-40])" +      // red bar along the top edge
            "W(I1)P[760,60]F(V[+39][,+180][-39][,-180])" +     // blue bar down the right edge
            "W(I3)P[620,150]F(C[+50])" +                       // a green disc between them
            "\x1b\\");
    }

    /// <summary>
    /// Prints a terminal's graphics and plays the dump back onto a fresh terminal.
    /// </summary>
    private static TerminalEmulatorBase PrintAndPlayBack(TerminalEmulatorBase source)
    {
        var sink = new MemoryPrintSink();
        source.PrintSink = sink;
        Assert.True(source.PrintGraphics(), "nothing was printed, so there is nothing to look at");

        var page = EmulatorFactory.CreateEmulator("VT340", 80, 24, 100);
        Feed(page, "\x1b[?25l\x1b[?80h");     // hide the cursor, park the image at the origin
        Feed(page, sink.ToText());
        return page;
    }

    [AvaloniaFact]
    public void APrintedPageComesBackTheWayItWentIn()
    {
        var source = EmulatorFactory.CreateEmulator("VT340", 80, 24, 100);
        Feed(source, "\x1b[?25l");
        DrawAFlag(source);
        source.GraphicsPrintOptions.Colour = true;

        var page = PrintAndPlayBack(source);

        using var shot = RenderedScreenshot.Capture(page, "print-sixel-upright");

        page.Graphics!.Composite();
        var output = page.Graphics.Output;

        // Red along the top edge, blue down the right edge: the picture as drawn.
        var top = output.GetPixel(650, 20);
        var right = output.GetPixel(780, 150);

        Assert.True(top.R > top.B, $"the top bar should be red; got {top.R},{top.G},{top.B}");
        Assert.True(right.B > right.R,
            $"the right bar should be blue; got {right.R},{right.G},{right.B}");
    }

    [AvaloniaFact]
    public void ARotatedPageTurnsCounterClockwise()
    {
        // DEC STD 070: "the VT240 rotates the image counter clockwise, so that the left side of the
        // paper ... corresponds to the top of the image on the terminal screen. This scanning order
        // was chosen to allow punching holes for a looseleaf notebook on the left side of the page."
        //
        // So the RED bar, drawn across the top of the screen, must come down the LEFT of the page.
        var source = EmulatorFactory.CreateEmulator("VT340", 80, 24, 100);
        Feed(source, "\x1b[?25l");
        DrawAFlag(source);
        source.GraphicsPrintOptions.Colour = true;
        source.GraphicsPrintOptions.Rotated = true;

        var page = PrintAndPlayBack(source);

        using var shot = RenderedScreenshot.Capture(page, "print-sixel-rotated");

        page.Graphics!.Composite();
        var output = page.Graphics.Output;

        // The two bars have swapped roles. The red bar ran along the top of the SCREEN and now runs
        // down the left of the PAGE; the blue bar ran down the right of the screen and now runs
        // across the top of the page. A clockwise turn would put them the other way about, and a
        // picture that failed to rotate at all would leave both where they started.
        var leftEdge = output.GetPixel(30, 200);
        var topEdge = output.GetPixel(300, 20);

        Assert.True(leftEdge.R > leftEdge.B,
            $"the red top bar should now run down the left edge; got {leftEdge.R},{leftEdge.G},{leftEdge.B}");
        Assert.True(topEdge.B > topEdge.R,
            $"the blue right bar should now run across the top; got {topEdge.R},{topEdge.G},{topEdge.B}");

        // THE DISC COMES OUT AS AN ELLIPSE, AND THAT IS CORRECT. "Rotated images are always
        // expanded" - DEC STD 070 - so every column is printed twice and the picture is twice as
        // wide as it is on the screen. It looks like a defect in the PNG and it is not one; this
        // assertion is here so that anyone who "fixes" it finds out immediately.
        int discWidth = 0;
        int discHeight = 0;
        for (int x = 0; x < output.Width; x++)
        {
            var pixel = output.GetPixel(x, 200);
            if (pixel.G > pixel.R && pixel.G > pixel.B) discWidth++;
        }

        for (int y = 0; y < output.Height; y++)
        {
            var pixel = output.GetPixel(300, y);
            if (pixel.G > pixel.R && pixel.G > pixel.B) discHeight++;
        }

        Assert.True(discWidth > discHeight,
            $"a rotated print is always expanded, so the disc must be wider than it is tall; "
            + $"got {discWidth} by {discHeight}");
    }
}
