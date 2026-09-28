namespace RetroTerm.Core.Protocols.Kermit.Tests;

/// <summary>
/// Tests for Send-Init parameter encoding, decoding, and negotiation.
/// </summary>
public class KermitParametersTests
{
    [Fact]
    public void Encode_DefaultParams_ProducesCorrectBytes()
    {
        var p = new KermitParameters();
        byte[] buf = new byte[16];
        int len = p.Encode(buf);

        Assert.Equal(9, len);

        // Position 1: MAXL = tochar(80)
        Assert.Equal(KermitConst.ToChar(80), buf[0]);

        // Position 2: TIME = tochar(8)
        Assert.Equal(KermitConst.ToChar(8), buf[1]);

        // Position 3: NPAD = tochar(0)
        Assert.Equal(KermitConst.ToChar(0), buf[2]);

        // Position 4: PADC = ctl(0) = '@'
        Assert.Equal(KermitConst.Ctl(0), buf[3]);

        // Position 5: EOL = tochar(13)
        Assert.Equal(KermitConst.ToChar(13), buf[4]);

        // Position 6: QCTL = '#'
        Assert.Equal((byte)'#', buf[5]);

        // Position 7: QBIN = 'Y'
        Assert.Equal((byte)'Y', buf[6]);

        // Position 8: CHKT = '1'
        Assert.Equal((byte)'1', buf[7]);

        // Position 9: REPT = ' ' (not supported)
        Assert.Equal((byte)' ', buf[8]);
    }

    [Fact]
    public void Decode_RecoversEncodedParams()
    {
        var original = new KermitParameters
        {
            MaxPacketLength = 90,
            Timeout = 10,
            PaddingCount = 0,
            PaddingChar = 0,
            EndOfLine = 13,
            ControlQuote = (byte)'#',
            EighthBitQuote = (byte)'&',
            CheckType = (byte)'1',
            RepeatPrefix = (byte)'~'
        };

        byte[] buf = new byte[16];
        original.Encode(buf);

        var decoded = new KermitParameters();
        decoded.Decode(buf.AsSpan(0, 9));

        Assert.Equal(90, decoded.MaxPacketLength);
        Assert.Equal(10, decoded.Timeout);
        Assert.Equal(0, decoded.PaddingCount);
        Assert.Equal(13, decoded.EndOfLine);
        Assert.Equal((byte)'#', decoded.ControlQuote);
        Assert.Equal((byte)'&', decoded.EighthBitQuote);
        Assert.Equal((byte)'1', decoded.CheckType);
        Assert.Equal((byte)'~', decoded.RepeatPrefix);
    }

    [Fact]
    public void Decode_ShortData_UsesDefaults()
    {
        // Only 3 bytes: MAXL, TIME, NPAD
        byte[] data = [KermitConst.ToChar(80), KermitConst.ToChar(5), KermitConst.ToChar(0)];

        var p = new KermitParameters();
        p.Decode(data);

        Assert.Equal(80, p.MaxPacketLength);
        Assert.Equal(5, p.Timeout);
        Assert.Equal(0, p.PaddingCount);
        // Remaining fields should retain defaults
        Assert.Equal(KermitConst.DefaultEol, p.EndOfLine);
        Assert.Equal(KermitConst.DefaultCtlQuote, p.ControlQuote);
    }

    [Fact]
    public void FromOptions_ProducesCorrectParams()
    {
        var options = new KermitOptions
        {
            MaxReceivePacketSize = 90,
            Timeout = 10,
            Parity = ParityMode.Even
        };

        var p = KermitParameters.FromOptions(options);

        Assert.Equal(90, p.MaxPacketLength);
        Assert.Equal(10, p.Timeout);
        // Even parity → 8th-bit quoting enabled → should advertise '&'
        Assert.Equal((byte)'&', p.EighthBitQuote);
    }

    [Fact]
    public void FromOptions_NoParity_No8BitQuoting()
    {
        var options = new KermitOptions { Parity = ParityMode.None };
        var p = KermitParameters.FromOptions(options);

        Assert.Equal((byte)'N', p.EighthBitQuote);
    }

    // --- 8th-bit quoting negotiation ---

    [Fact]
    public void Negotiate8BitQuoting_BothY_ReturnsTrue()
    {
        bool result = KermitParameters.Negotiate8BitQuoting((byte)'Y', (byte)'Y', out byte prefix);
        Assert.True(result);
        Assert.Equal(KermitConst.Default8BitQuote, prefix);
    }

    [Fact]
    public void Negotiate8BitQuoting_OneN_ReturnsFalse()
    {
        bool result = KermitParameters.Negotiate8BitQuoting((byte)'Y', (byte)'N', out _);
        Assert.False(result);
    }

    [Fact]
    public void Negotiate8BitQuoting_OneY_OneChar_UsesChar()
    {
        bool result = KermitParameters.Negotiate8BitQuoting((byte)'Y', (byte)'&', out byte prefix);
        Assert.True(result);
        Assert.Equal((byte)'&', prefix);
    }

    [Fact]
    public void Negotiate8BitQuoting_BothSameChar_UsesChar()
    {
        bool result = KermitParameters.Negotiate8BitQuoting((byte)'&', (byte)'&', out byte prefix);
        Assert.True(result);
        Assert.Equal((byte)'&', prefix);
    }

    [Fact]
    public void Negotiate8BitQuoting_DifferentChars_ReturnsFalse()
    {
        bool result = KermitParameters.Negotiate8BitQuoting((byte)'&', (byte)'~', out _);
        Assert.False(result);
    }
}
