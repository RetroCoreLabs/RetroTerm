//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs/RetroTerm
// RetroTerm — Connecting today's machines to yesterday's systems
//

using System;
using System.Collections.Generic;

using RetroTerm.Tests.Oracle;

using Xunit;

namespace RetroTerm.Tests.Hercules
{
    /// <summary>
    /// Once BUGS.md B17: a failing live TK5 test printed every record the client sent, and the logon
    /// record holds the password in EBCDIC. These feed a made-up logon through the same paths a
    /// failure message is built from (<see cref="Tk5WireLog"/>, behind
    /// <see cref="RetroTermTk5Terminal.WireText"/>, and <see cref="Tk5WireTap.Decode(List{Tk5WireChunk}, bool, SentDataMask)"/>)
    /// and check the password is in none of its forms. No TK5 needed.
    /// </summary>
    public class FailureMessagePasswordMaskTests
    {
        // Made up; never a real password.
        private const string FakePassword = "zq7xk2";

        // The TSO password prompt as TK5 sent it (Oracle\Recordings\tso-ready.txt line 25,
        // IAC EOR removed): Erase/Write, then "ENTER CURRENT PASSWORD FOR HERC03-" and an input
        // field with attribute X'4C', unprotected and non-display.
        private static readonly byte[] PasswordPrompt =
        {
            0xF5, 0xC3, 0x11, 0x5D, 0x7F, 0x1D, 0x40, 0x11, 0x40, 0x40, 0x1D, 0xC8, 0xC5, 0xD5, 0xE3, 0xC5, 0xD9, 0x40,
            0xC3, 0xE4, 0xD9, 0xD9, 0xC5, 0xD5, 0xE3, 0x40, 0xD7, 0xC1, 0xE2, 0xE2, 0xE6, 0xD6, 0xD9, 0xC4, 0x40, 0xC6,
            0xD6, 0xD9, 0x40, 0xC8, 0xC5, 0xD9, 0xC3, 0xF0, 0xF3, 0x60, 0x1D, 0x40, 0x11, 0xC1, 0x50, 0x1D, 0x4C, 0x13,
        };

        // A screen with no non-display field: Erase/Write, one protected field, "READY".
        private static readonly byte[] ReadyScreen = { 0xF5, 0xC3, 0x11, 0x40, 0x40, 0x1D, 0x60, 0xD9, 0xC5, 0xC1, 0xC4, 0xE8 };

        [Fact]
        public void TheLogonRecordAfterANonDisplayPromptIsMaskedWithoutKnowingThePassword()
        {
            // No known passwords: only the non-display field rule can hide it.
            var log = new Tk5WireLog(new SentDataMask(Array.Empty<string>()));
            log.Host(PasswordPrompt);
            log.Sent(LogonRecord(FakePassword));

            string message = log.Text();
            AssertHoldsNoForm(message, FakePassword);
            AssertHoldsNoForm(message, FakePassword.ToUpperInvariant());
            Assert.Contains(PlaceholderHex(), message, StringComparison.Ordinal);
        }

        [Fact]
        public void AKnownPasswordIsMaskedEvenWithNoPromptBeforeIt()
        {
            // The screen has no non-display field, so only the known-password search can hide it.
            var log = new Tk5WireLog(new SentDataMask(new[] { FakePassword }));
            log.Host(ReadyScreen);
            log.Sent(LogonRecord(FakePassword.ToUpperInvariant()));
            log.Sent(LogonRecord(FakePassword));

            AssertHoldsNoForm(log.Text(), FakePassword);
        }

        [Fact]
        public void OtherTypedTextOnAnOrdinaryScreenIsStillShown()
        {
            // The mask must not hide everything: a command on a READY screen is what a failure
            // needs to show.
            var log = new Tk5WireLog(new SentDataMask(new[] { FakePassword }));
            log.Host(ReadyScreen);
            log.Sent(LogonRecord("LISTCAT"));

            Assert.Contains(Oracle.TelnetUnit.Hex(PasswordMask.Ebcdic("LISTCAT")), log.Text(), StringComparison.Ordinal);
        }

        [Fact]
        public void TheDecodedClientSideOfATappedLogonHoldsNoPassword()
        {
            // As Tk5WireTap keeps it: wire bytes, IAC EOR at the end of each record.
            var chunks = new List<Tk5WireChunk>
            {
                new Tk5WireChunk(0, true, WithEor(PasswordPrompt)),
                new Tk5WireChunk(5, false, Array.Empty<byte>(), "key ENTER"),
                new Tk5WireChunk(10, false, WithEor(LogonRecord(FakePassword))),
            };

            string decoded = Tk5WireTap.Decode(chunks, false, new SentDataMask(Array.Empty<string>()));
            AssertHoldsNoForm(decoded, FakePassword);
            AssertHoldsNoForm(decoded, FakePassword.ToUpperInvariant());
        }

        [Fact]
        public void PlainTextWithAKnownPasswordIsMaskedInAnyCase()
        {
            // An s3270 command or a note, as text.
            string masked = PasswordMask.Text("s3270 refused String(\"" + FakePassword.ToUpperInvariant() + "\")", new[] { FakePassword });
            AssertHoldsNoForm(masked, FakePassword);
        }

        // ENTER, cursor, SBA to the field, then the text in EBCDIC: what a 3270 sends for one
        // field typed and Enter (the shape of tso-ready.txt line 28).
        private static byte[] LogonRecord(string typed)
        {
            byte[] text = PasswordMask.Ebcdic(typed);
            byte[] record = new byte[6 + text.Length];
            record[0] = 0x7D;
            record[1] = 0xC1;
            record[2] = 0xD7;
            record[3] = 0x11;
            record[4] = 0xC1;
            record[5] = 0xD1;
            text.CopyTo(record, 6);
            return record;
        }

        private static byte[] WithEor(byte[] record)
        {
            byte[] wire = new byte[record.Length + 2];
            record.CopyTo(wire, 0);
            wire[record.Length] = 0xFF;
            wire[record.Length + 1] = 0xEF;
            return wire;
        }

        private static string PlaceholderHex() => Oracle.TelnetUnit.Hex(PasswordMask.Ebcdic(PasswordMask.Placeholder));

        // The same five forms HostRecording.AssertHolds searches recording files for. The
        // failure message names the form, never the password.
        private static void AssertHoldsNoForm(string message, string secret)
        {
            string upper = message.ToUpperInvariant();
            string[] forms = PasswordMask.Forms(secret);
            for (int f = 0; f < forms.Length; f++)
            {
                Assert.False(upper.Contains(forms[f], StringComparison.Ordinal), "the message holds the password in form " + (f + 1) + " (1 text, 2-3 ASCII hex, 4-5 EBCDIC hex)");
            }
        }
    }
}
