using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using RetroTerm.Core.Commands;
using RetroTerm.Core.Commands.Builtin;
using RetroTerm.Core.Protocols.TelnetServer.Telnet;
using RetroTerm.Core.Scripting;
using RetroTerm.Core.Session;
using RetroTerm.Mcp;
using RetroTerm.TestServer.App;
using Xunit;

namespace RetroTerm.Tests.Mcp;

/// <summary>
/// The full end-to-end stack (P4.6): a real MCP client over streamable HTTP →
/// McpServerHost (Kestrel, localhost) → tool provider → session host → a REAL
/// TelnetConnection over TCP → the real RetroTerm TestServer.
/// This is exactly what Claude Code does when it connects to the desktop app,
/// minus the Avalonia window.
/// </summary>
public class McpEndToEndTests : IAsyncLifetime
{
    private int _telnetPort;
    private int _mcpPort;
    private McpServerHost? _mcpServer;
    private InProcessSessionHost? _sessionHost;
    private string? _tempScripts;

    private static int GetFreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    public async Task InitializeAsync()
    {
        // Real TestServer on a free TCP port. RunAsync never returns (accept loop) —
        // it dies with the test process; the port is random so runs never collide.
        _telnetPort = GetFreePort();
        var telnetServer = new TelnetServer(_telnetPort, new TestServerApp());
        _ = Task.Run(() => telnetServer.RunAsync());

        // MCP server over a headless session host with REAL connections.
        _mcpPort = GetFreePort();
        _sessionHost = new InProcessSessionHost();
        var registry = new CommandRegistry();
        BuiltinCommands.RegisterAll(registry);
        _tempScripts = Path.Combine(Path.GetTempPath(), "RetroTermMcpE2E-" + Guid.NewGuid().ToString("N"));
        var provider = new RetroTermToolProvider(_sessionHost, registry, new ScriptLibrary(_tempScripts));
        _mcpServer = await McpServerHost.StartAsync(provider, _mcpPort);
    }

    public async Task DisposeAsync()
    {
        if (_mcpServer != null)
        {
            await _mcpServer.DisposeAsync();
        }
        _sessionHost?.Dispose();
        if (_tempScripts != null && Directory.Exists(_tempScripts))
        {
            Directory.Delete(_tempScripts, recursive: true);
        }
    }

    private static string Text(CallToolResult result)
    {
        Assert.NotEmpty(result.Content);
        var block = Assert.IsType<TextContentBlock>(result.Content[0]);
        return block.Text;
    }

    [Fact]
    public async Task FullStack_OpenWaitReadClose_AgainstRealTestServer()
    {
        // A real MCP client, exactly like Claude Code's HTTP transport.
        await using var transport = new HttpClientTransport(new HttpClientTransportOptions
        {
            Endpoint = new Uri(_mcpServer!.Url)
        });
        await using var client = await McpClient.CreateAsync(transport);

        // tools/list over the wire shows the generated tool set.
        var tools = await client.ListToolsAsync();
        var names = new List<string>();
        for (int i = 0; i < tools.Count; i++)
        {
            names.Add(tools[i].Name);
        }
        Assert.Contains("terminal_open", names);
        Assert.Contains("terminal_waitfor", names);
        Assert.Contains("terminal_readscreen", names);

        // Open a REAL telnet session to the REAL TestServer.
        var open = await client.CallToolAsync("terminal_open",
            new Dictionary<string, object?>
            {
                ["host"] = "127.0.0.1",
                ["port"] = _telnetPort,
                ["emulator"] = "VT100"
            });
        Assert.NotEqual(true, open.IsError);
        var openText = Text(open);
        var sessionId = openText.Substring(openText.IndexOf(':') + 1);
        sessionId = sessionId.Substring(0, sessionId.IndexOf('\n')).Trim();

        // Wait for the TestServer's menu on the RENDERED screen — the server first
        // runs terminal detection (DA queries our VT100 emulator answers), so this
        // exercises the query/response path too.
        var wait = await client.CallToolAsync("terminal_waitfor",
            new Dictionary<string, object?>
            {
                ["sessionId"] = sessionId,
                ["pattern"] = "Main Menu",
                ["where"] = "screen",
                ["timeout"] = 20_000
            });
        Assert.NotEqual(true, wait.IsError);

        // Read the screen: the menu the human would see.
        var read = await client.CallToolAsync("terminal_readscreen",
            new Dictionary<string, object?> { ["sessionId"] = sessionId });
        Assert.NotEqual(true, read.IsError);
        Assert.Contains("Main Menu", Text(read));

        // Status shows a live connection with traffic.
        var status = await client.CallToolAsync("terminal_status",
            new Dictionary<string, object?> { ["sessionId"] = sessionId });
        Assert.Contains("connected: yes", Text(status));

        // Close it down.
        var close = await client.CallToolAsync("terminal_close",
            new Dictionary<string, object?> { ["sessionId"] = sessionId });
        Assert.NotEqual(true, close.IsError);

        var list = await client.CallToolAsync("terminal_list", new Dictionary<string, object?>());
        Assert.Contains("no open sessions", Text(list));
    }
}
