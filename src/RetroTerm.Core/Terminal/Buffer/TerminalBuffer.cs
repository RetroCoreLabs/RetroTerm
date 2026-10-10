using System;
using System.Collections.Generic;

namespace RetroTerm.Core.Terminal.Buffer;

/// <summary>
/// Manages the terminal's screen buffer and scrollback history.
/// Uses a 2D array for the active screen and a circular buffer for scrollback.
/// </summary>
public class TerminalBuffer
{
    private TerminalCell[,] _screen;
    private TerminalCell[,]? _alternateScreen;

    // Per-line metadata. Today just "did this line wrap onto the next one", which the screen has
    // no other way to know: a line that filled up and continued and a line that ended with a
    // newline look identical once they are in the grid. Without it, reading the screen back as
    // text (what MCP and scripts do) breaks every wrapped line at the terminal's width, and
    // reflow-on-resize is impossible in principle. Kept parallel to the grids and swapped with
    // them, so the alternate screen has its own.
    private bool[] _lineWrapped;
    private bool[]? _alternateLineWrapped;
    private bool[] _scrollbackWrapped;

    // Scrollback as a fixed-capacity ring.
    //
    // This used to be a List that Add()ed a freshly allocated row and then RemoveAt(0)'d once it
    // was over the limit. Both halves were wrong on the hot path: RemoveAt(0) shifts every
    // remaining entry, so at the default 10000 lines a full screen of scrolling moved a quarter of
    // a million references for nothing, and every scrolled line allocated a new row array that the
    // GC had to take back. A ring does neither - the oldest slot is overwritten in place, and its
    // row array is REUSED rather than reallocated, so steady-state scrolling allocates nothing.
    private TerminalCell[]?[] _scrollbackRing;
    private int _scrollbackStart;   // index of the oldest line in the ring
    private int _scrollbackCount;   // number of lines currently held
    private readonly int _maxScrollbackLines;
    private bool _usingAlternateBuffer;

    /// <summary>
    /// Gets the current width of the terminal in columns
    /// </summary>
    public int Width { get; private set; }

    /// <summary>
    /// Gets the current height of the terminal in rows
    /// </summary>
    public int Height { get; private set; }

    /// <summary>
    /// Gets whether the alternate screen buffer is currently active
    /// </summary>
    public bool IsUsingAlternateBuffer => _usingAlternateBuffer;

    /// <summary>
    /// Gets the number of lines in the scrollback buffer
    /// </summary>
    public int ScrollbackLineCount => _scrollbackCount;

    /// <summary>
    /// Gets or sets the maximum number of scrollback lines to keep
    /// </summary>
    public int MaxScrollbackLines => _maxScrollbackLines;

    /// <summary>
    /// Creates a new terminal buffer with the specified dimensions
    /// </summary>
    /// <param name="width">
    /// Width in columns
    /// </param>
    /// <param name="height">
    /// Height in rows
    /// </param>
    /// <param name="maxScrollbackLines">
    /// Maximum scrollback lines (default 10000)
    /// </param>
    public TerminalBuffer(int width, int height, int maxScrollbackLines = 10000)
    {
        if (width <= 0) throw new ArgumentException("Width must be positive", nameof(width));
        if (height <= 0) throw new ArgumentException("Height must be positive", nameof(height));
        if (maxScrollbackLines < 0) throw new ArgumentException("Max scrollback lines cannot be negative", nameof(maxScrollbackLines));

        Width = width;
        Height = height;
        _maxScrollbackLines = maxScrollbackLines;
        _screen = new TerminalCell[height, width];

        // Only the slot array is allocated up front (one reference per line - 80 KB at the default
        // 10000). The row arrays themselves are created on first use and then recycled.
        _scrollbackRing = maxScrollbackLines > 0 ? new TerminalCell[maxScrollbackLines][] : Array.Empty<TerminalCell[]?>();
        _scrollbackWrapped = maxScrollbackLines > 0 ? new bool[maxScrollbackLines] : Array.Empty<bool>();
        _lineWrapped = new bool[height];

        Clear();
    }

    /// <summary>
    /// Gets whether the given screen line continues onto the next one (it filled up and wrapped)
    /// rather than ending there.
    /// </summary>
    public bool IsLineWrapped(int row)
    {
        return row >= 0 && row < _lineWrapped.Length && _lineWrapped[row];
    }

    /// <summary>
    /// Records whether the given screen line continues onto the next one. Set by the emulator when
    /// auto-wrap carries the cursor to the following row.
    /// </summary>
    public void SetLineWrapped(int row, bool wrapped)
    {
        if (row >= 0 && row < _lineWrapped.Length)
        {
            _lineWrapped[row] = wrapped;
        }
    }

    /// <summary>
    /// Gets whether a scrollback line continued onto the line after it.
    /// </summary>
    /// <param name="index">
    /// Index from oldest (0) to newest (ScrollbackLineCount-1)
    /// </param>
    public bool IsScrollbackLineWrapped(int index)
    {
        if (index < 0 || index >= _scrollbackCount)
            return false;

        return _scrollbackWrapped[(_scrollbackStart + index) % _maxScrollbackLines];
    }

    /// <summary>
    /// Appends one line to scrollback, overwriting the oldest line once the ring is full.
    ///
    /// The row array of the evicted line is reused when it is already the right width, which is
    /// the normal case - that is what makes steady-state scrolling allocation-free.
    /// </summary>
    private void PushToScrollback(int screenRow)
    {
        if (_maxScrollbackLines == 0)
            return;

        // NOT from the alternate screen. Switching buffers swaps which grid _screen points at, so
        // without this the scroll path cannot tell a shell's output scrolling off the top from a
        // pager scrolling its own display - and it pushed both. Scrollback is the user's record of
        // what went past them; a full-screen program's redraws never went past anything.
        if (_usingAlternateBuffer)
            return;

        int slot;
        if (_scrollbackCount < _maxScrollbackLines)
        {
            slot = (_scrollbackStart + _scrollbackCount) % _maxScrollbackLines;
            _scrollbackCount++;
        }
        else
        {
            // Full: the oldest line is the one being replaced, so the window slides forward.
            slot = _scrollbackStart;
            _scrollbackStart = (_scrollbackStart + 1) % _maxScrollbackLines;
        }

        var line = _scrollbackRing[slot];
        if (line == null || line.Length != Width)
        {
            line = new TerminalCell[Width];
            _scrollbackRing[slot] = line;
        }

        for (var col = 0; col < Width; col++)
        {
            line[col] = _screen[screenRow, col];
        }

        // The wrap flag goes to history with the line it belongs to.
        _scrollbackWrapped[slot] = IsLineWrapped(screenRow);
    }

    /// <summary>
    /// Gets a reference to a cell at the specified position
    /// </summary>
    public ref TerminalCell this[int row, int col]
    {
        get
        {
            if (row < 0 || row >= Height)
                throw new ArgumentOutOfRangeException(nameof(row), $"Row must be between 0 and {Height - 1}");
            if (col < 0 || col >= Width)
                throw new ArgumentOutOfRangeException(nameof(col), $"Column must be between 0 and {Width - 1}");

            return ref _screen[row, col];
        }
    }

    /// <summary>
    /// Tries to get a cell at the specified position without throwing exceptions
    /// </summary>
    public bool TryGetCell(int row, int col, out TerminalCell cell)
    {
        if (row >= 0 && row < Height && col >= 0 && col < Width)
        {
            cell = _screen[row, col];
            return true;
        }

        cell = default;
        return false;
    }

    /// <summary>
    /// Sets a cell at the specified position
    /// </summary>
    public void SetCell(int row, int col, TerminalCell cell)
    {
        if (row >= 0 && row < Height && col >= 0 && col < Width)
        {
            _screen[row, col] = cell;
        }
    }

    /// <summary>
    /// Gets a cell at the specified position
    /// </summary>
    public TerminalCell GetCell(int row, int col)
    {
        if (row >= 0 && row < Height && col >= 0 && col < Width)
        {
            return _screen[row, col];
        }

        return TerminalCell.Empty;
    }

    /// <summary>
    /// Gets a line from scrollback history
    /// </summary>
    /// <param name="index">
    /// Index from oldest (0) to newest (ScrollbackLineCount-1)
    /// </param>
    public TerminalCell[]? GetScrollbackLine(int index)
    {
        if (index < 0 || index >= _scrollbackCount)
            return null;

        return _scrollbackRing[(_scrollbackStart + index) % _maxScrollbackLines];
    }

    /// <summary>
    /// Clears the entire screen buffer to empty cells
    /// </summary>
    public void Clear()
    {
        // THE ACTIVE SCREEN ONLY, and that is the whole point of this method.
        //
        // It used to clear the inactive buffer as well, which meant a full-screen program erasing
        // the alternate screen also wiped the primary screen behind it - so when the program
        // exited, the shell's screen was blank. That is the classic "quitting the editor cleared
        // my terminal" complaint, and it was this line.
        //
        // A reset genuinely does want both, and asks for both: see ClearBothBuffers.
        for (var row = 0; row < Height; row++)
        {
            for (var col = 0; col < Width; col++)
            {
                _screen[row, col] = TerminalCell.Empty; // Cleared: codepoint 0, distinct from a WRITTEN space
            }
        }

        Array.Clear(_lineWrapped, 0, _lineWrapped.Length);
    }

    /// <summary>
    /// Takes over another buffer's contents: the visible screen, the per-line wrap flags and as much
    /// scrollback as this buffer has room for.
    /// </summary>
    /// <remarks>
    /// <para><b>Why a buffer copies rather than an emulator adopting one</b></para>
    /// Changing the terminal type mid-session builds a NEW emulator, and an emulator's buffer is a
    /// readonly field created in its constructor - deliberately, because everything else in the
    /// emulator holds on to it. So the new buffer takes the old one's contents instead of the new
    /// emulator taking the old buffer. It lives here because the scrollback ring's start, count and
    /// row arrays are private to this class, and reaching them through the public surface would mean
    /// widening it for one caller.
    /// <para><b>What deliberately does not come across</b></para>
    /// The alternate screen. It belongs to a full-screen program that is running RIGHT NOW on the
    /// other end, and that program is talking to a terminal that has just been replaced underneath
    /// it. Carrying its screen over would show a picture drawn for a terminal that no longer exists;
    /// the program will repaint on its next output either way.
    /// Graphics planes do not come across either, and cannot: the new terminal may have no bitmap at
    /// all. The caller says so rather than dropping them silently.
    /// </remarks>
    /// <param name="source">
    /// The buffer to copy from. Nothing happens when it is null or is this same buffer.
    /// </param>
    public void CopyFrom(TerminalBuffer source)
    {
        if (source == null || ReferenceEquals(source, this)) return;

        // Only the overlap is copied. The two are usually the same size - the caller resizes first -
        // but a terminal with a different fixed geometry (a 4014 is 74 by 35) legitimately differs,
        // and the top-left corner is the part worth keeping either way.
        int rows = Math.Min(Height, source.Height);
        int columns = Math.Min(Width, source.Width);

        for (int row = 0; row < rows; row++)
        {
            for (int col = 0; col < columns; col++)
            {
                _screen[row, col] = source._screen[row, col];
            }

            _lineWrapped[row] = source._lineWrapped[row];
        }

        // Scrollback, oldest first, keeping the NEWEST when the destination ring is smaller - the
        // recent lines are the ones anybody scrolls back to look at.
        if (_maxScrollbackLines == 0 || source._scrollbackCount == 0) return;

        int available = source._scrollbackCount;
        int first = available > _maxScrollbackLines ? available - _maxScrollbackLines : 0;

        _scrollbackStart = 0;
        _scrollbackCount = 0;

        for (int i = first; i < available; i++)
        {
            var line = source.GetScrollbackLine(i);
            if (line == null) continue;

            // The destination's own row width, so a line from a wider terminal is truncated and a
            // line from a narrower one is padded with cleared cells rather than left as garbage.
            var copy = new TerminalCell[Width];
            int take = Math.Min(Width, line.Length);
            for (int col = 0; col < take; col++) copy[col] = line[col];
            for (int col = take; col < Width; col++) copy[col] = TerminalCell.Empty;

            int slot = _scrollbackCount;
            _scrollbackRing[slot] = copy;
            _scrollbackWrapped[slot] = source.IsScrollbackLineWrapped(i);
            _scrollbackCount++;
        }
    }

    /// <summary>
    /// Clears the active screen AND the one behind it.
    /// </summary>
    /// <remarks>
    /// For a hard reset, which means "you are a terminal that has just been switched on" - and a
    /// terminal that has just been switched on has nothing on either screen. Every other erase
    /// wants <see cref="Clear"/>, which leaves the inactive buffer alone.
    /// </remarks>
    public void ClearBothBuffers()
    {
        Clear();

        if (_alternateScreen == null) return;

        for (var row = 0; row < Height; row++)
        {
            for (var col = 0; col < Width; col++)
            {
                _alternateScreen[row, col] = TerminalCell.Empty;
            }
        }

        if (_alternateLineWrapped != null)
        {
            Array.Clear(_alternateLineWrapped, 0, _alternateLineWrapped.Length);
        }
    }

    /// <summary>
    /// Clears a specific line
    /// </summary>
    public void ClearLine(int row)
    {
        if (row < 0 || row >= Height)
            throw new ArgumentOutOfRangeException(nameof(row));

        for (var col = 0; col < Width; col++)
        {
            _screen[row, col] = TerminalCell.Empty; // Cleared, not a written space
        }

        // A cleared line no longer continues onto the next one.
        _lineWrapped[row] = false;
    }

    /// <summary>
    /// Clears from the specified position to the end of the line
    /// </summary>
    public void ClearToEndOfLine(int row, int startCol)
    {
        if (row < 0 || row >= Height)
            throw new ArgumentOutOfRangeException(nameof(row));

        for (var col = startCol; col < Width; col++)
        {
            _screen[row, col] = TerminalCell.Empty; // Cleared, not a written space
        }

        // Erasing to the end of a line ends it. Whatever spilled over onto the next line is no
        // longer the continuation of anything, so the flag must go — otherwise a screen-to-text
        // read joins two lines that are no longer related, and reflow would too.
        _lineWrapped[row] = false;
    }

    /// <summary>
    /// Clears from the beginning of the line to the specified position
    /// </summary>
    public void ClearFromStartOfLine(int row, int endCol)
    {
        if (row < 0 || row >= Height)
            throw new ArgumentOutOfRangeException(nameof(row));

        for (var col = 0; col <= endCol && col < Width; col++)
        {
            _screen[row, col] = TerminalCell.Empty; // Cleared, not a written space
        }
    }

    /// <summary>
    /// Scrolls the entire buffer up by one line, moving the top line to scrollback
    /// </summary>
    public void ScrollUp()
    {
        ScrollUp(0, Height - 1);
    }

    /// <summary>
    /// Scrolls a region up by one line. Content scrolled off the top of a region that starts at
    /// row 0 goes to scrollback, because that is the screen's own history.
    /// </summary>
    /// <param name="topRow">
    /// Top row of scroll region (inclusive)
    /// </param>
    /// <param name="bottomRow">
    /// Bottom row of scroll region (inclusive)
    /// </param>
    public void ScrollUp(int topRow, int bottomRow)
    {
        ScrollUp(topRow, bottomRow, topRow == 0);
    }

    /// <summary>
    /// Scrolls a region up by one line, choosing explicitly whether the departing line is kept.
    ///
    /// The distinction matters: an index/line-feed at the bottom of the screen pushes the top line
    /// into history, but DL (delete line) at row 0 must NOT - those lines were removed by the
    /// application, they never scrolled off, and putting them in scrollback corrupts the history a
    /// user scrolls back through.
    /// </summary>
    /// <param name="topRow">
    /// Top row of scroll region (inclusive)
    /// </param>
    /// <param name="bottomRow">
    /// Bottom row of scroll region (inclusive)
    /// </param>
    /// <param name="toScrollback">
    /// Whether the line leaving the top of the region is kept in scrollback
    /// </param>
    public void ScrollUp(int topRow, int bottomRow, bool toScrollback)
    {
        if (topRow < 0 || topRow >= Height)
            throw new ArgumentOutOfRangeException(nameof(topRow));
        if (bottomRow < 0 || bottomRow >= Height)
            throw new ArgumentOutOfRangeException(nameof(bottomRow));
        if (topRow > bottomRow)
            throw new ArgumentException("Top row must be less than or equal to bottom row");

        // Save the departing line to scrollback when asked. Only meaningful from row 0 - a line
        // leaving the top of a mid-screen scrolling region is simply discarded.
        if (toScrollback && topRow == 0)
        {
            PushToScrollback(0);
        }

        // Shift lines up. The screen is a rectangular array, so it is laid out row-major and the
        // whole block moves in one Array.Copy - which handles the overlap as if it had copied via
        // a temporary. That replaces a cell-by-cell double loop on the hottest path there is.
        int rowsToMove = bottomRow - topRow;
        if (rowsToMove > 0)
        {
            Array.Copy(_screen, (topRow + 1) * Width, _screen, topRow * Width, rowsToMove * Width);
            // The wrap flags belong to the lines, so they move with them.
            Array.Copy(_lineWrapped, topRow + 1, _lineWrapped, topRow, rowsToMove);
        }

        // Clear the bottom line
        ClearLine(bottomRow);
    }

    /// <summary>
    /// Scrolls a region down by one line
    /// </summary>
    /// <param name="topRow">
    /// Top row of scroll region (inclusive)
    /// </param>
    /// <param name="bottomRow">
    /// Bottom row of scroll region (inclusive)
    /// </param>
    public void ScrollDown(int topRow, int bottomRow)
    {
        if (topRow < 0 || topRow >= Height)
            throw new ArgumentOutOfRangeException(nameof(topRow));
        if (bottomRow < 0 || bottomRow >= Height)
            throw new ArgumentOutOfRangeException(nameof(bottomRow));
        if (topRow > bottomRow)
            throw new ArgumentException("Top row must be less than or equal to bottom row");

        // Shift lines down - one block move, same reasoning as ScrollUp.
        int rowsToMove = bottomRow - topRow;
        if (rowsToMove > 0)
        {
            Array.Copy(_screen, topRow * Width, _screen, (topRow + 1) * Width, rowsToMove * Width);
            Array.Copy(_lineWrapped, topRow, _lineWrapped, topRow + 1, rowsToMove);
        }

        // Clear the top line
        ClearLine(topRow);
    }

    /// <summary>
    /// Inserts blank lines at the specified position, scrolling content down to the bottom of the
    /// screen. Prefer the overload taking an explicit bottom row - IL is defined against the active
    /// scrolling region, not the whole screen.
    /// </summary>
    public void InsertLines(int row, int count)
    {
        InsertLines(row, count, Height - 1);
    }

    /// <summary>
    /// Inserts blank lines at the specified position, scrolling content down within a region.
    ///
    /// IL (CSI L) is bounded by the active scrolling region: lines pushed past the region's bottom
    /// row fall off, they do not push the rest of the screen down. Ignoring the region is what made
    /// full-screen editors inside a DECSTBM region smear their status line.
    /// </summary>
    /// <param name="row">
    /// Row to insert at (inclusive)
    /// </param>
    /// <param name="count">
    /// Number of blank lines to insert
    /// </param>
    /// <param name="bottomRow">
    /// Bottom row of the active scrolling region (inclusive)
    /// </param>
    public void InsertLines(int row, int count, int bottomRow)
    {
        if (row < 0 || row >= Height)
            throw new ArgumentOutOfRangeException(nameof(row));
        if (bottomRow < 0 || bottomRow >= Height)
            throw new ArgumentOutOfRangeException(nameof(bottomRow));

        // A cursor outside the region means IL does nothing at all.
        if (row > bottomRow)
            return;

        for (var i = 0; i < count; i++)
        {
            ScrollDown(row, bottomRow);
        }
    }

    /// <summary>
    /// Deletes lines at the specified position, scrolling content up to the bottom of the screen.
    /// Prefer the overload taking an explicit bottom row.
    /// </summary>
    public void DeleteLines(int row, int count)
    {
        DeleteLines(row, count, Height - 1);
    }

    /// <summary>
    /// Deletes lines at the specified position, scrolling content up within a region.
    ///
    /// Deleted lines are NOT added to scrollback - see ScrollUp(top, bottom, toScrollback).
    /// </summary>
    /// <param name="row">
    /// Row to delete from (inclusive)
    /// </param>
    /// <param name="count">
    /// Number of lines to delete
    /// </param>
    /// <param name="bottomRow">
    /// Bottom row of the active scrolling region (inclusive)
    /// </param>
    public void DeleteLines(int row, int count, int bottomRow)
    {
        if (row < 0 || row >= Height)
            throw new ArgumentOutOfRangeException(nameof(row));
        if (bottomRow < 0 || bottomRow >= Height)
            throw new ArgumentOutOfRangeException(nameof(bottomRow));

        // A cursor outside the region means DL does nothing at all.
        if (row > bottomRow)
            return;

        for (var i = 0; i < count; i++)
        {
            ScrollUp(row, bottomRow, toScrollback: false);
        }
    }

    /// <summary>
    /// Clears the scrollback buffer
    /// </summary>
    public void ClearScrollback()
    {
        // The row arrays are deliberately kept in the ring, not dropped. They are the right size and
        // will be reused by the next lines that scroll off, which is the whole point of the ring.
        _scrollbackStart = 0;
        _scrollbackCount = 0;
    }

    /// <summary>
    /// Resizes the terminal buffer, preserving content where possible
    /// </summary>
    public void Resize(int newWidth, int newHeight)
    {
        if (newWidth <= 0) throw new ArgumentException("Width must be positive", nameof(newWidth));
        if (newHeight <= 0) throw new ArgumentException("Height must be positive", nameof(newHeight));

        if (newWidth == Width && newHeight == Height)
            return; // No change needed

        _screen = ResizeGrid(_screen, newWidth, newHeight);

        // The inactive buffer must be resized too. It used to be left at the old size while
        // Width/Height moved on, so Clear() indexed past the end of it (a hard crash on any resize
        // taller than the old screen), and switching back handed the caller a wrongly sized
        // primary screen. Whichever buffer is inactive still holds real content - the primary's
        // scrollback view, or an editor's screen - so it is resized, not thrown away.
        if (_alternateScreen != null)
        {
            _alternateScreen = ResizeGrid(_alternateScreen, newWidth, newHeight);
            _alternateLineWrapped = ResizeFlags(_alternateLineWrapped, newHeight);
        }

        _lineWrapped = ResizeFlags(_lineWrapped, newHeight);

        Width = newWidth;
        Height = newHeight;
    }

    /// <summary>
    /// Resizes AND reflows: paragraphs that wrapped are re-laid out to the new width instead of
    /// being cut off at the edge.
    ///
    /// WHY THIS IS SEPARATE FROM <see cref="Resize"/>. Reflow only makes sense for the primary
    /// screen. The alternate buffer belongs to a full-screen program — an editor, a pager — which
    /// is told the new size and redraws itself; re-wrapping its lines underneath it would corrupt
    /// a display it is about to repaint anyway. So the caller decides, and <see cref="Resize"/>
    /// stays as the plain "keep the top-left corner" behaviour.
    ///
    /// The per-line wrap flags exist precisely for this. In the grid, a line that filled up and
    /// carried on looks exactly like one that ended with a newline; the flag is the only record of
    /// which it was, and without it a resize cannot know where a paragraph ends.
    /// </summary>
    /// <param name="newWidth">
    /// New width in columns.
    /// </param>
    /// <param name="newHeight">
    /// New height in rows.
    /// </param>
    /// <param name="cursorRow">
    /// Cursor row in, reflowed cursor row out.
    /// </param>
    /// <param name="cursorColumn">
    /// Cursor column in, reflowed cursor column out.
    /// </param>
    public void ResizeWithReflow(int newWidth, int newHeight, ref int cursorRow, ref int cursorColumn)
    {
        if (newWidth <= 0) throw new ArgumentException("Width must be positive", nameof(newWidth));
        if (newHeight <= 0) throw new ArgumentException("Height must be positive", nameof(newHeight));

        if (newWidth == Width && newHeight == Height)
            return;

        // The alternate buffer never reflows and has no scrollback of its own: a full-screen
        // program is told the new size and repaints.
        if (IsUsingAlternateBuffer)
        {
            Resize(newWidth, newHeight);
            if (cursorRow > Height - 1) cursorRow = Height - 1;
            if (cursorColumn > Width - 1) cursorColumn = Width - 1;
            return;
        }

        // A pure height change has nothing to re-wrap, but it is NOT nothing: the screen slides
        // over the history rather than being cut off at the bottom. See ResizeHeightThroughHistory.
        if (newWidth == Width)
        {
            ResizeHeightThroughHistory(newHeight, ref cursorRow);
            if (cursorColumn > Width - 1) cursorColumn = Width - 1;
            return;
        }

        // ── 1. Read the screen as PARAGRAPHS ─────────────────────────────────────────────
        // A paragraph is one or more consecutive rows joined by the wrap flag. Its length is the
        // full width for every row that continues, plus the used part of the row that ends it.
        var paragraphs = new List<TerminalCell[]>(Height);
        var cursorParagraph = -1;
        var cursorOffset = 0;

        var current = new List<TerminalCell>(Width * 2);
        var paragraphStartRow = 0;

        // THE HISTORY COMES FIRST, BUT ONLY WHEN WIDENING. Until 10 October 2026 only the screen was
        // ever re-laid out. Narrowing the window pushed the rows that no longer fit into scrollback
        // at the NARROW width, and widening it again never reached them: they stayed wrapped at 20
        // or 27 columns in the history while the screen showed only the tail of what had been
        // there. A welcome screen dragged small and big again came back with its logo in pieces.
        // History rows are stored exactly like screen rows - one row, one wrap flag - so when the
        // window gets wider they join the same paragraphs as the screen, and the document is split
        // again below: the last rows fill the screen, anything older goes back into scrollback at
        // the new width.
        //
        // NARROWING LEAVES OLD HISTORY ALONE, as it always did. Re-wrapping it narrower multiplies
        // its rows (a 110-column line becomes six rows at 20), and the ring holds a fixed number of
        // ROWS: measured, a 12,000-line history dragged to 20 columns needed about 70,000 rows and
        // the ring kept the newest 10,000, so the oldest lines were deleted for good by a window
        // drag. Only the rows that spill off the screen NOW are written at the narrow width, and
        // widening rejoins exactly those.
        // WIDENING STOPS AT THE LAST OLD LINE THAT IS WIDER THAN THE NEW WIDTH. A line written at 120
        // columns and met again at 40 would be wrapped into three rows, and a history near the ring's
        // limit would lose its oldest lines to that, only because the window passed through 40 on
        // its way to 100 (measured: 10,000 rows became 3,308 over one drag). So history is folded in
        // only from the row after the last STANDALONE row (not part of a wrapped paragraph) longer
        // than the new width. Everything before that stays exactly as it was stored. The rows a
        // shrink wrapped narrow have no such row after them, and those are the ones that must come
        // back.
        bool foldHistoryIn = newWidth > Width;
        int foldFrom = 0;
        if (foldHistoryIn)
        {
            for (int i = _scrollbackCount - 1; i >= 0; i--)
            {
                var candidate = GetScrollbackLine(i);
                bool standalone = !IsScrollbackLineWrapped(i) && (i == 0 || !IsScrollbackLineWrapped(i - 1));
                if (standalone && candidate != null && UsedLengthOf(candidate) > newWidth)
                {
                    foldFrom = i + 1;
                    break;
                }
            }
        }

        int historyRows = foldHistoryIn ? _scrollbackCount : 0;
        for (int i = foldFrom; i < historyRows; i++)
        {
            var historyLine = GetScrollbackLine(i);
            bool historyContinues = IsScrollbackLineWrapped(i);
            int historyUsed = historyLine == null ? 0 : (historyContinues ? historyLine.Length : UsedLengthOf(historyLine));

            for (int col = 0; col < historyUsed; col++)
            {
                current.Add(historyLine![col]);
            }

            if (!historyContinues)
            {
                paragraphs.Add(current.ToArray());
                current.Clear();
            }
        }

        for (int row = 0; row < Height; row++)
        {
            bool continues = row < Height - 1 && _lineWrapped[row];
            int used = continues ? Width : UsedLength(row);

            // Remember where the cursor sits INSIDE its paragraph, so it can be put back at the
            // same point in the text rather than at the same grid position.
            if (row == cursorRow)
            {
                cursorParagraph = paragraphs.Count;
                cursorOffset = current.Count + cursorColumn;
            }

            for (int col = 0; col < used; col++)
            {
                current.Add(_screen[row, col]);
            }

            if (!continues)
            {
                paragraphs.Add(current.ToArray());
                current.Clear();
                paragraphStartRow = row + 1;
            }
        }

        if (current.Count > 0 || paragraphStartRow < Height)
        {
            paragraphs.Add(current.ToArray());
        }

        // ── 2. Lay the paragraphs out again at the new width ─────────────────────────────
        var rows = new List<TerminalCell[]>(Height);
        var wrapped = new List<bool>(Height);
        int newCursorRow = 0;
        int newCursorColumn = 0;

        for (int p = 0; p < paragraphs.Count; p++)
        {
            var text = paragraphs[p];
            int emittedForThisParagraph = 0;
            int offset = 0;

            // A paragraph always occupies at least one row, even when empty — an empty line on
            // screen is still a line.
            do
            {
                int take = text.Length - offset;
                if (take > newWidth) take = newWidth;
                if (take < 0) take = 0;

                var line = new TerminalCell[newWidth];
                for (int i = 0; i < newWidth; i++)
                {
                    line[i] = i < take ? text[offset + i] : TerminalCell.Empty;
                }

                if (p == cursorParagraph
                    && cursorOffset >= offset
                    && (cursorOffset < offset + newWidth || offset + newWidth >= text.Length))
                {
                    newCursorRow = rows.Count;
                    newCursorColumn = cursorOffset - offset;
                    if (newCursorColumn > newWidth - 1) newCursorColumn = newWidth - 1;
                }

                rows.Add(line);
                offset += newWidth;
                emittedForThisParagraph++;

                // Every row of a paragraph except its last one continues onto the next.
                wrapped.Add(offset < text.Length);
            }
            while (offset < text.Length);

            _ = emittedForThisParagraph;
        }

        // ── 3. Fit the result to the new screen, spilling the top into scrollback ────────
        // Drop trailing BLANK rows first. Narrowing a screen makes a paragraph occupy more rows,
        // and if the blank rows below it counted towards the total they pushed real text off the
        // top into scrollback — so shrinking a 5-row screen holding one line of text scrolled that
        // line away and left the screen showing its tail. Blank rows below the cursor are padding,
        // not content, so they give way first.
        int lastMeaningful = rows.Count - 1;
        while (lastMeaningful > newCursorRow && !wrapped[lastMeaningful] && IsBlankLine(rows[lastMeaningful]))
        {
            lastMeaningful--;
        }

        if (lastMeaningful < rows.Count - 1)
        {
            int drop = rows.Count - 1 - lastMeaningful;
            rows.RemoveRange(lastMeaningful + 1, drop);
            wrapped.RemoveRange(lastMeaningful + 1, drop);
        }

        // When the history was folded into the document above, the ring starts again empty and takes
        // back only what does not fit on the screen. Its row arrays stay in the ring for reuse.
        // When it was not (narrowing), the ring keeps what it has and the overflow is added to it.
        if (foldHistoryIn)
        {
            // The rows before foldFrom were not touched and stay; the rest went into the document.
            _scrollbackCount = foldFrom;
            if (foldFrom == 0)
            {
                _scrollbackStart = 0;
            }
        }

        int overflow = rows.Count - newHeight;
        if (overflow > 0)
        {
            for (int i = 0; i < overflow; i++)
            {
                PushLineToScrollback(rows[i], wrapped[i]);
            }

            rows.RemoveRange(0, overflow);
            wrapped.RemoveRange(0, overflow);
            newCursorRow -= overflow;
        }

        while (rows.Count < newHeight)
        {
            var blank = new TerminalCell[newWidth];
            for (int i = 0; i < newWidth; i++) blank[i] = TerminalCell.Empty;
            rows.Add(blank);
            wrapped.Add(false);
        }

        // ── 4. Commit ───────────────────────────────────────────────────────────────────
        var screen = new TerminalCell[newHeight, newWidth];
        for (int row = 0; row < newHeight; row++)
        {
            var line = rows[row];
            for (int col = 0; col < newWidth; col++)
            {
                screen[row, col] = line[col];
            }
        }

        _screen = screen;

        if (_alternateScreen != null)
        {
            _alternateScreen = ResizeGrid(_alternateScreen, newWidth, newHeight);
            _alternateLineWrapped = ResizeFlags(_alternateLineWrapped, newHeight);
        }

        _lineWrapped = new bool[newHeight];
        for (int row = 0; row < newHeight; row++)
        {
            _lineWrapped[row] = wrapped[row];
        }

        Width = newWidth;
        Height = newHeight;

        if (newCursorRow < 0) newCursorRow = 0;
        if (newCursorRow > newHeight - 1) newCursorRow = newHeight - 1;
        if (newCursorColumn < 0) newCursorColumn = 0;
        if (newCursorColumn > newWidth - 1) newCursorColumn = newWidth - 1;

        cursorRow = newCursorRow;
        cursorColumn = newCursorColumn;
    }

    /// <summary>
    /// Changes the height by sliding the screen over the scrollback, rather than keeping the top
    /// corner and cutting off the bottom.
    ///
    /// A terminal window is a WINDOW onto a longer history. Making it shorter must scroll the top
    /// rows up into that history and keep what is at the bottom — the shell prompt, the last line
    /// of output, whatever the user was actually looking at. Keeping the top and dropping the
    /// bottom threw away the newest content and left the cursor pointing at nothing.
    ///
    /// Making it taller does the reverse: rows come back out of scrollback onto the top, pushing
    /// the existing screen down, so widening a window that has been shrunk restores what was
    /// there. The cursor rides along with its own line in both directions.
    ///
    /// Found by libvterm's 63screen_resize script.
    /// </summary>
    private void ResizeHeightThroughHistory(int newHeight, ref int cursorRow)
    {
        if (newHeight == Height)
        {
            return;
        }

        if (newHeight < Height)
        {
            // SHORTER. Scroll only as much as actually has to scroll.
            //
            // Rows being cut off the bottom that are BLANK cost nothing to lose, so a screen with
            // ten lines of output shrinking from 25 rows to 20 just truncates and nothing moves.
            // Scrolling regardless would push the top of the output into history for no reason and
            // drag the cursor up with it.
            //
            // Two things force a scroll: real content below the new last row, and the cursor
            // itself, which must stay on screen.
            int lastContentRow = -1;
            for (int row = Height - 1; row >= 0; row--)
            {
                if (!IsRowBlank(row))
                {
                    lastContentRow = row;
                    break;
                }
            }

            int pushed = lastContentRow + 1 - newHeight;
            int keepCursorVisible = cursorRow - newHeight + 1;
            if (keepCursorVisible > pushed) pushed = keepCursorVisible;
            if (pushed < 0) pushed = 0;
            if (pushed > Height - newHeight) pushed = Height - newHeight;

            for (int row = 0; row < pushed; row++)
            {
                PushToScrollback(row);
            }

            var shrunk = new TerminalCell[newHeight, Width];
            var shrunkWrapped = new bool[newHeight];

            for (int row = 0; row < newHeight; row++)
            {
                for (int col = 0; col < Width; col++)
                {
                    shrunk[row, col] = _screen[row + pushed, col];
                }
                shrunkWrapped[row] = _lineWrapped[row + pushed];
            }

            _screen = shrunk;
            _lineWrapped = shrunkWrapped;

            // The cursor moved up with its line. Clamped at 0 — a cursor already near the top has
            // nowhere to go, and a negative row is how this crashed elsewhere.
            cursorRow -= pushed;
            if (cursorRow < 0) cursorRow = 0;
        }
        else
        {
            // TALLER. Pull rows back out of history onto the top, as many as there are.
            //
            // A history line is stored at the width it was written at, which can be WIDER than the
            // screen is now - narrow the window, then make it taller, and the lines coming back are
            // longer than the rows they land in. Copying the first Width columns and dropping the
            // rest silently deletes text the user can still see in their scrollback, so a long line
            // is laid out over as many rows as it needs, carrying the wrap flag that says so.
            //
            // A line only comes back if ALL of its rows fit. Half a line on screen and the other
            // half still in history is a state the ring cannot represent, and it would lose the
            // remainder the next time the line was pushed back.
            int room = newHeight - Height;
            var pulled = new List<TerminalCell[]>(room);
            var pulledWrapped = new List<bool>(room);
            int popped = 0;
            int rowsUsed = 0;

            while (popped < _scrollbackCount)
            {
                // Newest first off the end of the ring, so the newest line ends up lowest.
                int index = _scrollbackCount - popped - 1;
                var line = GetScrollbackLine(index);
                bool lineWrapped = IsScrollbackLineWrapped(index);
                int length = line == null ? 0 : line.Length;

                int needed = length <= Width ? 1 : (length + Width - 1) / Width;
                if (rowsUsed + needed > room)
                    break;

                for (int r = needed - 1; r >= 0; r--)
                {
                    var rowCells = new TerminalCell[Width];
                    for (int col = 0; col < Width; col++)
                    {
                        int source = r * Width + col;
                        rowCells[col] = line != null && source < length ? line[source] : TerminalCell.Empty;
                    }

                    // Every row of a split line continues onto the next; only its last row carries
                    // the flag the line itself was stored with.
                    pulled.Insert(0, rowCells);
                    pulledWrapped.Insert(0, r < needed - 1 ? true : lineWrapped);
                }

                rowsUsed += needed;
                popped++;
            }

            var grown = new TerminalCell[newHeight, Width];
            var grownWrapped = new bool[newHeight];

            for (int i = 0; i < rowsUsed; i++)
            {
                var rowCells = pulled[i];
                for (int col = 0; col < Width; col++)
                {
                    grown[i, col] = rowCells[col];
                }
                grownWrapped[i] = pulledWrapped[i];
            }

            for (int row = 0; row < Height; row++)
            {
                for (int col = 0; col < Width; col++)
                {
                    grown[row + rowsUsed, col] = _screen[row, col];
                }
                grownWrapped[row + rowsUsed] = _lineWrapped[row];
            }

            for (int row = Height + rowsUsed; row < newHeight; row++)
            {
                for (int col = 0; col < Width; col++)
                {
                    grown[row, col] = TerminalCell.Empty;
                }
            }

            _screen = grown;
            _lineWrapped = grownWrapped;
            _scrollbackCount -= popped;     // those lines are back on the screen, not in history

            cursorRow += rowsUsed;
            if (cursorRow > newHeight - 1) cursorRow = newHeight - 1;
        }

        if (_alternateScreen != null)
        {
            _alternateScreen = ResizeGrid(_alternateScreen, Width, newHeight);
            _alternateLineWrapped = ResizeFlags(_alternateLineWrapped, newHeight);
        }

        Height = newHeight;
    }

    /// <summary>
    /// True when no cell of a screen row was ever written to.
    /// </summary>
    private bool IsRowBlank(int row)
    {
        for (int col = 0; col < Width; col++)
        {
            if (_screen[row, col].Codepoint != 0)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// True when a built line was never written to.
    ///
    /// Codepoint 0 only — a line of WRITTEN spaces is content, not padding. Treating a typed space
    /// as blank here would let reflow discard the trailing space of a shell prompt.
    /// </summary>
    private static bool IsBlankLine(TerminalCell[] line)
    {
        for (int i = 0; i < line.Length; i++)
        {
            if (line[i].Codepoint != 0)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// How many columns of a row were actually written to, ignoring the untouched tail.
    ///
    /// Again codepoint 0 only. A shell prompt "> " is two written columns and must reflow as two,
    /// so the space after the caret survives being dragged narrower and back.
    /// </summary>
    private int UsedLength(int row)
    {
        for (int col = Width - 1; col >= 0; col--)
        {
            if (_screen[row, col].Codepoint != 0)
            {
                return col + 1;
            }
        }

        return 0;
    }

    /// <summary>
    /// How many cells of a stored line were written to, ignoring the untouched tail. The same rule
    /// as <see cref="UsedLength"/>, for a line that is not on the screen.
    /// </summary>
    private static int UsedLengthOf(TerminalCell[] line)
    {
        for (int col = line.Length - 1; col >= 0; col--)
        {
            if (line[col].Codepoint != 0)
            {
                return col + 1;
            }
        }

        return 0;
    }

    /// <summary>
    /// Pushes one already-built line into the scrollback ring.
    /// </summary>
    private void PushLineToScrollback(TerminalCell[] line, bool lineWrapped)
    {
        if (_maxScrollbackLines <= 0) return;

        int slot;
        if (_scrollbackCount < _maxScrollbackLines)
        {
            slot = (_scrollbackStart + _scrollbackCount) % _maxScrollbackLines;
            _scrollbackCount++;
        }
        else
        {
            slot = _scrollbackStart;
            _scrollbackStart = (_scrollbackStart + 1) % _maxScrollbackLines;
        }

        var stored = _scrollbackRing[slot];
        if (stored == null || stored.Length != line.Length)
        {
            stored = new TerminalCell[line.Length];
            _scrollbackRing[slot] = stored;
        }

        Array.Copy(line, stored, line.Length);
        _scrollbackWrapped[slot] = lineWrapped;
    }

    /// <summary>
    /// Copies a per-line flag array into a new one of the given height, keeping the flags of the
    /// lines that survive the resize.
    /// </summary>
    private static bool[] ResizeFlags(bool[]? source, int newHeight)
    {
        var result = new bool[newHeight];
        if (source != null)
        {
            Array.Copy(source, result, Math.Min(source.Length, newHeight));
        }
        return result;
    }

    /// <summary>
    /// Copies a screen grid into a new one of the given size, preserving the overlapping
    /// top-left region and filling the rest with empty cells.
    /// </summary>
    private static TerminalCell[,] ResizeGrid(TerminalCell[,] source, int newWidth, int newHeight)
    {
        var result = new TerminalCell[newHeight, newWidth];

        var oldHeight = source.GetLength(0);
        var oldWidth = source.GetLength(1);
        var copyRows = Math.Min(oldHeight, newHeight);
        var copyCols = Math.Min(oldWidth, newWidth);

        for (var row = 0; row < copyRows; row++)
        {
            for (var col = 0; col < copyCols; col++)
            {
                result[row, col] = source[row, col];
            }

            // Fill remaining columns with empty cells
            for (var col = copyCols; col < newWidth; col++)
            {
                result[row, col] = TerminalCell.Empty;
            }
        }

        // Fill remaining rows with empty cells
        for (var row = copyRows; row < newHeight; row++)
        {
            for (var col = 0; col < newWidth; col++)
            {
                result[row, col] = TerminalCell.Empty;
            }
        }

        return result;
    }

    /// <summary>
    /// Gets a cell from a scrolled viewport. When scrollOffset is 0, returns live buffer cells.
    /// When scrollOffset > 0, maps viewport rows through scrollback history.
    /// </summary>
    /// <param name="viewportRow">
    /// Row in the viewport (0 to Height-1)
    /// </param>
    /// <param name="col">
    /// Column (0 to Width-1)
    /// </param>
    /// <param name="scrollOffset">
    /// Lines scrolled back (0 = live view)
    /// </param>
    /// <param name="cell">
    /// The cell at the mapped position
    /// </param>
    /// <returns>
    /// True if the position maps to valid content
    /// </returns>
    public bool TryGetViewportCell(int viewportRow, int col, int scrollOffset, out TerminalCell cell)
    {
        if (col < 0 || col >= Width || viewportRow < 0 || viewportRow >= Height || scrollOffset < 0)
        {
            cell = default;
            return false;
        }

        if (scrollOffset == 0)
        {
            cell = _screen[viewportRow, col];
            return true;
        }

        // Map viewport position through scrollback
        // totalLine is the index into the combined [scrollback + screen] sequence
        int totalLine = _scrollbackCount - scrollOffset + viewportRow;

        if (totalLine < 0)
        {
            // Before the start of scrollback — no content here
            cell = default;
            return false;
        }

        if (totalLine < _scrollbackCount)
        {
            // Reading from scrollback, through the ring
            var line = _scrollbackRing[(_scrollbackStart + totalLine) % _maxScrollbackLines];
            cell = line != null && col < line.Length ? line[col] : TerminalCell.Empty;
            return true;
        }

        // Reading from live screen buffer
        int screenRow = totalLine - _scrollbackCount;
        if (screenRow < Height)
        {
            cell = _screen[screenRow, col];
            return true;
        }

        cell = default;
        return false;
    }

    /// <summary>
    /// Gets a snapshot of the entire screen as a 2D array
    /// </summary>
    public TerminalCell[,] GetSnapshot()
    {
        var snapshot = new TerminalCell[Height, Width];
        Array.Copy(_screen, snapshot, _screen.Length);
        return snapshot;
    }

    /// <summary>
    /// Switches to the alternate screen buffer (DECSET 47, 1047, 1049)
    /// </summary>
    /// <param name="clearOnSwitch">
    /// Whether to clear the alternate buffer when switching to it
    /// </param>
    public void SwitchToAlternateBuffer(bool clearOnSwitch = true)
    {
        if (_usingAlternateBuffer)
            return; // Already using alternate buffer

        // Create alternate buffer if it doesn't exist or size has changed
        if (_alternateScreen == null || _alternateScreen.GetLength(0) != Height || _alternateScreen.GetLength(1) != Width)
        {
            _alternateScreen = new TerminalCell[Height, Width];
        }

        if (_alternateLineWrapped == null || _alternateLineWrapped.Length != Height)
        {
            _alternateLineWrapped = new bool[Height];
        }

        // Clear alternate buffer if requested
        if (clearOnSwitch)
        {
            for (int row = 0; row < Height; row++)
            {
                for (int col = 0; col < Width; col++)
                {
                    _alternateScreen[row, col] = TerminalCell.Empty;
                }
            }
        }

        // Swap buffers, and the per-line metadata with them
        (_screen, _alternateScreen) = (_alternateScreen, _screen);
        (_lineWrapped, _alternateLineWrapped) = (_alternateLineWrapped, _lineWrapped);
        _usingAlternateBuffer = true;
    }

    /// <summary>
    /// Switches back to the primary screen buffer (DECRST 47, 1047, 1049)
    /// </summary>
    public void SwitchToPrimaryBuffer()
    {
        if (!_usingAlternateBuffer)
            return; // Already using primary buffer

        // Swap buffers back, and the per-line metadata with them
        (_screen, _alternateScreen) = (_alternateScreen!, _screen);
        (_lineWrapped, _alternateLineWrapped) = (_alternateLineWrapped!, _lineWrapped);
        _usingAlternateBuffer = false;
    }
}

