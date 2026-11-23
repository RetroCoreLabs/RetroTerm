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
