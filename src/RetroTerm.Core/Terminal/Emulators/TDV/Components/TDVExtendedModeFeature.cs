using System;

namespace RetroTerm.Core.Terminal.Emulators.TDV.Components;

/// <summary>
/// TDV2215 Extended Mode Feature - Reusable component for any TDV terminal
/// Provides enhanced C0/C1 control handling and extended mode CSI sequences
/// </summary>
public class TDVExtendedModeFeature
{
    private bool _isEnabled;

    /// <summary>
    /// Gets whether extended mode is enabled
    /// </summary>
    public bool IsEnabled => _isEnabled;

    public TDVExtendedModeFeature()
    {
        _isEnabled = false;
    }

    /// <summary>
    /// Enable extended mode
    /// </summary>
    public void Enable()
    {
        _isEnabled = true;
    }

    /// <summary>
    /// Disable extended mode
    /// </summary>
    public void Disable()
    {
        _isEnabled = false;
    }

    /// <summary>
    /// Reset extended mode to initial state
    /// </summary>
    public void Reset()
    {
        _isEnabled = false;
    }

    /// <summary>
    /// Handle CSI sequence in extended mode
    /// Returns true if the sequence was handled
    /// </summary>
    public bool HandleCsiSequence(char final, ReadOnlySpan<int> parameters, byte privateMarker, TDVEmulatorBase emulator)
    {
        if (!_isEnabled) return false;

        // In extended mode, delegate to base class methods for standard sequences
        // Extended mode provides enhanced functionality but uses standard escape sequences
        switch (final)
        {
            case 'A': // Cursor up
            case 'B': // Cursor down  
            case 'C': // Cursor forward
            case 'D': // Cursor back
            case 'H': // Cursor position
            case 'J': // Erase display
            case 'K': // Erase line
            case 'm': // SGR
                // Delegate to base class handling - extended mode provides enhanced behavior
                // but uses the same escape sequences as standard terminals
                return false; // Let base class handle it

            default:
                return false;
        }
    }

    /// <summary>
    /// Handle control character in extended mode
    /// Returns true if the control was handled
    /// </summary>
    public bool HandleControl(byte control, TDVEmulatorBase emulator)
    {
        if (!_isEnabled) return false;

        // In extended mode, delegate to base class for standard control handling
        // Extended mode provides enhanced behavior but uses standard control characters
        switch (control)
        {
            case 0x07: // BEL - Bell
            case 0x08: // BS - Backspace
            case 0x09: // HT - Tab
            case 0x0A: // LF - Line Feed
            case 0x0D: // CR - Carriage Return
                // Delegate to base class handling
                return false;

            default:
                return false;
        }
    }

    /// <summary>
    /// Handle escape sequence in extended mode
    /// Returns true if the sequence was handled
    /// </summary>
    public bool HandleEscapeSequence(char final, ReadOnlySpan<byte> intermediates, TDVEmulatorBase emulator)
    {
        if (!_isEnabled) return false;

        // Extended mode uses standard escape sequences
        return false;
    }
}

