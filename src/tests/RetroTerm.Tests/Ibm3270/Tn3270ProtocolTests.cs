//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs/RetroTerm
// RetroTerm — Connecting today's machines to yesterday's systems
//

using System;
using System.Collections.Generic;

using RetroIO.Protocols.Tn3270;
using RetroTerm.Core.Configuration;
using RetroTerm.Core.Protocols;
using RetroTerm.Ibm3270;

using Xunit;

namespace RetroTerm.Tests.Ibm3270
{
    public class Tn3270ProtocolTests
    {
        // The host side of the TK5 handshake, byte for byte, from
        // docs\spec\IBM3270\TN3270-NEGOTIATION.md section 3, then a short Erase/Write record.
        private static readonly byte[] HostBytes =
        {
            0xFF, 0xFD, 0x18,
            0xFF, 0xFA, 0x18, 0x01, 0xFF, 0xF0,
            0xFF, 0xFB, 0x00,
            0xFF, 0xFD, 0x00,
            0xFF, 0xFB, 0x19, 0xFF, 0xFD, 0x19,
            0xF5, 0x42, 0x11, 0x40, 0x40, 0x1D, 0x60, 0xC8, 0x85,
            0xFF, 0xEF,
        };

        // What the client that worked against TK5 sent back, in the same order (section 3).
        private static readonly byte[] ExpectedClientBytes =
        {
            0xFF, 0xFB, 0x18,
            // "IBM-3278-2-E": the s3270 trace of 22 November 2026 sends this, and the -E is what makes
            // TSO ask for a Query Reply; without it TSO answers the first Enter with IKT00405I.
            0xFF, 0xFA, 0x18, 0x00, 0x49, 0x42, 0x4D, 0x2D, 0x33, 0x32, 0x37, 0x38, 0x2D, 0x32, 0x2D, 0x45, 0xFF, 0xF0,
            0xFF, 0xFD, 0x00,
            0xFF, 0xFB, 0x00,
            0xFF, 0xFD, 0x19,
            0xFF, 0xFB, 0x19,
        };

        private static (Tn3270Protocol Protocol, List<byte> Wire, List<byte[]> Records, List<string> Errors) Create()
        {
            var wire = new List<byte>();
            var records = new List<byte[]>();
            var errors = new List<string>();
            var p = new Tn3270Protocol(bytes => wire.AddRange(bytes.ToArray()));
            p.RecordReceived += r => records.Add(r.ToArray());
            p.ProtocolError += errors.Add;
            return (p, wire, records, errors);
        }

        [Fact]
        public void TheTk5HandshakeGetsTheSameAnswersAsTheClientThatWorked()
        {
            var (p, wire, records, errors) = Create();

            p.Feed(HostBytes);

            Assert.Equal(ExpectedClientBytes, wire.ToArray());
            Assert.True(p.In3270Mode);
            Assert.Empty(errors);
            Assert.Single(records);
            Assert.Equal(new byte[] { 0xF5, 0x42, 0x11, 0x40, 0x40, 0x1D, 0x60, 0xC8, 0x85 }, records[0]);
        }

        [Fact]
        public void TheSameBytesSplitAtEveryPositionGiveTheSameResult()
        {
            for (int cut = 1; cut < HostBytes.Length; cut++)
            {
                var (p, wire, records, _) = Create();

                p.Feed(HostBytes.AsSpan(0, cut));
                p.Feed(HostBytes.AsSpan(cut));

                Assert.Equal(ExpectedClientBytes, wire.ToArray());
                Assert.Single(records);
            }
        }

        [Fact]
        public void Tn3270eIsRefused()
        {
            // TK5 never asks; a host that does must get WONT, since RFC 2355 is not built.
            var (p, wire, _, _) = Create();

            p.Feed(new byte[] { 0xFF, 0xFD, 0x28 });

            Assert.Equal(new byte[] { 0xFF, 0xFC, 0x28 }, wire.ToArray());
        }

        [Fact]
        public void AnOutgoingRecordIsEscapedAndEndsWithIacEor()
        {
            var (p, wire, _, _) = Create();

            p.SendRecord(new byte[] { 0x7D, 0xFF, 0x40 });

            Assert.Equal(new byte[] { 0x7D, 0xFF, 0xFF, 0x40, 0xFF, 0xEF }, wire.ToArray());
        }

        [Fact]
        public void ARecordBefore3270ModeIsReportedNotShown()
        {
            var (p, _, records, errors) = Create();

            p.Feed(new byte[] { 0xF5, 0x42, 0xFF, 0xEF });

            Assert.Empty(records);
            Assert.Single(errors);
        }

        [Theory]
        [InlineData(ConnectionFactory.ProtocolType.TN3270, "VT220", "IBM-3278-2")]
        [InlineData(ConnectionFactory.ProtocolType.TN3270, null, "IBM-3278-2")]
        [InlineData(ConnectionFactory.ProtocolType.TN3270, "IBM-3278-5", "IBM-3278-5")]
        [InlineData(ConnectionFactory.ProtocolType.TN3270, "ibm-3278-4", "IBM-3278-4")]
        [InlineData(ConnectionFactory.ProtocolType.Telnet, "VT220", "VT220")]
        [InlineData(ConnectionFactory.ProtocolType.Telnet, null, "VT100")]
        public void Tn3270AlwaysGetsThe3278AndOtherProtocolsKeepTheirChoice(
            ConnectionFactory.ProtocolType protocol, string? chosen, string expected)
        {
            Assert.Equal(expected, ConnectionFactory.EmulatorTypeFor(protocol, chosen));
        }

        [Fact]
        public void TheFactoriesBuildTheTn3270PairByName()
        {
            var connection = ConnectionFactory.CreateConnection(new ConnectionFactory.ConnectionParameters
            {
                Protocol = ConnectionFactory.ProtocolType.TN3270,
                Host = "localhost",
                Port = 3270,
            });
            var emulator = EmulatorFactory.CreateEmulator("IBM-3278-2");

            Assert.IsType<Tn3270Connection>(connection);
            Assert.IsType<Ibm3270Emulator>(emulator);
            connection.Dispose();
        }

        [Theory]
        [InlineData("IBM-3278-2", 2, 24, 80, "IBM-3278-2-E")]
        [InlineData("IBM-3278-3", 3, 32, 80, "IBM-3278-3-E")]
        [InlineData("IBM-3278-4", 4, 43, 80, "IBM-3278-4-E")]
        [InlineData("IBM-3278-5", 5, 27, 132, "IBM-3278-5-E")]
        public void EachModelGetsItsSizeAndAnnouncesItsName(string emulatorType, int model, int rows, int columns, string terminalType)
        {
            // Sizes from RFC 1576 section 4 and x3270 include\3270ds.h; the status row sits under them.
            var connection = (Tn3270Connection)ConnectionFactory.CreateConnection(new ConnectionFactory.ConnectionParameters
            {
                Protocol = ConnectionFactory.ProtocolType.TN3270,
                Host = "localhost",
                Port = 3270,
                EmulatorType = emulatorType,
            });
            var emulator = (Ibm3270Emulator)EmulatorFactory.CreateEmulator(emulatorType);

            Assert.Equal(terminalType, connection.TerminalType);
            Assert.Equal(model, emulator.Model);
            Assert.Equal(columns, emulator.Buffer.Width);
            Assert.Equal(rows + 1, emulator.Buffer.Height);
            connection.Dispose();
        }

        [Fact]
        public void TheDefaultTerminalTypeIsTheModel2Name()
        {
            // Two places hold the name: the protocol's default and TerminalTypeFor. They must agree.
            Assert.Equal(Tn3270Protocol.DefaultTerminalType, Ibm3270Emulator.TerminalTypeFor(2));
        }

        [Fact]
        public void AModel4ShowsTheAlternateSizeAfterEraseWriteAlternate()
        {
            // EWA (X'7E') switches to 43 x 80 (GA23-0059-4 p. 3-5): an A at address 3439 (row 43,
            // column 80; SBA X'F5' X'6F', 12-bit coded: 53 x 64 + 47) shows,
            // and line 44 is the status line. EW (X'F5') goes back to 24 x 80 and blanks row 43.
            var emulator = new Ibm3270Emulator(4);
            emulator.ProcessData(new byte[] { Command3270.EraseWriteAlternate, 0x02, Order3270.SetBufferAddress, 0xF5, 0x6F, 0xC1 });
            Assert.Equal(43, emulator.Display.Rows);
            Assert.Equal("A", emulator.Buffer.GetCell(42, 79).GetString());
            Assert.Equal("4", emulator.Buffer.GetCell(43, 0).GetString());

            emulator.ProcessData(new byte[] { Command3270.EraseWrite, 0x02 });
            Assert.Equal(24, emulator.Display.Rows);
            Assert.Equal(" ", emulator.Buffer.GetCell(42, 79).GetString());
            Assert.Equal("4", emulator.Buffer.GetCell(43, 0).GetString());
        }
    }
}
