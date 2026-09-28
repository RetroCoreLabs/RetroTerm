using System;
using System.Threading;

namespace RetroTerm.Core.Protocols.WebSocket.Gateway;

/// <summary>
/// Tracks gateway I/O statistics for terminal and disk operations.
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
        Interlocked.Exchange(ref _clientConnects, 0);
        Interlocked.Exchange(ref _clientDisconnects, 0);
        EmulatorConnectedSince = null;
        DiskWorkerConnectedSince = null;
    }
}
