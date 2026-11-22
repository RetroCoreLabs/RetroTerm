//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs/RetroTerm
// RetroTerm — Connecting today's machines to yesterday's systems
//

namespace RetroTerm.Core.Terminal.Input.Keyboards
{
    /// <summary>
    /// A key on a described on-screen keyboard was pressed, after Shift, Lock and Alt were taken
    /// into account.
    /// </summary>
    /// <param name="cap">
    /// The cap that was clicked.
    /// </param>
    /// <param name="keyName">
    /// The key name to press, without the family, or null.
    /// </param>
    /// <param name="text">
    /// The text to type, or null.
    /// </param>
    public delegate void OnScreenKeyPressed(KeyCap cap, string? keyName, string? text);
}
