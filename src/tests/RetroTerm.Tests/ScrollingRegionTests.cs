using System;
using System.Text;
using RetroTerm.Core.Terminal.Buffer;
using RetroTerm.Core.Terminal.Emulators;
using Xunit;

namespace RetroTerm.Tests;

public class ScrollingRegionTests
{
    [Fact]
    public void ScrollingRegion_ShouldScrollUp_WhenCursorReachesBottom()
    {
        // Arrange
        var emulator = new TestTerminalEmulator(80, 24);

        // Set scroll region to lines 5-15 (0-indexed: 4-14)
        var scrollRegionData = Encoding.UTF8.GetBytes("\x1b[5;15r");
        emulator.ProcessData(scrollRegionData);

        // Debug: Check scroll region values
        var cursor = emulator.GetCursor();
        Console.WriteLine($"After setting scroll region: Cursor at ({cursor.Row}, {cursor.Column})");

        // Move cursor to line 5 (0-indexed: 4)
        var moveCursorData = Encoding.UTF8.GetBytes("\x1b[5;1H");
        emulator.ProcessData(moveCursorData);

        // Debug: Check cursor position after move
        cursor = emulator.GetCursor();
        Console.WriteLine($"After cursor move: Cursor at ({cursor.Row}, {cursor.Column})");

        // Debug: Check what's in the buffer before writing
        var bufferBefore = emulator.GetBuffer();
        Console.WriteLine($"Before writing: buffer[4,0] = {(char)bufferBefore[4, 0].Codepoint} (codepoint: {bufferBefore[4, 0].Codepoint})");

        // Act: Write 15 lines to fill the scroll region
        for (int i = 1; i <= 15; i++)
        {
            var lineData = Encoding.UTF8.GetBytes($"Line {i}\r\n");
            emulator.ProcessData(lineData);
        }

        // Assert: Check that only the last 11 lines are visible (lines 5-15)
        var buffer = emulator.GetBuffer();

        // Line 4 should contain "Line 6" (first 5 lines scrolled out, so line 6 is now at row 4)
        Assert.Equal("Line 6", GetLineText(buffer, 4));

        // Lines 5-13 should contain the last 9 lines (Line 7 through Line 15)
        for (int i = 0; i < 9; i++)
        {
            var expectedLine = $"Line {i + 7}";
            var actualLine = GetLineText(buffer, 5 + i);
            Assert.Equal(expectedLine, actualLine);
        }

        // The last line of the scroll region (row 14) should be empty (cursor is positioned there after the last line feed)
        Assert.Equal("", GetLineText(buffer, 14));
    }

    [Fact]
    public void ScrollingRegion_ShouldNotScroll_WhenCursorAboveRegion()
    {
        // Arrange
        var emulator = new TestTerminalEmulator(80, 24);

        // Set scroll region to lines 5-15
        var scrollRegionData = Encoding.UTF8.GetBytes("\x1b[5;15r");
        emulator.ProcessData(scrollRegionData);

        // Move cursor to line 3 (above scroll region)
        var moveCursorData = Encoding.UTF8.GetBytes("\x1b[3;1H");
        emulator.ProcessData(moveCursorData);

        // Act: Write several lines
        for (int i = 1; i <= 5; i++)
        {
            var lineData = Encoding.UTF8.GetBytes($"Above {i}\r\n");
            emulator.ProcessData(lineData);
        }

        // Assert: Lines should be written normally without scrolling
        var buffer = emulator.GetBuffer();

        // Check that lines are written in sequence (VT100 allows writing anywhere)
        Assert.Equal("Above 2", GetLineText(buffer, 3)); // Text written outside scroll region should be preserved
        Assert.Equal("Above 3", GetLineText(buffer, 4));
        Assert.Equal("Above 4", GetLineText(buffer, 5));
        Assert.Equal("Above 5", GetLineText(buffer, 6));
        Assert.Equal("", GetLineText(buffer, 7)); // No more lines written
    }

    [Fact]
    public void ScrollingRegion_ShouldScroll_WhenWritingMoreLinesThanRegionHeight()
    {
        // Arrange
        var emulator = new TestTerminalEmulator(80, 24);

        // Set scroll region to lines 5-15 (11 lines total)
        var scrollRegionData = Encoding.UTF8.GetBytes("\x1b[5;15r");
        emulator.ProcessData(scrollRegionData);

        // Move cursor to line 5
        var moveCursorData = Encoding.UTF8.GetBytes("\x1b[5;1H");
        emulator.ProcessData(moveCursorData);

        // Act: Write 20 lines (more than the 11-line region)
        for (int i = 1; i <= 20; i++)
        {
            var lineData = Encoding.UTF8.GetBytes($"Line {i}\r\n");
            emulator.ProcessData(lineData);
        }

        // Assert: Only the last 11 lines should be visible
        var buffer = emulator.GetBuffer();

        // After writing 20 lines to an 11-line scroll region, the first 9 lines scroll out
        // leaving lines 11-20 in the scroll region
        // Row 4 should contain Line 11
        Assert.Equal("Line 11", GetLineText(buffer, 4));

        // Rows 5-13 should contain Line 12 through Line 20
        for (int i = 0; i < 9; i++)
        {
            var expectedLine = $"Line {i + 12}";
            var actualLine = GetLineText(buffer, 5 + i);
            Assert.Equal(expectedLine, actualLine);
        }

        // The last line of the scroll region (row 14) should be empty (cursor is positioned there after the last line feed)
        Assert.Equal("", GetLineText(buffer, 14));
    }

    private static string GetLineText(TerminalBuffer buffer, int row)
    {
        var sb = new StringBuilder();
        for (int col = 0; col < buffer.Width; col++)
        {
            var cell = buffer[row, col];
            if (cell.Codepoint == 0) break;
            sb.Append(char.ConvertFromUtf32((int)cell.Codepoint));
        }
        return sb.ToString().TrimEnd();
    }
}

/// <summary>
/// Test implementation of TerminalEmulatorBase for unit testing
/// </summary>
public class TestTerminalEmulator : TerminalEmulatorBase
{
    public TestTerminalEmulator(int width, int height) : base(width, height)
    {
    }

    // The base is ECMA-48, not a terminal, so every emulator must declare what it is.
    public override RetroTerm.Core.Terminal.Profiles.TerminalProfile Profile
        => RetroTerm.Core.Terminal.Profiles.TerminalProfile.Ansi;
}
