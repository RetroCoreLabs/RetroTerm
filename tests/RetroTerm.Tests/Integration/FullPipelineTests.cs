using System;
using System.Text;
using System.Threading.Tasks;
using RetroTerm.Core.Protocols.Mock;
using RetroTerm.Core.Session;
using RetroTerm.Core.Terminal.Emulators;
using Xunit;

namespace RetroTerm.Tests.Integration;

/// <summary>
/// Full pipeline integration tests to find the actual bugs
/// Tests: MockConnection → TerminalSession → Emulator → Buffer
/// </summary>
public class FullPipelineTests
{
    private readonly ITestOutputHelper _output;

    public FullPipelineTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task FullPipeline_MockConnection_DataShouldReachBuffer()
    {
        // This test will show us WHERE data is getting lost

        // Arrange
        var emulator = new VT100Emulator(80, 24);
        var session = new TerminalSession(emulator, "Test");
        var mockConnection = new MockConnection(autoRespond: false); // Manual control

        bool emulatorInvalidated = false;

        session.StatusChanged += status => _output.WriteLine($"Status: {status}");
        session.ErrorOccurred += ex => _output.WriteLine($"Error: {ex.Message}");
        emulator.Invalidated += () => emulatorInvalidated = true;

        // Act - Connect
        await session.ConnectAsync(mockConnection, TestContext.Current.CancellationToken);
        _output.WriteLine("✓ Session connected");

        // Act - Send simple text
        mockConnection.SimulateReceive("Hello World");
        await Task.Delay(100, TestContext.Current.CancellationToken); // Give time for processing

        // Assert - Check buffer
        var buffer = emulator.GetBuffer();
        var firstLine = GetBufferLine(buffer, 0);

        _output.WriteLine($"Buffer line 0: '{firstLine}'");
        _output.WriteLine($"Emulator invalidated: {emulatorInvalidated}");

        Assert.Contains("Hello", firstLine);
    }

    [Fact]
    public async Task FullPipeline_ColoredText_ShouldHaveColors()
    {
        // This test will show us if colors are being stored in buffer

        // Arrange
        var emulator = new VT100Emulator(80, 24);
        var session = new TerminalSession(emulator, "Test");
        var mockConnection = new MockConnection(autoRespond: false);

        // Act - Connect
        await session.ConnectAsync(mockConnection, TestContext.Current.CancellationToken);

        // Act - Send RED text
        mockConnection.SimulateReceive("\x1b[31mRED\x1b[0m");
        await Task.Delay(100, TestContext.Current.CancellationToken);

        // Assert - Check buffer cell colors
        var buffer = emulator.GetBuffer();
        var cell = buffer[0, 0]; // 'R'

        _output.WriteLine($"Cell [0,0]: Char='{(char)cell.Codepoint}' FG={cell.Foreground.IsIndexed}/{cell.Foreground.Index}");

        Assert.Equal('R', (char)cell.Codepoint);
        Assert.True(cell.Foreground.IsIndexed, "Foreground should be indexed");
        Assert.Equal(1, cell.Foreground.Index); // Red = index 1
    }

    [Fact]
    public async Task FullPipeline_TelnetLikeData_ShouldProcess()
    {
        // This simulates what the test server sends

        // Arrange
        var emulator = new VT100Emulator(80, 24);
        var session = new TerminalSession(emulator, "Test");
        var mockConnection = new MockConnection(autoRespond: false);

        // Act - Connect
        await session.ConnectAsync(mockConnection, TestContext.Current.CancellationToken);

        // Act - Simulate test server welcome message
        mockConnection.SimulateReceive("\x1b[2J\x1b[H"); // Clear screen, home cursor
        mockConnection.SimulateReceive("\x1b[1;36m"); // Bold cyan
        mockConnection.SimulateReceive("=== Test Menu ===\r\n");
        mockConnection.SimulateReceive("\x1b[0m"); // Reset
        await Task.Delay(100, TestContext.Current.CancellationToken);

        // Assert
        var buffer = emulator.GetBuffer();
        var line = GetBufferLine(buffer, 0);

        _output.WriteLine($"Buffer line 0: '{line}'");
        var cursor = emulator.GetCursor();
        _output.WriteLine($"Cursor: ({cursor.Row}, {cursor.Column})");

        Assert.Contains("Test Menu", line);
    }

    [Fact]
    public async Task Session_DisconnectAsync_ShouldNotHang()
    {
        // Test the disconnect hang issue

        // Arrange
        var emulator = new VT100Emulator(80, 24);
        var session = new TerminalSession(emulator, "Test");
        var mockConnection = new MockConnection(autoRespond: false);

        await session.ConnectAsync(mockConnection, TestContext.Current.CancellationToken);

        // Act - Disconnect with timeout
        var disconnectTask = session.DisconnectAsync();
        var timeoutTask = Task.Delay(2000, TestContext.Current.CancellationToken);
        var completed = await Task.WhenAny(disconnectTask, timeoutTask);

        // Assert - Should complete quickly, not hang
        Assert.Same(disconnectTask, completed);
        Assert.Equal(Core.Protocols.ConnectionStatus.Disconnected, session.Status);
    }

    [Fact]
    public async Task FullPipeline_RapidData_ShouldNotCrash()
    {
        // Test rapid data like the test server sends

        // Arrange
        var emulator = new VT100Emulator(80, 24);
        var session = new TerminalSession(emulator, "Test");
        var mockConnection = new MockConnection(autoRespond: false);

        await session.ConnectAsync(mockConnection, TestContext.Current.CancellationToken);

        // Act - Send lots of data rapidly
        for (int i = 0; i < 100; i++)
        {
            mockConnection.SimulateReceive($"Line {i}\r\n");
        }

        await Task.Delay(500, TestContext.Current.CancellationToken); // Give time to process

        // Assert - Should still be connected, not crashed
        Assert.True(session.IsConnected);

        var buffer = emulator.GetBuffer();
        var cursor = emulator.GetCursor();
        _output.WriteLine($"Cursor: ({cursor.Row}, {cursor.Column})");

        // Should have scrolled and show some data - check several lines
        var linesWithContent = 0;
        for (int row = 0; row < 24; row++)
        {
            var line = GetBufferLine(buffer, row);
            if (!string.IsNullOrWhiteSpace(line))
            {
                linesWithContent++;
                if (linesWithContent <= 5) // Print first 5 non-empty lines for debugging
                {
                    _output.WriteLine($"Row {row}: '{line.Trim()}'");
                }
            }
        }

        _output.WriteLine($"Total lines with content: {linesWithContent}");

        // After sending 100 lines to a 24-line buffer, we should have at least some content
        Assert.True(linesWithContent > 0, $"Expected some lines with content, but found none");
    }

    // Helper to get a line from buffer as string
    private string GetBufferLine(Core.Terminal.Buffer.TerminalBuffer buffer, int row)
    {
        var sb = new StringBuilder();
        for (int col = 0; col < buffer.Width; col++)
        {
            var cell = buffer[row, col];
            if (cell.Codepoint > 0)
            {
                sb.Append((char)cell.Codepoint);
            }
            else
            {
                sb.Append(' ');
            }
        }
        return sb.ToString();
    }
}

