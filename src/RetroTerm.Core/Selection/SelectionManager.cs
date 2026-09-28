using System;
using System.Collections.Generic;
using System.Linq;
using RetroTerm.Core.Terminal.Buffer;

namespace RetroTerm.Core.Selection;

/// <summary>
/// Selection mode for text selection
/// </summary>
public enum SelectionMode
{
    /// <summary>
    /// Character-by-character selection
    /// </summary>
    Character,

    /// <summary>
    /// Word selection (double-click)
    /// </summary>
    Word,

    /// <summary>
    /// Line selection (triple-click)
    /// </summary>
    Line,

    /// <summary>
    /// Rectangular selection (Alt+drag)
    /// </summary>
    Rectangular
}

/// <summary>
/// Represents a text selection in the terminal buffer
/// </summary>
public class TextSelection
{
    /// <summary>
    /// Start position (row, column)
    /// </summary>
    public (int Row, int Col) Start { get; set; }

    /// <summary>
    /// End position (row, column)
    /// </summary>
    public (int Row, int Col) End { get; set; }

    /// <summary>
    /// Selection mode
    /// </summary>
    public SelectionMode Mode { get; set; }

    /// <summary>
    /// Gets whether this selection is empty
    /// </summary>
    public bool IsEmpty => Start == End;

    /// <summary>
    /// Gets the normalized start and end positions, ordered so the start is never after the end
    /// </summary>
    public ((int Row, int Col) Start, (int Row, int Col) End) Normalized
    {
        get
        {
            var start = Start;
            var end = End;

            // Normalize: ensure start <= end
            if (start.Row > end.Row || (start.Row == end.Row && start.Col > end.Col))
            {
                (start, end) = (end, start);
            }

            return (start, end);
        }
    }
}

/// <summary>
/// Manages text selection in the terminal buffer
/// </summary>
public class SelectionManager
{
    private TextSelection? _currentSelection;
    private readonly TerminalBuffer _buffer;

    public SelectionManager(TerminalBuffer buffer)
    {
        _buffer = buffer ?? throw new ArgumentNullException(nameof(buffer));
    }

    /// <summary>
    /// Gets the current selection, or null if no selection
    /// </summary>
    public TextSelection? CurrentSelection => _currentSelection;

    /// <summary>
    /// Gets whether there is an active selection
    /// </summary>
    public bool HasSelection => _currentSelection != null && !_currentSelection.IsEmpty;

    /// <summary>
    /// Starts a new selection at the specified position
    /// </summary>
    public void StartSelection(int row, int col, SelectionMode mode = SelectionMode.Character)
    {
        _currentSelection = new TextSelection
        {
            Start = (row, col),
            End = (row, col),
            Mode = mode
        };
    }

    /// <summary>
    /// Extends the current selection to the specified position
    /// </summary>
    public void ExtendSelection(int row, int col)
    {
        if (_currentSelection == null)
        {
            StartSelection(row, col);
            return;
        }

        _currentSelection.End = (row, col);

        // Adjust selection based on mode
        AdjustSelectionForMode();
    }

    /// <summary>
    /// Selects all buffer content (row 0 col 0 to last row last col)
    /// </summary>
    public void SelectAll()
    {
        StartSelection(0, 0, SelectionMode.Character);
        ExtendSelection(_buffer.Height - 1, _buffer.Width - 1);
    }

    /// <summary>
    /// Clears the current selection
    /// </summary>
    public void ClearSelection()
    {
        _currentSelection = null;
    }

    /// <summary>
    /// Gets the selected text from the buffer
    /// </summary>
    public string GetSelectedText()
    {
        if (!HasSelection || _currentSelection == null)
            return string.Empty;

        var (start, end) = _currentSelection.Normalized;

        return _currentSelection.Mode switch
        {
            SelectionMode.Rectangular => GetRectangularSelection(start, end),
            SelectionMode.Word => GetWordSelection(start, end),
            SelectionMode.Line => GetLineSelection(start, end),
            _ => GetCharacterSelection(start, end)
        };
    }

    /// <summary>
    /// Gets all selected cell positions
    /// </summary>
    public IEnumerable<(int Row, int Col)> GetSelectedCells()
    {
        if (!HasSelection || _currentSelection == null)
            yield break;

        var (start, end) = _currentSelection.Normalized;

        if (_currentSelection.Mode == SelectionMode.Rectangular)
        {
            // Rectangular selection: select columns from start.Col to end.Col for each row
            // For rectangular selection, we keep all columns (including whitespace) as that's the nature of rectangular selection
            var minCol = Math.Min(start.Col, end.Col);
            var maxCol = Math.Max(start.Col, end.Col);
            var minRow = Math.Min(start.Row, end.Row);
            var maxRow = Math.Max(start.Row, end.Row);

            for (int row = minRow; row <= maxRow; row++)
            {
                for (int col = minCol; col <= maxCol; col++)
                {
                    yield return (row, col);
                }
            }
        }
        else
        {
            // Character/word/line selection: select from start to end, but trim trailing whitespace
            for (int row = start.Row; row <= end.Row; row++)
            {
                // Determine the column range for this row
                int rowStartCol, rowEndCol;

                if (row == start.Row && row == end.Row)
                {
                    // Single line selection
                    rowStartCol = start.Col;
                    rowEndCol = end.Col;
                }
                else if (row == start.Row)
                {
                    // First line of multi-line selection
                    rowStartCol = start.Col;
                    rowEndCol = _buffer.Width - 1; // Full line
                }
                else if (row == end.Row)
                {
                    // Last line of multi-line selection
                    rowStartCol = 0;
                    rowEndCol = end.Col;
                }
                else
                {
                    // Intermediate line
                    rowStartCol = 0;
                    rowEndCol = _buffer.Width - 1; // Full line
                }

                // Find the last non-whitespace column on this row
                var lastNonWhitespaceCol = FindLastNonWhitespaceColumn(row);

                // Determine effective end column
                int effectiveEndCol;
                if (lastNonWhitespaceCol >= 0)
                {
                    // There's content on this row - trim trailing whitespace
                    effectiveEndCol = Math.Min(rowEndCol, lastNonWhitespaceCol);
                }
                else
                {
                    // No content on this row - but if selection explicitly includes this row, include it
                    // Only skip if this is an intermediate line with no content
                    if (row == start.Row || row == end.Row)
                    {
                        // First or last line - include the selection range even if it's all whitespace
                        effectiveEndCol = rowEndCol;
                    }
                    else
                    {
                        // Intermediate line with no content - skip it
                        continue;
                    }
                }

                // Only include cells from rowStartCol to effectiveEndCol
                for (int col = rowStartCol; col <= effectiveEndCol; col++)
                {
                    yield return (row, col);
                }
            }
        }
    }

    /// <summary>
    /// The selected column range on ONE row, without materialising anything.
    ///
    /// WHY THIS EXISTS. The renderer used to call <see cref="GetSelectedCells"/> and
    /// ToHashSet() once per frame, then test every cell against that set. A full-screen
    /// selection therefore yielded one tuple per cell (1,920 on an 80x24 screen), boxed
    /// them into a HashSet, and rescanned the buffer for trailing whitespace on every row
    /// — sixty times a second, to answer a question that is a pair of integer
    /// comparisons. It also put LINQ in the render hot path, which this project forbids.
    ///
    /// This returns the same answer per ROW, so a renderer calls it Height times per
    /// frame instead of allocating per cell. The rules are identical to
    /// <see cref="GetSelectedCells"/> — including the trailing-whitespace trim and the
    /// skipping of empty intermediate lines — because both read the same selection.
    /// </summary>
    /// <param name="row">
    /// Buffer row to query.
    /// </param>
    /// <param name="startColumn">
    /// First selected column on this row, inclusive.
    /// </param>
    /// <param name="endColumn">
    /// Last selected column on this row, inclusive.
    /// </param>
    /// <returns>
    /// False when nothing on this row is selected; the out values are then 0.
    /// </returns>
    public bool TryGetSelectedColumnRange(int row, out int startColumn, out int endColumn)
    {
        startColumn = 0;
        endColumn = 0;

        if (!HasSelection || _currentSelection == null)
        {
            return false;
        }

        var (start, end) = _currentSelection.Normalized;

        if (_currentSelection.Mode == SelectionMode.Rectangular)
        {
            int minRow = Math.Min(start.Row, end.Row);
            int maxRow = Math.Max(start.Row, end.Row);
            if (row < minRow || row > maxRow)
            {
                return false;
            }
            startColumn = Math.Min(start.Col, end.Col);
            endColumn = Math.Max(start.Col, end.Col);
            return true;
        }

        if (row < start.Row || row > end.Row)
        {
            return false;
        }

        // Same row-range cases as GetSelectedCells, in the same order.
        int rowStartCol, rowEndCol;
        if (row == start.Row && row == end.Row)
        {
            rowStartCol = start.Col;
            rowEndCol = end.Col;
        }
        else if (row == start.Row)
        {
            rowStartCol = start.Col;
            rowEndCol = _buffer.Width - 1;
        }
        else if (row == end.Row)
        {
            rowStartCol = 0;
            rowEndCol = end.Col;
        }
        else
        {
            rowStartCol = 0;
            rowEndCol = _buffer.Width - 1;
        }

        // Trailing whitespace is trimmed so a dragged selection does not highlight the
        // empty right-hand side of a short line — except on the first and last rows,
        // where the user's own drag defined the range explicitly.
        int lastNonWhitespaceCol = FindLastNonWhitespaceColumn(row);
        int effectiveEndCol;
        if (lastNonWhitespaceCol >= 0)
        {
            effectiveEndCol = Math.Min(rowEndCol, lastNonWhitespaceCol);
        }
        else if (row == start.Row || row == end.Row)
        {
            effectiveEndCol = rowEndCol;
        }
        else
        {
            return false; // empty intermediate line — skipped, exactly as GetSelectedCells does
        }

        if (effectiveEndCol < rowStartCol)
        {
            return false;
        }

        startColumn = rowStartCol;
        endColumn = effectiveEndCol;
        return true;
    }

    /// <summary>
    /// Reads one cell, from the screen or from the history above it.
    /// </summary>
    /// <param name="row">
    /// Row 0 is the top of the live screen; minus one is the line immediately above it, which is
    /// how a match is numbered by the search and how the canvas reports a click while the view is
    /// scrolled back.
    /// </param>
    /// <param name="col">
    /// Column, 0-based.
    /// </param>
    /// <param name="cell">
    /// The cell, or a blank when the position does not exist.
    /// </param>
    /// <returns>
    /// False when the row or column is outside everything this buffer holds.
    /// </returns>
    /// <remarks>
    /// EVERY read in this class goes through here, and that is the point. They all used to call the
    /// screen grid directly, which silently answered for the LIVE screen no matter which line the
    /// user had actually pointed at - so a drag made while scrolled back highlighted one line and
    /// copied another. One accessor is what keeps the highlight and the copy telling the same story.
    /// </remarks>
    private bool TryGetCellAt(int row, int col, out TerminalCell cell)
    {
        if (row >= 0)
        {
            return _buffer.TryGetCell(row, col, out cell);
        }

        cell = TerminalCell.Empty;

        // A history line is stored at the width it was written at, so its own length decides
        // what is there - the screen may since have been made narrower or wider.
        int index = _buffer.ScrollbackLineCount + row;
        if (index < 0 || col < 0)
        {
            return false;
        }

        var line = _buffer.GetScrollbackLine(index);
        if (line == null || col >= line.Length)
        {
            return false;
        }

        cell = line[col];
        return true;
    }

    /// <summary>
    /// Finds the last non-whitespace column in a row
    /// </summary>
    private int FindLastNonWhitespaceColumn(int row)
    {
        // Start from the rightmost column and work backwards
        for (int col = _buffer.Width - 1; col >= 0; col--)
        {
            if (TryGetCellAt(row, col, out var cell))
            {
                var ch = (char)cell.Codepoint;
                if (!char.IsWhiteSpace(ch) && ch != 0)
                {
                    return col;
                }
            }
        }

        // If all cells are whitespace, return -1 (no content)
        return -1;
    }

    private void AdjustSelectionForMode()
    {
        if (_currentSelection == null) return;

        switch (_currentSelection.Mode)
        {
            case SelectionMode.Word:
                AdjustWordSelection();
                break;
            case SelectionMode.Line:
                AdjustLineSelection();
                break;
        }
    }

    private void AdjustWordSelection()
    {
        if (_currentSelection == null) return;

        var (start, end) = _currentSelection.Normalized;

        // Expand start to word start
        var wordStart = FindWordStart(start.Row, start.Col);
        // Expand end to word end
        var wordEnd = FindWordEnd(end.Row, end.Col);

        _currentSelection.Start = wordStart;
        _currentSelection.End = wordEnd;
    }

    private void AdjustLineSelection()
    {
        if (_currentSelection == null) return;

        var (start, end) = _currentSelection.Normalized;

        // Expand to full lines
        _currentSelection.Start = (start.Row, 0);
        _currentSelection.End = (end.Row, _buffer.Width - 1);
    }

    private (int Row, int Col) FindWordStart(int row, int col)
    {
        // Move backward until we hit a word boundary
        while (col > 0)
        {
            if (!TryGetCellAt(row, col - 1, out var prevCell))
                break;

            // The cell under the cursor comes through the same accessor: the indexer that used to
            // be here reads the SCREEN grid and throws outright on a history row.
            if (!TryGetCellAt(row, col, out var here))
                break;

            if (IsWordBoundary(prevCell, here))
                break;

            col--;
        }

        return (row, col);
    }

    private (int Row, int Col) FindWordEnd(int row, int col)
    {
        // Move forward until we hit a word boundary
        while (col < _buffer.Width - 1)
        {
            if (!TryGetCellAt(row, col + 1, out var nextCell))
                break;

            if (!TryGetCellAt(row, col, out var here))
                break;

            if (IsWordBoundary(here, nextCell))
                break;

            col++;
        }

        return (row, col);
    }

    private bool IsWordBoundary(TerminalCell cell1, TerminalCell cell2)
    {
        var ch1 = (char)cell1.Codepoint;
        var ch2 = (char)cell2.Codepoint;

        // Word boundary: transition between word character and non-word character
        return char.IsLetterOrDigit(ch1) != char.IsLetterOrDigit(ch2);
    }

    /// <summary>
    /// Builds the text of a character-mode selection, row by row.
    /// </summary>
    /// <param name="start">
    /// Top-left end of the normalised selection.
    /// </param>
    /// <param name="end">
    /// Bottom-right end of the normalised selection.
    /// </param>
    /// <returns>
    /// The selected text, with one line break per LOGICAL line.
    /// </returns>
    /// <remarks>
    /// <para><b>Two rules, both of which this method used to get wrong</b></para>
    /// It walked the grid column by column and put a break at every screen row boundary, taking
    /// every cell it passed. That is wrong twice over:
    ///
    ///  - A line longer than the screen is ONE line that the terminal split. The buffer records
    ///    which rows were wrapped, and a break inside one turns a copied long command into two
    ///    commands when it is pasted back into a shell.
    ///  - The blank right-hand end of a short row was copied as spaces, so the text that came out
    ///    was not the text that was highlighted - the highlight has always trimmed it.
    ///
    /// Both are fixed by reading the row range from <see cref="TryGetSelectedColumnRange"/>, which
    /// is the same method the renderer highlights from. That is what this class's own note about
    /// one accessor keeping the highlight and the copy telling the same story was asking for.
    ///
    /// A row that method refuses - an empty line inside the selection - still ends its line here.
    /// The highlight can skip a blank row because there is nothing to paint; copied text cannot,
    /// because the blank line between two paragraphs is part of what was selected.
    /// </remarks>
    private string GetCharacterSelection((int Row, int Col) start, (int Row, int Col) end)
    {
        var result = new System.Text.StringBuilder();
        bool anyRowSeen = false;
        bool previousRowWrapped = false;

        for (int row = start.Row; row <= end.Row; row++)
        {
            if (anyRowSeen && !previousRowWrapped)
            {
                result.Append('\n');
            }

            anyRowSeen = true;
            previousRowWrapped = IsRowWrapped(row);

            if (!TryGetSelectedColumnRange(row, out int fromColumn, out int toColumn))
            {
                continue;   // an empty row: it contributes a line break and nothing else
            }

            for (int col = fromColumn; col <= toColumn; col++)
            {
                if (TryGetCellAt(row, col, out var cell))
                {
                    result.Append(cell.GetString());
                }
            }
        }

        return result.ToString();
    }

    /// <summary>
    /// True when the terminal wrapped this row into the next one, so the two are one logical line.
    /// </summary>
    /// <param name="row">
    /// Row 0 is the top of the live screen; minus one is the line above it.
    /// </param>
    /// <returns>
    /// False for a row that ended because the host sent a line break, and for a row that does not
    /// exist.
    /// </returns>
    /// <remarks>
    /// The screen and the history keep their wrap flags in separate arrays, so both have to be
    /// asked. Forgetting the history one would leave long output - which is exactly the output
    /// people scroll back to copy - still breaking in the middle.
    /// </remarks>
    private bool IsRowWrapped(int row)
    {
        if (row >= 0)
        {
            return _buffer.IsLineWrapped(row);
        }

        int index = _buffer.ScrollbackLineCount + row;
        return index >= 0 && _buffer.IsScrollbackLineWrapped(index);
    }

    private string GetWordSelection((int Row, int Col) start, (int Row, int Col) end)
    {
        var wordStart = FindWordStart(start.Row, start.Col);
        var wordEnd = FindWordEnd(end.Row, end.Col);
        return GetCharacterSelection(wordStart, wordEnd);
    }

    private string GetLineSelection((int Row, int Col) start, (int Row, int Col) end)
    {
        var result = new System.Text.StringBuilder();

        for (int row = start.Row; row <= end.Row; row++)
        {
            for (int col = 0; col < _buffer.Width; col++)
            {
                if (TryGetCellAt(row, col, out var cell))
                {
                    result.Append(cell.GetString());
                }
            }

            // Same rule as the character mode above: a wrapped row runs straight into the next
            // one, so triple-clicking a long command gives back the command, not half of it.
            if (row < end.Row && !IsRowWrapped(row))
            {
                result.Append('\n');
            }
        }

        return result.ToString();
    }

    private string GetRectangularSelection((int Row, int Col) start, (int Row, int Col) end)
    {
        var result = new System.Text.StringBuilder();
        var minCol = Math.Min(start.Col, end.Col);
        var maxCol = Math.Max(start.Col, end.Col);
        var minRow = Math.Min(start.Row, end.Row);
        var maxRow = Math.Max(start.Row, end.Row);

        for (int row = minRow; row <= maxRow; row++)
        {
            for (int col = minCol; col <= maxCol; col++)
            {
                if (TryGetCellAt(row, col, out var cell))
                {
                    result.Append(cell.GetString());
                }
            }

            if (row < maxRow)
            {
                result.Append('\n');
            }
        }

        return result.ToString();
    }
}


