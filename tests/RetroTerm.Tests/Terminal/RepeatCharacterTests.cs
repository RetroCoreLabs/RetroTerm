using System.Text;
using RetroTerm.Core.Configuration;
using RetroTerm.Core.Terminal.Emulators;
using Xunit;

namespace RetroTerm.Tests.Terminal;

/// <summary>
/// REP - ECMA-48's repeat, and the two rules about it that a specification alone does not give.
/// </summary>
/// <remarks>
/// ECMA-48 8.3.103 defines CSI Pn b as REPEAT, and xterm's ctlseqs lists it as
/// "Repeat the preceding graphic character Ps times". Neither says what counts as "preceding" once
/// something else has happened in between, and that is the whole of the difficulty.
///
/// The xterm.js fixture t0040-REP was captured from a real xterm and settles it: a control OR a
/// control sequence ends the run, so the repeat has nothing left to work on. In the fixture,
/// "abcdefg" then CSI 3 D then CSI b comes out as "abcdefg" - unchanged. A terminal that kept the
/// character would have printed an eighth letter into the middle of the word.
///
/// The fixture also settles the default: CSI 0 b repeats ONCE, not zero times.
/// </remarks>
public class RepeatCharacterTests
{
    private const string Esc = "";

    private static TerminalEmulatorBase Build(int width = 20, int height = 5)
        => EmulatorFactory.CreateEmulator("XTERM", width, height, 100);

    private static void Feed(TerminalEmulatorBase emulator, string text)
        => emulator.ProcessData(Encoding.ASCII.GetBytes(text));

    /// <summary>
    /// Reads one row back as text, with a dot for every blank so a count is easy to make.
    /// </summary>
    private static string Row(TerminalEmulatorBase emulator, int row)
    {
        var buffer = emulator.GetBuffer();
        var sb = new StringBuilder(buffer.Width);
        for (int column = 0; column < buffer.Width; column++)
        {
            uint codepoint = buffer[row, column].Codepoint;
            sb.Append(codepoint == 0 || codepoint == ' ' ? '.' : (char)codepoint);
        }
        return sb.ToString();
    }

    [Fact]
    public void FiveRepeatsOfOneCharacterMakeSixOfThem()
    {
        // The fixture's first line: "x" then CSI 5 b gives "xxxxxx" - the original plus five.
        var emulator = Build();
        Feed(emulator, "x" + Esc + "[5b");

        Assert.Equal("xxxxxx..............", Row(emulator, 0));
    }

    [Fact]
    public void AnOmittedCountRepeatsOnce()
    {
        var emulator = Build();
        Feed(emulator, "y" + Esc + "[b");

        Assert.Equal("yy..................", Row(emulator, 0));
    }

    [Fact]
    public void AnExplicitZeroAlsoRepeatsOnce()
    {
        // ECMA-48's default for an omitted parameter is 1, and the fixture's last line shows a real
        // xterm treating an explicit 0 the same way: "?" CSI 0 b "-" comes out as "?" "?" "-".
        var emulator = Build();
        Feed(emulator, "?" + Esc + "[0b-");

        Assert.Equal("??-.................", Row(emulator, 0));
    }

    [Fact]
    public void ACursorMoveEndsTheRunSoThereIsNothingToRepeat()
    {
        // The fixture line that matters most: "abcdefg" CSI 3 D CSI b leaves the word alone.
        var emulator = Build();
        Feed(emulator, "abcdefg" + Esc + "[3D" + Esc + "[b");

        Assert.Equal("abcdefg.............", Row(emulator, 0));
    }

    [Fact]
    public void AndTheCursorHasNotMovedEither()
    {
        // If the repeat had printed, the character after it would land one column further on. The
        // fixture's next line proves it did not: the '!' overwrites the 'e'.
        var emulator = Build();
        Feed(emulator, "abcdefg" + Esc + "[3D" + Esc + "[b!");

        Assert.Equal("abcd!fg.............", Row(emulator, 0));
    }

    [Fact]
    public void ACarriageReturnAndLineFeedEndTheRunToo()
    {
        // The fixture's second line: after CR LF, a CSI 3 b at the start of the line repeats
        // nothing, and the '<' printed after it is alone on the row.
        var emulator = Build();
        Feed(emulator, "x\r\n" + Esc + "[3b<");

        Assert.Equal("<...................", Row(emulator, 1));
    }

    [Fact]
    public void ARepeatCarriesOnAcrossTheRightMargin()
    {
        // The fixture's fifth line, in miniature: a character near the end of the row plus a repeat
        // long enough to run off it. The wrap has to happen the way it would for typed text.
        var emulator = Build(width: 10);
        Feed(emulator, Esc + "[1;9H@" + Esc + "[5b");

        Assert.Equal("........@@", Row(emulator, 0));
        Assert.Equal("@@@@......", Row(emulator, 1));
    }

    [Fact]
    public void TheRepeatedCharacterCarriesTheCurrentAttributes()
    {
        // REP prints, so it prints the way a character would print now - not the way the original
        // one did. Nothing in the fixture covers this; it follows from the sequence being a print.
        var emulator = Build();
        Feed(emulator, Esc + "[31mR" + Esc + "[3b");

        var buffer = emulator.GetBuffer();
        Assert.Equal(buffer[0, 0].Foreground, buffer[0, 3].Foreground);
    }

    [Fact]
    public void ARepeatWithNothingBeforeItDoesNothing()
    {
        // A host that starts a session with CSI 5 b has printed nothing, so there is nothing to
        // repeat. Printing a NUL or a space five times here would be an invention.
        var emulator = Build();
        Feed(emulator, Esc + "[5b");

        Assert.Equal("....................", Row(emulator, 0));
        Assert.Equal(0, emulator.GetCursor().Column);
    }

    [Fact]
    public void TwoRepeatsInARowBothRepeatTheSameCharacter()
    {
        // No fixture covers this. It follows from REP being defined as repeating the preceding
        // GRAPHIC character: the repeat prints that character, so it is still the preceding one.
        var emulator = Build();
        Feed(emulator, "z" + Esc + "[2b" + Esc + "[2b");

        Assert.Equal("zzzzz...............", Row(emulator, 0));
    }
}
