using RetroTerm.Core.Terminal.Profiles;

namespace RetroTerm.Core.Terminal.Emulators;

/// <summary>
/// VT100 terminal emulator implementation.
/// Most VT100 functionality is provided by the base class.
/// This class can override specific behaviors if needed.
/// </summary>
public class VT100Emulator : TerminalEmulatorBase
{
    public VT100Emulator(int width = 80, int height = 24, int maxScrollback = 10000)
        : base(width, height, maxScrollback)
    {
    }

    /// <inheritdoc/>
    public override TerminalProfile Profile => TerminalProfile.VT100;

    /// <summary>
    /// Gets the terminal type identification
    /// </summary>
    public override string ToString() => "VT100";
}

