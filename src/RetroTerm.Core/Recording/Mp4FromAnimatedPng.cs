//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs/RetroTerm
// RetroTerm — Connecting today's machines to yesterday's systems
//

using System;
using System.IO;
using System.Text;

namespace RetroTerm.Core.Recording;

/// <summary>
/// What making the MP4 gave: the file, or ffmpeg's own words about why not.
/// </summary>
public sealed class Mp4Outcome
{
    /// <summary>
    /// Full path of the MP4 written, or null when none was.
    /// </summary>
    public string? Path { get; set; }

    /// <summary>
    /// The MP4's picture width: the film's, plus one when the film's was odd.
    /// </summary>
    public int Width { get; set; }

    /// <summary>
    /// The MP4's picture height: the film's, plus one when the film's was odd.
    /// </summary>
    public int Height { get; set; }

    /// <summary>
    /// Why there is no MP4, ffmpeg's error text included; null when it was made.
    /// </summary>
    public string? Problem { get; set; }
}

/// <summary>
/// Runs ffmpeg on a finished film and reports what it gave.
/// </summary>
/// <param name="ffmpegPath">
/// The ffmpeg program, from <see cref="FfmpegLocator"/>.
/// </param>
/// <param name="filmPath">
/// The finished animated PNG.
/// </param>
/// <param name="width">
/// The film's width.
/// </param>
/// <param name="height">
/// The film's height.
/// </param>
/// <param name="cancellationToken">
/// Stops ffmpeg and gives up.
/// </param>
/// <returns>
/// The MP4, or ffmpeg's reason for not making one.
/// </returns>
/// <remarks>
/// A delegate because running and stopping a process tree needs APIs this netstandard2.1 library
/// does not have (Process.WaitForExitAsync, Process.Kill with its tree). The desktop hands in
/// RetroTerm.Desktop.Services.Mp4Encoder.MakeAsync, which runs <see cref="Mp4FromAnimatedPng.Arguments"/>.
/// </remarks>
public delegate System.Threading.Tasks.Task<Mp4Outcome> Mp4Maker(string ffmpegPath, string filmPath, int width, int height,
    System.Threading.CancellationToken cancellationToken);

/// <summary>
/// Turns a finished animated PNG film into an MP4 with ffmpeg, keeping each frame's duration.
/// </summary>
/// <remarks>
/// <para><b>Why ffmpeg reads the APNG itself, with these options</b></para>
/// Measured 24 November 2026 with ffmpeg 7.1.1 and ffprobe on a five-frame film of 500, 1234, 70,
/// 3000 and 17 ms. The APNG straight in with default options put frames at 0.494, 1.729 and
/// 1.800 s instead of 0.500, 1.734 and 1.804, because the encoder's time base (1/10880 there)
/// rounds every duration. The concat demuxer, one PNG per frame with a <c>duration</c> line each,
/// rounded to 1/25 s (0.52, 1.20, 0.08, 3.00, 0.04). The APNG straight in with
/// <c>-fps_mode passthrough -enc_time_base demux -video_track_timescale 1000 -bf 0</c> gave every
/// frame's start and duration exact to the millisecond, the last one included. So that is what
/// runs. Each option, in plain words: passthrough keeps every frame with its
/// own time and invents or drops none; <c>enc_time_base demux</c> keeps the APNG's own time base
/// (1/100000 s) through the encoder; the MP4 track counts in milliseconds, the unit the film is
/// written in; and <c>-bf 0</c> turns off B-frames, which reorder frames and, with frames of
/// uneven length, left the MP4 with no per-frame durations at all.
/// <para><b>H.264, yuv420p, even sizes</b></para>
/// H.264 in yuv420p is what YouTube, browsers and video editors take. yuv420p stores colour at
/// half size, so the width and height must be even: an odd one gets one black column or row
/// added on the right or the bottom (<c>pad</c>), never a stretch. The alpha channel is dropped;
/// a terminal picture is opaque.
/// <para><b>A separate process, after the PNG is closed</b></para>
/// The caller runs this only once the APNG is finished, and ffmpeg runs as its own process. If it
/// fails, the PNG is untouched and its error text is the answer.
/// </remarks>
public static class Mp4FromAnimatedPng
{
    /// <summary>
    /// The most of ffmpeg's error text kept for the reply.
    /// </summary>
    public const int MaxErrorText = 2000;

    /// <summary>
    /// Where the MP4 for a film goes: beside it, the same name with .mp4.
    /// </summary>
    /// <param name="filmPath">
    /// The animated PNG.
    /// </param>
    /// <returns>
    /// The MP4 path. A film already named .mp4 gets a second .mp4 added, so the MP4 can never be
    /// written over the PNG it is made from.
    /// </returns>
    public static string Mp4PathFor(string filmPath)
    {
        var mp4 = Path.ChangeExtension(filmPath, ".mp4");
        return string.Equals(mp4, filmPath, StringComparison.OrdinalIgnoreCase) ? filmPath + ".mp4" : mp4;
    }

    /// <summary>
    /// The arguments ffmpeg is started with, one per entry, as measured above.
    /// </summary>
    /// <param name="filmPath">
    /// The animated PNG.
    /// </param>
    /// <param name="mp4Path">
    /// The MP4 to write; an existing one is replaced.
    /// </param>
    /// <returns>
    /// The arguments.
    /// </returns>
    public static string[] Arguments(string filmPath, string mp4Path) => new[]
    {
        "-hide_banner", "-nostdin", "-loglevel", "error", "-y",
        "-i", filmPath,
        "-fps_mode", "passthrough",
        "-enc_time_base", "demux",
        "-video_track_timescale", "1000",
        "-vf", "pad=ceil(iw/2)*2:ceil(ih/2)*2",
        "-c:v", "libx264", "-bf", "0", "-pix_fmt", "yuv420p",
        mp4Path
    };

    /// <summary>
    /// The lines RECORDSTOP adds about the MP4: the files written, with their kinds, and which
    /// ffmpeg made the MP4 or why there is none.
    /// </summary>
    /// <param name="filmPath">
    /// The animated PNG written.
    /// </param>
    /// <param name="ffmpeg">
    /// The ffmpeg found, or why none.
    /// </param>
    /// <param name="mp4">
    /// What running it gave; null when it was not run.
    /// </param>
    /// <returns>
    /// The lines, each ending in a newline but the last.
    /// </returns>
    public static string Describe(string filmPath, FfmpegChoice ffmpeg, Mp4Outcome? mp4)
    {
        var sb = new StringBuilder();
        sb.Append("files written:\n");
        sb.Append("  ").Append(filmPath).Append(" - animated PNG\n");
        if (mp4?.Path != null)
        {
            sb.Append("  ").Append(mp4.Path).Append(" - MP4 (H.264, yuv420p, ")
              .Append(mp4.Width).Append('x').Append(mp4.Height).Append(")\n");
            sb.Append("ffmpeg used: ").Append(ffmpeg.Path).Append(" (").Append(ffmpeg.Source).Append(')');
        }
        else if (mp4?.Problem != null)
        {
            sb.Append("no MP4: ").Append(mp4.Problem).Append(" - the animated PNG is kept");
        }
        else
        {
            sb.Append("no MP4: ").Append(ffmpeg.Problem);
        }
        return sb.ToString();
    }
}
