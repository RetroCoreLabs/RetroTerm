using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace RetroTerm.Core.Protocols.Net;

/// <summary>
/// Manages SSH host keys for security (similar to OpenSSH known_hosts).
/// Stores host key fingerprints and validates new connections.
/// </summary>
public class SSHHostKeyManager
{
    private readonly string _knownHostsPath;
    private Dictionary<string, HostKeyEntry> _knownHosts;

    /// <summary>
    /// Represents a known host entry
    /// </summary>
    public class HostKeyEntry
    {
        public string Host { get; set; } = "";
        public int Port { get; set; }
        public string Fingerprint { get; set; } = ""; // SHA256 fingerprint
        public string FingerprintMD5 { get; set; } = ""; // MD5 fingerprint (legacy)
        public DateTime FirstSeen { get; set; }
        public DateTime LastSeen { get; set; }
    }

    public SSHHostKeyManager(string? knownHostsPath = null)
    {
        // Default to user's config directory
        _knownHostsPath = knownHostsPath ??
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "RetroTerm",
                "known_hosts.json");

        _knownHosts = new Dictionary<string, HostKeyEntry>();
        Load();
    }

    /// <summary>
    /// Validates a host key. Returns true if trusted, false if unknown or changed.
    /// </summary>
    public HostKeyValidationResult ValidateHostKey(string host, int port, byte[] hostKey)
    {
        var fingerprint = ComputeSHA256Fingerprint(hostKey);
        var fingerprintMD5 = ComputeMD5Fingerprint(hostKey);
        var key = GetHostKey(host, port);

        var entry = _knownHosts.GetValueOrDefault(key);

        if (entry == null)
        {
            return new HostKeyValidationResult
            {
                Status = HostKeyStatus.Unknown,
                Fingerprint = fingerprint,
                FingerprintMD5 = fingerprintMD5,
                HostKey = hostKey,
                Message = $"Unknown host: {host}:{port}\n" +
                         $"Fingerprint (SHA256): {fingerprint}\n" +
                         $"Fingerprint (MD5): {fingerprintMD5}\n" +
                         $"Do you want to trust this host?"
            };
        }

        if (entry.Fingerprint != fingerprint)
        {
            return new HostKeyValidationResult
            {
                Status = HostKeyStatus.Changed,
                Fingerprint = fingerprint,
                FingerprintMD5 = fingerprintMD5,
                KnownFingerprint = entry.Fingerprint,
                HostKey = hostKey,
                Message = $"WARNING: Host key for {host}:{port} has changed!\n" +
                         $"Known fingerprint: {entry.Fingerprint}\n" +
                         $"New fingerprint:   {fingerprint}\n" +
                         $"This could indicate a man-in-the-middle attack.\n" +
                         $"Do you want to accept the new key?"
            };
        }

        // Update last seen
        entry.LastSeen = DateTime.UtcNow;
        Save();

        return new HostKeyValidationResult
        {
            Status = HostKeyStatus.Trusted,
            Fingerprint = fingerprint,
            FingerprintMD5 = fingerprintMD5,
            HostKey = hostKey,
            Message = "Host key matches known fingerprint"
        };
    }

    /// <summary>
    /// Adds or updates a host key entry
    /// </summary>
    public void TrustHostKey(string host, int port, byte[] hostKey)
    {
        var fingerprint = ComputeSHA256Fingerprint(hostKey);
        var fingerprintMD5 = ComputeMD5Fingerprint(hostKey);
        var key = GetHostKey(host, port);
        var now = DateTime.UtcNow;

        if (_knownHosts.TryGetValue(key, out var existing))
        {
            // Update existing
            existing.Fingerprint = fingerprint;
            existing.FingerprintMD5 = fingerprintMD5;
            existing.LastSeen = now;
        }
        else
        {
            // Add new
            _knownHosts[key] = new HostKeyEntry
            {
                Host = host,
                Port = port,
                Fingerprint = fingerprint,
                FingerprintMD5 = fingerprintMD5,
                FirstSeen = now,
                LastSeen = now
            };
        }

        Save();
    }

    /// <summary>
    /// Removes a host key entry
    /// </summary>
    public void RemoveHostKey(string host, int port)
    {
        var key = GetHostKey(host, port);
        _knownHosts.Remove(key);
        Save();
    }

    /// <summary>
    /// Gets all known hosts
    /// </summary>
    public IEnumerable<HostKeyEntry> GetAllHosts()
    {
        return _knownHosts.Values.ToList();
    }

    private string GetHostKey(string host, int port)
    {
        return $"{host}:{port}";
    }

    private string ComputeSHA256Fingerprint(byte[] data)
    {
        using var sha256 = SHA256.Create();
        var hash = sha256.ComputeHash(data);
        return "SHA256:" + Convert.ToBase64String(hash).TrimEnd('=');
    }

    private string ComputeMD5Fingerprint(byte[] data)
    {
        using var md5 = MD5.Create();
        var hash = md5.ComputeHash(data);
        return BitConverter.ToString(hash).Replace("-", ":").ToLowerInvariant();
    }

    private void Load()
    {
        try
        {
            if (File.Exists(_knownHostsPath))
            {
                var json = File.ReadAllText(_knownHostsPath);
                var entries = JsonSerializer.Deserialize<List<HostKeyEntry>>(json);

                if (entries != null)
                {
                    _knownHosts = entries.ToDictionary(
                        e => GetHostKey(e.Host, e.Port),
                        e => e);
                }
            }
        }
        catch (Exception)
        {
            // If loading fails, start with empty known hosts
            _knownHosts = new Dictionary<string, HostKeyEntry>();
        }
    }

    private void Save()
    {
        try
        {
            var directory = Path.GetDirectoryName(_knownHostsPath);
            if (directory != null && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var entries = _knownHosts.Values.ToList();
            var json = JsonSerializer.Serialize(entries, new JsonSerializerOptions
            {
                WriteIndented = true
            });

            File.WriteAllText(_knownHostsPath, json);
        }
        catch (Exception)
        {
            // Silently fail - don't prevent SSH connection if we can't save
        }
    }
}

/// <summary>
/// Result of host key validation
/// </summary>
public class HostKeyValidationResult
{
    public HostKeyStatus Status { get; set; }
    public string Fingerprint { get; set; } = "";
    public string FingerprintMD5 { get; set; } = "";
    public string? KnownFingerprint { get; set; }
    public string Message { get; set; } = "";
    public byte[] HostKey { get; set; } = Array.Empty<byte>();

    public bool IsValid => Status == HostKeyStatus.Trusted;
}

/// <summary>
/// Host key validation status
/// </summary>
public enum HostKeyStatus
{
    /// <summary>
    /// Host key matches known fingerprint
    /// </summary>
    Trusted,

    /// <summary>
    /// Host key is not in known_hosts
    /// </summary>
    Unknown,

    /// <summary>
    /// Host key has changed (potential MITM attack!)
    /// </summary>
    Changed
}

