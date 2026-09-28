using System;
using System.Collections.Generic;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using RetroTerm.Core.Terminal.Emulators.TDV;
using RetroTerm.Core.Terminal.Input;

namespace RetroTerm.Desktop.Views;

public partial class KeyboardReferenceWindow : Window
{
    private readonly string _emulatorType;

    public KeyboardReferenceWindow() : this("TDV2200")
    {
    }

    public KeyboardReferenceWindow(string emulatorType)
    {
        _emulatorType = emulatorType;

        AvaloniaXamlLoader.Load(this);

        Title = $"Keyboard Reference - {emulatorType}";

        var windowTitle = this.FindControl<TextBlock>("WindowTitle");
        if (windowTitle != null)
        {
            windowTitle.Text = $"Keyboard Reference - {emulatorType}";
        }

        PopulateKeyMappings();
    }

    private void PopulateKeyMappings()
    {
        if (_emulatorType.Contains("TDV"))
        {
            PopulateTDVKeys();
        }
        else if (_emulatorType.Contains("VT"))
        {
            PopulateVTKeys();
        }
        else
        {
            PopulateTDVKeys(); // Default to TDV
        }
    }

    /// <summary>
    /// Fills the four TDV tabs from TDV2200KeyRegistry, so this window can only ever say what the
    /// keyboard actually sends.
    /// </summary>
    /// <remarks>
    /// Until 27 September 2026 these tables were typed in by hand and every sequence in them was
    /// VT-style (ESC[28~, ESC[M, ESC[5~ ...) - not one matched the CSI nn _ the registry sends, and
    /// the Application tab listed an Alt+C binding that has never existed. The registry is the
    /// single source of truth for the keyboard, and a second copy of it in a window is exactly the
    /// kind of thing that drifts. Everything shown here is now read from it at open time.
    /// </remarks>
    private void PopulateTDVKeys()
    {
        // Application tab: every default Alt binding that is not a PUSH key.
        var appGrid = this.FindControl<Grid>("ApplicationKeysGrid");
        if (appGrid != null)
        {
            var rows = new List<(string key, string seq, string desc)>();
            var altMap = TDV2200KeyRegistry.DefaultAltMappings;
            var vks = new List<int>(altMap.Keys);
            vks.Sort();
            for (int i = 0; i < vks.Count; i++)
            {
                var grid = altMap[vks[i]];
                if (!TDV2200KeyRegistry.TryGetKey(grid, out var def)) continue;
                if ((def.Flags & TDVKeyFlags.IsProgrammable) != 0) continue;
                var seq = TDV2200KeyRegistry.GetSequence(grid, true, false);
                rows.Add(("Alt+" + TDVKeyBindingConfiguration.GetVKCodeName(vks[i]),
                    ToVisible(seq), DescribeKey(grid, def)));
            }
            FillGrid(appGrid, rows);
        }

        // Editing tab: every plain PC key that reaches a TDV key through the VK codes in the registry.
        var editGrid = this.FindControl<Grid>("EditingKeysGrid");
        if (editGrid != null)
        {
            var rows = new List<(string key, string seq, string desc)>();
            var keys = TDV2200KeyRegistry.AllKeys;
            var grids = new List<string>(keys.Keys);
            for (int i = 0; i < grids.Count; i++)
            {
                var def = keys[grids[i]];
                if (def.VirtualKeyCode == 0) continue;
                if ((def.Flags & TDVKeyFlags.IsProgrammable) != 0) continue;
                // Only the key the VK actually resolves to (registration is first-wins).
                if (TDV2200KeyRegistry.GetGridForVK(def.VirtualKeyCode) != grids[i]) continue;
                var seq = TDV2200KeyRegistry.GetSequence(grids[i], true, false);
                if (string.IsNullOrEmpty(seq)) continue; // plain character keys type their own text
                rows.Add((TDVKeyBindingConfiguration.GetVKCodeName(def.VirtualKeyCode),
                    ToVisible(seq), DescribeKey(grids[i], def)));
            }
            rows.Sort(static (a, b) => string.CompareOrdinal(a.desc, b.desc));
            FillGrid(editGrid, rows);
        }

        // PUSH tab: the programmable keys and every Alt combination that reaches them.
        var pushGrid = this.FindControl<Grid>("PushKeysGrid");
        if (pushGrid != null)
        {
            var rows = new List<(string key, string seq, string desc)>();
            for (int n = 1; n <= 8; n++)
            {
                var grid = "G" + n;
                var stored = TDVPushKeyConfiguration.Instance.GetKeyString(grid, false);
                rows.Add(($"Alt+{n} / Alt+F{n}",
                    string.IsNullOrEmpty(stored) ? "(not programmed)" : ToVisible(stored),
                    $"PUSH {n} (P{n}, {grid}) - the string stored on the key"));
            }
            for (int n = 1; n <= 8; n++)
            {
                var grid = "G" + n;
                var stored = TDVPushKeyConfiguration.Instance.GetKeyString(grid, true);
                rows.Add(($"Alt+Shift+F{n}",
                    string.IsNullOrEmpty(stored) ? "(not programmed)" : ToVisible(stored),
                    $"PUSH {n + 8} (P{n + 8}, shifted {grid})"));
            }
            FillGrid(pushGrid, rows);
        }

        // Navigation tab: the fixed keys that send one C0 byte in every mode.
        var navGrid = this.FindControl<Grid>("NavigationKeysGrid");
        if (navGrid != null)
        {
            var rows = new List<(string key, string seq, string desc)>();
            var keys = TDV2200KeyRegistry.AllKeys;
            var grids = new List<string>(keys.Keys);
            for (int i = 0; i < grids.Count; i++)
            {
                var def = keys[grids[i]];
                if ((def.Flags & TDVKeyFlags.AlwaysSameCode) == 0) continue;
                var seq = TDV2200KeyRegistry.GetSequence(grids[i], true, false);
                if (string.IsNullOrEmpty(seq)) continue;
                var pcKey = def.VirtualKeyCode == 0
                    ? "(virtual keyboard only)"
                    : TDVKeyBindingConfiguration.GetVKCodeName(def.VirtualKeyCode);
                rows.Add((pcKey, ToVisible(seq), DescribeKey(grids[i], def) + " - same byte in both modes"));
            }
            rows.Sort(static (a, b) => string.CompareOrdinal(a.desc, b.desc));
            FillGrid(navGrid, rows);
        }
    }

    /// <summary>
    /// Builds the three-column layout and adds a header plus one row per entry.
    /// </summary>
    /// <param name="grid">
    /// The empty grid from the window's XAML.
    /// </param>
    /// <param name="rows">
    /// One entry per row: the PC key, the bytes it sends, and what the key is.
    /// </param>
    private void FillGrid(Grid grid, List<(string key, string seq, string desc)> rows)
    {
        grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto)); // Header
        for (int i = 0; i < rows.Count; i++) grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(150)));
        grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(150)));
        grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));

        AddKeyHeader(grid, 0);
        for (int i = 0; i < rows.Count; i++)
        {
            AddKeyRow(grid, i + 1, rows[i].key, rows[i].seq, rows[i].desc);
        }
    }

    /// <summary>
    /// Names a TDV key by its registry name and grid position, for example "HJELP (G53)".
    /// </summary>
    /// <remarks>
    /// The same words the virtual keyboard and the binding popup use.
    /// </remarks>
    /// <param name="grid">
    /// The grid position, for example G53.
    /// </param>
    /// <param name="def">
    /// The registry entry for that position.
    /// </param>
    /// <returns>
    /// The name followed by the grid position in brackets.
    /// </returns>
    private static string DescribeKey(string grid, TDVKeyDefinition def)
    {
        return $"{def.Name} ({grid})";
    }

    /// <summary>
    /// Renders a sequence the way the manuals print it: ESC and other control bytes by name,
    /// printable text as it is.
    /// </summary>
    /// <param name="seq">
    /// The bytes a key sends, or null when it sends nothing.
    /// </param>
    /// <returns>
    /// Readable text such as "ESC [28_" or "0x06", or "(none)" for an empty sequence.
    /// </returns>
    private static string ToVisible(string? seq)
    {
        if (string.IsNullOrEmpty(seq)) return "(none)";
        var sb = new StringBuilder(seq.Length * 3);
        for (int i = 0; i < seq.Length; i++)
        {
            char c = seq[i];
            if (c == 0x1B) sb.Append("ESC ");
            else if (c < 0x20) sb.Append("0x").Append(((int)c).ToString("X2")).Append(' ');
            else if (c == 0x7F) sb.Append("DEL ");
            else sb.Append(c);
        }
        return sb.ToString().TrimEnd();
    }


    private void PopulateVTKeys()
    {
        // VT100/VT220 key mappings
        var navGrid = this.FindControl<Grid>("NavigationKeysGrid");
        if (navGrid != null)
        {
            navGrid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            for (int i = 0; i < 10; i++) navGrid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            navGrid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(150)));
            navGrid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(150)));
            navGrid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));

            AddKeyHeader(navGrid, 0);

            var vtKeys = new[]
            {
                ("Up Arrow", "ESC[A", "Cursor up"),
                ("Down Arrow", "ESC[B", "Cursor down"),
                ("Right Arrow", "ESC[C", "Cursor forward"),
                ("Left Arrow", "ESC[D", "Cursor backward"),
                ("F1-F4", "ESC O P-S", "Function keys (app mode)"),
                ("F5-F10", "ESC[15~-21~", "Function keys"),
                ("F11-F20", "ESC[23~-34~", "Extended function keys"),
                ("Home", "ESC[1~", "Cursor to home"),
                ("End", "ESC[4~", "Cursor to end"),
                ("Page Up/Down", "ESC[5~/6~", "Scroll pages")
            };

            for (int i = 0; i < vtKeys.Length; i++)
            {
                AddKeyRow(navGrid, i + 1, vtKeys[i].Item1, vtKeys[i].Item2, vtKeys[i].Item3);
            }
        }

        // Hide TDV-specific tabs for VT terminals
        // For VT terminals, we only show the Navigation tab
        // TDV-specific tabs (Application, Editing, PUSH) are not populated
    }

    private void AddKeyHeader(Grid grid, int row)
    {
        var keyHeader = new TextBlock
        {
            Text = "Key",
            FontWeight = FontWeight.Bold,
            Foreground = new SolidColorBrush(Color.Parse("#CCCCCC")),
            Margin = new Thickness(0, 0, 0, 10)
        };
        Grid.SetRow(keyHeader, row);
        Grid.SetColumn(keyHeader, 0);
        grid.Children.Add(keyHeader);

        var seqHeader = new TextBlock
        {
            Text = "Sequence",
            FontWeight = FontWeight.Bold,
            Foreground = new SolidColorBrush(Color.Parse("#CCCCCC")),
            Margin = new Thickness(0, 0, 0, 10)
        };
        Grid.SetRow(seqHeader, row);
        Grid.SetColumn(seqHeader, 1);
        grid.Children.Add(seqHeader);

        var descHeader = new TextBlock
        {
            Text = "Description",
            FontWeight = FontWeight.Bold,
            Foreground = new SolidColorBrush(Color.Parse("#CCCCCC")),
            Margin = new Thickness(0, 0, 0, 10)
        };
        Grid.SetRow(descHeader, row);
        Grid.SetColumn(descHeader, 2);
        grid.Children.Add(descHeader);
    }

    private void AddKeyRow(Grid grid, int row, string keyName, string sequence, string description)
    {
        var keyText = new TextBlock
        {
            Text = keyName,
            Foreground = new SolidColorBrush(Color.Parse("#4EC9B0")),
            FontFamily = new FontFamily("Consolas,Courier New"),
            Margin = new Thickness(0, 0, 0, 8)
        };
        Grid.SetRow(keyText, row);
        Grid.SetColumn(keyText, 0);
        grid.Children.Add(keyText);

        var seqText = new TextBlock
        {
            Text = sequence,
            Foreground = new SolidColorBrush(Color.Parse("#CE9178")),
            FontFamily = new FontFamily("Consolas,Courier New"),
            FontSize = 11,
            Margin = new Thickness(0, 0, 0, 8)
        };
        Grid.SetRow(seqText, row);
        Grid.SetColumn(seqText, 1);
        grid.Children.Add(seqText);

        var descText = new TextBlock
        {
            Text = description,
            Foreground = new SolidColorBrush(Color.Parse("#999999")),
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 8)
        };
        Grid.SetRow(descText, row);
        Grid.SetColumn(descText, 2);
        grid.Children.Add(descText);
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}
