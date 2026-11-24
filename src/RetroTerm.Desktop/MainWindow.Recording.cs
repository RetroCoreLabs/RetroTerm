//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs/RetroTerm
// RetroTerm — Connecting today's machines to yesterday's systems
//

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using RetroTerm.Core.Commands.Builtin;
using RetroTerm.Core.Recording;
using RetroTerm.Core.Session;
using RetroTerm.Desktop.Models;
using RetroTerm.Desktop.Services;
using ScreenCaptureArea = RetroTerm.Core.Commands.Builtin.ScreenCaptureArea;

namespace RetroTerm.Desktop;

/// <summary>
/// RECORDSTART and RECORDSTOP: films of a session's screen, one recording per session.
/// </summary>
/// <remarks>
/// <para><b>One drawing route</b></para>
/// Every frame is drawn exactly as a SCREENSHOT is: <see cref="FindCaptureTarget"/> picks the
/// terminal control or the window, and <see cref="RenderVisual"/> draws it into an offscreen
/// bitmap. Nothing here knows how a terminal is drawn.
/// <para><b>The frame source can be driven</b></para>
/// A <see cref="DispatcherTimer"/> calls <see cref="TickRecording"/> every interval, and the time
/// of each look comes from <see cref="RecordingClock"/>. Tests turn the timer off, set the clock,
/// and call the tick themselves - the same way CursorBlinkLifetimeTests drives the blink - so the
/// film a test gets never depends on how fast the machine is. The timing rules themselves (a frame
/// only when the picture changed, lasting as long as it was shown) live in
/// <see cref="ScreenFilm"/>.
/// <para><b>When a recording ends by itself</b></para>
/// If the area can no longer be drawn (a window picture while another tab is selected, a picture
/// that changed size) the recording ends there and the file is finished with what it has. The
/// reason is kept until RECORDSTOP collects it, so a script hears why its film is shorter than it
/// asked for, instead of the film quietly showing something else.
/// <para><b>Closing the tab finishes the file</b></para>
/// Every path that closes a tab calls <see cref="EndRecordingForTab"/>, next to where it stops the
/// session log. It closes the PNG at once and then runs the same finish RECORDSTOP runs, MP4
/// included, in the background; closing the main window waits for those, with a time limit
/// (<see cref="FilmFinishLimitOnExit"/>).
/// </remarks>
public partial class MainWindow
{
    /// <summary>
    /// One running (or ended but not yet collected) recording.
    /// </summary>
    private sealed class ActiveRecording
    {
        /// <summary>
        /// The film being written.
        /// </summary>
        public required ScreenFilm Film { get; init; }

        /// <summary>
        /// What is being drawn.
        /// </summary>
        public required ScreenCaptureArea Area { get; init; }

        /// <summary>
        /// The timer that looks at the screen, or null when tests drive the ticks.
        /// </summary>
        public DispatcherTimer? Timer { get; set; }

        /// <summary>
        /// The picture converted for the film, reused from tick to tick.
        /// </summary>
        public byte[] Rgba { get; set; } = Array.Empty<byte>();

        /// <summary>
        /// Set when the recording ended before RECORDSTOP, saying why.
        /// </summary>
        public string? EndedBecause { get; set; }
    }

    private readonly Dictionary<TerminalSession, ActiveRecording> _recordings = new();

    private readonly Stopwatch _recordingStopwatch = Stopwatch.StartNew();

    /// <summary>
    /// Where the time of each look comes from. Tests replace it with a clock they move by hand.
    /// </summary>
    internal Func<TimeSpan>? RecordingClock { get; set; }

    /// <summary>
    /// When true, a new recording gets no timer and only moves when a test calls
    /// <see cref="TickRecordingForTesting"/>.
    /// </summary>
    internal bool RecordingTimersOffForTesting { get; set; }

    /// <summary>
    /// The time of a look: <see cref="RecordingClock"/> when a test set one, otherwise the time
    /// since the window was made, which only goes forward.
    /// </summary>
    /// <returns>
    /// The time.
    /// </returns>
    private TimeSpan RecordingNow() => RecordingClock != null ? RecordingClock() : _recordingStopwatch.Elapsed;

    /// <summary>
    /// RECORDSTART for one session. Takes the first frame at once, so a recording that could not
    /// draw anything is refused here and not discovered later.
    /// </summary>
    /// <param name="session">
    /// The session.
    /// </param>
    /// <param name="area">
    /// What to draw.
    /// </param>
    /// <param name="path">
    /// Full path of the film.
    /// </param>
    /// <param name="intervalMs">
    /// How often to look.
    /// </param>
    /// <returns>
    /// Null when recording, or why not.
    /// </returns>
    private string? StartRecording(TerminalSession session, ScreenCaptureArea area, string path, int intervalMs)
    {
        if (session == null) return "no session";

        return Dispatcher.UIThread.Invoke(() =>
        {
            if (_recordings.TryGetValue(session, out var existing))
            {
                return existing.EndedBecause == null
                    ? $"a recording is already running for this session (to {existing.Film.Path}) - RECORDSTOP first"
                    : $"the last recording for this session (to {existing.Film.Path}) ended by itself and was not "
                      + "collected yet - RECORDSTOP first to hear why";
            }

            var target = FindCaptureTarget(session, area, out var problem);
            if (target == null) return problem;

            var recording = new ActiveRecording { Film = new ScreenFilm(path), Area = area };

            // The first frame, now. Its failure is the command's failure.
            var firstProblem = LookOnce(recording, target);
            if (firstProblem != null)
            {
                recording.Film.Dispose();
                return firstProblem;
            }

            _recordings[session] = recording;

            if (!RecordingTimersOffForTesting)
            {
                var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(intervalMs) };
                timer.Tick += (s, e) => TickRecording(session);
                recording.Timer = timer;
                timer.Start();
            }

            return null;
        });
    }

    /// <summary>
    /// RECORDSTOP for one session: finishes the file and reports what is in it.
    /// </summary>
    /// <param name="session">
    /// The session.
    /// </param>
    /// <returns>
    /// The outcome, or a refusal when no recording was started.
    /// </returns>
    private ScreenRecordingOutcome StopRecording(TerminalSession session)
    {
        if (session == null) return ScreenRecordingOutcome.Refused("no session");

        return Dispatcher.UIThread.Invoke(() =>
        {
            if (!_recordings.TryGetValue(session, out var recording))
            {
                return ScreenRecordingOutcome.Refused(
                    "no recording is running for this session - RECORDSTART first");
            }

            _recordings.Remove(session);
            return EndRecording(recording, null);
        });
    }

    /// <summary>
    /// One look at the screen for one session's recording. Called by the timer, and by tests.
    /// </summary>
    /// <param name="session">
    /// The session.
    /// </param>
    private void TickRecording(TerminalSession session)
    {
        if (!_recordings.TryGetValue(session, out var recording)) return;
        if (recording.EndedBecause != null) return;

        var target = FindCaptureTarget(session, recording.Area, out var problem);
        if (target == null)
        {
            EndRecording(recording, problem);
            return;
        }

        var lookProblem = LookOnce(recording, target);
        if (lookProblem != null)
        {
            EndRecording(recording, lookProblem);
        }
    }

    /// <summary>
    /// Draws the target and offers the picture to the film.
    /// </summary>
    /// <param name="recording">
    /// The recording.
    /// </param>
    /// <param name="target">
    /// What to draw, from <see cref="FindCaptureTarget"/>.
    /// </param>
    /// <returns>
    /// Null when the picture went into the film (changed or not), or why the recording cannot go on.
    /// </returns>
    private string? LookOnce(ActiveRecording recording, global::Avalonia.Visual target)
    {
        // Same size rule as SCREENSHOT: a visual never laid out has no pixels.
        int width = (int)Math.Round(target.Bounds.Width);
        int height = (int)Math.Round(target.Bounds.Height);
        if (width <= 0 || height <= 0)
        {
            return "that tab has not been laid out yet, so it has no pixels to record - select it in the window first";
        }

        try
        {
            using var bitmap = RenderVisual(target, width, height);

            int count = width * height * 4;
            if (recording.Rgba.Length != count) recording.Rgba = new byte[count];

            var problem = CopyAsStraightRgba(bitmap, width, height, recording.Rgba);
            if (problem != null) return problem;

            var offer = recording.Film.Offer(recording.Rgba, width, height, RecordingNow());
            if (offer == ScreenFilmOffer.SizeChanged)
            {
                return $"the picture changed size from {recording.Film.Width}x{recording.Film.Height} to {width}x{height}, "
                    + "and a film has one size";
            }

            return null;
        }
        catch (Exception ex)
        {
            return "recording a frame failed: " + ex.Message;
        }
    }

    /// <summary>
    /// Copies a drawn bitmap's pixels out as red, green, blue, alpha bytes, not premultiplied,
    /// which is what a PNG holds.
    /// </summary>
    /// <param name="bitmap">
    /// The drawn bitmap.
    /// </param>
    /// <param name="width">
    /// Its width.
    /// </param>
    /// <param name="height">
    /// Its height.
    /// </param>
    /// <param name="rgba">
    /// Where the pixels go; width times height times four bytes.
    /// </param>
    /// <returns>
    /// Null when copied, or why the bitmap's layout is one this does not know.
    /// </returns>
    /// <remarks>
    /// The byte order and alpha form are read from the bitmap rather than assumed, and a layout
    /// not handled here stops the recording by name instead of writing wrong colours - the red
    /// and blue swap CLAUDE.md records was exactly an assumed byte order.
    /// </remarks>
    private static string? CopyAsStraightRgba(global::Avalonia.Media.Imaging.RenderTargetBitmap bitmap,
        int width, int height, byte[] rgba)
    {
        var format = bitmap.Format;
        bool bgra;
        if (format == global::Avalonia.Platform.PixelFormats.Bgra8888) bgra = true;
        else if (format == global::Avalonia.Platform.PixelFormats.Rgba8888) bgra = false;
        else return "the drawn picture is in pixel format " + format + ", which the recorder does not read";

        var alpha = bitmap.AlphaFormat;
        bool premultiplied = alpha == global::Avalonia.Platform.AlphaFormat.Premul;

        int stride = width * 4;
        var handle = GCHandle.Alloc(rgba, GCHandleType.Pinned);
        try
        {
            bitmap.CopyPixels(new global::Avalonia.PixelRect(0, 0, width, height),
                handle.AddrOfPinnedObject(), rgba.Length, stride);
        }
        finally
        {
            handle.Free();
        }

        for (int i = 0; i < rgba.Length; i += 4)
        {
            byte c0 = rgba[i];
            byte c2 = rgba[i + 2];
            if (bgra)
            {
                rgba[i] = c2;
                rgba[i + 2] = c0;
            }

            if (alpha == global::Avalonia.Platform.AlphaFormat.Opaque)
            {
                rgba[i + 3] = 255;
            }
            else if (premultiplied)
            {
                int a = rgba[i + 3];
                if (a == 0)
                {
                    rgba[i] = 0;
                    rgba[i + 1] = 0;
                    rgba[i + 2] = 0;
                }
                else if (a != 255)
                {
                    rgba[i] = (byte)Math.Min(255, (rgba[i] * 255 + a / 2) / a);
                    rgba[i + 1] = (byte)Math.Min(255, (rgba[i + 1] * 255 + a / 2) / a);
                    rgba[i + 2] = (byte)Math.Min(255, (rgba[i + 2] * 255 + a / 2) / a);
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Ends a recording: stops its timer and finishes the file.
    /// </summary>
    /// <param name="recording">
    /// The recording.
    /// </param>
    /// <param name="because">
    /// Why it ended by itself, or null when RECORDSTOP or a closed tab ended it.
    /// </param>
    /// <returns>
    /// What was written.
    /// </returns>
    private ScreenRecordingOutcome EndRecording(ActiveRecording recording, string? because)
    {
        if (recording.Timer != null)
        {
            recording.Timer.Stop();
            recording.Timer = null;
        }

        if (recording.EndedBecause == null)
        {
            recording.EndedBecause = because;
            try
            {
                recording.Film.Finish(RecordingNow());
            }
            catch (Exception ex)
            {
                recording.Film.Dispose();
                recording.EndedBecause = "finishing the file failed: " + ex.Message;
            }
        }

        return new ScreenRecordingOutcome
        {
            Path = recording.Film.Path,
            Pictures = recording.Film.Pictures,
            Frames = recording.Film.FramesWritten,
            Width = recording.Film.Width,
            Height = recording.Film.Height,
            Duration = recording.Film.Duration,
            EndedEarlyBecause = recording.EndedBecause
        };
    }

    /// <summary>
    /// Finishes the film of a tab that is closing. Called next to every StopSessionLogging on a
    /// close path, on the UI thread.
    /// </summary>
    /// <param name="tab">
    /// The closing tab.
    /// </param>
    private void EndRecordingForTab(TabSession tab)
    {
        if (!_recordings.TryGetValue(tab.Session, out var recording)) return;
        _recordings.Remove(tab.Session);
        var outcome = EndRecording(recording, null);
        StartClosedFilmFinish(outcome);
    }

    // ── The finish of a film whose tab closed ───────────────────────

    /// <summary>
    /// The finishes of closed tabs' films that may still be running ffmpeg. Touched on the UI
    /// thread only.
    /// </summary>
    private readonly List<Task> _filmFinishes = new();

    /// <summary>
    /// Stops every ffmpeg still running for a closed tab's film; cancelled only when the main
    /// window is closing and the wait in <see cref="FinishClosedFilmsBeforeExitAsync"/> ran out.
    /// </summary>
    private readonly CancellationTokenSource _filmFinishCancel = new();

    /// <summary>
    /// What runs ffmpeg on a finished film: <see cref="Mp4Encoder.MakeAsync"/>. RECORDSTOP and the
    /// close paths both take it from here. Tests put in one that answers at once or never.
    /// </summary>
    internal Mp4Maker RecordingMp4Maker { get; set; } = Mp4Encoder.MakeAsync;

    /// <summary>
    /// How long closing the main window waits for MP4s still being made before it stops ffmpeg.
    /// </summary>
    /// <remarks>
    /// Why wait at all, rather than let the finish go on after the window is gone: the finish runs
    /// inside this process, so when the process ends the report has nowhere to go, and on Windows
    /// the ffmpeg child is left running with nobody to read its pipes or tidy a half-written MP4.
    /// Why a limit: a stuck ffmpeg must not keep RetroTerm from closing. The wait does not hold the
    /// UI thread (OnClosing has already cancelled the close and awaits), so the window keeps
    /// drawing while it waits. Thirty seconds is a judgement, not a measurement: the films in the
    /// Ffmpeg tests convert in well under a second, and a long session film is the case the margin
    /// is for. When it runs out, ffmpeg is stopped with what it started, its unfinished MP4 is
    /// deleted, and the PNG stays.
    /// </remarks>
    internal TimeSpan FilmFinishLimitOnExit { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// The last report of a closed tab's film, as written to the log.
    /// </summary>
    internal string? LastClosedFilmReport { get; private set; }

    /// <summary>
    /// Which ffmpeg a film's MP4 is made with: the one set in Preferences, Recording tab, else the
    /// one on the PATH, asked each time so a Preferences change counts at once.
    /// </summary>
    /// <returns>
    /// The choice, or why there is none.
    /// </returns>
    private static FfmpegChoice FindRecordingFfmpeg()
        => FfmpegLocator.FindOnThisMachine(Themes.ThemeManager.Instance.FfmpegPath);

    /// <summary>
    /// Runs the same finish RECORDSTOP runs (<see cref="FilmFinish.FinishAsync"/>) for a film
    /// whose tab, popped-out window or main window closed, off the UI thread.
    /// </summary>
    /// <param name="outcome">
    /// What <see cref="EndRecording"/> wrote.
    /// </param>
    /// <remarks>
    /// No command is waiting for this report, so it goes where the app already puts things nobody
    /// asked for: the application log (the Log window, and its file when that is switched on) and
    /// the status bar. The session log cannot take it: every close path stops the session log just
    /// before it ends the recording.
    /// </remarks>
    private void StartClosedFilmFinish(ScreenRecordingOutcome outcome)
    {
        // Asked here, on the UI thread, so the background task reads no Preferences object.
        var ffmpeg = FindRecordingFfmpeg();
        var make = RecordingMp4Maker;
        var token = _filmFinishCancel.Token;
        var limit = FilmFinishLimitOnExit;

        var task = Task.Run(async () =>
        {
            string report;
            try
            {
                report = await FilmFinish.FinishAsync(outcome, () => ffmpeg, make, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                report = $"film saved to {outcome.Path}\nno MP4: RetroTerm was closing and ffmpeg was still making it after "
                    + $"{limit.TotalSeconds.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)} s, so ffmpeg "
                    + "was stopped; the animated PNG is kept";
            }
            catch (Exception ex)
            {
                report = $"film saved to {outcome.Path}\nno MP4: making it failed: {ex.Message}";
            }

            ReportClosedFilm(report);
        });

        // Drop the ones already done, so a long session does not keep a growing list.
        for (int i = _filmFinishes.Count - 1; i >= 0; i--)
        {
            if (_filmFinishes[i].IsCompleted) _filmFinishes.RemoveAt(i);
        }
        _filmFinishes.Add(task);
    }

    /// <summary>
    /// Puts a closed tab's film report in the application log and the status bar.
    /// </summary>
    /// <param name="report">
    /// The report, several lines.
    /// </param>
    private void ReportClosedFilm(string report)
    {
        var text = "recording of a closed tab finished: " + report;
        LastClosedFilmReport = text;
        Core.Logging.ApplicationLogger.Log(Core.Logging.LogCategory.General, Core.Logging.LogLevel.Info, "Recording", text);

        // The status bar holds one line.
        var line = text.Replace("\r", string.Empty).Replace('\n', ' ');
        Dispatcher.UIThread.Post(() => UpdateStatus(line));
    }

    /// <summary>
    /// Waits for closed tabs' MP4s before the main window goes, up to
    /// <see cref="FilmFinishLimitOnExit"/>, then stops what is left and waits for its report.
    /// </summary>
    /// <returns>
    /// A task that completes when every finish has reported.
    /// </returns>
    private async Task FinishClosedFilmsBeforeExitAsync()
    {
        if (_filmFinishes.Count == 0) return;

        var all = Task.WhenAll(_filmFinishes.ToArray());
        var first = await Task.WhenAny(all, Task.Delay(FilmFinishLimitOnExit)).ConfigureAwait(true);
        if (first != all)
        {
            _filmFinishCancel.Cancel();
        }
        await all.ConfigureAwait(true);
        _filmFinishes.Clear();
    }

    /// <summary>
    /// Waits for every closed tab's film finish that is still running.
    /// </summary>
    /// <returns>
    /// A task that completes when they have all reported.
    /// </returns>
    internal Task WaitForClosedFilmsForTesting() => Task.WhenAll(_filmFinishes.ToArray());

    /// <summary>
    /// The part of closing the main window that ends every tab and popped-out window, without
    /// the question about connected sessions.
    /// </summary>
    /// <returns>
    /// A task that completes when everything is closed and every film has reported.
    /// </returns>
    internal Task CleanUpForExitForTesting() => CleanUpAllTabsAndPopouts();

    /// <summary>
    /// Closes a tab the way its close button does.
    /// </summary>
    /// <param name="tab">
    /// The tab.
    /// </param>
    internal void CloseTabForTesting(TabSession tab) => CloseTab(tab);

    /// <summary>
    /// One look at a session's screen, as the recording timer takes.
    /// </summary>
    /// <param name="session">
    /// The session being recorded.
    /// </param>
    internal void TickRecordingForTesting(TerminalSession session) => TickRecording(session);

    /// <summary>
    /// Whether a recording is held for a session.
    /// </summary>
    /// <param name="session">
    /// The session.
    /// </param>
    /// <returns>
    /// True while one is running or ended but not collected.
    /// </returns>
    internal bool IsRecordingForTesting(TerminalSession session) => _recordings.ContainsKey(session);
}
