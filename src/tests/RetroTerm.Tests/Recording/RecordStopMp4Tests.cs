//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs/RetroTerm
// RetroTerm — Connecting today's machines to yesterday's systems
//

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Protocol;
using RetroTerm.Core.Commands;
using RetroTerm.Core.Commands.Builtin;
using RetroTerm.Core.Recording;
using RetroTerm.Core.Scripting;
using RetroTerm.Core.Session;
using RetroTerm.Desktop.Services;
using RetroTerm.Mcp;
using RetroTerm.Tests.TDV;
using Xunit;

namespace RetroTerm.Tests.Recording;

/// <summary>
/// RECORDSTOP's MP4 (docs\PLAN.md, 24 November 2026): made beside the animated PNG with the ffmpeg
/// set in Preferences or else the one on the PATH, each frame keeping its real duration, and a
/// reply that lists every file written with its kind and which ffmpeg was used, or why there is
/// no MP4. The reply is checked through the real MCP tool provider and through the registry the
/// script runner uses, and must be the same text on both.
/// </summary>
/// <remarks>
/// <para><b>The tests that need ffmpeg</b></para>
/// They carry <c>[Trait("Category", "Ffmpeg")]</c>, find ffmpeg exactly as the app does
/// (<see cref="FfmpegLocator.FindOnThisMachine"/> with no Preferences path, so the PATH), and FAIL
/// naming what is missing when it is not there. They are never skipped. The GitHub gate leaves the
/// category out (.github\workflows\build.yml), as it does Oracle and Hercules.
/// </remarks>
public sealed class RecordStopMp4Tests : IDisposable
{
    private readonly string _folder;
    private readonly InProcessSessionHost _host;
    private readonly string _tempScripts;

    public RecordStopMp4Tests()
    {
        _folder = Path.Combine(Path.GetTempPath(), "RetroTermMp4Tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_folder);
        _tempScripts = Path.Combine(_folder, "scripts");
        _host = new InProcessSessionHost(p => new InMemoryConnection());
    }

    public void Dispose()
    {
        _host.Dispose();
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    // ── helpers ─────────────────────────────────────────────────────

    /// <summary>
    /// Writes an animated PNG with one differently coloured frame per duration, the way
    /// ScreenFilm writes one, and returns what RECORDSTOP would be told about it.
    /// </summary>
    private ScreenRecordingOutcome WriteFilm(string name, int width, int height, int[] durationsMs)
    {
        var path = Path.Combine(_folder, name);
        var rgba = new byte[width * height * 4];
        int frames;
        using (var writer = new AnimatedPngWriter(path, width, height))
        {
            for (int f = 0; f < durationsMs.Length; f++)
            {
                for (int i = 0; i < rgba.Length; i += 4)
                {
                    rgba[i] = (byte)(f * 60);
                    rgba[i + 1] = (byte)(i / 4 % width);
                    rgba[i + 2] = 128;
                    rgba[i + 3] = 255;
                }
                writer.WriteFrame(rgba, durationsMs[f]);
            }
            writer.Finish();
            frames = writer.FramesWritten;
        }

        long total = 0;
        for (int f = 0; f < durationsMs.Length; f++) total += durationsMs[f];
        return new ScreenRecordingOutcome
        {
            Path = path,
            Pictures = durationsMs.Length,
            Frames = frames,
            Width = width,
            Height = height,
            Duration = TimeSpan.FromMilliseconds(total)
        };
    }

    private static CommandRegistry Registry(ScreenRecordingOutcome stopped, Func<FfmpegChoice> findFfmpeg)
    {
        var registry = new CommandRegistry();
        BuiltinCommands.RegisterAll(registry);
        RecordCommands.RegisterAll(registry,
            (session, area, path, interval) => null,
            session => stopped,
            findFfmpeg,
            Mp4Encoder.MakeAsync);
        return registry;
    }

    /// <summary>
    /// Runs RECORDSTOP through the registry (what a script step does) and through the real MCP
    /// tool provider, checks the two texts are the same, and returns it.
    /// </summary>
    private async Task<string> StopOnBothSurfaces(CommandRegistry registry)
    {
        var provider = new RetroTermToolProvider(_host, registry, new ScriptLibrary(_tempScripts));
        var open = await provider.CallToolAsync("terminal_open",
            new Dictionary<string, JsonElement> { ["host"] = JsonSerializer.SerializeToElement("film-host") },
            CancellationToken.None);
        Assert.NotEqual(true, open.IsError);
        var sessions = _host.ListSessions();
        var id = sessions[sessions.Count - 1].Id;

        var script = await registry.ExecuteAsync("RECORDSTOP", _host.GetSession(id)!, new CommandArgs(), CancellationToken.None);
        Assert.True(script.Success, script.Error);

        var mcp = await provider.CallToolAsync("terminal_recordstop",
            new Dictionary<string, JsonElement> { [RetroTermToolProvider.SessionIdArgument] = JsonSerializer.SerializeToElement(id.ToString()) },
            CancellationToken.None);
        Assert.NotEqual(true, mcp.IsError);
        var mcpText = Assert.IsType<TextContentBlock>(mcp.Content[0]).Text;

        // The MCP result is the command's own output plus the timing line the provider adds.
        // When ffmpeg failed, its own error text follows "failed with exit code"; that text differs
        // from one ffmpeg run to the next (memory addresses, and lines from its threads in varying
        // order), so the two replies are compared up to it - everything RetroTerm itself writes.
        const string FfmpegSays = "failed with exit code";
        int cut = script.Output!.IndexOf(FfmpegSays, StringComparison.Ordinal);
        if (cut >= 0)
        {
            Assert.StartsWith(script.Output.Substring(0, cut + FfmpegSays.Length), mcpText, StringComparison.Ordinal);
            Assert.EndsWith(" - the animated PNG is kept\n", mcpText.Substring(0, mcpText.LastIndexOf("[took ", StringComparison.Ordinal)), StringComparison.Ordinal);
        }
        else
        {
            Assert.StartsWith(script.Output + "\n[took ", mcpText, StringComparison.Ordinal);
        }
        return script.Output!;
    }

    /// <summary>
    /// The ffmpeg the app would use with nothing set in Preferences, or a failure naming what is
    /// missing - never a skip.
    /// </summary>
    private static FfmpegChoice RequireFfmpeg()
    {
        var found = FfmpegLocator.FindOnThisMachine(null);
        if (found.Path == null)
        {
            Assert.Fail("this test needs ffmpeg and none was found the way RetroTerm looks for it: " + found.Problem);
        }
        return found;
    }

    /// <summary>
    /// ffprobe from the same folder as the ffmpeg found, or a failure naming it.
    /// </summary>
    private static string RequireFfprobe(FfmpegChoice ffmpeg)
    {
        var name = OperatingSystem.IsWindows() ? "ffprobe.exe" : "ffprobe";
        var probe = Path.Combine(Path.GetDirectoryName(ffmpeg.Path!)!, name);
        if (!File.Exists(probe))
        {
            Assert.Fail("this test reads the MP4 with ffprobe and there is none beside " + ffmpeg.Path + ": " + probe);
        }
        return probe;
    }

    /// <summary>
    /// Runs ffprobe and returns what it printed. A process this test starts and waits for; it is
    /// stopped by its handle, with what it started, if it has not finished in a minute.
    /// </summary>
    private static string Probe(string ffprobe, params string[] args)
    {
        var info = new ProcessStartInfo(ffprobe)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        for (int i = 0; i < args.Length; i++) info.ArgumentList.Add(args[i]);
        using var process = Process.Start(info)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(60000))
        {
            process.Kill(entireProcessTree: true);
            Assert.Fail("ffprobe did not finish in a minute");
        }
        Assert.True(process.ExitCode == 0, "ffprobe failed: " + error.Result);
        return output.Result;
    }

    // ── finding ffmpeg (no ffmpeg needed) ───────────────────────────

    [Fact]
    public void APathSetInPreferencesIsUsedWhenItExists()
    {
        var fake = Path.Combine(_folder, "my-ffmpeg.exe");
        File.WriteAllBytes(fake, Array.Empty<byte>());

        var found = FfmpegLocator.Find(fake, null);

        Assert.Equal(Path.GetFullPath(fake), found.Path);
        Assert.Equal("set in Preferences", found.Source);
        Assert.Null(found.Problem);
    }

    [Fact]
    public void AMissingPreferencesPathIsReportedAndThePathIsNotUsedInstead()
    {
        // An ffmpeg on the PATH that must NOT be taken: the person set another one.
        var pathFolder = Path.Combine(_folder, "on-path");
        Directory.CreateDirectory(pathFolder);
        File.WriteAllBytes(Path.Combine(pathFolder, FfmpegLocator.ProgramName), Array.Empty<byte>());
        var missing = Path.Combine(_folder, "no-such-ffmpeg.exe");

        var found = FfmpegLocator.Find(missing, pathFolder);

        Assert.Null(found.Path);
        Assert.Equal($"the ffmpeg set in Preferences ({missing}) does not exist - fix the path in "
            + "Preferences, Recording tab, or empty it to use the one on the PATH", found.Problem);
    }

    [Fact]
    public void WithNothingSetTheFirstFfmpegOnThePathIsUsed()
    {
        var empty = Path.Combine(_folder, "empty");
        var first = Path.Combine(_folder, "first");
        var second = Path.Combine(_folder, "second");
        Directory.CreateDirectory(empty);
        Directory.CreateDirectory(first);
        Directory.CreateDirectory(second);
        File.WriteAllBytes(Path.Combine(first, FfmpegLocator.ProgramName), Array.Empty<byte>());
        File.WriteAllBytes(Path.Combine(second, FfmpegLocator.ProgramName), Array.Empty<byte>());

        var found = FfmpegLocator.Find("  ", empty + Path.PathSeparator + first + Path.PathSeparator + second);

        Assert.Equal(Path.Combine(first, FfmpegLocator.ProgramName), found.Path);
        Assert.Equal("found on the PATH", found.Source);
    }

    [Fact]
    public void WithNothingSetAndNoneOnThePathTheReasonSaysBoth()
    {
        var found = FfmpegLocator.Find(null, Path.Combine(_folder, "nothing-here"));

        Assert.Null(found.Path);
        Assert.Equal("no ffmpeg was found: none is set in Preferences, Recording tab, and there is no "
            + FfmpegLocator.ProgramName + " on the PATH", found.Problem);
    }

    [Fact]
    public void TheMp4GoesBesideTheFilmAndNeverOverIt()
    {
        Assert.Equal(Path.Combine(_folder, "login.mp4"), Mp4FromAnimatedPng.Mp4PathFor(Path.Combine(_folder, "login.png")));
        Assert.Equal(Path.Combine(_folder, "odd.mp4.mp4"), Mp4FromAnimatedPng.Mp4PathFor(Path.Combine(_folder, "odd.mp4")));
    }

    // ── RECORDSTOP with no ffmpeg ───────────────────────────────────

    [Fact]
    public async Task WithNoFfmpegOnlyThePngIsWrittenAndTheReplySaysWhy()
    {
        var film = WriteFilm("no-ffmpeg.png", 40, 20, new[] { 300, 700 });
        var missing = Path.Combine(_folder, "missing", FfmpegLocator.ProgramName);
        var registry = Registry(film, () => FfmpegLocator.Find(missing, null));

        var text = await StopOnBothSurfaces(registry);

        Assert.Equal(
            $"film saved to {film.Path} (40x20, 2 picture(s) in 2 frame(s), 1.0 s)\n"
            + "files written:\n"
            + $"  {film.Path} - animated PNG\n"
            + $"no MP4: the ffmpeg set in Preferences ({missing}) does not exist - fix the path in "
            + "Preferences, Recording tab, or empty it to use the one on the PATH",
            text);
        Assert.True(File.Exists(film.Path));
        Assert.False(File.Exists(Mp4FromAnimatedPng.Mp4PathFor(film.Path)));
    }

    [Fact]
    public async Task AFilmThatWasNotWrittenIsNotHandedToFfmpeg()
    {
        var stopped = new ScreenRecordingOutcome { Path = Path.Combine(_folder, "never.png"), Frames = 0 };
        int asked = 0;
        var registry = Registry(stopped, () => { asked++; return new FfmpegChoice { Problem = "not asked" }; });

        var text = await StopOnBothSurfaces(registry);

        Assert.EndsWith("no MP4: the animated PNG was not written, so there is nothing to make it from", text, StringComparison.Ordinal);
        Assert.Equal(0, asked);
    }

    // ── RECORDSTOP with ffmpeg ──────────────────────────────────────

    /// <summary>
    /// The film's frames, in milliseconds. Uneven on purpose (B-frames lost these, the default
    /// time base rounded them), an odd size (yuv420p needs even), and one still longer than an
    /// APNG frame can hold, which the film stores as two frames.
    /// </summary>
    private static readonly int[] Durations = { 500, 1234, 70, 3000, 17, 70000 };

    [Fact]
    [Trait("Category", "Ffmpeg")]
    public async Task WithFfmpegTheMp4IsMadeAndEveryFrameKeepsItsDuration()
    {
        var ffmpeg = RequireFfmpeg();
        var ffprobe = RequireFfprobe(ffmpeg);
        var film = WriteFilm("with-ffmpeg.png", 101, 51, Durations);
        Assert.Equal(Durations.Length + 1, film.Frames);
        var registry = Registry(film, () => FfmpegLocator.FindOnThisMachine(null));

        var text = await StopOnBothSurfaces(registry);

        var mp4 = Mp4FromAnimatedPng.Mp4PathFor(film.Path);
        Assert.Equal(
            $"film saved to {film.Path} (101x51, {Durations.Length} picture(s) in {film.Frames} frame(s), 74.8 s)\n"
            + "files written:\n"
            + $"  {film.Path} - animated PNG\n"
            + $"  {mp4} - MP4 (H.264, yuv420p, 102x52)\n"
            + $"ffmpeg used: {ffmpeg.Path} (found on the PATH)",
            text);
        Assert.True(File.Exists(film.Path));

        var stream = Probe(ffprobe, "-v", "error", "-select_streams", "v:0",
            "-show_entries", "stream=codec_name,pix_fmt,width,height", "-of", "default=noprint_wrappers=1", mp4);
        Assert.Contains("codec_name=h264", stream, StringComparison.Ordinal);
        Assert.Contains("pix_fmt=yuv420p", stream, StringComparison.Ordinal);
        Assert.Contains("width=102", stream, StringComparison.Ordinal);
        Assert.Contains("height=52", stream, StringComparison.Ordinal);

        // Every packet's start and length, exactly as the film holds them. The 70 s still is two
        // frames in the film (65535 ms, then the rest) and stays two in the MP4.
        var packets = Probe(ffprobe, "-v", "error", "-select_streams", "v:0",
            "-show_entries", "packet=pts_time,duration_time", "-of", "csv=p=0", mp4)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var expected = new List<int>();
        for (int i = 0; i < Durations.Length; i++)
        {
            int left = Durations[i];
            while (left > AnimatedPngWriter.MaxFrameDurationMs)
            {
                expected.Add(AnimatedPngWriter.MaxFrameDurationMs);
                left -= AnimatedPngWriter.MaxFrameDurationMs;
            }
            expected.Add(left);
        }
        Assert.Equal(expected.Count, packets.Length);
        long start = 0;
        for (int i = 0; i < packets.Length; i++)
        {
            var parts = packets[i].Split(',');
            long pts = (long)Math.Round(double.Parse(parts[0], CultureInfo.InvariantCulture) * 1000);
            long duration = (long)Math.Round(double.Parse(parts[1], CultureInfo.InvariantCulture) * 1000);
            Assert.True(start == pts, $"frame {i} starts at {pts} ms in the MP4, {start} ms in the film");
            Assert.True(expected[i] == duration, $"frame {i} lasts {duration} ms in the MP4, {expected[i]} ms in the film");
            start += expected[i];
        }
    }

    [Fact]
    [Trait("Category", "Ffmpeg")]
    public async Task WhenFfmpegFailsItsErrorTextIsReportedAndThePngIsKept()
    {
        var ffmpeg = RequireFfmpeg();
        // A "film" ffmpeg cannot read: the reply must carry ffmpeg's own words, and the file stays.
        var broken = Path.Combine(_folder, "broken.png");
        File.WriteAllText(broken, "this is not a PNG");
        var stopped = new ScreenRecordingOutcome { Path = broken, Pictures = 1, Frames = 1, Width = 10, Height = 10 };
        var registry = Registry(stopped, () => FfmpegLocator.FindOnThisMachine(null));

        var text = await StopOnBothSurfaces(registry);

        var prefix = $"no MP4: ffmpeg ({ffmpeg.Path}) failed with exit code ";
        int at = text.IndexOf(prefix, StringComparison.Ordinal);
        Assert.True(at > 0, text);
        Assert.EndsWith(" - the animated PNG is kept", text, StringComparison.Ordinal);
        // ffmpeg said something of its own (7.1.1 says "Invalid PNG signature"; the words depend on
        // the version, so only that it said something is checked).
        var said = text.Substring(at + prefix.Length);
        Assert.DoesNotContain("(it wrote no error text)", said, StringComparison.Ordinal);
        Assert.True(said.Length > 40, said);
        // ffmpeg 7.1.1 leaves an empty MP4 behind when it fails; that is not a file written.
        Assert.True(File.Exists(broken));
        Assert.False(File.Exists(Mp4FromAnimatedPng.Mp4PathFor(broken)));
    }
}
