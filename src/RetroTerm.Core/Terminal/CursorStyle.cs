//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs/RetroTerm
// RetroTerm — Connecting today's machines to yesterday's systems
//

namespace RetroTerm.Core.Terminal;

/// <summary>
/// Defines the visual style of the cursor
/// </summary>
public enum CursorStyle : byte
{
    /// <summary>
    /// Block cursor (default)
    /// </summary>
    Block,

    /// <summary>
    /// Underline cursor
    /// </summary>
    Underline,

    /// <summary>
    /// Vertical bar/beam cursor
    /// </summary>
    Bar,

    /// <summary>
    /// Blinking block cursor
    /// </summary>
    BlinkingBlock,

    /// <summary>
    /// Blinking underline cursor
    /// </summary>
    BlinkingUnderline,

    /// <summary>
    /// Blinking vertical bar cursor
    /// </summary>
    BlinkingBar
}

