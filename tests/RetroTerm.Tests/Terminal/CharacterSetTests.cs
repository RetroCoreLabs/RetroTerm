using System;
using System.Text;
using RetroTerm.Core.Terminal.Buffer;
using RetroTerm.Core.Terminal.Emulators;
using Xunit;

namespace RetroTerm.Tests.Terminal;

/// <summary>
/// Tests for character set handling, specifically DEC Special Graphics mapping.
/// </summary>
public class CharacterSetTests
{
    [Fact]
    public void DEC_Special_Graphics_ShouldMapCharactersCorrectly()
    {
        // Arrange
        var emulator = new TestTerminalEmulator(80, 24);

        // Act: Set G0 to DEC Special Graphics and write test characters
        Console.WriteLine("Sending ESC(0 sequence...");
        emulator.ProcessData(Encoding.UTF8.GetBytes("\x1b(0")); // ESC(0 - Set G0 to DEC Special Graphics
        Console.WriteLine("ESC(0 sequence sent.");
        emulator.ProcessData(Encoding.UTF8.GetBytes("lqqqwqqqk")); // Should become ┌───┬───┐
        emulator.ProcessData(Encoding.UTF8.GetBytes("\r\n"));
        emulator.ProcessData(Encoding.UTF8.GetBytes("x x x")); // Should become │ │ │
        emulator.ProcessData(Encoding.UTF8.GetBytes("\r\n"));
        emulator.ProcessData(Encoding.UTF8.GetBytes("mqqq nqqqj")); // Should become └────┼────┘

        // Assert: Check that characters are mapped correctly
        var buffer = emulator.GetBuffer();

        // Debug: Check character set of first character
        var firstCell = buffer[0, 0];
        Console.WriteLine($"First cell: codepoint={firstCell.Codepoint} ({(char)firstCell.Codepoint}), characterSet={firstCell.CharacterSet}");

        // First line: lqqqwqqqk should become ┌───┬───┐
        var line1 = GetLineText(buffer, 0);
        Console.WriteLine($"Line 1: '{line1}'");
        Assert.Equal("┌───┬───┐", line1);

        // Second line: x x x should become │ │ │
        var line2 = GetLineText(buffer, 1);
        Assert.Equal("│ │ │", line2);

        // Third line: mqqq nqqqj should become └─── ┼───┘
        var line3 = GetLineText(buffer, 2);
        Assert.Equal("└─── ┼───┘", line3);
    }

    [Fact]
    public void DEC_Special_Graphics_ShouldStoreCorrectCharacterSet()
    {
        // Arrange
        var emulator = new TestTerminalEmulator(80, 24);

        // Act: Set G0 to DEC Special Graphics and write a character
        emulator.ProcessData(Encoding.UTF8.GetBytes("\x1b(0")); // ESC(0 - Set G0 to DEC Special Graphics
        emulator.ProcessData(Encoding.UTF8.GetBytes("l")); // 'l' should map to '┌'

        // Assert: Check that the character set is stored correctly
        var buffer = emulator.GetBuffer();
        var cell = buffer[0, 0];

        // The character set should be 2 (DEC Special Graphics)
        Assert.Equal(2, cell.CharacterSet);

        // The codepoint should be the mapped character '┌' (U+250C)
        Assert.Equal(0x250Cu, cell.Codepoint);
    }

    [Fact]
    public void US_ASCII_ShouldNotMapCharacters()
    {
        // Arrange
        var emulator = new TestTerminalEmulator(80, 24);

        // Act: Set G0 to US ASCII and write test characters
        emulator.ProcessData(Encoding.UTF8.GetBytes("\x1b(B")); // ESC(B - Set G0 to US ASCII
        emulator.ProcessData(Encoding.UTF8.GetBytes("lqqqwqqqk")); // Should remain as-is

        // Assert: Check that characters are NOT mapped
        var buffer = emulator.GetBuffer();
        var line = GetLineText(buffer, 0);
        Assert.Equal("lqqqwqqqk", line);

        // Character set should be 0 (US ASCII)
        var cell = buffer[0, 0];
        Assert.Equal(0, cell.CharacterSet);
    }

    private static string GetLineText(TerminalBuffer buffer, int row)
    {
        var sb = new StringBuilder();
        for (int col = 0; col < buffer.Width; col++)
        {
            var cell = buffer[row, col];
            if (cell.Codepoint == 0) break; // Stop at first empty cell
            sb.Append((char)cell.Codepoint);
        }
        // Trim trailing spaces since empty cells are represented as spaces
        return sb.ToString().TrimEnd();
    }
}

/// <summary>
/// Test terminal emulator that exposes internal state for testing.
/// </summary>
public class TestTerminalEmulator : TerminalEmulatorBase
{
    public TestTerminalEmulator(int width, int height) : base(width, height)
    {
    }

    // The base is ECMA-48, not a terminal, so every emulator must declare what it is.
    public override RetroTerm.Core.Terminal.Profiles.TerminalProfile Profile
        => RetroTerm.Core.Terminal.Profiles.TerminalProfile.Ansi;

    public void ProcessData(byte[] data)
    {
        foreach (byte b in data)
        {
            Parser.ProcessBytes(new byte[] { b });
        }
    }
}
