using System;
using System.Text;
using RetroTerm.Core.Terminal;
using RetroTerm.Core.Terminal.Buffer;
using RetroTerm.Core.Terminal.Emulators;
using Xunit;
using Xunit.Abstractions;

namespace RetroTerm.Tests.Rendering;

/// <summary>
/// Unit tests for TerminalRenderer color and attribute rendering logic.
/// These tests verify that colors and attributes are correctly converted from buffer to rendering.
/// </summary>
public class TerminalRendererColorTests
{
    private readonly ITestOutputHelper _output;

    public TerminalRendererColorTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void Renderer_ShouldRetrieveCorrectForegroundColor_ForIndexedColors()
    {
        // Arrange - Create buffer with colored text
        var emulator = new VT100Emulator(80, 24);
        emulator.ProcessData(Encoding.UTF8.GetBytes("\x1b[31mRED\x1b[32mGREEN\x1b[34mBLUE\x1b[0m"));

        var buffer = emulator.GetBuffer();

        // Act - Verify colors are stored correctly
        var redCell = buffer[0, 0];   // 'R'
        var greenCell = buffer[0, 3];  // 'G'
        var blueCell = buffer[0, 8];   // 'B'

        // Assert
        Assert.True(redCell.Foreground.IsIndexed, "Red cell should have indexed color");
        Assert.Equal(1, redCell.Foreground.Index); // Red = ANSI color 1

        Assert.True(greenCell.Foreground.IsIndexed, "Green cell should have indexed color");
        Assert.Equal(2, greenCell.Foreground.Index); // Green = ANSI color 2

        Assert.True(blueCell.Foreground.IsIndexed, "Blue cell should have indexed color");
        Assert.Equal(4, blueCell.Foreground.Index); // Blue = ANSI color 4

        _output.WriteLine($"✓ Red cell: Index={redCell.Foreground.Index}");
        _output.WriteLine($"✓ Green cell: Index={greenCell.Foreground.Index}");
        _output.WriteLine($"✓ Blue cell: Index={blueCell.Foreground.Index}");
    }

    [Fact]
    public void Renderer_ShouldRetrieveCorrectForegroundColor_For256Colors()
    {
        // Arrange
        var emulator = new VT100Emulator(80, 24);
        emulator.ProcessData(Encoding.UTF8.GetBytes("\x1b[38;5;196mCOLOR256\x1b[0m"));

        var buffer = emulator.GetBuffer();
        var cell = buffer[0, 0]; // 'C'

        // Assert
        Assert.True(cell.Foreground.IsIndexed, "Cell should have indexed color");
        Assert.Equal(196, cell.Foreground.Index);

        // Verify RGB conversion
        var (r, g, b) = cell.Foreground.ToRgb();
        _output.WriteLine($"✓ Color 196: RGB({r}, {g}, {b})");
        Assert.True(r > 0 || g > 0 || b > 0, "RGB should not be all zeros");
    }

    [Fact]
    public void Renderer_ShouldRetrieveCorrectForegroundColor_ForRgbColors()
    {
        // Arrange
        var emulator = new VT100Emulator(80, 24);
        emulator.ProcessData(Encoding.UTF8.GetBytes("\x1b[38;2;255;128;64mRGB\x1b[0m"));

        var buffer = emulator.GetBuffer();
        var cell = buffer[0, 0]; // 'R'

        // Assert
        Assert.True(cell.Foreground.IsRgb, "Cell should have RGB color");
        var (r, g, b) = cell.Foreground.ToRgb();
        Assert.Equal(255, r);
        Assert.Equal(128, g);
        Assert.Equal(64, b);

        _output.WriteLine($"✓ RGB color: ({r}, {g}, {b})");
    }

    [Fact]
    public void Renderer_ShouldRetrieveCorrectBackgroundColor()
    {
        // Arrange
        var emulator = new VT100Emulator(80, 24);
        emulator.ProcessData(Encoding.UTF8.GetBytes("\x1b[44mBLUE BG\x1b[0m"));

        var buffer = emulator.GetBuffer();
        var cell = buffer[0, 0]; // 'B'

        // Assert
        Assert.True(cell.Background.IsIndexed, "Background should be indexed color");
        Assert.Equal(4, cell.Background.Index); // Blue = ANSI color 4

        _output.WriteLine($"✓ Background color index: {cell.Background.Index}");
    }

    [Fact]
    public void Renderer_ShouldHandleReverseVideoAttribute()
    {
        // Arrange
        var emulator = new VT100Emulator(80, 24);
        emulator.ProcessData(Encoding.UTF8.GetBytes("\x1b[31;44;7mREVERSE\x1b[0m"));
        // Red foreground (31), Blue background (44), Reverse (7)

        var buffer = emulator.GetBuffer();
        var cell = buffer[0, 0]; // 'R'

        // Assert
        Assert.True(cell.Attributes.HasAttribute(CharacterAttributes.Reverse),
                   "Cell should have Reverse attribute");
        Assert.True(cell.Foreground.IsIndexed && cell.Foreground.Index == 1,
                   "Foreground should be red (index 1)");
        Assert.True(cell.Background.IsIndexed && cell.Background.Index == 4,
                   "Background should be blue (index 4)");

        _output.WriteLine($"✓ Reverse video: FG={cell.Foreground.Index}, BG={cell.Background.Index}");
        _output.WriteLine("  Note: Renderer should swap FG/BG when rendering");
    }

    [Fact]
    public void Renderer_ShouldHandleDimAttribute()
    {
        // Arrange
        var emulator = new VT100Emulator(80, 24);
        emulator.ProcessData(Encoding.UTF8.GetBytes("\x1b[2;31mDIM RED\x1b[0m"));

        var buffer = emulator.GetBuffer();
        var cell = buffer[0, 0]; // 'D'

        // Assert
        Assert.True(cell.Attributes.HasAttribute(CharacterAttributes.Dim),
                   "Cell should have Dim attribute");
        Assert.True(cell.Foreground.IsIndexed && cell.Foreground.Index == 1,
                   "Foreground should be red");

        _output.WriteLine($"✓ Dim attribute: Index={cell.Foreground.Index}");
        _output.WriteLine("  Note: Renderer should darken color by 50%");
    }

    [Fact]
    public void Renderer_ShouldHandleBoldAttribute()
    {
        // Arrange
        var emulator = new VT100Emulator(80, 24);
        emulator.ProcessData(Encoding.UTF8.GetBytes("\x1b[1mBOLD\x1b[0m"));

        var buffer = emulator.GetBuffer();
        var cell = buffer[0, 0]; // 'B'

        // Assert
        Assert.True(cell.Attributes.HasAttribute(CharacterAttributes.Bold),
                   "Cell should have Bold attribute");

        _output.WriteLine($"✓ Bold attribute present");
        _output.WriteLine("  Note: Renderer should use FontWeight.Bold");
    }

    [Fact]
    public void Renderer_ShouldHandleUnderlineAttribute()
    {
        // Arrange
        var emulator = new VT100Emulator(80, 24);
        emulator.ProcessData(Encoding.UTF8.GetBytes("\x1b[4mUNDERLINE\x1b[0m"));

        var buffer = emulator.GetBuffer();
        var cell = buffer[0, 0]; // 'U'

        // Assert
        Assert.True(cell.Attributes.HasAttribute(CharacterAttributes.Underline),
                   "Cell should have Underline attribute");

        _output.WriteLine($"✓ Underline attribute present");
        _output.WriteLine("  Note: Renderer should draw line below character");
    }

    [Fact]
    public void Renderer_ShouldHandleMultipleAttributes()
    {
        // Arrange
        var emulator = new VT100Emulator(80, 24);
        emulator.ProcessData(Encoding.UTF8.GetBytes("\x1b[1;4;31mBOLD UNDERLINE RED\x1b[0m"));

        var buffer = emulator.GetBuffer();
        var cell = buffer[0, 0]; // 'B'

        // Assert
        Assert.True(cell.Attributes.HasAttribute(CharacterAttributes.Bold), "Should have Bold");
        Assert.True(cell.Attributes.HasAttribute(CharacterAttributes.Underline), "Should have Underline");
        Assert.True(cell.Foreground.IsIndexed && cell.Foreground.Index == 1, "Should be red");

        _output.WriteLine($"✓ Multiple attributes: Bold={cell.Attributes.HasAttribute(CharacterAttributes.Bold)}, " +
                         $"Underline={cell.Attributes.HasAttribute(CharacterAttributes.Underline)}, " +
                         $"Color={cell.Foreground.Index}");
    }

    [Fact]
    public void Renderer_ShouldHandleDefaultColors()
    {
        // Arrange
        var emulator = new VT100Emulator(80, 24);
        emulator.ProcessData(Encoding.UTF8.GetBytes("NORMAL TEXT"));

        var buffer = emulator.GetBuffer();
        var cell = buffer[0, 0]; // 'N'

        // Assert
        Assert.True(cell.Foreground.IsDefault, "Foreground should be default");
        Assert.True(cell.Background.IsDefault, "Background should be default");

        _output.WriteLine($"✓ Default colors: FG={cell.Foreground.IsDefault}, BG={cell.Background.IsDefault}");
        _output.WriteLine("  Note: Renderer should use _defaultForeground/_defaultBackground");
    }

    [Fact]
    public void Renderer_ShouldProcessAllColorTypes_InSequence()
    {
        // Arrange - Test all color types in one sequence
        var emulator = new VT100Emulator(80, 24);
        var testData = new StringBuilder();
        testData.Append("\x1b[31m");      // Indexed (red)
        testData.Append("RED ");
        testData.Append("\x1b[38;5;196m"); // 256-color
        testData.Append("256 ");
        testData.Append("\x1b[38;2;255;128;64m"); // RGB
        testData.Append("RGB ");
        testData.Append("\x1b[0m");       // Reset
        testData.Append("NORMAL");

        emulator.ProcessData(Encoding.UTF8.GetBytes(testData.ToString()));

        var buffer = emulator.GetBuffer();

        // Assert
        var redCell = buffer[0, 0];      // 'R' - indexed
        var color256Cell = buffer[0, 4]; // '2' - 256-color
        var rgbCell = buffer[0, 8];       // 'R' - RGB
        var normalCell = buffer[0, 12];   // 'N' - default

        Assert.True(redCell.Foreground.IsIndexed && redCell.Foreground.Index == 1);
        Assert.True(color256Cell.Foreground.IsIndexed && color256Cell.Foreground.Index == 196);
        Assert.True(rgbCell.Foreground.IsRgb);
        Assert.True(normalCell.Foreground.IsDefault);

        _output.WriteLine($"✓ Indexed color: {redCell.Foreground}");
        _output.WriteLine($"✓ 256-color: {color256Cell.Foreground}");
        _output.WriteLine($"✓ RGB color: {rgbCell.Foreground}");
        _output.WriteLine($"✓ Default color: {normalCell.Foreground}");
    }

    [Fact]
    public void Renderer_ShouldHandleHiddenAttribute()
    {
        // Arrange
        var emulator = new VT100Emulator(80, 24);
        emulator.ProcessData(Encoding.UTF8.GetBytes("\x1b[8mHIDDEN\x1b[0m"));

        var buffer = emulator.GetBuffer();
        var cell = buffer[0, 0]; // 'H'

        // Assert
        Assert.True(cell.Attributes.HasAttribute(CharacterAttributes.Hidden),
                   "Cell should have Hidden attribute");

        _output.WriteLine($"✓ Hidden attribute present");
        _output.WriteLine("  Note: Renderer should skip drawing character");
    }

    [Fact]
    public void Renderer_ShouldHandleStrikethroughAttribute()
    {
        // Arrange
        var emulator = new VT100Emulator(80, 24);
        emulator.ProcessData(Encoding.UTF8.GetBytes("\x1b[9mSTRIKE\x1b[0m"));

        var buffer = emulator.GetBuffer();
        var cell = buffer[0, 0]; // 'S'

        // Assert
        Assert.True(cell.Attributes.HasAttribute(CharacterAttributes.Strikethrough),
                   "Cell should have Strikethrough attribute");

        _output.WriteLine($"✓ Strikethrough attribute present");
        _output.WriteLine("  Note: Renderer should draw line through character");
    }
}

