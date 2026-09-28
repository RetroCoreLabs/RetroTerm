using System;

namespace RetroTerm.Core.Terminal.Graphics;

/// <summary>
/// One layer of the graphics display: a surface, plus whether it is currently shown.
///
/// Planes exist because a terminal's graphics are not one picture. An ND terminal can hold a
/// drawing and hide it, draw into a second graphics memory while the first is displayed, and put a
/// crosshair over the top that must not become part of the drawing when the host reads the plane
/// back. A single shared surface makes all three impossible, and the crosshair one is the trap: a
/// GIN cursor scribbled onto the drawing plane is still there after the crosshair moves.
///
/// Identified by name so a protocol module can find the plane it owns without holding an index
/// that another module's plane could shift.
/// </summary>
public sealed class GraphicsPlane
{
    /// <summary>
    /// Who this plane belongs to, e.g. "nd-graphics" or "gin".
    /// </summary>
    public string Id { get; }

    /// <summary>
    /// Where this plane's pixels live.
    /// </summary>
    public InMemoryGraphicsSurface Surface { get; }

    /// <summary>
    /// Whether the compositor draws this plane. Hiding is not the same as erasing: an ND host can
    /// hide a drawing and show it again unchanged, so a hidden plane keeps every pixel.
    /// </summary>
    public bool IsVisible { get; set; } = true;

    public int Width => Surface.Width;
    public int Height => Surface.Height;

    /// <param name="id">
    /// Owner name, so a module can find its own plane without holding an index.
    /// </param>
    /// <param name="width">
    /// Width in surface pixels.
    /// </param>
    /// <param name="height">
    /// Height in surface pixels.
    /// </param>
    public GraphicsPlane(string id, int width, int height)
    {
        Id = id ?? throw new ArgumentNullException(nameof(id));
        Surface = new InMemoryGraphicsSurface(width, height);
    }

    /// <summary>
    /// Erases the plane completely, leaving whatever is below it showing through.
    /// </summary>
    public void Clear() => Surface.Clear(GraphicsColor.Transparent);
}
