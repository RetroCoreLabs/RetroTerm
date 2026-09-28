using System.Collections.Generic;
using System.Text;
using RetroTerm.Core.Configuration;
using RetroTerm.Core.Terminal.Emulators;
using Xunit;

namespace RetroTerm.Tests.Terminal;

/// <summary>
/// The ECMA-48 cursor-position family that was missing.
///
/// CUP, CUU, CUD, CUF, CUB, CHA and VPA were implemented. Their five siblings — HPA, HPR, HPB,
/// VPR and VPB — were not, so <c>ESC[5`</c> and friends did nothing whatsoever: the CSI was
/// parsed, fell off the end of the switch, and the cursor stayed put. Nothing DEC-private about
/// any of them; they are plain ECMA-48 and cost one line each.
///
/// Found by libvterm's 11state_movecursor script — see tests\RetroTerm.Tests\Conformance.
/// </summary>
public class Ecma48PositionSequenceTests
{
    /// <summary>
    /// Run these on every terminal the factory builds, not just the one that found it.
    /// </summary>
    public static IEnumerable<object[]> AllEmulators()
    {
        var types = EmulatorFactory.AvailableEmulators;
        for (int i = 0; i < types.Length; i++)
        {
            yield return new object[] { types[i] };
        }
        yield return new object[] { "ANSI" };
    }

    private static TerminalEmulatorBase Build(string type) => EmulatorFactory.CreateEmulator(type, 80, 25, 100);

    private static void Feed(TerminalEmulatorBase emulator, string text)
        => emulator.ProcessData(Encoding.ASCII.GetBytes(text));

    [Theory]
    [MemberData(nameof(AllEmulators))]
    public void Hpa_MovesToAnAbsoluteColumn(string emulatorType)
    {
        var emulator = Build(emulatorType);
        Feed(emulator, "\u001b[5`");            // HPA: column 5, one-based

        Assert.Equal(4, emulator.GetCursor().Column);
        Assert.Equal(0, emulator.GetCursor().Row);
    }

    [Theory]
    [MemberData(nameof(AllEmulators))]
    public void Hpr_MovesRightByAnAmount(string emulatorType)
    {
        var emulator = Build(emulatorType);
        Feed(emulator, "\u001b[5`");
        Feed(emulator, "\u001b[3a");            // HPR: three columns right of 4

        Assert.Equal(7, emulator.GetCursor().Column);
    }

    [Theory]
    [MemberData(nameof(AllEmulators))]
    public void Hpb_MovesLeftByAnAmount(string emulatorType)
    {
        var emulator = Build(emulatorType);
        Feed(emulator, "\u001b[8`");
        Feed(emulator, "\u001b[3j");            // HPB: three columns left of 7

        Assert.Equal(4, emulator.GetCursor().Column);
    }

    [Theory]
    [MemberData(nameof(AllEmulators))]
    public void Vpr_MovesDownByAnAmount(string emulatorType)
    {
        var emulator = Build(emulatorType);
        Feed(emulator, "\u001b[5;3H");          // row 4, column 2
        Feed(emulator, "\u001b[2e");            // VPR: two rows down

        Assert.Equal(6, emulator.GetCursor().Row);
        Assert.Equal(2, emulator.GetCursor().Column);   // column must not move
    }

    [Theory]
    [MemberData(nameof(AllEmulators))]
    public void Vpb_IsIgnored(string emulatorType)
    {
        // ECMA-48 8.3.159 defines CSI Pn k as VPB, and this used to move the cursor up. No
        // terminal emulated here has it: xterm's ctlseqs has no entry for CSI Ps k, and DEC's
        // VT330/VT340 programming manual never mentions VPB. Two xterm.js screen fixtures show
        // a real xterm doing nothing at all with it, so "d ESC[k e ESC[k f" comes out as one
        // line of text rather than four rows of one letter each.
        var emulator = Build(emulatorType);
        Feed(emulator, "\u001b[7;3H");          // row 6, column 2
        Feed(emulator, "\u001b[2k");            // and nothing happens

        Assert.Equal(6, emulator.GetCursor().Row);      // unmoved
        Assert.Equal(2, emulator.GetCursor().Column);
    }

    [Theory]
    [MemberData(nameof(AllEmulators))]
    public void TheyDefaultToOne(string emulatorType)
    {
        // ECMA-48 default for an omitted parameter is 1, not 0. A missing default would leave
        // the cursor motionless and look exactly like the sequence not being implemented at all.
        var emulator = Build(emulatorType);
        Feed(emulator, "\u001b[5;5H");

        Feed(emulator, "\u001b[a");
        Assert.Equal(5, emulator.GetCursor().Column);

        Feed(emulator, "\u001b[j");
        Assert.Equal(4, emulator.GetCursor().Column);

        Feed(emulator, "\u001b[e");
        Assert.Equal(5, emulator.GetCursor().Row);

        Feed(emulator, "\u001b[k");
        // CSI k has no default because it has no effect at all - see Vpb_IsIgnored.
        Assert.Equal(5, emulator.GetCursor().Row);
    }

    [Theory]
    [MemberData(nameof(AllEmulators))]
    public void ErasingToEndOfLineCancelsTheContinuation(string emulatorType)
    {
        // A line that filled up and spilled over carries a continuation flag, so a screen-to-text
        // read joins it to the next line. Erasing to the end of that line ENDS it — whatever
        // spilled over is no longer a continuation of anything, and the flag has to go with it.
        var emulator = Build(emulatorType);
        Feed(emulator, new string('D', 100));   // 80 fill row 0, 20 spill onto row 1

        Assert.True(emulator.GetBuffer().IsLineWrapped(0), $"{emulatorType}: row 0 should continue");

        Feed(emulator, "\u001b[1;79H\u001b[K"); // back to row 0, near the end, erase to end of line

        Assert.False(emulator.GetBuffer().IsLineWrapped(0),
            $"{emulatorType}: EL should have ended row 0");
    }
}
