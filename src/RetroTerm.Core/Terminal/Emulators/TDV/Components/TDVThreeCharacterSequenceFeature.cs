using System;

namespace RetroTerm.Core.Terminal.Emulators.TDV.Components;

/// <summary>
/// TDV2215 Three-Character ESC Sequence Feature - Reusable component for any TDV terminal
/// Handles ESC ? ? h/l and ESC # # sequences
/// </summary>
public class TDVThreeCharacterSequenceFeature
{
    private readonly TDVExtendedModeFeature _extendedMode;
    private readonly TDVTransparentModeFeature _transparentMode;

    public TDVThreeCharacterSequenceFeature(
        TDVExtendedModeFeature extendedMode,
        TDVTransparentModeFeature transparentMode)
    {
        _extendedMode = extendedMode ?? throw new ArgumentNullException(nameof(extendedMode));
        _transparentMode = transparentMode ?? throw new ArgumentNullException(nameof(transparentMode));
    }

    /// <summary>
    /// Handle three-character ESC sequence
    /// Returns true if the sequence was handled
    /// </summary>
    public bool HandleThreeCharacterSequence(char final, ReadOnlySpan<byte> intermediates, TDVEmulatorBase emulator)
    {
        if (intermediates.Length < 2) return false;

        var inter1 = (char)intermediates[0];
        var inter2 = (char)intermediates[1];

        // TDV2215 specific three-character sequences
        switch (inter1)
        {
            case '?':
                return HandleThreeCharacterPrivateSequence(final, inter2);

            case '#':
                return HandleThreeCharacterHashSequence(final, inter2);

            default:
                return false;
        }
    }

    /// <summary>
    /// Handle three-character private sequences (ESC ? ? h/l)
    /// </summary>
    private bool HandleThreeCharacterPrivateSequence(char final, char inter2)
    {
        switch (inter2)
        {
            case '1': // Extended mode control
                switch (final)
                {
                    case 'h': // Enable extended mode
                        _extendedMode.Enable();
                        return true;

                    case 'l': // Disable extended mode
                        _extendedMode.Disable();
                        return true;
                }
                break;

            case '2': // Transparent mode control
                switch (final)
                {
                    case 'h': // Enable transparent mode
                        _transparentMode.Enable();
                        return true;

                    case 'l': // Disable transparent mode
                        _transparentMode.Disable();
                        return true;
                }
                break;
        }

        return false;
    }

    /// <summary>
    /// Handle three-character hash sequences (ESC # #)
    /// </summary>
    private bool HandleThreeCharacterHashSequence(char final, char inter2)
    {
        // TODO: Implement three-character hash sequences
        return false;
    }
}

