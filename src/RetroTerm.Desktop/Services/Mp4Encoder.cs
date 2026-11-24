//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs/RetroTerm
// RetroTerm — Connecting today's machines to yesterday's systems
//

using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using RetroTerm.Core.Recording;

namespace RetroTerm.Desktop.Services;

/// <summary>
/// Runs ffmpeg on a finished animated PNG film to make the MP4 beside it.
/// </summary>
/// <remarks>
/// <para><b>What runs, and why</b></para>
/// The arguments, the output name and the measurements behind them are in
/// <see cref="Mp4FromAnimatedPng"/> in Core; this class only starts the process, waits for it
/// and reads its error text. It lives here because RetroTerm.Core targets netstandard2.1, which
/// has neither <see cref="Process.WaitForExitAsync(CancellationToken)"/> nor a way to stop a
/// process together with the ones it started.
/// <para><b>A separate process, after the PNG is closed</b></para>
/// RECORDSTOP calls this once the film's file is finished. ffmpeg only reads that file; when it
/// fails, the PNG is untouched and its error text is the answer.
/// <para><b>Stopping it</b></para>
/// A cancelled RECORDSTOP stops the ffmpeg this started by its own process handle, together with
/// anything it started: a package manager's launcher (Chocolatey's, on the development machine)
/// runs the real ffmpeg as a child, and stopping only the launcher would leave that running.
/// </remarks>
public static class Mp4Encoder
{
    /// <summary>
    /// Runs ffmpeg on a finished film.
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
    /// Stops ffmpeg (and anything it started) and gives up.
    /// </param>
    /// <returns>
    /// The MP4, or ffmpeg's reason for not making one.
    /// </returns>
    public static async Task<Mp4Outcome> MakeAsync(string ffmpegPath, string filmPath, int width, int height,
        CancellationToken cancellationToken)
    {
        var mp4Path = Mp4FromAnimatedPng.Mp4PathFor(filmPath);
        var info = new ProcessStartInfo(ffmpegPath)
        {
            UseShellExecute = false,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            CreateNoWindow = true
        };
        var args = Mp4FromAnimatedPng.Arguments(filmPath, mp4Path);
        for (int i = 0; i < args.Length; i++)
        {
            info.ArgumentList.Add(args[i]);
        }

        Process? process;
        try
        {
            process = Process.Start(info);
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            return new Mp4Outcome { Problem = $"ffmpeg ({ffmpegPath}) could not be started: {ex.Message}" };
        }
        if (process == null)
        {
            return new Mp4Outcome { Problem = $"ffmpeg ({ffmpegPath}) could not be started" };
        }

        using (process)
        {
            // Both pipes are read to the end while ffmpeg runs, so a full pipe can never stop it.
            var errorText = process.StandardError.ReadToEndAsync(CancellationToken.None);
            var outputText = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
            try
            {
                await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch (InvalidOperationException)
                {
                    // It had already exited between the cancel and the kill; nothing is left.
                }
                throw;
            }

            var error = (await errorText.ConfigureAwait(false)).Trim();
            await outputText.ConfigureAwait(false);

            if (process.ExitCode != 0 || !File.Exists(mp4Path))
            {
                if (error.Length > Mp4FromAnimatedPng.MaxErrorText)
                {
                    error = "..." + error.Substring(error.Length - Mp4FromAnimatedPng.MaxErrorText);
                }
                var said = error.Length > 0 ? error : "(it wrote no error text)";

                // A failed ffmpeg can leave the MP4 it opened behind, empty (7.1.1 does: "Nothing
                // was written into output file"). That is not a film, so it does not stay to be
                // mistaken for one.
                if (File.Exists(mp4Path))
                {
                    try
                    {
                        File.Delete(mp4Path);
                    }
                    catch (IOException ex)
                    {
                        said += $" (and the unfinished {mp4Path} it left could not be deleted: {ex.Message})";
                    }
                    catch (UnauthorizedAccessException ex)
                    {
                        said += $" (and the unfinished {mp4Path} it left could not be deleted: {ex.Message})";
                    }
                }
                return new Mp4Outcome { Problem = $"ffmpeg ({ffmpegPath}) failed with exit code {process.ExitCode}: {said}" };
            }

            return new Mp4Outcome { Path = mp4Path, Width = (width + 1) & ~1, Height = (height + 1) & ~1 };
        }
    }
}
