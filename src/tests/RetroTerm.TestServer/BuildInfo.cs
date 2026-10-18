//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs/RetroTerm
// RetroTerm — Connecting today's machines to yesterday's systems
//

using System;
using System.IO;
using System.Reflection;

namespace RetroTerm.TestServer;

/// <summary>
/// Provides build information including build date/time
/// </summary>
public static class BuildInfo
{
    public static string Version => "1.0";

    /// <summary>
    /// Gets the build date/time based on the executable's last write time
    /// </summary>
    public static DateTime BuildDateTime
    {
        get
        {
            // For single-file apps, use the EXE path from BaseDirectory
            var exePath = Path.Combine(AppContext.BaseDirectory, "RetroTerm.TestServer.exe");
            if (File.Exists(exePath))
            {
                return new FileInfo(exePath).LastWriteTime;
            }

            // Fallback for development (running from bin folder)
            // Avoid Assembly.Location to be compatible with single-file deployments
            var baseDir = AppContext.BaseDirectory;
            var probablePath = Path.Combine(baseDir, "RetroTerm.TestServer.dll");
            if (File.Exists(probablePath))
            {
                return new FileInfo(probablePath).LastWriteTime;
            }

            // Last resort: just return current time
            return DateTime.Now;
        }
    }

    /// <summary>
    /// Gets formatted build date/time string
    /// </summary>
    public static string BuildDateTimeString => BuildDateTime.ToString("yyyy-MM-dd HH:mm:ss");
}

