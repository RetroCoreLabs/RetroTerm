using System;
using System.Text;
using RetroTerm.Core.Terminal.Buffer;
using RetroTerm.Core.Terminal.Emulators;
using Xunit;

namespace RetroTerm.Tests;

public class FullScreenRenderingTest
{
    private readonly ITestOutputHelper _output;

    public FullScreenRenderingTest(ITestOutputHelper output)
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
    public void FullScreen_ShouldRender24LinesOf79Characters()
    {
        // Arrange: Create emulator with 80x24 screen (standard VT100 size)
        var emulator = new TestTerminalEmulator(80, 24);

        // Create 24 unique lines, each 79 characters wide
        var expectedLines = new string[24];
        for (int i = 0; i < 24; i++)
        {
            // Create a unique line with a pattern that's easy to identify
            // Format: "Line 01: ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789abcdefghijklmnopqrstuvwxyz..."
            var lineNumber = (i + 1).ToString("D2");
            var content = $"Line {lineNumber}: ";

            // Fill the rest with a repeating pattern to make it exactly 79 characters
            var pattern = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789abcdefghijklmnopqrstuvwxyz";
            while (content.Length < 79)
            {
                var remaining = 79 - content.Length;
                if (remaining >= pattern.Length)
                {
                    content += pattern;
                }
                else
                {
                    content += pattern.Substring(0, remaining);
                }
            }

            expectedLines[i] = content;
        }

        // Act: Write all 24 lines to the terminal
        for (int i = 0; i < 24; i++)
        {
            // Move cursor to the start of each line
            emulator.ProcessData(Encoding.UTF8.GetBytes($"\x1b[{i + 1};1H"));

            // Write the line content
            emulator.ProcessData(Encoding.UTF8.GetBytes(expectedLines[i]));
        }

        // Assert: Verify that all lines are rendered correctly
        var buffer = emulator.GetBuffer();

        _output.WriteLine("=== FULL SCREEN RENDERING TEST ===");
        _output.WriteLine($"Screen size: {buffer.Width}x{buffer.Height}");
        _output.WriteLine("");

        for (int row = 0; row < 24; row++)
        {
            var actualLine = GetLineText(buffer, row);
            var expectedLine = expectedLines[row];

            _output.WriteLine($"Row {row,2}: '{actualLine}'");

            // Assert that each line matches exactly
            Assert.Equal(expectedLine, actualLine);
        }

        _output.WriteLine("");
        _output.WriteLine("=== ALL LINES RENDERED CORRECTLY ===");
    }

    [Fact]
    public void FullScreen_ShouldHandleSequentialWrites()
    {
        // Arrange: Create emulator with 80x24 screen
        var emulator = new TestTerminalEmulator(80, 24);

        // Create 24 unique lines, each 79 characters wide
        var expectedLines = new string[24];
        var sb = new StringBuilder();

        for (int i = 0; i < 24; i++)
        {
            var lineNumber = (i + 1).ToString("D2");
            var content = $"Line {lineNumber}: ";

            // Fill with unique pattern
            var pattern = $"{i:D2}".PadLeft(77 - content.Length, (char)('A' + (i % 26)));
            content += pattern;

            expectedLines[i] = content;

            // Add to sequential write buffer
            sb.Append(content);
            if (i < 23) // Don't add newline after last line
            {
                sb.Append("\r\n");
            }
        }

        // Act: Write all lines in one go
        emulator.ProcessData(Encoding.UTF8.GetBytes(sb.ToString()));

        // Assert: Verify that all lines are rendered correctly
        var buffer = emulator.GetBuffer();

        _output.WriteLine("=== SEQUENTIAL WRITE TEST ===");
        _output.WriteLine($"Screen size: {buffer.Width}x{buffer.Height}");
        _output.WriteLine("");

        for (int row = 0; row < 24; row++)
        {
            var actualLine = GetLineText(buffer, row);
            var expectedLine = expectedLines[row];

            _output.WriteLine($"Row {row,2}: Expected='{expectedLine}'");
            _output.WriteLine($"        Actual  ='{actualLine}'");

            if (actualLine != expectedLine)
            {
                _output.WriteLine($"        MISMATCH!");
            }

            // Assert that each line matches exactly
            Assert.Equal(expectedLine, actualLine);
        }

        _output.WriteLine("");
        _output.WriteLine("=== ALL LINES RENDERED CORRECTLY ===");
    }

    [Fact]
    public void FullScreen_ShouldRenderWithoutScrollRegion()
    {
        // Arrange: Create emulator with 80x24 screen
        var emulator = new TestTerminalEmulator(80, 24);

        // Create 24 unique lines
        var expectedLines = new string[24];
        for (int i = 0; i < 24; i++)
        {
            expectedLines[i] = $"Line {i + 1:D2}".PadRight(79, (char)('0' + (i % 10)));
        }

        // Act: Write all lines
        for (int i = 0; i < 24; i++)
        {
            emulator.ProcessData(Encoding.UTF8.GetBytes($"\x1b[{i + 1};1H{expectedLines[i]}"));
        }

        // Assert: Verify buffer contents
        var buffer = emulator.GetBuffer();

        _output.WriteLine("=== FULL SCREEN WITHOUT SCROLL REGION ===");
        _output.WriteLine($"Screen size: {buffer.Width}x{buffer.Height}");
        _output.WriteLine("");

        for (int row = 0; row < 24; row++)
        {
            var actualLine = GetLineText(buffer, row);
            _output.WriteLine($"Row {row,2}: '{actualLine}'");
            Assert.Equal(expectedLines[row], actualLine);
        }
    }
}

