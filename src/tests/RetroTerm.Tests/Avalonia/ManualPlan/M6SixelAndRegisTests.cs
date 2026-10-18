using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Avalonia.Headless.XUnit;
using RetroTerm.Core.Configuration;
using RetroTerm.Core.Terminal.Emulators;
using RetroTerm.Core.Terminal.Graphics;
using SkiaSharp;
using Xunit;

namespace RetroTerm.Tests.Avalonia.ManualPlan;

/// <summary>
/// The machine half of the by-hand pass in
/// <c>docs\manual-tests\M6-SIXEL-AND-REGIS.md</c>.
/// </summary>
/// <remarks>
/// <para><b>What this adds that the corpus tests do not</b></para>
/// <c>Vt340SixelCorpusRenderingTests</c> already renders every fixture and saves a PNG. Judging one
/// then meant finding the matching capture in the fetched corpus folder, opening it in a second
/// viewer, and holding the two in your head - eleven times. This builds the pair into ONE sheet per
/// fixture, ours above the hardware's, so the whole judgement is a folder of images to page through.
///
/// The assertions here cannot say the two halves are the same picture; the bottom half is a
/// photograph of curved glass. What they CAN say is that both halves carry ink, which stops a
/// fixture that silently decoded to nothing from presenting a blank top half as something to
/// approve.
/// </remarks>
[Collection("Avalonia")]
public class M6SixelAndRegisTests
{
    /// <summary>
    /// Where the fetched corpus lands. Resolved from the assembly so it points at the source tree.
    /// </summary>
    private static string CorpusFolder => Path.GetFullPath(Path.Combine(
        Path.GetDirectoryName(typeof(M6SixelAndRegisTests).Assembly.Location) ?? "",
        "..", "..", "..", "Conformance", "vt340test"));

    /// <summary>
    /// Height of the bar drawn between the two halves of a comparison sheet.
    /// </summary>
    private const int SeparatorHeight = 6;

    /// <summary>
    /// Height of the strip reserved above each panel for its name.
    /// </summary>
    private const int LabelHeight = 20;

    /// <summary>
    /// Writes one panel's name across the top of it, so a reader knows which half they are looking at.
    /// </summary>
    /// <param name="canvas">
    /// The sheet being drawn.
    /// </param>
    /// <param name="text">
    /// The label.
    /// </param>
    /// <param name="top">
    /// Y of the top of the panel this names.
    /// </param>
    /// <remarks>
    /// <para><b>It gets its OWN strip and never covers the picture</b></para>
    /// The first version drew onto a translucent band laid over the top of each panel, on the
    /// assumption that the top twenty pixels of every fixture are empty margin. THEY ARE NOT.
    /// map8.six declares a raster of 93 by 14 - the whole image is fourteen pixels tall, at the top
    /// left - so the band covered it completely and the sheet showed an empty screen. A label that
    /// can erase the picture is worse than no label, and it took a colour census to notice rather
    /// than an eye, because "nothing there" is exactly what a decoder failure looks like.
    ///
    /// So the sheet now RESERVES a strip above each panel and the pictures start below it.
    ///
    /// <para><b>The default typeface, and no failure if it is missing</b></para>
    /// The original objection to labelling was that a font might be absent on some machine. The whole
    /// call is guarded, so on a machine with no usable typeface the sheet comes out exactly as it did
    /// before - unlabelled - rather than failing a run over an artefact.
    /// </remarks>
    private static void Label(SKCanvas canvas, string text, int top)
    {
        try
        {
            using var font = new SKFont(SKTypeface.Default, 13);
            using var ink = new SKPaint();
            ink.Color = new SKColor(255, 200, 0);
            ink.IsAntialias = true;

            // Into the strip RESERVED above the panel, never on top of it.
            canvas.DrawText(text, 6, top + LabelHeight - 5, SKTextAlign.Left, font, ink);
        }
        catch
        {
            // An artefact is never worth failing a run over - the same rule the sheets themselves
            // and the printed PDFs already follow.
        }
    }

    /// <summary>
    /// Name used when the corpus has not been fetched, so the theory still has a case to run.
    /// </summary>
    private const string NotFetched = "(corpus not fetched)";

    /// <summary>
    /// Every fixture that has BOTH a Sixel stream and a capture of what real hardware made of it.
    /// </summary>
    /// <returns>
    /// One case per pair, or a single placeholder when the corpus is absent.
    /// </returns>
    public static IEnumerable<object[]> PairedFixtures()
    {
        if (!Directory.Exists(CorpusFolder))
        {
            yield return new object[] { NotFetched };
            yield break;
        }

        string[] streams = Directory.GetFiles(CorpusFolder, "*.six");
        Array.Sort(streams, StringComparer.Ordinal);

        var found = new List<string>();
        for (int i = 0; i < streams.Length; i++)
        {
            string name = Path.GetFileNameWithoutExtension(streams[i]);
            if (File.Exists(Path.Combine(CorpusFolder, name + ".png")))
            {
                found.Add(name);
            }
        }

        if (found.Count == 0)
        {
            yield return new object[] { NotFetched };
            yield break;
        }

        for (int i = 0; i < found.Count; i++)
        {
            yield return new object[] { found[i] };
        }
    }

    /// <summary>
    /// Builds a VT340 with the cursor parked and plays a fixture into it.
    /// </summary>
    /// <param name="fixture">
    /// File name inside the fetched corpus, without its extension.
    /// </param>
    /// <returns>
    /// The emulator, ready to be rendered.
    /// </returns>
    private static TerminalEmulatorBase Play(string fixture)
    {
        var emulator = EmulatorFactory.CreateEmulator("VT340", 80, 24, 100);

        // A visible cursor is ink like any other, and it would sit in the middle of the picture.
        emulator.ProcessData(new byte[] { 0x1B, (byte)'[', (byte)'?', (byte)'2', (byte)'5', (byte)'l' });
        emulator.ProcessData(File.ReadAllBytes(Path.Combine(CorpusFolder, fixture + ".six")));

        return emulator;
    }

    /// <summary>
    /// M6.1a - every fixture that has a hardware capture is built into one sheet to judge.
    /// </summary>
    /// <param name="fixture">
    /// The fixture name, from <see cref="PairedFixtures"/>.
    /// </param>
    /// <remarks>
    /// The sheet is stacked rather than side by side: both pictures are wider than they are tall,
    /// so stacking keeps each at its own full size on a screen without shrinking either. The layout
    /// is fixed and documented in the manual case.
    ///
    /// <para><b>The sheet used to carry no labels, and that was wrong</b></para>
    /// The original reasoning was that drawing labels means choosing a font, and a font missing on
    /// some machine turns the artefact into a puzzle. The missing font was hypothetical. The puzzle
    /// was real: Ronny judged these sheets on 27 August 2026 and said "i dont know which one is
    /// supposed to be which". An unlabelled comparison cannot be judged at all, which is worse than
    /// any font problem it was avoiding - so both panels are now named, and each carries its own
    /// pixel size because the two are NOT the same scale and that difference is itself a finding.
    /// </remarks>
    [AvaloniaTheory]
    [MemberData(nameof(PairedFixtures))]
    public void M6_1a_EveryHardwarePairIsBuiltIntoOneSheetToJudge(string fixture)
    {
        if (fixture == NotFetched)
        {
            return;
        }

        var emulator = Play(fixture);

        // Capture through the production renderer, and read the PNG it wrote back in - that file IS
        // the top half, so the sheet can never show something the ordinary corpus artefact did not.
        // AT THE HARDWARE'S OWN RESOLUTION. The VT340 graphics plane is 800 by 480 and so is
        // every capture in the corpus, so ours is taken at exactly that and the two halves of the
        // sheet are finally the same picture at the same size. Without it the font decides: a
        // VT340 draws through Consolas 14, which makes 80 by 24 come out 616 by 393 and shrinks
        // the graphics by two different factors, so even the aspect was wrong.
        // AT THE HARDWARE'S OWN CELL. A real VT340 is 10 by 20 pixels per character, so 80 by 24
        // is 800 by 480 - the size of the Sixel plane and of every capture in the corpus. Nothing
        // is scaled anywhere, so the two halves of the sheet are the same picture at the same size
        // and a colour difference is a colour difference rather than a resampling artefact.
        using var shot = RenderedScreenshot.Capture(emulator, "vt340-sixel-" + fixture, saveZoom: 1,
            cellPixelWidth: TerminalEmulatorBase.GraphicsPlaneWidth / 80.0,
            cellPixelHeight: TerminalEmulatorBase.GraphicsPlaneHeight / 24.0);
        Assert.NotNull(shot.SavedPath);

        using var ours = SKBitmap.Decode(File.ReadAllBytes(shot.SavedPath!));
        using var hardware = SKBitmap.Decode(File.ReadAllBytes(Path.Combine(CorpusFolder, fixture + ".png")));

        Assert.NotNull(ours);
        Assert.NotNull(hardware);

        int width = Math.Max(ours.Width, hardware.Width);
        int height = LabelHeight + ours.Height + SeparatorHeight + LabelHeight + hardware.Height;

        using var sheet = new SKBitmap(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul));
        using (var canvas = new SKCanvas(sheet))
        {
            canvas.Clear(new SKColor(24, 24, 24));

            int oursTop = LabelHeight;
            int barTop = oursTop + ours.Height;
            int hardwareTop = barTop + SeparatorHeight + LabelHeight;

            canvas.DrawBitmap(ours, 0, oursTop, SKSamplingOptions.Default);

            using (var bar = new SKPaint())
            {
                bar.Color = new SKColor(255, 128, 0);
                bar.Style = SKPaintStyle.Fill;
                canvas.DrawRect(new SKRect(0, barTop, width, barTop + SeparatorHeight), bar);
            }

            canvas.DrawBitmap(hardware, 0, hardwareTop, SKSamplingOptions.Default);

            // Named last so the text sits ON TOP of both pictures rather than under them.
            Label(canvas, "OURS - " + fixture + ".six through our decoder   "
                + ours.Width + " x " + ours.Height, 0);
            Label(canvas, "REAL HARDWARE - " + fixture + ".png, photographed   "
                + hardware.Width + " x " + hardware.Height, barTop + SeparatorHeight);
        }

        var folder = RenderedScreenshot.ImagesFolder;
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, "compare-" + fixture + ".png");

        // A viewer may be holding the previous copy open. A manual artefact is not worth failing a
        // run over - the same rule the printed PDFs follow.
        try
        {
            using var data = sheet.Encode(SKEncodedImageFormat.Png, 100);
            using var file = File.Create(path);
            data.SaveTo(file);
        }
        catch (IOException)
        {
        }

        // What can be stated without looking: BOTH halves carry ink. A fixture that decoded to
        // nothing would otherwise offer a blank top half to be approved by a tired eye.
        Assert.True(HasInk(ours), fixture + " decoded to a blank screen - nothing to compare");
        Assert.True(HasInk(hardware), fixture + ".png in the corpus is blank - the fetch is damaged");
    }

    /// <summary>
    /// M6.1a - the sheet count matches the number of pairs the corpus holds.
    /// </summary>
    /// <remarks>
    /// The theory above skips silently when the corpus is absent, which is right - absence is
    /// expected. What is NOT right is a corpus that is present and half fetched: the number of
    /// sheets would quietly shrink while every one of them still passed. Eleven pairs as fetched on
    /// 2026-08-17.
    /// </remarks>
    [Fact]
    public void M6_1a_TheCorpusStillHoldsElevenHardwarePairs()
    {
        if (!Directory.Exists(CorpusFolder) || Directory.GetFiles(CorpusFolder, "*.six").Length == 0)
        {
            return;
        }

        int pairs = 0;
        string[] streams = Directory.GetFiles(CorpusFolder, "*.six");
        for (int i = 0; i < streams.Length; i++)
        {
            string name = Path.GetFileNameWithoutExtension(streams[i]);
            if (File.Exists(Path.Combine(CorpusFolder, name + ".png")))
            {
                pairs++;
            }
        }

        Assert.Equal(11, pairs);
    }

    /// <summary>
    /// M6.4c - the ReGIS text command, drawn at several sizes and tilts to be read.
    /// </summary>
    /// <remarks>
    /// <para><b>Twenty-one assertions cannot tell you whether a letter is legible</b></para>
    /// The sizes in <c>RegisTextAndLoadTests</c> come from the manual's table 7-3 and are pinned
    /// exactly. The SHAPES cannot be: the chapter prints six example glyphs and no font, so the
    /// glyphs are the TDV2200 character ROM used as a stand-in. Whether text drawn that way reads
    /// as text is a judgement, and this is the picture it is made against.
    ///
    /// Drawn on the plane at one pixel to one pixel, for the reason the Tektronix patterns are:
    /// an 8 by 10 glyph squeezed through the screen scaling is a smudge whatever the decoder did.
    /// </remarks>
    [AvaloniaFact]
    public void M6_4c_TheTextCommandIsDrawnAtSeveralSizesToBeRead()
    {
        var surface = new InMemoryGraphicsSurface(760, 480);
        var decoder = new RegisDecoder();

        // Sizes S0 to S4 down the left, each labelled with its own number.
        decoder.Decode("P[10,10]T(S0)'S0 The quick brown fox jumps over the lazy dog'", surface);
        decoder.Decode("P[10,30]T(S1)'S1 The quick brown fox'", surface);
        decoder.Decode("P[10,70]T(S2)'S2 Sizes from table 7-3'", surface);
        decoder.Decode("P[10,120]T(S3)'S3 ReGIS text'", surface);
        decoder.Decode("P[10,180]T(S4)'S4 Text'", surface);

        // The height multiplier and italics, which change one axis each.
        decoder.Decode("P[10,260]T(S1)(H2)'H2 stretched vertically'", surface);
        decoder.Decode("P[10,310]T(S1)(H1)(I30)'I30 italic'", surface);

        // A string running down the screen, and one running back up it.
        decoder.Decode("P[560,20]T(S1)(I0)(D270)'DOWN'", surface);
        decoder.Decode("P[620,200]T(S1)(D90)'UP'", surface);

        // A loaded cell: a checkerboard in set 1, drawn large enough to count the squares.
        //
        // The tilt and the height multiplier are put back FIRST, and that is not tidiness. Chapter
        // 7: "the values you select with these options remain in effect until you define new
        // values". Without these the checkerboard inherited D90 and I30 from the lines above and
        // came out as a narrow slanted strip running up the screen - which is what a real VT340
        // would also have drawn, and which made this sheet look like a defect it was not.
        decoder.Decode("L(A1'CHECKER')L\"Z\"AA,55,AA,55,AA,55,AA,55,AA,55", surface);
        decoder.Decode("P[10,380]T(D0)(I0)(H1)(A1)(S4)'ZZZ'", surface);

        SavePlane(surface, "regis-text-sample");

        int lit = 0;
        for (int y = 0; y < surface.Height; y++)
        {
            for (int x = 0; x < surface.Width; x++)
            {
                if (!surface.GetPixel(x, y).IsTransparent) lit++;
            }
        }

        Assert.True(lit > 500, $"the text sample lit only {lit} pixels - that is not readable text");
    }

    /// <summary>
    /// Where the fetched ReGIS fixtures land.
    /// </summary>
    private static string RegisFolder => Path.GetFullPath(Path.Combine(
        Path.GetDirectoryName(typeof(M6SixelAndRegisTests).Assembly.Location) ?? "",
        "..", "..", "..", "Conformance", "vt340regis"));

    /// <summary>
    /// Every fetched ReGIS fixture, or a placeholder when the corpus is absent.
    /// </summary>
    /// <returns>
    /// One case per fixture.
    /// </returns>
    public static IEnumerable<object[]> RegisFixtures()
    {
        if (!Directory.Exists(RegisFolder))
        {
            yield return new object[] { NotFetched };
            yield break;
        }

        string[] files = Directory.GetFiles(RegisFolder, "*.regis");
        if (files.Length == 0)
        {
            yield return new object[] { NotFetched };
            yield break;
        }

        Array.Sort(files, StringComparer.Ordinal);

        for (int i = 0; i < files.Length; i++)
        {
            yield return new object[] { Path.GetFileName(files[i]) };
        }
    }

    /// <summary>
    /// M6.4d - the fetched ReGIS fixtures, drawn at plane size to be looked at.
    /// </summary>
    /// <param name="fixture">
    /// File name inside the fetched ReGIS corpus.
    /// </param>
    /// <remarks>
    /// <para><b>What these judge that nothing else can</b></para>
    /// <c>hersheydemo.regis</c> is 44 KB of Hershey font output - far more text, at more sizes and
    /// tilts, than any sample written here. It is the fixture the gap document named as the one
    /// that would exercise the text command hard, and until now there was no text command to point
    /// it at.
    ///
    /// Drawn onto the plane at one pixel to one pixel, for the reason every other graphics artefact
    /// here now is: an 8 by 10 glyph squeezed through the screen scaling is a smudge whatever the
    /// decoder did.
    /// </remarks>
    [AvaloniaTheory]
    [MemberData(nameof(RegisFixtures))]
    public void M6_4d_EveryFetchedRegisFixtureIsDrawnToBeLookedAt(string fixture)
    {
        if (fixture == NotFetched)
        {
            return;
        }

        var surface = new InMemoryGraphicsSurface(RegisDecoder.DefaultWidth, RegisDecoder.DefaultHeight);
        var decoder = new RegisDecoder();

        // The files are raw ReGIS, as a host would send inside a DCS.
        decoder.Decode(File.ReadAllText(Path.Combine(RegisFolder, fixture)), surface);

        SavePlane(surface, "regis-corpus-" + Path.GetFileNameWithoutExtension(fixture));

        int lit = 0;
        for (int y = 0; y < surface.Height; y++)
        {
            for (int x = 0; x < surface.Width; x++)
            {
                if (!surface.GetPixel(x, y).IsTransparent) lit++;
            }
        }

        // resetpalette.regis sets the sixteen colour registers and draws NOTHING, which is what
        // it is for - it is the smallest thing in the corpus that exercises S(M...). A fixture
        // that only changes state legitimately leaves the plane blank, so it is named here rather
        // than weakening the check for every other file.
        if (fixture == "resetpalette.regis")
        {
            Assert.Equal(0, lit);
            return;
        }

        Assert.True(lit > 0, fixture + " drew nothing at all");
    }

    /// <summary>
    /// The registest drawings that have a capture from real hardware beside them.
    /// </summary>
    /// <returns>
    /// One case per picture, or a placeholder when they have not been captured.
    /// </returns>
    public static IEnumerable<object[]> RegisPicturesWithCaptures()
    {
        string[] pictures = { "grid", "checkerboard", "bitplane", "raf" };
        var found = new List<string>();

        if (Directory.Exists(RegisFolder))
        {
            for (int i = 0; i < pictures.Length; i++)
            {
                if (File.Exists(Path.Combine(RegisFolder, "registest-" + pictures[i] + ".regis"))
                    && File.Exists(Path.Combine(RegisFolder, "registest-" + pictures[i] + ".png")))
                {
                    found.Add(pictures[i]);
                }
            }
        }

        if (found.Count == 0)
        {
            yield return new object[] { NotFetched };
            yield break;
        }

        for (int i = 0; i < found.Count; i++)
        {
            yield return new object[] { found[i] };
        }
    }

    /// <summary>
    /// M6.4e - each registest drawing beside the capture a real VT340 made of it.
    /// </summary>
    /// <param name="picture">
    /// The picture name - grid, checkerboard, bitplane or raf.
    /// </param>
    /// <remarks>
    /// <para><b>These are the only ReGIS pictures with a hardware reference</b></para>
    /// Everything else in this file compares Sixel. hackerb9's <c>registest.sh</c> draws four
    /// pictures and photographed each one off a real VT340, and the drawings themselves live inside
    /// a shell script rather than in a file - so the fetch script RUNS the script and keeps its
    /// output. Transcribing it into C# would have meant copying somebody else's test content into
    /// this repository, which the rule here forbids.
    ///
    /// The grid one is the interesting one for the work just done: it is the only fixture anywhere
    /// that uses the text command, and it uses the temporary text control on every label.
    ///
    /// Ours is drawn on the plane at one pixel to one pixel and stacked above the capture, the same
    /// layout as the Sixel sheets.
    /// </remarks>
    [AvaloniaTheory]
    [MemberData(nameof(RegisPicturesWithCaptures))]
    public void M6_4e_EachRegistestDrawingSitsBesideItsHardwareCapture(string picture)
    {
        if (picture == NotFetched)
        {
            return;
        }

        var surface = new InMemoryGraphicsSurface(RegisDecoder.DefaultWidth, RegisDecoder.DefaultHeight);
        var decoder = new RegisDecoder();

        // The captured output is a whole DCS string - ESC P 1 p ... ESC \ - so the envelope is
        // stripped and the ReGIS inside is what the decoder is given.
        string stream = File.ReadAllText(Path.Combine(RegisFolder, "registest-" + picture + ".regis"));
        decoder.Decode(StripDeviceControlString(stream), surface);

        SavePlane(surface, "regis-registest-" + picture);

        // THE BOTTOM LABELS of the grid used to fall off the plane entirely, and now do not. That is
        // judged HERE BY EYE, on the comparison sheet, and pinned by machine in RegisPvSpacingTests
        // instead of by counting pixels on this plane. Counting here does not work: registest.sh
        // opens with S(E), which fills the whole plane with the background code, so every pixel is
        // opaque and "has ink" means nothing. An assertion written that way passed with the feature
        // switched off, which is exactly how it was caught.

        using var ours = SKBitmap.Decode(File.ReadAllBytes(
            Path.Combine(RenderedScreenshot.ImagesFolder, "regis-registest-" + picture + ".png")));
        using var hardware = SKBitmap.Decode(File.ReadAllBytes(
            Path.Combine(RegisFolder, "registest-" + picture + ".png")));

        Assert.NotNull(ours);
        Assert.NotNull(hardware);

        int width = Math.Max(ours.Width, hardware.Width);
        int height = LabelHeight + ours.Height + SeparatorHeight + LabelHeight + hardware.Height;

        using var sheet = new SKBitmap(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul));
        using (var canvas = new SKCanvas(sheet))
        {
            canvas.Clear(new SKColor(24, 24, 24));

            int oursTop = LabelHeight;
            int barTop = oursTop + ours.Height;
            int hardwareTop = barTop + SeparatorHeight + LabelHeight;

            canvas.DrawBitmap(ours, 0, oursTop, SKSamplingOptions.Default);

            using (var bar = new SKPaint())
            {
                bar.Color = new SKColor(255, 128, 0);
                bar.Style = SKPaintStyle.Fill;
                canvas.DrawRect(new SKRect(0, barTop, width, barTop + SeparatorHeight), bar);
            }

            canvas.DrawBitmap(hardware, 0, hardwareTop, SKSamplingOptions.Default);

            // Named last so the text sits ON TOP of both pictures rather than under them.
            Label(canvas, "OURS - registest-" + picture + " through our decoder   "
                + ours.Width + " x " + ours.Height, 0);

            // The bitplane capture is a photograph of hackerb9's WHOLE SCREEN - emacs holding
            // registest.sh, a modeline, a shell prompt - with the circles drawn over it. None of
            // that text was ever sent to the terminal: registest-bitplane.regis is 411 bytes of
            // circles and colour-map writes and contains no text command at all. Ronny reported
            // the missing text as a defect on 1 September 2026, which it is not, so the sheet now
            // says so itself rather than waiting to mislead the next person who judges it.
            string hardwareLabel = "REAL HARDWARE - registest-" + picture + ".png   "
                + hardware.Width + " x " + hardware.Height;
            if (picture == "bitplane")
            {
                hardwareLabel += "   - FULL-SCREEN PHOTO: the editor and shell text below is NOT"
                    + " in the fixture, compare the CIRCLES only";
            }

            Label(canvas, hardwareLabel, barTop + SeparatorHeight);
        }

        try
        {
            using var data = sheet.Encode(SKEncodedImageFormat.Png, 100);
            using var file = File.Create(Path.Combine(RenderedScreenshot.ImagesFolder,
                "compare-registest-" + picture + ".png"));
            data.SaveTo(file);
        }
        catch (IOException)
        {
        }

        Assert.True(HasInk(ours), picture + " drew nothing - there is nothing to compare");
        Assert.True(HasInk(hardware), "registest-" + picture + ".png is blank - the fetch is damaged");

        // THE GRID'S TWO PATTERNED RULES, COUNTED AGAINST THE HARDWARE. This is the only assertion
        // in the program whose expected value is a real VT340 rather than a document: the grid's
        // top and bottom edges are drawn with W(P10(M2)), a two-bit pattern stretched to two pixels
        // a bit, and the capture says how many pixels that leaves lit across 800.
        //
        // It read 800 against the hardware's 400 until pattern memory was built, because every line
        // was solid. Both rules are compared, because a pattern that started on the wrong bit would
        // match one of them and not the other.
        if (picture == "grid")
        {
            int[] rules = { 0, ours.Height - 1 };

            for (int r = 0; r < rules.Length; r++)
            {
                int row = rules[r];
                Assert.Equal(LitPixelsOnRow(hardware, row), LitPixelsOnRow(ours, row));
            }
        }
    }

    /// <summary>
    /// Counts pixels on one row that carry ink rather than the background.
    /// </summary>
    /// <param name="image">
    /// The picture to measure.
    /// </param>
    /// <param name="row">
    /// Which row.
    /// </param>
    /// <returns>
    /// How many pixels are lit.
    /// </returns>
    /// <remarks>
    /// Ink is anything appreciably brighter than black in the green channel. Both pictures here draw
    /// this rule in white on a dark ground, so a single threshold reads both without knowing which
    /// is which - the capture's own background is not pure black.
    /// </remarks>
    private static int LitPixelsOnRow(SKBitmap image, int row)
    {
        if (row < 0 || row >= image.Height) return 0;

        int lit = 0;
        for (int x = 0; x < image.Width; x++)
        {
            if (image.GetPixel(x, row).Green > 60) lit++;
        }

        return lit;
    }

    /// <summary>
    /// Strips the DCS envelope a captured stream arrives in, leaving the ReGIS itself.
    /// </summary>
    /// <param name="stream">
    /// What the script emitted.
    /// </param>
    /// <returns>
    /// The characters between the introducer and the terminator.
    /// </returns>
    /// <remarks>
    /// The terminal does this itself when a host sends one; here the decoder is being driven
    /// directly, so the envelope has to come off first or its <c>1p</c> would be read as ReGIS.
    /// </remarks>
    private static string StripDeviceControlString(string stream)
    {
        int start = stream.IndexOf('p');

        // The FIRST terminator, not the last. Two of these captures hold more than one ReGIS block,
        // and the blocks after the first are not part of the picture that was photographed:
        // registest.sh's bitplane drawing swaps green and magenta, then stops at
        //
        //     read -s -n1 -p "Magenta and green swapped. Hit any key to reset colormap..."
        //
        // and only undoes the swap once a key is pressed. hackerb9 took the photograph while it was
        // waiting. Reading to the last terminator would run the undo as well and compare a picture
        // the camera never saw - and would feed the decoder the cursor-positioning sequences
        // between the blocks into the bargain.
        int end = stream.IndexOf((char)0x1B, start + 1);

        if (start < 0 || end <= start)
        {
            return stream;
        }

        return stream.Substring(start + 1, end - start - 1);
    }

    /// <summary>
    /// M6.4h - hackerb9's faketextcolor trick, which really does give multicoloured text.
    /// </summary>
    /// <remarks>
    /// <para><b>The trick</b></para>
    /// A VT340 cannot show multicoloured text: "the value of any pixel of text is 0111", code 7,
    /// and bold text is 1111, code 15, so recolouring one text pixel recolours every one of them.
    /// hackerb9 gets round it by writing index 0 through a PLANE MASK. A text pixel becomes
    /// <c>old AND NOT mask</c>, while the background - already 0000 - does not move at all. Sixteen
    /// boxes through sixteen masks give sixteen values, and the letters come out in different
    /// colours while the screen behind them is untouched.
    ///
    /// <para><b>What this emulator does</b></para>
    /// Text is a cell buffer here rather than part of the bitmap, so the same operation is applied
    /// per CELL - see <c>TerminalCell.RegisColorIndex</c>. It lands in the right place because the
    /// geometry agrees: the plane is 800 by 480, a cell on an 80 by 24 screen is exactly 10 by 20,
    /// and the boxes are 20 wide, so each covers exactly two characters.
    ///
    /// <para><b>The prediction worth checking against the photograph</b></para>
    /// Ordinary text is 7, which has no bit 3. So mask i and mask i+8 leave it at the same value,
    /// and the plain row must show EIGHT colours repeating twice. The bold row starts at 15, where
    /// every bit counts, so it must show sixteen different values ending in 0 - a pair of letters
    /// the same colour as the background, which is to say invisible.
    /// </remarks>
    [AvaloniaFact]
    public void M6_4h_TheFakeTextColourTrickRecoloursTheLettersItPassesOver()
    {
        var emulator = EmulatorFactory.CreateEmulator("VT340", 80, 24, 100);

        // The fixture's own opening: erase, PV multiplier 20, draw in colour index 0.
        emulator.ProcessData(Encoding.ASCII.GetBytes("P1p S(E) W(M20,I0) \\"));

        // Two rows of text where the fixture puts them.
        emulator.ProcessData(Encoding.ASCII.GetBytes(
            "[11;26HABCDEFGHIJKLMNOPQRSTUVWXYZ012345"));
        emulator.ProcessData(Encoding.ASCII.GetBytes(
            "[13;26H[1mABCDEFGHIJKLMNOPQRSTUVWXYZ012345[0m"));

        // Sixteen filled boxes, one per plane mask, straight across each row of text - the heart of
        // the trick. 0642 is east, south, west, north at twenty coordinates a step. Both blocks are
        // sent, exactly as the fixture does: y 200 covers the plain row, y 240 the bold one.
        PlayTheSixteenBoxes(emulator, 200);
        PlayTheSixteenBoxes(emulator, 240);

        using var shot = RenderedScreenshot.Capture(emulator, "regis-faketextcolor-ours", saveZoom: 1);

        // The letters are untouched as CHARACTERS. Only their pixel value changed, which is all the
        // hardware changes either.
        Assert.Equal((uint)'A', emulator.Buffer[10, 25].Codepoint);
        Assert.Equal((uint)'A', emulator.Buffer[12, 25].Codepoint);

        Assert.NotNull(emulator.Regis);
        Assert.False(emulator.Regis!.UnhandledCommands.ContainsKey('V'));

        // Box i starts at plane x = 250 + 20i and is 20 wide, so it covers columns 25+2i and
        // 26+2i. Its right edge lands on the FIRST PIXEL of column 27+2i, which is the left-hand
        // cell of the next box - so that cell takes two masks, this box's and the next one's.
        //
        // On the hardware that seam is one pixel column of one character and nobody would ever see
        // it. Here a cell is the smallest thing that can hold a colour, so one stray pixel colours
        // the whole character. That is the approximation this design makes, stated out loud, and it
        // is why the checks below take the cell that is WHOLLY inside each box - 26+2i - rather than
        // both of them.
        Assert.False(emulator.Buffer[10, 26].HasRegisColor);   // mask 0 touches no plane at all
        Assert.False(emulator.Buffer[12, 26].HasRegisColor);

        for (int mask = 1; mask < 16; mask++)
        {
            int column = 26 + mask * 2;

            Assert.Equal(7 & ~mask, emulator.Buffer[10, column].RegisColorIndex);

            // Bold text starts at 1111, so every one of the four bits does something.
            Assert.Equal(15 & ~mask, emulator.Buffer[12, column].RegisColorIndex);
        }

        // The eight-colours-twice prediction, stated as an assertion rather than left in prose:
        // plain text has no bit 3, so masks 1 and 9 leave it at the same value. Bold text has one,
        // so they do not.
        Assert.Equal(emulator.Buffer[10, 28].RegisColorIndex, emulator.Buffer[10, 44].RegisColorIndex);
        Assert.NotEqual(emulator.Buffer[12, 28].RegisColorIndex, emulator.Buffer[12, 44].RegisColorIndex);

        // NOTHING was painted onto the graphics plane. On the hardware these boxes are invisible:
        // every one of them writes index 0 into a background that is already 0, so the value never
        // moves. Painting them anyway is what used to put a black bar over the letters.
        var plane = emulator.Graphics!.FindPlane(TerminalEmulatorBase.RegisPlaneId);
        Assert.NotNull(plane);

        int lit = 0;
        var surface = plane!.Surface;
        for (int y = 0; y < surface.Height; y++)
        {
            for (int x = 0; x < surface.Width; x++)
            {
                if (!surface.GetPixel(x, y).IsTransparent) lit++;
            }
        }

        Assert.Equal(0, lit);

        // And the pixels agree with the buffer. Two pairs that the plane values say must differ,
        // read off the rendered screen rather than trusted.
        var first = shot.BrightestColorInCell(10, 28);
        var second = shot.BrightestColorInCell(10, 32);
        Assert.False(RenderedScreenshot.ApproximatelyEqual(first, second, 8),
            $"columns 28 and 32 hold plane values {emulator.Buffer[10, 28].RegisColorIndex} and "
            + $"{emulator.Buffer[10, 32].RegisColorIndex} but rendered the same colour {first}");


        // The visible colours, MEASURED off hackerb9's photograph of a real VT340 - the brightest
        // pixel of each pair of letters on the plain row. Code 0 shows nothing at all.
        //
        // Asserting against the hardware rather than against our own map pins the whole chain in
        // one place: the plane value the trick produces, the colour the map turns it into, and the
        // brush the renderer paints with. Any one of the three going wrong fails here.
        int[] hardwareCode = { 6, 5, 4, 3, 2, 1 };
        byte[] hardwareRed = { 201, 51, 201, 51, 201, 51 };
        byte[] hardwareGreen = { 201, 201, 51, 201, 33, 51 };
        byte[] hardwareBlue = { 51, 201, 201, 51, 33, 201 };

        for (int n = 0; n < hardwareCode.Length; n++)
        {
            var ours = emulator.GraphicsColorMap.Register(hardwareCode[n]);

            Assert.True(
                Math.Abs(ours.R - hardwareRed[n]) <= 8
                && Math.Abs(ours.G - hardwareGreen[n]) <= 8
                && Math.Abs(ours.B - hardwareBlue[n]) <= 8,
                $"colour map register {hardwareCode[n]} is ({ours.R},{ours.G},{ours.B}); the VT340 "
                + $"photograph shows ({hardwareRed[n]},{hardwareGreen[n]},{hardwareBlue[n]})");
        }

        // REGISTER 7 IS THE ONE THAT DISAGREES, and it is left disagreeing on purpose.
        // The manual's own default map calls it "gray 50%" and gives 53 percent, which is 135. The
        // photograph shows 117, which is 46 percent. Every other register in the picture agrees
        // within eight, so this is not the image being re-encoded loosely.
        // Nothing here can say which is right - it needs a VT340 - so our value stays the manual's,
        // and the measurement is written down rather than tuned away.
        Assert.Equal(135, emulator.GraphicsColorMap.Register(7).R);

        SavePlane(surface, "regis-faketextcolor-plane");
    }

    /// <summary>
    /// M6.4i - what happens to colourised text when it moves, checked against the four cases
    /// hackerb9 measured on a real VT340.
    /// </summary>
    /// <remarks>
    /// His notes under "Potential problems" record all four:
    ///  - shifted DOWN by reverse index: colour survives.
    ///  - shifted UP by newline: colour survives.
    ///  - shifted LEFT by delete character: "Colors are removed from the line".
    ///  - shifted RIGHT by insert mode: colours are removed as well.
    /// The split is not arbitrary. A vertical scroll moves the bitmap along with the text, so the
    /// values travel with the characters. A sideways shift redraws the characters where they now
    /// belong, and drawing a character writes the ordinary text value over whatever was underneath.
    /// </remarks>
    [AvaloniaFact]
    public void M6_4i_ColouredTextSurvivesScrollingAndIsLostWhenTheLineShiftsSideways()
    {
        var emulator = EmulatorFactory.CreateEmulator("VT340", 80, 24, 100);

        emulator.ProcessData(Encoding.ASCII.GetBytes("P1p S(E) W(M20,I0) \\"));
        emulator.ProcessData(Encoding.ASCII.GetBytes(
            "[11;26HABCDEFGHIJKLMNOPQRSTUVWXYZ012345"));
        PlayTheSixteenBoxes(emulator, 200);

        // Column 28 sits wholly inside the box drawn through plane mask 1, so it holds 7 AND NOT 1.
        const int Coloured = 28;
        Assert.Equal(6, emulator.Buffer[10, Coloured].RegisColorIndex);

        // Down one row - reverse index at the top of the screen.
        emulator.ProcessData(Encoding.ASCII.GetBytes("[1;1HM"));
        Assert.Equal((uint)'D', emulator.Buffer[11, Coloured].Codepoint);
        Assert.Equal(6, emulator.Buffer[11, Coloured].RegisColorIndex);

        // And back up again - index at the bottom.
        emulator.ProcessData(Encoding.ASCII.GetBytes("[24;1HD"));
        Assert.Equal((uint)'D', emulator.Buffer[10, Coloured].Codepoint);
        Assert.Equal(6, emulator.Buffer[10, Coloured].RegisColorIndex);

        // Sideways, and the colour goes. Delete five characters from the front of the line.
        emulator.ProcessData(Encoding.ASCII.GetBytes("[11;1H[5P"));

        for (int col = 0; col < 80; col++)
        {
            Assert.False(emulator.Buffer[10, col].HasRegisColor,
                $"column {col} kept its colour through a delete-character");
        }
    }

    /// <summary>
    /// Sends hackerb9's sixteen filled boxes, one per plane mask, along one row of the plane.
    /// </summary>
    /// <param name="emulator">
    /// Terminal to send them to.
    /// </param>
    /// <param name="planeRow">
    /// Top edge of the boxes in plane pixels; 200 is the plain row, 240 the bold one.
    /// </param>
    private static void PlayTheSixteenBoxes(TerminalEmulatorBase emulator, int planeRow)
    {
        var boxes = new StringBuilder();
        boxes.Append("P0p").Append("P[250,").Append(planeRow).Append(']');

        for (int mask = 0; mask < 16; mask++)
        {
            boxes.Append("F(V(W(F").Append(mask).Append(")) 0642) V0 ");
        }

        boxes.Append("\\");
        emulator.ProcessData(Encoding.ASCII.GetBytes(boxes.ToString()));
    }

    /// <summary>
    /// M6.4f - one panel for each write control, on a sheet that can be judged by eye.
    /// </summary>
    /// <remarks>
    /// <para><b>Why a made-up sheet when four hardware captures exist</b></para>
    /// The captures are worth more than anything drawn here, but between them they leave two of
    /// these controls untested: no registest drawing uses a vertical shading reference line, and
    /// none uses replace writing in a way that shows what it does. This sheet exercises each control
    /// on its own, so a regression names itself instead of showing up as a slightly wrong picture.
    ///
    /// One pixel per plane pixel, like every other artefact here. The thing being judged is a few
    /// pixels wide, and scaling has twice now produced a picture that lied.
    /// </remarks>
    [AvaloniaFact]
    public void M6_4f_TheWriteControlsEachDrawTheirOwnPanel()
    {
        const int PanelWidth = 120;
        const int PanelHeight = 60;

        var surface = new InMemoryGraphicsSurface(PanelWidth * 3, PanelHeight * 2);
        var decoder = new RegisDecoder();

        // Panel 1 - plane select. Three bars in three planes; the crossings take a fourth and fifth
        // colour because the CODE is being written, not the colour.
        decoder.Decode("W(F1I15)P[10,10]V[110,10]P[10,20]V[110,20]"
            + "W(F2I15)P[30,5]V[30,50]P[40,5]V[40,50]"
            + "W(F4I15)P[60,5]V[60,50]W(F15)", surface);

        // Panel 2 - shading to a row. Panel 3 - shading to a column. Both give a solid disc.
        decoder.Decode("P[180,30]W(S1)C[205,30]W(S0)", surface);
        decoder.Decode("W(S(X)[300])P[300,30]C[325,30]W(S0)", surface);

        // Panel 4 - the same dashed line with negation off, then on.
        decoder.Decode("W(I3P2(M2)N0)P[10,70]V[110,70]"
            + "W(N1)P[10,80]V[110,80]W(N0)", surface);

        // Panel 5 - a solid bar, then the same dashed line over it in overlay and in replace.
        decoder.Decode("W(I15P1)P[130,70]V[230,70]P[130,85]V[230,85]"
            + "W(I1P2(M2)V)P[130,70]V[230,70]"
            + "W(R)P[130,85]V[230,85]W(V)", surface);

        // Panel 6 - a drawing, then two map locations moved under it. Nothing is redrawn.
        decoder.Decode("W(I3P1)P[250,70]V[350,70]P[250,80]V[350,80]"
            + "S(M3(H120 L49 S59))", surface);

        SavePlane(surface, "regis-write-controls");

        // Panel 1: the crossing of two planes is a colour that is NEITHER of them.
        var alongOne = surface.GetPixel(20, 10);
        var downOne = surface.GetPixel(30, 30);
        var crossing = surface.GetPixel(30, 10);
        Assert.NotEqual(alongOne.Value, crossing.Value);
        Assert.NotEqual(downOne.Value, crossing.Value);
        Assert.False(crossing.IsTransparent);

        // Panels 2 and 3: shaded circles are solid, to a row and to a column alike.
        Assert.False(surface.GetPixel(180, 30).IsTransparent);
        Assert.False(surface.GetPixel(300, 30).IsTransparent);

        // Panel 4: negation inverts. Every pixel of the second line is the opposite of the first.
        int opposed = 0;
        for (int x = 12; x < 108; x++)
        {
            if (surface.GetPixel(x, 70).IsTransparent != surface.GetPixel(x, 80).IsTransparent)
            {
                opposed++;
            }
        }

        Assert.Equal(96, opposed);

        // Panel 5: overlay leaves the bar showing in the gaps, replace erases it. The sample must be
        // in a GAP - P2 at multiplier 2 runs eight lit then eight dark from x=130, so x=140 is dark
        // and x=133 is not. Sampling a dash instead compares two identical blue pixels and says
        // nothing, which is exactly what this assertion did first time round.
        var overlayGap = surface.GetPixel(140, 70);
        var replaceGap = surface.GetPixel(140, 85);
        Assert.NotEqual(overlayGap.Value, replaceGap.Value);
        Assert.Equal(new GraphicsColorMap().Register(15).Value, overlayGap.Value);
        Assert.Equal(new GraphicsColorMap().Register(0).Value, replaceGap.Value);

        // Panel 6: the drawing changed colour without being redrawn.
        uint wasGreen = new GraphicsColorMap().Register(3).Value;
        Assert.NotEqual(wasGreen, surface.GetPixel(300, 70).Value);
        Assert.False(surface.GetPixel(300, 70).IsTransparent);
    }

    /// <summary>
    /// Writes a surface out at one image pixel per plane pixel.
    /// </summary>
    /// <param name="surface">
    /// What to write.
    /// </param>
    /// <param name="fileName">
    /// Name for the PNG, without an extension.
    /// </param>
    private static void SavePlane(InMemoryGraphicsSurface surface, string fileName)
    {
        using var image = new SKBitmap(
            new SKImageInfo(surface.Width, surface.Height, SKColorType.Bgra8888, SKAlphaType.Premul));

        var ground = new SKColor(0, 20, 12);

        for (int y = 0; y < surface.Height; y++)
        {
            for (int x = 0; x < surface.Width; x++)
            {
                var pixel = surface.GetPixel(x, y);
                image.SetPixel(x, y, pixel.IsTransparent ? ground : new SKColor(pixel.R, pixel.G, pixel.B));
            }
        }

        var folder = RenderedScreenshot.ImagesFolder;
        Directory.CreateDirectory(folder);

        try
        {
            using var data = image.Encode(SKEncodedImageFormat.Png, 100);
            using var file = File.Create(Path.Combine(folder, fileName + ".png"));
            data.SaveTo(file);
        }
        catch (IOException)
        {
        }
    }

    /// <summary>
    /// M6.1e - every paired fixture is also dumped at its NATIVE size, for a pixel-exact look.
    /// </summary>
    /// <param name="fixture">
    /// The fixture name, from <see cref="PairedFixtures"/>.
    /// </param>
    /// <remarks>
    /// <para><b>Why the comparison sheet is not enough</b></para>
    /// The sheet shows the SCREEN: the graphics plane stretched over the text area, which is a
    /// different size, with the blending that implies. That is the right picture for judging what a
    /// user sees, and the wrong one for judging a decoder - a single red pixel next to a black one
    /// arrives on the sheet as a dark red that is neither.
    ///
    /// A hardware capture is close to the plane's own size, so this dump is what can be compared
    /// pixel for pixel. It is the same lesson the Tektronix dash patterns taught: when the thing
    /// being judged is smaller than the scaling, scale nothing.
    /// </remarks>
    [AvaloniaTheory]
    [MemberData(nameof(PairedFixtures))]
    public void M6_1e_EveryPairedFixtureIsAlsoDumpedAtItsNativeSize(string fixture)
    {
        if (fixture == NotFetched)
        {
            return;
        }

        var emulator = Play(fixture);

        var compositor = emulator.Graphics;
        Assert.NotNull(compositor);
        compositor!.Composite();
        var surface = compositor.Output;

        using var image = new SKBitmap(
            new SKImageInfo(surface.Width, surface.Height, SKColorType.Bgra8888, SKAlphaType.Premul));

        var ground = new SKColor(0, 0, 0);

        for (int y = 0; y < surface.Height; y++)
        {
            for (int x = 0; x < surface.Width; x++)
            {
                var pixel = surface.GetPixel(x, y);
                image.SetPixel(x, y, pixel.A == 0 ? ground : new SKColor(pixel.R, pixel.G, pixel.B));
            }
        }

        var folder = RenderedScreenshot.ImagesFolder;
        Directory.CreateDirectory(folder);

        try
        {
            using var data = image.Encode(SKEncodedImageFormat.Png, 100);
            using var file = File.Create(Path.Combine(folder, "plane-" + fixture + ".png"));
            data.SaveTo(file);
        }
        catch (IOException)
        {
        }

        Assert.True(HasInk(image), fixture + " decoded to a blank plane");
    }

    /// <summary>
    /// M6.1b - the cat's four colours convert exactly as the hardware drew them.
    /// </summary>
    /// <remarks>
    /// <para><b>A rare thing: a colour conversion checked against real hardware</b></para>
    /// <c>cat-vt340.six</c> defines four colours and no more, all in HLS:
    ///
    ///  - <c>#1;1;0;0;0</c> - lightness zero, so black whatever the hue says.
    ///  - <c>#2;1;120;50;100</c> - fully saturated at half lightness. DEC's hue 120 is RED.
    ///  - <c>#3;1;0;99;0</c> - no saturation at full lightness, so white.
    ///  - <c>#0;1;280;35;60</c> - the green this fixture puts behind everything.
    ///
    /// The capture beside it was taken off a real VT340, and its background pixels measure
    /// 36,138,102. Our conversion answers 36,143,107 for the same three numbers - within five per
    /// channel of a photographed screen, which is as close as that comparison can get.
    ///
    /// That is worth pinning, because it confirms the part of the conversion that is easiest to get
    /// wrong and hardest to notice: DEC's hue is offset so that 0 degrees is BLUE. Getting it wrong
    /// rotates every colour by a third of the wheel, which looks like a plausible picture in the
    /// wrong colours rather than like a fault.
    /// </remarks>
    [Fact]
    public void M6_1b_TheCatsFourColoursConvertAsTheHardwareDrewThem()
    {
        var black = GraphicsColorMap.FromHls(0, 0, 0);
        Assert.Equal(0, black.R);
        Assert.Equal(0, black.G);
        Assert.Equal(0, black.B);

        var red = GraphicsColorMap.FromHls(120, 50, 100);
        Assert.Equal(255, red.R);
        Assert.Equal(0, red.G);
        Assert.Equal(0, red.B);

        var white = GraphicsColorMap.FromHls(0, 99, 0);
        Assert.True(white.R >= 250 && white.G >= 250 && white.B >= 250,
            $"lightness 99 with no saturation is white; got {white.R},{white.G},{white.B}");

        // The measured one. 36,138,102 is what the real VT340 put on the glass.
        var green = GraphicsColorMap.FromHls(280, 35, 60);
        Assert.True(Math.Abs(green.R - 36) <= 5, $"red channel {green.R}, hardware measured 36");
        Assert.True(Math.Abs(green.G - 138) <= 6, $"green channel {green.G}, hardware measured 138");
        Assert.True(Math.Abs(green.B - 102) <= 6, $"blue channel {green.B}, hardware measured 102");
    }

    /// <summary>
    /// M6.1f - the cat turns the VT340's screen green, the way the hardware capture shows.
    /// </summary>
    /// <remarks>
    /// <para><b>The VT340's peculiar ordering scheme, end to end</b></para>
    /// <c>cat-vt340.six</c> defines exactly sixteen colours, in an order that has nothing to do
    /// with their register numbers, and its own comment says why: on a VT340 the SIXTH colour
    /// defined becomes the text foreground and the SIXTEENTH the text background. The sixteenth it
    /// defines is <c>#0;1;280;35;60</c> - the green that fills the whole screen of the capture
    /// taken from real hardware, measured at 36,138,102.
    ///
    /// This asserts the whole path: the decoder counts definitions in order, the emulator adopts
    /// the pair because its profile claims the behaviour, and the colour that arrives is the one
    /// the hardware showed.
    /// </remarks>
    [AvaloniaFact]
    public void M6_1f_TheCatSetsTheScreenBackgroundTheWayTheHardwareShows()
    {
        if (!File.Exists(Path.Combine(CorpusFolder, "cat-vt340.six")))
        {
            return;
        }

        var emulator = Play("cat-vt340");

        Assert.True(emulator.ImageTextBackground.HasValue,
            "the image defines sixteen colours and none became the text background");

        var background = emulator.ImageTextBackground!.Value;

        // What the real VT340 put on the glass, within the tolerance a photographed screen allows.
        Assert.True(Math.Abs(background.R - 36) <= 5, $"red channel {background.R}, hardware measured 36");
        Assert.True(Math.Abs(background.G - 138) <= 6, $"green channel {background.G}, hardware measured 138");
        Assert.True(Math.Abs(background.B - 102) <= 6, $"blue channel {background.B}, hardware measured 102");

        // The sixth defined is #7 - grey at 46 percent lightness with no saturation.
        Assert.True(emulator.ImageTextForeground.HasValue,
            "the sixth colour defined should have become the text foreground");
        var foreground = emulator.ImageTextForeground!.Value;
        Assert.Equal(foreground.R, foreground.G);
        Assert.Equal(foreground.G, foreground.B);
    }

    /// <summary>
    /// M6.1f - an image with fewer than six colours changes nothing.
    /// </summary>
    /// <remarks>
    /// The guard. Ordinary Sixel images define two or three colours and must leave the terminal's
    /// text colours alone - a rule that fires on every picture would repaint the screen behind any
    /// program that showed one.
    /// </remarks>
    [AvaloniaFact]
    public void M6_1f_AnImageWithFewerThanSixColoursLeavesTheTextColoursAlone()
    {
        var emulator = EmulatorFactory.CreateEmulator("VT340", 80, 24, 100);

        var image = new System.Text.StringBuilder();
        image.Append((char)0x1B).Append("Pq");
        image.Append("#1;2;100;0;0#2;2;0;100;0#3;2;0;0;100");
        image.Append("#1~~~");
        image.Append((char)0x1B).Append('\\');

        emulator.ProcessData(System.Text.Encoding.Latin1.GetBytes(image.ToString()));

        Assert.True(emulator.Graphics != null && emulator.Graphics.HasAnythingToDraw(),
            "the setup image drew nothing, so this proves nothing about the rule");
        Assert.False(emulator.ImageTextForeground.HasValue,
            "three colours must not set the text foreground");
        Assert.False(emulator.ImageTextBackground.HasValue,
            "three colours must not set the text background");
    }

    /// <summary>
    /// M6.1f - an xterm draws the same image and keeps its own colours.
    /// </summary>
    /// <remarks>
    /// The rule is one model's, and this is what keeps it there. xterm and libsixel both draw Sixel
    /// and neither does this, so a terminal that claimed the behaviour by accident would repaint
    /// the screen of every modern program that shows a picture.
    /// </remarks>
    [AvaloniaFact]
    public void M6_1f_AnXtermIgnoresTheOrderingRuleEntirely()
    {
        if (!File.Exists(Path.Combine(CorpusFolder, "cat-vt340.six")))
        {
            return;
        }

        var emulator = EmulatorFactory.CreateEmulator("XTERM", 80, 24, 100);
        emulator.ProcessData(File.ReadAllBytes(Path.Combine(CorpusFolder, "cat-vt340.six")));

        Assert.False(emulator.ImageTextForeground.HasValue,
            "an xterm adopted a VT340 quirk it never had");
        Assert.False(emulator.ImageTextBackground.HasValue,
            "an xterm adopted a VT340 quirk it never had");
    }

    /// <summary>
    /// True when a bitmap has more than one colour in it, which is the cheapest honest test for
    /// "something was drawn".
    /// </summary>
    /// <param name="bitmap">
    /// The image to look at.
    /// </param>
    /// <returns>
    /// False for an image that is one flat colour.
    /// </returns>
    /// <remarks>
    /// Sampled on a grid rather than pixel by pixel: a hardware capture can be a megapixel
    /// photograph, and this question does not need every pixel of it. The step is small enough that
    /// no picture in the corpus can hide its whole subject between samples.
    /// </remarks>
    private static bool HasInk(SKBitmap bitmap)
    {
        SKColor first = bitmap.GetPixel(0, 0);

        for (int y = 0; y < bitmap.Height; y += 4)
        {
            for (int x = 0; x < bitmap.Width; x += 4)
            {
                if (bitmap.GetPixel(x, y) != first)
                {
                    return true;
                }
            }
        }

        return false;
    }
}
