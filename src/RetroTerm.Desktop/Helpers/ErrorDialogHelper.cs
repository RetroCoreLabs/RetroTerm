using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;

namespace RetroTerm.Desktop.Helpers;

/// <summary>
/// Themed dialog helpers for error messages, confirmations, and save prompts.
/// </summary>
/// <remarks>
/// <para><b>Nothing here sets a colour by hand</b></para>
/// Until 27 September 2026 every button in these dialogs carried its own hex brushes on the
/// Button itself. The Fluent Button theme swaps the ContentPresenter's background on
/// :pointerover, which a brush set on the Button cannot reach, so the fill vanished under the
/// mouse - the defect Ronny reported on the Close RetroTerm dialog, which was built the same
/// way. The buttons now use the shared dialog-primary, dialog-secondary and dialog-danger classes
/// from FluentTheme.axaml, which pin their fill through hover and press, and the window and text
/// take the theme's brushes so a theme change reaches them.
/// </remarks>
public static class ErrorDialogHelper
{
    /// <summary>
    /// Builds the dialog window every helper here shows, sized and themed the same way.
    /// </summary>
    /// <param name="title">
    /// The window title.
    /// </param>
    /// <param name="width">
    /// Window width in device-independent pixels.
    /// </param>
    /// <returns>
    /// An owner-centred, fixed-size window with the theme's background.
    /// </returns>
    private static Window NewDialog(string title, double width)
    {
        var dialog = new Window
        {
            Title = title,
            Width = width,
            Height = 170,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false
        };
        dialog[!Window.BackgroundProperty] = new DynamicResourceExtension("WindowBackgroundBrush");
        return dialog;
    }

    /// <summary>
    /// Builds the message line, in the theme's primary text colour.
    /// </summary>
    /// <param name="message">
    /// The text to show.
    /// </param>
    /// <returns>
    /// A wrapping, centred text block.
    /// </returns>
    private static TextBlock NewMessage(string message)
    {
        var text = new TextBlock
        {
            Text = message,
            TextWrapping = TextWrapping.Wrap,
            FontSize = 13,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        text[!TextBlock.ForegroundProperty] = new DynamicResourceExtension("FluentTextPrimaryBrush");
        return text;
    }

    /// <summary>
    /// Builds a dialog button carrying one of the shared dialog button classes.
    /// </summary>
    /// <param name="content">
    /// The button text.
    /// </param>
    /// <param name="dialogClass">
    /// dialog-primary, dialog-secondary or dialog-danger.
    /// </param>
    /// <returns>
    /// The button, styled entirely by the theme.
    /// </returns>
    private static Button NewButton(string content, string dialogClass)
    {
        return new Button
        {
            Content = content,
            Classes = { dialogClass }
        };
    }

    /// <summary>
    /// Shows a themed error dialog with an OK button.
    /// </summary>
    /// <param name="parent">
    /// The window the dialog is centred on and blocks.
    /// </param>
    /// <param name="message">
    /// The error text.
    /// </param>
    /// <returns>
    /// A task that completes when the dialog closes.
    /// </returns>
    public static async Task ShowErrorDialogAsync(Window parent, string message)
    {
        var dialog = NewDialog("Error", 450);

        var panel = new StackPanel
        {
            Margin = new Thickness(24),
            Spacing = 16
        };
        panel.Children.Add(NewMessage(message));

        var okBtn = NewButton("OK", "dialog-primary");
        okBtn.HorizontalAlignment = HorizontalAlignment.Right;
        okBtn.Click += (_, _) => dialog.Close();

        panel.Children.Add(okBtn);
        dialog.Content = panel;

        await dialog.ShowDialog(parent);
    }

    /// <summary>
    /// Shows a themed confirmation dialog.
    /// </summary>
    /// <param name="parent">
    /// The window the dialog is centred on and blocks.
    /// </param>
    /// <param name="title">
    /// The window title.
    /// </param>
    /// <param name="message">
    /// The question to put to the user.
    /// </param>
    /// <param name="confirmText">
    /// Text on the confirming button.
    /// </param>
    /// <param name="cancelText">
    /// Text on the dismissing button.
    /// </param>
    /// <param name="isDanger">
    /// True when confirming destroys something, which colours the confirming button with the
    /// theme's error colour instead of its accent.
    /// </param>
    /// <returns>
    /// True when the user confirmed, false when they cancelled or closed the window.
    /// </returns>
    public static async Task<bool> ShowConfirmDialogAsync(Window parent, string title, string message,
        string confirmText = "OK", string cancelText = "Cancel", bool isDanger = false)
    {
        bool result = false;

        var dialog = NewDialog(title, 400);

        var panel = new StackPanel
        {
            Margin = new Thickness(24),
            Spacing = 16
        };
        panel.Children.Add(NewMessage(message));

        var buttonPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 12,
            HorizontalAlignment = HorizontalAlignment.Center
        };

        var cancelBtn = NewButton(cancelText, "dialog-secondary");
        cancelBtn.Click += (_, _) => dialog.Close();

        var confirmBtn = NewButton(confirmText, isDanger ? "dialog-danger" : "dialog-primary");
        confirmBtn.Click += (_, _) =>
        {
            result = true;
            dialog.Close();
        };

        buttonPanel.Children.Add(cancelBtn);
        buttonPanel.Children.Add(confirmBtn);
        panel.Children.Add(buttonPanel);
        dialog.Content = panel;

        await dialog.ShowDialog(parent);
        return result;
    }

    /// <summary>
    /// Shows a save-before-action prompt with three options.
    /// </summary>
    /// <param name="parent">
    /// The window the dialog is centred on and blocks.
    /// </param>
    /// <param name="message">
    /// What is unsaved and what is about to happen.
    /// </param>
    /// <returns>
    /// "save", "discard" or "cancel".
    /// </returns>
    public static async Task<string> ShowSavePromptAsync(Window parent, string message)
    {
        string result = "cancel";

        var dialog = NewDialog("Unsaved Changes", 450);

        var panel = new StackPanel
        {
            Margin = new Thickness(24),
            Spacing = 16
        };
        panel.Children.Add(NewMessage(message));

        var buttonPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            HorizontalAlignment = HorizontalAlignment.Center
        };

        var cancelBtn = NewButton("Cancel", "dialog-secondary");
        cancelBtn.Click += (_, _) => dialog.Close();

        var discardBtn = NewButton("Don't Save", "dialog-danger");
        discardBtn.Click += (_, _) =>
        {
            result = "discard";
            dialog.Close();
        };

        var saveBtn = NewButton("Save", "dialog-primary");
        saveBtn.Click += (_, _) =>
        {
            result = "save";
            dialog.Close();
        };

        buttonPanel.Children.Add(cancelBtn);
        buttonPanel.Children.Add(discardBtn);
        buttonPanel.Children.Add(saveBtn);
        panel.Children.Add(buttonPanel);
        dialog.Content = panel;

        await dialog.ShowDialog(parent);
        return result;
    }
}
