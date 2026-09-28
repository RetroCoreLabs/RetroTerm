using System;

namespace RetroTerm.Core.Terminal.Graphics;

/// <summary>
/// A colour on a graphics plane: eight bits each of alpha, red, green and blue, packed into one
/// 32-bit value.
///
/// NOT <see cref="Buffer.TerminalColor"/>, and the difference matters. A cell's colour is a
/// SEMANTIC thing — "palette index 2", "default" — that a theme is free to present however it
/// likes, and it has no notion of transparency because a cell always covers its whole box. A
/// graphics plane is the opposite: it holds literal pixels, and most of them are TRANSPARENT so
/// the text underneath shows through. A plane that could not say "nothing here" would black out
/// the screen the moment a host drew one line on it.
///
/// Packed rather than four bytes because a plane is width * height of these and they are copied,
/// compared and composited in bulk. One <c>uint</c> compare answers "is this pixel unset".
/// </summary>
public readonly struct GraphicsColor : IEquatable<GraphicsColor>
{
    /// <summary>
    /// Alpha, red, green, blue — 0xAARRGGBB.
    /// </summary>
    public uint Value { get; }

    /// <param name="argb">
    /// Packed colour, alpha in the high byte.
    /// </param>
    public GraphicsColor(uint argb) => Value = argb;

    /// <param name="r">
    /// Red channel.
    /// </param>
    /// <param name="g">
    /// Green channel.
    /// </param>
    /// <param name="b">
    /// Blue channel.
    /// </param>
    /// <param name="a">
    /// Alpha channel; opaque unless given, because a drawing command means to be visible.
    /// </param>
    public GraphicsColor(byte r, byte g, byte b, byte a = 255)
        => Value = ((uint)a << 24) | ((uint)r << 16) | ((uint)g << 8) | b;

    public byte A => (byte)(Value >> 24);
    public byte R => (byte)(Value >> 16);
    public byte G => (byte)(Value >> 8);
    public byte B => (byte)Value;

    /// <summary>
    /// Nothing drawn here. The state every pixel of a new plane starts in.
    /// </summary>
    public static GraphicsColor Transparent => new GraphicsColor(0u);

    /// <summary>
    /// Whether this pixel would show anything at all.
    /// </summary>
    public bool IsTransparent => A == 0;

    /// <summary>
    /// Whether this pixel completely hides what is behind it.
    /// </summary>
    public bool IsOpaque => A == 255;

    /// <param name="other">
    /// The colour to compare against.
    /// </param>
    /// <returns>
    /// True when both colours have identical channels.
    /// </returns>
    public bool Equals(GraphicsColor other) => Value == other.Value;
    /// <param name="obj">
    /// The object to compare against.
    /// </param>
    /// <returns>
    /// True when the object is a colour with identical channels.
    /// </returns>
    public override bool Equals(object? obj) => obj is GraphicsColor other && Equals(other);
    public override int GetHashCode() => (int)Value;

    /// <param name="left">
    /// Left operand.
    /// </param>
    /// <param name="right">
    /// Right operand.
    /// </param>
    /// <returns>
    /// True when both colours have identical channels.
    /// </returns>
    public static bool operator ==(GraphicsColor left, GraphicsColor right) => left.Equals(right);
    /// <param name="left">
    /// Left operand.
    /// </param>
    /// <param name="right">
    /// Right operand.
    /// </param>
    /// <returns>
    /// True when the colours differ in any channel.
    /// </returns>
    public static bool operator !=(GraphicsColor left, GraphicsColor right) => !left.Equals(right);

    public override string ToString() => $"#{Value:X8}";
}
