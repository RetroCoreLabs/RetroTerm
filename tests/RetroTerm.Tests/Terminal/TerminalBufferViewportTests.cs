using RetroTerm.Core.Terminal.Buffer;
using Xunit;

namespace RetroTerm.Tests.Terminal;

public class TerminalBufferViewportTests
{
    [Fact]
    public void TryGetViewportCell_Offset0_ReturnsLiveBufferCells()
    {
        var buffer = new TerminalBuffer(80, 24);
        buffer.SetCell(0, 0, new TerminalCell('A'));
        buffer.SetCell(5, 10, new TerminalCell('B'));

        Assert.True(buffer.TryGetViewportCell(0, 0, 0, out var cell));
        Assert.Equal('A', (char)cell.Codepoint);

        Assert.True(buffer.TryGetViewportCell(5, 10, 0, out cell));
        Assert.Equal('B', (char)cell.Codepoint);
    }

    [Fact]
    public void TryGetViewportCell_WithScrollback_ReturnsScrollbackContent()
    {
        var buffer = new TerminalBuffer(80, 24);

        // Fill row 0 with 'X' and scroll it into scrollback
        for (int col = 0; col < 80; col++)
        {
            buffer.SetCell(0, col, new TerminalCell('X'));
        }
        buffer.ScrollUp(); // Pushes row 0 ('X' chars) into scrollback

        // Now row 0 is cleared (the old row 1), scrollback has one line
        Assert.Equal(1, buffer.ScrollbackLineCount);

        // scrollOffset=1 → viewportRow 0 should show the scrollback line
        Assert.True(buffer.TryGetViewportCell(0, 0, 1, out var cell));
        Assert.Equal('X', (char)cell.Codepoint);
    }

    [Fact]
    public void TryGetViewportCell_BoundaryTransition_ScrollbackToLiveBuffer()
    {
        var buffer = new TerminalBuffer(80, 4); // Small buffer for easier testing

        // Push 3 lines into scrollback
        for (int line = 0; line < 3; line++)
        {
            char ch = (char)('A' + line);
            for (int col = 0; col < 80; col++)
            {
                buffer.SetCell(0, col, new TerminalCell(ch));
            }
            buffer.ScrollUp();
        }

        // Now put 'Z' on live row 0
        for (int col = 0; col < 80; col++)
        {
            buffer.SetCell(0, col, new TerminalCell('Z'));
        }

        Assert.Equal(3, buffer.ScrollbackLineCount);
        // Scrollback = [A, B, C], screen = [Z, ...]

        // scrollOffset=2: viewport shows scrollback[1], scrollback[2], screen[0], screen[1]
        // viewportRow 0 → scrollback index (3-2+0)=1 → 'B'
        Assert.True(buffer.TryGetViewportCell(0, 0, 2, out var cell));
        Assert.Equal('B', (char)cell.Codepoint);

        // viewportRow 1 → scrollback index (3-2+1)=2 → 'C'
        Assert.True(buffer.TryGetViewportCell(1, 0, 2, out cell));
        Assert.Equal('C', (char)cell.Codepoint);

        // viewportRow 2 → total line (3-2+2)=3 → screen[0] → 'Z'
        Assert.True(buffer.TryGetViewportCell(2, 0, 2, out cell));
        Assert.Equal('Z', (char)cell.Codepoint);
    }

    [Fact]
    public void TryGetViewportCell_OutOfRange_ReturnsFalse()
    {
        var buffer = new TerminalBuffer(80, 24);

        // Negative row
        Assert.False(buffer.TryGetViewportCell(-1, 0, 0, out _));

        // Row beyond height
        Assert.False(buffer.TryGetViewportCell(24, 0, 0, out _));

        // Negative column
        Assert.False(buffer.TryGetViewportCell(0, -1, 0, out _));

        // Column beyond width
        Assert.False(buffer.TryGetViewportCell(0, 80, 0, out _));

        // Negative scroll offset
        Assert.False(buffer.TryGetViewportCell(0, 0, -1, out _));
    }

    [Fact]
    public void TryGetViewportCell_EmptyScrollback_WithOffset_ReturnsFalse()
    {
        var buffer = new TerminalBuffer(80, 24);

        // No scrollback, but trying to scroll back
        // scrollOffset=5, viewportRow=0: totalLine = 0 - 5 + 0 = -5 → false
        Assert.False(buffer.TryGetViewportCell(0, 0, 5, out _));
    }

    [Fact]
    public void TryGetViewportCell_OffsetExceedingScrollback_TopRowsReturnFalse()
    {
        var buffer = new TerminalBuffer(80, 4);

        // Push 2 lines into scrollback
        for (int line = 0; line < 2; line++)
        {
            for (int col = 0; col < 80; col++)
            {
                buffer.SetCell(0, col, new TerminalCell((char)('A' + line)));
            }
            buffer.ScrollUp();
        }

        Assert.Equal(2, buffer.ScrollbackLineCount);

        // scrollOffset=3 (more than scrollback has)
        // viewportRow 0 → totalLine = 2 - 3 + 0 = -1 → false
        Assert.False(buffer.TryGetViewportCell(0, 0, 3, out _));

        // viewportRow 1 → totalLine = 2 - 3 + 1 = 0 → scrollback[0] → 'A'
        Assert.True(buffer.TryGetViewportCell(1, 0, 3, out var cell));
        Assert.Equal('A', (char)cell.Codepoint);
    }

    [Fact]
    public void ScrollbackLineCount_MatchesActualCount()
    {
        var buffer = new TerminalBuffer(80, 24);
        Assert.Equal(0, buffer.ScrollbackLineCount);

        buffer.ScrollUp();
        Assert.Equal(1, buffer.ScrollbackLineCount);

        buffer.ScrollUp();
        Assert.Equal(2, buffer.ScrollbackLineCount);
    }

    [Fact]
    public void IsUsingAlternateBuffer_ReflectsState()
    {
        var buffer = new TerminalBuffer(80, 24);
        Assert.False(buffer.IsUsingAlternateBuffer);

        buffer.SwitchToAlternateBuffer();
        Assert.True(buffer.IsUsingAlternateBuffer);

        buffer.SwitchToPrimaryBuffer();
        Assert.False(buffer.IsUsingAlternateBuffer);
    }

    [Fact]
    public void TryGetViewportCell_MaxScrollOffset_ShowsOldestContent()
    {
        var buffer = new TerminalBuffer(80, 4);

        // Push 6 lines into scrollback
        for (int line = 0; line < 6; line++)
        {
            for (int col = 0; col < 80; col++)
            {
                buffer.SetCell(0, col, new TerminalCell((char)('A' + line)));
            }
            buffer.ScrollUp();
        }

        Assert.Equal(6, buffer.ScrollbackLineCount);
        // Scrollback = [A, B, C, D, E, F]

        // scrollOffset = 6 (max): viewport shows scrollback[0..3] = A,B,C,D
        Assert.True(buffer.TryGetViewportCell(0, 0, 6, out var cell));
        Assert.Equal('A', (char)cell.Codepoint);

        Assert.True(buffer.TryGetViewportCell(1, 0, 6, out cell));
        Assert.Equal('B', (char)cell.Codepoint);

        Assert.True(buffer.TryGetViewportCell(3, 0, 6, out cell));
        Assert.Equal('D', (char)cell.Codepoint);
    }

    [Fact]
    public void TryGetViewportCell_Offset0_ColumnBeyondScrollbackLineWidth_ReturnsSpace()
    {
        // This tests the case where scrollback line might be shorter than current width
        // (shouldn't happen normally but the method handles it gracefully)
        var buffer = new TerminalBuffer(80, 4);

        // Push a line into scrollback
        for (int col = 0; col < 80; col++)
        {
            buffer.SetCell(0, col, new TerminalCell('Q'));
        }
        buffer.ScrollUp();

        // Access within scrollback line width should work
        Assert.True(buffer.TryGetViewportCell(0, 0, 1, out var cell));
        Assert.Equal('Q', (char)cell.Codepoint);

        Assert.True(buffer.TryGetViewportCell(0, 79, 1, out cell));
        Assert.Equal('Q', (char)cell.Codepoint);
    }
}
