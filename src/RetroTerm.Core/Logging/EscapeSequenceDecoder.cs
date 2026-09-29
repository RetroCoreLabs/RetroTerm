using System;
using System.Text;

namespace RetroTerm.Core.Logging;

/// <summary>
/// Turns raw escape / control sequences into named, human-readable descriptions for the
/// protocol log.
/// </summary>
/// <remarks>
/// <para>
/// This type is DIAGNOSTIC ONLY. It never influences emulation - it exists so the log shows
/// <c>CUP row=7 col=3</c> instead of <c>Final=0x48, Params=7,3</c>.
/// </para>
/// <para>
/// Coverage is deliberately a superset of what the emulators implement, and includes the
/// ECMA-48 / ISO 6429 core, the DEC private modes, and the Tandberg TDV extensions
/// (NDSAR/NDAAR/NDRAR/NDFC/NDDWA/NDSREC/NDRREC/NDVIDEO). Anything not listed returns
/// <see cref="DecodedSequence.IsKnown"/> = false so the caller can flag it.
/// </para>
/// <para>
/// Sources: ECMA-48 5th edition; DEC STD 070 / VT220 Programmer Reference;
/// docs\TermCap.txt (the 1998 posting of ND's own termcap and terminfo entries; the header at
/// its top decodes the sequences) for the TDV private finals.
/// </para>
/// </remarks>
public static class EscapeSequenceDecoder
{
    /// <summary>
    /// Decodes a CSI (Control Sequence Introducer) sequence.
    /// </summary>
    /// <param name="privateMarker">
    /// Private parameter prefix byte such as '?' or '>', or 0 when absent.
    /// </param>
    /// <param name="intermediates">
    /// Intermediate bytes in the 0x20-0x2F range, e.g. '$' or ' '.
    /// </param>
    /// <param name="parameters">
    /// Numeric parameters as parsed, in order.
    /// </param>
    /// <param name="finalByte">
    /// The final byte in the 0x40-0x7E range that selects the function.
    /// </param>
    /// <returns>
    /// A decoded description of the sequence.
    /// </returns>
    public static DecodedSequence DecodeCsi(
        byte privateMarker,
        ReadOnlySpan<byte> intermediates,
        ReadOnlySpan<int> parameters,
        byte finalByte)
    {
        var rendered = RenderCsi(privateMarker, intermediates, parameters, finalByte);

        // DEC private sequences (CSI ? ...) form their own namespace and must be checked first,
        // otherwise 'h'/'l'/'n' would be mistaken for their ANSI equivalents.
        if (privateMarker == (byte)'?')
            return DecodeDecPrivate(intermediates, parameters, finalByte, rendered);

        // Intermediate '$' selects the DECRQM/DECRPM request/report family.
        if (intermediates.Length == 1 && intermediates[0] == (byte)'$')
        {
            switch (finalByte)
            {
                case (byte)'p':
                    return Known("DECRQM", "Request Mode", $"mode={P(parameters, 0, 0)}", rendered);
                case (byte)'y':
                    return Known("DECRPM", "Report Mode", $"mode={P(parameters, 0, 0)} value={P(parameters, 1, 0)}", rendered);
            }
        }

        switch (finalByte)
        {
            case (byte)'A':
                return Known("CUU", "Cursor Up", $"n={P(parameters, 0, 1)}", rendered);
            case (byte)'B':
                return Known("CUD", "Cursor Down", $"n={P(parameters, 0, 1)}", rendered);
            case (byte)'C':
                return Known("CUF", "Cursor Forward", $"n={P(parameters, 0, 1)}", rendered);
            case (byte)'D':
                return Known("CUB", "Cursor Back", $"n={P(parameters, 0, 1)}", rendered);
            case (byte)'E':
                return Known("CNL", "Cursor Next Line", $"n={P(parameters, 0, 1)}", rendered);
            case (byte)'F':
                return Known("CPL", "Cursor Previous Line", $"n={P(parameters, 0, 1)}", rendered);
            case (byte)'G':
                return Known("CHA", "Cursor Horizontal Absolute", $"col={P(parameters, 0, 1)}", rendered);
            case (byte)'H':
                return Known("CUP", "Cursor Position", $"row={P(parameters, 0, 1)} col={P(parameters, 1, 1)}", rendered);
            case (byte)'f':
                return Known("HVP", "Horizontal and Vertical Position", $"row={P(parameters, 0, 1)} col={P(parameters, 1, 1)}", rendered);
            case (byte)'I':
                return Known("CHT", "Cursor Forward Tabulation", $"n={P(parameters, 0, 1)}", rendered);
            case (byte)'J':
                return Known("ED", "Erase in Display", DescribeErase(P(parameters, 0, 0), isDisplay: true), rendered);
            case (byte)'K':
                return Known("EL", "Erase in Line", DescribeErase(P(parameters, 0, 0), isDisplay: false), rendered);
            case (byte)'L':
                return Known("IL", "Insert Lines", $"n={P(parameters, 0, 1)}", rendered);
            case (byte)'M':
                return Known("DL", "Delete Lines", $"n={P(parameters, 0, 1)}", rendered);
            case (byte)'P':
                return Known("DCH", "Delete Characters", $"n={P(parameters, 0, 1)}", rendered);
            case (byte)'S':
                return Known("SU", "Scroll Up", $"n={P(parameters, 0, 1)}", rendered);
            case (byte)'T':
                return Known("SD", "Scroll Down", $"n={P(parameters, 0, 1)}", rendered);
            case (byte)'X':
                return Known("ECH", "Erase Characters", $"n={P(parameters, 0, 1)}", rendered);
            case (byte)'Z':
                return Known("CBT", "Cursor Backward Tabulation", $"n={P(parameters, 0, 1)}", rendered);
            case (byte)'@':
                return Known("ICH", "Insert Characters", $"n={P(parameters, 0, 1)}", rendered);
            case (byte)'`':
                return Known("HPA", "Horizontal Position Absolute", $"col={P(parameters, 0, 1)}", rendered);
            case (byte)'a':
                return Known("HPR", "Horizontal Position Relative", $"n={P(parameters, 0, 1)}", rendered);
            case (byte)'c':
                return Known("DA", "Device Attributes", "primary attributes query", rendered);
            case (byte)'d':
                return Known("VPA", "Vertical Position Absolute", $"row={P(parameters, 0, 1)}", rendered);
            case (byte)'e':
                return Known("VPR", "Vertical Position Relative", $"n={P(parameters, 0, 1)}", rendered);
            case (byte)'g':
                return Known("TBC", "Tabulation Clear", P(parameters, 0, 0) == 3 ? "clear all tab stops" : "clear tab stop at cursor", rendered);
            case (byte)'h':
                return Known("SM", "Set Mode", DescribeAnsiModes(parameters, set: true), rendered);
            case (byte)'l':
                return Known("RM", "Reset Mode", DescribeAnsiModes(parameters, set: false), rendered);
            case (byte)'m':
                return Known("SGR", "Select Graphic Rendition", DescribeSgr(parameters), rendered);
            case (byte)'n':
                return Known("DSR", "Device Status Report", DescribeDsr(P(parameters, 0, 0)), rendered);
            case (byte)'r':
                return Known("DECSTBM", "Set Scrolling Region",
                    parameters.Length == 0 ? "reset to full screen" : $"top={P(parameters, 0, 1)} bottom={P(parameters, 1, 0)}", rendered);
            case (byte)'s':
                return Known("SCOSC", "Save Cursor Position", string.Empty, rendered);
            case (byte)'u':
                // Ambiguous: SCORC in ANSI, NDSREC (Save Rectangle) on TDV. Both are reported.
                return Known("SCORC/NDSREC", "Restore Cursor / TDV Save Rectangle", DescribeParams(parameters), rendered);
            case (byte)'t':
                return Known("XTWINOPS", "Window Manipulation", DescribeParams(parameters), rendered);

            // ---- Tandberg TDV private finals ----
            case (byte)'z':
                return Known("NDSAR", "TDV Set Attribute in Rectangle", DescribeRect(parameters), rendered);
            case (byte)'{':
                return Known("NDAAR", "TDV Add Attribute in Rectangle", DescribeRect(parameters), rendered);
            // 7C is NDRAR and 7D is NDFC. These two were the wrong way round here until
            // 11 September 2026 - ND Display Terminal 1200 section 2.8 and the TDV 2200 CSI table
            // both list "CSI ms | <7C> NDRAR" and "CSI ms } <7D> NDFC".
            case (byte)'|':
                return Known("NDRAR", "TDV Remove Attribute in Rectangle", DescribeRect(parameters), rendered);
            case (byte)'}':
                return Known("NDFC", "TDV Fill Character in Rectangle", DescribeRect(parameters), rendered);
            case (byte)'~':
                return Known("NDDWA", "TDV Define Work Area", DescribeRect(parameters), rendered);
            case (byte)'v':
                return Known("NDRREC", "TDV Restore Rectangle", DescribeParams(parameters), rendered);
            case (byte)'<':
                return Known("NDVIDEO", "TDV Alpha/Graphics Toggle", DescribeParams(parameters), rendered);
            case (byte)'_':
                // Terminal -> host only: function/push key report, e.g. ESC[46_ = HJELP (F1).
                return Known("TDVKEY", "TDV Function Key Report", $"key={P(parameters, 0, 0)}", rendered);
        }

        return Unknown($"CSI final 0x{finalByte:X2} ('{Printable(finalByte)}')", rendered);
    }

    /// <summary>
    /// Decodes a two (or more) byte escape sequence that is not a CSI, i.e. ESC followed by
    /// intermediates and a final byte.
    /// </summary>
    /// <param name="intermediates">
    /// Intermediate bytes, e.g. '(' for a G0 charset designation.
    /// </param>
    /// <param name="finalByte">
    /// The final byte selecting the function.
    /// </param>
    /// <returns>
    /// A decoded description of the sequence.
    /// </returns>
    public static DecodedSequence DecodeEscape(ReadOnlySpan<byte> intermediates, byte finalByte)
    {
        var sb = new StringBuilder(16);
        sb.Append("ESC");
        for (int i = 0; i < intermediates.Length; i++)
            sb.Append((char)intermediates[i]);
        sb.Append(Printable(finalByte));
        var rendered = sb.ToString();

        // Character set designation: ESC ( <final> selects G0, ESC ) <final> selects G1, etc.
        if (intermediates.Length == 1)
        {
            var g = intermediates[0];
            if (g == (byte)'(' || g == (byte)')' || g == (byte)'*' || g == (byte)'+')
            {
                int slot = g - (byte)'(';
                return Known("SCS", "Select Character Set",
                    $"G{slot} = {DescribeCharset(finalByte)}", rendered);
            }

            if (g == (byte)'#')
            {
                switch (finalByte)
                {
                    case (byte)'3': return Known("DECDHL", "Double Height Line", "top half", rendered);
                    case (byte)'4': return Known("DECDHL", "Double Height Line", "bottom half", rendered);
                    case (byte)'5': return Known("DECSWL", "Single Width Line", string.Empty, rendered);
                    case (byte)'6': return Known("DECDWL", "Double Width Line", string.Empty, rendered);
                    case (byte)'8': return Known("DECALN", "Screen Alignment Pattern", "fill screen with 'E'", rendered);
                }
            }
        }

        if (intermediates.Length == 0)
        {
            switch (finalByte)
            {
                case (byte)'7': return Known("DECSC", "Save Cursor", string.Empty, rendered);
                case (byte)'8': return Known("DECRC", "Restore Cursor", string.Empty, rendered);
                case (byte)'=': return Known("DECKPAM", "Keypad Application Mode", string.Empty, rendered);
                case (byte)'>': return Known("DECKPNM", "Keypad Numeric Mode", string.Empty, rendered);
                case (byte)'D': return Known("IND", "Index", "cursor down, scroll if at bottom", rendered);
                case (byte)'E': return Known("NEL", "Next Line", string.Empty, rendered);
                case (byte)'H': return Known("HTS", "Horizontal Tab Set", "set tab stop at cursor", rendered);
                case (byte)'M': return Known("RI", "Reverse Index", "cursor up, scroll if at top", rendered);
                case (byte)'N': return Known("SS2", "Single Shift 2", "next char from G2", rendered);
                case (byte)'O': return Known("SS3", "Single Shift 3", "next char from G3", rendered);
                case (byte)'P': return Known("DCS", "Device Control String", "start", rendered);
                case (byte)'Z': return Known("DECID", "Identify Terminal", string.Empty, rendered);
                case (byte)'c': return Known("RIS", "Reset to Initial State", "full terminal reset", rendered);
                case (byte)'\\': return Known("ST", "String Terminator", string.Empty, rendered);
                case (byte)']': return Known("OSC", "Operating System Command", "start", rendered);
            }
        }

        return Unknown($"ESC final 0x{finalByte:X2} ('{Printable(finalByte)}')", rendered);
    }

    /// <summary>
    /// Decodes a C0 control character (0x00-0x1F) or DEL (0x7F).
    /// </summary>
    /// <param name="control">
    /// The control byte.
    /// </param>
    /// <returns>
    /// A decoded description of the control character.
    /// </returns>
    public static DecodedSequence DecodeControl(byte control)
    {
        // TDV note: several C0 codes are reused as cursor controls by the TDV keyboard
        // (UP=0x1C, DOWN=0x0B, LEFT=0x08, RIGHT=0x18, HOME=0x1D). Those meanings are shown
        // alongside the ASCII name because the log carries both directions.
        switch (control)
        {
            case 0x00: return Known("NUL", "Null", string.Empty, "NUL");
            case 0x05: return Known("ENQ", "Enquiry", string.Empty, "ENQ");
            case 0x07: return Known("BEL", "Bell", string.Empty, "BEL");
            case 0x08: return Known("BS", "Backspace", "TDV: cursor left", "BS");
            case 0x09: return Known("HT", "Horizontal Tab", string.Empty, "HT");
            case 0x0A: return Known("LF", "Line Feed", string.Empty, "LF");
            case 0x0B: return Known("VT", "Vertical Tab", "TDV: cursor down", "VT");
            case 0x0C: return Known("FF", "Form Feed", string.Empty, "FF");
            case 0x0D: return Known("CR", "Carriage Return", string.Empty, "CR");
            case 0x0E: return Known("SO", "Shift Out", "invoke G1 into GL", "SO");
            case 0x0F: return Known("SI", "Shift In", "invoke G0 into GL", "SI");
            // On TDV terminals DLE introduces a 3-byte cursor address (DLE row col). The
            // WireScanner consumes it as a unit; this entry is the fallback for a bare DLE.
            case 0x10: return Known("DLE", "Data Link Escape", "TDV: cursor address introducer", "DLE");
            case 0x11: return Known("DC1", "Device Control 1 (XON)", string.Empty, "DC1");
            case 0x13: return Known("DC3", "Device Control 3 (XOFF)", string.Empty, "DC3");
            case 0x18: return Known("CAN", "Cancel", "TDV: cursor right", "CAN");
            case 0x1A: return Known("SUB", "Substitute", string.Empty, "SUB");
            case 0x1B: return Known("ESC", "Escape", string.Empty, "ESC");
            case 0x1C: return Known("FS", "File Separator", "TDV: cursor up", "FS");
            case 0x1D: return Known("GS", "Group Separator", "TDV: home", "GS");
            case 0x1E: return Known("RS", "Record Separator", string.Empty, "RS");
            case 0x1F: return Known("US", "Unit Separator", string.Empty, "US");
            case 0x7F: return Known("DEL", "Delete", string.Empty, "DEL");
        }

        return Unknown($"control 0x{control:X2}", $"0x{control:X2}");
    }

    /// <summary>
    /// Describes an SGR parameter list, e.g. "0" becomes "Reset attributes" and "1;31" becomes
    /// "Bold, FG Red".
    /// </summary>
    /// <param name="parameters">
    /// The SGR parameters.
    /// </param>
    /// <returns>
    /// A comma separated description.
    /// </returns>
    public static string DescribeSgr(ReadOnlySpan<int> parameters)
    {
        if (parameters.Length == 0)
            return "Reset attributes";

        var sb = new StringBuilder(48);

        for (int i = 0; i < parameters.Length; i++)
        {
            if (sb.Length != 0)
                sb.Append(", ");

            int p = parameters[i];

            // Extended colour: 38/48 ; 5 ; n (indexed) or 38/48 ; 2 ; r ; g ; b (direct).
            if ((p == 38 || p == 48) && i + 1 < parameters.Length)
            {
                string which = p == 38 ? "FG" : "BG";
                int mode = parameters[i + 1];

                if (mode == 5 && i + 2 < parameters.Length)
                {
                    sb.Append(which).Append(" index ").Append(parameters[i + 2]);
                    i += 2;
                    continue;
                }

                if (mode == 2 && i + 4 < parameters.Length)
                {
                    sb.Append(which).Append(" rgb(")
                      .Append(parameters[i + 2]).Append(',')
                      .Append(parameters[i + 3]).Append(',')
                      .Append(parameters[i + 4]).Append(')');
                    i += 4;
                    continue;
                }
            }

            sb.Append(SgrName(p));
        }

        return sb.ToString();
    }

    private static string SgrName(int p)
    {
        switch (p)
        {
            case 0: return "Reset attributes";
            case 1: return "Bold";
            case 2: return "Dim";
            case 3: return "Italic";
            case 4: return "Underline";
            case 5: return "Blink";
            case 6: return "Fast blink";
            case 7: return "Reverse video";
            case 8: return "Conceal";
            case 9: return "Strikethrough";
            case 21: return "Double underline";
            case 22: return "Normal intensity";
            case 23: return "Italic off";
            case 24: return "Underline off";
            case 25: return "Blink off";
            case 27: return "Reverse off";
            case 28: return "Reveal";
            case 29: return "Strikethrough off";
            case 39: return "FG default";
            case 49: return "BG default";
        }

        if (p >= 30 && p <= 37)
            return "FG " + ColorName(p - 30);
        if (p >= 40 && p <= 47)
            return "BG " + ColorName(p - 40);
        if (p >= 90 && p <= 97)
            return "FG bright " + ColorName(p - 90);
        if (p >= 100 && p <= 107)
            return "BG bright " + ColorName(p - 100);

        return "SGR " + p.ToString();
    }

    private static string ColorName(int index)
    {
        switch (index)
        {
            case 0: return "Black";
            case 1: return "Red";
            case 2: return "Green";
            case 3: return "Yellow";
            case 4: return "Blue";
            case 5: return "Magenta";
            case 6: return "Cyan";
            case 7: return "White";
            default: return index.ToString();
        }
    }

    private static DecodedSequence DecodeDecPrivate(
        ReadOnlySpan<byte> intermediates,
        ReadOnlySpan<int> parameters,
        byte finalByte,
        string rendered)
    {
        if (intermediates.Length == 1 && intermediates[0] == (byte)'$')
        {
            switch (finalByte)
            {
                case (byte)'p':
                    return Known("DECRQM", "Request DEC Private Mode", $"mode={P(parameters, 0, 0)} ({DecModeName(P(parameters, 0, 0))})", rendered);
                case (byte)'y':
                    return Known("DECRPM", "Report DEC Private Mode", $"mode={P(parameters, 0, 0)} value={P(parameters, 1, 0)}", rendered);
            }
        }

        switch (finalByte)
        {
            case (byte)'h':
                return Known("DECSET", "Set DEC Private Mode", DescribeDecModes(parameters, set: true), rendered);
            case (byte)'l':
                return Known("DECRST", "Reset DEC Private Mode", DescribeDecModes(parameters, set: false), rendered);
            case (byte)'n':
                return Known("DECDSR", "DEC Device Status Report", $"request={P(parameters, 0, 0)}", rendered);
            case (byte)'c':
                return Known("DA", "Device Attributes", "DEC private attributes query", rendered);
        }

        return Unknown($"DEC private final 0x{finalByte:X2} ('{Printable(finalByte)}')", rendered);
    }

    private static string DescribeDecModes(ReadOnlySpan<int> parameters, bool set)
    {
        if (parameters.Length == 0)
            return set ? "set (no mode given)" : "reset (no mode given)";

        var sb = new StringBuilder(48);
        for (int i = 0; i < parameters.Length; i++)
        {
            if (sb.Length != 0)
                sb.Append(", ");
            sb.Append(DecModeName(parameters[i])).Append(set ? " ON" : " OFF");
        }
        return sb.ToString();
    }

    private static string DecModeName(int mode)
    {
        switch (mode)
        {
            case 1: return "DECCKM cursor keys application";
            case 3: return "DECCOLM 132 column";
            case 4: return "DECSCLM smooth scroll";
            case 5: return "DECSCNM reverse video screen";
            case 6: return "DECOM origin mode";
            case 7: return "DECAWM auto wrap";
            case 8: return "DECARM auto repeat";
            case 12: return "cursor blink";
            case 25: return "DECTCEM cursor visible";
            case 47: return "alternate screen buffer";
            case 1000: return "X11 mouse reporting";
            case 1002: return "mouse button-event tracking";
            case 1003: return "mouse any-event tracking";
            case 1006: return "SGR mouse encoding";
            case 1047: return "alternate screen buffer";
            case 1048: return "save/restore cursor";
            case 1049: return "alt screen + save cursor";
            case 2004: return "bracketed paste";
            default: return "DEC mode " + mode.ToString();
        }
    }

    private static string DescribeAnsiModes(ReadOnlySpan<int> parameters, bool set)
    {
        if (parameters.Length == 0)
            return set ? "set (no mode given)" : "reset (no mode given)";

        var sb = new StringBuilder(32);
        for (int i = 0; i < parameters.Length; i++)
        {
            if (sb.Length != 0)
                sb.Append(", ");

            switch (parameters[i])
            {
                case 2: sb.Append("KAM keyboard action"); break;
                case 4: sb.Append("IRM insert/replace"); break;
                case 12: sb.Append("SRM send/receive (local echo)"); break;
                case 20: sb.Append("LNM line feed/new line"); break;
                default: sb.Append("mode ").Append(parameters[i]); break;
            }

            sb.Append(set ? " ON" : " OFF");
        }
        return sb.ToString();
    }

    private static string DescribeDsr(int request)
    {
        switch (request)
        {
            case 5: return "request terminal status";
            case 6: return "request cursor position (CPR)";
            default: return "request=" + request.ToString();
        }
    }

    private static string DescribeErase(int mode, bool isDisplay)
    {
        switch (mode)
        {
            case 0: return isDisplay ? "cursor to end of screen" : "cursor to end of line";
            case 1: return isDisplay ? "start of screen to cursor" : "start of line to cursor";
            case 2: return isDisplay ? "entire screen" : "entire line";
            case 3: return isDisplay ? "entire screen + scrollback" : "entire line";
            default: return "mode=" + mode.ToString();
        }
    }

    private static string DescribeRect(ReadOnlySpan<int> parameters)
    {
        // TDV rectangle operations take top;left;bottom;right, optionally followed by an operand.
        if (parameters.Length >= 4)
        {
            var sb = new StringBuilder(48);
            sb.Append("top=").Append(parameters[0])
              .Append(" left=").Append(parameters[1])
              .Append(" bottom=").Append(parameters[2])
              .Append(" right=").Append(parameters[3]);

            for (int i = 4; i < parameters.Length; i++)
            {
                sb.Append(" arg").Append(i - 3).Append('=').Append(parameters[i]);
            }

            return sb.ToString();
        }

        return DescribeParams(parameters);
    }

    private static string DescribeParams(ReadOnlySpan<int> parameters)
    {
        if (parameters.Length == 0)
            return "no parameters";

        var sb = new StringBuilder(24);
        sb.Append("params=");
        for (int i = 0; i < parameters.Length; i++)
        {
            if (i != 0)
                sb.Append(';');
            sb.Append(parameters[i]);
        }
        return sb.ToString();
    }

    private static string DescribeCharset(byte final)
    {
        switch (final)
        {
            case (byte)'A': return "United Kingdom (ISO 646 GB)";
            case (byte)'B': return "US ASCII";
            case (byte)'0': return "DEC Special Graphics";
            case (byte)'1': return "DEC Alternate ROM";
            case (byte)'2': return "DEC Alternate ROM Special Graphics";
            case (byte)'4': return "Dutch (ISO 646 NL)";
            case (byte)'5': return "Finnish (ISO 646 FI)";
            case (byte)'6': return "Norwegian/Danish (ISO 646 NO)";
            case (byte)'7': return "Swedish (ISO 646 SE)";
            case (byte)'C': return "Finnish (ISO 646 FI)";
            case (byte)'E': return "Norwegian/Danish (ISO 646 NO)";
            case (byte)'H': return "Swedish (ISO 646 SE)";
            case (byte)'K': return "German (ISO 646 DE)";
            case (byte)'Q': return "French Canadian (ISO 646 CA)";
            case (byte)'R': return "French (ISO 646 FR)";
            case (byte)'Y': return "Italian (ISO 646 IT)";
            case (byte)'Z': return "Spanish (ISO 646 ES)";
            default: return $"charset '{Printable(final)}' (0x{final:X2})";
        }
    }

    /// <summary>
    /// Renders a CSI sequence in printable wire form, e.g. <c>ESC[?25h</c>.
    /// </summary>
    /// <param name="privateMarker">
    /// Private prefix byte, or 0 when absent.
    /// </param>
    /// <param name="intermediates">
    /// Intermediate bytes.
    /// </param>
    /// <param name="parameters">
    /// Numeric parameters.
    /// </param>
    /// <param name="finalByte">
    /// Final byte.
    /// </param>
    /// <returns>
    /// The printable form.
    /// </returns>
    public static string RenderCsi(
        byte privateMarker,
        ReadOnlySpan<byte> intermediates,
        ReadOnlySpan<int> parameters,
        byte finalByte)
    {
        var sb = new StringBuilder(24);
        sb.Append("ESC[");

        if (privateMarker != 0)
            sb.Append((char)privateMarker);

        for (int i = 0; i < parameters.Length; i++)
        {
            if (i != 0)
                sb.Append(';');
            sb.Append(parameters[i]);
        }

        for (int i = 0; i < intermediates.Length; i++)
            sb.Append((char)intermediates[i]);

        sb.Append(Printable(finalByte));
        return sb.ToString();
    }

    /// <summary>
    /// Renders a run of bytes as a quoted printable string, replacing non-printable bytes with
    /// a dot. Used to collapse long runs of text in the protocol log.
    /// </summary>
    /// <param name="data">
    /// The bytes to render.
    /// </param>
    /// <param name="maxChars">
    /// Maximum characters to include before truncating.
    /// </param>
    /// <returns>
    /// A quoted string, with a trailing character count when truncated.
    /// </returns>
    public static string RenderPrintable(ReadOnlySpan<byte> data, int maxChars = 60)
    {
        var sb = new StringBuilder(maxChars + 16);
        sb.Append('"');

        int shown = data.Length < maxChars ? data.Length : maxChars;
        for (int i = 0; i < shown; i++)
        {
            byte b = data[i];
            sb.Append(b >= 0x20 && b < 0x7F ? (char)b : '.');
        }

        sb.Append('"');

        if (data.Length > shown)
        {
            sb.Append(" (").Append(data.Length).Append(" chars)");
        }

        return sb.ToString();
    }

    private static char Printable(byte b)
    {
        return b >= 0x20 && b < 0x7F ? (char)b : '.';
    }

    private static int P(ReadOnlySpan<int> parameters, int index, int defaultValue)
    {
        if (index >= parameters.Length)
            return defaultValue;

        // A parameter that was omitted parses as 0 and means "use the default".
        return parameters[index] == 0 && defaultValue != 0 ? defaultValue : parameters[index];
    }

    private static DecodedSequence Known(string mnemonic, string name, string arguments, string rendered)
    {
        return new DecodedSequence(mnemonic, name, arguments, rendered, true);
    }

    private static DecodedSequence Unknown(string description, string rendered)
    {
        return new DecodedSequence("UNKNOWN", "Unrecognised sequence", description, rendered, false);
    }
}
