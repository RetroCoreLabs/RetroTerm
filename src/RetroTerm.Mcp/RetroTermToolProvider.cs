using System;
using System.Collections.Generic;
using System.Text;
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

namespace RetroTerm.Mcp;

/// <summary>
/// Builds the MCP tool list and dispatches tool calls.
///
/// The tool surface has two halves:
///  - session lifetime tools (terminal_open / terminal_close / terminal_list) that
///    talk to the <see cref="ITerminalSessionHost"/>;
///  - one tool per registered <see cref="ISessionCommand"/> ("terminal_" + name),
///    generated from the SAME registry the script DSL uses — a newly registered
///    command becomes an MCP tool with zero MCP-layer changes;
///  - script tools (terminal_run_script / terminal_scripts) over the script library.
///
/// Tool descriptions and input schemas come from the command metadata, so what the
/// LLM sees in tools/list is the same generated documentation HELP prints.
/// </summary>
public sealed class RetroTermToolProvider
{
    private readonly ITerminalSessionHost _host;
    private readonly CommandRegistry _registry;
    private readonly ScriptLibrary _scriptLibrary;
    private readonly ScriptParser _scriptParser;
    private readonly ScriptRunner _scriptRunner;

    // The stored-connection list terminal_open resolves name= against — the same list
    // CONNECT/CONNLIST and the Quick Connect window use. Null when the hosting process
    // has no stored connections (the bare McpServerHost convenience path); open-by-name
    // then reports that instead of pretending the name does not exist.
    private readonly ConfigurationManager? _configurationManager;

    public RetroTermToolProvider(ITerminalSessionHost host, CommandRegistry registry, ScriptLibrary scriptLibrary,
        ConfigurationManager? configurationManager = null)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _scriptLibrary = scriptLibrary ?? throw new ArgumentNullException(nameof(scriptLibrary));
        _configurationManager = configurationManager;
        _scriptParser = new ScriptParser(registry);
        _scriptRunner = new ScriptRunner(registry);
    }

    // ─────────────────────────────────────────────────────────────
    // tools/list
    // ─────────────────────────────────────────────────────────────

    public IList<Tool> BuildToolList()
    {
        var tools = new List<Tool>();

        tools.Add(new Tool
        {
            Name = "terminal_open",
            Description = "Open a PERSISTENT terminal session. Returns a sessionId used by every " +
                          "other tool. The session stays open across calls and turns — do not open-and-close " +
                          "per command; reconnecting mid-program can hang the line. Three ways to say where: " +
                          "name=<stored connection> (terminal_connlist shows them, serial included; other " +
                          "parameters are then ignored), host+port for ad-hoc telnet/ssh, or " +
                          "protocol=serial with port_name (+ baud, data_bits, parity, stop_bits) for ad-hoc serial.",
            InputSchema = Schema(
                ("name", "string", false, "Stored connection name — opens it exactly as saved, serial included; " +
                                          "every other parameter is ignored"),
                ("protocol", "string", false, "telnet, ssh or serial (default telnet)"),
                ("host", "string", false, "Host name or IP address (telnet/ssh; serial uses port_name)"),
                ("port", "integer", false, "TCP port (RetroCore terminal ports are plain telnet)"),
                ("emulator", "string", false, "Terminal emulator: VT100 (default) or TDV2200"),
                ("width", "integer", false, "Screen columns; default is the terminal's own (80)"),
                ("height", "integer", false, "Screen rows; default is the terminal's own (24 for a VT, 25 for a TDV)"),
                ("username", "string", false, "SSH user name"),
                ("password", "string", false, "SSH password"),
                ("port_name", "string", false, "Serial port name, e.g. COM11 (required for ad-hoc serial)"),
                ("baud", "integer", false, "Serial speed in bits per second, default 9600"),
                ("data_bits", "integer", false, "Serial data bits 5-8, default 8"),
                ("parity", "string", false, "Serial parity: none (default), odd, even, mark or space"),
                ("stop_bits", "string", false, "Serial stop bits: 1 (default), 1.5 or 2"))
        });

        tools.Add(new Tool
        {
            Name = "terminal_close",
            Description = "Close a terminal session opened with terminal_open.",
            InputSchema = Schema(
                ("sessionId", "string", true, "The session to close"))
        });

        tools.Add(new Tool
        {
            Name = "terminal_list",
            Description = "List open terminal sessions (id, title, connected, emulator) — lets a new turn " +
                          "find a session opened earlier.",
            InputSchema = Schema()
        });

        tools.Add(new Tool
        {
            Name = "terminal_build",
            Description = "Which BUILD of RetroTerm this server is - version, git commit, branch, whether "
                          + "the tree was dirty, and when it was built. Check it before trusting a test "
                          + "result: more than one copy of RetroTerm can be running from the same folder "
                          + "and only the first one gets this port, so a caller can reach an old binary "
                          + "without noticing.",
            InputSchema = Schema()
        });

        tools.Add(new Tool
        {
            Name = "terminal_run_script",
            Description = "Run a script against a session: either a stored script by name, or inline DSL text " +
                          "(one command per line, see terminal_help). Stops at the first failing step and " +
                          "returns the per-step transcript with timings — the session stays live for " +
                          "single-command poking afterwards.",
            InputSchema = Schema(
                ("sessionId", "string", true, "The session to run against"),
                ("name", "string", false, "Name of a stored script from the library"),
                ("script", "string", false, "Inline script text (used when name is not given)"))
        });

        tools.Add(new Tool
        {
            Name = "terminal_scripts",
            Description = "List the stored scripts in the script library, or save one (name + script).",
            InputSchema = Schema(
                ("save", "string", false, "Script name to save under; omit to just list"),
                ("script", "string", false, "Script text to save (required with save)"))
        });

        // One tool per registered command — the same registry the script DSL uses.
        var commands = _registry.Commands;
        for (int i = 0; i < commands.Count; i++)
        {
            var command = commands[i];
            var parameters = command.Parameters;

            var schemaParams = new (string Name, string Type, bool Required, string Description)[parameters.Count + 1];
            schemaParams[0] = ("sessionId", "string", true, "The session to operate on");
            for (int p = 0; p < parameters.Count; p++)
            {
                var param = parameters[p];
                var typeName = param.Type switch
                {
                    CommandParameterType.Int => "integer",
                    CommandParameterType.Bool => "boolean",
                    _ => "string"
                };
                var description = param.DefaultValue != null && !param.Required
                    ? $"{param.Description} (default {param.DefaultValue})"
                    : param.Description;
                schemaParams[p + 1] = (param.Name, typeName, param.Required, description);
            }

            tools.Add(new Tool
            {
                Name = "terminal_" + command.Name.ToLowerInvariant(),
                Description = $"{command.Summary}. Script DSL equivalent: {command.Example}",
                InputSchema = Schema(schemaParams)
            });
        }

        return tools;
    }

    // ─────────────────────────────────────────────────────────────
    // tools/call
    // ─────────────────────────────────────────────────────────────

    public async Task<CallToolResult> CallToolAsync(string name, IDictionary<string, JsonElement>? arguments,
        CancellationToken cancellationToken)
    {
        // Traffic log: every call and its result, shown live in View → MCP Log.
        McpTrafficLog.Instance.LogCall(name, DescribeArguments(arguments));
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        CallToolResult result;
        try
        {
            result = name switch
            {
                "terminal_open" => await OpenAsync(arguments, cancellationToken).ConfigureAwait(false),
                "terminal_close" => await CloseAsync(arguments).ConfigureAwait(false),
                "terminal_list" => ListSessions(),
                "terminal_build" => DescribeBuild(),
                "terminal_run_script" => await RunScriptAsync(arguments, cancellationToken).ConfigureAwait(false),
                "terminal_scripts" => Scripts(arguments),
                _ => await DispatchCommandAsync(name, arguments, cancellationToken).ConfigureAwait(false)
            };
        }
        catch (OperationCanceledException)
        {
            McpTrafficLog.Instance.LogResult(name, isError: true, "cancelled", stopwatch.Elapsed);
            throw;
        }
        catch (Exception ex)
        {
            // Rule 6: never swallow errors into silence — the failure text goes back
            // to the client with IsError set.
            result = Error($"{name} failed: {ex.Message}");
        }

        McpTrafficLog.Instance.LogResult(name, result.IsError == true, FirstText(result), stopwatch.Elapsed);
        return result;
    }

    /// <summary>
    /// Compact one-line rendering of call arguments for the traffic log.
    /// </summary>
    private static string DescribeArguments(IDictionary<string, JsonElement>? args)
    {
        if (args == null || args.Count == 0)
        {
            return "";
        }
        var sb = new StringBuilder(128);
        var e = args.GetEnumerator();
        bool first = true;
        while (e.MoveNext())
        {
            if (!first) sb.Append(' ');
            first = false;
            sb.Append(e.Current.Key).Append('=');
            // Never log passwords in clear text.
            if (string.Equals(e.Current.Key, "password", StringComparison.OrdinalIgnoreCase))
            {
                sb.Append("***");
            }
            else
            {
                sb.Append(JsonToString(e.Current.Value));
            }
        }
        return sb.ToString();
    }

    private static string FirstText(CallToolResult result)
    {
        if (result.Content.Count > 0 && result.Content[0] is TextContentBlock text)
        {
            return text.Text;
        }
        return "";
    }

    private async Task<CallToolResult> OpenAsync(IDictionary<string, JsonElement>? args, CancellationToken ct)
    {
        ConnectionFactory.ConnectionParameters parameters;

        var name = GetString(args, "name");
        if (name != null)
        {
            // Open a STORED connection exactly as saved — same rule as terminal_connect:
            // every other parameter is ignored. This is what lets a serial connection be
            // opened in one call, with no bounce through a foreign telnet host first.
            if (_configurationManager == null)
            {
                return Error("this server has no stored-connection list, so name= cannot be used here — " +
                             "give host/port or serial fields instead");
            }
            var config = ConnectionCommands.FindByName(_configurationManager, name);
            if (config == null)
            {
                return Error($"no stored connection named '{name}' — terminal_connlist shows them");
            }
            parameters = config.ToConnectionParameters();
            if (parameters.Protocol == ConnectionFactory.ProtocolType.Serial
                && string.IsNullOrWhiteSpace(parameters.PortName))
            {
                return Error($"stored connection '{name}' has no serial port name — " +
                             "terminal_connsave it with port_name=COM...");
            }
            config.MarkAsUsed();
            await _configurationManager.SaveAsync().ConfigureAwait(false);
        }
        else if (ConnectionFactory.ParseProtocolType(GetString(args, "protocol") ?? "telnet")
                 == ConnectionFactory.ProtocolType.Serial)
        {
            // Ad-hoc serial. host/port do not map onto a COM port — refuse them by name
            // rather than letting the factory die with its internal parameter name.
            if (GetString(args, "host") != null || (args != null && args.ContainsKey("port")))
            {
                return Error("terminal_open with protocol=serial takes port_name and baud, not host/port — " +
                             "e.g. port_name=COM11 baud=115200");
            }
            var portName = GetString(args, "port_name");
            if (portName == null)
            {
                return Error("terminal_open with protocol=serial needs port_name (e.g. port_name=COM11), " +
                             "plus optional baud, data_bits, parity, stop_bits — or name=<stored connection>");
            }

            int parityValue = 0;
            int stopBitsValue = 1;
            var problem = ConnectionFactory.ValidateSerialFields(
                GetInt(args, "baud", 9600), GetInt(args, "data_bits", 8),
                GetString(args, "parity"), GetString(args, "stop_bits"),
                ref parityValue, ref stopBitsValue);
            if (problem != null)
            {
                return Error("terminal_open: " + problem);
            }

            var serialEmulator = GetString(args, "emulator") ?? "VT100";
            var serialSize = EmulatorFactory.GetRecommendedSize(serialEmulator);
            parameters = new ConnectionFactory.ConnectionParameters
            {
                Protocol = ConnectionFactory.ProtocolType.Serial,
                PortName = portName,
                BaudRate = GetInt(args, "baud", 9600),
                DataBits = GetInt(args, "data_bits", 8),
                ParityValue = parityValue,
                StopBitsValue = stopBitsValue,
                EmulatorType = serialEmulator,
                // The terminal's own size unless the caller says otherwise. A TDV2200 opened here
                // used to default to 24 rows and lose row 25 (docs/manual-tests/FINDINGS-2026-08-20.md).
                Width = GetInt(args, "width", serialSize.width),
                Height = GetInt(args, "height", serialSize.height)
            };
        }
        else
        {
            var netEmulator = GetString(args, "emulator") ?? "VT100";
            var netSize = EmulatorFactory.GetRecommendedSize(netEmulator);
            parameters = new ConnectionFactory.ConnectionParameters
            {
                Protocol = ConnectionFactory.ParseProtocolType(GetString(args, "protocol") ?? "telnet"),
                Host = GetString(args, "host") ?? string.Empty,
                Port = GetInt(args, "port", 23),
                EmulatorType = netEmulator,
                Width = GetInt(args, "width", netSize.width),
                Height = GetInt(args, "height", netSize.height),
                Username = GetString(args, "username"),
                Password = GetString(args, "password")
            };

            if (parameters.Host.Length == 0)
            {
                return Error("terminal_open needs a stored connection name, a host, " +
                             "or protocol=serial with port_name");
            }
        }

        var id = await _host.OpenSessionAsync(parameters, ct).ConfigureAwait(false);
        return Ok($"sessionId: {id}\nconnected to {parameters.DisplayName} as {parameters.EmulatorType}");
    }

    private async Task<CallToolResult> CloseAsync(IDictionary<string, JsonElement>? args)
    {
        if (!TryGetSessionId(args, out var id, out var error))
        {
            return error!;
        }
        await _host.CloseSessionAsync(id).ConfigureAwait(false);
        return Ok($"closed {id}");
    }

    /// <summary>
    /// Reports which build is answering.
    /// </summary>
    /// <returns>
    /// The build identity as readable lines.
    /// </returns>
    /// <remarks>
    /// Needs no session on purpose. The question "am I talking to the right binary" comes up before
    /// anything is connected, and a tool that demanded a session first would be useless exactly then.
    /// </remarks>
    private CallToolResult DescribeBuild()
    {
        var sb = new StringBuilder();
        sb.AppendLine(Core.BuildIdentity.Describe());
        sb.AppendLine("version: " + Core.BuildIdentity.Version);
        sb.AppendLine("commit:  " + Core.BuildIdentity.Commit);
        sb.AppendLine("branch:  " + Core.BuildIdentity.Branch);
        sb.AppendLine("built:   " + Core.BuildIdentity.BuildDate + " " + Core.BuildIdentity.BuildTime);

        // Said out loud rather than left as a flag to interpret. A build made from an uncommitted
        // tree cannot be reproduced from its hash, and anybody comparing results needs to know.
        if (Core.BuildIdentity.IsDirty)
        {
            sb.AppendLine("NOTE: built from a tree with uncommitted changes, so the commit above does "
                + "not fully describe it");
        }

        return Ok(sb.ToString());
    }

    private CallToolResult ListSessions()
    {
        var sessions = _host.ListSessions();
        if (sessions.Count == 0)
        {
            return Ok("no open sessions");
        }

        var sb = new StringBuilder();
        for (int i = 0; i < sessions.Count; i++)
        {
            var s = sessions[i];
            sb.Append(s.Id).Append("  ")
              .Append(s.IsConnected ? "connected   " : "disconnected")
              .Append("  ").Append(s.EmulatorType)
              .Append("  ").Append(s.Title).Append('\n');
        }
        return Ok(sb.ToString());
    }

    private async Task<CallToolResult> RunScriptAsync(IDictionary<string, JsonElement>? args, CancellationToken ct)
    {
        if (!TryGetSessionId(args, out var id, out var error))
        {
            return error!;
        }
        var session = _host.GetSession(id);
        if (session == null)
        {
            return Error($"unknown sessionId {id} — use terminal_list");
        }

        string scriptText;
        var name = GetString(args, "name");
        if (name != null)
        {
            if (!_scriptLibrary.Exists(name))
            {
                return Error($"no stored script named '{name}' — terminal_scripts lists what exists");
            }
            scriptText = _scriptLibrary.Load(name);
        }
        else
        {
            scriptText = GetString(args, "script") ?? string.Empty;
            if (scriptText.Length == 0)
            {
                return Error("terminal_run_script needs either name or script");
            }
        }

        var parsed = _scriptParser.Parse(scriptText);
        if (!parsed.IsValid)
        {
            var sb = new StringBuilder("script has parse errors:\n");
            for (int i = 0; i < parsed.Errors.Count; i++)
            {
                sb.Append("  ").Append(parsed.Errors[i]).Append('\n');
            }
            return Error(sb.ToString());
        }

        var result = await _scriptRunner.RunAsync(parsed, session, ct).ConfigureAwait(false);
        return FormatRunResult(result);
    }

    private static CallToolResult FormatRunResult(ScriptRunResult result)
    {
        var sb = new StringBuilder();
        sb.Append(result.Success ? "SCRIPT OK" : "SCRIPT FAILED")
          .Append(" — ").Append(result.Transcript.Count).Append(" step(s), ")
          .Append((long)result.Elapsed.TotalMilliseconds).Append(" ms total\n");

        for (int i = 0; i < result.Transcript.Count; i++)
        {
            var step = result.Transcript[i];
            sb.Append(step.Result.Success ? "  ok   " : "  FAIL ")
              .Append("line ").Append(step.Step.LineNumber)
              .Append(" [").Append((long)step.Result.Elapsed.TotalMilliseconds).Append(" ms] ")
              .Append(step.Step.SourceText).Append('\n');
            if (!step.Result.Success && step.Result.Error != null)
            {
                sb.Append("       ").Append(step.Result.Error).Append('\n');
            }
            // Checkpoint steps (READSCREEN, READNEW, STATUS, HELP): their output IS
            // the point — include it in the transcript for the MCP client.
            if (step.ShowOutput && !string.IsNullOrEmpty(step.Result.Output))
            {
                sb.Append(step.Result.Output).Append('\n');
            }
        }

        if (!result.Success)
        {
            sb.Append(result.Error).Append('\n');
            // The screen at the failing step — the most valuable thing on a failure.
            if (result.Transcript.Count > 0)
            {
                var last = result.Transcript[result.Transcript.Count - 1].Result;
                if (last.Output != null)
                {
                    sb.Append("screen at failure:\n").Append(last.Output).Append('\n');
                }
            }
        }

        var toolResult = result.Success ? Ok(sb.ToString()) : Error(sb.ToString());
        return toolResult;
    }

    private CallToolResult Scripts(IDictionary<string, JsonElement>? args)
    {
        var saveName = GetString(args, "save");
        if (saveName != null)
        {
            var text = GetString(args, "script");
            if (text == null)
            {
                return Error("terminal_scripts with save needs the script text");
            }
            // Validate before saving so the library never holds a broken script silently.
            var parsed = _scriptParser.Parse(text);
            if (!parsed.IsValid)
            {
                return Error($"not saved — script has {parsed.Errors.Count} parse error(s), first: {parsed.Errors[0]}");
            }
            _scriptLibrary.Save(saveName, text);
            return Ok($"saved '{saveName}' ({parsed.Steps.Count} steps) to {_scriptLibrary.PathFor(saveName)}");
        }

        var names = _scriptLibrary.List();
        if (names.Count == 0)
        {
            return Ok($"no stored scripts (library: {_scriptLibrary.Directory})");
        }
        var sb = new StringBuilder();
        for (int i = 0; i < names.Count; i++)
        {
            sb.Append(names[i]).Append('\n');
        }
        return Ok(sb.ToString());
    }

    private async Task<CallToolResult> DispatchCommandAsync(string toolName,
        IDictionary<string, JsonElement>? args, CancellationToken ct)
    {
        const string prefix = "terminal_";
        if (!toolName.StartsWith(prefix, StringComparison.Ordinal))
        {
            return Error($"unknown tool '{toolName}'");
        }
        var commandName = toolName.Substring(prefix.Length);

        if (!_registry.TryGet(commandName, out var command))
        {
            return Error($"unknown tool '{toolName}'");
        }

        if (!TryGetSessionId(args, out var id, out var error))
        {
            return error!;
        }
        var session = _host.GetSession(id);
        if (session == null)
        {
            return Error($"unknown sessionId {id} — use terminal_list");
        }

        // Convert JSON arguments to the registry's text args (sessionId stays MCP-level).
        // A parameter flagged DecodeEscapes (SEND's text) carries the backslash-escape
        // notation the script parser normally decodes from a quoted string. Over MCP
        // there is NO quoting layer, so the raw JSON value skips the parser — we must
        // decode it here or "SYSTEM\r" reaches the wire as two literal characters
        // instead of a carriage return (the reported bug).
        var commandArgs = new CommandArgs();
        if (args != null)
        {
            var e = args.GetEnumerator();
            while (e.MoveNext())
            {
                if (string.Equals(e.Current.Key, "sessionId", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                var value = JsonToString(e.Current.Value);
                if (ShouldDecodeEscapes(command, e.Current.Key))
                {
                    if (!ScriptStringEscapes.TryDecode(value, out var decoded, out var escError))
                    {
                        return Error($"{toolName}: bad escape in '{e.Current.Key}' — {escError}");
                    }
                    value = decoded;
                }
                commandArgs.Set(e.Current.Key, value);
            }
        }

        var result = await _registry.ExecuteAsync(commandName, session, commandArgs, ct).ConfigureAwait(false);

        var sb = new StringBuilder();
        if (result.Error != null)
        {
            sb.Append(result.Error).Append('\n');
        }
        if (result.Output != null)
        {
            sb.Append(result.Output).Append('\n');
        }
        sb.Append("[took ").Append((long)result.Elapsed.TotalMilliseconds).Append(" ms]");

        return result.Success ? Ok(sb.ToString()) : Error(sb.ToString());
    }

    // ─────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────

    private bool TryGetSessionId(IDictionary<string, JsonElement>? args, out Guid id, out CallToolResult? error)
    {
        id = Guid.Empty;
        error = null;
        var text = GetString(args, "sessionId");
        if (text == null || !Guid.TryParse(text, out id))
        {
            error = Error("missing or invalid sessionId — terminal_open returns one, terminal_list shows open ones");
            return false;
        }
        return true;
    }

    private static string? GetString(IDictionary<string, JsonElement>? args, string name)
    {
        if (args == null || !args.TryGetValue(name, out var value))
        {
            return null;
        }
        return JsonToString(value);
    }

    private static int GetInt(IDictionary<string, JsonElement>? args, string name, int defaultValue)
    {
        if (args == null || !args.TryGetValue(name, out var value))
        {
            return defaultValue;
        }
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var n))
        {
            return n;
        }
        if (value.ValueKind == JsonValueKind.String && int.TryParse(value.GetString(), out var parsed))
        {
            return parsed;
        }
        return defaultValue;
    }

    /// <summary>
    /// True when the named parameter of this command is flagged DecodeEscapes — its
    /// value must run through the shared escape decoder before it reaches the command.
    /// </summary>
    private static bool ShouldDecodeEscapes(ISessionCommand command, string paramName)
    {
        var parameters = command.Parameters;
        for (int i = 0; i < parameters.Count; i++)
        {
            var p = parameters[i];
            if (string.Equals(p.Name, paramName, StringComparison.OrdinalIgnoreCase))
            {
                return p.DecodeEscapes;
            }
        }
        return false;
    }

    private static string JsonToString(JsonElement value)
    {
        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString() ?? string.Empty,
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.Null => string.Empty,
            _ => value.GetRawText()
        };
    }

    /// <summary>
    /// Builds a JSON Schema object element for a tool's parameters.
    /// </summary>
    private static JsonElement Schema(params (string Name, string Type, bool Required, string Description)[] parameters)
    {
        var sb = new StringBuilder(256);
        sb.Append("{\"type\":\"object\",\"properties\":{");
        for (int i = 0; i < parameters.Length; i++)
        {
            if (i > 0) sb.Append(',');
            sb.Append(JsonSerializer.Serialize(parameters[i].Name))
              .Append(":{\"type\":\"").Append(parameters[i].Type).Append("\",\"description\":")
              .Append(JsonSerializer.Serialize(parameters[i].Description))
              .Append('}');
        }
        sb.Append('}');

        bool any = false;
        for (int i = 0; i < parameters.Length; i++)
        {
            if (parameters[i].Required)
            {
                sb.Append(any ? "," : ",\"required\":[");
                sb.Append(JsonSerializer.Serialize(parameters[i].Name));
                any = true;
            }
        }
        if (any) sb.Append(']');

        sb.Append('}');
        using var doc = JsonDocument.Parse(sb.ToString());
        return doc.RootElement.Clone();
    }

    private static CallToolResult Ok(string text) => Result(text, isError: false);

    private static CallToolResult Error(string text) => Result(text, isError: true);

    private static CallToolResult Result(string text, bool isError)
    {
        return new CallToolResult
        {
            Content = new List<ContentBlock> { new TextContentBlock { Text = text } },
            IsError = isError
        };
    }
}
