//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs/RetroTerm
// RetroTerm — Connecting today's machines to yesterday's systems
//

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Protocol;
using RetroTerm.Core.Commands;
using RetroTerm.Core.Commands.Builtin;
using RetroTerm.Core.Configuration;
using RetroTerm.Core.Protocols;
using RetroTerm.Core.Scripting;
using RetroTerm.Core.Session;
using RetroTerm.Mcp;
using RetroTerm.Tests.TDV;
using Xunit;

namespace RetroTerm.Tests.Mcp;

/// <summary>
/// BUGS.md B20: terminal_open's <c>protocol</c> said "telnet, ssh or serial", the code also took
/// tn3270, gateway and opcomsimulator, and any other word - a typo included - quietly became
/// telnet. The argument now declares its words (ConnectionFactory.OpenableProtocolNames) and a
/// wrong one is refused by name, by the same rule and message a registry command uses
/// (CommandParameterValidation.TryCheckValue). The script CONNECT and CONNSAVE declare theirs too;
/// CommandSurfaceEquivalenceTests' word-list rows cover those on both surfaces.
/// </summary>
public sealed class TerminalOpenProtocolTests : IDisposable
{
    private readonly InProcessSessionHost _host;
    private readonly RetroTermToolProvider _provider;
    private readonly string _tempScripts;
    private readonly string _tempConfig;
    private readonly List<ConnectionFactory.ConnectionParameters> _openedWith = new();

    public TerminalOpenProtocolTests()
    {
        _host = new InProcessSessionHost(p =>
        {
            _openedWith.Add(p);
            return new InMemoryConnection();
        });

        var registry = new CommandRegistry();
        BuiltinCommands.RegisterAll(registry);

        _tempScripts = Path.Combine(Path.GetTempPath(), "RetroTermOpenProtocolTests-" + Guid.NewGuid().ToString("N"));
        _tempConfig = Path.Combine(Path.GetTempPath(), "RetroTermOpenProtocolTests-" + Guid.NewGuid().ToString("N") + ".json");
        _provider = new RetroTermToolProvider(_host, registry, new ScriptLibrary(_tempScripts), new ConfigurationManager(_tempConfig));
    }

    public void Dispose()
    {
        _host.Dispose();
        if (Directory.Exists(_tempScripts))
        {
            Directory.Delete(_tempScripts, recursive: true);
        }
        if (File.Exists(_tempConfig))
        {
            File.Delete(_tempConfig);
        }
    }

    private static string Text(CallToolResult result)
    {
        Assert.NotEmpty(result.Content);
        return Assert.IsType<TextContentBlock>(result.Content[0]).Text;
    }

    private static CommandParameter ProtocolArgument()
    {
        var declared = RetroTermToolProvider.DeclaredArguments("terminal_open")!;
        for (int i = 0; i < declared.Count; i++)
        {
            if (declared[i].Name == "protocol")
            {
                return declared[i];
            }
        }
        throw new InvalidOperationException("terminal_open declares no protocol argument");
    }

    [Fact]
    public void TheProtocolArgumentDeclaresTheWordsAnOpenCanUse()
    {
        Assert.Equal(ConnectionFactory.OpenableProtocolNames, ProtocolArgument().AllowedValues);
    }

    [Fact]
    public void EveryOpenableWordParsesToItsOwnProtocolAndNoneFallsToTelnetByAccident()
    {
        // Each word must mean what it says: ParseProtocolType maps an unknown word to Telnet, so a
        // word in the list that it did not know would be the very fault B20 is about.
        var words = ConnectionFactory.ProtocolNames;
        var seen = new HashSet<ConnectionFactory.ProtocolType>();
        for (int i = 0; i < words.Count; i++)
        {
            Assert.True(seen.Add(ConnectionFactory.ParseProtocolType(words[i])),
                $"'{words[i]}' parses to a protocol another word already names");
        }
        Assert.Equal(Enum.GetValues<ConnectionFactory.ProtocolType>().Length, seen.Count);
    }

    [Fact]
    public async Task AMisspelledProtocolIsRefusedByNameAndNothingOpens()
    {
        var args = new Dictionary<string, JsonElement>
        {
            ["host"] = JsonSerializer.SerializeToElement("test-nd"),
            ["protocol"] = JsonSerializer.SerializeToElement("telnt")
        };

        var result = await _provider.CallToolAsync("terminal_open", args, CancellationToken.None);

        Assert.True(result.IsError ?? false, "terminal_open accepted protocol=telnt");
        Assert.True(CommandParameterValidation.TryCheckValue("terminal_open", ProtocolArgument(), "telnt", out var expected) == false);
        Assert.Equal(expected, Text(result));
        Assert.Contains("'protocol'", Text(result), StringComparison.Ordinal);
        Assert.Contains("telnt", Text(result), StringComparison.Ordinal);
        Assert.Empty(_openedWith);
        Assert.Empty(_host.ListSessions());
    }

    [Fact]
    public async Task GatewayIsRefusedByNameRatherThanFailingInsideTheFactory()
    {
        var args = new Dictionary<string, JsonElement>
        {
            ["host"] = JsonSerializer.SerializeToElement("test-nd"),
            ["protocol"] = JsonSerializer.SerializeToElement("gateway")
        };

        var result = await _provider.CallToolAsync("terminal_open", args, CancellationToken.None);

        Assert.True(result.IsError ?? false);
        Assert.Contains("'protocol' must be one of", Text(result), StringComparison.Ordinal);
        Assert.Empty(_openedWith);
    }

    [Theory]
    [InlineData("telnet", ConnectionFactory.ProtocolType.Telnet)]
    [InlineData("SSH", ConnectionFactory.ProtocolType.SSH)]
    [InlineData("tn3270", ConnectionFactory.ProtocolType.TN3270)]
    public async Task ANetworkWordOpensThatProtocol(string word, ConnectionFactory.ProtocolType expected)
    {
        var args = new Dictionary<string, JsonElement>
        {
            ["host"] = JsonSerializer.SerializeToElement("test-nd"),
            ["protocol"] = JsonSerializer.SerializeToElement(word)
        };

        var result = await _provider.CallToolAsync("terminal_open", args, CancellationToken.None);

        Assert.NotEqual(true, result.IsError);
        Assert.Single(_openedWith);
        Assert.Equal(expected, _openedWith[0].Protocol);
    }

    [Fact]
    public async Task TheOpcomSimulatorOpensWithNoHost()
    {
        var args = new Dictionary<string, JsonElement>
        {
            ["protocol"] = JsonSerializer.SerializeToElement("opcomsimulator")
        };

        var result = await _provider.CallToolAsync("terminal_open", args, CancellationToken.None);

        Assert.NotEqual(true, result.IsError);
        Assert.Single(_openedWith);
        Assert.Equal(ConnectionFactory.ProtocolType.OpcomSimulator, _openedWith[0].Protocol);
    }

    [Fact]
    public void TheSchemaCarriesTheWordsAsAnEnum()
    {
        var tools = _provider.BuildToolList();
        for (int i = 0; i < tools.Count; i++)
        {
            if (tools[i].Name != "terminal_open") continue;
            var words = tools[i].InputSchema.GetProperty("properties").GetProperty("protocol").GetProperty("enum");
            var allowed = ConnectionFactory.OpenableProtocolNames;
            Assert.Equal(allowed.Count, words.GetArrayLength());
            for (int w = 0; w < allowed.Count; w++)
            {
                Assert.Equal(allowed[w], words[w].GetString());
            }
            return;
        }
        Assert.Fail("terminal_open is not in tools/list");
    }
}
