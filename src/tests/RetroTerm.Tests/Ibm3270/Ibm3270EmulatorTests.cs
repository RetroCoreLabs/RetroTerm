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
