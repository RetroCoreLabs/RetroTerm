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
    /// The TN3270E BIND-IMAGE and SYSREQ functions and NVT mode (RFC 2355 sections 9.1, 10.3,
    /// 10.5, 11) on the wire.
    /// </summary>
    /// <remarks>
    /// The bytes are the RFC's. No host on this machine offers TN3270E: TK5 on localhost:3270 was
    /// traced on 24 November 2026 with s3270 4.5ga6, and its first bytes are FF FD 18 (DO
    /// TERMINAL-TYPE), then TERMINAL-TYPE SEND, BINARY and EOR, and 3270 data; FF FD 28 (DO
    /// TN3270E) never comes, so it never sends a BIND-IMAGE. The BIND inside is IBM's suggested
    /// LU type 2 Bind, GA23-0061-2 p. 5-61.
    /// </remarks>
    public class Tn3270EBindSysReqNvtTests
    {
        private static readonly byte[] DoTn3270E = { 0xFF, 0xFD, 0x28 };
        private static readonly byte[] SendDeviceType = { 0xFF, 0xFA, 0x28, 0x08, 0x02, 0xFF, 0xF0 };

        // RFC 2355 13.4, third example: DEVICE-TYPE IS IBM-3278-5-E CONNECT myterm.
        private static readonly byte[] DeviceTypeIs = Bytes("FF FA 28 02 04", "'IBM-3278-5-E", "01", "'myterm", "FF F0");

        // GA23-0061-2 p. 5-61, the LU type 2 model 2 Bind, 27 bytes.
        private const string IbmModel2Bind = "31 01 03 03 B1 90 30 80 00 00 87 87 00 00 02 00 00 00 00 00 00 00 00 00 02 00 00";

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

        private sealed class Wire
        {
            public Wire(string functionsIs)
            {
                Protocol = new Tn3270Protocol(bytes => Sent.AddRange(bytes.ToArray()), "IBM-3278-5-E@myterm");
                Protocol.RecordReceived += r => Records.Add(r.ToArray());
                Protocol.ProtocolError += Errors.Add;
                Protocol.HostMessageReceived += (kind, data) => Messages.Add((kind, data.ToArray()));
                Protocol.Feed(DoTn3270E);
                Protocol.Feed(SendDeviceType);
                Protocol.Feed(DeviceTypeIs);
                Protocol.Feed(Bytes("FF FA 28 03 04", functionsIs, "FF F0"));
                Assert.True(Protocol.InTn3270E);
                Sent.Clear();
            }

            public Tn3270Protocol Protocol { get; }

            public List<byte> Sent { get; } = new List<byte>();

            public List<byte[]> Records { get; } = new List<byte[]>();

            public List<string> Errors { get; } = new List<string>();

            public List<(Tn3270EHostMessage Kind, byte[] Data)> Messages { get; } = new List<(Tn3270EHostMessage, byte[])>();
        }

        [Fact]
        public void TheRfcExampleWithBindImageIsAgreed()
        {
            // RFC 2355 13.4: "FUNCTIONS IS RESPONSES BIND-IMAGE" (codes X'02' and X'00').
            var w = new Wire("02 00");

            Assert.True(w.Protocol.BindImageAgreed);
            Assert.True(w.Protocol.ResponsesAgreed);
            Assert.False(w.Protocol.SysReqAgreed);
            Assert.False(w.Protocol.Bound);
            Assert.Equal("myterm", w.Protocol.GrantedLuName);
            Assert.Empty(w.Errors);
        }

        [Fact]
        public void NoThreeTwoSevenZeroDataFlowsEitherWayBeforeTheBindImage()
        {
            var w = new Wire("00");

            // RFC 2355 10.3: "data messages with DATA-TYPE set to 3270-DATA ... are not allowed
            // before the first BIND-IMAGE is received by the client".
            w.Protocol.Feed(Bytes("00 00 00 00 00 F5 42 FF EF"));
            Assert.Empty(w.Records);
            Assert.Contains("before any BIND-IMAGE", w.Errors[0]);

            w.Protocol.SendRecord(Bytes("7D 40 40"));
            Assert.Empty(w.Sent);
            Assert.Contains("no SNA session is bound", w.Errors[1]);
        }

        [Fact]
        public void ABindImageIsHandedUpAndThen3270DataFlows()
        {
            var w = new Wire("00");

            // DATA-TYPE X'03', then the BIND exactly as received (RFC 2355 10.3).
            w.Protocol.Feed(Bytes("03 00 00 00 00", IbmModel2Bind, "FF EF"));

            var (kind, data) = Assert.Single(w.Messages);
            Assert.Equal(Tn3270EHostMessage.BindImage, kind);
            Assert.Equal(Bytes(IbmModel2Bind), data);
            Assert.True(w.Protocol.Bound);

            w.Protocol.Feed(Bytes("00 00 00 00 00 F5 42 FF EF"));
            Assert.Equal(Bytes("F5 42"), Assert.Single(w.Records));
            w.Protocol.SendRecord(Bytes("7D 40 40"));
            Assert.Equal(Bytes("00 00 00 00 00 7D 40 40 FF EF"), w.Sent.ToArray());
            Assert.Empty(w.Errors);
        }

        [Fact]
        public void AnUnbindEndsTheSessionWithItsReasonCode()
        {
            var w = new Wire("00");
            w.Protocol.Feed(Bytes("03 00 00 00 00", IbmModel2Bind, "FF EF"));

            // RFC 2355 10.3: X'01', "normal end of session".
            w.Protocol.Feed(Bytes("04 00 00 00 00 01 FF EF"));

            Assert.Equal(Tn3270EHostMessage.Unbind, w.Messages[1].Kind);
            Assert.Equal(new byte[] { 0x01 }, w.Messages[1].Data);
            Assert.False(w.Protocol.Bound);
        }

        [Fact]
        public void ABindImageWithoutTheFunctionIsSaidOutLoudAndNotUsed()
        {
            var w = new Wire("02");

            w.Protocol.Feed(Bytes("03 00 00 00 00", IbmModel2Bind, "FF EF"));

            Assert.Empty(w.Messages);
            Assert.Contains("BIND-IMAGE function was not agreed", Assert.Single(w.Errors));
        }

        [Fact]
        public void SysReqIsAbortOutputAndTheSscpSessionCarriesItsOwnDataType()
        {
            // RESPONSES and SYSREQ, no BIND-IMAGE: 3270 mode at once.
            var w = new Wire("02 04");

            // RFC 2355 10.5.2: "the client should transmit a Telnet AO command".
            Assert.True(w.Protocol.SendSysReq());
            Assert.Equal(Bytes("FF F5"), w.Sent.ToArray());

            // The server answers on the SSCP-LU session: "IKJ" NL.
            w.Sent.Clear();
            w.Protocol.Feed(Bytes("07 00 00 00 00 C9 D2 D1 15 FF EF"));
            var (kind, data) = Assert.Single(w.Messages);
            Assert.Equal(Tn3270EHostMessage.SscpLuData, kind);
            Assert.Equal(Bytes("C9 D2 D1 15"), data);
            Assert.True(w.Protocol.InSscpLuMode);

            // "any data messages exchanged ... should have the DATA-TYPE flag set to
            // SSCP-LU-DATA". RFC 2355 10.4 counts 3270-DATA only, so the sequence stays 0.
            w.Protocol.SendRecord(Bytes("D3 D6 C7 D6 C6 C6"));
            Assert.Equal(Bytes("07 00 00 00 00 D3 D6 C7 D6 C6 C6 FF EF"), w.Sent.ToArray());

            // A second Sys Req goes the same way; the server then resumes the application.
            w.Sent.Clear();
            Assert.True(w.Protocol.SendSysReq());
            Assert.Equal(Bytes("FF F5"), w.Sent.ToArray());
            w.Protocol.Feed(Bytes("00 00 00 00 00 F5 42 FF EF"));
            Assert.False(w.Protocol.InSscpLuMode);
            w.Sent.Clear();
            w.Protocol.SendRecord(Bytes("7D 40 40"));
            Assert.Equal(Bytes("00 00 00 00 00 7D 40 40 FF EF"), w.Sent.ToArray());
            Assert.Empty(w.Errors);
        }

        [Fact]
        public void WithoutSysReqAgreedNothingIsSentAndSscpDataIsRefused()
        {
            var w = new Wire(string.Empty);

            Assert.False(w.Protocol.SendSysReq());
            Assert.Empty(w.Sent);

            // Basic TN3270E has 3270-DATA and NVT-DATA only (RFC 2355 section 9).
            w.Protocol.Feed(Bytes("07 00 00 00 00 C9 FF EF"));
            Assert.Empty(w.Messages);
            Assert.Contains("SSCP-LU-DATA", Assert.Single(w.Errors));
        }

        [Fact]
        public void NvtDataSwitchesToNvtModeAndOurRecordsFollow()
        {
            // RFC 2355 9.1: NVT-DATA is part of basic TN3270E; receiving it switches modes.
            var w = new Wire(string.Empty);

            w.Protocol.Feed(Bytes("05 00 00 00 00 3E 20 FF EF"));

            var (kind, data) = Assert.Single(w.Messages);
            Assert.Equal(Tn3270EHostMessage.NvtData, kind);
            Assert.Equal(Bytes("3E 20"), data);
            Assert.True(w.Protocol.InNvtMode);

            w.Protocol.SendRecord(Bytes("68 69 0D 0A"));
            Assert.Equal(Bytes("05 00 00 00 00 68 69 0D 0A FF EF"), w.Sent.ToArray());
            Assert.Empty(w.Errors);
        }

        [Fact]
        public void AttnUnderTn3270eIsInterruptProcess()
        {
            // RFC 2355 11: "the client should transmit a Telnet IP command".
            var w = new Wire("00");

            w.Protocol.SendInterruptProcess();

            Assert.Equal(Bytes("FF F4"), w.Sent.ToArray());
        }
    }
}
