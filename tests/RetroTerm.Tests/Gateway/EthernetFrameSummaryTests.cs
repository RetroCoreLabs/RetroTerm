using RetroTerm.Core.Protocols.WebSocket.Gateway.Ethernet;
using Xunit;

namespace RetroTerm.Tests.Gateway;

/// <summary>
/// The one-line frame decode in the gateway debug window. The TCP case is the one that matters:
/// a telnet option loop is only visible from the payload bytes, and a frame padded to 60 bytes
/// must not show its padding as data.
/// </summary>
public class EthernetFrameSummaryTests
{
    /// <summary>Ethernet II + IPv4 + 20-byte TCP header carrying <paramref name="data"/>,
    /// padded to 60 bytes the way a short frame arrives from the wire.</summary>
    private static byte[] TcpFrame(byte[] data)
    {
        int ipTotal = 20 + 20 + data.Length;
        var f = new byte[System.Math.Max(60, 14 + ipTotal)];
        f[12] = 0x08;                       // EtherType 0800
        f[14] = 0x45;                       // IPv4, IHL 5
        f[16] = (byte)(ipTotal >> 8);
        f[17] = (byte)ipTotal;
        f[23] = 6;                          // TCP
        f[26] = 192; f[27] = 168; f[28] = 199; f[29] = 40;
        f[30] = 192; f[31] = 168; f[32] = 199; f[33] = 1;
        f[34] = 0; f[35] = 23;              // source port 23
        f[36] = 0x0E; f[37] = 0x9D;         // destination port 3741
        f[46] = 0x50;                       // data offset 5 words
        f[47] = 0x18;                       // PSH ACK
        data.CopyTo(f, 54);
        return f;
    }

    [Fact]
    public void Tcp_ShowsThePayloadInHex()
    {
        var f = TcpFrame(new byte[] { 0xFF, 0xFB, 0x01 });

        string s = EthernetFrameSummary.Describe(f, 0, f.Length);

        Assert.EndsWith("TCP 192.168.199.40:23 > 192.168.199.1:3741 [P.] data 3: FF FB 01", s);
    }

    [Fact]
    public void Tcp_PaddingIsNotData()
    {
        var f = TcpFrame(new byte[0]);      // a bare ACK, padded from 54 to 60 bytes

        string s = EthernetFrameSummary.Describe(f, 0, f.Length);

        Assert.DoesNotContain("data", s);
    }

    [Fact]
    public void Tcp_LongPayloadIsCutAt16Bytes()
    {
        var data = new byte[40];
        var f = TcpFrame(data);

        string s = EthernetFrameSummary.Describe(f, 0, f.Length);

        Assert.Contains("data 40: 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 ...", s);
    }
}
