using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using RetroTerm.Core.Terminal;
using RetroTerm.Core.Terminal.Buffer;
using RetroTerm.Core.Terminal.Emulators.TDV;

namespace RetroTerm.Tests.TDV.TDV2200Validation;

/// <summary>
/// UI Automation Server interface for automated UI validation
/// Provides commands to interact with RetroTerm.Desktop for validation testing
/// </summary>
public interface IUIAutomationServer
{
    /// <summary>
    /// Sends a sequence to the terminal
    /// </summary>
    Task SendSequenceAsync(string sequence);

    /// <summary>
    /// Captures the current screen state
    /// </summary>
    Task<ScreenCapture> CaptureScreenAsync();

    /// <summary>
    /// Gets the current cursor position
    /// </summary>
    Task<(int row, int col)> GetCursorPositionAsync();

    /// <summary>
    /// Gets cell attributes at a specific position
    /// </summary>
    Task<CellAttributes> GetCellAttributesAsync(int row, int col);

    /// <summary>
    /// Sends a key press to the terminal
    /// </summary>
    Task SendKeyAsync(string keyName, bool shift = false, bool ctrl = false, bool alt = false);

    /// <summary>
    /// Sends a query and captures the response
    /// </summary>
    Task<string?> QueryResponseAsync(string query, TimeSpan timeout);

    /// <summary>
    /// Connects to the automation server
    /// </summary>
    Task ConnectAsync();

    /// <summary>
    /// Disconnects from the automation server
    /// </summary>
    Task DisconnectAsync();
}

/// <summary>
/// Screen capture data structure
/// </summary>
public class ScreenCapture
{
    public TerminalCell[,] Cells { get; set; } = new TerminalCell[0, 0];
    public int Width { get; set; }
    public int Height { get; set; }
    public (int row, int col) CursorPosition { get; set; }
}

/// <summary>
/// Cell attributes for validation
/// </summary>
public class CellAttributes
{
    public uint Codepoint { get; set; }
    public CharacterAttributes Attributes { get; set; }
    public TerminalColor Foreground { get; set; }
    public TerminalColor Background { get; set; }
    public byte FontNumber { get; set; }
}

/// <summary>
/// In-memory UI automation server implementation for unit tests
/// Uses emulator directly instead of actual UI
/// </summary>
public class InMemoryUIAutomationServer : IUIAutomationServer
{
    private readonly TDV2200Emulator _emulator;
    private readonly List<string> _responses = new();
    private bool _connected = false;

    public InMemoryUIAutomationServer(TDV2200Emulator emulator)
    {
        _emulator = emulator ?? throw new ArgumentNullException(nameof(emulator));
        _emulator.OnResponseReady += response => _responses.Add(response);
    }

    public Task ConnectAsync()
    {
        _connected = true;
        return Task.CompletedTask;
    }

    public Task DisconnectAsync()
    {
        _connected = false;
        return Task.CompletedTask;
    }

    public Task SendSequenceAsync(string sequence)
    {
        if (!_connected)
            throw new InvalidOperationException("Not connected");

        var bytes = System.Text.Encoding.UTF8.GetBytes(sequence);
        _emulator.ProcessInput(bytes);
        return Task.CompletedTask;
    }

    public Task<ScreenCapture> CaptureScreenAsync()
    {
        if (!_connected)
            throw new InvalidOperationException("Not connected");

        var buffer = _emulator.GetBuffer();
        var cursor = _emulator.GetCursor();
        var cells = new TerminalCell[_emulator.Height, _emulator.Width];

        for (int row = 0; row < _emulator.Height; row++)
        {
            for (int col = 0; col < _emulator.Width; col++)
            {
                cells[row, col] = buffer[row, col];
            }
        }

        return Task.FromResult(new ScreenCapture
        {
            Cells = cells,
            Width = _emulator.Width,
            Height = _emulator.Height,
            CursorPosition = (cursor.Row, cursor.Column)
        });
    }

    public Task<(int row, int col)> GetCursorPositionAsync()
    {
        if (!_connected)
            throw new InvalidOperationException("Not connected");

        var cursor = _emulator.GetCursor();
        return Task.FromResult((cursor.Row, cursor.Column));
    }

    public Task<CellAttributes> GetCellAttributesAsync(int row, int col)
    {
        if (!_connected)
            throw new InvalidOperationException("Not connected");

        var buffer = _emulator.GetBuffer();
        if (row >= 0 && row < _emulator.Height && col >= 0 && col < _emulator.Width)
        {
            var cell = buffer[row, col];
            return Task.FromResult(new CellAttributes
            {
                Codepoint = cell.Codepoint,
                Attributes = cell.Attributes,
                Foreground = cell.Foreground,
                Background = cell.Background,
                FontNumber = cell.FontNumber
            });
        }

        throw new ArgumentOutOfRangeException($"Cell position ({row}, {col}) is out of range");
    }

    public Task SendKeyAsync(string keyName, bool shift = false, bool ctrl = false, bool alt = false)
    {
        if (!_connected)
            throw new InvalidOperationException("Not connected");

        _emulator.HandleKeyPress(keyName, shift, ctrl, alt);
        return Task.CompletedTask;
    }

    public async Task<string?> QueryResponseAsync(string query, TimeSpan timeout)
    {
        if (!_connected)
            throw new InvalidOperationException("Not connected");

        _responses.Clear();
        var bytes = System.Text.Encoding.UTF8.GetBytes(query);
        _emulator.ProcessInput(bytes);

        // Wait for response with timeout
        var startTime = DateTime.Now;
        while (DateTime.Now - startTime < timeout)
        {
            if (_responses.Count > 0)
            {
                return _responses[0];
            }
            await Task.Delay(10);
        }

        return null;
    }
}

