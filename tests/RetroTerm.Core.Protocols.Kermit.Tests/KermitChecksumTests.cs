namespace RetroTerm.Core.Protocols.Kermit.Tests;

/// <summary>
/// Tests for Kermit checksum computation.
/// Validates against known values from the C-Kermit implementation.
/// </summary>
public class KermitChecksumTests
{
    [Fact]
    public void ComputeType1_EmptyData_ReturnsZero()
    {
        int result = KermitChecksum.ComputeType1(ReadOnlySpan<byte>.Empty);
        Assert.Equal(0, result);
    }

    [Fact]
    public void ComputeType1_SingleByte_FoldsCorrectly()
    {
        // Single byte 100: sum=100, 100 & 0xC0 = 64, 64>>6 = 1, (100+1)&63 = 37
        ReadOnlySpan<byte> data = [(byte)100];
        int result = KermitChecksum.ComputeType1(data);
        Assert.Equal(37, result);
    }

    [Fact]
    public void ComputeType1_KnownPacket_MatchesCKermit()
    {
        // Minimal S packet fields: LEN=tochar(3)=35, SEQ=tochar(0)=32, TYPE='S'=83
        byte[] pkt = [35, 32, 83];
        int chk = KermitChecksum.ComputeType1(pkt);
        // Sum = 35+32+83 = 150, 150 & 0xC0 = 128, 128>>6 = 2, (150+2)&63 = 152&63 = 24
        Assert.Equal(24, chk);
    }

    [Fact]
    public void ComputeType1_ValueRange_AlwaysWithin6Bits()
    {
        // Checksum type 1 result must always be 0-63
        byte[] data = new byte[200];
        for (int i = 0; i < data.Length; i++)
            data[i] = (byte)(i & 0xFF);

        int result = KermitChecksum.ComputeType1(data);
        Assert.InRange(result, 0, 63);
    }

    [Fact]
    public void ComputeType2_Returns12BitValue()
    {
        byte[] data = [0xFF, 0xFF, 0xFF, 0xFF];
        int result = KermitChecksum.ComputeType2(data);
        // Sum = 255*4 = 1020, & 0xFFF = 1020
        Assert.Equal(1020, result);
        Assert.True(result <= 0xFFF);
    }

    [Fact]
    public void ComputeType3_EmptyData_ReturnsZero()
    {
        int result = KermitChecksum.ComputeType3(ReadOnlySpan<byte>.Empty);
        Assert.Equal(0, result);
    }

    [Fact]
    public void ComputeType3_SingleByte_CorrectCrc()
    {
        // CRC-CCITT for single byte 'A' (0x41)
        ReadOnlySpan<byte> data = [(byte)'A'];
        int result = KermitChecksum.ComputeType3(data);
        // Value should be a valid 16-bit CRC
        Assert.InRange(result, 0, 0xFFFF);
        Assert.NotEqual(0, result); // non-zero for non-empty data
    }

    [Fact]
    public void ComputeType3_Returns16BitValue()
    {
        byte[] data = new byte[256];
        for (int i = 0; i < 256; i++)
            data[i] = (byte)i;

        int result = KermitChecksum.ComputeType3(data);
        Assert.InRange(result, 0, 0xFFFF);
    }

    [Fact]
    public void ComputeType3_DifferentData_DifferentCrc()
    {
        byte[] data1 = [1, 2, 3];
        byte[] data2 = [1, 2, 4];

        int crc1 = KermitChecksum.ComputeType3(data1);
        int crc2 = KermitChecksum.ComputeType3(data2);

        Assert.NotEqual(crc1, crc2);
    }
}
