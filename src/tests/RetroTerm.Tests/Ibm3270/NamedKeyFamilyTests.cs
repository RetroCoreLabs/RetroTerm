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
using RetroTerm.Core.Terminal.Emulators.TDV;
using RetroTerm.Ibm3270;
using RetroTerm.Tests.TDV;

using Xunit;

namespace RetroTerm.Tests.Ibm3270
{
    /// <summary>
    /// SENDKEY with key families (TDV.HJELP, IBM.PF3, VT.PF1): the family must match the tab, and a
    /// bare name is looked up in the tab's own terminal. Ronny's choice, 22 November 2026.
    /// </summary>
    public class NamedKeyFamilyTests
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

        private static async Task<(TerminalSession Session, InMemoryConnection Connection)> Open(TerminalEmulatorBase emulator)
        {
            var session = new TerminalSession(emulator, "Test");
            var connection = new InMemoryConnection();
            await session.ConnectAsync(connection, TestContext.Current.CancellationToken);
            return (session, connection);
        }

        private static Task<CommandResult> SendKey(TerminalSession session, string key)
        {
            return new SendKeyCommand().ExecuteAsync(session, new CommandArgs().Set("key", key),
                TestContext.Current.CancellationToken);
        }

        [Theory]
        [InlineData("IBM.ENTER")]
        [InlineData("ibm.enter")]
        [InlineData("ENTER")]
        public async Task AnIbmKeyNameSendsTheRecord(string key)
        {
            var (session, connection) = await Open(new Ibm3270Emulator());
            connection.SimulateReceive(Screen);
            await session.FlushAsync();
            connection.ClearSentData();

            var result = await SendKey(session, key);

            Assert.True(result.Success, result.Error);
            var sent = connection.GetSentData();
            Assert.Single(sent);
            Assert.Equal(new byte[] { 0x7D, 0x40, 0xC6 }, sent[0]);
        }

        [Fact]
        public async Task Pf24SendsItsAid()
        {
            var (session, connection) = await Open(new Ibm3270Emulator());
            connection.SimulateReceive(Screen);
            await session.FlushAsync();
            connection.ClearSentData();

            var result = await SendKey(session, "IBM.PF24");

            Assert.True(result.Success, result.Error);
            Assert.Equal(Aid.Pf(24), connection.GetSentData()[0][0]);
        }

        [Fact]
        public async Task ATdvKeyOnA3270TabIsRefusedAndNothingIsSent()
        {
            var (session, connection) = await Open(new Ibm3270Emulator());
            connection.ClearSentData();

            var result = await SendKey(session, "TDV.HJELP");

            Assert.False(result.Success);
            Assert.Contains("TDV key", result.Error);
            Assert.Empty(connection.GetSentData());
        }

        [Fact]
        public async Task AnIbmKeyOnATdvTabIsRefusedAndNothingIsSent()
        {
            var (session, connection) = await Open(new TDV2200Emulator(80, 24));
            connection.ClearSentData();

            var result = await SendKey(session, "IBM.PF3");

            Assert.False(result.Success);
            Assert.Contains("IBM key", result.Error);
            Assert.Empty(connection.GetSentData());
        }

        [Fact]
        public async Task TheTdvFamilyPrefixStillSendsTheTdvKey()
        {
            var (session, connection) = await Open(new TDV2200Emulator(80, 24));
            connection.ClearSentData();

            var prefixed = await SendKey(session, "TDV.HJELP");
            string? withPrefix = connection.GetLastSentAsString();
            connection.ClearSentData();
            var bare = await SendKey(session, "HJELP");

            Assert.True(prefixed.Success, prefixed.Error);
            Assert.True(bare.Success, bare.Error);
            Assert.Equal(connection.GetLastSentAsString(), withPrefix);
        }

        [Fact]
        public async Task AKeyThatIsNotBuiltFailsAndSendsNothing()
        {
            var (session, connection) = await Open(new Ibm3270Emulator());
            connection.ClearSentData();

            var result = await SendKey(session, "IBM.ERASEEOF");

            Assert.False(result.Success);
            Assert.Contains("does not do yet", result.Error);
            Assert.Empty(connection.GetSentData());
        }

        [Fact]
        public async Task AnUnknownFamilyOrKeyIsRefusedByName()
        {
            var (session, _) = await Open(new Ibm3270Emulator());

            var family = await SendKey(session, "XYZ.PF3");
            var key = await SendKey(session, "IBM.PF99");

            Assert.False(family.Success);
            Assert.Contains("unknown key family", family.Error);
            Assert.False(key.Success);
            Assert.Contains("unknown IBM key", key.Error);
        }

        [Fact]
        public void EveryListedIbmKeyNameIsKnownToThePressTable()
        {
            // A name in the list that the press table does not know would be offered to scripts and
            // to the on-screen keyboard and then refused.
            var emulator = new Ibm3270Emulator();
            var names = emulator.KeyNames;
            for (int i = 0; i < names.Count; i++)
            {
                Assert.NotEqual(Core.Terminal.Input.NamedKeyResult.UnknownName, emulator.PressNamedKey(names[i], out _));
            }
        }
    }
}
