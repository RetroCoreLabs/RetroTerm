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

        // The TN3270E exchange of RFC 2355 section 7, as docs\spec\IBM3270\TN3270-NEGOTIATION.md
        // section 4 lists it from the RFC, x3270 and tnz: DO TN3270E, SEND DEVICE-TYPE, then IS
        // with the LU the host gave, then FUNCTIONS. TK5 never offers TN3270E, so these bytes are
        // the specification's, not a capture.
        private static readonly byte[] DoTn3270E = { 0xFF, 0xFD, 0x28 };
        private static readonly byte[] SendDeviceType = { 0xFF, 0xFA, 0x28, 0x08, 0x02, 0xFF, 0xF0 };

        // DEVICE-TYPE IS IBM-3278-2-E CONNECT TCP00034
        private static readonly byte[] DeviceTypeIs = Bytes("FF FA 28 02 04", "'IBM-3278-2-E", "01", "'TCP00034", "FF F0");

        // Hex pieces, with a text piece written as "'TEXT" (its ASCII bytes), joined in order.
        private static byte[] Bytes(params string[] parts)
        {
            var all = new List<byte>();
            for (int i = 0; i < parts.Length; i++)
            {
                if (parts[i].StartsWith('\''))
                {
                    all.AddRange(System.Text.Encoding.ASCII.GetBytes(parts[i].Substring(1)));
                }
                else
                {
                    all.AddRange(Convert.FromHexString(parts[i].Replace(" ", string.Empty, StringComparison.Ordinal)));
                }
            }

            return all.ToArray();
        }

        [Fact]
        public void BasicTn3270eIsNegotiatedAsTheRfcAndX3270DoIt()
        {
            var (p, wire, _, errors) = Create();
            var changes = new List<bool>();
            p.Tn3270EChanged += changes.Add;

            p.Feed(DoTn3270E);
            Assert.Equal(Bytes("FF FB 28"), wire.ToArray());

            wire.Clear();
            p.Feed(SendDeviceType);
            Assert.Equal(Bytes("FF FA 28 02 07", "'IBM-3278-2-E", "FF F0"), wire.ToArray());

            // Granted: the client asks for BIND-IMAGE, RESPONSES and SYSREQ (RFC 2355 s.7.2,
            // s.10.3 to s.10.5; functions X'00', X'02', X'04'). The host here grants none, which is
            // basic TN3270E (RFC 2355 s.9).
            wire.Clear();
            p.Feed(DeviceTypeIs);
            Assert.Equal(Bytes("FF FA 28 03 07 00 02 04 FF F0"), wire.ToArray());
            Assert.Equal("IBM-3278-2-E", p.GrantedDeviceType);
            Assert.Equal("TCP00034", p.GrantedLuName);
            Assert.False(p.InTn3270E);

            wire.Clear();
            p.Feed(Bytes("FF FA 28 03 04 FF F0"));
            Assert.Empty(wire);
            Assert.True(p.InTn3270E);
            Assert.True(p.In3270Mode);
            Assert.Equal(new[] { true }, changes);
            Assert.Empty(errors);
        }

        [Fact]
        public void TheWholeExchangeFedOneByteAtATimeEndsTheSame()
        {
            var (p, _, records, errors) = Create();
            var all = new List<byte>();
            all.AddRange(DoTn3270E);
            all.AddRange(SendDeviceType);
            all.AddRange(DeviceTypeIs);
            all.AddRange(Bytes("FF FA 28 03 04 FF F0", "00 00 00 00 00 F5 42 FF EF"));

            for (int i = 0; i < all.Count; i++)
            {
                p.Feed(new[] { all[i] });
            }

            Assert.True(p.InTn3270E);
            Assert.Equal("TCP00034", p.GrantedLuName);
            Assert.Equal(Bytes("F5 42"), Assert.Single(records));
            Assert.Empty(errors);
        }
        [Fact]
        public void AnLuAfterTheAtSignIsAskedForWithConnect()
        {
            var wire = new List<byte>();
            var p = new Tn3270Protocol(bytes => wire.AddRange(bytes.ToArray()), "IBM-3278-4-E@TSO1");

            p.Feed(DoTn3270E);
            wire.Clear();
            p.Feed(SendDeviceType);

            Assert.Equal(Bytes("FF FA 28 02 07", "'IBM-3278-4-E", "01", "'TSO1", "FF F0"), wire.ToArray());
        }

        [Fact]
        public void RecordsCarryTheFiveByteHeaderBothWays()
        {
            var (p, wire, records, errors) = Create();
            p.Feed(DoTn3270E);
            p.Feed(SendDeviceType);
            p.Feed(DeviceTypeIs);
            p.Feed(Bytes("FF FA 28 03 04 FF F0"));
            wire.Clear();

            // 3270-DATA header, then Erase/Write with an FF doubled inside it, then IAC EOR.
            p.Feed(Bytes("00 00 00 00 00 F5 42 FF FF C1 FF EF"));
            Assert.Single(records);
            Assert.Equal(Bytes("F5 42 FF C1"), records[0]);

            // Our input: the header of zeros first (tnz send_3270_data; x3270 net_output).
            p.SendRecord(Bytes("7D 40 40"));
            Assert.Equal(Bytes("00 00 00 00 00 7D 40 40 FF EF"), wire.ToArray());
            Assert.Empty(errors);
        }

        [Fact]
        public void ARecordThatIsNot3270DataOrAsksForAResponseIsSaidOutLoud()
        {
            var (p, wire, records, errors) = Create();
            p.Feed(DoTn3270E);
            p.Feed(SendDeviceType);
            p.Feed(DeviceTypeIs);
            p.Feed(Bytes("FF FA 28 03 04 FF F0"));
            wire.Clear();

            // SCS-DATA (X'01') is printer data, not handled on a display: it belongs to the
            // SCS-CTL-CODES function of a printer session (RFC 2355 10.1), and the message says so.
            p.Feed(Bytes("01 00 00 00 00 41 42 FF EF"));
            Assert.Empty(records);
            Assert.Contains("SCS-DATA", errors[0]);

            p.Feed(Bytes("00 00 02 00 01 F5 42 FF EF"));
            Assert.Single(records);
            Assert.Contains("RESPONSES was not agreed", errors[1]);
            Assert.Empty(wire);
        }

        [Fact]
        public void ARejectedDeviceTypeBacksOffToPlainTn3270()
        {
            var (p, wire, _, errors) = Create();
            p.Feed(DoTn3270E);
            p.Feed(SendDeviceType);
            wire.Clear();

            p.Feed(Bytes("FF FA 28 02 06 05 01 FF F0"));

            Assert.Equal(Bytes("FF FC 28"), wire.ToArray());
            Assert.Contains("DEVICE-IN-USE", errors[0]);
            Assert.False(p.InTn3270E);
            Assert.Null(p.GrantedLuName);
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
