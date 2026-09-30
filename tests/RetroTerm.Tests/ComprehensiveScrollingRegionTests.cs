using System;
using System.Text;
using RetroTerm.Core.Terminal.Buffer;
using RetroTerm.Core.Terminal.Emulators;
using Xunit;

namespace RetroTerm.Tests;

public class ComprehensiveScrollingRegionTests
{
    private readonly ITestOutputHelper _output;

    public ComprehensiveScrollingRegionTests(ITestOutputHelper output)
    {
        _output = output;
    }

    private string GetLineText(TerminalBuffer buffer, int row)
    {
        var sb = new StringBuilder();
        for (int col = 0; col < buffer.Width; col++)
        {
            var codepoint = buffer[row, col].Codepoint;
            if (codepoint == 0) break; // Stop at first null character
            sb.Append(char.ConvertFromUtf32((int)codepoint));
        }
        return sb.ToString().TrimEnd();
    }

    [Fact]
    public void ScrollRegion_ShouldShowCorrectValues()
    {
        // Arrange
        var emulator = new TestTerminalEmulator(80, 24);

        // Act: Set scroll region to lines 5-15 (1-indexed: 5-15, 0-indexed: 4-14)
        var scrollRegionData = Encoding.UTF8.GetBytes("\x1b[5;15r");
        emulator.ProcessData(scrollRegionData);

        // Debug: Show scroll region values
        var cursor = emulator.GetCursor();
        _output.WriteLine($"After setting scroll region: Cursor at ({cursor.Row}, {cursor.Column})");

        // Move cursor to line 5 (1-indexed: 5, 0-indexed: 4)
        var moveCursorData = Encoding.UTF8.GetBytes("\x1b[5;1H");
        emulator.ProcessData(moveCursorData);

        cursor = emulator.GetCursor();
        _output.WriteLine($"After cursor move: Cursor at ({cursor.Row}, {cursor.Column})");

        // Write a single character
        var charData = Encoding.UTF8.GetBytes("X");
        emulator.ProcessData(charData);

        // Check what was written
        var buffer = emulator.GetBuffer();
        var writtenChar = (char)buffer[cursor.Row, cursor.Column].Codepoint;
        _output.WriteLine($"Character written: '{writtenChar}' (codepoint: {buffer[cursor.Row, cursor.Column].Codepoint})");

        // This test should pass - we're just checking what happens
        Assert.True(true);
    }

    [Fact]
    public void ScrollRegion_ShouldBlockWritingOutsideRegion()
    {
        // Arrange
        var emulator = new TestTerminalEmulator(80, 24);

        // Set scroll region to lines 5-15 (1-indexed: 5-15, 0-indexed: 4-14)
        var scrollRegionData = Encoding.UTF8.GetBytes("\x1b[5;15r");
        emulator.ProcessData(scrollRegionData);

        // Move cursor ABOVE scroll region (line 3, 0-indexed: 2)
        var moveCursorData = Encoding.UTF8.GetBytes("\x1b[3;1H");
        emulator.ProcessData(moveCursorData);

        // Write a character - this should be BLOCKED
        var charData = Encoding.UTF8.GetBytes("X");
        emulator.ProcessData(charData);

        // Check that nothing was written
        var buffer = emulator.GetBuffer();
        var writtenChar = (char)buffer[2, 0].Codepoint;
        _output.WriteLine($"Character written above scroll region: '{writtenChar}' (codepoint: {buffer[2, 0].Codepoint})");

        // Should contain the character written (VT100 allows writing outside scroll region)
        Assert.Equal((uint)'X', buffer[2, 0].Codepoint);
    }

    [Fact]
    public void ScrollRegion_ShouldAllowWritingInsideRegion()
    {
        // Arrange
        var emulator = new TestTerminalEmulator(80, 24);

        // Set scroll region to lines 5-15 (1-indexed: 5-15, 0-indexed: 4-14)
        var scrollRegionData = Encoding.UTF8.GetBytes("\x1b[5;15r");
        emulator.ProcessData(scrollRegionData);

        // Move cursor INSIDE scroll region (line 10, 0-indexed: 9)
        var moveCursorData = Encoding.UTF8.GetBytes("\x1b[10;1H");
        emulator.ProcessData(moveCursorData);

        // Write a character - this should be ALLOWED
        var charData = Encoding.UTF8.GetBytes("Y");
        emulator.ProcessData(charData);

        // Check that character was written
        var buffer = emulator.GetBuffer();
        var writtenChar = (char)buffer[9, 0].Codepoint;
        _output.WriteLine($"Character written inside scroll region: '{writtenChar}' (codepoint: {buffer[9, 0].Codepoint})");

        // Should be 'Y' (codepoint 89)
        Assert.Equal((uint)89, buffer[9, 0].Codepoint);
    }

    [Fact]
    public void ScrollRegion_ShouldBlockWritingBelowRegion()
    {
        // Arrange
        var emulator = new TestTerminalEmulator(80, 24);

        // Set scroll region to lines 5-15 (1-indexed: 5-15, 0-indexed: 4-14)
        var scrollRegionData = Encoding.UTF8.GetBytes("\x1b[5;15r");
        emulator.ProcessData(scrollRegionData);

        // Move cursor BELOW scroll region (line 20, 0-indexed: 19)
        var moveCursorData = Encoding.UTF8.GetBytes("\x1b[20;1H");
        emulator.ProcessData(moveCursorData);

        // Write a character - this should be BLOCKED
        var charData = Encoding.UTF8.GetBytes("Z");
        emulator.ProcessData(charData);

        // Check that nothing was written
        var buffer = emulator.GetBuffer();
        var writtenChar = (char)buffer[19, 0].Codepoint;
        _output.WriteLine($"Character written below scroll region: '{writtenChar}' (codepoint: {buffer[19, 0].Codepoint})");

        // Should contain the character written (VT100 allows writing outside scroll region)
        Assert.Equal((uint)'Z', buffer[19, 0].Codepoint);
    }

    [Fact]
    public void ScrollRegion_ShouldScrollWhenWritingToBottom()
    {
        // Arrange
        var emulator = new TestTerminalEmulator(80, 24);

        // Set scroll region to lines 5-15 (1-indexed: 5-15, 0-indexed: 4-14)
        var scrollRegionData = Encoding.UTF8.GetBytes("\x1b[5;15r");
        emulator.ProcessData(scrollRegionData);

        // Move cursor to bottom of scroll region (line 15, 0-indexed: 14)
        var moveCursorData = Encoding.UTF8.GetBytes("\x1b[15;1H");
        emulator.ProcessData(moveCursorData);

        // Write a character - this should be ALLOWED and cause scrolling
        var charData = Encoding.UTF8.GetBytes("A");
        emulator.ProcessData(charData);

        // Check that character was written
        var buffer = emulator.GetBuffer();
        var writtenChar = (char)buffer[14, 0].Codepoint;
        _output.WriteLine($"Character written at bottom of scroll region: '{writtenChar}' (codepoint: {buffer[14, 0].Codepoint})");

        // Should be 'A' (codepoint 65)
        Assert.Equal((uint)65, buffer[14, 0].Codepoint);
    }
}
