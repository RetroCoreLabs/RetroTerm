using System;
using System.Text;
using RetroTerm.Core.Terminal;
using RetroTerm.Core.Terminal.Buffer;
using RetroTerm.Core.Terminal.Emulators;
using Xunit;

namespace RetroTerm.Tests.Rendering;

/// <summary>
/// Tests to verify that colors and attributes are correctly parsed and stored in the buffer
/// </summary>
public class ColorAndAttributeTests
{
    [Fact]
    public void BasicColors_ShouldBeStoredInBuffer()
    {
        // Arrange
        var emulator = new VT100Emulator(80, 24);

        // Act - Send red text
        var redText = "\x1b[31mRED\x1b[0m";
        emulator.ProcessData(Encoding.UTF8.GetBytes(redText));

        // Assert
        var buffer = emulator.GetBuffer();
        var cell = buffer[0, 0]; // 'R'

        Assert.True(cell.Foreground.IsIndexed, "Foreground should be indexed color");
        Assert.Equal(1, cell.Foreground.Index); // Red = ANSI color 1
    }

    [Fact]
    public void BoldAttribute_ShouldBeStoredInBuffer()
    {
        // Arrange
        var emulator = new VT100Emulator(80, 24);

        // Act - Send bold text
        var boldText = "\x1b[1mBOLD\x1b[0m";
        emulator.ProcessData(Encoding.UTF8.GetBytes(boldText));

        // Assert
        var buffer = emulator.GetBuffer();
        var cell = buffer[0, 0]; // 'B'

        Assert.True(cell.Attributes.HasAttribute(CharacterAttributes.Bold),
                   $"Cell should have Bold attribute. Actual attributes: {cell.Attributes}");
    }

    [Fact]
    public void UnderlineAttribute_ShouldBeStoredInBuffer()
    {
        // Arrange
        var emulator = new VT100Emulator(80, 24);

        // Act - Send underlined text
        var underlineText = "\x1b[4mUNDERLINE\x1b[0m";
        emulator.ProcessData(Encoding.UTF8.GetBytes(underlineText));

        // Assert
        var buffer = emulator.GetBuffer();
        var cell = buffer[0, 0]; // 'U'

        Assert.True(cell.Attributes.HasAttribute(CharacterAttributes.Underline),
                   $"Cell should have Underline attribute. Actual attributes: {cell.Attributes}");
    }

    [Fact]
    public void MultipleAttributes_ShouldBeCombined()
    {
        // Arrange
        var emulator = new VT100Emulator(80, 24);

        // Act - Send bold + underline + green text
        var combinedText = "\x1b[1;4;32mCOMBINED\x1b[0m";
        emulator.ProcessData(Encoding.UTF8.GetBytes(combinedText));

        // Assert
        var buffer = emulator.GetBuffer();
        var cell = buffer[0, 0]; // 'C'

        Assert.True(cell.Attributes.HasAttribute(CharacterAttributes.Bold), "Should have Bold");
        Assert.True(cell.Attributes.HasAttribute(CharacterAttributes.Underline), "Should have Underline");
        Assert.True(cell.Foreground.IsIndexed, "Should have indexed color");
        Assert.Equal(2, cell.Foreground.Index); // Green = ANSI color 2
    }

    [Fact]
    public void BackgroundColor_ShouldBeStoredInBuffer()
    {
        // Arrange
        var emulator = new VT100Emulator(80, 24);

        // Act - Send text with blue background
        var bgText = "\x1b[44mBLUE BG\x1b[0m";
        emulator.ProcessData(Encoding.UTF8.GetBytes(bgText));

        // Assert
        var buffer = emulator.GetBuffer();
        var cell = buffer[0, 0]; // 'B'

        Assert.True(cell.Background.IsIndexed, "Background should be indexed color");
        Assert.Equal(4, cell.Background.Index); // Blue = ANSI color 4
    }

    [Fact]
    public void Color256_ShouldBeStoredInBuffer()
    {
        // Arrange
        var emulator = new VT100Emulator(80, 24);

        // Act - Send 256-color text (color 196 = bright red)
        var color256Text = "\x1b[38;5;196mCOLOR256\x1b[0m";
        emulator.ProcessData(Encoding.UTF8.GetBytes(color256Text));

        // Assert
        var buffer = emulator.GetBuffer();
        var cell = buffer[0, 0]; // 'C'

        Assert.True(cell.Foreground.IsIndexed, "Foreground should be indexed color");
        Assert.Equal(196, cell.Foreground.Index);
    }

    [Fact]
    public void RgbColor_ShouldBeStoredInBuffer()
    {
        // Arrange
        var emulator = new VT100Emulator(80, 24);

        // Act - Send RGB color text (255, 128, 64 = orange)
        var rgbText = "\x1b[38;2;255;128;64mRGB\x1b[0m";
        emulator.ProcessData(Encoding.UTF8.GetBytes(rgbText));

        // Assert
        var buffer = emulator.GetBuffer();
        var cell = buffer[0, 0]; // 'R'

        Assert.True(cell.Foreground.IsRgb, "Foreground should be RGB color");
        var (r, g, b) = cell.Foreground.ToRgb();
        Assert.Equal(255, r);
        Assert.Equal(128, g);
        Assert.Equal(64, b);
    }

    [Fact]
    public void ReverseVideo_ShouldBeStoredInBuffer()
    {
        // Arrange
        var emulator = new VT100Emulator(80, 24);

        // Act - Send reverse video text
        var reverseText = "\x1b[7mREVERSE\x1b[0m";
        emulator.ProcessData(Encoding.UTF8.GetBytes(reverseText));

        // Assert
        var buffer = emulator.GetBuffer();
        var cell = buffer[0, 0]; // 'R'

        Assert.True(cell.Attributes.HasAttribute(CharacterAttributes.Reverse),
                   $"Cell should have Reverse attribute. Actual attributes: {cell.Attributes}");
    }

    [Fact]
    public void Reset_ShouldClearAttributesAndColors()
    {
        // Arrange
        var emulator = new VT100Emulator(80, 24);

        // Act - Set attributes, then reset
        var resetText = "\x1b[1;4;31mBOLD RED\x1b[0mNORMAL";
        emulator.ProcessData(Encoding.UTF8.GetBytes(resetText));

        // Assert
        var buffer = emulator.GetBuffer();
        var boldCell = buffer[0, 0]; // 'B' - should be bold red
        var normalCell = buffer[0, 8]; // 'N' - should be normal

        // Bold cell should have attributes
        Assert.True(boldCell.Attributes.HasAttribute(CharacterAttributes.Bold), "First cell should be bold");
        Assert.True(boldCell.Foreground.IsIndexed && boldCell.Foreground.Index == 1, "First cell should be red");

        // Normal cell should not have attributes
        Assert.False(normalCell.Attributes.HasAttribute(CharacterAttributes.Bold), "Second cell should not be bold");
        Assert.True(normalCell.Foreground.IsDefault, "Second cell should have default color");
    }
}

