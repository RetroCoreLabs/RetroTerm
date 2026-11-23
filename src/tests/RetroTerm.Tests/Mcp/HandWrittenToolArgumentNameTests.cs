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
/// BUGS.md B14: the MCP tools that are not registry commands (terminal_open, terminal_close,
/// terminal_list, terminal_build, terminal_run_script, terminal_scripts) read their arguments
/// by name, so a misspelled one was read as absent and the tool went on with its default.
/// They now declare their arguments once; the declaration is both their schema and the list
/// the shared name check (CommandParameterValidation.TryCheckName) refuses against.
///
/// Every test is generated over <see cref="RetroTermToolProvider.HandWrittenToolNames"/>, so
/// a new hand-written tool is covered the moment it is declared.
/// </summary>
public sealed class HandWrittenToolArgumentNameTests : IDisposable
{
    private const string UnknownName = "hots";

    private readonly InProcessSessionHost _host;
    private readonly RetroTermToolProvider _provider;
    private readonly ScriptLibrary _library;
    private readonly string _tempScripts;
    private readonly string _tempConfig;
    private readonly List<ConnectionFactory.ConnectionParameters> _openedWith = new();

    public HandWrittenToolArgumentNameTests()
    {
        _host = new InProcessSessionHost(p =>
        {
            _openedWith.Add(p);
            return new InMemoryConnection();
        });

        var registry = new CommandRegistry();
        BuiltinCommands.RegisterAll(registry);

        _tempScripts = Path.Combine(Path.GetTempPath(), "RetroTermHandWrittenTests-" + Guid.NewGuid().ToString("N"));
        _tempConfig = Path.Combine(Path.GetTempPath(), "RetroTermHandWrittenTests-" + Guid.NewGuid().ToString("N") + ".json");
        _library = new ScriptLibrary(_tempScripts);
        _provider = new RetroTermToolProvider(_host, registry, _library, new ConfigurationManager(_tempConfig));
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

    public static IEnumerable<object[]> AllHandWrittenTools()
    {
        var names = RetroTermToolProvider.HandWrittenToolNames();
        for (int i = 0; i < names.Count; i++)
        {
            yield return new object[] { names[i] };
        }
    }

    private static string Text(CallToolResult result)
    {
        Assert.NotEmpty(result.Content);
        return Assert.IsType<TextContentBlock>(result.Content[0]).Text;
    }

    private async Task<Guid> OpenAsync()
    {
        var args = new Dictionary<string, JsonElement>
        {
            ["host"] = JsonSerializer.SerializeToElement("test-nd")
        };
        var result = await _provider.CallToolAsync("terminal_open", args, CancellationToken.None);
        Assert.NotEqual(true, result.IsError);
        var sessions = _host.ListSessions();
        return sessions[sessions.Count - 1].Id;
    }

    /// <summary>
    /// Every value a tool's arguments could need to get past "missing sessionId": a real one
    /// for the tools that take it, so the refusal tested is the name check and nothing else.
    /// </summary>
    private async Task<Dictionary<string, JsonElement>> RequiredArgsAsync(string toolName)
    {
        var args = new Dictionary<string, JsonElement>();
        var declared = RetroTermToolProvider.DeclaredArguments(toolName)!;
        for (int i = 0; i < declared.Count; i++)
        {
            if (declared[i].Required && declared[i].Name == RetroTermToolProvider.SessionIdArgument)
            {
                var id = await OpenAsync();
                args[RetroTermToolProvider.SessionIdArgument] = JsonSerializer.SerializeToElement(id.ToString());
            }
        }
        return args;
    }

    [Fact]
    public void TheHandWrittenToolsAreTheOnesWithTheirOwnHandlers()
    {
        // The list the tests are generated over must be the real set, or a tool can slip out of it.
        var names = RetroTermToolProvider.HandWrittenToolNames();
        Assert.Equal(
            new[] { "terminal_open", "terminal_close", "terminal_list", "terminal_build", "terminal_run_script", "terminal_scripts" },
            names);
    }

    [Theory]
    [MemberData(nameof(AllHandWrittenTools))]
    public async Task AnUnknownArgumentNameIsRefusedAndTheToolDoesNothing(string toolName)
    {
        var declared = RetroTermToolProvider.DeclaredArguments(toolName)!;
        Assert.False(CommandParameterValidation.TryCheckName(toolName, declared, UnknownName, "tools/list", out var expected),
            $"{toolName} really has an argument named '{UnknownName}' - pick another name for this test");
        Assert.Contains("'" + UnknownName + "'", expected!, StringComparison.Ordinal);
        for (int i = 0; i < declared.Count; i++)
        {
            Assert.Contains(declared[i].Name, expected!, StringComparison.Ordinal);
        }

        var args = await RequiredArgsAsync(toolName);
        // A save that would happen if the tool ran, and a host that would open if it ran.
        args["save"] = JsonSerializer.SerializeToElement("b14");
        args["script"] = JsonSerializer.SerializeToElement("ESC");
        args["host"] = JsonSerializer.SerializeToElement("test-nd");
        // Strip whatever this tool really takes from the extras, so the only unknown is ours.
        var extras = new[] { "save", "script", "host" };
        for (int i = 0; i < extras.Length; i++)
        {
            if (CommandParameterValidation.TryCheckName(toolName, declared, extras[i], "tools/list", out _) == false)
            {
                args.Remove(extras[i]);
            }
        }
        args[UnknownName] = JsonSerializer.SerializeToElement("test-nd");

        int opensBefore = _openedWith.Count;
        int sessionsBefore = _host.ListSessions().Count;

        var result = await _provider.CallToolAsync(toolName, args, CancellationToken.None);

        Assert.True(result.IsError ?? false, $"{toolName} accepted the undeclared argument '{UnknownName}' without a word");
        Assert.Equal(expected, Text(result));
        Assert.Equal(opensBefore, _openedWith.Count);
        Assert.Equal(sessionsBefore, _host.ListSessions().Count);
        Assert.Empty(_library.List());
    }

    [Theory]
    [MemberData(nameof(AllHandWrittenTools))]
    public async Task EveryArgumentTheSchemaListsIsAccepted(string toolName)
    {
        var schema = FindTool(toolName).InputSchema;
        var properties = schema.GetProperty("properties").EnumerateObject();
        while (properties.MoveNext())
        {
            var name = properties.Current.Name;
            var args = await RequiredArgsAsync(toolName);
            if (!args.ContainsKey(name))
            {
                args[name] = properties.Current.Value.GetProperty("type").GetString() == "integer"
                    ? JsonSerializer.SerializeToElement(1)
                    : JsonSerializer.SerializeToElement("x");
            }

            var result = await _provider.CallToolAsync(toolName, args, CancellationToken.None);

            // The call may fail for its own reasons (no host, no such script); it must not be
            // refused for the NAME the schema itself advertised.
            Assert.DoesNotContain("has no parameter", Text(result), StringComparison.Ordinal);
        }
    }

    [Theory]
    [MemberData(nameof(AllHandWrittenTools))]
    public void TheSchemaListsExactlyTheDeclaredArguments(string toolName)
    {
        var declared = RetroTermToolProvider.DeclaredArguments(toolName)!;
        var schema = FindTool(toolName).InputSchema;

        var listed = new List<string>();
        var properties = schema.GetProperty("properties").EnumerateObject();
        while (properties.MoveNext())
        {
            listed.Add(properties.Current.Name);
        }

        var expected = new string[declared.Count];
        for (int i = 0; i < declared.Count; i++)
        {
            expected[i] = declared[i].Name;
        }
        Assert.Equal(expected, listed);
    }

    private Tool FindTool(string toolName)
    {
        var tools = _provider.BuildToolList();
        for (int i = 0; i < tools.Count; i++)
        {
            if (tools[i].Name == toolName)
            {
                return tools[i];
            }
        }
        throw new InvalidOperationException($"{toolName} is not in tools/list");
    }
}
