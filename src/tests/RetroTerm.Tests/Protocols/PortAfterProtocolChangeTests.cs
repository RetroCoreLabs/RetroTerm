//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs/RetroTerm
// RetroTerm — Connecting today's machines to yesterday's systems
//

using RetroTerm.Core.Protocols;

using Xunit;

namespace RetroTerm.Tests.Protocols
{
    /// <summary>
    /// The port a connection form shows after the protocol changes: the new default while the
    /// box still holds a default, the person's own port otherwise.
    /// </summary>
    public class PortAfterProtocolChangeTests
    {
        [Theory]
        [InlineData(ConnectionFactory.ProtocolType.TN3270, "23", "3270")]
        [InlineData(ConnectionFactory.ProtocolType.TN3270, "", "3270")]
        [InlineData(ConnectionFactory.ProtocolType.Telnet, "3270", "23")]
        [InlineData(ConnectionFactory.ProtocolType.SSH, "3270", "22")]
        [InlineData(ConnectionFactory.ProtocolType.TN3270, "22", "3270")]
        [InlineData(ConnectionFactory.ProtocolType.TN3270, "9010", "9010")]
        [InlineData(ConnectionFactory.ProtocolType.SSH, "2323", "2323")]
        public void ThePortFollowsTheProtocolOnlyWhileItIsADefault(ConnectionFactory.ProtocolType protocol, string current, string expected)
        {
            Assert.Equal(expected, ConnectionFactory.PortAfterProtocolChange(protocol, current));
        }

        [Fact]
        public void Tn3270DefaultsToPort3270()
        {
            Assert.Equal(3270, ConnectionFactory.DefaultPortFor(ConnectionFactory.ProtocolType.TN3270));
        }
    }
}
