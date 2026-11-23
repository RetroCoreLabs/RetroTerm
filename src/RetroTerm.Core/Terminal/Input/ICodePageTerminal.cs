//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs/RetroTerm
// RetroTerm — Connecting today's machines to yesterday's systems
//

using System.Collections.Generic;

namespace RetroTerm.Core.Terminal.Input
{
    /// <summary>
    /// A terminal whose characters travel as bytes of a chosen code page: the EBCDIC page of an
    /// IBM 3270, which decides both what a byte from the host shows as and what byte a typed
    /// character becomes.
    /// </summary>
    /// <remarks>
    /// Called on the session's pump thread (SessionPump.cs, the thread rule).
    /// </remarks>
    public interface ICodePageTerminal
    {
        /// <summary>
        /// The code page in use, by its IBM number (37 for the USA and Canada, 277 for Denmark
        /// and Norway).
        /// </summary>
        int CodePage { get; }

        /// <summary>
        /// Every code page that can be chosen, as "number name", for example "277 Denmark, Norway".
        /// </summary>
        IReadOnlyList<string> CodePages { get; }

        /// <summary>
        /// Changes the code page and draws the screen again with it.
        /// </summary>
        /// <param name="number">
        /// The IBM code page number.
        /// </param>
        /// <returns>
        /// Null when it was changed, otherwise why not.
        /// </returns>
        string? SetCodePage(int number);
    }
}
