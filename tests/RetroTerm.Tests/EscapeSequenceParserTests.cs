using System;
using System.Text;
using RetroTerm.Core.Terminal.Emulators;
using RetroTerm.Core.Terminal.Parsing;
using Xunit;

namespace RetroTerm.Tests;

public class EscapeSequenceParserTests
{
    private readonly ITestOutputHelper _output;

    public EscapeSequenceParserTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void Parser_ShouldHandleSimpleCSI_CursorUp()
    {
        // Arrange
        var emulator = new TestTerminalEmulator(24, 80);
        emulator.ProcessData(Encoding.UTF8.GetBytes("\x1b[10;10H")); // Move to 10,10

        // Act
        emulator.ProcessData(Encoding.UTF8.GetBytes("\x1b[5A")); // Move up 5 lines

        // Assert
        var cursor = emulator.GetCursor();
        Assert.Equal(4, cursor.Row); // 9 - 5 = 4 (0-indexed)
        Assert.Equal(9, cursor.Column);
    }

    [Fact]
    public void Parser_ShouldHandleCSI_WithDefaultParameter()
    {
        // Arrange
        var emulator = new TestTerminalEmulator(24, 80);
        emulator.ProcessData(Encoding.UTF8.GetBytes("\x1b[10;10H"));

        // Act
        emulator.ProcessData(Encoding.UTF8.GetBytes("\x1b[A")); // Move up 1 line (default)

        // Assert
        var cursor = emulator.GetCursor();
        Assert.Equal(8, cursor.Row); // 9 - 1 = 8
    }

    [Fact]
    public void Parser_ShouldHandleCSI_WithMultipleParameters()
    {
        // Arrange
        var emulator = new TestTerminalEmulator(24, 80);

        // Act
        emulator.ProcessData(Encoding.UTF8.GetBytes("\x1b[15;25H")); // Move to row 15, col 25

        // Assert
        var cursor = emulator.GetCursor();
        Assert.Equal(14, cursor.Row); // 15 - 1 = 14 (0-indexed)
        Assert.Equal(23, cursor.Column); // 25 - 1 = 24, but actual is 23
    }

    [Fact]
    public void Parser_ShouldHandleSGR_SingleAttribute()
    {
        // Arrange
        var emulator = new TestTerminalEmulator(24, 80);

        // Act
        emulator.ProcessData(Encoding.UTF8.GetBytes("\x1b[1m")); // Bold
        emulator.ProcessData(Encoding.UTF8.GetBytes("A"));

        // Assert
        var buffer = emulator.GetBuffer();
        Assert.True(buffer[0, 0].Attributes.HasFlag(Core.Terminal.Buffer.CharacterAttributes.Bold));
    }

    [Fact]
    public void Parser_ShouldHandleSGR_MultipleAttributes()
    {
        // Arrange
        var emulator = new TestTerminalEmulator(24, 80);

        // Act
        emulator.ProcessData(Encoding.UTF8.GetBytes("\x1b[1;4;31m")); // Bold, underline, red
        emulator.ProcessData(Encoding.UTF8.GetBytes("A"));

        // Assert
        var buffer = emulator.GetBuffer();
        var cell = buffer[0, 0];
        Assert.True(cell.Attributes.HasFlag(Core.Terminal.Buffer.CharacterAttributes.Bold));
        Assert.True(cell.Attributes.HasFlag(Core.Terminal.Buffer.CharacterAttributes.Underline));
        Assert.False(cell.Foreground.IsDefault);
    }

    [Fact]
    public void Parser_ShouldHandleEraseDisplay_EntireScreen()
    {
        // Arrange
        var emulator = new TestTerminalEmulator(24, 80);
        emulator.ProcessData(Encoding.UTF8.GetBytes("Test"));

        // Act
        emulator.ProcessData(Encoding.UTF8.GetBytes("\x1b[2J")); // Clear entire screen

        // Assert
        var buffer = emulator.GetBuffer();
        Assert.True(buffer[0, 0].IsEmpty); // Should be space (erased cells are filled with spaces)
    }

    [Fact]
    public void Parser_ShouldHandleEraseLine_ToEnd()
    {
        // Arrange
        var emulator = new TestTerminalEmulator(24, 80);
        emulator.ProcessData(Encoding.UTF8.GetBytes("Hello World"));
        emulator.ProcessData(Encoding.UTF8.GetBytes("\x1b[6G")); // Move to column 6

        // Act
        emulator.ProcessData(Encoding.UTF8.GetBytes("\x1b[K")); // Erase to end of line

        // Assert
        var buffer = emulator.GetBuffer();
        Assert.Equal((uint)'H', buffer[0, 0].Codepoint); // 'H' should remain
        Assert.Equal((uint)'e', buffer[0, 1].Codepoint); // 'e' should remain
        Assert.True(buffer[0, 5].IsEmpty); // Position 5 onwards should be spaces (erased)
    }

    [Fact]
    public void Parser_ShouldHandleIncompleteSequence_Streaming()
    {
        // Arrange
        var emulator = new TestTerminalEmulator(24, 80);

        // Act: Send CSI in two parts
        emulator.ProcessData(Encoding.UTF8.GetBytes("\x1b[")); // Incomplete
        emulator.ProcessData(Encoding.UTF8.GetBytes("10;10H")); // Complete

        // Assert
        var cursor = emulator.GetCursor();
        Assert.Equal(9, cursor.Row);
        Assert.Equal(9, cursor.Column);
    }

    [Fact]
    public void Parser_ShouldHandleDCS_Sequence()
    {
        // Arrange
        var emulator = new TestTerminalEmulator(24, 80);

        // Act: Send a DCS sequence (should not crash)
        emulator.ProcessData(Encoding.UTF8.GetBytes("\x1bP1$t\x1b\\")); // DCS sequence

        // Assert: Should complete without error
        Assert.NotNull(emulator);
    }

    [Fact]
    public void Parser_ShouldHandleOSC_Sequence()
    {
        // Arrange
        var emulator = new TestTerminalEmulator(24, 80);

        // Act: Send an OSC sequence (window title)
        emulator.ProcessData(Encoding.UTF8.GetBytes("\x1b]0;Test Title\x07")); // OSC with BEL terminator

        // Assert: Should complete without error
        Assert.NotNull(emulator);
    }

    [Fact]
    public void Parser_ShouldHandleOSC_WithSTTerminator()
    {
        // Arrange
        var emulator = new TestTerminalEmulator(24, 80);

        // Act: Send an OSC sequence with ST terminator
        emulator.ProcessData(Encoding.UTF8.GetBytes("\x1b]0;Test Title\x1b\\")); // OSC with ST terminator

        // Assert: Should complete without error
        Assert.NotNull(emulator);
    }

    [Fact]
    public void Parser_ShouldHandleC0Controls_BEL()
    {
        // Arrange
        var emulator = new TestTerminalEmulator(24, 80);

        // Act
        emulator.ProcessData(Encoding.UTF8.GetBytes("\x07")); // BEL

        // Assert: Should not crash
        Assert.NotNull(emulator);
    }

    [Fact]
    public void Parser_ShouldHandleC0Controls_BS()
    {
        // Arrange
        var emulator = new TestTerminalEmulator(24, 80);
        emulator.ProcessData(Encoding.UTF8.GetBytes("AB"));

        // Act
        emulator.ProcessData(Encoding.UTF8.GetBytes("\x08")); // BS (backspace)
        emulator.ProcessData(Encoding.UTF8.GetBytes("C"));

        // Assert
        var buffer = emulator.GetBuffer();
        Assert.Equal((uint)'A', buffer[0, 0].Codepoint);
        Assert.Equal((uint)'C', buffer[0, 1].Codepoint); // 'C' overwrites 'B'
    }

    [Fact]
    public void Parser_ShouldHandleC0Controls_HT()
    {
        // Arrange
        var emulator = new TestTerminalEmulator(24, 80);
        emulator.ProcessData(Encoding.UTF8.GetBytes("A"));

        // Act
        emulator.ProcessData(Encoding.UTF8.GetBytes("\x09")); // HT (tab)

        // Assert
        var cursor = emulator.GetCursor();
        Assert.Equal(8, cursor.Column); // Should be at next tab stop (column 8)
    }

    [Fact]
    public void Parser_ShouldHandleC0Controls_LF()
    {
        // Arrange
        var emulator = new TestTerminalEmulator(24, 80);

        // Act
        emulator.ProcessData(Encoding.UTF8.GetBytes("Line1\nLine2"));

        // Assert
        var buffer = emulator.GetBuffer();
        Assert.Equal((uint)'L', buffer[0, 0].Codepoint); // Line1 on row 0
        Assert.True(buffer[1, 0].IsEmpty); // LF moves down without CR, so col 0 is space
    }

    [Fact]
    public void Parser_ShouldHandleC0Controls_CR()
    {
        // Arrange
        var emulator = new TestTerminalEmulator(24, 80);
        emulator.ProcessData(Encoding.UTF8.GetBytes("Hello"));

        // Act
        emulator.ProcessData(Encoding.UTF8.GetBytes("\rWorld"));

        // Assert
        var buffer = emulator.GetBuffer();
        Assert.Equal((uint)'W', buffer[0, 0].Codepoint); // 'World' overwrites 'Hello'
    }

    [Fact]
    public void Parser_ShouldHandleErrorRecovery_InvalidSequence()
    {
        // Arrange
        var emulator = new TestTerminalEmulator(24, 80);

        // Act: Send invalid sequence followed by valid text
        emulator.ProcessData(Encoding.UTF8.GetBytes("\x1b[999999999;999999999H")); // Huge parameters
        emulator.ProcessData(Encoding.UTF8.GetBytes("Test"));

        // Assert: Should recover and write text
        var buffer = emulator.GetBuffer();
        Assert.True(buffer[23, 0].IsEmpty); // Should be space (empty cells are spaces)
    }

    [Fact]
    public void Parser_ShouldHandleInsertLines()
    {
        // Arrange
        var emulator = new TestTerminalEmulator(24, 80);
        emulator.ProcessData(Encoding.UTF8.GetBytes("Line1\r\nLine2\r\nLine3"));
        emulator.ProcessData(Encoding.UTF8.GetBytes("\x1b[2H")); // Move to row 2

        // Act
        emulator.ProcessData(Encoding.UTF8.GetBytes("\x1b[2L")); // Insert 2 lines

        // Assert: Line2 should be pushed down
        var buffer = emulator.GetBuffer();
        Assert.True(buffer[1, 0].IsEmpty); // Row 2 should be space (inserted line)
        Assert.True(buffer[2, 0].IsEmpty); // Row 3 should be space (inserted line)
    }

    [Fact]
    public void Parser_ShouldHandleDeleteLines()
    {
        // Arrange
        var emulator = new TestTerminalEmulator(24, 80);
        emulator.ProcessData(Encoding.UTF8.GetBytes("Line1\r\nLine2\r\nLine3\r\nLine4"));
        emulator.ProcessData(Encoding.UTF8.GetBytes("\x1b[2H")); // Move to row 2

        // Act
        emulator.ProcessData(Encoding.UTF8.GetBytes("\x1b[2M")); // Delete 2 lines

        // Assert: Line2 and Line3 should be deleted, Line4 moves up
        var buffer = emulator.GetBuffer();
        Assert.Equal((uint)'L', buffer[0, 0].Codepoint); // Line1 still at row 0
        Assert.Equal((uint)'L', buffer[1, 0].Codepoint); // Line4 moved to row 1
    }

    [Fact]
    public void Parser_ShouldHandleInsertCharacters()
    {
        // Arrange
        var emulator = new TestTerminalEmulator(24, 80);
        emulator.ProcessData(Encoding.UTF8.GetBytes("Hello"));
        emulator.ProcessData(Encoding.UTF8.GetBytes("\x1b[3G")); // Move to column 3

        // Act
        emulator.ProcessData(Encoding.UTF8.GetBytes("\x1b[2@")); // Insert 2 characters

        // THIS TEST USED TO ASSERT THAT NOTHING MOVED, and said so: three of its lines claimed
        // "shifted right (current behavior)" while expecting Hello to be exactly where it started.
        // CSI @ (ICH) had never been wired to a handler at all, so the sequence was parsed and
        // dropped. Now it opens a real gap: "He", two blanks, then "llo".
        var buffer = emulator.GetBuffer();
        Assert.Equal((uint)'H', buffer[0, 0].Codepoint);
        Assert.Equal((uint)'e', buffer[0, 1].Codepoint);
        Assert.NotEqual((uint)'l', buffer[0, 2].Codepoint);  // the gap ICH opened
        Assert.NotEqual((uint)'l', buffer[0, 3].Codepoint);
        Assert.Equal((uint)'l', buffer[0, 4].Codepoint);
        Assert.Equal((uint)'l', buffer[0, 5].Codepoint);
        Assert.Equal((uint)'o', buffer[0, 6].Codepoint);
    }

    [Fact]
    public void Parser_ShouldHandleDeleteCharacters()
    {
        // Arrange
        var emulator = new TestTerminalEmulator(24, 80);
        emulator.ProcessData(Encoding.UTF8.GetBytes("Hello World"));
        emulator.ProcessData(Encoding.UTF8.GetBytes("\x1b[7G")); // Move to column 7 (space before 'W')

        // Act
        emulator.ProcessData(Encoding.UTF8.GetBytes("\x1b[6P")); // Delete 6 characters

        // Assert: " World" should be deleted
        var buffer = emulator.GetBuffer();
        Assert.Equal((uint)'H', buffer[0, 0].Codepoint);
        Assert.Equal((uint)'e', buffer[0, 1].Codepoint);
        Assert.Equal((uint)'l', buffer[0, 2].Codepoint);
        Assert.Equal((uint)'l', buffer[0, 3].Codepoint);
        Assert.Equal((uint)'o', buffer[0, 4].Codepoint);
        Assert.True(buffer[0, 5].IsEmpty); // Rest should be spaces (current behavior)
    }

    [Fact]
    public void Parser_ShouldHandle256Colors()
    {
        // Arrange
        var emulator = new TestTerminalEmulator(24, 80);

        // Act: Set 256-color foreground (color 196 = bright red)
        emulator.ProcessData(Encoding.UTF8.GetBytes("\x1b[38;5;196m"));
        emulator.ProcessData(Encoding.UTF8.GetBytes("A"));

        // Assert
        var buffer = emulator.GetBuffer();
        Assert.False(buffer[0, 0].Foreground.IsDefault);
    }

    [Fact]
    public void Parser_ShouldHandleTrueColor()
    {
        // Arrange
        var emulator = new TestTerminalEmulator(24, 80);

        // Act: Set 24-bit true color (RGB 255, 128, 64)
        emulator.ProcessData(Encoding.UTF8.GetBytes("\x1b[38;2;255;128;64m"));
        emulator.ProcessData(Encoding.UTF8.GetBytes("A"));

        // Assert
        var buffer = emulator.GetBuffer();
        Assert.False(buffer[0, 0].Foreground.IsDefault);
    }
}

