using System;
using System.Collections.Generic;
using RetroTerm.Core.Selection;
using RetroTerm.Core.Terminal.Buffer;
using Xunit;

namespace RetroTerm.Tests.Selection;

/// <summary>
/// TryGetSelectedColumnRange must give exactly the answer GetSelectedCells gives.
///
/// WHY THIS SHAPE. The renderer used to ask GetSelectedCells().ToHashSet() once per frame
/// and test every cell against the set — one tuple per selected cell, a HashSet rebuild,
/// and a buffer rescan per row for the trailing-whitespace trim, sixty times a second.
/// The replacement answers per ROW with integer comparisons and allocates nothing.
///
/// The risk in that change is not performance, it is DRIFT: two implementations of one
/// rule, and the tricky parts of the rule (trailing-whitespace trimming, empty
/// intermediate lines being skipped, first/last rows being exempt) are exactly the parts
/// easy to get subtly wrong. So these tests do not assert my understanding of the rule —
/// they assert that both implementations agree, cell for cell, across a spread of
/// selection shapes. If the rule itself is wrong, it is wrong identically in both, and
/// SelectionManagerTests is where that is pinned.
/// </summary>
public class SelectedColumnRangeTests
{
    private const int Width = 80;
    private const int Height = 24;

    /// <summary>
    /// A buffer with a deliberately awkward mix: full rows, short rows, completely blank
    /// rows, and rows whose content stops mid-line. Blank and short rows are what the
    /// whitespace-trim and skip-intermediate rules turn on.
    /// </summary>
    private static TerminalBuffer BuildBuffer()
    {
        var buffer = new TerminalBuffer(Width, Height);
        for (int row = 0; row < Height; row++)
        {
            int contentWidth = row switch
            {
                3 => 0,           // completely blank
                7 => 0,           // completely blank
                8 => 1,           // one character
                12 => Width,      // full width
                17 => 0,          // completely blank
                _ => 10 + (row * 3) % 40
            };

            for (int col = 0; col < contentWidth; col++)
            {
                var cell = new TerminalCell { Codepoint = (uint)('A' + ((row + col) % 26)) };
                buffer.SetCell(row, col, cell);
            }
        }
        return buffer;
    }

    /// <summary>
    /// Every selection shape worth checking, as (startRow, startCol, endRow, endCol).
    /// </summary>
    public static IEnumerable<object[]> Selections()
    {
        (int, int, int, int)[] cases =
        {
            (0, 0, 0, 5),            // single row, short
            (0, 0, 0, Width - 1),    // single row, full width
            (2, 4, 2, 4),            // single cell
            (1, 10, 5, 20),          // multi-row spanning a blank row (3)
            (6, 0, 9, 40),           // spans two blank rows and the one-character row
            (0, 0, Height - 1, Width - 1), // whole screen
            (3, 0, 3, 40),           // entirely inside a blank row
            (3, 5, 7, 5),            // blank row to blank row
            (11, 70, 13, 2),         // crosses the full-width row
            (16, 0, 18, 79),         // blank row as an intermediate
            (20, 30, 22, 10),        // ordinary multi-row
        };

        for (int i = 0; i < cases.Length; i++)
        {
            var (sr, sc, er, ec) = cases[i];
            yield return new object[] { sr, sc, er, ec };
        }
    }

    [Theory]
    [MemberData(nameof(Selections))]
    public void CharacterSelection_RangeMatchesTheCellSet(int startRow, int startCol, int endRow, int endCol)
    {
        AssertAgreement(SelectionMode.Character, startRow, startCol, endRow, endCol);
    }

    [Theory]
    [MemberData(nameof(Selections))]
    public void RectangularSelection_RangeMatchesTheCellSet(int startRow, int startCol, int endRow, int endCol)
    {
        AssertAgreement(SelectionMode.Rectangular, startRow, startCol, endRow, endCol);
    }

    /// <summary>
    /// A selection dragged BACKWARDS (end before start) must behave the same as the
    /// forward drag — both implementations normalise, and they must normalise alike.
    /// </summary>
    [Theory]
    [MemberData(nameof(Selections))]
    public void BackwardsDrag_RangeMatchesTheCellSet(int startRow, int startCol, int endRow, int endCol)
    {
        AssertAgreement(SelectionMode.Character, endRow, endCol, startRow, startCol);
    }

    [Fact]
    public void NoSelection_ReportsNoRangeOnAnyRow()
    {
        var buffer = BuildBuffer();
        var manager = new SelectionManager(buffer);

        for (int row = 0; row < Height; row++)
        {
            Assert.False(manager.TryGetSelectedColumnRange(row, out var start, out var end),
                $"row {row} reported a selection when nothing is selected");
            Assert.Equal(0, start);
            Assert.Equal(0, end);
        }
    }

    [Fact]
    public void RowsOutsideTheSelection_ReportNoRange()
    {
        var buffer = BuildBuffer();
        var manager = new SelectionManager(buffer);
        manager.StartSelection(10, 5);
        manager.ExtendSelection(12, 20);

        Assert.False(manager.TryGetSelectedColumnRange(9, out _, out _));
        Assert.False(manager.TryGetSelectedColumnRange(13, out _, out _));
        Assert.True(manager.TryGetSelectedColumnRange(11, out _, out _));
    }

    /// <summary>
    /// Rows well outside the buffer must be answered, not throw — the renderer asks about
    /// whatever rows the frame has, and a resize between frames is ordinary.
    /// </summary>
    [Fact]
    public void RowsOutsideTheBuffer_AreAnsweredNotThrown()
    {
        var buffer = BuildBuffer();
        var manager = new SelectionManager(buffer);
        manager.SelectAll();

        Assert.False(manager.TryGetSelectedColumnRange(-1, out _, out _));
        Assert.False(manager.TryGetSelectedColumnRange(Height + 100, out _, out _));
    }

    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// The agreement check: build the authoritative cell set from GetSelectedCells, then
    /// walk every cell of the buffer and demand the range query says the same thing.
    /// Walking EVERY cell, not just the selected ones, is deliberate — a range that is too
    /// WIDE is as wrong as one that is too narrow, and only the full sweep catches it.
    /// </summary>
    private static void AssertAgreement(SelectionMode mode, int startRow, int startCol, int endRow, int endCol)
    {
        var buffer = BuildBuffer();
        var manager = new SelectionManager(buffer);
        manager.StartSelection(startRow, startCol, mode);
        manager.ExtendSelection(endRow, endCol);

        var expected = new HashSet<(int Row, int Col)>();
        var cells = manager.GetSelectedCells();
        var enumerator = cells.GetEnumerator();
        while (enumerator.MoveNext())
        {
            expected.Add(enumerator.Current);
        }

        for (int row = 0; row < Height; row++)
        {
            bool hasRange = manager.TryGetSelectedColumnRange(row, out int rangeStart, out int rangeEnd);

            for (int col = 0; col < Width; col++)
            {
                bool inSet = expected.Contains((row, col));
                bool inRange = hasRange && col >= rangeStart && col <= rangeEnd;

                Assert.True(inSet == inRange,
                    $"{mode} ({startRow},{startCol})-({endRow},{endCol}) disagree at row {row} col {col}: "
                    + $"GetSelectedCells says {inSet}, TryGetSelectedColumnRange says {inRange} "
                    + $"(range {(hasRange ? $"{rangeStart}..{rangeEnd}" : "none")})");
            }
        }
    }
}
