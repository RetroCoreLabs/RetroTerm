namespace RetroTerm.Core.Protocols.Kermit.Tests;

/// <summary>
/// Tests for the fundamental Kermit encoding primitives: tochar, unchar, ctl.
/// These are the building blocks of the entire protocol, so correctness is critical.
/// </summary>
public class KermitConstTests
{
    [Theory]
    [InlineData(0, 32)]   // tochar(0) = space
    [InlineData(1, 33)]   // tochar(1) = '!'
    [InlineData(13, 45)]  // tochar(13) = '-'
    [InlineData(63, 95)]  // tochar(63) = '_'
    [InlineData(94, 126)] // tochar(94) = '~' (max for short packet LEN)
    public void ToChar_ConvertsIntegerToPrintableAscii(int input, byte expected)
    {
        byte result = KermitConst.ToChar(input);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(32, 0)]
    [InlineData(33, 1)]
    [InlineData(45, 13)]
    [InlineData(95, 63)]
    [InlineData(126, 94)]
    public void UnChar_ReverseOfToChar(int input, int expected)
    {
        int result = KermitConst.UnChar(input);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void ToChar_And_UnChar_AreInverses()
    {
        for (int i = 0; i <= 94; i++)
        {
            byte encoded = KermitConst.ToChar(i);
            int decoded = KermitConst.UnChar(encoded);
            Assert.Equal(i, decoded);
        }
    }

    [Theory]
    [InlineData(0x00, 0x40)]  // NUL → '@'
    [InlineData(0x01, 0x41)]  // SOH → 'A'
    [InlineData(0x0D, 0x4D)]  // CR → 'M'
    [InlineData(0x0A, 0x4A)]  // LF → 'J'
    [InlineData(0x1B, 0x5B)]  // ESC → '['
    [InlineData(0x1F, 0x5F)]  // US → '_'
    [InlineData(0x7F, 0x3F)]  // DEL → '?'
    public void Ctl_ConvertsControlCharsToAndFromPrintable(byte input, byte expected)
    {
        byte result = KermitConst.Ctl(input);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void Ctl_IsOwnInverse()
    {
        // ctl(ctl(x)) == x for all values
        for (int i = 0; i < 256; i++)
        {
            byte first = KermitConst.Ctl(i);
            byte second = KermitConst.Ctl(first);
            Assert.Equal((byte)i, second);
        }
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(1, true)]
    [InlineData(31, true)]
    [InlineData(127, true)]
    [InlineData(32, false)]  // space is not control
    [InlineData(65, false)]  // 'A'
    [InlineData(126, false)] // '~'
    public void IsControlChar_IdentifiesControlCharacters(byte input, bool expected)
    {
        Assert.Equal(expected, KermitConst.IsControlChar(input));
    }

    [Theory]
    [InlineData(0x00, false)]
    [InlineData(0x7F, false)]
    [InlineData(0x80, true)]
    [InlineData(0xFF, true)]
    public void HasHighBit_DetectsHighBitSet(byte input, bool expected)
    {
        Assert.Equal(expected, KermitConst.HasHighBit(input));
    }

    [Fact]
    public void StripParity_ClearsHighBit()
    {
        Assert.Equal(0x41, KermitConst.StripParity(0xC1));
        Assert.Equal(0x00, KermitConst.StripParity(0x80));
        Assert.Equal(0x41, KermitConst.StripParity(0x41)); // no-op when already clear
    }

    [Theory]
    [InlineData(0x00, 0x00)] // 00000000 → 0 bits → even → parity 0
    [InlineData(0x01, 0x81)] // 00000001 → 1 bit  → odd  → parity 1
    [InlineData(0x03, 0x03)] // 00000011 → 2 bits → even → parity 0
    [InlineData(0x41, 0x41)] // 01000001 → 2 bits → even → parity 0
    [InlineData(0x07, 0x87)] // 00000111 → 3 bits → odd  → parity 1
    public void AddEvenParity_SetsParityBitCorrectly(byte input, byte expected)
    {
        Assert.Equal(expected, KermitConst.AddEvenParity(input));
    }

    [Fact]
    public void AddEvenParity_AlwaysProducesEvenBitCount()
    {
        for (int i = 0; i < 128; i++)
        {
            byte result = KermitConst.AddEvenParity((byte)i);
            int bits = System.Numerics.BitOperations.PopCount((uint)result);
            Assert.True(bits % 2 == 0, $"Byte 0x{i:X2} → 0x{result:X2} has {bits} bits (expected even)");
        }
    }

    [Theory]
    [InlineData(63, true)]   // '?' — ctl(DEL)
    [InlineData(64, true)]   // '@' — ctl(NUL)
    [InlineData(77, true)]   // 'M' — ctl(CR)
    [InlineData(95, true)]   // '_' — ctl(US)
    [InlineData(62, false)]  // '>' — below range
    [InlineData(96, false)]  // '`' — above range
    [InlineData(35, false)]  // '#' — the quote char itself
    public void IsCtlEncoded_IdentifiesCtlRange(byte input, bool expected)
    {
        Assert.Equal(expected, KermitConst.IsCtlEncoded(input));
    }
}
