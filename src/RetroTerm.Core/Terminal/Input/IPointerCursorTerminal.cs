//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs/RetroTerm
// RetroTerm — Connecting today's machines to yesterday's systems
//

namespace RetroTerm.Core.Terminal.Input
{
    /// <summary>
    /// A terminal whose cursor the mouse can place: a click moves the cursor to the cell, a double
    /// click moves it and presses Enter.
    /// </summary>
    /// <remarks>
    /// The IBM 3270, as IBM's tnz does (its README, feature list). The canvas calls this for a
    /// left click that did not drag; a drag still selects text. The call is made on the UI thread
    /// and the terminal moves its work to the session thread itself (ILocalKeyboard).
    /// </remarks>
    public interface IPointerCursorTerminal
    {
        /// <summary>
        /// The person clicked a cell.
        /// </summary>
        /// <param name="row">
        /// The row, counted from 0 at the top of the screen.
        /// </param>
        /// <param name="column">
        /// The column, counted from 0.
        /// </param>
        /// <param name="doubleClick">
        /// True for the second click of a double click.
        /// </param>
        /// <returns>
        /// True when the terminal took the click; false when the cell is not one it places the
        /// cursor on (the status line, outside the screen).
        /// </returns>
        bool PointerClick(int row, int column, bool doubleClick);

        /// <summary>
        /// Gets or sets whether a double click presses Enter after placing the cursor. When false,
        /// PointerClick refuses the second click of a double click, so the canvas keeps the word
        /// selection it made (a setting per connection, HostConfiguration.DoubleClickPressesEnter).
        /// </summary>
        bool DoubleClickPressesEnter { get; set; }
    }
}
