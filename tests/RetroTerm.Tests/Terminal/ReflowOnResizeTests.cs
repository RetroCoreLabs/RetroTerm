using System.Collections.Generic;
using System.Text;
using RetroTerm.Core.Configuration;
using RetroTerm.Core.Terminal.Emulators;
using Xunit;

namespace RetroTerm.Tests.Terminal;

/// <summary>
/// Reflow on resize: a paragraph that wrapped is re-laid out at the new width instead of being
/// cut off at the edge of the screen.
///
/// Before this, resizing preserved the top-left corner and nothing else, so dragging a window
/// narrower threw away the right-hand side of every wrapped line permanently — widening it again
/// did not bring the text back, because it was gone from the buffer.
///
/// The per-line wrap flags exist for exactly this. In the grid a line that filled up and carried
/// on looks identical to one that ended with a newline; the flag is the only record of which it
/// was. <c>TerminalBuffer.Resize</c> has carried a comment since the flags were added saying they
/// were what "a future reflow will need" — this is that reflow.
///
/// Verified against libvterm's 69screen_reflow, which went from 29 disagreements to 9.
/// </summary>
public class ReflowOnResizeTests
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

    private static TerminalEmulatorBase Build(string type, int width, int height)
        => EmulatorFactory.CreateEmulator(type, width, height, 100);

    private static void Feed(TerminalEmulatorBase emulator, string text)
        => emulator.ProcessData(Encoding.ASCII.GetBytes(text));

    private static string Row(TerminalEmulatorBase emulator, int row)
    {
        var text = new StringBuilder(emulator.Width);
        for (int col = 0; col < emulator.Width; col++)
        {
            emulator.GetBuffer().TryGetCell(row, col, out var cell);
            uint cp = cell.Codepoint;
            text.Append(cp == 0 ? ' ' : (char)cp);
        }

        int end = text.Length;
        while (end > 0 && text[end - 1] == ' ') end--;
        return text.ToString(0, end);
    }

    [Theory]
    [MemberData(nameof(AllEmulators))]
    public void WideningRejoinsAWrappedParagraph(string emulatorType)
    {
        // The exact case from the corpus: 12 characters on a 10-column screen wrap onto two rows,
        // and widening to 15 must pull them back onto one.
        var emulator = Build(emulatorType, 10, 5);
        Feed(emulator, new string('A', 12));

        Assert.Equal("AAAAAAAAAA", Row(emulator, 0));
        Assert.Equal("AA", Row(emulator, 1));
        Assert.True(emulator.GetBuffer().IsLineWrapped(0));

        emulator.Resize(15, 5);

        Assert.Equal("AAAAAAAAAAAA", Row(emulator, 0));
        Assert.Equal("", Row(emulator, 1));
        Assert.False(emulator.GetBuffer().IsLineWrapped(0), "the paragraph fits on one row now");
    }

    [Theory]
    [MemberData(nameof(AllEmulators))]
    public void TheCursorTravelsWithItsTextNotItsGridPosition(string emulatorType)
    {
        var emulator = Build(emulatorType, 10, 5);
        Feed(emulator, new string('A', 12));
        Assert.Equal(1, emulator.GetCursor().Row);
        Assert.Equal(2, emulator.GetCursor().Column);

        emulator.Resize(15, 5);

        // Still just after the twelfth A — now on row 0 because that is where the text went.
        Assert.Equal(0, emulator.GetCursor().Row);
        Assert.Equal(12, emulator.GetCursor().Column);
    }

    [Theory]
    [MemberData(nameof(AllEmulators))]
    public void NarrowingSplitsALineAndMarksTheContinuation(string emulatorType)
    {
        var emulator = Build(emulatorType, 10, 5);
        Feed(emulator, "ABCDEFGHI");

        emulator.Resize(8, 5);

        Assert.Equal("ABCDEFGH", Row(emulator, 0));
        Assert.Equal("I", Row(emulator, 1));
        Assert.True(emulator.GetBuffer().IsLineWrapped(0), "row 0 continues onto row 1 now");
    }

    [Theory]
    [MemberData(nameof(AllEmulators))]
    public void NarrowingDoesNotScrollTextAwayToMakeRoomForBlanks(string emulatorType)
    {
        // The bug this pins: a narrower screen needs MORE rows for the same paragraph, and when
        // the blank rows below it counted towards the total they pushed the real text off the top
        // into scrollback. Shrinking a screen holding one line of text scrolled that line away and
        // left only its tail showing.
        var emulator = Build(emulatorType, 10, 5);
        Feed(emulator, "ABCDEFGHI");

        emulator.Resize(6, 5);

        Assert.Equal("ABCDEF", Row(emulator, 0));
        Assert.Equal("GHI", Row(emulator, 1));
    }

    [Theory]
    [MemberData(nameof(AllEmulators))]
    public void ARoundTripThroughANarrowerWidthKeepsTheText(string emulatorType)
    {
        // The user-visible promise: drag it narrow, drag it back, nothing is lost.
        var emulator = Build(emulatorType, 20, 5);
        Feed(emulator, "The quick brown fox jumps");

        emulator.Resize(8, 5);
        emulator.Resize(20, 5);

        // Row() trims trailing blanks, so the twentieth column (a space) does not show here.
        Assert.Equal("The quick brown fox", Row(emulator, 0));
        Assert.Equal("jumps", Row(emulator, 1));
    }

    [Theory]
    [MemberData(nameof(AllEmulators))]
    public void SeparateLinesAreNotJoinedTogether(string emulatorType)
    {
        // Only rows the wrap flag marks as continuations may be rejoined. Two lines that ended
        // with a newline must stay two lines however wide the screen gets.
        var emulator = Build(emulatorType, 10, 5);
        Feed(emulator, "one\r\ntwo");

        emulator.Resize(40, 5);

        Assert.Equal("one", Row(emulator, 0));
        Assert.Equal("two", Row(emulator, 1));
    }

    [Theory]
    [MemberData(nameof(AllEmulators))]
    public void AHeightOnlyChangeLeavesTheTextAlone(string emulatorType)
    {
        // Nothing re-wraps when the width does not change, so this must take the cheap path.
        var emulator = Build(emulatorType, 10, 5);
        Feed(emulator, new string('A', 12));

        emulator.Resize(10, 8);

        Assert.Equal("AAAAAAAAAA", Row(emulator, 0));
        Assert.Equal("AA", Row(emulator, 1));
        Assert.True(emulator.GetBuffer().IsLineWrapped(0));
    }

    [Theory]
    [MemberData(nameof(AllEmulators))]
    public void TheAlternateBufferIsNeverReflowed(string emulatorType)
    {
        // A full-screen program owns the alternate buffer, is told the new size, and repaints.
        // Re-wrapping its lines underneath it would corrupt a display it is about to redraw.
        var emulator = Build(emulatorType, 10, 5);
        Feed(emulator, "\u001b[?1049h");        // switch to the alternate screen
        Feed(emulator, new string('B', 12));

        emulator.Resize(20, 5);

        Assert.Equal("BBBBBBBBBB", Row(emulator, 0));
        Assert.Equal("BB", Row(emulator, 1));
    }
}
