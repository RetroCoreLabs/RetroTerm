using System;

namespace RetroTerm.Core.Protocols.WebSocket.Gateway.Ethernet;

/// <summary>
/// Joins the emulator's Ethernet card to whichever host mapping the user chose.
///
/// This exists as its own object rather than more branches inside
/// <see cref="GatewayListener"/> because the two have nothing to do with each other: the
/// listener owns a WebSocket and knows about terminals and disks; this owns a backend and knows
/// about Ethernet frames. The only thing they share is a way to send bytes to the emulator.
///
/// Wire format, identical to the nd100x gateway so a browser guest cannot tell the two apart:
///
///     0x30  gateway  -> emulator   [0x30][segment][lenHi][lenLo][frame...]
///     0x31  emulator -> gateway    [0x31][segment][lenHi][lenLo][frame...]
///     0x32  gateway  -> emulator   [0x32][segment][present]
///
/// The length is big-endian, as HDLC's is, and covers the frame only.
/// </summary>
public sealed class GatewayEthernetBridge : IDisposable
{
    private readonly Action<byte[], string> _sendToEmulator;
    private readonly object _stateLock = new();

    private IEthernetBackend _backend = new NullEthernetBackend();
    private int _segment;
    private bool _lastLinkReported;

    /// <summary>Create a bridge that reaches the emulator through
    /// <paramref name="sendToEmulator"/>, which takes the finished binary frame and a label
    /// for the debug window.</summary>
    public GatewayEthernetBridge(Action<byte[], string> sendToEmulator)
    {
        _sendToEmulator = sendToEmulator ?? throw new ArgumentNullException(nameof(sendToEmulator));
    }

    /// <summary>The active mapping, for the settings and statistics windows.</summary>
    public string Description
    {
        get
        {
            lock (_stateLock)
            {
                return _backend.Description;
            }
        }
    }

    /// <summary>True while frames can actually move.</summary>
    public bool IsActive
    {
        get
        {
            lock (_stateLock)
            {
                return _backend.IsActive;
            }
        }
    }

    /// <summary>Frames sent from the guest since the mapping was started.</summary>
    public long FramesFromGuest { get; private set; }

    /// <summary>Frames delivered to the guest since the mapping was started.</summary>
    public long FramesToGuest { get; private set; }

    /// <summary>Switch to the mapping named by <paramref name="spec"/> (see
    /// <see cref="EthernetBackendFactory"/>). Stops whatever was running first, so this is also
    /// how the user changes mapping without restarting the app.</summary>
    public void Configure(string? spec, int segment)
    {
        IEthernetBackend created = EthernetBackendFactory.FromSpec(spec);

        lock (_stateLock)
        {
            _backend.OnPacketReceived -= OnBackendPacket;
            _backend.Stop();
            _backend.Dispose();

            _backend = created;
            _segment = segment;
            _backend.OnPacketReceived += OnBackendPacket;
            _backend.Start();
        }

        ReportLink();
    }

    /// <summary>Stop carrying traffic and drop back to the null mapping.</summary>
    public void Stop()
    {
        lock (_stateLock)
        {
            _backend.OnPacketReceived -= OnBackendPacket;
            _backend.Stop();
            _backend.Dispose();
            _backend = new NullEthernetBackend();
        }

        ReportLink();
    }

    /// <summary>Handle a binary frame the emulator sent. Returns true when it was an Ethernet
    /// frame and has been dealt with, so the listener can stop looking at it.</summary>
    public bool TryHandleEmulatorFrame(byte[] buffer, int length)
    {
        if (buffer is null || length < 4 || buffer[0] != 0x31)
        {
            return false;
        }

        int frameLength = RethProtocol.ReadLengthPrefix(buffer, 2);

        // The guard is not paranoia: a length that overruns the message would send whatever
        // follows it in the buffer out onto a real network.
        if (frameLength > 0 && length >= 4 + frameLength)
        {
            FramesFromGuest++;

            IEthernetBackend backend;
            lock (_stateLock)
            {
                backend = _backend;
            }

            backend.SendPacket(buffer, 4, frameLength);
        }

        return true;
    }

    /// <inheritdoc />
    public void Dispose() => Stop();

    private void OnBackendPacket(byte[] data, int length)
    {
        if (length <= 0 || length > RethProtocol.MaxFrameBytes)
        {
            return;
        }

        int segment;
        lock (_stateLock)
        {
            segment = _segment;
        }

        var frame = new byte[4 + length];
        frame[0] = 0x30;
        frame[1] = (byte)(segment & 0xFF);
        RethProtocol.WriteLengthPrefix(frame, 2, length);
        Buffer.BlockCopy(data, 0, frame, 4, length);

        FramesToGuest++;
        _sendToEmulator(frame, $"eth-rx seg={segment} {length}B");
    }

    /// <summary>Tell the guest whether anything is on its wire. Sent only when the answer
    /// CHANGES - a link-status frame per poll would be noise in the debug window and work for
    /// the guest to no purpose.</summary>
    private void ReportLink()
    {
        bool up = IsActive;
        if (up == _lastLinkReported)
        {
            return;
        }

        _lastLinkReported = up;

        int segment;
        lock (_stateLock)
        {
            segment = _segment;
        }

        var frame = new byte[3];
        frame[0] = 0x32;
        frame[1] = (byte)(segment & 0xFF);
        frame[2] = (byte)(up ? 1 : 0);

        _sendToEmulator(frame, $"eth-link seg={segment} {(up ? "up" : "down")}");
    }
}
