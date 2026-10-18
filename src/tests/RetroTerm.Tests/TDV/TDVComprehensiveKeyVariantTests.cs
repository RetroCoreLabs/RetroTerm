using System.Collections.Generic;
using System.Text;
using RetroTerm.Core.Terminal.Emulators.TDV;
using RetroTerm.Core.Terminal.Input;
using Xunit;

namespace RetroTerm.Tests.TDV;

/// <summary>
/// Comprehensive test of EVERY key in TDV2200KeyRegistry, in ALL variants
/// (Normal, Shift, Ctrl) through both the registry directly and the mapper.
/// Ensures no key/variant combination silently fails.
/// </summary>
[Collection("TDVKeyBinding")]
public class TDVComprehensiveKeyVariantTests
{
    private readonly ITestOutputHelper _output;
    private readonly TDV2200KeyboardMapper _mapper;

    public TDVComprehensiveKeyVariantTests(ITestOutputHelper output)
    {
        _output = output;
        TDVKeyBindingConfiguration.ResetForTesting();
        _mapper = new TDV2200KeyboardMapper();
    }

    private static string ToVisible(string? s)
    {
        if (s == null) return "(null)";
        var sb = new StringBuilder();
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            if (c == 0x1B) sb.Append("ESC");
            else if (c < 0x20) sb.Append($"<{(int)c:X2}>");
            else sb.Append(c);
        }
        return sb.ToString();
    }

    // =================================================================
    // Registry Direct: Every key, Normal variant
    // =================================================================

    [Fact]
    public void Registry_AllKeys_Normal_ProduceExpectedSequence()
    {
        var allKeys = TDV2200KeyRegistry.AllKeys;
        var tested = 0;
        var sb = new StringBuilder();

        foreach (var kvp in allKeys)
        {
            var grid = kvp.Key;
            var def = kvp.Value;

            // Skip modifiers, toggles, programmable keys, keys with no sequences
            if ((def.Flags & TDVKeyFlags.IsModifier) != 0) continue;
            if ((def.Flags & TDVKeyFlags.IsToggle) != 0) continue;
            if ((def.Flags & TDVKeyFlags.IsProgrammable) != 0) continue;
            if (def.ExtNormal == null) continue;

            var seq = TDV2200KeyRegistry.GetSequence(grid, true, false, false, false);
            Assert.NotNull(seq);
            Assert.Equal(def.ExtNormal, seq);
            sb.AppendLine($"  {grid} {def.Name}: Normal → {ToVisible(seq)}");
            tested++;
        }

        _output.WriteLine($"Registry Normal: {tested} keys tested");
        _output.WriteLine(sb.ToString());
        Assert.True(tested > 40, $"Expected 40+ keys, got {tested}");
    }

    // =================================================================
    // Registry Direct: Every key, Shift variant
    // =================================================================

    [Fact]
    public void Registry_AllKeys_Shift_ProduceExpectedSequence()
    {
        var allKeys = TDV2200KeyRegistry.AllKeys;
        var tested = 0;
        var sb = new StringBuilder();

        foreach (var kvp in allKeys)
        {
            var grid = kvp.Key;
            var def = kvp.Value;

            if ((def.Flags & TDVKeyFlags.IsModifier) != 0) continue;
            if ((def.Flags & TDVKeyFlags.IsToggle) != 0) continue;
            if ((def.Flags & TDVKeyFlags.IsProgrammable) != 0) continue;
            if (def.ExtNormal == null) continue;

            var seq = TDV2200KeyRegistry.GetSequence(grid, true, false, true, false);
            Assert.NotNull(seq);

            if ((def.Flags & TDVKeyFlags.AlwaysSameCode) != 0)
            {
                // AlwaysSameCode: Shift returns same as Normal
                Assert.Equal(def.ExtNormal, seq);
                sb.AppendLine($"  {grid} {def.Name}: Shift → {ToVisible(seq)} (AlwaysSameCode)");
            }
            else if (def.ExtShift != null)
            {
                Assert.Equal(def.ExtShift, seq);
                sb.AppendLine($"  {grid} {def.Name}: Shift → {ToVisible(seq)}");
            }
            else
            {
                // No shift variant: falls to Normal
                Assert.Equal(def.ExtNormal, seq);
                sb.AppendLine($"  {grid} {def.Name}: Shift → {ToVisible(seq)} (no shift, fallback)");
            }
            tested++;
        }

        _output.WriteLine($"Registry Shift: {tested} keys tested");
        _output.WriteLine(sb.ToString());
        Assert.True(tested > 40, $"Expected 40+ keys, got {tested}");
    }

    // =================================================================
    // Registry Direct: Every key, Ctrl variant
    // =================================================================

    [Fact]
    public void Registry_AllKeys_Ctrl_ProduceExpectedSequence()
    {
        var allKeys = TDV2200KeyRegistry.AllKeys;
        var tested = 0;
        var ctrlKeys = 0;
        var sb = new StringBuilder();

        foreach (var kvp in allKeys)
        {
            var grid = kvp.Key;
            var def = kvp.Value;

            if ((def.Flags & TDVKeyFlags.IsModifier) != 0) continue;
            if ((def.Flags & TDVKeyFlags.IsToggle) != 0) continue;
            if ((def.Flags & TDVKeyFlags.IsProgrammable) != 0) continue;
            if (def.ExtNormal == null) continue;

            var seq = TDV2200KeyRegistry.GetSequence(grid, true, false, false, true);
            Assert.NotNull(seq);

            if ((def.Flags & TDVKeyFlags.AlwaysSameCode) != 0)
            {
                Assert.Equal(def.ExtNormal, seq);
                sb.AppendLine($"  {grid} {def.Name}: Ctrl → {ToVisible(seq)} (AlwaysSameCode)");
            }
            else if (def.ExtCtrl != null)
            {
                Assert.Equal(def.ExtCtrl, seq);
                sb.AppendLine($"  {grid} {def.Name}: Ctrl → {ToVisible(seq)} (HAS CTRL VARIANT)");
                ctrlKeys++;
            }
            else
            {
                // No ctrl variant: falls to Normal
                Assert.Equal(def.ExtNormal, seq);
                sb.AppendLine($"  {grid} {def.Name}: Ctrl → {ToVisible(seq)} (no ctrl, fallback to normal)");
            }
            tested++;
        }

        _output.WriteLine($"Registry Ctrl: {tested} keys tested, {ctrlKeys} have actual Ctrl variants");
        _output.WriteLine(sb.ToString());
        Assert.True(tested > 40, $"Expected 40+ keys, got {tested}");
        // Only F2 (F52) and F3 (F53) have Ctrl variants in the spec
        Assert.Equal(2, ctrlKeys);
    }

    // =================================================================
    // Mapper: Every VK-mapped key, Normal variant
    // =================================================================

    [Fact]
    public void Mapper_AllVKKeys_Normal_ProduceSequence()
    {
        var allKeys = TDV2200KeyRegistry.AllKeys;
        var tested = 0;
        var failures = new List<string>();
        var sb = new StringBuilder();

        foreach (var kvp in allKeys)
        {
            var grid = kvp.Key;
            var def = kvp.Value;

            if (def.VirtualKeyCode <= 0) continue;
            if ((def.Flags & TDVKeyFlags.IsModifier) != 0) continue;
            if ((def.Flags & TDVKeyFlags.IsToggle) != 0) continue;
            if ((def.Flags & TDVKeyFlags.IsProgrammable) != 0) continue;
            if (def.ExtNormal == null) continue;

            // Check what grid the VK actually maps to (first-registered wins)
            var actualGrid = TDV2200KeyRegistry.GetGridForVK(def.VirtualKeyCode);
            if (actualGrid == null) continue;

            var mapperSeq = _mapper.MapKey(def.VirtualKeyCode, KeyModifiers.None, TerminalModes.None);
            var registrySeq = TDV2200KeyRegistry.GetSequence(actualGrid, true, false, false, false);

            if (mapperSeq == null)
            {
                failures.Add($"  FAIL: VK {def.VirtualKeyCode} ({def.Name}, {grid}) → mapper returned null, expected {ToVisible(registrySeq)}");
            }
            else if (mapperSeq != registrySeq)
            {
                failures.Add($"  MISMATCH: VK {def.VirtualKeyCode} ({def.Name}, {grid}) → mapper={ToVisible(mapperSeq)}, registry={ToVisible(registrySeq)} (actual grid={actualGrid})");
            }
            else
            {
                sb.AppendLine($"  OK: VK {def.VirtualKeyCode} ({def.Name}, {grid}) → {ToVisible(mapperSeq)}");
            }
            tested++;
        }

        _output.WriteLine($"Mapper Normal: {tested} VK-mapped keys tested");
        _output.WriteLine(sb.ToString());
        if (failures.Count > 0)
        {
            _output.WriteLine($"\nFAILURES ({failures.Count}):");
            for (int i = 0; i < failures.Count; i++)
                _output.WriteLine(failures[i]);
        }
        Assert.Empty(failures);
    }

    // =================================================================
    // Mapper: Every VK-mapped key, Shift variant
    // =================================================================

    [Fact]
    public void Mapper_AllVKKeys_Shift_ProduceSequence()
    {
        var allKeys = TDV2200KeyRegistry.AllKeys;
        var tested = 0;
        var failures = new List<string>();
        var sb = new StringBuilder();

        foreach (var kvp in allKeys)
        {
            var grid = kvp.Key;
            var def = kvp.Value;

            if (def.VirtualKeyCode <= 0) continue;
            if ((def.Flags & TDVKeyFlags.IsModifier) != 0) continue;
            if ((def.Flags & TDVKeyFlags.IsToggle) != 0) continue;
            if ((def.Flags & TDVKeyFlags.IsProgrammable) != 0) continue;
            if (def.ExtNormal == null) continue;

            var actualGrid = TDV2200KeyRegistry.GetGridForVK(def.VirtualKeyCode);
            if (actualGrid == null) continue;

            var mapperSeq = _mapper.MapKey(def.VirtualKeyCode, KeyModifiers.Shift, TerminalModes.None);
            var registrySeq = TDV2200KeyRegistry.GetSequence(actualGrid, true, false, true, false);

            if (mapperSeq == null)
            {
                failures.Add($"  FAIL: Shift+VK {def.VirtualKeyCode} ({def.Name}, {grid}) → mapper returned null, expected {ToVisible(registrySeq)}");
            }
            else if (mapperSeq != registrySeq)
            {
                failures.Add($"  MISMATCH: Shift+VK {def.VirtualKeyCode} ({def.Name}, {grid}) → mapper={ToVisible(mapperSeq)}, registry={ToVisible(registrySeq)} (actual grid={actualGrid})");
            }
            else
            {
                sb.AppendLine($"  OK: Shift+VK {def.VirtualKeyCode} ({def.Name}, {grid}) → {ToVisible(mapperSeq)}");
            }
            tested++;
        }

        _output.WriteLine($"Mapper Shift: {tested} VK-mapped keys tested");
        _output.WriteLine(sb.ToString());
        if (failures.Count > 0)
        {
            _output.WriteLine($"\nFAILURES ({failures.Count}):");
            for (int i = 0; i < failures.Count; i++)
                _output.WriteLine(failures[i]);
        }
        Assert.Empty(failures);
    }

    // =================================================================
    // Mapper: Every VK-mapped key, Ctrl variant
    // =================================================================

    [Fact]
    public void Mapper_AllVKKeys_Ctrl_ProduceSequence()
    {
        var allKeys = TDV2200KeyRegistry.AllKeys;
        var tested = 0;
        var failures = new List<string>();
        var sb = new StringBuilder();

        foreach (var kvp in allKeys)
        {
            var grid = kvp.Key;
            var def = kvp.Value;

            if (def.VirtualKeyCode <= 0) continue;
            if ((def.Flags & TDVKeyFlags.IsModifier) != 0) continue;
            if ((def.Flags & TDVKeyFlags.IsToggle) != 0) continue;
            if ((def.Flags & TDVKeyFlags.IsProgrammable) != 0) continue;
            if (def.ExtNormal == null) continue;

            var actualGrid = TDV2200KeyRegistry.GetGridForVK(def.VirtualKeyCode);
            if (actualGrid == null) continue;

            var mapperSeq = _mapper.MapKey(def.VirtualKeyCode, KeyModifiers.Ctrl, TerminalModes.None);
            var registrySeq = TDV2200KeyRegistry.GetSequence(actualGrid, true, false, false, true);

            if (mapperSeq == null)
            {
                failures.Add($"  FAIL: Ctrl+VK {def.VirtualKeyCode} ({def.Name}, {grid}) → mapper returned null, expected {ToVisible(registrySeq)}");
            }
            else if (mapperSeq != registrySeq)
            {
                failures.Add($"  MISMATCH: Ctrl+VK {def.VirtualKeyCode} ({def.Name}, {grid}) → mapper={ToVisible(mapperSeq)}, registry={ToVisible(registrySeq)} (actual grid={actualGrid})");
            }
            else
            {
                sb.AppendLine($"  OK: Ctrl+VK {def.VirtualKeyCode} ({def.Name}, {grid}) → {ToVisible(mapperSeq)}");
            }
            tested++;
        }

        _output.WriteLine($"Mapper Ctrl: {tested} VK-mapped keys tested");
        _output.WriteLine(sb.ToString());
        if (failures.Count > 0)
        {
            _output.WriteLine($"\nFAILURES ({failures.Count}):");
            for (int i = 0; i < failures.Count; i++)
                _output.WriteLine(failures[i]);
        }
        Assert.Empty(failures);
    }

    // =================================================================
    // Specific: System keys FUNK/SKRIV/HJELP/SLUTT with Ctrl
    // =================================================================

    [Theory]
    [InlineData("G51", "FUNK", "\x1B[42_", "\x1B[43_", "\x1B[42_")]   // No Ctrl → Normal
    [InlineData("G52", "SKRIV", "\x1B[44_", "\x1B[45_", "\x1B[44_")]   // No Ctrl → Normal
    [InlineData("G53", "HJELP", "\x1B[46_", "\x1B[47_", "\x1B[46_")]   // No Ctrl → Normal
    [InlineData("G54", "SLUTT", "\x1B[48_", "\x1B[49_", "\x1B[48_")]   // No Ctrl → Normal
    public void SystemKeys_AllVariants(string grid, string name,
        string expectedNormal, string expectedShift, string expectedCtrl)
    {
        var normal = TDV2200KeyRegistry.GetSequence(grid, true, false, false, false);
        var shift = TDV2200KeyRegistry.GetSequence(grid, true, false, true, false);
        var ctrl = TDV2200KeyRegistry.GetSequence(grid, true, false, false, true);

        Assert.Equal(expectedNormal, normal);
        Assert.Equal(expectedShift, shift);
        Assert.Equal(expectedCtrl, ctrl);
        _output.WriteLine($"{grid} {name}: Normal={ToVisible(normal)}, Shift={ToVisible(shift)}, Ctrl={ToVisible(ctrl)}");
    }

    // =================================================================
    // Specific: F1-F4 with Ctrl (F2/F3 have real Ctrl variants)
    // =================================================================

    [Theory]
    [InlineData("F51", "F1", "\x1B[50_", "\x1B[51_", "\x1B[50_")]     // No Ctrl → Normal
    [InlineData("F52", "F2", "\x1B[52_", "\x1B[53_", "\x1B[54_")]     // HAS Ctrl variant
    [InlineData("F53", "F3", "\x1B[55_", "\x1B[56_", "\x1B[57_")]     // HAS Ctrl variant
    [InlineData("F54", "F4", "\x1B[58_", "\x1B[59_", "\x1B[58_")]     // No Ctrl → Normal
    public void FunctionKeys_F1toF4_AllVariants(string grid, string name,
        string expectedNormal, string expectedShift, string expectedCtrl)
    {
        var normal = TDV2200KeyRegistry.GetSequence(grid, true, false, false, false);
        var shift = TDV2200KeyRegistry.GetSequence(grid, true, false, true, false);
        var ctrl = TDV2200KeyRegistry.GetSequence(grid, true, false, false, true);

        Assert.Equal(expectedNormal, normal);
        Assert.Equal(expectedShift, shift);
        Assert.Equal(expectedCtrl, ctrl);
        _output.WriteLine($"{grid} {name}: Normal={ToVisible(normal)}, Shift={ToVisible(shift)}, Ctrl={ToVisible(ctrl)}");
    }

    // =================================================================
    // Specific: F5-F8 with all variants
    // =================================================================

    [Theory]
    [InlineData("E51", "F5", "\x1B[60_", "\x1B[61_", "\x1B[60_")]     // No Ctrl → Normal
    [InlineData("E52", "F6", "\x1B[62_", "\x1B[63_", "\x1B[62_")]     // No Ctrl → Normal
    [InlineData("E53", "F7", "\x1B[64_", "\x1B[65_", "\x1B[64_")]     // No Ctrl → Normal
    [InlineData("E54", "F8", "\x1B[66_", "\x1B[67_", "\x1B[66_")]     // No Ctrl → Normal
    public void FunctionKeys_F5toF8_AllVariants(string grid, string name,
        string expectedNormal, string expectedShift, string expectedCtrl)
    {
        var normal = TDV2200KeyRegistry.GetSequence(grid, true, false, false, false);
        var shift = TDV2200KeyRegistry.GetSequence(grid, true, false, true, false);
        var ctrl = TDV2200KeyRegistry.GetSequence(grid, true, false, false, true);

        Assert.Equal(expectedNormal, normal);
        Assert.Equal(expectedShift, shift);
        Assert.Equal(expectedCtrl, ctrl);
        _output.WriteLine($"{grid} {name}: Normal={ToVisible(normal)}, Shift={ToVisible(shift)}, Ctrl={ToVisible(ctrl)}");
    }

    // =================================================================
    // Specific: Mapper — TDV F1 via VK 112 (F1 physical key)
    // =================================================================

    [Fact]
    public void Mapper_F1Key_AllVariants_SendsTDVF1()
    {
        // VK 112 → F51 TDV F1
        var normal = _mapper.MapKey(112, KeyModifiers.None, TerminalModes.None);
        var shift = _mapper.MapKey(112, KeyModifiers.Shift, TerminalModes.None);
        var ctrl = _mapper.MapKey(112, KeyModifiers.Ctrl, TerminalModes.None);

        Assert.Equal("\x1B[50_", normal);   // TDV F1 normal
        Assert.Equal("\x1B[51_", shift);    // TDV F1 shifted
        Assert.Equal("\x1B[50_", ctrl);     // TDV F1 (no ctrl variant → falls to normal)

        _output.WriteLine($"VK 112 (F1→TDV F1): Normal={ToVisible(normal)}, Shift={ToVisible(shift)}, Ctrl={ToVisible(ctrl)}");
    }

    // =================================================================
    // Specific: Mapper — F2 via VK 113 (has actual Ctrl variant)
    // =================================================================

    [Fact]
    public void Mapper_F2Key_AllVariants()
    {
        var normal = _mapper.MapKey(113, KeyModifiers.None, TerminalModes.None);
        var shift = _mapper.MapKey(113, KeyModifiers.Shift, TerminalModes.None);
        var ctrl = _mapper.MapKey(113, KeyModifiers.Ctrl, TerminalModes.None);

        Assert.Equal("\x1B[52_", normal);   // F2 normal
        Assert.Equal("\x1B[53_", shift);    // F2 shifted
        Assert.Equal("\x1B[54_", ctrl);     // F2 ctrl (ACTUAL ctrl variant)

        _output.WriteLine($"VK 113 (F2): Normal={ToVisible(normal)}, Shift={ToVisible(shift)}, Ctrl={ToVisible(ctrl)}");
    }

    // =================================================================
    // Specific: Mapper — F3 via VK 114 (has actual Ctrl variant)
    // =================================================================

    [Fact]
    public void Mapper_F3Key_AllVariants()
    {
        var normal = _mapper.MapKey(114, KeyModifiers.None, TerminalModes.None);
        var shift = _mapper.MapKey(114, KeyModifiers.Shift, TerminalModes.None);
        var ctrl = _mapper.MapKey(114, KeyModifiers.Ctrl, TerminalModes.None);

        Assert.Equal("\x1B[55_", normal);   // F3 normal
        Assert.Equal("\x1B[56_", shift);    // F3 shifted
        Assert.Equal("\x1B[57_", ctrl);     // F3 ctrl (ACTUAL ctrl variant)

        _output.WriteLine($"VK 114 (F3): Normal={ToVisible(normal)}, Shift={ToVisible(shift)}, Ctrl={ToVisible(ctrl)}");
    }

    // =================================================================
    // Specific: Mapper — Navigation keys (PageUp/Down, Insert, Delete)
    // =================================================================

    [Theory]
    [InlineData(33, "PageUp/ROLLUP", "\x1B[28_", "\x1B[29_", "\x1B[28_")]   // spec section 6.3
    [InlineData(34, "PageDn/ROLLDN", "\x1B[32_", "\x1B[33_", "\x1B[32_")]
    [InlineData(45, "Insert/INNS", "\x1B[82_", "\x1B[83_", "\x1B[82_")]
    [InlineData(46, "Delete/STRYK", "\x1B[10_", "\x1B[11_", "\x1B[10_")]
    public void Mapper_NavKeys_AllVariants(int vk, string name,
        string expectedNormal, string expectedShift, string expectedCtrl)
    {
        var normal = _mapper.MapKey(vk, KeyModifiers.None, TerminalModes.None);
        var shift = _mapper.MapKey(vk, KeyModifiers.Shift, TerminalModes.None);
        var ctrl = _mapper.MapKey(vk, KeyModifiers.Ctrl, TerminalModes.None);

        Assert.Equal(expectedNormal, normal);
        Assert.Equal(expectedShift, shift);
        Assert.Equal(expectedCtrl, ctrl);
        _output.WriteLine($"VK {vk} ({name}): Normal={ToVisible(normal)}, Shift={ToVisible(shift)}, Ctrl={ToVisible(ctrl)}");
    }

    // =================================================================
    // Specific: Arrow keys (AlwaysSameCode — all variants identical)
    // =================================================================

    [Theory]
    [InlineData(38, "Up", "\x1c")]
    [InlineData(40, "Down", "\x0b")]
    [InlineData(39, "Right", "\x18")]
    [InlineData(37, "Left", "\x08")]
    [InlineData(36, "Home", "\x1d")]
    public void Mapper_ArrowKeys_AllVariants_SameC0(int vk, string name, string expected)
    {
        var normal = _mapper.MapKey(vk, KeyModifiers.None, TerminalModes.None);
        var shift = _mapper.MapKey(vk, KeyModifiers.Shift, TerminalModes.None);
        var ctrl = _mapper.MapKey(vk, KeyModifiers.Ctrl, TerminalModes.None);
        var ctrlShift = _mapper.MapKey(vk, KeyModifiers.Ctrl | KeyModifiers.Shift, TerminalModes.None);

        Assert.Equal(expected, normal);
        Assert.Equal(expected, shift);
        Assert.Equal(expected, ctrl);
        Assert.Equal(expected, ctrlShift);
        _output.WriteLine($"VK {vk} ({name}): All variants → {ToVisible(expected)} (AlwaysSameCode)");
    }

    // =================================================================
    // Specific: Tab key (VK 9 → F47 TAB)
    // =================================================================

    [Fact]
    public void Mapper_Tab_AllVariants()
    {
        var normal = _mapper.MapKey(9, KeyModifiers.None, TerminalModes.None);
        var shift = _mapper.MapKey(9, KeyModifiers.Shift, TerminalModes.None);
        var ctrl = _mapper.MapKey(9, KeyModifiers.Ctrl, TerminalModes.None);

        Assert.Equal("\x1B[16_", normal);   // TAB+ (F47)
        Assert.Equal("\x1B[17_", shift);    // TAB- (F47 shifted)
        Assert.Equal("\x1B[16_", ctrl);     // No ctrl variant → normal

        _output.WriteLine($"VK 9 (Tab→F47): Normal={ToVisible(normal)}, Shift={ToVisible(shift)}, Ctrl={ToVisible(ctrl)}");
    }

    // =================================================================
    // Specific: Enter and Escape (AlwaysSameCode)
    // =================================================================

    [Fact]
    public void Mapper_Enter_AllVariants()
    {
        var normal = _mapper.MapKey(13, KeyModifiers.None, TerminalModes.None);
        var shift = _mapper.MapKey(13, KeyModifiers.Shift, TerminalModes.None);
        var ctrl = _mapper.MapKey(13, KeyModifiers.Ctrl, TerminalModes.None);

        Assert.Equal("\r", normal);
        Assert.Equal("\r", shift);
        Assert.Equal("\r", ctrl);
    }

    [Fact]
    public void Mapper_Escape_AllVariants()
    {
        var normal = _mapper.MapKey(27, KeyModifiers.None, TerminalModes.None);
        var shift = _mapper.MapKey(27, KeyModifiers.Shift, TerminalModes.None);
        var ctrl = _mapper.MapKey(27, KeyModifiers.Ctrl, TerminalModes.None);

        Assert.Equal("\x1b", normal);
        Assert.Equal("\x1b", shift);
        Assert.Equal("\x1b", ctrl);
    }

    // =================================================================
    // Summary: All editing/action/nav keys with all variants
    // =================================================================

    [Theory]
    // Editing keys
    [InlineData("G9", "MERK", "\x1B[00_", "\x1B[01_", "\x1B[00_")]
    [InlineData("G10", "FELT", "\x1B[02_", "\x1B[03_", "\x1B[02_")]
    [InlineData("G11", "AVSN", "\x1B[04_", "\x1B[05_", "\x1B[04_")]
    [InlineData("G12", "SETN", "\x1B[06_", "\x1B[07_", "\x1B[06_")]
    [InlineData("G13", "ORD", "\x1B[08_", "\x1B[09_", "\x1B[08_")]
    // Action keys
    [InlineData("G47", "STRYK", "\x1B[10_", "\x1B[11_", "\x1B[10_")]
    [InlineData("G48", "KOPI", "\x1B[12_", "\x1B[13_", "\x1B[12_")]
    [InlineData("G49", "FLYTT", "\x1B[14_", "\x1B[15_", "\x1B[14_")]
    // Tab area
    [InlineData("F47", "TAB", "\x1B[16_", "\x1B[17_", "\x1B[16_")]
    [InlineData("F48", "SEARCH", "\x1B[18_", "\x1B[19_", "\x1B[18_")]
    [InlineData("F49", "REPLACE", "\x1B[20_", "\x1B[21_", "\x1B[20_")]
    // Guillemets
    [InlineData("E47", "GUILLEMETS", "\x1B[22_", "\x1B[23_", "\x1B[22_")]
    [InlineData("E48", "JUST", "\x1B[24_", "\x1B[25_", "\x1B[24_")]
    [InlineData("E49", "SINGLEGUILLEMETS", "\x1B[26_", "\x1B[27_", "\x1B[26_")]
    // Navigation
    [InlineData("D47", "ROLLUP", "\x1B[28_", "\x1B[29_", "\x1B[28_")]
    [InlineData("D48", "ANGRE", "\x1B[30_", "\x1B[31_", "\x1B[30_")]
    [InlineData("D49", "ROLLDN", "\x1B[32_", "\x1B[33_", "\x1B[32_")]
    [InlineData("C47", "FIELDLEFT", "\x1B[34_", "\x1B[35_", "\x1B[34_")]
    [InlineData("C49", "FIELDRIGHT", "\x1B[36_", "\x1B[37_", "\x1B[36_")]
    [InlineData("A47", "TABLEFT", "\x1B[38_", "\x1B[39_", "\x1B[38_")]
    [InlineData("A49", "TABRIGHT", "\x1B[40_", "\x1B[41_", "\x1B[40_")]
    // System keys
    [InlineData("G51", "FUNK", "\x1B[42_", "\x1B[43_", "\x1B[42_")]
    [InlineData("G52", "SKRIV", "\x1B[44_", "\x1B[45_", "\x1B[44_")]
    [InlineData("G53", "HJELP", "\x1B[46_", "\x1B[47_", "\x1B[46_")]
    [InlineData("G54", "SLUTT", "\x1B[48_", "\x1B[49_", "\x1B[48_")]
    // F-keys
    [InlineData("F51", "F1", "\x1B[50_", "\x1B[51_", "\x1B[50_")]
    [InlineData("F52", "F2", "\x1B[52_", "\x1B[53_", "\x1B[54_")]  // Has Ctrl!
    [InlineData("F53", "F3", "\x1B[55_", "\x1B[56_", "\x1B[57_")]  // Has Ctrl!
    [InlineData("F54", "F4", "\x1B[58_", "\x1B[59_", "\x1B[58_")]
    [InlineData("E51", "F5", "\x1B[60_", "\x1B[61_", "\x1B[60_")]
    [InlineData("E52", "F6", "\x1B[62_", "\x1B[63_", "\x1B[62_")]
    [InlineData("E53", "F7", "\x1B[64_", "\x1B[65_", "\x1B[64_")]
    [InlineData("E54", "F8", "\x1B[66_", "\x1B[67_", "\x1B[66_")]
    // Other
    [InlineData("D99", "INNS", "\x1B[82_", "\x1B[83_", "\x1B[82_")]
    [InlineData("C99", "MODE", "\x1B[84_", "\x1B[85_", "\x1B[84_")]
    [InlineData("E13", "NEWPARA", "\x1B[86_", "\x1B[87_", "\x1B[86_")]
    public void Registry_EveryKey_NormalShiftCtrl(string grid, string name,
        string expectedNormal, string expectedShift, string expectedCtrl)
    {
        var normal = TDV2200KeyRegistry.GetSequence(grid, true, false, false, false);
        var shift = TDV2200KeyRegistry.GetSequence(grid, true, false, true, false);
        var ctrl = TDV2200KeyRegistry.GetSequence(grid, true, false, false, true);

        Assert.Equal(expectedNormal, normal);
        Assert.Equal(expectedShift, shift);
        Assert.Equal(expectedCtrl, ctrl);
        _output.WriteLine($"{grid} {name}: N={ToVisible(normal)}, S={ToVisible(shift)}, C={ToVisible(ctrl)}");
    }
}
