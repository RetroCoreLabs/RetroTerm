using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace RetroTerm.Core.Configuration;

/// <summary>
/// Manages host configurations with persistence to disk
/// </summary>
public class ConfigurationManager
{
    private readonly string _configFilePath;
    private readonly JsonSerializerOptions _jsonOptions;
    private List<HostConfiguration> _configurations;
    private bool _isLoaded;

    /// <summary>
    /// Event raised when configurations are modified
    /// </summary>
    public event Action? ConfigurationsChanged;

    /// <summary>
    /// Gets all configurations
    /// </summary>
    public IReadOnlyList<HostConfiguration> Configurations => _configurations.AsReadOnly();

    /// <summary>
    /// Gets favorite configurations
    /// </summary>
    public IEnumerable<HostConfiguration> Favorites => _configurations.Where(c => c.IsFavorite);

    /// <summary>
    /// Gets recently used configurations (last 10)
    /// </summary>
    public IEnumerable<HostConfiguration> Recent => _configurations
        .Where(c => c.LastUsedAt != null)
        .OrderByDescending(c => c.LastUsedAt)
        .Take(10);

    public ConfigurationManager(string? configFilePath = null)
    {
        _configFilePath = configFilePath ?? GetDefaultConfigPath();
        _jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            // Replaces the obsolete IgnoreNullValues (SYSLIB0020); same behaviour on write.
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };
        _configurations = new List<HostConfiguration>();
    }

    /// <summary>
    /// Loads configurations from disk
    /// </summary>
    public async Task LoadAsync(bool createDefaults = false)
    {
        if (_isLoaded) return;

        try
        {
            if (File.Exists(_configFilePath))
            {
                var json = await File.ReadAllTextAsync(_configFilePath);
                var configs = JsonSerializer.Deserialize<List<HostConfiguration>>(json, _jsonOptions);
                _configurations = configs ?? new List<HostConfiguration>();
                if (MigrateSettings(_configurations))
                {
                    await SaveAsync();
                }
            }
            else
            {
                _configurations = new List<HostConfiguration>();
                if (createDefaults)
                {
                    await CreateDefaultConfigurations();
                }
            }
        }
        catch (Exception)
        {
            // If loading fails, start with empty list
            _configurations = new List<HostConfiguration>();
            if (createDefaults)
            {
                await CreateDefaultConfigurations();
            }
        }

        _isLoaded = true;
    }

    /// <summary>
    /// The settings version this build writes.
    /// </summary>
    /// <remarks>
    /// See <see cref="HostConfiguration.SettingsVersion"/> for what each number means.
    /// </remarks>
    public const int CurrentSettingsVersion = 1;

    /// <summary>
    /// Marks every record as written under <see cref="CurrentSettingsVersion"/>.
    /// </summary>
    /// <param name="configurations">
    /// The records about to be written to disk.
    /// </param>
    public static void StampCurrentSettingsVersion(List<HostConfiguration> configurations)
    {
        for (int i = 0; i < configurations.Count; i++)
        {
            configurations[i].SettingsVersion = CurrentSettingsVersion;
        }
    }

    /// <summary>
    /// Brings records written under an older set of defaults up to the current one.
    /// </summary>
    /// <remarks>
    /// <para><b>Version 0 to 1, 27 September 2026</b></para>
    /// Two things were wrong in every file written before this date, and both were the program's
    /// doing rather than the user's, which is why they are corrected here rather than left:
    /// <para>
    /// Backspace. The choice used to be a plain bool that defaulted to false and was written for
    /// every connection, so a file cannot tell "the user unticked it" from "nobody looked". Since
    /// nobody could have chosen the default state on purpose (there was no other way to leave the
    /// box), false is read as "not chosen" and becomes null, which means the terminal's own
    /// default - DEL on a TDV, BS on a VT. A true stays true. A VT connection is unaffected either
    /// way, because its default is the BS it already sent.
    /// </para>
    /// <para>
    /// Rows. A fixed-screen terminal (one without TerminalFeatures.HostResize) saved with 24 rows
    /// when its profile says 25 was sized by the dialog's default, not by anyone deciding a TDV
    /// should be 24 lines tall - the dialog did not change the size when the type was picked. Only
    /// the row count is touched and only that one case: columns are left alone, because a TDV at
    /// 132 columns is a documented way to ask for SINTRAN's wide mode.
    /// </para>
    /// The version is stamped on each record so this runs once per record, and a false written
    /// by the three-state checkbox after this date is an explicit choice that is never touched.
    /// </remarks>
    /// <param name="configurations">
    /// The records just read from the file. Changed in place.
    /// </param>
    /// <returns>
    /// True when any record changed, so the caller writes the file back; false when all were current.
    /// </returns>
    public static bool MigrateSettings(List<HostConfiguration> configurations)
    {
        bool changed = false;
        for (int i = 0; i < configurations.Count; i++)
        {
            var config = configurations[i];
            if (config.SettingsVersion >= CurrentSettingsVersion) continue;

            if (config.BackspaceSendsDel == false)
            {
                config.BackspaceSendsDel = null;
            }

            if (!string.IsNullOrEmpty(config.EmulatorType))
            {
                try
                {
                    var profile = EmulatorFactory.GetProfile(config.EmulatorType);
                    if (!profile.Supports(Terminal.Profiles.TerminalFeatures.HostResize)
                        && config.Height == 24 && profile.Rows == 25)
                    {
                        config.Height = 25;
                    }
                }
                catch (NotSupportedException)
                {
                    // A type this build cannot make keeps whatever size it was saved with.
                }
            }

            config.SettingsVersion = 1;
            changed = true;
        }
        return changed;
    }

    /// <summary>
    /// Saves configurations to disk
    /// </summary>
    public async Task SaveAsync()
    {
        try
        {
            var directory = Path.GetDirectoryName(_configFilePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            // Everything in memory already has the current meaning - migrated on load or built
            // new - so it is stamped as such on the way out. Done here, at the one place the file
            // is written, rather than at every place a record is built.
            StampCurrentSettingsVersion(_configurations);
            var json = JsonSerializer.Serialize(_configurations, _jsonOptions);
            await File.WriteAllTextAsync(_configFilePath, json);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to save configurations: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Adds a new configuration
    /// </summary>
    public async Task AddAsync(HostConfiguration configuration)
    {
        await EnsureLoadedAsync();

        if (string.IsNullOrWhiteSpace(configuration.Name))
        {
            throw new ArgumentException("Configuration name cannot be empty", nameof(configuration));
        }

        // Check for duplicate names
        if (_configurations.Any(c => c.Name.Equals(configuration.Name, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException($"A configuration with name '{configuration.Name}' already exists");
        }

        _configurations.Add(configuration);
        await SaveAsync();
        ConfigurationsChanged?.Invoke();
    }

    /// <summary>
    /// Updates an existing configuration
    /// </summary>
    public async Task UpdateAsync(HostConfiguration configuration)
    {
        await EnsureLoadedAsync();

        var existing = _configurations.FirstOrDefault(c => c.Id == configuration.Id);
        if (existing == null)
        {
            throw new InvalidOperationException($"Configuration with ID '{configuration.Id}' not found");
        }

        var index = _configurations.IndexOf(existing);
        _configurations[index] = configuration;

        await SaveAsync();
        ConfigurationsChanged?.Invoke();
    }

    /// <summary>
    /// Removes a configuration
    /// </summary>
    public async Task RemoveAsync(string id)
    {
        await EnsureLoadedAsync();

        var configuration = _configurations.FirstOrDefault(c => c.Id == id);
        if (configuration == null)
        {
            throw new InvalidOperationException($"Configuration with ID '{id}' not found");
        }

        _configurations.Remove(configuration);
        await SaveAsync();
        ConfigurationsChanged?.Invoke();
    }

    /// <summary>
    /// Gets a configuration by ID
    /// </summary>
    public async Task<HostConfiguration?> GetByIdAsync(string id)
    {
        await EnsureLoadedAsync();
        return _configurations.FirstOrDefault(c => c.Id == id);
    }

    /// <summary>
    /// Gets configurations by name (case-insensitive partial match)
    /// </summary>
    public async Task<IEnumerable<HostConfiguration>> SearchByNameAsync(string name)
    {
        await EnsureLoadedAsync();
        return _configurations.Where(c =>
            c.Name.Contains(name, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Gets configurations by tag
    /// </summary>
    public async Task<IEnumerable<HostConfiguration>> GetByTagAsync(string tag)
    {
        await EnsureLoadedAsync();
        return _configurations.Where(c =>
            c.Tags.Contains(tag, StringComparer.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Marks a configuration as used (updates timestamp and use count)
    /// </summary>
    public async Task MarkAsUsedAsync(string id)
    {
        await EnsureLoadedAsync();

        var configuration = _configurations.FirstOrDefault(c => c.Id == id);
        if (configuration != null)
        {
            configuration.MarkAsUsed();
            await SaveAsync();
        }
    }

    /// <summary>
    /// Duplicates a configuration with a new name
    /// </summary>
    public async Task<HostConfiguration> DuplicateAsync(string id, string newName)
    {
        await EnsureLoadedAsync();

        var original = _configurations.FirstOrDefault(c => c.Id == id);
        if (original == null)
        {
            throw new InvalidOperationException($"Configuration with ID '{id}' not found");
        }

        var duplicate = original.Clone();
        duplicate.Name = newName;

        await AddAsync(duplicate);
        return duplicate;
    }

    /// <summary>
    /// Exports configurations to a file
    /// </summary>
    public async Task ExportAsync(string filePath)
    {
        await EnsureLoadedAsync();

        StampCurrentSettingsVersion(_configurations); // an export is a file too
        var json = JsonSerializer.Serialize(_configurations, _jsonOptions);
        await File.WriteAllTextAsync(filePath, json);
    }

    /// <summary>
    /// Imports configurations from a file
    /// </summary>
    public async Task ImportAsync(string filePath, bool overwriteExisting = false)
    {
        await EnsureLoadedAsync();

        var json = await File.ReadAllTextAsync(filePath);
        var importedConfigs = JsonSerializer.Deserialize<List<HostConfiguration>>(json, _jsonOptions);

        if (importedConfigs == null) return;

        foreach (var config in importedConfigs)
        {
            // Generate new ID to avoid conflicts
            config.Id = Guid.NewGuid().ToString();

            if (overwriteExisting)
            {
                var existing = _configurations.FirstOrDefault(c => c.Name == config.Name);
                if (existing != null)
                {
                    _configurations.Remove(existing);
                }
            }

            _configurations.Add(config);
        }

        await SaveAsync();
        ConfigurationsChanged?.Invoke();
    }

    private async Task EnsureLoadedAsync()
    {
        if (!_isLoaded)
        {
            await LoadAsync();
        }
    }

    private async Task CreateDefaultConfigurations()
    {
        // Create some default configurations
        var defaults = new[]
        {
            new HostConfiguration
            {
                Name = "Local Telnet Server",
                Host = "localhost",
                Port = 8080,
                Protocol = "Telnet",
                EmulatorType = "VT100",
                Tags = new List<string> { "local", "development" }
            },
            new HostConfiguration
            {
                Name = "Local SSH Server",
                Host = "localhost",
                Port = 22,
                Protocol = "SSH",
                Username = Environment.UserName,
                EmulatorType = "VT100",
                Tags = new List<string> { "local", "ssh" }
            },
            new HostConfiguration
            {
                Name = "TDV Test Server",
                Host = "localhost",
                Port = 8080,
                Protocol = "Telnet",
                EmulatorType = "TDV1200",
                // The terminal's own screen. This record used to take HostConfiguration's 80 by 24
                // default, so the sample TDV was one row short from its first run.
                Width = EmulatorFactory.GetRecommendedSize("TDV1200").width,
                Height = EmulatorFactory.GetRecommendedSize("TDV1200").height,
                Tags = new List<string> { "tdv", "test" },
                IsFavorite = true
            }
        };

        foreach (var config in defaults)
        {
            _configurations.Add(config);
        }

        await SaveAsync();
    }

    private static string GetDefaultConfigPath()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var configDir = Path.Combine(appData, "RetroTerm");
        return Path.Combine(configDir, "host-configurations.json");
    }
}
