//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs/RetroTerm
// RetroTerm — Connecting today's machines to yesterday's systems
//

using System.Threading.Tasks;

using RetroIO.Protocols.Tn3270;

using RetroTerm.Core.Protocols;
using RetroTerm.Core.Session;
using RetroTerm.Ibm3270;
using RetroTerm.Tests.TDV;

using Xunit;

namespace RetroTerm.Tests.Ibm3270
{
    /// <summary>
    /// The 3270 inside a real TerminalSession: host records go in through the pump, and key presses
    /// change the screen on the pump too (the session thread rule), not on the UI thread.
    /// </summary>
    public class Ibm3270SessionTests
    {
        private static readonly byte[] Screen =
        {
            Command3270.EraseWrite, 0x02,
            Order3270.StartField, 0x60, 0xD5, 0xC1, 0xD4, 0xC5,
            Order3270.StartField, 0x40,
            Order3270.InsertCursor,
            Order3270.SetBufferAddress, 0x40, 0xD4,
            Order3270.StartField, 0xF0,
        };

        [Fact]
        public async Task KeysRunOnTheSessionThreadAndEnterReachesTheConnection()
        {
            var emulator = new Ibm3270Emulator();
            var session = new TerminalSession(emulator, "Test");
            var connection = new InMemoryConnection();
            await session.ConnectAsync(connection, TestContext.Current.CancellationToken);

            // The session must hand the emulator its pump.
            Assert.NotNull(emulator.SessionThread);

            // A TN3270 connection delivers one whole record per receive.
            connection.SimulateReceive(Screen);
            await session.FlushAsync();
            Assert.Equal(6, emulator.Display.CursorAddress);
            connection.ClearSentData();

            Assert.True(emulator.HandleText("hi"));
            Assert.True(emulator.HandleKey("Enter", false, false, false));
            await session.FlushAsync();

            var sent = connection.GetSentData();
            Assert.Single(sent);
            Assert.Equal(new byte[] { 0x7D, 0x40, 0xC8, 0x11, 0x40, 0xC6, 0x88, 0x89 }, sent[0]);
            Assert.Equal(KeyboardLock.WaitingForHost, emulator.Display.Lock);
        }

        [Fact]
        public async Task TextFromMcpAndScriptsIsTypedNotSentRaw()
        {
            // terminal_send / SEND "hi\r" reaches SendInputAsync. For a 3270 it must type "hi" and
            // press Enter, giving ONE 3270 record - never the raw characters.
            var emulator = new Ibm3270Emulator();
            var session = new TerminalSession(emulator, "Test");
            var connection = new InMemoryConnection();
            await session.ConnectAsync(connection, TestContext.Current.CancellationToken);
            connection.SimulateReceive(Screen);
            await session.FlushAsync();
            connection.ClearSentData();

            await session.SendInputAsync("hi\r", TestContext.Current.CancellationToken);

            var sent = connection.GetSentData();
            Assert.Single(sent);
            Assert.Equal(new byte[] { 0x7D, 0x40, 0xC8, 0x11, 0x40, 0xC6, 0x88, 0x89 }, sent[0]);
        }

        [Fact]
        public async Task Line25ShowsTheConnectionTheLockAndTheCursorAsWc3270Does()
        {
            // wc3270\screen.c, draw_oia: "4" at 0, "A" at 1, the box at 2 (blank in 3270 mode, "?"
            // otherwise), the message from 7, the cursor "%03d/%03d" at 72.
            var emulator = new Ibm3270Emulator();
            var session = new TerminalSession(emulator, "Test");
            var connection = new InMemoryConnection();
            await session.FlushAsync();
            Assert.Equal(Line25("?", "X Not Connected", string.Empty), await RowAsync(session, 24));

            await session.ConnectAsync(connection, TestContext.Current.CancellationToken);
            connection.SimulateReceive(Screen);
            await session.FlushAsync();
            Assert.Equal(Line25(" ", string.Empty, "001/007"), await RowAsync(session, 24));

            // Enter: waiting for the host. A Write without keyboard restore: System Lock.
            Assert.True(emulator.HandleKey("Enter", false, false, false));
            await session.FlushAsync();
            Assert.Equal(Line25(" ", "X Wait", "001/007"), await RowAsync(session, 24));
            connection.SimulateReceive(new byte[] { Command3270.Write, Wcc.ResetMdt });
            await session.FlushAsync();
            Assert.Equal(Line25(" ", "X SYSTEM", "001/007"), await RowAsync(session, 24));

            // The host still sees 24 rows: row 24 is the host's, untouched by line 25.
            Assert.Equal(new string(' ', 80), await RowAsync(session, 23));

            await session.DisconnectAsync();
            await session.FlushAsync();
            Assert.Equal(Line25("?", "X Not Connected", string.Empty), await RowAsync(session, 24));
        }

        private static string Line25(string box, string message, string cursor)
        {
            char[] line = new string(' ', 80).ToCharArray();
            line[0] = '4';
            line[1] = 'A';
            line[2] = box[0];
            message.CopyTo(0, line, 7, message.Length);
            cursor.CopyTo(0, line, 72, cursor.Length);
            return new string(line);
        }

        private static async Task<string> RowAsync(TerminalSession session, int row)
        {
            ScreenSnapshot snapshot = await session.ReadScreenAsync(stripTrailingBlanks: false);
            string[] rows = snapshot.Text.Split('\n');
            return rows[row].PadRight(80);
        }
    }
}
