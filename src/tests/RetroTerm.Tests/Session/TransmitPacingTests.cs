using System;
using RetroTerm.Core.Configuration;
using RetroTerm.Core.Protocols;
using RetroTerm.Desktop.Views;
using Xunit;

namespace RetroTerm.Tests.Session;

/// <summary>
/// Transmit pacing: a minimum gap between bytes, and an extra pause after a line ending.
/// </summary>
/// <remarks>
/// <para><b>Why it exists</b></para>
/// RetroTerm sends a whole line as one burst with no gap. Hardware that POLLS its UART in
/// software, rather than taking an interrupt per character, loses bytes at that rate - the next
/// character overwrites the last before the poll comes round. The ND-120's emulated period UART
/// showed exactly that, and it is why a scripted "li-fi" reached SINTRAN as "HF" on
/// 31 August 2026.
/// <para><b>What is NOT covered here</b></para>
/// The actual timing on the wire needs a real port, so it is not asserted in this suite. These
/// tests pin the settings, their defaults, the clamping and the parsing - everything that decides
/// WHETHER pacing happens and by how much.
/// </remarks>
public class TransmitPacingTests
{
    [Fact]
    public void ByDefaultThereIsNoDelayAtAll()
    {
        // Every connection saved before this existed, and every new one, must send exactly as
        // fast as it always did.
        var config = new HostConfiguration { Name = "plain", Host = "localhost", Port = 23 };

        Assert.Equal(0, config.TransmitDelayPerCharMs);
        Assert.Equal(0, config.TransmitDelayPerLineMs);

        var parameters = config.ToConnectionParameters();
        Assert.Equal(0, parameters.TransmitDelayPerCharMs);
        Assert.Equal(0, parameters.TransmitDelayPerLineMs);
    }

    [Fact]
    public void TheDelaysSurviveTheStoredConnection()
    {
        var config = new HostConfiguration
        {
            Name = "nd-120",
            Protocol = "Serial",
            PortName = "COM11",
            TransmitDelayPerCharMs = 20,
            TransmitDelayPerLineMs = 250
        };

        var parameters = config.ToConnectionParameters();
        Assert.Equal(20, parameters.TransmitDelayPerCharMs);
        Assert.Equal(250, parameters.TransmitDelayPerLineMs);

        // Clone forgetting a field is how a setting quietly stops working.
        var clone = config.Clone();
        Assert.Equal(20, clone.TransmitDelayPerCharMs);
        Assert.Equal(250, clone.TransmitDelayPerLineMs);
    }

    [Fact]
    public void ANegativeStoredDelayIsReadAsNone()
    {
        // A hand-edited file must not produce a negative, which would throw inside Task.Delay
        // the first time anything was typed.
        var config = new HostConfiguration
        {
            Name = "odd",
            Protocol = "Serial",
            PortName = "COM11",
            TransmitDelayPerCharMs = -5,
            TransmitDelayPerLineMs = -1
        };

        var parameters = config.ToConnectionParameters();

        Assert.Equal(0, parameters.TransmitDelayPerCharMs);
        Assert.Equal(0, parameters.TransmitDelayPerLineMs);
    }

    [Theory]
    [InlineData("0", 0)]
    [InlineData("20", 20)]
    [InlineData("250", 250)]
    [InlineData("", 0)]
    [InlineData("   ", 0)]
    [InlineData("-5", 0)]      // a stray minus means "none", not a crash later
    [InlineData("2x", 0)]      // half-typed
    [InlineData("abc", 0)]
    [InlineData(null, 0)]
    public void TheDelayBoxIsForgivingWhileItIsBeingTypedInto(string? typed, int expected)
    {
        Assert.Equal(expected, ManageConnectionsWindow.ParseDelayForTesting(typed));
    }

    [Fact]
    public void TheFactoryRefusesToSilentlyDropARequestedDelay()
    {
        // The factory reaches SerialConnection by reflection. If the property were ever renamed,
        // a caller asking for pacing would get an unpaced line and no warning - so a non-zero
        // value with nowhere to go is an error rather than a shrug. Proven by asking for a
        // property that genuinely does not exist.
        var ex = Record.Exception(() =>
            ConnectionFactory.SetOptionalIntForTesting(typeof(string), "irrelevant", "NoSuchProperty", 20));

        Assert.NotNull(ex);
        Assert.Contains("NoSuchProperty", ex!.Message);
    }

    [Fact]
    public void AZeroDelayNeedsNoPropertyAtAll()
    {
        // Nothing was asked for, so nothing can be lost - this must not throw on a connection
        // type that has no pacing.
        var ex = Record.Exception(() =>
            ConnectionFactory.SetOptionalIntForTesting(typeof(string), "irrelevant", "NoSuchProperty", 0));

        Assert.Null(ex);
    }
}
