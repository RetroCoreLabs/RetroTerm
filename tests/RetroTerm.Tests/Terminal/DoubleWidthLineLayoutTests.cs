using System.Collections.Generic;
using System.Text;
using RetroTerm.Core.Configuration;
using RetroTerm.Core.Terminal.Buffer;
using RetroTerm.Core.Terminal.Emulators;
using Xunit;

namespace RetroTerm.Tests.Terminal;

/// <summary>
/// A double-width line holds HALF as many characters, and stays double-width while you type.
///
/// TWO DEFECTS, and the second hid the first.
///
/// 1. Line size is the one part of <c>CharacterAttributes</c> that belongs to the LINE rather
///    than to the character. Writing a character did <c>cell.Attributes = CurrentAttributes</c>,
///    which replaced the whole word — so a DECDWL line stopped being double-width the instant
///    anything was typed on it, and the flag survived only on cells nobody had touched.
///
/// 2. With that fixed, the wrap boundary still has to move: DECDWL draws every character at twice
///    the width, so 80 columns hold 40 characters and the line must wrap at column 39. Treating a
///    double-width line as full width ran text off the visible right-hand edge into cells that
///    are never drawn.
///
/// Found by libvterm's 28state_dbl_wh script — see tests\RetroTerm.Tests\Conformance. Note the
/// first defect made the second INVISIBLE: a fix for the wrap alone changed nothing, because by
/// the time the cursor reached column 39 the line no longer claimed to be double-width.
/// </summary>
public class DoubleWidthLineLayoutTests
{
    public static IEnumerable<object[]> AllEmulators()
    {
        var types = EmulatorFactory.AvailableEmulators;
        for (int i = 0; i < types.Length; i++)
        {
            yield return new object[] { types[i] };
        }
        yield return new object[] { "ANSI" };
    }

    private static TerminalEmulatorBase Build(string type) => EmulatorFactory.CreateEmulator(type, 80, 6, 100);

    private static void Feed(TerminalEmulatorBase emulator, string text)
        => emulator.ProcessData(Encoding.ASCII.GetBytes(text));

    private static TerminalCell CellAt(TerminalEmulatorBase emulator, int row, int col)
    {
        Assert.True(emulator.GetBuffer().TryGetCell(row, col, out var cell), $"no cell at {row},{col}");
        return cell;
    }

    [Theory]
    [MemberData(nameof(AllEmulators))]
    public void TypingOnADoubleWidthLineKeepsItDoubleWidth(string emulatorType)
    {
        var emulator = Build(emulatorType);
        Feed(emulator, "\u001b#6");
        Feed(emulator, "Hello");

        for (int col = 0; col < 5; col++)
        {
            Assert.True(CellAt(emulator, 0, col).DoubleWidth,
                $"{emulatorType}: column {col} lost its double width when written to");
        }
    }

    [Theory]
    [MemberData(nameof(AllEmulators))]
    public void TypingOnADoubleHeightLineKeepsBothHalvesFlagged(string emulatorType)
    {
        var emulator = Build(emulatorType);
        Feed(emulator, "\u001b#3");         // DECDHL top half
        Feed(emulator, "Hello");

        var cell = CellAt(emulator, 0, 2);
        Assert.True(cell.DoubleHeightTop, $"{emulatorType}: lost the top-half flag");
        Assert.True(cell.DoubleWidth, $"{emulatorType}: double height implies double width");
    }

    [Theory]
    [MemberData(nameof(AllEmulators))]
    public void OrdinarySgrAttributesStillApplyOnADoubleWidthLine(string emulatorType)
    {
        // Carrying the line size across must not smuggle anything else across with it.
        var emulator = Build(emulatorType);
        Feed(emulator, "\u001b#6");
        Feed(emulator, "\u001b[1mA");       // bold
        Feed(emulator, "\u001b[0mB");       // back to plain

        Assert.True(CellAt(emulator, 0, 0).Attributes.HasAttribute(CharacterAttributes.Bold));
        Assert.False(CellAt(emulator, 0, 1).Attributes.HasAttribute(CharacterAttributes.Bold));
        Assert.True(CellAt(emulator, 0, 1).DoubleWidth, $"{emulatorType}: SGR 0 must not clear the line size");
    }

    [Theory]
    [MemberData(nameof(AllEmulators))]
    public void ADoubleWidthLineWrapsAtHalfTheScreen(string emulatorType)
    {
        // The exact case from the corpus: on an 80-column screen, column 39 is the last usable
        // cell, so the character after it lands on the next row.
        var emulator = Build(emulatorType);
        Feed(emulator, "\u001b#6");
        Feed(emulator, "\u001b[40GAB");     // cursor to column 39, then two characters

        Assert.Equal((uint)'A', CellAt(emulator, 0, 39).Codepoint);
        Assert.Equal((uint)'B', CellAt(emulator, 1, 0).Codepoint);
        Assert.Equal(1, emulator.GetCursor().Row);
        Assert.Equal(1, emulator.GetCursor().Column);
    }

    [Theory]
    [MemberData(nameof(AllEmulators))]
    public void ANormalLineStillUsesTheWholeScreen(string emulatorType)
    {
        // The control: without DECDWL the same sequence must NOT wrap.
        var emulator = Build(emulatorType);
        Feed(emulator, "\u001b[40GAB");

        Assert.Equal((uint)'A', CellAt(emulator, 0, 39).Codepoint);
        Assert.Equal((uint)'B', CellAt(emulator, 0, 40).Codepoint);
        Assert.Equal(0, emulator.GetCursor().Row);
    }

    [Theory]
    [MemberData(nameof(AllEmulators))]
    public void DecswlGivesTheWholeLineBack(string emulatorType)
    {
        // ESC # 5 returns the line to single width, so the wrap boundary returns to column 79.
        var emulator = Build(emulatorType);
        Feed(emulator, "\u001b#6");
        Feed(emulator, "\u001b#5");
        Feed(emulator, "\u001b[40GAB");

        Assert.Equal((uint)'B', CellAt(emulator, 0, 40).Codepoint);
        Assert.Equal(0, emulator.GetCursor().Row);
    }
}
