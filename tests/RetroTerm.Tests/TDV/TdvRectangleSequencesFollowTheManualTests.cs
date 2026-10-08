using System.Text;
using RetroTerm.Core.Terminal.Buffer;
using RetroTerm.Core.Terminal.Emulators.TDV;
using Xunit;

namespace RetroTerm.Tests.TDV;

/// <summary>
/// NDSAR, NDAAR, NDRAR, NDFC and NDDWA as ND Display Terminal 1200 (ND-12054-1) defines them.
/// BUGS.md B1, fixed 8 October 2026.
/// </summary>
/// <remarks>
/// <para><b>What the manual says, and where</b></para>
/// All four attribute and fill functions take the two corners FIRST and the attributes (or the
/// characters) AFTER: <c>CSI l1 ; c1 ; l2 ; c2 ; a1 ; ... an</c> - NDSAR section 5.50, NDAAR 5.36,
/// NDRAR 5.46, NDFC 5.43. The code used to read an attribute first and then four corners.
/// <para></para>
/// Coordinates start at 1: section 5.41 (NDWA) calls the work area's upper left corner "the home
/// position with coordinates 1,1", and CUP's default is "line 1, position 1" (5.15). The code used
/// to treat them as 0-based.
/// <para></para>
/// NDSAR: "Previously specified aspects within the rectangle shall be reset" (5.50). NDAAR: they
/// "shall remain in effect" (5.36). The attribute numbers are the SGR table in 5.67: 0 reset,
/// 1 ignored, 2 low intensity, 3 ignored, 4 underlined, 5 slow blink, 6 ignored, 7 inverse,
/// 8 invisible. With no corners the default is the whole screen or work area (all four sections).
/// <para></para>
/// l2 must be at least l1 and c2 at least c1, otherwise the sequence is ignored (all four).
/// <para></para>
/// <b>Not decided by the manual:</b> fewer than four corner parameters but more than none, and how
/// an NDFC string of several characters is laid out over the rectangle. Both are left unhandled
/// and listed in BUGS.md.
/// </remarks>
public class TdvRectangleSequencesFollowTheManualTests
{
    private const string Esc = "\u001b";

    private static TDV2200Emulator NewEmulator()
    {
        var emulator = new TDV2200Emulator(80, 24);

        // Three rows of known text, so there is something in every cell the tests look at.
        // Cursor addressing is CUP, which is 1-based.
        emulator.ProcessData(Encoding.ASCII.GetBytes(Esc + "[1;1H" + "AAAAAAAAAA"));
        emulator.ProcessData(Encoding.ASCII.GetBytes(Esc + "[2;1H" + "BBBBBBBBBB"));
        emulator.ProcessData(Encoding.ASCII.GetBytes(Esc + "[3;1H" + "CCCCCCCCCC"));
        return emulator;
    }

    private static void Send(TDV2200Emulator emulator, string csiBody)
    {
        emulator.ProcessData(Encoding.ASCII.GetBytes(Esc + "[" + csiBody));
    }

    private static CharacterAttributes At(TDV2200Emulator emulator, int row, int col)
    {
        return emulator.Buffer.GetCell(row, col).Attributes;
    }

    private static char Ch(TDV2200Emulator emulator, int row, int col)
    {
        return (char)emulator.Buffer.GetCell(row, col).Codepoint;
    }

    // -----------------------------------------------------------------------------------
    // NDSAR
    // -----------------------------------------------------------------------------------

    [Fact]
    public void NdsarTakesTheCornersFirstAndCountsFromOne()
    {
        var emulator = NewEmulator();

        // Lines 2 to 3, columns 2 to 4, inverse (7): CSI 2;2;3;4;7 z
        Send(emulator, "2;2;3;4;7z");

        // 1-based lines 2..3 are buffer rows 1..2, columns 2..4 are buffer columns 1..3.
        for (int row = 1; row <= 2; row++)
        {
            for (int col = 1; col <= 3; col++)
            {
                Assert.True((At(emulator, row, col) & CharacterAttributes.Reverse) != 0,
                    $"row {row} col {col} should be inverse");
            }
        }

        // Just outside on every side.
        Assert.Equal(CharacterAttributes.None, At(emulator, 0, 1));
        Assert.Equal(CharacterAttributes.None, At(emulator, 1, 0));
        Assert.Equal(CharacterAttributes.None, At(emulator, 1, 4));
        Assert.Equal(CharacterAttributes.None, At(emulator, 3, 1));
    }

    [Fact]
    public void NdsarResetsWhatWasThereBeforeAndSetsSeveralAttributes()
    {
        var emulator = NewEmulator();
        Send(emulator, "1;1;1;3;7z"); // inverse on line 1, columns 1..3

        // 4 underlined and 5 slow blink together replace the inverse.
        Send(emulator, "1;1;1;3;4;5z");

        for (int col = 0; col <= 2; col++)
        {
            var attributes = At(emulator, 0, col);
            Assert.True((attributes & CharacterAttributes.Underline) != 0);
            Assert.True((attributes & CharacterAttributes.Blink) != 0);
            Assert.True((attributes & CharacterAttributes.Reverse) == 0, "NDSAR resets earlier aspects");
        }
    }

    [Fact]
    public void NdsarWithNoAttributeGivesNormalRendition()
    {
        var emulator = NewEmulator();
        Send(emulator, "1;1;1;3;7z");

        Send(emulator, "1;1;1;3z");

        Assert.Equal(CharacterAttributes.None, At(emulator, 0, 0));
        Assert.Equal(CharacterAttributes.None, At(emulator, 0, 2));
    }

    [Fact]
    public void NdsarAttributeNumbersAreTheSgrTableNotAnsi()
    {
        var emulator = NewEmulator();

        Send(emulator, "1;1;1;1;2z"); // 2 = low intensity
        Send(emulator, "1;2;1;2;8z"); // 8 = invisible
        Send(emulator, "1;3;1;3;1z"); // 1 = ignored in the TDV table

        Assert.True((At(emulator, 0, 0) & CharacterAttributes.Dim) != 0);
        Assert.True((At(emulator, 0, 1) & CharacterAttributes.Hidden) != 0);
        Assert.Equal(CharacterAttributes.None, At(emulator, 0, 2));
    }

    [Fact]
    public void NdsarWithNoParametersCoversTheWholeScreen()
    {
        var emulator = NewEmulator();
        Send(emulator, "1;1;24;80;7z");
        Assert.True((At(emulator, 23, 79) & CharacterAttributes.Reverse) != 0);

        Send(emulator, "z"); // default: whole screen, normal attribute

        Assert.Equal(CharacterAttributes.None, At(emulator, 0, 0));
        Assert.Equal(CharacterAttributes.None, At(emulator, 23, 79));
    }

    [Fact]
    public void NdsarWithNoParametersCoversTheWorkAreaWhenOneIsDefined()
    {
        var emulator = NewEmulator();
        Send(emulator, "1;1;24;80;7z");
        emulator.WorkAreas.DefineWorkArea(2, 1, 4, 2); // columns 2..4, rows 1..2 (0-based API)

        Send(emulator, "z");

        Assert.Equal(CharacterAttributes.None, At(emulator, 1, 2));
        Assert.True((At(emulator, 0, 0) & CharacterAttributes.Reverse) != 0, "outside the work area");
        Assert.True((At(emulator, 3, 5) & CharacterAttributes.Reverse) != 0, "outside the work area");
    }

    [Fact]
    public void NdsarWithTheSecondCornerBeforeTheFirstIsIgnored()
    {
        var emulator = NewEmulator();

        Send(emulator, "3;3;2;2;7z"); // l2 < l1 and c2 < c1

        Assert.Equal(CharacterAttributes.None, At(emulator, 1, 1));
        Assert.Equal(CharacterAttributes.None, At(emulator, 2, 2));
    }

    // -----------------------------------------------------------------------------------
    // NDAAR and NDRAR
    // -----------------------------------------------------------------------------------

    [Fact]
    public void NdaarAddsWithoutRemovingWhatWasThere()
    {
        var emulator = NewEmulator();
        Send(emulator, "1;1;1;3;4z"); // underlined

        Send(emulator, "1;1;1;3;5{"); // add slow blink

        for (int col = 0; col <= 2; col++)
        {
            Assert.True((At(emulator, 0, col) & CharacterAttributes.Underline) != 0);
            Assert.True((At(emulator, 0, col) & CharacterAttributes.Blink) != 0);
        }
    }

    [Fact]
    public void NdrarRemovesOnlyTheNamedAspects()
    {
        var emulator = NewEmulator();
        Send(emulator, "1;1;1;3;4;5z"); // underlined and slow blink

        Send(emulator, "1;1;1;2;4|"); // remove underline from columns 1..2

        Assert.True((At(emulator, 0, 0) & CharacterAttributes.Underline) == 0);
        Assert.True((At(emulator, 0, 0) & CharacterAttributes.Blink) != 0, "blink stays");
        Assert.True((At(emulator, 0, 2) & CharacterAttributes.Underline) != 0, "column 3 is outside");
    }

    [Fact]
    public void NdaarAndNdrarWithTheSecondCornerBeforeTheFirstAreIgnored()
    {
        var emulator = NewEmulator();
        Send(emulator, "1;1;3;3;4z");

        Send(emulator, "3;3;1;1;5{");
        Send(emulator, "3;3;1;1;4|");

        Assert.True((At(emulator, 1, 1) & CharacterAttributes.Underline) != 0, "NDRAR was ignored");
        Assert.True((At(emulator, 1, 1) & CharacterAttributes.Blink) == 0, "NDAAR was ignored");
    }

    // -----------------------------------------------------------------------------------
    // NDFC
    // -----------------------------------------------------------------------------------

    [Fact]
    public void NdfcTakesTheCornersFirstThenTheCharacter()
    {
        var emulator = NewEmulator();

        // Lines 2 to 3, columns 2 to 4, fill with '*' (42): CSI 2;2;3;4;42 }
        Send(emulator, "2;2;3;4;42}");

        for (int row = 1; row <= 2; row++)
        {
            for (int col = 1; col <= 3; col++)
            {
                Assert.Equal('*', Ch(emulator, row, col));
            }
        }

        Assert.Equal('A', Ch(emulator, 0, 1));
        Assert.Equal('B', Ch(emulator, 1, 0));
        Assert.Equal('B', Ch(emulator, 1, 4));
    }

    [Fact]
    public void NdfcWithNoParametersFillsTheWholeScreenWithSpace()
    {
        var emulator = NewEmulator();

        Send(emulator, "}");

        Assert.Equal(' ', Ch(emulator, 0, 0));
        Assert.Equal(' ', Ch(emulator, 2, 9));
    }

    [Fact]
    public void NdfcWithTheSecondCornerBeforeTheFirstIsIgnored()
    {
        var emulator = NewEmulator();

        Send(emulator, "3;3;2;2;42}");

        Assert.Equal('B', Ch(emulator, 1, 1));
    }

    [Fact]
    public void NdfcWithAStringOfSeveralCharactersIsNotImplementedYet()
    {
        // The manual allows up to ten characters (5.43) but does not say how they are laid out
        // over the rectangle, so nothing is drawn. BUGS.md B3. When that is settled this test
        // changes into one that checks the layout.
        var emulator = NewEmulator();

        Send(emulator, "1;1;1;4;65;66}");

        Assert.Equal('A', Ch(emulator, 0, 0));
        Assert.Equal('A', Ch(emulator, 0, 3));
    }

    // -----------------------------------------------------------------------------------
    // NDDWA
    // -----------------------------------------------------------------------------------

    [Fact]
    public void NddwaCountsFromOneToo()
    {
        var emulator = NewEmulator();

        // Lines 2 to 3, columns 2 to 4: CSI 2;2;3;4 ~
        Send(emulator, "2;2;3;4~");

        var area = emulator.WorkAreas.GetCurrentWorkArea();
        Assert.Equal((1, 1, 3, 2), (area.Left, area.Top, area.Right, area.Bottom));
    }
}
