using RetroTerm.Core.Terminal.Input;
using Xunit;
using Xunit.Abstractions;

namespace RetroTerm.Tests.TDV;

/// <summary>
/// Unit tests for Alt+key combinations as alternative input method for TDV special keys.
/// These tests verify that Alt+letter/number combinations correctly map to TDV-specific keys
/// that are not available on a standard PC keyboard.
/// Sequences are TDV-native CSI nn _ format from TDV2200KeyRegistry.
/// </summary>
[Collection("TDVKeyBinding")]
public class TDV2200AltKeyUnitTests
{
    private readonly ITestOutputHelper _output;
    private readonly TDV2200KeyboardMapper _mapper;

    public TDV2200AltKeyUnitTests(ITestOutputHelper output)
    {
        _output = output;
        // Ensure default key binding configuration for consistent test behavior
        TDVKeyBindingConfiguration.ResetForTesting();
        _mapper = new TDV2200KeyboardMapper();
    }

    #region Alt+Letter Application Key Tests

    [Fact]
    public void AltH_ShouldMapToHELP()
    {
        var result = _mapper.MapKey(72, KeyModifiers.Alt, TerminalModes.None); // VK_H = 72
        Assert.Equal("\x1b[46_", result);
        _output.WriteLine("Alt+H = HJELP: PASS");
    }

    [Fact]
    public void AltD_ShouldMapToDO()
    {
        var result = _mapper.MapKey(68, KeyModifiers.Alt, TerminalModes.None); // VK_D = 68
        Assert.Equal("\x1b[20_", result);
        _output.WriteLine("Alt+D = REPLACE: PASS");
    }

    [Fact]
    public void AltU_ShouldMapToFUNC()
    {
        var result = _mapper.MapKey(85, KeyModifiers.Alt, TerminalModes.None); // VK_U = 85
        Assert.Equal("\x1b[42_", result);
        _output.WriteLine("Alt+U = FUNK: PASS");
    }

    [Fact]
    public void AltP_ShouldMapToPRINT()
    {
        var result = _mapper.MapKey(80, KeyModifiers.Alt, TerminalModes.None); // VK_P = 80
        Assert.Equal("\x1b[44_", result);
        _output.WriteLine("Alt+P = SKRIV: PASS");
    }

    [Fact]
    public void AltX_ShouldMapToGUILLEMETS()
    {
        var result = _mapper.MapKey(88, KeyModifiers.Alt, TerminalModes.None); // VK_X = 88
        Assert.Equal("\x1b[22_", result);
        _output.WriteLine("Alt+X = GUILLEMETS: PASS");
    }

    [Fact]
    public void AltBackspace_ShouldMapToANGRE()
    {
        var result = _mapper.MapKey(8, KeyModifiers.Alt, TerminalModes.None); // VK_BACK = 8
        Assert.Equal("\x1b[30_", result);
        _output.WriteLine("Alt+Backspace = ANGRE: PASS");
    }

    [Fact]
    public void AltM_ShouldMapToCOMMAND()
    {
        var result = _mapper.MapKey(77, KeyModifiers.Alt, TerminalModes.None); // VK_M = 77
        Assert.Equal("\x1b[84_", result);
        _output.WriteLine("Alt+M = MODE: PASS");
    }

    [Fact]
    public void AltF_ShouldMapToFIND()
    {
        var result = _mapper.MapKey(70, KeyModifiers.Alt, TerminalModes.None); // VK_F = 70
        Assert.Equal("\x1b[18_", result);
        _output.WriteLine("Alt+F = SEARCH: PASS");
    }

    [Fact]
    public void AltS_ShouldMapToSLUTT()
    {
        var result = _mapper.MapKey(83, KeyModifiers.Alt, TerminalModes.None); // VK_S = 83
        Assert.Equal("\x1b[48_", result);
        _output.WriteLine("Alt+S = SLUTT: PASS");
    }

    #endregion

    #region Alt+Letter Editing Key Tests

    [Fact]
    public void AltK_ShouldMapToCOPY()
    {
        var result = _mapper.MapKey(75, KeyModifiers.Alt, TerminalModes.None); // VK_K = 75
        Assert.Equal("\x1b[12_", result);
        _output.WriteLine("Alt+K = KOPI: PASS");
    }

    [Fact]
    public void AltV_ShouldMapToMOVE()
    {
        var result = _mapper.MapKey(86, KeyModifiers.Alt, TerminalModes.None); // VK_V = 86
        Assert.Equal("\x1b[14_", result);
        _output.WriteLine("Alt+V = FLYTT: PASS");
    }

    [Fact]
    public void AltJ_ShouldMapToJUST()
    {
        var result = _mapper.MapKey(74, KeyModifiers.Alt, TerminalModes.None); // VK_J = 74
        Assert.Equal("\x1b[24_", result);
        _output.WriteLine("Alt+J = JUST: PASS");
    }

    [Fact]
    public void AltA_ShouldMapToMARK()
    {
        var result = _mapper.MapKey(65, KeyModifiers.Alt, TerminalModes.None); // VK_A = 65
        Assert.Equal("\x1b[00_", result);
        _output.WriteLine("Alt+A = MERK: PASS");
    }

    [Fact]
    public void AltL_ShouldMapToFIELD()
    {
        var result = _mapper.MapKey(76, KeyModifiers.Alt, TerminalModes.None); // VK_L = 76
        Assert.Equal("\x1b[02_", result);
        _output.WriteLine("Alt+L = FELT: PASS");
    }

    [Fact]
    public void AltR_ShouldMapToPARA()
    {
        var result = _mapper.MapKey(82, KeyModifiers.Alt, TerminalModes.None); // VK_R = 82
        Assert.Equal("\x1b[04_", result);
        _output.WriteLine("Alt+R = AVSN: PASS");
    }

    [Fact]
    public void AltE_ShouldMapToSENT()
    {
        var result = _mapper.MapKey(69, KeyModifiers.Alt, TerminalModes.None); // VK_E = 69
        Assert.Equal("\x1b[06_", result);
        _output.WriteLine("Alt+E = SETN: PASS");
    }

    [Fact]
    public void AltW_ShouldMapToWORD()
    {
        var result = _mapper.MapKey(87, KeyModifiers.Alt, TerminalModes.None); // VK_W = 87
        Assert.Equal("\x1b[08_", result);
        _output.WriteLine("Alt+W = ORD: PASS");
    }

    [Fact]
    public void AltI_ShouldMapToINSERT_HERE()
    {
        var result = _mapper.MapKey(73, KeyModifiers.Alt, TerminalModes.None); // VK_I = 73
        Assert.Equal("\x1b[26_", result);
        _output.WriteLine("Alt+I = SINGLEGUILLEMETS: PASS");
    }

    #endregion

    #region Alt+Number PUSH Key Tests

    [Theory]
    [InlineData(49, 1)] // VK_1
    [InlineData(50, 2)] // VK_2
    [InlineData(51, 3)] // VK_3
    [InlineData(52, 4)] // VK_4
    [InlineData(53, 5)] // VK_5
    [InlineData(54, 6)] // VK_6
    [InlineData(55, 7)] // VK_7
    [InlineData(56, 8)] // VK_8
    public void AltNumber_ShouldMapToPUSHKey(int vkCode, int pushNum)
    {
        // Program the PUSH key with a test string
        TDVPushKeyConfiguration.ResetForTesting();
        var testString = $"PUSH{pushNum}";
        TDVPushKeyConfiguration.Instance.ProgramKey(pushNum, testString);

        var result = _mapper.MapKey(vkCode, KeyModifiers.Alt, TerminalModes.None);
        Assert.Equal(testString, result);
        _output.WriteLine($"Alt+{pushNum} = PUSH{pushNum}: PASS");
    }

    #endregion

    #region Alt+Navigation Key Tests

    [Fact]
    public void AltDelete_ShouldMapToREMOVE()
    {
        var result = _mapper.MapKey(46, KeyModifiers.Alt, TerminalModes.None); // VK_DELETE = 46
        Assert.Equal("\x1b[10_", result);
        _output.WriteLine("Alt+Delete = STRYK: PASS");
    }

    // keyboard-spec.md section 6.3: RollUp (D47, ESC[28_) is the Page Up key and RollDown
    // (D49, ESC[32_) is Page Down. Until 27 September 2026 these two tests pinned the reverse,
    // written from the code rather than the spec, which is why the crossed keys stayed green.
    [Fact]
    public void AltPageUp_ShouldMapToRollUp()
    {
        var result = _mapper.MapKey(33, KeyModifiers.Alt, TerminalModes.None); // VK_PRIOR = 33
        Assert.Equal("\x1b[28_", result);
        _output.WriteLine("Alt+PageUp = ROLLUP: PASS");
    }

    [Fact]
    public void AltPageDown_ShouldMapToRollDown()
    {
        var result = _mapper.MapKey(34, KeyModifiers.Alt, TerminalModes.None); // VK_NEXT = 34
        Assert.Equal("\x1b[32_", result);
        _output.WriteLine("Alt+PageDown = ROLLDN: PASS");
    }

    #endregion

    #region Comprehensive Alt+Letter Test

    [Theory]
    [InlineData(72, "\x1b[46_", "HJELP")]       // Alt+H → G53
    [InlineData(68, "\x1b[20_", "REPLACE")]      // Alt+D → F49
    [InlineData(85, "\x1b[42_", "FUNK")]         // Alt+U → G51
    [InlineData(80, "\x1b[44_", "SKRIV")]        // Alt+P → G52
    [InlineData(88, "\x1b[22_", "GUILLEMETS")]    // Alt+X → E47
    [InlineData(8, "\x1b[30_", "ANGRE")]          // Alt+Backspace → D48
    [InlineData(77, "\x1b[84_", "MODE")]         // Alt+M → C99
    [InlineData(70, "\x1b[18_", "SEARCH")]       // Alt+F → F48
    [InlineData(83, "\x1b[48_", "SLUTT")]         // Alt+S → G54
    [InlineData(75, "\x1b[12_", "KOPI")]         // Alt+K → G48
    [InlineData(86, "\x1b[14_", "FLYTT")]        // Alt+V → G49
    [InlineData(74, "\x1b[24_", "JUST")]         // Alt+J → E48
    [InlineData(65, "\x1b[00_", "MERK")]         // Alt+A → G9
    [InlineData(76, "\x1b[02_", "FELT")]         // Alt+L → G10
    [InlineData(82, "\x1b[04_", "AVSN")]         // Alt+R → G11
    [InlineData(69, "\x1b[06_", "SETN")]         // Alt+E → G12
    [InlineData(87, "\x1b[08_", "ORD")]          // Alt+W → G13
    [InlineData(73, "\x1b[26_", "SINGLEGUILLEMETS")] // Alt+I → E49
    public void AllAltLetterKeys_ShouldMapCorrectly(int vkCode, string expected, string keyName)
    {
        var result = _mapper.MapKey(vkCode, KeyModifiers.Alt, TerminalModes.None);
        Assert.Equal(expected, result);
        _output.WriteLine($"Alt+{(char)vkCode} = {keyName}: PASS");
    }

    #endregion

    #region TDV1200 Alt+Key Tests

    [Fact]
    public void TDV1200_AltH_ShouldMapToHELP()
    {
        var mapper = new TDV1200KeyboardMapper();
        var result = mapper.MapKey(72, KeyModifiers.Alt, TerminalModes.None);
        Assert.Equal("\x1b[46_", result);
        _output.WriteLine("TDV1200 Alt+H = HJELP: PASS");
    }

    [Fact]
    public void TDV1200_AltD_ShouldMapToDO()
    {
        var mapper = new TDV1200KeyboardMapper();
        var result = mapper.MapKey(68, KeyModifiers.Alt, TerminalModes.None);
        Assert.Equal("\x1b[20_", result);
        _output.WriteLine("TDV1200 Alt+D = REPLACE: PASS");
    }

    [Fact]
    public void TDV1200_AltK_ShouldMapToCOPY()
    {
        var mapper = new TDV1200KeyboardMapper();
        var result = mapper.MapKey(75, KeyModifiers.Alt, TerminalModes.None);
        Assert.Equal("\x1b[12_", result);
        _output.WriteLine("TDV1200 Alt+K = KOPI: PASS");
    }

    [Fact]
    public void TDV1200_AltV_ShouldMapToMOVE()
    {
        var mapper = new TDV1200KeyboardMapper();
        var result = mapper.MapKey(86, KeyModifiers.Alt, TerminalModes.None);
        Assert.Equal("\x1b[14_", result);
        _output.WriteLine("TDV1200 Alt+V = FLYTT: PASS");
    }

    #endregion

    #region TDV2215 Alt+Key Tests

    [Fact]
    public void TDV2215_AltH_ShouldMapToHELP()
    {
        var mapper = new TDV2215KeyboardMapper();
        var result = mapper.MapKey(72, KeyModifiers.Alt, TerminalModes.None);
        Assert.Equal("\x1b[46_", result);
        _output.WriteLine("TDV2215 Alt+H = HJELP: PASS");
    }

    [Fact]
    public void TDV2215_AltD_ShouldMapToDO()
    {
        var mapper = new TDV2215KeyboardMapper();
        var result = mapper.MapKey(68, KeyModifiers.Alt, TerminalModes.None);
        Assert.Equal("\x1b[20_", result);
        _output.WriteLine("TDV2215 Alt+D = REPLACE: PASS");
    }

    [Fact]
    public void TDV2215_AltU_ShouldMapToFUNC()
    {
        var mapper = new TDV2215KeyboardMapper();
        var result = mapper.MapKey(85, KeyModifiers.Alt, TerminalModes.None);
        Assert.Equal("\x1b[42_", result);
        _output.WriteLine("TDV2215 Alt+U = FUNK: PASS");
    }

    #endregion

    #region Unmapped Alt+Letter Should Return Null

    [Theory]
    [InlineData(66)] // VK_B - not mapped
    [InlineData(71)] // VK_G - not mapped
    [InlineData(78)] // VK_N - not mapped
    [InlineData(79)] // VK_O - not mapped
    [InlineData(81)] // VK_Q - not mapped
    [InlineData(84)] // VK_T - not mapped
    [InlineData(89)] // VK_Y - not mapped
    [InlineData(90)] // VK_Z - not mapped
    public void UnmappedAltLetter_ShouldReturnNull(int vkCode)
    {
        var result = _mapper.MapKey(vkCode, KeyModifiers.Alt, TerminalModes.None);
        Assert.Null(result);
        _output.WriteLine($"Alt+{(char)vkCode} = null (correctly unmapped): PASS");
    }

    #endregion

    #region Alt+Number 9 and 0 Should Not Map to PUSH

    [Theory]
    [InlineData(48)] // VK_0
    [InlineData(57)] // VK_9
    public void Alt9And0_ShouldNotMapToPUSH(int vkCode)
    {
        var result = _mapper.MapKey(vkCode, KeyModifiers.Alt, TerminalModes.None);
        // PUSH keys are only 1-8, so 0 and 9 should not produce PUSH sequences
        Assert.Null(result);
        _output.WriteLine($"Alt+{vkCode - 48} = null (no PUSH key): PASS");
    }

    #endregion
}
