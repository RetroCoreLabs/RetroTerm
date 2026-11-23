//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs/RetroTerm
// RetroTerm — Connecting today's machines to yesterday's systems
//

using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;

using RetroIO.Protocols.Tn3270;

using RetroTerm.Core.Commands;
using RetroTerm.Core.Commands.Builtin;
using RetroTerm.Core.Session;
using RetroTerm.Ibm3270;
using RetroTerm.Tests.TDV;

using Xunit;

namespace RetroTerm.Tests.Ibm3270
{
    /// <summary>
    /// HOSTSEND and HOSTRECEIVE (IND$FILE) through a real session, with the host's side played from
    /// the records TK5 sent on 23 November 2026 (the guide 08 live test, Scenario08FileTransferTests).
    /// </summary>
    public class HostFileTransferCommandTests
    {
        // EW, WCC restore, SF protected "NAME", SF unprotected, IC: an input field from address 6
        // to the end of the screen, the cursor at its start.
        private static readonly byte[] Screen =
        {
            Command3270.EraseWrite, 0x02,
            Order3270.StartField, 0x60, 0xD5, 0xC1, 0xD4, 0xC5,
            Order3270.StartField, 0x40,
            Order3270.InsertCursor,
        };

        // EW, WCC restore, SF unprotected, five blanks, SF protected, IC at address 1: an input
        // field five characters long.
        private static readonly byte[] ShortField =
        {
            Command3270.EraseWrite, 0x02,
            Order3270.StartField, 0x40, 0x40, 0x40, 0x40, 0x40, 0x40,
            Order3270.StartField, 0x60,
            Order3270.SetBufferAddress, 0x40, 0xC1,
            Order3270.InsertCursor,
        };

        private static async Task<(TerminalSession Session, InMemoryConnection Connection, Ibm3270Emulator Emulator)> Open(byte[] screen)
        {
            var emulator = new Ibm3270Emulator();
            var session = new TerminalSession(emulator, "Test");
            var connection = new InMemoryConnection();
            await session.ConnectAsync(connection, TestContext.Current.CancellationToken);
            connection.SimulateReceive(screen);
            await session.FlushAsync();
            return (session, connection, emulator);
        }

        private static byte[] Hex(string text)
        {
            string[] parts = text.Split(' ');
            byte[] bytes = new byte[parts.Length];
            for (int i = 0; i < parts.Length; i++)
            {
                bytes[i] = Convert.ToByte(parts[i], 16);
            }

            return bytes;
        }

        // TK5's records for "IND$FILE PUT RTTFT.TEXT ASCII CRLF" after our Enter: Read Partition
        // Query, Open FT:DATA (upload), Set Cursor + Get twice (one record, then end of file),
        // Close, Open FT:MSG, the TRANS03 message (85 bytes: text, '$', blanks), then READY.
        private static byte[][] Tk5Upload()
        {
            byte[] head = Hex("F3 00 06 40 00 F1 C2 00 0A D0 47 11 01 05 00 80 00 00 5F D0 47 04 C0 80 61 00 5A");
            byte[] message = new byte[head.Length + 85];
            head.CopyTo(message, 0);
            byte[] text = Encoding.ASCII.GetBytes("TRANS03   File transfer complete$");
            text.CopyTo(message, head.Length);
            for (int i = head.Length + text.Length; i < message.Length; i++)
            {
                message[i] = 0x20;
            }

            return new[]
            {
                Hex("F5 C3 13"),
                Hex("F3 00 05 01 FF 02"),
                Hex("F3 00 06 40 00 F1 C2 00 29 D0 00 12 01 06 01 01 04 03 0A 0A 00 01 00 00 00 00 01 00 50 05 52 03 F0 08 06 27 04 3F EF 03 09 46 54 3A 44 41 54 41"),
                Hex("F3 00 06 40 00 F1 C2 00 0F D0 45 11 01 05 00 06 00 09 05 01 03 00 00 09 D0 46 11 01 04 00 80"),
                Hex("F3 00 06 40 00 F1 C2 00 0F D0 45 11 01 05 00 06 00 09 05 01 03 00 00 09 D0 46 11 01 04 00 80"),
                Hex("F3 00 06 40 00 F1 C2 00 05 D0 41 12"),
                Hex("F3 00 06 40 00 F1 C2 00 23 D0 00 12 01 06 01 01 04 03 0A 0A 00 00 00 00 11 01 01 00 50 05 52 03 F0 03 09 46 54 3A 4D 53 47 20"),
                message,
                Hex("F5 C3 11 5D 7F 1D 40 11 40 40 1D C8 D9 C5 C1 C4 E8 40 1D 40 11 C1 50 13"),
            };
        }

        [Fact]
        public async Task HostSendTypesTheCommandAndEndsOnTheHostsMessage()
        {
            var (session, connection, _) = await Open(Screen);
            string path = Path.Combine(Path.GetTempPath(), "rtt-hostsend-" + Guid.NewGuid().ToString("N") + ".txt");
            File.WriteAllText(path, "HELLO\r\n", Encoding.ASCII);
            try
            {
                Task<CommandResult> run = new HostSendCommand().ExecuteAsync(session,
                    new CommandArgs().Set("path", path).Set("dataset", "RTTFT.TEXT").Set("mode", "ascii").Set("options", "").Set("timeout", "10000"),
                    TestContext.Current.CancellationToken);
                await session.FlushAsync();

                // Enter with the command typed into the field: AID 7D, the cursor, then SBA to
                // address 6 and "IND$FILE PUT RTTFT.TEXT ASCII CRLF" in EBCDIC.
                byte[] enter = connection.GetSentData()[0];
                Assert.Equal(0x7D, enter[0]);
                Assert.Equal(Hex("11 40 C6 C9 D5 C4 5B C6 C9 D3 C5 40 D7 E4 E3 40 D9 E3 E3 C6 E3 4B E3 C5 E7 E3 40 C1 E2 C3 C9 C9 40 C3 D9 D3 C6"), enter.AsSpan(3).ToArray());

                byte[][] records = Tk5Upload();
                for (int i = 0; i < records.Length; i++)
                {
                    connection.SimulateReceive(records[i]);
                    await session.FlushAsync();
                }

                CommandResult result = await run;
                Assert.True(result.Success, result.Error);
                Assert.Equal("TRANS03   File transfer complete", result.Output);

                // The one record went up as it is on disk, bytes unchanged: in ASCII mode the
                // host translates (IND-FILE-TRANSFER.md 6.1).
                bool found = false;
                var sent = connection.GetSentData();
                for (int i = 0; i < sent.Count; i++)
                {
                    if (sent[i].Length > 4 && sent[i][3] == 0xD0 && sent[i][4] == 0x46 && sent[i][5] == 0x05)
                    {
                        Assert.Equal(Encoding.ASCII.GetBytes("HELLO\r\n"), sent[i].AsSpan(17).ToArray());
                        found = true;
                    }
                }

                Assert.True(found, "no upload data buffer was sent");
            }
            finally
            {
                File.Delete(path);
            }
        }

        // TK5's message record: the 85-byte TRANSxx text, '$', blanks (both directions end so).
        private static byte[] Tk5Message(string text)
        {
            byte[] head = Hex("F3 00 06 40 00 F1 C2 00 0A D0 47 11 01 05 00 80 00 00 5F D0 47 04 C0 80 61 00 5A");
            byte[] message = new byte[head.Length + 85];
            head.CopyTo(message, 0);
            byte[] bytes = Encoding.ASCII.GetBytes(text + "$");
            bytes.CopyTo(message, head.Length);
            for (int i = head.Length + bytes.Length; i < message.Length; i++)
            {
                message[i] = 0x20;
            }

            return message;
        }

        // TK5's records for "IND$FILE GET RTTFT.TEXT ASCII CRLF", 23 November 2026: Query, Open
        // FT:DATA (download, short form), one Data to Insert with both lines and their CR LF,
        // Close, Open FT:MSG, the message, READY.
        private static byte[][] Tk5Download(string message)
        {
            return new[]
            {
                Hex("F5 C3 13"),
                Hex("F3 00 05 01 FF 02"),
                Hex("F3 00 06 40 00 F1 C2 00 23 D0 00 12 01 06 01 01 04 03 0A 0A 00 00 00 00 11 01 01 00 50 05 52 03 F0 03 09 46 54 3A 44 41 54 41"),
                Hex("F3 00 06 40 00 F1 C2 00 0A D0 47 11 01 05 00 80 00 00 36 D0 47 04 C0 80 61 00 31 48 45 4C 4C 4F 20 46 52 4F 4D 20 52 45 54 52 4F 54 45 52 4D 0D 0A 4C 49 4E 45 20 54 57 4F 20 4F 46 20 54 48 45 20 54 45 53 54 0D 0A"),
                Hex("F3 00 06 40 00 F1 C2 00 05 D0 41 12"),
                Hex("F3 00 06 40 00 F1 C2 00 23 D0 00 12 01 06 01 01 04 03 0A 0A 00 00 00 00 11 01 01 00 50 05 52 03 F0 03 09 46 54 3A 4D 53 47 20"),
                Tk5Message(message),
                Hex("F5 C3 11 5D 7F 1D 40 11 40 40 1D C8 D9 C5 C1 C4 E8 40 1D 40 11 C1 50 13"),
            };
        }

        private static async Task<CommandResult> ReceiveAsync(string path, string message)
        {
            var (session, connection, _) = await Open(Screen);
            Task<CommandResult> run = new HostReceiveCommand().ExecuteAsync(session,
                new CommandArgs().Set("path", path).Set("dataset", "RTTFT.TEXT").Set("mode", "ascii").Set("options", "").Set("timeout", "10000"),
                TestContext.Current.CancellationToken);
            await session.FlushAsync();
            byte[][] records = Tk5Download(message);
            for (int i = 0; i < records.Length; i++)
            {
                connection.SimulateReceive(records[i]);
                await session.FlushAsync();
            }

            return await run;
        }

        [Fact]
        public async Task HostReceiveWritesTheFileWhenTheHostSaysComplete()
        {
            string path = Path.Combine(Path.GetTempPath(), "rtt-hostreceive-" + Guid.NewGuid().ToString("N") + ".txt");
            try
            {
                CommandResult result = await ReceiveAsync(path, "TRANS03   File transfer complete");

                Assert.True(result.Success, result.Error);
                Assert.Equal("HELLO FROM RETROTERM\r\nLINE TWO OF THE TEST\r\n", File.ReadAllText(path, Encoding.ASCII));
                Assert.False(File.Exists(path + ".part"));
            }
            finally
            {
                File.Delete(path);
                File.Delete(path + ".part");
            }
        }

        [Fact]
        public async Task AFailedReceiveLeavesTheOldFileAloneAndNoPartFile()
        {
            string path = Path.Combine(Path.GetTempPath(), "rtt-hostreceive-" + Guid.NewGuid().ToString("N") + ".txt");
            File.WriteAllText(path, "OLD", Encoding.ASCII);
            try
            {
                // Any message but TRANS03 or TRANS04 is a failure (IND-FILE-TRANSFER.md 3.13); the
                // data that did arrive must not replace the file that was there.
                CommandResult result = await ReceiveAsync(path, "TRANS17   Missing or incorrect TSO data set name");

                Assert.False(result.Success);
                Assert.StartsWith("TRANS17", result.Error);
                Assert.Equal("OLD", File.ReadAllText(path, Encoding.ASCII));
                Assert.False(File.Exists(path + ".part"));
            }
            finally
            {
                File.Delete(path);
                File.Delete(path + ".part");
            }
        }

        [Fact]
        public async Task HostSendRefusesACommandLongerThanTheField()
        {
            var (session, connection, _) = await Open(ShortField);
            string path = Path.Combine(Path.GetTempPath(), "rtt-hostsend-" + Guid.NewGuid().ToString("N") + ".txt");
            File.WriteAllText(path, "X", Encoding.ASCII);
            try
            {
                CommandResult result = await new HostSendCommand().ExecuteAsync(session,
                    new CommandArgs().Set("path", path).Set("dataset", "A").Set("mode", "ascii").Set("options", "").Set("timeout", "1000"),
                    TestContext.Current.CancellationToken);

                // A command cut off at the field's end would run on the host with another meaning
                // (IND-FILE-TRANSFER.md 2.5), so nothing is typed and nothing is sent.
                Assert.False(result.Success);
                Assert.Contains("input field holds 5", result.Error);
                Assert.Empty(connection.GetSentData());
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        public async Task HostSendRefusesWhileTheKeyboardIsLocked()
        {
            var (session, connection, emulator) = await Open(Screen);
            Assert.True(emulator.HandleKey("Enter", false, false, false));
            await session.FlushAsync();
            connection.ClearSentData();
            string path = Path.Combine(Path.GetTempPath(), "rtt-hostsend-" + Guid.NewGuid().ToString("N") + ".txt");
            File.WriteAllText(path, "X", Encoding.ASCII);
            try
            {
                CommandResult result = await new HostSendCommand().ExecuteAsync(session,
                    new CommandArgs().Set("path", path).Set("dataset", "A").Set("mode", "ascii").Set("options", "").Set("timeout", "1000"),
                    TestContext.Current.CancellationToken);

                Assert.False(result.Success);
                Assert.Contains("keyboard is locked", result.Error);
                Assert.Empty(connection.GetSentData());
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        public async Task AnUnknownModeIsRefusedByName()
        {
            var (session, _, _) = await Open(Screen);

            CommandResult result = await new HostReceiveCommand().ExecuteAsync(session,
                new CommandArgs().Set("path", Path.Combine(Path.GetTempPath(), "x.txt")).Set("dataset", "A").Set("mode", "text").Set("options", "").Set("timeout", "1000"),
                TestContext.Current.CancellationToken);

            Assert.False(result.Success);
            Assert.Contains("mode must be ascii or binary, not 'text'", result.Error);
        }

        [Fact]
        public void TheHostCommandIsBuiltFromTheParameters()
        {
            Assert.Equal("IND$FILE PUT HELLO.TEXT ASCII CRLF", HostFileTransfer.Command(true, "HELLO.TEXT", "ascii", ""));
            Assert.Equal("IND$FILE GET 'SYS2.JCLLIB(X)'", HostFileTransfer.Command(false, "'SYS2.JCLLIB(X)'", "binary", ""));
            Assert.Equal("IND$FILE PUT A ASCII CRLF RECFM(F) LRECL(80)", HostFileTransfer.Command(true, "A", "ascii", "RECFM(F) LRECL(80)"));
        }
    }
}
