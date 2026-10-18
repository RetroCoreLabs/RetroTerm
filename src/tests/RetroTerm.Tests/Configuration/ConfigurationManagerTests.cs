using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using RetroTerm.Core.Configuration;
using Xunit;

namespace RetroTerm.Tests.Configuration;

public class ConfigurationManagerTests : IDisposable
{
    private readonly string _tempConfigPath;
    private readonly ConfigurationManager _configManager;

    public ConfigurationManagerTests()
    {
        _tempConfigPath = Path.Combine(Path.GetTempPath(), $"retroterm-test-{Guid.NewGuid()}.json");
        _configManager = new ConfigurationManager(_tempConfigPath);
    }

    public void Dispose()
    {
        if (File.Exists(_tempConfigPath))
        {
            File.Delete(_tempConfigPath);
        }
    }

    [Fact]
    public async Task LoadAsync_ShouldCreateDefaultConfigurations()
    {
        // Act
        await _configManager.LoadAsync(createDefaults: true);

        // Assert
        Assert.True(_configManager.Configurations.Count > 0);
        Assert.Contains(_configManager.Configurations, c => c.Name == "Local Telnet Server");
        Assert.Contains(_configManager.Configurations, c => c.Name == "Local SSH Server");
        Assert.Contains(_configManager.Configurations, c => c.Name == "TDV Test Server");
    }

    [Fact]
    public async Task AddAsync_ShouldAddConfiguration()
    {
        // Arrange
        await _configManager.LoadAsync();
        var config = new HostConfiguration
        {
            Name = "Test Server",
            Host = "test.example.com",
            Port = 23,
            Protocol = "Telnet",
            EmulatorType = "VT100"
        };

        // Act
        await _configManager.AddAsync(config);

        // Assert
        Assert.Contains(_configManager.Configurations, c => c.Name == "Test Server");
        Assert.True(File.Exists(_tempConfigPath));
    }

    [Fact]
    public async Task AddAsync_WithDuplicateName_ShouldThrowException()
    {
        // Arrange
        await _configManager.LoadAsync();
        var config1 = new HostConfiguration { Name = "Test Server" };
        var config2 = new HostConfiguration { Name = "Test Server" };

        // Act & Assert
        await _configManager.AddAsync(config1);
        await Assert.ThrowsAsync<InvalidOperationException>(() => _configManager.AddAsync(config2));
    }

    [Fact]
    public async Task AddAsync_WithEmptyName_ShouldThrowException()
    {
        // Arrange
        await _configManager.LoadAsync();
        var config = new HostConfiguration { Name = "" };

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() => _configManager.AddAsync(config));
    }

    [Fact]
    public async Task UpdateAsync_ShouldUpdateConfiguration()
    {
        // Arrange
        await _configManager.LoadAsync();
        var config = new HostConfiguration
        {
            Name = "Test Server",
            Host = "test.example.com"
        };
        await _configManager.AddAsync(config);

        // Act
        config.Host = "updated.example.com";
        config.Port = 22;
        await _configManager.UpdateAsync(config);

        // Assert
        var updated = _configManager.Configurations.First(c => c.Id == config.Id);
        Assert.Equal("updated.example.com", updated.Host);
        Assert.Equal(22, updated.Port);
    }

    [Fact]
    public async Task UpdateAsync_WithNonExistentId_ShouldThrowException()
    {
        // Arrange
        await _configManager.LoadAsync();
        var config = new HostConfiguration { Id = "non-existent" };

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() => _configManager.UpdateAsync(config));
    }

    [Fact]
    public async Task RemoveAsync_ShouldRemoveConfiguration()
    {
        // Arrange
        await _configManager.LoadAsync();
        var config = new HostConfiguration { Name = "Test Server" };
        await _configManager.AddAsync(config);
        var configId = config.Id;

        // Act
        await _configManager.RemoveAsync(configId);

        // Assert
        Assert.DoesNotContain(_configManager.Configurations, c => c.Id == configId);
    }

    [Fact]
    public async Task RemoveAsync_WithNonExistentId_ShouldThrowException()
    {
        // Arrange
        await _configManager.LoadAsync();

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() => _configManager.RemoveAsync("non-existent"));
    }

    [Fact]
    public async Task GetByIdAsync_ShouldReturnConfiguration()
    {
        // Arrange
        await _configManager.LoadAsync();
        var config = new HostConfiguration { Name = "Test Server" };
        await _configManager.AddAsync(config);

        // Act
        var result = await _configManager.GetByIdAsync(config.Id);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(config.Id, result.Id);
        Assert.Equal("Test Server", result.Name);
    }

    [Fact]
    public async Task GetByIdAsync_WithNonExistentId_ShouldReturnNull()
    {
        // Arrange
        await _configManager.LoadAsync();

        // Act
        var result = await _configManager.GetByIdAsync("non-existent");

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task SearchByNameAsync_ShouldReturnMatchingConfigurations()
    {
        // Arrange
        await _configManager.LoadAsync();
        var config1 = new HostConfiguration { Name = "Production Server" };
        var config2 = new HostConfiguration { Name = "Development Server" };
        var config3 = new HostConfiguration { Name = "Test Environment" };

        await _configManager.AddAsync(config1);
        await _configManager.AddAsync(config2);
        await _configManager.AddAsync(config3);

        // Act
        var results = await _configManager.SearchByNameAsync("Server");

        // Assert
        Assert.Equal(2, results.Count());
        Assert.Contains(results, c => c.Name == "Production Server");
        Assert.Contains(results, c => c.Name == "Development Server");
    }

    [Fact]
    public async Task GetByTagAsync_ShouldReturnMatchingConfigurations()
    {
        // Arrange
        await _configManager.LoadAsync();
        var config1 = new HostConfiguration
        {
            Name = "Server 1",
            Tags = new List<string> { "production", "database" }
        };
        var config2 = new HostConfiguration
        {
            Name = "Server 2",
            Tags = new List<string> { "development", "web" }
        };

        await _configManager.AddAsync(config1);
        await _configManager.AddAsync(config2);

        // Act
        var results = await _configManager.GetByTagAsync("production");

        // Assert
        Assert.Single(results);
        Assert.Equal("Server 1", results.First().Name);
    }

    [Fact]
    public async Task MarkAsUsedAsync_ShouldUpdateUseCountAndTimestamp()
    {
        // Arrange
        await _configManager.LoadAsync();
        var config = new HostConfiguration { Name = "Test Server" };
        await _configManager.AddAsync(config);
        var originalLastUsed = config.LastUsedAt;
        var originalCount = config.UseCount;

        // Act
        await _configManager.MarkAsUsedAsync(config.Id);

        // Assert
        var updated = _configManager.Configurations.First(c => c.Id == config.Id);
        Assert.True(updated.LastUsedAt > originalLastUsed);
        Assert.Equal(originalCount + 1, updated.UseCount);
    }

    [Fact]
    public async Task DuplicateAsync_ShouldCreateCopyWithNewId()
    {
        // Arrange
        await _configManager.LoadAsync();
        var original = new HostConfiguration
        {
            Name = "Original Server",
            Host = "original.example.com",
            Port = 23,
            IsFavorite = true
        };
        await _configManager.AddAsync(original);

        // Act
        var duplicate = await _configManager.DuplicateAsync(original.Id, "Duplicate Server");

        // Assert
        Assert.NotEqual(original.Id, duplicate.Id);
        Assert.Equal("Duplicate Server", duplicate.Name);
        Assert.Equal(original.Host, duplicate.Host);
        Assert.Equal(original.Port, duplicate.Port);
        Assert.Equal(original.IsFavorite, duplicate.IsFavorite);
        Assert.Equal(0, duplicate.UseCount);
    }

    [Fact]
    public async Task Favorites_ShouldReturnOnlyFavoriteConfigurations()
    {
        // Arrange
        await _configManager.LoadAsync();
        var config1 = new HostConfiguration { Name = "Server 1", IsFavorite = true };
        var config2 = new HostConfiguration { Name = "Server 2", IsFavorite = false };
        var config3 = new HostConfiguration { Name = "Server 3", IsFavorite = true };

        await _configManager.AddAsync(config1);
        await _configManager.AddAsync(config2);
        await _configManager.AddAsync(config3);

        // Act
        var favorites = _configManager.Favorites;

        // Assert
        Assert.Equal(2, favorites.Count());
        Assert.Contains(favorites, c => c.Name == "Server 1");
        Assert.Contains(favorites, c => c.Name == "Server 3");
    }

    [Fact]
    public async Task Recent_ShouldReturnRecentlyUsedConfigurations()
    {
        // Arrange
        await _configManager.LoadAsync();
        var config1 = new HostConfiguration { Name = "Server 1" };
        var config2 = new HostConfiguration { Name = "Server 2" };
        var config3 = new HostConfiguration { Name = "Server 3" };

        await _configManager.AddAsync(config1);
        await _configManager.AddAsync(config2);
        await _configManager.AddAsync(config3);

        // Mark them as used in different order
        await _configManager.MarkAsUsedAsync(config2.Id);
        await Task.Delay(10, TestContext.Current.CancellationToken); // Small delay to ensure different timestamps
        await _configManager.MarkAsUsedAsync(config1.Id);
        await Task.Delay(10, TestContext.Current.CancellationToken);
        await _configManager.MarkAsUsedAsync(config3.Id);

        // Act
        var recent = _configManager.Recent;

        // Assert
        Assert.Equal(3, recent.Count());
        Assert.Equal("Server 3", recent.First().Name); // Most recently used
        Assert.Equal("Server 1", recent.Skip(1).First().Name);
        Assert.Equal("Server 2", recent.Last().Name);
    }

    [Fact]
    public async Task ConfigurationsChanged_ShouldBeRaisedOnModifications()
    {
        // Arrange
        await _configManager.LoadAsync();
        var eventRaised = false;
        _configManager.ConfigurationsChanged += () => eventRaised = true;

        // Act
        var config = new HostConfiguration { Name = "Test Server" };
        await _configManager.AddAsync(config);

        // Assert
        Assert.True(eventRaised);
    }
}
