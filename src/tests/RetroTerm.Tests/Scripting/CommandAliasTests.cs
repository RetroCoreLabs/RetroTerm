//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs/RetroTerm
// RetroTerm — Connecting today's machines to yesterday's systems
//

using System;

using RetroTerm.Core.Commands;
using RetroTerm.Core.Commands.Builtin;
using RetroTerm.Core.Scripting;

using Xunit;

namespace RetroTerm.Tests.Scripting;

/// <summary>
/// IND$PUT and IND$GET, IBM's names for HOSTSEND and HOSTRECEIVE: a script may use them, and they
/// are the same commands, not copies.
/// </summary>
public class CommandAliasTests
{
    private static CommandRegistry Registry()
    {
        var registry = new CommandRegistry();
        BuiltinCommands.RegisterAll(registry);
        return registry;
    }

    [Theory]
    [InlineData("IND$PUT", "HOSTSEND")]
    [InlineData("ind$get", "HOSTRECEIVE")]
    public void AScriptLineWithTheIbmNameBecomesTheCommand(string alias, string command)
    {
        var script = new ScriptParser(Registry()).Parse(alias + " \"C:\\\\temp\\\\x.txt\" RTTFT.TEXT mode=binary");

        Assert.True(script.IsValid, script.Errors.Count > 0 ? script.Errors[0].Message : string.Empty);
        Assert.Equal(command, script.Steps[0].CommandName);
        Assert.Equal("RTTFT.TEXT", script.Steps[0].Args.GetString("dataset"));
        Assert.Equal("binary", script.Steps[0].Args.GetString("mode"));
    }

    [Fact]
    public void TheAliasIsTheSameObjectAndIsNotListedTwice()
    {
        CommandRegistry registry = Registry();

        Assert.True(registry.TryGet("IND$PUT", out ISessionCommand alias));
        Assert.True(registry.TryGet("HOSTSEND", out ISessionCommand command));
        Assert.Same(command, alias);
        for (int i = 0; i < registry.Commands.Count; i++)
        {
            Assert.DoesNotContain("$", registry.Commands[i].Name, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void AnAliasForAnUnknownCommandOrATakenNameIsRefused()
    {
        CommandRegistry registry = Registry();

        Assert.Throws<ArgumentException>(() => registry.RegisterAlias("X", "NOSUCHCOMMAND"));
        Assert.Throws<ArgumentException>(() => registry.RegisterAlias("HOSTRECEIVE", "HOSTSEND"));
    }
}
