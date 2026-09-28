using System;
using RetroTerm.Core.Terminal.Buffer;

namespace RetroTerm.Core.Terminal.Emulators;

/// <summary>
/// Interface defining the contract for all terminal emulators.
/// This enables polymorphism, dependency injection, and improved testability.
/// </summary>
public interface ITerminalEmulator
{
    /// <summary>
    /// Gets the terminal width in columns
    /// </summary>
    int Width { get; }

    /// <summary>
    /// Gets the terminal height in rows
    /// </summary>
    int Height { get; }

    /// <summary>
    /// Gets the terminal title
    /// </summary>
    string Title { get; }

    /// <summary>
    /// Event raised when the terminal needs to be repainted
    /// </summary>
    event Action? Invalidated;

    /// <summary>
    /// Event raised when the terminal title changes
    /// </summary>
    event Action<string>? TitleChanged;

    /// <summary>
    /// Event raised when the terminal bell is triggered
    /// </summary>
    event Action? Bell;

    /// <summary>
    /// Event raised when data needs to be sent back to the host (keyboard input, responses)
    /// </summary>
    event Action<byte[]>? DataToSend;

    /// <summary>
    /// Gets the terminal buffer for rendering
    /// </summary>
    /// <returns>
    /// The terminal buffer containing all cell data
    /// </returns>
    TerminalBuffer GetBuffer();

    /// <summary>
    /// Gets the cursor for rendering
    /// </summary>
    /// <returns>
    /// The cursor with position and style information
    /// </returns>
    Cursor GetCursor();

    /// <summary>
    /// Processes incoming data from the host
    /// </summary>
    /// <param name="data">
    /// Raw byte data to process
    /// </param>
    void ProcessData(ReadOnlySpan<byte> data);

    /// <summary>
    /// Resets the terminal to its initial state
    /// </summary>
    void Reset();

    /// <summary>
    /// Resizes the terminal to new dimensions
    /// </summary>
    /// <param name="newWidth">
    /// New width in columns
    /// </param>
    /// <param name="newHeight">
    /// New height in rows
    /// </param>
    void Resize(int newWidth, int newHeight);
}

