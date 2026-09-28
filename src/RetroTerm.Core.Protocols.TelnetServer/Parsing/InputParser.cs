using System;
using System.Collections.Generic;
using System.Text;

namespace RetroTerm.Core.Protocols.TelnetServer.Parsing;

/// <summary>
/// Parses input bytes into commands and escape sequences using a state machine.
/// Properly handles fragmented TCP delivery of escape sequences.
/// </summary>
public class InputParser
{
    private enum State
    {
        Normal,         // Waiting for input
        EscReceived,    // Got ESC, waiting for next byte
        CsiParams,      // Got ESC [, collecting parameters until final byte
        Ss3Received,    // Got ESC O, waiting for final byte (F1-F4)
        DcsString,      // Got ESC P, collecting until ST (ESC \)
    }

    private State _state = State.Normal;
    private readonly StringBuilder _escapeBuffer = new StringBuilder();
    private readonly Queue<ParsedInput> _outputQueue = new Queue<ParsedInput>();
    private DateTime _escapeStartTime;
    private const int EscapeTimeoutMs = 100; // Max time to wait for escape sequence completion

    /// <summary>
    /// Feed bytes into the parser
    /// </summary>
    public void Feed(ReadOnlySpan<byte> data)
    {
        for (int i = 0; i < data.Length; i++)
        {
            ProcessByte(data[i]);
        }
    }

    /// <summary>
    /// Feed a string into the parser
    /// </summary>
    public void Feed(string data)
    {
        for (int i = 0; i < data.Length; i++)
        {
            ProcessByte((byte)data[i]);
        }
    }

    /// <summary>
    /// Check for timeout on incomplete escape sequences
    /// Call this periodically when no data is arriving
    /// </summary>
    public void CheckTimeout()
    {
        if (_state != State.Normal && (DateTime.UtcNow - _escapeStartTime).TotalMilliseconds > EscapeTimeoutMs)
        {
            // Timeout - emit whatever we have as an incomplete escape sequence
            if (_escapeBuffer.Length > 0)
            {
                _outputQueue.Enqueue(new ParsedInput(InputType.EscapeSequence, _escapeBuffer.ToString(), isComplete: false));
                _escapeBuffer.Clear();
            }
            _state = State.Normal;
        }
    }

    /// <summary>
    /// Try to get the next parsed input
    /// </summary>
    public bool TryGetNext(out ParsedInput result)
    {
        CheckTimeout();
        if (_outputQueue.Count > 0)
        {
            result = _outputQueue.Dequeue();
            return true;
        }
        result = default;
        return false;
    }

    /// <summary>
    /// Check if there's pending input available
    /// </summary>
    public bool HasPendingInput => _outputQueue.Count > 0;

    /// <summary>
    /// Check if we're in the middle of parsing an escape sequence
    /// </summary>
    public bool IsParsingEscape => _state != State.Normal;

    /// <summary>
    /// Clears all queued output and resets parser state
    /// </summary>
    public void Clear()
    {
        _outputQueue.Clear();
        _escapeBuffer.Clear();
        _state = State.Normal;
    }

    private void ProcessByte(byte b)
    {
        switch (_state)
        {
            case State.Normal:
                ProcessNormal(b);
                break;
            case State.EscReceived:
                ProcessEscReceived(b);
                break;
            case State.CsiParams:
                ProcessCsiParams(b);
                break;
            case State.Ss3Received:
                ProcessSs3Received(b);
                break;
            case State.DcsString:
                ProcessDcsString(b);
                break;
        }
    }

    private void ProcessNormal(byte b)
    {
        if (b == 0x1B) // ESC
        {
            _state = State.EscReceived;
            _escapeBuffer.Clear();
            _escapeBuffer.Append((char)b);
            _escapeStartTime = DateTime.UtcNow;
        }
        else if (b >= 0x20 && b < 0x7F) // Printable ASCII
        {
            _outputQueue.Enqueue(new ParsedInput(InputType.Character, ((char)b).ToString(), isComplete: true));
        }
        else if (b == 0x0D || b == 0x0A) // CR or LF
        {
            _outputQueue.Enqueue(new ParsedInput(InputType.Enter, "\r\n", isComplete: true));
        }
        else if (b == 0x08) // BS — also TDV Cursor_Left (C0 code)
        {
            _outputQueue.Enqueue(new ParsedInput(InputType.Backspace, "\b", isComplete: true, "Backspace / TDV Cursor_Left"));
        }
        else if (b == 0x7F) // DEL
        {
            _outputQueue.Enqueue(new ParsedInput(InputType.Backspace, "\x7F", isComplete: true, "Delete (DEL)"));
        }
        else if (b == 0x09) // TAB — also TDV TAB (C0 code, 2115 mode)
        {
            _outputQueue.Enqueue(new ParsedInput(InputType.Tab, "\t", isComplete: true, "Tab / TDV TAB (C0)"));
        }
        else if (b < 0x20) // Other control characters
        {
            var name = IdentifyControlChar(b);
            _outputQueue.Enqueue(new ParsedInput(InputType.Control, ((char)b).ToString(), isComplete: true, name));
        }
    }

    /// <summary>
    /// Identifies single-byte control characters by name.
    /// Shows Ctrl+letter for 0x01-0x1A, plus TDV C0 name where applicable.
    /// </summary>
    private static string? IdentifyControlChar(byte b)
    {
        // Ctrl+letter: 0x01=Ctrl+A .. 0x1A=Ctrl+Z
        // Some also have TDV C0 meanings — show both
        return b switch
        {
            0x00 => "Ctrl+@ / NUL",
            0x01 => "Ctrl+A",
            0x02 => "Ctrl+B / TDV Video_Off",
            0x03 => "Ctrl+C / TDV Video_On",
            0x04 => "Ctrl+D / TDV Erase_Line",
            0x05 => "Ctrl+E / TDV Light1_On",
            0x06 => "Ctrl+F / TDV Light2_On",
            0x07 => "Ctrl+G / Bell",
            // 0x08 Ctrl+H = Backspace — handled before reaching here
            // 0x09 Ctrl+I = Tab — handled before reaching here
            // 0x0A Ctrl+J = LF — handled before reaching here
            0x0B => "Ctrl+K / TDV Cursor_Down",
            0x0C => "Ctrl+L / TDV Roll_Up",
            // 0x0D Ctrl+M = CR — handled before reaching here
            0x0E => "Ctrl+N / TDV Underline",
            0x0F => "Ctrl+O / TDV Normal",
            0x10 => "Ctrl+P / TDV Cursor_Load",
            0x11 => "Ctrl+Q",
            0x12 => "Ctrl+R",
            0x13 => "Ctrl+S",
            0x14 => "Ctrl+T",
            0x15 => "Ctrl+U / TDV Light3_On",
            0x16 => "Ctrl+V / TDV Lights_Off",
            0x17 => "Ctrl+W / TDV Roll_Down",
            0x18 => "Ctrl+X / TDV Cursor_Right",
            0x19 => "Ctrl+Y / TDV Erase_Page",
            0x1A => "Ctrl+Z",
            0x1C => "Ctrl+\\ / TDV Cursor_Up",
            0x1D => "Ctrl+] / TDV Cursor_Home",
            0x1E => "Ctrl+^",
            0x1F => "Ctrl+_",
            _ => null
        };
    }

    private void ProcessEscReceived(byte b)
    {
        _escapeBuffer.Append((char)b);

        if (b == '[') // CSI - Control Sequence Introducer
        {
            _state = State.CsiParams;
        }
        else if (b == 'O') // SS3 - Single Shift 3 (F1-F4)
        {
            _state = State.Ss3Received;
        }
        else if (b == 'P') // DCS - Device Control String
        {
            _state = State.DcsString;
        }
        else if (b >= 0x40 && b <= 0x7E) // Two-character escape sequence (ESC + final)
        {
            EmitEscapeSequence();
        }
        else if (b == 0x1B) // Another ESC - previous was standalone
        {
            // Emit standalone ESC
            _outputQueue.Enqueue(new ParsedInput(InputType.EscapeSequence, "\x1B", isComplete: true));
            // Start new escape sequence
            _escapeBuffer.Clear();
            _escapeBuffer.Append((char)b);
            _escapeStartTime = DateTime.UtcNow;
        }
        else if (b < 0x20) // Control char after ESC - emit ESC and process control
        {
            _outputQueue.Enqueue(new ParsedInput(InputType.EscapeSequence, "\x1B", isComplete: true));
            _state = State.Normal;
            ProcessNormal(b);
        }
        // else: intermediate byte (0x20-0x2F), stay in EscReceived
    }

    private void ProcessCsiParams(byte b)
    {
        _escapeBuffer.Append((char)b);

        if (b >= 0x40 && b <= 0x7E) // Final byte - sequence complete
        {
            EmitEscapeSequence();
        }
        else if (b == 0x1B) // New ESC - emit incomplete and start over
        {
            _outputQueue.Enqueue(new ParsedInput(InputType.EscapeSequence, _escapeBuffer.ToString(), isComplete: false));
            _escapeBuffer.Clear();
            _escapeBuffer.Append((char)b);
            _state = State.EscReceived;
            _escapeStartTime = DateTime.UtcNow;
        }
        // else: parameter byte (0x30-0x3F) or intermediate (0x20-0x2F), keep collecting
    }

    private void ProcessSs3Received(byte b)
    {
        _escapeBuffer.Append((char)b);

        if (b >= 0x40 && b <= 0x7E) // Final byte - sequence complete
        {
            EmitEscapeSequence();
        }
        else if (b == 0x1B) // New ESC - emit incomplete and start over
        {
            _outputQueue.Enqueue(new ParsedInput(InputType.EscapeSequence, _escapeBuffer.ToString(), isComplete: false));
            _escapeBuffer.Clear();
            _escapeBuffer.Append((char)b);
            _state = State.EscReceived;
            _escapeStartTime = DateTime.UtcNow;
        }
    }

    private void ProcessDcsString(byte b)
    {
        _escapeBuffer.Append((char)b);

        // DCS ends with ST (String Terminator) which is ESC \
        // Check if buffer ends with ESC \
        if (_escapeBuffer.Length >= 3)
        {
            var len = _escapeBuffer.Length;
            if (_escapeBuffer[len - 2] == '\x1B' && _escapeBuffer[len - 1] == '\\')
            {
                EmitEscapeSequence();
            }
        }
    }

    private void EmitEscapeSequence()
    {
        var seq = _escapeBuffer.ToString();
        _escapeBuffer.Clear();
        _state = State.Normal;

        // Identify the sequence
        var name = IdentifySequence(seq);
        _outputQueue.Enqueue(new ParsedInput(InputType.EscapeSequence, seq, isComplete: true, name));
    }

    internal static string? IdentifySequence(string seq)
    {
        return seq switch
        {
            // ===================================================================
            // VT100 Arrow keys — ESC [ A/B/C/D
            // ===================================================================
            "\x1B[A" => "VT100 Up",
            "\x1B[B" => "VT100 Down",
            "\x1B[C" => "VT100 Right",
            "\x1B[D" => "VT100 Left",

            // ===================================================================
            // VT100 F1-F4 (SS3 style) — ESC O P/Q/R/S
            // ===================================================================
            "\x1BOP" => "VT100 F1 (SS3)",
            "\x1BOQ" => "VT100 F2 (SS3)",
            "\x1BOR" => "VT100 F3 (SS3)",
            "\x1BOS" => "VT100 F4 (SS3)",

            // ===================================================================
            // VT220 F-keys — ESC [ nn ~
            // ===================================================================
            "\x1B[11~" => "VT220 F1",
            "\x1B[12~" => "VT220 F2",
            "\x1B[13~" => "VT220 F3",
            "\x1B[14~" => "VT220 F4",
            "\x1B[15~" => "VT220 F5",
            "\x1B[17~" => "VT220 F6",
            "\x1B[18~" => "VT220 F7",
            "\x1B[19~" => "VT220 F8",
            "\x1B[20~" => "VT220 F9",
            "\x1B[21~" => "VT220 F10",
            "\x1B[23~" => "VT220 F11",
            "\x1B[24~" => "VT220 F12",
            "\x1B[25~" => "VT220 F13",
            "\x1B[26~" => "VT220 F14",
            "\x1B[28~" => "VT220 F15",
            "\x1B[29~" => "VT220 F16",
            "\x1B[31~" => "VT220 F17",
            "\x1B[32~" => "VT220 F18",
            "\x1B[33~" => "VT220 F19",
            "\x1B[34~" => "VT220 F20",

            // ===================================================================
            // VT100/VT220 Navigation — ESC [ H/F/nn~
            // ===================================================================
            "\x1B[H" => "VT100 Home",
            "\x1B[F" => "VT100 End",
            "\x1B[1~" => "VT220 Home",
            "\x1B[4~" => "VT220 End",
            "\x1B[2~" => "VT220 Insert",
            "\x1B[3~" => "VT220 Delete",
            "\x1B[5~" => "VT220 PageUp",
            "\x1B[6~" => "VT220 PageDown",
            "\x1B[7~" => "VT220 Home (rxvt)",
            "\x1B[8~" => "VT220 End (rxvt)",

            // ===================================================================
            // VT100 Modifier + Arrow/Home/End — ESC [ 1;m A/B/C/D/H/F
            // Modifier: 2=Shift, 3=Alt, 5=Ctrl, 6=Ctrl+Shift
            // ===================================================================
            "\x1B[1;2A" => "VT100 Shift+Up",
            "\x1B[1;2B" => "VT100 Shift+Down",
            "\x1B[1;2C" => "VT100 Shift+Right",
            "\x1B[1;2D" => "VT100 Shift+Left",
            "\x1B[1;2H" => "VT100 Shift+Home",
            "\x1B[1;2F" => "VT100 Shift+End",
            "\x1B[1;3A" => "VT100 Alt+Up",
            "\x1B[1;3B" => "VT100 Alt+Down",
            "\x1B[1;3C" => "VT100 Alt+Right",
            "\x1B[1;3D" => "VT100 Alt+Left",
            "\x1B[1;5A" => "VT100 Ctrl+Up",
            "\x1B[1;5B" => "VT100 Ctrl+Down",
            "\x1B[1;5C" => "VT100 Ctrl+Right",
            "\x1B[1;5D" => "VT100 Ctrl+Left",
            "\x1B[1;5H" => "VT100 Ctrl+Home",
            "\x1B[1;5F" => "VT100 Ctrl+End",
            "\x1B[1;6A" => "VT100 Ctrl+Shift+Up",
            "\x1B[1;6B" => "VT100 Ctrl+Shift+Down",
            "\x1B[1;6C" => "VT100 Ctrl+Shift+Right",
            "\x1B[1;6D" => "VT100 Ctrl+Shift+Left",

            // ===================================================================
            // VT220 Modifier + Navigation — ESC [ n;m ~
            // Modifier: 2=Shift, 3=Alt, 5=Ctrl
            // ===================================================================
            "\x1B[2;2~" => "VT220 Shift+Insert",
            "\x1B[3;2~" => "VT220 Shift+Delete",
            "\x1B[5;2~" => "VT220 Shift+PageUp",
            "\x1B[6;2~" => "VT220 Shift+PageDown",
            "\x1B[2;5~" => "VT220 Ctrl+Insert",
            "\x1B[3;5~" => "VT220 Ctrl+Delete",
            "\x1B[5;5~" => "VT220 Ctrl+PageUp",
            "\x1B[6;5~" => "VT220 Ctrl+PageDown",

            // ===================================================================
            // VT220 Modifier + F-keys — ESC [ nn;m ~
            // Modifier: 2=Shift, 3=Alt, 5=Ctrl
            // ===================================================================
            // Shift+F1..F12
            "\x1B[11;2~" => "VT220 Shift+F1",
            "\x1B[12;2~" => "VT220 Shift+F2",
            "\x1B[13;2~" => "VT220 Shift+F3",
            "\x1B[14;2~" => "VT220 Shift+F4",
            "\x1B[15;2~" => "VT220 Shift+F5",
            "\x1B[17;2~" => "VT220 Shift+F6",
            "\x1B[18;2~" => "VT220 Shift+F7",
            "\x1B[19;2~" => "VT220 Shift+F8",
            "\x1B[20;2~" => "VT220 Shift+F9",
            "\x1B[21;2~" => "VT220 Shift+F10",
            "\x1B[23;2~" => "VT220 Shift+F11",
            "\x1B[24;2~" => "VT220 Shift+F12",
            // Ctrl+F1..F12
            "\x1B[11;5~" => "VT220 Ctrl+F1",
            "\x1B[12;5~" => "VT220 Ctrl+F2",
            "\x1B[13;5~" => "VT220 Ctrl+F3",
            "\x1B[14;5~" => "VT220 Ctrl+F4",
            "\x1B[15;5~" => "VT220 Ctrl+F5",
            "\x1B[17;5~" => "VT220 Ctrl+F6",
            "\x1B[18;5~" => "VT220 Ctrl+F7",
            "\x1B[19;5~" => "VT220 Ctrl+F8",
            "\x1B[20;5~" => "VT220 Ctrl+F9",
            "\x1B[21;5~" => "VT220 Ctrl+F10",
            "\x1B[23;5~" => "VT220 Ctrl+F11",
            "\x1B[24;5~" => "VT220 Ctrl+F12",

            // ===================================================================
            // TDV old-style control sequences (pre-Extended Control Mode)
            // ===================================================================
            "\x1B[Z" => "TDV Tab_Left",
            "\x1B[M" => "TDV Del_Line",
            "\x1B[L" => "TDV Ins_Line",
            "\x1B[P" => "TDV Del_Char",
            "\x1B[@" => "TDV Ins_Char",

            // ===================================================================
            // TDV PUSH keys — DCS (ESC P N/S <digit> ESC \)
            // ===================================================================
            "\x1BPN1\x1B\\" => "TDV PUSH1",
            "\x1BPN2\x1B\\" => "TDV PUSH2",
            "\x1BPN3\x1B\\" => "TDV PUSH3",
            "\x1BPN4\x1B\\" => "TDV PUSH4",
            "\x1BPN5\x1B\\" => "TDV PUSH5",
            "\x1BPN6\x1B\\" => "TDV PUSH6",
            "\x1BPN7\x1B\\" => "TDV PUSH7",
            "\x1BPN8\x1B\\" => "TDV PUSH8",
            "\x1BPS1\x1B\\" => "TDV Shift+PUSH1",
            "\x1BPS2\x1B\\" => "TDV Shift+PUSH2",
            "\x1BPS3\x1B\\" => "TDV Shift+PUSH3",
            "\x1BPS4\x1B\\" => "TDV Shift+PUSH4",
            "\x1BPS5\x1B\\" => "TDV Shift+PUSH5",
            "\x1BPS6\x1B\\" => "TDV Shift+PUSH6",
            "\x1BPS7\x1B\\" => "TDV Shift+PUSH7",
            "\x1BPS8\x1B\\" => "TDV Shift+PUSH8",

            // ===================================================================
            // TDV Extended Control Mode — CSI nn _ sequences
            // Source: TDV-2200/9 User's Guide, Section 9.1
            // Format: ESC [ <digit> <digit> _ (1B 5B nn 5F)
            // ===================================================================

            // Row G — Editing keys (MERK, FELT, AVSN, SETN, ORD)
            "\x1B[00_" => "TDV MERK",
            "\x1B[01_" => "TDV Shift+MERK",
            "\x1B[02_" => "TDV FELT",
            "\x1B[03_" => "TDV Shift+FELT",
            "\x1B[04_" => "TDV AVSN",
            "\x1B[05_" => "TDV Shift+AVSN",
            "\x1B[06_" => "TDV SETN",
            "\x1B[07_" => "TDV Shift+SETN",
            "\x1B[08_" => "TDV ORD",
            "\x1B[09_" => "TDV Shift+ORD",

            // Row G — Action keys (STRYK, KOPI, FLYTT)
            "\x1B[10_" => "TDV STRYK",
            "\x1B[11_" => "TDV Shift+STRYK",
            "\x1B[12_" => "TDV KOPI",
            "\x1B[13_" => "TDV Shift+KOPI",
            "\x1B[14_" => "TDV FLYTT",
            "\x1B[15_" => "TDV Shift+FLYTT",

            // Row F — F47 TAB+/TAB-, F48 SEARCH, F49 REPLACE
            "\x1B[16_" => "TDV TAB+",
            "\x1B[17_" => "TDV TAB-",
            "\x1B[18_" => "TDV SEARCH",
            "\x1B[19_" => "TDV Shift+SEARCH",
            "\x1B[20_" => "TDV REPLACE",
            "\x1B[21_" => "TDV Shift+REPLACE",

            // Row E middle — GUILLEMETS, JUST, SINGLEGUILLEMETS
            "\x1B[22_" => "TDV GUILLEMETS",
            "\x1B[23_" => "TDV Shift+GUILLEMETS",
            "\x1B[24_" => "TDV JUST",
            "\x1B[25_" => "TDV Shift+JUST",
            "\x1B[26_" => "TDV SINGLEGUILLEMETS",
            "\x1B[27_" => "TDV Shift+SINGLEGUILLEMETS",

            // Row D — ROLLUP, ANGRE, ROLLDN
            "\x1B[28_" => "TDV ROLLUP",
            "\x1B[29_" => "TDV ROLLLEFT",
            "\x1B[30_" => "TDV ANGRE",
            "\x1B[31_" => "TDV Shift+ANGRE",
            "\x1B[32_" => "TDV ROLLDN",
            "\x1B[33_" => "TDV ROLLRIGHT",

            // Row C — FIELDLEFT, FIELDRIGHT
            "\x1B[34_" => "TDV FIELDLEFT",
            "\x1B[35_" => "TDV Shift+FIELDLEFT",
            "\x1B[36_" => "TDV FIELDRIGHT",
            "\x1B[37_" => "TDV Shift+FIELDRIGHT",

            // Row A — TABLEFT, TABRIGHT
            "\x1B[38_" => "TDV TABLEFT",
            "\x1B[39_" => "TDV Shift+TABLEFT",
            "\x1B[40_" => "TDV TABRIGHT",
            "\x1B[41_" => "TDV Shift+TABRIGHT",

            // System keys (FUNK, SKRIV, HJELP, SLUTT)
            "\x1B[42_" => "TDV FUNK",
            "\x1B[43_" => "TDV Shift+FUNK",
            "\x1B[44_" => "TDV SKRIV",
            "\x1B[45_" => "TDV Shift+SKRIV",
            "\x1B[46_" => "TDV HJELP",
            "\x1B[47_" => "TDV Shift+HJELP",
            "\x1B[48_" => "TDV SLUTT",
            "\x1B[49_" => "TDV Shift+SLUTT",

            // TDV F1-F4 (Extended Control Mode)
            "\x1B[50_" => "TDV F1",
            "\x1B[51_" => "TDV Shift+F1",
            "\x1B[52_" => "TDV F2",
            "\x1B[53_" => "TDV Shift+F2",
            "\x1B[54_" => "TDV Ctrl+F2",
            "\x1B[55_" => "TDV F3",
            "\x1B[56_" => "TDV Shift+F3",
            "\x1B[57_" => "TDV Ctrl+F3",
            "\x1B[58_" => "TDV F4",
            "\x1B[59_" => "TDV Shift+F4",

            // TDV F5-F8 (Extended Control Mode)
            "\x1B[60_" => "TDV F5",
            "\x1B[61_" => "TDV Shift+F5",
            "\x1B[62_" => "TDV F6",
            "\x1B[63_" => "TDV Shift+F6",
            "\x1B[64_" => "TDV F7",
            "\x1B[65_" => "TDV Shift+F7",
            "\x1B[66_" => "TDV F8",
            "\x1B[67_" => "TDV Shift+F8",

            // TDV Numpad Function Mode (CSI nn _)
            "\x1B[68_" => "TDV Numpad_0",
            "\x1B[69_" => "TDV Numpad_1",
            "\x1B[70_" => "TDV Numpad_2",
            "\x1B[71_" => "TDV Numpad_3",
            "\x1B[72_" => "TDV Numpad_4",
            "\x1B[73_" => "TDV Numpad_5",
            "\x1B[74_" => "TDV Numpad_6",
            "\x1B[75_" => "TDV Numpad_7",
            "\x1B[76_" => "TDV Numpad_8",
            "\x1B[77_" => "TDV Numpad_9",
            "\x1B[78_" => "TDV Numpad_.",
            "\x1B[79_" => "TDV Numpad_-",
            "\x1B[80_" => "TDV Numpad_SP",
            "\x1B[81_" => "TDV Numpad_ENTER",

            // TDV EKSP/INNS, MODE
            "\x1B[82_" => "TDV EKSP",
            "\x1B[83_" => "TDV INNS",
            "\x1B[84_" => "TDV MODE",
            "\x1B[85_" => "TDV Shift+MODE",

            // TDV NEWPARA
            "\x1B[86_" => "TDV NEWPARA",
            "\x1B[87_" => "TDV Shift+NEWPARA",

            _ => null
        };
    }
}

/// <summary>
/// Type of parsed input
/// </summary>
public enum InputType
{
    Character,       // Regular printable character
    Enter,           // Enter/Return key
    Backspace,       // Backspace or Delete
    Tab,             // Tab key
    Control,         // Control character (Ctrl+A, etc.)
    EscapeSequence,  // Escape sequence (F-keys, arrows, etc.)
}

/// <summary>
/// Parsed input result
/// </summary>
public readonly struct ParsedInput
{
    public InputType Type { get; }
    public string Value { get; }
    public bool IsComplete { get; }
    public string? Name { get; }  // Human-readable name for escape sequences

    public ParsedInput(InputType type, string value, bool isComplete, string? name = null)
    {
        Type = type;
        Value = value;
        IsComplete = isComplete;
        Name = name;
    }

    public bool IsMenuCommand => Type == InputType.Character && Value.Length == 1;
    public char CommandChar => IsMenuCommand ? Value[0] : '\0';

    public override string ToString()
    {
        if (Name != null)
            return $"{Type}: {Name} ({ToVisible(Value)})";
        return $"{Type}: {ToVisible(Value)}";
    }

    private static string ToVisible(string s)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < s.Length; i++)
        {
            var c = s[i];
            if (c == 0x1B)
                sb.Append("ESC");
            else if (c < 0x20)
                sb.Append($"^{(char)(c + 0x40)}");
            else if (c == 0x7F)
                sb.Append("DEL");
            else
                sb.Append(c);
        }
        return sb.ToString();
    }
}
