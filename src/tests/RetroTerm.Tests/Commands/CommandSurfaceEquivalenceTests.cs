using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using RetroTerm.Core.Commands;
using RetroTerm.Core.Commands.Builtin;
using RetroTerm.Core.Protocols;
using RetroTerm.Core.Scripting;
using RetroTerm.Core.Session;
using RetroTerm.Core.Terminal.Emulators;
using RetroTerm.Mcp;
using Xunit;

namespace RetroTerm.Tests.Commands;

/// <summary>
/// EVERY registered command, driven through BOTH front ends, compared.
///
/// WHY THIS EXISTS. A command reaches the machine two ways: the script DSL
/// (<see cref="ScriptParser"/>) and the MCP tool provider
/// (<see cref="RetroTermToolProvider"/>). When a behaviour is implemented at the call
/// site instead of on the command's metadata, it gets fixed on one surface and stays
/// broken on the other. That is not hypothetical: SEND swallowing <c>\r</c> was fixed
/// in the script parser and shipped broken over MCP for weeks afterwards, because the
/// only MCP tests were structural — "does the tool exist, is its schema well formed" —
/// and a schema cannot see a decoding difference.
///
/// The existing registry-driven tests (ToolList_ContainsSessionToolsAndOneToolPerCommand,
/// ToolList_CommandTools_CarryGeneratedSchemas, Help_Overview_ListsEveryRegisteredCommand)
/// already cover STRUCTURE exhaustively. This file covers BEHAVIOUR the same way, so a
/// new command is checked the moment it is registered rather than when someone
/// remembers to hand-write a test for it.
///
/// HOW. Each real command is wrapped in a <see cref="RecordingCommand"/> that copies the
/// CommandArgs it was handed and returns success without touching a session. Both
/// surfaces run against that registry, so this exercises the REAL ScriptParser and the
/// REAL tool provider — only the leaf is swapped.
///
/// WHAT "EQUIVALENT" MEANS. Not "identical input text" — the two surfaces have
/// deliberately different authoring layers. The script DSL has quoted strings with
/// escapes as a LANGUAGE feature (every quoted string is decoded by the tokenizer);
/// MCP has raw JSON strings and decodes only parameters flagged
/// <see cref="CommandParameter.DecodeEscapes"/>. So the contract asserted here is:
///
///     for one intended value V, each surface spelled the way ITS OWN notation
///     requires must deliver exactly V to the command.
///
/// That catches the original bug in both directions: MCP failing to decode a flagged
/// parameter, and MCP over-eagerly decoding an unflagged one (which would mangle
/// WAITFOR's regex patterns).
/// </summary>
public sealed class CommandSurfaceEquivalenceTests : IDisposable
{
    // ─────────────────────────────────────────────────────────────
    // Harness
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Stands in for a real command: same identity, same metadata (both surfaces read
    /// metadata to validate, so it must be preserved exactly), but instead of doing
    /// anything it records what it was handed.
    /// </summary>
    private sealed class RecordingCommand : ISessionCommand
    {
        private readonly ISessionCommand _inner;

        public CommandArgs? LastArgs { get; private set; }
        public int CallCount { get; private set; }

        public RecordingCommand(ISessionCommand inner) => _inner = inner;

        public string Name => _inner.Name;
        public string Summary => _inner.Summary;
        public IReadOnlyList<CommandParameter> Parameters => _inner.Parameters;
        public string Example => _inner.Example;
        public bool ProducesOutput => _inner.ProducesOutput;
        public bool CanTimeOut => _inner.CanTimeOut;
        public bool ProducesCapture => _inner.ProducesCapture;

        public Task<CommandResult> ExecuteAsync(TerminalSession session, CommandArgs args,
            CancellationToken cancellationToken)
        {
            LastArgs = args;
            CallCount++;
            return Task.FromResult(CommandResult.Ok("recorded"));
        }

        public void Reset()
        {
            LastArgs = null;
            CallCount = 0;
        }
    }

    /// <summary>
    /// A host with exactly one session and no network. The provider only needs
    /// GetSession to hand something non-null to the command; the recording command
    /// never touches it.
    /// </summary>
    private sealed class SingleSessionHost : ITerminalSessionHost
    {
        private readonly Guid _id = Guid.NewGuid();
        private readonly TerminalSession _session;

        public SingleSessionHost()
        {
            _session = new TerminalSession(new VT100Emulator(80, 24), "EquivalenceTest");
        }

        public Guid Id => _id;

        public Task<Guid> OpenSessionAsync(ConnectionFactory.ConnectionParameters parameters,
            CancellationToken cancellationToken = default) => Task.FromResult(_id);

        public TerminalSession? GetSession(Guid id) => id == _id ? _session : null;

        public IReadOnlyList<SessionInfo> ListSessions() =>
            new[] { new SessionInfo(_id, "EquivalenceTest", false, "VT100") };

        public Task CloseSessionAsync(Guid id) => Task.CompletedTask;
    }

    private readonly CommandRegistry _recordingRegistry = new CommandRegistry();
    private readonly Dictionary<string, RecordingCommand> _recorders =
        new Dictionary<string, RecordingCommand>(StringComparer.OrdinalIgnoreCase);
    private readonly ScriptParser _parser;
    private readonly RetroTermToolProvider _provider;
    private readonly SingleSessionHost _host = new SingleSessionHost();
    private readonly string _tempScripts;

    public CommandSurfaceEquivalenceTests()
    {
        var real = new CommandRegistry();
        BuiltinCommands.RegisterAll(real);

        var commands = real.Commands;
        for (int i = 0; i < commands.Count; i++)
        {
            var recorder = new RecordingCommand(commands[i]);
            _recorders[commands[i].Name] = recorder;
            _recordingRegistry.Register(recorder);
        }

        _tempScripts = Path.Combine(Path.GetTempPath(), "RetroTermEquiv-" + Guid.NewGuid().ToString("N"));
        _parser = new ScriptParser(_recordingRegistry);
        _provider = new RetroTermToolProvider(_host, _recordingRegistry, new ScriptLibrary(_tempScripts));
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempScripts))
        {
            Directory.Delete(_tempScripts, recursive: true);
        }
    }

    // ─────────────────────────────────────────────────────────────
    // The generated matrix
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Every (command, string parameter) pair in the registry. Regenerates itself when a
    /// command is added — that is the whole point.
    /// </summary>
    public static IEnumerable<object[]> StringParameters()
    {
        var registry = new CommandRegistry();
        BuiltinCommands.RegisterAll(registry);
        var commands = registry.Commands;
        for (int i = 0; i < commands.Count; i++)
        {
            var parameters = commands[i].Parameters;
            for (int j = 0; j < parameters.Count; j++)
            {
                if (parameters[j].Type == CommandParameterType.String)
                {
                    yield return new object[] { commands[i].Name, parameters[j].Name };
                }
            }
        }
    }

    /// <summary>
    /// Every (command, parameter) pair regardless of type.
    /// </summary>
    public static IEnumerable<object[]> AllParameters()
    {
        var registry = new CommandRegistry();
        BuiltinCommands.RegisterAll(registry);
        var commands = registry.Commands;
        for (int i = 0; i < commands.Count; i++)
        {
            var parameters = commands[i].Parameters;
            for (int j = 0; j < parameters.Count; j++)
            {
                yield return new object[] { commands[i].Name, parameters[j].Name };
            }
        }
    }

    /// <summary>
    /// Every registered command name.
    /// </summary>
    public static IEnumerable<object[]> AllCommands()
    {
        var registry = new CommandRegistry();
        BuiltinCommands.RegisterAll(registry);
        var commands = registry.Commands;
        for (int i = 0; i < commands.Count; i++)
        {
            yield return new object[] { commands[i].Name };
        }
    }

    // ─────────────────────────────────────────────────────────────
    // The contract
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// A plain value with nothing special in it must arrive byte-identical on both
    /// surfaces. The trivial case, and the one that would catch a surface mangling
    /// values wholesale (trimming, case-folding, appending a terminator — SEND used to
    /// append a CR of its own).
    /// </summary>
    [Theory]
    [MemberData(nameof(StringParameters))]
    public async Task PlainValue_ArrivesIdentically(string commandName, string parameterName)
    {
        const string intended = "plain-value_42";
        await AssertBothSurfacesDeliver(commandName, parameterName, intended);
    }

    /// <summary>
    /// THE regression. A value carrying control characters must arrive as those control
    /// characters, not as the two-character text that spells them. Written the way each
    /// surface's own notation demands: escaped in the script (the tokenizer decodes every
    /// quoted string), and escaped over MCP only where DecodeEscapes says so.
    /// </summary>
    [Theory]
    [MemberData(nameof(StringParameters))]
    public async Task ControlCharacters_ArriveDecodedOnBothSurfaces(string commandName, string parameterName)
    {
        // CR, LF, ESC and TAB — the four that matter on a terminal wire. \u001b, never
        // "\x1b": C# \x is VARIABLE length and swallows following hex digits.
        const string intended = "SYSTEM\r\n\u001b[2J\tX";
        await AssertBothSurfacesDeliver(commandName, parameterName, intended);
    }

    /// <summary>
    /// A backslash that means a literal backslash — a Windows path, a regex — must
    /// survive both surfaces. This is the direction that breaks if someone makes the MCP
    /// layer decode escapes for EVERY parameter instead of only the flagged ones: WAITFOR
    /// patterns like \d+ would silently stop matching.
    /// </summary>
    [Theory]
    [MemberData(nameof(StringParameters))]
    public async Task LiteralBackslashes_Survive(string commandName, string parameterName)
    {
        const string intended = @"C:\temp\data \d+ 100%";
        await AssertBothSurfacesDeliver(commandName, parameterName, intended);
    }

    /// <summary>
    /// A quote inside a value. The script surface has to escape it to survive
    /// tokenizing; MCP does not. Both must still deliver one quote character.
    /// </summary>
    [Theory]
    [MemberData(nameof(StringParameters))]
    public async Task EmbeddedQuotes_Survive(string commandName, string parameterName)
    {
        const string intended = "say \"hello\" twice";
        await AssertBothSurfacesDeliver(commandName, parameterName, intended);
    }

    /// <summary>
    /// A required parameter left out must fail on both surfaces, and the message must
    /// name the parameter — the shared check lives in CommandRegistry.ExecuteAsync, the
    /// script parser additionally catches it at PARSE time so the editor can flag the
    /// line. Either way the command must never run.
    /// </summary>
    [Theory]
    [MemberData(nameof(AllCommands))]
    public async Task MissingRequiredParameter_FailsOnBothSurfaces_WithoutRunningTheCommand(string commandName)
    {
        var command = Resolve(commandName);
        var required = FirstRequired(command);
        if (required == null)
        {
            return; // nothing to omit
        }

        var recorder = _recorders[commandName];

        recorder.Reset();
        var parsed = _parser.Parse(commandName);
        Assert.NotEmpty(parsed.Errors);
        Assert.Contains(required.Name, DescribeErrors(parsed), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, recorder.CallCount);

        recorder.Reset();
        var mcp = await CallMcpAsync(commandName, new Dictionary<string, string>());
        Assert.True(mcp.IsError ?? false,
            $"{commandName}: MCP accepted a call with required parameter '{required.Name}' missing");
        Assert.Contains(required.Name, McpText(mcp), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, recorder.CallCount);
    }

    /// <summary>
    /// An omitted OPTIONAL parameter must be absent on both surfaces, not present as an
    /// empty string — a command distinguishes "not given" (use the default) from "given
    /// as empty" through CommandArgs.Contains.
    /// </summary>
    [Theory]
    [MemberData(nameof(AllParameters))]
    public async Task OmittedOptionalParameter_IsAbsentOnBothSurfaces(string commandName, string parameterName)
    {
        var command = Resolve(commandName);
        var parameter = FindParameter(command, parameterName);
        if (parameter.Required)
        {
            return;
        }

        // Supply every OTHER required parameter so the call is otherwise valid.
        var supplied = new Dictionary<string, string>();
        var parameters = command.Parameters;
        for (int i = 0; i < parameters.Count; i++)
        {
            if (parameters[i].Required)
            {
                supplied[parameters[i].Name] = SampleFor(parameters[i]);
            }
        }

        var scriptArgs = await RunScriptAsync(commandName, supplied);
        Assert.False(scriptArgs.Contains(parameterName),
            $"{commandName}: script surface invented a value for omitted optional '{parameterName}'");

        var mcpArgs = await RunMcpAsync(commandName, supplied, escapeForMcp: false);
        Assert.False(mcpArgs.Contains(parameterName),
            $"{commandName}: MCP surface invented a value for omitted optional '{parameterName}'");
    }

    /// <summary>
    /// A value that is not a whole number, given for an Int parameter, must be rejected
    /// on both surfaces rather than silently falling back to the default.
    ///
    /// This is the shape that already bit once: `timeout=300/ontimeout=wake` (a slash
    /// typo) parsed as one string, GetInt fell back to the default, and the script "ran"
    /// with the wrong timeout and no jump. The script parser gained a parse-time type
    /// check for it; this asserts the MCP surface refuses it too.
    /// </summary>
    [Theory]
    [MemberData(nameof(AllParameters))]
    public async Task NonNumericValueForIntParameter_IsRejectedOnBothSurfaces(string commandName, string parameterName)
    {
        var command = Resolve(commandName);
        var parameter = FindParameter(command, parameterName);
        if (parameter.Type != CommandParameterType.Int)
        {
            return;
        }

        var supplied = BuildRequired(command);
        supplied[parameterName] = "300/ontimeout=wake";

        var recorder = _recorders[commandName];

        recorder.Reset();
        var parsed = _parser.Parse(BuildScriptLine(commandName, supplied));
        Assert.NotEmpty(parsed.Errors);
        Assert.Equal(0, recorder.CallCount);

        recorder.Reset();
        var mcp = await CallMcpAsync(commandName, supplied);
        Assert.True(mcp.IsError ?? false,
            $"{commandName}: MCP accepted '{parameterName}=300/ontimeout=wake' for an Int parameter — "
            + "GetInt will silently use the default and the caller will never know");
        Assert.Equal(0, recorder.CallCount);
    }

    /// <summary>
    /// No command may declare a parameter named after a step-level keyword. The script
    /// parser consumes optional=, ontimeout= and into= BEFORE it looks at the command's
    /// parameters, so such a parameter could never be set from a script while working
    /// fine over MCP — a divergence built into the language rather than into a call site.
    /// </summary>
    [Theory]
    [MemberData(nameof(AllParameters))]
    public void NoParameterCollidesWithAStepKeyword(string commandName, string parameterName)
    {
        Assert.False(
            string.Equals(parameterName, "optional", StringComparison.OrdinalIgnoreCase)
            || string.Equals(parameterName, "ontimeout", StringComparison.OrdinalIgnoreCase)
            || string.Equals(parameterName, "into", StringComparison.OrdinalIgnoreCase),
            $"{commandName} declares a parameter '{parameterName}', which the script parser "
            + "treats as a step-level keyword — it could never be set from a script.");
    }

    /// <summary>
    /// The mirror of ToolList_ContainsSessionToolsAndOneToolPerCommand: every command
    /// tool must map BACK to a registered command. A hand-written tool that bypasses the
    /// registry also bypasses its escape rules, its required-parameter check and its
    /// generated help.
    /// </summary>
    [Fact]
    public void EveryCommandToolMapsBackToARegisteredCommand()
    {
        var tools = _provider.BuildToolList();
        int checkedCount = 0;
        for (int i = 0; i < tools.Count; i++)
        {
            var name = tools[i].Name;
            if (!name.StartsWith("terminal_", StringComparison.Ordinal))
            {
                continue;
            }
            var suffix = name.Substring("terminal_".Length);
            if (!_recordingRegistry.TryGet(suffix, out _))
            {
                // Session/capability tools (open, close, list, run_script...) are
                // deliberately not registry commands; they are enumerated explicitly so a
                // NEW one cannot slip in unnoticed.
                Assert.Contains(suffix, KnownNonCommandTools);
                continue;
            }
            checkedCount++;
        }
        Assert.True(checkedCount > 0, "no command-backed tools found — the harness is wrong, not the code");
    }

    /// <summary>
    /// Tools that legitimately are not registry commands. Listed rather than pattern-matched
    /// so adding one is a deliberate edit here, visible in review.
    /// </summary>
    private static readonly string[] KnownNonCommandTools =
    {
        "open", "close", "list", "connect", "disconnect", "run_script", "scripts", "help",

        // build - which binary is answering. Not a registry command on purpose: every registry
        // command takes a sessionId, and the question "am I talking to the right build" has to be
        // answerable BEFORE anything is connected, which is exactly when it matters most.
        "build"
    };

    // ─────────────────────────────────────────────────────────────
    // Driving the two surfaces
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// The core assertion: one intended value, spelled for each surface's own notation,
    /// must reach the command unchanged on both.
    /// </summary>
    private async Task AssertBothSurfacesDeliver(string commandName, string parameterName, string intended)
    {
        var command = Resolve(commandName);
        var supplied = BuildRequired(command);
        supplied[parameterName] = intended;

        var scriptArgs = await RunScriptAsync(commandName, supplied);
        Assert.True(scriptArgs.TryGet(parameterName, out var fromScript),
            $"{commandName}: script surface delivered no '{parameterName}'");
        Assert.Equal(intended, fromScript);

        var mcpArgs = await RunMcpAsync(commandName, supplied,
            escapeForMcp: FindParameter(command, parameterName).DecodeEscapes);
        Assert.True(mcpArgs.TryGet(parameterName, out var fromMcp),
            $"{commandName}: MCP surface delivered no '{parameterName}'");
        Assert.Equal(intended, fromMcp);
    }

    /// <summary>
    /// Parses and runs one command line, returning what the command was handed.
    /// </summary>
    private async Task<CommandArgs> RunScriptAsync(string commandName, Dictionary<string, string> intendedValues)
    {
        var recorder = _recorders[commandName];
        recorder.Reset();

        var line = BuildScriptLine(commandName, intendedValues);
        var parsed = _parser.Parse(line);
        Assert.True(parsed.Errors.Count == 0,
            $"{commandName}: script line did not parse — {DescribeErrors(parsed)}\n  line: {line}");
        Assert.Single(parsed.Steps);

        // The parser has already produced the args; executing through the registry proves
        // they survive dispatch intact rather than being rebuilt somewhere on the way.
        var session = _host.GetSession(_host.Id)!;
        var result = await _recordingRegistry.ExecuteAsync(commandName, session, parsed.Steps[0].Args);
        Assert.True(result.Success, $"{commandName}: script dispatch failed — {result.Error}");
        Assert.NotNull(recorder.LastArgs);
        return recorder.LastArgs!;
    }

    /// <summary>
    /// Calls the MCP tool, returning what the command was handed.
    /// </summary>
    private async Task<CommandArgs> RunMcpAsync(string commandName, Dictionary<string, string> intendedValues,
        bool escapeForMcp)
    {
        var recorder = _recorders[commandName];
        recorder.Reset();

        var result = await CallMcpAsync(commandName, intendedValues, escapeForMcp);
        Assert.False(result.IsError ?? false, $"{commandName}: MCP call failed — {McpText(result)}");
        Assert.NotNull(recorder.LastArgs);
        return recorder.LastArgs!;
    }

    private async Task<ModelContextProtocol.Protocol.CallToolResult> CallMcpAsync(
        string commandName, Dictionary<string, string> intendedValues, bool escapeForMcp = false)
    {
        var json = new Dictionary<string, JsonElement>
        {
            ["sessionId"] = JsonSerializer.SerializeToElement(_host.Id.ToString())
        };
        var e = intendedValues.GetEnumerator();
        while (e.MoveNext())
        {
            // MCP has no quoting layer: a flagged parameter carries the escape notation
            // and the provider decodes it; everything else goes verbatim.
            var text = escapeForMcp ? EscapeForNotation(e.Current.Value) : e.Current.Value;
            json[e.Current.Key] = JsonSerializer.SerializeToElement(text);
        }
        return await _provider.CallToolAsync("terminal_" + commandName, json, CancellationToken.None);
    }

    // ─────────────────────────────────────────────────────────────
    // Spelling one intended value for each surface
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Writes a script line. Every value goes in a quoted string, because the script
    /// tokenizer decodes escapes in EVERY quoted string regardless of DecodeEscapes —
    /// that is a property of the language, not of the parameter.
    /// </summary>
    private static string BuildScriptLine(string commandName, Dictionary<string, string> intendedValues)
    {
        var sb = new StringBuilder(commandName);
        var e = intendedValues.GetEnumerator();
        while (e.MoveNext())
        {
            sb.Append(' ').Append(e.Current.Key).Append("=\"")
              .Append(EscapeForNotation(e.Current.Value)).Append('"');
        }
        return sb.ToString();
    }

    /// <summary>
    /// Spells a value in the shared backslash notation (ScriptStringEscapes) so that
    /// decoding it returns the original. Both surfaces use the same table, which is
    /// exactly why the notation can be written once here.
    /// </summary>
    private static string EscapeForNotation(string value)
    {
        var sb = new StringBuilder(value.Length + 8);
        for (int i = 0; i < value.Length; i++)
        {
            char c = value[i];
            switch (c)
            {
                case '\\': sb.Append("\\\\"); break;
                case '"': sb.Append("\\\""); break;
                case '\r': sb.Append("\\r"); break;
                case '\n': sb.Append("\\n"); break;
                case '\t': sb.Append("\\t"); break;
                case '\u001b': sb.Append("\\e"); break;
                default: sb.Append(c); break;
            }
        }
        return sb.ToString();
    }

    // ─────────────────────────────────────────────────────────────
    // Small helpers
    // ─────────────────────────────────────────────────────────────

    private ISessionCommand Resolve(string commandName)
    {
        Assert.True(_recordingRegistry.TryGet(commandName, out var command), $"unknown command {commandName}");
        return command;
    }

    private static CommandParameter FindParameter(ISessionCommand command, string parameterName)
    {
        var parameters = command.Parameters;
        for (int i = 0; i < parameters.Count; i++)
        {
            if (string.Equals(parameters[i].Name, parameterName, StringComparison.OrdinalIgnoreCase))
            {
                return parameters[i];
            }
        }
        Assert.Fail($"{command.Name} has no parameter '{parameterName}'");
        return null!;
    }

    private static CommandParameter? FirstRequired(ISessionCommand command)
    {
        var parameters = command.Parameters;
        for (int i = 0; i < parameters.Count; i++)
        {
            if (parameters[i].Required) return parameters[i];
        }
        return null;
    }

    private static Dictionary<string, string> BuildRequired(ISessionCommand command)
    {
        var supplied = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var parameters = command.Parameters;
        for (int i = 0; i < parameters.Count; i++)
        {
            if (parameters[i].Required)
            {
                supplied[parameters[i].Name] = SampleFor(parameters[i]);
            }
        }
        return supplied;
    }

    /// <summary>
    /// A valid value of the parameter's declared type, for filling in the rest of a call.
    /// </summary>
    private static string SampleFor(CommandParameter parameter)
    {
        return parameter.Type switch
        {
            CommandParameterType.Int => "1000",
            CommandParameterType.Bool => "true",
            _ => "sample"
        };
    }

    private static string DescribeErrors(ParsedScript parsed)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < parsed.Errors.Count; i++)
        {
            if (i > 0) sb.Append("; ");
            sb.Append(parsed.Errors[i].Message);
        }
        return sb.ToString();
    }

    private static string McpText(ModelContextProtocol.Protocol.CallToolResult result)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < result.Content.Count; i++)
        {
            if (result.Content[i] is ModelContextProtocol.Protocol.TextContentBlock text)
            {
                sb.Append(text.Text);
            }
        }
        return sb.ToString();
    }
}
