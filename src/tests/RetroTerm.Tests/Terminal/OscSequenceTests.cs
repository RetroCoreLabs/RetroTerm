using System.Text;
using RetroTerm.Core.Configuration;
using RetroTerm.Core.Terminal.Emulators;
using Xunit;

namespace RetroTerm.Tests.Terminal;

/// <summary>
/// Operating System Command strings - the xterm side of a terminal's conversation with its host.
///
/// The colour queries are the ones that earn their keep: OSC 11 is how vim and tmux work out
/// whether they are on a dark or a light terminal, and a terminal that stays silent gets a colour
/// scheme picked by guesswork. Answering them needs the emulator to know what the UI actually
/// paints, which is why the display colours live on the emulator and the canvas pushes them in.
/// </summary>
public class OscSequenceTests
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
    public void OscZeroSetsBothTheTitleAndTheIconName()
    {
        var emulator = Build();

        Feed(emulator, "\x1b]0;a build is running\x1b\\");

        Assert.Equal("a build is running", emulator.Title);
        Assert.Equal("a build is running", emulator.IconName);
    }

    [Fact]
    public void OscOneSetsOnlyTheIconName()
    {
        // A host can set the two to different things, which is the whole reason they are separate.
        var emulator = Build();
        Feed(emulator, "\x1b]2;the long window title\x1b\\");

        Feed(emulator, "\x1b]1;short\x1b\\");

        Assert.Equal("the long window title", emulator.Title);
        Assert.Equal("short", emulator.IconName);
    }

    [Fact]
    public void OscSevenRecordsTheWorkingDirectoryVerbatim()
    {
        // Stored, not parsed. Nothing acts on it yet, and turning it into a path would be
        // inventing a meaning for a string this terminal has not been asked to use.
        var emulator = Build();

        Feed(emulator, "\x1b]7;file://host/home/ronny/dev\x1b\\");

        Assert.Equal("file://host/home/ronny/dev", emulator.ReportedWorkingDirectory);
    }

    [Fact]
    public void TheBackgroundQueryAnswersWithTheColourTheTerminalPaints()
    {
        var emulator = Build();
        emulator.DisplayBackground = (0x12, 0x34, 0x56);
        var replies = CaptureReplies(emulator);

        Feed(emulator, "\x1b]11;?\x1b\\");

        Assert.Equal("\x1b]11;rgb:1212/3434/5656\x1b\\", replies.ToString());
    }

    [Fact]
    public void TheForegroundAndCursorQueriesAnswerTheirOwnColours()
    {
        var emulator = Build();
        emulator.DisplayForeground = (255, 255, 255);
        emulator.DisplayCursorColour = (255, 0, 0);
        var replies = CaptureReplies(emulator);

        Feed(emulator, "\x1b]10;?\x1b\\");
        Feed(emulator, "\x1b]12;?\x1b\\");

        Assert.Equal("\x1b]10;rgb:ffff/ffff/ffff\x1b\\\x1b]12;rgb:ffff/0000/0000\x1b\\",
            replies.ToString());
    }

    [Fact]
    public void WhiteComesBackAsAllOnes()
    {
        // The eight-bit value is DOUBLED rather than shifted up. Shifting would report white as
        // fefe rather than ffff, leaving every colour slightly dark.
        var emulator = Build();
        emulator.DisplayBackground = (255, 255, 255);
        var replies = CaptureReplies(emulator);

        Feed(emulator, "\x1b]11;?\x1b\\");

        Assert.Contains("ffff/ffff/ffff", replies.ToString());
    }

    [Fact]
    public void APaletteEntryCanBeAskedFor()
    {
        // Index 1 is red, 205,0,0 in the xterm defaults this terminal reports.
        var emulator = Build();
        var replies = CaptureReplies(emulator);

        Feed(emulator, "\x1b]4;1;?\x1b\\");

        Assert.Equal("\x1b]4;1;rgb:cdcd/0000/0000\x1b\\", replies.ToString());
    }

    [Fact]
    public void AnIndexOutsideThePaletteIsNotAnswered()
    {
        // There is no colour 300. Answering with a different one would be worse than silence.
        var emulator = Build();
        var replies = CaptureReplies(emulator);

        Feed(emulator, "\x1b]4;300;?\x1b\\");

        Assert.Equal("", replies.ToString());
    }

    [Fact]
    public void SettingAColourIsIgnoredRatherThanAcceptedAndNotPainted()
    {
        // The palette a session paints from is built once by the renderer, so honouring a set needs
        // an override store and a repaint. Until that exists, accepting the sequence and painting
        // something else would be the worse of the two answers.
        var emulator = Build();
        var replies = CaptureReplies(emulator);

        Feed(emulator, "\x1b]11;rgb:ffff/0000/0000\x1b\\");
        Feed(emulator, "\x1b]11;?\x1b\\");

        // Still the colour it started with, and it says so.
        Assert.Equal("\x1b]11;rgb:0000/1919/1111\x1b\\", replies.ToString());
    }

    [Fact]
    public void TheClipboardSequenceIsNotAnswered()
    {
        // OSC 52 lets the host at the far end of a connection READ this machine's clipboard. That
        // is the person at the keyboard's decision, not something to switch on because the
        // sequence exists.
        var emulator = Build();
        var replies = CaptureReplies(emulator);

        Feed(emulator, "\x1b]52;c;?\x1b\\");

        Assert.Equal("", replies.ToString());
    }

    [Fact]
    public void AParameterlessOscDoesNotUpsetTheParser()
    {
        // OSC 104 with no parameters resets the whole palette. It is not implemented, but it used
        // to be dropped before anything could even look at the command number, and the text after
        // it must still print.
        var emulator = Build();

        Feed(emulator, "\x1b]104\x1b\\OK");

        emulator.GetBuffer().TryGetCell(0, 0, out var first);
        emulator.GetBuffer().TryGetCell(0, 1, out var second);
        Assert.Equal('O', (char)first.Codepoint);
        Assert.Equal('K', (char)second.Codepoint);
    }
}
