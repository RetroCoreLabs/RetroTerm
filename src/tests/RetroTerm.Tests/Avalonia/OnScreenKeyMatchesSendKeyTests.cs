//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs/RetroTerm
// RetroTerm — Connecting today's machines to yesterday's systems
//

using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

using Avalonia.Controls;
using Avalonia.Headless.XUnit;

using RetroIO.Protocols.Tn3270;

using RetroTerm.Core.Commands;
using RetroTerm.Core.Commands.Builtin;
using RetroTerm.Core.Session;
using RetroTerm.Core.Terminal.Input.Keyboards;
using RetroTerm.Desktop.Controls;
using RetroTerm.Desktop.Views;
using RetroTerm.Ibm3270;
using RetroTerm.Tests.TDV;

using Xunit;

namespace RetroTerm.Tests.Avalonia
{
    /// <summary>
    /// Every key on both IBM on-screen keyboards, clicked in the real keyboard window, does exactly
    /// what SENDKEY does with the key's name: the same bytes to the host, the same screen, the same
    /// cursor and the same keyboard lock. Plain, with Shift latched, and with Alt latched.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Two sessions get the same screen. One is driven through the window's click path (the
    /// window raises DescribedKeyPressed, and the press is handed to OnScreenKeyPress.Deliver the
    /// way MainWindow.OnDescribedKeyPressed does). The other gets SENDKEY IBM.NAME through the real
    /// command. A typing key has no SENDKEY name; its twin types the same text on the terminal's
    /// own keyboard, so the click is checked to type what the cap says and nothing else.
    /// </para>
    /// <para>
    /// A key that is not built answers "not built" on both surfaces and sends nothing on either;
    /// that is checked too, so a key that works on one surface only fails here.
    /// </para>
    /// </remarks>
    [Collection("Avalonia")]
    public class OnScreenKeyMatchesSendKeyTests
    {
        // An unprotected field holding ABC with the cursor on the A, then a protected field. Keys
        // like DELETE, ERASEEOF, INSERT and the arrows all change something visible on it.
        private static readonly byte[] Screen =
        {
            Command3270.EraseWrite, 0x02,
            Order3270.StartField, 0x40,
            Order3270.InsertCursor,
            0xC1, 0xC2, 0xC3,
            Order3270.SetBufferAddress, 0x40, 0xD4,
            Order3270.StartField, 0x60,
        };

        private enum Latch
        {
            None,
            Shift,
            Alt,
        }

        [AvaloniaTheory]
        [InlineData("IBM3278")]
        [InlineData("KB3270PLUS")]
        public async Task EveryKeyDoesWhatSendKeyDoes(string id)
        {
            var window = new VirtualKeyboardWindow();
            var keyboards = new Ibm3270Emulator().OnScreenKeyboards;
            window.ShowKeyboardsFor(keyboards);
            var view = window.FindControl<DescribedKeyboardView>("DescribedKeyboard")!;
            view.Description = id == "IBM3278" ? Ibm3270Keyboards.Ibm3278 : Ibm3270Keyboards.Kb3270Plus;
            var description = view.Description;

            KeyCap? shiftCap = null;
            KeyCap? altCap = null;
            for (int i = 0; i < description.Keys.Count; i++)
            {
                var c = description.Keys[i];
                if (shiftCap == null && c.Latch == KeyLatch.Shift) shiftCap = c;
                if (altCap == null && c.Latch == KeyLatch.Alt) altCap = c;
            }

            Assert.NotNull(shiftCap);
            Assert.NotNull(altCap);

            // What the window delivers, wired the way MainWindow wires it.
            Ibm3270Emulator? clickTarget = null;
            string? clickProblem = null;
            int deliveries = 0;
            window.DescribedKeyPressed += (cap, keyName, text) =>
            {
                deliveries++;
                clickProblem = OnScreenKeyPress.Deliver(clickTarget, description.Family, keyName, text);
            };

            int checkedPresses = 0;
            var failures = new StringBuilder();
            for (int i = 0; i < description.Keys.Count; i++)
            {
                var cap = description.Keys[i];
                if (cap.Latch != KeyLatch.None)
                {
                    continue;
                }

                for (int l = 0; l < 3; l++)
                {
                    var latch = (Latch)l;
                    if (!cap.ResolvePress(latch == Latch.Shift, latch == Latch.Alt, out string? keyName, out string? text))
                    {
                        // Nothing to compare: a blank key, or Alt on a key with no Alt function.
                        // ABlankKeyDoesNothing and AltOnAKeyWithoutAnAltFunctionDoesNothing pin that.
                        continue;
                    }

                    var (clickSession, clickConnection, clickEmulator) = await Open();
                    var (twinSession, twinConnection, twinEmulator) = await Open();

                    // The click.
                    clickTarget = clickEmulator;
                    clickProblem = null;
                    deliveries = 0;
                    if (latch == Latch.Shift) view.Press(shiftCap!);
                    if (latch == Latch.Alt) view.Press(altCap!);
                    view.Press(cap);
                    await clickSession.FlushAsync();

                    // The twin: SENDKEY for a named key, the terminal's own typing for text.
                    bool twinPressed;
                    if (keyName != null)
                    {
                        CommandResult result = await new SendKeyCommand().ExecuteAsync(twinSession,
                            new CommandArgs().Set("key", "IBM." + keyName), TestContext.Current.CancellationToken);
                        twinPressed = result.Success;
                    }
                    else
                    {
                        twinEmulator.HandleText(text!);
                        twinPressed = true;
                    }

                    await twinSession.FlushAsync();
                    checkedPresses++;

                    string what = "'" + cap.Legend + "' " + latch + " (" + (keyName != null ? "IBM." + keyName : "types " + text) + ")";
                    if (deliveries != 1)
                    {
                        failures.Append(what).Append(": the click was delivered ").Append(deliveries).AppendLine(" times");
                    }

                    if ((clickProblem == null) != twinPressed)
                    {
                        failures.Append(what).Append(": click says '").Append(clickProblem ?? "pressed")
                            .Append("', SENDKEY ").AppendLine(twinPressed ? "pressed" : "refused");
                    }

                    string clickState = State(clickEmulator, clickConnection.GetSentData());
                    string twinState = State(twinEmulator, twinConnection.GetSentData());
                    if (clickState != twinState)
                    {
                        failures.Append(what).Append(": click ").Append(clickState).Append(" but twin ").AppendLine(twinState);
                    }

                    clickSession.Dispose();
                    twinSession.Dispose();
                }
            }

            // Shift and Alt must not still be latched for the next test.
            Assert.False(view.ShiftLatched);
            Assert.False(view.AltLatched);
            Assert.True(failures.Length == 0, failures.ToString());

            // A floor so a description that lost its keys cannot pass by checking nothing. The 3278
            // has 87 keys and the KB 3270 Plus 122; with Shift and Alt variants there are more
            // presses than keys.
            Assert.True(checkedPresses > description.Keys.Count, "only " + checkedPresses + " presses were checked");
        }

        private static async Task<(TerminalSession Session, InMemoryConnection Connection, Ibm3270Emulator Emulator)> Open()
        {
            var emulator = new Ibm3270Emulator();
            var session = new TerminalSession(emulator, "Test");
            var connection = new InMemoryConnection();
            await session.ConnectAsync(connection, TestContext.Current.CancellationToken);
            connection.SimulateReceive(Screen);
            await session.FlushAsync();
            connection.ClearSentData();
            return (session, connection, emulator);
        }

        // Everything a key can change: the bytes sent, every buffer byte, the cursor, insert mode
        // and the keyboard lock.
        private static string State(Ibm3270Emulator emulator, IReadOnlyList<byte[]> sent)
        {
            var sb = new StringBuilder(4096);
            sb.Append("sent[");
            for (int i = 0; i < sent.Count; i++)
            {
                var record = sent[i];
                for (int b = 0; b < record.Length; b++)
                {
                    sb.Append(record[b].ToString("X2"));
                }

                sb.Append(';');
            }

            var display = emulator.Display;
            sb.Append("] cursor ").Append(display.CursorAddress)
              .Append(" insert ").Append(display.InsertMode)
              .Append(" lock ").Append(display.Lock)
              .Append(" buffer ");
            for (int i = 0; i < display.Size; i++)
            {
                sb.Append(display.GetByte(i).ToString("X2"));
            }

            return sb.ToString();
        }
    }
}
