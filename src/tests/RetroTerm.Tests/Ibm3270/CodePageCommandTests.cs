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
    /// CODEPAGE, run as scripts and MCP run it (the registered command).
    /// </summary>
    public class CodePageCommandTests
    {
        // EW, WCC restore, SBA(0), X'4A': the cent sign in 037, another character in 277.
        private static readonly byte[] Screen =
        {
            Command3270.EraseWrite, 0x02, Order3270.SetBufferAddress, 0x40, 0x40, 0x4A,
        };

        private static async Task<(TerminalSession Session, Ibm3270Emulator Emulator)> Open()
        {
            var emulator = new Ibm3270Emulator();
            var session = new TerminalSession(emulator, "Test");
            var connection = new InMemoryConnection();
            await session.ConnectAsync(connection, TestContext.Current.CancellationToken);
            connection.SimulateReceive(Screen);
            await session.FlushAsync();
            return (session, emulator);
        }

        private static Task<CommandResult> Run(TerminalSession session, CommandArgs args)
        {
            return new CodePageCommand().ExecuteAsync(session, args, TestContext.Current.CancellationToken);
        }

        [Fact]
        public async Task ChangingTheCodePageDrawsTheScreenAgainWithIt()
        {
            var (session, emulator) = await Open();
            Assert.Equal(CodePage.Cp037.ToUnicode(0x4A), (char)emulator.Buffer.GetCell(0, 0).Codepoint);

            CommandResult result = await Run(session, new CommandArgs().Set("number", "277"));

            Assert.True(result.Success, result.Error);
            Assert.Equal(CodePage.Cp277, emulator.Display.CodePage);
            Assert.Equal(CodePage.Cp277.ToUnicode(0x4A), (char)emulator.Buffer.GetCell(0, 0).Codepoint);
        }

        [Fact]
        public async Task WithNoNumberItListsThePagesAndNamesTheOneInUse()
        {
            var (session, _) = await Open();

            CommandResult result = await Run(session, new CommandArgs());

            Assert.True(result.Success);
            Assert.StartsWith("code page 37, choose from:", result.Output);
            Assert.Contains("277 Denmark, Norway", result.Output);
            Assert.DoesNotContain("\n  310 ", result.Output);
        }

        [Theory]
        [InlineData("310")]
        [InlineData("9999")]
        public async Task ANumberThatIsNoBasePageIsRefusedAndNothingChanges(string number)
        {
            var (session, emulator) = await Open();

            CommandResult result = await Run(session, new CommandArgs().Set("number", number));

            Assert.False(result.Success);
            Assert.Contains("no base code page " + number, result.Error);
            Assert.Equal(CodePage.Cp037, emulator.Display.CodePage);
        }

        [Fact]
        public async Task AVtTabHasNoCodePage()
        {
            var session = new TerminalSession(new VT100Emulator(), "Test");

            CommandResult result = await Run(session, new CommandArgs().Set("number", "277"));

            Assert.False(result.Success);
            Assert.Contains("has no EBCDIC code page", result.Error);
        }
    }
}
