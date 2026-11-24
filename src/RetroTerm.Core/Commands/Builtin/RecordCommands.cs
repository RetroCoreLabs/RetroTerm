//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs/RetroTerm
// RetroTerm — Connecting today's machines to yesterday's systems
//

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using RetroTerm.Core.Recording;
using RetroTerm.Core.Session;

namespace RetroTerm.Core.Commands.Builtin;

/// <summary>
/// What a finished recording holds, or why there is none.
/// </summary>
public sealed class ScreenRecordingOutcome
{
    /// <summary>
    /// Why nothing could be stopped, or null when a recording was stopped.
    /// </summary>
    public string? Problem { get; set; }

    /// <summary>
    /// Full path of the film written.
    /// </summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>
    /// How many different pictures the film holds.
    /// </summary>
    public int Pictures { get; set; }

    /// <summary>
    /// How many frames the file holds; more than <see cref="Pictures"/> only when one picture
    /// lasted longer than an APNG frame can.
    /// </summary>
    public int Frames { get; set; }

    /// <summary>
    /// Picture width in pixels.
    /// </summary>
    public int Width { get; set; }

    /// <summary>
    /// Picture height in pixels.
    /// </summary>
    public int Height { get; set; }

    /// <summary>
    /// How long the film plays.
    /// </summary>
    public TimeSpan Duration { get; set; }

    /// <summary>
    /// When the recording ended by itself before RECORDSTOP, the reason; otherwise null.
    /// </summary>
    public string? EndedEarlyBecause { get; set; }

    /// <summary>
    /// A refusal with a reason.
    /// </summary>
    /// <param name="problem">
    /// The reason.
    /// </param>
    /// <returns>
    /// The outcome.
    /// </returns>
    public static ScreenRecordingOutcome Refused(string problem) => new ScreenRecordingOutcome { Problem = problem };
}

/// <summary>
/// Starts recording one session's screen.
/// </summary>
/// <param name="session">
/// The session.
/// </param>
/// <param name="area">
/// The terminal area or the whole window - the same choice SCREENSHOT offers.
/// </param>
/// <param name="path">
/// Full path of the film.
/// </param>
/// <param name="intervalMs">
/// How often the screen is looked at, in milliseconds.
/// </param>
/// <returns>
/// Null when the recording started (its first frame is already taken), or why it did not.
/// </returns>
public delegate string? ScreenRecordingStart(TerminalSession session, ScreenCaptureArea area, string path, int intervalMs);

/// <summary>
/// Stops the recording of one session's screen and finishes the file.
/// </summary>
/// <param name="session">
/// The session.
/// </param>
/// <returns>
/// What was written, or a refusal.
/// </returns>
public delegate ScreenRecordingOutcome ScreenRecordingStop(TerminalSession session);

/// <summary>
/// RECORDSTART and RECORDSTOP: record the screen as an animated PNG film.
/// </summary>
/// <remarks>
/// <para><b>The names</b></para>
/// docs\PLAN.md wrote them "RECORD START" and "RECORD STOP". A script line's verb is one word
/// (ScriptParser takes the first token as the command), and the existing pairs are written as one
/// word too - LOGSTART/LOGSTOP, TRACESTART/TRACESTOP - so these are RECORDSTART and RECORDSTOP,
/// and MCP gets terminal_recordstart and terminal_recordstop from the same declarations.
/// <para><b>Core declares, the desktop draws</b></para>
/// The same shape as SCREENSHOT: only the desktop can draw a frame, so it hands in the doing, and
/// it draws each frame through the same route a SCREENSHOT takes.
/// </remarks>
public static class RecordCommands
{
    /// <summary>
    /// The sampling interval when none is given: ten looks a second.
    /// </summary>
    public const int DefaultIntervalMs = 100;

    /// <summary>
    /// The shortest sampling interval accepted. Each look draws the whole area on the UI thread,
    /// and faster than this would take the window's time away from the person using it.
    /// </summary>
    public const int MinIntervalMs = 20;

    /// <summary>
    /// The longest sampling interval accepted.
    /// </summary>
    public const int MaxIntervalMs = 10000;

    /// <summary>
    /// Adds both commands to a registry, which gives the script DSL and MCP the same two.
    /// </summary>
    /// <param name="registry">
    /// The registry.
    /// </param>
    /// <param name="start">
    /// The desktop's way of starting a recording.
    /// </param>
    /// <param name="stop">
    /// The desktop's way of stopping one.
    /// </param>
    /// <param name="findFfmpeg">
    /// Which ffmpeg RECORDSTOP makes the MP4 with, asked at each stop so a Preferences change
    /// counts at once: the desktop passes <see cref="FfmpegLocator.FindOnThisMachine"/> over its
    /// Preferences path.
    /// </param>
    /// <param name="makeMp4">
    /// What runs that ffmpeg on the finished film (the desktop's Mp4Encoder.MakeAsync).
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when an argument is null.
    /// </exception>
    public static void RegisterAll(CommandRegistry registry, ScreenRecordingStart start, ScreenRecordingStop stop,
        Func<FfmpegChoice> findFfmpeg, Mp4Maker makeMp4)
    {
        if (registry == null) throw new ArgumentNullException(nameof(registry));
        registry.Register(new RecordStartCommand(start));
        registry.Register(new RecordStopCommand(stop, findFfmpeg, makeMp4));
    }
}

/// <summary>
/// RECORDSTART - begin recording the screen to an animated PNG.
/// </summary>
public sealed class RecordStartCommand : ISessionCommand
{
    private readonly ScreenRecordingStart _start;

    /// <summary>
    /// Builds the command around the desktop's recorder.
    /// </summary>
    /// <param name="start">
    /// What starts the recording.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="start"/> is null.
    /// </exception>
    public RecordStartCommand(ScreenRecordingStart start)
    {
        _start = start ?? throw new ArgumentNullException(nameof(start));
    }

    /// <summary>
    /// The command's name in the script DSL.
    /// </summary>
    public string Name => "RECORDSTART";

    /// <summary>
    /// One line for the generated help.
    /// </summary>
    public string Summary => "Start recording the screen as a film (animated PNG): a frame each time the picture changes, "
        + "lasting as long as it was shown; RECORDSTOP finishes the file";

    /// <summary>
    /// A usable example for the generated help.
    /// </summary>
    public string Example => "RECORDSTART \"C:\\\\films\\\\login.png\" area=terminal"; // \\ in the DSL - \ starts an escape

    /// <summary>
    /// Whether the command writes a line of output.
    /// </summary>
    public bool ProducesOutput => true;

    /// <summary>
    /// The parameters, read by the script parser and the MCP tool list alike.
    /// </summary>
    public IReadOnlyList<CommandParameter> Parameters { get; } = new[]
    {
        new CommandParameter("file", CommandParameterType.String, required: true, defaultValue: null,
            "Animated PNG file to write (directory must exist); an existing file is replaced"),

        // The same words as SCREENSHOT, declared once.
        ScreenCaptureAreaParameter.CreateParameter(),

        new CommandParameter("interval", CommandParameterType.Int, required: false,
            defaultValue: RecordCommands.DefaultIntervalMs.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "How often the screen is looked at, in milliseconds (" + RecordCommands.MinIntervalMs + " to "
            + RecordCommands.MaxIntervalMs + "). A frame is only added when the picture changed, so this sets "
            + "how exact each frame's duration is, not the size of the file")
    };

    /// <summary>
    /// Checks the arguments and starts the recording.
    /// </summary>
    /// <param name="session">
    /// The session to record.
    /// </param>
    /// <param name="args">
    /// The parsed arguments.
    /// </param>
    /// <param name="cancellationToken">
    /// Unused; starting does not wait.
    /// </param>
    /// <returns>
    /// Where the film goes, or why it could not start.
    /// </returns>
    public Task<CommandResult> ExecuteAsync(TerminalSession session, CommandArgs args, CancellationToken cancellationToken)
    {
        var path = Path.GetFullPath(args.GetString("file")!);
        var dir = Path.GetDirectoryName(path);
        if (dir != null && !Directory.Exists(dir))
        {
            return Task.FromResult(CommandResult.Fail($"directory not found: {dir}"));
        }

        int interval = args.GetInt("interval", RecordCommands.DefaultIntervalMs);
        if (interval < RecordCommands.MinIntervalMs || interval > RecordCommands.MaxIntervalMs)
        {
            return Task.FromResult(CommandResult.Fail(
                $"interval={interval} is outside {RecordCommands.MinIntervalMs} to {RecordCommands.MaxIntervalMs} milliseconds"));
        }

        var area = ScreenCaptureAreaParameter.Parse(args);

        var problem = _start(session, area, path, interval);
        if (problem != null)
        {
            return Task.FromResult(CommandResult.Fail(problem));
        }

        return Task.FromResult(CommandResult.Ok(
            $"recording the {(area == ScreenCaptureArea.Window ? "window" : "screen")} to {path}, looking every {interval} ms - RECORDSTOP finishes it"));
    }
}

/// <summary>
/// RECORDSTOP - stop the recording and finish the file.
/// </summary>
public sealed class RecordStopCommand : ISessionCommand
{
    private readonly ScreenRecordingStop _stop;
    private readonly Func<FfmpegChoice> _findFfmpeg;
    private readonly Mp4Maker _makeMp4;

    /// <summary>
    /// Builds the command around the desktop's recorder.
    /// </summary>
    /// <param name="stop">
    /// What stops the recording.
    /// </param>
    /// <param name="findFfmpeg">
    /// Which ffmpeg makes the MP4, or why there is none.
    /// </param>
    /// <param name="makeMp4">
    /// What runs it.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when an argument is null.
    /// </exception>
    public RecordStopCommand(ScreenRecordingStop stop, Func<FfmpegChoice> findFfmpeg, Mp4Maker makeMp4)
    {
        _stop = stop ?? throw new ArgumentNullException(nameof(stop));
        _findFfmpeg = findFfmpeg ?? throw new ArgumentNullException(nameof(findFfmpeg));
        _makeMp4 = makeMp4 ?? throw new ArgumentNullException(nameof(makeMp4));
    }

    /// <summary>
    /// The command's name in the script DSL.
    /// </summary>
    public string Name => "RECORDSTOP";

    /// <summary>
    /// One line for the generated help.
    /// </summary>
    public string Summary => "Stop the screen recording started by RECORDSTART and finish the file; also makes an MP4 beside it "
        + "when ffmpeg is found (Preferences, Recording tab, else the PATH); reports every file written with its kind, "
        + "and which ffmpeg made the MP4 or why there is none";

    /// <summary>
    /// A usable example for the generated help.
    /// </summary>
    public string Example => "RECORDSTOP";

    /// <summary>
    /// Whether the command writes a line of output.
    /// </summary>
    public bool ProducesOutput => true;

    /// <summary>
    /// No parameters.
    /// </summary>
    public IReadOnlyList<CommandParameter> Parameters { get; } = Array.Empty<CommandParameter>();

    /// <summary>
    /// Stops the recording.
    /// </summary>
    /// <param name="session">
    /// The session being recorded.
    /// </param>
    /// <param name="args">
    /// None.
    /// </param>
    /// <param name="cancellationToken">
    /// Stops ffmpeg if the MP4 is still being made.
    /// </param>
    /// <returns>
    /// The files and what is in them, or why nothing was stopped.
    /// </returns>
    /// <remarks>
    /// The MP4 is made HERE, in Core, after the desktop has closed the PNG, so the script DSL and
    /// the MCP tool give the same reply from one place (CLAUDE.md, "One behaviour, one place").
    /// The PNG is kept whatever ffmpeg does; a failing ffmpeg is reported with its own error text
    /// and RECORDSTOP still succeeds, because the film the caller asked for was written.
    /// </remarks>
    public async Task<CommandResult> ExecuteAsync(TerminalSession session, CommandArgs args, CancellationToken cancellationToken)
    {
        var outcome = _stop(session);
        if (outcome.Problem != null)
        {
            return CommandResult.Fail(outcome.Problem);
        }

        var text = $"film saved to {outcome.Path} ({outcome.Width}x{outcome.Height}, {outcome.Pictures} picture(s) in "
            + $"{outcome.Frames} frame(s), {outcome.Duration.TotalSeconds.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)} s)";
        if (outcome.EndedEarlyBecause != null)
        {
            text += " - the recording had already ended by itself: " + outcome.EndedEarlyBecause;
        }

        // The MP4, only from a film that was really written.
        FfmpegChoice ffmpeg;
        Mp4Outcome? mp4 = null;
        if (outcome.Frames == 0 || !File.Exists(outcome.Path))
        {
            ffmpeg = new FfmpegChoice { Problem = "the animated PNG was not written, so there is nothing to make it from" };
        }
        else
        {
            ffmpeg = _findFfmpeg();
            if (ffmpeg.Path != null)
            {
                mp4 = await _makeMp4(ffmpeg.Path, outcome.Path, outcome.Width, outcome.Height,
                    cancellationToken).ConfigureAwait(false);
            }
        }
        text += "\n" + Mp4FromAnimatedPng.Describe(outcome.Path, ffmpeg, mp4);

        var result = CommandResult.Ok(text);
        result.CaptureValue = outcome.Path;
        return result;
    }
}
