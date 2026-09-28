using System.Text;
using RetroTerm.Core.Configuration;
using RetroTerm.Core.Terminal.Emulators;
using Xunit;

namespace RetroTerm.Tests.Terminal;

/// <summary>
/// Page memory, from chapter 6 of the VT420 Programmer Reference held in spec\DEC.
///
/// The terminal holds more lines than it shows and divides them into pages. A host writes to a page
/// by moving to it first, which is how a VT320 or VT420 updates a screen the user is not looking at
/// and then switches to it complete.
///
/// The rules here are the manual's, not recollection: DECSLPP's six page lengths come from its table
/// on page 140, and the split between the functions that home the cursor and the ones that keep its
/// row and column comes from pages 136 to 139.
/// </summary>
public class PageMemoryTests
{
    private static readonly string Esc = ((char)0x1B).ToString();

    private static TerminalEmulatorBase Build(string type = "VT420")
        => EmulatorFactory.CreateEmulator(type, 20, 6, 100);

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
        return text.ToString().TrimEnd('.');
    }

    // ── DECSLPP: how many pages the memory divides into ──────────────────────────────────

    [Theory]
    [InlineData(24, 6)]
    [InlineData(25, 5)]
    [InlineData(36, 4)]
    [InlineData(48, 3)]
    [InlineData(72, 2)]
    [InlineData(144, 1)]
    public void TheLinesPerPageDecideHowManyPagesThereAre(int linesPerPage, int expectedPages)
    {
        // Straight from the manual's table. 144 lines of memory divided by the page length.
        var emulator = Build();

        Feed(emulator, Esc + "[" + linesPerPage + "t");

        Assert.Equal(expectedPages, emulator.PageCount);
    }

    [Fact]
    public void APageLengthTheTerminalDoesNotOfferIsIgnored()
    {
        // The manual lists exactly six. Rounding 30 to the nearest would let a host believe in a
        // layout no VT420 could produce, so it is refused rather than approximated.
        var emulator = Build();
        Feed(emulator, Esc + "[24t");

        Feed(emulator, Esc + "[30t");

        Assert.Equal(6, emulator.PageCount);
    }

    [Fact]
    public void ATerminalWithoutPageMemoryReadsTheSameSequenceAsAWindowCommand()
    {
        // CSI 24 t is DECSLPP on a VT420 and xterm's window manipulation everywhere else. No
        // terminal is both, so the profile decides - and a VT220 must not grow pages.
        var emulator = Build("VT220");

        Feed(emulator, Esc + "[24t");

        Assert.Equal(1, emulator.PageCount);
    }

    // ── Moving between pages ─────────────────────────────────────────────────────────────

    [Fact]
    public void NextPageGoesToTheHomePositionOfTheNextPage()
    {
        var emulator = Build();
        Feed(emulator, Esc + "[24t");          // six pages
        Feed(emulator, Esc + "[3;8H");         // somewhere in the middle

        Feed(emulator, Esc + "[U");             // NP

        Assert.Equal(2, emulator.CurrentPage);
        Assert.Equal(0, emulator.GetCursor().Row);
        Assert.Equal(0, emulator.GetCursor().Column);
    }

    [Fact]
    public void PrecedingPageDoesTheSameGoingBackwards()
    {
        var emulator = Build();
        Feed(emulator, Esc + "[24t");
        Feed(emulator, Esc + "[3U");           // to page 4
        Feed(emulator, Esc + "[3;8H");

        Feed(emulator, Esc + "[V");             // PP

        Assert.Equal(3, emulator.CurrentPage);
        Assert.Equal(0, emulator.GetCursor().Row);
        Assert.Equal(0, emulator.GetCursor().Column);
    }

    [Fact]
    public void PagePositionAbsoluteKeepsTheRowAndColumn()
    {
        // THE difference between the two families. NP and PP home the cursor; PPA, PPB and PPR
        // move to "the corresponding row and column" on the new page.
        var emulator = Build();
        Feed(emulator, Esc + "[24t");
        Feed(emulator, Esc + "[3;8H");

        Feed(emulator, Esc + "[4 P");          // PPA to page 4

        Assert.Equal(4, emulator.CurrentPage);
        Assert.Equal(2, emulator.GetCursor().Row);
        Assert.Equal(7, emulator.GetCursor().Column);
    }

    [Fact]
    public void PagePositionRelativeAndBackwardKeepItToo()
    {
        var emulator = Build();
        Feed(emulator, Esc + "[24t");
        Feed(emulator, Esc + "[3;8H");

        Feed(emulator, Esc + "[2 Q");          // PPR forward two
        Assert.Equal(3, emulator.CurrentPage);
        Assert.Equal(2, emulator.GetCursor().Row);
        Assert.Equal(7, emulator.GetCursor().Column);

        Feed(emulator, Esc + "[1 R");          // PPB back one
        Assert.Equal(2, emulator.CurrentPage);
        Assert.Equal(2, emulator.GetCursor().Row);
        Assert.Equal(7, emulator.GetCursor().Column);
    }

    [Fact]
    public void MovingPastTheEndStopsAtTheLastPage()
    {
        // "If Pn tries to move the cursor past the last page in memory, then the cursor stops at
        // the last page."
        var emulator = Build();
        Feed(emulator, Esc + "[24t");

        Feed(emulator, Esc + "[99U");

        Assert.Equal(6, emulator.CurrentPage);
    }

    [Fact]
    public void AndMovingBackTooFarStopsAtTheFirst()
    {
        var emulator = Build();
        Feed(emulator, Esc + "[24t");
        Feed(emulator, Esc + "[2U");

        Feed(emulator, Esc + "[99V");

        Assert.Equal(1, emulator.CurrentPage);
    }

    [Fact]
    public void WithOnlyOnePageTheMovementIsIgnored()
    {
        // "If there is only one page, the terminal ignores NP." The default is a single page, so
        // this is what every host that never asked for pages sees.
        var emulator = Build();
        Feed(emulator, Esc + "[3;8H");

        Feed(emulator, Esc + "[U");

        Assert.Equal(1, emulator.CurrentPage);
        Assert.Equal(2, emulator.GetCursor().Row);
        Assert.Equal(7, emulator.GetCursor().Column);
    }

    // ── What is on the pages ─────────────────────────────────────────────────────────────

    [Fact]
    public void EachPageKeepsItsOwnText()
    {
        // The point of the whole feature: write to a page the user cannot see, then show it.
        var emulator = Build();
        Feed(emulator, Esc + "[24t");
        Feed(emulator, Esc + "[1;1H" + "PAGE ONE");

        Feed(emulator, Esc + "[U");
        Feed(emulator, Esc + "[1;1H" + "PAGE TWO");

        Assert.Equal("PAGE TWO", Row(emulator, 0));

        Feed(emulator, Esc + "[V");
        Assert.Equal("PAGE ONE", Row(emulator, 0));

        Feed(emulator, Esc + "[U");
        Assert.Equal("PAGE TWO", Row(emulator, 0));
    }

    [Fact]
    public void APageNobodyHasWrittenToIsBlank()
    {
        var emulator = Build();
        Feed(emulator, Esc + "[24t");
        Feed(emulator, Esc + "[1;1H" + "PAGE ONE");

        Feed(emulator, Esc + "[5 P");

        Assert.Equal("", Row(emulator, 0));
    }

    [Fact]
    public void ShrinkingThePageCountBringsTheScreenBackToAPageThatExists()
    {
        // Going from six pages to one while sitting on page four leaves nowhere to be.
        var emulator = Build();
        Feed(emulator, Esc + "[24t");
        Feed(emulator, Esc + "[4 P");
        Assert.Equal(4, emulator.CurrentPage);

        Feed(emulator, Esc + "[144t");

        Assert.Equal(1, emulator.PageCount);
        Assert.Equal(1, emulator.CurrentPage);
    }
}

