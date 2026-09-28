using System;
using System.Collections.Generic;
using System.Text;
using RetroTerm.Core.Configuration;
using RetroTerm.Core.Terminal.Buffer;
using RetroTerm.Core.Terminal.Emulators;
using Xunit;

namespace RetroTerm.Tests.Terminal;

/// <summary>
/// The DEC line-size sequences — ESC # 3/4/5/6 — on EVERY terminal, not just the TDV models.
///
/// WHAT WAS WRONG. `HandleDoubleWidthHeight`, `SetLineHeight` and `SetLineWidth` lived in
/// `TDVEmulatorBase`, so a VT100 or ANSI session ignored DECDHL/DECDWL/DECSWL completely —
/// even though `CharacterAttributes` has carried DoubleWidth, DoubleHeightTop and
/// DoubleHeightBottom from the beginning, and the TDV implementation's own comment cited the
/// VT220 spec. Nothing about the behaviour was TDV-specific.
///
/// Worse, there were THREE copies: the TDV base, an override in TDV2200, and an override in
/// TDV1200 that fanned out to four one-line private helpers. They had not diverged, except
/// that TDV2200 cleared DoubleWidth a second time on ESC # 5 — harmless, and exactly the kind
/// of pointless difference that three copies of one behaviour accumulate.
///
/// The other half of this change: double size used to be recorded TWICE per cell — in the
/// packed `_flags` byte AND in the attributes. `TerminalCell.DoubleWidth`/`DoubleHeight` are
/// now derived from the attributes, so the two cannot drift.
/// </summary>
public class LineSizeSequenceTests
{
    /// <summary>
    /// Every emulator the factory builds. New terminals are covered automatically.
    /// </summary>
    public static IEnumerable<object[]> AllEmulators()
    {
        var types = EmulatorFactory.AvailableEmulators;
        for (int i = 0; i < types.Length; i++)
        {
            yield return new object[] { types[i] };
        }
        yield return new object[] { "ANSI" };
    }

    private static TerminalEmulatorBase Build(string type) => EmulatorFactory.CreateEmulator(type, 20, 6, 100);

    private static void Feed(TerminalEmulatorBase emulator, string text)
        => emulator.ProcessData(Encoding.ASCII.GetBytes(text));

    private static TerminalCell CellAt(TerminalEmulatorBase emulator, int row, int col)
    {
        Assert.True(emulator.GetBuffer().TryGetCell(row, col, out var cell), $"no cell at {row},{col}");
        return cell;
    }

    // ─────────────────────────────────────────────────────────────
    // Every terminal honours them
    // ─────────────────────────────────────────────────────────────

    [Theory]
    [MemberData(nameof(AllEmulators))]
    public void Decdwl_MakesTheCursorLineDoubleWidth(string emulatorType)
    {
        var emulator = Build(emulatorType);
        Feed(emulator, "\u001b#6");

        var cell = CellAt(emulator, 0, 0);
        Assert.True(cell.DoubleWidth, $"{emulatorType}: ESC # 6 should set double width");
        Assert.False(cell.DoubleHeight, $"{emulatorType}: DECDWL is width only");
    }

    [Theory]
    [MemberData(nameof(AllEmulators))]
    public void DecdhlTop_SetsTopHalfAndImpliesDoubleWidth(string emulatorType)
    {
        // VT220: a double-height line is ALWAYS also double-width. A renderer drawing the top
        // half at single width would produce a squashed glyph, so this is not a detail.
        var emulator = Build(emulatorType);
        Feed(emulator, "\u001b#3");

        var cell = CellAt(emulator, 0, 0);
        Assert.True(cell.DoubleHeightTop, $"{emulatorType}: ESC # 3 should set the TOP half");
        Assert.False(cell.DoubleHeightBottom, $"{emulatorType}: ESC # 3 is not the bottom half");
        Assert.True(cell.DoubleWidth, $"{emulatorType}: double height implies double width");
    }

    [Theory]
    [MemberData(nameof(AllEmulators))]
    public void DecdhlBottom_SetsBottomHalfAndImpliesDoubleWidth(string emulatorType)
    {
        var emulator = Build(emulatorType);
        Feed(emulator, "\u001b#4");

        var cell = CellAt(emulator, 0, 0);
        Assert.True(cell.DoubleHeightBottom, $"{emulatorType}: ESC # 4 should set the BOTTOM half");
        Assert.False(cell.DoubleHeightTop, $"{emulatorType}: ESC # 4 is not the top half");
        Assert.True(cell.DoubleWidth, $"{emulatorType}: double height implies double width");
    }

    [Theory]
    [MemberData(nameof(AllEmulators))]
    public void Decswl_ClearsEverything(string emulatorType)
    {
        // ESC # 5 must undo both height AND width. The TDV2200 copy called SetLineWidth after
        // SetLineHeight to be sure; SetLineHeight already clears all three attributes, which is
        // what this pins.
        var emulator = Build(emulatorType);
        Feed(emulator, "\u001b#3");
        Feed(emulator, "\u001b#5");

        var cell = CellAt(emulator, 0, 0);
        Assert.False(cell.DoubleWidth, $"{emulatorType}: ESC # 5 should clear double width");
        Assert.False(cell.DoubleHeight, $"{emulatorType}: ESC # 5 should clear double height");
        Assert.False(cell.DoubleHeightTop, $"{emulatorType}: ESC # 5 should clear the top-half flag");
        Assert.False(cell.DoubleHeightBottom, $"{emulatorType}: ESC # 5 should clear the bottom-half flag");
    }

    [Theory]
    [MemberData(nameof(AllEmulators))]
    public void TheSequenceAppliesToTheWHOLELine(string emulatorType)
    {
        // Line size is a property of the LINE, not of the text after the sequence — that is why
        // it is not an SGR attribute. Every column must carry it, including ones written later.
        var emulator = Build(emulatorType);
        Feed(emulator, "\u001b#6");

        for (int col = 0; col < emulator.Width; col++)
        {
            Assert.True(CellAt(emulator, 0, col).DoubleWidth,
                $"{emulatorType}: column {col} should be double width");
        }
    }

    [Theory]
    [MemberData(nameof(AllEmulators))]
    public void OtherLinesAreUnaffected(string emulatorType)
    {
        var emulator = Build(emulatorType);
        Feed(emulator, "\r\n");           // move to row 1
        Feed(emulator, "\u001b#6");

        Assert.False(CellAt(emulator, 0, 0).DoubleWidth, $"{emulatorType}: row 0 should be untouched");
        Assert.True(CellAt(emulator, 1, 0).DoubleWidth, $"{emulatorType}: row 1 should be double width");
    }

    [Theory]
    [MemberData(nameof(AllEmulators))]
    public void DoubleWidthThenDoubleHeight_EndsAsDoubleHeight(string emulatorType)
    {
        // DECDHL replaces DECDWL rather than combining with it, because SetLineHeight clears
        // all three attributes before setting its own.
        var emulator = Build(emulatorType);
        Feed(emulator, "\u001b#6");
        Feed(emulator, "\u001b#4");

        var cell = CellAt(emulator, 0, 0);
        Assert.True(cell.DoubleHeightBottom);
        Assert.True(cell.DoubleWidth);
        Assert.False(cell.DoubleHeightTop);
    }

    [Theory]
    [MemberData(nameof(AllEmulators))]
    public void DecalnIsNotTreatedAsALineSize(string emulatorType)
    {
        // ESC # 8 is DECALN, a screen-fill test pattern. It shares the '#' intermediate and must
        // NOT be swallowed by the line-size switch as an unknown size.
        var emulator = Build(emulatorType);
        Feed(emulator, "\u001b#8");

        var cell = CellAt(emulator, 0, 0);
        Assert.False(cell.DoubleWidth, $"{emulatorType}: ESC # 8 must not set a line size");
        Assert.False(cell.DoubleHeight, $"{emulatorType}: ESC # 8 must not set a line size");
    }

    // ─────────────────────────────────────────────────────────────
    // One representation, not two
    // ─────────────────────────────────────────────────────────────

    [Theory]
    [MemberData(nameof(AllEmulators))]
    public void TheCellFlagsAgreeWithTheAttributes(string emulatorType)
    {
        // TerminalCell.DoubleWidth/DoubleHeight used to be bits 0-1 of the packed _flags byte,
        // written alongside the attributes by one method and read only by tests. Two records of
        // one fact, with nothing keeping them equal. They are derived now, and this asserts it
        // across all four states rather than trusting that.
        string[] sequences = { "\u001b#5", "\u001b#6", "\u001b#3", "\u001b#4" };

        for (int i = 0; i < sequences.Length; i++)
        {
            var emulator = Build(emulatorType);
            Feed(emulator, sequences[i]);
            var cell = CellAt(emulator, 0, 0);

            Assert.Equal(cell.Attributes.HasAttribute(CharacterAttributes.DoubleWidth), cell.DoubleWidth);
            Assert.Equal(
                cell.Attributes.HasAttribute(CharacterAttributes.DoubleHeightTop)
                    || cell.Attributes.HasAttribute(CharacterAttributes.DoubleHeightBottom),
                cell.DoubleHeight);
            Assert.Equal(cell.Attributes.HasAttribute(CharacterAttributes.DoubleHeightTop), cell.DoubleHeightTop);
            Assert.Equal(cell.Attributes.HasAttribute(CharacterAttributes.DoubleHeightBottom), cell.DoubleHeightBottom);
        }
    }

    [Theory]
    [MemberData(nameof(AllEmulators))]
    public void FontNumberSurvivesALineSizeChange(string emulatorType)
    {
        // The two freed bits sat next to FontNumber in the same packed byte. Setting a line size
        // must not disturb it — TDV glyph selection reads FontNumber, so a stray write there
        // would draw characters from the wrong ROM set.
        var emulator = Build(emulatorType);
        Feed(emulator, "ABC");

        var before = CellAt(emulator, 0, 0).FontNumber;
        Feed(emulator, "\u001b#3");
        Assert.Equal(before, CellAt(emulator, 0, 0).FontNumber);
    }
}
