//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs/RetroTerm
// RetroTerm — Connecting today's machines to yesterday's systems
//

using System;
using System.Collections.Generic;

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
            0xFF, 0xFA, 0x18, 0x00, 0x49, 0x42, 0x4D, 0x2D, 0x33, 0x32, 0x37, 0x38, 0x2D, 0x32, 0xFF, 0xF0,
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
    }
}
