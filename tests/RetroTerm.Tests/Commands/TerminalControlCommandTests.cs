using RetroTerm.Core.Commands;
using RetroTerm.Core.Commands.Builtin;
using Xunit;

namespace RetroTerm.Tests.Commands;

/// <summary>
/// Metadata tests for the terminal-control commands (CLEAR, ECHO, SNAPSHOT, LOGSTART, LOGSTOP).
/// </summary>
/// <remarks>
/// <para><b>Why a test that only reads metadata is worth having</b></para>
/// <see cref="CommandSurfaceEquivalenceTests"/> generates over the whole registry and proves the
/// script DSL and the MCP tool provider deliver the SAME value for one intended value. It reads
/// <see cref="CommandParameter.DecodeEscapes"/> to decide how to spell the MCP side, so it agrees
/// with whatever the flag currently says and passes either way. It cannot catch a flag that is
/// MISSING, because nothing tells it what the flag ought to be.
///
/// <para><b>What that cost, 31 August 2026</b></para>
/// ECHO writes through <c>TerminalSession.WriteToTerminal</c>, so its text is PARSED by the
/// emulator exactly like bytes arriving off the wire. The script tokenizer decodes escapes in
/// every quoted string as a property of the language, so from a .rts script
/// <c>ECHO "\x1bP1p...\x1b\x5c"</c> drew a real ReGIS picture. The same value over MCP arrived as
/// the literal characters backslash-x-1-b and printed as text, because the parameter was never
/// flagged. One command, two behaviours - the same shape of fault as SEND swallowing a carriage
/// return on one surface for weeks.
/// </remarks>
public class TerminalControlCommandTests
{
    /// <summary>
    /// Builds a registry holding just the terminal-control commands.
    /// </summary>
    /// <returns>
    /// A registry with CLEAR, ECHO, SNAPSHOT, LOGSTART and LOGSTOP registered.
    /// </returns>
    private static CommandRegistry BuildRegistry()
    {
        var registry = new CommandRegistry();
        TerminalControlCommands.RegisterAll(registry);
        return registry;
    }

    /// <summary>
    /// Finds a parameter by name without LINQ.
    /// </summary>
    /// <param name="command">
    /// The command to search.
    /// </param>
    /// <param name="name">
    /// Parameter name, matched case-insensitively.
    /// </param>
    /// <returns>
    /// The parameter, or null when the command has no such parameter.
    /// </returns>
    private static CommandParameter? FindParameter(ISessionCommand command, string name)
    {
        var parameters = command.Parameters;
        for (int i = 0; i < parameters.Count; i++)
        {
            if (string.Equals(parameters[i].Name, name, System.StringComparison.OrdinalIgnoreCase))
            {
                return parameters[i];
            }
        }

        return null;
    }

    [Fact]
    public void TheEchoTextCarriesBackslashEscapes()
    {
        // ECHO's text reaches the emulator's parser, so an escape sequence written in the shared
        // backslash notation must survive the MCP surface as well as the script one. Without this
        // flag a ReGIS or ANSI sequence echoed over MCP prints as text instead of being obeyed.
        var registry = BuildRegistry();

        Assert.True(registry.TryGet("ECHO", out var echo));
        Assert.NotNull(echo);

        var text = FindParameter(echo!, "text");
        Assert.NotNull(text);
        Assert.True(text!.DecodeEscapes,
            "ECHO writes through the emulator's parser, so its text must decode escapes on BOTH surfaces");
    }

    [Fact]
    public void TheEchoTextIsStillTheRequiredFirstParameter()
    {
        // Guards the shape the MCP schema and the generated help are built from. A reordering here
        // would change the tool signature without anything else going red.
        var registry = BuildRegistry();

        Assert.True(registry.TryGet("ECHO", out var echo));
        Assert.NotNull(echo);

        Assert.Single(echo!.Parameters);
        Assert.Equal("text", echo.Parameters[0].Name);
        Assert.True(echo.Parameters[0].Required);
        Assert.Equal(CommandParameterType.String, echo.Parameters[0].Type);
    }
}
