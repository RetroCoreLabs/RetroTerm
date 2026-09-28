using RetroTerm.Core.Terminal.Emulators.TDV;
using Xunit;
using Xunit.Abstractions;

namespace RetroTerm.Tests.TDV;

/// <summary>
/// Comprehensive tests for TDV2200KeyRegistry.GetSequence covering ALL defined
/// normal, shifted, and ctrl variants. These are the escape sequences the
/// TestServer should identify.
/// </summary>
public class TDV2200KeyRegistrySequenceTests
{
    private readonly ITestOutputHelper _output;

    public TDV2200KeyRegistrySequenceTests(ITestOutputHelper output)
    {
        _output = output;
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

    #region Normal + Shifted — G-row Application Keys

    [Theory]
    [InlineData("G9", "MERK", "\x1B[00_", "\x1B[01_")]
    [InlineData("G10", "FELT", "\x1B[02_", "\x1B[03_")]
    [InlineData("G11", "AVSN", "\x1B[04_", "\x1B[05_")]
    [InlineData("G12", "SETN", "\x1B[06_", "\x1B[07_")]
    [InlineData("G13", "ORD", "\x1B[08_", "\x1B[09_")]
    public void GRow_ApplicationKeys_NormalAndShift(string grid, string name,
        string expectedNormal, string expectedShift)
    {
        var normal = TDV2200KeyRegistry.GetSequence(grid, true, false, false, false);
        var shifted = TDV2200KeyRegistry.GetSequence(grid, true, false, true, false);

        Assert.Equal(expectedNormal, normal);
        Assert.Equal(expectedShift, shifted);
        _output.WriteLine($"{grid} {name}: normal={ToVisible(normal)}, shift={ToVisible(shifted)}");
    }

    #endregion

    #region Normal + Shifted — G-row Navigation

    [Theory]
    [InlineData("G47", "STRYK", "\x1B[10_", "\x1B[11_")]
    [InlineData("G48", "KOPI", "\x1B[12_", "\x1B[13_")]
    [InlineData("G49", "FLYTT", "\x1B[14_", "\x1B[15_")]
    public void GRow_NavigationKeys_NormalAndShift(string grid, string name,
        string expectedNormal, string expectedShift)
    {
        var normal = TDV2200KeyRegistry.GetSequence(grid, true, false, false, false);
        var shifted = TDV2200KeyRegistry.GetSequence(grid, true, false, true, false);

        Assert.Equal(expectedNormal, normal);
        Assert.Equal(expectedShift, shifted);
        _output.WriteLine($"{grid} {name}: normal={ToVisible(normal)}, shift={ToVisible(shifted)}");
    }

    #endregion

    #region Normal + Shifted — G-row Function Area

    [Theory]
    [InlineData("G51", "FUNK", "\x1B[42_", "\x1B[43_")]
    [InlineData("G52", "SKRIV", "\x1B[44_", "\x1B[45_")]
    [InlineData("G53", "HJELP", "\x1B[46_", "\x1B[47_")]
    [InlineData("G54", "SLUTT", "\x1B[48_", "\x1B[49_")]
    public void GRow_FunctionKeys_NormalAndShift(string grid, string name,
        string expectedNormal, string expectedShift)
    {
        var normal = TDV2200KeyRegistry.GetSequence(grid, true, false, false, false);
        var shifted = TDV2200KeyRegistry.GetSequence(grid, true, false, true, false);

        Assert.Equal(expectedNormal, normal);
        Assert.Equal(expectedShift, shifted);
        _output.WriteLine($"{grid} {name}: normal={ToVisible(normal)}, shift={ToVisible(shifted)}");
    }

    #endregion

    #region Normal + Shifted — F-row

    [Theory]
    [InlineData("F47", "TAB", "\x1B[16_", "\x1B[17_")]
    [InlineData("F48", "SEARCH", "\x1B[18_", "\x1B[19_")]
    [InlineData("F49", "REPLACE", "\x1B[20_", "\x1B[21_")]
    public void FRow_NavigationKeys_NormalAndShift(string grid, string name,
        string expectedNormal, string expectedShift)
    {
        var normal = TDV2200KeyRegistry.GetSequence(grid, true, false, false, false);
        var shifted = TDV2200KeyRegistry.GetSequence(grid, true, false, true, false);

        Assert.Equal(expectedNormal, normal);
        Assert.Equal(expectedShift, shifted);
        _output.WriteLine($"{grid} {name}: normal={ToVisible(normal)}, shift={ToVisible(shifted)}");
    }

    [Theory]
    [InlineData("F51", "F1", "\x1B[50_", "\x1B[51_")]
    [InlineData("F52", "F2", "\x1B[52_", "\x1B[53_")]
    [InlineData("F53", "F3", "\x1B[55_", "\x1B[56_")]
    [InlineData("F54", "F4", "\x1B[58_", "\x1B[59_")]
    public void FRow_FunctionKeys_NormalAndShift(string grid, string name,
        string expectedNormal, string expectedShift)
    {
        var normal = TDV2200KeyRegistry.GetSequence(grid, true, false, false, false);
        var shifted = TDV2200KeyRegistry.GetSequence(grid, true, false, true, false);

        Assert.Equal(expectedNormal, normal);
        Assert.Equal(expectedShift, shifted);
        _output.WriteLine($"{grid} {name}: normal={ToVisible(normal)}, shift={ToVisible(shifted)}");
    }

    #endregion

    #region Ctrl Variants (only F2 and F3 have ExtCtrl)

    [Theory]
    [InlineData("F52", "F2", "\x1B[54_")]
    [InlineData("F53", "F3", "\x1B[57_")]
    public void FRow_CtrlVariants_CorrectSequence(string grid, string name, string expectedCtrl)
    {
        var ctrl = TDV2200KeyRegistry.GetSequence(grid, true, false, false, true);
        Assert.Equal(expectedCtrl, ctrl);
        _output.WriteLine($"{grid} {name}: ctrl={ToVisible(ctrl)}");
    }

    [Fact]
    public void F1_HasNoCtrlVariant_ReturnsNormal()
    {
        // F1 has no ExtCtrl, so Ctrl should fall through to ExtNormal
        var ctrl = TDV2200KeyRegistry.GetSequence("F51", true, false, false, true);
        var normal = TDV2200KeyRegistry.GetSequence("F51", true, false, false, false);
        Assert.Equal(normal, ctrl);
        _output.WriteLine($"F51 F1: ctrl falls back to normal={ToVisible(ctrl)}");
    }

    [Fact]
    public void F4_HasNoCtrlVariant_ReturnsNormal()
    {
        var ctrl = TDV2200KeyRegistry.GetSequence("F54", true, false, false, true);
        var normal = TDV2200KeyRegistry.GetSequence("F54", true, false, false, false);
        Assert.Equal(normal, ctrl);
    }

    #endregion

    #region Normal + Shifted — E-row

    [Theory]
    [InlineData("E13", "NEWPARA", "\x1B[86_", "\x1B[87_")]
    [InlineData("E47", "GUILLEMETS", "\x1B[22_", "\x1B[23_")]
    [InlineData("E48", "JUST", "\x1B[24_", "\x1B[25_")]
    [InlineData("E49", "SINGLEGUILLEMETS", "\x1B[26_", "\x1B[27_")]
    public void ERow_NavigationKeys_NormalAndShift(string grid, string name,
        string expectedNormal, string expectedShift)
    {
        var normal = TDV2200KeyRegistry.GetSequence(grid, true, false, false, false);
        var shifted = TDV2200KeyRegistry.GetSequence(grid, true, false, true, false);

        Assert.Equal(expectedNormal, normal);
        Assert.Equal(expectedShift, shifted);
        _output.WriteLine($"{grid} {name}: normal={ToVisible(normal)}, shift={ToVisible(shifted)}");
    }

    [Theory]
    [InlineData("E51", "F5", "\x1B[60_", "\x1B[61_")]
    [InlineData("E52", "F6", "\x1B[62_", "\x1B[63_")]
    [InlineData("E53", "F7", "\x1B[64_", "\x1B[65_")]
    [InlineData("E54", "F8", "\x1B[66_", "\x1B[67_")]
    public void ERow_FunctionKeys_NormalAndShift(string grid, string name,
        string expectedNormal, string expectedShift)
    {
        var normal = TDV2200KeyRegistry.GetSequence(grid, true, false, false, false);
        var shifted = TDV2200KeyRegistry.GetSequence(grid, true, false, true, false);

        Assert.Equal(expectedNormal, normal);
        Assert.Equal(expectedShift, shifted);
        _output.WriteLine($"{grid} {name}: normal={ToVisible(normal)}, shift={ToVisible(shifted)}");
    }

    #endregion

    #region Normal + Shifted — D-row

    [Theory]
    [InlineData("D99", "INNS", "\x1B[82_", "\x1B[83_")]
    [InlineData("D47", "ROLLUP", "\x1B[28_", "\x1B[29_")]
    [InlineData("D48", "ANGRE", "\x1B[30_", "\x1B[31_")]
    [InlineData("D49", "ROLLDN", "\x1B[32_", "\x1B[33_")]
    public void DRow_NavigationKeys_NormalAndShift(string grid, string name,
        string expectedNormal, string expectedShift)
    {
        var normal = TDV2200KeyRegistry.GetSequence(grid, true, false, false, false);
        var shifted = TDV2200KeyRegistry.GetSequence(grid, true, false, true, false);

        Assert.Equal(expectedNormal, normal);
        Assert.Equal(expectedShift, shifted);
        _output.WriteLine($"{grid} {name}: normal={ToVisible(normal)}, shift={ToVisible(shifted)}");
    }

    #endregion

    #region Normal + Shifted — C-row

    [Theory]
    [InlineData("C99", "MODE", "\x1B[84_", "\x1B[85_")]
    [InlineData("C47", "FIELDLEFT", "\x1B[34_", "\x1B[35_")]
    [InlineData("C49", "FIELDRIGHT", "\x1B[36_", "\x1B[37_")]
    public void CRow_NavigationKeys_NormalAndShift(string grid, string name,
        string expectedNormal, string expectedShift)
    {
        var normal = TDV2200KeyRegistry.GetSequence(grid, true, false, false, false);
        var shifted = TDV2200KeyRegistry.GetSequence(grid, true, false, true, false);

        Assert.Equal(expectedNormal, normal);
        Assert.Equal(expectedShift, shifted);
        _output.WriteLine($"{grid} {name}: normal={ToVisible(normal)}, shift={ToVisible(shifted)}");
    }

    #endregion

    #region Normal + Shifted — A-row

    [Theory]
    [InlineData("A47", "TABLEFT", "\x1B[38_", "\x1B[39_")]
    [InlineData("A49", "TABRIGHT", "\x1B[40_", "\x1B[41_")]
    public void ARow_NavigationKeys_NormalAndShift(string grid, string name,
        string expectedNormal, string expectedShift)
    {
        var normal = TDV2200KeyRegistry.GetSequence(grid, true, false, false, false);
        var shifted = TDV2200KeyRegistry.GetSequence(grid, true, false, true, false);

        Assert.Equal(expectedNormal, normal);
        Assert.Equal(expectedShift, shifted);
        _output.WriteLine($"{grid} {name}: normal={ToVisible(normal)}, shift={ToVisible(shifted)}");
    }

    #endregion

    #region AlwaysSameCode Keys — Shift Makes No Difference

    [Theory]
    [InlineData("G0", "ESC", "\x1B")]
    [InlineData("C48", "UP", "\x1C")]
    [InlineData("A48", "DOWN", "\x0B")]
    [InlineData("B47", "LEFT", "\x08")]
    [InlineData("B49", "RIGHT", "\x18")]
    [InlineData("B48", "HOME", "\x1D")]
    [InlineData("C13", "RETURN", "\x0D")]
    [InlineData("D13", "LF", "\x0A")]
    [InlineData("E14", "DEL", "\x7F")]
    [InlineData("B54", "KPENTER", "\x0D")]
    public void AlwaysSameCode_IgnoresShift_ReturnsSameSequence(string grid, string name, string expected)
    {
        var normal = TDV2200KeyRegistry.GetSequence(grid, true, false, false, false);
        var shifted = TDV2200KeyRegistry.GetSequence(grid, true, false, true, false);
        var ctrl = TDV2200KeyRegistry.GetSequence(grid, true, false, false, true);
        var shiftCtrl = TDV2200KeyRegistry.GetSequence(grid, true, false, true, true);

        Assert.Equal(expected, normal);
        Assert.Equal(expected, shifted);
        Assert.Equal(expected, ctrl);
        Assert.Equal(expected, shiftCtrl);
        _output.WriteLine($"{grid} {name}: always returns {ToVisible(expected)}");
    }

    #endregion

    #region Programmable Keys — Return Null

    [Theory]
    [InlineData("G1", "P1")]
    [InlineData("G2", "P2")]
    [InlineData("G3", "P3")]
    [InlineData("G4", "P4")]
    [InlineData("G5", "P5")]
    [InlineData("G6", "P6")]
    [InlineData("G7", "P7")]
    [InlineData("G8", "P8")]
    public void ProgrammableKeys_ReturnNull(string grid, string name)
    {
        var normal = TDV2200KeyRegistry.GetSequence(grid, true, false, false, false);
        var shifted = TDV2200KeyRegistry.GetSequence(grid, true, false, true, false);

        Assert.Null(normal);
        Assert.Null(shifted);
        _output.WriteLine($"{grid} {name}: correctly returns null (programmable)");
    }

    #endregion

    #region Simple ASCII Mode — Non-Extended

    [Theory]
    [InlineData("G10", "FELT", "\x02")]
    [InlineData("G11", "AVSN", "\x01")]
    [InlineData("G12", "SETN", "\x03")]
    [InlineData("F47", "TAB", "\x09")]
    [InlineData("F48", "SEARCH", "\x11")]
    [InlineData("F49", "REPLACE", "\x14")]
    [InlineData("D99", "INNS", "\x07")]
    [InlineData("D47", "ROLLUP", "\x06")]
    [InlineData("D48", "ANGRE", "\x15")]
    [InlineData("D49", "ROLLDN", "\x05")]
    [InlineData("C99", "MODE", "\x05")]
    [InlineData("C47", "FIELDLEFT", "\x0C")]
    [InlineData("C49", "FIELDRIGHT", "\x17")]
    [InlineData("A47", "TABLEFT", "\x15")]
    [InlineData("A49", "TABRIGHT", "\x09")]
    public void SimpleAsciiMode_ReturnsC0Codes(string grid, string name, string expected)
    {
        var simple = TDV2200KeyRegistry.GetSequence(grid, false, false, false, false);
        Assert.Equal(expected, simple);
        _output.WriteLine($"{grid} {name}: simple ASCII = {ToVisible(expected)}");
    }

    [Theory]
    [InlineData("C48", "UP", "\x1C")]
    [InlineData("A48", "DOWN", "\x0B")]
    [InlineData("B47", "LEFT", "\x08")]
    [InlineData("B49", "RIGHT", "\x18")]
    [InlineData("B48", "HOME", "\x1D")]
    [InlineData("C13", "RETURN", "\x0D")]
    [InlineData("D13", "LF", "\x0A")]
    [InlineData("E14", "DEL", "\x7F")]
    public void SimpleAsciiMode_AlwaysSameCode_ReturnsSimpleAscii(string grid, string name, string expected)
    {
        var simple = TDV2200KeyRegistry.GetSequence(grid, false, false, false, false);
        Assert.Equal(expected, simple);
        _output.WriteLine($"{grid} {name}: simple ASCII = {ToVisible(expected)}");
    }

    #endregion

    #region GetGridForVK — Reverse Lookup

    [Theory]
    [InlineData(38, "C48")]   // VK_UP → UP
    [InlineData(40, "A48")]   // VK_DOWN → DOWN
    [InlineData(37, "B47")]   // VK_LEFT → LEFT
    [InlineData(39, "B49")]   // VK_RIGHT → RIGHT
    [InlineData(36, "B48")]   // VK_HOME → HOME
    [InlineData(46, "G47")]   // VK_DELETE → STRYK
    [InlineData(45, "D99")]   // VK_INSERT → INNS
    [InlineData(33, "D47")]   // VK_PRIOR (PageUp) → ROLLUP, keyboard-spec.md section 6.3
    [InlineData(34, "D49")]   // VK_NEXT (PageDown) → ROLLDN
    [InlineData(13, "C13")]   // VK_RETURN → RETURN
    [InlineData(27, "G0")]    // VK_ESCAPE → ESC
    [InlineData(9, "F47")]    // VK_TAB → TAB
    public void GetGridForVK_KnownKeys_ReturnsCorrectGrid(int vk, string expectedGrid)
    {
        var grid = TDV2200KeyRegistry.GetGridForVK(vk);
        Assert.Equal(expectedGrid, grid);
    }

    [Fact]
    public void GetGridForVK_UnknownVK_ReturnsNull()
    {
        var grid = TDV2200KeyRegistry.GetGridForVK(9999);
        Assert.Null(grid);
    }

    #endregion

    #region GetGridForName — Name Aliases

    [Theory]
    [InlineData("HELP", "G53")]
    [InlineData("EXIT", "G54")]
    [InlineData("FUNC", "G51")]
    [InlineData("PRINT", "G52")]
    [InlineData("COPY", "G48")]
    [InlineData("MOVE", "G49")]
    [InlineData("MARK", "G9")]
    [InlineData("FIELD", "G10")]
    [InlineData("WORD", "G13")]
    [InlineData("DELETE_KEY", "G47")]
    [InlineData("ENTER", "C13")]
    [InlineData("PAGEUP", "D47")]   // RollUp is Page Up, keyboard-spec.md section 6.3
    [InlineData("PAGEDOWN", "D49")]
    [InlineData("INSERT", "C49")]
    [InlineData("MODE", "C99")]
    [InlineData("PUSH1", "G1")]
    [InlineData("PUSH8", "G8")]
    public void GetGridForName_Aliases_ResolveCorrectly(string name, string expectedGrid)
    {
        var grid = TDV2200KeyRegistry.GetGridForName(name);
        Assert.Equal(expectedGrid, grid);
    }

    [Fact]
    public void GetGridForName_CaseInsensitive()
    {
        Assert.Equal("G53", TDV2200KeyRegistry.GetGridForName("help"));
        Assert.Equal("G53", TDV2200KeyRegistry.GetGridForName("HELP")!);
        Assert.Equal("G53", TDV2200KeyRegistry.GetGridForName("Help"));
    }

    [Fact]
    public void GetGridForName_UnknownName_ReturnsNull()
    {
        Assert.Null(TDV2200KeyRegistry.GetGridForName("NONEXISTENT"));
    }

    #endregion

    #region Invalid Grid Position

    [Fact]
    public void GetSequence_InvalidGrid_ReturnsNull()
    {
        var seq = TDV2200KeyRegistry.GetSequence("ZZZ", true, false, false, false);
        Assert.Null(seq);
    }

    [Fact]
    public void TryGetKey_InvalidGrid_ReturnsFalse()
    {
        Assert.False(TDV2200KeyRegistry.TryGetKey("ZZZ", out _));
    }

    #endregion

    #region NumPad Function Mode

    [Theory]
    [InlineData("D51", "KP7", "\x1B[75_")]
    [InlineData("D52", "KP8", "\x1B[76_")]
    [InlineData("D53", "KP9", "\x1B[77_")]
    [InlineData("D54", "KPSPACE", "\x1B[80_")]
    [InlineData("C51", "KP4", "\x1B[72_")]
    [InlineData("C52", "KP5", "\x1B[73_")]
    [InlineData("C53", "KP6", "\x1B[74_")]
    [InlineData("C54", "KPMINUS", "\x1B[79_")]
    [InlineData("B51", "KP1", "\x1B[69_")]
    [InlineData("B52", "KP2", "\x1B[70_")]
    [InlineData("B53", "KP3", "\x1B[71_")]
    [InlineData("A51", "KP0", "\x1B[68_")]
    [InlineData("A53", "KPDOT", "\x1B[78_")]
    public void NumPadFuncMode_ReturnsSpecialSequences(string grid, string name, string expected)
    {
        var seq = TDV2200KeyRegistry.GetSequence(grid, true, true, false, false);
        Assert.Equal(expected, seq);
        _output.WriteLine($"{grid} {name}: numPadFunc={ToVisible(expected)}");
    }

    [Fact]
    public void NumPadFuncMode_NonNumPadKey_IgnoresFlag()
    {
        // G53 HJELP is not a numpad key; numPadFuncMode should be ignored
        var normal = TDV2200KeyRegistry.GetSequence("G53", true, false, false, false);
        var withNumPad = TDV2200KeyRegistry.GetSequence("G53", true, true, false, false);
        Assert.Equal(normal, withNumPad);
    }

    #endregion

    #region Comprehensive — All Keys With Sequences

    [Fact]
    public void AllKeysWithExtNormal_ReturnNonNull()
    {
        var allKeys = TDV2200KeyRegistry.AllKeys;
        int count = 0;
        int withSequence = 0;

        foreach (var kvp in allKeys)
        {
            count++;
            var key = kvp.Value;
            if ((key.Flags & TDVKeyFlags.IsProgrammable) != 0) continue;
            if ((key.Flags & TDVKeyFlags.IsModifier) != 0) continue;
            if ((key.Flags & TDVKeyFlags.IsToggle) != 0) continue;

            if (key.ExtNormal != null)
            {
                var seq = TDV2200KeyRegistry.GetSequence(kvp.Key, true, false, false, false);
                Assert.NotNull(seq);
                withSequence++;
                _output.WriteLine($"{kvp.Key} ({key.Name}): {ToVisible(seq)}");
            }
        }

        _output.WriteLine($"Total keys: {count}, with sequences: {withSequence}");
        Assert.True(withSequence > 30, "Should have many keys with sequences");
    }

    #endregion
}
