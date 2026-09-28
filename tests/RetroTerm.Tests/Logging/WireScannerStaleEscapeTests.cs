using System;
using System.Collections.Generic;
using System.Text;
using RetroTerm.Core.Logging;
using Xunit;

namespace RetroTerm.Tests.Logging;

/// <summary>
/// An escape that is never finished must not swallow whatever is typed next.
/// </summary>
/// <remarks>
/// <para><b>The observed defect, on 20 August 2026</b></para>
/// A lone <c>ESC</c> was sent to wake a SINTRAN line. Fourteen seconds later Ronny typed his login,
/// and the trace reported <c>ESC S - Unrecognised sequence</c> while the raw block beside it plainly
/// carried <c>53 59 53 54 45 4D 0D 0D</c>, an ordinary "SYSTEM" and two carriage returns. Nothing
/// wrong reached the host. The keystroke was fine; the INSTRUMENT was wrong.
/// <para><b>Why that matters more than an ordinary defect</b></para>
/// The trace is what every other case in the by-hand pass is judged with. A tool that invents an
/// unrecognised sequence out of a normal keypress can send a whole afternoon chasing a defect that
/// was never there, and it can just as easily hide a real one behind a plausible-looking entry.
/// <para><b>Why the state exists at all, and must not simply be removed</b></para>
/// A genuine sequence really does arrive split across two network reads - the sample log that this
/// scanner was written from shows <c>1B 5B</c> ending one packet and <c>36 3B 33 48</c> starting the
/// next. Throwing the state away at every block boundary would break that, which is a worse trade.
/// The distinction is TIME: a split packet continues in milliseconds, and nothing continues after
/// fourteen seconds.
/// <para><b>Why the clock is injected</b></para>
/// So the gap can be stated rather than waited for. Sleeping a test into passing is forbidden here
/// and would prove nothing anyway - a sleep asserts that the machine was busy, not that the scanner
/// did the right thing.
/// </remarks>
public class WireScannerStaleEscapeTests
{
    private readonly List<ProtocolTraceEntry> _emitted = new();

    /// <summary>
    /// The fake clock's current reading.
    /// </summary>
    private DateTime _now = new DateTime(2026, 8, 20, 12, 0, 0, DateTimeKind.Local);

    /// <summary>
    /// Builds a scanner reading the fake clock.
    /// </summary>
    /// <returns>
    /// The scanner.
    /// </returns>
    private WireScanner CreateScanner()
    {
        return new WireScanner(TraceDirection.Tx, entry => _emitted.Add(entry), () => _now);
    }

    /// <summary>
    /// Builds a RECEIVE-side scanner reading the same fake clock.
    /// </summary>
    /// <returns>
    /// The scanner.
    /// </returns>
    /// <remarks>
    /// The direction matters as of 2 September 2026. A host's sequence really does arrive split
    /// across two network reads, so the RX scanner keeps a half-finished sequence between feeds and
    /// judges it by the timeout. The TX scanner does NOT: <c>TerminalSession.SendBytesAsync</c> is
    /// the single TX feed point and feeds one whole send per call, so anything still pending when a
    /// TX feed ends is a stale escape rather than a sequence in progress.
    /// <para>
    /// Two tests below were written against a Tx scanner while describing host traffic - DECKPAM,
    /// and a CUP split across two packets. Both are RX scenarios and now say so.
    /// </para>
    /// </remarks>
    private WireScanner CreateReceiveScanner()
    {
        return new WireScanner(TraceDirection.Rx, entry => _emitted.Add(entry), () => _now);
    }

    /// <summary>
    /// Joins everything the scanner rendered, so a test can ask what the trace would have shown.
    /// </summary>
    /// <returns>
    /// One line per entry.
    /// </returns>
    private string RenderedTrace()
    {
        var text = new StringBuilder();
        for (int i = 0; i < _emitted.Count; i++)
        {
            text.Append(_emitted[i].Mnemonic).Append(' ').Append(_emitted[i].Rendered).Append('\n');
        }

        return text.ToString();
    }

    [Fact]
    public void AKeystrokeTypedLongAfterALoneEscapeIsStillAKeystroke()
    {
        // THE OBSERVED CASE, byte for byte. ESC on its own to wake the line, a long human pause,
        // then a login typed at the prompt.
        var scanner = CreateScanner();

        scanner.Feed(new ReadOnlySpan<byte>(new byte[] { 0x1B }));

        _now = _now.AddSeconds(14);
        scanner.Feed(new ReadOnlySpan<byte>(Encoding.ASCII.GetBytes("SYSTEM\r\r")));

        string trace = RenderedTrace();

        Assert.DoesNotContain("ESC S", trace);
        Assert.Contains("SYSTEM", trace);
    }

    [Fact]
    public void TheAbandonedEscapeIsReportedRatherThanQuietlyDropped()
    {
        // Discarding it silently would be a different kind of lie: the byte WAS on the wire and the
        // trace has to account for every byte, or it stops being usable as evidence.
        var scanner = CreateScanner();

        scanner.Feed(new ReadOnlySpan<byte>(new byte[] { 0x1B }));

        _now = _now.AddSeconds(14);
        scanner.Feed(new ReadOnlySpan<byte>(Encoding.ASCII.GetBytes("SYSTEM\r\r")));

        // The ESC must appear as its own entry, and must not claim to be a known sequence.
        bool foundAbandonedEscape = false;
        for (int i = 0; i < _emitted.Count; i++)
        {
            if (_emitted[i].Bytes.Length == 1 && _emitted[i].Bytes[0] == 0x1B)
            {
                foundAbandonedEscape = true;
                Assert.False(_emitted[i].IsKnown);
            }
        }

        Assert.True(foundAbandonedEscape,
            "the lone ESC vanished from the trace entirely - every byte on the wire has to be "
            + "accounted for, or the trace cannot be used as evidence");
    }

    [Fact]
    public void ASequenceSplitAcrossTwoNetworkReadsIsStillOneSequence()
    {
        // THE REASON THE STATE EXISTS, and the thing the fix must not break. Taken from the sample
        // log the scanner was written from: ESC [ ends one packet, 6 ; 3 H starts the next.
        // RECEIVE side: this is a HOST sequence arriving split, which is the only direction where
        // that happens.
        var scanner = CreateReceiveScanner();

        scanner.Feed(new ReadOnlySpan<byte>(new byte[] { 0x1B, (byte)'[' }));

        // A few milliseconds later, which is what a split packet actually looks like.
        _now = _now.AddMilliseconds(4);
        scanner.Feed(new ReadOnlySpan<byte>(Encoding.ASCII.GetBytes("6;3H")));

        Assert.Single(_emitted);
        Assert.Equal("CUP", _emitted[0].Mnemonic);
    }

    [Fact]
    public void AGapInsideOneBlockIsNotPossibleSoNothingIsSplitThere()
    {
        // Bytes inside a single block arrived together by definition, so no amount of clock movement
        // between Feed calls may break a sequence that was whole when it was handed over.
        var scanner = CreateScanner();

        _now = _now.AddHours(3);
        scanner.Feed(new ReadOnlySpan<byte>(new byte[] { 0x1B, (byte)'[', (byte)'0', (byte)'m' }));

        Assert.Single(_emitted);
        Assert.Equal("SGR", _emitted[0].Mnemonic);
    }

    [Fact]
    public void AnEscapeFinishedPromptlyIsUntouched()
    {
        // The ordinary case, pinned so the timeout cannot start eating live sequences. RECEIVE
        // side: DECKPAM is something a host sends TO the terminal, and the two bytes arriving in
        // separate feeds is a split packet.
        var scanner = CreateReceiveScanner();

        scanner.Feed(new ReadOnlySpan<byte>(new byte[] { 0x1B }));

        _now = _now.AddMilliseconds(20);
        scanner.Feed(new ReadOnlySpan<byte>(new byte[] { (byte)'=' }));

        Assert.Single(_emitted);
        Assert.Equal("DECKPAM", _emitted[0].Mnemonic);
    }

    [Fact]
    public void AHalfTypedCsiIsAbandonedToo()
    {
        // Not only the bare ESC. A CSI that stops half way leaves the scanner just as stuck, and the
        // next keystroke would be eaten as a parameter byte instead of a final one.
        var scanner = CreateScanner();

        scanner.Feed(new ReadOnlySpan<byte>(new byte[] { 0x1B, (byte)'[', (byte)'1' }));

        _now = _now.AddSeconds(30);
        scanner.Feed(new ReadOnlySpan<byte>(Encoding.ASCII.GetBytes("SYSTEM\r\r")));

        Assert.Contains("SYSTEM", RenderedTrace());
    }
}
