using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input.Platform; // ClipboardExtensions.TryGetTextAsync
using Avalonia.Threading;

namespace RetroTerm.Desktop.Services;

/// <summary>
/// Avalonia implementation of IClipboardService
/// Note: Avalonia clipboard is async, so we use a wrapper pattern
/// </summary>
public class AvaloniaClipboardService : RetroTerm.Core.Clipboard.IClipboardService
{
    private string? _cachedText;
    private bool _hasText;

    public string? GetText()
    {
        // Return cached value if available
        // Note: This is a limitation - Avalonia clipboard is async
        // In practice, we'll use async methods directly
        return _cachedText;
    }

    public void SetText(string text)
    {
        // Avalonia clipboard is async, post to UI thread
        Dispatcher.UIThread.Post(async () =>
        {
            try
            {
                if (Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
                {
                    var clipboard = TopLevel.GetTopLevel(desktop.MainWindow)?.Clipboard;
                    if (clipboard != null)
                    {
                        await clipboard.SetTextAsync(text);
                    }
                }
            }
            catch (Exception)
            {
                // Silently fail - clipboard might not be available
            }
        });
    }

    public bool ContainsText()
    {
        return _hasText;
    }

    /// <summary>
    /// Async method to get clipboard text (preferred)
    /// </summary>
    public async Task<string?> GetTextAsync()
    {
        try
        {
            if (Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
            {
                var clipboard = TopLevel.GetTopLevel(desktop.MainWindow)?.Clipboard;
                if (clipboard != null)
                {
                    // TryGetTextAsync replaces the obsolete GetTextAsync (CS0618).
                    // It returns null instead of throwing when the clipboard holds
                    // no text, which the checks below already handle.
                    var text = await clipboard.TryGetTextAsync();
                    _cachedText = text;
                    _hasText = !string.IsNullOrEmpty(text);
                    return text;
                }
            }
        }
        catch (Exception)
        {
            // Clipboard might not be available
        }

        _cachedText = null;
        _hasText = false;
        return null;
    }

    /// <summary>
    /// Async method to check if clipboard contains text (preferred)
    /// </summary>
    public async Task<bool> ContainsTextAsync()
    {
        var text = await GetTextAsync();
        return !string.IsNullOrEmpty(text);
    }
}

