using System;
using RetroTerm.Core.Protocols.Net;
using Xunit;

namespace RetroTerm.Tests.Session;

/// <summary>
/// The 7-bit receive mask on a serial line.
///
/// A 7E1 line (the ND-120 console runs 115200 7E1) cannot carry an 8-bit byte, so bit 7
/// of a received byte is not data — it is the parity bit if it leaks through the driver
/// at all, or noise. The reason it must be cleared rather than tolerated is the escape
/// parser: it reads 0x80-0x9F as 8-bit C1 CONTROLS, and ESC (0x1B) carrying even parity
/// becomes 0x9B, which is CSI. That does not show one wrong character — it starts a
/// control sequence that eats the text after it.
/// </summary>
public class SevenBitParityMaskTests
{
    [Fact]
    public void OnASevenBitLine_TheHighBitIsCleared()
    {
        // "AB" with the parity bit set on each byte, as a 7E1 line could deliver it.
        var data = new byte[] { 0xC1, 0xC2 };

        SerialConnection.StripHighBitIfSevenBit(data, dataBits: 7);

        Assert.Equal((byte)'A', data[0]);
        Assert.Equal((byte)'B', data[1]);
    }

    [Fact]
    public void OnASevenBitLine_EscWithEvenParity_DoesNotBecomeC1Csi()
    {
        // THE failure this mask exists for: ESC is 0x1B, which has an odd number of set
        // bits, so even parity sets bit 7 and produces 0x9B — the C1 CSI the parser acts on.
        var data = new byte[] { 0x9B };

        SerialConnection.StripHighBitIfSevenBit(data, dataBits: 7);

        Assert.Equal(0x1B, data[0]);
    }

    [Fact]
    public void OnAnEightBitLine_HighBytesSurviveUntouched()
    {
        // A real 8-bit ND/TDV line carries its national characters at 0xA0 and above.
        // Masking those was a defect this project already fixed once inside the parser,
        // so the mask must never fire here.
        var data = new byte[] { 0xC5, 0xE6, 0xF8, 0x9B };

        SerialConnection.StripHighBitIfSevenBit(data, dataBits: 8);

        Assert.Equal(0xC5, data[0]);
        Assert.Equal(0xE6, data[1]);
        Assert.Equal(0xF8, data[2]);
        Assert.Equal(0x9B, data[3]);
    }

    [Fact]
    public void SevenBitData_ThatNeverHadTheBitSet_IsUnchanged()
    {
        // The normal case when the driver already stripped parity: nothing to do, and
        // the mask must not disturb it.
        var data = new byte[] { 0x0D, 0x0A, (byte)'X' };

        SerialConnection.StripHighBitIfSevenBit(data, dataBits: 7);

        Assert.Equal(0x0D, data[0]);
        Assert.Equal(0x0A, data[1]);
        Assert.Equal((byte)'X', data[2]);
    }

    [Fact]
    public void AnEmptyRead_IsSafe()
    {
        SerialConnection.StripHighBitIfSevenBit(Array.Empty<byte>(), dataBits: 7);
    }

    [Fact]
    public void TheSendPathUsesTheSameRule_SoTheTraceMatchesTheWire()
    {
        // The outgoing half. A 7-bit UART drops bit 7 regardless, so this changes nothing on
        // the wire — it stops the log and the protocol trace claiming a byte that never left.
        // 'Æ' in an 8-bit ND set is 0xC6 and reaches the host as 0x46 'F'.
        var outgoing = new byte[] { 0xC6 };

        SerialConnection.StripHighBitIfSevenBit(outgoing, dataBits: 7);

        Assert.Equal((byte)'F', outgoing[0]);
    }

    [Fact]
    public void OnAnEightBitLine_TheSendPathLeavesNationalCharactersAlone()
    {
        // The same byte on a real 8-bit ND line IS the character and must go out whole.
        var outgoing = new byte[] { 0xC6 };

        SerialConnection.StripHighBitIfSevenBit(outgoing, dataBits: 8);

        Assert.Equal(0xC6, outgoing[0]);
    }
}
