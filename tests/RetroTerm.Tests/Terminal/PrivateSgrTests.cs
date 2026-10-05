using System.Text;
using RetroTerm.Core.Configuration;
using RetroTerm.Core.Terminal.Buffer;
using RetroTerm.Core.Terminal.Emulators;
using Xunit;

namespace RetroTerm.Tests.Terminal;

/// <summary>
/// CSI sequences that END in 'm' but are not SGR.
///
/// <c>CSI &gt; 4 ; 2 m</c> is xterm's modifyOtherKeys setting, and Claude Code, vim and other
/// full-screen programs send it at startup. 'm' with a private marker ('&gt;', '?', '=', '&lt;') is
/// never Select Graphic Rendition, but it was dispatched as one: the 4 turned on underline and the
/// 2 turned on dim, so everything the program drew afterwards was underlined.
/// </summary>
public class PrivateSgrTests
{
    private static TerminalEmulatorBase Build()
        => EmulatorFactory.CreateEmulator("xterm-256color", 80, 24, 100);

    private static void Feed(TerminalEmulatorBase emulator, string text)
        => emulator.ProcessData(Encoding.ASCII.GetBytes(text));

    [Theory]
    [InlineData("\x1b[>4;2m")]   // XTMODKEYS: enable modifyOtherKeys level 2
    [InlineData("\x1b[>4m")]
    [InlineData("\x1b[>4;0m")]   // XTMODKEYS reset
    [InlineData("\x1b[?4m")]
    [InlineData("\x1b[=4m")]
    [InlineData("\x1b[<4m")]
    public void APrivateMarkedSequenceEndingInMIsNotSgr(string sequence)
    {
        var emulator = Build();

        Feed(emulator, sequence + "A");

        var attributes = emulator.Buffer.GetCell(0, 0).Attributes;
        Assert.False(attributes.HasAttribute(CharacterAttributes.Underline));
        Assert.False(attributes.HasAttribute(CharacterAttributes.Dim));
    }

    [Fact]
    public void PlainSgrStillWorksAfterThePrivateOne()
    {
        var emulator = Build();

        Feed(emulator, "\x1b[>4;2m\x1b[4mA\x1b[24mB");

        Assert.True(emulator.Buffer.GetCell(0, 0).Attributes.HasAttribute(CharacterAttributes.Underline));
        Assert.False(emulator.Buffer.GetCell(0, 1).Attributes.HasAttribute(CharacterAttributes.Underline));
    }
}
