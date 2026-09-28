using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using RetroTerm.Core.Configuration;

namespace RetroTerm.Desktop.ViewModels;

/// <summary>
/// ViewModel for ManageConnectionsWindow. Manages grouped connection list,
/// search, CRUD operations, and the current profile being edited.
/// </summary>
public class ConnectionsManagerViewModel : INotifyPropertyChanged
{
    private readonly ConfigurationManager _configManager;
    private string _searchText = "";

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// The profile currently loaded in the right editing panel.
    /// </summary>
    public ConnectionProfileViewModel CurrentProfile { get; } = new();

    /// <summary>
    /// Favorite connections (sorted alphabetically).
    /// </summary>
    public List<HostConfiguration> FavoriteConnections { get; private set; } = new();

    /// <summary>
    /// All connections (sorted alphabetically).
    /// </summary>
    public List<HostConfiguration> AllConnections { get; private set; } = new();

    /// <summary>
    /// The currently selected HostConfiguration from the list (may be null).
    /// </summary>
    public HostConfiguration? SelectedConfiguration { get; set; }

    /// <summary>
    /// Search text for filtering connections.
    /// </summary>
    public string SearchText
    {
        get => _searchText;
        set
        {
            if (_searchText != value)
            {
                _searchText = value;
                OnPropertyChanged();
                LoadConnections();
            }
        }
    }

    public ConnectionsManagerViewModel(ConfigurationManager configManager)
    {
        _configManager = configManager ?? throw new ArgumentNullException(nameof(configManager));
    }

    /// <summary>
    /// Reloads connections from ConfigurationManager, splits into favorites/all,
    /// applies search filter, and sorts alphabetically.
    /// </summary>
    public void LoadConnections()
    {
        var configs = _configManager.Configurations;
        var favorites = new List<HostConfiguration>();
        var all = new List<HostConfiguration>();

        bool hasFilter = !string.IsNullOrWhiteSpace(_searchText);

        for (int i = 0; i < configs.Count; i++)
        {
            var config = configs[i];

            if (hasFilter && config.Name.IndexOf(_searchText, StringComparison.OrdinalIgnoreCase) < 0)
                continue;

            all.Add(config);
            if (config.IsFavorite)
                favorites.Add(config);
        }

        favorites.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        all.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));

        FavoriteConnections = favorites;
        AllConnections = all;

        OnPropertyChanged(nameof(FavoriteConnections));
        OnPropertyChanged(nameof(AllConnections));
    }

    /// <summary>
    /// Loads a configuration into the CurrentProfile for editing.
    /// </summary>
    public void SelectProfile(HostConfiguration config)
    {
        SelectedConfiguration = config;
        CurrentProfile.LoadFromConfiguration(config);
        OnPropertyChanged(nameof(SelectedConfiguration));
    }

    /// <summary>
    /// Clears the CurrentProfile to defaults for creating a new profile.
    /// </summary>
    public void NewProfile()
    {
        SelectedConfiguration = null;
        CurrentProfile.LoadDefaults();
        OnPropertyChanged(nameof(SelectedConfiguration));
    }

    /// <summary>
    /// Saves the current profile (add or update) via ConfigurationManager.
    /// </summary>
    public async Task SaveCurrentProfileAsync()
    {
        var config = CurrentProfile.ToHostConfiguration();

        if (CurrentProfile.IsNew)
        {
            config.CreatedAt = DateTime.UtcNow;
            config.LastUsedAt = DateTime.UtcNow;
            await _configManager.AddAsync(config);
        }
        else
        {
            // Preserve metadata from original
            if (SelectedConfiguration != null)
            {
                config.IsFavorite = SelectedConfiguration.IsFavorite;
                config.CreatedAt = SelectedConfiguration.CreatedAt;
                config.LastUsedAt = SelectedConfiguration.LastUsedAt;
                config.UseCount = SelectedConfiguration.UseCount;
                config.Tags = SelectedConfiguration.Tags;
                config.Notes = SelectedConfiguration.Notes;
            }
            await _configManager.UpdateAsync(config);
        }

        CurrentProfile.MarkClean();
        LoadConnections();

        // Re-select the saved config
        for (int i = 0; i < AllConnections.Count; i++)
        {
            if (AllConnections[i].Id == config.Id)
            {
                SelectedConfiguration = AllConnections[i];
                CurrentProfile.LoadFromConfiguration(AllConnections[i]);
                break;
            }
        }
    }

    /// <summary>
    /// Deletes the currently selected configuration.
    /// </summary>
    public async Task DeleteSelectedAsync()
    {
        if (SelectedConfiguration == null) return;

        await _configManager.RemoveAsync(SelectedConfiguration.Id);
        SelectedConfiguration = null;
        CurrentProfile.LoadDefaults();
        LoadConnections();
        OnPropertyChanged(nameof(SelectedConfiguration));
    }

    /// <summary>
    /// Toggles the favorite status of a configuration and re-sorts lists.
    /// </summary>
    public async Task ToggleFavoriteAsync(HostConfiguration config)
    {
        config.IsFavorite = !config.IsFavorite;
        await _configManager.UpdateAsync(config);
        LoadConnections();
    }

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
