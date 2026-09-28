using System;
using System.Diagnostics;
using System.Threading;

namespace RetroTerm.Core.Protocols.Kermit;

/// <summary>
/// Live statistics for a Kermit file transfer session.
/// Thread-safe — updated from engine callbacks, read from UI timer.
/// </summary>
public sealed class KermitTransferStatistics
{
    private long _charsSent;
    private long _charsReceived;
    private long _fileBytes;
    private int _packetsSent;
    private int _packetsReceived;
    private int _retries;
    private int _timeouts;
    private readonly Stopwatch _wallClock = new();

    /// <summary>
    /// Total raw bytes sent on the wire (including framing).
    /// </summary>
    public long CharsSent => Volatile.Read(ref _charsSent);

    /// <summary>
    /// Total raw bytes received from the wire.
    /// </summary>
    public long CharsReceived => Volatile.Read(ref _charsReceived);

    /// <summary>
    /// Total bytes read from or written to files (payload only).
    /// </summary>
    public long FileBytes => Volatile.Read(ref _fileBytes);

    /// <summary>
    /// Number of packets sent.
    /// </summary>
    public int PacketsSent => Volatile.Read(ref _packetsSent);

    /// <summary>
    /// Number of packets received.
    /// </summary>
    public int PacketsReceived => Volatile.Read(ref _packetsReceived);

    /// <summary>
    /// Number of packet retransmissions.
    /// </summary>
    public int Retries => Volatile.Read(ref _retries);

    /// <summary>
    /// Number of timeouts that fired.
    /// </summary>
    public int Timeouts => Volatile.Read(ref _timeouts);

    /// <summary>
    /// Wall-clock elapsed time since transfer started.
    /// </summary>
    public TimeSpan Elapsed => _wallClock.Elapsed;

    /// <summary>
    /// Effective baud rate (file bytes per second × 10 for start/stop bits).
    /// </summary>
    public int EffectiveBaud
    {
        get
        {
            double seconds = _wallClock.Elapsed.TotalSeconds;
            if (seconds < 0.001) return 0;
            long bytes = Volatile.Read(ref _fileBytes);
            return (int)(bytes * 10.0 / seconds);
        }
    }

    /// <summary>
    /// Current protocol state name.
    /// </summary>
    public string StateName { get; set; } = "Idle";

    /// <summary>
    /// Current file being transferred.
    /// </summary>
    public string CurrentFile { get; set; } = "";

    /// <summary>
    /// Negotiated 8-bit quoting status.
    /// </summary>
    public bool Use8BitQuoting { get; set; }

    /// <summary>
    /// Negotiated block check type (1, 2, or 3).
    /// </summary>
    public int BlockCheckType { get; set; } = 1;

    /// <summary>
    /// Negotiated max send data length.
    /// </summary>
    public int MaxSendDataLength { get; set; }

    /// <summary>
    /// Last error message, if any.
    /// </summary>
    public string? LastError { get; set; }

    /// <summary>
    /// Event raised when statistics change (for UI refresh).
    /// </summary>
    public event Action? Updated;

    public void NotifyUpdated() => Updated?.Invoke();

    public void Start() => _wallClock.Start();
    public void Stop() => _wallClock.Stop();

    public void AddCharsSent(int count)
    {
        Interlocked.Add(ref _charsSent, count);
        Interlocked.Increment(ref _packetsSent);
        Updated?.Invoke();
    }

    public void AddCharsReceived(int count)
    {
        Interlocked.Add(ref _charsReceived, count);
        Interlocked.Increment(ref _packetsReceived);
        Updated?.Invoke();
    }

    public void AddFileBytes(long count)
    {
        Interlocked.Add(ref _fileBytes, count);
        Updated?.Invoke();
    }

    public void AddRetry()
    {
        Interlocked.Increment(ref _retries);
        Updated?.Invoke();
    }

    public void AddTimeout()
    {
        Interlocked.Increment(ref _timeouts);
        Updated?.Invoke();
    }

    public void Reset()
    {
        Volatile.Write(ref _charsSent, 0);
        Volatile.Write(ref _charsReceived, 0);
        Volatile.Write(ref _fileBytes, 0);
        Volatile.Write(ref _packetsSent, 0);
        Volatile.Write(ref _packetsReceived, 0);
        Volatile.Write(ref _retries, 0);
        Volatile.Write(ref _timeouts, 0);
        _wallClock.Reset();
        StateName = "Idle";
        CurrentFile = "";
        LastError = null;
        Updated?.Invoke();
    }
}
