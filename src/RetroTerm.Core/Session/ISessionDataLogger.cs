using System;

namespace RetroTerm.Core.Session;

/// <summary>
/// Logs raw session data (bytes on the wire) to a persistent store for debugging and analysis.
/// </summary>
public interface ISessionDataLogger : IDisposable
{
    /// <summary>
    /// Gets whether logging is currently active
    /// </summary>
    bool IsLogging { get; }

    /// <summary>
    /// Starts logging to the specified file in the given format
    /// </summary>
    void Start(string filePath, SessionLogFormat format);

    /// <summary>
    /// Stops logging and flushes all buffered data
    /// </summary>
    void Stop();

    /// <summary>
    /// Logs incoming data (host → terminal)
    /// </summary>
    void LogIncoming(ReadOnlySpan<byte> data);

    /// <summary>
    /// Logs outgoing data (terminal → host)
    /// </summary>
    void LogOutgoing(ReadOnlySpan<byte> data);
}

/// <summary>
/// Format for session data logging
/// </summary>
public enum SessionLogFormat
{
    /// <summary>
    /// Binary framing: 1-byte direction (0x00=in, 0x01=out) + 4-byte LE length + raw data
    /// </summary>
    RawBinary,

    /// <summary>
    /// Human-readable hex dump with timestamps and ASCII decode
    /// </summary>
    HexDump,

    /// <summary>
    /// UTF-8 decoded text with control codes shown as mnemonics
    /// </summary>
    DecodedText
}
