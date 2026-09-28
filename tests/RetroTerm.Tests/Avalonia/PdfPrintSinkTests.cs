using System;
using System.IO;
using System.Text;
using Avalonia.Headless.XUnit;
using RetroTerm.Core.Configuration;
using RetroTerm.Core.Terminal.Emulators;
using RetroTerm.Desktop.Printing;
using Xunit;

namespace RetroTerm.Tests.Avalonia;

/// <summary>
/// The PDF print sink - a print job turned into a page with a real size.
/// </summary>
/// <remarks>
/// <para><b>What can be checked here, and what cannot</b></para>
/// Checkable: that a PDF is produced at all, that it has the page count the job asked for, that
/// the page rectangle is the size the sixel grid says it should be, and that a job with nothing in
/// it produces no sheet. NOT checkable here: whether the printed page LOOKS right on paper. That
/// is a P5 question for Ronny, and the fixture to judge it by is <c>vaxrgl-lntest.six</c>.
///
/// <para><b>Why an Avalonia test</b></para>
/// It is not the UI that is needed, it is SkiaSharp's native library, which the headless Avalonia
/// fixture has already loaded. Running these outside the fixture would be the first thing in the
/// suite to need libSkiaSharp on its own.
///
/// <para><b>Reading a PDF without a PDF library</b></para>
/// The page size is asserted by reading the <c>/MediaBox</c> out of the file as text. PDF keeps
/// that entry as plain ASCII, so this needs no dependency and no parser - and a full PDF reader
/// would be a large thing to add for one number.
/// </remarks>
[Collection("Avalonia")]
public class PdfPrintSinkTests
{
    private static void Feed(TerminalEmulatorBase emulator, string data)
        => emulator.ProcessData(Encoding.ASCII.GetBytes(data));

    /// <summary>
    /// Pulls the first MediaBox out of a PDF, as four numbers in points.
    /// </summary>
    private static float[] MediaBox(byte[] pdf)
    {
        string text = Encoding.Latin1.GetString(pdf);
        int at = text.IndexOf("/MediaBox", StringComparison.Ordinal);
        Assert.True(at >= 0, "the PDF has no MediaBox, so it has no page");

        int open = text.IndexOf('[', at);
        int close = text.IndexOf(']', open);
        string[] parts = text.Substring(open + 1, close - open - 1)
            .Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

        var box = new float[4];
        for (int i = 0; i < 4; i++)
        {
            box[i] = float.Parse(parts[i], System.Globalization.CultureInfo.InvariantCulture);
        }

        return box;
    }

    private static byte[] Print(Action<PdfPrintSink> job)
    {
        using var stream = new MemoryStream();
        using (var sink = new PdfPrintSink(stream))
        {
            job(sink);
        }

        return stream.ToArray();
    }

    [Fact]
    public void ATextJobProducesAPdf()
    {
        byte[] pdf = Print(sink =>
        {
            sink.Write(Encoding.ASCII.GetBytes("HELLO PAPER\r\nSECOND LINE\r\n"));
        });

        Assert.True(pdf.Length > 0);
        Assert.Equal("%PDF", Encoding.ASCII.GetString(pdf, 0, 4));
    }

    [Fact]
    public void AJobWithNothingInItProducesNoSheet()
    {
        int pages = -1;
        using var stream = new MemoryStream();
        using (var sink = new PdfPrintSink(stream))
        {
            pages = sink.PageCount;
        }

        Assert.Equal(0, pages);
    }

    [Fact]
    public void EachFormFeedStartsANewSheet()
    {
        int pages = 0;
        using var stream = new MemoryStream();
        using (var sink = new PdfPrintSink(stream))
        {
            sink.Write(Encoding.ASCII.GetBytes("PAGE ONE"));
            sink.FormFeed();
            sink.Write(Encoding.ASCII.GetBytes("PAGE TWO"));
            sink.FormFeed();
            sink.Write(Encoding.ASCII.GetBytes("PAGE THREE"));
            sink.EndJob();
            pages = sink.PageCount;
        }

        Assert.Equal(3, pages);
    }

    [Fact]
    public void AFormFeedWithAnEmptyPageDoesNotEmitABlankSheet()
    {
        int pages = 0;
        using var stream = new MemoryStream();
        using (var sink = new PdfPrintSink(stream))
        {
            sink.Write(Encoding.ASCII.GetBytes("ONLY PAGE"));
            sink.FormFeed();
            sink.FormFeed();
            sink.FormFeed();
            pages = sink.PageCount;
        }

        Assert.Equal(1, pages);
    }

    [Fact]
    public void ASmallTextJobLandsOnALetterSheet()
    {
        byte[] pdf = Print(sink => sink.Write(Encoding.ASCII.GetBytes("SHORT\r\n")));

        var box = MediaBox(pdf);

        Assert.Equal(PdfPrintSink.DefaultPageWidth, box[2], 1);
        Assert.Equal(PdfPrintSink.DefaultPageHeight, box[3], 1);
    }

    [Fact]
    public void AGraphicsPrintGetsThePageSizeTheSixelGridAsksFor()
    {
        // The whole reason for PDF rather than PNG. An 800 by 480 plane at the default macro
        // parameter is 800 x 0.5 = 400 points wide and 480 x 0.5 = 240 points tall - square
        // pixels, because the decoder has already applied the sixel aspect ratio. Both fit inside
        // a Letter sheet, so the sheet stays Letter; the test that follows makes it grow.
        var emulator = EmulatorFactory.CreateEmulator("VT340", 80, 24, 100);
        Feed(emulator, "\x1bPpW(I3)P[10,10]V[700,10][700,400][10,400][10,10]\x1b\\");

        byte[] pdf = Print(sink =>
        {
            emulator.PrintSink = sink;
            Assert.True(emulator.PrintGraphics());
        });

        var box = MediaBox(pdf);
        Assert.Equal(PdfPrintSink.DefaultPageWidth, box[2], 1);
        Assert.Equal(PdfPrintSink.DefaultPageHeight, box[3], 1);
        Assert.True(pdf.Length > 1000, "a page with a picture on it should not be nearly empty");
    }

    [Fact]
    public void ASheetGrowsRatherThanCroppingWhatWillNotFit()
    {
        // A plotter page is bigger than Letter. Losing its edges would be worse than an unusual
        // page size, so the sheet grows to hold the picture.
        using var stream = new MemoryStream();
        using (var sink = new PdfPrintSink(stream))
        {
            // A level 2 sixel declaring 2000 by 100: 1000 points wide, past Letter's 612.
            var dump = new StringBuilder();
            dump.Append("\x1bP0;1;0q\"1;1;2000;100");
            dump.Append("!2000~");
            dump.Append("\x1b\\");
            sink.Write(Encoding.ASCII.GetBytes(dump.ToString()));
        }

        var box = MediaBox(stream.ToArray());

        Assert.True(box[2] > PdfPrintSink.DefaultPageWidth,
            $"the sheet should have grown past Letter; it is {box[2]} points wide");
        Assert.True(box[2] >= 1000, $"2000 pixels at half a point each is 1000 points; got {box[2]}");
    }

    /// <summary>
    /// The real thing: a DEC plotter sheet streamed through the terminal to a PDF.
    /// </summary>
    /// <remarks>
    /// <para><b>This is the case the whole printing design started from</b></para>
    /// <c>vaxrgl-lntest.six</c> is a 972 by 1548 sixel from hackerb9's corpus - a DEC engineering
    /// drawing sheet, title block <c>ENG=E FONTANA</c>, <c>LAST_MODIFIED=Mon 3-Mar-87</c>. It is a
    /// PLOTTER PAGE, not a screen image: on an 800 by 480 graphics plane only its top-left corner
    /// is visible. Printer controller mode is how it was meant to arrive, and a PDF is the only
    /// output here that can hold it at its true size.
    ///
    /// <para><b>Skipped, not failed, when the corpus is absent</b></para>
    /// The fixtures are fetched by <c>tools\fetch-conformance-corpora.ps1</c> and never committed.
    /// </remarks>
    [AvaloniaFact]
    public void ADecPlotterSheetPrintsThroughTheTerminalToAPdf()
    {
        string source = Path.Combine(CorpusFolder, "vaxrgl-lntest.six");
        if (!File.Exists(source)) return;

        var emulator = EmulatorFactory.CreateEmulator("VT340", 80, 24, 100);
        Feed(emulator, "\x1b[?25l");

        string path = Path.Combine(RenderedScreenshot.ImagesFolder, "print-plotter-sheet.pdf");

        // CLOSED BEFORE THE FILE IS READ, and that is not a style choice. The sink holds the file
        // open for writing with no sharing, and it is DISPOSING that closes the PDF - EndJob only
        // flushes, since a terminal ends a print job many times a session. A "using var" here
        // disposes at the end of the METHOD, so every assertion below ran against a file that was
        // still open and still being written: one run failed to read it at all and another measured
        // it at zero bytes.
        using (var sink = TryOpenArtefact(path))
        {
            if (sink == null) return;

            emulator.PrintSink = sink;

            // CSI 5 i - the terminal becomes a wire. Every byte of the drawing goes to the
            // printer and none of it reaches the screen, which is the whole point: this page was
            // never meant for a screen.
            Feed(emulator, "\x1b[5i");
            emulator.ProcessData(File.ReadAllBytes(source));
            Feed(emulator, "\x1b[4i");
        }

        var written = new FileInfo(path);
        Assert.True(written.Exists, "no PDF was written");

        // A 972 by 1548 picture at half a point per pixel is 486 by 774 points plus margins, so
        // the sheet grows past Letter's height. That number IS the drawing's real size.
        var box = MediaBox(File.ReadAllBytes(path));
        Assert.True(box[3] > PdfPrintSink.DefaultPageHeight,
            $"the sheet should have grown to hold a 1548-pixel drawing; it is {box[3]} points tall");
        Assert.True(written.Length > 20000, $"the PDF is only {written.Length} bytes");
    }

    /// <summary>
    /// Every sixel in the corpus, one to a page, as a single catalogue to flip through.
    /// </summary>
    /// <remarks>
    /// <para><b>Why one file rather than sixteen</b></para>
    /// These exist to be LOOKED at, and looking at sixteen pictures means paging through one
    /// document, not opening sixteen. The pages come out in name order so the four <c>cat-*</c>
    /// encodings of the same photograph land next to each other - which is the comparison that
    /// makes them worth having.
    ///
    /// <para><b>A fresh terminal per picture</b></para>
    /// Each stream sets its own colour map, and a VT340 has ONE map that both graphics languages
    /// write to. Reusing a terminal would let one fixture's palette colour the next one's picture.
    /// </remarks>
    [AvaloniaFact]
    public void EverySixelInTheCorpusPrintsToOneCatalogue()
    {
        if (!Directory.Exists(CorpusFolder)) return;

        var fixtures = Directory.GetFiles(CorpusFolder, "*.six");
        if (fixtures.Length == 0) return;
        Array.Sort(fixtures, StringComparer.OrdinalIgnoreCase);

        string path = Path.Combine(RenderedScreenshot.ImagesFolder, "print-sixel-catalogue.pdf");

        using var sink = TryOpenArtefact(path);
        if (sink == null) return;

        for (int i = 0; i < fixtures.Length; i++)
        {
            var emulator = EmulatorFactory.CreateEmulator("VT340", 80, 24, 100);
            Feed(emulator, "\x1b[?25l");
            emulator.PrintSink = sink;

            // Printer controller mode: the bytes reach the paper untouched.
            Feed(emulator, "\x1b[5i");
            emulator.ProcessData(File.ReadAllBytes(fixtures[i]));
            Feed(emulator, "\x1b[4i");

            sink.FormFeed();
        }

        Assert.Equal(fixtures.Length, sink.PageCount);
    }

    [Fact]
    public void MoreThanOnePrintJobSurvivesToTheSameDocument()
    {
        // THE DEFECT THE CATALOGUE FOUND. Leaving printer controller mode calls EndJob on the
        // sink, and a terminal does that once per print job - many times over a session. The PDF
        // sink treated it as "close the document", so everything after the FIRST job was written
        // to a closed document and vanished. Invisible while only one thing was ever printed;
        // sixteen corpus pictures in a row came out as a single page.
        using var stream = new MemoryStream();
        int pages;

        using (var sink = new PdfPrintSink(stream))
        {
            for (int job = 0; job < 3; job++)
            {
                var emulator = EmulatorFactory.CreateEmulator("VT340", 80, 24, 100);
                emulator.PrintSink = sink;

                Feed(emulator, "\x1b[5i");
                Feed(emulator, "\x1bP0;1;0q\"1;1;8;6!8~\x1b\\");
                Feed(emulator, "\x1b[4i");      // ends the job - must NOT end the document
            }

            pages = sink.PageCount;
        }

        Assert.Equal(3, pages);
    }

    /// <summary>
    /// The three gnuplot Tektronix streams, printed as hard copies.
    /// </summary>
    /// <remarks>
    /// A different route to the same sink: these are VECTORS, not a sixel stream, so the terminal
    /// draws them and then re-encodes its own bitmap - which is what <c>ESC ETB</c> asks for on a
    /// real 4014 and what the printing design calls a graphics hard copy. Nothing about the page
    /// says where the picture came from, which is the point of having one sink.
    /// </remarks>
    [AvaloniaFact]
    public void TheTektronixStreamsPrintAsHardCopies()
    {
        if (!Directory.Exists(TektronixFolder)) return;

        var fixtures = Directory.GetFiles(TektronixFolder, "*.tek40xx");
        if (fixtures.Length == 0) return;
        Array.Sort(fixtures, StringComparer.OrdinalIgnoreCase);

        string path = Path.Combine(RenderedScreenshot.ImagesFolder, "print-tektronix.pdf");

        using var sink = TryOpenArtefact(path);
        if (sink == null) return;

        int printed = 0;
        for (int i = 0; i < fixtures.Length; i++)
        {
            var emulator = new RetroTerm.Core.Terminal.Emulators.Tektronix.Tek4014Emulator();
            emulator.PrintSink = sink;
            emulator.ProcessData(File.ReadAllBytes(fixtures[i]));

            if (emulator.PrintGraphics()) printed++;
            sink.FormFeed();
        }

        Assert.Equal(fixtures.Length, printed);
        Assert.Equal(fixtures.Length, sink.PageCount);
    }

    /// <summary>
    /// Where the vendored Tektronix streams live.
    /// </summary>
    private static string TektronixFolder => Path.GetFullPath(Path.Combine(
        Path.GetDirectoryName(typeof(PdfPrintSinkTests).Assembly.Location) ?? "",
        "..", "..", "..", "Conformance", "tektronix"));

    /// <summary>
    /// Opens a kept artefact for writing, or gives up quietly when it is already open.
    /// </summary>
    /// <remarks>
    /// These files exist to be LOOKED AT, and on Windows a PDF viewer holds its file open. A test
    /// run must not fail because the artefact from the last run is on screen - that would punish
    /// exactly the thing the file is there for.
    /// </remarks>
    /// <param name="path">
    /// Where the artefact goes.
    /// </param>
    /// <returns>
    /// A sink, or null when the file is locked.
    /// </returns>
    private static PdfPrintSink? TryOpenArtefact(string path)
    {
        Directory.CreateDirectory(RenderedScreenshot.ImagesFolder);

        try
        {
            return new PdfPrintSink(path);
        }
        catch (IOException)
        {
            return null;    // someone is reading the last one; that is the point of keeping it
        }
    }

    /// <summary>
    /// Where the fetched conformance fixtures live.
    /// </summary>
    private static string CorpusFolder => Path.GetFullPath(Path.Combine(
        Path.GetDirectoryName(typeof(PdfPrintSinkTests).Assembly.Location) ?? "",
        "..", "..", "..", "Conformance", "vt340test"));

    [AvaloniaFact]
    public void AWholePrintJobGoesFromTheTerminalToAPdfFile()
    {
        // End to end, through the real paths: draw with ReGIS, ask for a hard copy, get a file.
        var emulator = EmulatorFactory.CreateEmulator("VT340", 80, 24, 100);
        Feed(emulator, "\x1b[?25l");
        Feed(emulator, "\x1bPpW(I2)P[100,100]F(C[+80])W(I3)P[300,100]F(V[+200][,+200][-200][,-200])\x1b\\");
        emulator.GraphicsPrintOptions.Colour = true;

        // KEPT, not deleted, and beside the rendered PNGs on purpose. Whether a printed page looks
        // right is the one thing no assertion here can answer, so the file has to survive the test
        // for a person to open. Same reason the screenshots are kept - see CLAUDE.md.
        string path = Path.Combine(RenderedScreenshot.ImagesFolder, "print-job.pdf");

        // Closed before the file is measured - see the plotter sheet test above for why.
        using (var sink = TryOpenArtefact(path))
        {
            if (sink == null) return;

            emulator.PrintSink = sink;

            // Page one: a screen print, so the TEXT path is in the file to be looked at too.
            Feed(emulator, "\x1b[H RETROTERM PRINT TEST\r\n"
                + " ---------------------\r\n"
                + " Column alignment matters on a print job:\r\n"
                + " |0123456789|0123456789|\r\n"
                + " |ABCDEFGHIJ|abcdefghij|\r\n");
            Feed(emulator, "\x1b[i");
            sink.FormFeed();

            // Page two: the graphics hard copy.
            Assert.True(emulator.PrintGraphics());
        }

        var written = new FileInfo(path);
        Assert.True(written.Exists, "no PDF was written");
        Assert.True(written.Length > 1000, $"the PDF is only {written.Length} bytes");
    }

    [Fact]
    public void APictureKeepsItsShapeOnThePage()
    {
        // THE DEFECT THIS WAS WRITTEN FOR. The first version of this sink read table 5-1 the
        // obvious way - horizontal grid 50 centipoints, vertical grid 100 - and every circle came
        // out an ellipse. Section 5.4.1.3 says the aspect "may be defined by Ps1 OR the Set Raster
        // Attributes sixel control code", and DECGRA wins; the decoder already applies it by
        // painting Pan/Pad rows per bit. Applying it again squares the distortion.
        //
        // So a square picture must produce a square area on the page. Found by opening the PDF;
        // this is the assertion that stops it coming back.
        using var stream = new MemoryStream();
        using (var sink = new PdfPrintSink(stream))
        {
            // 1700 by 1700 pixels is 850 points square, and BOTH sides then exceed Letter's 612
            // by 792 - so the sheet grows to the picture on both axes and the MediaBox IS the
            // picture. At 1200 it did not: 600 points is under Letter's height, the sheet kept its
            // 792, and the test compared the sheet's shape rather than the picture's.
            const int side = 1700;
            const int bands = side / 6;

            var dump = new StringBuilder();
            dump.Append("\x1bP0;1;0q\"1;1;").Append(side).Append(';').Append(side);
            for (int band = 0; band < bands; band++)
            {
                dump.Append("!").Append(side).Append('~');
                if (band != bands - 1) dump.Append('-');
            }

            dump.Append("\x1b\\");
            sink.Write(Encoding.ASCII.GetBytes(dump.ToString()));
        }

        var box = MediaBox(stream.ToArray());

        Assert.Equal(box[2], box[3], 1);
    }

    [Fact]
    public void ADeviceControlStringThatIsNotSixelIsNotDrawnAsOne()
    {
        // A print stream may carry other DCS protocols. Guessing at one would draw nonsense.
        byte[] pdf = Print(sink =>
        {
            sink.Write(Encoding.ASCII.GetBytes("BEFORE\r\n\x1bP1$rSOMETHING\x1b\\AFTER\r\n"));
        });

        var box = MediaBox(pdf);
        Assert.Equal(PdfPrintSink.DefaultPageWidth, box[2], 1);
    }

    [Fact]
    public void ControlSequencesAroundAPictureAreNotPrintedAsText()
    {
        // THE DEFECT THIS WAS WRITTEN FOR. A print-through job carries the printer's own control
        // sequences - page size, margins, unit selection - and they are commands, not text.
        // Printing them as characters put "[7 I[?20 J[1;66r" along the bottom of the first real
        // plotter sheet this produced. Found by opening the PDF.
        byte[] pdf = Print(sink =>
        {
            sink.Write(Encoding.ASCII.GetBytes(
                "\x1b\\\x1b[7 I\x1b[?20 J\x1b[1;66r"
                + "\x1bP0;1;0q\"1;1;8;6!8~\x1b\\"
                + "REAL TEXT\r\n"));
        });

        string body = Encoding.Latin1.GetString(pdf);

        // The real text survives; the sequences leave no trace. PDF stores glyph runs, so this
        // looks for the digits of "1;66" rather than a whole phrase.
        Assert.DoesNotContain("?20", body);
        Assert.DoesNotContain("1;66r", body);
    }

    [Fact]
    public void EndJobTwiceIsHarmless()
    {
        // DISPOSED, and that is not tidiness. An SKDocument left to the finalizer unrefs a managed
        // stream wrapper that has already gone, which throws a NullReferenceException on the
        // finalizer thread and takes the whole test host down - not a failed test, a crashed
        // process. This test used to get away with it only because EndJob closed the document;
        // the moment EndJob became a flush, it crashed the run.
        using var stream = new MemoryStream();
        using var sink = new PdfPrintSink(stream);
        sink.Write(Encoding.ASCII.GetBytes("ONE PAGE"));

        sink.EndJob();
        sink.EndJob();

        Assert.Equal(1, sink.PageCount);
    }
}
