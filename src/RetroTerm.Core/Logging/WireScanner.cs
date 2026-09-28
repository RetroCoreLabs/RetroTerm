using System;

namespace RetroTerm.Core.Logging;

/// <summary>
/// A small, stateful byte-stream scanner that splits a raw terminal stream into individual
/// escape sequences, control characters and printable text runs for the protocol trace.
/// </summary>
/// <remarks>
/// <para>
/// This is intentionally SEPARATE from <see cref="RetroTerm.Core.Terminal.Parsing.EscapeSequenceParser"/>. The parser
/// drives emulation and must stay on the zero-allocation hot path; this scanner exists only to
/// describe what was on the wire, runs only when tracing is switched on, and is allowed to
/// allocate strings.
/// </para>
/// <para>
/// State is retained between <see cref="Feed"/> calls so a sequence split across two network
/// reads (which the sample log shows happening - <c>...1B-5B</c> at the end of one packet,
/// <c>36-3B-33-48</c> at the start of the next) is still decoded as one item instead of two
/// pieces of garbage.
/// </para>
/// </remarks>
public sealed class WireScanner
{
    private enum ScanState
    {
        Ground,
        Escape,
        CsiEntry,
        OscString,
        DcsString,
        DleCoordinates
    }

    private const int MaxSequenceBytes = 256;
    private const int MaxTextRun = 512;

    /// <summary>
    /// How long a half-finished sequence may wait for its next byte, in milliseconds.
    /// </summary>
    /// <remarks>
    /// <para><b>Why there is a limit at all</b></para>
    /// On 20 August 2026 a lone ESC, sent to wake a SINTRAN line, sat in the escape state for
    /// FOURTEEN SECONDS and then took the first letter of the login typed afterwards as its final
    /// byte. The trace reported an unrecognised "ESC S" while the raw block beside it plainly
    /// carried an ordinary "SYSTEM". Nothing wrong went to the host - but this scanner is the
    /// instrument every other case is judged with, and it invented a defect out of a keypress.
    /// <para><b>Why the state is not simply thrown away at each block instead</b></para>
    /// A real sequence does arrive split across two network reads. The sample log this scanner was
    /// written from shows 1B 5B ending one packet and 36 3B 33 48 starting the next, and decoding
    /// that as two pieces of garbage would be a worse trade than the defect being fixed.
    /// <para><b>Why one second</b></para>
    /// The two cases are separated by time, not by anything in the bytes. A split packet continues
    /// in single-digit milliseconds; a person pausing between keystrokes takes hundreds. One second
    /// is a hundred times longer than the case being protected and fourteen times shorter than the
    /// case observed, so nothing has to be finely judged for it to be right.
    /// </remarks>
    private const int StaleSequenceMilliseconds = 1000;

    private readonly TraceDirection _direction;
    private readonly Action<ProtocolTraceEntry> _emit;

    // Bytes of the sequence currently being accumulated (including the leading ESC).
    private readonly byte[] _seq = new byte[MaxSequenceBytes];
    private int _seqLength;

    // Bytes of the printable run currently being accumulated.
    private readonly byte[] _text = new byte[MaxTextRun];
    private int _textLength;

    // CSI decomposition.
    private readonly int[] _params = new int[32];
    private int _paramCount;
    private int _currentParam;
    private bool _hasCurrentParam;
    private byte _privateMarker;
    private readonly byte[] _intermediates = new byte[8];
    private int _intermediateCount;

    private ScanState _state = ScanState.Ground;

    /// <summary>
    /// Reads the current time, or null to use the system clock.
    /// </summary>
    private readonly Func<DateTime>? _clock;

    /// <summary>
    /// When the last block of bytes arrived, or null before any has.
    /// </summary>
    private DateTime? _lastFeedAt;

    /// <summary>
    /// Creates a scanner for one direction of the stream.
    /// </summary>
    /// <param name="direction">
    /// Direction the scanned bytes travel.
    /// </param>
    /// <param name="emit">
    /// Callback invoked for every decoded item.
    /// </param>
    /// <param name="clock">
    /// Reads the current time. Supply one only in tests; live code leaves it null and gets the
    /// system clock.
    /// </param>
    public WireScanner(TraceDirection direction, Action<ProtocolTraceEntry> emit,
        Func<DateTime>? clock = null)
    {
        _direction = direction;
        _emit = emit ?? throw new ArgumentNullException(nameof(emit));
        _clock = clock;
    }

    /// <summary>
    /// Discards any partially accumulated sequence or text run.
    /// </summary>
    public void Reset()
    {
        _state = ScanState.Ground;
        _seqLength = 0;
        _textLength = 0;
        ResetCsiState();
    }

    /// <summary>
    /// Feeds a block of bytes through the scanner, emitting one entry per decoded item.
    /// </summary>
    /// <param name="data">
    /// The bytes to scan.
    /// </param>
    public void Feed(ReadOnlySpan<byte> data)
    {
        // ABANDON A SEQUENCE NOTHING IS COMING BACK FOR, before the new bytes are looked at.
        // Only at a block boundary: bytes inside one block arrived together by definition, so
        // there is no gap in the middle of one for a timeout to be measuring.
        DateTime now = _clock != null ? _clock() : DateTime.Now;
        if (_state != ScanState.Ground && _lastFeedAt != null
            && (now - _lastFeedAt.Value).TotalMilliseconds >= StaleSequenceMilliseconds)
        {
            AbandonPendingSequence(now);
        }

        _lastFeedAt = now;

        for (int i = 0; i < data.Length; i++)
        {
            Step(data[i]);
        }

        // Flush any trailing printable run so short packets (a single echoed keystroke)
        // show up immediately rather than waiting for the next control byte.
        FlushText();

        // ON TX, NEVER CARRY A HALF-FINISHED SEQUENCE INTO THE NEXT FEED.
        //
        // The timeout above exists because a HOST's sequence genuinely arrives split across two
        // network reads, so RX has to keep the state and judge it by time. Nothing of the kind
        // happens on the way out. TerminalSession.SendBytesAsync is "THE single TX feed point",
        // and it feeds one whole send per call: a keypress maps to a COMPLETE byte string, and an
        // emulator reply is written whole. So a sequence still pending when a TX feed ends is by
        // construction never going to be completed by the next one, whatever the gap.
        //
        // Leaving it pending is what let a lone ESC - the one sent to wake a SINTRAN line - eat
        // the next keystroke and render it as "ESC final 0x18" while the raw block beside it read
        // one byte. The timeout could not catch that case because the two presses were a fraction
        // of a second apart, well inside the one second it waits. Reproduced 2 September 2026 and
        // pinned by WireScannerGluedKeystrokeTests.
        if (_direction == TraceDirection.Tx && _state != ScanState.Ground)
        {
            AbandonPendingSequence(now);
        }
    }

    /// <summary>
    /// Emits whatever half-finished sequence is being held, and returns to ground.
    /// </summary>
    /// <param name="now">
    /// The time to stamp the entry with.
    /// </param>
    /// <remarks>
    /// <para><b>Reported, never silently dropped</b></para>
    /// Discarding the bytes would be a different kind of lie. They WERE on the wire, and a trace
    /// that quietly loses bytes cannot be used as evidence about the ones it kept. So the partial
    /// sequence is emitted as itself, marked not known, saying how long it waited.
    /// </remarks>
    private void AbandonPendingSequence(DateTime now)
    {
        if (_seqLength == 0)
        {
            ReturnToGround();
            return;
        }

        int waitedMilliseconds = _lastFeedAt != null
            ? (int)(now - _lastFeedAt.Value).TotalMilliseconds
            : 0;

        _emit(new ProtocolTraceEntry
        {
            Timestamp = now,
            Direction = _direction,
            Kind = TraceKind.Sequence,
            Bytes = SequenceBytes(),
            Mnemonic = "ESC",
            Name = "Incomplete sequence, abandoned",
            Arguments = "nothing followed for " + waitedMilliseconds + " ms",
            Rendered = EscapeSequenceDecoder.RenderPrintable(SequenceBytes()),
            IsKnown = false
        });

        ReturnToGround();
    }

    private void Step(byte b)
    {
        switch (_state)
        {
            case ScanState.Ground:
                StepGround(b);
                break;

            case ScanState.Escape:
                StepEscape(b);
                break;

            case ScanState.CsiEntry:
                StepCsi(b);
                break;

            case ScanState.OscString:
            case ScanState.DcsString:
                StepString(b);
                break;

            case ScanState.DleCoordinates:
                StepDle(b);
                break;
        }
    }

    /// <summary>
    /// Collects the two coordinate bytes that follow a TDV DLE (0x10) introducer.
    /// </summary>
    /// <remarks>
    /// <para>
    /// TDV terminals address the cursor with <c>DLE row col</c> rather than CSI.
    /// Without this state the monitor reported 0x10 as an unknown control and then rendered the
    /// two coordinate bytes as bogus TEXT - which is precisely what made this hard to diagnose.
    /// </para>
    /// <para>
    /// Coordinates are masked (row 5-bit, column 7-bit) to match
    /// TDV2115CompatibilityHandler.HandleDLEByte, so both the unbiased and the 0x7F-biased
    /// encodings decode identically.
    /// </para>
    /// <para>
    /// CAVEAT: the tracer has no terminal-type context, so 0x10 is decoded this way for every
    /// session. In a non-TDV stream a bare DLE would be shown as a cursor address. That is
    /// acceptable for a diagnostic view - DLE carries no other meaning in the terminals this
    /// application emulates.
    /// </para>
    /// </remarks>
    private void StepDle(byte b)
    {
        Accumulate(b);

        if (_seqLength < 3)
            return;

        // _seq is [0x10, rowByte, colByte]. Report 1-based coordinates to match the CUP
        // sequences the same programs emit in ANSI mode, so the two are directly comparable.
        int row = (_seq[1] & 0b11111) + 1;
        int col = (_seq[2] & 0b1111111) + 1;

        _emit(new ProtocolTraceEntry
        {
            Timestamp = DateTime.Now,
            Direction = _direction,
            Kind = TraceKind.Sequence,
            Bytes = SequenceBytes(),
            Mnemonic = "DLE",
            Name = "TDV Cursor Address",
            Arguments = $"row={row} col={col}",
            // Wire form: DLE plus the two raw coordinate bytes, so the hex column and the
            // decode can be checked against each other at a glance.
            Rendered = $"DLE {_seq[1]:X2} {_seq[2]:X2}",
            IsKnown = true
        });

        ReturnToGround();
    }

    private void StepGround(byte b)
    {
        if (b == 0x1B)
        {
            FlushText();
            _state = ScanState.Escape;
            _seqLength = 0;
            Accumulate(b);
            return;
        }

        // DLE introduces a 3-byte TDV cursor address; the next two bytes are binary
        // coordinates and must not be decoded as controls or text.
        if (b == 0x10)
        {
            FlushText();
            _state = ScanState.DleCoordinates;
            _seqLength = 0;
            Accumulate(b);
            return;
        }

        if (b < 0x20 || b == 0x7F)
        {
            FlushText();
            var decoded = EscapeSequenceDecoder.DecodeControl(b);
            EmitDecoded(TraceKind.Control, decoded, new[] { b });
            return;
        }

        // Printable byte: accumulate into the current text run.
        if (_textLength == _text.Length)
            FlushText();

        _text[_textLength++] = b;
    }

    private void StepEscape(byte b)
    {
        Accumulate(b);

        if (b == (byte)'[')
        {
            _state = ScanState.CsiEntry;
            ResetCsiState();
            return;
        }

        if (b == (byte)']')
        {
            _state = ScanState.OscString;
            return;
        }

        if (b == (byte)'P')
        {
            _state = ScanState.DcsString;
            return;
        }

        // Intermediate bytes (0x20-0x2F) may precede the final byte, e.g. ESC ( B.
        if (b >= 0x20 && b <= 0x2F)
        {
            if (_intermediateCount < _intermediates.Length)
                _intermediates[_intermediateCount++] = b;
            return;
        }

        // Anything else is the final byte of a plain escape sequence.
        var decoded = EscapeSequenceDecoder.DecodeEscape(
            new ReadOnlySpan<byte>(_intermediates, 0, _intermediateCount), b);

        EmitDecoded(TraceKind.Sequence, decoded, SequenceBytes());
        ReturnToGround();
    }

    private void StepCsi(byte b)
    {
        Accumulate(b);

        // Private parameter prefix, only valid as the first byte after CSI.
        if (b >= 0x3C && b <= 0x3F && _paramCount == 0 && !_hasCurrentParam && _privateMarker == 0)
        {
            _privateMarker = b;
            return;
        }

        // Numeric parameter digits.
        if (b >= (byte)'0' && b <= (byte)'9')
        {
            _currentParam = (_currentParam * 10) + (b - (byte)'0');
            _hasCurrentParam = true;
            return;
        }

        // Parameter separator.
        if (b == (byte)';')
        {
            PushParam();
            return;
        }

        // Intermediate bytes, e.g. '$' in the DECRQM family.
        if (b >= 0x20 && b <= 0x2F)
        {
            if (_intermediateCount < _intermediates.Length)
                _intermediates[_intermediateCount++] = b;
            return;
        }

        // Final byte in the 0x40-0x7E range terminates the sequence.
        if (b >= 0x40 && b <= 0x7E)
        {
            if (_hasCurrentParam)
                PushParam();

            var decoded = EscapeSequenceDecoder.DecodeCsi(
                _privateMarker,
                new ReadOnlySpan<byte>(_intermediates, 0, _intermediateCount),
                new ReadOnlySpan<int>(_params, 0, _paramCount),
                b);

            EmitDecoded(TraceKind.Sequence, decoded, SequenceBytes());
            ReturnToGround();
            return;
        }

        // A control character inside a CSI aborts it on a real terminal.
        if (b < 0x20)
        {
            EmitRaw(TraceKind.Sequence, "aborted CSI", "Malformed", "control byte inside CSI", SequenceBytes(), isKnown: false);
            ReturnToGround();
            Step(b);
        }
    }

    private void StepString(byte b)
    {
        Accumulate(b);

        // OSC/DCS strings end with BEL or ST (ESC \). The ESC of ST lands here first, then
        // the backslash, so terminating on either byte is sufficient for a trace.
        bool isBel = b == 0x07;
        bool isStTail = b == (byte)'\\' && _seqLength >= 2 && _seq[_seqLength - 2] == 0x1B;

        if (isBel || isStTail)
        {
            var kind = _state == ScanState.OscString ? "OSC" : "DCS";
            var bytes = SequenceBytes();
            var body = bytes.Length > 3
                ? EscapeSequenceDecoder.RenderPrintable(new ReadOnlySpan<byte>(bytes, 2, bytes.Length - 2))
                : string.Empty;

            EmitRaw(TraceKind.StringCommand, kind, kind == "OSC" ? "Operating System Command" : "Device Control String", body, bytes, isKnown: true);
            ReturnToGround();
            return;
        }

        // Guard against an unterminated string eating the whole session.
        if (_seqLength >= MaxSequenceBytes - 1)
        {
            EmitRaw(TraceKind.StringCommand, "OSC/DCS", "Unterminated string", "exceeded byte limit", SequenceBytes(), isKnown: false);
            ReturnToGround();
        }
    }

    private void PushParam()
    {
        if (_paramCount < _params.Length)
            _params[_paramCount++] = _currentParam;

        _currentParam = 0;
        _hasCurrentParam = false;
    }

    private void ResetCsiState()
    {
        _paramCount = 0;
        _currentParam = 0;
        _hasCurrentParam = false;
        _privateMarker = 0;
        _intermediateCount = 0;
    }

    private void ReturnToGround()
    {
        _state = ScanState.Ground;
        _seqLength = 0;
        ResetCsiState();
    }

    private void Accumulate(byte b)
    {
        if (_seqLength < _seq.Length)
            _seq[_seqLength++] = b;
    }

    private byte[] SequenceBytes()
    {
        var result = new byte[_seqLength];
        Array.Copy(_seq, result, _seqLength);
        return result;
    }

    private void FlushText()
    {
        if (_textLength == 0)
            return;

        var bytes = new byte[_textLength];
        Array.Copy(_text, bytes, _textLength);
        _textLength = 0;

        var entry = new ProtocolTraceEntry
        {
            Timestamp = DateTime.Now,
            Direction = _direction,
            Kind = TraceKind.Text,
            Bytes = bytes,
            Mnemonic = "TEXT",
            Name = bytes.Length == 1 ? "1 char" : bytes.Length + " chars",
            Arguments = string.Empty,
            Rendered = EscapeSequenceDecoder.RenderPrintable(bytes),
            IsKnown = true
        };

        _emit(entry);
    }

    private void EmitDecoded(TraceKind kind, DecodedSequence decoded, byte[] bytes)
    {
        var entry = new ProtocolTraceEntry
        {
            Timestamp = DateTime.Now,
            Direction = _direction,
            Kind = kind,
            Bytes = bytes,
            Mnemonic = decoded.Mnemonic,
            Name = decoded.Name,
            Arguments = decoded.Arguments,
            Rendered = decoded.Rendered,
            IsKnown = decoded.IsKnown
        };

        _emit(entry);
    }

    private void EmitRaw(TraceKind kind, string mnemonic, string name, string arguments, byte[] bytes, bool isKnown)
    {
        var entry = new ProtocolTraceEntry
        {
            Timestamp = DateTime.Now,
            Direction = _direction,
            Kind = kind,
            Bytes = bytes,
            Mnemonic = mnemonic,
            Name = name,
            Arguments = arguments,
            Rendered = mnemonic,
            IsKnown = isKnown
        };

        _emit(entry);
    }
}
