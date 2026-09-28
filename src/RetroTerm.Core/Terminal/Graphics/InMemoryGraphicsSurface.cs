using System;
using System.Buffers;

namespace RetroTerm.Core.Terminal.Graphics;

/// <summary>
/// A graphics surface backed by a flat array of packed pixels.
///
/// This is the ONLY implementation Core needs. The Desktop renderer does not implement the
/// interface a second time — it blits this one's pixels — so a graphics protocol behaves
/// identically in a headless test and on screen, and there is no second copy of the drawing rules
/// to drift.
///
/// Row-major, one <c>uint</c> per pixel. Flat rather than <c>[,]</c> so a row is a contiguous span
/// that can be filled or copied in one go; the plane compositor will want exactly that.
/// </summary>
public sealed class InMemoryGraphicsSurface : IGraphicsSurface
{
    private readonly uint[] _pixels;

    /// <summary>
    /// The code stored in this pixel by indexed writing, or <see cref="NoIndex"/>.
    /// </summary>
    /// <remarks>
    /// This is the bitmap a VT340 actually has: four planes deep, so four bits per pixel, packed
    /// here one byte per pixel because a byte array indexes faster than any bit-twiddling would and
    /// the plane is only ever a few hundred kilobytes.
    /// </remarks>
    private readonly byte[] _codes;

    /// <summary>
    /// Marks a pixel whose colour did not come from the map, so a map change must leave it alone.
    /// </summary>
    /// <remarks>
    /// A Tektronix vector or a cleared plane has no code. Repainting it from the map would invent
    /// one - most likely code 0 - and quietly turn every un-indexed pixel black.
    /// </remarks>
    private const byte NoIndex = 0xFF;

    public int Width { get; }
    public int Height { get; }

    /// <param name="width">
    /// Width in pixels; must be positive.
    /// </param>
    /// <param name="height">
    /// Height in pixels; must be positive.
    /// </param>
    public InMemoryGraphicsSurface(int width, int height)
    {
        if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));

        Width = width;
        Height = height;
        _pixels = new uint[width * height];   // zero == transparent, which is the right start

        _codes = new byte[width * height];
        _codes.AsSpan().Fill(NoIndex);        // nothing has been drawn, so nothing has a code yet
    }

    /// <summary>
    /// The colour map indexed writing resolves codes through, or null for direct colour writing.
    /// </summary>
    public GraphicsColorMap? IndexedMap { get; set; }

    /// <summary>
    /// The code indexed writing draws with, or -1 to write colours directly.
    /// </summary>
    public int DrawIndex { get; set; } = -1;

    /// <summary>
    /// Which planes indexed writing may change. All four by default.
    /// </summary>
    public int PlaneMask { get; set; } = 15;

    /// <summary>
    /// The code the pattern's 0 bits write, or -1 to leave them alone.
    /// </summary>
    public int BackgroundIndex { get; set; } = -1;

    /// <summary>
    /// The row shading runs to, or <see cref="GraphicsShading.None"/>.
    /// </summary>
    public int ShadeToY { get; set; } = GraphicsShading.None;

    /// <summary>
    /// The column shading runs to, or <see cref="GraphicsShading.None"/>.
    /// </summary>
    public int ShadeToX { get; set; } = GraphicsShading.None;

    /// <summary>
    /// Set while a shading run is being drawn, so its own pixels do not shade again.
    /// </summary>
    private bool _shading;

    /// <summary>
    /// The top 8 rows of a character cell, tiled over a shaded area, or null for solid shading.
    /// </summary>
    /// <remarks>
    /// Chapter 3's "Select Shading Character": <c>W(S'X')</c> shades with a text character instead
    /// of a solid fill, which is how the manual says to get halftones on a device with only two
    /// intensities. Eight rows because it says so - "the terminal only displays the top 8 x 8 matrix
    /// of the 8 x 10 character cell".
    /// Tiled against the SURFACE origin rather than the run, so neighbouring runs line up into one
    /// continuous texture instead of each restarting the pattern at its own top.
    /// Row 0 is the top; the most significant bit is the leftmost pixel, the same order the glyph
    /// store and the load command's hex pairs both use.
    /// </remarks>
    public byte[]? ShadeStencil { get; set; }

    /// <summary>
    /// Whether the shading stencil lets this pixel through.
    /// </summary>
    /// <param name="x">
    /// Surface pixel.
    /// </param>
    /// <param name="y">
    /// Surface pixel.
    /// </param>
    /// <returns>
    /// True when the pixel should be drawn.
    /// </returns>
    private bool StencilAllows(int x, int y)
    {
        var stencil = ShadeStencil;
        if (stencil == null || stencil.Length == 0) return true;

        // Modulo that stays positive for negative coordinates, which a shading run reaching off the
        // left or top of the surface produces.
        int row = ((y % stencil.Length) + stencil.Length) % stencil.Length;
        int column = ((x % 8) + 8) % 8;

        return (stencil[row] & (1 << (7 - column))) != 0;
    }

    /// <summary>
    /// True when this surface is writing four-bit codes rather than colours.
    /// </summary>
    private bool IsIndexed => IndexedMap != null && DrawIndex >= 0;

    /// <summary>
    /// Draws the run from a point just drawn to the shading reference line.
    /// </summary>
    /// <param name="x">
    /// The point that was drawn.
    /// </param>
    /// <param name="y">
    /// The point that was drawn.
    /// </param>
    /// <param name="colour">
    /// The colour the point was drawn in.
    /// </param>
    /// <remarks>
    /// "The shading includes the point being drawn, as well as the point on the reference line" -
    /// so both ends are inclusive, and the point itself is drawn twice, harmlessly, except under
    /// complement writing where the outer call has already flipped it and this must not flip it
    /// back. Hence the run starts one step along.
    /// </remarks>
    private void DrawShading(int x, int y, GraphicsColor colour)
    {
        _shading = true;

        try
        {
            // A stencilled run walks the same pixels and simply skips the ones the character
            // leaves blank, so the run's length and both inclusive ends stay exactly as they are.
            if (ShadeToY != GraphicsShading.None)
            {
                // Clamped to the surface before the walk rather than inside it: a host may put the
                // reference line far off the screen, and running the loop out to it would cost the
                // distance rather than the height. The comparison below MUST be against the clamped
                // target - against the raw one, a run starting past the edge would never reach it.
                int target = ShadeToY < 0 ? 0 : ShadeToY >= Height ? Height - 1 : ShadeToY;

                if (target != y)
                {
                    int step = target > y ? 1 : -1;
                    for (int row = y + step; ; row += step)
                    {
                        if (StencilAllows(x, row)) SetPixel(x, row, colour);
                        if (row == target) break;
                    }
                }
            }

            if (ShadeToX != GraphicsShading.None)
            {
                int target = ShadeToX < 0 ? 0 : ShadeToX >= Width ? Width - 1 : ShadeToX;

                if (target != x)
                {
                    int step = target > x ? 1 : -1;
                    for (int col = x + step; ; col += step)
                    {
                        if (StencilAllows(col, y)) SetPixel(col, y, colour);
                        if (col == target) break;
                    }
                }
            }
        }
        finally
        {
            _shading = false;
        }
    }

    /// <summary>
    /// Repaints every coded pixel from the colour map as it stands now.
    /// </summary>
    public void ReapplyColourMap()
    {
        var map = IndexedMap;
        if (map == null) return;

        for (int at = 0; at < _codes.Length; at++)
        {
            byte code = _codes[at];
            if (code == NoIndex) continue;

            _pixels[at] = map.Register(code).Value;
        }
    }

    /// <summary>
    /// Writes one pixel of the four-plane bitmap, honouring the plane mask and the writing mode.
    /// </summary>
    /// <param name="at">
    /// Index into the flat pixel array; already known to be inside the surface.
    /// </param>
    /// <remarks>
    /// <para><b>The three writing styles, on the code rather than on the colour</b></para>
    /// Chapter 3 defines them as operations on the bitmap value, and only the plane mask decides
    /// which bits take part:
    ///  - overlay and replace put the drawing code into the selected planes.
    ///  - complement exclusive-ors the selected planes, which is table 3-4.
    ///  - erase clears the selected planes to the background code.
    ///
    /// A pixel nothing has drawn on yet counts as code 0 - the background - because on the hardware
    /// every pixel always holds some code. That is what makes a one-plane write to fresh screen come
    /// out as that plane's own value rather than as the full drawing code.
    /// </remarks>
    private void SetIndexedPixel(int at)
    {
        var map = IndexedMap;
        if (map == null) return;

        int mask = PlaneMask & 15;

        // No-plane writing changes nothing - not the code, and not the pixel. Falling through here
        // would give a pixel nothing had drawn on yet the background colour, so a W(F0) drawing
        // would paint itself in black instead of leaving the screen alone.
        if (mask == 0) return;

        byte stored = _codes[at];
        int old = stored == NoIndex ? 0 : stored;
        int code;

        switch (WritingMode)
        {
            case GraphicsWritingMode.Complement:
                code = old ^ mask;
                break;

            case GraphicsWritingMode.Erase:
                code = old & ~mask;
                break;

            default:
                code = (old & ~mask) | (DrawIndex & mask);
                break;
        }

        code &= 15;

        // The write reaches the text underneath as well. On the hardware there is nothing to reach
        // THROUGH - characters and drawings share one four-plane bitmap, so a write changes
        // whatever was already there. Here text is a cell buffer, so the same operation is handed
        // to whoever owns the text, one cell at a time.
        //
        // ALL FOUR PLANES COUNT, not just a partial mask. It is tempting to think a full-mask write
        // is ordinary drawing that could not leave the background alone, but hackerb9's last box is
        // exactly that: mask 15 over index 0 takes bold text from 1111 to 0000 while the background,
        // already 0000, does not move. Skipping it left the last pair of bold letters uncoloured.
        // Where a write does paint an opaque pixel the recolour is simply hidden underneath it,
        // which is what happens on the hardware too.
        NotifyMaskedWrite(at, mask);

        // A write that leaves the value where it found it has drawn nothing, and on a pixel nothing
        // had touched yet that value is the background. Painting it anyway is what put an opaque
        // black bar over hackerb9's text: sixteen boxes writing index 0 through sixteen different
        // plane masks all resolve to 0 on empty screen, which is exactly the case the hardware
        // shows as "the background did not move, only the letters changed colour".
        if (code == old && stored == NoIndex) return;

        _codes[at] = (byte)code;
        _pixels[at] = map.Register(code).Value;
    }

    /// <summary>
    /// Told about every plane write that leaves some of the four planes alone, so the owner of the
    /// text can apply the same operation to the characters underneath.
    /// </summary>
    /// <remarks>
    /// Arguments are the cell column, the cell row, the plane mask, the drawing index and the
    /// writing style. Set <see cref="TextCellWidth"/> and <see cref="TextCellHeight"/> to switch it
    /// on; while either is zero nothing is reported and the surface behaves exactly as it did.
    /// </remarks>
    public MaskedWriteHandler? OnMaskedWrite { get; set; }

    /// <summary>
    /// Signature of <see cref="OnMaskedWrite"/>. A named delegate rather than an
    /// <c>Action</c> so the parameters have names at the call site.
    /// </summary>
    /// <param name="cellColumn">
    /// Text column the written pixel falls in.
    /// </param>
    /// <param name="cellRow">
    /// Text row the written pixel falls in.
    /// </param>
    /// <param name="planeMask">
    /// Which of the four planes the write touches.
    /// </param>
    /// <param name="drawIndex">
    /// The code being written, before masking.
    /// </param>
    /// <param name="mode">
    /// Overlay, replace, complement or erase.
    /// </param>
    public delegate void MaskedWriteHandler(int cellColumn, int cellRow, int planeMask, int drawIndex,
        GraphicsWritingMode mode);

    /// <summary>
    /// Width of one character cell in surface pixels. Zero switches text recolouring off.
    /// </summary>
    public int TextCellWidth { get; set; }

    /// <summary>
    /// Height of one character cell in surface pixels. Zero switches text recolouring off.
    /// </summary>
    public int TextCellHeight { get; set; }

    /// <summary>
    /// The operation each cell is currently accumulating, and how many of its pixels that
    /// operation has covered so far. Allocated on the first write and never grown.
    /// </summary>
    /// <remarks>
    /// <para><b>Why coverage is counted rather than the first pixel acted on</b></para>
    /// A cell is the smallest thing that can hold a colour here, and a drawing does not respect
    /// cell boundaries. Recolouring a character the moment ANY pixel of its cell is written gives
    /// a badly wrong picture: hackerb9's script puts a one-pixel <c>V0</c> line between each pair of
    /// boxes, which on the hardware clips the top row of a character and is invisible, and which
    /// cell-wide would repaint every letter it crossed.
    /// So an operation changes a character only when it covered MOST of it - half the cell's area
    /// or more. That is the closest a cell can get to the hardware's question, which is whether
    /// most of the character's own pixels changed value.
    /// <para><b>Why it is deferred</b></para>
    /// The count is not known until the shape is finished, so the operation is held until either a
    /// different operation reaches that cell or <see cref="FlushTextRecolours"/> is called at the
    /// end of the drawing.
    /// </remarks>
    private int[]? _cellOperation;

    /// <summary>
    /// Pixels of each cell the pending operation has covered. Parallel to
    /// <see cref="_cellOperation"/>.
    /// </summary>
    private int[]? _cellCoverage;

    /// <summary>
    /// Records that one pixel of a cell took part in the current operation.
    /// </summary>
    /// <param name="at">
    /// Index into the flat pixel array.
    /// </param>
    /// <param name="mask">
    /// Plane mask already reduced to four bits.
    /// </param>
    private void NotifyMaskedWrite(int at, int mask)
    {
        if (OnMaskedWrite == null) return;

        int cellWidth = TextCellWidth;
        int cellHeight = TextCellHeight;
        if (cellWidth <= 0 || cellHeight <= 0) return;

        int y = at / Width;
        int x = at - y * Width;

        int column = x / cellWidth;
        int row = y / cellHeight;

        int columns = (Width + cellWidth - 1) / cellWidth;
        int rows = (Height + cellHeight - 1) / cellHeight;
        if (column >= columns || row >= rows) return;

        // Pack the operation into one int so "is this still the same operation" is a single
        // compare. Zero is kept free to mean "this cell has nothing pending".
        int operation = 1 | (mask << 1) | ((DrawIndex & 15) << 5) | ((int)WritingMode << 9);

        var pending = _cellOperation;
        if (pending == null || pending.Length != columns * rows)
        {
            pending = new int[columns * rows];
            _cellOperation = pending;
            _cellCoverage = new int[columns * rows];
        }

        int cell = row * columns + column;
        if (pending[cell] != operation)
        {
            // A different operation has reached this cell, so whatever was pending is finished.
            ApplyPendingRecolour(cell, columns);
            pending[cell] = operation;
            _cellCoverage![cell] = 0;
        }

        _cellCoverage![cell]++;
    }

    /// <summary>
    /// Hands one cell's pending operation to <see cref="OnMaskedWrite"/>, if it covered enough of
    /// the cell to count, and clears it either way.
    /// </summary>
    /// <param name="cell">
    /// Index into the per-cell arrays.
    /// </param>
    /// <param name="columns">
    /// How many cells fit across the surface.
    /// </param>
    private void ApplyPendingRecolour(int cell, int columns)
    {
        var pending = _cellOperation;
        if (pending == null) return;

        int operation = pending[cell];
        if (operation == 0) return;

        pending[cell] = 0;

        int covered = _cellCoverage![cell];
        _cellCoverage[cell] = 0;

        // Half the cell or more. Below that the character keeps its colour, which is what a real
        // bitmap shows: most of its pixels still hold the value they had.
        if (covered * 2 < TextCellWidth * TextCellHeight) return;

        int mask = (operation >> 1) & 15;
        int index = (operation >> 5) & 15;
        var mode = (GraphicsWritingMode)(operation >> 9);

        OnMaskedWrite?.Invoke(cell % columns, cell / columns, mask, index, mode);
    }

    /// <summary>
    /// Finishes every cell still accumulating an operation. Call once a drawing is complete.
    /// </summary>
    /// <remarks>
    /// Without this the last shape drawn would never be applied, because nothing else would come
    /// along to displace it.
    /// </remarks>
    public void FlushTextRecolours()
    {
        var pending = _cellOperation;
        if (pending == null) return;

        int cellWidth = TextCellWidth;
        if (cellWidth <= 0) return;

        int columns = (Width + cellWidth - 1) / cellWidth;

        for (int cell = 0; cell < pending.Length; cell++)
        {
            if (pending[cell] != 0) ApplyPendingRecolour(cell, columns);
        }
    }


    /// <summary>
    /// The raw pixels, row-major, for a compositor or a renderer to read in bulk.
    ///
    /// Exposed as a read-only span rather than the array: a caller may blit it, and may not
    /// scribble on it behind the surface's back.
    /// </summary>
    public ReadOnlySpan<uint> Pixels => _pixels;

    /// <param name="colour">
    /// Colour to fill with; transparent erases the plane.
    /// </param>
    public void Clear(GraphicsColor colour)
    {
        // Span.Fill rather than a loop: this runs on a host clearing the screen, which some
        // protocols do between every frame.
        _pixels.AsSpan().Fill(colour.Value);

        // The codes go with them. A cleared plane holds no code the map could repaint - the colour
        // it was cleared to was chosen by the caller, not looked up.
        _codes.AsSpan().Fill(NoIndex);
    }

    /// <summary>
    /// Moves the picture up by a number of PIXEL rows and clears the rows that fall off
    /// the bottom, so graphics travel with the text they were drawn beside.
    ///
    /// A real VT340 has one bitmap carrying both text and graphics, so a scroll moves the
    /// picture with everything else. Here they are separate surfaces and the picture used
    /// to stand still while the text slid out from under it. Ronny's call, 9 September
    /// 2026: be faithful and move it.
    ///
    /// The colour codes move with the pixels. They are what a later colour-map change
    /// repaints through, so leaving them behind would make a picture change colour in the
    /// wrong places the next time the map was touched.
    /// </summary>
    /// <param name="pixelRows">
    /// How far to move. Zero or less does nothing; a value at or past the height clears
    /// the surface outright.
    /// </param>
    public void ScrollUp(int pixelRows)
    {
        if (pixelRows <= 0) return;

        if (pixelRows >= Height)
        {
            Clear(GraphicsColor.Transparent);
            return;
        }

        int moved = (Height - pixelRows) * Width;
        int from = pixelRows * Width;

        // Overlapping copy towards the front - Span.CopyTo handles the overlap.
        _pixels.AsSpan(from, moved).CopyTo(_pixels.AsSpan(0, moved));
        _codes.AsSpan(from, moved).CopyTo(_codes.AsSpan(0, moved));

        ClearRows(Height - pixelRows, pixelRows);
    }

    /// <summary>
    /// Wipes a horizontal band of the surface back to nothing at all.
    /// </summary>
    /// <remarks>
    /// A RAW clear, deliberately: it goes straight at the storage rather than through the writing
    /// styles and the plane mask. Erasing is not drawing, so a plane mask left set by the last
    /// drawing must not decide which planes get erased, and complement writing must not turn the
    /// erase into its opposite.
    /// Used when a line attribute changes, which on a VT340 clears the graphics on that row.
    /// </remarks>
    /// <param name="top">
    /// First pixel row to wipe.
    /// </param>
    /// <param name="height">
    /// How many pixel rows; zero or less does nothing.
    /// </param>
    public void ClearRows(int top, int height)
    {
        if (height <= 0) return;

        int first = top < 0 ? 0 : top;
        int last = top + height;
        if (last > Height) last = Height;
        if (first >= last) return;

        int start = first * Width;
        int count = (last - first) * Width;

        _pixels.AsSpan(start, count).Clear();          // zero is transparent
        _codes.AsSpan(start, count).Fill(NoIndex);     // and no code the map could repaint
    }

    /// <param name="x">
    /// Horizontal surface pixel.
    /// </param>
    /// <param name="y">
    /// Vertical surface pixel.
    /// </param>
    /// <param name="colour">
    /// Colour to write.
    /// </param>
    public void SetPixel(int x, int y, GraphicsColor colour)
    {
        if ((uint)x >= (uint)Width || (uint)y >= (uint)Height)
        {
            // One unsigned compare per axis catches negatives too - a negative int reinterpreted
            // as unsigned is enormous, so it fails the same test.
            return;
        }

        int at = y * Width + x;

        // A protocol writing four-bit codes takes the bitmap path, where the plane mask and the
        // writing style act on the code and the colour falls out of the map.
        if (IsIndexed)
        {
            SetIndexedPixel(at);

            if (!_shading) DrawShading(x, y, colour);
            return;
        }

        // Whatever code was here belonged to a picture this pixel is no longer part of. Leaving it
        // would let a later map change repaint a pixel a different protocol had drawn over.
        _codes[at] = NoIndex;

        // The common case first and with no extra work: a protocol with no writing modes pays one
        // predictable branch per pixel and nothing else.
        switch (WritingMode)
        {
            case GraphicsWritingMode.Overlay:
                _pixels[at] = colour.Value;
                break;

            case GraphicsWritingMode.Erase:
                _pixels[at] = GraphicsColor.Transparent.Value;
                break;

            default:
                // Complement: a lit pixel goes out, an unlit one comes on. Read-modify-write, so
                // it cannot go through the fast paths that fill spans wholesale.
                _pixels[at] = _pixels[at] == GraphicsColor.Transparent.Value
                    ? colour.Value
                    : GraphicsColor.Transparent.Value;
                break;
        }

        // Shading is not a ReGIS-only idea, so it hangs off the pixel write rather than off the
        // indexed path. A protocol that never sets a reference line pays one compare.
        if (!_shading) DrawShading(x, y, colour);
    }

    /// <summary>
    /// What drawing does to a pixel that is already lit.
    /// </summary>
    /// <remarks>
    /// See <see cref="GraphicsWritingMode"/>. Only <see cref="SetPixel"/> honours it, so the
    /// bulk fills below deliberately do not - they belong to protocols that have no writing
    /// modes, and a span fill cannot express a complement anyway.
    /// </remarks>
    public GraphicsWritingMode WritingMode { get; set; } = GraphicsWritingMode.Overlay;

    /// <param name="x">
    /// Horizontal surface pixel.
    /// </param>
    /// <param name="y">
    /// Vertical surface pixel.
    /// </param>
    /// <returns>
    /// The pixel, or transparent when the position is outside the surface.
    /// </returns>
    public GraphicsColor GetPixel(int x, int y)
    {
        if ((uint)x >= (uint)Width || (uint)y >= (uint)Height)
        {
            return GraphicsColor.Transparent;
        }

        return new GraphicsColor(_pixels[y * Width + x]);
    }

    /// <summary>
    /// Bresenham's line algorithm, integer only.
    ///
    /// The all-integer form, deliberately: a vector protocol draws thousands of these and any
    /// floating-point stepping would both cost more and put pixels in different places depending
    /// on which end the line was drawn from. This form is symmetric — drawing B to A lights
    /// exactly the same pixels as A to B, which
    /// <c>InMemoryGraphicsSurfaceTests.ALineIsTheSameDrawnFromEitherEnd</c> pins.
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
    public void DrawLine(int x0, int y0, int x1, int y1, GraphicsColor colour)
    {
        // A solid line is a patterned line whose every bit is set. One walk, not two: a second copy
        // of Bresenham would be a second place for the tie-break below to drift.
        int phase = 0;
        DrawLine(x0, y0, x1, y1, colour, LinePattern.Solid, ref phase);
    }

    /// <summary>
    /// Draws a line in a dash pattern, carrying the pattern's phase across calls.
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
    /// <param name="pattern">
    /// The dash pattern.
    /// </param>
    /// <param name="phase">
    /// How far through the pattern to start, and on return how far the walk got.
    /// </param>
    public void DrawLine(int x0, int y0, int x1, int y1, GraphicsColor colour, LinePattern pattern,
        ref int phase)
    {
        // Resolved ONCE per line rather than per pixel: the mask is a constant for the whole walk.
        // One bit per pixel, which is what a named Tektronix pattern means.
        DrawLine(x0, y0, x1, y1, colour, LinePatternMask.For(pattern), LinePatternMask.Period, 1,
            ref phase);
    }

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
    /// The pattern bits, least significant bit first.
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
    public void DrawLine(int x0, int y0, int x1, int y1, GraphicsColor colour, ushort mask,
        int period, int multiplier, ref int phase)
    {
        if (period <= 0) period = LinePatternMask.Period;
        if (multiplier <= 0) multiplier = 1;

        int step = phase;
        // Draw from a CANONICAL end, so the caller's order cannot change the result.
        //
        // Plain Bresenham is not symmetric: when the accumulated error lands exactly on a tie, the
        // pixel it picks depends on which end the walk started from, so a line drawn B to A can sit
        // a pixel off the line drawn A to B. A shape's outline would then shift depending on the
        // order a protocol happened to emit its vectors, and nothing about the picture would
        // explain why. Swapping to a fixed order removes the question rather than tuning the
        // tie-break. Caught by ALineIsTheSameDrawnFromEitherEnd, which failed on exactly one pixel.
        if (x0 > x1 || (x0 == x1 && y0 > y1))
        {
            int swap = x0; x0 = x1; x1 = swap;
            swap = y0; y0 = y1; y1 = swap;
        }

        int dx = Math.Abs(x1 - x0);
        int dy = -Math.Abs(y1 - y0);
        int stepX = x0 < x1 ? 1 : -1;
        int stepY = y0 < y1 ? 1 : -1;
        int error = dx + dy;

        int x = x0;
        int y = y0;

        while (true)
        {
            // The mask decides whether this step puts ink down. The phase advances either way, so
            // a gap still costs its pixels and the dashes keep their spacing.
            // The multiplier stretches each bit over that many pixels, which is how ReGIS's
            // W(P...(M n)) widens a dash without changing the pattern itself.
            if (((mask >> ((step / multiplier) % period)) & 1) != 0)
            {
                SetPixel(x, y, colour);   // clips on its own, so an off-screen line costs no branches here
            }
            else if (BackgroundIndex >= 0 && IndexedMap != null)
            {
                // Replace writing: the gap between the dashes is not skipped, it is written in the
                // background. Swapping the draw code for one pixel keeps the plane mask, the
                // writing style and the shading all working exactly as they do for a lit pixel -
                // which the table on printed page 56 says they must.
                int saved = DrawIndex;
                DrawIndex = BackgroundIndex;
                SetPixel(x, y, IndexedMap.Register(BackgroundIndex));
                DrawIndex = saved;
            }

            step++;

            if (x == x1 && y == y1) break;

            // Doubled so the comparison stays integer. e2 is 2*error.
            int e2 = 2 * error;
            if (e2 >= dy)
            {
                if (x == x1) break;   // guard against a zero-length step on a degenerate line
                error += dy;
                x += stepX;
            }
            if (e2 <= dx)
            {
                if (y == y1) break;
                error += dx;
                y += stepY;
            }
        }

        // Hand the phase back so the next segment of the same figure carries on where this one
        // stopped. Wrapped to the period so a long polyline cannot run the counter away.
        phase = step & (LinePatternMask.Period - 1);
    }

    /// <param name="x">
    /// Left edge in surface pixels.
    /// </param>
    /// <param name="y">
    /// Top edge in surface pixels.
    /// </param>
    /// <param name="width">
    /// Width; zero or less draws nothing.
    /// </param>
    /// <param name="height">
    /// Height; zero or less draws nothing.
    /// </param>
    /// <param name="colour">
    /// Colour to fill with.
    /// </param>
    public void FillRectangle(int x, int y, int width, int height, GraphicsColor colour)
    {
        if (width <= 0 || height <= 0) return;

        // Clip to the surface before touching anything, so the inner loop has no bounds test.
        int left = x < 0 ? 0 : x;
        int top = y < 0 ? 0 : y;
        int right = x + width;
        int bottom = y + height;
        if (right > Width) right = Width;
        if (bottom > Height) bottom = Height;

        if (left >= right || top >= bottom) return;

        int runLength = right - left;

        // Indexed writing cannot use a span fill: every pixel's new code depends on the code that
        // was already there, so it has to be read before it is written.
        if (IsIndexed)
        {
            for (int row = top; row < bottom; row++)
            {
                int rowStart = row * Width;
                for (int col = left; col < right; col++)
                {
                    SetIndexedPixel(rowStart + col);
                }
            }

            return;
        }

        var pixels = _pixels.AsSpan();
        var codes = _codes.AsSpan();
        for (int row = top; row < bottom; row++)
        {
            pixels.Slice(row * Width + left, runLength).Fill(colour.Value);
            codes.Slice(row * Width + left, runLength).Fill(NoIndex);
        }
    }

    /// <summary>
    /// Midpoint circle: integer arithmetic only, and one eighth of the circle walked with the other
    /// seven mirrored from it.
    /// </summary>
    /// <remarks>
    /// The mirroring is what makes it exactly symmetric, which matters for the same reason the line
    /// draws from a canonical end: a circle that is a pixel fatter on one side than the other looks
    /// wrong and nothing in the picture explains why.
    /// </remarks>
    /// <param name="centreX">
    /// Centre in surface pixels.
    /// </param>
    /// <param name="centreY">
    /// Centre in surface pixels.
    /// </param>
    /// <param name="radius">
    /// Radius in surface pixels; zero or less draws nothing.
    /// </param>
    /// <param name="colour">
    /// Colour to draw with.
    /// </param>
    public void DrawCircle(int centreX, int centreY, int radius, GraphicsColor colour)
    {
        if (radius <= 0) return;

        int x = radius;
        int y = 0;

        // The decision variable, in the integer form that avoids any square root.
        int error = 1 - radius;

        while (x >= y)
        {
            // The eight octants. SetPixel clips, so a circle half off the screen costs nothing
            // extra here.
            SetPixel(centreX + x, centreY + y, colour);
            SetPixel(centreX + y, centreY + x, colour);
            SetPixel(centreX - y, centreY + x, colour);
            SetPixel(centreX - x, centreY + y, colour);
            SetPixel(centreX - x, centreY - y, colour);
            SetPixel(centreX - y, centreY - x, colour);
            SetPixel(centreX + y, centreY - x, colour);
            SetPixel(centreX + x, centreY - y, colour);

            y++;
            if (error < 0)
            {
                error += 2 * y + 1;
            }
            else
            {
                x--;
                error += 2 * (y - x) + 1;
            }
        }
    }

    /// <summary>
    /// How many crossings a scanline may collect on the stack before the fill rents a buffer.
    /// </summary>
    /// <remarks>
    /// A scanline cannot cross more edges than the polygon has, and ReGIS caps a polygon at 256
    /// vertices, so in practice this is never exceeded and the fill never allocates. The rented
    /// path exists so a caller that is not ReGIS cannot make this throw.
    /// </remarks>
    private const int StackCrossings = 256;

    /// <param name="points">
    /// Vertices as x,y,x,y... in surface pixels.
    /// </param>
    /// <param name="colour">
    /// Colour to fill with.
    /// </param>
    public void FillPolygon(ReadOnlySpan<int> points, GraphicsColor colour)
    {
        int count = points.Length / 2;              // an odd trailing value is not half a vertex
        if (count < 3) return;

        // Bounding box, clipped to the surface, so the scan only walks rows that can hold ink.
        int top = int.MaxValue;
        int bottom = int.MinValue;
        for (int v = 0; v < count; v++)
        {
            int y = points[v * 2 + 1];
            if (y < top) top = y;
            if (y > bottom) bottom = y;
        }

        if (top < 0) top = 0;
        if (bottom >= Height) bottom = Height - 1;
        if (top > bottom) return;

        // The scratch buffer is chosen here and the scan lives in its own method, because a
        // stackalloc may not be assigned to a span declared outside the block that allocates it.
        if (count <= StackCrossings)
        {
            Span<int> crossings = stackalloc int[StackCrossings];
            FillScan(points, count, top, bottom, crossings, colour);
            return;
        }

        int[] rented = ArrayPool<int>.Shared.Rent(count);
        try
        {
            FillScan(points, count, top, bottom, rented.AsSpan(0, count), colour);
        }
        finally
        {
            ArrayPool<int>.Shared.Return(rented);
        }
    }

    /// <summary>
    /// Walks the scanlines of a polygon and paints between crossing pairs.
    /// </summary>
    /// <param name="points">
    /// Vertices as x,y,x,y...
    /// </param>
    /// <param name="count">
    /// How many vertices.
    /// </param>
    /// <param name="top">
    /// First row to scan, already clipped to the surface.
    /// </param>
    /// <param name="bottom">
    /// Last row to scan, already clipped to the surface.
    /// </param>
    /// <param name="crossings">
    /// Scratch space for one row's crossings.
    /// </param>
    /// <param name="colour">
    /// Colour to fill with.
    /// </param>
    private void FillScan(ReadOnlySpan<int> points, int count, int top, int bottom,
        Span<int> crossings, GraphicsColor colour)
    {
        for (int y = top; y <= bottom; y++)
        {
            int found = 0;

            for (int v = 0; v < count; v++)
            {
                int ax = points[v * 2];
                int ay = points[v * 2 + 1];

                // The last vertex joins back to the first, so the caller never has to repeat it.
                int next = v + 1 == count ? 0 : v + 1;
                int bx = points[next * 2];
                int by = points[next * 2 + 1];

                if (ay == by) continue;         // a horizontal edge crosses no scanline

                // Half-open test - the lower endpoint counts, the upper one does not. That is what
                // stops a vertex shared by two edges being counted twice and turning the inside of
                // the shape inside out for that one row.
                int lowY = ay < by ? ay : by;
                int highY = ay < by ? by : ay;
                if (y < lowY || y >= highY) continue;

                // Where the edge cuts this row. Long arithmetic because a coordinate times a span
                // can leave int range on a large surface.
                long numerator = (long)(y - ay) * (bx - ax);
                crossings[found++] = ax + (int)(numerator / (by - ay));

                if (found == crossings.Length) break;
            }

            if (found < 2) continue;

            // Insertion sort: the counts here are tiny and it is stable and allocation-free.
            for (int a = 1; a < found; a++)
            {
                int value = crossings[a];
                int b = a - 1;
                while (b >= 0 && crossings[b] > value)
                {
                    crossings[b + 1] = crossings[b];
                    b--;
                }
                crossings[b + 1] = value;
            }

            // Even-odd: paint between crossing pairs.
            for (int pair = 0; pair + 1 < found; pair += 2)
            {
                int left = crossings[pair];
                int right = crossings[pair + 1];
                if (right < left) continue;

                if (left < 0) left = 0;
                if (right >= Width) right = Width - 1;
                if (left > right) continue;

                // Same reason as the rectangle fill: a coded write reads before it writes.
                if (IsIndexed)
                {
                    int rowStart = y * Width;
                    for (int col = left; col <= right; col++)
                    {
                        SetIndexedPixel(rowStart + col);
                    }

                    continue;
                }

                _pixels.AsSpan(y * Width + left, right - left + 1).Fill(colour.Value);
                _codes.AsSpan(y * Width + left, right - left + 1).Fill(NoIndex);
            }
        }
    }
}
