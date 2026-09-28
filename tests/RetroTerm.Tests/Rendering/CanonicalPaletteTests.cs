using RetroTerm.Core.Terminal.Buffer;
using Xunit;

namespace RetroTerm.Tests.Rendering;

/// <summary>
/// Phase 5 part 1: one 256-colour palette (verified live bug 10).
///
/// There were two, and they disagreed. The renderer built its brushes from the xterm cube ramp
/// (0, 95, 135, 175, 215, 255); TerminalColor computed (idx / 36) * 51, an even six-way split
/// giving 0, 51, 102, 153, 204, 255. The screen showed one colour and Core reported another, so
/// nothing that asked Core for a colour could be made to agree with what was drawn.
///
/// The existing test checked index 16 and index 231 — the two points where the two formulas AGREE
/// (0 and 255). It passed while 214 of the 216 cube colours were wrong. That is the shape to watch
/// for: a test whose sample points are exactly the ones that cannot discriminate.
/// </summary>
public class CanonicalPaletteTests
{
    // ─────────────────────────────────────────────────────────────
    // The cube ramp — where the two tables differed
    // ─────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 95)]
    [InlineData(2, 135)]
    [InlineData(3, 175)]
    [InlineData(4, 215)]
    [InlineData(5, 255)]
    public void TheCubeUsesTheXtermRampNotAnEvenSplit(int level, byte expected)
    {
        // Index of the pure-red entry at this level: 16 + 36*level.
        var (r, g, b) = TerminalPalette.GetRgb((byte)(16 + 36 * level));

        Assert.Equal(expected, r);
        Assert.Equal(0, g);
        Assert.Equal(0, b);
    }

    [Fact]
    public void TheSecondLevelIs95Not51()
    {
        // THE regression, at the single index that states it most plainly. An even split would
        // give 51 here; xterm's first step is deliberately wider so dark colours stay apart
        // from black.
        var (r, _, _) = TerminalPalette.GetRgb(52);   // 16 + 36 = cube (1,0,0)

        Assert.Equal(95, r);
        Assert.NotEqual(51, r);
    }

    [Fact]
    public void TheCubeIsRedMajor()
    {
        // index = 16 + 36*r + 6*g + b. Getting this ordering wrong would swap the axes and still
        // produce a plausible-looking gradient, so it is worth pinning separately from the ramp.
        Assert.Equal((byte)0, TerminalPalette.GetRgb(16 + 1).R);     // b = 1
        Assert.Equal((byte)95, TerminalPalette.GetRgb(16 + 1).B);
        Assert.Equal((byte)95, TerminalPalette.GetRgb(16 + 6).G);    // g = 1
        Assert.Equal((byte)95, TerminalPalette.GetRgb(16 + 36).R);   // r = 1
    }

    [Fact]
    public void TheEndsOfTheCubeAreBlackAndWhite()
    {
        // The two points the old test checked. Kept — they are still true — but no longer the
        // only evidence.
        Assert.Equal(((byte)0, (byte)0, (byte)0), TerminalPalette.GetRgb(16));
        Assert.Equal(((byte)255, (byte)255, (byte)255), TerminalPalette.GetRgb(231));
    }

    // ─────────────────────────────────────────────────────────────
    // The rest of the range
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void TheStandardSixteenAreTheXtermDefaults()
    {
        Assert.Equal(((byte)0, (byte)0, (byte)0), TerminalPalette.GetRgb(0));
        Assert.Equal(((byte)205, (byte)0, (byte)0), TerminalPalette.GetRgb(1));
        Assert.Equal(((byte)229, (byte)229, (byte)229), TerminalPalette.GetRgb(7));
        Assert.Equal(((byte)127, (byte)127, (byte)127), TerminalPalette.GetRgb(8));
        Assert.Equal(((byte)92, (byte)92, (byte)255), TerminalPalette.GetRgb(12));
        Assert.Equal(((byte)255, (byte)255, (byte)255), TerminalPalette.GetRgb(15));
    }

    [Fact]
    public void TheGreyRampRunsFrom8To238InStepsOfTen()
    {
        Assert.Equal(((byte)8, (byte)8, (byte)8), TerminalPalette.GetRgb(232));
        Assert.Equal(((byte)238, (byte)238, (byte)238), TerminalPalette.GetRgb(255));

        for (int i = 232; i <= 255; i++)
        {
            var (r, g, b) = TerminalPalette.GetRgb((byte)i);
            Assert.Equal(r, g);
            Assert.Equal(g, b);
            Assert.Equal((byte)(8 + (i - 232) * 10), r);
        }
    }

    [Fact]
    public void EveryIndexIsCovered()
    {
        // No index falls through to a default. A byte cannot be out of range, so there is no
        // "unknown colour" case left to get wrong.
        for (int i = 0; i <= 255; i++)
        {
            var (r, g, b) = TerminalPalette.GetRgb((byte)i);
            // Index 0 is legitimately black; everything else in the palette that computes to
            // black must be a cube or grey entry, never a fallback.
            if (r == 0 && g == 0 && b == 0)
                Assert.True(i == 0 || i == 16, $"index {i} resolved to black unexpectedly");
        }
    }

    // ─────────────────────────────────────────────────────────────
    // Core and the drawn colour now come from the same place
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void TerminalColorAgreesWithThePaletteAcrossTheWholeRange()
    {
        // The property the two tables violated. Sweeping ALL 256 rather than sampling, because
        // sampling is exactly what let this survive.
        for (int i = 0; i <= 255; i++)
        {
            Assert.Equal(TerminalPalette.GetRgb((byte)i), TerminalColor.FromIndex((byte)i).ToRgb());
        }
    }
}
