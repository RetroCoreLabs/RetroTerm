using System;
using System.Collections.Generic;
using RetroTerm.Core.Configuration;
using Xunit;

namespace RetroTerm.Tests.Configuration;

public class HostConfigurationTests
{
    [Fact]
    public void Constructor_ShouldInitializeWithDefaults()
    {
        // Act
        var config = new HostConfiguration();

        // Assert
        Assert.NotEmpty(config.Id);
        Assert.Equal("localhost", config.Host);
        Assert.Equal(23, config.Port);
        Assert.Equal("Telnet", config.Protocol);
        Assert.Equal("VT100", config.EmulatorType);
        Assert.Equal(80, config.Width);
        Assert.Equal(24, config.Height);
        Assert.Equal(1000, config.MaxScrollback);
        Assert.NotNull(config.EmulatorSettings);
        Assert.NotNull(config.ConnectionSettings);
        Assert.NotNull(config.Tags);
        Assert.False(config.IsFavorite);
        Assert.Equal(0, config.UseCount);
    }

    [Fact]
    public void Clone_ShouldCreateCopyWithNewId()
    {
        // Arrange
        var original = new HostConfiguration
        {
            Name = "Test Config",
            Host = "example.com",
            Port = 22,
            Protocol = "SSH",
            Username = "user",
            Password = "pass",
            EmulatorType = "TDV1200",
            Width = 132,
            Height = 25,
            MaxScrollback = 2000,
            IsFavorite = true,
            Tags = new List<string> { "test", "ssh" },
            Notes = "Test notes"
        };

        // Act
        var clone = original.Clone();

        // Assert
        Assert.NotEqual(original.Id, clone.Id);
        Assert.Equal("Test Config (Copy)", clone.Name);
        Assert.Equal(original.Host, clone.Host);
        Assert.Equal(original.Port, clone.Port);
        Assert.Equal(original.Protocol, clone.Protocol);
        Assert.Equal(original.Username, clone.Username);
        Assert.Equal(original.Password, clone.Password);
        Assert.Equal(original.EmulatorType, clone.EmulatorType);
        Assert.Equal(original.Width, clone.Width);
        Assert.Equal(original.Height, clone.Height);
        Assert.Equal(original.MaxScrollback, clone.MaxScrollback);
        Assert.Equal(original.IsFavorite, clone.IsFavorite);
        Assert.Equal(original.Tags, clone.Tags);
        Assert.Equal(original.Notes, clone.Notes);
        Assert.Equal(0, clone.UseCount);
        Assert.NotEqual(original.CreatedAt, clone.CreatedAt);
    }

    [Fact]
    public void MarkAsUsed_ShouldUpdateTimestampAndCount()
    {
        // Arrange
        var config = new HostConfiguration();
        var originalLastUsed = config.LastUsedAt;
        var originalCount = config.UseCount;

        // Act
        config.MarkAsUsed();

        // Assert
        Assert.True(config.LastUsedAt > originalLastUsed);
        Assert.Equal(originalCount + 1, config.UseCount);
    }

    [Fact]
    public void ToString_ShouldReturnFormattedString()
    {
        // Arrange
        var config = new HostConfiguration
        {
            Name = "Test Server",
            Host = "example.com",
            Port = 22,
            Protocol = "SSH"
        };

        // Act
        var result = config.ToString();

        // Assert
        Assert.Equal("Test Server (example.com:22 via SSH)", result);
    }

    [Fact]
    public void EmulatorSettings_ShouldBeMutable()
    {
        // Arrange
        var config = new HostConfiguration();

        // Act
        config.EmulatorSettings["test"] = "value";
        config.EmulatorSettings["number"] = 42;

        // Assert
        Assert.Equal("value", config.EmulatorSettings["test"]);
        Assert.Equal(42, config.EmulatorSettings["number"]);
    }

    [Fact]
    public void ConnectionSettings_ShouldBeMutable()
    {
        // Arrange
        var config = new HostConfiguration();

        // Act
        config.ConnectionSettings["timeout"] = 30;
        config.ConnectionSettings["keepalive"] = true;

        // Assert
        Assert.Equal(30, config.ConnectionSettings["timeout"]);
        Assert.Equal(true, config.ConnectionSettings["keepalive"]);
    }

    [Fact]
    public void Tags_ShouldBeMutable()
    {
        // Arrange
        var config = new HostConfiguration();

        // Act
        config.Tags.Add("production");
        config.Tags.Add("database");

        // Assert
        Assert.Contains("production", config.Tags);
        Assert.Contains("database", config.Tags);
        Assert.Equal(2, config.Tags.Count);
    }
}
