using System;

namespace RetroTerm.Core.Logging;

/// <summary>
/// Central sink for the protocol trace shown in the Protocol Monitor window.
/// </summary>
/// <remarks>
/// <para>
/// There is exactly ONE feed point per direction:
/// </para>
/// - RX is fed by <c>TerminalSession</c>, after telnet de-escaping.
/// - TX is fed by <c>TerminalSession</c>, before handing bytes to the connection.
/// <para>
/// That is what removes the 4x duplication the old log had: the transport, the session and the
/// emulator each dumped the same payload. Now the payload is traced once, on the way past.
/// </para>
/// <para>
/// Tracing is OFF by default. When off, <see cref="TraceIncoming"/> and
/// <see cref="TraceOutgoing"/> return immediately without touching the byte spans, so a
/// normal session pays a single boolean test per network block.
/// </para>
/// </remarks>
public static class ProtocolTracer
{
    private const int DefaultCapacity = 20000;

    private static readonly object _lock = new object();

    // Ring buffer of decoded items. _head is the index of the OLDEST entry.
    private static ProtocolTraceEntry[] _entries = new ProtocolTraceEntry[DefaultCapacity];
    private static int _head;
    private static int _count;

    // Next entry id. Strictly increasing for the whole app run — survives Clear so
    // "since id >= N" polling from MCP never sees the same id twice.
    private static long _nextId = 1;

    private static readonly WireScanner _rxScanner = new WireScanner(TraceDirection.Rx, Append);
    private static readonly WireScanner _txScanner = new WireScanner(TraceDirection.Tx, Append);

    /// <summary>
    /// Master switch. While false the tracer does no work at all. The Protocol Monitor window
    /// turns it on while it is open.
    /// </summary>
    public static bool Enabled { get; set; }

    /// <summary>
    /// When true, each whole network read/write is also recorded as a
    /// <see cref="TraceKind.RawBlock"/> entry for the raw pane.
    /// </summary>
    public static bool CaptureRawBlocks { get; set; } = true;

    /// <summary>
    /// Raised for every trace entry. Subscribers must marshal to their own thread.
    /// </summary>
    public static event Action<ProtocolTraceEntry>? TraceAdded;

    /// <summary>
    /// Traces a block of bytes received from the host, after telnet de-escaping.
    /// </summary>
    /// <param name="data">
    /// The received bytes.
    /// </param>
    public static void TraceIncoming(ReadOnlySpan<byte> data)
    {
        if (!Enabled || data.Length == 0)
            return;

        if (CaptureRawBlocks)
            AppendRawBlock(TraceDirection.Rx, data);

        _rxScanner.Feed(data);
    }

    /// <summary>
    /// Traces a block of bytes about to be sent to the host, before telnet IAC escaping.
    /// </summary>
    /// <param name="data">
    /// The bytes being sent.
    /// </param>
    public static void TraceOutgoing(ReadOnlySpan<byte> data)
    {
        if (!Enabled || data.Length == 0)
            return;

        if (CaptureRawBlocks)
            AppendRawBlock(TraceDirection.Tx, data);

        _txScanner.Feed(data);
    }

    /// <summary>
    /// Records a free-text marker in the trace, e.g. "connected" or "emulator switched".
    /// Markers are recorded even when byte tracing is enabled, so the timeline has context.
    /// </summary>
    /// <param name="direction">
    /// Direction the marker relates to.
    /// </param>
    /// <param name="text">
    /// The marker text.
    /// </param>
    public static void TraceMarker(TraceDirection direction, string text)
    {
        if (!Enabled)
            return;

        Append(new ProtocolTraceEntry
        {
            Timestamp = DateTime.Now,
            Direction = direction,
            Kind = TraceKind.Marker,
            Bytes = Array.Empty<byte>(),
            Mnemonic = "--",
            Name = text,
            Rendered = "--",
            IsKnown = true
        });
    }

    private static void AppendRawBlock(TraceDirection direction, ReadOnlySpan<byte> data)
    {
        var bytes = new byte[data.Length];
        data.CopyTo(bytes);

        Append(new ProtocolTraceEntry
        {
            Timestamp = DateTime.Now,
            Direction = direction,
            Kind = TraceKind.RawBlock,
            Bytes = bytes,
            Mnemonic = "BLOCK",
            Name = data.Length + " bytes",
            Rendered = string.Empty,
            IsKnown = true
        });
    }

    private static void Append(ProtocolTraceEntry entry)
    {
        lock (_lock)
        {
            entry.Id = _nextId++;
            if (_count < _entries.Length)
            {
                _entries[(_head + _count) % _entries.Length] = entry;
                _count++;
            }
            else
            {
                _entries[_head] = entry;
                _head = (_head + 1) % _entries.Length;
            }
        }

        // An unrecognised sequence is exactly the thing a protocol debugger is hunting for,
        // so it is promoted into the main application log at Warn level.
        if (!entry.IsKnown && ApplicationLogger.IsEnabled(LogCategory.Parser, LogLevel.Warn))
        {
            ApplicationLogger.Log(
                LogCategory.Parser,
                LogLevel.Warn,
                entry.DirectionTag,
                $"Unhandled sequence {entry.Rendered} - {entry.Arguments}",
                entry.Bytes);
        }

        TraceAdded?.Invoke(entry);
    }

    /// <summary>
    /// Returns a snapshot of the buffered trace entries in chronological order.
    /// </summary>
    /// <returns>
    /// A newly allocated array of the currently buffered entries.
    /// </returns>
    public static ProtocolTraceEntry[] GetEntries()
    {
        lock (_lock)
        {
            var result = new ProtocolTraceEntry[_count];
            for (int i = 0; i < _count; i++)
            {
                result[i] = _entries[(_head + i) % _entries.Length];
            }
            return result;
        }
    }

    /// <summary>
    /// Returns buffered entries with <see cref="ProtocolTraceEntry.Id"/> >= minId,
    /// oldest first, at most maxCount — the incremental poll MCP uses
    /// ("give me everything since the last id I saw").
    /// </summary>
    public static ProtocolTraceEntry[] GetEntriesSince(long minId, int maxCount)
    {
        lock (_lock)
        {
            // Entries are stored oldest→newest with strictly increasing ids: find the
            // first match, then take up to maxCount from there.
            int first = -1;
            for (int i = 0; i < _count; i++)
            {
                if (_entries[(_head + i) % _entries.Length].Id >= minId)
                {
                    first = i;
                    break;
                }
            }
            if (first < 0 || maxCount <= 0)
            {
                return Array.Empty<ProtocolTraceEntry>();
            }
            int take = _count - first;
            if (take > maxCount) take = maxCount;
            var result = new ProtocolTraceEntry[take];
            for (int i = 0; i < take; i++)
            {
                result[i] = _entries[(_head + first + i) % _entries.Length];
            }
            return result;
        }
    }

    /// <summary>
    /// The id the NEXT entry will get — "poll from here" for a fresh client.
    /// </summary>
    public static long NextId
    {
        get { lock (_lock) { return _nextId; } }
    }

    /// <summary>
    /// Discards all buffered trace entries and resets both scanners.
    /// </summary>
    public static void Clear()
    {
        lock (_lock)
        {
            for (int i = 0; i < _entries.Length; i++)
            {
                _entries[i] = null!;
            }
            _head = 0;
            _count = 0;
        }

        _rxScanner.Reset();
        _txScanner.Reset();
    }

    /// <summary>
    /// Resets all tracer state. Intended for unit tests.
    /// </summary>
    public static void ResetForTesting()
    {
        Enabled = false;
        CaptureRawBlocks = true;
        Clear();
        lock (_lock) { _nextId = 1; } // tests only — production Clear keeps ids increasing
    }
}
