using System;

namespace RetroTerm.Core.Terminal.Graphics;

/// <summary>
/// The ONE place logical graphics coordinates become surface pixels, and back.
///
/// Every graphics protocol this project will grow speaks its own space and none of them speaks the
/// screen's. ND graphics and Tektronix 4010/4014 both use a grid with the ORIGIN AT THE BOTTOM LEFT
/// and Y increasing UPWARD — the opposite of every surface, window and bitmap — over 1024 x 780
/// addressable points (verified in <c>spec\Tektronix\nd-graphic-terminal-analysis.md</c>, not
/// assumed: the architecture review says "0..4095-style" loosely and the real ND crosshair space is
/// 1024 x 780).
///
/// The rule from section C.7 of the architecture review is that neither the protocol module nor the
/// renderer does this arithmetic. A protocol says "draw to 512,390" in its own space; the surface
/// takes surface pixels; this sits between them. Two copies of a Y flip in two modules is how a
/// crosshair ends up half a screen away from the line it is supposed to be pointing at, with each
/// module looking correct on its own.
///
/// Nothing here throws or refuses. Mapping a point outside the logical space returns a surface
/// coordinate outside the surface, and the surface clips it — a protocol is entitled to draw off
/// the edge, and making that an error at this layer would push the special case into every caller.
/// </summary>
public sealed class GraphicsViewport
{
    /// <summary>
    /// Addressable width of the protocol's space, e.g. 1024 for Tek 4014.
    /// </summary>
    public int LogicalWidth { get; }

    /// <summary>
    /// Addressable height of the protocol's space, e.g. 780 for Tek 4014.
    /// </summary>
    public int LogicalHeight { get; }

    /// <summary>
    /// Whether logical Y increases UPWARD, as it does on Tektronix and ND. False means the logical
    /// space already agrees with the surface and no flip is needed.
    /// </summary>
    public bool LogicalYIncreasesUpward { get; }

    /// <summary>
    /// Width of the surface being drawn on, in pixels.
    /// </summary>
    public int SurfaceWidth { get; }

    /// <summary>
    /// Height of the surface being drawn on, in pixels.
    /// </summary>
    public int SurfaceHeight { get; }

    /// <summary>
    /// Whether the logical space keeps its shape, letterboxing where the surface is a different
    /// shape. A Tek screen is roughly 4:3 and stretching it to fill a wide window turns circles
    /// into ellipses, so this defaults on.
    /// </summary>
    public bool PreserveAspectRatio { get; }

    /// <summary>
    /// Surface pixels per logical unit horizontally.
    /// </summary>
    public double ScaleX { get; }

    /// <summary>
    /// Surface pixels per logical unit vertically.
    /// </summary>
    public double ScaleY { get; }

    /// <summary>
    /// Left edge of the drawn area within the surface; non-zero only when letterboxed.
    /// </summary>
    public double OffsetX { get; }

    /// <summary>
    /// Top edge of the drawn area within the surface; non-zero only when letterboxed.
    /// </summary>
    public double OffsetY { get; }

    public GraphicsViewport(
        int logicalWidth, int logicalHeight,
        int surfaceWidth, int surfaceHeight,
        bool logicalYIncreasesUpward = true,
        bool preserveAspectRatio = true)
    {
        if (logicalWidth <= 0) throw new ArgumentOutOfRangeException(nameof(logicalWidth));
        if (logicalHeight <= 0) throw new ArgumentOutOfRangeException(nameof(logicalHeight));
        if (surfaceWidth <= 0) throw new ArgumentOutOfRangeException(nameof(surfaceWidth));
        if (surfaceHeight <= 0) throw new ArgumentOutOfRangeException(nameof(surfaceHeight));

        LogicalWidth = logicalWidth;
        LogicalHeight = logicalHeight;
        SurfaceWidth = surfaceWidth;
        SurfaceHeight = surfaceHeight;
        LogicalYIncreasesUpward = logicalYIncreasesUpward;
        PreserveAspectRatio = preserveAspectRatio;

        // The logical space is a grid of POINTS, not of boxes: 1024 addressable columns means the
        // outermost points are 0 and 1023, so the distance across is 1023 units, not 1024. Dividing
        // by the count instead would leave the far edge one pixel short of the surface and put a
        // visible sliver of unpainted screen down the right and bottom.
        double spanX = logicalWidth > 1 ? logicalWidth - 1 : 1;
        double spanY = logicalHeight > 1 ? logicalHeight - 1 : 1;

        double fitX = (surfaceWidth - 1) / spanX;
        double fitY = (surfaceHeight - 1) / spanY;

        if (preserveAspectRatio)
        {
            double uniform = fitX < fitY ? fitX : fitY;
            ScaleX = uniform;
            ScaleY = uniform;
            OffsetX = (surfaceWidth - 1 - spanX * uniform) / 2.0;
            OffsetY = (surfaceHeight - 1 - spanY * uniform) / 2.0;
        }
        else
        {
            ScaleX = fitX;
            ScaleY = fitY;
            OffsetX = 0.0;
            OffsetY = 0.0;
        }
    }

    /// <summary>
    /// Whether a logical point is inside the addressable space.
    /// </summary>
    /// <param name="logicalX">
    /// Horizontal position in the protocol's own space.
    /// </param>
    /// <param name="logicalY">
    /// Vertical position in the protocol's own space.
    /// </param>
    /// <returns>
    /// True when the point is inside the addressable space.
    /// </returns>
    public bool ContainsLogical(int logicalX, int logicalY)
        => logicalX >= 0 && logicalX < LogicalWidth && logicalY >= 0 && logicalY < LogicalHeight;

    /// <summary>
    /// Logical point to surface pixel. Points outside the logical space map outside the surface;
    /// the surface clips them.
    /// </summary>
    public void ToSurface(int logicalX, int logicalY, out int surfaceX, out int surfaceY)
    {
        // The flip is HERE and nowhere else. Tektronix and ND put 0 at the bottom.
        double flippedY = LogicalYIncreasesUpward ? (LogicalHeight - 1 - logicalY) : logicalY;

        surfaceX = (int)Math.Round(OffsetX + logicalX * ScaleX);
        surfaceY = (int)Math.Round(OffsetY + flippedY * ScaleY);
    }

    /// <summary>
    /// Surface pixel back to a logical point — the direction GIN needs, where a pointer on screen
    /// has to be reported to the host in the protocol's own space.
    ///
    /// Clamped to the logical space on purpose: a pointer can sit in the letterbox margin, and a
    /// crosshair report of "-3" is not something a host can do anything sensible with.
    /// </summary>
    public void ToLogical(int surfaceX, int surfaceY, out int logicalX, out int logicalY)
    {
        double x = ScaleX != 0.0 ? (surfaceX - OffsetX) / ScaleX : 0.0;
        double y = ScaleY != 0.0 ? (surfaceY - OffsetY) / ScaleY : 0.0;

        if (LogicalYIncreasesUpward)
        {
            y = LogicalHeight - 1 - y;
        }

        logicalX = Clamp((int)Math.Round(x), 0, LogicalWidth - 1);
        logicalY = Clamp((int)Math.Round(y), 0, LogicalHeight - 1);
    }

    private static int Clamp(int value, int low, int high)
    {
        if (value < low) return low;
        if (value > high) return high;
        return value;
    }
}
