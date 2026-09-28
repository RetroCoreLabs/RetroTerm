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
/// Tests for script variables: SET, $expansion, into= capture (incl. regex group 1),
/// and IF/ELSE/ENDIF.
/// </summary>
public class ScriptVariableTests : IDisposable
{
    private readonly VT100Emulator _emulator;
    private readonly TerminalSession _session;
    private readonly InMemoryConnection _connection;
    private readonly ScriptParser _parser;
    private readonly ScriptRunner _runner;

    public ScriptVariableTests()
    {
        _emulator = new VT100Emulator(80, 24);
        _session = new TerminalSession(_emulator, "VarTest");
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
    // ScriptContext expansion
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void Expand_NameAndBracedAndEscape()
    {
        var ctx = new ScriptContext();
        ctx.Set("user", "GUEST");
        ctx.Set("n", "5");

        Assert.Equal("LOGIN GUEST", ctx.Expand("LOGIN $user"));
        Assert.Equal("GUEST5x", ctx.Expand("${user}${n}x"));
        Assert.Equal("costs $5", ctx.Expand("costs $$5"));
        Assert.Equal("", ctx.Expand("$unknown"));       // shell-style: empty
        Assert.Equal("tail $", ctx.Expand("tail $"));   // trailing $ literal
    }

    // ─────────────────────────────────────────────────────────────
    // SET + expansion in commands
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Set_ThenSend_ExpandsVariable()
    {
        await _session.ConnectAsync(_connection);

        var script = _parser.Parse(
            "SET user \"SYSTEM\"\n" +
            "SEND \"LOGIN $user\"\n");
        Assert.True(script.IsValid);

        var result = await _runner.RunAsync(script, _session);

        Assert.True(result.Success, result.Error);
        Assert.Equal("LOGIN SYSTEM", _connection.GetLastSentAsString()); // SEND appends nothing
    }

    [Fact]
    public async Task CallerContext_PreSetVariables_AreVisible()
    {
        await _session.ConnectAsync(_connection);

        var context = new ScriptContext();
        context.Set("password", "SECRET");

        var script = _parser.Parse("SEND \"$password\"\n");
        var result = await _runner.RunAsync(script, _session, context: context);

        Assert.True(result.Success);
        Assert.Equal("SECRET", _connection.GetLastSentAsString());
    }

    // ─────────────────────────────────────────────────────────────
    // into= capture
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task WaitFor_RegexGroup_CapturesIntoVariable()
    {
        await _session.ConnectAsync(_connection);
        _connection.SimulateReceive(Encoding.UTF8.GetBytes("SINTRAN III VERSION K RELEASE 4"));

        var context = new ScriptContext();
        var script = _parser.Parse(
            "WAITFOR \"VERSION ([A-Z]) RELEASE\" regex=true where=screen timeout=5000 into=ver\n" +
            "SEND \"GOT $ver\"\n");
        Assert.True(script.IsValid);

        var result = await _runner.RunAsync(script, _session, context: context);

        Assert.True(result.Success, result.Error);
        Assert.Equal("K", context.Get("ver"));               // group 1, not the whole match
        Assert.Equal("GOT K", _connection.GetLastSentAsString());
    }

    [Fact]
    public async Task ReadScreen_Into_CapturesScreenText()
    {
        await _session.ConnectAsync(_connection);
        _connection.SimulateReceive(Encoding.UTF8.GetBytes("hello world"));

        var context = new ScriptContext();
        var script = _parser.Parse("READSCREEN into=scr\n");
        var result = await _runner.RunAsync(script, _session, context: context);

        Assert.True(result.Success);
        Assert.Contains("hello world", context.Get("scr"));
    }

    // ─────────────────────────────────────────────────────────────
    // IF / ELSE / ENDIF
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task If_TrueBranch_RunsThenSkipsElse()
    {
        await _session.ConnectAsync(_connection);

        var script = _parser.Parse(
            "SET mode \"PROD\"\n" +
            "IF $mode == \"PROD\"\n" +
            "SEND \"IS-PROD\"\n" +
            "ELSE\n" +
            "SEND \"IS-TEST\"\n" +
            "ENDIF\n" +
            "SEND \"AFTER\"\n");
        Assert.True(script.IsValid);

        var result = await _runner.RunAsync(script, _session);

        Assert.True(result.Success, result.Error);
        var sent = _connection.GetSentData();
        Assert.Equal(2, sent.Count);
        Assert.Equal("IS-PROD", Encoding.UTF8.GetString(sent[0]));
        Assert.Equal("AFTER", Encoding.UTF8.GetString(sent[1]));
    }

    [Fact]
    public async Task If_FalseBranch_RunsElse()
    {
        await _session.ConnectAsync(_connection);

        var script = _parser.Parse(
            "SET mode \"TEST\"\n" +
            "IF $mode == \"PROD\"\n" +
            "SEND \"IS-PROD\"\n" +
            "ELSE\n" +
            "SEND \"IS-TEST\"\n" +
            "ENDIF\n");
        Assert.True(script.IsValid);

        var result = await _runner.RunAsync(script, _session);

        Assert.True(result.Success, result.Error);
        Assert.Equal("IS-TEST", _connection.GetLastSentAsString());
        Assert.Single(_connection.GetSentData());
    }

    [Fact]
    public async Task If_WithoutElse_FalseSkipsBlock()
    {
        await _session.ConnectAsync(_connection);

        var script = _parser.Parse(
            "IF \"a\" == \"b\"\n" +
            "SEND \"NEVER\"\n" +
            "ENDIF\n" +
            "SEND \"ALWAYS\"\n");
        Assert.True(script.IsValid);

        var result = await _runner.RunAsync(script, _session);

        Assert.True(result.Success);
        Assert.Single(_connection.GetSentData());
        Assert.Equal("ALWAYS", _connection.GetLastSentAsString());
    }

    [Fact]
    public async Task If_ContainsAndMatches_Operators()
    {
        await _session.ConnectAsync(_connection);

        var script = _parser.Parse(
            "SET banner \"SINTRAN III VSX\"\n" +
            "IF $banner CONTAINS \"VSX\"\n" +
            "SEND \"HAS-VSX\"\n" +
            "ENDIF\n" +
            "IF $banner MATCHES \"SINTRAN [IV]+\"\n" +
            "SEND \"IS-SINTRAN\"\n" +
            "ENDIF\n");
        Assert.True(script.IsValid);

        var result = await _runner.RunAsync(script, _session);

        Assert.True(result.Success, result.Error);
        Assert.Equal(2, _connection.GetSentData().Count);
    }

    [Fact]
    public async Task NestedIf_Works()
    {
        await _session.ConnectAsync(_connection);

        var script = _parser.Parse(
            "SET a \"1\"\nSET b \"2\"\n" +
            "IF $a == \"1\"\n" +
            "IF $b == \"2\"\n" +
            "SEND \"BOTH\"\n" +
            "ENDIF\n" +
            "ENDIF\n");
        Assert.True(script.IsValid);

        var result = await _runner.RunAsync(script, _session);

        Assert.True(result.Success, result.Error);
        Assert.Equal("BOTH", _connection.GetLastSentAsString());
    }

    // ─────────────────────────────────────────────────────────────
    // Parse validation
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void Parse_IfWithoutEndif_IsAnError()
    {
        var script = _parser.Parse("IF \"a\" == \"a\"\nSEND \"x\"\n");
        Assert.False(script.IsValid);
        Assert.Contains("ENDIF", script.Errors[0].Message);
    }

    [Fact]
    public void Parse_ElseWithoutIf_IsAnError()
    {
        var script = _parser.Parse("ELSE");
        Assert.False(script.IsValid);
        Assert.Contains("ELSE without", script.Errors[0].Message);
    }

    [Fact]
    public void Parse_SecondElse_IsAnError()
    {
        var script = _parser.Parse("IF \"a\" == \"a\"\nELSE\nELSE\nENDIF\n");
        Assert.False(script.IsValid);
        Assert.Contains("Second ELSE", script.Errors[0].Message);
    }

    [Fact]
    public void Parse_BadIfOperator_IsAnError()
    {
        var script = _parser.Parse("IF \"a\" >= \"b\"\nENDIF\n");
        Assert.False(script.IsValid);
        Assert.Contains(">=", script.Errors[0].Message);
    }

    [Fact]
    public void Parse_SetNeedsNameAndValue()
    {
        var script = _parser.Parse("SET onlyname");
        Assert.False(script.IsValid);
        Assert.Contains("SET needs", script.Errors[0].Message);
    }
}
