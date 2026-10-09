using RetroTerm.Core.Protocols.WebSocket.Gateway;
using RetroTerm.Desktop.Helpers;
using Xunit;

namespace RetroTerm.Tests.Desktop;

/// <summary>
/// The terminal list a gateway connection prints. A logical device of -1 means "none" and must not
/// be printed (the NDIX ttys, which have no SINTRAN).
/// </summary>
public class GatewayMenuFormatterTests
{
    private static GatewayTerminalInfo Terminal(string name, int identCode, int logicalDevice)
    {
        return new GatewayTerminalInfo { Name = name, IdentCode = identCode, LogicalDevice = logicalDevice };
    }

    [Fact]
    public void ASintranTerminalShowsItsLogicalDevice()
    {
        string line = GatewayMenuFormatter.TerminalLine(1, Terminal("TERMINAL 12", 43, 51), false);

        // The name is padded to 12 so the device column lines up, then " device".
        Assert.Equal("   1. TERMINAL 12  device 51", line);
    }

    [Fact]
    public void ATerminalWithNoLogicalDeviceShowsNone()
    {
        // What the NDIX standalone ND-500 registers: logicalDevice -1.
        string line = GatewayMenuFormatter.TerminalLine(2, Terminal("tty2 (NDIX)", 3, -1), false);

        Assert.Equal("   2. tty2 (NDIX)", line);
        Assert.DoesNotContain("device", line);
        Assert.DoesNotContain("-1", line);
    }

    [Fact]
    public void ATerminalInUseIsMarkedWithAndWithoutADevice()
    {
        Assert.EndsWith(" device 51 [in use]", GatewayMenuFormatter.TerminalLine(1, Terminal("TERMINAL 12", 43, 51), true));
        string ndix = GatewayMenuFormatter.TerminalLine(1, Terminal("tty1 (NDIX)", 2, -1), true);
        Assert.EndsWith("[in use]", ndix);
        Assert.Contains("tty1 (NDIX)", ndix);
        Assert.DoesNotContain("device", ndix);
    }

    [Fact]
    public void DeviceZeroIsARealDeviceAndIsShown()
    {
        string line = GatewayMenuFormatter.TerminalLine(1, Terminal("X", 1, 0), false);

        Assert.Contains("device 0", line);
    }
}
