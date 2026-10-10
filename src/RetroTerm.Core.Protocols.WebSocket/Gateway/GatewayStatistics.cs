using System;
using System.Threading;

namespace RetroTerm.Core.Protocols.WebSocket.Gateway;

/// <summary>
/// Tracks gateway I/O statistics for terminal, disk and Ethernet traffic.
/// All counters use Interlocked for thread-safe increment.
/// </summary>
public class GatewayStatistics
{
    // Terminal I/O
    private long _termInputFrames;
    private long _termInputBytes;
    private long _termOutputFrames;
    private long _termOutputBytes;

    // Disk I/O
    private long _diskReadOps;
    private long _diskReadBytes;
    private long _diskWriteOps;
    private long _diskWriteBytes;
    private long _diskErrors;

    // Ethernet (0x31 from the guest, 0x30 to the guest)
    private long _ethFromGuestFrames;
    private long _ethFromGuestBytes;
    private long _ethToGuestFrames;
    private long _ethToGuestBytes;
    private long _ethBadFrames;

    // Every WebSocket message, whatever its type (emulator and disk worker sockets)
    private long _wsSentMessages;
    private long _wsSentBytes;
    private long _wsReceivedMessages;
    private long _wsReceivedBytes;

    // Connection
    private long _clientConnects;
    private long _clientDisconnects;

    public long TermInputFrames => Interlocked.Read(ref _termInputFrames);
    public long TermInputBytes => Interlocked.Read(ref _termInputBytes);
    public long TermOutputFrames => Interlocked.Read(ref _termOutputFrames);
    public long TermOutputBytes => Interlocked.Read(ref _termOutputBytes);

    public long DiskReadOps => Interlocked.Read(ref _diskReadOps);
    public long DiskReadBytes => Interlocked.Read(ref _diskReadBytes);
    public long DiskWriteOps => Interlocked.Read(ref _diskWriteOps);
    public long DiskWriteBytes => Interlocked.Read(ref _diskWriteBytes);
    public long DiskErrors => Interlocked.Read(ref _diskErrors);

    public long EthFromGuestFrames => Interlocked.Read(ref _ethFromGuestFrames);
    public long EthFromGuestBytes => Interlocked.Read(ref _ethFromGuestBytes);
    public long EthToGuestFrames => Interlocked.Read(ref _ethToGuestFrames);
    public long EthToGuestBytes => Interlocked.Read(ref _ethToGuestBytes);
    public long EthBadFrames => Interlocked.Read(ref _ethBadFrames);

    public long WsSentMessages => Interlocked.Read(ref _wsSentMessages);
    public long WsSentBytes => Interlocked.Read(ref _wsSentBytes);
    public long WsReceivedMessages => Interlocked.Read(ref _wsReceivedMessages);
    public long WsReceivedBytes => Interlocked.Read(ref _wsReceivedBytes);

    public long ClientConnects => Interlocked.Read(ref _clientConnects);
    public long ClientDisconnects => Interlocked.Read(ref _clientDisconnects);

    /// <summary>
    /// When the emulator WebSocket connected. Null if not connected.
    /// </summary>
    public DateTime? EmulatorConnectedSince { get; set; }

    /// <summary>
    /// When the disk worker WebSocket connected. Null if not connected.
    /// </summary>
    public DateTime? DiskWorkerConnectedSince { get; set; }

    public void RecordTermInput(int dataBytes)
    {
        Interlocked.Increment(ref _termInputFrames);
        Interlocked.Add(ref _termInputBytes, dataBytes);
    }

    public void RecordTermOutput(int dataBytes)
    {
        Interlocked.Increment(ref _termOutputFrames);
        Interlocked.Add(ref _termOutputBytes, dataBytes);
    }

    public void RecordDiskRead(int dataBytes)
    {
        Interlocked.Increment(ref _diskReadOps);
        Interlocked.Add(ref _diskReadBytes, dataBytes);
    }

    public void RecordDiskWrite(int dataBytes)
    {
        Interlocked.Increment(ref _diskWriteOps);
        Interlocked.Add(ref _diskWriteBytes, dataBytes);
    }

    public void RecordDiskError()
    {
        Interlocked.Increment(ref _diskErrors);
    }

    public void RecordEthFromGuest(int frameBytes)
    {
        Interlocked.Increment(ref _ethFromGuestFrames);
        Interlocked.Add(ref _ethFromGuestBytes, frameBytes);
    }

    public void RecordEthToGuest(int frameBytes)
    {
        Interlocked.Increment(ref _ethToGuestFrames);
        Interlocked.Add(ref _ethToGuestBytes, frameBytes);
    }

    /// <summary>A 0x31 message whose length field does not fit the message.</summary>
    public void RecordEthBadFrame()
    {
        Interlocked.Increment(ref _ethBadFrames);
    }

    /// <summary>One WebSocket message sent by the gateway.</summary>
    public void RecordWsSent(int bytes)
    {
        Interlocked.Increment(ref _wsSentMessages);
        Interlocked.Add(ref _wsSentBytes, bytes);
    }

    /// <summary>One received fragment; a message is counted when its last fragment arrives.</summary>
    public void RecordWsReceived(int bytes, bool endOfMessage)
    {
        if (endOfMessage) Interlocked.Increment(ref _wsReceivedMessages);
        Interlocked.Add(ref _wsReceivedBytes, bytes);
    }

    public void RecordClientConnect()
    {
        Interlocked.Increment(ref _clientConnects);
    }

    public void RecordClientDisconnect()
    {
        Interlocked.Increment(ref _clientDisconnects);
    }

    /// <summary>
    /// Reset all counters to zero.
    /// </summary>
    public void Reset()
    {
        Interlocked.Exchange(ref _termInputFrames, 0);
        Interlocked.Exchange(ref _termInputBytes, 0);
        Interlocked.Exchange(ref _termOutputFrames, 0);
        Interlocked.Exchange(ref _termOutputBytes, 0);
        Interlocked.Exchange(ref _diskReadOps, 0);
        Interlocked.Exchange(ref _diskReadBytes, 0);
        Interlocked.Exchange(ref _diskWriteOps, 0);
        Interlocked.Exchange(ref _diskWriteBytes, 0);
        Interlocked.Exchange(ref _diskErrors, 0);
        Interlocked.Exchange(ref _ethFromGuestFrames, 0);
        Interlocked.Exchange(ref _ethFromGuestBytes, 0);
        Interlocked.Exchange(ref _ethToGuestFrames, 0);
        Interlocked.Exchange(ref _ethToGuestBytes, 0);
        Interlocked.Exchange(ref _ethBadFrames, 0);
        Interlocked.Exchange(ref _wsSentMessages, 0);
        Interlocked.Exchange(ref _wsSentBytes, 0);
        Interlocked.Exchange(ref _wsReceivedMessages, 0);
        Interlocked.Exchange(ref _wsReceivedBytes, 0);
        Interlocked.Exchange(ref _clientConnects, 0);
        Interlocked.Exchange(ref _clientDisconnects, 0);
        EmulatorConnectedSince = null;
        DiskWorkerConnectedSince = null;
    }
}
