using System.Text;
using RetroTerm.Core.Configuration;
using RetroTerm.Core.Terminal.Emulators;
using Xunit;

namespace RetroTerm.Tests.Terminal;

/// <summary>
/// DECNRCM - private mode 42, whether national replacement character sets are honoured.
/// </summary>
/// <remarks>
/// <para><b>What this mode can and cannot be seen through</b></para>
/// A VT300 answers to twelve national replacement sets. This program has a glyph table for exactly
/// one of them, British, so British is the only place the mode changes anything - and this file says
/// so rather than implying wider coverage than exists. No profile's DA reply claims capability 9,
/// so no host is being told otherwise either.
/// <para><b>The designator that moves</b></para>
/// <c>ESC ( A</c> means British NRCS with the mode set and ISO Latin-1 with it reset. That is the
/// documented meaning of the mode, not a choice made here.
/// <para><b>Honest limit on what a pass here proves</b></para>
/// British NRCS currently maps no codepoint differently from ASCII - <c>MapCharacterSetCharacter</c>
/// returns the codepoint unchanged for set 1 - so what these tests can show is that the right SET
/// NUMBER reaches the cell, which is what the renderer reads. They do not show a pound sign
/// appearing where a hash was typed, because today nothing in this program would make one.
/// </remarks>
public class DecNrcmTests
{
    /// <summary>
    /// The escape character, built from its code so no raw control byte lands in this file.
    /// </summary>
    private const char Escape = (char)0x1B;

    /// <summary>
    /// The character set number this program uses for ASCII and for ISO Latin-1.
    /// </summary>
    private const byte Ascii = 0;

    /// <summary>
    /// The character set number for British NRCS.
    /// </summary>
    private const byte British = 1;

    /// <summary>
    /// A fresh VT340.
    /// </summary>
    /// <returns>
    /// The emulator.
    /// </returns>
    private static TerminalEmulatorBase NewVt340()
    {
        return EmulatorFactory.CreateEmulator("VT340", 80, 24, 100);
    }

    /// <summary>
    /// Designates a set into G0, prints one character, and reports the set that character was
    /// stamped with.
    /// </summary>
    /// <param name="emulator">
    /// The terminal to drive.
    /// </param>
    /// <param name="designator">
    /// The final character of the designation sequence.
    /// </param>
    /// <returns>
    /// The character set number carried by the cell that was just written.
    /// </returns>
    /// <remarks>
    /// Read off a CELL rather than off the emulator's own G0 slot, which is protected and would in
    /// any case only prove that a field was assigned. This goes the whole way a real character does:
    /// designate, print, and see what the buffer ended up holding, which is what the renderer will
    /// later read.
    ///
    /// The cursor is homed first so every call looks at the same cell.
    /// </remarks>
    private static byte DesignateAndPrint(TerminalEmulatorBase emulator, char designator)
    {
        emulator.ProcessData(Encoding.ASCII.GetBytes(Escape + "[1;1H"));
        emulator.ProcessData(Encoding.ASCII.GetBytes(Escape + "(" + designator));
        emulator.ProcessData(Encoding.ASCII.GetBytes("#"));

        return emulator.GetBuffer().GetCell(0, 0).CharacterSet;
    }

    [Fact]
    public void BritishIsHonouredBeforeAnyHostSaysOtherwise()
    {
        // The power-on value, and a deliberate deviation: a real VT220 follows its set-up menu and
        // xterm defaults the mode off. Changing that while adding the mode would have arrived as
        // "the pound sign stopped working" rather than as a change anybody chose.
        Assert.Equal(British, DesignateAndPrint(NewVt340(), 'A'));
    }

    [Fact]
    public void WithTheModeResetTheSameDesignatorMeansLatinOne()
    {
        // The faithful half. 'A' in the 94-character position is two different designators and this
        // mode is what picks between them.
        var emulator = NewVt340();

        emulator.ProcessData(Encoding.ASCII.GetBytes(Escape + "[?42l"));

        Assert.Equal(Ascii, DesignateAndPrint(emulator, 'A'));
    }

    [Fact]
    public void SettingTheModeBringsBritishBack()
    {
        var emulator = NewVt340();

        emulator.ProcessData(Encoding.ASCII.GetBytes(Escape + "[?42l"));
        Assert.Equal(Ascii, DesignateAndPrint(emulator, 'A'));

        emulator.ProcessData(Encoding.ASCII.GetBytes(Escape + "[?42h"));
        Assert.Equal(British, DesignateAndPrint(emulator, 'A'));
    }

    [Fact]
    public void TheModeDoesNotDisturbAsciiOrTheSpecialGraphicSet()
    {
        // Only the ambiguous designator moves. A mode that quietly changed what 'B' or '0' meant
        // would break line drawing, which is a far more visible thing than a pound sign.
        var emulator = NewVt340();

        emulator.ProcessData(Encoding.ASCII.GetBytes(Escape + "[?42l"));

        Assert.Equal(Ascii, DesignateAndPrint(emulator, 'B'));
        Assert.Equal((byte)2, DesignateAndPrint(emulator, '0'));
    }

    [Fact]
    public void AHardResetPutsNationalSetsBack()
    {
        var emulator = NewVt340();

        emulator.ProcessData(Encoding.ASCII.GetBytes(Escape + "[?42l"));
        emulator.ProcessData(Encoding.ASCII.GetBytes(Escape + "c"));

        Assert.Equal(British, DesignateAndPrint(emulator, 'A'));
    }

    [Fact]
    public void TheModeIsAnsweredRatherThanCounted()
    {
        var emulator = NewVt340();

        emulator.ProcessData(Encoding.ASCII.GetBytes(Escape + "[?42h"));

        Assert.False(emulator.UnrecognisedSequences.ContainsKey("private mode 42 set"));
    }
}
