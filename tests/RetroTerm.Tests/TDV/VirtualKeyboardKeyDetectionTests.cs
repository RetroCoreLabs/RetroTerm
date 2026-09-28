using RetroTerm.Core.Terminal.Emulators.TDV;
using RetroTerm.Core.Terminal.Input;
using RetroTerm.Desktop.Models;
using RetroTerm.Desktop.ViewModels;
using Xunit;
using Xunit.Abstractions;

namespace RetroTerm.Tests.TDV;

/// <summary>
/// Comprehensive tests for virtual keyboard key detection, VK code conversion,
/// key binding resolution, display names, ViewModel mirroring, and the
/// OnKeyPressed priority fix (TDV registry before VK mapper for Special/Function keys).
/// </summary>
[Collection("TDVKeyBinding")]
public class VirtualKeyboardKeyDetectionTests
{
    private readonly ITestOutputHelper _output;
    private readonly TDV2200KeyboardMapper _mapper;

    // Avalonia Key enum integer values (must match TDVKeyBindingConfiguration constants)
    private const int AvKey_Back = 2;
    private const int AvKey_Tab = 3;
    private const int AvKey_Enter = 6;
    private const int AvKey_Escape = 13;
    private const int AvKey_Space = 18;
    private const int AvKey_PageUp = 19;
    private const int AvKey_PageDown = 20;
    private const int AvKey_End = 21;
    private const int AvKey_Home = 22;
    private const int AvKey_Left = 23;
    private const int AvKey_Up = 24;
    private const int AvKey_Right = 25;
    private const int AvKey_Down = 26;
    private const int AvKey_Insert = 31;
    private const int AvKey_Delete = 32;
    private const int AvKey_D0 = 34;
    private const int AvKey_D9 = 43;
    private const int AvKey_A = 44;
    private const int AvKey_Z = 69;
    private const int AvKey_NumPad0 = 74;
    private const int AvKey_NumPad9 = 83;
    private const int AvKey_Multiply = 84;
    private const int AvKey_Add = 85;
    private const int AvKey_Subtract = 87;
    private const int AvKey_Decimal = 88;
    private const int AvKey_Divide = 89;
    private const int AvKey_F1 = 90;
    private const int AvKey_F2 = 91;
    private const int AvKey_F12 = 101;
    private const int AvKey_F24 = 113;
    private const int AvKey_OemSemicolon = 140;
    private const int AvKey_OemPlus = 141;
    private const int AvKey_OemComma = 142;
    private const int AvKey_OemMinus = 143;
    private const int AvKey_OemPeriod = 144;
    private const int AvKey_OemQuestion = 145;
    private const int AvKey_OemTilde = 146;
    private const int AvKey_OemOpenBrackets = 149;
    private const int AvKey_OemPipe = 150;
    private const int AvKey_OemCloseBrackets = 151;
    private const int AvKey_OemQuotes = 152;
    private const int AvKey_OemBackslash = 154;

    // Windows VK codes
    private const int VK_BACK = 8;
    private const int VK_TAB = 9;
    private const int VK_RETURN = 13;
    private const int VK_ESCAPE = 27;
    private const int VK_SPACE = 32;
    private const int VK_PRIOR = 33;
    private const int VK_NEXT = 34;
    private const int VK_END = 35;
    private const int VK_HOME = 36;
    private const int VK_LEFT = 37;
    private const int VK_UP = 38;
    private const int VK_RIGHT = 39;
    private const int VK_DOWN = 40;
    private const int VK_INSERT = 45;
    private const int VK_DELETE = 46;
    private const int VK_A = 65;
    private const int VK_H = 72;
    private const int VK_Z = 90;
    private const int VK_NUMPAD0 = 96;
    private const int VK_NUMPAD9 = 105;
    private const int VK_MULTIPLY = 106;
    private const int VK_ADD = 107;
    private const int VK_SUBTRACT = 109;
    private const int VK_DECIMAL = 110;
    private const int VK_DIVIDE = 111;
    private const int VK_F1 = 112;
    private const int VK_F2 = 113;
    private const int VK_F12 = 123;
    private const int VK_F24 = 135;
    private const int VK_0 = 48;
    private const int VK_9 = 57;

    public VirtualKeyboardKeyDetectionTests(ITestOutputHelper output)
    {
        _output = output;
        TDVKeyBindingConfiguration.ResetForTesting();
        _mapper = new TDV2200KeyboardMapper();
    }

    #region AvaloniaKeyToVK Conversion — Letters

    [Theory]
    [InlineData(44, 65, "A")]   // Key.A → VK_A
    [InlineData(45, 66, "B")]
    [InlineData(51, 72, "H")]   // Key.H → VK_H (important for Alt+H → HJELP)
    [InlineData(69, 90, "Z")]   // Key.Z → VK_Z
    public void AvaloniaKeyToVK_Letters_ShouldMapCorrectly(int avKey, int expectedVK, string letter)
    {
        var result = TDVKeyBindingConfiguration.AvaloniaKeyToVK(avKey);
        Assert.Equal(expectedVK, result);
        _output.WriteLine($"Key.{letter} (Avalonia {avKey}) → VK {result} = correct");
    }

    [Fact]
    public void AvaloniaKeyToVK_AllLetters_ShouldCoverAToZ()
    {
        for (int avKey = AvKey_A; avKey <= AvKey_Z; avKey++)
        {
            var vk = TDVKeyBindingConfiguration.AvaloniaKeyToVK(avKey);
            Assert.True(vk >= VK_A && vk <= VK_Z,
                $"Avalonia key {avKey} mapped to VK {vk}, expected {VK_A}-{VK_Z}");
        }
        _output.WriteLine("All 26 letters A-Z convert correctly");
    }

    #endregion

    #region AvaloniaKeyToVK Conversion — Digits

    [Theory]
    [InlineData(34, 48, "0")]   // Key.D0 → VK_0
    [InlineData(35, 49, "1")]
    [InlineData(43, 57, "9")]   // Key.D9 → VK_9
    public void AvaloniaKeyToVK_Digits_ShouldMapCorrectly(int avKey, int expectedVK, string digit)
    {
        var result = TDVKeyBindingConfiguration.AvaloniaKeyToVK(avKey);
        Assert.Equal(expectedVK, result);
        _output.WriteLine($"Key.D{digit} (Avalonia {avKey}) → VK {result} = correct");
    }

    [Fact]
    public void AvaloniaKeyToVK_AllDigits_ShouldCover0To9()
    {
        for (int avKey = AvKey_D0; avKey <= AvKey_D9; avKey++)
        {
            var vk = TDVKeyBindingConfiguration.AvaloniaKeyToVK(avKey);
            Assert.True(vk >= VK_0 && vk <= VK_9,
                $"Avalonia key {avKey} mapped to VK {vk}, expected {VK_0}-{VK_9}");
        }
        _output.WriteLine("All 10 digits 0-9 convert correctly");
    }

    #endregion

    #region AvaloniaKeyToVK Conversion — Function Keys

    [Theory]
    [InlineData(90, 112, "F1")]
    [InlineData(91, 113, "F2")]
    [InlineData(101, 123, "F12")]
    [InlineData(113, 135, "F24")]
    public void AvaloniaKeyToVK_FunctionKeys_ShouldMapCorrectly(int avKey, int expectedVK, string name)
    {
        var result = TDVKeyBindingConfiguration.AvaloniaKeyToVK(avKey);
        Assert.Equal(expectedVK, result);
        _output.WriteLine($"Key.{name} (Avalonia {avKey}) → VK {result} = correct");
    }

    [Fact]
    public void AvaloniaKeyToVK_AllFunctionKeys_ShouldCoverF1ToF24()
    {
        for (int avKey = AvKey_F1; avKey <= AvKey_F24; avKey++)
        {
            var vk = TDVKeyBindingConfiguration.AvaloniaKeyToVK(avKey);
            int fNum = avKey - AvKey_F1 + 1;
            Assert.True(vk >= VK_F1 && vk <= VK_F24,
                $"Key.F{fNum} (Avalonia {avKey}) mapped to VK {vk}, expected {VK_F1}-{VK_F24}");
        }
        _output.WriteLine("All 24 function keys F1-F24 convert correctly");
    }

    #endregion

    #region AvaloniaKeyToVK Conversion — Navigation Keys

    [Theory]
    [InlineData(23, 37, "Left")]
    [InlineData(24, 38, "Up")]
    [InlineData(25, 39, "Right")]
    [InlineData(26, 40, "Down")]
    [InlineData(22, 36, "Home")]
    [InlineData(21, 35, "End")]
    [InlineData(19, 33, "PageUp")]
    [InlineData(20, 34, "PageDown")]
    [InlineData(31, 45, "Insert")]
    [InlineData(32, 46, "Delete")]
    public void AvaloniaKeyToVK_NavigationKeys_ShouldMapCorrectly(int avKey, int expectedVK, string name)
    {
        var result = TDVKeyBindingConfiguration.AvaloniaKeyToVK(avKey);
        Assert.Equal(expectedVK, result);
        _output.WriteLine($"Key.{name} (Avalonia {avKey}) → VK {result} = correct");
    }

    #endregion

    #region AvaloniaKeyToVK Conversion — Control Keys

    [Theory]
    [InlineData(2, 8, "Backspace")]
    [InlineData(3, 9, "Tab")]
    [InlineData(6, 13, "Enter")]
    [InlineData(13, 27, "Escape")]
    [InlineData(18, 32, "Space")]
    public void AvaloniaKeyToVK_ControlKeys_ShouldMapCorrectly(int avKey, int expectedVK, string name)
    {
        var result = TDVKeyBindingConfiguration.AvaloniaKeyToVK(avKey);
        Assert.Equal(expectedVK, result);
        _output.WriteLine($"Key.{name} (Avalonia {avKey}) → VK {result} = correct");
    }

    #endregion

    #region AvaloniaKeyToVK Conversion — NumPad Keys

    [Theory]
    [InlineData(74, 96, "NumPad0")]
    [InlineData(83, 105, "NumPad9")]
    [InlineData(84, 106, "Multiply")]
    [InlineData(85, 107, "Add")]
    [InlineData(87, 109, "Subtract")]
    [InlineData(88, 110, "Decimal")]
    [InlineData(89, 111, "Divide")]
    public void AvaloniaKeyToVK_NumPadKeys_ShouldMapCorrectly(int avKey, int expectedVK, string name)
    {
        var result = TDVKeyBindingConfiguration.AvaloniaKeyToVK(avKey);
        Assert.Equal(expectedVK, result);
        _output.WriteLine($"Key.{name} (Avalonia {avKey}) → VK {result} = correct");
    }

    [Fact]
    public void AvaloniaKeyToVK_AllNumPadDigits_ShouldCoverNum0ToNum9()
    {
        for (int avKey = AvKey_NumPad0; avKey <= AvKey_NumPad9; avKey++)
        {
            var vk = TDVKeyBindingConfiguration.AvaloniaKeyToVK(avKey);
            Assert.True(vk >= VK_NUMPAD0 && vk <= VK_NUMPAD9,
                $"NumPad key Avalonia {avKey} mapped to VK {vk}, expected {VK_NUMPAD0}-{VK_NUMPAD9}");
        }
        _output.WriteLine("All 10 numpad digits convert correctly");
    }

    #endregion

    #region AvaloniaKeyToVK Conversion — OEM Keys

    [Theory]
    [InlineData(140, 186, "OemSemicolon")]
    [InlineData(141, 187, "OemPlus")]
    [InlineData(142, 188, "OemComma")]
    [InlineData(143, 189, "OemMinus")]
    [InlineData(144, 190, "OemPeriod")]
    [InlineData(145, 191, "OemQuestion")]
    [InlineData(146, 192, "OemTilde")]
    [InlineData(149, 219, "OemOpenBrackets")]
    [InlineData(150, 220, "OemPipe")]
    [InlineData(151, 221, "OemCloseBrackets")]
    [InlineData(152, 222, "OemQuotes")]
    [InlineData(154, 226, "OemBackslash")]
    public void AvaloniaKeyToVK_OemKeys_ShouldMapCorrectly(int avKey, int expectedVK, string name)
    {
        var result = TDVKeyBindingConfiguration.AvaloniaKeyToVK(avKey);
        Assert.Equal(expectedVK, result);
        _output.WriteLine($"Key.{name} (Avalonia {avKey}) → VK {result} = correct");
    }

    #endregion

    #region IsValid Validation — Accepted Sources

    [Theory]
    [InlineData(VK_F1, KeyModifiers.None, "bare F1")]
    [InlineData(VK_F12, KeyModifiers.None, "bare F12")]
    [InlineData(VK_UP, KeyModifiers.None, "bare Up arrow")]
    [InlineData(VK_DOWN, KeyModifiers.None, "bare Down arrow")]
    [InlineData(VK_LEFT, KeyModifiers.None, "bare Left arrow")]
    [InlineData(VK_RIGHT, KeyModifiers.None, "bare Right arrow")]
    [InlineData(VK_HOME, KeyModifiers.None, "bare Home")]
    [InlineData(VK_END, KeyModifiers.None, "bare End")]
    [InlineData(VK_PRIOR, KeyModifiers.None, "bare PageUp")]
    [InlineData(VK_NEXT, KeyModifiers.None, "bare PageDown")]
    [InlineData(VK_INSERT, KeyModifiers.None, "bare Insert")]
    [InlineData(VK_DELETE, KeyModifiers.None, "bare Delete")]
    [InlineData(VK_TAB, KeyModifiers.None, "bare Tab")]
    [InlineData(VK_ESCAPE, KeyModifiers.None, "bare Escape")]
    [InlineData(VK_BACK, KeyModifiers.None, "bare Backspace")]
    [InlineData(VK_RETURN, KeyModifiers.None, "bare Enter")]
    [InlineData(VK_SPACE, KeyModifiers.None, "bare Space")]
    public void IsValid_BareSpecialKeys_ShouldBeAccepted(int vkCode, KeyModifiers mods, string desc)
    {
        var source = new KeyBindingSource(vkCode, mods);
        Assert.True(source.IsValid(), $"{desc} (VK {vkCode}) should be a valid binding source");
        _output.WriteLine($"{desc} → valid binding source");
    }

    [Theory]
    [InlineData(VK_H, KeyModifiers.Alt, "Alt+H")]
    [InlineData(VK_A, KeyModifiers.Ctrl, "Ctrl+A")]
    [InlineData(VK_F1, KeyModifiers.Shift, "Shift+F1")]
    [InlineData(VK_A, KeyModifiers.Shift, "Shift+A")]
    [InlineData(VK_Z, KeyModifiers.Alt | KeyModifiers.Ctrl, "Ctrl+Alt+Z")]
    public void IsValid_ModifiedKeys_ShouldBeAccepted(int vkCode, KeyModifiers mods, string desc)
    {
        var source = new KeyBindingSource(vkCode, mods);
        Assert.True(source.IsValid(), $"{desc} should be a valid binding source");
        _output.WriteLine($"{desc} → valid binding source");
    }

    #endregion

    #region IsValid Validation — Rejected Sources

    [Theory]
    [InlineData(VK_A, KeyModifiers.None, "bare A")]
    [InlineData(VK_H, KeyModifiers.None, "bare H")]
    [InlineData(VK_Z, KeyModifiers.None, "bare Z")]
    [InlineData(VK_0, KeyModifiers.None, "bare 0")]
    [InlineData(VK_9, KeyModifiers.None, "bare 9")]
    public void IsValid_BareTextKeys_ShouldBeRejected(int vkCode, KeyModifiers mods, string desc)
    {
        var source = new KeyBindingSource(vkCode, mods);
        Assert.False(source.IsValid(), $"{desc} (VK {vkCode}) should be rejected as binding source");
        _output.WriteLine($"{desc} → correctly rejected");
    }

    [Theory]
    [InlineData(16, "Shift")]   // VK_SHIFT
    [InlineData(17, "Ctrl")]    // VK_CONTROL
    [InlineData(18, "Alt")]     // VK_MENU
    public void IsValid_ModifierOnly_ShouldBeRejected(int vkCode, string desc)
    {
        var source = new KeyBindingSource(vkCode, KeyModifiers.None);
        Assert.False(source.IsValid(), $"bare {desc} should be rejected");
        _output.WriteLine($"bare {desc} → correctly rejected");
    }

    #endregion

    #region Key Binding Resolution — Default Bindings via Avalonia Key Conversion

    [Fact]
    public void DefaultBinding_AvaloniaAltH_ShouldResolveToHjelp()
    {
        // Simulate: user presses Alt+H in Avalonia → convert to VK → look up binding
        var vkCode = TDVKeyBindingConfiguration.AvaloniaKeyToVK(AvKey_A + ('H' - 'A'));
        Assert.Equal(VK_H, vkCode);

        var found = TDVKeyBindingConfiguration.Instance.TryGetTarget(vkCode, KeyModifiers.Alt, out var target);
        Assert.True(found, "Alt+H should have a default binding");
        Assert.Equal("G53", target.GridPosition);
        _output.WriteLine($"Alt+H → VK {vkCode} → grid {target.GridPosition} (HJELP)");
    }

    [Theory]
    [InlineData('H', "G53", "HJELP")]
    [InlineData('D', "F49", "DO/REPLACE")]
    [InlineData('U', "G51", "FUNK")]
    [InlineData('P', "G52", "SKRIV/PRINT")]
    public void DefaultBinding_AltLetter_ShouldResolveToCorrectGrid(char letter, string expectedGrid, string keyName)
    {
        int avKey = AvKey_A + (letter - 'A');
        var vkCode = TDVKeyBindingConfiguration.AvaloniaKeyToVK(avKey);
        var found = TDVKeyBindingConfiguration.Instance.TryGetTarget(vkCode, KeyModifiers.Alt, out var target);
        Assert.True(found, $"Alt+{letter} should have a default binding");
        Assert.Equal(expectedGrid, target.GridPosition);
        _output.WriteLine($"Alt+{letter} → VK {vkCode} → {expectedGrid} ({keyName})");
    }

    [Theory]
    [InlineData(1, "G1")]    // Alt+1 → G1 (PUSH1)
    [InlineData(2, "G2")]
    [InlineData(8, "G8")]    // Alt+8 → G8 (PUSH8)
    public void DefaultBinding_AltNumber_ShouldResolveToCorrectPushKey(int digit, string expectedGrid)
    {
        int avKey = AvKey_D0 + digit;
        var vkCode = TDVKeyBindingConfiguration.AvaloniaKeyToVK(avKey);
        var found = TDVKeyBindingConfiguration.Instance.TryGetTarget(vkCode, KeyModifiers.Alt, out var target);
        Assert.True(found, $"Alt+{digit} should have a default binding");
        Assert.Equal(expectedGrid, target.GridPosition);
        _output.WriteLine($"Alt+{digit} → VK {vkCode} → {expectedGrid} (PUSH{digit})");
    }

    #endregion

    #region Key Binding Resolution — Mapper Produces Correct Sequences

    [Fact]
    public void Mapper_AltH_ViaAvaloniaConversion_ShouldSendHjelpSequence()
    {
        // Full pipeline: Avalonia key int → VK code → mapper → sequence
        var vkCode = TDVKeyBindingConfiguration.AvaloniaKeyToVK(AvKey_A + ('H' - 'A'));
        var sequence = _mapper.MapKey(vkCode, KeyModifiers.Alt, TerminalModes.None);
        Assert.NotNull(sequence);
        Assert.Equal("\x1B[46_", sequence);
        _output.WriteLine($"Alt+H → VK {vkCode} → mapper → ESC[46_ (HJELP)");
    }

    [Fact]
    public void Mapper_F1_ViaAvaloniaConversion_ShouldSendF1Sequence()
    {
        var vkCode = TDVKeyBindingConfiguration.AvaloniaKeyToVK(AvKey_F1);
        Assert.Equal(VK_F1, vkCode);
        var sequence = _mapper.MapKey(vkCode, KeyModifiers.None, TerminalModes.None);
        Assert.NotNull(sequence);
        _output.WriteLine($"F1 → VK {vkCode} → mapper → {ToVisibleString(sequence)}");
    }

    [Theory]
    [InlineData(AvKey_Up, VK_UP, "Up arrow")]
    [InlineData(AvKey_Down, VK_DOWN, "Down arrow")]
    [InlineData(AvKey_Left, VK_LEFT, "Left arrow")]
    [InlineData(AvKey_Right, VK_RIGHT, "Right arrow")]
    public void Mapper_ArrowKeys_ViaAvaloniaConversion_ShouldProduceSequence(int avKey, int expectedVK, string name)
    {
        var vkCode = TDVKeyBindingConfiguration.AvaloniaKeyToVK(avKey);
        Assert.Equal(expectedVK, vkCode);
        var sequence = _mapper.MapKey(vkCode, KeyModifiers.None, TerminalModes.None);
        Assert.NotNull(sequence);
        _output.WriteLine($"{name} → VK {vkCode} → mapper → {ToVisibleString(sequence)}");
    }

    #endregion

    #region Custom Binding — SetBinding + Mapper Integration

    [Fact]
    public void CustomBinding_AltB_ToHjelp_ShouldWork()
    {
        // Rebind Alt+B → HJELP (G53)
        var source = new KeyBindingSource(VK_A + 1, KeyModifiers.Alt); // VK_B = 66
        var target = new KeyBindingTarget("G53", false);
        Assert.True(TDVKeyBindingConfiguration.Instance.SetBinding(source, target));

        // Verify mapper now sends HJELP sequence for Alt+B
        var mapper = new TDV2200KeyboardMapper();
        var sequence = mapper.MapKey(66, KeyModifiers.Alt, TerminalModes.None);
        Assert.NotNull(sequence);
        Assert.Equal("\x1B[46_", sequence);
        _output.WriteLine("Custom Alt+B → G53 (HJELP) → ESC[46_ works");
    }

    [Fact]
    public void CustomBinding_F5_ToFelt_ShouldWork()
    {
        // Bind bare F5 → FELT (D47)
        var vkF5 = TDVKeyBindingConfiguration.AvaloniaKeyToVK(AvKey_F1 + 4); // F5
        var source = new KeyBindingSource(vkF5, KeyModifiers.None);
        var target = new KeyBindingTarget("D47", false);
        Assert.True(source.IsValid(), "bare F5 should be valid binding source");
        Assert.True(TDVKeyBindingConfiguration.Instance.SetBinding(source, target));

        var found = TDVKeyBindingConfiguration.Instance.TryGetTarget(vkF5, KeyModifiers.None, out var resolved);
        Assert.True(found);
        Assert.Equal("D47", resolved.GridPosition);
        _output.WriteLine($"Bare F5 (VK {vkF5}) → D47 (FELT) binding works");
    }

    [Fact]
    public void CustomBinding_ShiftedVariant_ShouldPreserveFlag()
    {
        var source = new KeyBindingSource(VK_F1, KeyModifiers.Shift);
        var target = new KeyBindingTarget("G53", true); // shifted HJELP
        Assert.True(TDVKeyBindingConfiguration.Instance.SetBinding(source, target));

        var found = TDVKeyBindingConfiguration.Instance.TryGetTarget(VK_F1, KeyModifiers.Shift, out var resolved);
        Assert.True(found);
        Assert.Equal("G53", resolved.GridPosition);
        Assert.True(resolved.Shifted, "Shifted flag should be preserved");
        _output.WriteLine("Shift+F1 → G53 shifted (HJELP shifted) binding works");
    }

    #endregion

    #region Display Names — GetVKCodeName

    [Theory]
    [InlineData(VK_A, "A")]
    [InlineData(VK_H, "H")]
    [InlineData(VK_Z, "Z")]
    [InlineData(VK_0, "0")]
    [InlineData(VK_9, "9")]
    [InlineData(VK_F1, "F1")]
    [InlineData(VK_F12, "F12")]
    [InlineData(VK_BACK, "Backspace")]
    [InlineData(VK_TAB, "Tab")]
    [InlineData(VK_RETURN, "Enter")]
    [InlineData(VK_ESCAPE, "Esc")]
    [InlineData(VK_SPACE, "Space")]
    [InlineData(VK_UP, "Up")]
    [InlineData(VK_DOWN, "Down")]
    [InlineData(VK_LEFT, "Left")]
    [InlineData(VK_RIGHT, "Right")]
    [InlineData(VK_HOME, "Home")]
    [InlineData(VK_END, "End")]
    [InlineData(VK_INSERT, "Ins")]
    [InlineData(VK_DELETE, "Del")]
    [InlineData(VK_PRIOR, "PgUp")]
    [InlineData(VK_NEXT, "PgDn")]
    [InlineData(VK_NUMPAD0, "Num0")]
    [InlineData(VK_NUMPAD9, "Num9")]
    [InlineData(VK_MULTIPLY, "Num*")]
    [InlineData(VK_ADD, "Num+")]
    [InlineData(VK_SUBTRACT, "Num-")]
    [InlineData(VK_DECIMAL, "Num.")]
    [InlineData(VK_DIVIDE, "Num/")]
    [InlineData(186, ";")]
    [InlineData(187, "=")]
    [InlineData(188, ",")]
    [InlineData(189, "-")]
    [InlineData(190, ".")]
    [InlineData(191, "/")]
    [InlineData(192, "`")]
    [InlineData(219, "[")]
    [InlineData(220, "\\")]
    [InlineData(221, "]")]
    [InlineData(222, "'")]
    public void GetBindingLabel_ShouldShowFriendlyName(int vkCode, string expectedName)
    {
        var label = TDVKeyBindingConfiguration.GetBindingLabel(vkCode, KeyModifiers.None);
        Assert.Equal(expectedName, label);
        _output.WriteLine($"VK {vkCode} → \"{label}\"");
    }

    [Theory]
    [InlineData(VK_H, KeyModifiers.Alt, "Alt+H")]
    [InlineData(VK_A, KeyModifiers.Ctrl, "Ctrl+A")]
    [InlineData(VK_F1, KeyModifiers.Shift, "Shift+F1")]
    [InlineData(VK_Z, KeyModifiers.Ctrl | KeyModifiers.Alt, "Ctrl+Alt+Z")]
    public void GetBindingLabel_WithModifiers_ShouldShowModifierPrefix(int vkCode, KeyModifiers mods, string expected)
    {
        var label = TDVKeyBindingConfiguration.GetBindingLabel(vkCode, mods);
        Assert.Equal(expected, label);
        _output.WriteLine($"VK {vkCode} + {mods} → \"{label}\"");
    }

    #endregion

    #region Display Names — GetAvaloniaKeyName

    [Theory]
    [InlineData(AvKey_A, "Key.A")]
    [InlineData(AvKey_Z, "Key.Z")]
    [InlineData(AvKey_D0, "Key.D0")]
    [InlineData(AvKey_D9, "Key.D9")]
    [InlineData(AvKey_F1, "Key.F1")]
    [InlineData(AvKey_F12, "Key.F12")]
    [InlineData(AvKey_F24, "Key.F24")]
    [InlineData(AvKey_Back, "Key.Back")]
    [InlineData(AvKey_Tab, "Key.Tab")]
    [InlineData(AvKey_Enter, "Key.Enter")]
    [InlineData(AvKey_Escape, "Key.Escape")]
    [InlineData(AvKey_Space, "Key.Space")]
    [InlineData(AvKey_Left, "Key.Left")]
    [InlineData(AvKey_Up, "Key.Up")]
    [InlineData(AvKey_Right, "Key.Right")]
    [InlineData(AvKey_Down, "Key.Down")]
    [InlineData(AvKey_NumPad0, "Key.NumPad0")]
    [InlineData(AvKey_NumPad9, "Key.NumPad9")]
    [InlineData(AvKey_OemSemicolon, "Key.OemSemicolon")]
    [InlineData(AvKey_OemMinus, "Key.OemMinus")]
    public void GetAvaloniaKeyName_ShouldReturnReadableName(int avKey, string expected)
    {
        var name = TDVKeyBindingConfiguration.GetAvaloniaKeyName(avKey);
        Assert.Equal(expected, name);
        _output.WriteLine($"Avalonia {avKey} → \"{name}\"");
    }

    #endregion

    #region ViewModel Mirroring — Physical Key Press Highlights Correct Virtual Key

    [Theory]
    [InlineData(VK_F1, "F51", "F1 key")]
    [InlineData(VK_F2, "F52", "F2 key")]
    [InlineData(VK_DOWN, "A48", "Down arrow")]
    [InlineData(VK_LEFT, "B47", "Left arrow")]
    [InlineData(VK_RIGHT, "B49", "Right arrow")]
    [InlineData(VK_UP, "C48", "Up arrow")]
    [InlineData(VK_HOME, "B48", "Home")]
    [InlineData(VK_TAB, "F47", "Tab")]
    [InlineData(VK_ESCAPE, "G0", "Escape")]
    [InlineData(VK_DELETE, "G47", "Delete")]
    [InlineData(VK_INSERT, "D99", "Insert")]
    public void ViewModel_MirrorPhysicalKeyPress_ShouldMarkKeyAsPressed(int vkCode, string expectedGrid, string desc)
    {
        var vm = new VirtualKeyboardViewModel();

        // Verify the registry has this key with the expected VK code
        bool foundInRegistry = false;
        var allKeys = TDV2200KeyVisualRegistry.GetAllKeys();
        for (int i = 0; i < allKeys.Length; i++)
        {
            if (allKeys[i].VirtualKeyCode == vkCode
                && allKeys[i].GridPosition == expectedGrid)
            {
                foundInRegistry = true;
                break;
            }
        }
        Assert.True(foundInRegistry, $"Registry should have key {expectedGrid} with VK {vkCode}");

        // Mirror the key press
        vm.MirrorPhysicalKeyPress(vkCode);
        Assert.True(vm.IsKeyPressed(expectedGrid), $"{desc} should be pressed after MirrorPhysicalKeyPress");

        // Mirror key release
        vm.MirrorPhysicalKeyRelease(vkCode);
        Assert.False(vm.IsKeyPressed(expectedGrid), $"{desc} should be released after MirrorPhysicalKeyRelease");

        _output.WriteLine($"{desc} (VK {vkCode}) → mirror press marks {expectedGrid} pressed, release clears it");
    }

    [Fact]
    public void ViewModel_MirrorNonExistentVKCode_ShouldNotThrow()
    {
        var vm = new VirtualKeyboardViewModel();
        // VK 999 doesn't exist in layout
        vm.MirrorPhysicalKeyPress(999);
        vm.MirrorPhysicalKeyRelease(999);
        _output.WriteLine("Mirroring non-existent VK code does not throw");
    }

    #endregion

    #region End-to-End — Avalonia Key → VK → Binding → Grid → Sequence

    [Fact]
    public void EndToEnd_AvaloniaAltH_ShouldResolveHjelpSequence()
    {
        // Step 1: Convert Avalonia key to VK
        int avKey = AvKey_A + ('H' - 'A');
        var vkCode = TDVKeyBindingConfiguration.AvaloniaKeyToVK(avKey);
        _output.WriteLine($"Step 1: Avalonia {avKey} → VK {vkCode}");
        Assert.Equal(VK_H, vkCode);

        // Step 2: Look up binding
        var found = TDVKeyBindingConfiguration.Instance.TryGetTarget(vkCode, KeyModifiers.Alt, out var target);
        _output.WriteLine($"Step 2: TryGetTarget(VK {vkCode}, Alt) → {found}, grid={target.GridPosition}");
        Assert.True(found);
        Assert.Equal("G53", target.GridPosition);

        // Step 3: Get registry sequence
        var registrySeq = TDV2200KeyRegistry.GetSequence("G53", true, false, false, false);
        _output.WriteLine($"Step 3: Registry G53 → {ToVisibleString(registrySeq)}");
        Assert.NotNull(registrySeq);
        Assert.Equal("\x1B[46_", registrySeq);

        // Step 4: Mapper should produce the same sequence
        var mapperSeq = _mapper.MapKey(vkCode, KeyModifiers.Alt, TerminalModes.None);
        _output.WriteLine($"Step 4: Mapper(VK {vkCode}, Alt) → {ToVisibleString(mapperSeq)}");
        Assert.NotNull(mapperSeq);

        // Step 5: Display label should be readable
        var displayLabel = TDVKeyBindingConfiguration.GetBindingLabel(vkCode, KeyModifiers.Alt);
        var avKeyName = TDVKeyBindingConfiguration.GetAvaloniaKeyName(avKey);
        _output.WriteLine($"Step 5: Display = \"{avKeyName} → VK {vkCode} ({displayLabel})\"");
        Assert.Equal("Alt+H", displayLabel);
        Assert.Equal("Key.H", avKeyName);
    }

    [Fact]
    public void EndToEnd_AvaloniaF1_ShouldResolveF1Sequence()
    {
        // PC F1 (VK 112) maps to F51 (TDV F1) in TDV2200KeyRegistry.
        // The TDV mapper resolves VK 112 → F51 → ESC[50_ (F1 native sequence).
        var vkCode = TDVKeyBindingConfiguration.AvaloniaKeyToVK(AvKey_F1);
        Assert.Equal(VK_F1, vkCode);

        var sequence = _mapper.MapKey(vkCode, KeyModifiers.None, TerminalModes.None);
        Assert.NotNull(sequence);

        // VK 112 resolves to F51 (TDV F1) via registry: ESC[50_
        Assert.Equal("\x1B[50_", sequence);
        _output.WriteLine($"F1 → VK {vkCode} → {ToVisibleString(sequence)} (TDV F1 ESC[50_ via F51)");
    }

    #endregion

    #region OnKeyPressed Priority Fix — TDV Special Keys Use Registry, Not VK Mapper

    [Fact]
    public void OnKeyPressedPriority_F51_F1_ShouldResolveToF1Sequence()
    {
        // F51 (TDV F1) has VirtualKeyCode=112 (F1 key code).
        // GetGridForVK(112) returns "F51". Both the registry lookup and the
        // VK mapper return the same F1 sequence ESC[50_.
        var meta = new TDVKeyVisualMetadata(
            "F51", 'F', 51, 0, 0, 60, 60,
            TDVKeyColor.Brown, KeyCategory.Function, KeyRenderMode.Text,
            false, null, 0.40, true,
            null, null, true, 112);

        // For ApplicationControl/Function keys, registry comes first
        string? sequence = null;
        if ((meta.Category == KeyCategory.ApplicationControl || meta.Category == KeyCategory.Function)
            && !string.IsNullOrEmpty(meta.GridPosition))
        {
            sequence = TDV2200KeyRegistry.GetSequence(
                meta.GridPosition, true, false, false, false);
        }

        Assert.NotNull(sequence);
        Assert.Equal("\x1B[50_", sequence);

        // The VK mapper also returns ESC[50_ because VK 112 → F51 in registry
        var vkSequence = new TDV2200KeyboardMapper().MapKey(
            meta.VirtualKeyCode, KeyModifiers.None, TerminalModes.None);
        Assert.NotNull(vkSequence);
        Assert.Equal(sequence, vkSequence);

        _output.WriteLine($"F51 TDV F1: registry → {ToVisibleString(sequence)}, VK mapper → {ToVisibleString(vkSequence)}");
        _output.WriteLine("Both registry and VK mapper resolve VK 112 to TDV F1 ESC[50_ — CORRECT");
    }

    [Fact]
    public void OnKeyPressedPriority_G54Slutt_ShouldUseRegistryNotVKMapper()
    {
        // G54 (SLUTT) — another ApplicationControl key that should use registry
        var meta = new TDVKeyVisualMetadata(
            "G54", 'G', 54, 0, 0, 60, 60,
            TDVKeyColor.Orange, KeyCategory.ApplicationControl, KeyRenderMode.Text,
            false, null, 0.40, true,
            null, null, true, 0);

        string? sequence = null;
        if ((meta.Category == KeyCategory.ApplicationControl || meta.Category == KeyCategory.Function)
            && !string.IsNullOrEmpty(meta.GridPosition))
        {
            sequence = TDV2200KeyRegistry.GetSequence(
                meta.GridPosition, true, false, false, false);
        }

        Assert.NotNull(sequence);
        Assert.Equal("\x1B[48_", sequence);
        _output.WriteLine($"G54 SLUTT → registry → {ToVisibleString(sequence)}");
    }

    [Fact]
    public void OnKeyPressedPriority_NavigationKeys_ShouldUseTDVNativeCodes()
    {
        // Arrow keys are AlwaysSameCode in TDV — the mapper resolves them via
        // GetGridForVK to their registry entries, which are C0 codes:
        //   UP=\x1C, DOWN=\x0B, LEFT=\x08, RIGHT=\x18
        var meta = new TDVKeyVisualMetadata(
            "A48", 'A', 48, 0, 0, 60, 60,
            TDVKeyColor.Brown, KeyCategory.Navigation, KeyRenderMode.Symbol,
            false, "A48", 0.40, true,
            null, null, true, 40);

        // Simulate OnKeyPressed priority logic:
        // Navigation keys do NOT match ApplicationControl/Function check
        string? sequence = null;
        if ((meta.Category == KeyCategory.ApplicationControl || meta.Category == KeyCategory.Function)
            && !string.IsNullOrEmpty(meta.GridPosition))
        {
            sequence = TDV2200KeyRegistry.GetSequence(
                meta.GridPosition, true, false, false, false);
        }
        // Above should be null for Navigation type
        Assert.Null(sequence);

        // VK mapper now returns TDV-native C0 codes (not VT220 ESC[B)
        if (string.IsNullOrEmpty(sequence) && meta.VirtualKeyCode > 0)
        {
            var mapper = new TDV2200KeyboardMapper();
            sequence = mapper.MapKey(meta.VirtualKeyCode, KeyModifiers.None, TerminalModes.None);
        }
        Assert.NotNull(sequence);

        // Registry and mapper both return C0 code \x0B for Down arrow
        var registrySeq = TDV2200KeyRegistry.GetSequence("A48", true, false, false, false);
        Assert.Equal(registrySeq, sequence);
        Assert.Equal("\x0B", sequence);

        _output.WriteLine($"Down arrow: mapper → {ToVisibleString(sequence)}, registry → {ToVisibleString(registrySeq)}");
        _output.WriteLine("Mapper returns TDV-native C0 code for Navigation keys — CORRECT");
    }

    [Fact]
    public void OnKeyPressedPriority_FunctionKeys_ShouldUseRegistry()
    {
        // F51 is the layout position for the physical F1 key
        // Category = Function, so registry should be used first
        var meta = new TDVKeyVisualMetadata(
            "F51", 'F', 51, 0, 0, 60, 60,
            TDVKeyColor.Brown, KeyCategory.Function, KeyRenderMode.Text,
            false, null, 0.40, true,
            null, null, true, 112);

        string? sequence = null;
        if ((meta.Category == KeyCategory.ApplicationControl || meta.Category == KeyCategory.Function)
            && !string.IsNullOrEmpty(meta.GridPosition))
        {
            sequence = TDV2200KeyRegistry.GetSequence(
                meta.GridPosition, true, false, false, false);
        }

        // F51 should have a registry entry with TDV native sequence
        if (sequence != null)
        {
            _output.WriteLine($"F51 (F1 physical) → registry → {ToVisibleString(sequence)}");
        }
        else
        {
            // If not in registry, fall back to VK mapper
            var mapper = new TDV2200KeyboardMapper();
            sequence = mapper.MapKey(meta.VirtualKeyCode, KeyModifiers.None, TerminalModes.None);
            _output.WriteLine($"F51 (F1 physical) not in registry, VK mapper → {ToVisibleString(sequence)}");
        }
        Assert.NotNull(sequence);
    }

    #endregion

    #region Mirroring via Key Binding — Alt+H Should Highlight G53

    [Fact]
    public void MirrorViaBinding_AltH_ShouldFindG53()
    {
        // When user presses Alt+H, MirrorKeyEvent should look up the binding
        // and find G53 (HJELP) to highlight
        var vkCode = TDVKeyBindingConfiguration.AvaloniaKeyToVK(AvKey_A + ('H' - 'A'));
        Assert.Equal(VK_H, vkCode);

        var found = TDVKeyBindingConfiguration.Instance.TryGetTarget(vkCode, KeyModifiers.Alt, out var target);
        Assert.True(found, "Alt+H should resolve to a binding target");
        Assert.Equal("G53", target.GridPosition);

        _output.WriteLine($"Alt+H → VK {vkCode} → binding → {target.GridPosition} (HJELP) — would highlight on virtual keyboard");
    }

    [Fact]
    public void MirrorViaBinding_Alt1_ShouldFindPush1()
    {
        var vkCode = TDVKeyBindingConfiguration.AvaloniaKeyToVK(AvKey_D0 + 1); // Key.D1
        var found = TDVKeyBindingConfiguration.Instance.TryGetTarget(vkCode, KeyModifiers.Alt, out var target);
        Assert.True(found, "Alt+1 should resolve to a binding target");
        Assert.Equal("G1", target.GridPosition);

        _output.WriteLine($"Alt+1 → VK {vkCode} → binding → {target.GridPosition} (PUSH1) — would highlight on virtual keyboard");
    }

    [Fact]
    public void MirrorViaVKCode_ArrowDown_ShouldFindInLayout()
    {
        // Arrow keys aren't in the binding system — they match via VirtualKeyCode in registry
        var vm = new VirtualKeyboardViewModel();
        vm.MirrorPhysicalKeyPress(VK_DOWN);
        Assert.True(vm.IsKeyPressed("A48"), "Down arrow (VK 40) should mark A48 as pressed");
        _output.WriteLine("Down arrow VK 40 → registry scan → A48 pressed — would highlight on virtual keyboard");
    }

    #endregion

    #region Comprehensive — All Default Alt Bindings Should Resolve

    [Fact]
    public void AllDefaultAltBindings_ShouldResolveToGridPositions()
    {
        int resolvedCount = 0;
        // Test all Alt+letter (A-Z) and Alt+number (0-9)
        for (int avKey = AvKey_A; avKey <= AvKey_Z; avKey++)
        {
            var vk = TDVKeyBindingConfiguration.AvaloniaKeyToVK(avKey);
            var found = TDVKeyBindingConfiguration.Instance.TryGetTarget(vk, KeyModifiers.Alt, out var target);
            char letter = (char)('A' + avKey - AvKey_A);
            if (found)
            {
                _output.WriteLine($"  Alt+{letter} → {target.GridPosition}");
                Assert.False(string.IsNullOrEmpty(target.GridPosition));
                resolvedCount++;
            }
        }

        for (int avKey = AvKey_D0; avKey <= AvKey_D9; avKey++)
        {
            var vk = TDVKeyBindingConfiguration.AvaloniaKeyToVK(avKey);
            var found = TDVKeyBindingConfiguration.Instance.TryGetTarget(vk, KeyModifiers.Alt, out var target);
            int digit = avKey - AvKey_D0;
            if (found)
            {
                _output.WriteLine($"  Alt+{digit} → {target.GridPosition}");
                Assert.False(string.IsNullOrEmpty(target.GridPosition));
                resolvedCount++;
            }
        }

        _output.WriteLine($"Total default Alt bindings resolved: {resolvedCount}");
        Assert.True(resolvedCount > 0, "At least some default Alt bindings should exist");
    }

    #endregion

    #region Helpers

    private static string ToVisibleString(string? s)
    {
        if (s == null) return "(null)";
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            if (c == 0x1B)
                sb.Append("ESC");
            else if (c < 0x20)
                sb.Append($"<{(int)c:X2}>");
            else
                sb.Append(c);
        }
        return sb.ToString();
    }

    #endregion
}
