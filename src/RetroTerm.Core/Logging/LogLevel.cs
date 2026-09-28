namespace RetroTerm.Core.Logging;

/// <summary>
/// Severity of a log entry. Ordered from most verbose to most severe so that a
/// simple >= comparison against <see cref="ApplicationLogger.MinimumLevel"/> works.
/// </summary>
public enum LogLevel
{
    /// <summary>
    /// Byte-level firehose: raw network reads/writes, per-character parser events.
    /// </summary>
    Trace = 0,

    /// <summary>
    /// Decoded protocol activity: escape sequences, telnet negotiation, key mappings.
    /// </summary>
    Debug = 1,

    /// <summary>
    /// Normal lifecycle events: connect, disconnect, emulator switched.
    /// </summary>
    Info = 2,

    /// <summary>
    /// Something unexpected but survivable - notably UNKNOWN/unhandled escape sequences.
    /// </summary>
    Warn = 3,

    /// <summary>
    /// A failure the user should know about.
    /// </summary>
    Error = 4
}
