using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace RetroTerm.Core.Protocols.WebSocket.Gateway.Ethernet;

/// <summary>
/// Carries frames over UDP MULTICAST, so every emulator that joins the same group forms one
/// virtual Ethernet segment with no relay in the middle and nothing to start first.
///
/// One Ethernet frame is one datagram - a 10-Mbit frame is at most ~1518 bytes, comfortably
/// inside one - so there is no application-level fragmentation. UDP may drop or reorder, and
/// that is CORRECT here: real Ethernet is lossy too, and the guest's own protocol layer
/// retransmits.
///
/// Multicast loopback is left ON so two emulators on the SAME machine can see each other. That
/// also means this socket receives its own datagrams, so the guest's MAC is learned from
/// outbound frames and matching inbound ones are dropped - without it the guest sees a copy of
/// everything it sends.
///
/// Scope is LAN-local: TTL 1, and managed switches may filter it by IGMP snooping. For anything
/// crossing a NAT or the internet, use the RETH segment instead.
/// </summary>
public sealed class UdpEthernetBackend : IEthernetBackend
{
    /// <summary>Administratively-scoped default group.</summary>
    public const string DefaultGroup = "239.3.9.4";

    private readonly IPAddress _group;
    private readonly int _port;
    private readonly byte[] _localMac = new byte[6];
    private readonly object _macLock = new();

    private UdpClient? _socket;
    private Thread? _receiveThread;
    private volatile bool _stopRequested;
    private bool _macKnown;

    /// <summary>Join <paramref name="group"/>:<paramref name="port"/>.</summary>
    public UdpEthernetBackend(string? group, int port)
    {
        if (!IPAddress.TryParse(string.IsNullOrWhiteSpace(group) ? DefaultGroup : group, out var parsed))
        {
            parsed = IPAddress.Parse(DefaultGroup);
        }

        _group = parsed;
        _port = port > 0 ? port : RethProtocol.DefaultPort;
    }

    /// <inheritdoc />
    public event Action<byte[], int>? OnPacketReceived;

    /// <inheritdoc />
    public bool IsActive { get; private set; }

    /// <inheritdoc />
    public string Description => $"multicast segment {_group}:{_port}";

    /// <inheritdoc />
    public void Start()
    {
        if (_socket is not null)
        {
            return;
        }

        try
        {
            var socket = new UdpClient();
            socket.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            socket.Client.Bind(new IPEndPoint(IPAddress.Any, _port));
            socket.JoinMulticastGroup(_group);

            // Loopback ON: see the class summary. TTL 1 keeps it on this LAN.
            socket.MulticastLoopback = true;
            socket.Ttl = 1;

            _socket = socket;
            IsActive = true;
        }
        catch (Exception)
        {
            _socket = null;
            IsActive = false;
            return;
        }

        _stopRequested = false;
        _receiveThread = new Thread(ReceiveLoop)
        {
            Name = "UdpEthernetReceive",
            IsBackground = true
        };
        _receiveThread.Start();
    }

    /// <inheritdoc />
    public void Stop()
    {
        _stopRequested = true;
        IsActive = false;

        var socket = _socket;
        _socket = null;
        if (socket is not null)
        {
            try { socket.DropMulticastGroup(_group); } catch (Exception) { }
            try { socket.Close(); } catch (Exception) { }
        }

        _receiveThread?.Join(2000);
        _receiveThread = null;
    }

    /// <inheritdoc />
    public void SendPacket(byte[] data, int offset, int length)
    {
        var socket = _socket;
        if (socket is null || !IsActive || length <= 0)
        {
            return;
        }

        NoteLocalMac(data, offset, length);

        try
        {
            var datagram = new byte[length];
            Buffer.BlockCopy(data, offset, datagram, 0, length);
            socket.Send(datagram, datagram.Length, new IPEndPoint(_group, _port));
        }
        catch (Exception)
        {
            // Reported, not swallowed: a dead socket that still claims a healthy link is the
            // hardest kind of network fault to diagnose.
            IsActive = false;
        }
    }

    /// <inheritdoc />
    public void Dispose() => Stop();

    private void NoteLocalMac(byte[] frame, int offset, int length)
    {
        if (length < 12)
        {
            return;
        }

        lock (_macLock)
        {
            if (_macKnown)
            {
                return;
            }

            Buffer.BlockCopy(frame, offset + 6, _localMac, 0, 6);
            _macKnown = true;
        }
    }

    private void ReceiveLoop()
    {
        var any = new IPEndPoint(IPAddress.Any, 0);

        while (!_stopRequested)
        {
            byte[] datagram;
            try
            {
                var socket = _socket;
                if (socket is null)
                {
                    return;
                }

                datagram = socket.Receive(ref any);
            }
            catch (Exception)
            {
                return;   // socket closed by Stop, or a hard error
            }

            if (datagram.Length < 14 || IsOurOwnTransmission(datagram))
            {
                continue;
            }

            OnPacketReceived?.Invoke(datagram, datagram.Length);
        }
    }

    private bool IsOurOwnTransmission(byte[] data)
    {
        lock (_macLock)
        {
            if (!_macKnown)
            {
                return false;
            }

            for (int i = 0; i < 6; i++)
            {
                if (data[6 + i] != _localMac[i])
                {
                    return false;
                }
            }

            return true;
        }
    }
}
