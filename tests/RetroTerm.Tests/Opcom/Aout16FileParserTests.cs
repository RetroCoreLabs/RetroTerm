using System;
using RetroTerm.Core.Opcom;
using Xunit;

namespace RetroTerm.Tests.Opcom;

public class Aout16FileParserTests
{
    [Fact]
    public void Parse_NullData_ReturnsNull()
    {
        Assert.Null(Aout16FileParser.Parse((byte[])null!));
    }

    [Fact]
    public void Parse_TooShort_ReturnsNull()
    {
        Assert.Null(Aout16FileParser.Parse(new byte[] { 0x07, 0x01 }));
    }

    [Fact]
    public void Parse_SimpleAout_ParsesCorrectly()
    {
        // Build a minimal a.out: magic=0407, text=2 words, data=1 word, bss=0, syms=0, entry=0, zp=0, flag=0
        // Then text: 0x1234, 0x5678; data: 0xABCD
        var data = new byte[]
        {
            // Header (8 x 16-bit LE words):
            0x07, 0x01,  // magic: 0x0107 = 0407 octal
            0x02, 0x00,  // text size: 2 words
            0x01, 0x00,  // data size: 1 word
            0x00, 0x00,  // bss: 0
            0x00, 0x00,  // syms: 0
            0x00, 0x00,  // entry: 0
            0x00, 0x00,  // zp: 0
            0x00, 0x00,  // flag: 0
            // Text segment (2 words, LE):
            0x34, 0x12,  // 0x1234
            0x78, 0x56,  // 0x5678
            // Data segment (1 word, LE):
            0xCD, 0xAB,  // 0xABCD
        };

        var result = Aout16FileParser.Parse(data);
        Assert.NotNull(result);
        Assert.Equal((ushort)0x0107, result!.Magic);
        Assert.Equal((ushort)2, result.TextSize);
        Assert.Equal((ushort)1, result.DataSize);
        Assert.Equal(3, result.TotalWords);
        Assert.Equal(3, result.Words.Length);
        Assert.Equal((ushort)0x1234, result.Words[0]);
        Assert.Equal((ushort)0x5678, result.Words[1]);
        Assert.Equal((ushort)0xABCD, result.Words[2]);
        Assert.Equal((ushort)0, result.LoadAddress);
        Assert.Contains("Normal", result.MagicDescription);
    }

    [Fact]
    public void Parse_WithEntryPoint_UsesEntryAsLoadAddress()
    {
        var data = new byte[]
        {
            0x07, 0x01,  // magic: 0407
            0x01, 0x00,  // text: 1
            0x00, 0x00,  // data: 0
            0x00, 0x00,  // bss: 0
            0x00, 0x00,  // syms: 0
            0x00, 0x10,  // entry: 0x1000
            0x00, 0x00,  // zp: 0
            0x00, 0x00,  // flag: 0
            // Text: 1 word
            0xFF, 0xFF,
        };

        var result = Aout16FileParser.Parse(data);
        Assert.NotNull(result);
        Assert.Equal((ushort)0x1000, result!.LoadAddress);
        Assert.Equal((ushort)0x1000, result.EntryPoint);
    }

    [Fact]
    public void Parse_BadMagic_ReturnsNull()
    {
        var data = new byte[]
        {
            0x00, 0x00,  // bad magic
            0x01, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0xFF, 0xFF,
        };

        Assert.Null(Aout16FileParser.Parse(data));
    }

    [Fact]
    public void IsAout16File_DetectsValidMagic()
    {
        var aoutData = new byte[] { 0x07, 0x01, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 };
        Assert.True(Aout16FileParser.IsAout16File(aoutData));
    }

    [Fact]
    public void IsAout16File_RejectsInvalidMagic()
    {
        var notAout = new byte[] { (byte)'!', 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 };
        Assert.False(Aout16FileParser.IsAout16File(notAout));
    }

    [Theory]
    [InlineData(new byte[] { 0x07, 0x01 }, true)]   // 0407 - normal
    [InlineData(new byte[] { 0x08, 0x01 }, true)]   // 0410 - read-only text
    [InlineData(new byte[] { 0x09, 0x01 }, true)]   // 0411 - separated I&D
    [InlineData(new byte[] { 0x05, 0x01 }, true)]   // 0405 - shareable
    public void IsAout16File_RecognizesAllValidMagics(byte[] magicBytes, bool expected)
    {
        var data = new byte[16];
        data[0] = magicBytes[0];
        data[1] = magicBytes[1];
        Assert.Equal(expected, Aout16FileParser.IsAout16File(data));
    }

    [Fact]
    public void Parse_WithZeroPage_SkipsZeroPageBytes()
    {
        var data = new byte[]
        {
            0x07, 0x01,  // magic: 0407
            0x01, 0x00,  // text: 1
            0x00, 0x00,  // data: 0
            0x00, 0x00,  // bss: 0
            0x00, 0x00,  // syms: 0
            0x00, 0x00,  // entry: 0
            0x02, 0x00,  // zp: 2 words
            0x00, 0x00,  // flag: 0
            // Zero page (2 words, skipped):
            0x00, 0x00,
            0x00, 0x00,
            // Text: 1 word
            0x42, 0x00,  // 0x0042
        };

        var result = Aout16FileParser.Parse(data);
        Assert.NotNull(result);
        Assert.Single(result!.Words);
        Assert.Equal((ushort)0x0042, result.Words[0]);
    }
}
