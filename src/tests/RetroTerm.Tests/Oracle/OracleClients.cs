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

using RetroIO.Protocols.Tn3270;

using RetroTerm.Core.Protocols;
using RetroTerm.Core.Session;
using RetroTerm.Core.Terminal.Input;
using RetroTerm.Ibm3270;

namespace RetroTerm.Tests.Oracle
{
    /// <summary>
    /// A 3270 client the replay drives: RetroTerm, or s3270 as the reference.
    /// </summary>
    internal interface IOracleClient : IAsyncDisposable
    {
        string Name { get; }

        /// <summary>
        /// Starts connecting to the replay host. The task may finish only once the replay has
        /// sent the negotiation, so it is awaited after the first record, not at once.
        /// </summary>
        Task ConnectAsync(int port);

        /// <summary>
        /// Waits until everything the host has sent so far is on the screen. Called after the
        /// replay host's timing mark came back, with the number of records it has sent.
        /// </summary>
        Task SettleAsync(int recordsSent);

        Task TypeAsync(string text);

        Task PressAsync(string key);

        Task<OracleScreen> ReadAsync();

        /// <summary>
        /// Waits until the last key that sent an AID has finished on the client's side. Called
        /// only after a host write that restores the keyboard.
        /// </summary>
        Task AidFinishedAsync();
    }

    /// <summary>
    /// RetroTerm's own 3270: the same session, Tn3270Connection and emulator the app uses.
    /// </summary>
    internal sealed class RetroTermOracleClient : IOracleClient
    {
        private readonly Ibm3270Emulator _emulator;
        private readonly TerminalSession _session;
        private readonly int _model;
        private int _recordsReceived;

        public RetroTermOracleClient(int model)
        {
            _model = model;
            _emulator = new Ibm3270Emulator(model);
            _session = new TerminalSession(_emulator, "replay");
        }

        public string Name => "RetroTerm";

        public Task ConnectAsync(int port)
        {
            IConnection connection = ConnectionFactory.CreateConnection(new ConnectionFactory.ConnectionParameters
            {
                Protocol = ConnectionFactory.ProtocolType.TN3270,
                Host = "127.0.0.1",
                Port = port,
                EmulatorType = "IBM-3278-" + _model.ToString(CultureInfo.InvariantCulture),
            });
            connection.DataReceived += _ => Interlocked.Increment(ref _recordsReceived);
            return _session.ConnectAsync(connection, Xunit.TestContext.Current.CancellationToken);
        }

        public async Task SettleAsync(int recordsSent)
        {
            // The connection hands each record to the session after the telnet layer has
            // answered the timing mark (Tn3270Connection raises its events after Feed), so the
            // count is awaited first, then the session pump is flushed.
            var clock = Stopwatch.StartNew();
            while (Volatile.Read(ref _recordsReceived) < recordsSent)
            {
                if (clock.ElapsedMilliseconds > HostStreamReplay.TimeoutMs)
                {
                    throw new Xunit.Sdk.XunitException("RetroTerm handed up " + _recordsReceived + " records of the " + recordsSent + " sent.");
                }

                await Task.Delay(5);
            }

            await _session.FlushAsync();
        }

        public async Task TypeAsync(string text)
        {
            await _session.SendInputAsync(text);
            await _session.FlushAsync();
        }

        public async Task PressAsync(string key)
        {
            NamedKeyResult result = await _session.RunOnSessionThreadAsync(() => _emulator.PressNamedKey(key, out _));
            if (result != NamedKeyResult.Pressed)
            {
                throw new Xunit.Sdk.XunitException("RetroTerm did not press " + key + ": " + result);
            }

            await _session.FlushAsync();
        }

        public Task AidFinishedAsync() => Task.CompletedTask;

        public async Task<OracleScreen> ReadAsync()
        {
            ScreenSnapshot snapshot = await _session.ReadScreenAsync(stripTrailingBlanks: false);
            return await _session.RunOnSessionThreadAsync(() =>
            {
                Display3270 display = _emulator.Display;
                int size = display.Size;
                var codes = new byte[size];
                var fields = new bool[size];
                var ge = new bool[size];
                var highlights = new byte[size];
                for (int a = 0; a < size; a++)
                {
                    codes[a] = display.GetByte(a);
                    fields[a] = display.IsFieldAttribute(a);
                    ge[a] = !fields[a] && display.IsGraphicEscape(a);
                    highlights[a] = (byte)((fields[a] ? display.GetFieldHighlight(a) : display.GetCharacterHighlight(a)) & 0x0F);
                }

                return new OracleScreen(display.Rows, display.Columns, snapshot.Text.Split('\n'), display.CursorAddress,
                    display.Lock != KeyboardLock.None, codes, fields, ge, highlights);
            });
        }

        public async ValueTask DisposeAsync()
        {
            await _session.DisconnectAsync();
            _session.Dispose();
        }
    }

    /// <summary>
    /// s3270, the window-less wc3270, as the reference: an outside 3270 that is RUN, never
    /// copied (the clean-room rule). Started as a 3278 of the recording's model, with "N:" (plain
    /// TN3270, no TN3270E) as the recording client was.
    /// </summary>
    /// <remarks>
    /// <para>
    /// unlockDelay is turned off. With it on, x3270 holds the keyboard locked for 350 ms after a
    /// host write that unlocks it, a pause for a person's eyes that is not part of the data
    /// stream; RetroTerm has nothing like it, and the replay reads the screen at once.
    /// </para>
    /// <para>
    /// The process is ours: started here and ended by its own handle in DisposeAsync, never by
    /// name.
    /// </para>
    /// </remarks>
    internal sealed class S3270OracleClient : IOracleClient
    {
        private readonly Process _process;
        private readonly SemaphoreSlim _one = new SemaphoreSlim(1);
        private Task? _connect;

        // An AID key still waiting for its "ok". s3270 answers Enter(), Clear(), PF() and PA()
        // only once the host has written back and the keyboard is unlocked, and the replay host
        // sends that write only after the driver goes on, so the driver must not wait here
        // (found 23 November 2026: waiting deadlocked the first run). It is awaited in
        // AidFinishedAsync, after the host write that restores the keyboard.
        private Task? _aid;

        private S3270OracleClient(Process process)
        {
            _process = process;
        }

        public string Name => "s3270";

        /// <summary>
        /// Gets the process id, for the report and for cleaning up.
        /// </summary>
        public int ProcessId => _process.Id;

        public static S3270OracleClient Start(int model)
        {
            string exe = S3270Path();
            var start = new ProcessStartInfo(exe)
            {
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            start.ArgumentList.Add("-model");
            start.ArgumentList.Add("3278-" + model.ToString(CultureInfo.InvariantCulture));
            start.ArgumentList.Add("-xrm");
            start.ArgumentList.Add("s3270.unlockDelay: false");
            Process process = Process.Start(start) ?? throw new Xunit.Sdk.XunitException("s3270 did not start: " + exe);
            return new S3270OracleClient(process);
        }

        /// <summary>
        /// Where s3270.exe is: the "s3270" entry of %USERPROFILE%\.retroterm\tk5-tests.json, the
        /// same file the live TK5 tests read. Fails, naming what is missing, when it is not there;
        /// it never skips (a test that quietly does nothing reads as a pass).
        /// </summary>
        /// <returns>
        /// The full path.
        /// </returns>
        public static string S3270Path()
        {
            string json = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".retroterm", "tk5-tests.json");
            const string Need = " The offline 3270 oracle tests (Category Oracle) run s3270, the window-less wc3270 from x3270.org; " +
                "put its path in the \"s3270\" entry of that file, or leave Category=Oracle out of the run.";
            if (!File.Exists(json))
            {
                throw new Xunit.Sdk.XunitException("s3270 is needed and " + json + " does not exist." + Need);
            }

            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(json));
            if (!document.RootElement.TryGetProperty("s3270", out JsonElement entry) || string.IsNullOrEmpty(entry.GetString()))
            {
                throw new Xunit.Sdk.XunitException("s3270 is needed and " + json + " has no \"s3270\" entry." + Need);
            }

            string exe = entry.GetString()!;
            if (!File.Exists(exe))
            {
                throw new Xunit.Sdk.XunitException("s3270 is needed and is not at " + exe + " (the \"s3270\" entry of " + json + ")." + Need);
            }

            return exe;
        }

        public Task ConnectAsync(int port)
        {
            _connect = RunAsync("Connect(N:127.0.0.1:" + port.ToString(CultureInfo.InvariantCulture) + ")");
            return Task.CompletedTask;
        }

        public async Task SettleAsync(int recordsSent)
        {
            // s3270 answered the timing mark after it had processed every byte before it, all on
            // one thread, so nothing is left to wait for, except the Connect command still
            // waiting for its answer the first time.
            if (_connect != null)
            {
                await _connect;
                _connect = null;
            }
        }

        public Task TypeAsync(string text) => RunAsync("String(\"" + text.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\")");

        public Task PressAsync(string key)
        {
            string k = key.ToUpperInvariant();
            if (k.StartsWith("PF", StringComparison.Ordinal))
            {
                return StartAid("PF(" + k.Substring(2) + ")");
            }

            if (k.StartsWith("PA", StringComparison.Ordinal))
            {
                return StartAid("PA(" + k.Substring(2) + ")");
            }

            switch (k)
            {
                case "ENTER": return StartAid("Enter()");
                case "CLEAR": return StartAid("Clear()");
                case "RESET": return RunAsync("Reset()");
                case "TAB": return RunAsync("Tab()");
                case "BACKTAB": return RunAsync("BackTab()");
                case "HOME": return RunAsync("Home()");
                case "ERASEEOF": return RunAsync("EraseEOF()");
                case "NEWLINE": return RunAsync("Newline()");
                default: throw new Xunit.Sdk.XunitException("S3270OracleClient has no mapping for key " + key);
            }
        }

        public async Task AidFinishedAsync()
        {
            if (_aid != null)
            {
                Task aid = _aid;
                _aid = null;
                await aid;
            }
        }

        // Sends an AID command without waiting for its answer (see _aid). The command has been
        // written to s3270's input when this returns, so the order of commands is kept.
        private async Task StartAid(string command)
        {
            await AidFinishedAsync();
            _aid = RunAsync(command);
            await Task.Yield();
        }

        public async Task<OracleScreen> ReadAsync()
        {
            List<string> text = (await RunAsync("Ascii()")).Data;
            Reply buffer = await RunAsync("ReadBuffer(Ebcdic)");
            string[] status = buffer.Status.Split(' ');
            int rows = int.Parse(status[6], CultureInfo.InvariantCulture);
            int columns = int.Parse(status[7], CultureInfo.InvariantCulture);
            int size = rows * columns;
            var codes = new byte[size];
            var fields = new bool[size];
            var ge = new bool[size];
            var highlights = new byte[size];

            // The running character highlight: SA(...) is printed only where it changes and is
            // never reset at a field (x3270 Common\task.c, the ReadBuffer loop, read 22 November
            // 2026 for Hercules\S3270Terminal).
            byte highlight = 0;
            int address = 0;
            for (int i = 0; i < buffer.Data.Count; i++)
            {
                string[] tokens = buffer.Data[i].Split(' ', StringSplitOptions.RemoveEmptyEntries);
                for (int t = 0; t < tokens.Length; t++)
                {
                    string token = tokens[t];
                    if (token.StartsWith("SA(", StringComparison.Ordinal))
                    {
                        highlight = HighlightIn(token, highlight);
                        continue;
                    }

                    if (address >= size)
                    {
                        throw new Xunit.Sdk.XunitException("s3270 ReadBuffer gave more than " + size + " positions.");
                    }

                    if (token.StartsWith("SF(", StringComparison.Ordinal))
                    {
                        fields[address] = true;
                        codes[address] = ValueIn(token, "c0=", 0);
                        highlights[address] = HighlightIn(token, 0);
                    }
                    else if (token.StartsWith("GE(", StringComparison.Ordinal))
                    {
                        ge[address] = true;
                        codes[address] = byte.Parse(token.AsSpan(3, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                        highlights[address] = highlight;
                    }
                    else if (token.Length == 2)
                    {
                        codes[address] = byte.Parse(token, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                        highlights[address] = highlight;
                    }
                    else
                    {
                        // A token this reader does not know is refused by name, never guessed at.
                        throw new Xunit.Sdk.XunitException("s3270 ReadBuffer(Ebcdic) token not understood: '" + token + "'");
                    }

                    address++;
                }
            }

            if (address != size)
            {
                throw new Xunit.Sdk.XunitException("s3270 ReadBuffer gave " + address + " positions for a " + columns + "x" + rows + " screen.");
            }

            int cursor = (int.Parse(status[8], CultureInfo.InvariantCulture) * columns) + int.Parse(status[9], CultureInfo.InvariantCulture);
            return new OracleScreen(rows, columns, text.ToArray(), cursor, status[0] != "U", codes, fields, ge, highlights);
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                if (!_process.HasExited)
                {
                    await _process.StandardInput.WriteLineAsync("Quit()");
                    _process.StandardInput.Close();
                }

                if (!_process.WaitForExit(5000))
                {
                    // Ours: started above, killed by its own handle, never by name.
                    _process.Kill();
                    _process.WaitForExit(5000);
                }
            }
            catch (IOException)
            {
                // Its input was already closed because it had ended.
                if (!_process.HasExited)
                {
                    _process.Kill();
                }
            }
            finally
            {
                _process.Dispose();
                _one.Dispose();
            }
        }

        // A key=value pair in an SF(...) or SA(...) token, or the given value when it has none.
        private static byte ValueIn(string token, string key, byte otherwise)
        {
            int at = token.IndexOf(key, StringComparison.Ordinal);
            return at < 0 ? otherwise : byte.Parse(token.AsSpan(at + key.Length, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        }

        // The 41= (XA_HIGHLIGHTING) value as its low four bits, or the given value.
        private static byte HighlightIn(string token, byte otherwise)
        {
            int at = token.IndexOf("41=", StringComparison.Ordinal);
            return at < 0 ? otherwise : (byte)(byte.Parse(token.AsSpan(at + 3, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture) & 0x0F);
        }

        private async Task<Reply> RunAsync(string command)
        {
            await _one.WaitAsync();
            try
            {
                await _process.StandardInput.WriteLineAsync(command);
                await _process.StandardInput.FlushAsync();
                var reply = new Reply();
                string? previous = null;

                // Every command has a deadline: an s3270 that never answers fails the test with
                // the command's name instead of hanging the run.
                using var deadline = new CancellationTokenSource(HostStreamReplay.TimeoutMs);
                while (true)
                {
                    string? line;
                    try
                    {
                        line = await _process.StandardOutput.ReadLineAsync(deadline.Token);
                    }
                    catch (OperationCanceledException)
                    {
                        throw new Xunit.Sdk.XunitException("s3270 did not answer " + command + " within " + HostStreamReplay.TimeoutMs + " ms. Its output so far: " + string.Join(" | ", reply.Data));
                    }

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
            finally
            {
                _one.Release();
            }
        }

        private sealed class Reply
        {
            public List<string> Data { get; } = new List<string>();

            public string Status { get; set; } = string.Empty;
        }
    }
}
