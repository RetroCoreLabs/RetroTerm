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
    /// TN3270 carries 3270 records, so its only terminals are the IBM 3278 models 2 to 5
    /// (<see cref="ConnectionFactory.Ibm3278Models"/>): the form offers those, model 2 first. Every
    /// other protocol offers every stream terminal. Leaving TN3270 puts back the terminal that was
    /// chosen before it, so a quick look at TN3270 does not lose the person's choice.
    /// </remarks>
    public sealed class TerminalChoiceTracker
    {
        // True while the box offers the 3278 models (the protocol is TN3270).
        private bool _on3270;

        // The stream terminal chosen before TN3270, to put back when the protocol changes again.
        private string? _before3270;

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
        public void Update(ConnectionFactory.ProtocolType protocol, string? currentChoice, out string[]? choices, out string? select)
        {
            bool on3270 = protocol == ConnectionFactory.ProtocolType.TN3270;
            choices = null;
            select = null;

            if (on3270 && !_on3270)
            {
                _before3270 = currentChoice;
                choices = ConnectionFactory.Ibm3278Models;
                select = ConnectionFactory.EmulatorTypeFor(protocol, currentChoice);
            }
            else if (!on3270 && _on3270)
            {
                choices = EmulatorFactory.AvailableEmulators;
                select = _before3270 ?? ConnectionFactory.EmulatorTypeFor(protocol, null);
            }

            _on3270 = on3270;
        }
    }
}
