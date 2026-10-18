//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs/RetroTerm
// RetroTerm — Connecting today's machines to yesterday's systems
//

namespace RetroTerm.Core.Session;

/// <summary>
/// An immutable snapshot of the rendered screen at one moment, taken on the session
/// pump thread (race-free against the emulator). This is what MCP tools, scripts and
/// the wait-for primitive hand out: once constructed it is plain data — safe to read
/// from any thread, and it stays consistent while the live buffer moves on.
/// </summary>
public sealed class ScreenSnapshot
{
    /// <summary>
    /// Visible screen as plain text, rows joined with '\n'.
    /// </summary>
    public string Text { get; }

    /// <summary>
    /// Cursor row, 0-based.
    /// </summary>
    public int CursorRow { get; }

    /// <summary>
    /// Cursor column, 0-based.
    /// </summary>
    public int CursorColumn { get; }

    /// <summary>
    /// Screen width in columns.
    /// </summary>
    public int Width { get; }

    /// <summary>
    /// Screen height in rows.
    /// </summary>
    public int Height { get; }

    public ScreenSnapshot(string text, int cursorRow, int cursorColumn, int width, int height)
    {
        Text = text;
        CursorRow = cursorRow;
        CursorColumn = cursorColumn;
        Width = width;
        Height = height;
    }
}
