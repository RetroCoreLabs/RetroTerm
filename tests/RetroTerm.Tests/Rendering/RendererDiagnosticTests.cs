using System;
using System.Text;
using RetroTerm.Core.Terminal;
using RetroTerm.Core.Terminal.Buffer;
using RetroTerm.Core.Terminal.Emulators;
using Xunit;

namespace RetroTerm.Tests.Rendering;

/// <summary>
/// Diagnostic tests to verify what's actually in the buffer when rendering fails
/// </summary>
public class RendererDiagnosticTests
{
    private readonly ITestOutputHelper _output;

    public RendererDiagnosticTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void DiagnoseColorRendering_BasicColors()
    {
        // Arrange
        var emulator = new VT100Emulator(80, 24);

        // Act - Send colored text
        var colorText = "\x1b[31mRED \x1b[32mGREEN \x1b[34mBLUE\x1b[0m";
        emulator.ProcessData(Encoding.UTF8.GetBytes(colorText));

        // Diagnose
        var buffer = emulator.GetBuffer();
        _output.WriteLine("=== BUFFER DUMP FOR BASIC COLORS ===");
        for (int col = 0; col < 15; col++)
        {
            var cell = buffer[0, col];
            _output.WriteLine($"Col {col}: '{(char)cell.Codepoint}' " +
                            $"FG={cell.Foreground.IsDefault}/{cell.Foreground.IsIndexed}/{cell.Foreground.Index} " +
                            $"BG={cell.Background.IsDefault}/{cell.Background.IsIndexed}/{cell.Background.Index} " +
                            $"Attrs={cell.Attributes}");
        }
    }

    [Fact]
    public void DiagnoseColorRendering_256Colors()
    {
        // Arrange
        var emulator = new VT100Emulator(80, 24);

        // Act - Send 256-color text
        var colorText = "\x1b[38;5;196mCOLOR196\x1b[0m";
        emulator.ProcessData(Encoding.UTF8.GetBytes(colorText));

        // Diagnose
        var buffer = emulator.GetBuffer();
        _output.WriteLine("=== BUFFER DUMP FOR 256 COLORS ===");
        for (int col = 0; col < 10; col++)
        {
            var cell = buffer[0, col];
            _output.WriteLine($"Col {col}: '{(char)cell.Codepoint}' " +
                            $"FG={cell.Foreground.IsDefault}/{cell.Foreground.IsIndexed}/{cell.Foreground.Index} " +
                            $"BG={cell.Background.IsDefault}");
        }
    }

    [Fact]
    public void DiagnoseAttributeRendering()
    {
        // Arrange
        var emulator = new VT100Emulator(80, 24);

        // Act - Send text with attributes
        var attrText = "\x1b[1mBOLD \x1b[4mUNDERLINE \x1b[7mREVERSE\x1b[0m";
        emulator.ProcessData(Encoding.UTF8.GetBytes(attrText));

        // Diagnose
        var buffer = emulator.GetBuffer();
        _output.WriteLine("=== BUFFER DUMP FOR ATTRIBUTES ===");
        for (int col = 0; col < 25; col++)
        {
            var cell = buffer[0, col];
            var ch = cell.Codepoint == 0 ? ' ' : (char)cell.Codepoint;
            _output.WriteLine($"Col {col}: '{ch}' Attrs={cell.Attributes} " +
                            $"Bold={cell.Attributes.HasAttribute(CharacterAttributes.Bold)} " +
                            $"Underline={cell.Attributes.HasAttribute(CharacterAttributes.Underline)} " +
                            $"Reverse={cell.Attributes.HasAttribute(CharacterAttributes.Reverse)}");
        }
    }

    [Fact]
    public void DiagnoseTestServer_ColorTest()
    {
        // Arrange
        var emulator = new VT100Emulator(80, 24);

        // Act - Simulate what test server sends for "Basic Colors (8 colors)" test
        var testOutput = new StringBuilder();
        testOutput.Append("Foreground colors:\r\n");

        // Standard ANSI colors
        string[] colorNames = { "Black", "Red", "Green", "Yellow", "Blue", "Magenta", "Cyan", "White" };
        for (int i = 0; i < 8; i++)
        {
            testOutput.Append($"\x1b[3{i}mColor {i}: {colorNames[i]}\x1b[0m\r\n");
        }

        emulator.ProcessData(Encoding.UTF8.GetBytes(testOutput.ToString()));

        // Diagnose
        var buffer = emulator.GetBuffer();
        _output.WriteLine("=== BUFFER DUMP FOR TEST SERVER COLOR TEST ===");
        for (int row = 0; row < 10; row++)
        {
            _output.WriteLine($"\n--- Row {row} ---");
            for (int col = 0; col < 30; col++)
            {
                var cell = buffer[row, col];
                if (cell.Codepoint != 0 && cell.Codepoint != ' ')
                {
                    _output.WriteLine($"  [{row},{col}]: '{(char)cell.Codepoint}' " +
                                    $"FG_Idx={cell.Foreground.Index} " +
                                    $"FG_IsIndexed={cell.Foreground.IsIndexed} " +
                                    $"FG_IsDefault={cell.Foreground.IsDefault}");
                }
            }
        }
    }
}

