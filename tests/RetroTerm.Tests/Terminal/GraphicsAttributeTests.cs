using System.Text;
using RetroTerm.Core.Configuration;
using RetroTerm.Core.Terminal.Emulators;
using Xunit;

namespace RetroTerm.Tests.Terminal;

/// <summary>
/// XTSMGRAPHICS - <c>CSI ? Pi ; Pa ; Pv S</c>, the question a Sixel host asks first.
/// </summary>
/// <remarks>
/// <para><b>Why this one matters more than its size suggests</b></para>
/// A Sixel-aware host asks it BEFORE sending an image: how many colour registers may I use, and how
/// big is the graphics area. Without an answer an encoder either guesses or refuses - so a terminal
/// that draws Sixel perfectly can still be sent nothing at all. It was missing, and it is the
/// sequence that stands between this emulator's decoder and `img2sixel` over a real connection.
///
/// <para><b>The form</b></para>
/// From xterm's ctlseqs, held at spec\DEC. The reply has the same shape as the request,
/// <c>CSI ? Pi ; Ps ; Pv S</c>, with Ps the status: 0 success, 1 error in Pi, 2 error in Pa,
/// 3 failure.
/// </remarks>
public class GraphicsAttributeTests
{
    private static readonly string Esc = ((char)0x1B).ToString();

    private static TerminalEmulatorBase Build(string type = "VT340")
        => EmulatorFactory.CreateEmulator(type, 80, 24, 100);

    private static string Ask(TerminalEmulatorBase emulator, string sequence)
    {
        var replies = new StringBuilder();
        emulator.DataToSend += bytes => replies.Append(Encoding.ASCII.GetString(bytes));

        emulator.ProcessData(Encoding.ASCII.GetBytes(Esc + sequence));
        return replies.ToString();
    }

    [Fact]
    public void ItReportsHowManyColourRegistersThereAre()
    {
        // "Pi = 1 -> item is number of color registers", "Pa = 1 -> read attribute".
        var emulator = Build();

        Assert.Equal(Esc + "[?1;0;256S", Ask(emulator, "[?1;1S"));
    }

    [Fact]
    public void AndReadingTheMaximumGivesTheSameAnswer()
    {
        // "Pa = 4 -> read the maximum allowed value." The number is fixed here, so the current
        // value IS the maximum - and saying so is more useful than refusing.
        var emulator = Build();

        Assert.Equal(Esc + "[?1;0;256S", Ask(emulator, "[?1;4S"));
    }

    [Fact]
    public void ItReportsTheSixelGeometry()
    {
        // "Pi = 2 -> item is Sixel graphics geometry (in pixels)." 800 by 480 is the VT340's
        // graphics space, which is what this emulator's plane is.
        var emulator = Build();

        Assert.Equal(Esc + "[?2;0;800;480S", Ask(emulator, "[?2;1S"));
    }

    [Fact]
    public void AndTheReGisGeometry()
    {
        // "Pi = 3 -> item is ReGIS graphics geometry (in pixels)." One plane serves both.
        var emulator = Build();

        Assert.Equal(Esc + "[?3;0;800;480S", Ask(emulator, "[?3;1S"));
    }

    [Fact]
    public void SettingIsRefusedTruthfully()
    {
        // xterm refuses it too: "The current implementation allows reading the graphics sizes, but
        // disallows modifying those sizes." Here the reason is that the plane is the hardware's
        // graphics space - a host resizing it would be resizing a VT340. Status 3 is "failure",
        // which is a truthful no rather than a silent one.
        var emulator = Build();

        Assert.Equal(Esc + "[?1;3S", Ask(emulator, "[?1;3;64S"));
        Assert.Equal(Esc + "[?2;3S", Ask(emulator, "[?2;2S"));
    }

    [Fact]
    public void AnItemItDoesNotKnowIsAnError()
    {
        // "Ps = 1 <- error in Pi." Answered, not ignored: a host waiting for a reply would
        // otherwise wait for ever.
        var emulator = Build();

        Assert.Equal(Esc + "[?9;1S", Ask(emulator, "[?9;1S"));
    }

    [Fact]
    public void AndSoIsAnActionItDoesNotKnow()
    {
        // "Ps = 2 <- error in Pa."
        var emulator = Build();

        Assert.Equal(Esc + "[?1;2S", Ask(emulator, "[?1;9S"));
    }

    [Fact]
    public void ATerminalWithNoGraphicsSaysNothing()
    {
        // The sequence is xterm's, and it only means anything on a terminal "configured to support
        // either Sixel Graphics or ReGIS Graphics". A VT220 has neither, and answering would tell
        // a host it could send an image.
        var emulator = Build("VT220");

        Assert.Equal("", Ask(emulator, "[?1;1S"));
    }

    [Fact]
    public void ItIsNotMistakenForScrollUp()
    {
        // SU is CSI Ps S with no private marker. The '?' is the whole difference, and reading one
        // as the other would scroll the screen when a host asked a question.
        var emulator = Build();
        emulator.ProcessData(Encoding.ASCII.GetBytes(Esc + "[1;1HA"));

        Ask(emulator, "[?1;1S");

        emulator.GetBuffer().TryGetCell(0, 0, out var cell);
        Assert.Equal((uint)'A', cell.Codepoint);
    }
}
