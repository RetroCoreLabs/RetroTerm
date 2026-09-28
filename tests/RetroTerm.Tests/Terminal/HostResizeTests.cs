using System.Collections.Generic;
using System.Text;
using RetroTerm.Core.Configuration;
using RetroTerm.Core.Terminal.Emulators;
using Xunit;

namespace RetroTerm.Tests.Terminal;

/// <summary>
/// Host-commanded screen size: DECCOLM and the xterm text-area commands.
///
/// The screen size used to be settable only by the person at the keyboard. That is wrong for a
/// terminal - a host has always been able to say how wide it wants the screen, and every plotting
/// or full-screen program that wants a size other than the default asks for one this way.
///
/// The other reason it is done with explicit commands: an earlier attempt grew the screen the
/// moment a Tektronix drawing arrived, and five random bytes are enough to be a valid drawing
/// command. <c>EscapeParserFuzzTests</c> caught it on three seeds. A five-byte named sequence
/// cannot happen by accident, which is the whole point of doing it this way.
/// </summary>
public class HostResizeTests
{
    /// <summary>
    /// Terminals whose screen the host is allowed to change.
    /// </summary>
    public static IEnumerable<object[]> ResizableEmulators()
    {
        yield return new object[] { "VT100" };
        yield return new object[] { "ANSI" };
    }

    private static TerminalEmulatorBase Build(string type, int width, int height)
        => EmulatorFactory.CreateEmulator(type, width, height, 100);

    private static void Feed(TerminalEmulatorBase emulator, string text)
        => emulator.ProcessData(Encoding.ASCII.GetBytes(text));

    /// <summary>
    /// Collects everything the emulator sends back, so a report can be read as a string.
    /// </summary>
    private static StringBuilder CaptureReplies(TerminalEmulatorBase emulator)
    {
        var replies = new StringBuilder();
        emulator.DataToSend += bytes => replies.Append(Encoding.ASCII.GetString(bytes));
        return replies;
    }

    [Theory]
    [MemberData(nameof(ResizableEmulators))]
    public void DeccolmSetMakesTheScreenAHundredAndThirtyTwoWide(string type)
    {
        var emulator = Build(type, 80, 24);

        Feed(emulator, "\x1b[?3h");

        Assert.Equal(132, emulator.Width);
        Assert.Equal(24, emulator.Height);   // DECCOLM is about columns only
    }

    [Theory]
    [MemberData(nameof(ResizableEmulators))]
    public void DeccolmResetGoesBackToEighty(string type)
    {
        var emulator = Build(type, 132, 24);

        Feed(emulator, "\x1b[?3l");

        Assert.Equal(80, emulator.Width);
    }

    /// <summary>
    /// The codepoint in one cell. 0 means the cell was cleared rather than written to.
    /// </summary>
    private static uint CellAt(TerminalEmulatorBase emulator, int row, int col)
    {
        emulator.GetBuffer().TryGetCell(row, col, out var cell);
        return cell.Codepoint;
    }

    [Fact]
    public void DeccolmClearsTheScreenAndHomesTheCursor()
    {
        var emulator = Build("VT100", 80, 24);
        Feed(emulator, "\x1b[5;3HHELLO");
        Assert.Equal('H', (char)CellAt(emulator, 4, 2));

        Feed(emulator, "\x1b[?3h");

        // Cleared cells carry codepoint 0, which is what distinguishes them from a typed space.
        Assert.Equal(0u, CellAt(emulator, 4, 2));
        Assert.Equal(0, emulator.GetCursor().Row);
        Assert.Equal(0, emulator.GetCursor().Column);
    }

    [Fact]
    public void DeccolmIsIgnoredByATerminalWithAFixedScreen()
    {
        // A TDV2200 has one screen, wired 80 by 25. Obeying here would invent a machine that never
        // existed - see TerminalFeatures.HostResize.
        var emulator = Build("TDV2200", 80, 25);

        Feed(emulator, "\x1b[?3h");

        Assert.Equal(80, emulator.Width);
        Assert.Equal(25, emulator.Height);
    }

    [Theory]
    [MemberData(nameof(ResizableEmulators))]
    public void TheHostCanAskForAnExactSize(string type)
    {
        var emulator = Build(type, 80, 24);

        Feed(emulator, "\x1b[8;30;100t");

        Assert.Equal(100, emulator.Width);
        Assert.Equal(30, emulator.Height);
    }

    [Fact]
    public void AZeroLeavesThatDimensionAlone()
    {
        var emulator = Build("VT100", 80, 24);

        Feed(emulator, "\x1b[8;0;100t");

        Assert.Equal(100, emulator.Width);
        Assert.Equal(24, emulator.Height);
    }

    [Fact]
    public void AMissingHeightLeavesTheHeightAlone()
    {
        var emulator = Build("VT100", 80, 24);

        Feed(emulator, "\x1b[8t");

        Assert.Equal(80, emulator.Width);
        Assert.Equal(24, emulator.Height);
    }

    [Fact]
    public void AnAbsurdSizeIsBroughtInsideWhatTheTerminalWillBuild()
    {
        var emulator = Build("VT100", 80, 24);

        Feed(emulator, "\x1b[8;99999;99999t");

        Assert.Equal(512, emulator.Width);
        Assert.Equal(512, emulator.Height);
    }

    [Fact]
    public void TheTextAreaReportNamesTheCurrentSize()
    {
        var emulator = Build("VT100", 80, 24);
        var replies = CaptureReplies(emulator);

        Feed(emulator, "\x1b[18t");

        Assert.Equal("\x1b[8;24;80t", replies.ToString());
    }

    [Fact]
    public void TheScreenSizeReportNamesTheSameNumbers()
    {
        // There is no desktop behind this terminal that is bigger than its own screen.
        var emulator = Build("VT100", 132, 30);
        var replies = CaptureReplies(emulator);

        Feed(emulator, "\x1b[19t");

        Assert.Equal("\x1b[9;30;132t", replies.ToString());
    }

    [Fact]
    public void TheReportFollowsTheScreenAfterAResize()
    {
        var emulator = Build("VT100", 80, 24);
        var replies = CaptureReplies(emulator);

        Feed(emulator, "\x1b[?3h");
        Feed(emulator, "\x1b[18t");

        Assert.Equal("\x1b[8;24;132t", replies.ToString());
    }

    [Fact]
    public void AWindowManagerRequestIsDroppedRatherThanAnswered()
    {
        // Iconify. Nothing to reply, nothing to resize, and no window for a host to move about.
        var emulator = Build("VT100", 80, 24);
        var replies = CaptureReplies(emulator);

        Feed(emulator, "\x1b[2t");

        Assert.Equal("", replies.ToString());
        Assert.Equal(80, emulator.Width);
        Assert.Equal(24, emulator.Height);
    }

    [Fact]
    public void ATerminalWithAFixedScreenStaysSilentAboutItsSize()
    {
        // The whole sequence family is an xterm extension. A TDV answering it would be claiming to
        // be something it is not.
        var emulator = Build("TDV2200", 80, 25);
        var replies = CaptureReplies(emulator);

        Feed(emulator, "\x1b[18t");

        Assert.Equal("", replies.ToString());
    }

    [Fact]
    public void DecrqmReportsTheWideScreenAfterDeccolm()
    {
        var emulator = Build("VT100", 80, 24);
        var replies = CaptureReplies(emulator);

        Feed(emulator, "\x1b[?3h");
        Feed(emulator, "\x1b[?3$p");

        // DECRPM: 1 means set.
        Assert.Equal("\x1b[?3;1$y", replies.ToString());
    }

    [Fact]
    public void TextSurvivesAResizeThatOnlyAddsColumns()
    {
        // CSI 8 t goes through the same reflowing resize the window drag uses, so it must not throw
        // the screen away the way DECCOLM deliberately does.
        var emulator = Build("VT100", 80, 24);
        Feed(emulator, "HELLO");

        Feed(emulator, "\x1b[8;24;100t");

        Assert.Equal('H', (char)CellAt(emulator, 0, 0));
        Assert.Equal(100, emulator.Width);
    }
}
