using RetroTerm.Core.Terminal.Buffer;

namespace RetroTerm.Tests.Terminal.Buffer;

public class TerminalBufferTests
{
    [Fact]
    public void Constructor_Should_Create_Buffer_With_Correct_Dimensions()
    {
        var buffer = new TerminalBuffer(80, 24);

        Assert.Equal(80, buffer.Width);
        Assert.Equal(24, buffer.Height);
        Assert.Equal(0, buffer.ScrollbackLineCount);
    }

    [Theory]
    [InlineData(0, 24)]
    [InlineData(80, 0)]
    [InlineData(-1, 24)]
    [InlineData(80, -1)]
    public void Constructor_Should_Throw_For_Invalid_Dimensions(int width, int height)
    {
        Action act = () => new TerminalBuffer(width, height);
        Assert.Throws<ArgumentException>(act);
    }

    [Fact]
    public void Clear_Should_Fill_Buffer_With_Empty_Cells()
    {
        var buffer = new TerminalBuffer(80, 24);

        // Set some cells
        buffer[0, 0] = new TerminalCell('A');
        buffer[10, 10] = new TerminalCell('B');

        buffer.Clear();

        // Verify all cells are empty
        for (var row = 0; row < buffer.Height; row++)
        {
            for (var col = 0; col < buffer.Width; col++)
            {
                Assert.True(buffer[row, col].IsEmpty);
            }
        }
    }

    [Fact]
    public void Indexer_Should_Get_And_Set_Cells()
    {
        var buffer = new TerminalBuffer(80, 24);
        var cell = new TerminalCell('X', CharacterAttributes.Bold,
            TerminalColor.FromIndex(StandardColors.Red), TerminalColor.Default);

        buffer[5, 10] = cell;

        Assert.Equal(cell, buffer[5, 10]);
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(0, -1)]
    [InlineData(24, 0)]
    [InlineData(0, 80)]
    public void Indexer_Should_Throw_For_Out_Of_Range(int row, int col)
    {
        var buffer = new TerminalBuffer(80, 24);

        Action act = () => { var _ = buffer[row, col]; };
        Assert.Throws<ArgumentOutOfRangeException>(act);
    }

    [Fact]
    public void TryGetCell_Should_Return_False_For_Out_Of_Range()
    {
        var buffer = new TerminalBuffer(80, 24);

        Assert.False(buffer.TryGetCell(-1, 0, out var cell));
        Assert.False(buffer.TryGetCell(0, -1, out cell));
        Assert.False(buffer.TryGetCell(24, 0, out cell));
        Assert.False(buffer.TryGetCell(0, 80, out cell));
    }

    [Fact]
    public void TryGetCell_Should_Return_True_For_Valid_Position()
    {
        var buffer = new TerminalBuffer(80, 24);
        buffer[10, 20] = new TerminalCell('A');

        var result = buffer.TryGetCell(10, 20, out var cell);

        Assert.True(result);
        Assert.Equal('A', cell.Codepoint);
    }

    [Fact]
    public void ClearLine_Should_Clear_Entire_Line()
    {
        var buffer = new TerminalBuffer(80, 24);

        // Fill line with data
        for (var col = 0; col < buffer.Width; col++)
        {
            buffer[5, col] = new TerminalCell('A');
        }

        buffer.ClearLine(5);

        // Verify line is cleared
        for (var col = 0; col < buffer.Width; col++)
        {
            Assert.True(buffer[5, col].IsEmpty);
        }
    }

    [Fact]
    public void ClearToEndOfLine_Should_Clear_From_Position_To_End()
    {
        var buffer = new TerminalBuffer(80, 24);

        // Fill line with data
        for (var col = 0; col < buffer.Width; col++)
        {
            buffer[5, col] = new TerminalCell('A');
        }

        buffer.ClearToEndOfLine(5, 40);

        // Verify first part is unchanged
        for (var col = 0; col < 40; col++)
        {
            Assert.Equal('A', buffer[5, col].Codepoint);
        }

        // Verify second part is cleared
        for (var col = 40; col < buffer.Width; col++)
        {
            Assert.True(buffer[5, col].IsEmpty);
        }
    }

    [Fact]
    public void ClearFromStartOfLine_Should_Clear_From_Start_To_Position()
    {
        var buffer = new TerminalBuffer(80, 24);

        // Fill line with data
        for (var col = 0; col < buffer.Width; col++)
        {
            buffer[5, col] = new TerminalCell('A');
        }

        buffer.ClearFromStartOfLine(5, 39);

        // Verify first part is cleared
        for (var col = 0; col <= 39; col++)
        {
            Assert.True(buffer[5, col].IsEmpty);
        }

        // Verify second part is unchanged
        for (var col = 40; col < buffer.Width; col++)
        {
            Assert.Equal('A', buffer[5, col].Codepoint);
        }
    }

    [Fact]
    public void ScrollUp_Should_Move_Lines_Up_And_Add_To_Scrollback()
    {
        var buffer = new TerminalBuffer(80, 24, maxScrollbackLines: 100);

        // Fill first line with 'A', second with 'B'
        for (var col = 0; col < buffer.Width; col++)
        {
            buffer[0, col] = new TerminalCell('A');
            buffer[1, col] = new TerminalCell('B');
        }

        buffer.ScrollUp();

        // First line should have 'B' (moved up from line 1)
        Assert.Equal('B', buffer[0, 0].Codepoint);

        // Last line should be empty
        Assert.True(buffer[buffer.Height - 1, 0].IsEmpty);

        // Scrollback should have the old first line with 'A'
        Assert.Equal(1, buffer.ScrollbackLineCount);
        var scrollbackLine = buffer.GetScrollbackLine(0);
        Assert.NotNull(scrollbackLine);
        Assert.Equal('A', scrollbackLine![0].Codepoint);
    }

    [Fact]
    public void ScrollUp_With_Region_Should_Only_Affect_Region()
    {
        var buffer = new TerminalBuffer(80, 24);

        // Fill lines 5-10 with different characters
        for (var row = 5; row <= 10; row++)
        {
            buffer[row, 0] = new TerminalCell((char)('A' + row - 5));
        }

        buffer.ScrollUp(5, 10);

        // Lines before region should be unaffected (empty)
        Assert.True(buffer[4, 0].IsEmpty);

        // Region should have scrolled
        Assert.Equal('B', buffer[5, 0].Codepoint); // Was 'A' at line 5, now 'B' from line 6
        Assert.Equal('F', buffer[9, 0].Codepoint); // Was 'E' at line 9, now 'F' from line 10
        Assert.True(buffer[10, 0].IsEmpty); // Bottom line cleared

        // Lines after region should be unaffected (empty)
        Assert.True(buffer[11, 0].IsEmpty);

        // Scrollback should NOT be updated (region doesn't start at top)
        Assert.Equal(0, buffer.ScrollbackLineCount);
    }

    [Fact]
    public void ScrollDown_Should_Move_Lines_Down()
    {
        var buffer = new TerminalBuffer(80, 24);

        // Fill first two lines
        for (var col = 0; col < buffer.Width; col++)
        {
            buffer[0, col] = new TerminalCell('A');
            buffer[1, col] = new TerminalCell('B');
        }

        buffer.ScrollDown(0, buffer.Height - 1);

        // First line should be empty (cleared)
        Assert.True(buffer[0, 0].IsEmpty);

        // Second line should have 'A' (moved down from line 0)
        Assert.Equal('A', buffer[1, 0].Codepoint);

        // Third line should have 'B' (moved down from line 1)
        Assert.Equal('B', buffer[2, 0].Codepoint);
    }

    [Fact]
    public void InsertLines_Should_Insert_Blank_Lines()
    {
        var buffer = new TerminalBuffer(80, 24);

        // Fill lines
        for (var row = 0; row < 5; row++)
        {
            buffer[row, 0] = new TerminalCell((char)('A' + row));
        }

        // Insert 2 blank lines at row 2
        buffer.InsertLines(2, 2);

        Assert.Equal('A', buffer[0, 0].Codepoint); // Line 0 unchanged
        Assert.Equal('B', buffer[1, 0].Codepoint); // Line 1 unchanged
        Assert.True(buffer[2, 0].IsEmpty);  // Line 2 now blank
        Assert.True(buffer[3, 0].IsEmpty);  // Line 3 now blank
        Assert.Equal('C', buffer[4, 0].Codepoint); // Line 4 now has old line 2 content
    }

    [Fact]
    public void DeleteLines_Should_Delete_Lines()
    {
        var buffer = new TerminalBuffer(80, 24);

        // Fill lines
        for (var row = 0; row < 5; row++)
        {
            buffer[row, 0] = new TerminalCell((char)('A' + row));
        }

        // Delete 2 lines at row 1
        buffer.DeleteLines(1, 2);

        Assert.Equal('A', buffer[0, 0].Codepoint); // Line 0 unchanged
        Assert.Equal('D', buffer[1, 0].Codepoint); // Line 1 now has old line 3 content
        Assert.Equal('E', buffer[2, 0].Codepoint); // Line 2 now has old line 4 content
        Assert.True(buffer[3, 0].IsEmpty);  // Line 3 now blank
        Assert.True(buffer[4, 0].IsEmpty);  // Line 4 now blank
    }

    [Fact]
    public void ScrollbackLimit_Should_Be_Enforced()
    {
        var buffer = new TerminalBuffer(80, 24, maxScrollbackLines: 5);

        // Scroll 10 times
        for (var i = 0; i < 10; i++)
        {
            buffer[0, 0] = new TerminalCell((char)('A' + i));
            buffer.ScrollUp();
        }

        // Should only keep last 5 lines
        Assert.Equal(5, buffer.ScrollbackLineCount);

        // First scrollback line should be 'F' (6th character, since we keep scrollback items 5-9)
        var line = buffer.GetScrollbackLine(0);
        Assert.Equal('F', line![0].Codepoint); // 6th character (index 5)
    }

    [Fact]
    public void ClearScrollback_Should_Remove_All_Scrollback()
    {
        var buffer = new TerminalBuffer(80, 24);

        // Add some scrollback
        for (var i = 0; i < 5; i++)
        {
            buffer.ScrollUp();
        }

        Assert.Equal(5, buffer.ScrollbackLineCount);

        buffer.ClearScrollback();

        Assert.Equal(0, buffer.ScrollbackLineCount);
    }

    [Fact]
    public void Resize_Should_Preserve_Content()
    {
        var buffer = new TerminalBuffer(80, 24);

        // Fill some cells
        buffer[0, 0] = new TerminalCell('A');
        buffer[10, 10] = new TerminalCell('B');

        // Resize larger
        buffer.Resize(100, 30);

        Assert.Equal(100, buffer.Width);
        Assert.Equal(30, buffer.Height);
        Assert.Equal('A', buffer[0, 0].Codepoint);
        Assert.Equal('B', buffer[10, 10].Codepoint);

        // New cells should be empty
        Assert.True(buffer[0, 90].IsEmpty);
        Assert.True(buffer[25, 0].IsEmpty);
    }

    [Fact]
    public void Resize_Smaller_Should_Truncate_Content()
    {
        var buffer = new TerminalBuffer(80, 24);

        buffer[0, 0] = new TerminalCell('A');
        buffer[20, 70] = new TerminalCell('B');

        // Resize smaller
        buffer.Resize(60, 20);

        Assert.Equal(60, buffer.Width);
        Assert.Equal(20, buffer.Height);
        Assert.Equal('A', buffer[0, 0].Codepoint); // Should still exist

        // Cell at [20, 70] should be lost (outside new bounds)
        Action act = () => { var _ = buffer[20, 70]; };
        Assert.Throws<ArgumentOutOfRangeException>(act);
    }

    [Fact]
    public void Resize_Same_Size_Should_Not_Change_Buffer()
    {
        var buffer = new TerminalBuffer(80, 24);
        buffer[5, 10] = new TerminalCell('X');

        buffer.Resize(80, 24);

        Assert.Equal(80, buffer.Width);
        Assert.Equal(24, buffer.Height);
        Assert.Equal('X', buffer[5, 10].Codepoint);
    }

    [Fact]
    public void GetSnapshot_Should_Return_Copy_Of_Buffer()
    {
        var buffer = new TerminalBuffer(80, 24);
        buffer[0, 0] = new TerminalCell('A');
        buffer[10, 10] = new TerminalCell('B');

        var snapshot = buffer.GetSnapshot();

        Assert.Equal(24, snapshot.GetLength(0));
        Assert.Equal(80, snapshot.GetLength(1));
        Assert.Equal('A', snapshot[0, 0].Codepoint);
        Assert.Equal('B', snapshot[10, 10].Codepoint);

        // Modify buffer after snapshot
        buffer[0, 0] = new TerminalCell('Z');

        // Snapshot should be unchanged
        Assert.Equal('A', snapshot[0, 0].Codepoint);
    }
}

