using System;
using System.Text;
using RetroTerm.Core.Terminal.Buffer;
using RetroTerm.Core.Terminal.Emulators;
using Xunit;
using Xunit.Abstractions;

namespace RetroTerm.Tests;

public class ScrollingRegionVisualFrameTests
{
    private readonly ITestOutputHelper _output;

    public ScrollingRegionVisualFrameTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void ScrollingRegion_VisualFrame_ShouldShowExactBoundaries()
    {
        // Arrange: Create emulator with 80x24 screen
        var emulator = new TestTerminalEmulator(80, 24);

        // Draw a visual frame around the entire screen first
        DrawScreenFrame(emulator);

        // Set scroll region from line 5 to line 15 (1-indexed, so 0-indexed: 4-14)
        emulator.ProcessData(Encoding.UTF8.GetBytes("\x1b[5;15r"));

        // Draw a visual frame around the scroll region
        DrawScrollRegionFrame(emulator);

        // Fill the scroll region with content
        emulator.ProcessData(Encoding.UTF8.GetBytes("\x1b[5;1H"));
        for (int i = 1; i <= 12; i++)
        {
            emulator.ProcessData(Encoding.UTF8.GetBytes($"SCROLL_LINE_{i}\r\n"));
        }

        // Try to write outside the scroll region
        emulator.ProcessData(Encoding.UTF8.GetBytes("\x1b[1;1H"));
        emulator.ProcessData(Encoding.UTF8.GetBytes("OUTSIDE_ABOVE"));

        emulator.ProcessData(Encoding.UTF8.GetBytes("\x1b[20;1H"));
        emulator.ProcessData(Encoding.UTF8.GetBytes("OUTSIDE_BELOW"));

        // Output the buffer for visual inspection
        var buffer = emulator.GetBuffer();
        _output.WriteLine("=== SCROLL REGION VISUAL FRAME ===");
        _output.WriteLine("Legend:");
        _output.WriteLine("  # = Screen border");
        _output.WriteLine("  * = Scroll region border");
        _output.WriteLine("  S = Scroll region content");
        _output.WriteLine("  O = Outside content (should be blocked)");
        _output.WriteLine("");

        for (int row = 0; row < 24; row++)
        {
            var lineText = GetLineText(buffer, row);
            _output.WriteLine($"Row {row,2}: {lineText}");
        }

        // Assert: The scroll region should contain the content, outside should be blocked
        // Note: Frame characters (* and #) are preserved at the right edge, so we strip them
        Assert.Equal("SCROLL_LINE_3", GetLineText(buffer, 4).Replace("*", "").Trim());  // Line 5 (0-indexed: 4)
        Assert.Equal("SCROLL_LINE_4", GetLineText(buffer, 5).Replace("*", "").Trim());  // Line 6
        Assert.Equal("SCROLL_LINE_5", GetLineText(buffer, 6).Replace("*", "").Trim());  // Line 7
                                                                                        // ... and so on

        // Lines outside should contain the text written there (VT100 allows writing outside scroll region)
        Assert.Equal("OUTSIDE_ABOVE", GetLineText(buffer, 0).Replace("#", "").Trim());  // Above region - should be preserved
        Assert.Equal("OUTSIDE_BELOW", GetLineText(buffer, 19).Replace("#", "").Trim()); // Below region - should be preserved
    }

    [Fact]
    public void ScrollingRegion_VisualFrame_ShouldShowScrollingBehavior()
    {
        // Arrange: Create emulator with 80x24 screen
        var emulator = new TestTerminalEmulator(80, 24);

        // Draw a visual frame around the entire screen
        DrawScreenFrame(emulator);

        // Set scroll region from line 5 to line 15
        emulator.ProcessData(Encoding.UTF8.GetBytes("\x1b[5;15r"));

        // Draw a visual frame around the scroll region
        DrawScrollRegionFrame(emulator);

        // Fill the scroll region with more content than it can hold
        emulator.ProcessData(Encoding.UTF8.GetBytes("\x1b[5;1H"));
        for (int i = 1; i <= 20; i++)  // More than the 11 lines the region can hold
        {
            emulator.ProcessData(Encoding.UTF8.GetBytes($"LINE_{i}\r\n"));
        }

        // Output the buffer for visual inspection
        var buffer = emulator.GetBuffer();
        _output.WriteLine("=== SCROLL REGION SCROLLING BEHAVIOR ===");
        _output.WriteLine("Legend:");
        _output.WriteLine("  # = Screen border");
        _output.WriteLine("  * = Scroll region border");
        _output.WriteLine("  S = Scroll region content (should show last 11 lines)");
        _output.WriteLine("");

        for (int row = 0; row < 24; row++)
        {
            var lineText = GetLineText(buffer, row);
            _output.WriteLine($"Row {row,2}: {lineText}");
        }

        // Assert: The scroll region should contain the last 11 lines (LINE_10 through LINE_20)
        // Note: Frame characters (* and #) are preserved at the right edge, so we strip them
        Assert.Equal("LINE_11", GetLineText(buffer, 4).Replace("*", "").Trim());   // Line 5 (0-indexed: 4)
        Assert.Equal("LINE_12", GetLineText(buffer, 5).Replace("*", "").Trim());   // Line 6
        Assert.Equal("LINE_13", GetLineText(buffer, 6).Replace("*", "").Trim());   // Line 7
        // ... and so on
        Assert.Equal("", GetLineText(buffer, 14).Replace("*", "").Trim());  // Line 15 (0-indexed: 14) - empty
    }

    private void DrawScreenFrame(TestTerminalEmulator emulator)
    {
        // Draw top border
        emulator.ProcessData(Encoding.UTF8.GetBytes("\x1b[1;1H"));
        emulator.ProcessData(Encoding.UTF8.GetBytes(new string('#', 80)));

        // Draw bottom border
        emulator.ProcessData(Encoding.UTF8.GetBytes("\x1b[24;1H"));
        emulator.ProcessData(Encoding.UTF8.GetBytes(new string('#', 80)));

        // Draw left and right borders
        for (int row = 2; row <= 23; row++)
        {
            emulator.ProcessData(Encoding.UTF8.GetBytes($"\x1b[{row};1H#"));
            emulator.ProcessData(Encoding.UTF8.GetBytes($"\x1b[{row};80H#"));
        }
    }

    private void DrawScrollRegionFrame(TestTerminalEmulator emulator)
    {
        // Draw top border of scroll region
        emulator.ProcessData(Encoding.UTF8.GetBytes("\x1b[5;1H"));
        emulator.ProcessData(Encoding.UTF8.GetBytes(new string('*', 80)));

        // Draw bottom border of scroll region
        emulator.ProcessData(Encoding.UTF8.GetBytes("\x1b[15;1H"));
        emulator.ProcessData(Encoding.UTF8.GetBytes(new string('*', 80)));

        // Draw left and right borders of scroll region
        for (int row = 6; row <= 14; row++)
        {
            emulator.ProcessData(Encoding.UTF8.GetBytes($"\x1b[{row};1H*"));
            emulator.ProcessData(Encoding.UTF8.GetBytes($"\x1b[{row};80H*"));
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
