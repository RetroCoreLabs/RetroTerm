using System.Collections.Generic;
using System.Text;
using RetroTerm.Core.Configuration;
using RetroTerm.Core.Terminal.Emulators;
using Xunit;

namespace RetroTerm.Tests.Terminal;

/// <summary>
/// Changing the HEIGHT slides the screen over the scrollback instead of cutting off the bottom.
///
/// A terminal window is a window onto a longer history. Making it shorter has to scroll the top
/// rows up into that history and keep what is at the bottom — the prompt, the last line of
/// output, whatever the user was looking at. Resize kept the top corner and truncated, so making
/// the window shorter threw away the NEWEST content and left the cursor pointing at nothing.
///
/// Making it taller does the reverse: rows come back out of scrollback onto the top.
///
/// The subtlety worth naming: it only scrolls as much as it has to. Rows cut off the bottom that
/// were never written cost nothing to lose, so a screen holding ten lines of output shrinking
/// from 25 rows to 20 just truncates and nothing moves. Scrolling regardless would push the top
/// of the output into history for no reason and drag the cursor up with it.
///
/// Found by libvterm's 63screen_resize script — see tests\RetroTerm.Tests\Conformance.
/// </summary>
public class HeightResizeThroughHistoryTests
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
        int lastWritten = 0;
        for (int col = 0; col < emulator.Width; col++)
        {
            emulator.GetBuffer().TryGetCell(row, col, out var cell);
            uint cp = cell.Codepoint;
            text.Append(cp == 0 ? ' ' : (char)cp);
            if (cp != 0) lastWritten = text.Length;
        }
        return text.ToString(0, lastWritten);
    }

    [Theory]
    [MemberData(nameof(AllEmulators))]
    public void ShrinkingKeepsTheBottomOfTheScreen(string emulatorType)
    {
        // The exact case from the corpus. Before this, "Line 25" — the newest content — was the
        // part thrown away.
        var emulator = Build(emulatorType, 80, 25);
        Feed(emulator, "Top\u001b[25HLine 25\u001b[15H");

        emulator.Resize(80, 20);

        Assert.Equal("", Row(emulator, 0));
        Assert.Equal("Line 25", Row(emulator, 19));
        Assert.Equal(9, emulator.GetCursor().Row);      // rode up with its own line, 14 - 5
    }

    [Theory]
    [MemberData(nameof(AllEmulators))]
    public void ShrinkingPutsTheScrolledRowsIntoHistory(string emulatorType)
    {
        var emulator = Build(emulatorType, 80, 25);
        Feed(emulator, "Top\u001b[25HLine 25\u001b[15H");

        int before = emulator.GetBuffer().ScrollbackLineCount;
        emulator.Resize(80, 20);

        Assert.Equal(before + 5, emulator.GetBuffer().ScrollbackLineCount);
    }

    [Theory]
    [MemberData(nameof(AllEmulators))]
    public void ShrinkingOverBlankRowsJustTruncates(string emulatorType)
    {
        // Nothing below row 9 was ever written, so nothing has to move.
        var emulator = Build(emulatorType, 80, 25);
        Feed(emulator, "Top\u001b[10HLine 10");

        int before = emulator.GetBuffer().ScrollbackLineCount;
        emulator.Resize(80, 20);

        Assert.Equal("Top", Row(emulator, 0));
        Assert.Equal("Line 10", Row(emulator, 9));
        Assert.Equal(9, emulator.GetCursor().Row);
        Assert.Equal(before, emulator.GetBuffer().ScrollbackLineCount);
    }

    [Theory]
    [MemberData(nameof(AllEmulators))]
    public void ShrinkingNeverSendsTheCursorAboveTheTop(string emulatorType)
    {
        // A cursor already near the top has nowhere to go. A negative row is how this crashed.
        var emulator = Build(emulatorType, 80, 25);
        Feed(emulator, "\u001b[24HLine 24\r\nLine 25\u001b[H");
        Assert.Equal(0, emulator.GetCursor().Row);

        emulator.Resize(80, 20);

        Assert.Equal(0, emulator.GetCursor().Row);
    }

    [Theory]
    [MemberData(nameof(AllEmulators))]
    public void ShrinkingKeepsTheLineTheCursorIsOn(string emulatorType)
    {
        // Even when everything below is blank, the cursor's own line must stay on screen.
        var emulator = Build(emulatorType, 80, 25);
        Feed(emulator, "\u001b[25HBottom");
        Assert.Equal(24, emulator.GetCursor().Row);

        emulator.Resize(80, 24);

        Assert.Equal("Bottom", Row(emulator, 23));
        Assert.Equal(23, emulator.GetCursor().Row);
    }

    [Theory]
    [MemberData(nameof(AllEmulators))]
    public void GrowingPullsRowsBackOutOfHistory(string emulatorType)
    {
        var emulator = Build(emulatorType, 80, 25);
        Feed(emulator, "Top\u001b[25HLine 25\u001b[15H");

        emulator.Resize(80, 20);       // pushes 5 into history
        emulator.Resize(80, 25);       // and takes them back

        Assert.Equal("Top", Row(emulator, 0));
        Assert.Equal("Line 25", Row(emulator, 24));
        Assert.Equal(14, emulator.GetCursor().Row);
    }

    [Theory]
    [MemberData(nameof(AllEmulators))]
    public void GrowingWithNoHistoryJustAddsBlankRowsAtTheBottom(string emulatorType)
    {
        var emulator = Build(emulatorType, 80, 10);
        Feed(emulator, "Hello");

        emulator.Resize(80, 20);

        Assert.Equal("Hello", Row(emulator, 0));
        Assert.Equal(0, emulator.GetCursor().Row);
        Assert.Equal("", Row(emulator, 19));
    }
}
