using System;
using RetroTerm.Core.Terminal.Emulators;
using RetroTerm.Core.Terminal.Emulators.TDV;
using RetroTerm.Core.Terminal.Emulators.Tektronix;
using RetroTerm.Core.Terminal.Profiles;

namespace RetroTerm.Core.Configuration;

/// <summary>
/// Factory for creating terminal emulators based on configuration
/// </summary>
public static class EmulatorFactory
{
    /// <summary>
    /// Available emulator types
    /// </summary>
    public static readonly string[] AvailableEmulators =
    {
        "VT100",
        "VT220",
        "TDV1200",
        "TDV2215",
        "TDV2200",
        "TEK4014",
        "VT52",
        "VT102",
        "VT240",
        "VT320",
        "VT340",
        "VT420",
        "XTERM",
        "XTERM-256COLOR"
    };

    /// <summary>
    /// Creates a terminal emulator based on configuration
    /// </summary>
    public static TerminalEmulatorBase CreateEmulator(HostConfiguration configuration)
    {
        if (configuration == null)
            throw new ArgumentNullException(nameof(configuration));

        return configuration.EmulatorType.ToUpperInvariant() switch
        {
            "VT100" => new VT100Emulator(configuration.Width, configuration.Height, configuration.MaxScrollback),

            // A generic ANSI/ECMA-48 terminal. No subclass: it differs from VT100 only in identity
            // and capabilities, which is exactly what a profile is for.
            "ANSI" => new Ecma48Emulator(TerminalProfile.Ansi, configuration.Width, configuration.Height, configuration.MaxScrollback),

            // A VT220 that says so. This used to build a VT100 answering as a VT100, because the
            // VT220 extensions were not implemented and a truthful VT100 beat a name it could not
            // live up to. Selective erase and DECCOLM have since landed, so the identity is now
            // one this terminal can back up - and its DA reply lists those two extensions and no
            // others. Soft character sets, DECUDK and the wider national sets are still absent, so
            // they are still absent from what it claims.
            "VT220" => new Ecma48Emulator(TerminalProfile.VT220, configuration.Width, configuration.Height, configuration.MaxScrollback),
            "TDV1200" => new TDV1200Emulator(configuration.Width, configuration.Height, configuration.MaxScrollback),
            "TDV2215" => new TDV2215Emulator(configuration.Width, configuration.Height, configuration.MaxScrollback),
            "TDV2200" => new TDV2200Emulator(configuration.Width, configuration.Height, configuration.MaxScrollback),

            // The VT102: a VT100 that can insert and delete. All four editing sequences are in the
            // base already, so this is the identity rather than new behaviour - and a host that
            // recognises the DA will use them instead of redrawing whole lines.
            "VT102" => new Ecma48Emulator(TerminalProfile.VT102, configuration.Width, configuration.Height, configuration.MaxScrollback),

            // The VT52: the terminal before ANSI, whose escapes are a bare ESC and one letter.
            // It needs a class of its own because those are not CSI sequences at all.
            "VT52" => new Vt52Emulator(configuration.Width, configuration.Height, configuration.MaxScrollback),

            // The VT240: a VT220 with ReGIS, on what was a monochrome screen. Pair it with a
            // single-phosphor theme and the drawing collapses to the phosphor with the text.
            "VT240" => new Ecma48Emulator(TerminalProfile.VT240, configuration.Width, configuration.Height, configuration.MaxScrollback),

            // The VT320: the VT300-series TEXT terminal. It differs from the VT340 by not having
            // graphics, and from the VT220 by page memory and by saying it is service class 63 -
            // which is DEC's own number for the VT300 family, from the VT330/VT340 manual.
            "VT320" => new Ecma48Emulator(TerminalProfile.VT320, configuration.Width, configuration.Height, configuration.MaxScrollback),

            // The VT340: a VT300-series terminal with Sixel graphics, which is the machine that
            // feature comes from. No subclass - it differs from the others in identity and
            // capabilities, which is what a profile is for.
            "VT340" => new Ecma48Emulator(TerminalProfile.VT340, configuration.Width, configuration.Height, configuration.MaxScrollback),

            // The VT420: the VT400 series, whose one addition here is left and right margins.
            "VT420" => new Ecma48Emulator(TerminalProfile.VT420, configuration.Width, configuration.Height, configuration.MaxScrollback),

            // xterm. No subclass: it differs from the VT100 in identity and capabilities, which is
            // what a profile is for. The two entries differ only in the name that reaches the host,
            // and that name is what picks the terminfo entry there.
            "XTERM" => new Ecma48Emulator(TerminalProfile.Xterm, configuration.Width, configuration.Height, configuration.MaxScrollback),
            "XTERM-256COLOR" => new Ecma48Emulator(TerminalProfile.Xterm256, configuration.Width, configuration.Height, configuration.MaxScrollback),

            // The Tektronix 4014 as itself, rather than as the half of a TDV2200 nobody could reach
            // without picking a Norwegian terminal first.
            "TEK4014" => new Tek4014Emulator(configuration.Width, configuration.Height, configuration.MaxScrollback),
            _ => throw new NotSupportedException($"Emulator type '{configuration.EmulatorType}' is not supported")
        };
    }

    /// <summary>
    /// Creates a terminal emulator with default settings
    /// </summary>
    public static TerminalEmulatorBase CreateEmulator(string emulatorType, int width = 80, int height = 24, int maxScrollback = 1000)
    {
        return CreateEmulator(new HostConfiguration
        {
            EmulatorType = emulatorType,
            Width = width,
            Height = height,
            MaxScrollback = maxScrollback
        });
    }

    /// <summary>
    /// Creates an emulator to REPLACE the one a session is running, sized the way that terminal
    /// should be.
    /// </summary>
    /// <remarks>
    /// <para><b>The size rule, in one place</b></para>
    /// A terminal whose geometry is part of what it is snaps to its own: a TDV2200 becomes 80 by 25
    /// and a 4014 becomes 74 by 35, whatever the previous terminal was. One that follows the window
    /// keeps the size already on screen, because the window is about to decide it again anyway.
    /// The line between the two is <c>TerminalFeatures.HostResize</c>, read off the profile that was
    /// just built - so there is no second list of "which terminals are fixed" to drift out of step
    /// with the profiles.
    /// This lives in the factory because the menu, the script command and the MCP tool all need the
    /// same answer, and a rule applied at three call sites is a rule that will be applied at two.
    /// </remarks>
    /// <param name="emulatorType">
    /// Terminal type to build.
    /// </param>
    /// <param name="currentWidth">
    /// Columns the session has now.
    /// </param>
    /// <param name="currentHeight">
    /// Rows the session has now.
    /// </param>
    /// <param name="maxScrollback">
    /// Scrollback lines; pass 0 to take the type's own default.
    /// </param>
    /// <returns>
    /// The new emulator, sized and ready to be handed to the session.
    /// </returns>
    public static TerminalEmulatorBase CreateForReplacement(string emulatorType, int currentWidth,
        int currentHeight, int maxScrollback = 0)
    {
        var (width, height) = GetRecommendedSize(emulatorType);
        int scrollback = maxScrollback > 0 ? maxScrollback : GetDefaultScrollback(emulatorType);

        var emulator = CreateEmulator(emulatorType, width, height, scrollback);

        if (emulator.Profile.Supports(TerminalFeatures.HostResize)
            && currentWidth > 0 && currentHeight > 0)
        {
            emulator.Resize(currentWidth, currentHeight);
        }

        return emulator;
    }

    /// <summary>
    /// Gets the default scrollback size for an emulator type
    /// </summary>
    public static int GetDefaultScrollback(string emulatorType)
    {
        return emulatorType.ToUpperInvariant() switch
        {
            "VT100" => 1000,
            "VT220" => 1000,
            "TDV1200" => 1500,
            "TDV2215" => 2500,
            "TDV2200" => 1800,

            // A storage tube has no scrollback at all - the tube IS the memory. The window this
            // runs in does, and 1000 lines is the ordinary default rather than a claim about the
            // hardware.
            "TEK4014" => 1000,
            "VT52" => 1000,
            "VT102" => 1000,
            "VT240" => 1000,
            "VT340" => 1000,
            "VT420" => 1000,
            "XTERM" => 1000,
            "XTERM-256COLOR" => 1000,
            _ => 1000
        };
    }

    /// <summary>
    /// The profile of a terminal type, without building an emulator to get at it.
    /// </summary>
    /// <remarks>
    /// One list, the same one <see cref="CreateEmulator(HostConfiguration)"/> builds from, so a
    /// type the factory can build always has a profile here and a type it cannot build throws
    /// the same way. Everything that used to be a per-type switch in this class (screen size,
    /// the Backspace default) is read off the profile now, so a new terminal is described in one
    /// place and nothing here has to be updated to know about it.
    /// </remarks>
    /// <param name="emulatorType">
    /// One of <see cref="AvailableEmulators"/>, or "ANSI"; case does not matter.
    /// </param>
    /// <returns>
    /// The shared, immutable profile for that terminal.
    /// </returns>
    public static TerminalProfile GetProfile(string emulatorType)
    {
        if (emulatorType == null) throw new ArgumentNullException(nameof(emulatorType));
        return emulatorType.ToUpperInvariant() switch
        {
            "VT100" => TerminalProfile.VT100,
            "ANSI" => TerminalProfile.Ansi,
            "VT220" => TerminalProfile.VT220,
            "TDV1200" => TDV1200Emulator.TdvProfile,
            "TDV2215" => TDV2215Emulator.TdvProfile,
            "TDV2200" => TDV2200Emulator.TdvProfile,
            "VT102" => TerminalProfile.VT102,
            "VT52" => TerminalProfile.VT52,
            "VT240" => TerminalProfile.VT240,
            "VT320" => TerminalProfile.VT320,
            "VT340" => TerminalProfile.VT340,
            "VT420" => TerminalProfile.VT420,
            "XTERM" => TerminalProfile.Xterm,
            "XTERM-256COLOR" => TerminalProfile.Xterm256,
            "TEK4014" => TerminalProfile.Tek4014,
            _ => throw new NotSupportedException($"Emulator type '{emulatorType}' is not supported")
        };
    }

    /// <summary>
    /// The screen size a terminal type should be given when it is created or chosen: the
    /// profile's own <see cref="TerminalProfile.Columns"/> and <see cref="TerminalProfile.Rows"/>.
    /// </summary>
    /// <remarks>
    /// Until 27 September 2026 this was its own switch, which said 24 rows for the TDV1200 with
    /// no citation, and was asked by only one of the nine places that pick a terminal type. See
    /// the remarks on <see cref="TerminalProfile.Columns"/>. An unknown type answers 80 by 24
    /// rather than throwing, because callers pass whatever a saved file or a script said.
    /// </remarks>
    /// <param name="emulatorType">
    /// The terminal type by name, as in <see cref="AvailableEmulators"/>; case does not matter.
    /// </param>
    /// <returns>
    /// Columns and rows for that terminal, or 80 by 24 for a name this build does not know.
    /// </returns>
    public static (int width, int height) GetRecommendedSize(string emulatorType)
    {
        try
        {
            var profile = GetProfile(emulatorType);
            return (profile.Columns, profile.Rows);
        }
        catch (NotSupportedException)
        {
            return (80, 24);
        }
    }

    /// <summary>
    /// Whether Backspace sends DEL on this terminal type when the connection has not chosen.
    /// </summary>
    /// <remarks>
    /// Read from the profile's <see cref="TerminalProfile.BackspaceSendsDel"/>: true for every TDV,
    /// false for the rest. An unknown type answers false, which is what this program always sent.
    /// </remarks>
    /// <param name="emulatorType">
    /// The terminal type by name, as in <see cref="AvailableEmulators"/>; case does not matter.
    /// </param>
    /// <returns>
    /// True when Backspace should send DEL (0x7F), false when it should send BS (0x08).
    /// </returns>
    public static bool GetDefaultBackspaceSendsDel(string emulatorType)
    {
        try
        {
            return GetProfile(emulatorType).BackspaceSendsDel;
        }
        catch (NotSupportedException)
        {
            return false;
        }
    }

    /// <summary>
    /// Validates that an emulator type is supported
    /// </summary>
    /// <param name="emulatorType">
    /// Type name to test; null/unknown simply returns false.
    /// </param>
    public static bool IsSupported(string? emulatorType)
    {
        return Array.Exists(AvailableEmulators, e =>
            e.Equals(emulatorType, StringComparison.OrdinalIgnoreCase));
    }
}
