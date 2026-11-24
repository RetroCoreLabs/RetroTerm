//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs/RetroTerm
// RetroTerm — Connecting today's machines to yesterday's systems
//

using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

using Avalonia.Headless.XUnit;
using Avalonia.Input;

using RetroTerm.Core.Commands;
using RetroTerm.Core.Commands.Builtin;
using RetroTerm.Core.Configuration;
using RetroTerm.Core.Session;
using RetroTerm.Core.Terminal.Emulators;
using RetroTerm.Desktop.Controls;
using RetroTerm.Tests.TDV;

using Xunit;

namespace RetroTerm.Tests.Avalonia
{
    /// <summary>
    /// Every VT key name SENDKEY knows, in every key mode, sends the bytes the VT420 Programmer
    /// Reference (EK-VT420-RM-002, Chapter 3) gives for that key - and, for every key a PC keyboard
    /// has, the same bytes as that key pressed in the real terminal window.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The modes: cursor keys normal or application (DECCKM, CSI ? 1 h), keypad numeric or
    /// application (DECKPNM ESC &gt; / DECKPAM ESC =), and 7- or 8-bit controls (S7C1T / S8C1T,
    /// ESC SP F / ESC SP G). All eight combinations, every key.
    /// </para>
    /// <para>
    /// The manual's bytes are written below as 7-bit text and converted to 8-bit by rule: CSI is
    /// 9/11 and SS3 is 8/15 in every table. The wire bytes are compared, after the profile's own
    /// transport encoding, because that is what a host receives.
    /// </para>
    /// </remarks>
    [Collection("Avalonia")]
    public class VtNamedKeyMatchesKeyPressTests
    {
        private const char Esc = (char)0x1B;

        private static string Csi(string tail) => Esc + "[" + tail;

        private static string Ss3(string tail) => Esc + "O" + tail;

        /// <summary>
        /// One key: its SENDKEY name, the PC key that is the same key (or null when a PC keyboard
        /// has none), and what the manual says it sends, 7-bit, by mode.
        /// </summary>
        private sealed record VtKey(string Name, Key? PcKey, string Normal, string CursorApp, string KeypadApp, string Source);

        private static List<VtKey> Keys()
        {
            var keys = new List<VtKey>
            {
                // Table 3-3: CSI A..D, SS3 A..D under DECCKM. The keypad mode does not touch them.
                new("UP", Key.Up, Csi("A"), Ss3("A"), Csi("A"), "Table 3-3"),
                new("DOWN", Key.Down, Csi("B"), Ss3("B"), Csi("B"), "Table 3-3"),
                new("RIGHT", Key.Right, Csi("C"), Ss3("C"), Csi("C"), "Table 3-3"),
                new("LEFT", Key.Left, Csi("D"), Ss3("D"), Csi("D"), "Table 3-3"),

                // Table 3-4: PF1-PF4 send SS3 P..S in BOTH keypad modes.
                new("PF1", Key.F1, Ss3("P"), Ss3("P"), Ss3("P"), "Table 3-4"),
                new("PF2", Key.F2, Ss3("Q"), Ss3("Q"), Ss3("Q"), "Table 3-4"),
                new("PF3", Key.F3, Ss3("R"), Ss3("R"), Ss3("R"), "Table 3-4"),
                new("PF4", Key.F4, Ss3("S"), Ss3("S"), Ss3("S"), "Table 3-4"),

                // Table 3-4: the rest of the keypad.
                new("KPCOMMA", Key.Separator, ",", ",", Ss3("l"), "Table 3-4"),
                new("KPMINUS", Key.Subtract, "-", "-", Ss3("m"), "Table 3-4"),
                new("KPPERIOD", Key.Decimal, ".", ".", Ss3("n"), "Table 3-4"),
                new("KPENTER", null, "\r", "\r", Ss3("M"), "Table 3-4"),

                // Table 3-1: the editing keys.
                new("FIND", null, Csi("1~"), Csi("1~"), Csi("1~"), "Table 3-1"),
                new("INSERT", Key.Insert, Csi("2~"), Csi("2~"), Csi("2~"), "Table 3-1"),
                new("REMOVE", Key.Delete, Csi("3~"), Csi("3~"), Csi("3~"), "Table 3-1"),
                new("SELECT", null, Csi("4~"), Csi("4~"), Csi("4~"), "Table 3-1"),
                new("PREV", Key.PageUp, Csi("5~"), Csi("5~"), Csi("5~"), "Table 3-1"),
                new("NEXT", Key.PageDown, Csi("6~"), Csi("6~"), Csi("6~"), "Table 3-1"),
            };

            for (int d = 0; d <= 9; d++)
            {
                string digit = ((char)('0' + d)).ToString();
                keys.Add(new("KP" + d, Key.NumPad0 + d, digit, digit, Ss3(((char)('p' + d)).ToString()), "Table 3-4"));
            }

            // Table 3-6: F6..F20. DEC skips 22, 27 and 30.
            int[] numbers = { 17, 18, 19, 20, 21, 23, 24, 25, 26, 28, 29, 31, 32, 33, 34 };
            for (int i = 0; i < numbers.Length; i++)
            {
                string code = Csi(numbers[i] + "~");
                keys.Add(new("F" + (6 + i), Key.F6 + i, code, code, code, "Table 3-6"));
            }

            return keys;
        }

        private static string EightBit(string sevenBit)
        {
            if (sevenBit.Length >= 2 && sevenBit[0] == Esc && sevenBit[1] == '[')
            {
                return "\u009B" + sevenBit.Substring(2);
            }

            if (sevenBit.Length >= 2 && sevenBit[0] == Esc && sevenBit[1] == 'O')
            {
                return "\u008F" + sevenBit.Substring(2);
            }

            return sevenBit;
        }

        // The host sequences that put a terminal in the mode being tested.
        private static string ModeSetup(bool cursorApp, bool keypadApp, bool eightBit)
        {
            return Csi(cursorApp ? "?1h" : "?1l")
                + Esc + (keypadApp ? "=" : ">")
                + Esc + " " + (eightBit ? "G" : "F");
        }

        [AvaloniaFact]
        public async Task EveryVtKeyNameSendsWhatTheManualSaysAndWhatTheRealKeySends()
        {
            var failures = new StringBuilder();
            var keys = Keys();
            int compared = 0;

            for (int mode = 0; mode < 8; mode++)
            {
                bool cursorApp = (mode & 1) != 0;
                bool keypadApp = (mode & 2) != 0;
                bool eightBit = (mode & 4) != 0;
                string setup = ModeSetup(cursorApp, keypadApp, eightBit);
                string modeName = (cursorApp ? "DECCKM" : "cursor-normal") + "/" + (keypadApp ? "DECKPAM" : "DECKPNM") + "/" + (eightBit ? "S8C1T" : "S7C1T");

                for (int k = 0; k < keys.Count; k++)
                {
                    var key = keys[k];
                    string manual = key.Name.StartsWith("KP", System.StringComparison.Ordinal) || key.Name.StartsWith("PF", System.StringComparison.Ordinal)
                        ? (keypadApp ? key.KeypadApp : key.Normal)
                        : (cursorApp ? key.CursorApp : key.Normal);
                    if (eightBit)
                    {
                        manual = EightBit(manual);
                    }

                    // SENDKEY through a real session.
                    var emulator = EmulatorFactory.CreateEmulator("VT340", 80, 24, 100);
                    var connection = new InMemoryConnection();
                    using var session = new TerminalSession(emulator, "VtKeyTest");
                    await session.ConnectAsync(connection);
                    connection.SimulateReceive(Encoding.ASCII.GetBytes(setup));
                    await session.FlushAsync();
                    connection.ClearSentData();

                    CommandResult result = await new SendKeyCommand().ExecuteAsync(session,
                        new CommandArgs().Set("key", "VT." + key.Name), TestContext.Current.CancellationToken);
                    byte[] expected = emulator.Profile.EncodeForTransport(manual);
                    if (!result.Success)
                    {
                        failures.Append(modeName).Append(' ').Append(key.Name).Append(": SENDKEY failed: ").Append(result.Error).Append('\n');
                        continue;
                    }

                    byte[] sent = Joined(connection.GetSentData());
                    if (!Same(sent, expected))
                    {
                        failures.Append(modeName).Append(' ').Append(key.Name).Append(" (").Append(key.Source)
                            .Append("): SENDKEY sent ").Append(Hex(sent)).Append(", the manual says ").Append(Hex(expected)).Append('\n');
                    }

                    // The same key pressed in the real terminal window, if a PC keyboard has it. In
                    // numeric keypad mode a keypad key travels the TEXT path (the OS types "4"), so a
                    // key-down there sends nothing from the canvas by design; it is checked against
                    // the manual above and not here.
                    bool keypadKey = key.PcKey is Key pk && (pk >= Key.NumPad0 && pk <= Key.NumPad9 || pk == Key.Separator || pk == Key.Subtract || pk == Key.Decimal);
                    if (key.PcKey is Key pcKey && (!keypadKey || keypadApp))
                    {
                        var pressed = PressInWindow(pcKey, setup, out var windowEmulator);
                        byte[] pressedBytes = windowEmulator.Profile.EncodeForTransport(pressed ?? string.Empty);
                        if (!Same(pressedBytes, sent))
                        {
                            failures.Append(modeName).Append(' ').Append(key.Name).Append(": the real key ").Append(pcKey)
                                .Append(" sends ").Append(Hex(pressedBytes)).Append(", SENDKEY sends ").Append(Hex(sent)).Append('\n');
                        }

                        compared++;
                    }
                }
            }

            Assert.True(failures.Length == 0, failures.ToString());

            // Guards against a loop that compared nothing: 40 keys have a PC key; the 27 that are
            // not keypad keys are pressed in all 8 modes, the 13 keypad ones only in the 4
            // application-keypad modes.
            Assert.Equal((27 * 8) + (13 * 4), compared);
        }

        [AvaloniaFact]
        public async Task AnUnknownVtKeyNameSendsNothingAndListsTheKeys()
        {
            var emulator = EmulatorFactory.CreateEmulator("VT340", 80, 24, 100);
            var connection = new InMemoryConnection();
            using var session = new TerminalSession(emulator, "VtKeyTest");
            await session.ConnectAsync(connection);

            CommandResult result = await new SendKeyCommand().ExecuteAsync(session,
                new CommandArgs().Set("key", "VT.HJELP"), TestContext.Current.CancellationToken);

            Assert.False(result.Success);
            Assert.Contains("unknown VT key", result.Error);
            Assert.Contains("PF1", result.Error);
            Assert.Empty(connection.GetSentData());
        }

        // A VT340 in a real TerminalCanvas, put in the mode, then one key-down. Returns what the
        // canvas handed on for the host (null when it handed on nothing).
        private static string? PressInWindow(Key key, string setup, out TerminalEmulatorBase emulator)
        {
            emulator = EmulatorFactory.CreateEmulator("VT340", 80, 24, 100);
            emulator.ProcessData(Encoding.ASCII.GetBytes(setup));

            var canvas = new TerminalCanvas();
            canvas.SetEmulator(emulator);
            string? sent = null;
            canvas.InputReceived += text => sent = (sent ?? string.Empty) + text;
            canvas.RaiseEvent(new KeyEventArgs
            {
                RoutedEvent = InputElement.KeyDownEvent,
                Key = key,
                KeyModifiers = global::Avalonia.Input.KeyModifiers.None,
            });
            return sent;
        }

        private static byte[] Joined(IReadOnlyList<byte[]> chunks)
        {
            int total = 0;
            for (int i = 0; i < chunks.Count; i++)
            {
                total += chunks[i].Length;
            }

            var all = new byte[total];
            int at = 0;
            for (int i = 0; i < chunks.Count; i++)
            {
                chunks[i].CopyTo(all, at);
                at += chunks[i].Length;
            }

            return all;
        }

        private static bool Same(byte[] a, byte[] b)
        {
            return a.AsSpan().SequenceEqual(b);
        }

        private static string Hex(byte[] bytes)
        {
            var sb = new StringBuilder(bytes.Length * 3);
            for (int i = 0; i < bytes.Length; i++)
            {
                if (i > 0)
                {
                    sb.Append(' ');
                }

                sb.Append(bytes[i].ToString("X2", System.Globalization.CultureInfo.InvariantCulture));
            }

            return bytes.Length == 0 ? "nothing" : sb.ToString();
        }
    }
}
