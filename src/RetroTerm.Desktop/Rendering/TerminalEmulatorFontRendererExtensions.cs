using RetroTerm.Core.Fonts;
using RetroTerm.Core.Terminal.Emulators;
using RetroTerm.Core.Terminal.Emulators.TDV;
using RetroTerm.Core.Terminal.Rendering;

namespace RetroTerm.Desktop.Rendering;

/// <summary>
/// Extension methods to provide font renderer implementations for terminal emulators.
/// This bridges the Core (interface) and Desktop (implementation) layers.
/// </summary>
public static class TerminalEmulatorFontRendererExtensions
{
    /// <summary>
    /// Creates a font renderer for the given emulator type.
    /// Each terminal type provides its own font rendering strategy.
    /// </summary>
    public static IFontRenderer CreateFontRenderer(this TerminalEmulatorBase emulator)
    {
        return emulator switch
        {
            TDV2200Emulator => new BitmapFontRenderer(new FontTDV2200()),
            TDV2215Emulator => new BitmapFontRenderer(new FontTDV2215()),
            // Default for VT100, VT220, TDV1200 and the rest. The emulator's downloaded character
            // set is handed over rather than copied, so a set that arrives mid-session is drawn
            // without anything having to notice and pass it again.
            _ => new SystemFontRenderer { SoftFont = emulator.SoftFont },
        };
    }
}

