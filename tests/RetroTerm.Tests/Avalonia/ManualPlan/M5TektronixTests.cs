using System;
using System.IO;
using System.Text;
using Avalonia.Headless.XUnit;
using RetroTerm.Core.Terminal.Emulators.Tektronix;
using SkiaSharp;
using Xunit;

namespace RetroTerm.Tests.Avalonia.ManualPlan;

/// <summary>
/// The machine half of the by-hand pass in
/// <c>docs\manual-tests\M5-TEKTRONIX.md</c>.
/// </summary>
/// <remarks>
/// <para><b>Why these draw sheets rather than assert</b></para>
/// Three of the 4014's features are settled by the manual in words and by nothing at all in
/// numbers. The five vector patterns are named - solid, dotted, dot-dash, short dash, long dash -
/// and their dash LENGTHS are not printed anywhere, so the masks in <c>LinePatternMask</c> are this
/// emulator's reading of those five words. An assertion on exact pixels would only test that
/// reading against itself.
///
/// So these produce the picture the reading is judged against, and the manual document says plainly
/// that if a pattern looks wrong then it is wrong and the mask is what to change. The assertions
/// stay on what can be stated without an opinion: that ink reached the plane, and that a pattern
/// with gaps has fewer lit pixels than a solid line of the same length.
/// </remarks>
[Collection("Avalonia")]
public class M5TektronixTests
{
    private const byte Gs = 0x1D;

    /// <summary>
    /// Height of the bar drawn between stacked panels on a sheet.
    /// </summary>
    private const int SeparatorHeight = 4;

    /// <summary>
    /// The four bytes for a point, in the order a host sends them.
    /// </summary>
    /// <param name="x">
    /// Tektronix x, 0 to 1023.
    /// </param>
    /// <param name="y">
    /// Tektronix y, 0 to 779.
    /// </param>
    /// <returns>
    /// High y, low y, high x, low x - the order a 4010 expects.
    /// </returns>
    private static byte[] Point(int x, int y) => new[]
    {
        (byte)(0x20 | ((y >> 5) & 0x1F)),
        (byte)(0x60 | (y & 0x1F)),
        (byte)(0x20 | ((x >> 5) & 0x1F)),
        (byte)(0x40 | (x & 0x1F)),
    };

    private static void Feed(Tek4014Emulator emulator, params byte[] bytes)
        => emulator.ProcessData(bytes);

    private static void Feed(Tek4014Emulator emulator, string text)
        => emulator.ProcessData(Encoding.ASCII.GetBytes(text));

    /// <summary>
    /// M5.1a - the five vector patterns, drawn one above the other to be judged.
    /// </summary>
    /// <remarks>
    /// One long horizontal line per pattern, in the manual's own order, far enough apart to read.
    /// The 4014 plane is 1024 by 780, so the lines run from x=60 to x=960 with 130 units between
    /// rows.
    ///
    /// <para><b>Why this dumps the PLANE instead of taking a screenshot</b></para>
    /// It was a screenshot first, and the sheet lied. The renderer stretches the 1024-wide plane
    /// over a text area about 535 pixels across, and a pattern that alternates ONE pixel on with
    /// one off cannot survive being sampled at roughly half scale - it aliases into runs of several
    /// pixels. On that sheet the dotted line showed the LONGEST marks of the three and the short
    /// dash showed the shortest, which is the reverse of what the masks say and of what the tests
    /// beside them pin.
    ///
    /// The masks are what is being judged here, so the plane is the honest picture of them: one
    /// plane pixel to one image pixel, no scaling anywhere. What a real screen then does with it is
    /// a separate question, and the other Tektronix artefacts already show that.
    /// </remarks>
    [AvaloniaFact]
    public void M5_1a_TheFiveVectorPatternsAreDrawnToBeJudged()
    {
        var emulator = new Tek4014Emulator();

        // ESC ` a b c d - solid, dotted, dot-dash, short dash, long dash.
        string[] selects = { "\u001b`", "\u001ba", "\u001bb", "\u001bc", "\u001bd" };

        for (int i = 0; i < selects.Length; i++)
        {
            Feed(emulator, selects[i]);

            int y = 700 - (i * 130);
            Feed(emulator, Gs);
            Feed(emulator, Point(60, y));
            Feed(emulator, Point(960, y));
        }

        Assert.True(emulator.Graphics!.HasAnythingToDraw(),
            "no pattern reached the plane - there is nothing to judge");

        SavePlaneAtFullSize(emulator, "tek-line-patterns");
    }

    /// <summary>
    /// M5.1a - the dash lengths come out in the order their names promise.
    /// </summary>
    /// <remarks>
    /// Measured on the PLANE, for the reason above: at screen scale the answer is aliasing rather
    /// than the pattern. Dotted must mark shortest, the short dash longer, the long dash longer
    /// still. That ordering is the only thing the manual's five words actually promise, and it is
    /// the first thing a person looking at the sheet will check.
    /// </remarks>
    [AvaloniaFact]
    public void M5_1a_TheDashesRunInTheOrderTheirNamesPromise()
    {
        int dotted = LongestMarkOnOneLine("\u001ba");
        int shortDash = LongestMarkOnOneLine("\u001bc");
        int longDash = LongestMarkOnOneLine("\u001bd");

        Assert.True(dotted < shortDash,
            $"a dot ({dotted} pixels) must be shorter than a short dash ({shortDash})");
        Assert.True(shortDash < longDash,
            $"a short dash ({shortDash} pixels) must be shorter than a long dash ({longDash})");
    }

    /// <summary>
    /// Writes the composited graphics plane out at one image pixel per plane pixel.
    /// </summary>
    /// <param name="emulator">
    /// The emulator whose plane should be dumped.
    /// </param>
    /// <param name="fileName">
    /// Name for the PNG, without an extension.
    /// </param>
    /// <remarks>
    /// Transparent plane pixels become the dark ground a terminal shows behind them, so the picture
    /// reads the way a screen does without pretending to be a screenshot.
    /// </remarks>
    private static void SavePlaneAtFullSize(Tek4014Emulator emulator, string fileName)
    {
        var compositor = emulator.Graphics!;
        compositor.Composite();
        var surface = compositor.Output;

        using var image = new SKBitmap(
            new SKImageInfo(surface.Width, surface.Height, SKColorType.Bgra8888, SKAlphaType.Premul));

        var ground = new SKColor(0, 20, 12);

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
            using var file = File.Create(Path.Combine(folder, fileName + ".png"));
            data.SaveTo(file);
        }
        catch (IOException)
        {
        }
    }

    /// <summary>
    /// Draws one horizontal line with the given pattern and measures its longest unbroken mark.
    /// </summary>
    /// <param name="select">
    /// The escape sequence that selects the pattern - the manual's grave accent through d.
    /// </param>
    /// <returns>
    /// The longest run of lit pixels anywhere on the plane, which for a single horizontal line is
    /// the longest mark of its pattern.
    /// </returns>
    private static int LongestMarkOnOneLine(string select)
    {
        var emulator = new Tek4014Emulator();
        Feed(emulator, select);
        Feed(emulator, Gs);
        Feed(emulator, Point(60, 400));
        Feed(emulator, Point(960, 400));

        var compositor = emulator.Graphics!;
        compositor.Composite();
        var surface = compositor.Output;

        int best = 0;

        for (int y = 0; y < surface.Height; y++)
        {
            int run = 0;
            for (int x = 0; x < surface.Width; x++)
            {
                if (surface.GetPixel(x, y).A != 0)
                {
                    run++;
                    if (run > best)
                    {
                        best = run;
                    }
                }
                else
                {
                    run = 0;
                }
            }
        }

        return best;
    }

    /// <summary>
    /// M5.1a - a dotted line puts down less ink than a solid one of the same length.
    /// </summary>
    /// <remarks>
    /// The one thing about the patterns that can be asserted without an opinion. It does not say
    /// the dots are the right size; it says the pattern is being APPLIED, which is what silently
    /// stopped working when the pattern lived on the surface instead of on the draw call.
    /// </remarks>
    [AvaloniaFact]
    public void M5_1a_ADottedLineUsesLessInkThanASolidOne()
    {
        int solid = InkOfOneLine("\u001b`");
        int dotted = InkOfOneLine("\u001ba");

        Assert.True(solid > 0, "the solid line drew nothing");
        Assert.True(dotted < solid,
            $"the dotted line lit {dotted} pixels and the solid one {solid} - the pattern is not being applied");
    }

    /// <summary>
    /// Draws one horizontal line with the given pattern and counts the pixels it lit.
    /// </summary>
    /// <param name="select">
    /// The escape sequence that selects the pattern.
    /// </param>
    /// <returns>
    /// How many pixels of the graphics plane are not transparent.
    /// </returns>
    private static int InkOfOneLine(string select)
    {
        var emulator = new Tek4014Emulator();
        Feed(emulator, select);
        Feed(emulator, Gs);
        Feed(emulator, Point(60, 400));
        Feed(emulator, Point(960, 400));

        // Composite first: the planes are drawn on separately and the published output is what the
        // renderer would show, which is the thing worth counting.
        var compositor = emulator.Graphics!;
        compositor.Composite();

        var surface = compositor.Output;
        int lit = 0;

        for (int y = 0; y < surface.Height; y++)
        {
            for (int x = 0; x < surface.Width; x++)
            {
                if (surface.GetPixel(x, y).A != 0)
                {
                    lit++;
                }
            }
        }

        return lit;
    }

    /// <summary>
    /// M5.1b - the same sentence at all four aligned character sizes, on one sheet.
    /// </summary>
    /// <remarks>
    /// Each size gives a different grid - 35 lines of 74 up to 64 of 133 - so they cannot share one
    /// screen. Each is captured on its own and the four are stacked into a single sheet, which is
    /// the only form in which "each is smaller than the one above" can be seen at a glance.
    /// </remarks>
    [AvaloniaFact]
    public void M5_1b_TheFourCharacterSizesAreDrawnOnOneSheet()
    {
        // ESC 8, ESC 9, ESC :, ESC ; - the four aligned sizes the manual gives.
        string[] selects = { "\u001b8", "\u001b9", "\u001b:", "\u001b;" };
        var panels = new SKBitmap[selects.Length];

        try
        {
            for (int i = 0; i < selects.Length; i++)
            {
                var emulator = new Tek4014Emulator();
                Feed(emulator, selects[i]);
                Feed(emulator, "The quick brown fox jumps over the lazy dog 0123456789");

                using var shot = RenderedScreenshot.Capture(
                    emulator, "tek-character-size-" + (i + 1), saveZoom: 1);
                Assert.NotNull(shot.SavedPath);
                panels[i] = SKBitmap.Decode(File.ReadAllBytes(shot.SavedPath!));
            }

            // Only the top three rows of each panel carry the sentence; the rest is empty screen,
            // and four full screens stacked would be a sheet nobody scrolls to the bottom of.
            SaveStack(panels, 3, "tek-character-sizes");
        }
        finally
        {
            for (int i = 0; i < panels.Length; i++)
            {
                panels[i]?.Dispose();
            }
        }
    }

    /// <summary>
    /// M5.1c - two-column writing, drawn to be judged.
    /// </summary>
    /// <remarks>
    /// A storage tube cannot scroll. When the screen fills, a 4014 starts again at the TOP in a
    /// second column down the middle, overstriking whatever is there. This writes more numbered
    /// lines than the screen holds, so both columns are in use by the end.
    /// </remarks>
    [AvaloniaFact]
    public void M5_1c_TwoColumnWritingIsDrawnToBeJudged()
    {
        var emulator = new Tek4014Emulator();

        // Comfortably more than the 35 lines of the power-on size, so the margin has to switch.
        for (int i = 1; i <= 50; i++)
        {
            Feed(emulator, "line " + i.ToString("D2") + "\r\n");
        }

        using var shot = RenderedScreenshot.Capture(emulator, "tek-two-column-writing", saveZoom: 1);

        // What can be stated without looking: text reached BOTH halves of the screen. A terminal
        // that scrolled instead of switching margins would leave the right half blank.
        var buffer = emulator.GetBuffer();
        bool inkOnTheLeft = false;
        bool inkOnTheRight = false;
        int middle = buffer.Width / 2;

        for (int row = 0; row < buffer.Height; row++)
        {
            for (int col = 0; col < buffer.Width; col++)
            {
                if (buffer.GetCell(row, col).IsEmpty)
                {
                    continue;
                }

                if (col < middle)
                {
                    inkOnTheLeft = true;
                }
                else
                {
                    inkOnTheRight = true;
                }
            }
        }

        Assert.True(inkOnTheLeft, "nothing was written in the first column");
        Assert.True(inkOnTheRight,
            "nothing reached the second column - the terminal scrolled instead of switching margins");
    }

    /// <summary>
    /// Stacks panels into one sheet and writes it beside the other rendered artefacts.
    /// </summary>
    /// <param name="panels">
    /// The images, top to bottom.
    /// </param>
    /// <param name="rowsToKeep">
    /// How many text rows of each panel to keep, so a sheet of four full screens does not run to
    /// several thousand pixels. Pass 0 to keep all of it.
    /// </param>
    /// <param name="fileName">
    /// Name for the sheet, without an extension.
    /// </param>
    /// <remarks>
    /// <para><b>Every panel is drawn to the SAME width, and that is the whole point</b></para>
    /// A capture is taken at its own natural size - columns times cell width - so a 133-column
    /// screen comes out WIDER than a 74-column one with glyphs of exactly the same size. Stacked
    /// like that the sheet shows four identical-looking sentences and answers nothing.
    ///
    /// A real 4014's glass does not grow. Its width is fixed and more columns means smaller
    /// characters, so each panel is scaled to one common width here and the text shrinks down the
    /// sheet the way it does on the tube.
    ///
    /// A row is taken as one twentieth of the panel height, which is close enough for a sheet whose
    /// only job is to be looked at - the four sizes have four different row heights, and cropping
    /// each to its own exact row height would need the font metrics threaded in for no gain.
    /// </remarks>
    private static void SaveStack(SKBitmap[] panels, int rowsToKeep, string fileName)
    {
        int width = 0;

        for (int i = 0; i < panels.Length; i++)
        {
            if (panels[i].Width > width)
            {
                width = panels[i].Width;
            }
        }

        // Height of each panel once it has been scaled to the common width.
        var heights = new int[panels.Length];
        int height = 0;

        for (int i = 0; i < panels.Length; i++)
        {
            int keep = rowsToKeep <= 0
                ? panels[i].Height
                : Math.Min(panels[i].Height, Math.Max(24, panels[i].Height * rowsToKeep / 20));

            double scale = width / (double)panels[i].Width;
            heights[i] = (int)Math.Round(keep * scale);
            height += heights[i] + SeparatorHeight;
        }

        using var sheet = new SKBitmap(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul));
        using (var canvas = new SKCanvas(sheet))
        {
            canvas.Clear(new SKColor(24, 24, 24));

            int y = 0;
            for (int i = 0; i < panels.Length; i++)
            {
                int keep = rowsToKeep <= 0
                    ? panels[i].Height
                    : Math.Min(panels[i].Height, Math.Max(24, panels[i].Height * rowsToKeep / 20));

                canvas.DrawBitmap(panels[i],
                    new SKRect(0, 0, panels[i].Width, keep),
                    new SKRect(0, y, width, y + heights[i]));
                y += heights[i];

                using (var bar = new SKPaint())
                {
                    bar.Color = new SKColor(255, 128, 0);
                    bar.Style = SKPaintStyle.Fill;
                    canvas.DrawRect(new SKRect(0, y, width, y + SeparatorHeight), bar);
                }

                y += SeparatorHeight;
            }
        }

        var folder = RenderedScreenshot.ImagesFolder;
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, fileName + ".png");

        // A viewer may be holding the previous copy open; a manual artefact is not worth failing a
        // run over, the same rule the printed PDFs follow.
        try
        {
            using var data = sheet.Encode(SKEncodedImageFormat.Png, 100);
            using var file = File.Create(path);
            data.SaveTo(file);
        }
        catch (IOException)
        {
        }
    }
}
