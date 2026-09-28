using System;
using System.Collections.Generic;

namespace RetroTerm.Core.Terminal.Graphics;

/// <summary>
/// Flattens the graphics planes into one picture for the renderer to put over the text.
///
/// WHAT THIS DOES NOT DO. The architecture review's diagram shows the text plane feeding the
/// compositor too, but Core has no glyph rasteriser and is not getting one — fonts, shaping and
/// the glyph cache all live in the Desktop renderer, and dragging them down here to satisfy a
/// diagram would be the tail wagging the dog. So the division is: this flattens the GRAPHICS
/// planes and the GIN overlay, the renderer draws the text and blits the result on top. The text
/// showing through is what the transparent pixels are for.
///
/// Planes are held bottom to top in the order they were added. Compositing is source-over alpha:
/// a fully opaque pixel replaces what is under it, a fully transparent one leaves it alone, and
/// anything between blends. Most terminal graphics are one or the other, but Sixel carries real
/// colours and getting the general case right costs a few lines here rather than a special case in
/// every protocol.
/// </summary>
public sealed class GraphicsCompositor
{
    private readonly List<GraphicsPlane> _planes = new List<GraphicsPlane>();

    /// <summary>
    /// Width every plane and the output share, in surface pixels.
    /// </summary>
    public int Width { get; }

    /// <summary>
    /// Height every plane and the output share, in surface pixels.
    /// </summary>
    public int Height { get; }

    /// <summary>
    /// The flattened picture, as last published by <see cref="Composite"/>. Read by the renderer,
    /// which blits it over the text it has already drawn.
    ///
    /// DOUBLE BUFFERED, for the same reason <c>ScreenFrame</c> is. Compositing happens on the
    /// session pump; the renderer reads this on the UI thread whenever it happens to draw. Writing
    /// into the surface the renderer is reading would tear the picture - half this frame, half the
    /// last. So <see cref="Composite"/> fills the surface that is NOT published and swaps the
    /// reference at the end, which is a single write.
    /// </summary>
    public InMemoryGraphicsSurface Output => System.Threading.Volatile.Read(ref _published);

    private InMemoryGraphicsSurface _published;
    private InMemoryGraphicsSurface _back;

    /// <summary>
    /// How many planes exist, visible or not.
    /// </summary>
    public int PlaneCount => _planes.Count;

    /// <param name="width">
    /// Width shared by every plane and the output, in surface pixels.
    /// </param>
    /// <param name="height">
    /// Height shared by every plane and the output, in surface pixels.
    /// </param>
    public GraphicsCompositor(int width, int height)
    {
        if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));

        Width = width;
        Height = height;
        _published = new InMemoryGraphicsSurface(width, height);
        _back = new InMemoryGraphicsSurface(width, height);
    }

    /// <summary>
    /// Adds a plane on top of the ones already there, and returns it.
    /// </summary>
    /// <param name="id">
    /// Owner name, e.g. "nd-graphics" or "gin". Must not already exist.
    /// </param>
    public GraphicsPlane AddPlane(string id)
    {
        if (id == null) throw new ArgumentNullException(nameof(id));
        if (FindPlane(id) != null)
        {
            throw new ArgumentException($"A plane called '{id}' already exists.", nameof(id));
        }

        var plane = new GraphicsPlane(id, Width, Height);
        _planes.Add(plane);
        return plane;
    }

    /// <summary>
    /// The plane with this name, or null. Lets a module reach its own plane by name.
    /// </summary>
    /// <param name="id">
    /// Owner name the plane was added under.
    /// </param>
    /// <returns>
    /// The plane, or null when no plane carries that name.
    /// </returns>
    public GraphicsPlane? FindPlane(string id)
    {
        for (int i = 0; i < _planes.Count; i++)
        {
            if (string.Equals(_planes[i].Id, id, StringComparison.Ordinal))
            {
                return _planes[i];
            }
        }
        return null;
    }

    /// <summary>
    /// The plane at a depth, 0 being the bottom.
    /// </summary>
    public GraphicsPlane PlaneAt(int index) => _planes[index];

    /// <summary>
    /// Scrolls every plane up by a number of pixel rows, so the whole picture travels
    /// with the text. See <see cref="InMemoryGraphicsSurface.ScrollUp"/> for why.
    ///
    /// Every plane moves, including the graphics-input crosshair: on a real VT340 there
    /// is one bitmap, so nothing drawn on it can stay behind while the rest slides.
    /// </summary>
    /// <param name="pixelRows">
    /// How far to move. Zero or less does nothing.
    /// </param>
    public void ScrollUp(int pixelRows)
    {
        if (pixelRows <= 0) return;
        for (int i = 0; i < _planes.Count; i++)
        {
            _planes[i].Surface.ScrollUp(pixelRows);
        }
    }

    /// <summary>
    /// Wipes every plane. What a full-screen erase does to the graphics.
    /// </summary>
    public void ClearAllPlanes()
    {
        for (int i = 0; i < _planes.Count; i++)
        {
            _planes[i].Clear();
        }
    }

    /// <summary>
    /// Whether any plane has a pixel that would show. Lets the renderer skip the blit.
    /// </summary>
    public bool HasAnythingToDraw()
    {
        for (int i = 0; i < _planes.Count; i++)
        {
            var plane = _planes[i];
            if (!plane.IsVisible) continue;

            var pixels = plane.Surface.Pixels;
            for (int p = 0; p < pixels.Length; p++)
            {
                // The alpha byte alone answers it, and a plane is mostly empty, so this bails on
                // the first lit pixel rather than walking the whole plane.
                if ((pixels[p] >> 24) != 0) return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Rebuilds <see cref="Output"/> from the visible planes, bottom to top.
    /// </summary>
    public void Composite()
    {
        // Build into the back surface, never the one the renderer may be reading.
        _target = _back;
        _target.Clear(GraphicsColor.Transparent);

        for (int i = 0; i < _planes.Count; i++)
        {
            var plane = _planes[i];
            if (!plane.IsVisible) continue;

            BlendOver(plane.Surface);
        }

        // One reference write publishes the finished picture. The surface that was published
        // becomes the next back buffer.
        var previous = _published;
        System.Threading.Volatile.Write(ref _published, _back);
        _back = previous;
    }

    /// <summary>
    /// Where the current Composite is writing. Only meaningful during one.
    /// </summary>
    private InMemoryGraphicsSurface? _target;

    /// <summary>
    /// Source-over blend of one plane onto the output.
    /// </summary>
    private void BlendOver(InMemoryGraphicsSurface source)
    {
        var src = source.Pixels;

        for (int p = 0; p < src.Length; p++)
        {
            uint s = src[p];
            int sourceAlpha = (int)(s >> 24);

            // The two cases that are nearly all of the traffic, exact and cheap.
            if (sourceAlpha == 0) continue;
            if (sourceAlpha == 255)
            {
                WriteOutput(p, s);
                continue;
            }

            uint d = _target!.Pixels[p];
            int destAlpha = (int)(d >> 24);
            int inverse = 255 - sourceAlpha;

            int outAlpha = sourceAlpha + Div255(destAlpha * inverse);
            if (outAlpha <= 0)
            {
                WriteOutput(p, 0u);
                continue;
            }

            // Straight (non-premultiplied) alpha, so each channel is weighted by its own alpha and
            // divided back out at the end. Every term fits an int: 255 * 255 * 255 is about 16.6M.
            int r = ChannelOver((int)((s >> 16) & 0xFF), sourceAlpha, (int)((d >> 16) & 0xFF), destAlpha, inverse, outAlpha);
            int g = ChannelOver((int)((s >> 8) & 0xFF), sourceAlpha, (int)((d >> 8) & 0xFF), destAlpha, inverse, outAlpha);
            int b = ChannelOver((int)(s & 0xFF), sourceAlpha, (int)(d & 0xFF), destAlpha, inverse, outAlpha);

            WriteOutput(p, ((uint)outAlpha << 24) | ((uint)r << 16) | ((uint)g << 8) | (uint)b);
        }
    }

    private static int ChannelOver(int sourceChannel, int sourceAlpha, int destChannel, int destAlpha,
        int inverse, int outAlpha)
    {
        int value = (sourceChannel * sourceAlpha + Div255(destChannel * destAlpha * inverse)) / outAlpha;
        return value > 255 ? 255 : value;
    }

    /// <summary>
    /// Divide by 255 with rounding, without a floating-point round trip.
    /// </summary>
    private static int Div255(int value) => (value + 127) / 255;

    /// <summary>
    /// Writes straight into the output's storage.
    ///
    /// <see cref="InMemoryGraphicsSurface.Pixels"/> is deliberately read-only so nothing can
    /// scribble on a surface behind its back, so the compositor goes through SetPixel - but it
    /// already knows the index, and recomputing x and y from it just to have SetPixel turn them
    /// back into the same index would be silly. Hence the arithmetic here.
    /// </summary>
    private void WriteOutput(int index, uint value)
    {
        int y = index / Width;
        int x = index - y * Width;
        _target!.SetPixel(x, y, new GraphicsColor(value));
    }
}
