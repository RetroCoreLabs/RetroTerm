using System;
using RetroTerm.Core.Terminal.Graphics;
using Xunit;

namespace RetroTerm.Tests.Terminal.Graphics;

/// <summary>
/// The ReGIS text command (chapter 7) and load command (chapter 8).
/// </summary>
/// <remarks>
/// <para><b>What can be asserted and what cannot</b></para>
/// The SIZES are the manual's, table 7-3, so they can be pinned exactly. The glyph SHAPES are not:
/// the manual prints six example characters and no font, so the shapes come from the TDV2200
/// character ROM as a stand-in. Nothing here asserts what a letter looks like - only that ink
/// lands inside the cell the manual gives, that the cell grows the way the table says, and that
/// the cursor moves by the character positioning value.
///
/// The load command is the opposite: a host defines the pixels, so those CAN be checked exactly.
/// </remarks>
public class RegisTextAndLoadTests
{
    private static InMemoryGraphicsSurface Draw(string commands, int width = 400, int height = 300)
    {
        var surface = new InMemoryGraphicsSurface(width, height);
        var decoder = new RegisDecoder();
        decoder.Decode(commands, surface);
        return surface;
    }

    /// <summary>
    /// Counts pixels that are not transparent.
    /// </summary>
    private static int Ink(InMemoryGraphicsSurface surface)
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

    /// <summary>
    /// The bounding box of everything drawn, as left, top, right, bottom. Right and bottom are
    /// inclusive; a blank surface answers all zeros with <paramref name="any"/> false.
    /// </summary>
    private static (int Left, int Top, int Right, int Bottom) Box(InMemoryGraphicsSurface surface,
        out bool any)
    {
        int left = int.MaxValue, top = int.MaxValue, right = -1, bottom = -1;

        for (int y = 0; y < surface.Height; y++)
        {
            for (int x = 0; x < surface.Width; x++)
            {
                if (surface.GetPixel(x, y).IsTransparent) continue;

                if (x < left) left = x;
                if (y < top) top = y;
                if (x > right) right = x;
                if (y > bottom) bottom = y;
            }
        }

        any = right >= 0;
        return any ? (left, top, right, bottom) : (0, 0, 0, 0);
    }

    [Fact]
    public void AStringDrawsSomething()
    {
        var surface = Draw("P[10,10]T'Hello'");

        Assert.True(Ink(surface) > 0, "T drew nothing at all");
    }

    [Fact]
    public void TheCharacterCellIsTheManualsEightByTen()
    {
        // S0 is asked for by name. It used to be the default and is not: table 7-4 gives the S
        // option a default of 1, with a footnote saying the defaults are "based on standard S1
        // character cell" and a display cell of [9,20]. Starting at S0 drew every label at half
        // height, which is how it was found - the registest grid came out with visibly smaller
        // text than the capture from the real VT340 beside it.
        var surface = Draw("P[100,100]T(S0)'H'");

        var box = Box(surface, out bool any);
        Assert.True(any, "nothing was drawn");

        Assert.True(box.Left >= 100, $"ink starts left of the cursor, at {box.Left}");
        Assert.True(box.Right < 108, $"ink runs past the 8-pixel cell, to {box.Right}");
        Assert.True(box.Top >= 100, $"ink starts above the cursor, at {box.Top}");
        Assert.True(box.Bottom < 110, $"ink runs past the 10-pixel cell, to {box.Bottom}");
    }

    [Theory]
    [InlineData(0, 8, 10)]
    [InlineData(1, 8, 20)]
    [InlineData(2, 16, 30)]
    [InlineData(4, 32, 60)]
    public void EachStandardSizeDrawsIntoItsOwnUnitCell(int size, int expectedWidth, int expectedHeight)
    {
        // Table 7-3. The glyph fills its cell to within a pixel or two, so the box is checked
        // against the cell rather than against an exact shape - the shapes are a stand-in and the
        // sizes are the manual's.
        var surface = Draw($"P[100,100]T(S{size})'H'", 600, 500);

        var box = Box(surface, out bool any);
        Assert.True(any, $"S{size} drew nothing");

        int drawnWidth = box.Right - box.Left + 1;
        int drawnHeight = box.Bottom - box.Top + 1;

        Assert.True(drawnWidth <= expectedWidth,
            $"S{size} drew {drawnWidth} pixels wide; the unit cell is {expectedWidth}");
        Assert.True(drawnHeight <= expectedHeight,
            $"S{size} drew {drawnHeight} pixels tall; the unit cell is {expectedHeight}");

        // ...and it really is bigger than the size below it, or the table is not being read.
        if (size >= 2)
        {
            Assert.True(drawnHeight > 10, $"S{size} is no taller than S0");
        }
    }

    [Fact]
    public void TheCursorMovesByTheCharacterPositioningValue()
    {
        // S0's character positioning is 9. Two characters therefore span 9 pixels plus the width of
        // the second, and the left edge of the second is 9 to the right of the first.
        var one = Draw("P[100,100]T'H'");
        var two = Draw("P[100,100]T'HH'");

        var boxOne = Box(one, out _);
        var boxTwo = Box(two, out _);

        Assert.Equal(boxOne.Left, boxTwo.Left);
        Assert.True(boxTwo.Right >= boxOne.Right + 9,
            $"the second character should start 9 pixels along; the box only grew to {boxTwo.Right}");
    }

    [Fact]
    public void ADisplayCellChangesTheSpacingAndNotTheCharacter()
    {
        // "This option does not change the size of characters."
        var normal = Draw("P[100,100]T'H'");
        var wide = Draw("P[100,100]T(S[40,10])'H'");

        var boxNormal = Box(normal, out _);
        var boxWide = Box(wide, out _);

        Assert.Equal(boxNormal.Right - boxNormal.Left, boxWide.Right - boxWide.Left);
    }

    [Fact]
    public void AUnitCellRoundsDownToTheMultiplesTheManualGives()
    {
        // "The width value must be a positive multiple of 8, the height value must be a positive
        // multiple of 5. ... If you do not use a multiple, ReGIS uses the next smaller size. For
        // example, if you select a height of 38, ReGIS uses 35."
        var state = new RegisTextState();

        state.SetUnitCell(38, 38);
        Assert.Equal(32, state.UnitCellWidth);
        Assert.Equal(35, state.UnitCellHeight);

        state.SetUnitCell(32, 35);
        Assert.Equal(32, state.UnitCellWidth);
        Assert.Equal(35, state.UnitCellHeight);
    }

    [Fact]
    public void TheHeightMultiplierStretchesOnlyTheHeight()
    {
        var plain = Draw("P[100,100]T'H'");
        var tall = Draw("P[100,100]T(H3)'H'", 400, 400);

        var boxPlain = Box(plain, out _);
        var boxTall = Box(tall, out _);

        Assert.Equal(boxPlain.Right - boxPlain.Left, boxTall.Right - boxTall.Left);
        Assert.True(boxTall.Bottom - boxTall.Top > (boxPlain.Bottom - boxPlain.Top) * 2,
            "H3 should be about three times as tall");
    }

    [Fact]
    public void ACarriageReturnGoesBackToWhereTheCommandStarted()
    {
        // "Returns the cursor to the horizontal position where the current text writing command
        // started" - so the second line starts under the first, not under the cursor's last spot.
        var surface = Draw("P[100,100]T'AB\rC'");

        var box = Box(surface, out bool any);
        Assert.True(any);
        Assert.True(box.Left >= 100 && box.Left < 108,
            $"the returned-to character should start at the command's column; ink starts at {box.Left}");
    }

    [Fact]
    public void ALineFeedDropsOneDisplayCell()
    {
        var surface = Draw("P[100,100]T'A\nB'");

        var box = Box(surface, out bool any);
        Assert.True(any);

        // S0's display cell is 10 tall, so the second row sits 10 lower and the box is about 20.
        Assert.True(box.Bottom >= 110, $"the second line should be a display cell lower; box ends at {box.Bottom}");
    }

    [Fact]
    public void ADoubledQuoteIsOneQuoteInsideTheString()
    {
        // "'don''t' appears on the screen as don't" - and, more to the point here, the string does
        // not end at the doubled quote, so what follows is still text rather than a command.
        var surface = Draw("P[100,100]T'don''t'");

        var box = Box(surface, out bool any);
        Assert.True(any);

        // Five characters at 9 apart plus the last one's width.
        Assert.True(box.Right >= 100 + (4 * 9),
            $"the string ended early; ink stops at {box.Right}");
    }

    [Fact]
    public void StringTiltRunsTheTextDownTheScreen()
    {
        // The tilt compass: 270 degrees runs the string downward. What is asserted is the SHAPE of
        // the answer - the text is taller than it is wide - rather than an exact angle, because
        // the compass is eight points and the glyphs are a stand-in.
        var surface = Draw("P[100,100]T(D270)'ABCD'", 400, 400);

        var box = Box(surface, out bool any);
        Assert.True(any, "the tilted string drew nothing");

        Assert.True(box.Bottom - box.Top > box.Right - box.Left,
            "a string tilted to 270 degrees should run further down than across");
    }

    [Fact]
    public void ATemporaryTextControlPutsTheOptionsBackAtItsEnd()
    {
        // T(B ...) ... T(E). "Temporary text controls have specific start and end options", and
        // unlike every other option here their values do NOT remain in effect afterwards.
        //
        // hackerb9's registest.sh - the fixture with real hardware captures beside it - uses this
        // on every label it draws: T(B D180 S1 W(I1V))' ... ' then T(E). Without it the tilt of one
        // label leaks into the next and the picture walks off the screen.
        var decoder = new RegisDecoder();
        var surface = new InMemoryGraphicsSurface(400, 300);

        decoder.Decode("T(S4)", surface);
        int wideCell = decoder.Text.UnitCellWidth;

        decoder.Decode("T(B S1)'small'(E)", surface);

        Assert.Equal(wideCell, decoder.Text.UnitCellWidth);
    }

    [Fact]
    public void AnEndWithNoStartChangesNothing()
    {
        // A confused stream must not be able to wipe the size the host set.
        var decoder = new RegisDecoder();
        var surface = new InMemoryGraphicsSurface(400, 300);

        decoder.Decode("T(S4)", surface);
        int wideCell = decoder.Text.UnitCellWidth;

        decoder.Decode("T(E)", surface);

        Assert.Equal(wideCell, decoder.Text.UnitCellWidth);
    }

    [Fact]
    public void ATemporaryWriteControlDoesNotSetTheTextSize()
    {
        // T(B D180 S1 W(I1V)) - the W group belongs to the WRITE controls, and reading its numbers
        // as text options would take the intensity for a cell size.
        var decoder = new RegisDecoder();
        var surface = new InMemoryGraphicsSurface(400, 300);

        decoder.Decode("T(B D180 S1 W(I1V))'x'(E)", surface);

        // S1's unit cell, not something derived from the I1 inside the write group.
        Assert.Equal(8, decoder.Text.UnitCellWidth);
    }

    // ─────────────────────────────────────────────────────────────
    // The load command - chapter 8
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void ALoadedCellDrawsExactlyTheBitsItWasGiven()
    {
        // A full top row and nothing else: ten rows, the first FF and the rest 00. The call letter
        // is Z, and set 1 is selected to load into and then to draw from.
        var surface = Draw(
            "P[100,100]L(A1)L\"Z\"FF,00,00,00,00,00,00,00,00,00T(A1)'Z'");

        var box = Box(surface, out bool any);
        Assert.True(any, "the loaded cell drew nothing");

        // One STORED row, eight pixels wide, at the cursor - and two screen rows tall, because the
        // default cell is S1 and its unit cell is twenty pixels for ten stored rows. The bits are
        // exactly what was loaded; the height is the size in force, which is the point of keeping
        // the two separate.
        Assert.Equal(100, box.Top);
        Assert.Equal(101, box.Bottom);
        Assert.Equal(100, box.Left);
        Assert.Equal(107, box.Right);
    }

    [Fact]
    public void OneHexDigitFillsTheRightHandFourPixels()
    {
        // "If you use only one hex value or end up with one, ReGIS assumes the first hex value is 0
        // and sets the first 4 bits in the row to 0 (off)."
        var surface = Draw(
            "P[100,100]L(A1)L\"Z\"F,00,00,00,00,00,00,00,00,00T(A1)'Z'");

        var box = Box(surface, out bool any);
        Assert.True(any, "the loaded cell drew nothing");

        Assert.Equal(104, box.Left);
        Assert.Equal(107, box.Right);
    }

    [Fact]
    public void ASetThatWasNeverLoadedDrawsNothingRatherThanAscii()
    {
        // Deliberate: a host that selected an empty set and got the built-in glyphs would never
        // find out it had forgotten to load them.
        var surface = Draw("P[100,100]T(A2)'Hello'");

        Assert.Equal(0, Ink(surface));
    }

    [Fact]
    public void ANamedSetKeepsItsName()
    {
        // For the report command, which is the only thing that reads it back.
        var decoder = new RegisDecoder();
        var surface = new InMemoryGraphicsSurface(100, 100);

        decoder.Decode("L(A2'Hershey')", surface);

        Assert.Equal("Hershey", decoder.Text.NameOf(2));
    }

    [Fact]
    public void ANameLongerThanTenCharactersIsCutToTen()
    {
        var decoder = new RegisDecoder();
        var surface = new InMemoryGraphicsSurface(100, 100);

        decoder.Decode("L(A1'ABCDEFGHIJKLMNOP')", surface);

        Assert.Equal("ABCDEFGHIJ", decoder.Text.NameOf(1));
    }

    [Fact]
    public void SetZeroCannotBeLoaded()
    {
        // "You can select character set 0, but you cannot load it." Loading into 0 must not
        // overwrite the built-in glyphs, so a following string still draws them.
        var surface = Draw(
            "P[100,100]L(A0)L\"H\"00,00,00,00,00,00,00,00,00,00T(A0)'H'");

        Assert.True(Ink(surface) > 0,
            "loading into set 0 blanked the built-in glyphs, which it must not be able to do");
    }

    [Fact]
    public void LoadingDoesNotChangeWhichSetIsBeingDrawnFrom()
    {
        // L(A n) picks the set to LOAD into; T(A n) picks the set to DRAW from. They share the
        // letter and nothing else, and confusing them would make every load silently switch the
        // font under the text that follows.
        var decoder = new RegisDecoder();
        var surface = new InMemoryGraphicsSurface(200, 200);

        decoder.Decode("L(A3)", surface);

        Assert.Equal(3, decoder.Text.LoadingSet);
        Assert.Equal(0, decoder.Text.ActiveSet);
    }
}
