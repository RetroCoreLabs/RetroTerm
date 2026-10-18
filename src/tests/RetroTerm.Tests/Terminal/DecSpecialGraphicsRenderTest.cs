using System;
using System.Text;
using RetroTerm.Core.Terminal.Buffer;
using RetroTerm.Core.Terminal.Emulators;
using Xunit;

namespace RetroTerm.Tests.Terminal;

/// <summary>
/// Tests for DEC Special Graphics character set rendering against render memory.
/// </summary>
public class DecSpecialGraphicsRenderTest
{
    [Fact]
    public void DEC_Special_Graphics_ShouldRenderCorrectlyInMemory()
    {
        // Arrange
        var emulator = new TestTerminalEmulator(80, 24);

        // Act: Set G0 to DEC Special Graphics and write test pattern
        emulator.ProcessData(Encoding.UTF8.GetBytes("\x1b(0")); // ESC(0 - Set G0 to DEC Special Graphics
        emulator.ProcessData(Encoding.UTF8.GetBytes("lqqqwqqqk")); // Should become ┌───┬───┐
        emulator.ProcessData(Encoding.UTF8.GetBytes("\r\n"));
        emulator.ProcessData(Encoding.UTF8.GetBytes("x x x")); // Should become │ │ │
        emulator.ProcessData(Encoding.UTF8.GetBytes("\r\n"));
        emulator.ProcessData(Encoding.UTF8.GetBytes("mqqq nqqqj")); // Should become └─── ┼───┘

        // Assert: Check render buffer content
        var buffer = emulator.GetBuffer();

        // Line 0: lqqqwqqqk should become ┌───┬───┐
        var line0 = GetLineText(buffer, 0);
        Assert.Equal("┌───┬───┐", line0);

        // Line 1: x x x should become │ │ │
        var line1 = GetLineText(buffer, 1);
        Assert.Equal("│ │ │", line1);

        // Line 2: mqqq nqqqj should become └─── ┼───┘
        var line2 = GetLineText(buffer, 2);
        Assert.Equal("└─── ┼───┘", line2);

        // Verify character set is correctly stored for all mapped characters
        Assert.Equal(2, buffer[0, 0].CharacterSet); // ┌ (DEC Special Graphics = 2)
        Assert.Equal(2, buffer[0, 1].CharacterSet); // ─ (DEC Special Graphics = 2)
        Assert.Equal(2, buffer[0, 4].CharacterSet); // ┬ (DEC Special Graphics = 2)
        Assert.Equal(2, buffer[0, 8].CharacterSet); // ┐ (DEC Special Graphics = 2)
        Assert.Equal(2, buffer[1, 0].CharacterSet); // │ (DEC Special Graphics = 2)
        Assert.Equal(2, buffer[1, 2].CharacterSet); // │ (DEC Special Graphics = 2)
        Assert.Equal(2, buffer[1, 4].CharacterSet); // │ (DEC Special Graphics = 2)
        Assert.Equal(2, buffer[2, 0].CharacterSet); // └ (DEC Special Graphics = 2)
        Assert.Equal(2, buffer[2, 4].CharacterSet); // ┼ (DEC Special Graphics = 2)
        Assert.Equal(2, buffer[2, 8].CharacterSet); // ┘ (DEC Special Graphics = 2)

        // Verify specific Unicode codepoints are stored correctly
        Assert.Equal(0x250Cu, buffer[0, 0].Codepoint); // ┌ (U+250C)
        Assert.Equal(0x2500u, buffer[0, 1].Codepoint); // ─ (U+2500)
        Assert.Equal(0x252Cu, buffer[0, 4].Codepoint); // ┬ (U+252C)
        Assert.Equal(0x2510u, buffer[0, 8].Codepoint); // ┐ (U+2510)
        Assert.Equal(0x2502u, buffer[1, 0].Codepoint); // │ (U+2502)
        Assert.Equal(0x2502u, buffer[1, 2].Codepoint); // │ (U+2502)
        Assert.Equal(0x2502u, buffer[1, 4].Codepoint); // │ (U+2502)
        Assert.Equal(0x2514u, buffer[2, 0].Codepoint); // └ (U+2514)
        Assert.Equal(0x253Cu, buffer[2, 5].Codepoint); // ┼ (U+253C) at col 5 due to space at col 4
        Assert.Equal(0x2518u, buffer[2, 9].Codepoint); // ┘ (U+2518) at col 9
    }

    [Fact]
    public void DEC_Special_Graphics_ShouldMapAllStandardCharacters()
    {
        // Arrange
        var emulator = new TestTerminalEmulator(80, 24);

        // Act: Set G0 to DEC Special Graphics and test all standard mappings
        emulator.ProcessData(Encoding.UTF8.GetBytes("\x1b(0")); // ESC(0 - Set G0 to DEC Special Graphics

        // Test all standard DEC Special Graphics characters
        string testChars = "jklmnqtxuvw"; // All DEC Special Graphics characters
        emulator.ProcessData(Encoding.UTF8.GetBytes(testChars));

        // Assert: Check that all characters are mapped correctly
        var buffer = emulator.GetBuffer();

        // Expected mappings in order of testChars (j k l m n q t x u v w):
        // j=┘ k=┐ l=┌ m=└ n=┼ q=─ t=├ x=│ u=┤ v=┴ w=┬
        var expectedCodepoints = new uint[]
        {
            0x2518, // j -> ┘
            0x2510, // k -> ┐
            0x250C, // l -> ┌
            0x2514, // m -> └
            0x253C, // n -> ┼
            0x2500, // q -> ─
            0x251C, // t -> ├
            0x2502, // x -> │
            0x2524, // u -> ┤
            0x2534, // v -> ┴
            0x252C  // w -> ┬
        };

        for (int i = 0; i < testChars.Length; i++)
        {
            var cell = buffer[0, i];
            Assert.Equal(expectedCodepoints[i], cell.Codepoint);
            Assert.Equal(2, cell.CharacterSet); // DEC Special Graphics = 2
        }
    }

    [Fact]
    public void DEC_Special_Graphics_ShouldNotAffectNonGraphicsCharacters()
    {
        // Arrange
        var emulator = new TestTerminalEmulator(80, 24);

        // Act: Set G0 to DEC Special Graphics and write mixed content
        emulator.ProcessData(Encoding.UTF8.GetBytes("\x1b(0")); // ESC(0 - Set G0 to DEC Special Graphics
        emulator.ProcessData(Encoding.UTF8.GetBytes("Hello World! 123")); // Regular text
        emulator.ProcessData(Encoding.UTF8.GetBytes("\r\n"));
        emulator.ProcessData(Encoding.UTF8.GetBytes("lqk")); // Graphics characters
        emulator.ProcessData(Encoding.UTF8.GetBytes("\r\n"));
        emulator.ProcessData(Encoding.UTF8.GetBytes("ABC")); // More regular text

        // Assert: Check that only graphics characters are mapped
        var buffer = emulator.GetBuffer();

        // Line 0: Regular text should be mapped through DEC Special Graphics
        var line0 = GetLineText(buffer, 0);
        Assert.Equal("He┌┌o Wor┌d! 123", line0);

        // Line 1: Graphics characters should be mapped
        var line1 = GetLineText(buffer, 1);
        Assert.Equal("┌─┐", line1);

        // Line 2: Regular text should remain unchanged
        var line2 = GetLineText(buffer, 2);
        Assert.Equal("ABC", line2);

        // Verify character sets - all should be DEC Special Graphics since G0 is set
        for (int i = 0; i < "Hello World! 123".Length; i++)
        {
            Assert.Equal(2, buffer[0, i].CharacterSet); // DEC Special Graphics = 2
        }

        // Graphics characters should have DEC_Special_Graphics character set
        Assert.Equal(2, buffer[1, 0].CharacterSet); // ┌ (DEC Special Graphics = 2)
        Assert.Equal(2, buffer[1, 1].CharacterSet); // ─ (DEC Special Graphics = 2)
        Assert.Equal(2, buffer[1, 2].CharacterSet); // ┐ (DEC Special Graphics = 2)
    }

    [Fact]
    public void DEC_Special_Graphics_ShouldResetToASCII()
    {
        // Arrange
        var emulator = new TestTerminalEmulator(80, 24);

        // Act: Set G0 to DEC Special Graphics, write graphics, then reset to ASCII
        emulator.ProcessData(Encoding.UTF8.GetBytes("\x1b(0")); // ESC(0 - Set G0 to DEC Special Graphics
        emulator.ProcessData(Encoding.UTF8.GetBytes("lqk")); // Should become ┌─┐
        emulator.ProcessData(Encoding.UTF8.GetBytes("\r\n"));
        emulator.ProcessData(Encoding.UTF8.GetBytes("\x1b(B")); // ESC(B - Reset G0 to US ASCII
        emulator.ProcessData(Encoding.UTF8.GetBytes("lqk")); // Should remain lqk

        // Assert: Check that character set switching works
        var buffer = emulator.GetBuffer();

        // Line 0: Graphics characters should be mapped
        var line0 = GetLineText(buffer, 0);
        Assert.Equal("┌─┐", line0);

        // Line 1: ASCII characters should remain unchanged
        var line1 = GetLineText(buffer, 1);
        Assert.Equal("lqk", line1);

        // Verify character sets
        Assert.Equal(2, buffer[0, 0].CharacterSet); // ┌ (DEC Special Graphics = 2)
        Assert.Equal(2, buffer[0, 1].CharacterSet); // ─ (DEC Special Graphics = 2)
        Assert.Equal(2, buffer[0, 2].CharacterSet); // ┐ (DEC Special Graphics = 2)
        Assert.Equal(0, buffer[1, 0].CharacterSet); // l (US ASCII = 0)
        Assert.Equal(0, buffer[1, 1].CharacterSet); // q (US ASCII = 0)
        Assert.Equal(0, buffer[1, 2].CharacterSet); // k (US ASCII = 0)
    }

    private static string GetLineText(TerminalBuffer buffer, int row)
    {
        var sb = new StringBuilder();
        for (int col = 0; col < buffer.Width; col++)
        {
            var cell = buffer[row, col];
            if (cell.Codepoint == 0) break; // End of line
            sb.Append((char)cell.Codepoint);
        }
        return sb.ToString().TrimEnd();
    }
}
