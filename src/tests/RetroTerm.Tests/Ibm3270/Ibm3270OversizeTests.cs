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
/// A 3278 with an alternate screen larger than its model's (x3270's -oversize): the name it
/// announces, the limits, 14-bit buffer addresses both ways, and the size in the Usable Area and
/// Implicit Partition query replies. The byte examples are GA23-0059-4 Appendix D, p. D-1; the
/// limits and the name were measured on s3270 4.5 on 24 November 2026 (Ibm3270Emulator, the
/// oversize constructor). The same session is replayed to s3270 itself by the Oracle test
/// "oversize" (Oracle\Recordings\oversize.txt).
/// </summary>
public class Ibm3270OversizeTests
{
    // Write Structured Field, Read Partition (X'01'), partition X'FF', Query (X'02') - p. 5-47.
    private static readonly byte[] Query = { Command3270.WriteStructuredField, 0x00, 0x05, 0x01, 0xFF, 0x02 };

    [Fact]
    public void ALargerScreenAnnouncesIbmDynamicAndTheModelsOwnAnnouncesTheModel()
    {
        // RFC 2355 section 7.1: IBM-DYNAMIC "implies no pre-defined alternate display size".
        Assert.Equal("IBM-DYNAMIC", new Ibm3270Emulator(2, 100, 50).TerminalType);
        Assert.Equal("IBM-3278-2-E", new Ibm3270Emulator(2).TerminalType);
        Assert.Equal("IBM-3278-5-E", new Ibm3270Emulator(5, 132, 27).TerminalType);
        Assert.False(new Ibm3270Emulator(5, 132, 27).IsOversize);
        Assert.True(new Ibm3270Emulator(2, 80, 25).IsOversize);
    }

    [Fact]
    public void ThePictureIsTheLargerScreenWithLine25UnderIt()
    {
        var e = new Ibm3270Emulator(2, 100, 50);

        Assert.Equal(100, e.Buffer.Width);
        Assert.Equal(51, e.Buffer.Height);
        Assert.Equal(100, e.AlternateScreenColumns);
        Assert.Equal(50, e.AlternateScreenRows);
        Assert.Equal(100, e.DrawnStatusLine.Length);
    }

    [Theory]
    [InlineData(2, 79, 24)]    // s3270: "Less than model 2 columns (80)"
    [InlineData(2, 132, 20)]   // s3270 refused it: fewer rows than a model 2
    [InlineData(5, 131, 30)]   // fewer columns than a model 5
    [InlineData(2, 128, 128)]  // 16384 cells; s3270: "Exceeds protocol limit"
    [InlineData(2, 200, 90)]   // 18000 cells; s3270: "Exceeds protocol limit"
    public void ASizeOutsideTheLimitsIsRefused(int model, int columns, int rows)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Ibm3270Emulator(model, columns, rows));
    }

    [Fact]
    public void TheLargestScreenIs16383Cells()
    {
        // 129 x 127 = 16383, which s3270 took. Its last cell is address 16382 = X'3FFE', which in
        // 14-bit form is B'00' then the 14 bits: X'3F' X'FE'.
        var e = new Ibm3270Emulator(2, 129, 127);
        var errors = new List<string>();
        e.ProtocolError += errors.Add;

        e.ProcessData(new byte[] { Command3270.EraseWriteAlternate, 0x02, Order3270.SetBufferAddress, 0x3F, 0xFE, 0xE9 });

        Assert.Empty(errors);
        Assert.Equal(16383, e.Display.Size);
        Assert.Equal(0xE9, e.Display.GetByte(16382));
        Assert.Equal('Z', (char)e.Buffer.GetCell(126, 128).Codepoint);
    }

    [Fact]
    public void TheManualsTwelveAndFourteenBitExamplesLandWhereTheManualSays()
    {
        // p. D-1: 14-bit 800 (X'320') is xx000011 00100000, so X'03' X'20'; 12-bit 160 is bits
        // 2-7 000010 and 100000, which Figure D-1 sends as X'C2' X'60'.
        var e = new Ibm3270Emulator(2, 100, 50);
        var errors = new List<string>();
        e.ProtocolError += errors.Add;

        e.ProcessData(new byte[]
        {
            Command3270.EraseWriteAlternate, 0x02,
            Order3270.SetBufferAddress, 0x03, 0x20, 0xC1,
            Order3270.SetBufferAddress, 0xC2, 0x60, 0xC2,
        });

        Assert.Empty(errors);
        Assert.Equal(0xC1, e.Display.GetByte(800));
        Assert.Equal(0xC2, e.Display.GetByte(160));

        // 800 on a 100-column screen is row 8, column 0; 160 is row 1, column 60.
        Assert.Equal('A', (char)e.Buffer.GetCell(8, 0).Codepoint);
        Assert.Equal('B', (char)e.Buffer.GetCell(1, 60).Codepoint);
    }

    [Fact]
    public void EnterOnTheLargerScreenSendsFourteenBitAddresses()
    {
        // p. 4-4: "If the partition is greater than 4096 characters, then all addresses from
        // that partition are in 14-bit form". 100 x 50 is 5000 cells. The unprotected field's
        // attribute is at 4950, so its data starts at 4951 = X'1357'; after HELLO the cursor is
        // at 4956 = X'135C'. In 12-bit form neither would fit (the largest is 4095).
        var e = new Ibm3270Emulator(2, 100, 50);
        var sent = new List<byte[]>();
        e.DataToSend += sent.Add;
        e.ProcessData(new byte[]
        {
            Command3270.EraseWriteAlternate, 0xC3,
            Order3270.SetBufferAddress, 0x13, 0x56, Order3270.StartField, 0x40, Order3270.InsertCursor,
            Order3270.SetBufferAddress, 0x13, 0x60, Order3270.StartField, 0x60,
        });

        const string Typed = "HELLO";
        for (int i = 0; i < Typed.Length; i++)
        {
            e.Display.Type(Typed[i]);
        }

        e.Display.Attention(Aid.Enter);

        Assert.Single(sent);
        Assert.Equal(new byte[] { 0x7D, 0x13, 0x5C, 0x11, 0x13, 0x57, 0xC8, 0xC5, 0xD3, 0xD3, 0xD6 }, sent[0]);
    }

    [Fact]
    public void TheUsableAreaReportsTheLargerScreenAndTwelveFourteenBitAddressing()
    {
        // p. 6-87, 6-88: ADDR X'1' is "12/14-bit addressing allowed". X'3' (12/14/16) would say
        // the terminal takes 16-bit addresses, which only a partition made by Create Partition
        // uses (p. D-1), and that is not built, so X'1' is the true answer for any size.
        // W x H is the larger screen, the 24 x 80 default fits inside it (p. 6-89), and BUFFSZ is
        // 5000 = X'1388'.
        byte[] reply = ReplyFor(new Ibm3270Emulator(2, 100, 50), 0x81);

        byte[] expected =
        {
            0x00, 0x17, 0x81, 0x81,
            0x01,                                   // 12/14-bit addressing (p. 6-88, X'1')
            0x00,
            0x00, 0x64,                             // W 100
            0x00, 0x32,                             // H 50
            0x00,
            0xFF, 0xFF, 0xFF, 0xFF,
            0xFF, 0xFF, 0xFF, 0xFF,
            0x09, 0x10,
            0x13, 0x88,                             // BUFFSZ 5000
        };
        Assert.Equal(expected, reply);
    }

    [Fact]
    public void TheImplicitPartitionReportsTheLargerScreenAsTheAlternate()
    {
        // p. 6-59 to 6-61: flags, then the size parameter X'01' of length 11 - default 80 x 24,
        // alternate 100 x 50.
        byte[] reply = ReplyFor(new Ibm3270Emulator(2, 100, 50), 0xA6);

        Assert.Equal(new byte[] { 0x00, 0x11, 0x81, 0xA6, 0x00, 0x00, 0x0B, 0x01, 0x00, 0x00, 0x50, 0x00, 0x18, 0x00, 0x64, 0x00, 0x32 }, reply);
    }

    // Sends a Query and returns the one query reply with the given code, length bytes included.
    private static byte[] ReplyFor(Ibm3270Emulator e, byte code)
    {
        var sent = new List<byte[]>();
        var errors = new List<string>();
        e.DataToSend += sent.Add;
        e.ProtocolError += errors.Add;

        e.ProcessData(Query);

        Assert.Empty(errors);
        Assert.Single(sent);
        byte[] record = sent[0];
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
