using System.Text;
using RetroTerm.Core.Configuration;
using RetroTerm.Core.Terminal.Emulators;
using Xunit;

namespace RetroTerm.Tests.Terminal;

/// <summary>
/// The shifts - SS2, SS3, LS2 and LS3 - and DECID.
/// </summary>
/// <remarks>
/// <para><b>Designating a set was implemented; invoking one was not</b></para>
/// Found by walking xterm's ctlseqs list. <c>ESC ( ) * +</c> loaded G0 to G3, and nothing could
/// then reach G2 or G3 at all: a host that designated DEC Special Graphic as G2 and shifted to it
/// got whatever was already in GL. So the line-drawing set could be loaded and never used.
///
/// SS2 and SS3 last for EXACTLY ONE character, which is what separates them from LS2 and LS3. A
/// host uses them to reach one glyph out of another set without disturbing what it was using.
///
/// <para><b>What is deliberately still missing</b></para>
/// LS1R, LS2R and LS3R - <c>ESC ~</c>, <c>ESC }</c> and <c>ESC |</c> - invoke a set as GR rather
/// than GL. There is no GR here: a cell carries one character-set number and the high half of the
/// code table is not mapped separately. Adding the sequences without the concept would be a
/// terminal that accepted the command and ignored it.
/// </remarks>
public class ShiftAndIdentifyTests
{
    private static readonly string Esc = ((char)0x1B).ToString();

    private static TerminalEmulatorBase Build(string type = "VT420")
        => EmulatorFactory.CreateEmulator(type, 20, 5, 100);

    private static void Feed(TerminalEmulatorBase emulator, string text)
        => emulator.ProcessData(Encoding.ASCII.GetBytes(text));

    /// <summary>
    /// The character-set number stamped on one cell.
    /// </summary>
    /// <param name="emulator">
    /// The terminal to read.
    /// </param>
    /// <param name="column">
    /// Which column of the top row.
    /// </param>
    /// <returns>
    /// The set number the cell was written with.
    /// </returns>
    private static byte SetOf(TerminalEmulatorBase emulator, int column)
    {
        emulator.GetBuffer().TryGetCell(0, column, out var cell);
        return cell.CharacterSet;
    }

    [Fact]
    public void SingleShiftTwoReachesG2ForOneCharacterOnly()
    {
        var emulator = Build();
        Feed(emulator, Esc + "*0");            // DEC Special Graphic into G2

        Feed(emulator, "a" + Esc + "Nb" + "c");

        Assert.Equal(0, SetOf(emulator, 0));   // ASCII
        Assert.Equal(2, SetOf(emulator, 1));   // the shifted one
        Assert.Equal(0, SetOf(emulator, 2));   // and straight back
    }

    [Fact]
    public void SingleShiftThreeDoesTheSameForG3()
    {
        var emulator = Build();
        Feed(emulator, Esc + "+0");            // DEC Special Graphic into G3

        Feed(emulator, "a" + Esc + "Ob" + "c");

        Assert.Equal(0, SetOf(emulator, 0));
        Assert.Equal(2, SetOf(emulator, 1));
        Assert.Equal(0, SetOf(emulator, 2));
    }

    [Fact]
    public void LockingShiftTwoStays()
    {
        // THE difference between LS2 and SS2: this one does not wear off.
        var emulator = Build();
        Feed(emulator, Esc + "*0");

        Feed(emulator, Esc + "n" + "abc");

        Assert.Equal(2, SetOf(emulator, 0));
        Assert.Equal(2, SetOf(emulator, 1));
        Assert.Equal(2, SetOf(emulator, 2));
    }

    [Fact]
    public void AndSoDoesLockingShiftThree()
    {
        var emulator = Build();
        Feed(emulator, Esc + ")0");            // something else in G1, to prove it is not that
        Feed(emulator, Esc + "+0");

        Feed(emulator, Esc + "o" + "ab");

        Assert.Equal(2, SetOf(emulator, 0));
        Assert.Equal(2, SetOf(emulator, 1));
    }

    [Fact]
    public void ASingleShiftOverridesALockedSetForItsOneCharacter()
    {
        var emulator = Build();
        Feed(emulator, Esc + "*0");            // G2 is DEC Special Graphic
        Feed(emulator, Esc + "+B");            // G3 is ASCII
        Feed(emulator, Esc + "n");             // lock G2 in

        Feed(emulator, "a" + Esc + "Ob" + "c");

        Assert.Equal(2, SetOf(emulator, 0));   // locked G2
        Assert.Equal(0, SetOf(emulator, 1));   // the single shift to G3, which holds ASCII
        Assert.Equal(2, SetOf(emulator, 2));   // and back to the locked set
    }

    [Fact]
    public void IdentifyAnswersWithThePrimaryDeviceAttributes()
    {
        // "ESC Z: Return Terminal ID (DECID). Obsolete form of CSI c" - and obsolete is not the
        // same as unused. It is what a VT52-era host sends to ask what it is talking to.
        var emulator = Build();
        var replies = new StringBuilder();
        emulator.DataToSend += bytes => replies.Append(Encoding.ASCII.GetString(bytes));

        Feed(emulator, Esc + "Z");

        Assert.Equal(Esc + "[?64;1;6;7;8c", replies.ToString());
    }

    [Fact]
    public void SevenAndEightBitControlSelectionIsReadable()
    {
        // S7C1T and S8C1T - ESC SP F and ESC SP G. The state was already here, set by DECSCL's
        // second parameter; these two are the direct way to it and were missing.
        var emulator = Build();

        Feed(emulator, Esc + " G");
        Assert.True(emulator.Send8BitControls);

        Feed(emulator, Esc + " F");
        Assert.False(emulator.Send8BitControls);
    }
}
