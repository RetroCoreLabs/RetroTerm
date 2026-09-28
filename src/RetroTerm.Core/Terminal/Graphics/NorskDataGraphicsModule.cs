using System;
using System.Collections.Generic;
using RetroTerm.Core.Terminal.Graphics;

namespace RetroTerm.Core.Terminal.Graphics;

/// <summary>
/// The Norsk Data graphics protocol — the first consumer of the graphics foundation.
///
/// An ND graphic terminal is a Tektronix 4014-compatible terminal with proprietary extensions. The
/// Tek half (vector drawing, point-plot mode, GIN) is shared; the ND half arrives as
/// <c>ESC " params final</c> sequences and adds rectangles, polygons, circles, arcs, font download,
/// graphic memory access and window operations.
///
/// WHAT THIS CLASS DOES AND DOES NOT DO. The spec
/// (<c>spec\Tektronix\nd-graphic-terminal-analysis.md</c>) documents THIRTY modes, recovered from a
/// Ghidra analysis of a 1986 ND test program. Some are pinned down exactly — mode 9 clears graphic
/// memory, mode 8 fills a rectangle from four coordinates. Others are named but their parameter
/// meanings are not stated: "set polygon/shape drawing mode" with an unexplained mode number is not
/// something that can be implemented without inventing the semantics.
///
/// So the ones that are unambiguous are implemented, and every other sequence is COUNTED rather
/// than guessed at. <see cref="UnhandledSequences"/> is not a placeholder — it is the instrument:
/// pointed at a real ND host it says which modes actually matter, which is a far better guide to
/// what to build next than working down the table in order.
/// </summary>
public sealed class NorskDataGraphicsModule
{
    /// <summary>
    /// The name this module's plane is registered under.
    /// </summary>
    public const string DrawingPlaneId = "nd-graphics";

    /// <summary>
    /// The name of the crosshair overlay plane.
    /// </summary>
    public const string CrosshairPlaneId = "nd-gin";

    private readonly GraphicsCompositor _compositor;
    private readonly GraphicsViewport _viewport;
    private readonly GraphicsPlane _drawing;
    private readonly GraphicsPlane _crosshair;

    private readonly Dictionary<string, int> _unhandled = new Dictionary<string, int>();

    private readonly TektronixPlotter _plotter;

    /// <summary>
    /// Where picks are reported from. Shared with the terminal's input path.
    /// </summary>
    public GinRouter Gin { get; }

    /// <summary>
    /// Colour the ND plane draws with. One phosphor: this is a 1980s graphics terminal.
    /// </summary>
    public GraphicsColor DrawColour
    {
        get => _plotter.DrawColour;
        set => _plotter.DrawColour = value;
    }

    /// <summary>
    /// Sequences this module recognised the SHAPE of but does not act on, keyed "mode:final".
    ///
    /// Deliberately a count rather than a log line: pointed at a real host it answers "which of the
    /// thirty modes does this program actually use", and that is the honest way to choose what to
    /// implement next.
    /// </summary>
    public IReadOnlyDictionary<string, int> UnhandledSequences => _unhandled;

    /// <summary>
    /// The Tektronix side of the terminal: GS/FS/US mode switching and the coordinate stream.
    ///
    /// An ND graphic terminal is a 4014 with extensions, and this is the 4014 half. Most real
    /// drawing traffic comes through here rather than through <c>ESC "</c> - vectors are what a
    /// plotting program sends; the ND sequences are what it sends to set things up first.
    /// </summary>
    public TektronixVectorDecoder Vectors => _plotter.Vectors;

    /// <summary>
    /// Builds the module's drawing and crosshair planes on the given compositor.
    /// </summary>
    /// <param name="compositor">
    /// Where this module's planes live.
    /// </param>
    /// <param name="viewport">
    /// The transform between the terminal's own space and surface pixels.
    /// </param>
    public NorskDataGraphicsModule(GraphicsCompositor compositor, GraphicsViewport viewport)
    {
        _compositor = compositor ?? throw new ArgumentNullException(nameof(compositor));
        _viewport = viewport ?? throw new ArgumentNullException(nameof(viewport));

        _drawing = compositor.FindPlane(DrawingPlaneId) ?? compositor.AddPlane(DrawingPlaneId);
        _crosshair = compositor.FindPlane(CrosshairPlaneId) ?? compositor.AddPlane(CrosshairPlaneId);

        // The graphics plane starts HIDDEN. A terminal shows text until a host asks for graphics
        // (mode 17), and a plane that started visible would put an empty layer over every session.
        _drawing.IsVisible = false;
        _crosshair.IsVisible = false;

        Gin = new GinRouter(_viewport) { ReportAsNorskData = true };

        // The painting itself is shared with the plain Tektronix 4014 - an ND graphic terminal is a
        // 4014 with extensions, and the vector half is the part it did not change.
        _plotter = new TektronixPlotter(_drawing, _viewport);
        _plotter.FirstVectorDrawn += () => FirstVectorDrawn?.Invoke();
    }

    /// <summary>
    /// Whether the graphics plane is being shown. Mode 17 sets it, ESC "17l clears it.
    /// </summary>
    public bool GraphicsDisplayed => _drawing.IsVisible;

    /// <summary>
    /// Whether the crosshair is up. Mode 6 raises it.
    /// </summary>
    public bool CrosshairVisible => _crosshair.IsVisible;

    /// <summary>
    /// Feeds one byte to the Tektronix side. Returns true when it was part of a drawing.
    /// </summary>
    /// <returns>
    /// False when the byte is ordinary text and the terminal should print it.
    /// </returns>
    /// <param name="b">
    /// The byte arriving from the host.
    /// </param>
    public bool ConsumeVectorByte(byte b) => _plotter.ConsumeVectorByte(b);

    /// <summary>
    /// Whether incoming bytes are currently being drawn rather than printed.
    /// </summary>
    public bool InGraphicsMode => _plotter.InGraphicsMode;

    /// <summary>
    /// The line type vectors are drawn with.
    /// </summary>
    /// <remarks>
    /// Set from the Tektronix line type escapes, which the ND analysis lists as standard 4014
    /// behaviour this terminal keeps. It lives on the plotter, which is what actually walks the
    /// line - this is only the way in for the emulator that owns the module.
    /// </remarks>
    public LinePattern Pattern
    {
        get => _plotter.Pattern;
        set => _plotter.Pattern = value;
    }

    /// <summary>
    /// Handles one <c>ESC "</c> sequence.
    /// </summary>
    /// <param name="parameters">
    /// The numeric parameters, in order.
    /// </param>
    /// <param name="final">
    /// The final byte: 'h' set, 'l' reset, 'd' query, and the rest.
    /// </param>
    public void HandleSequence(ReadOnlySpan<int> parameters, char final)
    {
        int mode = parameters.Length > 0 ? parameters[0] : 0;

        switch (final)
        {
            case 'h':
                HandleSet(mode, parameters);
                return;

            case 'l':
                HandleReset(mode);
                return;

            default:
                // 'd' query, 'n' report, 'a' address, 'C' cursor, '.' font parameters. Every one of
                // these needs a reply or a state model the spec does not pin down; counted, not
                // guessed.
                Count(mode, final);
                return;
        }
    }

    /// <summary>
    /// Moves the crosshair and redraws it, without reporting.
    /// </summary>
    /// <param name="surfaceX">
    /// Pointer position in surface pixels.
    /// </param>
    /// <param name="surfaceY">
    /// Pointer position in surface pixels.
    /// </param>
    public void MoveCrosshair(int surfaceX, int surfaceY)
    {
        Gin.MoveCrosshair(surfaceX, surfaceY);

        // The crosshair is redrawn on its OWN plane, so erasing it cannot touch the drawing under
        // it. That separation is the whole reason the plane model exists.
        _crosshair.Clear();
        if (!_crosshair.IsVisible) return;

        _crosshair.Surface.DrawLine(0, surfaceY, _crosshair.Width - 1, surfaceY, DrawColour);
        _crosshair.Surface.DrawLine(surfaceX, 0, surfaceX, _crosshair.Height - 1, DrawColour);
    }

    /// <summary>
    /// Raised the first time a real coordinate is drawn, so the terminal can take on a
    /// Tektronix-shaped screen. Raised once, not once per point.
    /// </summary>
    public event Action? FirstVectorDrawn;

    /// <summary>
    /// Returns the module to its start-of-session state: no drawing, both planes hidden, the
    /// vector decoder back in alpha mode, and <see cref="FirstVectorDrawn"/> armed again.
    /// </summary>
    /// <remarks>
    /// The re-arming is the part that is easy to miss. Without it a session that plotted once and
    /// was then reset would never grow its screen for the SECOND plot, because the one-shot flag
    /// stayed set - which is exactly what
    /// <c>TektronixGeometryTests.APlotAfterAResetGrowsTheScreenAgain</c> caught.
    /// </remarks>
    public void Reset()
    {
        _drawing.Clear();
        _crosshair.Clear();
        _drawing.IsVisible = false;
        _crosshair.IsVisible = false;

        _plotter.Reset();
        Gin.Disarm();
    }

    private void HandleSet(int mode, ReadOnlySpan<int> parameters)
    {
        switch (mode)
        {
            case 6:
                // Enable crosshair cursor. This is what puts the terminal into GIN: a crosshair
                // appears and the next thing the user does is reported to the host.
                _crosshair.IsVisible = true;
                Gin.Arm();
                return;

            case 8:
                // Rectangle fill: ESC "8;x1;y1;x2;y2h.
                //
                // ASSUMPTION, flagged rather than hidden: the coordinates are read as the
                // terminal's own logical space, the same one GIN reports in. The spec names the
                // parameters "4 coords" without saying which space, and every other coordinate in
                // this protocol is logical, so that is the reading taken. It has NOT been confirmed
                // against hardware.
                if (parameters.Length >= 5)
                {
                    FillRectangle(parameters[1], parameters[2], parameters[3], parameters[4]);
                }
                return;

            case 9:
                // Clear graphic memory. Erases the drawing, leaving the text underneath.
                _drawing.Clear();
                return;

            case 10:
                // Cursor visibility: ESC "10;0h hides, ESC "10;1h shows. One of the few modes the
                // spec gives both values for, which is why it is implemented and its neighbours
                // are counted.
                if (parameters.Length >= 2)
                {
                    _crosshair.IsVisible = parameters[1] != 0;
                }
                return;

            case 24:
                // Define circle: ESC "24;cx;cy;rh.
                //
                // ASSUMPTION, flagged rather than hidden: this DRAWS. The spec lists a separate
                // "execute draw" (mode 30) and names its callers as the polygon and copy-window
                // commands - not the circle command, which the same table shows using modes 5, 19,
                // 20 and 24 and never 30. So a circle appears to be drawn where it is defined. If
                // real hardware says otherwise the fix is one line: store it and let mode 30 draw
                // it, the way a polygon does.
                //
                // The coordinates are read as the terminal's own logical space, the same reading
                // mode 8 takes, for the same reason: every other coordinate in this protocol is
                // logical and the spec does not say.
                if (parameters.Length >= 4)
                {
                    DrawCircle(parameters[1], parameters[2], parameters[3]);
                }
                return;

            case 30:
                // Execute draw. Nothing is pending yet: polygons (mode 27) are still counted rather
                // than collected, so there is no shape for this to render. Counted so that a real
                // host's use of it still shows up in UnhandledSequences rather than being silently
                // swallowed by an empty implementation that looks finished.
                Count(mode, 'h');
                return;

            case 17:
                // Enable graphic display: show the graphics plane.
                _drawing.IsVisible = true;
                return;

            default:
                Count(mode, 'h');
                return;
        }
    }

    private void HandleReset(int mode)
    {
        switch (mode)
        {
            case 6:
                // Crosshair down, and GIN with it - a cancelled pick must not report later.
                _crosshair.IsVisible = false;
                Gin.Disarm();
                return;

            case 17:
                // Hide the graphics plane. NOT the same as clearing it: an ND host hides a drawing
                // and shows it again unchanged, which is exactly why planes have a visible flag
                // rather than being erased.
                _drawing.IsVisible = false;
                return;

            default:
                Count(mode, 'l');
                return;
        }
    }

    /// <summary>
    /// Fills a rectangle given in the terminal's logical space.
    ///
    /// The corners are ordered here rather than trusted: a host may name either diagonal, and the
    /// surface draws nothing at all for a negative width.
    /// </summary>
    private void FillRectangle(int x1, int y1, int x2, int y2)
    {
        _viewport.ToSurface(x1, y1, out int sx1, out int sy1);
        _viewport.ToSurface(x2, y2, out int sx2, out int sy2);

        int left = sx1 < sx2 ? sx1 : sx2;
        int top = sy1 < sy2 ? sy1 : sy2;
        int right = sx1 < sx2 ? sx2 : sx1;
        int bottom = sy1 < sy2 ? sy2 : sy1;

        // Inclusive of both corners: a host asking for the rectangle from (0,0) to (0,0) means one
        // pixel, not none.
        _drawing.Surface.FillRectangle(left, top, right - left + 1, bottom - top + 1, DrawColour);
    }

    /// <summary>
    /// Draws a circle given in the terminal's logical space.
    /// </summary>
    /// <remarks>
    /// The radius is converted by measuring a point one radius to the right of the centre and
    /// taking the distance in surface pixels, rather than by scaling the number directly. The
    /// viewport is the only owner of that arithmetic, and asking it twice is what keeps this
    /// correct if the logical space and the surface ever stop being the same size.
    /// </remarks>
    /// <param name="centreX">
    /// Circle centre in the terminal's own space.
    /// </param>
    /// <param name="centreY">
    /// Circle centre in the terminal's own space.
    /// </param>
    /// <param name="radius">
    /// Radius in the terminal's own space.
    /// </param>
    private void DrawCircle(int centreX, int centreY, int radius)
    {
        if (radius <= 0) return;

        _viewport.ToSurface(centreX, centreY, out int surfaceX, out int surfaceY);
        _viewport.ToSurface(centreX + radius, centreY, out int edgeX, out int _);

        int surfaceRadius = edgeX - surfaceX;
        if (surfaceRadius < 0) surfaceRadius = -surfaceRadius;

        _drawing.Surface.DrawCircle(surfaceX, surfaceY, surfaceRadius, DrawColour);
    }

    private void Count(int mode, char final)
    {
        string key = mode.ToString(System.Globalization.CultureInfo.InvariantCulture) + ":" + final;
        _unhandled.TryGetValue(key, out int seen);
        _unhandled[key] = seen + 1;
    }
}
