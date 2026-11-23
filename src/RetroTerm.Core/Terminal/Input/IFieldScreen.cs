//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs/RetroTerm
// RetroTerm — Connecting today's machines to yesterday's systems
//

namespace RetroTerm.Core.Terminal.Input
{
    /// <summary>
    /// A screen made of fields (the IBM 3270), which the FIELDS command lists for scripts and MCP.
    /// </summary>
    /// <remarks>
    /// Read on the session's pump thread (SessionPump.cs, the thread rule).
    /// </remarks>
    public interface IFieldScreen
    {
        /// <summary>
        /// Describes every field on the screen, one line each, in buffer order.
        /// </summary>
        /// <returns>
        /// The lines, or one line saying the screen has no fields.
        /// </returns>
        string DescribeFields();

        /// <summary>
        /// Replaces the text of one input field, as an operator would: the cursor goes to the
        /// field's first position, Erase EOF empties it, and the text is typed.
        /// </summary>
        /// <param name="number">
        /// The field's number in <see cref="DescribeFields"/>, from 1.
        /// </param>
        /// <param name="text">
        /// The text to type.
        /// </param>
        /// <returns>
        /// Null when done, or why not (no such field, a protected field, the keyboard locked, text
        /// longer than the field).
        /// </returns>
        string? FillField(int number, string text);

        /// <summary>
        /// Moves the cursor, as clicking the cell would.
        /// </summary>
        /// <param name="row">
        /// The row, from 1.
        /// </param>
        /// <param name="column">
        /// The column, from 1.
        /// </param>
        /// <returns>
        /// Null when moved, or why not.
        /// </returns>
        string? MoveCursorTo(int row, int column);
    }
}
