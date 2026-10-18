using System.Text;
using RetroTerm.Core.Configuration;
using RetroTerm.Core.Terminal.Emulators;
using Xunit;

namespace RetroTerm.Tests.Terminal;

/// <summary>
/// The VT420 rectangle operations: DECFRA, DECERA, DECSERA and DECCRA.
///
/// All four carry a <c>$</c> intermediate, which is the only thing separating them from other
/// sequences that own the same final bytes — 'x' is DECREQTPARM, and 'v' and 'z' are nothing at
/// all. A terminal that read them without checking the intermediate would act on the wrong command.
/// </summary>
public class RectangleOperationTests
{
    private static readonly string Esc = ((char)0x1B).ToString();

    private static TerminalEmulatorBase Build(string type = "VT420")
        => EmulatorFactory.CreateEmulator(type, 10, 5, 100);

    private static void Feed(TerminalEmulatorBase emulator, string text)
        => emulator.ProcessData(Encoding.ASCII.GetBytes(text));

    private static string Row(TerminalEmulatorBase emulator, int row)
    {
        var text = new StringBuilder(emulator.Width);
        for (int col = 0; col < emulator.Width; col++)
        {
            emulator.GetBuffer().TryGetCell(row, col, out var cell);
            text.Append(cell.Codepoint == 0 ? '.' : (char)cell.Codepoint);
        }
        return text.ToString();
    }

    /// <summary>
    /// A screen of A's, five rows of ten.
    /// </summary>
    private static TerminalEmulatorBase FullOfAs()
    {
        var emulator = Build();
        for (int row = 1; row <= 5; row++)
        {
            Feed(emulator, Esc + "[" + row + ";1H" + "AAAAAAAAAA");
        }
        return emulator;
    }

    [Fact]
    public void FillPutsOneCharacterInsideTheRectangle()
    {
        // DECFRA: character first, then top, left, bottom, right. 88 is 'X'.
        var emulator = FullOfAs();

        Feed(emulator, Esc + "[88;2;3;4;6$x");

        Assert.Equal("AAAAAAAAAA", Row(emulator, 0));
        Assert.Equal("AAXXXXAAAA", Row(emulator, 1));
        Assert.Equal("AAXXXXAAAA", Row(emulator, 3));
        Assert.Equal("AAAAAAAAAA", Row(emulator, 4));
    }

    [Fact]
    public void FillRefusesACharacterWithNoGlyph()
    {
        // DEC restricts the fill character to the printable ranges. Filling the screen with a
        // control code would put something on it that cannot be drawn.
        var emulator = FullOfAs();

        Feed(emulator, Esc + "[7;1;1;5;10$x");

        Assert.Equal("AAAAAAAAAA", Row(emulator, 0));
    }

    [Fact]
    public void EraseClearsTheRectangleAndNothingElse()
    {
        var emulator = FullOfAs();

        Feed(emulator, Esc + "[2;3;4;6$z");

        Assert.Equal("AAAAAAAAAA", Row(emulator, 0));
        Assert.Equal("AA....AAAA", Row(emulator, 1));
        Assert.Equal("AA....AAAA", Row(emulator, 3));
        Assert.Equal("AAAAAAAAAA", Row(emulator, 4));
    }

    [Fact]
    public void AnOmittedParameterMeansTheEdgeOfTheScreen()
    {
        // Every one of the four defaults to "as far as the screen goes", so a bare erase clears
        // everything. That is DEC's rule, and defaulting to zero instead would erase one cell.
        var emulator = FullOfAs();

        Feed(emulator, Esc + "[$z");

        Assert.Equal("..........", Row(emulator, 0));
        Assert.Equal("..........", Row(emulator, 4));
    }

    [Fact]
    public void SelectiveEraseSparesProtectedText()
    {
        // DECSERA follows the same rule DECSED and DECSEL do: text marked protected by DECSCA
        // survives.
        var emulator = Build();
        Feed(emulator, Esc + "[1;1H" + Esc + "[1\"q" + "KEEP" + Esc + "[0\"q" + "GONE");

        Feed(emulator, Esc + "[1;1;1;10${");

        Assert.Equal("KEEP......", Row(emulator, 0));
    }

    [Fact]
    public void CopyMovesARectangleSomewhereElse()
    {
        var emulator = Build();
        Feed(emulator, Esc + "[1;1H" + "ABCD......");

        // Source rows 1-1, columns 1-4; destination row 3, column 5.
        Feed(emulator, Esc + "[1;1;1;4;1;3;5;1$v");

        Assert.Equal("ABCD......", Row(emulator, 0));
        Assert.Equal("....ABCD..", Row(emulator, 2));
    }

    [Fact]
    public void AnOverlappingCopyDoesNotSmearTheSourceAcrossItself()
    {
        // THE hard case. A program scrolls a panel by copying a rectangle onto one that overlaps
        // it; copying cell by cell in place reads cells it has already overwritten and drags the
        // first character across the whole destination.
        var emulator = Build();
        Feed(emulator, Esc + "[1;1H" + "ABCDE.....");

        // Copy columns 1-5 of row 1 one column to the right, onto itself.
        Feed(emulator, Esc + "[1;1;1;5;1;1;2;1$v");

        Assert.Equal("AABCDE....", Row(emulator, 0));
    }

    [Fact]
    public void ACopyOffTheEdgeIsClippedRatherThanRefused()
    {
        var emulator = Build();
        Feed(emulator, Esc + "[1;1H" + "ABCD......");

        Feed(emulator, Esc + "[1;1;1;4;1;1;9;1$v");

        Assert.Equal("ABCD....AB", Row(emulator, 0));
    }

    [Fact]
    public void ATerminalWithoutTheCapabilityIgnoresThem()
    {
        // A VT100 has no rectangle operations, and 'x' with a '$' intermediate is not something it
        // should act on.
        var emulator = Build("VT100");
        Feed(emulator, Esc + "[1;1H" + "AAAAAAAAAA");

        Feed(emulator, Esc + "[88;1;1;1;10$x");

        Assert.Equal("AAAAAAAAAA", Row(emulator, 0));
    }

    [Fact]
    public void ARectangleThatIsNotOneDoesNothing()
    {
        // Bottom above top. The host asked for something meaningless; guessing at its intent would
        // change cells it did not choose.
        var emulator = FullOfAs();

        Feed(emulator, Esc + "[4;3;2;6$z");

        Assert.Equal("AAAAAAAAAA", Row(emulator, 1));
        Assert.Equal("AAAAAAAAAA", Row(emulator, 3));
    }

    /// <summary>
    /// Whether a cell carries an attribute.
    /// </summary>
    private static bool Has(TerminalEmulatorBase emulator, int row, int col,
        RetroTerm.Core.Terminal.Buffer.CharacterAttributes attribute)
    {
        emulator.GetBuffer().TryGetCell(row, col, out var cell);
        return (cell.Attributes & attribute) == attribute;
    }

    [Fact]
    public void ChangingAttributesSetsThemOverTheArea()
    {
        var emulator = FullOfAs();

        // DECCARA: top, left, bottom, right, then the attributes. 4 is underline.
        Feed(emulator, Esc + "[2;3;3;6;4$r");

        Assert.True(Has(emulator, 1, 2, RetroTerm.Core.Terminal.Buffer.CharacterAttributes.Underline));
        Assert.False(Has(emulator, 0, 2, RetroTerm.Core.Terminal.Buffer.CharacterAttributes.Underline));
    }

    [Fact]
    public void TheDefaultExtentIsAStreamAndNotARectangle()
    {
        // THE thing that is easy to get wrong. With DECSACE at its power-on value the four numbers
        // are a START and an END, and the change runs like TEXT: to the end of the first row,
        // across the rows between, and up to the end column on the last. Implementing the rectangle
        // and calling it done looks right in any test whose region spans whole rows, and is wrong
        // for every real use.
        var emulator = FullOfAs();

        Feed(emulator, Esc + "[2;3;3;6;4$r");

        // First row of the run: from column 3 to the END of the row.
        Assert.True(Has(emulator, 1, 9, RetroTerm.Core.Terminal.Buffer.CharacterAttributes.Underline));

        // Last row: from the START of the row up to column 6.
        Assert.True(Has(emulator, 2, 0, RetroTerm.Core.Terminal.Buffer.CharacterAttributes.Underline));
        Assert.False(Has(emulator, 2, 8, RetroTerm.Core.Terminal.Buffer.CharacterAttributes.Underline));
    }

    [Fact]
    public void DecsaceTwoMakesItARectangle()
    {
        var emulator = FullOfAs();

        Feed(emulator, Esc + "[2*x");                 // DECSACE: rectangle
        Feed(emulator, Esc + "[2;3;3;6;4$r");

        Assert.True(emulator.AttributeChangeIsRectangular);

        // Now the same columns on every row, and nothing outside them.
        Assert.True(Has(emulator, 1, 2, RetroTerm.Core.Terminal.Buffer.CharacterAttributes.Underline));
        Assert.False(Has(emulator, 1, 9, RetroTerm.Core.Terminal.Buffer.CharacterAttributes.Underline));
        Assert.False(Has(emulator, 2, 0, RetroTerm.Core.Terminal.Buffer.CharacterAttributes.Underline));
    }

    [Fact]
    public void ReverseAttributesTogglesRatherThanSets()
    {
        // DECRARA. Applied twice, everything is back where it started - which is what makes it a
        // reverse rather than a set.
        var emulator = FullOfAs();
        Feed(emulator, Esc + "[2*x");
        Feed(emulator, Esc + "[1;1;5;10;4$r");        // underline everything

        Feed(emulator, Esc + "[2;3;3;6;4$t");         // reverse a patch of it

        Assert.False(Has(emulator, 1, 2, RetroTerm.Core.Terminal.Buffer.CharacterAttributes.Underline));
        Assert.True(Has(emulator, 0, 2, RetroTerm.Core.Terminal.Buffer.CharacterAttributes.Underline));

        Feed(emulator, Esc + "[2;3;3;6;4$t");
        Assert.True(Has(emulator, 1, 2, RetroTerm.Core.Terminal.Buffer.CharacterAttributes.Underline));
    }

    [Fact]
    public void ZeroClearsTheFourItOwnsAndLeavesTheRest()
    {
        // A protected cell must not stop being protected because a host changed its rendition.
        var emulator = Build();
        Feed(emulator, Esc + "[1;1H" + Esc + "[1\"q" + Esc + "[4m" + "AAAA");
        Feed(emulator, Esc + "[2*x");

        Feed(emulator, Esc + "[1;1;1;4;0$r");

        Assert.False(Has(emulator, 0, 0, RetroTerm.Core.Terminal.Buffer.CharacterAttributes.Underline));
        Assert.True(Has(emulator, 0, 0, RetroTerm.Core.Terminal.Buffer.CharacterAttributes.Protected));
    }

    [Fact]
    public void AHardResetReturnsTheExtentToStream()
    {
        var emulator = Build();
        Feed(emulator, Esc + "[2*x");
        Assert.True(emulator.AttributeChangeIsRectangular);

        emulator.Reset();

        Assert.False(emulator.AttributeChangeIsRectangular);
    }

    [Fact]
    public void ATerminalWithoutTheCapabilityIgnoresTheAttributeCommands()
    {
        var emulator = Build("VT100");
        Feed(emulator, Esc + "[1;1H" + "AAAA");

        Feed(emulator, Esc + "[1;1;1;4;4$r");

        Assert.False(Has(emulator, 0, 0, RetroTerm.Core.Terminal.Buffer.CharacterAttributes.Underline));
    }
}
