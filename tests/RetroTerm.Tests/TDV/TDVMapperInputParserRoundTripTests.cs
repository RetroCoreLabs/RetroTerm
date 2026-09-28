using System.Collections.Generic;
using System.Text;
using RetroTerm.Core.Protocols.TelnetServer.Parsing;
using RetroTerm.Core.Terminal.Emulators.TDV;
using RetroTerm.Core.Terminal.Input;
using Xunit;
using Xunit.Abstractions;

namespace RetroTerm.Tests.TDV;

/// <summary>
/// Round-trip test: every sequence produced by TDV2200KeyboardMapper
/// is fed through InputParser and must be correctly recognized with a TDV label.
/// Also verifies ALL registry sequences are recognized by InputParser.
/// </summary>
[Collection("TDVKeyBinding")]
public class TDVMapperInputParserRoundTripTests
{
    private readonly ITestOutputHelper _output;
    private readonly TDV2200KeyboardMapper _mapper;
    private readonly InputParser _parser;

    public TDVMapperInputParserRoundTripTests(ITestOutputHelper output)
    {
        _output = output;
        TDVKeyBindingConfiguration.ResetForTesting();
        _mapper = new TDV2200KeyboardMapper();
        _parser = new InputParser();
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
            else if (c == 0x7F) sb.Append("DEL");
            else sb.Append(c);
        }
        return sb.ToString();
    }

    /// <summary>
    /// Feed a sequence through InputParser and return the parsed result.
    /// </summary>
    private ParsedInput? FeedAndParse(string sequence)
    {
        _parser.Clear();
        _parser.Feed(sequence);
        if (_parser.TryGetNext(out var result))
            return result;
        return null;
    }

    // =================================================================
    // Round-trip: Every VK-mapped key through mapper → InputParser
    // Normal, Shift, Ctrl variants
    // =================================================================

    [Fact]
    public void Mapper_AllVKKeys_Normal_RecognizedByInputParser()
    {
        var allKeys = TDV2200KeyRegistry.AllKeys;
        var tested = 0;
        var failures = new List<string>();
        var sb = new StringBuilder();

        // AllKeys is a Dictionary, so there is no index to iterate by - enumerate it.
        foreach (var kvp in allKeys)
        {
            var grid = kvp.Key;
            var def = kvp.Value;

            if (def.VirtualKeyCode <= 0) continue;
            if ((def.Flags & TDVKeyFlags.IsModifier) != 0) continue;
            if ((def.Flags & TDVKeyFlags.IsToggle) != 0) continue;
            if ((def.Flags & TDVKeyFlags.IsProgrammable) != 0) continue;

            var actualGrid = TDV2200KeyRegistry.GetGridForVK(def.VirtualKeyCode);
            if (actualGrid == null) continue;

            // Skip ESC key — InputParser uses ESC as escape sequence start,
            // a lone ESC requires timeout to flush (not testable synchronously)
            if (def.VirtualKeyCode == 27) continue;

            // Normal
            var seq = _mapper.MapKey(def.VirtualKeyCode, KeyModifiers.None, TerminalModes.None);
            if (seq != null)
            {
                var parsed = FeedAndParse(seq);
                if (parsed == null)
                {
                    failures.Add($"  FAIL: VK {def.VirtualKeyCode} ({def.Name}) Normal → {ToVisible(seq)} — InputParser returned nothing");
                }
                else if (parsed.Value.Name == null)
                {
                    // C0 codes like CR/LF don't get a specific name, that's OK
                    if (parsed.Value.Type != InputType.Enter && parsed.Value.Type != InputType.Backspace)
                    {
                        failures.Add($"  FAIL: VK {def.VirtualKeyCode} ({def.Name}) Normal → {ToVisible(seq)} — InputParser has no name (type={parsed.Value.Type})");
                    }
                    else
                    {
                        sb.AppendLine($"  OK: VK {def.VirtualKeyCode} ({def.Name}) Normal → {ToVisible(seq)} → {parsed.Value.Type}");
                    }
                }
                else
                {
                    sb.AppendLine($"  OK: VK {def.VirtualKeyCode} ({def.Name}) Normal → {ToVisible(seq)} → \"{parsed.Value.Name}\"");
                }
                tested++;
            }

            // Shift
            seq = _mapper.MapKey(def.VirtualKeyCode, KeyModifiers.Shift, TerminalModes.None);
            if (seq != null)
            {
                var parsed = FeedAndParse(seq);
                if (parsed == null)
                {
                    failures.Add($"  FAIL: VK {def.VirtualKeyCode} ({def.Name}) Shift → {ToVisible(seq)} — InputParser returned nothing");
                }
                else if (parsed.Value.Name == null)
                {
                    if (parsed.Value.Type != InputType.Enter && parsed.Value.Type != InputType.Backspace)
                    {
                        failures.Add($"  FAIL: VK {def.VirtualKeyCode} ({def.Name}) Shift → {ToVisible(seq)} — InputParser has no name (type={parsed.Value.Type})");
                    }
                    else
                    {
                        sb.AppendLine($"  OK: VK {def.VirtualKeyCode} ({def.Name}) Shift → {ToVisible(seq)} → {parsed.Value.Type}");
                    }
                }
                else
                {
                    sb.AppendLine($"  OK: VK {def.VirtualKeyCode} ({def.Name}) Shift → {ToVisible(seq)} → \"{parsed.Value.Name}\"");
                }
                tested++;
            }

            // Ctrl
            seq = _mapper.MapKey(def.VirtualKeyCode, KeyModifiers.Ctrl, TerminalModes.None);
            if (seq != null)
            {
                var parsed = FeedAndParse(seq);
                if (parsed == null)
                {
                    failures.Add($"  FAIL: VK {def.VirtualKeyCode} ({def.Name}) Ctrl → {ToVisible(seq)} — InputParser returned nothing");
                }
                else if (parsed.Value.Name == null)
                {
                    if (parsed.Value.Type != InputType.Enter && parsed.Value.Type != InputType.Backspace)
                    {
                        failures.Add($"  FAIL: VK {def.VirtualKeyCode} ({def.Name}) Ctrl → {ToVisible(seq)} — InputParser has no name (type={parsed.Value.Type})");
                    }
                    else
                    {
                        sb.AppendLine($"  OK: VK {def.VirtualKeyCode} ({def.Name}) Ctrl → {ToVisible(seq)} → {parsed.Value.Type}");
                    }
                }
                else
                {
                    sb.AppendLine($"  OK: VK {def.VirtualKeyCode} ({def.Name}) Ctrl → {ToVisible(seq)} → \"{parsed.Value.Name}\"");
                }
                tested++;
            }
        }

        _output.WriteLine($"Mapper → InputParser: {tested} sequences tested");
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
    // Every registry CSI nn _ sequence recognized by InputParser
    // =================================================================

    [Fact]
    public void Registry_AllExtendedSequences_RecognizedByInputParser()
    {
        var allKeys = TDV2200KeyRegistry.AllKeys;
        var tested = 0;
        var failures = new List<string>();
        var sb = new StringBuilder();

        foreach (var kvp in allKeys)
        {
            var grid = kvp.Key;
            var def = kvp.Value;

            if ((def.Flags & TDVKeyFlags.IsModifier) != 0) continue;
            if ((def.Flags & TDVKeyFlags.IsToggle) != 0) continue;
            if ((def.Flags & TDVKeyFlags.IsProgrammable) != 0) continue;

            // Test ExtNormal
            if (def.ExtNormal != null && def.ExtNormal.Length > 1 && def.ExtNormal[0] == 0x1B)
            {
                var parsed = FeedAndParse(def.ExtNormal);
                if (parsed == null || parsed.Value.Name == null)
                {
                    failures.Add($"  FAIL: {grid} {def.Name} Normal {ToVisible(def.ExtNormal)} — not recognized by InputParser");
                }
                else if (!parsed.Value.Name.StartsWith("TDV"))
                {
                    failures.Add($"  FAIL: {grid} {def.Name} Normal {ToVisible(def.ExtNormal)} — recognized as \"{parsed.Value.Name}\" (not TDV-prefixed)");
                }
                else
                {
                    sb.AppendLine($"  OK: {grid} {def.Name} Normal {ToVisible(def.ExtNormal)} → \"{parsed.Value.Name}\"");
                }
                tested++;
            }

            // Test ExtShift
            if (def.ExtShift != null && def.ExtShift.Length > 1 && def.ExtShift[0] == 0x1B)
            {
                var parsed = FeedAndParse(def.ExtShift);
                if (parsed == null || parsed.Value.Name == null)
                {
                    failures.Add($"  FAIL: {grid} {def.Name} Shift {ToVisible(def.ExtShift)} — not recognized by InputParser");
                }
                else if (!parsed.Value.Name.StartsWith("TDV"))
                {
                    failures.Add($"  FAIL: {grid} {def.Name} Shift {ToVisible(def.ExtShift)} — recognized as \"{parsed.Value.Name}\" (not TDV-prefixed)");
                }
                else
                {
                    sb.AppendLine($"  OK: {grid} {def.Name} Shift {ToVisible(def.ExtShift)} → \"{parsed.Value.Name}\"");
                }
                tested++;
            }

            // Test ExtCtrl
            if (def.ExtCtrl != null && def.ExtCtrl.Length > 1 && def.ExtCtrl[0] == 0x1B)
            {
                var parsed = FeedAndParse(def.ExtCtrl);
                if (parsed == null || parsed.Value.Name == null)
                {
                    failures.Add($"  FAIL: {grid} {def.Name} Ctrl {ToVisible(def.ExtCtrl)} — not recognized by InputParser");
                }
                else if (!parsed.Value.Name.StartsWith("TDV"))
                {
                    failures.Add($"  FAIL: {grid} {def.Name} Ctrl {ToVisible(def.ExtCtrl)} — recognized as \"{parsed.Value.Name}\" (not TDV-prefixed)");
                }
                else
                {
                    sb.AppendLine($"  OK: {grid} {def.Name} Ctrl {ToVisible(def.ExtCtrl)} → \"{parsed.Value.Name}\"");
                }
                tested++;
            }
        }

        _output.WriteLine($"Registry → InputParser: {tested} escape sequences tested");
        _output.WriteLine(sb.ToString());
        if (failures.Count > 0)
        {
            _output.WriteLine($"\nFAILURES ({failures.Count}):");
            for (int i = 0; i < failures.Count; i++)
                _output.WriteLine(failures[i]);
        }
        Assert.True(tested > 50, $"Expected 50+ sequences, got {tested}");
        Assert.Empty(failures);
    }

    // =================================================================
    // Every registry C0 code recognized by InputParser with TDV label
    // =================================================================

    [Fact]
    public void Registry_AllC0Codes_RecognizedByInputParser()
    {
        var allKeys = TDV2200KeyRegistry.AllKeys;
        var tested = 0;
        var failures = new List<string>();
        var sb = new StringBuilder();

        foreach (var kvp in allKeys)
        {
            var grid = kvp.Key;
            var def = kvp.Value;

            if ((def.Flags & TDVKeyFlags.IsModifier) != 0) continue;
            if ((def.Flags & TDVKeyFlags.IsToggle) != 0) continue;
            if ((def.Flags & TDVKeyFlags.IsProgrammable) != 0) continue;

            // Test single-byte C0 codes (ExtNormal is single byte < 0x20 or 0x7F)
            if (def.ExtNormal != null && def.ExtNormal.Length == 1)
            {
                byte b = (byte)def.ExtNormal[0];
                // Skip CR (0x0D) and LF (0x0A) — InputParser maps these to Enter type, no name
                if (b == 0x0D || b == 0x0A) continue;
                // Skip ESC (0x1B) — InputParser starts escape sequence parsing
                if (b == 0x1B) continue;

                var parsed = FeedAndParse(def.ExtNormal);
                if (parsed == null)
                {
                    failures.Add($"  FAIL: {grid} {def.Name} C0={b:X2} — InputParser returned nothing");
                }
                else if (parsed.Value.Name != null && parsed.Value.Name.Contains("TDV"))
                {
                    sb.AppendLine($"  OK: {grid} {def.Name} C0={b:X2} → \"{parsed.Value.Name}\"");
                }
                else if (b == 0x08 || b == 0x7F || b == 0x09)
                {
                    // Backspace/DEL/Tab have special handling in InputParser, name may not start with TDV
                    sb.AppendLine($"  OK: {grid} {def.Name} C0={b:X2} → type={parsed.Value.Type} name=\"{parsed.Value.Name ?? "(none)"}\"");
                }
                else
                {
                    failures.Add($"  FAIL: {grid} {def.Name} C0={b:X2} → name=\"{parsed.Value.Name ?? "(none)"}\" (missing TDV label)");
                }
                tested++;
            }
        }

        _output.WriteLine($"Registry C0 → InputParser: {tested} C0 codes tested");
        _output.WriteLine(sb.ToString());
        if (failures.Count > 0)
        {
            _output.WriteLine($"\nFAILURES ({failures.Count}):");
            for (int i = 0; i < failures.Count; i++)
                _output.WriteLine(failures[i]);
        }
        Assert.True(tested > 5, $"Expected 5+ C0 codes, got {tested}");
        Assert.Empty(failures);
    }

    // =================================================================
    // Registry numpad function mode sequences recognized by InputParser
    // =================================================================

    [Fact]
    public void Registry_AllNumpadSequences_RecognizedByInputParser()
    {
        var allKeys = TDV2200KeyRegistry.AllKeys;
        var tested = 0;
        var failures = new List<string>();
        var sb = new StringBuilder();

        foreach (var kvp in allKeys)
        {
            var grid = kvp.Key;
            var def = kvp.Value;

            if ((def.Flags & TDVKeyFlags.IsNumericPad) == 0) continue;

            // NumPadFunc is the function-mode sequence (CSI nn _)
            if (def.NumPadFunc != null && def.NumPadFunc.Length > 1 && def.NumPadFunc[0] == 0x1B)
            {
                var parsed = FeedAndParse(def.NumPadFunc);
                if (parsed == null || parsed.Value.Name == null)
                {
                    failures.Add($"  FAIL: {grid} {def.Name} NumPadFunc {ToVisible(def.NumPadFunc)} — not recognized");
                }
                else if (!parsed.Value.Name.StartsWith("TDV"))
                {
                    failures.Add($"  FAIL: {grid} {def.Name} NumPadFunc {ToVisible(def.NumPadFunc)} — \"{parsed.Value.Name}\" (not TDV-prefixed)");
                }
                else
                {
                    sb.AppendLine($"  OK: {grid} {def.Name} NumPadFunc {ToVisible(def.NumPadFunc)} → \"{parsed.Value.Name}\"");
                }
                tested++;
            }
        }

        _output.WriteLine($"Numpad → InputParser: {tested} numpad sequences tested");
        _output.WriteLine(sb.ToString());
        if (failures.Count > 0)
        {
            _output.WriteLine($"\nFAILURES ({failures.Count}):");
            for (int i = 0; i < failures.Count; i++)
                _output.WriteLine(failures[i]);
        }
        Assert.True(tested > 10, $"Expected 10+ numpad sequences, got {tested}");
        Assert.Empty(failures);
    }

    // =================================================================
    // PUSH key sequences (DCS) recognized by InputParser
    // =================================================================

    [Theory]
    [InlineData(1, false, "TDV PUSH1")]
    [InlineData(2, false, "TDV PUSH2")]
    [InlineData(3, false, "TDV PUSH3")]
    [InlineData(4, false, "TDV PUSH4")]
    [InlineData(5, false, "TDV PUSH5")]
    [InlineData(6, false, "TDV PUSH6")]
    [InlineData(7, false, "TDV PUSH7")]
    [InlineData(8, false, "TDV PUSH8")]
    [InlineData(1, true, "TDV Shift+PUSH1")]
    [InlineData(2, true, "TDV Shift+PUSH2")]
    [InlineData(3, true, "TDV Shift+PUSH3")]
    [InlineData(4, true, "TDV Shift+PUSH4")]
    [InlineData(5, true, "TDV Shift+PUSH5")]
    [InlineData(6, true, "TDV Shift+PUSH6")]
    [InlineData(7, true, "TDV Shift+PUSH7")]
    [InlineData(8, true, "TDV Shift+PUSH8")]
    public void PushKeys_RecognizedByInputParser(int num, bool shifted, string expectedName)
    {
        var flag = shifted ? "S" : "N";
        var seq = $"\x1bP{flag}{num}\x1b\\";
        var parsed = FeedAndParse(seq);
        Assert.NotNull(parsed);
        Assert.Equal(expectedName, parsed.Value.Name);
    }

    // =================================================================
    // End key (VT220 fallback) recognized by InputParser
    // =================================================================

    [Fact]
    public void EndKey_SendsSLUTT_RecognizedByInputParser()
    {
        var seq = _mapper.MapKey(35, KeyModifiers.None, TerminalModes.None);
        Assert.NotNull(seq);
        Assert.Equal("\x1b[48_", seq);

        var parsed = FeedAndParse(seq);
        Assert.NotNull(parsed);
        Assert.Equal("TDV SLUTT", parsed.Value.Name);
        _output.WriteLine($"End key → {ToVisible(seq)} → \"{parsed.Value.Name}\"");
    }
}
