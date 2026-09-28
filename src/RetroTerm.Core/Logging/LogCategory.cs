using System;

namespace RetroTerm.Core.Logging;

/// <summary>
/// Subsystem a log entry originated from. Declared as [Flags] so the log viewer can
/// enable/disable whole subsystems with a single bitmask test (no per-entry string compare).
/// </summary>
[Flags]
public enum LogCategory
{
    /// <summary>
    /// No categories (used to mute everything).
    /// </summary>
    None = 0,

    /// <summary>
    /// Uncategorised / general application messages.
    /// </summary>
    General = 1 << 0,

    /// <summary>
    /// Transport layer: TCP reads/writes, serial port, IAC escaping.
    /// </summary>
    Network = 1 << 1,

    /// <summary>
    /// Telnet option negotiation (WILL/WONT/DO/DONT, subnegotiation).
    /// </summary>
    Telnet = 1 << 2,

    /// <summary>
    /// TerminalSession coordination: routing between connection and emulator.
    /// </summary>
    Session = 1 << 3,

    /// <summary>
    /// Escape sequence parser dispatch events.
    /// </summary>
    Parser = 1 << 4,

    /// <summary>
    /// Emulator behaviour: cursor moves, attributes, buffer edits, TDV features.
    /// </summary>
    Emulator = 1 << 5,

    /// <summary>
    /// Keyboard mapping and key binding resolution.
    /// </summary>
    Keyboard = 1 << 6,

    /// <summary>
    /// UI/window/rendering concerns.
    /// </summary>
    UI = 1 << 7,

    /// <summary>
    /// All categories enabled.
    /// </summary>
    All = General | Network | Telnet | Session | Parser | Emulator | Keyboard | UI
}
