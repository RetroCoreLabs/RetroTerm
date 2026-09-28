using System;

namespace RetroTerm.Core.Terminal.Buffer;

/// <summary>
/// Represents the visual attributes of a terminal character cell.
/// Designed for zero-allocation and high-performance operations.
/// </summary>
[Flags]
public enum CharacterAttributes : ushort
{
    None = 0,

    // Basic text attributes (SGR - Select Graphic Rendition)
    Bold = 1 << 0,           // SGR 1
    Dim = 1 << 1,            // SGR 2
    Italic = 1 << 2,         // SGR 3
    Underline = 1 << 3,      // SGR 4
    Blink = 1 << 4,          // SGR 5
    RapidBlink = 1 << 5,     // SGR 6
    Reverse = 1 << 6,        // SGR 7 (inverse video)
    Hidden = 1 << 7,         // SGR 8 (concealed)
    Strikethrough = 1 << 8,  // SGR 9

    // Double width/height (DEC specific)
    DoubleWidth = 1 << 9,    // DECDHL, DECDWL
    DoubleHeightTop = 1 << 10,    // DECDHL top half
    DoubleHeightBottom = 1 << 11, // DECDHL bottom half

    // Special attributes
    Protected = 1 << 12,     // Used for protected fields (3270, forms)

    // Reserved for future use
    Reserved1 = 1 << 13,
    Reserved2 = 1 << 14,
    Reserved3 = 1 << 15
}

/// <summary>
/// Extension methods for CharacterAttributes operations
/// </summary>
public static class CharacterAttributesExtensions
{
    /// <summary>
    /// Checks if the specified attribute flag is set
    /// </summary>
    public static bool HasAttribute(this CharacterAttributes attributes, CharacterAttributes flag)
    {
        return (attributes & flag) == flag;
    }

    /// <summary>
    /// Sets the specified attribute flag
    /// </summary>
    public static CharacterAttributes SetAttribute(this CharacterAttributes attributes, CharacterAttributes flag)
    {
        return attributes | flag;
    }

    /// <summary>
    /// Clears the specified attribute flag
    /// </summary>
    public static CharacterAttributes ClearAttribute(this CharacterAttributes attributes, CharacterAttributes flag)
    {
        return attributes & ~flag;
    }

    /// <summary>
    /// Toggles the specified attribute flag
    /// </summary>
    public static CharacterAttributes ToggleAttribute(this CharacterAttributes attributes, CharacterAttributes flag)
    {
        return attributes ^ flag;
    }

    /// <summary>
    /// Checks if any double-size attributes are set
    /// </summary>
    public static bool IsDoubleSize(this CharacterAttributes attributes)
    {
        return attributes.HasAttribute(CharacterAttributes.DoubleWidth) ||
               attributes.HasAttribute(CharacterAttributes.DoubleHeightTop) ||
               attributes.HasAttribute(CharacterAttributes.DoubleHeightBottom);
    }
}

