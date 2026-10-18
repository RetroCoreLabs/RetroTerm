using System;
using System.Text;
using RetroTerm.Core.Terminal.Buffer;
using RetroTerm.Core.Terminal.Emulators;
using Xunit;

namespace RetroTerm.Tests;

public class ScrollingRegionBufferValidationTests
{
    private readonly ITestOutputHelper _output;

    public ScrollingRegionBufferValidationTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void ScrollingRegion_ShouldBlockTextOutsideRegion_AndScrollOnlyWithinRegion()
    {
        // Arrange: Create emulator with 80x24 screen
        var emulator = new TestTerminalEmulator(80, 24);

        // Act: Set scroll region from line 5 to line 15 (1-indexed, so 0-indexed: 4-14)
        emulator.ProcessData(Encoding.UTF8.GetBytes("\x1b[5;15r"));

        // Move cursor to line 1 (above scroll region) and write text
        emulator.ProcessData(Encoding.UTF8.GetBytes("\x1b[1;1H"));
        emulator.ProcessData(Encoding.UTF8.GetBytes("ABOVE_REGION"));

        // Move cursor to line 5 (start of scroll region) and write text
        emulator.ProcessData(Encoding.UTF8.GetBytes("\x1b[5;1H"));
        emulator.ProcessData(Encoding.UTF8.GetBytes("START_OF_SCROLL"));

        // Move cursor to line 10 (middle of scroll region) and write text
        emulator.ProcessData(Encoding.UTF8.GetBytes("\x1b[10;1H"));
        emulator.ProcessData(Encoding.UTF8.GetBytes("MIDDLE_OF_SCROLL"));

        // Move cursor to line 15 (end of scroll region) and write text
        emulator.ProcessData(Encoding.UTF8.GetBytes("\x1b[15;1H"));
        emulator.ProcessData(Encoding.UTF8.GetBytes("END_OF_SCROLL"));

        // Move cursor to line 20 (below scroll region) and write text
        emulator.ProcessData(Encoding.UTF8.GetBytes("\x1b[20;1H"));
        emulator.ProcessData(Encoding.UTF8.GetBytes("BELOW_REGION"));

        // Now fill the scroll region with 15 lines to trigger scrolling
        emulator.ProcessData(Encoding.UTF8.GetBytes("\x1b[5;1H"));
        for (int i = 1; i <= 15; i++)
        {
            emulator.ProcessData(Encoding.UTF8.GetBytes($"\x1b[K"));  // Clear line to end
            emulator.ProcessData(Encoding.UTF8.GetBytes($"SCROLL_LINE_{i}\r\n"));
        }

        // Assert: Validate the entire buffer
        var buffer = emulator.GetBuffer();

        // Lines 1-4 (above scroll region): Should contain text written there (VT100 allows writing anywhere)
        Assert.Equal("ABOVE_REGION", GetLineText(buffer, 0)); // Text written at line 1 (row 0) should be preserved
        Assert.Equal("", GetLineText(buffer, 1));
        Assert.Equal("", GetLineText(buffer, 2));
        Assert.Equal("", GetLineText(buffer, 3));

        // Lines 5-15 (scroll region): Should contain the scrolled content
        // After writing 15 lines to an 11-line scroll region, the first 5 lines scroll out
        // So rows 4-13 should contain SCROLL_LINE_6 through SCROLL_LINE_15, and row 14 should be empty
        Assert.Equal("SCROLL_LINE_6", GetLineText(buffer, 4));
        Assert.Equal("SCROLL_LINE_7", GetLineText(buffer, 5));
        Assert.Equal("SCROLL_LINE_8", GetLineText(buffer, 6));
        Assert.Equal("SCROLL_LINE_9", GetLineText(buffer, 7));
        Assert.Equal("SCROLL_LINE_10", GetLineText(buffer, 8));
        Assert.Equal("SCROLL_LINE_11", GetLineText(buffer, 9));
        Assert.Equal("SCROLL_LINE_12", GetLineText(buffer, 10));
        Assert.Equal("SCROLL_LINE_13", GetLineText(buffer, 11));
        Assert.Equal("SCROLL_LINE_14", GetLineText(buffer, 12));
        Assert.Equal("SCROLL_LINE_15", GetLineText(buffer, 13));
        Assert.Equal("", GetLineText(buffer, 14)); // Last line of scroll region is empty after scrolling

        // Lines 16-24 (below scroll region): Should contain text written there (VT100 allows writing anywhere)
        Assert.Equal("BELOW_REGION", GetLineText(buffer, 19)); // Text written at line 20 (row 19) should be preserved
        Assert.Equal("", GetLineText(buffer, 20));
        Assert.Equal("", GetLineText(buffer, 21));
        Assert.Equal("", GetLineText(buffer, 22));
        Assert.Equal("", GetLineText(buffer, 23));

        // Output the buffer for visual inspection
        _output.WriteLine("=== BUFFER CONTENTS ===");
        for (int row = 0; row < 24; row++)
        {
            var lineText = GetLineText(buffer, row);
            _output.WriteLine($"Row {row,2}: '{lineText}'");
        }
    }

    [Fact]
    public void ScrollingRegion_ShouldNotAffectLinesOutsideRegion_WhenScrolling()
    {
        // Arrange: Create emulator and set up content outside scroll region
        var emulator = new TestTerminalEmulator(80, 24);

        // Write content to lines above and below the scroll region
        emulator.ProcessData(Encoding.UTF8.GetBytes("\x1b[1;1H"));
        emulator.ProcessData(Encoding.UTF8.GetBytes("LINE_1_ABOVE"));

        emulator.ProcessData(Encoding.UTF8.GetBytes("\x1b[2;1H"));
        emulator.ProcessData(Encoding.UTF8.GetBytes("LINE_2_ABOVE"));

        emulator.ProcessData(Encoding.UTF8.GetBytes("\x1b[3;1H"));
        emulator.ProcessData(Encoding.UTF8.GetBytes("LINE_3_ABOVE"));

        emulator.ProcessData(Encoding.UTF8.GetBytes("\x1b[4;1H"));
        emulator.ProcessData(Encoding.UTF8.GetBytes("LINE_4_ABOVE"));

        emulator.ProcessData(Encoding.UTF8.GetBytes("\x1b[16;1H"));
        emulator.ProcessData(Encoding.UTF8.GetBytes("LINE_16_BELOW"));

        emulator.ProcessData(Encoding.UTF8.GetBytes("\x1b[17;1H"));
        emulator.ProcessData(Encoding.UTF8.GetBytes("LINE_17_BELOW"));

        emulator.ProcessData(Encoding.UTF8.GetBytes("\x1b[18;1H"));
        emulator.ProcessData(Encoding.UTF8.GetBytes("LINE_18_BELOW"));

        emulator.ProcessData(Encoding.UTF8.GetBytes("\x1b[19;1H"));
        emulator.ProcessData(Encoding.UTF8.GetBytes("LINE_19_BELOW"));

        // Set scroll region from line 5 to line 15
        emulator.ProcessData(Encoding.UTF8.GetBytes("\x1b[5;15r"));

        // Fill the scroll region with content to trigger scrolling
        emulator.ProcessData(Encoding.UTF8.GetBytes("\x1b[5;1H"));
        for (int i = 1; i <= 12; i++)
        {
            emulator.ProcessData(Encoding.UTF8.GetBytes($"SCROLL_CONTENT_{i}\r\n"));
        }

        // Assert: Lines outside the scroll region should be unchanged
        var buffer = emulator.GetBuffer();

        // Debug: Output buffer contents
        _output.WriteLine("=== BUFFER CONTENTS ===");
        for (int row = 0; row < 24; row++)
        {
            var lineText = GetLineText(buffer, row);
            _output.WriteLine($"Line {row}: '{lineText}'");
        }
        _output.WriteLine("=== END BUFFER CONTENTS ===");

        // Lines above scroll region should be unchanged
        Assert.Equal("LINE_1_ABOVE", GetLineText(buffer, 0));
        Assert.Equal("LINE_2_ABOVE", GetLineText(buffer, 1));
        Assert.Equal("LINE_3_ABOVE", GetLineText(buffer, 2));
        Assert.Equal("LINE_4_ABOVE", GetLineText(buffer, 3));

        // Lines below scroll region should be unchanged
        Assert.Equal("LINE_16_BELOW", GetLineText(buffer, 15));
        Assert.Equal("LINE_17_BELOW", GetLineText(buffer, 16));
        Assert.Equal("LINE_18_BELOW", GetLineText(buffer, 17));
        Assert.Equal("LINE_19_BELOW", GetLineText(buffer, 18));

        // Lines within scroll region should contain the scrolled content
        Assert.Equal("SCROLL_CONTENT_3", GetLineText(buffer, 4));  // Line 5 (0-indexed: 4)
        Assert.Equal("SCROLL_CONTENT_4", GetLineText(buffer, 5));  // Line 6
        Assert.Equal("SCROLL_CONTENT_5", GetLineText(buffer, 6));  // Line 7
                                                                   // ... and so on

        _output.WriteLine("=== BUFFER CONTENTS AFTER SCROLLING ===");
        for (int row = 0; row < 24; row++)
        {
            var lineText = GetLineText(buffer, row);
            _output.WriteLine($"Row {row,2}: '{lineText}'");
        }
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
