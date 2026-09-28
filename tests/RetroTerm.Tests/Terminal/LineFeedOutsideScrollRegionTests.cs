using System.Collections.Generic;
using System.Text;
using RetroTerm.Core.Configuration;
using RetroTerm.Core.Terminal.Emulators;
using Xunit;

namespace RetroTerm.Tests.Terminal;

/// <summary>
/// LF and RI when the cursor is OUTSIDE the scrolling region.
///
/// THE DEFECT. The boundary test was "at or past" rather than "exactly on":
/// <c>HandleLineFeed</c> read <c>Row is less than ScrollBottom ? MoveDown() : ScrollUp()</c> and
/// <c>HandleReverseLineFeed</c> read <c>Row at or above ScrollTop ? ScrollDown() : MoveUp()</c>.
/// Inside the region both are right. Outside it they are wrong in the worst way: with a region of
/// rows 0..9 and the cursor parked on row 19, a linefeed scrolled a region the cursor was not in
/// AND left the cursor where it was.
///
/// That is not an exotic case. A full-screen program that reserves part of the screen with
/// DECSTBM and then writes below it — a status line, a pager footer, a split-window editor — got
/// a screen that jumped and a cursor that would not move.
///
/// Found by libvterm's 12state_scroll script — see tests\RetroTerm.Tests\Conformance.
/// </summary>
public class LineFeedOutsideScrollRegionTests
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

    private static TerminalEmulatorBase Build(string type) => EmulatorFactory.CreateEmulator(type, 20, 25, 100);

    private static void Feed(TerminalEmulatorBase emulator, string text)
        => emulator.ProcessData(Encoding.ASCII.GetBytes(text));

    [Theory]
    [MemberData(nameof(AllEmulators))]
    public void LinefeedBelowTheRegionJustMovesDown(string emulatorType)
    {
        // The exact case from the corpus.
        var emulator = Build(emulatorType);
        Feed(emulator, "\u001b[1;10r");     // scrolling region = rows 0..9
        Feed(emulator, "\u001b[20H");       // row 19, well below it

        Feed(emulator, "\n");

        Assert.Equal(20, emulator.GetCursor().Row);
    }

    [Theory]
    [MemberData(nameof(AllEmulators))]
    public void LinefeedBelowTheRegionDoesNotScrollIt(string emulatorType)
    {
        var emulator = Build(emulatorType);
        Feed(emulator, "\u001b[1;10r");
        Feed(emulator, "\u001b[1;1HInside");    // text on row 0, inside the region
        Feed(emulator, "\u001b[20H");
        Feed(emulator, "\n");

        // If the region had scrolled, "Inside" would have moved off row 0.
        Assert.True(emulator.GetBuffer().TryGetCell(0, 0, out var cell));
        Assert.Equal((uint)'I', cell.Codepoint);
    }

    [Theory]
    [MemberData(nameof(AllEmulators))]
    public void LinefeedStopsAtTheBottomOfTheScreen(string emulatorType)
    {
        // Below the region the cursor walks down and simply stops — it must not run off the end.
        var emulator = Build(emulatorType);
        Feed(emulator, "\u001b[1;10r");
        Feed(emulator, "\u001b[25H");       // last row of a 25-row screen

        Feed(emulator, "\n");

        // EXCEPT ON A 4014, where this stopped being true when two-column writing arrived. A
        // storage tube cannot scroll, so the manual gives a line feed on the last row somewhere
        // else to go: "The terminal receives a line feed on the last row of the display" switches
        // the active margin and wraps to the TOP of the screen in the other column. The invariant
        // this test was really written for - the cursor must not run off the end - still holds,
        // and that is what is checked for the 4014.
        if (emulatorType == "TEK4014")
        {
            int row = emulator.GetCursor().Row;
            Assert.True(row >= 0 && row < emulator.Height,
                $"the cursor ran off a {emulator.Height}-row screen to row {row}");
            Assert.Equal(0, row);
            return;
        }

        Assert.Equal(24, emulator.GetCursor().Row);
    }

    [Theory]
    [MemberData(nameof(AllEmulators))]
    public void LinefeedOnTheRegionBottomStillScrolls(string emulatorType)
    {
        // The behaviour that was already right must stay right.
        var emulator = Build(emulatorType);
        Feed(emulator, "\u001b[1;10r");
        Feed(emulator, "\u001b[1;1HTop");
        Feed(emulator, "\u001b[10;1H");     // row 9 — the region's last line

        Feed(emulator, "\n");

        Assert.Equal(9, emulator.GetCursor().Row);          // cursor holds still
        Assert.True(emulator.GetBuffer().TryGetCell(0, 0, out var cell));
        Assert.NotEqual((uint)'T', cell.Codepoint);         // ...and "Top" scrolled away
    }

    [Theory]
    [MemberData(nameof(AllEmulators))]
    public void ReverseIndexAboveTheRegionJustMovesUp(string emulatorType)
    {
        var emulator = Build(emulatorType);
        Feed(emulator, "\u001b[10;20r");    // region = rows 9..19
        Feed(emulator, "\u001b[5H");        // row 4, above it

        Feed(emulator, "\u001bM");          // RI

        Assert.Equal(3, emulator.GetCursor().Row);
    }

    [Theory]
    [MemberData(nameof(AllEmulators))]
    public void ReverseIndexOnTheRegionTopStillScrolls(string emulatorType)
    {
        var emulator = Build(emulatorType);
        Feed(emulator, "\u001b[10;20r");
        Feed(emulator, "\u001b[10;1HTop");  // row 9 — the region's first line

        Feed(emulator, "\u001b[10;1H");
        Feed(emulator, "\u001bM");

        Assert.Equal(9, emulator.GetCursor().Row);          // cursor holds still
        Assert.True(emulator.GetBuffer().TryGetCell(10, 0, out var cell));
        Assert.Equal((uint)'T', cell.Codepoint);            // ...and "Top" moved down one
    }
}
