using System;
using System.Net.Sockets;
using System.Threading;

namespace RetroTerm.Core.Protocols.WebSocket.Gateway.Ethernet;

/// <summary>
/// Joins an existing Ethernet segment by dialling out to it and speaking RETH.
///
/// The peer can be anything that speaks the format: another RetroTerm, a RetroCore instance, the
/// nd100x <c>gateway.js</c> ethernet segment, or a standalone relay. Because this side dials OUT,
/// it works through NAT with no port forwarding - put the relay on any reachable host.
///
/// A background thread owns the connection: it dials, exchanges the handshake, then reads frames
/// until the link breaks, and reconnects after a short pause. That matters for a desktop app -
/// the peer being restarted, or not yet started, must not require the user to do anything.
/// </summary>
public sealed class RethTcpBackend : IEthernetBackend
{
    private const int ReconnectDelayMs = 2000;

    private readonly string _host;
    private readonly int _port;

    private readonly object _sendLock = new();
    private TcpClient? _client;
    private NetworkStream? _stream;
    private Thread? _worker;
    private volatile bool _stopRequested;
    private volatile bool _linkUp;

    /// <summary>Create a backend that dials <paramref name="host"/>:<paramref name="port"/>.</summary>
    public RethTcpBackend(string host, int port)
    {
        _host = string.IsNullOrWhiteSpace(host) ? "127.0.0.1" : host.Trim();
        _port = port > 0 ? port : RethProtocol.DefaultPort;
    }

    /// <inheritdoc />
    public event Action<byte[], int>? OnPacketReceived;

    /// <inheritdoc />
    public bool IsActive => _linkUp;

    /// <inheritdoc />
    public string Description => $"RETH segment at {_host}:{_port}";

    /// <inheritdoc />
    public void Start()
    {
        if (_worker is not null)
        {
            return;
        }

        _stopRequested = false;
        _worker = new Thread(RunConnectionLoop)
        {
            Name = "RethTcpBackend",
            IsBackground = true
        };
        _worker.Start();
    }

    /// <inheritdoc />
    public void Stop()
    {
        _stopRequested = true;
        CloseLink();

        // Join briefly rather than indefinitely: the worker is parked in a blocking Read most
        // of the time and CloseLink is what wakes it. A stuck socket must not hang app shutdown.
        _worker?.Join(2000);
        _worker = null;
        _linkUp = false;
    }

    /// <inheritdoc />
    public void SendPacket(byte[] data, int offset, int length)
    {
        if (!RethProtocol.IsPlausibleLength(length))
        {
            return;
        }

        lock (_sendLock)
        {
            var stream = _stream;
            if (stream is null || !_linkUp)
            {
                return;   // no link: an Ethernet segment with nothing on it drops frames
            }

            try
            {
                // One write, not two. A separate write for the prefix can be seen by the peer
                // before the body arrives, and a peer that reads a length then finds the socket
                // idle has no way to tell a slow sender from a truncated frame.
                var buffer = new byte[2 + length];
                RethProtocol.WriteLengthPrefix(buffer, 0, length);
                Buffer.BlockCopy(data, offset, buffer, 2, length);
                stream.Write(buffer, 0, buffer.Length);
            }
            catch (Exception)
            {
                // The read loop notices the same break and handles the reconnect.
                _linkUp = false;
            }
        }
    }

    /// <inheritdoc />
    public void Dispose() => Stop();

    private void RunConnectionLoop()
    {
        while (!_stopRequested)
        {
            try
            {
                using var client = new TcpClient();
                client.Connect(_host, _port);
                client.NoDelay = true;   // frames are already framed; batching only adds latency

                var stream = client.GetStream();

                lock (_sendLock)
                {
                    _client = client;
                    _stream = stream;
                }

                if (ExchangeHandshake(stream))
                {
                    _linkUp = true;
                    ReadFrames(stream);
                }
            }
            catch (Exception)
            {
                // Peer down, refused, or vanished mid-frame. Falls through to the pause below.
            }
            finally
            {
                _linkUp = false;
                CloseLink();
            }

            if (!_stopRequested)
            {
                Thread.Sleep(ReconnectDelayMs);
            }
        }
    }

    /// <summary>Write our greeting FIRST, then read the peer's - see <see cref="RethProtocol"/>
    /// for why the order matters.</summary>
    private static bool ExchangeHandshake(NetworkStream stream)
    {
        var hello = RethProtocol.BuildHandshake();
        stream.Write(hello, 0, hello.Length);

        var peer = new byte[RethProtocol.HandshakeLength];
        if (!ReadExactly(stream, peer, peer.Length))
        {
            return false;
        }

        return RethProtocol.IsHandshake(peer, peer.Length, out _);
    }

    private void ReadFrames(NetworkStream stream)
    {
        var header = new byte[2];
        var frame = new byte[RethProtocol.MaxFrameBytes];

        while (!_stopRequested)
        {
            if (!ReadExactly(stream, header, 2))
            {
                return;
            }

            int length = RethProtocol.ReadLengthPrefix(header, 0);
            if (!RethProtocol.IsPlausibleLength(length))
            {
                // Out of step and unrecoverable - drop the link and let the loop redial.
                return;
            }

            if (!ReadExactly(stream, frame, length))
            {
                return;
            }

            OnPacketReceived?.Invoke(frame, length);
        }
    }

    /// <summary>Fill <paramref name="count"/> bytes or fail. A stream Read is allowed to return
    /// fewer bytes than asked for, and treating a short read as a whole frame is the classic way
    /// to desynchronise a length-prefixed protocol.</summary>
    private static bool ReadExactly(NetworkStream stream, byte[] buffer, int count)
    {
        int read = 0;
        while (read < count)
        {
            int n;
            try
            {
                n = stream.Read(buffer, read, count - read);
            }
            catch (Exception)
            {
                return false;
            }

            if (n <= 0)
            {
                return false;   // peer closed
            }

            read += n;
        }

        return true;
    }

    private void CloseLink()
    {
        lock (_sendLock)
        {
            try { _stream?.Dispose(); } catch (Exception) { }
            try { _client?.Close(); } catch (Exception) { }
            _stream = null;
            _client = null;
        }
    }
}
