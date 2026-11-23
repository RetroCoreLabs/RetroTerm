//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs/RetroTerm
// RetroTerm — Connecting today's machines to yesterday's systems
//

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using RetroTerm.Core.Protocols;
using RetroTerm.Core.Session;
using RetroTerm.Ibm3270;

using Xunit;

namespace RetroTerm.Tests.Ibm3270
{
    /// <summary>
    /// The TN3270E RESPONSES function (RFC 2355 section 10.4): negotiating it, and the response
    /// sent for each host record once the terminal has applied it.
    /// </summary>
    /// <remarks>
    /// The bytes are the RFC's (no host here offers TN3270E; TK5 does not, measured 23 November
    /// 2026). The response layout is section 10.4.1: header DATA-TYPE RESPONSE (X'02'), REQUEST-FLAG
    /// 0, RESPONSE-FLAG 0 positive or 1 negative, the host's SEQ-NUMBER, then ONE status byte:
    /// X'00' Device End for a positive response, X'00' Command Reject or X'02' Operation Check for
    /// a negative one, then IAC EOR.
    /// </remarks>
    public class Tn3270EResponsesTests
    {
        private static readonly byte[] DoTn3270E = { 0xFF, 0xFD, 0x28 };
        private static readonly byte[] SendDeviceType = { 0xFF, 0xFA, 0x28, 0x08, 0x02, 0xFF, 0xF0 };
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

        // TN3270E in force with RESPONSES: the host accepts the list we asked for.
        private static (Tn3270Protocol Protocol, List<byte> Wire, List<byte[]> Records, List<string> Errors) CreateWithResponses()
        {
            var t = Create();
            t.Protocol.Feed(DoTn3270E);
            t.Protocol.Feed(SendDeviceType);
            t.Protocol.Feed(DeviceTypeIs);
            t.Protocol.Feed(Bytes("FF FA 28 03 04 02 FF F0"));
            Assert.True(t.Protocol.ResponsesAgreed);
            t.Wire.Clear();
            return t;
        }

        // One host 3270-DATA record with this response flag and sequence number (already
        // IAC-doubled as the host would send it), carrying Write with WCC X'C3'.
        private static byte[] HostRecord(string flag, string sequenceOnWire) =>
            Bytes("00 00", flag, sequenceOnWire, "F1 C3 FF EF");

        // Applies one record as the session does: starting, then the outcome.
        private static void Apply(Tn3270Protocol p, HostRecordOutcome? outcome)
        {
            p.HostRecordStarting();
            p.HostRecordProcessed(outcome);
        }

        [Fact]
        public void TheClientAsksForResponsesAndTheHostAgrees()
        {
            var (p, wire, _, errors) = Create();
            p.Feed(DoTn3270E);
            p.Feed(SendDeviceType);
            wire.Clear();

            // DEVICE-TYPE IS: FUNCTIONS REQUEST RESPONSES (RFC 2355 7.2, code X'02').
            p.Feed(DeviceTypeIs);
            Assert.Equal(Bytes("FF FA 28 03 07 02 FF F0"), wire.ToArray());
            Assert.False(p.InTn3270E);

            wire.Clear();
            p.Feed(Bytes("FF FA 28 03 04 02 FF F0"));
            Assert.Empty(wire);
            Assert.True(p.InTn3270E);
            Assert.True(p.ResponsesAgreed);
            Assert.Empty(errors);
        }

        [Fact]
        public void AHostThatGrantsNoFunctionsGivesBasicTn3270e()
        {
            var (p, _, _, errors) = Create();
            p.Feed(DoTn3270E);
            p.Feed(SendDeviceType);
            p.Feed(DeviceTypeIs);

            // FUNCTIONS IS with less than was asked for is taken (RFC 2355 7.2.1; x3270 too).
            p.Feed(Bytes("FF FA 28 03 04 FF F0"));
            Assert.True(p.InTn3270E);
            Assert.False(p.ResponsesAgreed);
            Assert.Empty(errors);
        }

        [Fact]
        public void AHostRequestWithMoreIsAnsweredWithWhatBothWant()
        {
            var (p, wire, _, _) = Create();
            p.Feed(DoTn3270E);
            p.Feed(SendDeviceType);
            p.Feed(DeviceTypeIs);
            wire.Clear();

            // The host asks for BIND-IMAGE, RESPONSES, SYSREQ and an unknown code X'30': we ask
            // back for RESPONSES only (RFC 2355 7.2.1; x3270 b8_and).
            p.Feed(Bytes("FF FA 28 03 07 00 02 04 30 FF F0"));
            Assert.Equal(Bytes("FF FA 28 03 07 02 FF F0"), wire.ToArray());
            Assert.False(p.InTn3270E);

            // It then asks for RESPONSES: accepted by sending the same list back in IS.
            wire.Clear();
            p.Feed(Bytes("FF FA 28 03 07 02 FF F0"));
            Assert.Equal(Bytes("FF FA 28 03 04 02 FF F0"), wire.ToArray());
            Assert.True(p.ResponsesAgreed);
        }

        [Fact]
        public void AHostRequestForNothingIsAcceptedAsBasicTn3270e()
        {
            var (p, wire, _, _) = Create();
            p.Feed(DoTn3270E);
            p.Feed(SendDeviceType);
            p.Feed(DeviceTypeIs);
            wire.Clear();

            p.Feed(Bytes("FF FA 28 03 07 FF F0"));
            Assert.Equal(Bytes("FF FA 28 03 04 FF F0"), wire.ToArray());
            Assert.True(p.InTn3270E);
            Assert.False(p.ResponsesAgreed);

            // Once RESPONSES was removed it is not asked for again in this negotiation (RFC
            // 2355 7.2.1, the rule against loops): a later host REQUEST for it is narrowed.
            wire.Clear();
            p.Feed(Bytes("FF FA 28 03 07 02 FF F0"));
            Assert.Equal(Bytes("FF FA 28 03 07 FF F0"), wire.ToArray());
        }

        [Fact]
        public void FunctionsTheHostAddedOnItsOwnEndTn3270e()
        {
            var (p, wire, _, errors) = Create();
            p.Feed(DoTn3270E);
            p.Feed(SendDeviceType);
            p.Feed(DeviceTypeIs);
            wire.Clear();

            // IS RESPONSES SYSREQ: SYSREQ was never asked for (x3270 "Host illegally added").
            p.Feed(Bytes("FF FA 28 03 04 02 04 FF F0"));

            Assert.Equal(Bytes("FF FC 28"), wire.ToArray());
            Assert.Contains("not asked for", errors[0]);
            Assert.False(p.InTn3270E);
        }

        [Theory]
        [InlineData(HostRecordOutcome.Applied)]
        [InlineData(HostRecordOutcome.CommandReject)]
        [InlineData(HostRecordOutcome.OperationCheck)]
        public void NoResponseIsNeverAnswered(HostRecordOutcome outcome)
        {
            var (p, wire, records, errors) = CreateWithResponses();
            p.Feed(HostRecord("00", "00 07"));
            Assert.Single(records);

            Apply(p, outcome);

            Assert.Empty(wire);
            Assert.Empty(errors);
        }

        [Fact]
        public void ErrorResponseIsNotAnsweredWhenTheRecordWentWell()
        {
            var (p, wire, _, errors) = CreateWithResponses();
            p.Feed(HostRecord("01", "00 07"));

            Apply(p, HostRecordOutcome.Applied);

            Assert.Empty(wire);
            Assert.Empty(errors);
        }

        [Theory]
        [InlineData(HostRecordOutcome.CommandReject, "00")]
        [InlineData(HostRecordOutcome.OperationCheck, "02")]
        public void ErrorResponseGetsANegativeResponseWhenTheRecordFailed(HostRecordOutcome outcome, string status)
        {
            var (p, wire, _, _) = CreateWithResponses();
            p.Feed(HostRecord("01", "12 34"));

            Apply(p, outcome);

            Assert.Equal(Bytes("02 00 01 12 34", status, "FF EF"), wire.ToArray());
        }

        [Fact]
        public void AlwaysResponseGetsAPositiveResponseWhenTheRecordWentWell()
        {
            var (p, wire, _, _) = CreateWithResponses();
            p.Feed(HostRecord("02", "12 34"));

            Apply(p, HostRecordOutcome.Applied);

            Assert.Equal(Bytes("02 00 00 12 34 00 FF EF"), wire.ToArray());
        }

        [Theory]
        [InlineData(HostRecordOutcome.CommandReject, "00")]
        [InlineData(HostRecordOutcome.OperationCheck, "02")]
        public void AlwaysResponseGetsANegativeResponseWhenTheRecordFailed(HostRecordOutcome outcome, string status)
        {
            var (p, wire, _, _) = CreateWithResponses();
            p.Feed(HostRecord("02", "00 01"));

            Apply(p, outcome);

            Assert.Equal(Bytes("02 00 01 00 01", status, "FF EF"), wire.ToArray());
        }

        [Fact]
        public void ASequenceNumberHoldingFfIsReadUndoubledAndSentDoubled()
        {
            // RFC 2355 8.1.4: a 0xFF byte in SEQ-NUMBER is doubled on the wire both ways. The host
            // sends 0x00FF as 00 FF FF; the response echoes it as 00 FF FF again.
            var (p, wire, records, _) = CreateWithResponses();
            p.Feed(HostRecord("02", "00 FF FF"));
            Assert.Equal(Bytes("F1 C3"), Assert.Single(records));

            Apply(p, HostRecordOutcome.Applied);

            Assert.Equal(Bytes("02 00 00 00 FF FF 00 FF EF"), wire.ToArray());
        }

        [Fact]
        public void EachRecordIsAnsweredWithItsOwnSequenceNumberInOrder()
        {
            // Three records arrive before the session has applied any (they sit on the pump).
            var (p, wire, _, _) = CreateWithResponses();
            p.Feed(HostRecord("02", "00 05"));
            p.Feed(HostRecord("00", "00 06"));
            p.Feed(HostRecord("01", "00 07"));

            Apply(p, HostRecordOutcome.Applied);
            Apply(p, HostRecordOutcome.OperationCheck);
            Apply(p, HostRecordOutcome.CommandReject);

            Assert.Equal(Bytes("02 00 00 00 05 00 FF EF", "02 00 01 00 07 00 FF EF"), wire.ToArray());
        }

        [Fact]
        public void APositiveResponseGoesAheadOfTheRecordsOwnReplyAndOnlyOnce()
        {
            // A Read Buffer asked for ALWAYS-RESPONSE: the terminal's reply is made while the
            // record is applied. x3270 net_output sends the positive response first.
            var (p, wire, _, _) = CreateWithResponses();
            p.Feed(Bytes("00 00 02 00 09 F2 FF EF"));

            p.HostRecordStarting();
            p.SendRecord(Bytes("60 40 40"));
            p.HostRecordProcessed(HostRecordOutcome.Applied);

            Assert.Equal(Bytes("02 00 00 00 09 00 FF EF", "00 00 00 00 00 60 40 40 FF EF"), wire.ToArray());
        }

        [Fact]
        public void AnInboundRecordSentBetweenHostRecordsCarriesNoResponse()
        {
            // An Enter pressed while a host record waits on the pump is not that record's reply.
            var (p, wire, _, _) = CreateWithResponses();
            p.Feed(HostRecord("02", "00 03"));

            p.SendRecord(Bytes("7D 40 40"));
            Assert.Equal(Bytes("00 00 00 00 00 7D 40 40 FF EF"), wire.ToArray());

            wire.Clear();
            Apply(p, HostRecordOutcome.Applied);
            Assert.Equal(Bytes("02 00 00 00 03 00 FF EF"), wire.ToArray());
        }

        [Fact]
        public void AnUnknownOutcomeSendsNothingAndSaysSo()
        {
            var (p, wire, _, errors) = CreateWithResponses();
            p.Feed(HostRecord("02", "00 03"));

            Apply(p, null);

            Assert.Empty(wire);
            Assert.Contains("not known", Assert.Single(errors));
        }

        [Fact]
        public void WithoutResponsesAgreedAnAskIsReportedAndNotAnswered()
        {
            var (p, wire, records, errors) = Create();
            p.Feed(DoTn3270E);
            p.Feed(SendDeviceType);
            p.Feed(DeviceTypeIs);
            p.Feed(Bytes("FF FA 28 03 04 FF F0"));
            wire.Clear();

            p.Feed(HostRecord("02", "00 01"));
            Assert.Single(records);
            Apply(p, HostRecordOutcome.Applied);

            Assert.Empty(wire);
            Assert.Contains("RESPONSES was not agreed", Assert.Single(errors));
        }

        [Fact]
        public void OurOwnRecordsCountFromZeroWithResponsesAndRoundAfter32767()
        {
            // RFC 2355 10.4: the sender counts its 3270-DATA messages from zero, wrapping to zero
            // after 32767; 0x00FF goes doubled.
            var (p, wire, _, _) = CreateWithResponses();
            byte[] record = { 0x7D, 0x40, 0x40 };

            p.SendRecord(record);
            Assert.Equal(Bytes("00 00 00 00 00 7D 40 40 FF EF"), wire.ToArray());
            wire.Clear();
            p.SendRecord(record);
            Assert.Equal(Bytes("00 00 00 00 01 7D 40 40 FF EF"), wire.ToArray());

            for (int i = 2; i < 255; i++)
            {
                p.SendRecord(record);
            }

            wire.Clear();
            p.SendRecord(record);
            Assert.Equal(Bytes("00 00 00 00 FF FF 7D 40 40 FF EF"), wire.ToArray());

            for (int i = 256; i < 32767; i++)
            {
                p.SendRecord(record);
            }

            wire.Clear();
            p.SendRecord(record);
            Assert.Equal(Bytes("00 00 00 7F FF FF 7D 40 40 FF EF"), wire.ToArray());
            wire.Clear();
            p.SendRecord(record);
            Assert.Equal(Bytes("00 00 00 00 00 7D 40 40 FF EF"), wire.ToArray());
        }

        [Fact]
        public void BasicTn3270eRecordsKeepSequenceZero()
        {
            var (p, wire, _, _) = Create();
            p.Feed(DoTn3270E);
            p.Feed(SendDeviceType);
            p.Feed(DeviceTypeIs);
            p.Feed(Bytes("FF FA 28 03 04 FF F0"));
            wire.Clear();

            p.SendRecord(Bytes("7D"));
            p.SendRecord(Bytes("7D"));

            Assert.Equal(Bytes("00 00 00 00 00 7D FF EF", "00 00 00 00 00 7D FF EF"), wire.ToArray());
        }

        // ---- Through the real session and the real 3270 emulator ----

        private static async Task<(TerminalSession Session, ProtocolConnection Connection)> ConnectAsync()
        {
            var emulator = new Ibm3270Emulator();
            var session = new TerminalSession(emulator, "Test");
            var connection = new ProtocolConnection();
            await session.ConnectAsync(connection, TestContext.Current.CancellationToken);
            connection.Host(DoTn3270E);
            connection.Host(SendDeviceType);
            connection.Host(DeviceTypeIs);
            connection.Host(Bytes("FF FA 28 03 04 02 FF F0"));
            await session.FlushAsync();
            Assert.True(connection.Protocol.ResponsesAgreed);
            connection.Wire.Clear();
            return (session, connection);
        }

        [Theory]
        // A good Write: positive for ALWAYS, nothing for ERROR or NO.
        [InlineData("02", "F1 C3", "02 00 00 00 2A 00 FF EF")]
        [InlineData("01", "F1 C3", "")]
        [InlineData("00", "F1 C3", "")]
        // Command code X'01' is not one the 3270 takes: Command Reject (GA23-0059-4 Appendix A).
        [InlineData("02", "01 C3", "02 00 01 00 2A 00 FF EF")]
        [InlineData("01", "01 C3", "02 00 01 00 2A 00 FF EF")]
        [InlineData("00", "01 C3", "")]
        // A Write whose SBA has the reserved flag bits B'10' (p. 4-4): Operation Check.
        [InlineData("02", "F1 C3 11 80 40", "02 00 01 00 2A 02 FF EF")]
        [InlineData("01", "F1 C3 11 80 40", "02 00 01 00 2A 02 FF EF")]
        [InlineData("00", "F1 C3 11 80 40", "")]
        public async Task TheEmulatorsOutcomeDecidesTheResponse(string flag, string data, string expected)
        {
            var (session, connection) = await ConnectAsync();

            connection.Host(Bytes("00 00", flag, "00 2A", data, "FF EF"));
            await session.FlushAsync();

            Assert.Equal(Bytes(expected), connection.Wire.ToArray());
        }

        [Fact]
        public async Task AReadBufferIsAnsweredPositivelyBeforeItsReply()
        {
            var (session, connection) = await ConnectAsync();

            connection.Host(Bytes("00 00 02 00 2B F2 FF EF"));
            await session.FlushAsync();

            byte[] wire = connection.Wire.ToArray();
            byte[] positive = Bytes("02 00 00 00 2B 00 FF EF");
            Assert.True(wire.Length > positive.Length + 5, "the Read Buffer reply is missing");
            Assert.Equal(positive, wire.AsSpan(0, positive.Length).ToArray());

            // Then the reply: our 3270-DATA header with our first sequence number, 0, and no
            // second response after it.
            Assert.Equal(Bytes("00 00 00 00 00"), wire.AsSpan(positive.Length, 5).ToArray());
            Assert.Equal(1, Count(wire, positive));
        }

        private static int Count(byte[] haystack, byte[] needle)
        {
            int count = 0;
            for (int i = 0; i + needle.Length <= haystack.Length; i++)
            {
                if (haystack.AsSpan(i, needle.Length).SequenceEqual(needle))
                {
                    count++;
                }
            }

            return count;
        }

        /// <summary>
        /// A connection with the real <see cref="Tn3270Protocol"/> and no socket: the test plays
        /// the host with <see cref="Host"/>, and everything the protocol writes lands in
        /// <see cref="Wire"/>. Like Tn3270Connection, it raises DataReceived once per record.
        /// </summary>
        private sealed class ProtocolConnection : IConnection, IHostRecordResponseConnection
        {
            private readonly object _gate = new object();

            public ProtocolConnection()
            {
                Protocol = new Tn3270Protocol(bytes =>
                {
                    lock (Wire)
                    {
                        Wire.AddRange(bytes.ToArray());
                    }
                });
                Protocol.RecordReceived += record => DataReceived?.Invoke(record.ToArray());
            }

            public event Action<ReadOnlyMemory<byte>>? DataReceived;

            public event Action<ConnectionStatus>? StatusChanged;

            // Never raised: nothing here fails on its own. Declared because IConnection has it.
            public event Action<Exception>? ErrorOccurred
            {
                add { }
                remove { }
            }

            public Tn3270Protocol Protocol { get; }

            public List<byte> Wire { get; } = new List<byte>();

            public ConnectionStatus Status { get; private set; }

            public string ConnectionType => "TN3270";

            public string Description => "test";

            public void Host(byte[] bytes)
            {
                lock (_gate)
                {
                    Protocol.Feed(bytes);
                }
            }

            public Task ConnectAsync(CancellationToken cancellationToken = default)
            {
                Status = ConnectionStatus.Connected;
                StatusChanged?.Invoke(Status);
                return Task.CompletedTask;
            }

            public Task DisconnectAsync()
            {
                Status = ConnectionStatus.Disconnected;
                StatusChanged?.Invoke(Status);
                return Task.CompletedTask;
            }

            public Task SendAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
            {
                lock (_gate)
                {
                    Protocol.SendRecord(data.Span);
                }

                return Task.CompletedTask;
            }

            public void HostRecordStarting()
            {
                lock (_gate)
                {
                    Protocol.HostRecordStarting();
                }
            }

            public void HostRecordProcessed(HostRecordOutcome? outcome)
            {
                lock (_gate)
                {
                    Protocol.HostRecordProcessed(outcome);
                }
            }

            public void Dispose()
            {
                // Nothing to release: there is no socket.
            }
        }
    }
}
