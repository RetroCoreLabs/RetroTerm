using System;
using System.Collections.Generic;
using System.Text;

namespace RetroTerm.Core.Protocols.TelnetServer.Utilities;

/// <summary>
/// Centralized escape sequence builder for TDV terminals.
/// Generates escape sequences as raw byte arrays to avoid C# string literal hex escape interpretation issues.
/// </summary>
public static class TDVSequenceBuilder
{
    private const byte ESC = 0x1B;
    private const byte CSI_START = 0x5B; // '['
    private const byte CSI_END_PRIVATE = 0x3F; // '?'
    private const byte CSI_END_SECONDARY = 0x3E; // '>'
    private const byte CSI_END_DECRQM = 0x24; // '$'

    /// <summary>
    /// Builds a Primary Device Attributes (DA) query: ESC [ c
    /// </summary>
    public static byte[] BuildDAQuery()
    {
        return new byte[] { ESC, CSI_START, (byte)'c' };
    }

    /// <summary>
    /// Builds a Secondary Device Attributes (DA) query: ESC [ > c
    /// </summary>
    public static byte[] BuildSecondaryDAQuery()
    {
        return new byte[] { ESC, CSI_START, CSI_END_SECONDARY, (byte)'c' };
    }

    /// <summary>
    /// Builds a Cursor Position Report (CPR) query: ESC [ 6 n
    /// </summary>
    public static byte[] BuildCPRQuery()
    {
        return new byte[] { ESC, CSI_START, (byte)'6', (byte)'n' };
    }

    /// <summary>
    /// Builds a Device Status Report (DSR) query: ESC [ 5 n
    /// </summary>
    public static byte[] BuildDSRQuery()
    {
        return new byte[] { ESC, CSI_START, (byte)'5', (byte)'n' };
    }

    /// <summary>
    /// Builds a Terminal Identification query: ESC Z
    /// </summary>
    public static byte[] BuildTerminalIDQuery()
    {
        return new byte[] { ESC, (byte)'Z' };
    }

    /// <summary>
    /// Builds a DECRQM (Request Mode) query: ESC [ ? Ps $ p
    /// </summary>
    /// <param name="mode">
    /// Mode number (e.g., 66 for 2115 compatibility mode, 67 for smooth scroll)
    /// </param>
    public static byte[] BuildDECRQMQuery(int mode)
    {
        var modeStr = mode.ToString();
        var bytes = new List<byte> { ESC, CSI_START, CSI_END_PRIVATE };
        bytes.AddRange(Encoding.ASCII.GetBytes(modeStr));
        bytes.Add(CSI_END_DECRQM);
        bytes.Add((byte)'p');
        return bytes.ToArray();
    }

    /// <summary>
    /// Builds a CSI sequence: ESC [ Ps F
    /// </summary>
    /// <param name="parameters">
    /// Parameters (can be empty)
    /// </param>
    public static byte[] BuildCSI(params int[] parameters)
    {
        return BuildCSI(null, parameters);
    }

    /// <summary>
    /// Builds a CSI sequence: ESC [ [private] Ps F
    /// </summary>
    /// <param name="privateMarker">
    /// Private marker ('?' for private sequences, '>' for secondary DA, null for standard)
    /// </param>
    /// <param name="parameters">
    /// Parameters (can be empty)
    /// </param>
    public static byte[] BuildCSI(char? privateMarker, params int[] parameters)
    {
        return BuildCSI(privateMarker, parameters, 'H'); // Default final is 'H' for cursor position
    }

    /// <summary>
    /// Builds a CSI sequence: ESC [ [private] Ps F
    /// </summary>
    /// <param name="privateMarker">
    /// Private marker ('?' for private sequences, '>' for secondary DA, null for standard)
    /// </param>
    /// <param name="parameters">
    /// Parameters (can be empty)
    /// </param>
    /// <param name="final">
    /// Final character
    /// </param>
    public static byte[] BuildCSI(char? privateMarker, int[] parameters, char final)
    {
        var bytes = new List<byte> { ESC, CSI_START };

        if (privateMarker.HasValue)
        {
            bytes.Add((byte)privateMarker.Value);
        }

        if (parameters != null && parameters.Length > 0)
        {
            // Build parameter string without LINQ
            var paramSb = new StringBuilder();
            for (int i = 0; i < parameters.Length; i++)
            {
                if (i > 0)
                    paramSb.Append(';');
                paramSb.Append(parameters[i].ToString());
            }
            bytes.AddRange(Encoding.ASCII.GetBytes(paramSb.ToString()));
        }

        bytes.Add((byte)final);
        return bytes.ToArray();
    }

    /// <summary>
    /// Builds a simple ESC sequence: ESC F
    /// </summary>
    /// <param name="final">
    /// Final character
    /// </param>
    public static byte[] BuildESC(char final)
    {
        return new byte[] { ESC, (byte)final };
    }

    /// <summary>
    /// Builds a character set selection sequence: ESC ( n
    /// </summary>
    /// <param name="setNumber">
    /// Character set number (0-9)
    /// </param>
    public static byte[] BuildCharacterSet(int setNumber)
    {
        if (setNumber < 0 || setNumber > 9)
            throw new ArgumentOutOfRangeException(nameof(setNumber), "Character set number must be 0-9");

        return new byte[] { ESC, (byte)'(', (byte)('0' + setNumber) };
    }

    /// <summary>
    /// Builds a SS2 (Single Shift 2) sequence: ESC N
    /// </summary>
    public static byte[] BuildSS2()
    {
        return new byte[] { ESC, (byte)'N' };
    }

    /// <summary>
    /// Builds a SS3 (Single Shift 3) sequence: ESC O
    /// </summary>
    public static byte[] BuildSS3()
    {
        return new byte[] { ESC, (byte)'O' };
    }

    /// <summary>
    /// Builds a RIS (Reset to Initial State) sequence: ESC c
    /// </summary>
    public static byte[] BuildRIS()
    {
        return new byte[] { ESC, (byte)'c' };
    }

    /// <summary>
    /// Builds a DECSC (Save Cursor) sequence: ESC 7
    /// </summary>
    public static byte[] BuildDECSC()
    {
        return new byte[] { ESC, (byte)'7' };
    }

    /// <summary>
    /// Builds a DECRC (Restore Cursor) sequence: ESC 8
    /// </summary>
    public static byte[] BuildDECRC()
    {
        return new byte[] { ESC, (byte)'8' };
    }

    /// <summary>
    /// Builds a double-width/height line sequence: ESC # n
    /// </summary>
    /// <param name="type">
    /// 3=double-height top, 4=double-height bottom, 5=single-width, 6=double-width
    /// </param>
    public static byte[] BuildDoubleWidthHeight(int type)
    {
        if (type < 3 || type > 6)
            throw new ArgumentOutOfRangeException(nameof(type), "Type must be 3, 4, 5, or 6");

        return new byte[] { ESC, (byte)'#', (byte)('0' + type) };
    }

    // ── TDV mode switches, rebuilt from the manuals on 11 September 2026 ──────────────────────
    //
    // Every sequence in this block used to carry a '?' marker and a mode number that named a
    // different switch in the real manuals. This matters more here than anywhere else in the
    // repository, because the TestServer SENDS these at a terminal: a wrong number here does not
    // fail a test, it silently reconfigures the printer or the handshake on whatever is listening.
    //
    // What they used to be, and what those numbers really are (TDV 2215 Functional Specifications
    // section 8.7.1):
    //     CSI ? 40 h  "enable 2115"          40 is PCF, the printer code format
    //     CSI ? 67 h  "smooth scroll"        67 is HAN, the XON/XOFF handshake
    //     CSI ? 68 h  "blink"                68 is CT, the cursor type
    //     CSI ? 69 h  "enhanced blink"       69 is PM, the printer mode
    //
    // See docs\TDV-MODES-AND-REPORTS-FROM-THE-MANUALS.md.

    /// <summary>
    /// Enters TDV 2115 compatibility mode: <c>ESC [ 66 l</c>.
    /// </summary>
    /// <remarks>
    /// RESET, not set, and no private marker. Mode 66 is the Extended Control switch, and TDV 2215
    /// Functional Specifications section 3.1 says "When this switch is set to OFF, the terminal
    /// works like a TDV 2115 from the host computer&#39;s point of view." ND Display Terminal 1200
    /// section 8.1 gives the same instruction outright: enter with <c>CSI 66 l</c>.
    /// </remarks>
    /// <returns>
    /// The sequence bytes.
    /// </returns>
    public static byte[] Build2115CompatibilityEnable()
    {
        return BuildCSI(null, new[] { 66 }, 'l');
    }

    /// <summary>
    /// Leaves TDV 2115 compatibility mode: <c>ESC [ 66 h</c>, the EC switch on.
    /// </summary>
    /// <remarks>
    /// This works only from extended operation. In 2115 mode the terminal discards the ESC of any
    /// control sequence, so nothing beginning with CSI can reach it - use
    /// <see cref="Build2115CompatibilityExitEscape"/> instead.
    /// </remarks>
    /// <returns>
    /// The sequence bytes.
    /// </returns>
    public static byte[] Build2115CompatibilityDisable()
    {
        return BuildCSI(null, new[] { 66 }, 'h');
    }

    /// <summary>
    /// The way out of 2115 mode from INSIDE it: <c>ESC Q</c>.
    /// </summary>
    /// <remarks>
    /// 2215 sections 3.1, 7.9.2 and the note under 8.7.1, and ND-1200 chapter 8, which gives the
    /// bytes as <c>1B 51</c>. In 2115 mode only C0 codes and this one escape get through.
    /// The manual names a second one, <c>ESC 0</c> (section 7.9.1), which does the same thing.
    /// </remarks>
    /// <returns>
    /// The two bytes ESC and 'Q'.
    /// </returns>
    public static byte[] Build2115CompatibilityExitEscape()
    {
        return new byte[] { ESC, (byte)'Q' };
    }

    /// <summary>
    /// Turns smooth scrolling on: <c>ESC [ 60 h</c>, the RT Roll Type switch set to SMOOTH.
    /// </summary>
    /// <remarks>
    /// TDV 2215 Functional Specifications section 8.7.1. An ANSI mode, no private marker.
    /// The ND Display Terminal 1200 uses DEC&#39;s own <c>CSI ? 4 h</c> instead - section 5.64.
    /// </remarks>
    /// <returns>
    /// The sequence bytes.
    /// </returns>
    public static byte[] BuildSmoothScrollEnable()
    {
        return BuildCSI(null, new[] { 60 }, 'h');
    }

    /// <summary>
    /// Turns smooth scrolling off: <c>ESC [ 60 l</c>, RT back to STEP.
    /// </summary>
    /// <returns>
    /// The sequence bytes.
    /// </returns>
    public static byte[] BuildSmoothScrollDisable()
    {
        return BuildCSI(null, new[] { 60 }, 'l');
    }

    /// <summary>
    /// Turns Beginning of Line Wrap on: <c>ESC [ 31 h</c>.
    /// </summary>
    /// <remarks>
    /// This replaces the old "blink mode" pair. NDBLWM is the Beginning of Line WRAP mode -
    /// ND-1200 section 4.10, "Cursor will wrap around to preceding line when left margin is
    /// reached" - and 31 is the 2215&#39;s number for the same switch, BOL (section 8.7.1).
    /// The manual&#39;s list of eighteen modes contains no blink mode at all.
    /// </remarks>
    /// <returns>
    /// The sequence bytes.
    /// </returns>
    public static byte[] BuildBeginningOfLineWrapEnable()
    {
        return BuildCSI(null, new[] { 31 }, 'h');
    }

    /// <summary>
    /// Turns Beginning of Line Wrap off: <c>ESC [ 31 l</c>, the cursor stops at the left margin.
    /// </summary>
    /// <returns>
    /// The sequence bytes.
    /// </returns>
    public static byte[] BuildBeginningOfLineWrapDisable()
    {
        return BuildCSI(null, new[] { 31 }, 'l');
    }

    /// <summary>
    /// Turns End of Line Wrap on: <c>ESC [ 36 h</c>.
    /// </summary>
    /// <remarks>
    /// NDELWM, ND-1200 section 4.11; EOL in 2215 section 8.7.1. A real TDV2200 termcap sets it in
    /// its init string. This replaces the old "enhanced blink" pair.
    /// </remarks>
    /// <returns>
    /// The sequence bytes.
    /// </returns>
    public static byte[] BuildEndOfLineWrapEnable()
    {
        return BuildCSI(null, new[] { 36 }, 'h');
    }

    /// <summary>
    /// Turns End of Line Wrap off: <c>ESC [ 36 l</c>, the cursor stops at the right margin.
    /// </summary>
    /// <returns>
    /// The sequence bytes.
    /// </returns>
    public static byte[] BuildEndOfLineWrapDisable()
    {
        return BuildCSI(null, new[] { 36 }, 'l');
    }

    /// <summary>
    /// Puts the numeric pad in "Function" mode: <c>ESC [ 80 h</c>.
    /// </summary>
    /// <remarks>
    /// TDV 2200/9 S User&#39;s Guide section 11.2, one of the eleven sequences the 2200 adds to the
    /// 2215 set. No private marker, so it does not collide with DEC private mode 80, DECSDM.
    /// </remarks>
    /// <returns>
    /// The sequence bytes.
    /// </returns>
    public static byte[] BuildNumericPadFunctionMode()
    {
        return BuildCSI(null, new[] { 80 }, 'h');
    }

    /// <summary>
    /// Puts the numeric pad back in "Numeric" mode: <c>ESC [ 80 l</c>.
    /// </summary>
    /// <returns>
    /// The sequence bytes.
    /// </returns>
    public static byte[] BuildNumericPadNumericMode()
    {
        return BuildCSI(null, new[] { 80 }, 'l');
    }

    // Build2115CompatibilityQuery and BuildSmoothScrollQuery are GONE. Both built a DECRQM
    // request, and no TDV manual has one: 2215 section 8.7 lists every CSI sequence the terminal
    // accepts and none carries a '$' intermediate, while section 8.3.2 lists everything it sends
    // and that is CPR alone. What a TDV really answers is NDRQ - ND-1200 section 5.48, CSI Ps x -
    // which this program does not implement yet.

    #region TDV1200 Specific Sequences

    public static byte[] BuildNDDWA(int row1, int col1, int row2, int col2)
    {
        return BuildCSI(null, new[] { row1, col1, row2, col2 }, '~');
    }

    // NDSAR, NDAAR, NDRAR and NDFC below take the two corners FIRST and the attribute or the
    // character LAST, counted from 1: ND Display Terminal 1200 sections 5.50, 5.36, 5.46 and 5.43.
    // They took the attribute first and 0-based corners until 8 October 2026 (BUGS.md B1).

    public static byte[] BuildNDSAR(int line1, int column1, int line2, int column2, int attribute)
    {
        return BuildCSI(null, new[] { line1, column1, line2, column2, attribute }, 'z');
    }

    public static byte[] BuildNDAAR(int line1, int column1, int line2, int column2, int attribute)
    {
        return BuildCSI(null, new[] { line1, column1, line2, column2, attribute }, '{');
    }

    /// <summary>
    /// NDRAR - Remove Attribute in Rectangle. The final byte is <c>|</c>, hex 7C.
    /// </summary>
    /// <param name="line1">
    /// Top line, counted from 1.
    /// </param>
    /// <param name="column1">
    /// Left column, counted from 1.
    /// </param>
    /// <param name="line2">
    /// Bottom line.
    /// </param>
    /// <param name="column2">
    /// Right column.
    /// </param>
    /// <param name="attribute">
    /// The attribute to remove, a number from the SGR table of section 5.67.
    /// </param>
    /// <returns>
    /// The sequence bytes.
    /// </returns>
    /// <remarks>
    /// NDRAR and NDFC were SWAPPED here until 11 September 2026 - this one built <c>}</c> and NDFC
    /// built <c>|</c>. Two independent tables give the right way round: ND Display Terminal 1200
    /// section 2.8 gives hex 7C as NDRAR and hex 7D as NDFC, and so does the TDV 2200 CSI list at
    /// <c>spec\TDV2200\Testing2200_9S\nd_csi_sequences.md</c>.
    /// </remarks>
    public static byte[] BuildNDRAR(int line1, int column1, int line2, int column2, int attribute)
    {
        return BuildCSI(null, new[] { line1, column1, line2, column2, attribute }, '|');
    }

    /// <summary>
    /// NDFC - Fill Character(s) in Rectangle. The final byte is <c>}</c>, hex 7D.
    /// </summary>
    /// <param name="line1">
    /// Top line, counted from 1.
    /// </param>
    /// <param name="column1">
    /// Left column, counted from 1.
    /// </param>
    /// <param name="line2">
    /// Bottom line.
    /// </param>
    /// <param name="column2">
    /// Right column.
    /// </param>
    /// <param name="character">
    /// The character code to fill with.
    /// </param>
    /// <returns>
    /// The sequence bytes.
    /// </returns>
    /// <remarks>
    /// See the remarks on <see cref="BuildNDRAR"/> for the swap this corrects.
    /// </remarks>
    public static byte[] BuildNDFC(int line1, int column1, int line2, int column2, int character)
    {
        return BuildCSI(null, new[] { line1, column1, line2, column2, character }, '}');
    }

    // ── The message lamps, ND Display Terminal 1200 sections 5.37, 5.38 and 5.52 ─────────────
    //
    // Three OPERATIONS on FOUR lamps, and that is worth saying plainly because this program's own
    // TDVMessageLEDs models the three operations as if they were three lamps:
    //
    //     CSI ? n1 ; n2 ... A   NDCLED   clear the named lamps
    //     CSI ? n1 ; n2 ... B   NDSLED   light the named lamps
    //     CSI ? n1 ; n2 ... C   NDBLED   blink the named lamps
    //
    // The parameters name the lamp: 0 all of them, 1 EXPAND, 2 APPEND, 3 BUSY, 4 MESSAGE. The
    // default is 0. Section 5.52 lists them under "Light the Message Lamps According to Parameters".
    //
    // The TestServer used to print the letters "NDCLED" and "NDSLED" on the screen and send
    // nothing at all.

    /// <summary>
    /// NDCLED - clear message lamps, <c>CSI ? Ps A</c>.
    /// </summary>
    /// <param name="lamp">
    /// The lamp: 0 all, 1 EXPAND, 2 APPEND, 3 BUSY, 4 MESSAGE.
    /// </param>
    /// <returns>
    /// The sequence bytes.
    /// </returns>
    public static byte[] BuildNDCLED(int lamp)
    {
        return BuildCSI('?', new[] { lamp }, 'A');
    }

    /// <summary>
    /// NDSLED - light message lamps, <c>CSI ? Ps B</c>.
    /// </summary>
    /// <param name="lamp">
    /// The lamp: 0 all, 1 EXPAND, 2 APPEND, 3 BUSY, 4 MESSAGE.
    /// </param>
    /// <returns>
    /// The sequence bytes.
    /// </returns>
    public static byte[] BuildNDSLED(int lamp)
    {
        return BuildCSI('?', new[] { lamp }, 'B');
    }

    /// <summary>
    /// NDBLED - blink message lamps, <c>CSI ? Ps C</c>.
    /// </summary>
    /// <param name="lamp">
    /// The lamp: 0 all, 1 EXPAND, 2 APPEND, 3 BUSY, 4 MESSAGE.
    /// </param>
    /// <returns>
    /// The sequence bytes.
    /// </returns>
    public static byte[] BuildNDBLED(int lamp)
    {
        return BuildCSI('?', new[] { lamp }, 'C');
    }

    public static byte[] BuildNDSREC(int row1, int col1, int row2, int col2)
    {
        return BuildCSI(null, new[] { row1, col1, row2, col2 }, 'u');
    }

    public static byte[] BuildNDRREC(int row, int col)
    {
        return BuildCSI(null, new[] { row, col }, 'v');
    }

    #endregion

    #region TDV2215 Specific Sequences

    // Extended mode and transparent mode, rebuilt from the manuals on 11 September 2026.
    //
    // These four used to send ESC ? 1 h, ESC ? 1 l, ESC ? 2 h and ESC ? 2 l - not CSI sequences at
    // all, and not in any manual. Neither TDV 2215 Functional Specifications section 8.7.1 nor ND
    // Display Terminal 1200 section 5.64 has an ESC ? form. The two numbers are real, but they
    // belong to the ND private family with a '>' marker, where 1 is Beginning of Line Wrap and 2 is
    // End of Line Wrap - the same pair the mode work of 11 September already corrected elsewhere in
    // this file. So the old bytes named the wrong switch AND used a shape no TDV parses.
    //
    // Extended mode is real and is mode 66, the Extended Control switch. Transparent mode is real
    // too, but it is NOT host-settable - see BuildTransparentModeEnable's removal note below.

    /// <summary>
    /// Turns the Extended Control switch ON: <c>ESC [ 66 h</c>.
    /// </summary>
    /// <remarks>
    /// EC on is extended operation, EC off is 2115-compatible operation, so this is the same switch
    /// as <see cref="Build2115CompatibilityEnable"/> seen from the other side, and calls the same
    /// bytes as <see cref="Build2115CompatibilityDisable"/>. Both names are kept because both appear
    /// in the manuals: TDV 2215 Functional Specifications section 3.1 calls it the Extended Control
    /// switch, and section 8.7.1 lists 66 as EC with RM = OFF and SM = ON.
    /// </remarks>
    /// <returns>
    /// The sequence bytes.
    /// </returns>
    public static byte[] BuildExtendedModeEnable()
    {
        return BuildCSI(null, new[] { 66 }, 'h');
    }

    /// <summary>
    /// Turns the Extended Control switch OFF: <c>ESC [ 66 l</c>.
    /// </summary>
    /// <remarks>
    /// EC off puts the terminal into 2115-compatible operation, where it discards the ESC of any
    /// control sequence. Getting back out needs <see cref="Build2115CompatibilityExitEscape"/>,
    /// not another CSI.
    /// </remarks>
    /// <returns>
    /// The sequence bytes.
    /// </returns>
    public static byte[] BuildExtendedModeDisable()
    {
        return BuildCSI(null, new[] { 66 }, 'l');
    }

    // BuildTransparentModeEnable / BuildTransparentModeDisable are GONE, and deliberately have no
    // replacement.
    //
    // On the TDV 2215 transparent operation is the TRANSPARENT setting of the Send-Receive Mode
    // soft-switch, section 4.3.1. It is reached from the keyboard and the manual is explicit that
    // it cannot be left any other way: "The only exit possible from this mode is obtained by
    // depressing the MODE key twice, which gives access to the Soft-switch menu." Section 8.7.1
    // does not list SRM at all, so no host sequence sets it.
    //
    // On the ND 1200 it is a set-up menu option, chapter 3, "Transparent mode: Disabled/Enabled".
    // Also keyboard-only.
    //
    // A host CANNOT put a TDV into transparent mode, and offering a builder that pretends otherwise
    // is how the old ESC ? 2 h got written in the first place.

    public static byte[] BuildDCSStart()
    {
        return new byte[] { ESC, (byte)'P' };
    }

    public static byte[] BuildDCSEnd()
    {
        return new byte[] { ESC, (byte)'\\' };
    }

    public static byte[] BuildDCS(string data)
    {
        var bytes = new List<byte> { ESC, (byte)'P' };
        bytes.AddRange(Encoding.ASCII.GetBytes(data));
        bytes.Add(ESC);
        bytes.Add((byte)'\\');
        return bytes.ToArray();
    }

    public static byte[] BuildPUSHKeyProgram(int pushKeyNumber, string hexData)
    {
        if (pushKeyNumber < 1 || pushKeyNumber > 16)
            throw new ArgumentOutOfRangeException(nameof(pushKeyNumber), "PUSH key number must be 1-16");

        var keyStr = pushKeyNumber.ToString("D2");
        var data = $"P{keyStr}{hexData}";
        return BuildDCS(data);
    }

    public static byte[] BuildPROGRAMKeyLoad(int programKeyNumber)
    {
        if (programKeyNumber < 1 || programKeyNumber > 16)
            throw new ArgumentOutOfRangeException(nameof(programKeyNumber), "PROGRAM key number must be 1-16");

        var keyStr = programKeyNumber.ToString("D2");
        var data = $"PROGRAM{keyStr}";
        return BuildDCS(data);
    }

    #endregion

    #region TDV2200 Specific Sequences

    /// <summary>
    /// Builds a national-version selection sequence: <c>ESC % V</c>.
    /// </summary>
    /// <param name="variant">
    /// The version letter: I International, N Norwegian, S Swedish, G German.
    /// </param>
    /// <returns>
    /// The sequence bytes.
    /// </returns>
    /// <remarks>
    /// <para><b>Four versions exist, not six</b></para>
    /// TDV 2215 sections 9.1.1 to 9.1.4 print the character tables for International, Norwegian,
    /// Swedish and German, and appendix A's ordering list names the same four. D and F, offered
    /// here until 11 September 2026, name terminals that were never built, and U was a second name
    /// for the International version. <c>TDV2200Emulator</c> has always accepted only I, N, S and
    /// G, so the three extra letters silently left the variant unchanged.
    ///
    /// <para><b>And the sequence itself has no source</b></para>
    /// No manual held here gives a host sequence for choosing the national version - it is a
    /// factory version, ordered by part number. <c>ESC %</c> is also ISO 2022's introducer for
    /// designating another coding system, where <c>ESC % G</c> means "switch to UTF-8", so this
    /// collides with a real standard. Kept because the emulator implements it and a real terminal
    /// is the only thing that can settle it - see the TestServer case, which says so on screen.
    /// </remarks>
    public static byte[] BuildISO646Variant(char variant)
    {
        var validVariants = new[] { 'I', 'N', 'S', 'G' };
        bool found = false;
        for (int i = 0; i < validVariants.Length; i++)
        {
            if (validVariants[i] == variant)
            {
                found = true;
                break;
            }
        }
        if (!found)
            throw new ArgumentException($"Invalid national version: {variant}. Must be one of: I, N, S, G", nameof(variant));

        return new byte[] { ESC, (byte)'%', (byte)variant };
    }

    public static byte[] BuildTektronixModeEnable()
    {
        return BuildCSI('?', new[] { 38 }, 'h');
    }

    public static byte[] BuildTektronixModeDisable()
    {
        return BuildCSI('?', new[] { 38 }, 'l');
    }

    public static byte[] BuildGraphicsExtensionEnable()
    {
        return BuildCSI(null, new[] { 1 }, '>');
    }

    public static byte[] BuildGraphicsExtensionDisable()
    {
        return BuildCSI(null, new[] { 0 }, '>');
    }

    #endregion

    /// <summary>
    /// Converts a byte array to a visible string representation for debugging
    /// </summary>
    public static string ToVisibleString(byte[] bytes)
    {
        if (bytes == null) return "null";

        var sb = new StringBuilder();
        for (int i = 0; i < bytes.Length; i++)
        {
            var b = bytes[i];
            switch (b)
            {
                case 0x1B:
                    sb.Append("<ESC>");
                    break;
                case 0x0D:
                    sb.Append("<CR>");
                    break;
                case 0x0A:
                    sb.Append("<LF>");
                    break;
                case 0x09:
                    sb.Append("<TAB>");
                    break;
                default:
                    if (b < 0x20 || b > 0x7E)
                    {
                        sb.Append($"<0x{b:X2}>");
                    }
                    else
                    {
                        sb.Append((char)b);
                    }
                    break;
            }
        }
        return sb.ToString();
    }
}
