using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace RetroTerm.Core.Protocols.WebSocket.Gateway.Ethernet;

/// <summary>
/// Makes RetroTerm itself the Ethernet segment: it listens, and any number of peers dial in.
///
/// A HUB, not a point-to-point link, and that distinction is the whole design. A frame from one
/// member goes to every OTHER member and up to the emulator; a frame from the emulator goes to
/// every member. Built the point-to-point way - one socket, replaced by whoever connects last -
/// it would work perfectly with two machines and silently drop the third, and two machines is the
/// first thing anybody tests by hand.
///
/// A frame is NEVER sent back to the member that produced it. A guest that receives its own frame
/// sees a duplicate of everything it sends, which reads like a duplicate-address fault.
/// </summary>
public sealed class RethHubBackend : IEthernetBackend
{
    private readonly int _port;
    private readonly object _membersLock = new();
    private readonly List<Member> _members = new();

    private TcpListener? _listener;
    private Thread? _acceptThread;
    private volatile bool _stopRequested;

    /// <summary>Listen for RETH peers on <paramref name="port"/>.</summary>
    public RethHubBackend(int port)
    {
        _port = port > 0 ? port : RethProtocol.DefaultPort;
    }

    /// <inheritdoc />
    public event Action<byte[], int>? OnPacketReceived;

    /// <inheritdoc />
    public bool IsActive { get; private set; }

    /// <inheritdoc />
    public string Description
    {
        get
        {
            int count;
            lock (_membersLock)
            {
                count = _members.Count;
            }

            return $"RETH segment on port {_port} ({count} peer{(count == 1 ? string.Empty : "s")})";
        }
    }

    /// <inheritdoc />
    public void Start()
    {
        if (_listener is not null)
        {
            return;
        }

        _stopRequested = false;

        try
        {
            _listener = new TcpListener(IPAddress.Any, _port);
            _listener.Start();
            IsActive = true;
        }
        catch (Exception)
        {
            // A port already in use must not take the gateway down - the mapping simply
            // reports itself inactive and the guest sees a dead link.
            _listener = null;
            IsActive = false;
            return;
        }

        _acceptThread = new Thread(AcceptLoop)
        {
            Name = "RethHubAccept",
            IsBackground = true
        };
        _acceptThread.Start();
    }

    /// <inheritdoc />
    public void Stop()
    {
        _stopRequested = true;
        IsActive = false;

        try { _listener?.Stop(); } catch (Exception) { }
        _listener = null;

        lock (_membersLock)
        {
            foreach (var member in _members)
            {
                member.Close();
            }

            _members.Clear();
        }

        _acceptThread?.Join(2000);
        _acceptThread = null;
    }

    /// <inheritdoc />
    public void SendPacket(byte[] data, int offset, int length)
    {
        if (!RethProtocol.IsPlausibleLength(length))
        {
            return;
        }

        var buffer = new byte[2 + length];
        RethProtocol.WriteLengthPrefix(buffer, 0, length);
        Buffer.BlockCopy(data, offset, buffer, 2, length);

        Broadcast(buffer, exclude: null);
    }

    /// <inheritdoc />
    public void Dispose() => Stop();

    private void Broadcast(byte[] framed, Member? exclude)
    {
        List<Member> snapshot;
        lock (_membersLock)
        {
            snapshot = new List<Member>(_members);
        }

        foreach (var member in snapshot)
        {
            if (ReferenceEquals(member, exclude))
            {
                continue;
            }

            if (!member.TrySend(framed))
            {
                Remove(member);
            }
        }
    }

    private void Remove(Member member)
    {
        lock (_membersLock)
        {
            _members.Remove(member);
        }

        member.Close();
    }

    private void AcceptLoop()
    {
        while (!_stopRequested)
        {
            TcpClient client;
            try
            {
                var listener = _listener;
                if (listener is null)
                {
                    return;
                }

                client = listener.AcceptTcpClient();
            }
            catch (Exception)
            {
                return;   // listener stopped, or the socket died
            }

            var member = new Member(client);
            lock (_membersLock)
            {
                _members.Add(member);
            }

            var thread = new Thread(() => ServeMember(member))
            {
                Name = "RethHubMember",
                IsBackground = true
            };
            thread.Start();
        }
    }

    private void ServeMember(Member member)
    {
        try
        {
            if (!member.ExchangeHandshake())
            {
                Remove(member);
                return;
            }

            var header = new byte[2];
            var frame = new byte[RethProtocol.MaxFrameBytes];

            while (!_stopRequested)
            {
                if (!member.ReadExactly(header, 2))
                {
                    break;
                }

                int length = RethProtocol.ReadLengthPrefix(header, 0);
                if (!RethProtocol.IsPlausibleLength(length))
                {
                    break;   // desynchronised, and a length-prefixed stream cannot recover
                }

                if (!member.ReadExactly(frame, length))
                {
                    break;
                }

                // To the other members...
                var framed = new byte[2 + length];
                RethProtocol.WriteLengthPrefix(framed, 0, length);
                Buffer.BlockCopy(frame, 0, framed, 2, length);
                Broadcast(framed, exclude: member);

                // ...and up to the emulator.
                OnPacketReceived?.Invoke(frame, length);
            }
        }
        finally
        {
            Remove(member);
        }
    }

    /// <summary>One dialled-in peer.</summary>
    private sealed class Member
    {
        private readonly TcpClient _client;
        private readonly NetworkStream _stream;
        private readonly object _sendLock = new();

        internal Member(TcpClient client)
        {
            _client = client;
            _client.NoDelay = true;
            _stream = client.GetStream();
        }

        internal bool ExchangeHandshake()
        {
            try
            {
                var hello = RethProtocol.BuildHandshake();
                _stream.Write(hello, 0, hello.Length);

                var peer = new byte[RethProtocol.HandshakeLength];
                if (!ReadExactly(peer, peer.Length))
                {
                    return false;
                }

                return RethProtocol.IsHandshake(peer, peer.Length, out _);
            }
            catch (Exception)
            {
                return false;
            }
        }

        internal bool ReadExactly(byte[] buffer, int count)
        {
            int read = 0;
            while (read < count)
            {
                int n;
                try
                {
                    n = _stream.Read(buffer, read, count - read);
                }
                catch (Exception)
                {
                    return false;
                }

                if (n <= 0)
                {
                    return false;
                }

                read += n;
            }

            return true;
        }

        internal bool TrySend(byte[] framed)
        {
            lock (_sendLock)
            {
                try
                {
                    _stream.Write(framed, 0, framed.Length);
                    return true;
                }
                catch (Exception)
                {
                    return false;
                }
            }
        }

        internal void Close()
        {
            try { _stream.Dispose(); } catch (Exception) { }
            try { _client.Close(); } catch (Exception) { }
        }
    }
}
