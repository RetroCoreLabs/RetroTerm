using System.Text;
using RetroTerm.Core.Configuration;
using RetroTerm.Core.Terminal.Emulators;
using RetroTerm.Core.Terminal.Emulators.TDV;
using Xunit;

namespace RetroTerm.Tests.TDV;

/// <summary>
/// What real PED on real SINTRAN actually sends a TDV2200 when it starts.
/// </summary>
/// <remarks>
/// <para><b>Where these bytes came from</b></para>
/// Captured 20 August 2026 from a live telnet session to D100 (ND-100 under RetroCore, real
/// SINTRAN III VSX/500), with the terminal type set by <c>set-term-type,,93</c> and the trace
/// started immediately before the <c>ped</c> command. They are a transcript, not a reading of a
/// manual, and not a guess. Trace entries 764 to 772.
/// <para><b>Why it is worth a test</b></para>
/// Every other TDV test in this folder asserts what a document says a TDV does. This one asserts
/// what a host that has driven real Tandberg terminals since 1983 actually puts on the wire. If a
/// future change makes the emulator stop understanding PED's opening handshake, this goes red for
/// a reason that matters, and no amount of specification reading would have caught it.
/// <para><b>The shape is corroborated</b></para>
/// A real TDV2200 termcap's <c>is</c> (initialisation) string is
/// <c>\E[62;36;66l \EQ \E[36;62;62h \E[0m</c> - see the <c>is</c> capability in
/// <c>docs\TDV2200 TERMCAP REFERENCE.md</c>. PED sends the same three-part ritual with different
/// numbers: an <c>ESC Q</c>, a reset list, then a set list. Mode 62 appears in both sources.
/// What neither source states is what mode 62 DOES, so nothing here asserts a behaviour for it -
/// it is counted as unhandled, which is the honest answer.
/// </remarks>
public class PedStartupCaptureTests
{
    /// <summary>
    /// The escape character, built from its code so no raw control byte lands in this file.
    /// </summary>
    private const char Escape = (char)0x1B;

    /// <summary>
    /// PED's opening handshake, byte for byte as captured.
    /// </summary>
    /// <remarks>
    /// Written as escapes plus text rather than a hex blob so a reader can see what it is:
    ///  - <c>ESC Q</c> - leave 2115 compatibility mode.
    ///  - <c>ESC [ 30;7;80 l</c> - RM, three mode numbers off.
    ///  - <c>ESC [ 62;62 h</c> - SM, mode 62 on, listed twice.
    ///  - four device control strings carrying <c>L10</c> through <c>L40</c>.
    ///  - <c>ESC [ 001;001 H</c> - CUP with zero-padded parameters, which is how PED writes them.
    ///  - <c>ESC [ 2J</c> - clear the screen.
    /// </remarks>
    private static string PedOpeningHandshake()
    {
        var text = new StringBuilder();

        text.Append(Escape).Append('Q');
        text.Append(Escape).Append("[30;7;80l");
        text.Append(Escape).Append("[62;62h");
        text.Append(Escape).Append("PL10").Append(Escape).Append('\\');
        text.Append(Escape).Append("PL20").Append(Escape).Append('\\');
        text.Append(Escape).Append("PL30").Append(Escape).Append('\\');
        text.Append(Escape).Append("PL40").Append(Escape).Append('\\');
        text.Append(Escape).Append("[001;001H");
        text.Append(Escape).Append("[2J");

        return text.ToString();
    }

    /// <summary>
    /// Builds a TDV2200 at the geometry a real one has.
    /// </summary>
    /// <returns>
    /// The emulator, already fed PED's opening handshake.
    /// </returns>
    /// <remarks>
    /// 80 by 25, not 80 by 24. PED addresses row 25 for its status line - trace entry 786 is
    /// <c>ESC [ 25;1 H</c> - so a 24-row terminal simply loses that line with nothing looking
    /// broken. That cost the first attempt at this session.
    /// </remarks>
    private static TerminalEmulatorBase TdvAfterPedStartup()
    {
        var emulator = EmulatorFactory.CreateEmulator("TDV2200", 80, 25, 100);
        emulator.ProcessData(Encoding.ASCII.GetBytes(PedOpeningHandshake()));
        return emulator;
    }

    [Fact]
    public void TheEscapeQIsUnderstood()
    {
        // The correction that cost a turn on 20 August: I read the trace decoder's "UNKNOWN" label
        // and reported ESC Q as a gap. It is not - TDV2200Emulator.cs handles it as "leave 2115
        // compatibility mode". The label means "this DECODER does not know it", never "the terminal
        // ignored it". This test pins the difference so the mistake cannot be made from the code.
        var unhandled = TdvAfterPedStartup().UnrecognisedSequences;

        Assert.False(unhandled.ContainsKey("ESC Q"));
    }

    [Fact]
    public void TheModeNumbersPedUsesAreCountedWithTheirNumbers()
    {
        // SM and RM are perfectly well known. The MODE NUMBERS are the unknown part, which is why
        // this one counter keeps its parameters - "CSI h" would name nothing worth building.
        //
        // TWO NUMBERS HAVE COME OFF THIS LIST, and both by the same route - the counter named a
        // number for long enough that somebody went and looked it up.
        //
        // Mode 80 went on 11 September 2026: the numeric pad Function/Numeric switch, TDV 2200/9 S
        // User's Guide section 11.2. Mode 62 went the same day: GRM, the Graphic Rendition Mode
        // switch, 2215 section 8.7.1, three positions - ATTR, UNDERLINE, SGR.
        //
        // What is left is mode 7, Vertical Editing Mode, which is known and not implemented, and
        // mode 30, which is in no manual held here.
        var unhandled = TdvAfterPedStartup().UnrecognisedSequences;

        Assert.True(unhandled.ContainsKey("mode 30 reset"), Keys(unhandled));
        Assert.True(unhandled.ContainsKey("mode 7 reset"), Keys(unhandled));
        Assert.False(unhandled.ContainsKey("mode 80 reset"), Keys(unhandled));
        Assert.False(unhandled.ContainsKey("mode 62 set"), Keys(unhandled));
    }

    /// <summary>
    /// PED lists mode 62 twice in one sequence, and the switch walks twice.
    /// </summary>
    /// <remarks>
    /// <c>ESC [ 62;62 h</c> really does repeat itself. This used to assert that the number was
    /// COUNTED twice, which was the right test while GRM was unimplemented. Now that the switch
    /// exists the repetition has a visible effect: GRM has three positions and each SM advances
    /// one, so two in a row walk ATTR to UNDERLINE to SGR. A handler that treated the second as a
    /// duplicate of the first would stop a position short.
    /// </remarks>
    [Fact]
    public void ModeSixtyTwoTwiceInOneSequenceWalksTheSwitchTwice()
    {
        var emulator = EmulatorFactory.CreateEmulator("TDV2200", 80, 25, 100) as TDVEmulatorBase;
        Assert.NotNull(emulator);

        // Start from the bottom of the walk, where PED's own reset list leaves it.
        emulator!.ProcessData(Encoding.ASCII.GetBytes(Escape + "[62l"));
        Assert.Equal(TDVEmulatorBase.TDVGraphicRenditionMode.Attribute, emulator.GraphicRenditionMode);

        emulator.ProcessData(Encoding.ASCII.GetBytes(Escape + "[62;62h"));

        Assert.Equal(TDVEmulatorBase.TDVGraphicRenditionMode.Sgr, emulator.GraphicRenditionMode);
    }

    [Fact]
    public void TheFourDeviceControlStringsAreCounted()
    {
        // Until these were counted, four whole payloads vanished in silence. A DCS carries DATA, so
        // what was dropped is the content - "L10" through "L40" - as well as the command.
        var unhandled = TdvAfterPedStartup().UnrecognisedSequences;

        Assert.Equal(4, unhandled["DCS L"]);
    }

    [Fact]
    public void TheCursorPositionWithPaddedParametersIsActedOn()
    {
        // PED writes its parameters zero-padded - ESC [ 001;001 H, not ESC [ 1;1 H. A parser that
        // read those three digits as anything but 1 would put the whole screen in the wrong place,
        // and nothing else in this suite feeds it a padded parameter.
        var emulator = EmulatorFactory.CreateEmulator("TDV2200", 80, 25, 100);
        emulator.ProcessData(Encoding.ASCII.GetBytes(Escape + "[010;007H"));

        Assert.Equal(9, emulator.Cursor.Row);
        Assert.Equal(6, emulator.Cursor.Column);
    }

    [Fact]
    public void NothingElseInTheHandshakeIsUnhandled()
    {
        // The boundary, and the reason this test is worth having at all. If a future change breaks
        // CUP, ED or the DCS framing, the count grows and this goes red naming the newcomer.
        //
        // THREE keys now: mode 30, mode 7 and DCS L. It was five until mode 80 was looked up in the
        // 2200 manual on 11 September 2026, then four, and then three when mode 62 turned out to be
        // GRM in the 2215's own table. The number in this assertion is a measure of how much of a
        // real PED handshake this program still does not understand, and it is meant to fall.
        var unhandled = TdvAfterPedStartup().UnrecognisedSequences;

        Assert.Equal(3, unhandled.Count);
    }

    /// <summary>
    /// Renders the counted keys for an assertion message.
    /// </summary>
    /// <param name="unhandled">
    /// What the emulator counted.
    /// </param>
    /// <returns>
    /// The keys, comma separated.
    /// </returns>
    private static string Keys(System.Collections.Generic.IReadOnlyDictionary<string, int> unhandled)
    {
        var text = new StringBuilder("counted: ");
        bool first = true;

        // No LINQ and no foreach - the dictionary is walked with its own enumerator by hand.
        var walker = unhandled.GetEnumerator();
        while (walker.MoveNext())
        {
            if (!first)
            {
                text.Append(", ");
            }

            text.Append(walker.Current.Key).Append(" x").Append(walker.Current.Value);
            first = false;
        }

        return text.ToString();
    }
}
