using System;

namespace RetroTerm.Core.Terminal.Emulators.TDV.Components;

/// <summary>
/// Handles ISO646 variant selection for TDV terminals
/// Supports Norwegian, Swedish, Danish, Finnish, German, and US variants
/// </summary>
public class TDVISO646VariantHandler
{
    private readonly TDVEmulatorBase _emulator;
    private bool _expectingIso646Variant = false;
    // Default to International (US ASCII) per TDV2115 spec section 9.1.1
    private TDV2200ISO646Variant _currentISO646Variant = TDV2200ISO646Variant.International;

    public TDVISO646VariantHandler(TDVEmulatorBase emulator)
    {
        _emulator = emulator ?? throw new ArgumentNullException(nameof(emulator));
    }

    /// <summary>
    /// Gets the current ISO646 variant
    /// </summary>
    public TDV2200ISO646Variant CurrentISO646Variant => _currentISO646Variant;

    /// <summary>
    /// Gets whether we're expecting an ISO646 variant selection character
    /// </summary>
    public bool IsExpectingVariant => _expectingIso646Variant;

    /// <summary>
    /// Start expecting an ISO646 variant selection (called when ESC % is received)
    /// </summary>
    public void StartExpectingVariant()
    {
        _expectingIso646Variant = true;
    }

    /// <summary>
    /// Handle a character that might be an ISO646 variant selection
    /// Returns true if the character was handled as a variant selection
    /// </summary>
    public bool HandleVariantSelection(char c)
    {
        if (!_expectingIso646Variant)
            return false;

        // Per TDV2115 spec section 9.1
        switch (c)
        {
            case 'I':
                _currentISO646Variant = TDV2200ISO646Variant.International;
                break;
            case 'N':
                _currentISO646Variant = TDV2200ISO646Variant.Norwegian;
                break;
            case 'S':
                _currentISO646Variant = TDV2200ISO646Variant.Swedish;
                break;
            case 'G':
                _currentISO646Variant = TDV2200ISO646Variant.German;
                break;
            default:
                return false;
        }

        _expectingIso646Variant = false;
        return true;
    }

    /// <summary>
    /// Reset the variant handler to initial state (International/US per TDV2115 spec 9.1.1)
    /// </summary>
    public void Reset()
    {
        _expectingIso646Variant = false;
        _currentISO646Variant = TDV2200ISO646Variant.International;
    }

    /// <summary>
    /// Set the ISO646 variant
    /// </summary>
    public void SetVariant(TDV2200ISO646Variant variant)
    {
        _currentISO646Variant = variant;
    }
}

