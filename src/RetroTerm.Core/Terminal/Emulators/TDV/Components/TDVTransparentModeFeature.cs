using System;

namespace RetroTerm.Core.Terminal.Emulators.TDV.Components;

/// <summary>
/// TDV2215 Transparent Mode Feature - Reusable component for any TDV terminal
/// Provides transparent mode control handling and pass-through sequences
/// </summary>
public class TDVTransparentModeFeature
{
    private bool _isEnabled;

    /// <summary>
    /// Gets whether transparent mode is enabled
    /// </summary>
    public bool IsEnabled => _isEnabled;

    public TDVTransparentModeFeature()
    {
        _isEnabled = false;
    }

    /// <summary>
    /// Enable transparent mode
    /// </summary>
    public void Enable()
    {
        _isEnabled = true;
    }

    /// <summary>
    /// Disable transparent mode
    /// </summary>
    public void Disable()
    {
        _isEnabled = false;
    }

    /// <summary>
    /// Reset transparent mode to initial state
    /// </summary>
    public void Reset()
    {
        _isEnabled = false;
    }

    /// <summary>
    /// Handle CSI sequence in transparent mode
    /// Returns true if the sequence was handled (passed through)
    /// </summary>
    public bool HandleCsiSequence(char final, ReadOnlySpan<int> parameters, byte privateMarker, TDVEmulatorBase emulator)
    {
        if (!_isEnabled) return false;

        // In transparent mode, most sequences are passed through
        // Only handle transparent mode specific sequences
        switch (final)
        {
            case 'P': // DCS - Device Control String
                // Pass through DCS sequences in transparent mode
                return true;

            case '\\': // ST - String Terminator
                // Pass through ST sequences in transparent mode
                return true;

            default:
                // Pass through other sequences
                return false;
        }
    }

    /// <summary>
    /// Handle control character in transparent mode
    /// Returns true if the control was handled (passed through)
    /// </summary>
    public bool HandleControl(byte control, TDVEmulatorBase emulator)
    {
        if (!_isEnabled) return false;

        // In transparent mode, control characters are passed through
        // Only handle transparent mode specific controls
        switch (control)
        {
            case 0x18: // CAN - Cancel
                // Pass through CAN in transparent mode
                return true;

            case 0x1A: // SUB - Substitute
                // Pass through SUB in transparent mode
                return true;

            case 0x1B: // ESC - Escape
                // Pass through ESC in transparent mode
                return true;

            default:
                // Pass through other control characters
                return false;
        }
    }
}

