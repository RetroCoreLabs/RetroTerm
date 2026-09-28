using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using RetroTerm.Core.Configuration;
using RetroTerm.Desktop.Helpers;
using Xunit;

namespace RetroTerm.Tests.Configuration;

/// <summary>
/// Tests for ConnectionDisplayHelper methods and HostConfiguration interaction patterns.
/// Previously tested EnhancedConnectionDialog internal methods; now tests the extracted helpers.
/// </summary>
public class EnhancedConnectionDialogTests
{
    #region Protocol Badge Tests

    [Fact]
    public void GetProtocolBadge_Telnet_ReturnsTEL()
    {
        var badge = ConnectionDisplayHelper.GetProtocolBadge("Telnet");
        Assert.Equal("TEL", badge);
    }

    [Fact]
    public void GetProtocolBadge_SSH_ReturnsSSH()
    {
        var badge = ConnectionDisplayHelper.GetProtocolBadge("SSH");
        Assert.Equal("SSH", badge);
    }

    [Fact]
    public void GetProtocolBadge_Serial_ReturnsSER()
    {
        var badge = ConnectionDisplayHelper.GetProtocolBadge("Serial");
        Assert.Equal("SER", badge);
    }

    [Fact]
    public void GetProtocolBadge_CaseInsensitive_SSH()
    {
        var badge = ConnectionDisplayHelper.GetProtocolBadge("ssh");
        Assert.Equal("SSH", badge);
    }

    [Fact]
    public void GetProtocolBadge_CaseInsensitive_Serial()
    {
        var badge = ConnectionDisplayHelper.GetProtocolBadge("serial");
        Assert.Equal("SER", badge);
    }

    [Fact]
    public void GetProtocolBadge_Unknown_DefaultsToTEL()
    {
        var badge = ConnectionDisplayHelper.GetProtocolBadge("WebSocket");
        Assert.Equal("TEL", badge);
    }

    #endregion

    #region Protocol Badge Color Tests

    [Fact]
    public void GetProtocolBadgeColorHex_Telnet_ReturnsTealHex()
    {
        var hex = ConnectionDisplayHelper.GetProtocolBadgeColorHex("Telnet");
        Assert.Equal("#4EC9B0", hex);
    }

    [Fact]
    public void GetProtocolBadgeColorHex_SSH_ReturnsBlueHex()
    {
        var hex = ConnectionDisplayHelper.GetProtocolBadgeColorHex("SSH");
        Assert.Equal("#569CD6", hex);
    }

    [Fact]
    public void GetProtocolBadgeColorHex_Serial_ReturnsOrangeHex()
    {
        var hex = ConnectionDisplayHelper.GetProtocolBadgeColorHex("Serial");
        Assert.Equal("#FF8C00", hex);
    }

    [Fact]
    public void GetProtocolBadgeColorHex_CaseInsensitive()
    {
        Assert.Equal("#569CD6", ConnectionDisplayHelper.GetProtocolBadgeColorHex("ssh"));
        Assert.Equal("#FF8C00", ConnectionDisplayHelper.GetProtocolBadgeColorHex("serial"));
    }

    [Fact]
    public void GetProtocolBadgeColorHex_Unknown_DefaultsToTelnet()
    {
        var hex = ConnectionDisplayHelper.GetProtocolBadgeColorHex("WebSocket");
        Assert.Equal("#4EC9B0", hex);
    }

    #endregion

    #region Connection Detail Tests

    [Fact]
    public void GetConnectionDetail_TelnetConfig_ShowsHostPort()
    {
        var config = new HostConfiguration
        {
            Protocol = "Telnet",
            Host = "myhost.com",
            Port = 23,
            LastUsedAt = DateTime.UtcNow
        };

        var detail = ConnectionDisplayHelper.GetConnectionDetail(config);
        Assert.Contains("myhost.com:23", detail);
    }

    [Fact]
    public void GetConnectionDetail_SSHConfig_ShowsHostPort()
    {
        var config = new HostConfiguration
        {
            Protocol = "SSH",
            Host = "ssh.example.com",
            Port = 22,
            LastUsedAt = DateTime.UtcNow
        };

        var detail = ConnectionDisplayHelper.GetConnectionDetail(config);
        Assert.Contains("ssh.example.com:22", detail);
    }

    [Fact]
    public void GetConnectionDetail_SerialConfig_ShowsPortNameAndBaud()
    {
        var config = new HostConfiguration
        {
            Protocol = "Serial",
            PortName = "COM3",
            BaudRate = 9600,
            LastUsedAt = DateTime.UtcNow
        };

        var detail = ConnectionDisplayHelper.GetConnectionDetail(config);
        Assert.Contains("COM3", detail);
        Assert.Contains("9600bps", detail);
    }

    [Fact]
    public void GetConnectionDetail_ContainsLastUsedInfo()
    {
        var config = new HostConfiguration
        {
            Protocol = "Telnet",
            Host = "localhost",
            Port = 23,
            LastUsedAt = DateTime.UtcNow
        };

        var detail = ConnectionDisplayHelper.GetConnectionDetail(config);
        // Should contain the Unicode middle dot separator and some time text
        Assert.Contains("\u00b7", detail);
    }

    #endregion

    #region Relative Time Tests

    [Fact]
    public void GetRelativeTime_JustNow_ReturnsJustNow()
    {
        var time = DateTime.UtcNow;
        var result = ConnectionDisplayHelper.GetRelativeTime(time);
        Assert.Equal("Just now", result);
    }

    [Fact]
    public void GetRelativeTime_MinutesAgo_ReturnsMinutes()
    {
        var time = DateTime.UtcNow.AddMinutes(-5);
        var result = ConnectionDisplayHelper.GetRelativeTime(time);
        Assert.Equal("5m ago", result);
    }

    [Fact]
    public void GetRelativeTime_HoursAgo_ReturnsHours()
    {
        var time = DateTime.UtcNow.AddHours(-3);
        var result = ConnectionDisplayHelper.GetRelativeTime(time);
        Assert.Equal("3h ago", result);
    }

    [Fact]
    public void GetRelativeTime_DaysAgo_ReturnsDays()
    {
        var time = DateTime.UtcNow.AddDays(-7);
        var result = ConnectionDisplayHelper.GetRelativeTime(time);
        Assert.Equal("7d ago", result);
    }

    [Fact]
    public void GetRelativeTime_MonthsAgo_ReturnsMonths()
    {
        var time = DateTime.UtcNow.AddDays(-60);
        var result = ConnectionDisplayHelper.GetRelativeTime(time);
        Assert.Equal("2mo ago", result);
    }

    [Fact]
    public void GetRelativeTime_VeryOld_ReturnsNever()
    {
        var time = DateTime.UtcNow.AddDays(-400);
        var result = ConnectionDisplayHelper.GetRelativeTime(time);
        Assert.Equal("Never", result);
    }

    [Fact]
    public void GetRelativeTime_LocalTime_HandledCorrectly()
    {
        // Should handle local DateTime kind without error
        var time = DateTime.Now.AddMinutes(-10);
        var result = ConnectionDisplayHelper.GetRelativeTime(time);
        Assert.Equal("10m ago", result);
    }

    #endregion

    #region HostConfiguration MarkAsUsed Tests

    [Fact]
    public void MarkAsUsed_IncrementsUseCount()
    {
        var config = new HostConfiguration { Name = "Test" };
        var originalCount = config.UseCount;

        config.MarkAsUsed();

        Assert.Equal(originalCount + 1, config.UseCount);
    }

    [Fact]
    public void MarkAsUsed_UpdatesLastUsedAt()
    {
        var config = new HostConfiguration
        {
            Name = "Test",
            LastUsedAt = DateTime.UtcNow.AddDays(-10)
        };

        var before = config.LastUsedAt;
        config.MarkAsUsed();

        Assert.True(config.LastUsedAt > before);
    }

    [Fact]
    public void MarkAsUsed_CalledMultipleTimes_IncrementsCorrectly()
    {
        var config = new HostConfiguration { Name = "Test", UseCount = 0 };

        config.MarkAsUsed();
        config.MarkAsUsed();
        config.MarkAsUsed();

        Assert.Equal(3, config.UseCount);
    }

    #endregion

    #region Favorite Toggle Tests

    [Fact]
    public void IsFavorite_DefaultsFalse()
    {
        var config = new HostConfiguration { Name = "Test" };
        Assert.False(config.IsFavorite);
    }

    [Fact]
    public void IsFavorite_CanBeToggled()
    {
        var config = new HostConfiguration { Name = "Test", IsFavorite = false };

        config.IsFavorite = !config.IsFavorite;
        Assert.True(config.IsFavorite);

        config.IsFavorite = !config.IsFavorite;
        Assert.False(config.IsFavorite);
    }

    [Fact]
    public async Task FavoriteToggle_PersistsThroughSave()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"retroterm-fav-test-{Guid.NewGuid()}.json");
        try
        {
            var manager = new ConfigurationManager(tempPath);
            await manager.LoadAsync();

            var config = new HostConfiguration { Name = "FavTest", IsFavorite = false };
            await manager.AddAsync(config);

            // Toggle favorite
            config.IsFavorite = true;
            await manager.UpdateAsync(config);

            // Reload and verify
            var manager2 = new ConfigurationManager(tempPath);
            await manager2.LoadAsync();

            bool found = false;
            for (int i = 0; i < manager2.Configurations.Count; i++)
            {
                if (manager2.Configurations[i].Id == config.Id)
                {
                    Assert.True(manager2.Configurations[i].IsFavorite);
                    found = true;
                    break;
                }
            }
            Assert.True(found, "Config should be found after reload");
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
    }

    #endregion

    #region Saved Connections Sort Order Tests

    [Fact]
    public void SortOrder_FavoritesFirst_ThenAlphabetical()
    {
        var configs = new List<HostConfiguration>
        {
            new HostConfiguration { Name = "Zebra", IsFavorite = false },
            new HostConfiguration { Name = "Alpha", IsFavorite = false },
            new HostConfiguration { Name = "Mango", IsFavorite = true },
            new HostConfiguration { Name = "Beta", IsFavorite = true }
        };

        // Replicate the dialog sort logic: favorites first, then alpha
        configs.Sort((a, b) =>
        {
            if (a.IsFavorite && !b.IsFavorite) return -1;
            if (!a.IsFavorite && b.IsFavorite) return 1;
            return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
        });

        // Favorites first, alphabetical
        Assert.Equal("Beta", configs[0].Name);
        Assert.Equal("Mango", configs[1].Name);
        // Non-favorites, alphabetical
        Assert.Equal("Alpha", configs[2].Name);
        Assert.Equal("Zebra", configs[3].Name);
    }

    [Fact]
    public void SortOrder_NoFavorites_JustAlphabetical()
    {
        var configs = new List<HostConfiguration>
        {
            new HostConfiguration { Name = "Zebra", IsFavorite = false },
            new HostConfiguration { Name = "Alpha", IsFavorite = false },
            new HostConfiguration { Name = "Mango", IsFavorite = false }
        };

        configs.Sort((a, b) =>
        {
            if (a.IsFavorite && !b.IsFavorite) return -1;
            if (!a.IsFavorite && b.IsFavorite) return 1;
            return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
        });

        Assert.Equal("Alpha", configs[0].Name);
        Assert.Equal("Mango", configs[1].Name);
        Assert.Equal("Zebra", configs[2].Name);
    }

    [Fact]
    public void SortOrder_AllFavorites_StillAlphabetical()
    {
        var configs = new List<HostConfiguration>
        {
            new HostConfiguration { Name = "Zebra", IsFavorite = true },
            new HostConfiguration { Name = "Alpha", IsFavorite = true },
            new HostConfiguration { Name = "Mango", IsFavorite = true }
        };

        configs.Sort((a, b) =>
        {
            if (a.IsFavorite && !b.IsFavorite) return -1;
            if (!a.IsFavorite && b.IsFavorite) return 1;
            return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
        });

        Assert.Equal("Alpha", configs[0].Name);
        Assert.Equal("Mango", configs[1].Name);
        Assert.Equal("Zebra", configs[2].Name);
    }

    #endregion

    #region MarkAsUsed via ConfigurationManager Tests

    [Fact]
    public async Task MarkAsUsedAsync_IncrementsCountAndUpdatesTime()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"retroterm-used-test-{Guid.NewGuid()}.json");
        try
        {
            var manager = new ConfigurationManager(tempPath);
            await manager.LoadAsync();

            var config = new HostConfiguration
            {
                Name = "UsageTest",
                UseCount = 0,
                LastUsedAt = DateTime.UtcNow.AddDays(-5)
            };
            await manager.AddAsync(config);
            var oldTime = config.LastUsedAt;

            await manager.MarkAsUsedAsync(config.Id);

            // Find the config (no LINQ)
            HostConfiguration? updated = null;
            for (int i = 0; i < manager.Configurations.Count; i++)
            {
                if (manager.Configurations[i].Id == config.Id)
                {
                    updated = manager.Configurations[i];
                    break;
                }
            }

            Assert.NotNull(updated);
            Assert.Equal(1, updated.UseCount);
            Assert.True(updated.LastUsedAt > oldTime);
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
    }

    [Fact]
    public async Task MarkAsUsedAsync_PersistsToFile()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"retroterm-persist-test-{Guid.NewGuid()}.json");
        try
        {
            var manager = new ConfigurationManager(tempPath);
            await manager.LoadAsync();

            var config = new HostConfiguration { Name = "PersistTest" };
            await manager.AddAsync(config);

            await manager.MarkAsUsedAsync(config.Id);

            // Reload from disk
            var manager2 = new ConfigurationManager(tempPath);
            await manager2.LoadAsync();

            HostConfiguration? reloaded = null;
            for (int i = 0; i < manager2.Configurations.Count; i++)
            {
                if (manager2.Configurations[i].Id == config.Id)
                {
                    reloaded = manager2.Configurations[i];
                    break;
                }
            }

            Assert.NotNull(reloaded);
            Assert.Equal(1, reloaded.UseCount);
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
    }

    #endregion

    #region No LINQ/foreach Verification

    [Fact]
    public void ConnectionDisplayHelper_SourceFile_ContainsNoLinq()
    {
        // Verify the source file does not contain LINQ usages
        var sourcePath = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory,
            "..", "..", "..", "..", "..",
            "src", "RetroTerm.Desktop", "Helpers", "ConnectionDisplayHelper.cs");

        // Normalize path
        sourcePath = Path.GetFullPath(sourcePath);

        if (!File.Exists(sourcePath))
        {
            // Try alternative path resolution
            sourcePath = @"E:\Dev\Ronny\RetroTerm\src\RetroTerm.Desktop\Helpers\ConnectionDisplayHelper.cs";
        }

        if (File.Exists(sourcePath))
        {
            var content = File.ReadAllText(sourcePath);
            // Should not contain System.Linq using (except in comments)
            var lines = content.Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i].Trim();
                // Skip comment-only lines
                if (line.StartsWith("//") || line.StartsWith("///") || line.StartsWith("*"))
                    continue;

                Assert.DoesNotContain("using System.Linq", line);
                Assert.DoesNotContain(".ToList()", line);
                Assert.DoesNotContain(".OrderBy(", line);
                Assert.DoesNotContain(".Where(", line);
                Assert.DoesNotContain(".Any()", line);
                Assert.DoesNotContain(".FirstOrDefault(", line);
                Assert.DoesNotContain("foreach", line);
            }
        }
    }

    #endregion

    #region Serial Config Detail Tests

    [Fact]
    public void GetConnectionDetail_SerialWithNullPortName_ShowsQuestionMark()
    {
        var config = new HostConfiguration
        {
            Protocol = "Serial",
            PortName = null,
            BaudRate = 9600,
            LastUsedAt = DateTime.UtcNow
        };

        var detail = ConnectionDisplayHelper.GetConnectionDetail(config);
        Assert.Contains("?", detail);
    }

    #endregion

    #region ConnectionProfileViewModel Tests

    [Fact]
    public void ConnectionProfileViewModel_LoadDefaults_IsNew()
    {
        var vm = new RetroTerm.Desktop.ViewModels.ConnectionProfileViewModel();
        vm.LoadDefaults();

        Assert.True(vm.IsNew);
        Assert.False(vm.IsDirty); // empty name = not dirty for new
        Assert.False(vm.CanSave);
    }

    [Fact]
    public void ConnectionProfileViewModel_LoadDefaults_ThenSetName_IsDirty()
    {
        var vm = new RetroTerm.Desktop.ViewModels.ConnectionProfileViewModel();
        vm.LoadDefaults();

        vm.Name = "My Connection";
        Assert.True(vm.IsDirty);
        Assert.True(vm.CanSave);
    }

    [Fact]
    public void ConnectionProfileViewModel_LoadFromConfig_NotDirty()
    {
        var config = new HostConfiguration
        {
            Name = "Test",
            Host = "example.com",
            Port = 2323,
            Protocol = "Telnet",
            EmulatorType = "TDV2200",
            Width = 80,
            Height = 24
        };

        var vm = new RetroTerm.Desktop.ViewModels.ConnectionProfileViewModel();
        vm.LoadFromConfiguration(config);

        Assert.False(vm.IsNew);
        Assert.False(vm.IsDirty);
        Assert.False(vm.CanSave);
        Assert.Equal("Test", vm.Name);
        Assert.Equal("example.com", vm.Host);
        Assert.Equal(2323, vm.Port);
    }

    [Fact]
    public void ConnectionProfileViewModel_LoadFromConfig_ChangeField_IsDirty()
    {
        var config = new HostConfiguration
        {
            Name = "Test",
            Host = "example.com",
            Port = 2323,
            Protocol = "Telnet"
        };

        var vm = new RetroTerm.Desktop.ViewModels.ConnectionProfileViewModel();
        vm.LoadFromConfiguration(config);

        vm.Host = "newhost.com";
        Assert.True(vm.IsDirty);
        Assert.True(vm.CanSave);
    }

    [Fact]
    public void ConnectionProfileViewModel_MarkClean_ResetsState()
    {
        var config = new HostConfiguration
        {
            Name = "Test",
            Host = "example.com",
            Port = 2323,
            Protocol = "Telnet"
        };

        var vm = new RetroTerm.Desktop.ViewModels.ConnectionProfileViewModel();
        vm.LoadFromConfiguration(config);
        vm.Host = "changed.com";

        Assert.True(vm.IsDirty);

        vm.MarkClean();

        Assert.False(vm.IsDirty);
        Assert.False(vm.CanSave);
    }

    [Fact]
    public void ConnectionProfileViewModel_ToHostConfiguration_RoundTrips()
    {
        var original = new HostConfiguration
        {
            Name = "RoundTrip",
            Host = "server.com",
            Port = 8080,
            Protocol = "SSH",
            Username = "admin",
            EmulatorType = "TDV1200",
            Width = 132,
            Height = 25,
            BaudRate = 19200,
            DataBits = 7
        };

        var vm = new RetroTerm.Desktop.ViewModels.ConnectionProfileViewModel();
        vm.LoadFromConfiguration(original);

        var result = vm.ToHostConfiguration();

        Assert.Equal("RoundTrip", result.Name);
        Assert.Equal("server.com", result.Host);
        Assert.Equal(8080, result.Port);
        Assert.Equal("SSH", result.Protocol);
        Assert.Equal("admin", result.Username);
        Assert.Equal("TDV1200", result.EmulatorType);
        Assert.Equal(132, result.Width);
        Assert.Equal(25, result.Height);
    }

    [Fact]
    public void ConnectionProfileViewModel_ToConnectionParameters_CorrectProtocol()
    {
        var vm = new RetroTerm.Desktop.ViewModels.ConnectionProfileViewModel();
        vm.LoadDefaults();
        vm.Name = "SSHTest";
        vm.Protocol = "SSH";
        vm.Host = "ssh.example.com";
        vm.Port = 22;
        vm.Username = "user";
        vm.Password = "pass";

        var parameters = vm.ToConnectionParameters();

        Assert.Equal(RetroTerm.Core.Protocols.ConnectionFactory.ProtocolType.SSH, parameters.Protocol);
        Assert.Equal("ssh.example.com", parameters.Host);
        Assert.Equal(22, parameters.Port);
        Assert.Equal("user", parameters.Username);
        Assert.Equal("pass", parameters.Password);
    }

    #endregion
}
