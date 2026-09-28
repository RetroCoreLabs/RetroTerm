using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;
using RetroTerm.Core.Commands;
using RetroTerm.Core.Commands.Builtin;
using RetroTerm.Core.Configuration;
using RetroTerm.Core.Scripting;
using RetroTerm.Core.Session;
using RetroTerm.Mcp;
using RetroTerm.Tests.TDV;
using Xunit;

namespace RetroTerm.Tests.Mcp;

/// <summary>
/// Every command the DESKTOP registers must reach the MCP surface, with all of its parameters.
/// </summary>
/// <remarks>
/// <para><b>The question that produced this, 27 August 2026</b></para>
/// Ronny, after a command was added: "havce you update the mcp to also support all new features".
/// The honest answer took a source read, because nothing in the suite could answer it.
///
/// <para><b>What was already covered, and the hole between the two</b></para>
/// <see cref="RetroTermToolProvider.BuildToolList"/> generates one tool per registered command in a loop, so
/// a command that reaches the registry does reach MCP. <see cref="RetroTerm.Tests.Commands.CommandSurfaceEquivalenceTests"/> then
/// checks the two surfaces agree about parameters and escaping, and
/// <see cref="RetroTerm.Tests.Commands.CommandSurfaceEquivalenceTests.EveryCommandToolMapsBackToARegisteredCommand"/> checks no hand-written tool bypasses the
/// registry.
///
/// Every one of those builds its registry with <see cref="BuiltinCommands.RegisterAll"/> ALONE - thirteen
/// commands plus the terminal-control and trace groups. The desktop registers SIX groups
/// (<c>MainWindow.Mcp.cs</c>, <c>GetOrCreateCommandRegistry</c>). So CONNECT, DISCONNECT, CONNLIST,
/// CONNSHOW, CONNSAVE, CONNDEL, SENDFILE, RECEIVEFILE, GATEWAY, SCREENSHOT, LOCALKEY, ZOOM and PASTE
/// were covered by NOTHING - not by the parity harness, not by the tool-list check. The whole
/// cross-surface guarantee stopped at the built-in subset, quietly, because the extra groups need
/// constructor dependencies the other tests did not want to build.
///
/// They are stubs. Nothing here calls them; this test only asks whether the command SURFACED.
///
/// <para><b>Red before green</b></para>
/// Comment out the <see cref="ScreenshotCommand.RegisterAll"/> line in <see cref="BuildTheRegistryTheDesktopBuilds"/>
/// and <see cref="EveryDesktopCommandGroupIsRepresented"/> fails naming SCREENSHOT. Confirmed by doing it.
/// That guard exists because this test builds its own copy of the desktop's registration list, and a
/// copy that silently falls behind the original would assert nothing - the exact failure mode this
/// test was written to close.
/// </remarks>
public class EveryRegisteredCommandReachesMcpTests : IDisposable
{
    private readonly InProcessSessionHost _host;
    private readonly RetroTermToolProvider _provider;
    private readonly CommandRegistry _registry;
    private readonly string _tempRoot;

    public EveryRegisteredCommandReachesMcpTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "RetroTermMcpParity-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);

        _host = new InProcessSessionHost(_ => new InMemoryConnection());
        _registry = BuildTheRegistryTheDesktopBuilds(_tempRoot);
        _provider = new RetroTermToolProvider(_host, _registry, new ScriptLibrary(Path.Combine(_tempRoot, "scripts")));
    }

    public void Dispose()
    {
        _host.Dispose();
        if (Directory.Exists(_tempRoot))
        {
            Directory.Delete(_tempRoot, recursive: true);
        }
    }

    /// <summary>
    /// The same six registration calls the desktop makes, with stub dependencies.
    /// </summary>
    /// <param name="tempRoot">
    /// A scratch directory for the configuration file, so the user's real connection list is never
    /// touched.
    /// </param>
    /// <returns>
    /// A registry holding every command a running RetroTerm exposes.
    /// </returns>
    /// <remarks>
    /// Mirrors <c>MainWindow.Mcp.cs</c>, <c>GetOrCreateCommandRegistry</c>. Kept in the same ORDER as
    /// that method so the two can be read side by side.
    ///
    /// The delegates are stubs that return a string, because the commands only need something
    /// callable to be REGISTERED. Nothing in this file invokes them.
    /// </remarks>
    private static CommandRegistry BuildTheRegistryTheDesktopBuilds(string tempRoot)
    {
        var registry = new CommandRegistry();

        BuiltinCommands.RegisterAll(registry);

        ConnectionCommands.RegisterAll(registry,
            new ConfigurationManager(Path.Combine(tempRoot, "connections.json")));

        RetroTerm.Core.Protocols.Kermit.KermitCommands.RegisterAll(registry);

        RetroTerm.Core.Protocols.WebSocket.Gateway.GatewayCommand.RegisterAll(registry, () => null);

        ScreenshotCommand.RegisterAll(registry, (session, path) => null);

        LocalInputCommands.RegisterAll(registry,
            (TerminalSession session, string key, bool shift) => null,
            LocalZoomStub,
            (TerminalSession session, string? text) => null);

        return registry;
    }

    /// <summary>
    /// A stub for the zoom delegate, which has an out parameter and so cannot be a lambda.
    /// </summary>
    /// <param name="session">
    /// Ignored.
    /// </param>
    /// <param name="percent">
    /// Ignored.
    /// </param>
    /// <param name="step">
    /// Ignored.
    /// </param>
    /// <param name="nowPercent">
    /// Always 100.
    /// </param>
    /// <returns>
    /// Always null, meaning no error.
    /// </returns>
    private static string? LocalZoomStub(TerminalSession session, int? percent, int step, out int nowPercent)
    {
        nowPercent = 100;
        return null;
    }

    /// <summary>
    /// The command groups the desktop registers beyond the built-ins, and one command name from each
    /// that proves the group arrived.
    /// </summary>
    /// <remarks>
    /// Written out by hand ON PURPOSE. If this test derived both sides from the same registry it
    /// would agree with itself no matter what the desktop did - the assertion-that-passes-for-the-
    /// wrong-reason failure this repository has already been bitten by three times. A name here is a
    /// claim about <c>MainWindow.Mcp.cs</c>, checked against it.
    /// </remarks>
    private static readonly (string Group, string Command)[] DesktopGroups =
    {
        ("BuiltinCommands", "SEND"),
        ("ConnectionCommands", "CONNECT"),
        ("KermitCommands", "SENDFILE"),
        ("GatewayCommand", "GATEWAY"),
        ("ScreenshotCommand", "SCREENSHOT"),
        ("LocalInputCommands", "LOCALKEY"),
        ("LocalInputCommands", "ZOOM"),
        ("LocalInputCommands", "PASTE"),
    };

    [Fact]
    public void EveryDesktopCommandGroupIsRepresented()
    {
        // Guards the premise of every other test in this file. If somebody adds a seventh group to
        // MainWindow.Mcp.cs and not to BuildTheRegistryTheDesktopBuilds, the tests below would still
        // pass while covering less than they claim. This does not catch that on its own - but it does
        // catch the reverse, a group being dropped from either side.
        for (int i = 0; i < DesktopGroups.Length; i++)
        {
            var (group, command) = DesktopGroups[i];
            Assert.True(_registry.TryGet(command, out _),
                group + " no longer registers " + command + ". If the desktop changed, change "
                + "BuildTheRegistryTheDesktopBuilds to match; if it did not, this is a real regression.");
        }
    }

    [Fact]
    public void EveryRegisteredCommandHasAnMcpTool()
    {
        // The question Ronny actually asked. Not "does the generator work" - it does - but "is
        // everything the app registers reachable over MCP", which nothing could answer before.
        var tools = _provider.BuildToolList();

        var toolNames = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < tools.Count; i++)
        {
            toolNames.Add(tools[i].Name);
        }

        var commands = _registry.Commands;
        Assert.True(commands.Count > 20,
            "only " + commands.Count.ToString(CultureInfo.InvariantCulture) + " commands registered - "
            + "the harness is wrong, not the code");

        for (int i = 0; i < commands.Count; i++)
        {
            string expected = "terminal_" + commands[i].Name.ToLowerInvariant();
            Assert.True(toolNames.Contains(expected),
                commands[i].Name + " is a registered command with no MCP tool. Expected a tool named "
                + expected + ". A command that works in a script and not over MCP is the two-surface "
                + "trap CLAUDE.md warns about.");
        }
    }

    [Fact]
    public void EveryCommandParameterReachesTheToolSchema()
    {
        // A tool that exists but drops a parameter is the same defect wearing a smaller hat: the
        // caller can name the command and cannot reach half of it. SEND swallowing its own argument
        // over MCP is exactly how this went wrong before.
        var tools = _provider.BuildToolList();

        var byName = new Dictionary<string, ModelContextProtocol.Protocol.Tool>(StringComparer.Ordinal);
        for (int i = 0; i < tools.Count; i++)
        {
            byName[tools[i].Name] = tools[i];
        }

        var commands = _registry.Commands;
        for (int i = 0; i < commands.Count; i++)
        {
            var command = commands[i];
            string toolName = "terminal_" + command.Name.ToLowerInvariant();
            Assert.True(byName.TryGetValue(toolName, out var tool), toolName + " is missing");

            var properties = PropertyNamesOf(tool!);

            // Every tool needs sessionId - it is how the caller says WHICH terminal.
            Assert.True(properties.Contains("sessionId"),
                toolName + " has no sessionId parameter, so it cannot name a session");

            var parameters = command.Parameters;
            for (int p = 0; p < parameters.Count; p++)
            {
                Assert.True(properties.Contains(parameters[p].Name),
                    command.Name + " declares the parameter '" + parameters[p].Name
                    + "' but the MCP tool " + toolName + " does not expose it.");
            }
        }
    }

    /// <summary>
    /// The property names in a tool's input schema.
    /// </summary>
    /// <param name="tool">
    /// The tool to read.
    /// </param>
    /// <returns>
    /// The names, or an empty set if the schema declares no properties.
    /// </returns>
    private static HashSet<string> PropertyNamesOf(ModelContextProtocol.Protocol.Tool tool)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);

        if (!tool.InputSchema.TryGetProperty("properties", out var properties))
        {
            return names;
        }

        // Indexed iteration is not available on a JsonElement object, so the enumerator is driven by
        // hand rather than with foreach - which is banned here because it allocates.
        var walker = properties.EnumerateObject();
        while (walker.MoveNext())
        {
            names.Add(walker.Current.Name);
        }

        return names;
    }
}
