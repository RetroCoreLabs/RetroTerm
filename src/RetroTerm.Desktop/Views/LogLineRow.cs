using Avalonia.Media;

namespace RetroTerm.Desktop.Views;

/// <summary>
/// One rendered line in the Log Viewer or the Protocol Monitor.
/// </summary>
/// <remarks>
/// A view model rather than a bare string so each line can carry its own colour (severity in
/// the log, direction / unknown-sequence in the trace) while still being copied as plain text.
/// Declared as a top-level type because Avalonia compiled bindings require an
/// <c>x:DataType</c>, which cannot reference a nested class.
/// </remarks>
public sealed class LogLineRow
{
    /// <summary>
    /// The formatted line as it is displayed and copied.
    /// </summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>
    /// Brush used to render the line.
    /// </summary>
    public IBrush Brush { get; set; } = Brushes.Gainsboro;

    /// <summary>
    /// True when this row represents an unrecognised sequence (rendered in gold).
    /// </summary>
    public bool IsUnknown { get; set; }

    /// <inheritdoc/>
    public override string ToString() => Text;
}
