using RetroTerm.Core.Terminal.Graphics;
using Xunit;

namespace RetroTerm.Tests.Terminal.Graphics;

/// <summary>
/// ReGIS replace writing - the <c>W(R)</c> style, and the <c>S(I)</c> background it draws with.
/// </summary>
/// <remarks>
/// <para><b>The one writing style that is not foreground-only</b></para>
/// The table on printed page 56 gives each style a "part of pattern memory affected". Overlay,
/// complement and erase all say "Foreground only" - the pattern's 0 bits do nothing. Replace says
/// "Foreground and background", so the 0 bits write the background instead of leaving the pixel
/// alone.
///
/// The visible difference is what happens to whatever was already there: a dashed line in replace
/// mode ERASES the gaps between its dashes, while the same line in overlay mode lets what is under
/// them show through. That is the whole test below.
///
/// hackerb9's <c>registest.sh</c> opens its grid with <c>W(I7RV)</c>, so a real host does send it -
/// though that particular one selects overlay again immediately afterwards.
/// </remarks>
public class RegisReplaceWritingTests
{
    /// <summary>
    /// Runs some ReGIS on a small surface and hands the surface back.
    /// </summary>
    /// <param name="commands">
    /// The ReGIS to run.
    /// </param>
    /// <returns>
    /// The surface it drew on.
    /// </returns>
    private static InMemoryGraphicsSurface Draw(string commands)
    {
        var surface = new InMemoryGraphicsSurface(40, 20);
        new RegisDecoder().Decode(commands, surface);
        return surface;
    }

    /// <summary>
    /// The colour the default map holds in one location.
    /// </summary>
    /// <param name="index">
    /// Output map location.
    /// </param>
    /// <returns>
    /// The colour.
    /// </returns>
    private static GraphicsColor MapColour(int index) => new GraphicsColorMap().Register(index);

    [Fact]
    public void OverlayWritingLeavesTheGapsOfAPatternAlone()
    {
        // The control. A solid line in code 15, then a dashed line in code 1 over it: with overlay
        // writing the gaps still show the first line.
        var surface = Draw("W(I15)P[0,5]V[19,5]W(V P4(M1) I1)P[0,5]V[19,5]");

        Assert.Equal(MapColour(1).Value, surface.GetPixel(0, 5).Value);
        Assert.Equal(MapColour(15).Value, surface.GetPixel(1, 5).Value);
    }

    [Fact]
    public void ReplaceWritingPutsTheBackgroundInTheGaps()
    {
        // The same drawing with replace writing. Now the gaps are background - code 0 - so the line
        // underneath is gone. This is the only writing style that can remove it.
        var surface = Draw("W(I15)P[0,5]V[19,5]W(R P4(M1) I1)P[0,5]V[19,5]");

        Assert.Equal(MapColour(1).Value, surface.GetPixel(0, 5).Value);
        Assert.Equal(MapColour(0).Value, surface.GetPixel(1, 5).Value);
    }

    [Fact]
    public void ReplaceWritingUsesTheBackgroundIntensityThatWasSelected()
    {
        // "Both options have the same basic format, but start with different command key letters
        // (W for write command, S for screen command)." So S(I2) makes the background red, and the
        // gaps come out red rather than black.
        var surface = Draw("S(I2)W(I15)P[0,5]V[19,5]W(R P4(M1) I1)P[0,5]V[19,5]");

        Assert.Equal(MapColour(2).Value, surface.GetPixel(1, 5).Value);
    }

    [Fact]
    public void SelectingAnotherStyleTurnsReplaceOff()
    {
        // The styles are one setting, not four flags. W(I7RV) - which registest.sh really sends -
        // must end up in overlay, because V came last.
        var surface = Draw("W(I15)P[0,5]V[19,5]W(I7RV)W(P4(M1) I1)P[0,5]V[19,5]");

        Assert.Equal(MapColour(15).Value, surface.GetPixel(1, 5).Value);
    }

    [Fact]
    public void ReplaceWritingStillHonoursThePlaneMask()
    {
        // The background half of a replace write is a write like any other, so the plane mask has
        // to apply to it too. With only plane 0 selected, writing background code 0 into a pixel
        // holding code 15 clears that one bit and leaves 14.
        var surface = Draw("W(I15)P[0,5]V[19,5]W(F1 R P4(M1) I1)P[0,5]V[19,5]");

        Assert.Equal(MapColour(14).Value, surface.GetPixel(1, 5).Value);
    }

    [Fact]
    public void ASolidPatternHasNoGapsToReplace()
    {
        // Replace writing with the power-on all-on pattern is indistinguishable from overlay -
        // there are no 0 bits. Worth pinning: it is what makes W(R) safe on an unpatterned drawing.
        var replace = Draw("W(R I1)P[0,5]V[19,5]");
        var overlay = Draw("W(V I1)P[0,5]V[19,5]");

        for (int x = 0; x < 20; x++)
        {
            Assert.Equal(overlay.GetPixel(x, 5).Value, replace.GetPixel(x, 5).Value);
        }
    }

    [Fact]
    public void ReplaceWritingIsNoLongerCountedAsUnhandled()
    {
        var surface = new InMemoryGraphicsSurface(40, 20);
        var decoder = new RegisDecoder();

        decoder.Decode("W(I7R)", surface);

        Assert.False(decoder.UnhandledCommands.ContainsKey('W'));
    }

    [Fact]
    public void TheBackgroundIntensityOptionIsNotCountedAsUnhandled()
    {
        var surface = new InMemoryGraphicsSurface(40, 20);
        var decoder = new RegisDecoder();

        decoder.Decode("S(I3)", surface);

        Assert.False(decoder.UnhandledCommands.ContainsKey('S'));
    }
}
