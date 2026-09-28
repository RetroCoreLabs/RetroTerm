using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using RetroTerm.Core.Terminal.Input;

namespace RetroTerm.Desktop.Views;

public partial class ProgrammableKeysDialog : Window
{
    private readonly ObservableCollection<PushKeyItem> _keyItems;
    private PushKeyItem? _selectedKey;

    /// <summary>
    /// Event raised when Test Key is clicked, with the parsed byte string to send.
    /// </summary>
    public event EventHandler<string>? TestKeyRequested;

    public ProgrammableKeysDialog()
    {
        _keyItems = new ObservableCollection<PushKeyItem>();

        InitializeComponent();

        InitializeKeyList();
    }

    /// <summary>
    /// Select a specific PUSH key by number (1-16). Used for right-click from virtual keyboard.
    /// </summary>
    public void SelectKey(int keyNumber)
    {
        if (keyNumber < 1 || keyNumber > 16)
            return;

        var keyListBox = this.FindControl<ListBox>("KeyListBox");
        if (keyListBox != null && keyNumber - 1 < _keyItems.Count)
        {
            keyListBox.SelectedIndex = keyNumber - 1;
        }
    }

    private void InitializeKeyList()
    {
        var config = TDVPushKeyConfiguration.Instance;

        for (int i = 1; i <= 16; i++)
        {
            var storedString = config.GetKeyString(i);
            var displaySequence = storedString != null
                ? EscapeSequenceFormatter.Format(storedString)
                : string.Empty;
            var maxLen = TDVPushKeyConfiguration.GetMaxLength(i);
            var byteLen = storedString?.Length ?? 0;

            var item = new PushKeyItem
            {
                KeyNumber = i,
                DisplayName = $"P{i}",
                Sequence = displaySequence,
                MaxLength = maxLen,
                StatusText = byteLen > 0
                    ? $"({byteLen}/{maxLen} bytes)"
                    : $"(empty) max {maxLen}"
            };
            _keyItems.Add(item);
        }

        var keyListBox = this.FindControl<ListBox>("KeyListBox");
        if (keyListBox != null)
        {
            keyListBox.ItemsSource = _keyItems;
        }
    }

    private void OnKeySelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        var listBox = sender as ListBox;
        if (listBox?.SelectedItem is PushKeyItem item)
        {
            _selectedKey = item;
            LoadKeyIntoEditor(item);
            ShowEditor();
        }
    }

    private void LoadKeyIntoEditor(PushKeyItem item)
    {
        var keyHeaderText = this.FindControl<TextBlock>("KeyHeaderText");
        var sequenceTextBox = this.FindControl<TextBox>("SequenceTextBox");

        if (keyHeaderText != null)
        {
            keyHeaderText.Text = $"P{item.KeyNumber} Configuration (max {item.MaxLength} bytes)";
        }

        if (sequenceTextBox != null)
        {
            sequenceTextBox.Text = item.Sequence;
        }

        UpdatePreview(item.Sequence);
    }

    private void ShowEditor()
    {
        var editorPanel = this.FindControl<StackPanel>("EditorPanel");
        var noSelectionPanel = this.FindControl<StackPanel>("NoSelectionPanel");

        if (editorPanel != null)
        {
            editorPanel.IsVisible = true;
        }

        if (noSelectionPanel != null)
        {
            noSelectionPanel.IsVisible = false;
        }
    }

    private void OnSequenceTextChanged(object? sender, TextChangedEventArgs e)
    {
        var textBox = sender as TextBox;
        if (textBox != null)
        {
            UpdatePreview(textBox.Text ?? string.Empty);
        }
    }

    private void UpdatePreview(string sequence)
    {
        var hexPreviewText = this.FindControl<TextBlock>("HexPreviewText");
        var lengthInfoText = this.FindControl<TextBlock>("LengthInfoText");

        if (string.IsNullOrEmpty(sequence))
        {
            if (hexPreviewText != null)
            {
                hexPreviewText.Text = "(empty)";
            }

            if (lengthInfoText != null)
            {
                var maxInfo = _selectedKey != null ? $" (max {_selectedKey.MaxLength})" : "";
                lengthInfoText.Text = $"Length: 0 bytes{maxInfo}";
            }

            return;
        }

        try
        {
            var bytes = EscapeSequenceFormatter.Parse(sequence);
            var hexBuilder = new StringBuilder();

            for (int i = 0; i < bytes.Length; i++)
            {
                if (i > 0 && i % 16 == 0)
                {
                    hexBuilder.Append('\n');
                }
                else if (i > 0)
                {
                    hexBuilder.Append(' ');
                }

                hexBuilder.Append(bytes[i].ToString("X2"));
            }

            if (hexPreviewText != null)
            {
                hexPreviewText.Text = hexBuilder.ToString();
            }

            if (lengthInfoText != null)
            {
                var maxInfo = _selectedKey != null ? $"/{_selectedKey.MaxLength}" : "";
                var overLimit = _selectedKey != null && bytes.Length > _selectedKey.MaxLength;
                lengthInfoText.Text = $"Length: {bytes.Length}{maxInfo} byte{(bytes.Length == 1 ? "" : "s")}"
                    + (overLimit ? " - EXCEEDS LIMIT!" : "");
            }
        }
        catch (Exception ex)
        {
            if (hexPreviewText != null)
            {
                hexPreviewText.Text = $"Error: {ex.Message}";
            }

            if (lengthInfoText != null)
            {
                lengthInfoText.Text = "Invalid sequence";
            }
        }
    }

    private void OnSaveKeyClick(object? sender, RoutedEventArgs e)
    {
        if (_selectedKey == null)
        {
            return;
        }

        var sequenceTextBox = this.FindControl<TextBox>("SequenceTextBox");
        var sequence = sequenceTextBox?.Text ?? string.Empty;

        try
        {
            // Parse display format to bytes
            var bytes = EscapeSequenceFormatter.Parse(sequence);

            // Check max length
            if (bytes.Length > _selectedKey.MaxLength)
            {
                ShowTemporaryFeedback(sender as Button,
                    $"Too long: {bytes.Length}/{_selectedKey.MaxLength} bytes");
                return;
            }

            // Convert bytes to stored char string
            var storedString = new StringBuilder(bytes.Length);
            for (int i = 0; i < bytes.Length; i++)
            {
                storedString.Append((char)bytes[i]);
            }

            // Save to persistent configuration
            var config = TDVPushKeyConfiguration.Instance;
            config.ProgramKey(_selectedKey.KeyNumber, storedString.ToString());
            config.Save();

            // Update UI
            RefreshListItem(_selectedKey, sequence, bytes.Length);

            ShowTemporaryFeedback(sender as Button, "Saved!");
        }
        catch (Exception ex)
        {
            ShowTemporaryFeedback(sender as Button, $"Error: {ex.Message}");
        }
    }

    private void OnClearKeyClick(object? sender, RoutedEventArgs e)
    {
        if (_selectedKey == null)
        {
            return;
        }

        var config = TDVPushKeyConfiguration.Instance;
        config.ClearKey(_selectedKey.KeyNumber);
        config.Save();

        var sequenceTextBox = this.FindControl<TextBox>("SequenceTextBox");
        if (sequenceTextBox != null)
        {
            sequenceTextBox.Text = string.Empty;
        }

        RefreshListItem(_selectedKey, string.Empty, 0);

        ShowTemporaryFeedback(sender as Button, "Cleared!");
    }

    private void RefreshListItem(PushKeyItem item, string displaySequence, int byteLength)
    {
        item.Sequence = displaySequence;
        item.StatusText = byteLength > 0
            ? $"({byteLength}/{item.MaxLength} bytes)"
            : $"(empty) max {item.MaxLength}";

        var keyListBox = this.FindControl<ListBox>("KeyListBox");
        if (keyListBox != null)
        {
            var index = _keyItems.IndexOf(item);
            if (index >= 0)
            {
                _keyItems[index] = item;
            }
        }
    }

    private void OnTestKeyClick(object? sender, RoutedEventArgs e)
    {
        if (_selectedKey == null)
        {
            return;
        }

        var sequenceTextBox = this.FindControl<TextBox>("SequenceTextBox");
        var sequence = sequenceTextBox?.Text ?? string.Empty;

        if (string.IsNullOrEmpty(sequence))
        {
            ShowTemporaryFeedback(sender as Button, "Nothing to test");
            return;
        }

        try
        {
            var byteString = EscapeSequenceFormatter.ParseToString(sequence);
            TestKeyRequested?.Invoke(this, byteString);
            ShowTemporaryFeedback(sender as Button, $"Sent {byteString.Length} bytes");
        }
        catch (Exception ex)
        {
            ShowTemporaryFeedback(sender as Button, $"Error: {ex.Message}");
        }
    }

    private async void OnImportClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            var storageProvider = StorageProvider;
            if (storageProvider == null)
            {
                return;
            }

            var files = await storageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Import PUSH Key Configuration",
                AllowMultiple = false,
                FileTypeFilter = new[]
                {
                    new FilePickerFileType("JSON files") { Patterns = new[] { "*.json" } },
                    new FilePickerFileType("All files") { Patterns = new[] { "*" } }
                }
            });

            if (files.Count == 0)
            {
                return;
            }

            var file = files[0];
            using var stream = await file.OpenReadAsync();
            using var reader = new StreamReader(stream);
            var json = await reader.ReadToEndAsync();

            // Import format: {"1": "display_format_string", ...}
            var imported = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
            if (imported != null)
            {
                var config = TDVPushKeyConfiguration.Instance;
                config.ClearAll();

                // Convert dictionary keys to array for iteration
                var keys = new string[imported.Count];
                var values = new string[imported.Count];
                int idx = 0;
                var importEnum = imported.GetEnumerator();
                while (importEnum.MoveNext())
                {
                    keys[idx] = importEnum.Current.Key;
                    values[idx] = importEnum.Current.Value;
                    idx++;
                }

                for (int i = 0; i < keys.Length; i++)
                {
                    if (int.TryParse(keys[i], out int keyNumber) && keyNumber >= 1 && keyNumber <= 16)
                    {
                        // Parse escape format to byte string
                        var byteString = EscapeSequenceFormatter.ParseToString(values[i]);
                        config.ProgramKey(keyNumber, byteString);
                    }
                }

                config.Save();

                // Refresh UI
                _keyItems.Clear();
                InitializeKeyList();

                ShowTemporaryFeedback(sender as Button, "Imported!");
            }
        }
        catch (Exception ex)
        {
            ShowTemporaryFeedback(sender as Button, $"Import failed: {ex.Message}");
        }
    }

    private async void OnExportClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            var storageProvider = StorageProvider;
            if (storageProvider == null)
            {
                return;
            }

            var file = await storageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Export PUSH Key Configuration",
                SuggestedFileName = "push_keys.json",
                FileTypeChoices = new[]
                {
                    new FilePickerFileType("JSON files") { Patterns = new[] { "*.json" } },
                    new FilePickerFileType("All files") { Patterns = new[] { "*" } }
                }
            });

            if (file == null)
            {
                return;
            }

            var config = TDVPushKeyConfiguration.Instance;
            var allKeys = config.GetAllKeys();

            // Export in human-readable escape format
            var exportDict = new Dictionary<string, string>();
            var dictKeys = new int[allKeys.Count];
            var dictValues = new string[allKeys.Count];
            int idx = 0;
            var exportEnum = allKeys.GetEnumerator();
            while (exportEnum.MoveNext())
            {
                dictKeys[idx] = exportEnum.Current.Key;
                dictValues[idx] = exportEnum.Current.Value;
                idx++;
            }

            for (int i = 0; i < dictKeys.Length; i++)
            {
                exportDict[dictKeys[i].ToString()] = EscapeSequenceFormatter.Format(dictValues[i]);
            }

            var json = JsonSerializer.Serialize(exportDict, new JsonSerializerOptions { WriteIndented = true });

            using var stream = await file.OpenWriteAsync();
            using var writer = new StreamWriter(stream);
            await writer.WriteAsync(json);

            ShowTemporaryFeedback(sender as Button, "Exported!");
        }
        catch (Exception ex)
        {
            ShowTemporaryFeedback(sender as Button, $"Export failed: {ex.Message}");
        }
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        // Safety net: ensure config is saved on close
        TDVPushKeyConfiguration.Instance.Save();
        base.OnClosing(e);
    }

    private async void ShowTemporaryFeedback(Button? button, string message)
    {
        if (button == null)
        {
            return;
        }

        var originalContent = button.Content;
        button.Content = message;
        await System.Threading.Tasks.Task.Delay(2000);
        button.Content = originalContent;
    }
}

public class PushKeyItem
{
    public int KeyNumber { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string Sequence { get; set; } = string.Empty;
    public string StatusText { get; set; } = string.Empty;
    public int MaxLength { get; set; }
}
