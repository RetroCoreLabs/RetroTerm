using System;
using RetroTerm.Core.Opcom;
using Xunit;

namespace RetroTerm.Tests.Opcom;

public class BpunFileParserTests
{
    [Fact]
    public void Parse_NullData_ReturnsNull()
    {
        Assert.Null(BpunFileParser.Parse((byte[])null!));
    }

    [Fact]
    public void Parse_TooShort_ReturnsNull()
    {
        Assert.Null(BpunFileParser.Parse(new byte[] { 0x30 }));
    }

    [Fact]
    public void Parse_SimpleBpun_ParsesCorrectly()
    {
        // Build a minimal BPUN:
        // Preamble: "0/100!" (start=0, load at 0, boot=100 octal)
        // Address: 0x0000 (big-endian)
        // Count: 0x0002 (2 words)
        // Data: 0x1234, 0x5678 (big-endian)
        // Checksum: 0x1234 + 0x5678 = 0x68AC
        // Action: 0x0000
        var data = new byte[]
        {
            // Preamble: "0/100!"
            (byte)'0', (byte)'/', (byte)'1', (byte)'0', (byte)'0', (byte)'!',
            // Address: 0x0000
            0x00, 0x00,
            // Count: 0x0002
            0x00, 0x02,
            // Data word 1: 0x1234
            0x12, 0x34,
            // Data word 2: 0x5678
            0x56, 0x78,
            // Checksum: 0x68AC
            0x68, 0xAC,
            // Action: 0x0000
            0x00, 0x00,
        };

        var result = BpunFileParser.Parse(data);
        Assert.NotNull(result);
        Assert.Equal(0, result!.StartAddress);
        Assert.Equal((ushort)0, result.LoadAddress);
        Assert.Equal((ushort)2, result.WordCount);
        Assert.Equal(2, result.Words.Length);
        Assert.Equal((ushort)0x1234, result.Words[0]);
        Assert.Equal((ushort)0x5678, result.Words[1]);
        Assert.True(result.ChecksumValid);
        Assert.Equal((ushort)0, result.Action);
        Assert.False(result.IsFloMon);
    }

    [Fact]
    public void Parse_ChecksumMismatch_DetectedButStillParses()
    {
        var data = new byte[]
        {
            // Preamble: "0/!"
            (byte)'0', (byte)'/', (byte)'!',
            // Address: 0x0100
            0x01, 0x00,
            // Count: 0x0001
            0x00, 0x01,
            // Data word: 0xAAAA
            0xAA, 0xAA,
            // Wrong checksum: 0x0000
            0x00, 0x00,
            // Action: 0x0000
            0x00, 0x00,
        };

        var result = BpunFileParser.Parse(data);
        Assert.NotNull(result);
        Assert.False(result!.ChecksumValid);
        Assert.Equal((ushort)0xAAAA, result.Words[0]);
    }

    [Fact]
    public void IsBpunFile_DetectsBpunByExclamation()
    {
        var bpunData = new byte[] { (byte)'0', (byte)'/', (byte)'1', (byte)'0', (byte)'0', (byte)'!', 0, 0, 0, 1 };
        Assert.True(BpunFileParser.IsBpunFile(bpunData));
    }

    [Fact]
    public void IsBpunFile_RejectsNonBpun()
    {
        var notBpun = new byte[] { 0x07, 0x01, 0x00, 0x10, 0x00, 0x04, 0x00, 0x00, 0x00, 0x00 };
        Assert.False(BpunFileParser.IsBpunFile(notBpun));
    }
}
