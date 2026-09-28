using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

namespace RetroTerm.Core.Protocols.WebSocket.Gateway;

/// <summary>
/// Configuration for the ND-100 Gateway WebSocket listener.
/// Persisted to %AppData%\RetroTerm\gateway-settings.json.
/// </summary>
public class GatewaySettings
{
    /// <summary>
    /// Whether the Gateway listener is enabled.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// The TCP port to listen on for WebSocket connections from the ND-100 emulator.
    /// </summary>
    public int Port { get; set; } = 8765;

    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private static string GetSettingsPath()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(appData, "RetroTerm", "gateway-settings.json");
    }

    /// <summary>
    /// Load settings from disk. Returns default settings if file does not exist.
    /// </summary>
    public static GatewaySettings Load()
    {
        var path = GetSettingsPath();
        if (!File.Exists(path))
            return new GatewaySettings();

        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<GatewaySettings>(json, s_jsonOptions)
                   ?? new GatewaySettings();
        }
        catch
        {
            return new GatewaySettings();
        }
    }

    /// <summary>
    /// Save settings to disk.
    /// </summary>
    public void Save()
    {
        var path = GetSettingsPath();
        var dir = Path.GetDirectoryName(path);
        if (dir != null && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        var json = JsonSerializer.Serialize(this, s_jsonOptions);
        File.WriteAllText(path, json);
    }
}
