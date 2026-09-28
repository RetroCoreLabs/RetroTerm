using RetroTerm.Core.Terminal.Buffer;

namespace RetroTerm.Tests.Terminal.Buffer;

public class TerminalColorTests
{
    [Fact]
    public void Default_Color_Should_Be_Default()
    {
        var color = TerminalColor.Default;

        Assert.True(color.IsDefault);
        Assert.False(color.IsIndexed);
        Assert.False(color.IsRgb);
    }

    [Fact]
    public void FromIndex_Should_Create_Indexed_Color()
    {
        var color = TerminalColor.FromIndex(42);

        Assert.True(color.IsIndexed);
        Assert.False(color.IsDefault);
        Assert.False(color.IsRgb);
        Assert.Equal(42, color.Index);
    }

    [Fact]
    public void FromRgb_Should_Create_RGB_Color()
    {
        var color = TerminalColor.FromRgb(255, 128, 64);

        Assert.True(color.IsRgb);
        Assert.False(color.IsDefault);
        Assert.False(color.IsIndexed);

        var (r, g, b) = color.ToRgb();
        Assert.Equal(255, r);
        Assert.Equal(128, g);
        Assert.Equal(64, b);
    }

    [Theory]
    [InlineData(StandardColors.Black, 0, 0, 0)]
    [InlineData(StandardColors.Red, 205, 0, 0)]
    [InlineData(StandardColors.Green, 0, 205, 0)]
    [InlineData(StandardColors.Blue, 0, 0, 238)]
    [InlineData(StandardColors.White, 229, 229, 229)]
    [InlineData(StandardColors.BrightRed, 255, 0, 0)]
    [InlineData(StandardColors.BrightWhite, 255, 255, 255)]
    public void Standard_Colors_Should_Convert_To_Correct_RGB(byte index, byte expectedR, byte expectedG, byte expectedB)
    {
        var color = TerminalColor.FromIndex(index);
        var (r, g, b) = color.ToRgb();

        Assert.Equal(expectedR, r);
        Assert.Equal(expectedG, g);
        Assert.Equal(expectedB, b);
    }

    [Fact]
    public void Color_Cube_Should_Convert_Correctly()
    {
        // Test a few colors from the 216 color cube (16-231)
        var color = TerminalColor.FromIndex(16); // First color in cube (0,0,0)
        var (r, g, b) = color.ToRgb();
        Assert.Equal(0, r);
        Assert.Equal(0, g);
        Assert.Equal(0, b);

        color = TerminalColor.FromIndex(231); // Last color in cube (5,5,5) = (255,255,255)
        (r, g, b) = color.ToRgb();
        Assert.Equal(255, r);
        Assert.Equal(255, g);
        Assert.Equal(255, b);
    }

    [Fact]
    public void Grayscale_Colors_Should_Convert_Correctly()
    {
        // Test grayscale range (232-255)
        var color = TerminalColor.FromIndex(232); // Darkest gray
        var (r, g, b) = color.ToRgb();
        Assert.Equal(8, r);
        Assert.Equal(8, g);
        Assert.Equal(8, b);

        color = TerminalColor.FromIndex(255); // Lightest gray
        (r, g, b) = color.ToRgb();
        Assert.Equal(238, r);
        Assert.Equal(238, g);
        Assert.Equal(238, b);
    }

    [Fact]
    public void Equality_Should_Work_Correctly()
    {
        var color1 = TerminalColor.FromIndex(42);
        var color2 = TerminalColor.FromIndex(42);
        var color3 = TerminalColor.FromIndex(43);

        Assert.Equal(color2, color1);
        Assert.NotEqual(color3, color1);

        var rgb1 = TerminalColor.FromRgb(100, 150, 200);
        var rgb2 = TerminalColor.FromRgb(100, 150, 200);
        var rgb3 = TerminalColor.FromRgb(100, 150, 201);

        Assert.Equal(rgb2, rgb1);
        Assert.NotEqual(rgb3, rgb1);

        Assert.Equal(TerminalColor.Default, TerminalColor.Default);
    }

    [Fact]
    public void ToString_Should_Return_Readable_Format()
    {
        Assert.Equal("Default", TerminalColor.Default.ToString());
        Assert.Equal("Index(42)", TerminalColor.FromIndex(42).ToString());
        Assert.Equal("RGB(100,150,200)", TerminalColor.FromRgb(100, 150, 200).ToString());
    }
}

