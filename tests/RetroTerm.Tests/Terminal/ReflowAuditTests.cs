using System.Text;
using RetroTerm.Core.Configuration;
using RetroTerm.Core.Terminal.Buffer;
using RetroTerm.Core.Terminal.Emulators;
using Xunit;

namespace RetroTerm.Tests.Terminal;

/// <summary>
/// The reflow path, audited the way DECSC, RIS, the alternate screen and scrollback were.
///
/// Reflow is where text moves in both directions between the screen and the history, which is
/// exactly the shape the previous findings had. The questions are the same: does anything get
/// lost on the way, and does anything end up in a place the user never put it?
/// </summary>
public class ReflowAuditTests
{
    private static readonly string Esc = ((char)0x1B).ToString();

    private static TerminalEmulatorBase Build(int width = 20, int height = 6)
        => EmulatorFactory.CreateEmulator("VT220", width, height, 100);

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
    public void NarrowingReWrapsAParagraphInsteadOfCuttingIt()
    {
        var emulator = Build();
        Feed(emulator, "ABCDEFGHIJKLMNOPQRST");   // exactly one full row

        emulator.Resize(10, 6);

        Assert.Equal("ABCDEFGHIJ", Row(emulator, 0));
        Assert.Equal("KLMNOPQRST", Row(emulator, 1));
    }

    [Fact]
    public void WideningJoinsItBackUp()
    {
        var emulator = Build(10, 6);
        Feed(emulator, "ABCDEFGHIJKLMNOPQRST");

        emulator.Resize(20, 6);

        Assert.Equal("ABCDEFGHIJKLMNOPQRST", Row(emulator, 0));
    }

    [Fact]
    public void ARoundTripGivesBackWhatWasThere()
    {
        // Narrow then widen. Anything the first move loses, the second cannot put back.
        var emulator = Build();
        Feed(emulator, "ABCDEFGHIJKLMNOPQRST");

        emulator.Resize(10, 6);
        emulator.Resize(20, 6);

        Assert.Equal("ABCDEFGHIJKLMNOPQRST", Row(emulator, 0));
    }

    [Fact]
    public void TheCursorStaysOnItsOwnCharacter()
    {
        // The cursor is remembered as an offset into the paragraph, not a grid position, so it must
        // come back next to the same text however the text moves.
        var emulator = Build();
        Feed(emulator, "ABCDEFGHIJKLMNO");
        Feed(emulator, Esc + "[1;13H");            // sitting on the 'M', column 12

        emulator.Resize(10, 6);

        Assert.Equal(1, emulator.GetCursor().Row);
        Assert.Equal(2, emulator.GetCursor().Column);
    }

    [Fact]
    public void HistoryThatWasAlreadyThereSurvivesAWidthChange()
    {
        // Reflow rebuilds the screen. The ring is not part of that and must come through untouched.
        var emulator = Build();
        for (int i = 1; i <= 10; i++)
        {
            Feed(emulator, "line " + i + "\r\n");
        }
        var buffer = emulator.GetBuffer();
        int before = buffer.ScrollbackLineCount;
        Assert.True(before > 0);

        emulator.Resize(10, 6);

        Assert.Equal(before, buffer.ScrollbackLineCount);
        Assert.Equal("line 1", ScrollbackRow(buffer, 0));
    }

    [Fact]
    public void WhatSpillsOffTheTopGoesToHistoryOldestFirst()
    {
        // Narrowing makes paragraphs taller, so a full screen no longer fits. The rows that leave
        // are the OLDEST ones and they must arrive in history in that order.
        var emulator = Build(20, 3);
        Feed(emulator, "AAAAAAAAAAAAAAAAAAAA");
        Feed(emulator, "BBBBBBBBBBBBBBBBBBBB");
        Feed(emulator, "CCCCCCCCCCCCCCCCCCCC");
        var buffer = emulator.GetBuffer();

        emulator.Resize(10, 3);

        // Six rows of text into a three-row screen: the first three spill.
        Assert.Equal(3, buffer.ScrollbackLineCount);
        Assert.Equal("AAAAAAAAAA", ScrollbackRow(buffer, 0));
        Assert.Equal("AAAAAAAAAA", ScrollbackRow(buffer, 1));
        Assert.Equal("BBBBBBBBBB", ScrollbackRow(buffer, 2));
    }

    [Fact]
    public void TheAlternateScreenIsResizedAndNotReflowed()
    {
        // A full-screen program is told the new size and repaints. Re-wrapping its display
        // underneath it would corrupt something it is about to draw over anyway.
        var emulator = Build();
        Feed(emulator, Esc + "[?1049h");
        Feed(emulator, "ABCDEFGHIJKLMNOPQRST");
        var buffer = emulator.GetBuffer();

        emulator.Resize(10, 6);

        Assert.Equal("ABCDEFGHIJ", Row(emulator, 0));
        Assert.Equal("", Row(emulator, 1));
        Assert.Equal(0, buffer.ScrollbackLineCount);
    }

    [Fact]
    public void ShrinkingTheHeightKeepsTheBottomAndPutsTheTopInHistory()
    {
        // A window is a view onto a longer history. Making it shorter must keep what the user was
        // looking at - the last line - not the top corner.
        var emulator = Build(20, 6);
        for (int i = 1; i <= 6; i++)
        {
            Feed(emulator, "row " + i + (i < 6 ? "\r\n" : ""));
        }
        var buffer = emulator.GetBuffer();

        emulator.Resize(20, 3);

        Assert.Equal("row 6", Row(emulator, 2));
        Assert.Equal(3, buffer.ScrollbackLineCount);
        Assert.Equal("row 1", ScrollbackRow(buffer, 0));
    }

    [Fact]
    public void AHistoryLineWiderThanTheScreenIsNotSilentlyCutWhenItComesBack()
    {
        // The sharp edge of storing history at the width it was written at. Narrow the screen, then
        // make it taller: rows come back out of the ring at their OLD width onto a screen that is
        // now narrower than they are. Copying the first N columns and dropping the rest would lose
        // text the user can see in their scrollback - so the tail wraps onto the next row instead.
        var emulator = Build(20, 4);
        Feed(emulator, "ABCDEFGHIJKLMNOPQRST\r\n");
        for (int i = 0; i < 5; i++)
        {
            Feed(emulator, "filler\r\n");
        }
        var buffer = emulator.GetBuffer();
        Assert.Equal("ABCDEFGHIJKLMNOPQRST", ScrollbackRow(buffer, 0));

        emulator.Resize(10, 4);      // history keeps its 20-column lines
        emulator.Resize(10, 12);     // and now they are pulled back onto a 10-column screen

        var seen = new StringBuilder();
        for (int row = 0; row < emulator.Height; row++)
        {
            seen.Append(Row(emulator, row));
        }

        Assert.Contains("ABCDEFGHIJKLMNOPQRST", seen.ToString());
    }

    [Fact]
    public void GrowingTheHeightPullsThoseRowsBackOut()
    {
        // And the reverse, so a window that was shrunk and reopened shows what it showed before.
        var emulator = Build(20, 6);
        for (int i = 1; i <= 6; i++)
        {
            Feed(emulator, "row " + i + (i < 6 ? "\r\n" : ""));
        }
        var buffer = emulator.GetBuffer();

        emulator.Resize(20, 3);
        emulator.Resize(20, 6);

        Assert.Equal("row 1", Row(emulator, 0));
        Assert.Equal("row 6", Row(emulator, 5));
        Assert.Equal(0, buffer.ScrollbackLineCount);
    }
}
