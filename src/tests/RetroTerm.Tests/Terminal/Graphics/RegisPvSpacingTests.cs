using System;
using RetroTerm.Core.Terminal.Graphics;
using Xunit;

namespace RetroTerm.Tests.Terminal.Graphics;

/// <summary>
/// <c>T&lt;digit&gt;</c> - PV spacing, which makes subscripts, superscripts and overstrikes.
/// </summary>
/// <remarks>
/// <para><b>Chapter 7, "PV Spacing - Subscripts, Superscripts, and Overstrikes"</b></para>
/// "In text commands, each PV value defines a movement equal to one half of the defined display
/// cell, in the direction specified. The PV multiplication factor does not affect this movement."
/// The named values are 1 and 2 for superscripts, 6 and 7 for subscripts, 4 for an overstrike
/// where "a 44 value moves the character back over the previous character cell", and 0 which
/// "moves a character forward one-half character cell along the baseline".
/// It accumulates and it persists: "ReGIS uses that PV value for all following text strings, until
/// you change the value. You can return to the original baseline by selecting the PV value for the
/// opposite function."
/// <para><b>Why this file exists rather than an assertion on the grid fixture</b></para>
/// This is what hackerb9's <c>registest.sh</c> uses when it writes <c>T22' 0,479 '</c>, and until
/// the digits were acted on those two labels were drawn below the last row and vanished. It cannot
/// be pinned on that fixture, because the fixture opens with <c>S(E)</c> and fills the whole plane
/// with the background code - every pixel is opaque, so counting ink there measures nothing. A
/// blank surface has no such problem.
/// </remarks>
public class RegisPvSpacingTests
{
    /// <summary>
    /// The topmost and bottommost rows carrying any ink.
    /// </summary>
    /// <param name="surface">
    /// Surface to measure.
    /// </param>
    /// <returns>
    /// First and last lit row, or -1 and -1 when nothing was drawn.
    /// </returns>
    private static (int Top, int Bottom) InkRows(InMemoryGraphicsSurface surface)
    {
        int top = -1;
        int bottom = -1;

        for (int y = 0; y < surface.Height; y++)
        {
            for (int x = 0; x < surface.Width; x++)
            {
                if (surface.GetPixel(x, y).IsTransparent) continue;

                if (top < 0) top = y;
                bottom = y;
                break;
            }
        }

        return (top, bottom);
    }

    /// <summary>
    /// The leftmost column carrying any ink.
    /// </summary>
    /// <param name="surface">
    /// Surface to measure.
    /// </param>
    /// <returns>
    /// The column, or -1 when nothing was drawn.
    /// </returns>
    private static int LeftmostInk(InMemoryGraphicsSurface surface)
    {
        for (int x = 0; x < surface.Width; x++)
        {
            for (int y = 0; y < surface.Height; y++)
            {
                if (!surface.GetPixel(x, y).IsTransparent) return x;
            }
        }

        return -1;
    }

    /// <summary>
    /// Draws one string and reports where its ink landed.
    /// </summary>
    /// <param name="commands">
    /// ReGIS to play on a blank surface.
    /// </param>
    /// <returns>
    /// The surface, ready to measure.
    /// </returns>
    private static InMemoryGraphicsSurface Draw(string commands)
    {
        var surface = new InMemoryGraphicsSurface(200, 200);
        new RegisDecoder().Decode(commands, surface);
        return surface;
    }

    [Fact]
    public void WithoutPvSpacingTextSitsOnTheBaseline()
    {
        var plain = Draw("W(I15)P[20,100]T'A'");
        var (top, _) = InkRows(plain);

        Assert.True(top >= 0, "nothing was drawn at all");
        Assert.InRange(top, 95, 115);
    }

    [Fact]
    public void TwoSuperscriptStepsLiftTextByAWholeDisplayCell()
    {
        // This is registest.sh's T22 exactly. The default display cell is 20 rows, so two halves
        // lift the text 20 rows.
        var plain = Draw("W(I15)P[20,100]T'A'");
        var lifted = Draw("W(I15)P[20,100]T22'A'");

        var (plainTop, _) = InkRows(plain);
        var (liftedTop, _) = InkRows(lifted);

        Assert.Equal(plainTop - 20, liftedTop);
    }

    [Fact]
    public void OneSuperscriptStepLiftsByHalfACell()
    {
        var plain = Draw("W(I15)P[20,100]T'A'");
        var lifted = Draw("W(I15)P[20,100]T2'A'");

        var (plainTop, _) = InkRows(plain);
        var (liftedTop, _) = InkRows(lifted);

        Assert.Equal(plainTop - 10, liftedTop);
    }

    [Fact]
    public void ASubscriptDropsTextBelowTheBaseline()
    {
        var plain = Draw("W(I15)P[20,100]T'A'");
        var dropped = Draw("W(I15)P[20,100]T6'A'");

        var (plainTop, _) = InkRows(plain);
        var (droppedTop, _) = InkRows(dropped);

        Assert.Equal(plainTop + 10, droppedTop);
    }

    [Fact]
    public void TheOppositeValueReturnsToTheBaseline()
    {
        // "You can return to the original baseline by selecting the PV value for the opposite
        // function. For example, if you selected superscripting (PV = 2), use subscripting (PV = 6)."
        var plain = Draw("W(I15)P[20,100]T'A'");
        var thereAndBack = Draw("W(I15)P[20,100]T2T6'A'");

        Assert.Equal(InkRows(plain), InkRows(thereAndBack));
    }

    [Fact]
    public void AnOverstrikeMovesBackwards()
    {
        // "A 44 value moves the character back over the previous character cell."
        var plain = Draw("W(I15)P[60,100]T'A'");
        var back = Draw("W(I15)P[60,100]T44'A'");

        int plainLeft = LeftmostInk(plain);
        int backLeft = LeftmostInk(back);

        Assert.True(backLeft < plainLeft, $"overstrike moved to {backLeft}, not left of {plainLeft}");
    }

    [Fact]
    public void ThePvMultiplierDoesNotAffectIt()
    {
        // "The PV multiplication factor does not affect this movement." Without this the same T2
        // would move twenty times as far under W(M20), which is what registest.sh sets elsewhere.
        var plain = Draw("W(I15)P[20,100]T2'A'");
        var multiplied = Draw("W(M20,I15)P[20,100]T2'A'");

        Assert.Equal(InkRows(plain), InkRows(multiplied));
    }

    [Fact]
    public void ItPersistsAcrossFollowingStrings()
    {
        // "ReGIS uses that PV value for all following text strings, until you change the value."
        var surface = Draw("W(I15)P[20,60]T2'A'P[20,120]T'B'");

        var (top, bottom) = InkRows(surface);

        // Both letters are lifted by half a cell, so the gap between them is unchanged at 60 while
        // both sit ten rows higher than they would unshifted.
        var unshifted = Draw("W(I15)P[20,60]T'A'P[20,120]T'B'");
        var (plainTop, plainBottom) = InkRows(unshifted);

        Assert.Equal(plainTop - 10, top);
        Assert.Equal(plainBottom - 10, bottom);
    }
}
