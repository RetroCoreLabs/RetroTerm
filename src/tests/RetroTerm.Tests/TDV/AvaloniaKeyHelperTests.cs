using global::Avalonia.Input;
using RetroTerm.Desktop.Helpers;
using Xunit;
using AvaloniaKeyMods = global::Avalonia.Input.KeyModifiers;
using CoreKeyMods = RetroTerm.Core.Terminal.Input.KeyModifiers;

namespace RetroTerm.Tests.TDV;

/// <summary>
/// Tests for AvaloniaKeyHelper static methods: VK code conversion,
/// modifier conversion, and special key detection.
/// </summary>
public class AvaloniaKeyHelperTests
{
    #region ToVKCodeForMapper — Letters

    [Theory]
    [InlineData(Key.A, 65)]
    [InlineData(Key.H, 72)]
    [InlineData(Key.Z, 90)]
    public void ToVKCodeForMapper_Letters_CorrectVK(Key key, int expectedVK)
    {
        var result = AvaloniaKeyHelper.ToVKCodeForMapper(key);
        Assert.Equal(expectedVK, result);
    }

    [Fact]
    public void ToVKCodeForMapper_AllLetters_Sequential65to90()
    {
        for (var key = Key.A; key <= Key.Z; key++)
        {
            var vk = AvaloniaKeyHelper.ToVKCodeForMapper(key);
            int expected = 65 + (key - Key.A);
            Assert.Equal(expected, vk);
        }
    }

    #endregion

    #region ToVKCodeForMapper — Digits

    [Theory]
    [InlineData(Key.D0, 48)]
    [InlineData(Key.D5, 53)]
    [InlineData(Key.D9, 57)]
    public void ToVKCodeForMapper_Digits_CorrectVK(Key key, int expectedVK)
    {
        var result = AvaloniaKeyHelper.ToVKCodeForMapper(key);
        Assert.Equal(expectedVK, result);
    }

    [Fact]
    public void ToVKCodeForMapper_AllDigits_Sequential48to57()
    {
        for (var key = Key.D0; key <= Key.D9; key++)
        {
            var vk = AvaloniaKeyHelper.ToVKCodeForMapper(key);
            int expected = 48 + (key - Key.D0);
            Assert.Equal(expected, vk);
        }
    }

    #endregion

    #region ToVKCodeForMapper — Special Keys (falls through to ToVKCode)

    [Theory]
    [InlineData(Key.F1, 112)]
    [InlineData(Key.F12, 123)]
    [InlineData(Key.Up, 38)]
    [InlineData(Key.Down, 40)]
    [InlineData(Key.Left, 37)]
    [InlineData(Key.Right, 39)]
    [InlineData(Key.Home, 36)]
    [InlineData(Key.End, 35)]
    [InlineData(Key.PageUp, 33)]
    [InlineData(Key.PageDown, 34)]
    [InlineData(Key.Insert, 45)]
    [InlineData(Key.Delete, 46)]
    [InlineData(Key.Enter, 13)]
    [InlineData(Key.Escape, 27)]
    [InlineData(Key.Tab, 9)]
    [InlineData(Key.Back, 8)]
    [InlineData(Key.Space, 32)]
    public void ToVKCodeForMapper_SpecialKeys_CorrectVK(Key key, int expectedVK)
    {
        var result = AvaloniaKeyHelper.ToVKCodeForMapper(key);
        Assert.Equal(expectedVK, result);
    }

    #endregion

    #region ToVKCode — Delegates to TDVKeyBindingConfiguration

    [Theory]
    [InlineData(Key.F1, 112)]
    [InlineData(Key.F24, 135)]
    [InlineData(Key.NumPad0, 96)]
    [InlineData(Key.NumPad9, 105)]
    [InlineData(Key.Multiply, 106)]
    [InlineData(Key.Add, 107)]
    [InlineData(Key.Subtract, 109)]
    [InlineData(Key.Decimal, 110)]
    [InlineData(Key.Divide, 111)]
    public void ToVKCode_SpecialAndNumPad_CorrectVK(Key key, int expectedVK)
    {
        var result = AvaloniaKeyHelper.ToVKCode(key);
        Assert.Equal(expectedVK, result);
    }

    #endregion

    #region ConvertModifiers

    [Fact]
    public void ConvertModifiers_None_ReturnsNone()
    {
        var result = AvaloniaKeyHelper.ConvertModifiers(AvaloniaKeyMods.None);
        Assert.Equal(CoreKeyMods.None, result);
    }

    [Fact]
    public void ConvertModifiers_Shift_ReturnsShift()
    {
        var result = AvaloniaKeyHelper.ConvertModifiers(AvaloniaKeyMods.Shift);
        Assert.True(result.HasFlag(CoreKeyMods.Shift));
    }

    [Fact]
    public void ConvertModifiers_Control_ReturnsCtrl()
    {
        var result = AvaloniaKeyHelper.ConvertModifiers(AvaloniaKeyMods.Control);
        Assert.True(result.HasFlag(CoreKeyMods.Ctrl));
    }

    [Fact]
    public void ConvertModifiers_Alt_ReturnsAlt()
    {
        var result = AvaloniaKeyHelper.ConvertModifiers(AvaloniaKeyMods.Alt);
        Assert.True(result.HasFlag(CoreKeyMods.Alt));
    }

    [Fact]
    public void ConvertModifiers_Meta_ReturnsMeta()
    {
        var result = AvaloniaKeyHelper.ConvertModifiers(AvaloniaKeyMods.Meta);
        Assert.True(result.HasFlag(CoreKeyMods.Meta));
    }

    [Fact]
    public void ConvertModifiers_ShiftCtrl_ReturnsBoth()
    {
        var input = AvaloniaKeyMods.Shift | AvaloniaKeyMods.Control;
        var result = AvaloniaKeyHelper.ConvertModifiers(input);
        Assert.True(result.HasFlag(CoreKeyMods.Shift));
        Assert.True(result.HasFlag(CoreKeyMods.Ctrl));
        Assert.False(result.HasFlag(CoreKeyMods.Alt));
    }

    [Fact]
    public void ConvertModifiers_AllFlags_ReturnsAll()
    {
        var input = AvaloniaKeyMods.Shift | AvaloniaKeyMods.Control
                  | AvaloniaKeyMods.Alt | AvaloniaKeyMods.Meta;
        var result = AvaloniaKeyHelper.ConvertModifiers(input);
        Assert.True(result.HasFlag(CoreKeyMods.Shift));
        Assert.True(result.HasFlag(CoreKeyMods.Ctrl));
        Assert.True(result.HasFlag(CoreKeyMods.Alt));
        Assert.True(result.HasFlag(CoreKeyMods.Meta));
    }

    #endregion

    #region IsSpecialKey

    [Theory]
    [InlineData(Key.Up)]
    [InlineData(Key.Down)]
    [InlineData(Key.Left)]
    [InlineData(Key.Right)]
    [InlineData(Key.Home)]
    [InlineData(Key.End)]
    [InlineData(Key.PageUp)]
    [InlineData(Key.PageDown)]
    [InlineData(Key.Insert)]
    [InlineData(Key.Delete)]
    [InlineData(Key.F1)]
    [InlineData(Key.F2)]
    [InlineData(Key.F3)]
    [InlineData(Key.F4)]
    [InlineData(Key.F5)]
    [InlineData(Key.F6)]
    [InlineData(Key.F7)]
    [InlineData(Key.F8)]
    [InlineData(Key.F9)]
    [InlineData(Key.F10)]
    [InlineData(Key.F11)]
    [InlineData(Key.F12)]
    [InlineData(Key.Tab)]
    [InlineData(Key.Escape)]
    [InlineData(Key.Back)]
    [InlineData(Key.Enter)]
    public void IsSpecialKey_SpecialKeys_ReturnsTrue(Key key)
    {
        Assert.True(AvaloniaKeyHelper.IsSpecialKey(key));
    }

    [Theory]
    [InlineData(Key.A)]
    [InlineData(Key.Z)]
    [InlineData(Key.D0)]
    [InlineData(Key.D9)]
    [InlineData(Key.Space)]
    [InlineData(Key.NumPad0)]
    [InlineData(Key.OemComma)]
    [InlineData(Key.LeftShift)]
    [InlineData(Key.LeftCtrl)]
    [InlineData(Key.LeftAlt)]
    [InlineData(Key.F13)]
    [InlineData(Key.F24)]
    public void IsSpecialKey_NonSpecialKeys_ReturnsFalse(Key key)
    {
        Assert.False(AvaloniaKeyHelper.IsSpecialKey(key));
    }

    #endregion
}
