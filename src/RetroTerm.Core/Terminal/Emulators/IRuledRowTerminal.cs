//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs/RetroTerm
// RetroTerm — Connecting today's machines to yesterday's systems
//

namespace RetroTerm.Core.Terminal.Emulators
{
    /// <summary>
    /// A terminal that has a thin rule drawn across the screen above one of its rows.
    /// </summary>
    /// <remarks>
    /// The IBM 3270's line 25 (the status line) is set off from the host's screen by a line.
    /// wc3270 draws a blank row underlined across the screen (wc3270\screen.c, draw_oia,
    /// status_skip); Ronny's decision of 22 November 2026 was a thin rule inside the picture
    /// instead, so no row is spent on it. The renderer draws it; the buffer holds no character
    /// for it.
    /// </remarks>
    public interface IRuledRowTerminal
    {
        /// <summary>
        /// Gets the row, counted from 0 at the top of the live screen, whose top edge gets the
        /// rule, or -1 for none.
        /// </summary>
        int RuleAboveRow { get; }
    }
}
