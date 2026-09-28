using RetroTerm.Core.Protocols.WebSocket.Gateway;
using Xunit;

namespace RetroTerm.Tests.Gateway;

public class GatewaySettingsTests
{
    [Fact]
    public void DefaultValues_AreCorrect()
    {
        var settings = new GatewaySettings();
        Assert.False(settings.Enabled);
        Assert.Equal(8765, settings.Port);
    }

    [Fact]
    public void GatewayTerminalInfo_StoresValues()
    {
        var info = new GatewayTerminalInfo
        {
            IdentCode = 43,
            Name = "TERMINAL 12",
            LogicalDevice = 1
        };

        Assert.Equal(43, info.IdentCode);
        Assert.Equal("TERMINAL 12", info.Name);
        Assert.Equal(1, info.LogicalDevice);
    }
}
