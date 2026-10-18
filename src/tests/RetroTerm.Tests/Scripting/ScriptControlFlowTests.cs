using System;
using System.Text;
using System.Threading.Tasks;
using RetroTerm.Core.Commands;
using RetroTerm.Core.Commands.Builtin;
using RetroTerm.Core.Scripting;
using RetroTerm.Core.Session;
using RetroTerm.Core.Terminal.Emulators;
using RetroTerm.Tests.TDV;
using Xunit;

namespace RetroTerm.Tests.Scripting;

/// <summary>
/// Tests for script control flow: LABEL / GOTO (no return) / GOSUB+RETURN, and the
/// ontimeout=label branch on the WAIT verbs.
/// </summary>
public class ScriptControlFlowTests : IDisposable
{
    private readonly VT100Emulator _emulator;
    private readonly TerminalSession _session;
    private readonly InMemoryConnection _connection;
    private readonly ScriptParser _parser;
    private readonly ScriptRunner _runner;

    public ScriptControlFlowTests()
    {
        _emulator = new VT100Emulator(80, 24);
        _session = new TerminalSession(_emulator, "FlowTest");
        _connection = new InMemoryConnection();
        var registry = new CommandRegistry();
        BuiltinCommands.RegisterAll(registry);
        _parser = new ScriptParser(registry);
        _runner = new ScriptRunner(registry);
    }

    public void Dispose()
    {
        _session.Dispose();
    }

    // ─────────────────────────────────────────────────────────────
    // Parser validation
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void Parse_GotoUnknownLabel_IsAParseError()
    {
        var script = _parser.Parse("GOTO nowhere");
        Assert.False(script.IsValid);
        Assert.Contains("unknown label", script.Errors[0].Message);
    }

    [Fact]
    public void Parse_DuplicateLabel_IsAParseError()
    {
        var script = _parser.Parse("LABEL a\nLABEL a");
        Assert.False(script.IsValid);
        Assert.Contains("Duplicate label", script.Errors[0].Message);
    }

    [Fact]
    public void Parse_OnTimeoutUnknownLabel_IsAParseError()
    {
        var script = _parser.Parse("WAITFOR \"x\" timeout=100 ontimeout=missing");
        Assert.False(script.IsValid);
        Assert.Contains("ontimeout", script.Errors[0].Message);
    }

    [Fact]
    public void Parse_LabelWithoutName_IsAParseError()
    {
        var script = _parser.Parse("LABEL");
        Assert.False(script.IsValid);
        Assert.Contains("label name", script.Errors[0].Message);
    }

    [Fact]
    public void Parse_ControlFlow_ProducesRightKinds()
    {
        var script = _parser.Parse("LABEL top\nGOTO top\nGOSUB top\nRETURN");
        Assert.True(script.IsValid);
        Assert.Equal(ScriptStepKind.Label, script.Steps[0].Kind);
        Assert.Equal(ScriptStepKind.Goto, script.Steps[1].Kind);
        Assert.Equal(ScriptStepKind.Gosub, script.Steps[2].Kind);
        Assert.Equal(ScriptStepKind.Return, script.Steps[3].Kind);
        Assert.Equal(0, script.Labels["top"]);
    }

    // ─────────────────────────────────────────────────────────────
    // Runner behavior
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Goto_SkipsSteps_NoReturn()
    {
        await _session.ConnectAsync(_connection, TestContext.Current.CancellationToken);

        var script = _parser.Parse(
            "GOTO past\n" +
            "SEND \"SKIPPED\"\n" +
            "LABEL past\n" +
            "SEND \"REACHED\"\n");
        Assert.True(script.IsValid);

        var result = await _runner.RunAsync(script, _session, TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        var sent = _connection.GetSentData();
        Assert.Single(sent);
        Assert.Equal("REACHED", Encoding.UTF8.GetString(sent[0]));
    }

    [Fact]
    public async Task Gosub_RunsSubroutine_ReturnComesBack()
    {
        await _session.ConnectAsync(_connection, TestContext.Current.CancellationToken);

        var script = _parser.Parse(
            "GOSUB sub\n" +
            "SEND \"AFTER\"\n" +
            "GOTO end\n" +
            "LABEL sub\n" +
            "SEND \"INSIDE\"\n" +
            "RETURN\n" +
            "LABEL end\n");
        Assert.True(script.IsValid);

        var result = await _runner.RunAsync(script, _session, TestContext.Current.CancellationToken);

        Assert.True(result.Success, result.Error);
        var sent = _connection.GetSentData();
        Assert.Equal(2, sent.Count);
        Assert.Equal("INSIDE", Encoding.UTF8.GetString(sent[0]));
        Assert.Equal("AFTER", Encoding.UTF8.GetString(sent[1]));
    }

    [Fact]
    public async Task NestedGosub_ReturnsInOrder()
    {
        await _session.ConnectAsync(_connection, TestContext.Current.CancellationToken);

        var script = _parser.Parse(
            "GOSUB outer\n" +
            "SEND \"MAIN\"\n" +
            "GOTO end\n" +
            "LABEL outer\n" +
            "GOSUB inner\n" +
            "SEND \"OUTER\"\n" +
            "RETURN\n" +
            "LABEL inner\n" +
            "SEND \"INNER\"\n" +
            "RETURN\n" +
            "LABEL end\n");
        Assert.True(script.IsValid);

        var result = await _runner.RunAsync(script, _session, TestContext.Current.CancellationToken);

        Assert.True(result.Success, result.Error);
        var sent = _connection.GetSentData();
        Assert.Equal(3, sent.Count);
        Assert.Equal("INNER", Encoding.UTF8.GetString(sent[0]));
        Assert.Equal("OUTER", Encoding.UTF8.GetString(sent[1]));
        Assert.Equal("MAIN", Encoding.UTF8.GetString(sent[2]));
    }

    [Fact]
    public async Task Return_WithoutGosub_FailsNamingTheLine()
    {
        await _session.ConnectAsync(_connection, TestContext.Current.CancellationToken);

        var script = _parser.Parse("SEND \"a\"\nRETURN\n");
        Assert.True(script.IsValid);

        var result = await _runner.RunAsync(script, _session, TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Contains("RETURN at line 2 without a matching GOSUB", result.Error);
    }

    [Fact]
    public async Task OnTimeout_JumpsInsteadOfFailing()
    {
        await _session.ConnectAsync(_connection, TestContext.Current.CancellationToken);

        // The prompt never appears: the WAITFOR times out and must BRANCH to the
        // recovery label instead of failing the script.
        var script = _parser.Parse(
            "WAITFOR \"NEVER\" timeout=100 ontimeout=recover\n" +
            "SEND \"NOT-REACHED\"\n" +
            "GOTO end\n" +
            "LABEL recover\n" +
            "SEND \"RECOVERED\"\n" +
            "LABEL end\n");
        Assert.True(script.IsValid);

        var result = await _runner.RunAsync(script, _session, TestContext.Current.CancellationToken);

        Assert.True(result.Success, result.Error);
        var sent = _connection.GetSentData();
        Assert.Single(sent);
        Assert.Equal("RECOVERED", Encoding.UTF8.GetString(sent[0]));
    }

    [Fact]
    public async Task OnTimeout_NotTakenWhenPatternMatches()
    {
        await _session.ConnectAsync(_connection, TestContext.Current.CancellationToken);
        _connection.SimulateReceive(Encoding.UTF8.GetBytes("READY"));

        var script = _parser.Parse(
            "WAITFOR \"READY\" timeout=5000 ontimeout=recover\n" +
            "SEND \"NORMAL\"\n" +
            "GOTO end\n" +
            "LABEL recover\n" +
            "SEND \"WRONG\"\n" +
            "LABEL end\n");
        Assert.True(script.IsValid);

        var result = await _runner.RunAsync(script, _session, TestContext.Current.CancellationToken);

        Assert.True(result.Success, result.Error);
        Assert.Equal("NORMAL", _connection.GetLastSentAsString());
    }

    [Fact]
    public async Task OnTimeout_RetryLoop_EscWakeup()
    {
        await _session.ConnectAsync(_connection, TestContext.Current.CancellationToken);

        // The classic SINTRAN wake-up: send ESC, wait briefly for the prompt, and on
        // timeout go back and send ESC again. The host answers after the second ESC.
        var script = _parser.Parse(
            "LABEL wake\n" +
            "SENDRAW ESC\n" +
            "WAITFOR \"ENTER\" where=screen timeout=200 ontimeout=wake\n" +
            "SEND \"USER\"\n");
        Assert.True(script.IsValid);

        var runTask = _runner.RunAsync(script, _session, TestContext.Current.CancellationToken);

        // Stay silent through the first ESC; answer after the second.
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (_connection.GetSentData().Count < 2 && DateTime.UtcNow < deadline)
        {
            await Task.Delay(20, TestContext.Current.CancellationToken);
        }
        Assert.True(_connection.GetSentData().Count >= 2, "second ESC never sent");
        _connection.SimulateReceive(Encoding.UTF8.GetBytes("SINTRAN\r\nENTER "));

        var result = await runTask;

        Assert.True(result.Success, result.Error);
        Assert.Equal("USER", _connection.GetLastSentAsString());
        // At least two wake attempts happened before the login was sent.
        var sent = _connection.GetSentData();
        Assert.Equal(0x1B, sent[0][0]);
        Assert.Equal(0x1B, sent[1][0]);
    }

    [Fact]
    public async Task RunawayGotoLoop_IsAborted()
    {
        await _session.ConnectAsync(_connection, TestContext.Current.CancellationToken);

        var script = _parser.Parse("LABEL spin\nGOTO spin\n");
        Assert.True(script.IsValid);

        var result = await _runner.RunAsync(script, _session, TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Contains("runaway", result.Error);
    }
}
