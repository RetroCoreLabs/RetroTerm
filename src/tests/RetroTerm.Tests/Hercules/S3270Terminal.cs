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
using System.Threading.Tasks;

namespace RetroTerm.Tests.Hercules
{
    /// <summary>
    /// s3270, the window-less build of wc3270, as the reference terminal: an outside 3270 that is
    /// RUN, never copied (clean-room rule). Driven with its stdin commands.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Started as a 3278 model 2 and with the "N:" host prefix (no TN3270E), so it asks the host
    /// for the same thing RetroTerm does.
    /// </para>
    /// <para>
    /// Each command answers with zero or more "data: " lines, a status line and "ok" or "error".
    /// Status field 1 is U when the keyboard is unlocked; fields 9 and 10 are the cursor row and
    /// column, counted from 0.
    /// </para>
    /// </remarks>
    internal sealed class S3270Terminal : ITk5Terminal
    {
        private readonly Process _process;

        // True when started as a 3279: only then does x3270 keep the colours the host sends.
        private readonly bool _colour;
        private bool _unlocked;

        private S3270Terminal(Process process, bool colour)
        {
            _process = process;
            _colour = colour;
        }

        public string Name => "s3270";

        public static Task<S3270Terminal> OpenAsync(Tk5Settings settings)
        {
            return OpenAsync(settings, "3278-2");
        }

        /// <summary>
        /// Starts s3270 as the given model and connects it. "3279-2" keeps colours, "3278-2" drops
        /// them (x3270 ctlr.c, every colour store is inside "if (mode3279)").
        /// </summary>
        public static async Task<S3270Terminal> OpenAsync(Tk5Settings settings, string model)
        {
            if (!File.Exists(settings.S3270))
            {
                throw new Xunit.Sdk.XunitException("s3270 not found at '" + settings.S3270 + "' (the s3270 entry in tk5-tests.json).");
            }

            var start = new ProcessStartInfo(settings.S3270, "-model " + model)
            {
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            Process process = Process.Start(start) ?? throw new Xunit.Sdk.XunitException("s3270 did not start");
            var terminal = new S3270Terminal(process, model.StartsWith("3279", StringComparison.Ordinal));
            await terminal.RunAsync("Connect(N:" + settings.Host + ":" + settings.Port.ToString(CultureInfo.InvariantCulture) + ")");
            return terminal;
        }

        public async Task TypeAsync(string text)
        {
            await RunAsync("String(\"" + text.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\")");
        }

        public async Task PressAsync(string key)
        {
            string k = key.ToUpperInvariant();
            if (k.StartsWith("PF", StringComparison.Ordinal))
            {
                await RunAsync("PF(" + k.Substring(2) + ")");
            }
            else if (k.StartsWith("PA", StringComparison.Ordinal))
            {
                await RunAsync("PA(" + k.Substring(2) + ")");
            }
            else
            {
                switch (k)
                {
                    case "ENTER": await RunAsync("Enter()"); break;
                    case "CLEAR": await RunAsync("Clear()"); break;
                    case "RESET": await RunAsync("Reset()"); break;
                    case "TAB": await RunAsync("Tab()"); break;
                    case "BACKTAB": await RunAsync("BackTab()"); break;
                    case "HOME": await RunAsync("Home()"); break;
                    case "ERASEEOF": await RunAsync("EraseEOF()"); break;
                    case "NEWLINE": await RunAsync("Newline()"); break;
                    default: throw new Xunit.Sdk.XunitException("S3270Terminal has no mapping for key " + key);
                }
            }
        }

        public async Task<Tk5Screen> WaitUntilAsync(Tk5ScreenTest done, string what, int timeoutMs)
        {
            var clock = Stopwatch.StartNew();
            Tk5Screen screen = await ReadAsync();
            while (!(done(screen) && _unlocked))
            {
                if (clock.ElapsedMilliseconds > timeoutMs)
                {
                    throw new Xunit.Sdk.XunitException("s3270: no " + what + " with the keyboard unlocked after " + timeoutMs + " ms. Screen:\n" + screen.Text());
                }

                await Task.Delay(50);
                screen = await ReadAsync();
            }

            return screen;
        }

        // What wc3270 draws on line 25 for the state s3270 reports, written from wc3270\screen.c
        // (draw_oia and status_connect) and NOT from RetroIO's StatusLine3270, so the comparison is
        // not RetroTerm against itself. rmargin is 79 on an 80-column screen: "4" at 0, "A" at 1,
        // the box at 2 (blank in 3270 mode), the message from 7, the cursor "%03d/%03d" (from 1) at
        // 72. A locked keyboard (field 1 "L") does not say which message; the steps only compare
        // unlocked screens, so a lock here is reported rather than guessed.
        private static string Wc3270Line25(string[] status)
        {
            char[] line = new string(' ', 80).ToCharArray();
            line[0] = '4';
            line[1] = 'A';
            bool in3270 = status[3].StartsWith("C(", StringComparison.Ordinal) && status[4] == "I";
            line[2] = in3270 ? ' ' : '?';
            string message = !status[3].StartsWith("C(", StringComparison.Ordinal) ? "X Not Connected"
                : status[0] == "L" ? "(s3270 reports a lock but not its reason)" : string.Empty;
            message.CopyTo(0, line, 7, Math.Min(message.Length, 35));
            if (in3270)
            {
                string cursor = (int.Parse(status[8], CultureInfo.InvariantCulture) + 1).ToString("000", CultureInfo.InvariantCulture) + "/" +
                    (int.Parse(status[9], CultureInfo.InvariantCulture) + 1).ToString("000", CultureInfo.InvariantCulture);
                cursor.CopyTo(0, line, 72, cursor.Length);
            }

            return new string(line);
        }

        public async Task<Tk5Screen> ReadAsync()
        {
            Reply text = await RunAsync("Ascii()");
            Reply buffer = await RunAsync("ReadBuffer(Ascii)");

            var addresses = new List<int>();
            var attributes = new List<byte>();
            var highlights = new byte[Tk5Screen.Rows * Tk5Screen.Columns];

            // The running character highlight: SA(...) is printed only where it changes, and is
            // never reset at a field (task.c, current_gr, starting at 0).
            byte highlight = 0;
            var colours = new byte[Tk5Screen.Rows * Tk5Screen.Columns];
            byte colour = 0;
            int address = 0;
            for (int i = 0; i < buffer.Data.Count; i++)
            {
                string[] tokens = buffer.Data[i].Split(' ', StringSplitOptions.RemoveEmptyEntries);
                for (int t = 0; t < tokens.Length; t++)
                {
                    string token = tokens[t];

                    // SA(...) is a character-attribute change (colour, highlight) printed in front
                    // of the character it applies to; it takes no buffer position (x3270
                    // Common\task.c, the ReadBuffer loop, read 22 November 2026).
                    if (token.StartsWith("SA(", StringComparison.Ordinal))
                    {
                        highlight = HighlightIn(token, highlight);
                        colour = ValueIn(token, "42=", colour);
                        continue;
                    }

                    if (address < highlights.Length)
                    {
                        highlights[address] = highlight;
                        colours[address] = colour;
                    }

                    if (token.StartsWith("SF(c0=", StringComparison.Ordinal))
                    {
                        addresses.Add(address);
                        attributes.Add(byte.Parse(token.AsSpan(6, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture));

                        // A field attribute's own highlight, printed only when it is not 0.
                        if (address < highlights.Length)
                        {
                            highlights[address] = HighlightIn(token, 0);
                            colours[address] = ValueIn(token, "42=", 0);
                        }
                    }

                    address++;
                }
            }

            string[] status = buffer.Status.Split(' ');
            _unlocked = status[0] == "U";
            int row = int.Parse(status[8], CultureInfo.InvariantCulture);
            int column = int.Parse(status[9], CultureInfo.InvariantCulture);
            string line25 = string.Join(' ', status, 0, 8);
            return new Tk5Screen(text.Data.ToArray(), row, column, addresses, attributes, line25, Wc3270Line25(status), highlights, _colour ? colours : null);
        }

        // A key=value pair in an SF(...) or SA(...) token, for example 42= (XA_FOREGROUND), or the
        // given value when the token has none.
        private static byte ValueIn(string token, string key, byte otherwise)
        {
            int at = token.IndexOf(key, StringComparison.Ordinal);
            return at < 0 ? otherwise : byte.Parse(token.AsSpan(at + 3, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        }

        // The 41= (XA_HIGHLIGHTING) value in an SF(...) or SA(...) token, as its low four bits, or
        // the given value when the token has none.
        private static byte HighlightIn(string token, byte otherwise)
        {
            int at = token.IndexOf("41=", StringComparison.Ordinal);
            if (at < 0)
            {
                return otherwise;
            }

            return (byte)(byte.Parse(token.AsSpan(at + 3, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture) & 0x0F);
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                await _process.StandardInput.WriteLineAsync("Quit()");
                _process.StandardInput.Close();
                if (!_process.WaitForExit(5000))
                {
                    // Ours: started above, killed by its own handle, never by name.
                    _process.Kill();
                }
            }
            finally
            {
                _process.Dispose();
            }
        }

        private async Task<Reply> RunAsync(string command)
        {
            await _process.StandardInput.WriteLineAsync(command);
            await _process.StandardInput.FlushAsync();
            var reply = new Reply();
            string? previous = null;
            while (true)
            {
                string? line = await _process.StandardOutput.ReadLineAsync();
                if (line == null)
                {
                    throw new Xunit.Sdk.XunitException("s3270 ended during " + command);
                }

                if (line == "ok" || line == "error")
                {
                    reply.Status = previous ?? string.Empty;
                    if (line == "error")
                    {
                        throw new Xunit.Sdk.XunitException("s3270 refused " + command + ": " + string.Join(" | ", reply.Data));
                    }

                    return reply;
                }

                if (line.StartsWith("data: ", StringComparison.Ordinal))
                {
                    reply.Data.Add(line.Substring(6));
                }
                else if (line == "data:")
                {
                    reply.Data.Add(string.Empty);
                }

                previous = line;
            }
        }

        private sealed class Reply
        {
            public List<string> Data { get; } = new List<string>();

            public string Status { get; set; } = string.Empty;
        }
    }
}
