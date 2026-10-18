using System.Text;
using RetroTerm.Core.Configuration;
using RetroTerm.Core.Terminal.Emulators;
using RetroTerm.Core.Terminal.Input;
using Xunit;

namespace RetroTerm.Tests.Terminal;

/// <summary>
/// Mouse reporting: the xterm tracking modes and the two wire formats.
///
/// Mode and encoding are separate settings a host turns on with separate sequences, and that
/// separation is the thing worth pinning. Mode 1006 changes HOW a report is written without
/// changing WHICH events are reported, so a terminal that treated them as one setting would either
/// drop the mode when the encoding changed or ignore the encoding entirely.
/// </summary>
public class MouseReportingTests
{
    private static TerminalEmulatorBase Build()
        => EmulatorFactory.CreateEmulator("VT100", 80, 24, 100);

    private static void Feed(TerminalEmulatorBase emulator, string text)
        => emulator.ProcessData(Encoding.ASCII.GetBytes(text));

    private static StringBuilder CaptureReplies(TerminalEmulatorBase emulator)
    {
        var replies = new StringBuilder();
        emulator.DataToSend += bytes => replies.Append(Encoding.Latin1.GetString(bytes));
        return replies;
    }

    [Fact]
    public void NothingIsReportedUntilTheHostAsks()
    {
        // The default, and the one that matters most: a terminal that sent mouse bytes unasked
        // would drop control codes into whatever the host was reading.
        var emulator = Build();
        var replies = CaptureReplies(emulator);

        Assert.False(emulator.Mouse.IsTracking);
        Assert.False(emulator.ReportMouse(MouseEventKind.Press, MouseButton.Left, 1, 1));
        Assert.Equal("", replies.ToString());
    }

    [Fact]
    public void NormalTrackingReportsAPressInTheX10Form()
    {
        var emulator = Build();
        var replies = CaptureReplies(emulator);
        Feed(emulator, "\x1b[?1000h");

        emulator.ReportMouse(MouseEventKind.Press, MouseButton.Left, 1, 1);

        // CSI M, then button, column and row each offset by 32.
        Assert.Equal("\x1b[M\x20\x21\x21", replies.ToString());
    }

    [Fact]
    public void TheX10FormCannotSayWhichButtonCameUp()
    {
        // A release is button 3 there - "something came up", not which. The format has nowhere to
        // put it, and this is exactly what SGR was invented to fix.
        var emulator = Build();
        var replies = CaptureReplies(emulator);
        Feed(emulator, "\x1b[?1000h");

        emulator.ReportMouse(MouseEventKind.Release, MouseButton.Right, 1, 1);

        Assert.Equal("\x1b[M\x23\x21\x21", replies.ToString());
    }

    [Fact]
    public void TheSgrFormKeepsTheButtonAndMarksTheReleaseWithItsFinalByte()
    {
        var emulator = Build();
        var replies = CaptureReplies(emulator);
        Feed(emulator, "\x1b[?1000h");
        Feed(emulator, "\x1b[?1006h");

        emulator.ReportMouse(MouseEventKind.Press, MouseButton.Right, 10, 5);
        emulator.ReportMouse(MouseEventKind.Release, MouseButton.Right, 10, 5);

        Assert.Equal("\x1b[<2;10;5M\x1b[<2;10;5m", replies.ToString());
    }

    [Fact]
    public void TheEncodingChangesWithoutLosingTheMode()
    {
        // 1006 says how, 1000 says what. Setting one must not clear the other.
        var emulator = Build();
        Feed(emulator, "\x1b[?1000h");
        Feed(emulator, "\x1b[?1006h");

        Assert.Equal(MouseTrackingMode.PressAndRelease, emulator.Mouse.Mode);
        Assert.Equal(MouseEncoding.Sgr, emulator.Mouse.Encoding);
    }

    [Fact]
    public void APositionPastTwoHundredAndTwentyThreeIsNotReportedInTheX10Form()
    {
        // One byte per coordinate means there is no byte left to use. xterm sends nothing, and so
        // does this - a report naming the wrong cell would make a program act on the wrong cell.
        var emulator = Build();
        var replies = CaptureReplies(emulator);
        Feed(emulator, "\x1b[?1000h");

        Assert.False(emulator.ReportMouse(MouseEventKind.Press, MouseButton.Left, 300, 1));
        Assert.Equal("", replies.ToString());
    }

    [Fact]
    public void AndTheSameWideScreenIsReportedFineInTheSgrForm()
    {
        // The reason a terminal wider than 223 columns needs 1006 at all.
        var emulator = Build();
        var replies = CaptureReplies(emulator);
        Feed(emulator, "\x1b[?1000h");
        Feed(emulator, "\x1b[?1006h");

        Assert.True(emulator.ReportMouse(MouseEventKind.Press, MouseButton.Left, 300, 1));
        Assert.Equal("\x1b[<0;300;1M", replies.ToString());
    }

    [Fact]
    public void NormalTrackingIgnoresMovement()
    {
        var emulator = Build();
        Feed(emulator, "\x1b[?1000h");

        Assert.False(emulator.ReportMouse(MouseEventKind.Motion, MouseButton.Left, 4, 4));
    }

    [Fact]
    public void ButtonMotionTrackingReportsADragButNotAWander()
    {
        // The difference between 1002 and 1003, and the mode a program uses to drag something.
        var emulator = Build();
        Feed(emulator, "\x1b[?1002h");

        Assert.True(emulator.ReportMouse(MouseEventKind.Motion, MouseButton.Left, 4, 4));
        Assert.False(emulator.ReportMouse(MouseEventKind.Motion, MouseButton.None, 4, 4));
    }

    [Fact]
    public void AnyEventTrackingReportsTheWanderToo()
    {
        var emulator = Build();
        Feed(emulator, "\x1b[?1003h");

        Assert.True(emulator.ReportMouse(MouseEventKind.Motion, MouseButton.None, 4, 4));
    }

    [Fact]
    public void MovementCarriesTheDragBit()
    {
        var emulator = Build();
        var replies = CaptureReplies(emulator);
        Feed(emulator, "\x1b[?1002h");
        Feed(emulator, "\x1b[?1006h");

        emulator.ReportMouse(MouseEventKind.Motion, MouseButton.Left, 7, 3);

        // 0 for left, plus 32 for "this is a drag rather than a fresh press".
        Assert.Equal("\x1b[<32;7;3M", replies.ToString());
    }

    [Fact]
    public void ModifiersRideAlongInTheButtonField()
    {
        var emulator = Build();
        var replies = CaptureReplies(emulator);
        Feed(emulator, "\x1b[?1000h");
        Feed(emulator, "\x1b[?1006h");

        emulator.ReportMouse(MouseEventKind.Press, MouseButton.Left, 1, 1,
            MouseModifiers.Control | MouseModifiers.Shift);

        Assert.Equal("\x1b[<20;1;1M", replies.ToString());
    }

    [Fact]
    public void TheX10ModeReportsNoReleasesAndNoModifiers()
    {
        // Mode 9 predates both. Adding either changes the number an old program reads.
        var emulator = Build();
        var replies = CaptureReplies(emulator);
        Feed(emulator, "\x1b[?9h");

        Assert.False(emulator.ReportMouse(MouseEventKind.Release, MouseButton.Left, 1, 1));
        emulator.ReportMouse(MouseEventKind.Press, MouseButton.Left, 1, 1, MouseModifiers.Control);

        Assert.Equal("\x1b[M\x20\x21\x21", replies.ToString());
    }

    [Fact]
    public void TheWheelIsAPressWithNoRelease()
    {
        var emulator = Build();
        var replies = CaptureReplies(emulator);
        Feed(emulator, "\x1b[?1000h");
        Feed(emulator, "\x1b[?1006h");

        emulator.ReportMouse(MouseEventKind.Press, MouseButton.WheelUp, 2, 2);
        emulator.ReportMouse(MouseEventKind.Press, MouseButton.WheelDown, 2, 2);

        Assert.Equal("\x1b[<64;2;2M\x1b[<65;2;2M", replies.ToString());
    }

    [Fact]
    public void TurningTrackingOffStopsTheReports()
    {
        var emulator = Build();
        Feed(emulator, "\x1b[?1003h");
        Feed(emulator, "\x1b[?1003l");

        Assert.False(emulator.Mouse.IsTracking);
        Assert.False(emulator.ReportMouse(MouseEventKind.Press, MouseButton.Left, 1, 1));
    }

    [Fact]
    public void TheModesAnswerADecrqmQuery()
    {
        var emulator = Build();
        var replies = CaptureReplies(emulator);

        Feed(emulator, "\x1b[?1000$p");
        Feed(emulator, "\x1b[?1000h");
        Feed(emulator, "\x1b[?1000$p");
        Feed(emulator, "\x1b[?1006$p");

        Assert.Equal("\x1b[?1000;2$y\x1b[?1000;1$y\x1b[?1006;2$y", replies.ToString());
    }
}
