//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs/RetroTerm
// RetroTerm — Connecting today's machines to yesterday's systems
//

namespace RetroTerm.Core.Terminal.Input
{
    /// <summary>
    /// A block-mode terminal that pastes into its own fields instead of sending the text.
    /// </summary>
    /// <remarks>
    /// The IBM 3270: a pasted line break goes to the input field on the next row, it does not
    /// press Enter, so pasting three lines fills three fields and sends nothing until the person
    /// presses Enter. Typed text and SEND go through ILocalKeyboard, where a carriage return IS
    /// Enter; a paste must not. The canvas paste (the Paste menu and the PASTE command alike)
    /// calls this when the terminal has it.
    /// </remarks>
    public interface IFieldPaste
    {
        /// <summary>
        /// Pastes text into the screen's fields.
        /// </summary>
        /// <param name="text">
        /// The pasted text, line breaks included.
        /// </param>
        void PasteIntoFields(string text);
    }
}
