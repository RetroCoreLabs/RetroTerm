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
using RetroTerm.Tests.Oracle;

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
        // what crossed the wire instead of only what the screen ended up as. What the client
        // sent is masked before it is written down (see PasswordMask).
        private readonly Tk5WireLog _wire = new Tk5WireLog();

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

        /// <summary>
        /// Gets the emulator, for a test that reads the screen size the host chose.
        /// </summary>
        public Ibm3270Emulator Emulator => _emulator;

        public static Task<RetroTermTk5Terminal> OpenAsync(Tk5Settings settings) => OpenAsync(settings, 2);

        public static async Task<RetroTermTk5Terminal> OpenAsync(Tk5Settings settings, int model)
        {
            var emulator = new Ibm3270Emulator(model);
            var session = new TerminalSession(emulator, "TK5");
            IConnection connection = ConnectionFactory.CreateConnection(new ConnectionFactory.ConnectionParameters
            {
                Protocol = ConnectionFactory.ProtocolType.TN3270,
                Host = settings.Host,
                Port = settings.Port,
                EmulatorType = "IBM-3278-" + model.ToString(System.Globalization.CultureInfo.InvariantCulture),
            });
            var terminal = new RetroTermTk5Terminal(session, emulator, settings.Host);
            connection.DataReceived += data => terminal._wire.Host(data.Span);
            emulator.DataToSend += data => terminal._wire.Sent(data);
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
                    // The whole message is masked, not only the records: the screen text and the
                    // step's name are printed too (see PasswordMask).
                    throw new Xunit.Sdk.XunitException(PasswordMask.Text("RetroTerm: no " + what + " with the keyboard unlocked after " + timeoutMs +
                        " ms (lock " + _emulator.Display.Lock + "). Screen:\n" + screen.Text() + "\nRecords, oldest first:\n" + WireText()));
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
                var colours = new byte[display.Size];
                for (int a = 0; a < display.Size; a++)
                {
                    if (display.IsFieldAttribute(a))
                    {
                        addresses.Add(a);
                        attributes.Add(display.GetByte(a));
                        highlights[a] = (byte)(display.GetFieldHighlight(a) & 0x0F);
                        colours[a] = display.GetFieldColor(a);
                    }
                    else
                    {
                        // Low four bits, as s3270 keeps them (Tk5Screen.Highlights).
                        highlights[a] = (byte)(display.GetCharacterHighlight(a) & 0x0F);
                        colours[a] = display.GetCharacterColor(a);
                    }
                }

                return new Tk5Screen(snapshot.Text.Split('\n'), snapshot.CursorRow, snapshot.CursorColumn, addresses, attributes, Line25(display), DrawnLine25(snapshot), highlights, colours);
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

        /// <summary>
        /// Every record both ways so far, oldest first, one line each (256 bytes shown), what
        /// the client sent masked (<see cref="Tk5WireLog"/>).
        /// </summary>
        /// <returns>
        /// The lines.
        /// </returns>
        public string WireText() => _wire.Text();

        public async ValueTask DisposeAsync()
        {
            await _session.DisconnectAsync();
            _session.Dispose();
        }
    }
}
