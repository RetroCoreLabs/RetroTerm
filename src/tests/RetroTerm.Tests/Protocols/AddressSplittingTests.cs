using RetroTerm.Core.Protocols;
using Xunit;

namespace RetroTerm.Tests.Protocols;

/// <summary>
/// Splitting an address somebody typed into a host and a port.
/// </summary>
/// <remarks>
/// <para><b>Why this is not a one-line split on the colon</b></para>
/// Typing the port after the host is the fastest way to say it, and it is what anybody pastes. The
/// obvious implementation - split on the first colon - is wrong for IPv6, whose addresses are made
/// of colons. Getting it wrong does not fail loudly either: it connects to a DIFFERENT machine, or
/// to the right one on the wrong port, and looks like the host being down.
/// </remarks>
public class AddressSplittingTests
{
    [Fact]
    public void APlainHostHasNoPort()
    {
        Assert.True(ConnectionFactory.SplitHostAndPort("localhost", out string host, out int port));

        Assert.Equal("localhost", host);
        Assert.Equal(-1, port);
    }

    [Fact]
    public void AHostAndPortAreSeparated()
    {
        Assert.True(ConnectionFactory.SplitHostAndPort("127.0.0.1:2323", out string host, out int port));

        Assert.Equal("127.0.0.1", host);
        Assert.Equal(2323, port);
    }

    [Fact]
    public void SurroundingSpaceIsIgnored()
    {
        // Pasting an address brings spaces with it more often than not.
        Assert.True(ConnectionFactory.SplitHostAndPort("  d100.lab:9010  ", out string host, out int port));

        Assert.Equal("d100.lab", host);
        Assert.Equal(9010, port);
    }

    [Fact]
    public void ABareIpv6AddressIsAllHost()
    {
        // THE ONE THAT MATTERS. Splitting on the first colon would connect to a host called "" on
        // no port at all, and splitting on the LAST would ask for host "::" and port 1.
        Assert.True(ConnectionFactory.SplitHostAndPort("fe80::1ff:fe23:4567", out string host, out int port));

        Assert.Equal("fe80::1ff:fe23:4567", host);
        Assert.Equal(-1, port);
    }

    [Fact]
    public void ABracketedIpv6AddressCanCarryAPort()
    {
        // Brackets are the only unambiguous way to write it, which is why they exist.
        Assert.True(ConnectionFactory.SplitHostAndPort("[::1]:23", out string host, out int port));

        Assert.Equal("::1", host);
        Assert.Equal(23, port);
    }

    [Fact]
    public void ABracketedIpv6AddressWithoutAPortIsStillFine()
    {
        Assert.True(ConnectionFactory.SplitHostAndPort("[fe80::1]", out string host, out int port));

        Assert.Equal("fe80::1", host);
        Assert.Equal(-1, port);
    }

    [Fact]
    public void SomethingAfterTheColonThatIsNotANumberIsPartOfTheHost()
    {
        // Not a port, so not treated as one. Refusing to guess is the whole point.
        Assert.True(ConnectionFactory.SplitHostAndPort("machine:instance", out string host, out int port));

        Assert.Equal("machine:instance", host);
        Assert.Equal(-1, port);
    }

    [Fact]
    public void NothingTypedIsNotAHost()
    {
        Assert.False(ConnectionFactory.SplitHostAndPort("   ", out string host, out int port));
        Assert.Equal(string.Empty, host);
        Assert.Equal(-1, port);
    }

    [Fact]
    public void AnUnclosedBracketIsRefusedRatherThanPatchedUp()
    {
        // It cannot be read either way round, so it is refused. Guessing at half an address would
        // silently connect somewhere nobody asked for.
        Assert.False(ConnectionFactory.SplitHostAndPort("[::1", out string host, out int port));
    }

    [Fact]
    public void ATrailingColonIsPartOfTheHostRatherThanAnEmptyPort()
    {
        Assert.True(ConnectionFactory.SplitHostAndPort("localhost:", out string host, out int port));

        Assert.Equal("localhost:", host);
        Assert.Equal(-1, port);
    }
}
