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
using RetroTerm.Core.Terminal.Input;
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

        [Fact]
        public async Task AttnSendsThePa1ShortReadThenABreakEvenWithTheKeyboardLocked()
        {
            // x3270 Attn_action in plain TN3270: PA1, then TELNET BREAK. Attn is for a host that
            // does not answer, so it must work while the keyboard waits for the host.
            var emulator = new Ibm3270Emulator();
            var session = new TerminalSession(emulator, "Test");
            var connection = new BreakCountingConnection();
            await session.ConnectAsync(connection, TestContext.Current.CancellationToken);
            connection.SimulateReceive(Screen);
            await session.FlushAsync();
            Assert.True(emulator.HandleKey("Enter", false, false, false));
            await session.FlushAsync();
            Assert.Equal(KeyboardLock.WaitingForHost, emulator.Display.Lock);
            connection.ClearSentData();

            Assert.Equal(NamedKeyResult.Pressed, emulator.PressNamedKey("ATTN", out _));
            await session.FlushAsync();

            var sent = connection.GetSentData();
            Assert.Single(sent);
            Assert.Equal(new byte[] { Aid.PA1 }, sent[0]);
            Assert.Equal(1, connection.Breaks);
            Assert.Equal(1, connection.SentBeforeFirstBreak);
            Assert.Equal(KeyboardLock.WaitingForHost, emulator.Display.Lock);
        }

        [Fact]
        public async Task EndSysReqAndCursorSelectAreReachedByName()
        {
            var emulator = new Ibm3270Emulator();
            var session = new TerminalSession(emulator, "Test");
            var connection = new InMemoryConnection();
            await session.ConnectAsync(connection, TestContext.Current.CancellationToken);
            connection.SimulateReceive(Screen);
            await session.FlushAsync();

            Assert.True(emulator.HandleText("hi"));
            Assert.Equal(NamedKeyResult.Pressed, emulator.PressNamedKey("HOME", out _));
            Assert.Equal(NamedKeyResult.Pressed, emulator.PressNamedKey("END", out _));
            await session.FlushAsync();
            Assert.Equal(8, emulator.Display.CursorAddress);

            // Not in a detectable field: Cursor Select locks the keyboard (Go Elsewhere).
            Assert.Equal(NamedKeyResult.Pressed, emulator.PressNamedKey("CURSORSELECT", out _));
            await session.FlushAsync();
            Assert.Equal(KeyboardLock.OperatorError, emulator.Display.Lock);
            Assert.Equal(NamedKeyResult.Pressed, emulator.PressNamedKey("RESET", out _));

            connection.ClearSentData();
            Assert.Equal(NamedKeyResult.Pressed, emulator.PressNamedKey("SYSREQ", out _));
            await session.FlushAsync();
            var sent = connection.GetSentData();
            Assert.Single(sent);
            Assert.Equal(new byte[] { 0x01, 0x6C, 0x61, 0x02, 0x11, 0x40, 0xC6, 0x88, 0x89 }, sent[0]);
        }

        [Fact]
        public async Task InTn3270eAttnIsMinusFunctionAndSysReqSendsNothing()
        {
            // x3270 Attn_action: with TN3270E and no SNA session bound, Minus Function, no PA1 and
            // no telnet command. SysReq_action: net_abort sends IAC AO only when SYSREQ was agreed,
            // and basic TN3270E agrees none. The session hands the connection's state over.
            var emulator = new Ibm3270Emulator();
            var session = new TerminalSession(emulator, "Test");
            var connection = new Tn3270ECountingConnection();
            await session.ConnectAsync(connection, TestContext.Current.CancellationToken);
            connection.SimulateReceive(Screen);
            connection.Agree(true);
            await session.FlushAsync();
            Assert.True(emulator.InTn3270E);
            string line25 = await RowAsync(session, 24);
            Assert.Equal('B', line25[1]);
            Assert.Equal("TCP00034", line25.Substring(54, 8));
            connection.ClearSentData();

            Assert.Equal(NamedKeyResult.Pressed, emulator.PressNamedKey("SYSREQ", out _));
            await session.FlushAsync();
            Assert.Empty(connection.GetSentData());
            Assert.Equal(0, connection.SysReqs);
            Assert.Equal(KeyboardLock.None, emulator.Display.Lock);

            Assert.Equal(NamedKeyResult.Pressed, emulator.PressNamedKey("ATTN", out _));
            await session.FlushAsync();
            Assert.Empty(connection.GetSentData());
            Assert.Equal(0, connection.Breaks);
            Assert.Equal(KeyboardLock.MinusFunction, emulator.Display.Lock);
            Assert.Contains("X -f", await RowAsync(session, 24));

            // TN3270E ends (the host backed off): Attn is plain TN3270's again.
            Assert.Equal(NamedKeyResult.Pressed, emulator.PressNamedKey("RESET", out _));
            connection.Agree(false);
            await session.FlushAsync();
            line25 = await RowAsync(session, 24);
            Assert.Equal('A', line25[1]);
            Assert.Equal("        ", line25.Substring(54, 8));
            Assert.Equal(NamedKeyResult.Pressed, emulator.PressNamedKey("ATTN", out _));
            await session.FlushAsync();
            Assert.Equal(new byte[] { Aid.PA1 }, Assert.Single(connection.GetSentData()));
            Assert.Equal(1, connection.Breaks);
        }

        [Fact]
        public async Task WithSysReqAgreedSysReqGoesToTheConnectionAndAttnNeedsABoundSession()
        {
            // RFC 2355 10.5.2: with SYSREQ agreed the key is telnet AO, which the connection
            // sends. RFC 2355 11 and x3270 Attn_action: Attn is IP once an SNA session is bound,
            // Minus Function before.
            var emulator = new Ibm3270Emulator();
            var session = new TerminalSession(emulator, "Test");
            var connection = new Tn3270ECountingConnection();
            await session.ConnectAsync(connection, TestContext.Current.CancellationToken);
            connection.Agree(true, bindImage: true, sysReq: true);
            await session.FlushAsync();
            connection.ClearSentData();

            Assert.Equal(NamedKeyResult.Pressed, emulator.PressNamedKey("SYSREQ", out _));
            await session.FlushAsync();
            Assert.Equal(1, connection.SysReqs);
            Assert.Empty(connection.GetSentData());

            Assert.Equal(NamedKeyResult.Pressed, emulator.PressNamedKey("ATTN", out _));
            await session.FlushAsync();
            Assert.Equal(0, connection.Attentions);
            Assert.Equal(KeyboardLock.MinusFunction, emulator.Display.Lock);

            // GA23-0061-2 p. 5-61, the Bind the manual suggests for LU type 2, model 2.
            Assert.Equal(NamedKeyResult.Pressed, emulator.PressNamedKey("RESET", out _));
            connection.Host(Tn3270EHostMessage.BindImage, IbmModel2Bind);
            await session.FlushAsync();
            Assert.Equal(NamedKeyResult.Pressed, emulator.PressNamedKey("ATTN", out _));
            await session.FlushAsync();
            Assert.Equal(1, connection.Attentions);
            Assert.Equal(0, connection.Breaks);

            // After an UNBIND nothing is bound again.
            connection.Host(Tn3270EHostMessage.Unbind, new byte[] { 0x01 });
            await session.FlushAsync();
            Assert.Equal(NamedKeyResult.Pressed, emulator.PressNamedKey("ATTN", out _));
            await session.FlushAsync();
            Assert.Equal(1, connection.Attentions);
            Assert.Equal(KeyboardLock.MinusFunction, emulator.Display.Lock);
        }

        [Fact]
        public async Task TheBindsScreenSizesAreUsedAndTheUnbindGivesTheModelsBack()
        {
            // A model 4 (24 x 80, 43 x 80) bound with X'7E' 32 x 80: one size for the session,
            // Erase/Write Alternate runs as Erase/Write (GA23-0061-2 p. 1-21).
            var emulator = new Ibm3270Emulator(4);
            var session = new TerminalSession(emulator, "Test");
            var connection = new Tn3270ECountingConnection();
            await session.ConnectAsync(connection, TestContext.Current.CancellationToken);
            connection.Agree(true, bindImage: true);
            await session.FlushAsync();

            byte[] bind = (byte[])IbmModel2Bind.Clone();
            bind[20] = 32;
            bind[21] = 80;
            bind[24] = 0x7E;
            connection.Host(Tn3270EHostMessage.BindImage, bind);
            connection.SimulateReceive(new byte[] { Command3270.EraseWriteAlternate, 0x02 });
            await session.FlushAsync();
            Assert.Equal(32, emulator.Display.Rows);

            connection.Host(Tn3270EHostMessage.Unbind, new byte[] { 0x01 });
            connection.SimulateReceive(new byte[] { Command3270.EraseWriteAlternate, 0x02 });
            await session.FlushAsync();
            Assert.Equal(43, emulator.Display.Rows);
        }

        [Fact]
        public async Task SscpLuTextIsShownAndEnterSendsTheOperatorsTextAlone()
        {
            var emulator = new Ibm3270Emulator();
            var session = new TerminalSession(emulator, "Test");
            var connection = new Tn3270ECountingConnection();
            await session.ConnectAsync(connection, TestContext.Current.CancellationToken);
            connection.Agree(true, sysReq: true);
            await session.FlushAsync();
            connection.ClearSentData();

            // "IKJ" NL, from the SSCP (GA23-0061-2 p. 5-37).
            connection.Host(Tn3270EHostMessage.SscpLuData, new byte[] { 0xC9, 0xD2, 0xD1, 0x15 });
            await session.FlushAsync();
            Assert.StartsWith("IKJ ", await RowAsync(session, 0));
            Assert.True(emulator.Display.SscpLuMode);

            Assert.True(emulator.HandleText("LOGOFF"));
            Assert.True(emulator.HandleKey("Enter", false, false, false));
            await session.FlushAsync();
            Assert.Equal(new byte[] { 0xD3, 0xD6, 0xC7, 0xD6, 0xC6, 0xC6 }, Assert.Single(connection.GetSentData()));

            // 3270 data from the application takes the screen back.
            connection.SimulateReceive(Screen);
            await session.FlushAsync();
            Assert.False(emulator.Display.SscpLuMode);
            Assert.Equal(6, emulator.Display.CursorAddress);
        }

        [Fact]
        public void AltSIsSysReq()
        {
            // KEYBOARD.md section 8: Alt+S, because the PC's own SysRq is Alt+PrintScreen.
            Assert.Equal("SYSREQ", Ibm3270Emulator.PcKeyToName("S", shift: false, control: false, alt: true));
        }

        // GA23-0061-2 p. 5-61, "Bind Default": the LU type 1 table with the LU type 2 changes
        // (byte 9 X'00', byte 10 X'87', byte 14 X'02', byte 18 X'00', byte 24 X'02' model 2).
        private static readonly byte[] IbmModel2Bind =
        {
            0x31, 0x01, 0x03, 0x03, 0xB1, 0x90, 0x30, 0x80, 0x00, 0x00, 0x87, 0x87, 0x00, 0x00,
            0x02, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x02, 0x00, 0x00,
        };

        // A connection whose TN3270E state the test sets, and which counts breaks.
        private sealed class Tn3270ECountingConnection : InMemoryConnection, IBreakConnection, ITn3270EConnection
        {
            public event System.Action<bool>? Tn3270EChanged;

            public bool InTn3270E { get; private set; }

            public event System.Action<Tn3270EHostMessage, System.ReadOnlyMemory<byte>>? HostMessageReceived;

            public string? GrantedLuName { get; private set; }

            public bool BindImageAgreed { get; private set; }

            public bool SysReqAgreed { get; private set; }

            public int Breaks { get; private set; }

            public int SysReqs { get; private set; }

            public int Attentions { get; private set; }

            public void SendBreak() => Breaks++;

            public void SendSysReq() => SysReqs++;

            public void SendAttention() => Attentions++;

            public void Agree(bool on, bool bindImage = false, bool sysReq = false)
            {
                InTn3270E = on;
                GrantedLuName = on ? "TCP00034" : null;
                BindImageAgreed = on && bindImage;
                SysReqAgreed = on && sysReq;
                Tn3270EChanged?.Invoke(on);
            }

            public void Host(Tn3270EHostMessage kind, byte[] data) => HostMessageReceived?.Invoke(kind, data);
        }
        // A connection that can send a break, and remembers how many records went before it.
        private sealed class BreakCountingConnection : InMemoryConnection, IBreakConnection
        {
            public int Breaks { get; private set; }

            public int SentBeforeFirstBreak { get; private set; } = -1;

            public void SendBreak()
            {
                if (Breaks == 0)
                {
                    SentBeforeFirstBreak = GetSentData().Count;
                }

                Breaks++;
            }
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
