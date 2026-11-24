//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs/RetroTerm
// RetroTerm — Connecting today's machines to yesterday's systems
//

using System;
using System.Collections.Generic;

using RetroTerm.Core.Protocols;
using RetroTerm.Ibm3270;

using Xunit;

namespace RetroTerm.Tests.Ibm3270
{
    /// <summary>
    /// The 3287 printer over TN3270E (RFC 2355): asking for a partner printer with ASSOCIATE, the
    /// printer functions SCS-CTL-CODES and DATA-STREAM-CTL, SCS-DATA and its responses, and
    /// PRINT-EOJ.
    /// </summary>
    /// <remarks>
    /// The bytes are the RFC's own examples (section 13.4, the last two: a printer asking for
    /// "IBM-3287-1 ASSOCIATE termxyz" and getting "CONNECT termxyz's-prt"), with the command codes
    /// of section 3 (ASSOCIATE X'00', CONNECT X'01'; functions DATA-STREAM-CTL X'01', RESPONSES
    /// X'02', SCS-CTL-CODES X'03'). No TN3270E host exists on this machine; nothing here has been
    /// checked against a real one.
    /// </remarks>
    public class Tn3270EPrinterTests
    {
        private static readonly byte[] DoTn3270E = { 0xFF, 0xFD, 0x28 };
        private static readonly byte[] SendDeviceType = { 0xFF, 0xFA, 0x28, 0x08, 0x02, 0xFF, 0xF0 };
        private static readonly byte[] DeviceTypeIsPrinter = Bytes("FF FA 28 02 04", "'IBM-3287-1", "01", "'termxyz's-prt", "FF F0");

        // Hex pieces, with a text piece written as "'TEXT" (its ASCII bytes), joined in order.
        internal static byte[] Bytes(params string[] parts)
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

        private sealed class Wired
        {
            public Wired(string? associate)
            {
                Protocol = new Tn3270Protocol(bytes => Wire.AddRange(bytes.ToArray()), Tn3270Protocol.PrinterDeviceType, associate);
                Protocol.RecordReceived += r => Records.Add(r.ToArray());
                Protocol.HostMessageReceived += (kind, data) => Messages.Add((kind, data.ToArray()));
                Protocol.PrintEndOfJobReceived += () => EndOfJobs++;
                Protocol.ProtocolError += Errors.Add;
            }

            public Tn3270Protocol Protocol { get; }

            public List<byte> Wire { get; } = new List<byte>();

            public List<byte[]> Records { get; } = new List<byte[]>();

            public List<(Tn3270EHostMessage Kind, byte[] Data)> Messages { get; } = new List<(Tn3270EHostMessage, byte[])>();

            public List<string> Errors { get; } = new List<string>();

            public int EndOfJobs { get; set; }
        }

        // Negotiated up to the host's answer to our FUNCTIONS REQUEST.
        private static Wired NegotiatedWith(string functionsFromHost)
        {
            var w = new Wired("termxyz");
            w.Protocol.Feed(DoTn3270E);
            w.Protocol.Feed(SendDeviceType);
            w.Protocol.Feed(DeviceTypeIsPrinter);
            w.Wire.Clear();
            w.Protocol.Feed(Bytes(functionsFromHost));
            return w;
        }

        [Fact]
        public void APrinterAsksForThePartnerOfADisplayWithAssociate()
        {
            var w = new Wired("termxyz");
            w.Protocol.Feed(DoTn3270E);
            w.Wire.Clear();

            // RFC 2355 13.4: "Client: IAC SB TN3270E DEVICE-TYPE REQUEST IBM-3287-1 ASSOCIATE
            // termxyz IAC SE".
            w.Protocol.Feed(SendDeviceType);
            Assert.Equal(Bytes("FF FA 28 02 07", "'IBM-3287-1", "00", "'termxyz", "FF F0"), w.Wire.ToArray());

            // "Server: ... DEVICE-TYPE IS IBM-3287-1 CONNECT termxyz's-prt". The printer then asks
            // for its functions: DATA-STREAM-CTL, RESPONSES, SCS-CTL-CODES (lowest code first).
            w.Wire.Clear();
            w.Protocol.Feed(DeviceTypeIsPrinter);
            Assert.Equal(Bytes("FF FA 28 03 07 01 02 03 FF F0"), w.Wire.ToArray());

            // "Server: IAC SB TN3270E FUNCTIONS IS SCS-CTL-CODES RESPONSES IAC SE": a subset of
            // what was asked, taken.
            w.Wire.Clear();
            w.Protocol.Feed(Bytes("FF FA 28 03 04 03 02 FF F0"));
            Assert.Empty(w.Wire);
            Assert.True(w.Protocol.InTn3270E);
            Assert.True(w.Protocol.ScsControlCodesAgreed);
            Assert.False(w.Protocol.DataStreamControlAgreed);
            Assert.True(w.Protocol.ResponsesAgreed);
            Assert.Equal("termxyz's-prt", w.Protocol.GrantedLuName);
            Assert.Empty(w.Errors);
        }

        [Fact]
        public void APrinterWithNoNameAsksForAnyPrinter()
        {
            var w = new Wired(null);
            w.Protocol.Feed(DoTn3270E);
            w.Wire.Clear();
            w.Protocol.Feed(SendDeviceType);
            Assert.Equal(Bytes("FF FA 28 02 07", "'IBM-3287-1", "FF F0"), w.Wire.ToArray());
        }

        [Fact]
        public void AHostFunctionRequestWithOnlyWantedFunctionsIsAccepted()
        {
            // The RFC's DATA-STREAM-CTL example has the server ask for "DATA-STREAM-CTL
            // RESPONSES". Both are wanted here, so the list goes back in FUNCTIONS IS (7.2.1).
            Wired w = NegotiatedWith("FF FA 28 03 07 01 02 FF F0");

            Assert.Equal(Bytes("FF FA 28 03 04 01 02 FF F0"), w.Wire.ToArray());
            Assert.True(w.Protocol.DataStreamControlAgreed);
            Assert.False(w.Protocol.ScsControlCodesAgreed);
            Assert.Empty(w.Errors);
        }

        [Fact]
        public void APrinterWithNeitherPrinterFunctionGivesUpTn3270E()
        {
            // RFC 2355 7.2.1: a printer that "refuses to accept either the SCS-CTL-CODES or
            // DATA-STREAM-CTL function" is an impasse; "the 'offended' party should terminate
            // the negotiation by sending an IAC DON'T (or WON'T) TN3270E".
            Wired w = NegotiatedWith("FF FA 28 03 04 02 FF F0");

            Assert.Equal(Bytes("FF FC 28"), w.Wire.ToArray());
            Assert.False(w.Protocol.InTn3270E);
            Assert.Single(w.Errors);
        }

        [Fact]
        public void AHostRequestKeepingNoPrinterFunctionGivesUpTn3270E()
        {
            Wired w = NegotiatedWith("FF FA 28 03 07 02 FF F0");

            Assert.Equal(Bytes("FF FC 28"), w.Wire.ToArray());
            Assert.False(w.Protocol.InTn3270E);
        }

        [Fact]
        public void ScsDataIsHandedUpAndAnsweredLikeA3270Record()
        {
            Wired w = NegotiatedWith("FF FA 28 03 04 03 02 FF F0");
            w.Wire.Clear();

            // SCS-DATA (X'01'), ALWAYS-RESPONSE, SEQ-NUMBER 5: "HI" NL.
            w.Protocol.Feed(Bytes("01 00 02 00 05 C8 C9 15 FF EF"));
            var message = Assert.Single(w.Messages);
            Assert.Equal(Tn3270EHostMessage.ScsData, message.Kind);
            Assert.Equal(Bytes("C8 C9 15"), message.Data);
            Assert.Empty(w.Records);

            // Applied: a positive response with the record's own sequence number (10.4.1).
            w.Protocol.HostRecordStarting();
            w.Protocol.HostRecordProcessed(HostRecordOutcome.Applied);
            Assert.Equal(Bytes("02 00 00 00 05 00 FF EF"), w.Wire.ToArray());

            // Failed with a parameter error: negative, Operation Check (X'02').
            w.Wire.Clear();
            w.Protocol.Feed(Bytes("01 00 01 00 06 2B C6 02 18 FF EF"));
            w.Protocol.HostRecordStarting();
            w.Protocol.HostRecordProcessed(HostRecordOutcome.OperationCheck);
            Assert.Equal(Bytes("02 00 01 00 06 02 FF EF"), w.Wire.ToArray());
            Assert.Empty(w.Errors);
        }

        [Fact]
        public void PrintEndOfJobEndsTheJob()
        {
            Wired w = NegotiatedWith("FF FA 28 03 04 03 02 FF F0");

            // PRINT-EOJ (X'08'), no data (8.1.1).
            w.Protocol.Feed(Bytes("08 00 00 00 00 FF EF"));
            Assert.Equal(1, w.EndOfJobs);
            Assert.Empty(w.Errors);
        }

        [Fact]
        public void ThreeTwoSeventyDataWithoutDataStreamControlIsRefused()
        {
            // Only SCS-CTL-CODES agreed: 3270-DATA belongs to DATA-STREAM-CTL (10.2).
            Wired w = NegotiatedWith("FF FA 28 03 04 03 02 FF F0");

            w.Protocol.Feed(Bytes("00 00 00 00 00 F5 C8 C1 FF EF"));
            Assert.Empty(w.Records);
            Assert.Single(w.Errors);
        }

        [Fact]
        public void ThreeTwoSeventyDataWithDataStreamControlIsARecord()
        {
            Wired w = NegotiatedWith("FF FA 28 03 04 01 02 FF F0");

            w.Protocol.Feed(Bytes("00 00 00 00 00 F5 C8 C1 FF EF"));
            Assert.Equal(Bytes("F5 C8 C1"), Assert.Single(w.Records));
            Assert.Empty(w.Errors);
        }

        [Fact]
        public void ScsDataOnADisplayIsRefused()
        {
            var errors = new List<string>();
            var messages = 0;
            var p = new Tn3270Protocol(_ => { });
            p.ProtocolError += errors.Add;
            p.HostMessageReceived += (_, _) => messages++;
            p.Feed(DoTn3270E);
            p.Feed(SendDeviceType);
            p.Feed(Bytes("FF FA 28 02 04", "'IBM-3278-2-E", "01", "'T1", "FF F0"));
            p.Feed(Bytes("FF FA 28 03 04 02 FF F0"));

            p.Feed(Bytes("01 00 00 00 00 C8 FF EF"));
            Assert.Equal(0, messages);
            Assert.Single(errors);
        }

        [Fact]
        public void AssociateIsOnlyForAPrinterAndNotWithAConnectName()
        {
            // RFC 2355 7.1.3: "ASSOCIATE can be used by the client only when requesting a
            // DEVICE-TYPE that represents a printer"; 7.1.1: CONNECT or ASSOCIATE.
            Assert.Throws<ArgumentException>(() => new Tn3270Protocol(_ => { }, "IBM-3278-2-E", "termxyz"));
            Assert.Throws<ArgumentException>(() => new Tn3270Protocol(_ => { }, "IBM-3287-1@myprt", "termxyz"));
        }
    }
}
