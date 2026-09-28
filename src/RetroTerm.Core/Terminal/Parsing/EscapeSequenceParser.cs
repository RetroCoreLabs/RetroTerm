using System;
using System.Buffers;
using System.Collections.Generic;

namespace RetroTerm.Core.Terminal.Parsing;

/// <summary>
/// High-performance escape sequence parser using a hybrid approach:
/// - Fast path for common sequences (CSI with simple parameters)
/// - State machine for complex sequences (DCS, OSC, etc.)
/// 
/// Designed for zero allocations using ReadOnlySpan of byte and ArrayPool.
/// </summary>
public class EscapeSequenceParser
{
    private ParserState _state;
    private int _paramCount;

    /// <summary>
    /// True when an ESC arrived inside a string sequence and we are waiting for the next
    /// byte to tell us whether it was ST ("ESC \") or an abort. See ProcessByte.
    /// </summary>
    private bool _escapePendingInString;

    // Raw-byte collection state, driven by ExpectRawBytes.
    private RawBytesHandler? _rawHandler;
    private int _rawRemaining;
    private int _rawCount;
    private readonly byte[] _rawBuffer;
    private readonly int[] _params;

    /// <summary>
    /// Parallel to <see cref="_params"/>: true when that parameter was introduced by ':'
    /// rather than ';', i.e. it is a sub-parameter of the preceding one.
    /// </summary>
    private readonly bool[] _paramIsSub;

    private readonly byte[] _intermediateBuffer;
    private int _intermediateCount;
    private byte _private;

    // OSC/DCS string accumulation
    private byte[]? _stringBuffer;
    private int _stringLength;

    // UTF-8 state
    private uint _utf8Codepoint;
    private int _utf8BytesRemaining;

    /// <summary>
    /// Whether bytes at 0xA0 and above start a UTF-8 sequence (true) or are single characters in
    /// their own right (false).
    ///
    /// The receive half of the terminal's transport encoding — see TerminalProfile.TransportEncoding
    /// for the send half. Defaults to true, which is what every session did before this existed.
    ///
    /// SCOPE: this switch covers 0xA0 and above only. The 0x80-0x9F range keeps being treated as
    /// 8-bit C1 controls on both settings; whether a TDV line should read those as data instead is
    /// a separate question, and one I have not verified against the TDV spec.
    /// </summary>
    public bool DecodeUtf8 { get; set; } = true;

    /// <summary>
    /// Whether <c>ESC "</c> begins a Norsk Data graphics sequence rather than an ordinary escape
    /// with <c>"</c> as an intermediate byte.
    ///
    /// OFF BY DEFAULT, AND THAT DEFAULT IS CORRECT, NOT CAUTIOUS. 0x22 is a legal ECMA-48
    /// intermediate, so <c>ESC "5d</c> genuinely IS an escape with intermediate <c>"</c> and final
    /// <c>5</c> on a VT, an xterm, or anything else that is not an ND graphic terminal. Turning
    /// this on globally would change how those terminals read a legal sequence.
    ///
    /// This is the "ground mode" seam from section C.1 of the architecture review, and the same
    /// shape VT52's binary coordinates and Tektronix's vector bytes will need: the parser
    /// recognises structure, the profile decides which structures exist, and meaning stays in the
    /// module. It is also why TDV grew <c>TDVInputProcessor</c> as a pre-parser filter - that was
    /// this seam, built outside the parser because the parser had none.
    /// </summary>
    public bool NorskDataGraphicsSequences { get; set; }

    private const int MaxParams = 32;
    private const int MaxIntermediates = 2;
    private const int InitialStringBufferSize = 256;

    /// <summary>
    /// How much DCS payload to accumulate before handing it to <see cref="OnDcsPut"/>.
    /// Chunked rather than per-byte so a full-screen Sixel image costs a few hundred
    /// callbacks instead of hundreds of thousands, and rather than whole-payload so an
    /// image never has to be held in memory twice.
    /// </summary>
    private const int DcsChunkSize = 4096;

    /// <summary>
    /// Largest run <see cref="ExpectRawBytes"/> will collect. The known users need 2
    /// (VT52 ESC Y row/column, ND DLE row/column); the cap keeps a malformed request from
    /// silently swallowing the stream.
    /// </summary>
    public const int MaxRawBytes = 8;

    /// <summary>
    /// Delegate for handling parsed escape sequences
    /// </summary>
    public delegate void SequenceHandler(EscapeSequenceParser parser);

    /// <summary>
    /// Delegate for handling string data (OSC, DCS)
    /// </summary>
    public delegate void StringDataHandler(ReadOnlySpan<byte> data);

    /// <summary>
    /// Receives a fixed-length run of raw bytes requested by <see cref="ExpectRawBytes"/>.
    /// </summary>
    /// <param name="bytes">
    /// Exactly the number of bytes that were asked for.
    /// </param>
    public delegate void RawBytesHandler(ReadOnlySpan<byte> bytes);

    /// <summary>
    /// Gets first refusal on every byte while the parser is in Ground state, before any
    /// ANSI interpretation happens.
    /// </summary>
    /// <param name="b">
    /// The byte from the host.
    /// </param>
    /// <returns>
    /// True if the byte was consumed and must not be interpreted as text or a control.
    /// </returns>
    public delegate bool GroundByteFilter(byte b);

    /// <summary>
    /// Event raised when a printable character is received
    /// </summary>
    public event Action<uint>? OnCharacter;

    /// <summary>
    /// Event raised when a C0/C1 control character is received (e.g., LF, CR, BS)
    /// </summary>
    public event Action<byte>? OnExecute;

    /// <summary>
    /// Event raised when an ESC sequence is completed (e.g., ESC D, ESC M)
    /// </summary>
    public event SequenceHandler? OnEscapeDispatch;

    /// <summary>
    /// Event raised when a CSI sequence is completed (e.g., CSI 2 J, CSI 1;1 H)
    /// </summary>
    public event SequenceHandler? OnCsiDispatch;

    /// <summary>
    /// Raised when a Norsk Data graphics sequence completes, e.g. <c>ESC "13;10l</c>.
    ///
    /// Its own event rather than folding into <see cref="OnCsiDispatch"/>: an ND <c>l</c> and a DEC
    /// mode-reset <c>l</c> are different commands that happen to share a final byte, and a handler
    /// that had to work out which it was looking at would be guessing.
    /// </summary>
    public event SequenceHandler? OnNorskDataDispatch;

    /// <summary>
    /// Event raised when an OSC sequence is completed
    /// </summary>
    public event StringDataHandler? OnOscDispatch;

    /// <summary>
    /// Event raised when a DCS sequence starts
    /// </summary>
    public event SequenceHandler? OnDcsHook;

    /// <summary>
    /// Raised with DCS payload as it arrives, in chunks of up to <see cref="DcsChunkSize"/>
    /// bytes, plus a final partial chunk when the sequence ends. A handler must treat this
    /// as a stream: one DCS can raise it many times.
    ///
    /// The span is only valid for the duration of the call - copy anything you keep.
    /// </summary>
    public event StringDataHandler? OnDcsPut;

    /// <summary>
    /// Event raised when a DCS sequence ends
    /// </summary>
    public event Action? OnDcsUnhook;

    public EscapeSequenceParser()
    {
        _state = ParserState.Ground;
        _params = new int[MaxParams];
        _paramIsSub = new bool[MaxParams];
        _rawBuffer = new byte[MaxRawBytes];
        _intermediateBuffer = new byte[MaxIntermediates];
        Reset();
    }

    /// <summary>
    /// Gets the current parser state
    /// </summary>
    public ParserState State => _state;

    /// <summary>
    /// Gets the final byte of the current sequence
    /// </summary>
    public byte FinalByte { get; private set; }

    /// <summary>
    /// Gets the private marker byte (? ! > etc. after CSI)
    /// </summary>
    public byte PrivateMarker => _private;

    /// <summary>
    /// Gets the parameters of the current CSI sequence
    /// </summary>
    public ReadOnlySpan<int> Parameters => _params.AsSpan(0, _paramCount);

    /// <summary>
    /// Gets the intermediate bytes (between params and final byte)
    /// </summary>
    public ReadOnlySpan<byte> Intermediates => _intermediateBuffer.AsSpan(0, _intermediateCount);

    /// <summary>
    /// Flags parallel to <see cref="Parameters"/>: true where that parameter followed a
    /// ':' rather than a ';', meaning it is a sub-parameter of the preceding parameter.
    /// A handler that does not care about the distinction can ignore this and read
    /// Parameters as a flat list, which is what the colon-less forms produce anyway.
    /// </summary>
    public ReadOnlySpan<bool> SubParameterFlags => _paramIsSub.AsSpan(0, _paramCount);

    /// <summary>
    /// Optional filter that sees every Ground-state byte before the parser interprets it.
    ///
    /// This is the seam for terminal modes whose bytes are NOT ANSI text: Tektronix vector
    /// coordinates after GS, the ND/TDV DLE cursor-addressing introducer, and anything else
    /// that redefines what a plain byte means. Set it to null to return to normal.
    ///
    /// It exists because the alternative already happened once and was a mistake: the TDV
    /// path filters DLE bytes in TDVInputProcessor BEFORE handing anything to the parser,
    /// which is why the parser cannot see terminal modes and terminal modes cannot see the
    /// parser. A filter here keeps the byte stream in one place.
    ///
    /// The parser still owns syntax: the filter decides only whether a byte belongs to the
    /// active mode, never what it means on screen.
    /// </summary>
    public GroundByteFilter? GroundFilter { get; set; }

    /// <summary>
    /// Asks the parser to hand the next <paramref name="count"/> bytes over verbatim,
    /// bypassing all interpretation, then call <paramref name="handler"/> with them.
    ///
    /// This is how a sequence with BINARY parameters is read - the bytes may be anything,
    /// including 0x1B, so they must not go through escape processing. Call it from inside
    /// a dispatch handler:
    ///   VT52 <c>ESC Y row col</c> - request 2 bytes when 'Y' dispatches;
    ///   ND/TDV <c>DLE row col</c> - request 2 bytes when DLE is executed.
    /// Without it, those coordinate bytes were printed to the screen as text.
    /// </summary>
    /// <param name="count">
    /// How many bytes to collect; 1 to <see cref="MaxRawBytes"/>.
    /// </param>
    /// <param name="handler">
    /// Called once, with exactly that many bytes.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// count is outside the supported range.
    /// </exception>
    /// <exception cref="ArgumentNullException">
    /// handler is null.
    /// </exception>
    public void ExpectRawBytes(int count, RawBytesHandler handler)
    {
        if (count < 1 || count > MaxRawBytes)
            throw new ArgumentOutOfRangeException(nameof(count), count,
                $"Raw byte requests must be between 1 and {MaxRawBytes}.");
        if (handler == null)
            throw new ArgumentNullException(nameof(handler));

        _rawHandler = handler;
        _rawRemaining = count;
        _rawCount = 0;
    }

    /// <summary>
    /// True while a <see cref="ExpectRawBytes"/> request is still being filled.
    /// </summary>
    public bool IsCollectingRawBytes => _rawRemaining > 0;

    /// <summary>
    /// Whether the parameter at <paramref name="index"/> is a sub-parameter of the one
    /// before it.
    /// </summary>
    /// <param name="index">
    /// Index into <see cref="Parameters"/>.
    /// </param>
    /// <returns>
    /// True for a ':'-introduced parameter; false otherwise or when out of range.
    /// </returns>
    public bool IsSubParameter(int index)
    {
        return index > 0 && index < _paramCount && _paramIsSub[index];
    }

    /// <summary>
    /// Resets the parser to initial state
    /// </summary>
    public void Reset()
    {
        _state = ParserState.Ground;
        _paramCount = 0;
        _intermediateCount = 0;
        _private = 0;
        FinalByte = 0;
        _stringLength = 0;
        _utf8BytesRemaining = 0;
        _utf8Codepoint = 0;

        // Abandon any half-finished string terminator or raw-byte request. GroundFilter is
        // deliberately NOT cleared: it represents a terminal mode the emulator owns, and a
        // parser reset (RIS, reconnect) must not silently switch that mode off behind the
        // emulator's back.
        _escapePendingInString = false;
        _rawHandler = null;
        _rawRemaining = 0;
        _rawCount = 0;
    }

    /// <summary>
    /// Processes input bytes and dispatches events for parsed sequences
    /// </summary>
    public int ProcessBytes(ReadOnlySpan<byte> data)
    {
        for (var i = 0; i < data.Length; i++)
        {
            ProcessByte(data[i]);

            // A dispatched sequence may have changed where the REST of this chunk belongs. Printer
            // controller mode is the one that does it: CSI 5 i means every following byte goes to
            // the printer, and if the parser ran on to the end of the chunk those bytes would land
            // on the screen instead. So a handler can ask to stop here and be given the rest.
            if (StopRequested)
            {
                StopRequested = false;
                return i + 1;
            }
        }

        return data.Length;
    }

    /// <summary>
    /// Asks <see cref="ProcessBytes(ReadOnlySpan{byte})"/> to return after the current byte.
    /// </summary>
    /// <remarks>
    /// Set by a sequence handler that has changed where the remaining bytes belong. Cleared by the
    /// parser as it returns, so it never carries into the next chunk.
    /// </remarks>
    public bool StopRequested { get; set; }

    /// <summary>
    /// Processes a single input byte
    /// </summary>
    private void ProcessByte(byte b)
    {
        // Raw-byte collection comes FIRST, ahead of even ESC handling: the bytes requested
        // by ExpectRawBytes are binary parameters that may legitimately be 0x1B or any
        // control code, so nothing may interpret them.
        if (_rawRemaining > 0)
        {
            _rawBuffer[_rawCount++] = b;
            if (--_rawRemaining == 0)
            {
                var handler = _rawHandler;
                _rawHandler = null;
                int count = _rawCount;
                _rawCount = 0;
                handler?.Invoke(new ReadOnlySpan<byte>(_rawBuffer, 0, count));
            }
            return;
        }

        // ESC (0x1B) aborts whatever is in progress and starts a new escape sequence.
        // This is the recovery path for a stream that went wrong mid-sequence.
        //
        // EXCEPT inside a string sequence (DCS payload / OSC / APC / PM / SOS). There ESC
        // is the first half of ST ("ESC \", 0x1B 0x5C), the standard terminator, so
        // swallowing it here made ST impossible and left the parser stuck in the string
        // until some later ESC happened to rescue it. Hosts overwhelmingly terminate with
        // ESC \ rather than the 8-bit 0x9C, so this blocked Sixel, ReGIS and DECUDK
        // outright. Those states get one byte of lookahead instead: see
        // HandleStringTerminatorEscape, which finishes the string on '\' and treats
        // anything else as a genuine abort.
        if (b == 0x1B && _state != ParserState.Escape)
        {
            if (IsStringState(_state))
            {
                _escapePendingInString = true;
                return;
            }

            // Reset any invalid state and start escape sequence
            if (_state == ParserState.Utf8Sequence)
            {
                // Cancel incomplete UTF-8 sequence
                _utf8BytesRemaining = 0;
                _utf8Codepoint = 0;
            }
            _state = ParserState.Escape;
            ClearParams();
            return;
        }

        // An ESC seen inside a string sequence is resolved here, one byte later.
        if (_escapePendingInString)
        {
            _escapePendingInString = false;
            HandleStringTerminatorEscape(b);
            return;
        }

        // Fast path for common cases in Ground state
        if (_state == ParserState.Ground)
        {
            // An active ground mode (Tektronix vectors, ND DLE addressing, ...) gets first
            // refusal. Only consulted in Ground, so it can never interfere with a partly
            // parsed escape sequence.
            if (GroundFilter != null && GroundFilter(b))
            {
                return;
            }

            if (b >= 0x20 && b <= 0x7E)
            {
                // Printable ASCII - most common case
                OnCharacter?.Invoke(b);
                return;
            }
            else if (b == 0x1B) // ESC
            {
                _state = ParserState.Escape;
                ClearParams();
                return;
            }
            else if (b < 0x20)
            {
                // C0 control character
                OnExecute?.Invoke(b);
                return;
            }
            // ORDER MATTERS: the 8-bit C1 range (0x80-0x9F) MUST be tested BEFORE the
            // generic ">= 0x80 means UTF-8 lead byte" branch below. It used to sit after
            // it, which made every C1 control unreachable dead code — 0x9B (CSI) fell
            // into HandleUtf8Start, matched none of the 0xC0/0xE0/0xF0 lead-byte masks
            // and was emitted as U+FFFD. That silently broke every 8-bit host: VT220+
            // in 8-bit mode sends 0x9B/0x90/0x9D instead of ESC [ / ESC P / ESC ].
            else if (b == 0x9B) // C1 CSI — equivalent to ESC [
            {
                _state = ParserState.CsiEntry;
                ClearParams();
                return;
            }
            else if (b == 0x9D) // C1 OSC — equivalent to ESC ]
            {
                _state = ParserState.OscString;
                _stringLength = 0;
                return;
            }
            else if (b == 0x90) // C1 DCS — equivalent to ESC P
            {
                _state = ParserState.DcsEntry;
                ClearParams();
                return;
            }
            else if (b >= 0x80 && b <= 0x9F)
            {
                // Remaining C1 controls (IND 0x84, NEL 0x85, HTS 0x88, RI 0x8D, SS2 0x8E,
                // SS3 0x8F, ST 0x9C, ...). Delivered to OnExecute exactly like C0 so the
                // emulator can treat them as the single-byte controls they are.
                OnExecute?.Invoke(b);
                return;
            }
            else if (b >= 0xA0)
            {
                if (!DecodeUtf8)
                {
                    // 8-bit line: this byte IS the character. Treating it as a UTF-8 lead byte
                    // corrupts an ND/TDV stream twice over — 0xC5 alone became U+FFFD and the
                    // byte was lost, and 0xC5 followed by a byte in 0x80-0xBF was folded into
                    // ONE wrong codepoint, so two characters of host output became one glyph.
                    OnCharacter?.Invoke(b);
                    return;
                }

                // UTF-8 lead byte. A byte here that is not a valid lead (0xA0-0xBF, i.e.
                // a stray continuation) yields U+FFFD inside HandleUtf8Start.
                HandleUtf8Start(b);
                return;
            }
        }

        // State machine for complex sequences
        ProcessByteStateMachine(b);
    }

    private void HandleUtf8Start(byte b)
    {
        if ((b & 0xE0) == 0xC0)
        {
            // 2-byte sequence
            _utf8Codepoint = (uint)(b & 0x1F);
            _utf8BytesRemaining = 1;
            _state = ParserState.Utf8Sequence;
        }
        else if ((b & 0xF0) == 0xE0)
        {
            // 3-byte sequence
            _utf8Codepoint = (uint)(b & 0x0F);
            _utf8BytesRemaining = 2;
            _state = ParserState.Utf8Sequence;
        }
        else if ((b & 0xF8) == 0xF0)
        {
            // 4-byte sequence
            _utf8Codepoint = (uint)(b & 0x07);
            _utf8BytesRemaining = 3;
            _state = ParserState.Utf8Sequence;
        }
        else
        {
            // Invalid UTF-8, print replacement character
            OnCharacter?.Invoke(0xFFFD);
        }
    }

    private void ProcessByteStateMachine(byte b)
    {
        // CAN (0x18) and SUB (0x1A) ABORT whatever sequence is in flight. Both mean "forget what I
        // was saying", they take effect in every sequence state, and neither prints anything.
        //
        // Without this they were treated as ordinary C0 controls, which are executed and leave the
        // sequence RUNNING - so "ESC [ CAN D" finished as CSI D and moved the cursor back a column.
        // The xterm.js fixtures t0014-CAN and t0015-SUB are eight lines of exactly that shape, and
        // a real xterm prints "abcdDefgh" on every one of them: the sequence is dropped and the 'D'
        // that follows is plain text.
        //
        // Some DEC hardware displays a reverse question mark where a SUB arrived. A real xterm
        // shows nothing, and the fixture's expected screens are identical for CAN and SUB, so
        // nothing is what this does.
        //
        // Utf8Sequence is left out on purpose: a byte that is not a continuation byte is already
        // handled there, and it prints the replacement character before re-reading the byte from
        // Ground - which is where CAN and SUB then correctly do nothing.
        //
        // The control is DELIVERED FIRST and the sequence abandoned afterwards, in that order,
        // because a Tektronix 4014 gives ESC SUB a meaning of its own - raise the crosshair and arm
        // GIN - and reads it while the parser is still in the Escape state. Aborting first threw
        // that away. On a VT or an xterm the same delivery does nothing, so both terminals get what
        // they need out of one rule.
        if ((b == 0x18 || b == 0x1A) && _state != ParserState.Utf8Sequence)
        {
            OnExecute?.Invoke(b);
            ClearParams();
            _state = ParserState.Ground;
            return;
        }

        switch (_state)
        {
            case ParserState.Utf8Sequence:
                if ((b & 0xC0) == 0x80)
                {
                    // Valid UTF-8 continuation byte
                    _utf8Codepoint = (_utf8Codepoint << 6) | (uint)(b & 0x3F);
                    _utf8BytesRemaining--;
                    if (_utf8BytesRemaining == 0)
                    {
                        OnCharacter?.Invoke(_utf8Codepoint);
                        _state = ParserState.Ground;
                        _utf8Codepoint = 0;
                    }
                }
                else
                {
                    // Invalid UTF-8 continuation - reset to Ground and re-process this byte
                    // This handles cases where ESC or other control characters appear during UTF-8 sequence
                    OnCharacter?.Invoke(0xFFFD);
                    // Reset UTF-8 state completely before re-processing
                    _utf8BytesRemaining = 0;
                    _utf8Codepoint = 0;
                    _state = ParserState.Ground;
                    ProcessByte(b); // Re-process this byte in Ground state (handles ESC correctly)
                }
                break;

            case ParserState.Escape:
                HandleEscapeState(b);
                break;

            case ParserState.NorskDataParam:
                HandleNorskDataParamState(b);
                break;

            case ParserState.EscapeIntermediate:
                HandleEscapeIntermediateState(b);
                break;

            case ParserState.CsiEntry:
            case ParserState.CsiParam:
            case ParserState.CsiIntermediate:
                HandleCsiState(b);
                break;

            case ParserState.CsiIgnore:
                HandleCsiIgnoreState(b);
                break;

            case ParserState.OscString:
                HandleOscState(b);
                break;

            case ParserState.DcsEntry:
            case ParserState.DcsParam:
            case ParserState.DcsIntermediate:
            case ParserState.DcsPassthrough:
                HandleDcsState(b);
                break;

            default:
                // Other states - simplified for now
                _state = ParserState.Ground;
                break;
        }
    }

    private void HandleEscapeState(byte b)
    {
        if (b == '[')
        {
            _state = ParserState.CsiEntry;
            ClearParams();
        }
        else if (b == ']')
        {
            _state = ParserState.OscString;
            _stringLength = 0;
        }
        else if (b == 'P')
        {
            _state = ParserState.DcsEntry;
            ClearParams();
        }
        else if (b == '"' && NorskDataGraphicsSequences)
        {
            // A Norsk Data graphics sequence: ESC " params final, e.g. ESC "13;10l.
            //
            // This branch is BEFORE the intermediate test on purpose, and it is gated on purpose.
            // 0x22 is a legal ECMA-48 intermediate byte, so by default ESC "5d is read as an
            // escape with intermediate '"' and final '5', leaving the 'd' to be printed - which is
            // the CORRECT reading for every terminal that is not an ND. Only a profile that knows
            // it is talking to an ND graphic terminal may switch this on. See section C.1 of
            // docs\ARCHITECTURE-REVIEW-TERMINAL-EMULATION-2026-08-08.md: the parser recognises
            // structure, it never interprets.
            _state = ParserState.NorskDataParam;
            ClearParams();
        }
        else if (b >= 0x20 && b <= 0x2F)
        {
            // Intermediate byte
            if (_intermediateCount < MaxIntermediates)
            {
                _intermediateBuffer[_intermediateCount++] = b;
            }
            _state = ParserState.EscapeIntermediate;
        }
        else if (b >= 0x30 && b <= 0x7E)
        {
            // Final byte - dispatch escape sequence
            FinalByte = b;
            OnEscapeDispatch?.Invoke(this);
            _state = ParserState.Ground;
        }
        else if (b == 0x1B)
        {
            // ESC in ESC, restart - stay in Escape state
            ClearParams();
            // Stay in Escape state to process the next byte
        }
        else if (b < 0x20)
        {
            // C0 control after ESC: execute it, then END the sequence.
            //
            // A SUBSCRIBER CAN STILL TELL ESC ENQ FROM A BARE ENQ, and that is the whole subtlety
            // here. State is not updated until after OnExecute returns, so a handler reading
            // Parser.State sees Escape for ESC ENQ and Ground for a lone one. TDV2200's
            // identification reply relies on exactly that.
            //
            // AN EARLIER VERSION OF THIS STAYED IN ESCAPE, on the reasoning that ECMA-48 says a C0
            // arriving mid-sequence is executed and the sequence continues. That was wrong for the
            // terminals this program emulates, and real data proved it: every gnuplot Tektronix
            // stream opens with ESC FF - the Tek ERASE SCREEN command, where the C0 IS the end of
            // the command - and staying in Escape made the GS and coordinate bytes that followed
            // get eaten as intermediates and a final. The first vector of every plot vanished.
            //
            // The tell was there before the data was: breaking the change on purpose showed the
            // ESC ENQ test passing either way, which meant the change bought nothing it was
            // adopted for. See ControlInsideEscapeTests.
            OnExecute?.Invoke(b);
            _state = ParserState.Ground;
        }
        else
        {
            // Invalid byte in Escape state - reset to Ground
            _state = ParserState.Ground;
        }
    }

    /// <summary>
    /// Collects a Norsk Data graphics sequence: <c>ESC " params final</c>.
    ///
    /// Shaped like CSI on purpose - digits accumulate, <c>;</c> starts the next parameter, a byte
    /// in 0x40..0x7E ends it - because that is what the sequences actually look like
    /// (<c>ESC "13;10l</c>, <c>ESC "5d</c>, <c>ESC "12;2h</c>). What it is NOT is CSI: it dispatches
    /// through its own event so an ND module can own the meaning and nothing has to guess whether a
    /// given <c>l</c> came from a DEC mode reset or an ND graphics command.
    /// </summary>
    private void HandleNorskDataParamState(byte b)
    {
        if (b >= '0' && b <= '9')
        {
            AccumulateParamDigit(b);
        }
        else if (b == ';')
        {
            StartNextParam();
        }
        else if (b >= 0x40 && b <= 0x7E)
        {
            FinalByte = b;
            OnNorskDataDispatch?.Invoke(this);
            _state = ParserState.Ground;
        }
        else if (b == 0x1B)
        {
            // ESC restarts, same as everywhere else - a truncated sequence must not swallow the
            // one that follows it.
            _state = ParserState.Escape;
            ClearParams();
        }
        else if (b < 0x20)
        {
            // C0 controls still execute mid-sequence. A host is entitled to send CR inside one.
            OnExecute?.Invoke(b);
        }
        else
        {
            // Anything else is not part of this grammar; drop back rather than collect rubbish.
            _state = ParserState.Ground;
        }
    }

    private void HandleEscapeIntermediateState(byte b)
    {
        if (b >= 0x20 && b <= 0x2F)
        {
            // Additional intermediate byte
            if (_intermediateCount < MaxIntermediates)
            {
                _intermediateBuffer[_intermediateCount++] = b;
            }
        }
        else if (b >= 0x30 && b <= 0x7E)
        {
            // Final byte
            FinalByte = b;
            OnEscapeDispatch?.Invoke(this);
            _state = ParserState.Ground;
        }
        else if (b == 0x1B)
        {
            // ESC, restart
            _state = ParserState.Escape;
            ClearParams();
        }
        else if (b < 0x20)
        {
            // C0 control
            OnExecute?.Invoke(b);
            _state = ParserState.Ground;
        }
        else
        {
            // Invalid, return to ground
            _state = ParserState.Ground;
        }
    }

    private void HandleCsiState(byte b)
    {
        // The private-marker range is 0x3C to 0x3F - '<', '=', '>' and '?' - and nothing else.
        //
        // '!' USED TO BE IN THIS LIST AND IS NOT ONE. 0x21 is an INTERMEDIATE byte, so "CSI ! p"
        // is a final 'p' with one intermediate, which is how DECSTR is written. Recording the '!'
        // as a marker instead left the sequence with no intermediates at all, and the soft
        // terminal reset matched nothing and did nothing - see SoftReset. Nothing else in this
        // repository ever read a '!' marker, which is why it went unnoticed for so long.
        if (_state == ParserState.CsiEntry && (b == '?' || b == '>' || b == '<' || b == '='))
        {
            _private = b;
            _state = ParserState.CsiParam;
        }
        else if (_state == ParserState.CsiIntermediate)
        {
            // Already in intermediate state - handle additional intermediates or final byte
            if (b >= 0x20 && b <= 0x2F)
            {
                // Additional intermediate byte
                if (_intermediateCount < MaxIntermediates)
                {
                    _intermediateBuffer[_intermediateCount++] = b;
                }
                // Stay in CsiIntermediate state
            }
            else if ((b >= 0x40 && b <= 0x7E) || b == '<' || b == '>' || b == '=')
            {
                // Final byte
                FinalByte = b;
                OnCsiDispatch?.Invoke(this);
                _state = ParserState.Ground;
            }
            else if (b == 0x1B)
            {
                // ESC, restart
                _state = ParserState.Escape;
                ClearParams();
            }
            else if (b < 0x20)
            {
                // C0 control
                OnExecute?.Invoke(b);
                _state = ParserState.Ground;
            }
            else
            {
                // A parameter byte after an intermediate is not legal CSI. The rest of the
                // sequence is swallowed rather than printed - see ParserState.CsiIgnore.
                _state = ParserState.CsiIgnore;
            }
        }
        else if (b >= '0' && b <= '9')
        {
            // Parameter digit
            AccumulateParamDigit(b);
            _state = ParserState.CsiParam;
        }
        else if (b == ';')
        {
            // Parameter separator
            StartNextParam();
            _state = ParserState.CsiParam;
        }
        else if (b == ':')
        {
            // Sub-parameter separator (SGR 38:2::R:G:B and friends)
            StartNextSubParam();
            _state = ParserState.CsiParam;
        }
        else if (b >= 0x20 && b <= 0x2F)
        {
            // Intermediate byte
            CollectIntermediate(b);
            _state = ParserState.CsiIntermediate;
        }
        else if ((b >= 0x40 && b <= 0x7E) || b == '<' || b == '>' || b == '=')
        {
            // Final byte (standard range 0x40-0x7E plus TDV-specific <, >, =)
            FinalByte = b;
            OnCsiDispatch?.Invoke(this);
            _state = ParserState.Ground;
        }
        else if (b == 0x1B)
        {
            // ESC, restart
            _state = ParserState.Escape;
            ClearParams();
        }
        else if (b < 0x20)
        {
            // C0 control
            OnExecute?.Invoke(b);
        }
        else
        {
            // Anything else here is not a legal CSI byte, so the rest of the sequence is swallowed
            // rather than printed - see ParserState.CsiIgnore.
            _state = ParserState.CsiIgnore;
        }
    }

    /// <summary>
    /// Swallows the rest of a CSI sequence that has already gone wrong.
    /// </summary>
    /// <remarks>
    /// C0 controls still act, which is what the DEC state diagram does - a line feed in the middle
    /// of a broken sequence still feeds a line. Everything from space to '?' is dropped, and the
    /// final byte ends the sequence WITHOUT dispatching it. ESC starts a new sequence, and CAN and
    /// SUB are handled before this is ever reached.
    /// </remarks>
    private void HandleCsiIgnoreState(byte b)
    {
        if (b == 0x1B)
        {
            _state = ParserState.Escape;
            ClearParams();
            return;
        }

        if (b < 0x20)
        {
            OnExecute?.Invoke(b);
            return;
        }

        if (b >= 0x40 && b <= 0x7E)
        {
            // The final byte ends the ruined sequence. Nothing is dispatched.
            ClearParams();
            _state = ParserState.Ground;
            return;
        }

        // 0x20..0x3F - still part of the sequence being thrown away.
    }

    private void HandleOscState(byte b)
    {
        if (b == 0x07 || b == 0x9C) // BEL or ST (String Terminator)
        {
            var str = _stringBuffer != null ? _stringBuffer.AsSpan(0, _stringLength) : ReadOnlySpan<byte>.Empty;
            OnOscDispatch?.Invoke(str);
            _stringLength = 0;
            _state = ParserState.Ground;
        }
        else
        {
            // Accumulate OSC string. ESC never reaches here: ProcessByte intercepts it
            // for string states and resolves ESC \ as ST (see HandleStringTerminatorEscape).
            AppendToStringBuffer(b);
        }
    }

    /// <summary>
    /// Whether a state is a string sequence, i.e. one terminated by ST.
    /// </summary>
    private static bool IsStringState(ParserState state)
    {
        return state == ParserState.OscString
            || state == ParserState.DcsPassthrough
            || state == ParserState.ApcString
            || state == ParserState.PmString
            || state == ParserState.SosString;
    }

    /// <summary>
    /// Resolves an ESC that arrived inside a string sequence, now that the following byte
    /// is known. <c>ESC \</c> is ST and ends the string normally; anything else means the
    /// host abandoned the string, so it is discarded and the byte is reprocessed as the
    /// start of a fresh escape sequence.
    /// </summary>
    /// <param name="b">
    /// The byte that followed the ESC.
    /// </param>
    private void HandleStringTerminatorEscape(byte b)
    {
        var stringState = _state;

        if (b == 0x5C) // '\' — ST
        {
            FinishStringSequence(stringState);
            _state = ParserState.Ground;
            return;
        }

        // Not ST: abandon the string without dispatching it, then let the byte start a
        // new escape sequence (this is the recovery path the old unconditional reset gave
        // us, kept intact for malformed streams).
        AbandonStringSequence(stringState);
        _state = ParserState.Escape;
        ClearParams();
        ProcessByte(b);
    }

    /// <summary>
    /// Dispatches a completed string sequence to its handler.
    /// </summary>
    private void FinishStringSequence(ParserState stringState)
    {
        if (stringState == ParserState.OscString)
        {
            OnOscDispatch?.Invoke(new ReadOnlySpan<byte>(_stringBuffer, 0, _stringLength));
            _stringLength = 0;
        }
        else if (stringState == ParserState.DcsPassthrough)
        {
            FlushDcsPayload();
            OnDcsUnhook?.Invoke();
        }
        // APC/PM/SOS have no handlers yet; their payload is simply dropped.
        _stringLength = 0;
    }

    /// <summary>
    /// Throws away a string sequence the host abandoned mid-way.
    /// </summary>
    private void AbandonStringSequence(ParserState stringState)
    {
        if (stringState == ParserState.DcsPassthrough)
        {
            // The hook already fired, so the consumer must be told the payload ended even
            // though it was truncated - otherwise it stays "in a DCS" forever.
            FlushDcsPayload();
            OnDcsUnhook?.Invoke();
        }
        _stringLength = 0;
    }

    /// <summary>
    /// Hands any buffered DCS payload to <see cref="OnDcsPut"/> and empties the buffer.
    /// </summary>
    private void FlushDcsPayload()
    {
        if (_stringLength > 0 && _stringBuffer != null)
        {
            OnDcsPut?.Invoke(new ReadOnlySpan<byte>(_stringBuffer, 0, _stringLength));
            _stringLength = 0;
        }
    }

    /// <summary>
    /// DCS: <c>DCS P1;P2 ... I F</c> payload <c>ST</c>.
    ///
    /// Was a stub that discarded parameters, had no case for DcsParam at all (so any
    /// parameterised DCS wedged the parser), never raised <see cref="OnDcsPut"/>, and could
    /// only be ended by a bare 0x9C. That made Sixel (<c>DCS q</c>), ReGIS (<c>DCS p</c>)
    /// and DECUDK unusable. Payload is now streamed to OnDcsPut in chunks as it arrives, so
    /// an image never has to be buffered whole.
    /// </summary>
    private void HandleDcsState(byte b)
    {
        switch (_state)
        {
            case ParserState.DcsEntry:
            case ParserState.DcsParam:
                if (b >= '0' && b <= '9')
                {
                    _state = ParserState.DcsParam;
                    AccumulateParamDigit(b);
                    return;
                }
                if (b == ';')
                {
                    _state = ParserState.DcsParam;
                    StartNextParam();
                    return;
                }
                if (b == ':')
                {
                    // Sub-parameter separator: recorded so the value is not silently merged
                    // with the previous one (see AccumulateParamDigit / SubParameterMarker).
                    _state = ParserState.DcsParam;
                    StartNextSubParam();
                    return;
                }
                if (b == '?' || b == '>' || b == '!' || b == '=')
                {
                    // Private marker, only valid before any parameter digits
                    if (_paramCount == 0) _private = b;
                    return;
                }
                if (b >= 0x20 && b <= 0x2F)
                {
                    _state = ParserState.DcsIntermediate;
                    CollectIntermediate(b);
                    return;
                }
                if (b >= 0x40 && b <= 0x7E)
                {
                    HookDcs(b);
                    return;
                }
                // Anything else is malformed - drop the sequence
                _state = ParserState.Ground;
                return;

            case ParserState.DcsIntermediate:
                if (b >= 0x20 && b <= 0x2F)
                {
                    CollectIntermediate(b);
                    return;
                }
                if (b >= 0x40 && b <= 0x7E)
                {
                    HookDcs(b);
                    return;
                }
                _state = ParserState.Ground;
                return;

            case ParserState.DcsPassthrough:
                if (b == 0x9C) // 8-bit ST
                {
                    FlushDcsPayload();
                    OnDcsUnhook?.Invoke();
                    _state = ParserState.Ground;
                    return;
                }
                // Everything else is payload. Buffered and flushed in chunks rather than
                // per byte, so a large Sixel image is a handful of calls, not thousands.
                AppendToStringBuffer(b);
                if (_stringLength >= DcsChunkSize)
                {
                    FlushDcsPayload();
                }
                return;

            default:
                _state = ParserState.Ground;
                return;
        }
    }

    /// <summary>
    /// Fires <see cref="OnDcsHook"/> for a completed DCS introducer and enters passthrough.
    /// </summary>
    private void HookDcs(byte finalByte)
    {
        FinalByte = finalByte;
        _stringLength = 0;
        _state = ParserState.DcsPassthrough;
        OnDcsHook?.Invoke(this);
    }

    // ─────────────────────────────────────────────────────────────
    // Parameter / intermediate accumulation, shared by CSI and DCS so the two
    // cannot drift apart in what they accept.
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Adds a decimal digit to the parameter currently being built.
    /// </summary>
    private void AccumulateParamDigit(byte digit)
    {
        if (_paramCount == 0)
        {
            _params[0] = 0;
            _paramIsSub[0] = false;
            _paramCount = 1;
        }

        // Saturate rather than overflow into a negative value on a hostile stream.
        int value = _params[_paramCount - 1];
        if (value <= (int.MaxValue - 9) / 10)
        {
            _params[_paramCount - 1] = value * 10 + (digit - '0');
        }
    }

    /// <summary>
    /// Starts a new parameter after ';'.
    /// </summary>
    private void StartNextParam()
    {
        if (_paramCount == 0)
        {
            // A leading ';' means an omitted first parameter
            _params[0] = 0;
            _paramIsSub[0] = false;
            _paramCount = 1;
        }

        if (_paramCount < MaxParams)
        {
            _params[_paramCount] = 0;
            _paramIsSub[_paramCount] = false;
            _paramCount++;
        }
    }

    /// <summary>
    /// Starts a new SUB-parameter after ':'. Sub-parameters belong to the preceding
    /// parameter: <c>SGR 38:2::255:0:0</c> is one colour instruction, not six independent
    /// ones. Previously ':' (0x3A) matched nothing and silently killed the whole sequence,
    /// so the colon form of SGR - which is the ITU-T T.416 form modern terminals emit -
    /// was dropped entirely.
    /// </summary>
    private void StartNextSubParam()
    {
        if (_paramCount == 0)
        {
            _params[0] = 0;
            _paramIsSub[0] = false;
            _paramCount = 1;
        }

        if (_paramCount < MaxParams)
        {
            _params[_paramCount] = 0;
            _paramIsSub[_paramCount] = true;
            _paramCount++;
        }
    }

    /// <summary>
    /// Records an intermediate byte, ignoring any past the supported maximum.
    /// </summary>
    private void CollectIntermediate(byte b)
    {
        if (_intermediateCount < MaxIntermediates)
        {
            _intermediateBuffer[_intermediateCount++] = b;
        }
    }

    /// <summary>
    /// Appends one byte to the pooled string/payload buffer, growing it if needed.
    /// </summary>
    private void AppendToStringBuffer(byte b)
    {
        if (_stringBuffer == null)
        {
            _stringBuffer = ArrayPool<byte>.Shared.Rent(InitialStringBufferSize);
        }
        else if (_stringLength >= _stringBuffer.Length)
        {
            var newBuffer = ArrayPool<byte>.Shared.Rent(_stringBuffer.Length * 2);
            Array.Copy(_stringBuffer, newBuffer, _stringLength);
            ArrayPool<byte>.Shared.Return(_stringBuffer);
            _stringBuffer = newBuffer;
        }
        _stringBuffer[_stringLength++] = b;
    }

    private void ClearParams()
    {
        Array.Clear(_params, 0, _paramCount);
        Array.Clear(_paramIsSub, 0, _paramCount);
        _paramCount = 0;
        _intermediateCount = 0;
        _private = 0;
        FinalByte = 0;
    }

    /// <summary>
    /// Gets a parameter value with a default if not present
    /// </summary>
    public int GetParam(int index, int defaultValue = 0)
    {
        if (index < 0 || index >= _paramCount)
            return defaultValue;
        var value = _params[index];
        return value == 0 ? defaultValue : value;
    }
}

