using System;
using RetroTerm.Core.Terminal.Buffer;

namespace RetroTerm.Core.Terminal.Rendering;

/// <summary>
/// One finished picture of the screen, handed from the session pump to the renderer.
///
/// WHY THIS EXISTS. The buffer's contract is single-writer-on-the-pump (see ScreenReader), and
/// scripts and MCP honour it by going through TerminalSession.RunOnSessionThreadAsync. The
/// renderer did not: it walked the live TerminalCell[,] from the UI thread while the pump was
/// writing into it. Two things go wrong there, and both are real rather than theoretical:
/// TerminalCell is a 20-byte struct, so a torn read can show a character with another cell's
/// colours; and a concurrent Resize swaps the whole array, so the indexer can throw mid-frame.
///
/// A frame fixes it by construction. The pump fills one and publishes it; the renderer only ever
/// reads a published frame, which nothing mutates afterwards. The frame is deliberately flat and
/// plain - it holds the finished answer, not a way to ask questions of the terminal.
///
/// Note it stores the VIEWPORT, already resolved through any scrollback offset. Resolving on the
/// pump is what lets the renderer stop touching scrollback - which the ring recycles in place, so
/// reading it from another thread was racy too.
/// </summary>
public sealed class ScreenFrame
{
    /// <summary>
    /// Cells in row-major order, Width * Height of them.
    /// </summary>
    private TerminalCell[] _cells = Array.Empty<TerminalCell>();

    /// <summary>
    /// The row immediately ABOVE the top of the screen, for smooth scrolling.
    /// </summary>
    /// <remarks>
    /// <para><b>Why a frame carries a row that is not on the screen</b></para>
    /// Smooth scrolling draws the whole screen shifted down by a few pixels and lets it slide up.
    /// Shifting it down leaves a gap at the top, and what belongs in that gap is the line that has
    /// just scrolled off - which is in the scrollback by the time anybody looks. Without it the
    /// animation is a blank band sliding up the screen instead of a line leaving it.
    ///
    /// One row, captured only while the mode is on. That is the whole cost of the feature on this
    /// side.
    /// </remarks>
    private TerminalCell[] _rowAboveTop = Array.Empty<TerminalCell>();

    /// <summary>
    /// Screen width in columns at the moment this frame was taken.
    /// </summary>
    public int Width { get; private set; }

    /// <summary>
    /// Screen height in rows at the moment this frame was taken.
    /// </summary>
    public int Height { get; private set; }

    /// <summary>
    /// Cursor row within the viewport.
    /// </summary>
    public int CursorRow { get; private set; }

    /// <summary>
    /// Cursor column within the viewport.
    /// </summary>
    public int CursorColumn { get; private set; }

    /// <summary>
    /// Whether the cursor should be drawn.
    /// </summary>
    public bool CursorVisible { get; private set; }

    /// <summary>
    /// The cursor's shape.
    /// </summary>
    public CursorStyle CursorStyle { get; private set; }

    /// <summary>
    /// DECSCNM - the whole screen is drawn with foreground and background swapped.
    /// </summary>
    /// <remarks>
    /// <para><b>Why it rides on the frame instead of flipping the cells</b></para>
    /// The VT420 Programmer Reference is explicit: "Screen mode only affects how the data appears
    /// on the screen. DECSCNM does not change the data in page memory." So the buffer keeps the
    /// colours the host wrote, and the inversion happens where the picture is drawn. Flipping the
    /// cells instead would corrupt every report that reads them back, and turning the mode off
    /// again would have to un-flip exactly the right ones.
    ///
    /// <para><b>It composes with SGR reverse rather than overriding it</b></para>
    /// A cell already marked reverse, on a reversed screen, comes out looking normal - the two
    /// swaps cancel. That is what makes a highlighted menu bar still stand out when a host turns
    /// the screen inverse, and it falls out of an exclusive-or rather than needing a rule.
    /// </remarks>
    public bool ReverseScreen { get; private set; }

    /// <summary>
    /// Text foreground a Sixel image asked for, or null when the theme's own default stands.
    /// </summary>
    /// <remarks>
    /// Carried on the frame rather than read from the emulator for the same reason
    /// <see cref="ReverseScreen"/> is: the renderer must see one consistent picture of the screen,
    /// and anything it reads separately can change between the frame being published and the frame
    /// being drawn.
    /// </remarks>
    public Graphics.GraphicsColor? ImageForeground { get; private set; }

    /// <summary>
    /// Text background a Sixel image asked for, or null when the theme's own default stands.
    /// </summary>
    public Graphics.GraphicsColor? ImageBackground { get; private set; }

    /// <summary>
    /// How many lines back this frame was resolved at. 0 is the live screen.
    /// </summary>
    public int ScrollOffset { get; private set; }

    /// <summary>
    /// How many lines of history existed when this frame was taken.
    /// </summary>
    public int ScrollbackLineCount { get; private set; }

    /// <summary>
    /// Whether this frame shows history rather than the live screen.
    /// </summary>
    public bool IsScrolledBack => ScrollOffset > 0;

    /// <summary>
    /// How many lines the PICTURE is deliberately running behind the buffer, for smooth scrolling.
    /// </summary>
    /// <remarks>
    /// <para><b>Separate from <see cref="ScrollOffset"/> on purpose</b></para>
    /// Both push the view back through the same history, so it is tempting to add the lag straight
    /// into the scroll offset and be done. That would be wrong: the scroll offset is the USER'S
    /// position, and several things read it as such - the scrollback indicator appears when it is
    /// non-zero, the cursor is hidden, and selection and search highlighting map view rows to buffer
    /// rows by subtracting it. Folding an animation into that number would flash the scrollback
    /// badge and misplace the selection every time output scrolled.
    ///
    /// So the lag resolves the cells, and the scroll offset keeps meaning what it always meant.
    /// </remarks>
    public int DisplayLagLines { get; private set; }

    /// <summary>
    /// Reads a cell. Out-of-range positions return a blank rather than throwing: a frame is a
    /// picture, and a renderer asking slightly outside it should draw nothing, not fall over.
    /// </summary>
    public TerminalCell this[int row, int col]
    {
        get
        {
            if (row < 0 || row >= Height || col < 0 || col >= Width)
                return TerminalCell.Empty;

            return _cells[(row * Width) + col];
        }
    }

    /// <summary>
    /// Whether <see cref="GetCellAboveTop"/> holds anything this frame.
    /// </summary>
    /// <remarks>
    /// False whenever smooth scrolling is off, so an ordinary frame costs nothing extra, and false
    /// as well when there is no scrollback to read - the very first line on a fresh screen has
    /// nothing above it, and drawing blanks there is the honest answer.
    /// </remarks>
    public bool HasRowAboveTop { get; private set; }

    /// <summary>
    /// One cell of the row immediately above the top of the screen.
    /// </summary>
    /// <param name="column">
    /// The column to read.
    /// </param>
    /// <returns>
    /// The cell, or an empty one when this frame carries no such row.
    /// </returns>
    public TerminalCell GetCellAboveTop(int column)
    {
        if (!HasRowAboveTop || column < 0 || column >= Width) return TerminalCell.Empty;
        return _rowAboveTop[column];
    }

    /// <summary>
    /// Fills this frame from the buffer. MUST be called on the session pump thread - it reads the
    /// live buffer, which is exactly the thing no other thread may do.
    /// </summary>
    /// <param name="buffer">
    /// The buffer to capture.
    /// </param>
    /// <param name="cursor">
    /// The cursor to capture.
    /// </param>
    /// <param name="scrollOffset">
    /// Lines scrolled back; 0 for the live screen.
    /// </param>
    /// <param name="reverseScreen">
    /// DECSCNM - draw the whole screen with foreground and background swapped.
    /// </param>
    /// <param name="imageForeground">
    /// Text foreground chosen by a graphics colour map, or null to keep the theme's.
    /// </param>
    /// <param name="imageBackground">
    /// Text background chosen by a graphics colour map, or null to keep the theme's.
    /// </param>
    /// <param name="captureRowAboveTop">
    /// Also capture the scrollback row just above the screen, which smooth scrolling draws into the
    /// gap while the screen is part-way through a line.
    /// </param>
    /// <param name="displayLagLines">
    /// How many lines the display is behind the buffer, so a fast host can keep writing while the
    /// smooth scroll catches up.
    /// </param>
    public void CaptureFrom(TerminalBuffer buffer, Cursor cursor, int scrollOffset,
        bool reverseScreen = false,
        Graphics.GraphicsColor? imageForeground = null,
        Graphics.GraphicsColor? imageBackground = null,
        bool captureRowAboveTop = false,
        int displayLagLines = 0)
    {
        if (buffer == null) throw new ArgumentNullException(nameof(buffer));
        if (cursor == null) throw new ArgumentNullException(nameof(cursor));
        if (scrollOffset < 0) scrollOffset = 0;
        if (displayLagLines < 0) displayLagLines = 0;

        // WHERE THE CELLS ARE READ FROM. The user's own scrollback position and the animation's lag
        // are two reasons to look back through the same history, so they simply add up here.
        int viewOffset = scrollOffset + displayLagLines;

        ReverseScreen = reverseScreen;
        ImageForeground = imageForeground;
        ImageBackground = imageBackground;

        Width = buffer.Width;
        Height = buffer.Height;
        ScrollOffset = scrollOffset;
        DisplayLagLines = displayLagLines;
        ScrollbackLineCount = buffer.ScrollbackLineCount;

        int needed = Width * Height;
        if (_cells.Length < needed)
        {
            // Grown, never shrunk - a frame is reused every publish and a resize is rare.
            _cells = new TerminalCell[needed];
        }

        if (viewOffset == 0)
        {
            // Live view: straight copy of the grid.
            for (int row = 0; row < Height; row++)
            {
                int rowStart = row * Width;
                for (int col = 0; col < Width; col++)
                {
                    _cells[rowStart + col] = buffer.GetCell(row, col);
                }
            }
        }
        else
        {
            // Scrolled back: resolve through history here, on the pump, so the renderer never
            // touches the scrollback ring.
            var blank = TerminalCell.Empty;
            for (int row = 0; row < Height; row++)
            {
                int rowStart = row * Width;
                for (int col = 0; col < Width; col++)
                {
                    _cells[rowStart + col] = buffer.TryGetViewportCell(row, col, viewOffset, out var cell)
                        ? cell
                        : blank;
                }
            }
        }

        // THE ROW ABOVE THE TOP, for smooth scrolling. Read through the same viewport helper the
        // scrollback view uses, one line further back than whatever is being shown - so it works
        // whether the reader is live or scrolled back. Only while the mode is on: an ordinary frame
        // must not pay for a feature nobody switched on.
        HasRowAboveTop = false;
        if (captureRowAboveTop)
        {
            if (_rowAboveTop.Length < Width)
            {
                _rowAboveTop = new TerminalCell[Width];
            }

            bool gotAny = false;
            for (int col = 0; col < Width; col++)
            {
                // Row 0 of a view one line further back IS the row above this view's top.
                // Asking for row -1 does not work: the helper rejects a negative row, and it is
                // right to - the offset is where "one line earlier" belongs.
                if (buffer.TryGetViewportCell(0, col, viewOffset + 1, out var cell))
                {
                    _rowAboveTop[col] = cell;
                    gotAny = true;
                }
                else
                {
                    _rowAboveTop[col] = TerminalCell.Empty;
                }
            }

            HasRowAboveTop = gotAny;
        }

        // THE CURSOR MOVES WITH THE PICTURE. While the view is running behind, the line the cursor
        // is on has not been revealed yet - it sat that many rows lower on the screen being shown.
        // Adding the lag walks it back to where it was, and if that is off the bottom the renderer
        // simply never matches a row and draws no cursor, which is the honest answer: the cursor is
        // on a line the viewer cannot see yet.
        CursorRow = cursor.Row + displayLagLines;
        CursorColumn = cursor.Column;
        CursorVisible = cursor.Visible;
        CursorStyle = cursor.Style;
    }
}
