using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using RetroTerm.Core.Configuration;
using RetroTerm.Desktop.Rendering;

namespace RetroTerm.Desktop.Views;

/// <summary>
/// What the colour picker returned.
/// </summary>
public sealed class TerminalColourPickerResult
{
    /// <summary>Chosen foreground as "#RRGGBB".</summary>
    public string Foreground { get; init; } = "";

    /// <summary>Chosen background as "#RRGGBB".</summary>
    public string Background { get; init; } = "";

    /// <summary>The name the pair was saved under, or null when it was not saved as a preset.</summary>
    public string? SavedPresetName { get; init; }
}

/// <summary>
/// Picks a terminal foreground and background. Each colour has a hex box and three sliders that
/// follow each other, a preview shows exactly how text will look on the chosen background, and a
/// contrast warning appears when the pair would be hard to read. The pair can also be saved as a
/// named preset that then appears in every colour menu.
/// </summary>
public sealed class TerminalColourPickerDialog : Window
{
    private readonly ColourEditor _foreground;
    private readonly ColourEditor _background;
    private readonly Border _previewScreen = new();
    private readonly TextBlock _previewLine1 = new();
    private readonly TextBlock _previewLine2 = new();
    private readonly TextBlock _previewLine3 = new();
    private readonly TextBlock _contrastText = new();
    private readonly TextBlock _warningText = new();
    private readonly TextBlock _errorText = new();
    private readonly CheckBox _saveCheck = new();
    private readonly TextBox _nameBox = new();
    private readonly UserTerminalColourStore _store;
    private TerminalColourPickerResult? _result;

    /// <summary>
    /// False while the dialog is still being built. The two colour editors report a change as soon
    /// as they are given their starting colour, and the second editor does not exist yet when the
    /// first one does - so a refresh before the build ends would read a colour that is not there.
    /// </summary>
    private bool _built;

    private TerminalColourPickerDialog(string foreground, string background, bool offerSave, UserTerminalColourStore store)
    {
        _store = store;
        Title = "Custom Terminal Colours";
        Width = 520;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        CanResize = false;
        this[!BackgroundProperty] = new DynamicResourceExtension("WindowBackgroundBrush");

        _foreground = new ColourEditor("Text colour", foreground, Refresh);
        _background = new ColourEditor("Background colour", background, Refresh);

        var root = new StackPanel { Margin = new Thickness(16), Spacing = 12 };
        root.Children.Add(BuildPreview());
        root.Children.Add(_warningText);
        root.Children.Add(_contrastText);
        root.Children.Add(_foreground.Panel);
        root.Children.Add(_background.Panel);

        if (offerSave)
        {
            _saveCheck.Content = "Save as a preset named:";
            _saveCheck[!ForegroundProperty] = new DynamicResourceExtension("PrimaryTextBrush");
            _saveCheck.IsCheckedChanged += (_, _) => _nameBox.IsEnabled = _saveCheck.IsChecked == true;

            _nameBox.PlaceholderText = "e.g. My dark blue";
            _nameBox.MinWidth = 180;
            _nameBox.IsEnabled = false;

            var saveRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            saveRow.Children.Add(_saveCheck);
            saveRow.Children.Add(_nameBox);
            root.Children.Add(saveRow);
        }

        _errorText.TextWrapping = TextWrapping.Wrap;
        _errorText[!ForegroundProperty] = new DynamicResourceExtension("ErrorBrush");
        _errorText.IsVisible = false;
        root.Children.Add(_errorText);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 12,
        };
        var cancel = new Button { Content = "Cancel", Classes = { "dialog-secondary" } };
        cancel.Click += (_, _) => Close();
        var ok = new Button { Content = "OK", Classes = { "dialog-primary" } };
        ok.Click += (_, _) => Accept();
        buttons.Children.Add(cancel);
        buttons.Children.Add(ok);
        root.Children.Add(buttons);

        Content = root;
        _built = true;
        Refresh();
    }

    /// <summary>
    /// Shows the picker over <paramref name="owner"/> and waits for it to close.
    /// </summary>
    /// <param name="owner">
    /// The window to centre over.
    /// </param>
    /// <param name="foreground">
    /// The starting foreground as hex. Anything that is not a colour starts as light grey.
    /// </param>
    /// <param name="background">
    /// The starting background as hex. Anything that is not a colour starts as black.
    /// </param>
    /// <param name="offerSave">
    /// True to offer "save as a preset". The preferences and tab menu both offer it.
    /// </param>
    /// <param name="store">
    /// Where a saved preset goes. Null uses the application's own file; tests pass their own.
    /// </param>
    /// <returns>
    /// The chosen pair, or null when the user cancelled.
    /// </returns>
    public static async Task<TerminalColourPickerResult?> ShowAsync(
        Window owner, string? foreground, string? background, bool offerSave = true,
        UserTerminalColourStore? store = null)
    {
        var dialog = Create(foreground, background, offerSave, store);
        await dialog.ShowDialog(owner);
        return dialog._result;
    }

    /// <summary>
    /// Builds the dialog without showing it, which is what the tests do.
    /// </summary>
    /// <param name="foreground">The starting foreground as hex.</param>
    /// <param name="background">The starting background as hex.</param>
    /// <param name="offerSave">Whether to offer "save as a preset".</param>
    /// <param name="store">Where a saved preset goes; null for the application's own.</param>
    internal static TerminalColourPickerDialog Create(
        string? foreground, string? background, bool offerSave = true, UserTerminalColourStore? store = null)
        => new(
            TerminalColourMath.TryParseHex(foreground, out _) ? TerminalColourMath.Normalise(foreground!) : "#CCCCCC",
            TerminalColourMath.TryParseHex(background, out _) ? TerminalColourMath.Normalise(background!) : "#000000",
            offerSave,
            store ?? UserTerminalColourStore.Default);

    /// <summary>The foreground text as currently typed, for the tests.</summary>
    internal string CurrentForeground => _foreground.Hex;

    /// <summary>The background text as currently typed, for the tests.</summary>
    internal string CurrentBackground => _background.Hex;

    /// <summary>The warning the dialog shows now, or empty when it shows none.</summary>
    internal string WarningShown => _warningText.IsVisible ? _warningText.Text ?? "" : "";

    /// <summary>Sets both colours as if typed, for the tests.</summary>
    internal void SetColours(string foreground, string background)
    {
        _foreground.SetHex(foreground);
        _background.SetHex(background);
    }

    private Control BuildPreview()
    {
        var mono = new FontFamily(SystemFontRenderer.DefaultFontFamily);
        foreach (var line in new[] { _previewLine1, _previewLine2, _previewLine3 })
        {
            line.FontFamily = mono;
            line.FontSize = 14;
        }

        _previewLine1.Text = "RetroTerm 1.0  -  preview";
        _previewLine2.Text = "The quick brown fox jumps over 0123456789";
        _previewLine3.Text = "login: ronny_";

        _previewScreen.Padding = new Thickness(12, 10);
        _previewScreen.CornerRadius = new CornerRadius(4);
        _previewScreen.BorderThickness = new Thickness(1);
        _previewScreen[!Border.BorderBrushProperty] = new DynamicResourceExtension("BorderBrush");
        _previewScreen.Child = new StackPanel
        {
            Spacing = 2,
            Children = { _previewLine1, _previewLine2, _previewLine3 },
        };
        return _previewScreen;
    }

    private void Refresh()
    {
        if (!_built) return;

        var fgOk = TerminalColourMath.TryParseHex(_foreground.Hex, out var fg);
        var bgOk = TerminalColourMath.TryParseHex(_background.Hex, out var bg);

        if (fgOk && bgOk)
        {
            var fgBrush = new SolidColorBrush(Color.FromRgb(fg.R, fg.G, fg.B));
            _previewScreen.Background = new SolidColorBrush(Color.FromRgb(bg.R, bg.G, bg.B));
            _previewLine1.Foreground = fgBrush;
            _previewLine2.Foreground = fgBrush;
            _previewLine3.Foreground = fgBrush;

            var ratio = TerminalColourMath.ContrastRatio(fg, bg);
            _contrastText.Text = $"Contrast {ratio:0.0}:1";
            _contrastText[!ForegroundProperty] = new DynamicResourceExtension("SecondaryTextBrush");

            var warning = TerminalColourMath.ContrastWarning(_foreground.Hex, _background.Hex);
            _warningText.Text = warning == null ? "" : "⚠ " + warning;
            _warningText.TextWrapping = TextWrapping.Wrap;
            _warningText.FontWeight = FontWeight.SemiBold;
            _warningText[!ForegroundProperty] = new DynamicResourceExtension("WarningBrush");
            _warningText.IsVisible = warning != null;
        }
        else
        {
            _contrastText.Text = "Enter both colours as hex, like #00FF88.";
            _warningText.IsVisible = false;
        }
    }

    private void Accept()
    {
        if (!TerminalColourMath.TryParseHex(_foreground.Hex, out _) ||
            !TerminalColourMath.TryParseHex(_background.Hex, out _))
        {
            ShowError("Both colours must be hex values like #00FF88.");
            return;
        }

        var fg = TerminalColourMath.Normalise(_foreground.Hex);
        var bg = TerminalColourMath.Normalise(_background.Hex);
        string? savedName = null;

        if (_saveCheck.IsChecked == true)
        {
            var name = (_nameBox.Text ?? "").Trim();
            if (!_store.TrySave(new TerminalColourPreset(name, fg, bg), out var error))
            {
                ShowError(error ?? "The preset could not be saved.");
                return;
            }
            savedName = name;
        }

        _result = new TerminalColourPickerResult { Foreground = fg, Background = bg, SavedPresetName = savedName };
        Close();
    }

    private void ShowError(string message)
    {
        _errorText.Text = message;
        _errorText.IsVisible = true;
    }

    /// <summary>
    /// One colour: a hex box and red, green and blue sliders that follow each other.
    /// </summary>
    private sealed class ColourEditor
    {
        private readonly TextBox _hexBox = new() { Width = 110, MaxLength = 7, FontFamily = new FontFamily(SystemFontRenderer.DefaultFontFamily) };
        private readonly Slider[] _sliders = new Slider[3];
        private readonly TextBlock[] _values = new TextBlock[3];
        private readonly Border _swatch = new() { Width = 28, Height = 28, CornerRadius = new CornerRadius(4), BorderThickness = new Thickness(1) };
        private readonly Action _changed;
        private bool _updating;

        public StackPanel Panel { get; }

        public string Hex => _hexBox.Text ?? "";

        public ColourEditor(string title, string initialHex, Action changed)
        {
            _changed = changed;

            var heading = new TextBlock { Text = title, FontWeight = FontWeight.SemiBold, FontSize = 13 };
            heading[!TextBlock.ForegroundProperty] = new DynamicResourceExtension("PrimaryTextBrush");
            _swatch[!Border.BorderBrushProperty] = new DynamicResourceExtension("BorderBrush");

            var top = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
            top.Children.Add(_swatch);
            top.Children.Add(_hexBox);
            top.Children.Add(heading);
            heading.VerticalAlignment = VerticalAlignment.Center;
            _hexBox.VerticalAlignment = VerticalAlignment.Center;

            Panel = new StackPanel { Spacing = 4 };
            Panel.Children.Add(top);

            var names = new[] { "R", "G", "B" };
            for (var i = 0; i < 3; i++)
            {
                var index = i;
                var slider = new Slider { Minimum = 0, Maximum = 255, Width = 340, SmallChange = 1, LargeChange = 16 };
                _sliders[i] = slider;
                slider.ValueChanged += (_, _) => OnSliderChanged(index);

                var label = new TextBlock { Text = names[i], Width = 16, VerticalAlignment = VerticalAlignment.Center };
                label[!TextBlock.ForegroundProperty] = new DynamicResourceExtension("SecondaryTextBrush");
                var value = new TextBlock { Width = 32, VerticalAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Right };
                value[!TextBlock.ForegroundProperty] = new DynamicResourceExtension("SecondaryTextBrush");
                _values[i] = value;

                var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
                row.Children.Add(label);
                row.Children.Add(slider);
                row.Children.Add(value);
                Panel.Children.Add(row);
            }

            _hexBox.TextChanged += (_, _) => OnHexChanged();
            SetHex(initialHex);
        }

        public void SetHex(string hex)
        {
            _hexBox.Text = hex;
            OnHexChanged();
        }

        private void OnHexChanged()
        {
            if (_updating) return;
            _updating = true;
            try
            {
                if (TerminalColourMath.TryParseHex(_hexBox.Text, out var c))
                {
                    _sliders[0].Value = c.R;
                    _sliders[1].Value = c.G;
                    _sliders[2].Value = c.B;
                    _values[0].Text = c.R.ToString();
                    _values[1].Text = c.G.ToString();
                    _values[2].Text = c.B.ToString();
                    _swatch.Background = new SolidColorBrush(Color.FromRgb(c.R, c.G, c.B));
                }
                else
                {
                    _swatch.Background = Brushes.Transparent;
                }
            }
            finally
            {
                _updating = false;
            }
            _changed();
        }

        private void OnSliderChanged(int index)
        {
            if (_updating) return;
            _updating = true;
            try
            {
                var r = (byte)Math.Round(_sliders[0].Value);
                var g = (byte)Math.Round(_sliders[1].Value);
                var b = (byte)Math.Round(_sliders[2].Value);
                _hexBox.Text = TerminalColourMath.ToHex(r, g, b);
                _values[0].Text = r.ToString();
                _values[1].Text = g.ToString();
                _values[2].Text = b.ToString();
                _swatch.Background = new SolidColorBrush(Color.FromRgb(r, g, b));
            }
            finally
            {
                _updating = false;
            }
            _changed();
        }
    }
}
