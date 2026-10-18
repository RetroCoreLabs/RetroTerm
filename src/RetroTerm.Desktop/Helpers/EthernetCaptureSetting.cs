//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs/RetroTerm
// RetroTerm — Connecting today's machines to yesterday's systems
//

using System;
using System.IO;
using RetroTerm.Core.Protocols.WebSocket.Gateway;
using RetroTerm.Desktop.Themes;

namespace RetroTerm.Desktop.Helpers;

/// <summary>
/// Turns the Preferences capture setting into a running (or stopped) capture on the gateway.
/// Called at start-up and whenever the setting changes, so both places agree on the file name
/// and on what "enabled" means.
/// </summary>
public static class EthernetCaptureSetting
{
    /// <summary>The file name the user chose, or the default when none was.</summary>
    public static string BaseFile(ThemeManager tm)
    {
        if (!string.IsNullOrWhiteSpace(tm.EthernetCaptureFile))
        {
            return tm.EthernetCaptureFile.Trim();
        }

        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                            "RetroTerm", "ethernet.pcapng");
    }

    /// <summary><paramref name="baseFile"/> with the current date and time added before the
    /// extension: ethernet.pcapng becomes ethernet-2026-10-10_150210.pcapng.</summary>
    public static string Timestamped(string baseFile)
    {
        string dir = Path.GetDirectoryName(baseFile) ?? string.Empty;
        string name = Path.GetFileNameWithoutExtension(baseFile);
        string ext = Path.GetExtension(baseFile);
        if (ext.Length == 0) ext = ".pcapng";
        return Path.Combine(dir, $"{name}-{DateTime.Now:yyyy-MM-dd_HHmmss}{ext}");
    }

    /// <summary>Start a new capture file now. Returns null, or why it could not start.</summary>
    public static string? StartNew(GatewayListener listener, ThemeManager tm)
    {
        string path = Timestamped(BaseFile(tm));
        try
        {
            string? dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
        {
            return ex.Message;
        }

        return listener.StartEthernetCapture(path);
    }

    /// <summary>Make the gateway match the setting: capture on and none running starts one;
    /// capture off stops any that is running.</summary>
    public static void Apply(GatewayListener? listener, ThemeManager tm)
    {
        if (listener == null) return;

        if (!tm.EthernetCaptureEnabled)
        {
            listener.StopEthernetCapture();
            return;
        }

        if (listener.EthernetCapturePath == null)
        {
            StartNew(listener, tm);
        }
    }
}
