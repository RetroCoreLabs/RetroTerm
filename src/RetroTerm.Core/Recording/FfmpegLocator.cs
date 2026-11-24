//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs/RetroTerm
// RetroTerm — Connecting today's machines to yesterday's systems
//

using System;
using System.IO;

namespace RetroTerm.Core.Recording;

/// <summary>
/// Which ffmpeg RECORDSTOP uses to make an MP4, or why there is none.
/// </summary>
public sealed class FfmpegChoice
{
    /// <summary>
    /// Full path of the ffmpeg program, or null when none can be used.
    /// </summary>
    public string? Path { get; set; }

    /// <summary>
    /// Where it came from, in words ("set in Preferences", "found on the PATH"), or null.
    /// </summary>
    public string? Source { get; set; }

    /// <summary>
    /// Why no ffmpeg can be used, or null when <see cref="Path"/> is set.
    /// </summary>
    public string? Problem { get; set; }
}

/// <summary>
/// Finds ffmpeg: the program set in Preferences when one is set, otherwise the first one on the
/// system PATH.
/// </summary>
/// <remarks>
/// <para><b>Why Preferences wins and does not fall back</b></para>
/// A path typed into Preferences is a choice. If it names a file that is not there, the person
/// meant that ffmpeg and not whichever one the PATH happens to hold, so the answer is "the one you
/// set is missing" rather than quietly using another. Leave the field empty to use the PATH.
/// <para><b>No environment variable of our own</b></para>
/// CLAUDE.md: app settings live in Preferences only. The system PATH is read because that is
/// where an installed program is found on every system, not as a RetroTerm setting.
/// <para><b>Nothing is shipped</b></para>
/// RetroTerm carries no ffmpeg; it uses the one the person installed, or makes no MP4.
/// </remarks>
public static class FfmpegLocator
{
    /// <summary>
    /// The program's file name on this system: ffmpeg.exe on Windows, ffmpeg elsewhere.
    /// </summary>
    public static string ProgramName =>
        System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows)
            ? "ffmpeg.exe" : "ffmpeg";

    /// <summary>
    /// Finds the ffmpeg to use on this machine: <see cref="Find"/> over the Preferences path and
    /// the system PATH as the process sees it. What the desktop and the ffmpeg tests both call,
    /// so a test finds ffmpeg exactly the way the app does.
    /// </summary>
    /// <param name="preferencesPath">
    /// The path set in Preferences, Recording tab; null or blank when none is set.
    /// </param>
    /// <returns>
    /// The program and where it came from, or why there is none.
    /// </returns>
    /// <remarks>
    /// The one read of the environment in the shipping code, and it is the system PATH, not a
    /// RetroTerm setting: it is where every system keeps the list of folders installed programs
    /// are found in. NoEnvironmentVariableConfigurationTests allows exactly this line and no other.
    /// </remarks>
    public static FfmpegChoice FindOnThisMachine(string? preferencesPath)
        => Find(preferencesPath, Environment.GetEnvironmentVariable(SystemPathVariable));

    /// <summary>
    /// The name of the system's program search list.
    /// </summary>
    public const string SystemPathVariable = "PATH";

    /// <summary>
    /// Finds the ffmpeg to use.
    /// </summary>
    /// <param name="preferencesPath">
    /// The path set in Preferences, Recording tab; null or blank when none is set.
    /// </param>
    /// <param name="systemPath">
    /// The system PATH, as the operating system holds it (folders separated by
    /// <see cref="System.IO.Path.PathSeparator"/>); null when there is none.
    /// </param>
    /// <returns>
    /// The program and where it came from, or why there is none.
    /// </returns>
    public static FfmpegChoice Find(string? preferencesPath, string? systemPath)
    {
        if (!string.IsNullOrWhiteSpace(preferencesPath))
        {
            var set = preferencesPath.Trim();
            if (File.Exists(set))
            {
                return new FfmpegChoice { Path = System.IO.Path.GetFullPath(set), Source = "set in Preferences" };
            }
            return new FfmpegChoice
            {
                Problem = $"the ffmpeg set in Preferences ({set}) does not exist - fix the path in "
                    + "Preferences, Recording tab, or empty it to use the one on the PATH"
            };
        }

        if (!string.IsNullOrEmpty(systemPath))
        {
            var folders = systemPath.Split(System.IO.Path.PathSeparator);
            for (int i = 0; i < folders.Length; i++)
            {
                var folder = folders[i].Trim().Trim('"');
                if (folder.Length == 0) continue;
                string candidate;
                try
                {
                    candidate = System.IO.Path.Combine(folder, ProgramName);
                }
                catch (ArgumentException)
                {
                    // A PATH entry holding characters a path cannot hold names no folder; the
                    // next entry is looked at.
                    continue;
                }
                if (File.Exists(candidate))
                {
                    return new FfmpegChoice { Path = System.IO.Path.GetFullPath(candidate), Source = "found on the PATH" };
                }
            }
        }

        return new FfmpegChoice
        {
            Problem = $"no ffmpeg was found: none is set in Preferences, Recording tab, and there is no "
                + $"{ProgramName} on the PATH"
        };
    }
}
