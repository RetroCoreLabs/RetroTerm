using System;
using System.Text;

namespace RetroTerm.Core.Clipboard;

/// <summary>
/// Clipboard service interface for platform-specific clipboard operations
/// </summary>
public interface IClipboardService
{
    /// <summary>
    /// Gets text from the clipboard
    /// </summary>
    string? GetText();

    /// <summary>
    /// Sets text to the clipboard
    /// </summary>
    void SetText(string text);

    /// <summary>
    /// Checks if clipboard contains text
    /// </summary>
    bool ContainsText();
}

/// <summary>
/// Manages clipboard operations for terminal text
/// </summary>
public class ClipboardManager
{
    private readonly IClipboardService _clipboardService;

    public ClipboardManager(IClipboardService clipboardService)
    {
        _clipboardService = clipboardService ?? throw new ArgumentNullException(nameof(clipboardService));
    }

    /// <summary>
    /// Copies text to clipboard
    /// </summary>
    public void Copy(string text)
    {
        if (string.IsNullOrEmpty(text))
            return;

        // Sanitize text: remove control characters except newlines and tabs
        var sanitized = SanitizeText(text);

        _clipboardService.SetText(sanitized);
    }

    /// <summary>
    /// Gets text from clipboard
    /// </summary>
    public string? Paste()
    {
        if (!_clipboardService.ContainsText())
            return null;

        var text = _clipboardService.GetText();

        if (string.IsNullOrEmpty(text))
            return null;

        // Sanitize pasted text: convert line endings and filter dangerous characters
        return SanitizePasteText(text);
    }

    /// <summary>
    /// Checks if clipboard contains text
    /// </summary>
    public bool CanPaste()
    {
        return _clipboardService.ContainsText();
    }

    /// <summary>
    /// Sanitizes text for copying (removes control characters except newlines/tabs)
    /// </summary>
    private string SanitizeText(string text)
    {
        var result = new StringBuilder(text.Length);

        foreach (var ch in text)
        {
            // Allow printable characters, newlines, tabs
            if (char.IsControl(ch) && ch != '\n' && ch != '\r' && ch != '\t')
            {
                // Skip control characters
                continue;
            }

            result.Append(ch);
        }

        return result.ToString();
    }

    /// <summary>
    /// Sanitizes text for pasting (converts line endings, filters dangerous characters)
    /// </summary>
    private string SanitizePasteText(string text)
    {
        var result = new StringBuilder(text.Length);

        foreach (var ch in text)
        {
            // Convert Windows line endings to Unix
            if (ch == '\r')
            {
                // Skip \r, we'll add \n if needed
                continue;
            }

            // Filter out dangerous control characters
            if (char.IsControl(ch) && ch != '\n' && ch != '\t')
            {
                // Skip dangerous control characters (ESC, BEL, etc.)
                continue;
            }

            result.Append(ch);
        }

        return result.ToString();
    }

    /// <summary>
    /// Estimates the size of text for paste confirmation
    /// </summary>
    public int EstimateSize(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return 0;

        return text.Length;
    }

    /// <summary>
    /// Checks if text is considered "large" and should prompt for confirmation
    /// </summary>
    public bool IsLargePaste(string? text, int threshold = 1000)
    {
        return EstimateSize(text) > threshold;
    }
}

