using System.Text;
using RetroTerm.Core.Configuration;
using RetroTerm.Core.Terminal.Emulators;
using Xunit;

namespace RetroTerm.Tests.Terminal;

/// <summary>
/// Editing the last column disarms the pending wrap, and VPR is not CUD.
/// </summary>
/// <remarks>
/// Three rules that five two-row xterm.js fixtures settled together. All three are about what
/// happens at the RIGHT-HAND EDGE of the screen, where the Last Column Flag is armed and every
/// off-by-one shows up as a character on the wrong row.
///
/// The two disarm rules come from t0050-ICH and t0055-EL. Each fills all 80 columns, which arms
/// the flag at column 79, then edits that column and prints. A real xterm prints on the SAME row,
/// over the character the edit removed.
///
/// The VPR rule comes from a pair: t0075-DECSTBM_CUU_CUD and t0079-DECSTBM_VPR are the same
/// stream apart from the final letter, and xterm answers differently for the two.
/// </remarks>
public class LastColumnEditTests
{
    private const string Esc = "";

    private static TerminalEmulatorBase Build(int width = 10, int height = 5)
        => EmulatorFactory.CreateEmulator("XTERM", width, height, 100);

    private static void Feed(TerminalEmulatorBase emulator, string text)
        => emulator.ProcessData(Encoding.ASCII.GetBytes(text));

    private static string Row(TerminalEmulatorBase emulator, int row)
    {
        var buffer = emulator.GetBuffer();
        var sb = new StringBuilder(buffer.Width);
        for (int column = 0; column < buffer.Width; column++)
        {
            uint codepoint = buffer[row, column].Codepoint;
            sb.Append(codepoint == 0 || codepoint == ' ' ? '.' : (char)codepoint);
        }
        return sb.ToString().TrimEnd('.');
    }

    [Fact]
    public void InsertingCharactersAtTheLastColumnDisarmsTheWrap()
    {
        var emulator = Build();
        Feed(emulator, "ABCDEFGHIJ");        // fills the row, arming the flag at column 9
        Feed(emulator, Esc + "[3@");         // ICH pushes "J" off the end, cursor stays put
        Feed(emulator, "!");

        // The "!" belongs in column 9, on the row the flag was armed on.
        Assert.Equal("ABCDEFGHI!", Row(emulator, 0));
        Assert.Equal("", Row(emulator, 1));
    }

    [Fact]
    public void AndTheWrapIsArmedAgainByThatCharacter()
    {
        // The disarm must not turn into "this row never wraps again" - the character written into
        // the last column arms the flag afresh, so the one after it goes to the next row. That is
        // exactly what t0050-ICH shows with "!@#": the "!" stays, the "@" and "#" wrap.
        var emulator = Build();
        Feed(emulator, "ABCDEFGHIJ");
        Feed(emulator, Esc + "[3@");
        Feed(emulator, "!@#");

        Assert.Equal("ABCDEFGHI!", Row(emulator, 0));
        Assert.Equal("@#", Row(emulator, 1));
    }

    [Theory]
    [InlineData("[K")]     // EL 0 - to the end of the line, the form the fixture proves
    [InlineData("[0K")]
    [InlineData("[1K")]    // to the start
    [InlineData("[2K")]    // the whole line
    public void ErasingInTheLineDisarmsTheWrap(string erase)
    {
        // Whichever part is erased, the character that armed the flag is gone, so the wrap it
        // asked for means nothing. Only the first form appears in the fixture; the reason covers
        // all of them.
        var emulator = Build();
        Feed(emulator, "ABCDEFGHIJ");
        Feed(emulator, Esc + erase);
        Feed(emulator, "!");

        Assert.Equal('!', emulator.GetBuffer()[0, 9].Codepoint);
        Assert.Equal("", Row(emulator, 1));
    }

    [Fact]
    public void CursorDownStopsAtTheBottomMarginEvenFromAboveTheRegion()
    {
        // t0075-DECSTBM_CUU_CUD. Setting the region homes the cursor to row 0, which is ABOVE the
        // region, and CUD still stops on the region's last row.
        var emulator = Build(width: 10, height: 25);

        Feed(emulator, Esc + "[10;19r");     // rows 9..18
        Feed(emulator, Esc + "[H");
        Feed(emulator, Esc + "[25B");

        Assert.Equal(18, emulator.GetCursor().Row);
    }

    [Fact]
    public void ButVerticalPositionRelativeRunsStraightPastIt()
    {
        // t0079-DECSTBM_VPR, the same three lines with CSI 25 e in place of CSI 25 B. The margin
        // does not stop it, and the cursor reaches the last row of the screen.
        var emulator = Build(width: 10, height: 25);

        Feed(emulator, Esc + "[10;19r");
        Feed(emulator, Esc + "[H");
        Feed(emulator, Esc + "[25e");

        Assert.Equal(24, emulator.GetCursor().Row);
    }

    [Fact]
    public void OriginModeStillConfinesVerticalPositionRelative()
    {
        // Ignoring the margin is not the same as ignoring origin mode. Under DECOM the host may
        // not address a row outside the region at all, and VPR is bounded by that instead.
        // NOT COVERED BY ANY FIXTURE - it follows from VPR being a position function, the same
        // way VPA is bounded.
        var emulator = Build(width: 10, height: 25);

        Feed(emulator, Esc + "[10;19r");
        Feed(emulator, Esc + "[?6h");        // DECOM on - homes into the region
        Feed(emulator, Esc + "[H");
        Feed(emulator, Esc + "[25e");

        Assert.Equal(18, emulator.GetCursor().Row);
    }
}
