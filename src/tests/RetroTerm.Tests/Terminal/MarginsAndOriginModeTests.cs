using System;
using System.Collections.Generic;
using System.Text;
using RetroTerm.Core.Terminal.Emulators;
using Xunit;

namespace RetroTerm.Tests.Terminal;

/// <summary>
/// Phase 2b part 3: margins and origin mode, implemented once (problem B.5).
///
/// These rules used to be re-derived at every call site and disagreed with each other:
///  - origin mode was applied to CUP only, so VPA addressed the wrong row whenever a host set a
///    scrolling region and turned origin mode on;
///  - CUP added the top margin without clamping to the bottom one, so a too-large row escaped the
///    region entirely;
///  - CUU/CUD clamped to the screen rather than the margins, so cursor-up walked straight out of
///    the region;
///  - DECOM homed to the absolute corner, putting the cursor OUTSIDE the region it had just
///    confined it to;
///  - CPR reported absolute rows, so a host that sent CUP and then asked where the cursor was got
///    back a different number from the one it sent.
///
/// Everything vertical now goes through SetCursorRowFromHost / MoveCursorUpWithinMargins /
/// MoveCursorDownWithinMargins.
/// </summary>
public class MarginsAndOriginModeTests
{
    private static void Feed(TerminalEmulatorBase emulator, string s)
    {
        emulator.ProcessData(Encoding.ASCII.GetBytes(s));
    }

    /// <summary>
    /// A 20x10 screen with a scrolling region over rows 3..7 (indices 2..6).
    /// </summary>
    private static VT100Emulator WithRegion()
    {
        var emulator = new VT100Emulator(20, 10);
        Feed(emulator, "\x1b[3;7r");
        return emulator;
    }

    // ─────────────────────────────────────────────────────────────
    // Origin mode changes what a row number means
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void WithOriginModeOff_CupAddressesTheScreen()
    {
        var emulator = WithRegion();

        Feed(emulator, "\x1b[?6l");     // DECOM off
        Feed(emulator, "\x1b[1;1H");

        Assert.Equal(0, emulator.Cursor.Row);
    }

    [Fact]
    public void WithOriginModeOn_CupAddressesTheRegion()
    {
        var emulator = WithRegion();

        Feed(emulator, "\x1b[?6h");     // DECOM on
        Feed(emulator, "\x1b[1;1H");

        Assert.Equal(2, emulator.Cursor.Row);   // row 1 means the top margin
    }

    [Fact]
    public void WithOriginModeOn_VerticalPositionAbsoluteAlsoAddressesTheRegion()
    {
        // VPA ignored origin mode entirely, so it and CUP disagreed about what row 2 meant.
        var emulator = WithRegion();

        Feed(emulator, "\x1b[?6h");
        Feed(emulator, "\x1b[2d");      // VPA 2

        Assert.Equal(3, emulator.Cursor.Row);
    }

    [Fact]
    public void CupAndVpaAgreeWithEachOther_InBothModes()
    {
        // The property that matters more than either individually.
        for (int originOn = 0; originOn <= 1; originOn++)
        {
            var viaCup = WithRegion();
            var viaVpa = WithRegion();
            string decom = originOn == 1 ? "\x1b[?6h" : "\x1b[?6l";

            Feed(viaCup, decom);
            Feed(viaCup, "\x1b[3;1H");

            Feed(viaVpa, decom);
            Feed(viaVpa, "\x1b[3d");

            Assert.Equal(viaCup.Cursor.Row, viaVpa.Cursor.Row);
        }
    }

    [Fact]
    public void WithOriginModeOn_ARowPastTheBottomMarginIsClampedToIt()
    {
        // CUP added the top margin but never clamped, so a large row escaped the region.
        var emulator = WithRegion();

        Feed(emulator, "\x1b[?6h");
        Feed(emulator, "\x1b[99;1H");

        Assert.Equal(6, emulator.Cursor.Row);   // the bottom margin, not row 9
    }

    [Fact]
    public void WithOriginModeOff_ARowPastTheScreenIsClampedToTheScreen()
    {
        var emulator = WithRegion();

        Feed(emulator, "\x1b[?6l");
        Feed(emulator, "\x1b[99;1H");

        Assert.Equal(9, emulator.Cursor.Row);
    }

    [Fact]
    public void TurningOriginModeOnHomesInsideTheRegion()
    {
        // DECOM homes the cursor - and home means the corner of what the cursor can address.
        var emulator = WithRegion();

        Feed(emulator, "\x1b[9;5H");    // somewhere below the region
        Feed(emulator, "\x1b[?6h");

        Assert.Equal(2, emulator.Cursor.Row);
        Assert.Equal(0, emulator.Cursor.Column);
    }

    [Fact]
    public void TurningOriginModeOffHomesToTheScreenCorner()
    {
        var emulator = WithRegion();

        Feed(emulator, "\x1b[?6h");
        Feed(emulator, "\x1b[3;5H");
        Feed(emulator, "\x1b[?6l");

        Assert.Equal(0, emulator.Cursor.Row);
        Assert.Equal(0, emulator.Cursor.Column);
    }

    // ─────────────────────────────────────────────────────────────
    // Cursor movement respects the margins
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void CursorUpStopsAtTheTopMargin()
    {
        var emulator = WithRegion();

        Feed(emulator, "\x1b[6;1H");    // index 5, inside the region
        Feed(emulator, "\x1b[20A");     // way up

        Assert.Equal(2, emulator.Cursor.Row);
    }

    [Fact]
    public void CursorDownStopsAtTheBottomMargin()
    {
        var emulator = WithRegion();

        Feed(emulator, "\x1b[4;1H");    // index 3, inside the region
        Feed(emulator, "\x1b[20B");

        Assert.Equal(6, emulator.Cursor.Row);
    }

    [Fact]
    public void ACursorAboveTheRegionIsNotDraggedIntoIt()
    {
        // The subtle half of the rule: the margin only limits a cursor that started inside.
        var emulator = WithRegion();

        Feed(emulator, "\x1b[?6l");
        Feed(emulator, "\x1b[2;1H");    // index 1, ABOVE the region
        Feed(emulator, "\x1b[5A");

        Assert.Equal(0, emulator.Cursor.Row);   // free to reach the top of the screen
    }

    [Fact]
    public void ACursorBelowTheRegionKeepsTheWholeScreenToMoveIn()
    {
        var emulator = WithRegion();

        Feed(emulator, "\x1b[9;1H");    // index 8, BELOW the region
        Feed(emulator, "\x1b[5B");

        Assert.Equal(9, emulator.Cursor.Row);
    }

    [Fact]
    public void CursorNextLineAndPreviousLineRespectTheMarginsToo()
    {
        var emulator = WithRegion();

        Feed(emulator, "\x1b[4;8H");
        Feed(emulator, "\x1b[20E");     // CNL
        Assert.Equal(6, emulator.Cursor.Row);
        Assert.Equal(0, emulator.Cursor.Column);   // CNL also returns to column 1

        Feed(emulator, "\x1b[20F");     // CPL
        Assert.Equal(2, emulator.Cursor.Row);
    }

    [Fact]
    public void WithNoRegionSet_MovementUsesTheWholeScreen()
    {
        // Guard: the margin-aware rules must not change the ordinary full-screen case.
        var emulator = new VT100Emulator(20, 10);

        Feed(emulator, "\x1b[5;1H");
        Feed(emulator, "\x1b[20A");
        Assert.Equal(0, emulator.Cursor.Row);

        Feed(emulator, "\x1b[20B");
        Assert.Equal(9, emulator.Cursor.Row);
    }

    [Fact]
    public void ACountOfZeroIsTreatedAsOne()
    {
        // ECMA-48: an omitted or zero parameter means 1.
        var emulator = new VT100Emulator(20, 10);

        Feed(emulator, "\x1b[5;1H");
        Feed(emulator, "\x1b[0A");

        Assert.Equal(3, emulator.Cursor.Row);
    }

    // ─────────────────────────────────────────────────────────────
    // Reporting back in the host's own coordinates
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void CursorPositionReportUsesRegionCoordinatesInOriginMode()
    {
        // A host that sends CUP and then asks where the cursor is must get its own number back.
        var emulator = WithRegion();
        var replies = new List<string>();
        emulator.DataToSend += bytes => replies.Add(Encoding.ASCII.GetString(bytes));

        Feed(emulator, "\x1b[?6h");
        Feed(emulator, "\x1b[2;5H");
        Feed(emulator, "\x1b[6n");      // DSR - report cursor position

        Assert.Single(replies);
        Assert.Equal("\x1b[2;5R", replies[0]);
    }

    [Fact]
    public void CursorPositionReportUsesScreenCoordinatesWithoutOriginMode()
    {
        var emulator = WithRegion();
        var replies = new List<string>();
        emulator.DataToSend += bytes => replies.Add(Encoding.ASCII.GetString(bytes));

        Feed(emulator, "\x1b[?6l");
        Feed(emulator, "\x1b[4;5H");
        Feed(emulator, "\x1b[6n");

        Assert.Single(replies);
        Assert.Equal("\x1b[4;5R", replies[0]);
    }

    [Fact]
    public void PositionSurvivesARoundTripThroughTheReport()
    {
        // The strongest form: whatever the host asked for is what it is told, in either mode.
        for (int originOn = 0; originOn <= 1; originOn++)
        {
            var emulator = WithRegion();
            var replies = new List<string>();
            emulator.DataToSend += bytes => replies.Add(Encoding.ASCII.GetString(bytes));

            Feed(emulator, originOn == 1 ? "\x1b[?6h" : "\x1b[?6l");
            Feed(emulator, "\x1b[3;7H");
            Feed(emulator, "\x1b[6n");

            Assert.Equal("\x1b[3;7R", replies[0]);
        }
    }
}
