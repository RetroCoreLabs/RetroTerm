using System;

namespace RetroTerm.Core.Terminal.Graphics;

/// <summary>
/// Turns a Tektronix coordinate stream into ink on a plane.
/// </summary>
/// <remarks>
/// <para><b>Why this is its own class</b></para>
/// The decoder says where the beam went; something has to decide what that means on a surface -
/// a move or a line, a single lit dot in point-plot mode, and the Y flip between the terminal's
/// own space and the pixels. That decision used to live inside the Norsk Data module, which was
/// fine while an ND graphic terminal was the only thing here that could draw.
///
/// A Tektronix 4014 is now a terminal in its own right, and it plots exactly the same way - it is
/// the machine the ND terminal was copying. Leaving the painting inside the ND module would have
/// meant a second copy of the same arithmetic, and the two would drift the first time either was
/// touched.
///
/// So: this owns the decoder and the plane, the ND module composes it and adds the ND-only
/// sequences on top, and the 4014 emulator uses it bare.
/// </remarks>
public sealed class TektronixPlotter
{
    private readonly GraphicsPlane _plane;
    private readonly GraphicsViewport _viewport;

    private int _lastSurfaceX;
    private int _lastSurfaceY;
    private bool _anyVectorDrawn;

    /// <summary>
    /// Raised the first time a real coordinate is drawn. Raised once, not once per point.
    /// </summary>
    public event Action? FirstVectorDrawn;

    /// <summary>
    /// The Tektronix coordinate stream: GS/FS/RS/US and the bytes between them.
    /// </summary>
    public TektronixVectorDecoder Vectors { get; }

    /// <summary>
    /// Colour the beam draws in. One phosphor: these are single-colour machines.
    /// </summary>
    public GraphicsColor DrawColour { get; set; } = new GraphicsColor(0, 255, 136);

    /// <summary>
    /// Builds a plotter that draws on the given plane.
    /// </summary>
    /// <param name="plane">
    /// Where the ink goes.
    /// </param>
    /// <param name="viewport">
    /// The transform between the terminal's own space and surface pixels.
    /// </param>
    public TektronixPlotter(GraphicsPlane plane, GraphicsViewport viewport)
    {
        _plane = plane ?? throw new ArgumentNullException(nameof(plane));
        _viewport = viewport ?? throw new ArgumentNullException(nameof(viewport));

        Vectors = new TektronixVectorDecoder();
        Vectors.PointReady += OnVectorPoint;
    }

    /// <summary>
    /// Whether incoming bytes are currently being drawn rather than printed.
    /// </summary>
    public bool InGraphicsMode => Vectors.Mode != TektronixMode.Alpha;

    /// <summary>
    /// The dash pattern vectors are drawn in, selected by <c>ESC `</c> through <c>ESC g</c>.
    /// </summary>
    public LinePattern Pattern { get; set; } = LinePattern.Solid;

    /// <summary>
    /// How far through <see cref="Pattern"/> the next segment starts.
    /// </summary>
    /// <remarks>
    /// Carried across segments so a polyline is ONE dashed line rather than a run of separately
    /// dashed pieces - a curve drawn as many short vectors would otherwise come out solid, every
    /// segment being shorter than the first dash.
    /// </remarks>
    private int _patternPhase;

    /// <summary>
    /// Feeds one byte to the coordinate stream.
    /// </summary>
    /// <param name="b">
    /// The byte arriving from the host.
    /// </param>
    /// <returns>
    /// False when the byte is ordinary text and the terminal should print it.
    /// </returns>
    public bool ConsumeVectorByte(byte b) => Vectors.Consume(b);

    /// <summary>
    /// Returns the plotter to its start-of-session state: nothing drawn, the decoder back in alpha
    /// mode, and <see cref="FirstVectorDrawn"/> armed again.
    /// </summary>
    /// <remarks>
    /// The re-arming is the part that is easy to miss. Without it a session that plotted once and
    /// was then reset would never raise the event for the SECOND plot, because the one-shot flag
    /// stayed set.
    /// </remarks>
    public void Reset()
    {
        Vectors.Reset();
        _anyVectorDrawn = false;
        _lastSurfaceX = 0;
        _lastSurfaceY = 0;

        // Selecting alpha mode "resets the pattern register and intensity", so an erase puts the
        // pattern back to solid along with everything else.
        Pattern = LinePattern.Solid;
        _patternPhase = 0;
    }

    private void OnVectorPoint(int logicalX, int logicalY)
    {
        if (!_anyVectorDrawn)
        {
            _anyVectorDrawn = true;
            FirstVectorDrawn?.Invoke();
        }

        _viewport.ToSurface(logicalX, logicalY, out int surfaceX, out int surfaceY);

        if (Vectors.NextPointStartsAFigure || Vectors.Mode == TektronixMode.PointPlot)
        {
            // A move, or a plotted point. Point plot lights single dots and never joins them,
            // which is the whole difference between FS and GS.
            if (Vectors.Mode == TektronixMode.PointPlot)
            {
                _plane.Surface.SetPixel(surfaceX, surfaceY, DrawColour);
            }

            // A MOVE RESTARTS THE PATTERN. Lifting the beam ends the figure, so the next line
            // begins with a dash rather than wherever the last one happened to stop.
            _patternPhase = 0;
        }
        else
        {
            _plane.Surface.DrawLine(_lastSurfaceX, _lastSurfaceY, surfaceX, surfaceY, DrawColour,
                Pattern, ref _patternPhase);
        }

        _lastSurfaceX = surfaceX;
        _lastSurfaceY = surfaceY;
    }
}
