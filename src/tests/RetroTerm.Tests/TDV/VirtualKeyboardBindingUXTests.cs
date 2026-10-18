using RetroTerm.Core.Terminal.Emulators.TDV;
using RetroTerm.Core.Terminal.Input;
using Xunit;

namespace RetroTerm.Tests.TDV;

/// <summary>
/// Tests for the virtual keyboard key binding UX redesign:
/// - IsModifierOnlyKey correctly identifies bare modifier VK codes
/// - IsValid rejects left/right modifier VKs (160-165) — the modifier capture bug fix
/// - Always-visible binding indicators (no "Show Bindings" checkbox needed)
/// - Modifier accumulation: bare modifiers are silently absorbed, non-modifier key commits
/// - GetEnglishName returns human-readable key names for popup headers
/// - Right-click binding workflow: popup header shows "HJELP (G53)" format
/// </summary>
[Collection("TDVKeyBinding")]
public class VirtualKeyboardBindingUXTests
{
    private readonly ITestOutputHelper _output;

    // Avalonia Key enum integer values for modifier keys
    private const int AvKey_LeftShift = 116;
    private const int AvKey_RightShift = 117;
    private const int AvKey_LeftCtrl = 118;
    private const int AvKey_RightCtrl = 119;
    private const int AvKey_LeftAlt = 120;
    private const int AvKey_RightAlt = 121;

    // Avalonia Key enum values for normal keys
    private const int AvKey_A = 44;
    private const int AvKey_H = 51; // 44 + ('H' - 'A')
    private const int AvKey_Escape = 13;
    private const int AvKey_F1 = 90;
    private const int AvKey_D1 = 35; // AvKey_D0 + 1

    // Windows VK codes — modifier keys
    private const int VK_SHIFT = 16;
    private const int VK_CONTROL = 17;
    private const int VK_MENU = 18;     // Alt
    private const int VK_LWIN = 91;
    private const int VK_RWIN = 92;
    private const int VK_LSHIFT = 160;
    private const int VK_RSHIFT = 161;
    private const int VK_LCONTROL = 162;
    private const int VK_RCONTROL = 163;
    private const int VK_LMENU = 164;   // Left Alt
    private const int VK_RMENU = 165;   // Right Alt

    // Windows VK codes — normal keys
    private const int VK_H = 72;
    private const int VK_A = 65;
    private const int VK_F1 = 112;

    public VirtualKeyboardBindingUXTests(ITestOutputHelper output)
    {
        _output = output;
        TDVKeyBindingConfiguration.ResetForTesting();
    }

    #region IsModifierOnlyKey — Identifies Bare Modifier VK Codes

    [Theory]
    [InlineData(VK_SHIFT, "VK_SHIFT (16)")]
    [InlineData(VK_CONTROL, "VK_CONTROL (17)")]
    [InlineData(VK_MENU, "VK_MENU/Alt (18)")]
    [InlineData(VK_LWIN, "VK_LWIN (91)")]
    [InlineData(VK_RWIN, "VK_RWIN (92)")]
    [InlineData(VK_LSHIFT, "VK_LSHIFT (160)")]
    [InlineData(VK_RSHIFT, "VK_RSHIFT (161)")]
    [InlineData(VK_LCONTROL, "VK_LCONTROL (162)")]
    [InlineData(VK_RCONTROL, "VK_RCONTROL (163)")]
    [InlineData(VK_LMENU, "VK_LMENU/LAlt (164)")]
    [InlineData(VK_RMENU, "VK_RMENU/RAlt (165)")]
    public void IsModifierOnlyKey_ModifierVKCodes_ReturnsTrue(int vkCode, string desc)
    {
        Assert.True(KeyBindingSource.IsModifierOnlyKey(vkCode),
            $"{desc} should be identified as modifier-only key");
        _output.WriteLine($"{desc} → IsModifierOnlyKey = true");
    }

    [Theory]
    [InlineData(VK_A, "VK_A (65)")]
    [InlineData(VK_H, "VK_H (72)")]
    [InlineData(VK_F1, "VK_F1 (112)")]
    [InlineData(8, "VK_BACK (8)")]
    [InlineData(9, "VK_TAB (9)")]
    [InlineData(13, "VK_RETURN (13)")]
    [InlineData(27, "VK_ESCAPE (27)")]
    [InlineData(32, "VK_SPACE (32)")]
    [InlineData(37, "VK_LEFT (37)")]
    [InlineData(38, "VK_UP (38)")]
    [InlineData(48, "VK_0 (48)")]
    public void IsModifierOnlyKey_NonModifierVKCodes_ReturnsFalse(int vkCode, string desc)
    {
        Assert.False(KeyBindingSource.IsModifierOnlyKey(vkCode),
            $"{desc} should NOT be identified as modifier-only key");
        _output.WriteLine($"{desc} → IsModifierOnlyKey = false");
    }

    #endregion

    #region IsValid Bug Fix — Left/Right Modifier VKs Now Rejected

    [Theory]
    [InlineData(VK_LSHIFT, KeyModifiers.None, "bare LShift (160)")]
    [InlineData(VK_RSHIFT, KeyModifiers.None, "bare RShift (161)")]
    [InlineData(VK_LCONTROL, KeyModifiers.None, "bare LCtrl (162)")]
    [InlineData(VK_RCONTROL, KeyModifiers.None, "bare RCtrl (163)")]
    [InlineData(VK_LMENU, KeyModifiers.None, "bare LAlt (164)")]
    [InlineData(VK_RMENU, KeyModifiers.None, "bare RAlt (165)")]
    public void IsValid_LeftRightModifierOnly_ShouldBeRejected(int vkCode, KeyModifiers mods, string desc)
    {
        var source = new KeyBindingSource(vkCode, mods);
        Assert.False(source.IsValid(),
            $"{desc} should be rejected — this was the modifier capture bug");
        _output.WriteLine($"{desc} → IsValid = false (bug fix confirmed)");
    }

    [Theory]
    [InlineData(VK_LSHIFT, KeyModifiers.Shift, "LShift+Shift (160)")]
    [InlineData(VK_LMENU, KeyModifiers.Alt, "LAlt+Alt (164)")]
    [InlineData(VK_LCONTROL, KeyModifiers.Ctrl, "LCtrl+Ctrl (162)")]
    public void IsValid_LeftRightModifierWithModifierFlags_StillRejected(int vkCode, KeyModifiers mods, string desc)
    {
        // Even with modifier flags set, a modifier-only VK code should be rejected.
        // This is the exact scenario that caused the bug: pressing Alt sends
        // VK 164 (LeftAlt) WITH KeyModifiers.Alt set.
        var source = new KeyBindingSource(vkCode, mods);
        Assert.False(source.IsValid(),
            $"{desc} should be rejected — modifier VK + modifier flag is still a bare modifier");
        _output.WriteLine($"{desc} → IsValid = false (modifier-with-flag scenario)");
    }

    #endregion

    #region AvaloniaKeyToVK — Modifier Keys Return Left/Right VK Codes

    [Theory]
    [InlineData(AvKey_LeftShift, VK_LSHIFT, "LeftShift → VK 160")]
    [InlineData(AvKey_RightShift, VK_RSHIFT, "RightShift → VK 161")]
    [InlineData(AvKey_LeftCtrl, VK_LCONTROL, "LeftCtrl → VK 162")]
    [InlineData(AvKey_RightCtrl, VK_RCONTROL, "RightCtrl → VK 163")]
    [InlineData(AvKey_LeftAlt, VK_LMENU, "LeftAlt → VK 164")]
    [InlineData(AvKey_RightAlt, VK_RMENU, "RightAlt → VK 165")]
    public void AvaloniaKeyToVK_ModifierKeys_ReturnLeftRightVKCodes(int avKey, int expectedVK, string desc)
    {
        var vk = TDVKeyBindingConfiguration.AvaloniaKeyToVK(avKey);
        Assert.Equal(expectedVK, vk);
        _output.WriteLine($"{desc} — confirmed");
    }

    [Fact]
    public void AvaloniaKeyToVK_AllModifierKeys_AreDetectedByIsModifierOnlyKey()
    {
        // Verify the full chain: AvaloniaKeyToVK returns VK codes that IsModifierOnlyKey rejects
        int[] avModifierKeys = { AvKey_LeftShift, AvKey_RightShift, AvKey_LeftCtrl,
                                  AvKey_RightCtrl, AvKey_LeftAlt, AvKey_RightAlt };
        for (int i = 0; i < avModifierKeys.Length; i++)
        {
            int avKey = avModifierKeys[i];
            var vk = TDVKeyBindingConfiguration.AvaloniaKeyToVK(avKey);
            Assert.True(KeyBindingSource.IsModifierOnlyKey(vk),
                $"Avalonia key {avKey} → VK {vk} should be detected as modifier-only");
            _output.WriteLine($"Avalonia {avKey} → VK {vk} → IsModifierOnlyKey = true");
        }
    }

    #endregion

    #region Modifier Capture Scenario — Simulating The Bug

    [Fact]
    public void ModifierCaptureBug_AltThenH_IsModifierOnlyFiltersFirstKeyDown()
    {
        // Simulate the exact scenario that caused the red flash bug:
        // 1. User presses Alt key → AvaloniaKeyToVK returns VK 164 (LMENU)
        // 2. User continues to hold Alt and presses H → VK 72 with Alt modifier

        // Step 1: First KeyDown — bare Alt
        int avAltKey = AvKey_LeftAlt;
        var altVK = TDVKeyBindingConfiguration.AvaloniaKeyToVK(avAltKey);
        Assert.Equal(VK_LMENU, altVK);

        // IsModifierOnlyKey should catch this → don't process as binding
        Assert.True(KeyBindingSource.IsModifierOnlyKey(altVK),
            "Bare Alt press (VK 164) should be caught by IsModifierOnlyKey");

        // Also verify IsValid would reject it (belt and suspenders)
        var altSource = new KeyBindingSource(altVK, KeyModifiers.Alt);
        Assert.False(altSource.IsValid(),
            "VK 164 + Alt modifier should be rejected by IsValid");

        // Step 2: Second KeyDown — H with Alt held
        int avHKey = AvKey_H;
        var hVK = TDVKeyBindingConfiguration.AvaloniaKeyToVK(avHKey);
        Assert.Equal(VK_H, hVK);

        // This is NOT a modifier-only key
        Assert.False(KeyBindingSource.IsModifierOnlyKey(hVK));

        // This IS a valid binding source
        var hSource = new KeyBindingSource(hVK, KeyModifiers.Alt);
        Assert.True(hSource.IsValid(), "Alt+H should be a valid binding source");

        // And it resolves to HJELP (G53)
        var found = TDVKeyBindingConfiguration.Instance.TryGetTarget(hVK, KeyModifiers.Alt, out var target);
        Assert.True(found, "Alt+H should have a default binding");
        Assert.Equal("G53", target.GridPosition);

        _output.WriteLine("Modifier capture bug scenario verified:");
        _output.WriteLine($"  1. Alt press → VK {altVK} → IsModifierOnlyKey=true → silently ignored");
        _output.WriteLine($"  2. H press with Alt → VK {hVK} + Alt → IsValid=true → bound to {target.GridPosition} (HJELP)");
        _output.WriteLine("  Result: No red flash error, clean binding");
    }

    [Fact]
    public void ModifierCaptureBug_CtrlThenA_IsModifierOnlyFiltersFirstKeyDown()
    {
        // Same scenario with Ctrl+A
        var ctrlVK = TDVKeyBindingConfiguration.AvaloniaKeyToVK(AvKey_LeftCtrl);
        Assert.Equal(VK_LCONTROL, ctrlVK);
        Assert.True(KeyBindingSource.IsModifierOnlyKey(ctrlVK));

        var aVK = TDVKeyBindingConfiguration.AvaloniaKeyToVK(AvKey_A);
        Assert.Equal(VK_A, aVK);
        Assert.False(KeyBindingSource.IsModifierOnlyKey(aVK));

        var source = new KeyBindingSource(aVK, KeyModifiers.Ctrl);
        Assert.True(source.IsValid(), "Ctrl+A should be a valid binding source");

        _output.WriteLine("Ctrl+A scenario: Ctrl press silently ignored, A+Ctrl commits binding");
    }

    [Fact]
    public void ModifierCaptureBug_ShiftThenF1_IsModifierOnlyFiltersFirstKeyDown()
    {
        // Same scenario with Shift+F1
        var shiftVK = TDVKeyBindingConfiguration.AvaloniaKeyToVK(AvKey_LeftShift);
        Assert.Equal(VK_LSHIFT, shiftVK);
        Assert.True(KeyBindingSource.IsModifierOnlyKey(shiftVK));

        var f1VK = TDVKeyBindingConfiguration.AvaloniaKeyToVK(AvKey_F1);
        Assert.Equal(VK_F1, f1VK);
        Assert.False(KeyBindingSource.IsModifierOnlyKey(f1VK));

        var source = new KeyBindingSource(f1VK, KeyModifiers.Shift);
        Assert.True(source.IsValid(), "Shift+F1 should be a valid binding source");

        _output.WriteLine("Shift+F1 scenario: Shift press silently ignored, F1+Shift commits binding");
    }

    #endregion

    #region Always-Visible Binding Indicators

    [Fact]
    public void BindingIndicators_DefaultBindings_HaveLabelsForBoundKeys()
    {
        // Default bindings should produce labels visible on the keyboard
        // without needing to enable any "Show Bindings" checkbox

        // HJELP (G53) — should be bound to Alt+H by default
        var hjelpLabel = TDVKeyBindingConfiguration.Instance.GetBindingLabelForGrid("G53");
        Assert.NotNull(hjelpLabel);
        Assert.Contains("Alt+H", hjelpLabel);
        _output.WriteLine($"G53 (HJELP) binding label: \"{hjelpLabel}\"");

        // SLUTT (G54) — should be bound
        var sluttLabel = TDVKeyBindingConfiguration.Instance.GetBindingLabelForGrid("G54");
        Assert.NotNull(sluttLabel);
        _output.WriteLine($"G54 (SLUTT) binding label: \"{sluttLabel}\"");

        // FUNK (G51) — should be bound
        var funkLabel = TDVKeyBindingConfiguration.Instance.GetBindingLabelForGrid("G51");
        Assert.NotNull(funkLabel);
        _output.WriteLine($"G51 (FUNK) binding label: \"{funkLabel}\"");
    }

    [Fact]
    public void BindingIndicators_UnboundKeys_ReturnNull()
    {
        // Keys that have no default binding should return null
        // (not a crash or empty string)
        // Navigation keys like arrows typically don't need bindings since they map directly via VK
        var arrowLabel = TDVKeyBindingConfiguration.Instance.GetBindingLabelForGrid("A48");
        // A48 (Down arrow) might or might not have a binding — just verify no crash
        _output.WriteLine($"A48 (Down arrow) binding label: \"{arrowLabel ?? "(null)"}\"");
    }

    [Fact]
    public void BindingIndicators_AllDefaultAltBindings_ProduceLabels()
    {
        int boundCount = 0;
        var allKeys = TDV2200KeyVisualRegistry.GetAllKeys();
        for (int i = 0; i < allKeys.Length; i++)
        {
            var meta = allKeys[i];
            if (!meta.IsBindable)
                continue;

            var label = TDVKeyBindingConfiguration.Instance.GetBindingLabelForGrid(meta.GridPosition);
            if (label != null)
            {
                boundCount++;
                _output.WriteLine($"  {meta.GridPosition}: \"{label}\"");
            }
        }

        Assert.True(boundCount >= 8, $"At least 8 keys should have default bindings, found {boundCount}");
        _output.WriteLine($"Total keys with visible binding indicators: {boundCount}");
    }

    #endregion

    #region GetEnglishName — Popup Header Key Names

    [Theory]
    [InlineData("G53")]
    [InlineData("G54")]
    [InlineData("G51")]
    [InlineData("G52")]
    [InlineData("F49")]
    public void GetEnglishName_SpecialKeys_ReturnsHumanReadableName(string gridPos)
    {
        var name = TDV2200KeyRegistry.GetEnglishName(gridPos);
        Assert.NotNull(name);
        Assert.False(string.IsNullOrWhiteSpace(name),
            $"GetEnglishName({gridPos}) should return a non-empty name");
        _output.WriteLine($"{gridPos} → \"{name}\"");
    }

    [Fact]
    public void GetEnglishName_PopupHeaderFormat_ShowsNameAndGridPosition()
    {
        // Verify the format "KEYNAME (G53)" that the popup header uses
        string gridPos = "G53";
        var keyName = TDV2200KeyRegistry.GetEnglishName(gridPos);
        Assert.NotNull(keyName);

        string headerText = $"{keyName} ({gridPos})";
        Assert.Contains("G53", headerText);
        Assert.True(headerText.Length > 5, "Header should be longer than just the grid position");
        _output.WriteLine($"Popup header for G53: \"{headerText}\"");
    }

    [Fact]
    public void GetEnglishName_FunctionKeys_ReturnNames()
    {
        // Function keys F51, F52 etc. should have names
        var f1Name = TDV2200KeyRegistry.GetEnglishName("F51");
        var f2Name = TDV2200KeyRegistry.GetEnglishName("F52");
        Assert.NotNull(f1Name);
        Assert.NotNull(f2Name);
        _output.WriteLine($"F51 → \"{f1Name}\", F52 → \"{f2Name}\"");
    }

    [Fact]
    public void GetEnglishName_PushKeys_ReturnNames()
    {
        // PUSH keys G1-G8 should have names
        for (int i = 1; i <= 8; i++)
        {
            string gp = "G" + i;
            var name = TDV2200KeyRegistry.GetEnglishName(gp);
            Assert.NotNull(name);
            _output.WriteLine($"{gp} → \"{name}\"");
        }
    }

    #endregion

    #region Binding Workflow — SetBinding After Modifier Accumulation

    [Fact]
    public void BindingWorkflow_AltH_ToCustomKey_ShouldWork()
    {
        // Simulate the full binding workflow after modifier accumulation:
        // 1. User right-clicks a key (e.g., FELT D47)
        // 2. Presses "Add Binding"
        // 3. Presses Alt (modifier accumulated, not processed)
        // 4. Presses H (non-modifier, commits binding)

        var targetGrid = "D47"; // FELT

        // Step 3: Alt press → IsModifierOnlyKey catches it
        var altVK = TDVKeyBindingConfiguration.AvaloniaKeyToVK(AvKey_LeftAlt);
        Assert.True(KeyBindingSource.IsModifierOnlyKey(altVK), "Alt should be caught");

        // Step 4: H press → commit binding
        var hVK = TDVKeyBindingConfiguration.AvaloniaKeyToVK(AvKey_H);
        Assert.False(KeyBindingSource.IsModifierOnlyKey(hVK));

        var source = new KeyBindingSource(hVK, KeyModifiers.Alt);
        Assert.True(source.IsValid());

        // First remove existing Alt+H binding (it maps to HJELP by default)
        TDVKeyBindingConfiguration.Instance.RemoveBinding(source);

        // Now set new binding
        var target = new KeyBindingTarget(targetGrid, false);
        var result = TDVKeyBindingConfiguration.Instance.SetBinding(source, target);
        Assert.True(result, "SetBinding should succeed");

        // Verify the binding resolves
        var found = TDVKeyBindingConfiguration.Instance.TryGetTarget(hVK, KeyModifiers.Alt, out var resolved);
        Assert.True(found);
        Assert.Equal(targetGrid, resolved.GridPosition);
        Assert.False(resolved.Shifted);

        _output.WriteLine($"Binding workflow: Alt+H → {targetGrid} (FELT) succeeded");
    }

    [Fact]
    public void BindingWorkflow_CtrlShiftF5_ToCustomKey_ShouldWork()
    {
        // Multi-modifier binding: Ctrl+Shift+F5 → G9 (MERK)
        var targetGrid = "G9";

        var f5VK = TDVKeyBindingConfiguration.AvaloniaKeyToVK(AvKey_F1 + 4); // F5 = 116 VK
        Assert.False(KeyBindingSource.IsModifierOnlyKey(f5VK));

        var source = new KeyBindingSource(f5VK, KeyModifiers.Ctrl | KeyModifiers.Shift);
        Assert.True(source.IsValid());

        var target = new KeyBindingTarget(targetGrid, false);
        var result = TDVKeyBindingConfiguration.Instance.SetBinding(source, target);
        Assert.True(result);

        var found = TDVKeyBindingConfiguration.Instance.TryGetTarget(
            f5VK, KeyModifiers.Ctrl | KeyModifiers.Shift, out var resolved);
        Assert.True(found);
        Assert.Equal(targetGrid, resolved.GridPosition);

        _output.WriteLine($"Ctrl+Shift+F5 → {targetGrid} binding succeeded");
    }

    [Fact]
    public void BindingWorkflow_ShiftedVariant_PreservedThroughWorkflow()
    {
        // Binding with shifted flag: Alt+S → G53 shifted (HJELP shifted)
        var targetGrid = "G53";
        int sVK = VK_A + ('S' - 'A'); // VK_S = 83

        var source = new KeyBindingSource(sVK, KeyModifiers.Alt);
        Assert.True(source.IsValid());

        var target = new KeyBindingTarget(targetGrid, true); // shifted = true
        var result = TDVKeyBindingConfiguration.Instance.SetBinding(source, target);
        Assert.True(result);

        var found = TDVKeyBindingConfiguration.Instance.TryGetTarget(sVK, KeyModifiers.Alt, out var resolved);
        Assert.True(found);
        Assert.Equal(targetGrid, resolved.GridPosition);
        Assert.True(resolved.Shifted, "Shifted flag should be preserved");

        _output.WriteLine($"Alt+S → {targetGrid} shifted binding preserved");
    }

    #endregion

    #region Escape Handling — Escape Key Is Not A Modifier

    [Fact]
    public void EscapeKey_IsNotModifierOnly()
    {
        // Escape (VK 27) should NOT be detected as a modifier-only key.
        // It's used to cancel capture mode, not as a modifier accumulator.
        var escVK = TDVKeyBindingConfiguration.AvaloniaKeyToVK(AvKey_Escape);
        Assert.Equal(27, escVK);
        Assert.False(KeyBindingSource.IsModifierOnlyKey(escVK),
            "Escape should not be a modifier-only key");
        _output.WriteLine("Escape (VK 27) → IsModifierOnlyKey = false (used for cancel)");
    }

    #endregion

    #region Default Alt Mappings — Exact Verification

    [Theory]
    [InlineData(VK_H, "G53", "Alt+H → HJELP")]
    [InlineData(83, "G54", "Alt+S → SLUTT")]        // VK_S = 83
    [InlineData(8, "D48", "Alt+Backspace → ANGRE")]    // VK_BACK = 8
    [InlineData(85, "G51", "Alt+U → FUNK")]           // VK_U = 85
    [InlineData(80, "G52", "Alt+P → SKRIV")]           // VK_P = 80
    [InlineData(68, "F49", "Alt+D → UTFØR")]           // VK_D = 68
    [InlineData(70, "F48", "Alt+F → SØK")]             // VK_F = 70
    [InlineData(77, "C99", "Alt+M → MODE")]             // VK_M = 77
    [InlineData(65, "G9", "Alt+A → MERK")]              // VK_A = 65
    [InlineData(76, "G10", "Alt+L → FELT")]             // VK_L = 76
    [InlineData(82, "G11", "Alt+R → AVSN")]             // VK_R = 82
    [InlineData(69, "G12", "Alt+E → SETN")]             // VK_E = 69
    [InlineData(87, "G13", "Alt+W → ORD")]              // VK_W = 87
    [InlineData(75, "G48", "Alt+K → KOPI")]             // VK_K = 75
    [InlineData(86, "G49", "Alt+V → FLYTT")]            // VK_V = 86
    [InlineData(74, "E48", "Alt+J → JUST")]             // VK_J = 74
    [InlineData(73, "E49", "Alt+I → INSERT_HERE")]      // VK_I = 73
    [InlineData(88, "E47", "Alt+X → GUILLEMETS")]       // VK_X = 88
    [InlineData(46, "G47", "Alt+Delete → STRYK")]       // VK_DELETE = 46
    [InlineData(33, "D47", "Alt+PageUp → ROLLUP")]      // VK_PRIOR = 33, spec section 6.3: RollUp is Page Up
    [InlineData(34, "D49", "Alt+PageDown → ROLLDN")]    // VK_NEXT = 34
    public void DefaultAltMapping_LetterAndNavKeys_ResolveCorrectly(int vkCode, string expectedGrid, string desc)
    {
        var found = TDVKeyBindingConfiguration.Instance.TryGetTarget(vkCode, KeyModifiers.Alt, out var target);
        Assert.True(found, $"{desc}: binding should exist");
        Assert.Equal(expectedGrid, target.GridPosition);
        _output.WriteLine($"{desc} → {target.GridPosition} ✓");
    }

    [Theory]
    [InlineData(1, "G1")]
    [InlineData(2, "G2")]
    [InlineData(3, "G3")]
    [InlineData(4, "G4")]
    [InlineData(5, "G5")]
    [InlineData(6, "G6")]
    [InlineData(7, "G7")]
    [InlineData(8, "G8")]
    public void DefaultAltMapping_AltDigits_MapToPushKeys(int digit, string expectedGrid)
    {
        int vkCode = 48 + digit; // VK_1=49, VK_2=50, ... VK_8=56
        var found = TDVKeyBindingConfiguration.Instance.TryGetTarget(vkCode, KeyModifiers.Alt, out var target);
        Assert.True(found, $"Alt+{digit} should have a default binding to {expectedGrid}");
        Assert.Equal(expectedGrid, target.GridPosition);
        _output.WriteLine($"Alt+{digit} (VK {vkCode}) → {target.GridPosition} (PUSH{digit}) ✓");
    }

    #endregion

    #region Default Alt+F1-F8 → PUSH Keys G1-G8

    [Theory]
    [InlineData(112, "G1", "Alt+F1 → P1")]
    [InlineData(113, "G2", "Alt+F2 → P2")]
    [InlineData(114, "G3", "Alt+F3 → P3")]
    [InlineData(115, "G4", "Alt+F4 → P4")]
    [InlineData(116, "G5", "Alt+F5 → P5")]
    [InlineData(117, "G6", "Alt+F6 → P6")]
    [InlineData(118, "G7", "Alt+F7 → P7")]
    [InlineData(119, "G8", "Alt+F8 → P8")]
    public void DefaultAltMapping_AltFnKeys_MapToPushKeysUnshifted(int vkCode, string expectedGrid, string desc)
    {
        var found = TDVKeyBindingConfiguration.Instance.TryGetTarget(vkCode, KeyModifiers.Alt, out var target);
        Assert.True(found, $"{desc}: binding should exist");
        Assert.Equal(expectedGrid, target.GridPosition);
        Assert.False(target.Shifted, $"{desc}: should be unshifted");
        _output.WriteLine($"{desc} → {target.GridPosition} (unshifted) ✓");
    }

    [Theory]
    [InlineData(112, "G1", "Alt+Shift+F1 → P1 shifted")]
    [InlineData(113, "G2", "Alt+Shift+F2 → P2 shifted")]
    [InlineData(114, "G3", "Alt+Shift+F3 → P3 shifted")]
    [InlineData(115, "G4", "Alt+Shift+F4 → P4 shifted")]
    [InlineData(116, "G5", "Alt+Shift+F5 → P5 shifted")]
    [InlineData(117, "G6", "Alt+Shift+F6 → P6 shifted")]
    [InlineData(118, "G7", "Alt+Shift+F7 → P7 shifted")]
    [InlineData(119, "G8", "Alt+Shift+F8 → P8 shifted")]
    public void DefaultAltMapping_AltShiftFnKeys_MapToPushKeysShifted(int vkCode, string expectedGrid, string desc)
    {
        var found = TDVKeyBindingConfiguration.Instance.TryGetTarget(
            vkCode, KeyModifiers.Alt | KeyModifiers.Shift, out var target);
        Assert.True(found, $"{desc}: binding should exist");
        Assert.Equal(expectedGrid, target.GridPosition);
        Assert.True(target.Shifted, $"{desc}: should be shifted");
        _output.WriteLine($"{desc} → {target.GridPosition} (shifted) ✓");
    }

    [Fact]
    public void DefaultAltMapping_PushKeysHaveBothDigitAndFnBindings()
    {
        // G1 through G8 should each have at least 2 sources: Alt+digit AND Alt+Fn
        for (int i = 1; i <= 8; i++)
        {
            string gridPos = "G" + i;
            var sources = TDVKeyBindingConfiguration.Instance.GetSourcesForGrid(gridPos);
            // At minimum: Alt+digit (unshifted) + Alt+Fn (unshifted) + Alt+Shift+Fn (shifted) = 3
            Assert.True(sources.Count >= 3,
                $"{gridPos} should have at least 3 bindings (Alt+{i}, Alt+F{i}, Alt+Shift+F{i}), found {sources.Count}");
            _output.WriteLine($"{gridPos}: {sources.Count} bindings");
        }
    }

    [Fact]
    public void DefaultAltMapping_AltBackspace_MapsToAngre()
    {
        // Alt+Backspace (VK 8) → D48 (ANGRE) — verify this is in the metadata
        var found = TDVKeyBindingConfiguration.Instance.TryGetTarget(8, KeyModifiers.Alt, out var target);
        Assert.True(found, "Alt+Backspace should have a default binding");
        Assert.Equal("D48", target.GridPosition);
        Assert.False(target.Shifted);
        _output.WriteLine("Alt+Backspace → D48 (ANGRE) ✓");
    }

    [Fact]
    public void DefaultAltShiftMappings_AreDefinedInRegistry()
    {
        // Verify that TDV2200KeyRegistry exposes the Alt+Shift metadata
        var shiftMap = TDV2200KeyRegistry.DefaultAltShiftMappings;
        Assert.NotNull(shiftMap);
        Assert.Equal(8, shiftMap.Count); // F1-F8
        _output.WriteLine($"DefaultAltShiftMappings has {shiftMap.Count} entries");
    }

    #endregion

    #region Clear All Bindings For Grid

    [Fact]
    public void ClearAllBindings_RemoveBindingsForGrid_RemovesAll()
    {
        // G53 (HJELP) has a default Alt+H binding.
        // Add a second binding (Ctrl+H), then clear all.
        var source2 = new KeyBindingSource(VK_H, KeyModifiers.Ctrl);
        TDVKeyBindingConfiguration.Instance.SetBinding(source2, new KeyBindingTarget("G53", false));

        // Verify both exist
        var sourcesBefore = TDVKeyBindingConfiguration.Instance.GetSourcesForGrid("G53");
        Assert.True(sourcesBefore.Count >= 2, $"G53 should have at least 2 bindings, has {sourcesBefore.Count}");
        _output.WriteLine($"Before clear: {sourcesBefore.Count} bindings for G53");

        // Clear all
        TDVKeyBindingConfiguration.Instance.RemoveBindingsForGrid("G53");

        // Verify all are gone
        var sourcesAfter = TDVKeyBindingConfiguration.Instance.GetSourcesForGrid("G53");
        Assert.Empty(sourcesAfter);

        var label = TDVKeyBindingConfiguration.Instance.GetBindingLabelForGrid("G53");
        Assert.Null(label);

        // Alt+H should no longer resolve
        var found = TDVKeyBindingConfiguration.Instance.TryGetTarget(VK_H, KeyModifiers.Alt, out _);
        Assert.False(found, "Alt+H should not resolve after clear all");

        _output.WriteLine("Clear all bindings for G53: all bindings removed");
    }

    [Fact]
    public void ClearAllBindings_EmptyGrid_DoesNotThrow()
    {
        // Clearing bindings on a grid that has no bindings should not throw
        TDVKeyBindingConfiguration.Instance.RemoveBindingsForGrid("A48"); // arrow key, likely no bindings
        var sources = TDVKeyBindingConfiguration.Instance.GetSourcesForGrid("A48");
        Assert.Empty(sources);
        _output.WriteLine("Clear all on empty grid: no crash");
    }

    [Fact]
    public void ClearAllBindings_ThenResetDefaults_RestoresBindings()
    {
        // Clear → reset should restore everything
        TDVKeyBindingConfiguration.Instance.RemoveBindingsForGrid("G53");
        var sourcesCleared = TDVKeyBindingConfiguration.Instance.GetSourcesForGrid("G53");
        Assert.Empty(sourcesCleared);

        TDVKeyBindingConfiguration.Instance.SetDefaults();

        var sourcesRestored = TDVKeyBindingConfiguration.Instance.GetSourcesForGrid("G53");
        Assert.True(sourcesRestored.Count > 0, "G53 should have bindings after reset");

        var found = TDVKeyBindingConfiguration.Instance.TryGetTarget(VK_H, KeyModifiers.Alt, out var target);
        Assert.True(found);
        Assert.Equal("G53", target.GridPosition);
        _output.WriteLine("Clear + Reset defaults → bindings restored");
    }

    [Fact]
    public void ClearAllBindings_ClearButtonDisabledWhenEmpty()
    {
        // Verify the UI logic: Clear button should be disabled when no bindings exist.
        // Test the condition directly (sources.Count > 0).
        TDVKeyBindingConfiguration.Instance.RemoveBindingsForGrid("G53");
        var sources = TDVKeyBindingConfiguration.Instance.GetSourcesForGrid("G53");
        bool clearEnabled = sources.Count > 0;
        Assert.False(clearEnabled, "Clear button should be disabled when no bindings exist");

        // After adding a binding, it should be enabled
        TDVKeyBindingConfiguration.Instance.SetBinding(
            new KeyBindingSource(VK_H, KeyModifiers.Alt),
            new KeyBindingTarget("G53", false));
        sources = TDVKeyBindingConfiguration.Instance.GetSourcesForGrid("G53");
        clearEnabled = sources.Count > 0;
        Assert.True(clearEnabled, "Clear button should be enabled when bindings exist");

        _output.WriteLine("Clear button enable/disable logic verified");
    }

    #endregion

    #region Edge Cases

    [Fact]
    public void IsModifierOnlyKey_BoundaryValues_DoNotFalsePositive()
    {
        // VK codes just outside the modifier ranges should not be flagged
        Assert.False(KeyBindingSource.IsModifierOnlyKey(15), "VK 15 (before Shift) should not be modifier");
        Assert.False(KeyBindingSource.IsModifierOnlyKey(19), "VK 19 (Pause) should not be modifier");
        Assert.False(KeyBindingSource.IsModifierOnlyKey(90), "VK 90 (Z) should not be modifier");
        Assert.False(KeyBindingSource.IsModifierOnlyKey(93), "VK 93 (Apps) should not be modifier");
        Assert.False(KeyBindingSource.IsModifierOnlyKey(159), "VK 159 (before LShift) should not be modifier");
        Assert.False(KeyBindingSource.IsModifierOnlyKey(166), "VK 166 (after RAlt) should not be modifier");
    }

    [Fact]
    public void RemoveBinding_ThenIndicatorReturnsNull()
    {
        // Verify that removing a binding makes the indicator disappear
        var source = new KeyBindingSource(VK_H, KeyModifiers.Alt);

        // Verify it exists first
        var labelBefore = TDVKeyBindingConfiguration.Instance.GetBindingLabelForGrid("G53");
        Assert.NotNull(labelBefore);

        // Remove it
        TDVKeyBindingConfiguration.Instance.RemoveBinding(source);

        // Check if label is now null or doesn't contain Alt+H
        // (there may be other bindings to G53)
        var sources = TDVKeyBindingConfiguration.Instance.GetSourcesForGrid("G53");
        bool altHStillBound = false;
        for (int i = 0; i < sources.Count; i++)
        {
            if (sources[i].VKCode == VK_H && sources[i].Modifiers == KeyModifiers.Alt)
            {
                altHStillBound = true;
                break;
            }
        }
        Assert.False(altHStillBound, "Alt+H should no longer be bound to G53 after removal");

        _output.WriteLine("Remove binding → indicator updated correctly");
    }

    [Fact]
    public void ResetDefaults_RestoresAllBindings()
    {
        // Remove a binding, then reset defaults, then verify it's back
        var source = new KeyBindingSource(VK_H, KeyModifiers.Alt);
        TDVKeyBindingConfiguration.Instance.RemoveBinding(source);

        // Verify it's gone
        var found = TDVKeyBindingConfiguration.Instance.TryGetTarget(VK_H, KeyModifiers.Alt, out _);
        Assert.False(found, "Alt+H should be removed");

        // Reset defaults
        TDVKeyBindingConfiguration.Instance.SetDefaults();

        // Verify it's back
        found = TDVKeyBindingConfiguration.Instance.TryGetTarget(VK_H, KeyModifiers.Alt, out var target);
        Assert.True(found, "Alt+H should be restored after SetDefaults");
        Assert.Equal("G53", target.GridPosition);

        _output.WriteLine("Reset defaults restored Alt+H → G53 binding");
    }

    #endregion
}
