using System;
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using RetroTerm.Core.Protocols.WebSocket.Gateway;
using RetroTerm.Core.Protocols.WebSocket.Gateway.Ethernet;
using RetroTerm.Desktop.Themes;

namespace RetroTerm.Desktop.Views;

public partial class PreferencesWindow : Window
{
    private bool _initializing = true;
    private readonly List<ThemeDefinition> _themes = new();

    /// <summary>
    /// How this window reaches the running gateway, set once by MainWindow. A provider rather
    /// than a constructor argument because Preferences is opened from several places and none
    /// of them should have to know the gateway exists.
    /// </summary>
    public static Func<GatewayListener?>? GatewayListenerProvider { get; set; }

    /// <summary>The gateway's own persisted settings (enabled, port), owned by MainWindow.</summary>
    public static Func<GatewaySettings?>? GatewaySettingsProvider { get; set; }

    /// <summary>
    /// The Ethernet mappings offered, with the hint shown beneath. These are different network
    /// topologies rather than presets of one another, so each states what its parameter means -
    /// the same box is an adapter for one and a port for another.
    /// </summary>
    private readonly List<PcapEthernetBackend.AdapterInfo> _adapters = new();

    private static readonly (EthernetMappingMode Mode, string Label, string Hint)[] EthernetModes =
    {
        (EthernetMappingMode.None, "Not mapped",
            "Frames are discarded - the guest sees a dead wire."),
        (EthernetMappingMode.HostAdapter, "Bridge to a host adapter (pcap)",
            "Adapter index or part of its name. Needs npcap on Windows and puts the guest on your real LAN."),
        (EthernetMappingMode.JoinSegment, "Join a segment (TCP)",
            "host:port to dial, e.g. 127.0.0.1:3094. Blank uses 127.0.0.1:3094."),
        (EthernetMappingMode.HostSegment, "Host a segment (TCP)",
            "Port to listen on, e.g. 3094. Every member sees every other, so three machines work as readily as two."),
        (EthernetMappingMode.Multicast, "Multicast segment (UDP)",
            "group:port, e.g. 239.3.9.4:3094. No relay to start, but local network only.")
    };

    public PreferencesWindow()
    {
        InitializeComponent();
        PopulateThemes();
        LoadCurrentSettings();
        _initializing = false;
    }

    private void PopulateThemes()
    {
        var combo = this.FindControl<ComboBox>("ThemeCombo");
        if (combo == null) return;

        for (int i = 0; i < BuiltInThemes.All.Length; i++)
        {
            _themes.Add(BuiltInThemes.All[i]);
        }

        combo.ItemsSource = _themes;
        combo.ItemTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<ThemeDefinition>(
            (theme, _) => new TextBlock { Text = theme.DisplayName });
    }

    private void LoadCurrentSettings()
    {
        var combo = this.FindControl<ComboBox>("ThemeCombo");
        if (combo != null)
        {
            string currentId = ThemeManager.Instance.CurrentTheme.Id;
            for (int i = 0; i < _themes.Count; i++)
            {
                if (_themes[i].Id == currentId)
                {
                    combo.SelectedIndex = i;
                    break;
                }
            }
        }

        var autoClose = this.FindControl<CheckBox>("AutoCloseCheck");
        if (autoClose != null)
            autoClose.IsChecked = ThemeManager.Instance.AutoCloseTransferWindow;

        // Kermit settings
        var tm = ThemeManager.Instance;
        var parityCombo = this.FindControl<ComboBox>("KermitParityCombo");
        if (parityCombo != null) parityCombo.SelectedIndex = tm.KermitParity;

        var force8bit = this.FindControl<CheckBox>("KermitForce8BitCheck");
        if (force8bit != null) force8bit.IsChecked = tm.KermitForce8BitQuoting;

        var delayUpDown = this.FindControl<NumericUpDown>("KermitDelayUpDown");
        if (delayUpDown != null) delayUpDown.Value = tm.KermitDelay;

        var timeoutUpDown = this.FindControl<NumericUpDown>("KermitTimeoutUpDown");
        if (timeoutUpDown != null) timeoutUpDown.Value = tm.KermitTimeout;

        LoadGatewaySettings();

        var retriesUpDown = this.FindControl<NumericUpDown>("KermitRetriesUpDown");
        if (retriesUpDown != null) retriesUpDown.Value = tm.KermitMaxRetries;

        var collisionCombo = this.FindControl<ComboBox>("KermitFileCollisionCombo");
        if (collisionCombo != null) collisionCombo.SelectedIndex = tm.KermitFileCollision;

        var blockCheckCombo = this.FindControl<ComboBox>("KermitBlockCheckCombo");
        if (blockCheckCombo != null) blockCheckCombo.SelectedIndex = Math.Max(0, tm.KermitBlockCheckType - 1);

        var packetSizeUpDown = this.FindControl<NumericUpDown>("KermitPacketSizeUpDown");
        if (packetSizeUpDown != null) packetSizeUpDown.Value = tm.KermitPacketSize;

        var autoDetect = this.FindControl<CheckBox>("KermitAutoDetectCheck");
        if (autoDetect != null) autoDetect.IsChecked = tm.KermitAutoDetectReceive;

        // MCP settings
        var mcpEnabled = this.FindControl<CheckBox>("McpEnabledCheck");
        if (mcpEnabled != null) mcpEnabled.IsChecked = tm.McpEnabled;

        var mcpPort = this.FindControl<NumericUpDown>("McpPortUpDown");
        if (mcpPort != null) mcpPort.Value = tm.McpPort;

        UpdateMcpInfoText();

        UpdatePreview(ThemeManager.Instance.CurrentTheme);
    }

    private void OnThemeSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_initializing) return;

        var combo = this.FindControl<ComboBox>("ThemeCombo");
        if (combo?.SelectedItem is ThemeDefinition theme)
        {
            ThemeManager.Instance.ApplyTheme(theme);
            UpdatePreview(theme);
        }
    }

    private void OnAutoCloseChanged(object? sender, RoutedEventArgs e)
    {
        if (_initializing) return;

        var autoClose = this.FindControl<CheckBox>("AutoCloseCheck");
        if (autoClose != null)
        {
            ThemeManager.Instance.AutoCloseTransferWindow = autoClose.IsChecked == true;
            // Save immediately by re-applying current theme (triggers save)
            ThemeManager.Instance.ApplyTheme(ThemeManager.Instance.CurrentTheme);
        }
    }

    private void UpdatePreview(ThemeDefinition theme)
    {
        var accent = this.FindControl<Border>("PreviewAccent");
        var success = this.FindControl<Border>("PreviewSuccess");
        var warning = this.FindControl<Border>("PreviewWarning");
        var error = this.FindControl<Border>("PreviewError");

        if (accent != null) accent.Background = new SolidColorBrush(theme.Accent);
        if (success != null) success.Background = new SolidColorBrush(theme.Success);
        if (warning != null) warning.Background = new SolidColorBrush(theme.Warning);
        if (error != null) error.Background = new SolidColorBrush(theme.Error);
    }

    private void OnKermitSettingChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_initializing) return;
        SaveKermitSettings();
    }

    private void OnKermitCheckChanged(object? sender, RoutedEventArgs e)
    {
        if (_initializing) return;
        SaveKermitSettings();
    }

    private void OnKermitNumericChanged(object? sender, NumericUpDownValueChangedEventArgs e)
    {
        if (_initializing) return;
        SaveKermitSettings();
    }

    private void SaveKermitSettings()
    {
        var tm = ThemeManager.Instance;

        var parityCombo = this.FindControl<ComboBox>("KermitParityCombo");
        if (parityCombo != null) tm.KermitParity = parityCombo.SelectedIndex;

        var force8bit = this.FindControl<CheckBox>("KermitForce8BitCheck");
        if (force8bit != null) tm.KermitForce8BitQuoting = force8bit.IsChecked == true;

        var delayUpDown = this.FindControl<NumericUpDown>("KermitDelayUpDown");
        if (delayUpDown != null) tm.KermitDelay = (int)(delayUpDown.Value ?? 0);

        var timeoutUpDown = this.FindControl<NumericUpDown>("KermitTimeoutUpDown");
        if (timeoutUpDown != null) tm.KermitTimeout = (int)(timeoutUpDown.Value ?? 8);

        var retriesUpDown = this.FindControl<NumericUpDown>("KermitRetriesUpDown");
        if (retriesUpDown != null) tm.KermitMaxRetries = (int)(retriesUpDown.Value ?? 10);

        var collisionCombo = this.FindControl<ComboBox>("KermitFileCollisionCombo");
        if (collisionCombo != null) tm.KermitFileCollision = collisionCombo.SelectedIndex;

        var blockCheckCombo = this.FindControl<ComboBox>("KermitBlockCheckCombo");
        if (blockCheckCombo != null) tm.KermitBlockCheckType = blockCheckCombo.SelectedIndex + 1;

        var packetSizeUpDown = this.FindControl<NumericUpDown>("KermitPacketSizeUpDown");
        if (packetSizeUpDown != null) tm.KermitPacketSize = (int)(packetSizeUpDown.Value ?? 80);

        var autoDetect = this.FindControl<CheckBox>("KermitAutoDetectCheck");
        if (autoDetect != null) tm.KermitAutoDetectReceive = autoDetect.IsChecked == true;

        tm.SavePreferences();
    }

    // ── MCP ─────────────────────────────────────────────────────────

    private void OnMcpSettingChanged(object? sender, RoutedEventArgs e)
    {
        if (_initializing) return;
        SaveMcpSettings();
    }

    private void OnMcpPortChanged(object? sender, NumericUpDownValueChangedEventArgs e)
    {
        if (_initializing) return;
        SaveMcpSettings();
        UpdateMcpInfoText();
    }

    private void SaveMcpSettings()
    {
        var tm = ThemeManager.Instance;

        var mcpEnabled = this.FindControl<CheckBox>("McpEnabledCheck");
        if (mcpEnabled != null) tm.McpEnabled = mcpEnabled.IsChecked == true;

        var mcpPort = this.FindControl<NumericUpDown>("McpPortUpDown");
        if (mcpPort != null) tm.McpPort = (int)(mcpPort.Value ?? 5715);

        tm.SavePreferences();
    }

    /// <summary>
    /// Keeps the connect-command hint in step with the chosen port.
    /// </summary>
    private void UpdateMcpInfoText()
    {
        var info = this.FindControl<TextBlock>("McpInfoText");
        if (info == null) return;

        int port = ThemeManager.Instance.McpPort;
        info.Text =
            "The MCP server listens on localhost only. Changes take effect the next time " +
            "RetroTerm starts. " +
            $"Connect Claude Code with: claude mcp add --transport http retroterm http://127.0.0.1:{port}/mcp " +
            "— see docs\\MCP-AND-SCRIPTING.md.";
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    // ── Gateway ─────────────────────────────────────────────────────

    private void LoadGatewaySettings()
    {
        var settings = GatewaySettingsProvider?.Invoke();

        var enabled = this.FindControl<CheckBox>("GatewayEnabledCheck");
        if (enabled != null) enabled.IsChecked = settings?.Enabled == true;

        var port = this.FindControl<NumericUpDown>("GatewayPortUpDown");
        if (port != null) port.Value = settings?.Port ?? 8765;

        var tm = ThemeManager.Instance;

        var ethEnabled = this.FindControl<CheckBox>("EthernetEnabledCheck");
        if (ethEnabled != null) ethEnabled.IsChecked = tm.EthernetEnabled;

        var target = this.FindControl<TextBox>("EthernetTargetBox");
        if (target != null) target.Text = tm.EthernetTarget;

        PopulateAdapters(tm.EthernetTarget);

        var combo = this.FindControl<ComboBox>("EthernetModeCombo");
        if (combo != null)
        {
            var labels = new string[EthernetModes.Length];
            int selected = 0;
            for (int i = 0; i < EthernetModes.Length; i++)
            {
                labels[i] = EthernetModes[i].Label;
                if (EthernetModes[i].Mode == tm.EthernetMapping) selected = i;
            }

            combo.ItemsSource = labels;
            combo.SelectedIndex = selected;
            UpdateEthernetControls(selected);
        }
    }

    /// <summary>
    /// Fill the adapter list from the machine's real adapters. The DESCRIPTION is shown and the
    /// system NAME is stored: on Windows the name is a GUID, which nobody can type and nobody
    /// should have to, while a description can change when a driver is updated.
    /// </summary>
    private void PopulateAdapters(string storedName)
    {
        var combo = this.FindControl<ComboBox>("EthernetAdapterCombo");
        if (combo == null) return;

        _adapters.Clear();
        var found = PcapEthernetBackend.ListInterfaces();
        for (int i = 0; i < found.Count; i++)
        {
            _adapters.Add(found[i]);
        }

        if (_adapters.Count == 0)
        {
            // Say why the list is empty instead of showing an empty dropdown.
            combo.ItemsSource = new[] { "No capture driver found - install npcap" };
            combo.SelectedIndex = 0;
            combo.IsEnabled = false;
            return;
        }

        var descriptions = new string[_adapters.Count];
        int selected = -1;
        for (int i = 0; i < _adapters.Count; i++)
        {
            descriptions[i] = _adapters[i].Description;
            if (string.Equals(_adapters[i].Name, storedName, StringComparison.OrdinalIgnoreCase))
            {
                selected = i;
            }
        }

        combo.IsEnabled = true;
        combo.ItemsSource = descriptions;

        // An adapter that is configured but not present now - a USB dongle unplugged, say -
        // must not silently become "the first one in the list".
        combo.SelectedIndex = selected >= 0 ? selected : (storedName.Length == 0 ? 0 : -1);
    }

    /// <summary>Show the control the chosen mapping actually needs, and name it correctly. The
    /// same slot is an adapter for one mapping, a host:port for another and a bare port for a
    /// third, so a single label like "target" would be wrong in most of them.</summary>
    private void UpdateEthernetControls(int index)
    {
        UpdateEthernetHint(index);

        var label = this.FindControl<TextBlock>("EthernetTargetLabel");
        var adapterCombo = this.FindControl<ComboBox>("EthernetAdapterCombo");
        var targetBox = this.FindControl<TextBox>("EthernetTargetBox");
        if (label == null || adapterCombo == null || targetBox == null) return;

        EthernetMappingMode mode = index >= 0 && index < EthernetModes.Length
            ? EthernetModes[index].Mode
            : EthernetMappingMode.None;

        bool isAdapter = mode == EthernetMappingMode.HostAdapter;
        bool needsParameter = mode != EthernetMappingMode.None;

        label.IsVisible = needsParameter;
        adapterCombo.IsVisible = needsParameter && isAdapter;
        targetBox.IsVisible = needsParameter && !isAdapter;

        label.Text = mode switch
        {
            EthernetMappingMode.HostAdapter => "Adapter:",
            EthernetMappingMode.JoinSegment => "Address:",
            EthernetMappingMode.HostSegment => "Listen port:",
            EthernetMappingMode.Multicast => "Group:",
            _ => string.Empty
        };
    }

    private void UpdateEthernetHint(int index)
    {
        var hint = this.FindControl<TextBlock>("EthernetHintText");
        if (hint != null && index >= 0 && index < EthernetModes.Length)
        {
            hint.Text = EthernetModes[index].Hint;
        }
    }

    private void OnGatewaySettingChanged(object? sender, RoutedEventArgs e)
    {
        if (_initializing) return;
        ApplyGatewaySettings();
    }

    private void OnGatewayPortChanged(object? sender, NumericUpDownValueChangedEventArgs e)
    {
        if (_initializing) return;
        ApplyGatewaySettings();
    }

    private void OnEthernetSettingChanged(object? sender, RoutedEventArgs e)
    {
        if (_initializing) return;
        ApplyEthernetSettings();
    }

    private void OnEthernetModeChanged(object? sender, SelectionChangedEventArgs e)
    {
        var combo = this.FindControl<ComboBox>("EthernetModeCombo");
        if (combo != null) UpdateEthernetControls(combo.SelectedIndex);

        if (_initializing) return;
        ApplyEthernetSettings();
    }

    /// <summary>Persist the gateway's own settings and start or stop it to match. Restarting is
    /// the only way a port change can take effect, and doing it here means the user does not
    /// have to know that.</summary>
    private async void ApplyGatewaySettings()
    {
        var settings = GatewaySettingsProvider?.Invoke();
        var listener = GatewayListenerProvider?.Invoke();
        if (settings == null) return;

        var enabledCheck = this.FindControl<CheckBox>("GatewayEnabledCheck");
        var portUpDown = this.FindControl<NumericUpDown>("GatewayPortUpDown");

        bool enabled = enabledCheck?.IsChecked == true;
        int port = (int)(portUpDown?.Value ?? 8765);
        bool changed = enabled != settings.Enabled || port != settings.Port;

        settings.Enabled = enabled;
        settings.Port = port;
        settings.Save();

        if (listener == null || !changed) return;

        try
        {
            if (listener.IsListening) await listener.StopAsync();
            if (enabled) await listener.StartAsync(port);
        }
        catch (Exception)
        {
            // A port already in use is the common case; the gateway status line reports it.
        }
    }

    private void ApplyEthernetSettings()
    {
        var tm = ThemeManager.Instance;

        var enabled = this.FindControl<CheckBox>("EthernetEnabledCheck");
        var combo = this.FindControl<ComboBox>("EthernetModeCombo");
        var target = this.FindControl<TextBox>("EthernetTargetBox");

        tm.EthernetEnabled = enabled?.IsChecked == true;

        int index = combo?.SelectedIndex ?? 0;
        tm.EthernetMapping = index >= 0 && index < EthernetModes.Length
            ? EthernetModes[index].Mode
            : EthernetMappingMode.None;

        // Read the parameter from the control the mode actually uses. The adapter's stored
        // value is its system name, never the description on screen.
        if (tm.EthernetMapping == EthernetMappingMode.HostAdapter)
        {
            var adapterCombo = this.FindControl<ComboBox>("EthernetAdapterCombo");
            int adapterIndex = adapterCombo?.SelectedIndex ?? -1;
            tm.EthernetTarget = adapterIndex >= 0 && adapterIndex < _adapters.Count
                ? _adapters[adapterIndex].Name
                : string.Empty;
        }
        else
        {
            tm.EthernetTarget = target?.Text?.Trim() ?? string.Empty;
        }
        tm.SavePreferences();

        // Applied immediately rather than at the next start: changing where the guest's wire
        // goes should not need the gateway stopped and started.
        GatewayListenerProvider?.Invoke()?.ConfigureEthernet(tm.BuildEthernetSpec(), tm.EthernetSegment);
    }
}
