using System;
using RetroTerm.Core.Terminal.Graphics;
using Xunit;

namespace RetroTerm.Tests.Terminal.Graphics;

/// <summary>
/// ReGIS macrographs - store a command string under a letter, replay it by name.
/// </summary>
/// <remarks>
/// <para><b>Where the rules come from</b></para>
/// The "Macrographs" chapter of
/// <c>spec\DEC\EK-VT3XX-GP-002_VT330_VT340_Graphics_Programming_May88.pdf</c>:
///  - <c>@:letter definition @;</c> defines, <c>@letter</c> invokes, <c>@.</c> clears all 26.
///  - "The VT300 does not draw macrographs when you define them."
///  - "You can nest macrographs up to 16 levels deep. However, a macrograph cannot call itself."
///  - "Selecting an empty macrograph does not cause an error."
///  - Call letters are not case sensitive.
///
/// <para><b>Why these were worth writing</b></para>
/// Before this, <c>@</c> was not a letter so the decoder skipped it as punctuation - which meant a
/// definition drew immediately and an invocation drew nothing, both backwards. The first test here
/// is the one that was red.
/// </remarks>
public class RegisMacrographTests
{
    private static InMemoryGraphicsSurface Surface(int width = 64, int height = 64)
        => new InMemoryGraphicsSurface(width, height);

    private static int LitPixels(InMemoryGraphicsSurface surface)
    {
        int count = 0;
        for (int y = 0; y < surface.Height; y++)
        {
            for (int x = 0; x < surface.Width; x++)
            {
                if (!surface.GetPixel(x, y).IsTransparent) count++;
            }
        }

        return count;
    }

    [Fact]
    public void DefiningAMacrographDrawsNothing()
    {
        var decoder = new RegisDecoder();
        var surface = Surface();

        decoder.Decode("@:A P[0,0] V[40,0] @;".AsSpan(), surface);

        Assert.Equal(0, LitPixels(surface));
    }

    [Fact]
    public void InvokingItDrawsWhatItStored()
    {
        var decoder = new RegisDecoder();
        var stored = Surface();
        var direct = Surface();

        decoder.Decode("@:A P[0,0] V[40,0] @; @A".AsSpan(), stored);
        new RegisDecoder().Decode("P[0,0] V[40,0]".AsSpan(), direct);

        Assert.True(LitPixels(direct) > 0, "the direct drawing drew nothing, so it proves nothing");

        for (int y = 0; y < stored.Height; y++)
        {
            for (int x = 0; x < stored.Width; x++)
            {
                Assert.Equal(direct.GetPixel(x, y), stored.GetPixel(x, y));
            }
        }
    }

    [Fact]
    public void TheCallLetterIsNotCaseSensitive()
    {
        var decoder = new RegisDecoder();
        var surface = Surface();

        decoder.Decode("@:a P[0,0] V[40,0] @; @A".AsSpan(), surface);

        Assert.True(LitPixels(surface) > 0);
    }

    [Fact]
    public void InvokingAnEmptySlotIsNotAnError()
    {
        // "Selecting an empty macrograph does not cause an error."
        var decoder = new RegisDecoder();
        var surface = Surface();

        decoder.Decode("@Z P[0,0] V[40,0]".AsSpan(), surface);

        Assert.True(LitPixels(surface) > 0, "the command after the empty invocation must still run");
    }

    [Fact]
    public void AnEmptyDefinitionClearsThatSlot()
    {
        // "@; clears the selected macrographs by specifying a blank definition."
        var decoder = new RegisDecoder();
        var surface = Surface();

        decoder.Decode("@:A P[0,0] V[40,0] @;".AsSpan(), surface);
        decoder.Decode("@:A@;".AsSpan(), surface);
        decoder.Decode("@A".AsSpan(), surface);

        Assert.Equal(0, LitPixels(surface));
    }

    [Fact]
    public void ClearAllEmptiesEverySlot()
    {
        var decoder = new RegisDecoder();
        var surface = Surface();

        decoder.Decode("@:A P[0,0] V[40,0] @; @:B P[0,10] V[40,10] @;".AsSpan(), surface);
        decoder.Decode("@.".AsSpan(), surface);
        decoder.Decode("@A @B".AsSpan(), surface);

        Assert.Equal(0, LitPixels(surface));
    }

    [Fact]
    public void OneMacrographCanCallAnother()
    {
        var decoder = new RegisDecoder();
        var surface = Surface();

        decoder.Decode("@:A P[0,0] V[40,0] @;".AsSpan(), surface);
        decoder.Decode("@:B @A @;".AsSpan(), surface);
        decoder.Decode("@B".AsSpan(), surface);

        Assert.True(LitPixels(surface) > 0);
    }

    [Fact]
    public void AMacrographThatCallsItselfStopsRatherThanRunningForever()
    {
        // "a macrograph cannot call itself". Without the busy flag this test does not fail, it
        // hangs - which is why the rule is enforced rather than trusted.
        var decoder = new RegisDecoder();
        var surface = Surface();

        decoder.Decode("@:A P[0,0] V[40,0] @A @;".AsSpan(), surface);
        decoder.Decode("@A".AsSpan(), surface);

        Assert.True(LitPixels(surface) > 0, "the part before the self-call must still draw");
    }

    [Fact]
    public void ARedefinitionReplacesTheOldContents()
    {
        var decoder = new RegisDecoder();
        var first = Surface();
        var second = Surface();

        decoder.Decode("@:A P[0,0] V[40,0] @; @A".AsSpan(), first);
        decoder.Decode("@:A P[0,20] V[40,20] @; @A".AsSpan(), second);

        // The redefined body draws on row 20 and the old one on row 0. If the old contents had
        // survived, row 0 would be lit in the second surface too.
        Assert.True(!second.GetPixel(20, 20).IsTransparent, "the new definition did not draw");
        Assert.True(second.GetPixel(20, 0).IsTransparent, "the old definition drew as well");
        Assert.True(!first.GetPixel(20, 0).IsTransparent, "the first definition did not draw");
    }

    [Fact]
    public void AnAtSignInsideAQuotedStringDoesNotEndTheDefinition()
    {
        // "ReGIS does not recognize any commands in a quoted text string", so the terminator has to
        // be looked for outside quotes. The T command is not implemented, but it must not be able
        // to cut a definition short either.
        var decoder = new RegisDecoder();
        var surface = Surface();

        decoder.Decode("@:A T'@;' P[0,0] V[40,0] @; @A".AsSpan(), surface);

        Assert.True(LitPixels(surface) > 0, "the vector after the quoted text was lost");
    }

    [Fact]
    public void AnUnterminatedDefinitionDoesNotDrawAndDoesNotThrow()
    {
        // A truncated stream is a normal thing to receive, not a reason to fall over.
        var decoder = new RegisDecoder();
        var surface = Surface();

        decoder.Decode("@:A P[0,0] V[40,0]".AsSpan(), surface);

        Assert.Equal(0, LitPixels(surface));
    }
}
