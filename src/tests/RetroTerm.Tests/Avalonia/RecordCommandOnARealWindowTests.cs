//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs/RetroTerm
// RetroTerm — Connecting today's machines to yesterday's systems
//

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using RetroTerm.Core.Commands;
using RetroTerm.Core.Commands.Builtin;
using RetroTerm.Core.Recording;
using RetroTerm.Desktop;
using RetroTerm.Desktop.Models;
using RetroTerm.Desktop.Themes;
using RetroTerm.Tests.Recording;
using RetroTerm.Tests.TDV;
using SkiaSharp;
using Xunit;

namespace RetroTerm.Tests.Avalonia;

/// <summary>
/// RECORDSTART and RECORDSTOP run through the desktop's own command registry on a real, shown
/// main window with a VT100 tab.
/// </summary>
/// <remarks>
/// <para><b>How the frames are driven</b></para>
/// The recording timer is switched off and the clock is set by hand; each test calls the tick
/// itself, as the timer would. So the film is the same on every machine, however slow.
/// <para><b>What proves the frames are right</b></para>
/// After every look, a SCREENSHOT of the same area is taken through the same registry, and the
/// film's frame must equal it pixel for pixel. The two share the drawing route, so this checks
/// the film carries what that route drew, with the colours in the right order - not a second
/// opinion on how a terminal looks. The frames are also saved to images\rendered to be looked at.
/// </remarks>
[Collection("Avalonia")]
public class RecordCommandOnARealWindowTests : IDisposable
{
    private TimeSpan _now;

    /// <summary>
    /// The ffmpeg path Preferences held before the test, put back by <see cref="Dispose"/>.
    /// </summary>
    private readonly string _ffmpegBefore;

    /// <summary>
    /// A path in Preferences that names no file, so RECORDSTOP makes no MP4 unless a test sets
    /// another: the tests about the film itself then never start ffmpeg, whatever the machine has.
    /// </summary>
    private static readonly string MissingFfmpeg =
        Path.Combine(Path.GetTempPath(), "retroterm-no-ffmpeg-" + Guid.NewGuid().ToString("N"), "ffmpeg.exe");

    public RecordCommandOnARealWindowTests()
    {
        _ffmpegBefore = ThemeManager.Instance.FfmpegPath;
        ThemeManager.Instance.FfmpegPath = MissingFfmpeg;
    }

    public void Dispose()
    {
        ThemeManager.Instance.FfmpegPath = _ffmpegBefore;
    }

    private static string TempPng(string name)
        => Path.Combine(Path.GetTempPath(), "retroterm-" + name + "-" + Guid.NewGuid().ToString("N") + ".png");

    /// <summary>
    /// A shown window with one connected VT100 tab, the recording timer off and a hand clock.
    /// </summary>
    private async Task<(MainWindow Main, TabSession Tab, InMemoryConnection Line)> Window()
    {
        var main = new MainWindow();
        main.RecordingTimersOffForTesting = true;
        main.RecordingClock = () => _now;
        main.Show();

        var tab = main.AddTabForTesting("film-host", "VT100");
        var line = new InMemoryConnection();
        await tab.Session.ConnectAsync(line, TestContext.Current.CancellationToken);
        main.SelectTabForTesting(tab);
        Dispatcher.UIThread.RunJobs();
        return (main, tab, line);
    }

    /// <summary>
    /// Clears the screen and writes one line, then lets the window catch up.
    /// </summary>
    private static async Task Show(TabSession tab, InMemoryConnection line, string text)
    {
        line.SimulateReceive(Encoding.ASCII.GetBytes("\u001b[2J\u001b[H" + text));
        await tab.Session.FlushAsync();
        Dispatcher.UIThread.RunJobs();
    }

    private static Task<CommandResult> Run(MainWindow main, TabSession tab, string command, Dictionary<string, string> values)
        => main.GetCommandRegistryForTesting().ExecuteAsync(command, tab.Session, new CommandArgs(values), CancellationToken.None);

    private static Task<CommandResult> Start(MainWindow main, TabSession tab, string path, string area = "terminal")
        => Run(main, tab, "RECORDSTART", new Dictionary<string, string> { ["file"] = path, ["area"] = area });

    private static Task<CommandResult> Stop(MainWindow main, TabSession tab)
        => Run(main, tab, "RECORDSTOP", new Dictionary<string, string>());

    /// <summary>
    /// A SCREENSHOT of the same area, decoded, to compare a frame against.
    /// </summary>
    private static async Task<SKBitmap> Screenshot(MainWindow main, TabSession tab, string area, List<string> cleanup)
    {
        var path = TempPng("film-reference");
        cleanup.Add(path);
        var result = await Run(main, tab, "SCREENSHOT",
            new Dictionary<string, string> { ["file"] = path, ["settle"] = "0", ["area"] = area });
        Assert.True(result.Success, result.Error);
        return SKBitmap.Decode(path);
    }

    /// <summary>
    /// How many pixels two pictures of the same size differ in.
    /// </summary>
    private static int Differences(SKBitmap a, SKBitmap b)
    {
        Assert.Equal(a.Width, b.Width);
        Assert.Equal(a.Height, b.Height);
        int differ = 0;
        for (int y = 0; y < a.Height; y++)
        {
            for (int x = 0; x < a.Width; x++)
            {
                if (a.GetPixel(x, y) != b.GetPixel(x, y)) differ++;
            }
        }
        return differ;
    }

    [AvaloniaFact]
    public async Task AFilmOfTheTerminalHoldsOneFramePerChangeWithItsRealDuration()
    {
        var (main, tab, line) = await Window();
        var film = TempPng("film-terminal");
        var cleanup = new List<string> { film };

        try
        {
            await Show(tab, line, "FIRST PICTURE");
            _now = TimeSpan.FromMilliseconds(1000);
            var started = await Start(main, tab, film);
            Assert.True(started.Success, started.Error);
            var expected0 = await Screenshot(main, tab, "terminal", cleanup);

            // Nothing changed: a look adds nothing.
            _now = TimeSpan.FromMilliseconds(1100);
            main.TickRecordingForTesting(tab.Session);

            await Show(tab, line, "SECOND PICTURE, LONGER LINE");
            _now = TimeSpan.FromMilliseconds(1500);
            main.TickRecordingForTesting(tab.Session);
            var expected1 = await Screenshot(main, tab, "terminal", cleanup);

            await Show(tab, line, "THIRD");
            _now = TimeSpan.FromMilliseconds(2200);
            main.TickRecordingForTesting(tab.Session);
            var expected2 = await Screenshot(main, tab, "terminal", cleanup);

            _now = TimeSpan.FromMilliseconds(3000);
            var stopped = await Stop(main, tab);
            Assert.True(stopped.Success, stopped.Error);
            Assert.StartsWith("film saved to " + film, stopped.Output!);
            Assert.Contains("3 picture(s) in 3 frame(s), 2.0 s", stopped.Output!);
            Assert.False(main.IsRecordingForTesting(tab.Session));

            var read = ApngTestReader.Read(film);
            read.SaveFramesForLooking(RenderedScreenshot.ImagesFolder, "record-command-terminal");

            Assert.Equal((int)Math.Round(tab.Control.Bounds.Width), read.Width);
            Assert.Equal((int)Math.Round(tab.Control.Bounds.Height), read.Height);
            Assert.Equal(3, read.DeclaredFrames);
            Assert.Equal(3, read.Frames.Count);

            Assert.Equal(500, read.Frames[0].DurationMs);
            Assert.Equal(700, read.Frames[1].DurationMs);
            Assert.Equal(800, read.Frames[2].DurationMs);

            // Each frame is what SCREENSHOT drew at that moment, exactly.
            Assert.Equal(0, Differences(expected0, read.Frames[0].Bitmap));
            Assert.Equal(0, Differences(expected1, read.Frames[1].Bitmap));
            Assert.Equal(0, Differences(expected2, read.Frames[2].Bitmap));

            // And the frames really are different pictures - ink where the text changed.
            Assert.True(Differences(read.Frames[0].Bitmap, read.Frames[1].Bitmap) > 50);
            Assert.True(Differences(read.Frames[1].Bitmap, read.Frames[2].Bitmap) > 50);
        }
        finally
        {
            main.Close();
            for (int i = 0; i < cleanup.Count; i++)
            {
                if (File.Exists(cleanup[i])) File.Delete(cleanup[i]);
            }
        }
    }

    [AvaloniaFact]
    public async Task AFilmOfTheWindowIsTheWholeWindow()
    {
        var (main, tab, line) = await Window();
        var film = TempPng("film-window");
        var cleanup = new List<string> { film };

        try
        {
            await Show(tab, line, "WINDOW FILM");
            var started = await Start(main, tab, film, "window");
            Assert.True(started.Success, started.Error);
            Assert.Contains("recording the window", started.Output!);
            var expected0 = await Screenshot(main, tab, "window", cleanup);

            await Show(tab, line, "WINDOW FILM, SECOND");
            _now = TimeSpan.FromMilliseconds(400);
            main.TickRecordingForTesting(tab.Session);
            var expected1 = await Screenshot(main, tab, "window", cleanup);

            _now = TimeSpan.FromMilliseconds(600);
            Assert.True((await Stop(main, tab)).Success);

            var read = ApngTestReader.Read(film);
            read.SaveFramesForLooking(RenderedScreenshot.ImagesFolder, "record-command-window");

            Assert.Equal((int)Math.Round(main.Bounds.Width), read.Width);
            Assert.Equal((int)Math.Round(main.Bounds.Height), read.Height);
            Assert.Equal(2, read.Frames.Count);
            Assert.Equal(0, Differences(expected0, read.Frames[0].Bitmap));
            Assert.Equal(0, Differences(expected1, read.Frames[1].Bitmap));
        }
        finally
        {
            main.Close();
            for (int i = 0; i < cleanup.Count; i++)
            {
                if (File.Exists(cleanup[i])) File.Delete(cleanup[i]);
            }
        }
    }

    [AvaloniaFact]
    public async Task StopWithoutStartAndASecondStartAreRefusedByName()
    {
        var (main, tab, line) = await Window();
        var film = TempPng("film-twice");
        var other = TempPng("film-second");

        try
        {
            var stop = await Stop(main, tab);
            Assert.False(stop.Success);
            Assert.Equal("no recording is running for this session - RECORDSTART first", stop.Error);

            Assert.True((await Start(main, tab, film)).Success);

            var again = await Start(main, tab, other);
            Assert.False(again.Success);
            Assert.Contains("a recording is already running for this session (to " + film + ") - RECORDSTOP first", again.Error!);
            Assert.False(File.Exists(other), "a second film was created");

            Assert.True((await Stop(main, tab)).Success);
            Assert.False((await Stop(main, tab)).Success);
        }
        finally
        {
            main.Close();
            if (File.Exists(film)) File.Delete(film);
            if (File.Exists(other)) File.Delete(other);
        }
    }

    [AvaloniaFact]
    public async Task ClosingTheTabFinishesTheFilm()
    {
        var (main, tab, line) = await Window();
        var film = TempPng("film-closed");

        try
        {
            await Show(tab, line, "BEFORE CLOSE");
            Assert.True((await Start(main, tab, film)).Success);

            await Show(tab, line, "STILL BEFORE CLOSE");
            _now = TimeSpan.FromMilliseconds(250);
            main.TickRecordingForTesting(tab.Session);

            _now = TimeSpan.FromMilliseconds(900);
            main.CloseTabForTesting(tab);
            Dispatcher.UIThread.RunJobs();

            Assert.False(main.IsRecordingForTesting(tab.Session));

            // A finished file: acTL holds the real count and IEND is there.
            var read = ApngTestReader.Read(film);
            Assert.Equal(2, read.DeclaredFrames);
            Assert.Equal(2, read.Frames.Count);
            Assert.Equal(250, read.Frames[0].DurationMs);
            Assert.Equal(650, read.Frames[1].DurationMs);
        }
        finally
        {
            main.Close();
            if (File.Exists(film)) File.Delete(film);
        }
    }

    [AvaloniaFact]
    public async Task AWindowFilmEndsByItselfWhenAnotherTabIsSelectedAndStopSaysWhy()
    {
        // A window picture with another tab selected would show the other tab's screen under
        // this session's name - the rule SCREENSHOT refuses by. The film stops there instead.
        var (main, tab, line) = await Window();
        var film = TempPng("film-tab-switch");

        try
        {
            await Show(tab, line, "MINE");
            Assert.True((await Start(main, tab, film, "window")).Success);

            var other = main.AddTabForTesting("other-host", "VT100");
            main.SelectTabForTesting(other);
            Dispatcher.UIThread.RunJobs();
            _now = TimeSpan.FromMilliseconds(300);
            main.TickRecordingForTesting(tab.Session);

            var stop = await Stop(main, tab);
            Assert.True(stop.Success, stop.Error);
            Assert.Contains("the recording had already ended by itself: that session's tab is not the selected tab", stop.Output!);

            var read = ApngTestReader.Read(film);
            Assert.Single(read.Frames);
            Assert.Equal(300, read.Frames[0].DurationMs);
        }
        finally
        {
            main.Close();
            if (File.Exists(film)) File.Delete(film);
        }
    }

    /// <summary>
    /// A file that stands for ffmpeg in Preferences, so the locator finds "an ffmpeg" while the
    /// test's own maker does the running. Deleted by the caller.
    /// </summary>
    private static string FakeFfmpeg()
    {
        var folder = Path.Combine(Path.GetTempPath(), "retroterm-fake-ffmpeg-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, FfmpegLocator.ProgramName);
        File.WriteAllBytes(path, Array.Empty<byte>());
        return path;
    }

    /// <summary>
    /// Closing a tab while it records runs the same finish RECORDSTOP runs: the MP4 maker is
    /// handed the finished PNG, and the report - with no command waiting for it - reaches the log
    /// and the status bar. Until 24 November 2026 a closed tab got its PNG and never an MP4.
    /// </summary>
    [AvaloniaFact]
    public async Task ClosingTheTabMakesTheMp4TheWayRecordStopDoes()
    {
        var fake = FakeFfmpeg();
        ThemeManager.Instance.FfmpegPath = fake;
        var (main, tab, line) = await Window();
        var film = TempPng("film-closed-mp4");
        var mp4 = Mp4FromAnimatedPng.Mp4PathFor(film);

        string? handedFfmpeg = null;
        string? handedFilm = null;
        bool pngFinishedWhenHanded = false;
        main.RecordingMp4Maker = (ffmpeg, filmPath, width, height, token) =>
        {
            handedFfmpeg = ffmpeg;
            handedFilm = filmPath;
            pngFinishedWhenHanded = ApngTestReader.Read(filmPath).Frames.Count == 1;
            return Task.FromResult(new Mp4Outcome { Path = mp4, Width = (width + 1) & ~1, Height = (height + 1) & ~1 });
        };

        try
        {
            await Show(tab, line, "CLOSE ME");
            Assert.True((await Start(main, tab, film)).Success);
            _now = TimeSpan.FromMilliseconds(500);

            main.CloseTabForTesting(tab);
            await main.WaitForClosedFilmsForTesting();
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(fake, handedFfmpeg);
            Assert.Equal(film, handedFilm);
            Assert.True(pngFinishedWhenHanded, "the PNG must be finished before ffmpeg is given it");

            var report = main.LastClosedFilmReport;
            Assert.NotNull(report);
            Assert.StartsWith("recording of a closed tab finished: film saved to " + film, report, StringComparison.Ordinal);
            Assert.Contains("  " + film + " - animated PNG\n", report, StringComparison.Ordinal);
            Assert.Contains("  " + mp4 + " - MP4 (H.264", report, StringComparison.Ordinal);
            Assert.EndsWith("ffmpeg used: " + fake + " (set in Preferences)", report, StringComparison.Ordinal);

            // The log holds it, and the status bar shows it on one line.
            var entries = global::RetroTerm.Core.Logging.ApplicationLogger.GetLogEntries();
            bool logged = false;
            for (int i = 0; i < entries.Length; i++)
            {
                if (entries[i].Message == report) logged = true;
            }
            Assert.True(logged, "the report is not in the application log");
            Assert.Equal(report.Replace('\n', ' '), main.StatusTextForTesting);
        }
        finally
        {
            main.Close();
            if (File.Exists(film)) File.Delete(film);
            Directory.Delete(Path.GetDirectoryName(fake)!, true);
        }
    }

    /// <summary>
    /// Closing the main window waits for an MP4 still being made, but not for ever: past the limit
    /// ffmpeg is stopped, the report says so, and the PNG stays.
    /// </summary>
    [AvaloniaFact]
    public async Task ClosingTheMainWindowStopsAnMp4ThatTakesTooLongAndSaysSo()
    {
        var fake = FakeFfmpeg();
        ThemeManager.Instance.FfmpegPath = fake;
        var (main, tab, line) = await Window();
        var film = TempPng("film-exit-limit");

        bool stopped = false;
        main.FilmFinishLimitOnExit = TimeSpan.FromMilliseconds(50);
        main.RecordingMp4Maker = async (ffmpeg, filmPath, width, height, token) =>
        {
            // An ffmpeg that never finishes by itself: only the cancellation ends it.
            try
            {
                await Task.Delay(Timeout.Infinite, token);
            }
            catch (OperationCanceledException)
            {
                stopped = true;
                throw;
            }
            return new Mp4Outcome { Problem = "not reached" };
        };

        try
        {
            await Show(tab, line, "EXIT");
            Assert.True((await Start(main, tab, film)).Success);
            _now = TimeSpan.FromMilliseconds(300);

            await main.CleanUpForExitForTesting();

            Assert.True(stopped, "the running MP4 was not stopped");
            Assert.Equal("recording of a closed tab finished: film saved to " + film + "\n"
                + "no MP4: RetroTerm was closing and ffmpeg was still making it after 0.05 s, so ffmpeg was stopped; "
                + "the animated PNG is kept", main.LastClosedFilmReport);
            Assert.Single(ApngTestReader.Read(film).Frames);
        }
        finally
        {
            main.Close();
            if (File.Exists(film)) File.Delete(film);
            Directory.Delete(Path.GetDirectoryName(fake)!, true);
        }
    }

    /// <summary>
    /// A tab closed while recording, with the real ffmpeg: the MP4 is there afterwards. Needs
    /// ffmpeg; fails naming it.
    /// </summary>
    [AvaloniaFact]
    [Trait("Category", "Ffmpeg")]
    public async Task ClosingTheTabWithFfmpegLeavesAnMp4()
    {
        var ffmpeg = FfmpegLocator.FindOnThisMachine(null);
        if (ffmpeg.Path == null)
        {
            Assert.Fail("this test needs ffmpeg and none was found the way RetroTerm looks for it: " + ffmpeg.Problem);
        }
        ThemeManager.Instance.FfmpegPath = ffmpeg.Path;

        var (main, tab, line) = await Window();
        var film = TempPng("film-closed-real-mp4");
        var mp4 = Mp4FromAnimatedPng.Mp4PathFor(film);

        try
        {
            await Show(tab, line, "FIRST");
            Assert.True((await Start(main, tab, film)).Success);
            await Show(tab, line, "SECOND");
            _now = TimeSpan.FromMilliseconds(400);
            main.TickRecordingForTesting(tab.Session);
            _now = TimeSpan.FromMilliseconds(1000);

            main.CloseTabForTesting(tab);
            await main.WaitForClosedFilmsForTesting();

            Assert.EndsWith("ffmpeg used: " + ffmpeg.Path + " (set in Preferences)", main.LastClosedFilmReport!, StringComparison.Ordinal);
            Assert.Contains("  " + mp4 + " - MP4 (H.264", main.LastClosedFilmReport!, StringComparison.Ordinal);
            Assert.True(new FileInfo(mp4).Length > 0);
        }
        finally
        {
            main.Close();
            if (File.Exists(film)) File.Delete(film);
            if (File.Exists(mp4)) File.Delete(mp4);
        }
    }

    /// <summary>
    /// The Preferences path decides: one that names no file gives the PNG only, and RECORDSTOP's
    /// reply says so, on the real window's registry.
    /// </summary>
    [AvaloniaFact]
    public async Task WithTheFfmpegInPreferencesMissingTheFilmIsThePngAndTheReplySaysWhy()
    {
        var (main, tab, line) = await Window();
        var film = TempPng("film-no-ffmpeg");

        try
        {
            await Show(tab, line, "NO FFMPEG");
            Assert.True((await Start(main, tab, film)).Success);
            _now = TimeSpan.FromMilliseconds(400);

            var stop = await Stop(main, tab);

            Assert.True(stop.Success, stop.Error);
            Assert.EndsWith("files written:\n"
                + "  " + film + " - animated PNG\n"
                + "no MP4: the ffmpeg set in Preferences (" + MissingFfmpeg + ") does not exist - fix the path in "
                + "Preferences, Recording tab, or empty it to use the one on the PATH", stop.Output!, StringComparison.Ordinal);
            Assert.True(File.Exists(film));
            Assert.False(File.Exists(Mp4FromAnimatedPng.Mp4PathFor(film)));
        }
        finally
        {
            main.Close();
            if (File.Exists(film)) File.Delete(film);
        }
    }

    /// <summary>
    /// A film of the real window's terminal, made into an MP4 by the ffmpeg set in Preferences -
    /// here the one the PATH holds, found the way the app finds it. Needs ffmpeg; fails naming it.
    /// </summary>
    [AvaloniaFact]
    [Trait("Category", "Ffmpeg")]
    public async Task WithFfmpegSetInPreferencesTheRealWindowsFilmGetsAnMp4()
    {
        var ffmpeg = FfmpegLocator.FindOnThisMachine(null);
        if (ffmpeg.Path == null)
        {
            Assert.Fail("this test needs ffmpeg and none was found the way RetroTerm looks for it: " + ffmpeg.Problem);
        }
        ThemeManager.Instance.FfmpegPath = ffmpeg.Path;

        var (main, tab, line) = await Window();
        var film = TempPng("film-with-ffmpeg");
        var mp4 = Mp4FromAnimatedPng.Mp4PathFor(film);

        try
        {
            await Show(tab, line, "FIRST");
            Assert.True((await Start(main, tab, film)).Success);
            await Show(tab, line, "SECOND");
            _now = TimeSpan.FromMilliseconds(600);
            main.TickRecordingForTesting(tab.Session);
            _now = TimeSpan.FromMilliseconds(1500);

            var stop = await Stop(main, tab);

            Assert.True(stop.Success, stop.Error);
            int width = (int)Math.Round(tab.Control.Bounds.Width);
            int height = (int)Math.Round(tab.Control.Bounds.Height);
            Assert.EndsWith("files written:\n"
                + "  " + film + " - animated PNG\n"
                + "  " + mp4 + " - MP4 (H.264, yuv420p, " + ((width + 1) & ~1) + "x" + ((height + 1) & ~1) + ")\n"
                + "ffmpeg used: " + ffmpeg.Path + " (set in Preferences)", stop.Output!, StringComparison.Ordinal);
            Assert.True(File.Exists(film));
            Assert.True(new FileInfo(mp4).Length > 0);
        }
        finally
        {
            main.Close();
            if (File.Exists(film)) File.Delete(film);
            if (File.Exists(mp4)) File.Delete(mp4);
        }
    }
}
