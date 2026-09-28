using System;
using RetroTerm.Core.Terminal.Buffer;

namespace RetroTerm.Core.Terminal.Rendering;

/// <summary>
/// Interface for terminal-specific font rendering strategies.
/// Each terminal emulator provides its own font renderer implementation.
/// Implementations are in the Desktop project (Avalonia-specific).
/// </summary>
public interface IFontRenderer
{
    /// <summary>
    /// Gets the character width in pixels
    /// </summary>
    double GetCharWidth();

    /// <summary>
    /// Gets the character height in pixels
    /// </summary>
    double GetCharHeight();

    /// <summary>
    /// Renders a character to the drawing context.
    /// The context type is platform-specific (Avalonia DrawingContext for Desktop).
    /// </summary>
    /// <param name="context">
    /// The platform-specific drawing context
    /// </param>
    /// <param name="cell">
    /// The terminal cell to render
    /// </param>
    /// <param name="x">
    /// X position in pixels
    /// </param>
    /// <param name="y">
    /// Y position in pixels
    /// </param>
    /// <param name="foreground">
    /// Foreground brush (platform-specific)
    /// </param>
    void DrawCharacter(object context, TerminalCell cell, double x, double y, object foreground);
}
