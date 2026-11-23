//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs/RetroTerm
// RetroTerm — Connecting today's machines to yesterday's systems
//

using RetroTerm.Core.Configuration;
using RetroTerm.Core.Protocols;

using Xunit;

namespace RetroTerm.Tests.Configuration
{
    /// <summary>
    /// The one terminal rule both connect dialogs use: TN3270 fixes the IBM 3278, and leaving
    /// TN3270 puts back the terminal chosen before it.
    /// </summary>
    public class TerminalChoiceTrackerTests
    {
        [Fact]
        public void Tn3270OffersTheFour3278Models()
        {
            var tracker = new TerminalChoiceTracker();

            tracker.Update(ConnectionFactory.ProtocolType.TN3270, "VT220", out string[]? choices, out string? select);

            Assert.NotNull(choices);
            Assert.Equal(new[] { "IBM-3278-2", "IBM-3278-3", "IBM-3278-4", "IBM-3278-5" }, choices);
            Assert.Equal("IBM-3278-2", select);
        }

        [Fact]
        public void LeavingTn3270PutsBackTheEarlierChoice()
        {
            var tracker = new TerminalChoiceTracker();
            tracker.Update(ConnectionFactory.ProtocolType.TN3270, "TDV2200", out _, out _);

            tracker.Update(ConnectionFactory.ProtocolType.Telnet, "IBM-3278-4", out string[]? choices, out string? select);

            Assert.NotNull(choices);
            Assert.True(choices.Length > 1);
            Assert.Equal("TDV2200", select);
        }

        [Fact]
        public void ChangingBetweenStreamProtocolsLeavesTheBoxAlone()
        {
            var tracker = new TerminalChoiceTracker();

            tracker.Update(ConnectionFactory.ProtocolType.SSH, "VT220", out string[]? choices, out string? select);

            Assert.Null(choices);
            Assert.Null(select);
        }
    }
}
