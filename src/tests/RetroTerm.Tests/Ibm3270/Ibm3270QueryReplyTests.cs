//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs/RetroTerm
// RetroTerm — Connecting today's machines to yesterday's systems
//

using System;
using System.Collections.Generic;

using RetroIO.Protocols.Tn3270;

using RetroTerm.Ibm3270;

using Xunit;

namespace RetroTerm.Tests.Ibm3270;

/// <summary>
/// The Usable Area and Character Sets query replies of every 3278 model, checked byte by byte
/// against GA23-0059-4. The replies are built in RetroIO (Display3270.AppendQueryReply); these
/// tests pin what RetroTerm actually sends. See docs/spec/IBM3270/3270-DATA-STREAM.md sections
/// 9.4, 9.4a and 9.9 (BUGS.md B15, fixed in RetroIO on 24 November 2026).
/// </summary>
public class Ibm3270QueryReplyTests
{
    // Write Structured Field, Read Partition (X'01'), partition X'FF', Query (X'02') - p. 5-47.
    private static readonly byte[] Query = { Command3270.WriteStructuredField, 0x00, 0x05, 0x01, 0xFF, 0x02 };

    [Theory]
    [InlineData(2, 80, 24)]
    [InlineData(3, 80, 32)]
    [InlineData(4, 80, 43)]
    [InlineData(5, 132, 27)]
    public void TheUsableAreaIsTheAlternateScreenWithItsBufferSize(int model, int columns, int rows)
    {
        // Every model's default size is 24 x 80 and fits inside its alternate size, so the
        // alternate size is the largest number of characters the screen can show, which is what
        // W x H mean (p. 6-89: "W x H defines the maximum number of characters that can be
        // presented in the Usable Area"). BUFFSZ is "the amount of character buffer available"
        // (p. 6-90); the largest screen is what the buffer has to hold. Neither size is above
        // 16383 cells (RetroIO refuses more), so BUFFSZ always fits its 2 bytes (p. 6-87).
        // No self-defining parameter follows, so the reply stops after BUFFSZ (p. 6-87: XMIN to
        // YMAX "may be omitted" when there are no self-defining parameters and no variable cells).
        byte[] reply = ReplyFor(model, 0x81);

        int cells = columns * rows;
        byte[] expected =
        {
            0x00, 0x17, 0x81, 0x81,
            0x01,                                   // 12/14-bit addressing (p. 6-88, X'1')
            0x00,                                   // VCP 0, matrix characters, W/H in cells
            (byte)(columns >> 8), (byte)columns,
            (byte)(rows >> 8), (byte)rows,
            0x00,                                   // UNITS inches
            0xFF, 0xFF, 0xFF, 0xFF,                 // Xr not known (allowed without Load PS, p. 6-89)
            0xFF, 0xFF, 0xFF, 0xFF,                 // Yr not known
            0x09, 0x10,                             // AW x AH, the 9 x 16 cell
            (byte)(cells >> 8), (byte)cells,        // BUFFSZ
        };
        Assert.Equal(expected, reply);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void TheBaseCharacterSetHasALocalIdForANonLoadableOneByteSet(int model)
    {
        // p. 6-26: "Nonloadable character sets will have default LCID assigned to them
        // (X'F0' to X'FE'), where: X'F0' to X'F7' = 1-byte character sets". p. 4-19 gives the
        // same range for the SA/SFE character set value, and keeps X'00' for "Default character
        // set" - a value a host sends, not an ID a set carries. RetroIO sent X'00' for the base
        // set until 24 November 2026 (BUGS.md B15, closed) and sends X'F0' now. Which of X'F0' to
        // X'F7' is not given by the manual; X'F1' is taken by the Graphic Escape set.
        byte[] reply = ReplyFor(model, 0x85);

        // Base bytes 0 to 12 (p. 6-21), then the first descriptor: SET, flags, LCID (p. 6-24).
        Assert.Equal(0x07, reply[12]);
        Assert.Equal(0x00, reply[13]);
        byte lcid = reply[15];
        Assert.True(lcid >= 0xF0 && lcid <= 0xF7, "base set local ID is X'" + lcid.ToString("X2") + "', p. 6-26 gives X'F0' to X'F7'");
        Assert.NotEqual(0xF1, lcid);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(5)]
    public void TheGraphicEscapeSetIsSetOneWithLocalIdF1(int model)
    {
        // p. 6-25: "The character set with device specific ID equal to X'01' is defined as the
        // alternate character page". It is non-loadable and 1-byte, so its ID is in X'F0' to
        // X'F7' (p. 6-26); X'F1' is in that range. Code page 310, character set 963.
        byte[] reply = ReplyFor(model, 0x85);

        Assert.Equal(13 + 7 + 7, reply.Length);
        Assert.Equal(new byte[] { 0x01, 0x00, 0xF1, 0x03, 0xC3, 0x01, 0x36 }, reply.AsSpan(20, 7).ToArray());
    }

    // Sends a Query to a fresh 3278 of the given model and returns the one query reply with the
    // given code, length bytes included.
    private static byte[] ReplyFor(int model, byte code)
    {
        var e = new Ibm3270Emulator(model);
        var sent = new List<byte[]>();
        var errors = new List<string>();
        e.DataToSend += sent.Add;
        e.ProtocolError += errors.Add;

        e.ProcessData(Query);

        Assert.Empty(errors);
        Assert.Single(sent);
        byte[] record = sent[0];

        // AID X'88', then structured fields: 2-byte length (counting itself), X'81', code (p. 6-1).
        Assert.Equal(0x88, record[0]);
        int at = 1;
        while (at + 4 <= record.Length)
        {
            int length = (record[at] << 8) | record[at + 1];
            Assert.True(length >= 4 && at + length <= record.Length, "bad structured field length at " + at);
            if (record[at + 2] == 0x81 && record[at + 3] == code)
            {
                return record.AsSpan(at, length).ToArray();
            }

            at += length;
        }

        throw new Xunit.Sdk.XunitException("no query reply X'" + code.ToString("X2") + "'");
    }
}
