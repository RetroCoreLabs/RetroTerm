using System;

namespace RetroTerm.Core.Terminal.Graphics;

/// <summary>
/// What the terminal is currently doing with incoming bytes.
/// </summary>
public enum TektronixMode
{
    /// <summary>
    /// Ordinary text. Bytes are characters.
    /// </summary>
    Alpha,

    /// <summary>
    /// Graph mode: coordinate pairs draw lines from the last point.
    /// </summary>
    Graph,

    /// <summary>
    /// Point plot: coordinate pairs light single points instead of drawing lines.
    /// </summary>
    PointPlot,

    /// <summary>
    /// Incremental plot: short moves relative to the last point.
    /// </summary>
    Incremental,
}

/// <summary>
/// Decodes the Tektronix coordinate stream — the direction a HOST draws in, and the mirror of
/// <see cref="TektronixGinEncoder"/>.
///
/// A coordinate is up to four bytes, and which is which is carried in the top bits rather than by
/// position:
/// <code>
///   0x20-0x3F   high-order five bits   (Y first, then X)
///   0x60-0x7F   low-order Y
///   0x40-0x5F   low-order X   -- and this ENDS the coordinate
/// </code>
///
/// A host may leave out any byte that has not changed since the last point, which is what makes
/// this a decoder rather than a four-byte read: a long horizontal line sends two bytes per point,
/// not four. The rule for telling a high Y from a high X is positional in a way the ranges alone
/// do not give — a high byte arriving BEFORE this coordinate's low Y is the high Y, and one
/// arriving after it is the high X.
///
/// THAT RULE IS NOT IN THE ND SPEC. <c>spec\Tektronix\nd-graphic-terminal-analysis.md</c> documents
/// the byte layout and the GIN direction it is used in, and says nothing about which bytes a host
/// may omit when drawing. The rule above is standard Tektronix 4010/4014 behaviour, applied here
/// because a decoder that demanded all four bytes would drop most real drawing traffic — but it is
/// derived, not quoted, and has not been confirmed against hardware.
///
/// Nothing is drawn here. The decoder turns bytes into finished points and hands them over; where
/// they go is the module's business.
/// </summary>
public sealed class TektronixVectorDecoder
{
    /// <summary>
    /// Handles a completed coordinate from the vector stream.
    /// </summary>
    /// <param name="logicalX">
    /// Horizontal position in the terminal's own space, where 0 is the left edge.
    /// </param>
    /// <param name="logicalY">
    /// Vertical position in the terminal's own space, where 0 is the BOTTOM edge.
    /// </param>
    public delegate void GraphicsPointHandler(int logicalX, int logicalY);

    /// <summary>
    /// Raised when a coordinate completes. Carries the point in the terminal's own space.
    /// </summary>
    /// <remarks>
    /// A named delegate rather than <c>Action of int, int</c>: two bare ints say nothing about
    /// which is which, and the Y axis here runs the opposite way to every surface and window, so
    /// getting them the wrong way round draws a plausible-looking picture that is upside down.
    /// </remarks>
    public event GraphicsPointHandler? PointReady;

    /// <summary>
    /// What incoming bytes currently mean.
    /// </summary>
    public TektronixMode Mode { get; private set; } = TektronixMode.Alpha;

    /// <summary>
    /// Whether the next completed point starts a new figure rather than continuing one.
    ///
    /// The first point after entering graph mode MOVES; every point after it DRAWS. Without that a
    /// host entering graph mode would draw a line from wherever the last drawing happened to end,
    /// which is a stray diagonal across the picture.
    /// </summary>
    public bool NextPointStartsAFigure { get; private set; } = true;

    /// <summary>
    /// Last completed point, in the terminal's own space.
    /// </summary>
    public int LastX { get; private set; }

    /// <summary>
    /// Last completed point, in the terminal's own space.
    /// </summary>
    public int LastY { get; private set; }

    /// <summary>
    /// Bytes seen in incremental-plot mode that are none of the ten characters Table F-5 defines.
    /// Non-zero means a host is using something this decoder does not know about.
    /// </summary>
    public int IncrementalBytesIgnored { get; private set; }

    // The four pieces of a coordinate. Kept between points on purpose: that is what lets a host
    // send only the bytes that changed.
    private int _highY;
    private int _lowY;
    private int _highX;
    private int _lowX;

    /// <summary>
    /// Whether this coordinate has seen its low Y yet, which is what separates high Y from high X.
    /// </summary>
    private bool _lowYSeen;

    /// <summary>
    /// Feeds one byte. Returns true when the decoder consumed it.
    /// </summary>
    /// <returns>
    /// False when the byte is not part of the coordinate stream and the caller should handle it —
    /// in alpha mode that is every byte, and in graph mode it is anything outside 0x20..0x7F.
    /// </returns>
    /// <param name="b">
    /// The byte arriving from the host.
    /// </param>
    public bool Consume(byte b)
    {
        switch (b)
        {
            case 0x1D:  // GS - graph mode
                Mode = TektronixMode.Graph;
                BeginFigure();
                return true;

            case 0x1C:  // FS - point plot
                Mode = TektronixMode.PointPlot;
                BeginFigure();
                return true;

            case 0x1E:  // RS - incremental plot
                Mode = TektronixMode.Incremental;
                BeginFigure();
                return true;

            case 0x1F:  // US - back to text
                Mode = TektronixMode.Alpha;
                BeginFigure();
                return true;
        }

        if (Mode == TektronixMode.Alpha) return false;

        // Inside a drawing mode, a control code ends the drawing rather than being plotted. CAN and
        // ESC both appear mid-stream in the real command traces, and swallowing them would strand
        // the terminal in graph mode with the host talking text at it.
        if (b < 0x20)
        {
            if (b == 0x18)   // CAN
            {
                Mode = TektronixMode.Alpha;
                BeginFigure();
                return true;
            }
            return false;
        }

        if (Mode == TektronixMode.Incremental)
        {
            return ConsumeIncremental(b);
        }

        if (b >= 0x20 && b <= 0x3F)
        {
            // High-order five bits. Before this coordinate's low Y it is the high Y; after it, the
            // high X.
            if (_lowYSeen)
            {
                _highX = b & 0x1F;
            }
            else
            {
                _highY = b & 0x1F;
            }
            return true;
        }

        if (b >= 0x60 && b <= 0x7F)
        {
            _lowY = b & 0x1F;
            _lowYSeen = true;
            return true;
        }

        if (b >= 0x40 && b <= 0x5F)
        {
            // Low X ends the coordinate.
            _lowX = b & 0x1F;

            LastX = (_highX << 5) | _lowX;
            LastY = (_highY << 5) | _lowY;
            _lowYSeen = false;

            PointReady?.Invoke(LastX, LastY);
            NextPointStartsAFigure = false;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Whether the beam is down, in incremental plot mode. Set by SP and P.
    /// </summary>
    public bool IncrementalBeamOn { get; private set; }

    /// <summary>
    /// Incremental plot: one-step moves in eight directions, from Table F-5 of the 4014 manual
    /// (<c>spec\Tektronix\4014-um.pdf</c>, Appendix F, Enhanced Graphics Module).
    ///
    /// <code>
    ///   SP  beam off (pen up)     P  beam on (pen down)
    ///   D   N        E   NE       A  E        I  SE
    ///   H   S        J   SW       B  W        F  NW
    /// </code>
    ///
    /// "The character that follows RS must be a Write Command — Beam off or Beam on… The write
    /// status does not change until a different write command is received." So the beam setting is
    /// sticky across steps, and only SP or P changes it.
    ///
    /// North is +Y: this is the terminal's own space, where Y increases UPWARD.
    ///
    /// This replaces a version that swallowed and counted these bytes because the encoding was not
    /// known. It was in the manual sitting in this repo the whole time.
    /// </summary>
    private bool ConsumeIncremental(byte b)
    {
        switch (b)
        {
            case (byte)' ':
                IncrementalBeamOn = false;
                return true;

            case (byte)'P':
                IncrementalBeamOn = true;
                return true;
        }

        int stepX = 0;
        int stepY = 0;

        switch (b)
        {
            case (byte)'D': stepY = 1; break;                   // N
            case (byte)'E': stepX = 1; stepY = 1; break;        // NE
            case (byte)'A': stepX = 1; break;                   // E
            case (byte)'I': stepX = 1; stepY = -1; break;       // SE
            case (byte)'H': stepY = -1; break;                  // S
            case (byte)'J': stepX = -1; stepY = -1; break;      // SW
            case (byte)'B': stepX = -1; break;                  // W
            case (byte)'F': stepX = -1; stepY = 1; break;       // NW

            default:
                // Not part of this grammar. Counted rather than guessed at, the way unknown bytes
                // have been all along.
                IncrementalBytesIgnored++;
                return true;
        }

        LastX += stepX;
        LastY += stepY;

        // A pen-up step MOVES. Reporting it as a draw would join every repositioning with a line,
        // which is how an incremental plot turns into a scribble.
        NextPointStartsAFigure = !IncrementalBeamOn;

        PointReady?.Invoke(LastX, LastY);
        return true;
    }

    /// <summary>
    /// Makes the next point a move rather than a draw, and drops any half-collected coordinate.
    ///
    /// Called on every mode change: a coordinate interrupted by a mode switch is not a coordinate,
    /// and carrying its bytes into the next one would put a point somewhere nobody asked for.
    /// </summary>
    public void BeginFigure()
    {
        NextPointStartsAFigure = true;
        _lowYSeen = false;
    }

    /// <summary>
    /// Full reset, for RIS and for a fresh connection.
    /// </summary>
    public void Reset()
    {
        Mode = TektronixMode.Alpha;
        _highY = _lowY = _highX = _lowX = 0;
        LastX = LastY = 0;
        IncrementalBytesIgnored = 0;
        IncrementalBeamOn = false;
        BeginFigure();
    }
}
