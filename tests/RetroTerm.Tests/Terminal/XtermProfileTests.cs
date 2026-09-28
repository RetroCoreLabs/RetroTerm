using System.Text;
using RetroTerm.Core.Configuration;
using RetroTerm.Core.Terminal.Emulators;
using RetroTerm.Core.Terminal.Profiles;
using Xunit;

namespace RetroTerm.Tests.Terminal;

/// <summary>
/// xterm as a terminal you can pick, rather than a pile of modes only reachable from the VT100
/// entry.
///
/// The name is the point. It goes out over TERMINAL-TYPE negotiation and becomes TERM on the host,
/// which is what picks the terminfo entry - so xterm-256color is how a program on the far side
/// learns it may use 256 colours instead of eight.
/// </summary>
public class XtermProfileTests
{
    private static TerminalEmulatorBase Build(string type)
        => EmulatorFactory.CreateEmulator(type, 80, 24, 100);

    private static void Feed(TerminalEmulatorBase emulator, string text)
        => emulator.ProcessData(Encoding.ASCII.GetBytes(text));

    private static StringBuilder CaptureReplies(TerminalEmulatorBase emulator)
    {
        var replies = new StringBuilder();
        emulator.DataToSend += bytes => replies.Append(Encoding.ASCII.GetString(bytes));
        return replies;
    }

    [Theory]
    [InlineData("XTERM")]
    [InlineData("XTERM-256COLOR")]
    public void BothAreSelectable(string type)
    {
        Assert.True(EmulatorFactory.IsSupported(type));

        var emulator = Build(type);
        Assert.Equal(80, emulator.Width);
        Assert.Equal(24, emulator.Height);
    }

    [Fact]
    public void TheNamesAreSpelledTheWayTerminfoSpellsThem()
    {
        // Lower case and hyphenated. A host looking up "XTERM-256COLOR" finds nothing.
        Assert.Equal("xterm", Build("XTERM").GetTerminalType());
        Assert.Equal("xterm-256color", Build("XTERM-256COLOR").GetTerminalType());
    }

    [Fact]
    public void ItIdentifiesAsWhatItCanActuallyDo()
    {
        // NOT the VT420-class string a real xterm answers with. That one begins 41 in the secondary
        // reply and tells a host it may send left and right margins and the rectangle operations,
        // none of which exist here.
        var emulator = Build("XTERM");
        var replies = CaptureReplies(emulator);

        Feed(emulator, "\x1b[c");
        Feed(emulator, "\x1b[>c");

        Assert.Equal("\x1b[?1;2c\x1b[>0;10;0c", replies.ToString());
    }

    [Fact]
    public void TheXtermExtensionsAreAllThereOnIt()
    {
        // The claim the name makes, checked rather than asserted in a comment.
        var emulator = Build("XTERM-256COLOR");
        var replies = CaptureReplies(emulator);

        Feed(emulator, "\x1b[?2004h");   // bracketed paste
        Feed(emulator, "\x1b[?1004h");   // focus reporting
        Feed(emulator, "\x1b[?1000h");   // mouse tracking
        Feed(emulator, "\x1b[?1006h");   // SGR encoding
        Feed(emulator, "\x1b[?3h");      // host resize

        Assert.True(emulator.BracketedPasteMode);
        Assert.True(emulator.FocusReporting);
        Assert.True(emulator.Mouse.IsTracking);
        Assert.Equal(132, emulator.Width);

        // And the colour query, which is what a program uses to find the background.
        replies.Clear();
        Feed(emulator, "\x1b]11;?\x1b\\");
        Assert.StartsWith("\x1b]11;rgb:", replies.ToString());
    }

    [Fact]
    public void ItClaimsTheColourItActuallyHas()
    {
        Assert.True(TerminalProfile.Xterm256.Supports(TerminalFeatures.Colour256));
        Assert.True(TerminalProfile.Xterm256.Supports(TerminalFeatures.TrueColour));
        Assert.True(TerminalProfile.Xterm256.Supports(TerminalFeatures.HostResize));
    }

    [Fact]
    public void ATdvIsStillNotAnXterm()
    {
        // The guard against the modes leaking everywhere: a TDV2200 has no host-commanded resize,
        // and adding xterm to the list must not have given it one.
        var emulator = Build("TDV2200");

        Feed(emulator, "\x1b[?3h");

        Assert.Equal(80, emulator.Width);
    }
}
