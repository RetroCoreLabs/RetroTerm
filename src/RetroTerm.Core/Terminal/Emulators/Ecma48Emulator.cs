using System;
using RetroTerm.Core.Terminal.Profiles;

namespace RetroTerm.Core.Terminal.Emulators;

/// <summary>
/// A terminal that is exactly what its profile says it is.
///
/// The base class implements ECMA-48/ISO 6429; a terminal that adds no behaviour of its own — only
/// a different identity, DA reply and capability set — needs no code, just a profile. This class
/// is the pairing of the two, and it is what makes the claim in TerminalProfile ("a profile is
/// data, so a new terminal that differs only in identity does not need a new class") true rather
/// than aspirational.
///
/// Terminals that DO add behaviour (the TDV family, and the VT variants once soft fonts and
/// national character sets land) still subclass. This is for the ones that do not.
/// </summary>
public sealed class Ecma48Emulator : TerminalEmulatorBase
{
    private readonly TerminalProfile _profile;

    public Ecma48Emulator(TerminalProfile profile, int width = 80, int height = 24, int maxScrollback = 10000)
        : base(width, height, maxScrollback)
    {
        _profile = profile ?? throw new ArgumentNullException(nameof(profile));
    }

    /// <inheritdoc/>
    public override TerminalProfile Profile => _profile;

    public override string ToString() => _profile.Name;
}
