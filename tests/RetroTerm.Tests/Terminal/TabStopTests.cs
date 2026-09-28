using System.Text;
using RetroTerm.Core.Configuration;
using RetroTerm.Core.Terminal.Emulators;
using Xunit;

namespace RetroTerm.Tests.Terminal;

/// <summary>
/// Tab stops, and what tabulation does to a pending wrap.
///
/// TABULATION NEVER CANCELS A PENDING WRAP. Every other cursor movement does, so this is the one
/// exception, and it took two fixtures to see the whole of it:
///
///  - t0080-HT and t0083-CHT show the standing-still half. On the last column no tab stop remains,
///    so the tab moves nothing, and it must not disturb the flag the last printed character armed.
///    xterm put "k" in the last column and "l" at the start of the next row; we had "l" on top
///    of "k".
///  - t0084-CBT shows the moving half. Backward tabulation really does move off the last column,
///    and the flag STILL survives - the next character wraps to the following row rather than
///    landing on the stop the cursor moved to.
/// </summary>
public class TabStopTests
{
    private static readonly string Esc = ((char)0x1B).ToString();

    private static TerminalEmulatorBase Build(int width = 20, int height = 3)
        => EmulatorFactory.CreateEmulator("XTERM", width, height, 100);

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

    [Fact]
    public void ATabAtTheLastColumnLeavesThePendingWrapArmed()
    {
        // THE defect. The character after such a tab belongs on the NEXT ROW, because the wrap was
        // already armed and the tab had nowhere to move to.
        var emulator = Build();
        Feed(emulator, Esc + "[1;20H");     // last column of a 20-column screen
        Feed(emulator, "k");                // fills it, arming the wrap
        Feed(emulator, "\t");               // no tab stop left, so nothing moves
        Feed(emulator, "l");

        Assert.Equal(".......................".Substring(0, 19) + "k", Row(emulator, 0));
        Assert.Equal("l" + new string('.', 19), Row(emulator, 1));
    }

    [Fact]
    public void AnOrdinaryTabStillMoves()
    {
        // The guard against fixing the flag by making tabulation do nothing at all. A cursor
        // movement in between - here CUP - is what cancels the wrap; the tab after it moves as
        // usual and the character lands on the stop.
        var emulator = Build(width: 40);
        Feed(emulator, Esc + "[1;40H");
        Feed(emulator, "k");                // arms the wrap at the last column
        Feed(emulator, Esc + "[1;10H");     // move back into the middle of the line - cancels it
        Feed(emulator, "\t");               // this one really moves, to column 16
        Feed(emulator, "x");

        // "x" landed on the stop, and the cursor sits just past it - printing advances.
        Assert.Equal('x', Row(emulator, 0)[16]);
        Assert.Equal(17, emulator.GetCursor().Column);
        Assert.Equal("", Row(emulator, 1).Replace(".", ""));   // nothing wrapped to the next row
    }

    [Fact]
    public void TheEscapeSequenceFormObeysTheSameRule()
    {
        // CHT is a tab written as a control sequence, and it had the same fault. Fixing only the
        // control character would have left this broken in exactly the same way - which is why
        // both go through one method now.
        var emulator = Build();
        Feed(emulator, Esc + "[1;20H");
        Feed(emulator, "k");
        Feed(emulator, Esc + "[I");         // CHT, nowhere left to go
        Feed(emulator, "l");

        Assert.Equal('k', Row(emulator, 0)[19]);
        Assert.Equal('l', Row(emulator, 1)[0]);
    }

    [Fact]
    public void BackwardTabulationKeepsTheWrapEvenThoughItMoves()
    {
        // t0084-CBT, the moving half of the rule. The cursor really does go back to column 16,
        // and the "!" still wraps to the next row instead of landing there.
        var emulator = Build(width: 20, height: 4);
        Feed(emulator, Esc + "[2;1H");
        Feed(emulator, "ABCDEFGHIJKLMNOPQRST");    // fills row 1, arming the wrap at column 19
        Feed(emulator, Esc + "[Z");                // CBT back to the stop at column 16
        Feed(emulator, "!");

        Assert.Equal("ABCDEFGHIJKLMNOPQRST", Row(emulator, 1));
        Assert.Equal('!', Row(emulator, 2)[0]);
    }

    [Fact]
    public void AndTheColumnItMovedToIsWhereTheNextCharacterGoesOnceTheWrapIsGone()
    {
        // The other half of the same fixture - its "at end with clipping:" rows. ESC M and ESC D
        // move up a row and straight back down, which DOES cancel the wrap, and the "!" then lands
        // at column 16 exactly. Without this the fix could not be told apart from "CBT does
        // nothing when the wrap is armed".
        var emulator = Build(width: 20, height: 4);
        Feed(emulator, Esc + "[2;1H");
        Feed(emulator, "ABCDEFGHIJKLMNOPQRST");
        Feed(emulator, Esc + "[Z");                // CBT back to column 16, wrap still armed
        Feed(emulator, Esc + "M" + Esc + "D");     // RI then IND - back where it started, wrap gone
        Feed(emulator, "!");

        Assert.Equal("ABCDEFGHIJKLMNOP!RST", Row(emulator, 1));
        Assert.Equal("", Row(emulator, 2).Replace(".", ""));
    }

    [Fact]
    public void TwoStopsBackWorksTheSameWay()
    {
        // CSI 2 Z, the fixture's "(2)" rows. Two stops back is column 8, and the wrap survives
        // both of them.
        var emulator = Build(width: 20, height: 4);
        Feed(emulator, Esc + "[2;1H");
        Feed(emulator, "ABCDEFGHIJKLMNOPQRST");
        Feed(emulator, Esc + "[2Z");
        Feed(emulator, Esc + "M" + Esc + "D");
        Feed(emulator, "!");

        Assert.Equal("ABCDEFGH!JKLMNOPQRST", Row(emulator, 1));
    }

    [Fact]
    public void TabsLandOnEveryEighthColumnByDefault()
    {
        var emulator = Build(width: 40);
        Feed(emulator, "a\tb\tc\td");

        Assert.Equal("a.......b.......c.......d" + new string('.', 15), Row(emulator, 0));
    }

    [Fact]
    public void ATabStopCanBeSetWhereTheCursorIs()
    {
        // HTS - ESC H. The fixture that this emptied alongside t0080.
        var emulator = Build(width: 40);
        Feed(emulator, Esc + "[1;5H" + Esc + "H");      // stop at column 5
        Feed(emulator, Esc + "[1;1H");
        Feed(emulator, "a\tb");

        Assert.Equal("a...b" + new string('.', 35), Row(emulator, 0));
    }

    [Fact]
    public void AndClearedAgain()
    {
        // TBC with no parameter clears the stop under the cursor, so the next tab runs on to the
        // one after it.
        var emulator = Build(width: 40);
        Feed(emulator, Esc + "[1;9H" + Esc + "[g");     // clear the stop at column 9
        Feed(emulator, Esc + "[1;1H");
        Feed(emulator, "a\tb");

        Assert.Equal("a" + new string('.', 15) + "b" + new string('.', 23), Row(emulator, 0));
    }

    [Fact]
    public void ClearingEveryStopSendsATabToTheLastColumn()
    {
        var emulator = Build(width: 20);
        Feed(emulator, Esc + "[3g");                    // TBC 3 - all stops gone
        Feed(emulator, Esc + "[1;1H");
        Feed(emulator, "a\tb");

        Assert.Equal("a" + new string('.', 18) + "b", Row(emulator, 0));
    }
}
