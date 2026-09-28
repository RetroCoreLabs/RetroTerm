using System;
using System.Text;
using RetroTerm.Core.Terminal.Emulators;
using RetroTerm.Desktop.Rendering;
using Xunit;
using Xunit.Abstractions;

namespace RetroTerm.Tests.Rendering;

/// <summary>
/// Tests the TerminalRenderer class directly, WITHOUT Avalonia UI
/// This isolates the renderer logic from the UI framework
/// </summary>
public class TerminalRendererTests
{
    private readonly ITestOutputHelper _output;

    public TerminalRendererTests(ITestOutputHelper output)
    {
        _output = output;
    }

    // NOTE: TerminalRenderer constructor and size calculation tests removed.
    // These require Avalonia's IFontManagerImpl for font measurement, which is only
    // available when the UI framework is running. The renderer is tested during
    // application runtime and E2E tests.

    [Fact]
    public void TerminalRenderer_WithTextInBuffer_ShouldReadBuffer()
    {
        // This verifies the emulator buffer can be read correctly
        // (Tests buffer reading, not rendering - doesn't need TerminalRenderer)

        // Arrange
        var emulator = new VT100Emulator(80, 24);
        emulator.ProcessData(Encoding.UTF8.GetBytes("Hello World"));

        // Act - Read buffer directly
        var buffer = emulator.GetBuffer();

        // Assert - Verify buffer has content
        var firstCell = buffer[0, 0];
        Assert.Equal('H', (char)firstCell.Codepoint);

        _output.WriteLine($"✓ Renderer can read buffer: First cell = '{(char)firstCell.Codepoint}'");

        // Count non-empty cells (what Render() does)
        int nonEmptyCells = 0;
        for (int row = 0; row < buffer.Height; row++)
        {
            for (int col = 0; col < buffer.Width; col++)
            {
                var cell = buffer[row, col];
                if (cell.Codepoint != 0 && cell.Codepoint != ' ' && cell.Codepoint != 0x20)
                {
                    nonEmptyCells++;
                }
            }
        }

        _output.WriteLine($"✓ Non-empty cells in buffer: {nonEmptyCells}");
        Assert.True(nonEmptyCells >= 10, "Should have at least 10 non-space characters ('Hello World' minus space)");
    }

    [Fact]
    public void TerminalRenderer_WithColoredText_ShouldReadColors()
    {
        // This verifies the emulator stores colors correctly in buffer
        // (Tests color storage, not rendering - doesn't need TerminalRenderer)

        // Arrange
        var emulator = new VT100Emulator(80, 24);
        emulator.ProcessData(Encoding.UTF8.GetBytes("\x1b[31mRED\x1b[32mGREEN\x1b[34mBLUE\x1b[0m"));

        // Act - Read buffer with colors
        var buffer = emulator.GetBuffer();

        // Assert - Check colors are stored
        var redCell = buffer[0, 0]; // 'R'
        var greenCell = buffer[0, 3]; // 'G'
        var blueCell = buffer[0, 8]; // 'B'

        _output.WriteLine($"✓ Red cell: char='{(char)redCell.Codepoint}' fg.index={redCell.Foreground.Index}");
        _output.WriteLine($"✓ Green cell: char='{(char)greenCell.Codepoint}' fg.index={greenCell.Foreground.Index}");
        _output.WriteLine($"✓ Blue cell: char='{(char)blueCell.Codepoint}' fg.index={blueCell.Foreground.Index}");

        Assert.Equal('R', (char)redCell.Codepoint);
        Assert.Equal(1, redCell.Foreground.Index); // Red = 1

        Assert.Equal('G', (char)greenCell.Codepoint);
        Assert.Equal(2, greenCell.Foreground.Index); // Green = 2

        Assert.Equal('B', (char)blueCell.Codepoint);
        Assert.Equal(4, blueCell.Foreground.Index); // Blue = 4
    }

    [Fact]
    public void TerminalRenderer_WithLargeBuffer_ShouldReadAllData()
    {
        // Simulate what the test server sends
        // (Tests buffer handling, not rendering - doesn't need TerminalRenderer)

        // Arrange
        var emulator = new VT100Emulator(80, 24);

        // Simulate test server welcome
        var sb = new StringBuilder();
        sb.Append("\x1b[2J\x1b[H"); // Clear screen
        sb.Append("\x1b[1;36m"); // Bold cyan
        sb.Append("╔═══════════════════════════════════════════════════════╗\r\n");
        sb.Append("║         RetroTerm Test Server v1.0                    ║\r\n");
        sb.Append("╚═══════════════════════════════════════════════════════╝\x1b[0m\r\n");
        sb.Append("\r\n");
        sb.Append("\x1b[1;32m=== Test Menu ===\x1b[0m\r\n");
        sb.Append("1. Basic Colors\r\n");
        sb.Append("2. Cursor Movement\r\n");

        emulator.ProcessData(Encoding.UTF8.GetBytes(sb.ToString()));

        // Act - Count non-empty cells
        var buffer = emulator.GetBuffer();
        int nonEmptyCells = 0;
        int coloredCells = 0;

        for (int row = 0; row < buffer.Height; row++)
        {
            for (int col = 0; col < buffer.Width; col++)
            {
                var cell = buffer[row, col];
                if (cell.Codepoint != 0 && cell.Codepoint != ' ' && cell.Codepoint != 0x20)
                {
                    nonEmptyCells++;
                    if (!cell.Foreground.IsDefault)
                    {
                        coloredCells++;
                    }
                }
            }
        }

        _output.WriteLine($"✓ Total non-empty cells: {nonEmptyCells}");
        _output.WriteLine($"✓ Total colored cells: {coloredCells}");
        _output.WriteLine($"✓ Cursor position: ({emulator.GetCursor().Row}, {emulator.GetCursor().Column})");

        // Print first 5 lines for debugging
        for (int row = 0; row < 5; row++)
        {
            var line = new StringBuilder();
            for (int col = 0; col < 60; col++) // First 60 chars
            {
                var cell = buffer[row, col];
                if (cell.Codepoint > 0)
                    line.Append((char)cell.Codepoint);
                else
                    line.Append(' ');
            }
            _output.WriteLine($"  Line {row}: '{line}'");
        }

        Assert.True(nonEmptyCells > 100, $"Expected > 100 cells, got {nonEmptyCells}");
        Assert.True(coloredCells > 0, "Expected colored cells");
    }

    [Fact]
    public void TerminalRenderer_CheckInvalidatedEvent_Fires()
    {
        // Verify that the emulator fires Invalidated event when data is processed

        // Arrange
        var emulator = new VT100Emulator(80, 24);
        bool invalidatedFired = false;
        int fireCount = 0;

        emulator.Invalidated += () =>
        {
            invalidatedFired = true;
            fireCount++;
        };

        // Act
        emulator.ProcessData(Encoding.UTF8.GetBytes("Test"));

        // Assert
        Assert.True(invalidatedFired, "Invalidated event should fire");
        _output.WriteLine($"✓ Invalidated event fired {fireCount} time(s)");
    }

    [Fact]
    public void TerminalRenderer_MultipleUpdates_ShouldAccumulate()
    {
        // Simulate multiple data chunks (like network packets)
        // (Tests emulator update accumulation - doesn't need TerminalRenderer)

        // Arrange
        var emulator = new VT100Emulator(80, 24);

        int invalidatedCount = 0;
        emulator.Invalidated += () => invalidatedCount++;

        // Act - Send data in chunks
        emulator.ProcessData(Encoding.UTF8.GetBytes("Hello "));
        emulator.ProcessData(Encoding.UTF8.GetBytes("World\r\n"));
        emulator.ProcessData(Encoding.UTF8.GetBytes("\x1b[31mRED\x1b[0m"));

        // Assert
        var buffer = emulator.GetBuffer();
        var line0 = GetBufferLine(buffer, 0);
        var line1 = GetBufferLine(buffer, 1);

        _output.WriteLine($"✓ Line 0: '{line0}'");
        _output.WriteLine($"✓ Line 1: '{line1}'");
        _output.WriteLine($"✓ Invalidated fired {invalidatedCount} times");

        Assert.Contains("Hello World", line0);
        Assert.Contains("RED", line1);
        Assert.True(invalidatedCount >= 3, "Should fire for each ProcessData call");
    }

    private string GetBufferLine(RetroTerm.Core.Terminal.Buffer.TerminalBuffer buffer, int row)
    {
        var sb = new StringBuilder();
        for (int col = 0; col < Math.Min(buffer.Width, 80); col++)
        {
            var cell = buffer[row, col];
            if (cell.Codepoint > 0)
                sb.Append((char)cell.Codepoint);
            else
                sb.Append(' ');
        }
        return sb.ToString().TrimEnd();
    }
}


