using System.Text;
using RetroTerm.Core.Configuration;
using RetroTerm.Core.Terminal.Emulators;
using RetroTerm.Core.Terminal.Emulators.Tektronix;
using RetroTerm.Core.Terminal.Printing;
using Xunit;

namespace RetroTerm.Tests.Terminal.Printing;

/// <summary>
/// Printing the graphics bitmap - the DEC-private print modes and the routes that ask for a dump.
/// </summary>
/// <remarks>
/// <para><b>One sink, more than one route</b></para>
/// A VT re-encoded its own bitmap as sixel and sent it out the printer port. Media copy asked for
/// it, so did the ReGIS hardcopy command, and so did Tektronix mode's <c>ESC ETB</c>. They all
/// reach <c>PrintGraphics</c>, which is the reason ETB was left unimplemented until there was a
/// sink for it to reach.
///
/// <para><b>Mode 47 is not here, on purpose</b></para>
/// DEC's DECGRPM, the rotated print, is private mode 47 - and so is xterm's Alternate Screen
/// Buffer. Both are right for their own terminal. The test below pins that this emulator keeps the
/// xterm reading, so that a decision to change it has to be made deliberately rather than by
/// someone noticing the mode was missing.
/// </remarks>
public class GraphicsPrintTests
{
    private static void Feed(TerminalEmulatorBase emulator, string data)
        => emulator.ProcessData(Encoding.ASCII.GetBytes(data));

    private static TerminalEmulatorBase BuildVt340(out MemoryPrintSink sink)
    {
        var emulator = EmulatorFactory.CreateEmulator("VT340", 80, 24, 100);
        sink = new MemoryPrintSink();
        emulator.PrintSink = sink;
        return emulator;
    }

    /// <summary>
    /// Draws something on the graphics plane, through the real ReGIS path.
    /// </summary>
    private static void DrawSomething(TerminalEmulatorBase emulator)
        => Feed(emulator, "\x1bPpW(I3)P[10,10]V[200,10][200,100][10,100][10,10]\x1b\\");

    // ─────────────────────────────────────────────────────────────
    // The print modes
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void Decgepm43SelectsAnExpandedPrint()
    {
        var emulator = BuildVt340(out _);

        Feed(emulator, "\x1b[?43h");
        Assert.True(emulator.GraphicsPrintOptions.Expanded);

        Feed(emulator, "\x1b[?43l");
        Assert.False(emulator.GraphicsPrintOptions.Expanded);
    }

    [Fact]
    public void Decgpcm44SelectsAColourPrint()
    {
        var emulator = BuildVt340(out _);

        Assert.False(emulator.GraphicsPrintOptions.Colour);   // black and white by default

        Feed(emulator, "\x1b[?44h");
        Assert.True(emulator.GraphicsPrintOptions.Colour);
    }

    [Fact]
    public void BothPrintModesAnswerDecrqm()
    {
        var emulator = BuildVt340(out _);
        string? reply = null;
        emulator.DataToSend += bytes => reply = Encoding.ASCII.GetString(bytes);

        Feed(emulator, "\x1b[?43h\x1b[?43$p");
        Assert.Equal("\x1b[?43;1$y", reply);

        Feed(emulator, "\x1b[?44$p");
        Assert.Equal("\x1b[?44;2$y", reply);
    }

    [Fact]
    public void Mode47IsStillTheAlternateScreenBufferAndNotTheRotatedPrint()
    {
        // THE COLLISION, and it is RESOLVED, not merely pinned. DEC's DECGRPM is mode 47; so is
        // xterm's alternate screen. Ronny decided on 2026-08-17 that the xterm reading wins on
        // every profile, DEC graphics ones included: modern software sends 1047 and 1049, so
        // honouring DEC would buy almost nothing while a mis-read 47 would silently swap a user's
        // screen mid-session. This test is what stops that decision being undone by accident.
        var emulator = BuildVt340(out _);
        Feed(emulator, "PRIMARY");

        Feed(emulator, "\x1b[?47h");

        Assert.False(emulator.GraphicsPrintOptions.Rotated);

        Feed(emulator, "\x1b[?47l");

        // The primary screen came back, which only happens if 47 really was the alternate buffer.
        emulator.GetBuffer().TryGetCell(0, 0, out var cell);
        Assert.Equal((uint)'P', cell.Codepoint);
    }

    [Fact]
    public void TheRotatedPrintIsStillReachableWithoutTheMode()
    {
        var emulator = BuildVt340(out var sink);
        DrawSomething(emulator);

        emulator.GraphicsPrintOptions.Rotated = true;
        Assert.True(emulator.PrintGraphics());

        // 800 by 480 rotated is 480 wide by 800 tall, and rotated is always expanded.
        Assert.Contains("\"1;1;960;800", sink.ToText());
    }

    // ─────────────────────────────────────────────────────────────
    // Asking for a dump
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void PrintGraphicsSendsASixelDumpOfWhatWasDrawn()
    {
        var emulator = BuildVt340(out var sink);
        DrawSomething(emulator);

        Assert.True(emulator.PrintGraphics());

        string dump = sink.ToText();
        Assert.StartsWith("\x1bP", dump);
        Assert.Contains("\"1;1;800;480", dump);
        Assert.EndsWith("\x1b\\", dump);
    }

    [Fact]
    public void PrintGraphicsDoesNothingWithNoPrinter()
    {
        var emulator = EmulatorFactory.CreateEmulator("VT340", 80, 24, 100);
        DrawSomething(emulator);

        Assert.False(emulator.PrintGraphics());
    }

    [Fact]
    public void PrintGraphicsDoesNothingWhenNothingHasBeenDrawn()
    {
        var emulator = BuildVt340(out var sink);

        Assert.False(emulator.PrintGraphics());
        Assert.Equal(0, sink.PendingLength);
    }

    [Fact]
    public void DecpffEndsThePageAfterAGraphicsPrint()
    {
        var emulator = BuildVt340(out var sink);
        DrawSomething(emulator);
        Feed(emulator, "\x1b[?18h");

        emulator.PrintGraphics();

        Assert.Single(sink.Pages);
    }

    // ─────────────────────────────────────────────────────────────
    // Tektronix ESC ETB
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void TektronixEtbPrintsAHardCopyOfTheBitmap()
    {
        // "This sequence prints a hard copy of the terminal's bitmap by using the sixel protocol."
        var emulator = new Tek4014Emulator();
        var sink = new MemoryPrintSink();
        emulator.PrintSink = sink;

        // GS then two points: a vector across the tube, so there is a bitmap to print.
        Feed(emulator, ((char)0x1D) + " `D@" + "!`k@");
        Feed(emulator, "\x1b\x17");

        string dump = sink.ToText();
        Assert.StartsWith("\x1bP", dump);
        Assert.EndsWith("\x1b\\", dump);
    }

    [Fact]
    public void TektronixEtbStillClearsTheBypassConditionWithNoPrinter()
    {
        // "The sequence also clears the bypass condition" and "only works when a printer is
        // connected" are two separate clauses. A host with no printer that used ETB to get talking
        // to us again must not be left ignored.
        var emulator = new Tek4014Emulator();

        Feed(emulator, "\x1b\x18");     // ESC CAN - set bypass
        Assert.True(emulator.IsBypassed);

        Feed(emulator, "\x1b\x17");     // ESC ETB - no printer attached

        Assert.False(emulator.IsBypassed);
    }
}
