//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs/RetroTerm
// RetroTerm — Connecting today's machines to yesterday's systems
//

using System.Threading.Tasks;

using RetroIO.Protocols.Tn3270;

using RetroTerm.Core.Commands;
using RetroTerm.Core.Commands.Builtin;
using RetroTerm.Core.Session;
using RetroTerm.Core.Terminal.Emulators;
using RetroTerm.Ibm3270;
using RetroTerm.Tests.TDV;

using Xunit;

namespace RetroTerm.Tests.Ibm3270
{
    /// <summary>
    /// STATUSLINE and WAITUNLOCK, run as scripts and MCP run them (the registered command).
    /// </summary>
    public class StatusLineCommandTests
    {
        // EW, WCC restore, SF protected "NAME", SF unprotected, IC.
        private static readonly byte[] Screen =
        {
            Command3270.EraseWrite, 0x02,
            Order3270.StartField, 0x60, 0xD5, 0xC1, 0xD4, 0xC5,
            Order3270.StartField, 0x40,
            Order3270.InsertCursor,
        };

        private static async Task<(TerminalSession Session, InMemoryConnection Connection, Ibm3270Emulator Emulator)> Open()
        {
            var emulator = new Ibm3270Emulator();
            var session = new TerminalSession(emulator, "Test");
            var connection = new InMemoryConnection();
            await session.ConnectAsync(connection, TestContext.Current.CancellationToken);
            connection.SimulateReceive(Screen);
            await session.FlushAsync();
            return (session, connection, emulator);
        }

        private static Task<CommandResult> Run(ISessionCommand command, TerminalSession session, CommandArgs args)
        {
            return command.ExecuteAsync(session, args, TestContext.Current.CancellationToken);
        }

        [Fact]
        public async Task StatusLineGivesLine25AndTheLock()
        {
            var (session, _, emulator) = await Open();

            CommandResult ready = await Run(new StatusLineCommand(), session, new CommandArgs());
            Assert.True(ready.Success);
            Assert.StartsWith("4A", ready.Output);
            Assert.Contains("001/007", ready.Output);
            Assert.EndsWith("keyboard: unlocked", ready.Output);

            Assert.True(emulator.HandleKey("Enter", false, false, false));
            await session.FlushAsync();
            CommandResult waiting = await Run(new StatusLineCommand(), session, new CommandArgs());
            Assert.Contains("X Wait", waiting.Output);
            Assert.EndsWith("keyboard: locked, waiting for the host", waiting.Output);
        }

        [Fact]
        public async Task WaitUnlockReturnsWhenTheHostRestoresTheKeyboard()
        {
            var (session, connection, emulator) = await Open();
            Assert.True(emulator.HandleKey("Enter", false, false, false));
            await session.FlushAsync();

            Task<CommandResult> wait = Run(new WaitUnlockCommand(), session, new CommandArgs().Set("timeout", "10000"));
            Assert.False(wait.IsCompleted);

            // A Write with WCC keyboard restore (GA23-0059-4 p. 3-3).
            connection.SimulateReceive(new byte[] { Command3270.Write, 0x02 });
            CommandResult result = await wait;

            Assert.True(result.Success, result.Error);
            Assert.Equal(KeyboardLock.None, emulator.Display.Lock);
        }

        [Fact]
        public async Task WaitUnlockTimesOutWhileTheHostSaysNothing()
        {
            var (session, _, emulator) = await Open();
            Assert.True(emulator.HandleKey("Enter", false, false, false));
            await session.FlushAsync();

            CommandResult result = await Run(new WaitUnlockCommand(), session, new CommandArgs().Set("timeout", "100"));

            Assert.False(result.Success);
            Assert.True(result.TimedOut);
            Assert.Contains("waiting for the host", result.Error);
        }

        [Fact]
        public async Task WaitUnlockRefusesAtOnceOnAnOperatorError()
        {
            // Typing on the protected "NAME" field: only Reset clears that, so waiting is pointless.
            var (session, _, emulator) = await Open();
            Assert.True(emulator.PressNamedKey("HOME", out _) == Core.Terminal.Input.NamedKeyResult.Pressed);
            await session.FlushAsync();
            await session.RunOnSessionThreadAsync(() => emulator.Display.MoveCursor(2));
            emulator.HandleText("x");
            await session.FlushAsync();
            Assert.Equal(KeyboardLock.OperatorError, emulator.Display.Lock);

            CommandResult result = await Run(new WaitUnlockCommand(), session, new CommandArgs().Set("timeout", "30000"));

            Assert.False(result.Success);
            Assert.False(result.TimedOut);
            Assert.Contains("Reset", result.Error);
        }

        [Fact]
        public async Task FieldsListsEveryFieldAndNeverAHiddenFieldsText()
        {
            // EW, WCC restore, SF protected "NAME", SF input with "hi" typed later, SBA 80 SF
            // protected bright "PASS" (X'E8'), SF input hidden (X'4C') holding "SECRET" from the
            // host, SBA 160 SF protected (stop).
            var emulator = new Ibm3270Emulator();
            var session = new TerminalSession(emulator, "Test");
            var connection = new InMemoryConnection();
            await session.ConnectAsync(connection, TestContext.Current.CancellationToken);
            connection.SimulateReceive(new byte[]
            {
                Command3270.EraseWrite, 0x02,
                Order3270.StartField, 0x60, 0xD5, 0xC1, 0xD4, 0xC5,
                Order3270.StartField, 0x40, Order3270.InsertCursor,
                Order3270.SetBufferAddress, 0xC1, 0x50, Order3270.StartField, 0xE8, 0xD7, 0xC1, 0xE2, 0xE2,
                Order3270.StartField, 0x4C, 0xE2, 0xC5, 0xC3, 0xD9, 0xC5, 0xE3,
                Order3270.SetBufferAddress, 0xC2, 0x60, Order3270.StartField, 0x60,
            });
            await session.FlushAsync();
            Assert.True(emulator.HandleText("hi"));
            await session.FlushAsync();

            CommandResult result = await Run(new FieldsCommand(), session, new CommandArgs());

            Assert.True(result.Success);
            string[] lines = result.Output!.Split('\n');
            Assert.Equal(5, lines.Length);
            Assert.Equal("field 1 row 1 col 2 length 4 protected \"NAME\"", lines[0]);
            Assert.Equal("field 2 row 1 col 7 length 74 input modified \"hi\"", lines[1]);
            Assert.Equal("field 3 row 2 col 2 length 4 protected bright \"PASS\"", lines[2]);
            Assert.StartsWith("field 4 row 2 col 7 length 74 input hidden \"", lines[3]);
            Assert.DoesNotContain("SECRET", result.Output);
        }

        [Fact]
        public async Task SetFieldReplacesAFieldsTextAndEnterSendsIt()
        {
            var (session, connection, emulator) = await Open();
            Assert.True(emulator.HandleText("old"));
            await session.FlushAsync();

            CommandResult set = await Run(new SetFieldCommand(), session, new CommandArgs().Set("field", "2").Set("text", "hi"));
            Assert.True(set.Success, set.Error);

            // Erase EOF emptied "old" first, so only "hi" goes: AID, cursor 8, SBA 6, "hi".
            connection.ClearSentData();
            Assert.True(emulator.HandleKey("Enter", false, false, false));
            await session.FlushAsync();
            Assert.Equal(new byte[] { 0x7D, 0x40, 0xC8, 0x11, 0x40, 0xC6, 0x88, 0x89 }, connection.GetSentData()[0]);
        }

        [Fact]
        public async Task SetFieldRefusesAProtectedFieldAMissingFieldAndTooMuchText()
        {
            var (session, _, _) = await Open();

            CommandResult protectedField = await Run(new SetFieldCommand(), session, new CommandArgs().Set("field", "1").Set("text", "x"));
            CommandResult missing = await Run(new SetFieldCommand(), session, new CommandArgs().Set("field", "9").Set("text", "x"));
            CommandResult tooLong = await Run(new SetFieldCommand(), session, new CommandArgs().Set("field", "2").Set("text", new string('x', 2000)));

            Assert.Contains("protected", protectedField.Error);
            Assert.Contains("no field 9", missing.Error);
            Assert.Contains("holds", tooLong.Error);
        }

        [Fact]
        public async Task CursorMovesTheCursorAndRefusesACellOffTheScreen()
        {
            var (session, _, emulator) = await Open();

            CommandResult moved = await Run(new CursorCommand(), session, new CommandArgs().Set("row", "2").Set("col", "7"));
            CommandResult off = await Run(new CursorCommand(), session, new CommandArgs().Set("row", "25").Set("col", "1"));

            Assert.True(moved.Success, moved.Error);
            Assert.Equal(86, emulator.Display.CursorAddress);
            Assert.False(off.Success);
        }

        [Fact]
        public async Task FieldsOnAnUnformattedScreenSaysSo()
        {
            var session = new TerminalSession(new Ibm3270Emulator(), "Test");
            await session.ConnectAsync(new InMemoryConnection(), TestContext.Current.CancellationToken);

            CommandResult result = await Run(new FieldsCommand(), session, new CommandArgs());

            Assert.Equal("no fields (unformatted screen)", result.Output);
        }

        [Fact]
        public async Task OnAVtTabStatusLineGivesTheHostLineAndWaitUnlockRefuses()
        {
            var session = new TerminalSession(new VT100Emulator(80, 24), "Test");
            await session.ConnectAsync(new InMemoryConnection(), TestContext.Current.CancellationToken);

            Assert.True((await Run(new StatusLineCommand(), session, new CommandArgs())).Success);
            CommandResult wait = await Run(new WaitUnlockCommand(), session, new CommandArgs());
            Assert.False(wait.Success);
        }
    }
}
