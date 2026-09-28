using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace RetroTerm.Core.Protocols.WebSocket.Gateway;

/// <summary>
/// Represents a single disk image entry with its drive type and unit number.
/// </summary>
public class GatewayDiskImageEntry
{
    /// <summary>
    /// Drive type: 0 = SMD, 1 = Floppy.
    /// </summary>
    public int DriveType { get; set; }

    /// <summary>
    /// Full path to the .IMG file on disk.
    /// </summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>
    /// Display name for the image.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Optional description of the image contents.
    /// </summary>
    public string Description { get; set; } = string.Empty;
}

/// <summary>
/// Configuration for gateway disk images.
/// Persisted to %AppData%\RetroTerm\gateway-disk-settings.json.
/// </summary>
public class GatewayDiskSettings
{
    /// <summary>
    /// All configured disk images. Each entry has a DriveType and Unit.
    /// </summary>
    public List<GatewayDiskImageEntry> Images { get; set; } = new();

    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private static string GetSettingsPath()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return System.IO.Path.Combine(appData, "RetroTerm", "gateway-disk-settings.json");
    }

    /// <summary>
    /// Load settings from disk. Returns default settings if file does not exist.
    /// </summary>
    public static GatewayDiskSettings Load()
    {
        var path = GetSettingsPath();
        if (!File.Exists(path))
            return new GatewayDiskSettings();

        try
        {
            var json = File.ReadAllText(path);
            var settings = JsonSerializer.Deserialize<GatewayDiskSettings>(json, s_jsonOptions);
            if (settings == null)
                return new GatewayDiskSettings();

            if (settings.Images == null)
                settings.Images = new List<GatewayDiskImageEntry>();

            return settings;
        }
        catch
        {
            return new GatewayDiskSettings();
        }
    }

    /// <summary>
    /// Save settings to disk.
    /// </summary>
    public void Save()
    {
        var path = GetSettingsPath();
        var dir = System.IO.Path.GetDirectoryName(path);
        if (dir != null && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        var json = JsonSerializer.Serialize(this, s_jsonOptions);
        File.WriteAllText(path, json);
    }
}
