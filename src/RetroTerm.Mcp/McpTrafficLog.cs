using System;
using System.Collections.Generic;

namespace RetroTerm.Mcp;

/// <summary>
/// One logged MCP event: a tool call arriving or its result going back.
/// </summary>
public sealed class McpLogEntry
{
    public DateTime Timestamp { get; }

    /// <summary>
    /// "call" or "result".
    /// </summary>
    public string Kind { get; }

    /// <summary>
    /// Tool name (terminal_send, ...).
    /// </summary>
    public string Tool { get; }

    /// <summary>
    /// Arguments (call) or response text (result), truncated for display.
    /// </summary>
    public string Text { get; }

    /// <summary>
    /// Result only: whether the tool reported an error.
    /// </summary>
    public bool IsError { get; }

    /// <summary>
    /// Result only: how long the call took.
    /// </summary>
    public TimeSpan? Elapsed { get; }

    public McpLogEntry(string kind, string tool, string text, bool isError, TimeSpan? elapsed)
    {
        Timestamp = DateTime.Now;
        Kind = kind;
        Tool = tool;
        Text = text;
        IsError = isError;
        Elapsed = elapsed;
    }

    public override string ToString()
    {
        var time = Timestamp.ToString("HH:mm:ss.fff");
        if (Kind == "call")
        {
            return $"{time}  →  {Tool}  {Text}";
        }
        var status = IsError ? "ERR" : "ok ";
        var ms = Elapsed.HasValue ? $"{(long)Elapsed.Value.TotalMilliseconds} ms" : "";
        return $"{time}  ←  {Tool}  [{status} {ms}]  {Text}";
    }
}

/// <summary>
/// In-memory ring buffer of MCP traffic (tool calls + results), fed by
/// <see cref="RetroTermToolProvider"/> and displayed live by the MCP Log window.
/// Thread-safe: calls arrive on Kestrel threads, the window reads on the UI thread.
/// </summary>
public sealed class McpTrafficLog
{
    /// <summary>
    /// Ring capacity — old entries drop off the front.
    /// </summary>
    private const int Capacity = 2000;

    /// <summary>
    /// How much of a call's args / a result's text is kept for display.
    /// </summary>
    private const int MaxTextLength = 500;

    private readonly object _lock = new object();
    private readonly List<McpLogEntry> _entries = new List<McpLogEntry>(Capacity);

    /// <summary>
    /// The app-wide log. One instance is enough: there is one MCP server per app.
    /// </summary>
    public static McpTrafficLog Instance { get; } = new McpTrafficLog();

    /// <summary>
    /// Raised for every new entry, ON THE CALLING (Kestrel) THREAD — subscribers
    /// must marshal to their own thread (the log window uses Dispatcher.Post).
    /// </summary>
    public event Action<McpLogEntry>? EntryAdded;

    public void LogCall(string tool, string argsText)
    {
        Add(new McpLogEntry("call", tool, Truncate(argsText), isError: false, elapsed: null));
    }

    public void LogResult(string tool, bool isError, string text, TimeSpan elapsed)
    {
        Add(new McpLogEntry("result", tool, Truncate(text), isError, elapsed));
    }

    private void Add(McpLogEntry entry)
    {
        lock (_lock)
        {
            if (_entries.Count >= Capacity)
            {
                _entries.RemoveAt(0);
            }
            _entries.Add(entry);
        }
        EntryAdded?.Invoke(entry);
    }

    /// <summary>
    /// Copy of the current buffer (oldest first), for a window opening late.
    /// </summary>
    public IReadOnlyList<McpLogEntry> GetSnapshot()
    {
        lock (_lock)
        {
            return _entries.ToArray();
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            _entries.Clear();
        }
    }

    private static string Truncate(string text)
    {
        // Single display line: newlines flattened, long text cut.
        text = text.Replace("\r", "").Replace('\n', '⏎');
        if (text.Length > MaxTextLength)
        {
            text = text.Substring(0, MaxTextLength) + "…";
        }
        return text;
    }
}
