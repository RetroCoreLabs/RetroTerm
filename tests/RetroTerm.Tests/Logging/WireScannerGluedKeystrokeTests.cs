using System;
using System.Collections.Generic;
using System.Text;
using RetroTerm.Core.Logging;
using Xunit;
using Xunit.Abstractions;

namespace RetroTerm.Tests.Logging;

/// <summary>
/// A keystroke must never be shown glued to an escape that arrived long before it.
/// </summary>
/// <remarks>
/// <para><b>Where this came from</b></para>
/// The run sheet carried a watch item from 20 August 2026: a trace showing "an <c>ESC S</c> where
/// you plainly typed <c>S</c>". It reproduced on 2 September against the live D100. Pressing the
/// RIGHT arrow rendered as
/// <c>TX 1B 18  ESC.  Unrecognised sequence - ESC final 0x18</c>, while the RAW block for the same
/// press read <c>TX 18  BLOCK  1 bytes</c> - one byte on the wire.
///
/// <para><b>Why it matters</b></para>
/// The wire was right, so this is a TRACE-RENDERING fault rather than a terminal one. That makes
/// it worse than it looks rather than better: the trace is the instrument every other case in the
/// manual pass is measured with, and this one sent a session chasing a keyboard fault that did not
/// exist.
/// </remarks>
public class WireScannerGluedKeystrokeTests
{
    private readonly ITestOutputHelper _output;
    private readonly List<ProtocolTraceEntry> _emitted = new();
    private DateTime _now = new DateTime(2026, 9, 2, 0, 50, 0, DateTimeKind.Local);

    public WireScannerGluedKeystrokeTests(ITestOutputHelper output)
    {
        _output = output;
    }

    private WireScanner CreateScanner()
        => new WireScanner(TraceDirection.Tx, entry => _emitted.Add(entry), () => _now);

    private string Rendered()
    {
        var sb = new StringBuilder();
        for (int i = 0; i < _emitted.Count; i++)
        {
            var e = _emitted[i];
            sb.Append(e.ToHex()).Append("  ").Append(e.Mnemonic).Append("  ").Append(e.Name).Append('\n');
        }

        return sb.ToString();
    }

    [Fact]
    public void AnEscapeThatWaitedIsNotGluedToTheNextKeystroke()
    {
        var scanner = CreateScanner();

        // A bare ESC, exactly as terminal_sendraw ESC puts on the wire to wake a SINTRAN line.
        scanner.Feed(new byte[] { 0x1B });

        // Nothing follows for a long time. The operator then presses the RIGHT arrow, which on a
        // TDV is the single byte CAN.
        _now = _now.AddSeconds(18);
        scanner.Feed(new byte[] { 0x18 });

        _output.WriteLine(Rendered());

        // The cursor-right byte must appear as ITSELF. If the scanner is still holding the ESC it
        // will consume 0x18 as that sequence's final byte and render one two-byte entry instead.
        bool sawCursorRightAlone = false;
        for (int i = 0; i < _emitted.Count; i++)
        {
            if (_emitted[i].Bytes.Length == 1 && _emitted[i].Bytes[0] == 0x18)
            {
                sawCursorRightAlone = true;
            }
        }

        Assert.True(sawCursorRightAlone,
            "the RIGHT keystroke was not shown as its own byte - the trace glued it to an escape "
            + "that arrived 18 seconds earlier:\n" + Rendered());
    }

    [Fact]
    public void TwoKEYSTROKESInQuickSuccessionAreNotWeldedIntoOneSequence()
    {
        // The timeout cannot catch this one: the second keystroke lands well inside a second, so
        // the stale-sequence rule never fires and the ESC is still pending when CAN arrives.
        //
        // On the TX side that is ALWAYS wrong, and the reason is structural rather than a matter
        // of timing. TerminalSession.SendBytesAsync is "THE single TX feed point", and it feeds
        // one whole send per call: a keypress maps to a COMPLETE byte string, so a real escape
        // sequence is always contained within one Feed. A sequence still pending when a Feed ends
        // is never going to be finished by the next one, whether that arrives in a millisecond or
        // an hour.
        var scanner = CreateScanner();

        scanner.Feed(new byte[] { 0x1B });
        _now = _now.AddMilliseconds(120);
        scanner.Feed(new byte[] { 0x18 });

        _output.WriteLine(Rendered());

        bool sawCursorRightAlone = false;
        for (int i = 0; i < _emitted.Count; i++)
        {
            if (_emitted[i].Bytes.Length == 1 && _emitted[i].Bytes[0] == 0x18)
            {
                sawCursorRightAlone = true;
            }
        }

        Assert.True(sawCursorRightAlone,
            "two separate keystrokes 120ms apart were welded into one sequence:\n" + Rendered());
    }

    [Fact]
    public void TheWaitingEscapeIsStillReportedRatherThanDropped()
    {
        // Abandoning must not mean discarding. The bytes were on the wire, and a trace that
        // quietly loses some cannot be used as evidence about the ones it kept.
        var scanner = CreateScanner();

        scanner.Feed(new byte[] { 0x1B });
        _now = _now.AddSeconds(18);
        scanner.Feed(new byte[] { 0x18 });

        bool sawTheEscape = false;
        for (int i = 0; i < _emitted.Count; i++)
        {
            if (_emitted[i].Bytes.Length == 1 && _emitted[i].Bytes[0] == 0x1B)
            {
                sawTheEscape = true;
            }
        }

        Assert.True(sawTheEscape,
            "the abandoned ESC vanished from the trace instead of being reported:\n" + Rendered());
    }
}
