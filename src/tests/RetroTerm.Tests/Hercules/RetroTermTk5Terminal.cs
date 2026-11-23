//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs/RetroTerm
// RetroTerm — Connecting today's machines to yesterday's systems
//

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;

using RetroIO.Protocols.Tn3270;

using RetroTerm.Core.Protocols;
using RetroTerm.Core.Session;
using RetroTerm.Core.Terminal.Input;
using RetroTerm.Ibm3270;

namespace RetroTerm.Tests.Hercules
{
    /// <summary>
    /// RetroTerm's own 3270 on a real TN3270 connection: the same session, connection and
    /// emulator the app uses, with keys pressed by name as scripts and MCP press them.
    /// </summary>
    internal sealed class RetroTermTk5Terminal : ITk5Terminal
    {
        private readonly TerminalSession _session;
        private readonly Ibm3270Emulator _emulator;
        private readonly string _host;

        // Every record in both directions, in order, printed when a wait fails so a failure says
        // what crossed the wire instead of only what the screen ended up as. Written from the
        // network and session threads, so it is locked.
        private readonly List<string> _wire = new List<string>();

        private RetroTermTk5Terminal(TerminalSession session, Ibm3270Emulator emulator, string host)
        {
            _host = host;
            _session = session;
            _emulator = emulator;
        }

        public string Name => "RetroTerm";

        /// <summary>
        /// Gets the session, for a test that runs a registry command on it (HOSTSEND, HOSTRECEIVE).
        /// </summary>
        public TerminalSession Session => _session;

        public static async Task<RetroTermTk5Terminal> OpenAsync(Tk5Settings settings)
        {
            var emulator = new Ibm3270Emulator();
            var session = new TerminalSession(emulator, "TK5");
            IConnection connection = ConnectionFactory.CreateConnection(new ConnectionFactory.ConnectionParameters
            {
                Protocol = ConnectionFactory.ProtocolType.TN3270,
                Host = settings.Host,
                Port = settings.Port,
                EmulatorType = "IBM-3278-2",
            });
            var terminal = new RetroTermTk5Terminal(session, emulator, settings.Host);
            connection.DataReceived += data => terminal.Note("host", data.Span);
            emulator.DataToSend += data => terminal.Note("sent", data);
            await session.ConnectAsync(connection, Xunit.TestContext.Current.CancellationToken);
            return terminal;
        }

        public async Task TypeAsync(string text)
        {
            await _session.SendInputAsync(text);
            await _session.FlushAsync();
        }

        public async Task PressAsync(string key)
        {
            NamedKeyResult result = _emulator.PressNamedKey(key, out string? notBuilt);
            if (result != NamedKeyResult.Pressed)
            {
                throw new Xunit.Sdk.XunitException("RetroTerm did not press " + key + ": " + result + " " + notBuilt);
            }

            await _session.FlushAsync();
        }

        public async Task<Tk5Screen> WaitUntilAsync(Tk5ScreenTest done, string what, int timeoutMs)
        {
            // A poll with a deadline on the thing asserted, never a sleep-then-assert: the host
            // answers when it answers.
            var clock = Stopwatch.StartNew();
            Tk5Screen screen = await ReadAsync();
            while (!(done(screen) && _emulator.Display.Lock == KeyboardLock.None))
            {
                if (clock.ElapsedMilliseconds > timeoutMs)
                {
                    throw new Xunit.Sdk.XunitException("RetroTerm: no " + what + " with the keyboard unlocked after " + timeoutMs +
                        " ms (lock " + _emulator.Display.Lock + "). Screen:\n" + screen.Text() + "\nRecords, oldest first:\n" + WireText());
                }

                await Task.Delay(50);
                screen = await ReadAsync();
            }

            return screen;
        }

        public async Task<Tk5Screen> ReadAsync()
        {
            ScreenSnapshot snapshot = await _session.ReadScreenAsync(stripTrailingBlanks: false);
            Tk5Screen screen = await _session.RunOnSessionThreadAsync(() =>
            {
                Display3270 display = _emulator.Display;
                var addresses = new List<int>();
                var attributes = new List<byte>();
                var highlights = new byte[display.Size];
                for (int a = 0; a < display.Size; a++)
                {
                    if (display.IsFieldAttribute(a))
                    {
                        addresses.Add(a);
                        attributes.Add(display.GetByte(a));
                        highlights[a] = (byte)(display.GetFieldHighlight(a) & 0x0F);
                    }
                    else
                    {
                        // Low four bits, as s3270 keeps them (Tk5Screen.Highlights).
                        highlights[a] = (byte)(display.GetCharacterHighlight(a) & 0x0F);
                    }
                }

                return new Tk5Screen(snapshot.Text.Split('\n'), snapshot.CursorRow, snapshot.CursorColumn, addresses, attributes, Line25(display), DrawnLine25(snapshot), highlights);
            });
            return screen;
        }

        // RetroTerm's line 25 state in s3270's status format (see Tk5Screen.Status), read from
        // the same Display3270 the status line will be drawn from. Called on the session thread.
        private string Line25(Display3270 display)
        {
            // x3270 writes L for any lock, whatever the reason (Common\task.c, status_string and
            // task_kbwait_state: kybdlock != 0).
            string keyboard = display.Lock == KeyboardLock.None ? "U" : "L";
            int field = display.GetFieldAttributeFor(display.CursorAddress);
            string formatted = field < 0 ? "U" : "F";
            string protectedField = field >= 0 && (display.GetByte(field) & 0x20) != 0 ? "P" : "U";
            string connection = _session.IsConnected ? "C(" + _host + ")" : "N";
            return keyboard + " " + formatted + " " + protectedField + " " + connection + " I 2 " + Tk5Screen.Rows + " " + Tk5Screen.Columns;
        }

        // Row 25 of RetroTerm's real screen: what the operator sees as line 25.
        private static string DrawnLine25(ScreenSnapshot snapshot)
        {
            string[] rows = snapshot.Text.Split('\n');
            string line = rows.Length > Tk5Screen.Rows ? rows[Tk5Screen.Rows] : string.Empty;
            return line.Length >= Tk5Screen.Columns ? line.Substring(0, Tk5Screen.Columns) : line.PadRight(Tk5Screen.Columns);
        }

        private void Note(string direction, ReadOnlySpan<byte> data)
        {
            var line = new System.Text.StringBuilder(direction.Length + 2 + (data.Length * 3));
            line.Append(direction).Append(':');
            // 256 bytes: enough for a whole IND$FILE message record (112 bytes on TK5).
            int shown = data.Length > 256 ? 256 : data.Length;
            for (int i = 0; i < shown; i++)
            {
                line.Append(' ').Append(data[i].ToString("X2", System.Globalization.CultureInfo.InvariantCulture));
            }

            if (shown < data.Length)
            {
                line.Append(" ... (").Append(data.Length).Append(" bytes)");
            }

            lock (_wire)
            {
                _wire.Add(line.ToString());
            }
        }

        /// <summary>
        /// Every record both ways so far, oldest first, one line each (256 bytes shown).
        /// </summary>
        /// <returns>
        /// The lines.
        /// </returns>
        public string WireText()
        {
            lock (_wire)
            {
                return string.Join("\n", _wire);
            }
        }

        public async ValueTask DisposeAsync()
        {
            await _session.DisconnectAsync();
            _session.Dispose();
        }
    }
}
