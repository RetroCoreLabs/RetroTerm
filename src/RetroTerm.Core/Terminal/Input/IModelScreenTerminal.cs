//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs/RetroTerm
// RetroTerm — Connecting today's machines to yesterday's systems
//

namespace RetroTerm.Core.Terminal.Input
{
    /// <summary>
    /// A terminal whose screen is fixed when it is built: an IBM 3278 of a model, with an
    /// alternate screen that is the model's own or a larger one (the Ibm3270Emulator three-argument
    /// constructor). The size cannot be changed afterwards, so CONNECT, which keeps the tab's
    /// terminal, reads it here to tell the host what the tab really is.
    /// </summary>
    public interface IModelScreenTerminal
    {
        /// <summary>
        /// The model: 2, 3, 4 or 5.
        /// </summary>
        int Model { get; }

        /// <summary>
        /// Columns of the alternate screen, the one Erase/Write Alternate switches to.
        /// </summary>
        int AlternateScreenColumns { get; }

        /// <summary>
        /// Rows of the alternate screen, the one Erase/Write Alternate switches to.
        /// </summary>
        int AlternateScreenRows { get; }

        /// <summary>
        /// Whether the alternate screen is larger than the model's own.
        /// </summary>
        bool IsOversize { get; }
    }
}
