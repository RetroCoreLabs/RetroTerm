using RetroTerm.Core.Terminal.Emulators.TDV;
using RetroTerm.Core.Terminal.Input;
using Xunit;
using Xunit.Abstractions;

namespace RetroTerm.Tests.TDV;

/// <summary>
/// Tests for TDV2200KeyboardMapper.MapKey 4-step resolution:
/// 1. User bindings → registry
/// 2. TDV2115 C0 codes
/// 3. All TDV keys → registry (Extended Control Mode ON)
/// 4. Backspace (PC-only key)
/// No VT220 fallback — keys without TDV equivalents return null.
/// Also tests PUSH key DCS generation, custom bindings, and ApplicationCursorKeys mode.
/// </summary>
[Collection("TDVKeyBinding")]
public class TDVMapperNavigationTests
{
    private readonly ITestOutputHelper _output;
    private readonly TDV2200KeyboardMapper _mapper;

    // VK codes
    private const int VK_PRIOR = 33;    // PageUp
    private const int VK_NEXT = 34;     // PageDown
    private const int VK_END = 35;
    private const int VK_HOME = 36;
    private const int VK_LEFT = 37;
    private const int VK_UP = 38;
    private const int VK_RIGHT = 39;
    private const int VK_DOWN = 40;
    private const int VK_INSERT = 45;
    private const int VK_DELETE = 46;
    private const int VK_F1 = 112;
    private const int VK_F12 = 123;

    public TDVMapperNavigationTests(ITestOutputHelper output)
    {
        _output = output;
        TDVKeyBindingConfiguration.ResetForTesting();
        _mapper = new TDV2200KeyboardMapper();
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

    #region Step 3 — Physical Nav Keys → TDV-Native via Registry

    [Fact]
    public void PageDown_SendsTDVNative_NotVT220()
    {
        var seq = _mapper.MapKey(VK_NEXT, KeyModifiers.None, TerminalModes.None);
        Assert.NotNull(seq);
        Assert.Equal("\x1B[32_", seq); // ROLLDN (D49) - spec section 6.3: RollDown is Page Down
        Assert.NotEqual("\x1b[6~", seq); // NOT VT220
        _output.WriteLine($"PageDown → {ToVisible(seq)} (TDV ROLLDN, not VT220)");
    }

    [Fact]
    public void PageUp_SendsTDVNative_NotVT220()
    {
        var seq = _mapper.MapKey(VK_PRIOR, KeyModifiers.None, TerminalModes.None);
        Assert.NotNull(seq);
        Assert.Equal("\x1B[28_", seq); // ROLLUP (D47) - spec section 6.3: RollUp is Page Up
        Assert.NotEqual("\x1b[5~", seq);
        _output.WriteLine($"PageUp → {ToVisible(seq)} (TDV ROLLUP)");
    }

    [Fact]
    public void Insert_SendsTDVNative_NotVT220()
    {
        var seq = _mapper.MapKey(VK_INSERT, KeyModifiers.None, TerminalModes.None);
        Assert.NotNull(seq);
        Assert.Equal("\x1B[82_", seq); // INNS (D99)
        Assert.NotEqual("\x1b[2~", seq);
        _output.WriteLine($"Insert → {ToVisible(seq)} (TDV INNS)");
    }

    [Fact]
    public void Delete_SendsTDVNative_NotVT220()
    {
        var seq = _mapper.MapKey(VK_DELETE, KeyModifiers.None, TerminalModes.None);
        Assert.NotNull(seq);
        Assert.Equal("\x1B[10_", seq); // STRYK (G47)
        Assert.NotEqual("\x1b[3~", seq);
        _output.WriteLine($"Delete → {ToVisible(seq)} (TDV STRYK)");
    }

    [Fact]
    public void ShiftPageDown_SendsTDVShiftedNative()
    {
        var seq = _mapper.MapKey(VK_NEXT, KeyModifiers.Shift, TerminalModes.None);
        Assert.NotNull(seq);
        Assert.Equal("\x1B[33_", seq); // ROLLDN shifted (D49)
        _output.WriteLine($"Shift+PageDown → {ToVisible(seq)}");
    }

    [Fact]
    public void ShiftPageUp_SendsTDVShiftedNative()
    {
        var seq = _mapper.MapKey(VK_PRIOR, KeyModifiers.Shift, TerminalModes.None);
        Assert.NotNull(seq);
        Assert.Equal("\x1B[29_", seq); // ROLLUP shifted (D47)
        _output.WriteLine($"Shift+PageUp → {ToVisible(seq)}");
    }

    [Fact]
    public void ShiftInsert_SendsTDVShiftedNative()
    {
        var seq = _mapper.MapKey(VK_INSERT, KeyModifiers.Shift, TerminalModes.None);
        Assert.NotNull(seq);
        Assert.Equal("\x1B[83_", seq); // INNS shifted (D99)
        _output.WriteLine($"Shift+Insert → {ToVisible(seq)}");
    }

    [Fact]
    public void ShiftDelete_SendsTDVShiftedNative()
    {
        var seq = _mapper.MapKey(VK_DELETE, KeyModifiers.Shift, TerminalModes.None);
        Assert.NotNull(seq);
        Assert.Equal("\x1B[11_", seq); // STRYK shifted (G47)
        _output.WriteLine($"Shift+Delete → {ToVisible(seq)}");
    }

    #endregion

    #region Step 3 Guard — Alt+Nav Skips Registry

    [Fact]
    public void AltPageDown_GoesToBindings_NotRegistry()
    {
        // Alt+PageDown has a default binding to D49 (ROLLDN) - spec section 6.3
        var seq = _mapper.MapKey(VK_NEXT, KeyModifiers.Alt, TerminalModes.None);
        Assert.NotNull(seq);
        // Step 1 binding: Alt+PageDown -> D49 -> ESC[32_
        Assert.Equal("\x1B[32_", seq);
        _output.WriteLine($"Alt+PageDown → {ToVisible(seq)} (via binding)");
    }

    [Fact]
    public void AltPageUp_GoesToBindings_NotRegistry()
    {
        var seq = _mapper.MapKey(VK_PRIOR, KeyModifiers.Alt, TerminalModes.None);
        Assert.NotNull(seq);
        Assert.Equal("\x1B[28_", seq); // Alt+PageUp → D47 ROLLUP
        _output.WriteLine($"Alt+PageUp → {ToVisible(seq)} (via binding)");
    }

    [Fact]
    public void AltDelete_GoesToBindings_NotRegistry()
    {
        var seq = _mapper.MapKey(VK_DELETE, KeyModifiers.Alt, TerminalModes.None);
        Assert.NotNull(seq);
        Assert.Equal("\x1B[10_", seq); // Alt+Delete → G47 STRYK
        _output.WriteLine($"Alt+Delete → {ToVisible(seq)} (via binding)");
    }

    #endregion

    #region Step 3 — Arrow Keys Send TDV C0 Codes (AlwaysSameCode from registry)

    [Theory]
    [InlineData(VK_UP, "\x1c")]       // FS
    [InlineData(VK_DOWN, "\x0b")]     // VT
    [InlineData(VK_RIGHT, "\x18")]    // CAN
    [InlineData(VK_LEFT, "\x08")]     // BS
    [InlineData(VK_HOME, "\x1d")]     // GS
    public void ArrowKeys_NormalMode_SendC0Codes(int vk, string expected)
    {
        var seq = _mapper.MapKey(vk, KeyModifiers.None, TerminalModes.None);
        Assert.Equal(expected, seq);
    }

    [Fact]
    public void EndKey_NormalMode_SendsSLUTT()
    {
        // End (VK 35) → G54 SLUTT
        var seq = _mapper.MapKey(VK_END, KeyModifiers.None, TerminalModes.None);
        Assert.Equal("\x1b[48_", seq);
    }

    [Theory]
    [InlineData(VK_UP, "\x1c")]
    [InlineData(VK_DOWN, "\x0b")]
    [InlineData(VK_RIGHT, "\x18")]
    [InlineData(VK_LEFT, "\x08")]
    [InlineData(VK_HOME, "\x1d")]
    public void ShiftArrowKeys_AlwaysSameCode_SendSameC0(int vk, string expected)
    {
        // TDV AlwaysSameCode: Shift doesn't change arrow sequences
        var seq = _mapper.MapKey(vk, KeyModifiers.Shift, TerminalModes.None);
        Assert.Equal(expected, seq);
        _output.WriteLine($"Shift+VK{vk} → {ToVisible(seq)} (same C0, AlwaysSameCode)");
    }

    [Fact]
    public void ShiftEnd_SendsShiftSLUTT()
    {
        // End (VK 35) + Shift → G54 SLUTT shifted
        var seq = _mapper.MapKey(VK_END, KeyModifiers.Shift, TerminalModes.None);
        Assert.Equal("\x1b[49_", seq);
    }

    [Theory]
    [InlineData(VK_UP, "\x1c")]
    [InlineData(VK_DOWN, "\x0b")]
    [InlineData(VK_RIGHT, "\x18")]
    [InlineData(VK_LEFT, "\x08")]
    public void CtrlArrowKeys_AlwaysSameCode_SendSameC0(int vk, string expected)
    {
        // TDV AlwaysSameCode: Ctrl doesn't change arrow sequences
        var seq = _mapper.MapKey(vk, KeyModifiers.Ctrl, TerminalModes.None);
        Assert.Equal(expected, seq);
    }

    [Theory]
    [InlineData(VK_UP, "\x1c")]
    [InlineData(VK_LEFT, "\x08")]
    public void ShiftCtrlArrowKeys_AlwaysSameCode_SendSameC0(int vk, string expected)
    {
        var seq = _mapper.MapKey(vk, KeyModifiers.Shift | KeyModifiers.Ctrl, TerminalModes.None);
        Assert.Equal(expected, seq);
    }

    #endregion

    #region Step 2 — TDV2115 Mode C0 Codes

    [Theory]
    [InlineData(VK_UP, "\x1c")]
    [InlineData(VK_DOWN, "\x0b")]
    [InlineData(VK_RIGHT, "\x18")]
    [InlineData(VK_LEFT, "\x08")]
    [InlineData(VK_HOME, "\x1d")]
    public void TDV2115Mode_ArrowKeys_SendC0Codes(int vk, string expected)
    {
        var seq = _mapper.MapKey(vk, KeyModifiers.None, TerminalModes.TDV2115Mode);
        Assert.Equal(expected, seq);
    }

    [Fact]
    public void TDV2115Mode_WithShift_StillSendsC0_AlwaysSameCode()
    {
        // Shift+arrow: step 2 (2115 mode) requires modifiers==None so skips,
        // but step 3 (registry) catches it → AlwaysSameCode returns same C0
        var seq = _mapper.MapKey(VK_UP, KeyModifiers.Shift, TerminalModes.TDV2115Mode);
        Assert.Equal("\x1c", seq);
        _output.WriteLine($"Shift+Up in 2115 mode → {ToVisible(seq)} (AlwaysSameCode C0)");
    }

    /// <summary>
    /// F1 DOES change in 2115 mode - it sends RS, not its extended sequence.
    /// </summary>
    /// <remarks>
    /// Named <c>TDV2115Mode_FKeys_NotAffected</c> until 2 September 2026, asserting the two modes
    /// were equal. It cited nothing and only described the mapper's behaviour.
    /// <c>spec\Keyboards\keyboard-spec.md</c> §6.8.3, from the User's Guide section 7.2, lists F51
    /// F1 as RS <c>0x1E</c> when Extended Control Mode is OFF.
    /// </remarks>
    [Fact]
    public void TDV2115Mode_FKeys_SendTheirC0Code()
    {
        var normal = _mapper.MapKey(VK_F1, KeyModifiers.None, TerminalModes.None);
        var mode2115 = _mapper.MapKey(VK_F1, KeyModifiers.None, TerminalModes.TDV2115Mode);

        Assert.NotEqual(normal, mode2115);
        Assert.Equal("\u001E", mode2115);
        _output.WriteLine($"F1 in 2115 mode -> {ToVisible(mode2115)} (RS, per keyboard-spec 6.8.3)");
    }

    #endregion

    #region Step 1 — User Bindings → Registry

    [Fact]
    public void AltH_DefaultBinding_SendsHjelpSequence()
    {
        var seq = _mapper.MapKey(72, KeyModifiers.Alt, TerminalModes.None); // VK_H
        Assert.NotNull(seq);
        Assert.Equal("\x1B[46_", seq); // HJELP (G53)
        _output.WriteLine($"Alt+H → {ToVisible(seq)} (HJELP)");
    }

    [Theory]
    [InlineData(72, "G53", "\x1B[46_")]   // Alt+H → HJELP
    [InlineData(68, "F49", "\x1B[20_")]   // Alt+D → REPLACE
    [InlineData(85, "G51", "\x1B[42_")]   // Alt+U → FUNK
    [InlineData(80, "G52", "\x1B[44_")]   // Alt+P → SKRIV
    [InlineData(88, "E47", "\x1B[22_")]   // Alt+X → GUILLEMETS
    [InlineData(8, "D48", "\x1B[30_")]    // Alt+Backspace → ANGRE
    [InlineData(77, "C99", "\x1B[84_")]   // Alt+M → MODE
    [InlineData(70, "F48", "\x1B[18_")]   // Alt+F → SEARCH
    [InlineData(83, "G54", "\x1B[48_")]   // Alt+S → SLUTT
    [InlineData(65, "G9", "\x1B[00_")]   // Alt+A → MERK
    [InlineData(76, "G10", "\x1B[02_")]   // Alt+L → FELT
    [InlineData(82, "G11", "\x1B[04_")]   // Alt+R → AVSN
    [InlineData(69, "G12", "\x1B[06_")]   // Alt+E → SETN
    [InlineData(87, "G13", "\x1B[08_")]   // Alt+W → ORD
    [InlineData(75, "G48", "\x1B[12_")]   // Alt+K → KOPI
    [InlineData(86, "G49", "\x1B[14_")]   // Alt+V → FLYTT
    [InlineData(74, "E48", "\x1B[24_")]   // Alt+J → JUST
    [InlineData(73, "E49", "\x1B[26_")]   // Alt+I → SINGLEGUILLEMETS
    public void AllDefaultAltBindings_ProduceCorrectTDVSequences(int vk, string grid, string expected)
    {
        var seq = _mapper.MapKey(vk, KeyModifiers.Alt, TerminalModes.None);
        Assert.NotNull(seq);
        Assert.Equal(expected, seq);
        _output.WriteLine($"Alt+VK{vk} → {grid} → {ToVisible(seq)}");
    }

    #endregion

    #region Step 1 — PUSH Key DCS Sequences

    [Theory]
    [InlineData(49, "G1", 1)]  // Alt+1 → PUSH1
    [InlineData(50, "G2", 2)]
    [InlineData(51, "G3", 3)]
    [InlineData(52, "G4", 4)]
    [InlineData(53, "G5", 5)]
    [InlineData(54, "G6", 6)]
    [InlineData(55, "G7", 7)]
    [InlineData(56, "G8", 8)]
    public void AltNumber_PushKeys_SendStoredString(int vk, string grid, int pushNum)
    {
        // Program the PUSH key, then verify it sends the stored string
        TDVPushKeyConfiguration.ResetForTesting();
        var testString = $"test{pushNum}";
        TDVPushKeyConfiguration.Instance.ProgramKey(pushNum, testString);

        var seq = _mapper.MapKey(vk, KeyModifiers.Alt, TerminalModes.None);
        Assert.NotNull(seq);
        Assert.Equal(testString, seq);
        _output.WriteLine($"Alt+{vk - 48} → {grid} → {ToVisible(seq)}");
    }

    [Fact]
    public void PushKey_ShiftedBinding_SendsStoredString()
    {
        // Program shifted PUSH key (P9 = Shift+G1)
        TDVPushKeyConfiguration.ResetForTesting();
        var testString = "shifted_push1";
        TDVPushKeyConfiguration.Instance.ProgramKey(9, testString); // P9 = Shift+P1

        // Set up a shifted PUSH key binding
        var source = new KeyBindingSource(VK_F1, KeyModifiers.Shift);
        var target = new KeyBindingTarget("G1", true); // shifted PUSH1 = P9
        TDVKeyBindingConfiguration.Instance.SetBinding(source, target);

        var seq = _mapper.MapKey(VK_F1, KeyModifiers.Shift, TerminalModes.None);
        Assert.NotNull(seq);
        Assert.Equal(testString, seq);
        _output.WriteLine($"Shift+F1 → G1 shifted → {ToVisible(seq)}");
    }

    #endregion

    #region F-keys → TDV-native via Registry

    [Theory]
    [InlineData(VK_F1, KeyModifiers.Shift, "\x1B[51_")]          // Shift+F1 → TDV F1 shifted (F51)
    [InlineData(VK_F1, KeyModifiers.Ctrl, "\x1B[50_")]           // Ctrl+F1 → TDV F1 (no ctrl variant, falls back to normal)
    [InlineData(113, KeyModifiers.Shift, "\x1B[53_")]             // Shift+F2 → F2 shifted (F52)
    [InlineData(113, KeyModifiers.Ctrl, "\x1B[54_")]              // Ctrl+F2 → F2 ctrl (F52)
    [InlineData(114, KeyModifiers.Shift, "\x1B[56_")]             // Shift+F3 → F3 shifted (F53)
    [InlineData(114, KeyModifiers.Ctrl, "\x1B[57_")]              // Ctrl+F3 → F3 ctrl (F53)
    public void FKeysWithModifiers_SendTDVNative(int vk, KeyModifiers mods, string expected)
    {
        var seq = _mapper.MapKey(vk, mods, TerminalModes.None);
        Assert.NotNull(seq);
        Assert.Equal(expected, seq);
        _output.WriteLine($"VK{vk}+{mods} → {ToVisible(seq)} (TDV-native)");
    }

    [Theory]
    [InlineData(120, KeyModifiers.Shift)]   // Shift+F9 → no TDV equivalent
    [InlineData(123, KeyModifiers.Ctrl)]    // Ctrl+F12 → no TDV equivalent
    public void FKeysWithModifiers_NoTDVEquivalent_ReturnNull(int vk, KeyModifiers mods)
    {
        var seq = _mapper.MapKey(vk, mods, TerminalModes.None);
        Assert.Null(seq);
    }

    [Fact]
    public void BareF1_SendsTDVF1_TDVNative()
    {
        // VK 112 → GetGridForVK → F51 (TDV F1) → ESC[50_
        var seq = _mapper.MapKey(VK_F1, KeyModifiers.None, TerminalModes.None);
        Assert.NotNull(seq);
        Assert.Equal("\x1B[50_", seq);
        _output.WriteLine($"Bare F1 → {ToVisible(seq)} (TDV F1, TDV-native)");
    }

    #endregion

    #region ApplicationCursorKeys Mode

    [Theory]
    [InlineData(VK_UP, "\x1c")]
    [InlineData(VK_DOWN, "\x0b")]
    [InlineData(VK_RIGHT, "\x18")]
    [InlineData(VK_LEFT, "\x08")]
    public void ApplicationCursorKeys_TDVMapper_StillSendsC0(int vk, string expected)
    {
        // TDV mapper resolves arrows via registry (step 3) before base mapper's
        // ApplicationCursorKeys conversion (step 5) can run. C0 codes are correct for TDV.
        var seq = _mapper.MapKey(vk, KeyModifiers.None, TerminalModes.ApplicationCursorKeys);
        Assert.Equal(expected, seq);
    }

    [Fact]
    public void ApplicationCursorKeys_Home_SendsC0()
    {
        // Home resolved via registry step 3 → C0 code 0x1D
        var seq = _mapper.MapKey(VK_HOME, KeyModifiers.None, TerminalModes.ApplicationCursorKeys);
        Assert.Equal("\x1d", seq);
    }

    #endregion

    #region Control Keys — TDV via Registry

    [Theory]
    [InlineData(13, "\r")]           // Enter (C13 RETURN, AlwaysSameCode)
    [InlineData(8, "\x08")]          // Backspace (PC-only key → _keyMappings)
    [InlineData(9, "\x1B[16_")]      // Tab (F47 TAB → TDV-native ESC[16_)
    [InlineData(27, "\x1b")]         // Escape (G0 ESC, AlwaysSameCode)
    public void ControlKeys_SendCorrectSequences(int vk, string expected)
    {
        var seq = _mapper.MapKey(vk, KeyModifiers.None, TerminalModes.None);
        Assert.Equal(expected, seq);
    }

    #endregion

    #region Custom Bindings Override Defaults

    [Fact]
    public void CustomBinding_OverridesDefault()
    {
        // By default, Alt+H → G53 (HJELP) = ESC[46_
        // Rebind Alt+H to G54 (SLUTT)
        var source = new KeyBindingSource(72, KeyModifiers.Alt);
        var target = new KeyBindingTarget("G54", false);
        TDVKeyBindingConfiguration.Instance.SetBinding(source, target);

        var mapper = new TDV2200KeyboardMapper();
        var seq = mapper.MapKey(72, KeyModifiers.Alt, TerminalModes.None);
        Assert.NotNull(seq);
        Assert.Equal("\x1B[48_", seq); // SLUTT, not HJELP
        _output.WriteLine($"Custom Alt+H → G54 SLUTT → {ToVisible(seq)}");
    }

    [Fact]
    public void UnmappedAltKey_ReturnsNull()
    {
        // Alt+Q has no default binding
        var seq = _mapper.MapKey(81, KeyModifiers.Alt, TerminalModes.None); // VK_Q
        Assert.Null(seq);
        _output.WriteLine("Alt+Q (unmapped) → null");
    }

    #endregion

    #region Factory

    [Theory]
    [InlineData("TDV2200Emulator", typeof(TDV2200KeyboardMapper))]
    [InlineData("TDV2215Emulator", typeof(TDV2215KeyboardMapper))]
    [InlineData("TDV1200Emulator", typeof(TDV1200KeyboardMapper))]
    [InlineData("VT100Emulator", typeof(VT100KeyboardMapper))]
    [InlineData("VT220Emulator", typeof(VT220KeyboardMapper))]
    [InlineData("UnknownEmulator", typeof(VT100KeyboardMapper))]
    public void KeyboardMapperFactory_CreatesCorrectType(string emulatorType, System.Type expectedType)
    {
        var mapper = KeyboardMapperFactory.CreateMapper(emulatorType);
        Assert.IsType(expectedType, mapper);
    }

    #endregion
}
