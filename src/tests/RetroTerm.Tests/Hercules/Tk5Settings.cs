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
    ///   "compareUser": "HERC04", "comparePassword": "...", "s3270": "path to s3270.exe",
    ///   "jrpUser": "HERC01", "jrpPassword": "..." }
    /// The JRP user is needed only by the 3287 printer test (Tk5PrinterTests).
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
        /// Gets the user that logs on to the JRP console (logon applid=cjrp) to start the 3287
        /// printer PRT4. Only users with read access to FACILITY JRPAUTH may: as shipped, HERC01
        /// and HERC02 (MVS TK4- Users Manual, "Operating JRP"). Empty when the file has none.
        /// </summary>
        public string JrpUser { get; private set; } = string.Empty;

        /// <summary>
        /// Gets the password of <see cref="JrpUser"/>.
        /// </summary>
        public string JrpPassword { get; private set; } = string.Empty;

        /// <summary>
        /// The same settings logged on as the compare user.
        /// </summary>
        public Tk5Settings AsCompareUser()
        {
            return new Tk5Settings { Host = Host, Port = Port, User = CompareUser, Password = ComparePassword, S3270 = S3270 };
        }

        /// <summary>
        /// The same settings pointed at another host and port: a relay that records the bytes
        /// (Tk5WireTap) in front of the real host.
        /// </summary>
        /// <param name="host">
        /// The host to connect to instead.
        /// </param>
        /// <param name="port">
        /// Its port.
        /// </param>
        /// <returns>
        /// The settings.
        /// </returns>
        public Tk5Settings Through(string host, int port)
        {
            return new Tk5Settings
            {
                Host = host,
                Port = port,
                User = User,
                Password = Password,
                CompareUser = CompareUser,
                ComparePassword = ComparePassword,
                S3270 = S3270,
                JrpUser = JrpUser,
                JrpPassword = JrpPassword,
            };
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
                JrpUser = root.TryGetProperty("jrpUser", out JsonElement ju) ? ju.GetString() ?? string.Empty : string.Empty,
                JrpPassword = root.TryGetProperty("jrpPassword", out JsonElement jp) ? jp.GetString() ?? string.Empty : string.Empty,
            };
            return settings;
        }
    }
}
