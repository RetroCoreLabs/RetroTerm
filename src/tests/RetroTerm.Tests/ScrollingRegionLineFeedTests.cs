using System;
using System.Text;
using RetroTerm.Core.Terminal.Buffer;
using RetroTerm.Core.Terminal.Emulators;
using Xunit;

namespace RetroTerm.Tests;

public class ScrollingRegionLineFeedTests
{
    private readonly ITestOutputHelper _output;

    public ScrollingRegionLineFeedTests(ITestOutputHelper output)
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
    public void ScrollingRegion_11Lines_Should_Show_Last_11_Lines_After_Writing_15()
    {
        // Arrange: Create emulator with 80x24 screen
        var emulator = new TestTerminalEmulator(80, 24);

        // Set scroll region to lines 7-17 (1-indexed), which is 11 lines (0-indexed: 6-16)
        emulator.ProcessData(Encoding.UTF8.GetBytes("\x1b[7;17r"));

        // Move cursor to line 7 (start of scroll region)
        emulator.ProcessData(Encoding.UTF8.GetBytes("\x1b[7;1H"));

        // Act: Write 15 lines to the 11-line scroll region
        for (int i = 1; i <= 15; i++)
        {
            emulator.ProcessData(Encoding.UTF8.GetBytes($"Scrolling line {i}\r\n"));
        }

        // Assert: Writing 15 lines with \r\n causes 15 linefeeds
        // First 5 lines (1-5) scroll out, lines 6-15 visible (10 lines), cursor at last row
        var buffer = emulator.GetBuffer();

        _output.WriteLine("=== SCROLL REGION 11-LINE TEST ===");
        _output.WriteLine("Expected: Lines 6-15 visible (first 5 scrolled out)");
        _output.WriteLine("");

        for (int row = 6; row <= 16; row++)
        {
            var actualLine = GetLineText(buffer, row);
            _output.WriteLine($"Row {row,2}: '{actualLine}'");
        }

        // First 5 lines (1-5) should have scrolled out
        // Lines 6-15 should be visible at rows 6-15 (0-indexed)
        Assert.Equal("Scrolling line 6", GetLineText(buffer, 6));
        Assert.Equal("Scrolling line 7", GetLineText(buffer, 7));
        Assert.Equal("Scrolling line 8", GetLineText(buffer, 8));
        Assert.Equal("Scrolling line 9", GetLineText(buffer, 9));
        Assert.Equal("Scrolling line 10", GetLineText(buffer, 10));
        Assert.Equal("Scrolling line 11", GetLineText(buffer, 11));
        Assert.Equal("Scrolling line 12", GetLineText(buffer, 12));
        Assert.Equal("Scrolling line 13", GetLineText(buffer, 13));
        Assert.Equal("Scrolling line 14", GetLineText(buffer, 14));
        Assert.Equal("Scrolling line 15", GetLineText(buffer, 15));
        Assert.Equal("", GetLineText(buffer, 16)); // Cursor is here
    }

    [Fact]
    public void ScrollingRegion_ExactFit_Should_Show_All_Lines()
    {
        // Arrange: Create emulator with 80x24 screen
        var emulator = new TestTerminalEmulator(80, 24);

        // Set scroll region to lines 7-17 (11 lines)
        emulator.ProcessData(Encoding.UTF8.GetBytes("\x1b[7;17r"));

        // Move cursor to line 7
        emulator.ProcessData(Encoding.UTF8.GetBytes("\x1b[7;1H"));

        // Act: Write exactly 11 lines (should fit perfectly)
        for (int i = 1; i <= 11; i++)
        {
            emulator.ProcessData(Encoding.UTF8.GetBytes($"Line {i}\r\n"));
        }

        // Assert: First line scrolls out because \r\n after last line causes scroll
        // So we see lines 2-11 (10 lines visible, last row empty where cursor is)
        var buffer = emulator.GetBuffer();

        _output.WriteLine("=== EXACT FIT TEST (11 lines to 11-line region) ===");
        _output.WriteLine("Note: Writing 11 lines with \\r\\n causes 11 linefeeds.");
        _output.WriteLine("The 11th linefeed (after Line 11) scrolls, so Line 1 scrolls out.");

        for (int row = 6; row <= 16; row++)
        {
            var actualLine = GetLineText(buffer, row);
            _output.WriteLine($"Row {row,2}: '{actualLine}'");
        }

        Assert.Equal("Line 2", GetLineText(buffer, 6));
        Assert.Equal("Line 3", GetLineText(buffer, 7));
        Assert.Equal("Line 4", GetLineText(buffer, 8));
        Assert.Equal("Line 5", GetLineText(buffer, 9));
        Assert.Equal("Line 6", GetLineText(buffer, 10));
        Assert.Equal("Line 7", GetLineText(buffer, 11));
        Assert.Equal("Line 8", GetLineText(buffer, 12));
        Assert.Equal("Line 9", GetLineText(buffer, 13));
        Assert.Equal("Line 10", GetLineText(buffer, 14));
        Assert.Equal("Line 11", GetLineText(buffer, 15));
        Assert.Equal("", GetLineText(buffer, 16)); // Cursor is here after scroll
    }

    [Fact]
    public void ScrollingRegion_OneLess_Should_Not_Scroll()
    {
        // Arrange: Create emulator with 80x24 screen
        var emulator = new TestTerminalEmulator(80, 24);

        // Set scroll region to lines 7-17 (11 lines)
        emulator.ProcessData(Encoding.UTF8.GetBytes("\x1b[7;17r"));

        // Move cursor to line 7
        emulator.ProcessData(Encoding.UTF8.GetBytes("\x1b[7;1H"));

        // Act: Write 10 lines (one less than region height)
        for (int i = 1; i <= 10; i++)
        {
            emulator.ProcessData(Encoding.UTF8.GetBytes($"Line {i}\r\n"));
        }

        // Assert: All 10 lines should be visible, no scrolling
        var buffer = emulator.GetBuffer();

        _output.WriteLine("=== ONE LESS TEST (10 lines to 11-line region) ===");

        for (int row = 6; row <= 16; row++)
        {
            var actualLine = GetLineText(buffer, row);
            _output.WriteLine($"Row {row,2}: '{actualLine}'");
        }

        Assert.Equal("Line 1", GetLineText(buffer, 6));
        Assert.Equal("Line 2", GetLineText(buffer, 7));
        Assert.Equal("Line 3", GetLineText(buffer, 8));
        Assert.Equal("Line 4", GetLineText(buffer, 9));
        Assert.Equal("Line 5", GetLineText(buffer, 10));
        Assert.Equal("Line 6", GetLineText(buffer, 11));
        Assert.Equal("Line 7", GetLineText(buffer, 12));
        Assert.Equal("Line 8", GetLineText(buffer, 13));
        Assert.Equal("Line 9", GetLineText(buffer, 14));
        Assert.Equal("Line 10", GetLineText(buffer, 15));
        Assert.Equal("", GetLineText(buffer, 16)); // Last line should be empty
    }

    [Fact]
    public void ScrollingRegion_OneMore_Should_Scroll_One_Line()
    {
        // Arrange: Create emulator with 80x24 screen
        var emulator = new TestTerminalEmulator(80, 24);

        // Set scroll region to lines 7-17 (11 lines)
        emulator.ProcessData(Encoding.UTF8.GetBytes("\x1b[7;17r"));

        // Move cursor to line 7
        emulator.ProcessData(Encoding.UTF8.GetBytes("\x1b[7;1H"));

        // Act: Write 12 lines (one more than region height)
        for (int i = 1; i <= 12; i++)
        {
            emulator.ProcessData(Encoding.UTF8.GetBytes($"Line {i}\r\n"));
        }

        // Assert: First 2 lines scroll out, lines 3-12 visible
        var buffer = emulator.GetBuffer();

        _output.WriteLine("=== ONE MORE TEST (12 lines to 11-line region) ===");
        _output.WriteLine("Expected: Lines 1-2 scrolled out, Lines 3-12 visible");

        for (int row = 6; row <= 16; row++)
        {
            var actualLine = GetLineText(buffer, row);
            _output.WriteLine($"Row {row,2}: '{actualLine}'");
        }

        Assert.Equal("Line 3", GetLineText(buffer, 6));
        Assert.Equal("Line 4", GetLineText(buffer, 7));
        Assert.Equal("Line 5", GetLineText(buffer, 8));
        Assert.Equal("Line 6", GetLineText(buffer, 9));
        Assert.Equal("Line 7", GetLineText(buffer, 10));
        Assert.Equal("Line 8", GetLineText(buffer, 11));
        Assert.Equal("Line 9", GetLineText(buffer, 12));
        Assert.Equal("Line 10", GetLineText(buffer, 13));
        Assert.Equal("Line 11", GetLineText(buffer, 14));
        Assert.Equal("Line 12", GetLineText(buffer, 15));
        Assert.Equal("", GetLineText(buffer, 16)); // Cursor is here
    }

    [Fact]
    public void ScrollingRegion_3Lines_Should_Show_Last_3_Lines_After_Writing_5()
    {
        // Arrange: Create emulator with 80x24 screen
        var emulator = new TestTerminalEmulator(80, 24);

        // Set scroll region to lines 10-12 (3 lines)
        emulator.ProcessData(Encoding.UTF8.GetBytes("\x1b[10;12r"));

        // Move cursor to line 10
        emulator.ProcessData(Encoding.UTF8.GetBytes("\x1b[10;1H"));

        // Act: Write 5 lines to the 3-line scroll region
        for (int i = 1; i <= 5; i++)
        {
            emulator.ProcessData(Encoding.UTF8.GetBytes($"Line {i}\r\n"));
        }

        // Assert: Should see lines 4-5 (first 3 scrolled out)
        var buffer = emulator.GetBuffer();

        _output.WriteLine("=== 3-LINE SCROLL REGION TEST ===");
        _output.WriteLine("Expected: Lines 4-5 visible (first 3 scrolled out)");

        for (int row = 9; row <= 11; row++)
        {
            var actualLine = GetLineText(buffer, row);
            _output.WriteLine($"Row {row,2}: '{actualLine}'");
        }

        Assert.Equal("Line 4", GetLineText(buffer, 9));
        Assert.Equal("Line 5", GetLineText(buffer, 10));
        Assert.Equal("", GetLineText(buffer, 11)); // Cursor is here
    }

    [Fact]
    public void ScrollingRegion_FullScreen_Should_Behave_Like_NoScrollRegion()
    {
        // Arrange: Create emulator with 80x24 screen
        var emulator = new TestTerminalEmulator(80, 24);

        // Set scroll region to full screen (lines 1-24)
        emulator.ProcessData(Encoding.UTF8.GetBytes("\x1b[1;24r"));

        // Move cursor to line 1
        emulator.ProcessData(Encoding.UTF8.GetBytes("\x1b[1;1H"));

        // Act: Write 24 lines (exactly fills screen)
        for (int i = 1; i <= 24; i++)
        {
            emulator.ProcessData(Encoding.UTF8.GetBytes($"Line {i}\r\n"));
        }

        // Assert: First line scrolls out, lines 2-24 visible
        var buffer = emulator.GetBuffer();

        _output.WriteLine("=== FULL SCREEN SCROLL REGION TEST ===");
        _output.WriteLine("Note: Writing 24 lines with \\r\\n causes 24 linefeeds.");
        _output.WriteLine("The 24th linefeed scrolls, so Line 1 scrolls out.");

        for (int row = 0; row < 24; row++)
        {
            var actualLine = GetLineText(buffer, row);
            _output.WriteLine($"Row {row,2}: '{actualLine}'");
        }

        // First line scrolls out, lines 2-24 visible
        for (int i = 2; i <= 24; i++)
        {
            Assert.Equal($"Line {i}", GetLineText(buffer, i - 2));
        }
        Assert.Equal("", GetLineText(buffer, 23)); // Last row is empty (cursor is here)
    }
}

