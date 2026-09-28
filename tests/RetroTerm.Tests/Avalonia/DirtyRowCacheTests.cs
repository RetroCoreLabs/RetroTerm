using System;
using System.IO;
using System.Text;
using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using RetroTerm.Core.Terminal.Emulators;
using RetroTerm.Desktop.Rendering;
using SkiaSharp;
using Xunit;

namespace RetroTerm.Tests.Avalonia;

/// <summary>
/// The row cache: a screen redrawn one row at a time must be pixel for pixel what a screen redrawn
/// whole would have been.
///
/// WHY THIS FILE EXISTS AND THE OTHER 500 UI TESTS ARE NOT ENOUGH. Every screenshot test builds a
/// fresh canvas and a fresh renderer, so its cache is always cold and it always does a full
/// repaint. They prove the cache draws a first frame correctly and prove nothing whatsoever about
/// the incremental path — which is the entire risk. A stale row is not a crash and not a failed
/// assertion; it is a smudge on the screen that a human notices days later.
///
/// So every test here renders TWICE through ONE renderer, changing something between, and compares
/// the result against a SECOND renderer that saw the final state cold. Same picture or the cache
/// is wrong.
/// </summary>
[Collection("Avalonia")]
public class DirtyRowCacheTests
{
    private static void Feed(TerminalEmulatorBase emulator, string text)
        => emulator.ProcessData(Encoding.ASCII.GetBytes(text));

    /// <summary>
    /// Renders one frame through the given renderer and returns the pixels.
    /// </summary>
    private static SKBitmap Draw(TerminalRenderer renderer, TerminalEmulatorBase emulator, double scale)
    {
        var natural = renderer.CalculateSize(emulator.Width, emulator.Height);
        int pixelWidth = (int)Math.Ceiling(natural.Width * scale);
        int pixelHeight = (int)Math.Ceiling(natural.Height * scale);

        using var target = new RenderTargetBitmap(new PixelSize(pixelWidth, pixelHeight), new Vector(96, 96));
        using (var context = target.CreateDrawingContext())
        {
            renderer.Render(context, natural, 0, scale);
        }

        using var stream = new MemoryStream();
        target.Save(stream);
        return SKBitmap.Decode(stream.ToArray());
    }

    /// <summary>
    /// Draws the emulator's CURRENT state through a renderer that has never seen it before, so the
    /// whole screen is painted in one go. This is the reference every incremental result is held
    /// against.
    /// </summary>
    private static SKBitmap DrawCold(TerminalEmulatorBase emulator, double scale)
    {
        var renderer = new TerminalRenderer(emulator);
        try
        {
            return Draw(renderer, emulator, scale);
        }
        finally
        {
            renderer.Dispose();
        }
    }

    /// <summary>
    /// Number of pixels that differ, and where the first one is.
    /// </summary>
    private static (int Count, int X, int Y) Differences(SKBitmap a, SKBitmap b)
    {
        Assert.Equal(a.Width, b.Width);
        Assert.Equal(a.Height, b.Height);

        int count = 0, firstX = -1, firstY = -1;
        for (int y = 0; y < a.Height; y++)
        {
            for (int x = 0; x < a.Width; x++)
            {
                if (a.GetPixel(x, y) != b.GetPixel(x, y))
                {
                    if (count == 0) { firstX = x; firstY = y; }
                    count++;
                }
            }
        }
        return (count, firstX, firstY);
    }

    private static void AssertSamePicture(SKBitmap incremental, SKBitmap full, string what)
    {
        var (count, x, y) = Differences(incremental, full);
        Assert.True(count == 0,
            $"{what}: {count} pixels differ from a full repaint, first at ({x},{y}) - " +
            "the cache left something stale on screen");
    }

    private static void SetPrivate(TerminalRenderer renderer, string field, bool value)
    {
        var info = typeof(TerminalRenderer).GetField(field,
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(info);
        info!.SetValue(renderer, value);
    }

    // ─────────────────────────────────────────────────────────────
    // The cases
    // ─────────────────────────────────────────────────────────────

    [AvaloniaFact]
    public void TextChangingOnOneRow()
    {
        // The ordinary case: a line of output arrives and one row changes.
        var emulator = new VT100Emulator(20, 6);
        Feed(emulator, "first line\r\nsecond line\r\nthird line");

        var renderer = new TerminalRenderer(emulator);
        try
        {
            using (Draw(renderer, emulator, 1.0)) { }

            Feed(emulator, "\u001b[2;1HREPLACED");

            using var incremental = Draw(renderer, emulator, 1.0);
            using var full = DrawCold(emulator, 1.0);
            AssertSamePicture(incremental, full, "one row of text changed");
        }
        finally
        {
            renderer.Dispose();
        }
    }

    [AvaloniaFact]
    public void TheCursorMovingBetweenRows()
    {
        // THE classic stale-row bug. The cursor is a filled block drawn on top of a cell; move it
        // and TWO rows change - the one it left and the one it arrived at. A cache that only
        // repaints the row it arrived at leaves a second cursor burned into the old row forever.
        var emulator = new VT100Emulator(20, 6);
        Feed(emulator, "alpha\r\nbravo\r\ncharlie");

        var renderer = new TerminalRenderer(emulator);
        try
        {
            Feed(emulator, "\u001b[1;1H");
            using (Draw(renderer, emulator, 1.0)) { }

            Feed(emulator, "\u001b[4;10H");

            using var incremental = Draw(renderer, emulator, 1.0);
            using var full = DrawCold(emulator, 1.0);
            AssertSamePicture(incremental, full, "cursor moved to another row");
        }
        finally
        {
            renderer.Dispose();
        }
    }

    [AvaloniaFact]
    public void TheBlinkPhaseFlipping()
    {
        // Blinking text changes appearance with NO change to any cell. Diffing cells alone would
        // decide nothing was dirty and the text would stop blinking the moment a cache existed.
        var emulator = new VT100Emulator(20, 4);
        Feed(emulator, "\u001b[5mBLINKING\u001b[0m steady");
        Feed(emulator, "\u001b[4;20H");

        var renderer = new TerminalRenderer(emulator);
        try
        {
            SetPrivate(renderer, "_slowBlinkOn", true);
            SetPrivate(renderer, "_rapidBlinkOn", true);
            using (Draw(renderer, emulator, 1.0)) { }

            // The phase flips; the cells are untouched.
            SetPrivate(renderer, "_slowBlinkOn", false);
            using var incremental = Draw(renderer, emulator, 1.0);

            var reference = new TerminalRenderer(emulator);
            try
            {
                SetPrivate(reference, "_slowBlinkOn", false);
                SetPrivate(reference, "_rapidBlinkOn", true);
                using var full = Draw(reference, emulator, 1.0);
                AssertSamePicture(incremental, full, "blink phase flipped");
            }
            finally
            {
                reference.Dispose();
            }
        }
        finally
        {
            renderer.Dispose();
        }
    }

    [AvaloniaFact]
    public void AWholeScreenScrolling()
    {
        // Every row changes at once. Nothing incremental to gain here, but it must not go wrong:
        // this is the path where a partially-updated cache would show two screens at once.
        var emulator = new VT100Emulator(20, 5);
        Feed(emulator, "one\r\ntwo\r\nthree\r\nfour\r\nfive");

        var renderer = new TerminalRenderer(emulator);
        try
        {
            using (Draw(renderer, emulator, 1.0)) { }

            Feed(emulator, "\r\nsix\r\nseven");

            using var incremental = Draw(renderer, emulator, 1.0);
            using var full = DrawCold(emulator, 1.0);
            AssertSamePicture(incremental, full, "the screen scrolled");
        }
        finally
        {
            renderer.Dispose();
        }
    }

    [AvaloniaFact]
    public void ADoubleHeightPairChanging()
    {
        // Double-size lines are the case where one row's drawing reaches outside its own band, so
        // they are the sharpest test of repainting a row in isolation.
        var emulator = new VT100Emulator(20, 6);
        Feed(emulator, "\u001b#3TOP\r\n\u001b#4TOP\r\nplain");
        Feed(emulator, "\u001b[6;20H");

        var renderer = new TerminalRenderer(emulator);
        try
        {
            using (Draw(renderer, emulator, 1.0)) { }

            Feed(emulator, "\u001b[1;1H\u001b#3BIG\r\n\u001b#4BIG");
            Feed(emulator, "\u001b[6;20H");

            using var incremental = Draw(renderer, emulator, 1.0);
            using var full = DrawCold(emulator, 1.0);
            AssertSamePicture(incremental, full, "a double-height pair changed");
        }
        finally
        {
            renderer.Dispose();
        }
    }

    [AvaloniaFact]
    public void NothingChangingAtAll()
    {
        // Two identical frames must produce two identical pictures. If the second draw came out
        // different, the cache would be corrupting rows it was told not to touch.
        var emulator = new VT100Emulator(20, 4);
        Feed(emulator, "unchanged text");
        Feed(emulator, "\u001b[4;20H");

        var renderer = new TerminalRenderer(emulator);
        try
        {
            using var first = Draw(renderer, emulator, 1.0);
            using var second = Draw(renderer, emulator, 1.0);
            AssertSamePicture(second, first, "nothing changed between frames");
        }
        finally
        {
            renderer.Dispose();
        }
    }

    [AvaloniaFact]
    public void TheSameChangeAtAMagnification()
    {
        // The cache holds DEVICE pixels, so the whole thing has to work at a magnification too -
        // and that is the case a window almost always shows.
        var emulator = new VT100Emulator(20, 6);
        Feed(emulator, "first line\r\nsecond line");

        var renderer = new TerminalRenderer(emulator);
        try
        {
            using (Draw(renderer, emulator, 2.0)) { }

            Feed(emulator, "\u001b[2;1HREPLACED");

            using var incremental = Draw(renderer, emulator, 2.0);
            using var full = DrawCold(emulator, 2.0);
            AssertSamePicture(incremental, full, "one row changed at 2x magnification");
        }
        finally
        {
            renderer.Dispose();
        }
    }

    [AvaloniaFact]
    public void AChangeOfMagnificationRebuildsEverything()
    {
        // Cached pixels describe one magnification. Reusing them at another would stretch a stale
        // screen; the cache has to be thrown away and rebuilt.
        var emulator = new VT100Emulator(20, 4);
        Feed(emulator, "some text here");
        Feed(emulator, "\u001b[4;20H");

        var renderer = new TerminalRenderer(emulator);
        try
        {
            using (Draw(renderer, emulator, 1.0)) { }

            using var afterRescale = Draw(renderer, emulator, 2.0);
            using var full = DrawCold(emulator, 2.0);
            AssertSamePicture(afterRescale, full, "the magnification changed");
        }
        finally
        {
            renderer.Dispose();
        }
    }
}
