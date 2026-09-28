using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using RetroTerm.Core.Commands;
using RetroTerm.Core.Scripting;
using RetroTerm.Core.Session;

namespace RetroTerm.Mcp;

/// <summary>
/// Hosts the MCP server inside the running application (the "server lives in the
/// running app" model): a Kestrel listener bound to LOCALHOST ONLY, speaking the MCP
/// streamable-HTTP transport at /mcp.
///
/// Claude Code connects with:
///   claude mcp add --transport http retroterm http://127.0.0.1:PORT/mcp
///
/// Security: the bind address is hard-coded to 127.0.0.1 — this tool types arbitrary
/// commands into machines, it must never listen on an external interface.
/// </summary>
public sealed class McpServerHost : IAsyncDisposable
{
    private readonly WebApplication _app;

    /// <summary>
    /// The URL the server is listening on (http://127.0.0.1:port/mcp).
    /// </summary>
    public string Url { get; }

    private McpServerHost(WebApplication app, string url)
    {
        _app = app;
        Url = url;
    }

    /// <summary>
    /// Builds and starts the server. The tool provider decides what the LLM can do;
    /// this class only owns transport and lifetime.
    /// </summary>
    public static async Task<McpServerHost> StartAsync(RetroTermToolProvider tools, int port,
        CancellationToken cancellationToken = default)
    {
        if (tools == null) throw new ArgumentNullException(nameof(tools));

        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders(); // the desktop app owns its own logging story

        // LOCALHOST ONLY — see class summary.
        builder.WebHost.UseUrls($"http://127.0.0.1:{port}");

        builder.Services
            .AddMcpServer(options =>
            {
                // The REAL build, not a hardcoded "1.0.0". A client sees this in the handshake, so
                // it is the earliest possible moment to notice it has reached the wrong copy of
                // the program - see BuildIdentity for the day that mattered.
                options.ServerInfo = new Implementation
                {
                    Name = "RetroTerm",
                    Version = Core.BuildIdentity.Short
                };

                options.ServerInstructions =
                    "RetroTerm terminal control. THIS SERVER IS " + Core.BuildIdentity.Describe() + ". " +
                    "Check that against the build you expect before trusting a test result - more than one " +
                    "copy of RetroTerm can be running and only the first gets this port. " +
                    "Open a PERSISTENT session with terminal_open, then drive it " +
                    "with terminal_send / terminal_waitfor / terminal_readscreen. Sequence steps by waiting on " +
                    "the RENDERED screen (terminal_waitfor), never with fixed delays. On a fresh SINTRAN " +
                    "connection send ESC first (terminal_sendraw bytes=ESC). terminal_help lists the command set.";
            })
            .WithHttpTransport()
            .WithListToolsHandler((request, ct) =>
                ValueTask.FromResult(new ListToolsResult { Tools = tools.BuildToolList() }))
            .WithCallToolHandler((request, ct) =>
                new ValueTask<CallToolResult>(
                    tools.CallToolAsync(request.Params?.Name ?? string.Empty, request.Params?.Arguments, ct)));

        var app = builder.Build();
        app.MapMcp("/mcp");

        await app.StartAsync(cancellationToken).ConfigureAwait(false);
        return new McpServerHost(app, $"http://127.0.0.1:{port}/mcp");
    }

    /// <summary>
    /// Convenience: builds the standard tool provider (built-in commands + default
    /// script library) over a session host, and starts the server with it.
    /// </summary>
    public static Task<McpServerHost> StartAsync(ITerminalSessionHost sessionHost, int port,
        CancellationToken cancellationToken = default)
    {
        var registry = new CommandRegistry();
        Core.Commands.Builtin.BuiltinCommands.RegisterAll(registry);
        var provider = new RetroTermToolProvider(sessionHost, registry, ScriptLibrary.CreateDefault());
        return StartAsync(provider, port, cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync().ConfigureAwait(false);
        await _app.DisposeAsync().ConfigureAwait(false);
    }
}
