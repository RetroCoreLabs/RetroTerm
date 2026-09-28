using RetroTerm.Core.Terminal.Input;
using Xunit;
using Xunit.Abstractions;

namespace RetroTerm.Tests.TDV;

/// <summary>
/// Unit tests for TDVKeyBindingConfiguration — generalized key binding system for TDV special keys.
/// Tests validation, CRUD, labels, migration, and mapper integration.
/// </summary>
[Collection("TDVKeyBinding")]
public class TDVKeyBindingConfigurationTests
{
    private readonly ITestOutputHelper _output;
    private readonly TDV2200KeyboardMapper _mapper;

    // VK codes for testing
    private const int VK_H = 72;
    private const int VK_B = 66;
    private const int VK_G = 71;
    private const int VK_D = 68;
    private const int VK_K = 75;
    private const int VK_A = 65;
    private const int VK_F1 = 112;
    private const int VK_F2 = 113;
    private const int VK_F12 = 123;
    private const int VK_TAB = 9;
    private const int VK_ESC = 27;
    private const int VK_UP = 38;
    private const int VK_DEL = 46;
    private const int VK_PGUP = 33;
    private const int VK_PGDN = 34;
    private const int VK_HOME = 36;
    private const int VK_1 = 49;

    public TDVKeyBindingConfigurationTests(ITestOutputHelper output)
    {
        _output = output;
        _mapper = new TDV2200KeyboardMapper();

        // Reset to defaults before each test
        TDVKeyBindingConfiguration.ResetForTesting();
    }

    #region Validation Tests

    [Fact]
    public void IsValidSource_BareLetterA_ShouldBeRejected()
    {
        var source = new KeyBindingSource(VK_A, KeyModifiers.None);
        Assert.False(source.IsValid());
        _output.WriteLine("Bare letter A correctly rejected");
    }

    [Fact]
    public void IsValidSource_BareDigit1_ShouldBeRejected()
    {
        var source = new KeyBindingSource(VK_1, KeyModifiers.None);
        Assert.False(source.IsValid());
        _output.WriteLine("Bare digit 1 correctly rejected");
    }

    [Fact]
    public void IsValidSource_BareF1_ShouldBeAccepted()
    {
        var source = new KeyBindingSource(VK_F1, KeyModifiers.None);
        Assert.True(source.IsValid());
        _output.WriteLine("Bare F1 correctly accepted");
    }

    [Fact]
    public void IsValidSource_BareF12_ShouldBeAccepted()
    {
        var source = new KeyBindingSource(VK_F12, KeyModifiers.None);
        Assert.True(source.IsValid());
        _output.WriteLine("Bare F12 correctly accepted");
    }

    [Fact]
    public void IsValidSource_BareTab_ShouldBeAccepted()
    {
        var source = new KeyBindingSource(VK_TAB, KeyModifiers.None);
        Assert.True(source.IsValid());
        _output.WriteLine("Bare Tab correctly accepted");
    }

    [Fact]
    public void IsValidSource_BareEscape_ShouldBeAccepted()
    {
        var source = new KeyBindingSource(VK_ESC, KeyModifiers.None);
        Assert.True(source.IsValid());
        _output.WriteLine("Bare Escape correctly accepted");
    }

    [Fact]
    public void IsValidSource_BareArrowUp_ShouldBeAccepted()
    {
        var source = new KeyBindingSource(VK_UP, KeyModifiers.None);
        Assert.True(source.IsValid());
        _output.WriteLine("Bare Arrow Up correctly accepted");
    }

    [Fact]
    public void IsValidSource_ShiftF1_ShouldBeAccepted()
    {
        var source = new KeyBindingSource(VK_F1, KeyModifiers.Shift);
        Assert.True(source.IsValid());
        _output.WriteLine("Shift+F1 correctly accepted");
    }

    [Fact]
    public void IsValidSource_CtrlA_ShouldBeAccepted()
    {
        var source = new KeyBindingSource(VK_A, KeyModifiers.Ctrl);
        Assert.True(source.IsValid());
        _output.WriteLine("Ctrl+A correctly accepted");
    }

    [Fact]
    public void IsValidSource_AltH_ShouldBeAccepted()
    {
        var source = new KeyBindingSource(VK_H, KeyModifiers.Alt);
        Assert.True(source.IsValid());
        _output.WriteLine("Alt+H correctly accepted");
    }

    [Fact]
    public void IsValidSource_CtrlShiftF1_ShouldBeAccepted()
    {
        var source = new KeyBindingSource(VK_F1, KeyModifiers.Ctrl | KeyModifiers.Shift);
        Assert.True(source.IsValid());
        _output.WriteLine("Ctrl+Shift+F1 correctly accepted");
    }

    [Fact]
    public void IsValidSource_ShiftAlone_ShouldBeRejected()
    {
        // VK_SHIFT = 16
        var source = new KeyBindingSource(16, KeyModifiers.None);
        Assert.False(source.IsValid());
        _output.WriteLine("Shift alone correctly rejected");
    }

    [Fact]
    public void IsValidSource_CtrlAlone_ShouldBeRejected()
    {
        // VK_CONTROL = 17
        var source = new KeyBindingSource(17, KeyModifiers.None);
        Assert.False(source.IsValid());
        _output.WriteLine("Ctrl alone correctly rejected");
    }

    [Fact]
    public void IsValidSource_AltAlone_ShouldBeRejected()
    {
        // VK_MENU = 18
        var source = new KeyBindingSource(18, KeyModifiers.None);
        Assert.False(source.IsValid());
        _output.WriteLine("Alt alone correctly rejected");
    }

    [Fact]
    public void IsValidSource_ShiftLetter_ShouldBeAccepted()
    {
        var source = new KeyBindingSource(VK_A, KeyModifiers.Shift);
        Assert.True(source.IsValid());
        _output.WriteLine("Shift+A correctly accepted");
    }

    [Fact]
    public void IsValidSource_BareHomeEndPageUpPageDownInsertDelete_ShouldBeAccepted()
    {
        Assert.True(new KeyBindingSource(VK_HOME, KeyModifiers.None).IsValid());
        Assert.True(new KeyBindingSource(35, KeyModifiers.None).IsValid()); // End
        Assert.True(new KeyBindingSource(VK_PGUP, KeyModifiers.None).IsValid());
        Assert.True(new KeyBindingSource(VK_PGDN, KeyModifiers.None).IsValid());
        Assert.True(new KeyBindingSource(45, KeyModifiers.None).IsValid()); // Insert
        Assert.True(new KeyBindingSource(VK_DEL, KeyModifiers.None).IsValid());
        _output.WriteLine("All bare navigation keys correctly accepted");
    }

    #endregion

    #region SetBinding Tests

    [Fact]
    public void SetBinding_ValidSource_ShouldSucceed()
    {
        var source = new KeyBindingSource(VK_F1, KeyModifiers.None);
        var target = new KeyBindingTarget("G53", false);

        var result = TDVKeyBindingConfiguration.Instance.SetBinding(source, target);

        Assert.True(result);
        Assert.True(TDVKeyBindingConfiguration.Instance.TryGetTarget(source, out var retrieved));
        Assert.Equal("G53", retrieved.GridPosition);
        Assert.False(retrieved.Shifted);
        _output.WriteLine("SetBinding with bare F1 → G53 succeeded");
    }

    [Fact]
    public void SetBinding_InvalidSource_ShouldFail()
    {
        var source = new KeyBindingSource(VK_A, KeyModifiers.None); // Bare letter
        var target = new KeyBindingTarget("G53", false);

        var result = TDVKeyBindingConfiguration.Instance.SetBinding(source, target);

        Assert.False(result);
        Assert.False(TDVKeyBindingConfiguration.Instance.TryGetTarget(source, out _));
        _output.WriteLine("SetBinding with bare A correctly rejected");
    }

    [Fact]
    public void SetBinding_WithShifted_ShouldPreserveShiftedFlag()
    {
        var source = new KeyBindingSource(VK_F1, KeyModifiers.Shift);
        var target = new KeyBindingTarget("F51", true);

        var result = TDVKeyBindingConfiguration.Instance.SetBinding(source, target);

        Assert.True(result);
        Assert.True(TDVKeyBindingConfiguration.Instance.TryGetTarget(source, out var retrieved));
        Assert.Equal("F51", retrieved.GridPosition);
        Assert.True(retrieved.Shifted);
        _output.WriteLine("Shift+F1 → F51 (shifted) binding preserved");
    }

    [Fact]
    public void SetBinding_OverwritesExisting()
    {
        var source = new KeyBindingSource(VK_F1, KeyModifiers.None);
        var target1 = new KeyBindingTarget("G53", false);
        var target2 = new KeyBindingTarget("F49", false);

        TDVKeyBindingConfiguration.Instance.SetBinding(source, target1);
        TDVKeyBindingConfiguration.Instance.SetBinding(source, target2);

        Assert.True(TDVKeyBindingConfiguration.Instance.TryGetTarget(source, out var retrieved));
        Assert.Equal("F49", retrieved.GridPosition);

        // G53 should no longer have this source
        var g53Sources = TDVKeyBindingConfiguration.Instance.GetSourcesForGrid("G53");
        bool hasF1 = false;
        for (int i = 0; i < g53Sources.Count; i++)
        {
            if (g53Sources[i] == source) hasF1 = true;
        }
        Assert.False(hasF1);
        _output.WriteLine("SetBinding correctly overwrites previous target");
    }

    [Fact]
    public void SetBinding_MultipleSourcesForSameGrid()
    {
        var source1 = new KeyBindingSource(VK_F1, KeyModifiers.None);
        var source2 = new KeyBindingSource(VK_H, KeyModifiers.Alt);
        var target = new KeyBindingTarget("G53", false);

        // Remove any existing Alt+H binding to G53 first (from defaults)
        TDVKeyBindingConfiguration.Instance.RemoveBinding(
            new KeyBindingSource(VK_H, KeyModifiers.Alt));

        TDVKeyBindingConfiguration.Instance.SetBinding(source1, target);
        TDVKeyBindingConfiguration.Instance.SetBinding(source2, target);

        var sources = TDVKeyBindingConfiguration.Instance.GetSourcesForGrid("G53");
        Assert.Equal(2, sources.Count);
        _output.WriteLine("Multiple bindings to same grid position work correctly");
    }

    [Fact]
    public void SetBinding_RaisesConfigurationChangedEvent()
    {
        var eventRaised = false;
        TDVKeyBindingConfiguration.Instance.ConfigurationChanged += (s, e) => eventRaised = true;

        var source = new KeyBindingSource(VK_F1, KeyModifiers.None);
        var target = new KeyBindingTarget("G53", false);
        TDVKeyBindingConfiguration.Instance.SetBinding(source, target);

        Assert.True(eventRaised);
        _output.WriteLine("ConfigurationChanged event raised on SetBinding");
    }

    #endregion

    #region RemoveBinding Tests

    [Fact]
    public void RemoveBinding_ExistingBinding_ShouldSucceed()
    {
        var source = new KeyBindingSource(VK_H, KeyModifiers.Alt);

        // Verify it exists (from defaults)
        Assert.True(TDVKeyBindingConfiguration.Instance.TryGetTarget(source, out _));

        var result = TDVKeyBindingConfiguration.Instance.RemoveBinding(source);

        Assert.True(result);
        Assert.False(TDVKeyBindingConfiguration.Instance.TryGetTarget(source, out _));
        _output.WriteLine("RemoveBinding for existing Alt+H succeeded");
    }

    [Fact]
    public void RemoveBinding_NonExistent_ShouldReturnFalse()
    {
        var source = new KeyBindingSource(VK_F1, KeyModifiers.None);
        var result = TDVKeyBindingConfiguration.Instance.RemoveBinding(source);

        Assert.False(result);
        _output.WriteLine("RemoveBinding for non-existent binding returns false");
    }

    [Fact]
    public void RemoveBindingsForGrid_ShouldRemoveAll()
    {
        // Add a second binding to G53
        var source2 = new KeyBindingSource(VK_F1, KeyModifiers.None);
        TDVKeyBindingConfiguration.Instance.SetBinding(source2, new KeyBindingTarget("G53", false));

        // Now G53 has at least 2 bindings (Alt+H from defaults + F1)
        var beforeSources = TDVKeyBindingConfiguration.Instance.GetSourcesForGrid("G53");
        Assert.True(beforeSources.Count >= 2);

        TDVKeyBindingConfiguration.Instance.RemoveBindingsForGrid("G53");

        // All gone
        var afterSources = TDVKeyBindingConfiguration.Instance.GetSourcesForGrid("G53");
        Assert.Empty(afterSources);

        // Neither binding should work
        Assert.False(TDVKeyBindingConfiguration.Instance.TryGetTarget(
            new KeyBindingSource(VK_H, KeyModifiers.Alt), out _));
        Assert.False(TDVKeyBindingConfiguration.Instance.TryGetTarget(source2, out _));
        _output.WriteLine("RemoveBindingsForGrid removed all bindings to G53");
    }

    [Fact]
    public void RemoveBinding_RaisesConfigurationChangedEvent()
    {
        var eventRaised = false;
        TDVKeyBindingConfiguration.Instance.ConfigurationChanged += (s, e) => eventRaised = true;

        var source = new KeyBindingSource(VK_H, KeyModifiers.Alt);
        TDVKeyBindingConfiguration.Instance.RemoveBinding(source);

        Assert.True(eventRaised);
        _output.WriteLine("ConfigurationChanged event raised on RemoveBinding");
    }

    #endregion

    #region Default Bindings Tests

    [Fact]
    public void Defaults_AltH_ShouldMapToG53()
    {
        Assert.True(TDVKeyBindingConfiguration.Instance.TryGetTarget(VK_H, KeyModifiers.Alt, out var target));
        Assert.Equal("G53", target.GridPosition);
        Assert.False(target.Shifted);
        _output.WriteLine("Default: Alt+H → G53 (HJELP)");
    }

    [Fact]
    public void Defaults_AltD_ShouldMapToF49()
    {
        Assert.True(TDVKeyBindingConfiguration.Instance.TryGetTarget(VK_D, KeyModifiers.Alt, out var target));
        Assert.Equal("F49", target.GridPosition);
        _output.WriteLine("Default: Alt+D → F49 (REPLACE)");
    }

    [Fact]
    public void Defaults_Alt1Through8_ShouldMapToPushKeys()
    {
        for (int i = 1; i <= 8; i++)
        {
            var vk = 48 + i; // VK_1=49, VK_2=50, etc.
            Assert.True(TDVKeyBindingConfiguration.Instance.TryGetTarget(vk, KeyModifiers.Alt, out var target));
            Assert.Equal($"G{i}", target.GridPosition);
            _output.WriteLine($"Default: Alt+{i} → G{i} (PUSH{i})");
        }
    }

    [Fact]
    public void Defaults_AltDel_ShouldMapToG47()
    {
        Assert.True(TDVKeyBindingConfiguration.Instance.TryGetTarget(VK_DEL, KeyModifiers.Alt, out var target));
        Assert.Equal("G47", target.GridPosition);
        _output.WriteLine("Default: Alt+Del → G47 (STRYK)");
    }

    [Fact]
    public void Defaults_AltPgUp_ShouldMapToD47RollUp()
    {
        Assert.True(TDVKeyBindingConfiguration.Instance.TryGetTarget(VK_PGUP, KeyModifiers.Alt, out var target));
        Assert.Equal("D47", target.GridPosition);
        _output.WriteLine("Default: Alt+PgUp → D47 (ROLLUP), spec section 6.3");
    }

    [Fact]
    public void Defaults_AltPgDn_ShouldMapToD49RollDown()
    {
        Assert.True(TDVKeyBindingConfiguration.Instance.TryGetTarget(VK_PGDN, KeyModifiers.Alt, out var target));
        Assert.Equal("D49", target.GridPosition);
        _output.WriteLine("Default: Alt+PgDn → D49 (ROLLDN)");
    }

    [Fact]
    public void SetDefaults_ShouldRestoreAfterModifications()
    {
        // Modify
        TDVKeyBindingConfiguration.Instance.RemoveBinding(new KeyBindingSource(VK_H, KeyModifiers.Alt));
        Assert.False(TDVKeyBindingConfiguration.Instance.TryGetTarget(VK_H, KeyModifiers.Alt, out _));

        // Reset
        TDVKeyBindingConfiguration.Instance.SetDefaults();

        // Verify restored
        Assert.True(TDVKeyBindingConfiguration.Instance.TryGetTarget(VK_H, KeyModifiers.Alt, out var target));
        Assert.Equal("G53", target.GridPosition);
        _output.WriteLine("SetDefaults correctly restores bindings");
    }

    #endregion

    #region Label Tests

    [Fact]
    public void GetBindingLabel_AltH_ShouldReturnCorrectFormat()
    {
        var label = TDVKeyBindingConfiguration.GetBindingLabel(VK_H, KeyModifiers.Alt);
        Assert.Equal("Alt+H", label);
        _output.WriteLine($"Label for Alt+H: {label}");
    }

    [Fact]
    public void GetBindingLabel_BareF1_ShouldReturnF1()
    {
        var label = TDVKeyBindingConfiguration.GetBindingLabel(VK_F1, KeyModifiers.None);
        Assert.Equal("F1", label);
        _output.WriteLine($"Label for bare F1: {label}");
    }

    [Fact]
    public void GetBindingLabel_ShiftF1_ShouldReturnShiftF1()
    {
        var label = TDVKeyBindingConfiguration.GetBindingLabel(VK_F1, KeyModifiers.Shift);
        Assert.Equal("Shift+F1", label);
        _output.WriteLine($"Label for Shift+F1: {label}");
    }

    [Fact]
    public void GetBindingLabel_CtrlA_ShouldReturnCtrlA()
    {
        var label = TDVKeyBindingConfiguration.GetBindingLabel(VK_A, KeyModifiers.Ctrl);
        Assert.Equal("Ctrl+A", label);
        _output.WriteLine($"Label for Ctrl+A: {label}");
    }

    [Fact]
    public void GetBindingLabel_CtrlShiftF1_ShouldReturnCorrect()
    {
        var label = TDVKeyBindingConfiguration.GetBindingLabel(VK_F1, KeyModifiers.Ctrl | KeyModifiers.Shift);
        Assert.Equal("Ctrl+Shift+F1", label);
        _output.WriteLine($"Label for Ctrl+Shift+F1: {label}");
    }

    [Fact]
    public void GetBindingLabel_BareTab_ShouldReturnTab()
    {
        var label = TDVKeyBindingConfiguration.GetBindingLabel(VK_TAB, KeyModifiers.None);
        Assert.Equal("Tab", label);
        _output.WriteLine($"Label for bare Tab: {label}");
    }

    [Fact]
    public void GetBindingLabel_Alt1_ShouldReturnAlt1()
    {
        var label = TDVKeyBindingConfiguration.GetBindingLabel(VK_1, KeyModifiers.Alt);
        Assert.Equal("Alt+1", label);
        _output.WriteLine($"Label for Alt+1: {label}");
    }

    [Fact]
    public void GetBindingLabelForGrid_SingleBinding_ShouldReturnLabel()
    {
        var label = TDVKeyBindingConfiguration.Instance.GetBindingLabelForGrid("G53");
        Assert.Equal("Alt+H", label);
        _output.WriteLine($"Grid label for G53: {label}");
    }

    [Fact]
    public void GetBindingLabelForGrid_MultipleBindings_ShouldReturnCommaSeparated()
    {
        // Add F1 as a second binding to G53
        TDVKeyBindingConfiguration.Instance.SetBinding(
            new KeyBindingSource(VK_F1, KeyModifiers.None),
            new KeyBindingTarget("G53", false));

        var label = TDVKeyBindingConfiguration.Instance.GetBindingLabelForGrid("G53");
        Assert.NotNull(label);
        Assert.Contains("Alt+H", label);
        Assert.Contains("F1", label);
        Assert.Contains(",", label);
        _output.WriteLine($"Grid label for G53 with multiple bindings: {label}");
    }

    [Fact]
    public void GetBindingLabelForGrid_NoBindings_ShouldReturnNull()
    {
        // Remove all bindings for G53
        TDVKeyBindingConfiguration.Instance.RemoveBindingsForGrid("G53");
        var label = TDVKeyBindingConfiguration.Instance.GetBindingLabelForGrid("G53");
        Assert.Null(label);
        _output.WriteLine("Grid label for unbound G53: null");
    }

    #endregion

    #region Migration Tests

    [Fact]
    public void MigrateFromOldFormat_ShouldConvertToAltBindings()
    {
        // Simulate old format JSON: { "72": "G53", "68": "F49" }
        var oldJson = "{\"72\":\"G53\",\"68\":\"F49\"}";

        TDVKeyBindingConfiguration.ResetForTesting();
        var result = TDVKeyBindingConfiguration.Instance.TryMigrateFromOldFormat(oldJson);

        Assert.True(result);

        // Verify Alt+H → G53
        Assert.True(TDVKeyBindingConfiguration.Instance.TryGetTarget(VK_H, KeyModifiers.Alt, out var target1));
        Assert.Equal("G53", target1.GridPosition);
        Assert.False(target1.Shifted);

        // Verify Alt+D → F49
        Assert.True(TDVKeyBindingConfiguration.Instance.TryGetTarget(VK_D, KeyModifiers.Alt, out var target2));
        Assert.Equal("F49", target2.GridPosition);

        _output.WriteLine("Old format migration successful");
    }

    [Fact]
    public void MigrateFromOldFormat_EmptyJson_ShouldReturnFalse()
    {
        var result = TDVKeyBindingConfiguration.Instance.TryMigrateFromOldFormat("{}");
        Assert.False(result);
        _output.WriteLine("Empty old format correctly returns false");
    }

    #endregion

    #region KeyBindingSource Equality Tests

    [Fact]
    public void KeyBindingSource_SameValues_ShouldBeEqual()
    {
        var a = new KeyBindingSource(VK_F1, KeyModifiers.Shift);
        var b = new KeyBindingSource(VK_F1, KeyModifiers.Shift);

        Assert.Equal(a, b);
        Assert.True(a == b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
        _output.WriteLine("KeyBindingSource equality works correctly");
    }

    [Fact]
    public void KeyBindingSource_DifferentVK_ShouldNotBeEqual()
    {
        var a = new KeyBindingSource(VK_F1, KeyModifiers.Shift);
        var b = new KeyBindingSource(VK_F2, KeyModifiers.Shift);

        Assert.NotEqual(a, b);
        Assert.True(a != b);
        _output.WriteLine("KeyBindingSource inequality (different VK) works correctly");
    }

    [Fact]
    public void KeyBindingSource_DifferentModifiers_ShouldNotBeEqual()
    {
        var a = new KeyBindingSource(VK_F1, KeyModifiers.Shift);
        var b = new KeyBindingSource(VK_F1, KeyModifiers.Ctrl);

        Assert.NotEqual(a, b);
        _output.WriteLine("KeyBindingSource inequality (different modifiers) works correctly");
    }

    [Fact]
    public void KeyBindingSource_WorksAsDictionaryKey()
    {
        var dict = new System.Collections.Generic.Dictionary<KeyBindingSource, string>();
        var key1 = new KeyBindingSource(VK_F1, KeyModifiers.None);
        var key2 = new KeyBindingSource(VK_F1, KeyModifiers.Shift);

        dict[key1] = "bare";
        dict[key2] = "shifted";

        Assert.Equal("bare", dict[key1]);
        Assert.Equal("shifted", dict[key2]);
        _output.WriteLine("KeyBindingSource works as dictionary key");
    }

    #endregion

    #region Mapper Integration Tests

    [Fact]
    public void Mapper_AltH_ShouldSendHjelpSequence()
    {
        var sequence = _mapper.MapKey(VK_H, KeyModifiers.Alt, TerminalModes.None);
        Assert.Equal("\x1b[46_", sequence);
        _output.WriteLine($"Alt+H sends HJELP: {EscapeForDisplay(sequence)}");
    }

    [Fact]
    public void Mapper_AfterRebindToAltB_ShouldSendHjelpSequence()
    {
        // Remove Alt+H binding, add Alt+B → G53
        TDVKeyBindingConfiguration.Instance.RemoveBinding(new KeyBindingSource(VK_H, KeyModifiers.Alt));
        TDVKeyBindingConfiguration.Instance.SetBinding(
            new KeyBindingSource(VK_B, KeyModifiers.Alt),
            new KeyBindingTarget("G53", false));

        // Alt+B should now send HJELP (TDV-native CSI 46 _)
        var sequence = _mapper.MapKey(VK_B, KeyModifiers.Alt, TerminalModes.None);
        Assert.Equal("\x1b[46_", sequence);

        // Alt+H should no longer send anything (well, may fall through to base)
        var oldSequence = _mapper.MapKey(VK_H, KeyModifiers.Alt, TerminalModes.None);
        Assert.Null(oldSequence);
        _output.WriteLine("After rebind: Alt+B sends HJELP, Alt+H inactive");
    }

    [Fact]
    public void Mapper_Alt1_ShouldSendPush1StoredString()
    {
        TDVPushKeyConfiguration.ResetForTesting();
        TDVPushKeyConfiguration.Instance.ProgramKey(1, "push1test");

        var sequence = _mapper.MapKey(VK_1, KeyModifiers.Alt, TerminalModes.None);
        Assert.Equal("push1test", sequence);
        _output.WriteLine($"Alt+1 sends PUSH1 stored string: {EscapeForDisplay(sequence)}");
    }

    [Fact]
    public void TDV1200Mapper_AltH_ShouldSendHjelpSequence()
    {
        var tdv1200Mapper = new TDV1200KeyboardMapper();
        var sequence = tdv1200Mapper.MapKey(VK_H, KeyModifiers.Alt, TerminalModes.None);
        Assert.Equal("\x1b[46_", sequence);
        _output.WriteLine($"TDV1200 Alt+H sends HJELP: {EscapeForDisplay(sequence)}");
    }

    [Fact]
    public void TDV2215Mapper_AltH_ShouldSendHjelpSequence()
    {
        var tdv2215Mapper = new TDV2215KeyboardMapper();
        var sequence = tdv2215Mapper.MapKey(VK_H, KeyModifiers.Alt, TerminalModes.None);
        Assert.Equal("\x1b[46_", sequence);
        _output.WriteLine($"TDV2215 Alt+H sends HJELP: {EscapeForDisplay(sequence)}");
    }

    #endregion

    #region Legacy Compatibility Tests

    [Fact]
    public void GetGridPosition_LegacyAPI_ShouldWork()
    {
        Assert.Equal("G53", TDVKeyBindingConfiguration.Instance.GetGridPosition(VK_H));
        Assert.Null(TDVKeyBindingConfiguration.Instance.GetGridPosition(VK_B));
        _output.WriteLine("Legacy GetGridPosition works");
    }

    [Fact]
    public void IsVKCodeAvailable_LegacyAPI_ShouldWork()
    {
        Assert.False(TDVKeyBindingConfiguration.Instance.IsVKCodeAvailable(VK_H));
        Assert.True(TDVKeyBindingConfiguration.Instance.IsVKCodeAvailable(VK_B));
        _output.WriteLine("Legacy IsVKCodeAvailable works");
    }

    #endregion

    #region Helper Methods

    private static string EscapeForDisplay(string? s)
    {
        if (s == null) return "(null)";
        return s.Replace("\x1b", "ESC")
                .Replace("\\", "\\\\");
    }

    #endregion
}
