using RetroTerm.Core.Terminal.Buffer;

namespace RetroTerm.Core.Terminal.Emulators.TDV.Components;

/// <summary>
/// Manages character set switching, SS2/SS3, and font numbers for TDV terminals
/// </summary>
public class TDVCharacterSetManager
{
    private readonly TDVEmulatorBase _emulator;
    private bool _isSS2Active = false;
    private bool _isSS3Active = false;
    private byte _nextFontNumber = 0;

    public TDVCharacterSetManager(TDVEmulatorBase emulator)
    {
        _emulator = emulator ?? throw new ArgumentNullException(nameof(emulator));
    }

    /// <summary>
    /// Gets whether SS2 (Single Shift 2) is currently active
    /// </summary>
    public bool IsSS2Active => _isSS2Active;

    /// <summary>
    /// Gets whether SS3 (Single Shift 3) is currently active
    /// </summary>
    public bool IsSS3Active => _isSS3Active;

    /// <summary>
    /// Gets the font number for the next character
    /// </summary>
    public byte NextFontNumber => _nextFontNumber;

    /// <summary>
    /// Activate SS2 (Single Shift to G2)
    /// </summary>
    public void ActivateSS2()
    {
        _isSS2Active = true;
        _nextFontNumber = 2; // Use fontNum 2 (graphics/line drawing, offset 128)
    }

    /// <summary>
    /// Activate SS3 (Single Shift to G3)
    /// </summary>
    public void ActivateSS3()
    {
        _isSS3Active = true;
        _nextFontNumber = 3; // Use fontNum 3 (subscript/superscript, offset 256)
    }

    /// <summary>
    /// Process a character when SS2 is active
    /// Returns the mapped character and font number, or null if SS2 is not active
    /// </summary>
    public (char mappedChar, byte fontNumber)? ProcessSingleShift2(char c)
    {
        if (!_isSS2Active)
            return null;

        // Get G2 character set from emulator
        var g2CharacterSet = _emulator.GetG2CharacterSet();

        // Map character using the specified character set
        char mappedChar = TDVCharacterSets.GetCharacter(g2CharacterSet, c);

        // Clear SS2 flag after processing
        _isSS2Active = false;
        byte fontNum = _nextFontNumber;
        _nextFontNumber = 0;

        return (mappedChar, fontNum);
    }

    /// <summary>
    /// Process a character when SS3 is active
    /// Returns the mapped character and font number, or null if SS3 is not active
    /// </summary>
    public (char mappedChar, byte fontNumber)? ProcessSingleShift3(char c)
    {
        if (!_isSS3Active)
            return null;

        // Get G3 character set from emulator
        var g3CharacterSet = _emulator.GetG3CharacterSet();

        // Map character using the specified character set
        char mappedChar = TDVCharacterSets.GetCharacter(g3CharacterSet, c);

        // Clear SS3 flag after processing
        _isSS3Active = false;
        byte fontNum = _nextFontNumber;
        _nextFontNumber = 0;

        return (mappedChar, fontNum);
    }

    /// <summary>
    /// Check if SS2/SS3 is active and process the character if so
    /// Returns the mapped character and font number if handled, or null if not handled
    /// </summary>
    public (char mappedChar, byte fontNumber)? HandleSingleShift(char c)
    {
        if (_isSS2Active)
        {
            return ProcessSingleShift2(c);
        }

        if (_isSS3Active)
        {
            return ProcessSingleShift3(c);
        }

        return null;
    }

    /// <summary>
    /// Reset character set manager to initial state
    /// </summary>
    public void Reset()
    {
        _isSS2Active = false;
        _isSS3Active = false;
        _nextFontNumber = 0;
    }

    /// <summary>
    /// Clear SS2/SS3 flags (called after character is processed)
    /// </summary>
    public void ClearSingleShiftFlags()
    {
        _isSS2Active = false;
        _isSS3Active = false;
        _nextFontNumber = 0;
    }
}

