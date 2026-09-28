using System.Text;
using Avalonia.Headless.XUnit;
using RetroTerm.Core.Configuration;
using RetroTerm.Core.Terminal.Emulators;
using SkiaSharp;
using Xunit;

namespace RetroTerm.Tests.Avalonia;

/// <summary>
/// Whether the 21-pixel diamond cursor can still be found once the screen is full of drawing.
/// </summary>
/// <remarks>
/// <para><b>The question this answers</b></para>
/// M6.5d asks whether the diamond - "This cursor is a 21 x 21 pixel diamond", so 21 pixels on an
/// 800 by 480 screen - can be found on a busy drawing. Every existing test puts it on a screen
/// holding one empty box, where anything at all is easy to see. That is not the case anybody
/// worried about.
///
/// <para><b>Why this measures the DIFFERENCE rather than the colour</b></para>
/// The obvious test - the cursor is drawn in full white, so count white pixels - does not work,
/// and finding out why is worth writing down. The ReGIS plane is a fixed 800 by 480 and it is
/// SCALED DOWN into the text area, which on an 80 by 24 screen is smaller than that. A one-pixel
/// white line therefore lands on less than one screen pixel and is averaged with whatever is
/// behind it, so it arrives as a light grey that no threshold can tell from register 7. The first
/// draft of this file asserted white at the four vertices and failed on a screen where the PNG
/// plainly shows the diamond.
///
/// Rendering the same drawing twice, once with the cursor up and once without, and comparing the
/// two, has none of that trouble. Whatever blending does to the line it does to both frames, so
/// every pixel that differs is the cursor and nothing else. That turns "can it be found" into two
/// counts:
///  - the changed pixels sit in one compact cluster the size of the diamond, at the point.
///  - nothing anywhere else on the screen changed.
///
/// <para><b>This is not the whole of M6.5d</b></para>
/// Whether it is comfortable to AIM with on a real monitor is a judgement, and it stays Ronny's.
/// What this settles is that the shape reaches the screen intact over the densest drawing, marks
/// only its own point, and draws BRIGHTER than what it covers. The PNG is written so the
/// judgement can be made from it.
/// </remarks>
[Collection("Avalonia")]
public class RegisDiamondOnABusyDrawingTests
{
    /// <summary>
    /// The escape character, built from its code so no raw control byte lands in this file.
    /// </summary>
    private const char Escape = (char)0x1B;

    /// <summary>
    /// The string terminator's backslash, built from its code for the same reason.
    /// </summary>
    private const char Backslash = (char)0x5C;

    /// <summary>
    /// Where the cursor is put, in ReGIS screen coordinates.
    /// </summary>
    /// <remarks>
    /// Chosen to sit in the middle of the densest part of the drawing rather than in a clear
    /// margin, which would make the case easier than the real one.
    /// </remarks>
    private const int CursorX = 400;

    /// <summary>
    /// See <see cref="CursorX"/>.
    /// </summary>
    private const int CursorY = 240;

    /// <summary>
    /// Half the diamond's width, so the distance from its centre to any vertex.
    /// </summary>
    private const int Reach = TerminalEmulatorBase.RegisDiamondSize / 2;

    /// <summary>
    /// A screen with a great deal on it, in several colours, and no white.
    /// </summary>
    /// <returns>
    /// The ReGIS commands that draw it.
    /// </returns>
    /// <remarks>
    /// Built out of the things a real ReGIS program draws: a ruled grid, a fan of diagonals from
    /// one corner, a nest of circles, and a run of text. The colours are taken from the default map
    /// by register number. Register 7, the pale grey, is used too: it is the entry that was once
    /// tried for the cursor itself and rejected for being too close to the background, so having it
    /// on screen is the hardest case for telling the cursor apart.
    /// </remarks>
    private static string BusyDrawing()
    {
        var regis = new StringBuilder();

        regis.Append("S(E)");

        // A ruled grid across the whole screen, every 40 pixels.
        regis.Append("W(I3)");
        for (int x = 0; x <= 800; x += 40)
        {
            regis.Append("P[").Append(x).Append(",0]V[").Append(x).Append(",479]");
        }
        for (int y = 0; y <= 479; y += 40)
        {
            regis.Append("P[0,").Append(y).Append("]V[799,").Append(y).Append(']');
        }

        // A fan of diagonals out of the top left corner.
        regis.Append("W(I1)");
        for (int x = 0; x <= 800; x += 50)
        {
            regis.Append("P[0,0]V[").Append(x).Append(",479]");
        }

        // A nest of circles centred on the cursor's own point, so the busiest part of the picture
        // is exactly where the cursor has to be found.
        regis.Append("W(I2)");
        for (int r = 15; r <= 200; r += 15)
        {
            regis.Append("P[").Append(CursorX).Append(',').Append(CursorY).Append("]C[+")
                 .Append(r).Append(']');
        }

        regis.Append("W(I7)");
        for (int r = 22; r <= 200; r += 15)
        {
            regis.Append("P[").Append(CursorX).Append(',').Append(CursorY).Append("]C[+")
                 .Append(r).Append(']');
        }

        // And some text, because a real screen has some.
        regis.Append("W(I6)P[60,440]T'BUSY DRAWING'");

        return regis.ToString();
    }

    /// <summary>
    /// Builds a VT340 and draws the busy screen on it.
    /// </summary>
    /// <param name="raiseTheDiamond">
    /// True to enter graphics input mode with the diamond cursor selected.
    /// </param>
    /// <returns>
    /// The terminal, ready to be rendered.
    /// </returns>
    /// <remarks>
    /// The style is chosen BEFORE the R(I0) that enters the mode. One-shot graphics input buffers
    /// everything the host sends after it, so a style selected afterwards would not arrive until
    /// the mode had already ended. That is the manual's behaviour, not a defect.
    /// </remarks>
    private static TerminalEmulatorBase BusyScreen(bool raiseTheDiamond)
    {
        var emulator = EmulatorFactory.CreateEmulator("VT340", 80, 24, 100);

        var stream = new StringBuilder();
        stream.Append(Escape).Append("P1p").Append(BusyDrawing());
        stream.Append("P[").Append(CursorX).Append(',').Append(CursorY).Append(']');

        if (raiseTheDiamond)
        {
            stream.Append("S(C(I1))R(I0)");
        }

        stream.Append(Escape).Append(Backslash);

        emulator.ProcessData(Encoding.ASCII.GetBytes(stream.ToString()));
        return emulator;
    }

    /// <summary>
    /// Where the pixels that the cursor changed are, and how many there are.
    /// </summary>
    private readonly struct ChangedRegion
    {
        /// <summary>
        /// How many pixels differ between the two frames.
        /// </summary>
        public readonly int Count;

        /// <summary>
        /// Leftmost changed column.
        /// </summary>
        public readonly int Left;

        /// <summary>
        /// Rightmost changed column.
        /// </summary>
        public readonly int Right;

        /// <summary>
        /// Topmost changed row.
        /// </summary>
        public readonly int Top;

        /// <summary>
        /// Bottommost changed row.
        /// </summary>
        public readonly int Bottom;

        /// <summary>
        /// How many of the changed pixels got BRIGHTER when the cursor went up.
        /// </summary>
        public readonly int Brighter;

        /// <summary>
        /// Records one measurement.
        /// </summary>
        /// <param name="count">
        /// How many pixels differ.
        /// </param>
        /// <param name="left">
        /// Leftmost changed column.
        /// </param>
        /// <param name="right">
        /// Rightmost changed column.
        /// </param>
        /// <param name="top">
        /// Topmost changed row.
        /// </param>
        /// <param name="bottom">
        /// Bottommost changed row.
        /// </param>
        /// <param name="brighter">
        /// How many got brighter.
        /// </param>
        public ChangedRegion(int count, int left, int right, int top, int bottom, int brighter)
        {
            Count = count;
            Left = left;
            Right = right;
            Top = top;
            Bottom = bottom;
            Brighter = brighter;
        }
    }

    /// <summary>
    /// Compares two frames of the same size and reports where they differ.
    /// </summary>
    /// <param name="without">
    /// The drawing with no cursor on it.
    /// </param>
    /// <param name="with">
    /// The same drawing with the cursor up.
    /// </param>
    /// <returns>
    /// The changed pixels, their bounding box, and how many grew brighter.
    /// </returns>
    private static ChangedRegion Compare(RenderedScreenshot without, RenderedScreenshot with)
    {
        int count = 0;
        int brighter = 0;
        int left = int.MaxValue;
        int right = int.MinValue;
        int top = int.MaxValue;
        int bottom = int.MinValue;

        for (int y = 0; y < with.Height; y++)
        {
            for (int x = 0; x < with.Width; x++)
            {
                SKColor before = without.PixelAt(x, y);
                SKColor after = with.PixelAt(x, y);

                // A generous tolerance, so that nothing but the cursor is ever counted. Anything
                // the cursor really drew moves a channel far further than this.
                if (RenderedScreenshot.ApproximatelyEqual(before, after, 12))
                {
                    continue;
                }

                count++;
                if (x < left) left = x;
                if (x > right) right = x;
                if (y < top) top = y;
                if (y > bottom) bottom = y;

                int sumBefore = before.Red + before.Green + before.Blue;
                int sumAfter = after.Red + after.Green + after.Blue;
                if (sumAfter > sumBefore) brighter++;
            }
        }

        return new ChangedRegion(count, left, right, top, bottom, brighter);
    }

    [AvaloniaFact]
    public void TheDiamondMarksItsOwnPointAndNothingElseOnTheScreen()
    {
        using var without = RenderedScreenshot.Capture(BusyScreen(false),
            "regis-diamond-busy-without-cursor");
        using var with = RenderedScreenshot.Capture(BusyScreen(true),
            "regis-diamond-on-a-busy-drawing");

        var changed = Compare(without, with);

        Assert.True(changed.Count > 0, "the cursor changed nothing - it did not draw at all");

        double scaleX = (double)with.Width / TerminalEmulatorBase.GraphicsPlaneWidth;
        double scaleY = (double)with.Height / TerminalEmulatorBase.GraphicsPlaneHeight;

        // The diamond's own box, with a pixel of margin each way for the scaling.
        int expectedLeft = (int)((CursorX - Reach) * scaleX) - 2;
        int expectedRight = (int)((CursorX + Reach) * scaleX) + 2;
        int expectedTop = (int)((CursorY - Reach) * scaleY) - 2;
        int expectedBottom = (int)((CursorY + Reach) * scaleY) + 2;

        // Nothing outside the diamond changed. This is the whole finding question: a cursor that
        // also disturbed the drawing elsewhere would be one more thing to hunt through.
        Assert.True(changed.Left >= expectedLeft,
            "something changed left of the diamond, at column " + changed.Left);
        Assert.True(changed.Right <= expectedRight,
            "something changed right of the diamond, at column " + changed.Right);
        Assert.True(changed.Top >= expectedTop,
            "something changed above the diamond, at row " + changed.Top);
        Assert.True(changed.Bottom <= expectedBottom,
            "something changed below the diamond, at row " + changed.Bottom);
    }

    [AvaloniaFact]
    public void TheDiamondReachesItsFullWidthAndHeightOverTheDrawing()
    {
        // The shape half. A diamond composited UNDER the picture, or clipped by it, would still
        // change SOME pixels and pass the test above while showing as a few specks. Its changed
        // region has to span the full 21 pixels each way.
        using var without = RenderedScreenshot.Capture(BusyScreen(false),
            "regis-diamond-busy-without-cursor");
        using var with = RenderedScreenshot.Capture(BusyScreen(true),
            "regis-diamond-on-a-busy-drawing");

        var changed = Compare(without, with);

        double scaleX = (double)with.Width / TerminalEmulatorBase.GraphicsPlaneWidth;
        double scaleY = (double)with.Height / TerminalEmulatorBase.GraphicsPlaneHeight;

        // Two pixels short of the full span is allowed, because the plane is scaled down and the
        // outermost point of each vertex can fall between samples.
        int expectedWidth = (int)(TerminalEmulatorBase.RegisDiamondSize * scaleX) - 2;
        int expectedHeight = (int)(TerminalEmulatorBase.RegisDiamondSize * scaleY) - 2;

        Assert.True(changed.Right - changed.Left >= expectedWidth,
            "the diamond is only " + (changed.Right - changed.Left) + " pixels wide, expected at "
            + "least " + expectedWidth);
        Assert.True(changed.Bottom - changed.Top >= expectedHeight,
            "the diamond is only " + (changed.Bottom - changed.Top) + " pixels tall, expected at "
            + "least " + expectedHeight);
    }

    [AvaloniaFact]
    public void TheDiamondDrawsBrighterThanTheDrawingItCovers()
    {
        // Why it can be found at all. The cursor is full white and the drawing under it is a
        // colour-map entry, so every pixel the cursor touches must come out brighter than it was.
        // A cursor that DARKENED what it covered would be a hole in the picture rather than a
        // mark on it, and over the pale register 7 circles it would be invisible.
        using var without = RenderedScreenshot.Capture(BusyScreen(false),
            "regis-diamond-busy-without-cursor");
        using var with = RenderedScreenshot.Capture(BusyScreen(true),
            "regis-diamond-on-a-busy-drawing");

        var changed = Compare(without, with);

        Assert.True(changed.Count > 0, "the cursor changed nothing - it did not draw at all");
        Assert.Equal(changed.Count, changed.Brighter);
    }
}
