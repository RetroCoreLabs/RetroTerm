//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs/RetroTerm
// RetroTerm — Connecting today's machines to yesterday's systems
//

using Xunit;

namespace RetroTerm.Tests.Hercules
{
    /// <summary>
    /// The live TK5 tests log on as one user, and TSO refuses a second logon of a user already on
    /// ("USERID IN USE"), so they run one at a time and never beside each other.
    /// </summary>
    [CollectionDefinition("Hercules", DisableParallelization = true)]
    public class HerculesCollection
    {
    }
}
