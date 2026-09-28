using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace RetroTerm.Core.Logging;

/// <summary>
/// In-memory ring-buffer logger with severity levels, subsystem categories and an
/// opt-in unbounded file sink.
/// </summary>
/// <remarks>
/// <para>
/// Design notes (see docs/LOGGING-REDESIGN-PLAN.md):
/// </para>
///   A fixed-size ring buffer replaces the old <c>Queue</c>: no per-entry dequeue churn and
///   no reallocation once steady state is reached.
///   <see cref="IsEnabled"/> must be called BEFORE building the message string. Interpolated
///   strings are evaluated at the call site, so an unguarded call allocates even when the entry
///   is going to be thrown away. Every hot-path caller (network read loop, parser dispatch)
///   is required to guard.
///   Console echo is OFF by default. The previous implementation wrote every single entry to
///   both Debug and Console, which dominated the cost of logging in the receive loop.
/// </remarks>
public static class ApplicationLogger
{
    private const int DefaultCapacity = 5000;

    private static readonly object _lock = new object();

    // Ring buffer. _count is the number of live entries; _head is the index of the OLDEST entry.
    private static LogEntry[] _entries = new LogEntry[DefaultCapacity];
    private static int _head;
    private static int _count;

    private static StreamWriter? _fileWriter;
    private static string? _filePath;

    /// <summary>
    /// Entries below this level are dropped at the call site. Defaults to <see cref="LogLevel.Info"/>
    /// so a normal session is quiet; the log viewer lowers it to Debug/Trace on demand.
    /// </summary>
    public static LogLevel MinimumLevel { get; set; } = LogLevel.Info;

    /// <summary>
    /// Bitmask of subsystems that are allowed to log. Defaults to everything.
    /// </summary>
    public static LogCategory EnabledCategories { get; set; } = LogCategory.All;

    /// <summary>
    /// When true, every entry is also written to stdout. Off by default (expensive).
    /// </summary>
    public static bool EchoToConsole { get; set; }

    /// <summary>
    /// When true, every entry is also written to the debugger output window.
    /// </summary>
    public static bool EchoToDebug { get; set; } = true;

    /// <summary>
    /// Full path of the active session log file, or null when file logging is off.
    /// </summary>
    public static string? FileLogPath => _filePath;

    /// <summary>
    /// True while an opt-in session log file is being written.
    /// </summary>
    public static bool IsFileLoggingEnabled => _fileWriter != null;

    /// <summary>
    /// Raised for every accepted entry. Subscribers must marshal to their own thread.
    /// </summary>
    public static event Action<LogEntry>? LogAdded;

    /// <summary>
    /// Fast pre-check. Call this before composing an interpolated message so that suppressed
    /// entries cost nothing but two comparisons.
    /// </summary>
    /// <param name="category">
    /// Subsystem the message belongs to.
    /// </param>
    /// <param name="level">
    /// Severity of the message.
    /// </param>
    /// <returns>
    /// True if an entry with this category and level would be recorded.
    /// </returns>
    public static bool IsEnabled(LogCategory category, LogLevel level)
    {
        return level >= MinimumLevel && (EnabledCategories & category) != 0;
    }

    /// <summary>
    /// Records a structured log entry.
    /// </summary>
    /// <param name="category">
    /// Subsystem the message belongs to.
    /// </param>
    /// <param name="level">
    /// Severity of the message.
    /// </param>
    /// <param name="source">
    /// Short component name, e.g. "TelnetConnection". Rendered in brackets.
    /// </param>
    /// <param name="message">
    /// Human readable message. Should NOT repeat the source name.
    /// </param>
    /// <param name="rawBytes">
    /// Optional raw bytes associated with the entry, rendered as hex on demand.
    /// </param>
    public static void Log(LogCategory category, LogLevel level, string source, string message, byte[]? rawBytes = null)
    {
        if (!IsEnabled(category, level))
            return;

        var entry = new LogEntry
        {
            Timestamp = DateTime.Now,
            Level = level,
            Category = category,
            Source = source,
            Message = message,
            RawBytes = rawBytes
        };

        Append(entry);
    }

    /// <summary>
    /// Backwards-compatible entry point for call sites that have not been migrated yet.
    /// Treated as <see cref="LogCategory.General"/> / <see cref="LogLevel.Info"/>.
    /// </summary>
    /// <param name="message">
    /// The message to log.
    /// </param>
    public static void Log(string message)
    {
        Log(LogCategory.General, LogLevel.Info, string.Empty, message);
    }

    private static void Append(LogEntry entry)
    {
        string? formatted = null;

        lock (_lock)
        {
            // Ring buffer insert: overwrite the oldest slot once full.
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

            if (_fileWriter != null)
            {
                formatted = entry.Format(includeRawBytes: true);
                _fileWriter.WriteLine(formatted);
            }
        }

        if (EchoToDebug || EchoToConsole)
        {
            formatted ??= entry.Format(includeRawBytes: true);

            if (EchoToDebug)
                System.Diagnostics.Debug.WriteLine(formatted);

            if (EchoToConsole)
                Console.WriteLine(formatted);
        }

        LogAdded?.Invoke(entry);
    }

    /// <summary>
    /// Returns a snapshot of the buffered entries in chronological order (oldest first).
    /// </summary>
    /// <returns>
    /// A newly allocated array of the currently buffered entries.
    /// </returns>
    public static LogEntry[] GetLogEntries()
    {
        lock (_lock)
        {
            var result = new LogEntry[_count];
            for (int i = 0; i < _count; i++)
            {
                result[i] = _entries[(_head + i) % _entries.Length];
            }
            return result;
        }
    }

    /// <summary>
    /// Discards all buffered entries. Does not affect the file sink.
    /// </summary>
    public static void Clear()
    {
        lock (_lock)
        {
            // Null out the slots so the entries (and their raw byte arrays) can be collected.
            for (int i = 0; i < _entries.Length; i++)
            {
                _entries[i] = null!;
            }
            _head = 0;
            _count = 0;
        }
    }

    /// <summary>
    /// Starts writing every subsequent entry to a session log file. Any previously buffered
    /// entries are flushed to the file first so no context is lost.
    /// </summary>
    /// <param name="path">
    /// Target file. When null, a timestamped file is created under
    /// <c>%AppData%\RetroTerm\logs\</c>.
    /// </param>
    /// <returns>
    /// The full path of the file being written.
    /// </returns>
    public static string StartFileLog(string? path = null)
    {
        // Snapshot outside the lock - GetLogEntries takes the same lock.
        var existing = GetLogEntries();

        lock (_lock)
        {
            StopFileLogCore();

            if (string.IsNullOrEmpty(path))
            {
                var dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "RetroTerm",
                    "logs");
                Directory.CreateDirectory(dir);
                var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
                path = Path.Combine(dir, $"session-{stamp}.log");
            }
            else
            {
                var dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir))
                    Directory.CreateDirectory(dir);
            }

            _filePath = path;
            _fileWriter = new StreamWriter(new FileStream(path!, FileMode.Create, FileAccess.Write, FileShare.Read), Encoding.UTF8)
            {
                AutoFlush = true
            };

            _fileWriter.WriteLine($"# RetroTerm session log started {DateTime.Now:yyyy-MM-dd HH:mm:ss}");

            // Replay what is already in the ring buffer so the file has the full picture.
            for (int i = 0; i < existing.Length; i++)
            {
                _fileWriter.WriteLine(existing[i].Format(includeRawBytes: true));
            }

            return path!;
        }
    }

    /// <summary>
    /// Stops file logging and closes the file. Safe to call when not logging.
    /// </summary>
    public static void StopFileLog()
    {
        lock (_lock)
        {
            StopFileLogCore();
        }
    }

    private static void StopFileLogCore()
    {
        if (_fileWriter == null)
            return;

        try
        {
            _fileWriter.WriteLine($"# RetroTerm session log closed {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            _fileWriter.Flush();
            _fileWriter.Dispose();
        }
        catch (IOException)
        {
            // Closing a log file must never take the application down.
        }

        _fileWriter = null;
        _filePath = null;
    }

    /// <summary>
    /// Resets all logger state. Intended for unit tests so shared static state does not leak
    /// between test classes.
    /// </summary>
    public static void ResetForTesting()
    {
        StopFileLog();
        Clear();
        MinimumLevel = LogLevel.Info;
        EnabledCategories = LogCategory.All;
        EchoToConsole = false;
        EchoToDebug = true;
    }
}
