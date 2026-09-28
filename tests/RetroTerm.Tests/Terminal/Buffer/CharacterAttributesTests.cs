using RetroTerm.Core.Terminal.Buffer;

namespace RetroTerm.Tests.Terminal.Buffer;

public class CharacterAttributesTests
{
    [Fact]
    public void None_Should_Have_No_Attributes()
    {
        var attr = CharacterAttributes.None;

        Assert.False(attr.HasAttribute(CharacterAttributes.Bold));
        Assert.False(attr.HasAttribute(CharacterAttributes.Underline));
        Assert.False(attr.HasAttribute(CharacterAttributes.Blink));
    }

    [Fact]
    public void SetAttribute_Should_Add_Attribute()
    {
        var attr = CharacterAttributes.None;

        attr = attr.SetAttribute(CharacterAttributes.Bold);
        Assert.True(attr.HasAttribute(CharacterAttributes.Bold));

        attr = attr.SetAttribute(CharacterAttributes.Underline);
        Assert.True(attr.HasAttribute(CharacterAttributes.Bold));
        Assert.True(attr.HasAttribute(CharacterAttributes.Underline));
    }

    [Fact]
    public void ClearAttribute_Should_Remove_Attribute()
    {
        var attr = CharacterAttributes.Bold | CharacterAttributes.Underline | CharacterAttributes.Blink;

        attr = attr.ClearAttribute(CharacterAttributes.Underline);
        Assert.True(attr.HasAttribute(CharacterAttributes.Bold));
        Assert.False(attr.HasAttribute(CharacterAttributes.Underline));
        Assert.True(attr.HasAttribute(CharacterAttributes.Blink));
    }

    [Fact]
    public void ToggleAttribute_Should_Flip_Attribute()
    {
        var attr = CharacterAttributes.Bold;

        attr = attr.ToggleAttribute(CharacterAttributes.Bold);
        Assert.False(attr.HasAttribute(CharacterAttributes.Bold));

        attr = attr.ToggleAttribute(CharacterAttributes.Bold);
        Assert.True(attr.HasAttribute(CharacterAttributes.Bold));
    }

    [Fact]
    public void Multiple_Attributes_Can_Be_Combined()
    {
        var attr = CharacterAttributes.Bold | CharacterAttributes.Underline | CharacterAttributes.Reverse;

        Assert.True(attr.HasAttribute(CharacterAttributes.Bold));
        Assert.True(attr.HasAttribute(CharacterAttributes.Underline));
        Assert.True(attr.HasAttribute(CharacterAttributes.Reverse));
        Assert.False(attr.HasAttribute(CharacterAttributes.Blink));
    }

    [Theory]
    [InlineData(CharacterAttributes.DoubleWidth, true)]
    [InlineData(CharacterAttributes.DoubleHeightTop, true)]
    [InlineData(CharacterAttributes.DoubleHeightBottom, true)]
    [InlineData(CharacterAttributes.Bold, false)]
    [InlineData(CharacterAttributes.None, false)]
    public void IsDoubleSize_Should_Detect_Double_Size_Attributes(CharacterAttributes attr, bool expected)
    {
        Assert.Equal(expected, attr.IsDoubleSize());
    }

    [Fact]
    public void Combined_Double_Size_Should_Be_Detected()
    {
        var attr = CharacterAttributes.DoubleWidth | CharacterAttributes.DoubleHeightTop;
        Assert.True(attr.IsDoubleSize());
    }
}

