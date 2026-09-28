using System;
using System.Collections.Generic;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using RetroTerm.Core.Protocols.WebSocket.Gateway;

namespace RetroTerm.Desktop.Views;

public partial class GatewayDiskConfigWindow : Window
{
    private readonly GatewayListener _listener;
    private readonly List<DiskRowEntry> _smdEntries = new();
    private readonly List<DiskRowEntry> _floppyEntries = new();

    private static readonly IBrush LabelBrush = new SolidColorBrush(Color.Parse("#CCCCCC"));
    private static readonly IBrush DimBrush = new SolidColorBrush(Color.Parse("#999999"));
    private static readonly IBrush EmptyBrush = new SolidColorBrush(Color.Parse("#666666"));
    private static readonly IBrush RowBgBrush = new SolidColorBrush(Color.Parse("#2D2D30"));
    private static readonly IBrush RowAltBgBrush = new SolidColorBrush(Color.Parse("#252526"));

    private class DiskRowEntry
    {
        public int DriveType;
        public string? ImagePath;
        public string? ImageName;

        // UI elements
        public TextBlock? PathLabel;
        public TextBlock? SizeLabel;
    }

    public GatewayDiskConfigWindow(GatewayListener listener, GatewayDiskSettings settings)
    {
        _listener = listener;
        InitializeComponent();
        LoadFromSettings(settings);
        RebuildSmdPanel();
        RebuildFloppyPanel();
    }

    private void LoadFromSettings(GatewayDiskSettings settings)
    {
        for (int i = 0; i < settings.Images.Count; i++)
        {
            var img = settings.Images[i];
            var entry = new DiskRowEntry
            {
                DriveType = img.DriveType,
                ImagePath = img.Path,
                ImageName = img.Name
            };

            if (img.DriveType == 0)
                _smdEntries.Add(entry);
            else
                _floppyEntries.Add(entry);
        }
    }

    // ───────────────────────────────────────────────────────────────────
    // Grid row building
    // ───────────────────────────────────────────────────────────────────

    private Border BuildRow(DiskRowEntry entry, int index)
    {
        // Grid: [Path:*] [Size:80] [Browse:70] [Remove:70]
        var grid = new Grid
        {
            ColumnDefinitions = ColumnDefinitions.Parse("*,80,70,70"),
            Margin = new Thickness(8, 4)
        };

        bool hasImage = !string.IsNullOrEmpty(entry.ImagePath);
        entry.PathLabel = new TextBlock
        {
            Text = hasImage ? entry.ImagePath : "(no image)",
            Foreground = hasImage ? LabelBrush : EmptyBrush,
            FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(8, 0)
        };
        Grid.SetColumn(entry.PathLabel, 0);

        entry.SizeLabel = new TextBlock
        {
            Text = GetFileSizeText(entry.ImagePath),
            Foreground = DimBrush,
            FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 0, 4, 0)
        };
        Grid.SetColumn(entry.SizeLabel, 1);

        // Secondary and Danger classes rather than the hand brushes these carried until
        // 27 September 2026: a brush set on the Button itself is lost under the mouse (see the
        // note above the Button styles in DarkTheme.axaml). The compact padding stays, because
        // these sit inside a table row.
        var browseButton = new Button
        {
            Content = "Browse",
            Classes = { "Secondary" },
            Padding = new Thickness(6, 2),
            FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            Tag = entry
        };
        browseButton.Click += OnBrowseClick;
        Grid.SetColumn(browseButton, 2);

        var removeButton = new Button
        {
            Content = "Remove",
            Classes = { "Danger" },
            Padding = new Thickness(6, 2),
            FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            Tag = entry
        };
        removeButton.Click += OnRemoveClick;
        Grid.SetColumn(removeButton, 3);

        grid.Children.Add(entry.PathLabel);
        grid.Children.Add(entry.SizeLabel);
        grid.Children.Add(browseButton);
        grid.Children.Add(removeButton);

        return new Border
        {
            Background = (index % 2 == 0) ? RowBgBrush : RowAltBgBrush,
            Padding = new Thickness(0, 2),
            Child = grid
        };
    }

    private static Border BuildHeader()
    {
        var grid = new Grid
        {
            ColumnDefinitions = ColumnDefinitions.Parse("*,80,70,70"),
            Margin = new Thickness(8, 2)
        };

        var pathH = new TextBlock { Text = "Image Path", Foreground = DimBrush, FontSize = 11, FontWeight = FontWeight.SemiBold, Margin = new Thickness(8, 0) };
        Grid.SetColumn(pathH, 0);

        var sizeH = new TextBlock { Text = "Size", Foreground = DimBrush, FontSize = 11, FontWeight = FontWeight.SemiBold, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 0, 4, 0) };
        Grid.SetColumn(sizeH, 1);

        grid.Children.Add(pathH);
        grid.Children.Add(sizeH);

        return new Border
        {
            Background = new SolidColorBrush(Color.Parse("#1E1E1E")),
            BorderBrush = new SolidColorBrush(Color.Parse("#3C3C3C")),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(0, 2),
            Child = grid
        };
    }

    // ───────────────────────────────────────────────────────────────────
    // Panel rebuild
    // ───────────────────────────────────────────────────────────────────

    private void RebuildSmdPanel()
    {
        var panel = this.FindControl<StackPanel>("SmdPanel");
        if (panel == null) return;
        panel.Children.Clear();
        panel.Children.Add(BuildHeader());
        for (int i = 0; i < _smdEntries.Count; i++)
        {
            panel.Children.Add(BuildRow(_smdEntries[i], i));
        }
        if (_smdEntries.Count == 0)
        {
            panel.Children.Add(new TextBlock
            {
                Text = "No SMD images configured. Click '+ Add SMD Image' below.",
                Foreground = EmptyBrush,
                FontSize = 12,
                Margin = new Thickness(16, 12),
                HorizontalAlignment = HorizontalAlignment.Center
            });
        }
    }

    private void RebuildFloppyPanel()
    {
        var panel = this.FindControl<StackPanel>("FloppyPanel");
        if (panel == null) return;
        panel.Children.Clear();
        panel.Children.Add(BuildHeader());
        for (int i = 0; i < _floppyEntries.Count; i++)
        {
            panel.Children.Add(BuildRow(_floppyEntries[i], i));
        }
        if (_floppyEntries.Count == 0)
        {
            panel.Children.Add(new TextBlock
            {
                Text = "No floppy images configured. Click '+ Add Floppy Image' below.",
                Foreground = EmptyBrush,
                FontSize = 12,
                Margin = new Thickness(16, 12),
                HorizontalAlignment = HorizontalAlignment.Center
            });
        }
    }

    // ───────────────────────────────────────────────────────────────────
    // Add / Remove / Browse
    // ───────────────────────────────────────────────────────────────────

    private void OnAddSmdClick(object? sender, RoutedEventArgs e)
    {
        _smdEntries.Add(new DiskRowEntry { DriveType = 0 });
        RebuildSmdPanel();
    }

    private void OnAddFloppyClick(object? sender, RoutedEventArgs e)
    {
        _floppyEntries.Add(new DiskRowEntry { DriveType = 1 });
        RebuildFloppyPanel();
    }

    private void OnRemoveClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not DiskRowEntry entry)
            return;

        if (entry.DriveType == 0)
        {
            _smdEntries.Remove(entry);
            RebuildSmdPanel();
        }
        else
        {
            _floppyEntries.Remove(entry);
            RebuildFloppyPanel();
        }
    }

    private async void OnBrowseClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not DiskRowEntry entry)
            return;

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null) return;

        string typeStr = entry.DriveType == 0 ? "SMD" : "Floppy";
        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = $"Select {typeStr} disk image",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("Disk Images") { Patterns = new[] { "*.img", "*.IMG" } },
                new FilePickerFileType("All Files") { Patterns = new[] { "*" } }
            }
        });

        if (files.Count == 0) return;

        var file = files[0];
        var path = file.TryGetLocalPath();
        if (string.IsNullOrEmpty(path)) return;

        entry.ImagePath = path;
        entry.ImageName = Path.GetFileNameWithoutExtension(path);

        if (entry.PathLabel != null)
        {
            entry.PathLabel.Text = path;
            entry.PathLabel.Foreground = LabelBrush;
        }
        if (entry.SizeLabel != null)
        {
            entry.SizeLabel.Text = GetFileSizeText(path);
        }
    }

    // ───────────────────────────────────────────────────────────────────
    // Apply / Cancel
    // ───────────────────────────────────────────────────────────────────

    private void OnApplyClick(object? sender, RoutedEventArgs e)
    {
        var settings = new GatewayDiskSettings();

        for (int i = 0; i < _smdEntries.Count; i++)
        {
            var entry = _smdEntries[i];
            if (string.IsNullOrEmpty(entry.ImagePath)) continue;
            settings.Images.Add(new GatewayDiskImageEntry
            {
                DriveType = 0,
                Path = entry.ImagePath,
                Name = entry.ImageName ?? string.Empty
            });
        }

        for (int i = 0; i < _floppyEntries.Count; i++)
        {
            var entry = _floppyEntries[i];
            if (string.IsNullOrEmpty(entry.ImagePath)) continue;
            settings.Images.Add(new GatewayDiskImageEntry
            {
                DriveType = 1,
                Path = entry.ImagePath,
                Name = entry.ImageName ?? string.Empty
            });
        }

        settings.Save();
        _listener.OpenDiskImages(settings);
        Close();
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    // ───────────────────────────────────────────────────────────────────
    // Helpers
    // ───────────────────────────────────────────────────────────────────

    private static string GetFileSizeText(string? path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
            return string.Empty;

        try
        {
            var info = new FileInfo(path);
            long bytes = info.Length;
            if (bytes < 1024)
                return $"{bytes} B";
            if (bytes < 1024 * 1024)
                return $"{bytes / 1024.0:F1} KB";
            if (bytes < 1024L * 1024 * 1024)
                return $"{bytes / (1024.0 * 1024):F1} MB";
            return $"{bytes / (1024.0 * 1024 * 1024):F2} GB";
        }
        catch
        {
            return string.Empty;
        }
    }
}
