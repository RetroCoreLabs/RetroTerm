using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using RetroTerm.Core.Protocols.WebSocket.Gateway;
using RetroTerm.Core.Protocols.WebSocket.Gateway.Ethernet;
using Xunit;

namespace RetroTerm.Tests.Gateway;

/// <summary>
/// The Ethernet mapping: the RETH wire format, the spec grammar, and two real backends
/// carrying a frame between them over loopback sockets.
///
/// The interop tests use REAL sockets rather than a fake transport on purpose. The wire format
/// is shared with RetroCore and with the nd100x gateway, and the failure that matters is a
/// framing disagreement between processes - which a test that never serialises anything cannot
/// see.
/// </summary>
public class GatewayEthernetTests
{
    /// <summary>A free loopback port, taken by binding one and letting go.</summary>
    private static int FreePort()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        int port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        return port;
    }

    private static byte[] MakeFrame(byte tag)
    {
        var frame = new byte[60];
        for (int i = 0; i < 6; i++)
        {
            frame[i] = 0xFF;              // broadcast destination
        }

        frame[6] = 0x02;                  // locally-administered source MAC
        frame[11] = tag;
        frame[12] = 0x88;
        frame[13] = 0xB5;                 // local experimental ethertype
        return frame;
    }

    [Fact]
    public void Handshake_RoundTrips()
    {
        byte[] hello = RethProtocol.BuildHandshake();

        Assert.Equal(RethProtocol.HandshakeLength, hello.Length);
        Assert.True(RethProtocol.IsHandshake(hello, hello.Length, out byte version));
        Assert.Equal(RethProtocol.ProtocolVersion, version);
    }

    [Fact]
    public void Handshake_RejectsAnythingElse()
    {
        var notReth = new byte[] { (byte)'H', (byte)'T', (byte)'T', (byte)'P', 1 };

        Assert.False(RethProtocol.IsHandshake(notReth, notReth.Length, out _));
    }

    [Fact]
    public void LengthPrefix_IsBigEndian()
    {
        var buffer = new byte[2];
        RethProtocol.WriteLengthPrefix(buffer, 0, 0x0102);

        Assert.Equal(0x01, buffer[0]);
        Assert.Equal(0x02, buffer[1]);
        Assert.Equal(0x0102, RethProtocol.ReadLengthPrefix(buffer, 0));
    }

    [Theory]
    [InlineData(0, false)]                              // a zero-length frame is desync
    [InlineData(1, true)]
    [InlineData(RethProtocol.MaxFrameBytes, true)]
    [InlineData(RethProtocol.MaxFrameBytes + 1, false)] // past the cap is desync
    public void PlausibleLength_BoundsTheFrame(int length, bool expected)
        => Assert.Equal(expected, RethProtocol.IsPlausibleLength(length));

    [Theory]
    [InlineData(null, typeof(NullEthernetBackend))]
    [InlineData("", typeof(NullEthernetBackend))]
    [InlineData("none", typeof(NullEthernetBackend))]
    [InlineData("pcap:eth0", typeof(PcapEthernetBackend))]
    [InlineData("tcp:127.0.0.1:3094", typeof(RethTcpBackend))]
    [InlineData("127.0.0.1:3094", typeof(RethTcpBackend))]
    [InlineData("listen:3094", typeof(RethHubBackend))]
    [InlineData("tcp-listen:3094", typeof(RethHubBackend))]
    [InlineData("udp", typeof(UdpEthernetBackend))]
    [InlineData("udp:239.3.9.4:3094", typeof(UdpEthernetBackend))]
    public void Factory_MapsSpecToBackend(string? spec, Type expected)
    {
        using IEthernetBackend backend = EthernetBackendFactory.FromSpec(spec);

        Assert.IsType(expected, backend);
    }

    [Fact]
    public void Factory_FallsBackToNullRatherThanThrowing()
    {
        // A typo in a settings file must not stop the gateway serving terminals and disks.
        using IEthernetBackend backend = EthernetBackendFactory.FromSpec("pcap:");

        Assert.IsType<NullEthernetBackend>(backend);
    }

    [Theory]
    [InlineData(EthernetMappingMode.JoinSegment, "10.0.0.5:3094", "tcp:10.0.0.5:3094")]
    [InlineData(EthernetMappingMode.JoinSegment, "", "tcp:127.0.0.1:3094")]
    [InlineData(EthernetMappingMode.HostSegment, "", "listen:3094")]
    [InlineData(EthernetMappingMode.HostSegment, "4000", "listen:4000")]
    [InlineData(EthernetMappingMode.HostAdapter, "eth0", "pcap:eth0")]
    [InlineData(EthernetMappingMode.HostAdapter, "", "none")]
    [InlineData(EthernetMappingMode.Multicast, "", "udp")]
    [InlineData(EthernetMappingMode.Multicast, "239.3.9.4:3094", "udp:239.3.9.4:3094")]
    [InlineData(EthernetMappingMode.None, "ignored", "none")]
    public void MappingSpec_BuiltFromModeAndTarget(EthernetMappingMode mode, string target, string expected)
        => Assert.Equal(expected, EthernetMappingSpec.Build(enabled: true, mode, target));

    [Fact]
    public void MappingSpec_DisabledBeatsTheSelectedMode()
    {
        // Switching Ethernet off gives a dead wire whatever is still selected, so the user's
        // choice survives being turned off and on again.
        Assert.Equal("none",
            EthernetMappingSpec.Build(enabled: false, EthernetMappingMode.HostAdapter, "eth0"));
    }

    [Fact]
    public void HubAndClient_CarryAFrameBetweenThem()
    {
        int port = FreePort();

        using var hub = new RethHubBackend(port);
        hub.Start();

        var received = new ManualResetEventSlim(false);
        byte[]? got = null;
        hub.OnPacketReceived += (data, length) =>
        {
            got = data[..length];
            received.Set();
        };

        using var client = new RethTcpBackend("127.0.0.1", port);
        client.Start();

        Assert.True(SpinUntil(() => client.IsActive, TimeSpan.FromSeconds(10)),
            "the client never completed the RETH handshake with the hub");

        byte[] sent = MakeFrame(0x11);
        client.SendPacket(sent, 0, sent.Length);

        Assert.True(received.Wait(TimeSpan.FromSeconds(10)),
            "the hub never received the frame the client sent");
        Assert.Equal(sent, got);
    }

    [Fact]
    public void Hub_NeverEchoesAFrameToItsSender()
    {
        int port = FreePort();

        using var hub = new RethHubBackend(port);
        hub.Start();

        using var client = new RethTcpBackend("127.0.0.1", port);
        int backToSender = 0;
        client.OnPacketReceived += (_, _) => Interlocked.Increment(ref backToSender);
        client.Start();

        Assert.True(SpinUntil(() => client.IsActive, TimeSpan.FromSeconds(10)),
            "the client never completed the RETH handshake with the hub");

        byte[] sent = MakeFrame(0x22);
        client.SendPacket(sent, 0, sent.Length);

        // A guest that receives its own frame sees a duplicate of everything it sends, which
        // reads like a duplicate-address fault rather than a bug in the segment.
        Thread.Sleep(1500);
        Assert.Equal(0, Volatile.Read(ref backToSender));
    }

    [Fact]
    public void NullBackend_SwallowsFramesAndStaysActive()
    {
        using var backend = new NullEthernetBackend();
        backend.Start();

        // Active because the MAPPING works; there is simply nothing on the other side of it.
        Assert.True(backend.IsActive);
        backend.SendPacket(MakeFrame(0x33), 0, 60);   // must not throw

        backend.Stop();
        Assert.False(backend.IsActive);
    }

    private static bool SpinUntil(Func<bool> condition, TimeSpan timeout)
    {
        DateTime deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
            {
                return true;
            }

            Thread.Sleep(50);
        }

        return condition();
    }
}
