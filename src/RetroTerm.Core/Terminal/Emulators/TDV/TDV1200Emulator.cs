using System;
using System.Text;
using RetroTerm.Core.Terminal.Buffer;
using RetroTerm.Core.Terminal.Emulators.TDV.Components;
using RetroTerm.Core.Terminal.Parsing;

namespace RetroTerm.Core.Terminal.Emulators.TDV;

/// <summary>
/// TDV1200 emulator with full Norsk Data compatibility
/// Implements ISO 646/2022/6429 compliant base with 2115 compatibility mode
/// Uses composition to reuse TDV2200 functionality
/// </summary>
public class TDV1200Emulator : TDVEmulatorBase
{
    /// <inheritdoc/>
    public override Profiles.TerminalProfile Profile => TdvProfile;

    /// <inheritdoc/>
    public override Input.TerminalModes GetActiveModes()
    {
        var modes = base.GetActiveModes();

        // The 1200 has no extended-mode flag of its own; outside 2115 compatibility it maps as
        // the plain TDV keyboard.
        if (Is2115CompatibilityMode) modes |= Input.TerminalModes.TDV2115Mode;

        return modes;
    }

    /// <summary>
    /// The TDV1200's identity. DA reply matches HandleDeviceAttributesQuery below.
    /// </summary>
    public static readonly Profiles.TerminalProfile TdvProfile =
        Profiles.TerminalProfile.ForTdv("TDV1200", "\x1b[?1;2c");

    // Component-based handlers (reusing TDV2200 functionality)

    /// <summary>
    /// Gets whether 2115 compatibility mode is active
    /// </summary>

    public TDV1200Emulator(int width = 80, int height = 24, int maxScrollback = 1500)
        : base(width, height, maxScrollback)
    {
        // Initialize character sets in base class
        _g0CharacterSet = TDVCharacterSets.TDVCharacterSetType.GraphicsI;
        _g1CharacterSet = TDVCharacterSets.TDVCharacterSetType.GraphicsI;
        _g2CharacterSet = TDVCharacterSets.TDVCharacterSetType.GraphicsII;
        _g3CharacterSet = TDVCharacterSets.TDVCharacterSetType.Math;

        // Initialize component handlers (reusing TDV2200 functionality)
    }

    /// <summary>
    /// Gets the maximum scrollback lines for TDV1200 (1500 lines)
    /// </summary>
    public override int MaxScrollback => 1500;

    public override void ResetToInitialState()
    {
        base.ResetToInitialState();

        // Reset component handlers (may be null during base constructor call)
        CompatibilityHandler?.Reset();
        CharacterSetManager?.Reset();
        Iso646Handler?.Reset();
        InputProcessor?.Reset();
    }

    protected override void Handle2115CompatibilityMode(bool enable)
    {
        CompatibilityHandler.Set2115CompatibilityMode(enable);
    }

    /// <summary>
    /// Gets 2115 compatibility mode state
    /// </summary>
    protected override bool Get2115CompatibilityMode()
    {
        return CompatibilityHandler.Is2115CompatibilityMode;
    }

    protected override void HandleCsiSequence(EscapeSequenceParser parser)
    {
        var final = (char)parser.FinalByte;
        var parameters = parser.Parameters;
        var privateMarker = parser.PrivateMarker;

        // Handle query sequences FIRST (before mode sequences)
        // This ensures DA, CPR, DSR, and mode queries are processed
        if (HandleQuerySequence(final, parameters, privateMarker, parser))
        {
            return;
        }

        // 2115 mode enable/disable (?40 and ?66) is handled once in TDVEmulatorBase, so
        // TDV1200 behaves exactly like TDV2215/TDV2200. It used to be duplicated here and
        // also routed CSI A/B/C/D/H/J/K through a stub that claimed to handle them and did
        // nothing, which silently swallowed all cursor movement and erase in 2115 mode.
        // 2115 mode does not change those sequences on any TDV model.

        // Handle TDV1200-specific sequences
        if (HandleTDV1200Sequence(final, parameters, privateMarker))
        {
            return;
        }

        // Fall back to base TDV handling
        base.HandleCsiSequence(parser);
    }

    /// <summary>
    /// Handles TDV1200-specific sequences
    /// </summary>
    private bool HandleTDV1200Sequence(char final, ReadOnlySpan<int> parameters, byte privateMarker)
    {
        // Handle ND-specific sequences from ND-12054-1-EN spec
        switch (final)
        {
            case 'z': // NDSAR - Set Attribute in Rectangle
                HandleSetAttributeInRectangle(parameters);
                return true;

            case 'u': // NDSREC - Save Rectangle
                HandleSaveRectangle(parameters);
                return true;

            case 'v': // NDRREC - Restore Rectangle
                HandleRestoreRectangle(parameters);
                return true;

            case '~': // NDDWA - Define Work Area
                HandleDefineWorkArea(parameters);
                return true;

            case '<': // NDVIDEO - Alpha/Graphics toggle
                HandleVideoToggle(parameters);
                return true;

            default:
                return false;
        }
    }

    // Note: HandleExecute and HandleEscapeSequence are NOT overridden here. 2115 mode C0
    // control codes go through TDV2115CompatibilityHandler via TDVInputProcessor (the same
    // path TDV2215/TDV2200 use), and ESC Q leaves 2115 mode in TDVEmulatorBase. The old
    // overrides consulted a stub that swallowed BEL/BS/HT/LF/CR and ESC D/E/M.

    // ESC # 3/4/5/6 (DECDHL/DECSWL/DECDWL) is TerminalEmulatorBase.HandleLineSize now.
    // This model carried its own override plus four one-line private helpers that did
    // exactly what the shared implementation does; TDV2200 carried a third copy. All that
    // distinguished them was TDV2200 clearing DoubleWidth twice on ESC # 5.

    /// <summary>
    /// Handles video toggle for graphics mode
    /// </summary>
    protected override void HandleVideoToggle(ReadOnlySpan<int> parameters)
    {
        if (parameters.Length < 1) return;

        var mode = parameters[0];

        switch (mode)
        {
            case 0: // Alpha mode
                // TODO: Switch to text mode
                break;

            case 1: // Graphics mode
                // TODO: Switch to graphics mode
                // This would involve enabling graphics rendering
                break;
        }
    }

    /// <summary>
    /// Gets the terminal type identifier
    /// </summary>
    public override string GetTerminalType()
    {
        return Is2115CompatibilityMode ? "TDV2115" : "TDV1200";
    }

    /// <summary>
    /// Gets the terminal capabilities
    /// </summary>
    public override string GetTerminalCapabilities()
    {
        var capabilities = new StringBuilder();
        capabilities.Append("TDV1200");

        if (Is2115CompatibilityMode)
        {
            capabilities.Append("+2115");
        }

        capabilities.Append("+NDGRAPHICS");
        capabilities.Append("+NDWORKAREA");
        capabilities.Append("+NDPROTECTED");
        capabilities.Append("+NDLEDS");
        capabilities.Append("+NDPUSHKEYS");

        return capabilities.ToString();
    }

    // Maximum scrollback: TDV1200 takes it from the constructor parameter, so there is no
    // member here to document - this used to be an XML comment on nothing at all.
    // TDV1200 uses the constructor parameter for MaxScrollback

    #region TDV1200-Specific Query Responses

    /// <summary>
    /// Handles device attributes query for TDV1200
    /// </summary>
    protected override string HandleDeviceAttributesQuery()
    {
        if (Is2115CompatibilityMode)
        {
            return "\x1b[?1;0c"; // TDV2115 compatible response
        }
        return "\x1b[?1;2c"; // TDV1200 response
    }

    /// <summary>
    /// Handles secondary device attributes query for TDV1200
    /// Format: ESC [ > Ps ; Pv ; Pc c where Ps is firmware ID (120 for TDV1200)
    /// </summary>
    protected override string HandleSecondaryDeviceAttributesQuery()
    {
        if (Is2115CompatibilityMode)
        {
            return "\x1b[>115;0;0c"; // TDV2115 firmware ID
        }
        return "\x1b[>120;0;0c"; // TDV1200 firmware ID
    }

    /// <summary>
    /// Handles terminal identification query for TDV1200
    /// </summary>
    protected override string HandleTerminalIdentification()
    {
        if (Is2115CompatibilityMode)
        {
            return "\x1b[?1;0c"; // TDV2115 identification
        }
        return "\x1b[?1;1c"; // TDV1200 identification
    }

    #endregion

    #region Character Set Variant (ISO 646)

    #endregion
}
