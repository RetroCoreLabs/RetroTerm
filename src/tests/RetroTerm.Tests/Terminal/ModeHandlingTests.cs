using System;
using System.Collections.Generic;
using System.Text;
using RetroTerm.Core.Terminal.Buffer;
using RetroTerm.Core.Terminal.Emulators;
using Xunit;

namespace RetroTerm.Tests.Terminal;

/// <summary>
/// Phase 3 part 3: the mode table — SM/RM, bundled modes, and reporting them.
///
/// Three defects, all host-visible:
///
///  1. The ANSI modes (SM/RM, the ones with no '?' marker) were not handled at all. That did not
///     merely ignore the sequences: IRM (insert mode) and LNM (new-line mode) had fields, reset
///     code and READ SITES in the base and in TDV2215, and nothing anywhere could set either to
///     true. The insert path in HandleCharacter was fully implemented and unreachable, so a host
///     using IRM to insert text silently got overwrite instead.
///  2. A mode sequence may carry several modes — "CSI ? 1 ; 25 h" sets DECCKM and DECTCEM together,
///     and hosts do bundle them — but only the first parameter was applied. Every mode after the
///     first was dropped.
///  3. DECRQM existed for private modes only, so a host could not ask about an ANSI mode at all.
/// </summary>
public class ModeHandlingTests
{
    private static void Feed(TerminalEmulatorBase emulator, string s)
    {
        emulator.ProcessData(Encoding.ASCII.GetBytes(s));
    }

    private static string Row(TerminalEmulatorBase emulator, int row)
    {
        return ScreenReader.GetRowText(emulator.Buffer, row);
    }

    private static List<string> CaptureReplies(TerminalEmulatorBase emulator)
    {
        var replies = new List<string>();
        emulator.DataToSend += bytes => replies.Add(Encoding.ASCII.GetString(bytes));
        return replies;
    }

    // ─────────────────────────────────────────────────────────────
    // IRM — insert mode, previously unreachable
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void InsertModePushesExistingTextRight()
    {
        var emulator = new VT100Emulator(20, 3);
        Feed(emulator, "ABCDE");

        Feed(emulator, "\x1b[1;1H");   // back to the start of the line
        Feed(emulator, "\x1b[4h");     // IRM on
        Feed(emulator, "XY");

        Assert.Equal("XYABCDE", Row(emulator, 0));
    }

    [Fact]
    public void ReplaceModeOverwrites()
    {
        var emulator = new VT100Emulator(20, 3);
        Feed(emulator, "ABCDE");

        Feed(emulator, "\x1b[1;1H");
        Feed(emulator, "\x1b[4l");     // IRM off — the default
        Feed(emulator, "XY");

        Assert.Equal("XYCDE", Row(emulator, 0));
    }

    [Fact]
    public void InsertModeIsOffUntilAHostTurnsItOn()
    {
        var emulator = new VT100Emulator(20, 3);
        Feed(emulator, "ABCDE");
        Feed(emulator, "\x1b[1;1H");
        Feed(emulator, "XY");

        Assert.Equal("XYCDE", Row(emulator, 0));
    }

    [Fact]
    public void ResetTurnsInsertModeOffAgain()
    {
        // RIS is ESC c, built from chars because C#'s "\x" escape is variable length — "\x1bc"
        // would be the single character U+01BC, not ESC followed by 'c'.
        var emulator = new VT100Emulator(20, 3);
        var replies = CaptureReplies(emulator);

        Feed(emulator, "\x1b[4h");
        Feed(emulator, new string(new[] { (char)0x1B, 'c' }));     // RIS

        // Assert the MODE, not only a symptom: a screen-shaped assertion on its own would pass
        // just as well if RIS had cleared the screen and left insert mode switched on.
        Feed(emulator, "\x1b[4$p");
        Assert.Equal("\x1b[4;2$y", replies[replies.Count - 1]);

        Feed(emulator, "ABCDE\x1b[1;1HXY");
        Assert.Equal("XYCDE", Row(emulator, 0));
    }

    // ─────────────────────────────────────────────────────────────
    // LNM — new-line mode, previously unreachable
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void NewLineModeMakesLineFeedAlsoReturnTheCarriage()
    {
        var emulator = new VT100Emulator(20, 4);

        Feed(emulator, "\x1b[20h");    // LNM on
        Feed(emulator, "ABC\nDEF");

        Assert.Equal("ABC", Row(emulator, 0));
        Assert.Equal("DEF", Row(emulator, 1));   // column reset by the LF
    }

    [Fact]
    public void WithoutNewLineModeALineFeedKeepsTheColumn()
    {
        var emulator = new VT100Emulator(20, 4);

        Feed(emulator, "\x1b[20l");
        Feed(emulator, "ABC\nDEF");

        Assert.Equal("ABC", Row(emulator, 0));
        Assert.Equal("   DEF", Row(emulator, 1));   // stayed in column 4
    }

    // ─────────────────────────────────────────────────────────────
    // Bundled modes
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void SeveralPrivateModesInOneSequenceAllApply()
    {
        // "CSI ? 1 ; 25 h" — the second mode used to be dropped on the floor.
        var emulator = new VT100Emulator(20, 3);

        Feed(emulator, "\x1b[?25l");             // hide the cursor first
        Feed(emulator, "\x1b[?1;25h");           // set DECCKM and DECTCEM together

        Assert.True(emulator.Cursor.Visible);    // the SECOND parameter took effect
    }

    [Fact]
    public void SeveralPrivateModesResetTogetherToo()
    {
        var emulator = new VT100Emulator(20, 3);

        Feed(emulator, "\x1b[?7;25h");
        Feed(emulator, "\x1b[?7;25l");

        Assert.False(emulator.Cursor.Visible);
    }

    [Fact]
    public void SeveralAnsiModesInOneSequenceAllApply()
    {
        var emulator = new VT100Emulator(20, 4);
        var replies = CaptureReplies(emulator);

        Feed(emulator, "\x1b[4;20h");            // IRM and LNM together

        Feed(emulator, "\x1b[4$p");
        Feed(emulator, "\x1b[20$p");

        Assert.Equal("\x1b[4;1$y", replies[0]);
        Assert.Equal("\x1b[20;1$y", replies[1]);
    }

    [Fact]
    public void AModeInABundleThisTerminalDoesNotKnowDoesNotStopTheOthers()
    {
        // A host bundling a mode we do not implement must still get the ones we do.
        var emulator = new VT100Emulator(20, 3);

        Feed(emulator, "\x1b[?25l");
        Feed(emulator, "\x1b[?9999;25h");

        Assert.True(emulator.Cursor.Visible);
    }

    // ─────────────────────────────────────────────────────────────
    // Reporting ANSI modes
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void AnAnsiModeCanBeQueried()
    {
        var emulator = new VT100Emulator(20, 3);
        var replies = CaptureReplies(emulator);

        Feed(emulator, "\x1b[4h");
        Feed(emulator, "\x1b[4$p");

        Assert.Single(replies);
        Assert.Equal("\x1b[4;1$y", replies[0]);
    }

    [Fact]
    public void AResetAnsiModeReportsReset()
    {
        var emulator = new VT100Emulator(20, 3);
        var replies = CaptureReplies(emulator);

        Feed(emulator, "\x1b[4l");
        Feed(emulator, "\x1b[4$p");

        Assert.Equal("\x1b[4;2$y", replies[0]);
    }

    [Fact]
    public void AnUnimplementedAnsiModeReportsNotRecognised()
    {
        var emulator = new VT100Emulator(20, 3);
        var replies = CaptureReplies(emulator);

        Feed(emulator, "\x1b[99$p");

        Assert.Equal("\x1b[99;0$y", replies[0]);
    }

    [Fact]
    public void QueryingAnAnsiModeDoesNotSetIt()
    {
        // CSI 4 $ p and CSI 4 h differ only by the intermediate, exactly as in the private form.
        var emulator = new VT100Emulator(20, 3);

        Feed(emulator, "\x1b[4$p");
        Feed(emulator, "ABCDE\x1b[1;1HXY");

        Assert.Equal("XYCDE", Row(emulator, 0));   // still replace mode
    }

    [Fact]
    public void APlainCsiPWithNoIntermediateIsIgnored()
    {
        // Guard: 'p' without the '$' is not DECRQM and must not answer anything.
        var emulator = new VT100Emulator(20, 3);
        var replies = CaptureReplies(emulator);

        Feed(emulator, "\x1b[4p");

        Assert.Empty(replies);
    }
}
