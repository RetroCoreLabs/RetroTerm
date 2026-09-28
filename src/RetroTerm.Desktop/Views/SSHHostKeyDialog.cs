using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;

namespace RetroTerm.Desktop.Views;

/// <summary>
/// Dialog for SSH host key verification. Asks the user to confirm an unknown or changed host key.
/// </summary>
/// <remarks>
/// Until 27 September 2026 this was a LIGHT dialog - white window, hex-coded text and buttons
/// marked "Light theme" in the comments - inside a dark app, and its buttons vanished under the
/// mouse because their brushes sat on the Button rather than in a class (see the note above the
/// Button styles in DarkTheme.axaml). It now takes every colour from the theme, with the two
/// warning headings on the theme's error and warning brushes.
/// </remarks>
public static class SSHHostKeyDialog
{
    /// <summary>
    /// A text block in the theme's primary text colour.
    /// </summary>
    /// <param name="text">
    /// The text to show.
    /// </param>
    /// <param name="fontSize">
    /// Font size in points.
    /// </param>
    /// <param name="weight">
    /// Font weight.
    /// </param>
    /// <returns>
    /// A wrapping text block bound to PrimaryTextBrush.
    /// </returns>
    private static TextBlock PrimaryText(string text, double fontSize, FontWeight weight)
    {
        var block = new TextBlock
        {
            Text = text,
            TextWrapping = TextWrapping.Wrap,
            FontSize = fontSize,
            FontWeight = weight
        };
        block[!TextBlock.ForegroundProperty] = new DynamicResourceExtension("PrimaryTextBrush");
        return block;
    }

    /// <summary>
    /// A fingerprint line: monospaced, in the theme's secondary text colour.
    /// </summary>
    /// <param name="text">
    /// The fingerprint text.
    /// </param>
    /// <param name="fontSize">
    /// Font size in points.
    /// </param>
    /// <returns>
    /// A monospaced text block bound to SecondaryTextBrush.
    /// </returns>
    private static TextBlock Fingerprint(string text, double fontSize)
    {
        var block = new TextBlock
        {
            Text = text,
            FontFamily = new FontFamily("Consolas, Courier New"),
            FontSize = fontSize
        };
        block[!TextBlock.ForegroundProperty] = new DynamicResourceExtension("SecondaryTextBrush");
        return block;
    }

    /// <summary>
    /// Shows a confirmation dialog for SSH host key verification
    /// </summary>
    /// <returns>
    /// True if user accepts the key, false if user declines
    /// </returns>
    public static async Task<bool> ShowConfirmationAsync(
        Window parent,
        string host,
        int port,
        string fingerprint,
        string fingerprintMD5,
        string? knownFingerprint = null)
    {
        var isChanged = !string.IsNullOrEmpty(knownFingerprint);
        var dialog = new Window
        {
            Title = "SSH Host Key Verification",
            Width = 600,
            Height = isChanged ? 400 : 320,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false
        };
        dialog[!Window.BackgroundProperty] = new DynamicResourceExtension("WindowBackgroundBrush");

        var border = new Border
        {
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(24)
        };
        border[!Border.BackgroundProperty] = new DynamicResourceExtension("PanelBackgroundBrush");
        border[!Border.BorderBrushProperty] = new DynamicResourceExtension("BorderBrush");

        var panel = new StackPanel { Spacing = 16 };

        // Warning message - different for changed vs unknown: error red for a changed key,
        // the theme's warning colour for one never seen before.
        var heading = new TextBlock
        {
            Text = isChanged ? "WARNING: SSH Host Key Changed!" : "WARNING: Unknown SSH Host Key",
            FontWeight = FontWeight.Bold,
            FontSize = 14
        };
        heading[!TextBlock.ForegroundProperty] = new DynamicResourceExtension(isChanged ? "ErrorBrush" : "WarningBrush");
        panel.Children.Add(heading);

        // Host info
        panel.Children.Add(PrimaryText(isChanged
                ? $"The host key for '{host}:{port}' has changed!\n\nThis could indicate a man-in-the-middle attack."
                : $"The authenticity of host '{host}:{port}' cannot be established.",
            13, FontWeight.Normal));

        // Show known fingerprint if changed
        if (isChanged)
        {
            panel.Children.Add(PrimaryText("Known fingerprint:", 12, FontWeight.SemiBold));
            panel.Children.Add(Fingerprint($"SHA256: {knownFingerprint}", 11));
            var newHeading = PrimaryText("New fingerprint:", 12, FontWeight.SemiBold);
            newHeading.Margin = new Thickness(0, 8, 0, 0);
            panel.Children.Add(newHeading);
        }

        // Fingerprints
        panel.Children.Add(Fingerprint($"SHA256: {fingerprint}\nMD5: {fingerprintMD5}", 12));

        // Warning
        panel.Children.Add(PrimaryText(isChanged
                ? "Do you want to accept the new key and continue?"
                : "Are you sure you want to continue connecting?",
            13, FontWeight.SemiBold));

        // Buttons
        var buttonPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 12
        };

        bool result = false;

        // The shared dialog button classes: they pin their fill through hover and press.
        var cancelBtn = new Button
        {
            Content = "Cancel",
            Classes = { "dialog-secondary" }
        };
        cancelBtn.Click += (_, _) => { result = false; dialog.Close(); };

        var acceptBtn = new Button
        {
            Content = "Accept & Connect",
            Classes = { "dialog-primary" }
        };
        acceptBtn.Click += (_, _) => { result = true; dialog.Close(); };

        buttonPanel.Children.Add(cancelBtn);
        buttonPanel.Children.Add(acceptBtn);
        panel.Children.Add(buttonPanel);

        border.Child = panel;
        dialog.Content = border;

        await dialog.ShowDialog(parent);

        return result;
    }
}

