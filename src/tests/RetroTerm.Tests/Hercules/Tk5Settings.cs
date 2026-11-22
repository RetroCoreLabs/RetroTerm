//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs/RetroTerm
// RetroTerm — Connecting today's machines to yesterday's systems
//

using System;
using System.IO;
using System.Text.Json;

namespace RetroTerm.Tests.Hercules
{
    /// <summary>
    /// Where the live TK5 host is and who to log on as, read from a file in the user's profile so
    /// that no password is ever in this repository.
    /// </summary>
    /// <remarks>
    /// The file is <c>%USERPROFILE%\.retroterm\tk5-tests.json</c>:
    /// <code>
    /// { "host": "localhost", "port": 3270, "user": "HERC03", "password": "...",
    ///   "compareUser": "HERC04", "comparePassword": "...", "s3270": "path to s3270.exe" }
    /// </code>
    /// Two users so that each test has its own and a session one test leaves hanging never
    /// refuses the other's logon. The comparison test logs on as the compare user in BOTH
    /// terminals: another user has other data sets, and LISTCAT would differ for that alone.
    /// A missing file fails the test with this text; it never skips (a test that quietly does
    /// nothing reads as a pass).
    /// </remarks>
    internal sealed class Tk5Settings
    {
        public string Host { get; private set; } = "localhost";

        public int Port { get; private set; } = 3270;

        public string User { get; private set; } = string.Empty;

        public string Password { get; private set; } = string.Empty;

        public string CompareUser { get; private set; } = string.Empty;

        public string ComparePassword { get; private set; } = string.Empty;

        public string S3270 { get; private set; } = string.Empty;

        /// <summary>
        /// The same settings logged on as the compare user.
        /// </summary>
        public Tk5Settings AsCompareUser()
        {
            return new Tk5Settings { Host = Host, Port = Port, User = CompareUser, Password = ComparePassword, S3270 = S3270 };
        }

        public static Tk5Settings Load()
        {
            string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".retroterm", "tk5-tests.json");
            if (!File.Exists(path))
            {
                throw new Xunit.Sdk.XunitException("The TK5 tests need " + path +
                    " with host, port, user, password, compareUser, comparePassword and s3270 (the path to s3270.exe). See Tk5Settings.cs.");
            }

            using JsonDocument json = JsonDocument.Parse(File.ReadAllText(path));
            JsonElement root = json.RootElement;
            var settings = new Tk5Settings
            {
                Host = root.GetProperty("host").GetString() ?? "localhost",
                Port = root.GetProperty("port").GetInt32(),
                User = root.GetProperty("user").GetString() ?? string.Empty,
                Password = root.GetProperty("password").GetString() ?? string.Empty,
                CompareUser = root.GetProperty("compareUser").GetString() ?? string.Empty,
                ComparePassword = root.GetProperty("comparePassword").GetString() ?? string.Empty,
                S3270 = root.TryGetProperty("s3270", out JsonElement s) ? s.GetString() ?? string.Empty : string.Empty,
            };
            return settings;
        }
    }
}
