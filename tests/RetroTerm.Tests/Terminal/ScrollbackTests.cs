using System.Text;
using RetroTerm.Core.Configuration;
using RetroTerm.Core.Terminal.Buffer;
using RetroTerm.Core.Terminal.Emulators;
using Xunit;

namespace RetroTerm.Tests.Terminal;

/// <summary>
/// The scrollback path, audited the way DECSC, RIS and the alternate screen were.
///
/// Scrollback is the one part of a terminal the user owns rather than the host. Everything here
/// asks the same question: can a program put something in the user's history that the user never
/// saw scroll past, or take something out of it that they did?
/// </summary>
public class ScrollbackTests
{
    private static readonly string Esc = ((char)0x1B).ToString();

    private static TerminalEmulatorBase Build()
        => EmulatorFactory.CreateEmulator("VT220", 20, 6, 100);

    private static void Feed(TerminalEmulatorBase emulator, string text)
        => emulator.ProcessData(Encoding.ASCII.GetBytes(text));

    private static string ScrollbackRow(TerminalBuffer buffer, int index)
    {
        var line = buffer.GetScrollbackLine(index);
        if (line == null)
            return "";

        var text = new StringBuilder(line.Length);
        for (int col = 0; col < line.Length; col++)
        {
            text.Append(line[col].Codepoint == 0 ? '.' : (char)line[col].Codepoint);
        }
        return text.ToString().TrimEnd('.');
    }

    [Fact]
    public void ScrollingOnTheAlternateScreenDoesNotEnterTheUsersHistory()
    {
        // THE defect this audit found. The alternate screen swaps which grid _screen points at, and
        // the scroll path pushed row 0 into scrollback without asking which screen it was on. So a
        // pager scrolling its own display filled the user's history with the pager's output - lines
        // that never scrolled off anything the user was reading.
        var emulator = Build();
        Feed(emulator, "shell line\r\n");
        var buffer = emulator.GetBuffer();
        int before = buffer.ScrollbackLineCount;

        Feed(emulator, Esc + "[?1049h");
        for (int i = 1; i <= 12; i++)
        {
            Feed(emulator, "pager " + i + "\r\n");
        }
        Feed(emulator, Esc + "[?1049l");

        Assert.Equal(before, buffer.ScrollbackLineCount);
    }

    [Fact]
    public void AndWhatWasAlreadyInHistoryIsStillReadable()
    {
        // The guard against fixing it by throwing the history away instead.
        var emulator = Build();
        Feed(emulator, "keep me\r\n");
        for (int i = 1; i <= 8; i++)
        {
            Feed(emulator, "line " + i + "\r\n");
        }
        var buffer = emulator.GetBuffer();

        Feed(emulator, Esc + "[?1049h");
        for (int i = 1; i <= 12; i++)
        {
            Feed(emulator, "pager " + i + "\r\n");
        }
        Feed(emulator, Esc + "[?1049l");

        Assert.Equal("keep me", ScrollbackRow(buffer, 0));
    }

    [Fact]
    public void OrdinaryScrollingStillFillsHistory()
    {
        // The other half of the guard: the primary screen must keep doing exactly what it did.
        var emulator = Build();
        for (int i = 1; i <= 10; i++)
        {
            Feed(emulator, "line " + i + "\r\n");
        }

        var buffer = emulator.GetBuffer();
        Assert.True(buffer.ScrollbackLineCount > 0);
        Assert.Equal("line 1", ScrollbackRow(buffer, 0));
    }

    [Fact]
    public void DeletingLinesDoesNotPretendTheyScrolledOff()
    {
        // DL removes lines the application put there. They never scrolled past the user, so they
        // are not history. Already true - kept here so the whole rule lives in one file.
        var emulator = Build();
        Feed(emulator, Esc + "[1;1H" + "FIRST");
        var buffer = emulator.GetBuffer();
        int before = buffer.ScrollbackLineCount;

        Feed(emulator, Esc + "[1;1H" + Esc + "[M");   // DL at the top of the screen

        Assert.Equal(before, buffer.ScrollbackLineCount);
    }

    [Fact]
    public void AScrollingRegionThatStartsBelowTheTopKeepsNothing()
    {
        // A region scroll moves lines inside a box the application drew. The line leaving the top
        // of that box is the application's, not the user's.
        var emulator = Build();
        var buffer = emulator.GetBuffer();
        Feed(emulator, Esc + "[2;5r");                // region rows 2..5
        int before = buffer.ScrollbackLineCount;

        Feed(emulator, Esc + "[5;1H");
        for (int i = 0; i < 8; i++)
        {
            Feed(emulator, "x\n");
        }

        Assert.Equal(before, buffer.ScrollbackLineCount);
    }

    [Fact]
    public void HistoryLinesKeepTheWidthTheyWereWrittenAt()
    {
        // The ring stores whole rows. When the screen gets narrower the old rows stay their old
        // length on purpose - a history line is what was on the screen at the time, and trimming it
        // to the new width would silently lose the text on the right.
        var emulator = Build();
        Feed(emulator, "0123456789ABCDEFGHI\r\n");
        for (int i = 0; i < 8; i++)
        {
            Feed(emulator, "filler\r\n");
        }
        var buffer = emulator.GetBuffer();

        emulator.Resize(10, 6);

        Assert.Equal("0123456789ABCDEFGHI", ScrollbackRow(buffer, 0));
    }

    [Fact]
    public void AHardResetEmptiesTheHistory()
    {
        // RIS means "just switched on", and a terminal that has just been switched on has nothing
        // to scroll back to.
        var emulator = Build();
        for (int i = 1; i <= 10; i++)
        {
            Feed(emulator, "line " + i + "\r\n");
        }
        var buffer = emulator.GetBuffer();
        Assert.True(buffer.ScrollbackLineCount > 0);

        emulator.Reset();

        Assert.Equal(0, buffer.ScrollbackLineCount);
    }
}
