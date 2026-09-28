using System;
using System.Runtime.InteropServices;

namespace RetroTerm.Core.Terminal.Buffer;

/// <summary>
/// Represents a terminal color that can be:
/// - A standard 16-color (0-15)
/// - An extended 256-color (0-255)
/// - A 24-bit RGB color
/// - Default terminal color
/// </summary>
[StructLayout(LayoutKind.Explicit)]
public readonly struct TerminalColor : IEquatable<TerminalColor>
{
    /// <summary>
    /// Color mode/type
    /// </summary>
    [FieldOffset(0)]
    private readonly ColorMode _mode;

    /// <summary>
    /// For indexed colors (0-255), this is the palette index
    /// For RGB colors, this is unused
    /// </summary>
    [FieldOffset(1)]
    private readonly byte _index;

    /// <summary>
    /// For RGB colors: Red component (0-255)
    /// </summary>
    [FieldOffset(2)]
    private readonly byte _r;

    /// <summary>
    /// For RGB colors: Green component (0-255)
    /// </summary>
    [FieldOffset(3)]
    private readonly byte _g;

    /// <summary>
    /// For RGB colors: Blue component (0-255)
    /// </summary>
    [FieldOffset(4)]
    private readonly byte _b;

    private enum ColorMode : byte
    {
        Default = 0,
        Indexed = 1,
        RGB = 2
    }

    private TerminalColor(ColorMode mode, byte index, byte r, byte g, byte b)
    {
        _mode = mode;
        _index = index;
        _r = r;
        _g = g;
        _b = b;
    }

    /// <summary>
    /// Default terminal color (uses terminal's default foreground or background)
    /// </summary>
    public static readonly TerminalColor Default = new(ColorMode.Default, 0, 0, 0, 0);

    /// <summary>
    /// Creates an indexed color (0-255 from terminal color palette)
    /// </summary>
    public static TerminalColor FromIndex(byte index) => new(ColorMode.Indexed, index, 0, 0, 0);

    /// <summary>
    /// Creates a 24-bit RGB color
    /// </summary>
    public static TerminalColor FromRgb(byte r, byte g, byte b) => new(ColorMode.RGB, 0, r, g, b);

    /// <summary>
    /// Gets whether this is the default terminal color
    /// </summary>
    public bool IsDefault => _mode == ColorMode.Default;

    /// <summary>
    /// Gets whether this is an indexed color
    /// </summary>
    public bool IsIndexed => _mode == ColorMode.Indexed;

    /// <summary>
    /// Gets whether this is an RGB color
    /// </summary>
    public bool IsRgb => _mode == ColorMode.RGB;

    /// <summary>
    /// Gets the palette index (only valid if IsIndexed is true)
    /// </summary>
    public byte Index => _index;

    /// <summary>
    /// Gets the RGB components (converts indexed to RGB if needed)
    /// </summary>
    public (byte R, byte G, byte B) ToRgb()
    {
        return _mode switch
        {
            ColorMode.RGB => (_r, _g, _b),
            ColorMode.Indexed => IndexToRgb(_index),
            _ => (0, 0, 0) // Default
        };
    }

    /// <summary>
    /// Converts an indexed color (0-255) to RGB using the standard xterm 256-color palette.
    ///
    /// The table used to be written out here as well as in the renderer, and the two disagreed on
    /// the colour cube — see <see cref="TerminalPalette"/> for what was wrong and why the existing
    /// test could not catch it.
    /// </summary>
    private static (byte R, byte G, byte B) IndexToRgb(byte index) => TerminalPalette.GetRgb(index);

    public bool Equals(TerminalColor other)
    {
        if (_mode != other._mode)
            return false;

        return _mode switch
        {
            ColorMode.Default => true,
            ColorMode.Indexed => _index == other._index,
            ColorMode.RGB => _r == other._r && _g == other._g && _b == other._b,
            _ => false
        };
    }

    public override bool Equals(object? obj) => obj is TerminalColor color && Equals(color);

    public override int GetHashCode()
    {
        return _mode switch
        {
            ColorMode.Default => 0,
            ColorMode.Indexed => HashCode.Combine(_mode, _index),
            ColorMode.RGB => HashCode.Combine(_mode, _r, _g, _b),
            _ => 0
        };
    }

    public static bool operator ==(TerminalColor left, TerminalColor right) => left.Equals(right);
    public static bool operator !=(TerminalColor left, TerminalColor right) => !left.Equals(right);

    public override string ToString()
    {
        return _mode switch
        {
            ColorMode.Default => "Default",
            ColorMode.Indexed => $"Index({_index})",
            ColorMode.RGB => $"RGB({_r},{_g},{_b})",
            _ => "Unknown"
        };
    }
}

/// <summary>
/// Standard terminal color indices (0-15)
/// </summary>
public static class StandardColors
{
    public const byte Black = 0;
    public const byte Red = 1;
    public const byte Green = 2;
    public const byte Yellow = 3;
    public const byte Blue = 4;
    public const byte Magenta = 5;
    public const byte Cyan = 6;
    public const byte White = 7;
    public const byte BrightBlack = 8;
    public const byte BrightRed = 9;
    public const byte BrightGreen = 10;
    public const byte BrightYellow = 11;
    public const byte BrightBlue = 12;
    public const byte BrightMagenta = 13;
    public const byte BrightCyan = 14;
    public const byte BrightWhite = 15;
}

