using System.Text;
using RetroTerm.Core.Configuration;
using RetroTerm.Core.Terminal.Emulators;
using RetroTerm.Core.Terminal.Printing;
using Xunit;

namespace RetroTerm.Tests.Terminal.Printing;

/// <summary>
/// Media copy - printer controller mode, autoprint and screen printing.
/// </summary>
/// <remarks>
/// <para><b>Where the rules come from</b></para>
/// <c>spec\DEC\xterm-ctlseqs.txt</c> for the MC parameters, the VT420 Programmer Reference for the
/// printer status replies and the power-up state of DECPFF and DECPEX, and DEC STD 070 section 7.8
/// for how a printer hung off the terminal in the first place. Designed in
/// <c>docs\PRINTING-SIXEL-TO-PDF-DESIGN-2026-08-17.md</c>.
///
/// <para><b>The trap these tests exist to pin</b></para>
/// <c>CSI 5 i</c> and <c>CSI ? 5 i</c> are different commands and the private marker is the only
/// difference. Reading one as the other sends a host's whole session to the printer, or a print
/// job to the screen.
/// </remarks>
public class MediaCopyTests
{
    private static TerminalEmulatorBase Build(out MemoryPrintSink sink)
    {
        var emulator = EmulatorFactory.CreateEmulator("VT420", 80, 24, 100);
        sink = new MemoryPrintSink();
        emulator.PrintSink = sink;
        return emulator;
    }

    private static void Feed(TerminalEmulatorBase emulator, string data)
        => emulator.ProcessData(Encoding.ASCII.GetBytes(data));

    private static string RowText(TerminalEmulatorBase emulator, int row)
    {
        var text = new StringBuilder();
        for (int col = 0; col < emulator.Width; col++)
        {
            emulator.GetBuffer().TryGetCell(row, col, out var cell);
            text.Append(cell.Codepoint == 0 ? ' ' : (char)cell.Codepoint);
        }

        return text.ToString().TrimEnd();
    }

    // ─────────────────────────────────────────────────────────────
    // Printer controller mode - CSI 5 i, no marker
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void PrinterControllerModeSendsEverythingToThePrinterAndNothingToTheScreen()
    {
        var emulator = Build(out var sink);

        Feed(emulator, "\x1b[5iHELLO PRINTER\x1b[4i");

        Assert.Equal("HELLO PRINTER", sink.ToText());
        Assert.Equal("", RowText(emulator, 0));
        Assert.False(emulator.PrinterControllerMode);
    }

    [Fact]
    public void TheScreenWorksAgainAfterTheModeEnds()
    {
        var emulator = Build(out var sink);

        Feed(emulator, "\x1b[5iPAPER\x1b[4iSCREEN");

        Assert.Equal("PAPER", sink.ToText());
        Assert.Equal("SCREEN", RowText(emulator, 0));
    }

    [Fact]
    public void TheModeSurvivesAChunkBoundary()
    {
        // Host data arrives in whatever sizes the network hands over.
        var emulator = Build(out var sink);

        Feed(emulator, "\x1b[5iONE ");
        Feed(emulator, "TWO ");
        Feed(emulator, "THREE\x1b[4i");

        Assert.Equal("ONE TWO THREE", sink.ToText());
    }

    [Fact]
    public void TheExitSequenceIsFoundEvenWhenSplitAcrossChunks()
    {
        // The one case a naive per-chunk scanner gets wrong: CSI 4 i arriving one byte at a time.
        var emulator = Build(out var sink);

        Feed(emulator, "\x1b[5iDATA");
        Feed(emulator, "\x1b");
        Feed(emulator, "[");
        Feed(emulator, "4");
        Feed(emulator, "i");
        Feed(emulator, "SCREEN");

        Assert.Equal("DATA", sink.ToText());
        Assert.Equal("SCREEN", RowText(emulator, 0));
    }

    [Fact]
    public void AnEscapeSequenceThatIsNotTheExitStillReachesThePrinter()
    {
        // A printer has its own protocol. Swallowing its escape sequences would corrupt the page -
        // this is the whole reason a sixel can be printed through the terminal at all.
        var emulator = Build(out var sink);

        Feed(emulator, "\x1b[5i\x1b[1mBOLD ON PAPER\x1b[4i");

        Assert.Equal("\x1b[1mBOLD ON PAPER", sink.ToText());
    }

    [Fact]
    public void ASixelPassesThroughByteForByte()
    {
        // The vaxrgl-lntest case: a plotter page streamed through the terminal to the printer. Not
        // a single byte may be reinterpreted, and none of it may reach the screen.
        var emulator = Build(out var sink);
        const string sixel = "\x1bP9;0;2q\"1;1;100;200#0;2;0;0;0#0~~@@vv@@~~$-#0??}}GG}}??-\x1b\\";

        Feed(emulator, "\x1b[5i" + sixel + "\x1b[4i");

        Assert.Equal(sixel, sink.ToText());
        Assert.Equal("", RowText(emulator, 0));
    }

    [Fact]
    public void ALoneEscapeAtTheEndOfAChunkIsNotLost()
    {
        var emulator = Build(out var sink);

        Feed(emulator, "\x1b[5iA\x1b");
        Feed(emulator, "ZB\x1b[4i");

        Assert.Equal("A\x1bZB", sink.ToText());
    }

    [Fact]
    public void WithNoPrinterTheJobIsSwallowedRatherThanShown()
    {
        // A terminal with an empty printer port loses the job. Putting it on the screen instead
        // would be worse: the host believes it printed and the user sees garbage.
        var emulator = EmulatorFactory.CreateEmulator("VT420", 80, 24, 100);

        Feed(emulator, "\x1b[5iSHOULD NOT APPEAR\x1b[4iSCREEN");

        Assert.Equal("SCREEN", RowText(emulator, 0));
    }

    // ─────────────────────────────────────────────────────────────
    // Autoprint - CSI ? 5 i, WITH the marker
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void AutoprintIsNotPrinterControllerMode()
    {
        // The private marker is the whole difference. With autoprint on, the screen still works.
        var emulator = Build(out var sink);

        Feed(emulator, "\x1b[?5iSCREEN");

        Assert.True(emulator.AutoPrintMode);
        Assert.False(emulator.PrinterControllerMode);
        Assert.Equal("SCREEN", RowText(emulator, 0));
    }

    [Fact]
    public void AutoprintPrintsEachLineAsItIsFinished()
    {
        var emulator = Build(out var sink);

        Feed(emulator, "\x1b[?5iONE\r\nTWO\r\n");

        Assert.Equal("ONE\r\nTWO\r\n", sink.ToText());
    }

    [Fact]
    public void AutoprintStopsWhenTurnedOff()
    {
        var emulator = Build(out var sink);

        Feed(emulator, "\x1b[?5iONE\r\n\x1b[?4iTWO\r\n");

        Assert.Equal("ONE\r\n", sink.ToText());
    }

    // ─────────────────────────────────────────────────────────────
    // Screen printing, DECPEX and DECPFF
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void PrintScreenPutsTheScreenOnPaper()
    {
        var emulator = Build(out var sink);
        Feed(emulator, "TOP\r\nNEXT");

        Feed(emulator, "\x1b[i");

        string printed = sink.ToText();
        Assert.StartsWith("TOP\r\nNEXT\r\n", printed);
    }

    [Fact]
    public void PrintScreenTrimsTheBlanksOffEachLine()
    {
        // A row is always Width cells whether or not anything was written to them.
        var emulator = Build(out var sink);
        Feed(emulator, "HI");

        Feed(emulator, "\x1b[i");

        Assert.StartsWith("HI\r\n", sink.ToText());
        Assert.DoesNotContain("HI  ", sink.ToText());
    }

    [Fact]
    public void PrintingTheCursorsLineIsOneLineOnly()
    {
        var emulator = Build(out var sink);
        Feed(emulator, "ONE\r\nTWO\r\nTHREE");

        Feed(emulator, "\x1b[?1i");

        Assert.Equal("THREE\r\n", sink.ToText());
    }

    [Fact]
    public void DecpexLimitsThePrintToTheScrollingRegion()
    {
        // DECPEX reset means the scrolling region only. Rows outside it stay off the paper.
        var emulator = Build(out var sink);
        Feed(emulator, "ONE\r\nTWO\r\nTHREE\r\nFOUR");
        Feed(emulator, "\x1b[2;3r");        // region is rows 2 to 3, one based
        Feed(emulator, "\x1b[?19l");        // DECPEX reset

        Feed(emulator, "\x1b[i");

        string printed = sink.ToText();
        Assert.Equal("TWO\r\nTHREE\r\n", printed);
    }

    [Fact]
    public void DecpexIsSetAtPowerUpSoAHostThatNeverMentionsItGetsTheWholeScreen()
    {
        var emulator = Build(out _);

        Assert.True(emulator.PrintExtentFullScreen);
    }

    [Fact]
    public void DecpffEndsThePageAfterAScreenPrint()
    {
        var emulator = Build(out var sink);
        Feed(emulator, "PAGE ONE");
        Feed(emulator, "\x1b[?18h");        // DECPFF set

        Feed(emulator, "\x1b[i");

        Assert.Single(sink.Pages);
        Assert.Equal(0, sink.PendingLength);
    }

    [Fact]
    public void WithoutDecpffThePageStaysOpen()
    {
        var emulator = Build(out var sink);
        Feed(emulator, "PAGE ONE");

        Feed(emulator, "\x1b[i");

        Assert.Empty(sink.Pages);
        Assert.True(sink.PendingLength > 0);
    }

    // ─────────────────────────────────────────────────────────────
    // What the terminal says about its printer
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void WithASinkAttachedTheTerminalReportsAPrinter()
    {
        // "CSI ? 10 n - Printer ready", VT420 Programmer Reference table 12-5.
        var emulator = Build(out _);
        string? reply = null;
        emulator.DataToSend += bytes => reply = Encoding.ASCII.GetString(bytes);

        Feed(emulator, "\x1b[?15n");

        Assert.Equal("\x1b[?10n", reply);
    }

    [Fact]
    public void WithNoSinkTheTerminalStillReportsNoPrinter()
    {
        // "CSI ? 13 n - No printer." An unkept promise sends a host down a path we cannot follow.
        var emulator = EmulatorFactory.CreateEmulator("VT420", 80, 24, 100);
        string? reply = null;
        emulator.DataToSend += bytes => reply = Encoding.ASCII.GetString(bytes);

        Feed(emulator, "\x1b[?15n");

        Assert.Equal("\x1b[?13n", reply);
    }

    [Fact]
    public void DecrqmAnswersForBothPrintModes()
    {
        var emulator = Build(out _);
        string? reply = null;
        emulator.DataToSend += bytes => reply = Encoding.ASCII.GetString(bytes);

        Feed(emulator, "\x1b[?18$p");
        Assert.Equal("\x1b[?18;2$y", reply);      // DECPFF reset

        Feed(emulator, "\x1b[?19$p");
        Assert.Equal("\x1b[?19;1$y", reply);      // DECPEX set
    }
}
