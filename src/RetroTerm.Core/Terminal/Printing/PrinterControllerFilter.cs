using System;

namespace RetroTerm.Core.Terminal.Printing;

/// <summary>
/// Routes host bytes to the printer while printer controller mode is on, watching for the one
/// sequence that turns it off.
/// </summary>
/// <remarks>
/// <para><b>What printer controller mode is</b></para>
/// <c>CSI 5 i</c> turns it on. From then until <c>CSI 4 i</c>, everything the host sends goes to
/// the printer and NOTHING reaches the screen - the terminal is a wire. That is how a host printed
/// a page through a terminal, and it is how <c>vaxrgl-lntest.six</c>, a plotter sheet that was
/// never meant for a screen, was meant to arrive.
///
/// <para><b>Note the private marker</b></para>
/// <c>CSI 5 i</c> with NO question mark is printer controller mode. <c>CSI ? 5 i</c> is AUTOPRINT,
/// which prints each line as it scrolls off and leaves the screen working normally. They are
/// different features and the marker is the whole difference - the same trap that made
/// <c>CSI ? 1 ; 1 S</c> get read as scroll-up.
///
/// <para><b>Why this is its own class and not part of the parser</b></para>
/// While the mode is on there is no parsing to do. Bytes are not characters, escape sequences are
/// not executed, and the C1 controls mean nothing: a sixel stream passing through must arrive at
/// the printer byte for byte or the picture is corrupt. So this is a tiny scanner of its own, and
/// the emulator's parser never sees the data at all.
///
/// <para><b>The exit sequence is matched across chunk boundaries</b></para>
/// Host data arrives in whatever sizes the network hands over, so <c>CSI 4 i</c> can be split
/// across two reads. The partial match is held in a five-byte state rather than a buffer, and if
/// the sequence turns out not to be the terminator the held bytes are sent on to the printer -
/// they were print data after all.
/// </remarks>
public sealed class PrinterControllerFilter
{
    /// <summary>
    /// How far into a possible exit sequence the scanner is.
    /// </summary>
    private enum MatchState
    {
        /// <summary>
        /// Not in a candidate sequence.
        /// </summary>
        None = 0,

        /// <summary>
        /// An ESC has arrived.
        /// </summary>
        Escape,

        /// <summary>
        /// ESC [ has arrived, and digits may follow.
        /// </summary>
        Bracket,
    }

    /// <summary>
    /// The longest partial match that can be held: ESC, [, and up to two parameter digits.
    /// </summary>
    private const int MaxHeld = 4;

    private readonly byte[] _held = new byte[MaxHeld];
    private int _heldCount;
    private MatchState _state;
    private int _parameter;
    private bool _hasParameter;

    /// <summary>
    /// Forgets any partial match, so a fresh job does not inherit half a sequence.
    /// </summary>
    public void Reset()
    {
        _heldCount = 0;
        _state = MatchState.None;
        _parameter = 0;
        _hasParameter = false;
    }

    /// <summary>
    /// Feeds a chunk of host data to the printer, stopping at the sequence that ends the mode.
    /// </summary>
    /// <param name="data">
    /// The bytes as received.
    /// </param>
    /// <param name="sink">
    /// Where the print data goes. May be null, in which case the data is discarded - a terminal
    /// with no printer attached swallows the job rather than putting it on the screen.
    /// </param>
    /// <param name="consumed">
    /// How many bytes of <paramref name="data"/> were used. When the mode ended, the rest of the
    /// span is ordinary terminal data and belongs to the parser.
    /// </param>
    /// <returns>
    /// True when the mode is still on, false when <c>CSI 4 i</c> ended it.
    /// </returns>
    public bool Process(ReadOnlySpan<byte> data, IPrintSink? sink, out int consumed)
    {
        int runStart = 0;                 // start of the plain bytes not yet handed to the sink

        for (int i = 0; i < data.Length; i++)
        {
            byte b = data[i];

            switch (_state)
            {
                case MatchState.None:
                    if (b != 0x1B) continue;             // ordinary print data, keep gathering

                    // A candidate starts here. Everything before it is print data.
                    Emit(sink, data.Slice(runStart, i - runStart));
                    runStart = i + 1;
                    Hold(b);
                    _state = MatchState.Escape;
                    continue;

                case MatchState.Escape:
                    if (b == (byte)'[')
                    {
                        Hold(b);
                        _state = MatchState.Bracket;
                        _parameter = 0;
                        _hasParameter = false;
                        continue;
                    }

                    // Not CSI. The ESC and this byte are print data.
                    ReleaseHeld(sink);
                    _state = MatchState.None;
                    if (b == 0x1B)
                    {
                        // An ESC ESC: the second one starts a fresh candidate.
                        Hold(b);
                        _state = MatchState.Escape;
                        runStart = i + 1;
                        continue;
                    }

                    runStart = i;
                    continue;

                case MatchState.Bracket:
                    if (b >= (byte)'0' && b <= (byte)'9' && _heldCount < MaxHeld)
                    {
                        _parameter = _parameter * 10 + (b - (byte)'0');
                        _hasParameter = true;
                        Hold(b);
                        continue;
                    }

                    if (b == (byte)'i' && _hasParameter && _parameter == 4)
                    {
                        // CSI 4 i - the mode ends here and the sequence itself is not printed.
                        _state = MatchState.None;
                        _heldCount = 0;
                        consumed = i + 1;
                        return false;
                    }

                    // Any other CSI is print data: the printer's own protocol may well use escape
                    // sequences, and swallowing them would corrupt the page.
                    ReleaseHeld(sink);
                    _state = MatchState.None;
                    runStart = i;
                    continue;
            }
        }

        // Whatever is left that is not part of a candidate goes to the printer now.
        if (_state == MatchState.None && runStart < data.Length)
        {
            Emit(sink, data.Slice(runStart));
        }

        consumed = data.Length;
        return true;
    }

    /// <summary>
    /// Sends a run of print data, skipping the call when the run is empty.
    /// </summary>
    /// <param name="sink">
    /// Where it goes; may be null.
    /// </param>
    /// <param name="data">
    /// The run.
    /// </param>
    private static void Emit(IPrintSink? sink, ReadOnlySpan<byte> data)
    {
        if (sink != null && data.Length != 0) sink.Write(data);
    }

    /// <summary>
    /// Keeps a byte that may turn out to be part of the exit sequence.
    /// </summary>
    /// <param name="b">
    /// The byte.
    /// </param>
    private void Hold(byte b)
    {
        if (_heldCount < MaxHeld) _held[_heldCount++] = b;
    }

    /// <summary>
    /// Sends the held bytes to the printer, because the candidate turned out to be print data.
    /// </summary>
    /// <param name="sink">
    /// Where it goes; may be null.
    /// </param>
    private void ReleaseHeld(IPrintSink? sink)
    {
        Emit(sink, new ReadOnlySpan<byte>(_held, 0, _heldCount));
        _heldCount = 0;
    }
}
