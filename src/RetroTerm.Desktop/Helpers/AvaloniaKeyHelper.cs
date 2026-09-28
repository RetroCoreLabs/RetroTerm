using Avalonia.Input;
using RetroTerm.Core.Terminal.Input;

namespace RetroTerm.Desktop.Helpers;

/// <summary>
/// Shared helper for converting Avalonia key types to core keyboard types.
/// Eliminates duplicate conversion logic across TerminalCanvas and VirtualKeyboardWindow.
/// </summary>
public static class AvaloniaKeyHelper
{
    /// <summary>
    /// Convert Avalonia Key to Windows VK code using TDVKeyBindingConfiguration mapping.
    /// </summary>
    public static int ToVKCode(Key key)
        => TDVKeyBindingConfiguration.AvaloniaKeyToVK((int)key);

    /// <summary>
    /// Convert Avalonia Key to a VK-aligned integer for the keyboard mapper.
    ///
    /// Identical to <see cref="ToVKCode"/>, and kept only because call sites name it. It used to
    /// re-implement the letter and digit ranges itself before delegating — the same two ranges
    /// AvaloniaKeyToVK already handles, with the same offsets. Two copies of a key table is how
    /// the two silently drift apart, and there is nothing here that the one table does not do.
    /// </summary>
    public static int ToVKCodeForMapper(Key key) => ToVKCode(key);

    /// <summary>
    /// Whether this key belongs to the numeric keypad.
    ///
    /// Kept separate from <see cref="IsSpecialKey"/> on purpose: a keypad key is only special
    /// while application keypad mode is on. In numeric mode it is an ordinary character and must
    /// keep travelling the normal text path, or it would be delivered twice.
    /// </summary>
    public static bool IsKeypadKey(Key key)
    {
        if (key >= Key.NumPad0 && key <= Key.NumPad9) return true;

        return key switch
        {
            Key.Multiply or Key.Add or Key.Separator or
            Key.Subtract or Key.Decimal or Key.Divide => true,
            _ => false
        };
    }

    /// <summary>
    /// Convert Avalonia KeyModifiers to Core KeyModifiers.
    /// </summary>
    public static Core.Terminal.Input.KeyModifiers ConvertModifiers(Avalonia.Input.KeyModifiers modifiers)
    {
        var result = Core.Terminal.Input.KeyModifiers.None;
        if (modifiers.HasFlag(Avalonia.Input.KeyModifiers.Shift)) result |= Core.Terminal.Input.KeyModifiers.Shift;
        if (modifiers.HasFlag(Avalonia.Input.KeyModifiers.Control)) result |= Core.Terminal.Input.KeyModifiers.Ctrl;
        if (modifiers.HasFlag(Avalonia.Input.KeyModifiers.Alt)) result |= Core.Terminal.Input.KeyModifiers.Alt;
        if (modifiers.HasFlag(Avalonia.Input.KeyModifiers.Meta)) result |= Core.Terminal.Input.KeyModifiers.Meta;
        return result;
    }

    /// <summary>
    /// Check if a key is a special key that needs terminal-specific mapping.
    /// </summary>
    public static bool IsSpecialKey(Key key)
    {
        return key switch
        {
            Key.Up or Key.Down or Key.Left or Key.Right or
            Key.Home or Key.End or Key.PageUp or Key.PageDown or
            Key.Insert or Key.Delete or
            Key.F1 or Key.F2 or Key.F3 or Key.F4 or Key.F5 or Key.F6 or
            Key.F7 or Key.F8 or Key.F9 or Key.F10 or Key.F11 or Key.F12 or
            Key.Tab or Key.Escape or Key.Back or Key.Enter => true,
            _ => false
        };
    }
}
