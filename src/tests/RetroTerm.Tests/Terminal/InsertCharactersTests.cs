using System.Collections.Generic;
using System.Text;
using RetroTerm.Core.Configuration;
using RetroTerm.Core.Terminal.Emulators;
using Xunit;

namespace RetroTerm.Tests.Terminal;

/// <summary>
/// ICH — <c>CSI Ps @</c>, Insert Characters.
///
/// Its opposite number DCH (<c>CSI Ps P</c>) was implemented and this was not, which is a hard
/// omission to notice: a host that opens a gap to insert text got no gap, so the insert quietly
/// became an overwrite and the line ended up one character short. <c>ShiftCharactersRight</c>
/// already did exactly the right work — it was only ever reachable through IRM insert mode.
///
/// Found by libvterm's 60screen_ascii script — see tests\RetroTerm.Tests\Conformance.
/// </summary>
public class InsertCharactersTests
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

    private static TerminalEmulatorBase Build(string type) => EmulatorFactory.CreateEmulator(type, 20, 5, 100);

    private static void Feed(TerminalEmulatorBase emulator, string text)
        => emulator.ProcessData(Encoding.ASCII.GetBytes(text));

    private static string Row(TerminalEmulatorBase emulator, int row, int width)
    {
        var text = new StringBuilder(width);
        for (int col = 0; col < width; col++)
        {
            emulator.GetBuffer().TryGetCell(row, col, out var cell);
            uint cp = cell.Codepoint;
            text.Append(cp == 0 ? ' ' : (char)cp);
        }
        return text.ToString();
    }

    [Theory]
    [MemberData(nameof(AllEmulators))]
    public void IchOpensAGapThatTheNextCharacterFills(string emulatorType)
    {
        // This is the exact case from the corpus: without ICH the '1' overwrote the 'A' and the
        // row read "1BC" instead of "1ABC".
        var emulator = Build(emulatorType);
        Feed(emulator, "ABC\u001b[H\u001b[@");
        Feed(emulator, "1");

        Assert.Equal("1ABC", Row(emulator, 0, 4));
    }

    [Theory]
    [MemberData(nameof(AllEmulators))]
    public void IchDefaultsToOneAndHonoursACount(string emulatorType)
    {
        var emulator = Build(emulatorType);
        Feed(emulator, "ABCDE\u001b[H\u001b[3@");

        // Three blanks pushed in; ABCDE moved right by three.
        Assert.Equal("   ABCDE", Row(emulator, 0, 8));
    }

    [Theory]
    [MemberData(nameof(AllEmulators))]
    public void IchInsertsAtTheCursor_NotAtTheStartOfTheLine(string emulatorType)
    {
        var emulator = Build(emulatorType);
        Feed(emulator, "ABCDE\u001b[1;3H\u001b[@");   // cursor on the 'C'

        Assert.Equal("AB CDE", Row(emulator, 0, 6));
    }

    [Theory]
    [MemberData(nameof(AllEmulators))]
    public void CharactersPushedPastTheRightEdgeAreLost(string emulatorType)
    {
        // ECMA-48: what falls off the end of the line is discarded, it does not wrap.
        var emulator = Build(emulatorType);
        Feed(emulator, new string('X', 20));          // fill the 20-column row
        Feed(emulator, "\u001b[H\u001b[@");

        string row = Row(emulator, 0, 20);
        Assert.Equal(' ', row[0]);
        Assert.Equal('X', row[19]);                   // still 19 X's, the twentieth fell off
        Assert.Equal(19, row.Replace(" ", "").Length);
    }
}
