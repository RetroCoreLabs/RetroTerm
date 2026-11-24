//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs/RetroTerm
// RetroTerm — Connecting today's machines to yesterday's systems
//

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Protocol;
using RetroIO.Protocols.Tn3270;
using RetroTerm.Core.Commands;
using RetroTerm.Core.Commands.Builtin;
using RetroTerm.Core.Configuration;
using RetroTerm.Core.Protocols;
using RetroTerm.Core.Scripting;
using RetroTerm.Core.Session;
using RetroTerm.Ibm3270;
using RetroTerm.Mcp;
using RetroTerm.Tests.TDV;
using Xunit;

namespace RetroTerm.Tests.Mcp;

/// <summary>
/// PLAN.md, "Every 3270 feature reachable over MCP": the 3278 model (which is also the screen
/// size) and the EBCDIC code page, chosen per connection from terminal_open, CONNECT and
/// CONNSAVE, with the words of the tables the Manage Connections window uses
/// (Ibm3270ConnectionSettings). CommandSurfaceEquivalenceTests covers the CONNECT and CONNSAVE
/// arguments on both surfaces, generated from the registry; these cover what the choices DO.
/// </summary>
public sealed class Ibm3270ConnectionChoiceTests : IDisposable
{
    private readonly string _tempScripts;
    private readonly string _tempConfig;

    public Ibm3270ConnectionChoiceTests()
    {
        _tempScripts = Path.Combine(Path.GetTempPath(), "RetroTerm3270Choice-" + Guid.NewGuid().ToString("N"));
        _tempConfig = Path.Combine(Path.GetTempPath(), "RetroTerm3270Choice-" + Guid.NewGuid().ToString("N") + ".json");
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempScripts))
        {
            Directory.Delete(_tempScripts, recursive: true);
        }
        if (File.Exists(_tempConfig))
        {
            File.Delete(_tempConfig);
        }
    }

    private RetroTermToolProvider Provider(InProcessSessionHost host, ConfigurationManager? manager = null)
    {
        var registry = new CommandRegistry();
        BuiltinCommands.RegisterAll(registry);
        return new RetroTermToolProvider(host, registry, new ScriptLibrary(_tempScripts), manager ?? new ConfigurationManager(_tempConfig));
    }

    private static string Text(CallToolResult result)
    {
        Assert.NotEmpty(result.Content);
        return Assert.IsType<TextContentBlock>(result.Content[0]).Text;
    }

    private static Dictionary<string, JsonElement> Args(params (string Name, object Value)[] pairs)
    {
        var args = new Dictionary<string, JsonElement>();
        for (int i = 0; i < pairs.Length; i++)
        {
            args[pairs[i].Name] = JsonSerializer.SerializeToElement(pairs[i].Value);
        }
        return args;
    }

    private static CommandParameter Declared(string name)
    {
        var declared = RetroTermToolProvider.DeclaredArguments("terminal_open")!;
        for (int i = 0; i < declared.Count; i++)
        {
            if (declared[i].Name == name)
            {
                return declared[i];
            }
        }
        throw new InvalidOperationException("terminal_open declares no " + name + " argument");
    }

    private static Guid SessionId(CallToolResult result)
    {
        string text = Text(result);
        const string prefix = "sessionId: ";
        int start = text.IndexOf(prefix, StringComparison.Ordinal) + prefix.Length;
        int end = text.IndexOf('\n', start);
        return Guid.Parse(text.AsSpan(start, end - start));
    }

    [Fact]
    public void TheWordsAreTheWindowsOwnTables()
    {
        // The model words are the size box's models; the code page words are RetroIO's
        // CodePage.All, which fills the code page box - the same order, nothing added.
        Assert.Equal(ConnectionFactory.Ibm3278Models, Declared("model").AllowedValues);
        IReadOnlyList<CodePage> all = CodePage.All;
        IReadOnlyList<string> words = Declared("codepage").AllowedValues!;
        Assert.Equal(all.Count, words.Count);
        for (int i = 0; i < all.Count; i++)
        {
            Assert.Equal(all[i].Number.ToString(System.Globalization.CultureInfo.InvariantCulture), words[i]);
        }
        Assert.DoesNotContain("310", words);
    }

    [Fact]
    public async Task TerminalOpenModel5AnnouncesItselfAndGetsThe132By27ScreenOnEraseWriteAlternate()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var host = new FakeTn3270Host(listener);
        Task running = host.RunAsync(TestContext.Current.CancellationToken);

        using var sessions = new InProcessSessionHost();
        var provider = Provider(sessions);
        var result = await provider.CallToolAsync("terminal_open",
            Args(("protocol", "tn3270"), ("host", "127.0.0.1"), ("port", port), ("model", "IBM-3278-5")),
            TestContext.Current.CancellationToken);
        Assert.NotEqual(true, result.IsError);
        Assert.Contains("as IBM-3278-5", Text(result), StringComparison.Ordinal);

        // What the host received in TERMINAL-TYPE IS, measured on the wire.
        Assert.Equal("IBM-3278-5-E", await host.TerminalType.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));

        // The bytes cross a real socket, so FlushAsync cannot see them: poll with a deadline.
        var session = sessions.GetSession(SessionId(result))!;
        var emulator = Assert.IsType<Ibm3270Emulator>(session.Emulator);
        (int Rows, int Columns) size = (0, 0);
        var clock = Stopwatch.StartNew();
        while (clock.Elapsed < TimeSpan.FromSeconds(10))
        {
            size = await session.RunOnSessionThreadAsync(() => (emulator.Display.Rows, emulator.Display.Columns));
            if (size.Rows == 27)
            {
                break;
            }
            await Task.Delay(20, TestContext.Current.CancellationToken);
        }

        Assert.Equal((27, 132), size);
        await sessions.CloseSessionAsync(SessionId(result));
        await running.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task TerminalOpenWithACodePageOpensTheTerminalOnIt()
    {
        using var sessions = new InProcessSessionHost(_ => new InMemoryConnection());
        var result = await Provider(sessions).CallToolAsync("terminal_open",
            Args(("protocol", "tn3270"), ("host", "test-host"), ("codepage", "277")),
            TestContext.Current.CancellationToken);

        Assert.NotEqual(true, result.IsError);
        var emulator = Assert.IsType<Ibm3270Emulator>(sessions.GetSession(SessionId(result))!.Emulator);
        Assert.Equal(2, emulator.Model);
        Assert.Equal(CodePage.Cp277, emulator.Display.CodePage);
    }

    [Fact]
    public async Task ACodePageAsANumberIsAcceptedToo()
    {
        using var sessions = new InProcessSessionHost(_ => new InMemoryConnection());
        var result = await Provider(sessions).CallToolAsync("terminal_open",
            Args(("protocol", "tn3270"), ("host", "test-host"), ("codepage", 1142)),
            TestContext.Current.CancellationToken);

        Assert.NotEqual(true, result.IsError);
        var emulator = Assert.IsType<Ibm3270Emulator>(sessions.GetSession(SessionId(result))!.Emulator);
        Assert.Equal(CodePage.Cp1142, emulator.Display.CodePage);
    }

    [Theory]
    [InlineData("model", "IBM-3278-6")]
    [InlineData("codepage", "999")]
    [InlineData("codepage", "310")]
    public async Task AWordOutsideTheListIsRefusedByNameAndNothingOpens(string argument, string word)
    {
        var opened = new List<ConnectionFactory.ConnectionParameters>();
        using var sessions = new InProcessSessionHost(p =>
        {
            opened.Add(p);
            return new InMemoryConnection();
        });
        var result = await Provider(sessions).CallToolAsync("terminal_open",
            Args(("protocol", "tn3270"), ("host", "test-host"), (argument, word)),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsError ?? false);
        Assert.False(CommandParameterValidation.TryCheckValue("terminal_open", Declared(argument), word, out var expected));
        Assert.Equal(expected, Text(result));
        Assert.Empty(opened);
    }

    [Theory]
    [InlineData("telnet", "model", "IBM-3278-5")]
    [InlineData("ssh", "codepage", "277")]
    [InlineData("serial", "model", "IBM-3278-2")]
    public async Task ModelAndCodePageAreRefusedOffTn3270(string protocol, string argument, string word)
    {
        var opened = new List<ConnectionFactory.ConnectionParameters>();
        using var sessions = new InProcessSessionHost(p =>
        {
            opened.Add(p);
            return new InMemoryConnection();
        });
        var args = protocol == "serial"
            ? Args(("protocol", protocol), ("port_name", "COM99"), (argument, word))
            : Args(("protocol", protocol), ("host", "test-host"), (argument, word));
        var result = await Provider(sessions).CallToolAsync("terminal_open", args, TestContext.Current.CancellationToken);

        Assert.True(result.IsError ?? false);
        Assert.Equal("terminal_open: '" + argument + "' only applies with protocol=tn3270", Text(result));
        Assert.Empty(opened);
    }

    [Fact]
    public async Task AModelThatContradictsTheEmulatorIsRefused()
    {
        using var sessions = new InProcessSessionHost(_ => new InMemoryConnection());
        var result = await Provider(sessions).CallToolAsync("terminal_open",
            Args(("protocol", "tn3270"), ("host", "test-host"), ("model", "IBM-3278-5"), ("emulator", "IBM-3278-3")),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsError ?? false);
        Assert.Contains("'model' is IBM-3278-5 but 'emulator' is IBM-3278-3", Text(result), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ConnSaveStoresTheModelItsScreenAndTheCodePage()
    {
        var manager = new ConfigurationManager(_tempConfig);
        var registry = new CommandRegistry();
        ConnectionCommands.RegisterAll(registry, manager);
        using var session = new TerminalSession(new RetroTerm.Core.Terminal.Emulators.VT100Emulator(80, 24), "test");

        var created = await registry.ExecuteAsync("CONNSAVE", session, new CommandArgs()
            .Set("name", "tk5").Set("protocol", "tn3270").Set("host", "localhost").Set("port", "3270")
            .Set("model", "IBM-3278-5").Set("codepage", "277"), TestContext.Current.CancellationToken);
        Assert.True(created.Success, created.Error);

        var saved = ConnectionCommands.FindByName(manager, "tk5")!;
        Assert.Equal("IBM-3278-5", saved.EmulatorType);
        Assert.Equal((132, 27), (saved.Width, saved.Height));
        Assert.Equal(277, saved.CodePage);

        // An update changes only what is given: the model, and with it the screen.
        var updated = await registry.ExecuteAsync("CONNSAVE", session, new CommandArgs()
            .Set("name", "tk5").Set("model", "IBM-3278-4"), TestContext.Current.CancellationToken);
        Assert.True(updated.Success, updated.Error);
        Assert.Equal("IBM-3278-4", saved.EmulatorType);
        Assert.Equal((80, 43), (saved.Width, saved.Height));
        Assert.Equal(277, saved.CodePage);

        var shown = await registry.ExecuteAsync("CONNSHOW", session, new CommandArgs().Set("name", "tk5"), TestContext.Current.CancellationToken);
        Assert.Contains("code page: 277", shown.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ConnectWithADifferentModelThanTheTabsIsRefusedByName()
    {
        var manager = new ConfigurationManager(_tempConfig);
        var registry = new CommandRegistry();
        var opened = new List<ConnectionFactory.ConnectionParameters>();
        ConnectionCommands.RegisterAll(registry, manager, p =>
        {
            opened.Add(p);
            return new InMemoryConnection();
        });
        using var session = new TerminalSession(new Ibm3270Emulator(2), "test");

        var refused = await registry.ExecuteAsync("CONNECT", session, new CommandArgs()
            .Set("protocol", "tn3270").Set("host", "test-host").Set("model", "IBM-3278-5"), TestContext.Current.CancellationToken);
        Assert.False(refused.Success);
        Assert.Contains("'model' is IBM-3278-5 but this tab's terminal is IBM-3278-2-E", refused.Error, StringComparison.Ordinal);
        Assert.Empty(opened);

        var connected = await registry.ExecuteAsync("CONNECT", session, new CommandArgs()
            .Set("protocol", "tn3270").Set("host", "test-host").Set("model", "IBM-3278-2").Set("codepage", "277"),
            TestContext.Current.CancellationToken);
        Assert.True(connected.Success, connected.Error);
        Assert.Single(opened);
        Assert.Equal(CodePage.Cp277, ((Ibm3270Emulator)session.Emulator).Display.CodePage);
    }

    /// <summary>
    /// A TN3270 host on a loopback socket: asks for the terminal type, reads the answer, agrees
    /// EOR and BINARY both ways (the host side of the TK5 handshake, TN3270-NEGOTIATION.md
    /// section 3), then sends one Erase/Write Alternate record.
    /// </summary>
    private sealed class FakeTn3270Host
    {
        private readonly TcpListener _listener;

        private readonly TaskCompletionSource<string> _terminalType = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public FakeTn3270Host(TcpListener listener) => _listener = listener;

        // The name the client sent in TERMINAL-TYPE IS, as soon as it has arrived.
        public Task<string> TerminalType => _terminalType.Task;

        public async Task RunAsync(CancellationToken cancellationToken)
        {
            using TcpClient client = await _listener.AcceptTcpClientAsync(cancellationToken);
            NetworkStream stream = client.GetStream();

            // IAC DO TERMINAL-TYPE, IAC SB TERMINAL-TYPE SEND IAC SE.
            await stream.WriteAsync(new byte[] { 0xFF, 0xFD, 0x18, 0xFF, 0xFA, 0x18, 0x01, 0xFF, 0xF0 }, cancellationToken);

            // Read until IAC SB TERMINAL-TYPE IS <name> IAC SE has arrived whole.
            var received = new List<byte>();
            var buffer = new byte[256];
            string? name = null;
            while (name == null)
            {
                int count = await stream.ReadAsync(buffer, cancellationToken);
                if (count == 0)
                {
                    throw new IOException("the client closed before it sent its terminal type");
                }
                for (int i = 0; i < count; i++)
                {
                    received.Add(buffer[i]);
                }
                name = FindTerminalType(received);
            }
            _terminalType.TrySetResult(name);

            // WILL EOR, DO EOR, WILL BINARY, DO BINARY, then Erase/Write Alternate with WCC
            // keyboard restore, SBA to 0, an "H", and IAC EOR.
            await stream.WriteAsync(new byte[]
            {
                0xFF, 0xFB, 0x19, 0xFF, 0xFD, 0x19, 0xFF, 0xFB, 0x00, 0xFF, 0xFD, 0x00,
                Command3270.EraseWriteAlternate, 0x02, Order3270.SetBufferAddress, 0x40, 0x40, 0xC8, 0xFF, 0xEF,
            }, cancellationToken);

            // Keep the line open until the test closes the session.
            while (await stream.ReadAsync(buffer, cancellationToken) > 0)
            {
            }
        }

        private static string? FindTerminalType(List<byte> bytes)
        {
            for (int i = 0; i + 3 < bytes.Count; i++)
            {
                if (bytes[i] == 0xFF && bytes[i + 1] == 0xFA && bytes[i + 2] == 0x18 && bytes[i + 3] == 0x00)
                {
                    for (int j = i + 4; j + 1 < bytes.Count; j++)
                    {
                        if (bytes[j] == 0xFF && bytes[j + 1] == 0xF0)
                        {
                            return Encoding.ASCII.GetString(bytes.ToArray(), i + 4, j - (i + 4));
                        }
                    }
                }
            }
            return null;
        }
    }
}
