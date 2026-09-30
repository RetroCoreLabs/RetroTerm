using System.Collections.Generic;
using RetroTerm.Core.Terminal.Emulators.TDV;
using RetroTerm.Core.Terminal.Input;
using RetroTerm.Desktop.Models;
using Xunit;

namespace RetroTerm.Tests.TDV;

/// <summary>
/// Tests for VirtualKeyboardPanel.OnKeyPressed resolution priority.
/// Simulates the 4-step logic without requiring Avalonia UI:
/// 1. ApplicationControl/Function → TDV2200KeyRegistry.GetSequence
/// 2. VK code > 0 → TDV2200KeyboardMapper.MapKey
/// 3. Grid fallback → registry again
/// 4. Alphanumeric type → label lookup
/// </summary>
[Collection("TDVKeyBinding")]
public class VirtualKeyboardOnKeyPressedTests
{
    private readonly ITestOutputHelper _output;
    private static readonly TDV2200KeyboardMapper _mapper = new TDV2200KeyboardMapper();

    public VirtualKeyboardOnKeyPressedTests(ITestOutputHelper output)
    {
        _output = output;
        TDVKeyBindingConfiguration.ResetForTesting();
    }

    /// <summary>
    /// Replicates VirtualKeyboardPanel.OnKeyPressed logic.
    /// </summary>
    private static string? SimulateOnKeyPressed(TDVKeyVisualMetadata meta,
        Dictionary<string, KeyLabel> labels, KeyModifiers modifiers)
    {
        bool shift = modifiers.HasFlag(KeyModifiers.Shift);
        bool ctrl = modifiers.HasFlag(KeyModifiers.Ctrl);
        string? sequence = null;

        // Step 1: ApplicationControl/Function/Local → registry
        if ((meta.Category == KeyCategory.ApplicationControl
             || meta.Category == KeyCategory.Function
             || meta.Category == KeyCategory.Local)
            && !string.IsNullOrEmpty(meta.GridPosition))
        {
            sequence = TDV2200KeyRegistry.GetSequence(
                meta.GridPosition, true, false, shift, ctrl);
        }

        // Step 2: VK code → cached mapper.
        //
        // Ctrl on an Alphanumeric key skips the mapper, mirroring VirtualKeyboardPanel: on an
        // on-screen keyboard the LABEL is what the user clicked and what changes with the national
        // layout, so it decides which control code Ctrl+<letter> sends — see step 4. The mapper's
        // own Ctrl+letter rule serves the physical-keyboard paths, which have no label.
        bool ctrlOnLetterKey = ctrl && meta.Category == KeyCategory.Alphanumeric;

        if (string.IsNullOrEmpty(sequence) && meta.VirtualKeyCode > 0 && !ctrlOnLetterKey)
        {
            sequence = _mapper.MapKey(meta.VirtualKeyCode, modifiers, TerminalModes.None);
        }

        // Step 3: Grid fallback → registry
        if (string.IsNullOrEmpty(sequence) && !string.IsNullOrEmpty(meta.GridPosition))
        {
            sequence = TDV2200KeyRegistry.GetSequence(
                meta.GridPosition, true, false, shift, ctrl);
        }

        // Step 4: Alphanumeric → label (with Ctrl+letter → control code)
        if (string.IsNullOrEmpty(sequence) && meta.Category == KeyCategory.Alphanumeric)
        {
            if (labels != null && labels.TryGetValue("no", out var label))
            {
                var charLabel = shift && !string.IsNullOrEmpty(label.Shifted)
                    ? label.Shifted : label.Primary;
                if (!string.IsNullOrEmpty(charLabel) && charLabel.Length == 1)
                {
                    char ch = charLabel[0];

                    // Ctrl+letter → ASCII control code (Ctrl+A=0x01, ..., Ctrl+Z=0x1A)
                    if (ctrl && char.IsLetter(ch))
                    {
                        char upper = char.ToUpper(ch);
                        if (upper >= 'A' && upper <= 'Z')
                        {
                            sequence = ((char)(upper - '@')).ToString();
                        }
                    }
                    else
                    {
                        sequence = charLabel;
                    }
                }
            }
        }

        return sequence;
    }

    private static TDVKeyVisualMetadata MakeKey(string grid, KeyCategory category, int vk = 0)
    {
        char row = grid[0];
        int.TryParse(grid.Substring(1), out int col);
        return new TDVKeyVisualMetadata(
            grid, row, col, 0, 0, 60, 60,
            TDVKeyColor.White, category, KeyRenderMode.Text,
            false, null, 0.40, true,
            null, null, true, vk);
    }

    private static Dictionary<string, KeyLabel> MakeLabels(string primary, string? shifted = null)
    {
        return new Dictionary<string, KeyLabel>
        {
            ["no"] = new KeyLabel(primary ?? "X", shifted, null, null)
        };
    }

    private static string ToVisible(string? s)
    {
        if (s == null) return "(null)";
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            if (c == 0x1B) sb.Append("ESC");
            else if (c < 0x20) sb.Append($"<{(int)c:X2}>");
            else sb.Append(c);
        }
        return sb.ToString();
    }

    #region Priority — Special Keys Use Registry (Not VK Mapper)

    [Fact]
    public void Hjelp_G53_UsesRegistryNotF1Sequence()
    {
        var key = MakeKey("G53", KeyCategory.ApplicationControl, vk: 112);
        var labels = MakeLabels("HJELP");
        var seq = SimulateOnKeyPressed(key, labels, KeyModifiers.None);
        Assert.Equal("\x1B[46_", seq); // TDV HJELP, NOT VT220 F1
        _output.WriteLine($"HJELP (G53, VK=112) → {ToVisible(seq)} (registry, not F1)");
    }

    [Fact]
    public void Slutt_G54_UsesRegistry()
    {
        var key = MakeKey("G54", KeyCategory.ApplicationControl);
        var labels = MakeLabels("SLUTT");
        var seq = SimulateOnKeyPressed(key, labels, KeyModifiers.None);
        Assert.Equal("\x1B[48_", seq);
    }

    [Fact]
    public void Funk_G51_UsesRegistry()
    {
        var key = MakeKey("G51", KeyCategory.ApplicationControl);
        var labels = MakeLabels("FUNK");
        var seq = SimulateOnKeyPressed(key, labels, KeyModifiers.None);
        Assert.Equal("\x1B[42_", seq);
    }

    [Fact]
    public void Skriv_G52_UsesRegistry()
    {
        var key = MakeKey("G52", KeyCategory.ApplicationControl, vk: 44);
        var labels = MakeLabels("SKRIV");
        var seq = SimulateOnKeyPressed(key, labels, KeyModifiers.None);
        Assert.Equal("\x1B[44_", seq);
    }

    #endregion

    #region Priority — Special Keys Shifted Variants from Registry

    [Theory]
    [InlineData("G53", "HJELP", "\x1B[47_")]
    [InlineData("G54", "SLUTT", "\x1B[49_")]
    [InlineData("G51", "FUNK", "\x1B[43_")]
    [InlineData("G52", "SKRIV", "\x1B[45_")]
    [InlineData("G47", "STRYK", "\x1B[11_")]
    [InlineData("G48", "KOPI", "\x1B[13_")]
    [InlineData("G49", "FLYTT", "\x1B[15_")]
    [InlineData("G9", "MERK", "\x1B[01_")]
    [InlineData("G10", "FELT", "\x1B[03_")]
    [InlineData("G11", "AVSN", "\x1B[05_")]
    [InlineData("G12", "SETN", "\x1B[07_")]
    [InlineData("G13", "ORD", "\x1B[09_")]
    public void SpecialKeys_ShiftedVariant_FromRegistry(string grid, string name, string expectedShifted)
    {
        var category = (grid.StartsWith("G") && int.TryParse(grid.Substring(1), out int num) && num >= 9 && num <= 13)
            ? KeyCategory.Function : KeyCategory.ApplicationControl;
        var key = MakeKey(grid, category);
        var labels = MakeLabels(name);
        var seq = SimulateOnKeyPressed(key, labels, KeyModifiers.Shift);
        Assert.Equal(expectedShifted, seq);
        _output.WriteLine($"Shift+{name} ({grid}) → {ToVisible(seq)}");
    }

    #endregion

    #region Priority — Ctrl Variants (F2, F3 only)

    [Fact]
    public void F2_Ctrl_ReturnsCtrlVariant()
    {
        var key = MakeKey("F52", KeyCategory.Function, vk: 113);
        var labels = MakeLabels("F2");
        var seq = SimulateOnKeyPressed(key, labels, KeyModifiers.Ctrl);
        Assert.Equal("\x1B[54_", seq); // F2 Ctrl variant
        _output.WriteLine($"Ctrl+F2 → {ToVisible(seq)}");
    }

    [Fact]
    public void F3_Ctrl_ReturnsCtrlVariant()
    {
        var key = MakeKey("F53", KeyCategory.Function, vk: 114);
        var labels = MakeLabels("F3");
        var seq = SimulateOnKeyPressed(key, labels, KeyModifiers.Ctrl);
        Assert.Equal("\x1B[57_", seq); // F3 Ctrl variant
        _output.WriteLine($"Ctrl+F3 → {ToVisible(seq)}");
    }

    #endregion

    #region Priority — Navigation Keys Use Mapper → TDV C0 Codes

    [Theory]
    [InlineData("C48", 38, "\x1c", "Up arrow")]
    [InlineData("A48", 40, "\x0b", "Down arrow")]
    [InlineData("B47", 37, "\x08", "Left arrow")]
    [InlineData("B49", 39, "\x18", "Right arrow")]
    [InlineData("B48", 36, "\x1d", "Home")]
    public void NavigationKeys_UseMapper_SendC0Codes(string grid, int vk, string expected, string name)
    {
        var key = MakeKey(grid, KeyCategory.Navigation, vk: vk);
        var labels = MakeLabels("nav");
        var seq = SimulateOnKeyPressed(key, labels, KeyModifiers.None);
        Assert.Equal(expected, seq);
        _output.WriteLine($"{name} ({grid}) → {ToVisible(seq)} (TDV C0 via mapper)");
    }

    [Theory]
    [InlineData("C48", 38, "\x1c", "Shift+Up")]
    [InlineData("A48", 40, "\x0b", "Shift+Down")]
    [InlineData("B47", 37, "\x08", "Shift+Left")]
    [InlineData("B49", 39, "\x18", "Shift+Right")]
    public void NavigationKeys_Shifted_AlwaysSameCode(string grid, int vk, string expected, string name)
    {
        // TDV AlwaysSameCode: Shift doesn't change arrow sequences
        var key = MakeKey(grid, KeyCategory.Navigation, vk: vk);
        var labels = MakeLabels("nav");
        var seq = SimulateOnKeyPressed(key, labels, KeyModifiers.Shift);
        Assert.Equal(expected, seq);
        _output.WriteLine($"{name} ({grid}) → {ToVisible(seq)} (AlwaysSameCode)");
    }

    #endregion

    #region Priority — Normal Character Keys Use Labels

    [Fact]
    public void NormalKey_LetterA_SendsPrimaryLabel()
    {
        var key = MakeKey("C1", KeyCategory.Alphanumeric, vk: 65);
        var labels = MakeLabels("A", "a");
        var seq = SimulateOnKeyPressed(key, labels, KeyModifiers.None);
        Assert.Equal("A", seq);
    }

    [Fact]
    public void NormalKey_ShiftLetterA_SendsShiftedLabel()
    {
        var key = MakeKey("C1", KeyCategory.Alphanumeric, vk: 65);
        var labels = MakeLabels("A", "a");
        var seq = SimulateOnKeyPressed(key, labels, KeyModifiers.Shift);
        // Shift+A with VK 65 goes through mapper first (step 2), but mapper returns null for Shift+A
        // So falls through to step 4 (label), uses shifted label
        Assert.Equal("a", seq);
    }

    [Fact]
    public void NormalKey_NorwegianAE_SendsCharacter()
    {
        var key = MakeKey("C11", KeyCategory.Alphanumeric, vk: 0);
        var labels = MakeLabels("\u00C6", "\u00E6");
        var seq = SimulateOnKeyPressed(key, labels, KeyModifiers.None);
        Assert.Equal("\u00C6", seq);
    }

    [Fact]
    public void NormalKey_ShiftNorwegianAE_SendsShiftedCharacter()
    {
        var key = MakeKey("C11", KeyCategory.Alphanumeric, vk: 0);
        var labels = MakeLabels("\u00C6", "\u00E6");
        var seq = SimulateOnKeyPressed(key, labels, KeyModifiers.Shift);
        Assert.Equal("\u00E6", seq);
    }

    [Fact]
    public void NormalKey_Number1_SendsCharacter()
    {
        var key = MakeKey("E1", KeyCategory.Alphanumeric, vk: 49);
        var labels = MakeLabels("1", "!");
        // VK 49 goes through mapper step 2, but mapper has no entry for VK 49 (digit keys not mapped)
        // Falls to step 4
        var seq = SimulateOnKeyPressed(key, labels, KeyModifiers.None);
        Assert.Equal("1", seq);
    }

    [Fact]
    public void NormalKey_ShiftNumber1_SendsExclamation()
    {
        var key = MakeKey("E1", KeyCategory.Alphanumeric, vk: 49);
        var labels = MakeLabels("1", "!");
        var seq = SimulateOnKeyPressed(key, labels, KeyModifiers.Shift);
        Assert.Equal("!", seq);
    }

    #endregion

    #region Priority — System Keys Use Mapper

    [Fact]
    public void Enter_SystemKey_SendsCR()
    {
        var key = MakeKey("C13", KeyCategory.System, vk: 13);
        var labels = MakeLabels("RETURN");
        var seq = SimulateOnKeyPressed(key, labels, KeyModifiers.None);
        Assert.Equal("\r", seq);
    }

    [Fact]
    public void Tab_SystemKey_SendsTDVNative()
    {
        var key = MakeKey("F47", KeyCategory.System, vk: 9);
        var labels = MakeLabels("TAB");
        var seq = SimulateOnKeyPressed(key, labels, KeyModifiers.None);
        // F47 TAB: type is System so step 1 skips,
        // step 2 VK mapper resolves VK 9 → F47 → ESC[16_ (TDV-native)
        Assert.Equal("\x1B[16_", seq);
    }

    [Fact]
    public void Escape_SystemKey_SendsESC()
    {
        var key = MakeKey("G0", KeyCategory.System, vk: 27);
        var labels = MakeLabels("ESC");
        var seq = SimulateOnKeyPressed(key, labels, KeyModifiers.None);
        Assert.Equal("\x1b", seq);
    }

    [Fact]
    public void Backspace_SystemKey_SendsBS()
    {
        var key = MakeKey("E13", KeyCategory.System, vk: 8);
        var labels = MakeLabels("\u00B4");
        var seq = SimulateOnKeyPressed(key, labels, KeyModifiers.None);
        Assert.Equal("\x08", seq);
    }

    #endregion

    #region All Function Keys — Normal and Shifted from Registry

    [Theory]
    [InlineData("F47", "TAB", "\x1B[16_", "\x1B[17_")]
    [InlineData("F48", "SEARCH", "\x1B[18_", "\x1B[19_")]
    [InlineData("F49", "REPLACE", "\x1B[20_", "\x1B[21_")]
    [InlineData("F51", "F1", "\x1B[50_", "\x1B[51_")]
    [InlineData("F52", "F2", "\x1B[52_", "\x1B[53_")]
    [InlineData("F53", "F3", "\x1B[55_", "\x1B[56_")]
    [InlineData("F54", "F4", "\x1B[58_", "\x1B[59_")]
    [InlineData("E51", "F5", "\x1B[60_", "\x1B[61_")]
    [InlineData("E52", "F6", "\x1B[62_", "\x1B[63_")]
    [InlineData("E53", "F7", "\x1B[64_", "\x1B[65_")]
    [InlineData("E54", "F8", "\x1B[66_", "\x1B[67_")]
    public void FunctionKeys_ViaPanel_NormalAndShifted(string grid, string name,
        string expectedNormal, string expectedShifted)
    {
        var key = MakeKey(grid, KeyCategory.Function);
        var labels = MakeLabels(name);
        var normal = SimulateOnKeyPressed(key, labels, KeyModifiers.None);
        var shifted = SimulateOnKeyPressed(key, labels, KeyModifiers.Shift);

        Assert.Equal(expectedNormal, normal);
        Assert.Equal(expectedShifted, shifted);
        _output.WriteLine($"{grid} {name}: normal={ToVisible(normal)}, shift={ToVisible(shifted)}");
    }

    #endregion

    #region All Navigation Rectangle Keys — Normal and Shifted from Panel

    [Theory]
    [InlineData("D47", "ROLLUP", 0, "\x1B[28_", "\x1B[29_")]
    [InlineData("D48", "ANGRE", 0, "\x1B[30_", "\x1B[31_")]
    [InlineData("D49", "ROLLDN", 0, "\x1B[32_", "\x1B[33_")]
    [InlineData("C47", "FIELDLEFT", 0, "\x1B[34_", "\x1B[35_")]
    [InlineData("C49", "FIELDRIGHT", 0, "\x1B[36_", "\x1B[37_")]
    [InlineData("A47", "TABLEFT", 0, "\x1B[38_", "\x1B[39_")]
    [InlineData("A49", "TABRIGHT", 0, "\x1B[40_", "\x1B[41_")]
    [InlineData("E47", "GUILLEMETS", 0, "\x1B[22_", "\x1B[23_")]
    [InlineData("E48", "JUST", 0, "\x1B[24_", "\x1B[25_")]
    [InlineData("E49", "SINGLEGUILLEMETS", 0, "\x1B[26_", "\x1B[27_")]
    [InlineData("E13", "NEWPARA", 0, "\x1B[86_", "\x1B[87_")]
    [InlineData("C99", "MODE", 0, "\x1B[84_", "\x1B[85_")]
    [InlineData("D99", "INNS", 45, "\x1B[82_", "\x1B[83_")]
    public void NavigationRectKeys_NormalAndShifted_FromFallbackRegistry(
        string grid, string name, int vk, string expectedNormal, string expectedShifted)
    {
        // These keys go through step 3 (fallback registry) because they're not ApplicationControl/Function
        // and their VK codes either map to TDV-native or have no VK
        var key = MakeKey(grid, KeyCategory.ApplicationControl, vk: vk);
        var labels = MakeLabels(name);
        var normal = SimulateOnKeyPressed(key, labels, KeyModifiers.None);
        var shifted = SimulateOnKeyPressed(key, labels, KeyModifiers.Shift);

        Assert.Equal(expectedNormal, normal);
        Assert.Equal(expectedShifted, shifted);
        _output.WriteLine($"{grid} {name}: normal={ToVisible(normal)}, shift={ToVisible(shifted)}");
    }

    #endregion

    #region Ctrl+Letter → ASCII Control Codes

    [Theory]
    [InlineData("A", 0x01)]
    [InlineData("C", 0x03)]
    [InlineData("D", 0x04)]
    [InlineData("Z", 0x1A)]
    public void CtrlLetter_SendsControlCode(string letter, int expectedCode)
    {
        var key = MakeKey("C1", KeyCategory.Alphanumeric, vk: 65);
        var labels = MakeLabels(letter);
        var seq = SimulateOnKeyPressed(key, labels, KeyModifiers.Ctrl);
        Assert.Equal(((char)expectedCode).ToString(), seq);
        _output.WriteLine($"Ctrl+{letter} → 0x{expectedCode:X2}");
    }

    [Theory]
    [InlineData("a", 0x01)]
    [InlineData("c", 0x03)]
    [InlineData("z", 0x1A)]
    public void CtrlLetter_LowercaseLabel_StillSendsControlCode(string letter, int expectedCode)
    {
        var key = MakeKey("C1", KeyCategory.Alphanumeric, vk: 65);
        var labels = MakeLabels(letter);
        var seq = SimulateOnKeyPressed(key, labels, KeyModifiers.Ctrl);
        Assert.Equal(((char)expectedCode).ToString(), seq);
        _output.WriteLine($"Ctrl+{letter} (lowercase) → 0x{expectedCode:X2}");
    }

    [Fact]
    public void CtrlDigit_DoesNotSendControlCode()
    {
        var key = MakeKey("E1", KeyCategory.Alphanumeric, vk: 49);
        var labels = MakeLabels("1", "!");
        var seq = SimulateOnKeyPressed(key, labels, KeyModifiers.Ctrl);
        // Ctrl+digit: char '1' is not a letter, so no control code conversion
        Assert.Equal("1", seq);
    }

    [Fact]
    public void CtrlShiftLetter_UsesShiftedLabel_ThenControlCode()
    {
        // Shift+Ctrl on letter: shifted label "a" is still a letter → control code
        var key = MakeKey("C1", KeyCategory.Alphanumeric, vk: 65);
        var labels = MakeLabels("A", "a");
        var seq = SimulateOnKeyPressed(key, labels, KeyModifiers.Ctrl | KeyModifiers.Shift);
        // shift selects "a", ctrl converts it: 'A' - '@' = 0x01
        Assert.Equal("\x01", seq);
    }

    #endregion
}
