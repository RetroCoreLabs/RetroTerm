//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs/RetroTerm
// RetroTerm — Connecting today's machines to yesterday's systems
//

using System.Collections.Generic;

using RetroIO.Protocols.Tn3270;

using RetroTerm.Core.Terminal.Buffer;
using RetroTerm.Ibm3270;

using Xunit;

namespace RetroTerm.Tests.Ibm3270;

public class Ibm3270EmulatorTests
{
    // EW, WCC restore, SBA(0) SF(protected) "NAME", SF(unprotected) IC, SBA(20) SF(autoskip),
    // SBA(85) SF(nondisplay unprotected) "SECRET".
    private static readonly byte[] Screen =
    {
        Command3270.EraseWrite, 0x02,
        Order3270.SetBufferAddress, 0x40, 0x40,
        Order3270.StartField, 0x60, 0xD5, 0xC1, 0xD4, 0xC5,
        Order3270.StartField, 0x40,
        Order3270.InsertCursor,
        Order3270.SetBufferAddress, 0x40, 0xD4,
        Order3270.StartField, 0xF0,
        Order3270.SetBufferAddress, 0xC1, 0xD5,                         // 85 = 1*64 + 21
        Order3270.StartField, 0x4C, 0xE2, 0xC5, 0xC3, 0xD9, 0xC5, 0xE3, // nondisplay, "SECRET"
    };

    [Fact]
    public void ARecordIsDrawnIntoTheCellBufferWithTheCursorWhereTheHostPutIt()
    {
        var e = new Ibm3270Emulator();

        e.ProcessData(Screen);

        Assert.Equal('N', (char)e.Buffer.GetCell(0, 1).Codepoint);
        Assert.Equal('E', (char)e.Buffer.GetCell(0, 4).Codepoint);
        Assert.Equal(' ', (char)e.Buffer.GetCell(0, 0).Codepoint); // field attribute shows blank
        Assert.Equal(0, e.Cursor.Row);
        Assert.Equal(6, e.Cursor.Column);
    }

    [Fact]
    public void ANondisplayFieldShowsNothing()
    {
        var e = new Ibm3270Emulator();

        e.ProcessData(Screen);

        for (int col = 6; col < 12; col++)
        {
            Assert.Equal(' ', (char)e.Buffer.GetCell(1, col).Codepoint);
        }

        // The data is still in the 3270 buffer; only the drawing hides it.
        Assert.Equal(0xE2, e.Display.GetByte(86));
    }

    [Fact]
    public void ProtectedAndUnprotectedFieldsGetTheirBaseColours()
    {
        var e = new Ibm3270Emulator();

        e.ProcessData(Screen);
        e.Display.Type('x');
        e.Refresh();

        // Protected normal: blue. Unprotected normal: green. x3270's RGB values
        // (Common\c3270\cscreen.c, rgbmap), on its neutral black.
        Assert.Equal(TerminalColor.FromRgb(0x1E, 0x90, 0xFF), e.Buffer.GetCell(0, 1).Foreground);
        Assert.Equal(TerminalColor.FromRgb(0x32, 0xCD, 0x32), e.Buffer.GetCell(0, 6).Foreground);
        Assert.Equal(TerminalColor.FromRgb(0x10, 0x10, 0x10), e.Buffer.GetCell(0, 6).Background);
        Assert.Equal('x', (char)e.Buffer.GetCell(0, 6).Codepoint);
    }

    [Fact]
    public void EnterSendsTheReplyRecordThroughDataToSend()
    {
        var e = new Ibm3270Emulator();
        var sent = new List<byte[]>();
        e.DataToSend += sent.Add;
        e.ProcessData(Screen);

        e.Display.Type('h');
        e.Display.Type('i');
        e.Display.Attention(Aid.Enter);

        Assert.Single(sent);
        Assert.Equal(new byte[] { 0x7D, 0x40, 0xC8, 0x11, 0x40, 0xC6, 0x88, 0x89 }, sent[0]);
    }

    [Fact]
    public void AProtocolErrorIsPassedOnNotSwallowed()
    {
        var e = new Ibm3270Emulator();
        var errors = new List<string>();
        e.ProtocolError += errors.Add;

        // 0x99 is not a 3270 command (GA23-0059-4 p. 3-1 lists them all).
        e.ProcessData(new byte[] { 0x99, 0xC3 });

        Assert.Single(errors);
    }

    [Fact]
    public void AQueryIsAnsweredWithAQueryReply()
    {
        // Write Structured Field, Read Partition, Query (GA23-0059-4 p. 5-41). The reply starts
        // with the structured-field AID 0x88 (p. 3-6).
        var e = new Ibm3270Emulator();
        var sent = new List<byte[]>();
        var errors = new List<string>();
        e.DataToSend += sent.Add;
        e.ProtocolError += errors.Add;

        e.ProcessData(new byte[] { Command3270.WriteStructuredField, 0x00, 0x05, 0x01, 0xFF, 0x02 });

        Assert.Empty(errors);
        Assert.Single(sent);
        Assert.Equal(0x88, sent[0][0]);
    }

    [Fact]
    public void ACharacterAfterAGraphicEscapeIsDrawnFromCodePage310()
    {
        // The first byte whose code page 310 character exists and differs from code page 037's.
        byte code = 0;
        for (int b = 0x41; b < 0xFF; b++)
        {
            char ge = CodePage.Cp310.ToUnicode((byte)b);
            if (ge != '\0' && ge != CodePage.Cp037.ToUnicode((byte)b))
            {
                code = (byte)b;
                break;
            }
        }

        Assert.NotEqual(0, code);
        var e = new Ibm3270Emulator();

        e.ProcessData(new byte[] { Command3270.EraseWrite, 0x02, Order3270.SetBufferAddress, 0x40, 0x40, Order3270.GraphicEscape, code, code });

        Assert.Equal(CodePage.Cp310.ToUnicode(code), (char)e.Buffer.GetCell(0, 0).Codepoint);
        Assert.Equal(CodePage.Cp037.ToUnicode(code), (char)e.Buffer.GetCell(0, 1).Codepoint); // no GE in front
    }

    [Fact]
    public void ACharacterSetAttributeOfF1DrawsFromCodePage310WithoutAGraphicEscape()
    {
        // The last part of the record TK5's M.7 TERMTEST sends after a field-test PF key,
        // copied byte for byte from the wire (24 November 2026, Hercules Scenario10TermtestTests):
        // SBA 1760, SA(colour 0), SF, "CHARACTER ATTRIBUTES:", SF, "NORMAL", SA(blink) "BLINK",
        // SA(reverse) "REVERSE", SA(underscore) "UNDERSCORE", SA(highlight 0), then
        // SA(character set X'F1') and 29 code page 310 characters with NO Graphic Escape in
        // front, SA(character set 0), SA(highlight 0), two blanks, a null, and SBA 1897 RA to 0
        // of GE X'D3' (the box intersection, filling the rest of the last row).
        byte[] apl =
        {
            0x41, 0x57, 0x53, 0x8A, 0x8B, 0x8F, 0x9F, 0xC2, 0xD2, 0xB6, 0xB8, 0x9A, 0x9B, 0xAA, 0xAB, 0x9D, 0xBA, 0xBB, 0xC3,
            0xF0, 0xF1, 0xF2, 0xF3, 0xF4, 0xF5, 0xF6, 0xF7, 0xF8, 0xF9,
        };
        var record = new List<byte>
        {
            0xF1, 0xC3, 0x11, 0x5B, 0x60, 0x28, 0x00, 0x00,
            0x1D, 0xF8, 0xC3, 0xC8, 0xC1, 0xD9, 0xC1, 0xC3, 0xE3, 0xC5, 0xD9, 0x40, 0xC1, 0xE3, 0xE3, 0xD9, 0xC9, 0xC2, 0xE4, 0xE3, 0xC5, 0xE2, 0x7A,
            0x1D, 0xF0, 0xD5, 0xD6, 0xD9, 0xD4, 0xC1, 0xD3,
            0x28, 0x41, 0xF1, 0xC2, 0xD3, 0xC9, 0xD5, 0xD2,
            0x28, 0x41, 0xF2, 0xD9, 0xC5, 0xE5, 0xC5, 0xD9, 0xE2, 0xC5,
            0x28, 0x41, 0xF4, 0xE4, 0xD5, 0xC4, 0xC5, 0xD9, 0xE2, 0xC3, 0xD6, 0xD9, 0xC5,
            0x28, 0x41, 0x00, 0x28, 0x43, 0xF1,
        };
        record.AddRange(apl);
        record.AddRange(new byte[] { 0x28, 0x43, 0x00, 0x28, 0x41, 0x00, 0x40, 0x40, 0x00, 0x11, 0x5D, 0xE9, 0x3C, 0x40, 0x40, 0x08, 0xD3 });
        var e = new Ibm3270Emulator();

        e.ProcessData(record.ToArray());

        // Row 22: the field at 1760, "CHARACTER ATTRIBUTES:" from 1761, the field at 1782,
        // NORMAL BLINK REVERSE UNDERSCORE from 1783 to 1810, the code page 310 run from 1811 to
        // the end of the row.
        Assert.Equal('C', (char)e.Buffer.GetCell(22, 1).Codepoint);
        Assert.Equal('U', (char)e.Buffer.GetCell(22, 41).Codepoint);
        for (int i = 0; i < apl.Length; i++)
        {
            char expected = CodePage.Cp310.ToUnicode(apl[i]);
            Assert.True(expected != '\0', "code page 310 has no character for X'" + apl[i].ToString("X2", System.Globalization.CultureInfo.InvariantCulture) + "'");
            Assert.Equal(expected, (char)e.Buffer.GetCell(22, 51 + i).Codepoint);
        }

        // The run ends the row exactly (1811 + 29 = 1840). After SA(character set 0) the base
        // page again: the two blanks at the start of row 23 (X'40' is a blank in both pages, so
        // this only checks they were written).
        Assert.Equal(80, 51 + apl.Length);
        Assert.Equal(' ', (char)e.Buffer.GetCell(23, 0).Codepoint);

        // The RA with GE: 1897 (row 23, column 57) to the end of the buffer.
        for (int column = 57; column < 80; column++)
        {
            Assert.Equal(CodePage.Cp310.ToUnicode(0xD3), (char)e.Buffer.GetCell(23, column).Codepoint);
        }
    }

    [Fact]
    public void TheScreensOwnBasePageIsUsedNotAlways037()
    {
        // X'4A' is the cent sign in 037 and something else in 277 (Denmark, Norway).
        Assert.NotEqual(CodePage.Cp037.ToUnicode(0x4A), CodePage.Cp277.ToUnicode(0x4A));
        var e = new Ibm3270Emulator();
        e.Display.CodePage = CodePage.Cp277;

        e.ProcessData(new byte[] { Command3270.EraseWrite, 0x02, Order3270.SetBufferAddress, 0x40, 0x40, 0x4A });

        Assert.Equal(CodePage.Cp277.ToUnicode(0x4A), (char)e.Buffer.GetCell(0, 0).Codepoint);
    }
}
