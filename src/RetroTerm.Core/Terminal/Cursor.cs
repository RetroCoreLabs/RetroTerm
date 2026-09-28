using System;

namespace RetroTerm.Core.Terminal;

/// <summary>
/// Represents the terminal cursor with position, style, and visibility
/// </summary>
public class Cursor
{
    private int _row;
    private int _col;
    private bool _pendingWrap;

    // NOT readonly: a resize changes them in place. See SetDimensions for why the cursor cannot
    // simply be replaced by a new object.
    private int _maxRows;
    private int _maxCols;

    /// <summary>
    /// Gets or sets the cursor row (0-based)
    /// </summary>
    public int Row
    {
        get => _row;
        // Any deliberate move cancels a pending wrap — see PendingWrap.
        set { _row = Math.Clamp(value, 0, _maxRows - 1); _pendingWrap = false; }
    }

    /// <summary>
    /// Gets or sets the cursor column (0-based)
    /// </summary>
    public int Column
    {
        get => _col;
        set { _col = Math.Clamp(value, 0, _maxCols - 1); _pendingWrap = false; }
    }

    /// <summary>
    /// The VT "Last Column Flag" (LCF), also called the pending or deferred wrap.
    ///
    /// A real VT does NOT move the cursor when it prints into the last column. It leaves the
    /// cursor sitting ON that column and arms this flag; the wrap to the next line happens when
    /// the NEXT printable character arrives. That is why <c>ESC[80G</c>, printing one character,
    /// and then asking for the cursor position still answers column 80 — and why a program can
    /// fill the bottom-right cell of the screen without scrolling it.
    ///
    /// We used to wrap eagerly instead, which showed up as 61 disagreements in libvterm's corpus
    /// (see tests\RetroTerm.Tests\Conformance) — the cursor a line too far, and the screen
    /// scrolled one line early in DEC's own vttest movement screens.
    ///
    /// ANY deliberate cursor movement cancels the flag, which is why the Row and Column setters
    /// clear it. Only <see cref="Advance"/> ever sets it.
    /// </summary>
    public bool PendingWrap => _pendingWrap;

    /// <summary>
    /// Gets or sets the cursor style
    /// </summary>
    public CursorStyle Style { get; set; }

    /// <summary>
    /// Gets or sets whether the cursor is visible
    /// </summary>
    public bool Visible { get; set; }

    /// <summary>
    /// Gets or sets whether autowrap is enabled
    /// (when text reaches right edge, wrap to next line)
    /// </summary>
    public bool AutoWrap { get; set; }

    /// <summary>
    /// Gets or sets whether the cursor wraps around at buffer edges
    /// </summary>
    public bool WrapAtMargin { get; set; }

    /// <summary>
    /// Gets or sets whether reverse wraparound is enabled
    /// </summary>
    public bool ReverseWrap { get; set; }

    /// <summary>
    /// Gets whether the cursor is at the last column
    /// </summary>
    public bool AtLastColumn => _col >= _maxCols - 1;

    /// <summary>
    /// Gets whether the cursor is at the first column
    /// </summary>
    public bool AtFirstColumn => _col == 0;

    /// <summary>
    /// Gets whether the cursor is at the last row
    /// </summary>
    public bool AtLastRow => _row >= _maxRows - 1;

    /// <summary>
    /// Gets whether the cursor is at the first row
    /// </summary>
    public bool AtFirstRow => _row == 0;

    /// <summary>
    /// Saved cursor state for DECSC/DECRC commands
    /// </summary>
    private (int Row, int Col, CursorStyle Style) _savedState;

    /// <summary>
    /// Creates a new cursor with the specified dimensions
    /// </summary>
    public Cursor(int maxRows, int maxCols)
    {
        if (maxRows <= 0) throw new ArgumentException("Max rows must be positive", nameof(maxRows));
        if (maxCols <= 0) throw new ArgumentException("Max cols must be positive", nameof(maxCols));

        _maxRows = maxRows;
        _maxCols = maxCols;
        _row = 0;
        _col = 0;
        Style = CursorStyle.Block;
        Visible = true;
        AutoWrap = true;
        WrapAtMargin = false;
        ReverseWrap = false;
        _savedState = (0, 0, CursorStyle.Block);
    }

    /// <summary>
    /// Moves the cursor to the specified position
    /// </summary>
    public void MoveTo(int row, int col)
    {
        Row = row;
        Column = col;
    }

    /// <summary>
    /// Moves the cursor to home position (0, 0)
    /// </summary>
    public void Home()
    {
        Row = 0;
        Column = 0;
    }

    /// <summary>
    /// Moves the cursor up by the specified number of rows
    /// </summary>
    public void MoveUp(int count = 1)
    {
        Row = Math.Max(0, _row - count);
    }

    /// <summary>
    /// Moves the cursor down by the specified number of rows
    /// </summary>
    public void MoveDown(int count = 1)
    {
        Row = Math.Min(_maxRows - 1, _row + count);
    }

    /// <summary>
    /// Moves the cursor forward (right) by the specified number of columns
    /// </summary>
    public void MoveForward(int count = 1)
    {
        Column = Math.Min(_maxCols - 1, _col + count);
    }

    /// <summary>
    /// Moves the cursor backward (left) by the specified number of columns
    /// </summary>
    public void MoveBackward(int count = 1)
    {
        Column = Math.Max(0, _col - count);
    }

    /// <summary>
    /// Moves the cursor to the beginning of the line
    /// </summary>
    public void CarriageReturn()
    {
        Column = 0;
    }

    /// <summary>
    /// Advances to the next line (moves down and optionally returns to column 0)
    /// </summary>
    public bool LineFeed(bool returnToStart = false)
    {
        var needsScroll = false;
        _pendingWrap = false;       // moving to a new line cancels a pending wrap

        if (_row >= _maxRows - 1)
        {
            // At bottom, need to scroll
            needsScroll = true;
        }
        else
        {
            // Move down
            _row++;
        }

        if (returnToStart)
        {
            CarriageReturn();
        }

        return needsScroll;
    }

    /// <summary>
    /// Advances the cursor forward, handling wraparound
    /// </summary>
    /// <returns>
    /// Always false. Kept as bool because callers written against the old eager-wrap behaviour
    /// used it to mean "a line feed just happened"; with the Last Column Flag nothing feeds here,
    /// so the answer is now always no. <see cref="ResolvePendingWrap"/> is where the feed happens.
    /// </returns>
    public bool Advance() => AdvanceWithin(_maxCols - 1);

    /// <summary>
    /// Advances, treating <paramref name="lastColumn"/> as the last usable column rather than the
    /// physical edge of the screen.
    ///
    /// This exists for DOUBLE-WIDTH LINES. A line marked DECDWL or DECDHL draws each character at
    /// twice the width, so an 80-column screen holds only 40 of them and the line has to wrap at
    /// column 39. The cursor has no idea which lines are double width — that lives in the buffer's
    /// cell attributes — so the emulator works the limit out and passes it in.
    /// </summary>
    public bool AdvanceWithin(int lastColumn)
    {
        if (lastColumn > _maxCols - 1) lastColumn = _maxCols - 1;
        if (lastColumn < 0) lastColumn = 0;

        if (_col < lastColumn)
        {
            _col++;
            _pendingWrap = false;
            return false;
        }

        // At the last usable column. A real VT does NOT move — it arms the Last Column Flag and
        // waits to see whether another printable character actually arrives. See PendingWrap.
        if (AutoWrap)
        {
            _pendingWrap = true;
        }

        return false;
    }

    /// <summary>
    /// Performs the wrap that <see cref="Advance"/> deferred: column 0 on the next line.
    /// Call this immediately BEFORE printing a character, and only when
    /// <see cref="PendingWrap"/> is set.
    /// </summary>
    /// <returns>
    /// True if the line feed ran off the bottom and the caller must scroll.
    /// </returns>
    public bool ResolvePendingWrap() => ResolvePendingWrapTo(0);

    /// <summary>
    /// Performs the deferred wrap, landing on <paramref name="leftColumn"/> rather than column 0.
    /// </summary>
    /// <remarks>
    /// For LEFT AND RIGHT MARGINS. Inside a margined region text wraps from the right margin to
    /// the LEFT MARGIN, not to the edge of the screen - that is the whole point of the feature, and
    /// wrapping to column 0 would spill every wrapped line out of the region it belongs to.
    /// </remarks>
    /// <param name="leftColumn">
    /// The column a wrap lands on.
    /// </param>
    /// <returns>
    /// True if the line feed ran off the bottom and the caller must scroll.
    /// </returns>
    public bool ResolvePendingWrapTo(int leftColumn)
    {
        _pendingWrap = false;
        _col = Math.Clamp(leftColumn, 0, _maxCols - 1);
        return LineFeed();
    }

    /// <summary>
    /// Drops a pending wrap without performing it — used when autowrap is turned off while the
    /// flag is armed, in which case the next character overwrites the last column instead.
    /// </summary>
    public void ClearPendingWrap() => _pendingWrap = false;

    /// <summary>
    /// Moves the cursor to a column WITHOUT cancelling a pending wrap.
    /// </summary>
    /// <param name="column">
    /// The column to land on, clamped to the screen the same way the setter clamps.
    /// </param>
    /// <remarks>
    /// The one movement that is known not to cancel the flag is tabulation - see
    /// TerminalEmulatorBase.TabulateToColumn for the fixture that proves it. Everything else must
    /// keep using the ordinary Column setter, which clears the flag.
    /// </remarks>
    public void SetColumnKeepingPendingWrap(int column)
    {
        _col = Math.Clamp(column, 0, _maxCols - 1);
    }

    /// <summary>
    /// Moves the cursor backward, handling reverse wraparound
    /// </summary>
    public void Reverse()
    {
        _pendingWrap = false;       // a backspace out of the last column cancels the pending wrap
        if (_col > 0)
        {
            _col--;
        }
        else if (ReverseWrap && _row > 0)
        {
            _row--;
            _col = _maxCols - 1;
        }
    }

    /// <summary>
    /// Saves the current cursor state (DECSC)
    /// </summary>
    public void Save()
    {
        _savedState = (_row, _col, Style);
    }

    /// <summary>
    /// Throws away what DECSC saved, so a later DECRC lands on the home position.
    /// </summary>
    /// <remarks>
    /// DECSTR's table gives the saved cursor's state after a soft reset as "Home position with
    /// VT420 defaults", and that is what an empty slot already means.
    /// </remarks>
    public void ResetSaved()
    {
        _savedState = (0, 0, Style);
    }

    /// <summary>
    /// Restores the saved cursor state (DECRC)
    /// </summary>
    public void Restore()
    {
        _row = Math.Clamp(_savedState.Row, 0, _maxRows - 1);
        _col = Math.Clamp(_savedState.Col, 0, _maxCols - 1);
        _pendingWrap = false;       // DECRC restores a position, not a half-finished wrap
        Style = _savedState.Style;
    }

    /// <summary>
    /// What DECSC last saved, so an emulator can put it aside and bring it back.
    /// </summary>
    /// <remarks>
    /// The main screen and the alternate screen each have their OWN saved cursor, which is not
    /// something the cursor can know about - it has one slot and the emulator swaps what is in it
    /// when the screen changes. Without that, a program that saved on the main screen, switched,
    /// saved again and restored twice got the same position both times: the alternate screen's
    /// save had overwritten the main screen's.
    /// </remarks>
    public (int Row, int Col, CursorStyle Style) SavedPosition
    {
        get => _savedState;
        set => _savedState = value;
    }

    /// <summary>
    /// Resets the cursor to its default state
    /// </summary>
    public void Reset()
    {
        _row = 0;
        _col = 0;
        _pendingWrap = false;       // RIS must not leave a wrap armed from before the reset
        Style = CursorStyle.Block;
        Visible = true;
        AutoWrap = true;
        WrapAtMargin = false;
        ReverseWrap = false;
        // Also reset saved state for RIS
        _savedState = (0, 0, CursorStyle.Block);
    }

    /// <summary>
    /// Re-bounds this cursor to a new screen size, clamping the position into it.
    ///
    /// WHY THIS EXISTS RATHER THAN <see cref="WithNewDimensions"/>. Everything that holds a
    /// cursor holds the same object — the emulator, the renderer, saved-state handling — so a
    /// resize cannot swap in a new one. <c>TerminalEmulatorBase.Resize</c> used to build a
    /// correctly-bounded cursor with WithNewDimensions and then throw it away, copying back only
    /// Row and Column. The live cursor kept the OLD _maxRows/_maxCols, so after a shrink it never
    /// wrapped at the new right margin: the column walked past the buffer and the next character
    /// threw ArgumentOutOfRangeException out of TerminalBuffer's indexer. Found by libvterm's
    /// 16state_resize corpus (see tests\RetroTerm.Tests\Conformance).
    /// </summary>
    public void SetDimensions(int newMaxRows, int newMaxCols)
    {
        if (newMaxRows <= 0) throw new ArgumentOutOfRangeException(nameof(newMaxRows));
        if (newMaxCols <= 0) throw new ArgumentOutOfRangeException(nameof(newMaxCols));

        _maxRows = newMaxRows;
        _maxCols = newMaxCols;

        // Clamp AFTER the bounds change, so the setters use the new limits.
        Row = _row;
        Column = _col;
    }

    public Cursor WithNewDimensions(int newMaxRows, int newMaxCols)
    {
        var newCursor = new Cursor(newMaxRows, newMaxCols)
        {
            Style = Style,
            Visible = Visible,
            AutoWrap = AutoWrap,
            WrapAtMargin = WrapAtMargin,
            ReverseWrap = ReverseWrap
        };

        newCursor.Row = Math.Min(_row, newMaxRows - 1);
        newCursor.Column = Math.Min(_col, newMaxCols - 1);

        return newCursor;
    }

    public override string ToString()
    {
        return $"Cursor({Row},{Column}) Style={Style} Visible={Visible}";
    }
}

