using RetroTerm.Core.Commands;
using RetroTerm.Core.Commands.Builtin;
using RetroTerm.Core.Scripting;
using Xunit;

namespace RetroTerm.Tests.Scripting;

/// <summary>
/// Tests for the script DSL parser (P3.1). The parser is generic over the command
/// registry: verbs, parameters and required-checks all come from command metadata.
/// </summary>
public class ScriptParserTests
{
    private readonly CommandRegistry _registry;
    private readonly ScriptParser _parser;

    public ScriptParserTests()
    {
        _registry = new CommandRegistry();
        BuiltinCommands.RegisterAll(_registry);
        _parser = new ScriptParser(_registry);
    }

    [Fact]
    public void Parse_CommentsAndBlankLines_AreSkipped()
    {
        var script = _parser.Parse("# a comment\n\n   \nSEND \"hi\"\n# another\n");

        Assert.True(script.IsValid);
        Assert.Single(script.Steps);
        Assert.Equal("SEND", script.Steps[0].CommandName);
        Assert.Equal(4, script.Steps[0].LineNumber);
    }

    [Fact]
    public void Parse_PositionalArgument_MapsToFirstParameter()
    {
        var script = _parser.Parse("SEND \"LIST-FILES\"");

        Assert.True(script.IsValid);
        Assert.Equal("LIST-FILES", script.Steps[0].Args.GetString("text"));
    }

    [Fact]
    public void Parse_NamedArguments_AndQuotedValues()
    {
        var script = _parser.Parse("WAITFOR \"X-C:\" timeout=5000 where=screen regex=false");

        Assert.True(script.IsValid);
        var args = script.Steps[0].Args;
        Assert.Equal("X-C:", args.GetString("pattern"));
        Assert.Equal(5000, args.GetInt("timeout", 0));
        Assert.Equal("screen", args.GetString("where"));
        Assert.False(args.GetBool("regex", true));
    }

    [Fact]
    public void Parse_KeyEqualsQuotedValue()
    {
        var script = _parser.Parse("WAITFOR pattern=\"hello world\" regex=false");

        Assert.True(script.IsValid);
        Assert.Equal("hello world", script.Steps[0].Args.GetString("pattern"));
        Assert.False(script.Steps[0].Args.GetBool("regex", true));
    }

    // ─────────────────────────────────────────────────────────────
    // String escapes: \r \n \t \e \xHH \NNN — SEND appends nothing,
    // the line terminator is written into the string.
    // ─────────────────────────────────────────────────────────────

    // ─────────────────────────────────────────────────────────────
    // Parameter TYPE validation at parse time
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void Parse_IntParamWithGarbage_IsError()
    {
        // The real-world typo that motivated this: '/' instead of a space glued
        // ontimeout onto the timeout value and the script silently ran with the
        // DEFAULT timeout and no jump.
        var script = _parser.Parse("WAITFOR \"ENTER\" timeout=300/ontimeout=wake\nLABEL wake\n");

        Assert.False(script.IsValid);
        Assert.Contains("whole number", script.Errors[0].Message);
        Assert.Contains("300/ontimeout=wake", script.Errors[0].Message);
    }

    [Fact]
    public void Parse_BoolParamWithGarbage_IsError()
    {
        var script = _parser.Parse("WAITFOR \"x\" regex=maybe");

        Assert.False(script.IsValid);
        Assert.Contains("true/false", script.Errors[0].Message);
    }

    [Fact]
    public void Parse_IntParamWithVariable_IsAllowed()
    {
        // $vars expand at RUN time — the parser cannot check them.
        var script = _parser.Parse("SET t \"5000\"\nWAITFOR \"x\" timeout=$t");

        Assert.True(script.IsValid);
    }

    [Fact]
    public void Parse_ValidTypedParams_StillParse()
    {
        var script = _parser.Parse("WAITFOR \"x\" timeout=2000 regex=true ontimeout=w\nLABEL w\n");

        Assert.True(script.IsValid);
    }

    // ─────────────────────────────────────────────────────────────
    // Step-flag validation: ontimeout/into only where they mean something
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void Parse_OnTimeoutOnCommandThatCannotTimeOut_IsError()
    {
        var script = _parser.Parse("SEND \"x\" ontimeout=w\nLABEL w\n");

        Assert.False(script.IsValid);
        Assert.Contains("ontimeout= does nothing on SEND", script.Errors[0].Message);
    }

    [Fact]
    public void Parse_IntoOnCommandWithNothingToStore_IsError()
    {
        var script = _parser.Parse("SEND \"x\" into=v");

        Assert.False(script.IsValid);
        Assert.Contains("into= does nothing on SEND", script.Errors[0].Message);
    }

    [Fact]
    public void Parse_IntoOnCaptureCommands_IsAllowed()
    {
        var script = _parser.Parse(
            "WAITFOR \"V ([A-Z])\" regex=true into=a\n" +
            "WAITIDLE 100 into=b\n" +
            "READSCREEN into=c\n" +
            "STATUS into=d\n");

        Assert.True(script.IsValid);
    }

    [Fact]
    public void Parse_OptionalWithGarbageValue_IsError()
    {
        var script = _parser.Parse("WAITFOR \"x\" optional=maybe");

        Assert.False(script.IsValid);
        Assert.Contains("optional=", script.Errors[0].Message);
    }

    [Fact]
    public void Parse_ControlEscapes_Decode()
    {
        var script = _parser.Parse("SEND \"A\\r\\n\\tB\\e\"");

        Assert.True(script.IsValid);
        Assert.Equal("A\r\n\tB\x1B", script.Steps[0].Args.GetString("text"));
    }

    [Fact]
    public void Parse_HexEscape_Decodes()
    {
        var script = _parser.Parse("SEND \"\\x1B[2J\"");

        Assert.True(script.IsValid);
        Assert.Equal("\x1B[2J", script.Steps[0].Args.GetString("text"));
    }

    [Fact]
    public void Parse_OctalEscape_Decodes()
    {
        // \033 = ESC, \0 = NUL, \177 = DEL
        var script = _parser.Parse("SEND \"\\033[2J\\0\\177\"");

        Assert.True(script.IsValid);
        Assert.Equal("\x1B[2J\0\x7F", script.Steps[0].Args.GetString("text"));
    }

    [Fact]
    public void Parse_UnknownEscape_IsError()
    {
        var script = _parser.Parse("SEND \"a\\q\"");

        Assert.False(script.IsValid);
        Assert.Contains("escape", script.Errors[0].Message);
    }

    [Fact]
    public void Parse_BadHexEscape_IsError()
    {
        var script = _parser.Parse("SEND \"\\xZZ\"");

        Assert.False(script.IsValid);
        Assert.Contains("hex", script.Errors[0].Message);
    }

    [Fact]
    public void Parse_OctalOverflow_IsError()
    {
        var script = _parser.Parse("SEND \"\\777\"");

        Assert.False(script.IsValid);
        Assert.Contains("377", script.Errors[0].Message);
    }

    [Fact]
    public void Parse_QuoteEscapes()
    {
        var script = _parser.Parse("SEND \"say \\\"hi\\\" and a back\\\\slash\"");

        Assert.True(script.IsValid);
        Assert.Equal("say \"hi\" and a back\\slash", script.Steps[0].Args.GetString("text"));
    }

    [Fact]
    public void Parse_QuotedPositionalContainingEquals_IsNotSplitAsNamed()
    {
        var script = _parser.Parse("SEND \"SET-PARAM X=1\"");

        Assert.True(script.IsValid);
        Assert.Equal("SET-PARAM X=1", script.Steps[0].Args.GetString("text"));
    }

    [Fact]
    public void Parse_InlineComment_AfterStep()
    {
        var script = _parser.Parse("SENDRAW ESC   # wake the SINTRAN line");

        Assert.True(script.IsValid);
        Assert.Equal("ESC", script.Steps[0].Args.GetString("bytes"));
    }

    [Fact]
    public void Parse_OptionalFlag_IsStepLevelNotCommandParameter()
    {
        var script = _parser.Parse("SLEEP 100 optional=true");

        Assert.True(script.IsValid);
        Assert.True(script.Steps[0].Optional);
        Assert.False(script.Steps[0].Args.Contains("optional"));
    }

    [Fact]
    public void Parse_UnknownVerb_ErrorWithLineNumber()
    {
        var script = _parser.Parse("SEND \"ok\"\nFROBNICATE now\nSEND \"ok\"");

        Assert.False(script.IsValid);
        Assert.Single(script.Errors);
        Assert.Equal(2, script.Errors[0].LineNumber);
        Assert.Contains("FROBNICATE", script.Errors[0].Message);
        Assert.Equal(2, script.Steps.Count); // parsing continued past the error
    }

    [Fact]
    public void Parse_MissingRequiredParameter_IsAParseError()
    {
        var script = _parser.Parse("WAITFOR timeout=1000");

        Assert.False(script.IsValid);
        Assert.Contains("pattern", script.Errors[0].Message);
    }

    [Fact]
    public void Parse_UnknownParameter_IsAParseError()
    {
        var script = _parser.Parse("SEND \"x\" bogus=1");

        Assert.False(script.IsValid);
        Assert.Contains("bogus", script.Errors[0].Message);
    }

    [Fact]
    public void Parse_UnterminatedString_IsAParseError()
    {
        var script = _parser.Parse("SEND \"never closed");

        Assert.False(script.IsValid);
        Assert.Contains("Unterminated", script.Errors[0].Message);
    }

    [Fact]
    public void Parse_TooManyPositionals_IsAParseError()
    {
        var script = _parser.Parse("SLEEP 100 200");

        Assert.False(script.IsValid);
        Assert.Contains("at most", script.Errors[0].Message);
    }

    [Fact]
    public void Parse_CollectsAllErrors_NotJustTheFirst()
    {
        var script = _parser.Parse("BAD1\nBAD2\nSEND \"ok\"");

        Assert.Equal(2, script.Errors.Count);
        Assert.Equal(1, script.Errors[0].LineNumber);
        Assert.Equal(2, script.Errors[1].LineNumber);
        Assert.Single(script.Steps);
    }

    [Fact]
    public void Parse_NewCommandInRegistry_IsAutomaticallyAVerb()
    {
        // The parser must not hard-code the verb list: registering a command makes
        // it parseable with zero parser changes.
        _registry.Register(new HelpProbeCommand());

        var script = _parser.Parse("PROBE \"target\"");

        Assert.True(script.IsValid);
        Assert.Equal("PROBE", script.Steps[0].CommandName);
    }

    private sealed class HelpProbeCommand : ISessionCommand
    {
        public string Name => "PROBE";
        public string Summary => "Test-only probe command";
        public string Example => "PROBE \"x\"";
        public System.Collections.Generic.IReadOnlyList<CommandParameter> Parameters { get; } = new[]
        {
            new CommandParameter("target", CommandParameterType.String, true, null, "What to probe")
        };
        public System.Threading.Tasks.Task<CommandResult> ExecuteAsync(
            RetroTerm.Core.Session.TerminalSession session, CommandArgs args, System.Threading.CancellationToken ct)
            => System.Threading.Tasks.Task.FromResult(CommandResult.Ok());
    }
}
