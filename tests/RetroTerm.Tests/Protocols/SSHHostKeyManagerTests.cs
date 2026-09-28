using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using RetroTerm.Core.Protocols.Net;
using Xunit;

namespace RetroTerm.Tests.Protocols;

/// <summary>
/// Unit tests for SSHHostKeyManager - host key validation and storage
/// </summary>
public class SSHHostKeyManagerTests : IDisposable
{
    private readonly string _testKnownHostsPath;
    private SSHHostKeyManager _manager;

    public SSHHostKeyManagerTests()
    {
        // Use a temporary file for testing
        _testKnownHostsPath = Path.Combine(Path.GetTempPath(), $"test_known_hosts_{Guid.NewGuid()}.json");
        _manager = new SSHHostKeyManager(_testKnownHostsPath);
    }

    public void Dispose()
    {
        // Clean up test file
        if (File.Exists(_testKnownHostsPath))
        {
            File.Delete(_testKnownHostsPath);
        }
    }

    [Fact]
    public void ValidateHostKey_UnknownHost_ShouldReturnUnknownStatus()
    {
        // Arrange
        var hostKey = GenerateTestHostKey();

        // Act
        var result = _manager.ValidateHostKey("test.example.com", 22, hostKey);

        // Assert
        Assert.Equal(HostKeyStatus.Unknown, result.Status);
        Assert.False(result.IsValid);
        Assert.NotNull(result.Fingerprint);
        Assert.NotNull(result.FingerprintMD5);
        Assert.Contains("Unknown host", result.Message);
    }

    [Fact]
    public void ValidateHostKey_TrustedHost_ShouldReturnTrustedStatus()
    {
        // Arrange
        var hostKey = GenerateTestHostKey();
        _manager.TrustHostKey("test.example.com", 22, hostKey);

        // Act
        var result = _manager.ValidateHostKey("test.example.com", 22, hostKey);

        // Assert
        Assert.Equal(HostKeyStatus.Trusted, result.Status);
        Assert.True(result.IsValid);
        Assert.Contains("matches known fingerprint", result.Message);
    }

    [Fact]
    public void ValidateHostKey_ChangedHostKey_ShouldReturnChangedStatus()
    {
        // Arrange
        var originalKey = GenerateTestHostKey();
        _manager.TrustHostKey("test.example.com", 22, originalKey);

        var newKey = GenerateTestHostKey(); // Different key

        // Act
        var result = _manager.ValidateHostKey("test.example.com", 22, newKey);

        // Assert
        Assert.Equal(HostKeyStatus.Changed, result.Status);
        Assert.False(result.IsValid);
        Assert.NotNull(result.KnownFingerprint);
        Assert.Contains("WARNING", result.Message);
        Assert.Contains("changed", result.Message);
    }

    [Fact]
    public void TrustHostKey_NewHost_ShouldAddEntry()
    {
        // Arrange
        var hostKey = GenerateTestHostKey();

        // Act
        _manager.TrustHostKey("newhost.example.com", 22, hostKey);

        // Assert
        var result = _manager.ValidateHostKey("newhost.example.com", 22, hostKey);
        Assert.Equal(HostKeyStatus.Trusted, result.Status);

        var allHosts = _manager.GetAllHosts().ToList();
        Assert.Single(allHosts);
        Assert.Equal("newhost.example.com", allHosts[0].Host);
        Assert.Equal(22, allHosts[0].Port);
    }

    [Fact]
    public void TrustHostKey_ExistingHost_ShouldUpdateEntry()
    {
        // Arrange
        var originalKey = GenerateTestHostKey();
        _manager.TrustHostKey("update.example.com", 22, originalKey);
        var originalFingerprint = _manager.ValidateHostKey("update.example.com", 22, originalKey).Fingerprint;

        var newKey = GenerateTestHostKey();

        // Act
        _manager.TrustHostKey("update.example.com", 22, newKey);

        // Assert
        var result = _manager.ValidateHostKey("update.example.com", 22, newKey);
        Assert.Equal(HostKeyStatus.Trusted, result.Status);
        Assert.NotEqual(originalFingerprint, result.Fingerprint);
    }

    [Fact]
    public void RemoveHostKey_ExistingHost_ShouldRemoveEntry()
    {
        // Arrange
        var hostKey = GenerateTestHostKey();
        _manager.TrustHostKey("remove.example.com", 22, hostKey);

        // Act
        _manager.RemoveHostKey("remove.example.com", 22);

        // Assert
        var result = _manager.ValidateHostKey("remove.example.com", 22, hostKey);
        Assert.Equal(HostKeyStatus.Unknown, result.Status);

        var allHosts = _manager.GetAllHosts().ToList();
        Assert.Empty(allHosts);
    }

    [Fact]
    public void ValidateHostKey_DifferentPorts_ShouldTreatAsDifferentHosts()
    {
        // Arrange
        var hostKey = GenerateTestHostKey();
        _manager.TrustHostKey("multiport.example.com", 22, hostKey);
        _manager.TrustHostKey("multiport.example.com", 2222, hostKey);

        // Act
        var result22 = _manager.ValidateHostKey("multiport.example.com", 22, hostKey);
        var result2222 = _manager.ValidateHostKey("multiport.example.com", 2222, hostKey);

        // Assert
        Assert.Equal(HostKeyStatus.Trusted, result22.Status);
        Assert.Equal(HostKeyStatus.Trusted, result2222.Status);

        var allHosts = _manager.GetAllHosts().ToList();
        Assert.Equal(2, allHosts.Count);
    }

    [Fact]
    public void Fingerprint_ShouldBeConsistent()
    {
        // Arrange
        var hostKey = GenerateTestHostKey();

        // Act
        var result1 = _manager.ValidateHostKey("fingerprint.example.com", 22, hostKey);
        var result2 = _manager.ValidateHostKey("fingerprint.example.com", 22, hostKey);

        // Assert
        Assert.Equal(result1.Fingerprint, result2.Fingerprint);
        Assert.Equal(result1.FingerprintMD5, result2.FingerprintMD5);
    }

    [Fact]
    public void Fingerprint_SHA256_ShouldStartWithSHA256()
    {
        // Arrange
        var hostKey = GenerateTestHostKey();

        // Act
        var result = _manager.ValidateHostKey("sha256.example.com", 22, hostKey);

        // Assert
        Assert.StartsWith("SHA256:", result.Fingerprint);
    }

    [Fact]
    public void GetAllHosts_ShouldReturnAllEntries()
    {
        // Arrange
        _manager.TrustHostKey("host1.example.com", 22, GenerateTestHostKey());
        _manager.TrustHostKey("host2.example.com", 22, GenerateTestHostKey());
        _manager.TrustHostKey("host3.example.com", 2222, GenerateTestHostKey());

        // Act
        var allHosts = _manager.GetAllHosts().ToList();

        // Assert
        Assert.Equal(3, allHosts.Count);
        Assert.Contains(allHosts, h => h.Host == "host1.example.com");
        Assert.Contains(allHosts, h => h.Host == "host2.example.com");
        Assert.Contains(allHosts, h => h.Host == "host3.example.com" && h.Port == 2222);
    }

    [Fact]
    public void Persistence_ShouldSaveAndLoad()
    {
        // Arrange
        var hostKey = GenerateTestHostKey();
        _manager.TrustHostKey("persist.example.com", 22, hostKey);

        // Act - Create new manager with same path
        var newManager = new SSHHostKeyManager(_testKnownHostsPath);

        // Assert
        var result = newManager.ValidateHostKey("persist.example.com", 22, hostKey);
        Assert.Equal(HostKeyStatus.Trusted, result.Status);
    }

    [Fact]
    public void LastSeen_ShouldUpdateOnValidation()
    {
        // Arrange
        var hostKey = GenerateTestHostKey();
        _manager.TrustHostKey("timeseen.example.com", 22, hostKey);

        var before = DateTime.UtcNow;
        System.Threading.Thread.Sleep(100); // Small delay

        // Act
        _manager.ValidateHostKey("timeseen.example.com", 22, hostKey);

        // Assert
        var allHosts = _manager.GetAllHosts().ToList();
        var entry = allHosts.First(h => h.Host == "timeseen.example.com");
        Assert.True(entry.LastSeen >= before);
    }

    private byte[] GenerateTestHostKey()
    {
        // Generate a fake host key for testing (32 random bytes)
        var key = new byte[32];
        using (var rng = RandomNumberGenerator.Create())
        {
            rng.GetBytes(key);
        }
        return key;
    }
}

