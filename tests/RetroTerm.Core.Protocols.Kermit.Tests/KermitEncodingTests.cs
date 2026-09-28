namespace RetroTerm.Core.Protocols.Kermit.Tests;

/// <summary>
/// Tests for Kermit data encoding (quoting) and decoding (unquoting).
/// Validates the control-character and 8th-bit quoting rules that allow
/// arbitrary binary data to traverse 7-bit terminal links.
/// </summary>
public class KermitEncodingTests
{
    private const byte CTL = KermitConst.DefaultCtlQuote;  // '#'
    private const byte EBQ = KermitConst.Default8BitQuote; // '&'

    // --- Encoding tests (raw → wire) ---

    [Fact]
    public void Encode_PrintableChar_PassesThrough()
    {
        byte[] input = [(byte)'A'];
        byte[] output = new byte[16];

        int written = KermitEncoding.Encode(input, output, 16, CTL, EBQ, false, out int consumed);

        Assert.Equal(1, consumed);
        Assert.Equal(1, written);
        Assert.Equal((byte)'A', output[0]);
    }

    [Fact]
    public void Encode_ControlChar_QuotedWithCtlPrefix()
    {
        // CR (0x0D) should become '#M' (0x23 0x4D)
        byte[] input = [0x0D];
        byte[] output = new byte[16];

        int written = KermitEncoding.Encode(input, output, 16, CTL, EBQ, false, out int consumed);

        Assert.Equal(1, consumed);
        Assert.Equal(2, written);
        Assert.Equal(CTL, output[0]);                // '#'
        Assert.Equal(KermitConst.Ctl(0x0D), output[1]); // 'M'
    }

    [Fact]
    public void Encode_NulByte_QuotedCorrectly()
    {
        // NUL (0x00) → '#@'
        byte[] input = [0x00];
        byte[] output = new byte[16];

        int written = KermitEncoding.Encode(input, output, 16, CTL, EBQ, false, out int consumed);

        Assert.Equal(1, consumed);
        Assert.Equal(2, written);
        Assert.Equal(CTL, output[0]);
        Assert.Equal((byte)'@', output[1]); // ctl(0) = 64 = '@'
    }

    [Fact]
    public void Encode_Del_QuotedCorrectly()
    {
        // DEL (127) → '#?'
        byte[] input = [0x7F];
        byte[] output = new byte[16];

        int written = KermitEncoding.Encode(input, output, 16, CTL, EBQ, false, out int consumed);

        Assert.Equal(1, consumed);
        Assert.Equal(2, written);
        Assert.Equal(CTL, output[0]);
        Assert.Equal((byte)'?', output[1]); // ctl(127) = 63 = '?'
    }

    [Fact]
    public void Encode_QuotePrefixItself_Doubled()
    {
        // '#' in data → '##'
        byte[] input = [CTL];
        byte[] output = new byte[16];

        int written = KermitEncoding.Encode(input, output, 16, CTL, EBQ, false, out int consumed);

        Assert.Equal(1, consumed);
        Assert.Equal(2, written);
        Assert.Equal(CTL, output[0]);
        Assert.Equal(CTL, output[1]);
    }

    [Fact]
    public void Encode_8BitQuoting_HighBitChar()
    {
        // 0xC1 (A with high bit) → '&A' on 7-bit link
        byte[] input = [0xC1];
        byte[] output = new byte[16];

        int written = KermitEncoding.Encode(input, output, 16, CTL, EBQ, true, out int consumed);

        Assert.Equal(1, consumed);
        Assert.Equal(2, written);
        Assert.Equal(EBQ, output[0]);         // '&'
        Assert.Equal((byte)'A', output[1]);   // stripped to 0x41
    }

    [Fact]
    public void Encode_8BitQuoting_HighBitControlChar()
    {
        // 0x8D (CR with high bit) → '&#M' on 7-bit link
        byte[] input = [0x8D];
        byte[] output = new byte[16];

        int written = KermitEncoding.Encode(input, output, 16, CTL, EBQ, true, out int consumed);

        Assert.Equal(1, consumed);
        Assert.Equal(3, written);
        Assert.Equal(EBQ, output[0]);                    // '&'
        Assert.Equal(CTL, output[1]);                    // '#'
        Assert.Equal(KermitConst.Ctl(0x0D), output[2]);  // 'M'
    }

    [Fact]
    public void Encode_8BitQuoting_EbqPrefixInData_QuotedWithCtl()
    {
        // '&' (0x26) in data when 8-bit quoting active → '#&'
        byte[] input = [EBQ];
        byte[] output = new byte[16];

        int written = KermitEncoding.Encode(input, output, 16, CTL, EBQ, true, out int consumed);

        Assert.Equal(1, consumed);
        Assert.Equal(2, written);
        Assert.Equal(CTL, output[0]);  // '#'
        Assert.Equal(EBQ, output[1]);  // '&'
    }

    [Fact]
    public void Encode_StopsWhenOutputFull()
    {
        // With maxOutput=2, can only fit one printable char + one 2-byte quoted char
        byte[] input = [(byte)'A', 0x0D, (byte)'B'];
        byte[] output = new byte[16];

        // maxOutput=3: 'A' takes 1, CR takes 2 → total 3, fits. 'B' would need 4.
        int written = KermitEncoding.Encode(input, output, 3, CTL, EBQ, false, out int consumed);

        Assert.Equal(2, consumed); // 'A' + CR consumed
        Assert.Equal(3, written);  // 'A' + '#' + 'M'
    }

    [Fact]
    public void Encode_EmptyInput_WritesNothing()
    {
        byte[] output = new byte[16];
        int written = KermitEncoding.Encode(ReadOnlySpan<byte>.Empty, output, 16, CTL, EBQ, false, out int consumed);

        Assert.Equal(0, consumed);
        Assert.Equal(0, written);
    }

    // --- Decoding tests (wire → raw) ---

    [Fact]
    public void Decode_PrintableChar_PassesThrough()
    {
        byte[] input = [(byte)'A'];
        byte[] output = new byte[16];

        int written = KermitEncoding.Decode(input, output, CTL, EBQ, false);

        Assert.Equal(1, written);
        Assert.Equal((byte)'A', output[0]);
    }

    [Fact]
    public void Decode_QuotedControlChar_RecoversOriginal()
    {
        // '#M' → CR (0x0D)
        byte[] input = [CTL, (byte)'M'];
        byte[] output = new byte[16];

        int written = KermitEncoding.Decode(input, output, CTL, EBQ, false);

        Assert.Equal(1, written);
        Assert.Equal(0x0D, output[0]);
    }

    [Fact]
    public void Decode_QuotedDel_RecoversOriginal()
    {
        // '#?' → DEL (127)
        byte[] input = [CTL, (byte)'?'];
        byte[] output = new byte[16];

        int written = KermitEncoding.Decode(input, output, CTL, EBQ, false);

        Assert.Equal(1, written);
        Assert.Equal(127, output[0]);
    }

    [Fact]
    public void Decode_DoubledQuote_RecoversLiteral()
    {
        // '##' → '#'
        byte[] input = [CTL, CTL];
        byte[] output = new byte[16];

        int written = KermitEncoding.Decode(input, output, CTL, EBQ, false);

        Assert.Equal(1, written);
        Assert.Equal(CTL, output[0]);
    }

    [Fact]
    public void Decode_8BitQuoted_HighBitChar()
    {
        // '&A' → 0xC1
        byte[] input = [EBQ, (byte)'A'];
        byte[] output = new byte[16];

        int written = KermitEncoding.Decode(input, output, CTL, EBQ, true);

        Assert.Equal(1, written);
        Assert.Equal(0xC1, output[0]);
    }

    [Fact]
    public void Decode_8BitQuoted_HighBitControlChar()
    {
        // '&#M' → 0x8D
        byte[] input = [EBQ, CTL, (byte)'M'];
        byte[] output = new byte[16];

        int written = KermitEncoding.Decode(input, output, CTL, EBQ, true);

        Assert.Equal(1, written);
        Assert.Equal(0x8D, output[0]);
    }

    [Fact]
    public void Decode_8BitQuoted_EbqLiteral()
    {
        // '#&' → '&' (literal ampersand)
        byte[] input = [CTL, EBQ];
        byte[] output = new byte[16];

        int written = KermitEncoding.Decode(input, output, CTL, EBQ, true);

        Assert.Equal(1, written);
        Assert.Equal(EBQ, output[0]);
    }

    // --- Round-trip tests ---

    [Fact]
    public void Encode_Decode_RoundTrip_AllByteValues_No8Bit()
    {
        // Test all 7-bit values (0-127) without 8th-bit quoting
        byte[] original = new byte[128];
        for (int i = 0; i < 128; i++)
            original[i] = (byte)i;

        byte[] encoded = new byte[1024];
        int encLen = KermitEncoding.Encode(original, encoded, 1024, CTL, EBQ, false, out int consumed);
        Assert.Equal(128, consumed);

        byte[] decoded = new byte[1024];
        int decLen = KermitEncoding.Decode(encoded.AsSpan(0, encLen), decoded, CTL, EBQ, false);

        Assert.Equal(128, decLen);
        for (int i = 0; i < 128; i++)
        {
            Assert.True(original[i] == decoded[i],
                $"Mismatch at index {i}: expected 0x{original[i]:X2}, got 0x{decoded[i]:X2}");
        }
    }

    [Fact]
    public void Encode_Decode_RoundTrip_AllByteValues_With8BitQuoting()
    {
        // Test all 256 byte values with 8th-bit quoting (7-bit link)
        byte[] original = new byte[256];
        for (int i = 0; i < 256; i++)
            original[i] = (byte)i;

        byte[] encoded = new byte[2048];
        int encLen = KermitEncoding.Encode(original, encoded, 2048, CTL, EBQ, true, out int consumed);
        Assert.Equal(256, consumed);

        byte[] decoded = new byte[2048];
        int decLen = KermitEncoding.Decode(encoded.AsSpan(0, encLen), decoded, CTL, EBQ, true);

        Assert.Equal(256, decLen);
        for (int i = 0; i < 256; i++)
        {
            Assert.True(original[i] == decoded[i],
                $"Mismatch at index {i}: expected 0x{original[i]:X2}, got 0x{decoded[i]:X2}");
        }
    }

    [Fact]
    public void Encode_Decode_RoundTrip_0xFF()
    {
        // 0xFF = high bit + DEL → should encode as '&#?' and decode back
        byte[] original = [0xFF];
        byte[] encoded = new byte[16];
        int encLen = KermitEncoding.Encode(original, encoded, 16, CTL, EBQ, true, out _);

        // Expect: '&' '#' '?'
        Assert.Equal(3, encLen);
        Assert.Equal(EBQ, encoded[0]);
        Assert.Equal(CTL, encoded[1]);
        Assert.Equal((byte)'?', encoded[2]);

        byte[] decoded = new byte[16];
        int decLen = KermitEncoding.Decode(encoded.AsSpan(0, encLen), decoded, CTL, EBQ, true);

        Assert.Equal(1, decLen);
        Assert.Equal(0xFF, decoded[0]);
    }
}
