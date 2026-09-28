using RetroTerm.Core.Terminal.Graphics;
using Xunit;

namespace RetroTerm.Tests.Terminal.Graphics;

/// <summary>
/// Dashed vectors - the surface half of the 4014's Select Vector Patterns.
/// </summary>
/// <remarks>
/// <para><b>What is documented and what is chosen</b></para>
/// The manual NAMES five patterns - solid, dotted, dot-dash, short dash, long dash - and does not
/// print their dash lengths. So the names and the sequences that select them are documented; the
/// sixteen-bit masks are this emulator's reading, marked as such in <see cref="LinePatternMask"/>.
/// These tests therefore pin the SHAPE of the behaviour - a dotted line has gaps, a long dash has
/// longer runs than a short one, the phase carries across segments - rather than exact pixels,
/// because exact pixels here would only be testing the choice against itself.
/// </remarks>
public class LinePatternTests
{
    private static InMemoryGraphicsSurface Surface(int width = 64, int height = 8)
        => new InMemoryGraphicsSurface(width, height);

    private static readonly GraphicsColor Ink = new GraphicsColor(255, 255, 255);

    private static int LitPixelsOnRow(InMemoryGraphicsSurface surface, int row)
    {
        int count = 0;
        for (int x = 0; x < surface.Width; x++)
        {
            if (!surface.GetPixel(x, row).IsTransparent) count++;
        }
        return count;
    }

    /// <summary>
    /// Longest unbroken run of ink on a row.
    /// </summary>
    private static int LongestRun(InMemoryGraphicsSurface surface, int row)
    {
        int best = 0;
        int run = 0;

        for (int x = 0; x < surface.Width; x++)
        {
            if (surface.GetPixel(x, row).IsTransparent)
            {
                run = 0;
                continue;
            }

            run++;
            if (run > best) best = run;
        }

        return best;
    }

    [Fact]
    public void ASolidLineHasNoGaps()
    {
        var surface = Surface();

        surface.DrawLine(0, 0, 63, 0, Ink);

        Assert.Equal(64, LitPixelsOnRow(surface, 0));
    }

    [Fact]
    public void AndTheSolidOverloadAgreesWithThePatternedOne()
    {
        // The five-argument DrawLine is the patterned one with every bit set. If the two walks ever
        // diverged, the tie-break that ALineIsTheSameDrawnFromEitherEnd pins would have two homes.
        var plain = Surface();
        var patterned = Surface();

        plain.DrawLine(3, 1, 60, 6, Ink);

        int phase = 0;
        patterned.DrawLine(3, 1, 60, 6, Ink, LinePattern.Solid, ref phase);

        for (int y = 0; y < plain.Height; y++)
        {
            for (int x = 0; x < plain.Width; x++)
            {
                Assert.Equal(plain.GetPixel(x, y), patterned.GetPixel(x, y));
            }
        }
    }

    [Fact]
    public void ADottedLineIsHalfInk()
    {
        var surface = Surface();
        int phase = 0;

        surface.DrawLine(0, 0, 63, 0, Ink, LinePattern.Dotted, ref phase);

        Assert.Equal(32, LitPixelsOnRow(surface, 0));
        Assert.Equal(1, LongestRun(surface, 0));
    }

    [Fact]
    public void ALongDashHasLongerRunsThanAShortOne()
    {
        // The one thing the names guarantee whatever the exact lengths turn out to be.
        var shortDash = Surface();
        var longDash = Surface();
        int phase = 0;

        shortDash.DrawLine(0, 0, 63, 0, Ink, LinePattern.ShortDash, ref phase);
        phase = 0;
        longDash.DrawLine(0, 0, 63, 0, Ink, LinePattern.LongDash, ref phase);

        Assert.True(LongestRun(longDash, 0) > LongestRun(shortDash, 0),
            "a long dash must draw longer runs than a short dash");
    }

    [Fact]
    public void EveryPatternExceptSolidLeavesGaps()
    {
        LinePattern[] patterns =
        {
            LinePattern.Dotted, LinePattern.DotDash, LinePattern.ShortDash, LinePattern.LongDash,
        };

        for (int i = 0; i < patterns.Length; i++)
        {
            var surface = Surface();
            int phase = 0;

            surface.DrawLine(0, 0, 63, 0, Ink, patterns[i], ref phase);

            int lit = LitPixelsOnRow(surface, 0);
            Assert.True(lit > 0, $"{patterns[i]} drew nothing at all");
            Assert.True(lit < 64, $"{patterns[i]} drew a solid line");
        }
    }

    [Fact]
    public void ThePhaseCarriesFromOneSegmentToTheNext()
    {
        // A dashed polyline is ONE dashed line. Drawn as two halves with the phase carried, it must
        // come out the same as one whole line - otherwise a curve sent as many short vectors gets a
        // dash at every vertex and reads as solid.
        var whole = Surface();
        var halves = Surface();

        int phase = 0;
        whole.DrawLine(0, 0, 63, 0, Ink, LinePattern.LongDash, ref phase);

        phase = 0;
        halves.DrawLine(0, 0, 31, 0, Ink, LinePattern.LongDash, ref phase);
        halves.DrawLine(32, 0, 63, 0, Ink, LinePattern.LongDash, ref phase);

        for (int x = 0; x < 64; x++)
        {
            Assert.Equal(whole.GetPixel(x, 0), halves.GetPixel(x, 0));
        }
    }

    [Fact]
    public void RestartingThePhaseBreaksTheLineUp()
    {
        // The negative of the test above, so it cannot pass by the phase being ignored entirely.
        //
        // SPLIT AT 21, NOT 32. The pattern repeats every sixteen pixels, so a first segment of
        // thirty-two leaves the phase at zero and carrying it is indistinguishable from restarting
        // it - which is how this test first passed while proving nothing.
        var carried = Surface();
        var restarted = Surface();

        int phase = 0;
        carried.DrawLine(0, 0, 20, 0, Ink, LinePattern.LongDash, ref phase);
        carried.DrawLine(21, 0, 63, 0, Ink, LinePattern.LongDash, ref phase);

        phase = 0;
        restarted.DrawLine(0, 0, 20, 0, Ink, LinePattern.LongDash, ref phase);
        phase = 0;
        restarted.DrawLine(21, 0, 63, 0, Ink, LinePattern.LongDash, ref phase);

        bool anyDifference = false;
        for (int x = 0; x < 64; x++)
        {
            if (carried.GetPixel(x, 0) != restarted.GetPixel(x, 0)) anyDifference = true;
        }

        Assert.True(anyDifference, "carrying the phase must change the picture, or it does nothing");
    }

    [Fact]
    public void AnUnknownPatternDrawsSolidRatherThanNothing()
    {
        // A line nobody can see is a worse answer than a line in the wrong style.
        Assert.Equal(0xFFFF, LinePatternMask.For((LinePattern)999));
    }
}
