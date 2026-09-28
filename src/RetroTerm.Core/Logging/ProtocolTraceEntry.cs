using System;
using System.Text;

namespace RetroTerm.Core.Logging;

/// <summary>
/// Direction of a traced item relative to the terminal.
/// </summary>
public enum TraceDirection
{
    /// <summary>
    /// Host to terminal (received).
    /// </summary>
    Rx = 0,

    /// <summary>
    /// Terminal to host (sent).
    /// </summary>
    Tx = 1
}

/// <summary>
/// What kind of item a trace entry represents.
/// </summary>
public enum TraceKind
{
    /// <summary>
    /// A whole network read or write, before decoding. Shown in the raw pane.
    /// </summary>
    RawBlock = 0,

    /// <summary>
    /// A run of printable characters.
    /// </summary>
    Text = 1,

    /// <summary>
    /// A single C0/C1 control character.
    /// </summary>
    Control = 2,

    /// <summary>
    /// An ESC or CSI escape sequence.
    /// </summary>
    Sequence = 3,

    /// <summary>
    /// An OSC or DCS string.
    /// </summary>
    StringCommand = 4,

    /// <summary>
    /// A telnet protocol command (IAC ...).
    /// </summary>
    Telnet = 5,

    /// <summary>
    /// A session marker such as connect/disconnect.
    /// </summary>
    Marker = 6
}

/// <summary>
/// One decoded item in the protocol trace: either a raw network block, or a single
/// sequence / control / text run extracted from the wire.
/// </summary>
public sealed class ProtocolTraceEntry
{
    /// <summary>
    /// Unique, strictly increasing id assigned when the entry enters the trace.
    /// NEVER reused and NEVER reset — not even by Clear — so an MCP client can poll
    /// incrementally with "give me entries with Id >= N" across the whole app run.
    /// </summary>
    public long Id { get; set; }

    /// <summary>
    /// Wall-clock time the item was traced.
    /// </summary>
    public DateTime Timestamp { get; set; }

    /// <summary>
    /// Whether the item was received or sent.
    /// </summary>
    public TraceDirection Direction { get; set; }

    /// <summary>
    /// What kind of item this is.
    /// </summary>
    public TraceKind Kind { get; set; }

    /// <summary>
    /// The exact bytes of this item as they appeared on the wire.
    /// </summary>
    public byte[] Bytes { get; set; } = Array.Empty<byte>();

    /// <summary>
    /// Short mnemonic, e.g. "CUP". Empty for raw blocks and text runs.
    /// </summary>
    public string Mnemonic { get; set; } = string.Empty;

    /// <summary>
    /// Full name, e.g. "Cursor Position".
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Decoded arguments, e.g. "row=7 col=3".
    /// </summary>
    public string Arguments { get; set; } = string.Empty;

    /// <summary>
    /// Printable wire form, e.g. <c>ESC[7;3H</c>, or a quoted run for text.
    /// </summary>
    public string Rendered { get; set; } = string.Empty;

    /// <summary>
    /// False when the decoder did not recognise the sequence.
    /// </summary>
    public bool IsKnown { get; set; } = true;

    /// <summary>
    /// Arrow marker used in the log line.
    /// </summary>
    public string DirectionTag => Direction == TraceDirection.Rx ? "RX" : "TX";

    /// <summary>
    /// The bytes rendered as space separated uppercase hex, truncated for long runs.
    /// </summary>
    /// <param name="maxBytes">
    /// Maximum bytes to render before truncating.
    /// </param>
    /// <returns>
    /// The hex string.
    /// </returns>
    public string ToHex(int maxBytes = 24)
    {
        if (Bytes.Length == 0)
            return string.Empty;

        var sb = new StringBuilder(maxBytes * 3 + 16);
        LogEntry.AppendHex(sb, Bytes, 0, Bytes.Length, maxBytes);
        return sb.ToString();
    }

    /// <summary>
    /// Renders the entry as one aligned protocol-monitor line.
    /// </summary>
    /// <param name="includeHex">
    /// When true, includes the raw hex column.
    /// </param>
    /// <returns>
    /// The formatted line.
    /// </returns>
    public string Format(bool includeHex)
    {
        var sb = new StringBuilder(160);

        sb.Append(Timestamp.ToString("HH:mm:ss.fff"));
        sb.Append("  ");
        sb.Append(DirectionTag);
        sb.Append("  ");

        if (includeHex)
        {
            var hex = ToHex();
            sb.Append(hex.Length >= 30 ? hex : hex.PadRight(30));
            sb.Append("  ");
        }

        sb.Append(Rendered.Length >= 18 ? Rendered : Rendered.PadRight(18));
        sb.Append("  ");
        sb.Append(Mnemonic.Length >= 10 ? Mnemonic : Mnemonic.PadRight(10));

        sb.Append(Name);
        if (Arguments.Length != 0)
        {
            if (Name.Length != 0)
                sb.Append(" - ");
            sb.Append(Arguments);
        }

        return sb.ToString();
    }

    /// <inheritdoc/>
    public override string ToString() => Format(includeHex: true);
}
