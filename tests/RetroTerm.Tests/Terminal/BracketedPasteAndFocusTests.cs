using System.Text;
using RetroTerm.Core.Configuration;
using RetroTerm.Core.Terminal.Emulators;
using Xunit;

namespace RetroTerm.Tests.Terminal;

/// <summary>
/// Two xterm private modes that modern shells switch on the moment they start.
///
/// Bracketed paste (2004) is the one that matters: with it on, a shell can tell typed input from
/// pasted input, which is the difference between a pasted command being shown for the user to look
/// at and it running the instant the newline inside it arrives.
///
/// Focus reporting (1004) is smaller but the same shape - a mode the host sets, and a report the
/// terminal must not send unless it did.
/// </summary>
public class BracketedPasteAndFocusTests
{
    private static TerminalEmulatorBase Build()
        => EmulatorFactory.CreateEmulator("VT100", 80, 24, 100);

    private static void Feed(TerminalEmulatorBase emulator, string text)
        => emulator.ProcessData(Encoding.ASCII.GetBytes(text));

    private static StringBuilder CaptureReplies(TerminalEmulatorBase emulator)
    {
        var replies = new StringBuilder();
        emulator.DataToSend += bytes => replies.Append(Encoding.ASCII.GetString(bytes));
        return replies;
    }

    [Fact]
    public void PastedTextIsUntouchedUntilTheHostAsksForBrackets()
    {
        // Off by default. A host that never asked would print the brackets.
        var emulator = Build();

        Assert.Equal("ls -l", emulator.WrapForPaste("ls -l"));
    }

    [Fact]
    public void WithTheModeOnThePasteArrivesBracketed()
    {
        var emulator = Build();

        Feed(emulator, "\x1b[?2004h");

        Assert.True(emulator.BracketedPasteMode);
        Assert.Equal("\x1b[200~ls -l\x1b[201~", emulator.WrapForPaste("ls -l"));
    }

    [Fact]
    public void TheModeCanBeTurnedOffAgain()
    {
        var emulator = Build();
        Feed(emulator, "\x1b[?2004h");

        Feed(emulator, "\x1b[?2004l");

        Assert.False(emulator.BracketedPasteMode);
        Assert.Equal("ls -l", emulator.WrapForPaste("ls -l"));
    }

    [Fact]
    public void AnEndMarkerInsideThePasteIsRemovedRatherThanTrusted()
    {
        // THE test. Whatever is on the clipboard came from somewhere. If it carries the end marker
        // itself, leaving it in closes the bracket early and everything after it reaches the shell
        // as if it had been typed - which is exactly what bracketed paste exists to prevent.
        var emulator = Build();
        Feed(emulator, "\x1b[?2004h");

        var wrapped = emulator.WrapForPaste("harmless\x1b[201~rm -rf /\n");

        Assert.Equal("\x1b[200~harmlessrm -rf /\n\x1b[201~", wrapped);

        // Said plainly: exactly one end marker, and it is the last thing in the string.
        Assert.EndsWith("\x1b[201~", wrapped);
        Assert.Equal(wrapped.Length - 6, wrapped.IndexOf("\x1b[201~", System.StringComparison.Ordinal));
    }

    [Fact]
    public void AnEmptyPasteStaysEmpty()
    {
        var emulator = Build();
        Feed(emulator, "\x1b[?2004h");

        Assert.Equal("", emulator.WrapForPaste(""));
    }

    [Fact]
    public void FocusIsNotReportedUntilTheHostAsks()
    {
        // An unasked-for report lands in the middle of whatever the host was reading, and a shell
        // that never enabled the mode would print the letter.
        var emulator = Build();
        var replies = CaptureReplies(emulator);

        emulator.ReportFocusChange(true);
        emulator.ReportFocusChange(false);

        Assert.Equal("", replies.ToString());
    }

    [Fact]
    public void WithTheModeOnFocusInAndOutAreDifferentReports()
    {
        var emulator = Build();
        var replies = CaptureReplies(emulator);

        Feed(emulator, "\x1b[?1004h");
        emulator.ReportFocusChange(true);
        emulator.ReportFocusChange(false);

        Assert.True(emulator.FocusReporting);
        Assert.Equal("\x1b[I\x1b[O", replies.ToString());
    }

    [Fact]
    public void BothModesAnswerADecrqmQuery()
    {
        // A host can ask whether this terminal knows a mode before using it. "I have never heard of
        // that" and "I know it and it is off" are different answers, and both modes are now on the
        // profile's recognised list so the second one is what comes back.
        var emulator = Build();
        var replies = CaptureReplies(emulator);

        Feed(emulator, "\x1b[?2004$p");
        Feed(emulator, "\x1b[?2004h");
        Feed(emulator, "\x1b[?2004$p");
        Feed(emulator, "\x1b[?1004$p");

        // 2 is reset, 1 is set.
        Assert.Equal("\x1b[?2004;2$y\x1b[?2004;1$y\x1b[?1004;2$y", replies.ToString());
    }
}
