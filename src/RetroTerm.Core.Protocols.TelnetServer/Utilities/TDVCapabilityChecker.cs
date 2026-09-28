using System;
using System.Collections.Generic;

namespace RetroTerm.Core.Protocols.TelnetServer.Utilities;

/// <summary>
/// Checks if a terminal supports specific features before running tests.
/// Queries terminal capabilities via DA/DSR and caches results.
/// </summary>
public class TDVCapabilityChecker
{
    private readonly TerminalType _terminalType;
    private readonly Dictionary<string, bool> _capabilityCache = new();

    public TDVCapabilityChecker(TerminalType terminalType)
    {
        _terminalType = terminalType;
        InitializeCapabilities();
    }

    /// <summary>
    /// Initialize capabilities based on terminal type
    /// </summary>
    private void InitializeCapabilities()
    {
        // TDV1200 capabilities
        if (_terminalType == TerminalType.TDV1200)
        {
            _capabilityCache["2115Compatibility"] = true;
            _capabilityCache["NDGraphics"] = true;
            _capabilityCache["ProtectedAreas"] = true;
            _capabilityCache["WorkAreas"] = true;
            _capabilityCache["MessageLEDs"] = true;
            _capabilityCache["CharacterSets"] = true;
            // Both of these were false here until 11 September 2026, and both manuals say
            // otherwise. ND Display Terminal 1200 section 5.64 lists mode 66, the 2115/extended
            // switch, and chapter 3 of the set-up functions lists "Transparent mode:
            // Disabled / Enabled". Transparent is a set-up menu option, not a host sequence.
            _capabilityCache["ExtendedMode"] = true;
            _capabilityCache["TransparentMode"] = true;
            _capabilityCache["DCSSequences"] = false;
            _capabilityCache["ISO646Variants"] = false;
            _capabilityCache["TektronixMode"] = false;
            _capabilityCache["GraphicsExtension"] = false;
        }
        // TDV2215 capabilities
        else if (_terminalType == TerminalType.TDV2215)
        {
            _capabilityCache["2115Compatibility"] = true;
            _capabilityCache["NDGraphics"] = true;
            _capabilityCache["ProtectedAreas"] = true;
            _capabilityCache["WorkAreas"] = true;
            _capabilityCache["MessageLEDs"] = true;
            _capabilityCache["CharacterSets"] = true;
            _capabilityCache["ExtendedMode"] = true;
            _capabilityCache["TransparentMode"] = true;
            _capabilityCache["DCSSequences"] = true;
            _capabilityCache["ISO646Variants"] = false;
            _capabilityCache["TektronixMode"] = false;
            _capabilityCache["GraphicsExtension"] = false;
        }
        // TDV2200 capabilities
        else if (_terminalType == TerminalType.TDV2200)
        {
            _capabilityCache["2115Compatibility"] = true;
            _capabilityCache["NDGraphics"] = true;
            _capabilityCache["ProtectedAreas"] = true;
            _capabilityCache["WorkAreas"] = true;
            _capabilityCache["MessageLEDs"] = true;
            _capabilityCache["CharacterSets"] = true;
            // TDV 2200/9 S User's Guide, the soft-switch table: "Extended Control Mode: On / Off"
            // and "Send Receive Mode: Simultaneous / Transparent". The 2200 has both switches, and
            // the whole function-key chapter turns on which way Extended Control is set.
            _capabilityCache["ExtendedMode"] = true;
            _capabilityCache["TransparentMode"] = true;
            _capabilityCache["DCSSequences"] = false;
            _capabilityCache["ISO646Variants"] = true;
            _capabilityCache["TektronixMode"] = true;
            _capabilityCache["GraphicsExtension"] = true;
        }
        // Non-TDV terminals
        else
        {
            _capabilityCache["2115Compatibility"] = false;
            _capabilityCache["NDGraphics"] = false;
            _capabilityCache["ProtectedAreas"] = false;
            _capabilityCache["WorkAreas"] = false;
            _capabilityCache["MessageLEDs"] = false;
            _capabilityCache["CharacterSets"] = false;
            _capabilityCache["ExtendedMode"] = false;
            _capabilityCache["TransparentMode"] = false;
            _capabilityCache["DCSSequences"] = false;
            _capabilityCache["ISO646Variants"] = false;
            _capabilityCache["TektronixMode"] = false;
            _capabilityCache["GraphicsExtension"] = false;
        }
    }

    /// <summary>
    /// Checks if the terminal supports a specific feature
    /// </summary>
    public bool Supports(string feature)
    {
        return _capabilityCache.TryGetValue(feature, out bool supported) && supported;
    }

    /// <summary>
    /// Checks if the terminal is a TDV terminal
    /// </summary>
    public bool IsTDVTerminal()
    {
        return _terminalType == TerminalType.TDV1200 ||
               _terminalType == TerminalType.TDV2200 ||
               _terminalType == TerminalType.TDV2215;
    }

    /// <summary>
    /// Checks if the terminal is TDV1200
    /// </summary>
    public bool IsTDV1200()
    {
        return _terminalType == TerminalType.TDV1200;
    }

    /// <summary>
    /// Checks if the terminal is TDV2200
    /// </summary>
    public bool IsTDV2200()
    {
        return _terminalType == TerminalType.TDV2200;
    }

    /// <summary>
    /// Checks if the terminal is TDV2215
    /// </summary>
    public bool IsTDV2215()
    {
        return _terminalType == TerminalType.TDV2215;
    }

    /// <summary>
    /// Gets the terminal type
    /// </summary>
    public TerminalType GetTerminalType()
    {
        return _terminalType;
    }

    /// <summary>
    /// Validates that the terminal supports the required features for a test
    /// </summary>
    public bool ValidateTestRequirements(params string[] requiredFeatures)
    {
        for (int i = 0; i < requiredFeatures.Length; i++)
        {
            if (!Supports(requiredFeatures[i]))
            {
                return false;
            }
        }
        return true;
    }

    /// <summary>
    /// Gets a human-readable list of supported features
    /// </summary>
    public List<string> GetSupportedFeatures()
    {
        var supported = new List<string>();
        var enumerator = _capabilityCache.GetEnumerator();
        while (enumerator.MoveNext())
        {
            if (enumerator.Current.Value)
            {
                supported.Add(enumerator.Current.Key);
            }
        }
        return supported;
    }

    /// <summary>
    /// Gets a human-readable list of unsupported features
    /// </summary>
    public List<string> GetUnsupportedFeatures()
    {
        var unsupported = new List<string>();
        var enumerator = _capabilityCache.GetEnumerator();
        while (enumerator.MoveNext())
        {
            if (!enumerator.Current.Value)
            {
                unsupported.Add(enumerator.Current.Key);
            }
        }
        return unsupported;
    }
}
