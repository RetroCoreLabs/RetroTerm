using System.Collections.Generic;
using Avalonia.Headless.XUnit;
using RetroTerm.Core.Terminal.Emulators.TDV;
using RetroTerm.Core.Terminal.Input;
using RetroTerm.Desktop.Models;
using Xunit;

namespace RetroTerm.Tests.Avalonia;

/// <summary>
/// Tests VirtualKeyboardPanel UI component to verify it sends correct
/// escape sequences when virtual keys are clicked.
/// This tests the full UI flow: click -> OnKeyPressed -> MapKey -> InputReceived
/// </summary>
[Collection("Avalonia")]
public class VirtualKeyboardPanelTests
{
    private readonly ITestOutputHelper _output;

    public VirtualKeyboardPanelTests(ITestOutputHelper output)
    {
        _output = output;
    }

    /// <summary>
    /// Helper to simulate key press and capture the sequence sent.
    /// Uses TDVKeyVisualMetadata + optional labels instead of KeyDefinition.
    /// </summary>
    private static string? SimulateKeyPress(TDVKeyVisualMetadata meta,
        Dictionary<string, KeyLabel>? labels = null, KeyModifiers modifiers = KeyModifiers.None)
    {
        string? sequence = null;

        // First, try to map by TDV key name for special keys (replicating VirtualKeyboardPanel logic)
        var tdvKeyName = MapGridPositionToTDVKeyName(meta.GridPosition, meta.Category);
        if (!string.IsNullOrEmpty(tdvKeyName))
        {
            var tdvMapper = new TDVKeyboardMapper();
            bool shift = modifiers.HasFlag(KeyModifiers.Shift);
            bool ctrl = modifiers.HasFlag(KeyModifiers.Ctrl);
            bool alt = modifiers.HasFlag(KeyModifiers.Alt);
            sequence = tdvMapper.MapKey(tdvKeyName, shift, ctrl, alt);
        }

        // Fall back to VK code-based mapper for standard keys
        if (string.IsNullOrEmpty(sequence) && meta.VirtualKeyCode > 0)
        {
            var mapper = new TDV2200KeyboardMapper();
            var terminalModes = TerminalModes.None;
            sequence = mapper.MapKey(meta.VirtualKeyCode, modifiers, terminalModes);
        }

        // Handle normal character keys (letters, numbers, symbols)
        if (string.IsNullOrEmpty(sequence) && meta.Category == KeyCategory.Alphanumeric)
        {
            if (labels != null && labels.TryGetValue("no", out var label))
            {
                bool shift = modifiers.HasFlag(KeyModifiers.Shift);
                var charLabel = shift && !string.IsNullOrEmpty(label.Shifted) ? label.Shifted : label.Primary;
                if (!string.IsNullOrEmpty(charLabel) && charLabel.Length == 1)
                {
                    sequence = charLabel;
                }
            }
        }

        return sequence;
    }

    /// <summary>
    /// Replicates the grid position to TDV key name mapping from VirtualKeyboardPanel
    /// </summary>
    private static string? MapGridPositionToTDVKeyName(string gridPosition, KeyCategory category)
    {
        if (category != KeyCategory.ApplicationControl && category != KeyCategory.Function && category != KeyCategory.PushKey)
            return null;

        return gridPosition switch
        {
            "G1" => "PUSH1",
            "G2" => "PUSH2",
            "G3" => "PUSH3",
            "G4" => "PUSH4",
            "G5" => "PUSH5",
            "G6" => "PUSH6",
            "G7" => "PUSH7",
            "G8" => "PUSH8",
            "G9" => "MARK",
            "G10" => "FIELD",
            "G11" => "PARA",
            "G12" => "SENT",
            "G13" => "WORD",
            "G48" => "COPY",
            "G49" => "MOVE",
            "G51" => "FUNC",
            "G52" => "PRINT",
            "G53" => "HELP",
            "G54" => "EXIT",
            "C47" => "ERASE_PAGE",
            "C48" => "ERASE_LINE",
            _ => null
        };
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
            ["no"] = new KeyLabel(primary, shifted, null, null)
        };
    }

    #region PUSH Key Tests (G1-G8)

    [AvaloniaFact]
    public void VirtualKeyboard_Push1Key_ShouldSendCorrectSequence()
    {
        var meta = MakeKey("G1", KeyCategory.PushKey);
        var sequence = SimulateKeyPress(meta);

        Assert.NotNull(sequence);
        Assert.Equal("\x1b[?1~", sequence);
        _output.WriteLine($"PUSH1 (G1) -> {ToVisibleString(sequence)}");
    }

    [AvaloniaFact]
    public void VirtualKeyboard_Push8Key_ShouldSendCorrectSequence()
    {
        var meta = MakeKey("G8", KeyCategory.PushKey);
        var sequence = SimulateKeyPress(meta);

        Assert.NotNull(sequence);
        Assert.Equal("\x1b[?8~", sequence);
        _output.WriteLine($"PUSH8 (G8) -> {ToVisibleString(sequence)}");
    }

    [AvaloniaTheory]
    [InlineData("G1", "\x1b[?1~")]
    [InlineData("G2", "\x1b[?2~")]
    [InlineData("G3", "\x1b[?3~")]
    [InlineData("G4", "\x1b[?4~")]
    [InlineData("G5", "\x1b[?5~")]
    [InlineData("G6", "\x1b[?6~")]
    [InlineData("G7", "\x1b[?7~")]
    [InlineData("G8", "\x1b[?8~")]
    public void VirtualKeyboard_AllPushKeys_ShouldSendCorrectSequences(string gridPos, string expected)
    {
        var meta = MakeKey(gridPos, KeyCategory.PushKey);
        var sequence = SimulateKeyPress(meta);

        Assert.NotNull(sequence);
        Assert.Equal(expected, sequence);
        _output.WriteLine($"{gridPos} -> {ToVisibleString(sequence)}");
    }

    #endregion

    #region TDV Special Key Tests

    [AvaloniaFact]
    public void VirtualKeyboard_HelpKey_ShouldSendCorrectSequence()
    {
        var meta = MakeKey("G53", KeyCategory.ApplicationControl, vk: 112);
        var sequence = SimulateKeyPress(meta);

        Assert.NotNull(sequence);
        Assert.Equal("\x1b[28~", sequence);
        _output.WriteLine($"HELP (G53) -> {ToVisibleString(sequence)}");
    }

    [AvaloniaFact]
    public void VirtualKeyboard_FuncKey_ShouldSendCorrectSequence()
    {
        var meta = MakeKey("G51", KeyCategory.ApplicationControl);
        var sequence = SimulateKeyPress(meta);

        Assert.NotNull(sequence);
        Assert.Equal("\x1b[@", sequence);
        _output.WriteLine($"FUNC (G51) -> {ToVisibleString(sequence)}");
    }

    [AvaloniaFact]
    public void VirtualKeyboard_CopyKey_ShouldSendCorrectSequence()
    {
        var meta = MakeKey("G48", KeyCategory.ApplicationControl, vk: 67);
        var sequence = SimulateKeyPress(meta);

        Assert.NotNull(sequence);
        Assert.Equal("\x1b[M", sequence);
        _output.WriteLine($"COPY (G48) -> {ToVisibleString(sequence)}");
    }

    [AvaloniaFact]
    public void VirtualKeyboard_MoveKey_ShouldSendCorrectSequence()
    {
        var meta = MakeKey("G49", KeyCategory.ApplicationControl, vk: 88);
        var sequence = SimulateKeyPress(meta);

        Assert.NotNull(sequence);
        Assert.Equal("\x1b[N", sequence);
        _output.WriteLine($"MOVE (G49) -> {ToVisibleString(sequence)}");
    }

    [AvaloniaFact]
    public void VirtualKeyboard_ExitKey_ShouldSendCorrectSequence()
    {
        var meta = MakeKey("G54", KeyCategory.ApplicationControl);
        var sequence = SimulateKeyPress(meta);

        Assert.NotNull(sequence);
        Assert.Equal("\x1b[C", sequence);
        _output.WriteLine($"EXIT (G54) -> {ToVisibleString(sequence)}");
    }

    [AvaloniaFact]
    public void VirtualKeyboard_PrintKey_ShouldSendCorrectSequence()
    {
        var meta = MakeKey("G52", KeyCategory.ApplicationControl, vk: 44);
        var sequence = SimulateKeyPress(meta);

        Assert.NotNull(sequence);
        Assert.Equal("\x1b[A", sequence);
        _output.WriteLine($"PRINT (G52) -> {ToVisibleString(sequence)}");
    }

    #endregion

    #region Application Key Tests (G9-G13)

    [AvaloniaFact]
    public void VirtualKeyboard_MarkKey_ShouldSendCorrectSequence()
    {
        var meta = MakeKey("G9", KeyCategory.Function);
        var sequence = SimulateKeyPress(meta);

        Assert.NotNull(sequence);
        Assert.Equal("\x1b[X", sequence);
        _output.WriteLine($"MARK (G9) -> {ToVisibleString(sequence)}");
    }

    [AvaloniaFact]
    public void VirtualKeyboard_FieldKey_ShouldSendCorrectSequence()
    {
        var meta = MakeKey("G10", KeyCategory.Function);
        var sequence = SimulateKeyPress(meta);

        Assert.NotNull(sequence);
        Assert.Equal("\x1b[Y", sequence);
        _output.WriteLine($"FIELD (G10) -> {ToVisibleString(sequence)}");
    }

    [AvaloniaFact]
    public void VirtualKeyboard_ParaKey_ShouldSendCorrectSequence()
    {
        var meta = MakeKey("G11", KeyCategory.Function);
        var sequence = SimulateKeyPress(meta);

        Assert.NotNull(sequence);
        Assert.Equal("\x1b[Z", sequence);
        _output.WriteLine($"PARA (G11) -> {ToVisibleString(sequence)}");
    }

    [AvaloniaFact]
    public void VirtualKeyboard_SentKey_ShouldSendCorrectSequence()
    {
        var meta = MakeKey("G12", KeyCategory.Function);
        var sequence = SimulateKeyPress(meta);

        Assert.NotNull(sequence);
        Assert.Equal("\x1b[[", sequence);
        _output.WriteLine($"SENT (G12) -> {ToVisibleString(sequence)}");
    }

    [AvaloniaFact]
    public void VirtualKeyboard_WordKey_ShouldSendCorrectSequence()
    {
        var meta = MakeKey("G13", KeyCategory.Function);
        var sequence = SimulateKeyPress(meta);

        Assert.NotNull(sequence);
        Assert.Equal("\x1b[\\", sequence);
        _output.WriteLine($"WORD (G13) -> {ToVisibleString(sequence)}");
    }

    #endregion

    #region Arrow Key Tests (using VK codes)

    [AvaloniaFact]
    public void VirtualKeyboard_ArrowUp_ShouldSendCorrectSequence()
    {
        var meta = MakeKey("B48", KeyCategory.Navigation, vk: 38);
        var sequence = SimulateKeyPress(meta);

        Assert.NotNull(sequence);
        Assert.Equal("\x1c", sequence); // TDV-native C0 FS code (AlwaysSameCode)
        _output.WriteLine($"Arrow Up (VK38) -> {ToVisibleString(sequence)}");
    }

    [AvaloniaFact]
    public void VirtualKeyboard_ArrowDown_ShouldSendCorrectSequence()
    {
        var meta = MakeKey("A48", KeyCategory.Navigation, vk: 40);
        var sequence = SimulateKeyPress(meta);

        Assert.NotNull(sequence);
        Assert.Equal("\x0b", sequence); // TDV-native C0 VT code (AlwaysSameCode)
        _output.WriteLine($"Arrow Down (VK40) -> {ToVisibleString(sequence)}");
    }

    [AvaloniaFact]
    public void VirtualKeyboard_ArrowLeft_ShouldSendCorrectSequence()
    {
        var meta = MakeKey("B47", KeyCategory.Navigation, vk: 37);
        var sequence = SimulateKeyPress(meta);

        Assert.NotNull(sequence);
        Assert.Equal("\x08", sequence); // TDV-native C0 BS code (AlwaysSameCode)
        _output.WriteLine($"Arrow Left (VK37) -> {ToVisibleString(sequence)}");
    }

    [AvaloniaFact]
    public void VirtualKeyboard_ArrowRight_ShouldSendCorrectSequence()
    {
        var meta = MakeKey("B49", KeyCategory.Navigation, vk: 39);
        var sequence = SimulateKeyPress(meta);

        Assert.NotNull(sequence);
        Assert.Equal("\x18", sequence); // TDV-native C0 CAN code (AlwaysSameCode)
        _output.WriteLine($"Arrow Right (VK39) -> {ToVisibleString(sequence)}");
    }

    #endregion

    #region Normal Character Key Tests

    [AvaloniaFact]
    public void VirtualKeyboard_LetterA_ShouldSendCharacter()
    {
        var meta = MakeKey("C1", KeyCategory.Alphanumeric, vk: 65);
        var labels = MakeLabels("A", "a");
        var sequence = SimulateKeyPress(meta, labels);

        Assert.NotNull(sequence);
        Assert.Equal("A", sequence);
        _output.WriteLine($"Letter A -> '{sequence}'");
    }

    [AvaloniaFact]
    public void VirtualKeyboard_LetterA_WithShift_ShouldSendLowercase()
    {
        var meta = MakeKey("C1", KeyCategory.Alphanumeric, vk: 65);
        var labels = MakeLabels("A", "a");
        var sequence = SimulateKeyPress(meta, labels, KeyModifiers.Shift);

        Assert.NotNull(sequence);
        Assert.Equal("a", sequence);
        _output.WriteLine($"Shift+Letter A -> '{sequence}'");
    }

    [AvaloniaFact]
    public void VirtualKeyboard_NorwegianAE_ShouldSendCharacter()
    {
        var meta = MakeKey("C11", KeyCategory.Alphanumeric);
        var labels = MakeLabels("Æ", "æ");
        var sequence = SimulateKeyPress(meta, labels);

        Assert.NotNull(sequence);
        Assert.Equal("Æ", sequence);
        _output.WriteLine($"Norwegian Æ -> '{sequence}'");
    }

    [AvaloniaFact]
    public void VirtualKeyboard_NorwegianOSlash_ShouldSendCharacter()
    {
        var meta = MakeKey("C10", KeyCategory.Alphanumeric);
        var labels = MakeLabels("Ø", "ø");
        var sequence = SimulateKeyPress(meta, labels);

        Assert.NotNull(sequence);
        Assert.Equal("Ø", sequence);
        _output.WriteLine($"Norwegian Ø -> '{sequence}'");
    }

    #endregion

    #region Control Key Tests

    [AvaloniaFact]
    public void VirtualKeyboard_Enter_ShouldSendCR()
    {
        var meta = MakeKey("C13", KeyCategory.System, vk: 13);
        var sequence = SimulateKeyPress(meta);

        Assert.NotNull(sequence);
        Assert.Equal("\r", sequence);
        _output.WriteLine($"Enter -> {ToVisibleString(sequence)}");
    }

    [AvaloniaFact]
    public void VirtualKeyboard_Tab_ShouldSendTab()
    {
        var meta = MakeKey("D0", KeyCategory.System, vk: 9);
        var sequence = SimulateKeyPress(meta);

        Assert.NotNull(sequence);
        Assert.Equal("\x1B[16_", sequence); // TDV TAB key (Extended Control Mode CSI sequence)
        _output.WriteLine($"Tab -> {ToVisibleString(sequence)}");
    }

    [AvaloniaFact]
    public void VirtualKeyboard_Escape_ShouldSendESC()
    {
        var meta = MakeKey("G0", KeyCategory.System, vk: 27);
        var sequence = SimulateKeyPress(meta);

        Assert.NotNull(sequence);
        Assert.Equal("\x1b", sequence);
        _output.WriteLine($"Escape -> {ToVisibleString(sequence)}");
    }

    [AvaloniaFact]
    public void VirtualKeyboard_Backspace_ShouldSendBS()
    {
        var meta = MakeKey("E13", KeyCategory.System, vk: 8);
        var sequence = SimulateKeyPress(meta);

        Assert.NotNull(sequence);
        Assert.Equal("\x08", sequence);
        _output.WriteLine($"Backspace -> {ToVisibleString(sequence)}");
    }

    [AvaloniaFact]
    public void VirtualKeyboard_Space_ShouldSendSpace()
    {
        // Space is treated as Alphanumeric for character label handling in virtual keyboard
        var meta = MakeKey("A5", KeyCategory.Alphanumeric, vk: 32);
        var labels = MakeLabels(" ");
        var sequence = SimulateKeyPress(meta, labels);

        Assert.NotNull(sequence);
        Assert.Equal(" ", sequence);
        _output.WriteLine($"Space -> ' '");
    }

    #endregion

    #region Modifier Key Combination Tests

    [AvaloniaFact]
    public void VirtualKeyboard_ShiftArrowUp_ShouldSendModifiedSequence()
    {
        var meta = MakeKey("B48", KeyCategory.Navigation, vk: 38);
        var sequence = SimulateKeyPress(meta, modifiers: KeyModifiers.Shift);

        Assert.NotNull(sequence);
        Assert.Equal("\x1c", sequence); // TDV-native: same C0 FS code (AlwaysSameCode, shift ignored)
        _output.WriteLine($"Shift+Arrow Up -> {ToVisibleString(sequence)}");
    }

    [AvaloniaFact]
    public void VirtualKeyboard_CtrlArrowRight_ShouldSendModifiedSequence()
    {
        var meta = MakeKey("B49", KeyCategory.Navigation, vk: 39);
        var sequence = SimulateKeyPress(meta, modifiers: KeyModifiers.Ctrl);

        Assert.NotNull(sequence);
        Assert.Equal("\x18", sequence); // TDV-native: same C0 CAN code (AlwaysSameCode, ctrl ignored)
        _output.WriteLine($"Ctrl+Arrow Right -> {ToVisibleString(sequence)}");
    }

    #endregion

    /// <summary>
    /// Convert escape sequences to visible string representation
    /// </summary>
    private static string ToVisibleString(string? s)
    {
        if (string.IsNullOrEmpty(s)) return "(null)";

        var result = new System.Text.StringBuilder();
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            if (c == 0x1b)
                result.Append("ESC");
            else if (c < 0x20)
                result.Append($"<{(int)c:X2}>");
            else
                result.Append(c);
        }
        return result.ToString();
    }
}
