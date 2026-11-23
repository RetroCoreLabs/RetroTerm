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
    }
}
