using System;
using System.Text;

namespace RetroTerm.Core.Logging;

/// <summary>
/// A single structured log record.
/// </summary>
public sealed class LogEntry
{
    /// <summary>
    /// Wall-clock time the entry was created.
    /// </summary>
    public DateTime Timestamp { get; set; }

    /// <summary>
    /// Severity of the entry.
    /// </summary>
    public LogLevel Level { get; set; } = LogLevel.Info;

    /// <summary>
    /// Subsystem the entry came from.
    /// </summary>
    public LogCategory Category { get; set; } = LogCategory.General;

    /// <summary>
    /// Short component name, e.g. "TelnetConnection". May be empty.
    /// </summary>
    public string Source { get; set; } = string.Empty;

    /// <summary>
    /// Human readable message.
    /// </summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// Raw bytes associated with the entry, or null. Rendered as hex only when the viewer
    /// asks for it, so a trace with raw bytes costs nothing to keep until it is displayed.
    /// </summary>
    public byte[]? RawBytes { get; set; }

    /// <summary>
    /// Renders the entry as a single fixed-column line.
    /// </summary>
    /// <param name="includeRawBytes">
    /// When true and <see cref="RawBytes"/> is set, appends a hex dump.
    /// </param>
    /// <returns>
    /// The formatted line.
    /// </returns>
    public string Format(bool includeRawBytes)
    {
        var sb = new StringBuilder(128);

        sb.Append(Timestamp.ToString("HH:mm:ss.fff"));
        sb.Append("  ");
        sb.Append(LevelTag(Level));
        sb.Append("  ");
        sb.Append(PadRight(CategoryTag(Category), 8));
        sb.Append("  ");

        if (Source.Length != 0)
        {
            sb.Append(PadRight(Source, 20));
            sb.Append("  ");
        }

        sb.Append(Message);

        if (includeRawBytes && RawBytes != null && RawBytes.Length != 0)
        {
            sb.Append("   [");
            AppendHex(sb, RawBytes, 0, RawBytes.Length, 64);
            sb.Append(']');
        }

        return sb.ToString();
    }

    /// <inheritdoc/>
    public override string ToString() => Format(includeRawBytes: true);

    /// <summary>
    /// Appends bytes as space separated uppercase hex, truncating with an ellipsis and a
    /// total count when the run is longer than <paramref name="maxBytes"/>.
    /// </summary>
    /// <param name="sb">
    /// Target builder.
    /// </param>
    /// <param name="data">
    /// Source bytes.
    /// </param>
    /// <param name="offset">
    /// Index of the first byte to render.
    /// </param>
    /// <param name="length">
    /// Number of bytes available from <paramref name="offset"/>.
    /// </param>
    /// <param name="maxBytes">
    /// Maximum number of bytes to render before truncating.
    /// </param>
    public static void AppendHex(StringBuilder sb, byte[] data, int offset, int length, int maxBytes)
    {
        int shown = length < maxBytes ? length : maxBytes;

        for (int i = 0; i < shown; i++)
        {
            if (i != 0)
                sb.Append(' ');
            sb.Append(HexChar(data[offset + i] >> 4));
            sb.Append(HexChar(data[offset + i] & 0x0F));
        }

        if (length > shown)
        {
            sb.Append(" ... (");
            sb.Append(length);
            sb.Append(" bytes)");
        }
    }

    private static char HexChar(int nibble)
    {
        return (char)(nibble < 10 ? '0' + nibble : 'A' + (nibble - 10));
    }

    private static string PadRight(string value, int width)
    {
        return value.Length >= width ? value : value.PadRight(width);
    }

    /// <summary>
    /// Three-letter tag for a severity, for fixed-column alignment.
    /// </summary>
    /// <param name="level">
    /// The severity to tag.
    /// </param>
    /// <returns>
    /// A three character tag.
    /// </returns>
    public static string LevelTag(LogLevel level)
    {
        switch (level)
        {
            case LogLevel.Trace: return "TRC";
            case LogLevel.Debug: return "DBG";
            case LogLevel.Info: return "INF";
            case LogLevel.Warn: return "WRN";
            case LogLevel.Error: return "ERR";
            default: return "???";
        }
    }

    /// <summary>
    /// Short tag for a category, for fixed-column alignment.
    /// </summary>
    /// <param name="category">
    /// The category to tag.
    /// </param>
    /// <returns>
    /// A short tag.
    /// </returns>
    public static string CategoryTag(LogCategory category)
    {
        switch (category)
        {
            case LogCategory.General: return "general";
            case LogCategory.Network: return "net";
            case LogCategory.Telnet: return "telnet";
            case LogCategory.Session: return "session";
            case LogCategory.Parser: return "parser";
            case LogCategory.Emulator: return "emul";
            case LogCategory.Keyboard: return "kbd";
            case LogCategory.UI: return "ui";
            default: return "multi";
        }
    }
}
