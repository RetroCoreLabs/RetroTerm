using System.Text;
using RetroTerm.Core.Configuration;
using RetroTerm.Core.Terminal.Emulators;
using RetroTerm.Core.Terminal.Printing;
using Xunit;

namespace RetroTerm.Tests.Terminal;

/// <summary>
/// DECGPBM - private mode 46, graphic print background mode.
/// </summary>
/// <remarks>
/// <para><b>What it decides</b></para>
/// Whether the pixels with nothing drawn on them are printed. Reset, a drawing goes on the paper as
/// ink on white; set, the background is laid down too, so the picture arrives inside a filled
/// rectangle. Reset is the sane default and is what the encoder already assumed - this mode is how
/// a host asks for the other one.
/// <para><b>Why it was missing until 25 August 2026</b></para>
/// The number is shared. <c>spec\DEC\xterm-ctlseqs.txt</c> lists Ps = 46 twice on consecutive
/// lines: XTLOGGING for xterm and Graphic Print Background Mode for the VT340. The coverage test
/// had read only the first and dismissed 46 as "logging", so it never appeared as a gap.
/// <para><b>Why the DEC reading wins here, when 47 goes the other way</b></para>
/// Cost of being wrong. xterm's logging is, in the spec's own words, "normally disabled by a
/// compile-time option", and this program has no logging feature for it to collide with - so
/// nothing is lost. Mode 47 is the opposite case: a mis-read there silently swaps a user's screen
/// mid-session, which is why the xterm meaning keeps that number.
/// </remarks>
public class GraphicPrintBackgroundModeTests
{
    /// <summary>
    /// The escape character, built from its code so no raw control byte lands in this file.
    /// </summary>
    private const char Escape = (char)0x1B;

    /// <summary>
    /// Builds a VT340 with one small filled shape on its graphics plane.
    /// </summary>
    /// <returns>
    /// The terminal, with something to print.
    /// </returns>
    private static TerminalEmulatorBase Vt340WithADrawing()
    {
        var emulator = EmulatorFactory.CreateEmulator("VT340", 80, 24, 100);
        emulator.ProcessData(Encoding.ASCII.GetBytes(Escape + "[?25l"));

        // A ReGIS square. Small on purpose: the test is about what surrounds the ink, not the ink.
        emulator.ProcessData(Encoding.ASCII.GetBytes(
            Escape + "Pp" + "W(I2)P[100,100]F(V[+40][,+40][-40][,-40])" + Escape + "\\"));

        return emulator;
    }

    /// <summary>
    /// Prints the graphics and returns what reached the printer.
    /// </summary>
    /// <param name="emulator">
    /// The terminal to print from.
    /// </param>
    /// <returns>
    /// The printed bytes as text.
    /// </returns>
    private static string Print(TerminalEmulatorBase emulator)
    {
        var sink = new MemoryPrintSink();
        emulator.PrintSink = sink;
        Assert.True(emulator.PrintGraphics(), "nothing was printed, so there is nothing to judge");
        return sink.ToText();
    }

    [Fact]
    public void TheModeIsAnsweredRatherThanCounted()
    {
        var emulator = EmulatorFactory.CreateEmulator("VT340", 80, 24, 100);

        emulator.ProcessData(Encoding.ASCII.GetBytes(Escape + "[?46h"));

        Assert.True(emulator.GraphicsPrintOptions.PrintBackground);
        Assert.False(emulator.UnrecognisedSequences.ContainsKey("private mode 46 set"));
    }

    [Fact]
    public void ResettingItPutsTheBackgroundBack()
    {
        var emulator = EmulatorFactory.CreateEmulator("VT340", 80, 24, 100);

        emulator.ProcessData(Encoding.ASCII.GetBytes(Escape + "[?46h"));
        emulator.ProcessData(Encoding.ASCII.GetBytes(Escape + "[?46l"));

        Assert.False(emulator.GraphicsPrintOptions.PrintBackground);
    }

    [Fact]
    public void APaperWithoutTheBackgroundIsNotTheSameAsOneWithIt()
    {
        // THE TEST THAT MATTERS. A mode that only sets a field would pass the two above and still
        // do nothing at all - the exact shape of defect this project keeps naming, where a sequence
        // is accepted, forgotten, and no longer counted as unhandled either.
        var withoutBackground = Print(Vt340WithADrawing());

        var withBackground = Vt340WithADrawing();
        withBackground.ProcessData(Encoding.ASCII.GetBytes(Escape + "[?46h"));

        Assert.NotEqual(withoutBackground, Print(withBackground));
    }

    [Fact]
    public void PrintingTheBackgroundMakesTheLongerPage()
    {
        // Which way round it differs is worth pinning, not just THAT it differs. Laying down the
        // background means colouring every pixel instead of only the drawn ones, so the encoded
        // page cannot be shorter. If this ever inverts, the two branches have been swapped.
        var withBackground = Vt340WithADrawing();
        withBackground.ProcessData(Encoding.ASCII.GetBytes(Escape + "[?46h"));

        Assert.True(Print(withBackground).Length > Print(Vt340WithADrawing()).Length,
            "printing the background produced no more output than leaving it out, so the mode is "
            + "reaching the encoder without changing what it does");
    }

    [Fact]
    public void AHostCanAskWhichWayItIsSet()
    {
        // DECRQM. A host that sets a print option is entitled to read it back, and 43 and 44 have
        // answered since they were built - 46 was the odd one out.
        var emulator = EmulatorFactory.CreateEmulator("VT340", 80, 24, 100);
        var replies = new StringBuilder();
        emulator.DataToSend += data => replies.Append(Encoding.ASCII.GetString(data));

        emulator.ProcessData(Encoding.ASCII.GetBytes(Escape + "[?46h"));
        emulator.ProcessData(Encoding.ASCII.GetBytes(Escape + "[?46$p"));

        // 1 is "set"; 0 would mean the mode is not recognised at all.
        Assert.Contains("46;1$y", replies.ToString());
    }
}
