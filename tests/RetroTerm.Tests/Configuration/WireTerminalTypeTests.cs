using RetroTerm.Core.Protocols;
using Xunit;

namespace RetroTerm.Tests.Configuration;

/// <summary>
/// The name a host is told this terminal is.
///
/// It becomes TERM on the far side and picks the terminfo entry there, so it has to be spelled the
/// way that host spells it. The emulator names in this application are upper case; the xterm
/// terminfo entries are lower case and hyphenated, and a host looking up "XTERM-256COLOR" finds
/// nothing and falls back to something dumb.
///
/// Building a connection does not open one, so this can be checked without a host.
/// </summary>
public class WireTerminalTypeTests
{
    private static string TerminalTypeOf(string emulatorType, ConnectionFactory.ProtocolType protocol)
    {
        var connection = ConnectionFactory.CreateConnection(new ConnectionFactory.ConnectionParameters
        {
            Protocol = protocol,
            Host = "localhost",
            Port = 23,
            Username = "user",
            Password = "",
            EmulatorType = emulatorType,
        });

        var property = connection.GetType().GetProperty("TerminalType");
        Assert.NotNull(property);
        return (string)property!.GetValue(connection)!;
    }

    [Theory]
    [InlineData("XTERM", "xterm")]
    [InlineData("XTERM-256COLOR", "xterm-256color")]
    public void TheXtermNamesGoOutTheWayTerminfoSpellsThem(string emulatorType, string expected)
    {
        Assert.Equal(expected, TerminalTypeOf(emulatorType, ConnectionFactory.ProtocolType.Telnet));
    }

    [Fact]
    public void ATdvKeepsItsOwnNameAndItsOwnCase()
    {
        // RFC 1091 has the telnet terminal type sent in upper case, and the ND hosts these
        // terminals talk to expect their own names. Only the xterm entries are the exception.
        Assert.Equal("TDV2200", TerminalTypeOf("TDV2200", ConnectionFactory.ProtocolType.Telnet));
    }

    [Fact]
    public void OverSshEverythingGoesLowerCase()
    {
        // SSH has no upper-case convention, and terminfo is lower case.
        Assert.Equal("vt100", TerminalTypeOf("VT100", ConnectionFactory.ProtocolType.SSH));
        Assert.Equal("xterm-256color", TerminalTypeOf("XTERM-256COLOR", ConnectionFactory.ProtocolType.SSH));
    }
}
