//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs/RetroTerm
// RetroTerm — Connecting today's machines to yesterday's systems
//

using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using RetroIO.Protocols.Tn3270;

using RetroTerm.Core.Commands.Builtin;
using RetroTerm.Core.Session;
using RetroTerm.Ibm3270;
using RetroTerm.Tests.TDV;

using Xunit;

namespace RetroTerm.Tests.Ibm3270
{
    /// <summary>
    /// The rules behind the IBM Host Files window, all in HostFileTransfer (RetroTerm.Core): the
    /// READY check on real 3270 screens, the host command for View, Download and Upload, and the
    /// Clear after a transfer. The host's side is played from the records TK5 sent on 23
    /// November 2026 (HostFileTransferCommandTests).
    /// </summary>
    public class HostFilesTests
    {
        // TK5's READY, the last record of every transfer it ran (23 November 2026): EW, WCC
        // restore, SBA to the last address, SF, SBA 0, SF, "READY ", SF, SBA to row 1 column 0,
        // IC. READY on row 0, the cursor at the start of row 1.
        internal static readonly byte[] Ready = HostFileTransferCommandTests.Hex("F5 C3 11 5D 7F 1D 40 11 40 40 1D C8 D9 C5 C1 C4 E8 40 1D 40 11 C1 50 13");

        // EW, WCC restore, IC: the blank screen TSO leaves after Clear at READY (measured on TK5,
        // cursor at row 0, column 0).
        private static readonly byte[] Cleared = HostFileTransferCommandTests.Hex("F5 C3 13");

        // READY, then an error message on row 1, the cursor on row 2: READY is not the last line.
        private static byte[] ReadyThenMessage()
        {
            var bytes = new System.Collections.Generic.List<byte> { Command3270.EraseWrite, 0xC3, Order3270.SetBufferAddress, 0x40, 0x40 };
            bytes.AddRange(Ebcdic("READY"));
            bytes.Add(Order3270.SetBufferAddress);
            bytes.Add(0xC1);
            bytes.Add(0x50); // row 1
            bytes.AddRange(Ebcdic("COMMAND XYZ NOT FOUND"));
            bytes.Add(Order3270.SetBufferAddress);
            bytes.Add(0xC2);
            bytes.Add(0x60); // row 2
            bytes.Add(Order3270.InsertCursor);
            return bytes.ToArray();
        }

        // Upper-case letters and blanks in EBCDIC, enough for the screens above.
        private static byte[] Ebcdic(string text)
        {
            byte[] bytes = new byte[text.Length];
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                bytes[i] = c == ' ' ? (byte)0x40
                    : c <= 'I' ? (byte)(0xC1 + (c - 'A'))
                    : c <= 'R' ? (byte)(0xD1 + (c - 'J'))
                    : (byte)(0xE2 + (c - 'S'));
            }

            return bytes;
        }

        [Fact]
        public async Task TsosReadyIsReady()
        {
            var (session, _, _) = await HostFileTransferCommandTests.Open(Ready);

            Assert.True(await HostFileTransfer.IsAtReadyAsync(session));
        }

        [Fact]
        public async Task ReadyWithSomethingTypedOnTheCursorsRowIsReady()
        {
            var (session, _, emulator) = await HostFileTransferCommandTests.Open(Ready);
            Assert.True(emulator.HandleText("LISTCAT"));
            await session.FlushAsync();

            Assert.True(await HostFileTransfer.IsAtReadyAsync(session));
        }

        [Fact]
        public async Task TheBlankScreenAfterClearIsReady()
        {
            var (session, _, _) = await HostFileTransferCommandTests.Open(Cleared);

            Assert.True(await HostFileTransfer.IsAtReadyAsync(session));
        }

        [Fact]
        public async Task AFullScreenPanelIsNotReady()
        {
            var (session, _, _) = await HostFileTransferCommandTests.Open(HostFileTransferCommandTests.Screen);

            Assert.False(await HostFileTransfer.IsAtReadyAsync(session));
        }

        [Fact]
        public async Task ReadyFollowedByAnotherLineIsNotReady()
        {
            var (session, _, _) = await HostFileTransferCommandTests.Open(ReadyThenMessage());

            Assert.False(await HostFileTransfer.IsAtReadyAsync(session));
        }

        [Fact]
        public async Task ReadyWithTheKeyboardLockedIsNotReady()
        {
            var (session, _, emulator) = await HostFileTransferCommandTests.Open(Ready);
            Assert.True(emulator.HandleKey("Enter", false, false, false));
            await session.FlushAsync();

            Assert.False(await HostFileTransfer.IsAtReadyAsync(session));
        }

        [Fact]
        public void TheRuleOnPlainRows()
        {
            Assert.True(HostFileTransfer.IsAtReady(new[] { "READY", "", "" }, 1, false));
            Assert.True(HostFileTransfer.IsAtReady(new[] { "  READY  ", "LISTCAT", "" }, 1, false));
            Assert.True(HostFileTransfer.IsAtReady(new[] { "", "", "" }, 0, false));
            Assert.False(HostFileTransfer.IsAtReady(new[] { "", "", "" }, 0, true));
            Assert.False(HostFileTransfer.IsAtReady(new[] { "READY", "", "" }, 1, true));
            Assert.False(HostFileTransfer.IsAtReady(new[] { "READY", "", "" }, 0, false));
            Assert.False(HostFileTransfer.IsAtReady(new[] { "READY", "IKJ56500I", "" }, 2, false));
            Assert.False(HostFileTransfer.IsAtReady(new[] { "ISPF PRIMARY OPTION MENU", "OPTION ===>", "" }, 1, false));
            Assert.False(HostFileTransfer.IsAtReady(new[] { "NOT READY", "", "" }, 1, false));
        }

        [Fact]
        public void TheUploadRecordOptionsAreBuiltAndChecked()
        {
            Assert.Null(HostFileTransfer.PutOptions("A.TEXT", "", " ", null, out string none));
            Assert.Equal(string.Empty, none);

            Assert.Null(HostFileTransfer.PutOptions("A.TEXT", "f", "80", "3120", out string all));
            Assert.Equal("RECFM(F) LRECL(80) BLKSIZE(3120)", all);

            Assert.Null(HostFileTransfer.PutOptions("A.TEXT", "", "255", "", out string one));
            Assert.Equal("LRECL(255)", one);

            Assert.Equal("RECFM must be F, V or U, not FB.", HostFileTransfer.PutOptions("A.TEXT", "FB", "", "", out _));
            Assert.Equal("LRECL must be a number, not 8O.", HostFileTransfer.PutOptions("A.TEXT", "", "8O", "", out _));
            Assert.Equal("BLKSIZE must be from 1 to 32760, not 40000.", HostFileTransfer.PutOptions("A.TEXT", "", "", "40000", out _));
            Assert.Equal("LRECL must be from 1 to 32760, not 0.", HostFileTransfer.PutOptions("A.TEXT", "", "0", "", out _));

            // Only ASCII and CRLF are valid for a member (IND-FILE-TRANSFER.md section 2.1).
            Assert.Equal("A member takes no RECFM, LRECL or BLKSIZE; leave them empty.", HostFileTransfer.PutOptions("'HERC01.DEMO.CNTL(HELLO)'", "F", "", "", out _));
        }

        [Fact]
        public void TheHostCommandForEachAction()
        {
            Assert.Equal("IND$FILE GET HERC01.DEMO.CNTL(HELLO) ASCII CRLF", HostFileTransfer.Command(false, "HERC01.DEMO.CNTL(HELLO)", "ascii", string.Empty));
            Assert.Equal("IND$FILE GET 'SYS1.LINKLIB.DUMP'", HostFileTransfer.Command(false, "'SYS1.LINKLIB.DUMP'", "binary", string.Empty));
            Assert.Equal("IND$FILE PUT NEW.DATA RECFM(U) BLKSIZE(6144)", HostFileTransfer.Command(true, "NEW.DATA", "binary", "RECFM(U) BLKSIZE(6144)"));
            Assert.Equal("IND$FILE PUT NEW.TEXT ASCII CRLF RECFM(F) LRECL(80)", HostFileTransfer.Command(true, "NEW.TEXT", "ascii", "RECFM(F) LRECL(80)"));
        }

        [Fact]
        public void ADataSetNameIsTrimmedAndMayNotHoldABlank()
        {
            Assert.Null(HostFileTransfer.CheckDataSetName("  'HERC01.DEMO.CNTL(HELLO)' ", out string name));
            Assert.Equal("'HERC01.DEMO.CNTL(HELLO)'", name);
            Assert.Equal("Give the host data set name.", HostFileTransfer.CheckDataSetName("   ", out _));
            Assert.Equal("A data set name has no blanks in it: A.B RECFM(F)", HostFileTransfer.CheckDataSetName("A.B RECFM(F)", out _));
        }

        [Fact]
        public async Task EveryActionIsRefusedAwayFromReadyAndNothingIsSent()
        {
            var (session, connection, _) = await HostFileTransferCommandTests.Open(HostFileTransferCommandTests.Screen);
            string path = Path.Combine(Path.GetTempPath(), "rtt-hostfiles-" + Guid.NewGuid().ToString("N") + ".txt");
            File.WriteAllText(path, "X", Encoding.ASCII);
            try
            {
                CancellationToken token = TestContext.Current.CancellationToken;
                HostFilesOutcome view = await HostFileTransfer.ViewAsync(session, "A.TEXT", 1000, token);
                HostFilesOutcome down = await HostFileTransfer.DownloadAsync(session, "A.TEXT", false, path + ".down", 1000, token);
                HostFilesOutcome up = await HostFileTransfer.UploadAsync(session, "A.TEXT", false, path, "", "", "", 1000, token);

                Assert.Equal(HostFileTransfer.NotAtReadyMessage, view.Message);
                Assert.Equal(HostFileTransfer.NotAtReadyMessage, down.Message);
                Assert.Equal(HostFileTransfer.NotAtReadyMessage, up.Message);
                Assert.False(view.Success || down.Success || up.Success);
                Assert.Empty(connection.GetSentData());
                Assert.False(File.Exists(path + ".down") || File.Exists(path + ".down.part"));
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        public async Task AViewCancelledBeforeItStartsTypesNothing()
        {
            // At READY, but already cancelled: the IND$FILE command must not be typed.
            var (session, connection, _) = await HostFileTransferCommandTests.Open(Ready);
            using var cancelled = new CancellationTokenSource();
            cancelled.Cancel();

            HostFilesOutcome view = await HostFileTransfer.ViewAsync(session, "A.TEXT", 1000, cancelled.Token);

            Assert.False(view.Success);
            Assert.StartsWith("Cancelled.", view.Message, StringComparison.Ordinal);
            Assert.Empty(connection.GetSentData());
        }

        [Fact]
        public async Task AMemberCannotGoAsBinary()
        {
            var (session, connection, _) = await HostFileTransferCommandTests.Open(Ready);

            HostFilesOutcome down = await HostFileTransfer.DownloadAsync(session, "HERC01.DEMO.CNTL(HELLO)", true, Path.Combine(Path.GetTempPath(), "x.bin"), 1000, TestContext.Current.CancellationToken);

            Assert.False(down.Success);
            Assert.Equal("A member can only be moved as Text.", down.Message);
            Assert.Empty(connection.GetSentData());
        }

        [Fact]
        public async Task UploadTypesTheCommandWithTheRecordOptionsAndPressesClearAfterwards()
        {
            var (session, connection, emulator) = await HostFileTransferCommandTests.Open(Ready);
            string path = Path.Combine(Path.GetTempPath(), "rtt-hostfiles-" + Guid.NewGuid().ToString("N") + ".txt");
            File.WriteAllText(path, "HELLO\r\n", Encoding.ASCII);
            try
            {
                Task<HostFilesOutcome> run = HostFileTransfer.UploadAsync(session, " NEW.TEXT ", false, path, "f", "80", "", 10000, TestContext.Current.CancellationToken);
                await WaitForSentAsync(connection, 1);

                // The command goes at the start of the input field the cursor is in, which TK5's
                // READY record starts right after "READY " on row 0 (StartHostFileTransfer).
                ScreenSnapshot typed = await session.ReadScreenAsync(stripTrailingBlanks: true);
                Assert.Equal("READY  IND$FILE PUT NEW.TEXT ASCII CRLF RECFM(F) LRECL(80)", typed.Text.Split('\n')[0].Trim());
                Assert.Equal(0x7D, connection.GetSentData()[0][0]);

                // Everything up to and with the TRANS03 message, but not TSO's READY after it.
                byte[][] records = HostFileTransferCommandTests.Tk5Upload();
                byte[][] allButReady = new byte[records.Length - 1][];
                Array.Copy(records, allButReady, allButReady.Length);
                await PlayAsync(session, connection, allButReady);

                // No Clear before READY: on TK5 a Clear sent straight after TRANS03 went out ahead
                // of TSO's READY, which then landed on the cleared screen. The old code sent it
                // within one poll of the message; half a second without one is the check.
                var quiet = Stopwatch.StartNew();
                while (quiet.ElapsedMilliseconds < 500)
                {
                    Assert.False(LastSentIsClear(connection), "Clear was pressed before TSO showed READY");
                    await Task.Delay(10, TestContext.Current.CancellationToken);
                }

                await PlayAsync(session, connection, new[] { records[records.Length - 1] });
                await AnswerClearAsync(session, connection);

                HostFilesOutcome outcome = await run;
                Assert.True(outcome.Success, outcome.Message);
                Assert.Equal("TRANS03   File transfer complete", outcome.Message);
                Assert.True(await HostFileTransfer.IsAtReadyAsync(session), "not back at an empty READY");
                Assert.Null(emulator.KeyboardLockReason);
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        public async Task ViewReturnsTheTextAndLeavesNoFile()
        {
            var (session, connection, _) = await HostFileTransferCommandTests.Open(Ready);
            string before = string.Join("|", Directory.GetFiles(Path.GetTempPath(), "*.part"));

            Task<HostFilesOutcome> run = HostFileTransfer.ViewAsync(session, "RTTFT.TEXT", 10000, TestContext.Current.CancellationToken);
            await WaitForSentAsync(connection, 1);
            ScreenSnapshot typed = await session.ReadScreenAsync(stripTrailingBlanks: true);
            Assert.Equal("READY  IND$FILE GET RTTFT.TEXT ASCII CRLF", typed.Text.Split('\n')[0].Trim());

            await PlayAsync(session, connection, HostFileTransferCommandTests.Tk5Download("TRANS03   File transfer complete"));
            await AnswerClearAsync(session, connection);

            HostFilesOutcome outcome = await run;
            Assert.True(outcome.Success, outcome.Message);
            Assert.Equal("HELLO FROM RETROTERM\r\nLINE TWO OF THE TEST\r\n", outcome.Text);
            Assert.Equal(before, string.Join("|", Directory.GetFiles(Path.GetTempPath(), "*.part")));
        }

        [Fact]
        public async Task DownloadInBinaryTypesNoAsciiAndWritesTheFile()
        {
            var (session, connection, _) = await HostFileTransferCommandTests.Open(Ready);
            string path = Path.Combine(Path.GetTempPath(), "rtt-hostfiles-" + Guid.NewGuid().ToString("N") + ".bin");
            try
            {
                Task<HostFilesOutcome> run = HostFileTransfer.DownloadAsync(session, "'SYS2.DATA'", true, path, 10000, TestContext.Current.CancellationToken);
                await WaitForSentAsync(connection, 1);
                ScreenSnapshot typed = await session.ReadScreenAsync(stripTrailingBlanks: true);
                Assert.Equal("READY  IND$FILE GET 'SYS2.DATA'", typed.Text.Split('\n')[0].Trim());

                await PlayAsync(session, connection, HostFileTransferCommandTests.Tk5Download("TRANS03   File transfer complete"));
                await AnswerClearAsync(session, connection);

                HostFilesOutcome outcome = await run;
                Assert.True(outcome.Success, outcome.Message);
                Assert.True(File.Exists(path));
                Assert.False(File.Exists(path + ".part"));
            }
            finally
            {
                File.Delete(path);
                File.Delete(path + ".part");
            }
        }

        [Fact]
        public async Task AFailedTransferStillClearsAndShowsTheHostsMessage()
        {
            var (session, connection, _) = await HostFileTransferCommandTests.Open(Ready);

            Task<HostFilesOutcome> run = HostFileTransfer.ViewAsync(session, "NO.SUCH", 10000, TestContext.Current.CancellationToken);
            await WaitForSentAsync(connection, 1);
            await PlayAsync(session, connection, HostFileTransferCommandTests.Tk5Download("TRANS17   Missing or incorrect TSO data set name"));
            await AnswerClearAsync(session, connection);

            HostFilesOutcome outcome = await run;
            Assert.False(outcome.Success);
            Assert.StartsWith("TRANS17", outcome.Message, StringComparison.Ordinal);
            Assert.Null(outcome.Text);
        }

        private static async Task PlayAsync(TerminalSession session, InMemoryConnection connection, byte[][] records)
        {
            for (int i = 0; i < records.Length; i++)
            {
                connection.SimulateReceive(records[i]);
                await session.FlushAsync();
            }
        }

        // The Clear goes out as AID 6D (IND-FILE-TRANSFER.md, the Clear row of the table). The
        // host answers with a blank screen and an unlocked keyboard, as TK5 did.
        private static async Task AnswerClearAsync(TerminalSession session, InMemoryConnection connection)
        {
            var clock = Stopwatch.StartNew();
            while (!LastSentIsClear(connection))
            {
                Assert.True(clock.ElapsedMilliseconds < 10000, "Clear was not pressed after the transfer");
                await Task.Delay(10, TestContext.Current.CancellationToken);
            }

            connection.SimulateReceive(Cleared);
            await session.FlushAsync();
        }

        private static bool LastSentIsClear(InMemoryConnection connection)
        {
            var sent = connection.GetSentData();
            return sent.Count > 0 && sent[sent.Count - 1].Length > 0 && sent[sent.Count - 1][0] == 0x6D;
        }

        private static async Task WaitForSentAsync(InMemoryConnection connection, int count)
        {
            var clock = Stopwatch.StartNew();
            while (connection.GetSentData().Count < count)
            {
                Assert.True(clock.ElapsedMilliseconds < 10000, "nothing was sent");
                await Task.Delay(10, TestContext.Current.CancellationToken);
            }
        }
    }
}
