//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs/RetroTerm
// RetroTerm — Connecting today's machines to yesterday's systems
//

using RetroTerm.Core.Protocols;

namespace RetroTerm.Core.Configuration
{
    /// <summary>
    /// The terminal list a connection form offers for a protocol, and the choice to go back to.
    /// ONE rule for Quick Connect and Manage Connections (Ronny, 22 November 2026: no duplicated
    /// code, as little as possible in the UI).
    /// </summary>
    /// <remarks>
    /// TN3270 carries 3270 records, so its only terminal is the IBM 3278 model 2
    /// (<see cref="ConnectionFactory.EmulatorTypeFor"/>): the form shows that one, fixed. Every other
    /// protocol offers every stream terminal. Leaving TN3270 puts back the terminal that was chosen
    /// before it, so a quick look at TN3270 does not lose the person's choice.
    /// </remarks>
    public sealed class TerminalChoiceTracker
    {
        private bool _fixed;
        private string? _beforeFixed;

        /// <summary>
        /// Works out what the terminal box shows after the protocol changed.
        /// </summary>
        /// <param name="protocol">
        /// The protocol now chosen.
        /// </param>
        /// <param name="currentChoice">
        /// The terminal the box shows now.
        /// </param>
        /// <param name="choices">
        /// The terminals to offer, or null when the list stays as it is.
        /// </param>
        /// <param name="select">
        /// The terminal to select, or null to leave the selection alone.
        /// </param>
        /// <returns>
        /// True when the choice is fixed (the box is shown but cannot be changed).
        /// </returns>
        public bool Update(ConnectionFactory.ProtocolType protocol, string? currentChoice, out string[]? choices, out string? select)
        {
            bool isFixed = protocol == ConnectionFactory.ProtocolType.TN3270;
            choices = null;
            select = null;

            if (isFixed && !_fixed)
            {
                _beforeFixed = currentChoice;
                string only = ConnectionFactory.EmulatorTypeFor(protocol, null);
                choices = new[] { only };
                select = only;
            }
            else if (!isFixed && _fixed)
            {
                choices = EmulatorFactory.AvailableEmulators;
                select = _beforeFixed ?? ConnectionFactory.EmulatorTypeFor(protocol, null);
            }

            _fixed = isFixed;
            return isFixed;
        }
    }
}
