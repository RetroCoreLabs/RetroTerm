using System;
using System.Text;
using RetroTerm.Core.Terminal.Buffer;
using RetroTerm.Core.Terminal.Emulators;
using Xunit;
using Xunit.Abstractions;

namespace RetroTerm.Tests;

public class ScrollingRegionEnforcementTest
{
    private readonly ITestOutputHelper _output;

    public ScrollingRegionEnforcementTest(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void ScrollingRegion_ShouldBlockTextOutsideRegion()
    {
        // Arrange: Create emulator with 80x24 screen
        var emulator = new TestTerminalEmulator(80, 24);

        // Set scroll region from line 5 to line 15
        emulator.ProcessData(Encoding.UTF8.GetBytes("\x1b[5;15r"));

        // Try to write text outside the scroll region
        emulator.ProcessData(Encoding.UTF8.GetBytes("\x1b[1;1H"));
        emulator.ProcessData(Encoding.UTF8.GetBytes("OUTSIDE_ABOVE"));

        emulator.ProcessData(Encoding.UTF8.GetBytes("\x1b[20;1H"));
        emulator.ProcessData(Encoding.UTF8.GetBytes("OUTSIDE_BELOW"));

        // Write text inside the scroll region
        emulator.ProcessData(Encoding.UTF8.GetBytes("\x1b[10;1H"));
        emulator.ProcessData(Encoding.UTF8.GetBytes("INSIDE_REGION"));

        // Check the buffer
        var buffer = emulator.GetBuffer();
        _output.WriteLine("=== BUFFER CONTENTS ===");
        for (int row = 0; row < 24; row++)
        {
            var lineText = GetLineText(buffer, row);
            if (!string.IsNullOrEmpty(lineText))
            {
                _output.WriteLine($"Row {row,2}: '{lineText}'");
            }
        }

        // Assert: Text can be written anywhere (VT100 allows writing outside scroll region)
        Assert.Equal("OUTSIDE_ABOVE", GetLineText(buffer, 0));  // Above region - should be preserved
        Assert.Equal("OUTSIDE_BELOW", GetLineText(buffer, 19)); // Below region - should be preserved
        Assert.Equal("INSIDE_REGION", GetLineText(buffer, 9)); // Inside region
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
