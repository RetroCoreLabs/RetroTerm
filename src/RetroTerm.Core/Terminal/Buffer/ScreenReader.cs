using System;
using System.Text;

namespace RetroTerm.Core.Terminal.Buffer;

/// <summary>
/// Renders <see cref="TerminalBuffer"/> content to plain text — the rendered screen
/// as a human (or an LLM over MCP) sees it, after all escape sequences, cursor moves
/// and repaints have been applied by the emulator.
///
/// THREADING: the buffer has no locks. All methods here must be called on the session
/// pump thread (via TerminalSession.RunOnSessionThreadAsync / ReadScreenAsync), which
/// is the single writer of the buffer — reads there are race-free by construction.
///
/// Allocation note: these methods build strings, which allocates. That is fine — screen
/// reads happen per user/script/MCP request, not per received byte. The byte hot path
/// never comes through here.
/// </summary>
public static class ScreenReader
{
    /// <summary>
    /// Renders one buffer row to text. Codepoint 0 (never written) renders as a space,
    /// like the display does. Trailing spaces are trimmed when stripTrailing is true.
    /// </summary>
    public static string GetRowText(TerminalBuffer buffer, int row, bool stripTrailing = true)
    {
        if (buffer == null) throw new ArgumentNullException(nameof(buffer));
        if (row < 0 || row >= buffer.Height) throw new ArgumentOutOfRangeException(nameof(row));

        var sb = new StringBuilder(buffer.Width);
        AppendRow(sb, buffer, row, stripTrailing);
        return sb.ToString();
    }

    /// <summary>
    /// Renders the visible screen as text: rows joined with '\n'.
    /// stripTrailingBlanks trims trailing spaces on every row AND drops trailing
    /// all-blank rows — the useful form for prompt matching and LLM consumption.
    ///
    /// joinWrappedLines puts a long line that the terminal wrapped back together instead of
    /// breaking it at the screen width. It is OFF by default because the screen-shaped form is
    /// what existing callers (MCP reads, prompt matching, the tests) expect; turn it on when the
    /// text is meant to be read as content rather than as a picture of the screen.
    /// </summary>
    public static string GetScreenText(TerminalBuffer buffer, bool stripTrailingBlanks = true, bool joinWrappedLines = false)
    {
        if (buffer == null) throw new ArgumentNullException(nameof(buffer));

        int lastRow = buffer.Height - 1;
        if (stripTrailingBlanks)
        {
            lastRow = FindLastNonBlankRow(buffer);
            if (lastRow < 0)
            {
                return string.Empty; // completely blank screen
            }
        }

        var sb = new StringBuilder((buffer.Width + 1) * (lastRow + 1));
        for (int row = 0; row <= lastRow; row++)
        {
            // A row only gets a newline before it if the row above did not wrap into it.
            if (row > 0 && !(joinWrappedLines && buffer.IsLineWrapped(row - 1)))
            {
                sb.Append('\n');
            }

            // A wrapped row must keep its trailing spaces, or joining would lose the gap between
            // the last word on this row and the first on the next.
            bool strip = stripTrailingBlanks && !(joinWrappedLines && buffer.IsLineWrapped(row));
            AppendRow(sb, buffer, row, strip);
        }
        return sb.ToString();
    }

    /// <summary>
    /// The "screen tail": the last maxRows non-blank-terminated rows of the screen,
    /// joined with '\n'. This is what prompt matching wants — "does the screen
    /// currently END with X-C:" — without being fooled by a prompt echoed earlier
    /// in a listing. maxRows = 1 gives just the last non-blank row.
    /// </summary>
    public static string GetTailText(TerminalBuffer buffer, int maxRows = 1)
    {
        if (buffer == null) throw new ArgumentNullException(nameof(buffer));
        if (maxRows < 1) throw new ArgumentOutOfRangeException(nameof(maxRows));

        int lastRow = FindLastNonBlankRow(buffer);
        if (lastRow < 0)
        {
            return string.Empty;
        }

        int firstRow = lastRow - maxRows + 1;
        if (firstRow < 0)
        {
            firstRow = 0;
        }

        var sb = new StringBuilder((buffer.Width + 1) * (lastRow - firstRow + 1));
        for (int row = firstRow; row <= lastRow; row++)
        {
            if (row > firstRow)
            {
                sb.Append('\n');
            }
            AppendRow(sb, buffer, row, stripTrailing: true);
        }
        return sb.ToString();
    }

    /// <summary>
    /// Renders one scrollback line to text (index 0 = oldest). Returns null when the
    /// index is out of range — mirrors TerminalBuffer.GetScrollbackLine.
    /// </summary>
    public static string? GetScrollbackLineText(TerminalBuffer buffer, int index, bool stripTrailing = true)
    {
        if (buffer == null) throw new ArgumentNullException(nameof(buffer));

        var line = buffer.GetScrollbackLine(index);
        if (line == null)
        {
            return null;
        }

        int end = line.Length;
        if (stripTrailing)
        {
            while (end > 0 && IsBlank(line[end - 1].Codepoint))
            {
                end--;
            }
        }

        var sb = new StringBuilder(end);
        for (int col = 0; col < end; col++)
        {
            AppendCodepoint(sb, line[col].Codepoint);
        }
        return sb.ToString();
    }

    /// <summary>
    /// Index of the last row containing any non-blank character, or -1 for a blank screen.
    /// </summary>
    public static int FindLastNonBlankRow(TerminalBuffer buffer)
    {
        if (buffer == null) throw new ArgumentNullException(nameof(buffer));

        for (int row = buffer.Height - 1; row >= 0; row--)
        {
            for (int col = 0; col < buffer.Width; col++)
            {
                if (!IsBlank(buffer.GetCell(row, col).Codepoint))
                {
                    return row;
                }
            }
        }
        return -1;
    }

    private static void AppendRow(StringBuilder sb, TerminalBuffer buffer, int row, bool stripTrailing)
    {
        int end = buffer.Width;
        if (stripTrailing)
        {
            while (end > 0 && IsBlank(buffer.GetCell(row, end - 1).Codepoint))
            {
                end--;
            }
        }

        for (int col = 0; col < end; col++)
        {
            AppendCodepoint(sb, buffer.GetCell(row, col).Codepoint);
        }
    }

    private static void AppendCodepoint(StringBuilder sb, uint codepoint)
    {
        if (codepoint == 0)
        {
            sb.Append(' '); // never-written cell renders as blank, same as the display
        }
        else if (codepoint <= 0xFFFF)
        {
            sb.Append((char)codepoint);
        }
        else
        {
            // Astral-plane codepoint: emit the surrogate pair.
            sb.Append(char.ConvertFromUtf32((int)codepoint));
        }
    }

    private static bool IsBlank(uint codepoint) => codepoint == 0 || codepoint == ' ';
}
