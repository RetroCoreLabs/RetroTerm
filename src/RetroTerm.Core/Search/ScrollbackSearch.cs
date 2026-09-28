using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using RetroTerm.Core.Terminal.Buffer;

namespace RetroTerm.Core.Search;

/// <summary>
/// Search options for scrollback search
/// </summary>
public class SearchOptions
{
    public bool CaseSensitive { get; set; }
    public bool WholeWord { get; set; }
    public bool UseRegex { get; set; }
    public bool SearchUp { get; set; }
}

/// <summary>
/// Represents a search match in the buffer
/// </summary>
public class SearchMatch
{
    public int Row { get; set; }
    public int StartCol { get; set; }
    public int EndCol { get; set; }
    public int Length => EndCol - StartCol + 1;
}

/// <summary>
/// Manages search functionality in terminal scrollback
/// </summary>
public class ScrollbackSearch
{
    private readonly TerminalBuffer _buffer;

    public ScrollbackSearch(TerminalBuffer buffer)
    {
        _buffer = buffer ?? throw new ArgumentNullException(nameof(buffer));
    }

    /// <summary>
    /// Searches for text in the buffer
    /// </summary>
    public List<SearchMatch> Search(string pattern, SearchOptions options)
    {
        if (string.IsNullOrEmpty(pattern))
            return new List<SearchMatch>();

        var matches = new List<SearchMatch>();

        if (options.UseRegex)
        {
            matches = SearchRegex(pattern, options);
        }
        else
        {
            matches = SearchPlainText(pattern, options);
        }

        return matches;
    }

    /// <summary>
    /// Finds the next match after the specified position
    /// </summary>
    public SearchMatch? FindNext(List<SearchMatch> matches, int currentRow, int currentCol)
    {
        if (matches.Count == 0)
            return null;

        // First match after the current position. The list is already in reading order - history
        // oldest-first, then the screen - so the first one that is past the position is the answer.
        for (int i = 0; i < matches.Count; i++)
        {
            var match = matches[i];
            if (match.Row > currentRow || (match.Row == currentRow && match.StartCol > currentCol))
                return match;
        }

        // Past the last one, so wrap round to the oldest.
        return matches[0];
    }

    /// <summary>
    /// Finds the previous match before the specified position
    /// </summary>
    public SearchMatch? FindPrevious(List<SearchMatch> matches, int currentRow, int currentCol)
    {
        if (matches.Count == 0)
            return null;

        // Last match before the current position, so walk backwards and take the first hit.
        for (int i = matches.Count - 1; i >= 0; i--)
        {
            var match = matches[i];
            if (match.Row < currentRow || (match.Row == currentRow && match.StartCol < currentCol))
                return match;
        }

        // Before the first one, so wrap round to the newest.
        return matches[matches.Count - 1];
    }

    private List<SearchMatch> SearchPlainText(string pattern, SearchOptions options)
    {
        var matches = new List<SearchMatch>();
        var comparison = options.CaseSensitive
            ? StringComparison.Ordinal
            : StringComparison.OrdinalIgnoreCase;

        // Search scrollback first
        for (int scrollRow = 0; scrollRow < _buffer.ScrollbackLineCount; scrollRow++)
        {
            var line = GetScrollbackLine(scrollRow);
            SearchLine(line, pattern, comparison, options, scrollRow - _buffer.ScrollbackLineCount, matches);
        }

        // Search current screen
        for (int row = 0; row < _buffer.Height; row++)
        {
            var line = GetScreenLine(row);
            SearchLine(line, pattern, comparison, options, row, matches);
        }

        return matches;
    }

    private List<SearchMatch> SearchRegex(string pattern, SearchOptions options)
    {
        var matches = new List<SearchMatch>();
        var regexOptions = options.CaseSensitive ? RegexOptions.None : RegexOptions.IgnoreCase;

        if (options.WholeWord)
        {
            // Group the pattern rather than escaping it. Escaping turned a regex into a search for
            // its own punctuation - "sec.nd" stopped matching "second" and started looking for a
            // literal full stop - which is the opposite of what asking for a regex means. The group
            // is needed so that \b binds to the whole pattern and not just its first alternative.
            pattern = @"\b(?:" + pattern + @")\b";
        }

        Regex regex;
        try
        {
            regex = new Regex(pattern, regexOptions);
        }
        catch (ArgumentException)
        {
            // Invalid regex pattern
            return matches;
        }

        // Search scrollback
        for (int scrollRow = 0; scrollRow < _buffer.ScrollbackLineCount; scrollRow++)
        {
            var line = GetScrollbackLine(scrollRow);
            SearchLineRegex(line, regex, options, scrollRow - _buffer.ScrollbackLineCount, matches);
        }

        // Search current screen
        for (int row = 0; row < _buffer.Height; row++)
        {
            var line = GetScreenLine(row);
            SearchLineRegex(line, regex, options, row, matches);
        }

        return matches;
    }

    private void SearchLine(string line, string pattern, StringComparison comparison,
                           SearchOptions options, int row, List<SearchMatch> matches)
    {
        int startIndex = 0;

        while (true)
        {
            var index = line.IndexOf(pattern, startIndex, comparison);
            if (index == -1)
                break;

            if (!options.WholeWord || IsWordBoundary(line, index, pattern.Length))
            {
                matches.Add(new SearchMatch
                {
                    Row = row,
                    StartCol = index,
                    EndCol = index + pattern.Length - 1
                });
            }

            startIndex = index + 1;
        }
    }

    private void SearchLineRegex(string line, Regex regex, SearchOptions options,
                                 int row, List<SearchMatch> matches)
    {
        var found = regex.Matches(line);
        for (int i = 0; i < found.Count; i++)
        {
            var match = found[i];
            matches.Add(new SearchMatch
            {
                Row = row,
                StartCol = match.Index,
                EndCol = match.Index + match.Length - 1
            });
        }
    }

    private bool IsWordBoundary(string line, int index, int length)
    {
        // Check character before match
        if (index > 0)
        {
            var before = line[index - 1];
            if (char.IsLetterOrDigit(before))
                return false;
        }

        // Check character after match
        if (index + length < line.Length)
        {
            var after = line[index + length];
            if (char.IsLetterOrDigit(after))
                return false;
        }

        return true;
    }

    private string GetScreenLine(int row)
    {
        var line = new System.Text.StringBuilder(_buffer.Width);
        for (int col = 0; col < _buffer.Width; col++)
        {
            if (_buffer.TryGetCell(row, col, out var cell))
            {
                line.Append(cell.GetString());
            }
        }
        return line.ToString();
    }

    /// <summary>
    /// Reads one line of history as text, oldest at index 0.
    ///
    /// A history line is stored at the width it was written at, which need not be the width the
    /// screen has now - so the line is read to ITS OWN length rather than the screen's. Reading
    /// only the current width would hide a match in the part of a long line that no longer fits,
    /// which is precisely the text a user scrolls back to look for.
    /// </summary>
    private string GetScrollbackLine(int scrollRow)
    {
        var cells = _buffer.GetScrollbackLine(scrollRow);
        if (cells == null)
            return string.Empty;

        var line = new System.Text.StringBuilder(cells.Length);
        for (int col = 0; col < cells.Length; col++)
        {
            line.Append(cells[col].GetString());
        }
        return line.ToString();
    }
}

