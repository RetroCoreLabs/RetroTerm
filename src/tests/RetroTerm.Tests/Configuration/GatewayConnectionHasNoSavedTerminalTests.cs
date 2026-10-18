using System;
using System.IO;
using System.Threading.Tasks;
using RetroTerm.Core.Configuration;
using RetroTerm.Core.Protocols;
using Xunit;

namespace RetroTerm.Tests.Configuration;

/// <summary>
/// A gateway connection no longer remembers a terminal. The terminal is chosen when the
/// connection is made, from the list the remote emulator registers, so the saved ident code
/// and terminal name were removed (8 October 2026).
/// </summary>
/// <remarks>
/// The connect code never read the saved ident code, but the connection list and the tab title
/// printed it ("Gateway identCode 44"), which looked like a choice that was being honoured.
/// Files saved before the removal still hold the two fields, so loading one has to keep working.
/// </remarks>
public class GatewayConnectionHasNoSavedTerminalTests
{
    // A connection list as an older build wrote it, with both removed fields present.
    // Written with doubled quotes so no backslash escapes are needed.
    private const string OldFile =
        "[{\"id\":\"g1\",\"name\":\"WASM ND100x\",\"protocol\":\"Gateway\"," +
        "\"gatewayIdentCode\":44,\"gatewayTerminalName\":\"TERMINAL 13\"," +
        "\"gatewaySelectFirstFree\":true}]";

    /// <summary>
    /// Loads a file in the old shape and returns the manager.
    /// </summary>
    private static async Task<ConfigurationManager> LoadOldFileAsync(string path)
    {
        File.WriteAllText(path, OldFile);
        var manager = new ConfigurationManager(path);
        await manager.LoadAsync();
        return manager;
    }

    [Fact]
    public async Task AFileSavedWithTheOldFieldsStillLoads()
    {
        string dir = Path.Combine(Path.GetTempPath(), "RetroTermGw_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var manager = await LoadOldFileAsync(Path.Combine(dir, "host-configurations.json"));

            // A failed load falls back to an empty list, so a count of one proves the parse worked.
            Assert.Single(manager.Configurations);
            Assert.Equal("WASM ND100x", manager.Configurations[0].Name);
            Assert.True(manager.Configurations[0].GatewaySelectFirstFree);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public async Task TheListSubtextForAGatewayIsJustGateway()
    {
        string dir = Path.Combine(Path.GetTempPath(), "RetroTermGw_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var manager = await LoadOldFileAsync(Path.Combine(dir, "host-configurations.json"));

            var parameters = manager.Configurations[0].ToConnectionParameters();

            Assert.Equal(ConnectionFactory.ProtocolType.Gateway, parameters.Protocol);
            Assert.Equal("Gateway", parameters.DisplayName);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void ASavedGatewayConnectionNoLongerWritesTheRemovedFields()
    {
        var config = new HostConfiguration { Name = "gw", Protocol = "Gateway", GatewaySelectFirstFree = true };

        string json = System.Text.Json.JsonSerializer.Serialize(config);

        Assert.DoesNotContain("IdentCode", json);
        Assert.DoesNotContain("GatewayTerminalName", json);
        Assert.Contains("GatewaySelectFirstFree", json);
    }
}
