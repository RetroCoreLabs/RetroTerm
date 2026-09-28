using System;
using System.Text;
using System.Text.RegularExpressions;

namespace RetroTerm.Core.Protocols.TelnetServer.Utilities;

/// <summary>
/// Validates terminal responses against expected patterns for TDV terminals.
/// Parses and validates DA, CPR, DSR, Terminal ID, and DECRQM responses.
/// </summary>
public static class TDVResponseValidator
{
    /// <summary>
    /// Parsed DA response data
    /// </summary>
    public class DAResponse
    {
        public bool IsValid { get; set; }
        public bool IsSecondary { get; set; }
        public int? FirmwareId { get; set; }
        public int? Version { get; set; }
        public int? Configuration { get; set; }
        public string RawResponse { get; set; } = string.Empty;
    }

    /// <summary>
    /// Parsed CPR response data
    /// </summary>
    public class CPRResponse
    {
        public bool IsValid { get; set; }
        public int? Row { get; set; }
        public int? Column { get; set; }
        public string RawResponse { get; set; } = string.Empty;
    }

    /// <summary>
    /// Parsed DSR response data
    /// </summary>
    public class DSRResponse
    {
        public bool IsValid { get; set; }
        public bool IsOK { get; set; }
        public bool IsFailure { get; set; }
        public string RawResponse { get; set; } = string.Empty;
    }

    /// <summary>
    /// Parsed Terminal ID response data
    /// </summary>
    public class TerminalIDResponse
    {
        public bool IsValid { get; set; }
        public string? TerminalType { get; set; }
        public int? FirmwareId { get; set; }
        public string RawResponse { get; set; } = string.Empty;
    }

    /// <summary>
    /// Parsed DECRQM response data
    /// </summary>
    public class DECRQMResponse
    {
        public bool IsValid { get; set; }
        public int? Mode { get; set; }
        public bool? IsEnabled { get; set; } // true=enabled, false=disabled, null=not recognized
        public string RawResponse { get; set; } = string.Empty;
    }

    /// <summary>
    /// Validates and parses a Primary or Secondary DA response
    /// Primary DA format: ESC [ ? Ps ; Pc c
    /// Secondary DA format: ESC [ > Ps ; Pv ; Pc c
    /// </summary>
    public static DAResponse ParseDAResponse(string? response)
    {
        var result = new DAResponse { RawResponse = response ?? string.Empty };

        if (string.IsNullOrEmpty(response))
        {
            return result;
        }

        // Check for Secondary DA: ESC [ > Ps ; Pv ; Pc c
        var secondaryMatch = Regex.Match(response, @"\x1B\[>(\d+);(\d+);(\d+)c");
        if (secondaryMatch.Success)
        {
            result.IsValid = true;
            result.IsSecondary = true;
            if (int.TryParse(secondaryMatch.Groups[1].Value, out int firmwareId))
                result.FirmwareId = firmwareId;
            if (int.TryParse(secondaryMatch.Groups[2].Value, out int version))
                result.Version = version;
            if (int.TryParse(secondaryMatch.Groups[3].Value, out int config))
                result.Configuration = config;
            return result;
        }

        // Check for Primary DA: ESC [ ? Ps ; Pc c
        var primaryMatch = Regex.Match(response, @"\x1B\[\?(\d+);(\d+)c");
        if (primaryMatch.Success)
        {
            result.IsValid = true;
            result.IsSecondary = false;
            if (int.TryParse(primaryMatch.Groups[1].Value, out int ps))
                result.FirmwareId = ps;
            if (int.TryParse(primaryMatch.Groups[2].Value, out int pc))
                result.Configuration = pc;
            return result;
        }

        // Also check for VT100-style DA: ESC [ ? 1 ; 2 c or ESC [ ? 1 ; 0 c
        var vt100Match = Regex.Match(response, @"\x1B\[\?(\d+);(\d+)c");
        if (vt100Match.Success)
        {
            result.IsValid = true;
            result.IsSecondary = false;
            if (int.TryParse(vt100Match.Groups[1].Value, out int ps))
                result.FirmwareId = ps;
            if (int.TryParse(vt100Match.Groups[2].Value, out int pc))
                result.Configuration = pc;
            return result;
        }

        return result;
    }

    /// <summary>
    /// Validates that a DA response matches expected TDV terminal type
    /// </summary>
    public static bool ValidateDATerminalType(DAResponse response, TerminalType expectedType)
    {
        if (!response.IsValid)
            return false;

        if (response.IsSecondary && response.FirmwareId.HasValue)
        {
            return expectedType switch
            {
                TerminalType.TDV1200 => response.FirmwareId == 120,
                TerminalType.TDV2200 => response.FirmwareId == 220,
                TerminalType.TDV2215 => response.FirmwareId == 115,
                _ => false
            };
        }

        // Primary DA may not have firmware ID, so check configuration
        return true; // Accept any valid primary DA response
    }

    /// <summary>
    /// Validates and parses a CPR (Cursor Position Report) response
    /// Format: ESC [ row ; col R
    /// </summary>
    public static CPRResponse ParseCPRResponse(string? response)
    {
        var result = new CPRResponse { RawResponse = response ?? string.Empty };

        if (string.IsNullOrEmpty(response))
        {
            return result;
        }

        // CPR format: ESC [ row ; col R
        var match = Regex.Match(response, @"\x1B\[(\d+);(\d+)R");
        if (match.Success)
        {
            result.IsValid = true;
            if (int.TryParse(match.Groups[1].Value, out int row))
                result.Row = row;
            if (int.TryParse(match.Groups[2].Value, out int col))
                result.Column = col;
        }

        return result;
    }

    /// <summary>
    /// Validates that a CPR response matches expected cursor position
    /// </summary>
    public static bool ValidateCPRPosition(CPRResponse response, int expectedRow, int expectedColumn)
    {
        if (!response.IsValid)
            return false;

        return response.Row == expectedRow && response.Column == expectedColumn;
    }

    /// <summary>
    /// Validates and parses a DSR (Device Status Report) response
    /// Format: ESC [ 0 n (OK) or ESC [ 3 n (Failure)
    /// </summary>
    public static DSRResponse ParseDSRResponse(string? response)
    {
        var result = new DSRResponse { RawResponse = response ?? string.Empty };

        if (string.IsNullOrEmpty(response))
        {
            return result;
        }

        // DSR format: ESC [ status n
        var match = Regex.Match(response, @"\x1B\[(\d+)n");
        if (match.Success)
        {
            result.IsValid = true;
            if (int.TryParse(match.Groups[1].Value, out int status))
            {
                result.IsOK = status == 0;
                result.IsFailure = status == 3;
            }
        }

        return result;
    }

    /// <summary>
    /// Validates and parses a Terminal Identification response
    /// Format: ESC [ ? Ps ; Pc c or ESC [ ? 1 ; 0 c
    /// </summary>
    public static TerminalIDResponse ParseTerminalIDResponse(string? response)
    {
        var result = new TerminalIDResponse { RawResponse = response ?? string.Empty };

        if (string.IsNullOrEmpty(response))
        {
            return result;
        }

        // Terminal ID format: ESC [ ? Ps ; Pc c
        var match = Regex.Match(response, @"\x1B\[\?(\d+);(\d+)c");
        if (match.Success)
        {
            result.IsValid = true;
            if (int.TryParse(match.Groups[1].Value, out int ps))
            {
                result.FirmwareId = ps;
                result.TerminalType = ps switch
                {
                    115 => "TDV2215",
                    120 => "TDV1200",
                    220 => "TDV2200",
                    _ => $"Unknown({ps})"
                };
            }
        }

        return result;
    }

    /// <summary>
    /// Validates that a Terminal ID response matches expected terminal type
    /// </summary>
    public static bool ValidateTerminalIDType(TerminalIDResponse response, TerminalType expectedType)
    {
        if (!response.IsValid || !response.FirmwareId.HasValue)
            return false;

        return expectedType switch
        {
            TerminalType.TDV1200 => response.FirmwareId == 120,
            TerminalType.TDV2200 => response.FirmwareId == 220,
            TerminalType.TDV2215 => response.FirmwareId == 115,
            _ => false
        };
    }

    /// <summary>
    /// Validates and parses a DECRQM (Request Mode) response
    /// Format: ESC [ ? Ps ; Pm $ y
    /// Pm: 0=not recognized, 1=enabled, 2=disabled, 3=permanently enabled, 4=permanently disabled
    /// </summary>
    /// <remarks>
    /// This parser is right, and it is DEC's. It sits in a TDV utility class only because that is
    /// where it was written - no TDV or ND manual describes DECRQM at all. TDV 2215 Functional
    /// Specifications section 8.7 lists every CSI sequence the terminal accepts and none carries a
    /// '$' intermediate; section 8.3.2 lists everything it sends, and that is CPR alone. The
    /// TestServer no longer sends a mode query to a TDV for that reason.
    ///
    /// What a TDV really answers is NDRQ, ND Display Terminal 1200 section 5.48. See
    /// docs\TDV-MODES-AND-REPORTS-FROM-THE-MANUALS.md.
    /// </remarks>
    /// <param name="response">
    /// The reply bytes as a string, or null.
    /// </param>
    /// <returns>
    /// The parsed reply. <c>IsValid</c> is false when nothing matched.
    /// </returns>
    public static DECRQMResponse ParseDECRQMResponse(string? response)
    {
        var result = new DECRQMResponse { RawResponse = response ?? string.Empty };

        if (string.IsNullOrEmpty(response))
        {
            return result;
        }

        // DECRQM format: ESC [ ? Ps ; Pm $ y
        var match = Regex.Match(response, @"\x1B\[\?(\d+);(\d+)\$y");
        if (match.Success)
        {
            result.IsValid = true;
            if (int.TryParse(match.Groups[1].Value, out int mode))
                result.Mode = mode;
            if (int.TryParse(match.Groups[2].Value, out int pm))
            {
                // Pm: 0=not recognized, 1=enabled, 2=disabled, 3=permanently enabled, 4=permanently disabled
                result.IsEnabled = pm switch
                {
                    0 => null, // Not recognized
                    1 => true, // Enabled
                    2 => false, // Disabled
                    3 => true, // Permanently enabled
                    4 => false, // Permanently disabled
                    _ => null
                };
            }
        }

        return result;
    }

    /// <summary>
    /// Validates that a DECRQM response indicates the mode is enabled
    /// </summary>
    public static bool ValidateDECRQMEnabled(DECRQMResponse response, int expectedMode)
    {
        if (!response.IsValid)
            return false;

        return response.Mode == expectedMode && response.IsEnabled == true;
    }

    /// <summary>
    /// Validates that a DECRQM response indicates the mode is disabled
    /// </summary>
    public static bool ValidateDECRQMDisabled(DECRQMResponse response, int expectedMode)
    {
        if (!response.IsValid)
            return false;

        return response.Mode == expectedMode && response.IsEnabled == false;
    }

    /// <summary>
    /// Converts a response string to visible format for debugging
    /// </summary>
    public static string ToVisibleString(string? response)
    {
        if (string.IsNullOrEmpty(response))
            return "null";

        var sb = new StringBuilder();
        for (int i = 0; i < response.Length; i++)
        {
            var ch = response[i];
            switch (ch)
            {
                case '\x1B':
                    sb.Append("<ESC>");
                    break;
                case '\r':
                    sb.Append("<CR>");
                    break;
                case '\n':
                    sb.Append("<LF>");
                    break;
                case '\t':
                    sb.Append("<TAB>");
                    break;
                default:
                    if (char.IsControl(ch))
                    {
                        sb.Append($"<0x{((int)ch):X2}>");
                    }
                    else
                    {
                        sb.Append(ch);
                    }
                    break;
            }
        }
        return sb.ToString();
    }
}
