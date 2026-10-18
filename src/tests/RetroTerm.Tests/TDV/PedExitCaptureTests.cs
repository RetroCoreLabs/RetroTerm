using System.Text;
using RetroTerm.Core.Configuration;
using RetroTerm.Core.Terminal.Emulators;
using RetroTerm.Core.Terminal.Emulators.TDV;
using Xunit;

namespace RetroTerm.Tests.TDV;

/// <summary>
/// What real PED on real SINTRAN sends a TDV2200 when it EXITS.
/// </summary>
/// <remarks>
/// <para><b>Where these bytes came from</b></para>
/// A live telnet session to D100 on 28 August 2026, same machine and same emulator as
/// <c>PedStartupCaptureTests</c>. Trace entries 288 and 289, taken as PED shut down after the
/// HOME-E-N command.
///
/// <para><b>Why it is a separate test</b></para>
/// The 20 August session captured PED's STARTUP and stopped there, so its closing ritual had
/// never been looked at. It is not the startup ritual run backwards: it carries a sequence that
/// appears nowhere in the startup capture and nowhere else in this suite.
///
/// <code>
/// 1B 5B 31 3B 32 3B 33 3B 34 52      ESC [ 1;2;3;4 R
/// 1B 5B 36 36 3B 36 32 3B 38 30 6C   ESC [ 66;62;80 l
/// </code>
///
/// <para><b>The R is the find</b></para>
/// A CSI ending in R is CPR, the cursor position REPORT - something a terminal sends a host, not
/// the other way round, and CPR carries two parameters rather than four. So this is a TDV command
/// that happens to share the final byte, and what it means is not known here. No manual held in
/// this repository lists it. It is counted, not implemented, which is the same answer mode 62 got.
///
/// <para><b>Mode 66 is the second new thing</b></para>
/// The startup reset 30, 7 and 80; the exit resets 66, 62 and 80. Mode 66 appears in a real
/// TDV2200 termcap's initialisation string as well - <c>ESC [ 62;36;66 l</c> in the <c>is</c>
/// capability - so the exit ritual is closer to the termcap's than the startup one is. What mode
/// 66 DOES is, again, not stated by anything held here.
/// </remarks>
public class PedExitCaptureTests
{
    /// <summary>
    /// The escape character, built from its code so no raw control byte lands in this file.
    /// </summary>
    private const char Escape = (char)0x1B;

    /// <summary>
    /// PED's closing ritual, byte for byte as captured.
    /// </summary>
    /// <returns>
    /// The two sequences, in the order they arrived.
    /// </returns>
    private static string PedClosingRitual()
    {
        var text = new StringBuilder();

        text.Append(Escape).Append("[1;2;3;4R");
        text.Append(Escape).Append("[66;62;80l");

        return text.ToString();
    }

    /// <summary>
    /// Builds a TDV2200 at the geometry a real one has and feeds it PED's closing ritual.
    /// </summary>
    /// <returns>
    /// The emulator, after the ritual.
    /// </returns>
    private static TerminalEmulatorBase TdvAfterPedExit()
    {
        var emulator = EmulatorFactory.CreateEmulator("TDV2200", 80, 25, 100);
        emulator.ProcessData(Encoding.ASCII.GetBytes(PedClosingRitual()));
        return emulator;
    }

    [Fact]
    public void EveryModeOfAListIsRoutedEvenWhenAnNdModeLeadsIt()
    {
        // The defect this file found. PED's exit sends CSI 66;62;80 l. Modes 66 and 80 are TDV
        // modes and ARE handled; 62 is not, and must reach the ordinary path, which counts it.
        // Before the fix the whole sequence was claimed by the ND handler on the strength of its
        // FIRST parameter, and 62 and 80 vanished - neither acted on nor counted.
        //
        // The numbers sent one at a time were always counted correctly, which is what made the
        // loss invisible: every test in the suite sent them singly.
        //
        // MODES 80 AND 62 BOTH MOVED FROM COUNTED TO HANDLED on 11 September 2026. 80 is the
        // numeric pad Function/Numeric switch, TDV 2200/9 S User's Guide section 11.2; 62 is GRM,
        // the Graphic Rendition Mode switch, 2215 section 8.7.1. Both were only ever counted here
        // because nothing in this program knew what they were. Reading the manuals is what turned
        // them into handled modes - which is exactly what the counter is for.
        //
        // So all three numbers of CSI 66;62;80 l are acted on now, and the routing this file was
        // written to prove is checked by their EFFECTS rather than by their counts.
        var emulator = TdvAfterPedExit();
        var unhandled = emulator.UnrecognisedSequences;

        Assert.False(unhandled.ContainsKey("mode 62 reset"), Keys(unhandled));
        Assert.False(unhandled.ContainsKey("mode 80 reset"), Keys(unhandled));

        // 62 reset is GRM to ATTR, and 80 reset is the numeric pad back to Numeric. Both are in
        // the middle and end of the list, which is where the old defect lost them.
        var tdv = emulator as TDVEmulatorBase;
        Assert.NotNull(tdv);
        Assert.Equal(TDVEmulatorBase.TDVGraphicRenditionMode.Attribute, tdv!.GraphicRenditionMode);
    }

    [Fact]
    public void ModeSixtySixIsStillHandledAndNotCounted()
    {
        // The other half of the same change: routing the rest of the list must not stop the ND
        // mode itself being handled. If 66 started being counted, the fix would have broken 2115
        // compatibility mode instead of repairing the list.
        var unhandled = TdvAfterPedExit().UnrecognisedSequences;

        Assert.False(unhandled.ContainsKey("mode 66 reset"), Keys(unhandled));
    }

    [Fact]
    public void AnOrdinaryModeListWithNoNdModeIsUntouched()
    {
        // The boundary of the change. A list carrying no TDV mode must behave exactly as before,
        // which for PED's STARTUP list was three counted numbers.
        //
        // It is TWO now, because mode 80 became a handled TDV mode on 11 September 2026 - the
        // numeric pad switch from TDV 2200/9 S User's Guide section 11.2. So this list DOES carry
        // a TDV mode after all, and the point it makes is now the other one: the two numbers that
        // are still nobody's are still counted, and the one that found an owner is not.
        var emulator = EmulatorFactory.CreateEmulator("TDV2200", 80, 25, 100);
        emulator.ProcessData(Encoding.ASCII.GetBytes(Escape + "[30;7;80l"));
        var unhandled = emulator.UnrecognisedSequences;

        Assert.Equal(2, unhandled.Count);
        Assert.True(unhandled.ContainsKey("mode 30 reset"), Keys(unhandled));
        Assert.True(unhandled.ContainsKey("mode 7 reset"), Keys(unhandled));
        Assert.False(unhandled.ContainsKey("mode 80 reset"), Keys(unhandled));
    }

    [Fact]
    public void InsertModeStillWorksBesideAnNdMode()
    {
        // IRM is mode 4, an ordinary ANSI mode with real behaviour behind it. A host that sets it
        // in the same list as an ND mode used to get overwrite instead of insert, silently. This
        // is the case where the lost mode changes what the screen does, not just what is counted.
        var emulator = EmulatorFactory.CreateEmulator("TDV2200", 80, 25, 100);
        emulator.ProcessData(Encoding.ASCII.GetBytes("ABC"));
        emulator.ProcessData(Encoding.ASCII.GetBytes(Escape + "[1;1H"));
        emulator.ProcessData(Encoding.ASCII.GetBytes(Escape + "[66;4h"));
        emulator.ProcessData(Encoding.ASCII.GetBytes("X"));

        // Insert pushes ABC right; overwrite would have replaced the A.
        Assert.Equal((uint)'X', emulator.Buffer.GetCell(0, 0).Codepoint);
        Assert.Equal((uint)'A', emulator.Buffer.GetCell(0, 1).Codepoint);
        Assert.Equal((uint)'B', emulator.Buffer.GetCell(0, 2).Codepoint);
        Assert.Equal((uint)'C', emulator.Buffer.GetCell(0, 3).Codepoint);
    }

    [Fact]
    public void TheFourParameterCsiRIsCounted()
    {
        // The whole reason this file exists. If a later change starts acting on it, or stops
        // counting it, this says so - and either would be worth knowing about.
        var unhandled = TdvAfterPedExit().UnrecognisedSequences;

        Assert.True(unhandled.ContainsKey("CSI R"), Keys(unhandled));
    }

    [Fact]
    public void TheCsiRDoesNotMoveTheCursor()
    {
        // A CSI ending in R that WAS read as something positional would move the cursor, and PED
        // sends this while shutting down - a cursor left in the wrong place would show up as the
        // SINTRAN prompt printing in the middle of the screen. It must be inert.
        var emulator = EmulatorFactory.CreateEmulator("TDV2200", 80, 25, 100);
        emulator.ProcessData(Encoding.ASCII.GetBytes(Escape + "[5;9H"));
        int rowBefore = emulator.Cursor.Row;
        int columnBefore = emulator.Cursor.Column;

        emulator.ProcessData(Encoding.ASCII.GetBytes(Escape + "[1;2;3;4R"));

        Assert.Equal(rowBefore, emulator.Cursor.Row);
        Assert.Equal(columnBefore, emulator.Cursor.Column);
    }

    [Fact]
    public void NothingElseInTheClosingRitualIsUnhandled()
    {
        // The boundary, same shape as the startup test's. ONE key now: the CSI R. Modes 66, 80 and
        // 62 are all absent because all three are handled - 66 always was, and 80 and 62 joined it
        // on 11 September 2026 when the manuals said what they were. A second key is either a
        // regression or a discovery, and both deserve a look.
        var unhandled = TdvAfterPedExit().UnrecognisedSequences;

        Assert.Single(unhandled);
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
