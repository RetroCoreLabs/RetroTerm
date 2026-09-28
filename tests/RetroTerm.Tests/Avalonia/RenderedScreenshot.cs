using System;
using System.IO;
using Avalonia;
using Avalonia.Media.Imaging;
using RetroTerm.Core.Terminal.Emulators;
using RetroTerm.Desktop.Controls;
using RetroTerm.Desktop.Rendering;
using SkiaSharp;

namespace RetroTerm.Tests.Avalonia;

/// <summary>
/// Renders a terminal through the REAL production path and gives the test both a PNG to
/// look at and pixels to assert on.
///
/// WHY THIS EXISTS. The older screenshot tests re-implemented rendering with SkiaSharp
/// (their own colour maths, their own bold/reverse handling, their own glyph loop). A PNG
/// produced that way proves only that the test file works — if TerminalRenderer regressed,
/// the picture stayed perfect and the suite stayed green. That is the "fooling ourselves"
/// case this class removes.
///
/// Here the pixels come out of the real chain and nothing else:
///
///     TerminalCanvas.Render(DrawingContext)      (production control)
///       -> TerminalRenderer.Render(...)          (production renderer: palette,
///                                                 reverse, bold, dim, cursor, selection)
///         -> IFontRenderer.DrawCharacter(...)    (production BitmapFontRenderer /
///                                                 SystemFontRenderer, real glyph ROM)
///
/// The canvas is arranged at exactly its natural size, so the renderer's scale is 1.0 and
/// one terminal cell maps to a whole number of pixels. That is what makes per-cell pixel
/// assertions meaningful.
///
/// Requires the headless Skia platform with UseHeadlessDrawing = false — see
/// AvaloniaTestAppBuilder. Tests using this must be [AvaloniaFact]/[AvaloniaTheory] and in
/// the "Avalonia" collection.
/// </summary>
public sealed class RenderedScreenshot : IDisposable
{
    private readonly SKBitmap _bitmap;

    /// <summary>
    /// Width of one character cell in pixels (scale is always 1.0 here).
    /// </summary>
    public double CellWidth { get; }

    /// <summary>
    /// Height of one character cell in pixels.
    /// </summary>
    public double CellHeight { get; }

    /// <summary>
    /// Full rendered width in pixels.
    /// </summary>
    public int Width => _bitmap.Width;

    /// <summary>
    /// Full rendered height in pixels.
    /// </summary>
    public int Height => _bitmap.Height;

    /// <summary>
    /// Path of the PNG written for human inspection, or null if none was saved.
    /// </summary>
    public string? SavedPath { get; }

    private RenderedScreenshot(SKBitmap bitmap, double cellWidth, double cellHeight, string? savedPath)
    {
        _bitmap = bitmap;
        CellWidth = cellWidth;
        CellHeight = cellHeight;
        SavedPath = savedPath;
    }

    /// <summary>
    /// Renders <paramref name="emulator"/> through the production canvas/renderer and
    /// captures the result.
    /// </summary>
    /// <param name="emulator">
    /// The emulator whose buffer should be drawn.
    /// </param>
    /// <param name="saveAs">
    /// File name (no directory) to write a PNG to under the images folder, so a human can
    /// look at it. Pass null to skip saving.
    /// </param>
    /// <param name="saveZoom">
    /// Nearest-neighbour magnification applied to the SAVED png only. A terminal cell is
    /// about 8x16 pixels, so a 1:1 screenshot is too small to judge by eye; the saved copy
    /// is enlarged with hard pixel edges so glyph shapes stay honest. Assertions always run
    /// against the true-size image, never the magnified one.
    /// </param>
    /// <returns>
    /// A screenshot that can be queried pixel by pixel; dispose when done.
    /// </returns>
    /// <remarks>
    /// Always captures the live screen. Scrollback rendering is not exposed because the
    /// canvas only changes its scroll offset through real wheel input, and adding a
    /// test-only setter to a production control to work around that would be worse than
    /// leaving this gap documented.
    /// </remarks>
    /// <param name="configureRenderer">
    /// Optional hook run against the canvas's own TerminalRenderer after it is built and
    /// before anything is drawn. It exists for renderer state that is TIME-based and
    /// therefore cannot be set up through the emulator: the blink phases, which decide
    /// whether a blinking cell is currently showing its glyph.
    ///
    /// The renderer is reached by reflection, deliberately. This file already argues that
    /// adding a test-only setter to a production control is worse than a documented gap,
    /// and that applies just as much to exposing the renderer — so the reflection lives
    /// here, in the test helper, and TerminalCanvas stays unaware that tests exist.
    /// </param>
    /// <param name="sizeMultiplier">
    /// How many times its natural size to arrange the canvas at. 1 (the default) pins the scale to
    /// exactly 1.0, which is what per-cell glyph assertions need.
    ///
    /// Anything else makes the canvas compute a REAL magnification, the way it does in a window
    /// that is not exactly the terminal's natural size — which is almost always. That path had no
    /// test at all until the scale moved out of the canvas and into the renderer. Cell metrics are
    /// reported multiplied, so cell-based queries keep working.
    /// </param>
    /// <param name="configureCanvas">
    /// Optional hook run against the CANVAS, before it is arranged.
    ///
    /// Separate from <paramref name="configureRenderer"/> because the canvas overwrites parts of the
    /// renderer from its own fields on every frame. Smooth scrolling is the case that forced it:
    /// setting the renderer's pixel offset through <paramref name="configureRenderer"/> looked like
    /// it worked - the test passed - while the saved PNG showed an unshifted screen.
    /// </param>
    /// <param name="cellPixelWidth">
    /// Overrides the width of one character cell, in pixels. Zero, the default, keeps the font's own.
    /// </param>
    /// <param name="cellPixelHeight">
    /// Overrides the height of one character cell, in pixels. Zero, the default, keeps the font's own.
    ///
    /// Set BOTH to pin the natural size, rather than asking for a larger picture. Asking for one
    /// makes the canvas magnify with interpolation: measured 27 August 2026, arranging a 616 by 393
    /// canvas into 800 by 480 gave 1,940 distinct colours where the hardware capture has 21, and a
    /// sample on the cat's coat read 252,252,252 where the hardware has 0,0,0. A blurred edge is
    /// easy to mistake for a colour-register defect, and was. Setting the cell keeps the scale at
    /// 1.0, so nothing resamples.
    /// </param>
    public static RenderedScreenshot Capture(TerminalEmulatorBase emulator, string? saveAs = null, int saveZoom = 3,
        Action<TerminalRenderer>? configureRenderer = null, int sizeMultiplier = 1,
        Action<TerminalCanvas>? configureCanvas = null, double cellPixelWidth = 0, double cellPixelHeight = 0)
    {
        if (emulator == null) throw new ArgumentNullException(nameof(emulator));
        if (sizeMultiplier < 1) throw new ArgumentOutOfRangeException(nameof(sizeMultiplier));

        // Build the real control and hand it the real emulator.
        var canvas = new TerminalCanvas();
        canvas.SetEmulator(emulator);

        // CANVAS state, for the things the renderer cannot be told directly because the canvas
        // overwrites them every frame from its own fields. Smooth scrolling is the case that forced
        // this: setting TerminalRenderer.SmoothScrollPixelOffset through configureRenderer LOOKED
        // like it worked - the test passed - and the saved PNG showed an unshifted screen, because
        // TerminalCanvas.Render assigns that property from its own offset on the way past.
        //
        // A reminder that an assertion can only catch what it was told to expect. That one passed
        // for the wrong reason and only looking at the picture found it.
        configureCanvas?.Invoke(canvas);

        if (configureRenderer != null)
        {
            var field = typeof(TerminalCanvas).GetField("_renderer",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            if (field?.GetValue(canvas) is TerminalRenderer renderer)
            {
                configureRenderer(renderer);
            }
            else
            {
                throw new InvalidOperationException(
                    "TerminalCanvas._renderer was not found - RenderedScreenshot's reflection hook needs updating.");
            }
        }

        // Cell metrics come from the same font renderer the production renderer will use,
        // so the numbers below describe the picture we are about to take.
        var fontRenderer = emulator.CreateFontRenderer();
        double cellWidth = fontRenderer.GetCharWidth();
        double cellHeight = fontRenderer.GetCharHeight();

        // THE HARDWARE'S OWN CELL, WHEN THE PICTURE IS JUDGED AGAINST A PHOTOGRAPH.
        //
        // The size above is whatever the FONT happens to measure. A VT340 draws through
        // SystemFontRenderer with Consolas at 14 point, which measures 7.7 by 16.375, so 80 by 24
        // comes out 616 by 393 - a PC font at a PC size, chosen by nothing. Real VT340 hardware is
        // a 10 by 20 cell, which is 80 by 24 = 800 by 480, and that is ALSO exactly the size of the
        // Sixel plane and of every capture in hackerb9's corpus.
        //
        // WHY NOT JUST ASK FOR AN 800 BY 480 PICTURE. That was the first attempt and it was wrong.
        // TerminalCanvas.Render derives its scale from Bounds against the NATURAL size, so arranging
        // a 616 by 393 canvas into 800 by 480 magnifies it by 1.3 with interpolation. Measured on
        // 27 August 2026: the result had 1,940 distinct colours against the hardware capture's 21,
        // and a sample at the cat's coat read (252,252,252) where the hardware has (0,0,0) - a
        // blurred edge, which is easy to mistake for a colour-register defect and was.
        //
        // Setting the CELL instead makes the natural size 800 by 480, so the scale is 1.0 and
        // nothing resamples at all.
        if (cellPixelWidth > 0 && cellPixelHeight > 0)
        {
            cellWidth = cellPixelWidth;
            cellHeight = cellPixelHeight;

            // The renderer lays the grid out from its OWN copy of the cell size, taken from the font
            // renderer in its constructor, so telling this method alone would give a canvas of the
            // right size with the text still on the old grid. Both fields are readonly, which
            // reflection can still set on an instance - the same hook this class already uses to
            // reach TerminalCanvas._renderer.
            var rendererField = typeof(TerminalCanvas).GetField("_renderer",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            if (rendererField?.GetValue(canvas) is TerminalRenderer live)
            {
                SetPrivate(live, "_charWidth", cellWidth);
                SetPrivate(live, "_charHeight", cellHeight);
            }
        }

        cellWidth *= sizeMultiplier;
        cellHeight *= sizeMultiplier;

        var buffer = emulator.GetBuffer();
        int pixelWidth = (int)Math.Round(cellWidth * buffer.Width);
        int pixelHeight = (int)Math.Round(cellHeight * buffer.Height);
        // Arrange at exactly sizeMultiplier times the natural size. TerminalCanvas.Render derives
        // its scale from Bounds vs natural size, so a multiplier of 1 pins scale to 1.0 and offset
        // to 0 - without that the control letterboxes and every pixel coordinate below would be
        // off. A larger multiplier is an EXACT whole-number magnification, so cell coordinates
        // still land on cell boundaries and no letterboxing appears.
        var size = new Size(pixelWidth, pixelHeight);
        canvas.Measure(size);
        canvas.Arrange(new Rect(size));

        // RenderTargetBitmap.Render invokes the control's real Render(DrawingContext).
        using var target = new RenderTargetBitmap(new PixelSize(pixelWidth, pixelHeight), new Vector(96, 96));
        target.Render(canvas);

        // Round-trip through PNG: it is the same encoder used for the saved artifact, and it
        // gives straightforward pixel access via SkiaSharp for the assertions.
        // Take the bytes ONCE - SKBitmap.Decode closes the stream it reads from, so the
        // saved file and the decoded bitmap must both come from this array.
        byte[] pngBytes;
        using (var stream = new MemoryStream())
        {
            target.Save(stream);
            pngBytes = stream.ToArray();
        }

        var bitmap = SKBitmap.Decode(pngBytes);

        string? savedPath = null;
        if (!string.IsNullOrEmpty(saveAs))
        {
            savedPath = SaveToImagesFolder(bitmap, saveAs!, saveZoom);
        }

        return new RenderedScreenshot(bitmap, cellWidth, cellHeight, savedPath);
    }

    /// <summary>
    /// Sets one private field on an object, including a readonly one.
    /// </summary>
    /// <param name="target">
    /// The object to change.
    /// </param>
    /// <param name="fieldName">
    /// The field.
    /// </param>
    /// <param name="value">
    /// What to put in it.
    /// </param>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the field is not there any more, so a rename is a loud failure rather than a
    /// picture that quietly went back to the wrong size.
    /// </exception>
    private static void SetPrivate(object target, string fieldName, double value)
    {
        var field = target.GetType().GetField(fieldName,
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        if (field == null)
        {
            throw new InvalidOperationException(
                "TerminalRenderer." + fieldName + " was not found - RenderedScreenshot's cell-size hook "
                + "needs updating.");
        }

        field.SetValue(target, value);
    }

    /// <summary>
    /// Captures a whole headless WINDOW using Avalonia's own
    /// <c>HeadlessWindowExtensions.CaptureRenderedFrame</c>.
    ///
    /// Use this when the thing under test is the assembled UI - window, layout, the canvas
    /// inside its real container, letterboxing, anything drawn around it - or when the test
    /// drives real input through <c>KeyPressQwerty</c>/<c>MouseDown</c> and wants to see the
    /// result on screen. <see cref="Capture"/> stays the sharper tool for per-cell glyph
    /// checks because it pins the control to its natural size; a window capture is whatever
    /// size the window is, so cell coordinates only line up if the caller sized it to match.
    /// </summary>
    /// <param name="window">
    /// A window that has already been shown.
    /// </param>
    /// <param name="saveAs">
    /// File name for the PNG written to the images folder, or null to skip.
    /// </param>
    /// <param name="cellWidth">
    /// Cell width to report when the caller knows it; 0 when not applicable.
    /// </param>
    /// <param name="cellHeight">
    /// Cell height to report when the caller knows it; 0 when not applicable.
    /// </param>
    /// <param name="saveZoom">
    /// Magnification for the saved PNG only.
    /// </param>
    /// <returns>
    /// A screenshot of the window, or null when the platform produced no frame.
    /// </returns>
    public static RenderedScreenshot? CaptureWindow(
        global::Avalonia.Controls.Window window,
        string? saveAs = null,
        double cellWidth = 0,
        double cellHeight = 0,
        int saveZoom = 2)
    {
        if (window == null) throw new ArgumentNullException(nameof(window));

        var frame = global::Avalonia.Headless.HeadlessWindowExtensions.CaptureRenderedFrame(window);
        if (frame == null)
        {
            return null;
        }

        byte[] pngBytes;
        using (frame)
        using (var stream = new MemoryStream())
        {
            frame.Save(stream);
            pngBytes = stream.ToArray();
        }

        var bitmap = SKBitmap.Decode(pngBytes);

        string? savedPath = null;
        if (!string.IsNullOrEmpty(saveAs))
        {
            savedPath = SaveToImagesFolder(bitmap, saveAs!, saveZoom);
        }

        return new RenderedScreenshot(bitmap, cellWidth, cellHeight, savedPath);
    }

    /// <summary>
    /// Writes the screenshot into the test images folder, magnified for human inspection,
    /// and returns its full path.
    /// </summary>
    /// <param name="bitmap">
    /// The true-size rendered bitmap.
    /// </param>
    /// <param name="fileName">
    /// File name; ".png" is appended when missing.
    /// </param>
    /// <param name="zoom">
    /// Nearest-neighbour magnification; 1 or less saves at true size.
    /// </param>
    private static string SaveToImagesFolder(SKBitmap bitmap, string fileName, int zoom)
    {
        var folder = ImagesFolder;
        Directory.CreateDirectory(folder);

        if (!fileName.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
        {
            fileName += ".png";
        }

        var path = Path.Combine(folder, fileName);

        if (zoom <= 1)
        {
            using var data = bitmap.Encode(SKEncodedImageFormat.Png, 100);
            using var file = File.Create(path);
            data.SaveTo(file);
            return path;
        }

        // Nearest-neighbour ONLY. Any smoothing would invent colours that the renderer
        // never produced, which would defeat the point of looking at the picture.
        var scaledInfo = new SKImageInfo(bitmap.Width * zoom, bitmap.Height * zoom);
        using (var scaled = bitmap.Resize(scaledInfo, new SKSamplingOptions(SKFilterMode.Nearest, SKMipmapMode.None)))
        using (var data = scaled.Encode(SKEncodedImageFormat.Png, 100))
        using (var file = File.Create(path))
        {
            data.SaveTo(file);
        }

        return path;
    }

    /// <summary>
    /// Folder the PNGs land in: tests\RetroTerm.Tests\Avalonia\images\rendered.
    /// Resolved from the assembly location so it points at the source tree, not bin\.
    /// </summary>
    public static string ImagesFolder => Path.GetFullPath(Path.Combine(
        Path.GetDirectoryName(typeof(RenderedScreenshot).Assembly.Location) ?? "",
        "..", "..", "..", "Avalonia", "images", "rendered"));

    // ─────────────────────────────────────────────────────────────
    // Pixel queries - what the assertions are built from
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Colour of a single pixel.
    /// </summary>
    public SKColor PixelAt(int x, int y) => _bitmap.GetPixel(x, y);

    /// <summary>
    /// Counts pixels inside one character cell that differ noticeably from the cell's
    /// most common colour. In practice: how much ink the glyph put down.
    /// </summary>
    /// <param name="row">
    /// 0-based buffer row.
    /// </param>
    /// <param name="col">
    /// 0-based buffer column.
    /// </param>
    /// <returns>
    /// Number of pixels that are not the cell's dominant (background) colour.
    /// </returns>
    public int InkPixelsInCell(int row, int col)
    {
        var background = DominantColorInCell(row, col);
        int count = 0;

        int x0 = (int)Math.Round(col * CellWidth);
        int y0 = (int)Math.Round(row * CellHeight);
        int x1 = Math.Min((int)Math.Round((col + 1) * CellWidth), _bitmap.Width);
        int y1 = Math.Min((int)Math.Round((row + 1) * CellHeight), _bitmap.Height);

        for (int y = y0; y < y1; y++)
        {
            for (int x = x0; x < x1; x++)
            {
                if (!ApproximatelyEqual(_bitmap.GetPixel(x, y), background, 24))
                {
                    count++;
                }
            }
        }

        return count;
    }

    /// <summary>
    /// True when the cell contains visible ink (i.e. something was drawn).
    /// </summary>
    /// <param name="row">
    /// 0-based buffer row.
    /// </param>
    /// <param name="col">
    /// 0-based buffer column.
    /// </param>
    /// <param name="minimumPixels">
    /// How many differing pixels count as "drawn".
    /// </param>
    public bool CellHasInk(int row, int col, int minimumPixels = 3)
        => InkPixelsInCell(row, col) >= minimumPixels;

    /// <summary>
    /// The most frequent colour inside a cell. For ordinary text that is the background,
    /// because a glyph never covers most of its cell; for a reverse-video or cursor cell it
    /// is the inverted background, which is exactly what the reverse-video tests check.
    /// </summary>
    /// <param name="row">
    /// 0-based buffer row.
    /// </param>
    /// <param name="col">
    /// 0-based buffer column.
    /// </param>
    public SKColor DominantColorInCell(int row, int col)
    {
        var counts = new System.Collections.Generic.Dictionary<uint, int>();

        int x0 = (int)Math.Round(col * CellWidth);
        int y0 = (int)Math.Round(row * CellHeight);
        int x1 = Math.Min((int)Math.Round((col + 1) * CellWidth), _bitmap.Width);
        int y1 = Math.Min((int)Math.Round((row + 1) * CellHeight), _bitmap.Height);

        uint best = 0;
        int bestCount = -1;

        for (int y = y0; y < y1; y++)
        {
            for (int x = x0; x < x1; x++)
            {
                uint key = (uint)_bitmap.GetPixel(x, y);
                counts.TryGetValue(key, out int n);
                n++;
                counts[key] = n;
                if (n > bestCount)
                {
                    bestCount = n;
                    best = key;
                }
            }
        }

        return new SKColor(best);
    }

    /// <summary>
    /// The brightest colour found in a cell — usually the glyph/ink colour.
    /// </summary>
    /// <param name="row">
    /// 0-based buffer row.
    /// </param>
    /// <param name="col">
    /// 0-based buffer column.
    /// </param>
    public SKColor BrightestColorInCell(int row, int col)
    {
        int x0 = (int)Math.Round(col * CellWidth);
        int y0 = (int)Math.Round(row * CellHeight);
        int x1 = Math.Min((int)Math.Round((col + 1) * CellWidth), _bitmap.Width);
        int y1 = Math.Min((int)Math.Round((row + 1) * CellHeight), _bitmap.Height);

        SKColor brightest = new SKColor(0, 0, 0);
        int bestLuma = -1;

        for (int y = y0; y < y1; y++)
        {
            for (int x = x0; x < x1; x++)
            {
                var c = _bitmap.GetPixel(x, y);
                int luma = c.Red + c.Green + c.Blue;
                if (luma > bestLuma)
                {
                    bestLuma = luma;
                    brightest = c;
                }
            }
        }

        return brightest;
    }

    /// <summary>
    /// Compares two colours allowing a per-channel tolerance.
    /// </summary>
    public static bool ApproximatelyEqual(SKColor a, SKColor b, int tolerance)
    {
        return Math.Abs(a.Red - b.Red) <= tolerance
            && Math.Abs(a.Green - b.Green) <= tolerance
            && Math.Abs(a.Blue - b.Blue) <= tolerance;
    }

    /// <summary>
    /// Fraction of pixels that differ between two screenshots of the same size.
    /// Used to assert that a change actually changed the picture (or that it did not).
    /// </summary>
    /// <param name="other">
    /// The screenshot to compare against.
    /// </param>
    /// <param name="tolerance">
    /// Per-channel tolerance before a pixel counts as different.
    /// </param>
    /// <returns>
    /// 0.0 when identical, 1.0 when every pixel differs.
    /// </returns>
    public double FractionDifferentFrom(RenderedScreenshot other, int tolerance = 8)
    {
        if (other == null) throw new ArgumentNullException(nameof(other));
        if (other.Width != Width || other.Height != Height)
        {
            throw new ArgumentException(
                $"Screenshot sizes differ: {Width}x{Height} vs {other.Width}x{other.Height}");
        }

        long different = 0;
        long total = (long)Width * Height;

        for (int y = 0; y < Height; y++)
        {
            for (int x = 0; x < Width; x++)
            {
                if (!ApproximatelyEqual(_bitmap.GetPixel(x, y), other._bitmap.GetPixel(x, y), tolerance))
                {
                    different++;
                }
            }
        }

        return (double)different / total;
    }

    /// <summary>
    /// True when any pixel in the whole image is not the given colour.
    /// </summary>
    public bool HasAnyPixelDifferentFrom(SKColor color, int tolerance = 8)
    {
        for (int y = 0; y < Height; y++)
        {
            for (int x = 0; x < Width; x++)
            {
                if (!ApproximatelyEqual(_bitmap.GetPixel(x, y), color, tolerance))
                {
                    return true;
                }
            }
        }
        return false;
    }

    public void Dispose() => _bitmap.Dispose();
}
