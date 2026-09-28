using RetroTerm.Core.Terminal.Emulators.TDV;
using RetroTerm.Core.Terminal.Input;
using RetroTerm.Desktop.Models;
using RetroTerm.Desktop.ViewModels;
using Xunit;

namespace RetroTerm.Tests.TDV;

/// <summary>
/// Tests for VirtualKeyboardViewModel: sticky modifiers, toggle modifiers,
/// auto-release, modifier state tracking, LED states, and key press handling.
/// </summary>
public class VirtualKeyboardViewModelTests
{
    private static TDVKeyVisualMetadata MakeKey(string grid, KeyCategory category, int vk = 0)
    {
        char row = grid[0];
        int.TryParse(grid.Substring(1), out int col);
        return new TDVKeyVisualMetadata(
            grid, row, col, 0, 0, 60, 60,
            TDVKeyColor.White, category, KeyRenderMode.Text,
            category == KeyCategory.Toggle, null, 0.40, true,
            null, null, true, vk);
    }

    #region Sticky Modifier Behavior

    [Fact]
    public void StickyModifier_ClickOnce_BecomesSticky()
    {
        var vm = new VirtualKeyboardViewModel();
        var shift = MakeKey("B99", KeyCategory.Modifier);

        vm.HandleKeyDown(shift);

        Assert.True(vm.IsStickyModifier("B99"));
        Assert.True(vm.IsKeyPressed("B99"));
        Assert.True(vm.IsModifierActive("B99"));
    }

    [Fact]
    public void StickyModifier_ClickTwice_Unsticks()
    {
        var vm = new VirtualKeyboardViewModel();
        var shift = MakeKey("B99", KeyCategory.Modifier);

        vm.HandleKeyDown(shift);
        Assert.True(vm.IsStickyModifier("B99"));

        vm.HandleKeyDown(shift);
        Assert.False(vm.IsStickyModifier("B99"));
        Assert.False(vm.IsKeyPressed("B99"));
        Assert.False(vm.IsModifierActive("B99"));
    }

    [Fact]
    public void StickyModifier_DoesNotFireKeyPressedEvent()
    {
        var vm = new VirtualKeyboardViewModel();
        var shift = MakeKey("B99", KeyCategory.Modifier);
        bool eventFired = false;
        vm.KeyPressed += (s, e) => eventFired = true;

        vm.HandleKeyDown(shift);

        Assert.False(eventFired, "Sticky modifier should not fire KeyPressed event");
    }

    [Fact]
    public void StickyModifier_AutoReleasedAfterNormalKey()
    {
        var vm = new VirtualKeyboardViewModel();
        var shift = MakeKey("B99", KeyCategory.Modifier);
        var letterA = MakeKey("C1", KeyCategory.Alphanumeric, vk: 65);

        vm.HandleKeyDown(shift);
        Assert.True(vm.IsStickyModifier("B99"));

        vm.HandleKeyDown(letterA);
        Assert.False(vm.IsStickyModifier("B99"));
        Assert.False(vm.IsKeyPressed("B99"));
        Assert.False(vm.IsModifierActive("B99"));
    }

    [Fact]
    public void StickyModifier_AutoRelease_FiresEvent()
    {
        var vm = new VirtualKeyboardViewModel();
        var shift = MakeKey("B99", KeyCategory.Modifier);
        var letterA = MakeKey("C1", KeyCategory.Alphanumeric, vk: 65);

        string[]? releasedPositions = null;
        vm.StickyModifiersReleased += (s, e) => releasedPositions = e.ReleasedGridPositions;

        vm.HandleKeyDown(shift);
        vm.HandleKeyDown(letterA);

        Assert.NotNull(releasedPositions);
        Assert.Single(releasedPositions);
        Assert.Equal("B99", releasedPositions[0]);
    }

    [Fact]
    public void StickyModifier_MouseUp_DoesNotRelease()
    {
        var vm = new VirtualKeyboardViewModel();
        var shift = MakeKey("B99", KeyCategory.Modifier);

        vm.HandleKeyDown(shift);
        Assert.True(vm.IsStickyModifier("B99"));

        vm.HandleKeyUp(shift);
        Assert.True(vm.IsStickyModifier("B99"), "Sticky modifier should stay after mouse-up");
        Assert.True(vm.IsKeyPressed("B99"));
    }

    [Fact]
    public void MultipleStickyModifiers_AllReleasedAfterNormalKey()
    {
        var vm = new VirtualKeyboardViewModel();
        var lShift = MakeKey("B99", KeyCategory.Modifier);
        var ctrl = MakeKey("D0", KeyCategory.Modifier);
        var letterA = MakeKey("C1", KeyCategory.Alphanumeric, vk: 65);

        string[]? releasedPositions = null;
        vm.StickyModifiersReleased += (s, e) => releasedPositions = e.ReleasedGridPositions;

        vm.HandleKeyDown(lShift);
        vm.HandleKeyDown(ctrl);
        Assert.True(vm.IsStickyModifier("B99"));
        Assert.True(vm.IsStickyModifier("D0"));

        vm.HandleKeyDown(letterA);
        Assert.False(vm.IsStickyModifier("B99"));
        Assert.False(vm.IsStickyModifier("D0"));
        Assert.NotNull(releasedPositions);
        Assert.Equal(2, releasedPositions.Length);
    }

    [Fact]
    public void StickyModifier_KeyPressedEventIncludesModifiers()
    {
        var vm = new VirtualKeyboardViewModel();
        var lShift = MakeKey("B99", KeyCategory.Modifier);
        var letterA = MakeKey("C1", KeyCategory.Alphanumeric, vk: 65);

        KeyModifiers receivedModifiers = KeyModifiers.None;
        vm.KeyPressed += (s, e) => receivedModifiers = e.Modifiers;

        vm.HandleKeyDown(lShift);
        vm.HandleKeyDown(letterA);

        Assert.True(receivedModifiers.HasFlag(KeyModifiers.Shift));
    }

    [Fact]
    public void StickyModifier_BothShiftKeys_MergeToShift()
    {
        var vm = new VirtualKeyboardViewModel();
        var lShift = MakeKey("B99", KeyCategory.Modifier);
        var rShift = MakeKey("B11", KeyCategory.Modifier);
        var letterA = MakeKey("C1", KeyCategory.Alphanumeric, vk: 65);

        KeyModifiers receivedModifiers = KeyModifiers.None;
        vm.KeyPressed += (s, e) => receivedModifiers = e.Modifiers;

        vm.HandleKeyDown(lShift);
        vm.HandleKeyDown(rShift);
        vm.HandleKeyDown(letterA);

        Assert.True(receivedModifiers.HasFlag(KeyModifiers.Shift));
    }

    [Fact]
    public void StickyModifier_ShiftPlusCtrl_CombinedModifiers()
    {
        var vm = new VirtualKeyboardViewModel();
        var shift = MakeKey("B99", KeyCategory.Modifier);
        var ctrl = MakeKey("D0", KeyCategory.Modifier);
        var letterA = MakeKey("C1", KeyCategory.Alphanumeric, vk: 65);

        KeyModifiers receivedModifiers = KeyModifiers.None;
        vm.KeyPressed += (s, e) => receivedModifiers = e.Modifiers;

        vm.HandleKeyDown(shift);
        vm.HandleKeyDown(ctrl);
        vm.HandleKeyDown(letterA);

        Assert.True(receivedModifiers.HasFlag(KeyModifiers.Shift));
        Assert.True(receivedModifiers.HasFlag(KeyModifiers.Ctrl));
    }

    #endregion

    #region Toggle Modifier Behavior

    [Fact]
    public void ToggleModifier_ClickOnce_Activates()
    {
        var vm = new VirtualKeyboardViewModel();
        var caps = MakeKey("E0", KeyCategory.Toggle);

        vm.HandleKeyDown(caps);

        Assert.True(vm.IsModifierActive("E0"));
        Assert.True(vm.IsKeyPressed("E0"));
    }

    [Fact]
    public void ToggleModifier_ClickTwice_Deactivates()
    {
        var vm = new VirtualKeyboardViewModel();
        var caps = MakeKey("E0", KeyCategory.Toggle);

        vm.HandleKeyDown(caps);
        Assert.True(vm.IsModifierActive("E0"));

        // Release and press again
        vm.HandleKeyUp(caps);
        vm.HandleKeyDown(caps);
        Assert.False(vm.IsModifierActive("E0"));
    }

    [Fact]
    public void ToggleModifier_DoesNotFireKeyPressedEvent()
    {
        var vm = new VirtualKeyboardViewModel();
        var caps = MakeKey("E0", KeyCategory.Toggle);
        bool eventFired = false;
        vm.KeyPressed += (s, e) => eventFired = true;

        vm.HandleKeyDown(caps);

        Assert.False(eventFired, "Toggle key handles state locally, does not fire KeyPressed");
    }

    [Fact]
    public void ToggleModifier_NotStickyModifier()
    {
        var vm = new VirtualKeyboardViewModel();
        var caps = MakeKey("E0", KeyCategory.Toggle);

        vm.HandleKeyDown(caps);

        Assert.False(vm.IsStickyModifier("E0"), "Toggle modifiers should not be sticky");
    }

    #endregion

    #region Normal Key Behavior

    [Fact]
    public void NormalKey_HandleKeyDown_MarksPressed()
    {
        var vm = new VirtualKeyboardViewModel();
        var letterA = MakeKey("C1", KeyCategory.Alphanumeric, vk: 65);

        vm.HandleKeyDown(letterA);
        Assert.True(vm.IsKeyPressed("C1"));
    }

    [Fact]
    public void NormalKey_HandleKeyUp_MarksReleased()
    {
        var vm = new VirtualKeyboardViewModel();
        var letterA = MakeKey("C1", KeyCategory.Alphanumeric, vk: 65);

        vm.HandleKeyDown(letterA);
        vm.HandleKeyUp(letterA);
        Assert.False(vm.IsKeyPressed("C1"));
    }

    [Fact]
    public void NormalKey_FiresKeyPressedEvent()
    {
        var vm = new VirtualKeyboardViewModel();
        var letterA = MakeKey("C1", KeyCategory.Alphanumeric, vk: 65);
        TDVKeyVisualMetadata? receivedKey = null;
        vm.KeyPressed += (s, e) => receivedKey = e.Key;

        vm.HandleKeyDown(letterA);

        Assert.NotNull(receivedKey);
        Assert.Equal("C1", receivedKey.GridPosition);
    }

    [Fact]
    public void NormalKey_FiresKeyReleasedEvent()
    {
        var vm = new VirtualKeyboardViewModel();
        var letterA = MakeKey("C1", KeyCategory.Alphanumeric, vk: 65);
        TDVKeyVisualMetadata? receivedKey = null;
        vm.KeyReleased += (s, e) => receivedKey = e.Key;

        vm.HandleKeyDown(letterA);
        vm.HandleKeyUp(letterA);

        Assert.NotNull(receivedKey);
        Assert.Equal("C1", receivedKey.GridPosition);
    }

    #endregion

    #region Null Safety

    [Fact]
    public void HandleKeyDown_Null_DoesNotThrow()
    {
        var vm = new VirtualKeyboardViewModel();
        vm.HandleKeyDown(null); // Should not throw
    }

    [Fact]
    public void HandleKeyUp_Null_DoesNotThrow()
    {
        var vm = new VirtualKeyboardViewModel();
        vm.HandleKeyUp(null); // Should not throw
    }

    #endregion

    #region LED State Management

    [Fact]
    public void LED_PowerOn_DefaultTrue()
    {
        var vm = new VirtualKeyboardViewModel();
        Assert.True(vm.GetLEDState("ON"));
    }

    [Fact]
    public void LED_AllOthers_DefaultFalse()
    {
        var vm = new VirtualKeyboardViewModel();
        Assert.False(vm.GetLEDState("CAPS"));
        Assert.False(vm.GetLEDState("LINE"));
        Assert.False(vm.GetLEDState("WAIT"));
        Assert.False(vm.GetLEDState("ERROR"));
        Assert.False(vm.GetLEDState("APP"));
        Assert.False(vm.GetLEDState("BUSY"));
        Assert.False(vm.GetLEDState("MSG"));
        Assert.False(vm.GetLEDState("CAR"));
    }

    [Fact]
    public void LED_SetState_ChangesValue()
    {
        var vm = new VirtualKeyboardViewModel();
        vm.SetLEDState("CAPS", true);
        Assert.True(vm.GetLEDState("CAPS"));

        vm.SetLEDState("CAPS", false);
        Assert.False(vm.GetLEDState("CAPS"));
    }

    [Fact]
    public void LED_UnknownName_ReturnsFalse()
    {
        var vm = new VirtualKeyboardViewModel();
        Assert.False(vm.GetLEDState("NONEXISTENT"));
    }

    #endregion

    #region Mirror Physical Key

    [Fact]
    public void MirrorPhysicalKeyPress_ValidVK_MarksPressed()
    {
        var vm = new VirtualKeyboardViewModel();
        vm.MirrorPhysicalKeyPress(38); // VK_UP
        Assert.True(vm.IsKeyPressed("C48"), "UP arrow should be pressed");
    }

    [Fact]
    public void MirrorPhysicalKeyRelease_ValidVK_MarksReleased()
    {
        var vm = new VirtualKeyboardViewModel();
        vm.MirrorPhysicalKeyPress(38);
        vm.MirrorPhysicalKeyRelease(38);
        Assert.False(vm.IsKeyPressed("C48"));
    }

    [Fact]
    public void MirrorPhysicalKeyPress_InvalidVK_DoesNotThrow()
    {
        var vm = new VirtualKeyboardViewModel();
        vm.MirrorPhysicalKeyPress(9999);
        vm.MirrorPhysicalKeyRelease(9999);
    }

    #endregion

    #region Layout Initialization

    [Fact]
    public void Layout_IsInitialized()
    {
        var vm = new VirtualKeyboardViewModel();
        Assert.NotNull(vm.Layout);
        Assert.NotNull(vm.Layout.KeyLabels);
        Assert.True(vm.Layout.KeyLabels.Count > 50, "Layout should have many key labels");
    }

    [Fact]
    public void SelectedLayout_DefaultNorwegian()
    {
        var vm = new VirtualKeyboardViewModel();
        Assert.Equal(NationalKeyboardLayout.Norwegian, vm.SelectedLayout);
        Assert.Equal("no", vm.CurrentLanguageCode);
    }

    [Fact]
    public void SelectedLayout_ChangeUpdatesLanguageCode()
    {
        var vm = new VirtualKeyboardViewModel();
        vm.SelectedLayout = NationalKeyboardLayout.Swedish;
        Assert.Equal("sv", vm.CurrentLanguageCode);
    }

    #endregion
}
