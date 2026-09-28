using System;
using RetroTerm.Core.Opcom;
using Xunit;

namespace RetroTerm.Tests.Opcom;

public class OctalHelperTests
{
    [Theory]
    [InlineData(0, "000000")]
    [InlineData(1, "000001")]
    [InlineData(7, "000007")]
    [InlineData(8, "000010")]
    [InlineData(0x1FF, "000777")]
    [InlineData(0xFFFF, "177777")]
    [InlineData(0x8000, "100000")]
    [InlineData(100, "000144")]
    public void ToOctal6_FormatsCorrectly(ushort input, string expected)
    {
        Assert.Equal(expected, OctalHelper.ToOctal6(input));
    }

    [Theory]
    [InlineData("0", true, (ushort)0)]
    [InlineData("1", true, (ushort)1)]
    [InlineData("7", true, (ushort)7)]
    [InlineData("10", true, (ushort)8)]
    [InlineData("777", true, (ushort)0x1FF)]
    [InlineData("177777", true, (ushort)0xFFFF)]
    [InlineData("100000", true, (ushort)0x8000)]
    [InlineData("", false, (ushort)0)]
    [InlineData("8", false, (ushort)0)]
    [InlineData("9", false, (ushort)0)]
    [InlineData("abc", false, (ushort)0)]
    public void TryParseOctal_ParsesCorrectly(string input, bool expectedSuccess, ushort expectedValue)
    {
        bool result = OctalHelper.TryParseOctal(input.AsSpan(), out ushort value);
        Assert.Equal(expectedSuccess, result);
        if (expectedSuccess)
            Assert.Equal(expectedValue, value);
    }

    [Theory]
    [InlineData(0x30, true)]   // '0'
    [InlineData(0x37, true)]   // '7'
    [InlineData(0x38, false)]  // '8'
    [InlineData(0x2F, false)]  // '/'
    [InlineData(0x41, false)]  // 'A'
    public void IsOctalDigit_DetectsCorrectly(byte input, bool expected)
    {
        Assert.Equal(expected, OctalHelper.IsOctalDigit(input));
    }

    [Fact]
    public void TryParseOctalBytes_ParsesOctalFromAsciiBytes()
    {
        byte[] data = { 0x31, 0x30, 0x30, 0x30, 0x20 }; // "1000 "
        bool result = OctalHelper.TryParseOctalBytes(data, out ushort value, out int consumed);
        Assert.True(result);
        Assert.Equal((ushort)0x200, value); // 1000 octal = 512 decimal = 0x200
        Assert.Equal(4, consumed);
    }

    [Fact]
    public void TryParseOctalBytes_StopsAtNonOctal()
    {
        byte[] data = { 0x31, 0x37, 0x37, 0x41 }; // "177A"
        bool result = OctalHelper.TryParseOctalBytes(data, out ushort value, out int consumed);
        Assert.True(result);
        Assert.Equal(3, consumed);
        Assert.Equal((ushort)127, value); // 177 octal = 127
    }

    [Fact]
    public void TryParseOctal32_ParsesLargeValues()
    {
        bool result = OctalHelper.TryParseOctal32("21540".AsSpan(), out int value);
        Assert.True(result);
        // 21540 octal = 2*4096 + 1*512 + 5*64 + 4*8 + 0 = 8192 + 512 + 320 + 32 = 9056
        Assert.Equal(9056, value);
    }

    [Fact]
    public void FormatOctal_WritesToSpan()
    {
        Span<char> buf = stackalloc char[6];
        int written = OctalHelper.FormatOctal(0x1FF, buf);
        Assert.Equal(6, written);
        Assert.Equal("000777", new string(buf));
    }

    [Fact]
    public void WordToAscii_ConvertsPrintableChars()
    {
        Span<char> buf = stackalloc char[2];
        OctalHelper.WordToAscii(0x4142, buf); // 'AB'
        Assert.Equal('A', buf[0]);
        Assert.Equal('B', buf[1]);
    }

    [Fact]
    public void WordToAscii_ReplacesControlChars()
    {
        Span<char> buf = stackalloc char[2];
        OctalHelper.WordToAscii(0x0001, buf); // Control chars
        Assert.Equal('.', buf[0]);
        Assert.Equal('.', buf[1]);
    }

    [Fact]
    public void ToOctal6_RoundTripsWithParse()
    {
        for (ushort i = 0; i < 1000; i++)
        {
            string octal = OctalHelper.ToOctal6(i);
            bool parsed = OctalHelper.TryParseOctal(octal.AsSpan(), out ushort result);
            Assert.True(parsed);
            Assert.Equal(i, result);
        }
    }
}
