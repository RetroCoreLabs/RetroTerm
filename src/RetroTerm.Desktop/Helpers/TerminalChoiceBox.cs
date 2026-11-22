//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs/RetroTerm
// RetroTerm — Connecting today's machines to yesterday's systems
//

using Avalonia.Controls;

using RetroTerm.Core.Configuration;
using RetroTerm.Core.Protocols;

namespace RetroTerm.Desktop.Helpers
{
    /// <summary>
    /// Puts the protocol's terminal rule (<see cref="TerminalChoiceTracker"/>) onto a terminal
    /// dropdown. The only UI part of the rule, shared by Quick Connect and Manage Connections.
    /// </summary>
    public static class TerminalChoiceBox
    {
        /// <summary>
        /// Updates the dropdown for a newly chosen protocol.
        /// </summary>
        /// <param name="box">
        /// The terminal dropdown.
        /// </param>
        /// <param name="tracker">
        /// The form's tracker, which remembers the choice made before TN3270.
        /// </param>
        /// <param name="protocol">
        /// The protocol now chosen.
        /// </param>
        public static void Apply(ComboBox box, TerminalChoiceTracker tracker, ConnectionFactory.ProtocolType protocol)
        {
            bool isFixed = tracker.Update(protocol, box.SelectedItem as string, out string[]? choices, out string? select);
            if (choices != null)
            {
                box.ItemsSource = choices;
                box.SelectedItem = select;
            }

            box.IsEnabled = !isFixed;
        }
    }
}
