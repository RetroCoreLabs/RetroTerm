using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using RetroTerm.Core.Commands;
using RetroTerm.Core.Commands.Builtin;
using RetroTerm.Core.Session;
using RetroTerm.Core.Terminal.Emulators;
using RetroTerm.Tests.TDV;
using Xunit;

namespace RetroTerm.Tests.Commands;

/// <summary>
/// Tests for the command registry (P2.5) and the generated help (P2.6).
/// Done-criterion of the "one class + one registration" rule (CommandRegistry.cs and
/// docs\MCP-AND-SCRIPTING.md section 4): a dummy command registered in one line
/// is callable through the generic dispatch path, and its generated help shows
/// description, params and example without any help-specific code.
/// </summary>
public class CommandRegistryTests : IDisposable
{
    private readonly VT100Emulator _emulator;
    private readonly TerminalSession _session;
    private readonly InMemoryConnection _connection;
    private readonly CommandRegistry _registry;

    public CommandRegistryTests()
    {
        _emulator = new VT100Emulator(80, 24);
        _session = new TerminalSession(_emulator, "CmdTest");
        _connection = new InMemoryConnection();
        _registry = new CommandRegistry();
        BuiltinCommands.RegisterAll(_registry);
    }

    public void Dispose()
    {
        _session.Dispose();
    }

    /// <summary>
    /// A minimal well-documented command for extensibility tests.
    /// </summary>
    private sealed class DummyCommand : ISessionCommand
    {
        public string Name => "DUMMY";
        public string Summary => "A dummy command that echoes its input";
        public string Example => "DUMMY text=\"hi\"";
        public IReadOnlyList<CommandParameter> Parameters { get; } = new[]
        {
            new CommandParameter("text", CommandParameterType.String, true, null, "Text to echo")
        };
        public Task<CommandResult> ExecuteAsync(TerminalSession session, CommandArgs args, CancellationToken ct)
            => Task.FromResult(CommandResult.Ok("echo: " + args.GetString("text")));
    }

    /// <summary>
    /// A command with missing docs — must fail registration.
    /// </summary>
    private sealed class UndocumentedCommand : ISessionCommand
    {
        public string Name => "BAD";
        public string Summary => "";
        public string Example => "BAD";
        public IReadOnlyList<CommandParameter> Parameters { get; } = Array.Empty<CommandParameter>();
        public Task<CommandResult> ExecuteAsync(TerminalSession session, CommandArgs args, CancellationToken ct)
            => Task.FromResult(CommandResult.Ok());
    }

    // ─────────────────────────────────────────────────────────────
    // Registry + dispatch
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task DummyCommand_RegisteredInOneLine_IsCallableViaGenericDispatch()
    {
        _registry.Register(new DummyCommand()); // the one line

        var result = await _registry.ExecuteAsync("dummy", _session,
            new CommandArgs().Set("text", "hi"));

        Assert.True(result.Success);
        Assert.Equal("echo: hi", result.Output);
    }

    [Fact]
    public void Register_RejectsMissingDocs()
    {
        Assert.Throws<ArgumentException>(() => _registry.Register(new UndocumentedCommand()));
    }

    [Fact]
    public void Register_RejectsDuplicateName()
    {
        _registry.Register(new DummyCommand());
        Assert.Throws<ArgumentException>(() => _registry.Register(new DummyCommand()));
    }

    [Fact]
    public async Task Execute_UnknownCommand_FailsWithKnownCommandList()
    {
        var result = await _registry.ExecuteAsync("NOSUCH", _session, CommandArgs.Empty);

        Assert.False(result.Success);
        Assert.Contains("Unknown command", result.Error);
        Assert.Contains("SEND", result.Error); // the list names what IS available
    }

    [Fact]
    public async Task Execute_MissingRequiredParameter_FailsNamingTheParameter()
    {
        var result = await _registry.ExecuteAsync("SEND", _session, CommandArgs.Empty);

        Assert.False(result.Success);
        Assert.Contains("text", result.Error);
    }

    // ─────────────────────────────────────────────────────────────
    // Generated help (P2.6)
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void Help_ForDummyCommand_ShowsDocsWithZeroHelpSpecificCode()
    {
        _registry.Register(new DummyCommand());

        var help = CommandHelpGenerator.GenerateFor(_registry, "DUMMY");

        Assert.Contains("DUMMY", help);
        Assert.Contains("echoes its input", help);       // summary
        Assert.Contains("text", help);                   // parameter
        Assert.Contains("required", help);               // required marker
        Assert.Contains("DUMMY text=\"hi\"", help);      // example
    }

    [Fact]
    public void Help_Overview_ListsEveryRegisteredCommand()
    {
        var overview = CommandHelpGenerator.GenerateOverview(_registry);

        Assert.Contains("SEND", overview);
        Assert.Contains("SENDRAW", overview);
        Assert.Contains("WAITFOR", overview);
        Assert.Contains("WAITIDLE", overview);
        Assert.Contains("READSCREEN", overview);
        Assert.Contains("STATUS", overview);
        Assert.Contains("SLEEP", overview);
    }

    [Fact]
    public void Help_UnknownName_ReturnsOverviewNotEmpty()
    {
        var help = CommandHelpGenerator.GenerateFor(_registry, "NOSUCH");

        Assert.Contains("Unknown command", help);
        Assert.Contains("SEND", help);
    }

    [Fact]
    public void Help_Overview_IncludesScriptLanguageLevel()
    {
        // The console's HELP must list the same language things the editor's panel
        // shows: labels/jumps, variables, IF, flags and string escapes.
        var overview = CommandHelpGenerator.GenerateOverview(_registry);

        Assert.Contains("LABEL", overview);
        Assert.Contains("GOSUB", overview);
        Assert.Contains("IF", overview);
        Assert.Contains("ontimeout=", overview);
        Assert.Contains("into=", overview);
        Assert.Contains("escapes", overview);
    }

    [Fact]
    public void Help_ForLanguageWord_ReturnsItsEntry()
    {
        var gosub = CommandHelpGenerator.GenerateFor(_registry, "GOSUB");
        Assert.Contains("RETURN", gosub);
        Assert.Contains("Example", gosub);

        // Forgiving lookup: no trailing '=' needed.
        var ontimeout = CommandHelpGenerator.GenerateFor(_registry, "ontimeout");
        Assert.Contains("BRANCHES", ontimeout);
    }

    // ─────────────────────────────────────────────────────────────
    // Built-in commands against a live session
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Send_SendsExactText_NothingAppended()
    {
        await _session.ConnectAsync(_connection);

        var result = await _registry.ExecuteAsync("SEND", _session,
            new CommandArgs().Set("text", "LIST-FILES"));

        Assert.True(result.Success);
        Assert.Equal("LIST-FILES", _connection.GetLastSentAsString());
    }

    [Fact]
    public async Task Send_ControlCharsInText_SentVerbatim()
    {
        await _session.ConnectAsync(_connection);

        // A CR is part of the TEXT (scripts write it as \r; MCP sends it in JSON) —
        // the command itself never adds or removes anything.
        var result = await _registry.ExecuteAsync("SEND", _session,
            new CommandArgs().Set("text", "abc\r"));

        Assert.True(result.Success);
        Assert.Equal("abc\r", _connection.GetLastSentAsString());
    }

    [Fact]
    public async Task SendRaw_EscKeyword_SendsSingleEscByte()
    {
        await _session.ConnectAsync(_connection);

        var result = await _registry.ExecuteAsync("SENDRAW", _session,
            new CommandArgs().Set("bytes", "ESC"));

        Assert.True(result.Success);
        var sent = _connection.GetSentData();
        Assert.Equal(new byte[] { 0x1B }, sent[sent.Count - 1]);
    }

    [Fact]
    public async Task SendRaw_HexPairs_SendsBytes()
    {
        await _session.ConnectAsync(_connection);

        var result = await _registry.ExecuteAsync("SENDRAW", _session,
            new CommandArgs().Set("bytes", "1B0D"));

        Assert.True(result.Success);
        var sent = _connection.GetSentData();
        Assert.Equal(new byte[] { 0x1B, 0x0D }, sent[sent.Count - 1]);
    }

    [Fact]
    public async Task SendRaw_InvalidHex_FailsWithClearError()
    {
        await _session.ConnectAsync(_connection);

        var result = await _registry.ExecuteAsync("SENDRAW", _session,
            new CommandArgs().Set("bytes", "XYZ"));

        Assert.False(result.Success);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public async Task WaitFor_MatchesScreenAndReportsElapsed()
    {
        await _session.ConnectAsync(_connection);
        _connection.SimulateReceive(Encoding.UTF8.GetBytes("ENTER"));

        var result = await _registry.ExecuteAsync("WAITFOR", _session,
            new CommandArgs().Set("pattern", "ENTER").Set("timeout", "5000"));

        Assert.True(result.Success);
        Assert.Contains("ENTER", result.Output);
        Assert.True(result.Elapsed >= TimeSpan.Zero);
    }

    [Fact]
    public async Task WaitFor_Timeout_FailsButStillCarriesScreen()
    {
        await _session.ConnectAsync(_connection);
        _connection.SimulateReceive(Encoding.UTF8.GetBytes("partial answer"));

        var result = await _registry.ExecuteAsync("WAITFOR", _session,
            new CommandArgs().Set("pattern", "NEVER").Set("timeout", "150"));

        Assert.False(result.Success);
        Assert.Contains("Timeout", result.Error);
        Assert.Contains("partial answer", result.Output); // rule 4
        Assert.True(result.Elapsed >= TimeSpan.FromMilliseconds(150)); // rule 5
    }

    [Fact]
    public async Task ReadScreen_ReturnsTextAndCursor()
    {
        await _session.ConnectAsync(_connection);
        _connection.SimulateReceive(Encoding.UTF8.GetBytes("hello"));

        var result = await _registry.ExecuteAsync("READSCREEN", _session, CommandArgs.Empty);

        Assert.True(result.Success);
        Assert.Contains("hello", result.Output);
        Assert.Contains("[cursor 0,5]", result.Output);
    }

    [Fact]
    public async Task Status_ReportsConnectionAndCounters()
    {
        await _session.ConnectAsync(_connection);
        _connection.SimulateReceive(Encoding.UTF8.GetBytes("abc"));
        await _session.FlushAsync();

        var result = await _registry.ExecuteAsync("STATUS", _session, CommandArgs.Empty);

        Assert.True(result.Success);
        Assert.Contains("connected: yes", result.Output);
        Assert.Contains("bytes received: 3", result.Output);
        Assert.Contains("time since last byte:", result.Output);
        Assert.Contains("80x24", result.Output);
    }

    [Fact]
    public async Task ReadNew_ReturnsOnlyNewScrollbackSinceLastCall()
    {
        await _session.ConnectAsync(_connection);

        // Fill 30 lines: 6 scroll out on a 24-row screen.
        var sb = new StringBuilder();
        for (int i = 0; i < 30; i++)
        {
            sb.Append("line").Append(i).Append("\r\n");
        }
        _connection.SimulateReceive(Encoding.UTF8.GetBytes(sb.ToString()));

        var first = await _registry.ExecuteAsync("READNEW", _session, CommandArgs.Empty);
        Assert.True(first.Success);
        Assert.Contains("line0", first.Output);            // scrolled-out line captured
        Assert.Contains("current screen", first.Output);

        // Nothing new arrived: second call must not repeat the old scrollback.
        var second = await _registry.ExecuteAsync("READNEW", _session, CommandArgs.Empty);
        Assert.True(second.Success);
        Assert.DoesNotContain("line0\n", second.Output);

        // More output scrolls more lines out — only those appear on the third call.
        var sb2 = new StringBuilder();
        for (int i = 30; i < 60; i++)
        {
            sb2.Append("line").Append(i).Append("\r\n");
        }
        _connection.SimulateReceive(Encoding.UTF8.GetBytes(sb2.ToString()));

        var third = await _registry.ExecuteAsync("READNEW", _session, CommandArgs.Empty);
        Assert.True(third.Success);
        Assert.DoesNotContain("line0\n", third.Output);
        Assert.Contains("line30", third.Output);
    }

    [Fact]
    public async Task CommandThatThrows_ComesBackAsFailedResult_NotException()
    {
        // SEND with no connection throws InvalidOperationException inside the command;
        // dispatch must convert that into a reportable failure.
        var result = await _registry.ExecuteAsync("SEND", _session,
            new CommandArgs().Set("text", "x"));

        Assert.False(result.Success);
        Assert.Contains("threw", result.Error);
    }
}
