using System;
using System.Diagnostics;
using System.Text;
using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using RetroTerm.Core.Terminal.Emulators;
using RetroTerm.Core.Terminal.Emulators.TDV;
using RetroTerm.Desktop.Rendering;
using Xunit;
using Xunit.Abstractions;

namespace RetroTerm.Tests.Avalonia;

/// <summary>
/// What a frame actually COSTS, printed rather than asserted.
///
/// WHY THIS EXISTS. Two render optimisations landed justified by facts readable straight off the
/// code — an allocation per cell per frame, twenty-one published frames per chunk. The next one on
/// the list, dirty-row redraw, is not like that: it means keeping a cached bitmap and repainting
/// only changed rows into it, which buys nothing unless a full frame is actually expensive, and
/// brings a new class of bug (stale rows) that shows up as wrong pixels rather than red tests. So
/// the number comes first.
///
/// THESE TESTS DO NOT ASSERT A TIME. A wall-clock threshold on a developer machine is a flaky test
/// waiting to happen — this box runs Visual Studio, Unity and a live emulator alongside the suite.
/// They assert only that the work really happened, and PRINT the timings for a human to read:
///
///     dotnet test --filter "FullyQualifiedName~RenderCostBenchmarkTests" -l "console;verbosity=detailed"
///
/// The honesty guard matters as much as the numbers. A headless backend that quietly skipped
/// rasterising would report a wonderful figure for doing nothing, so every measurement first proves
/// through <see cref="RenderedScreenshot"/> that this emulator and renderer really put ink on real
/// pixels.
/// </summary>
/// <remarks>
/// <para><b>What it measured, 9 August 2026</b></para>
/// Fastest frame of 200, best of three runs, on a box that was also running Visual Studio, Unity
/// and a live emulator. Kept here because the numbers decided what got built next, and a later
/// reading of this harness should be compared against them:
///
///     VT100 80x24, every cell text             15.0 ms   (90% of a 60 Hz frame)
///     TDV2200 80x24, every cell text            9.2 ms   (55%)
///     VT100 80x24, every cell text + colour     8.0 ms   (48%)
///     VT100 80x24, ONE line of text             0.29 ms  (1.7%)
///     Blit a cached 80x24 screen bitmap         0.29 ms  (1.7%)
///     PublishFrame 80x24 (not a render)         0.039 ms (0.2%)
///
/// Read the minimum, not the mean; the method comment on MillisecondsPerFrame says why.
///
/// <para><b>What dirty-row painting changed</b></para>
/// Same harness, before and after TerminalRenderer started caching the screen bitmap and
/// repainting only the rows whose appearance changed:
///
///     Full screen, nothing changing              11.2 ms  to  0.28 ms
///     Full screen, one row changes (common case) 11.2 ms  to  0.91 ms
///     Full screen, whole screen scrolls (worst)  11.2 ms  to  6.0 ms
///
/// No case got worse, which was the thing to check.
///
/// <para><b>Three caveats that travel with the numbers</b></para>
/// First: the earlier frame-batching change was justified by PublishFrame copying 1,920 cells.
/// Measured, that copy is 0.039 ms, two tenths of one percent of a frame, and not on the UI thread
/// anyway. The real saving of batching was avoiding twenty-one REPAINT REQUESTS per chunk, each a
/// full render at 8 to 23 ms. Right change, wrong headline reason.
///
/// Second: the plain full-text screen came out consistently SLOWER than the coloured one (15.0
/// against 8.0 ms fastest) although the coloured screen draws the same 1,920 glyphs plus a
/// background rectangle behind each. Nobody has explained this. It does not change the
/// conclusion, but it means the absolute figures are an order of magnitude, not precision.
///
/// Third: this harness renders into a RenderTargetBitmap in one call, so it times recording and
/// rasterising TOGETHER on one thread. The running app splits them - Render records a display
/// list on the UI thread and the compositor rasterises on its own thread. So 11.2 ms is an upper
/// bound on the combined work, not a measurement of how long the UI thread stalls. The relative
/// figures (a full screen is about 40 times a blit) are unaffected; the headline is weaker.
///
/// The whole-screen-scroll case at 6.0 ms is also below the 11.2 ms an uncached full screen cost,
/// and that is unexplained too. Clipping each band may let Skia reject work cheaply, or the two
/// screens may not be quite comparable. Nothing above rests on it.
/// </remarks>
[Collection("Avalonia")]
public class RenderCostBenchmarkTests
{
    private readonly ITestOutputHelper _output;

    public RenderCostBenchmarkTests(ITestOutputHelper output) => _output = output;

    /// <summary>Frames per measurement. Enough to average out scheduler noise, few enough to stay
    /// well inside a normal test run.</summary>
    private const int MeasuredFrames = 200;

    /// <summary>
    /// Frames drawn and thrown away first, so JIT and Skia setup are not in the number.
    /// </summary>
    private const int WarmupFrames = 10;

    private static void Feed(TerminalEmulatorBase emulator, string text)
        => emulator.ProcessData(Encoding.ASCII.GetBytes(text));

    /// <summary>Fills the screen with dense, varied text — the expensive case, and a realistic one
    /// for a terminal showing a directory listing or a source file.</summary>
    private static void FillWithText(TerminalEmulatorBase emulator)
    {
        var line = new StringBuilder(emulator.Width);
        for (int row = 0; row < emulator.Height; row++)
        {
            line.Clear();
            for (int col = 0; col < emulator.Width; col++)
            {
                // Varied on purpose: a screen of one repeated letter would exercise one cached
                // glyph and flatter the renderer.
                line.Append((char)('!' + ((row * 7 + col * 3) % 90)));
            }
            Feed(emulator, line.ToString());
            if (row < emulator.Height - 1) Feed(emulator, "\r\n");
        }
    }

    /// <summary>
    /// Draws the emulator's screen <see cref="MeasuredFrames"/> times through the production
    /// renderer and returns the FASTEST and the MEDIAN frame, in milliseconds.
    ///
    /// The mean was tried first and thrown out. This machine runs Visual Studio, Unity and a live
    /// emulator beside the test run, and across four repeats of the same measurement the mean
    /// ranged from 24 ms to 56 ms for one identical screen — noise from other processes, not
    /// render cost. The FASTEST frame is the one least polluted by whatever else the CPU was
    /// doing, so it is the closest thing to the real cost; the median beside it shows how much
    /// interference there was. A gap between them is a warning that the box was busy, not a
    /// finding about the renderer.
    /// </summary>
    private static (double Fastest, double Median) MillisecondsPerFrame(TerminalEmulatorBase emulator)
    {
        var renderer = new TerminalRenderer(emulator);
        try
        {
            var size = renderer.CalculateSize(emulator.Width, emulator.Height);
            var pixelSize = new PixelSize((int)Math.Round(size.Width), (int)Math.Round(size.Height));
            using var target = new RenderTargetBitmap(pixelSize, new Vector(96, 96));

            for (int i = 0; i < WarmupFrames; i++)
            {
                DrawOnce(renderer, target, size);
            }

            var samples = new double[MeasuredFrames];
            var stopwatch = new Stopwatch();
            for (int i = 0; i < MeasuredFrames; i++)
            {
                stopwatch.Restart();
                DrawOnce(renderer, target, size);
                stopwatch.Stop();
                samples[i] = stopwatch.Elapsed.TotalMilliseconds;
            }

            Array.Sort(samples);
            return (samples[0], samples[samples.Length / 2]);
        }
        finally
        {
            // The renderer owns a 500 ms blink timer. Leaving it running would keep this renderer,
            // its emulator and its palette alive for the whole test run.
            renderer.Dispose();
        }
    }

    private static void DrawOnce(TerminalRenderer renderer, RenderTargetBitmap target, Size size)
    {
        using var context = target.CreateDrawingContext();
        renderer.Render(context, size, 0);
    }

    /// <summary>
    /// Proves this emulator really draws before its timing is believed. Without this, a headless
    /// backend that skipped rasterising would report a superb number for doing nothing at all.
    /// </summary>
    private static void AssertReallyDraws(TerminalEmulatorBase emulator, string name)
    {
        using var shot = RenderedScreenshot.Capture(emulator, null);
        Assert.True(shot.CellHasInk(0, 0),
            $"{name}: nothing was drawn, so any timing taken here would measure nothing");
    }

    private void Report(string name, (double Fastest, double Median) timing)
    {
        // 16.67 ms is one frame at 60 Hz. The share of that budget is the number the dirty-row
        // question actually turns on.
        double budgetShare = timing.Fastest / 16.667 * 100.0;
        _output.WriteLine(
            $"{name,-34} fastest {timing.Fastest,8:F3} ms  median {timing.Median,8:F3} ms   " +
            $"{budgetShare,6:F1}% of a 60 Hz frame");
    }

    // ─────────────────────────────────────────────────────────────
    // The measurements
    // ─────────────────────────────────────────────────────────────

    [AvaloniaFact]
    public void AFullScreenOfTextThroughTheSystemFont()
    {
        var emulator = new VT100Emulator(80, 24);
        FillWithText(emulator);
        AssertReallyDraws(emulator, "VT100 full screen");

        Report("VT100 80x24 full text", MillisecondsPerFrame(emulator));
    }

    [AvaloniaFact]
    public void AFullScreenOfTextThroughTheBitmapFont()
    {
        // The heavier path: TDV glyphs came out of a ROM bitmap and used to be drawn one
        // FillRectangle per lit pixel.
        var emulator = new TDV2200Emulator(80, 24);
        FillWithText(emulator);
        AssertReallyDraws(emulator, "TDV2200 full screen");

        Report("TDV2200 80x24 full text", MillisecondsPerFrame(emulator));
    }

    [AvaloniaFact]
    public void AMostlyEmptyScreen()
    {
        // The floor. Whatever a frame costs with almost nothing on it is what dirty-row redraw
        // could never get below, because the background fill and the frame read still happen.
        var emulator = new VT100Emulator(80, 24);
        Feed(emulator, "a single line of text");
        AssertReallyDraws(emulator, "VT100 sparse screen");

        Report("VT100 80x24 one line", MillisecondsPerFrame(emulator));
    }

    [AvaloniaFact]
    public void AScreenWithColourOnEveryCell()
    {
        // Colour means a background fill per cell on top of every glyph, which is the case a
        // cached-bitmap approach would help most.
        var emulator = new VT100Emulator(80, 24);
        for (int row = 0; row < 24; row++)
        {
            Feed(emulator, "\u001b[4" + (row % 8) + "m\u001b[3" + ((row + 3) % 8) + "m");
            var line = new StringBuilder(80);
            for (int col = 0; col < 80; col++)
            {
                line.Append((char)('!' + ((row * 5 + col) % 90)));
            }
            Feed(emulator, line.ToString());
            if (row < 23) Feed(emulator, "\r\n");
        }
        AssertReallyDraws(emulator, "VT100 coloured screen");

        Report("VT100 80x24 every cell coloured", MillisecondsPerFrame(emulator));
    }

    // ─────────────────────────────────────────────────────────────
    // Screens that CHANGE — the honest cases now that a row cache exists
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Renders repeatedly, mutating the emulator between frames, and returns the fastest and
    /// median frame. The mutation happens OUTSIDE the timed region: this measures drawing, not
    /// parsing.
    /// </summary>
    private static (double Fastest, double Median) MillisecondsPerChangingFrame(
        TerminalEmulatorBase emulator, Action<int> mutate)
    {
        var renderer = new TerminalRenderer(emulator);
        try
        {
            var size = renderer.CalculateSize(emulator.Width, emulator.Height);
            var pixelSize = new PixelSize((int)Math.Round(size.Width), (int)Math.Round(size.Height));
            using var target = new RenderTargetBitmap(pixelSize, new Vector(96, 96));

            for (int i = 0; i < WarmupFrames; i++)
            {
                mutate(i);
                DrawOnce(renderer, target, size);
            }

            var samples = new double[MeasuredFrames];
            var stopwatch = new Stopwatch();
            for (int i = 0; i < MeasuredFrames; i++)
            {
                mutate(i);
                stopwatch.Restart();
                DrawOnce(renderer, target, size);
                stopwatch.Stop();
                samples[i] = stopwatch.Elapsed.TotalMilliseconds;
            }

            Array.Sort(samples);
            return (samples[0], samples[samples.Length / 2]);
        }
        finally
        {
            renderer.Dispose();
        }
    }

    [AvaloniaFact]
    public void OneRowChangingEveryFrame()
    {
        // The case a terminal actually spends its life in: a line of output arrives and one row
        // changes. This is what the row cache was built for, and it is the number to quote.
        var emulator = new VT100Emulator(80, 24);
        FillWithText(emulator);
        AssertReallyDraws(emulator, "VT100 one row changing");

        var line = new StringBuilder(80);
        Report("VT100 80x24 one row changes", MillisecondsPerChangingFrame(emulator, i =>
        {
            line.Clear();
            for (int col = 0; col < 79; col++)
            {
                line.Append((char)('!' + ((i * 11 + col) % 90)));
            }
            Feed(emulator, "\u001b[12;1H" + line);
        }));
    }

    [AvaloniaFact]
    public void EveryRowChangingEveryFrame()
    {
        // The worst case, and the one that keeps the cache honest: when nothing can be reused the
        // cache is pure overhead - a full repaint PLUS the diff PLUS the blit. If this were far
        // above what an uncached full screen cost, the cache would be a bad trade for anyone
        // watching fast output.
        var emulator = new VT100Emulator(80, 24);
        FillWithText(emulator);
        AssertReallyDraws(emulator, "VT100 every row changing");

        var scrolled = new StringBuilder(80);
        Report("VT100 80x24 whole screen changes", MillisecondsPerChangingFrame(emulator, i =>
        {
            // Scrolling by one line moves every row, so nothing can be reused.
            //
            // The line has to be FULL WIDTH. A first attempt scrolled in "line 1", "line 2" and so
            // on, and after a few hundred frames the screen was almost empty - so it measured a
            // blank screen and reported the worst case as CHEAPER than changing one row of a full
            // one. A benchmark that quietly empties its own subject is worse than no benchmark.
            scrolled.Clear();
            for (int col = 0; col < 79; col++)
            {
                scrolled.Append((char)('!' + ((i * 13 + col * 3) % 90)));
            }
            Feed(emulator, "\r\n" + scrolled);
        }));
    }

    // ─────────────────────────────────────────────────────────────
    // The number dirty-row redraw lives or dies on
    // ─────────────────────────────────────────────────────────────

    [AvaloniaFact]
    public void BlittingACachedScreenBitmap()
    {
        // Dirty-row redraw means keeping the screen in a bitmap, repainting only the rows that
        // changed into it, and drawing that bitmap each frame. So EVERY frame pays for the blit,
        // whatever else it saves. If the blit costs what a full render costs, the design is worth
        // nothing and should not be built - which is exactly the kind of thing worth finding out
        // in one measurement rather than after a day of work.
        var emulator = new VT100Emulator(80, 24);
        FillWithText(emulator);
        AssertReallyDraws(emulator, "VT100 for blit test");

        var renderer = new TerminalRenderer(emulator);
        try
        {
            var size = renderer.CalculateSize(emulator.Width, emulator.Height);
            var pixelSize = new PixelSize((int)Math.Round(size.Width), (int)Math.Round(size.Height));

            // The cache: the screen, drawn once.
            using var cache = new RenderTargetBitmap(pixelSize, new Vector(96, 96));
            DrawOnce(renderer, cache, size);

            // The frame buffer the cache would be blitted into.
            using var target = new RenderTargetBitmap(pixelSize, new Vector(96, 96));
            var full = new Rect(0, 0, size.Width, size.Height);

            for (int i = 0; i < WarmupFrames; i++)
            {
                using var warm = target.CreateDrawingContext();
                warm.DrawImage(cache, full, full);
            }

            var samples = new double[MeasuredFrames];
            var stopwatch = new Stopwatch();
            for (int i = 0; i < MeasuredFrames; i++)
            {
                stopwatch.Restart();
                using (var context = target.CreateDrawingContext())
                {
                    context.DrawImage(cache, full, full);
                }
                stopwatch.Stop();
                samples[i] = stopwatch.Elapsed.TotalMilliseconds;
            }

            Array.Sort(samples);
            Report("Blit cached 80x24 screen", (samples[0], samples[samples.Length / 2]));
        }
        finally
        {
            renderer.Dispose();
        }
    }

    // ─────────────────────────────────────────────────────────────
    // The other half of a frame's cost: getting the data there
    // ─────────────────────────────────────────────────────────────

    [AvaloniaFact]
    public void PublishingAFrameFromTheBuffer()
    {
        // PublishFrame copies every cell on screen. It runs on the session pump, not the UI
        // thread, so it does not eat the frame budget — but it is what the batching change was
        // about, and knowing its size puts that change in proportion.
        var emulator = new VT100Emulator(80, 24);
        FillWithText(emulator);

        for (int i = 0; i < WarmupFrames; i++) emulator.PublishFrame();

        var samples = new double[MeasuredFrames];
        var stopwatch = new Stopwatch();
        for (int i = 0; i < MeasuredFrames; i++)
        {
            stopwatch.Restart();
            emulator.PublishFrame();
            stopwatch.Stop();
            samples[i] = stopwatch.Elapsed.TotalMilliseconds;
        }

        Array.Sort(samples);
        Assert.NotNull(emulator.LatestFrame);
        Report("PublishFrame 80x24", (samples[0], samples[samples.Length / 2]));
    }
}
