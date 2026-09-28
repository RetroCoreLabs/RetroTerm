using System;

namespace RetroTerm.Core.Terminal.Graphics;

/// <summary>
/// The value that means "no shading reference line".
/// </summary>
/// <remarks>
/// A sentinel rather than a separate on/off flag, so there is one thing to set and no way to have
/// shading switched on with no line to shade to.
/// </remarks>
public static class GraphicsShading
{
    /// <summary>
    /// Shading is off.
    /// </summary>
    public const int None = int.MinValue;
}

/// <summary>
/// Somewhere a graphics protocol can draw, in SURFACE pixels.
///
/// The four graphics protocols this project will grow — ND, Tektronix 4010/4014, Sixel, ReGIS —
/// share almost no command semantics, and pretending otherwise with a universal "graphics command"
/// type would be a lie that every one of them then has to work around. What they genuinely share is
/// this: somewhere to put pixels, a plane model, one owner of the coordinate transform, and a
/// GIN-style input path. This is the first of those.
///
/// Coordinates here are already SURFACE coordinates — top-left origin, x right, y down. A protocol
/// speaking its own logical space (Tektronix and ND both use a bottom-left 0..4095-ish grid) does
/// NOT convert here; the compositor owns that transform, so exactly one piece of code does the
/// arithmetic. See docs\ARCHITECTURE-REVIEW-TERMINAL-EMULATION-2026-08-08.md section C.7.
///
/// Everything CLIPS rather than throws. A host is perfectly entitled to send a line running off the
/// screen, and a terminal that fell over when it did would be broken; drawing the part that lands
/// on the surface and discarding the rest is what the hardware did.
///
/// No Skia, no Avalonia, no drawing library — this lives in Core so a graphics protocol can be
/// tested with no UI at all, by reading the pixels back.
/// </summary>
public interface IGraphicsSurface
{
    /// <summary>
    /// Width in surface pixels.
    /// </summary>
    int Width { get; }

    /// <summary>
    /// Height in surface pixels.
    /// </summary>
    int Height { get; }

    /// <summary>
    /// What drawing does to a pixel that is already lit.
    /// </summary>
    /// <remarks>
    /// Honoured by <see cref="SetPixel"/>, and therefore by everything that draws through it -
    /// lines, circles and points. <see cref="Clear"/> ignores it, because clearing the plane is
    /// not drawing on it. Defaults to <see cref="GraphicsWritingMode.Overlay"/>, so a protocol
    /// that has no writing modes never has to think about this.
    /// </remarks>
    GraphicsWritingMode WritingMode { get; set; }

    /// <summary>
    /// The colour map that indexed writing resolves codes through, or null when nothing indexed is
    /// being drawn.
    /// </summary>
    /// <remarks>
    /// <para><b>Why a surface knows about a colour map at all</b></para>
    /// A VT340's pixel memory does not hold colours. It holds a four-bit CODE per pixel - one bit in
    /// each of four planes - and the output map turns that code into a colour as the screen is
    /// scanned. Everything in chapter 3 that this project was missing depends on that being true:
    /// the plane mask writes some bits of the code and leaves the others, negative writing inverts
    /// the pattern, complement writing exclusive-ors the code, and changing the map repaints
    /// everything already on the screen without redrawing any of it.
    ///
    /// A surface that only remembered the resolved colour can express none of those. So the code is
    /// kept per pixel as well, and the colour beside it is what the map currently makes of it.
    ///
    /// Protocols with no plane model - Tektronix, and the ND vector module - leave this null and are
    /// entirely unaffected.
    /// </remarks>
    GraphicsColorMap? IndexedMap { get; set; }

    /// <summary>
    /// The code indexed writing draws with, or -1 to write colours directly as before.
    /// </summary>
    int DrawIndex { get; set; }

    /// <summary>
    /// Which planes indexed writing may change, one bit per plane.
    /// </summary>
    /// <remarks>
    /// The <c>W(F)</c> option of chapter 3. "The plane select option defines a 4-bit mask for the
    /// VT340, 1 bit for each plane", and the default "lets the terminal write to all planes" - so
    /// this starts at 15 and a protocol that never sets it never notices it.
    /// </remarks>
    int PlaneMask { get; set; }

    /// <summary>
    /// The code the pattern's 0 bits write, or -1 to leave those pixels alone.
    /// </summary>
    /// <remarks>
    /// <para><b>What makes replace writing different from overlay</b></para>
    /// The table on printed page 56 gives each writing style a "part of pattern memory affected".
    /// Overlay, complement and erase all say "Foreground only" - the pattern's 0 bits do nothing at
    /// all. Replace says "Foreground and background": the 0 bits write the background, so a dashed
    /// line in replace mode ERASES the gaps between its dashes rather than letting what is under
    /// them show through.
    ///
    /// That is why it needs a value here rather than a flag: the walk has to know what to put down.
    /// </remarks>
    int BackgroundIndex { get; set; }

    /// <summary>
    /// The row that shading runs to, or <see cref="GraphicsShading.None"/> when off.
    /// </summary>
    /// <remarks>
    /// <para><b>Shading is a property of drawing, not a separate command</b></para>
    /// "During shading commands, vector and curve commands operate as usual. However, as each point
    /// in a vector or curve is drawn, shading occurs from that point to a point on a shading
    /// reference line. The shading includes the point being drawn, as well as the point on the
    /// reference line."
    ///
    /// So it belongs here, where the points are, rather than in the protocol. Put it in the decoder
    /// and every primitive would need its own copy - and a circle would need the point walk written
    /// out a second time, which is exactly the duplication the shared Bresenham exists to avoid.
    /// That is also why a shaded circle comes out as a filled disc without anyone computing one: the
    /// top and bottom arcs both shade to the same line and cover the inside between them.
    /// </remarks>
    int ShadeToY { get; set; }

    /// <summary>
    /// The column that shading runs to, or <see cref="GraphicsShading.None"/> when off.
    /// </summary>
    /// <remarks>
    /// The <c>W(S(X)[x])</c> form. "The type of reference line you use has no effect on the way
    /// shading patterns are oriented."
    /// </remarks>
    int ShadeToX { get; set; }

    /// <summary>
    /// The top 8 rows of a character cell tiled over shaded areas, or null for solid shading.
    /// </summary>
    /// <remarks>
    /// Chapter 3's shading character select, <c>W(S'X')</c>. Eight rows because the manual says
    /// the terminal shows only the top 8 by 8 of the 8 by 10 cell.
    /// </remarks>
    byte[]? ShadeStencil { get; set; }

    /// <summary>
    /// Repaints every pixel whose code came from the map, using what the map says now.
    /// </summary>
    /// <remarks>
    /// This is the output map doing its job. A host that draws a picture and then changes map
    /// location 3 expects everything drawn in code 3 to change colour, because on the hardware the
    /// colour was never stored in the first place. hackerb9's bitplane drawing swaps two colours and
    /// swaps them back to show exactly this.
    ///
    /// Pixels put down by a protocol that was not writing codes are left alone - there is no code to
    /// look up for them.
    /// </remarks>
    void ReapplyColourMap();

    /// <summary>
    /// Sets every pixel to one colour. Pass transparent to erase the plane.
    /// </summary>
    void Clear(GraphicsColor colour);

    /// <summary>
    /// Sets one pixel. Coordinates outside the surface are ignored.
    /// </summary>
    void SetPixel(int x, int y, GraphicsColor colour);

    /// <summary>
    /// Reads one pixel. Coordinates outside the surface read as transparent, so a caller
    /// inspecting a region never has to bounds-check first.
    /// </summary>
    GraphicsColor GetPixel(int x, int y);

    /// <summary>
    /// Draws a one-pixel-wide solid line from (x0,y0) to (x1,y1) inclusive of both ends.
    /// </summary>
    void DrawLine(int x0, int y0, int x1, int y1, GraphicsColor colour);

    /// <summary>
    /// Draws a one-pixel-wide line in a dash pattern, carrying the pattern's phase.
    /// </summary>
    /// <remarks>
    /// <para><b>Why the phase is a ref parameter</b></para>
    /// A dashed polyline is ONE dashed line, not a series of separately dashed segments. Restarting
    /// the pattern at every vertex would put a dash at the start of every segment, so a short
    /// segment would come out solid and a shape drawn as many small vectors would come out solid
    /// everywhere. The caller keeps the phase between calls and the dashes run on through the
    /// corners, which is what a plotter does.
    ///
    /// The caller resets it to zero to start a fresh figure.
    /// </remarks>
    /// <param name="x0">
    /// Start point, horizontal.
    /// </param>
    /// <param name="y0">
    /// Start point, vertical.
    /// </param>
    /// <param name="x1">
    /// End point, horizontal.
    /// </param>
    /// <param name="y1">
    /// End point, vertical.
    /// </param>
    /// <param name="colour">
    /// Colour to draw with.
    /// </param>
    /// <param name="pattern">
    /// The dash pattern.
    /// </param>
    /// <param name="phase">
    /// How far through the pattern the walk starts, and on return how far it got.
    /// </param>
    void DrawLine(int x0, int y0, int x1, int y1, GraphicsColor colour, LinePattern pattern,
        ref int phase);

    /// <summary>
    /// Draws a line through an arbitrary pattern memory, the way ReGIS does.
    /// </summary>
    /// <param name="x0">
    /// Start point, horizontal.
    /// </param>
    /// <param name="y0">
    /// Start point, vertical.
    /// </param>
    /// <param name="x1">
    /// End point, horizontal.
    /// </param>
    /// <param name="y1">
    /// End point, vertical.
    /// </param>
    /// <param name="colour">
    /// Colour to draw with.
    /// </param>
    /// <param name="mask">
    /// The pattern bits, least significant bit first, one bit per step.
    /// </param>
    /// <param name="period">
    /// How many bits of the mask are in use before it repeats.
    /// </param>
    /// <param name="multiplier">
    /// How many pixels each bit covers.
    /// </param>
    /// <param name="phase">
    /// How far through the pattern the walk starts, and on return how far it got.
    /// </param>
    /// <remarks>
    /// <para><b>Why an arbitrary mask rather than another named pattern</b></para>
    /// Tektronix has five patterns with names and no numbers; ReGIS has an eight-bit PATTERN MEMORY
    /// that a host writes whatever it likes into - one of ten standard values from table 3-1, or its
    /// own binary string of two to eight bits, either of them stretched by a multiplier. An enum
    /// cannot carry that, and a second copy of the line walk to carry it would be a second place for
    /// the tie-break to drift. Both go through one walk and the caller brings its own bits.
    /// </remarks>
    void DrawLine(int x0, int y0, int x1, int y1, GraphicsColor colour, ushort mask, int period,
        int multiplier, ref int phase);

    /// <summary>
    /// Fills a rectangle. A zero or negative width or height draws nothing; the rest is clipped
    /// to the surface.
    /// </summary>
    void FillRectangle(int x, int y, int width, int height, GraphicsColor colour);

    /// <summary>
    /// Draws the outline of a circle, one pixel wide.
    /// </summary>
    /// <remarks>
    /// Here rather than in the protocol that asked for it because more than one of them draws
    /// circles - the ND terminal has a define-circle sequence and ReGIS has a circle command - and
    /// a second copy of the arithmetic is a second place for it to be wrong. A zero or negative
    /// radius draws nothing; anything off the edge is clipped like everything else.
    /// </remarks>
    /// <param name="centreX">
    /// Centre in surface pixels.
    /// </param>
    /// <param name="centreY">
    /// Centre in surface pixels.
    /// </param>
    /// <param name="radius">
    /// Radius in surface pixels.
    /// </param>
    /// <param name="colour">
    /// Colour to draw with.
    /// </param>
    void DrawCircle(int centreX, int centreY, int radius, GraphicsColor colour);

    /// <summary>
    /// Fills a closed polygon given as a flat run of x,y pairs.
    /// </summary>
    /// <remarks>
    /// <para><b>Flat, not a point type</b></para>
    /// The vertices arrive as one span of alternating x and y so a caller can build them in a
    /// pooled or stack buffer and pass them without allocating a point array on the way in. Fewer
    /// than three vertices draws nothing, and an odd-length span ignores the trailing value.
    ///
    /// <para><b>Even-odd, and why that is the right rule here</b></para>
    /// A pixel is inside when a ray from it crosses the outline an odd number of times. ReGIS is
    /// the caller that wanted this, and its manual says a figure that crosses itself gives
    /// "unpredictable" results on real hardware - so there is no documented behaviour that the
    /// non-zero winding rule would match better, and even-odd is the simpler of the two.
    ///
    /// The polygon closes itself: the last vertex joins back to the first whether or not the
    /// caller repeated it.
    /// </remarks>
    /// <param name="points">
    /// Vertices as x,y,x,y... in surface pixels.
    /// </param>
    /// <param name="colour">
    /// Colour to fill with.
    /// </param>
    void FillPolygon(ReadOnlySpan<int> points, GraphicsColor colour);
}
